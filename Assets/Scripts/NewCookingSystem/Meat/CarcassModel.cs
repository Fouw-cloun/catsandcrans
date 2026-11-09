using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    public enum MeatSpecies : byte { Pork = 0, Beef = 1 }

    /// <summary>Части туши — крупные, общие (одинаково у свиньи и быка).</summary>
    public enum MeatPart : byte
    {
        FrontL = 0, FrontR, Brisket, LoinL, LoinR, RibsL, RibsR, RearL, RearR,
        LegFL, LegFR, LegHL, LegHR,
    }

    /// <summary>Готовый меш части (или ломтя): вершины в своих осях (центр — Origin в осях туши).</summary>
    public sealed class MeatMesh
    {
        public Vector3[] V, N;
        public int[] T;
        public Color[] Clean, Film;      // цвет зачищенной поверхности и с плёнкой/шкурой (у среза — одинаковые)
        public float[] Mask;             // 0 — плёнки тут нет (срез, кость, копыто), 1 — есть
        public byte[] Cell;              // клетка плёнки (сетка CellN³ по габариту части)
        public Vector3 Origin;           // центр меша в осях туши
        public Vector3 Min, Max;         // габарит в своих осях
        public float AxisLo, AxisHi;     // протяжённость вдоль оси нарезки (оси туши)
        public float Volume;             // м³
    }

    /// <summary>
    /// ТУША ИЗ КОДА — круглая «мультяшная» свинья / бычок без головы, на коротких ножках.
    /// Разрезана КРУПНО: лопатки, корейка, рёбра, грудинка (брискет), окорока, четыре ноги (рулька / голяшка).
    /// Швы между частями волнистые и скруглённые — туша выглядит целой, а куски — аккуратными «мультяшными» кусками.
    /// На срезе: розовое/красное мясо с белыми прожилками, толстый жировой край, кость кружком с мозгом.
    /// Одинаково на сервере и у всех клиентов (детерминированно): ломоть строится заново там же.
    /// </summary>
    public static class CarcassModel
    {
        public const int PartCount = 13;
        public const int CellN = 6;
        public const short NoSlab = short.MinValue;

        // ───────────────────────── параметры видов ─────────────────────────

        private struct Ell { public Vector3 C, R; public float Pw; public Ell(Vector3 c, Vector3 r, float pw = 0f) { C = c; R = r; Pw = pw; } }

        private struct Leg { public Vector3 A, B, C, D; public float RA, RB, RC, RD; public bool Hind; public int Side; }

        private struct Box
        {
            public float X0, X1, Y0, Y1, Z0, Z1;
            public Box(float x0, float x1, float y0, float y1, float z0, float z1) { X0 = x0; X1 = x1; Y0 = y0; Y1 = y1; Z0 = z0; Z1 = z1; }
            public float Sdf(Vector3 p) => Mathf.Max(Mathf.Max(Mathf.Max(X0 - p.x, p.x - X1), Mathf.Max(Y0 - p.y, p.y - Y1)), Mathf.Max(Z0 - p.z, p.z - Z1));
        }

        private struct Leaf { public Box B; }

        private sealed class Sp
        {
            public MeatSpecies Kind;
            public float S;                         // масштаб от свиньи
            public float Voxel, Gap, K, KLeg, KLegBody, Round, Wave;
            public float Yc, Top, HalfW, XFront;
            public Ell[] Torso;
            public Leg[] Legs;                      // FL, FR, HL, HR
            public float XFrontEnd, XRear, YLoin, YBrisket, YLegF, ZInnerF, YLegH, HoofH;
            public float SpineY, SpineR, RibSpacing, RibR, FatCap, FatSide;
            public Color Meat, MeatDark, Fat, Skin, Skin2, Hoof, Bone, Marrow;
            public float Steak, Chunk, LegChunk;
            public Vector3 Lo, Hi;
            public List<Leaf>[] Parts;
        }

        private const float Big = 50f;
        private static readonly Sp PorkSp = Make(MeatSpecies.Pork), BeefSp = Make(MeatSpecies.Beef);
        private static Sp Of(MeatSpecies s) => s == MeatSpecies.Beef ? BeefSp : PorkSp;

        /// <summary>Свинья — шаблон; бычок — та же форма крупнее (×1.6), с холкой и тёмными копытами.</summary>
        private static Sp Make(MeatSpecies kind)
        {
            bool beef = kind == MeatSpecies.Beef;
            float S = beef ? 1.6f : 1f;
            var s = new Sp
            {
                Kind = kind, S = S,
                Voxel = beef ? 0.018f : 0.012f, Gap = 0.002f * S, K = 0.07f * S, KLeg = 0.025f * S, KLegBody = 0.06f * S,
                Round = 0.016f * S, Wave = 0.032f * S,
                Yc = 0.52f * S, Top = 0.76f * S, HalfW = 0.215f * S, XFront = 0.57f * S,
                XFrontEnd = 0.16f * S, XRear = -0.20f * S, ZInnerF = 0.04f * S, HoofH = 0.045f * S,
                SpineR = 0.022f * S, RibSpacing = 0.05f * S, RibR = 0.009f * S, FatCap = 0.028f * S, FatSide = 0.012f * S,
                Steak = beef ? 0.032f : 0.025f, Chunk = beef ? 0.08f : 0.06f, LegChunk = beef ? 0.07f : 0.05f,
            };
            float y = s.Yc;
            s.YLoin = y + 0.08f * S; s.YBrisket = y - 0.10f * S; s.YLegF = 0.26f * S; s.YLegH = 0.24f * S;
            s.SpineY = y + 0.16f * S;
            if (beef)
            {
                s.Meat = new Color(0.80f, 0.22f, 0.20f); s.MeatDark = new Color(0.68f, 0.15f, 0.15f); s.Fat = new Color(0.99f, 0.94f, 0.82f);
                s.Skin = new Color(0.98f, 0.91f, 0.74f); s.Skin2 = new Color(0.84f, 0.30f, 0.27f); s.Hoof = new Color(0.22f, 0.17f, 0.15f);
            }
            else
            {
                s.Meat = new Color(0.95f, 0.55f, 0.53f); s.MeatDark = new Color(0.88f, 0.45f, 0.45f); s.Fat = new Color(0.99f, 0.95f, 0.89f);
                s.Skin = new Color(1.00f, 0.80f, 0.72f); s.Skin2 = new Color(0.97f, 0.72f, 0.65f); s.Hoof = new Color(0.58f, 0.44f, 0.40f);
            }
            s.Bone = new Color(0.98f, 0.95f, 0.88f); s.Marrow = new Color(0.86f, 0.42f, 0.38f);

            y = 0.52f;                                                    // шаблон — в размерах свиньи, потом ×S
            var t = new List<Ell>
            {
                new Ell(V(0.02f, y, 0f), V(0.55f, 0.24f, 0.215f), 2.3f),   // круглый корпус
                new Ell(V(-0.02f, y + 0.07f, 0f), V(0.46f, 0.15f, 0.17f)),   // спина
                new Ell(V(0f, y - 0.06f, 0f), V(0.42f, 0.19f, 0.20f)),       // брюшко
                new Ell(V(-0.34f, y - 0.03f, 0.10f), V(0.22f, 0.22f, 0.13f)),  // окорока
                new Ell(V(-0.34f, y - 0.03f, -0.10f), V(0.22f, 0.22f, 0.13f)),
                new Ell(V(0.26f, y + 0.01f, 0.07f), V(0.21f, 0.21f, 0.14f)),   // лопатки
                new Ell(V(0.26f, y + 0.01f, -0.07f), V(0.21f, 0.21f, 0.14f)),
                new Ell(V(-0.36f, 0.30f, 0.11f), V(0.11f, 0.13f, 0.085f)),     // бёдра
                new Ell(V(-0.36f, 0.30f, -0.11f), V(0.11f, 0.13f, 0.085f)),
                new Ell(V(0.27f, 0.29f, 0.11f), V(0.08f, 0.11f, 0.07f)),       // предплечья
                new Ell(V(0.27f, 0.29f, -0.11f), V(0.08f, 0.11f, 0.07f)),
            };
            if (beef) t.Add(new Ell(V(0.24f, y + 0.15f, 0f), V(0.20f, 0.10f, 0.12f)));   // холка
            for (int i = 0; i < t.Count; i++) t[i] = new Ell(t[i].C * S, t[i].R * S, t[i].Pw);
            s.Torso = t.ToArray();

            s.Legs = new Leg[4];
            for (int i = 0; i < 4; i++)
            {
                bool hind = i >= 2; int side = i % 2 == 0 ? 1 : -1;
                Vector3 m = new Vector3(S, S, S * side);
                s.Legs[i] = hind
                    ? new Leg { A = Vector3.Scale(V(-0.36f, 0.28f, 0.11f), m), B = Vector3.Scale(V(-0.38f, 0.14f, 0.11f), m), C = Vector3.Scale(V(-0.36f, 0.05f, 0.105f), m), D = Vector3.Scale(V(-0.345f, 0.012f, 0.105f), m),
                                RA = 0.075f * S, RB = 0.048f * S, RC = 0.042f * S, RD = 0.04f * S, Hind = true, Side = side }
                    : new Leg { A = Vector3.Scale(V(0.27f, 0.30f, 0.11f), m), B = Vector3.Scale(V(0.28f, 0.13f, 0.11f), m), C = Vector3.Scale(V(0.285f, 0.05f, 0.105f), m), D = Vector3.Scale(V(0.30f, 0.012f, 0.105f), m),
                                RA = 0.06f * S, RB = 0.048f * S, RC = 0.042f * S, RD = 0.04f * S, Hind = false, Side = side };
            }
            Finish(s);
            return s;
        }

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>Габарит тела и раскладка частей.</summary>
        private static void Finish(Sp s)
        {
            Vector3 lo = new Vector3(1e3f, 0f, 1e3f), hi = new Vector3(-1e3f, -1e3f, -1e3f);
            foreach (Ell e in s.Torso) { lo = Vector3.Min(lo, e.C - e.R); hi = Vector3.Max(hi, e.C + e.R); }
            foreach (Leg g in s.Legs)
                foreach (Vector3 p in new[] { g.A, g.B, g.C, g.D }) { lo = Vector3.Min(lo, p - Vector3.one * g.RA); hi = Vector3.Max(hi, p + Vector3.one * g.RA); }
            hi.x = Mathf.Min(hi.x, s.XFront);
            s.Lo = lo - Vector3.one * 0.04f * s.S; s.Hi = hi + Vector3.one * 0.04f * s.S;
            s.Lo.y = -0.02f;

            float xF = s.XFront + 0.1f, xE = s.XFrontEnd, xR = s.XRear, xB = -Big;
            s.Parts = new List<Leaf>[PartCount];
            for (int i = 0; i < PartCount; i++) s.Parts[i] = new List<Leaf>();
            Action<MeatPart, Box> Add = (p, b) => s.Parts[(int)p].Add(new Leaf { B = b });
            float L0 = 0f, L1 = Big, R0 = -Big, R1 = 0f;

            // перед: лопатки (с шеей) сверху, грудинка снизу, ноги
            Add(MeatPart.FrontL, new Box(xE, xF, s.YBrisket, Big, L0, L1));
            Add(MeatPart.FrontR, new Box(xE, xF, s.YBrisket, Big, R0, R1));
            Add(MeatPart.Brisket, new Box(xE, xF, s.YLegF, s.YBrisket, -Big, Big));
            Add(MeatPart.Brisket, new Box(xE, xF, -Big, s.YLegF, -s.ZInnerF, s.ZInnerF));
            Add(MeatPart.LegFL, new Box(xE, xF, -Big, s.YLegF, s.ZInnerF, Big));
            Add(MeatPart.LegFR, new Box(xE, xF, -Big, s.YLegF, -Big, -s.ZInnerF));
            // середина: корейка сверху, рёбра по бокам, грудинка (брискет) снизу
            Add(MeatPart.LoinL, new Box(xR, xE, s.YLoin, Big, L0, L1));
            Add(MeatPart.LoinR, new Box(xR, xE, s.YLoin, Big, R0, R1));
            Add(MeatPart.RibsL, new Box(xR, xE, s.YBrisket, s.YLoin, L0, L1));
            Add(MeatPart.RibsR, new Box(xR, xE, s.YBrisket, s.YLoin, R0, R1));
            Add(MeatPart.Brisket, new Box(xR, xE, -Big, s.YBrisket, -Big, Big));
            // зад: окорока, задние ноги
            Add(MeatPart.RearL, new Box(xB, xR, s.YLegH, Big, L0, L1));
            Add(MeatPart.RearR, new Box(xB, xR, s.YLegH, Big, R0, R1));
            Add(MeatPart.LegHL, new Box(xB, xR, -Big, s.YLegH, L0, L1));
            Add(MeatPart.LegHR, new Box(xB, xR, -Big, s.YLegH, R0, R1));
        }

        // ───────────────────────── описание частей ─────────────────────────

        public static bool Exists(MeatSpecies sp, MeatPart p) => (int)p < PartCount && Of(sp).Parts[(int)p].Count > 0;

        public static bool IsLeft(MeatPart p) => p.ToString().EndsWith("L");
        public static bool IsSided(MeatPart p) => p != MeatPart.Brisket;

        /// <summary>Латинское имя объекта: Pork_LoinL.</summary>
        public static string Id(MeatSpecies sp, MeatPart p) => (sp == MeatSpecies.Beef ? "Beef_" : "Pork_") + p;

        /// <summary>«Тип» части без стороны: 0 лопатка, 1 грудинка, 2 корейка, 3 рёбра, 4 окорок, 5 нога.</summary>
        public static int Kind(MeatPart p)
        {
            switch (p)
            {
                case MeatPart.FrontL: case MeatPart.FrontR: return 0;
                case MeatPart.Brisket: return 1;
                case MeatPart.LoinL: case MeatPart.LoinR: return 2;
                case MeatPart.RibsL: case MeatPart.RibsR: return 3;
                case MeatPart.RearL: case MeatPart.RearR: return 4;
                default: return 5;
            }
        }

        public const int KindCount = 6;

        // Продукты — общие, чтобы подходили во все рецепты «Свинина / Говядина — части туши».
        // Свои продукты только там, где их просит отдельное блюдо: брискет, рёбра, рулька, голяшка, стейк рибай.
        private static readonly string[] PorkTitles = { "Свинина", "Сало", "Свинина", "Рёбра", "Свинина", "Свиная рулька" };   // грудинка — сало (борщ, копчение)
        private static readonly string[] BeefTitles = { "Говядина", "Брискет", "Говядина", "Говядина", "Говядина", "Говяжья голяшка" };
        private static readonly string[] PorkSlices = { null, null, null, null, null, null };
        private static readonly string[] BeefSlices = { null, null, "Стейк рибай", null, null, null };
        private static readonly string[] PorkNames = { "Свиная лопатка", "Свиная грудинка (сало)", "Свиная корейка", "Свиные рёбра", "Свиной окорок", "Свиная рулька" };
        private static readonly string[] BeefNames = { "Говяжья лопатка", "Брискет", "Толстый край", "Говяжьи рёбра", "Говяжье бедро", "Говяжья голяшка" };

        public static string KindTitle(MeatSpecies sp, int kind) => (sp == MeatSpecies.Beef ? BeefTitles : PorkTitles)[kind];
        public static string KindSliceTitle(MeatSpecies sp, int kind) => (sp == MeatSpecies.Beef ? BeefSlices : PorkSlices)[kind];

        /// <summary>Продукт части (для рецептов).</summary>
        public static string Title(MeatSpecies sp, MeatPart p) => KindTitle(sp, Kind(p));

        /// <summary>Продукт ломтя пилой: стейк или null — тот же продукт, просто кусок.</summary>
        public static string SliceTitle(MeatSpecies sp, MeatPart p) => KindSliceTitle(sp, Kind(p));

        /// <summary>Название части для подсказки: «Свиная корейка (лев.)».</summary>
        public static string Nice(MeatSpecies sp, MeatPart p)
        {
            string t = (sp == MeatSpecies.Beef ? BeefNames : PorkNames)[Kind(p)];
            string n = p.ToString();
            if (n.StartsWith("LegF")) t += " передн.";
            else if (n.StartsWith("LegH")) t += " задн.";
            return t + (IsSided(p) ? (IsLeft(p) ? " (лев.)" : " (прав.)") : "");
        }

        /// <summary>Ось нарезки: 0 — вдоль туши (X), 1 — вдоль ноги (Y).</summary>
        public static int SliceAxis(MeatPart p) => Kind(p) == 5 ? 1 : 0;

        public static float SliceThickness(MeatSpecies sp, MeatPart p)
        {
            Sp s = Of(sp);
            int k = Kind(p);
            if (k == 2) return s.Steak;
            if (k == 5) return s.LegChunk;
            return s.Chunk;
        }

        public static bool Sliceable(MeatPart p) => true;
        public static bool IsRibs(MeatPart p) => Kind(p) == 3;
        public static float Voxel(MeatSpecies sp) => Of(sp).Voxel;

        /// <summary>Габарит части в осях туши (для сетки плёнки и меша).</summary>
        public static void PartBox(MeatSpecies sp, MeatPart p, out Vector3 lo, out Vector3 hi)
        {
            Sp s = Of(sp);
            lo = new Vector3(1e3f, 1e3f, 1e3f); hi = -lo;
            foreach (Leaf l in s.Parts[(int)p])
            {
                lo = Vector3.Min(lo, new Vector3(l.B.X0, l.B.Y0, l.B.Z0));
                hi = Vector3.Max(hi, new Vector3(l.B.X1, l.B.Y1, l.B.Z1));
            }
            // волнистый шов может зайти за плоскость — запас
            Vector3 w = Vector3.one * (s.Wave * 1.3f);
            lo = Vector3.Max(lo - w, s.Lo); hi = Vector3.Min(hi + w, s.Hi);
        }

        public static int CellOf(MeatSpecies sp, MeatPart p, Vector3 carcassPoint)
        {
            PartBox(sp, p, out Vector3 lo, out Vector3 hi);
            Vector3 d = hi - lo;
            int cx = Mathf.Clamp((int)((carcassPoint.x - lo.x) / Mathf.Max(1e-4f, d.x) * CellN), 0, CellN - 1);
            int cy = Mathf.Clamp((int)((carcassPoint.y - lo.y) / Mathf.Max(1e-4f, d.y) * CellN), 0, CellN - 1);
            int cz = Mathf.Clamp((int)((carcassPoint.z - lo.z) / Mathf.Max(1e-4f, d.z) * CellN), 0, CellN - 1);
            return (cz * CellN + cy) * CellN + cx;
        }

        public static Vector3 CellCenter(MeatSpecies sp, MeatPart p, int cell)
        {
            PartBox(sp, p, out Vector3 lo, out Vector3 hi);
            int cx = cell % CellN, cy = (cell / CellN) % CellN, cz = cell / (CellN * CellN);
            Vector3 d = hi - lo;
            return lo + new Vector3((cx + 0.5f) / CellN * d.x, (cy + 0.5f) / CellN * d.y, (cz + 0.5f) / CellN * d.z);
        }

        // ───────────────────────── поля расстояний ─────────────────────────

        private static float Ellipsoid(Vector3 p, Vector3 r)
        {
            float k0 = new Vector3(p.x / r.x, p.y / r.y, p.z / r.z).magnitude;
            float k1 = new Vector3(p.x / (r.x * r.x), p.y / (r.y * r.y), p.z / (r.z * r.z)).magnitude;
            return k1 > 1e-6f ? k0 * (k0 - 1f) / k1 : -Mathf.Min(r.x, Mathf.Min(r.y, r.z));
        }

        private static float Shape(Vector3 p, Ell e)
        {
            Vector3 q = p - e.C;
            if (e.Pw <= 2.01f) return Ellipsoid(q, e.R);
            float n = e.Pw;
            float k = Mathf.Pow(Mathf.Pow(Mathf.Abs(q.x / e.R.x), n) + Mathf.Pow(Mathf.Abs(q.y / e.R.y), n) + Mathf.Pow(Mathf.Abs(q.z / e.R.z), n), 1f / n);
            return (k - 1f) * Mathf.Min(e.R.x, Mathf.Min(e.R.y, e.R.z));
        }

        private static float RoundCone(Vector3 p, Vector3 a, Vector3 b, float r1, float r2)
        {
            Vector3 ba = b - a;
            float l2 = Vector3.Dot(ba, ba);
            float rr = r1 - r2;
            float a2 = l2 - rr * rr;
            float il2 = 1f / l2;
            Vector3 pa = p - a;
            float y = Vector3.Dot(pa, ba);
            float z = y - l2;
            Vector3 q = pa * l2 - ba * y;
            float x2 = Vector3.Dot(q, q);
            float y2 = y * y * l2;
            float z2 = z * z * l2;
            float k = Mathf.Sign(rr) * rr * rr * x2;
            if (Mathf.Sign(z) * a2 * z2 > k) return Mathf.Sqrt(x2 + z2) * il2 - r2;
            if (Mathf.Sign(y) * a2 * y2 < k) return Mathf.Sqrt(x2 + y2) * il2 - r1;
            return (Mathf.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
        }

        private static float Capsule(Vector3 p, Vector3 a, Vector3 b, float r)
        {
            Vector3 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector3.Dot(pa, ba) / Mathf.Max(1e-8f, Vector3.Dot(ba, ba)));
            return (pa - ba * h).magnitude - r;
        }

        private static float SMin(float a, float b, float k)
        {
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Min(a, b) - h * h * k * 0.25f;
        }

        private static float SMax(float a, float b, float k) => -SMin(-a, -b, k);

        private static float LegSdf(Sp s, Leg g, Vector3 p)
        {
            float d = RoundCone(p, g.A, g.B, g.RA, g.RB);
            d = SMin(d, RoundCone(p, g.B, g.C, g.RB, g.RC), s.KLeg);
            d = SMin(d, RoundCone(p, g.C, g.D, g.RC, g.RD), s.KLeg);
            // копытце: щель спереди
            float groove = Mathf.Max(Mathf.Max(Mathf.Abs(p.z - g.D.z) - 0.003f * s.S, p.y - s.HoofH * 0.8f), (g.D.x - g.RD * 0.1f) - p.x);
            return Mathf.Max(d, -groove);
        }

        /// <summary>Тело целиком. &lt; 0 — внутри.</summary>
        private static float Body(Sp s, Vector3 p)
        {
            float d = Shape(p, s.Torso[0]);
            for (int i = 1; i < s.Torso.Length; i++) d = SMin(d, Shape(p, s.Torso[i]), s.K);
            foreach (Leg g in s.Legs)
            {
                Vector3 lo = Vector3.Min(Vector3.Min(g.A, g.B), Vector3.Min(g.C, g.D)) - Vector3.one * (g.RA + s.KLegBody);
                Vector3 hi = Vector3.Max(Vector3.Max(g.A, g.B), Vector3.Max(g.C, g.D)) + Vector3.one * (g.RA + s.KLegBody);
                if (p.x < lo.x || p.y < lo.y || p.z < lo.z || p.x > hi.x || p.y > hi.y || p.z > hi.z) continue;
                d = SMin(d, LegSdf(s, g, p), s.KLegBody);
            }
            return Mathf.Max(d, -p.y);                     // стоит на полу
        }

        /// <summary>Волна шва: одна на всю тушу — части по-прежнему стыкуются без щелей.</summary>
        private static Vector3 Warp(Sp s, Vector3 p)
        {
            float w = s.Wave, k = 1f / s.S;
            return new Vector3(w * Mathf.Sin(p.y * 7.3f * k + p.z * 3.1f * k + 0.4f),
                               w * 0.8f * Mathf.Sin(p.x * 5.2f * k + 0.7f) + w * 0.4f * Mathf.Sin(p.z * 9f * k),
                               0f);
        }

        private static float Region(Sp s, MeatPart part, Vector3 p)
        {
            Vector3 q = p + Warp(s, p);
            float best = Big;
            foreach (Leaf l in s.Parts[(int)part]) best = Mathf.Min(best, l.B.Sdf(q));
            // шея срезана ровно (срез, а не шкура)
            return Mathf.Max(best + s.Gap, p.x - s.XFront);
        }

        private static float Sq(float x) => x * x;

        /// <summary>smoothstep как в шейдерах (Mathf.SmoothStep в Unity — интерполяция from→to, не порог!).</summary>
        private static float SStep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // ───────────────────────── кости (кружки на срезе) ─────────────────────────

        private static float BoneSdf(Sp s, MeatPart part, Vector3 p, out bool marrow)
        {
            marrow = false;
            float d = Big;
            int k = Kind(part);
            // позвоночник — кружок у корейки, лопаток, окорока
            if (k == 0 || k == 2 || k == 4)
            {
                float sp = new Vector2(p.y - s.SpineY, p.z).magnitude;
                d = Mathf.Min(d, sp - s.SpineR);
                if (sp < s.SpineR * 0.45f) marrow = true;
            }
            // рёбра — косточки под поверхностью
            if (k == 3)
            {
                float ph = Frac((p.x - s.XRear) / s.RibSpacing);
                float dx = Mathf.Abs(ph - 0.5f) * s.RibSpacing - s.RibR;
                float depth = -Body(s, p);
                float dd = Mathf.Abs(depth - (s.FatSide + s.RibR * 2.2f)) - s.RibR * 1.2f;
                d = Mathf.Min(d, Mathf.Max(dx, dd));
            }
            // кости ног — кружок с мозгом
            foreach (Leg g in s.Legs)
            {
                if (g.Side > 0 != p.z > 0f) continue;
                if (k != 5 && k != 4 && k != 0) continue;
                float r = s.SpineR * 1.05f;
                float b = Mathf.Min(Mathf.Min(Capsule(p, Vector3.Lerp(g.A, g.B, 0.1f) + Vector3.up * 0.06f * s.S, g.B, r), Capsule(p, g.B, g.C, r * 0.9f)),
                                    Capsule(p, g.C, g.D + Vector3.up * s.HoofH * 0.7f, r * 0.8f));
                if (b < -r * 0.45f) marrow = true;
                d = Mathf.Min(d, b);
            }
            return d;
        }

        // ───────────────────────── шум ─────────────────────────

        private static float Frac(float x) => x - Mathf.Floor(x);

        private static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }

        private static float Noise(Vector3 p)
        {
            int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
            float fx = p.x - ix, fy = p.y - iy, fz = p.z - iz;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float a = Mathf.Lerp(Hash(ix, iy, iz), Hash(ix + 1, iy, iz), fx), b = Mathf.Lerp(Hash(ix, iy + 1, iz), Hash(ix + 1, iy + 1, iz), fx);
            float c = Mathf.Lerp(Hash(ix, iy, iz + 1), Hash(ix + 1, iy, iz + 1), fx), d = Mathf.Lerp(Hash(ix, iy + 1, iz + 1), Hash(ix + 1, iy + 1, iz + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
        }

        // ───────────────────────── цвет (мультяшно: ровные цвета, белые прожилки) ─────────────────────────

        private static Color A(Color c, float a) { c.a = a; return c; }
        private static float Soft(float edge, float x, float w) => Mathf.Clamp01(0.5f + (edge - x) / (2f * w));   // 1 — x < edge

        /// <summary>Белые прожилки — плавные волнистые линии, немного.</summary>
        private static float Streaks(Sp s, Vector3 p, float amount)
        {
            float k = 1f / s.S;
            float n = Noise(p * 4f * k + Vector3.one * 3f);
            float a = Mathf.Abs(Mathf.Sin((p.x * 6f + p.y * 9f - p.z * 5f) * k + n * 5f));
            float b = Mathf.Abs(Mathf.Sin((p.x * -4f + p.y * 3.5f + p.z * 8f) * k + n * 4f + 1.3f));
            float w = 0.09f * amount;
            return Mathf.Max(Soft(w, a, 0.06f), Soft(w * 0.7f, b, 0.06f) * 0.8f);
        }

        private static Color Muscle(Sp s, MeatPart part, Vector3 p, bool layers = true)
        {
            int k = Kind(part);
            Color m = Color.Lerp(s.Meat, s.MeatDark, SStep(0.35f, 0.75f, Noise(p * 3f / s.S)));
            float fat = Streaks(s, p, (k == 2 ? 1.2f : k == 5 ? 0.6f : 1f) * (layers ? 1f : 0.6f));
            // грудинка — полоски сала
            if (k == 1 && layers)
            {
                float band = Mathf.Sin((p.y + 0.01f * s.S * Noise(p * 8f / s.S)) / (0.026f * s.S) * 6.283f);
                fat = Mathf.Max(fat, SStep(0.2f, 0.6f, band) * 0.95f);
            }
            return Color.Lerp(A(m, 0.6f), A(s.Fat, 0.3f), fat);
        }

        /// <summary>Срез: мясо с прожилками, толстый жировой край, кость кружком.</summary>
        private static Color Inside(Sp s, MeatPart part, Vector3 p, float depth)
        {
            float w = s.Voxel * 0.6f;
            Color c = Muscle(s, part, p);
            float cap = Kind(part) == 5 ? s.FatSide * 0.6f : Mathf.Lerp(s.FatSide, s.FatCap, Mathf.Clamp01((p.y - s.Yc) / (s.Top - s.Yc)));
            c = Color.Lerp(c, A(s.Fat, 0.3f), Soft(cap, depth, w));
            if (s.Kind == MeatSpecies.Pork) c = Color.Lerp(c, A(s.Skin, 0.25f), Soft(0.004f, depth, w * 0.5f));
            float bd = BoneSdf(s, part, p, out bool marrow);
            c = Color.Lerp(c, A(marrow ? s.Marrow : s.Bone, 0.15f), Soft(0f, bd, w * 0.6f));
            return c;
        }

        /// <summary>Снаружи: шкурка (свинья) или жир с плёнкой (бычок) — и зачищенное мясо под ними.</summary>
        private static void Outside(Sp s, MeatPart part, Vector3 p, out Color film, out Color clean, out float mask)
        {
            int k = Kind(part);
            if (k == 5 && p.y < s.HoofH)
            {
                film = clean = A(s.Hoof, 0.1f); mask = 0f;
                return;
            }
            clean = Muscle(s, part, p, false);
            if (s.Kind == MeatSpecies.Pork)
            {
                Color skin = Color.Lerp(s.Skin, s.Skin2, SStep(0.5f, 0.8f, Noise(p * 6f)) * 0.6f);
                film = A(skin, 0.18f);
                mask = 1f;
            }
            else
            {
                // жир ровными «шапками» сверху и на окороке, ниже — красное мясо в тонкой плёнке
                float up = (p.y - s.Yc) / (s.Top - s.Yc);
                float fatAmt = SStep(0.15f, 0.3f, up + (Noise(p * 2.5f / s.S) - 0.5f) * 0.5f);
                if (k == 5) fatAmt = 0f;
                Color meat = Color.Lerp(clean, s.Skin2, 0.5f);
                film = Color.Lerp(A(meat, 0.45f), A(s.Skin, 0.22f), fatAmt);
                mask = 1f;
            }
        }

        // ───────────────────────── меш ─────────────────────────

        private static readonly Dictionary<long, MeatMesh> _cache = new Dictionary<long, MeatMesh>();

        private static long Key(MeatSpecies sp, MeatPart p, short a, short b)
            => ((long)(byte)sp << 48) | ((long)(byte)p << 40) | ((long)(ushort)a << 16) | (ushort)b;

        public static short ToMm(float m) => (short)Mathf.Clamp(Mathf.RoundToInt(m * 1000f), short.MinValue + 1, short.MaxValue);
        public static float FromMm(short mm) => mm / 1000f;

        /// <summary>Меш части целиком (slabA = slabB = NoSlab) или ломтя между slabA..slabB (мм, по оси нарезки, оси туши).</summary>
        public static MeatMesh Build(MeatSpecies sp, MeatPart part, short slabA = NoSlab, short slabB = NoSlab)
        {
            long key = Key(sp, part, slabA, slabB);
            lock (_cache) { if (_cache.TryGetValue(key, out MeatMesh have)) return have; }
            Sp s = Of(sp);
            if (s.Parts[(int)part].Count == 0) return null;
            int axis = SliceAxis(part);
            bool slab = slabA != NoSlab && slabB != NoSlab;
            float a = slab ? FromMm(slabA) : 0f, b = slab ? FromMm(slabB) : 0f;
            Func<Vector3, float> f = p =>
            {
                float r = Region(s, part, p);
                if (slab) { float t = axis == 0 ? p.x : p.y; r = Mathf.Max(r, Mathf.Max(a + s.Gap - t, t - b + s.Gap)); }
                return SMax(Body(s, p), r, s.Round);          // края кусков скруглены — аккуратные «мультяшные» куски
            };
            PartBox(sp, part, out Vector3 lo, out Vector3 hi);
            if (slab)
            {
                if (axis == 0) { lo.x = Mathf.Max(lo.x, a); hi.x = Mathf.Min(hi.x, b); }
                else { lo.y = Mathf.Max(lo.y, a); hi.y = Mathf.Min(hi.y, b); }
            }
            // тонкий ломоть (стейк) — сетка мельче, края ровнее
            float h = slab && b - a < 0.09f ? s.Voxel * 0.5f : s.Voxel;
            MeatMesh m = Nets(s, sp, part, f, lo - Vector3.one * h * 2f, hi + Vector3.one * h * 2f, axis, h);
            if (m != null) lock (_cache) { _cache[key] = m; }
            return m;
        }

        /// <summary>Уже построен (без ожидания)?</summary>
        public static bool TryCached(MeatSpecies sp, MeatPart part, short slabA, short slabB, out MeatMesh m)
        {
            lock (_cache) return _cache.TryGetValue(Key(sp, part, slabA, slabB), out m);
        }

        /// <summary>Центр куска в осях туши — его «точка опоры» (так стоит кусок в туше и так лежит отрезанный).</summary>
        public static Vector3 BoxCenter(MeatSpecies sp, MeatPart part, short slabA = NoSlab, short slabB = NoSlab)
        {
            PartBox(sp, part, out Vector3 lo, out Vector3 hi);
            if (slabA != NoSlab && slabB != NoSlab)
            {
                int axis = SliceAxis(part);
                float a = FromMm(slabA), b = FromMm(slabB);
                if (axis == 0) { lo.x = Mathf.Max(lo.x, a); hi.x = Mathf.Min(hi.x, b); }
                else { lo.y = Mathf.Max(lo.y, a); hi.y = Mathf.Min(hi.y, b); }
            }
            return (lo + hi) * 0.5f;
        }

        private static readonly int[] EdgeA = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
        private static readonly int[] EdgeB = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };

        /// <summary>Surface Nets: вершина в каждой клетке со сменой знака, четырёхугольник на каждое пересечённое ребро сетки.</summary>
        private static MeatMesh Nets(Sp s, MeatSpecies sp, MeatPart part, Func<Vector3, float> f, Vector3 lo, Vector3 hi, int axis, float h)
        {
            int nx = Mathf.Max(2, Mathf.CeilToInt((hi.x - lo.x) / h) + 1), ny = Mathf.Max(2, Mathf.CeilToInt((hi.y - lo.y) / h) + 1), nz = Mathf.Max(2, Mathf.CeilToInt((hi.z - lo.z) / h) + 1);
            var F = new float[nx * ny * nz];
            int inside = 0;
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        float v = f(lo + new Vector3(i * h, j * h, k * h));
                        F[(k * ny + j) * nx + i] = v;
                        if (v < 0f) inside++;
                    }
            if (inside == 0) return null;
            int cx = nx - 1, cy = ny - 1, cz = nz - 1;
            var cv = new int[cx * cy * cz];
            var verts = new List<Vector3>();
            var corner = new float[8];
            for (int k = 0; k < cz; k++)
                for (int j = 0; j < cy; j++)
                    for (int i = 0; i < cx; i++)
                    {
                        int mask = 0;
                        for (int n = 0; n < 8; n++)
                        {
                            float v = F[((k + ((n >> 2) & 1)) * ny + j + ((n >> 1) & 1)) * nx + i + (n & 1)];
                            corner[n] = v;
                            if (v < 0f) mask |= 1 << n;
                        }
                        int ci = (k * cy + j) * cx + i;
                        if (mask == 0 || mask == 255) { cv[ci] = -1; continue; }
                        Vector3 sum = Vector3.zero; int cnt = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            float va = corner[EdgeA[e]], vb = corner[EdgeB[e]];
                            if ((va < 0f) == (vb < 0f)) continue;
                            float t = va / (va - vb);
                            Vector3 pa = new Vector3(EdgeA[e] & 1, (EdgeA[e] >> 1) & 1, (EdgeA[e] >> 2) & 1);
                            Vector3 pb = new Vector3(EdgeB[e] & 1, (EdgeB[e] >> 1) & 1, (EdgeB[e] >> 2) & 1);
                            sum += Vector3.Lerp(pa, pb, t); cnt++;
                        }
                        cv[ci] = verts.Count;
                        verts.Add(lo + (new Vector3(i, j, k) + sum / cnt) * h);
                    }
            var tris = new List<int>();
            Func<int, int, int, int> C = (i, j, k) => cv[(k * cy + j) * cx + i];
            Action<int, int, int, int, Vector3> Quad = (q0, q1, q2, q3, outw) =>
            {
                if (q0 < 0 || q1 < 0 || q2 < 0 || q3 < 0) return;
                Tri(verts, tris, q0, q1, q2, outw); Tri(verts, tris, q0, q2, q3, outw);
            };
            for (int k = 1; k < cz; k++)
                for (int j = 1; j < cy; j++)
                    for (int i = 0; i < cx; i++)
                    {
                        float v0 = F[(k * ny + j) * nx + i], v1 = F[(k * ny + j) * nx + i + 1];
                        if ((v0 < 0f) == (v1 < 0f)) continue;
                        Quad(C(i, j - 1, k - 1), C(i, j, k - 1), C(i, j, k), C(i, j - 1, k), v0 < 0f ? Vector3.right : Vector3.left);
                    }
            for (int k = 1; k < cz; k++)
                for (int j = 0; j < cy; j++)
                    for (int i = 1; i < cx; i++)
                    {
                        float v0 = F[(k * ny + j) * nx + i], v1 = F[(k * ny + j + 1) * nx + i];
                        if ((v0 < 0f) == (v1 < 0f)) continue;
                        Quad(C(i - 1, j, k - 1), C(i, j, k - 1), C(i, j, k), C(i - 1, j, k), v0 < 0f ? Vector3.up : Vector3.down);
                    }
            for (int k = 0; k < cz; k++)
                for (int j = 1; j < cy; j++)
                    for (int i = 1; i < cx; i++)
                    {
                        float v0 = F[(k * ny + j) * nx + i], v1 = F[((k + 1) * ny + j) * nx + i];
                        if ((v0 < 0f) == (v1 < 0f)) continue;
                        Quad(C(i - 1, j - 1, k), C(i, j - 1, k), C(i, j, k), C(i - 1, j, k), v0 < 0f ? Vector3.forward : Vector3.back);
                    }

            // притянуть вершины к поверхности (ровнее, без «лесенки»), нормали — по полю
            int nv = verts.Count;
            var V = new Vector3[nv]; var N = new Vector3[nv];
            float e2 = h * 0.35f;
            for (int i = 0; i < nv; i++)
            {
                Vector3 p = verts[i];
                for (int it = 0; it < 2; it++)
                {
                    Vector3 g = Grad(f, p, e2);
                    float d = f(p), gg = Mathf.Max(1e-8f, Vector3.Dot(g, g));
                    Vector3 step = g * (d / gg);
                    if (step.magnitude > h * 0.7f) step = step.normalized * h * 0.7f;
                    p -= step;
                }
                V[i] = p;
                // снаружи — нормаль тела (гладко до самого шва), на срезе — нормаль среза (ровная грань)
                bool outerV = Body(s, p) >= f(p) - h * 0.08f;
                N[i] = (outerV ? Grad(q => Body(s, q), p, e2) : Grad(f, p, e2)).normalized;
            }

            var m = new MeatMesh { T = tris.ToArray(), Clean = new Color[nv], Film = new Color[nv], Mask = new float[nv], Cell = new byte[nv] };
            Vector3 mn = new Vector3(1e3f, 1e3f, 1e3f), mx = -mn;
            for (int i = 0; i < nv; i++)
            {
                Vector3 p = V[i];
                mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
                float body = Body(s, p);
                float reg = f(p);
                bool outer = body >= reg - h * 0.08f;
                if (outer)
                {
                    Outside(s, part, p, out Color film, out Color clean, out float mask);
                    m.Film[i] = film; m.Clean[i] = clean; m.Mask[i] = mask;
                }
                else
                {
                    Color c = Inside(s, part, p, -body);
                    m.Film[i] = m.Clean[i] = c; m.Mask[i] = 0f;
                }
                m.Cell[i] = (byte)CellOf(sp, part, p);
            }
            Vector3 origin = (mn + mx) * 0.5f;
            for (int i = 0; i < nv; i++) V[i] -= origin;
            m.V = V; m.N = N; m.Origin = origin; m.Min = mn - origin; m.Max = mx - origin;
            m.AxisLo = axis == 0 ? mn.x : mn.y; m.AxisHi = axis == 0 ? mx.x : mx.y;
            m.Volume = inside * h * h * h;
            return m;
        }

        private static Vector3 Grad(Func<Vector3, float> f, Vector3 p, float e)
        {
            return new Vector3(f(p + new Vector3(e, 0f, 0f)) - f(p - new Vector3(e, 0f, 0f)),
                               f(p + new Vector3(0f, e, 0f)) - f(p - new Vector3(0f, e, 0f)),
                               f(p + new Vector3(0f, 0f, e)) - f(p - new Vector3(0f, 0f, e))) / (2f * e);
        }

        private static void Tri(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, outward) < 0f) { int x = b; b = c; c = x; }
            t.Add(a); t.Add(b); t.Add(c);
        }

