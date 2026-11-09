using UnityEngine;

/// <summary>
/// ПЫЛЬ ТАЧКИ. Из-под колёс, при заносе и при загрузке.
///
/// Вешается на корень тачки. Системы частиц, материал и текстуру создаёт сам —
/// импортировать нечего.
///
/// ═══ ТРИ РАЗНЫХ ПЫЛИ, А НЕ ОДНА С РАЗНОЙ СИЛОЙ ═══
///
/// КАЧЕНИЕ — мелкая, низкая, стелется. Она сообщает, что тачка едет, и должна
/// быть еле заметной: это фон, а не событие.
///
/// ЗАНОС — высокая, густая, срывается вбок. Это уже СОБЫТИЕ: игрок повернул
/// резко, и пыль ему об этом говорит раньше, чем он потеряет равновесие.
/// Поэтому она поднимается вверх, а не стелется — вертикаль видна боковым
/// зрением, горизонталь нет.
///
/// ЗАГРУЗКА — короткий хлопок в корыте, когда в него что-то бросили.
///
/// Разделение не косметическое. Одна система с плавно растущей силой не
/// смогла бы одновременно быть незаметным фоном и внятным предупреждением:
/// либо фон слишком яркий, либо предупреждение не читается.
///
/// ═══ ПОВОРОТ СЧИТАЕТСЯ ПО ТАЧКЕ, А НЕ ПО ВВОДУ ═══
///
/// Угловая скорость берётся из того, как реально поворачивается корпус.
/// Читать Input.GetAxis нельзя: в сетевой игре у чужой тачки этого ввода нет,
/// и пыль от заноса была бы видна только водителю — то есть ровно тому, кто и
/// так знает, что он повернул.
/// </summary>
[AddComponentMenu("CatCooks/Тачка/Cart Dust")]
public class CartDust : MonoBehaviour
{
    [Header("━━━ Связи ━━━")]
    [Tooltip("ХОДОВАЯ ЧАСТЬ — из неё берутся точки касания колёс. Пусто — " +
             "ищется на этом же объекте.\n\n" +
             "Без неё пыль пойдёт от запасных точек, но без учёта того, " +
             "оторвалось колесо от земли или нет: пыль полетит и в прыжке.")]
    [SerializeField] private CartRig _rig;

    [SerializeField] private PushCart _cart;

    [Tooltip("Точки, откуда сыпать, если подвески нет. Обычно колёса.")]
    [SerializeField] private Transform[] _fallbackPoints;

    [Header("━━━ Общее ━━━")]
    [Tooltip("Цвет пыли. Под цвет грунта площадки.")]
    [SerializeField] private Color _color = new Color(0.78f, 0.72f, 0.58f, 0.55f);

    [Tooltip("Общий множитель количества. Для слабого железа 0.5.")]
    [Range(0.1f, 2f)][SerializeField] private float _quality = 1f;

    [Tooltip("Скорость, при которой пыль выходит на полную, м/с")]
    [Min(0.2f)][SerializeField] private float _fullSpeed = 2.6f;

    // ═════════════════════════ КАЧЕНИЕ ═════════════════════════

    [Header("━━━ Пыль качения ━━━")]
    [Tooltip("Частиц в секунду с каждого колеса на полной скорости")]
    [Range(0f, 60f)][SerializeField] private float _rollRate = 14f;

    [Range(0.05f, 1.5f)][SerializeField] private float _rollSize = 0.32f;
    [Range(0.2f, 4f)][SerializeField] private float _rollLife = 1.1f;

    [Tooltip("Подъём вверх. Держи МАЛЫМ: пыль качения должна стелиться, " +
             "иначе она конкурирует с пылью заноса и та перестаёт читаться " +
             "как предупреждение.")]
    [Range(0f, 2f)][SerializeField] private float _rollRise = 0.35f;

    // ═════════════════════════ ЗАНОС ═════════════════════════

    [Header("━━━ Пыль заноса ━━━")]
    [Tooltip("С какой угловой скорости считать это заносом, градусов в " +
             "секунду. Ниже — обычный поворот, пыли не будет.")]
    [Range(10f, 200f)][SerializeField] private float _driftAt = 55f;

    [Range(0f, 120f)][SerializeField] private float _driftRate = 40f;
    [Range(0.1f, 3f)][SerializeField] private float _driftSize = 0.55f;
    [Range(0.3f, 5f)][SerializeField] private float _driftLife = 1.8f;

