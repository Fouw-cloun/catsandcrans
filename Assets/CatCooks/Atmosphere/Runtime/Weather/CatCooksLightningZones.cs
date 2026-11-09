using UnityEngine;

// ============================================================================
//  CatCooksLightningZones — куда бьёт молния.
//   • Ближняя зона (вокруг игрока): разряд в землю + место удара: вспышка
//     света, искры, ударное кольцо, дым, выжженное пятно на земле.
//   • Дальняя зона (за игровой зоной): только разряд у горизонта, без удара.
//  Пульт молний (CatCooksLightning) спрашивает этот компонент, где бить
//  (поле Zones). Seed общий — у всех игроков одна и та же молния.
// ============================================================================
[AddComponentMenu("CatCooks/Атмосфера/Lightning Zones")]
public class CatCooksLightningZones : MonoBehaviour
{
    public CatCooksLightningBolt bolt;
    [Header("Ближняя зона (игрок)")]
    [Range(0f, 1f)] public float nearChance = 0.25f;
    public Vector2 nearDistance = new Vector2(25f, 70f);
    [Header("Дальняя зона (за игровой)")]
    public Vector2 farDistance = new Vector2(250f, 750f);
    public LayerMask groundMask = ~0;
    [Header("Удар")]
    public Color impactColor = new Color(0.75f, 0.85f, 1f);
    public float lightIntensity = 12f;
    public float lightRange = 45f;
    public float scorchSize = 3f;
    public float scorchSeconds = 25f;

    ParticleSystem _sparks, _ring, _smoke;
    Light _light; float _lightT;
    Mesh _quad; Material _scorchMat;

    /// <summary>Вызывает CatCooksLightning. true — разряд в землю случился.</summary>
    public bool Fire(int seed, float power)
    {
        if (bolt == null || !CatCooksWeatherState.TryGetPlayer(out Vector3 pl)) return false;
        var rng = new System.Random(seed ^ 0x5bd1e995);
        bool near = rng.NextDouble() < nearChance * Mathf.Lerp(0.5f, 1f, power);
        Vector2 d = near ? nearDistance : farDistance;
        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
        float dist = Mathf.Lerp(d.x, d.y, (float)rng.NextDouble());
        Vector3 p = pl + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
        Vector3 n = Vector3.up;
        if (Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out RaycastHit h, 800f, groundMask, QueryTriggerInteraction.Ignore))
        { p = h.point; n = h.normal; }
        else p.y = pl.y - 2f;
        bool ok = bolt.FireAt(p, seed, power);
        if (ok && near) Impact(p, n);
        return ok;
    }

    void Build()
    {
        if (_sparks != null) return;
        _sparks = CatCooksFxKit.System(transform, "Bolt Sparks", CatCooksFxKit.Mat(CatCooksFxKit.Dot, true), 300);
        var m = _sparks.main; m.loop = false; m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f); m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        m.gravityModifier = 1.4f; m.startColor = impactColor;
        var sh = _sparks.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.3f;
        var r = _sparks.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f;
        var c = _sparks.colorOverLifetime; c.enabled = true; c.color = CatCooksFxKit.Fade(impactColor, 1f, 0.3f);

        _ring = CatCooksFxKit.System(transform, "Bolt Ring", CatCooksFxKit.Mat(CatCooksFxKit.Ring, true), 8);
        var rm = _ring.main; rm.loop = false; rm.startLifetime = 0.45f; rm.startSpeed = 0f; rm.startSize = 2f;
        rm.startRotation3D = true; rm.startRotationX = Mathf.PI * 0.5f; rm.startColor = impactColor;
        var rs = _ring.sizeOverLifetime; rs.enabled = true; rs.size = new ParticleSystem.MinMaxCurve(6f, AnimationCurve.EaseInOut(0, 0.2f, 1, 1f));
        var rc = _ring.colorOverLifetime; rc.enabled = true; rc.color = CatCooksFxKit.Fade(Color.white, 1f, 0.1f);
        _ring.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.World;

        _smoke = CatCooksFxKit.System(transform, "Bolt Smoke", CatCooksFxKit.Mat(CatCooksFxKit.Blob, false), 40);
        var sm = _smoke.main; sm.loop = false; sm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.8f); sm.startSize = new ParticleSystem.MinMaxCurve(1f, 2.5f);
        sm.startColor = new Color(0.35f, 0.33f, 0.38f, 0.7f);
        var ssh = _smoke.shape; ssh.enabled = true; ssh.shapeType = ParticleSystemShapeType.Hemisphere; ssh.radius = 0.6f;
        var ssz = _smoke.sizeOverLifetime; ssz.enabled = true; ssz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
        var sc = _smoke.colorOverLifetime; sc.enabled = true; sc.color = CatCooksFxKit.Fade(Color.white, 1f, 0.4f);

        var lgo = new GameObject("Bolt Light") { hideFlags = HideFlags.DontSave };
        lgo.transform.SetParent(transform, false);
        _light = lgo.AddComponent<Light>(); _light.type = LightType.Point; _light.color = impactColor;
        _light.range = lightRange; _light.intensity = 0f; _light.shadows = LightShadows.None;

        _quad = new Mesh { name = "Scorch", hideFlags = HideFlags.DontSave };
        _quad.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f) };
        _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        _scorchMat = CatCooksFxKit.Mat(CatCooksFxKit.Blob, false);
    }

    void Impact(Vector3 p, Vector3 n)
    {
        Build();
        var ep = new ParticleSystem.EmitParams { position = p + n * 0.1f, applyShapeToPosition = true };
        _sparks.Emit(ep, 60); _ring.Emit(new ParticleSystem.EmitParams { position = p + n * 0.05f }, 1); _smoke.Emit(ep, 10);
        _light.transform.position = p + n * 2f; _lightT = 0.35f;
        if (_scorchMat != null)
        {
            var go = new GameObject("Scorch") { hideFlags = HideFlags.DontSave };
            go.transform.position = p + n * 0.03f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0, Random.value * 360f, 0);
            go.transform.localScale = Vector3.one * scorchSize;
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = _scorchMat;
            var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Tint", new Color(0.12f, 0.1f, 0.1f, 0.85f)); mr.SetPropertyBlock(mpb);
            Destroy(go, scorchSeconds);
        }
    }

    void Update()
    {
        if (_light == null) return;
        _lightT -= Time.deltaTime;
        // мерцание: два-три всплеска
        _light.intensity = _lightT > 0f ? lightIntensity * Mathf.Clamp01(_lightT / 0.35f) * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(_lightT * 70f))) : 0f;
    }
}
