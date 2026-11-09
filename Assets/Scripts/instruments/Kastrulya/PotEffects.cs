using CatCooks.Food;
using UnityEngine;

/// <summary>
/// ЗВУК, ПАР И ТРЯСКА ПОСУДЫ. Вешается на кастрюлю рядом с CookwarePressure (новая) или PotThermal (старая).
///
/// Чисто локальная штука: всё считается у каждого клиента по уже
/// синхронизированному состоянию. По сети не гоняется ничего.
///
/// Система частиц собирается из кода — настраивать в инспекторе нечего.
/// </summary>
public class PotEffects : MonoBehaviour
{
    [Header("Тряска на перегреве")]
    [Tooltip("Размах дрожи корпуса, метры")]
    [SerializeField] private float _shake = 0.006f;

    [Tooltip("Что трясти. Пусто = дочерний визуал не трясётся, только крышка.")]
    [SerializeField] private Transform _shakeTarget;

    [Header("Живая кастрюля (от температуры)")]
    [Tooltip("Общая сила «жизни»: дрожь с 70 °C, подпрыгивания при кипении, сильная тряска под давлением")]
    [Range(0f, 4f)][SerializeField] private float _life = 1.5f;
    [Tooltip("Мультяшное сжатие-растяжение корпуса при кипении (доля размера)")]
    [Range(0f, 0.08f)][SerializeField] private float _squash = 0.02f;

    [Header("Пар")]
    [Tooltip("Откуда идёт пар. Пусто = чуть выше центра кастрюли.")]
    [SerializeField] private Transform _steamPoint;

    [SerializeField] private float _steamPerSecond = 40f;

    [Tooltip("Пар при обычном кипении. У НОВОЙ кастрюли выключи: кипение рисует CookwareHeat " +
             "(пар с поверхности, из-под крышки). Здесь останется пар перегрева, разрыва и взрыва.")]
    [SerializeField] private bool _boilSteam = true;
    [SerializeField] private float _steamSize = 0.07f;

    [Header("Звук")]
    [SerializeField] private AudioSource _boilLoop;
    [SerializeField] private AudioClip _ruptureSound;

    private IPotBody _pot;
    private ParticleSystem _steam;
    private Vector3 _shakeBase;
    private bool _hasBase;
    private float _carry;
    private bool _wasSteaming;

    private void Awake()
    {
        _pot = GetComponent<IPotBody>();
        if (_shakeTarget != null) { _shakeBase = _shakeTarget.localPosition; _hasBase = true; }
        CreateSteam();
    }

    private void OnEnable() { if (_pot != null) _pot.Ruptured += OnRuptured; }
    private void OnDisable() { if (_pot != null) _pot.Ruptured -= OnRuptured; }

    private void OnDestroy()
    {
        if (_steam != null) Destroy(_steam.gameObject);
    }

    private CatCooks.Food.BoilFx _softFx;

    private void Update()
    {
        if (_pot == null) return;
        PotCondition st = _pot.Condition;

        UpdateShake(st);
        UpdateSteam(st);
        UpdateSound(st);
    }

    /// <summary>Трясёт тем сильнее, чем выше давление. Это предупреждение,
    /// которое видно без градусника.</summary>
    private Vector3 _scaleBase;
    private float _sizeK = -1f;

