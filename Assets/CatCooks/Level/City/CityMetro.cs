using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ВХОД В МЕТРО — в духе Нью-Йорка, мультяшно: проём в тротуаре, лестница уходит вниз в темноту, зелёные перила
    /// с трёх сторон, два фонаря-глобуса у входа, табличка с цветными кружками линий.
    /// Земля — обычная модель улицы, поэтому «дырка» сделана трафаретом: над проёмом плоскость-метка и сброс глубины
    /// (шейдер Metro Hole), яма (шейдер Toon с трафаретом) видна только внутри проёма и темнеет книзу.
    /// Поставь объект на тротуар (вход — по +Z объекта, спуск идёт вперёд), «Собрать» в инспекторе.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("CatCooks/Город/Вход в метро")]
    public sealed class CityMetroEntrance : MonoBehaviour
    {
        [Tooltip("Единиц сцены в метре. 0 — взять у «Пропсы и машины» (или 3.5)")]
        [Min(0f)] public float UnitsPerMeter = 0f;
        [Tooltip("Ширина проёма, м")]
        [Range(1.5f, 6f)] public float Width = 2.6f;
        [Tooltip("Длина проёма, м")]
        [Range(3f, 12f)] public float Length = 6.5f;
        [Tooltip("Глубина спуска, м")]
        [Range(2f, 8f)] public float Depth = 3.6f;
        [Range(6, 30)] public int Steps = 14;
        [Tooltip("Высота перил, м")]
        [Range(0.6f, 1.6f)] public float RailHeight = 1.05f;
        public Color RailColor = new Color(0.16f, 0.42f, 0.28f);
        public Color StoneColor = new Color(0.82f, 0.74f, 0.62f);
        public Color TileColor = new Color(0.93f, 0.90f, 0.82f);
        public Color TileBand = new Color(0.55f, 0.32f, 0.22f);
        public Color SignColor = new Color(0.10f, 0.22f, 0.16f);
        public Color[] LineColors =
        {
            new Color(0.93f, 0.20f, 0.20f), new Color(0.10f, 0.62f, 0.30f), new Color(1f, 0.55f, 0.10f), new Color(0.15f, 0.35f, 0.85f)
        };

        [Header("Материалы (ставит «Собрать»)")]
        public Material Toon, Glow, Pit, HoleMark, HoleClear;

        public float Meter()
        {
            if (UnitsPerMeter > 0f) return UnitsPerMeter;
            CityProps p = FindFirstObjectByType<CityProps>();
            return p != null ? p.Meter() : 3.5f;
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject go = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
        }

        /// <summary>Собрать геометрию (материалы должны быть заданы).</summary>
        public void Build()
        {
            Clear();
            float m = Meter();
            float W = Width * m, L = Length * m, D = Depth * m, hw = W * 0.5f;
            float cope = 0.28f * m, copeH = 0.12f * m;

            // ── яма: лестница, стены, площадка, тёмный проход ──
            CityToonMesh pit = new CityToonMesh();
            float stairL = L * 0.82f;
            float run = stairL / Steps, rise = D / Steps;
            for (int i = 0; i < Steps; i++)
            {
                float z0 = i * run, z1 = (i + 1) * run, yTop = -(i + 1) * rise, yPrev = -i * rise;
                Color st = (i % 2 == 0) ? StoneColor : StoneColor * 0.93f; st.a = 1f;
                // подступенок: вертикаль между ступенями, лицом вниз по лестнице (+Z)
                pit.Quad(new Vector3(hw, yTop, z0), new Vector3(hw, yPrev, z0), new Vector3(-hw, yPrev, z0), new Vector3(-hw, yTop, z0), st * 0.85f);
                // проступь (вверх)
                pit.Quad(new Vector3(-hw, yTop, z0), new Vector3(-hw, yTop, z1), new Vector3(hw, yTop, z1), new Vector3(hw, yTop, z0), st);
            }
            // площадка внизу
            pit.Quad(new Vector3(-hw, -D, stairL), new Vector3(-hw, -D, L), new Vector3(hw, -D, L), new Vector3(hw, -D, stairL), StoneColor * 0.8f);
            // боковые стены (внутрь): плитка с полосой
            float bandY = -D + 1.3f * m;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = hw * s;
                // плитка от верха до полосы, полоса, низ
                WallX(pit, x, s, 0f, L, -D - 0.2f * m, bandY - 0.15f * m, TileColor * 0.92f);
                WallX(pit, x, s, 0f, L, bandY - 0.15f * m, bandY + 0.15f * m, TileBand);
                WallX(pit, x, s, 0f, L, bandY + 0.15f * m, 0f, TileColor);
            }
            // задняя стена (лицом к −Z) с тёмным проходом
            float door = Mathf.Min(W * 0.8f, 2.2f * m), doorH = Mathf.Min(2.5f * m, D * 0.85f);
            pit.Quad(new Vector3(-hw, -D, L), new Vector3(-hw, 0f, L), new Vector3(hw, 0f, L), new Vector3(hw, -D, L), TileColor);
            Color dark = new Color(0.02f, 0.02f, 0.04f);
            pit.M = Matrix4x4.Translate(new Vector3(0, 0, -0.02f * m));
            pit.Quad(new Vector3(-door * 0.5f, -D, L), new Vector3(-door * 0.5f, -D + doorH, L), new Vector3(door * 0.5f, -D + doorH, L), new Vector3(door * 0.5f, -D, L), dark);
            // тёплый свет из прохода — полоска над полом
            pit.M = Matrix4x4.Translate(new Vector3(0, 0, -0.04f * m));
            pit.Quad(new Vector3(-door * 0.5f, -D, L), new Vector3(-door * 0.5f, -D + 0.25f * m, L), new Vector3(door * 0.5f, -D + 0.25f * m, L), new Vector3(door * 0.5f, -D, L), new Color(0.9f, 0.65f, 0.3f));
            pit.M = Matrix4x4.identity;
            // передняя стена под первыми ступенями (лицом к +Z) — закрыть коробку
            pit.Quad(new Vector3(hw, -D - 0.2f * m, 0f), new Vector3(hw, 0f, 0f), new Vector3(-hw, 0f, 0f), new Vector3(-hw, -D - 0.2f * m, 0f), StoneColor * 0.7f);
            // низ под лестницей (на случай взгляда снизу) — пол коробки
            pit.Quad(new Vector3(-hw, -D - 0.2f * m, 0f), new Vector3(-hw, -D - 0.2f * m, L), new Vector3(hw, -D - 0.2f * m, L), new Vector3(hw, -D - 0.2f * m, 0f), dark);
            GameObject pitGo = CityToonMesh.Make("Pit", transform, pit.Build("Metro_Pit"), Pit, false);
            Renderer pr = pitGo.GetComponent<Renderer>();
            if (Pit != null) pr.sharedMaterial = Pit;

            // ── дырка: метка и сброс глубины ──
            CityToonMesh hole = new CityToonMesh();
            float y = 0.03f * m;
            hole.Quad(new Vector3(-hw, y, 0f), new Vector3(-hw, y, L), new Vector3(hw, y, L), new Vector3(hw, y, 0f), Color.white);
            Mesh hm = hole.Build("Metro_Hole");
            CityToonMesh.Make("Hole_Mark", transform, hm, HoleMark, false);
            CityToonMesh.Make("Hole_Clear", transform, hm, HoleClear, false);

            // ── наверху: бортик, перила, фонари, табличка ──
            CityToonMesh top = new CityToonMesh();
            CityToonMesh glow = new CityToonMesh();
            // бортик вокруг проёма (с боков и сзади), спереди — низкий порожек
            top.Box(new Vector3(-hw - cope * 0.5f, copeH * 0.5f, L * 0.5f + cope * 0.5f), new Vector3(cope, copeH, L + cope), StoneColor);
            top.Box(new Vector3(hw + cope * 0.5f, copeH * 0.5f, L * 0.5f + cope * 0.5f), new Vector3(cope, copeH, L + cope), StoneColor);
            top.Box(new Vector3(0f, copeH * 0.5f, L + cope * 0.5f), new Vector3(W, copeH, cope), StoneColor);
            top.Box(new Vector3(0f, copeH * 0.25f, -cope * 0.25f), new Vector3(W + 2f * cope, copeH * 0.5f, cope * 0.5f), StoneColor * 0.9f);
            // перила: стойки, верхний и средний поручни, частые прутья
            float rh = RailHeight * m, post = 0.08f * m, bar = 0.035f * m, yb = copeH;
            Rail(top, new Vector3(-hw - cope * 0.5f, yb, 0f), new Vector3(-hw - cope * 0.5f, yb, L + cope * 0.5f), rh, post, bar, m);
            Rail(top, new Vector3(hw + cope * 0.5f, yb, 0f), new Vector3(hw + cope * 0.5f, yb, L + cope * 0.5f), rh, post, bar, m);
            Rail(top, new Vector3(-hw - cope * 0.5f, yb, L + cope * 0.5f), new Vector3(hw + cope * 0.5f, yb, L + cope * 0.5f), rh, post, bar, m);
            // фонари-глобусы у входа
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 b = new Vector3((hw + cope * 0.5f) * s, yb, 0f);
                top.Cylinder(b, 0.06f * m, 2.3f * m, 8, RailColor);
                top.Cylinder(b + Vector3.up * 2.3f * m, 0.12f * m, 0.12f * m, 8, RailColor * 0.8f);
                glow.Ellipsoid(b + Vector3.up * 2.62f * m, 0.24f * m, 0.24f * m, 0.24f * m, 8, 10, null, null);
            }
            // табличка над задними перилами: тёмно-зелёная, белая полоса, кружки линий
            float sw = W * 0.92f, sh = 0.55f * m, sy = yb + rh + 0.12f * m + sh * 0.5f, sz = L + cope * 0.5f;
            top.Box(new Vector3(0f, sy, sz), new Vector3(sw, sh, 0.06f * m), SignColor);
            top.Box(new Vector3(-sw * 0.45f, yb + rh + 0.06f * m, sz), new Vector3(0.05f * m, 0.14f * m, 0.05f * m), RailColor);
            top.Box(new Vector3(sw * 0.45f, yb + rh + 0.06f * m, sz), new Vector3(0.05f * m, 0.14f * m, 0.05f * m), RailColor);
            top.Box(new Vector3(0f, sy + sh * 0.28f, sz - 0.035f * m), new Vector3(sw * 0.9f, 0.05f * m, 0.01f * m), Color.white);
            int nl = LineColors != null ? LineColors.Length : 0;
            for (int i = 0; i < nl; i++)
            {
                float x = (i - (nl - 1) * 0.5f) * 0.36f * m;
                top.Disc(new Vector3(x, sy - sh * 0.1f, sz - 0.04f * m), Vector3.back, 0.14f * m, 12, LineColors[i]);
            }
            CityToonMesh.Make("Railings", transform, top.Build("Metro_Top"), Toon, true);
            CityToonMesh.Make("Globes", transform, glow.Build("Metro_Globes"), Glow != null ? Glow : Toon, false);
        }

        /// <summary>Стена по X = x, нормаль внутрь проёма (−s).</summary>
        private static void WallX(CityToonMesh t, float x, int s, float z0, float z1, float y0, float y1, Color col)
        {
            if (s > 0) t.Quad(new Vector3(x, y0, z1), new Vector3(x, y1, z1), new Vector3(x, y1, z0), new Vector3(x, y0, z0), col);   // лицом к −X
            else t.Quad(new Vector3(x, y0, z0), new Vector3(x, y1, z0), new Vector3(x, y1, z1), new Vector3(x, y0, z1), col);         // лицом к +X
        }

        private void Rail(CityToonMesh t, Vector3 a, Vector3 b, float h, float post, float bar, float m)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / (1.4f * m)) + 1);
            for (int i = 0; i < posts; i++)
            {
                Vector3 p = a + d * ((float)i / (posts - 1));
                t.Box(p + Vector3.up * h * 0.5f, new Vector3(post, h, post), RailColor);
                t.Box(p + Vector3.up * (h + 0.03f * m), new Vector3(post * 1.6f, 0.06f * m, post * 1.6f), RailColor * 0.85f);
            }
            t.Beam(a + Vector3.up * h, b + Vector3.up * h, post * 0.9f, RailColor);
            t.Beam(a + Vector3.up * (h * 0.45f), b + Vector3.up * (h * 0.45f), bar, RailColor);
            t.Beam(a + Vector3.up * (0.12f * m), b + Vector3.up * (0.12f * m), bar, RailColor);
            int bars = Mathf.Max(2, Mathf.FloorToInt(len / (0.22f * m)));
            for (int i = 1; i < bars; i++)
            {
                Vector3 p = a + d * ((float)i / bars);
                t.Box(p + Vector3.up * (h * 0.5f + 0.06f * m), new Vector3(bar, h - 0.12f * m, bar), RailColor);
            }
        }
    }
}
