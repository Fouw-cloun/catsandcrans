using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ПОИСК ПОЛОСТИ В МОДЕЛИ (ковш экскаватора, ковш бульдозера, корыто кривой формы).
    ///
    /// Как «налить воду в модель» и посмотреть, где она остановится:
    ///   1. сверху вниз бьём сетку лучей по треугольникам модели — первая встреча
    ///      с металлом сверху = «дно» в этой точке (внутри ковша — внутренняя поверхность,
    ///      на стенке — её кромка, мимо модели — пусто);
    ///   2. заливаем «водой» от краёв (алгоритм заполнения низин): вода, которая
    ///      может утечь за край или мимо модели, утекает; остаётся только та, что
    ///      держат стенки, — это и есть полость, а её уровень — край, через который
    ///      ковш начнёт переливаться (самая низкая точка кромки, у ковша — режущая кромка);
    ///   3. из полости: самая глубокая точка (якорь жидкости), горловина, объём,
    ///      ширина воды на каждой высоте (профиль для жидкости) и коробки-коллайдеры
    ///      для дна и стенок.
    ///
    /// Всё в осях «рамки» (frame): её Y — куда смотрит горловина. Работает и в редакторе,
    /// и в игре; меши читаются через sharedMesh (в редакторе Read/Write не обязателен).
    /// </summary>
    public static class CookwareCavityScan
    {
        public sealed class Result
        {
            public bool Ok;
            public string Why;
            public Quaternion Frame = Quaternion.identity;   // мир: Y рамки — вверх полости
            public Vector3 Origin;                            // мир: начало рамки
            public Vector3 Anchor;                            // мир: самая глубокая точка дна полости
            public Vector3 Mouth;                             // мир: центр горловины на уровне перелива
            public float Depth;                               // м: от самой глубокой точки до уровня перелива
            public Vector2 MouthHalf;                         // м: полуразмеры воды на уровне перелива (X, Z рамки)
            public Vector2 MouthCenter;                       // м: центр воды на уровне перелива относительно якоря (X, Z)
            public float Volume;                              // м³ до уровня перелива
            public Vector4[] Profile;                         // по высоте снизу вверх: (полуX, полуZ, центрX, центрZ) от якоря
            public float[] Area;                              // м²: площадь воды на тех же высотах (для круглой — радиус = √(S/π))
            public Vector2 FloorCenter;                       // якорь в осях рамки (X, Z) и
            public float FloorY;                              // высота самой глубокой точки в осях рамки
            public readonly List<Vector3> BoxCenters = new List<Vector3>();   // мир
            public readonly List<Vector3> BoxSizes = new List<Vector3>();     // м, в осях рамки
            public int Wet, Cells;
            public float Cell;
        }

        private struct Tri { public Vector3 A, B, C; }

        /// <summary>Полный разбор с выбранной осью «вверх» (frame.up).</summary>
        public static Result Scan(Transform root, IList<Renderer> renderers, Quaternion frame, int resolution = 64,
                                  int bands = 12, int colliderGrid = 14, float minThickness = 0.02f, int closeGaps = 0)
        {
            var res = new Result { Frame = frame, Origin = root.position };
            Quaternion inv = Quaternion.Inverse(frame);
            var tris = new List<Tri>();
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh m = mf != null ? mf.sharedMesh : null;
                if (m == null) continue;
                Vector3[] v = m.vertices;
                if (v == null || v.Length == 0) continue;
                Matrix4x4 l2w = mf.transform.localToWorldMatrix;
                var q = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    q[i] = inv * (l2w.MultiplyPoint3x4(v[i]) - res.Origin);
                    mn = Vector3.Min(mn, q[i]); mx = Vector3.Max(mx, q[i]);
                }
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    int[] t = m.GetTriangles(s);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                        tris.Add(new Tri { A = q[t[i]], B = q[t[i + 1]], C = q[t[i + 2]] });
                }
            }
            if (tris.Count == 0) { res.Why = "нет треугольников (мешей)"; return res; }

            Vector3 size = mx - mn;
            float longest = Mathf.Max(size.x, size.z);
            if (longest < 1e-4f) { res.Why = "модель плоская сверху"; return res; }
            float cell = longest / Mathf.Max(8, resolution);
            int nx = Mathf.Max(3, Mathf.CeilToInt(size.x / cell) + 2);
            int nz = Mathf.Max(3, Mathf.CeilToInt(size.z / cell) + 2);
            float x0 = mn.x - cell, z0 = mn.z - cell;          // по клетке пустоты вокруг — «мимо модели»
            res.Cell = cell;
            res.Cells = nx * nz;

            // Высоты по клеткам: каждый треугольник обрезаем по квадрату клетки (в 3D, вертикальными
            // плоскостями) — так в клетку попадают и вертикальные стенки, и тонкие кромки,
            // которые луч из центра клетки проскочил бы. top — самая высокая точка металла
            // в клетке (для полости — дно), bot — самая низкая.
            var top = new float[nx * nz];
            var bot = new float[nx * nz];
            var hit = new bool[nx * nz];
            for (int k = 0; k < top.Length; k++) { top[k] = float.NegativeInfinity; bot[k] = float.PositiveInfinity; }
            var poly = new List<Vector3>(12);
            var tmp = new List<Vector3>(12);
            for (int i = 0; i < tris.Count; i++)
            {
                Tri t = tris[i];
                float tx0 = Mathf.Min(t.A.x, Mathf.Min(t.B.x, t.C.x)), tx1 = Mathf.Max(t.A.x, Mathf.Max(t.B.x, t.C.x));
                float tz0 = Mathf.Min(t.A.z, Mathf.Min(t.B.z, t.C.z)), tz1 = Mathf.Max(t.A.z, Mathf.Max(t.B.z, t.C.z));
                int ix0 = Mathf.Clamp(Mathf.FloorToInt((tx0 - x0) / cell), 0, nx - 1);
                int ix1 = Mathf.Clamp(Mathf.FloorToInt((tx1 - x0) / cell), 0, nx - 1);
                int iz0 = Mathf.Clamp(Mathf.FloorToInt((tz0 - z0) / cell), 0, nz - 1);
                int iz1 = Mathf.Clamp(Mathf.FloorToInt((tz1 - z0) / cell), 0, nz - 1);
                for (int z = iz0; z <= iz1; z++)
                    for (int x = ix0; x <= ix1; x++)
                    {
                        float cx0 = x0 + x * cell, cz0 = z0 + z * cell;
                        poly.Clear(); poly.Add(t.A); poly.Add(t.B); poly.Add(t.C);
                        Clip(poly, tmp, 0, cx0, true); if (poly.Count == 0) continue;
                        Clip(poly, tmp, 0, cx0 + cell, false); if (poly.Count == 0) continue;
                        Clip(poly, tmp, 2, cz0, true); if (poly.Count == 0) continue;
                        Clip(poly, tmp, 2, cz0 + cell, false); if (poly.Count == 0) continue;
                        int k = z * nx + x;
                        hit[k] = true;
                        foreach (Vector3 p in poly)
                        {
                            if (p.y > top[k]) top[k] = p.y;
                            if (p.y < bot[k]) bot[k] = p.y;
                        }
                    }
            }

            // Заделать щели: пустая клетка рядом с металлом (зазор между листами, дырка в сетке)
            // считается стенкой высотой с самого высокого соседа — вода через неё не утечёт.
            for (int pass = 0; pass < closeGaps; pass++)
            {
                var add = new List<int>();
                var addTop = new List<float>();
                for (int z = 0; z < nz; z++)
                    for (int x = 0; x < nx; x++)
                    {
                        int k = z * nx + x;
                        if (hit[k] || x == 0 || z == 0 || x == nx - 1 || z == nz - 1) continue;
                        int n = 0; float hi = float.NegativeInfinity, lo = float.PositiveInfinity;
                        for (int dz2 = -1; dz2 <= 1; dz2++)
                            for (int dx2 = -1; dx2 <= 1; dx2++)
                            {
                                int j = (z + dz2) * nx + x + dx2;
                                if (!hit[j]) continue;
                                n++; hi = Mathf.Max(hi, top[j]); lo = Mathf.Min(lo, bot[j]);
                            }
                        // Щель — металл хотя бы с двух сторон (а не край модели снаружи).
                        bool across = (hit[k - 1] && hit[k + 1]) || (hit[k - nx] && hit[k + nx]) ||
                                      (hit[k - nx - 1] && hit[k + nx + 1]) || (hit[k - nx + 1] && hit[k + nx - 1]);
                        if (n >= 2 && across) { add.Add(k); addTop.Add(hi); bot[k] = lo; }
                    }
                for (int i = 0; i < add.Count; i++) { hit[add[i]] = true; top[add[i]] = addTop[i]; }
                if (add.Count == 0) break;
            }

            // Заливка от краёв: уровень воды, которую удержат стенки.
            var level = new float[nx * nz];
            var done = new bool[nx * nz];
            var heap = new MinHeap(nx * nz);
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int k = z * nx + x;
                    bool edge = x == 0 || z == 0 || x == nx - 1 || z == nz - 1;
                    if (!hit[k]) { level[k] = float.NegativeInfinity; done[k] = true; heap.Push(float.NegativeInfinity, k); }
                    else if (edge) { level[k] = top[k]; done[k] = true; heap.Push(top[k], k); }
                }
            int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
            while (heap.Count > 0)
            {
                heap.Pop(out float lv, out int k);
                int x = k % nx, z = k / nx;
                for (int d = 0; d < 4; d++)
                {
                    int ax = x + dx[d], az = z + dz[d];
                    if (ax < 0 || az < 0 || ax >= nx || az >= nz) continue;
                    int j = az * nx + ax;
                    if (done[j]) continue;
                    done[j] = true;
                    level[j] = Mathf.Max(top[j], lv);
                    heap.Push(level[j], j);
                }
            }

            // Мокрые клетки и самая большая «лужа».
            float eps = cell * 0.05f;
            var wet = new bool[nx * nz];
            for (int k = 0; k < wet.Length; k++) wet[k] = hit[k] && level[k] - top[k] > eps;
            var comp = new int[nx * nz];
            for (int k = 0; k < comp.Length; k++) comp[k] = -1;
            int bestComp = -1; float bestVol = 0f;
            var stack = new Stack<int>();
            int compN = 0;
            float area = cell * cell;
            for (int s = 0; s < wet.Length; s++)
            {
                if (!wet[s] || comp[s] >= 0) continue;
                float vol = 0f;
                stack.Push(s); comp[s] = compN;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    vol += (level[k] - top[k]) * area;
                    int x = k % nx, z = k / nx;
                    for (int d = 0; d < 4; d++)
                    {
                        int ax = x + dx[d], az = z + dz[d];
                        if (ax < 0 || az < 0 || ax >= nx || az >= nz) continue;
                        int j = az * nx + ax;
                        if (!wet[j] || comp[j] >= 0) continue;
                        comp[j] = compN; stack.Push(j);
                    }
                }
                if (vol > bestVol) { bestVol = vol; bestComp = compN; }
                compN++;
            }
            if (bestComp < 0) { res.Why = "полость не найдена — вода нигде не держится (ковш стоит горловиной вниз/вбок?)"; return res; }

            // Уровень перелива, глубина, якорь.
            float L = float.NegativeInfinity, floorMin = float.PositiveInfinity;
            int deep = -1;
            float mx0 = float.PositiveInfinity, mx1 = float.NegativeInfinity, mz0 = float.PositiveInfinity, mz1 = float.NegativeInfinity;
            for (int k = 0; k < comp.Length; k++)
            {
                if (comp[k] != bestComp) continue;
                res.Wet++;
                if (level[k] > L) L = level[k];
                if (top[k] < floorMin) { floorMin = top[k]; deep = k; }
                float px = x0 + (k % nx + 0.5f) * cell, pz = z0 + (k / nx + 0.5f) * cell;
                mx0 = Mathf.Min(mx0, px); mx1 = Mathf.Max(mx1, px); mz0 = Mathf.Min(mz0, pz); mz1 = Mathf.Max(mz1, pz);
            }
            // Якорь — среди почти самых глубоких клеток та, что ближе к середине воды
            // (у плоского дна корыта иначе якорь уехал бы в угол).
            {
                float cxm = (mx0 + mx1) * 0.5f, czm = (mz0 + mz1) * 0.5f;
                float tolD = Mathf.Max(cell * 0.5f, (L - floorMin) * 0.04f), bestD = float.MaxValue;
                for (int k = 0; k < comp.Length; k++)
                {
                    if (comp[k] != bestComp || top[k] > floorMin + tolD) continue;
                    float px = x0 + (k % nx + 0.5f) * cell, pz = z0 + (k / nx + 0.5f) * cell;
                    float d2 = (px - cxm) * (px - cxm) + (pz - czm) * (pz - czm);
                    if (d2 < bestD) { bestD = d2; deep = k; }
                }
                floorMin = top[deep];
            }
            float ax0 = x0 + (deep % nx + 0.5f) * cell, az0 = z0 + (deep / nx + 0.5f) * cell;
            res.Depth = L - floorMin;
            res.Volume = bestVol;
            res.Anchor = res.Origin + frame * new Vector3(ax0, floorMin, az0);
            res.MouthHalf = new Vector2((mx1 - mx0) * 0.5f + cell * 0.5f, (mz1 - mz0) * 0.5f + cell * 0.5f);
            res.MouthCenter = new Vector2((mx0 + mx1) * 0.5f - ax0, (mz0 + mz1) * 0.5f - az0);
            res.Mouth = res.Origin + frame * new Vector3((mx0 + mx1) * 0.5f, L, (mz0 + mz1) * 0.5f);

            // Профиль воды по высоте.
            bands = Mathf.Max(2, bands);
            res.Profile = new Vector4[bands];
            res.Area = new float[bands];
            res.FloorCenter = new Vector2(ax0, az0);
            res.FloorY = floorMin;
            for (int b = 0; b < bands; b++)
            {
                float y = floorMin + Mathf.Max(res.Depth * 0.03f, res.Depth * b / (bands - 1));
                float bx0 = float.PositiveInfinity, bx1 = float.NegativeInfinity, bz0 = float.PositiveInfinity, bz1 = float.NegativeInfinity;
                for (int k = 0; k < comp.Length; k++)
                {
                    if (comp[k] != bestComp || top[k] >= y) continue;
                    res.Area[b] += cell * cell;
                    float px = x0 + (k % nx + 0.5f) * cell, pz = z0 + (k / nx + 0.5f) * cell;
                    bx0 = Mathf.Min(bx0, px); bx1 = Mathf.Max(bx1, px); bz0 = Mathf.Min(bz0, pz); bz1 = Mathf.Max(bz1, pz);
                }
                if (bx0 > bx1) { bx0 = bx1 = ax0; bz0 = bz1 = az0; }
                res.Profile[b] = new Vector4((bx1 - bx0) * 0.5f + cell * 0.5f, (bz1 - bz0) * 0.5f + cell * 0.5f,
                                             (bx0 + bx1) * 0.5f - ax0, (bz0 + bz1) * 0.5f - az0);
            }

            BuildBoxes(res, nx, nz, x0, z0, cell, hit, top, bot, comp, bestComp, colliderGrid, minThickness);
            res.Ok = true;
            return res;
        }

        /// <summary>Подобрать, какая ось корня — «вверх» полости: где воды больше, туда и смотрит горловина.</summary>
        public static Quaternion BestFrame(Transform root, IList<Renderer> renderers, out string axisName, out float volume)
            => BestFrame(root, renderers, 0, out axisName, out volume);

        /// <summary>Рамка с осью «вверх» = заданная ось корня (локально), вперёд — по возможности вперёд корня.</summary>
        public static Quaternion FrameFor(Transform root, Vector3 localUp)
        {
            Vector3 up = root.TransformDirection(localUp).normalized;
            Vector3 fwd = Mathf.Abs(Vector3.Dot(up, root.forward)) < 0.9f ? root.forward : root.right;
            return Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, up).normalized, up);
        }

        public static Quaternion BestFrame(Transform root, IList<Renderer> renderers, int closeGaps, out string axisName, out float volume)
        {
            // Горловина ковша часто смотрит НАИСКОСОК к осям модели. Поэтому: 26 направлений
            // (оси, диагонали граней и углов куба корня), потом уточнение вокруг лучшего.
            var dirs = new List<Vector3>();
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (x != 0 || y != 0 || z != 0) dirs.Add(new Vector3(x, y, z).normalized);
            Vector3 bestLocal = Vector3.up;
            volume = 0f;
            foreach (Vector3 d in dirs)
            {
                float v = Try(root, renderers, d, closeGaps, 36);
                if (v > volume) { volume = v; bestLocal = d; }
            }
            // Уточнение: шаги 20° → 10° → 5° вокруг лучшего.
            foreach (float step in new[] { 20f, 10f, 5f })
            {
                Vector3 baseDir = bestLocal;
                Vector3 a1 = Vector3.Cross(baseDir, Mathf.Abs(baseDir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 a2 = Vector3.Cross(baseDir, a1).normalized;
                for (int i = 0; i < 8; i++)
                {
                    float ang = i * 45f * Mathf.Deg2Rad;
                    Vector3 axis = a1 * Mathf.Cos(ang) + a2 * Mathf.Sin(ang);
                    Vector3 d = Quaternion.AngleAxis(step, axis) * baseDir;
                    float v = Try(root, renderers, d, closeGaps, 36);
                    if (v > volume * 1.01f) { volume = v; bestLocal = d; }
                }
            }
            axisName = AxisName(bestLocal);
            return FrameFor(root, bestLocal);
        }

        private static float Try(Transform root, IList<Renderer> renderers, Vector3 localUp, int closeGaps, int res)
        {
            Quaternion f = FrameFor(root, localUp);
            Result r = Scan(root, renderers, f, res, 4, 4, 0.01f, closeGaps);
            if (!r.Ok) return 0f;
            // Небольшой бонус направлению, что сейчас смотрит в мировой верх: при равных объёмах — как стоит.
            return r.Volume * (1f + 0.05f * Mathf.Max(0f, Vector3.Dot(f * Vector3.up, Vector3.up)));
        }

        private static string AxisName(Vector3 d)
        {
            Vector3 a = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
            string main = a.y >= a.x && a.y >= a.z ? (d.y > 0 ? "+Y" : "−Y") : a.z >= a.x ? (d.z > 0 ? "+Z" : "−Z") : (d.x > 0 ? "+X" : "−X");
            float m = Mathf.Max(a.x, Mathf.Max(a.y, a.z));
            float off = Mathf.Acos(Mathf.Clamp(m, -1f, 1f)) * Mathf.Rad2Deg;
            return off < 3f ? main : $"{main} с наклоном {off:0}° (локально {d.x:0.00}, {d.y:0.00}, {d.z:0.00})";
        }

        /// <summary>Обрезать многоугольник (3D) вертикальной плоскостью: ось 0 — X, 2 — Z; keepGreater — оставить, что больше value.</summary>
        private static void Clip(List<Vector3> poly, List<Vector3> tmp, int axis, float value, bool keepGreater)
        {
            tmp.Clear();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % n];
                float da = (keepGreater ? 1f : -1f) * (a[axis] - value);
                float db = (keepGreater ? 1f : -1f) * (b[axis] - value);
                bool ina = da >= -1e-7f, inb = db >= -1e-7f;
                if (ina) tmp.Add(a);
                if (ina != inb)
                {
                    float tt = da / (da - db);
                    tmp.Add(a + (b - a) * tt);
                }
            }
            poly.Clear();
            poly.AddRange(tmp);
        }

        /// <summary>
        /// Коллайдеры: под полостью — плита от низа модели до дна полости, остальное, где
        /// есть металл, — столбик от низа до кромки. Соседние одинаковые столбики сливаются
        /// в полосы, полосы — в прямоугольники, чтобы коробок было немного.
        /// </summary>
        private static void BuildBoxes(Result res, int nx, int nz, float x0, float z0, float cell,
                                       bool[] hit, float[] top, float[] bot, int[] comp, int bestComp,
                                       int grid, float minThick)
        {
            int f = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(nx, nz) / (float)Mathf.Max(4, grid)));
            int gx = Mathf.CeilToInt(nx / (float)f), gz = Mathf.CeilToInt(nz / (float)f);
            float gc = cell * f;
            var lo = new float[gx * gz];
            var hi = new float[gx * gz];
            var has = new bool[gx * gz];
            for (int z = 0; z < gz; z++)
                for (int x = 0; x < gx; x++)
                {
                    float l = float.PositiveInfinity, h = float.NegativeInfinity;
                    int wetN = 0, solidN = 0;
                    float wetTop = float.PositiveInfinity;
                    for (int zz = z * f; zz < Mathf.Min(nz, z * f + f); zz++)
                        for (int xx = x * f; xx < Mathf.Min(nx, x * f + f); xx++)
                        {
                            int k = zz * nx + xx;
                            if (!hit[k]) continue;
                            solidN++;
                            l = Mathf.Min(l, bot[k]);
                            if (comp[k] == bestComp) { wetN++; wetTop = Mathf.Min(wetTop, top[k]); }
                            else h = Mathf.Max(h, top[k]);
                        }
                    if (solidN == 0) continue;
                    int g = z * gx + x;
                    has[g] = true;
                    // Больше половины крупной клетки — полость: плита до дна; иначе — стенка до кромки.
                    float hTop = wetN * 2 >= solidN ? wetTop : Mathf.Max(h, wetTop < float.PositiveInfinity ? wetTop : h);
                    if (hTop - l < minThick) l = hTop - minThick;
                    lo[g] = l; hi[g] = hTop;
                }

            float tol = gc * 0.35f;
            var used = new bool[gx * gz];
            for (int z = 0; z < gz; z++)
                for (int x = 0; x < gx; x++)
                {
                    int g = z * gx + x;
                    if (!has[g] || used[g]) continue;
                    // Полоса вправо.
                    int x1 = x;
                    while (x1 + 1 < gx && has[z * gx + x1 + 1] && !used[z * gx + x1 + 1] &&
                           Mathf.Abs(lo[z * gx + x1 + 1] - lo[g]) < tol && Mathf.Abs(hi[z * gx + x1 + 1] - hi[g]) < tol) x1++;
                    // Растим вниз по Z, пока вся полоса такая же.
                    int z1 = z;
                    while (z1 + 1 < gz)
                    {
                        bool ok = true;
                        for (int xx = x; xx <= x1 && ok; xx++)
                        {
                            int j = (z1 + 1) * gx + xx;
                            ok = has[j] && !used[j] && Mathf.Abs(lo[j] - lo[g]) < tol && Mathf.Abs(hi[j] - hi[g]) < tol;
                        }
                        if (!ok) break;
                        z1++;
                    }
                    float l = float.PositiveInfinity, h = float.NegativeInfinity;
                    for (int zz = z; zz <= z1; zz++)
                        for (int xx = x; xx <= x1; xx++)
                        {
                            int j = zz * gx + xx;
                            used[j] = true;
                            l = Mathf.Min(l, lo[j]); h = Mathf.Max(h, hi[j]);
                        }
                    float cx = x0 + (x + x1 + 1) * 0.5f * gc, cz = z0 + (z + z1 + 1) * 0.5f * gc;
                    Vector3 c = new Vector3(cx, (l + h) * 0.5f, cz);
                    res.BoxCenters.Add(res.Origin + res.Frame * c);
                    res.BoxSizes.Add(new Vector3((x1 - x + 1) * gc, Mathf.Max(minThick, h - l), (z1 - z + 1) * gc));
                }
        }

        /// <summary>Двоичная куча (уровень, клетка) — для заливки.</summary>
        private sealed class MinHeap
        {
            private float[] _k; private int[] _v; private int _n;
            public MinHeap(int cap) { _k = new float[Mathf.Max(16, cap)]; _v = new int[_k.Length]; }
            public int Count => _n;
            public void Push(float key, int val)
            {
                if (_n == _k.Length) { System.Array.Resize(ref _k, _n * 2); System.Array.Resize(ref _v, _n * 2); }
                int i = _n++;
                _k[i] = key; _v[i] = val;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_k[p] <= _k[i]) break;
                    Swap(i, p); i = p;
                }
            }
            public void Pop(out float key, out int val)
            {
                key = _k[0]; val = _v[0];
                _n--;
                if (_n == 0) return;
                _k[0] = _k[_n]; _v[0] = _v[_n];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < _n && _k[l] < _k[m]) m = l;
                    if (r < _n && _k[r] < _k[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
            }
            private void Swap(int a, int b)
            {
                float tk = _k[a]; _k[a] = _k[b]; _k[b] = tk;
                int tv = _v[a]; _v[a] = _v[b]; _v[b] = tv;
            }
        }
    }
}
