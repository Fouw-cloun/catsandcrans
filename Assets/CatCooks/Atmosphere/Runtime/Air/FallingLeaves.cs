using UnityEngine;

/// <summary>
/// ПАДАЮЩИЕ ЛИСТЬЯ. Система частиц собирается кодом целиком — руками в
/// инспекторе Shuriken крутить нечего.
///
/// Повесь на пустышку над зоной, где должны лететь листья, задай размер зоны и
/// плотность. Всё остальное настраивается полями этого компонента.
///
/// ═══ ПОЛЁТ ПО КРИВОЙ: ОРБИТАЛЬНАЯ СКОРОСТЬ ═══
///
/// Главное отличие от типовых уроков. Обычно листопад делают так: гравитация
/// вниз плюс модуль Noise. Получается прямая линия с дрожанием — лист
/// ТРЯСЁТСЯ, но летит всё равно вертикально.
///
/// Настоящий лист падает ПО СПИРАЛИ: он планирует, заваливается, разворачивается
/// и уходит в сторону. Даёт это Orbital Velocity — вращение вокруг оси системы.
/// Лист буквально закручивается вокруг центра зоны, спускаясь.
///
/// Шум при этом остаётся, но играет другую роль: он ломает идеальность
/// спирали. Один без другого не работает — орбита без шума выглядит
/// каруселью, шум без орбиты выглядит падением в лифте.
///
/// ═══ ЗАВИХРЕНИЯ ═══
///
/// Radial добавляет к орбите движение от центра или к центру. Комбинация с
/// орбитой и даёт вихрь: лист не просто крутится по кругу, а раскручивается
/// наружу по расширяющейся спирали.
///
/// ═══ ПОРЫВЫ ═══
///
/// Постоянный ветер быстро надоедает — глаз перестаёт его замечать. Порывы с
/// паузами читаются гораздо сильнее, поэтому сила ветра идёт по кривой, а
/// метод Gust() позволяет дёрнуть порыв извне — например с залпа мехов.
///
/// ═══ ОПТИМИЗАЦИЯ ═══
///
/// Коллизии выключены по умолчанию, и это осознанно. Модуль Collision в режиме
/// World гоняет проверки для каждой живой частицы каждый кадр — на паре сотен
/// листьев это заметная нагрузка ради того, чтобы лист лёг на пол, а не растаял
/// в воздухе. Включай точечно, если листья реально должны копиться на этаже.
/// </summary>
[DisallowMultipleComponent]
public class FallingLeaves : MonoBehaviour
{
    /// <summary>Как сыплются листья.</summary>
    public enum LeafMode
    {
        /// <summary>Ровный поток без пауз.</summary>
        Continuous = 0,

        /// <summary>Только порывы: пролетела стайка — и снова тишина.</summary>
        Gusts = 1,

        /// <summary>Редкий фоновый поток плюс порывы поверх него.</summary>
        Both = 2
    }

    [Header("Режим")]
    [Tooltip("CONTINUOUS — ровный поток.\n\n" +
             "GUSTS — только порывы: пролетела стайка и снова тишина. Тишина " +
             "здесь работает не меньше самих листьев: без пауз глаз перестаёт " +
             "замечать движение уже через полминуты.\n\n" +
             "BOTH — редкий фон плюс порывы поверх.")]
    [SerializeField] private LeafMode _mode = LeafMode.Continuous;

    [Header("Количество")]
    [Tooltip("Базовая плотность: листьев в секунду")]
    [Min(0f)][SerializeField] private float _leavesPerSecond = 14f;

    [Tooltip("МНОЖИТЕЛЬ КОЛИЧЕСТВА. Отдельно от базы, чтобы можно было " +
             "крутить густоту листопада, не трогая настроенный баланс — и " +
             "менять её из кода по ходу игры.")]
    [Range(0f, 5f)][SerializeField] private float _densityMultiplier = 1f;

    [Tooltip("Потолок живых частиц. Защита от того, чтобы долгая жизнь и " +
             "высокая плотность не сложились в тысячи листьев.")]
    [Min(4)][SerializeField] private int _maxParticles = 250;

