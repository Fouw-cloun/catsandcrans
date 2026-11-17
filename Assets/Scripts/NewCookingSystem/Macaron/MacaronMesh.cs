using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// МЕШИ МАКАРОН — строятся кодом (в игре и в меню редактора), цвет в вершинах под шейдер
    /// «CatCooks/Food Heap» (альфа вершины = сколько узора текстуры).
    ///
    ///   Lathe   — тело вращения по профилю (r, y). Профиль обходить «по часовой» вокруг сечения
    ///             материала: верх — наружу, внешняя стенка — вниз, низ — внутрь, внутренняя — вверх.
    ///             Тогда передние грани смотрят наружу (Cross(вдоль круга, вдоль профиля) = наружу).
    ///             Повтор точки профиля = острая грань.
    ///   Puck    — капля теста в кольце / начинка: плоский купол, радиус 1, высота 1.
    ///   Shell   — скорлупка макарон: гладкий купол + «юбочка» (кружевной ободок) снизу; треснувшая —
    ///             купол в буграх и тёмных трещинах.
    ///   Dollop  — v23.61: отсаженная капля с хвостиком от носика, оседает по уровням.
    ///   ShellRise — v23.61: скорлупка поднимается в духовке (юбочка растёт по шагам).
    ///   Tube    — обрубок трубы (кольцо для отсадки): внутренний радиус ri, внешний ro, высота h.
    /// </summary>
    public static class MacaronMesh
    {
        public delegate Vector3 Warp(int profileIndex, float theta, Vector3 p);
        public delegate Color Paint(int profileIndex, float theta, Vector3 p);

        /// <summary>Тело вращения. prof: (r, y). Возвращает меш (новый, если m == null).</summary>
        public static Mesh Lathe(Mesh m, IList<Vector2> prof, int segs, Paint paint, Warp warp = null, string name = "Lathe")
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            int rows = prof.Count;
            for (int j = 0; j <= segs; j++)
            {
                float th = j / (float)segs * Mathf.PI * 2f;
                float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
                for (int i = 0; i < rows; i++)
                {
                    Vector3 p = new Vector3(prof[i].x * cs, prof[i].y, prof[i].x * sn);
                    if (warp != null && j < segs) p = warp(i, th, p);
                    v.Add(p);
                    c.Add(paint != null ? paint(i, th, p) : Color.white);
                }
            }
            // шов: последний столбец = первый (после деформации)
            if (warp != null)
                for (int i = 0; i < rows; i++) { v[segs * rows + i] = v[i]; }
            for (int j = 0; j < segs; j++)
                for (int i = 0; i < rows - 1; i++)
                {
                    if ((prof[i] - prof[i + 1]).sqrMagnitude < 1e-12f) continue;   // повтор точки — грань
                    int a = j * rows + i, b = a + 1, a2 = a + rows, b2 = b + rows;
                    t.Add(a); t.Add(a2); t.Add(b);
                    t.Add(b); t.Add(a2); t.Add(b2);
                }
            if (m == null) m = new Mesh();
            m.Clear();
            m.name = name;
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            // шов: нормали первого и последнего столбца усредняем
            Vector3[] n = m.normals;
            for (int i = 0; i < rows; i++)
            {
                Vector3 avg = (n[i] + n[segs * rows + i]).normalized;
                n[i] = avg; n[segs * rows + i] = avg;
            }
            m.normals = n;
            m.RecalculateBounds();
            return m;
        }

        private static Mesh _puck, _shell, _shellCracked;

        /// <summary>Капля теста / начинки: плоский купол радиус 1, высота 1, низ плоский.</summary>
        public static Mesh Puck()
        {
            if (_puck != null) return _puck;
            var prof = new List<Vector2>();
            for (int k = 0; k <= 8; k++)
            {
                float r = k / 8f;
                prof.Add(new Vector2(r * 0.97f, 0.12f + 0.88f * Mathf.Pow(Mathf.Max(0f, 1f - r * r), 0.7f)));
            }
            prof.Add(new Vector2(1f, 0.06f));
            prof.Add(new Vector2(0.98f, 0f)); prof.Add(new Vector2(0.98f, 0f));
            prof.Add(new Vector2(0f, 0f));
            _puck = Lathe(null, prof, 40, (i, th, p) => new Color(1f, 1f, 1f, 0.55f), null, "MacaronPuck (auto)");
            _puck.hideFlags = HideFlags.DontSave;
            return _puck;
        }

        /// <summary>Скорлупка: радиус 1, высота ~0.62 (купол 0.47 + юбочка 0.15). Цвет — белый, тон даёт материал.</summary>
        public static Mesh Shell(bool cracked)
        {
            if (!cracked && _shell != null) return _shell;
            if (cracked && _shellCracked != null) return _shellCracked;
            Mesh m = BuildShell(cracked, 1f, cracked ? "MacaronShellCracked (auto)" : "MacaronShell (auto)");
            if (cracked) _shellCracked = m; else _shell = m;
            return m;
        }

        public const int RiseSteps = 8;
        private static Mesh[] _rise;

        /// <summary>
        /// v23.61: скорлупка ПОДНИМАЕТСЯ в духовке. step 0 — гладкий купол без юбочки (как капля теста),
        /// RiseSteps — готовая юбочка. Высота купола одна; общий рост даёт масштаб противня.
        /// </summary>
        public static Mesh ShellRise(int step)
        {
            step = Mathf.Clamp(step, 0, RiseSteps);
            if (step == RiseSteps) return Shell(false);
            if (_rise == null) _rise = new Mesh[RiseSteps];
            if (_rise[step] != null) return _rise[step];
            float t = step / (float)RiseSteps;
            _rise[step] = BuildShell(false, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.15f) / 0.7f)), "MacaronShellRise" + step + " (auto)");
            return _rise[step];
        }

        /// <summary>feetK 0..1 — насколько выросла юбочка (0 — её нет, купол сидит на листе).</summary>
        private static Mesh BuildShell(bool cracked, float feetK, string name)
        {
            float feet = 0.15f * Mathf.Max(0.02f, feetK);
            var prof = new List<Vector2>();
            // купол: центр → край
            for (int k = 0; k <= 10; k++)
            {
                float r = k / 10f * 0.96f;
                float y = feet + 0.47f * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(r / 0.96f, 2.2f)), 0.55f);
                prof.Add(new Vector2(r, y));
            }
            int domeEnd = prof.Count - 1;
            prof.Add(prof[domeEnd]);                                    // грань купол/юбочка
            // юбочка: чуть шире купола, кружевная
            float wide = Mathf.Lerp(0.97f, 1.0f, feetK), bulge = Mathf.Lerp(0.97f, 1.03f, feetK);
            prof.Add(new Vector2(wide, feet * 0.9f));
            prof.Add(new Vector2(bulge, feet * 0.55f));
            prof.Add(new Vector2(wide, feet * 0.15f));
            prof.Add(new Vector2(0.97f, 0f)); prof.Add(new Vector2(0.97f, 0f));
            prof.Add(new Vector2(0f, 0f));
            int feetFrom = domeEnd + 2, feetTo = domeEnd + 4;
            float lace = feetK;
            Mesh m = Lathe(null, prof, 56,
                (i, th, p) =>
                {
                    if (i >= feetFrom && i <= feetTo) return new Color(Mathf.Lerp(1f, 1.04f, lace), Mathf.Lerp(1f, 1.02f, lace), Mathf.Lerp(1f, 0.98f, lace), 1f);   // юбочка — пористая
                    if (i > feetTo) return new Color(0.92f, 0.9f, 0.86f, 0.6f);                 // донышко
                    if (!cracked) return new Color(1f, 1f, 1f, 0.35f);                            // купол — гладкий
                    float crack = CrackAt(th, p.x * p.x + p.z * p.z);
                    return Color.Lerp(new Color(1f, 1f, 1f, 0.5f), new Color(0.55f, 0.45f, 0.35f, 1f), crack);
                },
                (i, th, p) =>
                {
                    if (i >= feetFrom && i <= feetTo)
                    {
                        // кружевная юбочка: частые бугорки по кругу
                        float k = 1f + lace * (0.035f * (0.5f + 0.5f * Mathf.Sin(th * 41f)) + 0.02f * Mathf.PerlinNoise(th * 6f, i * 1.3f));
                        return new Vector3(p.x * k, p.y + lace * 0.012f * Mathf.Sin(th * 29f + i), p.z * k);
                    }
                    if (cracked && i <= domeEnd && i > 0)
                    {
                        float crack = CrackAt(th, p.x * p.x + p.z * p.z);
                        float bump = (Mathf.PerlinNoise(p.x * 3f + 5f, p.z * 3f + 2f) - 0.5f) * 0.08f;
                        return new Vector3(p.x, p.y + bump - crack * 0.05f, p.z);
                    }
                    return p;
                }, name);
            m.hideFlags = HideFlags.DontSave;
            return m;
        }

        public const int DollopLevels = 6;
        /// <summary>v23.62: на каком уровне хвостик остаётся (осел наполовину и подогнулся).</summary>
        public const int DollopRest = 3;
        private static Mesh[] _dollop;

        /// <summary>
        /// v23.61: ОТСАЖЕННАЯ КАПЛЯ ТЕСТА (радиус 1, высота ~1 без хвостика). level 0 — уже осела, гладкий купол;
        /// DollopLevels — только что отсадили: в центре «хвостик» от носика шприца с лёгким завитком.
        /// Хвостик оседает за пару секунд (меняется level) — как у настоящего макаронажа.
        /// </summary>
        public static Mesh Dollop(int level)
        {
            level = Mathf.Clamp(level, 0, DollopLevels);
            if (_dollop == null) _dollop = new Mesh[DollopLevels + 1];
            if (_dollop[level] != null) return _dollop[level];
            float pk = level / (float)DollopLevels;
            var prof = new List<Vector2>();
            const int N = 16;
            for (int k = 0; k <= N; k++)
            {
                float r = k / (float)N;
                float y = 0.12f + 0.88f * Mathf.Pow(Mathf.Max(0f, 1f - r * r), 0.7f);
                y += pk * 0.38f * Mathf.Exp(-(r / 0.18f) * (r / 0.18f));          // хвостик от носика
                prof.Add(new Vector2(r * 0.97f, y));
            }
            prof.Add(new Vector2(1f, 0.06f));
            prof.Add(new Vector2(0.98f, 0f)); prof.Add(new Vector2(0.98f, 0f));
            prof.Add(new Vector2(0f, 0f));
            Mesh m = Lathe(null, prof, 48, (i, th, p) => new Color(1f, 1f, 1f, 0.55f),
                (i, th, p) =>
                {
                    if (i > N || pk <= 0f) return p;
                    // завиток: хвостик чуть закручен и наклонён, по краю купола — еле заметная волна
                    float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                    float swirl = pk * 0.05f * Mathf.Sin(th * 3f + r * 14f) * Mathf.Exp(-(r / 0.45f) * (r / 0.45f));
                    // v23.62: чем ниже осел хвостик, тем сильнее он подогнут набок (как у настоящей отсадки)
                    float lean = (0.08f + 0.5f * (1f - pk)) * Mathf.Exp(-(r / 0.25f) * (r / 0.25f)) * Mathf.Max(0f, p.y - 0.85f);
                    return new Vector3(p.x + lean, p.y + swirl, p.z);
                }, "MacaronDollop" + level + " (auto)");
            m.hideFlags = HideFlags.DontSave;
            _dollop[level] = m;
            return m;
        }

        /// <summary>Трещины на куполе: несколько ломаных лучей от центра.</summary>
        private static float CrackAt(float th, float r2)
        {
            float best = 0f;
            for (int k = 0; k < 4; k++)
            {
                float a = k * 1.7f + 0.4f + Mathf.Sin(r2 * 9f + k) * 0.18f;
                float d = Mathf.Abs(Mathf.DeltaAngle(th * Mathf.Rad2Deg, a * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                float w = 0.07f + 0.03f * k;
                if (r2 > 0.05f && r2 < 0.9f) best = Mathf.Max(best, Mathf.Clamp01(1f - d / w));
            }
            return best;
        }

        /// <summary>Обрубок трубы: кольцо для отсадки. Срезы чуть светлее (свежий металл).</summary>
        public static Mesh Tube(float ri, float ro, float h, Color body, Color cut, int segs = 40)
        {
            var prof = new List<Vector2>
            {
                new Vector2(ri, h), new Vector2(ro, h), new Vector2(ro, h),
                new Vector2(ro, 0f), new Vector2(ro, 0f),
                new Vector2(ri, 0f), new Vector2(ri, 0f),
                new Vector2(ri, h),
            };
            return Lathe(null, prof, segs, (i, th, p) => i <= 1 ? cut : body, null, "PipeRing");
        }
    }
}
