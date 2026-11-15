using UnityEngine;
using CatCooks.Food;

/// <summary>
/// v21: ТАЧКА ВОЗИТ ГРУЗ, А НЕ ЖИДКОСТЬ. Работает вместе со старой тачкой (PushCart + CartRig + CartBalance:
/// езда, занос, мини-игра баланса — как было).
///
///   • ГРУЗ СТАТИЧЕН: то, что лежит в корыте (масло, паста, порошки, кучки), не плещется и не качается —
///     лежит, как положили. Никаких волн, плеска и брызг.
///   • ЖИДКОСТЬ НЕ ДЕРЖИТ: налили молоко/воду — сразу вытекает лужей рядом (в консоли подсказка).
///     Масло, паста, сметана, мороженое, порошки, сахар, кучки — остаются.
///   • СТОЯНОЧНЫЙ ТОРМОЗ: отпущенная тачка не катается сама. Как только остановилась — «встаёт на тормоз»
///     (не едет вбок/вперёд и не крутится вокруг оси), но может падать и крениться. Взялся за ручки — тормоз снят.
///
/// Вешается на корень тачки (меню CatCooks → Тачка → Тачка с физикой и мини-игрой).
/// </summary>
[DisallowMultipleComponent]
public class CartCargo : MonoBehaviour
{
    [Header("Груз")]
    [Tooltip("Жидкости вытекают из тачки (она возит только твёрдое и густое)")]
    [SerializeField] private bool _noLiquids = true;
    [Tooltip("Содержимое лежит неподвижно: без плеска, волн и качания")]
    [SerializeField] private bool _staticCargo = true;

    [Header("Стояночный тормоз (чтобы не каталась сама)")]
    [SerializeField] private bool _parkBrake = true;
    [Tooltip("Медленнее этого, м/с, — считается, что остановилась")]
    [Range(0.02f, 1f)][SerializeField] private float _stopSpeed = 0.25f;
    [Tooltip("Через сколько секунд после остановки встаёт на тормоз")]
    [Range(0.05f, 2f)][SerializeField] private float _stopAfter = 0.25f;
    [Tooltip("Гашение скорости отпущенной тачки (чтобы быстрее останавливалась)")]
    [Range(0f, 8f)][SerializeField] private float _coastDamping = 2.5f;

    [SerializeField] private bool _log = true;

    private Rigidbody _rb;
    private PushCart _cart;
    private Cookware _cookware;
    private CookwareLiquidVisual _liquid;
    private float _slowT, _tick;
    private bool _braked;
    private RigidbodyConstraints _freeConstraints;
    private bool _warned;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _cart = GetComponent<PushCart>();
        _cookware = GetComponentInChildren<Cookware>(true);
        _liquid = GetComponentInChildren<CookwareLiquidVisual>(true);
        if (_rb != null) _freeConstraints = _rb.constraints;
        if (_log)
            Debug.Log($"[Тачка-груз] «{name}»: тара {(_cookware != null ? "есть" : "нет")}, старая тачка (PushCart) {(_cart != null ? "есть" : "НЕТ — нажми меню CatCooks → Тачка")}, " +
                      $"груз статичен: {_staticCargo}, жидкости вытекают: {_noLiquids}, тормоз: {_parkBrake}", this);
    }

    private void Start()
    {
        if (_staticCargo && _liquid != null) _liquid.TuneStatic();
    }

    private void FixedUpdate()
    {
        if (!_parkBrake || _rb == null) return;
        bool held = _rb.isKinematic || (_cart != null && _cart.IsGripped);
        bool tipped = _cart != null && _cart.IsTipped;
        if (held || tipped)
        {
            if (_braked) Release();
            _slowT = 0f;
            return;
        }
        if (_braked) return;
        float dt = Time.fixedDeltaTime;
        // катится сама (круглые колёса) — гасим ход по земле, падение не трогаем
        Vector3 v = _rb.linearVelocity;
        float k = 1f / (1f + dt * _coastDamping);
        _rb.linearVelocity = new Vector3(v.x * k, v.y, v.z * k);
        Vector3 w = _rb.angularVelocity;
        _rb.angularVelocity = new Vector3(w.x, w.y * k, w.z);
        float flat = new Vector2(v.x, v.z).magnitude;
        if (flat < _stopSpeed && Mathf.Abs(v.y) < 1.5f) _slowT += dt; else _slowT = 0f;
        if (_slowT >= _stopAfter)
        {
            _braked = true;
            _rb.linearVelocity = new Vector3(0f, v.y, 0f);
            _rb.constraints = _freeConstraints | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationY;
        }
    }

    private void Release()
    {
        _braked = false;
        if (_rb != null) _rb.constraints = _freeConstraints;
    }

    private void OnDisable()
    {
        if (_braked) Release();
    }

    private void Update()
    {
        if (!_noLiquids || _cookware == null || !_cookware.IsServerInitialized) return;
        _tick -= Time.deltaTime;
        if (_tick > 0f) return;
        _tick = 0.3f;
        for (int i = 0; i < _cookware.ViewCount; i++)
        {
            BagView v = _cookware.GetView(i);
            if (v.Count <= 0 || !IsLiquid(v.Product, v.State)) continue;
            int n = _cookware.ServerTake(v.Product, v.Count);
            if (n > 0) _cookware.ServerSpillOver(v.Product, v.State, n, v.Cook);
            if (_log && !_warned)
            {
                _warned = true;
                Debug.Log($"[Тачка-груз] «{name}»: жидкость в тачке не держится — вытекла ({n} порц.). Тачка возит масло, пасту, порошки, кучки", this);
            }
            return;                                 // по одному продукту за раз (список изменился)
        }
    }

    /// <summary>Жидкое — то, что льётся: не «вещество» (масло, паста, порошок, сахар) и не густое/замёрзшее.</summary>
    private bool IsLiquid(ushort product, CatCooks.Food.FoodState state)
    {
        if (CatCooks.Food.FoodStateInfo.VisualOf(state) != CatCooks.Food.FoodVisual.Liquid) return false;
        if (state == CatCooks.Food.FoodState.Frozen) return false;
        FoodDatabase db = FoodDatabase.Instance;
        ProductDef def = db != null ? db.Get(product) : null;
        if (def != null && FoodSubstance.Of(def, state) != null) return false;       // масло, паста, порошок — вещество
        CookwareLiquidVisual.Role role;
        if (_liquid != null && _liquid.TryGetRole(product, out role))
        {
            if (role == CookwareLiquidVisual.Role.Paste || role == CookwareLiquidVisual.Role.Thick) return false;
            if (role == CookwareLiquidVisual.Role.IceCream && state != CatCooks.Food.FoodState.Melted) return false;
        }
        return true;
    }
}
