using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v15: ОГОНЬ НА ПРЕДМЕТЕ («как в Zelda») — для любого горящего продукта.
    ///
    ///   • языки пламени — частицы-капли (шейдер CatCooks/Stylized Fire): колышутся, в них
    ///     прогрызаются дыры с тёмно-оранжевой каймой, кверху светлеют, рассыпаются;
    ///   • мягкий оранжевый ореол, искры-угольки вверх, тёмный дымок, марево, мерцающий свет.
    ///
    /// Сеть не нужна: FoodBurnable у каждого игрока сам создаёт CCFireFx по стадии горения.
    /// Размер — по габариту рендереров продукта (каждый кадр, продукт может двигаться).
    /// </summary>
    public sealed class CCFireFx : MonoBehaviour
    {
        private Renderer[] _rends;
        private float _power;
        private ParticleSystem _flames, _glow, _embers;
        private static Material _flameMat, _glowMat, _emberMat;
        private Light _light;
        private float _flameAcc, _glowAcc, _emberAcc, _smokeAcc;
        private float _seedT;

        public static CCFireFx Attach(Renderer[] rends)
        {
            if (Shader.Find("CatCooks/Stylized Fire") == null)
            {
                Debug.LogWarning("[Горение] нет шейдера «CatCooks/Stylized Fire» — огня не будет видно. Меню CatCooks → Огонь и жар → 2");
                return null;
            }
            var go = new GameObject("CC Fire (auto)") { hideFlags = HideFlags.DontSave };
            CCFireFx f = go.AddComponent<CCFireFx>();
            f._rends = rends;
            f.Build();
            return f;
        }

        /// <summary>0..1 — сила огня (разгорается, затухает).</summary>
        public float Power { get { return _power; } set { _power = Mathf.Clamp01(value); } }

        private bool BoundsOf(out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            if (_rends == null) return false;
            foreach (Renderer r in _rends)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        private void Update()
        {
            Bounds b;
            if (_power <= 0.001f || !BoundsOf(out b))
            {
                if (_light != null) _light.enabled = false;
                return;
            }
            float dt = Time.deltaTime;
            Vector3 c = b.center, e = b.extents;
            float size = Mathf.Max(0.04f, Mathf.Max(e.x, Mathf.Max(e.y, e.z)) * 2f);
            float k = _power;
            float sp = Mathf.Sqrt(size / 0.15f);

            // языки
            _flameAcc += dt * (10f + 14f * k) * Mathf.Clamp(size / 0.15f, 0.7f, 2.5f);
            var ep = new ParticleSystem.EmitParams();
            while (_flameAcc >= 1f)
            {
                _flameAcc -= 1f;
                Vector2 r = Random.insideUnitCircle * 0.65f;
                ep.position = new Vector3(c.x + r.x * e.x, Mathf.Lerp(b.min.y, c.y, Random.Range(0.15f, 0.9f)), c.z + r.y * e.z);
                ep.velocity = new Vector3(Random.Range(-0.04f, 0.04f), Random.Range(0.25f, 0.55f), Random.Range(-0.04f, 0.04f)) * sp;
                ep.startSize = size * Random.Range(0.65f, 1.15f) * Mathf.Lerp(0.5f, 1f, k);
                ep.startLifetime = Random.Range(0.45f, 0.85f);
                ep.rotation = Random.Range(-12f, 12f);
                ep.startColor = new Color(Random.value, Random.Range(0.6f, 1f), 0f, 1f);       // R — зерно, G — яркость
                _flames.Emit(ep, 1);
            }
            // ореол
            _glowAcc += dt * 6f * k;
            while (_glowAcc >= 1f)
            {
                _glowAcc -= 1f;
                ep.position = c + Vector3.up * size * 0.2f;
                ep.velocity = Vector3.up * 0.1f * sp;
                ep.startSize = size * Random.Range(1.8f, 2.4f);
                ep.startLifetime = Random.Range(0.35f, 0.55f);
                ep.rotation = Random.Range(0f, 360f);
                ep.startColor = new Color(1f, 0.45f, 0.1f, 0.22f * k);
                _glow.Emit(ep, 1);
            }
            // угольки
            _emberAcc += dt * 9f * k;
            while (_emberAcc >= 1f)
            {
                _emberAcc -= 1f;
                Vector2 r = Random.insideUnitCircle * 0.6f;
                ep.position = new Vector3(c.x + r.x * e.x, c.y + e.y * Random.Range(-0.2f, 0.8f), c.z + r.y * e.z);
                ep.velocity = (Vector3.up * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * 0.35f) * sp;
                ep.startSize = size * Random.Range(0.025f, 0.05f);
                ep.startLifetime = Random.Range(0.6f, 1.3f);
                ep.rotation = 0f;
                ep.startColor = new Color(1f, Random.Range(0.55f, 0.85f), 0.25f, 1f);
                _embers.Emit(ep, 1);
            }
            // дымок и марево сверху
            _smokeAcc += dt * 3.5f * k;
            if (_smokeAcc >= 1f)
            {
                _smokeAcc -= 1f;
                CCHeatFx.Wisp(new Vector3(c.x, b.max.y + size * 0.6f, c.z) + Random.insideUnitSphere * size * 0.15f,
                              Vector3.up * Random.Range(0.3f, 0.55f) * sp, size * Random.Range(0.7f, 1.1f),
                              new Color(0.32f, 0.3f, 0.29f, 0.35f), Random.Range(1.2f, 1.9f));
            }
            CCHeatFx.Haze(new Vector3(c.x, b.max.y, c.z), size * 1.6f, size * 3f, 0.8f * k);

            // свет мерцает
            if (_light == null) BuildLight();
            _light.enabled = true;
            _seedT += dt;
            float fl = 0.75f + 0.5f * Mathf.PerlinNoise(_seedT * 9f, 0.37f);
            _light.transform.position = c + Vector3.up * size * 0.5f;
            _light.intensity = 2.2f * k * fl;
            _light.range = Mathf.Clamp(size * 8f, 1.2f, 6f);
        }

        private void BuildLight()
        {
            var go = new GameObject("Fire Light") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.55f, 0.2f);
            _light.shadows = LightShadows.None;
        }

        private void Build()
        {
            if (_flameMat == null)
            {
                _flameMat = new Material(Shader.Find("CatCooks/Stylized Fire")) { name = "Stylized Fire (auto)", hideFlags = HideFlags.DontSave };
                _glowMat = AdditiveMat("Fire Glow (auto)", 3001);
                _emberMat = AdditiveMat("Fire Embers (auto)", 3002);
            }
            // языки: вертикальные билборды, «износ» A от 1 к 0 — это эрозия в шейдере
            _flames = MakePS("Fire Flames", _flameMat, ParticleSystemRenderMode.VerticalBillboard, 300, -0.05f);
            var col = _flames.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.75f, 0.45f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = _flames.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.85f)));
            var pr = _flames.GetComponent<ParticleSystemRenderer>();
            pr.pivot = new Vector3(0f, 0.35f, 0f);        // низ капли у точки рождения

            _glow = MakePS("Fire Glow", _glowMat, ParticleSystemRenderMode.Billboard, 60, 0f);
            var gc = _glow.colorOverLifetime;
            gc.enabled = true;
            var gg = new Gradient();
            gg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
            gc.color = gg;

            _embers = MakePS("Fire Embers", _emberMat, ParticleSystemRenderMode.Stretch, 120, -0.1f);
            var er = _embers.GetComponent<ParticleSystemRenderer>();
            er.lengthScale = 1.4f; er.velocityScale = 0.05f;
            var en = _embers.noise; en.enabled = true; en.strength = 0.5f; en.frequency = 1.8f;
            var es = _embers.sizeOverLifetime;
            es.enabled = true; es.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        }

        private ParticleSystem MakePS(string name, Material mat, ParticleSystemRenderMode mode, int max, float gravity)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.playOnAwake = true; m.duration = 1f;
            m.startSpeed = 0f;
            m.gravityModifier = gravity;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Shape;
            m.maxParticles = max;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var e = ps.emission; e.enabled = true; e.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        private static Material AdditiveMat(string name, int queue)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            Material m = s != null ? new Material(s) : new Material(CCRuntimeParticles.SharedMaterial);
            m.name = name;
            m.hideFlags = HideFlags.DontSave;
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = queue;
            m.mainTexture = CCHeatFx.WispTexture();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", CCHeatFx.WispTexture());
            return m;
        }

        /// <summary>Погасить: частицы долетают, потом объект удаляется.</summary>
        public void Stop(float after)
        {
            _power = 0f;
            if (_light != null) _light.enabled = false;
            CCHeatFx.Kill(gameObject, after);
        }

        /// <summary>Горсть угольков и пепла (рассыпался).</summary>
        public void Burst(Vector3 at, float size, int count)
        {
            if (_embers == null) return;
            var ep = new ParticleSystem.EmitParams();
            float sp = Mathf.Sqrt(Mathf.Max(0.04f, size) / 0.15f);
            for (int i = 0; i < count; i++)
            {
                ep.position = at + Random.insideUnitSphere * size * 0.35f;
                ep.velocity = (Random.onUnitSphere * 0.6f + Vector3.up * Random.Range(0.4f, 1.2f)) * sp;
                ep.startSize = size * Random.Range(0.03f, 0.06f);
                ep.startLifetime = Random.Range(0.6f, 1.5f);
                ep.startColor = new Color(1f, Random.Range(0.5f, 0.85f), 0.2f, 1f);
                _embers.Emit(ep, 1);
            }
        }
    }

    /// <summary>
    /// v15–16: ПЕПЕЛ И ГАРЬ — после сгорания (только вид, у каждого игрока по RPC):
    ///   • кучка: гранёная (мультяшно), из 2–3 горок, снизу тёмно-серая, сверху присыпана светлым
    ///     пеплом, чёрные крапины угля, первые секунды тлеют оранжевые угольки и остывают;
    ///   • вокруг и на кучке — чёрные угольки-обломки разной формы;
    ///   • под кучкой — кольцо рассыпанного пепла и тёмный рваный след сажи.
    /// Лежит Ash Lifetime секунд, потом оседает и тает. Не больше 40 штук.
    /// </summary>
    public sealed class CCAshMark : MonoBehaviour
    {
        private static readonly List<CCAshMark> _all = new List<CCAshMark>();
        private static Mesh[] _mounds, _chunks;
        private static Mesh _quad;
        private static Material _ashMat, _scorchMat, _powderMat;
        private static Texture2D _scorchTex, _powderTex;

        private float _t, _life;
        private Transform _pile;
        private Vector3 _pileScale;
        private Transform _scorch, _powder;
        private Vector3 _scorchScale, _powderScale;
        private Color32[] _cols;
        private bool[] _hot;
        private Mesh _pileMesh;
        private readonly List<Transform> _bits = new List<Transform>();

        public static void Spawn(Vector3 at, Vector3 normal, float size, float life)
        {
            if (_all.Count >= 40 && _all[0] != null) Destroy(_all[0].gameObject);
            var go = new GameObject("CC Ash (auto)") { hideFlags = HideFlags.DontSave };
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            go.transform.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            CCAshMark a = go.AddComponent<CCAshMark>();
            a.Build(Mathf.Max(0.03f, size), Mathf.Max(4f, life));
            _all.Add(a);
        }

        private static bool Linear => QualitySettings.activeColorSpace == ColorSpace.Linear;

        /// <summary>Цвет вершины в пространстве цвета проекта (в Linear вершины не переводятся сами — иначе пепел белёсый).</summary>
        private static Color32 Col(float r, float g, float b, byte tag = 255)
        {
            if (Linear) { r = Mathf.GammaToLinearSpace(r); g = Mathf.GammaToLinearSpace(g); b = Mathf.GammaToLinearSpace(b); }
            return new Color32((byte)(Mathf.Clamp01(r) * 255f), (byte)(Mathf.Clamp01(g) * 255f), (byte)(Mathf.Clamp01(b) * 255f), tag);
        }

        private void Build(float size, float life)
        {
            _life = life;
            if (_mounds == null) { _mounds = new Mesh[3]; var rr = new System.Random(99); for (int i = 0; i < 3; i++) _mounds[i] = Mound(rr); }
            if (_chunks == null) _chunks = Chunks();
            if (_quad == null) _quad = Quad();
            if (_ashMat == null) _ashMat = AshMaterial();
            if (_scorchMat == null) _scorchMat = ScorchMaterial();
            if (_powderMat == null) _powderMat = PowderMaterial();

            // гарь — шире всего, на 2 мм над поверхностью
            _scorch = Layer("Scorch", _quad, _scorchMat, 0.002f, size * Random.Range(1.9f, 2.4f));
            _scorchScale = _scorch.localScale;
            // рассыпанный пепел — поменьше, на 3 мм
            _powder = Layer("Ash Powder", _quad, _powderMat, 0.003f, size * Random.Range(1.2f, 1.5f));
            _powder.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            _powderScale = _powder.localScale;

            // кучка
            var pg = new GameObject("Ash Pile") { hideFlags = HideFlags.DontSave };
            pg.transform.SetParent(transform, false);
            _pileScale = new Vector3(size, size * Random.Range(0.26f, 0.34f), size) * 0.66f;
            pg.transform.localScale = _pileScale;
            _pileMesh = Instantiate(_mounds[Random.Range(0, _mounds.Length)]);
            _pileMesh.hideFlags = HideFlags.DontSave;
            _cols = _pileMesh.colors32;
            _hot = new bool[_cols.Length];
            for (int i = 0; i < _cols.Length; i++) _hot[i] = _cols[i].a == 254;
            pg.AddComponent<MeshFilter>().sharedMesh = _pileMesh;
            MeshRenderer mr = pg.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _ashMat;
            _pile = pg.transform;

            // угольки-обломки: часть на кучке, часть рядом
            int n = Random.Range(5, 9);
            for (int i = 0; i < n; i++)
            {
                var bg = new GameObject("Char Bit") { hideFlags = HideFlags.DontSave };
                bg.transform.SetParent(transform, false);
                bool onTop = i < n / 2;
                Vector2 d = Random.insideUnitCircle * (onTop ? 0.18f : 0.42f) * size;
                if (!onTop && d.magnitude < size * 0.25f) d = d.normalized * size * Random.Range(0.25f, 0.42f);
                float y = onTop ? _pileScale.y * Random.Range(0.35f, 0.75f) : size * 0.01f;
                bg.transform.localPosition = new Vector3(d.x, y, d.y);
                bg.transform.localRotation = Random.rotation;
                bg.transform.localScale = Vector3.one * size * Random.Range(0.08f, 0.16f);
                bg.AddComponent<MeshFilter>().sharedMesh = _chunks[Random.Range(0, _chunks.Length)];
                MeshRenderer br = bg.AddComponent<MeshRenderer>();
                br.sharedMaterial = _ashMat;
                _bits.Add(bg.transform);
            }
        }

        private Transform Layer(string name, Mesh mesh, Material mat, float lift, float scale)
        {
            var g = new GameObject(name) { hideFlags = HideFlags.DontSave };
            g.transform.SetParent(transform, false);
            g.transform.localPosition = Vector3.up * lift;
            g.transform.localScale = Vector3.one * scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            // угольки в кучке тлеют и остывают за 7 с
            if (_t < 8f && _cols != null)
            {
                float cool = Mathf.Clamp01(_t / 7f);
                var c = new Color32[_cols.Length];
                Color ash = new Color(0.22f, 0.2f, 0.19f);
                if (Linear) ash = ash.linear;
                for (int i = 0; i < _cols.Length; i++)
                {
                    if (!_hot[i]) { c[i] = _cols[i]; continue; }
                    float fl = 0.7f + 0.3f * Mathf.Sin(_t * 7f + i * 1.7f);
                    Color hot = new Color(1f, 0.42f, 0.08f) * fl;
                    c[i] = Color.Lerp(hot, ash, cool * cool);
                }
                _pileMesh.colors32 = c;
            }
            // в конце — кучка оседает, пепел и гарь бледнеют (сжимаются)
            float end = Mathf.Clamp01((_t - (_life - 3f)) / 3f);
            if (end > 0f)
            {
                _pile.localScale = Vector3.Scale(_pileScale, new Vector3(1f, 1f - end, 1f));
                _powder.localScale = _powderScale * (1f - end);
                _scorch.localScale = _scorchScale * (1f - end * 0.6f);
                foreach (Transform b in _bits) if (b != null) b.localScale *= 1f - Time.deltaTime * 0.6f;
            }
            if (_t >= _life) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            _all.Remove(this);
            if (_pileMesh != null) CCHeatFx.Kill(_pileMesh);
        }

        /// <summary>
        /// Холмик: 2–3 горки (сумма «куполов»), неровный край, гранёный (у каждого треугольника свой цвет):
        /// снизу тёмно-серый, кверху присыпан светлым пеплом, чёрные крапины угля, ~12% — тлеющие (метка a=254).
        /// </summary>
        private static Mesh Mound(System.Random r)
        {
            const int rings = 8, seg = 20;
            float ph = (float)r.NextDouble() * 6.28f;
            int bumps = 2 + r.Next(2);
            var bc = new Vector3[bumps];          // x, z, амплитуда
            var bs = new float[bumps];
            for (int b = 0; b < bumps; b++)
            {
                float a = (float)r.NextDouble() * 6.28f, d = b == 0 ? 0.05f : 0.15f + 0.12f * (float)r.NextDouble();
                bc[b] = new Vector3(Mathf.Cos(a) * d, b == 0 ? 1f : 0.5f + 0.35f * (float)r.NextDouble(), Mathf.Sin(a) * d);
                bs[b] = b == 0 ? 0.34f : 0.17f + 0.07f * (float)r.NextDouble();
            }
            var grid = new Vector3[rings + 1, seg];
            for (int i = 0; i <= rings; i++)
                for (int s = 0; s < seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f + (i % 2) * 0.5f / seg * Mathf.PI * 2f;
                    float edge = 0.5f * (1f + 0.16f * Mathf.Sin(a * 3f + ph) + 0.09f * Mathf.Sin(a * 5f + ph * 1.7f) + 0.05f * Mathf.Sin(a * 9f + ph));
                    float rad = edge * i / rings;
                    float x = Mathf.Cos(a) * rad, z = Mathf.Sin(a) * rad;
                    float h = 0f;
                    for (int b = 0; b < bumps; b++)
                    {
                        float dx = x - bc[b].x, dz = z - bc[b].z;
                        h += bc[b].y * Mathf.Exp(-(dx * dx + dz * dz) / (bs[b] * bs[b]));
                    }
                    h += ((float)r.NextDouble() - 0.5f) * 0.06f;
                    h *= 1f - Mathf.SmoothStep(0.7f, 1f, i / (float)rings);
                    if (i == rings) h = 0f;
                    grid[i, s] = new Vector3(x, Mathf.Max(0f, h), z);
                }
            Vector3 top = new Vector3(bc[0].x, 0f, bc[0].z);
            float th = 0f;
            for (int b = 0; b < bumps; b++) th += bc[b].y * Mathf.Exp(-((top.x - bc[b].x) * (top.x - bc[b].x) + (top.z - bc[b].z) * (top.z - bc[b].z)) / (bs[b] * bs[b]));
            top.y = th;

            var v = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
            float maxH = Mathf.Max(0.2f, th);
            for (int s = 0; s < seg; s++) Tri(v, c, t, r, top, grid[0, (s + 1) % seg], grid[0, s], maxH);
            for (int i = 0; i < rings; i++)
                for (int s = 0; s < seg; s++)
                {
                    Vector3 a0 = grid[i, s], a1 = grid[i, (s + 1) % seg], b0 = grid[i + 1, s], b1 = grid[i + 1, (s + 1) % seg];
                    Tri(v, c, t, r, a0, a1, b0, maxH);
                    Tri(v, c, t, r, a1, b1, b0, maxH);
                }
            var m = new Mesh { name = "AshMound", hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static void Tri(List<Vector3> v, List<Color32> c, List<int> t, System.Random r, Vector3 a, Vector3 b, Vector3 d, float maxH)
        {
            // лицом наружу (вверх)
            Vector3 n = Vector3.Cross(b - a, d - a);
            if (n.y < 0f) { Vector3 x = b; b = d; d = x; }
            float h = (a.y + b.y + d.y) / 3f / maxH;
            double roll = r.NextDouble();
            Color32 col;
            if (h > 0.08f && h < 0.85f && roll < 0.07) col = Col(1f, 0.42f, 0.08f, 254);                 // тлеет
            else if (roll < 0.17) { float g = 0.07f + 0.05f * (float)r.NextDouble(); col = Col(g, g * 0.95f, g * 0.9f); }   // уголь
            else
            {
                // тёмно-серый внизу → светлый пепел сверху (с разбросом)
                float g = Mathf.Lerp(0.2f, 0.62f, Mathf.SmoothStep(0.15f, 0.9f, h)) + ((float)r.NextDouble() - 0.5f) * 0.08f;
                col = Col(g, g * 0.97f, g * 0.93f);
            }
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d);
            c.Add(col); c.Add(col); c.Add(col);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        /// <summary>Чёрные угольки: обломки скорлупы (CocoaFx), почти чёрные, с серым налётом по граням.</summary>
        private static Mesh[] Chunks()
        {
            Mesh[] src = CocoaFx.ShellMeshes();
            var outM = new Mesh[src.Length];
            var r = new System.Random(5);
            for (int k = 0; k < src.Length; k++)
            {
                Mesh m = Object.Instantiate(src[k]);
                m.hideFlags = HideFlags.DontSave;
                m.name = "CharBit";
                Vector3[] vv = m.vertices;
                var cc = new Color32[vv.Length];
                for (int i = 0; i < vv.Length; i += 3)
                {
                    float g = r.NextDouble() < 0.25 ? 0.3f : 0.06f + 0.05f * (float)r.NextDouble();
                    Color32 col = Col(g, g * 0.95f, g * 0.9f);
                    for (int j = 0; j < 3 && i + j < cc.Length; j++) cc[i + j] = col;
                }
                m.colors32 = cc;
                outM[k] = m;
            }
            return outM;
        }

        private static Mesh Quad()
        {
            var m = new Mesh { name = "ScorchQuad", hideFlags = HideFlags.DontSave };
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            return m;
        }

        private static Material AshMaterial()
        {
            Shader s = Shader.Find("CatCooks/Toon Poof");             // цвет — цвет вершин, мультяшный свет
            if (s != null)
            {
                var m = new Material(s) { name = "Ash (auto)", hideFlags = HideFlags.DontSave };
                if (m.HasProperty("_Spec")) m.SetFloat("_Spec", 0f);
                if (m.HasProperty("_Rim")) m.SetFloat("_Rim", 0.12f);
                if (m.HasProperty("_ShadeDark")) m.SetFloat("_ShadeDark", 0.6f);
                if (m.HasProperty("_ShadowTint")) m.SetColor("_ShadowTint", new Color(0.8f, 0.78f, 0.82f));
                if (m.HasProperty("_LightMix")) m.SetFloat("_LightMix", 0.25f);
                if (m.HasProperty("_NearFade")) m.SetVector("_NearFade", new Vector4(0.03f, 0.1f, 0f, 0f));
                return m;
            }
            Shader lit = Shader.Find("Universal Render Pipeline/Simple Lit");
            var f = new Material(lit != null ? lit : Shader.Find("Sprites/Default")) { name = "Ash (auto, fallback)", hideFlags = HideFlags.DontSave };
            if (f.HasProperty("_BaseColor")) f.SetColor("_BaseColor", new Color(0.25f, 0.24f, 0.23f));
            return f;
        }

        private static Texture2D Blotch(string name, int seed, float edgeK, float soft, float speck)
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[S * S];
            var rr = new System.Random(seed);
            float p1 = (float)rr.NextDouble() * 6f, p2 = (float)rr.NextDouble() * 6f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float ang = Mathf.Atan2(dy, dx);
                    float edge = edgeK + 0.12f * Mathf.Sin(ang * 4f + p1) + 0.08f * Mathf.Sin(ang * 7f + p2) + 0.05f * Mathf.Sin(ang * 13f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / edge;
                    float a = Mathf.Clamp01((1f - d) / soft);
                    a = a * a * (3f - 2f * a);
                    float sp = Mathf.PerlinNoise(x * 0.3f + p1, y * 0.3f + p2);
                    a *= (1f - speck) + speck * sp;
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static Material ScorchMaterial()
        {
            if (_scorchTex == null) _scorchTex = Blotch("Scorch", 7, 0.72f, 0.5f, 0.3f);
            var m = new Material(CCRuntimeParticles.SharedMaterial) { name = "Scorch (auto)", hideFlags = HideFlags.DontSave };
            m.mainTexture = _scorchTex;
            m.color = new Color(0.06f, 0.045f, 0.035f, 0.85f);
            m.renderQueue = 2460;            // после непрозрачного, до прозрачного
            return m;
        }

        private static Material PowderMaterial()
        {
            if (_powderTex == null) _powderTex = Blotch("AshPowder", 21, 0.62f, 0.35f, 0.65f);
            var m = new Material(CCRuntimeParticles.SharedMaterial) { name = "Ash Powder (auto)", hideFlags = HideFlags.DontSave };
            m.mainTexture = _powderTex;
            m.color = new Color(0.42f, 0.4f, 0.38f, 0.75f);
            m.renderQueue = 2461;
            return m;
        }
    }
}
