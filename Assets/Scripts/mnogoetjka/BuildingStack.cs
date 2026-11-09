using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// БАШНЯ ИЗ ЭТАЖЕЙ. Растёт вверх по одной линии.
///
/// ═══ УСТРОЙСТВО ═══
///
///   ┌─────────────┐  ← активные этажи, ВСЕГДА сверху
///   │  активный 2 │     на них идёт работа
///   ├─────────────┤
///   │  активный 1 │
///   ├─────────────┤
///   │   холостой  │  ← стеклянные пустышки, только вид
///   ├─────────────┤     их число и есть уровень
///   │   холостой  │
///   ├─────────────┤
///   │ первый этаж │  ← подъезд, всегда один и внизу
///   └─────────────┘
///
/// Поднять уровень значит вставить ещё один холостой этаж МЕЖДУ первым и
/// активными. Активные при этом не пересоздаются, а переезжают выше — это
/// важно, потому что на них лежит всё, что игроки успели там сделать.
///
/// ═══ ПОЧЕМУ ПИВОТЫ БОЛЬШЕ НЕ ВАЖНЫ ═══
///
/// Обычно этажи ставят по координатам пивотов — и тогда каждый префаб обязан
/// иметь пивот в одном и том же месте. Обязанность эта ничем не подкреплена:
/// пивот уезжает при любой пересборке модели, и узнают об этом по щели в
/// стыке через неделю.
///
/// Здесь этажи ставятся ПО ГАБАРИТАМ. Каждый этаж измеряется при создании, и
/// его НИЗ кладётся ровно на верх предыдущего. Где при этом оказался пивот,
/// системе безразлично: он может быть в центре, в углу или в полусотне метров
/// в стороне — стык всё равно сойдётся.
///
/// Горизонталь выравнивается так же, по центру габаритов. Поэтому этажи стоят
/// на одной линии по построению, а не потому, что кто-то аккуратно вбил
/// одинаковые X и Z.
///
/// ═══ ПРО СЕТЬ ═══
///
/// Вся башня — чистая функция ОДНОГО ЧИСЛА, уровня. Значит и синхронизировать
/// надо одно число: получил новый уровень — позвал SetLevel, и стопка сошлась
/// сама, одинаково у всех. Списки этажей, их позиции и порядок по сети гонять
/// не нужно вообще.
/// </summary>
[AddComponentMenu("CatCooks/Стройка/Building Stack")]
public class BuildingStack : MonoBehaviour
{
    [System.Serializable]
    public class Floor
    {
        public GameObject prefab;

        [Tooltip("ВЫСОТА ВРУЧНУЮ, метры. Ноль — мерить по мешам.\n\n" +
                 "Нужна, когда сверху торчит что-то, чего в высоте этажа быть " +
                 "не должно: антенна, кран, леса. По мешам такой этаж " +
                 "померится вместе с торчащим, и следующий встанет с зазором.")]
        [Min(0f)] public float heightOverride = 0f;
    }

    // ═════════════════════════ ЭТАЖИ ═════════════════════════

    [Header("━━━ Этажи ━━━")]
    [Tooltip("ПЕРВЫЙ ЭТАЖ. Подъезд, вход, всё уникальное. Всегда один и " +
             "всегда внизу.")]
    [SerializeField] private Floor _ground = new Floor();

    [Tooltip("ХОЛОСТОЙ ЭТАЖ. Стеклянная пустышка, только вид. Именно их число " +
             "и растёт при поднятии уровня.")]
    [SerializeField] private Floor _filler = new Floor();

    [Tooltip("АКТИВНЫЕ ЭТАЖИ. Всегда сверху, в этом же порядке снизу вверх.\n\n" +
             "Сейчас их два. Добавишь третий — просто впиши сюда, остальное " +
             "подстроится: система нигде не рассчитывает на их количество.")]
    [SerializeField] private Floor[] _actives = new Floor[0];

    // ═════════════════════════ УРОВЕНЬ ═════════════════════════

