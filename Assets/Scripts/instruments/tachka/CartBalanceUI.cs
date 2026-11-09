using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ИНТЕРФЕЙС БАЛАНСА ТАЧКИ. Кнопки QTE, шкала крена, счётчик серии.
///
/// Вешается на тот же объект, что CartBalance, или на любой другой — тогда
/// ссылку на баланс надо задать вручную.
///
/// ═══ ГРАФИКА РИСУЕТСЯ КОДОМ ═══
///
/// Ни одного спрайта импортировать не нужно: формы генерируются в текстуру при
/// первом запуске и кэшируются статически на все кнопки сцены.
///
/// Так сделано не из экономии, а по необходимости: пока идёт настройка
/// механики, форма кнопки меняется каждые полчаса, и гонять её через
/// импорт-ассет означает править в двух местах и обязательно однажды забыть.
/// Когда стиль устоится, поле Plate Override примет обычный спрайт художника —
/// и генератор отключится сам.
///
/// ═══ ЧЕТЫРЁХСЛОЙНАЯ СБОРКА ═══
///
///   Кольцо   — расходится и гаснет. Живёт СНАРУЖИ подложки, поэтому не
///              мешает читать букву в момент вспышки.
///   Подложка — белая, красится тинтом. Именно белая: перекрашивать по
///              состоянию можно только незакрашенную графику.
///   Детали   — протектор, фаска, блик. В анимации не участвуют, кроме
///              вращения у колеса.
///   Буква    — ОТДЕЛЬНЫЙ объект, всегда сверху. Если запечь её в подложку,
///              смена цвета состояния потребует перерисовки всей картинки.
///
/// Масштабируется ТОЛЬКО корень виджета. Внутренние слои о пульсации не
/// знают — поэтому стиль можно поменять, не трогая код анимации.
///
/// ═══ ПРО ВРАЩЕНИЕ КОЛЕСА ═══
///
/// Крутится только кольцо протектора, буква остаётся неподвижной. Собери
/// колесо одним спрайтом и покрути целиком — буква станет нечитаемой уже на
/// втором обороте.
/// </summary>
[AddComponentMenu("CatCooks/Тачка/Cart Balance UI")]
public class CartBalanceUI : MonoBehaviour
{
    public enum Style
    {
        /// <summary>Выпуклая клавиша с пульта. Форма подсказывает действие.</summary>
        Button = 0,

        /// <summary>Шестигранник под ключ. Компактный силуэт.</summary>
        Nut = 1,

        /// <summary>Колесо тачки. Протектор вращается.</summary>
        Wheel = 2
    }

    public enum Layout
    {
        /// <summary>Показывать только нужную кнопку. Меньше шума на экране.</summary>
        RequiredOnly = 0,

        /// <summary>Обе висят, нужная подсвечена. Игрок заранее знает раскладку.</summary>
        BothHighlight = 1
    }

    [Header("━━━ Связь ━━━")]
    [Tooltip("Пусто — ищется на этом же объекте, затем в сцене.")]
    [SerializeField] private CartBalance _balance;

    [Tooltip("Пусто — берётся любой Canvas в сцене, а если его нет, создаётся " +
             "свой.")]
    [SerializeField] private Canvas _canvas;

    [Header("━━━ Вид ━━━")]
    [SerializeField] private Style _style = Style.Wheel;
    [SerializeField] private Layout _layout = Layout.BothHighlight;

    [Tooltip("Размер кнопки в пикселях интерфейса")]
    [Range(60f, 320f)][SerializeField] private float _size = 150f;

    [Tooltip("Расстояние между кнопками при раскладке «обе»")]
    [Range(80f, 700f)][SerializeField] private float _spread = 340f;

    [Tooltip("Смещение от центра экрана вниз, доля высоты")]
    [Range(-0.5f, 0.5f)][SerializeField] private float _verticalOffset = -0.22f;

