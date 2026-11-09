using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Level
{
    /// <summary>
    /// Сборщик простых мультяшных мешей из примитивов (коробки, эллипсоиды, цилиндры, полосы) с цветом в вершинах.
    /// Для входа в метро и небесных пропсов. Грани плоские (у каждой — свои вершины), обход по часовой (лицевая сторона Unity).
    /// </summary>
    public sealed class CityToonMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();
        public Matrix4x4 M = Matrix4x4.identity;

        /// <summary>Четырёхугольник a-b-c-d по часовой, если смотреть на лицевую сторону.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            Quad(a, b, c, d, col, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); c = M.MultiplyPoint3x4(c); d = M.MultiplyPoint3x4(d);
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c - a, d - a);
            n.Normalize();
            int s = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            for (int i = 0; i < 4; i++) { N.Add(n); C.Add(col); }
            UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
            T.Add(s); T.Add(s + 1); T.Add(s + 2); T.Add(s); T.Add(s + 2); T.Add(s + 3);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); c = M.MultiplyPoint3x4(c);
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int s = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            for (int i = 0; i < 3; i++) { N.Add(n); C.Add(col); UV.Add(Vector2.zero); }
            T.Add(s); T.Add(s + 1); T.Add(s + 2);
        }

        /// <summary>Коробка по центру и размеру (в осях текущей матрицы M).</summary>
        public void Box(Vector3 c, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            float x0 = c.x - h.x, x1 = c.x + h.x, y0 = c.y - h.y, y1 = c.y + h.y, z0 = c.z - h.z, z1 = c.z + h.z;
            Quad(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), col);   // +Z
            Quad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), col);   // −Z
            Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), col);   // +X
            Quad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), col);   // −X
            Quad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), col);   // верх
            Quad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), col);   // низ
        }

        /// <summary>Коробка между двумя точками (балка/трос) толщиной t.</summary>
        public void Beam(Vector3 a, Vector3 b, float t, Color col)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            Matrix4x4 keep = M;
            M = keep * Matrix4x4.TRS((a + b) * 0.5f, Quaternion.LookRotation(d / len, Mathf.Abs(d.y / len) > 0.95f ? Vector3.forward : Vector3.up), Vector3.one);
            Box(Vector3.zero, new Vector3(t, t, len), col);
            M = keep;
        }

        /// <summary>
        /// Эллипсоид (или тело вращения) вокруг оси Z: радиусы rx, ry, длина по Z — rz. colorAt(широта 0..1 спереди-назад,
        /// долгота 0..1) — цвет грани (полосы). radiusAt(t 0..1 по длине) — множитель радиуса (капля, сигара).
        /// </summary>
        public void Ellipsoid(Vector3 c, float rx, float ry, float rz, int lat, int lon, System.Func<float, float, Color> colorAt, System.Func<float, float> radiusAt)
        {
            for (int i = 0; i < lat; i++)
            {
                float t0 = (float)i / lat, t1 = (float)(i + 1) / lat;
                for (int j = 0; j < lon; j++)
                {
                    float u0 = (float)j / lon, u1 = (float)(j + 1) / lon;
                    Vector3 a = P(c, rx, ry, rz, t0, u0, radiusAt), b = P(c, rx, ry, rz, t1, u0, radiusAt);
                    Vector3 cc = P(c, rx, ry, rz, t1, u1, radiusAt), d = P(c, rx, ry, rz, t0, u1, radiusAt);
                    Color col = colorAt != null ? colorAt((t0 + t1) * 0.5f, (u0 + u1) * 0.5f) : Color.white;
                    if (i == 0) Tri(a, b, cc, col);                  // нос: a == d
                    else if (i == lat - 1) Tri(a, b, d, col);        // хвост: b == cc
                    else Quad(a, b, cc, d, col);
                }
            }
        }

        private static Vector3 P(Vector3 c, float rx, float ry, float rz, float t, float u, System.Func<float, float> radiusAt)
        {
            float th = t * Mathf.PI;                       // 0 — нос (+Z), π — хвост (−Z)
            float ph = u * 2f * Mathf.PI;
            float k = Mathf.Sin(th) * (radiusAt != null ? radiusAt(t) : 1f);
            return c + new Vector3(Mathf.Cos(ph) * rx * k, Mathf.Sin(ph) * ry * k, Mathf.Cos(th) * rz);
        }

        /// <summary>Цилиндр по оси Y (столб), n граней, с крышками.</summary>
        public void Cylinder(Vector3 bottom, float r, float h, int n, Color col)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i * 2f * Mathf.PI / n, a1 = (i + 1) * 2f * Mathf.PI / n;
                Vector3 p0 = bottom + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r), p1 = bottom + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
                Vector3 up = Vector3.up * h;
                Quad(p0, p0 + up, p1 + up, p1, col);
                Tri(bottom + up, p1 + up, p0 + up, col);
                Tri(bottom, p0, p1, col);
            }
        }

        /// <summary>Диск (кружок) лицом по нормали n.</summary>
        public void Disc(Vector3 c, Vector3 n, float r, int seg, Color col)
        {
            Vector3 a = Vector3.Cross(n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 b = Vector3.Cross(n, a).normalized;
            for (int i = 0; i < seg; i++)
            {
                float t0 = i * 2f * Mathf.PI / seg, t1 = (i + 1) * 2f * Mathf.PI / seg;
                Vector3 p0 = c + (a * Mathf.Cos(t0) + b * Mathf.Sin(t0)) * r, p1 = c + (a * Mathf.Cos(t1) + b * Mathf.Sin(t1)) * r;
                Vector3 nn = Vector3.Cross(p0 - c, p1 - c);
                if (Vector3.Dot(nn, n) > 0f) Tri(c, p0, p1, col); else Tri(c, p1, p0, col);
            }
        }

        public Mesh Build(string name)
        {
            Mesh m = new Mesh { name = name };
            m.indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(V); m.SetNormals(N); m.SetColors(C); m.SetUVs(0, UV);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Создать объект с мешем и материалом (тени выключены — фон и мелочь).</summary>
        public static GameObject Make(string name, Transform parent, Mesh mesh, Material mat, bool shadows)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = shadows;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }
    }
}
