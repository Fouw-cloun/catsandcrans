using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Geometry and layout contain no camera-dependent sizing or global random state.
public static class CatCooksFireGeometry
{
    public struct Source
    {
        public Vector3 position;
        public Vector2 radius;
        public float height, phase;
    }

    public static float Hash(int seed)
    {
        unchecked
        {
            uint x = (uint)seed;
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15;
            x *= 0x846ca68bu; x ^= x >> 16;
            return (x & 0xffffffu) / 16777216f;
        }
    }

    public static void Grid(Vector2 area, float spacing, int budget, bool ellipse,
        float height, int seed, List<Source> result)
    {
        result.Clear();
        area.x = Mathf.Max(.1f, area.x); area.y = Mathf.Max(.1f, area.y);
        spacing = Mathf.Max(.12f, spacing); budget = Mathf.Clamp(budget, 1, 128);
        int nx = Mathf.Clamp(Mathf.CeilToInt(area.x / spacing), 1, budget);
        int nz = Mathf.Clamp(Mathf.CeilToInt(area.y / spacing), 1, budget);
        while (nx * nz > budget)
        {
            if (nx > 1 && (nz == 1 || nx / area.x >= nz / area.y)) nx--;
            else nz--;
        }
        float rx = Mathf.Min(area.x * .5f, area.x / nx * .72f);
        float rz = Mathf.Min(area.y * .5f, area.y / nz * .72f);
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            float u = nx == 1 ? 0 : x / (nx - 1f) * 2 - 1;
            float v = nz == 1 ? 0 : z / (nz - 1f) * 2 - 1;
            // A concentric square-to-disc mapping keeps the budget and covers
            // the oval without deleting edge cells or leaving a hole in the middle.
            Vector2 p = new Vector2(u, v);
            if (ellipse && p.sqrMagnitude > .00001f)
            {
                float rr, a;
                if (Mathf.Abs(u) > Mathf.Abs(v)) { rr = u; a = Mathf.PI * .25f * (v / u); }
                else { rr = v; a = Mathf.PI * .5f - Mathf.PI * .25f * (u / v); }
                p = new Vector2(rr * Mathf.Cos(a), rr * Mathf.Sin(a));
            }
            int id = x + z * nx;
            float centre = 1 - Mathf.Clamp01(p.magnitude * .7f);
            Vector2 extent = new Vector2(area.x * .5f - rx, area.y * .5f - rz);
            Vector2 jitter = new Vector2((Hash(seed + id * 211 + 17) * 2 - 1) * Mathf.Min(rx * .18f, extent.x),
                (Hash(seed + id * 307 + 23) * 2 - 1) * Mathf.Min(rz * .18f, extent.y));
            jitter.x *= 1 - Mathf.Abs(p.x); jitter.y *= 1 - Mathf.Abs(p.y);
            result.Add(new Source {
                position = new Vector3(p.x * extent.x + jitter.x, 0, p.y * extent.y + jitter.y),
                radius = new Vector2(rx, rz),
                height = height * (.49f + .25f * centre + .26f * Hash(seed + id * 71)),
                phase = Hash(seed + id * 137) * 60f
            });
        }
    }

    public sealed class Template
    {
        public Vector3[] vertices, normals;
        public Vector2[] uv;
        public int[] triangles;
    }

    // Closed lathed blob. Duplicated pole vertices are harmless; no degenerate
    // pole triangles are submitted. Winding is outward in Unity's convention.
    public static Template MakeTemplate(int rings, int sides, bool flame)
    {
        var verts = new List<Vector3>(); var tex = new List<Vector2>();
        var tris = new List<int>();
        for (int y = 0; y <= rings; y++)
        {
            float t = y / (float)rings;
            // Broad rounded foot, tapering tongue; no rectangular bottom edge.
            float r = flame ? 1.1f * Mathf.Sqrt(Mathf.Clamp01(t / .12f))
                * Mathf.Pow(Mathf.Max(0, 1 - t), .72f) : Mathf.Max(0, Mathf.Sin(Mathf.PI * t));
            // Exact poles avoid tiny negative sine values entering Pow.
            if (y == 0 || y == rings) r = 0;
            for (int j = 0; j <= sides; j++)
            {
                float a = j / (float)sides * Mathf.PI * 2;
                float lobes = flame ? 1 + .075f * Mathf.Sin(a * 3 + t * 5) * Mathf.Sin(t * Mathf.PI)
                    : 1 + .10f * Mathf.Sin(a * 3 + t * 6) * Mathf.Sin(t * Mathf.PI);
                float bend = flame ? .26f * t * t : 0;
                verts.Add(new Vector3(Mathf.Cos(a) * r * lobes + bend,
                    flame ? t : (1 - Mathf.Cos(t * Mathf.PI)) * .5f,
                    Mathf.Sin(a) * r * lobes));
                tex.Add(new Vector2(j / (float)sides, t));
            }
        }
        for (int y = 0; y < rings; y++)
        for (int j = 0; j < sides; j++)
        {
            int a = y * (sides + 1) + j, b = a + sides + 1;
            if (y > 0) { tris.Add(a); tris.Add(b); tris.Add(a + 1); }
            if (y < rings - 1) { tris.Add(a + 1); tris.Add(b); tris.Add(b + 1); }
        }
        var normals = new Vector3[verts.Count];
        for (int i = 0; i < tris.Count; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            normals[a] += n; normals[b] += n; normals[c] += n;
        }
        for (int y = 0; y <= rings; y++)
        {
            int a = y * (sides + 1), b = a + sides;
            Vector3 n = normals[a] + normals[b]; normals[a] = n; normals[b] = n;
            if (y == 0 || y == rings)
                for (int j = 0; j <= sides; j++) normals[a + j] = y == 0 ? Vector3.down : Vector3.up;
        }
        for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;
        return new Template { vertices = verts.ToArray(), normals = normals, uv = tex.ToArray(), triangles = tris.ToArray() };
    }

    public sealed class Buffer
    {
        public readonly Mesh mesh;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Vector4> data = new List<Vector4>();
        readonly List<Vector3> shape = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();

        public Buffer(string name, bool dynamic)
        {
            mesh = new Mesh { name = "CCF3/" + name, hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32 };
            if (dynamic) mesh.MarkDynamic();
        }
        public void Clear()
        {
            vertices.Clear(); normals.Clear(); uv.Clear(); data.Clear();
            shape.Clear(); colors.Clear(); triangles.Clear();
        }

        /// <summary>Сбросить кеш индексов. Нужен, когда меш пересоздают или
        /// меняют шаблоны: иначе Commit решит, что раскладка та же.</summary>
        public void Invalidate() { uploadedIndices = -1; uploadedVerts = -1; }
        public void Add(Template t, Vector3 position, Vector3 scale, Quaternion rotation,
            Vector4 info, Color color)
        {
            int start = vertices.Count;
            scale.x = Mathf.Max(.0001f, scale.x); scale.y = Mathf.Max(.0001f, scale.y);
            scale.z = Mathf.Max(.0001f, scale.z);
            for (int i = 0; i < t.vertices.Length; i++)
            {
                Vector3 p = t.vertices[i];
                vertices.Add(position + rotation * Vector3.Scale(p, scale));
                Vector3 n = t.normals[i];
                normals.Add((rotation * new Vector3(n.x / scale.x, n.y / scale.y, n.z / scale.z)).normalized);
                uv.Add(t.uv[i]); data.Add(info); shape.Add(p); colors.Add(color);
            }
            for (int i = 0; i < t.triangles.Length; i++) triangles.Add(start + t.triangles[i]);
        }
        // Сколько индексов уже залито в меш. Индексы у частиц меняются ТОЛЬКО
        // когда меняется их число, а число меняется редко — на рождении и
        // смерти. В остальных кадрах заливать их заново незачем.
        int uploadedIndices = -1, uploadedVerts = -1;

        /// <summary>
        /// ЗАЛИВКА МЕША. Самое горячее место всего эффекта: зовётся на каждую
        /// пересборку, а пересборок несколько за кадр.
        ///
        /// ═══ ЧТО ИЗМЕНЕНО ═══
        ///
        /// ГРАНИЦЫ НЕ СЧИТАЮТСЯ ПО ВЕРШИНАМ.
        ///
        /// Было: SetTriangles(triangles, 0, true). Флаг true — это
        /// calculateBounds, то есть ещё один полный проход по всем вершинам
        /// ради коробки, которую и так можно задать руками: костёр никуда не
        /// уходит из своего объёма, его размеры известны заранее.
        ///
        /// На пятнадцати тысячах вершин этот проход стоит больше, чем сама
        /// заливка данных.
        ///
        /// ИНДЕКСЫ ЗАЛИВАЮТСЯ ТОЛЬКО КОГДА МЕНЯЮТСЯ.
        ///
        /// Частицы живут секундами, а кадров за это время сотня. Всё это время
        /// треугольники те же самые — меняются только позиции вершин. Заливка
        /// индексов вдобавок тянет за собой проверку каждого из них на выход
        /// за границы буфера, а их тридцать тысяч.
        ///
        /// Clear(false) при этом стирает и индексы, поэтому звать его можно
        /// только вместе с перезаливкой.
        /// </summary>
        public void Commit(Bounds worldBounds, float padding)
        {
            // Сверяем И вершины, И индексы. Совпадения только по индексам
            // мало: набор частиц мог смениться так, что число треугольников
            // случайно совпало при другой раскладке вершин, — и старые
            // индексы стали бы указывать не туда.
            bool sameLayout = triangles.Count == uploadedIndices
                           && vertices.Count == uploadedVerts;

            if (!sameLayout) mesh.Clear(false);

            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
            mesh.SetUVs(1, data); mesh.SetUVs(2, shape); mesh.SetColors(colors);

            if (!sameLayout)
            {
                mesh.SetTriangles(triangles, 0, false);
                uploadedIndices = triangles.Count;
                uploadedVerts = vertices.Count;
            }

            worldBounds.Expand(Mathf.Max(0, padding) * 2);
            mesh.bounds = worldBounds;
        }
    }
}