    [Header("━━━ Цвета состояний ━━━")]
    [SerializeField] private Color _colorIdle = new Color(0.55f, 0.58f, 0.62f, 1f);
    [SerializeField] private Color _colorWarn = new Color(0.95f, 0.72f, 0.20f, 1f);
    [SerializeField] private Color _colorCall = new Color(1f, 0.55f, 0.10f, 1f);
    [SerializeField] private Color _colorHit = new Color(0.37f, 0.76f, 0.42f, 1f);
    [SerializeField] private Color _colorMiss = new Color(0.72f, 0.22f, 0.18f, 1f);

    [Tooltip("Цвет неактивной кнопки при раскладке «обе». Держи ТУСКЛЫМ: " +
             "если обе кнопки яркие, игрок теряет полсекунды на выбор — " +
             "ровно столько, сколько длится окно.")]
    [SerializeField] private Color _colorDim = new Color(0.3f, 0.32f, 0.35f, 0.55f);

    [Header("━━━ Анимация ━━━")]
    [Tooltip("Пульсация в покое: во сколько раз кнопка дышит")]
    [Range(1f, 1.2f)][SerializeField] private float _idlePulse = 1.04f;

    [Tooltip("Пульсация при открытом окне. Заметно сильнее — это и есть " +
             "сигнал «жми сейчас».")]
    [Range(1f, 1.5f)][SerializeField] private float _callPulse = 1.14f;

    [Range(0.1f, 3f)][SerializeField] private float _idlePeriod = 1.6f;
    [Range(0.1f, 2f)][SerializeField] private float _callPeriod = 0.42f;

    [Tooltip("Скачок при попадании")]
    [Range(1f, 2f)][SerializeField] private float _hitPop = 1.3f;

    [Tooltip("Амплитуда тряски при промахе, пиксели")]
    [Range(0f, 40f)][SerializeField] private float _missShake = 14f;

    [Tooltip("Скорость появления и исчезновения, секунды")]
    [Range(0.05f, 1f)][SerializeField] private float _fade = 0.15f;

    [Header("━━━ Кольцо ━━━")]
    [Tooltip("До какого размера расходится кольцо")]
    [Range(1.1f, 2.5f)][SerializeField] private float _ringMax = 1.55f;

    [Header("━━━ Кнопка тормоза ━━━")]
    [Tooltip("Кнопка S появляется, когда тачка набрала ход и её надо " +
             "останавливать.\n\n" +
             "Она принципиально отличается от кнопок баланса: те требуют " +
             "НАЖАТИЯ в окне, эта — УДЕРЖАНИЯ до полной остановки. Поэтому " +
             "она не пульсирует, а показывает остаток хода заливкой: игрок " +
             "видит, сколько ещё держать.")]
    [SerializeField] private bool _showBrake = true;

    [Range(60f, 260f)][SerializeField] private float _brakeSize = 118f;
    [Range(-0.5f, 0.5f)][SerializeField] private float _brakeOffsetY = -0.40f;

    [SerializeField] private Color _colorBrake = new Color(0.85f, 0.28f, 0.22f, 1f);
    [SerializeField] private Color _colorBraking = new Color(0.42f, 0.72f, 0.95f, 1f);

    [Header("━━━ Шкала крена ━━━")]
    [Tooltip("Полоска, показывающая, куда и насколько валит. Без неё игрок " +
             "видит только момент, когда уже поздно.")]
    [SerializeField] private bool _showLeanBar = true;

    [Range(100f, 900f)][SerializeField] private float _barWidth = 420f;
    [Range(4f, 40f)][SerializeField] private float _barHeight = 12f;
    [Range(-0.5f, 0.5f)][SerializeField] private float _barOffset = -0.36f;

    [Header("━━━ Своя графика ━━━")]
    [Tooltip("Спрайт подложки художника. Пусто — рисуется кодом.\n\n" +
             "ВАЖНО: спрайт должен быть БЕЛЫМ. Цвет накладывается тинтом, и " +
             "закрашенную графику перекрасить по состоянию невозможно.")]
    [SerializeField] private Sprite _plateOverride;

    [SerializeField] private Sprite _detailOverride;
    [SerializeField] private Sprite _ringOverride;

    [Tooltip("Шрифт буквы. Пусто — берётся стандартный из настроек TMP.")]
    [SerializeField] private TMP_FontAsset _font;

    // ───────────────────────── состояние ─────────────────────────