#if !CARCASS_TEST
        /// <summary>Сделать Unity-меш (цвета: membrane — доля плёнки по клеткам, null — вся плёнка на месте).</summary>
        public static Mesh ToMesh(MeatMesh m, string name, Vector3 origin, IList<byte> membrane = null, float cooked = 0f, float burnt = 0f)
        {
            var mesh = new Mesh { name = name };
            Fill(mesh, m, origin, membrane, cooked, burnt);
            return mesh;
        }

        /// <summary>Залить меш (вершины — в осях куска с центром origin, оси туши).</summary>
        /// <param name="shrink">Усадка при готовке: 1 — сырое, 0.92 — жареное (меш сжимается к своему центру).</param>
        public static void Fill(Mesh mesh, MeatMesh m, Vector3 origin, IList<byte> membrane = null, float cooked = 0f, float burnt = 0f, float shrink = 1f)
        {
            mesh.Clear();
            mesh.indexFormat = m.V.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            Vector3 off = m.Origin - origin;
            var v = new Vector3[m.V.Length];
            float k = Mathf.Clamp(shrink, 0.5f, 1f);
            for (int i = 0; i < v.Length; i++) v[i] = m.V[i] * k + off;
            mesh.vertices = v;
            mesh.normals = m.N;
            mesh.colors = Paint(m, membrane, cooked, burnt);
            mesh.triangles = m.T;
            mesh.RecalculateBounds();
        }

#endif
        private static readonly Color Cooked = new Color(0.52f, 0.30f, 0.18f), CookedIn = new Color(0.66f, 0.46f, 0.38f), Char = new Color(0.12f, 0.08f, 0.06f);

        public static Color[] Paint(MeatMesh m, IList<byte> membrane, float cooked, float burnt, Color[] into = null)
        {
            int n = m.V.Length;
            Color[] c = into != null && into.Length == n ? into : new Color[n];
            for (int i = 0; i < n; i++)
            {
                float mem = membrane != null && m.Cell[i] < membrane.Count ? membrane[m.Cell[i]] / 255f : 1f;
                Color col = Color.Lerp(m.Clean[i], m.Film[i], mem * m.Mask[i]);
                float a = col.a;
                if (cooked > 0f)
                {
                    bool surf = m.Mask[i] > 0f;
                    col = Color.Lerp(col, surf ? Cooked : Color.Lerp(CookedIn, col, 0.35f), cooked * (surf ? 0.85f : 0.6f));
                }
                if (burnt > 0f) col = Color.Lerp(col, Char, burnt);
                col.a = a;
                c[i] = col;
            }
            return c;
        }
    }
}
