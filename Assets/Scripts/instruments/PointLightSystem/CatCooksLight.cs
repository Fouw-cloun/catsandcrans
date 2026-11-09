using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// КАСТОМНЫЙ ИСТОЧНИК СВЕТА. Обвязка над Unity-светом.
///
/// Своя модель освещения здесь не нужна и была бы вредна: она сломала бы тени,
/// Forward+, запекание и световые пробники. Всё это уже работает — не хватает
/// только УПРАВЛЕНИЯ. Компонент даёт направление, форму конуса, живую
/// анимацию яркости и контроль над стоимостью.
///
/// ═══ ФОРМА ═══
///
/// ВОКРУГ СЕБЯ (Omni) — обычная точка, светит во все стороны. Костёр, лампа
/// под потолком, фонарь на столбе.
///
/// КОНУС (Cone) — прожектор. Угол раствора и мягкость края настраиваются
/// отдельно: широкий мягкий конус это лампа с абажуром, узкий резкий —
/// прожектор на башне.
///
/// Под капотом это Point и Spot соответственно. Переключение мгновенное,
/// потому что Unity не умеет плавно перетекать между типами — но между
/// широким конусом (170°) и точкой разница почти незаметна, поэтому для
/// плавного «раскрытия» держи Cone и анимируй угол.
///
/// ═══ НАПРАВЛЕНИЕ ═══
///
/// СВОЁ         — куда повёрнут объект.
/// НА ЦЕЛЬ      — следит за трансформом. Кран, следящий за котом.
/// ОБЗОР        — качается по дуге сам. Прожектор на вышке.
///
/// ═══ ЖИЗНЬ ═══
///
/// Ровно горящая лампа выглядит мёртвой. Модули анимации складываются:
/// непрерывное мерцание задаёт характер, редкие случайные события — вспышки
/// и провалы — ломают предсказуемость.
///
/// Важно: событие не должно быть симметричным. Настоящая лампа чаще проседает,
/// чем вспыхивает, а вспышка короче провала. Симметричное мерцание читается
/// как анимация по синусу, и глаз это ловит за пару секунд.
///
/// ═══ ЦЕНА ═══
///
/// Сам по себе дополнительный свет в Forward+ дёшев. Дорого стоят ТЕНИ: у
/// каждого теневого источника свой проход отрисовки. Поэтому теней всегда
/// меньше, чем светов, и раздаёт их CatCooksLightDirector по важности.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
[AddComponentMenu("CatCooks/Свет/Custom Light")]
[ExecuteAlways]
public class CatCooksLight : MonoBehaviour
{
    public enum Shape
    {
        /// <summary>Во все стороны. Point.</summary>
        Omni = 0,

        /// <summary>Конусом. Spot.</summary>
        Cone = 1,

        /// <summary>
        /// СОЛНЦЕ. Directional.
        ///
        /// У направленного света нет ни положения, ни дальности, ни конуса —
        /// он светит отовсюду в одну сторону. Поэтому здесь не работают
        /// Range, Cone Angle, отсечение по дистанции и бюджет: солнце нельзя
        /// «выключить ради экономии», без него сцена чёрная.
        ///
        /// Что работает: цвет, яркость, диммер и мерцание. Этого хватает для
        /// смены суток и для затмения при буре.
        /// </summary>
        Sun = 2
    }

    public enum Aim
    {
        /// <summary>Куда повёрнут объект.</summary>
        Self = 0,

        /// <summary>Следить за трансформом.</summary>
        Target = 1,

        /// <summary>Качаться по дуге самостоятельно.</summary>
        Sweep = 2
    }

    /// <summary>
    /// ГОТОВЫЕ ТИПЫ ПРИБОРОВ.
    ///
    /// Пресет задаёт всё разом: форму, дальность, угол, цветовую температуру,
    /// характер мерцания и силу. Дальше можно править что угодно — пресет
    /// переключится в Custom и больше не будет вмешиваться.
    /// </summary>
    public enum Preset
    {
        /// <summary>Ничего не трогать, всё вручную.</summary>
        Custom = 0,

        Candle = 1,          // свеча
        OilLamp = 2,         // керосиновая лампа
        Bulb = 3,            // лампочка накаливания
        Fluorescent = 4,     // лампа дневного света
        WorkLamp = 5,        // рабочий прожектор на стойке
        Floodlight = 6,      // прожектор на башне
        Headlight = 7,       // фара техники
        Campfire = 8,        // костёр
        Welding = 9,         // сварка
        Beacon = 10,         // аварийный маячок
        Sunlight = 11        // солнце
    }

    public enum Flicker
    {
        None = 0,

        /// <summary>Свеча и костёр: мягкий непрерывный шум.</summary>
        Candle = 1,

        /// <summary>Лампа дневного света: почти ровно плюс резкие срывы.</summary>
        Fluorescent = 2,

        /// <summary>Ровная пульсация. Маяк, индикатор.</summary>
        Pulse = 3,

        /// <summary>Строб. Аварийный маячок.</summary>
        Strobe = 4
    }

    // ═════════════════════════ ФОРМА ═════════════════════════

    [Header("━━━ Форма ━━━")]
    [SerializeField] private Shape _shape = Shape.Omni;

    [Tooltip("Угол раствора конуса, градусы. Работает только в режиме Cone.\n\n" +
             "20° — прожектор, 60° — фонарь, 120° — лампа с абажуром, " +
             "170° — почти сфера.")]
    [Range(1f, 179f)][SerializeField] private float _coneAngle = 45f;

    [Tooltip("МЯГКОСТЬ КРАЯ конуса, 0..1.\n\n" +
             "Ноль — резкая граница пятна, как у театрального прибора. " +
             "Единица — плавное угасание к краю.\n\n" +
             "Резкий край почти всегда выглядит дёшево: у настоящих приборов " +
             "он размыт хотя бы немного.")]
    [Range(0f, 1f)][SerializeField] private float _coneSoftness = 0.45f;

    [Tooltip("Дальность в метрах. Из неё же считается затухание, поэтому " +
             "завышать её ради «чтобы добивало» нельзя — свет станет плоским.")]
    [Min(0.1f)][SerializeField] private float _range = 12f;

    // ═════════════════════════ ЦВЕТ ═════════════════════════

    [Header("━━━ Пресет ━━━")]
    [Tooltip("ГОТОВЫЙ ТИП ПРИБОРА. Задаёт форму, дальность, угол, цвет, " +
             "мерцание и силу разом.\n\n" +
             "Как только правишь что-то руками — переключи в Custom, иначе " +
             "пресет перезапишет правку при следующем изменении.")]
    [SerializeField] private Preset _preset = Preset.Custom;

    [Header("━━━ Яркость ━━━")]
    [Tooltip("ЯРКОСТЬ В ЧЕЛОВЕЧЕСКИХ ЕДИНИЦАХ.\n\n" +
             "Включено — задаёшь силу и дистанцию, а четырёхзначное число " +
             "для Unity считается само по квадрату расстояния.\n\n" +
             "Выключено — вводишь Intensity вручную, как в стандартном Light.")]
    [SerializeField] private bool _autoIntensity = true;

    [Tooltip("СИЛА СВЕТА на целевой дистанции.\n\n" +
             "0.5 — едва различимое пятно\n" +
             "2   — уютная лампа\n" +
             "5   — рабочее освещение, читаемое пятно\n" +
             "10  — яркий прожектор\n" +
             "20  — слепящий\n\n" +
             "Число НЕ ЗАВИСИТ от расстояния: одна и та же пятёрка " +
             "выглядит одинаково и на двух метрах, и на двадцати.")]
    [Range(0f, 30f)][SerializeField] private float _brightness = 3f;

