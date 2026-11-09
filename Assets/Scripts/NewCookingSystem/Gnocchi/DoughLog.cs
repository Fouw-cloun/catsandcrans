using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using CatCooks.Food.Tools;

namespace CatCooks.Food
{
    /// <summary>
    /// КУСОК ТЕСТА после нарезки шпателем: колбаска (длинная) или ньокк (короткий). Мягкая «подушка» — скруглённый брусок.
    /// Размер — свой у каждого (Length × Width × Thickness), меш строится сам; префаб один на все размеры.
    ///   • Шпатель ЛКМ по куску: лезвие поперёк куска — делит по длине (в точке удара), вдоль — по ширине. Любой порядок.
    ///   • Взять в руки можно (F), как предмет.
    ///   • Подсказка, когда смотришь: размер и «в самый раз ✔ / крупно / мелко» — это пойдёт в оценку блюда.
    /// ═══ ЗАГОТОВКА ═══ CatCooks → Ньокки → 6. Заготовка куска теста → сохрани префабом → Spawnable Prefabs →
    ///   перетащи в Dough Sheet → Piece Prefab (или выдели префаб и меню 7).
    /// </summary>
    public sealed class DoughLog : PickupItem, ISpatulaTarget
    {
        [Header("Размер ньокка «в самый раз» (длина и ширина), м — при масштабе мира 4")]
        [SerializeField] private float _pieceMin = 0.10f;
        [SerializeField] private float _pieceMax = 0.18f;
        [Tooltip("Меньше этого кусок не отрезать, м")]
        [SerializeField] private float _minPart = 0.05f;
        [Tooltip("Скругление краёв (доля от меньшего размера)")]
        [Range(0.1f, 0.5f)][SerializeField] private float _round = 0.42f;
        [SerializeField] private bool _logPieces = true;

        private readonly SyncVar<Vector3> _size = new SyncVar<Vector3>(new Vector3(0.6f, 0.12f, 0.15f));   // x — длина, y — толщина, z — ширина
        private readonly SyncVar<Color> _color = new SyncVar<Color>(new Color(0.97f, 0.84f, 0.48f));
        private readonly SyncVar<byte> _quality = new SyncVar<byte>(255);
        private readonly SyncVar<byte> _kind = new SyncVar<byte>(0);

        private MeshFilter _mf;
        private Mesh _mesh;
        private BoxCollider _box;
        private float _lookAt; private bool _looking;
        private Vector3 _built = Vector3.zero;

        public Vector3 Size => _size.Value;
        public float Length => _size.Value.x;
        public float Width => _size.Value.z;
        public float Thickness => _size.Value.y;
        public float Quality01 => _quality.Value / 255f;
        public DoughKind Kind => (DoughKind)_kind.Value;
        public bool IsGnocchi => Mathf.Max(Length, Width) <= _pieceMax * 1.6f;
        public float Volume => Length * Width * Thickness * 0.82f;

        /// <summary>0..1 — насколько ньокк «в самый раз» по размеру (колбаска — 0).</summary>
        public float Size01
        {
            get
            {
                float a = Length, b = Width;
                float ea = a < _pieceMin ? (_pieceMin - a) / _pieceMin : a > _pieceMax ? (a - _pieceMax) / _pieceMax : 0f;
                float eb = b < _pieceMin ? (_pieceMin - b) / _pieceMin : b > _pieceMax ? (b - _pieceMax) / _pieceMax : 0f;
                return Mathf.Clamp01(1f - (ea + eb) * 1.5f);
            }
        }

        protected override void Awake()
        {
            base.Awake();
            _mf = GetComponent<MeshFilter>();
            _box = GetComponent<BoxCollider>();
        }

