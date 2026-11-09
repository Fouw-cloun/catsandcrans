using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ПРОБОИНЫ В ПОСУДЕ. Вешается на кастрюлю, рядом с CookwarePressure (новая) или PotThermal (старая).
///
/// При разрыве в стенке появляются настоящие сквозные дыры, через них течёт
/// содержимое, и заваривать их надо горелкой — каждую отдельно.
///
/// КАК УСТРОЕНО
///   Дыры живут в МАСКЕ — текстуре, где белое значит «металл цел», чёрное
///   «дыра». Шейдер CatCooks/PotDamage читает её и выбрасывает пиксель через
///   clip(). Меш не трогается совсем.
///
/// ПО СЕТИ ХОДЯТ НЕ ПИКСЕЛИ, А СПИСОК ДЫР
///   Гонять текстуру между клиентами было бы безумием. Синхронизируется
///   список из нескольких структур по 8 байт, а маску каждый клиент рисует
///   себе сам — результат детерминирован, значит у всех он одинаковый.
///
/// МАСКА РИСУЕТСЯ НА ПРОЦЕССОРЕ, а не через RenderTexture и Blit.
///   Она маленькая (256×256), меняется редко — при разрыве и по ходу сварки, —
///   и перерисовка целиком занимает около миллисекунды. Взамен не нужны ни
///   второй шейдер для кисти, ни возня с временными целями рендера, ни
///   разбирательства, почему RenderTexture опустела после сворачивания окна.
///
/// КООРДИНАТЫ ЦИЛИНДРИЧЕСКИЕ, А НЕ ПО UV МЕША
///   Чтобы поставить дыру в точке попадания, нужны UV этой точки, а
///   RaycastHit.textureCoord требует Read/Write на меше — той самой галки, на
///   которой уже спотыкалась деформация. Кастрюля это цилиндр, поэтому угол
///   вокруг оси Y даёт U, высота даёт V. Read/Write не нужен, и наложенная
///   развёртка больше не может испортить картину.
/// </summary>
public class PotDamage : NetworkBehaviour
{
    /// <summary>
    /// Одно повреждение. Координаты в цилиндрической развёртке.
    ///
    /// Круглая дыра и трещина — это ОДНА структура, а не два типа: трещина
    /// просто дыра с ненулевой длиной. Иначе пришлось бы вести два списка,
    /// два пути синхронизации и две ветки в сварке, а разница между ними
    /// ровно в одном числе.
    /// </summary>
    public struct Hole
    {
        public float U;        // 0..1 вокруг кастрюли
        public float V;        // 0..1 снизу вверх
        public float Radius;   // толщина, в долях высоты
        public float Heal;     // 0 = открыта, 1 = заварена
        public float Length;   // 0 = круглая дыра, больше = трещина
        public float Angle;    // куда тянется трещина, радианы
    }

    [Header("Меш")]
    [Tooltip("Рендереры корпуса, у которых материал с шейдером " +
             "CatCooks/PotDamage. Пусто = соберутся сами со всей кастрюли.")]
    [SerializeField] private Renderer[] _shell;

    [Tooltip("Радиус ТЕЛА кастрюли, метры. Задаётся ЯВНО, а не выводится из " +
             "габаритов: у настоящей модели bounding box несимметричен — " +
             "ручки по бокам раздувают его на 30-40% только по одной оси, и " +
             "автоматический расчёт радиуса из высоты или из bounds ошибался " +
             "бы на кастрюлях с более длинными ручками. Значение по умолчанию " +
             "снято с присланной модели (радиус 0.123 м при высоте 0.240 м).")]
    [SerializeField] private float _bodyRadius = 0.123f;

    [Tooltip("Разрешение маски. 256 хватает: дыра размером с монету занимает " +
             "десяток пикселей, а больше и не надо — край всё равно " +
             "размывается ободком в шейдере.")]
    [SerializeField] private int _maskSize = 256;

    [Tooltip("Если материал корпуса без поддержки дыр — сам заменить его копией на шейдере " +
             "«CatCooks/Toon Props Damage» (цвет и текстура сохраняются). Без этого дыры не видны.")]
    [SerializeField] private bool _autoDamageMaterial = true;
    [Tooltip("Шейдер с дырами. Пусто — найдётся по имени «CatCooks/Toon Props Damage». " +
             "Для сборки игры лучше перетащить сюда, чтобы шейдер точно попал в билд.")]
    [SerializeField] private Shader _damageShader;

    [Header("Пробоины")]
    [Tooltip("Сколько повреждений делать при разрыве")]
    [SerializeField] private Vector2Int _holesPerRupture = new Vector2Int(3, 6);

    [Tooltip("Доля ТРЕЩИН среди повреждений. Остальное — круглые дыры. " +
             "0.6 значит, что трещин примерно втрое больше, чем дыр: рвущийся " +
             "от давления металл именно трескается, а ровные отверстия — " +
             "исключение.")]
    [Range(0f, 1f)][SerializeField] private float _crackChance = 0.6f;