    [Header("Размер")]
    [Tooltip("Минимальный размер листа, метры")]
    [SerializeField] private float _sizeMin = 0.16f;

    [Tooltip("Максимальный размер листа, метры")]
    [SerializeField] private float _sizeMax = 0.3f;

    [Tooltip("МНОЖИТЕЛЬ МАСШТАБА. Тот же приём, что с плотностью: база " +
             "настраивается один раз, а этим полем листья подгоняются под " +
             "конкретную сцену.")]
    [Range(0.1f, 5f)][SerializeField] private float _scaleMultiplier = 1f;

    [Tooltip("Насколько лист сжимается к концу жизни. Чуть меньше единицы — " +
             "лист как будто удаляется, и исчезновение читается мягче.")]
    [Range(0.3f, 1.5f)][SerializeField] private float _endScale = 0.85f;

    [Header("Зона")]
    [Tooltip("Размер коробки, из которой сыплются листья, метры")]
    [SerializeField] private Vector3 _zoneSize = new Vector3(14f, 1f, 14f);

    [Tooltip("Сколько живёт лист, секунд. Вместе со скоростью падения задаёт, " +
             "докуда он долетит.")]
    [Min(0.5f)][SerializeField] private float _lifetime = 7f;

    [Tooltip("Заполнить воздух листьями сразу при старте, а не ждать, пока " +
             "они долетят. Без этого первые секунды сцены небо пустое.")]
    [SerializeField] private bool _prewarm = true;

    [Header("Падение")]
    [Tooltip("Скорость снижения, м/с. Лист не камень — 0.6..1.5 разумно.")]
    [SerializeField] private float _fallSpeed = 0.9f;

    [Tooltip("СОПРОТИВЛЕНИЕ ВОЗДУХА. Гасит разгон, из-за него лист планирует, " +
             "а не ускоряется. Ноль — падает как гирька.")]
    [Range(0f, 5f)][SerializeField] private float _drag = 0.8f;

    [Header("Ветер")]
    [Tooltip("Направление ветра в МИРОВЫХ координатах. Y обычно ноль.")]
    [SerializeField] private Vector3 _windDirection = new Vector3(1f, 0f, 0.35f);

    [Tooltip("Сила ветра, м/с²")]
    [SerializeField] private float _windStrength = 1.2f;

    [Header("Завихрения")]
    [Tooltip("ОРБИТАЛЬНАЯ СКОРОСТЬ — то, что даёт полёт по кривой.\n\n" +
             "Лист закручивается вокруг оси Y этого объекта, спускаясь по " +
             "спирали. Именно этого не хватает типовым листопадам, где " +
             "гравитация и шум дают прямую линию с дрожанием.")]
    [Range(0f, 3f)][SerializeField] private float _swirl = 0.55f;

    [Tooltip("РАЗБЕГАНИЕ ОТ ЦЕНТРА. В паре с орбитой превращает круг в " +
             "раскручивающуюся спираль. Отрицательное значение стягивает " +
             "листья к центру — получается воронка.")]
    [Range(-2f, 2f)][SerializeField] private float _radial = 0.25f;

    [Tooltip("Турбулентность. Ломает идеальность спирали — без неё орбита " +
             "выглядит каруселью.")]
    [Range(0f, 3f)][SerializeField] private float _turbulence = 0.55f;

    [Tooltip("Крупность турбулентности. Низкая — плавные широкие заносы, " +
             "высокая — мелкая дрожь.")]
    [Range(0.05f, 3f)][SerializeField] private float _turbulenceScale = 0.35f;

    [Tooltip("Скорость движения турбулентности")]
    [Range(0f, 3f)][SerializeField] private float _turbulenceScroll = 0.4f;

    [Header("Вращение")]
    [Tooltip("СКОРОСТЬ ПЕРЕВОРОТОВ, оборотов в секунду по каждой оси.\n\n" +
             "Вращение по ТРЁМ осям обязательно: только по одной лист крутится " +
             "как пропеллер и выдаёт свою плоскость. По трём — переворачивается " +
             "и читается объёмным.")]
    [Range(0f, 2f)][SerializeField] private float _flipSpeed = 0.35f;

