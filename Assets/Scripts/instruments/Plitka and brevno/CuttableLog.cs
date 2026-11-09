using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>Уровень распила. Порядок значений важен: следующий = предыдущий + 1,
/// по этому и проверяется правильность цепочки префабов.</summary>
public enum LogTier : byte
{
    /// <summary>Целое бревно. Пилится поперёк на палена.</summary>
    Log = 0,

    /// <summary>Палено — кругляк в длину полметра. Колется на четвертинки.</summary>
    Billet = 1,

    /// <summary>Четвертинка. Дальше не пилится.</summary>
    Quarter = 2
}

/// <summary>
/// РАСПИЛ БРЕВНА. Вешается на корень бревна и на корень палена — это один и тот
/// же скрипт на двух уровнях, разница только в поле Tier и в том, что лежит в
/// Piece Prefab.
///
/// ═══ ПОЧЕМУ УРОВНИ — ЭТО ЦЕПОЧКА ПРЕФАБОВ, А НЕ СОСТОЯНИЯ ═══
///
///   Бревно   → пилится на 5 палён   → Piece Prefab = Billet
///   Палено   → колется на 4 части   → Piece Prefab = Quarter
///   Четвертинка — обычный предмет, этого скрипта на ней нет.
///
/// Переход уровня — это ПОДМЕНА ОБЪЕКТА, а не смена флага внутри него. Так
/// выходит потому, что у бревна и у палена разная геометрия, разный коллайдер,
/// разная масса и разное число частей. Держать это одним объектом с полем
/// «я сейчас бревно / я сейчас палено» значит хранить в нём три набора мешей
/// и переключать их — и первый же новый уровень заставит переписывать всё.
///
/// ЧЕТВЕРТИНОК СРАЗУ НЕ БЫВАЕТ, и это проверяется, а не подразумевается. Поле
/// Tier у куска обязано быть ровно на единицу больше, чем у целого. Положил в
/// бревно префаб четвертинки — получишь ошибку в консоли при первом же распиле,
/// а не молча пропущенный уровень. Ошибка эта на удивление частая: префабы
/// кусков похожи между собой, и перепутать их в инспекторе легко.
///
/// ═══ ЧЕМ ОТЛИЧАЕТСЯ ОТ SliceableFood ═══
///
/// Механика отпиливания та же — спрятать часть с хвоста и заспавнить её
/// отдельным предметом. Но SliceableFood намертво завязан на FoodItem: он
/// делит FoodPortion, тянет StateIndex, зовёт ServerMarkConsumed и отказывается
/// спавнить кусок без FoodItem. Бревно едой не является. Здесь FoodItem
/// НЕОБЯЗАТЕЛЕН: есть — журнал и масса поделятся как у продукта; нет — делится
/// только вес в килограммах.
///
/// ═══ СЕТЬ ═══
/// Сколько частей отпилено — один байт. Какие прятать, каждый клиент считает
/// сам. Масса — float, понадобится костру: сколько дров, столько и жара.
///
/// ═══ ПОРЯДОК ЧАСТЕЙ ═══
/// Части сортируются ВДОЛЬ ДЛИННОЙ ОСИ по геометрии, а не по именам. Имена
/// вида timber 1.003 ничего не говорят о том, где кусок физически, а пилить
/// надо с торца, а не из середины.
/// </summary>
public class CuttableLog : NetworkBehaviour, ICuttable
{
    [Header("Уровень")]
    [Tooltip("Что это. Бревно пилится на палена, палено — на четвертинки.\n\n" +
             "У Piece Prefab уровень обязан быть РОВНО НА ЕДИНИЦУ больше. " +
             "Иначе распил не начнётся и в консоли будет ошибка.")]
    [SerializeField] private LogTier _tier = LogTier.Log;

    [Header("Части")]
    [Tooltip("Части текущего уровня по порядку от торца.\n\n" +
             "У БРЕВНА это ГРУППЫ-палена, а не отдельные меши четвертинок. " +
             "Собрать группы из плоского списка timber 1.001, timber 1.002… — " +
             "кнопкой «Разложить на палена по именам» в шестерёнке компонента.")]
    [SerializeField] private Transform[] _parts;