    [Tooltip("Радиус круглой дыры в долях высоты кастрюли. Уменьшено: " +
             "прежние 0.06–0.13 давали пробоину в четверть посуды, от такой " +
             "кастрюли остаётся не кастрюля, а обрезок.")]
    [SerializeField] private Vector2 _holeRadius = new Vector2(0.022f, 0.05f);

    [Tooltip("Толщина трещины в долях высоты. Трещина узкая — это разрыв " +
             "металла, а не прорезь.")]
    [SerializeField] private Vector2 _crackWidth = new Vector2(0.008f, 0.018f);

    [Tooltip("Длина трещины в долях высоты")]
    [SerializeField] private Vector2 _crackLength = new Vector2(0.08f, 0.22f);

    [Tooltip("Насколько трещина виляет вбок относительно своей длины. " +
             "Идеально прямая читается как разрез ножом.")]
    [Range(0f, 0.5f)][SerializeField] private float _crackWaver = 0.18f;

    [Tooltip("В какой части высоты рвутся дыры — ТОЛЬКО СТЕНКИ. Дно (V=0) и " +
             "верхнюю кромку с ручкой (V=1) не трогаем ни при каких " +
             "настройках: это жёсткая граница, а не подбираемое число — дно " +
             "не должно отрываться, а у кромки дыра всё равно почти не видна.")]
    [SerializeField] private Vector2 _wallRange = new Vector2(0.28f, 0.82f);

    [Tooltip("Мягкость края в долях радиуса. Резкий край выглядит как " +
             "просверленное отверстие, размытый — как прогар.")]
    [Range(0.05f, 0.8f)][SerializeField] private float _edgeSoftness = 0.35f;

    [Header("Сварка")]
    [Tooltip("Насколько дыра затягивается за секунду работы горелкой")]
    [SerializeField] private float _weldPerSecond = 0.35f;

    [Tooltip("На каком расстоянии от повреждения электрод его достаёт, метры. " +
             "Меряется ПО ПОВЕРХНОСТИ в координатах маски, а не по прямой в " +
             "мире: цилиндр HoleWorldPos — приближение, а настоящая кастрюля " +
             "сужается книзу, и на реальном меше промах доходил бы до " +
             "нескольких сантиметров.")]
    [SerializeField] private float _weldReach = 0.10f;

    [Tooltip("Как часто слать прогресс сварки, раз в секунду. Каждый кадр " +
             "не нужно: полоска и так плавная у того, кто варит.")]
    [Min(2f)][SerializeField] private float _weldSyncRate = 8f;

    [Tooltip("Радиус пятна дуги на стенке в координатах маски. Только " +
             "картинка, на саму заварку не влияет.")]
    [Range(0.02f, 0.4f)][SerializeField] private float _arcRadius = 0.12f;

    [Tooltip("Как быстро разгорается пятно дуги, единиц в секунду")]
    [SerializeField] private float _arcRise = 6f;

    [Tooltip("Как быстро гаснет, когда горелку убрали. Медленнее, чем " +
             "разгорается: раскалённый металл остывает не мгновенно.")]
    [SerializeField] private float _arcFade = 1.8f;

    [Header("Отладка")]
    [SerializeField] private bool _debugGizmo = true;

    [Tooltip("Панель у кастрюли: список повреждений, расстояние до электрода, " +
             "что мешает варить. Включай, когда заварка не идёт.")]
    [SerializeField] private bool _debugHud;

    [Tooltip("Клавиша: пробить кастрюлю прямо сейчас, не доводя до перегрева. " +
             "Работает у хоста. None = выключено.")]
    [SerializeField] private KeyCode _debugPunchKey = KeyCode.None;

    [Tooltip("Клавиша: заварить всё разом")]
    [SerializeField] private KeyCode _debugSealKey = KeyCode.None;

    /// <summary>
    /// Список дыр. Именно он и есть состояние: маска — только его отрисовка.
    /// </summary>
    private readonly SyncList<Hole> _holes = new SyncList<Hole>();

    private CatCooks.Food.IPotBody _thermal;
    private Texture2D _mask;
    private Color32[] _pixels;
    private MaterialPropertyBlock _mpb;

    private float _minY, _maxY;      // границы кастрюли в объектных координатах
    private float _height = 1f;
    private bool _dirty;
    private float _nextWeldSync;

    // куда сейчас бьёт дуга и насколько раскалилось
    private Vector2 _arcUV;
    private float _arcHeat;

    // последний запрос от электрода — для панели
    private Vector3 _lastProbePoint;
    private float _lastProbeDist = -1f;
    private int _lastProbeHole = -1;
    private float _lastProbeTime = -99f;
    private string _lastReject = "";
    private int _weldingIndex = -1;
    private float _lastWeldTime = -99f;

    /// <summary>Дыра, которую варят прямо сейчас. -1, если никто не варит.</summary>
    public int WeldingHole => _weldingIndex;