    private float SizeK()
    {
        if (_sizeK > 0f) return _sizeK;
        bool any = false; Bounds b = default(Bounds);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !PotBodyFilter.IsBody(r, transform)) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        _sizeK = any ? Mathf.Max(1f, b.size.y / 0.3f) : 1f;
        return _sizeK;
    }

    /// <summary>
    /// Кастрюля «живёт» от температуры: с 70 °C мелко дрожит, на кипении подпрыгивает
    /// и пружинит (сжатие-растяжение), под давлением трясётся всё сильнее. Амплитуда — от
    /// размера кастрюли, чтобы и у огромной было видно.
    /// </summary>
    private void UpdateShake(PotCondition st)
    {
        if (!_hasBase) return;
        if (_scaleBase == Vector3.zero) _scaleBase = _shakeTarget.localScale;

        float temp = _pot.TemperatureC;
        float heat = Mathf.Clamp01(Mathf.InverseLerp(70f, 100f, temp));
        float boil = st == PotCondition.Boiling ? 1f : 0f;
        float press = st == PotCondition.Overheat ? Mathf.Max(0.3f, _pot.Pressure01) : 0f;
        if (st == PotCondition.Ruptured) heat = boil = press = 0f;
        float k = (heat * 0.25f + boil * 0.5f + press * 2.2f) * _life;

        if (k <= 0.001f)
        {
            _shakeTarget.localPosition = _shakeBase;
            _shakeTarget.localScale = _scaleBase;
            return;
        }

        float amp = _shake * SizeK() * k;
        float t = Time.time;
        float ft = t * (25f + 20f * press);
        // Дрожь — Перлин (связная, как железка), подпрыгивание — короткие «бульк»-толчки.
        Vector3 jitter = new Vector3(Mathf.PerlinNoise(ft, 0.2f) - 0.5f, 0f, Mathf.PerlinNoise(ft, 7.8f) - 0.5f) * 2f;
        float hop = Mathf.Pow(Mathf.Abs(Mathf.Sin(t * 6.1f)), 8f) * (boil + press);
        Vector3 off = jitter * amp + Vector3.up * (hop * amp * 1.5f);
        // Поправка в осях родителя (Shake Target — ребёнок кастрюли).
        Transform par = _shakeTarget.parent;
        if (par != null) off = par.InverseTransformVector(off);
        _shakeTarget.localPosition = _shakeBase + off;

        float sq = 1f + _squash * (boil + press) * _life * Mathf.Sin(t * 8.3f) + _squash * hop * 0.8f;
        float side = 1f / Mathf.Sqrt(Mathf.Max(0.5f, sq));
        // Какая ось модели смотрит вверх (у FBX из Blender это часто Z).
        Vector3 u = _shakeTarget.InverseTransformDirection(Vector3.up);
        int ax = Mathf.Abs(u.x) > Mathf.Abs(u.y) && Mathf.Abs(u.x) > Mathf.Abs(u.z) ? 0 : Mathf.Abs(u.y) > Mathf.Abs(u.z) ? 1 : 2;
        Vector3 m = new Vector3(side, side, side); m[ax] = sq;
        _shakeTarget.localScale = Vector3.Scale(_scaleBase, m);
    }

    /// <summary>Пар идёт, когда кипит. На перегреве бьёт струёй из-под крышки,
    /// у пробитой — валит через дыры.</summary>
    private void UpdateSteam(PotCondition st)
    {
        if (_steam == null) return;

        // У новой кастрюли обычное кипение рисует CookwareHeat — здесь только перегрев и пробоины.
        bool heatDraws = GetComponent<CatCooks.Food.CookwareHeat>() != null;
        if (st == PotCondition.Cold || st == PotCondition.Warm || (st == PotCondition.Boiling && (!_boilSteam || heatDraws)))
        {
            // Гасим уже висящие клубы: иначе после сварки над целой кастрюлей
            // ещё пару секунд болтается пар, и кажется, что она всё ещё парит.
            if (_steam.particleCount > 0 && _wasSteaming)
                _steam.Clear();

            _wasSteaming = false;
            return;
        }

        _wasSteaming = true;

        float rate = st == PotCondition.Overheat ? 3.5f : st == PotCondition.Ruptured ? 1.5f : 1f;
        _carry += _steamPerSecond * rate * Time.deltaTime;

        int count = Mathf.FloorToInt(_carry);
        if (count <= 0) return;
        _carry -= count;
        if (count > 20) count = 20;

        Vector3 at = _steamPoint != null ? _steamPoint.position
                                         : transform.position + Vector3.up * 0.22f;

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            ep.position = at + Random.insideUnitSphere * 0.05f;
            ep.startSize = _steamSize * Random.Range(0.6f, 1.8f);
            ep.startLifetime = Random.Range(0.8f, 2f);

            float a = st == PotCondition.Overheat ? 0.55f : 0.3f;
            ep.startColor = new Color(1f, 1f, 1f, a * Random.Range(0.6f, 1f));

            // На перегреве пар не поднимается, а бьёт в стороны из-под крышки.
            Vector3 dir = st == PotCondition.Overheat
                ? (Vector3.up * 0.4f + Random.insideUnitSphere).normalized
                : (Vector3.up + Random.insideUnitSphere * 0.25f).normalized;

            ep.velocity = dir * Random.Range(0.2f, 0.9f) * rate;
            if (_softFx == null) _softFx = CatCooks.Food.BoilFx.For(transform);
            if (_softFx != null)
            {
                // Мягкие клубы вместо квадратиков.
                float sc = _softFx.Scale;
                _softFx.Steam(ep.position, ep.velocity * 0.6f * Mathf.Sqrt(sc), ep.startSize * 3.5f * sc, a * 0.8f, Color.white);
                continue;
            }
            _steam.Emit(ep, 1);
        }
    }

    private void UpdateSound(PotCondition st)
    {
        if (_boilLoop == null) return;

        bool play = st == PotCondition.Boiling || st == PotCondition.Overheat;
        if (play && !_boilLoop.isPlaying) _boilLoop.Play();
        else if (!play && _boilLoop.isPlaying) _boilLoop.Stop();

        if (play) _boilLoop.pitch = st == PotCondition.Overheat ? 1.35f : 1f;
    }

    private void OnRuptured()
    {
        if (_ruptureSound != null)
            AudioSource.PlayClipAtPoint(_ruptureSound, transform.position, 1f);

        // Разовый выхлоп пара во все стороны — момент разрыва должно быть видно.
        if (_steam == null) return;

        Vector3 at = _steamPoint != null ? _steamPoint.position
                                         : transform.position + Vector3.up * 0.2f;

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < 120; i++)
        {
            ep.position = at + Random.insideUnitSphere * 0.08f;
            ep.startSize = _steamSize * Random.Range(1f, 3f);
            ep.startLifetime = Random.Range(1f, 2.5f);
            ep.startColor = new Color(1f, 1f, 1f, Random.Range(0.4f, 0.8f));
            ep.velocity = Random.onUnitSphere * Random.Range(1.5f, 5f) + Vector3.up;
            _steam.Emit(ep, 1);
        }
    }

    private void CreateSteam()
    {
        GameObject go = new GameObject("PotSteam (auto)");

        // ВЫКЛЮЧЕННЫМ: система играет сразу при AddComponent, и часть свойств
        // на играющей менять нельзя.
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 900;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startSize = _steamSize;
        main.startLifetime = 1.5f;
        main.gravityModifier = -0.08f;      // пар всплывает

        // При сцене далеко от нуля Automatic отключил бы систему целиком:
        // её трансформ вне экрана, хотя частицы рядом с игроком.
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // Клубы разрастаются и тают.
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            AnimationCurve.EaseInOut(0f, 0.5f, 1f, 2.2f));

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = g;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");
        if (sh != null) r.material = new Material(sh) { name = "PotSteam (auto)" };

        go.SetActive(true);
        ps.Play();
        _steam = ps;
    }
}
