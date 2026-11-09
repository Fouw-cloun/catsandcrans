using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Level
{
    /// <summary>Сборка дальних поясов (3 — кольцо модулей, 4 — стена высоток) в меши-ассеты. Только редактор.</summary>
    public static class CityFarBuilder
    {
        public const string MatPath = "Assets/Generated/City/CC_CityFar.mat";
        private const int KWall = 0, KRoof = 1, KGround = 2, KRoad = 3, KPlain = 4;

        // ───────────────────────── сборщик меша ─────────────────────────
        private sealed class MB
        {
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<Vector2> UV = new List<Vector2>(), Info = new List<Vector2>();
            public readonly List<int> T = new List<int>();
            public int Houses;
            public Vector3 Origin;          // центр города в осях меша (наружные грани не строим)
            public bool SkipOut;
            public int WallKind;            // стена дома: с окнами (0) или глухая (4)

            /// <summary>Четырёхугольник по часовой (если смотреть на лицевую сторону).</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, int kind, float rnd)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (SkipOut && Mathf.Abs(n.y) < 0.5f && IsOut(a, c, n)) return;
                int s = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                for (int i = 0; i < 4; i++) { N.Add(n); C.Add(col); Info.Add(new Vector2(kind, rnd)); }
                UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
                T.Add(s); T.Add(s + 1); T.Add(s + 2); T.Add(s); T.Add(s + 2); T.Add(s + 3);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col, int kind, float rnd)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (SkipOut && Mathf.Abs(n.y) < 0.5f && IsOut(a, c, n)) return;
                int s = V.Count;
                V.Add(a); V.Add(b); V.Add(c);
                for (int i = 0; i < 3; i++) { N.Add(n); C.Add(col); Info.Add(new Vector2(kind, rnd)); }
                UV.Add(new Vector2(a.x + a.z, a.y)); UV.Add(new Vector2(b.x + b.z, b.y)); UV.Add(new Vector2(c.x + c.z, c.y));
                T.Add(s); T.Add(s + 1); T.Add(s + 2);
            }

            private bool IsOut(Vector3 a, Vector3 c, Vector3 n)
            {
                Vector3 p = (a + c) * 0.5f - Origin; p.y = 0;
                float l = p.magnitude;
                return l > 1e-3f && Vector3.Dot(n, p / l) > 0.5f;
            }

            public void Ground(float x0, float z0, float x1, float z1, float y, int kind)
            {
                bool so = SkipOut; SkipOut = false;
                Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Color.white,
                     new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0), kind, 0f);
                SkipOut = so;
            }

            /// <summary>Коробка без дна; floorV — от какой высоты считать этажи (окна непрерывны по ярусам). roof — крыша да/нет.</summary>
            public void Box(float x0, float z0, float x1, float z1, float y0, float y1, Color col, float rnd, float floorV, int wallKind, bool roof)
            {
                float h0 = y0 - floorV, h1 = y1 - floorV, w = x1 - x0, d = z1 - z0;
                Quad(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), col,
                     new Vector2(0, h0), new Vector2(0, h1), new Vector2(w, h1), new Vector2(w, h0), wallKind, rnd);
                Quad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), col,
                     new Vector2(0, h0), new Vector2(0, h1), new Vector2(w, h1), new Vector2(w, h0), wallKind, rnd);
                Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), col,
                     new Vector2(0, h0), new Vector2(0, h1), new Vector2(d, h1), new Vector2(d, h0), wallKind, rnd);
                Quad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), col,
                     new Vector2(0, h0), new Vector2(0, h1), new Vector2(d, h1), new Vector2(d, h0), wallKind, rnd);
                if (roof)
                    Quad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), col,
                         new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0), KRoof, rnd);
            }

            /// <summary>Восьмигранная башня (или n-гранная), с крышкой.</summary>
            public void Prism(float cx, float cz, float rad, int sides, float y0, float y1, Color col, float rnd, float floorV, float rot)
            {
                float per = 2f * Mathf.PI * rad / sides;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = rot + i * 2f * Mathf.PI / sides, a1 = rot + (i + 1) * 2f * Mathf.PI / sides;
                    Vector3 p0 = new Vector3(cx + Mathf.Cos(a0) * rad, 0, cz + Mathf.Sin(a0) * rad);
                    Vector3 p1 = new Vector3(cx + Mathf.Cos(a1) * rad, 0, cz + Mathf.Sin(a1) * rad);
                    Quad(new Vector3(p0.x, y0, p0.z), new Vector3(p0.x, y1, p0.z), new Vector3(p1.x, y1, p1.z), new Vector3(p1.x, y0, p1.z), col,
                         new Vector2(i * per, y0 - floorV), new Vector2(i * per, y1 - floorV), new Vector2((i + 1) * per, y1 - floorV), new Vector2((i + 1) * per, y0 - floorV), WallKind, rnd);
                    bool so = SkipOut; SkipOut = false;
                    Tri(new Vector3(cx, y1, cz), new Vector3(p1.x, y1, p1.z), new Vector3(p0.x, y1, p0.z), col, KRoof, rnd);
                    SkipOut = so;
                }
            }

            /// <summary>Двускатная крыша на прямоугольнике (конёк вдоль длинной стороны).</summary>
            public void Gable(float x0, float z0, float x1, float z1, float y1, float rh, Color col, float rnd)
            {
                float yr = y1 + rh;
                if (x1 - x0 >= z1 - z0)
                {
                    float zm = (z0 + z1) * 0.5f;
                    Quad(new Vector3(x0, y1, z0), new Vector3(x0, yr, zm), new Vector3(x1, yr, zm), new Vector3(x1, y1, z0), col, Vector2.zero, Vector2.up, Vector2.one, Vector2.right, KRoof, rnd);
                    Quad(new Vector3(x1, y1, z1), new Vector3(x1, yr, zm), new Vector3(x0, yr, zm), new Vector3(x0, y1, z1), col, Vector2.zero, Vector2.up, Vector2.one, Vector2.right, KRoof, rnd);
                    Tri(new Vector3(x0, y1, z1), new Vector3(x0, yr, zm), new Vector3(x0, y1, z0), col, KPlain, rnd);
                    Tri(new Vector3(x1, y1, z0), new Vector3(x1, yr, zm), new Vector3(x1, y1, z1), col, KPlain, rnd);
                }
                else
                {
                    float xm = (x0 + x1) * 0.5f;
                    // конёк вдоль Z: скаты на запад и восток, фронтоны на юг и север
                    Quad(new Vector3(x0, y1, z1), new Vector3(xm, yr, z1), new Vector3(xm, yr, z0), new Vector3(x0, y1, z0), col, Vector2.zero, Vector2.up, Vector2.one, Vector2.right, KRoof, rnd);
                    Quad(new Vector3(x1, y1, z0), new Vector3(xm, yr, z0), new Vector3(xm, yr, z1), new Vector3(x1, y1, z1), col, Vector2.zero, Vector2.up, Vector2.one, Vector2.right, KRoof, rnd);
                    Tri(new Vector3(x0, y1, z0), new Vector3(xm, yr, z0), new Vector3(x1, y1, z0), col, KPlain, rnd);
                    Tri(new Vector3(x1, y1, z1), new Vector3(xm, yr, z1), new Vector3(x0, y1, z1), col, KPlain, rnd);
                }
            }

            public Mesh Build(string name)
            {
                Mesh m = new Mesh { name = name };
                m.indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                m.SetVertices(V); m.SetNormals(N); m.SetColors(C); m.SetUVs(0, UV); m.SetUVs(1, Info);
                m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        private static float R(System.Random r, Vector2 range) { return Mathf.Lerp(range.x, range.y, (float)r.NextDouble()); }
        private static float F(System.Random r) { return (float)r.NextDouble(); }

        private static Color Pick(CityFarBands f, System.Random r, float haze, bool linear)
        {
            Color c = f.WallColors != null && f.WallColors.Length > 0 ? f.WallColors[r.Next(f.WallColors.Length)] : new Color(0.77f, 0.51f, 0.36f);
            return Finish(f, r, c, haze, linear, f.ColorJitter);
        }

        private static Color PickTrim(CityFarBands f, System.Random r, float haze, bool linear)
        {
            Color c = f.TrimColors != null && f.TrimColors.Length > 0 ? f.TrimColors[r.Next(f.TrimColors.Length)] : new Color(0.9f, 0.82f, 0.7f);
            return Finish(f, r, c, haze, linear, f.ColorJitter * 0.5f);
        }

        /// <summary>Разброс оттенка/насыщенности/яркости, дымка, перевод в линейный.</summary>
        private static Color Finish(CityFarBands f, System.Random r, Color c, float haze, bool linear, float j)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            h = Mathf.Repeat(h + (F(r) - 0.5f) * 0.04f * j, 1f);
            s = Mathf.Clamp01(s * (1f + (F(r) - 0.5f) * 0.5f * j));
            v = Mathf.Clamp01(v * (1f + (F(r) - 0.5f) * 0.3f * j));
            c = Color.HSVToRGB(h, s, v);
            c = Color.Lerp(c, f.HazeColor, haze);
            c.a = 1f;
            return linear ? c.linear : c;
        }

        /// <summary>
        /// Дом интересной формы в прямоугольнике: двускатный (низкий), уступы, восьмигранник, Г-образный, с карнизом,
        /// шпилем и коробками на крыше. S — размер модуля.
        /// </summary>
        private static void House(MB mb, System.Random r, CityFarBands f, float x0, float z0, float x1, float z1, float y0, float h, Color col, float S,
                                  float haze, float windows, bool linear)
        {
            float rnd = 0.02f + 0.96f * F(r);
            float w = x1 - x0, d = z1 - z0;
            float top = y0 + h;
            bool tall = h > 0.42f * S, low = h < 0.24f * S;
            Color dark = PickTrim(f, r, haze, linear);
            mb.WallKind = F(r) < windows ? KWall : KPlain;

            if (low && F(r) < f.GableChance)
            {
                float eave = y0 + h * 0.78f;
                mb.Box(x0, z0, x1, z1, y0, eave, col, rnd, y0, mb.WallKind, false);
                mb.Gable(x0, z0, x1, z1, eave, h * 0.32f, col, rnd);
                mb.Houses++;
                return;
            }
            if (tall && F(r) < f.OctagonChance && Mathf.Abs(w - d) < 0.5f * Mathf.Max(w, d))
            {
                float rad = Mathf.Min(w, d) * 0.5f;
                float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
                float hb = h * (0.7f + 0.15f * F(r));
                mb.Prism(cx, cz, rad, 8, y0, y0 + hb, col, rnd, y0, Mathf.PI / 8f);
                mb.Prism(cx, cz, rad * 0.72f, 8, y0 + hb, top, col, rnd, y0, Mathf.PI / 8f);
                if (F(r) < f.SpireChance) mb.Box(cx - rad * 0.06f, cz - rad * 0.06f, cx + rad * 0.06f, cz + rad * 0.06f, top, top + h * 0.16f, dark, rnd, y0, KPlain, true);
                mb.Houses++;
                return;
            }
            if (F(r) < f.LShapeChance && w > 0.05f * S && d > 0.05f * S)
            {
                // Г: основной корпус на всю длину по одной стороне + крыло пониже
                bool alongX = w >= d;
                float split = 0.5f + 0.15f * (F(r) - 0.5f);
                float hw = h * (0.55f + 0.25f * F(r));
                if (alongX)
                {
                    float zm = z0 + d * split, xm = x0 + w * (0.35f + 0.2f * F(r));
                    Stack(mb, r, f, x0, z0, x1, zm, y0, h, col, dark, rnd, S, tall);
                    Stack(mb, r, f, x0, zm, xm, z1, y0, hw, col, dark, rnd, S, false);
                }
                else
                {
                    float xm = x0 + w * split, zm = z0 + d * (0.35f + 0.2f * F(r));
                    Stack(mb, r, f, x0, z0, xm, z1, y0, h, col, dark, rnd, S, tall);
                    Stack(mb, r, f, xm, z0, x1, zm, y0, hw, col, dark, rnd, S, false);
                }
                mb.Houses++;
                return;
            }
            Stack(mb, r, f, x0, z0, x1, z1, y0, h, col, dark, rnd, S, tall);
            mb.Houses++;
        }

        /// <summary>Корпус-ступени: 1–3 яруса (у высоких), карниз, шпиль, коробки на крыше.</summary>
        private static void Stack(MB mb, System.Random r, CityFarBands f, float x0, float z0, float x1, float z1, float y0, float h, Color col, Color dark, float rnd, float S, bool tall)
        {
            int tiers = tall && F(r) < f.TierChance ? (F(r) < 0.4f ? 3 : 2) : 1;
            bool tierCol = F(r) < f.TierColorChance;
            Color tcol = Color.Lerp(col, dark, 0.5f);
            float y = y0;
            float ax0 = x0, az0 = z0, ax1 = x1, az1 = z1;
            for (int t = 0; t < tiers; t++)
            {
                float part = tiers == 1 ? 1f : (t == 0 ? 0.6f + 0.1f * F(r) : (t == tiers - 1 ? 1f : 0.82f));
                float yt = t == tiers - 1 ? y0 + h : Mathf.Max(y + 0.04f * S, y0 + h * part);
                mb.Box(ax0, az0, ax1, az1, y, yt, t > 0 && tierCol ? tcol : col, rnd, y0, mb.WallKind, true);
                // карниз по верху яруса
                if (F(r) < f.CorniceChance)
                {
                    float lip = 0.006f * S, th = 0.012f * S;
                    mb.Box(ax0 - lip, az0 - lip, ax1 + lip, az1 + lip, yt - th, yt, dark, rnd, y0, KPlain, true);
                }
                if (t < tiers - 1)
                {
                    float iw = (ax1 - ax0) * (0.1f + 0.08f * F(r)), id = (az1 - az0) * (0.1f + 0.08f * F(r));
                    ax0 += iw; ax1 -= iw; az0 += id; az1 -= id;
                }
                y = yt;
            }
            float top = y0 + h;
            // шпиль / антенна на самых высоких
            if (tall && F(r) < f.SpireChance)
            {
                float s = Mathf.Min(ax1 - ax0, az1 - az0) * 0.07f;
                float cx = (ax0 + ax1) * 0.5f, cz = (az0 + az1) * 0.5f;
                mb.Box(cx - s, cz - s, cx + s, cz + s, top, top + h * 0.14f, dark, rnd, y0, KPlain, true);
            }
            // коробки на крыше: вентиляция, баки, будки лестниц
            int n = f.RoofClutter > 0 ? r.Next(f.RoofClutter + 1) : 0;
            for (int i = 0; i < n; i++)
            {
                float rw = (ax1 - ax0) * (0.12f + 0.18f * F(r)), rd = (az1 - az0) * (0.12f + 0.18f * F(r));
                float cx = Mathf.Lerp(ax0 + rw, ax1 - rw, F(r)), cz = Mathf.Lerp(az0 + rd, az1 - rd, F(r));
                float rh = (0.012f + 0.025f * F(r)) * S;
                mb.Box(cx - rw * 0.5f, cz - rd * 0.5f, cx + rw * 0.5f, cz + rd * 0.5f, top, top + rh, dark, rnd, y0, KPlain, true);
            }
        }

        public static void Build(CityFarBands f)
        {
            CityBlockGenerator city = f.City != null ? f.City : f.GetComponent<CityBlockGenerator>();
            if (city == null) city = Object.FindFirstObjectByType<CityBlockGenerator>();
            if (city == null || city.Grid == null) { EditorUtility.DisplayDialog("Дальние пояса", "Нужна застройка City с сеткой плиток (Grid).", "Ок"); return; }
            f.City = city;
            if (f.Material == null) f.Material = EnsureMaterial();
            if (f.Material == null) return;
            Soften(f.Material);

            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            TileGridGenerator grid = city.Grid;
            Vector2 cs = city.ModuleSize();
            float S = Mathf.Min(cs.x, cs.y);
            int W = grid.Width, D = grid.Depth;
            Vector3 c00; Quaternion rot;
            city.ModuleFrame(0, 0, out c00, out rot);
            Transform gt = grid.transform;
            float groundY = gt.InverseTransformPoint(c00).y;
            Vector3 origin = grid.CenterPivot ? Vector3.zero : new Vector3(W * cs.x * 0.5f, 0, D * cs.y * 0.5f);
            System.Random rnd = new System.Random(f.Seed);

            string folder = "Assets/Generated/City/Far";
            Folder(folder);
            if (!string.IsNullOrEmpty(f.BakeFolder) && AssetDatabase.IsValidFolder(f.BakeFolder)) AssetDatabase.DeleteAsset(f.BakeFolder);
            string sceneName = f.gameObject.scene.IsValid() && !string.IsNullOrEmpty(f.gameObject.scene.name) ? f.gameObject.scene.name : "Scene";
            string bake = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Safe(sceneName + "_" + f.name));
            Folder(bake);

            Transform old = f.transform.Find(CityFarBands.ContainerName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            GameObject root = new GameObject(CityFarBands.ContainerName);
            Undo.RegisterCreatedObjectUndo(root, "Дальние пояса");
            root.transform.SetParent(f.transform, false);
            root.transform.SetPositionAndRotation(gt.position, gt.rotation);
            root.isStatic = true;

            int houses = 0, tris = 0, rends = 0;
            float road = city.RoadMargin, side = city.Sidewalk * S;
            int rings = f.Band3 ? Mathf.Max(1, f.Band3Rings) : 0;

            // ───── пояс 3 ─────
            if (f.Band3)
            {
                GameObject b3 = new GameObject("Band3_Ring");
                b3.transform.SetParent(root.transform, false);
                b3.isStatic = true;
                for (int z = -rings; z < D + rings; z++)
                    for (int x = -rings; x < W + rings; x++)
                    {
                        if (x >= 0 && x < W && z >= 0 && z < D) continue;
                        Vector3 cl = grid.CellLocal(x, z) + new Vector3(0, groundY, 0);
                        MB mb = new MB();
                        mb.Origin = origin - cl;
                        float hx = cs.x * 0.5f, hz = cs.y * 0.5f;
                        float lx = cs.x * (0.5f - road), lz = cs.y * (0.5f - road);
                        if (f.Band3Ground) { mb.Ground(-hx, -hz, hx, hz, -0.06f, KRoad); mb.Ground(-lx, -lz, lx, lz, 0f, KGround); }
                        float bx0 = -lx + side, bx1 = lx - side, bz0 = -lz + side, bz1 = lz - side;
                        float dS = R(rnd, f.Band3Depth) * S, dN = R(rnd, f.Band3Depth) * S, dW = R(rnd, f.Band3Depth) * S, dE = R(rnd, f.Band3Depth) * S;
                        Row(mb, rnd, f, bx0, bx1, bz0, bz0 + dS, true, linear, S);
                        Row(mb, rnd, f, bx0, bx1, bz1 - dN, bz1, true, linear, S);
                        Row(mb, rnd, f, bz0 + dS, bz1 - dN, bx0, bx0 + dW, false, linear, S);
                        Row(mb, rnd, f, bz0 + dS, bz1 - dN, bx1 - dE, bx1, false, linear, S);
                        if (rnd.NextDouble() < f.Band3TowerChance)
                        {
                            float ix0 = bx0 + dW, ix1 = bx1 - dE, iz0 = bz0 + dS, iz1 = bz1 - dN;
                            float tw = Mathf.Min(ix1 - ix0, iz1 - iz0) * (0.45f + 0.3f * F(rnd));
                            if (tw > 0.06f * S)
                            {
                                float cx = Mathf.Lerp(ix0 + tw * 0.5f, ix1 - tw * 0.5f, F(rnd));
                                float cz = Mathf.Lerp(iz0 + tw * 0.5f, iz1 - tw * 0.5f, F(rnd));
                                House(mb, rnd, f, cx - tw * 0.5f, cz - tw * 0.5f, cx + tw * 0.5f, cz + tw * 0.5f, 0f, R(rnd, f.Band3TowerHeights) * S,
                                      Pick(f, rnd, f.Band3Haze, linear), S, f.Band3Haze, Mathf.Max(f.Band3Windows, 0.85f), linear);
                            }
                        }
                        string nm = "B3_" + x + "_" + z;
                        Mesh m = mb.Build(nm);
                        AssetDatabase.CreateAsset(m, bake + "/" + nm + ".asset");
                        Renderer(b3.transform, nm, m, cl, f.Material);
                        houses += mb.Houses; tris += mb.T.Count / 3; rends++;
                    }
            }

            // ───── пояс 4 ─────
            if (f.Band4)
            {
                GameObject b4 = new GameObject("Band4_Wall");
                b4.transform.SetParent(root.transform, false);
                b4.isStatic = true;
                float inX = (W * 0.5f + rings) * cs.x, inZ = (D * 0.5f + rings) * cs.y;
                float depth = f.Band4Thickness * S;
                float outX = inX + depth, outZ = inZ + depth;
                Vector3 o = origin;
                float[,] rects =
                {
                    { -outX, -outZ, outX, -inZ }, { -outX, inZ, outX, outZ },
                    { -outX, -inZ, -inX, inZ }, { inX, -inZ, outX, inZ }
                };
                string[] names = { "B4_South", "B4_North", "B4_West", "B4_East" };
                for (int k = 0; k < 4; k++)
                {
                    float rx0 = rects[k, 0] + o.x, rz0 = rects[k, 1] + o.z, rx1 = rects[k, 2] + o.x, rz1 = rects[k, 3] + o.z;
                    bool alongX = k < 2;
                    MB mb = new MB();
                    mb.Origin = o; mb.SkipOut = true;
                    if (f.Band4Ground) mb.Ground(rx0, rz0, rx1, rz1, groundY - 0.08f, KRoad);
                    float d0 = alongX ? (k == 0 ? rz1 : rz0) : (k == 2 ? rx1 : rx0);
                    float dir = (k == 0 || k == 2) ? -1f : 1f;
                    float dlen = alongX ? rz1 - rz0 : rx1 - rx0;
                    float a0 = alongX ? rx0 : rz0, a1 = alongX ? rx1 : rz1;
                    float dcur = 0f;
                    while (dcur < dlen - 0.02f * S)
                    {
                        float rowD = Mathf.Min(R(rnd, f.Band4Size) * S, dlen - dcur);
                        float t = (dcur + rowD * 0.5f) / Mathf.Max(dlen, 1e-3f);
                        float acur = a0;
                        while (acur < a1 - 0.02f * S)
                        {
                            float wv = Mathf.Min(R(rnd, f.Band4Size) * S, a1 - acur);
                            float gap = F(rnd) * 0.008f * S;
                            float inset = F(rnd) * 0.04f * rowD;
                            float tt = t * t * (3f - 2f * t);
                            float h = Mathf.Lerp(R(rnd, f.Band4NearHeights), R(rnd, f.Band4FarHeights), tt) * S;
                            float p0 = d0 + dir * (dcur + inset), p1 = d0 + dir * (dcur + rowD - gap);
                            float lo = Mathf.Min(p0, p1), hi = Mathf.Max(p0, p1);
                            float hz = Mathf.Lerp(f.Band4Haze * 0.6f, f.Band4Haze, tt);
                            Color col = Pick(f, rnd, hz, linear);
                            if (alongX) House(mb, rnd, f, acur, lo, acur + wv - gap, hi, groundY, h, col, S, hz, f.Band4Windows, linear);
                            else House(mb, rnd, f, lo, acur, hi, acur + wv - gap, groundY, h, col, S, hz, f.Band4Windows, linear);
                            acur += wv;
                        }
                        dcur += rowD;
                    }
                    Mesh m = mb.Build(names[k]);
                    AssetDatabase.CreateAsset(m, bake + "/" + names[k] + ".asset");
                    Renderer(b4.transform, names[k], m, Vector3.zero, f.Material);
                    houses += mb.Houses; tris += mb.T.Count / 3; rends++;
                }
            }

            AssetDatabase.SaveAssets();
            Undo.RecordObject(f, "Дальние пояса");
            f.BakeFolder = bake; f.LastHouses = houses; f.LastTriangles = tris; f.LastRenderers = rends;
            EditorUtility.SetDirty(f);
            if (!UnityEditor.EditorApplication.isPlaying && f.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(f.gameObject.scene);
            Debug.Log($"[Застройка/дальние] собрано: домов {houses}, треугольников {tris:N0}, рендереров {rends} · меши в {bake}", f);
        }

        /// <summary>Ряд домов вдоль улицы: вдоль — от a0 до a1 (X, если alongX, иначе Z); поперёк — от c0 до c1.</summary>
        private static void Row(MB mb, System.Random r, CityFarBands f, float a0, float a1, float c0, float c1, bool alongX, bool linear, float S)
        {
            if (a1 - a0 <= 0f || c1 <= c0) return;
            float cur = a0;
            while (cur < a1 - 0.03f * S)
            {
                float w = Mathf.Min(R(r, f.Band3Width) * S, a1 - cur);
                if (a1 - (cur + w) < 0.05f * S) w = a1 - cur;
                float back = F(r) * 0.15f * (c1 - c0);
                float h = R(r, f.Band3Heights) * S;
                Color col = Pick(f, r, f.Band3Haze, linear);
                float lo = c0 + (c0 < 0 ? 0 : back), hi = c1 - (c0 < 0 ? back : 0);
                if (alongX) House(mb, r, f, cur, lo, cur + w, hi, 0f, h, col, S, f.Band3Haze, f.Band3Windows, linear);
                else House(mb, r, f, lo, cur, hi, cur + w, 0f, h, col, S, f.Band3Haze, f.Band3Windows, linear);
                cur += w;
                if (r.NextDouble() < 0.1) cur += 0.02f * S;
            }
        }

        private static void Renderer(Transform parent, string name, Mesh m, Vector3 localPos, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public static void Clear(CityFarBands f)
        {
            Transform old = f.transform.Find(CityFarBands.ContainerName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        /// <summary>Материал из прошлых версий (v1.7, яркий и контрастный) — один раз переводим на мягкие значения.</summary>
        private static void Soften(Material m)
        {
            if (!m.HasProperty("_FarVer") || m.GetFloat("_FarVer") >= 3f) return;
            Undo.RecordObject(m, "Цвета под атласы");
            m.SetFloat("_Saturation", 1f); m.SetFloat("_Contrast", 0.85f); m.SetFloat("_WinStrength", 0.55f);
            m.SetFloat("_RoofMix", 0.35f); m.SetFloat("_Ambient", 0.75f); m.SetFloat("_Wrap", 0.35f);
            m.SetColor("_WinColor", new Color(0.42f, 0.52f, 0.56f, 1f));
            m.SetColor("_RoofColor", new Color(0.45f, 0.37f, 0.34f, 1f));
            m.SetColor("_ShadowColor", new Color(0.58f, 0.50f, 0.58f, 1f));
            m.SetColor("_LightTint", new Color(1f, 0.95f, 0.88f, 1f));
            m.SetVector("_WinSize", new Vector4(5f, 4.5f, 0f, 0f));
            m.SetVector("_WinFill", new Vector4(0.42f, 0.5f, 0f, 0f));
            m.SetFloat("_FarVer", 3f);
            EditorUtility.SetDirty(m);
            Debug.Log("[Застройка/дальние] материал CC_CityFar переведён на цвета под атласы (v1.8.1)", m);
        }

        public static Material EnsureMaterial()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m != null) return m;
            Shader sh = Shader.Find("CatCooks/City/Far");
            if (sh == null) { Debug.LogWarning("[Застройка/дальние] нет шейдера CatCooks/City/Far"); return null; }
            Folder("Assets/Generated/City");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, MatPath);
            return m;
        }

        private static void Folder(string folder)
        {
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        private static string Safe(string s)
        {
            char[] bad = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in s) { bool ok = ch != ' '; foreach (char b in bad) if (b == ch) { ok = false; break; } sb.Append(ok ? ch : '_'); }
            return sb.ToString();
        }
    }

    [CustomEditor(typeof(CityFarBands))]
    public sealed class CityFarBandsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CityFarBands f = (CityFarBands)target;
            EditorGUILayout.HelpBox("Пояс 3 — кольцо модулей за сеткой. Пояс 4 — стена высоток вокруг всего. Формы: уступы, восьмигранники, Г, двускатные, карнизы, шпили. " +
                                    "Окна ночью горят от моста «Окна ночью». Теней и коллайдеров нет.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("Собрать пояса", GUILayout.Height(28))) CityFarBuilder.Build(f);
                GUI.backgroundColor = new Color(1f, 0.95f, 0.6f);
                if (GUILayout.Button("Новый вариант", GUILayout.Height(28))) { Undo.RecordObject(f, "Seed"); f.Seed++; CityFarBuilder.Build(f); }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Очистить", GUILayout.Height(28))) CityFarBuilder.Clear(f);
            }
            if (f.LastRenderers > 0)
                EditorGUILayout.LabelField($"Последняя сборка: домов {f.LastHouses}, треугольников {f.LastTriangles:N0}, рендереров {f.LastRenderers}", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
            DrawDefaultInspector();
        }
    }
}