    [Tooltip("НА КАКУЮ ДИСТАНЦИЮ рассчитана сила, метры. Обычно это " +
             "расстояние до пола или до того, что нужно осветить.\n\n" +
             "Свет затухает по обратному квадрату, поэтому удвоение " +
             "дистанции требует ЧЕТЫРЁХКРАТНОЙ яркости. Отсюда и берутся " +
             "четырёхзначные числа в Unity.")]
    [Min(0.2f)][SerializeField] private float _targetDistance = 5f;

    [Space(6)]
    [SerializeField] private Color _color = new Color(1f, 0.93f, 0.78f, 1f);

    [Tooltip("Задавать цвет ЦВЕТОВОЙ ТЕМПЕРАТУРОЙ вместо подбора RGB.\n\n" +
             "Так гораздо проще попасть в правдоподобный оттенок: у " +
             "настоящих ламп цвет лежит на узкой кривой, а свободный выбор " +
             "RGB с неё почти всегда сходит.")]
    [SerializeField] private bool _useTemperature = false;

    [Tooltip("Кельвины.\n\n" +
             "1800 — свеча и костёр\n" +
             "2700 — лампочка накаливания\n" +
             "4000 — лампа дневного света\n" +
             "5500 — полдень\n" +
             "7000 — пасмурное небо\n" +
             "9000 — синие сумерки")]
    [Range(1000f, 12000f)][SerializeField] private float _temperature = 3200f;

    [Tooltip("Итоговое число для Unity. Считается из силы и дистанции, если " +
             "включена автояркость.")]
    [Min(0f)][SerializeField] private float _intensity = 75f;

    [Tooltip("ЦВЕТ НА ПРОСАДКЕ. Когда лампа тускнеет, она не просто гаснет — " +
             "она КРАСНЕЕТ: нить остывает.\n\n" +
             "Один этот переход отличает живой огонь от анимированной " +
             "прозрачности.")]
    [SerializeField] private Color _dimColor = new Color(1f, 0.55f, 0.25f, 1f);

    [Tooltip("Насколько сильно цвет уходит в тёплый при просадке")]
    [Range(0f, 1f)][SerializeField] private float _dimColorAmount = 0.6f;

    [Tooltip("Общий выключатель яркости, 0..1. Для плавного включения " +
             "и выключения из кода.")]
    [Range(0f, 1f)][SerializeField] private float _dimmer = 1f;

    // ═════════════════════════ НАПРАВЛЕНИЕ ═════════════════════════

    [Header("━━━ Направление ━━━")]
    [SerializeField] private Aim _aim = Aim.Self;

    [Tooltip("Цель для режима Target")]
    [SerializeField] private Transform _target;

    [Tooltip("Скорость доводки на цель, градусов в секунду. Ноль — мгновенно.\n\n" +
             "Мгновенная доводка выглядит механической: прожектор с " +
             "инерцией читается как управляемый кем-то.")]
    [Min(0f)][SerializeField] private float _turnSpeed = 90f;

    [Header("Обзор (режим Sweep)")]
    [Tooltip("Размах по горизонтали, градусы в каждую сторону")]
    [Range(0f, 180f)][SerializeField] private float _sweepYaw = 60f;

    [Tooltip("Размах по вертикали, градусы")]
    [Range(0f, 80f)][SerializeField] private float _sweepPitch = 10f;

    [Tooltip("Период полного цикла, секунды")]
    [Min(0.1f)][SerializeField] private float _sweepPeriod = 8f;

    [Tooltip("ЗАДЕРЖКА НА КРАЯХ. Ноль — равномерное качание, единица — " +
             "прожектор задерживается по краям дуги и быстро проходит " +
             "середину.\n\n" +
             "Равномерное качание выглядит как механизм на реле. Задержка " +
             "читается как осмотр.")]
    [Range(0f, 1f)][SerializeField] private float _sweepEase = 0.6f;

    [SerializeField] private float _sweepPhase = 0f;

    // ═════════════════════════ МЕРЦАНИЕ ═════════════════════════

    [Header("━━━ Мерцание ━━━")]
    [SerializeField] private Flicker _flicker = Flicker.None;

    [Tooltip("Глубина мерцания. Ноль — не мерцает, единица — гаснет до нуля.")]
    [Range(0f, 1f)][SerializeField] private float _flickerAmount = 0.25f;

    [Tooltip("Скорость. Для свечи 2–4, для лампы дневного света 8–15, для " +
             "строба это частота вспышек в секунду.")]
    [Min(0.01f)][SerializeField] private float _flickerSpeed = 3f;

    [Tooltip("Доля времени, когда строб горит. Только для режима Strobe.")]
    [Range(0.02f, 0.98f)][SerializeField] private float _strobeDuty = 0.15f;

    [Tooltip("ДРОЖАНИЕ ПОЛОЖЕНИЯ, метры. Огонь не только меняет яркость — " +
             "он ещё и смещается. Достаточно пары сантиметров: тени начинают " +
             "жить, и это заметнее, чем сама смена яркости.")]
    [Range(0f, 0.5f)][SerializeField] private float _positionJitter = 0f;

    // ═════════════════════════ СЛУЧАЙНЫЕ СОБЫТИЯ ═════════════════════════

    [Header("━━━ Случайные события ━━━")]
    [Tooltip("Редкие вспышки и провалы поверх постоянного мерцания.\n\n" +
             "Именно они ломают предсказуемость: без них любое мерцание " +
             "через несколько секунд начинает читаться как цикл.")]
    [SerializeField] private bool _randomEvents = false;

    [Tooltip("Провалов в минуту, примерно")]
    [Range(0f, 120f)][SerializeField] private float _dipsPerMinute = 10f;

    [Tooltip("Насколько глубоко проседает: 0.2 значит «до пятой части яркости»")]
    [Range(0f, 1f)][SerializeField] private float _dipDepth = 0.25f;

    [Tooltip("Длительность провала, секунды. Разброс от половины до полной.")]
    [Range(0.02f, 3f)][SerializeField] private float _dipDuration = 0.35f;

    [Space(6)]
    [Tooltip("Вспышек в минуту. Держи РЕЖЕ провалов: настоящая лампа чаще " +
             "проседает, чем вспыхивает, и симметрия сразу читается как " +
             "анимация.")]
    [Range(0f, 120f)][SerializeField] private float _surgesPerMinute = 3f;

    [Tooltip("Во сколько раз ярче на вспышке")]
    [Range(1f, 4f)][SerializeField] private float _surgeStrength = 1.6f;

    [Tooltip("Длительность вспышки. Короче провала — так честнее.")]
    [Range(0.02f, 2f)][SerializeField] private float _surgeDuration = 0.12f;

    [Space(6)]
    [Tooltip("ПОЛНЫЙ ОТКАЗ. Лампа выключается целиком на несколько секунд и " +
             "снова разгорается. Для аварийного освещения на стройке.")]
    [Range(0f, 30f)][SerializeField] private float _blackoutsPerMinute = 0f;

    [Range(0.05f, 8f)][SerializeField] private float _blackoutDuration = 1.2f;

    // ═════════════════════════ ТЕНИ И ЦЕНА ═════════════════════════

    [Header("━━━ Тени ━━━")]
    [Tooltip("Хочет ли источник тени. Окончательное решение принимает " +
             "режиссёр: теней всегда меньше, чем светов.")]
    [SerializeField] private bool _wantsShadows = false;