    [Tooltip("Свой префаб на КАЖДУЮ часть, в том же порядке. Нужен, только " +
             "если части разной формы и у каждой свой префаб.")]
    [SerializeField] private GameObject[] _piecePrefabs;

    [Tooltip("Общий префаб части — берётся, когда своего нет. Для бревна это " +
             "префаб ПАЛЕНА, для палена — префаб ЧЕТВЕРТИНКИ.\n\n" +
             "Обязан быть в Spawnable Prefabs у NetworkManager.")]
    [SerializeField] private GameObject _piecePrefab;

    [Header("Распил")]
    [Tooltip("Секунд удержания пилы до отделения одной части. Ноль — взять " +
             "общее время с инструмента.\n\n" +
             "Бревно ~2.5, палено ~1.2: перепилить кругляк поперёк заметно " +
             "дольше, чем расколоть его вдоль.")]
    [Min(0f)][SerializeField] private float _secondsPerCut = 2.5f;

    [Tooltip("Глушить физику, пока пилят. Бревно от касания пилой уезжает, " +
             "и без этого догнать его невозможно.")]
    [SerializeField] private bool _clampWhileCutting = true;

    [Tooltip("С каким импульсом отваливается часть")]
    [SerializeField] private float _pieceImpulse = 1.5f;

    [Tooltip("Разброс отскока, чтобы части не падали одинаково")]
    [SerializeField] private float _pieceSpread = 0.3f;

    [Header("Масса")]
    [Tooltip("Вес целого объекта, кг. Делится между частями пропорционально. " +
             "Понадобится костру: сколько дров, столько и жара.")]
    [SerializeField] private float _startMassKg = 200f;

    [Header("Щепа")]
    [Tooltip("Цвет крошек при распиле. Прозрачный — пила возьмёт свой.")]
    [SerializeField] private Color _crumbColor = new Color(0.62f, 0.44f, 0.26f, 1f);

    // ───────── сеть ─────────
    /// <summary>Сколько частей уже отпилено.</summary>
    private readonly SyncVar<byte> _cut = new SyncVar<byte>(0);

    /// <summary>Зажат пилой — не двигается, пока не отпилят.</summary>
    private readonly SyncVar<bool> _clamped = new SyncVar<bool>(false);

    /// <summary>Текущая масса, кг.</summary>
    private readonly SyncVar<float> _massKg = new SyncVar<float>(0f);

    // ───────── локальное ─────────
    private FoodItem _food;          // может не быть — бревно не еда
    private Rigidbody _rb;
    private bool _wasKinematic;

    public LogTier Tier => _tier;
    public int PartCount => _parts != null ? _parts.Length : 0;
    public int PartsLeft => PartCount - _cut.Value;
    public float MassKg => _massKg.Value;

    // ───────────────────────── ICuttable ─────────────────────────

    /// <summary>Последняя часть не пилится: это уже не «распилить», а
    /// «уничтожить». Вместо этого предпоследний распил выпускает обе части
    /// сразу и убирает исходный объект.</summary>
    public bool CanCut => PartsLeft > 1;

    public float SecondsPerCut => _secondsPerCut;
    public bool ClampWhileCutting => _clampWhileCutting;
    public Color CrumbColor => _crumbColor;

    public string CutLabel => _tier switch
    {
        LogTier.Log => $"палён осталось {PartsLeft} из {PartCount}",
        LogTier.Billet => $"четвертинок осталось {PartsLeft} из {PartCount}",
        _ => $"частей {PartsLeft}"
    };

    /// <summary>Пропил идёт по ТОРЦУ последней части, а не по её центру: щепа
    /// должна лететь из места реза, а не из середины полена.</summary>
    public Vector3 CutPoint()
    {
        int left = PartsLeft;
        if (_parts == null || left <= 0) return transform.position;

        Transform last = _parts[left - 1];
        if (last == null) return transform.position;
        if (left < 2 || _parts[left - 2] == null) return last.position;

        // Граница между последней частью и предыдущей.
        return Vector3.Lerp(last.position, _parts[left - 2].position, 0.5f);
    }

