using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// СТЕКЛО ЗА СТЕНОЙ. У части моделей (папка middle) стекло — большой лист позади фасада, а окна — проёмы
    /// в стене перед ним. Шейдер видел один огромный кусок стекла и делил его сеткой → окна горели «кубиками».
    /// Здесь лист стекла режется по проёмам: стена, что стоит прямо перед стеклом, растеризуется на его плоскость,
    /// открытые места = окна, каждое окно становится своим прямоугольником стекла (в той же плоскости, за стеной).
    /// Обычные стёкла (кусок на окно: перед ними пусто, позади — стена) не трогаются.
    /// </summary>
    public static class CityGlassOpenings
    {
        public struct Wall
        {
            public Vector3[] V;
            public int[] T;
            public Matrix4x4 ToGlass;     // из осей стены в оси меша стекла
        }

        private const int Res = 256;

        /// <summary>Порезать стекло по проёмам. null — резать нечего (оставить как есть).</summary>
        public static Mesh Split(Mesh src, Vector3 up, List<Wall> walls, out int planesCut, out int windows)
        {
            planesCut = 0; windows = 0;
            if (walls == null || walls.Count == 0) return null;
            Vector3[] v = src.vertices;
            Vector3[] nr = src.normals;
            if (v == null || v.Length == 0) return null;
            List<int> tri = new List<int>();
            for (int s = 0; s < src.subMeshCount; s++) tri.AddRange(src.GetTriangles(s));
            int tc = tri.Count / 3;
            if (tc == 0 || tc > 20000) return null;

            // стены — в оси стекла один раз
            List<Vector3[]> wv = new List<Vector3[]>();
            foreach (Wall w in walls)
            {
                Vector3[] p = new Vector3[w.V.Length];
                for (int i = 0; i < p.Length; i++) p[i] = w.ToGlass.MultiplyPoint3x4(w.V[i]);
                wv.Add(p);
            }

            float size = Mathf.Max(src.bounds.size.magnitude, 1e-3f);
            Vector3[] tn = new Vector3[tc];
            float[] td = new float[tc];
            for (int t = 0; t < tc; t++)
            {
                Vector3 a = v[tri[t * 3]], b = v[tri[t * 3 + 1]], c = v[tri[t * 3 + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                tn[t] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.zero;
                td[t] = Vector3.Dot(tn[t], a);
            }

            int[] grp = new int[tc];
            for (int t = 0; t < tc; t++) grp[t] = -1;
            List<List<int>> groups = new List<List<int>>();
            for (int t = 0; t < tc; t++)
            {
                if (grp[t] >= 0 || tn[t] == Vector3.zero) continue;
                List<int> g = new List<int>();
                for (int k = t; k < tc; k++)
                {
                    if (grp[k] >= 0 || tn[k] == Vector3.zero) continue;
                    if (Vector3.Dot(tn[k], tn[t]) < 0.95f || Mathf.Abs(td[k] - td[t]) > size * 0.003f) continue;
                    grp[k] = groups.Count; g.Add(k);
                }
                groups.Add(g);
            }

            List<Vector3> ov = new List<Vector3>();
            List<Vector3> on = new List<Vector3>();
            List<int> ot = new List<int>();
            bool any = false;
            foreach (List<int> g in groups)
            {
                List<Rect> rects;
                Vector3 n, u, w2, nrmAvg; float d0;
                if (TryCut(v, nr, tri, g, tn, td, up, wv, walls, out rects, out n, out u, out w2, out d0, out nrmAvg))
                {
                    any = true; planesCut++; windows += rects.Count;
                    foreach (Rect r in rects)
                    {
                        int b = ov.Count;
                        ov.Add(u * r.xMin + w2 * r.yMin + n * d0);
                        ov.Add(u * r.xMax + w2 * r.yMin + n * d0);
                        ov.Add(u * r.xMax + w2 * r.yMax + n * d0);
                        ov.Add(u * r.xMin + w2 * r.yMax + n * d0);
                        for (int k = 0; k < 4; k++) on.Add(nrmAvg);
                        // обход как у исходных треугольников (нормаль по векторному произведению = n)
                        ot.Add(b); ot.Add(b + 1); ot.Add(b + 3);
                        ot.Add(b + 1); ot.Add(b + 2); ot.Add(b + 3);
                    }
                }
                else
                {
                    foreach (int t in g)
                        for (int k = 0; k < 3; k++)
                        {
                            int vi = tri[t * 3 + k];
                            ot.Add(ov.Count); ov.Add(v[vi]); on.Add(nr != null && nr.Length == v.Length ? nr[vi] : tn[t]);
                        }
                }
            }
            if (!any) return null;
            Mesh m = new Mesh();
            m.name = src.name;
            if (ov.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(ov);
            m.SetNormals(on);
            m.SetTriangles(ot, 0);
            m.RecalculateBounds();
            return m;
        }

        private static bool TryCut(Vector3[] v, Vector3[] nr, List<int> tri, List<int> g, Vector3[] tn, float[] td, Vector3 up,
                                   List<Vector3[]> wv, List<Wall> walls, out List<Rect> rects,
                                   out Vector3 n, out Vector3 u, out Vector3 w2, out float d0, out Vector3 nrmAvg)
        {
            rects = null; u = w2 = nrmAvg = Vector3.zero; d0 = 0f;
            n = Vector3.zero;
            foreach (int t in g) n += tn[t];
            if (n.sqrMagnitude < 1e-8f) return false;
            n.Normalize();
            Vector3 upc = Mathf.Abs(Vector3.Dot(n, up)) < 0.9f ? up : Vector3.Cross(n, Vector3.right).normalized;
            u = Vector3.Cross(upc, n).normalized;
            w2 = Vector3.Cross(n, u);
            float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
            nrmAvg = Vector3.zero;
            foreach (int t in g)
                for (int k = 0; k < 3; k++)
                {
                    int vi = tri[t * 3 + k];
                    Vector3 p = v[vi];
                    float a = Vector3.Dot(p, u), b = Vector3.Dot(p, w2);
                    a0 = Mathf.Min(a0, a); a1 = Mathf.Max(a1, a); b0 = Mathf.Min(b0, b); b1 = Mathf.Max(b1, b);
                    d0 += Vector3.Dot(p, n);
                    nrmAvg += nr != null && nr.Length == v.Length ? nr[vi] : n;
                }
            d0 /= g.Count * 3;
            nrmAvg = nrmAvg.sqrMagnitude > 1e-8f ? nrmAvg.normalized : n;
            float ext = Mathf.Max(a1 - a0, b1 - b0);
            if (ext < 1e-4f) return false;
            float px = ext / Res;
            int r0 = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / px) + 1), r1 = Mathf.Max(1, Mathf.CeilToInt((b1 - b0) / px) + 1);
            if (r0 < 8 || r1 < 8) return false;

            bool[] gm = new bool[r0 * r1];
            foreach (int t in g)
                Raster(gm, r0, r1, To2(v[tri[t * 3]], u, w2, a0, b0, px), To2(v[tri[t * 3 + 1]], u, w2, a0, b0, px), To2(v[tri[t * 3 + 2]], u, w2, a0, b0, px));
            int gCount = 0;
            for (int i = 0; i < gm.Length; i++) if (gm[i]) gCount++;
            if (gCount < 64) return false;

            float depth = 0.06f * ext;
            bool[] best = null; float bestCov = -1f;
            for (int side = 1; side >= -1; side -= 2)
            {
                bool[] cm = new bool[r0 * r1];
                for (int wi = 0; wi < walls.Count; wi++)
                {
                    Vector3[] p = wv[wi]; int[] T = walls[wi].T;
                    for (int t = 0; t + 2 < T.Length; t += 3)
                    {
                        Vector3 A = p[T[t]], B = p[T[t + 1]], C = p[T[t + 2]];
                        float da = (Vector3.Dot(A, n) - d0) * side, db = (Vector3.Dot(B, n) - d0) * side, dc = (Vector3.Dot(C, n) - d0) * side;
                        float lo = -0.02f * depth;
                        if (da < lo || db < lo || dc < lo || da > depth || db > depth || dc > depth) continue;
                        Raster(cm, r0, r1, To2(A, u, w2, a0, b0, px), To2(B, u, w2, a0, b0, px), To2(C, u, w2, a0, b0, px));
                    }
                }
                int c = 0;
                for (int i = 0; i < gm.Length; i++) if (gm[i] && cm[i]) c++;
                float cov = c / (float)gCount;
                // проёмы есть, только если стена перед стеклом закрывает его частично
                if (cov < 0.15f || cov > 0.92f) continue;
                if (best == null || Mathf.Abs(cov - 0.6f) < Mathf.Abs(bestCov - 0.6f)) { best = cm; bestCov = cov; }
            }
            if (best == null) return false;

            // открытые места = стекло без стены → связные области
            int[] lab = new int[r0 * r1];
            List<Rect> found = new List<Rect>();
            Stack<int> st = new Stack<int>();
            int next = 1;
            for (int i = 0; i < lab.Length; i++)
            {
                if (lab[i] != 0 || !gm[i] || best[i]) continue;
                int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
                st.Push(i); lab[i] = next;
                while (st.Count > 0)
                {
                    int q = st.Pop();
                    int x = q % r0, y = q / r0;
                    if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                    if (x > 0) Push(q - 1, gm, best, lab, st, next);
                    if (x < r0 - 1) Push(q + 1, gm, best, lab, st, next);
                    if (y > 0) Push(q - r0, gm, best, lab, st, next);
                    if (y < r1 - 1) Push(q + r0, gm, best, lab, st, next);
                }
                next++;
                int w = x1 - x0 + 1, h = y1 - y0 + 1;
                if (w < 3 || h < 3) continue;
                if (w * h > 0.3f * r0 * r1) continue;
                // прямоугольник проёма на плоскости стекла (+1 пиксель запаса — край прячется за стеной)
                found.Add(Rect.MinMaxRect(a0 + (x0 - 1) * px, b0 + (y0 - 1) * px, a0 + (x1 + 2) * px, b0 + (y1 + 2) * px));
            }
            if (found.Count < 2) return false;
            rects = found;
            return true;
        }

        private static void Push(int q, bool[] gm, bool[] cm, int[] lab, Stack<int> st, int id)
        {
            if (lab[q] != 0 || !gm[q] || cm[q]) return;
            lab[q] = id; st.Push(q);
        }

        private static Vector2 To2(Vector3 p, Vector3 u, Vector3 w, float a0, float b0, float px)
        {
            return new Vector2((Vector3.Dot(p, u) - a0) / px, (Vector3.Dot(p, w) - b0) / px);
        }

        /// <summary>Закрасить треугольник (в пикселях). Ребром к плоскости (тонкая полоска) — закрасить его рёбра.</summary>
        private static void Raster(bool[] m, int r0, int r1, Vector2 a, Vector2 b, Vector2 c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(r0 - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int y1 = Mathf.Min(r1 - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            if (x1 < x0 || y1 < y0) return;
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-6f)
            {
                Line(m, r0, r1, a, b); Line(m, r0, r1, b, c); Line(m, r0, r1, c, a);
                return;
            }
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float l1 = ((b.y - c.y) * (px - c.x) + (c.x - b.x) * (py - c.y)) / d;
                    float l2 = ((c.y - a.y) * (px - c.x) + (a.x - c.x) * (py - c.y)) / d;
                    float l3 = 1f - l1 - l2;
                    if (l1 >= -1e-4f && l2 >= -1e-4f && l3 >= -1e-4f) m[y * r0 + x] = true;
                }
        }

        private static void Line(bool[] m, int r0, int r1, Vector2 a, Vector2 b)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)) * 2f));
            for (int i = 0; i <= n; i++)
            {
                Vector2 q = Vector2.Lerp(a, b, i / (float)n);
                int x = Mathf.FloorToInt(q.x), y = Mathf.FloorToInt(q.y);
                if (x >= 0 && x < r0 && y >= 0 && y < r1) m[y * r0 + x] = true;
            }
        }
    }
}
