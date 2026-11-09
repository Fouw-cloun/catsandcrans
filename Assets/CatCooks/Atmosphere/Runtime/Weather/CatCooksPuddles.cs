using System.Collections.Generic;
using UnityEngine;

// ============================================================================
//  CatCooksPuddles — лужи вокруг игрока. В дождь растекаются от центра
//  неровным краем, по ним бегут круги; после дождя медленно высыхают.
//  Мокрость копится сама (по CatCooksWeatherState.Rain) и пишется в
//  CatCooksWeatherState.Wetness.
//  Spawn() — разовая лужа любого цвета (молоко, масло, сок) — пригодится
//  для пролитых жидкостей.
// ============================================================================
[AddComponentMenu("CatCooks/Атмосфера/Puddles")]
public class CatCooksPuddles : MonoBehaviour
{
    public int count = 14;
    public float radius = 26f;
    public Vector2 size = new Vector2(1.2f, 3.5f);
    public LayerMask groundMask = ~0;
    [Tooltip("Секунд дождя до полной мокрости / секунд до высыхания")]
    public float soakSeconds = 25f, drySeconds = 90f;
    public Color skyTint = new Color(0.7f, 0.85f, 0.95f);

    class P { public Transform t; public Material m; public float seed; public Color liquid; public float fixedFill = -1f; public float life = -1f; }
    readonly List<P> _rain = new List<P>();
    readonly List<P> _spilled = new List<P>();
    Mesh _quad;
    Shader _sh;

    void OnEnable()
    {
        _sh = Shader.Find("CatCooks/Puddle");
        _quad = MakeQuad();
    }

    void OnDisable()
    {
        foreach (var p in _rain) if (p.t != null) Destroy(p.t.gameObject);
        foreach (var p in _spilled) if (p.t != null) Destroy(p.t.gameObject);
        _rain.Clear(); _spilled.Clear();
    }

    static Mesh MakeQuad()
    {
        var m = new Mesh { name = "Puddle", hideFlags = HideFlags.DontSave };
        m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return m;
    }

    P Create(Vector3 pos, Vector3 normal, float s, Color liquid)
    {
        if (_sh == null) return null;
        var go = new GameObject("Puddle") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, true);
        go.transform.position = pos + normal * 0.02f;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, Random.value * 360f, 0);
        go.transform.localScale = new Vector3(s, 1f, s * Random.Range(0.6f, 1f));
        go.AddComponent<MeshFilter>().sharedMesh = _quad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var p = new P { t = go.transform, m = new Material(_sh) { hideFlags = HideFlags.DontSave }, seed = Random.value * 50f, liquid = liquid };
        p.m.SetFloat("_Seed", p.seed);
        mr.sharedMaterial = p.m;
        return p;
    }

    bool Ground(Vector3 around, out RaycastHit hit)
    {
        Vector2 c = Random.insideUnitCircle * radius;
        Vector3 o = around + new Vector3(c.x, 8f, c.y);
        return Physics.Raycast(o, Vector3.down, out hit, 40f, groundMask, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.85f;
    }

    /// <summary>Разовая лужа (пролили молоко и т.п.). Растекается и живёт lifeSeconds.</summary>
    public void Spawn(Vector3 pos, Vector3 normal, Color liquid, float diameter, float lifeSeconds = 40f)
    {
        var p = Create(pos, normal, diameter, new Color(liquid.r, liquid.g, liquid.b, 1f));
        if (p == null) return;
        p.fixedFill = 0f; p.life = lifeSeconds;
        _spilled.Add(p);
    }

    void Update()
    {
        float rain = CatCooksWeatherState.Rain;
        float w = CatCooksWeatherState.Wetness;
        w = rain > 0.05f ? Mathf.MoveTowards(w, 1f, Time.deltaTime * rain / Mathf.Max(1f, soakSeconds))
                         : Mathf.MoveTowards(w, 0f, Time.deltaTime / Mathf.Max(1f, drySeconds));
        CatCooksWeatherState.Wetness = w;

        bool hasPlayer = CatCooksWeatherState.TryGetPlayer(out Vector3 pl);
        // лужи дождя появляются, когда мокро, и переезжают за игроком
        if (hasPlayer && w > 0.05f)
        {
            while (_rain.Count < count && Ground(pl, out RaycastHit h))
            {
                var p = Create(h.point, h.normal, Random.Range(size.x, size.y), new Color(0, 0, 0, 0));
                if (p == null) break;
                _rain.Add(p);
            }
            foreach (var p in _rain)
                if (p.t != null && (p.t.position - pl).sqrMagnitude > radius * radius * 2.2f && Ground(pl, out RaycastHit h2))
                {
                    p.t.position = h2.point + h2.normal * 0.02f;
                    p.t.rotation = Quaternion.FromToRotation(Vector3.up, h2.normal) * Quaternion.Euler(0, Random.value * 360f, 0);
                }
        }
        Color sky = Color.Lerp(skyTint, CatCooksWeatherState.FogFar, 0.5f);
        foreach (var p in _rain)
        {
            if (p.t == null) continue;
            float fill = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.8f, w + (p.seed % 1f) * 0.15f));
            p.m.SetFloat("_Fill", fill);
            p.m.SetFloat("_Rain", rain);
            p.m.SetColor("_SkyColor", sky);
            p.t.gameObject.SetActive(fill > 0.01f);
        }
        for (int i = _spilled.Count - 1; i >= 0; i--)
        {
            var p = _spilled[i];
            if (p.t == null) { _spilled.RemoveAt(i); continue; }
            p.life -= Time.deltaTime;
            p.fixedFill = Mathf.MoveTowards(p.fixedFill, 1f, Time.deltaTime * 2.5f);   // быстро растекается
            float fade = Mathf.Clamp01(p.life / 6f);
            p.m.SetFloat("_Fill", p.fixedFill * Mathf.Lerp(0.7f, 1f, fade));
            p.m.SetFloat("_Alpha", 0.9f * fade);
            p.m.SetColor("_Liquid", p.liquid);
            p.m.SetColor("_SkyColor", sky);
            p.m.SetFloat("_Rain", rain);
            if (p.life <= 0f) { Destroy(p.t.gameObject); _spilled.RemoveAt(i); }
        }
    }
}
