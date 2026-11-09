using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ТРЁХМЕРНЫЙ ГРАДУСНИК НАД ПОСУДОЙ. Вешать на КАСТРЮЛЮ (туда же, где
/// CookingVessel и PotThermal). Модель градусника — дочерний объект, ссылка
/// на неё кладётся в поле «Модель».
///
/// ЭТО ПЕРЕПИСАННАЯ ВЕРСИЯ. Прошлые ломались об одно и то же: компонент
/// прятал градусник, ВЫКЛЮЧАЯ объект или обнуляя его масштаб, и если скрипт
/// после этого не отрабатывал — по любой причине, хоть из-за ненайденной
/// камеры — вернуть градусник было уже некому. Он навсегда оставался мёртвой
/// точкой в сцене.
///
/// ЗДЕСЬ ЭТО НЕВОЗМОЖНО ПО УСТРОЙСТВУ:
///   1. Прячем ГАСЯ РЕНДЕРЕРЫ, а не объект. Выключенный Renderer не мешает
///      скрипту работать — самовыключение исключено в принципе.
///   2. Масштаб и позиция не используются как способ спрятать, поэтому
///      сломанный градусник виден и его можно чинить глазами.
///   3. Ни один сбой не приводит к раннему выходу из LateUpdate. Нет камеры —
///      не разворачиваемся к ней, но продолжаем жить.
///   4. Есть HUD «Диагностика», который прямо говорит, что найдено, что нет
///      и почему прибор сейчас скрыт.
///
/// ЧЕТЫРЕ ДИАПАЗОНА:
///   до  40° — синее свечение, холод;
///   до 110° — оранжевый, по краям пар;
///   до 150° — густой дым, сильно кипит;
///   выше    — огонь, всё красное.
/// Границы перекрываются полосой, а не стоят встык: иначе ровно на 110.0
/// картинка щёлкала бы за один кадр.
///
/// Чисто локальная штука: температура уже синхронизирована, по сети отсюда
/// не уходит ничего.
/// </summary>
public class PotThermometer3D : MonoBehaviour
{
    // ───────────────────────── ССЫЛКИ ─────────────────────────

    [Header("Модель")]
    [Tooltip("Корень модели градусника — дочерний объект кастрюли. " +
             "Пусто = найдётся сам по имени (см. «Искать по имени»).")]
    [SerializeField] private Transform _model;

    [Tooltip("По какому куску имени искать модель, если ссылка не задана. " +
             "Регистр не важен.")]
    [SerializeField] private string _findByName = "termometer";

    [Tooltip("Масштаб модели. Ставится в Awake и больше не трогается: " +
             "прятать масштабом — плохая идея, из-за неё градусник и пропадал.")]
    [SerializeField] private Vector3 _modelScale = Vector3.one;

    [Tooltip("Брать масштаб модели КАК ОНА СТОИТ В СЦЕНЕ/ПРЕФАБЕ (вкл), а не из поля Model Scale. " +
             "Выключи, если хочешь задать размер числом в Model Scale.")]
    [SerializeField] private bool _keepSceneScale = true;

    [Tooltip("САМОНАСТРОЙКА по столбику (вкл): ось шкалы, ход пуговки (Scale Min/Max, Bar Axis) и " +
             "границы заливки считаются из меша столбика. Выкл — как задано полями руками.")]
    [SerializeField] private bool _autoCalibrate = true;
    [Tooltip("Ставить градусник ВЕРТИКАЛЬНО по его столбику (вкл), какой бы осью ни смотрела модель. " +
             "И стоять он будет низом на точке над кастрюлей, по центру — не по опоре модели.")]
    [SerializeField] private bool _uprightByColumn = true;
    [Tooltip("Доворот модели после выравнивания, градусы (если всё же смотрит не той стороной)")]
    [SerializeField] private Vector3 _extraEuler = Vector3.zero;

    [Tooltip("СРЕДНИЙ СЕГМЕНТ — столбик с шейдером. Меш не масштабируется: " +
             "заливку и цвет рисует шейдер, стекло вокруг остаётся целым.")]
    [SerializeField] private Renderer _column;

    [Tooltip("ПУГОВКА: деталь, которая ездит по столбику и показывает уровень.")]
    [SerializeField] private Transform _slider;

    [Tooltip("Нижний край хода пуговки в локальных координатах модели")]
    [SerializeField] private float _scaleMin;

    [Tooltip("Верхний край хода пуговки")]
    [SerializeField] private float _scaleMax = 0.172f;

    [Tooltip("Вдоль какой локальной оси идёт шкала. У модели из блендера это " +
             "чаще всего Z, а не Y.")]
    [SerializeField] private Axis _barAxis = Axis.Z;

    // ───────────────────────── ШКАЛА ─────────────────────────

    [Header("Шкала")]
    [Tooltip("Какой температуре соответствует пустой столбик")]
    [SerializeField] private float _minTemp = 20f;

    [Tooltip("Какой температуре соответствует полный столбик")]
    [SerializeField] private float _maxTemp = 250f;

    // ───────────────────────── РАЗМЕЩЕНИЕ ─────────────────────────

    [Header("Размещение")]
    [Tooltip("ОТ ЧЕГО отсчитывать высоту. Пусто = от самой кастрюли.\n\n" +
             "Поставь сюда пустышку на краю кастрюли, если хочешь повесить " +
             "градусник сбоку, а не по центру.")]
    [SerializeField] private Transform _anchor;

    [Tooltip("СЧИТАТЬ ВЕРХ КАСТРЮЛИ САМОСТОЯТЕЛЬНО.\n\n" +
             "Origin у модели может быть где угодно — у дна, в центре, в " +
             "стороне. Отсчёт от него и есть причина, по которой градусник " +
             "оказывался внутри посуды. С этой галкой высота меряется от " +
             "РЕАЛЬНОЙ верхней кромки: берутся габариты мешей кастрюли, и " +
             "прибор ставится над ними. Работает с любой моделью без подгонки.")]
    [SerializeField] private bool _autoTop = true;

    [Tooltip("Зазор над верхней кромкой кастрюли, метры. Работает при " +
             "включённом «Считать верх».")]
    [SerializeField] private float _clearance = 0.28f;

    [Tooltip("На сколько поднять, метры. Работает, когда «Считать верх» " +
             "выключено — тогда это просто высота над origin кастрюли.")]
    [SerializeField] private float _height = 0.95f;

    [Tooltip("Доводка положения в МИРОВЫХ координатах, метры. Прибавляется " +
             "к вычисленной точке: X и Z сдвигают вбок, Y добавляет высоты. " +
             "Этим и подгоняется окончательно.")]
    [SerializeField] private Vector3 _offset;

    [Tooltip("Разворачивать к камере")]
    [SerializeField] private bool _faceCamera = true;

    [Tooltip("Дальше этого прятать, метры")]
    [SerializeField] private float _maxDistance = 12f;

    // ───────────────────────── КОГДА ПОКАЗЫВАТЬ ─────────────────────────

    [Header("Когда показывать")]
    [Tooltip("Показывать ВСЕГДА. Отладочный тумблер: видно с ним — дело в " +
             "условиях показа, не видно — в ссылках или в самой модели.")]
    [SerializeField] private bool _alwaysShow;

    [Tooltip("Показывать, пока кастрюля стоит на конфорке — даже холодную")]
    [SerializeField] private bool _showOnBurner = true;

    [Tooltip("Показывать вне плиты, если температура выше этого значения")]
    [SerializeField] private float _showAboveTemp = 21f;

    [Tooltip("Показывать, когда игрок смотрит на кастрюлю вблизи")]
    [SerializeField] private bool _showWhenAimed = true;

    [Tooltip("С какого расстояния засчитывать взгляд, метры")]
    [SerializeField] private float _aimReach = 3f;

    [Tooltip("Насколько точно надо смотреть, градусы. Проверяем УГЛОМ, а не " +
             "лучом: луч на каждую кастрюлю каждый кадр дороже всего " +
             "остального градусника.")]
    [Range(3f, 60f)][SerializeField] private float _aimAngle = 22f;

    [Tooltip("На каком расстоянии искать конфорку под кастрюлей, метры")]
    [SerializeField] private float _burnerRadius = 0.45f;

    [Header("Появление")]
    [Tooltip("Секунд на разворачивание")]
    [SerializeField] private float _unfoldTime = 0.4f;

    [Tooltip("Секунд на сворачивание. Короче: появление должно быть заметным, " +
             "исчезновение — незаметным.")]
    [SerializeField] private float _foldTime = 0.22f;

    [Tooltip("Насколько перелетать за свой размер при разворачивании. " +
             "0 = просто вырастает, 2 = заметно подпрыгивает.")]
    [Range(0f, 3f)][SerializeField] private float _overshoot = 1.6f;

    // ───────────────────────── ЖИВОСТЬ ─────────────────────────

    [Header("Полёт")]
    [Tooltip("Размах покачивания вверх-вниз, метры")]
    [SerializeField] private float _bobAmplitude = 0.035f;

    [Tooltip("Скорость покачивания")]
    [SerializeField] private float _bobSpeed = 1.1f;

    [Tooltip("Насколько пульсирует масштабом. 0.06 = плюс-минус 6%.")]
    [Range(0f, 0.3f)][SerializeField] private float _pulseAmount = 0.06f;

    [Tooltip("Скорость пульсации на холоде. При нагреве ускоряется сама.")]
    [SerializeField] private float _pulseSpeed = 1.5f;

    [Tooltip("Мягкость пульсации. Больше — резче.")]
    [SerializeField] private float _pulseLerp = 9f;

    [Tooltip("Крен вбок, градусы")]
    [Range(0f, 20f)][SerializeField] private float _tiltAngle = 5f;

    [Header("Пуговка")]
    [Tooltip("Жёсткость пружины: как сильно тянет к своей отметке")]
    [SerializeField] private float _springK = 90f;

    [Tooltip("Затухание. Меньше — дольше качается после рывка.")]
    [SerializeField] private float _springDamp = 9f;

    [Tooltip("Мелкая дрожь при кипении, доли шкалы")]
    [Range(0f, 0.1f)][SerializeField] private float _sliderJitter = 0.012f;

    // ───────────────────────── ДИАПАЗОНЫ ─────────────────────────

    [Header("Диапазоны")]
    [Tooltip("Ниже этого — холод: синее свечение")]
    [SerializeField] private float _coldBelowC = 40f;

    [Tooltip("До этого — тепло: оранжевый и пар по краям")]
    [SerializeField] private float _steamToC = 110f;

    [Tooltip("До этого — кипение: густой дым")]
    [SerializeField] private float _smokeToC = 150f;

    [Tooltip("Ширина полосы перехода между диапазонами, градусы")]
    [Min(1f)][SerializeField] private float _blendC = 15f;

    [Header("Цвета")]
    [SerializeField] private Color _coldColor = new Color(0.35f, 0.70f, 1f);
    [SerializeField] private Color _warmColor = new Color(1f, 0.62f, 0.18f);
    [SerializeField] private Color _boilColor = new Color(1f, 0.42f, 0.10f);
    [SerializeField] private Color _hotColor = new Color(1f, 0.13f, 0.08f);

    [Tooltip("На сколько разнести струи пара вбок от корпуса, метры")]
    [SerializeField] private float _steamSideOffset = 0.055f;

    [Tooltip("Свет от эффектов. Пусто = создастся сам.")]
    [SerializeField] private Light _glow;

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    [Header("Диагностика")]
    [Tooltip("Панель на экране: что найдено, что нет, почему скрыт. " +
             "Показывается, когда смотришь на эту кастрюлю вблизи.")]
    [SerializeField] private bool _debugHud;

    [Tooltip("Рисовать состояние в сцене жёлтой линией и подписью")]
    [SerializeField] private bool _debugGizmo = true;

    public enum Axis { X, Y, Z }

    // Свойства шейдера столбика
    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int AxisId = Shader.PropertyToID("_FillAxis");
    private static readonly int MinId = Shader.PropertyToID("_MinBound");
    private static readonly int MaxId = Shader.PropertyToID("_MaxBound");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");

    // Имя свойства цвета у шейдера неизвестно, поэтому пишем во все, что есть.
    private static readonly string[] ColorProps =
        { "_FillColor", "_BaseColor", "_Color", "_EmissionColor", "_HeatColor" };

    private CatCooks.Food.IPotBody _thermal;
    private CookingMinigame _game;

    /// <summary>Рендереры модели. Именно их мы гасим и зажигаем.</summary>
    private readonly List<Renderer> _renderers = new List<Renderer>();

    private MaterialPropertyBlock _mpb;
    private readonly List<int> _colorIds = new List<int>();

    private Vector3 _sliderBase;
    private float _shown;
    private float _fold;
    private bool _visible = true;
    private float _pulseShown = 1f;
    private float _sliderPos, _sliderVel;
    private bool _ruptureLatch;
    private string _whyHidden = "";

    /// <summary>Насколько верхняя кромка кастрюли выше её origin, метры.</summary>
    private float _topOffset;
    private float _nextTopCheck;

    // конфорка под кастрюлей
    private static readonly List<StoveBurner> _burners = new List<StoveBurner>();
    private static float _burnersRefreshedAt = -999f;
    private bool _onBurner;
    private float _nextBurnerCheck;

    private ParticleSystem _frost, _steam, _smoke, _fire;
    private float _frostCarry, _steamCarry, _smokeCarry, _fireCarry;

    private Camera _cam;
    private float _nextCamCheck;

    // ───────────────────────── ЗАПУСК ─────────────────────────

    private void Awake()
    {
        // Ищем и выше по иерархии: компонент могли повесить на держатель.
        _thermal = GetComponent<CatCooks.Food.IPotBody>();
        if (_thermal == null) _thermal = GetComponentInParent<CatCooks.Food.IPotBody>();
        _game = GetComponent<CookingMinigame>() ?? GetComponentInParent<CookingMinigame>();

        ResolveModel();
        CollectRenderers();

        if (_model != null)
        {
            if (_keepSceneScale && _model.localScale.sqrMagnitude > 1e-10f) _modelScale = _model.localScale;
            if (_modelScale.sqrMagnitude < 1e-8f) _modelScale = Vector3.one;

            // Масштаб ставим СРАЗУ и больше не используем как способ спрятать.
            // Даже если ниже всё пойдёт наперекосяк, модель будет нормального
            // размера и на месте — сломанный, но видимый градусник чинится
            // глазами, невидимый не чинится ничем.
            _model.localScale = _modelScale;
            _model.gameObject.SetActive(true);
        }

        if (_column != null)
        {
            _mpb = new MaterialPropertyBlock();
            CacheColorProps();
            _sliderBase = _slider != null ? _slider.localPosition : Vector3.zero;
        }
        if (_slider != null) _sliderBase = _slider.localPosition;

        _colAxis = (int)_barAxis; _colMin = _scaleMin; _colMax = _scaleMax;
        if (_model != null) Calibrate();

        _shown = _thermal != null ? _thermal.TemperatureC : _minTemp;
        _sliderPos = Mathf.Clamp01(Mathf.InverseLerp(_minTemp, _maxTemp, _shown));

        BuildEffects();

        // Стартуем скрытым, но модель при этом ЖИВАЯ: погашены только
        // рендереры.
        _fold = 0f;
        SetVisible(false);

        Report();
    }

    /// <summary>
    /// Одна строка в консоли на старте. Она отвечает на первый вопрос при
    /// любой поломке — «а компонент вообще работает?» — и экономит полчаса
    /// гадания. Если этой строки в логе нет, значит объект выключен или
    /// скрипта на нём нет вовсе.
    /// </summary>
    private void Report()
    {
        string model = _model != null ? $"«{_model.name}»" : "НЕ НАЙДЕНА";
        string thermal = _thermal != null ? "есть" : "НЕТ";
        string column = _column != null ? "есть" : "нет";
        string slider = _slider != null ? "есть" : "нет";

        if (_thermal == null || _model == null)
        {
            Debug.LogError($"[Градусник] «{name}»: модель {model}, CookwarePressure/PotThermal {thermal}. " +
                           "Без них работать нечему. Компонент должен висеть на самой " +
                           "кастрюле, а модель градусника лежать у неё в детях.", this);
            return;
        }

        Debug.Log($"[Градусник] «{name}» запущен. Модель {model}, " +
                  $"рендереров {_renderers.Count}, CookwarePressure/PotThermal {thermal}, " +
                  $"столбик {column}, пуговка {slider}.", this);
    }

    /// <summary>Модель ищется по имени, если ссылку забыли поставить. Одна
    /// забытая ссылка не должна стоить вечера отладки.</summary>
    private void ResolveModel()
    {
        if (_model != null) return;
        if (string.IsNullOrEmpty(_findByName)) return;

        string needle = _findByName.ToLowerInvariant();

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform) continue;
            if (!t.name.ToLowerInvariant().Contains(needle)) continue;

            // Берём самый верхний подходящий: у модели внутри могут лежать
            // «inner termometer» и «stripe», а нам нужен их общий корень.
            if (_model == null || t.childCount > _model.childCount) _model = t;
        }

        if (_model != null)
            Debug.Log($"[Градусник] «{name}»: модель не была назначена, " +
                      $"нашлась по имени — «{_model.name}».", this);
    }

    /// <summary>
    /// Рендереры собираем ОДИН РАЗ и дальше только переключаем. Гасить
    /// рендерер, а не объект — единственный способ спрятать модель, который
    /// не может выключить сам скрипт, где бы тот ни висел.
    /// </summary>
    private void CollectRenderers()
    {
        _renderers.Clear();
        if (_model == null) return;

        foreach (Renderer r in _model.GetComponentsInChildren<Renderer>(true))
        {
            // Свои же частицы гасим отдельно, по температуре.
            if (r is ParticleSystemRenderer) continue;
            _renderers.Add(r);
        }

        if (_renderers.Count == 0)
            Debug.LogWarning($"[Градусник] «{name}»: в модели «{_model.name}» нет ни " +
                             "одного рендерера. Показывать нечего.", this);
    }

    private void CacheColorProps()
    {
        Material m = _column.sharedMaterial;
        if (m == null) return;

        foreach (string p in ColorProps)
            if (m.HasProperty(p)) _colorIds.Add(Shader.PropertyToID(p));
    }

    private void OnDestroy()
    {
        DestroyPS(ref _frost);
        DestroyPS(ref _steam);
        DestroyPS(ref _smoke);
        DestroyPS(ref _fire);
    }

    // ───────────────────────── ЦИКЛ ─────────────────────────

    /// <summary>
    /// НИ ОДНОГО РАННЕГО ВЫХОДА, кроме отсутствующей модели.
    ///
    /// Прежние версии выходили из LateUpdate, если не нашлась Camera.main, —
    /// и градусник замирал ровно в том виде, в каком его застало. Здесь
    /// камера нужна только для разворота и дистанции; нет её — работаем без
    /// неё, а не умираем.
    /// </summary>
    private void LateUpdate()
    {
        if (_model == null) return;

        float dt = Time.deltaTime;
        float temp = _thermal != null ? _thermal.TemperatureC : _minTemp;

        Camera cam = GetCamera();
        UpdateBurnerLink();
        UpdateRuptureLatch(temp);

        UpdateTopOffset();

        Vector3 anchor = Anchor();
        bool want = WantVisible(cam, anchor, temp);

        _fold = Mathf.MoveTowards(_fold, want ? 1f : 0f,
            dt / Mathf.Max(0.01f, want ? _unfoldTime : _foldTime));

        bool visible = _fold > 0.001f;
        if (_visible != visible) SetVisible(visible);

        // Позицию и анимацию считаем ВСЕГДА, даже когда скрыт: при появлении
        // прибор уже стоит где надо, а не подлетает откуда-то из прошлого.
        _shown = Mathf.Lerp(_shown, temp, 1f - Mathf.Exp(-4f * dt));

        Place(cam, anchor, temp, dt);
        ApplyColumn(temp);
        ApplySlider(dt, temp);

        if (visible) UpdateEffects(dt, temp);
    }

    /// <summary>
    /// ТОЧКА, НАД КОТОРОЙ ВИСИТ ГРАДУСНИК.
    ///
    /// Раньше здесь было просто «origin плюс высота», и этого хватало ровно до
    /// первой модели, у которой origin не в том месте: прибор оказывался
    /// внутри посуды, и лечилось это только подбором числа под каждую
    /// кастрюлю. Теперь верхняя кромка считается по габаритам мешей — то есть
    /// по тому, где кастрюля КОНЧАЕТСЯ на самом деле.
    /// </summary>
    private Vector3 Anchor()
    {
        Transform basis = _anchor != null ? _anchor : transform;

        float up = _autoTop ? _topOffset + _clearance : _height;
        return basis.position + Vector3.up * up + _offset;
    }

    /// <summary>
    /// Габариты кастрюли БЕЗ самого градусника: иначе он попадёт в собственный
    /// расчёт и с каждым кадром будет уползать всё выше сам от себя.
    ///
    /// Пересчитываем редко: кастрюлю мнёт деформация, и верхняя кромка после
    /// удара действительно меняется, но не за один кадр.
    /// </summary>
    private void UpdateTopOffset()
    {
        if (!_autoTop) return;
        if (Time.time < _nextTopCheck) return;
        _nextTopCheck = Time.time + 2f;

        bool any = false;
        Bounds b = new Bounds();

        foreach (Renderer r in GetComponentsInChildren<Renderer>(false))
        {
            if (r is ParticleSystemRenderer) continue;

            // Пропускаем всё, что лежит внутри модели градусника.
            if (_model != null && r.transform.IsChildOf(_model)) continue;

            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }

        if (!any) return;

        _topOffset = b.max.y - transform.position.y;
    }

    private bool WantVisible(Camera cam, Vector3 anchor, float temp)
    {
        if (_alwaysShow) { _whyHidden = ""; return true; }

        if (_ruptureLatch)
        {
            _whyHidden = "кастрюля разорвана — до починки и нагрева";
            return false;
        }

        if (cam != null && Vector3.Distance(cam.transform.position, anchor) > _maxDistance)
        {
            _whyHidden = $"дальше {_maxDistance} м";
            return false;
        }

        if (_showOnBurner && _onBurner) { _whyHidden = ""; return true; }
        if (temp > _showAboveTemp) { _whyHidden = ""; return true; }
        if (_showWhenAimed && cam != null && IsAimedAt(cam, anchor)) { _whyHidden = ""; return true; }

        _whyHidden = $"не на плите, {temp:0}° (нужно >{_showAboveTemp}), не смотрю";
        return false;
    }

    /// <summary>
    /// Скрытие — это ГАШЕНИЕ РЕНДЕРЕРОВ. Объект остаётся включённым, скрипт
    /// продолжает работать, вернуть градусник можно всегда. Именно на этом
    /// ломались прошлые версии: они выключали объект или обнуляли масштаб, и
    /// стоило скрипту после этого не отработать — оживить прибор было нечем.
    /// </summary>
    private void SetVisible(bool value)
    {
        _visible = value;

        for (int i = 0; i < _renderers.Count; i++)
            if (_renderers[i] != null) _renderers[i].enabled = value;

        if (_glow != null) _glow.enabled = value;

        if (!value) _frostCarry = _steamCarry = _smokeCarry = _fireCarry = 0f;
    }

    // ───────────────────────── РАЗМЕЩЕНИЕ ─────────────────────────

    /// <summary>
    /// Позиция задаётся в МИРОВЫХ координатах: кастрюлю могут наклонить, нести
    /// вчетвером или уронить, а градусник должен висеть ровно.
    /// </summary>
    private void Place(Camera cam, Vector3 anchor, float temp, float dt)
    {
        float heat01 = Mathf.Clamp01(Mathf.InverseLerp(_minTemp, _maxTemp, temp));

        // Две синусоиды с несовпадающими периодами: одна выглядит как
        // механический маятник, две — как живое парение.
        float bob = (Mathf.Sin(Time.time * _bobSpeed) * 0.75f +
                     Mathf.Sin(Time.time * _bobSpeed * 1.7f + 1.3f) * 0.25f)
                    * _bobAmplitude;

        // ── поворот ──
        Quaternion fix = _uprightByColumn && _hasUp ? Quaternion.FromToRotation(_upLocal, Vector3.up) : Quaternion.identity;
        fix = fix * Quaternion.Euler(_extraEuler);
        if (_faceCamera && cam != null)
        {
            // Только по горизонтали: смотришь сверху — градусник не заваливается назад.
            Vector3 f = _model.position - cam.transform.position;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            float tilt = Mathf.Sin(Time.time * _bobSpeed * 0.8f) * _tiltAngle;
            Quaternion yaw = Quaternion.LookRotation(f.normalized, Vector3.up);
            _model.rotation = _uprightByColumn
                ? yaw * Quaternion.Euler(0f, 0f, tilt) * fix
                : Quaternion.LookRotation(_model.position - cam.transform.position) * Quaternion.Euler(0f, 0f, tilt);
        }
        else if (_uprightByColumn && _hasUp)
            _model.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * fix;

        // Пульсация через Lerp: синус задаёт цель, Lerp сглаживает. При смене
        // темпа (нагрелся — задышал чаще) масштаб переходит мягко, а не
        // дёргается на стыке частот.
        float speed = _pulseSpeed * (1f + heat01 * 2.2f);
        float amount = _pulseAmount * (1f + heat01 * 0.8f);
        float target = 1f + Mathf.Sin(Time.time * speed) * amount;

        _pulseShown = Mathf.Lerp(_pulseShown, target, 1f - Mathf.Exp(-_pulseLerp * dt));

        // Масштаб НИКОГДА не уходит в ноль: сворачивание рисуется появлением
        // рендереров, а не схлопыванием. Ноль здесь однажды уже стоил
        // нескольких заходов отладки.
        float k = Mathf.Max(0.02f, Unfold(_fold) * _pulseShown);
        _model.localScale = _modelScale * k;

        // ── позиция: низом на точку, по центру над кастрюлей (не по опоре модели) ──
        Vector3 pos = anchor + Vector3.up * bob;
        if (_uprightByColumn && _hasBox)
        {
            Vector3 ls = _model.lossyScale;
            Quaternion r = _model.rotation;
            float minY = float.MaxValue;
            Vector3 mn = new Vector3(float.MaxValue, 0f, float.MaxValue), mx = new Vector3(float.MinValue, 0f, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? _boxMin.x : _boxMax.x, (i & 2) == 0 ? _boxMin.y : _boxMax.y, (i & 4) == 0 ? _boxMin.z : _boxMax.z);
                Vector3 w = r * Vector3.Scale(ls, c);
                minY = Mathf.Min(minY, w.y);
                mn.x = Mathf.Min(mn.x, w.x); mn.z = Mathf.Min(mn.z, w.z);
                mx.x = Mathf.Max(mx.x, w.x); mx.z = Mathf.Max(mx.z, w.z);
            }
            pos += new Vector3(-(mn.x + mx.x) * 0.5f, -minY, -(mn.z + mx.z) * 0.5f);
        }
        _model.position = pos;
    }

    // ───────────────────────── САМОНАСТРОЙКА ─────────────────────────

    private int _colAxis;
    private float _colMin, _colMax;
    private bool _hasUp, _hasBox;
    private Vector3 _upLocal = Vector3.up, _boxMin, _boxMax;

    /// <summary>
    /// Всё из меша столбика: где низ (начало заливки) и верх шкалы, вдоль какой оси
    /// ездит пуговка и в каких пределах. Чтобы скопированная с другой кастрюли модель
    /// работала без подбора чисел.
    /// </summary>
    private void Calibrate()
    {
        // Габарит модели в её осях (для центровки).
        bool any = false; Vector3 bmin = Vector3.zero, bmax = Vector3.zero;
        foreach (Renderer r in _renderers)
        {
            if (r == null) continue;
            MeshFilter rmf = r.GetComponent<MeshFilter>();
            if (rmf == null || rmf.sharedMesh == null) continue;
            Bounds b = rmf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 l = _model.InverseTransformPoint(r.transform.TransformPoint(c));
                if (!any) { bmin = bmax = l; any = true; } else { bmin = Vector3.Min(bmin, l); bmax = Vector3.Max(bmax, l); }
            }
        }
        _hasBox = any; _boxMin = bmin; _boxMax = bmax;

        MeshFilter mf = _column != null ? _column.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null)
        {
            // Столбика нет — «верх» модели по её самой длинной оси.
            if (any)
            {
                Vector3 size = bmax - bmin;
                int ax = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Vector3 u = Vector3.zero; u[ax] = 1f;
                _upLocal = u; _hasUp = true;
            }
            return;
        }

        Bounds mb = mf.sharedMesh.bounds;
        int a = mb.size.x >= mb.size.y && mb.size.x >= mb.size.z ? 0 : mb.size.y >= mb.size.z ? 1 : 2;
        Vector3 startM = mb.center, endM = mb.center;
        startM[a] = mb.min[a]; endM[a] = mb.max[a];

        // Направление «низ → верх» столбика в осях модели. Низ — начало заливки (min по оси меша).
        Vector3 sW = _column.transform.TransformPoint(startM), eW = _column.transform.TransformPoint(endM);
        Vector3 up = _model.InverseTransformDirection(eW - sW);
        if (up.sqrMagnitude > 1e-10f) { _upLocal = up.normalized; _hasUp = true; }

        if (!_autoCalibrate) return;

        _colAxis = a; _colMin = mb.min[a]; _colMax = mb.max[a];

        if (_slider != null && _slider.parent != null)
        {
            Transform sp = _slider.parent;
            Vector3 s0 = sp.InverseTransformPoint(sW), s1 = sp.InverseTransformPoint(eW);
            Vector3 d = s1 - s0;
            int sa = Mathf.Abs(d.x) >= Mathf.Abs(d.y) && Mathf.Abs(d.x) >= Mathf.Abs(d.z) ? 0 : Mathf.Abs(d.y) >= Mathf.Abs(d.z) ? 1 : 2;
            float lo = s0[sa], hi = s1[sa];
            float inset = (hi - lo) * 0.04f;
            _barAxis = (Axis)sa;
            _scaleMin = lo + inset;
            _scaleMax = hi - inset;
        }
        Debug.Log($"[Градусник] «{name}»: самонастройка по столбику — ось пуговки {_barAxis}, ход {_scaleMin:0.###}…{_scaleMax:0.###}", this);
    }

    /// <summary>Кривая с перелётом: прибор вырастает, чуть проскакивает свой
    /// размер и оседает. Линейный рост выглядит как включение объекта.</summary>
    private float Unfold(float t)
    {
        t = Mathf.Clamp01(t);
        if (_overshoot <= 0.001f) return t;

        float c1 = _overshoot;
        float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    // ───────────────────────── СТОЛБИК И ПУГОВКА ─────────────────────────

    private void ApplyColumn(float temp)
    {
        if (_column == null || _mpb == null) return;

        float t = Mathf.Clamp01(Mathf.InverseLerp(_minTemp, _maxTemp, _shown));
        Color c = HeatColor(temp);

        _column.GetPropertyBlock(_mpb);
        _mpb.SetFloat(FillId, t);
        _mpb.SetFloat(AxisId, (float)_colAxis);
        _mpb.SetFloat(MinId, _colMin);
        _mpb.SetFloat(MaxId, _colMax);
        _mpb.SetFloat(HeatId, Mathf.Clamp01(Mathf.InverseLerp(_minTemp, _smokeToC, temp)));

        for (int i = 0; i < _colorIds.Count; i++) _mpb.SetColor(_colorIds[i], c);

        _column.SetPropertyBlock(_mpb);
    }

    /// <summary>Цвет по диапазону, с плавными переходами через полосу _blendC.</summary>
    private Color HeatColor(float temp)
    {
        if (temp <= _coldBelowC - _blendC) return _coldColor;

        if (temp < _coldBelowC + _blendC)
            return Color.Lerp(_coldColor, _warmColor,
                Mathf.InverseLerp(_coldBelowC - _blendC, _coldBelowC + _blendC, temp));

        if (temp < _steamToC + _blendC)
            return Color.Lerp(_warmColor, _boilColor,
                Mathf.InverseLerp(_steamToC - _blendC, _steamToC + _blendC, temp));

        return Color.Lerp(_boilColor, _hotColor,
            Mathf.InverseLerp(_smokeToC - _blendC, _smokeToC + _blendC, temp));
    }

    /// <summary>
    /// ПУГОВКА едет к своей отметке ПРУЖИНОЙ: с перелётом и качанием. Lerp
    /// всегда подползает снизу и встаёт намертво, пружина проскакивает и
    /// качается — именно это читается как деталь прибора, а не как ползунок.
    ///
    /// Затухание по экспоненте: вычитание vel * damp * dt на просадке кадров
    /// уходит в минус и начинает дребезжать.
    /// </summary>
    private void ApplySlider(float dt, float temp)
    {
        if (_slider == null) return;

        float target = _game != null && _game.IsCooking
            ? Mathf.Clamp01(Mathf.InverseLerp(_minTemp, _maxTemp, _game.Target))
            : Mathf.Clamp01(Mathf.InverseLerp(_minTemp, _maxTemp, _shown));

        dt = Mathf.Min(dt, 0.05f);

        _sliderVel += (target - _sliderPos) * _springK * dt;
        _sliderVel *= Mathf.Exp(-_springDamp * dt);
        _sliderPos += _sliderVel * dt;

        // Кипение трясёт пуговку: понятно, что внутри бурлит, ещё до взгляда
        // на цифру.
        float boil = Mathf.Clamp01(Mathf.InverseLerp(_steamToC - _blendC, _smokeToC, temp));
        float jitter = boil > 0.01f ? Mathf.Sin(Time.time * 34f) * _sliderJitter * boil : 0f;

        float pos = Mathf.Lerp(_scaleMin, _scaleMax, Mathf.Clamp01(_sliderPos + jitter));
        Vector3 p = _sliderBase;

        switch (_barAxis)
        {
            case Axis.X: p.x = pos; break;
            case Axis.Y: p.y = pos; break;
            default: p.z = pos; break;
        }

        _slider.localPosition = p;
    }

    // ───────────────────────── ЭФФЕКТЫ ─────────────────────────

    private void UpdateEffects(float dt, float temp)
    {
        float cold01 = 1f - Mathf.Clamp01(
            Mathf.InverseLerp(_coldBelowC - _blendC, _coldBelowC, temp));

        float steam01 = Mathf.Clamp01(Mathf.InverseLerp(_coldBelowC - _blendC, _coldBelowC + _blendC, temp))
                      * (1f - Mathf.Clamp01(Mathf.InverseLerp(_steamToC, _smokeToC, temp)));

        float smoke01 = Mathf.Clamp01(Mathf.InverseLerp(_steamToC - _blendC, _smokeToC, temp));

        float fire01 = Mathf.Clamp01(
            Mathf.InverseLerp(_smokeToC - _blendC, _smokeToC + _blendC * 3f, temp));

        // Пока разворачивается — эффекты набирают силу вместе с ним, иначе дым
        // повалит от ещё не появившегося прибора.
        float gate = Mathf.Clamp01(_fold);
        cold01 *= gate; steam01 *= gate; smoke01 *= gate; fire01 *= gate;

        EmitFrost(dt, cold01);
        EmitSteam(dt, steam01);
        EmitSmoke(dt, smoke01);
        EmitFire(dt, fire01);
        UpdateGlow(temp, cold01, smoke01, fire01);
    }

    private void EmitFrost(float dt, float k)
    {
        if (_frost == null || k <= 0.02f) return;

        int n = Take(ref _frostCarry, 14f * k * dt, 4);
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < n; i++)
        {
            // Иней оседает ВНИЗ и медленно: холод не бьёт струёй, он висит.
            ep.position = _model.position + Random.insideUnitSphere * 0.09f;
            ep.startSize = Random.Range(0.004f, 0.011f);
            ep.startLifetime = Random.Range(0.8f, 1.6f);
            ep.startColor = _coldColor * (0.7f + k * 0.3f);
            ep.velocity = new Vector3(Random.Range(-0.04f, 0.04f),
                                      Random.Range(-0.09f, -0.02f),
                                      Random.Range(-0.04f, 0.04f));
            _frost.Emit(ep, 1);
        }
    }

    private void EmitSteam(float dt, float k)
    {
        if (_steam == null || k <= 0.02f) return;

        int n = Take(ref _steamCarry, 34f * k * dt, 8);
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        Vector3 right = _model.right;

        for (int i = 0; i < n; i++)
        {
            // ПО КРАЯМ: чётные влево, нечётные вправо. Две струи читаются как
            // «из прибора», одна по центру — как «из кастрюли».
            float side = (i & 1) == 0 ? -1f : 1f;

            ep.position = _model.position + right * (side * _steamSideOffset)
                        + Random.insideUnitSphere * 0.012f;
            ep.startSize = Random.Range(0.02f, 0.05f) * (0.6f + k);
            ep.startLifetime = Random.Range(0.5f, 1.1f);
            ep.startColor = new Color(1f, 1f, 1f, 0.35f + k * 0.35f);
            ep.velocity = right * (side * Random.Range(0.10f, 0.22f))
                        + Vector3.up * Random.Range(0.20f, 0.45f) * (0.5f + k);
            _steam.Emit(ep, 1);
        }
    }

    private void EmitSmoke(float dt, float k)
    {
        if (_smoke == null || k <= 0.02f) return;

        // Дыма именно МНОГО: это единственный диапазон, который должно быть
        // видно с другого конца кухни, иначе перегрев замечают уже по взрыву.
        int n = Take(ref _smokeCarry, 48f * k * dt, 10);
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < n; i++)
        {
            ep.position = _model.position + Vector3.up * 0.03f
                        + Random.insideUnitSphere * 0.035f;
            ep.startSize = Random.Range(0.06f, 0.14f) * (0.5f + k * 1.8f);
            ep.startLifetime = Random.Range(1.1f, 2.2f) * (0.7f + k);

            float dark = Mathf.Lerp(0.55f, 0.10f, k);
            ep.startColor = new Color(dark, dark, dark, 0.30f + k * 0.5f);
            ep.velocity = new Vector3(Random.Range(-0.11f, 0.11f),
                                      Random.Range(0.35f, 0.80f) * (0.6f + k),
                                      Random.Range(-0.11f, 0.11f));
            _smoke.Emit(ep, 1);
        }
    }

    private void EmitFire(float dt, float k)
    {
        if (_fire == null || k <= 0.02f) return;

        int n = Take(ref _fireCarry, 55f * k * dt, 8);
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < n; i++)
        {
            ep.position = _model.position + Random.insideUnitSphere * 0.025f;
            ep.startSize = Random.Range(0.025f, 0.06f) * (0.5f + k * 1.4f);
            ep.startLifetime = Random.Range(0.18f, 0.40f);
            ep.startColor = Color.Lerp(new Color(1f, 0.85f, 0.35f), _hotColor, Random.value);
            ep.velocity = new Vector3(Random.Range(-0.10f, 0.10f),
                                      Random.Range(0.55f, 1.25f) * (0.6f + k),
                                      Random.Range(-0.10f, 0.10f));
            _fire.Emit(ep, 1);
        }
    }

    /// <summary>Один свет на все диапазоны, цвет тот же, что у столбика: так
    /// свечение и прибор никогда не расходятся.</summary>
    private void UpdateGlow(float temp, float cold01, float smoke01, float fire01)
    {
        if (_glow == null) return;

        float warm = Mathf.Max(fire01, smoke01 * 0.4f);
        bool on = cold01 > 0.05f || warm > 0.05f;

        _glow.enabled = on && _visible;
        if (!on) return;

        _glow.transform.position = _model.position;
        _glow.color = HeatColor(temp);

        if (warm >= cold01)
        {
            // Мерцает только жар: мигающий иней выглядит как сломанная лампочка.
            float flicker = 0.85f + Mathf.PerlinNoise(Time.time * 11f, 0.5f) * 0.3f;
            _glow.intensity = warm * 3.2f * flicker;
            _glow.range = 0.8f + warm * 1.8f;
        }
        else
        {
            _glow.intensity = cold01 * 1.6f;
            _glow.range = 0.6f + cold01 * 0.8f;
        }
    }

    private static int Take(ref float carry, float add, int max)
    {
        carry += add;
        int n = Mathf.FloorToInt(carry);
        if (n <= 0) return 0;
        carry -= n;
        return n > max ? max : n;
    }

    // ───────────────────────── ОКРУЖЕНИЕ ─────────────────────────

    private void UpdateRuptureLatch(float temp)
    {
        if (_thermal == null) return;

        if (_thermal.IsRuptured) { _ruptureLatch = true; return; }

        // Защёлка снимается ДВУМЯ условиями: заварили И снова нагрели.
        // Одного ремонта мало — ServerRepaired сбрасывает температуру к
        // комнатной, и градусник вылез бы на холодной кастрюле, показывая
        // ровно ничего.
        if (_ruptureLatch && temp > _showAboveTemp) _ruptureLatch = false;
    }

    private void UpdateBurnerLink()
    {
        if (Time.time < _nextBurnerCheck) return;
        _nextBurnerCheck = Time.time + 0.25f;

        RefreshBurnerCache();

        _onBurner = false;
        float r2 = _burnerRadius * _burnerRadius;
        Vector3 me = transform.position;

        for (int i = 0; i < _burners.Count; i++)
        {
            StoveBurner b = _burners[i];
            if (b == null) continue;
            if ((b.Plate.position - me).sqrMagnitude <= r2) { _onBurner = true; return; }
        }
    }

    /// <summary>Кэш общий на все кастрюли и обновляется редко: десять кастрюль
    /// не должны десять раз в секунду обшаривать сцену.</summary>
    private static void RefreshBurnerCache()
    {
        if (Time.time - _burnersRefreshedAt < 5f && _burners.Count > 0) return;
        _burnersRefreshedAt = Time.time;

        _burners.Clear();
        _burners.AddRange(Object.FindObjectsByType<StoveBurner>(FindObjectsSortMode.None));
    }

    private bool IsAimedAt(Camera cam, Vector3 anchor)
    {
        Vector3 to = anchor - cam.transform.position;
        float d = to.magnitude;
        if (d > _aimReach || d < 0.01f) return false;

        return Vector3.Dot(cam.transform.forward, to / d) >= Mathf.Cos(_aimAngle * Mathf.Deg2Rad);
    }

    /// <summary>
    /// Камера с запасными вариантами. Camera.main требует тега MainCamera, и
    /// если его забыли поставить — а его забывают — прежние версии просто
    /// вставали. Здесь при отсутствии тега берём любую включённую камеру.
    /// </summary>
    private Camera GetCamera()
    {
        if (_cam != null && _cam.isActiveAndEnabled) return _cam;
        if (Time.time < _nextCamCheck) return _cam;

        _nextCamCheck = Time.time + 0.5f;
        _cam = Camera.main;

        if (_cam == null)
        {
            _cam = Camera.current;
            if (_cam == null)
            {
                Camera[] all = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
                foreach (Camera c in all)
                    if (c.isActiveAndEnabled) { _cam = c; break; }

                if (_cam != null)
                    Debug.LogWarning($"[Градусник] «{name}»: Camera.main не найдена " +
                                     "(нет тега MainCamera), взял камеру " +
                                     $"«{_cam.name}». Поставь тег, чтобы не искать каждый раз.",
                                     this);
            }
        }

        return _cam;
    }

    // ───────────────────────── ЧАСТИЦЫ ─────────────────────────

    private void BuildEffects()
    {
        _frost = MakePS("ThermoFrost (auto)", 200, -0.05f, true);
        _steam = MakePS("ThermoSteam (auto)", 300, -0.02f, false);
        _smoke = MakePS("ThermoSmoke (auto)", 500, -0.04f, false);
        _fire = MakePS("ThermoFire (auto)", 400, -0.20f, true);

        if (_glow == null && _model != null)
        {
            GameObject go = new GameObject("ThermoGlow (auto)");
            go.transform.SetParent(_model, false);
            _glow = go.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.shadows = LightShadows.None;
            _glow.range = 1f;
            _glow.intensity = 0f;
            _glow.enabled = false;
        }
    }

    /// <summary>
    /// Система создаётся ВЫКЛЮЧЕННОЙ: она играет сразу при AddComponent, а на
    /// играющей часть свойств менять нельзя — Unity сыплет ошибки про duration.
    /// </summary>
    private ParticleSystem MakePS(string psName, int maxParticles, float gravity, bool additive)
    {
        GameObject go = new GameObject(psName);
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startSize = 0.05f;
        main.startLifetime = 1f;
        main.gravityModifier = gravity;

        // Сцена стоит далеко от нуля координат. При Automatic Unity гасит
        // систему целиком, потому что её трансформ вне экрана, хотя частицы
        // прямо перед игроком.
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;                       // испускаем вручную

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.18f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = g;

        if (!additive)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));
        }

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");

        if (sh != null)
        {
            Material m = new Material(sh) { name = psName };
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);

            // Огонь и иней светятся (Additive), пар и дым просто прозрачные:
            // additive-дым на светлом фоне становится невидимым, а его как раз
            // и надо видеть издалека.
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", additive ? 2f : 0f);
            if (m.HasProperty("_SrcBlend"))
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend"))
                m.SetFloat("_DstBlend", additive
                    ? (float)UnityEngine.Rendering.BlendMode.One
                    : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            r.material = m;
        }

        go.SetActive(true);
        ps.Play();
        return ps;
    }

    private static void DestroyPS(ref ParticleSystem ps)
    {
        if (ps == null) return;
        Destroy(ps.gameObject);
        ps = null;
    }

    // ───────────────────────── ДИАГНОСТИКА ─────────────────────────

    /// <summary>
    /// Панель говорит прямо, что нашлось и почему прибор скрыт. Гадать,
    /// почему объект не появился, дороже, чем прочитать шесть строк.
    /// </summary>
    private void OnGUI()
    {
        if (!_debugHud) return;

        Camera cam = _cam;
        if (cam == null) return;

        Vector3 anchor = Anchor();
        if (Vector3.Distance(cam.transform.position, anchor) > 5f) return;

        Vector3 sp = cam.WorldToScreenPoint(anchor);
        if (sp.z <= 0f) return;

        string txt =
            $"модель: {(_model != null ? _model.name : "НЕТ")}\n" +
            $"рендереров: {_renderers.Count}\n" +
            $"Кастрюля: {(_thermal != null ? $"{_thermal.TemperatureC:0}°" : "НЕТ")}\n" +
            $"на плите: {(_onBurner ? "да" : "нет")}\n" +
            $"верх кастрюли: +{_topOffset:0.00} м, зазор {_clearance:0.00}\n" +
            $"fold: {_fold:0.00}   видим: {(_visible ? "да" : "нет")}\n" +
            (string.IsNullOrEmpty(_whyHidden) ? "показан" : $"скрыт: {_whyHidden}");

        GUIStyle st = new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.UpperLeft };
        st.normal.textColor = _model == null || _thermal == null
            ? new Color(1f, 0.45f, 0.35f)
            : new Color(0.75f, 0.95f, 1f);

        Vector2 size = st.CalcSize(new GUIContent(txt));
        GUI.Box(new Rect(sp.x + 20f, Screen.height - sp.y, size.x + 14f, size.y + 10f), txt, st);
    }

    private void OnDrawGizmos()
    {
        if (!_debugGizmo) return;

        Vector3 anchor = Application.isPlaying
            ? Anchor()
            : transform.position + Vector3.up * (_autoTop ? _clearance : _height) + _offset;

        Gizmos.color = _model != null ? new Color(1f, 0.9f, 0.3f, 0.8f)
                                      : new Color(1f, 0.3f, 0.3f, 0.8f);
        Gizmos.DrawLine(transform.position, anchor);
        Gizmos.DrawWireSphere(anchor, 0.06f);

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, _burnerRadius);
    }
}
