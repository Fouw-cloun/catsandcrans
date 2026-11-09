using UnityEngine;

/// <summary>
/// ВИЗУАЛ ПРОДУКТА. Отвечает за материал и за вспышку при смене состояния.
///
/// ПОЧЕМУ ВСПЫШКА, А НЕ ПЛАВНОЕ СМЕШИВАНИЕ ТЕКСТУР:
///   Material.Lerp не умеет смешивать текстуры — числа и цвета он
///   интерполирует, а ссылку на текстуру просто переключает. Отсюда щелчок.
///   Смешивать две текстуры можно только в шейдере, а это лишняя возня.
///   Вспышка прячет подмену не хуже и не требует правок шейдера вообще.
///
/// КАК РАБОТАЕТ:
///   по мере обработки продукт чуть теплеет (выбеливается), в момент смены
///   состояния вспыхивает и плавно возвращается к норме уже с новой текстурой.
///
/// Крутятся штатные свойства шейдера CatCooks/Toon Food: _Saturation и
/// _Brightness. Порядок в шейдере — сперва обесцвечивание, потом яркость,
/// поэтому «обесцветить и поджечь» и даёт выбеливание.
///
/// Если у материала таких свойств нет, вспышка просто не играет, а смена
/// состояния остаётся мгновенной. Ошибок не будет.
///
/// Вешается на тот же объект, что и FoodItem.
/// </summary>
[RequireComponent(typeof(FoodItem))]
public class FoodVisual : MonoBehaviour
{
    [Tooltip("ПУСТО = собрать все MeshRenderer из детей. У нарезанного продукта " +
             "их несколько (каждый кусок свой), и материал состояния надо " +
             "красить на ВСЕХ — иначе при мойке отмоется только первый кусок.")]
    [SerializeField] private MeshRenderer[] _renderers;

    [Header("Осветление при обработке")]
    [Tooltip("Во сколько раз ярче на пике. МНОЖИТЕЛЬ к яркости материала, " +
             "а не абсолютное значение — поэтому продукт светлеет, а не " +
             "выгорает в белый. 1.3 это заметно, но мягко.")]
    [Range(1f, 3f)][SerializeField] private float _brightnessBoost = 1.3f;

    [Tooltip("Сколько насыщенности теряется на пике. 0 = цвет не блёкнет " +
             "совсем, 1 = полностью серое. Держи низким, иначе будет белое пятно.")]
    [Range(0f, 1f)][SerializeField] private float _desaturate = 0.2f;

    [Tooltip("Насколько продукт светлеет ПО ХОДУ обработки")]
    [Range(0f, 1f)][SerializeField] private float _progressGlow = 0.7f;

    [Tooltip("Кривая осветления. 1 = равномерно с самого начала, " +
             "3 = только под конец.")]
    [Range(1f, 3f)][SerializeField] private float _glowCurve = 1.4f;

    [Tooltip("Доп. вспышка в момент подмены текстуры — она прячет щелчок. " +
             "0 = без вспышки, подмена будет заметна.")]
    [Range(0f, 1f)][SerializeField] private float _swapFlash = 0.45f;

    [Tooltip("Скорость угасания вспышки. 2.5 = примерно полсекунды.")]
    [SerializeField] private float _flashFade = 2.5f;

    private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
    private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");

    private FoodItem _item;

    private Material _basis;        // общий материал текущего состояния
    private Material _runtime;      // инстанс, на котором крутится вспышка
    private Material _runtimeOf;    // из какого общего сделан инстанс

    private float _baseSaturation = 1f;
    private float _baseBrightness = 1f;
    private bool _canFlash;

    private float _flash;           // 0..1, текущее выбеливание
    private bool _first = true;

    private void Awake()
    {
        _item = GetComponent<FoodItem>();

        // Включая выключенные: отрезанные куски спрятаны через SetActive(false),
        // но материал им всё равно нужен верный — вдруг продукт «соберут» обратно
        // или кусок понадобится показать.
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<MeshRenderer>(true);
    }

    private void OnEnable() => _item.StateChanged += OnStateChanged;
    private void OnDisable() => _item.StateChanged -= OnStateChanged;

    private void Start()
    {
        // Догоняем состояние БЕЗ вспышки: при позднем подключении игрока
        // продукт уже может быть почищен, и вспышка на пустом месте выглядела
        // бы как баг.
        OnStateChanged(_item.StateIndex, 0f);
        _first = false;
        Apply();
    }

    private void OnDestroy()
    {
        // Инстанс материала сам не убирается — это утечка на каждый продукт.
        if (_runtime != null) Destroy(_runtime);
    }

    // ───────────────────────── СОСТОЯНИЕ ─────────────────────────

    /// <summary>blendSeconds из графа больше не используется: длительность
    /// задаёт _flashFade на этом компоненте.</summary>
    private void OnStateChanged(byte state, float blendSeconds)
    {
        if (_item.Form == null) return;

        _basis = _item.Form.GetMaterial(state);

        // Подмена текстуры происходит на пике вспышки, поэтому её почти не видно.
        if (!_first) _flash = Mathf.Max(_flash, _swapFlash);
    }

    // ───────────────────────── ВСПЫШКА ─────────────────────────

    private void Update()
    {
        // Кривая осветления. При _glowCurve = 1 продукт светлеет равномерно
        // с самого начала, при 3 — почти только под конец. Полностью убирать
        // кривизну не стоит: брошенная на середине картошка так и останется
        // подсвеченной ровно на столько, сколько её успели обработать.
        float t = Mathf.Pow(_item.Progress01, _glowCurve);
        float target = t * _progressGlow;

        _flash = _flash > target
            ? Mathf.MoveTowards(_flash, target, _flashFade * Time.deltaTime)
            : target;

        Apply();
    }

    private void Apply()
    {
        if (_renderers == null || _renderers.Length == 0 || _basis == null) return;

        EnsureRuntime();

        // Вспышки нет — возвращаемся на общий материал, чтобы не держать
        // инстанс в рендерерах и не терять батчинг зря.
        if (!_canFlash || _flash <= 0.002f)
        {
            SetAll(_basis);
            return;
        }

        // МНОЖИТЕЛИ к тому, что стоит в материале. Абсолютные значения
        // гарантированно выжигали бы продукт в белое пятно независимо от того,
        // какая у него текстура.
        _runtime.SetFloat(SaturationId, _baseSaturation * Mathf.Lerp(1f, 1f - _desaturate, _flash));
        _runtime.SetFloat(BrightnessId, _baseBrightness * Mathf.Lerp(1f, _brightnessBoost, _flash));

        SetAll(_runtime);
    }

    /// <summary>
    /// Один материал на ВСЕ куски продукта. У нарезанной картошки рендереров
    /// несколько, и без этого при мойке отмывался бы только первый кусок.
    /// </summary>
    private void SetAll(Material m)
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            MeshRenderer r = _renderers[i];
            if (r != null && r.sharedMaterial != m) r.sharedMaterial = m;
        }
    }

    /// <summary>Инстанс делаем лениво и пересоздаём, только если сменился
    /// материал состояния.</summary>
    private void EnsureRuntime()
    {
        if (_runtimeOf == _basis && _runtime != null) return;

        _canFlash = _basis.HasProperty(SaturationId) && _basis.HasProperty(BrightnessId);
        if (!_canFlash) { _runtimeOf = _basis; return; }

        _baseSaturation = _basis.GetFloat(SaturationId);
        _baseBrightness = _basis.GetFloat(BrightnessId);

        if (_runtime != null) Destroy(_runtime);
        _runtime = new Material(_basis);
        _runtimeOf = _basis;
    }
}