    [Header("Цвет")]
    [Tooltip("Первый оттенок. Система смешивает случайно между двумя.")]
    [SerializeField] private Color _colorA = new Color(0.85f, 0.55f, 0.15f);

    [Tooltip("Второй оттенок")]
    [SerializeField] private Color _colorB = new Color(0.65f, 0.25f, 0.08f);

    [Tooltip("Материал листа. Пусто — соберётся на шейдере CatCooks/Leaf.")]
    [SerializeField] private Material _material;

    [Header("Порывы (режим Gusts и Both)")]
    [Tooltip("Пауза между порывами, секунд — минимум")]
    [Min(0.2f)][SerializeField] private float _gustIntervalMin = 6f;

    [Tooltip("Пауза между порывами, секунд — максимум.\n\n" +
             "Диапазон, а не одно число: строго ритмичные порывы выдают " +
             "механику. Разброс делает их погодой, а не таймером.")]
    [Min(0.2f)][SerializeField] private float _gustIntervalMax = 14f;

    [Tooltip("Сколько длится сам порыв, секунд — минимум")]
    [Min(0.1f)][SerializeField] private float _gustDurationMin = 1.2f;

    [Tooltip("Сколько длится порыв, секунд — максимум")]
    [Min(0.1f)][SerializeField] private float _gustDurationMax = 3f;

    [Tooltip("Листьев в порыве — минимум")]
    [Min(1)][SerializeField] private int _gustLeavesMin = 18;

    [Tooltip("Листьев в порыве — максимум")]
    [Min(1)][SerializeField] private int _gustLeavesMax = 45;

    [Header("Разброс рисунка полёта")]
    [Tooltip("НА СКОЛЬКО ГРАДУСОВ МЕНЯЕТСЯ НАПРАВЛЕНИЕ ВЕТРА от порыва к " +
             "порыву.\n\n" +
             "360 — ветер каждый раз с новой стороны, порывы совсем не похожи. " +
             "40–90 — ветер в целом дует в одну сторону, но каждый порыв " +
             "заходит чуть иначе. Второе обычно естественнее.")]
    [Range(0f, 360f)][SerializeField] private float _gustDirectionJitter = 70f;

    [Tooltip("Во сколько раз может отличаться сила ветра в порыве")]
    [SerializeField] private Vector2 _gustWindRange = new Vector2(0.7f, 2.2f);

    [Tooltip("Во сколько раз может отличаться закрутка")]
    [SerializeField] private Vector2 _gustSwirlRange = new Vector2(0.5f, 2f);

    [Tooltip("МЕНЯТЬ НАПРАВЛЕНИЕ ЗАКРУТКИ.\n\n" +
             "Половина порывов закручивается по часовой, половина против. " +
             "Одна эта мелочь отличает соседние порывы сильнее, чем все " +
             "остальные разбросы вместе.")]
    [SerializeField] private bool _flipSwirlDirection = true;

    [Tooltip("Порыв заходит с той стороны, откуда дует ветер, а не сыплется " +
             "по всей зоне. Так он читается как ПРОЛЕТАЮЩАЯ стайка, а не как " +
             "внезапно погустевший листопад.")]
    [SerializeField] private bool _gustFromUpwindEdge = true;

    [Header("Столкновения")]
    [Tooltip("Листья ложатся на поверхности, а не тают в воздухе.\n\n" +
             "ВЫКЛЮЧЕНО НЕ ПРОСТО ТАК: модуль Collision в режиме World " +
             "проверяет каждую живую частицу каждый кадр. На паре сотен " +
             "листьев это заметная трата ради того, чтобы лист лёг на пол.")]
    [SerializeField] private bool _collide = false;

    [Tooltip("Что считается поверхностью")]
    [SerializeField] private LayerMask _collisionMask = ~0;

    [Tooltip("Сколько жизни лист теряет при касании, доля. 0 — лежит до конца " +
             "своего срока, 1 — исчезает сразу.")]
    [Range(0f, 1f)][SerializeField] private float _lifetimeLossOnHit = 0.3f;

