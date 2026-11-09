using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// НАРЕЗАЕМЫЙ ПРОДУКТ. Вешается на КОРЕНЬ продукта, рядом с FoodItem.
///
/// Модель уже разрезана в блендере на отдельные меши-куски (в FBX это
/// Cube, Cube.001, Cube.002...). Отрезание — это скрыть очередной кусок на
/// целой картошке и заспавнить его отдельным предметом рядом.
///
/// ПОЧЕМУ ОТДЕЛЬНАЯ ОСЬ, А НЕ СОСТОЯНИЕ В ГРАФЕ ФОРМЫ:
///   резать можно в любом состоянии — грязную, чистую, чищеную. Если бы куски
///   были состояниями графа, пришлось бы завести все сочетания «чищеная минус
///   два куска» и так далее, и граф взорвался бы. Здесь грязь живёт своим
///   графом, а количество кусков — своим счётчиком, и они не мешают друг другу.
///
/// СЕТЬ: сколько отрезано — один байт. Какие куски прятать, каждый клиент
/// считает сам.
/// </summary>
public class SliceableFood : NetworkBehaviour, ICuttable
{
    [Header("Куски")]
    [Tooltip("Куски модели по порядку. ПУСТО = собрать из дочерних объектов " +
             "автоматически и отсортировать вдоль длинной оси.")]
    [SerializeField] private Transform[] _slices;

    [Tooltip("Свой префаб на КАЖДЫЙ кусок, В ТОМ ЖЕ ПОРЯДКЕ, что и Slices. " +
             "Заполняй это, если куски у тебя отдельными префабами.\n\n" +
             "КАЖДЫЙ из них обязан быть в Spawnable Prefabs у NetworkManager.")]
    [SerializeField] private GameObject[] _piecePrefabs;

    [Tooltip("Запасной префаб — берётся, если для куска не задан свой. " +
             "Хватает, когда все куски одинаковой формы или когда на префабе " +
             "стоит SlicePiece и меш выбирается по индексу.")]
    [SerializeField] private GameObject _piecePrefab;

    [Header("Отделение")]
    [Tooltip("С каким импульсом отваливается кусок")]
    [SerializeField] private float _pieceImpulse = 1.2f;

    [Tooltip("Разброс направления отскока, чтобы куски не падали одинаково")]
    [SerializeField] private float _pieceSpread = 0.35f;

    // ───────── сеть ─────────
    /// <summary>Сколько кусков уже отрезано.</summary>
    private readonly SyncVar<byte> _cut = new SyncVar<byte>(0);

    /// <summary>Продукт зажат пилой — не двигается, пока не отпилят.</summary>
    private readonly SyncVar<bool> _clamped = new SyncVar<bool>(false);

    private FoodItem _food;
    private Rigidbody _rb;
    private bool _wasKinematic;

    public int SliceCount => _slices != null ? _slices.Length : 0;
    public int SlicesLeft => SliceCount - _cut.Value;

    /// <summary>Последний кусок не режется — это уже не «нарезать», а «уничтожить».</summary>
    public bool CanCut => SlicesLeft > 1;

    public bool IsClamped => _clamped.Value;

    // ───────── ICuttable ─────────
    // Общий контракт с бревном: пила не должна знать, что именно режет.

    /// <summary>Ноль — брать выдержку с инструмента. У продуктов время реза
    /// одинаковое, разделять его по картошкам смысла нет; у брёвен наоборот.</summary>
    public float SecondsPerCut => 0f;

    /// <summary>Продукт держит рука игрока или он лежит в ёмкости — глушить
    /// физику незачем. Бревно в двести кило от касания пилой уезжает, поэтому
    /// решение оставлено за целью.</summary>
    public bool ClampWhileCutting => false;

    public string CutLabel => $"кусков осталось {SlicesLeft}";

    /// <summary>Цвет крошек берём у ингредиента. Прозрачный — пила подставит свой.</summary>
    public Color CrumbColor =>
        _food != null && _food.Ingredient != null ? _food.Ingredient.DebrisColor : default;

