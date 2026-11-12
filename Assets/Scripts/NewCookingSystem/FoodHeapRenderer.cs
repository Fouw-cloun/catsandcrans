using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ВОКСЕЛЬНАЯ КУЧКА — только визуал, у каждого игрока свой.
    ///
    /// Строит один меш из кубиков: горка тёртой картошки, пюре с кусочками,
    /// фарш. Кубик — «воксель», чуть разного оттенка; часть кубиков крупнее и
    /// темнее — это недотёртые кусочки продукта.
    ///
    /// Раскладка зависит только от (порций, цвета, seed). Seed = Id сетевого
    /// объекта, поэтому у всех игроков кучка одинаковая до кубика — по сети
    /// летит одно число порций, а не позиции частиц.
    ///
    /// Меш перестраивается ТОЛЬКО при смене порций или цвета, не каждый кадр.
    ///
    /// ═══ ДВЕ ФОРМЫ ═══
    ///   Mound — горка на полу / в полёте: радиус растёт с объёмом.
    ///   Fill  — заполнение тары: основание задано (Footprint), растёт высота.
    ///
    /// Точка объекта = центр ОСНОВАНИЯ кучки (дно тары, пол).
    /// </summary>
    [ExecuteAlways]
    public sealed class FoodHeapRenderer : MonoBehaviour
    {
        public enum Shape : byte { Mound = 0, Fill = 1 }

        /// <summary>Из чего собрана кучка.</summary>
        public enum Grain : byte
        {
            [InspectorName("Кубики (крошка, фарш)")]          Cubes = 0,
            [InspectorName("Стружка (как с тёрки)")]           Shreds = 1,
            [InspectorName("Гладкая масса (пюре, соус)")]      Smooth = 2,
            [InspectorName("Метаболы + кусочки (тёртое, пюре с кусками)")] Blobby = 3,
            [InspectorName("Живые частицы (падают, скатываются, плещутся)")] Particles = 4,
        }

        public enum HeapPreset : byte
        {
            [InspectorName("Свои настройки")]                   Custom = 0,
            [InspectorName("1. Мягкие бруски (живые частицы)")] SoftSticks = 1,
            [InspectorName("2. Кубики (живые частицы)")]        Cubes = 2,
            [InspectorName("3. Труха полосками (объёмная стружка)")] GratedStrands = 3,
            [InspectorName("4. Пюре")]                          Puree = 4,
            [InspectorName("5. Порошок (закваска, сахар, мука, специи)")] Powder = 5,
            [InspectorName("6. Жидкость (лужица, капля на лопате)")] Liquid = 6,
            [InspectorName("7. Густая масса (сметана, масло, тесто)")] Dollop = 7,
            [InspectorName("8. Фарш (завитки, цвет продукта)")] Mince = 8,
            [InspectorName("9. Ньокки (рифлёные, по одному на порцию)")] Gnocchi = 9,
            [InspectorName("10. Целые клубни (картофель: одна на 6 порций)")] Whole = 10,
        }

        [Header("Пресет вида")]
        [Tooltip("Выбери — все поля ниже настроятся сами. Дальше меняешь только материалы. " +
                 "Если потом крутишь поля руками — пресет не мешает, он применяется один раз при выборе.")]
        [SerializeField] private HeapPreset _preset = HeapPreset.Custom;
        [SerializeField, HideInInspector] private HeapPreset _appliedPreset = HeapPreset.Custom;

        [Header("Хаос")]
        [Tooltip("0 — ровная аккуратная горка. 1 — бугры, наплывы, что-то торчит, рядом " +
                 "мелкие кучки и капли. Бугров и капель становится больше вместе с порциями.")]
        [Range(0f, 1f)][SerializeField] private float _chaos = 0.5f;
        [Tooltip("Макушка: комок, торчащий сверху (пюре «шапочкой»). 0 — нет.")]
        [Range(0f, 1f)][SerializeField] private float _peak = 0f;
        [Tooltip("Капли и мелкие кучки вокруг. Только для кучки на полу (в таре их нет).")]
        [SerializeField] private bool _satellites = true;

        [Header("Вязкость (пюре, каша)")]
        [Tooltip("Растекание: после падения или досыпки масса плавно становится шире и ниже. " +
                 "0 — держит форму (труха, крошка), 1 — жидкая каша.")]
        [Range(0f, 1f)][SerializeField] private float _flow = 0f;
        [Tooltip("Колыхание: масса пружинит, когда в неё сыплют, черпают или она падает")]
        [Range(0f, 1f)][SerializeField] private float _wobble = 0f;
        [Tooltip("Сколько секунд масса растекается до конца")]
        [Range(0.3f, 5f)][SerializeField] private float _flowTime = 1.6f;

        [Header("Капли с лопаты")]
        [Tooltip("Размер капли, доля шарика-метабола (масса) или стружки (частицы)")]
        [Range(0.2f, 2f)][SerializeField] private float _dropScale = 0.8f;

        [Header("Форма")]
        [SerializeField] private Shape _shape = Shape.Mound;
        [Tooltip("Кубики — крошка и фарш. Стружка — тёртая картошка, сыр, морковь. " +
                 "Гладкая масса — пюре, соус, тесто (узор даёт текстура).")]
        [SerializeField] private Grain _grain = Grain.Blobby;
        [Tooltip("Размер кубика, м. У тебя мир крупный (картошка со Scale 5) — начни с 0.05.")]
        [Min(0.002f)][SerializeField] private float _voxel = 0.05f;
        [Tooltip("Объём одной порции, м³. Больше — кучки крупнее.")]
        [Min(0.000001f)][SerializeField] private float _portionVolume = 0.004f;
        [Tooltip("Mound: высота горки к радиусу. 0.5 — пологая, 0.9 — острая.")]
        [Range(0.2f, 1.2f)][SerializeField] private float _slope = 0.55f;

        [Header("Заполнение тары (Fill)")]
        [Tooltip("Полуразмеры дна по X и Z, м. Кучка не вылезет за них.")]
        [SerializeField] private Vector2 _footprint = new Vector2(0.4f, 0.25f);
        [Tooltip("Горка в таре — по форме дна: прямоугольник со скруглёнными углами. " +
                 "Выкл — овал, как раньше.")]
        [SerializeField] private bool _fillRect = true;
        [Tooltip("Насколько прямоугольная: 2 — овал, 6 — скруглённый прямоугольник, 16 — почти острые углы")]
        [Range(2f, 20f)][SerializeField] private float _rectSharpness = 8f;
        [Tooltip("Плоскость верха в таре: 2 — купол, 4 — плоский верх и крутые края, 8 — почти брусок")]
        [Range(2f, 12f)][SerializeField] private float _topFlatness = 4f;
        [Tooltip("В таре — одна гладкая оболочка («пузырь») по форме дна вместо шариков-метаболов: " +
                 "без ступенек по высоте, ровные бока, горка сверху, в разы дешевле. " +
                 "Для пюре с комочками выключается само (комочкам нужны метаболы).")]
        [SerializeField] private bool _fillShell = true;
        [Tooltip("Лёгкая неровность оболочки, доля высоты — чтобы не была идеально гладкой")]
        [Range(0f, 0.1f)][SerializeField] private float _shellNoise = 0.02f;
        [Tooltip("Бугры сверху в таре. 0 — ровный верх (строго), 1 — как на полу.")]
        [Range(0f, 1f)][SerializeField] private float _trayChaos = 0f;
        [Tooltip("Высота края тары, м. До неё горка заполняет тару ровно.")]
        [Min(0.01f)][SerializeField] private float _maxHeight = 0.35f;
        [Tooltip("Насколько горка может подняться КУПОЛОМ над краем, доля от Max Height. " +
                 "0 — ровно по край, 0.6 — «с горкой». Выше — осыпается через борт.")]
        [Range(0f, 1.5f)][SerializeField] private float _aboveRim = 0.6f;
        [Tooltip("Расширение кверху (тара-трапеция: сковорода, конусное ведро): радиус на высоте Max Height ÷ радиус дна (Footprint). " +
                 "1 — ровный цилиндр, как раньше; 1.3 — у края на 30% шире дна")]
        [Range(0.5f, 2.5f)][SerializeField] private float _flareTop = 1f;

        [Header("Живые частицы")]
        [Tooltip("Размер одной стружки/кусочка, м (радиус столкновения). Мельче — точнее и дороже.")]
        [Range(0.004f, 0.08f)][SerializeField] private float _grainSize = 0.018f;
        [Tooltip("Сколько частиц на одну порцию")]
        [Range(4, 80)][SerializeField] private int _grainsPerPortion = 24;
        [Tooltip("Предел частиц в одной кучке. Больше порций — частицы крупнее.")]
        [Range(100, 4000)][SerializeField] private int _maxGrains = 1500;
        [Tooltip("Статическое трение: больше — круче горка. Тёртое ~1.2, крошка ~0.8, сыпучее ~0.5")]
        [Range(0.2f, 3f)][SerializeField] private float _grainStaticFriction = 1.2f;
        [Tooltip("Трение скольжения")]
        [Range(0.1f, 2f)][SerializeField] private float _grainFriction = 0.8f;
        [Tooltip("Насколько содержимое плещется, когда тару дёргают")]
        [Range(0f, 1f)][SerializeField] private float _grainInertia = 0.6f;
        [Tooltip("Разлёт при ударе о пол (брошенная лопатой кучка)")]
        [Range(0f, 3f)][SerializeField] private float _landingSplash = 1.2f;
        [Tooltip("Разброс места, куда сыплются новые частицы, м")]
        [Range(0f, 0.3f)][SerializeField] private float _pourSpread = 0.04f;
        [Tooltip("Проходов расталкивания за шаг: 2 — быстро, 4 — плотнее")]
        [Range(1, 5)][SerializeField] private int _grainIterations = 3;
        [Tooltip("Материал для самих стружек (с текстурой стружки, «UV меша» включено). " +
                 "Пусто — тот же, что у массы.")]
        [SerializeField] private Material _grainMaterial;
        [Tooltip("Отбрасывают ли стружки тень. Выкл — дешевле и без «грязи» из теней внутри горки, " +
                 "но и на полу тени от кучки не будет.")]
        [SerializeField] private bool _grainShadows = true;
        [Tooltip("Скругление граней стружки: 0 — острый брусок, 1 — почти цилиндр. " +
                 "Со скруглением свет обтекает края мягко, стружка кажется сочной.")]
        [Range(0f, 1f)][SerializeField] private float _grainRoundness = 0.6f;
        [Tooltip("Под стружками — плотная масса (текстура ленточек из поля Material). " +
                 "Для «натёртой в труху» картошки: стружки сверху, масса снизу.")]
        [SerializeField] private bool _underMass = true;
        [Tooltip("Размер массы относительно горки стружек")]
        [Range(0.3f, 1.1f)][SerializeField] private float _underMassScale = 0.85f;

        [Header("Объёмная стружка (ленточки на поверхности массы)")]
        [Tooltip("Тысячи изогнутых ленточек поверх массы — объёмная тёртая картошка. " +
                 "Работает с режимами «Метаболы», «Гладкая масса» и с подложкой живых частиц.")]
        [SerializeField] private bool _strands;
        [Tooltip("Материал на шейдере CatCooks/Food Strands")]
        [SerializeField] private Material _strandMaterial;
        [Tooltip("Ленточек на одну порцию")]
        [Range(20, 3000)][SerializeField] private int _strandsPerPortion = 400;
        [Tooltip("Больше этого не будет, сколько бы ни было порций")]
        [Range(1000, 200000)][SerializeField] private int _maxStrands = 40000;
        [Tooltip("Длина ленточки, м")]
        [SerializeField] private Vector2 _strandLength = new Vector2(0.03f, 0.07f);
        [Tooltip("Ширина ленточки, м")]
        [SerializeField] private Vector2 _strandWidth = new Vector2(0.006f, 0.011f);
        [Tooltip("Толщина слоя ленточек под поверхностью, м — даёт объём")]
        [Range(0f, 0.1f)][SerializeField] private float _strandLayer = 0.02f;
        [Tooltip("Доля ленточек, которые торчат наружу")]
        [Range(0f, 1f)][SerializeField] private float _strandStickOut = 0.25f;
        [Tooltip("Изгиб ленточки, радиан на всю длину")]
        [Range(0f, 4f)][SerializeField] private float _strandBend = 1.6f;
        [Tooltip("Закрутка ленточки, радиан")]
        [Range(0f, 3f)][SerializeField] private float _strandTwist = 1.2f;
        [Tooltip("Горбик над поверхностью, доля длины")]
        [Range(0f, 0.5f)][SerializeField] private float _strandArch = 0.12f;
        [Tooltip("Сегментов на ленточку: больше — плавнее изгиб, дороже")]
        [Range(2, 12)][SerializeField] private int _strandSegments = 6;
        [SerializeField] private bool _strandShadows = true;
        [Tooltip("Спрятать массу под ленточками — кучка целиком из ленточек (труха). " +
                 "Масса остаётся невидимой опорой: по её форме ложатся ленточки.")]
        [SerializeField] private bool _strandsHideMass;

        [Header("Комочки (пюре, каша)")]
        [Tooltip("Мелких комочков на поверхности на одну порцию. Кучка становится рыхлой, " +
                 "как манная каша с комками. 0 — гладкая масса.")]
        [Range(0, 200)][SerializeField] private int _microLumpsPerPortion = 0;
        [Tooltip("Размер комочка, доля шарика-метабола")]
        [Range(0.2f, 0.9f)][SerializeField] private float _microLumpSize = 0.45f;
        [Tooltip("Насколько комочки выступают над поверхностью, доля их размера")]
        [Range(0f, 1f)][SerializeField] private float _microLumpRaise = 0.35f;

        [Header("Метаболы")]
        [Tooltip("Размер шарика-метабола в кубиках. Больше — глаже и толще комки.")]
        [Range(0.8f, 3f)][SerializeField] private float _blobSize = 1.5f;
        [Tooltip("Разрешение поверхности: ячейка в долях кубика. Меньше — точнее, дороже.")]
        [Range(0.35f, 1f)][SerializeField] private float _surfaceCell = 0.55f;
        [Tooltip("Порог слипания. Меньше — шарики сливаются в одну массу, больше — комковато.")]
        [Range(0.1f, 0.8f)][SerializeField] private float _iso = 0.35f;

        public enum Pieces : byte
        {
            [InspectorName("Нет")]                      None = 0,
            [InspectorName("Стружка (тёртое)")]         Shreds = 1,
            [InspectorName("Кусочки (пюре с кусками)")] Chunks = 2,
        }

        [Header("Кусочки, торчащие из массы")]
        [SerializeField] private Pieces _pieces = Pieces.Shreds;
        [Tooltip("Сколько кусочков на одну порцию")]
        [Range(0f, 40f)][SerializeField] private float _piecesPerPortion = 10f;
        [Tooltip("Насколько кусочек утоплен в массу: 0 — лежит сверху, 0.8 — почти скрыт")]
        [Range(0f, 0.9f)][SerializeField] private float _pieceSink = 0.45f;
        [Tooltip("Кусочки светлее/темнее массы: 1 — тот же цвет")]
        [Range(0.7f, 1.3f)][SerializeField] private float _pieceTone = 1.06f;

        [Header("Стружка")]
        [Tooltip("Сколько узора текстуры на кубиках и стружке (режимы Кубики/Стружка): " +
                 "0 — чистый цвет, 1 — полный узор.")]
        [Range(0f, 1f)][SerializeField] private float _boxDetail = 0.25f;
        [Tooltip("Длина стружки в кубиках")]
        [SerializeField] private Vector2 _shredLength = new Vector2(2.2f, 3.6f);
        [Tooltip("Толщина стружки в кубиках")]
        [SerializeField] private Vector2 _shredThickness = new Vector2(0.35f, 0.55f);
        [Tooltip("Стружек на один кубик объёма. Больше — гуще, дороже.")]
        [Range(1, 4)][SerializeField] private int _shredDensity = 2;
        [Tooltip("Наклон стружки, градусы: 0 — все лежат плашмя")]
        [Range(0f, 60f)][SerializeField] private float _shredTilt = 25f;

        [Header("Гладкая масса")]
        [Tooltip("Бугристость поверхности, доля высоты")]
        [Range(0f, 0.5f)][SerializeField] private float _lumpiness = 0.18f;
        [Tooltip("Размер бугров, в кубиках")]
        [Range(0.5f, 8f)][SerializeField] private float _lumpSize = 2.5f;

        [Header("Вид")]
        [Tooltip("Доля крупных кусочков")]
        [Range(0f, 0.4f)][SerializeField] private float _chunkChance = 0.08f;
        [Tooltip("Разброс оттенка кубиков")]
        [Range(0f, 0.3f)][SerializeField] private float _colorJitter = 0.04f;
        [Tooltip("Насколько темнее нижние слои (тень внутри кучки)")]
        [Range(0f, 0.5f)][SerializeField] private float _depthShade = 0.18f;
        [Tooltip("Материал на шейдере «CatCooks/Food Heap» (с текстурой тёртого продукта). " +
                 "Пусто — простой материал на этом же шейдере без текстуры.")]
        [SerializeField] private Material _material;
        [Tooltip("Больше кубиков не будет — они станут крупнее")]
        [Range(50, 4000)][SerializeField] private int _maxVoxels = 1500;

        [Header("Оптимизация")]
        [Tooltip("Горка в ТАРЕ строится один раз — сразу для полной тары — и дальше только " +
                 "«надувается» по высоте. Кучка на ПОЛУ пересобирается, только когда выросла " +
                 "заметно (Rebuild Ratio), а между пересборками растёт масштабом. " +
                 "Досыпка порции почти ничего не стоит.")]
        [SerializeField] private bool _inflate = true;
        [Tooltip("Кучка на полу пересобирается, когда порций стало больше/меньше в столько раз")]
        [Range(1.1f, 3f)][SerializeField] private float _rebuildRatio = 1.35f;
        [Tooltip("Отбрасывать тени (масса, стружки, ленточки). Выкл — заметно дешевле, " +
                 "и внутри горки нет тёмных пятен от собственных теней.")]
        [SerializeField] private bool _castShadows = false;
        [Tooltip("Ближе этого, м, — все ленточки")]
        [SerializeField] private float _strandLodNear = 3f;
        [Tooltip("Дальше этого, м, — только доля Strand Lod Min (оставшиеся шире, щелей нет)")]
        [SerializeField] private float _strandLodFar = 14f;
        [Range(0.05f, 1f)][SerializeField] private float _strandLodMin = 0.15f;

        [Header("Масштаб и поворот")]
        [Tooltip("Размеры кучки (Voxel, Footprint, Max Height) — в НАСТОЯЩИХ метрах мира, " +
                 "независимо от Scale родителя. Без этого кучка в банке со Scale 5 вырастает в 5 раз.")]
        [SerializeField] private bool _ignoreParentScale = true;
        [Tooltip("Держать кучку «вверх» по миру, даже если тара наклонена или HeapAnchor повёрнут. " +
                 "Для тары — вкл, для полотна лопаты — выкл.")]
        [SerializeField] private bool _worldUpright;

        [Header("Анимация")]
        [Tooltip("Подпрыгнуть, когда кучка выросла")]
        [SerializeField] private bool _punch = true;

        [Header("Превью в редакторе (в игре не используется)")]
        [Tooltip("Показывать кучку в сцене и в префабе без запуска игры")]
        [SerializeField] private bool _preview = true;
        [Tooltip("Сколько порций показать. Двигай — смотри, как растёт.")]
        [Range(0f, 40f)][SerializeField] private float _previewPortions = 6f;
        [SerializeField] private Color _previewColor = new Color(0.92f, 0.82f, 0.55f);
        [SerializeField] private int _previewSeed = 1;

        private static Material _autoMat;

        private MeshFilter _mf;
        private MeshRenderer _mr;
        private Mesh _mesh;
        private float _shownPortions = -1f;
        private Color _shownColor;
        private int _shownSeed;
        private float _punchT = 1f;

        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Color32> _c = new List<Color32>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<int> _t = new List<int>();

        /// <summary>Радиус основания последней построенной кучки, м (для коллайдера).</summary>
        public float Radius
        {
            get => _grain == Grain.Particles && _pile != null && _pile.Count > 0 ? _pile.ExtentRadius : _radius * _growScale.x;
            private set => _radius = value;
        }
        private float _radius;

        // ───────── живые частицы ─────────
        private FoodGrainPile _pile;
        private int _pileSeed = int.MinValue;
        private Vector3 _pourWorld;
        private float _pourUntil;
        private Vector3 _lastFramePos, _lastFrameVel;
        private bool _frameTracked;
        private float _simAccum;
        private MaterialPropertyBlock _mpb;
        // вязкость: растекание (0 — только что упала, 1 — растеклась) и пружина колыхания
        private float _spread = 1f;
        private float _jig, _jigVel;
        private static Mesh _dropSphere;
        private FoodStrandField _strandField;
        private float _massR = -1f, _massH = -1f;
        private Mesh _grainMeshA, _grainMeshB, _grainMeshC;
        private readonly Matrix4x4[][] _batches = { new Matrix4x4[1023], new Matrix4x4[1023], new Matrix4x4[1023] };
        private readonly int[] _batchCount = new int[3];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        /// <summary>Высота последней построенной кучки, м.</summary>
        public float Height
        {
            get => _grain == Grain.Particles && _pile != null && _pile.Count > 0 ? _pile.ExtentHeight : _height * _growScale.y;
            private set => _height = value;
        }
        private float _height;

        public Shape HeapShape => _shape;
        /// <summary>v18.5: полуразмеры основания (полотно лопаты, дно тары), м.</summary>
        public Vector2 Footprint => _footprint;
        /// <summary>v18.5: объём одной порции, м³.</summary>
        public float PortionVolume => _portionVolume;

        private const string MeshChildName = "HeapMesh (auto)";

        private void Awake()
        {
            if (!Application.isPlaying) return;
            RemoveStaleChild();      // превью из редактора в игру не тащим
            EnsureMesh();
        }

        private void EnsureMesh()
        {
            if (_mf != null) return;
            // После перекомпиляции ссылки обнуляются, а старый объект кучки
            // остаётся — убираем его, чтобы не плодить двойники.
            RemoveStaleChild();
            GameObject go = new GameObject(MeshChildName);
            // Объект кучки никогда не сохраняется в сцену и префаб: его строит
            // этот скрипт — в игре из порций, в редакторе из полей превью.
            go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            _mf = go.AddComponent<MeshFilter>();
            _mr = go.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = _material != null ? _material : AutoMaterial;
            _mr.shadowCastingMode = _castShadows ? UnityEngine.Rendering.ShadowCastingMode.On
                                                 : UnityEngine.Rendering.ShadowCastingMode.Off;
            _mesh = new Mesh { name = "FoodHeap", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                               hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            _mf.sharedMesh = _mesh;
        }

        private void RemoveStaleChild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform c = transform.GetChild(i);
                if (c.name != MeshChildName) continue;
                if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject);
            }
            _mf = null; _mr = null; _mesh = null;
            _shownPortions = -1f;
        }

        private static Material AutoMaterial
        {
            get
            {
                if (_autoMat != null) return _autoMat;
                Shader sh = Shader.Find("CatCooks/Food Heap");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Lit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                _autoMat = new Material(sh) { name = "CC Food Heap (auto)" };
                return _autoMat;
            }
        }

        /// <summary>
        /// Настроить как горку на полотне лопаты/мастерка: заполнение по полотну, всегда
        /// вверх по миру, без масштаба родителя (у моделей бывает Scale 300). Зовёт
        /// ScoopItem, если на Blade не было своего FoodHeapRenderer.
        /// </summary>
        public void ConfigureAsToolBlade(Vector2 footprint, float maxHeight)
        {
            _shape = Shape.Fill;
            _footprint = footprint;
            _maxHeight = maxHeight;
            _fillRect = true;
            _fillShell = true;
            _aboveRim = 1.2f;
            _ignoreParentScale = true;
            _worldUpright = true;
            _preview = false;
            _baseJson = null;               // вид по содержимому будет отталкиваться от этих настроек
            if (Application.isPlaying) EnsureMesh();
        }

        // ── фарш: «колбаски» из мясорубки ──
        private Material _wormMaterial;
        private Color _wormColor = new Color(0.7f, 0.2f, 0.2f);
        private MeshFilter _wormMf;
        private MeshRenderer _wormMr;
        private Mesh _wormMesh;
        private readonly List<Vector3> _wv = new List<Vector3>(8192);
        private readonly List<Color> _wc = new List<Color>(8192);
        private readonly List<int> _wt = new List<int>(16384);

        /// <summary>Цвет фарша: мясо сочнее и краснее (жарится потом до коричневого — это уже состояние).</summary>
        private static Color MinceTint(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            // к красному, насыщеннее, чуть темнее: говядина — тёмно-красная, свинина — розовая
            if (h > 0.08f && h < 0.5f) h = Mathf.Lerp(h, 0.99f, 0.6f);
            s = Mathf.Clamp01(s * 1.15f + 0.08f);
            v = Mathf.Clamp01(v * 0.95f);
            Color r = Color.HSVToRGB(h, s, v);
            r.a = c.a;
            return r;
        }

        /// <summary>
        /// Фарш = слой толстых извитых «колбасок» (как из решётки мясорубки) по верху массы.
        /// Каждая — трубка с закруглёнными концами, лежит на поверхности горки, вьётся,
        /// оттенок чуть гуляет (прожилки жира светлее). Строится, когда меняется горка.
        /// </summary>
        private void RebuildWorms(float portions, int seed)
        {
            bool on = _runtimeLook == HeapPreset.Mince && portions > 0.001f && _mesh != null && _mesh.vertexCount > 2;
            if (!on) { if (_wormMr != null) _wormMr.enabled = false; return; }
            if (_wormMf == null)
            {
                var go = new GameObject("MinceWorms") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable, layer = gameObject.layer };
                go.transform.SetParent(_mf.transform, false);
                _wormMf = go.AddComponent<MeshFilter>();
                _wormMr = go.AddComponent<MeshRenderer>();
                _wormMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _wormMesh = new Mesh { name = "MinceWorms", hideFlags = HideFlags.DontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                _wormMesh.MarkDynamic();
                _wormMf.sharedMesh = _wormMesh;
            }
            _wormMr.enabled = true;
            _wormMr.sharedMaterial = _wormMaterial != null ? _wormMaterial : _mr.sharedMaterial;

            Vector3[] mv = _mesh.vertices;
            Vector3[] mn = _mesh.normals;
            int[] mt = _mesh.triangles;
            if (mn == null || mn.Length != mv.Length) { _mesh.RecalculateNormals(); mn = _mesh.normals; }

            // треугольники «сверху» с весом по площади
            var tris = new List<int>();
            var cum = new List<float>();
            float area = 0f;
            for (int i = 0; i + 2 < mt.Length; i += 3)
            {
                Vector3 a = mv[mt[i]], b = mv[mt[i + 1]], c = mv[mt[i + 2]];
                Vector3 cr = Vector3.Cross(b - a, c - a);
                float ar = cr.magnitude * 0.5f;
                if (ar < 1e-9f || cr.normalized.y < 0.2f) continue;
                area += ar;
                tris.Add(i); cum.Add(area);
            }
            _wv.Clear(); _wc.Clear(); _wt.Clear();
            if (tris.Count == 0) { _wormMesh.Clear(); return; }

            var rnd = new System.Random(seed * 7919 + 13);
            System.Func<float> R = () => (float)rnd.NextDouble();
            // ФАРШ ИЗ МЯСОРУБКИ: тонкие круглые «макаронины», выходят ПУЧКАМИ — по 3–5 штук
            // рядом, параллельно, и вьются вместе (как на фото). Пучки лежат в 2–3 слоя
            // и полностью закрывают горку. Крапинки жира — на самих колбасках (текстура).
            const int Segs = 18, Sides = 6;
            float radius = Mathf.Clamp(Mathf.Max(Radius, 0.05f) * 0.022f, 0.0035f, 0.016f);
            float meanLen = radius * 30f;
            float strandArea = meanLen * radius * 2f;
            int strands = Mathf.Clamp(Mathf.RoundToInt(area / strandArea * 2.6f), 24, 900);
            int bundles = Mathf.Max(6, strands / 4);
            int made = 0;

            for (int bnd = 0; bnd < bundles && made < strands; bnd++)
            {
                // точка на поверхности
                float pick = R() * area;
                int lo = 0, hi = cum.Count - 1;
                while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] < pick) lo = mid + 1; else hi = mid; }
                int ti = tris[lo];
                float u = R(), v = R(); if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                int i0 = mt[ti], i1 = mt[ti + 1], i2 = mt[ti + 2];
                Vector3 p0 = mv[i0] + (mv[i1] - mv[i0]) * u + (mv[i2] - mv[i0]) * v;
                Vector3 n = (mn[i0] + (mn[i1] - mn[i0]) * u + (mn[i2] - mn[i0]) * v).normalized;
                if (n.y < 0.15f) n = Vector3.Slerp(n, Vector3.up, 0.5f).normalized;

                Vector3 t0 = Vector3.Cross(n, R() < 0.5f ? Vector3.right : Vector3.forward).normalized;
                Vector3 dir0 = Quaternion.AngleAxis(R() * 360f, n) * t0;
                int inBundle = 3 + (int)(R() * 3f);
                float len = meanLen * Mathf.Lerp(0.6f, 1.4f, R());
                float curl = (R() - 0.5f) * 0.5f;                      // плавный изгиб пучка
                float wave = R() * 6.28f;
                float layer = 0.6f + (bnd % 3) * 1.1f;                  // 3 слоя по высоте

                for (int sIdx = 0; sIdx < inBundle && made < strands; sIdx++, made++)
                {
                    float off = (sIdx - (inBundle - 1) * 0.5f) * radius * 2.05f;   // рядом, впритык
                    float rr = radius * Mathf.Lerp(0.9f, 1.1f, R());
                    Color col = _wormColor * Mathf.Lerp(0.9f, 1.07f, R());
                    col.a = 1f;
                    Vector3 dir = dir0;
                    Vector3 side0 = Vector3.Cross(n, dir).normalized;
                    Vector3 pos = p0 + side0 * off + n * (rr * layer);
                    float seg = len * Mathf.Lerp(0.85f, 1.1f, R()) / Segs;
                    int baseIdx = _wv.Count;
                    for (int k = 0; k <= Segs; k++)
                    {
                        Vector3 side = Vector3.Cross(n, dir).normalized;
                        Vector3 up = Vector3.Cross(dir, side).normalized;
                        // закруглённые концы; тело — ровная круглая «макаронина»
                        float endK = Mathf.Min(k, Segs - k) / 1.2f;
                        float rk = rr * Mathf.Sqrt(Mathf.Clamp01(endK));
                        for (int q = 0; q < Sides; q++)
                        {
                            float ang = q / (float)Sides * Mathf.PI * 2f;
                            _wv.Add(pos + (side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * rk);
                            float shade = Mathf.Lerp(0.62f, 1.06f, (Mathf.Sin(ang) + 1f) * 0.5f);
                            _wc.Add(new Color(col.r * shade, col.g * shade, col.b * shade, 1f));
                        }
                        // весь пучок вьётся одинаково (параллельно), с лёгкой волной вверх-вниз
                        float turn = curl + Mathf.Sin(k * 0.45f + wave) * 0.12f;
                        dir = Quaternion.AngleAxis(turn * Mathf.Rad2Deg * 0.35f, n) * dir;
                        pos += dir * seg + n * (Mathf.Sin(k * 0.6f + wave) * rr * 0.12f);
                    }
                    for (int k = 0; k < Segs; k++)
                        for (int q = 0; q < Sides; q++)
                        {
                            int a = baseIdx + k * Sides + q, b = baseIdx + k * Sides + (q + 1) % Sides;
                            int c2 = a + Sides, d = b + Sides;
                            _wt.Add(a); _wt.Add(c2); _wt.Add(b);
                            _wt.Add(b); _wt.Add(c2); _wt.Add(d);
                        }
                }
            }
            _wormMesh.Clear();
            _wormMesh.SetVertices(_wv);
            _wormMesh.SetColors(_wc);
            _wormMesh.SetTriangles(_wt, 0);
            _wormMesh.RecalculateNormals();
            _wormMesh.RecalculateBounds();
        }

        // ── вид по содержимому (FoodHeapLook) ──
        private string _baseJson;
        private HeapPreset _runtimeLook = (HeapPreset)255;
        private int _runtimeKey = -1;

        // ── материалы под вид (общие на всех, создаются один раз) ──
        private static readonly Dictionary<long, Material> _lookMats = new Dictionary<long, Material>();
        private static Material _autoStrandMat;
        private static Texture2D _minceAlbedo, _minceNormal;

        private static Material AutoStrandMaterial
        {
            get
            {
                if (_autoStrandMat != null) return _autoStrandMat;
                Shader sh = Shader.Find("CatCooks/Food Strands");
                if (sh == null) return null;
                _autoStrandMat = new Material(sh) { name = "CC Food Strands (auto)", hideFlags = HideFlags.DontSave };
                return _autoStrandMat;
            }
        }

        /// <summary>Копия материала под вид: фарш — свой узор и рельеф, остальное — без узора.</summary>
        private static Material LookMaterial(Material src, HeapPreset look)
        {
            if (src == null) return null;
            int kind = look == HeapPreset.Mince ? 1 : 0;
            long key = ((long)src.GetInstanceID() << 2) | (long)kind;
            if (_lookMats.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(src) { name = src.name + (kind == 1 ? " (фарш)" : " (без узора)"), hideFlags = HideFlags.DontSave };
            if (kind == 1)
            {
                EnsureMinceTextures();
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", _minceAlbedo);
                if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", _minceNormal);
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 1.1f);
                if (m.HasProperty("_TexScale")) m.SetFloat("_TexScale", 18f);        // крапинки мельче — под тонкие колбаски
                if (m.HasProperty("_TexDetail")) m.SetFloat("_TexDetail", 1f);
            }
            else
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
                if (m.HasProperty("_TexDetail")) m.SetFloat("_TexDetail", 0f);
            }
            _lookMats[key] = m;
            return m;
        }

        /// <summary>
        /// Узор фарша — рисуется кодом (текстуры не нужны): светлые волокна-завитки,
        /// тёмные промежутки, белые крапинки жира. Серый — цвет даёт продукт
        /// (говядина темнее и краснее, свинина розовее). Бесшовный.
        /// </summary>
        private static void EnsureMinceTextures()
        {
            if (_minceAlbedo != null && _minceNormal != null) return;
            // Узор для КОЛБАСОК фарша: мясо с лёгкой зернистостью и редкие светлые
            // крапинки жира (без «червячков» — колбаски теперь настоящие, из меша).
            const int N = 128;
            float[] h = new float[N * N];
            float[] v = new float[N * N];
            var rnd = new System.Random(4217);
            for (int i = 0; i < h.Length; i++)
            {
                float grain = (float)rnd.NextDouble();
                h[i] = 0.3f + grain * 0.12f;
                v[i] = 0.86f + grain * 0.1f;
            }
            for (int f = 0; f < 46; f++)
            {
                int cx = rnd.Next(N), cy = rnd.Next(N);
                float r = 1.2f + (float)rnd.NextDouble() * 1.6f;
                int ri = Mathf.CeilToInt(r);
                for (int dy = -ri; dy <= ri; dy++)
                    for (int dx = -ri; dx <= ri; dx++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                        if (d > 1f) continue;
                        int idx = ((cy + dy) & (N - 1)) * N + ((cx + dx) & (N - 1));
                        float k = 1f - d * d;
                        v[idx] = Mathf.Lerp(v[idx], 1.36f, k);          // жир — светлый
                        h[idx] = Mathf.Max(h[idx], 0.3f + k * 0.5f);
                    }
            }
            _minceAlbedo = new Texture2D(N, N, TextureFormat.RGBA32, true, false)
            { name = "Mince Specks (auto)", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            _minceNormal = new Texture2D(N, N, TextureFormat.RGBA32, true, true)
            { name = "Mince Normal (auto)", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var ca = new Color32[N * N];
            var cn = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    byte gb = (byte)(Mathf.Clamp01(v[i] * 0.74f) * 255f);
                    ca[i] = new Color32(gb, gb, gb, 255);
                    float hx = h[y * N + ((x + 1) & (N - 1))] - h[y * N + ((x - 1) & (N - 1))];
                    float hy = h[((y + 1) & (N - 1)) * N + x] - h[((y - 1) & (N - 1)) * N + x];
                    Vector3 n = new Vector3(-hx * 2f, -hy * 2f, 1f).normalized;
                    cn[i] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f),
                                        (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
                }
            _minceAlbedo.SetPixels32(ca); _minceAlbedo.Apply(true, false);
            _minceNormal.SetPixels32(cn); _minceNormal.Apply(true, false);
        }

        /// <summary>
        /// Сменить вид под то, что лежит (порошок, кубики, стружка, пюре).
        /// Custom — вернуть то, что настроено на этой кучке в инспекторе.
        /// Зовут CookwareHeapVisual, FoodHeap, лопата — перед Show.
        /// </summary>
        // Тара: горка наклоняется вместе с тарой (не держится «вверх по миру»). Не сериализуется —
        // SetLook восстанавливает настройки из инспектора, а это решение тары, а не кучки.
        [System.NonSerialized] private bool _followParent;
        [System.NonSerialized] private bool _restSet;
        [System.NonSerialized] private Quaternion _restParent, _restHeap;
        [System.NonSerialized] private Vector3 _restUpLocal;

        /// <summary>Горка следует за наклоном родителя (ведро наклонили — горка вместе с ним).</summary>
        public void FollowParentRotation(bool on) => _followParent = on;

        public void SetLook(HeapPreset look) => SetLook(look, false);

        /// <summary>
        /// keepTexture — оставить текстуры материалов кучки (картофельные) как есть: только
        /// для овощей. Иначе узор снимается (порошок, масса, жидкость, мясо кусочками) или
        /// ставится свой (фарш) — картошка больше нигде не «проступает».
        /// </summary>
        public void SetLook(HeapPreset look, bool keepTexture)
        {
            int key = (int)look | (keepTexture ? 256 : 0);
            if (!Application.isPlaying || key == _runtimeKey) return;
            if (_baseJson == null) _baseJson = JsonUtility.ToJson(this);   // настройки из инспектора — запомнить один раз
            bool wasParticles = _grain == Grain.Particles;
            Material oldGrainMat = _grainMaterial;
            JsonUtility.FromJsonOverwrite(_baseJson, this);
            if (look != HeapPreset.Custom) ApplyPreset(look);
            _runtimeLook = look;
            _runtimeKey = key;

            // Материалы под вид: без картофельного узора или с узором фарша.
            if (look == HeapPreset.Mince && !keepTexture)
            {
                // Фарш: масса под колбасками — гладкая тёмная «тень», крапинки только на колбасках.
                Material src = _material != null ? _material : AutoMaterial;
                _wormMaterial = LookMaterial(src, HeapPreset.Mince);
                _material = LookMaterial(src, HeapPreset.Powder);
                _grainMaterial = LookMaterial(_grainMaterial, HeapPreset.Powder);
                _strandMaterial = null;
            }
            else if (look == HeapPreset.Custom && !keepTexture)
            {
                // Форма как настроена на кучке (режим измельчителя), но без картофельного узора.
                _material = LookMaterial(_material != null ? _material : AutoMaterial, HeapPreset.Powder);
                _grainMaterial = LookMaterial(_grainMaterial, HeapPreset.Powder);
                _strandMaterial = LookMaterial(_strandMaterial, HeapPreset.Powder);
            }
            else if (look != HeapPreset.Custom && !keepTexture)
            {
                _material = LookMaterial(_material != null ? _material : AutoMaterial, look);
                _grainMaterial = LookMaterial(_grainMaterial, look);
                Material strand = _strandMaterial != null ? _strandMaterial : (look == HeapPreset.Mince ? AutoStrandMaterial : null);
                _strandMaterial = LookMaterial(strand, look);
            }

            // Сменился способ рисования или материал крупинок — убрать старое и пересобрать.
            if ((wasParticles && _grain != Grain.Particles) || oldGrainMat != _grainMaterial) _pile = null;
            if ((look == HeapPreset.Gnocchi) != _grainGnocchi)                                          // крупинки другой формы и размера
            {
                if (!_grainGnocchi) { DestroyMesh(_grainMeshA); DestroyMesh(_grainMeshB); DestroyMesh(_grainMeshC); }
                _pile = null; _grainMeshA = _grainMeshB = _grainMeshC = null;
            }
            _strandField?.Clear();
            if (_mr != null) _mr.enabled = true;
            if (_shownPortions >= 0f)
            {
                float n = _shownPortions; Color c = _shownColor; int sd = _shownSeed;
                _shownPortions = -1f;
                Show(n, c, sd);
            }
        }

        /// <summary>Показать кучку. Зовётся при смене содержимого.</summary>
        /// <summary>v18.6: зовётся при каждом Show (лопата сменила груз) — для ScoopSubstanceView.</summary>
        public event System.Action Shown;

        public void Show(float portions, Color color, int seed)
        {
            if (Shown != null && Application.isPlaying) Shown();
            EnsureMesh();
            if (_subOn && _sub == null) LeaveSubstance();
            if (_runtimeLook == HeapPreset.Mince)
            {
                _wormColor = MinceTint(color);
                color = _wormColor * 0.42f;           // масса под колбасками — тёмная, как тень в щелях
                color.a = 1f;
            }
            bool grew = portions > _shownPortions + 0.01f && _shownPortions >= 0f;
            if (Mathf.Approximately(portions, _shownPortions) && color == _shownColor && seed == _shownSeed) return;
            float before = _shownPortions;
            if (Application.isPlaying && before >= 0f)
            {
                if (portions > before + 0.01f) { Kick(0.6f); if (_flow > 0f && _spread > 0.6f) _spread = 0.6f; }
                else if (portions < before - 0.01f) Kick(-0.8f);
            }
            _shownPortions = portions;
            _shownColor = color;
            _shownSeed = seed;

            if (_sub != null && Application.isPlaying)
            {
                ShowSubstance(before, portions, color, seed);     // v18: вещество рисует само (масло, порошок)
                return;
            }

            if ((_runtimeLook == HeapPreset.Gnocchi || _runtimeLook == HeapPreset.Whole) && Application.isPlaying)
            {
                ShowGnocchi(portions, seed);                 // целые ньокки / клубни, аккуратно, без физики
                return;
            }
            if (_grain == Grain.Particles)
            {
                ShowParticles(before, portions, seed);
                return;
            }

            ShowMass(portions, color, seed);
            if (grew && _punch) _punchT = 0f;
        }

        /// <summary>Тара: сколько порций соответствует полной. Ставит CookwareHeapVisual.
        /// Тогда высота горки считается от ДОЛИ заполнения — полная тара = полный купол,
        /// при любых Portion Volume и Capacity.</summary>
        public void SetFillCapacity(int capacity)
        {
            if (capacity == _fillCapacity) return;
            _fillCapacity = Mathf.Max(0, capacity);
            _refFull = false;
        }

        private void ShowMass(float portions, Color color, int seed)
        {
            if (!_inflate || !Application.isPlaying || portions <= 0.001f)
            {
                _growScale = Vector3.one;
                Build(portions, color, seed);
                _builtFor = portions;
                _refFull = false;
                return;
            }

            if (_shape == Shape.Fill && _fillCapacity > 0)
            {
                // Один раз — горка для ПОЛНОЙ тары (с ленточками), дальше только высота.
                if (!_refFull || color != _refColor || seed != _refSeed)
                {
                    _buildFull = true;
                    Build(_fillCapacity, color, seed);
                    _buildFull = false;
                    _refFull = true; _refColor = color; _refSeed = seed;
                    _centreFull = Mathf.Max(1e-4f, _height);
                }
                FillShape(Mathf.Clamp01(portions / _fillCapacity), out float centre, out _);
                _growScale = new Vector3(1f, Mathf.Max(0.04f, centre / _centreFull), 1f);
                _mr.enabled = !(_strands && _strandsHideMass && _strandField != null && _strandField.Count > 0);
                return;
            }

            // Кучка на полу: пересобрать, только если выросла/уменьшилась заметно.
            bool rebuild = _builtFor <= 0f || color != _refColor || seed != _refSeed ||
                           portions > _builtFor * _rebuildRatio || portions < _builtFor / _rebuildRatio;
            if (rebuild)
            {
                Build(portions, color, seed);
                _builtFor = portions; _refColor = color; _refSeed = seed;
                _growScale = Vector3.one;
            }
            else
            {
                float k = Mathf.Pow(portions / _builtFor, 1f / 3f);   // объём ∝ размер³
                _growScale = new Vector3(k, k, k);
            }
        }

        /// <summary>Профиль тары по доле заполнения 0..1: высота центра и края.
        /// 0..первый этап — ровно до края, дальше — купол над краем.</summary>
        /// <summary>Тара-трапеция: вершины горки раздвигаются по высоте (у дна — Footprint, у края — × Flare Top).</summary>
        private void FlareV()
        {
            if (_shape != Shape.Fill || Mathf.Abs(_flareTop - 1f) < 0.001f || _maxHeight <= 0f) return;
            for (int i = 0; i < _v.Count; i++)
            {
                Vector3 p = _v[i];
                float k = 1f + (_flareTop - 1f) * Mathf.Clamp01(p.y / _maxHeight);
                p.x *= k; p.z *= k;
                _v[i] = p;
            }
        }

        /// <summary>Расширение кверху (меню настройки тары). 1 — цилиндр.</summary>
        public float FlareTop { get => _flareTop; set => _flareTop = Mathf.Clamp(value, 0.5f, 2.5f); }

        private void FillShape(float frac, out float centre, out float edge)
        {
            float rx = Mathf.Max(_footprint.x, 0.001f), rz = Mathf.Max(_footprint.y, 0.001f);
            FillMoments(out float aK, out float m);
            float area = aK * rx * rz;
            float fl = _shape == Shape.Fill ? Mathf.Max(0.5f, _flareTop) : 1f;
            area *= (1f + fl + fl * fl) / 3f;                 // трапеция: средняя площадь по высоте
            float vFull = area * _maxHeight * (1f + _aboveRim * (1f - m));
            FillFromVolume(frac * vFull, area, m, out centre, out edge);
        }

        /// <summary>
        /// Профиль тары y(r) = край + (центр − край)·(1 − r²) по объёму.
        /// Объём = площадь · (край + (центр − край)·(1 − m)), где m — средний r² по дну
        /// (у овала 0.5, у скруглённого прямоугольника больше). Считается точно для формы
        /// дна, поэтому доля заполнения соответствует высоте при любой форме.
        /// </summary>
        private void FillFromVolume(float volume, float area, float m, out float centre, out float edge)
        {
            float stage1 = area * _maxHeight * (1f - 0.5f * m);          // центр дошёл до края тары
            if (volume <= stage1)
            {
                centre = volume / (area * (1f - 0.5f * m));
                edge = centre * 0.5f;
                return;
            }
            float baseK = 0.5f + 0.5f * (1f - m);
            float slope = 0.5f + (_aboveRim - 0.5f) * (1f - m);
            float t = Mathf.Clamp01((volume / (area * _maxHeight) - baseK) / Mathf.Max(1e-4f, slope));
            centre = _maxHeight * (1f + _aboveRim * t);
            edge = _maxHeight * (0.5f + 0.5f * t);
        }

        private float _momSharp = -1f;
        private bool _momRect;
        private float _momArea = Mathf.PI, _momM = 0.5f, _momFlat = -1f;

        /// <summary>Площадь дна (в долях rx·rz) и средний r² — численно, с кэшем.</summary>
        private void FillMoments(out float areaK, out float m)
        {
            if (!_fillRect) { areaK = Mathf.PI; m = 2f / (_topFlatness + 2f); return; }
            if (Mathf.Approximately(_momSharp, _rectSharpness) && _momRect == _fillRect &&
                Mathf.Approximately(_momFlat, _topFlatness))
            { areaK = _momArea; m = _momM; return; }
            const int N = 80;
            float a = 0f, r2sum = 0f;
            for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                float u = (i + 0.5f) / N * 2f - 1f, w = (j + 0.5f) / N * 2f - 1f;
                float r2 = RectR2(u, w);
                if (r2 > 1f) continue;
                a += 1f; r2sum += Mathf.Pow(r2, _topFlatness * 0.5f);
            }
            _momArea = a / (N * N) * 4f;
            _momM = a > 0f ? r2sum / a : 0.5f;
            _momSharp = _rectSharpness; _momRect = _fillRect; _momFlat = _topFlatness;
            areaK = _momArea; m = _momM;
        }

        /// <summary>«Радиус²» точки дна: у прямоугольника — суперэллипс, у овала — эллипс.</summary>
        private float RectR2(float u, float w)
        {
            if (!_fillRect || _shape != Shape.Fill) return u * u + w * w;
            float n = _rectSharpness;
            float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), n) + Mathf.Pow(Mathf.Abs(w), n), 1f / n);
            return r * r;
        }

        public void Clear() => Show(0f, _shownColor, _shownSeed);

        private void Update()
        {
            if (_mf == null) return;
            if (_rebuildPending && Application.isPlaying) { _rebuildPending = false; RebuildNow(); }
            if (_subOn && Application.isPlaying) { UpdateSubstance(); return; }
            Vector3 punch = Vector3.one;
            if (Application.isPlaying && _punchT < 1f)
            {
                _punchT = Mathf.Min(1f, _punchT + Time.deltaTime * 5f);
                float s = 1f + Mathf.Sin(_punchT * Mathf.PI) * 0.12f * (1f - _punchT);
                punch = new Vector3(1f + (s - 1f) * 0.5f, s, 1f + (s - 1f) * 0.5f);
            }
            if (Application.isPlaying) punch = Vector3.Scale(punch, Vector3.Scale(ViscousScale(Time.deltaTime), _growScale));
            ApplyFrame(punch);
            if (Application.isPlaying && OnTool) GlueToBlade();               // v18.4: любая горка лежит на полотне
            if ((_runtimeLook == HeapPreset.Gnocchi || _runtimeLook == HeapPreset.Whole) && Application.isPlaying) { DrawGnocchi(); return; }
            if (_grain == Grain.Particles) UpdateParticles();
        }

        // ───────── НЬОККИ: целые штуки, разложены аккуратно (без физики — не прыгают) ─────────
        private readonly List<Matrix4x4> _gnoc = new List<Matrix4x4>();
        private Matrix4x4[] _gnocBuf;

        private void ShowGnocchi(float portions, int seed)
        {
            _pile = null;
            bool whole = _runtimeLook == HeapPreset.Whole;
            int n = whole ? Mathf.Clamp(Mathf.CeilToInt(portions / WholeVegMesh.PortionsPer - 0.01f), 0, 32)   // клубень — на 6 порций
                          : Mathf.Clamp(Mathf.RoundToInt(portions), 0, 64);
            _gnoc.Clear();
            if (_mr != null) _mr.enabled = false;
            if (n == 0) { _radius = 0f; _height = 0f; return; }
            Vector3 d = whole ? WholeVegMesh.Dims : GnocchiMesh.Dims;
            var rnd = new System.Random(seed * 7919 + 13);
            System.Func<float> R = () => (float)rnd.NextDouble() - 0.5f;
            bool fill = _shape == Shape.Fill;                 // лопата/тара — ровными рядами; пол — горкой
            float maxR = 0f, top = 0f;
            int placed = 0, layer = 0;
            while (placed < n)
            {
                // раскладка слоя: на лопате 2×2 параллельно, на полу — цветок (центр + 6 вокруг); выше — меньше
                List<Vector3> slots = new List<Vector3>();
                if (fill)
                {
                    if (layer == 0) { slots.Add(new Vector3(-0.52f * d.x, 0f, -0.5f * d.z)); slots.Add(new Vector3(0.52f * d.x, 0f, -0.5f * d.z)); slots.Add(new Vector3(-0.52f * d.x, 0f, 0.5f * d.z)); slots.Add(new Vector3(0.52f * d.x, 0f, 0.5f * d.z)); }
                    else if (layer == 1) { slots.Add(new Vector3(0f, 0f, -0.3f * d.z)); slots.Add(new Vector3(0f, 0f, 0.3f * d.z)); }
                    else slots.Add(Vector3.zero);
                }
                else
                {
                    if (layer == 0) { slots.Add(Vector3.zero); for (int k = 0; k < 6; k++) { float a = k * Mathf.PI / 3f + 0.3f; slots.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d.z * 0.95f); } }
                    else if (layer == 1) { for (int k = 0; k < 3; k++) { float a = k * Mathf.PI * 2f / 3f + 1.1f; slots.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d.z * 0.45f); } }
                    else slots.Add(new Vector3(R() * d.x * 0.4f, 0f, R() * d.z * 0.4f));
                }
                foreach (Vector3 sl in slots)
                {
                    if (placed >= n) break;
                    float y = d.y * 0.5f + layer * d.y * 0.82f;
                    Vector3 pos = sl + new Vector3(R() * d.x * 0.12f, y, R() * d.z * 0.12f);
                    float yaw = fill ? R() * 20f : (float)rnd.NextDouble() * 360f;
                    Quaternion q = Quaternion.Euler(layer > 0 ? R() * 14f : R() * 4f, yaw, layer > 0 ? R() * 14f : R() * 4f);
                    _gnoc.Add(Matrix4x4.TRS(pos, q, d));
                    maxR = Mathf.Max(maxR, new Vector2(pos.x, pos.z).magnitude + d.z * 0.5f);
                    top = Mathf.Max(top, y + d.y * 0.5f);
                    placed++;
                }
                layer++;
            }
            _radius = maxR; _height = top;
            if (Debug.isDebugBuild) Debug.Log($"[Кучка] «{name}»: {(whole ? "клубни" : "ньокки")} {(fill ? "в лотке/таре" : "на полу")} — {n} шт (размер {d.z * 100f:0} см)", this);
        }

        private void DrawGnocchi()
        {
            if (_gnoc.Count == 0) return;
            Transform frame = _mf != null ? _mf.transform : transform;
            Material mat = _grainMaterial != null ? _grainMaterial : (_material != null ? _material : AutoMaterial);
            if (!mat.enableInstancing) mat.enableInstancing = true;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.SetColor(BaseColorId, _shownColor);
            if (_gnocBuf == null || _gnocBuf.Length < _gnoc.Count) _gnocBuf = new Matrix4x4[Mathf.Max(16, _gnoc.Count)];
            Matrix4x4 m = frame.localToWorldMatrix;
            for (int i = 0; i < _gnoc.Count; i++) _gnocBuf[i] = m * _gnoc[i];
            RenderParams rp = new RenderParams(mat)
            {
                matProps = _mpb,
                layer = gameObject.layer,
                shadowCastingMode = _castShadows || _grainShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = new Bounds(frame.position, Vector3.one * Mathf.Max(1f, _radius * 4f)),
            };
            Graphics.RenderMeshInstanced(rp, _runtimeLook == HeapPreset.Whole ? WholeVegMesh.Get() : GnocchiMesh.Get(1f), 0, _gnocBuf, _gnoc.Count);
        }

        /// <summary>
        /// Пересобрать горку с текущими настройками и тем же содержимым — после
        /// правки полей в инспекторе во время игры.
        /// </summary>
        private void RebuildNow()
        {
            if (_shownPortions < 0f) return;
            if (_grain == Grain.Particles)
            {
                if (_pile == null) return;
                _pile.S = PileSettings();
                int target = Mathf.Min(_maxGrains, Mathf.RoundToInt(_shownPortions * _grainsPerPortion));
                LayoutWithChaos(target, _shownPortions, _shownSeed);
                RebuildUnderMass(true);
                return;
            }
            Build(_shownPortions, _shownColor, _shownSeed);
        }

        /// <summary>Снять масштаб и наклон родителя с меша кучки.</summary>
        private void ApplyFrame(Vector3 punch)
        {
            Transform t = _mf.transform;
            if (_worldUpright)
            {
                // Только поворот вокруг вертикали остаётся от тары.
                Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                Quaternion upright = f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f, Vector3.up) : Quaternion.identity;
                if (_followParent)
                {
                    // Тара: горка наклоняется ВМЕСТЕ с тарой — но от её «стоячего» положения.
                    // HeapAnchor бывает повёрнут как угодно (модель с поворотом −90°), поэтому
                    // берём не его оси, а насколько тара наклонилась с тех пор, как стояла ровно.
                    if (!_restSet || Vector3.Angle(transform.rotation * _restUpLocal, Vector3.up) < 2f)
                    {
                        _restSet = true;
                        _restParent = transform.rotation;
                        _restUpLocal = Quaternion.Inverse(transform.rotation) * Vector3.up;
                        _restHeap = upright;
                    }
                    Quaternion tilt = transform.rotation * Quaternion.Inverse(_restParent);
                    t.rotation = tilt * _restHeap;
                }
                else t.rotation = upright;
            }
            else t.localRotation = Quaternion.identity;

            Vector3 k = Vector3.one;
            if (_ignoreParentScale)
            {
                Vector3 ls = transform.lossyScale;
                k = new Vector3(Safe(ls.x), Safe(ls.y), Safe(ls.z));
                if (_worldUpright)
                {
                    // Наклонённый объект со сжатием по осям точно не компенсировать;
                    // берём средний масштаб — для равномерного Scale это точно.
                    float m = (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f;
                    k = Vector3.one * Safe(m);
                }
            }
            t.localScale = Vector3.Scale(k, punch);
        }

        private static void DestroyMesh(Mesh m)
        {
            if (m == null) return;
            if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
        }

        private static float Safe(float v) => Mathf.Abs(v) < 1e-5f ? 1f : 1f / v;

        // ═════════════════════ ЖИВЫЕ ЧАСТИЦЫ ═════════════════════

        /// <summary>
        /// Куда сыплется содержимое (мир). Измельчитель сообщает точку падения
        /// струи — новые частицы в таре появятся над ней и упадут туда же.
        /// Действует полторы секунды.
        /// </summary>
        public void SetPourPoint(Vector3 world)
        {
            _pourWorld = world;
            _pourUntil = Time.time + 1.5f;
        }

        /// <summary>Удар: кучка приземлилась — частицы разлетаются и оседают заново.</summary>
        public void Splash(float strength = -1f)
        {
            // Масса упала: расплющилась ударом и заново растекается.
            if (_flow > 0f || _wobble > 0f) { _spread = 0f; Kick(-1.2f); }
            if (_pile == null) return;
            _pile.Splash(strength < 0f ? _landingSplash : strength);
        }

        /// <summary>Разбудить (лопата ткнула, что-то упало сверху).</summary>
        public void WakeUp() { if (_pile != null) _pile.Wake(); }

        private FoodGrainPile.Settings PileSettings()
        {
            return new FoodGrainPile.Settings
            {
                Radius = _grainSize,
                Friction = _grainFriction,
                StaticFriction = _grainStaticFriction,
                FreezeSpeed = 0.12f,
                GravityScale = 1f,
                Damping = 0.02f,
                Box = _shape == Shape.Fill,
                HalfSize = _footprint,
                Inertia = _grainInertia,
                Iterations = _grainIterations,
                SleepSpeed = 0.03f,
            };
        }

        private void ShowParticles(float before, float portions, int seed)
        {
            if (_pile == null)
            {
                _pile = new FoodGrainPile(_maxGrains, seed);
                _pileSeed = seed;
            }
            else if (seed != _pileSeed)
            {
                _pile.Reseed(seed);
                _pileSeed = seed;
            }
            _pile.S = PileSettings();

            if (_mr != null && !_underMass) _mr.enabled = false;   // без подложки меш не нужен
            int target = Mathf.Min(_maxGrains, Mathf.RoundToInt(portions * _grainsPerPortion));
            int have = _pile.Count;

            // Первый показ, поздний вход, превью — сразу уложенная горка без падения.
            bool instant = before < 0f || !Application.isPlaying;
            if (instant)
            {
                LayoutWithChaos(target, portions, seed);
                if (!Application.isPlaying) BakePileToMesh();
                else RebuildUnderMass(true);
                return;
            }

            if (target > have)
            {
                Vector3 spawn = PourLocal(out float spread);
                _pile.Add(target - have, spawn, spread, GrainSize, GrainRotation);
            }
            else if (target < have)
            {
                _pile.RemoveTop(have - target);
            }
        }

        /// <summary>
        /// Уложить горку живых частиц с хаосом: бугры по краям, мелкие кучки
        /// вокруг. Размер горки — из объёма частиц, как у обычной раскладки.
        /// </summary>
        private void LayoutWithChaos(int n, float portions, int seed)
        {
            if (_chaos <= 0.001f) { _pile.Layout(n, _slope, GrainSize, GrainRotation); return; }

            float grainVol = Mathf.Pow(_grainSize * 2f, 3f) * 1.35f;
            float volume = Mathf.Max(1, n) * grainVol;
            float rx, rz, h;
            if (_shape == Shape.Fill)
            {
                rx = Mathf.Max(_footprint.x - _grainSize, _grainSize);
                rz = Mathf.Max(_footprint.y - _grainSize, _grainSize);
                h = Mathf.Max(_grainSize * 2f, volume / (Mathf.PI * rx * rz * 0.75f));
            }
            else
            {
                float r = Mathf.Pow(2f * volume / (Mathf.PI * _slope), 1f / 3f);
                rx = rz = r;
                h = _slope * r;
            }

            PlanChaos(portions, rx, rz, _grainSize * 3f, seed);
            float ext = _shape == Shape.Fill ? Mathf.Max(rx, rz) : ChaosExtent();

            Vector3[] clusters = new Vector3[_sats.Count];
            for (int i = 0; i < _sats.Count; i++) clusters[i] = new Vector3(_sats[i].X, _sats[i].Z, _sats[i].R);

            _pile.LayoutShaped(n, _shape == Shape.Fill ? rx : ext, _shape == Shape.Fill ? rz : ext,
                               (x, z) => h * ChaosProfile(x, z),
                               clusters, 0.06f * _chaos, GrainSize, GrainRotation);
        }

        /// <summary>Точка, откуда падают новые частицы, в осях рамки.</summary>
        private Vector3 PourLocal(out float spread)
        {
            Transform frame = _mf != null ? _mf.transform : transform;
            Vector3 local;
            if (Time.time < _pourUntil)
                local = frame.InverseTransformPoint(_pourWorld);
            else
                local = Vector3.zero;

            // Над текущей горкой в этом месте — чтобы падали на неё, а не рождались внутри.
            float top = _pile.TopAt(local.x, local.z, _grainSize * 6f);
            local.y = Mathf.Max(local.y, top) + _grainSize * 6f;
            if (_shape == Shape.Fill)
            {
                local.x = Mathf.Clamp(local.x, -_footprint.x + _grainSize, _footprint.x - _grainSize);
                local.z = Mathf.Clamp(local.z, -_footprint.y + _grainSize, _footprint.y - _grainSize);
            }
            spread = Mathf.Max(_pourSpread, _grainSize);
            return local;
        }

        private Vector3 GrainSize(System.Random rnd)
        {
            float d = _grainSize * 2f;
            if (_pieces == Pieces.Chunks || _pieces == Pieces.None)
            {
                float k = 0.85f + 0.35f * (float)rnd.NextDouble();
                return new Vector3(d * k, d * k * 0.85f, d * k);
            }
            // Стружка: длиннее зерна, тоньше — лежат вразнобой и переплетаются.
            float len = d * Mathf.Lerp(_shredLength.x, _shredLength.y, (float)rnd.NextDouble()) * 0.7f;
            float th = d * Mathf.Lerp(_shredThickness.x, _shredThickness.y, (float)rnd.NextDouble()) * 1.6f;
            return new Vector3(th, th * 0.75f, len);
        }

        private Quaternion GrainRotation(System.Random rnd)
        {
            return Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 2f * _shredTilt,
                                    (float)rnd.NextDouble() * 360f,
                                    ((float)rnd.NextDouble() - 0.5f) * 2f * _shredTilt);
        }

        /// <summary>
        /// В редакторе частицы запекаются в обычный меш: Update в редакторе
        /// вызывается не каждый кадр, и мгновенная отрисовка мерцала бы.
        /// </summary>
        private void BakePileToMesh()
        {
            if (_mesh == null || _mr == null) return;
            _v.Clear(); _c.Clear(); _n.Clear(); _t.Clear();
            for (int i = 0; i < _pile.Count; i++)
            {
                float k = _pile.Shade(i) == 0 ? 0.9f : _pile.Shade(i) == 1 ? 1f : 1.08f;
                Color32 col = Shade(_shownColor, k);
                col.a = 0;
                AddBox(_pile.Position(i), _pile.Size(i), _pile.Rotation(i), col);
            }
            _mesh.Clear();
            FlareV(); _mesh.SetVertices(_v);
            _mesh.SetNormals(_n);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
            _mr.enabled = _pile.Count > 0;
        }

        private void UpdateParticles()
        {
            if (_pile == null || _pile.Count == 0 || !Application.isPlaying) return;
            Transform frame = _mf != null ? _mf.transform : transform;

            if (Application.isPlaying)
            {
                // Рывки тары → инерция содержимого.
                Vector3 fp = frame.position;
                if (_frameTracked && Time.deltaTime > 0f)
                {
                    Vector3 vel = (fp - _lastFramePos) / Time.deltaTime;
                    Vector3 acc = (vel - _lastFrameVel) / Time.deltaTime;
                    if (acc.sqrMagnitude > 1f) _pile.Shove(frame.InverseTransformDirection(acc), 1f / 60f);
                    _lastFrameVel = vel;
                }
                _lastFramePos = fp;
                _frameTracked = true;

                // Физика фиксированным шагом, не больше трёх шагов за кадр.
                Vector3 gLocal = frame.InverseTransformDirection(Physics.gravity);
                // Масштаб рамки снят ApplyFrame, но на всякий случай нормируем к мировой длине.
                gLocal = gLocal.normalized * Physics.gravity.magnitude;
                _simAccum = Mathf.Min(_simAccum + Time.deltaTime, 3f / 60f);
                while (_simAccum >= 1f / 60f)
                {
                    _pile.Step(1f / 60f, gLocal);
                    _simAccum -= 1f / 60f;
                }
            }

            if (_pile.Asleep) RebuildUnderMass(false);
            DrawParticles(frame);
        }

        /// <summary>
        /// Подложка-масса под стружками. Строится, когда горка улеглась, и только
        /// если её размер заметно изменился — не каждый кадр.
        /// </summary>
        private void RebuildUnderMass(bool force)
        {
            if (!_underMass || _mesh == null || _mr == null || _pile == null) return;
            if (_pile.Count == 0) { _mesh.Clear(); _mr.enabled = false; _massR = _massH = -1f; return; }

            float r = _pile.ExtentRadius * _underMassScale;
            float h = _pile.ExtentHeight * _underMassScale * 0.9f;
            if (!force && Mathf.Abs(r - _massR) < r * 0.05f && Mathf.Abs(h - _massH) < h * 0.05f) return;
            _massR = r; _massH = h;

            float rx = r, rz = r;
            if (_shape == Shape.Fill)
            {
                rx = Mathf.Min(r, _footprint.x);
                rz = Mathf.Min(r, _footprint.y);
            }

            _v.Clear(); _c.Clear(); _n.Clear(); _t.Clear();
            BuildSmooth(rx, rz, Mathf.Max(h, _grainSize * 2f), Mathf.Max(_voxel, _grainSize * 2f), _shownColor, _shownSeed);
            _mesh.Clear();
            FlareV(); _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            _mr.sharedMaterial = _material != null ? _material : AutoMaterial;
            _mr.enabled = true;
            RebuildStrands(_shownPortions, _shownSeed);
        }

        /// <summary>Разложить объёмную стружку по текущей поверхности массы.</summary>
        private void RebuildStrands(float portions, int seed)
        {
            RebuildWorms(portions, seed);
            if (!_strands || _mesh == null) { _strandField?.Clear(); return; }
            if (_strandField == null) _strandField = new FoodStrandField();
            int count = Mathf.Min(_maxStrands, Mathf.RoundToInt(Mathf.Max(0f, portions) * _strandsPerPortion));
            _strandField.Build(_mesh, count, new FoodStrandField.Settings
            {
                Length = _strandLength,
                Width = _strandWidth,
                LayerDepth = _strandLayer,
                StickOut = _strandStickOut,
                Bend = _strandBend,
                Twist = _strandTwist,
                Arch = _strandArch,
                Segments = _strandSegments,
                Clamp = _shape == Shape.Fill,
                ClampHalf = _footprint * 0.96f,
            }, seed * 7 + 3);
            ApplyMassVisibility();
        }

        /// <summary>Масса видна, если ленточки не должны её прятать.</summary>
        private void ApplyMassVisibility()
        {
            if (_mr == null) return;
            bool hide = _strands && _strandsHideMass && _strandField != null && _strandField.Count > 0;
            if (hide) _mr.enabled = false;
            else if (_mesh != null && _mesh.vertexCount > 0 && _shownPortions > 0f) _mr.enabled = true;
        }

        private void DrawParticles(Transform frame)
        {
            Material mat = _grainMaterial != null ? _grainMaterial : (_material != null ? _material : AutoMaterial);
            if (!mat.enableInstancing)
            {
                // Без этой галки Unity нарисует всё в одной точке или не нарисует вовсе.
                mat.enableInstancing = true;
            }
            EnsureGrainMeshes();
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.SetColor(BaseColorId, _shownColor);

            Matrix4x4 m = frame.localToWorldMatrix;
            _batchCount[0] = _batchCount[1] = _batchCount[2] = 0;

            RenderParams rp = new RenderParams(mat)
            {
                matProps = _mpb,
                layer = gameObject.layer,
                shadowCastingMode = _grainShadows && _castShadows ? UnityEngine.Rendering.ShadowCastingMode.On
                                                                  : UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = new Bounds(frame.position, Vector3.one * (Mathf.Max(_pile.ExtentRadius, 0.5f) * 4f)),
            };

            for (int i = 0; i < _pile.Count; i++)
            {
                int b = _pile.Shade(i);
                _batches[b][_batchCount[b]++] = m * Matrix4x4.TRS(_pile.Position(i), _pile.Rotation(i), _pile.Size(i));
                if (_batchCount[b] == 1023) Flush(rp, b);
            }
            for (int b = 0; b < 3; b++) Flush(rp, b);
        }

        private void Flush(RenderParams rp, int b)
        {
            if (_batchCount[b] == 0) return;
            Mesh mesh = b == 0 ? _grainMeshA : b == 1 ? _grainMeshB : _grainMeshC;
            Graphics.RenderMeshInstanced(rp, mesh, 0, _batches[b], _batchCount[b]);
            _batchCount[b] = 0;
        }

        /// <summary>Три меша-кубика 1×1×1 с разным оттенком в цвете вершин:
        /// соседние стружки чуть отличаются, а цвет продукта задаёт материал.</summary>
        private bool _grainGnocchi;
        private void EnsureGrainMeshes()
        {
            bool g = _runtimeLook == HeapPreset.Gnocchi;
            if (_grainMeshA != null && g == _grainGnocchi) return;
            _grainGnocchi = g;
            if (g)
            {
                // общие меши ньокков (не свои) — не удалять
                _grainMeshA = GnocchiMesh.Get(0.9f);
                _grainMeshB = GnocchiMesh.Get(1.0f);
                _grainMeshC = GnocchiMesh.Get(1.08f);
                return;
            }
            _grainMeshA = SoftShred(0.92f);
            _grainMeshB = SoftShred(1.0f);
            _grainMeshC = SoftShred(1.07f);
        }

        /// <summary>
        /// Мягкая стружка: брусок 1×1×1 вдоль Z со скруглёнными рёбрами и
        /// скруглёнными концами, нормали сглажены — свет обтекает края.
        /// Сечение — «скруглённый прямоугольник» (суперэллипс), Roundness задаёт,
        /// насколько он близок к кругу. U идёт вдоль длины (бороздки текстуры
        /// стружки ложатся вдоль), V — по окружности сечения.
        /// </summary>
        private Mesh SoftShred(float shade)
        {
            const int ring = 12;                               // точек в сечении
            Color32 col = new Color(Mathf.Clamp01(shade), Mathf.Clamp01(shade), Mathf.Clamp01(shade), 1f);
            // Суперэллипс: показатель 2 — круг, большой — квадрат.
            float e = Mathf.Lerp(8f, 2.2f, _grainRoundness);

            // Кольца вдоль длины: z и масштаб сечения (концы скруглены).
            float[] rz = { -0.5f, -0.47f, -0.40f, 0.40f, 0.47f, 0.5f };
            float[] rs = { 0.35f, 0.78f, 1.0f, 1.0f, 0.78f, 0.35f };
            float[] nzBias = { -0.9f, -0.45f, 0f, 0f, 0.45f, 0.9f };

            var v = new List<Vector3>(); var n = new List<Vector3>();
            var uv = new List<Vector2>(); var c = new List<Color32>(); var t = new List<int>();

            for (int r = 0; r < rz.Length; r++)
            {
                for (int k = 0; k <= ring; k++)                 // +1: шов для UV
                {
                    float a = k / (float)ring * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    // Точка суперэллипса и его нормаль.
                    float px = Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), 2f / e);
                    float py = Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), 2f / e);
                    float nx = Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), (2f * (e - 1f)) / e);
                    float ny = Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), (2f * (e - 1f)) / e);
                    Vector3 sideN = new Vector3(nx, ny, 0f).normalized;
                    v.Add(new Vector3(px * 0.5f * rs[r], py * 0.5f * rs[r], rz[r]));
                    n.Add((sideN + Vector3.forward * nzBias[r]).normalized);
                    uv.Add(new Vector2(rz[r] + 0.5f, k / (float)ring));
                    c.Add(col);
                }
            }
            int row = ring + 1;
            for (int r = 0; r < rz.Length - 1; r++)
            for (int k = 0; k < ring; k++)
            {
                int i0 = r * row + k, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                // Лицом наружу (проверено: нормаль грани смотрит от оси).
                t.Add(i0); t.Add(i1); t.Add(i2);
                t.Add(i1); t.Add(i3); t.Add(i2);
            }
            // Торцы — веером к центру.
            for (int end = 0; end < 2; end++)
            {
                int r = end == 0 ? 0 : rz.Length - 1;
                int centre = v.Count;
                v.Add(new Vector3(0f, 0f, rz[r]));
                n.Add(end == 0 ? Vector3.back : Vector3.forward);
                uv.Add(new Vector2(rz[r] + 0.5f, 0.5f));
                c.Add(col);
                for (int k = 0; k < ring; k++)
                {
                    int a0 = r * row + k, a1 = a0 + 1;
                    if (end == 0) { t.Add(centre); t.Add(a1); t.Add(a0); }
                    else { t.Add(centre); t.Add(a0); t.Add(a1); }
                }
            }

            Mesh mesh = new Mesh { name = "FoodShredSoft", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Кубик 1×1×1 с UV: на длинных гранях U идёт ВДОЛЬ длины стружки (ось Z),
        /// поэтому бороздки текстуры стружки ложатся вдоль неё. Касательные
        /// посчитаны — карта нормалей работает. Альфа вершин 1 — узор включён.
        /// </summary>
        private Mesh UnitBox(float shade)
        {
            Color32 col = new Color(Mathf.Clamp01(shade), Mathf.Clamp01(shade), Mathf.Clamp01(shade), 1f);
            var v = new List<Vector3>(24); var n = new List<Vector3>(24);
            var uv = new List<Vector2>(24); var c = new List<Color32>(24); var t = new List<int>(36);

            for (int f = 0; f < 6; f++)
            {
                Vector3 nn = FaceN[f];
                // Оси грани: для боковых граней вторая ось — Z (длина стружки).
                Vector3 a, b;
                if (Mathf.Abs(nn.z) > 0.5f) { a = Vector3.right; b = Vector3.up; }
                else { a = Vector3.forward; b = Vector3.Cross(nn, a); }
                // Порядок вершин — по часовой снаружи (лицевая сторона Unity).
                if (Vector3.Dot(Vector3.Cross(a, b), nn) < 0f) { Vector3 tmp = a; a = b; b = tmp; }

                int s0 = v.Count;
                Vector3 c0 = nn * 0.5f;
                Vector3[] corners =
                {
                    c0 - a * 0.5f - b * 0.5f, c0 - a * 0.5f + b * 0.5f,
                    c0 + a * 0.5f + b * 0.5f, c0 + a * 0.5f - b * 0.5f,
                };
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = corners[k];
                    v.Add(p); n.Add(nn); c.Add(col);
                    // U — вдоль длины (Z) на боковых гранях, иначе вдоль X.
                    float u = Mathf.Abs(nn.z) > 0.5f ? p.x + 0.5f : p.z + 0.5f;
                    float w = Mathf.Abs(nn.z) > 0.5f ? p.y + 0.5f : Vector3.Dot(p, Vector3.Cross(nn, Vector3.forward)) + 0.5f;
                    uv.Add(new Vector2(u, w));
                }
                t.Add(s0); t.Add(s0 + 2); t.Add(s0 + 1);
                t.Add(s0); t.Add(s0 + 3); t.Add(s0 + 2);
            }

            Mesh mesh = new Mesh { name = "FoodGrain", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ───────────────────────── ПОСТРОЕНИЕ ─────────────────────────

        private void Build(float portions, Color color, int seed)
        {
            _v.Clear(); _c.Clear(); _n.Clear(); _t.Clear();
            Radius = 0f; Height = 0f;

            if (portions <= 0.001f)
            {
                _mesh.Clear();
                _mr.enabled = false;
                _strandField?.Clear();
                if (_wormMr != null) _wormMr.enabled = false;
                return;
            }
            _mr.enabled = true;

            float volume = portions * _portionVolume;
            float voxel = _voxel;

            // Не больше Max Voxels: если не влезаем — кубики крупнее.
            float count = volume / (voxel * voxel * voxel);
            if (count > _maxVoxels) voxel *= Mathf.Pow(count / _maxVoxels, 1f / 3f);

            float rx, rz, h;
            if (_shape == Shape.Mound)
            {
                // Параболоид: V = π/2 · R² · H,  H = slope · R
                float r = Mathf.Pow(2f * volume / (Mathf.PI * _slope), 1f / 3f);
                r = Mathf.Max(r, voxel * 1.5f);
                rx = rz = r;
                h = _slope * r;
            }
            else
            {
                rx = Mathf.Max(_footprint.x, voxel);
                rz = Mathf.Max(_footprint.y, voxel);
                float area = Mathf.PI * rx * rz;
                // Профиль горки в таре: y(r) = край + (центр − край)·(1 − r²),
                // объём = площадь · (центр + край) / 2.
                //   Этап 1: край = ½ центра, центр растёт до Max Height.
                //   Этап 2: центр растёт до Max Height·(1 + Above Rim), край — до Max Height:
                //           горка поднимается куполом над бортом, края доходят до борта.
                // Переход между этапами непрерывный (проверено числами: без скачков).
                float centre, edge;
                FillMoments(out float aK, out float mom);
                area = aK * rx * rz;
                if (_fillCapacity > 0)
                {
                    // Высота — от доли заполнения тары (полная = купол), а не от Portion Volume.
                    float frac = _buildFull ? 1f : Mathf.Clamp01(portions / _fillCapacity);
                    FillShape(frac, out centre, out edge);
                }
                else FillFromVolume(volume, area, mom, out centre, out edge);
                h = centre;
                _fillBase = centre > 1e-5f ? edge / centre : 0.5f;
                // Настоящий объём горки (при расчёте от доли заполнения он другой) —
                // от него размер шариков массы, иначе шарики мелкие, а сетка грубая → дыры.
                volume = area * (edge + (centre - edge) * (1f - mom));
                voxel = _voxel;
                float cnt = volume / (voxel * voxel * voxel);
                if (cnt > _maxVoxels) voxel *= Mathf.Pow(cnt / _maxVoxels, 1f / 3f);
                // Мало — не растягиваем тонким слоем на всё дно, а собираем в центр.
                float minH = voxel * 2f;
                if (h < minH)
                {
                    float k = Mathf.Sqrt(Mathf.Max(0.05f, h / minH));
                    rx *= k; rz *= k; h = minH;
                }
            }

            Radius = Mathf.Max(rx, rz);
            Height = h;

            System.Random rnd = new System.Random(seed);

            // Тара: гладкая оболочка-«пузырь» вместо метаболов (без ступенек, дёшево).
            if (_shape == Shape.Fill && _fillShell && _microLumpsPerPortion == 0 &&
                (_grain == Grain.Blobby || _grain == Grain.Smooth))
            {
                BuildFillShell(rx, rz, h, color, seed);
                _mesh.Clear();
                FlareV(); _mesh.SetVertices(_v);
                _mesh.SetColors(_c);
                _mesh.SetTriangles(_t, 0);
                _mesh.RecalculateNormals();
                _mesh.RecalculateBounds();
                RebuildStrands(portions, seed);
                return;
            }

            if (_grain == Grain.Blobby)
            {
                BuildBlobby(portions, rx, rz, h, voxel, color, seed);
                _mesh.Clear();
                FlareV(); _mesh.SetVertices(_v);
                _mesh.SetNormals(_n);
                _mesh.SetColors(_c);
                _mesh.SetTriangles(_t, 0);
                _mesh.RecalculateBounds();
                RebuildStrands(portions, seed);
                return;
            }

            if (_grain == Grain.Smooth)
            {
                BuildSmooth(rx, rz, h, voxel, color, seed);
                _mesh.Clear();
                FlareV(); _mesh.SetVertices(_v);
                _mesh.SetColors(_c);
                _mesh.SetTriangles(_t, 0);
                _mesh.RecalculateNormals();
                _mesh.RecalculateBounds();
                RebuildStrands(portions, seed);
                return;
            }

            // Стружка тоньше кубика — чтобы кучка не просела, их больше.
            int perSlot = _grain == Grain.Shreds ? _shredDensity : 1;
            int nx = Mathf.CeilToInt(rx / voxel);
            int nz = Mathf.CeilToInt(rz / voxel);
            Color32 dark = Shade(color, 0.92f);

            for (int ix = -nx; ix <= nx; ix++)
            for (int iz = -nz; iz <= nz; iz++)
            {
                float x = ix * voxel, z = iz * voxel;
                float u = x / rx, w = z / rz;
                float r2 = u * u + w * w;
                if (r2 > 1f) continue;

                float profile = _shape == Shape.Mound ? (1f - r2) : (1f - 0.5f * r2);
                float colH = h * profile * (0.88f + 0.24f * (float)rnd.NextDouble());
                float layers = colH / voxel;
                int n = Mathf.FloorToInt(layers);
                if ((float)rnd.NextDouble() < layers - n) n++;
                if (n <= 0 && r2 < 0.6f) n = 1;

                for (int k = 0; k < n; k++)
                for (int m = 0; m < perSlot; m++)
                {
                    bool top = k == n - 1;
                    bool chunk = top && m == 0 && (float)rnd.NextDouble() < _chunkChance;
                    float depth = n > 1 ? 1f - (float)k / (n - 1) : 0f;
                    float j = ((float)rnd.NextDouble() - 0.5f) * 2f * _colorJitter;
                    Color32 col = chunk ? dark : Shade(color, 1f + j - depth * _depthShade);

                    Vector3 jitter = new Vector3((float)rnd.NextDouble() - 0.5f, ((float)rnd.NextDouble() - 0.5f) * 0.5f,
                                                 (float)rnd.NextDouble() - 0.5f) * voxel * 0.6f;
                    Vector3 center = new Vector3(x, voxel * (k + 0.5f), z) + jitter;

                    Vector3 size;
                    Quaternion rot;
                    if (chunk)
                    {
                        // Недотёртый кусочек: крупный кубик, повёрнут.
                        float cs = voxel * 1.55f * (0.9f + 0.2f * (float)rnd.NextDouble());
                        size = new Vector3(cs, cs, cs);
                        rot = Quaternion.Euler((float)rnd.NextDouble() * 40f, (float)rnd.NextDouble() * 360f,
                                               (float)rnd.NextDouble() * 40f);
                    }
                    else if (_grain == Grain.Shreds)
                    {
                        float len = voxel * Mathf.Lerp(_shredLength.x, _shredLength.y, (float)rnd.NextDouble());
                        float th = voxel * Mathf.Lerp(_shredThickness.x, _shredThickness.y, (float)rnd.NextDouble());
                        size = new Vector3(th, th * 0.8f, len);
                        rot = Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 2f * _shredTilt,
                                               (float)rnd.NextDouble() * 360f,
                                               ((float)rnd.NextDouble() - 0.5f) * 2f * _shredTilt);
                    }
                    else
                    {
                        float cs = voxel * (0.9f + 0.12f * (float)rnd.NextDouble());
                        size = new Vector3(cs, cs, cs);
                        rot = Quaternion.Euler(0f, ((float)rnd.NextDouble() - 0.5f) * 20f, 0f);
                    }

                    col.a = (byte)Mathf.RoundToInt(_boxDetail * 255f);
                    AddBox(center, size, rot, col);
                    if (_v.Count > 250000) break;
                }
            }

            _mesh.Clear();
            FlareV(); _mesh.SetVertices(_v);
            _mesh.SetNormals(_n);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
        }

        private static Color32 Shade(Color c, float k)
        {
            return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);
        }

        private static readonly Vector3[] FaceN =
        {
            Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back
        };

        /// <summary>
        /// Коробка: кубик, стружка, кусочек. Треугольники — ПО ЧАСОВОЙ, если
        /// смотреть снаружи: так Unity считает грань лицевой. (В прошлой версии
        /// порядок был обратный — видна была изнанка, и кучка казалась пустой.)
        /// </summary>
        private void AddBox(Vector3 center, Vector3 size, Quaternion rot, Color32 col)
        {
            Vector3 half = size * 0.5f;
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = FaceN[f];
                Vector3 a = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 b = Vector3.Cross(n, a);
                a = Vector3.Cross(b, n);

                Vector3 c0 = Vector3.Scale(n, half);
                Vector3 sa = Vector3.Scale(a, half);
                Vector3 sb = Vector3.Scale(b, half);

                int start = _v.Count;
                _v.Add(center + rot * (c0 - sa - sb));
                _v.Add(center + rot * (c0 - sa + sb));
                _v.Add(center + rot * (c0 + sa + sb));
                _v.Add(center + rot * (c0 + sa - sb));
                Vector3 rn = rot * n;
                for (int i = 0; i < 4; i++) { _n.Add(rn); _c.Add(col); }
                _t.Add(start); _t.Add(start + 2); _t.Add(start + 1);
                _t.Add(start); _t.Add(start + 3); _t.Add(start + 2);
            }
        }

        // ═════════════════════ МЕТАБОЛЫ (SURFACE NETS) ═════════════════════
        //
        // 1. По объёму кучки раскладываем шарики-метаболы (как кубики раньше,
        //    но крупнее и с перекрытием).
        // 2. Каждый шарик добавляет «плотность» в сетку вокруг себя.
        // 3. По сетке строим одну гладкую поверхность там, где плотность = порог
        //    (алгоритм Surface Nets: вершина в каждой ячейке, где поверхность
        //    проходит, и четырёхугольник на каждое пересечённое ребро).
        // 4. Нормали — из градиента плотности: свет ложится гладко.
        // 5. Сверху втыкаем кусочки (стружку или кубики), наполовину в массу.
        //
        // Одна поверхность без внутренних рёбер: обводка проекта не чернит
        // кучку, тени не забиваются между сотней кубиков.

        private float[] _field;
        private Vector3[] _grad;
        private int[] _cellVert;

        // ═════════════════════ v18: ВЕЩЕСТВО (FoodSubstance) ═════════════════════
        //
        // Если у продукта есть профиль вещества со своим видом (порошок, масло) — кучка рисуется
        // по профилю: одна форма и один материал на полу, в таре (HeapAnchor) и на полотне
        // мастерка/лопаты. Дрожь (джигл) — наклоном и приседанием меша, вес не меняется.

        [System.NonSerialized] private FoodSubstance _sub;
        [System.NonSerialized] private bool _subOn;
        [System.NonSerialized] private int _toolState = -1;
        private Vector3 _subPrevPos, _subPrevVel, _subAcc;
        private bool _subTracked;
        private Vector2 _tilt, _tiltVel;
        private float _subSquash, _subSquashVel, _dripCd, _steamCd;

        [System.NonSerialized] private bool _spoiled;

        /// <summary>v19: испорчено (состояние «Испорчено») — серо-зелёные пятна на любом веществе. Звать перед Show.</summary>
        public void SetSpoiled(bool on)
        {
            if (!Application.isPlaying || on == _spoiled) return;
            _spoiled = on;
            if (_sub != null && _shownPortions >= 0f)
            {
                float n = _shownPortions; Color c = _shownColor; int sd = _shownSeed;
                _shownPortions = -1f;
                Show(n, c, sd);
            }
        }

        /// <summary>Профиль вещества, которым сейчас рисуется кучка (или null).</summary>
        public FoodSubstance Substance => _sub;

        /// <summary>Рисовать по профилю вещества (null или профиль без своего вида — как раньше). Звать перед Show.</summary>
        public void SetSubstance(FoodSubstance s)
        {
            if (!Application.isPlaying) return;
            if (s != null && !s.OwnLook) s = null;
            if (s == _sub) return;
            _sub = s;
            if (_shownPortions >= 0f)
            {
                float n = _shownPortions; Color c = _shownColor; int sd = _shownSeed;
                _shownPortions = -1f;
                Show(n, c, sd);
            }
        }

        /// <summary>Полотно мастерка/лопаты (не тара и не кучка на полу).</summary>
        private bool OnTool
        {
            get
            {
                if (_toolState < 0)
                    _toolState = GetComponentInParent<Cookware>() == null && GetComponentInParent<FoodHeap>() == null
                                 && GetComponentInParent<CarriedContainer>() == null ? 1 : 0;
                return _toolState == 1;
            }
        }

        private float WorldK
        {
            get
            {
                if (_ignoreParentScale) return 1f;
                Vector3 ls = transform.lossyScale;
                return (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f;
            }
        }

        private void LeaveSubstance()
        {
            _subOn = false;
            _subTracked = false;
            _tilt = _tiltVel = Vector2.zero;
            _subSquash = _subSquashVel = 0f;
            if (_mr != null) _mr.sharedMaterial = _material != null ? _material : AutoMaterial;
            _refFull = false;
            _builtFor = 0f;
        }

        private void ShowSubstance(float before, float portions, Color color, int seed)
        {
            _subOn = true;
            _pile = null;
            _growScale = Vector3.one;
            _strandField?.Clear();
            if (_wormMr != null) _wormMr.enabled = false;
            if (portions <= 0.001f)
            {
                _mesh.Clear();
                _mr.enabled = false;
                Radius = 0f; Height = 0f;
                return;
            }
            Color col = _sub.ColorFor(color);
            FoodSubstanceMesh.Dims d = SubstanceDims(portions);
            bool tool = OnTool;
            FoodSubstanceMesh.SpoilK = _spoiled ? 1f : 0f;                     // v19: испорчено — видно на любом веществе
            if (_sub.Look == SubstanceLook.Powder)
                FoodSubstanceMesh.Powder(_mesh, d, _sub.Wonky, col, seed);
            else if (_sub.Look == SubstanceLook.Crystals)
                FoodSubstanceMesh.Crystals(_mesh, d, _sub.CrystalSize, _sub.CrystalDensity, col, seed);
            else if (_sub.Look == SubstanceLook.Spread)
                FoodSubstanceMesh.Spread(_mesh, d, _shape == Shape.Fill && !tool ? _sub.Curl * 0.5f : _sub.Curl, col, seed);
            else
            {
                float curl = _shape == Shape.Fill && !tool ? _sub.Curl * 0.5f : _sub.Curl;
                float depth = _sub.SmearDepth * (_shape == Shape.Fill && !tool ? _sub.CookwareRelief : 1f);
                if (tool) FoodSubstanceMesh.ButterSmear(_mesh, d, curl, col, seed);
                else FoodSubstanceMesh.Butter(_mesh, d, curl, _sub.Smears, depth, col, seed);
            }
            FoodSubstanceMesh.SpoilK = 0f;
            Material m = _sub.GetMaterial();
            if (m != null) _mr.sharedMaterial = m;
            _mr.enabled = true;
            Radius = Mathf.Max(d.Rx, d.Rz);
            Height = d.H;

            if (tool && before <= 0.001f)
            {
                PickBladeAxis();                                                  // зачерпнули на пустое полотно
                if (_subLogged != _sub)
                {
                    _subLogged = _sub;
                    Transform root = transform.root;
                    Debug.Log($"[Вещества] полотно «{root.name}»: «{_sub.Title}» — горка {d.Rx * 2f:0.###}×{d.Rz * 2f:0.###}×{d.H:0.###} м " +
                              $"(полотно {_footprint.x * 2f:0.###}×{_footprint.y * 2f:0.###}), нормаль полотна — ось {_bladeAxis}", this);
                }
            }
            if (before < 0f || !Application.isPlaying) return;
            float wk = WorldK;
            Vector3 top = transform.position + Vector3.up * d.H * wk;
            float size = Mathf.Max(d.Rx, d.Rz) * 2f * wk;
            if (portions > before + 0.01f)
            {
                KickSub(0.8f);
                if (tool) FoodSubstanceFx.Scoop(_sub, top, col, size);
                else FoodSubstanceFx.Drop(_sub, top, col, size);
            }
            else if (portions < before - 0.01f)
            {
                KickSub(-1f);
                if (!tool) FoodSubstanceFx.Scoop(_sub, top, col, size);
            }
        }

        /// <summary>Размеры горки вещества: на полу и на полотне — горка, в таре — заполнение по дну.</summary>
        private FoodSubstanceMesh.Dims SubstanceDims(float portions)
        {
            var d = new FoodSubstanceMesh.Dims();
            d.RectN = 2f;
            float volume = portions * _portionVolume;
            bool tool = OnTool;
            if (_shape == Shape.Mound || tool)
            {
                float slope = Mathf.Max(0.1f, _sub.FloorSlope * (tool ? _sub.BladeHeight : 1f));
                // параболоид: V = π/2 · R² · H,  H = slope · R
                float r = Mathf.Pow(2f * volume / (Mathf.PI * slope), 1f / 3f);
                float rx = r, rz = r, h = slope * r;
                if (tool)
                {
                    // v18.2: на полотне — горка ПО ФОРМЕ ПОЛОТНА и крупная: всегда не меньше BladeCover
                    // полотна (больше половины), растёт до краёв; объём — с запасом (только вид)
                    float fx = Mathf.Max(0.005f, _footprint.x * 0.94f), fz = Mathf.Max(0.005f, _footprint.y * 0.94f);
                    float m = Mathf.Min(fx, fz), big = Mathf.Max(fx, fz);
                    float vol = volume * _sub.BladeVolume;
                    float rv = Mathf.Pow(2f * vol / (Mathf.PI * slope), 1f / 3f);
                    float grow = Mathf.Clamp01(rv / big);
                    float cover = Mathf.Lerp(_sub.BladeCover, 1f, grow);
                    rx = fx * cover; rz = fz * cover;
                    h = 2f * vol / (Mathf.PI * rx * rz);
                    h = Mathf.Clamp(h, m * 0.35f, Mathf.Max(slope * m * 1.15f, m * 0.4f));
                    d.RectN = Mathf.Lerp(2.6f, _fillRect ? Mathf.Clamp(_rectSharpness, 2.6f, 5f) : 2.6f, grow);
                }
                d.Rx = rx; d.Rz = rz; d.H = h; d.Edge = 0f;
                return d;
            }

            float fxr = Mathf.Max(_footprint.x, _voxel), fzr = Mathf.Max(_footprint.y, _voxel);
            FillMoments(out float aK, out float mom);
            float area = aK * fxr * fzr;
            float centre, edge;
            if (_fillCapacity > 0) FillShape(Mathf.Clamp01(portions / _fillCapacity), out centre, out edge);
            else FillFromVolume(volume, area, mom, out centre, out edge);
            float hh = centre;
            float minH = _voxel * 2f;
            if (hh < minH)
            {
                float k = Mathf.Sqrt(Mathf.Max(0.05f, hh / minH));
                fxr *= k; fzr *= k; hh = minH;
            }
            d.Rx = fxr; d.Rz = fzr; d.H = hh;
            d.Edge = centre > 1e-5f ? Mathf.Clamp(edge / centre, 0.05f, 1f) : 0.5f;
            d.RectN = _fillRect ? _rectSharpness : 2f;
            return d;
        }

        // ═════════════════════ v18.4: ПОЛОТНО ЛОПАТЫ/МАСТЕРКА — САМ ===========
        //
        // Горка на полотне сама смотрит, что лежит на лопате (поля ScoopItem: продукт, состояние,
        // количество), и если у продукта есть профиль вещества — рисует его, прячет кубик
        // («BladePiece (auto)») и не слушает старые вызовы. Так вид не зависит от версии ScoopItem.

        [System.NonSerialized] private bool _toolOverride, _fromPoll, _pollTried, _pollLogged;
        [System.NonSerialized] private MonoBehaviour _scoop;
        [System.NonSerialized] private System.Reflection.FieldInfo _fProd, _fState, _fCount;
        [System.NonSerialized] private int _pollProd = -1, _pollState = -1, _pollCount = -1;

        private static int SyncInt(System.Reflection.FieldInfo f, object owner)
        {
            if (f == null || owner == null) return -1;
            object sv = f.GetValue(owner);
            if (sv == null) return -1;
            System.Reflection.PropertyInfo pv = sv.GetType().GetProperty("Value");
            object v = pv != null ? pv.GetValue(sv, null) : sv;
            try { return System.Convert.ToInt32(v); } catch { return -1; }
        }

        private void ToolPoll()
        {
            if (!_pollTried)
            {
                _pollTried = true;
                foreach (MonoBehaviour mb in GetComponentsInParent<MonoBehaviour>(true))
                    if (mb != null && mb.GetType().Name == "ScoopItem") { _scoop = mb; break; }
                if (_scoop != null)
                {
                    var fl = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                    System.Type ty = _scoop.GetType();
                    _fProd = ty.GetField("_loadProduct", fl);
                    _fState = ty.GetField("_loadState", fl);
                    _fCount = ty.GetField("_loadCount", fl);
                }
                bool ok = _scoop != null && _fProd != null && _fCount != null;
                Debug.Log(ok ? $"[Вещества] полотно «{transform.root.name}»: горка v18.4 подключена — масло/порошок рисуются по профилю, горка лежит на полотне"
                             : $"[Вещества] полотно «{transform.root.name}»: не нашёл у лопаты поля _loadProduct/_loadCount — вид по профилю на полотне не включится (пришли ScoopItem)", this);
            }
            if (_scoop == null || _fProd == null || _fCount == null) return;
            int prod = SyncInt(_fProd, _scoop), st = Mathf.Max(0, SyncInt(_fState, _scoop)), cnt = Mathf.Max(0, SyncInt(_fCount, _scoop));
            if (_toolOverride) HideBladePiece();
            if (prod == _pollProd && st == _pollState && cnt == _pollCount) return;
            _pollProd = prod; _pollState = st; _pollCount = cnt;

            FoodDatabase db = FoodDatabase.Instance;
            ProductDef def = db != null && prod > 0 ? db.Get((ushort)prod) : null;
            FoodSubstance sub = def != null ? FoodSubstance.Of(def, (FoodState)st) : null;
            bool own = sub != null && sub.OwnLook && cnt > 0;
            if (!own)
            {
                if (_toolOverride)
                {
                    _toolOverride = false;              // дальше — как раньше (ScoopItem сам зовёт Show)
                    SetSubstance(null);
                    _fromPoll = true;
                    if (cnt > 0 && def != null) FoodHeapLook.Apply(this, def, (FoodState)st);
                    Show(cnt, FoodColors.Of(def, (FoodState)st), _shownSeed);
                    _fromPoll = false;
                }
                return;
            }
            _toolOverride = true;
            if (!_pollLogged || _sub != sub)
            {
                _pollLogged = true;
                Debug.Log($"[Вещества] полотно «{transform.root.name}»: «{def.Title}» ×{cnt} → профиль «{sub.Title}»", this);
            }
            FishNet.Object.NetworkBehaviour nb = _scoop as FishNet.Object.NetworkBehaviour;
            int seed = nb != null && nb.IsSpawned ? nb.ObjectId * 31 + 5 : 3;
            _fromPoll = true;
            FoodHeapLook.Apply(this, def, (FoodState)st);
            SetSubstance(sub);
            Show(cnt, FoodColors.Of(def, (FoodState)st), seed);
            _fromPoll = false;
            HideBladePiece();
        }

        private void HideBladePiece()
        {
            Transform bp = transform.Find("BladePiece (auto)");
            if (bp != null && bp.gameObject.activeSelf) bp.gameObject.SetActive(false);
        }

        // v18.2: горка ЛЕЖИТ на полотне (поворачивается вместе с ним), а не стоит «вверх по миру»
        [System.NonSerialized] private bool _bladeAxisSet;
        [System.NonSerialized] private FoodSubstance _subLogged;
        [System.NonSerialized] private Vector3 _bladeAxis = Vector3.up;

        /// <summary>Какая ось Blade — «нормаль полотна»: та, что смотрела вверх, когда зачерпнули (обычно Y).</summary>
        private void PickBladeAxis()
        {
            Vector3[] axes = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            float best = Vector3.Dot(transform.up, Vector3.up);
            _bladeAxis = Vector3.up;
            if (best < 0.5f)
                for (int i = 1; i < axes.Length; i++)
                {
                    float d = Vector3.Dot(transform.TransformDirection(axes[i]), Vector3.up);
                    if (d > best) { best = d; _bladeAxis = axes[i]; }
                }
            _bladeAxisSet = true;
        }

        private void GlueToBlade()
        {
            if (!_bladeAxisSet) PickBladeAxis();
            _mf.transform.localRotation = Quaternion.FromToRotation(Vector3.up, _bladeAxis);
        }

        private void KickSub(float s)
        {
            if (_sub == null) return;
            float j = Mathf.Max(0.05f, _sub.Jiggle);
            _subSquashVel += s * 2.2f * j;
            _tiltVel += Random.insideUnitCircle * Mathf.Abs(s) * 0.8f * j;
        }

        private void UpdateSubstance()
        {
            float dt = Time.deltaTime;
            if (dt <= 1e-5f || _sub == null) { ApplyFrame(Vector3.one); return; }
            Vector3 pos = transform.position;
            if (!_subTracked) { _subTracked = true; _subPrevPos = pos; _subPrevVel = Vector3.zero; _subAcc = Vector3.zero; }
            Vector3 vel = (pos - _subPrevPos) / dt;
            Vector3 acc = (vel - _subPrevVel) / dt;
            _subPrevPos = pos; _subPrevVel = vel;
            if (vel.sqrMagnitude > 900f) acc = Vector3.zero;                    // телепорт / спавн
            _subAcc = Vector3.Lerp(_subAcc, acc, 1f - Mathf.Exp(-dt * 20f));    // сгладить сетевые рывки

            // дрожь: верх отстаёт от рывка (наклон), от вертикального рывка — приседает
            float jig = _sub.Jiggle;
            Vector2 target = Vector2.ClampMagnitude(new Vector2(-_subAcc.x, -_subAcc.z) * 0.012f * jig, 0.35f);
            float sq = Mathf.Clamp(-_subAcc.y * 0.004f * jig, -0.25f, 0.25f);
            float k = Mathf.Lerp(320f, 90f, jig), damp = Mathf.Lerp(20f, 6f, jig);
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = (target - _tilt) * k - _tiltVel * damp;
                _tiltVel += a * h; _tilt += _tiltVel * h;
                float sa = (sq - _subSquash) * k - _subSquashVel * damp;
                _subSquashVel += sa * h; _subSquash += _subSquashVel * h;
            }
            _tilt = Vector2.ClampMagnitude(_tilt, 0.4f);
            _subSquash = Mathf.Clamp(_subSquash, -0.3f, 0.3f);
            ApplyFrame(new Vector3(1f - _subSquash * 0.5f, 1f + _subSquash, 1f - _subSquash * 0.5f));
            if (OnTool) GlueToBlade();
            Vector3 ax = new Vector3(_tilt.y, 0f, -_tilt.x);
            float ang = ax.magnitude * Mathf.Rad2Deg;
            if (ang > 0.01f) _mf.transform.rotation = Quaternion.AngleAxis(ang, ax / ax.magnitude) * _mf.transform.rotation;

            if (_shownPortions <= 0.001f || !_mr.enabled) return;
            float wk = WorldK;
            // капли/крошки с края полотна, когда дёргаешь (только вид)
            _dripCd -= dt;
            if (_sub.EdgeDrips && OnTool && _dripCd <= 0f && _subAcc.magnitude > _sub.DripShake)
            {
                _dripCd = Random.Range(0.12f, 0.3f);
                Vector3 side = Vector3.ProjectOnPlane(-_subAcc, Vector3.up);
                side = side.sqrMagnitude > 1e-6f ? side.normalized : Random.onUnitSphere;
                Vector3 at = pos + side * Radius * wk * 0.9f + Vector3.up * Height * wk * 0.3f;
                float ds = Mathf.Max(0.006f, Radius * 2f * wk * _sub.DripSize);
                FoodSubstanceFx.Drip(_sub, at, vel + side * 0.3f, _sub.ColorFor(_shownColor), ds);
            }
            // лёгкий пар
            _steamCd -= dt;
            if (_sub.Steam && _steamCd <= 0f)
            {
                _steamCd = Random.Range(0.5f, 1f);
                FoodSubstanceFx.Steam(pos + Vector3.up * Height * wk + Random.insideUnitSphere * Radius * wk * 0.4f, Radius * wk);
            }
        }

        // ═════════════════════ ВЯЗКОСТЬ ═════════════════════
        //
        // Масса не пересчитывается каждый кадр (это дорого) — её растекание и
        // колыхание делаются масштабом: шире и ниже при том же объёме, плюс
        // затухающая пружина. Для каши выглядит как настоящее оседание.

        private Vector3 ViscousScale(float dt)
        {
            if (_flow <= 0.001f && _wobble <= 0.001f) return Vector3.one;

            // Растекание: от 0 к 1 за Flow Time.
            if (_spread < 1f) _spread = Mathf.Min(1f, _spread + dt / Mathf.Max(0.1f, _flowTime));
            float e = 1f - (1f - _spread) * (1f - _spread);        // быстро в начале, плавно в конце
            // В таре растекаться некуда — только колыхание.
            float wide = _shape == Shape.Fill ? 1f : Mathf.Lerp(0.88f, 1f + 0.22f * _flow, e);

            // Пружина колыхания (затухающая).
            float k = 90f, damp = 7f;
            _jigVel += (-k * _jig - damp * _jigVel) * dt;
            _jig += _jigVel * dt;
            float j = Mathf.Clamp(_jig, -0.35f, 0.35f) * _wobble;

            // Объём сохраняется: шире — значит ниже.
            float sx = wide * (1f + j * 0.5f);
            float sy = 1f / (wide * wide) * (1f - j);
            return new Vector3(sx, sy, sx);
        }

        /// <summary>Толкнуть массу: в неё сыплют, черпают, она упала.</summary>
        private void Kick(float strength)
        {
            if (_wobble <= 0f && _flow <= 0f) return;
            _jigVel += strength * 6f;
        }

        /// <summary>
        /// Капли с края: комочки (масса) или брусочки (частицы) падают из точки,
        /// шлёпаются и исчезают. Только визуал. Зовёт лопата, когда черпает.
        /// </summary>
        public void SpawnDrops(Vector3 worldPoint, Vector3 outward, int count)
        {
            if (count <= 0 || _shownPortions < 0f) return;
            if (_subOn && _sub != null)
            {
                // v18: капли/крошки — по веществу (комочки масла, крошки порошка)
                Color sc = _sub.ColorFor(_shownColor);
                float ds = Mathf.Max(0.006f, Radius * 2f * _sub.DripSize);
                for (int i = 0; i < count; i++)
                    FoodSubstanceFx.Drip(_sub, worldPoint + Random.insideUnitSphere * ds, outward * Random.Range(0.3f, 0.7f) + Vector3.up * 0.4f, sc, ds);
                return;
            }
            bool mass = _grain != Grain.Particles && _grain != Grain.Cubes && _grain != Grain.Shreds;
            Mesh mesh;
            Material mat;
            Vector3 size;
            if (mass)
            {
                mesh = DropSphere;
                mat = _material != null ? _material : AutoMaterial;
                float r = Mathf.Max(_voxel * _blobSize * 0.5f, 0.01f) * _dropScale;
                size = new Vector3(r, r * 0.85f, r) * 2f;
            }
            else
            {
                EnsureGrainMeshes();
                mesh = _grainMeshB;
                mat = _grainMaterial != null ? _grainMaterial : (_material != null ? _material : AutoMaterial);
                float d = _grainSize * 2f * _dropScale;
                size = new Vector3(d * 0.6f, d * 0.5f, d * 1.8f);
            }
            for (int i = 0; i < count; i++)
            {
                Vector3 v = outward * Random.Range(0.3f, 0.8f) + Random.insideUnitSphere * 0.3f;
                Vector3 at = worldPoint + Random.insideUnitSphere * size.x * 0.5f;
                FoodDropFx.Spawn(at, v, mesh, mat, _shownColor, size * Random.Range(0.75f, 1.2f),
                                 mass && _flow > 0.1f, gameObject.layer);
            }
        }

        /// <summary>Та же сфера для других визуалов (масло в ведре, брызги кувалды).</summary>
        internal static Mesh DropSphereMesh => DropSphere;

        /// <summary>Низкополигональная сфера с белым цветом вершин (цвет даёт материал).</summary>
        private static Mesh DropSphere
        {
            get
            {
                if (_dropSphere != null) return _dropSphere;
                const int lat = 8, lon = 12;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
                for (int a = 0; a <= lat; a++)
                for (int b = 0; b <= lon; b++)
                {
                    float th = a / (float)lat * Mathf.PI, ph = b / (float)lon * Mathf.PI * 2f;
                    Vector3 p = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(p * 0.5f); n.Add(p); c.Add(new Color32(255, 255, 255, 255));
                }
                for (int a = 0; a < lat; a++)
                for (int b = 0; b < lon; b++)
                {
                    int i0 = a * (lon + 1) + b, i1 = i0 + 1, i2 = i0 + lon + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i1); t.Add(i2);
                    t.Add(i1); t.Add(i3); t.Add(i2);
                }
                _dropSphere = new Mesh { name = "FoodDropSphere", hideFlags = HideFlags.DontSave };
                _dropSphere.SetVertices(v); _dropSphere.SetNormals(n); _dropSphere.SetColors(c);
                _dropSphere.SetTriangles(t, 0); _dropSphere.RecalculateBounds();
                return _dropSphere;
            }
        }

        // ═════════════════════ ХАОС: бугры, макушка, капли ═════════════════════
        //
        // Одна и та же раскладка для метаболов и живых частиц: от seed, у всех
        // игроков одинаковая. Растёт с порциями: больше порций — больше бугров
        // и капель вокруг.

        private struct Lobe { public float X, Z, R, H; }
        private readonly List<Lobe> _lobes = new List<Lobe>();
        private readonly List<Lobe> _sats = new List<Lobe>();
        private float _planRx, _planRz, _planMain;
        // Оптимизация: «надувание»
        private int _fillCapacity;
        private Vector3 _growScale = Vector3.one;
        private float _builtFor = -1f;
        private bool _refFull, _buildFull;
        private Color _refColor;
        private int _refSeed;
        private float _centreFull = 1f;
        // Доля высоты, где кончается ровное заполнение и начинается купол над краем (Fill).
        // 0 — купола нет, профиль прежний.
        private float _fillBase = 0.5f;
        private bool _rebuildPending;

        private void PlanChaos(float portions, float rx, float rz, float unit, int seed)
        {
            _lobes.Clear();
            _sats.Clear();
            _planRx = rx; _planRz = rz;
            // Главная горка чуть меньше — бугры по краям делают контур неровным.
            _planMain = _shape == Shape.Fill ? 1f : 1f - 0.32f * _chaos;   // в таре — во всё дно
            if (_chaos <= 0.001f) { _planMain = 1f; return; }

            System.Random rnd = new System.Random(seed * 31 + 17);
            int lobes = Mathf.Clamp(Mathf.RoundToInt(_chaos * (1.5f + portions / 5f)), 1, 6);
            // В таре — строгая форма: бугров столько, сколько разрешает Tray Chaos.
            if (_shape == Shape.Fill) lobes = Mathf.RoundToInt(lobes * _trayChaos);
            for (int i = 0; i < lobes; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                // Наплывы у края главной горки — они и делают контур неровным
                // (проверено на виде сверху: ближе к центру их просто не видно).
                float d = Mathf.Lerp(0.55f, 1.0f, (float)rnd.NextDouble());
                _lobes.Add(new Lobe
                {
                    X = Mathf.Cos(a) * d * rx,
                    Z = Mathf.Sin(a) * d * rz,
                    R = Mathf.Lerp(0.4f, 0.65f, (float)rnd.NextDouble()) * Mathf.Max(rx, rz),
                    H = Mathf.Lerp(0.5f, 0.9f, (float)rnd.NextDouble()),
                });
            }

            if (!_satellites || _shape == Shape.Fill) return;
            int sats = Mathf.Clamp(Mathf.RoundToInt(_chaos * (1.5f + Mathf.Sqrt(portions) * 1.5f)), 0, 9);
            for (int i = 0; i < sats; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float d = Mathf.Lerp(1.1f, 1.8f, (float)rnd.NextDouble());
                _sats.Add(new Lobe
                {
                    X = Mathf.Cos(a) * d * rx,
                    Z = Mathf.Sin(a) * d * rz,
                    R = unit * Mathf.Lerp(0.6f, 1.6f, (float)rnd.NextDouble()),
                    H = Mathf.Lerp(0.3f, 0.7f, (float)rnd.NextDouble()),
                });
            }
        }

        /// <summary>Высота горки в точке, доля от h. ≤0 — горки нет.</summary>
        private float ChaosProfile(float x, float z)
        {
            float u = x / (_planRx * _planMain), w = z / (_planRz * _planMain);
            float r2 = _shape == Shape.Fill ? RectR2(u, w) : u * u + w * w;
            float best;
            if (_shape == Shape.Mound) best = 1f - r2;
            else if (r2 > 1f) best = -1f;
            else best = _fillBase + (1f - _fillBase) * (1f - Mathf.Pow(r2, _topFlatness * 0.5f));   // край + верх (см. Build)
            foreach (Lobe l in _lobes)
            {
                float dx = x - l.X, dz = z - l.Z;
                float q = 1f - (dx * dx + dz * dz) / (l.R * l.R);
                if (q > 0f) best = Mathf.Max(best, q * l.H);
            }
            if (_shape == Shape.Fill && (Mathf.Abs(x) > _footprint.x || Mathf.Abs(z) > _footprint.y)) return -1f;
            return best;
        }

        /// <summary>Насколько далеко от центра может что-то лежать (с каплями).</summary>
        private float ChaosExtent()
        {
            float e = Mathf.Max(_planRx, _planRz);
            foreach (Lobe l in _lobes) e = Mathf.Max(e, Mathf.Sqrt(l.X * l.X + l.Z * l.Z) + l.R);
            foreach (Lobe s in _sats) e = Mathf.Max(e, Mathf.Sqrt(s.X * s.X + s.Z * s.Z) + s.R * 1.5f);
            return e;
        }

        private void ApplyPreset(HeapPreset p)
        {
            switch (p)
            {
                case HeapPreset.SoftSticks:
                    _grain = Grain.Particles; _pieces = Pieces.Shreds;
                    _grainRoundness = 0.65f; _grainStaticFriction = 1.2f; _grainFriction = 0.8f;
                    _shredLength = new Vector2(2.2f, 3.6f); _shredThickness = new Vector2(0.35f, 0.55f);
                    _shredTilt = 25f; _underMass = false; _strands = false; _strandsHideMass = false; _microLumpsPerPortion = 0;
                    _chaos = 0.6f; _peak = 0f; _satellites = false; _flow = 0f; _wobble = 0f;
                    break;
                case HeapPreset.Cubes:
                    _grain = Grain.Particles; _pieces = Pieces.Chunks;
                    _grainRoundness = 0.15f; _grainStaticFriction = 0.9f; _grainFriction = 0.7f;
                    _shredTilt = 35f; _underMass = false; _strands = false; _strandsHideMass = false; _microLumpsPerPortion = 0;
                    _chaos = 0.6f; _peak = 0f; _satellites = false; _flow = 0f; _wobble = 0f;
                    break;
                case HeapPreset.GratedStrands:
                    // Кучка ЦЕЛИКОМ из ленточек: масса спрятана и служит только формой,
                    // ленточки лежат толстым слоем в несколько рядов.
                    _grain = Grain.Blobby; _pieces = Pieces.None; _microLumpsPerPortion = 0;
                    _blobSize = 1.6f; _iso = 0.32f; _surfaceCell = 0.55f;
                    _strands = true; _strandsHideMass = true;
                    _strandsPerPortion = 1600; _maxStrands = 120000;
                    _strandStickOut = 0.35f; _strandLayer = 0.06f;
                    _strandBend = 1.8f; _strandTwist = 1.4f; _strandArch = 0.16f;
                    _chaos = 0.65f; _peak = 0.25f; _satellites = false; _flow = 0f; _wobble = 0f;
                    break;
                case HeapPreset.Gnocchi:
                    // Ньокки: живые частицы, каждая — рифлёный ньокк (GnocchiMesh), одна штука на порцию, крупные.
                    _grain = Grain.Particles; _pieces = Pieces.Shreds;
                    _grainSize = 0.05f * Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
                    _grainsPerPortion = 1; _maxGrains = 400;
                    _shredLength = new Vector2(2.6f, 3.0f); _shredThickness = new Vector2(0.62f, 0.74f);
                    _grainRoundness = 0.8f; _grainStaticFriction = 1.4f; _grainFriction = 0.9f;
                    _shredTilt = 12f; _underMass = false; _strands = false; _strandsHideMass = false; _microLumpsPerPortion = 0;
                    _chaos = 0.25f; _peak = 0f; _satellites = false; _flow = 0f; _wobble = 0f;
                    break;
                case HeapPreset.Whole:
                    goto case HeapPreset.Gnocchi;                     // рисуется так же — целыми штуками без физики (меш — клубень)
                case HeapPreset.Mince:
                    // Фарш: видимая бугристая масса + много коротких толстых завитков сверху
                    // (как из мясорубки), мелкие комочки, крапинки жира — цвет даёт продукт.
                    _grain = Grain.Blobby; _pieces = Pieces.None;
                    _blobSize = 1.3f; _iso = 0.34f; _surfaceCell = 0.5f;
                    _microLumpsPerPortion = 0; _chunkChance = 0f;
                    _strands = false; _strandsHideMass = false;          // вместо ленточек — колбаски (MinceWorms)
                    _strandsPerPortion = 1100; _maxStrands = 120000;
                    _strandLength = new Vector2(0.012f, 0.026f); _strandWidth = new Vector2(0.005f, 0.008f);
                    _strandLayer = 0.035f; _strandStickOut = 0.2f;
                    _strandBend = 3.2f; _strandTwist = 2.2f; _strandArch = 0.3f; _strandSegments = 6;
                    _colorJitter = 0.04f; _depthShade = 0.3f;
                    _chaos = 0.45f; _peak = 0.3f; _satellites = false; _flow = 0.05f; _wobble = 0.2f;
                    break;
                case HeapPreset.Powder:
                    // Сыпучий порошок: гладкий конус с острой макушкой, без кусочков и
                    // ленточек, немного «щепоток» вокруг. В таре — ровный слой.
                    _grain = Grain.Smooth; _pieces = Pieces.None; _microLumpsPerPortion = 0;
                    _lumpiness = 0.05f; _lumpSize = 1.5f; _slope = 0.8f;
                    _strands = false; _strandsHideMass = false;
                    _colorJitter = 0.02f; _depthShade = 0.08f;
                    _chaos = 0.2f; _peak = 0.8f; _satellites = true; _flow = 0f; _wobble = 0f;
                    break;
                case HeapPreset.Liquid:
                    // Жидкость на лопате/в полёте: плоская гладкая лужица, дрожит.
                    _grain = Grain.Smooth; _pieces = Pieces.None; _microLumpsPerPortion = 0;
                    _lumpiness = 0.02f; _lumpSize = 3f; _slope = 0.25f;
                    _strands = false; _strandsHideMass = false;
                    _colorJitter = 0f; _depthShade = 0.04f;
                    _chaos = 0.1f; _peak = 0f; _satellites = false; _flow = 1f; _wobble = 1f; _flowTime = 0.6f;
                    break;
                case HeapPreset.Dollop:
                    // Густая масса: гладкий наплыв с мягкой макушкой, чуть оплывает.
                    _grain = Grain.Smooth; _pieces = Pieces.None; _microLumpsPerPortion = 0;
                    _lumpiness = 0.12f; _lumpSize = 2.2f; _slope = 0.6f;
                    _strands = false; _strandsHideMass = false;
                    _colorJitter = 0.01f; _depthShade = 0.06f;
                    _chaos = 0.35f; _peak = 0.55f; _satellites = false; _flow = 0.35f; _wobble = 0.8f; _flowTime = 1.4f;
                    break;
                case HeapPreset.Puree:
                    // Рыхлая «манная каша»: гладкая масса + много мелких комочков.
                    _grain = Grain.Blobby; _pieces = Pieces.None;
                    _blobSize = 2.0f; _iso = 0.3f; _surfaceCell = 0.42f;
                    _microLumpsPerPortion = 60; _microLumpSize = 0.45f; _microLumpRaise = 0.35f;
                    _strands = false; _strandsHideMass = false;
                    _chaos = 0.5f; _peak = 0.6f; _satellites = true; _flow = 0.7f; _wobble = 0.6f; _flowTime = 1.6f;
                    break;
            }
        }

        [ContextMenu("Ленточки: крупнее ×2, реже ×2")]
        private void StrandsBiggerSparser()
        {
            _strandLength *= 2f;
            _strandWidth *= 2f;
            _strandLayer = Mathf.Min(0.3f, _strandLayer * 2f);
            _strandsPerPortion = Mathf.Max(20, _strandsPerPortion / 2);
            _maxStrands = Mathf.Max(1000, _maxStrands / 2);
            Debug.Log($"[Кучка] ленточки: длина {_strandLength}, ширина {_strandWidth}, слой {_strandLayer}, " +
                      $"на порцию {_strandsPerPortion}", this);
            _rebuildPending = true;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            if (!Application.isPlaying) OnValidate();
#endif
        }

        [ContextMenu("Ленточки: крупнее ×3, реже ×3")]
        private void StrandsBiggerSparser3()
        {
            _strandLength *= 3f;
            _strandWidth *= 3f;
            _strandLayer = Mathf.Min(0.4f, _strandLayer * 3f);
            _strandsPerPortion = Mathf.Max(10, _strandsPerPortion / 3);
            _maxStrands = Mathf.Max(1000, _maxStrands / 3);
            Debug.Log($"[Кучка] ленточки: длина {_strandLength}, ширина {_strandWidth}, слой {_strandLayer}, " +
                      $"на порцию {_strandsPerPortion}", this);
            _rebuildPending = true;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            if (!Application.isPlaying) OnValidate();
#endif
        }

        [ContextMenu("Применить пресет заново")]
        private void ReapplyPreset()
        {
            ApplyPreset(_preset);
            _appliedPreset = _preset;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            if (!Application.isPlaying) OnValidate();
#endif
        }

        /// <summary>
        /// ОБОЛОЧКА ТАРЫ: один гладкий меш по форме дна. Сверху — поверхность по
        /// профилю (ровно у краёв + горка), по периметру — вертикальный бок до дна.
        /// Точки сетки за пределами дна прижимаются к границе, поэтому край ровный
        /// и точно по форме (прямоугольник со скруглёнными углами).
        /// </summary>
        private void BuildFillShell(float rx, float rz, float h, Color color, int seed)
        {
            _planRx = rx; _planRz = rz; _planMain = 1f;
            PlanChaos(Mathf.Max(1f, _shownPortions), rx, rz, _voxel, seed);   // бугры — только если Tray Chaos > 0

            int resX = Mathf.Clamp(Mathf.CeilToInt(rx / Mathf.Max(rx, rz) * 56f), 12, 56);
            int resZ = Mathf.Clamp(Mathf.CeilToInt(rz / Mathf.Max(rx, rz) * 56f), 12, 56);
            float ox = (seed % 997) * 0.173f, oz = (seed / 997 % 997) * 0.311f;
            float noiseF = 3f / Mathf.Max(0.05f, Mathf.Min(rx, rz));
            Color32 col = Shade(color, 1f);
            col.a = 255;

            // ── верх: сетка (resX+1)×(resZ+1) ──
            for (int iz = 0; iz <= resZ; iz++)
            for (int ix = 0; ix <= resX; ix++)
            {
                float u = ix / (float)resX * 2f - 1f;
                float w = iz / (float)resZ * 2f - 1f;
                float r2 = RectR2(u, w);
                if (r2 > 1f)
                {
                    // Прижать к границе дна вдоль луча из центра.
                    float k = 1f / Mathf.Sqrt(r2);
                    u *= k; w *= k;
                }
                float x = u * rx, z = w * rz;
                float prof = Mathf.Max(0f, ChaosProfile(x * 0.9999f, z * 0.9999f));
                // Неровность верха: крупные бугры + мелкая рябь. У края слабее —
                // бока остаются ровными, «хаос» только сверху.
                float big = Mathf.PerlinNoise(ox + x * noiseF, oz + z * noiseF) - 0.5f;
                float small = Mathf.PerlinNoise(ox * 1.7f + x * noiseF * 3.1f, oz * 1.3f + z * noiseF * 3.1f) - 0.5f;
                float topW = Mathf.Clamp01(1f - r2 * 1.1f);            // 1 в центре, 0 у стенки
                float n = (big * 2f + small * 0.8f) * _shellNoise * (0.25f + 0.75f * topW);
                float y = h * prof * (1f + n);
                _v.Add(new Vector3(x, y, z));
                _c.Add(col);
            }
            int row = resX + 1;
            for (int iz = 0; iz < resZ; iz++)
            for (int ix = 0; ix < resX; ix++)
            {
                int i0 = iz * row + ix, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                _t.Add(i0); _t.Add(i2); _t.Add(i1);
                _t.Add(i1); _t.Add(i2); _t.Add(i3);
            }

            // ── бок: периметр сетки → вниз до дна ──
            var loop = new List<int>();
            for (int ix = 0; ix < resX; ix++) loop.Add(ix);                                  // низ (z = −rz)
            for (int iz = 0; iz < resZ; iz++) loop.Add(iz * row + resX);                    // правый край
            for (int ix = resX; ix > 0; ix--) loop.Add(resZ * row + ix);                    // верх
            for (int iz = resZ; iz > 0; iz--) loop.Add(iz * row);                           // левый край
            int count = loop.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = _v[loop[i]], b = _v[loop[(i + 1) % count]];
                if ((a - b).sqrMagnitude < 1e-10f) continue;                            // прижатые к углу совпали
                int s0 = _v.Count;
                _v.Add(a); _v.Add(b);
                _v.Add(new Vector3(b.x, 0f, b.z)); _v.Add(new Vector3(a.x, 0f, a.z));
                Color32 side = Shade(color, 1f - _depthShade * 0.3f);
                side.a = 255;
                _c.Add(side); _c.Add(side); _c.Add(side); _c.Add(side);
                // Бок смотрит наружу — проверяем по направлению от центра.
                Vector3 fn = Vector3.Cross(b - a, new Vector3(a.x, 0f, a.z) - a);
                Vector3 outward = new Vector3((a.x + b.x) * 0.5f, 0f, (a.z + b.z) * 0.5f);
                if (Vector3.Dot(fn, outward) >= 0f)
                {
                    _t.Add(s0); _t.Add(s0 + 1); _t.Add(s0 + 3);
                    _t.Add(s0 + 1); _t.Add(s0 + 2); _t.Add(s0 + 3);
                }
                else
                {
                    _t.Add(s0); _t.Add(s0 + 3); _t.Add(s0 + 1);
                    _t.Add(s0 + 1); _t.Add(s0 + 3); _t.Add(s0 + 2);
                }
            }
        }

        private void BuildBlobby(float portions, float rx, float rz, float h, float voxel, Color color, int seed)
        {
            System.Random rnd = new System.Random(seed);
            float blobR = voxel * _blobSize;
            float cell = voxel * _surfaceCell;

            PlanChaos(portions, rx, rz, blobR, seed);
            float ext = ChaosExtent();
            float exX = _shape == Shape.Fill ? rx : Mathf.Max(rx, ext);
            float exZ = _shape == Shape.Fill ? rz : Mathf.Max(rz, ext);
            float peakEff = _shape == Shape.Fill ? _peak * _trayChaos : _peak;
            float peakH = h * peakEff * 0.45f;

            // ── сетка ──
            // Два прохода: если сетка упёрлась в предел и ячейка выросла, шарики
            // увеличиваются (иначе дыры), а границы сетки пересчитываются под них
            // (иначе поверхность обрежется сверху — тоже дыры).
            float pad = 0f;
            Vector3 min = default(Vector3), max = default(Vector3);
            int nx = 0, ny = 0, nz = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                pad = blobR * 1.3f;
                min = new Vector3(-exX - pad, -cell * 1.5f, -exZ - pad);
                max = new Vector3(exX + pad, h + peakH + pad, exZ + pad);
                nx = Mathf.Clamp(Mathf.CeilToInt((max.x - min.x) / cell) + 1, 4, 128);
                ny = Mathf.Clamp(Mathf.CeilToInt((max.y - min.y) / cell) + 1, 4, 96);
                nz = Mathf.Clamp(Mathf.CeilToInt((max.z - min.z) / cell) + 1, 4, 128);
                cell = Mathf.Max(cell, Mathf.Max((max.x - min.x) / (nx - 1), Mathf.Max((max.y - min.y) / (ny - 1), (max.z - min.z) / (nz - 1))));
                if (blobR >= cell * 1.8f) break;
                blobR = cell * 1.8f;
            }

            int total = nx * ny * nz;
            if (_field == null || _field.Length < total) { _field = new float[total]; _grad = new Vector3[total]; }
            System.Array.Clear(_field, 0, total);

            // ── шарики ──
            float step = blobR * 0.85f;
            float mainX = _shape == Shape.Fill ? rx : Mathf.Max(rx, ext * 0.8f);
            float mainZ = _shape == Shape.Fill ? rz : Mathf.Max(rz, ext * 0.8f);
            int bx = Mathf.CeilToInt(mainX / step), bz = Mathf.CeilToInt(mainZ / step);
            float invR2 = 1f / (blobR * blobR);
            int reach = Mathf.CeilToInt(blobR * 2.2f / cell) + 1;

            for (int ix = -bx; ix <= bx; ix++)
            for (int iz = -bz; iz <= bz; iz++)
            {
                float x = ix * step, z = iz * step;
                float profile = ChaosProfile(x, z);
                if (profile <= 0f) continue;
                float colH = Mathf.Max(blobR * 0.5f, h * profile * (0.9f + 0.2f * (float)rnd.NextDouble()));
                int layers = Mathf.Max(1, Mathf.CeilToInt(colH / step));

                for (int k = 0; k < layers; k++)
                {
                    float y = Mathf.Min(colH - blobR * 0.35f, (k + 0.5f) * step);
                    Vector3 c = new Vector3(x, Mathf.Max(0f, y), z) +
                                new Vector3((float)rnd.NextDouble() - 0.5f, 0f, (float)rnd.NextDouble() - 0.5f) * step * 0.6f;
                    float rMul = 0.85f + 0.3f * (float)rnd.NextDouble();
                    Splat(c, blobR * rMul, invR2 / (rMul * rMul), reach, min, cell, nx, ny, nz);
                }
            }

            // ── комочки на поверхности: рыхлая «каша» ──
            if (_microLumpsPerPortion > 0)
            {
                int lumps = Mathf.Min(900, Mathf.RoundToInt(portions * _microLumpsPerPortion));
                float lr = blobR * _microLumpSize;
                float ex = _shape == Shape.Fill ? rx : Mathf.Max(rx, ext * 0.8f);
                float ez = _shape == Shape.Fill ? rz : Mathf.Max(rz, ext * 0.8f);
                for (int i = 0; i < lumps; i++)
                {
                    float x = ((float)rnd.NextDouble() * 2f - 1f) * ex;
                    float z = ((float)rnd.NextDouble() * 2f - 1f) * ez;
                    float prof = ChaosProfile(x, z);
                    if (prof <= 0.02f) continue;
                    // Комочки кучками по 1–3: так поверхность «рыхлая», а не в сыпь.
                    int n = 1 + rnd.Next(0, 3);
                    for (int k = 0; k < n; k++)
                    {
                        float r = lr * Mathf.Lerp(0.6f, 1.2f, (float)rnd.NextDouble());
                        float ox = ((float)rnd.NextDouble() - 0.5f) * lr * 1.4f;
                        float oz = ((float)rnd.NextDouble() - 0.5f) * lr * 1.4f;
                        float surf = h * Mathf.Max(0f, ChaosProfile(x + ox, z + oz));
                        Vector3 c = new Vector3(x + ox, surf + r * (_microLumpRaise - 0.5f), z + oz);
                        Splat(c, r, 1f / (r * r), reach, min, cell, nx, ny, nz);
                    }
                }
            }

            // ── макушка: комок, торчащий сверху ──
            if (peakEff > 0.01f)
            {
                float px = ((float)rnd.NextDouble() - 0.5f) * rx * 0.3f;
                float pz = ((float)rnd.NextDouble() - 0.5f) * rz * 0.3f;
                float top = h * Mathf.Max(0f, ChaosProfile(px, pz));
                int n = 1 + Mathf.RoundToInt(_peak * 2f);
                for (int k = 0; k < n; k++)
                {
                    float r = blobR * Mathf.Lerp(1.15f, 0.6f, k / (float)Mathf.Max(1, n - 1));
                    Vector3 c = new Vector3(px + k * blobR * 0.12f, top + k * blobR * 0.55f * _peak, pz);
                    Splat(c, r, 1f / (r * r), reach, min, cell, nx, ny, nz);
                }
            }

            // ── капли и мелкие кучки вокруг ──
            foreach (Lobe sat in _sats)
            {
                int n = 1 + rnd.Next(0, 3);
                for (int k = 0; k < n; k++)
                {
                    float r = sat.R * Mathf.Lerp(0.6f, 1f, (float)rnd.NextDouble());
                    Vector3 c = new Vector3(sat.X + ((float)rnd.NextDouble() - 0.5f) * sat.R,
                                            r * sat.H * 0.5f,
                                            sat.Z + ((float)rnd.NextDouble() - 0.5f) * sat.R);
                    Splat(c, r, 1f / (r * r), reach, min, cell, nx, ny, nz);
                }
            }

            // Пол: ниже нуля — «воздух». Поверхность закроется снизу ровно.
            for (int j = 0; j < ny; j++)
            {
                float y = min.y + j * cell;
                if (y >= 0f) break;
                for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                    _field[Idx(i, j, k, nx, ny)] = 0f;
            }

            // ── градиенты (для нормалей) ──
            for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                float gx = F(i + 1, j, k, nx, ny, nz) - F(i - 1, j, k, nx, ny, nz);
                float gy = F(i, j + 1, k, nx, ny, nz) - F(i, j - 1, k, nx, ny, nz);
                float gz = F(i, j, k + 1, nx, ny, nz) - F(i, j, k - 1, nx, ny, nz);
                _grad[Idx(i, j, k, nx, ny)] = new Vector3(gx, gy, gz);
            }

            // ── вершины: по одной в каждой ячейке, через которую идёт поверхность ──
            int cells = (nx - 1) * (ny - 1) * (nz - 1);
            if (_cellVert == null || _cellVert.Length < cells) _cellVert = new int[cells];
            float iso = _iso;
            Color32 baseCol = Shade(color, 1f);

            for (int k = 0; k < nz - 1; k++)
            for (int j = 0; j < ny - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int ci = CIdx(i, j, k, nx, ny);
                _cellVert[ci] = -1;

                int mask = 0;
                for (int c = 0; c < 8; c++)
                    if (_field[Idx(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1), nx, ny)] > iso) mask |= 1 << c;
                if (mask == 0 || mask == 255) continue;

                Vector3 sum = Vector3.zero, gsum = Vector3.zero;
                int cnt = 0;
                for (int e = 0; e < 12; e++)
                {
                    int a = EdgeA[e], b = EdgeB[e];
                    bool ina = (mask & (1 << a)) != 0, inb = (mask & (1 << b)) != 0;
                    if (ina == inb) continue;
                    int ia = Idx(i + (a & 1), j + ((a >> 1) & 1), k + ((a >> 2) & 1), nx, ny);
                    int ib = Idx(i + (b & 1), j + ((b >> 1) & 1), k + ((b >> 2) & 1), nx, ny);
                    float fa = _field[ia], fb = _field[ib];
                    float t = Mathf.Clamp01((iso - fa) / (fb - fa));
                    Vector3 pa = new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1);
                    Vector3 pb = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);
                    sum += Vector3.Lerp(pa, pb, t);
                    gsum += Vector3.Lerp(_grad[ia], _grad[ib], t);
                    cnt++;
                }

                Vector3 local = sum / cnt;
                Vector3 pos = min + new Vector3((i + local.x) * cell, (j + local.y) * cell, (k + local.z) * cell);
                Vector3 nrm = gsum.sqrMagnitude > 1e-10f ? -gsum.normalized : Vector3.up;

                _cellVert[ci] = _v.Count;
                _v.Add(pos);
                _n.Add(nrm);
                // Низ кучки чуть темнее, верх светлее; альфа 1 = на массе есть узор текстуры.
                float lift = Mathf.Clamp01(pos.y / Mathf.Max(0.001f, h));
                Color32 col = Shade(color, 1f - _depthShade * 0.5f * (1f - lift));
                col.a = 255;
                _c.Add(col);
            }

            // ── грани: четырёхугольник на каждое ребро сетки со сменой знака ──
            for (int k = 1; k < nz - 1; k++)
            for (int j = 1; j < ny - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                bool a = _field[Idx(i, j, k, nx, ny)] > iso;
                bool b = _field[Idx(i + 1, j, k, nx, ny)] > iso;
                if (a == b) continue;
                Quad(CIdx(i, j - 1, k - 1, nx, ny), CIdx(i, j, k - 1, nx, ny),
                     CIdx(i, j, k, nx, ny), CIdx(i, j - 1, k, nx, ny), a);
            }
            for (int k = 1; k < nz - 1; k++)
            for (int j = 0; j < ny - 1; j++)
            for (int i = 1; i < nx - 1; i++)
            {
                bool a = _field[Idx(i, j, k, nx, ny)] > iso;
                bool b = _field[Idx(i, j + 1, k, nx, ny)] > iso;
                if (a == b) continue;
                Quad(CIdx(i - 1, j, k - 1, nx, ny), CIdx(i - 1, j, k, nx, ny),
                     CIdx(i, j, k, nx, ny), CIdx(i, j, k - 1, nx, ny), a);
            }
            for (int k = 0; k < nz - 1; k++)
            for (int j = 1; j < ny - 1; j++)
            for (int i = 1; i < nx - 1; i++)
            {
                bool a = _field[Idx(i, j, k, nx, ny)] > iso;
                bool b = _field[Idx(i, j, k + 1, nx, ny)] > iso;
                if (a == b) continue;
                Quad(CIdx(i - 1, j - 1, k, nx, ny), CIdx(i, j - 1, k, nx, ny),
                     CIdx(i, j, k, nx, ny), CIdx(i - 1, j, k, nx, ny), a);
            }

            // ── кусочки, торчащие из массы ──
            if (_pieces == Pieces.None || _piecesPerPortion <= 0f) return;
            int pieces = Mathf.Min(600, Mathf.RoundToInt(portions * _piecesPerPortion));
            for (int p = 0; p < pieces; p++)
            {
                // Случайная точка на поверхности горки (по профилю).
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float rr = Mathf.Sqrt((float)rnd.NextDouble()) * 0.95f;
                float px = Mathf.Cos(ang) * rr * rx, pz = Mathf.Sin(ang) * rr * rz;
                float profile = ChaosProfile(px, pz);
                if (profile <= 0.02f) continue;
                float surfY = Mathf.Max(0f, h * profile);

                Vector3 size;
                if (_pieces == Pieces.Shreds)
                {
                    float len = voxel * Mathf.Lerp(_shredLength.x, _shredLength.y, (float)rnd.NextDouble());
                    float th = voxel * Mathf.Lerp(_shredThickness.x, _shredThickness.y, (float)rnd.NextDouble());
                    size = new Vector3(th, th * 0.8f, len);
                }
                else
                {
                    float cs = voxel * (0.7f + 0.6f * (float)rnd.NextDouble());
                    size = new Vector3(cs, cs * 0.85f, cs);
                }

                // Утоплен в массу на Piece Sink своей толщины.
                float sink = size.y * (_pieceSink - 0.5f);
                Vector3 center = new Vector3(px, surfY - sink, pz);

                // Наклон по склону горки + разброс: торчат, как из настоящей кучки.
                Vector3 slope = new Vector3(px / (rx * rx), 0f, pz / (rz * rz)) * 2f * h;
                Vector3 surfN = new Vector3(slope.x, 1f, slope.z).normalized;
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, surfN) *
                                 Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 2f * _shredTilt,
                                                  (float)rnd.NextDouble() * 360f,
                                                  ((float)rnd.NextDouble() - 0.5f) * 2f * _shredTilt);

                float j = ((float)rnd.NextDouble() - 0.5f) * 2f * _colorJitter;
                Color32 col = Shade(color, _pieceTone + j);
                col.a = 0;          // альфа 0 — кусочек гладкий, без узора текстуры
                AddBox(center, size, rot, col);
            }
        }

        private void Splat(Vector3 c, float r, float invR2, int reach, Vector3 min, float cell, int nx, int ny, int nz)
        {
            int ci = Mathf.RoundToInt((c.x - min.x) / cell);
            int cj = Mathf.RoundToInt((c.y - min.y) / cell);
            int ck = Mathf.RoundToInt((c.z - min.z) / cell);
            for (int k = Mathf.Max(0, ck - reach); k <= Mathf.Min(nz - 1, ck + reach); k++)
            for (int j = Mathf.Max(0, cj - reach); j <= Mathf.Min(ny - 1, cj + reach); j++)
            for (int i = Mathf.Max(0, ci - reach); i <= Mathf.Min(nx - 1, ci + reach); i++)
            {
                float dx = min.x + i * cell - c.x, dy = min.y + j * cell - c.y, dz = min.z + k * cell - c.z;
                float q = 1f - (dx * dx + dy * dy + dz * dz) * invR2;
                if (q <= 0f) continue;
                _field[Idx(i, j, k, nx, ny)] += q * q * q;
            }
        }

        /// <summary>Четырёхугольник из вершин четырёх ячеек. inside — где масса:
        /// у начала ребра (нормаль вдоль оси) или у конца (против оси).</summary>
        private void Quad(int c0, int c1, int c2, int c3, bool insideAtStart)
        {
            int v0 = _cellVert[c0], v1 = _cellVert[c1], v2 = _cellVert[c2], v3 = _cellVert[c3];
            if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) return;
            if (insideAtStart)
            {
                _t.Add(v0); _t.Add(v1); _t.Add(v2);
                _t.Add(v0); _t.Add(v2); _t.Add(v3);
            }
            else
            {
                _t.Add(v0); _t.Add(v2); _t.Add(v1);
                _t.Add(v0); _t.Add(v3); _t.Add(v2);
            }
        }

        private static int Idx(int i, int j, int k, int nx, int ny) => i + nx * (j + ny * k);
        private static int CIdx(int i, int j, int k, int nx, int ny) => i + (nx - 1) * (j + (ny - 1) * k);

        private float F(int i, int j, int k, int nx, int ny, int nz)
        {
            i = Mathf.Clamp(i, 0, nx - 1); j = Mathf.Clamp(j, 0, ny - 1); k = Mathf.Clamp(k, 0, nz - 1);
            return _field[Idx(i, j, k, nx, ny)];
        }

        // Рёбра куба-ячейки: пары углов (угол = бит x | бит y<<1 | бит z<<2).
        private static readonly int[] EdgeA = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
        private static readonly int[] EdgeB = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };

        /// <summary>
        /// Гладкая масса: одна бугристая поверхность-горка. Узор «тёртого»
        /// даёт текстура шейдера, поэтому полигонов мало.
        /// </summary>
        private void BuildSmooth(float rx, float rz, float h, float voxel, Color color, int seed)
        {
            int res = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(rx, rz) * 2f / (voxel * 0.7f)), 10, 64);
            float ox = (seed % 1000) * 0.137f, oz = (seed / 1000 % 1000) * 0.291f;
            float lump = 1f / Mathf.Max(0.001f, voxel * _lumpSize);
            System.Random rnd = new System.Random(seed);

            for (int iz = 0; iz <= res; iz++)
            for (int ix = 0; ix <= res; ix++)
            {
                float u = ix / (float)res * 2f - 1f;
                float w = iz / (float)res * 2f - 1f;
                float r2 = u * u + w * w;

                // Круг → вершины за краем прижимаем к кромке, чтобы край был круглым.
                float r = Mathf.Sqrt(r2);
                if (r > 1f) { u /= r; w /= r; r2 = 1f; }

                float x = u * rx, z = w * rz;
                float profile = _shape == Shape.Mound ? (1f - r2) : (1f - 0.5f * r2);
                float noise = Mathf.PerlinNoise(ox + x * lump, oz + z * lump) - 0.5f;
                float y = r2 >= 0.999f ? 0f : Mathf.Max(0f, h * profile * (1f + noise * 2f * _lumpiness));

                _v.Add(new Vector3(x, y, z));
                float j = ((float)rnd.NextDouble() - 0.5f) * 2f * _colorJitter * 0.5f;
                _c.Add(Shade(color, 1f + j - (1f - profile) * _depthShade * 0.5f));
            }

            int row = res + 1;
            for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                int i0 = iz * row + ix, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                // По часовой, если смотреть сверху (+Y).
                _t.Add(i0); _t.Add(i2); _t.Add(i1);
                _t.Add(i1); _t.Add(i2); _t.Add(i3);
            }
        }

        private void OnDestroy()
        {
            if (!_grainGnocchi) { DestroyMesh(_grainMeshA); DestroyMesh(_grainMeshB); DestroyMesh(_grainMeshC); }   // ньокки — общие меши
            _strandField?.Dispose();
            _strandField = null;
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
        }

        private void OnEnable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            // Буфер ленточек освобождается при выключении — вернуть, если горка уже есть.
            if (_strands && _mesh != null && _shownPortions > 0f) RebuildStrands(_shownPortions, _shownSeed);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += EditorPreview;