    [SerializeField] private LightShadows _shadowType = LightShadows.Soft;

    [Tooltip("Дальше этой дистанции тени снимаются даже у важных источников. " +
             "Ноль — не снимать.")]
    [Min(0f)][SerializeField] private float _shadowDistance = 25f;

    [Range(0f, 2f)][SerializeField] private float _shadowStrength = 0.85f;
    [Min(0f)][SerializeField] private float _shadowBias = 0.05f;
    [Min(0f)][SerializeField] private float _shadowNormalBias = 0.4f;

    [Header("━━━ Дальность и цена ━━━")]
    [Tooltip("ВАЖНОСТЬ. Чем больше, тем позже источник отключат при нехватке " +
             "бюджета и тем раньше он получит тень.\n\n" +
             "Ставь высокую фонарю игрока и низкую — декоративным лампочкам " +
             "на заднем плане.")]
    [Range(0f, 10f)][SerializeField] private float _priority = 1f;

    [Tooltip("Дальше этого расстояния до камеры источник гасится. Ноль — " +
             "считать по дальности света автоматически.")]
    [Min(0f)][SerializeField] private float _cullDistance = 0f;

    [Tooltip("Гасить ПЛАВНО у границы отсечения. Без этого источники " +
             "включаются щелчком, и это заметнее, чем сам факт отсечения.")]
    [Range(0f, 0.6f)][SerializeField] private float _cullFadeBand = 0.25f;

    [Tooltip("НИКОГДА не отключать этот источник. Для фонаря игрока и " +
             "сюжетных ламп.")]
    [SerializeField] private bool _neverCull = false;

    // ═════════════════════════ СВЯЗАННАЯ ГЕОМЕТРИЯ ═════════════════════════

    [Header("━━━ Лампа ━━━")]
    [Tooltip("Рендерер самой лампочки. Его свечение синхронизируется с " +
             "яркостью света.\n\n" +
             "Без этого получается странное: свет мигает, а лампа горит " +
             "ровно. Глаз ловит несоответствие мгновенно.")]
    [SerializeField] private Renderer _bulbRenderer;

    [SerializeField] private string _emissionProperty = "_EmissionColor";

    [Tooltip("Множитель свечения лампочки относительно яркости света")]
    [Min(0f)][SerializeField] private float _bulbEmission = 2f;

    // ───────────────────────── состояние ─────────────────────────

    private static readonly List<CatCooksLight> All = new List<CatCooksLight>();

    /// <summary>Все включённые источники. Читает режиссёр.</summary>
    public static IReadOnlyList<CatCooksLight> Registry => All;

    private Light _light;
    private Vector3 _basePosition;
    private Quaternion _baseRotation;

    private float _noiseSeed;
    private float _eventValue = 1f;      // множитель от случайного события
    private float _eventUntil;
    private float _eventFrom = 1f, _eventTo = 1f;
    private float _eventStart;
    private float _nextDip, _nextSurge, _nextBlackout;

    private float _cullFade = 1f;
    private bool _allowedByDirector = true;
    private bool _shadowsAllowedByDirector = true;

    private MaterialPropertyBlock _mpb;
    private int _emissionId;

    private float _currentValue = 1f;

    /// <summary>Итоговая яркость этого кадра с учётом всего.</summary>
    public float CurrentIntensity => _currentValue;

    public float Priority => _priority;
    public bool NeverCull => _neverCull;
    public bool WantsShadows => _wantsShadows;
    public float Range => _range;
    public float BaseIntensity => _intensity;

    /// <summary>Расстояние, дальше которого источник не нужен.</summary>
    public float EffectiveCullDistance => _shape == Shape.Sun
        ? float.MaxValue
        : (_cullDistance > 0.01f ? _cullDistance : _range * 2.5f);

    /// <summary>Солнце нельзя отключать бюджетом ни при каких условиях.</summary>
    public bool IsSun => _shape == Shape.Sun;

    // ───────────────────────── жизненный цикл ─────────────────────────

    private void OnEnable()
    {
        _light = GetComponent<Light>();
        _noiseSeed = Random.value * 500f;

        GuardDirectional();

        _basePosition = transform.localPosition;
        _baseRotation = transform.localRotation;

        _emissionId = Shader.PropertyToID(_emissionProperty);
        if (_bulbRenderer != null) _mpb = new MaterialPropertyBlock();

        ScheduleAll();
        ApplyStatic();

        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
        if (_light != null) _light.enabled = false;
    }

    /// <summary>
    /// ЗАЩИТА ОТ УБИЙСТВА СОЛНЦА.
    ///
    /// Компонент переписывает тип света каждый кадр. Если его повесить на
    /// Directional Light в форме Omni или Cone, солнце превратится в
    /// точечный источник с дальностью двенадцать метров, и сцена станет
    /// чёрной. Внешне это выглядит как «свет не работает», хотя работает всё
    /// ровно так, как написано.
    ///
    /// Молча переключаемся в форму Sun и говорим об этом вслух.
    /// </summary>
    private void GuardDirectional()
    {
        if (_light == null || _shape == Shape.Sun) return;
        if (_light.type != LightType.Directional) return;

        _shape = Shape.Sun;
        _neverCull = true;

        Debug.LogWarning(
            $"[CatCooksLight] На «{name}» стоит Directional Light, а форма была " +
            $"задана как {(_shape == Shape.Cone ? "Cone" : "Omni")}. Это превратило бы " +
            "солнце в точечный источник и погасило сцену.\n\n" +
            "Форма переключена на Sun. Если это НЕ солнце, а обычная лампа — " +
            "смени тип у самого компонента Light на Point или Spot.", this);
    }

    private void ScheduleAll()
    {
        _nextDip = NextEventTime(_dipsPerMinute);
        _nextSurge = NextEventTime(_surgesPerMinute);
        _nextBlackout = NextEventTime(_blackoutsPerMinute);
    }

    /// <summary>
    /// Когда произойдёт следующее событие.
    ///
    /// Экспоненциальное распределение, а не фиксированный интервал: у
    /// равномерных пауз слышен ритм, и мерцание превращается в метроном.
    /// Здесь же интервалы естественно неравные — два провала подряд возможны,
    /// и именно это делает лампу неисправной, а не запрограммированной.
    /// </summary>
    private float NextEventTime(float perMinute)
    {
        if (perMinute <= 0.001f) return float.MaxValue;

        float mean = 60f / perMinute;
        float u = Mathf.Clamp(Random.value, 0.0001f, 0.9999f);

        return Time.time - Mathf.Log(u) * mean;
    }

    // ───────────────────────── кадр ─────────────────────────

    private void LateUpdate()
    {
        if (_light == null) _light = GetComponent<Light>();
        if (_light == null) return;

        if (!Application.isPlaying)
        {
            ApplyStatic();
            _light.intensity = _intensity * _dimmer;
            _light.color = _color;
            _light.enabled = true;
            return;
        }

        UpdateAim();

        float flick = EvaluateFlicker();
        float events = EvaluateEvents();

        // Общий множитель режиссёра — последним. Без режиссёра он равен
        // единице, поэтому компонент полностью работоспособен в одиночку.
        _currentValue = flick * events * _dimmer * _cullFade
                      * CatCooksLightDirector.Global;

        ApplyToLight();
        ApplyToBulb();
    }

