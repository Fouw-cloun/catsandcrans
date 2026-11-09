using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЗОЛОТЫЕ ЗВЁЗДОЧКИ «БЛЮДО ГОТОВО» — общий фонтанчик для любых блюд (макарон, …).
    /// Только вид, у каждого игрока свой: звать из ObserversRpc.
    ///   CelebrationStars.Burst(точка, масштаб, цвет, сколько);
    /// Одна система частиц на всю сцену (мировые координаты): прошлые звёздочки долетают,
    /// новые вылетают из новой точки. Ничего настраивать не надо.
    /// </summary>
    public static class CelebrationStars
    {
        private static ParticleSystem _ps;
        private static Texture2D _tex;

        public static readonly Color Gold = new Color(1f, 0.82f, 0.22f, 1f);

        /// <param name="at">Откуда вылетают (мир)</param>
        /// <param name="scale">1 — под предмет ~0.6 м; меньше — ниже и мельче</param>
        public static void Burst(Vector3 at, float scale, Color color, int count)
        {
            if (count <= 0) return;
            if (_ps == null) _ps = Make();
            if (_ps == null) return;
            float k = Mathf.Clamp(scale, 0.15f, 4f);
            _ps.transform.SetPositionAndRotation(at, Quaternion.identity);
            ParticleSystem.MainModule m = _ps.main;
            m.startSpeed = new ParticleSystem.MinMaxCurve(2.2f * k, 4.2f * k);
            m.startSize = new ParticleSystem.MinMaxCurve(0.09f * k, 0.2f * k);
            m.gravityModifier = 0.75f;
            m.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.55f));
            ParticleSystem.ShapeModule sh = _ps.shape;
            sh.radius = 0.12f * k;
            _ps.Emit(count);
        }

        private static ParticleSystem Make()
        {
            var go = new GameObject("CelebrationStars (auto)");
            Object.DontDestroyOnLoad(go);
            go.SetActive(false);                                         // свойства — до первого Play
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule m = ps.main;
            m.playOnAwake = false;
            m.loop = false;
            m.duration = 1f;
            m.maxParticles = 400;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.7f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule em = ps.emission; em.enabled = false;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 28f; sh.radius = 0.12f;
            sh.rotation = new Vector3(-90f, 0f, 0f);                      // конус вверх
            ParticleSystem.RotationOverLifetimeModule rl = ps.rotationOverLifetime;
            rl.enabled = true; rl.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            ParticleSystem.SizeOverLifetimeModule sl = ps.sizeOverLifetime;
            sl.enabled = true;
            sl.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.15f, 1f), new Keyframe(0.75f, 0.85f), new Keyframe(1f, 0f)));
            ParticleSystem.ColorOverLifetimeModule cl = ps.colorOverLifetime;
            cl.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            cl.color = g;
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Shader shd = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shd == null) Debug.LogWarning("[Звёздочки] нет шейдера Sprites/Default — звёздочки не видны");
            else
            {
                var mat = new Material(shd) { name = "CelebrationStars (auto)" };
                mat.mainTexture = Tex();
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", Tex());
                r.sharedMaterial = mat;
            }
            go.SetActive(true);
            return ps;
        }

        /// <summary>Пятиконечная звёздочка с мягким ореолом (белая — цвет даёт частица).</summary>
        private static Texture2D Tex()
        {
            if (_tex != null) return _tex;
            const int N = 64;
            _tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "CelebrationStar (auto)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float rr = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Atan2(u, v);
                    float f = Mathf.Repeat(a / (Mathf.PI * 2f) * 5f, 1f);
                    float tri = 1f - Mathf.Abs(f - 0.5f) * 2f;               // 0 на луче, 1 между лучами
                    float rs = Mathf.Lerp(0.95f, 0.4f, Mathf.Pow(tri, 0.8f));
                    float star = Mathf.Clamp01((rs - rr) / 0.06f);
                    float glow = 0.35f * Mathf.Exp(-rr * rr * 6f);
                    float core = Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(1f - rr * 1.6f));
                    px[y * N + x] = new Color(core, core, core, Mathf.Clamp01(star + glow));
                }
            _tex.SetPixels(px);
            _tex.Apply(false, true);
            return _tex;
        }
    }
}