    private void Awake()
    {
        _food = GetComponent<FoodItem>();
        _rb = GetComponent<Rigidbody>();
        CollectSlices();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _cut.OnChange += OnCutChanged;
        _clamped.OnChange += OnClampChanged;
        RefreshVisual();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _cut.OnChange -= OnCutChanged;
        _clamped.OnChange -= OnClampChanged;
    }

    // ───────────────────────── КУСКИ ─────────────────────────

    /// <summary>
    /// Собирает куски из детей и сортирует ВДОЛЬ ДЛИННОЙ ОСИ, а не по именам.
    /// Порядок Cube, Cube.001, Cube.002 из блендера ничего не гарантирует —
    /// куски могли экспортироваться вразнобой, и тогда отваливались бы из
    /// середины.
    /// </summary>
    private void CollectSlices()
    {
        if (_slices != null && _slices.Length > 0)
        {
            Debug.Log($"[Куски] {name}: список задан вручную, кусков {_slices.Length}. " +
                      Diagnose(), this);
            return;
        }

        // Ищем ГЛУБОКО, а не только среди прямых детей. FBX почти всегда
        // импортируется с промежуточным узлом-обёрткой, и куски оказываются
        // внуками — при поиске по прямым детям список выходил бы пустым,
        // а тогда CanCut = false и картошка молча не режется.
        List<Transform> found = new List<Transform>();
        foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
            if (r.transform != transform) found.Add(r.transform);

        if (found.Count == 0)
        {
            Debug.LogError($"[SliceableFood] На {name} не найдено дочерних кусков " +
                           "с MeshRenderer. Назначь Slices вручную.", this);
            _slices = new Transform[0];
            return;
        }

        SortSlices(found, out string axisName);
        _slices = found.ToArray();

        Debug.Log($"[Куски] {name}: собрано автоматически {_slices.Length} " +
                  $"вдоль оси {axisName}. " + Diagnose(), this);
    }

    /// <summary>Разбор частых ошибок настройки одной строкой в логе.</summary>
    private string Diagnose()
    {
        int own = 0;
        System.Text.StringBuilder bad = new System.Text.StringBuilder();

        // Проверяем каждый префаб, который реально будет спавниться.
        for (int i = 0; i < SliceCount; i++)
        {
            GameObject p = PrefabFor(i);
            if (p == null) { bad.Append($" кусок {i}: префаба нет;"); continue; }

            if (_piecePrefabs != null && i < _piecePrefabs.Length && _piecePrefabs[i] != null)
                own++;

            if (p.GetComponent<SliceableFood>() != null)
                bad.Append($" кусок {i}: «{p.name}» — это ЦЕЛЫЙ продукт, на нём " +
                           "SliceableFood. Нужен префаб кусочка;");
            else if (p.GetComponent<FoodItem>() == null)
                bad.Append($" кусок {i}: на «{p.name}» нет FoodItem;");
        }

        if (bad.Length > 0) return "ОШИБКИ:" + bad;

        int need = SliceCount;
        string arr = _piecePrefabs == null ? "пуст" : $"{own} из {need} задано";
        return $"Piece Prefabs: {arr}" +
               (_piecePrefab != null ? $", запасной «{_piecePrefab.name}»" : ", запасного нет") +
               ". Порядок ok.";
    }

    /// <summary>
    /// Центр куска в системе координат продукта.
    ///
    /// Считается по ГРАНИЦАМ МЕША, а не по localPosition. Блендер сплошь и
    /// рядом экспортирует все объекты с общим центром в нуле — тогда у всех
    /// кусков localPosition одинаковая, сортировка вырождается в ничто,
    /// и порядок остаётся случайным. Отсюда и отрезание из середины.
    /// </summary>
    private Vector3 SliceCenter(Transform t)
    {
        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
            return transform.InverseTransformPoint(
                t.TransformPoint(mf.sharedMesh.bounds.center));

        Renderer r = t.GetComponent<Renderer>();
        if (r != null)
            return transform.InverseTransformPoint(r.bounds.center);

        return t.localPosition;
    }