    private class Widget
    {
        public RectTransform root;
        public RectTransform ring;
        public Image ringImg;
        public Image plate;
        public Image detail;
        public RectTransform detailRt;
        public TextMeshProUGUI label;
        public CanvasGroup group;
        public Vector2 home;
        public float spin;
    }

    private Widget _left, _right, _brake;
    private RectTransform _brakeFill;
    private Image _brakeFillImg;
    private RectTransform _barRoot, _barFill, _barMark;
    private Image _barFillImg;
    private bool _built;
    private float _shakeSeed;
    private float _hitTime;
    private CartBalance.Phase _lastPhase = CartBalance.Phase.Calm;

    // Формы кэшируются на все кнопки сцены: у двух виджетов одна и та же
    // текстура, и генерировать её дважды незачем.
    private static Sprite _spPlateCircle, _spPlateHex, _spTread, _spRing,
                          _spBevelHex, _spGlossTop, _spBar;

    private void Awake()
    {
        if (_balance == null) _balance = GetComponent<CartBalance>();
        if (_balance == null) _balance = Object.FindFirstObjectByType<CartBalance>();

        _shakeSeed = Random.value * 100f;
        Build();
    }

    private void OnDestroy()
    {
        if (_left != null && _left.root != null) Destroy(_left.root.gameObject);
        if (_right != null && _right.root != null) Destroy(_right.root.gameObject);
        if (_barRoot != null) Destroy(_barRoot.gameObject);
    }

    // ───────────────────────── сборка ─────────────────────────

    private void Build()
    {
        EnsureSprites();
        EnsureCanvas();

        if (_canvas == null) return;

        _left = MakeWidget("QTE_Left", "A");
        _right = MakeWidget("QTE_Right", "D");

        if (_showBrake) MakeBrake();
        if (_showLeanBar) MakeBar();

        _built = true;
        Refresh(0f, true);
    }

    private void EnsureCanvas()
    {
        if (_canvas != null) return;

        _canvas = Object.FindFirstObjectByType<Canvas>();
        if (_canvas != null && _canvas.renderMode != RenderMode.WorldSpace) return;

        GameObject go = new GameObject("CartBalanceCanvas");
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Масштабирование по высоте: кнопка QTE должна занимать одну и ту же
        // долю экрана на любом разрешении, иначе на широком мониторе она
        // окажется вдвое мельче, чем задумано.
        CanvasScaler cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        cs.matchWidthOrHeight = 1f;

        go.AddComponent<GraphicRaycaster>();
    }

    private Widget MakeWidget(string name, string letter)
    {
        Widget w = new Widget();

        GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(_canvas.transform, false);

        w.root = root.GetComponent<RectTransform>();
        w.root.sizeDelta = new Vector2(_size, _size);
        w.group = root.GetComponent<CanvasGroup>();
        w.group.alpha = 0f;
        w.group.interactable = false;
        w.group.blocksRaycasts = false;

        // ── КОЛЬЦО ──
        // Первым в иерархии, значит рисуется ПОД остальным. Оно расходится
        // наружу и не должно перекрывать букву в момент вспышки.
        GameObject ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
        ring.transform.SetParent(w.root, false);
        w.ring = ring.GetComponent<RectTransform>();
        w.ring.sizeDelta = new Vector2(_size, _size);
        w.ringImg = ring.GetComponent<Image>();
        w.ringImg.sprite = _ringOverride != null ? _ringOverride : _spRing;
        w.ringImg.raycastTarget = false;

        // ── ПОДЛОЖКА ──
        GameObject plate = new GameObject("Plate", typeof(RectTransform), typeof(Image));
        plate.transform.SetParent(w.root, false);
        RectTransform prt = plate.GetComponent<RectTransform>();
        prt.sizeDelta = new Vector2(_size, _size);
        w.plate = plate.GetComponent<Image>();
        w.plate.sprite = _plateOverride != null ? _plateOverride : PlateSprite();
        w.plate.raycastTarget = false;

        // ── ДЕТАЛИ ──
        GameObject det = new GameObject("Detail", typeof(RectTransform), typeof(Image));
        det.transform.SetParent(w.root, false);
        w.detailRt = det.GetComponent<RectTransform>();
        w.detailRt.sizeDelta = new Vector2(_size, _size);
        w.detail = det.GetComponent<Image>();
        w.detail.sprite = _detailOverride != null ? _detailOverride : DetailSprite();
        w.detail.color = Color.white;
        w.detail.raycastTarget = false;

        // ── БУКВА ──
        GameObject txt = new GameObject("Label", typeof(RectTransform));
        txt.transform.SetParent(w.root, false);
        RectTransform trt = txt.GetComponent<RectTransform>();
        trt.sizeDelta = new Vector2(_size, _size);

        w.label = txt.AddComponent<TextMeshProUGUI>();
        w.label.text = letter;
        w.label.alignment = TextAlignmentOptions.Center;
        w.label.fontSize = _size * 0.46f;
        w.label.fontStyle = FontStyles.Bold;
        w.label.raycastTarget = false;
        if (_font != null) w.label.font = _font;

        return w;
    }