    // ───────────────────────── ЖИЗНЕННЫЙ ЦИКЛ ─────────────────────────

    private void Awake()
    {
        _food = GetComponent<FoodItem>();
        _rb = GetComponent<Rigidbody>();
        CollectParts();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Массу ставим, только если её ещё не принесли извне: у части,
        // отпиленной от бревна, вес уже посчитан, и стартовое значение
        // префаба затёрло бы его.
        if (_massKg.Value <= 0f) _massKg.Value = _startMassKg;

        ValidatePieceChain();
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

    /// <summary>Задать массу до Spawn — так делает распил уровнем выше.</summary>
    [Server]
    public void ServerSetMass(float kg) => _massKg.Value = Mathf.Max(0f, kg);

    // ───────────────────────── ПРОВЕРКА ЦЕПОЧКИ ─────────────────────────

    /// <summary>
    /// Уровень куска обязан быть ровно на единицу выше. Проверяем один раз при
    /// спавне, а не при каждом распиле: ошибка эта настроечная, и увидеть её
    /// надо сразу, а не через минуту игры.
    /// </summary>
    private void ValidatePieceChain()
    {
        if (_tier == LogTier.Quarter)
        {
            if (_piecePrefab != null || (_piecePrefabs != null && _piecePrefabs.Length > 0))
                Debug.LogWarning($"[Бревно] «{name}»: уровень Quarter, но задан " +
                                 "префаб части. Четвертинка дальше не пилится — " +
                                 "поле лишнее.", this);
            return;
        }

        for (int i = 0; i < PartCount; i++)
        {
            GameObject p = PrefabFor(i);
            if (p == null)
            {
                Debug.LogError($"[Бревно] «{name}»: для части {i} нет префаба " +
                               "ни в Piece Prefabs, ни в запасном.", this);
                return;
            }

            CuttableLog child = p.GetComponent<CuttableLog>();
            LogTier expected = (LogTier)((byte)_tier + 1);

            if (child == null)
            {
                // Отсутствие скрипта законно ТОЛЬКО у последнего уровня.
                if (expected != LogTier.Quarter)
                    Debug.LogError($"[Бревно] «{name}» (уровень {_tier}): на префабе " +
                                   $"«{p.name}» нет CuttableLog, а должен быть " +
                                   $"уровень {expected}. Сейчас распил перескочит " +
                                   "через уровень.", this);
                continue;
            }

            if (child.Tier != expected)
                Debug.LogError($"[Бревно] «{name}» (уровень {_tier}): у префаба части " +
                               $"«{p.name}» уровень {child.Tier}, а нужен {expected}. " +
                               "Из бревна должны получаться палена, а не сразу " +
                               "четвертинки.", this);

            if (child == this || p.GetComponent<CuttableLog>().Tier == _tier)
                Debug.LogError($"[Бревно] «{name}»: префаб части того же уровня — " +
                               "распил зациклится и бревно будет бесконечным.", this);
        }
    }

    // ───────────────────────── ЧАСТИ ─────────────────────────

    /// <summary>
    /// Собрать части, если список пуст. Берём ПРЯМЫХ детей с мешем — в отличие
    /// от SliceableFood, который ищет вглубь. Для бревна глубокий поиск как раз
    /// вреден: он нашёл бы двадцать четвертинок вместо пяти палён и порезал бы
    /// бревно сразу до мелочи.
    /// </summary>
    private void CollectParts()
    {
        if (_parts != null && _parts.Length > 0) return;

        List<Transform> found = new List<Transform>();
        foreach (Transform child in transform)
        {
            if (child.GetComponentInChildren<MeshRenderer>(true) == null) continue;
            found.Add(child);
        }

        if (found.Count == 0)
        {
            Debug.LogError($"[Бревно] «{name}»: не найдено ни одной части. " +
                           "Заполни Parts вручную или нажми «Разложить на палена " +
                           "по именам» в шестерёнке компонента.", this);
            _parts = new Transform[0];
            return;
        }

        SortAlongLongestAxis(found, out string axis);
        _parts = found.ToArray();

        Debug.Log($"[Бревно] «{name}» ({_tier}): частей {_parts.Length}, " +
                  $"порядок вдоль оси {axis}", this);
    }

    /// <summary>Центр части по границам мешей, а не по localPosition: блендер
    /// сплошь и рядом отдаёт всё с общим центром в нуле, и тогда сортировка
    /// вырождается в ничто, а пилить начинает из середины.</summary>
    private Vector3 PartCenter(Transform t)
    {
        Bounds b = default;
        bool has = false;

        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }

        return has ? transform.InverseTransformPoint(b.center) : t.localPosition;
    }