        private void OnEnable() { if (!DoughStick.Spatulable.Contains(this)) DoughStick.Spatulable.Add(this); }
        private void OnDisable() { DoughStick.Spatulable.Remove(this); }
        private void OnDestroy() { if (_mesh != null) Destroy(_mesh); }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _size.OnChange += (a, b, s) => Rebuild();
            _color.OnChange += (a, b, s) => Rebuild(true);
            Rebuild(true);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // пару мгновений неподвижно: пласт под куском ещё не пересобрал коллайдер — без «выстрела» вверх
            if (_rb != null) _rb.isKinematic = true;
        }

        /// <summary>До Spawn: размер (длина, толщина, ширина), цвет теста, качество, тип.</summary>
        public void ServerPreset(float length, float width, float thickness, Color color, float quality01, DoughKind kind)
        {
            _size.Value = new Vector3(Mathf.Max(0.02f, length), Mathf.Max(0.01f, thickness), Mathf.Max(0.02f, width));
            _color.Value = color;
            _quality.Value = (byte)Mathf.RoundToInt(Mathf.Clamp01(quality01) * 255f);
            _kind.Value = (byte)kind;
            Rebuild(true);
        }

        /// <summary>Сервер: лежащий кусок — неподвижный (kinematic): куски не толкаются и не «взрываются» друг об друга.
        /// Бросили из рук — падает как обычно, улёгся (почти не движется 0.4 с) — снова замирает.</summary>
        private void Update()
        {
            if (!base.IsServerInitialized || _rb == null || _rb.isKinematic) { _restT = 0f; return; }
            if (_box != null && !_box.enabled) { _restT = 0f; return; }                   // в руке
            bool still = _rb.linearVelocity.sqrMagnitude < 0.0064f && _rb.angularVelocity.sqrMagnitude < 0.04f;
            _restT = still ? _restT + Time.deltaTime : 0f;
            if (_restT >= 0.4f) { _rb.isKinematic = true; _restT = 0f; }
        }
        private float _restT;

        // ───────────────────────── шпатель ─────────────────────────

        public float SpatulaDistance(Vector3 point)
        {
            if (_box == null || !_box.enabled) return float.MaxValue;
            return (_box.ClosestPoint(point) - point).magnitude;
        }

        /// <summary>Лезвие поперёк куска — режем длину в точке удара; вдоль — ширину. Остаток — новым куском рядом.</summary>
        public bool ServerSpatulaHit(Vector3 point, Vector3 dir, out string why)
        {
            why = null;
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            Vector3 blade = flat.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, flat.normalized) : transform.forward;
            Vector3 lb = transform.InverseTransformDirection(blade); lb.y = 0f;
            Vector3 lp = transform.InverseTransformPoint(point);
            Vector3 sz = _size.Value;
            bool cutLength = Mathf.Abs(lb.normalized.x) < 0.7071f;          // лезвие вдоль Z куска — поперёк длины
            if (cutLength && sz.x < _minPart * 2f && sz.z >= _minPart * 2f) cutLength = false;  // короткий — режем по ширине
            if (!cutLength && sz.z < _minPart * 2f && sz.x >= _minPart * 2f) cutLength = true;
            float full = cutLength ? sz.x : sz.z;
            if (full < _minPart * 2f) { why = $"кусок уже маленький ({Length * 100f:0}×{Width * 100f:0} см) — меньше не режется"; return false; }
            float at = Mathf.Clamp(cutLength ? lp.x : lp.z, -full * 0.5f + _minPart, full * 0.5f - _minPart);
            float a = at + full * 0.5f, b = full * 0.5f - at;                 // «минус»-часть и «плюс»-часть
            Vector3 axis = cutLength ? Vector3.right : Vector3.forward;
            Vector3 ca = axis * (-full * 0.5f + a * 0.5f), cb = axis * (full * 0.5f - b * 0.5f);

