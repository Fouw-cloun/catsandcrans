using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЦЕЛЫЙ КЛУБЕНЬ (картофелина) — процедурный меш для кучек (FoodHeapRenderer, пресет «Целые»):
    /// на лопате, в ведре, в любой таре картошка лежит целыми клубнями, а не стружкой — пока её не растолкли.
    /// Единичный меш (габарит 1×1×1), масштаб — Dims. Одна картофелина = Portions Per порций (как у целой: 6 слайсов).
    /// </summary>
    public static class WholeVegMesh
    {
        public const float PortionsPer = 6f;
        public static float Length => 0.36f * Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
        public static Vector3 Dims => new Vector3(0.78f, 0.64f, 1f) * Length;            // ширина, высота, длина

        private static Mesh _mesh;

        public static Mesh Get()
        {
            if (_mesh != null) return _mesh;
            const int La = 18, Lo = 28;                                  // широта, долгота
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= La; i++)
            {
                float th = Mathf.PI * i / La, st = Mathf.Sin(th), ct = Mathf.Cos(th);
                for (int j = 0; j <= Lo; j++)
                {
                    float ph = Mathf.PI * 2f * j / Lo;
                    Vector3 n = new Vector3(st * Mathf.Cos(ph), ct, st * Mathf.Sin(ph));      // y — вдоль длины
                    // бугристость клубня: пара низких гармоник + мелкая рябь, «глазки» — ямки
                    float lump = 1f + 0.07f * Mathf.Sin(ph * 2f + th * 3f) + 0.05f * Mathf.Sin(ph * 3f - th * 2f + 1.3f) + 0.02f * Mathf.Sin(ph * 7f + th * 6f);
                    float eye = 0f;
                    for (int e = 0; e < 5; e++)
                    {
                        float eth = 0.6f + e * 0.45f, eph = e * 2.3f;
                        Vector3 en = new Vector3(Mathf.Sin(eth) * Mathf.Cos(eph), Mathf.Cos(eth), Mathf.Sin(eth) * Mathf.Sin(eph));
                        float dd = Vector3.Dot(n, en);
                        if (dd > 0.985f) eye += (dd - 0.985f) / 0.015f;
                    }
                    float r = 0.5f * (lump - 0.06f * eye);
                    Vector3 p = n * r;
                    v.Add(new Vector3(p.x, p.z, p.y));                                          // длинная ось → Z (как у ньокка)
                    uv.Add(new Vector2(j / (float)Lo * 2f, i / (float)La));
                }
            }
            for (int i = 0; i < La; i++)
                for (int j = 0; j < Lo; j++)
                {
                    int a = i * (Lo + 1) + j, b = a + Lo + 1;
                    t.Add(a); t.Add(a + 1); t.Add(b); t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            _mesh = new Mesh { name = "WholePotato (auto)" };
            _mesh.SetVertices(v); _mesh.SetUVs(0, uv); _mesh.SetTriangles(t, 0);
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
            // треугольники наружу: проверим одну грань и при нужде перевернём все
            Vector3[] vv = _mesh.vertices; int[] tt = _mesh.triangles;
            int k = (La / 2) * Lo * 6;
            Vector3 c = (vv[tt[k]] + vv[tt[k + 1]] + vv[tt[k + 2]]) / 3f;
            Vector3 fn = Vector3.Cross(vv[tt[k + 1]] - vv[tt[k]], vv[tt[k + 2]] - vv[tt[k]]);
            if (Vector3.Dot(fn, c) < 0f)
            {
                for (int q = 0; q < tt.Length; q += 3) { int x = tt[q + 1]; tt[q + 1] = tt[q + 2]; tt[q + 2] = x; }
                _mesh.triangles = tt; _mesh.RecalculateNormals();
            }
            return _mesh;
        }
    }
}
