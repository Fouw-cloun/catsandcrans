using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ОБЛАКО ГАЗА. Спавнится баллоном при утечке и живёт само по себе.
///
/// Копится, пока травит, и рассеивается, когда перестало. Поднесли огонь —
/// рвануло: разбросало всё вокруг, разорвало кастрюли под давлением, сорвало
/// крышки.
///
/// ОБЛАКО НЕ ПРИКЛЕЕНО К БАЛЛОНУ. Газ натекает туда, где баллон стоял, и
/// остаётся там даже если баллон унесли. Так и должно быть: унести баллон
/// из угла, где уже натекло, не значит обезвредить угол.
///
/// СЕТЬ: объём и центр синхронизируются, всё остальное каждый клиент считает
/// сам — и облако, и вспышку взрыва.
/// </summary>
public class GasCloud : NetworkBehaviour
{
    [Header("Облако")]
    [Tooltip("Сколько газа нужно, чтобы облако стало опасным")]
    [Min(0.1f)][SerializeField] private float _dangerAmount = 6f;

    [Tooltip("Максимальный объём. Дальше не растёт, но и не безопаснее.")]
    [Min(1f)][SerializeField] private float _maxAmount = 40f;

    [Tooltip("Сколько рассеивается в секунду, когда подпитки нет")]
    [SerializeField] private float _dissipatePerSecond = 1.2f;

    [Tooltip("Радиус при максимальном объёме, метры")]
    [SerializeField] private float _maxRadius = 3.5f;

    [Header("Взрыв")]
    [Tooltip("Насколько дальше облака бьёт ударная волна")]
    [SerializeField] private float _blastMultiplier = 1.6f;

    [Tooltip("Сила разброса при полном облаке")]
    [SerializeField] private float _blastForce = 14f;

    [Tooltip("Подбрасывание вверх: без него всё просто разъезжается по полу")]
    [SerializeField] private float _blastUpward = 0.6f;

    [Tooltip("Слои, которые задевает взрыв")]
    [SerializeField] private LayerMask _blastMask = ~0;

    [Header("Вид")]
    [SerializeField] private Color _gasColor = new Color(0.75f, 0.9f, 0.7f, 0.25f);
    [SerializeField] private float _particlesPerSecond = 40f;

    [Header("Отладка")]
    [Tooltip("Показывать облако видимым. Настоящий газ прозрачный, но пока " +
             "нет других подсказок, лучше видеть, где он есть.")]
    [SerializeField] private bool _visible = true;

    // ───────── сеть ─────────
    private readonly SyncVar<float> _amount = new SyncVar<float>(0f);

    private ParticleSystem _fog;
    private float _carry;
    private readonly Collider[] _hits = new Collider[64];

    public float Amount => _amount.Value;
    public float Radius => Mathf.Lerp(0.4f, _maxRadius, Mathf.Clamp01(_amount.Value / _maxAmount));
    public bool IsDangerous => _amount.Value >= _dangerAmount;

    private void Awake() => CreateFog();

    private void OnDestroy()
    {
        if (_fog != null) Destroy(_fog.gameObject);
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    /// <summary>Подлить газа. Зовётся баллоном, пока травит.</summary>
    [Server]
    public void ServerFeed(Vector3 at, float amount)
    {
        // Центр сдвигается к источнику, но не прыгает: облако расползается,
        // а не телепортируется за баллоном.
        transform.position = Vector3.Lerp(transform.position, at, 0.15f);
        _amount.Value = Mathf.Min(_maxAmount, _amount.Value + amount);
    }

    private void Update()
    {
        if (base.IsServerInitialized) ServerTick();
        DrawFog();
    }

    [Server]
    private void ServerTick()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.1f);

        _amount.Value = Mathf.Max(0f, _amount.Value - _dissipatePerSecond * dt);

        if (_amount.Value <= 0.01f)
        {
            // Рассеялось — объект больше не нужен.
            base.Despawn(base.NetworkObject);
            return;
        }

        if (!IsDangerous) return;

