using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ПАСТА В ТАРЕ — пюре, фарш, тесто. Вешается на ёмкость, рядом с CookingVessel.
///
/// Это НЕ жидкость. PotLiquidVisual рисует налив: плоское зеркало на уровне,
/// объём внутри полости. Паста ведёт себя наоборот — она ДЕРЖИТ ФОРМУ: лежит
/// кучками там, куда её положили, покрыта пиками от инструмента и трясётся как
/// единое тело, а не плещется. Общей математики почти нет, поэтому это
/// отдельный компонент, а не второй режим внутри первого.
///
/// КУЧКИ, А НЕ ОДИН КУПОЛ
///   Каждая растолчённая картофелина оставляет СВОЮ кучку в том месте, где
///   лежала. Кучки складываются как метаболы: там, где они перекрываются,
///   высота суммируется и между ними вырастает перемычка — то есть они
///   сливаются сами, без отдельного кода на «слияние поверхностей».
///
///   Дальше кучки МЕДЛЕННО СТЕКАЮТСЯ к общему центру тяжести и расплываются,
///   пока не станут одной массой. Скорость зависит от густоты: жижа
///   растекается за секунды, глина стоит кучками долго. Это и есть разница
///   между пюре и фаршем на глаз, без единого нового ассета.
///
///   Соседние кучки, наехавшие друг на друга, объединяются в одну. Иначе их
///   число росло бы с каждой картофелиной, а считать надо на каждую вершину
///   сетки — двадцать кучек на 625 вершин это уже заметно.
///
/// ГУСТОТА СЧИТАЕТСЯ ИЗ СОСТАВА, а не задаётся руками. Доля фракций категории
/// Liquid в общей массе — вот и всё разжижение: долил воды, доля выросла, паста
/// осела, пики сгладились, кучки поплыли. Категория Liquid в справочнике уже
/// есть и заполнена; отдельное поле «вязкость» пришлось бы проставлять руками
/// у каждого продукта и обязательно где-нибудь забыть.
///
/// ДЖИГЛ — ПРУЖИНЫ, А НЕ ФИЗДВИЖОК. Три моды с собственной частотой и
/// затуханием, возбуждаются импульсом от удара. Настоящая мягкотельная физика
/// здесь не окупается: по сети её не синхронизировать, а на глаз разница с
/// тремя синусоидами не читается.
///
/// СЕТЬ: форма и густота считаются из уже синхронизированного состава, поэтому
/// у всех совпадают. Места кучек приходят разовым RPC в момент растворения.
/// Джигл от ударов возбуждается локально и может разойтись на доли секунды —
/// это косметика, ради неё гонять трафик незачем.
/// </summary>
[RequireComponent(typeof(CookingVessel))]
public class PasteVisual : MonoBehaviour
{
    [Header("Где паста")]
    [Tooltip("Коллайдер внутреннего объёма тары. По нему берутся размеры дна " +
             "и высота бортов. Пусто = ищется BoxCollider в детях.")]
    [SerializeField] private BoxCollider _bounds;

    [Tooltip("Материал поверхности. Обычный Lit подойдёт — цвет всё равно " +
             "подставляется из состава через MaterialPropertyBlock.")]
    [SerializeField] private Material _material;

    [Header("Сетка")]
    [Tooltip("Разбиение поверхности. Теперь край кучки виден по-настоящему, " +
             "и на грубой сетке он ступенчатый — 32 заметно лучше 24, а " +
             "дороже почти не выходит: считается только высота.")]
    [Range(8, 64)][SerializeField] private int _resolution = 32;

    [Tooltip("Отступ от стенок, доля половины размера")]
    [Range(0f, 0.3f)][SerializeField] private float _inset = 0.06f;

    [Tooltip("КАКАЯ ЛОКАЛЬНАЯ ОСЬ КОЛЛАЙДЕРА СМОТРИТ ВВЕРХ.\n\n" +
             "AUTO — определить самим: берётся та ось коллайдера, которая " +
             "в мире смотрит выше остальных. Для тары это однозначно, " +
             "гадать не нужно. Выбранную ось компонент пишет в консоль при " +
             "старте.\n\n" +
             "Определяется ОДИН РАЗ, пока тара стоит ровно. Дальше её можно " +
             "носить и наклонять: паста наклоняется вместе с ней, как и " +
             "положено содержимому.\n\n" +
             "Вручную задавать имеет смысл, только если тара спавнится уже " +
             "лежащей на боку и автоопределение промахнулось.")]
    [SerializeField] private Axis _upAxis = Axis.Auto;

    [Tooltip("СУЖЕНИЕ КНИЗУ. Во сколько раз дно у́же верха.\n\n" +
             "BoxCollider — прямая коробка, а тара обычно трапеция: стенки " +
             "расходятся кверху. Задал коробку по верху — паста на дне шире " +
             "тары и вылезает сквозь стенки; задал по дну — висит узким " +
             "столбиком посередине.\n\n" +
             "1 = стенки отвесные, коробка и есть форма. 0.6 = дно вдвое " +
             "с лишним у́же. Мерь по модели: ширина дна делить на ширину верха.")]
    [Range(0.2f, 1f)][SerializeField] private float _bottomScale = 1f;

    // ───────────────────────── КУЧКИ ─────────────────────────

    [Header("Кучки")]
    [Tooltip("Радиус кучки от одной порции, доля половины ширины тары. " +
             "Больше — кучки шире и сливаются раньше.")]
    [Range(0.1f, 1f)][SerializeField] private float _blobRadius = 0.32f;

    [Tooltip("Высота кучки от ОДНОЙ ПОРЦИИ, доля высоты бортов")]
    [Range(0.05f, 1.5f)][SerializeField] private float _blobHeight = 0.5f;

    [Tooltip("СКОЛЬКО ГРАММОВ ДАЮТ ЭТУ ВЫСОТУ.\n\n" +
             "Раньше высота считалась от вместимости тары — и в банке на " +
             "три килограмма одна картофелина в 300 г давала горку в " +
             "десятую долю борта. Поверхность строилась, но была настолько " +
             "плоской, что обрезка съедала её целиком.\n\n" +
             "Теперь опорой служит РАЗМЕР ПОРЦИИ, а не тары: одна " +
             "картофелина — заметная кучка в любом ведре, хоть в тазике, " +
             "хоть в чане. Ставь примерно массу одного продукта.")]
    [Min(1f)][SerializeField] private float _gramsForBlobHeight = 350f;

    [Tooltip("Как быстро кучки стекаются к общему центру, доля в секунду. " +
             "Это скорость для ЖИДКОЙ пасты; густая ползёт во столько раз " +
             "медленнее, во сколько она гуще.")]
    [SerializeField] private float _flowSpeed = 0.35f;

    [Tooltip("Как быстро кучка расплывается вширь, доля в секунду")]
    [SerializeField] private float _spreadSpeed = 0.25f;