    /// <summary>Ось, вдоль которой продукт нарезан — по ней куски и разбросаны.</summary>
    private Vector3 LongestAxis(List<Transform> parts, out string label)
    {
        Vector3 min = SliceCenter(parts[0]), max = min;
        foreach (Transform t in parts)
        {
            Vector3 c = SliceCenter(t);
            min = Vector3.Min(min, c);
            max = Vector3.Max(max, c);
        }

        Vector3 span = max - min;
        if (span.x >= span.y && span.x >= span.z) { label = "X"; return Vector3.right; }
        if (span.y >= span.z) { label = "Y"; return Vector3.up; }
        label = "Z";
        return Vector3.forward;
    }

    /// <summary>Разложить куски по порядку вдоль длинной оси.</summary>
    private void SortSlices(List<Transform> parts, out string label)
    {
        Vector3 axis = LongestAxis(parts, out label);
        parts.Sort((a, b) => Vector3.Dot(SliceCenter(a), axis)
                              .CompareTo(Vector3.Dot(SliceCenter(b), axis)));
    }

    private void OnCutChanged(byte prev, byte next, bool asServer)
    {
        // У ХОСТА OnChange приходит дважды: asServer = true, потом false.
        // Прятать куски дважды безвредно, но пусть будет один раз.
        if (asServer && base.IsClientInitialized) return;
        RefreshVisual();
    }

    /// <summary>Прячем отрезанные куски с ХВОСТА: режем всегда с одного конца.</summary>
    private void RefreshVisual()
    {
        if (_slices == null) return;

        int left = SlicesLeft;
        for (int i = 0; i < _slices.Length; i++)
            if (_slices[i] != null) _slices[i].gameObject.SetActive(i < left);
    }

    // ───────────────────────── ПИЛЕНИЕ ─────────────────────────

    /// <summary>
    /// Зажать продукт пилой. Пока зажат, он не двигается — иначе картошка
    /// уезжала бы из-под пилы от каждого движения.
    /// </summary>
    [Server]
    public void ServerSetClamped(bool value)
    {
        if (_clamped.Value == value) return;
        _clamped.Value = value;
        // Счёт при снятии зажима НЕ трогаем: отпустил на середине — вернулся
        // и допилил с того же места.
    }

    /// <summary>
    /// Замок — локальная реакция на SyncVar: физику глушим у всех, иначе
    /// у владельца картошка стоит, а у остальных продолжает катиться.
    /// </summary>
    private void OnClampChanged(bool prev, bool next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;
        if (_rb == null) return;

        if (next)
        {
            _wasKinematic = _rb.isKinematic;
            _rb.isKinematic = true;
        }
        else
        {
            _rb.isKinematic = _wasKinematic;
            _rb.WakeUp();
        }
    }

    /// <summary>Отрезать один кусок: спрятать на целой картошке и заспавнить
    /// отдельным предметом.</summary>
    [Server]
    public void ServerCutOne(ProcessDef cutProcess, float quality01)
    {
        if (!CanCut)
        {
            Debug.LogWarning($"[Куски/сервер] резать нельзя: всего {SliceCount}, " +
                             $"осталось {SlicesLeft}");
            return;
        }

        int index = SlicesLeft - 1;                // режем с хвоста
        Transform slice = _slices[index];
        Vector3 at = slice != null ? slice.position : transform.position;
        Quaternion rot = slice != null ? slice.rotation : transform.rotation;

        // Доля массы, которая уходит в кусок. Считается от ТЕКУЩЕЙ массы:
        // первый кусок из четырёх забирает четверть, следующий — треть
        // остатка, и так пропорции остаются верными до конца.
        int left = SlicesLeft;
        float share = left > 0 ? 1f / left : 0f;

        SpawnPiece(at, rot, share, index, cutProcess, quality01);

        // Запись в журнал самой картошки: для рецепта важно, что её резали.
        // Перехода состояния тут нет — нарезка идёт отдельной осью.
        if (_food != null && cutProcess != null)
            _food.ServerApplyProcess(cutProcess, quality01);

        // ПОСЛЕДНИЙ РАСПИЛ. Кусков было два, один только что отвалился —
        // и держать целый объект ради единственного оставшегося незачем.
        // Выпускаем и его отдельным предметом, а исходную картошку убираем.
        //
        // Иначе получалось некрасиво: один кусок — полноценный предмет,
        // а второй так и остаётся «недорезанной картошкой» с чужим коллайдером
        // и лишними компонентами.
        if (left == 2)
        {
            Transform rest = _slices[0];
            Vector3 at2 = rest != null ? rest.position : transform.position;
            Quaternion rot2 = rest != null ? rest.rotation : transform.rotation;

            // Доля 1.0 — вся оставшаяся масса уходит в этот кусок.
            SpawnPiece(at2, rot2, 1f, 0, cutProcess, quality01);

            Debug.Log($"[Куски] распилен последний надвое, «{name}» убираю");

            if (_food != null) _food.ServerMarkConsumed();

            // Владение снимаем ДО деспавна: клиент-владелец не должен слать
            // позицию уже мёртвого объекта.
            NetworkObject.RemoveOwnership();
            base.Despawn(NetworkObject);
            return;
        }

        _cut.Value = (byte)(_cut.Value + 1);
    }

