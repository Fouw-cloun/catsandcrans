using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// НЕБО ГОРОДА — мультяшные летающие штуки вокруг уровня, на виду с этажа игрока: лайнер высоко по прямой,
    /// маленькие самолёты со следом кругами, самолёт с рекламным баннером, дирижабль с надписью, воздушные шары
    /// дрейфуют по ветру, изредка стайка птиц клином. Всё собирается из примитивов (CityToonMesh), шейдер Toon.
    /// Двигается в игре (галка «Летать в редакторе» — посмотреть без Play). Объект ставится в центр уровня
    /// на высоте этажа игрока (ViewHeight) — «Собрать» в инспекторе.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("CatCooks/Город/Небо (самолёты, дирижабль, шары, птицы)")]
    public sealed class CitySky : MonoBehaviour
    {
        public enum FlyerKind { Airliner = 0, SmallPlane = 1, BannerPlane = 2, Blimp = 3, Balloon = 4, Birds = 5 }

        [System.Serializable]
        public sealed class Flyer
        {
            public FlyerKind Kind;
            public Transform T;
            public Transform Prop;
            public List<Transform> Wings = new List<Transform>();
            public float R, Rz, H, Speed, Phase, Dir = 1f, Bob, Timer, S;
            public Vector3 A, B;
        }

        public CityBlockGenerator City;
        [Tooltip("Высота глаз игрока над землёй, м — от неё считаются высоты полётов")]
        public float ViewHeight = 25f;
        [Tooltip("Единиц сцены в метре. 0 — у «Пропсы и машины» (или 3.5)")]
        [Min(0f)] public float UnitsPerMeter = 0f;
        public int Seed = 3;
        [Range(0, 3)] public int Airliners = 1;
        [Range(0, 6)] public int SmallPlanes = 2;
        [Range(0, 3)] public int BannerPlanes = 1;
        [Range(0, 3)] public int Blimps = 1;
        [Range(0, 10)] public int Balloons = 3;
        [Tooltip("Стаек птиц (появляются изредка)")]
        [Range(0, 4)] public int BirdFlocks = 1;
        [Tooltip("Пауза между пролётами птиц, с")]
        public Vector2 BirdInterval = new Vector2(20f, 50f);
        [Tooltip("Надпись на баннере и дирижабле (латиницей)")]
        public string BannerText = "EAT AT CAT COOKS!";
        public bool PreviewInEditor = false;

        [Header("Материалы (ставит «Собрать»)")]
        public Material Toon, Banner, Trail;

        public List<Flyer> Flyers = new List<Flyer>();
        private float _t;

        public float Meter()
        {
            if (UnitsPerMeter > 0f) return UnitsPerMeter;
            CityProps p = City != null ? City.GetComponent<CityProps>() : FindFirstObjectByType<CityProps>();
            return p != null ? p.Meter() : 3.5f;
        }

        private static readonly Color[] Bright =
        {
            new Color(0.93f, 0.30f, 0.25f), new Color(1f, 0.78f, 0.20f), new Color(0.25f, 0.60f, 0.95f), new Color(0.35f, 0.80f, 0.45f),
            new Color(0.95f, 0.55f, 0.75f), new Color(1f, 0.55f, 0.15f), new Color(0.60f, 0.40f, 0.85f), new Color(0.98f, 0.95f, 0.88f)
        };

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject go = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            Flyers.Clear();
        }

        /// <summary>Собрать: поставить объект над центром уровня, создать летающих.</summary>
        public void Build()
        {
            Clear();
            if (City == null) City = FindFirstObjectByType<CityBlockGenerator>();
            float m = Meter();
            float S = 250f;
            Vector3 c = transform.position;
            if (City != null)
            {
                S = Mathf.Min(City.ModuleSize().x, City.ModuleSize().y);
                Vector3 lc;
                if (City.LevelCenter(out lc)) c = lc;
                transform.rotation = City.transform.rotation;
            }
            transform.position = c + transform.up * ViewHeight * m;
            System.Random r = new System.Random(Seed);
            Mesh birdBody = BirdBody(m), birdWing = BirdWing(m);
            for (int i = 0; i < Airliners; i++) Add(FlyerKind.Airliner, r, m, S, null, null);
            for (int i = 0; i < SmallPlanes; i++) Add(FlyerKind.SmallPlane, r, m, S, null, null);
            for (int i = 0; i < BannerPlanes; i++) Add(FlyerKind.BannerPlane, r, m, S, null, null);
            for (int i = 0; i < Blimps; i++) Add(FlyerKind.Blimp, r, m, S, null, null);
            for (int i = 0; i < Balloons; i++) Add(FlyerKind.Balloon, r, m, S, null, null);
            for (int i = 0; i < BirdFlocks; i++) Add(FlyerKind.Birds, r, m, S, birdBody, birdWing);
            _t = 0f;
            Step(0f);
        }

        private static float Rn(System.Random r, float a, float b) { return a + (b - a) * (float)r.NextDouble(); }

        private void Add(FlyerKind k, System.Random r, float m, float S, Mesh birdBody, Mesh birdWing)
        {
            Flyer f = new Flyer { Kind = k, Phase = Rn(r, 0f, 6.28f), Dir = r.NextDouble() < 0.5 ? 1f : -1f, S = S };
            Color a = Bright[r.Next(Bright.Length)], b = Bright[r.Next(Bright.Length)];
            switch (k)
            {
                case FlyerKind.Airliner:
                    f.T = Airliner(m, a).transform; f.H = Rn(r, 140f, 180f) * m; f.Speed = 70f * m; f.Timer = Rn(r, 0f, 8f);
                    NewLine(f, r, 3f * S, 5f * S, 14f * S); f.T.gameObject.SetActive(false); break;
                case FlyerKind.SmallPlane:
                    f.T = SmallPlane(m, a, b, out f.Prop, true).transform; f.H = Rn(r, 35f, 70f) * m; f.R = Rn(r, 1.3f, 2.2f) * S; f.Rz = f.R * Rn(r, 0.7f, 1.1f);
                    f.Speed = 28f * m; f.Bob = 3f * m; break;
                case FlyerKind.BannerPlane:
                    f.T = SmallPlane(m, a, b, out f.Prop, false).transform; BannerTow(f.T, m);
                    f.H = Rn(r, 25f, 45f) * m; f.R = Rn(r, 1.6f, 2.4f) * S; f.Rz = f.R * Rn(r, 0.7f, 1f); f.Speed = 20f * m; f.Bob = 2f * m; break;
                case FlyerKind.Blimp:
                    f.T = Blimp(m, a).transform; f.H = Rn(r, 70f, 100f) * m; f.R = Rn(r, 2.2f, 3f) * S; f.Rz = f.R * Rn(r, 0.8f, 1f); f.Speed = 7f * m; f.Bob = 2f * m; break;
                case FlyerKind.Balloon:
                    f.T = Balloon(m, r).transform; f.H = Rn(r, 40f, 120f) * m; f.R = 3f * S;
                    float ang = Rn(r, 0f, 6.28f), dist = Rn(r, 1.5f, 2.8f) * S;
                    f.A = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                    float wa = Rn(r, 0f, 6.28f), ws = Rn(r, 2f, 4f) * m;
                    f.B = new Vector3(Mathf.Cos(wa) * ws, 0f, Mathf.Sin(wa) * ws);
                    f.Bob = 3f * m; break;
                default:
                    f.T = Birds(m, birdBody, birdWing, r, f.Wings).transform; f.H = Rn(r, 5f, 30f) * m; f.Speed = 13f * m;
                    f.Timer = Rn(r, BirdInterval.x * 0.3f, BirdInterval.y * 0.5f);
                    NewLine(f, r, 0.4f * S, 1.2f * S, 8f * S);
                    f.T.gameObject.SetActive(false); break;
            }
            f.T.name = k + "_" + Flyers.Count;
            Flyers.Add(f);
        }

        /// <summary>Новая прямая через окрестность центра: d0..d1 — промах мимо центра, len — длина пролёта.</summary>
        private static void NewLine(Flyer f, System.Random r, float d0, float d1, float len)
        {
            float th = Rn(r, 0f, 6.28f);
            Vector3 dir = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
            Vector3 side = new Vector3(-dir.z, 0f, dir.x) * Rn(r, d0, d1) * (r.NextDouble() < 0.5 ? 1f : -1f);
            f.A = side - dir * len * 0.5f;
            f.B = side + dir * len * 0.5f;
            f.Phase = 0f;
        }

        // ───────────────────────── движение ─────────────────────────

        private void Update()
        {
            if (!Application.isPlaying && !PreviewInEditor) return;
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            _t += dt;
            Step(dt);
        }

        private void Step(float dt)
        {
            System.Random r = null;
            for (int i = 0; i < Flyers.Count; i++)
            {
                Flyer f = Flyers[i];
                if (f == null || f.T == null) continue;
                if (f.Prop != null) f.Prop.localRotation = Quaternion.Euler(0f, 0f, _t * 1440f);
                switch (f.Kind)
                {
                    case FlyerKind.SmallPlane:
                    case FlyerKind.BannerPlane:
                    case FlyerKind.Blimp:
                    {
                        float w = f.Speed / Mathf.Max(f.R, 1f);
                        float a = f.Phase + f.Dir * w * _t;
                        Vector3 p = new Vector3(Mathf.Cos(a) * f.R, f.H + Mathf.Sin(_t * 0.4f + f.Phase) * f.Bob, Mathf.Sin(a) * f.Rz);
                        Vector3 d = new Vector3(-Mathf.Sin(a) * f.R, 0f, Mathf.Cos(a) * f.Rz) * f.Dir;
                        float bank = f.Kind == FlyerKind.Blimp ? 0f : -f.Dir * 14f;
                        f.T.localPosition = p;
                        f.T.localRotation = Quaternion.LookRotation(d.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
                        break;
                    }
                    case FlyerKind.Balloon:
                    {
                        f.A += f.B * dt;
                        Vector2 xz = new Vector2(f.A.x, f.A.z);
                        if (xz.magnitude > f.R) { f.A = new Vector3(-f.A.x * 0.9f, 0f, -f.A.z * 0.9f); }
                        f.T.localPosition = f.A + Vector3.up * (f.H + Mathf.Sin(_t * 0.25f + f.Phase) * f.Bob);
                        f.T.localRotation = Quaternion.Euler(0f, f.Phase * 57.3f + _t * 3f, 0f);
                        break;
                    }
                    default:                                 // лайнер и птицы — по прямой, потом пауза и новая прямая
                    {
                        if (f.Timer > 0f)
                        {
                            f.Timer -= dt;
                            if (f.Timer <= 0f) f.T.gameObject.SetActive(true);
                            if (f.Timer > 0f) break;
                        }
                        float len = (f.B - f.A).magnitude;
                        f.Phase += f.Speed * dt / Mathf.Max(len, 1f);
                        if (f.Phase >= 1f)
                        {
                            if (r == null) r = new System.Random(Seed + Mathf.FloorToInt(_t * 10f) + i);
                            if (f.Kind == FlyerKind.Birds)
                            {
                                NewLine(f, r, 0.4f * f.S, 1.2f * f.S, 8f * f.S);
                                f.Timer = Rn(r, BirdInterval.x, BirdInterval.y);
                                f.T.gameObject.SetActive(false);
                            }
                            else { NewLine(f, r, 3f * f.S, 5f * f.S, 14f * f.S); f.Timer = Rn(r, 4f, 15f); f.T.gameObject.SetActive(false); }
                            break;
                        }
                        Vector3 dir = (f.B - f.A).normalized;
                        f.T.localPosition = Vector3.Lerp(f.A, f.B, f.Phase) + Vector3.up * f.H;
                        f.T.localRotation = Quaternion.LookRotation(dir, Vector3.up);
                        if (f.Kind == FlyerKind.Birds)
                            for (int k = 0; k < f.Wings.Count; k++)
                            {
                                if (f.Wings[k] == null) continue;
                                float side = (k % 2 == 0) ? 1f : -1f;
                                f.Wings[k].localRotation = Quaternion.Euler(0f, 0f, side * Mathf.Sin(_t * 9f + k * 0.7f) * 35f);
                            }
                        break;
                    }
                }
            }
        }

        // ───────────────────────── модели ─────────────────────────

        private GameObject Root(string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go;
        }

        private static float SinT(float t) { return Mathf.Max(Mathf.Sin(t * Mathf.PI), 0.01f); }

        private GameObject Airliner(float m, Color accent)
        {
            GameObject go = Root("Airliner");
            CityToonMesh t = new CityToonMesh();
            Color white = new Color(0.97f, 0.96f, 0.93f), belly = new Color(0.78f, 0.80f, 0.84f);
            t.Ellipsoid(Vector3.zero, 2f * m, 2.1f * m, 18f * m, 14, 12,
                        (lt, u) => { float y = Mathf.Sin(u * 6.283f); return y > -0.05f && y < 0.25f ? accent : (y < -0.4f ? belly : white); },
                        lt => Mathf.Min(1f, 2.4f * SinT(lt)) / SinT(lt));
            for (int s = -1; s <= 1; s += 2)
            {
                t.M = Matrix4x4.TRS(new Vector3(s * 9f * m, -0.8f * m, -1f * m), Quaternion.Euler(0f, s * -22f, s * -4f), Vector3.one);
                t.Box(Vector3.zero, new Vector3(16f * m, 0.4f * m, 4.2f * m), belly);
                t.M = Matrix4x4.identity;
                t.Ellipsoid(new Vector3(s * 6.5f * m, -2f * m, 1f * m), 0.9f * m, 0.9f * m, 2.4f * m, 6, 8, (lt, u) => belly, null);
                t.M = Matrix4x4.TRS(new Vector3(s * 3.5f * m, 1f * m, -16f * m), Quaternion.Euler(0f, s * -20f, 0f), Vector3.one);
                t.Box(Vector3.zero, new Vector3(6f * m, 0.3f * m, 2.4f * m), white);
                t.M = Matrix4x4.identity;
            }
            t.M = Matrix4x4.TRS(new Vector3(0f, 4f * m, -15.5f * m), Quaternion.Euler(-25f, 0f, 0f), Vector3.one);
            t.Box(Vector3.zero, new Vector3(0.4f * m, 6f * m, 3.6f * m), accent);
            t.M = Matrix4x4.identity;
            CityToonMesh.Make("Body", go.transform, t.Build("Sky_Airliner"), Toon, false);
            return go;
        }

        private GameObject SmallPlane(float m, Color body, Color wing, out Transform prop, bool trail)
        {
            GameObject go = Root("SmallPlane");
            CityToonMesh t = new CityToonMesh();
            t.Ellipsoid(Vector3.zero, 0.7f * m, 0.75f * m, 3.6f * m, 10, 10,
                        (lt, u) => lt < 0.18f && Mathf.Sin(u * 6.283f) > 0.2f ? new Color(0.25f, 0.45f, 0.6f) : body,
                        lt => Mathf.Min(1f, 1.8f * SinT(lt)) / SinT(lt));
            t.Box(new Vector3(0f, 0.75f * m, 0.6f * m), new Vector3(11f * m, 0.18f * m, 1.6f * m), wing);
            t.Box(new Vector3(0f, 0.2f * m, -3.2f * m), new Vector3(3.6f * m, 0.14f * m, 1f * m), wing);
            t.Box(new Vector3(0f, 0.9f * m, -3.2f * m), new Vector3(0.14f * m, 1.4f * m, 1.1f * m), wing);
            t.Box(new Vector3(0.5f * m, -0.95f * m, 0.8f * m), new Vector3(0.2f * m, 0.4f * m, 0.4f * m), new Color(0.15f, 0.15f, 0.18f));
            t.Box(new Vector3(-0.5f * m, -0.95f * m, 0.8f * m), new Vector3(0.2f * m, 0.4f * m, 0.4f * m), new Color(0.15f, 0.15f, 0.18f));
            CityToonMesh.Make("Body", go.transform, t.Build("Sky_SmallPlane"), Toon, false);
            CityToonMesh p = new CityToonMesh();
            p.Box(Vector3.zero, new Vector3(2.4f * m, 0.22f * m, 0.08f * m), new Color(0.2f, 0.2f, 0.22f));
            p.Box(Vector3.zero, new Vector3(0.3f * m, 0.3f * m, 0.3f * m), wing);
            GameObject pg = CityToonMesh.Make("Prop", go.transform, p.Build("Sky_Prop"), Toon, false);
            pg.transform.localPosition = new Vector3(0f, 0f, 3.65f * m);
            prop = pg.transform;
            if (trail && Trail != null)
            {
                GameObject tg = new GameObject("Trail");
                tg.transform.SetParent(go.transform, false);
                tg.transform.localPosition = new Vector3(0f, 0.2f * m, -3.6f * m);
                TrailRenderer tr = tg.AddComponent<TrailRenderer>();
                tr.sharedMaterial = Trail;
                tr.time = 4f;
                tr.minVertexDistance = 2f * m;
                tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1.4f * m), new Keyframe(1f, 0f));
                Gradient g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;
            }
            return go;
        }

        /// <summary>Трос и баннер за самолётом (две стороны — надпись читается с обеих).</summary>
        private void BannerTow(Transform plane, float m)
        {
            float z0 = -9f * m, z1 = -25f * m, y0 = -2.6f * m, y1 = 0.9f * m;
            CityToonMesh rope = new CityToonMesh();
            rope.Beam(new Vector3(0f, 0f, -3.6f * m), new Vector3(0f, (y0 + y1) * 0.5f, z0), 0.06f * m, new Color(0.3f, 0.3f, 0.3f));
            rope.Beam(new Vector3(0f, y0, z0), new Vector3(0f, y1, z0), 0.08f * m, new Color(0.3f, 0.3f, 0.3f));
            CityToonMesh.Make("Rope", plane, rope.Build("Sky_Rope"), Toon, false);
            CityToonMesh b = new CityToonMesh();
            float e = 0.02f * m;
            // +X: u растёт к носу (+Z); −X: u растёт к хвосту — с обеих сторон читается слева направо
            b.Quad(new Vector3(e, y0, z1), new Vector3(e, y1, z1), new Vector3(e, y1, z0), new Vector3(e, y0, z0), Color.white);
            b.Quad(new Vector3(-e, y0, z0), new Vector3(-e, y1, z0), new Vector3(-e, y1, z1), new Vector3(-e, y0, z1), Color.white);
            CityToonMesh.Make("Banner", plane, b.Build("Sky_Banner"), Banner != null ? Banner : Toon, false);
        }

        private GameObject Blimp(float m, Color stripe)
        {
            GameObject go = Root("Blimp");
            float rx = 5f * m, ry = 5f * m, rz = 16f * m;
            CityToonMesh t = new CityToonMesh();
            Color silver = new Color(0.86f, 0.87f, 0.90f);
            t.Ellipsoid(Vector3.zero, rx, ry, rz, 16, 16, (lt, u) => Mathf.Abs(Mathf.Sin(u * 6.283f) + 0.75f) < 0.12f ? stripe : silver, null);
            for (int k = 0; k < 4; k++)
            {
                t.M = Matrix4x4.TRS(new Vector3(0f, 0f, -13.5f * m), Quaternion.Euler(0f, 0f, k * 90f), Vector3.one);
                t.Box(new Vector3(0f, 3.6f * m, 0f), new Vector3(0.3f * m, 3.4f * m, 3.6f * m), stripe);
                t.M = Matrix4x4.identity;
            }
            t.Box(new Vector3(0f, -5.3f * m, 1.5f * m), new Vector3(2f * m, 1.4f * m, 5f * m), new Color(0.35f, 0.32f, 0.30f));
            CityToonMesh.Make("Body", go.transform, t.Build("Sky_Blimp"), Toon, false);
            // надпись по бокам — полосой по поверхности
            CityToonMesh b = new CityToonMesh();
            int seg = 10;
            float zs0 = -8f * m, zs1 = 8f * m, ya = -2.1f * m, yb = 2.1f * m;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < seg; i++)
                {
                    float za = Mathf.Lerp(zs0, zs1, (float)i / seg), zb = Mathf.Lerp(zs0, zs1, (float)(i + 1) / seg);
                    float ua = (float)i / seg, ub = (float)(i + 1) / seg;
                    Vector3 p00 = Surf(rx, ry, rz, za, ya, s, m), p01 = Surf(rx, ry, rz, za, yb, s, m), p11 = Surf(rx, ry, rz, zb, yb, s, m), p10 = Surf(rx, ry, rz, zb, ya, s, m);
                    if (s > 0) b.Quad(p00, p01, p11, p10, Color.white, new Vector2(ua, 0), new Vector2(ua, 1), new Vector2(ub, 1), new Vector2(ub, 0));
                    else b.Quad(p10, p11, p01, p00, Color.white, new Vector2(1 - ub, 0), new Vector2(1 - ub, 1), new Vector2(1 - ua, 1), new Vector2(1 - ua, 0));
                }
            CityToonMesh.Make("Ad", go.transform, b.Build("Sky_BlimpAd"), Banner != null ? Banner : Toon, false);
            return go;
        }

        private static Vector3 Surf(float rx, float ry, float rz, float z, float y, int side, float m)
        {
            float k = 1f - (z * z) / (rz * rz) - (y * y) / (ry * ry);
            float x = rx * Mathf.Sqrt(Mathf.Max(0f, k)) + 0.08f * m;
            return new Vector3(x * side, y, z);
        }

        private GameObject Balloon(float m, System.Random r)
        {
            GameObject go = Root("Balloon");
            Color c1 = Bright[r.Next(Bright.Length)], c2 = Bright[r.Next(Bright.Length)], c3 = new Color(0.98f, 0.95f, 0.88f);
            CityToonMesh t = new CityToonMesh();
            t.M = Matrix4x4.TRS(new Vector3(0f, 9f * m, 0f), Quaternion.Euler(-90f, 0f, 0f), Vector3.one);     // ось Z → вверх
            t.Ellipsoid(Vector3.zero, 7f * m, 7f * m, 8f * m, 12, 12,
                        (lt, u) => { int g = (int)(u * 12f); return g % 3 == 0 ? c1 : (g % 3 == 1 ? c2 : c3); },
                        lt => lt < 0.55f ? 1f : Mathf.Lerp(1f, 0.55f, (lt - 0.55f) / 0.45f));
            t.M = Matrix4x4.identity;
            Color basket = new Color(0.55f, 0.36f, 0.20f);
            t.Box(new Vector3(0f, -1.2f * m, 0f), new Vector3(1.6f * m, 1.2f * m, 1.6f * m), basket);
            for (int k = 0; k < 4; k++)
            {
                float sx = (k & 1) == 0 ? -0.75f : 0.75f, sz = (k & 2) == 0 ? -0.75f : 0.75f;
                t.Beam(new Vector3(sx * m, -0.6f * m, sz * m), new Vector3(sx * 2.4f * m, 2.2f * m, sz * 2.4f * m), 0.06f * m, new Color(0.3f, 0.25f, 0.2f));
            }
            CityToonMesh.Make("Body", go.transform, t.Build("Sky_Balloon"), Toon, false);
            return go;
        }

        private static Mesh BirdBody(float m)
        {
            CityToonMesh t = new CityToonMesh();
            t.Ellipsoid(Vector3.zero, 0.14f * m, 0.12f * m, 0.38f * m, 5, 6, (lt, u) => new Color(0.18f, 0.17f, 0.20f), null);
            return t.Build("Sky_BirdBody");
        }

        private static Mesh BirdWing(float m)
        {
            CityToonMesh t = new CityToonMesh();
            t.Box(new Vector3(0.42f * m, 0f, 0f), new Vector3(0.84f * m, 0.03f * m, 0.28f * m), new Color(0.22f, 0.21f, 0.24f));
            return t.Build("Sky_BirdWing");
        }

        /// <summary>Стайка клином: вожак впереди, остальные уступом назад-в стороны; крылья машут.</summary>
        private GameObject Birds(float m, Mesh body, Mesh wing, System.Random r, List<Transform> wings)
        {
            GameObject go = Root("Birds");
            int n = r.Next(5, 10);
            for (int i = 0; i < n; i++)
            {
                int rank = (i + 1) / 2;
                float side = i == 0 ? 0f : (i % 2 == 0 ? 1f : -1f);
                GameObject bird = CityToonMesh.Make("Bird_" + i, go.transform, body, Toon, false);
                bird.transform.localPosition = new Vector3(side * rank * 1.6f * m, Rn(r, -0.3f, 0.3f) * m, -rank * 1.8f * m);
                for (int s = -1; s <= 1; s += 2)
                {
                    GameObject w = CityToonMesh.Make(s > 0 ? "WingR" : "WingL", bird.transform, wing, Toon, false);
                    w.transform.localPosition = new Vector3(s * 0.1f * m, 0.03f * m, 0f);
                    if (s < 0) w.transform.localScale = new Vector3(-1f, 1f, 1f);
                    wings.Add(w.transform);
                }
            }
            return go;
        }
    }
}
