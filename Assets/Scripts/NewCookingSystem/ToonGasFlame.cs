using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v14: МУЛЬТЯШНАЯ ГАЗОВАЯ СТРУЯ — для горелок (маленькой и кровельной).
    ///
    /// Три вложенных конуса (шейдер CatCooks/Toon Flame): оболочка → середина → ядро у сопла.
    /// Ступени цвета жёсткие, граница гуляет языками, кончик рвётся, вершины дышат и виляют.
    /// Упёрлась в предмет — струя короче и «расплющивается» (шире у препятствия).
    /// Над кончиком — марево (CCHeatFx), из кончика изредка искры и дымок.
    ///
    /// Не компонент сцены: горелка создаёт объект сама (ToonGasFlame.Create) и каждый кадр
    /// вызывает Tick(сопло, горит 0..1, длина, радиус). Объект не дочерний (масштаб модели
    /// горелки на пламя не влияет). Если шейдера нет — Create вернёт null, горелка покажет
    /// старые частицы.
    /// </summary>
    public sealed class ToonGasFlame : MonoBehaviour
    {
        public enum Kind { Blow, Roofing }

        private static Mesh _cone;

        private Transform[] _layers;
        private MeshRenderer[] _rends;
        private Material[] _mats;
        private Kind _kind;
        private float _on;
        private float _seed;
        private float _sparkAcc, _smokeAcc;
        private ParticleSystem _sparks;
        private Material _sparkMat;
        private readonly RaycastHit[] _hits = new RaycastHit[12];
        private float _len;

        // доли длины / радиуса слоёв
        private static readonly float[] LayerLen = { 1f, 0.58f, 0.26f };
        private static readonly float[] LayerRad = { 1f, 0.6f, 0.34f };

        public static ToonGasFlame Create(string name, Kind kind)
        {
            Shader sh = Shader.Find("CatCooks/Toon Flame");
            if (sh == null)
            {
                Debug.LogWarning("[Горелка] нет шейдера «CatCooks/Toon Flame» — пламя старыми частицами. Меню CatCooks → Огонь и жар → Добавить шейдеры в сборку");
                return null;
            }
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            ToonGasFlame f = go.AddComponent<ToonGasFlame>();
            f.Build(sh, kind);
            return f;
        }

        private void Build(Shader sh, Kind kind)
        {
            _kind = kind;
            _seed = Random.Range(0f, 50f);
            if (_cone == null) _cone = BuildCone(28, 18);
            _layers = new Transform[3];
            _rends = new MeshRenderer[3];
            _mats = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject(i == 0 ? "Flame Outer" : i == 1 ? "Flame Mid" : "Flame Core") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _cone;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                Material m = new Material(sh) { name = "ToonFlame " + go.name + " (auto)", hideFlags = HideFlags.DontSave };
                m.renderQueue = 3000 + i;                    // оболочка → середина → ядро поверх
                Preset(m, kind, i);
                // v15: дыры в языках с каймой (как в Zelda): у оболочки больше, у ядра нет
                m.SetFloat("_Holes", i == 0 ? (kind == Kind.Roofing ? 0.55f : 0.32f) : i == 1 ? (kind == Kind.Roofing ? 0.28f : 0.12f) : 0f);
                m.SetColor("_EdgeCol", kind == Kind.Roofing ? new Color(0.95f, 0.28f, 0.05f) : (i == 0 ? new Color(0.35f, 0.3f, 1f) : new Color(0.5f, 0.7f, 1f)));
                m.SetFloat("_Seed", _seed + i * 3.3f);
                mr.sharedMaterial = m;
                _layers[i] = go.transform;
                _rends[i] = mr;
                _mats[i] = m;
            }
            SetVisible(false);
        }

        /// <summary>Цвета и характер слоёв: маленькая — синяя газовая; кровельная — рыжий мультяшный огонь с синим низом.</summary>
        private static void Preset(Material m, Kind kind, int layer)
        {
            if (kind == Kind.Blow)
            {
                if (layer == 0)
                {
                    Set(m, new Color(0.4f, 0.68f, 1f), new Color(0.3f, 0.42f, 1f), new Color(1f, 0.55f, 0.25f), 0.35f, 0.78f, 0.62f, 1f, 0.2f, 0.3f);
                    m.SetFloat("_Wobble", 0.16f); m.SetFloat("_Sway", 0.1f); m.SetFloat("_Speed", 7f); m.SetFloat("_Alpha", 0.75f);
                }
                else if (layer == 1)
                {
                    Set(m, new Color(0.5f, 0.82f, 1f), new Color(0.3f, 0.58f, 1f), new Color(0.28f, 0.45f, 1f), 0.4f, 0.85f, 0.78f, 0.9f, 0.15f, 0.15f);      // v16: меньше белого
                    m.SetFloat("_Alpha", 0.7f);
                    m.SetFloat("_Wobble", 0.08f); m.SetFloat("_Sway", 0.04f); m.SetFloat("_Speed", 9f);
                }
                else
                {
                    Set(m, new Color(0.72f, 0.92f, 1f), new Color(0.5f, 0.8f, 1f), new Color(0.4f, 0.7f, 1f), 0.5f, 0.9f, 0.9f, 0.95f, 0.1f, 0.1f);       // v16: ядро голубое, не белое
                    m.SetFloat("_Alpha", 0.6f);
                    m.SetFloat("_Wobble", 0.03f); m.SetFloat("_Sway", 0.01f); m.SetFloat("_Speed", 10f);
                }
            }
            else
            {
                if (layer == 0)
                {
                    Set(m, new Color(1f, 0.85f, 0.35f), new Color(1f, 0.52f, 0.12f), new Color(0.95f, 0.25f, 0.08f), 0.2f, 0.52f, 0.5f, 1.15f, 0.25f, 0.2f);
                    m.SetFloat("_Wobble", 0.3f); m.SetFloat("_Sway", 0.22f); m.SetFloat("_Speed", 8f); m.SetFloat("_BandNoise", 0.26f); m.SetFloat("_NoiseScale", 4.5f);
                }
                else if (layer == 1)
                {
                    Set(m, new Color(1f, 0.92f, 0.55f), new Color(1f, 0.72f, 0.22f), new Color(1f, 0.5f, 0.12f), 0.3f, 0.7f, 0.62f, 1.25f, 0.3f, 0.25f);
                    m.SetFloat("_Wobble", 0.18f); m.SetFloat("_Sway", 0.12f); m.SetFloat("_Speed", 9f);
                }
                else
                {
                    Set(m, new Color(0.85f, 0.95f, 1f), new Color(0.4f, 0.6f, 1f), new Color(0.3f, 0.45f, 1f), 0.35f, 0.8f, 0.8f, 1.4f, 0.3f, 0.2f);
                    m.SetFloat("_Wobble", 0.05f); m.SetFloat("_Sway", 0.02f); m.SetFloat("_Speed", 11f);
                }
            }
        }

        private static void Set(Material m, Color a, Color b, Color c, float b1, float b2, float dissolve, float glow, float core, float additive)
        {
            m.SetColor("_ColA", a); m.SetColor("_ColB", b); m.SetColor("_ColC", c);
            m.SetFloat("_Band1", b1); m.SetFloat("_Band2", b2);
            m.SetFloat("_Dissolve", dissolve); m.SetFloat("_Glow", glow);
            m.SetFloat("_Core", core); m.SetFloat("_Additive", additive);
        }

        /// <summary>Каждый кадр от горелки. on — 0..1 (разгорается/гаснет), length/radius — как у струи (IHeatJet).</summary>
        public void Tick(Transform nozzle, float on, float length, float radius, Transform ignoreRoot)
        {
            if (nozzle == null) return;
            _on = Mathf.MoveTowards(_on, on, Time.deltaTime * (on > _on ? 6f : 8f));
            bool vis = _on > 0.01f;
            SetVisible(vis);
            if (!vis) return;

            Vector3 o = nozzle.position, d = nozzle.forward;
            // упёрлась — короче и шире
            float L = Mathf.Max(0.02f, length) * Mathf.Lerp(0.3f, 1f, _on);
            float hitDist = ObstacleDistance(o, d, L, radius * 0.35f, ignoreRoot);
            float squash = 1f;
            if (hitDist < L)
            {
                float k = Mathf.Clamp01(hitDist / L);
                squash = Mathf.Lerp(1.9f, 1f, k);
                L = Mathf.Max(hitDist * 1.08f, L * 0.18f);
            }
            _len = Mathf.Lerp(_len <= 0f ? L : _len, L, 1f - Mathf.Exp(-Time.deltaTime * 25f));
            float R = Mathf.Max(0.005f, radius) * Mathf.Lerp(0.5f, 1f, _on);
            float flick = 1f + (Mathf.PerlinNoise(Time.time * 11f, _seed) - 0.5f) * 0.12f;

            _nozzle = nozzle;
            ApplyPose();
            for (int i = 0; i < 3; i++)
            {
                float rr = R * LayerRad[i] * (i == 0 ? squash : Mathf.Lerp(1f, squash, 0.5f));
                float ll = _len * LayerLen[i] * (i == 0 ? flick : 1f);
                if (i > 0) ll = Mathf.Min(ll, _len * 0.92f);
                _layers[i].localScale = new Vector3(rr, rr, ll);
            }

            // v19: марево (летающая «трапеция») убрано — вместо него ровное свечение у сопла; искры, дымок
            Vector3 tip = o + d * _len;
            _glowSize = Mathf.Max(R * 7f, 0.06f) * (_kind == Kind.Roofing ? 1.3f : 1f);
            _glowOn = _on;
            Sparks(o, d, tip, R);
        }

        // v16: пламя не отстаёт от мыши — поза горелки в руке ставится позже Update (рукой), поэтому
        // прямо перед отрисовкой кадра ещё раз ставим пламя на сопло
        private Transform _nozzle;

        private void ApplyPose()
        {
            UpdateGlow();
            if (_nozzle == null) return;
            Vector3 d = _nozzle.forward;
            transform.SetPositionAndRotation(_nozzle.position, Quaternion.LookRotation(d, Mathf.Abs(Vector3.Dot(d, Vector3.up)) > 0.95f ? _nozzle.up : Vector3.up));
        }

        private void OnEnable() { Application.onBeforeRender += ApplyPose; }
        private void OnDisable() { Application.onBeforeRender -= ApplyPose; }
        private void LateUpdate() { ApplyPose(); }

        // ── v19: ровное свечение у сопла — мягкий круглый ореол, всегда лицом к камере, не прыгает ──
        private float _glowSize, _glowOn;
        private Transform _glow;
        private Material _glowMat;

        private static Mesh _glowQuad;

        private static Mesh GlowQuad()
        {
            if (_glowQuad != null) return _glowQuad;
            _glowQuad = new Mesh { name = "Torch Glow Quad", hideFlags = HideFlags.DontSave };
            _glowQuad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            _glowQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _glowQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _glowQuad.RecalculateBounds();
            return _glowQuad;
        }

        private void UpdateGlow()
        {
            if (_glowOn <= 0.01f) { if (_glow != null && _glow.gameObject.activeSelf) _glow.gameObject.SetActive(false); return; }
            if (_glow == null)
            {
                var go = new GameObject("Glow (auto)") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = GlowQuad();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                _glowMat = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "Torch Glow (auto)", hideFlags = HideFlags.DontSave };
                if (_glowMat.HasProperty("_Surface")) _glowMat.SetFloat("_Surface", 1f);
                if (_glowMat.HasProperty("_Blend")) _glowMat.SetFloat("_Blend", 2f);                // добавочное
                if (_glowMat.HasProperty("_SrcBlend")) _glowMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (_glowMat.HasProperty("_DstBlend")) _glowMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                if (_glowMat.HasProperty("_ZWrite")) _glowMat.SetFloat("_ZWrite", 0f);
                _glowMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _glowMat.renderQueue = 3100;
                _glowMat.mainTexture = CCHeatFx.WispTexture();
                if (_glowMat.HasProperty("_BaseMap")) _glowMat.SetTexture("_BaseMap", CCHeatFx.WispTexture());
                mr.sharedMaterial = _glowMat;
                _glow = go.transform;
            }
            if (!_glow.gameObject.activeSelf) _glow.gameObject.SetActive(true);
            Camera cam = Camera.main;
            if (cam != null) _glow.rotation = Quaternion.LookRotation(_glow.position - cam.transform.position, cam.transform.up);
            float pulse = 1f + Mathf.Sin(Time.time * 3f + _seed) * 0.04f;                    // еле заметно дышит
            Vector3 ls = transform.lossyScale;
            float inv = 1f / Mathf.Max(1e-4f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
            _glow.localPosition = Vector3.forward * (_glowSize * 0.15f * inv);
            _glow.localScale = Vector3.one * (_glowSize * pulse * inv);
            Color c = _kind == Kind.Roofing ? new Color(1f, 0.55f, 0.25f, 0.35f) : new Color(0.45f, 0.65f, 1f, 0.35f);
            c.a *= _glowOn;
            if (_glowMat.HasProperty("_BaseColor")) _glowMat.SetColor("_BaseColor", c);
            _glowMat.color = c;
        }

        private float ObstacleDistance(Vector3 o, Vector3 d, float L, float r, Transform ignoreRoot)
        {
            int n = Physics.SphereCastNonAlloc(o, Mathf.Max(0.004f, r), d, _hits, L, ~0, QueryTriggerInteraction.Ignore);
            float best = L;
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i].collider;
                if (c == null) continue;
                if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) continue;
                if (c.GetComponentInParent<CharacterController>() != null) continue;
                float dist = _hits[i].distance;
                if (dist <= 0f) continue;                      // сопло внутри — не укорачиваем
                if (dist < best) best = dist;
            }
            return best;
        }

        private void Sparks(Vector3 o, Vector3 d, Vector3 tip, float R)
        {
            float rate = (_kind == Kind.Roofing ? 7f : 2.5f) * _on;
            _sparkAcc += rate * Time.deltaTime;
            if (_sparkAcc >= 1f)
            {
                _sparkAcc -= 1f;
                if (_sparks == null) BuildSparks();
                var ep = new ParticleSystem.EmitParams();
                ep.position = Vector3.Lerp(o, tip, Random.Range(0.6f, 1f)) + Random.insideUnitSphere * R * 0.5f;
                ep.velocity = d * Random.Range(1f, 2.5f) + Vector3.up * Random.Range(0.5f, 1.5f) + Random.insideUnitSphere * 0.6f;
                ep.startSize = R * Random.Range(0.08f, 0.16f);
                ep.startLifetime = Random.Range(0.25f, 0.6f);
                ep.startColor = _kind == Kind.Roofing ? new Color(1f, Random.Range(0.6f, 0.9f), 0.3f, 1f) : new Color(1f, 0.75f, 0.4f, 1f);
                _sparks.Emit(ep, 1);
            }
            // дымок от кровельной (жирное пламя), у маленькой — почти нет
            float sm = (_kind == Kind.Roofing ? 2.5f : 0.4f) * _on;
            _smokeAcc += sm * Time.deltaTime;
            if (_smokeAcc >= 1f)
            {
                _smokeAcc -= 1f;
                float s = Mathf.Max(0.08f, R * 2.2f);
                CCHeatFx.Wisp(tip + Vector3.up * s * 0.3f, Vector3.up * Random.Range(0.25f, 0.5f) + d * 0.2f, s,
                              new Color(0.55f, 0.53f, 0.52f, 0.22f), Random.Range(0.9f, 1.4f));
            }
        }

        private void BuildSparks()
        {
            var go = new GameObject("Flame Sparks") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule m = ps.main;
            m.loop = true; m.playOnAwake = true; m.startSpeed = 0f;
            m.gravityModifier = -0.15f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Shape;
            m.maxParticles = 80;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule e = ps.emission; e.enabled = true; e.rateOverTime = 0f;
            ParticleSystem.ShapeModule sh = ps.shape; sh.enabled = false;
            ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
            sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            ParticleSystem.NoiseModule nz = ps.noise; nz.enabled = true; nz.strength = 0.6f; nz.frequency = 2f;
            ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Stretch;
            pr.lengthScale = 1.5f; pr.velocityScale = 0.06f;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s != null)
            {
                _sparkMat = new Material(s) { name = "Flame Sparks (auto)", hideFlags = HideFlags.DontSave };
                if (_sparkMat.HasProperty("_Surface")) _sparkMat.SetFloat("_Surface", 1f);
                if (_sparkMat.HasProperty("_Blend")) _sparkMat.SetFloat("_Blend", 2f);
                if (_sparkMat.HasProperty("_SrcBlend")) _sparkMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (_sparkMat.HasProperty("_DstBlend")) _sparkMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                if (_sparkMat.HasProperty("_ZWrite")) _sparkMat.SetFloat("_ZWrite", 0f);
                _sparkMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _sparkMat.renderQueue = 3005;
                _sparkMat.mainTexture = CCHeatFx.WispTexture();
                if (_sparkMat.HasProperty("_BaseMap")) _sparkMat.SetTexture("_BaseMap", CCHeatFx.WispTexture());
                pr.sharedMaterial = _sparkMat;
            }
            else pr.sharedMaterial = CCRuntimeParticles.SharedMaterial;
            go.SetActive(true);
            ps.Play();
            _sparks = ps;
        }

        private void SetVisible(bool v)
        {
            if (_rends == null) return;
            for (int i = 0; i < _rends.Length; i++) if (_rends[i] != null && _rends[i].enabled != v) _rends[i].enabled = v;
        }

        /// <summary>Единичный конус вдоль +Z: у сопла узкий, раздувается к трети, к кончику сходится в точку. uv.y — доля длины.</summary>
        private static Mesh BuildCone(int rings, int seg)
        {
            int vc = (rings + 1) * (seg + 1);
            var v = new Vector3[vc];
            var n = new Vector3[vc];
            var uv = new Vector2[vc];
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                float rad = Profile(t);
                float dr = (Profile(Mathf.Min(1f, t + 0.01f)) - Profile(Mathf.Max(0f, t - 0.01f))) / 0.02f;
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    int i = r * (seg + 1) + s;
                    v[i] = new Vector3(ca * rad, sa * rad, t);
                    n[i] = new Vector3(ca, sa, -dr).normalized;
                    uv[i] = new Vector2(s / (float)seg, t);
                }
            }
            var tri = new int[rings * seg * 6];
            int k = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a0 = r * (seg + 1) + s, a1 = a0 + 1, b0 = a0 + seg + 1, b1 = b0 + 1;
                    // лицом наружу
                    tri[k++] = a0; tri[k++] = a1; tri[k++] = b0;
                    tri[k++] = a1; tri[k++] = b1; tri[k++] = b0;
                }
            var m = new Mesh { name = "ToonFlameCone", hideFlags = HideFlags.DontSave };
            m.vertices = v; m.normals = n; m.uv = uv; m.triangles = tri;
            m.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(3f, 3f, 1.6f));
            return m;
        }

        private static float Profile(float t)
        {
            float swell = 0.32f + 0.68f * Mathf.Sqrt(Mathf.Clamp01(t / 0.38f));
            float taper = Mathf.Pow(1f - Mathf.SmoothStep(0.3f, 1f, t), 0.7f);
            return swell * taper;
        }

        private void OnDestroy()
        {
            if (_mats != null) foreach (Material m in _mats) if (m != null) CCHeatFx.Kill(m);
            if (_sparkMat != null) CCHeatFx.Kill(_sparkMat);
        }
    }
}
