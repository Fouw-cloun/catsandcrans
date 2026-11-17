using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v23.50: ПАР ОТ СВЕЖЕГО БИСКВИТА — как из печи. Только вид, у каждого игрока свой.
    ///
    /// Бисквит испёкся (опалубка → «Испечено») — <see cref="MarkBaked"/>. С этого момента пар идёт от бисквита в опалубке,
    /// от вынутого пласта и от каждого отрезанного кусочка (и на мастерке) и за <see cref="CoolSeconds"/> (~минута)
    /// стихает — остыл. Кусочек, отрезанный позже, парит уже слабее: «остывание» общее.
    /// Вешается кодом (опалубка, пласт, кусочек), руками не нужно.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpongeSteam : MonoBehaviour
    {
        /// <summary>За сколько секунд бисквит остывает (пар пропадает).</summary>
        public static float CoolSeconds = 60f;
        private static float _bakedAt = -1000f;

        /// <summary>Бисквит только что испёкся — пошёл пар.</summary>
        public static void MarkBaked() => _bakedAt = Time.time;

        /// <summary>Жар 1 → 0 за CoolSeconds.</summary>
        public static float Heat => Mathf.Clamp01(1f - (Time.time - _bakedAt) / Mathf.Max(1f, CoolSeconds));

        [Tooltip("Сила пара (струек в секунду при полном жаре на 1 м² верха)")]
        public float Rate = 26f;

        private ParticleSystem _ps;
        private Renderer _src;
        private float _boundsAt;
        private static Material _mat;
        private static Texture2D _tex;

        private void OnEnable()
        {
            if (Application.isBatchMode) { enabled = false; return; }
            _src = GetComponent<Renderer>();
        }

        private void Update()
        {
            float heat = Heat;
            if (heat <= 0.001f)
            {
                if (_ps != null && _ps.emission.enabled) { ParticleSystem.EmissionModule e0 = _ps.emission; e0.enabled = false; }
                return;
            }
            if (_src == null) _src = GetComponent<Renderer>();
            if (_src == null || !_src.enabled || !_src.gameObject.activeInHierarchy) { if (_ps != null) { ParticleSystem.EmissionModule e1 = _ps.emission; e1.enabled = false; } return; }
            if (_ps == null) _ps = Make();
            if (_ps == null) return;
            if (Time.time >= _boundsAt)
            {
                _boundsAt = Time.time + 0.25f;
                Bounds b = _src.bounds;
                _ps.transform.SetPositionAndRotation(new Vector3(b.center.x, b.max.y - b.size.y * 0.05f, b.center.z), Quaternion.identity);
                ParticleSystem.ShapeModule sh = _ps.shape;
                sh.scale = new Vector3(Mathf.Max(0.02f, b.size.x * 0.8f), 0.01f, Mathf.Max(0.02f, b.size.z * 0.8f));
                float size = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z), 0.04f, 1.2f);
                ParticleSystem.MainModule m = _ps.main;
                // v23.51: незаметнее и ровнее — мелкие прозрачные струйки, их больше, без «пыхов»
                m.startSize = new ParticleSystem.MinMaxCurve(size * 0.14f, size * 0.26f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(size * 0.04f, size * 0.1f);
                ParticleSystem.EmissionModule em = _ps.emission;
                em.enabled = true;
                float area = Mathf.Max(0.002f, b.size.x * b.size.z);
                em.rateOverTime = Mathf.Clamp(Rate * area * 10f, 4f, 40f) * heat;          // остывает — пар реже
                m.startColor = new Color(1f, 0.99f, 0.97f, (0.06f + 0.08f * heat) * heat);     // и прозрачнее
            }
        }

        private void OnDisable()
        {
            if (_ps != null) { ParticleSystem.EmissionModule e = _ps.emission; e.enabled = false; }
        }

        private void OnDestroy()
        {
            if (_ps != null) Destroy(_ps.gameObject);
        }

        private ParticleSystem Make()
        {
            var go = new GameObject("SpongeSteam (auto)") { hideFlags = HideFlags.DontSave };
            go.SetActive(false);                                           // без родителя: масштаб родителя (ковш ×200, мастерок) не раздувает пар
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule m = ps.main;
            m.playOnAwake = true;
            m.loop = true;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.gravityModifier = -0.02f;                                    // тёплый пар поднимается
            m.maxParticles = 160;
            ParticleSystem.EmissionModule em = ps.emission; em.enabled = true; em.rateOverTime = 4f;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.rotation = Vector3.zero;
            ParticleSystem.VelocityOverLifetimeModule vl = ps.velocityOverLifetime;
            vl.enabled = true; vl.space = ParticleSystemSimulationSpace.World;
            vl.x = new ParticleSystem.MinMaxCurve(-0.015f, 0.015f);
            vl.y = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            vl.z = new ParticleSystem.MinMaxCurve(-0.015f, 0.015f);
            ParticleSystem.SizeOverLifetimeModule sl = ps.sizeOverLifetime;
            sl.enabled = true;
            sl.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.6f)));   // расплывается
            ParticleSystem.RotationOverLifetimeModule rl = ps.rotationOverLifetime;
            rl.enabled = true; rl.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            ParticleSystem.ColorOverLifetimeModule cl = ps.colorOverLifetime;
            cl.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.35f), new GradientAlphaKey(0.5f, 0.7f), new GradientAlphaKey(0f, 1f) });
            cl.color = g;
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Material mat = Mat();
            if (mat == null) { Destroy(go); enabled = false; return null; }
            r.sharedMaterial = mat;
            go.SetActive(true);
            return ps;
        }

        private static Material Mat()
        {
            if (_mat != null) return _mat;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) return null;
            _mat = new Material(sh) { name = "SpongeSteam (auto)", hideFlags = HideFlags.DontSave };
            _mat.mainTexture = Tex();
            if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", Tex());
            return _mat;
        }

        /// <summary>Мягкое облачко: круг с размытым краем и лёгкими клочьями.</summary>
        private static Texture2D Tex()
        {
            if (_tex != null) return _tex;
            const int N = 64;
            _tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "SpongeSteam (auto)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float n = Mathf.PerlinNoise(u * 3.1f + 5.3f, v * 3.1f + 1.7f);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * a * (0.8f + 0.3f * n);                                  // v23.51: мягкий край, без резких клочьев
                    px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            _tex.SetPixels(px);
            _tex.Apply(false, true);
            return _tex;
        }
    }
}