    /// <summary>Статические настройки — тип, углы, тени. Их не нужно
    /// трогать каждый кадр, только при сборке и смене формы.</summary>
    private void ApplyStatic()
    {
        if (_light == null) return;

        // СОЛНЦЕ трогаем осторожно: у него нет ни дальности, ни конуса, и
        // запись этих полей ничего не даст, а вот смена типа — угробит сцену.
        if (_shape == Shape.Sun)
        {
            _light.type = LightType.Directional;

            _light.shadowStrength = Mathf.Clamp01(_shadowStrength);
            _light.shadowBias = _shadowBias;
            _light.shadowNormalBias = _shadowNormalBias;
            return;
        }

        _light.type = _shape == Shape.Cone ? LightType.Spot : LightType.Point;
        _light.range = _range;

        if (_shape == Shape.Cone)
        {
            _light.spotAngle = _coneAngle;

            // Внутренний угол задаёт мягкость: чем он ближе к внешнему, тем
            // резче край пятна. Единица мягкости — внутренний угол в нуле.
            _light.innerSpotAngle = _coneAngle * (1f - _coneSoftness);
        }

        _light.shadowStrength = Mathf.Clamp01(_shadowStrength);
        _light.shadowBias = _shadowBias;
        _light.shadowNormalBias = _shadowNormalBias;

        _light.renderMode = LightRenderMode.Auto;
    }

    private void UpdateAim()
    {
        switch (_aim)
        {
            case Aim.Target:
            {
                if (_target == null) break;

                Vector3 dir = _target.position - transform.position;
                if (dir.sqrMagnitude < 1e-6f) break;

                Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);

                // Инерция доводки. Мгновенный поворот читается как механизм,
                // плавный — как управляемый прожектор.
                transform.rotation = _turnSpeed <= 0.01f
                    ? want
                    : Quaternion.RotateTowards(transform.rotation, want,
                                               _turnSpeed * Time.deltaTime);
                break;
            }

            case Aim.Sweep:
            {
                float t = (Time.time / Mathf.Max(0.1f, _sweepPeriod) + _sweepPhase) % 1f;

                // Треугольная волна: качание туда-обратно.
                float tri = t < 0.5f ? t * 2f : 2f - t * 2f;

                // Задержка на краях. Ровное качание выглядит как реле;
                // задержка читается как осмотр местности.
                float eased = Mathf.SmoothStep(0f, 1f, tri);
                float s = Mathf.Lerp(tri, eased, _sweepEase) * 2f - 1f;

                // Вертикаль идёт вдвое чаще горизонтали: строго синхронный
                // ход по обеим осям даёт прямую линию на стене.
                float p = Mathf.Sin((Time.time / Mathf.Max(0.1f, _sweepPeriod)) *
                                    Mathf.PI * 4f + _sweepPhase * 6.283f);

                transform.localRotation = _baseRotation *
                    Quaternion.Euler(p * _sweepPitch, s * _sweepYaw, 0f);
                break;
            }
        }