    private ParticleSystem _ps;
    private Transform _root;

    // ───────── состояние порывов ─────────
    private float _nextGustAt;      // когда начнётся следующий
    private float _gustEndsAt;      // когда закончится текущий
    private bool _gustActive;

    /// <summary>Идёт ли порыв прямо сейчас. Полезно, если захочешь синхронно
    /// качнуть что-то ещё — брезент, сетку, флаги.</summary>
    public bool GustActive => _gustActive;

    /// <summary>Готовая система — если понадобится доснастить снаружи.</summary>
    public ParticleSystem System => _ps;

    private void Awake() => Build();

    private void Update()
    {
        if (_mode == LeafMode.Continuous || _ps == null) return;

        if (_gustActive)
        {
            if (Time.time >= _gustEndsAt) EndGust();
            return;
        }

        if (Time.time >= _nextGustAt) StartGust(1f);
    }

    // ───────────────────────── ПОРЫВЫ ─────────────────────────

    /// <summary>
    /// Начать порыв, заново разыграв рисунок полёта.
    ///
    /// Меняется не только сила: направление, закрутка, её ЗНАК, разбегание и
    /// турбулентность. Крутить одну лишь силу мало — порывы отличались бы
    /// густотой и больше ничем, а глаз читает именно траекторию.
    /// </summary>
    private void StartGust(float strength01)
    {
        float s = Mathf.Clamp01(strength01);

        // ── направление ──
        // Отклоняем от заданного ветра, а не берём случайное с нуля: ветер в
        // сцене в целом дует куда-то, и порывы должны быть его вариациями.
        Vector3 baseDir = _windDirection.sqrMagnitude < 0.0001f
            ? Vector3.right : _windDirection.normalized;

        float jitter = Random.Range(-_gustDirectionJitter, _gustDirectionJitter) * 0.5f;
        Vector3 dir = Quaternion.Euler(0f, jitter, 0f) * baseDir;
        dir.y = 0f;
        dir.Normalize();

        float wind = _windStrength * Random.Range(_gustWindRange.x, _gustWindRange.y) * s;

        // ── закрутка ──
        float swirl = _swirl * Random.Range(_gustSwirlRange.x, _gustSwirlRange.y);
        if (_flipSwirlDirection && Random.value < 0.5f) swirl = -swirl;

        // Разбегание тоже меняет знак: наружу — раскрывающаяся спираль,
        // внутрь — воронка. Два заметно разных рисунка из одного числа.
        float radial = _radial * Random.Range(0.4f, 1.6f);
        if (Random.value < 0.35f) radial = -radial;

        float turb = _turbulence * Random.Range(0.6f, 1.7f);

        ApplyFlightPattern(dir, wind, swirl, radial, turb);

        // ── откуда заходит ──
        if (_gustFromUpwindEdge) MoveSpawnUpwind(dir);
        else ResetSpawnToWholeZone();

        // ── сколько и как долго ──
        float duration = Random.Range(_gustDurationMin, _gustDurationMax);
        int leaves = Mathf.RoundToInt(
            Random.Range(_gustLeavesMin, _gustLeavesMax + 1) * s * _densityMultiplier);

        ParticleSystem.EmissionModule em = _ps.emission;
        em.rateOverTime = leaves / Mathf.Max(0.1f, duration);

        _gustActive = true;
        _gustEndsAt = Time.time + duration;
    }

    private void EndGust()
    {
        _gustActive = false;

        ParticleSystem.EmissionModule em = _ps.emission;

        // В режиме Both между порывами остаётся редкий фон — но именно
        // редкий: если фон заметен, порыв на его фоне теряется.
        em.rateOverTime = _mode == LeafMode.Both
            ? _leavesPerSecond * _densityMultiplier * 0.15f
            : 0f;

        ResetSpawnToWholeZone();

        _nextGustAt = Time.time + Random.Range(_gustIntervalMin, _gustIntervalMax);
    }