    /// <summary>
    /// Кнопка тормоза. Отдельный виджет, а не третья кнопка баланса: у неё
    /// другая природа — не нажатие в окне, а удержание до остановки.
    /// </summary>
    private void MakeBrake()
    {
        _brake = MakeWidget("QTE_Brake", "S");
        _brake.root.sizeDelta = new Vector2(_brakeSize, _brakeSize);
        _brake.ring.sizeDelta = new Vector2(_brakeSize, _brakeSize);
        _brake.label.fontSize = _brakeSize * 0.46f;

        foreach (RectTransform rt in _brake.root.GetComponentsInChildren<RectTransform>())
            if (rt != _brake.root) rt.sizeDelta = new Vector2(_brakeSize, _brakeSize);

        // ЗАЛИВКА ОСТАТКА ХОДА. Кольцо здесь не подходит: оно расходится и
        // гаснет, то есть говорит «жми сейчас». Тормоз же говорит «держи ещё
        // столько», а это величина, а не момент.
        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(_brake.root, false);
        fill.transform.SetSiblingIndex(2);

        _brakeFill = fill.GetComponent<RectTransform>();
        _brakeFill.sizeDelta = new Vector2(_brakeSize * 0.92f, _brakeSize * 0.14f);
        _brakeFill.anchoredPosition = new Vector2(0f, -_brakeSize * 0.62f);

        _brakeFillImg = fill.GetComponent<Image>();
        _brakeFillImg.sprite = _spBar;
        _brakeFillImg.raycastTarget = false;
    }

