using UnityEngine;
using CatCooks.Food;

/// <summary>
/// v19.3: ТАЧКА-ТАРА. Связывает тачку (PushCart, CartRig, CartBalance) с тарой в корыте
/// (Cookware + CookwareLiquidVisual + CookwareSpill), чтобы жижа реагировала на всё, что делает тачка.
///
/// Что уже работает само (компоненты тары это умеют):
///   • разгон, торможение, повороты — жижа плещется против рывка (тара сама меряет своё ускорение);
///   • подвеска качает кузов — жижа «подпрыгивает»;
///   • завалил тачку вперёд (ПКМ) или перевернул — льётся через край струёй, на полу лужа (CookwareSpill).
///
/// Что добавляет этот компонент:
///   • КОЧКИ: удар колеса (CartRig.Impact) — волна от переднего края и подскок поверхности;
///   • КРЕН (мини-игра баланса): тачку валит набок — волна бежит к низкому борту; на опасном крене
///     полная тачка ПЛЕСКАЕТ ЧЕРЕЗ БОРТ (порции уходят лужей рядом);
///   • сильная кочка с полной тачкой — тоже выплёскивается порция;
///   • ВЕС: жижа утяжеляет тачку для баланса (CartTrough.MassKg), если у баланса включена «реальная масса».
///
/// Вешается на корень тачки (меню CatCooks → Тачка → Сделать тарой — повесит само).
/// </summary>
[DisallowMultipleComponent]
public class CartCookwareLink : MonoBehaviour
{
    [Header("Ссылки (пусто — найдутся сами в тачке)")]
    [SerializeField] private Cookware _cookware;
    [SerializeField] private CookwareLiquidVisual _liquid;

    [Header("Кочки")]
    [Tooltip("С какой силы удара колеса (CartRig.Impact) жижа реагирует")]
    [Range(0f, 1f)][SerializeField] private float _bumpMin = 0.08f;
    [Tooltip("Сила волны от кочки")]
    [Range(0f, 3f)][SerializeField] private float _bumpWave = 1f;

    [Header("Крен")]
    [Tooltip("Сила волны от крена (баланс): бежит к низкому борту")]
    [Range(0f, 3f)][SerializeField] private float _leanWave = 1f;
    [Tooltip("С какой опасности крена (0..1, как шкала баланса) полная тачка плещет через борт")]
    [Range(0.3f, 1f)][SerializeField] private float _spillDanger = 0.85f;
    [Tooltip("С какой заполненности (доля вместимости) плещет через борт на крене и кочках")]
    [Range(0.3f, 1f)][SerializeField] private float _spillFill = 0.8f;
    [Tooltip("Не чаще раза в столько секунд уходит порция через борт")]
    [Range(0.1f, 3f)][SerializeField] private float _spillEvery = 0.45f;

    [Header("v19.5: устойчивость тачки")]
    [Tooltip("Отпущенная тачка не скользит «как по льду», не дрожит и не катается сама: трение, гашение, засыпание, " +
             "мягкое выталкивание (без подскоков). Во время езды (тачку катят) не вмешивается")]
    [SerializeField] private bool _stabilize = true;
    [Tooltip("Трение колёс и корпуса о пол (0.6 — обычно, 1.2 — цепко)")]
    [Range(0.2f, 2f)][SerializeField] private float _friction = 1.1f;
    [Tooltip("Максимальная скорость выталкивания из пола/предметов, м/с. Меньше — без подскоков")]
    [Range(0.2f, 5f)][SerializeField] private float _maxDepenetration = 1f;
    [Tooltip("Жижа берёт движение корпуса тачки, а не дрожь подвески — поверхность не скачет, дно не открывается")]
    [SerializeField] private bool _smoothLiquid = true;

    [Header("Вес")]
    [Tooltip("Добавлять вес жижи к грузу для баланса. Выключено — поведение тачки как без тары")]
    [SerializeField] private bool _addWeight = false;
    [Tooltip("Кг на порцию жижи (ведро ~ 20 порций ≈ 12 кг)")]
    [Range(0f, 5f)][SerializeField] private float _kgPerPortion = 0.6f;

    [Header("Отладка")]
    [SerializeField] private bool _log = true;

    private CartRig _rig;
    private CartBalance _balance;
    private PushCart _cart;
    private float _bumpCd, _spillCd, _prevLean, _waveCd, _prevImpact;

    /// <summary>Вес содержимого тары, кг (для баланса).</summary>
    public float CargoKg => _addWeight && _cookware != null ? _cookware.TotalCount * _kgPerPortion : 0f;

    private Rigidbody _rb;
    private Vector3 _liqLocal, _smoothPos;
    private bool _smoothInit;
    private float _idleT;
    private static PhysicsMaterial _grip;

    private void Awake()
    {
        if (_cookware == null) _cookware = GetComponentInChildren<Cookware>(true);
        if (_liquid == null) _liquid = GetComponentInChildren<CookwareLiquidVisual>(true);
        _rig = GetComponent<CartRig>();
        _balance = GetComponent<CartBalance>();
        _cart = GetComponent<PushCart>();
        CartTrough.ExtraMassKg = TroughExtraMass;
        _rb = GetComponent<Rigidbody>();
        if (_stabilize) SetupPhysics();
        if (_smoothLiquid && _liquid != null)
        {
            // точка жижи в осях КОРНЯ тачки на старте — дальше подвеска (кузов) её не трясёт
            _liqLocal = transform.InverseTransformPoint(_liquid.transform.position);
            _liquid.MotionSource = SmoothLiquidPos;
            _liquid.TuneForVehicle();
        }
        if (_log)
            Debug.Log($"[Тачка-тара] «{name}»: тара {(_cookware != null ? "есть" : "НЕТ — меню CatCooks → Тачка → Сделать тарой")}, " +
                      $"жидкость {(_liquid != null ? "есть" : "нет")}, ходовая {(_rig != null ? "есть" : "нет")}, баланс {(_balance != null ? "есть" : "нет")}", this);
    }