    /// <summary>Разыграть новый рисунок полёта: ветер, закрутку, разбегание,
    /// турбулентность. Трогаем только эти модули — пересобирать систему
    /// целиком ради порыва незачем.</summary>
    private void ApplyFlightPattern(Vector3 dir, float wind, float swirl,
                                    float radial, float turb)
    {
        Vector3 w = dir * wind;

        ParticleSystem.ForceOverLifetimeModule force = _ps.forceOverLifetime;
        force.enabled = wind > 0.001f;

        // Кривая нарастания и спада: порыв должен налетать и стихать, а не
        // включаться рубильником.
        AnimationCurve ramp = new AnimationCurve(
            new Keyframe(0f, 0.25f),
            new Keyframe(0.3f, 1f),
            new Keyframe(0.75f, 0.55f),
            new Keyframe(1f, 0.2f));

        force.x = new ParticleSystem.MinMaxCurve(w.x, ramp);
        force.y = new ParticleSystem.MinMaxCurve(w.y, ramp);
        force.z = new ParticleSystem.MinMaxCurve(w.z, ramp);

        ParticleSystem.VelocityOverLifetimeModule vel = _ps.velocityOverLifetime;

        // Все три оси разом — иначе Unity ругается на разные режимы кривых.
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(swirl * 0.4f, swirl);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.radial = new ParticleSystem.MinMaxCurve(radial * 0.5f, radial);

        ParticleSystem.NoiseModule noise = _ps.noise;
        noise.enabled = turb > 0.001f;
        noise.strengthX = turb;
        noise.strengthY = turb * 0.35f;
        noise.strengthZ = turb;

        // Смещаем шум по времени: без этого соседние порывы попадают в один и
        // тот же участок шумового поля и заносят листья одинаково.
        noise.scrollSpeed = _turbulenceScroll * Random.Range(0.7f, 1.5f);
        noise.frequency = _turbulenceScale * Random.Range(0.75f, 1.35f);
    }

    /// <summary>
    /// Сдвинуть зону рождения к наветренному краю и сузить её.
    ///
    /// Без этого порыв сыплется по всей площади разом и читается как «вдруг
    /// стало гуще». А стайка, зашедшая с одной стороны и прошедшая насквозь,
    /// читается как порыв ветра — то есть как событие с направлением.
    /// </summary>
    private void MoveSpawnUpwind(Vector3 dir)
    {
        ParticleSystem.ShapeModule shape = _ps.shape;

        // Система развёрнута на 90° по X, поэтому её локальные оси не совпадают
        // с мировыми: локальный X — это мировой X, локальный Y — мировой Z,
        // локальный Z — мировая высота. Промахнуться тут легко, а выглядит это
        // как «зона уехала куда-то вбок».
        float halfX = _zoneSize.x * 0.5f;
        float halfZ = _zoneSize.z * 0.5f;

        Vector3 upwind = -dir;
        shape.position = new Vector3(upwind.x * halfX * 0.75f,
                                     upwind.z * halfZ * 0.75f,
                                     0f);

        shape.scale = new Vector3(_zoneSize.x * 0.45f,
                                  _zoneSize.z * 0.45f,
                                  _zoneSize.y);
    }

    private void ResetSpawnToWholeZone()
    {
        ParticleSystem.ShapeModule shape = _ps.shape;
        shape.position = Vector3.zero;
        shape.scale = new Vector3(_zoneSize.x, _zoneSize.z, _zoneSize.y);
    }

    // ───────────────────────── СБОРКА ─────────────────────────

    /// <summary>Собрать систему. Публичный, чтобы пересобирать после правки
    /// полей из кнопки в инспекторе.</summary>
    public void Build()
    {
        EnsureRoot();
        Apply();
    }

    private void EnsureRoot()
    {
        if (_root == null)
        {
            Transform found = transform.Find("__FallingLeaves");

            if (found != null)
            {
                _root = found;
            }
            else
            {
                GameObject go = new GameObject("__FallingLeaves");
                go.transform.SetParent(transform, false);
                _root = go.transform;
            }
        }

        // Система смотрит ВНИЗ: Unity выпускает частицы вдоль локальной +Z,
        // и без разворота коробка эмиссии лежала бы боком.
        _root.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _root.localPosition = Vector3.zero;

        _ps = _root.GetComponent<ParticleSystem>();
        if (_ps == null) _ps = _root.gameObject.AddComponent<ParticleSystem>();
    }

