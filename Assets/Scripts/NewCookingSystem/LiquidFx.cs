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
    ///   v4 — МУЛЬТЯШНО: капли с тёмной обводкой и белым бликом (шейдер CatCooks/Toon Drop),
    ///                  вылетают «с пружинкой»; упавшая на пол капля оставляет кляксу (Splat),
    ///                  которая лежит пару секунд и тает; Puff — клуб пены (туча при взбивании).
    ///   v5 — ГЛЯНЕЦ: капля как на картинках молока — объёмная, белый блик-полоса,
    ///                  холодная тень снизу, тонкая обводка. Капель меньше, но крупнее;
    ///                  у камеры капли и тучки тают (не липнут на экран), размер на экране ограничен.
    ///   v6 — ПУФЫ: объёмные комки пены (меш, шейдер CatCooks/Toon Poof) — вспухают,
    ///                  разлетаются, тормозят и сжимаются в точку. WhipBurst — «взрывчики»
    ///                  разных видов в точке взбивания: клубок, взрывчик с ядром и искрами,
    ///                  кольцо по поверхности, столбик вверх, россыпь.
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

        /// <summary>
        /// Капли (v4 — мультяшные): если есть шейдер «CatCooks/Toon Drop» — тело цвета жидкости,
        /// тёмная обводка, белый блик-боб. Нет шейдера — старая мягкая капля.
        /// </summary>
        public static Material DropMaterial
        {
            get
            {
                if (_dropMat != null) return _dropMat;
                _dropMat = MakeToonMat("CC Liquid Drops (toon)", ToonMask(0), false);
                if (_dropMat == null) _dropMat = MakeParticleMat("CC Liquid Drops (auto)", DropTexture, false);
                return _dropMat;
            }
        }

        // ───────────────────────── МУЛЬТЯШНЫЕ МАСКИ ─────────────────────────

        private static readonly Texture2D[] _toonTex = new Texture2D[3];
        private static Material _puffMat, _splatMat;

        /// <summary>
        /// Маски для «CatCooks/Toon Drop»: R — форма, B — блик,
        /// G — заливка: 0.1 — обводка, 0.3..1 — свет на теле (0.3 — тень, 1 — полный свет).
        /// kind: 0 — капля, 1 — клуб пены (облачко из кружков), 2 — клякса на полу (с брызгами-точками).
        /// </summary>
        public static Texture2D ToonMask(int kind)
        {
            kind = Mathf.Clamp(kind, 0, 2);
            if (_toonTex[kind] != null) return _toonTex[kind];
            const int N = 96;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true, true)
            {
                name = "CC Toon Mask " + kind + " (auto)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            var px = new Color[N * N];
            float aa = 2.2f / N;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float sd, line, hl, lit;
                ToonShape(kind, u, v, out sd, out line, out hl, out lit);
                float a = Mathf.Clamp01(-sd / aa + 0.5f);
                // мягкий переход обводка → тело (1 пиксель)
                float fill = Mathf.Clamp01((-sd - line) / aa + 0.5f);
                float g = Mathf.Lerp(0.1f, 0.3f + 0.7f * Mathf.Clamp01(lit), fill);
                px[y * N + x] = new Color(a, g, hl * a * fill, 1f);
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            _toonTex[kind] = tex;
            return tex;
        }

        private static float Ellipse(float u, float v, float cx, float cy, float rx, float ry, float rotDeg)
        {
            float a = rotDeg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float du = u - cx, dv = v - cy;
            float pu = (du * c + dv * s) / rx, pv = (-du * s + dv * c) / ry;
            return Mathf.Sqrt(pu * pu + pv * pv) - 1f;     // <0 внутри
        }

        private static void ToonShape(int kind, float u, float v, out float sd, out float line, out float hl, out float lit)
        {
            if (kind == 1)
            {
                // облачко: свет сверху-слева, снизу холодная тень, у каждого кружка своя «шапка» света
                lit = Mathf.Clamp01(0.62f + v * 0.55f - u * 0.12f);
                // облачко пены: несколько кружков
                sd = 9f;
                sd = Mathf.Min(sd, Mathf.Sqrt(u * u + (v + 0.12f) * (v + 0.12f)) - 0.5f);
                sd = Mathf.Min(sd, Mathf.Sqrt((u + 0.42f) * (u + 0.42f) + (v + 0.02f) * (v + 0.02f)) - 0.36f);
                sd = Mathf.Min(sd, Mathf.Sqrt((u - 0.44f) * (u - 0.44f) + (v + 0.06f) * (v + 0.06f)) - 0.38f);
                sd = Mathf.Min(sd, Mathf.Sqrt((u - 0.12f) * (u - 0.12f) + (v - 0.34f) * (v - 0.34f)) - 0.4f);
                sd = Mathf.Min(sd, Mathf.Sqrt((u + 0.2f) * (u + 0.2f) + (v - 0.3f) * (v - 0.3f)) - 0.3f);
                line = 0.075f;
                hl = Ellipse(u, v, -0.05f, 0.5f, 0.17f, 0.08f, 15f) < 0f ? 1f : 0f;
                if (Ellipse(u, v, -0.5f, 0.18f, 0.09f, 0.05f, 30f) < 0f) hl = 1f;
                return;
            }
            if (kind == 2)
            {
                lit = 1f;
                // клякса: неровное пятно + капли вокруг
                float ang = Mathf.Atan2(v, u), r = Mathf.Sqrt(u * u + v * v);
                float edge = 0.56f * (1f + 0.13f * Mathf.Sin(ang * 5f + 0.7f) + 0.08f * Mathf.Sin(ang * 9f + 2.1f) + 0.05f * Mathf.Sin(ang * 13f));
                sd = r - edge;
                float[] sat = { 0.3f, 0.12f, 0.84f, 1.6f, 0.08f, 0.8f, 2.7f, 0.1f, 0.86f, 3.9f, 0.065f, 0.78f, 5.1f, 0.09f, 0.88f };
                for (int k = 0; k < sat.Length; k += 3)
                {
                    float cx = Mathf.Cos(sat[k]) * sat[k + 2], cy = Mathf.Sin(sat[k]) * sat[k + 2];
                    sd = Mathf.Min(sd, Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy)) - sat[k + 1]);
                }
                line = 0.055f;
                hl = Ellipse(u, v, -0.2f, 0.22f, 0.2f, 0.07f, 25f) < 0f ? 1f : 0f;
                if (Ellipse(u, v, 0.18f, 0.32f, 0.05f, 0.035f, 0f) < 0f) hl = 1f;
                return;
            }
            // капля (v5): глянцевый шарик — объём по нормали сферы, блик-полоса, отражённый свет снизу
            float rr = Mathf.Sqrt(u * u + v * v);
            sd = rr - 0.9f;
            line = 0.06f;
            float nx = u / 0.9f, ny = v / 0.9f, nz = Mathf.Sqrt(Mathf.Max(0f, 1f - nx * nx - ny * ny));
            float lam = nx * -0.45f + ny * 0.55f + nz * 0.70f;
            float bounce = Mathf.Clamp01(-ny - 0.55f) * 0.5f * nz;                 // светлая кромка снизу (рефлекс)
            lit = Mathf.Clamp01(lam * 0.62f + 0.45f + bounce);
            // блик: изогнутая полоса сверху-слева + точка
            hl = Ellipse(u, v, -0.3f, 0.32f, 0.3f, 0.11f, 42f) < 0f ? 1f : 0f;
            if (Mathf.Sqrt((u - 0.2f) * (u - 0.2f) + (v - 0.5f) * (v - 0.5f)) < 0.075f) hl = 1f;
            if (Ellipse(u, v, 0.34f, -0.48f, 0.16f, 0.05f, -38f) < 0f) hl = 0.55f;   // слабый блик снизу
        }

        /// <summary>Материал под «CatCooks/Toon Drop»; null — шейдера нет (старый вид).</summary>
        private static Material MakeToonMat(string name, Texture tex, bool onFloor)
        {
            Shader sh = Shader.Find("CatCooks/Toon Drop");
            if (sh == null) return null;
            var m = new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
            m.mainTexture = tex;
            if (onFloor) { m.SetFloat("_OffsetFactor", -2f); m.SetFloat("_OffsetUnits", -2f); }
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + (onFloor ? -5 : 1);
            return m;
        }

        private static Material PuffMaterial
        {
            get
            {
                if (_puffMat != null) return _puffMat;
                _puffMat = MakeToonMat("CC Liquid Puffs (toon)", ToonMask(1), false);
                if (_puffMat == null) _puffMat = MakeParticleMat("CC Liquid Puffs (auto)", DropTexture, false);
                return _puffMat;
            }
        }

        private static Material SplatMaterial
        {
            get
            {
                if (_splatMat != null) return _splatMat;
                _splatMat = MakeToonMat("CC Liquid Splats (toon)", ToonMask(2), true);
                if (_splatMat == null) _splatMat = MakeParticleMat("CC Liquid Splats (auto)", DropTexture, false);
                return _splatMat;
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

        // ───────────────────────── v10.4: ПРОГРЕВ ШЕЙДЕРОВ ─────────────────────────
        // Без прогрева шейдер компилируется в момент первого пролива: в редакторе — голубая
        // заглушка на месте молока, в сборке — рывок кадра. Сразу после загрузки сцены рисуем
        // все материалы жидкости один раз крошечным невидимым объектом перед камерой.

        /// <summary>Прогреть сразу после загрузки сцены (можно выключить: LiquidFx.AutoWarmup = false).</summary>
        public static bool AutoWarmup = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoWarmupOnLoad()
        {
            if (!AutoWarmup) return;
            var go = new GameObject("CC Liquid Warmup (auto)") { hideFlags = HideFlags.DontSave };
            go.AddComponent<WarmupRunner>();
        }

        private sealed class WarmupRunner : MonoBehaviour
        {
            private readonly System.Collections.Generic.List<GameObject> _tmp = new System.Collections.Generic.List<GameObject>();
            private int _frames;

            private void Start()
            {
                var mats = new System.Collections.Generic.List<Material>();
                if (EnsurePool())
                {
                    mats.Add(DropMaterial); mats.Add(FlashMaterial); mats.Add(BubbleMaterial);
                    mats.Add(PuffMaterial); mats.Add(SplatMaterial);
                    if (_poofMat != null) mats.Add(_poofMat);
                }
                LiquidImpactFx.CollectMaterials(mats);
                Material pud = LiquidPuddle.WarmupMaterial; if (pud != null) mats.Add(pud);
                Material str = LiquidStreamRibbon.WarmupMaterial; if (str != null) mats.Add(str);
                Camera cam = Camera.main;
                if (cam == null) { Destroy(gameObject); return; }
                Mesh quad = BuildWarmQuad();
                foreach (Material m in mats)
                {
                    if (m == null) continue;
                    var q = new GameObject("warm " + m.name) { hideFlags = HideFlags.DontSave };
                    q.transform.SetParent(cam.transform, false);
                    q.transform.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.5f);
                    q.transform.localScale = Vector3.one * 0.0005f;                // меньше пикселя — не видно
                    q.AddComponent<MeshFilter>().sharedMesh = quad;
                    var r = q.AddComponent<MeshRenderer>();
                    r.sharedMaterial = m;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _tmp.Add(q);
                }
            }

            private void Update()
            {
                // 3 кадра рисуем (шейдеры компилируются), потом убираем
                if (++_frames < 4) return;
                foreach (GameObject g in _tmp) if (g != null) Destroy(g);
                Destroy(gameObject);
            }
        }

        private static Mesh BuildWarmQuad()
        {
            var m = new Mesh { name = "CC Warm Quad", hideFlags = HideFlags.DontSave };
            m.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.SetUVs(1, new System.Collections.Generic.List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero });
            m.SetUVs(2, new System.Collections.Generic.List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero });
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateBounds();
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
            ParticleSystem ps = CCRuntimeParticles.CreateStream(parent, DropLook(size, speed), DropMaterial);
            // v8: ИСПРАВЛЕНИЕ «огромных капель». Поток крепится к предмету (щуп, струя), а режим
            // Hierarchy умножал размер капель на масштаб модели предмета (FBX ×100 и т.п.).
            // Теперь масштаб родителя на размер капель не влияет.
            if (ps != null)
            {
                ParticleSystem.MainModule m = ps.main;
                m.scalingMode = ParticleSystemScalingMode.Shape;
                ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null) r.maxParticleSize = 0.09f;
            }
            return ps;
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
            // v4: капля «живая» — вылетает с пружинкой (раздулась-сжалась), потом тает
            ds.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.08f, 1.22f), new Keyframe(0.18f, 0.9f), new Keyframe(0.3f, 1.06f),
                new Keyframe(0.45f, 0.97f), new Keyframe(0.82f, 0.9f), new Keyframe(1f, 0f)));
            ParticleSystem.RotationOverLifetimeModule dr = _drops.rotationOverLifetime;
            dr.enabled = true;
            dr.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            ParticleSystem.LimitVelocityOverLifetimeModule lv = _drops.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.drag = 0.25f;
            lv.multiplyDragByParticleSize = false;
            lv.limit = 100f;
            // Капля ударилась — маленькая вспышка.
            ParticleSystem.SubEmittersModule sub = _drops.subEmitters;
            sub.enabled = true;
            _hits.transform.SetParent(_drops.transform, false);
            sub.AddSubEmitter(_hits, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritColor, 0.35f);

            // v4: капля упала на пол — мультяшная клякса лежит пару секунд и тает.
            _splats = MakeSystem(root.transform, "DropSplats", SplatMaterial, false, 0f, 600);
            ParticleSystemRenderer sr = _splats.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ParticleSystem.MainModule sm = _splats.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.075f);
            sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sm.startSpeed = 0f;
            sm.loop = false;
            sm.playOnAwake = false;
            ParticleSystem.SizeOverLifetimeModule ss = _splats.sizeOverLifetime;
            ss.enabled = true;
            ss.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.2f), new Keyframe(0.05f, 1.15f), new Keyframe(0.12f, 0.95f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.85f)));
            ParticleSystem.ColorOverLifetimeModule sc = _splats.colorOverLifetime;
            sc.enabled = true;
            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            sc.color = sg;
            ParticleSystem.EmissionModule se = _splats.emission;
            se.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            _splats.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _splats.transform.SetParent(_drops.transform, false);
            sub.AddSubEmitter(_splats, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritColor, 0.55f);

            // v4: клубы пены — облачка из кружков, вспухают и тают (взбивание).
            _puffs = MakeSystem(root.transform, "Puffs", PuffMaterial, false, -0.05f, 1200);
            ParticleSystem.SizeOverLifetimeModule ps = _puffs.sizeOverLifetime;
            ps.enabled = true;
            ps.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.3f), new Keyframe(0.15f, 1.1f), new Keyframe(0.3f, 0.95f), new Keyframe(0.75f, 1.05f), new Keyframe(1f, 0.2f)));
            ParticleSystem.RotationOverLifetimeModule pr = _puffs.rotationOverLifetime;
            pr.enabled = true;
            pr.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            ParticleSystem.ColorOverLifetimeModule pcol = _puffs.colorOverLifetime;
            pcol.enabled = true;
            var pg2 = new Gradient();
            pg2.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            pcol.color = pg2;
            ParticleSystem.LimitVelocityOverLifetimeModule plv = _puffs.limitVelocityOverLifetime;
            plv.enabled = true;
            plv.drag = 4f;
            plv.multiplyDragByParticleSize = false;      // v5: мелкие тучки тормозят так же, как крупные — «пыхнули» и встали
            plv.limit = 100f;

            // v6: объёмные пуфы-комки (мультяшная пыль/пена)
            _poofs = MakePoofSystem(root.transform);

            // v9: «рикошет» — капли-хвостики, вылетающие с зубцов коронки (вытянуты по скорости)
            _jets = MakeSystem(root.transform, "Jets", DropMaterial, true, 1f, 800);
            ParticleSystemRenderer jr = _jets.GetComponent<ParticleSystemRenderer>();
            jr.lengthScale = 1.6f;
            jr.velocityScale = 0.055f;
            ParticleSystem.SizeOverLifetimeModule js = _jets.sizeOverLifetime;
            js.enabled = true;
            js.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(0.15f, 1.1f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0f)));
            ParticleSystem.CollisionModule jc = _jets.collision;
            jc.enabled = true;
            jc.type = ParticleSystemCollisionType.World;
            jc.mode = ParticleSystemCollisionMode.Collision3D;
            jc.quality = ParticleSystemCollisionQuality.Low;
            jc.dampen = 0.9f; jc.bounce = 0.05f; jc.lifetimeLoss = 1f; jc.radiusScale = 0.3f;
            jc.maxCollisionShapes = 64;                                         // v10.4: дешевле

            _root = root.transform;
            EnsureBubbles();
            return true;
        }

        // ───────────────────────── v6: ПУФЫ ─────────────────────────

        private static ParticleSystem _poofs, _jets;

        /// <summary>
        /// v9: капля-«хвостик» (рикошет с коронки, отскок струи): вытянута по скорости,
        /// как на картинках с молоком. Нет системы — обычная капля.
        /// </summary>
        public static void Jet(Vector3 at, Vector3 velocity, float size, Color color, float life = 0.45f)
        {
            if (!EnsurePool()) return;
            if (_jets == null) { Drop(at, velocity, size, color, life); return; }
            var p = new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startSize = size, startLifetime = life,
                startColor = Bright(color, 0.12f), applyShapeToPosition = false,
            };
            _jets.Emit(p, 1);
        }
        private static Mesh _poofMesh;
        private static Material _poofMat;

        private static Mesh[] _poofMeshes;

        /// <summary>Первый комок (совместимость).</summary>
        public static Mesh PoofMesh { get { return PoofMeshes[0]; } }

        /// <summary>
        /// v8: тучки РАЗНОЙ формы — каждая собрана из 3–6 шаров (как облачко из шариков):
        /// круглая, приплюснутая, вытянутая, «гриб», кочка. Диаметр ≈ 1.
        /// </summary>
        public static Mesh[] PoofMeshes
        {
            get
            {
                if (_poofMeshes != null && _poofMeshes.Length > 0 && _poofMeshes[0] != null) return _poofMeshes;
                // v9: 8 видов, кривее — 4–9 шаров разного размера + мелкие бугорки (детали)
                _poofMeshes = new Mesh[8];
                var rnd = new System.Random(4242);
                for (int k = 0; k < _poofMeshes.Length; k++)
                {
                    int big = 3 + rnd.Next(4);
                    int small = 3 + rnd.Next(5);
                    int balls = big + small;
                    var cs = new Vector3[balls];
                    var rs = new float[balls];
                    Vector3 squash = new Vector3(Mathf.Lerp(0.85f, 1.3f, (float)rnd.NextDouble()),
                                                 Mathf.Lerp(0.65f, 1.15f, (float)rnd.NextDouble()),
                                                 Mathf.Lerp(0.8f, 1.2f, (float)rnd.NextDouble()));
                    for (int b2 = 0; b2 < balls; b2++)
                    {
                        bool isSmall = b2 >= big;
                        float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                        float el = ((float)rnd.NextDouble() - 0.3f) * 1.4f;
                        float dist = b2 == 0 ? 0f : isSmall ? Mathf.Lerp(0.3f, 0.42f, (float)rnd.NextDouble())
                                                            : Mathf.Lerp(0.16f, 0.36f, (float)rnd.NextDouble());
                        cs[b2] = new Vector3(Mathf.Cos(ang) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(ang) * Mathf.Cos(el)) * dist;
                        rs[b2] = b2 == 0 ? Mathf.Lerp(0.34f, 0.42f, (float)rnd.NextDouble())
                               : isSmall ? Mathf.Lerp(0.07f, 0.14f, (float)rnd.NextDouble())
                                         : Mathf.Lerp(0.18f, 0.32f, (float)rnd.NextDouble());
                    }
                    _poofMeshes[k] = BuildBallCluster(cs, rs, squash, k);
                }
                return _poofMeshes;
            }
        }

        /// <summary>Сетка-шар, у которой радиус по направлению = дальний край объединения шаров.</summary>
        private static Mesh BuildBallCluster(Vector3[] cs, float[] rs, Vector3 squash, int id)
        {
            const int lat = 16, lon = 24;                                   // v9: гуще — мелкие бугорки видны
            var v = new Vector3[(lat + 1) * (lon + 1)];
            float maxR = 0.01f;
            for (int y = 0; y <= lat; y++)
            for (int x = 0; x <= lon; x++)
            {
                float th = y / (float)lat * Mathf.PI, ph = x / (float)lon * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                float best = 0.1f;
                for (int i = 0; i < cs.Length; i++)
                {
                    float along = Vector3.Dot(d, cs[i]);
                    float perp2 = cs[i].sqrMagnitude - along * along;
                    if (perp2 >= rs[i] * rs[i]) continue;
                    float t = along + Mathf.Sqrt(rs[i] * rs[i] - perp2);
                    if (t > best) best = t;
                }
                Vector3 p = Vector3.Scale(d * best, squash);
                v[y * (lon + 1) + x] = p;
                maxR = Mathf.Max(maxR, p.magnitude);
            }
            for (int i = 0; i < v.Length; i++) v[i] *= 0.5f / maxR;                // диаметр ≈ 1
            var t2 = new int[lat * lon * 6];
            int q = 0;
            for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int a = y * (lon + 1) + x, b = a + lon + 1;
                t2[q++] = a; t2[q++] = a + 1; t2[q++] = b;
                t2[q++] = a + 1; t2[q++] = b + 1; t2[q++] = b;
            }
            var m = new Mesh { name = "CC Poof " + id + " (auto)", hideFlags = HideFlags.DontSave };
            m.vertices = v;
            m.triangles = t2;
            m.RecalculateNormals();
            Vector3[] n = m.normals;
            for (int y = 0; y <= lat; y++)
            {
                int a = y * (lon + 1), b = a + lon;
                Vector3 mm = (n[a] + n[b]).normalized;
                n[a] = mm; n[b] = mm;
            }
            // полюса: одна нормаль на всё кольцо (без «звёздочки»)
            Vector3 top = Vector3.zero, bot = Vector3.zero;
            for (int x = 0; x <= lon; x++) { top += n[x]; bot += n[lat * (lon + 1) + x]; }
            top.Normalize(); bot.Normalize();
            for (int x = 0; x <= lon; x++) { n[x] = top; n[lat * (lon + 1) + x] = bot; }
            m.normals = n;
            m.RecalculateBounds();
            return m;
        }

        private static ParticleSystem MakePoofSystem(Transform parent)
        {
            Shader sh = Shader.Find("CatCooks/Toon Poof");
            if (sh == null) return null;                               // нет шейдера — пуфы идут старыми тучками
            _poofMat = new Material(sh) { name = "CC Poofs (toon)", hideFlags = HideFlags.DontSave };
            ParticleSystem ps = MakeSystem(parent, "Poofs", _poofMat, false, -0.08f, 1500);
            SetupPoofLook(ps);
            return ps;
        }

        /// <summary>Общий вид пуфов: меши-тучки разной формы, вспух → сжался, торможение, вращение.</summary>
        private static void SetupPoofLook(ParticleSystem ps)
        {
            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            Mesh[] ms = PoofMeshes;
            r.SetMeshes(ms, ms.Length);
            r.alignment = ParticleSystemRenderSpace.World;
            r.enableGPUInstancing = false;
            ParticleSystem.MainModule m = ps.main;
            m.startRotation3D = true;
            // вспух → держится → сжался в точку (без прозрачности, как в мультике)
            ParticleSystem.SizeOverLifetimeModule so = ps.sizeOverLifetime;
            so.enabled = true;
            so.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.25f), new Keyframe(0.1f, 1.15f), new Keyframe(0.22f, 0.98f),
                new Keyframe(0.6f, 0.82f), new Keyframe(1f, 0f)));
            ParticleSystem.RotationOverLifetimeModule ro = ps.rotationOverLifetime;
            ro.enabled = true;
            ro.separateAxes = true;
            ro.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
            ro.y = new ParticleSystem.MinMaxCurve(-2f, 2f);
            ro.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.drag = 5f;                         // пыхнул — и сразу встал
            lv.multiplyDragByParticleSize = false;
            lv.limit = 100f;
        }

        /// <summary>
        /// v8: «туча взбивания» — своя система пуфов в ЛОКАЛЬНЫХ координатах: её объект
        /// ставят в точку щупа каждый кадр, и вся туча едет за щупом. С шумом (клубится).
        /// null — нет шейдера Toon Poof.
        /// </summary>
        public static ParticleSystem CreateWhipCloud(string name)
        {
            if (!EnsurePool() || _poofMat == null) return null;
            GameObject holder = new GameObject(name) { hideFlags = HideFlags.DontSave };
            ParticleSystem ps = MakeSystem(holder.transform, "Cloud", _poofMat, false, -0.05f, 700);
            ps.transform.localPosition = Vector3.zero;
            SetupPoofLook(ps);
            ParticleSystem.MainModule m = ps.main;
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            m.scalingMode = ParticleSystemScalingMode.Shape;
            ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
            lv.drag = 3.5f;
            ParticleSystem.NoiseModule nz = ps.noise;
            nz.enabled = true;
            nz.strength = 0.12f;
            nz.frequency = 1.6f;
            nz.scrollSpeed = 1.2f;
            nz.octaveCount = 2;
            nz.quality = ParticleSystemNoiseQuality.Medium;
            nz.sizeAmount = 0.25f;                // тучки «дышат» размером
            return ps;
        }

        private static ParticleSystem _into;

        /// <summary>Пуф в свою систему (туча взбивания). Точка и скорость — в мире.</summary>
        public static void PoofInto(ParticleSystem ps, Vector3 at, Vector3 velocity, float size, Color color, float life)
        {
            if (ps == null) { Poof(at, velocity, size, color, life); return; }
            Transform t = ps.transform;
            var p = new ParticleSystem.EmitParams
            {
                position = t.InverseTransformPoint(at), velocity = t.InverseTransformDirection(velocity),
                startSize = size, startLifetime = life, startColor = (Color32)color, applyShapeToPosition = false,
                rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f),
            };
            ps.Emit(p, 1);
        }

        /// <summary>
        /// Один объёмный пуф (комок пены): вспухает, тормозит, сжимается в точку.
        /// Нет шейдера Toon Poof — старая плоская тучка.
        /// </summary>
        public static void Poof(Vector3 at, Vector3 velocity, float size, Color color, float life = 0.45f)
        {
            if (!EnsurePool()) return;
            if (_into != null) { ParticleSystem tgt = _into; _into = null; PoofInto(tgt, at, velocity, size, color, life); _into = tgt; return; }
            if (_poofs == null) { Puff(at, velocity, size * 1.4f, color, life); return; }
            var p = new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startSize = size, startLifetime = life,
                startColor = (Color32)color, applyShapeToPosition = false,
                rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f),
            };
            _poofs.Emit(p, 1);
        }

        /// <summary>Видов всплеска у WhipBurst.</summary>
        public const int WhipBurstKinds = 5;

        /// <summary>
        /// «Взрывчик» в точке взбивания. kind: 0 — клубок (плотный пуф), 1 — взрывчик
        /// (яркое ядро, комки во все стороны, капли и искры), 2 — кольцо по поверхности,
        /// 3 — столбик вверх, 4 — россыпь мелких комков. scale — размер (1 — ведро).
        /// </summary>
        public static void WhipBurst(Vector3 at, Vector3 up, Color color, float scale, Vector3 carrier, int kind,
                                     float sizeK = 1f, float speedK = 1f, ParticleSystem into = null)
        {
            if (!EnsurePool()) return;
            _into = into;
            try { WhipBurstCore(at, up, color, scale, carrier, kind, sizeK, speedK); }
            finally { _into = null; }
        }

        private static void WhipBurstCore(Vector3 at, Vector3 up, Color color, float scale, Vector3 carrier, int kind,
                                          float sizeK, float speedK)
        {
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();
            Vector3 t1 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(up, t1);
            Color foam = Color.Lerp(color, Color.white, 0.5f);
            Color core = Color.Lerp(color, Color.white, 0.85f);
            float S = Mathf.Max(0.05f, scale);
            float Z = S * Mathf.Max(0.1f, sizeK);          // размер комков
            float V = S * Mathf.Max(0.05f, speedK);        // скорость разлёта (крупные комки — не быстрее)
            switch (kind)
            {
                case 0:     // клубок: плотный шар комков, чуть вверх
                {
                    int n = Random.Range(9, 15);
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 d = Random.onUnitSphere;
                        if (Vector3.Dot(d, up) < 0f) d = Vector3.Reflect(d, up);
                        Poof(at + d * 0.014f * Z, d * Random.Range(0.12f, 0.45f) * V + up * 0.18f * V + carrier,
                             Random.Range(0.024f, 0.05f) * Z, Shade(foam), Random.Range(0.35f, 0.55f));
                    }
                    break;
                }
                case 1:     // взрывчик: ядро + комки во все стороны + капли + искры
                {
                    Poof(at + up * 0.01f * Z, up * 0.12f * V + carrier, 0.075f * Z, core, 0.28f);
                    int n = Random.Range(12, 19);
                    for (int i = 0; i < n; i++)
                    {
                        float a = Random.value * Mathf.PI * 2f, el = Random.Range(8f, 65f) * Mathf.Deg2Rad;
                        Vector3 d = (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * Mathf.Cos(el) + up * Mathf.Sin(el);
                        Poof(at + d * 0.02f * Z, d * Random.Range(0.6f, 1.4f) * V + carrier,
                             Random.Range(0.016f, 0.036f) * Z, Shade(foam), Random.Range(0.3f, 0.55f));
                    }
                    int drops = Random.Range(2, 5);
                    for (int i = 0; i < drops; i++)
                    {
                        Vector3 d = (Random.onUnitSphere * 0.5f + up).normalized;
                        Drop(at + up * 0.01f * Z, d * Random.Range(1.1f, 1.9f) * Mathf.Sqrt(V) + carrier,
                             Random.Range(0.014f, 0.026f) * S, color, Random.Range(0.5f, 0.8f));
                    }
                    int sparks = Random.Range(5, 9);
                    for (int i = 0; i < sparks; i++)
                    {
                        Vector3 d = (Random.onUnitSphere + up * 0.6f).normalized;
                        Poof(at, d * Random.Range(1.4f, 2.2f) * V + carrier, Random.Range(0.006f, 0.011f) * Z,
                             core, Random.Range(0.22f, 0.38f));
                    }
                    break;
                }
                case 2:     // кольцо по поверхности (как пыль от шага)
                {
                    int n = Random.Range(10, 15);
                    float a0 = Random.value * Mathf.PI * 2f;
                    for (int i = 0; i < n; i++)
                    {
                        float a = a0 + i / (float)n * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
                        Vector3 h = t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a);
                        Poof(at + h * 0.015f * Z, h * Random.Range(0.5f, 0.9f) * V + up * Random.Range(0.04f, 0.14f) * V + carrier,
                             Random.Range(0.02f, 0.034f) * Z, Shade(foam), Random.Range(0.35f, 0.5f));
                    }
                    break;
                }
                case 3:     // столбик: комки вверх, к верху мельче
                {
                    int n = Random.Range(5, 9);
                    Vector3 lean = (t1 * Random.Range(-1f, 1f) + t2 * Random.Range(-1f, 1f)) * 0.15f;
                    for (int i = 0; i < n; i++)
                    {
                        float k = i / (float)Mathf.Max(1, n - 1);
                        Poof(at + up * (i * 0.01f * Z), (up + lean) * Random.Range(0.55f, 1.05f) * V * (0.6f + k * 0.6f) + carrier,
                             Mathf.Lerp(0.045f, 0.014f, k) * Z, Shade(foam), Random.Range(0.35f, 0.6f));
                    }
                    break;
                }
                default:    // россыпь: много мелких комков вокруг
                {
                    int n = Random.Range(10, 16);
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 d = Random.onUnitSphere;
                        if (Vector3.Dot(d, up) < -0.2f) d = Vector3.Reflect(d, up);
                        Poof(at + d * 0.03f * Z, d * Random.Range(0.3f, 0.9f) * V + carrier,
                             Random.Range(0.01f, 0.02f) * Z, Shade(foam), Random.Range(0.25f, 0.45f));
                    }
                    break;
                }
            }
        }

        /// <summary>Чуть разный тон у комков — пена не «пластиковая».</summary>
        private static Color Shade(Color c)
        {
            float k = Random.Range(-0.04f, 0.05f);
            return new Color(Mathf.Clamp01(c.r + k), Mathf.Clamp01(c.g + k), Mathf.Clamp01(c.b + k * 0.6f), 1f);
        }

        private static ParticleSystem _splats, _puffs;

        /// <summary>Клякса на полу (капля упала). Только вид, тает за несколько секунд.</summary>
        public static void Splat(Vector3 at, float size, Color color)
        {
            if (!EnsurePool() || _splats == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = at + Vector3.up * 0.002f, startSize = size, startColor = Bright(color, 0.05f),
                rotation = Random.value * 360f, applyShapeToPosition = false,
            };
            _splats.Emit(p, 1);
        }

        /// <summary>
        /// Клуб пены (взбивание): облачко вспухает, чуть всплывает и тает.
        /// velocity — начальная скорость (тормозит сама), size — диаметр, м.
        /// </summary>
        public static void Puff(Vector3 at, Vector3 velocity, float size, Color color, float life = 0.8f)
        {
            if (!EnsurePool() || _puffs == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startSize = size, startLifetime = life,
                startColor = Bright(color, 0.3f), rotation = Random.value * 360f, applyShapeToPosition = false,
            };
            _puffs.Emit(p, 1);
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
                // v5: капля почти круглая — лёгкая вытяжка по скорости, без «спиц»
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 1.0f;
                r.velocityScale = 0.01f;
            }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            // v5: у самой камеры частица не раздувается на пол-экрана
            r.maxParticleSize = 0.09f;
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
            int n = Mathf.RoundToInt(Mathf.Lerp(3f, 11f, power));
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
                    startSize = Random.Range(0.013f, 0.03f) * (0.75f + power * 0.5f),
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
                    startSize = Random.Range(0.03f, 0.05f), startLifetime = Random.Range(0.5f, 0.9f),
                    startColor = Bright(color, 0.1f), applyShapeToPosition = false,
                };
                _drops.Emit(p, 1);
            }
            // Мелкая пыль брызг — быстрая, крошечная.
            for (int i = 0; i < Mathf.RoundToInt(1f + power * 3f); i++)
            {
                Vector3 dir = (Quaternion.AngleAxis((Random.value - 0.5f) * 160f, up) * outward + up * Random.Range(0.5f, 1.5f)).normalized;
                var p = new ParticleSystem.EmitParams
                {
                    position = at, velocity = dir * Random.Range(1.2f, 2.8f) * (0.5f + power) + carry,
                    startSize = Random.Range(0.005f, 0.009f), startLifetime = Random.Range(0.2f, 0.45f),
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
            if (Random.value < 0.15f)
            {
                Vector3 side = Vector3.ProjectOnPlane(Random.onUnitSphere, up) * 0.35f;
                Drop(at + up * size * 0.4f, (up + side).normalized * Random.Range(0.4f, 1.1f) + carrierVelocity,
                     Random.Range(0.007f, 0.013f), color, Random.Range(0.3f, 0.5f));
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

            int n = Mathf.RoundToInt(Mathf.Lerp(3f, 10f, power));
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
                    startSize = Random.Range(0.011f, 0.026f) * sizeK * (0.75f + power * 0.45f),
                    startLifetime = Random.Range(0.3f, 0.65f),
                    startColor = Bright(color, Random.Range(0.1f, 0.3f)),
                    applyShapeToPosition = false,
                };
                _drops.Emit(p, 1);
            }
        }
    }
}
