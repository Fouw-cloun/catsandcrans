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
        private float Stiff => Mathf.Max(Mathf.Max(_pasteShown, _thickShown), Mathf.Max(_iceShown, _frozenShown));
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

        private void OnEnable() { if (_cookware != null) _cookware.ContentsChanged += Refresh; }
        private void OnDisable()
        {
            if (_cookware != null) _cookware.ContentsChanged -= Refresh;
            if (_bubbles != null) CCRuntimeParticles.SetStream(_bubbles, false, Vector3.zero, Vector3.zero, Color.clear, Color.clear, 0f);
        }
        private void Start() => Refresh();

        private void OnDestroy()
        {
            if (_mesh != null) SafeDestroy.Kill(_mesh);
            foreach (Lump l in _lumps) if (l.T != null) SafeDestroy.Kill(l.T.gameObject);
            foreach (Lump l in _bits) if (l.T != null) SafeDestroy.Kill(l.T.gameObject);
            if (_bubbles != null) SafeDestroy.Kill(_bubbles.gameObject);
            if (_whipCloud != null) SafeDestroy.Kill(_whipCloud.transform.parent != null ? _whipCloud.transform.parent.gameObject : _whipCloud.gameObject);
            foreach (Topping t in _tops) { if (t.Go != null) SafeDestroy.Kill(t.Go); if (t.Mesh != null) SafeDestroy.Kill(t.Mesh); }   // v19.1
            _tops.Clear();
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

        private sealed class Topping
        {
            public ushort Id; public FoodState State; public int Count, Built = -1;
            public FoodSubstance Sub; public GameObject Go; public Mesh Mesh; public MeshRenderer Mr;
            public float Ang, Dist;
        }

        private readonly List<Topping> _tops = new List<Topping>();
        private int _pasteBest; private ushort _baseId;
        private float _mixP, _mixDustAcc;
        private Color _mixColor = new Color(0.36f, 0.19f, 0.1f);

        /// <summary>Этот продукт рисуется добавкой-кучкой поверх (HeapAnchor его не рисует).</summary>
        public bool ShowsTopping(ushort productId)
        {
            foreach (Topping t in _tops) if (t.Id == productId && t.Count > 0) return true;
            return false;
        }

        private void RebuildToppings()
        {
            FoodDatabase db = FoodDatabase.Instance;
            foreach (Topping t in _tops) t.Count = 0;
            if (_cookware != null && Butter > 0 && db != null)
                for (int i = 0; i < _cookware.ViewCount; i++)
                {
                    BagView v = _cookware.GetView(i);
                    if (v.Count == 0 || v.Product == _baseId) continue;
                    ProductDef d = db.Get(v.Product);
                    FoodSubstance sub = d != null ? FoodSubstance.Of(d, v.State) : null;
                    if (sub == null || !sub.OwnLook || FoodHeapLook.IsLiquid(d, v.State)) continue;
                    Topping t = null;
                    foreach (Topping x in _tops) if (x.Id == v.Product) { t = x; break; }
                    if (t == null)
                    {
                        t = new Topping { Id = v.Product };
                        float hsh = Mathf.Repeat(v.Product * 0.618034f, 1f);
                        t.Ang = hsh * 6.2832f; t.Dist = 0.3f + 0.15f * Mathf.Repeat(v.Product * 0.377f, 1f);
                        _tops.Add(t);
                    }
                    t.State = v.State; t.Count += v.Count; t.Sub = sub;
                }
            for (int i = _tops.Count - 1; i >= 0; i--)
                if (_tops[i].Count <= 0)
                {
                    if (_tops[i].Go != null) SafeDestroy.Kill(_tops[i].Go);
                    if (_tops[i].Mesh != null) SafeDestroy.Kill(_tops[i].Mesh);
                    _tops.RemoveAt(i);
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

        private void UpdateToppings(float dt)
        {
            float S = Scale, R = Mathf.Max(0.05f, RadiusAtRim);
            bool stirring = _stirAmt > 0.3f && Time.time < _stirUntil + 0.1f;
            // смешивание (только вид; результат считает сервер — FoodBlend)
            if (stirring && _tops.Count > 0)
            {
                float sec; Color rc;
                if (FoodBlend.ClientInfo(_cookware, out sec, out rc)) { _mixColor = rc; _mixP = Mathf.Min(0.97f, _mixP + dt / Mathf.Max(0.5f, sec)); }
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
                if (t.Built != t.Count)
                {
                    t.Built = t.Count;
                    ProductDef d = db != null ? db.Get(t.Id) : null;
                    Color c = t.Sub.ColorFor(FoodColors.Of(d, t.State));
                    float r = R * S * Mathf.Clamp(0.18f + 0.5f * Mathf.Sqrt(t.Count / (float)total), 0.16f, 0.5f);
                    var dm = new FoodSubstanceMesh.Dims { Rx = r, Rz = r, H = r * Mathf.Max(0.3f, t.Sub.FloorSlope) * 0.85f, Edge = 0f, RectN = 2f };
                    int seed = t.Id * 131 + 7;
                    FoodSubstanceMesh.SpoilK = FoodStateInfo.IsSpoiled(t.State) ? 1f : 0f;
                    if (t.Sub.Look == SubstanceLook.Powder) FoodSubstanceMesh.Powder(t.Mesh, dm, t.Sub.Wonky, c, seed);
                    else if (t.Sub.Look == SubstanceLook.Crystals) FoodSubstanceMesh.Crystals(t.Mesh, dm, t.Sub.CrystalSize, t.Sub.CrystalDensity, c, seed);
                    else if (t.Sub.Look == SubstanceLook.Spread) FoodSubstanceMesh.Spread(t.Mesh, dm, t.Sub.Curl * 0.6f, c, seed);
                    else FoodSubstanceMesh.ButterSmear(t.Mesh, dm, 0f, c, seed);
                    FoodSubstanceMesh.SpoilK = 0f;
                    Material m = t.Sub.GetMaterial();
                    if (m != null) t.Mr.sharedMaterial = m;
                }
                float ang = t.Ang + _mixP * _mixP * 7.5f;                        // мешаешь — закручиваются к центру
                float dist = R * t.Dist * (1f - 0.85f * _mixP);
                Vector3 local = new Vector3(Mathf.Cos(ang) * dist, _lc + lift, Mathf.Sin(ang) * dist);
                t.Go.transform.SetPositionAndRotation(transform.position + transform.rotation * (local * S),
                                                      transform.rotation * Quaternion.Euler(0f, -ang * Mathf.Rad2Deg, 0f));
                float sc = Mathf.Pow(1f - _mixP, 0.8f);                            // и тают
                t.Go.transform.localScale = Vector3.one * sc;
                bool show = _surfaceOk && sc > 0.03f;
                if (t.Go.activeSelf != show) t.Go.SetActive(show);
            }
        }

        private Material PowderMat
        {
            get
            {
                if (_powderSub != null && _powderSub.Material != null) return _powderSub.Material;
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
            float lam = Mathf.Max(0.02f, _waveLength);
            float k = 6.2831853f / lam, h = 0f;
            for (int i = 0; i < _wv.Length; i++)
            {
                Vector4 w = _wv[i];
                if (w.y <= 0f) continue;
                float a = t - w.w;
                if (a < 0f || a > 2.6f) continue;
                float dx = x - w.x, dz = z - w.z;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                float u = r - a * _waveSpeed;
                float env = Mathf.Exp(-u * u / (lam * lam * 1.6f)) * Mathf.Exp(-a * 1.4f) / (1f + r * 5f);
                // отражение от стенки: тот же фронт, бегущий назад от края
                float R = Mathf.Max(0.03f, RadiusAtRim);
                float ur = (2f * R - r) - a * _waveSpeed;
                float envR = a * _waveSpeed > R - r ? Mathf.Exp(-ur * ur / (lam * lam * 1.6f)) * Mathf.Exp(-a * 1.9f) * 0.55f / (1f + r * 5f) : 0f;
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
        public void ForceRefresh() => Refresh();

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

        private void Refresh()
        {
            if (_cookware == null || _products == null) return;
            _liquid = _layer = _floating = _paste = _thick = _ice = _frozenN = _spoiledN = 0;
            _powderN = 0;
            _pasteBest = 0; _baseId = 0;
            _slabN = 0;
            _slabThickN = 0;
            _slabSpoilN = 0;
            int waterBest = 0;
            _waterLike = false;
            for (int i = 0; i < _cookware.ViewCount; i++)
            {
                BagView v = _cookware.GetView(i);
                foreach (Entry e in _products)
                {
                    if (e.Product == null || e.Product.Id != v.Product) continue;
                    Color col = e.Color.a > 0.01f ? e.Color : e.Product.BaseColor;
                    switch (e.Role)
                    {
                        case Role.Liquid:
                            _liquid += v.Count; _liquidColor = col;
                            if (v.Count > waterBest) { waterBest = v.Count; _waterLike = IsWater(e.Product) && e.Color.a <= 0.01f; }
                            break;
                        case Role.TopLayer: _layer += v.Count; _layerColor = col; break;
                        case Role.Floating: _floating += v.Count; _floatColor = col; if (v.Count > _pasteBest) { _pasteBest = v.Count; _pasteColor = col; _baseId = e.Product.Id; } if (IsPowder(e.Product, v.State)) _powderN += v.Count; else if (IsSlab(e.Product, v.State)) { _slabN += v.Count; if (FoodStateInfo.IsSpoiled(v.State)) _slabSpoilN += v.Count; } break;
                        case Role.Paste: _paste += v.Count; if (v.Count > _pasteBest) { _pasteBest = v.Count; _pasteColor = col; _baseId = e.Product.Id; } if (IsPowder(e.Product, v.State)) _powderN += v.Count; else if (IsSlab(e.Product, v.State)) { _slabN += v.Count; if (FoodStateInfo.IsSpoiled(v.State)) _slabSpoilN += v.Count; } break;
                        case Role.Thick: _thick += v.Count; _thickColor = col; if (v.State == FoodState.Spoiled) _spoiledN += v.Count; if (IsSlab(e.Product, v.State)) _slabThickN += v.Count; break;
                        case Role.IceCream: _ice += v.Count; _iceColor = col; break;
                    }
                    // заморожено азотом (не мороженое) — покрыто льдом, пока не оттает
                    if (v.State == FoodState.Frozen && e.Role != Role.IceCream) _frozenN += v.Count;
                }
            }
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
            float u = Mathf.Abs(x - bp.z) / Mathf.Max(1e-4f, bp.x), w = Mathf.Abs(z - bp.w) / Mathf.Max(1e-4f, bp.y);
            float u2 = u * u, w2 = w * w, u8 = u2 * u2 * u2 * u2, w8 = w2 * w2 * w2 * w2;
            return Mathf.Pow(u8 + w8, 0.125f);
        }

        private bool Inside(Vector3 p) => p.y >= 0f && p.y <= RimH && Radial(p.x, p.z, p.y) <= 1f;

        private float Area(float y)
        {
            if (_form == Form.Round) return Mathf.PI * WallR(y) * WallR(y);
            Vector4 b = BoxAt(y);
            return 4f * b.x * b.y * 0.978f;                     // скруглённый прямоугольник
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
                foreach (Vector4 b in _boxProfile) m = Mathf.Max(m, Mathf.Max(b.x, b.y));
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
                foreach (Vector4 b in _boxProfile) { rx = Mathf.Max(rx, Mathf.Abs(b.z) + b.x); rz = Mathf.Max(rz, Mathf.Abs(b.w) + b.y); }
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

            UpdateTrailAndStirWaves(dt);
            if (_mf != null)
            {
                float dens = _waterLike ? 0f : _density;
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
                _mr.enabled = _surfaceOk;
                if (_surfaceOk) BuildSurface(ag, dt);
                UpdateToppings(dt);

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
                _mpb.SetFloat(FoamId, Mathf.Clamp01(Mathf.Max(ag * _foam + _stirAmt * 0.35f, _whipFoam)) * (1f - _thickShown));
                _mpb.SetFloat(MeniscusId, 1f);
                _mpb.SetFloat(DensityId, dens);
                _mpb.SetFloat(SkinId, _waterLike ? 0f : _milkSkin * (1f - _creamShown * 0.6f) * (1f - Stiff));
                float baseOpacity = _waterLike ? _waterOpacity
                                  : (want != null && want.HasProperty(OpacityId) ? want.GetFloat(OpacityId) : 0.95f);
                float soupK = _soupTint > 0.001f ? Mathf.Clamp01(_soupShown / _soupTint) : 0f;
                _mpb.SetFloat(OpacityId, Mathf.Lerp(baseOpacity, Mathf.Max(baseOpacity, _soupOpacity), soupK));
                _mpb.SetFloat(PasteId, 0f);        // масло задаётся по вершинам (растёт от стенок), см. SetVertex
                // v4: жирные глазки — только на молоке/сливках (не вода, не густое)
                float fat = _waterLike || _liquid + _layer == 0 ? 0f : _fatSpots * (1f - Stiff) * (1f - _soupShown);
                _mpb.SetFloat(FatId, fat);
                _mpb.SetFloat(FatScaleId, _fatScale);
                for (int i = 0; i < _trail.Length; i++) _trailSend[i] = _trail[i];
                _mpb.SetVectorArray(TrailId, _trailSend);
                _mpb.SetFloat(TrailAmountId, _waterLike ? _stirTrails * 0.4f : _stirTrails);
                // v7: молоко матовое — меньше блика, отсвета и френеля, свет площе (не «металл», не вода)
                float matte = _waterLike ? 0f : _milkMatte * (1f - _soupShown);
                if (want != null)
                {
                    _mpb.SetFloat(GlintStrengthId, Mathf.Lerp(MatF(want, GlintStrengthId, 1.3f), 0.28f, matte));
                    _mpb.SetFloat(GlintSizeId, Mathf.Lerp(MatF(want, GlintSizeId, 0.965f), 0.988f, matte));
                    _mpb.SetFloat(SheenStrengthId, Mathf.Lerp(MatF(want, SheenStrengthId, 0.25f), 0.06f, matte));
                    _mpb.SetFloat(FresnelStrengthId, Mathf.Lerp(MatF(want, FresnelStrengthId, 0.35f), 0.1f, matte));
                    _mpb.SetFloat(FlatId, Mathf.Lerp(MatF(want, FlatId, 0.6f), 0.85f, matte));
                }
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
        private void UpdateMotion(float dt)
        {
            Vector3 p = transform.position;
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
            Vector2 drive = new Vector2(-_accF.x, -_accF.z) / 9.81f * _sloshGain * fluid;
            float bDrive = -_accF.y / 9.81f * R * 0.12f * _bounceGain * fluid;
            z1Damp += Stiff * 12f;
            // v7: «тело» молока — тяжелее: слабее толчки, быстрее успокаивается
            float bodyK = _waterLike ? 0f : _milkBody;
            drive *= 1f - 0.35f * bodyK;
            bDrive *= 1f - 0.3f * bodyK;
            z1Damp += 2f * 0.1f * bodyK * w1;
            UpdateJelly(dt, jerk);
            // Толчок (шаг, остановка) — «подпрыгивание»: изменение ускорения за кадр.
            float dAcc = Mathf.Min(new Vector2(jerk.x, jerk.z).magnitude * dt, 20f);
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
            for (int s = 0; s < Dirs; s++)
            {
                float a = s / (float)Dirs * Mathf.PI * 2f;
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
                if (_tEdge[s] > m * 1.12f + 0.005f) _tEdge[s] = 0.5f * (ta + tb);
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
                    float a = s / (float)Dirs * Mathf.PI * 2f;
                    Vector3 u = _e1 * Mathf.Cos(a) + _e2 * Mathf.Sin(a);
                    Vector3 p = c + u * (_tEdge[s] * rho);
                    float lift = 0f;
                    if (_wallEdge[s])
                    {
                        float m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 1f, rho));
                        lift = _meniscus * m * m;
                        if (lip) p += u * _edgeOverlap;                  // заходит под стенку — без щели
                    }
                    SetVertex(vi++, p, rho, n, lift, wob, t, H, toLocal, baseCol, lip);
                }
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
            if (wob > 0f) d += wob * Mathf.Sin(p.x * 40f + t * 17f) * Mathf.Cos(p.z * 37f - t * 13f) * (1f - rho * 0.5f);
            d += WaveH(p.x, p.z, t) * (1f - Stiff);                             // v4: бегущие волны
            if (_stirAmt > 0.01f)
            {
                float sd = new Vector2(p.x - _stirLocal.x, p.z - _stirLocal.y).magnitude;
                // v4: ЭПИЦЕНТР — объёмный горб бурления: пузыри кипят и вспучивают поверхность у щупа
                float br = Mathf.Max(0.02f, _whipBoilRadius);
                float fall = Mathf.Exp(-sd * sd / (br * br));
                float boilA = Mathf.PerlinNoise(p.x * 70f + t * 9f, p.z * 70f - t * 8f) - 0.35f;
                float boilB = Mathf.PerlinNoise(p.x * 140f - t * 15f, p.z * 140f + t * 13f) - 0.5f;
                d += _stirAmt * _whipBoil * fall * (0.55f + boilA + boilB * 0.7f);
                d += _stirAmt * 0.0035f * Mathf.Sin(sd * 140f - t * 38f) * Mathf.Exp(-sd / 0.06f);
                d -= _stirAmt * 0.004f * Mathf.Exp(-sd * sd / 0.0005f);            // ямка прямо у щупа
                // Кипит и вокруг: пузыри вспучивают поверхность шире.
                float boil = Mathf.PerlinNoise(p.x * 60f + t * 6f, p.z * 60f - t * 5f) - 0.45f;
                d += _stirAmt * 0.004f * boil * Mathf.Exp(-sd / 0.12f);
            }
            // Брожение: поверхность медленно «дышит» — закваска работает.
            if (_fermenting && _fermentHeave > 0f)
                d += _fermentHeave * (Mathf.PerlinNoise(p.x * 18f + t * 0.6f, p.z * 18f - t * 0.45f) - 0.5f) * (1f - _thickShown * 0.6f);
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
                float ice = (Mathf.PerlinNoise(p.x * 30f + 4.1f, p.z * 30f + 8.3f) - 0.5f) * 0.002f;
                float crack = Mathf.Pow(1f - Mathf.Abs(Mathf.PerlinNoise(p.x * 14f + 1.7f, p.z * 14f + 9.9f) * 2f - 1f), 12f);
                d = Mathf.Lerp(d, ice - crack * 0.0015f, fm);
                Color basec = (Color)col;
                Color fc = Color.Lerp(basec, _frostColor, 0.45f + crack * 0.25f);
                Color mixc = Color.Lerp(basec, fc, fm);
                col = (Color32)new Color(mixc.r, mixc.g, mixc.b, basec.a);
            }
            _uv3[i] = new Vector2(im, fm);
            Vector3 q = p + n * (d + lift) + jellyShear;
            if (!lip && q.y > H - 0.002f) q.y = H - 0.002f;
            if (lip && q.y > _maxHeight - 0.003f) q.y = _maxHeight - 0.003f;
            if (q.y < 0.0015f) q.y = 0.0015f;
            _vtx[i] = Vector3.Scale(q, toLocal);
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
            float k = 1f / Mathf.Max(0.02f, _thickBlob * 1.6f);
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
            float k = 1f / Mathf.Max(0.02f, _thickBlob);
            float n1 = Mathf.PerlinNoise(x * k + 5.1f, z * k + 12.7f);
            float n2 = Mathf.PerlinNoise(x * k * 1.9f + 27.3f, z * k * 1.9f + 2.2f);
            float h = (Mathf.SmoothStep(0f, 1f, n1) - 0.5f) * 0.9f + (n2 - 0.5f) * 0.35f;
            // Пики: острые гребни с наклоном в одну сторону — «загнутый кончик».
            float n3 = Mathf.PerlinNoise(x * k * 1.3f + 61.7f, z * k * 1.3f + 33.1f);
            float ridge = Mathf.Pow(1f - Mathf.Abs(n3 * 2f - 1f), 6f);
            float lean = Mathf.PerlinNoise(x * k * 1.3f + 61.9f, z * k * 1.3f + 33.3f) - n3;   // сдвиг гребня
            h += ridge * _thickPeaks * (0.85f + lean * 6f);
            // Спираль от мешания (если мешали) — как сметана, размешанная ложкой.
            Vector2 d0 = new Vector2(x, z) - _pasteSwirlC;
            float rr = d0.magnitude;
            float R = Mathf.Max(0.05f, RadiusAtRim * 0.85f);
            float trail = Mathf.Clamp01(_pasteSwirl * 0.3f) * Mathf.Exp(-rr / (R * 0.9f));
            h += Mathf.Sin(rr * 120f + Mathf.Atan2(d0.y, d0.x) * 2f) * 0.25f * trail;
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
                h += _fermentHeave * 1.5f * tm * (Mathf.PerlinNoise(x * 25f + t * 0.5f, z * 25f - t * 0.4f) - 0.4f) * (1f - _thickShown);
            return h;
        }

        /// <summary>Мороженое появляется сразу по всей поверхности, край — неровный.</summary>
        private float IceMask(float x, float z, float rho)
        {
            float w = _iceShown;
            if (w <= 0.001f) return 0f;
            if (w >= 0.995f) return 1f;
            float n = Mathf.PerlinNoise(x * 9f + 71.3f, z * 9f + 13.7f);
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
            float R = Mathf.Max(0.05f, RadiusAtRim);
            Vector2 q = new Vector2(x, z);
            float h = 0f;
            if (_iceDunes > 0.001f)
            {
                // «КАК ИЗ МАГАЗИННОЙ БАНКИ»: поверхность в волнах-дюнах, как после
                // заводской ложки-скребка: изогнутые гребни с мягкими острыми макушками,
                // в двух направлениях поверх общего купола.
                float u = x / R, v = z / R;
                float warp = (Mathf.PerlinNoise(u * 1.6f + 3.1f, v * 1.6f + 7.7f) - 0.5f) * 3.2f;
                float a1 = (u * 0.8f + v * 0.6f) * 6.5f + warp;
                float a2 = (u * -0.55f + v * 0.83f) * 4.2f + warp * 0.7f + 1.3f;
                // асимметричный гребень дюны: пологий склон, крутой подветренный, острая макушка
                float d1 = Mathf.Repeat(a1 / 6.2832f, 1f);
                float dune1 = d1 < 0.7f ? Mathf.SmoothStep(0f, 1f, d1 / 0.7f) : Mathf.SmoothStep(1f, 0f, (d1 - 0.7f) / 0.3f);
                float d2 = Mathf.Repeat(a2 / 6.2832f, 1f);
                float dune2 = d2 < 0.65f ? Mathf.SmoothStep(0f, 1f, d2 / 0.65f) : Mathf.SmoothStep(1f, 0f, (d2 - 0.65f) / 0.35f);
                float dunes = Mathf.Max(dune1, dune2 * 0.75f);
                float peak = Mathf.Pow(dunes, 5f);                                   // кончики гребней — пиками
                float dome = 0.35f * (1f - rho * rho);
                h = Mathf.Max(h, (dome + dunes * 0.55f + peak * 0.3f) * _iceDunes);
            }
            // Шарики: центр + три вокруг. Положения постоянные для тары — у всех одинаково.
            for (int k = 0; k < 4; k++)
            {
                float ang = k * 2.09f + 0.6f;
                Vector2 c = k == 0 ? Vector2.zero : new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R * 0.48f;
                float r = R * (k == 0 ? 0.55f : 0.42f);
                Vector2 dq = q - c;
                float dd = dq.magnitude / r;
                if (dd > 1.25f) continue;
                float dome = Mathf.Sqrt(Mathf.Max(0f, 1f - dd * dd * 0.8f));
                // Следы ложки: дуги, закрученные вокруг шарика (каждый шарик — в свою сторону).
                float a = Mathf.Atan2(dq.y, dq.x) + k * 1.3f;
                float arc = Mathf.Sin(dd * 15f + a * (k % 2 == 0 ? 1.5f : -1.5f));
                float marks = (arc * 0.5f + 0.5f);
                marks = marks * marks * _iceScoopMarks * 0.28f * Mathf.SmoothStep(0.15f, 0.7f, dd);
                // край шарика — завиток/губа, как стружка у ложки
                float lipC = Mathf.Exp(-(dd - 0.95f) * (dd - 0.95f) / 0.01f) * 0.18f * (0.6f + Mathf.PerlinNoise(a * 2f + k, dd * 3f) * 0.8f);
                h = Mathf.Max(h, (dome - marks + lipC) * (k == 0 ? 1f : 0.88f) * _iceScoops);
            }
            // крупная неровность — не идеальные шары
            h += (Mathf.PerlinNoise(x * 17f + 3.3f, z * 17f + 5.5f) - 0.5f) * 0.18f;
            // Рыхлость: мелкие комочки и «крошка» по всей поверхности, как у только что
            // наскобленного мороженого — не ровная масса.
            float crumb = Mathf.PerlinNoise(x * 48f + 11.1f, z * 48f + 2.9f);
            float crumb2 = Mathf.PerlinNoise(x * 95f + 7.7f, z * 95f + 19.3f);
            h += (Mathf.SmoothStep(0.35f, 0.8f, crumb) - 0.45f) * 0.32f * _iceLoose + (crumb2 - 0.5f) * 0.12f * _iceLoose;
            // у стенки мороженое налипает валиком
            h += Mathf.SmoothStep(0.82f, 1f, rho) * 0.25f;
            h *= _iceRelief * im;
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
        private void EmitColdVapor(float dt)
        {
            float cold = Mathf.Max(_iceShown, _frozenShown);
            if (!_coldVapor || !_surfaceOk || cold < 0.4f || _coldVaporRate <= 0f) { _vaporAcc = 0f; return; }
            _vaporAcc += dt * _coldVaporRate * cold * UnityEngine.Random.Range(0.5f, 1.5f);
            if (_vaporAcc < 1f) return;
            _vaporAcc -= 1f;
            float S = Scale;
            Vector2 rnd = UnityEngine.Random.insideUnitCircle * _tEdgeMin * 0.8f;
            Vector3 local = _center + _e1 * rnd.x + _e2 * rnd.y;
            if (!Inside(local)) return;
            Vector3 at = transform.position + transform.rotation * (local * S) + Vector3.up * 0.03f * S;
            // холодный воздух: чуть вверх и в сторону, потом сползает вниз через край
            Vector3 vel = new Vector3(UnityEngine.Random.Range(-0.12f, 0.12f), UnityEngine.Random.Range(0.02f, 0.1f),
                                      UnityEngine.Random.Range(-0.12f, 0.12f));
            Color c = new Color(0.86f, 0.94f, 1f);
            CatCooksDustSystem ds = CatCooksDustSystem.Active;
            if (ds != null)
                ds.Emit(CatCooksDustSystem.Effect.Smoke, at, Vector3.up, vel, 0.25f, null, 0.22f * S, 0.3f, c);
            else
                CCRuntimeParticles.Burst(at, Vector3.up, c, Color.white, 2, CCRuntimeParticles.Look.Dust.Scaled(0.5f * S));
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
            float amount = Mathf.Max(agitation, _stirAmt);
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
            EmitWhipBubbles();
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
            float fxk = Mathf.Max(0.1f, _whipFxSpeed * (Time.time < _stirFxUntil ? _stirFxK : 1f));   // большой миксер — медленнее вибратора
            dt *= fxk;                                                           // v18.8: и появляются вдвое чаще
            if (_whipCloud != null) _whipCloud.transform.parent.SetPositionAndRotation(atRaw, Quaternion.identity);

            // v5: редкие крупные капли — вверх и в стороны
            _sprayAcc += dt * _whipDrops * _stirAmt * wet;
            int n = Mathf.Min(4, (int)_sprayAcc);
            _sprayAcc -= n;
            for (int i = 0; i < n; i++)
            {
                Vector2 j = UnityEngine.Random.insideUnitCircle * 0.025f;
                Vector3 off = rot * new Vector3(j.x, 0f, j.y);
                Vector3 rad = off.sqrMagnitude > 1e-8f ? off.normalized : rot * Vector3.right;
                Vector3 vel = _nDynW * UnityEngine.Random.Range(0.9f, 1.8f) + rad * UnityEngine.Random.Range(0.3f, 0.85f) + _velF;
                float size = UnityEngine.Random.Range(_whipDropSize.x, Mathf.Max(_whipDropSize.x, _whipDropSize.y)) * S;
                LiquidFx.Drop(at + off * S, vel, size, c, UnityEngine.Random.Range(0.5f, 0.9f));
            }

            // v7/v8: «возня» — крупные комки-тучки разной формы шапкой у щупа. Если есть туча
            // взбивания (локальная система) — комки живут в ней и едут за щупом целиком; клубятся шумом.
            Vector3 stirVel = Vector3.zero;
            if (_stirPrevOk) stirVel = Vector3.ClampMagnitude((atRaw - _stirPrevW) / Mathf.Max(1e-3f, dt / fxk), 3f);
            _stirPrevW = atRaw; _stirPrevOk = true;
            _stirVelW = Vector3.Lerp(_stirVelW, stirVel, 1f - Mathf.Exp(-dt / 0.06f));
            float follow = _whipPoofFollow;
            float pScale = _whipPoofScale * 1.9f;                            // v9: ещё крупнее
            bool inCloud = _whipCloud != null && follow > 0.01f;
            _puffAcc += dt * _whipDust * _stirAmt * (0.6f + 0.4f * _whipFoam) / Mathf.Max(1f, pScale * 0.5f);
            n = Mathf.Min(16, (int)_puffAcc);
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
            _poofBurstT -= dt * _stirAmt;
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
                                       S * _whipBurstSize * (0.85f + 0.3f * _whipFoam),
                                       cloud ? Vector3.zero : _velF + _stirVelW * (0.7f * _whipPoofFollow), kind,
                                       _whipPoofScale * 1.1f, Mathf.Lerp(1f, 0.45f, _whipPoofFollow), cloud ? _whipCloud : null);
                }
            }

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
