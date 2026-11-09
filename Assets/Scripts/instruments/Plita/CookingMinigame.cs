using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// МИНИ-ИГРА: ДЕРЖАТЬ ТЕМПЕРАТУРУ В КОРИДОРЕ.
/// Вешается на кастрюлю, рядом с PotThermal и CookingVessel.
///
/// ПРАВИЛА:
///   у блюда есть коридор — например, варка это 95..105 градусов;
///   пока температура в нём, готовка идёт и копится качество;
///   вышла за коридор — готовка почти встаёт, а качество проседает;
///   ушла совсем далеко — начинает портиться.
///
/// А температура сама не стоит: конфорка гуляет. Значит подходить и
/// подкручивать придётся — но не каждую секунду, а раз в полминуты.
///
/// ПОЧЕМУ ЭТО РАБОТАЕТ КАК ИГРА:
///   наказание мягкое и обратимое — переварил чуть, потерял пару процентов
///   качества. Провалить блюдо можно, только забив совсем. Поэтому следить
///   хочется, а не приходится.
///
/// Оценка честно уходит в журнал обработки: точность удержания и есть
/// качество варки.
/// </summary>
[RequireComponent(typeof(CookingVessel))]
[RequireComponent(typeof(PotThermal))]
public class CookingMinigame : NetworkBehaviour
{
    [Header("Что готовим")]
    [Tooltip("Обработка, которая идёт при нагреве. Обычно — варка.")]
    [SerializeField] private ProcessDef _process;

    [Tooltip("Секунд в коридоре, чтобы блюдо приготовилось")]
    [Min(1f)][SerializeField] private float _secondsToCook = 45f;

    [Header("Коридор температуры")]
    [Tooltip("Нижняя граница, градусы")]
    [SerializeField] private float _low = 95f;

    [Tooltip("Верхняя граница, градусы")]
    [SerializeField] private float _high = 105f;

    [Tooltip("Насколько мягкие края коридора, градусы. Внутри этой полосы " +
             "качество падает плавно, а не обрывается.")]
    [SerializeField] private float _softEdge = 12f;

    [Header("Наказание")]
    [Tooltip("Насколько медленнее идёт готовка совсем вне коридора. " +
             "0.15 = почти встала, но не насмерть.")]
    [Range(0f, 1f)][SerializeField] private float _outsideSpeed = 0.15f;

    [Tooltip("Минимальное качество, ниже которого не падает даже при полном " +
             "разгильдяйстве. Блюдо выйдет плохим, но выйдет.")]
    [Range(0f, 0.6f)][SerializeField] private float _minQuality = 0.15f;

    [Header("Требуется содержимое")]
    [Tooltip("Не готовить пустую посуду")]
    [SerializeField] private bool _requireContents = true;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud = true;

    // ───────── сеть ─────────
    /// <summary>Насколько сейчас попадаем в коридор, 0..255 — для градусника
    /// и подсказок. Считает сервер, показывают все.</summary>
    private readonly SyncVar<byte> _accuracy = new SyncVar<byte>(0);

    private readonly SyncVar<bool> _cooking = new SyncVar<bool>(false);

    private CookingVessel _vessel;
    private PotThermal _thermal;

    public float Low => _low;
    public float High => _high;
    public float Target => (_low + _high) * 0.5f;

    /// <summary>Насколько точно держим, 0..1. Единица — ровно в коридоре.</summary>
    public float Accuracy01 => _accuracy.Value / 255f;

    public bool IsCooking => _cooking.Value;
    public float Progress01 => _vessel != null ? _vessel.CookProgress01 : 0f;

    /// <summary>Температура в долях коридора: 0 — на нижней границе,
    /// 1 — на верхней. Нужна градуснику, чтобы рисовать метку.</summary>
    public float BandPosition01 => Mathf.InverseLerp(_low, _high, _thermal != null
        ? _thermal.Temperature : 0f);

    private void Awake()
    {
        _vessel = GetComponent<CookingVessel>();
        _thermal = GetComponent<PotThermal>();
    }

    private void Update()
    {
        if (!base.IsServerInitialized) return;

        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        float acc = Accuracy(_thermal.Temperature);

        byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(acc) * 255f);
        if (Mathf.Abs(a - _accuracy.Value) >= 4) _accuracy.Value = a;

        bool canCook = _process != null && !_thermal.IsRuptured &&
                       (!_requireContents || !_vessel.IsEmpty);

        if (!canCook)
        {
            if (_cooking.Value) _cooking.Value = false;
            return;
        }

        // Холодная посуда не готовится вовсе: греть надо хотя бы до подхода
        // к коридору, иначе «готовка» шла бы на комнатной температуре.
        if (_thermal.Temperature < _low - _softEdge * 2f)
        {
            if (_cooking.Value) _cooking.Value = false;
            return;
        }

        if (!_cooking.Value) _cooking.Value = true;

        // Вне коридора готовка почти встаёт, но не насмерть: иначе отойти
        // было бы нельзя вообще, а это уже не игра, а надзор.
        float speed = Mathf.Lerp(_outsideSpeed, 1f, acc);
        float delta = dt / Mathf.Max(1f, _secondsToCook) * speed;

        // Качество = точность удержания. Прямее связи не придумать.
        float quality = Mathf.Lerp(_minQuality, 1f, acc);

        _vessel.ServerCook(_process, delta, quality);
    }

    /// <summary>
    /// Насколько точно держим температуру.
    ///
    /// Внутри коридора — единица. За краем падает плавно на протяжении
    /// SoftEdge градусов, а не обрывается: чуть перегрел — чуть потерял,
    /// и это ощущается справедливо.
    /// </summary>
    public float Accuracy(float temp)
    {
        if (temp >= _low && temp <= _high) return 1f;

        float over = temp < _low ? _low - temp : temp - _high;
        return Mathf.Clamp01(1f - over / Mathf.Max(0.1f, _softEdge));
    }

    /// <summary>Задать коридор снаружи — например, из рецепта блюда.</summary>
    [Server]
    public void ServerSetBand(float low, float high, ProcessDef process = null)
    {
        _low = Mathf.Min(low, high);
        _high = Mathf.Max(low, high);
        if (process != null) _process = process;

        if (_vessel != null) _vessel.ServerResetCook();
    }

    private void OnGUI()
    {
        if (!_debugHud) return;

        Camera cam = Camera.main;
        if (cam == null || _thermal == null) return;

        Vector3 world = transform.position + Vector3.up * 0.75f;
        if (Vector3.Distance(cam.transform.position, world) > 8f) return;

        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;

        float acc = Accuracy01;

        GUIStyle st = new GUIStyle(GUI.skin.box) { fontSize = 13 };
        st.normal.textColor = acc > 0.9f ? new Color(0.5f, 1f, 0.6f)
                            : acc > 0.4f ? new Color(1f, 0.9f, 0.4f)
                            : new Color(1f, 0.5f, 0.3f);

        string state = !IsCooking ? "не готовится"
                     : acc > 0.9f ? "в самый раз"
                     : _thermal.Temperature < _low ? "слабовато — прибавь"
                     : "перегрев — убавь";

        string txt = $"{_thermal.Temperature:0}°  ({_low:0}–{_high:0})\n" +
                     $"{state}   готово {Progress01 * 100f:0}%";

        Vector2 size = st.CalcSize(new GUIContent(txt));
        GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y, size.x + 12f, 40f),
                txt, st);
    }
}