        // Огонь в облаке — рвём. Радиус берём от размера облака: маленькое
        // пшикнет, большое разнесёт полкухни.
        if (IgnitionRegistry.AnyNear(transform.position, Radius, out Vector3 where))
            ServerExplode(where);
    }

    /// <summary>
    /// Взрыв. Разбрасывает всё вокруг и рвёт кастрюли под давлением.
    /// </summary>
    [Server]
    private void ServerExplode(Vector3 spark)
    {
        float power = Mathf.Clamp01(_amount.Value / _maxAmount);
        float radius = Radius * _blastMultiplier;
        Vector3 center = transform.position;

        Debug.Log($"[Газ] ВЗРЫВ: объём {_amount.Value:0.0}, радиус {radius:0.0} м", this);

        RpcExplosion(center, radius, power);

        int n = Physics.OverlapSphereNonAlloc(center, radius, _hits, _blastMask,
                                              QueryTriggerInteraction.Ignore);

        for (int i = 0; i < n; i++)
        {
            Collider c = _hits[i];
            if (c == null) continue;

            // Кастрюли под давлением рвёт заодно — цепная реакция на кухне
            // это ровно то, чего ждёшь от взрыва газа.
            PotThermal pot = c.GetComponentInParent<PotThermal>();
            if (pot != null && !pot.IsRuptured) pot.ServerRupture();

            Rigidbody rb = c.attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;

            rb.AddExplosionForce(_blastForce * power, center, radius,
                                 _blastUpward, ForceMode.Impulse);
        }

        // Газ сгорел весь.
        _amount.Value = 0f;
        base.Despawn(base.NetworkObject);
    }

    /// <summary>Вспышку рисует каждый клиент сам — по сети идёт только факт.</summary>
    [ObserversRpc]
    private void RpcExplosion(Vector3 center, float radius, float power)
    {
        int sparks = Mathf.RoundToInt(Mathf.Lerp(80f, 400f, power));

        SparkBurst.Play(center, Vector3.up, 180f, sparks,
                        Mathf.Lerp(6f, 16f, power),
                        new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.05f),
                        Mathf.Lerp(2f, 6f, power));
    }

    // ───────────────────────── ВИД ─────────────────────────

    private void DrawFog()
    {
        if (_fog == null || !_visible || _amount.Value <= 0.01f) return;

        float r = Radius;
        _carry += _particlesPerSecond * Mathf.Clamp01(_amount.Value / _maxAmount)
                * Time.deltaTime;

        int count = Mathf.FloorToInt(_carry);
        if (count <= 0) return;
        _carry -= count;
        if (count > 10) count = 10;

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            // Газ стелется низом: он тяжелее воздуха, и стоять в нём по колено
            // страшнее, чем видеть облако над головой.
            Vector3 p = Random.insideUnitSphere * r;
            p.y = Mathf.Abs(p.y) * 0.35f;

            ep.position = transform.position + p;
            ep.startSize = Random.Range(r * 0.4f, r * 0.9f);
            ep.startLifetime = Random.Range(0.8f, 1.8f);

            Color c = _gasColor;
            c.a *= Random.Range(0.5f, 1f) * (IsDangerous ? 1.4f : 0.7f);
            ep.startColor = c;

            ep.velocity = new Vector3(Random.Range(-0.15f, 0.15f), 0.05f,
                                      Random.Range(-0.15f, 0.15f));
            _fog.Emit(ep, 1);
        }
    }

    private void CreateFog()
    {
        GameObject go = new GameObject("GasFog (auto)");

        // ВЫКЛЮЧЕННЫМ: система играет сразу при AddComponent, и часть свойств
        // на играющей менять нельзя.
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.startLifetime = 1.5f;
        main.gravityModifier = 0f;

        // При сцене далеко от нуля Automatic отключил бы систему целиком:
        // её трансформ вне экрана, хотя частицы рядом с игроком.
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = g;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");
        if (sh != null) r.material = new Material(sh) { name = "GasFog (auto)" };

        go.SetActive(true);
        ps.Play();
        _fog = ps;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsDangerous ? new Color(1f, 0.3f, 0.2f, 0.5f)
                                   : new Color(0.7f, 0.9f, 0.6f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, Radius);

        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, Radius * _blastMultiplier);
    }
}