    private void SortAlongLongestAxis(List<Transform> parts, out string label)
    {
        Vector3 min = PartCenter(parts[0]), max = min;
        foreach (Transform t in parts)
        {
            Vector3 c = PartCenter(t);
            min = Vector3.Min(min, c);
            max = Vector3.Max(max, c);
        }

        Vector3 span = max - min;
        Vector3 axis;
        if (span.x >= span.y && span.x >= span.z) { label = "X"; axis = Vector3.right; }
        else if (span.y >= span.z) { label = "Y"; axis = Vector3.up; }
        else { label = "Z"; axis = Vector3.forward; }

        parts.Sort((a, b) => Vector3.Dot(PartCenter(a), axis)
                              .CompareTo(Vector3.Dot(PartCenter(b), axis)));
    }

    private void OnCutChanged(byte prev, byte next, bool asServer)
    {
        // У ХОСТА OnChange приходит дважды: asServer = true, потом false.
        if (asServer && base.IsClientInitialized) return;
        RefreshVisual();
    }

    /// <summary>Прячем отпиленное с ХВОСТА: пилят всегда с одного торца.</summary>
    private void RefreshVisual()
    {
        if (_parts == null) return;

        int left = PartsLeft;
        for (int i = 0; i < _parts.Length; i++)
            if (_parts[i] != null) _parts[i].gameObject.SetActive(i < left);
    }

    // ───────────────────────── ЗАЖИМ ─────────────────────────

    [Server]
    public void ServerSetClamped(bool value)
    {
        if (_clamped.Value == value) return;
        _clamped.Value = value;
        // Счётчик распила НЕ трогаем: отпустил на середине — вернулся и допилил.
    }

    /// <summary>Физику глушим у ВСЕХ, а не только у сервера: иначе у одного
    /// бревно стоит, а у остальных продолжает катиться.</summary>
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

    // ───────────────────────── РАСПИЛ ─────────────────────────

