using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ОБЪЁМНАЯ СТРУЖКА на поверхности кучки — только визуал.
    ///
    /// Раскидывает тысячи ленточек по поверхности массы (меш горки): чем больше
    /// площадь треугольника, тем больше на нём ленточек. Часть ленточек лежит
    /// в глубине слоя, часть торчит наружу — поэтому кучка объёмная, а не
    /// «картинка на горке».
    ///
    /// Данные ленточек уходят в буфер видеокарты ОДИН раз при перестройке горки.
    /// Каждый кадр — один вызов отрисовки на все ленточки (Graphics.RenderPrimitives),
    /// форму строит шейдер CatCooks/Food Strands.
    ///
    /// Раскладка зависит только от seed — у всех игроков одинаковая.
    /// </summary>
    public sealed class FoodStrandField : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Strand
        {
            public Vector3 Root;
            public Vector3 Dir;
            public Vector3 Nrm;
            public float Len;
            public float Width;
            public float Bend;
            public float Lift;
            public float Shade;
            public float Twist;
            public float Depth;
        }

        private const int Stride = sizeof(float) * 16;

        public struct Settings
        {
            public Vector2 Length;       // м
            public Vector2 Width;        // м
            public float LayerDepth;     // толщина слоя ленточек, м
            public float StickOut;       // 0..1 — доля «торчащих» наружу
            public float Bend;           // максимальный изгиб, радиан
            public float Twist;          // максимальная закрутка, радиан
            public float Arch;           // высота горбика в долях длины
            public int Segments;         // сегментов на ленточку
            public bool Clamp;           // держать ленточки внутри дна тары
            public Vector2 ClampHalf;    // полуразмеры дна, м
        }

        private ComputeBuffer _buffer;
        private Strand[] _data = Array.Empty<Strand>();
        private int _count;
        private Bounds _localBounds;
        private MaterialPropertyBlock _mpb;
        private int _segments = 6;

        private static readonly int StrandsId = Shader.PropertyToID("_Strands");
        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int SegmentsId = Shader.PropertyToID("_Segments");

        public int Count => _count;

        /// <summary>Раскидать count ленточек по поверхности меша (оси меша = оси кучки).</summary>
        public void Build(Mesh surface, int count, Settings st, int seed)
        {
            _count = 0;
            _segments = Mathf.Clamp(st.Segments, 2, 12);
            if (surface == null || count <= 0) return;

            Vector3[] verts = surface.vertices;
            Vector3[] norms = surface.normals;
            int[] tris = surface.triangles;
            if (verts.Length == 0 || tris.Length < 3) return;
            bool hasN = norms != null && norms.Length == verts.Length;

            // Площади треугольников → накопленная сумма для выбора «по площади».
            int triCount = tris.Length / 3;
            float[] cdf = new float[triCount];
            float total = 0f;
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = verts[tris[t * 3]], b = verts[tris[t * 3 + 1]], c = verts[tris[t * 3 + 2]];
                Vector3 fn = Vector3.Cross(b - a, c - a);
                float area = fn.magnitude * 0.5f;
                // Низ и вертикальные закрытия горки — без ленточек (их не видно).
                if (fn.sqrMagnitude > 1e-12f && fn.normalized.y < -0.2f) area = 0f;
                total += area;
                cdf[t] = total;
            }
            if (total <= 0f) return;

            if (_data.Length < count) _data = new Strand[count];
            System.Random rnd = new System.Random(seed);
            Vector3 bmin = Vector3.one * float.MaxValue, bmax = Vector3.one * float.MinValue;

            for (int i = 0; i < count; i++)
            {
                // Треугольник по площади.
                float pick = (float)rnd.NextDouble() * total;
                int lo = 0, hi = triCount - 1;
                while (lo < hi) { int mid = (lo + hi) >> 1; if (cdf[mid] < pick) lo = mid + 1; else hi = mid; }
                int t0 = tris[lo * 3], t1 = tris[lo * 3 + 1], t2 = tris[lo * 3 + 2];

                // Точка в треугольнике.
                float u = (float)rnd.NextDouble(), v = (float)rnd.NextDouble();
                if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                Vector3 p = verts[t0] + (verts[t1] - verts[t0]) * u + (verts[t2] - verts[t0]) * v;
                Vector3 n = hasN
                    ? (norms[t0] * (1f - u - v) + norms[t1] * u + norms[t2] * v).normalized
                    : Vector3.Cross(verts[t1] - verts[t0], verts[t2] - verts[t0]).normalized;
                if (n.sqrMagnitude < 0.5f) n = Vector3.up;

                // Глубина в слое: больше ленточек у поверхности, меньше в глубине.
                float depth = Mathf.Pow((float)rnd.NextDouble(), 1.8f);
                Vector3 root = p - n * (depth * st.LayerDepth);

                // Направление вдоль поверхности, часть — наружу.
                Vector3 rand = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f,
                                           (float)rnd.NextDouble() - 0.5f);
                Vector3 dir = Vector3.ProjectOnPlane(rand, n);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.Cross(n, Vector3.right);
                dir.Normalize();
                bool stick = rnd.NextDouble() < st.StickOut && depth < 0.3f;
                // У стенок тары — ровный край: ленточки не торчат наружу и лежат вдоль стенки.
                bool nearWall = st.Clamp && (Mathf.Abs(p.x) > st.ClampHalf.x * 0.82f || Mathf.Abs(p.z) > st.ClampHalf.y * 0.82f);
                if (nearWall)
                {
                    stick = false;
                    Vector3 along = Mathf.Abs(p.x) / Mathf.Max(1e-4f, st.ClampHalf.x) > Mathf.Abs(p.z) / Mathf.Max(1e-4f, st.ClampHalf.y)
                        ? Vector3.forward : Vector3.right;
                    Vector3 wd = Vector3.ProjectOnPlane(along, n);
                    if (wd.sqrMagnitude > 1e-6f) dir = Vector3.Slerp(dir, wd.normalized, 0.8f).normalized;
                }
                Vector3 nrm = n;
                if (stick)
                {
                    // Торчит: направление приподнято над поверхностью, «горб» меньше.
                    float up = 0.35f + 0.4f * (float)rnd.NextDouble();
                    dir = (dir + n * up).normalized;
                    nrm = Vector3.ProjectOnPlane(n, dir).normalized;
                    if (nrm.sqrMagnitude < 0.5f) nrm = Vector3.Cross(dir, Vector3.Cross(n, dir)).normalized;
                }

                float len = Mathf.Lerp(st.Length.x, st.Length.y, (float)rnd.NextDouble());
                // В таре: ленточка не должна проходить сквозь стенку. Не влезает —
                // укорачиваем; совсем не влезает — ставим вдоль стенки.
                if (st.Clamp)
                {
                    Vector3 half = dir * (len * 0.5f);
                    float ex = Mathf.Abs(p.x) + Mathf.Abs(half.x), ez = Mathf.Abs(p.z) + Mathf.Abs(half.z);
                    float kx = ex > st.ClampHalf.x ? Mathf.Max(0f, st.ClampHalf.x - Mathf.Abs(p.x)) / Mathf.Max(1e-5f, Mathf.Abs(half.x)) : 1f;
                    float kz = ez > st.ClampHalf.y ? Mathf.Max(0f, st.ClampHalf.y - Mathf.Abs(p.z)) / Mathf.Max(1e-5f, Mathf.Abs(half.z)) : 1f;
                    float k = Mathf.Min(kx, kz);
                    if (k < 0.5f)
                    {
                        // Повернуть вдоль ближайшей стенки.
                        Vector3 along = Mathf.Abs(p.x) / st.ClampHalf.x > Mathf.Abs(p.z) / st.ClampHalf.y ? Vector3.forward : Vector3.right;
                        dir = Vector3.ProjectOnPlane(along, n).normalized;
                        nrm = n;
                        k = 0.6f;
                    }
                    len *= Mathf.Clamp01(k);
                }
                // Ленточка начинается не из корня, а «проходит» через точку: сдвиг на полдлины назад.
                root -= dir * (len * 0.5f);

                _data[i] = new Strand
                {
                    Root = root,
                    Dir = dir,
                    Nrm = nrm,
                    Len = len,
                    Width = Mathf.Lerp(st.Width.x, st.Width.y, (float)rnd.NextDouble()),
                    Bend = ((float)rnd.NextDouble() - 0.5f) * 2f * st.Bend,
                    Lift = len * st.Arch * (stick ? 0.3f : (0.4f + 0.6f * (float)rnd.NextDouble())),
                    Shade = 0.88f + 0.22f * (float)rnd.NextDouble(),
                    Twist = ((float)rnd.NextDouble() - 0.5f) * 2f * st.Twist,
                    Depth = depth,
                };

                bmin = Vector3.Min(bmin, root - Vector3.one * len);
                bmax = Vector3.Max(bmax, root + Vector3.one * len * 2f);
            }

            _count = count;
            _localBounds = new Bounds((bmin + bmax) * 0.5f, bmax - bmin);

            if (_buffer == null || _buffer.count < count)
            {
                _buffer?.Release();
                _buffer = new ComputeBuffer(Mathf.NextPowerOfTwo(count), Stride);
            }
            _buffer.SetData(_data, 0, 0, count);
        }

        /// <summary>Нарисовать для одной камеры (или для всех, если camera = null).</summary>
        private static readonly int WidthScaleId = Shader.PropertyToID("_WidthScale");

        /// <summary>
        /// fraction — какую долю ленточек рисовать (дальность: вдали меньше).
        /// Ленточки перемешаны случайно, поэтому «первые N» — равномерная выборка
        /// по всей кучке. Чтобы не было щелей, оставшиеся рисуются шире (как у
        /// систем травы: меньше травинок вдали, но каждая толще).
        /// </summary>
        public void Draw(Material mat, Matrix4x4 frame, Color tint, int layer, bool shadows, Camera camera,
                         float fraction = 1f)
        {
            if (_count == 0 || _buffer == null || mat == null) return;
            int n = Mathf.Clamp(Mathf.CeilToInt(_count * Mathf.Clamp01(fraction)), 1, _count);
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetBuffer(StrandsId, _buffer);
            _mpb.SetMatrix(FrameId, frame);
            _mpb.SetColor(TintId, tint);
            _mpb.SetInt(SegmentsId, _segments);
            _mpb.SetFloat(WidthScaleId, Mathf.Sqrt(_count / (float)n));

            Bounds wb = TransformBounds(frame, _localBounds);
            RenderParams rp = new RenderParams(mat)
            {
                matProps = _mpb,
                layer = layer,
                camera = camera,
                worldBounds = wb,
                shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On
                                            : UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = true,
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, _segments * 6, n);
        }

        public void Clear() => _count = 0;

        public void Dispose()
        {
            _buffer?.Release();
            _buffer = null;
            _count = 0;
        }

        private static Bounds TransformBounds(Matrix4x4 m, Bounds b)
        {
            Vector3 c = m.MultiplyPoint3x4(b.center);
            Vector3 e = b.extents;
            Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0, 0));
            Vector3 ay = m.MultiplyVector(new Vector3(0, e.y, 0));
            Vector3 az = m.MultiplyVector(new Vector3(0, 0, e.z));
            Vector3 ext = new Vector3(
                Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, ext * 2f);
        }
    }
}
