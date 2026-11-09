using UnityEngine;

/// <summary>
/// ВИЗУАЛ КОСТРА. Переводит состояние Firebox в параметры шейдеров огня и дыма.
///
/// ═══ ПОЧЕМУ ОТДЕЛЬНЫЙ СКРИПТ ═══
///
/// Firebox считает горение: килограммы, тягу, градусы. Он не должен знать ни
/// про меши, ни про материалы, ни про то, что костёр вообще видно. Смешай их —
/// и любая правка внешнего вида станет правкой симуляции.
///
/// ═══ MaterialPropertyBlock, А НЕ material ═══
///
/// ЭТО ГЛАВНОЕ ДЛЯ ПРОИЗВОДИТЕЛЬНОСТИ. Обращение к renderer.material создаёт
/// КОПИЮ материала на каждый объект. Десять костров — десять материалов, десять
/// разных состояний шейдера, ни одного объединённого вызова отрисовки. Плюс
/// копии текут в памяти и не выгружаются до конца сцены.
///
/// MaterialPropertyBlock подменяет значения без копирования материала. Материал
/// остаётся ОДИН на все костры, GPU Instancing собирает их в один DrawCall, а
/// параметры у каждого свои. Десять костров стоят почти столько же, сколько
/// один.
///
/// ═══ ФАЗА ═══
///
/// Каждому костру выдаётся случайный сдвиг времени. Без него все костры на
/// площадке пульсируют синхронно и вся сцена дышит как один организм — глаз
/// это замечает мгновенно, даже не понимая, что именно не так.
///
/// ═══ LOD ═══
///
/// Дальше _SimpleDistance шейдер переключается в FIRE_SIMPLE: вторая октава
/// шума и языки отключаются, фрагмент дешевеет вдвое. Дальше _CullDistance
/// рендереры выключаются целиком. Костёр за сто метров занимает тридцать
/// пикселей, и разницы там не видит никто, а платить за неё приходится.
///
/// Расстояние проверяется НЕ каждый кадр: камера за десятую долю секунды не
/// перепрыгнет через порог, а Camera.main — дорогой поиск по сцене.
/// </summary>
[RequireComponent(typeof(Firebox))]
public class FireVisualController : MonoBehaviour
{
    [Header("Рендереры")]
    [Tooltip("Меш пламени: цилиндр без крышек, пивот в основании, высота 1")]
    [SerializeField] private Renderer _fireRenderer;

    [Tooltip("Меш дыма: конус без крышек, пивот в основании, высота 1")]
    [SerializeField] private Renderer _smokeRenderer;

    [Tooltip("Корень пламени — масштабируется по высоте горения")]
    [SerializeField] private Transform _fireRoot;

    [Header("Свет")]
    [Tooltip("Point Light в центре кострища. Тени у него должны быть None: " +
             "динамический источник посреди сцены с кучей геометрии вокруг " +
             "стоит дороже всего остального костра вместе взятого.")]
    [SerializeField] private Light _fireLight;

    [SerializeField] private float _lightMaxIntensity = 6f;
    [SerializeField] private float _lightMaxRange = 12f;
    [SerializeField] private Color _lightFlame = new Color(1f, 0.62f, 0.25f);
    [SerializeField] private Color _lightEmber = new Color(1f, 0.34f, 0.10f);

    [Tooltip("Глубина мерцания. 0 — ровная лампочка, 0.25 — живой костёр.")]
    [Range(0f, 0.6f)][SerializeField] private float _flicker = 0.22f;

    [Header("Размеры")]
    [Tooltip("Радиус кострища, метры. Ширина пламени.")]
    [SerializeField] private float _fireRadius = 1.4f;

    [Tooltip("Высота пламени при МАКСИМАЛЬНОЙ мощности, метры")]
    [SerializeField] private float _maxFlameHeight = 2.6f;

    [Tooltip("Высота пламени на голых углях, метры. Не ноль: угли должны " +
             "теплиться, а не исчезать.")]
    [SerializeField] private float _emberFlameHeight = 0.35f;

    [Tooltip("Высота дымового столба, метры")]
    [SerializeField] private float _smokeHeight = 7f;

    [Header("Обтекание котла")]
    [Tooltip("Подставка. Пусто — ищется в родителях. Нужна, чтобы шейдер знал, " +
             "где дно котла и какого он радиуса.")]
    [SerializeField] private FireCauldronStand _stand;

    [Tooltip("Запас к радиусу котла, метры. Пламя должно выплёскиваться ЗА " +
             "край, а не упираться в него ровно.")]
    [SerializeField] private float _obstaclePadding = 0.25f;

