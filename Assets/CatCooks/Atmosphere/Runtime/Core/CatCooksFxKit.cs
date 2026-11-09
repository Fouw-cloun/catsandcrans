using UnityEngine;

// ============================================================================
//  CatCooksFxKit — общие текстуры, материалы и «сборка» систем частиц для
//  погодных эффектов (дождь, брызги, круги, искры, песок).
//  Шейдер частиц свой: CatCooks/FX Particle (добавь в Always Included Shaders).
// ============================================================================
public static class CatCooksFxKit
{
    static Texture2D _dot, _ring, _blob, _streak;
    static Material _alpha, _add;

    public static Texture2D Dot => _dot != null ? _dot : (_dot = Make("FX Dot", (u, v) =>
    {
        float r = Mathf.Sqrt(u * u + v * v);
        return Mathf.Clamp01((1f - r) * 6f);                      // чёткая мультяшная капля
    }));

    public static Texture2D Ring => _ring != null ? _ring : (_ring = Make("FX Ring", (u, v) =>
    {
        float r = Mathf.Sqrt(u * u + v * v);
        return Mathf.Clamp01(1f - Mathf.Abs(r - 0.78f) * 11f);    // тонкое кольцо (круг на воде)
    }));

    public static Texture2D Blob => _blob != null ? _blob : (_blob = Make("FX Blob", (u, v) =>
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float a = Mathf.Atan2(v, u);
        float edge = 0.78f + 0.12f * Mathf.Sin(a * 5f) * Mathf.Cos(a * 3f);   // клякса с лепестками
        return Mathf.Clamp01((edge - r) * 8f);
    }));

    public static Texture2D Streak => _streak != null ? _streak : (_streak = Make("FX Streak", (u, v) =>
    {
        float w = Mathf.Clamp01(1f - Mathf.Abs(u) * 2.2f);
        float l = Mathf.Clamp01(1f - Mathf.Abs(v));
        return Mathf.Clamp01(w * w * l * 2f);
    }));

    static Texture2D Make(string name, System.Func<float, float, float> f)
    {
        const int N = 64;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                byte a = (byte)(Mathf.Clamp01(f(u, v)) * 255f);
                px[y * N + x] = new Color32(255, 255, 255, a);
            }
        t.SetPixels32(px);
        t.Apply(true, true);
        return t;
    }

    public static Material Mat(Texture tex, bool additive)
    {
        var sh = Shader.Find("CatCooks/FX Particle");
        if (sh == null) { Debug.LogWarning("[FX] нет шейдера CatCooks/FX Particle"); return null; }
        var m = new Material(sh) { hideFlags = HideFlags.DontSave, mainTexture = tex };
        m.SetFloat("_SrcBlend", additive ? 1f : 5f);     // One / SrcAlpha
        m.SetFloat("_DstBlend", additive ? 1f : 10f);    // One / OneMinusSrcAlpha
        m.renderQueue = 3000;
        return m;
    }

    /// <summary>Пустая система частиц в мировом пространстве, без формы и эмиссии — настраивает вызывающий.</summary>
    public static ParticleSystem System(Transform parent, string name, Material mat, int max)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.playOnAwake = false;
        main.loop = true;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    public static Gradient Fade(Color c, float aStart = 1f, float holdUntil = 0.5f)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(aStart, 0f), new GradientAlphaKey(aStart, holdUntil), new GradientAlphaKey(0f, 1f) });
        return g;
    }
}