#endif
        }

        private void OnDisable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            // Буфер видеокарты освобождаем сразу — иначе при перекомпиляции Unity
            // ругается на «утёкший» ComputeBuffer.
            _strandField?.Dispose();
#if UNITY_EDITOR
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall -= EditorPreview;
            RemoveStaleChild();
#endif
        }

        /// <summary>
        /// Объёмная стружка рисуется перед каждой камерой — так она видна и в
        /// игре, и в окне сцены в редакторе (там Update идёт не каждый кадр).
        /// </summary>
        private void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            if (!_strands || _strandField == null || _strandField.Count == 0 || _mf == null || _shownPortions <= 0f) return;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            Material mat = _strandMaterial;
            if (mat == null) return;
            // Дальность: вдали меньше ленточек (оставшиеся шире — щелей нет).
            float d = Vector3.Distance(cam.transform.position, _mf.transform.position);
            float lod = Mathf.Lerp(1f, _strandLodMin, Mathf.InverseLerp(_strandLodNear, _strandLodFar, d));
            _strandField.Draw(mat, _mf.transform.localToWorldMatrix, _shownColor, gameObject.layer,
                              _strandShadows && _castShadows, cam, lod);
        }

#if UNITY_EDITOR
        // ── превью в редакторе ──
        // Строится отложенно: в OnValidate Unity не разрешает создавать объекты.

        private void OnValidate()
        {
            // Выбрали пресет — один раз выставить все поля под него.
            if (_preset != _appliedPreset)
            {
                if (_preset != HeapPreset.Custom) ApplyPreset(_preset);
                _appliedPreset = _preset;
            }
            // В игре: поменял поле — горка пересобирается сразу, а не при следующей порции.
            if (Application.isPlaying) { _rebuildPending = true; return; }
            UnityEditor.EditorApplication.delayCall -= EditorPreview;
            UnityEditor.EditorApplication.delayCall += EditorPreview;
        }

        private void EditorPreview()
        {
            if (this == null || Application.isPlaying) return;
            if (!_preview || !isActiveAndEnabled) { RemoveStaleChild(); return; }
            if (_mr != null) _mr.sharedMaterial = _material != null ? _material : AutoMaterial;
            _shownPortions = -1f;                 // заставить перестроить
            if (_shape == Shape.Fill)
            {
                Cookware cw = GetComponentInParent<Cookware>();
                _fillCapacity = cw != null && cw.Def != null ? cw.Capacity : 0;
            }
            Show(_previewPortions, _previewColor, _previewSeed);
        }