    /// <summary>Точка мира, куда бьёт дуга. Для искр и звука.</summary>
    public Vector3 WeldingPoint =>
        _weldingIndex >= 0 && _weldingIndex < _holes.Count
            ? HoleWorldPos(_holes[_weldingIndex])
            : Vector3.zero;

    /// <summary>Насколько заварена дыра, которую варят. Для полоски прогресса.</summary>
    public float WeldingProgress =>
        _weldingIndex >= 0 && _weldingIndex < _holes.Count
            ? _holes[_weldingIndex].Heal
            : 0f;

    /// <summary>Зовётся, когда очередная дыра заварена до конца. Индекс дыры.</summary>
    public event System.Action<Vector3> HoleSealed;

    private static readonly int MaskId = Shader.PropertyToID("_DamageMask");
    private static readonly int MinYId = Shader.PropertyToID("_MaskMinY");
    private static readonly int MaxYId = Shader.PropertyToID("_MaskMaxY");
    private static readonly int HeatId = Shader.PropertyToID("_EdgeHeat");
    private static readonly int WeldId = Shader.PropertyToID("_WeldPoint");
    private static readonly int W2PId = Shader.PropertyToID("_DamageWorldToPot");

    /// <summary>Сколько дыр ещё не заварено.</summary>
    public int OpenHoles
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _holes.Count; i++) if (_holes[i].Heal < 1f) n++;
            return n;
        }
    }

    public bool IsSealed => OpenHoles == 0;

    private void Awake()
    {
        _thermal = GetComponent<CatCooks.Food.IPotBody>();
        if (_thermal == null) Debug.LogWarning($"[Пробоины] «{name}»: нет CookwarePressure/PotThermal — дыры при разрыве не появятся", this);

        if (_shell == null || _shell.Length == 0)
        {
            List<Renderer> found = new List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (PotBodyFilter.IsBody(r, transform)) found.Add(r);
            _shell = found.ToArray();
            Debug.Log($"[Пробоины] «{name}»: корпус собран сам — {_shell.Length} меш(ей)" +
                      (_shell.Length > 0 ? $": {string.Join(", ", System.Array.ConvertAll(_shell, x => x.name))}" : ""), this);
        }

        if (_autoDamageMaterial) EnsureDamageMaterial();
        MeasureBounds();
        BuildMask();
    }

    /// <summary>
    /// Корпус на обычном материале (Toon Props, Lit) дыр не покажет: маску читает только
    /// шейдер «CatCooks/Toon Props Damage». Подменяем копией материала на этом шейдере —
    /// одноимённые свойства (цвет, текстура, тени) переходят сами.
    /// </summary>
    private void EnsureDamageMaterial()
    {
        Shader sh = _damageShader != null ? _damageShader : Shader.Find("CatCooks/Toon Props Damage");
        if (sh == null)
        {
            Debug.LogWarning($"[Пробоины] «{name}»: не найден шейдер «CatCooks/Toon Props Damage» — дыры не будут видны " +
                             "(перетащи его в поле Damage Shader)", this);
            return;
        }
        foreach (Renderer r in _shell)
        {
            if (r == null) continue;
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].HasProperty("_DamageMask")) continue;
                if (!PotBodyFilter.IsBody(r, transform)) continue;
                var m = new Material(mats[i]) { name = mats[i].name + " (damage auto)" };
                m.shader = sh;
                mats[i] = m;
                changed = true;
            }
            if (changed)
            {
                r.sharedMaterials = mats;
                Debug.Log($"[Пробоины] «{r.name}»: материал переведён на шейдер с дырами", this);
            }
        }
    }

    /// <summary>
    /// Границы кастрюли по оси Y в ОБЪЕКТНЫХ координатах — их же читает
    /// шейдер, чтобы перевести высоту точки в координату маски. Считаем один
    /// раз: посуду мнёт деформация, но её габариты от этого не уезжают
    /// настолько, чтобы дыры поплыли.
    /// </summary>
    private void MeasureBounds()
    {
        bool any = false;
        Bounds b = new Bounds();

        // ПЕРЕВОДИМ ВОСЕМЬ УГЛОВ, а не центр с полуосями.
        //
        // Здесь был баг. Раньше поворачивался только ЦЕНТР бокса, а extents
        // брались как есть — а они в системе меша. Узел модели у кастрюли
        // несёт поворот −90° по X (Blender отдаёт геометрию Z-вверх), и вместо
        // настоящей высоты 0.240 м в расчёт шла горизонтальная полуось 0.168.
        // Ошибка в 40% попадала и в _height, и в _minY/_maxY, то есть во все
        // расстояния для наводки электрода. Углы переживают любой поворот.
        foreach (Renderer r in _shell)
        {
            if (r == null) continue;

            Bounds lb = r.localBounds;
            Vector3 c = lb.center;
            Vector3 e = lb.extents;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);

                Vector3 inPot = transform.InverseTransformPoint(
                    r.transform.TransformPoint(corner));

                if (!any) { b = new Bounds(inPot, Vector3.zero); any = true; }
                else b.Encapsulate(inPot);
            }
        }

        if (!any)
        {
            _minY = -0.5f; _maxY = 0.5f;
        }
        else
        {
            _minY = b.min.y;
            _maxY = b.max.y;
        }

        _height = Mathf.Max(0.01f, _maxY - _minY);

        // Радиус можно ПРОВЕРИТЬ по тем же габаритам: он не должен сильно
        // отличаться от половины горизонтального размера. Сильно отличается —
        // либо _bodyRadius выставлен наугад, либо ось кастрюли не Y, и тогда
        // все дыры лягут поперёк.
        if (any)
        {
            float halfWide = Mathf.Max(b.extents.x, b.extents.z);
            if (_bodyRadius < halfWide * 0.4f || _bodyRadius > halfWide * 1.6f)
                Debug.LogWarning(
                    $"[Пробоины] «{name}»: Body Radius = {_bodyRadius:0.000} м, а по " +
                    $"габаритам ожидалось около {halfWide:0.000} м. Либо поправь " +
                    "поле, либо у кастрюли ось не Y — тогда развёртка ляжет " +
                    "поперёк и дыры поедут.", this);

            Debug.Log($"[Пробоины] «{name}»: габариты в пространстве кастрюли — " +
                      $"высота {_height:0.000} м (Y от {_minY:0.000} до {_maxY:0.000}), " +
                      $"полуширина X {b.extents.x:0.000}, Z {b.extents.z:0.000}, " +
                      $"Body Radius {_bodyRadius:0.000}", this);
        }
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        _holes.OnChange += OnHolesChanged;
        if (_thermal != null) _thermal.Ruptured += OnRuptured;

        Redraw();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();

        _holes.OnChange -= OnHolesChanged;
        if (_thermal != null) _thermal.Ruptured -= OnRuptured;
    }

    private void OnHolesChanged(SyncListOperation op, int index, Hole prev, Hole next, bool asServer)
    {
        // У ХОСТА этот обработчик срабатывает ДВАЖДЫ: один раз как у сервера,
        // второй как у клиента. Перерисовывать маску дважды за кадр незачем,
        // поэтому здесь только помечаем и рисуем в Update.
        if (asServer && base.IsClientInitialized) return;
        _dirty = true;
    }

    private void Update()
    {
        if (_dirty) { _dirty = false; Redraw(); }

        DebugKeys();
        UpdateArc();

        // Раскалённый край гаснет вместе с кастрюлей — свежая пробоина
        // светится, остывшая просто чёрная.
        if (_thermal != null && _mpb != null)
        {
            float heat = Mathf.Clamp01(Mathf.InverseLerp(150f, 400f, _thermal.TemperatureC));
            ApplyToShell(heat);
        }
    }

    /// <summary>Пробить и заварить с клавиши — чтобы не гонять кастрюлю до
    /// перегрева каждый раз, когда проверяешь сварку.</summary>
    private void DebugKeys()
    {
        if (!base.IsServerInitialized) return;

        if (_debugPunchKey != KeyCode.None && Input.GetKeyDown(_debugPunchKey))
            ServerPunchRandom();

        if (_debugSealKey != KeyCode.None && Input.GetKeyDown(_debugSealKey))
            ServerSealAll();
    }

    /// <summary>
    /// СВЕЧЕНИЕ ПОД ДУГОЙ идёт отдельным параметром шейдера, а не рисуется
    /// в маску.
    ///
    /// Перерисовывать текстуру каждый кадр ради анимации было бы дорого и
    /// бессмысленно: пятно дуги живёт секунды и не оставляет следа. Шейдер
    /// получает точку и накал, а остальное считает сам — анимация обходится
    /// в один float4 на кадр.
    ///
    /// Светится ТОЛЬКО та дыра, которую варят. Остальные остаются холодными
    /// дырами, и по кастрюле сразу видно, где работа идёт, а где ещё нет.
    /// </summary>
    private void UpdateArc()
    {
        // Горелку убрали, если ServerWeldAt не звали последние полкадра-другой.
        bool welding = Time.time - _lastWeldTime < 0.15f;

        _arcHeat = Mathf.MoveTowards(_arcHeat, welding ? 1f : 0f,
            (welding ? _arcRise : _arcFade) * Time.deltaTime);

        if (!welding && _arcHeat <= 0.001f) _weldingIndex = -1;
    }

    // ───────────────────────── РАЗРЫВ ─────────────────────────

    private void OnRuptured()
    {
        if (!base.IsServerInitialized) return;
        ServerPunchRandom();
    }

    /// <summary>Наделать дыр в случайных местах стенки.</summary>
    [Server]
    public void ServerPunchRandom()
    {
        int n = Random.Range(_holesPerRupture.x, _holesPerRupture.y + 1);

        int cracks = 0;

        for (int i = 0; i < n; i++)
        {
            bool crack = Random.value < _crackChance;
            if (crack) cracks++;

            _holes.Add(new Hole
            {
                U = Random.value,
                V = Random.Range(_wallRange.x, _wallRange.y),
                Radius = crack
                    ? Random.Range(_crackWidth.x, _crackWidth.y)
                    : Random.Range(_holeRadius.x, _holeRadius.y),
                Length = crack ? Random.Range(_crackLength.x, _crackLength.y) : 0f,

                // Трещины идут преимущественно ВДОЛЬ стенки, а не поперёк:
                // кастрюлю распирает изнутри, и металл расходится по
                // вертикали. Разброс оставляем, чтобы не выглядело
                // штампованно.
                Angle = crack
                    ? Mathf.PI * 0.5f + Random.Range(-0.7f, 0.7f)
                    : 0f,
                Heal = 0f
            });
        }

        Debug.LogWarning($"[Пробоины] «{name}»: пробило в {n} местах " +
                         $"(трещин {cracks}, дыр {n - cracks})", this);
    }

    /// <summary>Пробить дыру в конкретной точке мира — например, туда,
    /// куда пришёлся удар.</summary>
    [Server]
    public void ServerPunchAt(Vector3 worldPoint, float radius)
    {
        Vector2 uv = WorldToMask(worldPoint);
        _holes.Add(new Hole { U = uv.x, V = uv.y, Radius = radius, Heal = 0f });
    }

    // ───────────────────────── СВАРКА ─────────────────────────

    /// <summary>
    /// Поварить в точке. Зовётся с сервера, пока игрок держит горелку у
    /// пробоины. Возвращает true, если тут действительно есть что варить —
    /// по этому и рисуются искры с полоской прогресса.
    ///
    /// Заваривается ОДНА, ближайшая дыра, а не все разом: иначе кастрюлю с
    /// четырьмя пробоинами чинили бы одним касанием в любом месте, и
    /// возиться с каждой было бы незачем.
    /// </summary>
    [Server]
    public bool ServerWeldAt(Vector3 worldPoint, float dt)
    {
        int best = FindHoleNear(worldPoint, out float bestDist);

        // Запоминаем КАЖДЫЙ запрос, даже неудачный: панель должна показывать,
        // насколько промахнулись, а не молчать, когда ничего не происходит.
        _lastProbePoint = worldPoint;
        _lastProbeDist = bestDist;
        _lastProbeHole = best;
        _lastProbeTime = Time.time;

        if (best < 0)
        {
            _lastReject = _holes.Count == 0
                ? "повреждений нет вообще"
                : "все повреждения уже заварены";
            return false;
        }

        if (bestDist > _weldReach)
        {
            _lastReject = $"мимо на {bestDist * 100f:0} см, добивает {_weldReach * 100f:0} см";
            return false;
        }

        _lastReject = "";

        Hole h = _holes[best];

        // Дуга бьёт сюда — и на сервере, и у всех клиентов через RpcArc.
        SetArc(best, h);
        RpcArc(best, h.U, h.V);

        h.Heal = Mathf.Clamp01(h.Heal + _weldPerSecond * dt);

        // Готовую дыру фиксируем сразу, промежуточный прогресс — с частотой
        // _weldSyncRate: полоску у сварщика рисует его собственный клиент,
        // а напарнику хватит нескольких обновлений в секунду.
        bool finished = h.Heal >= 1f;
        if (finished || Time.time >= _nextWeldSync)
        {
            _nextWeldSync = Time.time + 1f / _weldSyncRate;
            _holes[best] = h;
        }

        if (finished)
        {
            Debug.Log($"[Пробоины] «{name}»: дыра заварена, осталось {OpenHoles}", this);
            RpcSealed(h.U, h.V);

            // Дыры закрыты — но решение, годна ли кастрюля ЦЕЛИКОМ, принимает
            // PotThermal: он же смотрит на DeformableMesh и знает, не мятая ли
            // она ещё. Заварить последнюю дыру раньше, чем выправить форму, —
            // абсолютно нормальный порядок, и он не должен объявлять посуду
            // готовой сам по себе.
            if (OpenHoles == 0 && _thermal != null)
                _thermal.ServerCheckFullyRepaired();
        }

        return true;
    }

    /// <summary>
    /// Показать дугу у всех. Наблюдательский RPC, а не SyncVar: пятно живёт
    /// доли секунды и никого не интересует после того, как погасло — хранить
    /// его в состоянии объекта незачем.
    /// </summary>
    [ObserversRpc(BufferLast = false)]
    private void RpcArc(int index, float u, float v)
    {
        _weldingIndex = index;
        _arcUV = new Vector2(u, v);
        _lastWeldTime = Time.time;
    }

    /// <summary>Дыра закрылась — можно вспыхнуть искрами и звякнуть.</summary>
    [ObserversRpc(BufferLast = false)]
    private void RpcSealed(float u, float v)
    {
        Vector3 p = HoleWorldPos(new Hole { U = u, V = v });

        SparkBurst.Play(p, 70, 5.5f);
        HoleSealed?.Invoke(p);
    }

    private void SetArc(int index, Hole h)
    {
        _weldingIndex = index;
        _arcUV = new Vector2(h.U, h.V);
        _lastWeldTime = Time.time;
    }

    /// <summary>
    /// Ближайшее незаваренное повреждение к точке на стенке. Расстояние
    /// возвращается В МЕТРАХ ПО ПОВЕРХНОСТИ.
    ///
    /// Считаем в координатах маски, а не по прямой в мире: обе точки лежат на
    /// стенке, и разница по развёртке — это и есть путь по металлу. Прямая
    /// через объём дала бы меньшее число для дыр на противоположных боках,
    /// а по шву вообще посчитала бы соседние точки далёкими.
    /// </summary>
    public int FindHoleNear(Vector3 worldPoint, out float metersOnSurface)
    {
        Vector2 uv = WorldToMask(worldPoint);
        float aspect = Aspect();

        int best = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _holes.Count; i++)
        {
            if (_holes[i].Heal >= 1f) continue;

            // Шов: точка на U=0.99 и точка на U=0.01 — соседи.
            float du = Mathf.Abs(uv.x - _holes[i].U);
            du = Mathf.Min(du, 1f - du);

            float dv = uv.y - _holes[i].V;

            // du умножаем на aspect, чтобы обе оси мерились в одних единицах —
            // долях высоты. Потом переводим в метры.
            float d = Mathf.Sqrt(du * aspect * du * aspect + dv * dv) * _height;

            if (d < bestDist) { bestDist = d; best = i; }
        }

        metersOnSurface = bestDist;
        return best;
    }

    /// <summary>Сколько повреждений всего, включая заваренные.</summary>
    public int TotalHoles => _holes.Count;

    /// <summary>Убрать все дыры — для отладки и для полного ремонта на станции.</summary>
    [Server]
    public void ServerSealAll()
    {
        _holes.Clear();
        if (_thermal != null) _thermal.ServerCheckFullyRepaired();
    }

    // ───────────────────────── ГЕОМЕТРИЯ ─────────────────────────

    /// <summary>Точка мира в координаты маски.</summary>
    public Vector2 WorldToMask(Vector3 worldPoint)
    {
        Vector3 p = transform.InverseTransformPoint(worldPoint);

        float u = Mathf.Atan2(p.x, p.z) / (2f * Mathf.PI) + 0.5f;
        float v = Mathf.InverseLerp(_minY, _maxY, p.y);
        return new Vector2(Mathf.Repeat(u, 1f), Mathf.Clamp01(v));
    }

    /// <summary>Где дыра находится в мире. Нужно и сварке, и течи.</summary>
    public Vector3 HoleWorldPos(Hole h)
    {
        // Радиус берём по габаритам: точной формы стенки мы не знаем, а для
        // попадания горелкой и для точки истечения этого достаточно.
        float ang = (h.U - 0.5f) * 2f * Mathf.PI;
        float r = _bodyRadius;

        Vector3 local = new Vector3(Mathf.Sin(ang) * r,
                                    Mathf.Lerp(_minY, _maxY, h.V),
                                    Mathf.Cos(ang) * r);

        return transform.TransformPoint(local);
    }

    /// <summary>
    /// Точки, откуда должно течь. Отдаём наружу, чтобы LiquidStream и PotSpill
    /// брали их как источники струй, а не изобретали свои.
    /// </summary>
    public void GetLeakPoints(List<Vector3> into)
    {
        into.Clear();
        for (int i = 0; i < _holes.Count; i++)
            if (_holes[i].Heal < 1f) into.Add(HoleWorldPos(_holes[i]));
    }

    // ───────────────────────── МАСКА ─────────────────────────

    private void BuildMask()
    {
        int size = Mathf.Clamp(_maskSize, 64, 1024);

        // МИПМАПЫ ВЫКЛЮЧЕНЫ намеренно. На шве, где U перескакивает с 1 на 0,
        // производные координат взрываются, GPU выбирает самый мелкий мип, и
        // вдоль кастрюли пошла бы серая полоса.
        _mask = new Texture2D(size, size, TextureFormat.R8, false, true)
        {
            name = $"PotDamageMask ({name})",
            wrapModeU = TextureWrapMode.Repeat,   // шов по кругу
            wrapModeV = TextureWrapMode.Clamp,    // по высоте не заворачиваем
            filterMode = FilterMode.Bilinear
        };

        _pixels = new Color32[size * size];
        _mpb = new MaterialPropertyBlock();

        Redraw();
    }

    /// <summary>Перерисовать маску по текущему списку дыр.</summary>
    private void Redraw()
    {
        if (_mask == null) return;

        int size = _mask.width;

        // Чистый металл.
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = new Color32(255, 255, 255, 255);

        for (int i = 0; i < _holes.Count; i++) PaintHole(_holes[i], size);

        _mask.SetPixels32(_pixels);
        _mask.Apply(false);

        ApplyToShell(0f);
    }

    /// <summary>
    /// Круг в цилиндрической развёртке — на самом деле эллипс.
    ///
    /// По высоте пиксель стоит столько же метров всегда, а по окружности
    /// зависит от того, насколько кастрюля широка. Если рисовать ровный круг
    /// в пикселях, на готовой модели он вытянется. Поэтому радиус по U
    /// пересчитывается через отношение окружности к высоте.
    ///
    /// Заваренная дыра просто СЖИМАЕТСЯ к центру: металл затягивает края
    /// внутрь, и это ровно то, что видит игрок под дугой.
    /// </summary>
    private void PaintHole(Hole h, int size)
    {
        float open = 1f - Mathf.Clamp01(h.Heal);
        if (open <= 0.001f) return;

        // ТРЕЩИНА — цепочка мелких пятен вдоль своей оси.
        //
        // Рисовать её одним вытянутым эллипсом было бы дешевле, но эллипс
        // всегда прямой и одинаковой толщины по всей длине. Цепочка даёт
        // виляние и сужение к концам — трещина заостряется там, где металл
        // ещё держится, и это ровно то, чем разрыв отличается от прорези.
        if (h.Length > 0.001f)
        {
            PaintCrack(h, size, open);
            return;
        }

        PaintBlob(h.U, h.V, h.Radius * open, size);
    }

    /// <summary>Трещина: цепочка пятен вдоль оси, тоньше к концам и с вилянием.</summary>
    private void PaintCrack(Hole h, int size, float open)
    {
        float len = h.Length * open;
        float wid = h.Radius * open;

        // Шаг в полтолщины: реже — цепочка распадается на «пунктир»,
        // чаще — впустую перерисовываем те же пиксели.
        int steps = Mathf.Max(3, Mathf.CeilToInt(len * size / Mathf.Max(1f, wid * size * 0.5f)));

        float aspect = Aspect();
        float dirU = Mathf.Cos(h.Angle) / aspect;   // вдоль обхвата — сжимаем
        float dirV = Mathf.Sin(h.Angle);

        // Перпендикуляр для виляния.
        float perpU = -dirV / aspect;
        float perpV = Mathf.Cos(h.Angle);

        // Своё зерно у каждой трещины: две трещины рядом не должны вилять
        // одинаково.
        float seed = h.U * 37.13f + h.V * 91.7f;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps - 0.5f;          // -0.5 .. 0.5

            // Сужение к концам: в середине полная толщина, на концах сходит
            // на нет.
            float taper = Mathf.Sqrt(Mathf.Max(0f, 1f - 4f * t * t));

            float wobble = (Mathf.PerlinNoise(seed + i * 0.35f, seed * 0.5f) - 0.5f)
                         * _crackWaver * len;

            float u = h.U + dirU * len * t + perpU * wobble;

            // ЖЁСТКО ЗАЖИМАЕМ каждое звено трещины в диапазон стенок. Без
            // этого длинная вертикальная трещина с центром у нижней границы
            // легко утаскивала свой конец за _wallRange.x — дырка на дне,
            // ровно то, чего просили избежать.
            float v = Mathf.Clamp(h.V + dirV * len * t + perpV * wobble,
                                  _wallRange.x, _wallRange.y);

            PaintBlob(u, v, wid * taper, size);
        }
    }

    /// <summary>Одно пятно маски. Общий кирпич для дыр и для звеньев трещины.</summary>
    private void PaintBlob(float u, float v, float rV, int size)
    {
        if (rV <= 0.0005f) return;

        float rU = rV / Aspect();

        int cx = Mathf.RoundToInt(Mathf.Repeat(u, 1f) * size);
        int cy = Mathf.RoundToInt(Mathf.Clamp01(v) * size);
        int spanX = Mathf.CeilToInt(rU * size) + 2;
        int spanY = Mathf.CeilToInt(rV * size) + 2;

        for (int dy = -spanY; dy <= spanY; dy++)
        {
            int y = cy + dy;
            if (y < 0 || y >= size) continue;          // по высоте не заворачиваем

            for (int dx = -spanX; dx <= spanX; dx++)
            {
                // По кругу заворачиваем: повреждение на шве должно быть
                // цельным, а не половинками у двух краёв текстуры.
                int x = (cx + dx) % size;
                if (x < 0) x += size;

                float nx = dx / (rU * size);
                float ny = dy / (rV * size);
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                if (d > 1f) continue;

                float k = Mathf.InverseLerp(1f, 1f - _edgeSoftness, d);
                byte val = (byte)Mathf.RoundToInt(Mathf.Lerp(255f, 0f, k));

                int idx = y * size + x;
                if (_pixels[idx].r > val) _pixels[idx] = new Color32(val, val, val, 255);
            }
        }
    }

    /// <summary>Во сколько раз развёртка растянута по U относительно V.</summary>
    private float Aspect()
    {
        float circumference = 2f * Mathf.PI * _bodyRadius;
        return Mathf.Max(0.01f, circumference / _height);
    }

    private void ApplyToShell(float edgeHeat)
    {
        if (_shell == null || _mpb == null) return;

        for (int i = 0; i < _shell.Length; i++)
        {
            Renderer r = _shell[i];
            if (r == null) continue;

            r.GetPropertyBlock(_mpb);
            _mpb.SetTexture(MaskId, _mask);
            _mpb.SetFloat(MinYId, _minY);
            _mpb.SetFloat(MaxYId, _maxY);
            _mpb.SetFloat(HeatId, edgeHeat);

            // ПЕРЕХОД МИР → КОРЕНЬ КАСТРЮЛИ. Шейдер больше не считает
            // развёртку от объектных координат меша: Blender отдаёт геометрию
            // Z-вверх, узел модели несёт поворот −90° по X, и высота с
            // обхватом менялись местами — повреждения расползались
            // диагональными полосами через всю посуду.
            //
            // Матрица берётся каждый кадр, а не кэшируется: кастрюлю носят,
            // ставят на плиту и роняют, worldToLocalMatrix при этом меняется.
            _mpb.SetMatrix(W2PId, transform.worldToLocalMatrix);

            // xy — точка на стенке, z — накал, w — радиус пятна.
            _mpb.SetVector(WeldId, new Vector4(_arcUV.x, _arcUV.y, _arcHeat, _arcRadius));

            r.SetPropertyBlock(_mpb);
        }
    }

    private void OnDestroy()
    {
        if (_mask != null) Destroy(_mask);
    }

    /// <summary>
    /// Панель у кастрюли. Отвечает на вопросы в том порядке, в каком они
    /// возникают, когда заварка не идёт: есть ли вообще повреждения, доходят
    /// ли до нас запросы от электрода, и если доходят — почему отказ.
    /// </summary>
    private void OnGUI()
    {
        if (!_debugHud) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 anchor = transform.position + Vector3.up * _height;
        if (Vector3.Distance(cam.transform.position, anchor) > 6f) return;

        Vector3 sp = cam.WorldToScreenPoint(anchor);
        if (sp.z <= 0f) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        sb.AppendLine($"ПРОБОИНЫ  всего {_holes.Count}, открыто {OpenHoles}");
        sb.AppendLine($"высота {_height:0.000} м, радиус {_bodyRadius:0.000} м, " +
                      $"добивает {_weldReach * 100f:0} см");
        sb.AppendLine($"маска {(_mask != null ? _mask.width.ToString() : "НЕТ")}, " +
                      $"рендереров {(_shell != null ? _shell.Length : 0)}");

        for (int i = 0; i < _holes.Count && i < 8; i++)
        {
            Hole h = _holes[i];
            string kind = h.Length > 0.001f ? "трещина" : "дыра   ";
            string mark = i == _lastProbeHole ? "►" : " ";
            sb.AppendLine($"{mark}{i}: {kind} u{h.U:0.00} v{h.V:0.00} " +
                          $"заварено {h.Heal * 100f:0}%");
        }

        bool fresh = Time.time - _lastProbeTime < 0.3f;

        if (!fresh)
            sb.Append("электрод НЕ ДОСТАЁТ — команда сюда не приходит. " +
                      "Смотри статус на самом аппарате.");
        else if (_lastReject.Length > 0)
            sb.Append($"отказ: {_lastReject}");
        else
            sb.Append($"ВАРИТ дыру {_lastProbeHole}, до неё {_lastProbeDist * 100f:0} см");

        string txt = sb.ToString();

        GUIStyle st = new GUIStyle(GUI.skin.box)
        {
            fontSize = 11,
            alignment = TextAnchor.UpperLeft
        };
        st.normal.textColor = !fresh
            ? new Color(0.8f, 0.8f, 0.85f)
            : _lastReject.Length > 0
                ? new Color(1f, 0.65f, 0.3f)
                : new Color(0.5f, 1f, 0.6f);

        Vector2 size = st.CalcSize(new GUIContent(txt));
        GUI.Box(new Rect(sp.x + 24f, Screen.height - sp.y, size.x + 14f, size.y + 10f),
                txt, st);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_debugGizmo || !Application.isPlaying) return;

        for (int i = 0; i < _holes.Count; i++)
        {
            Hole h = _holes[i];
            Gizmos.color = h.Heal >= 1f
                ? new Color(0.4f, 1f, 0.5f, 0.7f)
                : Color.Lerp(new Color(1f, 0.3f, 0.2f, 0.9f),
                             new Color(1f, 0.9f, 0.3f, 0.9f), h.Heal);

            Vector3 p = HoleWorldPos(h);
            Gizmos.DrawWireSphere(p, h.Radius * _height * transform.lossyScale.y);
            Gizmos.DrawLine(transform.position, p);
        }

        // Куда последний раз тыкали электродом и добил ли он.
        if (Time.time - _lastProbeTime < 1f)
        {
            Gizmos.color = _lastReject.Length > 0
                ? new Color(1f, 0.5f, 0.2f, 0.9f)
                : new Color(0.4f, 1f, 0.5f, 0.9f);

            Gizmos.DrawWireSphere(_lastProbePoint, _weldReach);

            if (_lastProbeHole >= 0 && _lastProbeHole < _holes.Count)
                Gizmos.DrawLine(_lastProbePoint, HoleWorldPos(_holes[_lastProbeHole]));
        }
    }
}