    [Server]
    private void SpawnPiece(Vector3 at, Quaternion rot, float share, int sliceIndex,
                            ProcessDef cutProcess, float quality01)
    {
        GameObject prefab = PrefabFor(sliceIndex);
        if (prefab == null)
        {
            Debug.LogError($"[Куски] для куска {sliceIndex} нет префаба: ни в " +
                           "Piece Prefabs, ни в запасном Piece Prefab.", this);
            return;
        }

        Debug.Log($"[Куски] отрезан кусок {sliceIndex}, спавню «{prefab.name}»");
        GameObject go = Instantiate(prefab, at, rot);
        FoodItem piece = go.GetComponent<FoodItem>();

        if (piece == null)
        {
            Debug.LogError("[SliceableFood] На Piece Prefab нет FoodItem", _piecePrefab);
            Destroy(go);
            return;
        }

        // Данные ставим ДО Spawn: в FoodItem.OnStartServer стоит проверка на
        // null, и она не затрёт принесённый журнал.
        if (_food != null && _food.Portion != null)
        {
            FoodPortion part = _food.Portion.Clone();
            part.ScaleMass(share);
            if (cutProcess != null) part.ApplyProcess(cutProcess, quality01);
            piece.ServerSetPortion(part);

            // У самой картошки массы становится меньше ровно на отданную долю.
            _food.Portion.ScaleMass(1f - share);
        }

        // Кусок наследует ВИД целого продукта: отпиленный от чищеной картошки
        // обязан выглядеть чищеным, а не начинать с грязного.
        if (_food != null) piece.ServerSetInitialState(_food.StateIndex);

        // И свою форму: куски у продукта разной геометрии, а префаб один.
        SlicePiece sp = go.GetComponent<SlicePiece>();
        if (sp != null) sp.ServerSetIndex((byte)sliceIndex);

        base.Spawn(go);

        // Кусок не должен толкать ни саму картошку, ни того, кто её пилит.
        // Иначе отвалившийся кусок бьёт по пиле и по игроку, всё смещается,
        // и захват срывается ровно в момент отреза — а следующий кусок уже
        // не пилится.
        Collider mine = GetComponentInChildren<Collider>();
        Collider pieceCol = go.GetComponentInChildren<Collider>();
        if (mine != null && pieceCol != null)
            Physics.IgnoreCollision(pieceCol, mine, true);

        // Толчок — уже после спавна, иначе Rigidbody ещё не инициализирован.
        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            Vector3 dir = (Vector3.up * 0.4f + Random.insideUnitSphere * _pieceSpread).normalized;
            rb.AddForce(dir * _pieceImpulse, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * _pieceImpulse, ForceMode.Impulse);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// КНОПКА В ИНСПЕКТОРЕ (шестерёнка компонента).
    ///
    /// Собирает куски, раскладывает их в правильном порядке вдоль длинной оси
    /// и тем же порядком записывает их меши в префаб кусочка. Руками этот
    /// порядок перепутать очень легко: имена вида Cube.001, Cube.003 ничего
    /// не говорят о том, где кусок находится физически.
    /// </summary>
    [ContextMenu("Собрать куски и заполнить префаб кусочка")]
    private void EditorRebuild()
    {
        List<Transform> found = new List<Transform>();
        foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
            if (r.transform != transform) found.Add(r.transform);

        if (found.Count == 0)
        {
            Debug.LogError("[Куски] не найдено ни одного дочернего меша", this);
            return;
        }

        SortSlices(found, out string axisName);
        _slices = found.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);

        Vector3 ax = axisName == "X" ? Vector3.right
                   : axisName == "Y" ? Vector3.up : Vector3.forward;

        // Показываем не только порядок, но и КООРДИНАТУ каждого куска вдоль оси.
        // Если у всех она одинаковая — модель экспортирована с общим центром,
        // и разложить куски по геометрии невозможно: надо перевыгрузить из
        // блендера так, чтобы у каждого куска был свой центр.
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append($"[Куски] порядок вдоль оси {axisName}:");
        for (int i = 0; i < _slices.Length; i++)
            sb.Append($"\n   {i}: «{_slices[i].name}»  координата " +
                      $"{Vector3.Dot(SliceCenter(_slices[i]), ax):0.0000}");
        Debug.Log(sb.ToString(), this);

        // Подгоняем массив префабов под число кусков, сохраняя уже заданное.
        if (_piecePrefabs == null || _piecePrefabs.Length != _slices.Length)
        {
            GameObject[] resized = new GameObject[_slices.Length];
            if (_piecePrefabs != null)
                for (int i = 0; i < Mathf.Min(_piecePrefabs.Length, resized.Length); i++)
                    resized[i] = _piecePrefabs[i];
            _piecePrefabs = resized;
            Debug.Log($"[Куски] массив Piece Prefabs приведён к длине {_slices.Length} — " +
                      "разложи туда префабы кусков в этом же порядке", this);
        }

        for (int i = 0; i < _slices.Length; i++)
        {
            GameObject pf = PrefabFor(i);
            Debug.Log($"[Куски]   {i}: «{_slices[i].name}» -> " +
                      $"{(pf != null ? "«" + pf.name + "»" : "ПРЕФАБ НЕ ЗАДАН")}", this);
        }

        if (_piecePrefab == null)
        {
            Debug.Log("[Куски] запасной Piece Prefab не задан — это нормально, " +
                      "если у каждого куска свой префаб", this);
            return;
        }

        SlicePiece sp = _piecePrefab.GetComponent<SlicePiece>();
        if (sp == null)
        {
            Debug.Log($"[Куски] на запасном «{_piecePrefab.name}» нет SlicePiece — " +
                      "это нормально, если формы кусков заданы отдельными префабами", this);
            return;
        }

        Mesh[] meshes = new Mesh[_slices.Length];
        for (int i = 0; i < _slices.Length; i++)
        {
            MeshFilter mf = _slices[i].GetComponent<MeshFilter>();
            meshes[i] = mf != null ? mf.sharedMesh : null;
        }

        sp.EditorSetMeshes(meshes);
        UnityEditor.EditorUtility.SetDirty(sp);
        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"[Куски] в «{_piecePrefab.name}» записано мешей: {meshes.Length}", this);
    }
#endif

    /// <summary>
    /// Какой префаб отвечает за этот кусок. Сперва свой из массива, потом
    /// запасной. Индекс тот же, что и в Slices, поэтому массивы обязаны
    /// совпадать по порядку.
    /// </summary>
    private GameObject PrefabFor(int sliceIndex)
    {
        if (_piecePrefabs != null && sliceIndex >= 0 && sliceIndex < _piecePrefabs.Length
            && _piecePrefabs[sliceIndex] != null)
            return _piecePrefabs[sliceIndex];

        return _piecePrefab;
    }

    /// <summary>Где сейчас идёт пропил — туда подаётся полотно и летят крошки.</summary>
    public Vector3 CutPoint()
    {
        if (_slices == null || SlicesLeft <= 0) return transform.position;

        Transform last = _slices[SlicesLeft - 1];
        return last != null ? last.position : transform.position;
    }
}
