using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// v22: ВИД СТРОЙКИ у каждого игрока (без физики и сети — рисует по BuildSiteState).
    ///   • призрачный шаблон формы (полупрозрачный, светлый край, бегущие полосы «чертежа»);
    ///   • доски каркаса: новая доска прилетает сверху, кружась, и встаёт на своё место с лёгким «пружинящим» стуком;
    ///   • светящиеся точки для гвоздей (пульсируют), гвозди торчат и уходят в доску с каждым ударом молотка.
    ///   • v23.52 (противень для макарон): гнёзда колец светятся, кольцо из трубы влетает и встаёт на место.
    /// </summary>
    public sealed class BuildSiteView : MonoBehaviour
    {
        private BuildManager _m;
        private int _r;                          // v23.52: какая постройка
        private bool _preview;
        private Transform[] _rings, _ringPoints;
        private float[] _ringT;
        private int _shownRings;
        private Transform _ghostRoot;
        private Material _ghostMat, _pointMat;
        private Transform[] _boards;
        private float[] _boardT;                 // 0..1 — анимация прилёта
        private Transform[] _nails, _points;
        private int[] _shownHits;
        private float[] _nailPunch;
        private int _shownBoards;
        private BuildSiteState _s;
        private static Mesh _nailMesh, _cube, _sphere;

        public static BuildSiteView Create(BuildManager m, int recipe, bool preview)
        {
            var go = new GameObject(preview ? "BuildPreview (fx)" : "BuildSite (fx)");
            var v = go.AddComponent<BuildSiteView>();
            v.Init(m, recipe, preview);
            return v;
        }

        private void Init(BuildManager m, int recipe, bool preview)
        {
            _m = m; _r = recipe; _preview = preview;
            Shader gs = Shader.Find("CatCooks/Build Ghost");
            if (gs == null) gs = Shader.Find("Universal Render Pipeline/Unlit");
            _ghostMat = new Material(gs) { hideFlags = HideFlags.DontSave };
            if (_ghostMat.HasProperty("_Color")) _ghostMat.SetColor("_Color", m.GhostColor);
            _pointMat = new Material(gs) { hideFlags = HideFlags.DontSave };
            if (_pointMat.HasProperty("_Color")) _pointMat.SetColor("_Color", m.PointColor);
            if (_pointMat.HasProperty("_StripeAlpha")) _pointMat.SetFloat("_StripeAlpha", 0f);

            _ghostRoot = new GameObject("Ghost").transform;
            _ghostRoot.SetParent(transform, false);
            int n = m.LookCount(_r);
            if (n == 0)
            {
                // нет вида опалубки — призрак-коробка по габариту
                Bounds b = m.LocalBounds(_r);
                AddMesh(_ghostRoot, "GhostBox", Cube, Matrix4x4.TRS(b.center, Quaternion.identity, b.size), _ghostMat);
            }
            for (int i = 0; i < n; i++)
                AddMesh(_ghostRoot, "Ghost" + i, m.LookMesh(_r, i), m.LookMatrix(_r, i), _ghostMat, true);
            if (preview) return;

            int nb = m.BoardsNeeded;
            _boards = new Transform[nb];
            _boardT = new float[nb];
            for (int i = 0; i < nb; i++)
            {
                Vector3 c, sz;
                m.BoardSlot(_r, i, out c, out sz);
                _boards[i] = AddMesh(transform, "Board" + i, Cube, Matrix4x4.TRS(c, Quaternion.identity, sz), m.WoodMat(_r));
                _boards[i].gameObject.SetActive(false);
            }
            _nails = new Transform[BuildManager.NailCount];
            _points = new Transform[BuildManager.NailCount];
            _shownHits = new int[BuildManager.NailCount];
            _nailPunch = new float[BuildManager.NailCount];
            float len = m.NailLength(_r);
            for (int i = 0; i < BuildManager.NailCount; i++)
            {
                Vector3 p, d;
                m.NailSlot(_r, i, out p, out d);
                _points[i] = AddMesh(transform, "NailPoint" + i, Sphere, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * len * 0.5f), _pointMat);
                _points[i].gameObject.SetActive(false);
                _nails[i] = AddMesh(transform, "Nail" + i, NailMesh, Matrix4x4.TRS(p, Quaternion.LookRotation(d), Vector3.one * len), m.NailMat(_r));
                _nails[i].gameObject.SetActive(false);
                _shownHits[i] = -1;
            }
            // v23.52: кольца противня — гнёзда (светящиеся диски) и сами кольца
            int nr = m.RingsNeeded(_r);
            _rings = new Transform[nr];
            _ringPoints = new Transform[nr];
            _ringT = new float[nr];
            Mesh rm = m.RingMesh(_r);
            for (int i = 0; i < nr; i++)
            {
                Vector3 c = m.RingSlot(_r, i);
                float rh = m.RingHeight(_r);
                Bounds rb = rm != null ? rm.bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
                _ringPoints[i] = AddMesh(transform, "RingPoint" + i, Sphere,
                                         Matrix4x4.TRS(c + Vector3.up * rh * 0.15f, Quaternion.identity, new Vector3(rb.size.x * 0.9f, rh * 0.25f, rb.size.z * 0.9f)), _pointMat);
                _ringPoints[i].gameObject.SetActive(false);
                if (rm != null)
                {
                    _rings[i] = AddMesh(transform, "Ring" + i, rm, Matrix4x4.TRS(c, Quaternion.identity, Vector3.one), m.RingMat(_r));
                    _rings[i].gameObject.SetActive(false);
                }
            }
        }

        private static Transform AddMesh(Transform parent, string name, Mesh mesh, Matrix4x4 m, Material mat, bool keepMatrix = false)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = m.GetColumn(3);
            Vector3 f = m.GetColumn(2), u = m.GetColumn(1);
            if (f.sqrMagnitude > 1e-10f && u.sqrMagnitude > 1e-10f) g.transform.localRotation = Quaternion.LookRotation(f, u);
            g.transform.localScale = new Vector3(((Vector3)m.GetColumn(0)).magnitude, u.magnitude, f.magnitude);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            int subs = mesh != null ? Mathf.Max(1, mesh.subMeshCount) : 1;
            var mats = new Material[subs];
            for (int i = 0; i < subs; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        public void ApplyPreview(Vector3 pos, float yaw)
        {
            Matrix4x4 m = _m.SiteMatrix(_r, pos, yaw);
            transform.SetPositionAndRotation(m.GetColumn(3), Quaternion.Euler(0f, yaw, 0f));
        }

        public void Apply(BuildSiteState s)
        {
            _s = s;
            ApplyPreview(s.Pos, s.Yaw);
            if (_preview || _boards == null) return;
            // новые доски — анимация прилёта (по одной, с маленькой задержкой)
            int want = Mathf.Min(s.Boards, _boards.Length);
            for (int i = _shownBoards; i < want; i++)
            {
                _boards[i].gameObject.SetActive(true);
                _boardT[i] = -0.12f * (i - _shownBoards);        // ступенька — «по одной»
            }
            if (want < _shownBoards) for (int i = want; i < _shownBoards; i++) _boards[i].gameObject.SetActive(false);
            _shownBoards = want;

            // v23.52: кольца — новые влетают по одной, гнёзда светятся, пока ждём кольца
            if (_rings != null)
            {
                int wr = Mathf.Min(s.Rings, _rings.Length);
                for (int i = _shownRings; i < wr; i++)
                {
                    if (_rings[i] != null) _rings[i].gameObject.SetActive(true);
                    _ringT[i] = -0.12f * (i - _shownRings);
                }
                if (wr < _shownRings) for (int i = wr; i < _shownRings; i++) if (_rings[i] != null) _rings[i].gameObject.SetActive(false);
                _shownRings = wr;
                for (int i = 0; i < _ringPoints.Length; i++) _ringPoints[i].gameObject.SetActive(s.Stage == BuildManager.StageRings && i >= wr);
            }

            for (int i = 0; i < BuildManager.NailCount; i++)
            {
                bool placed = (s.Nails & (1 << i)) != 0;
                int hits = BuildManager.HitsOf(s, i);
                _nails[i].gameObject.SetActive(placed);
                bool showPoint = (s.Stage == 1 && !placed);
                _points[i].gameObject.SetActive(showPoint);
                if (placed && hits != _shownHits[i])
                {
                    if (_shownHits[i] >= 0 && hits > _shownHits[i]) _nailPunch[i] = 1f;
                    _shownHits[i] = hits;
                }
            }
        }

        private void Update()
        {
            if (_preview || _boards == null) { Pulse(_ghostMat, _m.GhostColor, 0.6f); return; }
            float dt = Time.deltaTime;
            // доски
            for (int i = 0; i < _shownBoards; i++)
            {
                if (_boardT[i] >= 1f) continue;
                _boardT[i] = Mathf.Min(1f, _boardT[i] + dt / 0.45f);
                float t = Mathf.Clamp01(_boardT[i]);
                Vector3 c, sz;
                _m.BoardSlot(_r, i, out c, out sz);
                Bounds b = _m.LocalBounds(_r);
                float drop = (1f - EaseOutBack(t)) * (b.size.y * 4f + 0.25f);
                _boards[i].localPosition = c + Vector3.up * drop;
                _boards[i].localRotation = Quaternion.Euler(0f, (1f - t) * 70f * (i % 2 == 0 ? 1f : -1f), (1f - t) * 25f);
                float sq = t < 1f ? 1f : 1f;
                _boards[i].localScale = sz * sq;
                _boards[i].gameObject.SetActive(_boardT[i] > 0f);
            }
            // v23.52: кольца — падают сверху, крутясь, и садятся в гнездо
            if (_rings != null)
            {
                Bounds rbx = _m.LocalBounds(_r);
                for (int i = 0; i < _shownRings; i++)
                {
                    if (_rings[i] == null || _ringT[i] >= 1f) continue;
                    _ringT[i] = Mathf.Min(1f, _ringT[i] + dt / 0.4f);
                    float t = Mathf.Clamp01(_ringT[i]);
                    Vector3 c = _m.RingSlot(_r, i);
                    float drop = (1f - EaseOutBack(t)) * (rbx.size.y * 4f + 0.3f);
                    _rings[i].localPosition = c + Vector3.up * drop;
                    _rings[i].localRotation = Quaternion.Euler((1f - t) * 50f, (1f - t) * 140f * (i % 2 == 0 ? 1f : -1f), 0f);
                    _rings[i].gameObject.SetActive(_ringT[i] > 0f);
                }
                for (int i = 0; i < _ringPoints.Length; i++)
                {
                    if (!_ringPoints[i].gameObject.activeSelf) continue;
                    float k = 0.85f + 0.15f * Mathf.Sin(Time.time * 5f + i * 0.7f);
                    Vector3 sc = _ringPoints[i].localScale;
                    Mesh rm = _m.RingMesh(_r);
                    float d = rm != null ? rm.bounds.size.x * 0.9f : 0.1f;
                    _ringPoints[i].localScale = new Vector3(d * k, sc.y, d * k);
                }
            }
            // гвозди
            float len = _m.NailLength(_r);
            int hitsMax = Mathf.Max(1, _m.HitsPerNail);
            for (int i = 0; i < BuildManager.NailCount; i++)
            {
                if (_points[i].gameObject.activeSelf)
                {
                    float k = 0.8f + 0.35f * Mathf.Sin(Time.time * 6f + i);
                    _points[i].localScale = Vector3.one * len * 0.5f * k;
                }
                if (!_nails[i].gameObject.activeSelf) continue;
                Vector3 p, d;
                _m.NailSlot(_r, i, out p, out d);
                int h = Mathf.Max(0, _shownHits[i]);
                float stick = Mathf.Lerp(0.85f, 0.04f, h / (float)hitsMax);       // торчит → утоплен
                _nailPunch[i] = Mathf.Max(0f, _nailPunch[i] - dt * 6f);
                float shake = _nailPunch[i] * 0.08f;
                _nails[i].localPosition = p - d * (len * stick) + Random.insideUnitSphere * shake * len;
                _nails[i].localScale = Vector3.one * len;
            }
            Pulse(_ghostMat, _m.GhostColor, (_s.Stage == BuildManager.StageBoards || _s.Stage == BuildManager.StageRings) ? 1f : 0.55f);
        }

        private static void Pulse(Material m, Color c, float k)
        {
            if (m == null || !m.HasProperty("_Color")) return;
            c.a *= k * (0.85f + 0.15f * Mathf.Sin(Time.time * 2.5f));
            m.SetColor("_Color", c);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.4f, c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        public void FinishAndDestroy()
        {
            Destroy(gameObject, 0.15f);       // настоящая опалубка появляется на этом месте
        }

        private void OnDestroy()
        {
            if (_ghostMat != null) Destroy(_ghostMat);
            if (_pointMat != null) Destroy(_pointMat);
        }

        // ───────────────────────── меши ─────────────────────────

        private static Mesh Cube
        {
            get
            {
                if (_cube != null) return _cube;
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cube = g.GetComponent<MeshFilter>().sharedMesh;
                Destroy(g);
                return _cube;
            }
        }

        private static Mesh Sphere
        {
            get
            {
                if (_sphere != null) return _sphere;
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sphere = g.GetComponent<MeshFilter>().sharedMesh;
                Destroy(g);
                return _sphere;
            }
        }

        /// <summary>Гвоздь длиной 1 вдоль +Z: остриё в +Z, шляпка у 0.</summary>
        public static Mesh NailMesh
        {
            get
            {
                if (_nailMesh != null) return _nailMesh;
                const int S = 8;
                float r = 0.045f, hr = 0.13f, ht = 0.035f;
                var v = new System.Collections.Generic.List<Vector3>();
                var tri = new System.Collections.Generic.List<int>();
                // стержень
                for (int i = 0; i < S; i++)
                {
                    float a = i / (float)S * Mathf.PI * 2f;
                    Vector3 o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    v.Add(o * r + Vector3.forward * ht);
                    v.Add(o * r * 0.6f + Vector3.forward * 0.9f);
                }
                int tip = v.Count; v.Add(Vector3.forward * 1f);
                for (int i = 0; i < S; i++)
                {
                    int a0 = i * 2, a1 = a0 + 1, b0 = ((i + 1) % S) * 2, b1 = b0 + 1;
                    tri.Add(a0); tri.Add(b0); tri.Add(a1);
                    tri.Add(a1); tri.Add(b0); tri.Add(b1);
                    tri.Add(a1); tri.Add(b1); tri.Add(tip);
                }
                // шляпка
                int hb = v.Count;
                for (int i = 0; i < S; i++)
                {
                    float a = i / (float)S * Mathf.PI * 2f;
                    Vector3 o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * hr;
                    v.Add(o); v.Add(o + Vector3.forward * ht);
                }
                int c0 = v.Count; v.Add(Vector3.zero);
                for (int i = 0; i < S; i++)
                {
                    int a0 = hb + i * 2, a1 = a0 + 1, b0 = hb + ((i + 1) % S) * 2, b1 = b0 + 1;
                    tri.Add(a0); tri.Add(a1); tri.Add(b0);
                    tri.Add(a1); tri.Add(b1); tri.Add(b0);
                    tri.Add(c0); tri.Add(b0); tri.Add(a0);
                }
                _nailMesh = new Mesh { name = "Nail (auto)" };
                _nailMesh.SetVertices(v);
                _nailMesh.SetTriangles(tri, 0);
                _nailMesh.RecalculateNormals();
                _nailMesh.RecalculateBounds();
                return _nailMesh;
            }
        }
    }
}
