using UnityEngine;

// ============================================================================
//  CatCooksSandstorm — песчаная буря в два режима.
//   1) ДАЛЕКО: стена песка у горизонта со стороны ветра. Пучится, растёт
//      и медленно надвигается на игрока. Дёшево: один меш, один шейдер.
//   2) У ИГРОКА: когда стена дошла — вокруг игрока кольцо песчаной мглы,
//      порывы-росчерки песка и клубы, летящие по ветру. Дороже, но только
//      рядом с камерой.
//  Сила — CatCooksWeatherState.Dust (пульт пишет из погоды «пыльная буря»).
// ============================================================================
[AddComponentMenu("CatCooks/Атмосфера/Sandstorm")]
public class CatCooksSandstorm : MonoBehaviour
{
    [Header("Стена (далеко)")]
    public float farRadius = 900f;
    public float nearRadius = 45f;
    [Tooltip("Сколько секунд стена идёт от горизонта до игрока")]
    public float approachSeconds = 90f;
    public float wallHeight = 220f;
    [Range(30f, 360f)] public float wallArc = 160f;
    public Color sandBottom = new Color(0.86f, 0.62f, 0.38f);
    public Color sandTop = new Color(0.95f, 0.8f, 0.6f);
    [Header("У игрока")]
    public float gustRate = 220f;
    public float puffRate = 18f;
    public float zone = 30f;
    [Tooltip("Ручная сила для теста; -1 = из погоды")]
    [Range(-1f, 1f)] public float overrideDust = -1f;

    Transform _wall, _veil;
    Material _wallMat, _veilMat;
    ParticleSystem _gusts, _puffs;
    float _progress;   // 0 — стена на горизонте, 1 — у игрока

    void OnEnable()
    {
        var sh = Shader.Find("CatCooks/Fog Wall");
        if (sh == null) { Debug.LogWarning("[Песчаная буря] нет шейдера CatCooks/Fog Wall"); enabled = false; return; }
        _wallMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        _wallMat.SetFloat("_Billow", 45f); _wallMat.SetFloat("_NoiseScale", 4f); _wallMat.SetFloat("_Speed", 0.12f);
        _wallMat.SetFloat("_TopRagged", 0.9f); _wallMat.SetFloat("_Puff", 0.5f); _wallMat.SetFloat("_SoftFade", 60f);
        _wall = CatCooksRingMesh.Create(transform, "Sand Wall", 1f, wallHeight, wallArc, 48, 8, _wallMat);
        _veilMat = new Material(_wallMat) { hideFlags = HideFlags.DontSave };
        _veilMat.SetFloat("_Billow", 6f); _veilMat.SetFloat("_SoftFade", 6f); _veilMat.SetFloat("_Puff", 0.2f);
        _veil = CatCooksRingMesh.Create(transform, "Sand Veil", 1f, 30f, 360f, 40, 4, _veilMat);

        _gusts = CatCooksFxKit.System(transform, "Sand Gusts", CatCooksFxKit.Mat(CatCooksFxKit.Streak, false), 1500);
        var gm = _gusts.main; gm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f); gm.startSpeed = 0f;
        gm.startSize3D = true; gm.startSizeX = new ParticleSystem.MinMaxCurve(0.03f, 0.08f); gm.startSizeY = new ParticleSystem.MinMaxCurve(0.6f, 1.6f); gm.startSizeZ = 1f;
        gm.startColor = new Color(sandTop.r, sandTop.g, sandTop.b, 0.5f);
        var gs = _gusts.shape; gs.enabled = true; gs.shapeType = ParticleSystemShapeType.Box; gs.scale = new Vector3(zone, 6f, zone);
        var gr = _gusts.GetComponent<ParticleSystemRenderer>(); gr.renderMode = ParticleSystemRenderMode.Stretch; gr.velocityScale = 0.05f;
        var gv = _gusts.velocityOverLifetime; gv.enabled = true; gv.space = ParticleSystemSimulationSpace.World;
        var gn = _gusts.noise; gn.enabled = true; gn.strength = 1.5f; gn.frequency = 0.3f;

