using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ФЕЙК-ЭТАЖИ под рабочим этажом — «картинки»: кольцо плоских стен по контуру дома с фотографией фасадов
    /// подвала (снимает генератор этажа кнопкой). Один меш, один материал, один вызов отрисовки на всю стопку.
    /// Этажей столько, сколько Floors — растёт с прогрессом: SetFloors(n) / Grow(1).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CityFakeFloors : MonoBehaviour
    {
        [Tooltip("Сколько фейковых этажей под рабочим (растёт с прогрессом уровня)")]
        [Range(0, 60)] public int Floors = 6;
        [Tooltip("Материал с атласом фасадов (ставит генератор)")]
        public Material Material;

        [Header("Данные (заполняет генератор)")]
        [HideInInspector] public float TopY;           // верх стопки (низ подвала), локально
        [HideInInspector] public float FloorH = 8f;    // высота этажа
        [HideInInspector] public List<Vector4> Edges = new List<Vector4>();   // x0, z0, x1, z1 (локально, слева → направо, если смотреть снаружи)
        [HideInInspector] public List<Vector4> EdgeUV = new List<Vector4>();  // u0, u1 (в атласе), v0, v1 (строка фасада в атласе)
        [HideInInspector] public List<Vector3> EdgeN = new List<Vector3>();   // нормаль наружу

        private Mesh _mesh;
        private int _built = -1, _dataHash;

        /// <summary>Сколько этажей (с прогрессом уровня).</summary>
        public void SetFloors(int n) { Floors = Mathf.Max(0, n); }
        public void Grow(int add) { SetFloors(Floors + add); }

        private void OnEnable() { _built = -1; Rebuild(); }
        private void OnValidate() { _built = -1; }

        private void OnDisable()
        {
            if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); _mesh = null; }
        }

        private void Update()
        {
            int h = Edges.Count * 31 + EdgeUV.Count + Mathf.RoundToInt(TopY * 100f) + Mathf.RoundToInt(FloorH * 100f);
            if (_built != Floors || h != _dataHash) Rebuild();
        }

        public void Rebuild()
        {
            _built = Floors;
            _dataHash = Edges.Count * 31 + EdgeUV.Count + Mathf.RoundToInt(TopY * 100f) + Mathf.RoundToInt(FloorH * 100f);
            MeshFilter mf = GetComponent<MeshFilter>();
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (Material != null && mr.sharedMaterial != Material) mr.sharedMaterial = Material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (_mesh == null) { _mesh = new Mesh { name = "CC_FakeFloors", hideFlags = HideFlags.DontSave }; _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
            _mesh.Clear();
            int n = Mathf.Min(Edges.Count, Mathf.Min(EdgeUV.Count, EdgeN.Count));
            if (n == 0 || Floors <= 0) { mf.sharedMesh = _mesh; return; }
            List<Vector3> v = new List<Vector3>(n * Floors * 4);
            List<Vector3> nn = new List<Vector3>(n * Floors * 4);
            List<Vector2> uv = new List<Vector2>(n * Floors * 4);
            List<int> t = new List<int>(n * Floors * 6);
            for (int f = 0; f < Floors; f++)
            {
                float y1 = TopY - f * FloorH, y0 = y1 - FloorH;
                for (int i = 0; i < n; i++)
                {
                    Vector4 e = Edges[i], u = EdgeUV[i];
                    Vector3 nrm = EdgeN[i];
                    int b = v.Count;
                    v.Add(new Vector3(e.x, y0, e.y)); v.Add(new Vector3(e.x, y1, e.y));
                    v.Add(new Vector3(e.z, y1, e.w)); v.Add(new Vector3(e.z, y0, e.w));
                    uv.Add(new Vector2(u.x, u.z)); uv.Add(new Vector2(u.x, u.w));
                    uv.Add(new Vector2(u.y, u.w)); uv.Add(new Vector2(u.y, u.z));
                    nn.Add(nrm); nn.Add(nrm); nn.Add(nrm); nn.Add(nrm);
                    t.Add(b); t.Add(b + 1); t.Add(b + 2);
                    t.Add(b); t.Add(b + 2); t.Add(b + 3);
                }
            }
            _mesh.SetVertices(v);
            _mesh.SetNormals(nn);
            _mesh.SetUVs(0, uv);
            _mesh.SetTriangles(t, 0);
            _mesh.RecalculateBounds();
            mf.sharedMesh = _mesh;
        }
    }
}