    [Tooltip("До какого радиуса расплывается кучка в пределе, доля половины " +
             "ширины тары. Дойдя до него, паста становится ровным слоем.")]
    [Range(0.3f, 2f)][SerializeField] private float _spreadLimit = 1.1f;

    [Tooltip("Насколько кучки должны наехать друг на друга, чтобы слиться в " +
             "одну. 0.5 = центры ближе половины суммы радиусов.")]
    [Range(0.1f, 1f)][SerializeField] private float _mergeAt = 0.45f;

    [Tooltip("Потолок числа кучек. При превышении самые мелкие сливаются " +
             "принудительно: считать их надо на каждую вершину сетки.")]
    [Range(2, 24)][SerializeField] private int _maxBlobs = 10;

    [Header("Край кучки")]
    [Tooltip("НА КАКОЙ ВЫСОТЕ КОНЧАЕТСЯ ПАСТА, доля высоты бортов.\n\n" +
             "Ниже этого поверхность прозрачная. Это и есть лекарство от " +
             "«бежевого квадрата»: сетка построена на всю тару, но рисуется " +
             "только там, где реально лежит масса, и края получаются по форме " +
             "кучек, а не по форме коробки.")]
    [Range(0.005f, 0.2f)][SerializeField] private float _edgeFade = 0.05f;

    [Header("Рыхлость")]
    [Tooltip("Высота комков поверх кучки, доля высоты бортов. Ровный купол " +
             "выглядит вылитым из формы, комковатый — размятым.")]
    [Range(0f, 0.6f)][SerializeField] private float _lumpHeight = 0.22f;

    [Tooltip("Размер комков — сколько их укладывается поперёк кучки. " +
             "Тоже нормировано на её размер.")]
    [SerializeField] private float _lumpScale = 5f;

    [Header("Пики")]
    [Tooltip("Высота пиков, доля высоты бортов. Это следы инструмента, как у " +
             "взбитого белка.")]
    [Range(0f, 0.5f)][SerializeField] private float _peakHeight = 0.12f;

    [Tooltip("Частота пиков — сколько бугорков укладывается поперёк кучки. " +
             "Координаты шума НОРМИРОВАНЫ на её размер, поэтому число значит " +
             "одно и то же в любой таре и при любом масштабе модели. " +
             "Раньше оно шло в локальных единицах коллайдера (у банки это " +
             "тысячные доли), и шум по всей поверхности был почти постоянным — " +
             "никакой рифлёности не получалось в принципе.")]
    [SerializeField] private float _peakScale = 9f;

    [Range(1, 3)][SerializeField] private int _peakOctaves = 2;

    [Header("Дрожь под инструментом")]
    [Range(0f, 0.2f)][SerializeField] private float _tremor = 0.035f;
    [SerializeField] private float _tremorSpeed = 26f;
    [SerializeField] private float _tremorHold = 0.35f;

    [Header("Джигл")]
    [Tooltip("Амплитуда качания от одного импульса, доля высоты бортов")]
    [Range(0f, 0.5f)][SerializeField] private float _jiggleGain = 0.12f;

    [Tooltip("Частота качания у ГУСТОЙ пасты, Гц — короткий резкий отклик")]
    [SerializeField] private float _jiggleFreqThick = 7f;

    [Tooltip("Частота качания у ЖИДКОЙ, Гц — ленивое долгое качание")]
    [SerializeField] private float _jiggleFreqThin = 3.2f;

    [SerializeField] private float _dampThick = 5.5f;
    [SerializeField] private float _dampThin = 1.8f;

    [Header("Разжижение")]
    [Tooltip("При какой доле жидкости в массе паста считается совсем жидкой")]
    [Range(0.1f, 1f)][SerializeField] private float _liquidForThin = 0.5f;

    [SerializeField] private float _thicknessLerp = 2.5f;

    [Header("Цвет")]
    [Tooltip("ОСВЕТЛЕНИЕ. DebrisColor задуман для мелких летящих ошмётков — " +
             "он тёмный и насыщенный, иначе крошки не видно в воздухе. " +
             "Растёртая же масса всегда СВЕТЛЕЕ целого продукта: пюре светлее " +
             "картофелины, фарш светлее куска мяса. Без осветления в таре " +
             "лежит тёмно-бурая клякса.")]
    [Range(0f, 1f)][SerializeField] private float _lighten = 0.45f;

    [SerializeField] private Color _fallbackColor = new Color(0.92f, 0.86f, 0.68f);

    [Header("Кучка растёт заранее")]
    [Tooltip("С какого прогресса продукт начинает ОСЕДАТЬ в тару, ещё будучи " +
             "предметом. 0.7 = последняя треть долбления уже наращивает " +
             "кучку под ним.\n\n" +
             "Без этого картофелина мнётся до конца и ИСЧЕЗАЕТ рывком, а " +
             "кучка появляется из ниоткуда — переход видно, и он выглядит " +
             "дёшево. С перекрытием одно перетекает в другое.")]
    [Range(0f, 0.95f)][SerializeField] private float _formFrom = 0.7f;

    [Tooltip("Как часто искать доходящие продукты, секунд")]
    [SerializeField] private float _formCheckEvery = 0.15f;

    [Header("Отладка")]
    [Tooltip("Рисовать поверхность ЦЕЛИКОМ, без обрезки края. Прямоугольник " +
             "на всю тару — это норма для этого режима.\n\n" +
             "Делит поиск пополам: видно с ним — значит геометрия строится и " +
             "дело в обрезке или высоте. Не видно — дело в материале, " +
             "коробке или в том, что кучек нет вовсе.")]
    [SerializeField] private bool _debugSolid;

    [SerializeField] private bool _debugHud;

    /// <summary>Одна кучка. Координаты в плане тары, метры от центра дна.</summary>
    public enum Axis { Auto, X, Y, Z }

    private struct Blob
    {
        public Vector2 Pos;
        public float Mass;      // граммы
        public float Radius;    // метры
    }

    /// <summary>Кучка, которая ещё растёт под недоработанным продуктом.
    /// Живёт отдельно от настоящих: её каждый кадр пересчитывают, а не
    /// копят.</summary>
    private struct Forming
    {
        public FoodItem Item;
        public Vector2 Pos;
        public float Mass;
        public float Radius;
    }

    private CookingVessel _vessel;
    private Mesh _mesh;
    private MeshFilter _filter;
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _mpb;

    private Vector3[] _verts;
    private Vector2[] _plan;      // положение вершины в плане

    /// <summary>Вершинные цвета: R — покрытие (есть ли тут паста), G — доля
    /// от полной высоты кучки. Шейдер по ним обрезает край и красит впадины.
    /// Дешевле любой текстуры и меняется вместе с формой.</summary>
    private Color[] _colors;
    private readonly List<Blob> _blobs = new List<Blob>();
    private readonly List<Forming> _forming = new List<Forming>();
    private float _nextFormCheck;
    private readonly Collider[] _formHits = new Collider[16];

