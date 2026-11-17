using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЖИДКОСТЬ В ТАРЕ — только визуал (у каждого игрока свой) + геометрия для выплёскивания.
    ///
    /// ═══ ЧТО ИЗМЕНИЛОСЬ (v3) ═══
    /// Раньше поверхность строилась «над точкой LiquidAnchor по миру». Стоило
    /// ведру наклониться — диск уезжал вбок и торчал сквозь стенку, а у другой
    /// стенки открывался зазор. Теперь:
    ///   • поверхность — плоскость (горизонт + плеск), РАЗРЕЗАННАЯ стенками тары:
    ///     край ищется лучами внутри тары, поэтому всегда ровно по стенке, по краю
    ///     и по дну — при любом наклоне, хоть при выливании на 80°;
    ///   • объём сохраняется: наклонил ведро — молоко перетекает к нижнему борту,
    ///     а не «поднимается вместе с дном»;
    ///   • плеск — физичный: частота из размера тары (как у настоящей воды),
    ///     скорость сглажена — не дёргается от шагов/сети;
    ///   • «подпрыгивание» центра (осесимметричная волна) от шагов;
    ///   • мениск у стенки — лёгкий подъём и заход под стенку: ни щелей, ни ступеньки;
    ///   • СЛИВКИ — отдельный густой слой в шейдере: пятна-островки растут и
    ///     сливаются в сплошную шапку, отбрасывают тень на молоко, матовые, в
    ///     складках; при взбивании закручиваются воронкой вокруг щупа (Stir);
    ///   • брызги: гребень волны ударил в борт — вспышка и корона капель.
    ///
    /// Для CookwareSpill: ComputeOverflow(), TryGetSurfaceY(), RimHeight.
    /// Для вибратора: Stir(точка, сила).
    /// </summary>
    public sealed class CookwareLiquidVisual : MonoBehaviour
    {
        public enum Role : byte
        {
            [InspectorName("Жидкость (молоко, вода)")] Liquid = 0,
            [InspectorName("Слой сверху (сливки)")] TopLayer = 1,
            [InspectorName("Плавающие комки (масло)")] Floating = 2,
            [InspectorName("Густая паста (масло)")] Paste = 3,
            [InspectorName("Сметана — густая, держит форму")] Thick = 4,
            [InspectorName("Мороженое — шарики, следы ложки")] IceCream = 5,
        }

        public enum Form : byte
        {
            [InspectorName("Круглая")] Round = 0,
            [InspectorName("Прямоугольная")] Box = 1,
        }

        [Serializable]
        public struct Entry
        {
            public ProductDef Product;
            public Role Role;
            [Tooltip("Свой цвет. Прозрачный — берётся Base Color продукта.")]
            public Color Color;
        }

        [SerializeField] private Cookware _cookware;
        [SerializeField] private Entry[] _products;

        [Header("Форма тары")]
        [SerializeField] private Form _form = Form.Round;
        [Min(0.01f)][SerializeField] private float _radiusBottom = 0.12f;
        [Min(0.01f)][SerializeField] private float _radiusTop = 0.15f;
        [Tooltip("Прямоугольная: полуразмеры дна по X и Z, м")]
        [SerializeField] private Vector2 _halfSize = new Vector2(0.4f, 0.25f);
        [Min(0.02f)][SerializeField] private float _maxHeight = 0.25f;
        [Tooltip("Прямоугольная тара сложной формы (ковш): ширина воды по высоте, снизу вверх — " +
                 "(полуX, полуZ, центрX, центрZ) от этой точки. Заполняет «Подогнать и собрать» у Cookware Auto Setup " +
                 "в режиме «По форме модели». Пусто — прямой ящик Half Size.")]
        [SerializeField] private Vector4[] _boxProfile = new Vector4[0];
        [Tooltip("v20.1: сужение по длине на каждой высоте (как Box Profile): 0 — прямоугольник, +0.2 — к +Z шире на 20%, к −Z уже. " +
                 "Тачка: корыто — трапеция, иначе вода не доходит до бортов и видно дно. Заполняет меню тачки")]
        [SerializeField] private float[] _boxTaper = new float[0];
        [Tooltip("v20.1: сужение идёт вдоль X (меняется размер по Z), а не вдоль Z")]
        [SerializeField] private bool _boxTaperAlongX = false;
        [Tooltip("v20.2: тара на технике (тачка): наибольший наклон поверхности К ТАРЕ, градусы, пока её не опрокидывают. " +
                 "0 — как у обычной тары (вода стоит по горизонту). Ставит тачка сама")]
        [Range(0f, 15f)][SerializeField] private float _vehicleSurfaceTilt = 0f;
        private float _vehicleMaxSlope;
        [Tooltip("Край для жидкости ниже края модели на столько, м. Полное ведро заполняется до этой высоты " +
                 "(порций влезает столько же), волны упираются сюда — у самой кромки модели диск не пляшет.")]
        [Range(0f, 0.15f)][SerializeField] private float _rimInset = 0.03f;
        [Tooltip("Уровень — по заполненности тары (порций / Capacity у Cookware): полная тара = край для жидкости. " +
                 "Выключи — уровень считается по Portion Volume (м³ на порцию), как раньше.")]
        [SerializeField] private bool _fillByCapacity = true;
        [Tooltip("Полная тара встаёт на эту долю высоты края для жидкости (чуть ниже — волнам есть куда плескаться)")]
        [Range(0.5f, 1f)][SerializeField] private float _fullLevel = 0.96f;
        [Tooltip("Объём одной порции жидкости, м³ (только если Fill By Capacity выключен)")]
        [Min(0.00001f)][SerializeField] private float _portionVolume = 0.001f;
        [SerializeField] private bool _ignoreParentScale = true;

        [Header("Стенки")]
        [Tooltip("Радиус внутренней стенки по высоте (снизу вверх). Заполняет кнопка ⋮ → «Подогнать под стенки модели». " +
                 "Пусто — прямой конус Radius Bottom → Radius Top.")]
        [SerializeField] private float[] _wallProfile = new float[0];
        [Tooltip("v4: граней у стенки (лоу-поли ведро: 12). 0 — круглая. Край жидкости идёт ровно по плоским граням, " +
                 "не выпирает сквозь них и не оставляет щелей в углах. Ставит «Подогнать под стенки модели»")]
        [Range(0, 48)][SerializeField] private int _wallSides = 0;
        [Tooltip("v4: угол середины грани, градусы (оси тары). Ставит «Подогнать под стенки модели»")]
        [SerializeField] private float _wallPhase = 0f;
        [Tooltip("На сколько край жидкости заходит под стенку, м. Прячет щель между молоком и стенкой.")]
        [Range(0f, 0.02f)][SerializeField] private float _edgeOverlap = 0.006f;
        [Tooltip("Подъём жидкости у стенки (мениск), м")]
        [Range(0f, 0.015f)][SerializeField] private float _meniscus = 0.004f;

        [Header("v7: молоко гуще и матовее")]
        [Tooltip("Тело молока: 0 — как вода, 1 — густое (тяжелее и мягче плещется, волны ниже). На воду не действует")]
        [Range(0f, 1f)][SerializeField] private float _milkBody = 0.6f;
        [Tooltip("Матовость: 0 — блестит как вода/металл, 1 — матовое молочное, маленький мягкий блик. На воду не действует")]
        [Range(0f, 1f)][SerializeField] private float _milkMatte = 0.75f;

        [Header("Плеск")]
        [Tooltip("Насколько сильно жидкость реагирует на рывки тары")]
        [Range(0f, 3f)][SerializeField] private float _sloshGain = 1f;
        [Tooltip("v23.2: лёгкие толчки тары (ускорение ниже этого, м/с²) не раскачивают жидкость — тара едет ровно, всё стоит на месте")]
        [Range(0f, 6f)][SerializeField] private float _motionDeadzone = 1.6f;
        [Tooltip("v23.2: минимальный видимый уровень (доля высоты тары) — немного жидкости всё равно покрывает всё дно, без «лесенки» и голых углов")]
        [Range(0f, 0.2f)][SerializeField] private float _minLevel = 0.035f;
        [Tooltip("v23.3: размер желтков в таре (1 — как желток разбитого на пол яйца)")]
        [Range(0.3f, 3f)][SerializeField] private float _yolkScale = 1f;
        [Tooltip("Частота качания — как у настоящей жидкости в такой таре (из радиуса и глубины). " +
                 "Выключи — работают Stiffness/Damping ниже, как раньше.")]
        [SerializeField] private bool _physicalFrequency = true;
        [Tooltip("Затухание: 0.03 — вода (долго качается), 0.08 — молоко, 0.25 — сливки/сметана")]
        [Range(0.01f, 0.6f)][SerializeField] private float _dampingRatio = 0.07f;
        [Tooltip("Старый режим: жёсткость")]
        [SerializeField] private float _sloshStiffness = 40f;
        [Tooltip("Старый режим: затухание")]
        [SerializeField] private float _sloshDamping = 3.5f;
        [Tooltip("Наибольший наклон поверхности, градусы")]
        [Range(0f, 45f)][SerializeField] private float _maxSloshAngle = 25f;
        [Tooltip("Сглаживание движения тары, с. Убирает дёрганье от шагов и сети.")]
        [Range(0f, 0.15f)][SerializeField] private float _motionSmoothing = 0.045f;
        [Tooltip("«Подпрыгивание» центра от шагов и толчков")]
        [Range(0f, 3f)][SerializeField] private float _bounceGain = 1f;
        [Tooltip("Плеск не поднимает молоко выше края и не открывает дно: волна упирается в борт, " +
                 "отскакивает и даёт брызги. Выключи — как раньше, до Max Slosh Angle.")]
        [SerializeField] private bool _limitToRim = true;
        [Tooltip("Сколько молока остаётся над дном на «низкой» стороне волны, м")]
        [Range(0f, 0.03f)][SerializeField] private float _bottomFilm = 0.006f;
        [Tooltip("Пока тару несут (наклон меньше этого, градусы), поверхность держится внутри: " +
                 "не выше края, не до дна, даже при раскачке. Сильнее наклонили (льют) — честная физика.")]
        [Range(10f, 70f)][SerializeField] private float _carryLockAngle = 30f;
        [Tooltip("Отскок волны от края: 0 — гаснет, 1 — упруго")]
        [Range(0f, 1f)][SerializeField] private float _rimBounce = 0.35f;

        [Header("Брызги при плеске")]
        [Tooltip("Гребень волны ударил в борт — вспышка и корона капель (только вид)")]
        [SerializeField] private bool _splashFx = true;
        [Tooltip("Брызги начинаются, когда волна подошла к краю ближе этого, м")]
        [Range(0f, 0.06f)][SerializeField] private float _sprayClearance = 0.02f;
        [Range(0f, 3f)][SerializeField] private float _splashPower = 1f;
        [SerializeField] private float _splashCooldown = 0.11f;

        [Header("Слой сливок")]
        [Tooltip("Сколько порций сливок дают сплошную шапку")]
        [Min(1)][SerializeField] private int _fullLayerAt = 6;
        [Tooltip("Размер островков сливок: больше — мельче пятна")]
        [Range(2f, 40f)][SerializeField] private float _creamScale = 11f;
        [Tooltip("Объём порции сливок относительно порции молока. 1 — правило 1 молоко → 1 сливки, " +
                 "2 — правило 2 → 1 (уровень не падает).")]
        [Range(0.5f, 4f)][SerializeField] private float _creamVolume = 1f;
        [Tooltip("Когда сливок больше этой доли, молоко под ними тоже желтеет — всё ведро «сливочное»")]
        [Range(0f, 1f)][SerializeField] private float _creamBodyFrom = 0.35f;
        [Tooltip("Как быстро появляются новые сливки, 1/с")]
        [SerializeField] private float _creamGrowSpeed = 1.2f;

        [Header("v17: порошок (тёртое какао, мука, сахар) — Role Paste/Floating")]
        [Tooltip("Продукт в роли Paste/Floating, в названии которого есть одно из этих слов, рисуется ПОРОШКОМ: горка, зерно, крошки, матовый (шейдер CatCooks/Powder Heap)")]
        [SerializeField] private string _powderWords = "тёрт,терт,порош,мука,муки,сахар,крошк,молот,пудр";
        [Tooltip("Высота горки порошка (множитель к Paste Relief)")]
        [Range(0.2f, 3f)][SerializeField] private float _powderRelief = 1.3f;

        [Header("Паста (масло) — Role Paste")]
        [Tooltip("Высота бугров и вмятин густой пасты, м")]
        [Range(0f, 0.06f)][SerializeField] private float _pasteRelief = 0.022f;
        [Tooltip("Размер комков пасты, м")]
        [Range(0.015f, 0.2f)][SerializeField] private float _pasteLumpSize = 0.055f;
        [Tooltip("Как быстро жидкость густеет в пасту на вид, 1/с (0.3 — за ~3 с)")]
        [SerializeField] private float _pasteGrowSpeed = 0.3f;
        [Tooltip("Взбивание пасты: насколько она пучится у щупа, м")]
        [Range(0f, 0.04f)][SerializeField] private float _pasteHeave = 0.014f;
        [Tooltip("Ямка от мастерка/ложки: радиус и глубина, м")]
        [SerializeField] private Vector2 _dent = new Vector2(0.045f, 0.022f);

        [Tooltip("Масло плотнее сливок: из полного ведра сливок выходит столько ведра масла (0.28 — примерно четверть)")]
        [Range(0.1f, 1f)][SerializeField] private float _pasteVolume = 0.28f;
        [Tooltip("Насколько «пюрешная» форма масла: пики, складки, следы венчика (множитель к Paste Relief)")]
        [Range(0.5f, 3f)][SerializeField] private float _mashAmount = 1.5f;

        [Header("Сметана — Role Thick")]
        [Tooltip("Высота мягких бугров и пиков сметаны, м")]
        [Range(0f, 0.08f)][SerializeField] private float _thickRelief = 0.03f;
        [Tooltip("Размер «наплывов» сметаны, м")]
        [Range(0.02f, 0.3f)][SerializeField] private float _thickBlob = 0.09f;
        [Tooltip("Острые пики с загнутым кончиком, как у ложки сметаны (0 — гладко)")]
        [Range(0f, 2f)][SerializeField] private float _thickPeaks = 1f;
        [Tooltip("Как быстро застывает на вид, 1/с")]
        [SerializeField] private float _thickSetSpeed = 0.25f;
        [Tooltip("Объём порции сметаны относительно порции молока")]
        [Range(0.3f, 1.5f)][SerializeField] private float _thickVolume = 0.95f;
        [Tooltip("Сметана-желе: насколько сильно качается от шагов и поворотов")]
        [Range(0f, 3f)][SerializeField] private float _jellyWobble = 1f;
        [Tooltip("Сметана-желе: частота дрожи, Гц (выше — мельче и чаще)")]
        [Range(1f, 8f)][SerializeField] private float _jellyFrequency = 3.2f;
        [Tooltip("Сметана-желе: затухание (меньше — дольше трясётся)")]
        [Range(0.03f, 0.6f)][SerializeField] private float _jellyDamping = 0.12f;
        [Tooltip("Испорченная сметана (перебор закваски): цвет сыворотки в ямках")]
        [SerializeField] private Color _wheyColor = new Color(0.82f, 0.86f, 0.62f);
        [Tooltip("Брожение с закваской: пузырей в секунду (буклит)")]
        [Range(0f, 20f)][SerializeField] private float _fermentBubbles = 4f;
        [Header("Мороженое — Role IceCream")]
        [Tooltip("Высота шариков мороженого, м")]
        [Range(0f, 0.12f)][SerializeField] private float _iceRelief = 0.05f;
        [Tooltip("Следы ложки: глубина бороздок (доля высоты)")]
        [Range(0f, 1f)][SerializeField] private float _iceScoopMarks = 0.55f;
        [Tooltip("Объём порции мороженого относительно порции молока (взбитое — пышнее)")]
        [Range(0.5f, 2f)][SerializeField] private float _iceVolume = 1.15f;

        [Tooltip("Рыхлость мороженого: комочки и крошка (0 — гладкое)")]
        [Range(0f, 2f)][SerializeField] private float _iceLoose = 0.6f;
        [Tooltip("Волны-дюны с пиками, как в магазинной банке (0 — только шарики от ложки)")]
        [Range(0f, 1.5f)][SerializeField] private float _iceDunes = 1f;
        [Tooltip("Шарики от ложки поверх дюн (0 — только дюны)")]
        [Range(0f, 1f)][SerializeField] private float _iceScoops = 0.35f;
        [Tooltip("Морозный пар «дышит» от мороженого и льда")]
        [SerializeField] private bool _coldVapor = true;
        [Tooltip("Клубов пара в секунду")]
        [Range(0f, 10f)][SerializeField] private float _coldVaporRate = 2.5f;

        [Header("Заморозка азотом (случайная смесь — тает обратно)")]
        [Tooltip("Цвет инея")]
        [SerializeField] private Color _frostColor = new Color(0.88f, 0.94f, 1f);
        [Tooltip("Как быстро покрывается льдом на вид, 1/с")]
        [SerializeField] private float _freezeSpeed = 1.5f;
        [Tooltip("Как быстро оттаивает на вид, 1/с")]
        [SerializeField] private float _thawSpeed = 0.12f;

        [Tooltip("Брожение: насколько «дышит» поверхность, м")]
        [Range(0f, 0.01f)][SerializeField] private float _fermentHeave = 0.0025f;

        [Header("Зёрна масла при взбивании (Role Floating или Paste)")]
        [Tooltip("Сколько зёрен масла плавает, пока в таре ещё есть сливки. Они сбиваются к стенкам и срастаются в пасту.")]
        [Range(0, 80)][SerializeField] private int _grainCount = 44;
        [Range(1, 30)][SerializeField] private int _maxLumps = 14;   // устарело, оставлено для совместимости
        [SerializeField] private Vector2 _lumpSize = new Vector2(0.025f, 0.045f);
        [SerializeField] private float _riseTime = 1.6f;

        [Header("Пена и пузыри")]
        [Tooltip("v19.3: пузыри (как в воде) при взбивании миксером/вибратором. Выключено — вместо них лёгкие хлопья пены")]
        [SerializeField] private bool _stirBubbles = false;
        [Tooltip("v19.3: лёгких хлопьев пены в секунду из точки взбивания (вместо пузырей)")]
        [Range(0f, 120f)][SerializeField] private float _whipFlecks = 40f;
        [SerializeField] private float _bubblesPerSecond = 60f;
        [Range(0f, 0.05f)][SerializeField] private float _surfaceWobble = 0.008f;
        [Range(0f, 1f)][SerializeField] private float _foam = 0.6f;
        [Tooltip("Пенка на молоке всегда: мелкие пузырьки у стенок и пятнами")]
        [Range(0f, 1f)][SerializeField] private float _milkSkin = 0.45f;
        [Tooltip("Взбиваешь — пена копится до этой доли, после остановки медленно оседает")]
        [Range(0f, 1f)][SerializeField] private float _whipFoamMax = 0.7f;
        [Tooltip("Крупных пузырей в секунду при взбивании")]
        [SerializeField] private float _whipBubbles = 110f;
        [Tooltip("Размер крупных пузырей, м")]
        [SerializeField] private float _whipBubbleSize = 0.022f;

        [Header("Взбивание: эпицентр (v4)")]
        [Tooltip("Там, где водят щупом, — объёмное бурление: горб из кипящих пузырей, м")]
        [Range(0f, 0.04f)][SerializeField] private float _whipBoil = 0.016f;
        [Tooltip("Радиус бурления вокруг щупа, м")]
        [Range(0.02f, 0.2f)][SerializeField] private float _whipBoilRadius = 0.075f;
        [Tooltip("v5: капель в секунду из точки взбивания — немного, зато крупные")]
        [Range(0f, 60f)][SerializeField] private float _whipDrops = 12f;
        [Tooltip("v5: размер капель взбивания, м (от и до)")]
        [SerializeField] private Vector2 _whipDropSize = new Vector2(0.018f, 0.036f);
        [Tooltip("v5: «пыль» взбивания — сколько тучек в секунду пыхает из точки щупа")]
        [Range(0f, 400f)][SerializeField] private float _whipDust = 120f;
        [Tooltip("v5: размер тучек, м (от и до)")]
        [SerializeField] private Vector2 _whipDustSize = new Vector2(0.02f, 0.055f);
        [Tooltip("v5: как резко тучки разлетаются в стороны, м/с (потом тормозят и тают)")]
        [Range(0f, 3f)][SerializeField] private float _whipDustBurst = 0.9f;
        [Tooltip("v6: «взрывчиков» в секунду в точке щупа — клубок, взрывчик с искрами, кольцо, столбик, россыпь (вперемешку)")]
        [Range(0f, 15f)][SerializeField] private float _whipBursts = 5f;
        [Tooltip("v6: размер взрывчиков (1 — под ведро)")]
        [Range(0.3f, 3f)][SerializeField] private float _whipBurstSize = 1f;
        [Tooltip("v18.8: скорость анимации тучек взбивания и появления новых (1 — как было, 2 — вдвое быстрее)")]
        [Range(0.5f, 4f)][SerializeField] private float _whipFxSpeed = 2f;
        [Tooltip("v6: какие взрывчики разрешены: клубок, взрывчик, кольцо, столбик, россыпь")]
        [SerializeField] private bool[] _whipBurstKinds = { true, true, true, true, true };
        [Tooltip("v7: пуфы взбивания крупнее во столько раз")]
        [Range(0.5f, 6f)][SerializeField] private float _whipPoofScale = 3f;
        [Tooltip("v7: пуфы облепляют щуп и едут за ним (0 — разлетаются по ведру, 1 — шапка у щупа)")]
        [Range(0f, 1f)][SerializeField] private float _whipPoofFollow = 0.75f;
        [Tooltip("Крупных пузырей в секунду прямо у щупа (кипящий горб)")]
        [Range(0f, 120f)][SerializeField] private float _whipEpicenterBubbles = 45f;

        [Header("Волны (v4)")]
        [Tooltip("Бегущие волны: от шагов, ударов в борт, струи и щупа расходятся круги и отражаются")]
        [Range(0f, 3f)][SerializeField] private float _waves = 1f;
        [Tooltip("Скорость волн, м/с")]
        [Range(0.1f, 1.2f)][SerializeField] private float _waveSpeed = 0.34f;
        [Tooltip("Длина волны, м")]
        [Range(0.03f, 0.2f)][SerializeField] private float _waveLength = 0.08f;
        [Tooltip("Рябь «сеткой», пока тару несут: две косые волны по ходу движения")]
        [Range(0f, 3f)][SerializeField] private float _moveWaves = 1f;

        [Header("Молоко: жир и следы (v4)")]
        [Tooltip("Жирные «глазки» на молоке: круглые блестящие капли с обводкой, сбиваются к стенкам и в ямки")]
        [Range(0f, 1f)][SerializeField] private float _fatSpots = 0.6f;
        [Tooltip("Размер глазков: больше — мельче")]
        [Range(10f, 120f)][SerializeField] private float _fatScale = 42f;
        [Tooltip("След от щупа: светлая полоса пены, тает за пару секунд")]
        [Range(0f, 1f)][SerializeField] private float _stirTrails = 0.7f;

        [Header("Вид молока")]
        [Tooltip("Плотность: 1 — молоко непрозрачное: пишет глубину, рисуется как твёрдая поверхность " +
                 "(обводки и швы дна сквозь него не видны). Меньше 1 — полупрозрачное.")]
        [Range(0f, 1f)][SerializeField] private float _density = 1f;
        [Header("Вода — мультяшная")]
        [Tooltip("Когда в таре в основном вода: свой цвет (если в Products у воды цвет не задан), прозрачнее, без пенки молока")]
        [SerializeField] private Color _waterColor = new Color(0.18f, 0.6f, 0.92f);
        [Range(0.2f, 1f)][SerializeField] private float _waterOpacity = 0.78f;
        private bool _waterLike;
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        [Header("Суп: жидкость с кусочками")]
        [Tooltip("В жидкости лежат кусочки (перец, чеснок, овощи): жидкость поднимается над ними, " +
                 "окрашивается их цветом, мутнеет, по поверхности плавают кусочки. Работает само, без настройки.")]
        [SerializeField] private bool _soup = true;
        [Tooltip("Насколько сильно кусочки красят жидкость (0 — не красят, 1 — целиком их цвета)")]
        [Range(0f, 1f)][SerializeField] private float _soupTint = 0.45f;
        [Tooltip("Непрозрачность «супа» (вода с кусочками мутнеет)")]
        [Range(0.3f, 1f)][SerializeField] private float _soupOpacity = 0.9f;
        [Tooltip("Сколько кусочков плавает на поверхности (при полной тары кусочков)")]
        [Range(0, 60)][SerializeField] private int _soupBits = 28;
        [Tooltip("Размер плавающих кусочков, м (от–до)")]
        [SerializeField] private Vector2 _soupBitSize = new Vector2(0.014f, 0.032f);

        [Header("Отладка")]
        [Tooltip("Писать в консоль, почему жидкость не видна (продукта нет в Products, не нашлась поверхность…)")]
        [SerializeField] private bool _debugLog = true;
        private string _diagLast;
        private float _diagAt;

        [Header("Материалы")]
        [Tooltip("Поверхность. Пусто — CatCooks/Liquid Surface.")]
        [SerializeField] private Material _material;
        [Tooltip("Комки масла. Пусто — CatCooks/Food Heap без текстуры.")]
        [SerializeField] private Material _lumpMaterial;

        // ── сетка ──
        private const int Dirs = 48, Rings = 18;      // + кольцо мениска (v4: гуще — для бегущих волн)
        private const int GridN = 14;                  // колонки для расчёта объёма при наклоне
        private const int TableN = 64;                 // таблица объёма «стоя»

        private Mesh _mesh;
        private MeshFilter _mf;
        private MeshRenderer _mr;
        private MaterialPropertyBlock _mpb, _lumpMpb;
        private Vector3[] _vtx;
        private Color32[] _col;
        private Vector2[] _uv0, _uv1, _uv2;
        private readonly float[] _tEdge = new float[Dirs];
        private readonly float[] _dirAng = new float[Dirs];

        /// <summary>
        /// v23.2: углы лучей сетки. У круглой тары — равномерно. У прямоугольной — «по эллипсу»:
        /// θ равномерно, угол = atan2(hz·sinθ, hx·cosθ). При θ = 45°, 135°… луч идёт ТОЧНО в угол
        /// (48 лучей делятся на 8) — углы формы больше не срезаются.
        /// </summary>
        private void UpdateDirAngles()
        {
            float hx = 1f, hz = 1f;
            if (_form == Form.Box)
            {
                Vector4 b = BoxAt(Mathf.Clamp(_lc, 0f, RimH));
                hx = Mathf.Max(1e-4f, b.x); hz = Mathf.Max(1e-4f, b.y);
            }
            for (int s = 0; s < Dirs; s++)
            {
                float th = s / (float)Dirs * Mathf.PI * 2f;
                _dirAng[s] = _form == Form.Box ? Mathf.Atan2(hz * Mathf.Sin(th), hx * Mathf.Cos(th)) : th;
            }
        }

        /// <summary>Случайная точка по всей поверхности (не только в круге посередине), оси тары.</summary>
        private Vector3 RandomSurfacePoint(float inset)
        {
            int s = UnityEngine.Random.Range(0, Dirs);
            float a = Mathf.LerpAngle(_dirAng[s] * Mathf.Rad2Deg, _dirAng[(s + 1) % Dirs] * Mathf.Rad2Deg, UnityEngine.Random.value) * Mathf.Deg2Rad;
            float t = Mathf.Min(_tEdge[s], _tEdge[(s + 1) % Dirs]) * inset * Mathf.Sqrt(UnityEngine.Random.value);
            return _center + (_e1 * Mathf.Cos(a) + _e2 * Mathf.Sin(a)) * t;
        }

        private float EdgeLimitAt(Vector3 local, float inset)
        {
            Vector3 d = local - _center;
            float a = Mathf.Atan2(Vector3.Dot(d, _e2), Vector3.Dot(d, _e1));
            float best = float.MaxValue; int bs = 0;
            for (int s = 0; s < Dirs; s++)
            {
                float da = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, _dirAng[s] * Mathf.Rad2Deg));
                if (da < best) { best = da; bs = s; }
            }
            return _tEdge[bs] * inset;
        }
        private readonly bool[] _wallEdge = new bool[Dirs];

        // ── содержимое ──
        private int _liquid, _layer, _floating, _paste, _thick, _ice, _frozenN;
        private Color _iceColor = new Color(0.98f, 0.95f, 0.84f);
        private float _iceShown, _frozenShown;
        private Vector2[] _uv3;
        private Color _thickColor = new Color(0.98f, 0.97f, 0.94f);
        private float _thickShown, _fermentBubbleAcc;
        private bool _fermenting;
        /// <summary>Насколько содержимое «держит форму» на вид: паста или сметана.</summary>
        private float Stiff => Mathf.Max(Mathf.Max(Mathf.Max(_pasteShown, _thickShown), Mathf.Max(_iceShown, _frozenShown)), _viscK);
        /// <summary>Совсем твёрдое (масло, мороженое, лёд) — не дрожит. Сметана — желе: дрожит, но не течёт.</summary>
        private float Rigid => Mathf.Max(_pasteShown, Mathf.Max(_iceShown, _frozenShown));
        /// <summary>Доля «желе» (сметана поверх жидкого).</summary>
        private float Jelly => _thickShown * (1f - Rigid);
        private int _spoiledN;
        // желе: сдвиг верха относительно дна (оси тары, м) и «присед» массы
        private Vector2 _jellyShear, _jellyShearVel;
        private float _jellySquash, _jellySquashVel;

        /// <summary>
        /// Желе (сметана): пружина в осях тары. Толчки тары сдвигают верх массы вбок,
        /// шаги — приседает и подпрыгивает. Затухает за несколько качаний. Сама
        /// поверхность при этом НЕ течёт и не наклоняется, как жидкость.
        /// </summary>
        private void UpdateJelly(float dt, Vector3 jerk)
        {
            float jel = Jelly;
            if (jel < 0.001f && _jellyShear.sqrMagnitude < 1e-8f && Mathf.Abs(_jellySquash) < 1e-6f) return;
            Vector3 aL = Quaternion.Inverse(transform.rotation) * _accF;
            float R = Mathf.Max(0.03f, RadiusAtRim);
            Vector2 drive = new Vector2(-aL.x, -aL.z) * (0.0035f * _jellyWobble * jel);
            float sDrive = -aL.y * 0.0006f * _jellyWobble * jel;
            // шаг/рывок — короткий пинок, чтобы желе «вздрогнуло»
            Vector3 jL = Quaternion.Inverse(transform.rotation) * jerk;
            _jellySquashVel += Mathf.Clamp(-jL.y * dt * 0.0004f, -0.02f, 0.02f) * _jellyWobble * jel;
            _jellyShearVel += new Vector2(-jL.x, -jL.z) * (dt * 0.0005f * _jellyWobble * jel);
            float w = _jellyFrequency * Mathf.PI * 2f, z = _jellyDamping;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / (1f / 240f)), 1, 8);
            float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                _jellyShearVel += ((drive - _jellyShear) * (w * w) - _jellyShearVel * (2f * z * w)) * h;
                _jellyShear += _jellyShearVel * h;
                float w2 = w * 1.4f;
                _jellySquashVel += ((sDrive - _jellySquash) * (w2 * w2) - _jellySquashVel * (2f * z * w2)) * h;
                _jellySquash += _jellySquashVel * h;
            }
            _jellyShear = Vector2.ClampMagnitude(_jellyShear, R * 0.12f);
            _jellySquash = Mathf.Clamp(_jellySquash, -0.06f, 0.06f);
        }
        private float _spoiledShown;
        private int Butter => _paste + _floating;       // масло любой ролью: Paste или Floating
        private Color _pasteColor = new Color(1f, 0.86f, 0.42f);
        private float _pasteShown, _pasteSwirl;
        private Vector2 _pasteSwirlC;
        private readonly Vector4[] _dents = new Vector4[10];   // x, z (оси тары), радиус, глубина
        private int _dentNext;
        // ── суп ──
        private int _solidN;
        private Color _soupColor = Color.white;
        private float _soupShown;
        private readonly List<Color> _soupCols = new List<Color>();
        private readonly List<Lump> _bits = new List<Lump>();
        /// <summary>Цвет жидкости с учётом кусочков в ней.</summary>
        private Color LiquidBase => _soupShown > 0.001f ? Color.Lerp(_liquidColor, _soupColor, _soupShown) : _liquidColor;
        private Color _liquidColor = Color.white, _layerColor = new Color(1f, 0.96f, 0.82f), _floatColor = Color.yellow;
        private float _targetVolume;
        private float _creamShown;

        // ── геометрия ──
        private bool _geomDirty = true;
        private readonly float[] _volTable = new float[TableN + 1];
        private Vector3[] _cells;                      // x, ymin, z (ymin < 0 — вне тары)
        private float _cellArea, _rMax;
        private float _lc;                             // высота жидкости на оси тары (м, по оси вверх)
        private float _lcUpright;
        private Vector3 _upLocal = Vector3.up;         // мировой «верх» в осях тары
        private Vector3 _nDynW = Vector3.up;           // нормаль поверхности с плеском, мир
        private Vector3 _center;                       // центр поверхности, оси тары (м)
        private bool _surfaceOk;
        private float _tEdgeMin;
        private Vector3 _e1, _e2, _nL;

        // ── движение ──
        private Vector3 _lastPos, _velF, _velPrevF, _accF, _accPrevF;
        private bool _tracked;
        private Vector2 _slope, _slopeVel;             // наклон плеска (мир, dy/dx, dy/dz)
        private float _bounce, _bounceVel;             // осесимметричная волна (м, на краю)
        private float _rimHit;                         // удар волны в борт (м/с у края), для брызг
        private Vector2 _rimHitDir;
        private float _whipFoam, _bubbleAcc;
        private float _lcStatic, _prevExcess;
        private static readonly Dictionary<Material, Material> DenseMats = new Dictionary<Material, Material>();
        private Vector2 _creamDrift;

        // ── брызги ──
        private float _splashT, _lastOver = -1f, _overVel;

        // ── взбивание ──
        private Vector2 _stirLocal;
        private float _stirAmt, _swirlPhase, _stirUntil;

        // ── круги от струи ──
        private readonly Vector4[] _ringData = { new Vector4(0, 0, 0, -1), new Vector4(0, 0, 0, -1),
                                                 new Vector4(0, 0, 0, -1), new Vector4(0, 0, 0, -1) };
        private int _ringNext;
        private float _ringCooldown;

        private struct Lump
        {
            public Transform T; public MeshRenderer R; public float Born; public Vector2 Pos; public float Size; public float Phase;
            public float Ang, Rad0, Tint; public Vector3 Squash;
        }
        private static Mesh[] _grainMeshes;
        private readonly List<Lump> _lumps = new List<Lump>();
        private ParticleSystem _bubbles;
        private static Material _autoMat, _autoLumpMat;
        private PotLidSocket _lid;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int AgitationId = Shader.PropertyToID("_Agitation");
        private static readonly int RingsId = Shader.PropertyToID("_Rings");
        private static readonly int CreamColorId = Shader.PropertyToID("_CreamColor");
        private static readonly int CreamCoverId = Shader.PropertyToID("_CreamCover");
        private static readonly int CreamScaleId = Shader.PropertyToID("_CreamScale");
        private static readonly int SwirlId = Shader.PropertyToID("_Swirl");
        private static readonly int SwirlAmountId = Shader.PropertyToID("_SwirlAmount");
        private static readonly int FoamId = Shader.PropertyToID("_Foam");
        private static readonly int MeniscusId = Shader.PropertyToID("_Meniscus");
        private static readonly int DensityId = Shader.PropertyToID("_Density");
        // v23.4: аниме-слои шейдера (см. CatCooksAnime.hlsl)
        private static readonly int AnimeCenterId = Shader.PropertyToID("_AnimeCenter");
        private static readonly int AnimeWallId = Shader.PropertyToID("_AnimeWallShadow");
        private static readonly int AnimeCausticId = Shader.PropertyToID("_AnimeCaustic");
        private static readonly int AnimeGlowId = Shader.PropertyToID("_AnimeGlow");
        private static readonly int AnimeBubblesId = Shader.PropertyToID("_AnimeBubbles");
        private static readonly int AnimeSoftId = Shader.PropertyToID("_AnimeSoft");
        private static readonly int AnimeRimId = Shader.PropertyToID("_AnimeRim");
        private static readonly int AnimeShineId = Shader.PropertyToID("_AnimeShine");
        private static readonly int AnimeFlecksId = Shader.PropertyToID("_AnimeFlecks");
        private static readonly int AnimeFleckColorId = Shader.PropertyToID("_AnimeFleckColor");
        private static readonly int AnimeVeinsId = Shader.PropertyToID("_AnimeVeins");
        private static readonly int AnimeVeinColorId = Shader.PropertyToID("_AnimeVeinColor");
        private static readonly int AnimePoresId = Shader.PropertyToID("_AnimePores");
        private static readonly int AnimeLayersId = Shader.PropertyToID("_AnimeLayers");     // v23.10
        private static readonly int AnimeKindId = Shader.PropertyToID("_AnimeKind");
        private static readonly int AnimeInkId = Shader.PropertyToID("_AnimeInk");
        private static readonly int AnimeDepthId = Shader.PropertyToID("_AnimeDepth");
        private static readonly int AnimeBreathId = Shader.PropertyToID("_AnimeBreath");     // v23.15
        private static readonly int AnimeRadiusId = Shader.PropertyToID("_AnimeRadius");     // v23.24
        /// <summary>v23.24: во сколько раз тара больше обычного ведра (сетка поверхности та же — мелкие узоры растягиваем, иначе «иглы»).</summary>
        private float _lf = 1f;
        private Material _animeSurfMat;
        private bool _animeSurf;
        /// <summary>v23.25: размер одного желтка — общий с полом (EggSplat.YolkK), доля длины яйца.</summary>
        public static float YolkOfEgg => CatCooks.Food.Tools.EggSplat.YolkK;
        private int _animeKind;
        private float _thickSyrup;                                                          // v23.12                                                             // v23.10: 0 гладкое … 5 мороженое
        private static readonly int SkinId = Shader.PropertyToID("_Skin");
        private static readonly int PasteId = Shader.PropertyToID("_Paste");
        private static readonly int FatId = Shader.PropertyToID("_Fat");
        private static readonly int FatScaleId = Shader.PropertyToID("_FatScale");
        private static readonly int TrailId = Shader.PropertyToID("_Trail");
        private static readonly int TrailAmountId = Shader.PropertyToID("_TrailAmount");
        private static readonly int GlintStrengthId = Shader.PropertyToID("_GlintStrength");
        private static readonly int GlintSizeId = Shader.PropertyToID("_GlintSize");
        private static readonly int SheenStrengthId = Shader.PropertyToID("_SheenStrength");
        private static readonly int FresnelStrengthId = Shader.PropertyToID("_FresnelStrength");
        private static readonly int FlatId = Shader.PropertyToID("_Flat");
        private static float MatF(Material m, int id, float def) { return m.HasProperty(id) ? m.GetFloat(id) : def; }

        // ── v4: бегущие волны (x, z — центр в осях тары, w — время рождения, y — сила, м) ──
        private readonly Vector4[] _wv = new Vector4[8];
        private int _wvNext;
        private float _wvStirT, _prevBounceVel;
        private Vector2 _moveDirL;                     // направление движения в осях тары
        private float _moveSpeed;
        // ── v4: след щупа (x, z — точка в осях тары, z — возраст, w — сила); [0] — самая свежая ──
        private readonly Vector4[] _trail = new Vector4[8];
        private readonly Vector4[] _trailSend = new Vector4[8];
        private float _trailT;
        private float _sprayAcc, _puffAcc, _epiAcc;

        // ───────── для выплёскивания и других скриптов ─────────
        /// <summary>Высота жидкости по оси тары над дном, м.</summary>
        public float Level => _lc;
        /// <summary>Высота края, м.</summary>
        public float RimHeight => RimH * Scale;
        public float LevelFraction => RimH > 0f ? Mathf.Clamp01(_lcUpright / RimH) : 0f;
        /// <summary>Наклон поверхности от плеска (подъём на метр в мире по X и Z).</summary>
        public Vector2 SloshSlope => _slope;
        public bool HasLiquid => _liquid + _layer + Butter + _thick + _ice > 0;
        /// <summary>Доля мороженого / заморозки на вид 0..1.</summary>
        public float IceAmount => _iceShown;
        /// <summary>
        /// Свой материал поверхности (шоколад — пористая паста / застывший). null — обычный материал жидкости.
        /// Ставит ChocolateSurfaceLook, когда в таре почти только шоколад.
        /// </summary>
        public Material SurfaceOverride { get; set; }
        /// <summary>Множитель бугров пасты (1 — как масло; шоколад глаже). Ставит ChocolateSurfaceLook.</summary>
        public float PasteReliefK { get; set; } = 1f;
        /// <summary>Свой цвет пасты (прозрачный — как обычно, из продукта/строки Products). Ставит ChocolateSurfaceLook.</summary>
        public Color PasteTint { get; set; }
        public float FrozenAmount => _frozenShown;
        public Color LiquidColor => _iceShown > 0.5f ? _iceColor
                                  : _frozenShown > 0.5f ? Color.Lerp(LiquidBase, _frostColor, 0.5f)
                                  : _thickShown > 0.5f ? _thickColor
                                  : _pasteShown > 0.5f ? _pasteColor : (_layer > _liquid ? _layerColor : LiquidBase);
        /// <summary>Доля сметаны на вид 0..1.</summary>
        public float ThickAmount => _thickShown;
        /// <summary>Бродит закваска (буклит).</summary>
        public bool Fermenting => _fermenting;
        /// <summary>Доля пасты на вид 0..1 (густеет плавно).</summary>
        public float PasteAmount => _pasteShown;
        /// <summary>Можно ли лить: есть жидкость и она ещё не загустела.</summary>
        public bool CanPour => _liquid + _layer + _thick > 0 && Rigid < 0.5f;
        // сметана (роль Thick) тоже «может литься» — медленно и только при сильном наклоне (решает Cookware Spill)
        public Form Shape => _form;
        /// <summary>v17.5: цвет того, что сверху (паста/порошок — их цвет, иначе жидкость).</summary>
        public Color SurfaceColor => Butter > 0 && _pasteShown > 0.3f ? _pasteColor : LiquidColor;
        public Vector2 HalfSize { get { if (_form != Form.Box) return new Vector2(_radiusTop, _radiusTop); Vector4 b = BoxAt(RimH); return new Vector2(b.x, b.y); } }
        /// <summary>Радиус у края в МИРЕ, м (с учётом масштаба, если он учитывается).</summary>
        public float RadiusAtRimWorld => RadiusAtRim * Scale;
        /// <summary>v23.3: полуразмеры дна в мире (для подгонки горки HeapAnchor под форму тары).</summary>
        public Vector2 BottomHalfSizeWorld { get { float S = Scale; if (_form != Form.Box) { float r = WallR(0.001f) * S; return new Vector2(r, r); } Vector4 b = BoxAt(0f); return new Vector2(b.x, b.y) * S; } }
        public Vector2 RimHalfSizeWorld => HalfSize * Scale;
        public float RimHeightWorld => RimH * Scale;
        public float RadiusAtRim { get { if (_form != Form.Box) return WallR(RimH); Vector4 b = BoxAt(RimH); return Mathf.Min(b.x, b.y); } }
        /// <summary>Скорость тары (сглаженная), мир — для капель и струи.</summary>
        public Vector3 ContainerVelocity => _velF;
        /// <summary>Доля сливок на поверхности 0..1 (как видно).</summary>
        public float CreamCover => _creamShown;
        /// <summary>Доля сливок в таре по объёму 0..1.</summary>
        public float CreamFraction
        {
            get
            {
                float c = _layer * _creamVolume, m = _liquid;
                return c + m > 0f ? c / (c + m) : 0f;
            }
        }

        /// <summary>Край для жидкости (ниже края модели на Rim Inset), оси тары, м.</summary>
        private float RimH => Mathf.Max(_maxHeight * 0.4f, _maxHeight - _rimInset);
        private float _fillScale = 1f;
        private float _capacityFill = -1f;          // ≥0 — доля заполнения по Capacity

        private float Scale => _ignoreParentScale ? 1f : Mathf.Max(1e-4f, transform.lossyScale.x);

        // ───────────────────────── ЖИЗНЬ ─────────────────────────

        private void Awake()
        {
            if (_cookware == null) _cookware = GetComponentInParent<Cookware>();
            _lid = GetComponentInParent<PotLidSocket>();
            _mpb = new MaterialPropertyBlock();
            _lumpMpb = new MaterialPropertyBlock();
            if (Application.isBatchMode) return;                    // выделенный сервер: только расчёт

            GameObject go = new GameObject("LiquidSurface (auto)");
            go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            _mf = go.AddComponent<MeshFilter>();
            _mr = go.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = SurfaceMat;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = true;
            _mesh = new Mesh { name = "Liquid", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            AllocMesh();
            _mf.sharedMesh = _mesh;
            _mr.enabled = false;
        }

        private void OnValidate() { _geomDirty = true; }

        private Material SurfaceMat
        {
            get
            {
                if (_material != null) return _material;
                if (_autoMat != null) return _autoMat;
                Shader sh = Shader.Find("CatCooks/Liquid Surface");
                if (sh == null) sh = Shader.Find("CatCooks/Food Heap");
                _autoMat = new Material(sh) { name = "CC Liquid (auto)", hideFlags = HideFlags.DontSave };
                if (_autoMat.HasProperty("_EdgeSoftness")) _autoMat.SetFloat("_EdgeSoftness", 0.02f);
                return _autoMat;
            }
        }

        /// <summary>
        /// Копия материала для плотного молока: непрозрачное смешивание, пишет глубину, очередь AlphaTest.
        /// Так сквозь молоко не видно ни дна, ни швов, ни обводок, и оно попадает в текстуру глубины.
        /// </summary>
        private static Material DenseMat(Material src)
        {
            if (src == null) return null;
            if (DenseMats.TryGetValue(src, out Material m) && m != null) return m;
            m = new Material(src) { name = src.name + " (dense)", hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", 1f);    // One
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", 0f);    // Zero
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 1f);
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = 2450;
            DenseMats[src] = m;
            return m;
        }

        private Material LumpMat
        {
            get
            {
                if (_lumpMaterial != null) return _lumpMaterial;
                if (_autoLumpMat != null) return _autoLumpMat;
                Shader sh = Shader.Find("CatCooks/Food Heap");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
                _autoLumpMat = new Material(sh) { name = "CC Butter (auto)", hideFlags = HideFlags.DontSave };
                if (_autoLumpMat.HasProperty("_TexDetail")) _autoLumpMat.SetFloat("_TexDetail", 0f);
                if (_autoLumpMat.HasProperty("_GlintStrength")) _autoLumpMat.SetFloat("_GlintStrength", 0.9f);
                return _autoLumpMat;
            }
        }

        private void OnEnable() { if (_cookware != null) _cookware.ContentsChanged += OnContents; }
        private void OnDisable()
        {
            if (_cookware != null) _cookware.ContentsChanged -= OnContents;
            foreach (BoilBubble b in _boilBubs) KillBubble(b);
            if (_bubbles != null) CCRuntimeParticles.SetStream(_bubbles, false, Vector3.zero, Vector3.zero, Color.clear, Color.clear, 0f);
        }
        private void Start() { _viewHash = ViewHash(_cookware); Refresh(); }

        /// <summary>
        /// v23.3 (FPS): сетку поверхности (сотни вершин с волнами) пересчитываем каждый кадр, только если её видно
        /// и она близко. Не видно — раз в 8 кадров; далеко (больше 35 м) — через кадр; очень далеко (70 м) — раз в 4.
        /// </summary>
        private bool SurfaceDue()
        {
            int f = Time.frameCount + (GetInstanceID() & 7);
            if (_mr != null && !_mr.isVisible) return f % 8 == 0;
            Camera cam = Camera.main;
            if (cam == null) return true;
            float d2 = (cam.transform.position - transform.position).sqrMagnitude;
            if (d2 > 70f * 70f) return f % 4 == 0;
            if (d2 > 35f * 35f) return (f & 1) == 0;
            return true;
        }

        /// <summary>
        /// v23.1 (FPS): тара шлёт «содержимое изменилось» на КАЖДУЮ строку списка и на каждый шаг таймера готовки.
        /// Пересчитываем вид только когда поменялся сам состав (продукт, состояние, количество, прожарка).
        /// </summary>
        private void OnContents()
        {
            int h = ViewHash(_cookware);
            if (h == _viewHash) return;
            _viewHash = h;
            Refresh();
        }

        public static int ViewHash(Cookware cw)
        {
            if (cw == null) return 0;
            unchecked
            {
                int h = 17 + cw.ViewCount * 31;
                for (int i = 0; i < cw.ViewCount; i++)
                {
                    BagView v = cw.GetView(i);
                    h = h * 486187739 + v.Product;
                    h = h * 486187739 + (int)v.State;
                    h = h * 486187739 + v.Count;
                    h = h * 486187739 + v.Cook;
                }
                return h;
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null) SafeDestroy.Kill(_mesh);
            foreach (Lump l in _lumps) if (l.T != null) SafeDestroy.Kill(l.T.gameObject);
            foreach (Lump l in _bits) if (l.T != null) SafeDestroy.Kill(l.T.gameObject);
            if (_bubbles != null) SafeDestroy.Kill(_bubbles.gameObject);
            if (_whipCloud != null) SafeDestroy.Kill(_whipCloud.transform.parent != null ? _whipCloud.transform.parent.gameObject : _whipCloud.gameObject);
            foreach (Topping t in _tops) { if (t.Go != null) SafeDestroy.Kill(t.Go); if (t.Mesh != null) SafeDestroy.Kill(t.Mesh); }   // v19.1
            _tops.Clear();
            foreach (BoilBubble b in _boilBubs) if (b.T != null) SafeDestroy.Kill(b.T.gameObject);
            _boilBubs.Clear();
        }

        public bool Handles(ushort productId)
        {
            if (_products == null) return false;
            foreach (Entry e in _products) if (e.Product != null && e.Product.Id == productId) return true;
            return false;
        }

        /// <summary>Роль продукта в этой таре; false — не жидкость.</summary>
        public bool TryGetRole(ushort productId, out Role role)
        {
            role = Role.Liquid;
            if (_products == null) return false;
            foreach (Entry e in _products)
                if (e.Product != null && e.Product.Id == productId) { role = e.Role; return true; }
            return false;
        }

        /// <summary>
        /// Пара для взбивания «по умолчанию»: основная жидкость (Role Liquid, есть в таре) → слой (Role TopLayer).
        /// Вибратор берёт её, если его Churn Rules не подошли.
        /// </summary>
        public bool TryGetChurnPair(Cookware cw, out ProductDef from, out ProductDef to) =>
            TryGetChurnPair(cw, out from, out to, out _);

        /// <summary>
        /// То же, по стадиям: молоко (Liquid) → сливки (TopLayer); молока нет — сливки → масло
        /// (Paste, или Floating, если Paste нет). thick = true — вторая стадия (загустение).
        /// </summary>
        private static bool IsWater(ProductDef p) => p != null && !string.IsNullOrEmpty(p.Title)
                                                     && p.Title.ToLowerInvariant().Contains("вода");

        private static bool IsMilk(ProductDef p) => p != null && !string.IsNullOrEmpty(p.Title)
                                                    && p.Title.ToLowerInvariant().Contains("молок");

        private static bool IsCream(ProductDef p) => p != null && !string.IsNullOrEmpty(p.Title)
                                                     && p.Title.ToLowerInvariant().Contains("сливк");

        /// <summary>Сливочное масло (не «Шоколадная паста», не «Шоколад» — их тоже кладут в роль Paste).</summary>
        private static bool IsButter(ProductDef p) => p != null && !string.IsNullOrEmpty(p.Title)
                                                      && p.Title.ToLowerInvariant().Contains("масл");

        // ── v17: порошок ──
        private int _powderN;
        private string[] _powderSplit;
        private static Material _powderMat;
        private static bool _powderMatTried;

        private bool IsPowder(ProductDef p, FoodState state)
        {
            if (p == null || string.IsNullOrEmpty(p.Title)) return false;
            FoodSubstance sub = FoodSubstance.Of(p, state);                  // v18: профиль вещества решает
            if (sub != null && sub.InCookware)
            {
                bool pw = sub.Look == SubstanceLook.Powder || sub.Look == SubstanceLook.Crystals;   // v19: сахар — тоже сыпучее (рельеф порошка, свой блестящий материал)
                if (pw) _powderSub = sub;
                return pw;
            }
            if (IsButter(p)) return false;
            if (_powderSplit == null) _powderSplit = (_powderWords ?? "").ToLowerInvariant().Split(new[] { ',', ';', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            string t = p.Title.ToLowerInvariant();
            if (t.Contains("паст") || t.Contains("масс")) return false;
            foreach (string w in _powderSplit) if (t.Contains(w)) return true;
            return false;
        }

        /// <summary>Почти всё «масло-подобное» в таре — порошок (и шоколад не перебил вид).</summary>
        private bool Powder => Butter > 0 && _powderN * 2 >= Butter && SurfaceOverride == null;

        // ── v18: вещества (FoodSubstance) ──
        private FoodSubstance _powderSub, _slabSub;
        private int _slabN, _slabThickN, _slabSpoilN;
        /// <summary>v19: доля испорченной массы (пятна плесени на поверхности).</summary>
        private float SlabSpoil => _slabN > 0 ? Mathf.Clamp01(_slabSpoilN / (float)_slabN) : 0f;

        /// <summary>Масло по профилю «пласт с мазками».</summary>
        private bool IsSlab(ProductDef p, FoodState state)
        {
            FoodSubstance sub = FoodSubstance.Of(p, state);
            if (sub == null || !sub.InCookware || (sub.Look != SubstanceLook.Butter && sub.Look != SubstanceLook.Spread)) return false;
            if (sub != _slabSub) { _slabSub = sub; _slabStrokes = null; }
            return true;
        }

        /// <summary>Почти всё «масло-подобное» в таре — пласт масла по профилю.</summary>
        private bool Slab => !Powder && Butter > 0 && _slabN * 2 >= Butter && _slabSub != null
                             && (SurfaceOverride == null || _slabSub.Look == SubstanceLook.Spread);   // v19: паста — свой вид, а не материал шоколада
        /// <summary>v18.1: масло, у которого на этой таре роль «Густое» (Thick), — тоже пласт с мазками.</summary>
        private bool SlabThick => _thick > 0 && _slabThickN * 2 >= _thick && SurfaceOverride == null && _slabSub != null;

        private int _subMode = -1;

        /// <summary>v18.1: в консоль — чем рисуется масса в таре (по профилю вещества или по роли).</summary>
        private void LogSubstanceMode()
        {
            int mode = Powder ? 1 : Slab ? 2 : SlabThick ? 3 : (Butter > 0 || _thick > 0) ? 4 : 0;
            if (mode == _subMode) return;
            _subMode = mode;
            string tara = _cookware != null ? _cookware.name : name;
            if (mode == 1) Debug.Log($"[Вещества] «{tara}»: порошок по профилю «{(_powderSub != null ? _powderSub.Title : "слова")}»", this);
            else if (mode == 2 || mode == 3) Debug.Log($"[Вещества] «{tara}»: масло пластом с мазками (роль {(mode == 2 ? "Paste/Floating" : "Thick")}), профиль «{_slabSub.Title}»", this);
            else if (mode == 4) Debug.Log($"[Вещества] «{tara}»: масса без профиля вещества — вид по роли на таре (паста {Butter}, густое {_thick}, из них по профилю масла {_slabN + _slabThickN})", this);
        }

        private struct SlabStroke { public Vector2 C, Dir; public float L, W, K, Dep; }
        private SlabStroke[] _slabStrokes;
        private Vector2 _slabWave;
        private Vector4[] _slabLobes = new Vector4[0], _slabPeaks = new Vector4[0];   // x,y — центр (доли радиуса), z — радиус, w — высота

        private void BuildSlabStrokes()
        {
            int seed = _cookware != null && _cookware.IsSpawned ? _cookware.ObjectId * 131 + 17 : 17;
            var rnd = new System.Random(seed);
            int n = _slabSub != null ? Mathf.Clamp(_slabSub.Smears, 0, 9) : 5;
            _slabStrokes = new SlabStroke[n];
            float baseA = (float)rnd.NextDouble() * 6.28f;
            for (int i = 0; i < n; i++)
            {
                float a = baseA + ((float)rnd.NextDouble() - 0.5f) * 1.6f + (i % 2) * 0.4f;
                _slabStrokes[i] = new SlabStroke
                {
                    C = new Vector2(((float)rnd.NextDouble() - 0.5f) * 1.1f, ((float)rnd.NextDouble() - 0.5f) * 1.1f),
                    Dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a)),
                    L = 0.7f + (float)rnd.NextDouble() * 0.7f,
                    W = 0.09f + (float)rnd.NextDouble() * 0.09f,
                    K = ((float)rnd.NextDouble() - 0.5f) * 1.4f,
                    Dep = 0.6f + (float)rnd.NextDouble() * 0.5f,
                };
            }
            _slabWave = new Vector2((float)rnd.NextDouble() * 40f, (float)rnd.NextDouble() * 40f);
            // v18.2: наплывы и пики
            _slabLobes = new Vector4[6];
            for (int i = 0; i < _slabLobes.Length; i++)
            {
                float a = i / (float)_slabLobes.Length * 6.28f + (float)rnd.NextDouble() * 0.8f;
                float rr = 0.15f + (float)rnd.NextDouble() * 0.5f;
                _slabLobes[i] = new Vector4(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, 0.3f + (float)rnd.NextDouble() * 0.2f, 0.18f + (float)rnd.NextDouble() * 0.17f);
            }
            int np = 3 + rnd.Next(2);
            _slabPeaks = new Vector4[np];
            for (int i = 0; i < np; i++)
            {
                float a = (float)rnd.NextDouble() * 6.28f;
                float rr = (float)rnd.NextDouble() * 0.5f;
                _slabPeaks[i] = new Vector4(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, 0.16f + (float)rnd.NextDouble() * 0.1f, 0.45f + (float)rnd.NextDouble() * 0.35f);
            }
        }

        /// <summary>
        /// v18.2: МАСЛО В ТАРЕ — мягкие наплывы и красивые пики (как взбитое масло), пара широких
        /// гладких мазков мастерком, масло размазано по стенке; ямки от мастерка. Только плавные формы —
        /// никаких мелких полосок и шума (на сетке они выглядели «рваными»).
        /// </summary>
        private float SlabH(float x, float z, float rho, float t, float pa)
        {
            if (_slabStrokes == null) BuildSlabStrokes();
            if (_slabSub != null && _slabSub.Look == SubstanceLook.Spread) return SpreadH(x, z, rho, t, pa);
            float R = Mathf.Max(0.05f, RadiusAtRim);
            float rel = _pasteRelief * PasteReliefK * pa * (_slabSub != null ? _slabSub.CookwareRelief : 1f);
            float depth = _slabSub != null ? _slabSub.SmearDepth : 1f;
            float u = x / R, v = z / R;
            // v18.8: «фермерское» рваное масло — комки разной величины, трещинки (как на полу и на мастерке)
            float h = 0.25f * (1f - rho * rho)
                    + FoodSubstanceMesh.FarmButter(u * 1.15f, v * 1.15f, Mathf.Min(1f, rho * 0.75f), _slabWave.x, _slabWave.y, 1.5f + 0.1f * depth) * 1.1f * depth;
            h *= rel * 2.6f;                                                   // v18.11: рельеф заметнее (было плоско)
            // масло размазано по стенке — плавный подъём у края
            h += rel * 0.4f * Mathf.SmoothStep(0.78f, 1f, rho);

            Vector2 qq = new Vector2(x, z);
            for (int i = 0; i < _dents.Length; i++)
            {
                Vector4 dn = _dents[i];
                if (dn.z <= 0f) continue;
                float dd = Vector2.Distance(qq, new Vector2(dn.x, dn.y));
                if (dd > dn.z * 2.2f) continue;
                float r2 = dn.z * dn.z;
                h -= dn.w * Mathf.Exp(-dd * dd / (r2 * 0.55f)) * pa;
                float ring = dd - dn.z;
                h += dn.w * 0.45f * Mathf.Exp(-ring * ring / (r2 * 0.12f)) * pa;
            }
            return h;
        }

        /// <summary>
        /// v19: ПАСТА (нутелла) В ТАРЕ — гладкая горка, закрученная спиралью, мягкие волны, ямки от мастерка.
        /// Испорченная — свернувшаяся, в комочках.
        /// </summary>
        private float SpreadH(float x, float z, float rho, float t, float pa)
        {
            float R = Mathf.Max(0.05f, RadiusAtRim);
            float rel = _pasteRelief * PasteReliefK * pa * (_slabSub != null ? _slabSub.CookwareRelief : 1f) * 2.2f;
            float u = x / R, v = z / R;
            float a = Mathf.Atan2(v, u);
            float rr = Mathf.Min(1f, Mathf.Sqrt(u * u + v * v));                 // v19.1: круглая спираль (rho у тары-«квадрата» давал квадрат)
            float h = 0.3f * (1f - rr * rr);
            h += Mathf.Pow(0.5f + 0.5f * Mathf.Sin(rr * Mathf.PI * 5f - a + _slabWave.x), 2.2f) * 0.3f * (1f - Mathf.Pow(rr, 4f));
            h += 0.25f * Mathf.Exp(-(rr * rr) / 0.04f);
            float sp = SlabSpoil;
            if (sp > 0.001f) h += (Mathf.PerlinNoise(u * 6f + _slabWave.y, v * 6f + _slabWave.x) - 0.45f) * 0.35f * sp;
            h *= rel;
            h += rel * 0.35f * Mathf.SmoothStep(0.8f, 1f, rho);
            Vector2 qq = new Vector2(x, z);
            for (int i = 0; i < _dents.Length; i++)
            {
                Vector4 dn = _dents[i];
                if (dn.z <= 0f) continue;
                float dd = Vector2.Distance(qq, new Vector2(dn.x, dn.y));
                if (dd > dn.z * 2.2f) continue;
                float r2 = dn.z * dn.z;
                h -= dn.w * Mathf.Exp(-dd * dd / (r2 * 0.55f)) * pa;
                float ring = dd - dn.z;
                h += dn.w * 0.4f * Mathf.Exp(-ring * ring / (r2 * 0.12f)) * pa;
            }
            return h;
        }

        // ═════════════ v19.1: ДОБАВКИ ПОВЕРХ (сахар на какао, масло на какао…) И СМЕШИВАНИЕ ═════════════
        //
        // Основа (чего больше среди пасты/порошка) рисуется поверхностью, как раньше. Остальное с профилем
        // вещества — отдельными кучками поверх, как положили мастерком (не перекрашивая всё). Мешаешь —
        // кучки закручиваются к центру и тают, а по основе спиралью расходятся разводы цвета результата.

        // ── v23: глянцевые жидкости, яйца раздельно, кипение, кучки по порциям ──
        private float _glossyK, _liqFrac;
        private int _yolkN;
        private int _yolkMixedN;                                                 // v23.28: сколько желтков уже размешано (не рисуем)
        private float _yolkMixT;
        private Color _yolkColor = new Color(1f, 0.72f, 0.1f);
        private bool _eggWhiteLook;
        private bool _yolkPile;                                                             // v23.20: одни желтки — кучей
        private bool EggClear => _eggWhiteLook || _yolkPile;                                // v23.20: сырой белок прозрачный
        private readonly List<Transform> _yolkGos = new List<Transform>();
        private float _yolkSpin;
        // v23.1: кипение — мультяшные пузыри-куполки цвета жидкости (вздуваются и лопаются рваной дыркой) + мягкое «дыхание» поверхности
        private sealed class BoilBubble { public Transform T; public MeshRenderer Mr; public float X, Z, Vx, Vz, Size, Life, Dur, Pop; public bool Alive; }
        private readonly List<BoilBubble> _boilBubs = new List<BoilBubble>();
        private float _boil, _boilTarget, _boilSetT = -10f, _bubAcc, _surgeT;
        private MaterialPropertyBlock _bubMpb;
        private static Mesh _bubMesh;
        private static Material _bubMat;
        private static bool _bubMatTried;
        private static readonly int BubColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BubPopId = Shader.PropertyToID("_Pop");
        private ushort _heapBaseId = ushort.MaxValue;
        private int _viewHash = int.MinValue;
        private readonly Dictionary<ushort, int> _topTotals = new Dictionary<ushort, int>();
        private int _topSerial;

        /// <summary>v23: «кипит» 0..1 — ставит CookwareHeat каждый кадр (0.3 — побулькивает, 1 — бурлит).</summary>
        public void SetBoil(float k) { _boilTarget = Mathf.Clamp01(k); _boilSetT = Time.time; }

        private static bool TitleHas(ProductDef d, params string[] keys)
        {
            if (d == null || string.IsNullOrEmpty(d.Title)) return false;
            string t = d.Title.ToLowerInvariant();
            foreach (string k in keys) if (t.Contains(k)) return true;
            return false;
        }
        private float _batterK, _syrupK;

        [Header("v23.4: аниме-вид")]
        [Tooltip("Тень от борта серпом на поверхности (как в аниме-рисовке)")]
        [Range(0f, 1f)][SerializeField] private float _animeWallShadow = 0.85f;

        /// <summary>
        /// v23.4: УНИВЕРСАЛЬНЫЕ ТИПЫ вида (меняется только цвет продукта):
        ///   вода/прозрачное — резкие ступени, каустика-сетка, свечение изнутри у цветных соков;
        ///   молоко/сливки — мягкая ступень, холодная сиреневая тень, тихий контур;
        ///   густое (сметана, тесто) — средняя ступень;
        ///   глянцевое (шоколад, карамель, мёд) — очень резкая ступень, яркий контур; мёд/сироп/карамель — пузырьки внутри.
        /// </summary>
        private void ApplyAnimeLook()
        {
            float S = Scale;
            Vector3 cW = transform.position + transform.rotation * (_center * S);
            _mpb.SetVector(AnimeCenterId, new Vector4(cW.x, cW.y, cW.z, 0f));
            _mpb.SetFloat(AnimeWallId, _surfaceOk ? _animeWallShadow : 0f);
            Color lc = LiquidColor;
            float mx = Mathf.Max(lc.r, Mathf.Max(lc.g, lc.b)), mn = Mathf.Min(lc.r, Mathf.Min(lc.g, lc.b));
            float sat = mx > 1e-3f ? (mx - mn) / mx : 0f;
            bool clear = _waterLike && !_eggWhiteLook;
            _mpb.SetFloat(AnimeCausticId, clear ? 1f : 0f);
            _mpb.SetFloat(AnimeGlowId, clear ? Mathf.Clamp01((sat - 0.15f) * 2.5f) : _syrupK * 0.5f);
            _mpb.SetFloat(AnimeBubblesId, 0f);                                  // v23.16: пузыри в мёде убраны (были странные) — вместо них ленты
            float soft = clear ? 0.02f : Mathf.Lerp(Mathf.Lerp(0.08f, 0.035f, Mathf.Max(_thickShown, _batterK)), 0.02f, _glossyK);
            _mpb.SetFloat(AnimeSoftId, soft);
            _mpb.SetFloat(AnimeRimId, clear ? 0.6f : (_animeKind == 3 || _animeKind == 5) ? 0.25f : Mathf.Lerp(0.4f, 0.85f, _glossyK));   // v23.14: матовым — слабый край
            // блик-«окно»: глянец и вода — ярко, молоко — мягко, густое — средне, порошок/масло — нет
            float shine = clear ? 0.7f : Mathf.Lerp(Mathf.Lerp(0.35f, 0.45f, Mathf.Max(_thickShown, _batterK)), 0.85f, _glossyK);
            if (Powder || Slab || SlabThick || _ice > 0) shine = 0f;
            if (_eggWhiteLook) shine = 0.2f;
            if (_animeKind == 3 || _animeKind == 5) shine = 0f;                  // v23.14: тесто, яичная масса, мороженое — матовые
            _mpb.SetFloat(AnimeShineId, shine);

            // v23.5: штрихи-блёстки, светящиеся прожилки, поры — по типу (кулинарное аниме)
            bool warm = lc.r > lc.b + 0.15f;
            float flecks = clear ? 0f
                         : _glossyK > 0.3f ? 0.55f
                         : _eggWhiteLook ? 0.2f
                         : (_batterK > 0.3f || _thickShown > 0.3f) ? 0.15f
                         : 0.28f;                                              // молоко, сливки, супы
            if (Powder || Slab || SlabThick || _ice > 0) flecks = 0f;
            flecks = 0f;                                                        // v23.12: штрихи-«ногти» убраны — вместо них слои-капли и складки
            _mpb.SetFloat(AnimeFlecksId, flecks);
            Color fc = _glossyK > 0.3f ? Color.Lerp(new Color(Mathf.Min(1f, lc.r * 1.3f + 0.1f), Mathf.Min(1f, lc.g * 1.3f + 0.08f), Mathf.Min(1f, lc.b * 1.2f + 0.05f)), Color.white, 0.35f)
                                       : Color.Lerp(lc, Color.white, 0.8f);
            fc.a = 1f;
            _mpb.SetColor(AnimeFleckColorId, fc);
            float veins = warm && !clear ? Mathf.Clamp01(_boil * 0.8f + (_glossyK > 0.3f ? 0.0f : sat * 0.35f) * _boil) : 0f;
            if (warm && _glossyK > 0.3f) veins = Mathf.Max(veins, 0.6f * _boil);
            _mpb.SetFloat(AnimeVeinsId, veins);
            Color vc = Color.Lerp(lc, new Color(1f, 0.85f, 0.35f), 0.75f); vc.a = 1f;
            _mpb.SetColor(AnimeVeinColorId, vc);
            float pores = _batterK > 0.01f ? _batterK : (_thickShown > 0.3f && !_eggWhiteLook ? 0.5f : 0f);
            if (_animeKind == 3) pores = 0f;                                    // v23.16: у теста свои пузырьки (слой шейдера)
            if (_animeKind == 4) pores = 0f;                                    // v23.43: шоколад — гладкие слои, без пор и светлых капель
            if (Powder || Slab || _ice > 0) pores = 0f;
            _mpb.SetFloat(AnimePoresId, pores);
            // v23.10: многослойность — как у кучек: озерцо, ступени к стенке, складки по виду, мениск, блик-окно, контур
            float layers = 1f;
            if (Powder || Slab || SlabThick) layers = 0f;
            _mpb.SetFloat(AnimeLayersId, layers);
            _mpb.SetFloat(AnimeKindId, _animeKind);
            _mpb.SetFloat(AnimeInkId, layers > 0f ? 0.75f : 0.45f);
            _mpb.SetFloat(AnimeDepthId, layers > 0f ? 0.18f : 0.12f);
            // v23.26: сдвиг «дыхания» — в единицах МЕША. Раньше давали метры: у банки (масштаб 200, Ignore Parent Scale)
            // это выходило в 200 раз больше — поверхность белка вздымалась «пирамидами» по метру. Сырой белок не дышит.
            float meshK = Scale / Mathf.Max(1e-5f, Mathf.Abs(transform.lossyScale.x));
            _mpb.SetFloat(AnimeBreathId, EggClear ? 0f : BreathFor(_animeKind) * RadiusAtRim * meshK);
            _mpb.SetFloat(AnimeRadiusId, RadiusAtRim);                                        // v23.24: узор капель — по размеру тары
        }
        /// <summary>v23.51: эффект взбивания (тучки, капли) сильнее во столько раз.</summary>
        private const float WhipBoost = 3f;
        /// <summary>v23.63: непрозрачность чистой карамели (1 — совсем плотная). Сквозь неё чуть видно дно кастрюли.</summary>
        private const float CaramelOpacity = 0.78f;
        /// <summary>v23.62: эффекты взбивания крупнее (тучки, взрывчики) — во столько раз по размеру.</summary>
        private const float WhipSizeK = 2.4f;
        /// <summary>v23.62: капель больше и они крупнее; плюс мелкие брызги-капельки.</summary>
        private const float WhipDropK = 2f;
        private static readonly Color MilkChoc = new Color(0.36f, 0.21f, 0.13f, 1f);   // v23.51: бисквитное тесто — шоколадное (было светлое 0.6/0.4/0.26)
        private static bool IsSpongeBatter(ProductDef d) => TitleHas(d, "бисквит");
        private static bool IsGlossyLiquid(ProductDef d) => TitleHas(d, "шоколад", "карамел", "мёд", "сироп", "глазур", "джем", "нутелл", "ганаш");
        private static bool IsYolkP(ProductDef d, FoodState s) => s == FoodState.Yolk || TitleHas(d, "желток");
        private static bool IsWhiteP(ProductDef d, FoodState s) => s == FoodState.White || TitleHas(d, "яичный белок");

        private sealed class Topping
        {
            public ushort Id; public FoodState State; public int Count, Built = -1;
            public float AddT; public int Serial;
            public FoodSubstance Sub; public GameObject Go; public Mesh Mesh; public MeshRenderer Mr;
            public float Ang, Dist;
            public float X, Z, Rn, ShowX, ShowZ, BuiltR; public bool Placed;
        }

        private readonly List<Topping> _tops = new List<Topping>();
        private int _pasteBest; private ushort _baseId;
        private float _mixP, _mixDustAcc;
        private Color _mixColor = new Color(0.36f, 0.19f, 0.1f);

        /// <summary>Этот продукт рисуется добавкой-кучкой поверх (HeapAnchor его не рисует).</summary>
        public bool ShowsTopping(ushort productId)
        {
            foreach (Topping t in _tops) if (t.Id == productId && t.Count > 0) return true;
            // v23: ещё не перестроили кучки (порядок событий) — решаем по правилу «сыпучее небольшой порцией»
            FoodDatabase db = FoodDatabase.Instance;
            if (_cookware != null && db != null && isActiveAndEnabled)
            {
                ComputeHeapBase();
                for (int i = 0; i < _cookware.ViewCount; i++)
                {
                    BagView v = _cookware.GetView(i);
                    if (v.Product == productId && v.Count > 0 && v.Product != _baseId && HeapLike(db.Get(v.Product), v.State, v.Count)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// v23: сыпучее и кристаллы (сахар, соль, какао, мука…) небольшими порциями лежат КУЧКАМИ поверх,
        /// а не засыпкой по всему дну. Большой объём (≥ 40% тары) — по-прежнему засыпка (ковш экскаватора).
        /// </summary>
        /// <summary>
        /// v23.1: первое, что попало в таре (если это сыпучее, а жидкости нет) — растекается засыпкой по всей таре,
        /// как раньше. Кучками ложится только то, что добавили ПОСЛЕ.
        /// </summary>
        private void ComputeHeapBase()
        {
            _heapBaseId = ushort.MaxValue;
            if (_cookware == null || _cookware.ViewCount == 0) return;
            FoodDatabase db = FoodDatabase.Instance;
            BagView first = _cookware.GetView(0);
            ProductDef d = db != null ? db.Get(first.Product) : null;
            if (d == null) return;
            FoodSubstance sub = FoodSubstance.Of(d, first.State);
            if (FoodHeapLook.IsLiquid(d, first.State)) return;
            if (sub != null && sub.OwnLook && sub.Look != SubstanceLook.Powder && sub.Look != SubstanceLook.Crystals) return;
            _heapBaseId = first.Product;
        }

        private bool HeapLike(ProductDef d, FoodState st, int count)
        {
            if (d == null) return false;
            if (d.Id == _heapBaseId) return false;
            FoodSubstance sub = FoodSubstance.Of(d, st);
            if (sub == null || !sub.OwnLook || FoodHeapLook.IsLiquid(d, st)) return false;
            if (sub.Look != SubstanceLook.Powder && sub.Look != SubstanceLook.Crystals) return false;
            int cap = _cookware != null && _cookware.Capacity > 0 ? _cookware.Capacity : 20;
            // v23.62: на меренге/жидкости сыпучее ВСЕГДА лежит кучками (мука, смесь, какао), пока не вмешают — даже много
            return count < cap * (WetBase() ? 0.9f : 0.4f);
        }

        private int _wetFrame = -1;
        private bool _wet, _thickBase;

        /// <summary>v23.62: в таре есть основа не-сыпучая (жидкость, меренга, масса) — сыпучее на ней лежит кучками.</summary>
        private bool WetBase()
        {
            if (_wetFrame == Time.frameCount) return _wet;
            _wetFrame = Time.frameCount;
            _wet = false; _thickBase = false;
            FoodDatabase db = FoodDatabase.Instance;
            if (_cookware == null || db == null) return false;
            for (int i = 0; i < _cookware.ViewCount; i++)
            {
                BagView v = _cookware.GetView(i);
                if (v.Count <= 0) continue;
                ProductDef d = db.Get(v.Product);
                if (d == null) continue;
                string tt = string.IsNullOrEmpty(d.Title) ? "" : d.Title.ToLowerInvariant();
                if (tt.Contains("меренг") || tt.Contains("тесто") || tt.Contains("крем") || tt.Contains("масса")) { _wet = true; _thickBase = true; }
                if (FoodHeapLook.IsLiquid(d, v.State)) { _wet = true; continue; }
                FoodSubstance sub = FoodSubstance.Of(d, v.State);
                if (sub != null && sub.OwnLook && sub.Look != SubstanceLook.Powder && sub.Look != SubstanceLook.Crystals && !d.Solid) _wet = true;
            }
            return _wet;
        }

        private void RebuildToppings()
        {
            FoodDatabase db = FoodDatabase.Instance;
            var totals = new Dictionary<ushort, int>();
            var states = new Dictionary<ushort, FoodState>();
            var subs = new Dictionary<ushort, FoodSubstance>();
            if (_cookware != null && db != null)
                for (int i = 0; i < _cookware.ViewCount; i++)
                {
                    BagView v = _cookware.GetView(i);
                    if (v.Count == 0 || v.Product == _baseId) continue;
                    ProductDef d = db.Get(v.Product);
                    FoodSubstance sub = d != null ? FoodSubstance.Of(d, v.State) : null;
                    if (sub == null || !sub.OwnLook || FoodHeapLook.IsLiquid(d, v.State)) continue;
                    bool heap = HeapLike(d, v.State, v.Count);
                    if (!heap && Butter <= 0) continue;                                // как раньше: добавки поверх масла/пасты
                    int c0; totals.TryGetValue(v.Product, out c0);
                    totals[v.Product] = c0 + v.Count;
                    states[v.Product] = v.State; subs[v.Product] = sub;
                }
            // по продуктам: прибавилось — новая кучка (порция), убавилось — снимаем с последних
            foreach (var kv in totals)
            {
                int prev; _topTotals.TryGetValue(kv.Key, out prev);
                int delta = kv.Value - prev;
                if (delta > 0)
                {
                    Topping last = null;
                    foreach (Topping x in _tops) if (x.Id == kv.Key && (last == null || x.Serial > last.Serial)) last = x;
                    if (last != null && Time.time - last.AddT < 0.6f) { last.Count += delta; last.AddT = Time.time; }   // сыплют струйкой — та же кучка
                    else
                    {
                        int k = _topSerial++;
                        var t = new Topping { Id = kv.Key, Count = delta, AddT = Time.time, Serial = k };
                        t.Ang = k * 2.39996f + Mathf.Repeat(kv.Key * 0.618034f, 1f) * 6.2832f;
                        t.Dist = 0.18f + 0.38f * Mathf.Sqrt(Mathf.Repeat(k * 0.6180339f + 0.31f, 1f));
                        PlaceNewTopping(t);
                        _tops.Add(t);
                    }
                }
                else if (delta < 0)
                {
                    int take = -delta;
                    for (int i = _tops.Count - 1; i >= 0 && take > 0; i--)
                    {
                        if (_tops[i].Id != kv.Key) continue;
                        int d = Mathf.Min(take, _tops[i].Count);
                        _tops[i].Count -= d; take -= d;
                    }
                }
            }
            foreach (Topping t in _tops)
            {
                if (!totals.ContainsKey(t.Id)) { t.Count = 0; continue; }
                t.State = states[t.Id]; t.Sub = subs[t.Id];
            }
            _topTotals.Clear();
            foreach (var kv in totals) _topTotals[kv.Key] = kv.Value;
            for (int i = _tops.Count - 1; i >= 0; i--)
                if (_tops[i].Count <= 0)
                {
                    if (_tops[i].Go != null) SafeDestroy.Kill(_tops[i].Go);
                    if (_tops[i].Mesh != null) SafeDestroy.Kill(_tops[i].Mesh);
                    _tops.RemoveAt(i);
                }
            RelaxToppings();
        }

        private float ToppingRadius01(Topping t)
        {
            int cap = _cookware != null && _cookware.Capacity > 0 ? _cookware.Capacity : 20;
            return Mathf.Clamp(0.12f + 0.55f * Mathf.Sqrt(t.Count / (float)Mathf.Max(1, cap)), 0.1f, 0.42f);
        }

        /// <summary>v23.1: новую кучку — туда, где больше всего свободного места (не в соседнюю).</summary>
        private void PlaceNewTopping(Topping t)
        {
            float rn = ToppingRadius01(t);
            float bestScore = float.MinValue, bx = 0f, bz = 0f;
            for (int c = 0; c < 40; c++)
            {
                float a = c * 2.39996f + t.Serial * 1.3f;
                float d = (0.85f - rn) * Mathf.Sqrt((c + 0.5f) / 40f);
                float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
                float score = 0.85f - rn - d;                                 // от стенки
                foreach (Topping o in _tops)
                {
                    if (o.Count <= 0 || !o.Placed) continue;
                    float dx = x - o.X, dz = z - o.Z;
                    score = Mathf.Min(score, Mathf.Sqrt(dx * dx + dz * dz) - rn - o.Rn);
                }
                if (score > bestScore) { bestScore = score; bx = x; bz = z; }
            }
            t.X = t.ShowX = bx; t.Z = t.ShowZ = bz; t.Rn = rn; t.Placed = true;
        }

        /// <summary>v23.1: кучки не лежат друг в друге — раздвигаются, при тесноте чуть уменьшаются.</summary>
        private void RelaxToppings()
        {
            int n = _tops.Count;
            if (n == 0) return;
            float area = 0f;
            foreach (Topping t in _tops)
            {
                if (!t.Placed) { t.Ang = t.Serial * 2.39996f; t.X = t.ShowX = Mathf.Cos(t.Ang) * 0.4f; t.Z = t.ShowZ = Mathf.Sin(t.Ang) * 0.4f; t.Placed = true; }
                t.Rn = ToppingRadius01(t);
                area += t.Rn * t.Rn;
            }
            float shrink = Mathf.Min(1f, Mathf.Sqrt(0.42f / Mathf.Max(1e-4f, area)));    // тесно — все кучки чуть меньше
            foreach (Topping t in _tops) t.Rn *= shrink;
            const float gap = 1.12f;
            for (int it = 0; it < 24; it++)
            {
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        Topping a = _tops[i], b = _tops[j];
                        float dx = b.X - a.X, dz = b.Z - a.Z;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        float need = (a.Rn + b.Rn) * gap;
                        if (d >= need) continue;
                        if (d < 1e-4f) { float an = (i * 7 + j * 13) * 0.7f; dx = Mathf.Cos(an); dz = Mathf.Sin(an); d = 1f; }
                        float push = (need - Mathf.Max(d, 1e-4f)) * 0.5f;
                        float nx = dx / Mathf.Max(d, 1e-4f), nz = dz / Mathf.Max(d, 1e-4f);
                        a.X -= nx * push; a.Z -= nz * push;
                        b.X += nx * push; b.Z += nz * push;
                    }
                foreach (Topping t in _tops)
                {
                    float lim = Mathf.Max(0f, 0.88f - t.Rn);
                    float d = Mathf.Sqrt(t.X * t.X + t.Z * t.Z);
                    if (d > lim && d > 1e-4f) { t.X *= lim / d; t.Z *= lim / d; }
                }
            }
        }

        private Color MixSwirl(Color pc, float x, float z)
        {
            float R = Mathf.Max(0.05f, RadiusAtRim);
            float u = x / R, v = z / R;
            float rr = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
            float band = 0.5f + 0.5f * Mathf.Sin(a * 2f + rr * 12f - _mixP * 14f);
            float m = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_mixP * 1.7f - band * 0.8f));
            return Color.Lerp(pc, _mixColor, m);
        }

        // ───────────────────────── v23.1: КИПЕНИЕ ─────────────────────────

        /// <summary>
        /// Мультяшное кипение (по мотивам «пузырей ведьминого котла»): на поверхности вздуваются куполки цвета жидкости
        /// с бликом по краю, лопаются рваной дыркой сверху — по воде расходится маленькое колечко.
        /// Поверхность мягко «дышит». Густое — пузыри крупнее, реже и медленнее. Сила — SetBoil (CookwareHeat).
        /// </summary>
        private void UpdateBoil(float dt)
        {
            // v23.39: густое и вязкое (шоколадная масса, тесто, меренга, мороженое, мёд, яичная масса) на огне НЕ кипит пузырями —
            // просто греется/тает (чистый переход без «взрывов»); кипят только жидкие (вода, молоко, бульон)
            int ak = _animeKind;
            bool calmMass = ak == 2 || ak == 3 || ak == 4 || ak == 5 || ak == 7 || ak == 8 || ak == 9 || _glossyK > 0.3f || Stiff > 0.35f || _batterK > 0.3f;
            float target = Time.time - _boilSetT < 0.5f && !calmMass ? _boilTarget : 0f;
            _boil = Mathf.MoveTowards(_boil, target, dt * (target > _boil ? 0.7f : 0.5f));
            float S = Scale, R = Mathf.Max(0.04f, RadiusAtRim);
            float thick = Mathf.Max(Stiff, Mathf.Max(_glossyK * 0.7f, _viscK));
            bool can = _surfaceOk && _boil > 0.02f && !Application.isBatchMode && !Powder && !Slab && !SlabThick;
            if (can)
            {
                // чаще у воды, реже и крупнее у густого; большая тара — больше пузырей
                _bubAcc += dt * _boil * _boil * Mathf.Lerp(26f, 7f, thick) * Mathf.Clamp(R * S / 0.15f, 0.7f, 4f);
                while (_bubAcc >= 1f) { _bubAcc -= 1f; SpawnBoilBubble(R, thick); }
            }
            else _bubAcc = 0f;
            // v23.2: крупные ленивые «накаты» по кипящей поверхности
            if (can)
            {
                _surgeT -= dt;
                if (_surgeT <= 0f)
                {
                    _surgeT = UnityEngine.Random.Range(0.25f, 0.6f) * (1f + thick);
                    Vector3 sp = RandomSurfacePoint(0.8f);
                    EmitWave(new Vector2(sp.x, sp.z), R * 0.012f * _boil * (1f - thick * 0.85f));   // v23.24: густое — без дрожи
                }
            }
            if (_boilBubs.Count == 0) return;
            if (_bubMpb == null) _bubMpb = new MaterialPropertyBlock();
            Color bc = BubbleColor();
            float edge = Mathf.Max(0.02f, _tEdgeMin) * 0.85f;
            for (int i = 0; i < _boilBubs.Count; i++)
            {
                BoilBubble b = _boilBubs[i];
                if (!b.Alive) continue;
                if (!_surfaceOk) { KillBubble(b); continue; }
                b.Life += dt;
                b.X += b.Vx * dt; b.Z += b.Vz * dt;
                Vector3 off = new Vector3(b.X, 0f, b.Z) - _center;
                float od = Mathf.Sqrt(off.x * off.x + off.z * off.z);
                float lim = EdgeLimitAt(new Vector3(b.X, 0f, b.Z), 0.9f);
                if (od > lim && od > 1e-5f) { b.X = _center.x + off.x / od * lim; b.Z = _center.z + off.z / od * lim; b.Vx = -b.Vx; b.Vz = -b.Vz; }
                float grow = Mathf.Clamp01(b.Life / b.Dur);
                if (b.Life >= b.Dur)
                {
                    b.Pop += dt / Mathf.Lerp(0.09f, 0.2f, thick);
                    if (b.Pop >= 1f)
                    {
                        EmitWave(new Vector2(b.X, b.Z), b.Size / Mathf.Max(1e-4f, S) * 0.35f * (1f - thick * 0.8f));   // v23.24: густое лопается мягко
                        // v23.2: лопнул — «выстрел» капель вверх (коронка), у густого реже, но крупнее
                        int nd = thick > 0.5f ? (UnityEngine.Random.value < 0.5f ? 1 : 0) : UnityEngine.Random.Range(2, 5);
                        if (_boil < 0.5f) nd = Mathf.Min(nd, 1);
                        float up = Mathf.Sqrt(b.Size) * Mathf.Lerp(2.6f, 1.4f, thick) * (0.6f + 0.6f * _boil);
                        for (int q = 0; q < nd; q++)
                        {
                            Vector3 side = UnityEngine.Random.insideUnitSphere; side -= _nDynW * Vector3.Dot(side, _nDynW);
                            LiquidFx.Drop(b.T.position + _nDynW * b.Size * 0.25f,
                                          _nDynW * up * UnityEngine.Random.Range(0.7f, 1.3f) + side * up * 0.3f + _velF,
                                          b.Size * UnityEngine.Random.Range(0.12f, 0.22f) * (1f + thick * 0.6f), BubbleColor(), UnityEngine.Random.Range(0.35f, 0.6f));
                        }
                        KillBubble(b);
                        continue;
                    }
                }
                float sz = b.Size * (1f - (1f - grow) * (1f - grow) * (1f - grow));      // ease-out: быстро вздулся, потом медленно
                Vector3 w = transform.position + transform.rotation * (new Vector3(b.X, _lc, b.Z) * S);
                float sy;
                if (TryGetSurfaceY(w, out sy)) w.y = sy + BoilH(b.X, b.Z, Time.time) * S;
                b.T.position = w - _nDynW * sz * 0.12f;
                b.T.rotation = Quaternion.FromToRotation(Vector3.up, _nDynW);
                b.T.localScale = new Vector3(sz, sz * Mathf.Lerp(0.8f, 0.55f, thick), sz);
                _bubMpb.SetColor(BubColorId, bc);
                _bubMpb.SetFloat(BubPopId, b.Pop);
                b.Mr.SetPropertyBlock(_bubMpb);
            }
        }

        private Color BubbleColor()
        {
            Color c = LiquidColor;
            c = Color.Lerp(c, Color.white, _waterLike ? 0.55f : 0.12f);
            c.a = 1f;
            return c;
        }

        private void SpawnBoilBubble(float R, float thick)
        {
            BoilBubble b = null;
            foreach (BoilBubble x in _boilBubs) if (!x.Alive) { b = x; break; }
            if (b == null)
            {
                if (_boilBubs.Count >= 48) return;
                Material m = BubbleMat;
                if (m == null) return;
                var go = new GameObject("BoilBubble (auto)") { hideFlags = HideFlags.DontSave };
                go.layer = gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = BubbleMesh;
                b = new BoilBubble { T = go.transform, Mr = go.AddComponent<MeshRenderer>() };
                b.Mr.sharedMaterial = m;
                b.Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _boilBubs.Add(b);
            }
            Vector3 local = RandomSurfacePoint(0.9f);                             // v23.2: по всей поверхности
            float S = Scale;
            b.X = local.x; b.Z = local.z;
            Vector2 v = UnityEngine.Random.insideUnitCircle * R * 0.06f;
            b.Vx = v.x; b.Vz = v.y;
            float big = UnityEngine.Random.value < 0.15f ? 1.9f : 1f;                     // изредка крупный «бульк»
            b.Size = R * S * UnityEngine.Random.Range(0.05f, 0.11f) * (1f + thick * 0.9f) * big * (0.75f + 0.25f * _boil);
            b.Life = 0f; b.Pop = 0f;
            b.Dur = UnityEngine.Random.Range(0.25f, 0.6f) * (1f + thick * 2f) * Mathf.Sqrt(big);
            b.Alive = true;
            b.T.gameObject.SetActive(true);
            b.T.localScale = Vector3.zero;
        }

        private static void KillBubble(BoilBubble b)
        {
            b.Alive = false;
            if (b.T != null && b.T.gameObject.activeSelf) b.T.gameObject.SetActive(false);
        }

        /// <summary>v23.22: купол-пузырёк и его материал — для пены яичной массы (CCFoamField).</summary>
        public static Material FoamBubbleMat => BubbleMat;
        public static Mesh FoamBubbleMesh => BubbleMesh;

        private CCFoamField _foamField;

        /// <summary>v23.22: настоящие пузырьки пены у яичной массы (в любой таре).</summary>
        private void UpdateFoamField()
        {
            bool want = _animeKind == 8 && _surfaceOk && !Application.isBatchMode;
            if (!want) { if (_foamField != null) _foamField.Active = false; return; }
            if (_foamField == null)
            {
                var go = new GameObject("EggFoam (auto)") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                _foamField = go.AddComponent<CCFoamField>();
                _foamField.Lv = this;
            }
            float S = Scale;
            _foamField.Center = transform.position + transform.rotation * (_center * S);
            _foamField.Radius = Mathf.Max(0.02f, _tEdgeMin) * S * 0.9f;
            _foamField.SurfaceY = _foamField.Center.y;
            _foamField.Color = Color.Lerp(LiquidColor, Color.white, 0.4f);
            _foamField.Count = Mathf.Clamp(Mathf.RoundToInt(_foamField.Radius / 0.006f), 12, 44);
            _foamField.Active = true;
        }

        private static Material BubbleMat
        {
            get
            {
                if (_bubMat == null && !_bubMatTried)
                {
                    _bubMatTried = true;
                    Shader sh = Shader.Find("CatCooks/Boil Bubble");
                    if (sh == null) { sh = Shader.Find("Universal Render Pipeline/Simple Lit"); Debug.LogWarning("[Кипение] нет шейдера «CatCooks/Boil Bubble» (Resources/CatCooksBoilBubble.shader) — пузыри будут простыми"); }
                    if (sh != null) _bubMat = new Material(sh) { name = "CC Boil Bubble (auto)", hideFlags = HideFlags.DontSave, enableInstancing = true };
                }
                return _bubMat;
            }
        }

        /// <summary>Куполок-полусфера радиуса 0.5 (низ открыт — сидит на воде).</summary>
        private static Mesh BubbleMesh
        {
            get
            {
                if (_bubMesh != null) return _bubMesh;
                const int seg = 18, rings = 7;
                var v = new Vector3[(seg + 1) * (rings + 1)];
                var n = new Vector3[v.Length];
                var t = new int[seg * rings * 6];
                for (int j = 0; j <= rings; j++)
                {
                    float ph = j / (float)rings * Mathf.PI * 0.5f;
                    for (int i = 0; i <= seg; i++)
                    {
                        float a = i / (float)seg * Mathf.PI * 2f;
                        Vector3 d = new Vector3(Mathf.Sin(ph) * Mathf.Cos(a), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(a));
                        v[j * (seg + 1) + i] = d * 0.5f; n[j * (seg + 1) + i] = d;
                    }
                }
                int k = 0;
                for (int j = 0; j < rings; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a0 = j * (seg + 1) + i, a1 = a0 + 1, b0 = a0 + seg + 1, b1 = b0 + 1;
                        t[k++] = a0; t[k++] = a1; t[k++] = b0;
                        t[k++] = a1; t[k++] = b1; t[k++] = b0;
                    }
                _bubMesh = new Mesh { name = "Boil Bubble", hideFlags = HideFlags.DontSave };
                _bubMesh.vertices = v; _bubMesh.normals = n; _bubMesh.triangles = t;
                _bubMesh.RecalculateBounds();
                return _bubMesh;
            }
        }

        /// <summary>«Дыхание» кипящей поверхности: мягкая крупная зыбь + мелкая рябь (как в шейдерных уроках — шум едет по времени).</summary>
        private float BoilH(float x, float z, float t)
        {
            float R = Mathf.Max(0.04f, RadiusAtRim);
            float thick = Mathf.Max(Stiff, _glossyK * 0.7f);
            float u = x / R, w = z / R;
            float big = Mathf.PerlinNoise(u * 1.8f + t * 0.3f + 3.1f, w * 1.8f - t * 0.22f + 7.7f) - 0.5f;
            return R * _boil * 0.04f * big * (1f - thick * 0.4f);              // v23.5: плавные медленные волны, без мелкой «вибрации»
        }

        // ───────────────────────── v23: ЖЕЛТКИ ЦЕЛЫМИ ─────────────────────────

        private void SyncYolks()
        {
            if (Application.isBatchMode) return;
            if (_yolkMixedN > _yolkN) _yolkMixedN = _yolkN;                      // v23.28: размешанные желтки не показываем
            int want = Mathf.Min(Mathf.Max(0, _yolkN - _yolkMixedN), _yolkPile ? 16 : 24);   // v23.20: куча — до 16
            while (_yolkGos.Count > want)
            {
                Transform y = _yolkGos[_yolkGos.Count - 1];
                if (y != null) Destroy(y.gameObject);
                _yolkGos.RemoveAt(_yolkGos.Count - 1);
            }
            while (_yolkGos.Count < want)
            {
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(g.GetComponent<Collider>());
                g.name = "Yolk (auto)";
                g.hideFlags = HideFlags.DontSave;
                g.transform.SetParent(transform, true);
                MeshRenderer mr = g.GetComponent<MeshRenderer>();
                Material ym = ToonYolkMat;
                if (ym != null) { g.GetComponent<MeshFilter>().sharedMesh = ToonYolkMesh(g.GetComponent<MeshFilter>().sharedMesh); mr.sharedMaterial = ym; }
                else mr.sharedMaterial = CatCooks.Food.Tools.EggSplat.YolkMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _yolkGos.Add(g.transform);
            }
        }

        private static Material _toonYolkMat;
        private static Mesh _toonYolkMesh;

        /// <summary>v23.5: мультяшный желток (шейдер «масла»: золотистая тень, сливочный свет, блик-«боб»), без «пластикового» блеска.</summary>
        /// <summary>v23.16: материал и сетка желтка — для стенда.</summary>
        public static Material YolkMaterial => ToonYolkMat;
        public static Mesh YolkMesh(Mesh sphere) => ToonYolkMesh(sphere);

        private static Material ToonYolkMat
        {
            get
            {
                if (_toonYolkMat != null) return _toonYolkMat;
                // v23.16: желток — аниме-шейдер кучек (тон, блик, СИЛУЭТ-ОБВОДКА), цвет из вершин
                Shader ah = Shader.Find(FoodSubstance.AnimeShader);
                if (ah != null)
                {
                    _toonYolkMat = new Material(ah) { name = "Anime Yolk (auto)", hideFlags = HideFlags.DontSave };
                    FoodSubstance.AnimePreset(_toonYolkMat, SubstanceLook.Butter);
                    _toonYolkMat.SetFloat("_HSpec", 0.75f); _toonYolkMat.SetFloat("_HSpecSize", 0.975f); _toonYolkMat.SetFloat("_HSparkle", 0f);
                    _toonYolkMat.SetFloat("_HOutline", 2f); _toonYolkMat.SetFloat("_HInk", 0.35f); _toonYolkMat.SetFloat("_HLayers", 0f);
                    _toonYolkMat.SetFloat("_HContact", 0f);
                    return _toonYolkMat;
                }
                Shader sh = Shader.Find("CatCooks/Butter");
                if (sh == null) return null;
                _toonYolkMat = new Material(sh) { name = "Toon Yolk (auto)", hideFlags = HideFlags.DontSave };
                if (_toonYolkMat.HasProperty("_Sheen")) _toonYolkMat.SetFloat("_Sheen", 0.55f);
                if (_toonYolkMat.HasProperty("_Rim")) _toonYolkMat.SetFloat("_Rim", 0.3f);
                if (_toonYolkMat.HasProperty("_Ripple")) _toonYolkMat.SetFloat("_Ripple", 0f);
                if (_toonYolkMat.HasProperty("_ShadowTint")) _toonYolkMat.SetColor("_ShadowTint", new Color(0.95f, 0.5f, 0.2f, 1f));
                return _toonYolkMat;
            }
        }

        /// <summary>Сфера с цветом желтка в вершинах (шейдеры еды берут цвет из вершин).</summary>
        private static Mesh ToonYolkMesh(Mesh src)
        {
            if (_toonYolkMesh != null) return _toonYolkMesh;
            if (src == null) return null;
            _toonYolkMesh = Instantiate(src);
            _toonYolkMesh.name = "Toon Yolk (auto)";
            _toonYolkMesh.hideFlags = HideFlags.DontSave;
            var c = new Color[_toonYolkMesh.vertexCount];
            Color yc = new Color(1f, 0.68f, 0.08f, 1f);
            for (int i = 0; i < c.Length; i++) c[i] = yc;
            _toonYolkMesh.colors = c;
            return _toonYolkMesh;
        }

        /// <summary>Желтки плавают целыми куполками на белке; мешаешь — кружатся вокруг щупа.</summary>
        private void UpdateYolks(float dt)
        {
            if (_yolkGos.Count == 0) return;
            if (_stirAmt > 0.05f) _yolkSpin += dt * _stirAmt * 2.2f;
            float S = Scale, R = Mathf.Max(0.04f, RadiusAtRim);
            // v23.3: размер настоящего желтка (как у шлепка на полу) — одинаковый в любой таре; в маленькой — не больше её
            // v23.24: ОДИН размер желтка везде (ведро, банка, ковш, пол) — от размера самого яйца, не от тары.
            // Уменьшается только если тара уже двух желтков.
            float size = Mathf.Min(CatCooks.Food.Tools.EggSplat.YolkWorld, R * S * 1.2f);   // v23.28: один размер (от яйца, запомненного один раз)
            // v23.28: мешают (миксер/вибратор) — желтки за 3 с расходятся в смеси и пропадают; новые — снова целые
            bool stirY = _stirAmt > 0.3f && Time.time < _stirUntil + 0.1f;
            if (stirY && !_yolkPile && (_liquid > 0 || _layer > 0)) _yolkMixT += dt;
            float mixY = Mathf.Clamp01(_yolkMixT / 3f);
            if (mixY >= 1f)
            {
                _yolkMixedN = _yolkN; _yolkMixT = 0f;
                SyncYolks();
                return;
            }
            size *= 1f - 0.9f * mixY * mixY;
            float edgeL = Mathf.Max(0.02f, _tEdgeMin);
            int n = _yolkGos.Count;
            for (int k = 0; k < n; k++)
            {
                Transform y = _yolkGos[k];
                if (y == null) continue;
                bool show = _surfaceOk;
                if (y.gameObject.activeSelf != show) y.gameObject.SetActive(show);
                if (!show) continue;
                float ang = k * 2.39996f + _yolkSpin;
                float dist = Mathf.Max(0.02f, _tEdgeMin) * 0.7f * Mathf.Sqrt((k + 0.5f) / n);
                float layerUp = 0f;
                if (_yolkPile)
                {
                    // v23.20: куча — слоями: 7 внизу по кругу, 5 выше, 3, потом 1 сверху
                    int li = k < 7 ? 0 : k < 12 ? 1 : k < 15 ? 2 : 3;
                    int start = li == 0 ? 0 : li == 1 ? 7 : li == 2 ? 12 : 15;
                    int cnt = li == 0 ? 7 : li == 1 ? 5 : li == 2 ? 3 : Mathf.Max(1, n - 15);
                    float ringR = size * (li == 0 ? 0.95f : li == 1 ? 0.62f : li == 2 ? 0.36f : 0.12f);
                    ang = (k - start) / (float)cnt * 6.2832f + li * 0.6f + _yolkSpin;
                    dist = Mathf.Min(ringR / Mathf.Max(1e-4f, S), Mathf.Max(0.02f, _tEdgeMin) * 0.8f);
                    layerUp = li * size * 0.42f;
                }
                dist = Mathf.Min(dist, Mathf.Max(0f, edgeL - size * 0.5f / Mathf.Max(1e-4f, S)));   // v23.26: желток целиком внутри, не торчит сквозь стенку
                Vector3 local = _center + _e1 * (Mathf.Cos(ang) * dist) + _e2 * (Mathf.Sin(ang) * dist);
                Vector3 w = transform.position + transform.rotation * (local * S);
                float sy;
                if (TryGetSurfaceY(w, out sy)) w.y = sy;
                float bob = Mathf.Sin(Time.time * 2f + k * 1.7f) * size * 0.04f;
                y.position = w + Vector3.up * (size * 0.1f + bob + layerUp);
                y.rotation = transform.rotation;
                y.localScale = Vector3.one;
                Vector3 ls = y.lossyScale;
                y.localScale = new Vector3(size / Mathf.Max(1e-5f, ls.x), size * 0.55f / Mathf.Max(1e-5f, ls.y), size / Mathf.Max(1e-5f, ls.z));
            }
        }

        private float _blendInfoAt, _blendSec = 6f;
        private bool _blendOk;
        private Color _blendColor = Color.white;

        private void UpdateToppings(float dt)
        {
            float S = Scale, R = Mathf.Max(0.05f, RadiusAtRim);
            bool stirring = _stirAmt > 0.3f && Time.time < _stirUntil + 0.1f;
            // смешивание (только вид; результат считает сервер — FoodBlend)
            if (stirring && _tops.Count > 0)
            {
                if (Time.time >= _blendInfoAt)                                     // v23.1 (FPS): поиск рецепта смеси — не каждый кадр
                {
                    _blendInfoAt = Time.time + 0.5f;
                    _blendOk = FoodBlend.ClientInfo(_cookware, out _blendSec, out _blendColor);
                }
                if (_blendOk) { _mixColor = _blendColor; _mixP = Mathf.Min(0.97f, _mixP + dt / Mathf.Max(0.5f, _blendSec)); }
            }
            // пыль и крошки при мешании порошка/масла (большой миксер — реже)
            if (stirring && _surfaceOk && (Powder || Slab))
            {
                float k = Time.time < _stirFxUntil ? _stirFxK : 1f;
                _mixDustAcc += dt * 3f * k;
                if (_mixDustAcc >= 1f)
                {
                    _mixDustAcc -= 1f;
                    Vector3 at = transform.position + transform.rotation * (new Vector3(_stirRawLocal.x, _lc, _stirRawLocal.y) * S);
                    if (Powder) CCPowderFx.Puff(at, _nDynW, _pasteColor, R * S * 0.35f, 0.5f);
                    else CCPowderFx.Crumb(at + _nDynW * 0.02f, _nDynW * 0.6f + UnityEngine.Random.insideUnitSphere * 0.3f, _pasteColor, R * S * 0.05f, true);
                }
            }
            if (_tops.Count == 0) return;
            FoodDatabase db = FoodDatabase.Instance;
            int total = Mathf.Max(1, _cookware != null ? _cookware.TotalCount : 1);
            float lift = _pasteRelief * PasteReliefK * 0.5f;
            foreach (Topping t in _tops)
            {
                if (t.Go == null)
                {
                    t.Go = new GameObject("Topping (auto)") { hideFlags = HideFlags.DontSave };
                    t.Go.layer = gameObject.layer;
                    t.Mesh = new Mesh { name = "Topping", hideFlags = HideFlags.DontSave };
                    t.Go.AddComponent<MeshFilter>().sharedMesh = t.Mesh;
                    t.Mr = t.Go.AddComponent<MeshRenderer>();
                    t.Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                if (t.Built != t.Count || Mathf.Abs(t.BuiltR - t.Rn) > 0.02f)
                {
                    t.Built = t.Count;
                    t.BuiltR = t.Rn;
                    ProductDef d = db != null ? db.Get(t.Id) : null;
                    Color c = t.Sub.ColorFor(FoodColors.Of(d, t.State));
                    float r = R * S * Mathf.Max(0.05f, t.Rn);
                    var dm = new FoodSubstanceMesh.Dims { Rx = r, Rz = r, H = r * Mathf.Max(0.3f, t.Sub.FloorSlope) * 0.85f, Edge = 0f, RectN = 2f };
                    int seed = t.Id * 131 + 7 + t.Serial * 977;
                    FoodSubstanceMesh.SpoilK = FoodStateInfo.IsSpoiled(t.State) ? 1f : 0f;
                    float softK = FoodSubstanceMesh.SoftHeapScale;
                    FoodSubstanceMesh.SoftHeapScale = 1f;                               // v23.24: в таре — размер по таре
                    if (t.Sub.Look == SubstanceLook.Powder) FoodSubstanceMesh.Powder(t.Mesh, dm, t.Sub.Wonky, c, seed, t.Sub.Sugar);
                    else if (t.Sub.Look == SubstanceLook.Crystals) FoodSubstanceMesh.Crystals(t.Mesh, dm, t.Sub.CrystalSize, t.Sub.CrystalDensity, c, seed);
                    else if (t.Sub.Look == SubstanceLook.Spread) FoodSubstanceMesh.Spread(t.Mesh, dm, t.Sub.Curl * 0.6f, c, seed);
                    else if (t.Sub.Look == SubstanceLook.IceCream) FoodSubstanceMesh.IceCream(t.Mesh, dm, c, seed);
                    else if (t.Sub.Look == SubstanceLook.Meringue) FoodSubstanceMesh.MeringueKiss(t.Mesh, dm, c, seed);
                    else FoodSubstanceMesh.ButterSmear(t.Mesh, dm, 0f, c, seed);
                    FoodSubstanceMesh.SoftHeapScale = softK;
                    FoodSubstanceMesh.SpoilK = 0f;
                    Material m = t.Sub.GetMaterial();
                    if (m != null) t.Mr.sharedMaterial = m;
                }
                float ease = 1f - Mathf.Exp(-dt * 6f);
                t.ShowX = Mathf.Lerp(t.ShowX, t.X, ease); t.ShowZ = Mathf.Lerp(t.ShowZ, t.Z, ease);
                float swirl = _mixP * _mixP * 4f;                                   // мешаешь — закручиваются к центру (v23.62: спокойнее)
                float cs = Mathf.Cos(swirl), sn = Mathf.Sin(swirl);
                float k01 = R * (1f - 0.85f * _mixP);
                float px = (t.ShowX * cs - t.ShowZ * sn) * k01, pz = (t.ShowX * sn + t.ShowZ * cs) * k01;
                float ang = t.Ang + swirl;
                float baseY = _surfaceOk ? _lc + lift : 0.002f;                   // v23: жидкости нет — кучка на дне
                baseY = Mathf.Clamp(baseY, 0.002f, RimH);                         // v23.2: не вылезает под дно при наклоне
                // v23.3: кучка целиком внутри стенок НА СВОЕЙ ВЫСОТЕ (ковш/ведро уже книзу — раньше край кучки торчал наружу)
                float rl = R * Mathf.Max(0.05f, t.Rn), fit = 1f;
                float yq = Mathf.Clamp(baseY + 0.002f, 0.001f, RimH);
                float c0 = Radial(rl, 0f, yq), c1 = Radial(0f, rl, yq);
                float cMax = Mathf.Max(c0, c1);
                if (cMax > 0.9f) fit = 0.9f / cMax;                               // кучка шире дна — меньше
                for (int it = 0; it < 8; it++)
                {
                    float dl = Mathf.Sqrt(px * px + pz * pz);
                    float ex = dl > 1e-5f ? (dl + rl * fit) / dl : 1f;
                    if (Radial(px * ex, pz * ex, yq) <= 0.93f) break;
                    px *= 0.82f; pz *= 0.82f;
                }
                Vector3 local = new Vector3(px, baseY, pz);
                t.Go.transform.SetPositionAndRotation(transform.position + transform.rotation * (local * S),
                                                      transform.rotation * Quaternion.Euler(0f, -ang * Mathf.Rad2Deg, 0f));
                float sc = Mathf.Pow(1f - _mixP, 0.8f) * fit;                      // и тают
                // v23.27: сахар/мука/какао на жидкости РАСТВОРЯЮТСЯ: полсекунды лежат, потом за 2,5 с тонут и исчезают
                // (мастерком их из жидкости уже не достать, кучкой не высыпаются — так решает сервер, Cookware.DissolvesIn)
                if (_surfaceOk && _liquid + _layer > 0 && t.Sub != null && !(WetBase() && _thickBase) &&   // v23.62: в меренге/тесте не тонут — лежат, пока не вмешают
                    (t.Sub.Look == SubstanceLook.Powder || t.Sub.Look == SubstanceLook.Crystals))
                {
                    float dk = Mathf.Clamp01((Time.time - t.AddT - 0.5f) / 2.5f);
                    sc *= 1f - dk * dk * (3f - 2f * dk);
                    local.y -= dk * R * Mathf.Max(0.05f, t.Rn) * 0.25f;
                    t.Go.transform.position = transform.position + transform.rotation * (local * S);
                }
                t.Go.transform.localScale = Vector3.one * sc;
                bool show = sc > 0.03f && (_surfaceOk || Butter + _liquid + _layer + _thick + _ice == 0);
                if (t.Go.activeSelf != show) t.Go.SetActive(show);
            }
        }

        private Material PowderMat
        {
            get
            {
                // v23.23: порошок в таре — тем же аниме-материалом, что кучки на мастерке/полу (мука, сахар, какао одинаково везде)
                if (_powderSub != null) { Material sm = _powderSub.GetMaterial(); if (sm != null) return sm; }
                if (FoodSubstance.AnimeHeaps) { Material am = FoodSubstance.AnimeMaterial(SubstanceLook.Powder); if (am != null) return am; }
                if (_powderMat == null && !_powderMatTried)
                {
                    _powderMatTried = true;
                    Shader sh = Shader.Find("CatCooks/Powder Heap");
                    if (sh != null) _powderMat = new Material(sh) { name = "CC Powder (auto)", hideFlags = HideFlags.DontSave };
                    else Debug.LogWarning("[Жидкость] нет шейдера «CatCooks/Powder Heap» — порошок будет как паста. Меню CatCooks → Огонь и жар → 2");
                }
                return _powderMat;
            }
        }

        /// <summary>
        /// Рельеф порошка (оси тары, м): мягкая горка, рыхлые бугорки, мелкое зерно, редкие комочки;
        /// ямки от носика/мастерка — воронка с осыпавшимся валиком.
        /// </summary>
        private static float Hash01(int x, int y, int k)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(k * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Глыбы порошка: ближайшая «клетка» (9 соседей), у каждой своя высота и радиус. 0..1.</summary>
        private static float PowderChunks(float u, float v)
        {
            int iu = Mathf.FloorToInt(u), iv = Mathf.FloorToInt(v);
            float best = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = iu + dx, cy = iv + dy;
                    float px = cx + 0.1f + 0.8f * Hash01(cx, cy, 1), py = cy + 0.1f + 0.8f * Hash01(cx, cy, 2);
                    float rad = 0.35f + 0.5f * Hash01(cx, cy, 3);
                    float d = Mathf.Sqrt((u - px) * (u - px) + (v - py) * (v - py));
                    float k = Mathf.Clamp01(1f - d / rad);
                    float hk = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.55f)) * (0.3f + 0.7f * Hash01(cx, cy, 4));
                    if (hk > best) best = hk;
                }
            return best - 0.35f;
        }

        private float PowderH(float x, float z, float rho, float t, float pa)
        {
            float R = Mathf.Max(0.05f, RadiusAtRim);
            float rel = _pasteRelief * PasteReliefK * _powderRelief * pa;
            // v17.2: дешевле — горка и крупные бугры; мелкие комочки и зерно рисует шейдер (без сетки)
            float dome = 0.55f * (1f - rho * rho);
            float lumps = (Mathf.PerlinNoise(x / (R * 0.32f) + 5.3f, z / (R * 0.32f) + 9.1f) - 0.5f) * 0.3f;
            float h = (dome + lumps - 0.2f) * rel;
            // v17.3: неровные куски — у каждой клетки своя высота и размер (рисунок высот и в сетке)
            h += PowderChunks(x / (R * 0.24f), z / (R * 0.24f)) * rel * 0.9f;
            h += rel * 0.22f * Mathf.SmoothStep(0.8f, 1f, rho);            // чуть налип у стенки
            Vector2 q = new Vector2(x, z);
            for (int i = 0; i < _dents.Length; i++)
            {
                Vector4 dn = _dents[i];
                if (dn.z <= 0f) continue;
                float dd = Vector2.Distance(q, new Vector2(dn.x, dn.y));
                if (dd > dn.z * 2.6f) continue;
                float r2 = dn.z * dn.z;
                h -= dn.w * 0.8f * Mathf.Exp(-dd * dd / (r2 * 0.45f)) * pa;       // воронка
                float ring = dd - dn.z * 1.1f;
                h += dn.w * 0.35f * Mathf.Exp(-ring * ring / (r2 * 0.18f)) * pa;  // осыпавшийся валик
            }
            return h;
        }

        public bool TryGetChurnPair(Cookware cw, out ProductDef from, out ProductDef to, out bool thick)
        {
            from = null; to = null; thick = false;
            if (_products == null || cw == null) return false;
            ProductDef liquid = null, layer = null, paste = null, floating = null, butter = null;
            foreach (Entry e in _products)
            {
                if (e.Product == null) continue;
                bool has = cw.CountOf(e.Product.Id) > 0;
                switch (e.Role)
                {
                    // взбивается в сливки только МОЛОКО (вода, смеси, бульон — нет)
                    case Role.Liquid: if (liquid == null && has && IsMilk(e.Product)) liquid = e.Product; break;
                    // слой — СЛИВКИ (если в списке есть другие TopLayer — сливки главнее)
                    case Role.TopLayer: if (layer == null || (!IsCream(layer) && IsCream(e.Product))) layer = e.Product; break;
                    // из сливок — МАСЛО: в роли Paste/Floating теперь лежат и шоколад, и паста, и ганаш
                    case Role.Paste: if (IsButter(e.Product)) { if (butter == null) butter = e.Product; } else if (paste == null) paste = e.Product; break;
                    case Role.Floating: if (IsButter(e.Product)) { if (butter == null) butter = e.Product; } else if (floating == null) floating = e.Product; break;
                }
            }
            if (liquid != null && layer != null && liquid != layer) { from = liquid; to = layer; return true; }
            ProductDef thickTo = butter != null ? butter : paste != null ? paste : floating;
            if (layer != null && thickTo != null && layer != thickTo && cw.CountOf(layer.Id) > 0)
            { from = layer; to = thickTo; thick = true; return true; }
            return false;
        }

        /// <summary>
        /// Ямка там, где зачерпнули (мастерок, ложка). В пасте остаётся след с валиком по краю,
        /// в жидкости — просто круги. Только вид.
        /// </summary>
        public void Dent(Vector3 worldPoint)
        {
            Vector3 l = Quaternion.Inverse(transform.rotation) * (worldPoint - transform.position) / Scale;
            Vector2 q = new Vector2(l.x, l.z);
            float lim = Mathf.Max(0.01f, RadiusAtRim - _dent.x * 0.6f);
            if (q.magnitude > lim) q = q.normalized * lim;
            if (Stiff < 0.3f) { Ripple(transform.position + transform.rotation * (new Vector3(q.x, _lc, q.y) * Scale)); return; }
            float r = _dent.x * UnityEngine.Random.Range(0.85f, 1.2f);
            _dents[_dentNext] = new Vector4(q.x, q.y, r, _dent.y * UnityEngine.Random.Range(0.8f, 1.15f));
            _dentNext = (_dentNext + 1) % _dents.Length;
            // v18: эффекты и звук вещества при зачерпывании (пыль у порошка, «чпок» у масла)
            FoodSubstance sub = Powder ? _powderSub : Slab ? _slabSub : null;
            if (sub != null)
            {
                float ry;
                Vector3 at = TryGetSurfaceY(worldPoint, out ry) ? new Vector3(worldPoint.x, ry, worldPoint.z) : worldPoint;
                FoodSubstanceFx.Scoop(sub, at, _pasteColor, Mathf.Max(0.05f, r * Scale * 2f));
            }
        }

        /// <summary>Круг на поверхности (сюда льётся струя). Визуал.</summary>
        public void Ripple(Vector3 worldPoint)
        {
            if (_ringCooldown > 0f) return;
            _ringCooldown = 0.12f;
            _ringData[_ringNext] = new Vector4(worldPoint.x, worldPoint.y, worldPoint.z, 0f);
            _ringNext = (_ringNext + 1) % _ringData.Length;
            // Струя толкает поверхность — лёгкая волна.
            _bounceVel -= 0.02f * _maxHeight;
            // v4: и бегущий круг от точки падения
            Vector3 l = Quaternion.Inverse(transform.rotation) * (worldPoint - transform.position) / Scale;
            EmitWave(new Vector2(l.x, l.z), 0.0018f);
        }

        /// <summary>
        /// v19.3: толчок тары снаружи (тачку тряхнуло на кочке, кренится, ударилась): волна из точки worldPoint
        /// и «подпрыгивание» центра. strength 0..1 (1 — сильный удар). Только вид, у всех игроков.
        /// </summary>
        public void Kick(Vector3 worldPoint, float strength)
        {
            if (!HasLiquid || strength <= 0f) return;
            float k = Mathf.Clamp01(strength) * (1f - Stiff);
            Vector3 l = Quaternion.Inverse(transform.rotation) * (worldPoint - transform.position) / Scale;
            EmitWave(new Vector2(l.x, l.z), 0.012f * _maxHeight * k + 0.002f * k);
            _bounceVel -= 0.12f * _maxHeight * k * (UnityEngine.Random.value > 0.5f ? 1f : -1f);
            // v19.5: сильный толчок — живые капли вверх из точки удара
            if (k > 0.25f && _surfaceOk)
            {
                float S = Scale;
                Vector3 at = transform.position + transform.rotation * (new Vector3(l.x, _lc, l.z) * S);
                int n = Mathf.Clamp(Mathf.RoundToInt(k * 7f), 1, 8);
                for (int i = 0; i < n; i++)
                {
                    Vector3 vel = Vector3.up * UnityEngine.Random.Range(0.8f, 1.8f) * (0.5f + k) + UnityEngine.Random.insideUnitSphere * 0.6f + _velF;
                    float size = _whipDropSize.y * S * UnityEngine.Random.Range(0.6f, 1.3f);
                    LiquidFx.Drop(at + UnityEngine.Random.insideUnitSphere * 0.03f * S, vel, size, LiquidColor, UnityEngine.Random.Range(0.5f, 0.9f));
                }
            }
        }

        /// <summary>v4: бегущий круг из точки (оси тары, м) силой amp (м). Только вид.</summary>
        public void EmitWave(Vector2 local, float amp)
        {
            if (_waves <= 0f || amp <= 0f) return;
            _wv[_wvNext] = new Vector4(local.x, amp, local.y, Time.time);
            _wvNext = (_wvNext + 1) % _wv.Length;
        }

        /// <summary>v4: высота бегущих волн и «сетки» от движения в точке (оси тары), м.</summary>
        private float WaveH(float x, float z, float t)
        {
            if (_waves <= 0f) return 0f;
            float lam = Mathf.Max(0.02f, _waveLength) * _lf;                    // v23.24: в большой таре волна длиннее (сетка та же)
            float spd = _waveSpeed * _lf;
            float k = 6.2831853f / lam, h = 0f;
            for (int i = 0; i < _wv.Length; i++)
            {
                Vector4 w = _wv[i];
                if (w.y <= 0f) continue;
                float a = t - w.w;
                if (a < 0f || a > 2.6f) continue;
                float dx = x - w.x, dz = z - w.z;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                float u = r - a * spd;
                float env = Mathf.Exp(-u * u / (lam * lam * 1.6f)) * Mathf.Exp(-a * 1.4f) / (1f + r * 5f);
                // отражение от стенки: тот же фронт, бегущий назад от края
                float R = Mathf.Max(0.03f, RadiusAtRim);
                float ur = (2f * R - r) - a * spd;
                float envR = a * spd > R - r ? Mathf.Exp(-ur * ur / (lam * lam * 1.6f)) * Mathf.Exp(-a * 1.9f) * 0.55f / (1f + r * 5f) : 0f;
                h += w.y * (env * Mathf.Cos(u * k) + envR * Mathf.Cos(ur * k));
            }
            if (_moveWaves > 0f && _moveSpeed > 0.04f)
            {
                // «сетка» косых волн по ходу движения — поверхность живая, а не плоский диск
                float amp = Mathf.Min(0.0032f, _moveSpeed * 0.0024f) * _moveWaves;
                float a1 = (x * _moveDirL.x + z * _moveDirL.y) * k * 0.75f - t * 5.3f;
                float a2 = (-x * _moveDirL.y + z * _moveDirL.x) * k * 0.55f + t * 2.9f;
                h += amp * (0.65f * Mathf.Sin(a1) + 0.35f * Mathf.Sin(a1 * 0.6f + a2) * Mathf.Cos(a2 * 0.8f));
            }
            return h * _waves * (_waterLike ? 1f : 1f - 0.45f * _milkBody);
        }

        /// <summary>
        /// Взбивание в точке (щуп вибратора). Вызывать каждый кадр, пока взбивают.
        /// Только вид: воронка, закрученные сливки, пена, дрожь.
        /// </summary>
        /// <summary>v19.1: мешает инструмент со своей «скоростью» эффектов (большой миксер — 0.5: тучки и пыль медленнее).</summary>
        public void Stir(Vector3 worldPoint, float strength, float fxSpeed)
        {
            _stirFxK = Mathf.Clamp(fxSpeed, 0.1f, 3f);
            _stirFxUntil = Time.time + 0.3f;
            Stir(worldPoint, strength);
        }

        private float _stirFxK = 1f, _stirFxUntil;

        public void Stir(Vector3 worldPoint, float strength = 1f)
        {
            Vector3 l = Quaternion.Inverse(transform.rotation) * (worldPoint - transform.position) / Scale;
            _stirLocal = Vector2.Lerp(_stirLocal, new Vector2(l.x, l.z), _stirAmt < 0.05f ? 1f : 0.25f);
            _stirRawLocal = new Vector2(l.x, l.z);           // v8: без сглаживания — туча взбивания стоит ровно у щупа
            _stirAmt = Mathf.Max(_stirAmt, Mathf.Clamp01(strength));
            _stirUntil = Time.time + 0.25f;
        }

        /// <summary>Пересчитать уровень сейчас (сервер без картинки не получает событий вида).</summary>
        public void ForceRefresh() { _viewHash = ViewHash(_cookware); Refresh(); }

        /// <summary>Высота поверхности (мир) над точкой; false — жидкости нет.</summary>
        public bool TryGetSurfaceY(Vector3 world, out float y)
        {
            y = 0f;
            if (!HasLiquid || !_surfaceOk) return false;
            Vector3 c = transform.position + transform.rotation * (_center * Scale);
            Vector3 n = _nDynW;
            y = c.y - (n.x * (world.x - c.x) + n.z * (world.z - c.z)) / Mathf.Max(0.05f, n.y);
            return true;
        }

        // ───────────────────────── СОДЕРЖИМОЕ ─────────────────────────

        // ── v19.4: продукты, которых нет в списке Products этой тары, подхватываются сами ──
        private static Entry[] _template;
        private readonly System.Collections.Generic.HashSet<ushort> _viscousIds = new System.Collections.Generic.HashSet<ushort>();
        private readonly System.Collections.Generic.HashSet<ushort> _batterIds = new System.Collections.Generic.HashSet<ushort>();
        private float _viscK;

        /// <summary>
        /// Список пуст (новая тара: тачка, ковш) — берём список у самой полной тары сцены.
        /// Неизвестный жидкий продукт — роль по названию: мороженое → «Мороженое», сметана → «Сметана»,
        /// масло → «Паста», остальное — «Жидкость». Тесто и яичная масса — жидкость, но ГУЩЕ (вяло плещется).
        /// </summary>
        private void EnsureAutoEntries()
        {
            if (_products == null || _products.Length == 0)
            {
                if (_template == null)
                {
                    int best = 0;
                    foreach (CookwareLiquidVisual o in FindObjectsByType<CookwareLiquidVisual>(FindObjectsSortMode.None))
                        if (o != null && o != this && o._products != null && o._products.Length > best) { best = o._products.Length; _template = o._products; }
                }
                _products = _template != null ? (Entry[])_template.Clone() : new Entry[0];
                if (_products.Length > 0) Debug.Log($"[Жидкость] «{name}»: свой список продуктов пуст — взят у другой тары ({_products.Length} шт.)", this);
            }
            if (_cookware == null) return;
            FoodDatabase db = FoodDatabase.Instance;
            for (int i = 0; i < _cookware.ViewCount; i++)
            {
                BagView v = _cookware.GetView(i);
                if (v.Count <= 0) continue;
                ProductDef d = db != null ? db.Get(v.Product) : null;
                if (d == null) continue;
                string t = string.IsNullOrEmpty(d.Title) ? "" : d.Title.ToLowerInvariant();
                if (t.Contains("тесто") || t.Contains("яичная масса") || t.Contains("смесь для морож") || t.Contains("крем"))
                    _viscousIds.Add(d.Id);
                if (t.Contains("бисквитное тесто")) _batterIds.Add(d.Id);
                bool listed = false;
                foreach (Entry e in _products) if (e.Product != null && e.Product.Id == v.Product) { listed = true; break; }
                if (listed) continue;
                bool ice = t.Contains("морожен") && !t.Contains("смесь");
                // v23.12: налитое без состояния (стенд, рецепты) — жидкость по названию, иначе тара его не рисовала
                if (!ice && FoodStateInfo.VisualOf(v.State) != FoodVisual.Liquid && !LiquidTitle(t)) continue;
                Role r = ice ? Role.IceCream : (t.Contains("сметан") || t.Contains("меренг") || t.Contains("крем")) ? Role.Thick : t.Contains("сливочное масло") ? Role.Paste : Role.Liquid;
                var list = new System.Collections.Generic.List<Entry>(_products);
                list.Add(new Entry { Product = d, Role = r, Color = new Color(0, 0, 0, 0) });
                _products = list.ToArray();
                Debug.Log($"[Жидкость] «{name}»: «{d.Title}» не было в Products — добавлен сам, роль {r}" + (_viscousIds.Contains(d.Id) ? " (густое)" : ""), this);
            }
        }

        // ───────────── v23.14: РЕЛЬЕФ ПО ВИДУ ВЕЩЕСТВА (общий для тары и стенда) ─────────────

        /// <summary>v23.15: насколько «дышит» поверхность (доля радиуса): тесто, шоколад, мёд — заметно; густое — чуть; молоко — плавно; вода — нет.</summary>
        /// <summary>
        /// v23.63: КАРАМЕЛЬ — 7 лёгких хвостов-стёков: широкие у стенки, тянутся к центру, слегка закручены, кончик — тонкий
        /// и круглый. 0..1 — насколько точка на хвосте. Та же формула, что kd 9 в шейдере Liquid Surface.
        /// </summary>
        public static float CaramelTails(float ang, float rho)
        {
            float best = 0f;
            for (int k = 0; k < 7; k++)
            {
                float e = 0.25f + 0.25f * Fr(k * 0.618f);
                if (rho < e - 0.03f || rho > 0.985f) continue;
                float sAlong = Mathf.Clamp01((1f - rho) / (1f - e));
                float ac = k * 0.8976f + 0.35f * Mathf.Sin(k * 2.1f) + 0.35f * (1f - rho) * (1f + 0.3f * Mathf.Sin(k));
                float da = ang - ac;
                da -= 6.2831853f * Mathf.Round(da / 6.2831853f);
                float d = da * rho;
                float w = 0.09f * (1f - 0.7f * sAlong) + 0.012f;
                float across = Mathf.Exp(-(d / w) * (d / w));
                float m = (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((sAlong - 0.8f) / 0.2f))) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rho - 0.95f) / 0.035f)));
                best = Mathf.Max(best, across * m);
            }
            return best;
        }

        private bool _caramelClear;

        public static float BreathFor(int kind)
        {
            switch (kind) { case 3: case 4: case 7: return 0.012f; case 9: return 0.006f; case 8: return 0.007f; case 1: case 2: case 5: return 0.005f; case 0: return 0.004f; default: return 0f; }
        }

        /// <summary>v23.43: слои шоколада (центр x,z в долях радиуса, радиус, фаза волны) — те же, что chl[] в шейдере Liquid Surface.</summary>
        private static readonly Vector4[] ChocLayers =
        {
            new Vector4(0f, 0f, 0.8f, 0f), new Vector4(-0.13f, 0.08f, 0.6f, 1.7f), new Vector4(0.15f, -0.07f, 0.47f, 3.4f),
            new Vector4(-0.05f, -0.13f, 0.33f, 5.1f), new Vector4(0.07f, 0.1f, 0.19f, 6.8f)
        };

        private static float Fr(float v) => v - Mathf.Floor(v);
        /// <summary>Тот же хэш, что hash21 в шейдере Liquid Surface — бугры модели совпадают с каплями и пиками шейдера.</summary>
        private static float SH21(float x, float y)
        {
            x = Fr(x * 123.34f); y = Fr(y * 456.21f);
            float d = x * (x + 45.32f) + y * (y + 45.32f);
            x += d; y += d;
            return Fr(x * y);
        }
        private static Vector2 SH22(float x, float y) { float n = SH21(x, y); return new Vector2(n, SH21(x + n + 17.17f, y + n + 17.17f)); }

        /// <summary>
        /// Высота рельефа (м) в точке (x, z — метры в осях тары, как uv1 шейдера), rho — доля до стенки, R — радиус.
        /// 1 сметана — складки; 2 меренга — мягкие пики; 3 тесто/яичная масса — пологие ленты;
        /// 4 шоколад/мёд — выпуклые капли (там же, где их рисует шейдер); 5 мороженое — гребни. Остальное — 0.
        /// </summary>
        public static float KindRelief(int kind, float x, float z, float rho, float R)
        {
            // v23.16: узоры в нормированных координатах круга q (|q| = доля до стенки) — те же формулы, что в шейдере,
            // поэтому контуры и блики ложатся точно на бугры. Поверх — пологие случайные бугры (их шейдер не рисует, их видно светом).
            float fade = 1f - Mathf.Pow(Mathf.Clamp01(rho), 5f);
            if (kind < 1 || kind > 9 || kind == 6 || fade <= 0f || R <= 1e-4f) return 0f;
            float len = Mathf.Sqrt(x * x + z * z);
            float ang = Mathf.Atan2(z, x);
            float qx = len > 1e-6f ? x / len * rho : 0f, qz = len > 1e-6f ? z / len * rho : 0f;
            float wq = 1.5f * Mathf.Sin(ang * 2f + rho * 3f) + 0.8f * Mathf.Sin(qx * 4.1f + qz * 3.3f);
            float u = x / R, v = z / R;
            float lfR = Mathf.Max(1f, R / 0.18f);                                          // v23.24: в большой таре капли крупнее (как в ведре)
            float n1 = Mathf.PerlinNoise(u * 2.2f + 31.7f, v * 2.2f + 5.9f) - 0.5f;
            float n2 = Mathf.PerlinNoise(u * 5.1f + 2.3f, v * 5.1f + 17.7f) - 0.5f;
            float bumps = R * (0.09f * n1 + 0.035f * n2);
            switch (kind)
            {
                case 1:                                                                    // сметана: складки
                case 3:                                                                    // тесто: v23.39 почти гладкое (льётся), лёгкие волны
                {
                    float s1 = Mathf.Sin(ang * 3f + rho * (kind == 1 ? 14.4f : 11.2f) + wq);
                    float fold = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, s1));
                    float amp = kind == 1 ? 0.1f : 0.022f;
                    float blobs = kind == 3 ? R * 0.025f * (Mathf.PerlinNoise(u * 3.6f + 8.8f, v * 3.6f + 1.4f) - 0.5f) : 0f;
                    return (R * amp * fold + blobs + bumps * (kind == 3 ? 0.4f : 1.2f)) * fade;
                }
                case 2:                                                                    // меренга: v23.51 нежная — мягкие облачка-клубы и пара мягких пик, без спиралей
                {
                    float dome = 0.14f * (1f - rho * rho);
                    float c1 = Mathf.PerlinNoise(u * 2.1f + 41.7f, v * 2.1f + 9.3f);
                    float c2 = Mathf.PerlinNoise(u * 4.3f + 3.1f, v * 4.3f + 27.9f);
                    float billow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.8f, c1)) * 0.09f + (c2 - 0.5f) * 0.03f;   // мягкие клубы
                    float dpx = qx - 0.1f, dpz = qz + 0.06f;
                    float peak = 0.2f * Mathf.Exp(-(dpx * dpx + dpz * dpz) / (0.2f * 0.2f));                // мягкая пика
                    float ex = qx + 0.28f, ez = qz - 0.2f;
                    float peak2 = 0.1f * Mathf.Exp(-(ex * ex + ez * ez) / (0.14f * 0.14f));                 // вторая, пониже
                    float tx = dpx - 0.04f, tz = dpz - 0.02f;
                    float tip = 0.06f * Mathf.Exp(-(tx * tx + tz * tz) / (0.06f * 0.06f));                  // загнутый кончик
                    float edgeK = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 0.96f, rho));
                    return (R * (dome + (billow + peak + peak2 + tip) * edgeK)) * fade;
                }
                case 4:                                                                    // шоколад: v23.43 многослойный — налитые друг на друга гладкие слои
                {
                    float h = 0.02f * (1f - rho * rho);
                    for (int l = 0; l < 5; l++)
                    {
                        Vector4 c = ChocLayers[l];
                        float dx = qx - c.x, dz = qz - c.y;
                        float aq = Mathf.Atan2(dz, dx);
                        float wk = Mathf.Min(1f, c.z * 1.8f);
                        float e = Mathf.Sqrt(dx * dx + dz * dz) - c.z - wk * (0.035f * Mathf.Sin(aq * 3f + c.w) + 0.02f * Mathf.Sin(aq * 5f + c.w * 1.3f));
                        h += 0.03f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, -0.03f, e));   // скруглённая ступенька слоя
                    }
                    float w1 = Mathf.PerlinNoise(u * 1.6f + 21.3f, v * 1.6f + 5.1f) - 0.5f;
                    return (R * (h + 0.012f * w1)) * fade;
                }
                case 5:                                                                    // мороженое: v23.36 естественный рыхлый рельеф (без круглых шариков)
                {
                    float m1 = Mathf.PerlinNoise(u * 2.4f + 12.3f, v * 2.4f + 4.7f) - 0.5f;
                    float m2 = Mathf.PerlinNoise(u * 5.2f + 3.9f, v * 5.2f + 21.1f) - 0.5f;
                    float rn = Mathf.PerlinNoise(u * 3.6f + 7.7f, v * 3.6f + 1.9f);
                    float ridge = Mathf.Pow(1f - Mathf.Abs(rn * 2f - 1f), 3f);            // стружки-завитки от ложки
                    float h = 0.06f * (1f - rho * rho) + 0.09f * m1 + 0.04f * m2 + 0.045f * ridge;
                    return (R * h + bumps * 0.4f) * fade;
                }
                case 8:                                                                    // v23.20: яичная масса — пологие бугры пены (там же, где кучки пузырьков шейдера)
                {
                    float px = x / lfR * 5f + 5.3f, pz = z / lfR * 5f + 2.1f;                // v23.21: кучек больше
                    float ix = Mathf.Floor(px), iz = Mathf.Floor(pz), fx = px - ix, fz = pz - iz;
                    float clu = 0f;
                    for (int yy = -1; yy <= 1; yy++)
                        for (int xx = -1; xx <= 1; xx++)
                        {
                            float cx = ix + xx, cz = iz + yy;
                            if (SH21(cx + 3.3f, cz + 3.3f) > 0.7f) continue;
                            Vector2 h = SH22(cx + 1.1f, cz + 1.1f);
                            float vx = xx + 0.2f + h.x * 0.6f - fx, vz = yy + 0.2f + h.y * 0.6f - fz;
                            float df = Mathf.Sqrt(vx * vx + vz * vz) / (0.3f + 0.18f * h.y);
                            clu = Mathf.Max(clu, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, df)));
                        }
                    float inner = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.2f, rho));
                    return (R * 0.02f * clu * inner + bumps * 0.6f) * fade;                  // v23.22: пузыри теперь настоящие — тут только лёгкие бугорки
                }
                case 7:                                                                    // мёд, карамель: ленты-петли
                {
                    float[] cx = { 0.05f, -0.18f, 0.2f, 0f }, cz = { 0.02f, 0.1f, -0.15f, -0.05f }, rr = { 0.38f, 0.25f, 0.22f, 0.55f };
                    float rib = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        float dx = qx - cx[k], dz = qz - cz[k];
                        float d = Mathf.Sqrt(dx * dx + dz * dz) - rr[k];
                        rib = Mathf.Max(rib, Mathf.Exp(-(d / 0.06f) * (d / 0.06f)) * (1f + 0.15f * k));
                    }
                    float dome = 0.05f * (1f - rho * rho) + 0.02f * Mathf.Sin(ang * 3f + rho * 6f);
                    return (R * (dome + 0.07f * rib) + bumps * 0.5f) * fade;
                }
                case 9:                                                                    // v23.63: карамель — гладкая, лёгкие хвосты-стёки от стенки
                {
                    float tl = CaramelTails(ang, rho);
                    return (R * (0.012f * (1f - rho * rho) + 0.03f * tl) + bumps * 0.12f) * fade;
                }
            }
            return 0f;
        }

        /// <summary>v23.12: жидкое и полужидкое по названию (для продуктов без состояния «налито»).</summary>
        private static bool LiquidTitle(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Contains("тёрт") || t.Contains("терт") || t.Contains("мука") || t.Contains("порош")) return false;
            string[] w = { "молок", "сливк", "вода", "белок", "желток", "яичн", "тесто", "масса", "морож", "меренг", "сметан", "крем",
                           "мёд", "мед", "карамел", "сироп", "шоколад", "соус", "ганаш", "глазур", "бульон", "сок" };
            foreach (string x in w) if (t.Contains(x)) return true;
            return false;
        }

        private void Refresh()
        {
            EnsureAutoEntries();
            if (_cookware == null || _products == null) return;
            _liquid = _layer = _floating = _paste = _thick = _ice = _frozenN = _spoiledN = 0;
            _powderN = 0;
            _pasteBest = 0; _baseId = 0;
            _slabN = 0;
            _slabThickN = 0;
            _slabSpoilN = 0;
            int waterBest = 0;
            int viscN = 0;
            _waterLike = false;
            // v23: яйца — белок жидкостью, желтки целыми шариками поверх (если есть другая жидкость)
            int otherLiquid = 0, whiteN = 0, glossyN = 0, batterN = 0, syrupN = 0;
            int merN = 0, foamN = 0, creamN = 0;                                // v23.10: вид складок
            int eggMassN = 0, yolkOnlyN = 0;                                    // v23.20
            int syrupThickN = 0;                                                // v23.12: мёд ролью «сметана» — без острых пиков
            int caramelN = 0, caramelSaltN = 0;                                 // v23.63: карамель (и солёная — она сливочная, плотнее)
            _yolkN = 0;
            ComputeHeapBase();
            for (int i = 0; i < _cookware.ViewCount; i++)
            {
                BagView pv = _cookware.GetView(i);
                foreach (Entry e in _products)
                {
                    if (e.Product == null || e.Product.Id != pv.Product || e.Role != Role.Liquid) continue;
                    if (!IsYolkP(e.Product, pv.State)) otherLiquid += pv.Count;
                }
            }
            for (int i = 0; i < _cookware.ViewCount; i++)
            {
                BagView v = _cookware.GetView(i);
                foreach (Entry e in _products)
                {
                    if (e.Product == null || e.Product.Id != v.Product) continue;
                    Color col = e.Color.a > 0.01f ? e.Color : e.Product.BaseColor;
                    Role role = e.Role;
                    if (HeapLike(e.Product, v.State, v.Count)) continue;                 // v23: кучкой поверх (Toppings)
                    if (TitleHas(e.Product, "меренг")) merN += v.Count;
                    if (TitleHas(e.Product, "яичная масса")) eggMassN += v.Count;
                    else if (TitleHas(e.Product, "смесь для морож")) foamN += v.Count;
                    if (TitleHas(e.Product, "сметан", "крем")) creamN += v.Count;
                    // v19.4: тесто — не «сметана», а жидкое, но гуще молока (льётся и вяло плещется)
                    if (role == Role.Thick && _batterIds.Contains(v.Product)) role = Role.Liquid;
                    switch (role)
                    {
                        case Role.Liquid:
                            if (otherLiquid > 0 && IsYolkP(e.Product, v.State)) { _yolkN += v.Count; _yolkColor = col; break; }   // v23: желтки — шариками
                            if (IsYolkP(e.Product, v.State))
                            {
                                // v23.20: одни желтки — куча шариков на тонкой прозрачной плёнке белка
                                _yolkN += v.Count; _yolkColor = col; yolkOnlyN += v.Count;
                                _liquid += Mathf.Max(1, v.Count / 3); _liquidColor = new Color(0.97f, 0.95f, 0.82f, 1f);
                                break;
                            }
                            if (IsWhiteP(e.Product, v.State)) { whiteN += v.Count; col = new Color(0.98f, 0.96f, 0.86f, 1f); }
                            if (IsGlossyLiquid(e.Product)) glossyN += v.Count;
                            // v23.39: шоколадная масса — светлее и теплее (молочный шоколад, как на фото), не чёрная
                            if (TitleHas(e.Product, "шоколадная масса", "ганаш")) { col = Color.Lerp(col, new Color(0.5f, 0.31f, 0.19f, 1f), 0.8f); col.a = 1f; }   // v23.43: молочный шоколад
                            if (TitleHas(e.Product, "карамел")) caramelN += v.Count;                                  // v23.63: карамель — свой вид
                            if (TitleHas(e.Product, "карамел") && TitleHas(e.Product, "солён", "солен")) caramelSaltN += v.Count;
                            if (TitleHas(e.Product, "мёд", "мед ", "сироп", "карамел")) { syrupN += v.Count; col = Color.Lerp(col, new Color(1f, 0.85f, 0.45f, 1f), 0.25f); col.a = 1f; }   // v23.12: мёд светлее, нежнее
                            if (IsSpongeBatter(e.Product)) { batterN += v.Count; col = Color.Lerp(col, MilkChoc, 0.9f); col.a = 1f; }
                            if (TitleHas(e.Product, "тесто для макарон")) { col = new Color(0.62f, 0.45f, 0.31f, 1f); }   // v23.61: светлое шоколадное — как в кольцах противня
                            _liquid += v.Count; _liquidColor = col;
                            if (v.Count > waterBest) { waterBest = v.Count; _waterLike = IsWater(e.Product) && e.Color.a <= 0.01f; }
                            break;
                        case Role.TopLayer: _layer += v.Count; _layerColor = col; break;
                        case Role.Floating: _floating += v.Count; _floatColor = col; if (v.Count > _pasteBest) { _pasteBest = v.Count; _pasteColor = col; _baseId = e.Product.Id; } if (IsPowder(e.Product, v.State)) _powderN += v.Count; else if (IsSlab(e.Product, v.State)) { _slabN += v.Count; if (FoodStateInfo.IsSpoiled(v.State)) _slabSpoilN += v.Count; } break;
                        case Role.Paste: _paste += v.Count; if (v.Count > _pasteBest) { _pasteBest = v.Count; _pasteColor = col; _baseId = e.Product.Id; } if (IsPowder(e.Product, v.State)) _powderN += v.Count; else if (IsSlab(e.Product, v.State)) { _slabN += v.Count; if (FoodStateInfo.IsSpoiled(v.State)) _slabSpoilN += v.Count; } break;
                        case Role.Thick:
                            if (TitleHas(e.Product, "карамел")) caramelN += v.Count;
                            if (TitleHas(e.Product, "карамел") && TitleHas(e.Product, "солён", "солен")) caramelSaltN += v.Count;
                            if (TitleHas(e.Product, "мёд", "мед ", "сироп", "карамел")) { syrupThickN += v.Count; glossyN += v.Count; col = Color.Lerp(col, new Color(1f, 0.85f, 0.45f, 1f), 0.25f); col.a = 1f; }
                            _thick += v.Count; _thickColor = col; if (v.State == FoodState.Spoiled) _spoiledN += v.Count; if (IsSlab(e.Product, v.State)) _slabThickN += v.Count; break;
                        case Role.IceCream:
                            if (v.State == FoodState.Melted) { _liquid += v.Count; _liquidColor = Color.Lerp(col, Color.white, 0.12f); viscN += v.Count; }   // v19.4: растаяло — льётся
                            else { _ice += v.Count; _iceColor = col; }
                            break;
                    }
                    if (role == Role.Liquid && _viscousIds.Contains(v.Product)) viscN += v.Count;
                    // заморожено азотом (не мороженое) — покрыто льдом, пока не оттает
                    if (v.State == FoodState.Frozen && e.Role != Role.IceCream) _frozenN += v.Count;
                }
            }
            _viscK = viscN > 0 ? 0.55f * viscN / Mathf.Max(1f, _liquid + _layer + _thick) : 0f;   // v19.4: тесто — гуще молока
            _glossyK = _liquid > 0 ? Mathf.Clamp01(glossyN / (float)_liquid) : 0f;
            _batterK = _liquid > 0 ? Mathf.Clamp01(batterN / (float)_liquid) : 0f;
            _syrupK = _liquid > 0 ? Mathf.Clamp01(syrupN / (float)_liquid) : 0f;
            _glossyK = Mathf.Max(_glossyK, _batterK * 0.45f);                   // v23.2: без жирных глазков и крапа (блик тесту выключен отдельно — v23.14)
            _eggWhiteLook = _liquid > 0 && whiteN * 2 >= _liquid;
            _yolkPile = yolkOnlyN > 0 && whiteN == 0 && _liquid <= Mathf.Max(1, yolkOnlyN / 3) + 1;
            if (_eggWhiteLook || _yolkPile)
            {
                // v23.20: сырой белок — ПРОЗРАЧНЫЙ (как в жизни), чуть желтоватый, глянцевый
                _waterLike = false;
                _glossyK = Mathf.Max(_glossyK, 0.3f);
                _liquidColor = new Color(0.96f, 0.95f, 0.8f, 1f);
            }
            _liqFrac = _liquid > 0 && _thick > 0 ? _liquid / (float)(_liquid + _thick) : 0f;
            _thickSyrup = _thick > 0 ? Mathf.Clamp01(syrupThickN / (float)_thick) : 0f;
            // v23.10: какие складки рисовать (по тому, чего больше)
            int totK = _liquid + _thick + _ice + _layer;
            _animeKind = 0;
            if (totK > 0)
            {
                if (_waterLike) _animeKind = 6;
                else if (_ice * 2 >= totK) _animeKind = 5;
                else if (merN * 2 >= totK) _animeKind = 2;
                else if (eggMassN * 2 >= totK) _animeKind = 8;                   // v23.20: яичная масса — своя
                else if ((batterN + foamN) * 2 >= totK) _animeKind = 3;
                else if (caramelN * 2 >= totK) _animeKind = 9;                    // v23.63: карамель — прозрачная, хвосты-стёки
                else if ((syrupN + syrupThickN) * 2 >= totK) _animeKind = 7;   // v23.16: мёд, карамель — свои ленты
                else if (glossyN * 2 >= totK) _animeKind = 4;
                else if ((_thick + creamN) * 2 >= totK) _animeKind = 1;
            }
            _caramelClear = _animeKind == 9 && caramelSaltN * 2 < caramelN;     // v23.63: чистая карамель — чуть прозрачная (солёная — сливочная, плотная)
            SyncYolks();
            CountSolids();
            LogSubstanceMode();
            RebuildToppings();
            _mixP = 0f;                                                          // состав сменился — смешивание заново
            // Кусочки лежат в жидкости — вытесняют её: поверхность поднимается над ними (суп, а не «вода + горка»).
            int displaced = _liquid > 0 ? _solidN : 0;
            _targetVolume = (_liquid + _layer * _creamVolume + Butter * _pasteVolume + _thick * _thickVolume + _ice * _iceVolume + displaced) * _portionVolume;
            _capacityFill = -1f;
            if (_fillByCapacity && _cookware != null && _cookware.Capacity > 0)
                _capacityFill = Mathf.Clamp01((_liquid + _layer * _creamVolume + Butter * _pasteVolume + _thick * _thickVolume + _ice * _iceVolume + displaced) / (float)_cookware.Capacity);
            _fermenting = FoodCulture.IsFermenting(_cookware);
            if (Butter <= 0 && _thick <= 0 && _ice <= 0) for (int i = 0; i < _dents.Length; i++) _dents[i] = Vector4.zero;
            SyncLumps();
            SyncBits();
        }

        /// <summary>
        /// Кусочки в таре (всё, что не жидкость этой тары и не добавка вроде соли):
        /// сколько их и каким цветом они красят жидкость.
        /// </summary>
        private void CountSolids()
        {
            _solidN = 0;
            _soupCols.Clear();
            if (!_soup || _cookware == null) return;
            FoodDatabase db = FoodDatabase.Instance;
            float r = 0f, g = 0f, b = 0f;
            for (int i = 0; i < _cookware.ViewCount; i++)
            {
                BagView v = _cookware.GetView(i);
                if (v.Count == 0 || Handles(v.Product)) continue;
                ProductDef d = db != null ? db.Get(v.Product) : null;
                if (d == null || FoodHeapLook.IsLiquid(d, v.State) || FoodUnits.IsAdditive(d, v.State)) continue;
                Color c = FoodColors.Of(d, v.State);
                _solidN += v.Count;
                r += c.r * v.Count; g += c.g * v.Count; b += c.b * v.Count;
                if (_soupCols.Count < 8) _soupCols.Add(c);
            }
            if (_solidN > 0) _soupColor = new Color(r / _solidN, g / _solidN, b / _solidN, 1f);
        }

        /// <summary>Сколько кусочков плавает: от доли кусочков в таре.</summary>
        private void SyncBits()
        {
            if (Application.isBatchMode) return;
            int want = 0;
            if (_soup && _liquid > 0 && _solidN > 0 && _soupBits > 0)
            {
                float cap = _cookware != null && _cookware.Capacity > 0 ? _cookware.Capacity : 30f;
                want = Mathf.Clamp(Mathf.RoundToInt(_soupBits * Mathf.Clamp01(_solidN / (cap * 0.3f))), Mathf.Min(4, _soupBits), _soupBits);
            }
            while (_bits.Count > want)
            {
                Lump l = _bits[_bits.Count - 1];
                if (l.T != null) Destroy(l.T.gameObject);
                _bits.RemoveAt(_bits.Count - 1);
            }
            while (_bits.Count < want)
            {
                int k = _bits.Count;
                GameObject go = new GameObject("SoupBit (auto)") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = GrainMesh(k + 1);
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = LumpMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float rnd = UnityEngine.Random.value;
                _bits.Add(new Lump
                {
                    T = go.transform, R = mr,
                    Born = Time.time,
                    Ang = UnityEngine.Random.value * Mathf.PI * 2f,
                    Rad0 = Mathf.Sqrt(UnityEngine.Random.value) * 0.85f,
                    Size = Mathf.Lerp(_soupBitSize.x, _soupBitSize.y, rnd * rnd),
                    Phase = UnityEngine.Random.value * 10f,
                    Tint = UnityEngine.Random.Range(-0.08f, 0.06f),
                    // плоские: кольца перца, ломтики
                    Squash = new Vector3(UnityEngine.Random.Range(0.9f, 1.4f), UnityEngine.Random.Range(0.3f, 0.5f), UnityEngine.Random.Range(0.8f, 1.2f)),
                });
            }
        }

        /// <summary>Кусочки плавают, кружатся при помешивании и бурлении, наполовину в жидкости.</summary>
        private void UpdateBits(float agitation)
        {
            if (_bits.Count == 0) return;
            float S = Scale, t = Time.time, dt = Time.deltaTime;
            Vector3 ls = transform.lossyScale;
            Vector3 inv = new Vector3(S / Mathf.Max(1e-5f, ls.x), S / Mathf.Max(1e-5f, ls.y), S / Mathf.Max(1e-5f, ls.z));
            float reach = _surfaceOk ? _tEdgeMin : RadiusAtRim;
            bool show = _surfaceOk && Stiff < 0.5f;
            for (int i = 0; i < _bits.Count; i++)
            {
                Lump l = _bits[i];
                if (l.T == null) continue;
                l.T.gameObject.SetActive(show);
                if (!show) continue;
                float rise = Mathf.Clamp01((t - l.Born) / 0.8f);
                l.Ang += dt * (0.12f + agitation * 2.6f + _stirAmt * 3f) * (0.6f + (l.Phase % 1f) * 0.8f);
                float wander = 0.06f + agitation * 0.12f;
                Vector2 pp = new Vector2(Mathf.Cos(l.Ang), Mathf.Sin(l.Ang)) * l.Rad0
                           + new Vector2(Mathf.Sin(t * 0.37f + l.Phase), Mathf.Cos(t * 0.29f + l.Phase * 1.7f)) * wander
                           - new Vector2(_nL.x, _nL.z) * 0.5f;
                pp = Vector2.ClampMagnitude(pp, 0.92f);
                float r = Mathf.Max(0f, reach - l.Size * 0.6f);
                Vector3 onSurf = _center + _e1 * (pp.x * r) + _e2 * (pp.y * r);
                float bob = Mathf.Sin(t * (2f + agitation * 6f) + l.Phase) * l.Size * (0.08f + agitation * 0.25f);
                // наполовину в жидкости; при бурлении то всплывают, то тонут
                Vector3 top = onSurf + _nL * (-l.Size * 0.12f + bob + _bounce * J0n(pp.magnitude));
                Vector3 bottom = onSurf - _nL * l.Size;
                l.T.localPosition = Vector3.Scale(Vector3.Lerp(bottom, top, rise), inv);
                l.T.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.9f + l.Phase) * (12f + agitation * 40f),
                                                     l.Phase * 36f + l.Ang * 30f,
                                                     Mathf.Cos(t * 0.7f + l.Phase) * (10f + agitation * 40f));
                l.T.localScale = Vector3.Scale(l.Squash * l.Size, inv);
                Color c = _soupCols.Count > 0 ? _soupCols[(int)(l.Phase * 97f) % _soupCols.Count] : _soupColor;
                c = c * (1f + l.Tint);
                c.a = 1f;
                _lumpMpb.SetColor(BaseColorId, c);
                l.R.SetPropertyBlock(_lumpMpb);
                _bits[i] = l;
            }
        }

        // ───────────────────────── ГЕОМЕТРИЯ ТАРЫ ─────────────────────────

        /// <summary>Радиус внутренней стенки на высоте h (оси тары, м).</summary>
        private float WallR(float h)
        {
            float H = _maxHeight;
            float f = Mathf.Clamp01(h / Mathf.Max(1e-4f, H));
            if (_wallProfile != null && _wallProfile.Length >= 2)
            {
                float x = f * (_wallProfile.Length - 1);
                int i = Mathf.Min((int)x, _wallProfile.Length - 2);
                return Mathf.Max(0.005f, Mathf.Lerp(_wallProfile[i], _wallProfile[i + 1], x - i));
            }
            return Mathf.Lerp(_radiusBottom, _radiusTop, f);
        }

        /// <summary>Где точка относительно стенки: ≤1 — внутри (по радиусу).</summary>
        private float Radial(float x, float z, float y)
        {
            if (_form == Form.Round)
            {
                float r = Mathf.Sqrt(x * x + z * z);
                if (_wallSides >= 3 && r > 1e-6f)
                {
                    // многогранная стенка: WallR — расстояние до середины грани; к углам — дальше
                    float seg = Mathf.PI * 2f / _wallSides;
                    float t = Mathf.Repeat(Mathf.Atan2(z, x) - _wallPhase * Mathf.Deg2Rad + seg * 0.5f, seg) - seg * 0.5f;
                    r *= Mathf.Cos(t);
                }
                return r / WallR(y);
            }
            Vector4 bp = BoxAt(y);
            float tk = TaperAt(y);
            if (tk != 0f)                                                        // v20.1: трапеция
            {
                if (_boxTaperAlongX) bp.y *= Mathf.Max(0.05f, 1f + tk * Mathf.Clamp((x - bp.z) / Mathf.Max(1e-4f, bp.x), -1f, 1f));
                else bp.x *= Mathf.Max(0.05f, 1f + tk * Mathf.Clamp((z - bp.w) / Mathf.Max(1e-4f, bp.y), -1f, 1f));
            }
            float u = Mathf.Abs(x - bp.z) / Mathf.Max(1e-4f, bp.x), w = Mathf.Abs(z - bp.w) / Mathf.Max(1e-4f, bp.y);
            // v23.2: угол острее (степень 16 вместо 8) — форма заполняется до самых углов
            float u2 = u * u, w2 = w * w, u8 = u2 * u2 * u2 * u2, w8 = w2 * w2 * w2 * w2;
            return Mathf.Pow(u8 * u8 + w8 * w8, 0.0625f);
        }

        private bool Inside(Vector3 p) => p.y >= 0f && p.y <= RimH && Radial(p.x, p.z, p.y) <= 1f;

        private float Area(float y)
        {
            if (_form == Form.Round) return Mathf.PI * WallR(y) * WallR(y);
            Vector4 b = BoxAt(y);
            return 4f * b.x * b.y * 0.978f;                     // скруглённый прямоугольник
        }

        /// <summary>v20.1: сужение прямоугольной тары по длине на высоте y (0 — нет).</summary>
        private float TaperAt(float y)
        {
            if (_boxTaper == null || _boxTaper.Length < 2) return 0f;
            float f = Mathf.Clamp01(y / Mathf.Max(1e-4f, _maxHeight)) * (_boxTaper.Length - 1);
            int i = Mathf.Min((int)f, _boxTaper.Length - 2);
            return Mathf.Clamp(Mathf.Lerp(_boxTaper[i], _boxTaper[i + 1], f - i), -0.9f, 0.9f);
        }

        private float TaperMax
        {
            get
            {
                float m = 0f;
                if (_boxTaper != null) foreach (float t in _boxTaper) m = Mathf.Max(m, Mathf.Abs(t));
                return Mathf.Min(m, 0.9f);
            }
        }

        /// <summary>Прямоугольная тара: (полуX, полуZ, центрX, центрZ) воды на высоте y. Без профиля — Half Size.</summary>
        private Vector4 BoxAt(float y)
        {
            if (_boxProfile == null || _boxProfile.Length < 2) return new Vector4(_halfSize.x, _halfSize.y, 0f, 0f);
            float f = Mathf.Clamp01(y / Mathf.Max(1e-4f, _maxHeight)) * (_boxProfile.Length - 1);
            int i = Mathf.Min((int)f, _boxProfile.Length - 2);
            Vector4 v = Vector4.Lerp(_boxProfile[i], _boxProfile[i + 1], f - i);
            v.x = Mathf.Max(0.005f, v.x); v.y = Mathf.Max(0.005f, v.y);
            return v;
        }

        /// <summary>Наибольший полуразмер воды по всей высоте (для волн и ограничения плеска).</summary>
        private float MaxHalf
        {
            get
            {
                if (_boxProfile == null || _boxProfile.Length < 2) return Mathf.Max(_halfSize.x, _halfSize.y);
                float m = 0f;
                foreach (Vector4 b in _boxProfile) m = Mathf.Max(m, Mathf.Max(b.x, b.y) * (1f + TaperMax));
                return m;
            }
        }

        /// <summary>Точка края на высоте H в направлении dir (оси тары).</summary>
        private Vector3 RimLocal(float cx, float cz, bool modelRim = false)
        {
            float H = modelRim ? _maxHeight : RimH;
            if (_form == Form.Round) { float r = WallR(H); return new Vector3(cx * r, H, cz * r); }
            if (_boxProfile != null && _boxProfile.Length >= 2)
            {
                // Центр воды смещён (ковш): ищем край лучом от оси, делением пополам.
                Vector4 bp = BoxAt(H);
                float lo = 0f, hi = (Mathf.Abs(bp.z) + Mathf.Abs(bp.w) + bp.x + bp.y) * 2f + 0.01f;
                if (Radial(0f, 0f, H) > 1f) return new Vector3(bp.z + cx * bp.x, H, bp.w + cz * bp.y);
                for (int it = 0; it < 18; it++)
                {
                    float m = (lo + hi) * 0.5f;
                    if (Radial(cx * m, cz * m, H) <= 1f) lo = m; else hi = m;
                }
                return new Vector3(cx * lo, H, cz * lo);
            }
            float k = Radial(cx, cz, H);
            k = k > 1e-5f ? 1f / k : 0f;
            return new Vector3(cx * k, H, cz * k);
        }

        private void RebuildGeometry()
        {
            _geomDirty = false;
            float H = RimH;
            _volTable[0] = 0f;
            float dy = H / TableN;
            for (int i = 1; i <= TableN; i++)
                _volTable[i] = _volTable[i - 1] + 0.5f * (Area((i - 1) * dy) + Area(i * dy)) * dy;
            // Объём до края модели → до края жидкости: полное ведро встаёт ровно на Rim Inset ниже кромки.
            float full = _volTable[TableN], top = _maxHeight - H;
            if (top > 1e-5f)
            {
                const int K = 16;
                float ddy = top / K;
                for (int i = 0; i < K; i++) full += 0.5f * (Area(H + i * ddy) + Area(H + (i + 1) * ddy)) * ddy;
            }
            _fillScale = full > 1e-9f ? _volTable[TableN] / full : 1f;

            // Колонки: где начинается внутренность (для расширяющейся кверху тары — не у самого дна).
            if (_cells == null || _cells.Length != GridN * GridN) _cells = new Vector3[GridN * GridN];
            float rx, rz;
            if (_form == Form.Round)
            {
                _rMax = 0f;
                for (int i = 0; i <= 16; i++) _rMax = Mathf.Max(_rMax, WallR(H * i / 16f));
                rx = rz = _rMax;
            }
            else if (_boxProfile != null && _boxProfile.Length >= 2)
            {
                rx = rz = 0f;
                float tm = 1f + TaperMax;
                foreach (Vector4 b in _boxProfile) { rx = Mathf.Max(rx, Mathf.Abs(b.z) + b.x * tm); rz = Mathf.Max(rz, Mathf.Abs(b.w) + b.y * tm); }
                _rMax = Mathf.Max(rx, rz);
            }
            else { rx = _halfSize.x; rz = _halfSize.y; _rMax = Mathf.Max(rx, rz); }
            float cw = 2f * rx / GridN, cd = 2f * rz / GridN;
            _cellArea = cw * cd;
            for (int iz = 0; iz < GridN; iz++)
            for (int ix = 0; ix < GridN; ix++)
            {
                float x = -rx + (ix + 0.5f) * cw, z = -rz + (iz + 0.5f) * cd;
                float ymin = -1f;
                for (int k = 0; k <= 24; k++)
                {
                    float y = H * k / 24f;
                    if (Radial(x, z, y) <= 1f) { ymin = y; break; }
                }
                _cells[iz * GridN + ix] = new Vector3(x, ymin, z);
            }
        }

        /// <summary>Объём тары «стоя» до высоты y, м³.</summary>
        private float VolumeUpTo(float y)
        {
            float H = RimH;
            if (y <= 0f) return 0f;
            if (y >= H) return _volTable[TableN];
            float x = y / H * TableN;
            int i = Mathf.Min((int)x, TableN - 1);
            return Mathf.Lerp(_volTable[i], _volTable[i + 1], x - i);
        }

        private float UprightLevel(float volume)
        {
            float H = RimH;
            if (volume <= 0f) return 0f;
            if (volume >= _volTable[TableN]) return H + (volume - _volTable[TableN]) / Mathf.Max(1e-6f, Area(H));
            float dy = H / TableN;
            for (int i = 1; i <= TableN; i++)
            {
                if (_volTable[i] < volume) continue;
                float a = _volTable[i - 1], b = _volTable[i];
                return (i - 1 + (volume - a) / Mathf.Max(1e-9f, b - a)) * dy;
            }
            return H;
        }

        /// <summary>Объём под плоскостью (через точку (0,lc,0), нормаль n — оси тары), с учётом стенок и края.</summary>
        private float VolumeUnder(float lc, Vector3 n)
        {
            float H = RimH, ny = Mathf.Max(0.06f, n.y), sum = 0f;
            for (int i = 0; i < _cells.Length; i++)
            {
                Vector3 c = _cells[i];
                if (c.y < 0f) continue;
                float top = Mathf.Min(H, lc - (n.x * c.x + n.z * c.z) / ny);
                if (top > c.y) sum += top - c.y;
            }
            return sum * _cellArea;
        }

        /// <summary>
        /// Уровень на оси с сохранением объёма: наклонили тару — жидкость
        /// перетекла к нижнему борту, лишнее выше края — выплёскивается.
        /// </summary>
        private void SolveLevel()
        {
            // По заполненности: полная тара — ровно до края для жидкости (с запасом Full Level), не выше.
            float vol = _capacityFill >= 0f
                ? _capacityFill * VolumeUpTo(RimH * _fullLevel)
                : _targetVolume * _fillScale;
            _lcUpright = UprightLevel(vol);
            if (vol > 0f) _lcUpright = Mathf.Max(_lcUpright, RimH * Mathf.Max(_minLevel, 0.055f));   // v23.2/23.5: тонкий слой — по всему дну и чуть выше дна
            if (_upLocal.y > 0.9994f || _targetVolume <= 0f) { _lc = _lcUpright; return; }
            float H = RimH;
            // Цель — объём той же сетки «стоя», чтобы на границе режимов уровень не прыгал.
            float target = VolumeUnder(Mathf.Min(_lcUpright, H), Vector3.up);
            float tan = Mathf.Sqrt(Mathf.Max(0f, 1f - _upLocal.y * _upLocal.y)) / Mathf.Max(0.06f, _upLocal.y);
            float lo = -H - _rMax * tan, hi = H + _rMax * tan + H;
            for (int it = 0; it < 14; it++)
            {
                float mid = 0.5f * (lo + hi);
                if (VolumeUnder(mid, _upLocal) < target) lo = mid; else hi = mid;
            }
            _lc = 0.5f * (lo + hi);
            if (_lcUpright > H) _lc += _lcUpright - H;              // переполнено — всё, что сверху, над краем
        }

        // ───────────────────────── КАДР ─────────────────────────

        private void LateUpdate()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 0.05f);
            if (_geomDirty || _cells == null) RebuildGeometry();

            UpdateMotion(dt);
            _upLocal = Quaternion.Inverse(transform.rotation) * Vector3.up;
            _nDynW = new Vector3(-_slope.x, 1f, -_slope.y).normalized;
            _nL = Quaternion.Inverse(transform.rotation) * _nDynW;
            SolveLevel();
            _lcStatic = _lc;
            ApplyCarryLock(dt);
            // Паста держится за тару: не растекается при наклоне.
            if (Stiff > 0.001f)
            {
                // Густое стоит по таре (не растекается при наклоне). Дрожь сметаны — отдельно (UpdateJelly).
                _nL = Vector3.Slerp(_nL, Vector3.up, Stiff).normalized;
                _lc = Mathf.Lerp(_lc, Mathf.Min(_lcUpright, RimH), Stiff);
                _nDynW = transform.rotation * _nL;
            }

            bool any = HasLiquid && _targetVolume > 0f;
            _surfaceOk = any && FindCenter();
            if (_debugLog && Time.time >= _diagAt) Diagnose(any);

            float ag = _cookware != null ? _cookware.Agitation01 : 0f;
            if (Time.time > _stirUntil) _stirAmt = Mathf.MoveTowards(_stirAmt, 0f, dt * 2.5f);
            _swirlPhase += dt * _stirAmt * 5f;
            // Взбили — пена копится; перестали — медленно оседает.
            _whipFoam = _stirAmt > 0.5f
                ? Mathf.MoveTowards(_whipFoam, _whipFoamMax, dt * 0.35f * _stirAmt)
                : Mathf.MoveTowards(_whipFoam, 0f, dt * 0.05f);

            // Сливки появляются плавно (новая порция — пятно растёт, а не щёлкает).
            int all = _liquid + _layer + Butter + _thick + _ice;
            float pasteTarget = all > 0 ? Mathf.Clamp01(Butter / (float)all * 1.1f) : 0f;
            // Сметана застывает пятнами по всей поверхности; только сметана — целиком.
            float thickTarget = all > 0 ? Mathf.Clamp01(_thick / (float)all * 1.15f) : 0f;
            if (_liquid + _layer + Butter == 0 && _thick > 0) thickTarget = 1f;
            _thickShown = Mathf.MoveTowards(_thickShown, thickTarget, dt * _thickSetSpeed * (thickTarget > _thickShown ? 1f : 2.5f));
            // Мороженое появляется быстро (азот схватывает за секунду); заморозка — быстро, оттаивание — медленно.
            float iceTarget = all > 0 ? (_liquid + _layer + Butter + _thick == 0 && _ice > 0 ? 1f : Mathf.Clamp01(_ice / (float)all * 1.15f)) : 0f;
            _iceShown = Mathf.MoveTowards(_iceShown, iceTarget, dt * (iceTarget > _iceShown ? 1.5f : 0.3f));
            float frozenTarget = all > 0 ? Mathf.Clamp01(_frozenN / (float)Mathf.Max(1, all - _ice) * 1.1f) : 0f;
            float spoiledTarget = _thick > 0 ? Mathf.Clamp01(_spoiledN / (float)_thick) : 0f;
            _spoiledShown = Mathf.MoveTowards(_spoiledShown, spoiledTarget, dt * 0.2f);
            _frozenShown = Mathf.MoveTowards(_frozenShown, frozenTarget, dt * (frozenTarget > _frozenShown ? _freezeSpeed : _thawSpeed));
            if (_liquid + _layer == 0 && Butter > 0) pasteTarget = 1f;
            _pasteShown = Mathf.MoveTowards(_pasteShown, pasteTarget, dt * _pasteGrowSpeed * (pasteTarget > _pasteShown ? 1f : 2f));
            if (Powder && pasteTarget > 0.5f) _pasteShown = pasteTarget;     // v17.4: порошок появляется сразу (без «масла» и синевы)
            if (_stirAmt > 0.3f)
            {
                // Взбивают — паста закручивается: складки-спирали остаются следом.
                _pasteSwirlC = Vector2.Lerp(_pasteSwirlC, _stirLocal, 1f - Mathf.Exp(-dt * 3f));
                _pasteSwirl += dt * _stirAmt * 1.1f;
            }
            float creamTarget = _layer <= 0 ? 0f : (_liquid <= 0 ? 1f
                : Mathf.Clamp01(0.22f + 0.78f * Mathf.Max(_layer / (float)_fullLayerAt, CreamFraction / 0.6f)));
            _creamShown = Mathf.MoveTowards(_creamShown, creamTarget, dt * _creamGrowSpeed * (creamTarget > _creamShown ? 1f : 3f));
            // Суп: чем больше кусочков, тем сильнее жидкость их цвета (плавно, как растворяется).
            float soupTarget = _soup && _liquid > 0 && _solidN > 0
                ? _soupTint * Mathf.Clamp01(_solidN / (float)(_solidN + _liquid) * 1.6f) : 0f;
            _soupShown = Mathf.MoveTowards(_soupShown, soupTarget, dt * (0.25f + ag * 0.8f + _stirAmt));
            // Сливки лёгкие — сползают к «низкой» стороне при плеске.
            Vector2 slopeLocal = new Vector2(_nL.x, _nL.z);
            _creamDrift = Vector2.Lerp(_creamDrift, slopeLocal * RadiusAtRim * 0.35f, 1f - Mathf.Exp(-dt * 2f));

            UpdateBoil(dt);
            UpdateTrailAndStirWaves(dt);
            if (_mf != null)
            {
                float dens = (_waterLike || EggClear || _caramelClear) ? 0f : _density;   // v23.63: карамель — не «плотная» (видно дно сквозь неё)
                Material want = SurfaceOverride != null ? SurfaceOverride : dens >= 0.99f ? DenseMat(SurfaceMat) : SurfaceMat;
                if (Powder && PowderMat != null) want = PowderMat;          // v17.4: порошок — сразу своим материалом
                else if (Slab)
                {
                    Material bm = _slabSub.GetMaterial();                    // v18: масло — своим материалом (лёгкий блеск)
                    if (bm != null && _pasteShown > 0.5f) want = bm;                   // v18.2: масло закрыло поверхность — свой материал (без «крапинок» жидкости)
                }
                else if (SlabThick && _thickShown > 0.5f)
                {
                    Material bm = _slabSub.GetMaterial();
                    if (bm != null) want = bm;
                }
                if (_mr.sharedMaterial != want) _mr.sharedMaterial = want;
                if (want != _animeSurfMat)
                {
                    _animeSurfMat = want;
                    _animeSurf = want != null && want.shader != null && want.shader.name == FoodSubstance.AnimeShader;
                }
                _mr.enabled = _surfaceOk;
                if (_surfaceOk && SurfaceDue()) BuildSurface(ag, dt);
                UpdateToppings(dt);
                UpdateYolks(dt);
                UpdateFoamField();                                              // v23.22

                _ringCooldown -= dt;
                for (int i = 0; i < _ringData.Length; i++)
                    if (_ringData[i].w >= 0f) { _ringData[i].w += dt; if (_ringData[i].w > 1.4f) _ringData[i].w = -1f; }

                float sloshEnergy = Mathf.Clamp01(_slope.magnitude * 2.5f + _slopeVel.magnitude * 0.15f
                                                  + Mathf.Abs(_bounceVel) * 6f);
                _mpb.SetColor(BaseColorId, Color.white);
                _mpb.SetFloat(AgitationId, Mathf.Clamp01(ag + sloshEnergy + _stirAmt * 0.5f));
                _mpb.SetVectorArray(RingsId, _ringData);
                _mpb.SetColor(CreamColorId, _layerColor);
                _mpb.SetFloat(CreamCoverId, _creamShown * (1f - _thickShown));
                _mpb.SetFloat(CreamScaleId, _creamScale);
                _mpb.SetVector(SwirlId, new Vector4(_stirLocal.x - _creamDrift.x, _stirLocal.y - _creamDrift.y,
                                                    Mathf.Max(0.03f, RadiusAtRim * 0.55f), _swirlPhase));
                _mpb.SetFloat(SwirlAmountId, _stirAmt);
                float foamK = Mathf.Clamp01(Mathf.Max(ag * _foam + _stirAmt * 0.35f, _whipFoam)) * (1f - _thickShown) * (1f - _glossyK * 0.85f);
                if (_batterK > 0.01f) foamK = Mathf.Lerp(foamK, 0.22f, _batterK);   // v23.2: тесто — редкие лёгкие пузырьки, а не белая крупа
                _mpb.SetFloat(FoamId, foamK);
                _mpb.SetFloat(MeniscusId, 1f);
                _mpb.SetFloat(DensityId, EggClear || _caramelClear ? 0f : Mathf.Max(dens, _glossyK));
                _mpb.SetFloat(SkinId, _waterLike ? 0f : _milkSkin * (1f - _creamShown * 0.6f) * (1f - Stiff) * (1f - _glossyK));
                float baseOpacity = EggClear ? 0.42f : _caramelClear ? CaramelOpacity : _waterLike ? _waterOpacity
                                  : (want != null && want.HasProperty(OpacityId) ? want.GetFloat(OpacityId) : 0.95f);
                float soupK = _soupTint > 0.001f ? Mathf.Clamp01(_soupShown / _soupTint) : 0f;
                _mpb.SetFloat(OpacityId, Mathf.Lerp(baseOpacity, Mathf.Max(baseOpacity, _soupOpacity), soupK));
                _mpb.SetFloat(PasteId, 0f);        // масло задаётся по вершинам (растёт от стенок), см. SetVertex
                // v4: жирные глазки — только на молоке/сливках (не вода, не густое)
                float fat = _waterLike || _liquid + _layer == 0 ? 0f : _fatSpots * (1f - Stiff) * (1f - _soupShown) * (1f - _glossyK);
                _mpb.SetFloat(FatId, fat);
                _mpb.SetFloat(FatScaleId, _fatScale);
                for (int i = 0; i < _trail.Length; i++) _trailSend[i] = _trail[i];
                _mpb.SetVectorArray(TrailId, _trailSend);
                _mpb.SetFloat(TrailAmountId, (_waterLike ? _stirTrails * 0.4f : _stirTrails) * ((_animeKind == 1 || _animeKind == 3) ? 0.15f : (_animeKind == 2 ? 0.12f : (_animeKind == 4 || _animeKind == 7 || _animeKind == 9) ? 0f : 0.45f)));   // v23.42: в шоколаде и мёде следа миксера нет   // v23.16: в сметане и смесях — едва заметно; v23.27: в меренге ещё тише
                // v7: молоко матовое — меньше блика, отсвета и френеля, свет площе (не «металл», не вода)
                float matte = _waterLike ? 0f : _milkMatte * (1f - _soupShown) * (1f - _glossyK);
                if (want != null)
                {
                    // v23: шоколад/карамель/мёд — шёлковый глянец: узкий яркий блик, широкий отсвет, меньше «плоского» света
                    float gk = _glossyK;
                    if (_animeKind == 3 || _animeKind == 5) { gk = 0f; matte = Mathf.Max(matte, 0.85f); }   // v23.14: тесто, яичная масса, мороженое — без блеска
                    _mpb.SetFloat(GlintStrengthId, Mathf.Lerp(Mathf.Lerp(MatF(want, GlintStrengthId, 1.3f), 0.28f, matte), 2.1f, gk) * 0.5f);   // v23.15: без «искр»
                    _mpb.SetFloat(GlintSizeId, Mathf.Lerp(Mathf.Lerp(MatF(want, GlintSizeId, 0.965f), 0.988f, matte), 0.978f, gk));
                    _mpb.SetFloat(SheenStrengthId, Mathf.Lerp(Mathf.Lerp(MatF(want, SheenStrengthId, 0.25f), 0.06f, matte), 0.6f, gk) * 0.6f);
                    _mpb.SetFloat(FresnelStrengthId, Mathf.Lerp(Mathf.Lerp(MatF(want, FresnelStrengthId, 0.35f), 0.1f, matte), 0.45f, gk));
                    _mpb.SetFloat(FlatId, Mathf.Lerp(Mathf.Lerp(MatF(want, FlatId, 0.6f), 0.85f, matte), 0.3f, gk));
                }
                ApplyAnimeLook();
                _mr.SetPropertyBlock(_mpb);

                UpdateLumps(ag);
                UpdateBits(ag);
                UpdateBubbles(ag);
                EmitWhipEpicenter(dt);
                EmitFermentBubbles(dt);
                EmitColdVapor(dt);
                if (_splashFx) UpdateSplashFx(dt);
            }
        }

        /// <summary>
        /// Плеск — две волны, как у настоящей жидкости:
        ///   • наклон (первая мода): ускорение тары наклоняет поверхность против
        ///     рывка (чай в дёрнутой чашке), дальше качается со своей частотой;
        ///   • «подпрыгивание» (осесимметричная): центр и край ходят навстречу —
        ///     от шагов и толчков.
        /// Скорость сглаживается — шаги персонажа и сеть больше не дёргают молоко.
        /// </summary>
        /// <summary>v19.5: внешний источник движения тары (тачка: гладкое движение корпуса без тряски подвески).
        /// null — своя позиция.</summary>
        public System.Func<Vector3> MotionSource;

        /// <summary>v19.5: настройки для техники (тачка): гладкое движение, волны при езде сильнее, дно не открывается.</summary>
        public void TuneForVehicle()
        {
            _motionSmoothing = Mathf.Max(_motionSmoothing, 0.1f);
            _moveWaves = Mathf.Max(_moveWaves, 2.2f);
            _sloshGain = Mathf.Max(_sloshGain, 1.3f);
            _bottomFilm = Mathf.Max(_bottomFilm, 0.025f);
            _limitToRim = true;
            _carryLockAngle = Mathf.Max(_carryLockAngle, 40f);
            _maxSloshAngle = Mathf.Min(_maxSloshAngle, 9f);      // v20.1: в широком корыте большой наклон сразу открывает дно
            if (_vehicleSurfaceTilt <= 0f) _vehicleSurfaceTilt = 2f;
            _vehicleMaxSlope = Mathf.Tan(_vehicleSurfaceTilt * Mathf.Deg2Rad);
            _bottomFilm = Mathf.Max(_bottomFilm, 0.03f);
        }

        /// <summary>v21: содержимое лежит НЕПОДВИЖНО (груз в тачке): без плеска, волн от езды и подпрыгивания;
        /// поверхность — строго по таре, пока её не опрокинули.</summary>
        public void TuneStatic()
        {
            _sloshGain = 0f;
            _moveWaves = 0f;
            _bounceGain = 0f;
            _limitToRim = true;
            _carryLockAngle = Mathf.Max(_carryLockAngle, 50f);
            _vehicleSurfaceTilt = 0.01f;
            _vehicleMaxSlope = 0.0002f;
        }

        private void UpdateMotion(float dt)
        {
            Vector3 p = MotionSource != null ? MotionSource() : transform.position;
            if (!_tracked) { _lastPos = p; _velF = _velPrevF = _accF = _accPrevF = Vector3.zero; _tracked = true; return; }
            Vector3 rawVel = (p - _lastPos) / dt;
            _lastPos = p;
            if (rawVel.sqrMagnitude > 400f) rawVel = _velF;                 // телепорт — не плескать
            float kv = _motionSmoothing > 0f ? 1f - Mathf.Exp(-dt / _motionSmoothing) : 1f;
            _velF = Vector3.Lerp(_velF, rawVel, kv);
            Vector3 rawAcc = (_velF - _velPrevF) / dt;
            _velPrevF = _velF;
            _accF = Vector3.Lerp(_accF, Vector3.ClampMagnitude(rawAcc, 40f), kv);
            Vector3 jerk = (_accF - _accPrevF) / dt;
            _accPrevF = _accF;

            float R = Mathf.Max(0.03f, RadiusAtRim);
            float depth = Mathf.Clamp(_lcUpright, 0.01f, RimH);
            float w1, z1Damp;
            if (_physicalFrequency)
            {
                float k1 = _form == Form.Round ? 1.841f / R : Mathf.PI / (2f * MaxHalf);
                w1 = Mathf.Sqrt(9.81f * k1 * (float)Math.Tanh(k1 * depth));
                z1Damp = 2f * _dampingRatio * w1;
            }
            else { w1 = Mathf.Sqrt(Mathf.Max(1f, _sloshStiffness)); z1Damp = _sloshDamping; }
            float k0 = 3.832f / R;
            float w0 = Mathf.Sqrt(9.81f * k0 * (float)Math.Tanh(k0 * depth));

            // Паста не плещется: чем гуще, тем слабее толчки и сильнее вязкость.
            // Густое не плещется (сметана дрожит по-своему — см. UpdateJelly).
            float fluid = (1f - Stiff) * (1f - Stiff);
            // v23.2: лёгкое движение тары (покачивание в руках, медленная езда ковша) не раскачивает жидкость
            float accM = _accF.magnitude;
            Vector3 accUse = accM > _motionDeadzone ? _accF * ((accM - _motionDeadzone) / accM) : Vector3.zero;
            Vector2 drive = new Vector2(-accUse.x, -accUse.z) / 9.81f * _sloshGain * fluid;
            float bDrive = -accUse.y / 9.81f * R * 0.12f * _bounceGain * fluid;
            z1Damp += Stiff * 12f;
            // v7: «тело» молока — тяжелее: слабее толчки, быстрее успокаивается
            float bodyK = _waterLike ? 0f : _milkBody;
            drive *= 1f - 0.35f * bodyK;
            bDrive *= 1f - 0.3f * bodyK;
            z1Damp += 2f * 0.1f * bodyK * w1;
            UpdateJelly(dt, jerk);
            // Толчок (шаг, остановка) — «подпрыгивание»: изменение ускорения за кадр.
            float dAcc = Mathf.Max(0f, Mathf.Min(new Vector2(jerk.x, jerk.z).magnitude * dt, 20f) - _motionDeadzone * 0.25f);
            _bounceVel += dAcc * R * 0.06f * _bounceGain * (1f - Stiff) * (Mathf.PerlinNoise(Time.time * 3.1f, 0.37f) > 0.5f ? 1f : -1f);

            // Подшаги — устойчиво при любом FPS.
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / (1f / 240f)), 1, 8);
            float h = dt / steps;
            float max = Mathf.Tan(_maxSloshAngle * Mathf.Deg2Rad);
            // Лимит по краю и дну: волна у борта не выше края, на другой стороне — не до дна.
            float room = float.MaxValue;
            if (_limitToRim && _upLocal.y > 0.8f)
            {
                float H = RimH;
                float lc = Mathf.Clamp(_lcUpright, 0f, H);
                float topRoom = Mathf.Max(0f, H - lc);
                float lowRoom = Mathf.Max(0f, lc - _bottomFilm);
                float rTop = Mathf.Max(0.02f, RadiusAtRim);
                float rLow = Mathf.Max(0.02f, _form == Form.Round ? WallR(lc) : MaxHalf);
                float sLim = Mathf.Min(topRoom / rTop, lowRoom / rLow);
                max = Mathf.Min(max, Mathf.Max(0.012f, sLim));
                room = Mathf.Min(topRoom, lowRoom);
            }
            for (int s = 0; s < steps; s++)
            {
                _slopeVel += ((drive - _slope) * (w1 * w1) - _slopeVel * z1Damp) * h;
                _slope += _slopeVel * h;
                float sm = _slope.magnitude;
                if (sm > max)
                {
                    // Упёрлись в борт: волна не лезет выше, а отскакивает — и брызги.
                    Vector2 dir = _slope / sm;
                    _slope = dir * max;
                    float vr = Vector2.Dot(_slopeVel, dir);
                    if (vr > 0f)
                    {
                        _slopeVel -= dir * vr * (1f + _rimBounce);
                        float hit = vr * R;
                        if (hit > _rimHit) { _rimHit = hit; _rimHitDir = dir; }
                    }
                }
                _bounceVel += ((bDrive - _bounce) * (w0 * w0) - _bounceVel * (2f * _dampingRatio * 1.4f * w0 + Stiff * 14f)) * h;
                _bounce += _bounceVel * h;
            }
            float bMax = R * 0.045f;
            if (room < float.MaxValue) bMax = Mathf.Min(bMax, Mathf.Max(0f, room - _slope.magnitude * R) * 0.9f);
            if (Mathf.Abs(_bounce) > bMax)
            {
                if (_bounce * _bounceVel > 0f && Mathf.Abs(_bounceVel) > _rimHit) { _rimHit = Mathf.Abs(_bounceVel); _rimHitDir = Vector2.zero; }
                _bounce = Mathf.Sign(_bounce) * bMax; _bounceVel *= -0.3f;
            }

            // v4: шаг «подбросил» молоко — от центра бежит круг.
            if (_prevBounceVel > 0f && _bounceVel <= 0f && _bounce > R * 0.004f)
                EmitWave(new Vector2(_center.x, _center.z), Mathf.Min(0.004f, _bounce * 0.6f));
            _prevBounceVel = _bounceVel;
            // v4: направление и скорость движения в осях тары — для «сетки» волн
            Vector3 vL = Quaternion.Inverse(transform.rotation) * _velF;
            Vector2 v2 = new Vector2(vL.x, vL.z);
            _moveSpeed = Mathf.Lerp(_moveSpeed, v2.magnitude, 1f - Mathf.Exp(-dt * 3f));
            if (v2.sqrMagnitude > 0.0025f) _moveDirL = Vector2.Lerp(_moveDirL, v2.normalized, 1f - Mathf.Exp(-dt * 4f)).normalized;

            // Щуп вибратора: мелкая дрожь поверхности.
            if (_stirAmt > 0.01f)
            {
                _slopeVel += UnityEngine.Random.insideUnitCircle * (_stirAmt * 0.03f);
                _bounceVel += (UnityEngine.Random.value - 0.5f) * _stirAmt * R * 0.02f;
            }
        }

        /// <summary>
        /// Тару несут/трясут: поверхность относительно тары не наклоняется сильнее, чем позволяет
        /// запас до края и до дна. Никаких голых мест и диска над краем. Упёрлась — брызги.
        /// При наклоне для выливания (больше Carry Lock Angle + 15°) — плавно отпускается в физику.
        /// </summary>
        private void ApplyCarryLock(float dt)
        {
            if (!_limitToRim || _nL.y <= 0.05f) { _prevExcess = 0f; return; }
            float tilt = Mathf.Acos(Mathf.Clamp(_upLocal.y, -1f, 1f)) * Mathf.Rad2Deg;
            float free = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_carryLockAngle, _carryLockAngle + 15f, tilt));
            if (free >= 0.999f) { _prevExcess = 0f; return; }

            float H = RimH;
            float lc = Mathf.Clamp(_lcUpright, 0f, H);
            float b = Mathf.Abs(_bounce);
            float topRoom = Mathf.Max(0f, H - lc - b);
            float lowRoom = Mathf.Max(0f, lc - _bottomFilm - b);
            float rTop = Mathf.Max(0.02f, RadiusAtRim);
            float rLow = Mathf.Max(0.02f, _form == Form.Round ? WallR(lc) : MaxHalf);
            float sMax = Mathf.Min(topRoom / rTop, lowRoom / rLow) * 0.97f;
            // v20.2: техника (тачка): поверхность держится ПО КОРЫТУ — наклон к нему не больше пары градусов.
            // Иначе в широком корыте с наклонными бортами вода уходила от борта и открывала дно.
            if (_vehicleMaxSlope > 0f) sMax = Mathf.Min(sMax, _vehicleMaxSlope);

            Vector2 sL = new Vector2(-_nL.x / _nL.y, -_nL.z / _nL.y);        // подъём на метр в осях тары
            float mag = sL.magnitude;
            float excess = mag - sMax;
            Vector3 nLock = _nL;
            if (excess > 0f && mag > 1e-5f)
            {
                Vector2 sc = sL / mag * sMax;
                nLock = new Vector3(-sc.x, 1f, -sc.y).normalized;
                // Волна «врезалась» в край быстро — брызги в ту сторону.
                float rate = (excess - _prevExcess) / dt;
                if (rate > 0.4f)
                {
                    float hit = rate * rTop * 0.35f;
                    if (hit > _rimHit)
                    {
                        _rimHit = hit;
                        Vector3 w = transform.rotation * new Vector3(sL.x, 0f, sL.y);
                        Vector2 d = new Vector2(w.x, w.z);
                        _rimHitDir = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.zero;
                    }
                }
            }
            _prevExcess = Mathf.Max(0f, excess);

            _nL = Vector3.Slerp(nLock, _nL, free).normalized;
            _lc = Mathf.Lerp(lc, _lc, free);
            _nDynW = transform.rotation * _nL;
        }

        /// <summary>Почему жидкость не видна — в консоль (только при смене причины).</summary>
        private void Diagnose(bool any)
        {
            _diagAt = Time.time + 1f;
            if (_cookware == null) { Report("нет Cookware выше по иерархии — LiquidAnchor не знает, чью жидкость рисовать"); return; }
            if (_cookware.ViewCount == 0) { Report(null); return; }
            FoodDatabase db = FoodDatabase.Instance;
            var missing = new System.Text.StringBuilder();
            for (int i = 0; i < _cookware.ViewCount; i++)
            {
                BagView v = _cookware.GetView(i);
                if (v.Count <= 0) continue;
                bool listed = false;
                if (_products != null) foreach (Entry e in _products) if (e.Product != null && e.Product.Id == v.Product) { listed = true; break; }
                if (listed) continue;
                ProductDef d = db != null ? db.Get(v.Product) : null;
                if (FoodStateInfo.VisualOf(v.State) == FoodVisual.Liquid)
                    missing.Append($"«{(d != null ? d.Title : "Id " + v.Product)}» ×{v.Count}; ");
            }
            if (missing.Length > 0) { Report($"в таре жидкое, которого НЕТ в Products этого LiquidAnchor → не рисуется: {missing}(добавь в Products, роль «Жидкость»)"); return; }
            if (!HasLiquid) { Report(null); return; }
            if (!any) { Report($"жидкость есть ({_liquid} порц.), но объём 0 — Portion Volume у LiquidAnchor = {_portionVolume}"); return; }
            if (!_surfaceOk)
            {
                Report($"жидкость есть ({_liquid + _layer + _thick} порц.), но поверхность не построилась: уровень {_lc:0.000} м из края {RimH:0.000} м, " +
                       $"форма {_form}" + (_form == Form.Box ? $", дно {BoxAt(0f)}, край {BoxAt(RimH)}" : $", радиус дна {WallR(0f):0.000}, края {WallR(RimH):0.000}") +
                       $", наклон тары {Vector3.Angle(_upLocal, Vector3.up):0}° — LiquidAnchor должен стоять на ДНЕ полости, зелёная ось вверх");
                return;
            }
            Report(null);
        }

        private void Report(string why)
        {
            if (why == _diagLast) return;
            _diagLast = why;
            if (why != null) Debug.LogWarning($"[Жидкость] «{transform.root.name}/{name}»: {why}", this);
            else if (_surfaceOk) Debug.Log($"[Жидкость] «{transform.root.name}/{name}»: рисую ({_liquid + _layer + _thick} порц., уровень {_lc:0.000} м)", this);
        }

        /// <summary>Центр поверхности внутри тары (при сильном наклоне ось может оказаться «сухой»).</summary>
        private bool FindCenter()
        {
            float H = RimH;
            Vector3 c = new Vector3(0f, Mathf.Min(_lc, H * 0.999f), 0f);
            Vector3 lowDir = new Vector3(-_upLocal.x, 0f, -_upLocal.z);
            lowDir = lowDir.sqrMagnitude > 1e-6f ? lowDir.normalized : Vector3.zero;
            float r0 = _form == Form.Round ? WallR(0f) : Mathf.Min(BoxAt(0f).x, BoxAt(0f).y);
            Vector3 plane0 = new Vector3(0f, _lc, 0f);
            // Кандидаты: ось; середина; нижний борт повыше; нижний борт у дна.
            for (int k = 0; k < 5; k++)
            {
                Vector3 q;
                switch (k)
                {
                    case 0: q = c; break;
                    case 1: q = new Vector3(0f, H * 0.5f, 0f); break;
                    case 2: q = lowDir * r0 * 0.45f + Vector3.up * H * 0.3f; break;
                    case 3: q = lowDir * r0 * 0.7f + Vector3.up * 0.01f; break;
                    default: q = lowDir * r0 * 0.9f + Vector3.up * 0.002f; break;
                }
                Vector3 onPlane = q + _nL * Vector3.Dot(plane0 - q, _nL);
                if (Inside(onPlane)) { _center = onPlane; return true; }
            }
            return false;
        }

        private float EdgeT(Vector3 c, Vector3 u, out bool wall)
        {
            float H = RimH;
            float limit = 2.2f * _rMax / Mathf.Max(0.12f, Mathf.Abs(_nL.y)) + H;
            float step = _rMax * 0.25f, t = 0f, inT = 0f, outT = -1f;
            while (t < limit)
            {
                t += step;
                if (!Inside(c + u * t)) { outT = t; break; }
                inT = t;
            }
            if (outT < 0f) { wall = true; return inT; }
            for (int i = 0; i < 8; i++)
            {
                float m = 0.5f * (inT + outT);
                if (Inside(c + u * m)) inT = m; else outT = m;
            }
            Vector3 e = c + u * inT;
            wall = e.y > 0.004f && e.y < H * 0.995f;
            return inT;
        }

        // Бессель J0 (для «подпрыгивания»), нормирован: 1 у стенки.
        private static float J0n(float rho)
        {
            float x = 3.832f * rho, x2 = x * x;
            float j = 1f - x2 / 4f + x2 * x2 / 64f - x2 * x2 * x2 / 2304f + x2 * x2 * x2 * x2 / 147456f
                      - x2 * x2 * x2 * x2 * x2 / 14745600f;
            return j / -0.4028f;
        }

        private void AllocMesh()
        {
            int vc = 1 + Dirs * (Rings + 1);
            _vtx = new Vector3[vc];
            _col = new Color32[vc];
            _uv0 = new Vector2[vc];
            _uv1 = new Vector2[vc];
            _uv2 = new Vector2[vc];
            _uv3 = new Vector2[vc];
            var tris = new List<int>(Dirs * 3 + Dirs * Rings * 6);
            for (int s = 0; s < Dirs; s++) { tris.Add(0); tris.Add(1 + (s + 1) % Dirs); tris.Add(1 + s); }
            for (int ring = 1; ring <= Rings; ring++)
            for (int s = 0; s < Dirs; s++)
            {
                int i0 = 1 + (ring - 1) * Dirs + s, i1 = 1 + (ring - 1) * Dirs + (s + 1) % Dirs;
                int i2 = 1 + ring * Dirs + s, i3 = 1 + ring * Dirs + (s + 1) % Dirs;
                tris.Add(i0); tris.Add(i1); tris.Add(i2);
                tris.Add(i1); tris.Add(i3); tris.Add(i2);
            }
            _mesh.vertices = _vtx;
            _mesh.SetTriangles(tris, 0);
        }

        private void BuildSurface(float agitation, float dt)
        {
            Vector3 c = _center, n = _nL;
            // Базис на плоскости, привязанный к осям тары (сливки не крутятся сами по себе).
            _e1 = Vector3.ProjectOnPlane(Vector3.right, n);
            if (_e1.sqrMagnitude < 1e-4f) _e1 = Vector3.ProjectOnPlane(Vector3.forward, n);
            _e1.Normalize();
            _e2 = Vector3.Cross(_e1, n);

            _tEdgeMin = float.MaxValue;
            _lf = Mathf.Max(1f, RadiusAtRim / 0.18f);                          // v23.24: большая тара — узоры крупнее, без «игл»
            UpdateDirAngles();
            for (int s = 0; s < Dirs; s++)
            {
                float a = _dirAng[s];
                Vector3 u = _e1 * Mathf.Cos(a) + _e2 * Mathf.Sin(a);
                _tEdge[s] = EdgeT(c, u, out _wallEdge[s]);
            }
            // v12.2: убрать «хвостик» — одиночное направление, где край вдруг сильно дальше соседей
            // (у неровной банки луч в угол проскакивал дальше стенки). И не дальше разумного предела.
            float capT = 1.6f * _rMax / Mathf.Max(0.3f, Mathf.Abs(_nL.y));
            for (int pass = 0; pass < 2; pass++)
            for (int s = 0; s < Dirs; s++)
            {
                float ta = _tEdge[(s + Dirs - 1) % Dirs], tb = _tEdge[(s + 1) % Dirs];
                float m = Mathf.Max(ta, tb);
                bool corner = _form == Form.Box && s % (Dirs / 4) == Dirs / 8;     // v23.2: луч в угол формы — не «хвостик», не срезаем
                if (!corner && _tEdge[s] > m * 1.12f + 0.005f) _tEdge[s] = 0.5f * (ta + tb);
                if (_tEdge[s] > capT) _tEdge[s] = capT;
            }
            for (int s = 0; s < Dirs; s++) _tEdgeMin = Mathf.Min(_tEdgeMin, _tEdge[s]);

            // Перевод «оси тары, метры» → локальные координаты объекта поверхности.
            Transform frame = _mf.transform;
            frame.localPosition = Vector3.zero;
            frame.localRotation = Quaternion.identity;
            frame.localScale = Vector3.one;
            float S = Scale;
            Vector3 ls = transform.lossyScale;
            Vector3 toLocal = new Vector3(S / Mathf.Max(1e-5f, ls.x), S / Mathf.Max(1e-5f, ls.y), S / Mathf.Max(1e-5f, ls.z));

            float t = Time.time, H = RimH;
            float wob = agitation * _surfaceWobble;
            // Сливок много — молоко под ними тоже густеет и желтеет: всё ведро становится сливками.
            float body = _layer <= 0 ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_creamBodyFrom, 1f, CreamFraction));
            Color bc = Color.Lerp(LiquidBase, _layerColor, _liquid == 0 ? 1f : body);
            Color32 baseCol = (Color32)bc;     // масло красится по вершинам — кольцом от стенок
            baseCol.a = 255;

            int vi = 0;
            SetVertex(vi++, c, 0f, n, 0f, wob, t, H, toLocal, baseCol, false);
            for (int ring = 1; ring <= Rings + 1; ring++)
            {
                bool lip = ring == Rings + 1;
                float k = Mathf.Min(1f, ring / (float)Rings);
                float rho = lip ? 1f : 1f - Mathf.Pow(1f - k, 1.6f);
                for (int s = 0; s < Dirs; s++)
                {
                    float a = _dirAng[s];
                    Vector3 u = _e1 * Mathf.Cos(a) + _e2 * Mathf.Sin(a);
                    Vector3 p = c + u * (_tEdge[s] * rho);
                    float lift = 0f;
                    if (_wallEdge[s])
                    {
                        float m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 1f, rho));
                        lift = _meniscus * m * m;
                        if (lip)
                        {
                            // заходит под стенку — без щели; у самого дна наоборот чуть отступает внутрь (v23.5: «хвостик» не вылезает из-под тары)
                            float kb = Mathf.Clamp01(_lc / Mathf.Max(1e-4f, RimH * 0.2f));
                            p += u * (_edgeOverlap * kb - RadiusAtRim * 0.035f * (1f - kb));
                        }
                    }
                    SetVertex(vi++, p, rho, n, lift, wob, t, H, toLocal, baseCol, lip);
                }
            }

            if (_animeSurf)
            {
                // v23.24: порошок/масло в таре аниме-шейдером кучек: ему в uv2 нужны «высота 0..1, радиус».
                // Раньше там лежали координаты (половина отрицательные) — шейдер принимал их за капли и стягивал вниз:
                // половина какао «лежала в дне», половина «вываливалась» из ковша.
                float rl = Mathf.Max(1e-4f, RadiusAtRim * toLocal.x);
                for (int k = 0; k < _uv1.Length; k++) _uv1[k] = new Vector2(Mathf.Clamp01(1f - _uv0[k].x) * 0.6f + 0.2f, rl);
            }
            _mesh.vertices = _vtx;
            _mesh.colors32 = _col;
            _mesh.uv = _uv0;
            _mesh.uv2 = _uv1;
            _mesh.uv3 = _uv2;                  // x — маска масла, y — сметаны
            _mesh.uv4 = _uv3;                  // x — мороженое, y — лёд (заморозка азотом)
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        private void SetVertex(int i, Vector3 p, float rho, Vector3 n, float lift, float wob, float t, float H,
                               Vector3 toLocal, Color32 col, bool lip)
        {
            // Волны вдоль нормали поверхности.
            float d = _bounce * J0n(rho) * Mathf.Clamp01(1.2f - Mathf.Abs(_nL.y - 1f) * 3f);
            float fx = p.x / _lf, fz = p.z / _lf;                               // v23.24: частоты по размеру тары
            if (wob > 0f) d += wob * Mathf.Sin(fx * 40f + t * 17f) * Mathf.Cos(fz * 37f - t * 13f) * (1f - rho * 0.5f);
            d += WaveH(p.x, p.z, t) * (1f - Stiff);                             // v4: бегущие волны
            if (_boil > 0.001f) d += BoilH(p.x, p.z, t);                        // v23: кипение — купола пузырей и «дыхание»
            if (_animeKind >= 1 && _animeKind != 6)                              // v23.14: рельеф вещества (совпадает со складками шейдера)
                d += KindRelief(_animeKind, p.x - _creamDrift.x, p.z - _creamDrift.y, rho, RadiusAtRim);
            if (_glossyK > 0.01f)
            {
                // v23: шоколад — мягкие шёлковые разводы (чуть светлее/темнее), медленно плывут
                float sw = Mathf.PerlinNoise(p.x * 7f + Mathf.Sin(p.z * 11f + t * 0.15f) * 0.6f + 2.1f, p.z * 7f + t * 0.04f + 5.3f);
                float k = 0.9f + 0.2f * Mathf.SmoothStep(0f, 1f, sw);
                Color cc = (Color)col;
                cc = Color.Lerp(cc, new Color(Mathf.Min(1f, cc.r * k), Mathf.Min(1f, cc.g * k), Mathf.Min(1f, cc.b * k), cc.a), _glossyK);
                col = (Color32)cc;
            }
            if (_stirAmt > 0.01f)
            {
                float sd = new Vector2(p.x - _stirLocal.x, p.z - _stirLocal.y).magnitude / _lf;
                // v4: ЭПИЦЕНТР — объёмный горб бурления: пузыри кипят и вспучивают поверхность у щупа
                float br = Mathf.Max(0.02f, _whipBoilRadius);
                float fall = Mathf.Exp(-sd * sd / (br * br));
                float boilA = Mathf.PerlinNoise(fx * 70f + t * 9f, fz * 70f - t * 8f) - 0.35f;
                float boilB = Mathf.PerlinNoise(fx * 140f - t * 15f, fz * 140f + t * 13f) - 0.5f;
                float soft = (_animeKind == 1 || _animeKind == 3) ? 0.25f : (_animeKind == 2 ? 0.2f : (_animeKind == 4 || _animeKind == 7 || _animeKind == 9) ? 0.08f : 0.5f);   // v23.16; v23.27: меренга — мягко; v23.42: шоколад — почти без вмятин
                d += _stirAmt * _whipBoil * fall * (0.55f + boilA + boilB * 0.7f) * soft;
                d += _stirAmt * 0.0035f * Mathf.Sin(sd * 140f - t * 38f) * Mathf.Exp(-sd / 0.06f) * soft;
                d -= _stirAmt * 0.004f * Mathf.Exp(-sd * sd / 0.0005f) * soft;      // ямка прямо у щупа
                // Кипит и вокруг: пузыри вспучивают поверхность шире.
                float boil = Mathf.PerlinNoise(fx * 60f + t * 6f, fz * 60f - t * 5f) - 0.45f;
                d += _stirAmt * 0.004f * boil * Mathf.Exp(-sd / 0.12f);
            }
            // Брожение: поверхность медленно «дышит» — закваска работает.
            if (_fermenting && _fermentHeave > 0f)
                d += _fermentHeave * (Mathf.PerlinNoise(fx * 18f + t * 0.6f, fz * 18f - t * 0.45f) - 0.5f) * (1f - _thickShown * 0.6f);
            float pm = PasteMask(p.x, p.z, rho);
            if (pm > 0.001f)
            {
                float ph = PasteH(p.x, p.z, rho, t, pm);
                // Масло нарастает у стенок и стоит выше жидкости (пока она есть) — видно, что оно «налипает».
                float proud = _pasteRelief * PasteReliefK * 0.9f * (1f - _pasteShown * 0.7f) * pm;
                d = d * (1f - pm) + ph + proud;
                float ao = 1f - Mathf.Clamp01(-ph / Mathf.Max(1e-4f, _pasteRelief * _mashAmount)) * 0.3f * pm;
                Color pasteC = PasteTint.a > 0.01f ? PasteTint : _pasteColor;
                Color pc = Color.Lerp((Color)col, pasteC, pm);
                // кромка масла чуть светлее — читается край наплыва
                float edge = pm * (1f - pm) * 4f;
                pc = Color.Lerp(pc, Color.Lerp(pasteC, Color.white, 0.35f), edge * 0.35f);

                // v19.1: рельеф видно за счёт обводок шейдера Toon Food (тёмные «пиксели» по высоте убраны — мерцали)
                if (Slab && SlabSpoil > 0.001f) pc = FoodSubstanceMesh.SpoilColor(pc, p.x * Scale, p.z * Scale, SlabSpoil);
                if (_mixP > 0.001f && (Slab || Powder)) pc = MixSwirl(pc, p.x, p.z);   // смешивание: разводы цвета результата по спирали
                col = (Color32)new Color(Mathf.Min(1f, pc.r * ao), Mathf.Min(1f, pc.g * ao), Mathf.Min(1f, pc.b * ao), 1f);
                if (lip) lift *= 1f - pm;
            }
            Vector3 jellyShear = Vector3.zero;
            float tm = ThickMask(p.x, p.z, rho);
            if (tm > 0.001f)
            {
                float th = ThickH(p.x, p.z, rho, t, tm);
                // Желе: верх наплывов качается вбок относительно дна (сдвиг), вся масса чуть приседает и подпрыгивает.
                float hold = (1f - rho * rho);                                    // у стенки прилипла — не двигается
                float lift01 = Mathf.Clamp01(0.55f + th / Mathf.Max(1e-4f, _thickRelief) * 0.45f);
                d = d * (1f - tm) + th * (1f + _jellySquash * 6f * hold) + _jellySquash * 0.02f * hold * tm;
                jellyShear = new Vector3(_jellyShear.x, 0f, _jellyShear.y) * (hold * lift01 * tm);
                // Складки чуть темнее, гребни светлее — читается густая форма.
                float fold = Mathf.Clamp01(-th / Mathf.Max(1e-4f, _thickRelief)) ;
                float crest = Mathf.Clamp01(th / Mathf.Max(1e-4f, _thickRelief));
                Color tc = Color.Lerp(_thickColor * (1f - fold * 0.12f), Color.Lerp(_thickColor, Color.white, 0.6f), crest * 0.35f);
                // край застывшего пятна — тонкий светлый бортик
                float rimT = tm * (1f - tm) * 4f;
                tc = Color.Lerp(tc, Color.white, rimT * 0.2f);
                if (_spoiledShown > 0.001f)
                {
                    // Перекисла: желтовато-серая, в ямках сыворотка.
                    Color sour = Color.Lerp(_thickColor, new Color(0.9f, 0.88f, 0.74f), 0.7f);
                    tc = Color.Lerp(tc, sour, _spoiledShown);
                    tc = Color.Lerp(tc, _wheyColor, fold * 1.6f * _spoiledShown);
                }
                if (SlabThick) tc = _thickColor;                                  // v19.1: ровный цвет, рельеф — обводками шейдера
                Color basec = (Color)col;
                if (_liqFrac > 0.02f && !SlabThick)
                {
                    // v23: густое + жидкое (яичная масса + шоколадная): мягкий мрамор лентами, а не тёмные «иглы» в складках
                    float sw = Mathf.PerlinNoise(p.x * 6f + Mathf.Sin(p.z * 9f) * 0.8f + 3.3f, p.z * 6f + Mathf.Cos(p.x * 8f) * 0.8f + 7.7f);
                    float ribbon = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.62f, sw));
                    Color mixed = Color.Lerp(_thickColor, _liquidColor, Mathf.Clamp01(_liqFrac * 0.9f));
                    tc = Color.Lerp(tc, mixed * (1f - fold * 0.08f), 0.75f);
                    tc = Color.Lerp(tc, _liquidColor * (1f - fold * 0.12f), ribbon * Mathf.Clamp01(_liqFrac * 1.4f) * 0.6f);
                    tc.a = 1f;
                    basec = Color.Lerp(basec, tc, 0.75f);
                }
                Color mixc = Color.Lerp(basec, tc, tm);
                col = (Color32)new Color(mixc.r, mixc.g, mixc.b, basec.a);
                if (lip) lift *= 1f - tm * 0.7f;
            }
            float im = IceMask(p.x, p.z, rho);
            if (im > 0.001f)
            {
                float ih = IceH(p.x, p.z, rho, im);
                d = d * (1f - im) + ih;
                float crest = Mathf.Clamp01(ih / Mathf.Max(1e-4f, _iceRelief));
                Color ic = Color.Lerp(_iceColor * 0.9f, Color.Lerp(_iceColor, Color.white, 0.35f), crest * 0.6f);
                // иней на гребнях — холодный голубоватый отлив
                ic = Color.Lerp(ic, new Color(0.86f, 0.93f, 1f), crest * 0.18f);
                Color basec = (Color)col;
                Color mixc = Color.Lerp(basec, ic, im);
                col = (Color32)new Color(mixc.r, mixc.g, mixc.b, basec.a);
                if (lip) lift *= 1f - im * 0.8f;
            }
            float fm = _frozenShown * (1f - im);
            if (fm > 0.001f)
            {
                // Лёд: волны встали, поверхность плоская с лёгкими наплывами и трещинками.
                float ice = (Mathf.PerlinNoise(fx * 30f + 4.1f, fz * 30f + 8.3f) - 0.5f) * 0.002f;
                float crack = Mathf.Pow(1f - Mathf.Abs(Mathf.PerlinNoise(fx * 14f + 1.7f, fz * 14f + 9.9f) * 2f - 1f), 12f);
                d = Mathf.Lerp(d, ice - crack * 0.0015f, fm);
                Color basec = (Color)col;
                Color fc = Color.Lerp(basec, _frostColor, 0.45f + crack * 0.25f);
                Color mixc = Color.Lerp(basec, fc, fm);
                col = (Color32)new Color(mixc.r, mixc.g, mixc.b, basec.a);
            }
            _uv3[i] = new Vector2(im, fm);
            float Rr = Mathf.Max(0.03f, RadiusAtRim);
            if (EggClear)
            {
                // v23.25: сырой белок — гладкая плёнка: никакого рельефа, только мягкие волны; у стенки без подъёма
                d = Mathf.Clamp(d, -Rr * 0.02f, Rr * 0.02f);
                lift = Mathf.Min(lift, Rr * 0.01f);
                jellyShear = Vector3.zero;
            }
            else d = Mathf.Clamp(d, -Rr * 0.3f, Rr * 0.95f);                   // v23.25: страховка от «игл» в любой таре
            Vector3 q = p + n * (d + lift) + jellyShear;
            if (!lip && q.y > H - 0.002f) q.y = H - 0.002f;
            if (lip && q.y > _maxHeight - 0.003f) q.y = _maxHeight - 0.003f;
            if (q.y < 0.0015f) q.y = 0.0015f;
            _vtx[i] = Vector3.Scale(q, toLocal);
            if (_mixP > 0.001f && _tops.Count > 0 && !(Slab || Powder))
            {
                // v23.62: кучки сыпучего вмешивают в меренгу/жидкость — основа плавно перекрашивается разводами по спирали в цвет результата
                Color c0 = (Color)col;
                Color mc = MixSwirl(c0, p.x, p.z); mc.a = c0.a;
                col = (Color32)mc;
            }
            _col[i] = col;
            _uv0[i] = new Vector2(rho, lip ? 1f : 0f);
            _uv1[i] = new Vector2(p.x - _creamDrift.x, p.z - _creamDrift.y);
            _uv2[i] = new Vector2(pm, tm);
        }

        /// <summary>
        /// Где уже сметана (0..1): застывает ПЯТНАМИ по всей поверхности (не от стенок,
        /// как масло), пятна растут и сливаются. Только сметана — вся поверхность.
        /// </summary>
        private float ThickMask(float x, float z, float rho)
        {
            float w = _thickShown;
            if (w <= 0.001f) return 0f;
            if (w >= 0.995f) return 1f;
            float k = 1f / Mathf.Max(0.02f, _thickBlob * 1.6f * _lf);
            float n = Mathf.PerlinNoise(x * k + 41.3f, z * k + 7.9f) * 0.75f
                    + Mathf.PerlinNoise(x * k * 2.7f + 3.3f, z * k * 2.7f + 19.1f) * 0.25f;
            float thr = Mathf.Lerp(0.95f, 0.05f, w);
            // у стенки застывает чуть раньше — сметана «прилипает» к борту
            thr -= Mathf.SmoothStep(0.8f, 1f, rho) * 0.12f * (1f - w);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(thr - 0.06f, thr + 0.06f, n));
        }

        /// <summary>
        /// Рельеф сметаны (оси тары, м): мягкие наплывы, гладкий купол, острые
        /// пики с загнутым кончиком (как после ложки), валик у стенки, ямки от мастерка.
        /// Без мелкого «зерна» масла — сметана гладкая и глянцевая.
        /// </summary>
        private float ThickH(float x, float z, float rho, float t, float tm)
        {
            if (SlabThick) return SlabH(x, z, rho, t, tm);                 // v18.1: масло ролью Thick — тоже пласт
            float k = 1f / Mathf.Max(0.02f, _thickBlob * _lf);                  // v23.24: в большой таре наплывы крупнее (не иглы)
            float n1 = Mathf.PerlinNoise(x * k + 5.1f, z * k + 12.7f);
            float n2 = Mathf.PerlinNoise(x * k * 1.9f + 27.3f, z * k * 1.9f + 2.2f);
            float h = (Mathf.SmoothStep(0f, 1f, n1) - 0.5f) * 0.9f + (n2 - 0.5f) * 0.35f;
            // Пики: острые гребни с наклоном в одну сторону — «загнутый кончик».
            float n3 = Mathf.PerlinNoise(x * k * 1.3f + 61.7f, z * k * 1.3f + 33.1f);
            float ridge = Mathf.Pow(1f - Mathf.Abs(n3 * 2f - 1f), 6f);
            float lean = Mathf.PerlinNoise(x * k * 1.3f + 61.9f, z * k * 1.3f + 33.3f) - n3;   // сдвиг гребня
            h *= 1f - 0.5f * _thickSyrup;                                      // v23.12: мёд — мягкие наплывы
            h += ridge * _thickPeaks * (1f - 0.9f * _thickSyrup) * (0.85f + lean * 6f);
            // Спираль от мешания (если мешали) — как сметана, размешанная ложкой.
            Vector2 d0 = new Vector2(x, z) - _pasteSwirlC;
            float rr = d0.magnitude;
            float R = Mathf.Max(0.05f, RadiusAtRim * 0.85f);
            float trail = Mathf.Clamp01(_pasteSwirl * 0.3f) * Mathf.Exp(-rr / (R * 0.9f));
            h += Mathf.Sin(rr * 120f / _lf + Mathf.Atan2(d0.y, d0.x) * 2f) * 0.25f * trail;
            // Купол — сметана собрана горкой, у стенки небольшой валик.
            h += 0.45f * (1f - rho * rho);
            h *= _thickRelief * tm;
            h += tm * _thickRelief * 0.35f * Mathf.SmoothStep(0.8f, 1f, rho);

            // Ямки от мастерка/ложки — сметана держит след.
            for (int i = 0; i < _dents.Length; i++)
            {
                Vector4 dn = _dents[i];
                if (dn.z <= 0f) continue;
                float dd = Vector2.Distance(new Vector2(x, z), new Vector2(dn.x, dn.y));
                if (dd > dn.z * 2.2f) continue;
                float r2 = dn.z * dn.z;
                h -= dn.w * Mathf.Exp(-dd * dd / (r2 * 0.6f)) * tm;
                float ring = dd - dn.z;
                h += dn.w * 0.5f * Mathf.Exp(-ring * ring / (r2 * 0.1f)) * tm;
            }
            // Перекисла: пошла крупкой (мелкие творожные комочки), между ними просела сыворотка.
            if (_spoiledShown > 0.001f)
            {
                float kc = k * 6f;
                float curd = Mathf.PerlinNoise(x * kc + 9.1f, z * kc + 2.7f);
                float curd2 = Mathf.PerlinNoise(x * kc * 2.1f + 4.4f, z * kc * 2.1f + 7.7f);
                float bumps = Mathf.SmoothStep(0.35f, 0.75f, curd) * 0.7f + curd2 * 0.3f - 0.45f;
                h = Mathf.Lerp(h, h * 0.45f + bumps * _thickRelief * 0.6f * tm, _spoiledShown);
            }
            // Брожение: редкие медленные вздутия на ещё мягкой сметане.
            if (_fermenting)
                h += _fermentHeave * 1.5f * tm * (Mathf.PerlinNoise(x / _lf * 25f + t * 0.5f, z / _lf * 25f - t * 0.4f) - 0.4f) * (1f - _thickShown);
            return h;
        }

        /// <summary>Мороженое появляется сразу по всей поверхности, край — неровный.</summary>
        private float IceMask(float x, float z, float rho)
        {
            float w = _iceShown;
            if (w <= 0.001f) return 0f;
            if (w >= 0.995f) return 1f;
            float n = Mathf.PerlinNoise(x / _lf * 9f + 71.3f, z / _lf * 9f + 13.7f);
            float thr = Mathf.Lerp(1.05f, -0.05f, w);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(thr - 0.08f, thr + 0.08f, n));
        }

        /// <summary>
        /// Рельеф мороженого (оси тары, м): несколько круглых «шариков» (как после
        /// ложки-скупа), по каждому — дуги-бороздки от ложки с завитком на краю;
        /// мелкие ямки-дырочки и шероховатость дорисовывает шейдер.
        /// </summary>
        private float IceH(float x, float z, float rho, float im)
        {
            // v23.34: шарики мороженого — ТЕ ЖЕ формулы, что рисует аниме-шейдер (kd 5): круглые купола со скруглённой
            // губой-завитком от ложки. Мелкие «кубики» шума убраны: следы ложки и точки ванили рисует шейдер.
            float R = Mathf.Max(0.05f, RadiusAtRim);
            Vector2 q = new Vector2(x, z);
            float h = KindRelief(5, x, z, rho, R);
            h += Mathf.SmoothStep(0.85f, 1f, rho) * R * 0.03f;                   // у стенки налипает валиком
            h *= im;
            // ямки от мастерка
            for (int i = 0; i < _dents.Length; i++)
            {
                Vector4 dn = _dents[i];
                if (dn.z <= 0f) continue;
                float dd = Vector2.Distance(q, new Vector2(dn.x, dn.y));
                if (dd > dn.z * 2.2f) continue;
                float r2 = dn.z * dn.z;
                h -= dn.w * 1.2f * Mathf.Exp(-dd * dd / (r2 * 0.6f)) * im;
                float ring = dd - dn.z;
                h += dn.w * 0.6f * Mathf.Exp(-ring * ring / (r2 * 0.1f)) * im;
            }
            return h;
        }

        private float _vaporAcc;

        /// <summary>
        /// Морозное «дыхание»: от холодного мороженого и льда поднимаются и тут же
        /// оседают по краю лёгкие клубы холодного пара (у каждого игрока свои, без сети).
        /// </summary>
        private float _flakeAcc, _twinkleAcc;

        /// <summary>v19.4: над мороженым и льдом — мультяшный холод: искорки инея на поверхности
        /// и редкие снежинки, которые поднимаются, кружатся и тают.</summary>
        private void EmitFrostSparkles(float dt, float cold)
        {
            if (!_surfaceOk || cold < 0.3f) { _flakeAcc = _twinkleAcc = 0f; return; }
            float S = Scale, R = Mathf.Max(0.05f, RadiusAtRim);
            _twinkleAcc += dt * 6f * cold;
            while (_twinkleAcc >= 1f)
            {
                _twinkleAcc -= 1f;
                Vector2 rnd = UnityEngine.Random.insideUnitCircle * _tEdgeMin * 0.9f;
                Vector3 local = _center + _e1 * rnd.x + _e2 * rnd.y;
                if (!Inside(local)) continue;
                Vector3 at = transform.position + transform.rotation * (local * S) + _nDynW * 0.006f * S;
                CCFrostFx.Twinkle(at, R * S * UnityEngine.Random.Range(0.05f, 0.1f));
            }
            _flakeAcc += dt * 2.5f * cold;
            while (_flakeAcc >= 1f)
            {
                _flakeAcc -= 1f;
                Vector2 rnd = UnityEngine.Random.insideUnitCircle * _tEdgeMin * 0.8f;
                Vector3 local = _center + _e1 * rnd.x + _e2 * rnd.y;
                Vector3 at = transform.position + transform.rotation * (local * S) + Vector3.up * 0.02f * S;
                Vector3 vel = Vector3.up * UnityEngine.Random.Range(0.04f, 0.12f) + UnityEngine.Random.insideUnitSphere * 0.04f;
                CCFrostFx.Flake(at, vel, R * S * UnityEngine.Random.Range(0.06f, 0.13f), UnityEngine.Random.Range(1.2f, 2.2f),
                                Color.Lerp(CCFrostFx.Ice, Color.white, UnityEngine.Random.value));
            }
        }

        private void EmitColdVapor(float dt)
        {
            float cold = Mathf.Max(_iceShown, _frozenShown);
            EmitFrostSparkles(dt, cold);
            if (!_coldVapor || !_surfaceOk || cold < 0.4f || _coldVaporRate <= 0f) { _vaporAcc = 0f; return; }
            // v23.34: МОРОЗНЫЙ ПАР — заметнее: клубы крупнее (по размеру тары), чаще; часть переваливает через край и стекает вниз
            _vaporAcc += dt * _coldVaporRate * 2.4f * cold * UnityEngine.Random.Range(0.6f, 1.4f);
            int nv = 0;
            while (_vaporAcc >= 1f && nv < 3)
            {
                _vaporAcc -= 1f; nv++;
                EmitOneVapor(cold);
            }
        }

        private void EmitOneVapor(float cold)
        {
            float S = Scale, Rv = Mathf.Max(0.05f, RadiusAtRim);
            if (UnityEngine.Random.value < 0.3f)
            {
                // у края: холодный воздух тяжелее — переваливает через бортик и стекает вниз
                float ang = UnityEngine.Random.value * 6.2832f;
                Vector3 dirL = _e1 * Mathf.Cos(ang) + _e2 * Mathf.Sin(ang);
                Vector3 rimL = _center + dirL * (_tEdgeMin * 0.92f);
                Vector3 rimW = transform.position + transform.rotation * (rimL * S) + Vector3.up * 0.02f * S;
                Vector3 outW = (transform.rotation * dirL).normalized;
                Vector3 velR = outW * UnityEngine.Random.Range(0.05f, 0.1f) + Vector3.down * UnityEngine.Random.Range(0.03f, 0.08f);
                Color cr = Color.Lerp(CCFreezeFx.IceBlue, CCFreezeFx.IceWhite, UnityEngine.Random.Range(0.6f, 1f)); cr.a = 0.32f;
                CCFreezeFx.Mist(rimW, velR, Rv * S * UnityEngine.Random.Range(0.18f, 0.3f), UnityEngine.Random.Range(1.6f, 2.6f), cr);
                return;
            }
            EmitSurfaceVapor(S, Rv);
        }

        private void EmitSurfaceVapor(float S, float Rv)
        {
            Vector2 rnd = UnityEngine.Random.insideUnitCircle * _tEdgeMin * 0.8f;
            Vector3 local = _center + _e1 * rnd.x + _e2 * rnd.y;
            if (!Inside(local)) return;
            Vector3 at = transform.position + transform.rotation * (local * S) + Vector3.up * 0.03f * S;
            // холодный воздух: чуть вверх и в сторону, потом сползает вниз через край
            Vector3 vel = new Vector3(UnityEngine.Random.Range(-0.12f, 0.12f), UnityEngine.Random.Range(0.02f, 0.1f),
                                      UnityEngine.Random.Range(-0.12f, 0.12f));
            vel = new Vector3(vel.x * 0.5f, UnityEngine.Random.Range(0.03f, 0.08f), vel.z * 0.5f);   // v23.34: медленно поднимается
            Color c = Color.Lerp(CCFreezeFx.IceBlue, CCFreezeFx.IceWhite, UnityEngine.Random.Range(0.6f, 1f)); c.a = 0.3f;
            CCFreezeFx.Mist(at, vel, Rv * S * UnityEngine.Random.Range(0.14f, 0.26f), UnityEngine.Random.Range(1.8f, 3f), c);   // мягкий клуб, не «кубики»
        }

        /// <summary>Закваска бродит — редкие пузыри поднимаются и лопаются («буль»).</summary>
        private void EmitFermentBubbles(float dt)
        {
            if (!_fermenting || !_surfaceOk || _fermentBubbles <= 0f) { _fermentBubbleAcc = 0f; return; }
            _fermentBubbleAcc += dt * _fermentBubbles * (1f - _thickShown * 0.6f) * UnityEngine.Random.Range(0.4f, 1.6f);
            int n = Mathf.Min(4, (int)_fermentBubbleAcc);
            if (n <= 0) return;
            _fermentBubbleAcc -= n;
            float S = Scale;
            Color c = Color.Lerp(LiquidColor, Color.white, 0.3f);
            for (int i = 0; i < n; i++)
            {
                Vector2 rnd = UnityEngine.Random.insideUnitCircle * _tEdgeMin * 0.85f;
                Vector3 local = _center + _e1 * rnd.x + _e2 * rnd.y;
                if (!Inside(local)) continue;
                Vector3 at = transform.position + transform.rotation * (local * S);
                float size = _whipBubbleSize * S * UnityEngine.Random.Range(0.25f, 0.7f);
                LiquidFx.Bubble(at, _nDynW, size, c, _velF);
            }
        }

        /// <summary>
        /// Где уже масло (0..1): сначала тонким кольцом у стенок, потом наплыв к центру.
        /// Край неровный — «языки» масла.
        /// </summary>
        private float PasteMask(float x, float z, float rho)
        {
            float w = _pasteShown;
            if (w <= 0.001f) return 0f;
            if (w >= 0.995f) return 1f;
            float ang = Mathf.Atan2(z, x);
            float n = Mathf.PerlinNoise(Mathf.Cos(ang) * 2.2f + 5.3f, Mathf.Sin(ang) * 2.2f + 1.7f)
                    + 0.35f * Mathf.PerlinNoise(x * 30f + 3f, z * 30f + 8f);
            float edge = 1f - w * 1.08f + (n - 0.65f) * 0.22f * (1f - w);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - 0.07f, edge + 0.05f, rho));
        }

        // ───────────────────────── ВЫПЛЁСКИВАНИЕ ─────────────────────────

        /// <summary>
        /// Насколько поверхность поднялась над самой низкой точкой края, м (мир, по вертикали).
        /// &gt;0 — льётся. dynamic=false — без плеска (для «льём наклоном»),
        /// dynamic=true — с плеском и волнами (для брызг при ходьбе).
        /// rimPoint/outward — где льётся (сглажено между соседними точками края — струя не прыгает).
        /// </summary>
        public float ComputeOverflow(bool dynamic, out Vector3 rimPoint, out Vector3 outward)
        {
            Transform a = transform;
            rimPoint = a.position + a.up * RimHeight;
            outward = a.forward;
            if (!HasLiquid || _targetVolume <= 0f) return -1f;
            if (_geomDirty || _cells == null) RebuildGeometry();

            float S = Scale;
            Quaternion rot = a.rotation;
            Vector3 pos = a.position;
            Vector3 nW = dynamic ? _nDynW : Vector3.up;
            Vector3 cW = pos + rot * (new Vector3(0f, dynamic ? _lc : _lcStatic, 0f) * S);
            float bRim = dynamic ? _bounce * S : 0f;

            const int N = 32;
            float best = float.MinValue;
            Span<float> over = stackalloc float[N];
            for (int i = 0; i < N; i++)
            {
                float ang = i / (float)N * Mathf.PI * 2f;
                Vector3 rimW = pos + rot * (RimLocal(Mathf.Cos(ang), Mathf.Sin(ang)) * S);
                over[i] = Vector3.Dot(nW, cW - rimW) / Mathf.Max(0.05f, nW.y) + bRim;
                if (over[i] > best) best = over[i];
            }
            // Мягкий максимум: точка края — взвешенная между соседями, без скачков.
            float sigma = 0.015f * RimHeight + 1e-4f, wsum = 0f;
            Vector3 rp = Vector3.zero, od = Vector3.zero;
            for (int i = 0; i < N; i++)
            {
                float w = Mathf.Exp((over[i] - best) / sigma);
                if (w < 0.01f) continue;
                float ang = i / (float)N * Mathf.PI * 2f;
                float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                rp += (pos + rot * (RimLocal(cx, cz, true) * S)) * w;
                od += rot * new Vector3(cx, 0f, cz) * w;
                wsum += w;
            }
            if (wsum > 0f)
            {
                rimPoint = rp / wsum;
                outward = od.sqrMagnitude > 1e-8f ? od.normalized : a.forward;
            }
            return best;
        }

        /// <summary>
        /// Рельеф пасты (оси тары, м): комки со складками, спирали от взбивания, мазки у стенки,
        /// ямки от мастерка с валиком по краю; у щупа пучится.
        /// </summary>
        private float PasteH(float x, float z, float rho, float t, float pa)
        {
            if (Powder) return PowderH(x, z, rho, t, pa);
            if (Slab) return SlabH(x, z, rho, t, pa);                      // v18: пласт масла с мазками
            Vector2 q = new Vector2(x, z);
            // Закрутка вокруг места, где взбивали, — складки спиралью.
            Vector2 d0 = q - _pasteSwirlC;
            float R = Mathf.Max(0.05f, RadiusAtRim * 0.85f);
            float ang = _pasteSwirl * Mathf.Exp(-d0.sqrMagnitude / (R * R)) * 1.6f;
            float sn = Mathf.Sin(ang), cs = Mathf.Cos(ang);
            Vector2 w = _pasteSwirlC + new Vector2(cs * d0.x - sn * d0.y, sn * d0.x + cs * d0.y);

            float k = 1f / Mathf.Max(0.01f, _pasteLumpSize);
            float n1 = Mathf.PerlinNoise(w.x * k + 13.7f, w.y * k + 3.1f);
            float n2 = Mathf.PerlinNoise(w.x * k * 2.3f + 7.7f, w.y * k * 2.3f + 21.4f);
            float n3 = Mathf.PerlinNoise(w.x * k * 5.1f + 1.3f, w.y * k * 5.1f + 9.2f);
            float blob = Mathf.SmoothStep(0f, 1f, Mathf.SmoothStep(0f, 1f, n1));      // круглые комки
            float crease = 1f - Mathf.Abs(n2 * 2f - 1f);                               // острые складки
            float h = (blob - 0.5f) * 1.1f + (crease - 0.55f) * 0.5f + (n3 - 0.5f) * 0.28f;
            // «Пюре»: острые пики и загнутые гребни, как у взбитого масла
            float n4 = Mathf.PerlinNoise(w.x * k * 1.7f + 31.3f, w.y * k * 1.7f + 17.9f);
            float peak = Mathf.Pow(1f - Mathf.Abs(n4 * 2f - 1f), 5f);
            h += peak * 0.75f - 0.12f;
            // Следы венчика: спиральные бороздки вокруг места, где взбивали
            float rr = d0.magnitude;
            float trail = Mathf.Clamp01(_pasteSwirl * 0.35f) * Mathf.Exp(-rr / (R * 0.9f));
            h += Mathf.Sin(rr * 260f + Mathf.Atan2(d0.y, d0.x) * 3f + _pasteSwirl * 2f) * 0.22f * trail;
            // Лёгкий купол — масло собрано горкой, а не блином
            h += 0.35f * (1f - rho * rho);
            h *= _pasteRelief * _mashAmount * pa * PasteReliefK;
            // Паста лезет на стенку — мазки выше у края.
            h += pa * _pasteRelief * PasteReliefK * 0.55f * Mathf.SmoothStep(0.72f, 1f, rho) * (0.6f + n2 * 0.8f);

            // Ямки от мастерка: впадина + выдавленный валик.
            for (int i = 0; i < _dents.Length; i++)
            {
                Vector4 dn = _dents[i];
                if (dn.z <= 0f) continue;
                float dd = Vector2.Distance(q, new Vector2(dn.x, dn.y));
                if (dd > dn.z * 2.2f) continue;
                float r2 = dn.z * dn.z;
                h -= dn.w * Mathf.Exp(-dd * dd / (r2 * 0.55f)) * pa;
                float ring = dd - dn.z;
                h += dn.w * 0.45f * Mathf.Exp(-ring * ring / (r2 * 0.08f)) * pa;
            }

            // Взбивают — пучится у щупа (медленные вздутия, не рябь).
            if (_stirAmt > 0.01f)
            {
                float sd = Vector2.Distance(q, _stirLocal);
                float heave = Mathf.PerlinNoise(x * 22f + t * 1.7f, z * 22f - t * 1.3f) - 0.35f;
                h += _stirAmt * _pasteHeave * pa * heave * Mathf.Exp(-sd / 0.12f);
                h -= _stirAmt * _pasteHeave * 0.6f * pa * Mathf.Exp(-sd * sd / 0.0015f);   // щуп продавливает
            }
            return h;
        }

        // ───────────────────────── БРЫЗГИ ─────────────────────────

        /// <summary>Гребень ударил в борт у края — вспышка и корона капель. Только вид, у каждого свой.</summary>
        private void UpdateSplashFx(float dt)
        {
            _splashT -= dt;
            if (!_surfaceOk) { _lastOver = -1f; return; }
            if (_upLocal.y < 0.8f) { _lastOver = -1f; return; }       // тару наклонили лить — там струя
            if (Stiff > 0.6f) { _lastOver = -1f; _rimHit = 0f; return; }   // паста и сметана не брызжут
            if (_lid != null && _lid.HasLid) { _lastOver = -1f; return; }  // крышка держит
            float over = ComputeOverflow(true, out Vector3 rim, out Vector3 outward);
            // Волна ударила в борт (лимит плеска) — брызги в сторону удара.
            float hitNow = _rimHit;
            Vector2 hitDir = _rimHitDir;
            _rimHit = 0f;
            if (hitNow > 0.015f && hitDir.sqrMagnitude > 0.5f)
            {
                // v4: волна ударила в борт — от этого места бежит круг обратно
                Vector3 hl = Quaternion.Inverse(transform.rotation) * new Vector3(hitDir.x, 0f, hitDir.y);
                Vector2 h2 = new Vector2(hl.x, hl.z);
                if (h2.sqrMagnitude > 1e-6f) EmitWave(h2.normalized * RadiusAtRim * 0.92f, Mathf.Min(0.006f, hitNow * 0.03f));
            }
            if (hitNow > 0.02f && _splashT <= 0f)
            {
                float pw = Mathf.Clamp01(hitNow * 5f) * _splashPower;
                if (pw > 0.05f)
                {
                    Vector3 at2 = rim, out2 = outward;
                    if (hitDir.sqrMagnitude > 0.5f)
                    {
                        // Точка края в направлении подъёма волны.
                        Vector3 wd = new Vector3(hitDir.x, 0f, hitDir.y);
                        Vector3 ld = Quaternion.Inverse(transform.rotation) * wd;
                        Vector2 l2 = new Vector2(ld.x, ld.z);
                        if (l2.sqrMagnitude > 1e-6f)
                        {
                            l2.Normalize();
                            at2 = transform.position + transform.rotation * (RimLocal(l2.x, l2.y, true) * Scale);
                            out2 = transform.rotation * new Vector3(l2.x, 0f, l2.y);
                        }
                    }
                    _splashT = _splashCooldown * Mathf.Lerp(1.4f, 0.7f, pw);
                    LiquidFx.SloshSplash(at2 - out2 * 0.012f * Scale, out2, transform.up, LiquidColor, pw, _velF);
                    _lastOver = over;
                    return;
                }
            }
            float v = (over - _lastOver) / dt;
            _lastOver = over;
            _overVel = Mathf.Lerp(_overVel, v, 0.5f);
            float clearance = -over;
            if (_splashT > 0f || clearance > _sprayClearance * Scale || _overVel < 0.05f) return;

            float power = Mathf.Clamp01(_overVel * 1.6f + Mathf.Max(0f, over) * 25f) * _splashPower;
            if (power < 0.05f) return;
            _splashT = _splashCooldown * Mathf.Lerp(1.4f, 0.7f, power);
            Vector3 at = rim - outward * 0.012f * Scale;
            LiquidFx.SloshSplash(at, outward, transform.up, LiquidColor, power, _velF);
        }

        // ───────────────────────── МАСЛО ─────────────────────────

        /// <summary>4 неровных «зерна» масла (одна и та же сфера смотрится как бусы — нужно разнообразие).</summary>
        private static Mesh GrainMesh(int variant)
        {
            if (_grainMeshes == null) _grainMeshes = new Mesh[4];
            variant = ((variant % 4) + 4) % 4;
            if (_grainMeshes[variant] != null) return _grainMeshes[variant];
            const int U = 12, V = 8;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float seed = variant * 7.3f + 1.1f;
            for (int j = 0; j <= V; j++)
            {
                float th = j / (float)V * Mathf.PI;
                for (int i = 0; i <= U; i++)
                {
                    float ph = i / (float)U * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    // бугры + сплющенный низ (зерно «сидит» на поверхности)
                    float bump = Mathf.PerlinNoise(d.x * 1.7f + seed, d.z * 1.7f + d.y * 1.3f + seed) - 0.5f;
                    float r = 0.5f * (1f + bump * 0.55f);
                    Vector3 v = d * r;
                    if (v.y < -0.18f) v.y = -0.18f - (v.y + 0.18f) * 0.3f;
                    if (i == U) v = verts[verts.Count - U];      // шов
                    verts.Add(v);
                }
            }
            for (int j = 0; j < V; j++)
            for (int i = 0; i < U; i++)
            {
                int a0 = j * (U + 1) + i, a1 = a0 + 1, b0 = a0 + U + 1, b1 = b0 + 1;
                tris.Add(a0); tris.Add(b0); tris.Add(a1);
                tris.Add(a1); tris.Add(b0); tris.Add(b1);
            }
            var m = new Mesh { name = "CC ButterGrain " + variant, hideFlags = HideFlags.DontSave };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            _grainMeshes[variant] = m;
            return m;
        }

        /// <summary>
        /// Зёрна масла: пока есть сливки, их тем больше, чем больше масла; всё сливки кончились —
        /// зёрна растворяются в пасту (она уже нарастает от стенок).
        /// </summary>
        private void SyncLumps()
        {
            if (Application.isBatchMode) return;
            int fluid = _liquid + _layer;
            int all = fluid + Butter;
            int want = 0;
            if (Butter > 0 && fluid > 0)
            {
                float f = Butter / (float)all;
                want = Mathf.Clamp(Mathf.RoundToInt(_grainCount * Mathf.Min(1f, f * 2.2f)), 5, Mathf.Max(5, _grainCount));
            }
            while (_lumps.Count > want)
            {
                Lump l = _lumps[_lumps.Count - 1];
                if (l.T != null) Destroy(l.T.gameObject);
                _lumps.RemoveAt(_lumps.Count - 1);
            }
            while (_lumps.Count < want)
            {
                int k = _lumps.Count;
                GameObject go = new GameObject("ButterGrain (auto)") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = GrainMesh(k);
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = LumpMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float rnd = UnityEngine.Random.value;
                // размеры: много мелких, немного крупных «комков»
                float size = Mathf.Lerp(_lumpSize.x * 0.45f, _lumpSize.y * 1.25f, rnd * rnd);
                _lumps.Add(new Lump
                {
                    T = go.transform, R = mr,
                    Born = Time.time,
                    Ang = UnityEngine.Random.value * Mathf.PI * 2f,
                    Rad0 = Mathf.Sqrt(UnityEngine.Random.value) * 0.8f,
                    Size = size,
                    Phase = UnityEngine.Random.value * 10f,
                    Tint = UnityEngine.Random.Range(-0.07f, 0.06f),
                    Squash = new Vector3(UnityEngine.Random.Range(0.9f, 1.35f), UnityEngine.Random.Range(0.55f, 0.85f), UnityEngine.Random.Range(0.85f, 1.2f)),
                });
            }
        }

        private void UpdateLumps(float agitation)
        {
            if (_lumps.Count == 0) return;
            float S = Scale, t = Time.time, dt = Time.deltaTime;
            Vector3 ls = transform.lossyScale;
            Vector3 inv = new Vector3(S / Mathf.Max(1e-5f, ls.x), S / Mathf.Max(1e-5f, ls.y), S / Mathf.Max(1e-5f, ls.z));
            float reach = _surfaceOk ? _tEdgeMin : RadiusAtRim;
            // внутренний край масляного кольца (доля радиуса): зёрна прибиваются к нему и «врастают»
            float ringIn = Mathf.Clamp01(1f - _pasteShown * 1.08f);
            for (int i = 0; i < _lumps.Count; i++)
            {
                Lump l = _lumps[i];
                if (l.T == null) continue;
                l.T.gameObject.SetActive(_surfaceOk);
                if (!_surfaceOk) continue;
                float age = t - l.Born;
                float rise = Mathf.Clamp01(age / Mathf.Max(0.1f, _riseTime));
                float e = 1f - (1f - rise) * (1f - rise);
                // Взбивают — зёрна кружатся; потом их тянет к стенкам.
                l.Ang += dt * (0.25f + agitation * 2.2f + _stirAmt * 2.5f) * (0.6f + (l.Phase % 1f) * 0.8f);
                float toWall = Mathf.Clamp01(age / 7f);
                float rad = Mathf.Lerp(l.Rad0, Mathf.Max(l.Rad0, ringIn - 0.04f + (l.Phase % 0.3f) * 0.2f), toWall);
                Vector2 pp = new Vector2(Mathf.Cos(l.Ang), Mathf.Sin(l.Ang)) * Mathf.Min(rad, 0.97f);
                pp += new Vector2(Mathf.Sin(t * 0.5f + l.Phase), Mathf.Cos(t * 0.4f + l.Phase * 1.3f)) * 0.03f
                      - new Vector2(_nL.x, _nL.z) * 0.6f;
                pp = Vector2.ClampMagnitude(pp, 0.97f);
                float r = Mathf.Max(0f, reach - l.Size * 0.5f);
                Vector3 onSurf = _center + _e1 * (pp.x * r) + _e2 * (pp.y * r);
                // у кольца зерно наполовину тонет в пасте — «срастается»
                float merge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ringIn - 0.1f, ringIn + 0.02f, pp.magnitude));
                Vector3 bottom = new Vector3(onSurf.x, l.Size * 0.5f, onSurf.z);
                Vector3 top = onSurf + _nL * (l.Size * (0.12f - merge * 0.25f) + _bounce * J0n(pp.magnitude));
                float bob = Mathf.Sin(t * 2.2f + l.Phase) * l.Size * 0.07f * (1f + agitation * 3f) * (1f - merge);
                Vector3 local = Vector3.Lerp(bottom, top, e) + _nL * bob;
                l.T.localPosition = Vector3.Scale(local, inv);
                l.T.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.7f + l.Phase) * 10f, l.Phase * 36f + l.Ang * 20f, Mathf.Cos(t * 0.6f + l.Phase) * 8f);
                float sz = l.Size * (1f - merge * 0.2f);
                l.T.localScale = Vector3.Scale(l.Squash * sz, inv);
                Color c = _pasteColor * (1f + l.Tint);
                c.a = 1f;
                _lumpMpb.SetColor(BaseColorId, c);
                l.R.SetPropertyBlock(_lumpMpb);
                _lumps[i] = l;
            }
        }

        // ───────────────────────── ПУЗЫРИ ─────────────────────────

        private void UpdateBubbles(float agitation)
        {
            // v19.3: взбивание — без «водяных» пузырей (и пока не уляжется после него)
            bool stirred = _stirAmt > 0.05f || Time.time < _stirUntil + 1.2f;
            float amount = _stirBubbles ? Mathf.Max(agitation, _stirAmt) : (stirred ? 0f : agitation);
            bool on = amount > 0.05f && _surfaceOk && _bubblesPerSecond > 0f;
            if (on && _bubbles == null)
            {
                _bubbles = LiquidFx.CreateStream(transform, 0.012f, 0.25f);
                // НЕ дочерним: поток масштабируется по иерархии, а у бочки бетономешалки масштаб ×400 —
                // пузыри были на полкарты. Позицию ставим сами каждый кадр.
                if (_bubbles != null)
                {
                    // SetParent(null, true) сохранил бы мировой масштаб ×400 — сбрасываем в 1
                    _bubbles.transform.SetParent(null, false);
                    _bubbles.transform.localScale = Vector3.one;
                    ParticleSystem.MainModule bm = _bubbles.main;
                    bm.scalingMode = ParticleSystemScalingMode.Local;
                }
            }
            if (_bubbles == null) return;
            if (!on) { CCRuntimeParticles.SetStream(_bubbles, false, Vector3.zero, Vector3.zero, Color.clear, Color.clear, 0f); return; }
            Vector3 local;
            if (_stirAmt > 0.05f && UnityEngine.Random.value < 0.7f)
            {
                Vector2 j = UnityEngine.Random.insideUnitCircle * 0.04f;
                local = new Vector3(_stirLocal.x + j.x, 0f, _stirLocal.y + j.y);
                local += _nL * Vector3.Dot(_center - local, _nL);
            }
            else
            {
                Vector2 rnd = UnityEngine.Random.insideUnitCircle * _tEdgeMin * 0.9f;
                local = _center + _e1 * rnd.x + _e2 * rnd.y;
            }
            Vector3 at = transform.position + transform.rotation * (local * Scale);
            CCRuntimeParticles.SetStream(_bubbles, true, at, Vector3.up, Color.white, LiquidBase, _bubblesPerSecond * amount, 0.3f);
            if (_stirBubbles) EmitWhipBubbles();
        }

        /// <summary>v4: след щупа (полоса пены) и круги от него. Только вид.</summary>
        private void UpdateTrailAndStirWaves(float dt)
        {
            for (int i = 0; i < _trail.Length; i++)
            {
                if (_trail[i].w <= 0f) continue;
                _trail[i].z += dt;
                if (_trail[i].z > 3.2f) _trail[i].w = 0f;
            }
            if (_stirAmt < 0.3f || !_surfaceOk) return;
            _trailT -= dt;
            if (_trailT <= 0f)
            {
                _trailT = 0.055f;
                for (int i = _trail.Length - 1; i > 0; i--) _trail[i] = _trail[i - 1];
                _trail[0] = new Vector4(_stirLocal.x - _creamDrift.x, _stirLocal.y - _creamDrift.y, 0f, _stirAmt);
            }
            _wvStirT -= dt;
            if (_wvStirT <= 0f)
            {
                _wvStirT = 0.2f;
                EmitWave(_stirLocal, 0.0024f * _stirAmt);
            }
        }

        /// <summary>
        /// v4: ЭПИЦЕНТР ВЗБИВАНИЯ — там, где водят щупом: кипящий горб крупных пузырей,
        /// фонтанчик живых капель вверх-в стороны и «туча» клубов пены над точкой.
        /// </summary>
        private float _poofBurstT;
        private Vector3 _stirPrevW, _stirVelW;
        private bool _stirPrevOk;
        private Vector2 _stirRawLocal;
        private ParticleSystem _whipCloud;
        private bool _whipCloudTried;

        /// <summary>Случайный разрешённый вид взрывчика; -1 — все выключены.</summary>
        private int PickBurstKind()
        {
            int total = LiquidFx.WhipBurstKinds;
            int start = UnityEngine.Random.Range(0, total);
            for (int k = 0; k < total; k++)
            {
                int kind = (start + k) % total;
                if (_whipBurstKinds == null || kind >= _whipBurstKinds.Length || _whipBurstKinds[kind]) return kind;
            }
            return -1;
        }

        private void EmitWhipEpicenter(float dt)
        {
            if (_stirAmt < 0.4f || !_surfaceOk || _pasteShown > 0.6f || (_lid != null && _lid.HasLid))
            { _sprayAcc = _puffAcc = _epiAcc = 0f; _stirPrevOk = false; _stirVelW = Vector3.zero; return; }
            Vector3 local = new Vector3(_stirLocal.x, 0f, _stirLocal.y);
            local += _nL * Vector3.Dot(_center - local, _nL);
            if (!Inside(local)) return;
            float S = Scale;
            Quaternion rot = transform.rotation;
            Vector3 at = transform.position + rot * (local * S) + _nDynW * (_whipBoil * 0.5f * S);
            Color c = LiquidColor;
            float wet = 1f - _thickShown;

            // v8: туча взбивания — своя система в локальных координатах, стоит ровно у щупа
            // (без сглаживания) и едет за ним целиком.
            Vector3 rawL = new Vector3(_stirRawLocal.x, 0f, _stirRawLocal.y);
            rawL += _nL * Vector3.Dot(_center - rawL, _nL);
            if (!Inside(rawL)) rawL = local;
            Vector3 atRaw = transform.position + rot * (rawL * S) + _nDynW * (_whipBoil * 0.5f * S);
            if (!_whipCloudTried)
            {
                _whipCloudTried = true;
                _whipCloud = LiquidFx.CreateWhipCloud("CC Whip Cloud (auto)");
                if (_whipCloud != null) { ParticleSystem.MainModule wm = _whipCloud.main; wm.simulationSpeed = _whipFxSpeed; }   // v18.8: тучки живут вдвое быстрее
            }
            float fxk = Mathf.Max(0.1f, _whipFxSpeed * 1.4f * (Time.time < _stirFxUntil ? _stirFxK : 1f));   // большой миксер — медленнее вибратора; v23.51: быстрее
            dt *= fxk;                                                           // v18.8: и появляются вдвое чаще
            if (_whipCloud != null) _whipCloud.transform.parent.SetPositionAndRotation(atRaw, Quaternion.identity);

            // v5: редкие крупные капли — вверх и в стороны
            _sprayAcc += dt * _whipDrops * _stirAmt * Mathf.Max(wet, 0.35f) * WhipBoost * WhipDropK;   // v23.51: капель втрое больше (и у густого); v23.62: ещё вдвое
            int n = Mathf.Min(24, (int)_sprayAcc);
            _sprayAcc -= n;
            for (int i = 0; i < n; i++)
            {
                Vector2 j = UnityEngine.Random.insideUnitCircle * 0.025f;
                Vector3 off = rot * new Vector3(j.x, 0f, j.y);
                Vector3 rad = off.sqrMagnitude > 1e-8f ? off.normalized : rot * Vector3.right;
                Vector3 vel = _nDynW * UnityEngine.Random.Range(1f, 2.1f) + rad * UnityEngine.Random.Range(0.35f, 1.1f) + _velF;
                float size = UnityEngine.Random.Range(_whipDropSize.x, Mathf.Max(_whipDropSize.x, _whipDropSize.y)) * S * 1.5f;   // v23.62: крупнее
                LiquidFx.Drop(at + off * S, vel, size, c, UnityEngine.Random.Range(0.5f, 0.9f));
                // v23.62: и россыпь мелких капелек вокруг — летят шире и ниже
                int fine = UnityEngine.Random.Range(1, 4);
                for (int k = 0; k < fine; k++)
                {
                    Vector2 j2 = UnityEngine.Random.insideUnitCircle;
                    Vector3 r2 = rot * new Vector3(j2.x, 0f, j2.y);
                    Vector3 v2 = _nDynW * UnityEngine.Random.Range(0.6f, 1.4f) + r2 * UnityEngine.Random.Range(0.6f, 1.5f) + _velF;
                    LiquidFx.Drop(at + off * S, v2, size * UnityEngine.Random.Range(0.3f, 0.5f), c, UnityEngine.Random.Range(0.35f, 0.7f));
                }
            }

            // v7/v8: «возня» — крупные комки-тучки разной формы шапкой у щупа. Если есть туча
            // взбивания (локальная система) — комки живут в ней и едут за щупом целиком; клубятся шумом.
            Vector3 stirVel = Vector3.zero;
            if (_stirPrevOk) stirVel = Vector3.ClampMagnitude((atRaw - _stirPrevW) / Mathf.Max(1e-3f, dt / fxk), 3f);
            _stirPrevW = atRaw; _stirPrevOk = true;
            _stirVelW = Vector3.Lerp(_stirVelW, stirVel, 1f - Mathf.Exp(-dt / 0.06f));
            float follow = _whipPoofFollow;
            float pScale = _whipPoofScale * 1.9f * WhipSizeK;                // v9: ещё крупнее; v23.62: ×2.4
            bool inCloud = _whipCloud != null && follow > 0.01f;
            _puffAcc += dt * _whipDust * _stirAmt * (0.6f + 0.4f * _whipFoam) / Mathf.Max(1f, pScale * 0.5f / WhipSizeK) * WhipBoost;   // v23.51: тучек втрое больше (v23.62: крупнее, а не реже)
            n = Mathf.Min(48, (int)_puffAcc);
            _puffAcc -= n;
            Color foamC = Color.Lerp(c, Color.white, 0.5f);
            for (int i = 0; i < n; i++)
            {
                Vector2 j = UnityEngine.Random.insideUnitCircle;
                Vector3 h = rot * new Vector3(j.x, 0f, j.y);
                Vector3 dirH = h.sqrMagnitude > 1e-6f ? h.normalized : rot * Vector3.forward;
                float burst = _whipDustBurst * UnityEngine.Random.Range(0.3f, 1f) * (1f - 0.75f * follow);
                Vector3 vel = dirH * burst + _nDynW * (burst * UnityEngine.Random.Range(0.2f, 0.6f) + 0.06f);
                float size = UnityEngine.Random.Range(_whipDustSize.x, Mathf.Max(_whipDustSize.x, _whipDustSize.y)) * S * pScale * (0.6f + 0.25f * _whipFoam)
                           * UnityEngine.Random.Range(0.7f, 1.3f);
                Vector3 pos = atRaw + h * (size * 0.7f) + _nDynW * (size * UnityEngine.Random.Range(0.05f, 0.4f));
                float life = UnityEngine.Random.Range(0.32f, 0.55f) + 0.3f * follow;
                if (inCloud) LiquidFx.PoofInto(_whipCloud, pos, vel, size, foamC, life);
                else LiquidFx.Poof(pos, (vel + _stirVelW * (0.85f * follow) + _velF) * fxk, size, foamC, life / fxk);
                // v9: мелкие детали — 2–3 крошечных комка вокруг большого (тучка не «грубая»)
                int bits = UnityEngine.Random.Range(1, 4);
                for (int k = 0; k < bits; k++)
                {
                    Vector3 o = UnityEngine.Random.onUnitSphere;
                    if (Vector3.Dot(o, _nDynW) < 0f) o = Vector3.Reflect(o, _nDynW);
                    Vector3 bp = pos + o * size * UnityEngine.Random.Range(0.45f, 0.75f);
                    Vector3 bv = vel * 1.3f + o * burst * 0.6f;
                    float bs = size * UnityEngine.Random.Range(0.16f, 0.32f);
                    Color bc = Color.Lerp(foamC, Color.white, UnityEngine.Random.Range(0f, 0.3f));
                    float bl = life * UnityEngine.Random.Range(0.6f, 0.9f);
                    if (inCloud) LiquidFx.PoofInto(_whipCloud, bp, bv, bs, bc, bl);
                    else LiquidFx.Poof(bp, (bv + _stirVelW * (0.85f * follow) + _velF) * fxk, bs, bc, bl / fxk);
                }
            }

            // v6: взрывчики разных видов — то клубок, то взрыв с искрами, то кольцо, то столбик
            _poofBurstT -= dt * _stirAmt * 2f;                                  // v23.51: взрывчики вдвое чаще
            if (_whipBursts > 0f && _poofBurstT <= 0f)
            {
                float mean = 1f / _whipBursts;
                _poofBurstT = UnityEngine.Random.Range(mean * 0.4f, mean * 1.6f);
                int kind = PickBurstKind();
                if (kind >= 0)
                {
                    Vector2 j = UnityEngine.Random.insideUnitCircle * 0.03f;
                    Vector3 pos = atRaw + rot * new Vector3(j.x, 0f, j.y) * S;
                    bool cloud = _whipCloud != null && _whipPoofFollow > 0.5f;
                    LiquidFx.WhipBurst(pos, _nDynW, Color.Lerp(c, Color.white, 0.2f),
                                       S * _whipBurstSize * (0.85f + 0.3f * _whipFoam) * WhipSizeK,
                                       cloud ? Vector3.zero : _velF + _stirVelW * (0.7f * _whipPoofFollow), kind,
                                       _whipPoofScale * 1.1f * WhipSizeK, Mathf.Lerp(1f, 0.45f, _whipPoofFollow), cloud ? _whipCloud : null);
                }
            }

            if (_stirBubbles)
            {
                _epiAcc += dt * _whipEpicenterBubbles * _stirAmt;
                n = Mathf.Min(10, (int)_epiAcc);
                _epiAcc -= n;
                for (int i = 0; i < n; i++)
                {
                    Vector2 j = UnityEngine.Random.insideUnitCircle * 0.035f;
                    Vector3 pos = at + rot * new Vector3(j.x, 0f, j.y) * S;
                    LiquidFx.Bubble(pos, _nDynW, _whipBubbleSize * S * UnityEngine.Random.Range(0.8f, 1.7f), c, _velF);
                }
            }
            else
            {
                // v19.3: лёгкие хлопья пены — мелкие светлые комочки медленно вспархивают у венчика и тают
                _epiAcc += dt * _whipFlecks * _stirAmt;
                n = Mathf.Min(8, (int)_epiAcc);
                _epiAcc -= n;
                Color flC = Color.Lerp(c, Color.white, 0.7f);
                for (int i = 0; i < n; i++)
                {
                    Vector2 j = UnityEngine.Random.insideUnitCircle * 0.06f;
                    Vector3 pos = atRaw + rot * new Vector3(j.x, 0f, j.y) * S;
                    Vector3 vel = _nDynW * UnityEngine.Random.Range(0.08f, 0.22f) + UnityEngine.Random.insideUnitSphere * 0.08f
                                + _stirVelW * 0.3f + _velF;
                    float size = _whipBubbleSize * S * UnityEngine.Random.Range(0.25f, 0.55f) * 2f;   // v23.62: хлопья крупнее
                    LiquidFx.Poof(pos, vel * fxk, size, flC, UnityEngine.Random.Range(0.5f, 0.9f) / fxk);
                }
            }
        }

        /// <summary>
        /// Взбивание — крупные пузыри: вспучиваются на поверхности у щупа и по всему
        /// ведру, лопаются с брызгами. Чем дольше взбиваешь, тем их больше.
        /// </summary>
        private void EmitWhipBubbles()
        {
            if (_stirAmt < 0.05f || _whipBubbles <= 0f) { _bubbleAcc = 0f; return; }
            _bubbleAcc += Time.deltaTime * _whipBubbles * _stirAmt * (0.6f + _whipFoam) * (1f - 0.75f * _pasteShown);
            int n = Mathf.Min(24, (int)_bubbleAcc);
            if (n <= 0) return;
            _bubbleAcc -= n;
            float S = Scale;
            Color c = LiquidColor;
            for (int i = 0; i < n; i++)
            {
                Vector3 local;
                // 60% — кольцом вокруг щупа, остальные — по всей поверхности.
                if (UnityEngine.Random.value < 0.6f)
                {
                    Vector2 j = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(0.012f, 0.08f);
                    local = new Vector3(_stirLocal.x + j.x, 0f, _stirLocal.y + j.y);
                    local += _nL * Vector3.Dot(_center - local, _nL);
                }
                else
                {
                    Vector2 rnd = UnityEngine.Random.insideUnitCircle * _tEdgeMin * 0.92f;
                    local = _center + _e1 * rnd.x + _e2 * rnd.y;
                }
                if (!Inside(local)) continue;
                Vector3 at = transform.position + transform.rotation * (local * S);
                float size = _whipBubbleSize * S * UnityEngine.Random.Range(0.35f, 1.25f) * (0.7f + _stirAmt * 0.5f)
                             * (1f + _pasteShown * 0.8f);                       // в пасте пузыри реже, но крупнее
                LiquidFx.Bubble(at, _nDynW, size, c, _velF);
            }
        }

        // ───────────────────────── РЕДАКТОР ─────────────────────────