    [Header("━━━ Уровень ━━━")]
    [Tooltip("Сколько холостых этажей стоит между первым и активными")]
    [Min(0)][SerializeField] private int _level = 0;

    [Tooltip("Потолок. Ноль — без потолка.")]
    [Min(0)][SerializeField] private int _maxLevel = 0;

    [Tooltip("Собрать башню при старте. Выключи, если стопку собирает " +
             "что-то другое — сохранение, сервер, режиссёр уровня.")]
    [SerializeField] private bool _buildOnAwake = true;

    // ═════════════════════════ СТЫКОВКА ═════════════════════════

    [Header("━━━ Стыковка ━━━")]
    [Tooltip("ЗАЗОР МЕЖДУ ЭТАЖАМИ, метры. Отрицательный — нахлёст.\n\n" +
             "Нужен, когда у этажа есть выступающий пол или карниз: по " +
             "габаритам он померится как часть этажа, и следующий встанет " +
             "на карниз, а не на перекрытие. Тогда сюда небольшой минус.")]
    [Range(-2f, 2f)][SerializeField] private float _seam = 0f;

    [Tooltip("ВЫРАВНИВАТЬ ПО ГОРИЗОНТАЛИ ПО ЦЕНТРУ ГАБАРИТОВ.\n\n" +
             "Включено — этажи встают на одну ось, где бы ни сидели их " +
             "пивоты. Это и избавляет от возни с пивотами.\n\n" +
             "Выключи, если у первого этажа есть выступающее крыльцо: оно " +
             "утянет центр габаритов в сторону, и башня поедет относительно " +
             "него. В этом случае пивоты придётся привести к одному правилу " +
             "руками.")]
    [SerializeField] private bool _centerHorizontally = true;

    [Header("━━━ Отладка ━━━")]
    [SerializeField] private bool _drawGizmos = true;
    [SerializeField] private bool _debugLog = false;

    // ───────────────────────── состояние ─────────────────────────

    /// <summary>
    /// ОДИН СОБРАННЫЙ ЭТАЖ. Высота и смещение пивота меряются ОДИН РАЗ, при
    /// создании: пересчитывать их на каждой перестановке значило бы каждый раз
    /// опрашивать всю геометрию, а она не меняется.
    /// </summary>
    private class Slice
    {
        public GameObject go;
        public float height;      // сколько занимает по вертикали
        public float pivotDrop;   // насколько пивот выше собственного низа
        public Vector2 centerXZ;  // центр габаритов относительно пивота
    }

    private Slice _groundSlice;
    private readonly List<Slice> _fillerSlices = new List<Slice>();
    private readonly List<Slice> _activeSlices = new List<Slice>();

    /// <summary>Текущий уровень. Меняется только через SetLevel.</summary>
    public int Level => _level;

    /// <summary>Высота всей башни от основания, метры.</summary>
    public float TotalHeight { get; private set; }

    /// <summary>Высота, на которой стоит НИЗ самого нижнего активного этажа.
    /// По ней удобно ставить лифт, лестницу и точки появления.</summary>
    public float ActiveFloorY { get; private set; }

    private void Awake()
    {
        if (_buildOnAwake) Rebuild();
    }

    // ═════════════════════════ УПРАВЛЕНИЕ ═════════════════════════

    /// <summary>
    /// ЗАДАТЬ УРОВЕНЬ. Единственная точка входа — всё остальное выводится.
    ///
    /// Активные этажи НЕ пересоздаются: их лишь переставляют выше. Иначе
    /// каждое поднятие уровня стирало бы всё, что игроки на них наработали, —
    /// а работают они именно там.
    /// </summary>
    public void SetLevel(int level)
    {
        level = Mathf.Max(0, level);
        if (_maxLevel > 0) level = Mathf.Min(level, _maxLevel);

        _level = level;

        EnsureGround();
        EnsureActives();
        EnsureFillers(level);
        Layout();

        if (_debugLog)
            Debug.Log($"[Башня] «{name}»: уровень {_level}, высота " +
                      $"{TotalHeight:0.00} м, активные с {ActiveFloorY:0.00} м", this);
    }