    private float _knownMassG;    // сколько массы уже разложено по кучкам
    private float _thickness = 1f;

    private readonly float[] _amp = new float[3];
    private readonly float[] _vel = new float[3];
    private float _lastImpulseTime = -99f;

    /// <summary>Индекс оси «вверх»: 0 = X, 1 = Y, 2 = Z.</summary>
    private int _up = 1;

    /// <summary>Ось ещё можно пересматривать. Замирает, как только в таре
    /// появилась паста.</summary>
    private bool _axisLocked;

    /// <summary>Самая высокая точка пасты. Только для отладочной панели —
    /// по ней сразу видно, слишком плоско или нет.</summary>
    private float _maxHeightShown;

    private float _sizeX = 0.3f, _sizeZ = 0.3f, _wallH = 0.2f;
    private Vector3 _floorLocal;
    private float _halfMin = 0.15f;

    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorLegacyId = Shader.PropertyToID("_Color");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");

    /// <summary>Густота: 1 — глина, 0 — почти жидкость.</summary>
    public float Thickness01 => _thickness;

    /// <summary>Сколько кучек лежит сейчас. Для отладки и звука.</summary>
    public int BlobCount => _blobs.Count;

    /// <summary>
    /// Во сколько раз у́же сечение тары на данной высоте.
    ///
    /// Трапеция: внизу _bottomScale, вверху 1. Паста прижимается к этому
    /// сечению, поэтому не вылезает сквозь наклонные стенки — а именно так
    /// она и вылезала, пока форма считалась прямой коробкой.
    /// </summary>
    private float SectionAt(float height01)
        => Mathf.Lerp(_bottomScale, 1f, Mathf.Clamp01(height01));

    /// <summary>
    /// ПОДОГНАТЬ КОРОБКУ ПОД ТАРУ. Правый клик по заголовку компонента →
    /// «Подогнать коробку под тару».
    ///
    /// Коробка внутреннего объёма — единственное, что нельзя вычислить: где
    /// у тары стенки и какой они толщины, знает только модель. Выставлять её
    /// руками, глядя на четыре числа в инспекторе, — верный способ получить
    /// вертикальную плиту вместо дна, что и случилось.
    ///
    /// Метод берёт габариты мешей самой тары, ужимает их на толщину стенок и
    /// кладёт результат в BoxCollider. Дальше останется подвинуть на глаз по
    /// гизмо, а не собирать форму с нуля.
    /// </summary>
    [ContextMenu("Подогнать коробку под тару")]
    private void FitBoundsToVessel()
    {
        BoxCollider bc = _bounds != null ? _bounds : GetComponentInChildren<BoxCollider>();

        if (bc == null)
        {
            Debug.LogError($"[Паста] «{name}»: нет BoxCollider, подгонять нечего. " +
                           "Создай пустой дочерний объект и повесь на него BoxCollider.", this);
            return;
        }

        // Габариты мешей ТАРЫ: свою поверхность и продукты внутри исключаем,
        // иначе коробка вырастет по картошке, которая в ней лежит.
        Bounds world = new Bounds();
        bool any = false;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (r.transform.name.StartsWith("PasteSurface")) continue;
            if (r.GetComponentInParent<FoodItem>() != null) continue;

            if (!any) { world = r.bounds; any = true; }
            else world.Encapsulate(r.bounds);
        }

        if (!any)
        {
            Debug.LogWarning($"[Паста] «{name}»: у тары не нашлось мешей — " +
                             "подгонять не по чему.", this);
            return;
        }

        // Переводим в систему коллайдера. Размер делим на мировой масштаб:
        // BoxCollider.size задаётся в ЛОКАЛЬНЫХ единицах, и при масштабе
        // родителя в сотню раз мировые метры туда класть нельзя — именно так
        // и получаются числа вроде 0.007 при метровой банке.
        Vector3 ls = bc.transform.lossyScale;
        Vector3 localCenter = bc.transform.InverseTransformPoint(world.center);
        Vector3 localSize = new Vector3(
            world.size.x / Mathf.Max(1e-6f, Mathf.Abs(ls.x)),
            world.size.y / Mathf.Max(1e-6f, Mathf.Abs(ls.y)),
            world.size.z / Mathf.Max(1e-6f, Mathf.Abs(ls.z)));

        // Ужимаем на стенки. По плану сильнее, по высоте почти нет: дно у
        // тары одно, а бортов четыре.
        int up = _upAxis == Axis.Auto ? DetectUpAxisFor(bc, false) : (int)_upAxis - 1;

        for (int i = 0; i < 3; i++)
            localSize[i] *= (i == up) ? 0.92f : 0.86f;

        bc.center = localCenter;
        bc.size = localSize;