    private void MakeBar()
    {
        GameObject root = new GameObject("LeanBar", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(_canvas.transform, false);

        _barRoot = root.GetComponent<RectTransform>();
        _barRoot.sizeDelta = new Vector2(_barWidth, _barHeight);

        Image bg = root.GetComponent<Image>();
        bg.sprite = _spBar;
        bg.color = new Color(0f, 0f, 0f, 0.45f);
        bg.raycastTarget = false;

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(_barRoot, false);
        _barFill = fill.GetComponent<RectTransform>();
        _barFillImg = fill.GetComponent<Image>();
        _barFillImg.sprite = _spBar;
        _barFillImg.raycastTarget = false;

        GameObject mark = new GameObject("Center", typeof(RectTransform), typeof(Image));
        mark.transform.SetParent(_barRoot, false);
        _barMark = mark.GetComponent<RectTransform>();
        _barMark.sizeDelta = new Vector2(2f, _barHeight + 8f);

        Image mi = mark.GetComponent<Image>();
        mi.sprite = _spBar;
        mi.color = new Color(1f, 1f, 1f, 0.5f);
        mi.raycastTarget = false;
    }

    // ───────────────────────── кадр ─────────────────────────

    private void LateUpdate()
    {
        if (!_built || _balance == null) return;
        Refresh(Time.deltaTime, false);
    }

    private void Refresh(float dt, bool instant)
    {
        CartBalance.Phase phase = _balance.CurrentPhase;
        bool show = _balance.UiVisible && _balance.IsLocalDriver;

        // Отсчёт отклика на попадание начинается заново в момент СМЕНЫ фазы,
        // а не каждый кадр: иначе скачок никогда не досчитает до конца.
        if (phase != _lastPhase)
        {
            _hitTime = 0f;
            _lastPhase = phase;
        }

        bool needLeft = _balance.RequiredLabel == "A" || _balance.RequiredLabel == "Q";

        Place(_left, -1f);
        Place(_right, +1f);

        // При раскладке «только нужная» вторая кнопка не просто тусклая — её
        // нет совсем. Меньше шума, но игрок не видит раскладку заранее.
        bool showL = show && (_layout == Layout.BothHighlight || needLeft);
        bool showR = show && (_layout == Layout.BothHighlight || !needLeft);

        Drive(_left, showL, needLeft, phase, dt, instant);
        Drive(_right, showR, !needLeft, phase, dt, instant);

        UpdateBrake(dt, instant);
        UpdateBar(show);
    }

    /// <summary>
    /// Тормоз. Без пульсации: пульсация означает «успей нажать», а здесь надо
    /// не успеть, а не отпускать. Обратная связь идёт цветом и заливкой.
    /// </summary>
    private void UpdateBrake(float dt, bool instant)
    {
        if (_brake == null) return;

        bool need = _balance.BrakeNeeded;
        float target = need ? 1f : 0f;

        _brake.group.alpha = instant
            ? target
            : Mathf.MoveTowards(_brake.group.alpha, target, dt / Mathf.Max(0.02f, _fade));

        _brake.root.anchoredPosition = new Vector2(0f, _brakeOffsetY * 1080f);

        if (_brake.group.alpha <= 0.001f) return;

        bool held = _balance.Braking;
        Color c = held ? _colorBraking : _colorBrake;

        _brake.plate.color = c;
        _brake.detail.color = Color.white;
        _brake.label.color = Luminance(c) > 0.55f
            ? new Color(0.12f, 0.09f, 0.05f, 1f) : Color.white;

        // Зажал — кнопка вдавливается. Отпустил — отпружинивает: физический
        // отклик подсказывает, что удержание засчитано, без единого слова.
        float scale = held ? 0.92f : 1f;
        _brake.root.localScale = Vector3.one * scale;

        _brake.ringImg.color = new Color(c.r, c.g, c.b, 0f);

        if (_brakeFill != null)
        {
            float m = _balance.Momentum01;
            _brakeFill.sizeDelta = new Vector2(_brakeSize * 0.92f * m, _brakeSize * 0.14f);
            _brakeFillImg.color = new Color(c.r, c.g, c.b, 0.9f);
        }
    }

    private void Place(Widget w, float side)
    {
        if (w == null) return;

        float x = _layout == Layout.BothHighlight ? side * _spread * 0.5f : 0f;
        float y = _verticalOffset * 1080f;

        w.home = new Vector2(x, y);
        w.root.anchoredPosition = w.home;
    }

    /// <summary>
    /// Вся анимация одного виджета.
    ///
    /// Масштаб пишется ТОЛЬКО в корень. Внутренние слои о пульсации не знают,
    /// поэтому стиль кнопки можно поменять, не трогая ни строчки отсюда.
    /// </summary>
    private void Drive(Widget w, bool visible, bool isRequired,
                       CartBalance.Phase phase, float dt, bool instant)
    {
        if (w == null) return;

        float targetAlpha = visible ? 1f : 0f;

        w.group.alpha = instant
            ? targetAlpha
            : Mathf.MoveTowards(w.group.alpha, targetAlpha, dt / Mathf.Max(0.02f, _fade));

        if (w.group.alpha <= 0.001f) return;

        // ── ЦВЕТ ──
        Color c = _colorIdle;

        if (!isRequired) c = _colorDim;
        else switch (phase)
        {
            case CartBalance.Phase.Warn: c = _colorWarn; break;
            case CartBalance.Phase.Call: c = _colorCall; break;
            case CartBalance.Phase.Hit:  c = _colorHit;  break;
            case CartBalance.Phase.Miss: c = _colorMiss; break;
        }

        w.plate.color = c;
        w.label.color = Luminance(c) > 0.55f
            ? new Color(0.12f, 0.09f, 0.05f, 1f)
            : Color.white;

        // Детали приглушаем у неактивной кнопки, иначе тусклая подложка с
        // яркими болтами выглядит ошибкой, а не «пока не нужна».
        w.detail.color = new Color(1f, 1f, 1f, isRequired ? 1f : 0.35f);

        // ── МАСШТАБ ──
        float scale = 1f;
        Vector2 offset = Vector2.zero;

        if (!isRequired)
        {
            scale = 0.82f;
        }
        else switch (phase)
        {
            case CartBalance.Phase.Warn:
                scale = Pulse(_idlePulse, _idlePeriod);
                break;

            case CartBalance.Phase.Call:
                scale = Pulse(_callPulse, _callPeriod);
                break;

            case CartBalance.Phase.Hit:
            {
                // Скачок с перелётом и быстрым возвратом: у попадания должен
                // быть отчётливый физический отклик, иначе игрок не уверен,
                // что нажатие засчитали.
                //
                // Затухание, а не пинг-понг: пинг-понг качал бы кнопку вечно,
                // пока фаза держится, и попадание выглядело бы как новая
                // пульсация вместо однократного отклика.
                _hitTime += dt;
                float k = Mathf.Clamp01(_hitTime / 0.35f);
                scale = Mathf.Lerp(_hitPop, 1f, k * k);
                break;
            }

            case CartBalance.Phase.Miss:
            {
                scale = 0.95f;

                // Тряска на шуме, а не на синусе: у синуса слышен период, и
                // промах начинает выглядеть анимацией, а не сбоем.
                float n = Mathf.PerlinNoise(_shakeSeed + Time.time * 34f, 0f) - 0.5f;
                offset = new Vector2(n * 2f * _missShake, 0f);
                break;
            }
        }

        w.root.localScale = Vector3.one * scale;
        w.root.anchoredPosition = w.home + offset;

        // ── КОЛЬЦО ──
        // Расходится только в фазе вызова: в остальное время оно бы просто
        // мельтешило и обесценило сигнал.
        if (isRequired && phase == CartBalance.Phase.Call)
        {
            float t = 1f - _balance.Window01;
            float k = Mathf.Repeat(t * 2.2f, 1f);

            w.ring.localScale = Vector3.one * Mathf.Lerp(0.88f, _ringMax, k);
            w.ringImg.color = new Color(c.r, c.g, c.b, (1f - k) * 0.85f);
        }
        else
        {
            w.ringImg.color = new Color(c.r, c.g, c.b, 0f);
        }

        // ── ВРАЩЕНИЕ ПРОТЕКТОРА ──
        // Крутится ТОЛЬКО кольцо деталей. Буква лежит отдельным слоем и
        // остаётся неподвижной: колесо, собранное одним спрайтом и покрученное
        // целиком, становится нечитаемым на втором обороте.
        if (_style == Style.Wheel)
        {
            float speed = phase == CartBalance.Phase.Call ? 190f : 55f;
            w.spin -= speed * dt;
            w.detailRt.localRotation = Quaternion.Euler(0f, 0f, w.spin);
        }
    }

    private float Pulse(float amount, float period)
    {
        float k = 0.5f + 0.5f * Mathf.Sin(Time.time / Mathf.Max(0.05f, period) * Mathf.PI * 2f);
        return Mathf.Lerp(1f, amount, k);
    }

    private void UpdateBar(bool show)
    {
        if (_barRoot == null) return;

        _barRoot.anchoredPosition = new Vector2(0f, _barOffset * 1080f);

        float lean = Mathf.Clamp(_balance.Lean, -1f, 1f);
        float danger = _balance.Danger;

        // Заливка растёт ОТ ЦЕНТРА в сторону падения. Полоска слева направо
        // показывала бы величину, но не сторону, а сторона здесь важнее:
        // именно она говорит, какую клавишу готовить.
        float half = _barWidth * 0.5f;
        float w = Mathf.Abs(lean) * half;

        _barFill.sizeDelta = new Vector2(w, _barHeight);
        _barFill.anchoredPosition = new Vector2(Mathf.Sign(lean) * w * 0.5f, 0f);

        _barFillImg.color = Color.Lerp(_colorWarn, _colorMiss, danger);

        float a = show ? 1f : Mathf.Clamp01(danger * 2.2f);
        _barRoot.gameObject.SetActive(a > 0.02f);

        Color bg = _barFillImg.color;
        bg.a = Mathf.Clamp01(a);
        _barFillImg.color = bg;
    }

    private static float Luminance(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

    private Sprite PlateSprite() => _style == Style.Nut ? _spPlateHex : _spPlateCircle;

    private Sprite DetailSprite() => _style switch
    {
        Style.Wheel => _spTread,
        Style.Nut => _spBevelHex,
        _ => _spGlossTop
    };

    // ═════════════════════ ГЕНЕРАЦИЯ ФОРМ ═════════════════════
    //
    //  Всё рисуется знаковым полем расстояния и сглаживается по ширине пикселя.
    //  Это дороже, чем залить пиксели по формуле, но даёт чистый край на любом
    //  размере — а кнопка QTE масштабируется постоянно, и лесенка на её
    //  контуре видна сразу.

    private static void EnsureSprites()
    {
        if (_spPlateCircle != null) return;

        const int N = 256;

        _spPlateCircle = Make(N, (x, y) =>
        {
            float d = Dist(x, y, N) - 0.44f;
            return AA(-d, N);
        });

        _spPlateHex = Make(N, (x, y) => AA(-(Hex(x, y, N) - 0.46f), N));

        // Протектор: блоки по кругу. Тёмные, поверх подложки.
        _spTread = Make(N, (x, y) =>
        {
            float d = Dist(x, y, N);
            if (d > 0.47f || d < 0.36f) return 0f;

            float ang = Mathf.Atan2(y - N * 0.5f, x - N * 0.5f);
            float seg = Mathf.Repeat(ang / (Mathf.PI * 2f) * 18f, 1f);

            float blk = seg < 0.55f ? 1f : 0f;
            return blk * 0.85f;
        }, dark: true);

        _spRing = Make(N, (x, y) =>
        {
            float d = Mathf.Abs(Dist(x, y, N) - 0.44f);
            return AA(0.022f - d, N);
        });

        // Фаска гайки: светлая полоса по верхней грани.
        _spBevelHex = Make(N, (x, y) =>
        {
            float h = Hex(x, y, N);
            if (h > 0.46f) return 0f;

            float top = 1f - Mathf.Clamp01((y - N * 0.5f) / (N * 0.42f) + 0.5f);
            return Mathf.Clamp01(top - 0.55f) * 0.7f;
        });

        // Блик кнопки: эллипс в верхней части.
        _spGlossTop = Make(N, (x, y) =>
        {
            float dx = (x - N * 0.5f) / (N * 0.30f);
            float dy = (y - N * 0.68f) / (N * 0.14f);
            return AA(1f - Mathf.Sqrt(dx * dx + dy * dy), N) * 0.55f;
        });

        _spBar = Make(8, (x, y) => 1f);
    }

    private static float Dist(int x, int y, int n)
    {
        float dx = (x + 0.5f) / n - 0.5f;
        float dy = (y + 0.5f) / n - 0.5f;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Знаковое поле шестиугольника. Плоской стороной вверх.</summary>
    private static float Hex(int x, int y, int n)
    {
        float px = Mathf.Abs((x + 0.5f) / n - 0.5f);
        float py = Mathf.Abs((y + 0.5f) / n - 0.5f);

        const float k = 0.8660254f;   // корень из трёх пополам
        return Mathf.Max(px * k + py * 0.5f, py);
    }

    /// <summary>Сглаживание шириной ровно в пиксель — контур чистый на любом
    /// размере кнопки.</summary>
    private static float AA(float sd, int n) => Mathf.Clamp01(sd * n * 0.9f + 0.5f);

    private static Sprite Make(int n, System.Func<int, int, float> f, bool dark = false)
    {
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };

        Color[] px = new Color[n * n];
        float v = dark ? 0.08f : 1f;

        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = new Color(v, v, v, Mathf.Clamp01(f(x, y)));

        tex.SetPixels(px);
        tex.Apply(false, false);

        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }
}
