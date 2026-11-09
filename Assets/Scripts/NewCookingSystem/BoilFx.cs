using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КРАСИВОЕ КИПЕНИЕ — только вид, у каждого игрока свой.
    ///
    /// Три системы частиц, собираются кодом (ничего настраивать не надо):
    ///   • Пузыри на поверхности: вздуваются куполком и лопаются — на месте
    ///     пузыря расходится кольцо по воде, иногда подпрыгивают капельки.
    ///   • Пар: мягкие белые клубы, медленно поднимаются, расплываются и тают,
    ///     закручиваются (шум), а не летят кубиками.
    ///   • Дым сухого дна — тем же паром, только серым и гуще.
    ///
    /// Создаёт CookwareHeat (BoilFx.Create). Текстуры — процедурные, общие на всех.
    /// </summary>
    public sealed class BoilFx : MonoBehaviour
    {
        private ParticleSystem _bubbles, _rings, _drops, _steam;

        private static Material _matSteam, _matBubble, _matRing, _matDrop;

        /// <summary>Общий на объект (кастрюлю): найдёт уже созданный или создаст.</summary>
        public static BoilFx For(Transform owner)
        {
            BoilFx fx = owner.GetComponentInChildren<BoilFx>(true);
            return fx != null ? fx : Create(owner);
        }

        public static BoilFx Create(Transform owner)
        {
            var go = new GameObject("BoilFx (auto)") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(owner, false);
            var fx = go.AddComponent<BoilFx>();      // Awake сам всё соберёт
            Debug.Log($"[Кипение] эффекты (пузыри, кольца, пар) созданы на «{owner.name}»", owner);
            return fx;
        }

        // ───────────────────────── ВЫЗОВЫ ─────────────────────────

        /// <summary>Пузырь на поверхности (мир). size — диаметр, м.</summary>
        public void Bubble(Vector3 at, float size, Color liquid)
        {
            // Мультяшно: тёмный контур (из текстуры) + белый блик, тон — чуть светлее воды.
            Color c = Color.Lerp(liquid, Color.white, 0.3f);
            c.a = 1f;
            if (_rings != null && Mathf.Abs(liquid.r - _ringTint.r) + Mathf.Abs(liquid.g - _ringTint.g) + Mathf.Abs(liquid.b - _ringTint.b) > 0.05f)
            {
                _ringTint = liquid;
                var rm = _rings.main;
                Color rc = Color.Lerp(liquid, Color.white, 0.15f); rc.a = 0.95f;
                rm.startColor = rc;
            }
            var p = new ParticleSystem.EmitParams
            {
                position = at + Vector3.up * size * 0.4f,
                velocity = Vector3.zero,
                startSize = size,
                startLifetime = Random.Range(0.25f, 0.6f),
                startColor = c,
                applyShapeToPosition = false,
            };
            _bubbles.Emit(p, 1);
        }

        private float _scale = 1f;
        private Color _ringTint = new Color(-1f, -1f, -1f, 0f);
        /// <summary>Во сколько раз кастрюля больше «обычной» (радиус 15 см).</summary>
        public float Scale => _scale;

        /// <summary>Подогнать кольца, брызги и пар под размер кастрюли.</summary>
        public void SetScale(float s)
        {
            s = Mathf.Max(0.2f, s);
            if (Mathf.Abs(s - _scale) < 0.05f * _scale || _rings == null) return;
            _scale = s;
            float q = Mathf.Sqrt(s);
            var rm = _rings.main;
            rm.startSize = new ParticleSystem.MinMaxCurve(0.035f * s, 0.07f * s);
            var dm = _drops.main;
            dm.startSize = new ParticleSystem.MinMaxCurve(0.006f * s, 0.013f * s);
            var dv = _drops.velocityOverLifetime;
            dv.x = new ParticleSystem.MinMaxCurve(-0.25f * q, 0.25f * q);
            dv.y = new ParticleSystem.MinMaxCurve(0.6f * q, 1.1f * q);
            dv.z = new ParticleSystem.MinMaxCurve(-0.25f * q, 0.25f * q);
            var nz = _steam.noise;
            nz.strength = 0.22f * q;
            var lv = _steam.limitVelocityOverLifetime;
            lv.limit = 0.9f * q;
        }

        /// <summary>Клуб пара (мир). alpha — плотность 0..1.</summary>
        public void Steam(Vector3 at, Vector3 vel, float size, float alpha, Color color)
        {
            color.a = Mathf.Clamp01(alpha);
            var p = new ParticleSystem.EmitParams
            {
                position = at,
                velocity = vel,
                startSize = size,
                startLifetime = Random.Range(2.2f, 3.6f),
                startColor = color,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false,
            };
            _steam.Emit(p, 1);
        }

        // ───────────────────────── БУРЛЯЩАЯ ПОВЕРХНОСТЬ ─────────────────────────
        //
        // Отдельный диск поверх воды: волны и вздутия считаются на процессоре, цвет —
        // по наклону к солнцу (гребни светлые, впадины темнее). Не зависит от шейдера
        // воды — видно на любой кастрюле.

        private const int Rings = 16, Segs = 48;
        private Mesh _surfMesh;
        private MeshRenderer _surfMr;
        private Transform _surfT;
        private Vector3[] _sv;
        private Color[] _sc;
        private Vector2[] _polar;           // (r01, угол)
        private float _surfStrength;
        private static Material _matSurf;

        /// <summary>
        /// Каждый кадр от CookwareHeat. strength 0..1 (0 — спрятать): 0.3 — «шумит», 1 — бурное кипение.
        /// center — центр поверхности воды (мир), radius — радиус у стенки, м.
        /// </summary>
        public void Surface(Vector3 center, float radius, float strength, Color liquid)
        {
            _surfStrength = Mathf.MoveTowards(_surfStrength, Mathf.Clamp01(strength), Time.deltaTime * 0.8f);
            if (_surfMesh == null) BuildSurface();
            bool on = _surfStrength > 0.02f && radius > 0.01f;
            if (_surfMr.enabled != on) _surfMr.enabled = on;
            if (!on) return;

            _surfT.SetPositionAndRotation(center + Vector3.up * 0.004f, Quaternion.identity);
            float t = Time.time;
            float amp = radius * 0.11f * _surfStrength;
            Vector3 sun = RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : new Vector3(0.3f, 0.85f, 0.4f).normalized;
            Color baseC = Color.Lerp(liquid, Color.white, 0.25f);

            for (int i = 0; i < _sv.Length; i++)
            {
                float r01 = _polar[i].x, a = _polar[i].y;
                float x = Mathf.Cos(a) * r01, z = Mathf.Sin(a) * r01;
                // Вздутия от пузырей (шум едет вверх) + бегущие волны + рябь.
                float bump = Mathf.PerlinNoise(x * 3.1f + 13.7f, z * 3.1f - t * 1.9f) - 0.5f;
                float bump2 = Mathf.PerlinNoise(x * 6.3f - t * 2.7f, z * 6.3f + 5.1f) - 0.5f;
                float wave = Mathf.Sin(x * 7f + t * 4.1f) * Mathf.Cos(z * 6f - t * 3.3f) * 0.35f;
                float edge = 1f - Mathf.Pow(r01, 4f);                     // у стенки — ровно
                float y = (bump * 1.4f + bump2 * 0.8f + wave) * amp * edge;
                _sv[i] = new Vector3(x * radius, y, z * radius);
            }
            _surfMesh.vertices = _sv;
            _surfMesh.RecalculateNormals();
            Vector3[] n = _surfMesh.normals;
            for (int i = 0; i < _sc.Length; i++)
            {
                float lit = Mathf.Clamp01(Vector3.Dot(n[i], sun));
                float crest = Mathf.Clamp01(_sv[i].y / Mathf.Max(1e-4f, amp) * 0.8f + 0.5f);
                // Мультяшные полосы света: тень / тон / светлый, и белые гребни-пена.
                float band = lit < 0.35f ? 0.55f : lit < 0.7f ? 0.85f : 1.05f;
                Color c = baseC * band;
                if (crest > 0.78f) c = Color.Lerp(c, Color.white, 0.85f);
                else if (crest > 0.62f) c = Color.Lerp(c, Color.white, 0.35f);
                c.a = Mathf.Lerp(0.5f, 0.95f, _surfStrength);
                _sc[i] = c;
            }
            _surfMesh.colors = _sc;
            _surfMesh.RecalculateBounds();
        }

        private void BuildSurface()
        {
            var go = new GameObject("BoilSurface") { hideFlags = HideFlags.DontSave };
            // НЕ дочерний: вершины в метрах мира, а у ковша экскаватора масштаб бывает ×100 —
            // дочерний диск раздувался на всю карту. Ставится в мир каждый кадр.
            _surfT = go.transform;
            _surfMesh = new Mesh { name = "Boil Surface (auto)", hideFlags = HideFlags.DontSave };
            _surfMesh.MarkDynamic();

            int nv = 1 + Rings * Segs;
            _sv = new Vector3[nv]; _sc = new Color[nv]; _polar = new Vector2[nv];
            var uv = new Vector2[nv];
            _polar[0] = Vector2.zero;
            for (int r = 1; r <= Rings; r++)
                for (int k = 0; k < Segs; k++)
                {
                    int i = 1 + (r - 1) * Segs + k;
                    _polar[i] = new Vector2(r / (float)Rings, k * Mathf.PI * 2f / Segs);
                }
            for (int i = 0; i < nv; i++) uv[i] = new Vector2(0.5f, 0.5f);

            var tris = new System.Collections.Generic.List<int>();
            for (int k = 0; k < Segs; k++) { tris.Add(0); tris.Add(1 + (k + 1) % Segs); tris.Add(1 + k); }
            for (int r = 1; r < Rings; r++)
                for (int k = 0; k < Segs; k++)
                {
                    int a = 1 + (r - 1) * Segs + k, b = 1 + (r - 1) * Segs + (k + 1) % Segs;
                    int c = 1 + r * Segs + k, d = 1 + r * Segs + (k + 1) % Segs;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            _surfMesh.vertices = _sv;
            _surfMesh.uv = uv;
            _surfMesh.colors = _sc;
            _surfMesh.SetTriangles(tris, 0);

            go.AddComponent<MeshFilter>().sharedMesh = _surfMesh;
            _surfMr = go.AddComponent<MeshRenderer>();
            _surfMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _surfMr.receiveShadows = false;
            if (_matSurf == null) _matSurf = MakeMat("CC Boil Surface (auto)", MakeTex(8, (u, v, r) => 1f));
            _surfMr.sharedMaterial = _matSurf;
            _surfMr.enabled = false;
        }

        private void OnDestroy()
        {
            if (_surfMesh != null) SafeDestroy.Kill(_surfMesh);
            if (_surfT != null) SafeDestroy.Kill(_surfT.gameObject);
        }

        private void OnDisable()
        {
            if (_surfMr != null) _surfMr.enabled = false;
            _surfStrength = 0f;
        }

        // ───────────────────────── СБОРКА ─────────────────────────

        private bool _built;

        // Собирается сам — даже если компонент повесили руками.
        private void Awake() { if (!_built) Build(); }

        private void Build()
        {
            _built = true;
            EnsureMaterials();

            // ── пар ──
            _steam = MakeSystem("Steam", transform, _matSteam, 160);
            {
                var sol = _steam.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.35f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.9f)));
                var col = _steam.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(new[] { 0f, 0.18f, 0.55f, 1f }, new[] { 0f, 1f, 0.6f, 0f });
                var rot = _steam.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
                var nz = _steam.noise;
                nz.enabled = true;
                nz.strength = 0.22f;
                nz.frequency = 0.7f;
                nz.scrollSpeed = 0.35f;
                nz.quality = ParticleSystemNoiseQuality.Low;
                var lv = _steam.limitVelocityOverLifetime;
                lv.enabled = true;
                lv.limit = 0.9f;
                lv.dampen = 0.08f;
                var r = _steam.GetComponent<ParticleSystemRenderer>();
                r.sortingFudge = 2f;
            }

            // ── пузыри ──
            _bubbles = MakeSystem("Bubbles", transform, _matBubble, 220);
            {
                var sol = _bubbles.sizeOverLifetime;
                sol.enabled = true;
                // Вздувается, чуть замирает и лопается.
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.15f), new Keyframe(0.7f, 1f), new Keyframe(0.9f, 1.08f), new Keyframe(1f, 0.6f)));
                var col = _bubbles.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(new[] { 0f, 0.15f, 0.85f, 1f }, new[] { 0f, 1f, 1f, 0f });
                var r = _bubbles.GetComponent<ParticleSystemRenderer>();
                r.sortingFudge = -1f;
            }

            // ── кольца на месте лопнувшего пузыря (суб-эмиттер) ──
            _rings = MakeSystem("Rings", _bubbles.transform, _matRing, 220);
            {
                var main = _rings.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.07f);
                main.startColor = new Color(1f, 1f, 1f, 0.55f);
                var em = _rings.emission;
                em.enabled = true;
                em.rateOverTime = 0f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                var sol = _rings.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 1.4f)));
                var col = _rings.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(new[] { 0f, 1f }, new[] { 1f, 0f });
                var r = _rings.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                r.sortingFudge = -2f;
            }

            // ── капельки (суб-эмиттер, не на каждом пузыре) ──
            _drops = MakeSystem("Drops", _bubbles.transform, _matDrop, 200);
            {
                var main = _drops.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.013f);
                main.startColor = new Color(1f, 1f, 1f, 0.9f);
                main.gravityModifier = 1.1f;
                var em = _drops.emission;
                em.enabled = true;
                em.rateOverTime = 0f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 4) });
                var vel = _drops.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
                vel.y = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
                var col = _drops.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(new[] { 0f, 0.7f, 1f }, new[] { 1f, 1f, 0f });
            }

            var sub = _bubbles.subEmitters;
            sub.enabled = true;
            sub.AddSubEmitter(_rings, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            sub.AddSubEmitter(_drops, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing, 0.25f);

            _steam.Play(); _bubbles.Play();
        }

        private static ParticleSystem MakeSystem(string name, Transform parent, Material mat, int max)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 5f;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 0.1f;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.minParticleSize = 0f;
            r.maxParticleSize = 2f;
            return ps;
        }

        private static ParticleSystem.MinMaxGradient Fade(float[] t, float[] a)
        {
            var g = new Gradient();
            var ak = new GradientAlphaKey[t.Length];
            for (int i = 0; i < t.Length; i++) ak[i] = new GradientAlphaKey(a[i], t[i]);
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, ak);
            return new ParticleSystem.MinMaxGradient(g);
        }

        // ───────────────────────── МАТЕРИАЛЫ / ТЕКСТУРЫ ─────────────────────────

        private static void EnsureMaterials()
        {
            if (_matSteam != null) return;
            _matSteam = MakeMat("CC Boil Steam (auto)", MakeTex(64, Puff));
            _matBubble = MakeMat("CC Boil Bubble (auto)", MakeTexC(64, BubbleTexC));
            _matRing = MakeMat("CC Boil Ring (auto)", MakeTexC(64, RingTexC));
            _matDrop = MakeMat("CC Boil Drop (auto)", MakeTex(32, DropTex));
        }

        private static Material MakeMat(string name, Texture tex)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1;
            return m;
        }

        private delegate float Alpha(float x, float y, float r);
        private delegate Color ColorF(float x, float y, float r);

        private static Texture2D MakeTexC(int n, ColorF f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true)
            { name = "CC Boil Tex (auto)", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    px[y * n + x] = f(u, v, Mathf.Sqrt(u * u + v * v));
                }
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>Пузырь-мультяшка: тёмный контур, светлая заливка, белый блик.</summary>
        private static Color BubbleTexC(float u, float v, float r)
        {
            if (r > 1f) return new Color(1f, 1f, 1f, 0f);
            float outline = Mathf.SmoothStep(0.78f, 0.86f, r) * (1f - Mathf.SmoothStep(0.95f, 1f, r));
            float fill = 1f - Mathf.SmoothStep(0.78f, 0.86f, r);
            float hx = u + 0.3f, hy = v - 0.32f;
            float hl = Mathf.Clamp01(1f - Mathf.SmoothStep(0.10f, 0.16f, Mathf.Sqrt(hx * hx + hy * hy)));
            Color c = new Color(1f, 1f, 1f, fill * 0.45f);                    // светлая заливка
            c = Color.Lerp(c, new Color(0.22f, 0.25f, 0.3f, 1f), outline);  // тёмный контур
            c = Color.Lerp(c, new Color(1f, 1f, 1f, 1f), hl);               // блик
            return c;
        }

        /// <summary>Кольцо по воде: тёмная полоса снаружи, белая внутри — видно на любой воде.</summary>
        private static Color RingTexC(float u, float v, float r)
        {
            float dark = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.06f, 2f));
            float white = Mathf.Exp(-Mathf.Pow((r - 0.72f) / 0.05f, 2f));
            Color c = new Color(0.25f, 0.3f, 0.35f, dark * 0.85f);
            c = Color.Lerp(c, new Color(1f, 1f, 1f, white * 0.95f), white / Mathf.Max(0.001f, white + dark));
            c.a = Mathf.Clamp01(dark * 0.85f + white * 0.95f);
            return c;
        }

        private static Texture2D MakeTex(int n, Alpha f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true)
            { name = "CC Boil Tex (auto)", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01(f(u, v, r));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>Мягкий клуб с рваным краем.</summary>
        private static float Puff(float u, float v, float r)
        {
            float n = Mathf.PerlinNoise(u * 2.3f + 7.1f, v * 2.3f + 3.3f) * 0.6f + Mathf.PerlinNoise(u * 5f + 1.7f, v * 5f + 9.2f) * 0.4f;
            float edge = 1f - Mathf.SmoothStep(0.25f, 1f, r + (n - 0.5f) * 0.45f);
            return edge * edge * (0.65f + 0.35f * n);
        }

        /// <summary>Пузырь: светлый ободок, прозрачная серёдка, блик сверху-слева.</summary>
        private static float BubbleTex(float u, float v, float r)
        {
            if (r > 1f) return 0f;
            float rim = Mathf.Exp(-Mathf.Pow((r - 0.8f) / 0.12f, 2f));
            float fill = 0.16f * (1f - Mathf.SmoothStep(0.75f, 0.95f, r));
            float hx = u + 0.32f, hy = v - 0.32f;
            float hl = Mathf.Exp(-(hx * hx + hy * hy) / 0.018f);
            return rim * 0.95f + fill + hl;
        }

        private static float RingTex(float u, float v, float r) =>
            Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.07f, 2f)) * 0.9f + Mathf.Exp(-Mathf.Pow((r - 0.6f) / 0.05f, 2f)) * 0.25f;

        private static float DropTex(float u, float v, float r) => 1f - Mathf.SmoothStep(0.45f, 1f, r);
    }
}