    /// <summary>Добавить холостой этаж. Активные уезжают выше.</summary>
    public void LevelUp() => SetLevel(_level + 1);

    /// <summary>Убрать холостой этаж.</summary>
    public void LevelDown() => SetLevel(_level - 1);

    /// <summary>Можно ли ещё расти.</summary>
    public bool CanLevelUp => _maxLevel <= 0 || _level < _maxLevel;

    /// <summary>
    /// СОБРАТЬ ЗАНОВО, с нуля. Всё, что стояло на активных этажах, пропадёт —
    /// поэтому в игре зови SetLevel, а это оставь для редактора и загрузки.
    /// </summary>
    public void Rebuild()
    {
        ClearAll();
        SetLevel(_level);
    }

    // ═════════════════════════ СБОРКА ═════════════════════════

    private void EnsureGround()
    {
        if (_groundSlice != null && _groundSlice.go != null) return;
        if (_ground == null || _ground.prefab == null) return;

        _groundSlice = Spawn(_ground, "Этаж_0_подъезд");
    }

    private void EnsureActives()
    {
        // Список активных мог поменяться в инспекторе — тогда пересоздаём их
        // целиком. В игре такого не бывает, а в редакторе это единственный
        // способ увидеть изменения.
        if (_activeSlices.Count == (_actives?.Length ?? 0))
        {
            bool alive = true;

            foreach (Slice s in _activeSlices)
                if (s == null || s.go == null) { alive = false; break; }

            if (alive) return;
        }

        foreach (Slice s in _activeSlices) Kill(s);
        _activeSlices.Clear();

        if (_actives == null) return;

        for (int i = 0; i < _actives.Length; i++)
        {
            if (_actives[i] == null || _actives[i].prefab == null) continue;

            Slice s = Spawn(_actives[i], $"Активный_{i + 1}");
            if (s != null) _activeSlices.Add(s);
        }
    }

    private void EnsureFillers(int want)
    {
        // Мёртвые ссылки: этаж могли снести снаружи.
        for (int i = _fillerSlices.Count - 1; i >= 0; i--)
            if (_fillerSlices[i] == null || _fillerSlices[i].go == null)
                _fillerSlices.RemoveAt(i);

        while (_fillerSlices.Count > want)
        {
            Kill(_fillerSlices[_fillerSlices.Count - 1]);
            _fillerSlices.RemoveAt(_fillerSlices.Count - 1);
        }

        if (_filler == null || _filler.prefab == null) return;

        while (_fillerSlices.Count < want)
        {
            Slice s = Spawn(_filler, $"Холостой_{_fillerSlices.Count + 1}");
            if (s == null) break;

            _fillerSlices.Add(s);
        }
    }

    /// <summary>
    /// РАССТАНОВКА. Идём снизу вверх и кладём НИЗ каждого этажа на верх
    /// предыдущего.
    ///
    /// Пивот при этом только компенсируется: где бы он ни сидел, низ этажа
    /// всё равно окажется там, где надо. Именно поэтому системе безразлично,
    /// как собраны префабы.
    /// </summary>
    private void Layout()
    {
        float y = 0f;

        y = Place(_groundSlice, y);

        foreach (Slice s in _fillerSlices) y = Place(s, y);

        ActiveFloorY = y;

        foreach (Slice s in _activeSlices) y = Place(s, y);

        TotalHeight = y;
    }

    private float Place(Slice s, float bottomLocalY)
    {
        if (s == null || s.go == null) return bottomLocalY;

        Transform t = s.go.transform;

        // Пивот надо поднять над низом ровно на столько, на сколько он выше
        // низа в самой модели.
        float py = bottomLocalY + s.pivotDrop;

        Vector3 lp = new Vector3(0f, py, 0f);

        // Горизонталь: центр габаритов на ось башни. Смещение пивота от
        // центра вычитается — тогда на оси оказывается сама постройка, а не
        // точка отсчёта.
        if (_centerHorizontally)
        {
            lp.x = -s.centerXZ.x;
            lp.z = -s.centerXZ.y;
        }

        t.localPosition = lp;
        t.localRotation = Quaternion.identity;

        return bottomLocalY + s.height + _seam;
    }

