using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// КАПЕЛЬ С КРАЁВ — точки на краях (CityDripEdge + края открытых зонтов), под каждой лучом вниз находим пол.
    /// Капля набухает на краю, срывается, падает с ускорением и даёт колечко на полу. Рисует шейдер Rain Drips
    /// одним вызовом на все точки. Точки пересобираются, только когда что-то сдвинулось/раскрылось.
    /// </summary>
    public class CityRainDrips
    {
        private GraphicsBuffer _buf;
        private int _count;
        private int _sig = int.MinValue;
        private float _check;
        private readonly List<Vector4> _pts = new List<Vector4>();
        private Vector4[] _data = new Vector4[0];
        private static readonly int IdDrips = Shader.PropertyToID("_CC_Drips");

        public int Count { get { return _count; } }

        public void Tick(float dt, float meter, int max, float umbrellaSpacing, LayerMask mask)
        {
            _check -= dt;
            if (_check > 0f && _buf != null) return;
            _check = 0.3f;
            int sig = 17;
            unchecked
            {
                foreach (CityDripEdge e in CityDripEdge.All) if (e != null && e.isActiveAndEnabled) sig = sig * 31 + e.Signature();
                foreach (CityRainBlocker b in CityRainBlocker.All)
                {
                    if (b == null || !b.isActiveAndEnabled || !b.Active) continue;
                    Vector4 k = b.Packed;
                    sig = sig * 31 + Mathf.RoundToInt(k.x * 10f) * 7 + Mathf.RoundToInt(k.y * 10f) * 13 + Mathf.RoundToInt(k.z * 10f) * 17 + Mathf.RoundToInt(k.w * 10f);
                }
                sig = sig * 31 + max + Mathf.RoundToInt(meter * 100f);
            }
            if (sig == _sig && _buf != null) return;
            _sig = sig;
            Rebuild(meter, max, umbrellaSpacing, mask);
        }

        private void Rebuild(float meter, int max, float umbrellaSpacing, LayerMask mask)
        {
            _pts.Clear();
            foreach (CityDripEdge e in CityDripEdge.All)
                if (e != null && e.isActiveAndEnabled && _pts.Count < max) e.Collect(_pts, meter, max);
            // края открытых зонтов: бахрома чуть ниже обода
            foreach (CityRainBlocker b in CityRainBlocker.All)
            {
                if (b == null || !b.isActiveAndEnabled || !b.Active) continue;
                Vector4 k = b.Packed;
                float r = k.w * 0.98f;
                float y = k.y - 0.12f * k.w;
                int n = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * r / Mathf.Max(0.1f, umbrellaSpacing * meter)), 6, 64);
                for (int i = 0; i < n && _pts.Count < max; i++)
                {
                    float a = (i + 0.5f) / n * Mathf.PI * 2f;
                    _pts.Add(new Vector4(k.x + Mathf.Cos(a) * r, y, k.z + Mathf.Sin(a) * r, 1f));
                }
            }

            _count = _pts.Count;
            int need = Mathf.Max(1, _count) * 2;
            if (_data.Length < need) _data = new Vector4[need];
            float maxFall = 40f * meter;
            for (int i = 0; i < _count; i++)
            {
                Vector4 p = _pts[i];
                Vector3 o = new Vector3(p.x, p.y - 0.02f * meter, p.z);
                RaycastHit hit;
                float fall = -maxFall;                                                       // минус — пола нет, без всплеска
                if (Physics.Raycast(o, Vector3.down, out hit, maxFall, mask, QueryTriggerInteraction.Ignore)) fall = hit.distance + 0.02f * meter;
                float seed = Mathf.Repeat(Mathf.Sin(p.x * 12.9898f + p.z * 78.233f + p.y * 37.719f) * 43758.5453f, 1f);
                _data[i * 2] = new Vector4(p.x, p.y, p.z, seed);
                _data[i * 2 + 1] = new Vector4(fall, p.w, 0f, 0f);
            }
            if (_buf == null || _buf.count < need)
            {
                if (_buf != null) _buf.Release();
                _buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(need), 16);
            }
            _buf.SetData(_data, 0, 0, need);
        }

        public void Draw(Material mat, Bounds bounds)
        {
            if (mat == null || _buf == null || _count == 0) return;
            mat.SetBuffer(IdDrips, _buf);
            RenderParams rp = new RenderParams(mat) { worldBounds = bounds, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 12, _count);
        }

        /// <summary>Нарисовать по тем же точкам другим материалом (сосульки): verts — вершин на точку.</summary>
        public void DrawWith(Material mat, Bounds bounds, int verts)
        {
            if (mat == null || _buf == null || _count == 0) return;
            mat.SetBuffer(IdDrips, _buf);
            RenderParams rp = new RenderParams(mat) { worldBounds = bounds, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, verts, _count);
        }

        public void Release()
        {
            if (_buf != null) _buf.Release();
            _buf = null;
            _count = 0;
            _sig = int.MinValue;
        }
    }
}