        // Дрожание положения. Пара сантиметров заставляет двигаться ТЕНИ, и
        // это заметнее любой смены яркости.
        if (_positionJitter > 0.0001f)
        {
            float t = Time.time * _flickerSpeed;

            Vector3 j = new Vector3(
                Mathf.PerlinNoise(_noiseSeed + t, 0.1f) - 0.5f,
                Mathf.PerlinNoise(_noiseSeed + t, 3.7f) - 0.5f,
                Mathf.PerlinNoise(_noiseSeed + t, 7.3f) - 0.5f);

            transform.localPosition = _basePosition + j * (_positionJitter * 2f);
        }
    }

    private float EvaluateFlicker()
    {
        if (_flicker == Flicker.None || _flickerAmount < 0.001f) return 1f;

        float t = Time.time * _flickerSpeed;
        float v;

        switch (_flicker)
        {
            case Flicker.Candle:
                // Две октавы шума: медленное дыхание плюс быстрая рябь.
                // Одна октава даёт слишком гладкое колебание, которое
                // читается как синус.
                v = Mathf.PerlinNoise(_noiseSeed + t, 0f) * 0.65f
                  + Mathf.PerlinNoise(_noiseSeed + t * 2.7f, 5f) * 0.35f;
                break;

            case Flicker.Fluorescent:
            {
                // Почти всегда горит ровно, изредка резко срывается. Порог по
                // шуму даёт именно такое поведение: перепад мгновенный, а не
                // плавный.
                float n = Mathf.PerlinNoise(_noiseSeed + t, 1.3f);
                v = n > 0.72f ? Random.value * 0.5f : 1f;

                // Плюс лёгкий гул на удвоенной частоте сети.
                v *= 0.97f + 0.03f * Mathf.Sin(Time.time * 100f);
                break;
            }

            case Flicker.Pulse:
                v = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f);
                break;

            case Flicker.Strobe:
                v = (t % 1f) < _strobeDuty ? 1f : 0f;
                break;

            default:
                v = 1f;
                break;
        }

        return Mathf.Lerp(1f, v, _flickerAmount);
    }

    private float EvaluateEvents()
    {
        if (!_randomEvents) return 1f;

        float now = Time.time;

        if (now >= _nextBlackout)
        {
            StartEvent(0f, _blackoutDuration * Random.Range(0.7f, 1.3f));
            _nextBlackout = NextEventTime(_blackoutsPerMinute);
        }
        else if (now >= _nextDip)
        {
            StartEvent(_dipDepth * Random.Range(0.6f, 1.2f),
                       _dipDuration * Random.Range(0.5f, 1f));
            _nextDip = NextEventTime(_dipsPerMinute);
        }
        else if (now >= _nextSurge)
        {
            StartEvent(_surgeStrength * Random.Range(0.85f, 1.15f),
                       _surgeDuration * Random.Range(0.5f, 1f));
            _nextSurge = NextEventTime(_surgesPerMinute);
        }

        if (now >= _eventUntil) { _eventValue = 1f; return 1f; }

        // Форма события: быстрый заход, медленный выход. Симметричная форма
        // читается как анимация; несимметричная — как физический процесс,
        // потому что нить накала остывает дольше, чем нагревается.
        float span = Mathf.Max(0.001f, _eventUntil - _eventStart);
        float k = (now - _eventStart) / span;

        float shape = k < 0.15f
            ? k / 0.15f
            : 1f - Mathf.Pow((k - 0.15f) / 0.85f, 0.6f);

        _eventValue = Mathf.Lerp(_eventFrom, _eventTo, Mathf.Clamp01(shape));
        return _eventValue;
    }

    private void StartEvent(float to, float duration)
    {
        _eventFrom = 1f;
        _eventTo = to;
        _eventStart = Time.time;
        _eventUntil = Time.time + Mathf.Max(0.02f, duration);
    }

    private void ApplyToLight()
    {
        bool on = _allowedByDirector && _currentValue > 0.002f;

        _light.enabled = on;
        if (!on) return;

        _light.intensity = _intensity * _currentValue;

        // ЦВЕТ НА ПРОСАДКЕ. Тускнеющая лампа краснеет: нить остывает. Один
        // этот переход отличает живой огонь от анимированной прозрачности.
        float dim = 1f - Mathf.Clamp01(_currentValue);
        _light.color = Color.Lerp(_color, _dimColor, dim * _dimColorAmount);

        bool shadows = _wantsShadows && _shadowsAllowedByDirector;
        _light.shadows = shadows ? _shadowType : LightShadows.None;
    }

    private void ApplyToBulb()
    {
        if (_bulbRenderer == null || _mpb == null) return;

        _bulbRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_emissionId, _light.color * (_currentValue * _bulbEmission));
        _bulbRenderer.SetPropertyBlock(_mpb);
    }

    // ───────────────────── интерфейс для режиссёра ─────────────────────

    /// <summary>Вызывает CatCooksLightDirector. Руками не трогать.</summary>
    public void SetBudget(bool allowed, bool shadowsAllowed, float distance)
    {
        _allowedByDirector = allowed || _shape == Shape.Sun;
        _shadowsAllowedByDirector = shadowsAllowed;

        if (_shadowDistance > 0.01f && distance > _shadowDistance)
            _shadowsAllowedByDirector = false;

        // Плавное гашение у границы. Без него источники включаются щелчком,
        // и это заметнее самого факта отсечения.
        float cull = EffectiveCullDistance;
        float band = cull * Mathf.Max(0.01f, _cullFadeBand);

        _cullFade = (_neverCull || _shape == Shape.Sun)
            ? 1f
            : 1f - Mathf.Clamp01((distance - (cull - band)) / band);
    }

    // ───────────────────── управление из игры ─────────────────────

    /// <summary>Плавное включение и выключение. Крути каждый кадр, это дёшево.</summary>
    public void SetDimmer(float value01) => _dimmer = Mathf.Clamp01(value01);

    public void SetColor(Color c) { _color = c; }

    public void SetIntensity(float value) { _intensity = Mathf.Max(0f, value); }

    /// <summary>Сменить форму. Перестраивает Unity-свет, поэтому не в каждом
    /// кадре.</summary>
    public void SetShape(Shape shape, float coneAngle = -1f)
    {
        _shape = shape;
        if (coneAngle > 0f) _coneAngle = Mathf.Clamp(coneAngle, 1f, 179f);
        ApplyStatic();
    }

    /// <summary>Угол конуса. Дёшево, можно анимировать: так делается
    /// «раскрытие» луча прожектора.</summary>
    public void SetConeAngle(float degrees)
    {
        _coneAngle = Mathf.Clamp(degrees, 1f, 179f);
        if (_shape != Shape.Cone || _light == null) return;

        _light.spotAngle = _coneAngle;
        _light.innerSpotAngle = _coneAngle * (1f - _coneSoftness);
    }

    public void SetTarget(Transform target)
    {
        _target = target;
        _aim = target != null ? Aim.Target : Aim.Self;
    }

    public void SetAim(Aim aim) => _aim = aim;

    /// <summary>Разовая вспышка. Для искр сварки, вспышки камеры, удара.</summary>
    public void Flash(float strength = 2f, float seconds = 0.12f)
        => StartEvent(Mathf.Max(1f, strength), seconds);

    /// <summary>Разовый провал. Для реакции на событие: тряхнуло, моргнуло.</summary>
    public void Dip(float depth01 = 0.2f, float seconds = 0.3f)
        => StartEvent(Mathf.Clamp01(depth01), seconds);



    // ═════════════════════════ ПРЕСЕТЫ ═════════════════════════

    private Preset _lastPreset = Preset.Custom;

    /// <summary>
    /// ПЕРЕСЧЁТ ЯРКОСТИ ИЗ СИЛЫ И ДИСТАНЦИИ.
    ///
    /// intensity = сила × дистанция²
    ///
    /// Прямое следствие обратного квадрата: чтобы на вдвое большем
    /// расстоянии светило так же, нужно вчетверо больше. Именно поэтому
    /// в Unity для прожектора на двадцати метрах приходится вводить
    /// четырёхзначные числа — и именно поэтому вводить их руками не надо.
    /// </summary>
    private void RecomputeIntensity()
    {
        if (!_autoIntensity) return;

        float d = Mathf.Max(0.2f, _targetDistance);
        _intensity = _brightness * d * d;
    }

    private void RecomputeColor()
    {
        if (!_useTemperature) return;

        // Цветовая температура вместо свободного RGB: у настоящих ламп цвет
        // лежит на узкой кривой Планка, и свободный подбор с неё почти
        // всегда сходит в сторону.
        _color = Mathf.CorrelatedColorTemperatureToRGB(_temperature);
    }

    /// <summary>
    /// Применить пресет. Задаёт всё разом, включая характер мерцания.
    ///
    /// Дальность у каждого прибора намеренно БОЛЬШЕ целевой дистанции: если
    /// они совпадают, свет работает на самом краю радиуса, где URP
    /// дополнительно гасит его сглаживанием к границе.
    /// </summary>
    public void ApplyPreset(Preset preset)
    {
        _preset = preset;
        _lastPreset = preset;

        if (preset == Preset.Custom) return;

        _autoIntensity = true;
        _useTemperature = true;
        _randomEvents = false;
        _positionJitter = 0f;
        _flicker = Flicker.None;
        _flickerAmount = 0f;
        _wantsShadows = false;
        _aim = Aim.Self;

        switch (preset)
        {
            case Preset.Candle:
                _shape = Shape.Omni;
                _brightness = 0.7f;  _targetDistance = 1.5f;
                _range = 5f;         _temperature = 1850f;
                _flicker = Flicker.Candle; _flickerAmount = 0.35f; _flickerSpeed = 3.5f;
                _positionJitter = 0.02f;
                _priority = 0.5f;
                break;

            case Preset.OilLamp:
                _shape = Shape.Omni;
                _brightness = 1.4f;  _targetDistance = 2.5f;
                _range = 8f;         _temperature = 2200f;
                _flicker = Flicker.Candle; _flickerAmount = 0.2f; _flickerSpeed = 2.5f;
                _positionJitter = 0.012f;
                _priority = 1f;
                break;

            case Preset.Bulb:
                _shape = Shape.Omni;
                _brightness = 2.5f;  _targetDistance = 4f;
                _range = 12f;        _temperature = 2700f;
                _priority = 1f;
                break;

            case Preset.Fluorescent:
                _shape = Shape.Cone;
                _coneAngle = 130f;   _coneSoftness = 0.7f;
                _brightness = 3.5f;  _targetDistance = 5f;
                _range = 14f;        _temperature = 4200f;
                _flicker = Flicker.Fluorescent; _flickerAmount = 0.35f; _flickerSpeed = 9f;
                _randomEvents = true;
                _dipsPerMinute = 14f; _surgesPerMinute = 3f;
                _priority = 1.5f;
                break;

            case Preset.WorkLamp:
                _shape = Shape.Cone;
                _coneAngle = 75f;    _coneSoftness = 0.45f;
                _brightness = 6f;    _targetDistance = 8f;
                _range = 22f;        _temperature = 3600f;
                _wantsShadows = true;
                _priority = 3f;
                break;

            case Preset.Floodlight:
                _shape = Shape.Cone;
                _coneAngle = 28f;    _coneSoftness = 0.3f;
                _brightness = 9f;    _targetDistance = 20f;
                _range = 50f;        _temperature = 5200f;
                _wantsShadows = true;
                _priority = 5f;
                break;

            case Preset.Headlight:
                _shape = Shape.Cone;
                _coneAngle = 42f;    _coneSoftness = 0.35f;
                _brightness = 6f;    _targetDistance = 12f;
                _range = 32f;        _temperature = 4500f;
                _priority = 4f;
                break;

            case Preset.Campfire:
                _shape = Shape.Omni;
                _brightness = 3.5f;  _targetDistance = 3f;
                _range = 11f;        _temperature = 1700f;
                _flicker = Flicker.Candle; _flickerAmount = 0.45f; _flickerSpeed = 4.5f;
                _positionJitter = 0.05f;
                _randomEvents = true;
                _dipsPerMinute = 18f; _surgesPerMinute = 10f; _surgeStrength = 1.5f;
                _priority = 2f;
                break;

            case Preset.Welding:
                // Сварка почти не горит ровно: это череда вспышек. Поэтому
                // базовая сила низкая, а всё видимое приходит от событий.
                _shape = Shape.Omni;
                _brightness = 1.5f;  _targetDistance = 4f;
                _range = 16f;        _temperature = 7500f;
                _flicker = Flicker.Fluorescent; _flickerAmount = 0.8f; _flickerSpeed = 22f;
                _randomEvents = true;
                _dipsPerMinute = 40f; _dipDepth = 0.05f; _dipDuration = 0.2f;
                _surgesPerMinute = 60f; _surgeStrength = 3.5f; _surgeDuration = 0.06f;
                _priority = 3f;
                break;

            case Preset.Beacon:
                _shape = Shape.Cone;
                _coneAngle = 95f;    _coneSoftness = 0.6f;
                _brightness = 5f;    _targetDistance = 6f;
                _range = 18f;
                _useTemperature = false;
                _color = new Color(1f, 0.35f, 0.15f, 1f);
                _dimColor = new Color(1f, 0.2f, 0.1f, 1f);
                _flicker = Flicker.Strobe; _flickerAmount = 1f; _flickerSpeed = 1.2f;
                _strobeDuty = 0.22f;
                _priority = 2f;
                break;

            case Preset.Sunlight:
                _shape = Shape.Sun;
                _autoIntensity = false;
                _intensity = 1.6f;
                _temperature = 5600f;
                _neverCull = true;
                _wantsShadows = true;
                _priority = 10f;
                break;
        }

        RecomputeColor();
        RecomputeIntensity();
        ApplyStatic();
    }

    [ContextMenu("★ Применить пресет заново")]
    private void EditorApplyPreset() => ApplyPreset(_preset);

    /// <summary>
    /// Задать силу и дистанцию из кода. Дёшево, можно анимировать: так
    /// делается разгорающийся прожектор.
    /// </summary>
    public void SetBrightness(float brightness, float targetDistance = -1f)
    {
        _autoIntensity = true;
        _brightness = Mathf.Max(0f, brightness);
        if (targetDistance > 0f) _targetDistance = targetDistance;
        RecomputeIntensity();
    }

    /// <summary>Цветовая температура в кельвинах.</summary>
    public void SetTemperature(float kelvin)
    {
        _useTemperature = true;
        _temperature = Mathf.Clamp(kelvin, 1000f, 12000f);
        RecomputeColor();
    }

    // ═════════════════════════ ДИАГНОСТИКА ═════════════════════════

    /// <summary>
    /// ПОЧЕМУ НЕ СВЕТИТ.
    ///
    /// Правой кнопкой по компоненту → «★ ПОЧЕМУ НЕ СВЕТИТ».
    ///
    /// Проверяет всю цепочку от настроек проекта до геометрии сцены и
    /// печатает разбор. Причин, по которым источник не виден, штук десять, и
    /// девять из них снаружи выглядят одинаково — как «скрипт не работает».
    /// </summary>
    [ContextMenu("★ ПОЧЕМУ НЕ СВЕТИТ")]
    public void Diagnose()
    {
        StringBuilder sb = new StringBuilder();
        int problems = 0;

        sb.AppendLine($"═══ ДИАГНОСТИКА СВЕТА: {name} ═══");

        // ── 1. Компонент ──
        Light l = GetComponent<Light>();
        if (l == null)
        {
            Debug.LogError($"[{name}] Нет компонента Light вообще. " +
                           "Add Component → Light.", this);
            return;
        }

        sb.AppendLine($"\nТИП: {l.type}   форма в скрипте: {_shape}");
        sb.AppendLine($"Unity Light.enabled: {l.enabled}");
        sb.AppendLine($"Пресет: {_preset}");

        if (_autoIntensity)
            sb.AppendLine($"Яркость: СИЛА {_brightness:0.##} на {_targetDistance:0.#} м " +
                          $"→ Intensity {_intensity:0} (сейчас на свете {l.intensity:0})");
        else
            sb.AppendLine($"Яркость: вручную {_intensity:0.##} " +
                          $"(сейчас на свете {l.intensity:0.00})");
        sb.AppendLine($"Дальность: {l.range} м   Цвет: {l.color}");

        // ── 2. Режим запекания ──
        // Самая обидная причина: свет стоит в Baked, лайтмапы не запечены,
        // и он не рисуется вообще. Ошибок при этом нигде нет.
#if UNITY_EDITOR
        if (l.lightmapBakeType == LightmapBakeType.Baked)
        {
            sb.AppendLine("\n❌ РЕЖИМ СВЕТА = BAKED.");
            sb.AppendLine("   Запечённый свет НЕ рисуется в реальном времени. " +
                          "Пока лайтмапы не запечены — его нет вообще.");
            sb.AppendLine("   ЛЕЧЕНИЕ: в компоненте Light поставь Mode = Realtime.");
            problems++;
        }
#endif

        // ── 3. Яркость и цвет ──
        if (_intensity <= 0.001f)
        {
            sb.AppendLine("\n❌ Базовая яркость (Intensity) равна нулю.");
            problems++;
        }

        if (_dimmer <= 0.001f)
        {
            sb.AppendLine("\n❌ Dimmer в нуле — свет намеренно погашен из скрипта.");
            problems++;
        }

        if (_color.maxColorComponent <= 0.001f)
        {
            sb.AppendLine("\n❌ Цвет чёрный. Свет горит, но не несёт яркости.");
            problems++;
        }

        // ── 4. Разбор итогового множителя ──
        if (Application.isPlaying)
        {
            sb.AppendLine("\n── ИЗ ЧЕГО СЛОЖИЛАСЬ ЯРКОСТЬ ──");
            sb.AppendLine($"   мерцание      × {EvaluateFlicker():0.000}");
            sb.AppendLine($"   события       × {_eventValue:0.000}");
            sb.AppendLine($"   диммер        × {_dimmer:0.000}");
            sb.AppendLine($"   гашение вдали × {_cullFade:0.000}");
            sb.AppendLine($"   общая яркость × {CatCooksLightDirector.Global:0.000}");
            sb.AppendLine($"   ИТОГО         = {_currentValue:0.000}");

            if (_cullFade <= 0.01f)
            {
                sb.AppendLine("\n❌ ГАШЕНИЕ ВДАЛИ СЪЕЛО СВЕТ ПОЛНОСТЬЮ.");
                sb.AppendLine($"   Источник дальше от ИГРОВОЙ камеры, чем " +
                              $"{EffectiveCullDistance:0.0} м.");
                sb.AppendLine("   Это считается от Camera.main, а НЕ от камеры окна Scene — " +
                              "поэтому в редакторе свет виден, а в игре нет, или наоборот.");
                sb.AppendLine("   ЛЕЧЕНИЕ: подними Cull Distance, или Range, " +
                              "или поставь галку Never Cull.");
                problems++;
            }

            if (!_allowedByDirector)
            {
                sb.AppendLine("\n❌ РЕЖИССЁР ОТКЛЮЧИЛ ЭТОТ ИСТОЧНИК — не хватило бюджета.");
                sb.AppendLine("   ЛЕЧЕНИЕ: подними Light Budget, или Priority у этого " +
                              "источника, или поставь Never Cull.");
                problems++;
            }

            if (CatCooksLightDirector.Global <= 0.001f)
            {
                sb.AppendLine("\n❌ Общая яркость режиссёра в нуле — погашено ВСЁ разом.");
                problems++;
            }
        }
        else
        {
            sb.AppendLine("\n(Режим редактора: мерцание и бюджет не считаются, " +
                          "свет горит на базовой яркости)");
        }

        // ── 5. Настройки URP ──
        // Самая частая причина после запекания: в URP-ассете дополнительные
        // источники просто выключены. Тогда не работает ни один Point и Spot
        // во всём проекте, что бы ты с ними ни делал.
        UniversalRenderPipelineAsset urp =
            GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        if (urp == null)
        {
            sb.AppendLine("\n❌ Активный пайплайн — НЕ URP. Шейдеры проекта его ждут.");
            problems++;
        }
        else if (_shape != Shape.Sun)
        {
            sb.AppendLine($"\n── URP-АССЕТ: {urp.name} ──");
            sb.AppendLine($"   Additional Lights: {urp.additionalLightsRenderingMode}");
            sb.AppendLine($"   Лимит на объект: {urp.maxAdditionalLightsCount}");
            sb.AppendLine($"   Тени доп. источников: {urp.supportsAdditionalLightShadows}");

            if (urp.additionalLightsRenderingMode == LightRenderingMode.Disabled)
            {
                sb.AppendLine("\n❌❌ ДОПОЛНИТЕЛЬНЫЕ ИСТОЧНИКИ ВЫКЛЮЧЕНЫ В URP-АССЕТЕ.");
                sb.AppendLine("   Ни один Point и Spot в проекте не светит. " +
                              "Скрипты тут ни при чём.");
                sb.AppendLine("   ЛЕЧЕНИЕ: URP-ассет → Lighting → Additional Lights → " +
                              "Per Pixel.");
                problems++;
            }
            else if (urp.additionalLightsRenderingMode == LightRenderingMode.PerVertex)
            {
                sb.AppendLine("\n⚠ Additional Lights = Per Vertex.");
                sb.AppendLine("   Свет считается только в вершинах. На крупных плоских " +
                              "полигонах земли это выглядит как полное отсутствие света.");
                sb.AppendLine("   ЛЕЧЕНИЕ: поставь Per Pixel.");
                problems++;
            }
        }

        // ── 6. Слои рендеринга ──
        if (l.renderingLayerMask != 1u && l.renderingLayerMask != uint.MaxValue)
        {
            sb.AppendLine($"\n⚠ Rendering Layer Mask = {l.renderingLayerMask}, не по умолчанию.");
            sb.AppendLine("   Источник освещает только объекты со совпадающим слоем. " +
                          "Если у геометрии слой другой — света на ней не будет.");
            problems++;
        }

        // ── 7. Достаёт ли свет до геометрии ──
        // Дальность у точечного света это не «яркость», а физическая граница:
        // за ней освещённость строго ноль. Лампа в двадцати метрах над землёй
        // с дальностью двенадцать не осветит НИЧЕГО.
        if (_shape != Shape.Sun)
        {
            Vector3 dir = _shape == Shape.Cone ? transform.forward : Vector3.down;

            if (Physics.Raycast(transform.position, dir, out RaycastHit hit, _range * 5f))
            {
                sb.AppendLine($"\n── ГЕОМЕТРИЯ ──");
                sb.AppendLine($"   Ближайшая поверхность по направлению света: " +
                              $"{hit.distance:0.0} м ({hit.collider.name})");

                if (hit.distance > _range)
                {
                    sb.AppendLine($"\n❌ ДАЛЬНОСТИ НЕ ХВАТАЕТ.");
                    sb.AppendLine($"   Range = {_range} м, а до поверхности " +
                                  $"{hit.distance:0.0} м. За границей дальности " +
                                  "освещённость СТРОГО НОЛЬ — это не затухание, " +
                                  "а обрыв.");
                    sb.AppendLine($"   ЛЕЧЕНИЕ: Range не меньше {hit.distance * 1.4f:0} м, " +
                                  "либо опусти источник ниже.");
                    problems++;
                }

                // ── ХВАТАЕТ ЛИ ЯРКОСТИ НА ЭТУ ДИСТАНЦИЮ ──
                //
                // Точечный и конусный свет затухают по ОБРАТНОМУ КВАДРАТУ
                // расстояния. Это не настройка и не стиль — так устроен
                // физический закон, и URP ему следует.
                //
                // Практический смысл: единица яркости, которой хватает лампе
                // в двух метрах над столом, на одиннадцати метрах даёт
                // 1/121 ≈ 0.008 — неотличимо от нуля. Свет при этом честно
                // горит, честно попадает в шейдер и честно даёт ничего.
                //
                // Это самая частая причина «свет не работает», и снаружи она
                // выглядит ровно как поломка.
                float d2 = Mathf.Max(0.01f, hit.distance * hit.distance);
                float illum = _intensity / d2;

                sb.AppendLine($"   Освещённость на поверхности: {illum:0.0000} " +
                              $"(яркость {_intensity} / расстояние² {d2:0.0})");

                if (_autoIntensity && Mathf.Abs(_targetDistance - hit.distance) > hit.distance * 0.4f)
                {
                    sb.AppendLine($"\n⚠ Целевая дистанция {_targetDistance:0.0} м не " +
                                  $"совпадает с реальной {hit.distance:0.0} м.");
                    sb.AppendLine($"   Поставь Target Distance = {hit.distance:0.0}, " +
                                  "и сила света начнёт означать то, что написано.");
                }

                // ── ТОЧНЫЙ РАСЧЁТ ПО ФОРМУЛЕ URP ──
                //
                // URP:  атт = (1/d²) × (1 − (d²/r²)²)²
                //
                // Второй множитель — оконная функция, которая доводит свет до
                // нуля ровно на границе дальности. Она наказывает за работу у
                // самого края радиуса: при d, близком к r, теряется ещё в
                // несколько раз. Именно поэтому «увеличить Range» помогает
                // даже тогда, когда свет и так до поверхности доставал.
                float rSqr = Mathf.Max(0.01f, _range * _range);
                float factor = d2 / rSqr;
                float window = Mathf.Max(0f, 1f - factor * factor);
                window *= window;

                float attenuation = window / d2;
                float linear = _intensity * attenuation;

                sb.AppendLine($"\n── СКОЛЬКО РЕАЛЬНО ДОЙДЁТ ──");
                sb.AppendLine($"   1/d²                    = {1f / d2:0.00000}");
                sb.AppendLine($"   окно дальности (d/r)    = {window:0.00000}" +
                              (window < 0.4f ? "   ← много теряется у края!" : ""));
                sb.AppendLine($"   итоговое затухание      = {attenuation:0.000000}");
                sb.AppendLine($"   ЯРКОСТЬ НА ПОВЕРХНОСТИ  = {linear:0.00} " +
                              "(1.0 = белое, больше — пересвет)");

                if (window < 0.4f)
                {
                    sb.AppendLine($"\n⚠ РАБОТА У САМОГО КРАЯ ДАЛЬНОСТИ.");
                    sb.AppendLine($"   Range {_range:0.0} м при расстоянии {hit.distance:0.0} м — " +
                                  $"оконная функция срезает {(1f - window) * 100f:0}% света.");
                    sb.AppendLine($"   ЛЕЧЕНИЕ: Range около {hit.distance * 2.5f:0} м. " +
                                  "Дальность не делает свет ярче — она задаёт, " +
                                  "докуда он вообще существует.");
                    problems++;
                }

                if (linear > 4f)
                {
                    sb.AppendLine($"\n❌ ПЕРЕСВЕТ В {linear:0} РАЗ.");
                    sb.AppendLine("   Центр пятна давно упёрся в белое, и тонемаппинг " +
                                  "сжимает всё сверху в одну точку — поэтому рост " +
                                  "яркости почти не виден.");
                    sb.AppendLine("\n   ЕСЛИ ХОЧЕТСЯ ПЯТНО БОЛЬШЕ — ЯРКОСТЬ НЕ ПОМОЖЕТ.");
                    sb.AppendLine("   Размер пятна задают ДВЕ ДРУГИЕ вещи:");
                    sb.AppendLine("     • Range — граница, за которой света нет вообще;");
                    sb.AppendLine("     • концентрация в шейдере (_AddLightFocus / " +
                                  "Доп. свет: концентрация) — МЕНЬШЕ единицы " +
                                  "распрямляет спад в широкое ровное пятно, " +
                                  "больше единицы сужает его в точку.");
                    float wantIntensity = 1.2f / Mathf.Max(attenuation, 1e-6f);
                    sb.AppendLine($"\n   Для этой сцены хватило бы Intensity {wantIntensity:0} " +
                                  $"(сейчас {_intensity:0}) — это яркость 1.2, чуть выше белого.");
                    problems++;
                }

                if (illum < 0.05f)
                {
                    sb.AppendLine($"\n❌ ЯРКОСТИ НЕ ХВАТАЕТ НА ЭТУ ДИСТАНЦИЮ.");
                    sb.AppendLine($"   Свет затухает по обратному квадрату: на " +
                                  $"{hit.distance:0.0} м остаётся 1/{d2:0} = " +
                                  $"{1f / d2:0.0000} от заданной яркости.");

                    if (_autoIntensity)
                        sb.AppendLine($"   ЛЕЧЕНИЕ: Brightness около 3 при " +
                                      $"Target Distance {hit.distance:0.0}.");
                    else
                        sb.AppendLine($"   ЛЕЧЕНИЕ: Intensity примерно {0.35f * d2:0}, " +
                                      "либо включи Auto Intensity и задай " +
                                      "Brightness 3.");

                    sb.AppendLine("   Либо опусти источник ближе — это всегда дешевле " +
                                  "по яркости, чем добивать издалека.");
                    problems++;
                }
                else if (illum > 12f)
                {
                    sb.AppendLine("\n⚠ Освещённость очень высокая — поверхность " +
                                  "уйдёт в белое пятно.");
                }
            }
            else
            {
                sb.AppendLine("\n⚠ Луч из источника ни во что не попал в пределах " +
                              $"{_range * 5f:0} м. Либо у геометрии нет коллайдеров " +
                              "(тогда проверка неинформативна), либо светить " +
                              "действительно не на что.");
            }
        }

        // ── 8. Режиссёр ──
        CatCooksLightDirector dir2 = Object.FindFirstObjectByType<CatCooksLightDirector>();
        sb.AppendLine($"\nРежиссёр в сцене: {(dir2 != null ? "есть" : "НЕТ (это нормально)")}");
        sb.AppendLine($"Источников зарегистрировано: {Registry.Count}");

        // Обычный цикл, а не Contains: Registry объявлен как IReadOnlyList,
        // чтобы список нельзя было испортить снаружи, а у него Contains нет —
        // он есть у List и ICollection. Тянуть System.Linq ради одной
        // проверки тоже незачем.
        bool registered = false;

        for (int i = 0; i < Registry.Count; i++)
        {
            if (Registry[i] != this) continue;
            registered = true;
            break;
        }

        if (!registered)
        {
            sb.AppendLine("\n❌ Этот источник НЕ зарегистрирован. Компонент выключен " +
                          "или объект неактивен.");
            problems++;
        }

        // ── итог ──
        sb.AppendLine($"\n═══ НАЙДЕНО ПРОБЛЕМ: {problems} ═══");

        if (problems == 0)
        {
            sb.AppendLine("Со стороны света всё в порядке. Если его всё равно не видно — " +
                          "причина в ШЕЙДЕРЕ освещаемой поверхности:");
            sb.AppendLine("  • в нём должен быть блок _ADDITIONAL_LIGHTS с циклом " +
                          "LIGHT_LOOP_BEGIN;");
            sb.AppendLine("  • под Forward+ этому циклу нужна переменная inputData " +
                          "с заполненным normalizedScreenSpaceUV.");
            sb.AppendLine("Проверка: поставь на пол стандартный материал URP/Lit. " +
                          "Если на нём свет виден, а на твоём toon-шейдере нет — " +
                          "дело в шейдере.");
        }

        if (problems > 0) Debug.LogWarning(sb.ToString(), this);
        else Debug.Log(sb.ToString(), this);
    }

    /// <summary>
    /// Выкрутить всё на максимум и обойти режиссёра. Если ПОСЛЕ этого света
    /// нет — дело точно не в настройках источника, а в URP или в шейдере
    /// поверхности.
    /// </summary>
    [ContextMenu("★ ТЕСТ: выкрутить на максимум")]
    public void DebugBlast()
    {
        Light l = GetComponent<Light>();
        if (l == null) return;

        _intensity = 25f;
        _range = Mathf.Max(_range, 60f);
        _dimmer = 1f;
        _flicker = Flicker.None;
        _randomEvents = false;
        _neverCull = true;
        _color = Color.white;
        _cullFade = 1f;
        _allowedByDirector = true;

        ApplyStatic();

        l.enabled = true;
        l.intensity = _intensity;
        l.color = Color.white;
        l.range = _range;

        Debug.LogWarning($"[{name}] Выкручено на максимум: яркость 25, дальность " +
                         $"{_range}, бюджет обойдён.\n" +
                         "Если света НЕТ и сейчас — причина вне этого компонента. " +
                         "Жми «★ ПОЧЕМУ НЕ СВЕТИТ».", this);
    }

    // ───────────────────────── редактор ─────────────────────────

    private void OnValidate()
    {
        if (_light == null) _light = GetComponent<Light>();

        // Пресет применяется только в момент СМЕНЫ. Иначе он затирал бы
        // любую ручную правку сразу после её внесения, и подстроить под себя
        // готовый прибор было бы невозможно.
        if (_preset != _lastPreset) ApplyPreset(_preset);

        GuardDirectional();
        RecomputeColor();
        RecomputeIntensity();
        ApplyStatic();
        _emissionId = Shader.PropertyToID(_emissionProperty);
    }

    private void Reset()
    {
        Light l = GetComponent<Light>();
        if (l == null) return;

        _color = l.color;
        _intensity = l.intensity;
        _range = l.range;
        _shape = l.type == LightType.Directional ? Shape.Sun
               : l.type == LightType.Spot ? Shape.Cone
               : Shape.Omni;
        _coneAngle = l.spotAngle;
    }

    private void OnDrawGizmosSelected()
    {
        if (_shape == Shape.Sun) return;

        Gizmos.color = new Color(_color.r, _color.g, _color.b, 0.9f);

        if (_shape == Shape.Cone)
        {
            // Рисуем конус: четыре образующие и окружность пятна.
            float half = _coneAngle * 0.5f * Mathf.Deg2Rad;
            float radius = Mathf.Tan(half) * _range;
            Vector3 tip = transform.position;
            Vector3 end = tip + transform.forward * _range;

            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Vector3 offset = transform.right * Mathf.Cos(a) * radius
                               + transform.up * Mathf.Sin(a) * radius;
                Gizmos.DrawLine(tip, end + offset);
            }

            Gizmos.DrawWireSphere(end, radius * 0.02f);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, _range);
        }

        // Граница отсечения — чтобы было видно, где источник погаснет.
        Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, EffectiveCullDistance);
    }
}