#if UNITY_EDITOR
        /// <summary>
        /// v4: Меряет внутреннюю стенку по ТРЕУГОЛЬНИКАМ модели: на каждой высоте режет стенки
        /// плоскостью и берёт ближайшее к оси расстояние до грани (не до вершины — у лоу-поли
        /// ведра середина плоской грани ближе угла на ~3.5%, жидкость выпирала сквозь стенку).
        /// Заодно находит число граней и их поворот — край жидкости идёт ровно по граням.
        /// Выключенные меши (старая модель) и сама жидкость не учитываются.
        /// </summary>
        [ContextMenu("Подогнать под стенки модели")]
        private void FitToModelWalls()
        {
            Transform root = _cookware != null ? _cookware.transform : (transform.parent != null ? transform.parent : transform);
            const int Bands = 24;
            float H = _maxHeight, guess = Mathf.Max(_radiusTop, _radiusBottom);
            var minR = new float[Bands];
            for (int b = 0; b < Bands; b++) minR[b] = float.MaxValue;
            var angles = new List<float>();
            var angR = new List<float>();
            int used = 0;
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.transform.IsChildOf(transform)) continue;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled || !mf.gameObject.activeInHierarchy) continue;      // старая модель выключена
                string n = mf.gameObject.name;
                if (n.Contains("(auto)") || n.Contains("AutoColliders") || n.Contains("HeapAnchor")) continue;
                Transform hp = root.Find("HandlePivot");
                if (hp != null && mf.transform.IsChildOf(hp)) continue;                 // дужка — не стенка
                Mesh mesh = mf.sharedMesh;
                Vector3[] verts = mesh.vertices;
                int[] tris = mesh.triangles;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                var loc = new Vector3[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                    loc[i] = inv * (m.MultiplyPoint3x4(verts[i]) - transform.position) / Scale;
                used++;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    Vector3 a = loc[tris[t]], b = loc[tris[t + 1]], c = loc[tris[t + 2]];
                    Vector3 nrm = Vector3.Cross(b - a, c - a);
                    float nm = nrm.magnitude;
                    if (nm < 1e-9f || Mathf.Abs(nrm.y / nm) > 0.75f) continue;        // дно, кромка сверху — не стенка
                    float ymin = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), ymax = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    for (int k = 0; k < Bands; k++)
                    {
                        float h = (k + 0.5f) / Bands * H;
                        if (h < ymin || h > ymax) continue;
                        Vector2 p0, p1;
                        if (!Slice(a, b, c, h, out p0, out p1)) continue;
                        float d = SegDist(p0, p1);
                        if (d < guess * 0.3f || d > guess * 2.2f) continue;            // мусор у оси / ручка снаружи
                        if (d < minR[k]) minR[k] = d;
                    }
                }
                // вершины середины высоты — для числа граней
                foreach (Vector3 l in loc)
                {
                    if (l.y < H * 0.12f || l.y > H * 0.85f) continue;
                    float r = new Vector2(l.x, l.z).magnitude;
                    if (r < guess * 0.4f || r > guess * 1.6f) continue;
                    angles.Add(Mathf.Atan2(l.z, l.x)); angR.Add(r);
                }
            }
            var prof = new float[Bands];
            var have = new bool[Bands];
            for (int b = 0; b < Bands; b++) { have[b] = minR[b] < float.MaxValue; if (have[b]) prof[b] = minR[b]; }
            int first = -1;
            for (int b = 0; b < Bands; b++) if (have[b]) { first = b; break; }
            if (first < 0)
            {
                Debug.LogWarning($"[Жидкость] «{name}»: не нашёл стенок (моделей: {used}). Проверь Radius Top / Max Height и что модель включена.", this);
                return;
            }
            for (int b = 0; b < Bands; b++)
            {
                if (have[b]) continue;
                int lo = b - 1; while (lo >= 0 && !have[lo]) lo--;
                int hi = b + 1; while (hi < Bands && !have[hi]) hi++;
                if (lo < 0) prof[b] = prof[hi];
                else if (hi >= Bands) prof[b] = prof[lo];
                else prof[b] = Mathf.Lerp(prof[lo], prof[hi], (b - lo) / (float)(hi - lo));
            }
            // Число граней: самая малая N (≥5), при которой вершины ложатся ровно через 360/N.
            int sides = 0; float phase = 0f;
            if (angles.Count >= 10)
            {
                float medR = 0f;
                var rs = new List<float>(angR); rs.Sort(); medR = rs[rs.Count / 4];
                for (int N = 5; N <= 40 && sides == 0; N++)
                {
                    float cx = 0f, cy = 0f; int cnt = 0;
                    for (int i = 0; i < angles.Count; i++)
                    {
                        if (angR[i] > medR * 1.15f) continue;                        // только внутренняя стенка
                        cx += Mathf.Cos(angles[i] * N); cy += Mathf.Sin(angles[i] * N); cnt++;
                    }
                    if (cnt < 8) break;
                    float R = Mathf.Sqrt(cx * cx + cy * cy) / cnt;
                    if (R > 0.96f)
                    {
                        sides = N;
                        float vAng = Mathf.Atan2(cy, cx) / N;                         // угол вершины
                        phase = (vAng + Mathf.PI / N) * Mathf.Rad2Deg;               // середина грани
                    }
                }
            }
            UnityEditor.Undo.RecordObject(this, "Подогнать жидкость под стенки");
            _wallProfile = prof;
            _radiusBottom = prof[0];
            _radiusTop = prof[Bands - 1];
            _wallSides = sides;
            _wallPhase = phase;
            _geomDirty = true;
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[Жидкость] «{name}»: стенка подогнана по граням — низ {prof[0]:0.###} м, уже всего {Mathf.Min(prof):0.###} м, " +
                      $"верх {prof[Bands - 1]:0.###} м, " + (sides >= 3 ? $"граней {sides} (поворот {phase:0.#}°)" : "круглая") + $", моделей {used}", this);
        }

        /// <summary>Сечение треугольника плоскостью y = h → отрезок (x, z).</summary>
        private static bool Slice(Vector3 a, Vector3 b, Vector3 c, float h, out Vector2 p0, out Vector2 p1)
        {
            p0 = p1 = Vector2.zero;
            int n = 0;
            Vector3[] e = { a, b, b, c, c, a };
            for (int i = 0; i < 6; i += 2)
            {
                Vector3 u = e[i], v = e[i + 1];
                if ((u.y - h) * (v.y - h) > 0f || Mathf.Approximately(u.y, v.y)) continue;
                float k = (h - u.y) / (v.y - u.y);
                Vector3 q = Vector3.Lerp(u, v, k);
                if (n == 0) p0 = new Vector2(q.x, q.z); else p1 = new Vector2(q.x, q.z);
                n++;
                if (n == 2) break;
            }
            return n == 2;
        }

        /// <summary>Расстояние от оси (0,0) до отрезка.</summary>
        private static float SegDist(Vector2 p0, Vector2 p1)
        {
            Vector2 d = p1 - p0;
            float L = d.sqrMagnitude;
            float t = L > 1e-12f ? Mathf.Clamp01(-Vector2.Dot(p0, d) / L) : 0f;
            return (p0 + d * t).magnitude;
        }

        [ContextMenu("Сбросить профиль стенок (прямой конус)")]
        private void ClearWallProfile()
        {
            UnityEditor.Undo.RecordObject(this, "Сбросить профиль стенок");
            _wallProfile = new float[0];
            _wallSides = 0;
            _geomDirty = true;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one * Scale);
            if (_form == Form.Box && _boxProfile != null && _boxProfile.Length >= 2)
            {
                for (int k = 0; k < _boxProfile.Length; k++)
                {
                    float y = _maxHeight * k / (_boxProfile.Length - 1);
                    Vector4 b = _boxProfile[k];
                    Gizmos.DrawWireCube(new Vector3(b.z, y, b.w), new Vector3(b.x * 2f, 0.001f, b.y * 2f));
                }
            }
            else if (_form == Form.Box)
            {
                Gizmos.DrawWireCube(Vector3.up * _maxHeight * 0.5f, new Vector3(_halfSize.x * 2f, _maxHeight, _halfSize.y * 2f));
            }
            else
            {
                for (int k = 0; k <= 4; k++)
                {
                    float y = _maxHeight * k / 4f, r = WallR(y);
                    for (int i = 0; i < 32; i++)
                    {
                        float a0 = i / 32f * Mathf.PI * 2f, a1 = (i + 1) / 32f * Mathf.PI * 2f;
                        Gizmos.DrawLine(new Vector3(Mathf.Cos(a0) * r, y, Mathf.Sin(a0) * r),
                                        new Vector3(Mathf.Cos(a1) * r, y, Mathf.Sin(a1) * r));
                    }
                }
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