    private void SetupPhysics()
    {
        if (_grip == null)
            _grip = new PhysicsMaterial("CartGrip (auto)")
            {
                dynamicFriction = _friction, staticFriction = _friction * 1.15f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Minimum
            };
        int n = 0;
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (c == null || c.isTrigger) continue;
            c.sharedMaterial = _grip;
            n++;
        }
        if (_rb != null)
        {
            _rb.maxDepenetrationVelocity = _maxDepenetration;
            _rb.solverIterations = Mathf.Max(_rb.solverIterations, 12);
            _rb.solverVelocityIterations = Mathf.Max(_rb.solverVelocityIterations, 4);
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (_rb.collisionDetectionMode == CollisionDetectionMode.Discrete) _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rb.sleepThreshold = Mathf.Max(_rb.sleepThreshold, 0.02f);
        }
        if (_log) Debug.Log($"[Тачка-тара] «{name}»: устойчивость — трение {_friction:0.0} на {n} коллайдерах, выталкивание ≤ {_maxDepenetration:0.0} м/с", this);
    }

    private Vector3 SmoothLiquidPos()
    {
        Vector3 want = transform.TransformPoint(_liqLocal);
        if (!_smoothInit) { _smoothPos = want; _smoothInit = true; return want; }
        float k = 1f - Mathf.Exp(-Time.deltaTime / 0.06f);
        _smoothPos = Vector3.Lerp(_smoothPos, want, k);
        return _smoothPos;
    }

    private void FixedUpdate()
    {
        if (!_stabilize || _rb == null || _rb.isKinematic) { _idleT = 0f; return; }
        bool tipped = _cart != null && _cart.IsTipped;
        // отпущенная тачка: гасим ползание и дрожь, на месте — усыпляем
        float v = _rb.linearVelocity.magnitude, w = _rb.angularVelocity.magnitude;
        if (!tipped)
        {
            _rb.linearVelocity *= 1f / (1f + Time.fixedDeltaTime * 1.5f);
            _rb.angularVelocity *= 1f / (1f + Time.fixedDeltaTime * 3f);
        }
        if (v < 0.08f && w < 0.15f) _idleT += Time.fixedDeltaTime; else _idleT = 0f;
        if (_idleT > 0.4f && !_rb.IsSleeping()) _rb.Sleep();
    }

    private static float TroughExtraMass(CartTrough t)
    {
        if (t == null) return 0f;
        CartCookwareLink l = t.GetComponentInParent<CartCookwareLink>();
        if (l == null) { PushCart pc = t.GetComponentInParent<PushCart>(); if (pc != null) l = pc.GetComponent<CartCookwareLink>(); }
        return l != null ? l.CargoKg : 0f;
    }

    private void Update()
    {
        if (_cookware == null) return;
        float dt = Time.deltaTime;
        _bumpCd -= dt; _spillCd -= dt; _waveCd -= dt;
        Vector3 mouth = _cookware.MouthPoint;
        Vector3 fwd = _rig != null ? _rig.Forward : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float half = 0.35f;
        Collider iv = _cookware.InsideVolume;
        if (iv != null) half = Mathf.Max(0.1f, Mathf.Min(iv.bounds.extents.x, iv.bounds.extents.z));

        // ── кочки (вид у всех) ──
        float impact = _rig != null ? _rig.Impact : 0f;
        bool rising = impact > _prevImpact + 0.03f;           // новый удар, а не затухание прошлого
        _prevImpact = impact;
        if (_liquid != null && rising && impact > _bumpMin && _bumpCd <= 0f)
        {
            _bumpCd = 0.12f;
            _liquid.Kick(mouth + fwd * half * 0.8f, Mathf.Clamp01(impact) * _bumpWave);
        }

        // ── крен: волна к низкому борту ──
        float lean = _balance != null ? _balance.Lean : 0f;
        float dLean = (lean - _prevLean) / Mathf.Max(1e-3f, dt);
        _prevLean = lean;
        if (_liquid != null && _waveCd <= 0f && Mathf.Abs(dLean) > 0.4f)
        {
            _waveCd = 0.18f;
            float side = Mathf.Sign(lean != 0f ? lean : dLean);
            _liquid.Kick(mouth - right * side * half * 0.8f, Mathf.Clamp01(Mathf.Abs(dLean) * 0.25f) * _leanWave);
        }

        // ── сервер: через борт ──
        if (!_cookware.IsServerInitialized || _spillCd > 0f || _cookware.Capacity <= 0) return;
        float fill = _cookware.TotalCount / (float)_cookware.Capacity;
        if (fill < _spillFill) return;
        float danger = _balance != null ? _balance.Danger : 0f;
        bool tip = danger >= _spillDanger;
        bool hardBump = rising && impact > 0.6f;
        if (!tip && !hardBump) return;
        _spillCd = _spillEvery;
        ushort product; CatCooks.Food.FoodState state; byte cook;
        int n = _cookware.ServerTakeTop(1, out product, out state, out cook);
        if (n <= 0) return;
        _cookware.ServerSpillOver(product, state, n, cook);
        if (_log) Debug.Log($"[Тачка-тара] «{name}»: {(tip ? "крен" : "кочка")} — порция через борт (заполнено {fill * 100f:0}%)", this);
    }
}
