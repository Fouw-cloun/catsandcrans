using FishNet.Object;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ЧАСЫ, ВРЕЗАННЫЕ В ВЕРХНЮЮ ГРАНЬ ЭКРАНА.
///
/// Корпус прижат к верху якорем (0.5, 1) и пивотом (0.5, 1), поэтому сигнальная
/// лента на его кромке продолжает грань экрана при любом разрешении.
///
/// Циферблат — фаянсовая тарелка, стрелки — ложка (часовая, короткая) и вилка
/// (минутная, длинная). Время читается по длине стрелок, как на обычных часах.
///
/// ─────────────────────────────────────────────────────────────────────────
/// РАЗМЕРЫ
///
/// Ширина и высота корпуса задаются НЕЗАВИСИМО через UiLayout: корпус можно
/// растягивать и сплющивать. Циферблат при этом остаётся круглым — его диаметр
/// берётся как меньшее из двух ограничений выреза, по ширине и по высоте.
///
/// Диаметр тарелки считается от реального выреза в картинке (384 px при
/// ширине 900, то есть 0.427 ширины). Раньше здесь стояло 0.8 ширины — почти
/// вдвое больше выреза, из-за чего тарелка вылезала за корпус сверху и снизу
/// и часы выглядели гигантскими. Коэффициенты живут в UiLayout, рядом с
/// пояснением, откуда взяты.
///
/// Ползунки в меню меняют вёрстку вживую через событие UiLayout.Changed —
/// канвас не пересобирается, меняются только размеры существующих
/// RectTransform.
/// ─────────────────────────────────────────────────────────────────────────
///
/// КУДА ВЕШАТЬ: префаб игрока, рядом с CatPawCrosshair.
/// </summary>
[DisallowMultipleComponent]
public class GameClockUI : NetworkBehaviour
{
    [Header("Что показывать")]
    [SerializeField] private bool _showDigital = true;
    [SerializeField] private bool _showDayNumber = true;
    [SerializeField] private bool _showSecondHand;

    [Header("Цвета")]
    [SerializeField] private Color _digitalColor = new Color32(0xFF, 0xD8, 0x4D, 0xFF);
    [SerializeField] private Color _dayColor = new Color32(0xD1, 0xE1, 0xEE, 0xFF);
    [SerializeField] private Color _lateColor = new Color32(0xFE, 0x3B, 0x5B, 0xFF);
    [SerializeField] private Color _housingTint = Color.white;

    [Header("Тревога в конце смены")]
    [Tooltip("За сколько игровых часов до конца смены часы начинают мигать")]
    [SerializeField] private float _alarmHours = 1f;

    [Header("Диагностика")]
    [SerializeField] private bool _verboseSetup = true;
    [Tooltip("Крутить стрелки самостоятельно, если GameClock в сцене нет")]
    [SerializeField] private bool _runWithoutGameClock;
    [Tooltip("Построить часы даже у чужих игроков. Только для отладки")]
    [SerializeField] private bool _buildForEveryone;

    // ───────── внутреннее ─────────

    private Canvas _canvas;
    private RectTransform _root;
    private Image _housing;
    private RectTransform _faceRoot;
    private RectTransform _plate;
    private RectTransform _hourHand;
    private RectTransform _minuteHand;
    private RectTransform _secondHand;
    private RectTransform _cap;
    private Text _digital;
    private Text _dayLabel;

    private float _fakeHours = 8f;
    private bool _built;
    private bool _warnedNoClock;

    // ───────── жизненный цикл ─────────

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (_verboseSetup)
            Debug.Log($"[GameClockUI] OnStartClient, IsOwner = {base.IsOwner}", this);

        if (!base.IsOwner && !_buildForEveryone)
        {
            enabled = false;
            return;
        }