    private void Apply()
    {
        if (_ps == null) return;

        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // ── основной модуль ──
        ParticleSystem.MainModule main = _ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 5f;
        main.prewarm = _prewarm;
        main.startLifetime = new ParticleSystem.MinMaxCurve(_lifetime * 0.75f, _lifetime);
        main.maxParticles = _maxParticles;

        // Начальная скорость нулевая: всё движение задают силы. Так листья
        // не выстреливают из зоны, а именно отрываются и планируют.
        main.startSpeed = 0f;

        main.startSize = new ParticleSystem.MinMaxCurve(_sizeMin * _scaleMultiplier,
                                                        _sizeMax * _scaleMultiplier);

        // ВРАЩЕНИЕ ПО ТРЁМ ОСЯМ при рождении: одинаково ориентированные листья
        // сразу выдают, что это плоские картинки.
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        main.startColor = new ParticleSystem.MinMaxGradient(_colorA, _colorB);

        // Гравитацию не используем: ею управлять грубее, чем скоростью, и
        // лист от неё разгоняется как камень.
        main.gravityModifier = 0f;

        // МИРОВОЕ пространство: листья не должны ездить за объектом, если
        // зону подвинули или она едет вместе с этажом.
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // ── эмиссия ──
        ParticleSystem.EmissionModule em = _ps.emission;
        em.enabled = true;

        // В режиме порывов постоянная эмиссия НУЛЕВАЯ: листья выпускает только
        // StartGust. Иначе между порывами шёл бы фон, и сами порывы на нём
        // терялись бы.
        em.rateOverTime = _mode switch
        {
            LeafMode.Gusts => 0f,
            LeafMode.Both => _leavesPerSecond * _densityMultiplier * 0.15f,
            _ => _leavesPerSecond * _densityMultiplier
        };

        // ── зона ──
        ParticleSystem.ShapeModule shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.randomDirectionAmount = 0f;

        // ОСИ ЗДЕСЬ НЕ СОВПАДАЮТ С МИРОВЫМИ. Система развёрнута на 90° по X,
        // чтобы выпускать частицы вниз, и из-за этого её локальный Y — это
        // мировой Z, а локальный Z — мировая высота.
        //
        // Раньше сюда уходил _zoneSize как есть, и зона (14, 1, 14) вместо
        // плоского листа превращалась в высокую узкую плиту: 14 в ширину, 1 в
        // глубину и 14 в высоту. Листья сыпались столбом.
        ResetSpawnToWholeZone();

        // Первый порыв планируем сразу, чтобы после старта не ждать полный
        // интервал в тишине.
        if (_mode != LeafMode.Continuous)
        {
            _gustActive = false;
            _nextGustAt = Time.time + Random.Range(0.5f, _gustIntervalMin);
        }

        // ── падение и ветер ──
        // Скорость вниз задаём здесь, а не гравитацией: постоянная скорость
        // читается как планирование, ускорение — как падение камня.
        ParticleSystem.VelocityOverLifetimeModule vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;

        // ВСЕ ТРИ ОСИ ОБЯЗАНЫ БЫТЬ В ОДНОМ РЕЖИМЕ КРИВОЙ.
        //
        // Unity хранит x/y/z одного модуля общим блоком и не допускает, чтобы
        // одна ось была диапазоном, а другая одиночной константой. Задашь
        // только y — остальные останутся константами по умолчанию, и посыплется
        // «curves must all be in the same mode».
        //
        // Поэтому x и z задаём явно, пусть и почти нулевыми: заодно они дают
        // лёгкий боковой снос, из-за которого листья не падают строем.
        float sway = _fallSpeed * 0.25f;
        vel.x = new ParticleSystem.MinMaxCurve(-sway, sway);
        vel.y = new ParticleSystem.MinMaxCurve(-_fallSpeed * 1.3f, -_fallSpeed * 0.7f);
        vel.z = new ParticleSystem.MinMaxCurve(-sway, sway);

        // ЗАВИХРЕНИЕ. Орбита вокруг оси Y плюс разбегание от центра — вместе
        // они и дают спираль вместо прямой линии.
        //
        // Орбитальные оси — такой же общий блок: X и Z задаём нулевыми
        // диапазонами, иначе тот же самый конфликт режимов.
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(_swirl * 0.4f, _swirl);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);

