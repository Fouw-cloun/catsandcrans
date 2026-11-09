using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КАПЛИ И БРЫЗГИ ЖИДКОСТИ — только визуал.
    ///
    ///   Splash       — простой всплеск в точке (старый API);
    ///   Stream       — поток капель (пузыри, брызги под струёй) — CreateStream;
    ///   SloshSplash  — НОВОЕ: волна ударила в борт. Яркая вспышка + корона
    ///                  мелких капель вверх-наружу + пара крупных «шлепков».
    ///                  Капли наследуют скорость ведра — при ходьбе летят вперёд;
    ///   ImpactSplash — НОВОЕ: струя/капля упала на поверхность: корона + вспышка;
    ///   Drop         — НОВОЕ: одна капля (отрыв от струи).
    ///
    /// Всё из ОБЩЕГО пула (две системы частиц на всю сцену) — без создания
    /// объектов на каждый всплеск, без мусора. Капли при ударе о мир дают
    /// маленькую вспышку сами (саб-эмиттер).
    /// </summary>
    public static class LiquidFx
    {
        private static Texture2D _dropTex, _flashTex, _bubbleTex;
        private static Material _dropMat, _flashMat, _bubbleMat;
        private static ParticleSystem _drops, _flash, _hits, _bubbles, _pops;

        // ───────────────────────── ТЕКСТУРЫ / МАТЕРИАЛЫ ─────────────────────────

        /// <summary>Мягкая круглая капля с бликом, генерируется один раз.</summary>
        public static Texture2D DropTexture
        {
            get
            {
                if (_dropTex != null) return _dropTex;
                const int N = 64;
                _dropTex = new Texture2D(N, N, TextureFormat.RGBA32, true)
                {
                    name = "CC Liquid Drop (auto)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
                };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01((1f - r) * 6f);
                    float lens = 0.85f + 0.15f * (1f - r);
                    float hl = Mathf.Clamp01(1f - Mathf.Sqrt((u + 0.35f) * (u + 0.35f) + (v - 0.35f) * (v - 0.35f)) * 4f);
                    float c = Mathf.Clamp01(lens + hl * 0.6f);
                    px[y * N + x] = new Color(c, c, c, a);
                }
                _dropTex.SetPixels32(px);
                _dropTex.Apply(true, true);
                return _dropTex;
            }
        }

        /// <summary>Пузырь: плёнка с яркой кромкой и бликом, середина полупрозрачная.</summary>
        private static Texture2D BubbleTexture
        {
            get
            {
                if (_bubbleTex != null) return _bubbleTex;
                const int N = 64;
                _bubbleTex = new Texture2D(N, N, TextureFormat.RGBA32, true)
                {
                    name = "CC Liquid Bubble (auto)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
                };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float inside = Mathf.Clamp01((1f - r) * 10f);
                    float ring = Mathf.Exp(-(r - 0.86f) * (r - 0.86f) / 0.006f);
                    float film = 0.55f + 0.35f * r * r;                       // плёнка к краю плотнее
                    float hl = Mathf.Clamp01(1f - Mathf.Sqrt((u + 0.38f) * (u + 0.38f) + (v - 0.4f) * (v - 0.4f)) * 5f);
                    float hl2 = Mathf.Clamp01(1f - Mathf.Sqrt((u - 0.3f) * (u - 0.3f) + (v + 0.42f) * (v + 0.42f)) * 9f) * 0.5f;
                    float a = Mathf.Clamp01(Mathf.Max(film, ring) + hl) * inside;
                    float c = Mathf.Clamp01(0.82f + ring * 0.25f + (hl + hl2) * 0.8f - (1f - r) * 0.1f);
                    px[y * N + x] = new Color(c, c, c, a);
                }
                _bubbleTex.SetPixels32(px);
                _bubbleTex.Apply(true, true);
                return _bubbleTex;
            }
        }

        private static Material BubbleMaterial
        {
            get
            {
                if (_bubbleMat != null) return _bubbleMat;
                _bubbleMat = MakeParticleMat("CC Liquid Bubbles (auto)", BubbleTexture, false);
                return _bubbleMat;
            }
        }

        /// <summary>Вспышка: мягкое пятно с ярким центром и лучиками.</summary>
        private static Texture2D FlashTexture
        {
            get
            {
                if (_flashTex != null) return _flashTex;
                const int N = 64;
                _flashTex = new Texture2D(N, N, TextureFormat.RGBA32, true)
                {
                    name = "CC Liquid Flash (auto)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
                };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float core = Mathf.Exp(-r * r * 18f);
                    float halo = Mathf.Clamp01(1f - r) * 0.35f;
                    float ang = Mathf.Atan2(v, u);
                    float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f)), 12f) * Mathf.Clamp01(1f - r) * 0.5f;
                    float a = Mathf.Clamp01(core + halo * halo + rays);
                    px[y * N + x] = new Color(1f, 1f, 1f, a);
                }
                _flashTex.SetPixels32(px);
                _flashTex.Apply(true, true);
                return _flashTex;
            }
        }

        public static Material DropMaterial
        {
            get
            {
                if (_dropMat != null) return _dropMat;
                _dropMat = MakeParticleMat("CC Liquid Drops (auto)", DropTexture, false);
                return _dropMat;
            }
        }

        private static Material FlashMaterial
        {
            get
            {
                if (_flashMat != null) return _flashMat;
                _flashMat = MakeParticleMat("CC Liquid Flash (auto)", FlashTexture, true);
                return _flashMat;
            }
        }

        private static Material MakeParticleMat(string name, Texture tex, bool additive)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", additive ? 2f : 0f);      // 2 — Additive в URP
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", additive ? (int)UnityEngine.Rendering.BlendMode.One
                                           : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + (additive ? 2 : 1);
            return m;
        }

        // ───────────────────────── СТАРЫЙ API ─────────────────────────

        /// <summary>Вид капель: размер в метрах мира.</summary>
        public static CCRuntimeParticles.Look DropLook(float size, float speed)
        {
            return new CCRuntimeParticles.Look
            {
                Lifetime = new Vector2(0.35f, 0.8f),
                Speed = new Vector2(speed * 0.5f, speed),
                Size = new Vector2(size * 0.5f, size),
                Gravity = 1.2f,
                ConeAngle = 55f,
                Collide = true,
                Tumble = 0f,
                Stretch = 0.6f,
                Drag = 0.2f,
            };
        }

        /// <summary>Всплеск в точке: капли вверх и в стороны.</summary>
        public static void Splash(Vector3 at, Vector3 up, Color color, int count, float size = 0.03f, float speed = 1.6f)
        {
            ImpactSplash(at, up, color, Mathf.Clamp01(count / 20f) * Mathf.Clamp(speed / 1.6f, 0.4f, 1.5f), size / 0.03f);
        }

        /// <summary>Поток капель (пузыри, брызги под струёй). Включать/выключать — CCRuntimeParticles.SetStream.</summary>
        public static ParticleSystem CreateStream(Transform parent, float size = 0.025f, float speed = 1.2f)
        {
            return CCRuntimeParticles.CreateStream(parent, DropLook(size, speed), DropMaterial);
        }

        // ───────────────────────── ПУЛ ─────────────────────────

        private static bool EnsurePool()
        {
            if (_drops != null && _flash != null && _bubbles != null) return true;
            if (_drops != null) { EnsureBubbles(); return true; }
            if (!Application.isPlaying) return false;
            GameObject root = new GameObject("CC Liquid FX (auto)") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(root);

            _flash = MakeSystem(root.transform, "Flash", FlashMaterial, false, 0f, 300);
            ParticleSystem.ColorOverLifetimeModule fc = _flash.colorOverLifetime;
            fc.enabled = true;
            var fg = new Gradient();
            fg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.35f), new GradientAlphaKey(0f, 1f) });
            fc.color = fg;
            ParticleSystem.SizeOverLifetimeModule fs = _flash.sizeOverLifetime;
            fs.enabled = true;
            fs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.25f)));

            _hits = MakeSystem(root.transform, "DropHits", FlashMaterial, false, 0f, 300);
            ParticleSystem.MainModule hm = _hits.main;
            hm.startLifetime = 0.1f;
            hm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            hm.startSpeed = 0f;
            ParticleSystem.ColorOverLifetimeModule hc = _hits.colorOverLifetime;
            hc.enabled = true;
            hc.color = fg;
            hm.loop = false;
            hm.playOnAwake = false;
            // Саб-эмиттер испускает по своим «вспышкам»: одна частица на удар капли.
            ParticleSystem.EmissionModule he = _hits.emission;
            he.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            _hits.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            _drops = MakeSystem(root.transform, "Drops", DropMaterial, true, 1f, 1500);
            ParticleSystem.CollisionModule col = _drops.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Low;
            col.dampen = 0.85f;
            col.bounce = 0.05f;
            col.lifetimeLoss = 0.7f;
            col.radiusScale = 0.3f;
            col.collidesWith = ~0;
            ParticleSystem.SizeOverLifetimeModule ds = _drops.sizeOverLifetime;
            ds.enabled = true;
            ds.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 0.85f), new Keyframe(1f, 0f)));
            ParticleSystem.LimitVelocityOverLifetimeModule lv = _drops.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.drag = 0.25f;
            lv.limit = 100f;
            // Капля ударилась — маленькая вспышка.
            ParticleSystem.SubEmittersModule sub = _drops.subEmitters;
            sub.enabled = true;
            _hits.transform.SetParent(_drops.transform, false);
            sub.AddSubEmitter(_hits, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritColor, 0.35f);
            _root = root.transform;
            EnsureBubbles();
            return true;
        }

        private static Transform _root;

        /// <summary>Пузыри: вспухают, чуть дрожат, лопаются вспышкой.</summary>
        private static bool EnsureBubbles()
        {
            if (_bubbles != null) return true;
            if (_root == null) return false;
            _pops = MakeSystem(_root, "BubblePops", FlashMaterial, false, 0f, 400);
            ParticleSystem.MainModule pm = _pops.main;
            pm.startLifetime = 0.07f;
            pm.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
            pm.loop = false;
            pm.playOnAwake = false;
            ParticleSystem.ColorOverLifetimeModule pc = _pops.colorOverLifetime;
            pc.enabled = true;
            var pg = new Gradient();
            pg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            pc.color = pg;
            ParticleSystem.EmissionModule pe = _pops.emission;
            pe.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            _pops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            _bubbles = MakeSystem(_root, "Bubbles", BubbleMaterial, false, 0f, 900);
            ParticleSystem.SizeOverLifetimeModule bs = _bubbles.sizeOverLifetime;
            bs.enabled = true;
            // Вспухает → держится → раздувается перед тем, как лопнуть.
            bs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.15f), new Keyframe(0.25f, 0.9f), new Keyframe(0.85f, 1f), new Keyframe(1f, 1.2f)));
            ParticleSystem.ColorOverLifetimeModule bc = _bubbles.colorOverLifetime;
            bc.enabled = true;
            var bg = new Gradient();
            bg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.92f), new GradientAlphaKey(0.2f, 1f) });
            bc.color = bg;
            ParticleSystem.NoiseModule nz = _bubbles.noise;
            nz.enabled = true;
            nz.strength = 0.04f;
            nz.frequency = 3f;
            nz.scrollSpeed = 1.5f;
            ParticleSystem.SubEmittersModule sub = _bubbles.subEmitters;
            sub.enabled = true;
            _pops.transform.SetParent(_bubbles.transform, false);
            sub.AddSubEmitter(_pops, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritColor, 0.8f);
            return true;
        }

        private static ParticleSystem MakeSystem(Transform parent, string name, Material mat, bool stretch, float gravity, int max)
        {
            GameObject go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule m = ps.main;
            m.loop = true;
            m.playOnAwake = true;
            m.duration = 1f;
            m.startLifetime = 0.5f;
            m.startSpeed = 0f;
            m.startSize = 0.02f;
            m.gravityModifier = gravity;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = max;
            m.scalingMode = ParticleSystemScalingMode.Shape;
            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = true;
            e.rateOverTime = 0f;
            ParticleSystem.ShapeModule s = ps.shape;
            s.enabled = false;
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 1.1f;
                r.velocityScale = 0.025f;
            }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        private static Color32 Bright(Color c, float k) => (Color32)Color.Lerp(c, Color.white, k);

        // ───────────────────────── НОВЫЕ ЭФФЕКТЫ ─────────────────────────

        /// <summary>Одна капля (отрыв от струи, стекание с края).</summary>
        public static void Drop(Vector3 at, Vector3 velocity, float size, Color color, float life = 0.9f)
        {
            if (!EnsurePool()) return;
            var p = new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startSize = size, startLifetime = life,
                startColor = Bright(color, 0.15f), applyShapeToPosition = false,
            };
            _drops.Emit(p, 1);
        }

        /// <summary>
        /// Волна ударила в борт (ходьба, рывок, удар по ведру).
        /// at — точка у края изнутри; outward — наружу; up — ось тары;
        /// power 0..1; carrierVelocity — скорость ведра (капли летят вместе с ним).
        /// </summary>
        public static void SloshSplash(Vector3 at, Vector3 outward, Vector3 up, Color color, float power, Vector3 carrierVelocity)
        {
            if (!EnsurePool()) return;
            power = Mathf.Clamp01(power);
            outward = Vector3.ProjectOnPlane(outward, up);
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.forward;
            outward.Normalize();
            Vector3 carry = carrierVelocity * 0.9f;

            // Вспышка — яркий «шлепок» у края.
            var f = new ParticleSystem.EmitParams
            {
                position = at + up * 0.01f, velocity = carry * 0.5f,
                startSize = Mathf.Lerp(0.06f, 0.16f, power), startLifetime = Mathf.Lerp(0.09f, 0.16f, power),
                startColor = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 0.75f, power)), applyShapeToPosition = false,
                rotation = Random.value * 360f,
            };
            _flash.Emit(f, 1);

            // Корона мелких капель: веер вверх-наружу.
            int n = Mathf.RoundToInt(Mathf.Lerp(6f, 28f, power));
            for (int i = 0; i < n; i++)
            {
                float az = (Random.value - 0.5f) * 110f;
                float el = Mathf.Lerp(25f, 80f, Random.value);
                Vector3 h = Quaternion.AngleAxis(az, up) * outward;
                Vector3 dir = h * Mathf.Cos(el * Mathf.Deg2Rad) + up * Mathf.Sin(el * Mathf.Deg2Rad);
                float sp = Mathf.Lerp(0.6f, 2.1f, power) * Random.Range(0.6f, 1.2f);
                var p = new ParticleSystem.EmitParams
                {
                    position = at + h * Random.Range(0f, 0.02f) + up * Random.Range(0f, 0.01f),
                    velocity = dir * sp + carry,
                    startSize = Random.Range(0.006f, 0.02f) * (0.7f + power * 0.6f),
                    startLifetime = Random.Range(0.4f, 0.9f),
                    startColor = Bright(color, Random.Range(0.1f, 0.35f)),
                    applyShapeToPosition = false,
                };
                _drops.Emit(p, 1);
            }
            // Пара крупных шлепков — тяжёлые, недалеко.
            int big = power > 0.35f ? Random.Range(1, 4) : 0;
            for (int i = 0; i < big; i++)
            {
                Vector3 h = Quaternion.AngleAxis((Random.value - 0.5f) * 60f, up) * outward;
                Vector3 dir = (h + up * Random.Range(0.2f, 0.7f)).normalized;
                var p = new ParticleSystem.EmitParams
                {
                    position = at, velocity = dir * Random.Range(0.4f, 1.0f) + carry,
                    startSize = Random.Range(0.025f, 0.045f), startLifetime = Random.Range(0.5f, 0.9f),
                    startColor = Bright(color, 0.1f), applyShapeToPosition = false,
                };
                _drops.Emit(p, 1);
            }
            // Мелкая пыль брызг — быстрая, крошечная.
            for (int i = 0; i < Mathf.RoundToInt(4f + power * 8f); i++)
            {
                Vector3 dir = (Quaternion.AngleAxis((Random.value - 0.5f) * 160f, up) * outward + up * Random.Range(0.5f, 1.5f)).normalized;
                var p = new ParticleSystem.EmitParams
                {
                    position = at, velocity = dir * Random.Range(1.2f, 2.8f) * (0.5f + power) + carry,
                    startSize = Random.Range(0.002f, 0.005f), startLifetime = Random.Range(0.2f, 0.45f),
                    startColor = Bright(color, 0.5f), applyShapeToPosition = false,
                };
                _drops.Emit(p, 1);
            }
        }

        /// <summary>
        /// Пузырь на поверхности (взбивание, кипение): вспухает, живёт долю секунды,
        /// лопается вспышкой, иногда выстреливает мелкой каплей.
        /// up — нормаль поверхности; carrierVelocity — скорость тары (пузырь едет с ней).
        /// </summary>
        public static void Bubble(Vector3 at, Vector3 up, float size, Color color, Vector3 carrierVelocity)
        {
            if (!EnsurePool() || _bubbles == null) return;
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();
            var p = new ParticleSystem.EmitParams
            {
                position = at + up * size * 0.25f,
                velocity = carrierVelocity + up * Random.Range(0f, 0.03f),
                startSize = size,
                startLifetime = Random.Range(0.18f, 0.6f) * Mathf.Clamp(size / 0.02f, 0.6f, 1.6f),
                startColor = Bright(color, Random.Range(0.25f, 0.5f)),
                rotation = Random.value * 360f,
                applyShapeToPosition = false,
            };
            _bubbles.Emit(p, 1);
            // Лопнул с брызгом.
            if (Random.value < 0.3f)
            {
                Vector3 side = Vector3.ProjectOnPlane(Random.onUnitSphere, up) * 0.35f;
                Drop(at + up * size * 0.4f, (up + side).normalized * Random.Range(0.4f, 1.1f) + carrierVelocity,
                     Random.Range(0.003f, 0.008f), color, Random.Range(0.3f, 0.5f));
            }
        }

        /// <summary>Струя или капля упала на поверхность: корона и вспышка. power 0..1, sizeK — масштаб капель.</summary>
        public static void ImpactSplash(Vector3 at, Vector3 normal, Color color, float power, float sizeK = 1f)
        {
            if (!EnsurePool()) return;
            power = Mathf.Clamp01(power);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            normal.Normalize();
            Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(normal, t1);

            var f = new ParticleSystem.EmitParams
            {
                position = at + normal * 0.005f,
                startSize = Mathf.Lerp(0.04f, 0.12f, power) * sizeK, startLifetime = Mathf.Lerp(0.08f, 0.14f, power),
                startColor = new Color(1f, 1f, 1f, Mathf.Lerp(0.25f, 0.6f, power)), applyShapeToPosition = false,
                rotation = Random.value * 360f,
            };
            _flash.Emit(f, 1);

            int n = Mathf.RoundToInt(Mathf.Lerp(5f, 20f, power));
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                float el = Mathf.Lerp(45f, 82f, Random.value) * Mathf.Deg2Rad;
                Vector3 h = t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a);
                Vector3 dir = h * Mathf.Cos(el) + normal * Mathf.Sin(el);
                var p = new ParticleSystem.EmitParams
                {
                    position = at + h * 0.01f,
                    velocity = dir * Mathf.Lerp(0.5f, 1.7f, power) * Random.Range(0.6f, 1.2f),
                    startSize = Random.Range(0.005f, 0.016f) * sizeK * (0.7f + power * 0.5f),
                    startLifetime = Random.Range(0.3f, 0.65f),
                    startColor = Bright(color, Random.Range(0.1f, 0.3f)),
                    applyShapeToPosition = false,
                };
                _drops.Emit(p, 1);
            }
        }
    }
}
