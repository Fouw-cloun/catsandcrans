using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v19.4: МУЛЬТЯШНЫЙ ХОЛОД — снежинки-звёздочки (шесть лучиков с веточками), искорки инея и
    /// голубоватые «морозные» хлопья. Картинка снежинки рисуется кодом, импортировать нечего.
    ///   • Flake — снежинка: медленно кружится, покачивается, тает;
    ///   • Twinkle — искорка-звёздочка: вспыхнула и погасла (иней на мороженом, лёд);
    ///   • Burst — горсть снежинок с искрами (удар струи азота).
    /// Одна система частиц на всю сцену, только вид (у каждого игрока свой).
    /// </summary>
    public static class CCFrostFx
    {
        private static ParticleSystem _ps;
        private static Texture2D _tex;
        private static Material _mat;
        public static readonly Color Ice = new Color(0.82f, 0.94f, 1f, 1f);
        public static readonly Color DeepIce = new Color(0.55f, 0.8f, 1f, 1f);

        private static ParticleSystem PS
        {
            get
            {
                if (_ps != null) return _ps;
                var go = new GameObject("CC Frost FX (auto)");
                Object.DontDestroyOnLoad(go);
                _ps = go.AddComponent<ParticleSystem>();
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule m = _ps.main;
                m.loop = false; m.playOnAwake = false;
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                m.maxParticles = 4000;
                m.startSpeed = 0f; m.startLifetime = 1f; m.startSize = 0.05f;
                m.gravityModifier = 0.02f;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                ParticleSystem.EmissionModule em = _ps.emission; em.enabled = false;
                ParticleSystem.ShapeModule sh = _ps.shape; sh.enabled = false;

                ParticleSystem.ColorOverLifetimeModule col = _ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                ParticleSystem.SizeOverLifetimeModule sz = _ps.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.15f, 1.1f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.6f)));

                ParticleSystem.RotationOverLifetimeModule rot = _ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-2.2f, 2.2f);

                ParticleSystem.NoiseModule nz = _ps.noise;
                nz.enabled = true; nz.strength = 0.25f; nz.frequency = 1.2f; nz.scrollSpeed = 0.4f; nz.damping = true;

                ParticleSystem.LimitVelocityOverLifetimeModule lv = _ps.limitVelocityOverLifetime;
                lv.enabled = true; lv.limit = 0.6f; lv.dampen = 0.08f;

                ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sharedMaterial = Mat;
                r.sortingFudge = -2f;
                _ps.Play();
                return _ps;
            }
        }

        private static Material Mat
        {
            get
            {
                if (_mat != null) return _mat;
                // v19.5: свой прозрачный шейдер (Resources). Раньше Sprites/Default мог не найтись — квадратики
                Shader s = Shader.Find("CatCooks/Frost Particle");
                if (s == null) s = Shader.Find("Sprites/Default");
                _mat = new Material(s) { name = "CC Frost (auto)", hideFlags = HideFlags.DontSave };
                _mat.SetTexture("_MainTex", Tex);
                return _mat;
            }
        }

        /// <summary>Снежинка: 6 лучей, на каждом две пары веточек, шестигранник в центре, мягкое свечение.</summary>
        private static Texture2D Tex
        {
            get
            {
                if (_tex != null) return _tex;
                const int N = 64;
                _tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "CC Snowflake (auto)", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                float c = (N - 1) * 0.5f;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = x - c, dy = y - c;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = 0f;
                        for (int k = 0; k < 6; k++)
                        {
                            float ang = k * Mathf.PI / 3f;
                            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                            a = Mathf.Max(a, Seg(dx, dy, Vector2.zero, dir * 27f, 1.9f));
                            for (int b = 0; b < 2; b++)
                            {
                                float at = b == 0 ? 11f : 18f, len = b == 0 ? 8f : 6f;
                                Vector2 p0 = dir * at;
                                Vector2 l = new Vector2(Mathf.Cos(ang + 1.0472f), Mathf.Sin(ang + 1.0472f)) * len;
                                Vector2 rr = new Vector2(Mathf.Cos(ang - 1.0472f), Mathf.Sin(ang - 1.0472f)) * len;
                                a = Mathf.Max(a, Seg(dx, dy, p0, p0 + l, 1.5f));
                                a = Mathf.Max(a, Seg(dx, dy, p0, p0 + rr, 1.5f));
                            }
                        }
                        a = Mathf.Max(a, Mathf.Clamp01(1f - (r - 4f) / 1.5f));        // серединка
                        float glow = Mathf.Clamp01(1f - r / 30f);
                        a = Mathf.Max(a, glow * glow * 0.35f);
                        float tint = Mathf.Clamp01(r / 28f);
                        Color col = Color.Lerp(Color.white, new Color(0.75f, 0.9f, 1f), tint * 0.6f);
                        col.a = a;
                        px[y * N + x] = col;
                    }
                _tex.SetPixels32(px);
                _tex.Apply(true, true);
                return _tex;
            }
        }

        private static float Seg(float x, float y, Vector2 a, Vector2 b, float w)
        {
            Vector2 p = new Vector2(x, y), ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            float d = (p - (a + ab * t)).magnitude;
            return Mathf.Clamp01(1f - (d - w * 0.5f) / 1.2f);
        }

        public static void Flake(Vector3 pos, Vector3 vel, float size, float life, Color color)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startSize = size, startLifetime = life,
                startColor = color, rotation = Random.Range(0f, 360f)
            };
            PS.Emit(ep, 1);
        }

        public static void Twinkle(Vector3 pos, float size)
        {
            Flake(pos, Vector3.up * 0.02f, size, Random.Range(0.25f, 0.5f), Color.white);
        }

        /// <summary>Горсть снежинок с искрами из точки (удар струи, хлопок мороза).</summary>
        public static void Burst(Vector3 pos, Vector3 normal, float scale, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 d = (Random.onUnitSphere + normal * 1.2f).normalized;
                float s = scale * Random.Range(0.6f, 1.4f);
                Flake(pos + Random.insideUnitSphere * scale * 0.5f, d * Random.Range(0.15f, 0.5f), s,
                      Random.Range(0.7f, 1.4f), Color.Lerp(Ice, Color.white, Random.value));
            }
        }
    }
}