        vel.radial = new ParticleSystem.MinMaxCurve(_radial * 0.5f, _radial);

        // ── ветер ──
        Vector3 w = _windDirection.normalized * _windStrength;

        ParticleSystem.ForceOverLifetimeModule force = _ps.forceOverLifetime;
        force.enabled = _windStrength > 0.001f;
        force.space = ParticleSystemSimulationSpace.World;

        // Ветер по кривой, а не константой: порыв нарастает и стихает, и
        // постоянный сквозняк не превращается в фон, который глаз перестаёт
        // замечать.
        AnimationCurve gust = new AnimationCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(0.35f, 1f),
            new Keyframe(0.7f, 0.5f),
            new Keyframe(1f, 0.8f));

        force.x = new ParticleSystem.MinMaxCurve(w.x, gust);
        force.y = new ParticleSystem.MinMaxCurve(w.y, gust);
        force.z = new ParticleSystem.MinMaxCurve(w.z, gust);

        // ── сопротивление воздуха ──
        // Без него лист разгоняется и втыкается в землю пулей.
        ParticleSystem.LimitVelocityOverLifetimeModule limit = _ps.limitVelocityOverLifetime;
        limit.enabled = _drag > 0.01f;
        limit.limit = 10000f;          // потолок скорости не трогаем
        limit.dampen = 0f;
        limit.drag = _drag;
        limit.multiplyDragByParticleSize = true;
        limit.multiplyDragByParticleVelocity = true;

        // ── турбулентность ──
        ParticleSystem.NoiseModule noise = _ps.noise;
        noise.enabled = _turbulence > 0.001f;
        noise.strength = _turbulence;
        noise.frequency = _turbulenceScale;
        noise.scrollSpeed = _turbulenceScroll;
        noise.quality = ParticleSystemNoiseQuality.Medium;
        noise.damping = true;

        // Раздельные оси: без этого шум одинаково дёргает лист во все стороны,
        // и заносы получаются симметричными, чего в воздухе не бывает.
        noise.separateAxes = true;
        noise.strengthX = _turbulence;
        noise.strengthY = _turbulence * 0.35f;   // вертикальную дрожь придавливаем
        noise.strengthZ = _turbulence;

        // ── переворачивание ──
        ParticleSystem.RotationOverLifetimeModule rot = _ps.rotationOverLifetime;
        rot.enabled = _flipSpeed > 0.001f;
        rot.separateAxes = true;

        float turns = _flipSpeed * Mathf.PI * 2f;
        rot.x = new ParticleSystem.MinMaxCurve(-turns, turns);
        rot.y = new ParticleSystem.MinMaxCurve(-turns * 1.4f, turns * 1.4f);
        rot.z = new ParticleSystem.MinMaxCurve(-turns, turns);

        // ── размер по жизни ──
        ParticleSystem.SizeOverLifetimeModule size = _ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, _endScale)));

        // ── появление и растворение ──
        // Резко возникший и резко исчезнувший лист читается как баг. Плавные
        // края решают это одной кривой.
        ParticleSystem.ColorOverLifetimeModule col = _ps.colorOverLifetime;
        col.enabled = true;

        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(1f, 0.85f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        // ── столкновения ──
        ParticleSystem.CollisionModule col3d = _ps.collision;
        col3d.enabled = _collide;

        if (_collide)
        {
            col3d.type = ParticleSystemCollisionType.World;
            col3d.mode = ParticleSystemCollisionMode.Collision3D;
            col3d.collidesWith = _collisionMask;
            col3d.bounce = 0f;            // лист не мячик
            col3d.dampen = 0.95f;         // гасим скорость почти полностью
            col3d.lifetimeLoss = _lifetimeLossOnHit;
            col3d.quality = ParticleSystemCollisionQuality.Medium;
        }

        // ── рендер ──
        ParticleSystemRenderer r = _root.GetComponent<ParticleSystemRenderer>();
        if (r != null)
        {
            // Mesh-режим не нужен: шейдер сам рисует силуэт листа на кваде, и
            // геометрия остаётся двумя треугольниками.
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.World;
            r.sortMode = ParticleSystemSortMode.None;
            r.material = _material != null ? _material : DefaultMaterial();

            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        _ps.Play();
    }

    /// <summary>Материал по умолчанию, если свой не назначен — чтобы система
    /// не рисовалась розовым и не выглядела поломанной при первом запуске.</summary>
    private Material DefaultMaterial()
    {
        Shader sh = Shader.Find("CatCooks/Leaf");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) return null;

        return new Material(sh) { name = "FallingLeaves (auto)" };
    }

    // ───────────────────────── УПРАВЛЕНИЕ ИЗ ИГРЫ ─────────────────────────

    /// <summary>Сменить густоту листопада на ходу. Пересобирать систему не
    /// нужно — меняется только частота эмиссии.</summary>
    public void SetDensity(float multiplier)
    {
        _densityMultiplier = Mathf.Max(0f, multiplier);
        if (_ps == null) return;

        // Во время порыва эмиссию не трогаем: её выставил StartGust под
        // конкретное число листьев и длительность, и перезапись сорвала бы
        // порыв на середине. Новая плотность подхватится со следующего.
        if (_gustActive) return;

        ParticleSystem.EmissionModule em = _ps.emission;
        em.rateOverTime = _mode switch
        {
            LeafMode.Gusts => 0f,
            LeafMode.Both => _leavesPerSecond * _densityMultiplier * 0.15f,
            _ => _leavesPerSecond * _densityMultiplier
        };
    }

    /// <summary>Сменить масштаб листьев на ходу.</summary>
    public void SetScale(float multiplier)
    {
        _scaleMultiplier = Mathf.Max(0.01f, multiplier);
        if (_ps == null) return;

        ParticleSystem.MainModule main = _ps.main;
        main.startSize = new ParticleSystem.MinMaxCurve(_sizeMin * _scaleMultiplier,
                                                        _sizeMax * _scaleMultiplier);
    }

    /// <summary>
    /// Порыв: разом выбросить пачку листьев.
    ///
    /// Зови откуда угодно — с залпа мехов, с проезда техники, с удара. Порыв
    /// читается сильнее любого постоянного ветра именно потому, что он
    /// событие, а не фон.
    /// </summary>
    /// <summary>
    /// Пустить порыв прямо сейчас, не дожидаясь таймера.
    ///
    /// Разыгрывает новый рисунок полёта, как обычный порыв, — поэтому зов
    /// снаружи выглядит так же, как естественный. Зови откуда угодно: с залпа
    /// мехов, с проезда техники, с удара.
    ///
    /// Работает в любом режиме, включая Continuous: там порыв просто ляжет
    /// поверх ровного потока.
    /// </summary>
    public void Gust(float strength01 = 1f)
    {
        if (_ps == null) return;

        // Уже идёт порыв — не перебиваем, иначе частые вызовы извне рвали бы
        // его на куски и он перестал бы читаться как цельное событие.
        if (_gustActive) return;

        StartGust(strength01);
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    [ContextMenu("Пересобрать систему")]
    private void EditorRebuild() => Build();

    [ContextMenu("Порыв ветра")]
    private void EditorGust() => Gust(1f);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.7f, 0.25f, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, _zoneSize);

        // Направление ветра — стрелкой от центра зоны.
        Gizmos.matrix = Matrix4x4.identity;
        Vector3 w = _windDirection.sqrMagnitude < 0.0001f
            ? Vector3.right : _windDirection.normalized;

        Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.9f);
        Gizmos.DrawRay(transform.position, w * 3f);
        Gizmos.DrawWireSphere(transform.position + w * 3f, 0.15f);
    }
}