    [Header("Плавность")]
    [Tooltip("За сколько секунд визуал догоняет мощность. Мгновенная реакция " +
             "выглядит переключателем: раздул мехами — и пламя МОРГНУЛО вверх. " +
             "0.4 читается как «занялось».")]
    [Min(0.01f)][SerializeField] private float _smoothSeconds = 0.4f;

    [Header("Оптимизация")]
    [Tooltip("Дальше этого расстояния — упрощённый шейдер, метры")]
    [SerializeField] private float _simpleDistance = 35f;

    [Tooltip("Дальше этого — не рисуем вовсе, метры")]
    [SerializeField] private float _cullDistance = 120f;

    [Tooltip("Как часто проверять расстояние, сек. Каждый кадр незачем.")]
    [SerializeField] private float _lodInterval = 0.25f;

    // Идентификаторы свойств кэшируем: Shader.PropertyToID делает поиск по
    // строке, и в Update это заметная трата на пустом месте.
    private static readonly int IdHeight = Shader.PropertyToID("_InstHeight");
    private static readonly int IdPhase = Shader.PropertyToID("_InstPhase");
    private static readonly int IdObstacleY = Shader.PropertyToID("_InstObstacleY");
    private static readonly int IdObstacleR = Shader.PropertyToID("_InstObstacleRadius");
    private static readonly int IdTint = Shader.PropertyToID("_InstTint");
    private static readonly int IdDensity = Shader.PropertyToID("_InstDensity");

    private static readonly Color FlameTint = new Color(1f, 1f, 1f, 1f);
    private static readonly Color EmberTint = new Color(1f, 0.55f, 0.35f, 1f);

    private Firebox _fire;
    private MaterialPropertyBlock _fireBlock, _smokeBlock;
    private Material _fireMaterialRef;

    private float _shown;        // сглаженная мощность
    private float _phase;
    private float _nextLod;
    private bool _simpleNow, _visibleNow = true;
    private Transform _cam;