    // ═════════════════════════ ЗАМЕР ═════════════════════════

    /// <summary>
    /// СОЗДАТЬ ЭТАЖ И ИЗМЕРИТЬ ЕГО.
    ///
    /// Замер идёт сразу после создания, пока этаж стоит в нулевой местной
    /// позиции: тогда всё, что намерено, выражено ОТНОСИТЕЛЬНО ПИВОТА, и
    /// дальше эти числа годятся при любой перестановке.
    /// </summary>
    private Slice Spawn(Floor def, string niceName)
    {
        if (def == null || def.prefab == null) return null;

        GameObject go = InstantiateFloor(def.prefab);
        if (go == null) return null;

        go.name = niceName;

        Transform t = go.transform;
        t.SetParent(transform, false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;

        Slice s = new Slice { go = go };

        if (TryBounds(t, out Bounds b))
        {
            // Габариты мировые, а пивот сейчас стоит в начале координат
            // башни — значит разница между ними и есть смещение от пивота.
            Vector3 pivot = t.position;

            s.pivotDrop = pivot.y - b.min.y;
            s.height = def.heightOverride > 0.0001f ? def.heightOverride : b.size.y;
            s.centerXZ = new Vector2(b.center.x - pivot.x, b.center.z - pivot.z);
        }
        else
        {
            // Ни одного рендерера. Пустой этаж-логика — законный случай, но
            // высоту тогда обязан задать человек.
            s.pivotDrop = 0f;
            s.height = def.heightOverride > 0.0001f ? def.heightOverride : 0f;
            s.centerXZ = Vector2.zero;

            if (def.heightOverride <= 0.0001f)
                Debug.LogWarning(
                    $"[Башня] «{name}»: у этажа «{def.prefab.name}» нет ни " +
                    "одного рендерера, и высота не задана вручную. Он займёт " +
                    "ноль метров, и следующий встанет прямо в него.", this);
        }

        return s;
    }

    private bool TryBounds(Transform root, out Bounds b)
    {
        b = new Bounds();
        bool any = false;

        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;

            // Частицы и следы в габариты не берём: их объём живёт своей
            // жизнью и меняется от того, когда на него посмотрели.
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;

            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }

        return any;
    }

    // ═════════════════════════ СОЗДАНИЕ И СНОС ═════════════════════════

    private GameObject InstantiateFloor(GameObject prefab)
    {
#if UNITY_EDITOR
        // В редакторе создаём СВЯЗАННЫЙ экземпляр префаба: обычный Instantiate
        // порвал бы связь, и правка префаба перестала бы доходить до уже
        // собранных башен.
        if (!Application.isPlaying)
            return PrefabUtility.InstantiatePrefab(prefab) as GameObject;
#endif
        return Instantiate(prefab);
    }

    private void Kill(Slice s)
    {
        if (s == null || s.go == null) return;

        if (Application.isPlaying) Destroy(s.go);
        else DestroyImmediate(s.go);
    }

    private void ClearAll()
    {
        Kill(_groundSlice);
        _groundSlice = null;

        foreach (Slice s in _fillerSlices) Kill(s);
        _fillerSlices.Clear();

        foreach (Slice s in _activeSlices) Kill(s);
        _activeSlices.Clear();

        // Хвосты от прошлых сборок: башню могли пересобирать до того, как
        // появились списки, или кто-то накидал детей руками.
        List<Transform> strays = new List<Transform>();
        foreach (Transform c in transform) strays.Add(c);

        foreach (Transform c in strays)
        {
            if (c == null) continue;

            if (Application.isPlaying) Destroy(c.gameObject);
            else DestroyImmediate(c.gameObject);
        }
    }

    // ═════════════════════════ РЕДАКТОР ═════════════════════════

    [ContextMenu("▲ УРОВЕНЬ ВЫШЕ")]
    private void MenuUp() { LevelUp(); MarkDirty(); }