#endif

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            if (_shape == Shape.Fill)
                Gizmos.DrawWireCube(new Vector3(0f, _maxHeight * 0.5f, 0f),
                                    new Vector3(_footprint.x * 2f, _maxHeight, _footprint.y * 2f));
            else
                Gizmos.DrawWireSphere(Vector3.zero, Radius > 0f ? Radius : _voxel * 3f);
        }
    }

    /// <summary>Цвет продукта в состоянии — для кучек и потоков.</summary>
    public static class FoodColors
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static Color Of(ProductDef def, FoodState state)
        {
            if (def == null) return new Color(0.8f, 0.75f, 0.6f);
            if (def.TryGetLook(state, out StateLook look) && look.Material != null)
            {
                Color c = Color.white;
                if (look.Material.HasProperty(BaseColorId)) c = look.Material.GetColor(BaseColorId);
                else if (look.Material.HasProperty(ColorId)) c = look.Material.GetColor(ColorId);
                // Белый цвет материала обычно значит «цвет в текстуре» — тогда он
                // кучке не подсказка, берём цвет продукта.
                if (c.r < 0.95f || c.g < 0.95f || c.b < 0.95f) return c;
            }
            return FoodStateInfo.IsSpoiled(state) ? def.BurntColor : def.BaseColor;
        }
    }
}