    private void Awake()
    {
        _fire = GetComponent<Firebox>();
        if (_stand == null) _stand = GetComponentInParent<FireCauldronStand>();

        _fireBlock = new MaterialPropertyBlock();
        _smokeBlock = new MaterialPropertyBlock();

        // Сдвиг фазы — свой у каждого костра, иначе площадка дышит синхронно.
        _phase = Random.Range(0f, 100f);

        if (_fireRenderer != null)
        {
            // sharedMaterial, НЕ material: обращение к material создало бы копию
            // и сломало объединение вызовов, ради которого всё и затевалось.
            _fireMaterialRef = _fireRenderer.sharedMaterial;
            _fireRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fireRenderer.receiveShadows = false;
            _fireRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        if (_smokeRenderer != null)
        {
            _smokeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _smokeRenderer.receiveShadows = false;
            _smokeRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }
    }

    private void LateUpdate()
    {
        UpdateLod();
        if (!_visibleNow) return;

        // ── мощность ──
        float target = _fire.Intensity01;
        _shown = Mathf.MoveTowards(_shown, target, Time.deltaTime / _smoothSeconds);

        bool lit = _fire.State == FireState.Burning
                || _fire.State == FireState.Embers
                || _fire.State == FireState.Catching;

        bool embers = _fire.State == FireState.Embers;

        ApplyFire(lit, embers);
        ApplySmoke(lit);
        ApplyLight(lit, embers);
    }

    /// <summary>
    /// Свет мерцает В ТОЙ ЖЕ ФАЗЕ, что и шейдер: и туда, и туда идёт один
    /// _phase. Раньше свет жил по своему шуму, и при внимательном взгляде было
    /// видно, что тени дёргаются не тогда, когда взмывает пламя. Заметить это
    /// почти невозможно, но починка стоит одной переменной.
    /// </summary>
    private void ApplyLight(bool lit, bool embers)
    {
        if (_fireLight == null) return;

        _fireLight.enabled = lit;
        if (!lit) return;

        float flick = 1f + (Mathf.PerlinNoise((Time.time + _phase) * 6f, 0f) - 0.5f) * 2f * _flicker;

        _fireLight.intensity = _lightMaxIntensity * _shown * flick;
        _fireLight.range = _lightMaxRange * Mathf.Lerp(0.4f, 1f, _shown);
        _fireLight.color = embers ? _lightEmber : Color.Lerp(_lightEmber, _lightFlame, _shown);
    }

    private void ApplyFire(bool lit, bool embers)
    {
        if (_fireRenderer == null) return;

        _fireRenderer.enabled = lit;
        if (!lit) return;

        // Физическая высота — масштабом объекта. Логическая (докуда пламя
        // доживает внутри оболочки) — параметром шейдера. Двумя ручками
        // получается и рост столба, и его прореживание: слабый костёр не просто
        // ниже, он ещё и рванее.
        float metres = Mathf.Lerp(_emberFlameHeight, _maxFlameHeight, _shown);
        if (embers) metres = _emberFlameHeight;

        if (_fireRoot != null)
        {
            // Ширина тоже растёт с мощностью, но ЗАМЕТНО СЛАБЕЕ высоты.
            // Раздутый костёр расширяется — но если тянуть ширину наравне с
            // высотой, пламя раздувается шаром и перестаёт быть языками.
            float wide = _fireRadius * Mathf.Lerp(0.75f, 1f, _shown);
            _fireRoot.localScale = new Vector3(wide, metres, wide);
        }

        _fireRenderer.GetPropertyBlock(_fireBlock);

        _fireBlock.SetFloat(IdHeight, Mathf.Lerp(0.25f, 1f, _shown));
        _fireBlock.SetFloat(IdPhase, _phase);
        _fireBlock.SetColor(IdTint, embers ? EmberTint : FlameTint);

        // ── котёл ──
        Vector3 obstacle = ObstacleInfo(out float radius);
        _fireBlock.SetFloat(IdObstacleY, obstacle.y);
        _fireBlock.SetFloat(IdObstacleR, radius);

        _fireRenderer.SetPropertyBlock(_fireBlock);
    }

    private void ApplySmoke(bool lit)
    {
        if (_smokeRenderer == null) return;

        _smokeRenderer.enabled = lit;
        if (!lit) return;

        _smokeRenderer.transform.localScale =
            new Vector3(_fireRadius * 1.3f, _smokeHeight, _fireRadius * 1.3f);

        _smokeRenderer.GetPropertyBlock(_smokeBlock);

        // ОБРАТНО мощности: хорошо горящий костёр дымит мало, тлеющий валит.
        // Заодно это бесплатная индикация — площадку затягивает ровно тогда,
        // когда за костром пора сходить.
        _smokeBlock.SetFloat(IdDensity, Mathf.Lerp(0.85f, 0.18f, _shown));
        _smokeBlock.SetFloat(IdPhase, _phase);

        _smokeRenderer.SetPropertyBlock(_smokeBlock);
    }

    /// <summary>Где дно котла и какого он радиуса. Радиус ноль — препятствия
    /// нет, шейдер обтекание пропустит.</summary>
    private Vector3 ObstacleInfo(out float radius)
    {
        radius = 0f;
        if (_stand == null || _stand.Vessel == null) return new Vector3(0f, 9999f, 0f);

        Collider col = _stand.Vessel.GetComponentInChildren<Collider>();
        if (col == null) return new Vector3(0f, 9999f, 0f);

        Bounds b = col.bounds;
        // Берём БОЛЬШУЮ из полуосей: котёл редко идеально круглый, и по
        // меньшей пламя пробивало бы его с боков.
        radius = Mathf.Max(b.extents.x, b.extents.z) + _obstaclePadding;

        return new Vector3(b.center.x, b.min.y, b.center.z);
    }

    // ───────────────────────── LOD ─────────────────────────

    private void UpdateLod()
    {
        if (Time.time < _nextLod) return;
        _nextLod = Time.time + _lodInterval;

        // Camera.main — поиск по тегу через всю сцену. Кэшируем и обновляем
        // только если камера пропала (смена сцены, выход из техники).
        if (_cam == null)
        {
            Camera c = Camera.main;
            if (c == null) return;
            _cam = c.transform;
        }

        float d = Vector3.Distance(_cam.position, transform.position);

        bool visible = d < _cullDistance;
        if (visible != _visibleNow)
        {
            _visibleNow = visible;
            if (_fireRenderer != null) _fireRenderer.enabled = visible;
            if (_smokeRenderer != null) _smokeRenderer.enabled = visible;
        }

        bool simple = d > _simpleDistance;
        if (simple != _simpleNow && _fireMaterialRef != null)
        {
            _simpleNow = simple;

            // Ключевое слово на ОБЩЕМ материале: значит все костры дальше
            // порога переключаются вместе. Это осознанный компромисс — держать
            // два материала ради посегментного LOD означало бы два DrawCall,
            // то есть ровно ту цену, которую мы экономим.
            if (simple) _fireMaterialRef.EnableKeyword("FIRE_SIMPLE");
            else _fireMaterialRef.DisableKeyword("FIRE_SIMPLE");
        }
    }
}
