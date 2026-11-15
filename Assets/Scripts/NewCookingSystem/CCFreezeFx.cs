using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v23.2: ЛУЧ ЗАМОРОЗКИ «как у Мэй» (Overwatch) — без кубиков.
    ///   • Mist — клубы ледяного пара: мягкие «облачка» с рваным краем, голубые у дула → белые,
    ///     вылетают быстро и тормозят (сопротивление воздуха), разбухают и тают;
    ///   • Streak — яркие вытянутые «струйки» вдоль луча (ядро струи);
    ///   • Flake — снежинки РАЗНЫЕ: ветвистая, звезда-дендрит, шестигранная пластинка (кружатся);
    ///   • Sparkle — искорка-звёздочка (иней, блеск).
    /// Все картинки рисуются кодом, свой прозрачный шейдер «CatCooks/Frost Particle» (Resources).
    /// Несколько общих систем частиц на сцену (по одной на вид) — никаких новых объектов на каждый клуб.
    /// </summary>
    public static class CCFreezeFx
    {
        public static readonly Color IceBlue = new Color(0.55f, 0.84f, 1f, 1f);
        public static readonly Color IceWhite = new Color(0.9f, 0.97f, 1f, 1f);

        private static ParticleSystem _mist, _streak;
        private static readonly ParticleSystem[] _flakes = new ParticleSystem[4];
        private static Texture2D _mistTex, _flakeAtlas;
        private static Shader _sh;

        // ───────────────────────── API ─────────────────────────

        /// <summary>Клуб пара. size — стартовый размер (разбухает ×2.5), drag в системе — летит и тормозит.</summary>
        public static void Mist(Vector3 pos, Vector3 vel, float size, float life, Color color)
        {
            ParticleSystem ps = MistPS;
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startSize = size, startLifetime = life,
                startColor = color, rotation = Random.Range(0f, 360f)
            };
            ps.Emit(ep, 1);
        }

        /// <summary>Яркая вытянутая струйка (ядро луча).</summary>
        public static void Streak(Vector3 pos, Vector3 vel, float size, float life, Color color)
        {
            ParticleSystem ps = StreakPS;
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams { position = pos, velocity = vel, startSize = size, startLifetime = life, startColor = color };
            ps.Emit(ep, 1);
        }

        /// <summary>Снежинка случайного вида (0 — ветвистая, 1 — дендрит, 2 — пластинка).</summary>
        public static void Flake(Vector3 pos, Vector3 vel, float size, float life, Color color)
        {
            ParticleSystem ps = FlakePS(Random.Range(0, 3));
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startSize = size, startLifetime = life,
                startColor = color, rotation = Random.Range(0f, 360f)
            };
            ps.Emit(ep, 1);
        }

        /// <summary>Искорка-звёздочка: вспыхнула и погасла.</summary>
        public static void Sparkle(Vector3 pos, float size)
        {
            ParticleSystem ps = FlakePS(3);
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos, velocity = Vector3.up * 0.02f, startSize = size, startLifetime = Random.Range(0.2f, 0.45f),
                startColor = Color.white, rotation = Random.Range(0f, 45f)
            };
            ps.Emit(ep, 1);
        }

        /// <summary>
        /// Кадр луча из дула: пар, ядро, снежинки, искры. range — дальность, cone — полуугол (°),
        /// power 0..1 (давление). acc — накопитель (держит вызывающий).
        /// </summary>
        public static void Beam(Vector3 origin, Vector3 fwd, float range, float cone, float power, float dt, ref Vector4 acc)
        {
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            float tan = Mathf.Tan(Mathf.Clamp(cone, 3f, 60f) * Mathf.Deg2Rad);
            float k = Mathf.Lerp(0.6f, 1.25f, Mathf.Clamp01(power));
            // пар: с drag ≈3/с пролетает ~ v/3 → v = 3·range
            acc.x += dt * 70f * k;
            while (acc.x >= 1f)
            {
                acc.x -= 1f;
                Vector3 side = Random.insideUnitSphere; side -= fwd * Vector3.Dot(side, fwd);
                Vector3 v = (fwd + side * tan * 0.9f).normalized * range * Random.Range(2.6f, 3.4f) * k;
                Color c = Color.Lerp(IceBlue, IceWhite, Random.Range(0.25f, 1f));
                c.a = Random.Range(0.55f, 0.85f);
                Mist(origin + fwd * range * Random.Range(0f, 0.06f), v, range * Random.Range(0.07f, 0.12f), Random.Range(0.5f, 0.85f), c);
            }
            // ядро: яркие струйки
            acc.y += dt * 45f * k;
            while (acc.y >= 1f)
            {
                acc.y -= 1f;
                Vector3 side = Random.insideUnitSphere; side -= fwd * Vector3.Dot(side, fwd);
                Vector3 v = (fwd + side * tan * 0.35f).normalized * range * Random.Range(3.5f, 5f) * k;
                Color c = Color.Lerp(IceWhite, Color.white, Random.value); c.a = 0.9f;
                Streak(origin, v, range * Random.Range(0.025f, 0.04f), Random.Range(0.16f, 0.28f), c);
            }
            // снежинки разные — летят потоком вперёд и кружатся
            acc.z += dt * 38f * k;
            while (acc.z >= 1f)
            {
                acc.z -= 1f;
                Vector3 side = Random.insideUnitSphere; side -= fwd * Vector3.Dot(side, fwd);
                Vector3 v = (fwd + side * tan * 1.1f).normalized * range * Random.Range(1.6f, 2.6f) * k + Vector3.down * 0.05f;
                Color c = Color.Lerp(IceBlue, Color.white, Random.Range(0.4f, 1f));
                Flake(origin + fwd * range * Random.Range(0.03f, 0.15f), v, range * Random.Range(0.03f, 0.075f), Random.Range(0.8f, 1.4f), c);
            }
            // искры у дула
            acc.w += dt * 12f;
            while (acc.w >= 1f)
            {
                acc.w -= 1f;
                Sparkle(origin + fwd * range * Random.Range(0.02f, 0.3f) + Random.insideUnitSphere * range * 0.04f, range * Random.Range(0.03f, 0.06f));
            }
        }

        /// <summary>Луч упёрся в поверхность: пар растекается кольцом по ней, снежинки и иней.</summary>
        public static void Impact(Vector3 point, Vector3 normal, float scale)
        {
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            for (int i = 0; i < 3; i++)
            {
                Vector3 t = Random.onUnitSphere; t -= normal * Vector3.Dot(t, normal);
                if (t.sqrMagnitude < 1e-4f) t = Vector3.Cross(normal, Vector3.right);
                t.Normalize();
                Color c = Color.Lerp(IceBlue, IceWhite, Random.Range(0.4f, 1f)); c.a = 0.6f;
                Mist(point + normal * scale * 0.2f, t * scale * Random.Range(4f, 7f) + normal * scale * 0.5f, scale * Random.Range(0.6f, 1.1f), Random.Range(0.6f, 1f), c);
            }
            Vector3 d = (Random.onUnitSphere + normal * 1.3f).normalized;
            Flake(point + normal * scale * 0.3f, d * scale * Random.Range(2f, 4f), scale * Random.Range(0.4f, 0.8f), Random.Range(0.7f, 1.2f), Color.Lerp(IceBlue, Color.white, Random.value));
            if (Random.value < 0.6f) Sparkle(point + normal * scale * 0.1f + Random.insideUnitSphere * scale, scale * Random.Range(0.4f, 0.7f));
        }

        // ───────────────────────── СИСТЕМЫ ─────────────────────────

        private static Shader Sh
        {
            get
            {
                if (_sh == null) _sh = Shader.Find("CatCooks/Frost Particle");
                if (_sh == null) _sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                return _sh;
            }
        }

        private static ParticleSystem MistPS
        {
            get
            {
                if (_mist != null) return _mist;
                if (Sh == null) return null;
                _mist = MakePS("CC Freeze Mist (auto)", MistTex, Vector2.one, Vector2.zero, 3000);
                ParticleSystem.ColorOverLifetimeModule col = _mist.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.08f), new GradientAlphaKey(0.45f, 0.55f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);
                ParticleSystem.SizeOverLifetimeModule sz = _mist.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.3f, 1.4f), new Keyframe(1f, 2.6f)));
                ParticleSystem.RotationOverLifetimeModule rot = _mist.rotationOverLifetime;
                rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
                ParticleSystem.LimitVelocityOverLifetimeModule lv = _mist.limitVelocityOverLifetime;
                lv.enabled = true; lv.limit = 100f; lv.dampen = 0f; lv.drag = 3.2f; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
                ParticleSystem.NoiseModule nz = _mist.noise;
                nz.enabled = true; nz.strength = 0.35f; nz.frequency = 0.9f; nz.scrollSpeed = 0.5f; nz.damping = true;
                ParticleSystem.MainModule m = _mist.main; m.gravityModifier = -0.015f;       // холодный пар чуть клубится вверх, потом оседает
                return _mist;
            }
        }

        private static ParticleSystem StreakPS
        {
            get
            {
                if (_streak != null) return _streak;
                if (Sh == null) return null;
                _streak = MakePS("CC Freeze Streak (auto)", MistTex, Vector2.one, Vector2.zero, 1500);
                ParticleSystemRenderer r = _streak.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 2.5f; r.velocityScale = 0.06f;
                ParticleSystem.ColorOverLifetimeModule col = _streak.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(IceBlue, 1f) },
                          new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);
                ParticleSystem.LimitVelocityOverLifetimeModule lv = _streak.limitVelocityOverLifetime;
                lv.enabled = true; lv.limit = 100f; lv.dampen = 0f; lv.drag = 2f; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
                return _streak;
            }
        }

        private static ParticleSystem FlakePS(int variant)
        {
            if (_flakes[variant] != null) return _flakes[variant];
            if (Sh == null) return null;
            Vector2 off = new Vector2((variant % 2) * 0.5f, (variant / 2) * 0.5f);
            ParticleSystem ps = MakePS("CC Freeze Flakes " + variant + " (auto)", FlakeAtlas, new Vector2(0.5f, 0.5f), off, 2000);
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            if (variant == 3)
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            else
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = variant == 3
                ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.3f, 1.2f), new Keyframe(1f, 0f)))
                : new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.15f, 1.1f), new Keyframe(1f, 0.55f)));
            ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
            rot.enabled = true; rot.z = variant == 3 ? new ParticleSystem.MinMaxCurve(0f) : new ParticleSystem.MinMaxCurve(-3f, 3f);
            if (variant != 3)
            {
                ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
                lv.enabled = true; lv.limit = 100f; lv.dampen = 0f; lv.drag = 2.2f; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
                ParticleSystem.NoiseModule nz = ps.noise;
                nz.enabled = true; nz.strength = 0.3f; nz.frequency = 1.3f; nz.scrollSpeed = 0.4f; nz.damping = true;
                ParticleSystem.MainModule m = ps.main; m.gravityModifier = 0.03f;
            }
            _flakes[variant] = ps;
            return ps;
        }

        private static ParticleSystem MakePS(string name, Texture2D tex, Vector2 tiling, Vector2 offset, int max)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            go.SetActive(false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule m = ps.main;
            m.loop = false; m.playOnAwake = false;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = max;
            m.startSpeed = 0f; m.startLifetime = 1f; m.startSize = 0.05f;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            ParticleSystem.EmissionModule em = ps.emission; em.enabled = false;
            ParticleSystem.ShapeModule sh = ps.shape; sh.enabled = false;
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var mat = new Material(Sh) { name = name + " mat", hideFlags = HideFlags.DontSave };
            if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", tex); mat.SetTextureScale("_MainTex", tiling); mat.SetTextureOffset("_MainTex", offset); }
            if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", tex); mat.SetTextureScale("_BaseMap", tiling); mat.SetTextureOffset("_BaseMap", offset); }
            r.sharedMaterial = mat;
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        // ───────────────────────── КАРТИНКИ (кодом) ─────────────────────────

        /// <summary>Облачко пара: мягкое, с «капустным» рваным краем и светлой серединкой.</summary>
        private static Texture2D MistTex
        {
            get
            {
                if (_mistTex != null) return _mistTex;
                const int N = 64;
                _mistTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "CC Freeze Mist (auto)", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                float c = (N - 1) * 0.5f;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x - c) / c, dy = (y - c) / c;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float ang = Mathf.Atan2(dy, dx);
                        // край-«облако»: радиус гуляет по углу (бугорки)
                        float edge = 0.78f + 0.1f * Mathf.Sin(ang * 5f + 1.3f) + 0.06f * Mathf.Sin(ang * 9f + 0.4f) + 0.05f * (Mathf.PerlinNoise(dx * 3f + 5f, dy * 3f + 9f) - 0.5f);
                        float a = Mathf.Clamp01((edge - r) / 0.35f);
                        a = a * a * (3f - 2f * a);
                        float inner = 0.75f + 0.25f * Mathf.PerlinNoise(dx * 4.5f + 2f, dy * 4.5f + 7f);
                        a *= inner;
                        float shade = Mathf.Lerp(1f, 0.86f, Mathf.Clamp01(r));                     // к краю чуть голубее/темнее
                        px[y * N + x] = new Color(shade, Mathf.Lerp(shade, 1f, 0.3f), 1f, a);
                    }
                _mistTex.SetPixels32(px);
                _mistTex.Apply(true, true);
                return _mistTex;
            }
        }

        /// <summary>Атлас 2×2: ветвистая снежинка, дендрит-звезда, шестигранная пластинка, искра.</summary>
        private static Texture2D FlakeAtlas
        {
            get
            {
                if (_flakeAtlas != null) return _flakeAtlas;
                const int C = 64, N = C * 2;
                _flakeAtlas = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "CC Freeze Flakes (auto)", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                for (int v = 0; v < 4; v++)
                {
                    int ox = (v % 2) * C, oy = (v / 2) * C;
                    float c = (C - 1) * 0.5f;
                    for (int y = 0; y < C; y++)
                        for (int x = 0; x < C; x++)
                        {
                            float dx = x - c, dy = y - c;
                            float a = FlakeAlpha(v, dx, dy);
                            float r = Mathf.Sqrt(dx * dx + dy * dy);
                            Color col = Color.Lerp(Color.white, new Color(0.72f, 0.9f, 1f), Mathf.Clamp01(r / 28f) * 0.6f);
                            col.a = a;
                            px[(oy + y) * N + ox + x] = col;
                        }
                }
                _flakeAtlas.SetPixels32(px);
                _flakeAtlas.Apply(true, true);
                return _flakeAtlas;
            }
        }

        private static float FlakeAlpha(int v, float dx, float dy)
        {
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = 0f;
            if (r > 31f) return 0f;
            if (v == 3)
            {
                // искра: 4 луча-иглы + 4 коротких по диагонали + свечение
                float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                a = Mathf.Max(Mathf.Clamp01(1f - ay / 1.6f) * Mathf.Clamp01(1f - ax / 29f), Mathf.Clamp01(1f - ax / 1.6f) * Mathf.Clamp01(1f - ay / 29f));
                float d1 = Mathf.Abs(dx - dy) * 0.7071f, d2 = Mathf.Abs(dx + dy) * 0.7071f;
                a = Mathf.Max(a, Mathf.Max(Mathf.Clamp01(1f - d1 / 1.3f), Mathf.Clamp01(1f - d2 / 1.3f)) * Mathf.Clamp01(1f - r / 13f));
                float glow = Mathf.Clamp01(1f - r / 18f);
                return Mathf.Max(a, glow * glow * 0.6f);
            }
            for (int k = 0; k < 6; k++)
            {
                float ang = k * Mathf.PI / 3f + (v == 2 ? Mathf.PI / 6f : 0f);
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                if (v == 0)
                {
                    a = Mathf.Max(a, Seg(dx, dy, Vector2.zero, dir * 27f, 1.9f));
                    for (int b = 0; b < 2; b++)
                    {
                        float at = b == 0 ? 11f : 18f, len = b == 0 ? 8f : 6f;
                        Vector2 p0 = dir * at;
                        a = Mathf.Max(a, Seg(dx, dy, p0, p0 + Rot(dir, 1.0472f) * len, 1.5f));
                        a = Mathf.Max(a, Seg(dx, dy, p0, p0 + Rot(dir, -1.0472f) * len, 1.5f));
                    }
                }
                else if (v == 1)
                {
                    // дендрит: длинный луч, много коротких веточек «ёлочкой», шарик на конце
                    a = Mathf.Max(a, Seg(dx, dy, Vector2.zero, dir * 28f, 1.6f));
                    for (int b = 0; b < 4; b++)
                    {
                        float at = 8f + b * 5f, len = 7f - b * 1.3f;
                        Vector2 p0 = dir * at;
                        a = Mathf.Max(a, Seg(dx, dy, p0, p0 + Rot(dir, 0.75f) * len, 1.2f));
                        a = Mathf.Max(a, Seg(dx, dy, p0, p0 + Rot(dir, -0.75f) * len, 1.2f));
                    }
                    Vector2 tip = dir * 28f;
                    a = Mathf.Max(a, Mathf.Clamp01(1f - ((new Vector2(dx, dy) - tip).magnitude - 1.8f) / 1.2f));
                }
                else
                {
                    // пластинка: шестиугольник-контур, внутренний шестиугольник и короткие лучики
                    Vector2 p0 = dir * 22f, p1 = new Vector2(Mathf.Cos(ang + Mathf.PI / 3f), Mathf.Sin(ang + Mathf.PI / 3f)) * 22f;
                    a = Mathf.Max(a, Seg(dx, dy, p0, p1, 2.2f));
                    a = Mathf.Max(a, Seg(dx, dy, p0 * 0.5f, p1 * 0.5f, 1.4f));
                    a = Mathf.Max(a, Seg(dx, dy, Vector2.zero, dir * 28f, 1.4f));
                }
            }
            if (v == 2) a = Mathf.Max(a, Mathf.Clamp01(1f - r / 22f) * 0.28f);            // полупрозрачная пластинка
            a = Mathf.Max(a, Mathf.Clamp01(1f - (r - 3.5f) / 1.5f));                      // серединка
            float g = Mathf.Clamp01(1f - r / 30f);
            return Mathf.Max(a, g * g * 0.3f);
        }

        private static Vector2 Rot(Vector2 d, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
        }

        private static float Seg(float x, float y, Vector2 a, Vector2 b, float w)
        {
            Vector2 p = new Vector2(x, y), ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            float d = (p - (a + ab * t)).magnitude;
            return Mathf.Clamp01(1f - (d - w * 0.5f) / 1.2f);
        }
    }
}