    [Tooltip("ПОДЪЁМ. Здесь наоборот — большой: вертикаль видна боковым " +
             "зрением, а горизонталь нет. Столб пыли предупреждает о заносе " +
             "раньше, чем игрок почувствует крен.")]
    [Range(0.5f, 6f)][SerializeField] private float _driftRise = 2.4f;

    [Tooltip("Снос вбок, наружу поворота")]
    [Range(0f, 4f)][SerializeField] private float _driftSide = 1.3f;

    // ═════════════════════════ ЗАГРУЗКА ═════════════════════════

    [Header("━━━ Пыль загрузки ━━━")]
    [Tooltip("Хлопок в корыте, когда в него что-то бросили.\n\n" +
             "Ловится по изменению числа предметов в корыте — трогать чужой " +
             "скрипт ради события не нужно.")]
    [SerializeField] private bool _dustOnLoad = true;

    [Range(1, 40)][SerializeField] private int _loadPuff = 12;
    [Range(0.05f, 1.5f)][SerializeField] private float _loadSize = 0.4f;
    [Range(0.2f, 3f)][SerializeField] private float _loadLife = 1.3f;

    [Tooltip("Куда сыпать хлопок. Пусто — центр корыта.")]
    [SerializeField] private Transform _loadPoint;

    // ═════════════════════════ УДАРЫ ═════════════════════════

    [Header("━━━ Пыль от ударов подвески ━━━")]
    [Tooltip("Хлопок под колесом, когда подвеска резко сжалась: спрыгнул с " +
             "бордюра, влетел в яму.")]
    [SerializeField] private bool _dustOnImpact = true;

    [Range(1, 30)][SerializeField] private int _impactPuff = 8;

    [Header("━━━ Отладка ━━━")]
    [SerializeField] private bool _drawGizmos = false;

    // ───────────────────────── состояние ─────────────────────────

    private ParticleSystem _roll, _drift, _load;
    private Material _mat;
    private ParticleSystem.EmitParams _emit;

    private Vector3 _lastPos;
    private Vector3 _lastFwd;
    private float _speed;
    private float _turnRate;
    private float[] _accum;
    private float[] _lastImpact;
    private int _lastCount = -1;

    private static Texture2D _puffTex;

    private void Awake()
    {
        if (_cart == null) _cart = GetComponent<PushCart>();
        if (_rig == null) _rig = GetComponent<CartRig>();
        if (_rig == null) _rig = GetComponentInChildren<CartRig>();

        _lastPos = transform.position;
        _lastFwd = transform.forward;

        int n = PointCount();
        _accum = new float[Mathf.Max(1, n)];
        _lastImpact = new float[Mathf.Max(1, n)];

        Build();
    }

    private void OnDestroy()
    {
        if (_roll != null) Destroy(_roll.gameObject);
        if (_drift != null) Destroy(_drift.gameObject);
        if (_load != null) Destroy(_load.gameObject);
    }

    private int PointCount()
    {
        if (_rig != null) return _rig.WheelCount;
        return _fallbackPoints != null ? _fallbackPoints.Length : 0;
    }

    private bool TryPoint(int i, out Vector3 pos)
    {
        pos = Vector3.zero;

        if (_rig != null)
            return _rig.TryGetContact(i, out pos, out _);

        if (_fallbackPoints == null || i >= _fallbackPoints.Length) return false;
        if (_fallbackPoints[i] == null) return false;

        pos = _fallbackPoints[i].position;
        return true;
    }

    // ───────────────────────── сборка ─────────────────────────

    private void Build()
    {
        EnsureSprite();

        _mat = MakeMaterial();

        _roll = MakeSystem("__DustRoll", _rollSize, _rollLife, false);
        _drift = MakeSystem("__DustDrift", _driftSize, _driftLife, true);
        _load = MakeSystem("__DustLoad", _loadSize, _loadLife, false);
    }

