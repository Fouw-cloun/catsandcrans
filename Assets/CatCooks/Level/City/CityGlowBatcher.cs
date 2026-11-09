using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Level
{
    /// <summary>
    /// ОПТИМИЗАЦИЯ СВЕТА ПРОПСОВ (только в игре, создаётся сам при загрузке сцены):
    ///   • весь свет пропсов склеивается в куски по клеткам города (а не меш на каждый фонарь) — несколько вызовов отрисовки вместо десятков;
    ///   • днём ночные куски (фонари, ореолы, подсветка щитов, пятна, конусы) выключены совсем — не рисуются и не перерисовывают пиксели;
    ///   • пятна на земле и конусы видны только вблизи: плавно гаснут к FarEnd и дальше выключаются;
    ///   • мигалки антенн и огни площадок — всегда.
    /// В редакторе (не в игре) свет остаётся на каждом пропсе отдельно, как был.
    /// </summary>
    public sealed class CityGlowBatcher : MonoBehaviour
    {
        [Tooltip("Размер клетки склейки, м")]
        public float CellMeters = 80f;
        [Tooltip("Пятна на земле и конусы: начинают гаснуть, м от камеры")]
        public float FarStart = 60f;
        [Tooltip("…и пропадают совсем, м")]
        public float FarEnd = 90f;
        [Tooltip("Ночные куски включаются, когда ночь больше этого (0..1)")]
        [Range(0f, 0.5f)] public float NightOn = 0.06f;

        private sealed class Chunk { public GameObject Go; public int Part; public Vector3 Center; public float Radius; }

        private readonly List<Chunk> _chunks = new List<Chunk>();
        private float _m = 3.5f, _nextCheck;
        private bool _nightShown = true;
        private static readonly int IdFar = Shader.PropertyToID("_CC_GlowFar");

        public static CityGlowBatcher Instance { get; private set; }

        /// <summary>Склеить свет всех пропсов сцены (в игре). Повторный вызов — пересклеить с новыми.</summary>
        public static void Run()
        {
            if (!Application.isPlaying) return;
            if (Instance == null)
            {
                GameObject go = new GameObject("CityGlowBatcher");
                Instance = go.AddComponent<CityGlowBatcher>();
            }
            Instance.Build();
        }

        private sealed class Acc
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<Vector4> P = new List<Vector4>();
            public readonly List<Vector4> Q = new List<Vector4>();
            public readonly List<int> T = new List<int>();
            public Material Mat;
            public int Part;
            public Bounds B;
            public bool HasB;
        }

        public void Build()
        {
            foreach (Chunk c in _chunks) if (c.Go != null) Destroy(c.Go);
            _chunks.Clear();
            _m = CityPropGlow.CityMeter();
            float cell = Mathf.Max(10f, CellMeters * _m);
            Dictionary<long, Acc> acc = new Dictionary<long, Acc>();
            List<Vector3> v = new List<Vector3>(); List<Color> c0 = new List<Color>(); List<Vector2> u = new List<Vector2>();
            List<Vector4> p = new List<Vector4>(), q = new List<Vector4>(); List<int> t = new List<int>();
            int merged = 0;
            foreach (CityPropGlow g in FindObjectsByType<CityPropGlow>(FindObjectsSortMode.None))
            {
                if (!g.isActiveAndEnabled) continue;
                for (int part = 0; part < 3; part++)
                {
                    Transform pt = g.transform.Find(CityPropGlow.PartNames[part]);
                    if (pt == null) continue;
                    MeshFilter mf = pt.GetComponent<MeshFilter>();
                    MeshRenderer mr = pt.GetComponent<MeshRenderer>();
                    if (mf == null || mr == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable || !mr.enabled) continue;
                    Mesh m = mf.sharedMesh;
                    Vector3 wp = pt.position;
                    long key = ((long)Mathf.FloorToInt(wp.x / cell) * 73856093L) ^ ((long)Mathf.FloorToInt(wp.z / cell) * 19349663L) ^ ((long)part << 58) ^ ((long)(mr.sharedMaterial != null ? mr.sharedMaterial.GetInstanceID() : 0) << 40);
                    Acc a;
                    if (!acc.TryGetValue(key, out a) || a.V.Count > 60000) { a = new Acc { Mat = mr.sharedMaterial, Part = part }; acc[key] = a; }
                    v.Clear(); c0.Clear(); u.Clear(); p.Clear(); q.Clear(); t.Clear();
                    m.GetVertices(v); m.GetColors(c0); m.GetUVs(0, u); m.GetUVs(1, p); m.GetUVs(2, q); m.GetTriangles(t, 0);
                    Matrix4x4 w = pt.localToWorldMatrix;
                    int s = a.V.Count;
                    for (int i = 0; i < v.Count; i++)
                    {
                        Vector3 x = w.MultiplyPoint3x4(v[i]);
                        a.V.Add(x);
                        if (!a.HasB) { a.B = new Bounds(x, Vector3.zero); a.HasB = true; } else a.B.Encapsulate(x);
                    }
                    a.C.AddRange(c0); a.U.AddRange(u); a.P.AddRange(p); a.Q.AddRange(q);
                    for (int i = 0; i < t.Count; i++) a.T.Add(t[i] + s);
                    mr.enabled = false;                                             // рисует кусок, а не пропс
                    merged++;
                }
            }
            foreach (Acc a in acc.Values)
            {
                if (a.V.Count == 0) continue;
                Mesh m = new Mesh { name = "GlowChunk" };
                if (a.V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(a.V); m.SetColors(a.C); m.SetUVs(0, a.U); m.SetUVs(1, a.P); m.SetUVs(2, a.Q);
                m.SetTriangles(a.T, 0);
                Bounds b = a.B; b.Expand(5f * _m);                                // ореолы раздуваются в шейдере
                m.bounds = b;
                GameObject go = new GameObject("GlowChunk_" + a.Part);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = a.Mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _chunks.Add(new Chunk { Go = go, Part = a.Part, Center = b.center, Radius = b.extents.magnitude });
            }
            Shader.SetGlobalVector(IdFar, new Vector4(FarStart * _m, FarEnd * _m, 0f, 0f));
            _nightShown = true;
            _nextCheck = 0f;
            Debug.Log("[Город/свет] свет пропсов склеен: " + merged + " кусков пропсов → " + _chunks.Count + " мешей (днём ночные выключены, пятна и конусы — только вблизи)", this);
        }

        private void OnDisable() { Shader.SetGlobalVector(IdFar, Vector4.zero); if (Instance == this) Instance = null; }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.25f;
            float night = Shader.GetGlobalFloat("_CC_Night");
            bool show = _nightShown ? night > NightOn * 0.5f : night > NightOn;    // с запасом, чтобы не мигало на границе
            _nightShown = show;
            Camera cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
            float far = FarEnd * _m;
            foreach (Chunk c in _chunks)
            {
                if (c.Go == null) continue;
                bool on = c.Part == 0 || show;
                if (on && c.Part == 2 && cam != null) on = Vector3.Distance(cp, c.Center) - c.Radius < far;
                if (c.Go.activeSelf != on) c.Go.SetActive(on);
            }
        }
    }
}