        Debug.Log($"[Паста] «{name}»: коробка подогнана — center {localCenter}, " +
                  $"size {localSize}, ось «вверх» " +
                  $"{(up == 0 ? "X" : up == 1 ? "Y" : "Z")}. " +
                  "Проверь по гизмо и подвинь, если надо.", this);
    }

    private void Awake()
    {
        _vessel = GetComponent<CookingVessel>();

        if (_bounds == null) _bounds = GetComponentInChildren<BoxCollider>();

        if (_bounds == null)
        {
            Debug.LogError($"[Паста] «{name}»: не найден BoxCollider внутреннего " +
                           "объёма. Без него неизвестно, где дно и где борта.", this);
            enabled = false;
            return;
        }

        MeasureBounds();
        BuildMesh();
    }

    /// <summary>
    /// Размеры берём В СИСТЕМЕ САМОГО КОЛЛАЙДЕРА, а не ёмкости.
    ///
    /// Коробку ты растягивал именно в его осях, и считать высоту логично там
    /// же. Раньше плоскость строилась по локальным X/Z ёмкости — и если её
    /// модель повёрнута (Blender отдаёт Z-вверх), паста ложилась вертикальной
    /// стеной вместо дна. Ровно это и было видно.
    /// </summary>
    private void MeasureBounds()
    {
        Vector3 size = _bounds.size;   // локальные размеры коллайдера

        _up = _upAxis == Axis.Auto ? DetectUpAxis() : (int)_upAxis - 1;

        int up = _up;
        int a = (up + 1) % 3;
        int b = (up + 2) % 3;

        _sizeX = size[a] * (1f - _inset * 2f);
        _sizeZ = size[b] * (1f - _inset * 2f);
        _wallH = size[up];
        _halfMin = Mathf.Max(0.01f, Mathf.Min(_sizeX, _sizeZ) * 0.5f);

        // Дно в локальных координатах КОЛЛАЙДЕРА.
        _floorLocal = _bounds.center;
        _floorLocal[up] -= _wallH * 0.5f;
    }

    /// <summary>
    /// ПЕРЕСМОТРЕТЬ ОСЬ, пока тара пуста.
    ///
    /// Определять её один раз в Awake оказалось нельзя. Тара — СЕТЕВОЙ ПРЕФАБ:
    /// на момент Awake поворот объекта ещё может быть не применён, компонент
    /// видит нулевой разворот, выбирает Y, а через кадр объект доворачивается,
    /// и Y оказывается горизонтальной. Паста встаёт стеной, и по инспектору
    /// причину не видно — там всё правильно.
    ///
    /// Пересматриваем, пока в таре ПУСТО: пустой посуде нечего
    /// переориентировать. Как только появилась первая кучка, ось замирает —
    /// иначе перевёрнутое ведро разложило бы пасту по новой стенке, чего от
    /// содержимого никто не ждёт.
    /// </summary>
    private void EnsureAxis()
    {
        if (_axisLocked || _upAxis != Axis.Auto) return;

        if (_blobs.Count > 0 || _forming.Count > 0) { _axisLocked = true; return; }

        int best = DetectUpAxis(false);
        if (best == _up) return;

        Debug.Log($"[Паста] «{name}»: ось «вверх» уточнена — было " +
                  $"{AxisName(_up)}, стало {AxisName(best)}. " +
                  "Так бывает, когда префаб доворачивается после Awake.", this);

        _up = best;
        MeasureBounds();
        RebuildPlan();
    }

    private static string AxisName(int i) => i == 0 ? "X" : i == 1 ? "Y" : "Z";

    /// <summary>
    /// Пересчитать положение вершин в плане после смены оси. Меш не
    /// пересоздаём: треугольники и UV те же, меняется только раскладка
    /// координат.
    /// </summary>
    private void RebuildPlan()
    {
        int n = Mathf.Clamp(_resolution, 8, 64);
        int side = n + 1;

        for (int z = 0; z <= n; z++)
        {
            for (int x = 0; x <= n; x++)
            {
                int i = z * side + x;
                float fx = x / (float)n - 0.5f;
                float fz = z / (float)n - 0.5f;

                _plan[i] = new Vector2(fx * _sizeX, fz * _sizeZ);
                _verts[i] = MakeLocal(_plan[i].x, _plan[i].y, 0f);
            }
        }

        _mesh.vertices = _verts;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    /// <summary>
    /// Какая локальная ось коллайдера смотрит в мировой верх.
    ///
    /// Ось у тары не вопрос вкуса, а факт: содержимое лежит на дне, дно внизу.
    /// Подбирать её руками — значит четыре раза перезапустить сцену и всё
    /// равно ошибиться, потому что модели приходят из Blender то Y-вверх,
    /// то Z-вверх, и по инспектору это не видно.
    /// </summary>
    private int DetectUpAxis(bool log = true) => DetectUpAxisFor(_bounds, log);

    private int DetectUpAxisFor(BoxCollider bounds, bool log = true)
    {
        Transform t = bounds.transform;
        Vector3[] axes = { t.right, t.up, t.forward };

        int best = 1;
        float bestDot = -2f;

        for (int i = 0; i < 3; i++)
        {
            float d = Vector3.Dot(axes[i].normalized, Vector3.up);

            // По модулю: ось может смотреть вниз, и тогда она всё равно «та
            // самая», просто коробка развёрнута. Знак учитывать не надо —
            // высота считается от центра коллайдера в обе стороны одинаково.
            if (Mathf.Abs(d) > bestDot) { bestDot = Mathf.Abs(d); best = i; }
        }

        if (log)
            Debug.Log($"[Паста] «{name}»: ось «вверх» определена как " +
                      $"{AxisName(best)} (совпадение с мировым верхом " +
                      $"{bestDot * 100f:0}%). Если паста всё равно встала стеной — " +
                      "задай ось вручную.", this);

        return best;
    }

    /// <summary>Собрать точку в локальных координатах коллайдера из плана и
    /// высоты. Единственное место, где ось «вверх» превращается в индекс.</summary>
    private Vector3 MakeLocal(float planA, float planB, float height)
    {
        int up = _up;
        Vector3 v = _floorLocal;
        v[up] += height;
        v[(up + 1) % 3] += planA;
        v[(up + 2) % 3] += planB;
        return v;
    }

    /// <summary>Мировая точка в план коллайдера.</summary>
    private Vector2 WorldToPlan(Vector3 world)
    {
        Vector3 l = _bounds.transform.InverseTransformPoint(world) - _floorLocal;
        int up = _up;
        return new Vector2(l[(up + 1) % 3], l[(up + 2) % 3]);
    }

    /// <summary>Сетка строится один раз, дальше только двигаются вершины:
    /// пересобирать меш каждый кадр значило бы каждый кадр кормить сборщик
    /// мусора, а меняется только высота.</summary>
    private void BuildMesh()
    {
        // Вешаем на КОЛЛАЙДЕР, а не на ёмкость: тогда локальные координаты
        // меша совпадают с теми, в которых мы считаем план и высоту, и
        // поворот модели ёмкости больше ни на что не влияет.
        GameObject go = new GameObject("PasteSurface (auto)");
        go.transform.SetParent(_bounds.transform, false);

        _filter = go.AddComponent<MeshFilter>();
        _renderer = go.AddComponent<MeshRenderer>();
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (_material != null) _renderer.sharedMaterial = _material;
        _mpb = new MaterialPropertyBlock();

        int n = Mathf.Clamp(_resolution, 8, 64);
        int side = n + 1;

        _verts = new Vector3[side * side];
        _plan = new Vector2[side * side];
        _colors = new Color[side * side];
        for (int i = 0; i < _colors.Length; i++) _colors[i] = new Color(0f, 0f, 0f, 1f);
        Vector2[] uv = new Vector2[side * side];

        for (int z = 0; z <= n; z++)
        {
            for (int x = 0; x <= n; x++)
            {
                int i = z * side + x;
                float fx = x / (float)n - 0.5f;
                float fz = z / (float)n - 0.5f;

                _plan[i] = new Vector2(fx * _sizeX, fz * _sizeZ);
                _verts[i] = MakeLocal(_plan[i].x, _plan[i].y, 0f);
                uv[i] = new Vector2(fx + 0.5f, fz + 0.5f);
            }
        }

        int[] tris = new int[n * n * 6];
        int t = 0;
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = z * side + x;
                tris[t++] = i;
                tris[t++] = i + side;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + side;
                tris[t++] = i + side + 1;
            }
        }

        _mesh = new Mesh { name = "PasteSurface" };
        _mesh.MarkDynamic();
        _mesh.vertices = _verts;
        _mesh.uv = uv;
        _mesh.colors = _colors;
        _mesh.triangles = tris;
        _mesh.RecalculateNormals();

        _filter.sharedMesh = _mesh;
    }

    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
        if (_filter != null) Destroy(_filter.gameObject);
    }

    // ───────────────────────── ВНЕШНИЙ ВХОД ─────────────────────────

    /// <summary>
    /// Положить кучку в этом месте. Зовётся, когда продукт растворился в таре.
    ///
    /// Место берём то, где лежал продукт, а не центр тары: смысл всей затеи в
    /// том, что кучки появляются там, где долбили, и только потом сползаются.
    /// </summary>
    public void AddDeposit(Vector3 worldPos, float massG)
    {
        if (massG <= 0f) return;

        Vector2 raw = WorldToPlan(worldPos);

        Vector2 p = new Vector2(
            Mathf.Clamp(raw.x, -_sizeX * 0.5f, _sizeX * 0.5f),
            Mathf.Clamp(raw.y, -_sizeZ * 0.5f, _sizeZ * 0.5f));

        _blobs.Add(new Blob
        {
            Pos = p,
            Mass = massG,
            Radius = _blobRadius * _halfMin
        });

        _knownMassG += massG;
        _lastImpulseTime = Time.time;

        // Свежая кучка шлёпнулась — вся паста вздрогнула.
        AddImpulse(0.6f);

        TrimBlobs();
    }

    /// <summary>
    /// Толкнуть пасту. Зовётся инструментом на каждый удар.
    ///
    /// Импульс раздаётся ТРЁМ модам с разным весом: низкая качает поверхность
    /// целиком, высокие дают рябь помельче. Бить в одну моду значило бы
    /// получить ровную синусоиду по всей таре — так не трясётся ничто.
    /// </summary>
    public void AddImpulse(float strength = 1f)
    {
        _lastImpulseTime = Time.time;

        // Жидкая отзывается сильнее: густую попробуй ещё расшевели.
        float soft = Mathf.Lerp(1.6f, 0.7f, _thickness);

        _vel[0] += strength * soft;
        _vel[1] += strength * soft * 0.6f * (Random.value < 0.5f ? -1f : 1f);
        _vel[2] += strength * soft * 0.35f * (Random.value < 0.5f ? -1f : 1f);
    }

    // ───────────────────────── ЦИКЛ ─────────────────────────

    private void LateUpdate()
    {
        if (_vessel == null || _mesh == null) return;

        EnsureAxis();
        UpdateThickness();
        SyncMassWithVessel();
        UpdateForming();

        bool show = (_blobs.Count > 0 && _vessel.MassKg > 0.0001f) || _forming.Count > 0;
        if (_renderer.enabled != show) _renderer.enabled = show;
        if (!show) return;

        FlowBlobs();
        UpdateJiggle();
        Deform();
        ApplyColor();
    }

    /// <summary>
    /// Масса в ёмкости может измениться мимо AddDeposit: налили воды, отсыпали
    /// в миску, загрузились в готовую сцену. Разницу раскидываем по уже
    /// лежащим кучкам, а если их нет — кладём одну в центр.
    ///
    /// Без этого паста показывала бы старый объём, пока в неё не бросят ещё
    /// картофелину.
    /// </summary>
    private void SyncMassWithVessel()
    {
        float actual = _vessel.MassKg * 1000f;
        float diff = actual - _knownMassG;

        if (Mathf.Abs(diff) < 1f) return;

        if (_blobs.Count == 0)
        {
            if (actual <= 0f) { _knownMassG = 0f; return; }

            _blobs.Add(new Blob
            {
                Pos = Vector2.zero,
                Mass = actual,
                Radius = _blobRadius * _halfMin
            });
            _knownMassG = actual;
            return;
        }

        // Раскидываем пропорционально: долитая вода расходится по всей пасте,
        // а не садится отдельной лужей поверх одной кучки.
        float k = _knownMassG > 0f ? actual / _knownMassG : 1f;

        for (int i = 0; i < _blobs.Count; i++)
        {
            Blob b = _blobs[i];
            b.Mass *= k;
            _blobs[i] = b;
        }

        _knownMassG = actual;

        if (actual <= 0f) _blobs.Clear();
    }

    /// <summary>
    /// Кучки, которые ЕЩЁ РАСТУТ под недоработанными продуктами.
    ///
    /// Картофелина мнётся до конца и исчезает рывком, а кучка появляется из
    /// ниоткуда — переход видно, и он выглядит дёшево. Здесь последняя треть
    /// долбления уже наращивает горку под предметом: к моменту растворения
    /// она почти готова, и одно перетекает в другое.
    ///
    /// Пересчитывается каждый кадр заново, а не копится: прогресс продукта
    /// уже синхронизирован, значит у всех получится одно и то же само собой,
    /// без единого пакета по сети.
    /// </summary>
    private void UpdateForming()
    {
        if (Time.time >= _nextFormCheck)
        {
            _nextFormCheck = Time.time + Mathf.Max(0.05f, _formCheckEvery);
            _forming.Clear();

            Vector3 center = _bounds.transform.TransformPoint(_bounds.center);
            float r = Mathf.Max(_sizeX, _sizeZ) * 0.5f + _wallH;

            int n = Physics.OverlapSphereNonAlloc(center, r, _formHits, ~0,
                                                  QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                if (_formHits[i] == null) continue;

                FoodItem f = _formHits[i].GetComponentInParent<FoodItem>();
                if (f == null || f.Consumed || f.Progress01 <= _formFrom) continue;

                bool dup = false;
                for (int k = 0; k < _forming.Count; k++)
                    if (_forming[k].Item == f) { dup = true; break; }
                if (dup) continue;

                // Массу берём из справочника: настоящая порция живёт на
                // сервере, а горку рисуют все. Для превью точность не нужна —
                // к моменту растворения её заменит настоящая кучка.
                float mass = f.Ingredient != null ? f.Ingredient.UnitMassG : 200f;

                _forming.Add(new Forming
                {
                    Item = f,
                    Pos = WorldToPlan(f.transform.position),
                    Mass = mass,
                    Radius = _blobRadius * _halfMin
                });
            }
        }

        // Позиции и объём подтягиваем КАЖДЫЙ кадр: продукт под молотком
        // подрагивает и оседает, и горка должна ехать за ним.
        for (int i = _forming.Count - 1; i >= 0; i--)
        {
            Forming f = _forming[i];

            if (f.Item == null || f.Item.Consumed || f.Item.Progress01 <= _formFrom)
            {
                _forming.RemoveAt(i);
                continue;
            }

            f.Pos = WorldToPlan(f.Item.transform.position);
            _forming[i] = f;
        }
    }

    /// <summary>Какая доля массы продукта уже осела в тару.</summary>
    private float FormShare(FoodItem item)
    {
        if (item == null) return 0f;
        return Mathf.Clamp01((item.Progress01 - _formFrom) / Mathf.Max(0.01f, 1f - _formFrom));
    }

    /// <summary>
    /// Кучки стекаются к общему центру тяжести и расплываются.
    ///
    /// Скорость завязана на густоту: жижа растекается за секунды, глина стоит
    /// кучками долго. Это и есть видимая разница между пюре и фаршем — без
    /// единого нового ассета.
    /// </summary>
    private void FlowBlobs()
    {
        if (_blobs.Count == 0) return;

        float dt = Mathf.Min(Time.deltaTime, 0.1f);

        // В КВАДРАТЕ, а не линейно. При линейной зависимости даже глина
        // сохраняла шестую часть подвижности и за минуту всё равно
        // растекалась в лужу — просто медленно. Квадрат оставляет ей
        // сотые доли, то есть она стоит.
        float mobility = 1f - _thickness;
        mobility *= mobility;

        // Общий центр тяжести — цель, к которой всё сползается.
        Vector2 centroid = Vector2.zero;
        float total = 0f;
        for (int i = 0; i < _blobs.Count; i++)
        {
            centroid += _blobs[i].Pos * _blobs[i].Mass;
            total += _blobs[i].Mass;
        }
        if (total <= 0f) return;
        centroid /= total;

        // Предел растекания тоже зависит от густоты: глина оседает чуть шире
        // своей кучки и на этом всё, жижа разливается по всей таре.
        float baseR = _blobRadius * _halfMin;
        float limit = Mathf.Lerp(_spreadLimit * _halfMin, baseR * 1.15f, _thickness);

        for (int i = 0; i < _blobs.Count; i++)
        {
            Blob b = _blobs[i];

            b.Pos = Vector2.Lerp(b.Pos, centroid, 1f - Mathf.Exp(-_flowSpeed * mobility * dt));
            b.Radius = Mathf.Lerp(b.Radius, limit, 1f - Mathf.Exp(-_spreadSpeed * mobility * dt));

            _blobs[i] = b;
        }

        MergeClose();
    }

    /// <summary>
    /// Слить наехавшие друг на друга кучки в одну.
    ///
    /// Визуально они и так сливаются суммой высот — это чисто про счёт: каждая
    /// кучка считается на КАЖДОЙ вершине сетки, и двадцать кучек на 625 вершин
    /// уже заметны в профайлере.
    /// </summary>
    private void MergeClose()
    {
        for (int i = 0; i < _blobs.Count; i++)
        {
            for (int j = _blobs.Count - 1; j > i; j--)
            {
                Blob a = _blobs[i];
                Blob b = _blobs[j];

                float need = (a.Radius + b.Radius) * _mergeAt;
                if ((a.Pos - b.Pos).sqrMagnitude > need * need) continue;

                float m = a.Mass + b.Mass;
                if (m <= 0f) continue;

                // Позиция по массе: тяжёлая кучка почти не сдвинется, приняв
                // в себя мелкую.
                a.Pos = (a.Pos * a.Mass + b.Pos * b.Mass) / m;
                a.Radius = Mathf.Max(a.Radius, b.Radius);
                a.Mass = m;

                _blobs[i] = a;
                _blobs.RemoveAt(j);
            }
        }
    }

    /// <summary>Принудительно слить самые мелкие, если кучек стало слишком много.</summary>
    private void TrimBlobs()
    {
        while (_blobs.Count > _maxBlobs)
        {
            int small = 0;
            for (int i = 1; i < _blobs.Count; i++)
                if (_blobs[i].Mass < _blobs[small].Mass) small = i;

            // Ищем ближайшую к ней и вливаем.
            int near = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _blobs.Count; i++)
            {
                if (i == small) continue;
                float d = (_blobs[i].Pos - _blobs[small].Pos).sqrMagnitude;
                if (d < best) { best = d; near = i; }
            }

            if (near < 0) break;

            Blob a = _blobs[near];
            Blob b = _blobs[small];
            float m = a.Mass + b.Mass;

            a.Pos = m > 0f ? (a.Pos * a.Mass + b.Pos * b.Mass) / m : a.Pos;
            a.Mass = m;
            a.Radius = Mathf.Max(a.Radius, b.Radius);

            _blobs[near] = a;
            _blobs.RemoveAt(small);
        }
    }

    /// <summary>
    /// Густота из состава: доля жидких фракций в общей массе.
    ///
    /// Берём категорию ингредиента, а не отдельное поле «вязкость»: категория
    /// Liquid в справочнике уже есть и заполнена, а новое поле пришлось бы
    /// проставлять руками у каждого продукта — и обязательно где-нибудь забыть.
    /// </summary>
    private void UpdateThickness()
    {
        var view = _vessel.View;
        if (view == null || view.Count == 0) return;

        FoodRegistry reg = FoodRegistry.Instance;
        float liquid = 0f, total = 0f;

        for (int i = 0; i < view.Count; i++)
        {
            int m = view[i].MassG;
            if (m <= 0) continue;

            total += m;

            IngredientDef def = reg != null ? reg.GetIngredient(view[i].IngredientId) : null;
            if (def != null && def.Category == IngredientCategory.Liquid) liquid += m;
        }

        if (total <= 0f) return;

        float ratio = liquid / total;
        float target = 1f - Mathf.Clamp01(ratio / Mathf.Max(0.01f, _liquidForThin));

        _thickness = Mathf.Lerp(_thickness, target,
                                1f - Mathf.Exp(-_thicknessLerp * Time.deltaTime));
    }

    /// <summary>
    /// Пружины. Каждая мода — затухающий осциллятор.
    ///
    /// Затухание по ЭКСПОНЕНТЕ, а не вычитанием: множитель (1 - damp*dt) на
    /// просадке кадров уходит в минус, и амплитуда начинает каждый кадр менять
    /// знак — поверхность дребезжит сама по себе. Та же ошибка, что когда-то
    /// трясла зону в мини-игре плиты.
    /// </summary>
    private void UpdateJiggle()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        float freq = Mathf.Lerp(_jiggleFreqThin, _jiggleFreqThick, _thickness);
        float damp = Mathf.Lerp(_dampThin, _dampThick, _thickness);

        for (int m = 0; m < 3; m++)
        {
            // Высокие моды звенят чаще и гаснут быстрее — как и положено.
            float w = 2f * Mathf.PI * freq * (1f + m * 0.8f);
            float d = damp * (1f + m * 0.5f);

            _vel[m] += -w * w * _amp[m] * dt;
            _vel[m] *= Mathf.Exp(-d * dt);
            _amp[m] += _vel[m] * dt;
        }
    }

    /// <summary>
    /// Собрать высоту каждой вершины: сумма кучек, пики, джигл, дрожь.
    ///
    /// Всё в локальных координатах ёмкости, поэтому паста наклоняется и едет
    /// вместе с тарой сама собой — отдельно её положение поддерживать не нужно.
    /// </summary>
    private void Deform()
    {
        float peak = _peakHeight * _thickness;

        bool tremor = Time.time - _lastImpulseTime < _tremorHold;
        float tremorAmp = tremor ? _tremor * _wallH * Mathf.Lerp(0.4f, 1f, _thickness) : 0f;
        float tt = Time.time * _tremorSpeed;

        // Высота от РАЗМЕРА ПОРЦИИ, а не от вместимости тары: иначе одна и
        // та же картофелина в тазике выглядит горой, а в чане — плёнкой.
        float perGram = _blobHeight * _wallH / Mathf.Max(1f, _gramsForBlobHeight);
        float baseR = _blobRadius * _halfMin;

        _maxHeightShown = 0f;

        for (int i = 0; i < _verts.Length; i++)
        {
            Vector2 p = _plan[i];
            float h = 0f;

            // СУММА КУЧЕК — то самое метаболное слияние: там, где две кучки
            // перекрываются, высоты складываются и между ними вырастает
            // перемычка. Отдельного кода на слияние поверхностей не нужно.
            for (int b = 0; b < _blobs.Count; b++)
            {
                Blob bl = _blobs[b];
                float d2 = (p - bl.Pos).sqrMagnitude;
                float r2 = bl.Radius * bl.Radius;
                if (d2 >= r2) continue;

                // ОБЪЁМ СОХРАНЯЕТСЯ: расплылась вширь — осела по высоте.
                // Без этого кучка, растекаясь, оставалась прежней высоты и
                // превращалась в ровное плато на всю тару — ту самую лужу.
                float shrink = (baseR * baseR) / Mathf.Max(1e-9f, r2);

                // Гладкий колокол с компактным носителем: за радиусом ровно
                // ноль, без хвоста до края тары.
                float k = 1f - d2 / r2;
                h += bl.Mass * perGram * shrink * k * k;
            }

            // Растущие кучки под недоработанными продуктами — тем же
            // колоколом, поэтому они сливаются с готовыми без шва.
            for (int b = 0; b < _forming.Count; b++)
            {
                Forming fm = _forming[b];
                float share = FormShare(fm.Item);
                if (share <= 0.001f) continue;

                float d2 = (p - fm.Pos).sqrMagnitude;
                float r2 = fm.Radius * fm.Radius * share;
                if (r2 < 1e-6f || d2 >= r2) continue;

                float k = 1f - d2 / r2;
                h += fm.Mass * share * perGram * k * k;
            }

            // Голая высота ОТ КУЧЕК — по ней и решается, есть ли тут паста.
            // Пики и джигл в счёт не идут: иначе рябь на пустом дне
            // прорисовывалась бы отдельными островками.
            float bare = h;

            // Насколько толстый слой у этой вершины. У края кучки паста
            // сходит на нет — там она темнее и глянцевее.
            float cover = Mathf.Clamp01(bare / Mathf.Max(1e-5f, _wallH * _edgeFade));

            if (h > 0.0001f)
            {
                // КОМКИ. Ровный купол выглядит вылитым из формы, комковатый —
                // размятым. Прижимаем покрытием, чтобы у края они не торчали
                // зубцами над пустотой.
                // Координаты делим на размер кучки: тогда «5 комков поперёк»
                // значит ровно это, а не зависит от того, в каких единицах
                // собрана модель тары.
                float ns = 1f / Mathf.Max(1e-5f, _halfMin);
                float lump = Noise(p.x * ns * _lumpScale + 7.3f,
                                   p.y * ns * _lumpScale - 3.1f);
                h += (lump - 0.5f) * _lumpHeight * _wallH * _thickness * cover;

                // Пики стоят НА МЕСТЕ, а не бегут: паста держит след
                // инструмента. Двигать их шумом по времени значило бы показать
                // кипящую жижу.
                h += Noise(p.x * ns * _peakScale, p.y * ns * _peakScale)
                     * peak * _wallH * cover;

                float nx = p.x / (_sizeX * 0.5f);
                float nz = p.y / (_sizeZ * 0.5f);

                // Джигл: моды с разной пространственной формой, иначе вся
                // поверхность ходила бы вверх-вниз одной плитой.
                h += _amp[0] * _jiggleGain * _wallH;
                h += _amp[1] * _jiggleGain * _wallH * 0.6f * Mathf.Cos(nx * Mathf.PI);
                h += _amp[2] * _jiggleGain * _wallH * 0.4f * Mathf.Cos(nz * Mathf.PI * 1.5f);

                if (tremorAmp > 0f) h += Mathf.Sin(tt + (p.x + p.y) * 40f) * tremorAmp;
            }

            h = Mathf.Max(0f, h);

            // ВЕРШИННЫЕ ЦВЕТА — то, по чему шейдер обрезает край.
            // R: есть ли тут паста вообще. Где ноль — фрагмент выбрасывается,
            //    и вместо бежевого квадрата на всю тару остаются сами кучки.
            // G: толщина слоя. По ней красятся впадины и подбирается блеск.
            float depth = Mathf.Clamp01(bare / Mathf.Max(1e-5f, _wallH * 0.5f));
            _colors[i] = new Color(_debugSolid ? 1f : cover, depth, 0f, 1f);

            if (bare > _maxHeightShown) _maxHeightShown = bare;

            // ПРИЖИМАЕМ К СЕЧЕНИЮ ТАРЫ. Вершина, попавшая за наклонную стенку
            // на своей высоте, подтягивается внутрь — иначе паста торчит
            // сквозь борта, и вместо пюре в банке видно коричневые стены.
            float sec = SectionAt(_wallH > 0f ? h / _wallH : 0f);
            Vector2 fit = p * sec;

            _verts[i] = MakeLocal(fit.x, fit.y, h);
        }

        _mesh.vertices = _verts;
        _mesh.colors = _colors;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    /// <summary>Две октавы шума. Одна даёт ровные волны, три уже не видно, а
    /// считать дороже.</summary>
    private float Noise(float x, float z)
    {
        float v = 0f, amp = 1f, freq = 1f, norm = 0f;

        for (int o = 0; o < _peakOctaves; o++)
        {
            v += Mathf.PerlinNoise(x * freq + 13.7f, z * freq + 41.3f) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2.3f;
        }
        return norm > 0f ? v / norm : 0f;
    }

    /// <summary>
    /// Цвет из состава по массе. Смешивание СУБТРАКТИВНОЕ — тем же способом,
    /// что в PotLiquidVisual: продукты гасят свет, а не усредняют яркость, и
    /// линейный Lerp свёклы с картошкой дал бы серую муть вместо розового.
    /// </summary>
    private void ApplyColor()
    {
        var view = _vessel.View;
        if (view == null || view.Count == 0) return;

        FoodRegistry reg = FoodRegistry.Instance;
        Vector3 absorb = Vector3.zero;
        float mass = 0f;

        for (int i = 0; i < view.Count; i++)
        {
            int m = view[i].MassG;
            if (m <= 0) continue;

            IngredientDef def = reg != null ? reg.GetIngredient(view[i].IngredientId) : null;
            Color c = def != null && def.DebrisColor.a > 0.01f ? def.DebrisColor : _fallbackColor;

            absorb += new Vector3(-Mathf.Log(Mathf.Max(c.r, 0.004f)),
                                  -Mathf.Log(Mathf.Max(c.g, 0.004f)),
                                  -Mathf.Log(Mathf.Max(c.b, 0.004f))) * m;
            mass += m;
        }

        if (mass <= 0f) return;

        absorb /= mass;
        Color mixed = new Color(Mathf.Exp(-absorb.x), Mathf.Exp(-absorb.y), Mathf.Exp(-absorb.z));

        // Растёртая масса СВЕТЛЕЕ целого продукта: пюре светлее картофелины,
        // фарш светлее куска мяса. Тянем к белому, а не поднимаем яркость
        // покомпонентно — иначе насыщенные цвета вроде свёклы выгорают в
        // неон, а тёмные остаются тёмными.
        mixed = Color.Lerp(mixed, Color.white, _lighten);

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(ColorId, mixed);
        _mpb.SetColor(ColorLegacyId, mixed);

        // Густоту отдаём шейдеру: от неё зависит блеск, резкость кусочков и
        // глубина просвета. Если материал на обычном Lit, свойства просто нет
        // и запись пропадёт впустую — это безвредно.
        _mpb.SetFloat(ThicknessId, _thickness);

        // Шейдер считает крупу в объектных координатах, а они у тары — тысячные
        // доли. Отдаём ему нормировку на размер кучки, иначе «90 крупинок»
        // превращаются в одно ровное пятно на всю поверхность.
        _mpb.SetFloat(NoiseScaleId, 1f / Mathf.Max(1e-5f, _halfMin));

        _renderer.SetPropertyBlock(_mpb);
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void OnGUI()
    {
        if (!_debugHud) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 anchor = transform.position + Vector3.up * _wallH;
        if (Vector3.Distance(cam.transform.position, anchor) > 5f) return;

        Vector3 sp = cam.WorldToScreenPoint(anchor);
        if (sp.z <= 0f) return;

        // Высоту показываем В ДОЛЯХ БОРТА: в локальных единицах коллайдера
        // число вроде 0.0004 ни о чём не говорит, а «8% борта» говорит всё.
        float rel = _wallH > 0f ? _maxHeightShown / _wallH : 0f;

        string txt = $"ПАСТА\n" +
                     $"масса {_vessel.MassKg:0.00} кг, кучек {_blobs.Count}\n" +
                     $"высота горки {rel * 100f:0.0}% борта " +
                     $"(обрезка ниже {_edgeFade * 100f:0.0}%)\n" +
                     $"густота {_thickness * 100f:0}% " +
                     $"({(_thickness > 0.7f ? "глина" : _thickness > 0.35f ? "паста" : "жижа")})\n" +
                     $"джигл {_amp[0]:0.00} / {_amp[1]:0.00} / {_amp[2]:0.00}";

        GUIStyle st = new GUIStyle(GUI.skin.box) { fontSize = 11, alignment = TextAnchor.UpperLeft };
        Vector2 size = st.CalcSize(new GUIContent(txt));
        GUI.Box(new Rect(sp.x + 20f, Screen.height - sp.y, size.x + 12f, size.y + 8f), txt, st);
    }

    private void OnDrawGizmosSelected()
    {
        // Рамку рисуем ВСЕГДА, даже в редакторе без запуска: по ней и
        // выравнивается BoxCollider внутреннего объёма. Подгонять его вслепую,
        // а потом гадать, почему паста лезет сквозь стенки, — то ещё занятие.
        BoxCollider bc = _bounds != null ? _bounds : GetComponentInChildren<BoxCollider>();
        if (bc != null)
        {
            int up = Application.isPlaying
                ? _up
                : (_upAxis == Axis.Auto ? 1 : (int)_upAxis - 1);
            int ia = (up + 1) % 3, ib = (up + 2) % 3;

            Vector3 sz = bc.size;
            float ins = 1f - _inset * 2f;

            Vector3 floorL = bc.center;
            floorL[up] -= sz[up] * 0.5f;

            Vector3 floor = bc.transform.TransformPoint(floorL);
            Vector3 upW = bc.transform.TransformDirection(AxisVec(up)) * sz[up]
                        * bc.transform.lossyScale[up] / Mathf.Max(1e-4f, bc.transform.localScale[up]);

            float sa = sz[ia] * bc.transform.lossyScale[ia];
            float sb = sz[ib] * bc.transform.lossyScale[ib];

            float bx = sa * ins * _bottomScale, bz = sb * ins * _bottomScale;
            float tx = sa * ins, tz = sb * ins;

            Vector3 dirA = bc.transform.TransformDirection(AxisVec(ia)).normalized;
            Vector3 dirB = bc.transform.TransformDirection(AxisVec(ib)).normalized;

            Gizmos.color = new Color(0.4f, 1f, 0.9f, 0.9f);
            Vector3 top = floor + upW;

            DrawRect(floor, dirA, dirB, bx, bz);   // дно, у́же
            DrawRect(top, dirA, dirB, tx, tz);     // верх, шире

            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz2 = -1; sz2 <= 1; sz2 += 2)
                    Gizmos.DrawLine(
                        floor + dirA * (bx * 0.5f * sx) + dirB * (bz * 0.5f * sz2),
                        top + dirA * (tx * 0.5f * sx) + dirB * (tz * 0.5f * sz2));
        }

        if (!Application.isPlaying || _blobs.Count == 0) return;

        Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.6f);
        for (int i = 0; i < _blobs.Count; i++)
        {
            Vector3 c = _bounds.transform.TransformPoint(
                MakeLocal(_blobs[i].Pos.x, _blobs[i].Pos.y, 0.01f));
            Gizmos.DrawWireSphere(c, _blobs[i].Radius);
        }
    }

    private static Vector3 AxisVec(int i)
        => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

    private static void DrawRect(Vector3 center, Vector3 dirA, Vector3 dirB,
                                 float sa, float sb)
    {
        Vector3 a = center + dirA * (-sa * 0.5f) + dirB * (-sb * 0.5f);
        Vector3 b = center + dirA * (sa * 0.5f) + dirB * (-sb * 0.5f);
        Vector3 c = center + dirA * (sa * 0.5f) + dirB * (sb * 0.5f);
        Vector3 d = center + dirA * (-sa * 0.5f) + dirB * (sb * 0.5f);

        Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
    }
}
