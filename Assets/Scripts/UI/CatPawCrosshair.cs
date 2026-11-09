using FishNet.Object;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ПРИЦЕЛ-ЛАПКА. Три нарисованные позы, между которыми идёт перекрёстное
/// затухание плюс короткий пружинистый подскок.
///
///   Покой   — лапка спокойная, полупрозрачная.
///   Разжата — под прицелом то, что можно взять: пальцы растопырены,
///             коготки выпущены, цвет уходит в жёлтый каски.
///   Кулачок — предмет в руке: пальцы поджаты, подушечки пальцев не видны.
///
/// Горячо и Нельзя переиспользуют растопыренную позу с другим цветом.
///
/// ─────────────────────────────────────────────────────────────────────────
/// ПОЧЕМУ NetworkBehaviour, А НЕ MonoBehaviour
///
/// Раньше канвас строился в Unity-методе Start() и там же проверялся IsOwner.
/// Это не работает: у объекта, заспавненного FishNet, Start() может отработать
/// РАНЬШЕ, чем назначено владение. IsOwner в этот момент ещё false, скрипт
/// выключал сам себя и больше не просыпался — прицела не было ни у кого,
/// включая хоста, и в иерархии не появлялось вообще ничего.
///
/// OnStartClient вызывается FishNet уже после инициализации объекта, и там
/// IsOwner достоверен. Это единственное надёжное место для такой проверки.
/// ─────────────────────────────────────────────────────────────────────────
///
/// КУДА ВЕШАТЬ: корень префаба игрока, рядом с PlayerInteractor и PlayerHand.
/// На объекте обязателен NetworkObject — он на префабе игрока и так есть.
/// </summary>
[DisallowMultipleComponent]
public class CatPawCrosshair : NetworkBehaviour
{
    public enum PawState { Idle, Open, Grab, Hot, Blocked }

    private enum Pose { Idle = 0, Open = 1, Grab = 2 }

    [Header("Ссылки (найдутся сами, если пусто)")]
    [SerializeField] private Transform _aimOrigin;

    [Header("Луч поиска цели")]
    [Tooltip("Перезаписывается из PlayerInteractor.Reach, если тот найден")]
    [SerializeField] private float _reach = 3f;
    [SerializeField] private LayerMask _interactMask = ~0;
    [Tooltip("Опросов цели в секунду. Каждый кадр не нужно")]
    [SerializeField] private float _scanRate = 20f;

    [Header("Кнопки сжатия")]
    [SerializeField] private bool _clenchOnLeftMouse = true;
    [SerializeField] private bool _clenchOnInteractKey = true;
    [SerializeField] private KeyCode _interactKey = KeyCode.F;
    [Tooltip("Сколько держится мгновенное сжатие до ответа сервера, сек")]
    [SerializeField] private float _clenchHold = 0.22f;

    [Header("Дополнительные состояния")]
    [SerializeField] private bool _useHotState = true;
    [SerializeField] private bool _useBlockedState;

    [Header("Цвета")]
    [SerializeField] private Color _idleColor = new Color32(0xFF, 0xAF, 0x34, 0xFF);
    [SerializeField] private Color _openColor = new Color32(0xFF, 0xD8, 0x4D, 0xFF);
    [SerializeField] private Color _grabColor = new Color32(0xAD, 0x41, 0x00, 0xFF);
    [SerializeField] private Color _hotColor = new Color32(0xFE, 0x3B, 0x5B, 0xFF);
    [SerializeField] private Color _blockedColor = new Color32(0x8E, 0xA3, 0xB8, 0xFF);
    [Tooltip("Непройденная часть шкалы прогресса")]
    [SerializeField] private Color _emptyColor = new Color32(0x5A, 0x55, 0x71, 0xFF);

    [Header("Анимация")]
    [SerializeField] private float _fadeSpeed = 16f;
    [SerializeField] private float _colorSpeed = 12f;
    [Tooltip("Насколько лапка подпрыгивает при смене позы")]
    [Range(0f, 0.5f)][SerializeField] private float _popScale = 0.16f;
    [SerializeField] private float _popSpeed = 15f;
    [SerializeField] private float _hotShake = 1.6f;

    [Header("Подсветка объекта")]
    [SerializeField] private bool _driveHighlight = true;

    [Header("Диагностика")]
    [Tooltip("Писать в консоль, что происходит при запуске. " +
             "Включи, если прицел не появился")]
    [SerializeField] private bool _verboseSetup = true;
    [Tooltip("Построить прицел даже у чужих игроков. Только для отладки — " +
             "в игре получишь по прицелу на каждого участника")]
    [SerializeField] private bool _buildForEveryone;

    // ───────── внутреннее ─────────

    private const int PoseCount = 3;
    private const float SpriteAspect = 400f / 420f;