        UiLayout.Load();
        Build();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        Teardown();
    }

    private void OnDestroy() => Teardown();

    private void Teardown()
    {
        if (_built) UiLayout.Changed -= ApplyLayout;

        if (_canvas != null)
        {
            Destroy(_canvas.gameObject);
            _canvas = null;
        }
        _built = false;
    }

    // ───────── сборка ─────────

    private void Build()
    {
        if (_built) return;

        if (UiKit.ClockHousing == null)
        {
            Debug.LogError("[GameClockUI] Спрайты часов не загрузились. " +
                           "Проверь, что PNG лежат в Assets/Resources/UI/.", this);
            return;
        }

        _canvas = UiKit.MakeCanvas("HUD_Clock", 90, false);

        // Размеры здесь произвольные — сразу после сборки их выставит
        // ApplyLayout, и он же будет зваться при каждом движении ползунка.
        _root = UiKit.MakeRect(_canvas.transform, "Clock",
            Vector2.one * 100f, Vector2.zero,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

        _housing = UiKit.MakeImage(_root, "Housing", UiKit.ClockHousing,
            Vector2.one * 100f, Vector2.zero, _housingTint);

        _faceRoot = UiKit.MakeRect(_root, "Face", Vector2.one * 100f, Vector2.zero);

        _plate = UiKit.MakeImage(_faceRoot, "Plate", UiKit.ClockFace,
            Vector2.one * 100f, Vector2.zero, Color.white).rectTransform;

        _hourHand = MakeHand(_faceRoot, "HourHand_Spoon", UiKit.HandSpoon);
        _minuteHand = MakeHand(_faceRoot, "MinuteHand_Fork", UiKit.HandFork);

        if (_showSecondHand)
        {
            _secondHand = MakeHand(_faceRoot, "SecondHand", UiKit.HandFork);
            _secondHand.GetComponent<Image>().color = _lateColor;
        }

        _cap = UiKit.MakeImage(_faceRoot, "Cap", UiKit.ClockCap,
            Vector2.one * 20f, Vector2.zero, Color.white).rectTransform;

        if (_showDigital)
        {
            _digital = UiKit.MakeText(_root, "Digital", "08:00", 20,
                Vector2.one * 40f, Vector2.zero, _digitalColor);
        }

        if (_showDayNumber)
        {
            _dayLabel = UiKit.MakeText(_root, "DayLabel", "смена 1", 12,
                Vector2.one * 30f, Vector2.zero, _dayColor);
        }

        _built = true;

        ApplyLayout();
        UiLayout.Changed += ApplyLayout;

        if (_verboseSetup)
            Debug.Log("[GameClockUI] Канвас HUD_Clock создан", this);
    }

    private RectTransform MakeHand(Transform parent, string name, Sprite sprite)
    {
        // Пивот у основания, чуть выше самого низа — короткий противовес за
        // осью, как у настоящих стрелок.
        RectTransform rt = UiKit.MakeRect(parent, name, Vector2.one * 10f,
            Vector2.zero, null, new Vector2(0.5f, 0.06f));
        rt.anchoredPosition = Vector2.zero;

        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.preserveAspect = false;
        return rt;
    }

    // ───────── вёрстка ─────────

    // ── Разметка корпуса, доли от ВЫСОТЫ, отсчёт ОТ ЦЕНТРА, вверх положительно ──
    //
    // Внимание на систему координат. У корня часов якорь и пивот в верху экрана,
    // но дочерние элементы UiKit.MakeRect создаёт с якорем ПО ЦЕНТРУ родителя.
    // Поэтому смещения тут считаются от центра корпуса, а не от его верхней
    // кромки. Ровно на этом я и ошибся в прошлой версии: считал доли от верха,
    // и всё содержимое уезжало вниз на половину высоты — тарелка вываливалась
    // под корпус, а цифры оказывались на песке.
    //
    // Числа получены из исходного PNG (900 × 540):
    //   вырез под тарелку   — центр 240 → 0.5 - 240/540 = +0.0556
    //   низ жёлтого корпуса — 432      → 0.5 - 432/540 = -0.3000
    //   тёмная табличка     — 366..520 → видна её часть ниже корпуса

    private const float FaceOffsetY = 0.0556f;
    private const float DigitalOffsetY = -0.345f;
    private const float DayOffsetY = -0.425f;

    /// <summary>Пересчитывает размеры и позиции под текущие значения UiLayout.
    /// Зовётся при сборке и на каждое движение ползунка в меню.</summary>
    private void ApplyLayout()
    {
        if (!_built) return;

        float w = UiLayout.ClockWidth;
        float h = UiLayout.ClockHeight;

        _root.sizeDelta = new Vector2(w, h);
        _root.anchoredPosition = new Vector2(0f, UiLayout.ClockOffsetY);

        // Корпус растягивается вместе с рамкой — он и есть фон.
        _housing.rectTransform.sizeDelta = new Vector2(w, h);

        // Тарелка круглая всегда: диаметр ограничен и шириной, и высотой выреза,
        // поэтому при сплющивании корпуса она не превращается в эллипс.
        float face = UiLayout.ClockFaceDiameter(w, h);

        _faceRoot.sizeDelta = new Vector2(face, face);
        _faceRoot.anchoredPosition = new Vector2(0f, h * FaceOffsetY);
        _plate.sizeDelta = new Vector2(face, face);

        // Стрелки и гайка масштабируются от тарелки, а не от корпуса: при
        // растягивании корпуса пропорции циферблата остаются правильными.
        _hourHand.sizeDelta = new Vector2(face * 0.155f, face * 0.30f);
        _minuteHand.sizeDelta = new Vector2(face * 0.135f, face * 0.43f);
        if (_secondHand != null)
            _secondHand.sizeDelta = new Vector2(face * 0.05f, face * 0.46f);

        _cap.sizeDelta = new Vector2(face * 0.13f, face * 0.13f);

        // Цифры ложатся в тёмную табличку, запечённую в нижней части картинки.
        if (_digital != null)
        {
            _digital.rectTransform.sizeDelta = new Vector2(w * 0.5f, h * 0.14f);
            _digital.rectTransform.anchoredPosition = new Vector2(0f, h * DigitalOffsetY);
            _digital.fontSize = Mathf.Max(9, Mathf.RoundToInt(h * 0.10f));
        }

        if (_dayLabel != null)
        {
            _dayLabel.rectTransform.sizeDelta = new Vector2(w * 0.6f, h * 0.09f);
            _dayLabel.rectTransform.anchoredPosition = new Vector2(0f, h * DayOffsetY);
            _dayLabel.fontSize = Mathf.Max(7, Mathf.RoundToInt(h * 0.058f));
        }
    }

    // ───────── каждый кадр ─────────

    private void Update()
    {
        if (!_built || _canvas == null) return;

        bool hidden = UiModal.AnyOpen;
        if (_canvas.enabled == hidden) _canvas.enabled = !hidden;
        if (hidden) return;

        float hours;
        int day;
        GameClock clock = GameClock.Instance;

        if (clock != null)
        {
            hours = clock.Hours;
            day = clock.Day;
        }
        else
        {
            if (!_warnedNoClock && _verboseSetup)
            {
                _warnedNoClock = true;
                Debug.LogWarning("[GameClockUI] GameClock.Instance пуст. Создай в " +
                                 "сцене пустой объект с NetworkObject и GameClock.", this);
            }

            if (!_runWithoutGameClock) return;

            _fakeHours = Mathf.Repeat(_fakeHours + Time.deltaTime * 0.5f, 24f);
            hours = _fakeHours;
            day = 1;
        }

        // Часовая делает два оборота за сутки, минутная — двадцать четыре.
        float hourAngle = Mathf.Repeat(hours, 12f) / 12f * 360f;
        float minuteAngle = (hours - Mathf.Floor(hours)) * 360f;

        _hourHand.localRotation = Quaternion.Euler(0f, 0f, -hourAngle);
        _minuteHand.localRotation = Quaternion.Euler(0f, 0f, -minuteAngle);

        if (_secondHand != null)
        {
            float seconds = hours * 3600f % 60f;
            _secondHand.localRotation = Quaternion.Euler(0f, 0f, -seconds / 60f * 360f);
        }

        if (_digital != null)
        {
            int hh = Mathf.FloorToInt(hours) % 24;
            int mm = Mathf.FloorToInt((hours - Mathf.Floor(hours)) * 60f);
            _digital.text = string.Format("{0:00}:{1:00}", hh, mm);
        }

        if (_dayLabel != null) _dayLabel.text = "смена " + day;

        UpdateAlarm(clock);
    }

    /// <summary>Конец смены близко — корпус и цифры пульсируют красным.</summary>
    private void UpdateAlarm(GameClock clock)
    {
        bool alarm = clock != null && clock.IsWorkTime
                     && clock.HoursLeftInShift <= _alarmHours;

        if (!alarm)
        {
            _housing.color = _housingTint;
            if (_digital != null) _digital.color = _digitalColor;
            return;
        }

        float pulse = Mathf.Sin(Time.time * 6f) * 0.5f + 0.5f;
        _housing.color = Color.Lerp(_housingTint, _lateColor, pulse * 0.55f);
        if (_digital != null)
            _digital.color = Color.Lerp(_digitalColor, _lateColor, pulse);
    }
}
