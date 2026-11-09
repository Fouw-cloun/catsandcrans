using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Level
{
    /// <summary>
    /// ГОЛУБИ — супер дешёвые: стайки сидят на краях крыш вокруг уровня, топчутся и клюют, время от времени вся стайка
    /// перелетает дугой на другую крышу. Гром (молния CityStorm) — все разом взлетают. Ночью спят (почти не летают).
    /// Один общий меш «сидит» и один «летит» (крылья машут в шейдере Sway Toon), один материал, один скрипт на всех —
    /// птицы — не объекты сцены: все рисуются двумя командами (GPU instancing). Дальние от камеры не рисуются
    /// и не анимируются. Появляются только в игре, в сцене ничего не сохраняется.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CatCooks/Город/Голуби на крышах")]
    public sealed class CityPigeons : MonoBehaviour
    {
        [Tooltip("Материал (шейдер Sway Toon, крылья машут вверх-вниз). Ставит меню")]
        public Material PigeonMaterial;
        [Tooltip("Сколько стаек")]
        [Range(1, 16)] public int Flocks = 5;
        [Tooltip("Голубей в стайке (от и до)")]
        public Vector2Int PerFlock = new Vector2Int(3, 7);
        [Tooltip("Крыши в этом радиусе вокруг ЭТОГО объекта, м (поставь объект в центр уровня)")]
        public float Radius = 140f;
        [Tooltip("Длина голубя, м (мультяшно крупнее настоящего — иначе их не видно)")]
        public float Size = 0.55f;
        [Tooltip("Сколько сидят на крыше, с (от и до)")]
        public Vector2 SitTime = new Vector2(10f, 30f);
        [Tooltip("Скорость полёта, м/с")]
        public float FlySpeed = 9f;
        [Tooltip("Взлетают от грома")]
        public bool StartleOnThunder = true;
        [Tooltip("Ночью спят (летают в 4 раза реже)")]
        public bool SleepAtNight = true;
        [Tooltip("Дальше этого от камеры не рисуются и не топчутся, м")]
        public float DrawDistance = 160f;

        private sealed class Bird
        {
            public Vector3 Pos;
            public Quaternion Rot = Quaternion.identity;
            public Vector3 Seat;         // место на крыше (смещение от точки стайки)
            public Vector3 From, To, Ctrl;
            public float Delay, Yaw, Peck, NextIdle;
            public bool Flying;
        }

        private sealed class Flock
        {
            public List<Bird> Birds = new List<Bird>();
            public int Perch = -1;
            public float Timer, FlyT, FlyDur;
            public bool Flying;
        }

        private readonly List<Vector3> _perch = new List<Vector3>();
        private readonly List<Vector3> _perchOut = new List<Vector3>();       // наружу от края крыши (куда смотрят)
        private readonly List<Flock> _flocks = new List<Flock>();
        private Mesh _sit, _fly;
        private float _m = 3.5f;
        private System.Random _r;
        private bool _startle;

        private float Rn(float a, float b) { return a + (b - a) * (float)_r.NextDouble(); }

        private void OnEnable() { CityStorm.OnStrike += OnStrike; }
        private void OnDisable() { CityStorm.OnStrike -= OnStrike; }
        private void OnStrike(float dist) { if (StartleOnThunder) _startle = true; }

        /// <summary>Спугнуть всех (например, что-то упало со стройки).</summary>
        public void Startle() { _startle = true; }

        private void Start()
        {
            if (!Application.isPlaying) return;
            _r = new System.Random(GetInstanceID());
            _m = CityPropGlow.CityMeter();
            FindPerches();
            if (_perch.Count < 2) { Debug.Log("[Город/голуби] вокруг «" + name + "» (радиус " + Radius.ToString("0") + " м) крыш не нашлось — передвинь объект к домам или увеличь Radius", this); enabled = false; return; }
            Debug.Log("[Город/голуби] мест на крышах вокруг «" + name + "»: " + _perch.Count + ", стаек " + Flocks, this);
            _sit = BuildMesh(false);
            _fly = BuildMesh(true);
            if (PigeonMaterial == null)
            {
                Shader sh = Shader.Find("CatCooks/City/Sway Toon");
                if (sh != null) { PigeonMaterial = new Material(sh) { enableInstancing = true }; PigeonMaterial.SetVector("_SwayAxis", new Vector4(0, 1, 0, 0)); PigeonMaterial.SetFloat("_SwaySpeed", 16f); PigeonMaterial.SetFloat("_SwayAmp", 0.32f); PigeonMaterial.SetFloat("_SwayRipple", 0f); PigeonMaterial.SetFloat("_WindK", 0f); PigeonMaterial.enableInstancing = true; }
            }
            if (PigeonMaterial != null && !PigeonMaterial.enableInstancing) PigeonMaterial.enableInstancing = true;   // рисуем пачкой
            for (int f = 0; f < Flocks; f++)
            {
                Flock fl = new Flock();
                fl.Perch = _r.Next(_perch.Count);
                fl.Timer = Rn(2f, SitTime.y);
                int n = _r.Next(Mathf.Min(PerFlock.x, PerFlock.y), Mathf.Max(PerFlock.x, PerFlock.y) + 1);
                for (int i = 0; i < n; i++)
                {
                    Bird b = new Bird();
                    b.Seat = SeatOffset(fl.Perch, i, n);
                    b.Yaw = Rn(-60f, 60f);
                    b.NextIdle = Rn(0.5f, 4f);
                    b.Pos = _perch[fl.Perch] + b.Seat;
                    fl.Birds.Add(b);
                }
                _flocks.Add(fl);
            }
        }

        /// <summary>Места на крышах: края крыш домов («B_…» в генераторе кварталов) в радиусе от камеры.</summary>
        private void FindPerches()
        {
            Vector3 c0 = transform.position;                                   // живут вокруг этого объекта (центр уровня)
            float R = Radius * _m;
            Dictionary<Transform, Bounds> houses = new Dictionary<Transform, Bounds>();
            Dictionary<Transform, float> roof = new Dictionary<Transform, float>();
            Dictionary<Transform, float> roofArea = new Dictionary<Transform, float>();
            foreach (CityBlockGenerator g in FindObjectsByType<CityBlockGenerator>(FindObjectsSortMode.None))
            {
                Transform cont = g.Container();
                if (cont == null) continue;
                foreach (MeshRenderer r in cont.GetComponentsInChildren<MeshRenderer>(false))
                {
                    Transform h = r.transform;
                    while (h != null && h != cont && !h.name.StartsWith("B_")) h = h.parent;
                    if (h == null || h == cont) continue;
                    Bounds b = r.bounds;
                    Vector3 d = b.center - c0; d.y = 0f;
                    if (d.magnitude > R) continue;
                    Bounds hb;
                    if (houses.TryGetValue(h, out hb)) { hb.Encapsulate(b); houses[h] = hb; } else houses[h] = b;
                    float area = b.size.x * b.size.z, a0;
                    if (!roofArea.TryGetValue(h, out a0) || area > a0) { roofArea[h] = area; roof[h] = b.max.y; }   // крыша — верх самого широкого куска
                }
            }
            foreach (KeyValuePair<Transform, Bounds> kv in houses)
            {
                Bounds b = kv.Value;
                float y = roof[kv.Key];
                if (y - b.min.y < 4f * _m) continue;                              // не на будки
                int n = b.size.x * b.size.z > 600f * _m * _m ? 2 : 1;
                for (int k = 0; k < n; k++)
                {
                    int side = _r.Next(4);
                    float t = Rn(0.15f, 0.85f);
                    Vector3 p, o;
                    float inset = 0.6f * _m;
                    switch (side)
                    {
                        case 0: p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, t), y, b.min.z + inset); o = Vector3.back; break;
                        case 1: p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, t), y, b.max.z - inset); o = Vector3.forward; break;
                        case 2: p = new Vector3(b.min.x + inset, y, Mathf.Lerp(b.min.z, b.max.z, t)); o = Vector3.left; break;
                        default: p = new Vector3(b.max.x - inset, y, Mathf.Lerp(b.min.z, b.max.z, t)); o = Vector3.right; break;
                    }
                    _perch.Add(p);
                    _perchOut.Add(o);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            float M = 3.5f;
            CityBlockGenerator g = FindFirstObjectByType<CityBlockGenerator>();
            CityProps p = g != null ? g.GetComponent<CityProps>() : null;
            if (p != null) M = p.Meter();
            Gizmos.color = new Color(0.6f, 0.75f, 1f, 0.9f);
            Vector3 c = transform.position;
            float R = Radius * M;
            for (int i = 0; i < 48; i++)
            {
                float a0 = i * Mathf.PI * 2f / 48f, a1 = (i + 1) * Mathf.PI * 2f / 48f;
                Gizmos.DrawLine(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * R, c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * R);
            }
        }

        /// <summary>Место птицы вдоль края крыши (стайка садится в рядок).</summary>
        private Vector3 SeatOffset(int perch, int i, int n)
        {
            Vector3 o = _perchOut[perch];
            Vector3 along = new Vector3(-o.z, 0f, o.x);
            float step = Size * _m * 1.1f;
            return along * ((i - (n - 1) * 0.5f) * step + Rn(-0.2f, 0.2f) * step) + o * Rn(-0.3f, 0.3f) * step;
        }

        private void Update()
        {
            if (_r == null) return;
            float dt = Time.deltaTime;
            float night = SleepAtNight ? Mathf.Clamp01(Shader.GetGlobalFloat("_CC_Night")) : 0f;
            bool startle = _startle; _startle = false;
            foreach (Flock fl in _flocks)
            {
                if (!fl.Flying)
                {
                    fl.Timer -= dt * (night > 0.6f ? 0.25f : 1f);
                    if (fl.Timer <= 0f || startle) TakeOff(fl, startle);
                }
                else
                {
                    fl.FlyT += dt;
                    bool all = true;
                    foreach (Bird b in fl.Birds) if (b.Flying) { FlyBird(b, fl.FlyT, fl.FlyDur); all = false; }
                    if (all) { fl.Flying = false; fl.Timer = Rn(SitTime.x, SitTime.y); }
                }
                if (Near(_perch[fl.Perch]))                                       // дальние стайки не топчутся (их не видно)
                    foreach (Bird b in fl.Birds) if (!b.Flying) Idle(b, fl, dt);
            }
        }

        private Vector3 _cam;
        private float _drawSq;
        private readonly List<Matrix4x4> _mSit = new List<Matrix4x4>(), _mFly = new List<Matrix4x4>();

        private bool Near(Vector3 p) { Vector3 d = p - _cam; return d.sqrMagnitude < _drawSq; }

        /// <summary>Нарисовать всех: сидящие — одной командой, летящие — другой (GPU instancing).</summary>
        private void LateUpdate()
        {
            if (_r == null || PigeonMaterial == null) return;
            Camera cam = Camera.main;
            _cam = cam != null ? cam.transform.position : transform.position;
            float dd = DrawDistance * _m; _drawSq = dd * dd;
            _mSit.Clear(); _mFly.Clear();
            Vector3 sc = Vector3.one * Size * _m;
            foreach (Flock fl in _flocks)
                foreach (Bird b in fl.Birds)
                {
                    if (!Near(b.Pos)) continue;
                    Matrix4x4 m = Matrix4x4.TRS(b.Pos, b.Rot, sc);
                    if (b.Flying) _mFly.Add(m); else _mSit.Add(m);
                }
            RenderParams rp = new RenderParams(PigeonMaterial);
            rp.shadowCastingMode = ShadowCastingMode.Off;
            rp.receiveShadows = false;
            rp.lightProbeUsage = LightProbeUsage.Off;
            rp.layer = gameObject.layer;
            rp.worldBounds = new Bounds(_cam, Vector3.one * dd * 2.2f);
            if (_mSit.Count > 0) Graphics.RenderMeshInstanced(rp, _sit, 0, _mSit);
            if (_mFly.Count > 0) Graphics.RenderMeshInstanced(rp, _fly, 0, _mFly);
        }

        private void TakeOff(Flock fl, bool startle)
        {
            int to = fl.Perch;
            Vector3 from = _perch[fl.Perch];
            for (int tries = 0; tries < 8; tries++)
            {
                int k = _r.Next(_perch.Count);
                float d = Vector3.Distance(_perch[k], from);
                if (k != fl.Perch && d > 8f * _m && d < Radius * _m * 0.8f) { to = k; break; }
            }
            if (to == fl.Perch) { fl.Timer = Rn(SitTime.x, SitTime.y); return; }
            float dist = Vector3.Distance(_perch[to], from);
            fl.FlyDur = dist / Mathf.Max(1f, FlySpeed * _m);
            fl.FlyT = 0f;
            fl.Flying = true;
            int n = fl.Birds.Count;
            for (int i = 0; i < n; i++)
            {
                Bird b = fl.Birds[i];
                b.From = b.Pos;
                b.Seat = SeatOffsetTo(to, i, n);
                b.To = _perch[to] + b.Seat;
                Vector3 mid = (b.From + b.To) * 0.5f;
                b.Ctrl = mid + Vector3.up * (dist * 0.3f + 4f * _m) + new Vector3(Rn(-1f, 1f), 0f, Rn(-1f, 1f)) * 2f * _m;
                b.Delay = startle ? Rn(0f, 0.25f) : Rn(0f, 0.9f);
                b.Flying = true;
            }
            fl.Perch = to;
        }

        private Vector3 SeatOffsetTo(int perch, int i, int n) { return SeatOffset(perch, i, n); }

        private static Vector3 Bez(Vector3 a, Vector3 c, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        private void FlyBird(Bird b, float time, float dur)
        {
            float t = Mathf.Clamp01((time - b.Delay) / Mathf.Max(0.3f, dur));
            if (time < b.Delay) return;
            float e = t * t * (3f - 2f * t);
            Vector3 p = Bez(b.From, b.Ctrl, b.To, e);
            Vector3 q = Bez(b.From, b.Ctrl, b.To, Mathf.Min(1f, e + 0.02f));
            Vector3 dir = q - p;
            b.Pos = p;
            if (dir.sqrMagnitude > 1e-6f)
            {
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                float pitch = -Mathf.Atan2(dir.y, Mathf.Max(flat.magnitude, 1e-4f)) * Mathf.Rad2Deg * 0.6f;
                b.Rot = Quaternion.LookRotation(flat.sqrMagnitude > 1e-6f ? flat : Vector3.forward) * Quaternion.Euler(pitch, 0f, 0f);
            }
            if (t >= 1f)
            {
                b.Flying = false;
                b.Yaw = Rn(-60f, 60f);
                b.NextIdle = Rn(0.5f, 3f);
            }
        }

        private void Idle(Bird b, Flock fl, float dt)
        {
            b.NextIdle -= dt;
            if (b.NextIdle <= 0f)
            {
                if (_r.NextDouble() < 0.5) b.Yaw += Rn(-70f, 70f); else b.Peck = 0.35f;
                b.Yaw = Mathf.Clamp(b.Yaw, -110f, 110f);
                b.NextIdle = Rn(0.8f, 4f);
            }
            float pk = 0f;
            if (b.Peck > 0f) { b.Peck -= dt; pk = Mathf.Sin(Mathf.Clamp01(b.Peck / 0.35f) * Mathf.PI) * 35f; }
            Vector3 o = _perchOut[fl.Perch];
            Quaternion face = Quaternion.LookRotation(o) * Quaternion.Euler(0f, b.Yaw, 0f);
            b.Rot = Quaternion.Slerp(b.Rot, face * Quaternion.Euler(pk, 0f, 0f), Mathf.Clamp01(dt * 8f));
            b.Pos = _perch[fl.Perch] + b.Seat;
        }

        // ── меш голубя (в «метрах голубя»: длина 1): тело, голова с клювом, хвост; крылья сложены или раскрыты ──
        private static Mesh BuildMesh(bool flying)
        {
            CityToonMesh t = new CityToonMesh();
            Color body = new Color(0.62f, 0.66f, 0.74f), dark = new Color(0.36f, 0.39f, 0.47f), neck = new Color(0.42f, 0.62f, 0.55f),
                  beak = new Color(0.95f, 0.62f, 0.25f), white = new Color(0.95f, 0.95f, 0.95f), eye = new Color(0.1f, 0.08f, 0.08f),
                  feet = new Color(0.9f, 0.4f, 0.4f);
            // тело — сплюснутый эллипсоид вдоль Z
            t.Ellipsoid(new Vector3(0f, 0.32f, 0f), 0.2f, 0.2f, 0.34f, 5, 8, (a, b) => Paint(a < 0.35f ? neck : body), null);
            t.Ellipsoid(new Vector3(0f, 0.55f, 0.27f), 0.13f, 0.13f, 0.13f, 4, 7, (a, b) => Paint(neck), null);      // голова
            t.Box(new Vector3(0f, 0.53f, 0.43f), new Vector3(0.05f, 0.04f, 0.1f), Paint(beak));                       // клюв
            t.Box(new Vector3(0.1f, 0.59f, 0.33f), new Vector3(0.025f, 0.04f, 0.04f), Paint(eye));
            t.Box(new Vector3(-0.1f, 0.59f, 0.33f), new Vector3(0.025f, 0.04f, 0.04f), Paint(eye));
            t.Box(new Vector3(0f, 0.3f, -0.42f), new Vector3(0.2f, 0.04f, 0.2f), Paint(dark));                        // хвост
            if (!flying)
            {
                t.Box(new Vector3(0.06f, 0.06f, 0.02f), new Vector3(0.03f, 0.12f, 0.03f), Paint(feet));
                t.Box(new Vector3(-0.06f, 0.06f, 0.02f), new Vector3(0.03f, 0.12f, 0.03f), Paint(feet));
                t.Box(new Vector3(0.19f, 0.35f, -0.05f), new Vector3(0.04f, 0.16f, 0.42f), Paint(dark));              // сложенные крылья
                t.Box(new Vector3(-0.19f, 0.35f, -0.05f), new Vector3(0.04f, 0.16f, 0.42f), Paint(dark));
            }
            else
            {
                // раскрытые крылья: у корня не двигаются (a = 0), к концу машут (a = 1)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 r0 = new Vector3(0.15f * s, 0.4f, 0.12f), r1 = new Vector3(0.15f * s, 0.4f, -0.15f);
                    Vector3 m0 = new Vector3(0.5f * s, 0.42f, 0.1f), m1 = new Vector3(0.5f * s, 0.42f, -0.18f);
                    Vector3 e0 = new Vector3(0.82f * s, 0.44f, 0.0f), e1 = new Vector3(0.78f * s, 0.44f, -0.2f);
                    Color w0 = new Color(body.r, body.g, body.b, 0.05f), w1 = new Color(body.r, body.g, body.b, 0.5f), w2 = new Color(white.r, white.g, white.b, 1f);
                    WingQuad(t, r0, r1, m1, m0, w0, w1, s);
                    WingQuad(t, m0, m1, e1, e0, w1, w2, s);
                }
            }
            Mesh m = t.Build(flying ? "Pigeon_Fly" : "Pigeon_Sit");
            m.RecalculateBounds();
            Bounds bb = m.bounds; bb.Expand(0.6f); m.bounds = bb;                 // крылья машут в шейдере
            return m;
        }

        private static Color Paint(Color c) { return new Color(c.r, c.g, c.b, 0f); }

        /// <summary>Кусок крыла: a — у корня (вес wa), b — у конца (вес wb); две стороны.</summary>
        private static void WingQuad(CityToonMesh t, Vector3 a0, Vector3 a1, Vector3 b1, Vector3 b0, Color wa, Color wb, int s)
        {
            int start = t.V.Count;
            if (s > 0) t.Quad(a0, b0, b1, a1, wa); else t.Quad(a0, a1, b1, b0, wa);
            if (s > 0) t.Quad(a0, a1, b1, b0, wa); else t.Quad(a0, b0, b1, a1, wa);
            // проставить вес движения по вершинам: точки конца — wb
            for (int i = start; i < t.V.Count; i++)
            {
                Vector3 v = t.V[i];
                bool tip = Mathf.Abs(v.x) > Mathf.Abs(a0.x) + 0.01f;
                t.C[i] = tip ? wb : wa;
            }
        }
    }
}