    [Server]
    public void ServerCutOne(ProcessDef cutProcess, float quality01)
    {
        if (!CanCut)
        {
            Debug.LogWarning($"[Бревно/сервер] «{name}»: пилить нечего, " +
                             $"частей {PartCount}, осталось {PartsLeft}");
            return;
        }

        int left = PartsLeft;
        int index = left - 1;                     // пилим с хвоста
        Transform part = _parts[index];

        Vector3 at = part != null ? part.position : transform.position;
        Quaternion rot = part != null ? part.rotation : transform.rotation;

        // Доля от ТЕКУЩЕЙ массы: первая часть из пяти забирает пятую, следующая —
        // четверть остатка, и пропорции остаются верными до конца.
        float share = 1f / left;

        SpawnPiece(at, rot, share, index, cutProcess, quality01);

        // Журнал ведём, только если объект вообще пищевой.
        if (_food != null && cutProcess != null)
            _food.ServerApplyProcess(cutProcess, quality01);

        // ПОСЛЕДНИЙ РАСПИЛ. Частей было две, одна отвалилась — держать целый
        // объект ради единственной оставшейся незачем: получилось бы, что одна
        // часть полноценный предмет, а вторая так и осталась «недопиленным
        // бревном» с чужим коллайдером и лишними компонентами.
        if (left == 2)
        {
            Transform rest = _parts[0];
            Vector3 at2 = rest != null ? rest.position : transform.position;
            Quaternion rot2 = rest != null ? rest.rotation : transform.rotation;

            SpawnPiece(at2, rot2, 1f, 0, cutProcess, quality01);

            Debug.Log($"[Бревно] «{name}» ({_tier}) распилен до конца, убираю");

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
    private void SpawnPiece(Vector3 at, Quaternion rot, float share, int partIndex,
                            ProcessDef cutProcess, float quality01)
    {
        GameObject prefab = PrefabFor(partIndex);
        if (prefab == null)
        {
            Debug.LogError($"[Бревно] «{name}»: для части {partIndex} нет префаба.", this);
            return;
        }

        GameObject go = Instantiate(prefab, at, rot);

        // ── масса ──
        // Ставим ДО Spawn: в OnStartServer у части стоит проверка «масса ещё не
        // задана», и стартовое значение её префаба не затрёт принесённое.
        float taken = _massKg.Value * share;

        CuttableLog childLog = go.GetComponent<CuttableLog>();
        if (childLog != null) childLog.ServerSetMass(taken);

        // ── журнал и вид, если это всё-таки продукт ──
        FoodItem piece = go.GetComponent<FoodItem>();
        if (piece != null && _food != null && _food.Portion != null)
        {
            FoodPortion part = _food.Portion.Clone();
            part.ScaleMass(share);
            if (cutProcess != null) part.ApplyProcess(cutProcess, quality01);
            piece.ServerSetPortion(part);
            piece.ServerSetInitialState(_food.StateIndex);

            _food.Portion.ScaleMass(1f - share);
        }

        _massKg.Value -= taken;

        base.Spawn(go);

        // Отвалившаяся часть не должна толкать ни исходное бревно, ни пилу:
        // иначе она бьёт по инструменту в момент отреза, всё смещается, и
        // следующая часть уже не пилится.
        Collider mine = GetComponentInChildren<Collider>();
        Collider pieceCol = go.GetComponentInChildren<Collider>();
        if (mine != null && pieceCol != null)
            Physics.IgnoreCollision(pieceCol, mine, true);

        // Толчок — уже после Spawn, иначе Rigidbody ещё не готов.
        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            Vector3 dir = (Vector3.up * 0.4f + Random.insideUnitSphere * _pieceSpread).normalized;
            rb.AddForce(dir * _pieceImpulse, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * _pieceImpulse, ForceMode.Impulse);
        }

        Debug.Log($"[Бревно] «{name}» ({_tier}) → отделена часть {partIndex} " +
                  $"«{prefab.name}», масса {taken:0.0} кг");
    }

    private GameObject PrefabFor(int partIndex)
    {
        if (_piecePrefabs != null && partIndex >= 0 && partIndex < _piecePrefabs.Length
            && _piecePrefabs[partIndex] != null)
            return _piecePrefabs[partIndex];

        return _piecePrefab;
    }

#if UNITY_EDITOR

    // ───────────────────────── ИНСТРУМЕНТЫ РЕДАКТОРА ─────────────────────────

    /// <summary>
    /// КНОПКА В ШЕСТЕРЁНКЕ КОМПОНЕНТА.
    ///
    /// Из плоского списка timber 1.001, timber 1.002 … timber 5.004 делает пять
    /// групп-палён, каждая со своими четырьмя четвертинками внутри. Ключ
    /// группировки — часть имени ДО последней точки.
    ///
    /// Зачем это вообще нужно: блендер отдаёт все двадцать мешей прямыми детьми
    /// бревна, без промежуточных узлов. Пилить бревно по этому списку значит
    /// отваливать по одной четвертинке — то есть перескочить уровень палена,
    /// ровно то, чего мы не хотим. Группы и есть недостающий уровень.
    ///
    /// Центр каждой группы ставится в середину её мешей — иначе палено
    /// отваливалось бы, вращаясь вокруг нуля бревна, и улетало бы в сторону.
    /// </summary>
    [ContextMenu("Разложить на палена по именам")]
    private void EditorGroupByName()
    {
        // Берём только ПРЯМЫХ детей с мешем: если уже сгруппировано, повторный
        // запуск ничего не испортит — группы мешей на себе не несут.
        List<Transform> loose = new List<Transform>();
        foreach (Transform child in transform)
            if (child.GetComponent<MeshRenderer>() != null) loose.Add(child);

        if (loose.Count == 0)
        {
            Debug.LogWarning($"[Бревно] «{name}»: прямых детей с мешем нет — " +
                             "похоже, уже сгруппировано. Нажми «Пересобрать " +
                             "порядок частей».", this);
            return;
        }

        Dictionary<string, List<Transform>> groups = new Dictionary<string, List<Transform>>();
        foreach (Transform t in loose)
        {
            int dot = t.name.LastIndexOf('.');
            string key = dot > 0 ? t.name.Substring(0, dot) : t.name;

            if (!groups.TryGetValue(key, out List<Transform> list))
            {
                list = new List<Transform>();
                groups[key] = list;
            }
            list.Add(t);
        }

        if (groups.Count < 2)
        {
            Debug.LogError($"[Бревно] «{name}»: по именам получилась одна группа " +
                           $"«{new List<string>(groups.Keys)[0]}». Имена должны быть " +
                           "вида «timber 1.001», где до точки — номер палена.", this);
            return;
        }

        List<Transform> created = new List<Transform>();

        foreach (KeyValuePair<string, List<Transform>> g in groups)
        {
            GameObject holder = new GameObject(g.Key);
            UnityEditor.Undo.RegisterCreatedObjectUndo(holder, "Группировка палён");

            holder.transform.SetParent(transform, false);

            // Центр группы — середина её мешей в мире.
            Bounds b = default;
            bool has = false;
            foreach (Transform t in g.Value)
            {
                Renderer r = t.GetComponent<Renderer>();
                if (r == null) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            holder.transform.position = has ? b.center : transform.position;
            holder.transform.rotation = transform.rotation;

            foreach (Transform t in g.Value)
                UnityEditor.Undo.SetTransformParent(t, holder.transform, "Группировка палён");

            created.Add(holder.transform);
        }

        SortAlongLongestAxis(created, out string axis);
        _parts = created.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append($"[Бревно] «{name}»: собрано {_parts.Length} палён вдоль оси {axis}:");
        for (int i = 0; i < _parts.Length; i++)
            sb.Append($"\n   {i}: «{_parts[i].name}» — четвертинок " +
                      $"{_parts[i].childCount}");
        Debug.Log(sb.ToString(), this);

        WarnUnevenGroups();
    }

    /// <summary>Пересобрать порядок, ничего не перемещая. Нужно палену, у
    /// которого четвертинки и так лежат прямыми детьми.</summary>
    [ContextMenu("Пересобрать порядок частей")]
    private void EditorRebuildOrder()
    {
        List<Transform> found = new List<Transform>();
        foreach (Transform child in transform)
            if (child.GetComponentInChildren<MeshRenderer>(true) != null) found.Add(child);

        if (found.Count == 0)
        {
            Debug.LogError($"[Бревно] «{name}»: детей с мешем нет", this);
            return;
        }

        SortAlongLongestAxis(found, out string axis);
        _parts = found.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append($"[Бревно] «{name}» ({_tier}): частей {_parts.Length} вдоль оси {axis}:");
        for (int i = 0; i < _parts.Length; i++)
            sb.Append($"\n   {i}: «{_parts[i].name}»");
        Debug.Log(sb.ToString(), this);

        WarnUnevenGroups();
    }

    /// <summary>Разное число четвертинок в паленах почти всегда значит опечатку
    /// в имени меша — например, timber 3.05 вместо timber 3.005.</summary>
    private void WarnUnevenGroups()
    {
        if (_parts == null || _parts.Length == 0) return;

        int first = _parts[0].childCount;
        foreach (Transform t in _parts)
        {
            if (t.childCount == first) continue;

            Debug.LogWarning($"[Бревно] «{name}»: в палёнах разное число " +
                             $"четвертинок (у «{_parts[0].name}» — {first}, " +
                             $"у «{t.name}» — {t.childCount}). Обычно это " +
                             "опечатка в имени меша.", this);
            return;
        }
    }
#endif
}
