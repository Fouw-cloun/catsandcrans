using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ГДЕ СТРУЯ БЬЁТ — мультяшный всплеск. Только визуал, у каждого свой.
    ///  • Crown(...) — корона: кольцо растёт, поднимается и рассыпается ячейками (аниме-всплеск);
    ///  • FoamPatch — пенное пятно под струёй: кольца пены бегут наружу, в центре белая шапка.
    ///  • v9.1 — «СЛЕД РИКОШЕТА»: 2–3 слоя нарисованных карточек-всплесков (зубчатая молочная
    ///    стенка с каплями на кончиках, как на картинках) — взлетают из точки удара,
    ///    всегда повёрнуты к камере, задний слой крупнее и темнее, передний — светлее.
    /// Короны берутся из пула (без мусора в памяти), обновляются одним скрытым объектом.
    /// </summary>
    public static class LiquidImpactFx
    {
        private const int PoolSize = 24;
        private static readonly int DeepId = Shader.PropertyToID("_Deep");
        private static readonly int MidId = Shader.PropertyToID("_Mid");
        private static readonly int LightId = Shader.PropertyToID("_Light");
        private static readonly int FoamId = Shader.PropertyToID("_FoamColor");
        private static readonly int AgeId = Shader.PropertyToID("_Age");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int SpinId = Shader.PropertyToID("_Spin");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        private class CrownFx
        {
            public Transform T; public MeshRenderer R; public float Born, Life, R0, H0; public bool Busy;
            public Transform Follow; public Vector3 Normal;
        }

        private static Mesh _crownMesh, _discMesh;
        private static Material _mat;
        private static readonly List<CrownFx> _pool = new List<CrownFx>();
        private static MaterialPropertyBlock _mpb;
        private static Runner _runner;

        private sealed class Runner : MonoBehaviour
        {
            private void LateUpdate() { Step(Time.time); StepCards(Time.time); }
        }

        public static Material Mat
        {
            get
            {
                if (_mat != null) return _mat;
                Shader sh = Shader.Find("CatCooks/Liquid Splash");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                _mat = new Material(sh) { name = "CC Liquid Splash (auto)", hideFlags = HideFlags.DontSave };
                return _mat;
            }
        }

        private static bool Ensure()
        {
            if (!Application.isPlaying) return false;
            if (_runner != null) return true;
            var go = new GameObject("CC Liquid Impact FX (auto)") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
            _mpb = new MaterialPropertyBlock();
            _pool.Clear();
            for (int i = 0; i < PoolSize; i++)
            {
                var c = new GameObject("Crown") { hideFlags = HideFlags.DontSave };
                c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = CrownMesh;
                var r = c.AddComponent<MeshRenderer>();
                r.sharedMaterial = Mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                c.SetActive(false);
                _pool.Add(new CrownFx { T = c.transform, R = r });
            }
            BuildCards(go.transform);
            return true;
        }

        // ─────────────────────── v9.1: карточки-всплески ───────────────────────

        private class CardFx
        {
            public Transform T; public MeshRenderer R; public float Born, Life, W, H, Depth, Tone, Mirror; public bool Busy;
            public Vector3 Pos, Up; public Color Col;
            public Transform Follow; public Vector3 FollowOff;
            public float Roll0, RollSpeed, Yaw0, YawAmp, YawFreq, Phase;
            public float Angle, AngSpeed, Ring0, Ring1, Flare, Delay;   // v10.1: коронка по кругу
        }

        private const int CardPool = 72;
        private static readonly List<CardFx> _cards = new List<CardFx>();
        private static Mesh _cardMesh;
        private static Material[] _cardMats;
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        private static Camera _cam;

        /// <summary>Сила «следа рикошета»: 0 — выкл, 1 — норма, 2 — вдвое крупнее.</summary>
        public static float SplashCards = 3f;                 // v9.2: втрое крупнее
        /// <summary>v10.4: не чаще, чем раз в столько секунд на одну струю.</summary>
        public static float CardCooldown = 0.24f;
        private static readonly Dictionary<Transform, float> _lastCards = new Dictionary<Transform, float>();

        /// <summary>v10.4: прогрев — пул, материалы и маски создаются заранее (а не при первом проливе).</summary>
        public static void Warmup() { Ensure(); }

        /// <summary>Материалы всплесков для прогрева шейдеров.</summary>
        public static void CollectMaterials(List<Material> into)
        {
            if (!Ensure()) return;
            into.Add(Mat);
            if (_cardMats != null) foreach (Material m in _cardMats) if (m != null) into.Add(m);
        }

        /// <summary>Квадрат с опорой внизу посередине (растёт вверх из точки удара).</summary>
        private static Mesh CardMesh
        {
            get
            {
                if (_cardMesh != null) return _cardMesh;
                _cardMesh = new Mesh { name = "CC Splash Card", hideFlags = HideFlags.DontSave };
                _cardMesh.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) };
                _cardMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                _cardMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                _cardMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                _cardMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                _cardMesh.RecalculateBounds();
                return _cardMesh;
            }
        }

        private static void BuildCards(Transform root)
        {
            _cards.Clear();
            Shader sh = Shader.Find("CatCooks/Toon Drop");
            if (sh == null) return;                                               // нет шейдера — без карточек
            _cardMats = new Material[3];
            for (int k = 0; k < 3; k++)
            {
                _cardMats[k] = new Material(sh) { name = "CC Splash Card " + k + " (auto)", hideFlags = HideFlags.DontSave };
                _cardMats[k].mainTexture = SplashMask(k);
                _cardMats[k].SetVector("_NearFade", new Vector4(0.25f, 0.7f, 0f, 0f));
                _cardMats[k].SetFloat("_OutlineDark", 0.62f);
                _cardMats[k].renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 2 + k;   // задний → передний
            }
            for (int i = 0; i < CardPool; i++)
            {
                var c = new GameObject("SplashCard") { hideFlags = HideFlags.DontSave };
                c.transform.SetParent(root, false);
                c.AddComponent<MeshFilter>().sharedMesh = CardMesh;
                var r = c.AddComponent<MeshRenderer>();
                r.sharedMaterial = _cardMats[i % 3];
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                c.SetActive(false);
                _cards.Add(new CardFx { T = c.transform, R = r });
            }
        }

        // ── v10: ФЛИПБУК всплеска (как в наборах water flipbooks): 8 кадров 4×2 ──
        private const int FbCols = 4, FbRows = 2, FbFrames = 8, FbCell = 112;

        /// <summary>
        /// Атлас 4×2 кадров для «CatCooks/Toon Drop» (R форма, G свет, B блик). Кадры: стенка
        /// поднимается → зубцы вытягиваются → капли отрываются → стенка оседает и рассыпается.
        /// Стиль флипбука: два тона (тень справа), светлый блик по верхне-левому краю, БЕЗ тёмного контура.
        /// </summary>
        private static Texture2D SplashMask(int variant)
        {
            int W = FbCell * FbCols, H = FbCell * FbRows;
            var rnd = new System.Random(911 + variant * 37);
            int spikes = 6 + variant + rnd.Next(2);
            var sc = new float[spikes]; var sa = new float[spikes]; var sw = new float[spikes]; var sl = new float[spikes];
            for (int k = 0; k < spikes; k++)
            {
                float t = (k + 0.5f) / spikes + ((float)rnd.NextDouble() - 0.5f) * 0.1f;
                sc[k] = Mathf.Lerp(-0.85f, 0.85f, t);
                float mid = 1f - Mathf.Abs(sc[k]) * 0.35f;
                sa[k] = Mathf.Lerp(0.22f, 0.44f, (float)rnd.NextDouble()) * mid;
                sw[k] = Mathf.Lerp(0.06f, 0.1f, (float)rnd.NextDouble());
                sl[k] = ((float)rnd.NextDouble() - 0.5f) * 0.3f + sc[k] * 0.7f;    // наклон зубца наружу
            }
            var px = new Color[W * H];
            var core = new bool[FbCell * FbCell];
            var alpha = new float[FbCell * FbCell];
            for (int f = 0; f < FbFrames; f++)
            {
                float t = f / (float)(FbFrames - 1);
                float riseF = Mathf.Sin(Mathf.Clamp01(t * 1.7f) * Mathf.PI * 0.5f);
                int ox = (f % FbCols) * FbCell, oy = (FbRows - 1 - f / FbCols) * FbCell;    // кадр 0 — слева сверху
                for (int y = 0; y < FbCell; y++)
                for (int x = 0; x < FbCell; x++)
                {
                    float acc = 0f;
                    for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float u = ((x + 0.25f + sx * 0.5f) / FbCell) * 2f - 1f;
                        float v = (y + 0.25f + sy * 0.5f) / FbCell;
                        if (FbInside(u, v, t, sc, sa, sw, sl, variant)) acc += 0.25f;
                    }
                    alpha[y * FbCell + x] = acc;
                    core[y * FbCell + x] = acc > 0.5f;
                }
                for (int y = 0; y < FbCell; y++)
                for (int x = 0; x < FbCell; x++)
                {
                    int i = y * FbCell + x;
                    float a = alpha[i];
                    float u = (x + 0.5f) / FbCell * 2f - 1f, v = (y + 0.5f) / FbCell;
                    // два тона: тень справа (граница волнистая), внутренняя стенка короны темнее
                    float shadeLine = 0.15f + 0.12f * Mathf.Sin(v * 7f + variant) + v * 0.2f;
                    float lit = u > shadeLine ? 0.6f : 1f;
                    float rim = FbRim(u, t, riseF);
                    if (v > rim - 0.04f && v < rim + 0.05f && Mathf.Abs(u) < 0.5f) lit = Mathf.Min(lit, 0.55f);
                    // блик: верхне-левый край (соседа сверху-слева нет) — светлая полоса, как в флипбуках
                    bool hl = false;
                    if (core[i])
                    {
                        int hx = x - 3, hy = y + 3;
                        if (hx < 0 || hy >= FbCell || !core[hy * FbCell + hx]) hl = true;
                    }
                    float g = 0.3f + 0.7f * lit;
                    px[(oy + y) * W + ox + x] = new Color(a, g, hl ? a : 0f, 1f);
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true, true)
            {
                name = "CC Splash Flipbook " + variant + " (auto)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Кромка «чаши»: в середине ниже, к краям выше (корона сбоку); растёт и оседает.</summary>
        private static float FbRim(float u, float t, float rise)
        {
            return (0.1f + 0.3f * rise * (1f - 0.5f * t)) * (0.75f + 0.4f * u * u) + 0.025f * Mathf.Sin(u * 6f + 1.3f);
        }

        /// <summary>Гладкий шум 0..1 (для эрозии и дырок — круглые, не квадратные).</summary>
        private static float Vn(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            float ab = a + (b - a) * fx, cd = c + (d - c) * fx;
            return ab + (cd - ab) * fy;
        }

        private static bool FbInside(float u, float v, float t, float[] sc, float[] sa, float[] sw, float[] sl, int variant)
        {
            float rise = Mathf.Sin(Mathf.Clamp01(t * 1.7f) * Mathf.PI * 0.5f);
            // растворение в конце — эрозия гладким шумом (как в водопадах)
            if (t > 0.5f)
            {
                float er = (t - 0.5f) / 0.5f;
                float nz = Vn((u + 1f) * 4.5f, v * 4.5f, variant + 7) * 0.65f + Vn((u + 1f) * 11f, v * 11f, variant + 3) * 0.35f;
                if (nz < er * 1.1f - 0.05f) return false;
            }
            // стенка раскрывается кверху (венчик)
            float half = (0.36f + 0.2f * t) + 0.95f * v * rise * v / (v + 0.12f) + 0.05f;
            float rim = FbRim(u, t, rise);
            bool body = Mathf.Abs(u) <= half && v <= rim;
            if (body && t > 0.3f && v > 0.05f)
            {
                float hz = Vn((u + 1f) * 6f, v * 6f, variant + 11);
                if (hz > 1.05f - (t - 0.3f) * 0.45f) body = false;                  // дырки в простыне
            }
            if (body) return true;
            // зубцы: толстые у основания, острые кверху; капля на кончике отрывается и летит
            float detach = Mathf.Clamp01((t - 0.4f) / 0.3f);
            for (int k = 0; k < sc.Length; k++)
            {
                float h = sa[k] * rise;
                float r0 = FbRim(sc[k], t, rise);
                float cx = sc[k] + sl[k] * (v - r0);
                float w = sw[k] * (1f - 0.5f * detach);
                float neck = r0 + h * (1f - detach * 0.55f);
                if (v > r0 - 0.03f && v < neck)
                {
                    float k01 = Mathf.Clamp01((v - r0) / Mathf.Max(1e-3f, h));
                    float ww = w * (1.5f - 1.05f * Mathf.Pow(k01, 0.7f));
                    if (Mathf.Abs(u - cx) < ww) return true;
                }
                float tip = r0 + h;
                float r = sw[k] * (0.7f + 0.15f * rise);
                float fly = detach * 0.22f;
                float dcx = sc[k] + sl[k] * (h + fly);
                float dcy = tip + r * 0.6f + fly - detach * detach * 0.1f;
                float dy = (v - dcy) * 0.85f;
                if (rise > 0.25f && (u - dcx) * (u - dcx) + dy * dy < r * r) return true;
                if (k % 2 == 0 && detach > 0.15f)
                {
                    float r2 = r * 0.55f, cx2 = dcx + sl[k] * 0.15f, cy2 = dcy + r * 2.2f * detach + 0.03f;
                    if ((u - cx2) * (u - cx2) + (v - cy2) * (v - cy2) < r2 * r2) return true;
                }
            }
            return false;
        }

        private static float Hash(int x, int y, int s)
        {
            int h = x * 374761393 + y * 668265263 + s * 982451653;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }

        /// <summary>
        /// v10.1: «след рикошета» — КОРОНКА ПО КРУГУ вокруг точки удара: 6–9 карточек-флипбуков
        /// стоят кольцом и смотрят наружу (не поперёк струи). Кольцо раскрывается, стенки
        /// отгибаются наружу и медленно закручиваются вокруг струи. radius — радиус удара, м.
        /// </summary>
        public static void SplashCardsAt(Vector3 pos, Vector3 normal, float radius, float height, Color liquid, float life,
                                         Transform follow = null)
        {
            if (SplashCards <= 0f || !Ensure() || _cards.Count == 0) return;
            // v10.4: не чаще раза в CardCooldown на одну струю — коронки не громоздятся (перерисовка прозрачного — дорого)
            if (follow != null)
            {
                float last;
                if (_lastCards.TryGetValue(follow, out last) && Time.time - last < CardCooldown) return;
                _lastCards[follow] = Time.time;
                if (_lastCards.Count > 64) _lastCards.Clear();
            }
            Vector3 up = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            int n = Random.Range(6, 10);
            float a0 = Random.value * Mathf.PI * 2f;
            float spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.4f, 1.1f);      // рад/с — закрутка вокруг струи
            float k = Mathf.Sqrt(SplashCards);
            float R = radius * k;
            for (int i = 0; i < n; i++)
            {
                CardFx c = TakeCard(i);
                if (c == null) return;
                c.Busy = true;
                c.Delay = Random.Range(0f, 0.06f);
                c.Born = Time.time + c.Delay;
                c.Life = life * Random.Range(0.85f, 1.15f);
                c.Angle = a0 + (i + Random.Range(-0.2f, 0.2f)) / n * Mathf.PI * 2f;
                c.AngSpeed = spin;
                c.Ring0 = R * Random.Range(0.55f, 0.7f);
                c.Ring1 = R * Random.Range(1.3f, 1.7f);
                c.W = 2f * c.Ring1 * Mathf.Sin(Mathf.PI / n) * Random.Range(1.5f, 1.9f);   // с перекрытием — сплошная стенка
                c.H = height * SplashCards * 0.6f * Random.Range(0.75f, 1.2f);
                c.Flare = Random.Range(12f, 30f);                                       // стенка отгибается наружу
                c.Tone = Random.Range(0.92f, 1.03f);
                c.Mirror = Random.value < 0.5f ? -1f : 1f;
                c.Pos = pos; c.Up = up; c.Col = liquid;
                c.Follow = follow;
                c.FollowOff = follow != null ? pos - follow.position : Vector3.zero;
                c.Phase = Random.value * 6.28f;
                c.T.gameObject.SetActive(true);
                PlaceCard(c, 0f);
            }
        }

        private static CardFx TakeCard(int layer)
        {
            CardFx best = null;
            float oldest = float.MaxValue;
            for (int i = layer % 3; i < _cards.Count; i += 3)                      // слой → свой материал (порядок рисования)
            {
                CardFx k = _cards[i];
                if (!k.Busy) return k;
                if (k.Born < oldest) { oldest = k.Born; best = k; }
            }
            return best;
        }

        private static void PlaceCard(CardFx c, float age)
        {
            // всплеск едет за точкой падения струи (не отстаёт)
            if (c.Follow != null && c.Follow.gameObject.activeInHierarchy)
                c.Pos = Vector3.Lerp(c.Pos, c.Follow.position + c.FollowOff * (1f - age), 0.5f);
            float a = Mathf.Clamp01(age);
            float tSec = a * c.Life;
            Vector3 up = c.Up;
            Vector3 t1 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(up, t1);
            float ang = c.Angle + c.AngSpeed * tSec;                                    // круговая закрутка
            Vector3 dir = t1 * Mathf.Cos(ang) + t2 * Mathf.Sin(ang);                    // наружу от струи
            float grow = 1f - (1f - a) * (1f - a);
            float ring = Mathf.Lerp(c.Ring0, c.Ring1, grow);
            float rise = a < 0.15f ? Mathf.SmoothStep(0.6f, 1.06f, a / 0.15f) : Mathf.Lerp(1.06f, 0.95f, (a - 0.15f) / 0.85f);
            float h = Mathf.Max(0.001f, c.H * rise);
            float w = Mathf.Max(0.001f, c.W * Mathf.Lerp(0.8f, 1.05f, grow));
            w *= 1f + 0.06f * Mathf.Sin(c.Phase * 2f + tSec * 14f) * (1f - a);         // «дышит»
            c.T.position = c.Pos + dir * ring - up * (h * 0.12f);                       // низ — в молоке
            // карточка — касательная к кругу, лицом наружу; верх отгибается наружу (венчик)
            Quaternion face = Quaternion.LookRotation(dir, up);
            Quaternion flare = Quaternion.AngleAxis(c.Flare * grow, Vector3.right);       // +X: верх уходит наружу
            c.T.rotation = face * flare;
            c.T.localScale = new Vector3(w * c.Mirror, h, 1f);
            float fade = a < 0.85f ? 1f : 1f - (a - 0.85f) / 0.15f;                   // исчезает кадрами флипбука
            Color tc = new Color(c.Col.r * c.Tone, c.Col.g * c.Tone, c.Col.b * c.Tone, Mathf.Clamp01(fade));
            _mpb.Clear();
            _mpb.SetColor(TintId, tc);
            int fr = Mathf.Clamp(Mathf.FloorToInt(a * FbFrames), 0, FbFrames - 1);
            _mpb.SetVector(MainTexStId, new Vector4(1f / FbCols, 1f / FbRows,
                                                    (fr % FbCols) / (float)FbCols, (FbRows - 1 - fr / FbCols) / (float)FbRows));
            c.R.SetPropertyBlock(_mpb);
        }

        private static void StepCards(float now)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                CardFx c = _cards[i];
                if (!c.Busy) continue;
                float age = (now - c.Born) / Mathf.Max(0.05f, c.Life);
                if (age >= 1f) { c.Busy = false; c.T.gameObject.SetActive(false); continue; }
                PlaceCard(c, Mathf.Max(0f, age));
            }
        }

        /// <summary>Открытое кольцо, расширяющееся кверху (как корона капли). uv: x — обхват, y — высота.</summary>
        public static Mesh CrownMesh
        {
            get
            {
                if (_crownMesh != null) return _crownMesh;
                const int S = 24;
                float[] hs = { 0f, 0.3f, 0.62f, 0.85f, 1f };
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                for (int k = 0; k < hs.Length; k++)
                {
                    float h = hs[k];
                    float r = 1f + h * h * 0.7f;                  // края короны раскрываются наружу
                    for (int i = 0; i <= S; i++)
                    {
                        float a = i / (float)S * Mathf.PI * 2f;
                        Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        v.Add(d * r + Vector3.up * h);
                        n.Add((d - Vector3.up * 0.3f * h).normalized);
                        uv.Add(new Vector2(i / (float)S, h));
                    }
                }
                for (int k = 0; k < hs.Length - 1; k++)
                for (int i = 0; i < S; i++)
                {
                    int a0 = k * (S + 1) + i, a1 = a0 + 1, b0 = a0 + S + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
                }
                _crownMesh = new Mesh { name = "CC Splash Crown", hideFlags = HideFlags.DontSave };
                _crownMesh.SetVertices(v); _crownMesh.SetNormals(n); _crownMesh.SetUVs(0, uv); _crownMesh.SetTriangles(t, 0);
                _crownMesh.RecalculateBounds();
                return _crownMesh;
            }
        }

        /// <summary>Круг-пятно. uv: x — угол 0..1, y — радиус 0..1.</summary>
        public static Mesh DiscMesh
        {
            get
            {
                if (_discMesh != null) return _discMesh;
                const int S = 32, Rn = 6;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                for (int k = 0; k <= Rn; k++)
                {
                    float r = k / (float)Rn;
                    for (int i = 0; i <= S; i++)
                    {
                        float a = i / (float)S * Mathf.PI * 2f;
                        v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                        n.Add(Vector3.up);
                        uv.Add(new Vector2(i / (float)S, r));
                    }
                }
                for (int k = 0; k < Rn; k++)
                for (int i = 0; i < S; i++)
                {
                    int a0 = k * (S + 1) + i, a1 = a0 + 1, b0 = a0 + S + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(a1); t.Add(b0); t.Add(a1); t.Add(b1); t.Add(b0);
                }
                _discMesh = new Mesh { name = "CC Splash Disc", hideFlags = HideFlags.DontSave };
                _discMesh.SetVertices(v); _discMesh.SetNormals(n); _discMesh.SetUVs(0, uv); _discMesh.SetTriangles(t, 0);
                _discMesh.RecalculateBounds();
                return _discMesh;
            }
        }

        /// <summary>
        /// Корона-всплеск: радиус struiRadius — толщина струи, м. Живёт ~0.3–0.5 с.
        /// </summary>
        public static void Crown(Vector3 pos, Vector3 normal, float streamRadius, LiquidPourProfile prof, Color liquid)
        {
            Crown(pos, normal, streamRadius, prof, liquid, null);
        }

        /// <summary>v9.2: follow — точка падения струи; корона и «след рикошета» едут за ней.</summary>
        public static void Crown(Vector3 pos, Vector3 normal, float streamRadius, LiquidPourProfile prof, Color liquid, Transform follow)
        {
            if (!Ensure()) return;
            CrownFx c = null;
            float oldest = float.MaxValue;
            foreach (CrownFx k in _pool)
            {
                if (!k.Busy) { c = k; break; }
                if (k.Born < oldest) { oldest = k.Born; c = k; }
            }
            if (c == null) return;
            if (prof == null) prof = LiquidPourProfile.For(liquid);
            float visc = prof.Viscosity;
            c.Busy = true;
            c.Born = Time.time;
            c.Life = Mathf.Lerp(0.34f, 0.6f, visc) * Random.Range(0.85f, 1.15f);
            c.R0 = Mathf.Max(0.01f, streamRadius) * prof.SplashSize * 0.5f * Random.Range(0.8f, 1.2f);
            c.H0 = c.R0 * Mathf.Lerp(1.3f, 0.5f, visc) * Random.Range(0.8f, 1.2f);
            c.T.position = pos + normal * 0.002f;
            c.T.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.value * 360f, 0f);
            c.T.gameObject.SetActive(true);
            c.Follow = follow;
            c.Normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            prof.Resolve(liquid, out Color deep, out Color mid, out Color light, out Color foam);
            if (!prof.FromLiquidColor) { deep = prof.Deep; mid = prof.Mid; light = prof.Light; foam = prof.Foam; }
            _mpb.Clear();
            _mpb.SetColor(DeepId, deep); _mpb.SetColor(MidId, mid); _mpb.SetColor(LightId, light); _mpb.SetColor(FoamId, foam);
            _mpb.SetFloat(ModeId, 0f);
            _mpb.SetFloat(SeedId, Random.value * 50f);
            _mpb.SetFloat(AgeId, 0f);
            _mpb.SetFloat(OpacityId, Mathf.Max(0.7f, prof.Opacity));
            c.R.SetPropertyBlock(_mpb);
            Scale(c, 0f);

            // v9.1: «след рикошета» — слои карточек-всплесков (вязкое — ниже и реже)
            if (visc < 0.75f && Random.value < Mathf.Lerp(0.85f, 0.35f, visc))
                SplashCardsAt(pos, normal, c.R0, c.H0 * Mathf.Lerp(3.2f, 1.6f, visc), liquid, Mathf.Lerp(0.5f, 0.7f, visc), follow);

            // v9: РИКОШЕТ — с зубцов коронки вылетают капли-хвостики (вытянуты по скорости),
            // часть — с круглой каплей на конце, как на картинках с молоком.
            int tips = Mathf.Max(2, Mathf.RoundToInt(Mathf.Lerp(8f, 3f, visc) * Random.Range(0.8f, 1.2f)));
            Vector3 up = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Vector3 t1 = c.T.right, t2 = c.T.forward;
            float sizeK = Mathf.Clamp(c.R0 / 0.05f, 0.5f, 1.8f);
            for (int i = 0; i < tips; i++)
            {
                float a = (i + Random.Range(-0.3f, 0.3f)) / tips * Mathf.PI * 2f;
                Vector3 h = t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a);
                Vector3 at = pos + h * (c.R0 * 0.9f) + up * (c.H0 * 0.6f);
                float sp = Mathf.Lerp(1.5f, 0.6f, visc) * Random.Range(0.7f, 1.3f) * Mathf.Sqrt(sizeK);
                Vector3 v = (h * Random.Range(0.5f, 0.9f) + up * Random.Range(0.8f, 1.4f)).normalized * sp;
                LiquidFx.Jet(at, v, c.R0 * Random.Range(0.16f, 0.26f), liquid, Random.Range(0.35f, 0.6f));
                if (Random.value < 0.6f)
                    LiquidFx.Drop(at + v * 0.04f, v * 1.08f, c.R0 * Random.Range(0.2f, 0.3f), liquid, Random.Range(0.4f, 0.7f));
            }
        }

        private static void Scale(CrownFx c, float age)
        {
            // растёт быстро и плавно тормозит; поднимается и опадает
            float grow = 1f - (1f - age) * (1f - age) * (1f - age);
            float r = c.R0 * Mathf.Lerp(0.55f, 1.5f, grow);
            float h = c.H0 * Mathf.Sin(Mathf.Clamp01(age * 1.15f) * Mathf.PI) * 1.1f + c.H0 * 0.05f;
            c.T.localScale = new Vector3(r, Mathf.Max(0.0005f, h), r);
        }

        private static void Step(float now)
        {
            foreach (CrownFx c in _pool)
            {
                if (!c.Busy) continue;
                float age = (now - c.Born) / Mathf.Max(0.05f, c.Life);
                if (age >= 1f) { c.Busy = false; c.T.gameObject.SetActive(false); continue; }
                Scale(c, age);
                if (c.Follow != null && c.Follow.gameObject.activeInHierarchy)
                    c.T.position = Vector3.Lerp(c.T.position, c.Follow.position + c.Normal * 0.002f, 0.5f);
                c.R.GetPropertyBlock(_mpb);
                _mpb.SetFloat(AgeId, age);
                c.R.SetPropertyBlock(_mpb);
            }
        }

        // ─────────────────────── пенное пятно ───────────────────────

        /// <summary>Пятно пены под струёй — одно на струю, плавно появляется/исчезает.</summary>
        public sealed class FoamPatch
        {
            private readonly Transform _t;
            private readonly MeshRenderer _r;
            private readonly MaterialPropertyBlock _pb = new MaterialPropertyBlock();
            private float _k, _r0;
            private Vector3 _pos, _nrm = Vector3.up;

            public FoamPatch(Transform parent)
            {
                var go = new GameObject("FoamPatch") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = DiscMesh;
                _r = go.AddComponent<MeshRenderer>();
                _r.sharedMaterial = Mat;
                _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _r.receiveShadows = false;
                _t = go.transform;
                go.SetActive(false);
            }

            public void Tick(bool on, Vector3 pos, Vector3 normal, float streamRadius, LiquidPourProfile prof, Color liquid, float dt)
            {
                _k = Mathf.MoveTowards(_k, on ? 1f : 0f, dt * (on ? 4f : 1.5f));
                if (_k <= 0.001f) { if (_t.gameObject.activeSelf) _t.gameObject.SetActive(false); return; }
                if (!_t.gameObject.activeSelf) { _t.gameObject.SetActive(true); _pos = pos; _nrm = normal; }
                if (on)
                {
                    // точка падения гуляет — пятно догоняет мягко
                    float f = 1f - Mathf.Exp(-dt / 0.08f);
                    _pos = Vector3.Lerp(_pos, pos, f);
                    _nrm = Vector3.Slerp(_nrm, normal, f).normalized;
                    _r0 = Mathf.Lerp(_r0 <= 0f ? streamRadius : _r0, streamRadius, f);
                }
                if (prof == null) prof = LiquidPourProfile.For(liquid);
                float rad = Mathf.Max(0.02f, _r0 * prof.FoamPatch) * Mathf.Lerp(0.6f, 1f, _k);
                _t.SetPositionAndRotation(_pos + _nrm * 0.003f, Quaternion.FromToRotation(Vector3.up, _nrm));
                _t.localScale = new Vector3(rad, 1f, rad);
                prof.Resolve(liquid, out Color deep, out Color mid, out Color light, out Color foam);
                if (!prof.FromLiquidColor) { deep = prof.Deep; mid = prof.Mid; light = prof.Light; foam = prof.Foam; }
                _pb.SetColor(DeepId, deep); _pb.SetColor(MidId, mid); _pb.SetColor(LightId, light); _pb.SetColor(FoamId, foam);
                _pb.SetFloat(ModeId, 1f);
                _pb.SetFloat(AgeId, 1f - _k);
                _pb.SetFloat(SpinId, Mathf.Lerp(1f, 0.25f, prof.Viscosity));
                _pb.SetFloat(OpacityId, 1f);
                _r.SetPropertyBlock(_pb);
            }

            public void Destroy()
            {
                if (_t != null) SafeDestroy.Kill(_t.gameObject);
            }
        }
    }
}