        _puffs = CatCooksFxKit.System(transform, "Sand Puffs", CatCooksFxKit.Mat(CatCooksFxKit.Blob, false), 200);
        var pm = _puffs.main; pm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f); pm.startSpeed = 0f;
        pm.startSize = new ParticleSystem.MinMaxCurve(3f, 7f); pm.startColor = new Color(sandBottom.r, sandBottom.g, sandBottom.b, 0.35f);
        pm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var ps = _puffs.shape; ps.enabled = true; ps.shapeType = ParticleSystemShapeType.Box; ps.scale = new Vector3(zone, 3f, zone);
        var pv = _puffs.velocityOverLifetime; pv.enabled = true; pv.space = ParticleSystemSimulationSpace.World;
        var pc = _puffs.colorOverLifetime; pc.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                          new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(0, 1) });
        pc.color = g;
        _gusts.Play(); _puffs.Play();
    }

    void OnDisable()
    {
        foreach (var t in new Component[] { _wall, _veil, _gusts, _puffs })
            if (t != null) Destroy(t.gameObject);
    }

    void Update()
    {
        if (_wall == null || !CatCooksWeatherState.TryGetPlayer(out Vector3 pl)) return;
        float dust = overrideDust >= 0f ? overrideDust : CatCooksWeatherState.Dust;
        // буря начинается — стена идёт; кончается — откатывается быстрее
        _progress = dust > 0.3f ? Mathf.MoveTowards(_progress, 1f, Time.deltaTime / Mathf.Max(5f, approachSeconds))
                                : Mathf.MoveTowards(_progress, 0f, Time.deltaTime / Mathf.Max(5f, approachSeconds * 0.4f));
        float strength = Mathf.Clamp01(dust * 1.4f);
        Vector3 wind = CatCooksWeatherState.WindDir3;
        if (wind.sqrMagnitude < 0.01f) wind = Vector3.forward;
        wind.Normalize();

        // стена: со стороны, ОТКУДА дует ветер
        float r = Mathf.Lerp(farRadius, nearRadius, Mathf.SmoothStep(0f, 1f, _progress));
        _wall.position = new Vector3(pl.x, pl.y - 30f, pl.z);
        _wall.rotation = Quaternion.LookRotation(-wind, Vector3.up);
        _wall.localScale = new Vector3(r, 1f, r);
        _wallMat.SetFloat("_Opacity", Mathf.Lerp(0f, 0.95f, strength) * Mathf.Lerp(0.85f, 1f, _progress));
        _wallMat.SetColor("_ColorBottom", sandBottom); _wallMat.SetColor("_ColorTop", sandTop);
        _wall.gameObject.SetActive(strength > 0.01f);

        // у игрока — только когда стена близко
        float nearK = Mathf.Clamp01((_progress - 0.75f) / 0.25f) * strength;
        _veil.position = new Vector3(pl.x, pl.y - 6f, pl.z);
        _veil.localScale = new Vector3(Mathf.Lerp(40f, 14f, nearK), 1f, Mathf.Lerp(40f, 14f, nearK));
        _veilMat.SetFloat("_Opacity", nearK * 0.75f);
        _veilMat.SetColor("_ColorBottom", sandBottom); _veilMat.SetColor("_ColorTop", sandTop);
        _veil.gameObject.SetActive(nearK > 0.01f);

        _gusts.transform.position = pl + wind * -zone * 0.4f + Vector3.up * 1.5f;
        _puffs.transform.position = pl + wind * -zone * 0.4f;
        var ge = _gusts.emission; ge.rateOverTime = gustRate * nearK;
        var pe = _puffs.emission; pe.rateOverTime = puffRate * nearK;
        float sp = Mathf.Lerp(8f, 26f, strength);
        var gv = _gusts.velocityOverLifetime; gv.x = wind.x * sp; gv.y = 0.5f; gv.z = wind.z * sp;
        var pv = _puffs.velocityOverLifetime; pv.x = wind.x * sp * 0.4f; pv.y = 0.3f; pv.z = wind.z * sp * 0.4f;
    }
}