            // эта часть — «минус», вторая — новый кусок
            Vector3 wb = transform.TransformPoint(cb);
            Quaternion rot = transform.rotation;
            Vector3 sa = sz, sb = sz;
            if (cutLength) { sa.x = a; sb.x = b; } else { sa.z = a; sb.z = b; }
            GameObject src = PrefabRef;
            if (src == null) foreach (DoughSheet ds in DoughSheet.All) if (ds != null && ds.PiecePrefab != null) { src = ds.PiecePrefab; break; }
            if (src == null) { why = "не знаю префаб куска (задай Piece Prefab у Dough Sheet)"; return false; }
            float gap = 0.006f;                                                   // зазор, чтобы половинки не упирались друг в друга
            Vector3 wa = transform.TransformPoint(ca - axis * gap);
            wb = transform.TransformPoint(cb + axis * gap);
            if (_rb != null) { _rb.isKinematic = true; _rb.position = wa; }
            transform.position = wa;
            _size.Value = sa;
            Rebuild();

            GameObject go = Instantiate(src, wb, rot);
            DoughLog other = go.GetComponent<DoughLog>();
            if (other == null) { Destroy(go); why = "у заготовки нет DoughLog"; return false; }
            other.PrefabRef = src;
            other.ServerPreset(sb.x, sb.z, sb.y, _color.Value, Quality01, Kind);
            base.Spawn(go);
            if (_logPieces)
                Debug.Log($"[Ньокки] шпатель: «{name}» {(cutLength ? "поперёк" : "вдоль")} → {Describe(sa)} + {Describe(sb)}", this);
            return true;
        }

        /// <summary>Префаб-источник (ставит пласт при спавне): новые куски спавним из него, а не клонируем сцену.</summary>
        [System.NonSerialized] public GameObject PrefabRef;

        private string Describe(Vector3 s) => Mathf.Max(s.x, s.z) <= _pieceMax * 1.6f
            ? $"ньокк {s.x * 100f:0}×{s.z * 100f:0}×{s.y * 100f:0} см"
            : $"колбаска {s.x:0.00} м × {s.z * 100f:0} см";

        // ───────────────────────── вид ─────────────────────────

        /// <summary>Скруглённый брусок (подушечка): кубик с подразбиением, вершины «скруглены» к внутреннему боксу.</summary>
        private void Rebuild(bool force = false)
        {
            Vector3 sz = _size.Value;
            if (!force && (sz - _built).sqrMagnitude < 1e-8f) return;
            _built = sz;
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_box == null) _box = GetComponent<BoxCollider>();
            if (_box != null) { _box.size = sz; _box.center = Vector3.zero; }
            if (_rb != null) _rb.mass = Mathf.Clamp(Volume * 600f, 0.2f, 80f);
            if (_mf == null) return;
            if (_mesh == null) { _mesh = new Mesh { name = "DoughLog" }; _mf.sharedMesh = _mesh; }
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(sz.x, sz.z) / Mathf.Max(0.02f, Mathf.Min(sz.x, sz.z)) * 4f), 4, 24);   // длинной — больше делений
            int nShort = 5;
            float r = Mathf.Min(sz.x, Mathf.Min(sz.y, sz.z)) * _round;
            Vector3 half = sz * 0.5f, inner = new Vector3(Mathf.Max(0f, half.x - r), Mathf.Max(0f, half.y - r), Mathf.Max(0f, half.z - r));
            var v = new List<Vector3>(); var nm = new List<Vector3>(); var col = new List<Color>(); var uv = new List<Vector2>(); var tri = new List<int>();
            Color baseC = _color.Value;
            // 6 граней куба в [-1,1]: (нормаль, ось u, ось v), деления по u/v
            Vector3[] N = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] U = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
            for (int f = 0; f < 6; f++)
            {
                Vector3 nf = N[f], uf = U[f], vf = Vector3.Cross(nf, uf);
                int nu = Mathf.Abs(uf.x) > 0.5f ? n : nShort, nv = Mathf.Abs(vf.x) > 0.5f ? n : nShort;
                int start = v.Count;
                for (int j = 0; j <= nv; j++)
                    for (int i = 0; i <= nu; i++)
                    {
                        Vector3 c = nf + uf * (i / (float)nu * 2f - 1f) + vf * (j / (float)nv * 2f - 1f);   // точка на кубе [-1,1]
                        Vector3 p = Vector3.Scale(c, half);
                        Vector3 cl = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 d = p - cl;
                        Vector3 nn = d.sqrMagnitude > 1e-10f ? d.normalized : nf;
                        Vector3 pos = cl + nn * r;
                        v.Add(pos); nm.Add(nn);
                        uv.Add(new Vector2(pos.x + pos.z, pos.y + pos.z) * 0.8f);
                        float big = Mathf.PerlinNoise(pos.x * 9f + 3.1f, pos.z * 9f + pos.y * 5f + 7.7f);
                        float m = 0.9f + 0.18f * big;
                        col.Add(new Color(baseC.r * m, baseC.g * m, baseC.b * m * 0.97f, 0.55f));
                    }
                for (int j = 0; j < nv; j++)
                    for (int i = 0; i < nu; i++)
                    {
                        int a = start + j * (nu + 1) + i, b = a + 1, c2 = a + (nu + 1), d2 = c2 + 1;
                        tri.Add(a); tri.Add(c2); tri.Add(b);
                        tri.Add(b); tri.Add(c2); tri.Add(d2);
                    }
            }
            _mesh.Clear();
            _mesh.SetVertices(v); _mesh.SetNormals(nm); _mesh.SetColors(col); _mesh.SetUVs(0, uv); _mesh.SetTriangles(tri, 0);
            // проверка лицевой стороны: нормаль треугольника должна смотреть наружу, иначе — переворачиваем все
            if (tri.Count >= 3)
            {
                Vector3 p0 = v[tri[0]], p1 = v[tri[1]], p2 = v[tri[2]];
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), nm[tri[0]]) < 0f)
                {
                    for (int k = 0; k < tri.Count; k += 3) { int t = tri[k + 1]; tri[k + 1] = tri[k + 2]; tri[k + 2] = t; }
                    _mesh.SetTriangles(tri, 0);
                }
            }
            _mesh.RecalculateBounds();
        }

        // ───────────────────────── подсказка ─────────────────────────

        private static GUIStyle _st;
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (Time.time >= _lookAt)
            {
                _lookAt = Time.time + 0.15f;
                Camera cam = Camera.main;
                _looking = cam != null && _box != null && _box.enabled && _box.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out _, 6f);
            }
            if (!_looking) return;
            Vector3 s = _size.Value;
            string q = Quality01 < 0.6f ? $" · <color=#FF8A5A>качество {Quality01 * 100f:0}%</color>" : $" · качество {Quality01 * 100f:0}%";
            string t;
            if (Mathf.Min(s.x, s.z) > 0.45f)
                t = $"<b>Пласт теста</b> {s.x:0.00}×{s.z:0.00} м × {s.y * 100f:0} см — режь шпателем на полосы (лезвие поперёк взгляда), потом полосы — на ньокки{q}";
            else if (IsGnocchi)
            {
                float k = Size01;
                string v = k >= 0.99f ? "<color=#9BE37A>в самый раз ✔</color>"
                         : Mathf.Max(s.x, s.z) > _pieceMax ? "<color=#FFD25A>крупно — разрежь</color>" : "<color=#FFD25A>мелко</color>";
                t = $"<b>Ньокк</b> {s.x * 100f:0}×{s.z * 100f:0}×{s.y * 100f:0} см — {v} (норма {_pieceMin * 100f:0}–{_pieceMax * 100f:0} см){q}";
            }
            else t = $"<b>Колбаска теста</b> {s.x:0.00} м × {s.z * 100f:0} см × {s.y * 100f:0} см — руби шпателем поперёк на ньокки ({_pieceMin * 100f:0}–{_pieceMax * 100f:0} см){q}";
            if (_st == null) _st = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
            Rect r = new Rect(Screen.width * 0.5f - 340f, Screen.height * 0.66f, 680f, 40f);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(r, t, _st);
        }
    }
}