    [ContextMenu("▼ УРОВЕНЬ НИЖЕ")]
    private void MenuDown() { LevelDown(); MarkDirty(); }

    [ContextMenu("★ СОБРАТЬ ЗАНОВО")]
    private void MenuRebuild() { Rebuild(); MarkDirty(); }

    private void MarkDirty()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(this);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

    [ContextMenu("★ ПОЧЕМУ НЕ РАБОТАЕТ")]
    public void Diagnose()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int problems = 0;

        sb.AppendLine($"═══ БАШНЯ: {name} ═══\n");
        sb.AppendLine($"Уровень {_level}" + (_maxLevel > 0 ? $" из {_maxLevel}" : ""));

        if (_ground == null || _ground.prefab == null)
        {
            sb.AppendLine("\n❌ Нет первого этажа.");
            problems++;
        }

        if (_filler == null || _filler.prefab == null)
        {
            sb.AppendLine("\n❌ Нет холостого этажа — расти будет нечем.");
            problems++;
        }

        if (_actives == null || _actives.Length == 0)
        {
            sb.AppendLine("\n❌ Нет активных этажей.");
            problems++;
        }

        sb.AppendLine($"\n── СТОПКА СНИЗУ ВВЕРХ ──");

        float y = 0f;
        y = Report(sb, _groundSlice, y, "первый");

        foreach (Slice s in _fillerSlices) y = Report(sb, s, y, "холостой");
        foreach (Slice s in _activeSlices) y = Report(sb, s, y, "АКТИВНЫЙ");

        sb.AppendLine($"\nВысота башни: {y:0.000} м");
        sb.AppendLine($"Низ активных: {ActiveFloorY:0.000} м");

        if (transform.lossyScale != Vector3.one)
        {
            sb.AppendLine($"\n⚠ Масштаб корня {transform.lossyScale}, не единичный. " +
                          "Замеры мировые, а расстановка местная — при " +
                          "неединичном масштабе стыки разъедутся тем сильнее, " +
                          "чем выше этаж.");
            problems++;
        }

        sb.AppendLine($"\n═══ ПРОБЛЕМ: {problems} ═══");

        if (problems == 0)
            sb.AppendLine("Чисто. Если между этажами щель или нахлёст — это " +
                          "Seam: габариты включают выступающий пол или карниз, " +
                          "и следующий этаж встаёт на него.");

        if (problems > 0) Debug.LogWarning(sb.ToString(), this);
        else Debug.Log(sb.ToString(), this);
    }

    private float Report(System.Text.StringBuilder sb, Slice s, float y, string kind)
    {
        if (s == null || s.go == null) return y;

        sb.AppendLine($"  {y,7:0.00} м  {kind,-9} {s.go.name}   " +
                      $"высота {s.height:0.000}   пивот выше низа на {s.pivotDrop:0.000}");

        return y + s.height + _seam;
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos) return;

        Vector3 baseP = transform.position;

        // Ось башни: по ней и выравниваются этажи.
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.6f);
        Gizmos.DrawLine(baseP, baseP + Vector3.up * Mathf.Max(1f, TotalHeight));

        float y = 0f;

        y = GizmoFloor(baseP, y, _groundSlice, new Color(1f, 0.8f, 0.3f, 0.9f));

        foreach (Slice s in _fillerSlices)
            y = GizmoFloor(baseP, y, s, new Color(0.5f, 0.8f, 1f, 0.5f));

        foreach (Slice s in _activeSlices)
            y = GizmoFloor(baseP, y, s, new Color(0.4f, 1f, 0.5f, 0.9f));
    }

    private float GizmoFloor(Vector3 baseP, float y, Slice s, Color c)
    {
        if (s == null || s.go == null) return y;

        Gizmos.color = c;

        Vector3 p = baseP + Vector3.up * y;
        Gizmos.DrawLine(p + transform.right * -2f, p + transform.right * 2f);
        Gizmos.DrawLine(p + transform.forward * -2f, p + transform.forward * 2f);

        return y + s.height + _seam;
    }
}
