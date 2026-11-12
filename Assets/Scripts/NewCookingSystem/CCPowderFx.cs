using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v18: ПЫЛЬ И ОШМЁТКИ — долбление (отбойник), удар (кувалда), зачерпывание, высыпание.
    ///   • облачка — МУЛЬТЯШНЫЕ клубки: объёмные «пуфы» из нескольких шаров (шейдер CatCooks/Toon Dust),
    ///     матовые, чуть прозрачные, две ступеньки света, без бликов. Форма устойчивая: клубок
    ///     быстро «выпрыгивает», висит и сжимается в ноль (а не тает размытым пятном);
    ///   • ошмётки — объёмные кусочки РАЗНОЙ формы (щепки и комочки), у каждого свой оттенок;
    /// Квадратиков и «реалистичного» тумана нет. По сети не ходит: каждый игрок рисует у себя.
    /// </summary>
    public static class CCPowderFx
    {
        private static ParticleSystem _shards, _clumps, _dust;
        private static Material _mat, _dustMat;
        private static Mesh[] _puffMeshes;

        /// <summary>Один мультяшный клубок пыли.</summary>
        private static void Dust(Vector3 at, Vector3 vel, float size, Color c, float life)
        {
            if (_dust == null) BuildDust();
            var ep = new ParticleSystem.EmitParams();
            ep.position = at;
            ep.velocity = vel;
            ep.startSize = size * 1.25f;
            c.a = 1f;
            ep.startColor = c;
            ep.startLifetime = life;
            ep.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
            _dust.Emit(ep, 1);
        }

        /// <summary>
        /// Клубок-«пуф»: шар с 4–6 выпуклостями (как мультяшное облачко). Три разных — облака не одинаковые.
        /// </summary>
        public static Mesh[] PuffMeshes()
        {
            if (_puffMeshes != null && _puffMeshes[0] != null) return _puffMeshes;
            _puffMeshes = new Mesh[3];
            for (int k = 0; k < 3; k++) _puffMeshes[k] = BuildPuff(1000 + k * 77, 4 + k);
            return _puffMeshes;
        }

        private static Mesh BuildPuff(int seed, int lobes)
        {
            var rnd = new System.Random(seed);
            Vector3[] dirs = new Vector3[lobes];
            float[] amp = new float[lobes];
            for (int i = 0; i < lobes; i++)
            {
                Vector3 d = new Vector3((float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 1.4f - 0.4f, (float)rnd.NextDouble() * 2f - 1f);
                dirs[i] = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.up;
                amp[i] = 0.18f + (float)rnd.NextDouble() * 0.22f;
            }
            const int LA = 12, LO = 20;            // широта × долгота
            var v = new Vector3[(LA + 1) * (LO + 1)];
            var t = new int[LA * LO * 6];
            int vi = 0;
            for (int i = 0; i <= LA; i++)
            {
                float th = i / (float)LA * Mathf.PI;
                for (int j = 0; j <= LO; j++)
                {
                    float ph = j / (float)LO * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    float r = 0.78f;
                    for (int l = 0; l < lobes; l++)
                    {
                        float k = Mathf.Max(0f, Vector3.Dot(d, dirs[l]));
                        r += amp[l] * k * k * k;                // круглые выпуклости-«шары»
                    }
                    if (d.y < -0.2f) r *= 1f + (d.y + 0.2f) * 0.35f;     // снизу чуть приплюснут
                    v[vi++] = d * (r * 0.5f);
                }
            }
            int ti = 0;
            for (int i = 0; i < LA; i++)
                for (int j = 0; j < LO; j++)
                {
                    int a = i * (LO + 1) + j, b = a + 1, c = a + LO + 1, d = c + 1;
                    t[ti++] = a; t[ti++] = b; t[ti++] = c;
                    t[ti++] = b; t[ti++] = d; t[ti++] = c;
                }
            var m = new Mesh { name = "CC Dust Puff " + seed, hideFlags = HideFlags.DontSave };
            m.vertices = v;
            m.triangles = t;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static void BuildDust()
        {
            var go = new GameObject("CC Powder Dust") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            go.SetActive(false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.playOnAwake = true; m.duration = 1f; m.startSpeed = 0f;
            m.startRotation3D = true;
            m.gravityModifier = -0.01f;                 // чуть всплывает
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Shape;
            m.maxParticles = 500;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var e = ps.emission; e.enabled = true; e.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            // мультяшно: «выпрыгнул» → висит → сжался в точку
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.25f, 0f, 9f), new Keyframe(0.14f, 1.05f), new Keyframe(0.22f, 1f),
                new Keyframe(0.7f, 0.95f), new Keyframe(1f, 0f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0.6f, 1f) });
            col.color = g;
            var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = 5f; lv.limit = 100f;   // быстро тормозит
            var ro = ps.rotationOverLifetime; ro.enabled = true; ro.separateAxes = true;
            ro.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f); ro.y = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f); ro.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.SetMeshes(PuffMeshes());
            r.alignment = ParticleSystemRenderSpace.World;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.sharedMaterial = DustMaterial;
            go.SetActive(true);
            ps.Play();
            _dust = ps;
        }

        /// <summary>Материал мультяшной пыли (Toon Dust; если шейдера нет — матовые частицы).</summary>
        public static Material DustMaterial
        {
            get
            {
                if (_dustMat != null) return _dustMat;
                Shader s = Shader.Find("CatCooks/Toon Dust");
                if (s != null)
                {
                    _dustMat = new Material(s) { name = "Toon Dust (auto)", hideFlags = HideFlags.DontSave };
                    return _dustMat;
                }
                Debug.LogWarning("[Пыль] нет шейдера «CatCooks/Toon Dust» — пыль будет простой. Меню CatCooks → Вещества → 1");
                s = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
                if (s == null) s = CCRuntimeParticles.SharedMaterial.shader;
                _dustMat = new Material(s) { name = "Powder Dust (auto)", hideFlags = HideFlags.DontSave };
                if (_dustMat.HasProperty("_Surface")) _dustMat.SetFloat("_Surface", 1f);
                if (_dustMat.HasProperty("_Smoothness")) _dustMat.SetFloat("_Smoothness", 0f);
                if (_dustMat.HasProperty("_SrcBlend")) _dustMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (_dustMat.HasProperty("_DstBlend")) _dustMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (_dustMat.HasProperty("_ZWrite")) _dustMat.SetFloat("_ZWrite", 0f);
                _dustMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _dustMat.renderQueue = 3000;
                return _dustMat;
            }
        }

        /// <summary>v18: клубок пыли (зачерпнули, высыпали, упало). size — размер места (м), amount — сколько.</summary>
        public static void Puff(Vector3 at, Vector3 up, Color color, float size, float amount = 1f)
        {
            if (!Application.isPlaying || amount <= 0f) return;
            size = Mathf.Clamp(size, 0.03f, 1f);
            float sp = Mathf.Sqrt(size / 0.3f);
            int n = Mathf.Max(1, Mathf.RoundToInt(2f * amount));
            Clouds(at, up, color, size * 0.3f, n, sp * 0.6f);
        }

        /// <summary>v18: одна крошка/комочек падает с края (мастерок дёрнули). clump — круглый комочек.</summary>
        public static void Crumb(Vector3 at, Vector3 vel, Color color, float size, bool clump)
        {
            if (!Application.isPlaying) return;
            Ensure();
            var ep = new ParticleSystem.EmitParams();
            ep.position = at;
            ep.velocity = vel;
            ep.startSize = Mathf.Max(0.004f, size);
            ep.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
            ep.startLifetime = Random.Range(1f, 1.6f);
            color.a = 1f;
            ep.startColor = color;
            (clump ? _clumps : _shards).Emit(ep, 1);
        }

        /// <summary>Отбойник: один «удар» в таре. size — радиус горловины, k — сила 0..1.</summary>
        public static void Pound(Vector3 at, Vector3 up, Color color, float size, float k)
        {
            if (!Application.isPlaying) return;
            size = Mathf.Clamp(size, 0.05f, 1f);
            float sp = Mathf.Sqrt(size / 0.3f);
            // v17.5: гуще — больше облачков и ошмётков
            Clouds(at + up * size * 0.03f, up, color, size * (0.26f + 0.1f * k), Random.Range(2, 4), sp * 0.85f);
            if (Random.value < 0.45f) Haze(at + up * size * 0.18f, up, color, size);
            Chunks(at + up * size * 0.03f, up, color, size, Random.Range(2, 4), sp);
        }

        /// <summary>Удар (кувалда) по полу, столу, продукту: горсть пыли и ошмётков. size — масштаб удара (м).</summary>
        /// <summary>
        /// v17.6: удар (кувалда): пыль плоским кольцом — «пых!» в стороны над поверхностью, быстро тает.
        /// </summary>
        public static void Hit(Vector3 at, Vector3 up, Color color, float size, float amount = 1f, float spread = 1f)
        {
            if (!Application.isPlaying) return;
            size = Mathf.Clamp(size, 0.05f, 1f);
            float sp = Mathf.Sqrt(size / 0.3f) * 2.2f * Mathf.Clamp(spread, 0.1f, 3f);
            Color dust = DustOf(color);
            int n = Mathf.RoundToInt(10 * amount);
            float a0 = Random.Range(0f, 6.28f);
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i / (float)n * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
                Vector3 side = Vector3.ProjectOnPlane(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), up).normalized;
                Vector3 v = (side * Random.Range(0.8f, 1.4f) + up * Random.Range(0.05f, 0.25f)) * sp;
                Dust(at + up * size * 0.04f + side * size * 0.1f * spread, v, size * Random.Range(0.2f, 0.3f) * Mathf.Lerp(1f, spread, 0.5f),
                     Color.Lerp(dust, color, Random.value * 0.5f), Random.Range(0.35f, 0.5f));
            }
            Chunks(at + up * size * 0.04f, up, color, size, Mathf.RoundToInt(5 * amount), sp * 0.6f);
        }

        public static void Impact(Vector3 at, Vector3 up, Color color, float size, float amount = 1f)
        {
            if (!Application.isPlaying) return;
            size = Mathf.Clamp(size, 0.05f, 1f);
            float sp = Mathf.Sqrt(size / 0.3f) * 1.3f;
            Clouds(at + up * size * 0.05f, up, color, size * 0.3f, Mathf.RoundToInt(4 * amount), sp);
            Haze(at + up * size * 0.2f, up, color, size * 1.4f);
            if (amount > 1.2f) Haze(at + up * size * 0.35f, up, color, size * 1.8f);
            Chunks(at + up * size * 0.04f, up, color, size, Mathf.RoundToInt(7 * amount), sp * 1.2f);
        }

        /// <summary>
        /// v17.5: высыпается из тары (ковш экскаватора): от края — комки и пыль вниз по дуге,
        /// у земли — облако. rim — край, outward — куда сыплется, land — куда упадёт.
        /// </summary>
        public static void Fall(Vector3 rim, Vector3 outward, Vector3 land, Color color, float size)
        {
            if (!Application.isPlaying) return;
            size = Mathf.Clamp(size, 0.05f, 1f);
            Ensure();
            var ep = new ParticleSystem.EmitParams();
            int n = Random.Range(3, 6);
            for (int i = 0; i < n; i++)
            {
                bool clump = Random.value < 0.6f;
                ep.position = rim + Random.insideUnitSphere * size * 0.15f;
                ep.velocity = outward * Random.Range(0.4f, 1f) + Vector3.down * Random.Range(0f, 0.4f) + Random.insideUnitSphere * 0.2f;
                ep.startSize = size * (clump ? Random.Range(0.05f, 0.1f) : Random.Range(0.05f, 0.11f));
                ep.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                ep.startLifetime = Random.Range(1.2f, 2f);
                Color c = color * Random.Range(0.6f, 1.1f); c.a = 1f;
                ep.startColor = c;
                (clump ? _clumps : _shards).Emit(ep, 1);
            }
            Clouds(rim, Vector3.up, color, size * 0.22f, 1, 0.5f);       // пыль у края
            Clouds(land + Vector3.up * size * 0.05f, Vector3.up, color, size * 0.35f, 2, 0.8f);   // облако у земли
            if (Random.value < 0.4f) Haze(land + Vector3.up * size * 0.25f, Vector3.up, color, size * 1.2f);
        }

        private static Color DustOf(Color c)
        {
            // пыль — того же цвета, чуть светлее и мягче (без ухода в серый)
            Color d = Color.Lerp(c, c * 1.25f, 0.6f);
            d.a = 1f;
            return d;
        }

        private static void Clouds(Vector3 at, Vector3 up, Color color, float size, int count, float sp)
        {
            Color dust = DustOf(color);
            for (int i = 0; i < count; i++)
            {
                Vector2 r = Random.insideUnitCircle.normalized;
                Vector3 side = Vector3.ProjectOnPlane(new Vector3(r.x, 0f, r.y), up).normalized;
                Vector3 v = (side * Random.Range(0.25f, 0.6f) + up * Random.Range(0.3f, 0.6f)) * sp;
                Vector3 p = at + side * size * 0.3f;
                float s = size * Random.Range(0.8f, 1.2f);
                float life = Random.Range(0.55f, 0.9f);
                Dust(p, v, s, Color.Lerp(dust, color, Random.Range(0f, 0.5f)), life);
                // мелкие детали рядом — облачко не «грубое»
                int bits = Random.Range(2, 4);
                for (int b = 0; b < bits; b++)
                {
                    Vector3 o = Random.onUnitSphere;
                    if (Vector3.Dot(o, up) < 0f) o = Vector3.Reflect(o, up);
                    Dust(p + o * s * Random.Range(0.45f, 0.75f), v * 1.2f + o * 0.3f * sp,
                         s * Random.Range(0.25f, 0.4f), Color.Lerp(dust, color, Random.value), life * Random.Range(0.6f, 0.9f));
                }
            }
        }

        /// <summary>v18: вместо «реалистичного» тумана — один крупный медленный клубок.</summary>
        private static void Haze(Vector3 at, Vector3 up, Color color, float size)
        {
            Color c = Color.Lerp(DustOf(color), color, 0.3f);
            Dust(at, up * Random.Range(0.1f, 0.2f) + Random.insideUnitSphere * 0.05f, size * Random.Range(0.35f, 0.5f), c, Random.Range(0.9f, 1.3f));
        }

        private static void Chunks(Vector3 at, Vector3 up, Color color, float size, int count, float sp)
        {
            if (count <= 0) return;
            Ensure();
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                bool clump = Random.value < 0.55f;
                Vector3 rnd = Vector3.ProjectOnPlane(Random.insideUnitSphere, up);
                ep.position = at;
                ep.velocity = (up * Random.Range(0.9f, 1.8f) + rnd * Random.Range(0.4f, 1f)) * sp;
                ep.startSize = size * (clump ? Random.Range(0.04f, 0.08f) : Random.Range(0.05f, 0.1f));
                ep.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                ep.startLifetime = Random.Range(0.9f, 1.5f);
                // у каждого свой оттенок: темнее/светлее
                Color c = color * Random.Range(0.55f, 1.1f);
                c.a = 1f;
                ep.startColor = c;
                (clump ? _clumps : _shards).Emit(ep, 1);
            }
        }

        private static void Ensure()
        {
            if (_shards != null && _clumps != null) return;
            if (_mat == null)
            {
                // частицы-сетки с цветом частицы (у каждого ошмётка свой оттенок)
                Shader s = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
                if (s == null) s = Shader.Find("Universal Render Pipeline/Particles/Lit");
                if (s == null) s = Shader.Find("Universal Render Pipeline/Simple Lit");
                _mat = new Material(s != null ? s : Shader.Find("Sprites/Default")) { name = "Powder Chunks (auto)", hideFlags = HideFlags.DontSave };
                if (_mat.HasProperty("_Surface")) _mat.SetFloat("_Surface", 0f);      // непрозрачные
                if (_mat.HasProperty("_Smoothness")) _mat.SetFloat("_Smoothness", 0f);
                if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", Color.white);
                _mat.enableInstancing = true;
            }
            if (_shards == null) { _shards = CocoaFx.MeshSystem("CC Powder Shards", _mat, CocoaFx.ShellMeshes(), 0.01f); Object.DontDestroyOnLoad(_shards.gameObject); }
            if (_clumps == null) { _clumps = CocoaFx.MeshSystem("CC Powder Clumps", _mat, CocoaFx.PulpMeshes(), 0.01f); Object.DontDestroyOnLoad(_clumps.gameObject); }
        }
    }
}