    /// <summary>
    /// Материал берём из URP-шейдера частиц. Если его вдруг нет в сборке —
    /// падаем на Sprites/Default: пыль станет плоской, но останется видимой,
    /// а это лучше, чем розовый материал-ошибка на всю площадку.
    /// </summary>
    private Material MakeMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        Material m = new Material(sh) { name = "CartDust (auto)" };

        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", _puffTex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", _puffTex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);

        // Surface = Transparent, Blend = Alpha. Через свойства, а не ключевые
        // слова: имена ключевых слов менялись между версиями URP, а числовые
        // свойства остались.
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);

        m.renderQueue = 3000;
        return m;
    }

    private ParticleSystem MakeSystem(string name, float size, float life, bool tall)
    {
        Transform t = transform.Find(name);

        if (t == null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            t = go.transform;
        }

        ParticleSystem ps = t.GetComponent<ParticleSystem>();
        if (ps == null) ps = t.gameObject.AddComponent<ParticleSystem>();

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = _color;

        // МИРОВОЕ пространство. В локальном пыль ехала бы за тачкой, то есть
        // висела бы у колёс как приклеенная — а она должна оставаться там,
        // где её подняли.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.CeilToInt(220 * _quality);
        main.cullingMode = ParticleSystemCullingMode.Automatic;
        main.gravityModifier = tall ? -0.02f : 0.01f;

        // Эмиссии по времени нет: всё сыплется через Emit из точек контакта.
        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, tall ? 0.45f : 0.6f), new Keyframe(1f, tall ? 2.2f : 1.5f)));

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;

        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, tall ? 0.2f : 0.15f),
                new GradientAlphaKey(0.75f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });

        col.color = new ParticleSystem.MinMaxGradient(g);

        // Торможение: поднятая пыль почти сразу теряет скорость и дальше
        // просто висит. Без этого она улетает по прямой, как выхлоп.
        ParticleSystem.LimitVelocityOverLifetimeModule lim = ps.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.dampen = tall ? 0.22f : 0.4f;
        lim.limit = new ParticleSystem.MinMaxCurve(tall ? 4f : 2f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.separateAxes = false;
        noise.strength = new ParticleSystem.MinMaxCurve(tall ? 0.5f : 0.22f);
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.4f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        noise.damping = true;

        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();

        if (r != null)
        {
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = _mat;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.maxParticleSize = 3f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        ps.Play();
        return ps;
    }

    // ───────────────────────── кадр ─────────────────────────

    private void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        Measure(dt);
        TickRoll(dt);
        TickDrift(dt);
        TickImpacts();
        TickLoad();
    }

    private void Measure(float dt)
    {
        Vector3 d = transform.position - _lastPos;
        _lastPos = transform.position;
        d.y = 0f;

        _speed = d.magnitude / Mathf.Max(0.0001f, dt);

        // Угловая скорость ПО КОРПУСУ, а не по вводу: у чужой тачки ввода нет,
        // и пыль заноса была бы видна только водителю.
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();

        float ang = Vector3.SignedAngle(_lastFwd, fwd, Vector3.up);
        _lastFwd = fwd;

        _turnRate = Mathf.Lerp(_turnRate, ang / Mathf.Max(0.0001f, dt), 0.35f);
    }

    private void TickRoll(float dt)
    {
        if (_roll == null || _cart == null || !_cart.IsGripped) return;

        float k = Mathf.Clamp01(_speed / _fullSpeed);
        if (k < 0.05f) return;

        int n = PointCount();

        for (int i = 0; i < n && i < _accum.Length; i++)
        {
            if (!TryPoint(i, out Vector3 p)) continue;

            _accum[i] += _rollRate * k * _quality * dt;

            int count = Mathf.FloorToInt(_accum[i]);
            if (count <= 0) continue;

            _accum[i] -= count;

            Vector3 back = -transform.forward;

            for (int c = 0; c < count && c < 8; c++)
            {
                _emit = new ParticleSystem.EmitParams
                {
                    position = p + Random.insideUnitSphere * 0.08f + Vector3.up * 0.03f,
                    applyShapeToPosition = false,

                    // Назад и чуть вверх: пыль отбрасывается против хода, как
                    // и должна — вперёд её выкидывало бы только при
                    // торможении юзом.
                    velocity = back * (_speed * 0.35f)
                             + Vector3.up * (_rollRise * k)
                             + Random.insideUnitSphere * 0.25f
                };

                _roll.Emit(_emit, 1);
            }
        }
    }

    private void TickDrift(float dt)
    {
        if (_drift == null || _cart == null || !_cart.IsGripped) return;

        float turn = Mathf.Abs(_turnRate);
        if (turn < _driftAt) return;

        // Занос требует И поворота, И скорости: развернуться на месте — не
        // занос, там колёса не срывает.
        float k = Mathf.Clamp01((turn - _driftAt) / _driftAt)
                * Mathf.Clamp01(_speed / _fullSpeed);

        if (k < 0.05f) return;

        int n = PointCount();
        float side = Mathf.Sign(_turnRate);

        for (int i = 0; i < n; i++)
        {
            if (!TryPoint(i, out Vector3 p)) continue;

            // Сыпем с ВНЕШНЕГО колеса: внутреннее в заносе разгружено, и пыль
            // из-под него выглядела бы одинаково при любом повороте.
            Vector3 local = transform.InverseTransformPoint(p);
            if (Mathf.Sign(local.x) == side) continue;

            int count = Mathf.CeilToInt(_driftRate * k * _quality * dt);

            for (int c = 0; c < count && c < 10; c++)
            {
                _emit = new ParticleSystem.EmitParams
                {
                    position = p + Random.insideUnitSphere * 0.12f,
                    applyShapeToPosition = false,

                    velocity = Vector3.up * (_driftRise * k * (0.6f + Random.value * 0.8f))
                             + transform.right * (-side * _driftSide * k)
                             + -transform.forward * (_speed * 0.2f)
                             + Random.insideUnitSphere * 0.4f
                };

                _drift.Emit(_emit, 1);
            }
        }
    }

    private void TickImpacts()
    {
        if (!_dustOnImpact || _rig == null || _load == null) return;

        for (int i = 0; i < _rig.WheelCount && i < _lastImpact.Length; i++)
        {
            CartRig.Wheel a = _rig.Wheels[i];
            if (a == null || a.mesh == null) continue;

            // Реагируем на ФРОНТ удара, а не на его величину: иначе один
            // наезд на бордюр сыпал бы пыль всё время, пока подвеска сжата.
            bool rising = a.impact > 0.05f && _lastImpact[i] <= 0.05f;
            _lastImpact[i] = a.impact;

            if (!rising || !a.grounded) continue;

            Puff(a.contact, Mathf.CeilToInt(_impactPuff * Mathf.Clamp01(a.impact) * _quality),
                 0.9f);
        }
    }

    private void TickLoad()
    {
        if (!_dustOnLoad || _cart == null || _cart.Trough == null || _load == null) return;

        int c = _cart.Trough.Count;

        if (_lastCount < 0) { _lastCount = c; return; }

        if (c > _lastCount)
        {
            Vector3 p = _loadPoint != null
                ? _loadPoint.position
                : _cart.Trough.transform.position + Vector3.up * 0.2f;

            Puff(p, Mathf.CeilToInt(_loadPuff * _quality), 0.7f);
        }

        _lastCount = c;
    }

    private void Puff(Vector3 pos, int count, float spread)
    {
        if (_load == null || count <= 0) return;

        count = Mathf.Min(count, 30);

        for (int i = 0; i < count; i++)
        {
            _emit = new ParticleSystem.EmitParams
            {
                position = pos + Random.insideUnitSphere * spread * 0.35f,
                applyShapeToPosition = false,
                velocity = Random.insideUnitSphere * spread
                         + Vector3.up * (0.4f + Random.value * 0.5f)
            };

            _load.Emit(_emit, 1);
        }
    }

    // ───────────────────── управление из игры ─────────────────────

    /// <summary>Хлопок пыли в точке. Для чего угодно: удар, падение, бросок.</summary>
    public void PuffAt(Vector3 worldPos, int count = 10, float spread = 0.7f)
        => Puff(worldPos, count, spread);

    // ═════════════════════ ТЕКСТУРА КЛУБА ═════════════════════
    //
    //  Рисуется кодом: пока идёт настройка, форма меняется часто, и гонять её
    //  через импорт-ассет означает править в двух местах и однажды забыть.

    private static void EnsureSprite()
    {
        if (_puffTex != null) return;

        const int N = 128;

        _puffTex = new Texture2D(N, N, TextureFormat.RGBA32, true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
            name = "CartDustPuff"
        };

        Color[] px = new Color[N * N];
        float seed = 17.3f;

        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N - 0.5f;
                float dy = (y + 0.5f) / N - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;

                // Мягкий круг, разъеденный шумом СО СРЕДНИМ ОКОЛО ЕДИНИЦЫ.
                // Простое умножение на шум съело бы половину плотности, и
                // клуб получился бы вдвое жиже задуманного.
                float n = Mathf.PerlinNoise(x * 0.06f + seed, y * 0.06f + seed);
                float a = Mathf.Clamp01(1f - r) * Mathf.Clamp01(0.55f + n * 0.9f);

                a *= a;   // мягче к краю

                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        }

        _puffTex.SetPixels(px);
        _puffTex.Apply(true, false);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos) return;

        Gizmos.color = new Color(1f, 0.9f, 0.5f, 0.8f);

        int n = PointCount();

        for (int i = 0; i < n; i++)
            if (TryPoint(i, out Vector3 p)) Gizmos.DrawWireSphere(p, 0.12f);
    }
}