    private readonly RectTransform[] _poseRect = new RectTransform[PoseCount];
    private readonly CanvasGroup[] _poseGroup = new CanvasGroup[PoseCount];
    private readonly Image[] _poseFill = new Image[PoseCount];
    private readonly Image[] _poseLine = new Image[PoseCount];
    private readonly Image[] _poseEmpty = new Image[PoseCount];
    private readonly float[] _poseAlpha = new float[PoseCount];

    private Canvas _canvas;
    private RectTransform _root;
    private PlayerInteractor _interactor;
    private PlayerHand _hand;

    private PawState _state = PawState.Idle;
    private Pose _pose = Pose.Idle;
    private bool _targetPickable;
    private Color _currentColor;
    private float _currentAlpha;
    private float _progress = -1f;
    private float _nextScan;
    private float _clenchUntil;
    private float _pop;
    private float _popVel;
    private bool _built;

    private InteractableHighlight _litTarget;

    // ───────── жизненный цикл ─────────

    private void Awake()
    {
        _interactor = GetComponent<PlayerInteractor>();
        _hand = GetComponent<PlayerHand>();
        _currentColor = _idleColor;
        _currentAlpha = UiLayout.CrosshairAlpha;
    }

    /// <summary>Единственное надёжное место для проверки владения: к моменту
    /// вызова FishNet уже полностью инициализировал объект.</summary>
    public override void OnStartClient()
    {
        base.OnStartClient();

        if (_verboseSetup)
            Debug.Log($"[CatPawCrosshair] OnStartClient, IsOwner = {base.IsOwner}", this);

        if (!base.IsOwner && !_buildForEveryone)
        {
            // Чужой игрок — прицел ему не нужен. Скрипт выключаем только
            // ЗДЕСЬ, после достоверной проверки, а не в Start().
            enabled = false;
            return;
        }

        // Параметры луча берём у интерактора, чтобы прицел целился ровно туда,
        // куда сработает F. Своя копия чисел в инспекторе рано или поздно
        // разъезжается с настоящей.
        if (_interactor != null)
        {
            _aimOrigin = _interactor.AimOrigin;
            _reach = _interactor.Reach;
            _interactMask = _interactor.InteractMask;
        }
        else if (_aimOrigin == null)
        {
            Camera cam = GetComponentInChildren<Camera>(true);
            if (cam == null) cam = Camera.main;
            if (cam != null) _aimOrigin = cam.transform;

            if (_verboseSetup)
                Debug.LogWarning("[CatPawCrosshair] PlayerInteractor не найден, " +
                                 "параметры луча взяты из инспектора", this);
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

        if (_litTarget != null)
        {
            _litTarget.SetHighlighted(false);
            _litTarget = null;
        }
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

        Sprite[] fills = { UiKit.PawIdleFill, UiKit.PawOpenFill, UiKit.PawGrabFill };
        Sprite[] lines = { UiKit.PawIdleLine, UiKit.PawOpenLine, UiKit.PawGrabLine };

        if (fills[0] == null)
        {
            Debug.LogError("[CatPawCrosshair] Спрайты лапы не загрузились. " +
                           "Проверь, что PNG лежат в Assets/Resources/UI/ — " +
                           "именно так, с папкой Resources в пути.", this);
            return;
        }

        _canvas = UiKit.MakeCanvas("HUD_PawCrosshair", 100, false);

        // Размеры тут временные — сразу после сборки их выставит ApplyLayout,
        // он же зовётся на каждое движение ползунка в меню.
        Vector2 tmp = Vector2.one * 40f;
        _root = UiKit.MakeRect(_canvas.transform, "Paw", tmp, Vector2.zero);

        string[] names = { "Idle", "Open", "Grab" };

        for (int i = 0; i < PoseCount; i++)
        {
            RectTransform g = UiKit.MakeRect(_root, "Pose" + names[i], tmp, Vector2.zero);
            _poseRect[i] = g;
            _poseGroup[i] = g.gameObject.AddComponent<CanvasGroup>();
            _poseGroup[i].alpha = i == 0 ? 1f : 0f;
            _poseGroup[i].blocksRaycasts = false;
            _poseGroup[i].interactable = false;

            // Порядок слоёв важен: подложка шкалы, заливка, контур сверху.
            // Если контур уйдёт под заливку, лапка растворится на песке.
            _poseEmpty[i] = UiKit.MakeImage(g, "Empty", fills[i],
                tmp, Vector2.zero, _emptyColor);
            _poseEmpty[i].enabled = false;

            _poseFill[i] = UiKit.MakeImage(g, "Fill", fills[i],
                tmp, Vector2.zero, _idleColor);
            _poseFill[i].type = Image.Type.Filled;
            _poseFill[i].fillMethod = Image.FillMethod.Vertical;
            _poseFill[i].fillOrigin = (int)Image.OriginVertical.Bottom;
            _poseFill[i].fillAmount = 1f;

            _poseLine[i] = UiKit.MakeImage(g, "Line", lines[i],
                tmp, Vector2.zero, Color.white);
        }

        _poseAlpha[0] = 1f;
        _built = true;

        ApplyLayout();
        UiLayout.Changed += ApplyLayout;

        if (_verboseSetup)
            Debug.Log("[CatPawCrosshair] Канвас HUD_PawCrosshair создан", this);
    }

    // ───────── вёрстка ─────────

    /// <summary>Пересчитывает размер под текущее значение UiLayout. Зовётся при
    /// сборке и на каждое движение ползунка. Канвас при этом не пересобирается —
    /// меняются только sizeDelta существующих прямоугольников.</summary>
    private void ApplyLayout()
    {
        if (!_built) return;

        float w = UiLayout.CrosshairSize;
        float h = w * SpriteAspect;
        var size = new Vector2(w, h);

        _root.sizeDelta = size;

        for (int i = 0; i < PoseCount; i++)
        {
            _poseRect[i].sizeDelta = size;
            _poseEmpty[i].rectTransform.sizeDelta = size;
            _poseFill[i].rectTransform.sizeDelta = size;
            _poseLine[i].rectTransform.sizeDelta = size;
        }
    }

    // ───────── каждый кадр ─────────

    private void Update()
    {
        if (!_built || _root == null) return;

        bool hidden = UiModal.AnyOpen;
        if (_canvas.enabled == hidden) _canvas.enabled = !hidden;
        if (hidden)
        {
            SetTargetHighlight(null);
            return;
        }

        if (Time.time >= _nextScan)
        {
            _nextScan = Time.time + 1f / Mathf.Max(1f, _scanRate);
            Scan();
        }

        ReadClenchInput();
        ResolveState();

        if (_interactor != null && _interactor.ChargeFraction01 > 0f)
            _progress = _interactor.ChargeFraction01;

        Animate();
    }

    /// <summary>Мгновенная реакция на нажатие, не дожидаясь сервера. Только
    /// если под прицелом есть что брать — иначе кулачок дёргался бы от любого
    /// клика в пустоту.</summary>
    private void ReadClenchInput()
    {
        if (!_targetPickable) return;

        bool pressed = (_clenchOnLeftMouse && Input.GetMouseButtonDown(0))
                       || (_clenchOnInteractKey && Input.GetKeyDown(_interactKey));

        if (pressed) _clenchUntil = Time.time + _clenchHold;
    }

    private void ResolveState()
    {
        PawState prev = _state;
        bool holding = _hand != null && _hand.HasItem;

        if (holding || Time.time < _clenchUntil)
        {
            _state = PawState.Grab;
        }
        else if (_state == PawState.Hot && _useHotState)
        {
            // выставлено в Scan, живёт до следующего опроса
        }
        else if (_state == PawState.Blocked && _useBlockedState)
        {
        }
        else
        {
            _state = _targetPickable ? PawState.Open : PawState.Idle;
        }

        Pose want = _state switch
        {
            PawState.Grab => Pose.Grab,
            PawState.Idle => Pose.Idle,
            _ => Pose.Open      // Open, Hot и Blocked делят одну позу
        };

        if (want != _pose)
        {
            _pose = want;
            // Подскок при смене позы — без него переход выглядит вялым.
            _popVel += _popSpeed * _popScale;
        }
        else if (prev != _state)
        {
            // Смена только цвета (Open → Hot) тоже заслуживает отклика.
            _popVel += _popSpeed * _popScale * 0.4f;
        }
    }

    /// <summary>Порядок разбора повторяет PlayerInteractor, чтобы прицел
    /// показывал ровно то действие, которое реально сработает.</summary>
    private void Scan()
    {
        _targetPickable = false;

        Transform origin = _aimOrigin != null ? _aimOrigin : transform;
        bool hit = Physics.Raycast(origin.position, origin.forward,
            out RaycastHit info, _reach, _interactMask, QueryTriggerInteraction.Ignore);

        if (!hit)
        {
            _state = PawState.Idle;
            SetTargetHighlight(null);
            if (_interactor == null || _interactor.ChargeFraction01 <= 0f) _progress = -1f;
            return;
        }

        // Горячее важнее прочего: обжечься можно и с занятой рукой.
        if (_useHotState)
        {
            CatCooks.Food.IPotBody thermal = info.collider.GetComponentInParent<CatCooks.Food.IPotBody>();
            if (thermal != null && thermal.Temp01 > 0.6f)
            {
                _state = PawState.Hot;
                SetTargetHighlight(info.collider.GetComponentInParent<InteractableHighlight>());
                return;
            }
        }

        // Часть техники проверяется ПЕРЕД предметом: рычаг живёт внутри крана,
        // и без этого порядка подсветился бы весь кран.
        IVehiclePart part = info.collider.GetComponentInParent<IVehiclePart>();
        if (part != null && part.OwningNetworkObject != null)
        {
            _targetPickable = true;
            SetTargetHighlight(info.collider.GetComponentInParent<InteractableHighlight>());
            return;
        }

        InteractableItem item = info.collider.GetComponentInParent<InteractableItem>();
        if (item == null)
        {
            _state = PawState.Idle;
            SetTargetHighlight(null);
            return;
        }

        bool holding = _hand != null && _hand.HasItem;
        // Занятая рука блокирует F по миру — кроме предметов, которые сами это
        // разрешают (ключ на гидранте, кольцо шланга, дуло пылесоса).
        bool blockedByHand = holding && !_hand.HeldItem.AllowInteractWhileHeld;

        if (blockedByHand || !item.CanPickup)
        {
            if (_useBlockedState) _state = PawState.Blocked;
            SetTargetHighlight(null);
            return;
        }

        _targetPickable = true;
        SetTargetHighlight(info.collider.GetComponentInParent<InteractableHighlight>());

        FoodItem food = item as FoodItem;
        _progress = food != null && food.Progress01 > 0f ? food.Progress01 : -1f;
    }

    private void SetTargetHighlight(InteractableHighlight hl)
    {
        if (!_driveHighlight || _litTarget == hl) return;
        if (_litTarget != null) _litTarget.SetHighlighted(false);
        _litTarget = hl;
        if (_litTarget != null) _litTarget.SetHighlighted(true);
    }

    // ───────── анимация ─────────

    private void Animate()
    {
        Color targetColor = _state switch
        {
            PawState.Open => _openColor,
            PawState.Hot => _hotColor,
            PawState.Blocked => _blockedColor,
            PawState.Grab => _grabColor,
            _ => _idleColor
        };

        float targetAlpha = _state == PawState.Idle ? UiLayout.CrosshairAlpha : 1f;

        // Экспоненциальное сглаживание: не зависит от частоты кадров, в отличие
        // от Lerp с постоянным коэффициентом.
        float tc = 1f - Mathf.Exp(-_colorSpeed * Time.deltaTime);
        _currentColor = Color.Lerp(_currentColor, targetColor, tc);
        _currentAlpha = Mathf.Lerp(_currentAlpha, targetAlpha, tc);

        float tf = 1f - Mathf.Exp(-_fadeSpeed * Time.deltaTime);
        int active = (int)_pose;
        for (int i = 0; i < PoseCount; i++)
        {
            _poseAlpha[i] = Mathf.Lerp(_poseAlpha[i], i == active ? 1f : 0f, tf);

            bool visible = _poseAlpha[i] > 0.004f;
            if (_poseGroup[i].gameObject.activeSelf != visible)
                _poseGroup[i].gameObject.SetActive(visible);

            if (visible) _poseGroup[i].alpha = _poseAlpha[i] * _currentAlpha;
        }

        // Подскок: затухающая пружина к нулю.
        _popVel -= _pop * _popSpeed * _popSpeed * 0.06f;
        _popVel -= _popVel * 9f * Time.deltaTime;
        _pop += _popVel * Time.deltaTime;

        Vector2 shake = Vector2.zero;
        if (_state == PawState.Hot)
        {
            // Частоты взаимно простые — иначе дрожь получила бы заметный цикл.
            shake = new Vector2(Mathf.Sin(Time.time * 47f) * _hotShake,
                                Mathf.Sin(Time.time * 61f) * _hotShake);
        }

        _root.anchoredPosition = shake;
        float s = 1f + _pop;
        _root.localScale = new Vector3(s, s, 1f);

        ApplyColor();
        ApplyProgress();
    }

    private void ApplyColor()
    {
        for (int i = 0; i < PoseCount; i++)
        {
            _poseFill[i].color = _currentColor;
            _poseEmpty[i].color = _emptyColor;
            _poseLine[i].color = Color.white;
        }
    }

    /// <summary>Шкала заполняет силуэт снизу вверх — лапка наливается цветом
    /// по мере готовности.</summary>
    private void ApplyProgress()
    {
        bool on = _progress >= 0f;

        for (int i = 0; i < PoseCount; i++)
        {
            if (_poseEmpty[i].enabled != on) _poseEmpty[i].enabled = on;
            _poseFill[i].fillAmount = on ? Mathf.Clamp01(_progress) : 1f;
        }
    }

    // ───────── публичный API ─────────

    /// <summary>Показать шкалу на лапке, 0..1 — для мини-игр готовки.</summary>
    public void SetProgress(float t) => _progress = Mathf.Clamp01(t);

    public void ClearProgress() => _progress = -1f;

    public void SetVisible(bool visible)
    {
        if (_canvas != null) _canvas.enabled = visible;
    }
}
