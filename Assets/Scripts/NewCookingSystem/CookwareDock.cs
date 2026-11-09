using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТОЧКА СТЫКОВКИ ТАРЫ — «магнит» у станка.
    ///
    /// Ставится туда, куда должна встать тара: под выход измельчителя, под кран
    /// с водой, к миксеру. Любая тара, чьё основание оказалось в зоне, считается
    /// ПОДКЛЮЧЁННОЙ — станок сыплет прямо в неё, без физики и дуг.
    ///
    ///   Ведро, корыто в руках — подносишь, отпускаешь рядом: магнит плавно
    ///   подтягивает тару в точку, выравнивает и фиксирует. Взял снова — отпустила.
    ///   Ящик на кране — висит в зоне: подключён, но НЕ притягивается (иначе
    ///   магнит дрался бы с тросом). Кран сам держит его где надо.
    ///
    /// Тара ищется по списку всей тары (Cookware.All), а не физикой: у тары в руках
    /// коллайдеры выключены, OverlapSphere её бы не увидел.
    ///
    /// Кто подключён, каждый игрок считает сам по позициям (они синхронизированы),
    /// поэтому подсветка и подсказки работают без сети. Притягивает только сервер.
    ///
    /// ═══ КАК ВЕШАТЬ ═══
    /// Пустой объект «Dock» дочерним у станка:
    ///   • позиция — на полу, где должен стоять ЦЕНТР ДНА тары;
    ///   • синяя ось (Z) — куда тара будет повёрнута «передом» после притягивания.
    /// Выдели — в сцене видны: зелёный круг (зона подключения), жёлтый (зона магнита).
    /// </summary>
    public sealed class CookwareDock : MonoBehaviour
    {
        [Header("Подключение")]
        [Tooltip("Основание тары ближе этого по горизонтали — тара подключена, м")]
        [Min(0.05f)][SerializeField] private float _radius = 0.8f;
        [Tooltip("На сколько тара может быть выше точки (ящик висит на кране), м")]
        [Min(0f)][SerializeField] private float _heightAbove = 2.5f;
        [Tooltip("На сколько ниже, м")]
        [Min(0f)][SerializeField] private float _heightBelow = 0.4f;
        [Tooltip("Какую тару принимать. Пусто — любую.")]
        [SerializeField] private CookwareDef[] _accept;

        [Header("Магнит")]
        [Tooltip("Притягивать отпущенную рядом тару в точку")]
        [SerializeField] private bool _magnet = true;
        [Tooltip("Зона магнита, м. Обычно чуть больше зоны подключения.")]
        [Min(0.05f)][SerializeField] private float _magnetRadius = 1.0f;
        [Tooltip("Ставить МГНОВЕННО (вкл): тара сразу оказывается в точке и фиксируется. " +
                 "Выкл — плавно подтягивается со скоростью Snap Speed.")]
        [SerializeField] private bool _instantSnap = true;
        [Tooltip("Скорость притягивания, м/с")]
        [SerializeField] private float _snapSpeed = 2.5f;
        [Tooltip("Поворачивать тару «передом» по синей оси точки")]
        [SerializeField] private bool _alignYaw = true;
        [Tooltip("Скорость поворота, град/с")]
        [SerializeField] private float _alignSpeed = 360f;

        [Tooltip("Ставить дно на настоящий пол под точкой (луч вниз). Выкл — ровно на высоту точки.")]
        [SerializeField] private bool _snapToFloor = true;
        [Tooltip("Зазор над полом, м — чтобы тара не цеплялась за пол")]
        [Min(0f)][SerializeField] private float _floorClearance = 0.005f;

        [Header("Куда может смотреть носик тары")]
        [Tooltip("Вдоль синей оси точки (Z+)")]
        [SerializeField] private bool _noseForward = true;
        [Tooltip("Против синей оси (Z−)")]
        [SerializeField] private bool _noseBack = true;
        [Tooltip("Вдоль красной оси (X+)")]
        [SerializeField] private bool _noseRight = true;
        [Tooltip("Против красной оси (X−). Выключи ту сторону, где станок — носик не упрётся в него.")]
        [SerializeField] private bool _noseLeft = false;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private Cookware _current;
        private Cookware _locked;
        private readonly HashSet<Rigidbody> _jointed = new HashSet<Rigidbody>();
        private float _jointScan;

        /// <summary>Подключённая тара (или null). Работает у всех игроков.</summary>
        public Cookware Current => _current;

        /// <summary>Точка приёма в мире: горловина подключённой тары или сама точка.</summary>
        public Vector3 PourPoint => _current != null ? _current.MouthPoint : transform.position;

        private void Update()
        {
            Cookware found = FindNearest(_radius);
            if (found != _current)
            {
                if (_log && InstanceFinder.IsServerStarted)
                    Debug.Log(found != null ? $"[Стыковка] подключена «{found.Def?.Title}»"
                                            : "[Стыковка] тара отключена", this);
                _current = found;
            }

        }

        /// <summary>Ближайшая подходящая тара в зоне.</summary>
        public Cookware FindNearest(float radius)
        {
            Cookware best = null;
            float bestD = float.MaxValue;
            Vector3 dock = transform.position;
            if (BakeOven.SeatedAt(dock)) return null;        // на огне стоит духовка — кастрюли не тянем
            foreach (Cookware c in Cookware.All)
            {
                if (c == null || !Accepts(c)) continue;
                if (BakeOven.Holds(c)) continue;             // стоит в духовке на решётке — не наша
                // Сама станция (точка — её ребёнок) и бетономешалка — не тара для точки: магнит её не тянет
                if (transform.IsChildOf(c.transform) || c.GetComponent<MixerDough>() != null) continue;
                Vector3 p = c.transform.position;
                float dy = p.y - dock.y;
                if (dy > _heightAbove || dy < -_heightBelow) continue;
                float d = new Vector2(p.x - dock.x, p.z - dock.z).magnitude;
                if (d > radius || d >= bestD) continue;
                best = c; bestD = d;
            }
            return best;
        }

        /// <summary>Свободна ли точка: никто не стоит, не подъезжает.</summary>
        public bool IsFree => _current == null && _snapping == null && _locked == null;
        public float MagnetRadius => _magnetRadius;

        public bool Accepts(Cookware c)
        {
            if (_accept == null || _accept.Length == 0) return true;
            foreach (CookwareDef d in _accept) if (d == c.Def) return true;
            return false;
        }

        // ───────────────────────── МАГНИТ (сервер) ─────────────────────────
        //
        // Притягивание идёт в ФИЗИЧЕСКОМ шаге и кинематически: сервер на время
        // забирает тару себе и плавно ведёт её в точку. Раньше тара двигалась в
        // Update, а физика толкала её обратно — отсюда дрожь.
        //
        // Поворот: модель тары может быть повёрнута как угодно (у FBX «верх»
        // часто не ось Y). Поэтому магнит сам находит ось модели, которая
        // смотрит вверх, выравнивает её по вертикали и доворачивает тару только
        // вокруг вертикали — до ближайшей стороны (не больше 45°).

        private Cookware _snapping;
        private Rigidbody _snapRb;
        private readonly Dictionary<Rigidbody, float> _ignoreUntil = new Dictionary<Rigidbody, float>();

        // ───────────────────────── КРАН ─────────────────────────
        //
        // Контракт для CraneLoad: стоящая в точке тара заморожена (кинематика),
        // и крюк сам её не возьмёт. Кран спрашивает «это тело держишь ты?» и,
        // если да, просит отпустить — дальше телом управляет кран.

        /// <summary>Эта точка держит тело (тара стоит или подъезжает). Для CraneLoad.</summary>
        public bool HasCraneClaim(Rigidbody body)
        {
            if (body == null) return false;
            if (_snapRb == body) return true;
            return _locked != null && _locked.GetComponent<Rigidbody>() == body;
        }

        /// <summary>Кран забирает тело: перестать держать и не хватать его снова пару секунд.</summary>
        public void ReleaseForCrane(Rigidbody body)
        {
            if (body == null || !InstanceFinder.IsServerStarted || !HasCraneClaim(body)) return;
            if (_snapRb == body) { _snapping = null; _snapRb = null; }
            if (_locked != null && _locked.GetComponent<Rigidbody>() == body) _locked = null;
            body.isKinematic = false;
            body.useGravity = true;
            body.WakeUp();
            _ignoreUntil[body] = Time.time + 2.5f;
            if (_log) Debug.Log($"[Стыковка] «{body.name}» отдана крану", this);
        }

        private bool IgnoredForCrane(Rigidbody rb)
        {
            if (_ignoreUntil.TryGetValue(rb, out float until))
            {
                if (Time.time < until) return true;
                _ignoreUntil.Remove(rb);
            }
            // Висит на крюке — не трогаем.
            CraneLoad load = rb.GetComponent<CraneLoad>();
            return load != null && load.IsAttached;
        }
        private Quaternion _snapRot;
        private Vector3 _snapPos;

        private void FixedUpdate()
        {
            if (_magnet && InstanceFinder.IsServerStarted) ServerMagnet(Time.fixedDeltaTime);
        }

        private void ServerMagnet(float dt)
        {
            Vector3 dp = transform.position;

            // Стоит на месте — отпускаем, только если унесли.
            if (_locked != null)
            {
                Vector3 lp = _locked.transform.position;
                if (new Vector2(lp.x - dp.x, lp.z - dp.z).magnitude > _magnetRadius * 1.2f) _locked = null;
                else return;
            }

            // Идёт притягивание.
            if (_snapping != null)
            {
                if (_snapRb == null) { _snapping = null; return; }
                // Кран подцепил посреди притягивания — отпускаем.
                CraneLoad movingLoad = _snapRb.GetComponent<CraneLoad>();
                if (movingLoad != null && movingLoad.IsAttached)
                { _snapRb.isKinematic = false; _snapping = null; _snapRb = null; return; }
                Vector3 p = _snapRb.position;
                // Схватили посреди притягивания и понесли — бросаем.
                if (new Vector2(p.x - dp.x, p.z - dp.z).magnitude > _magnetRadius * 1.3f)
                {
                    _snapping = null; _snapRb = null;
                    return;
                }

                Vector3 np = Vector3.MoveTowards(p, _snapPos, _snapSpeed * dt);
                Quaternion nr = Quaternion.RotateTowards(_snapRb.rotation, _snapRot, _alignSpeed * dt);
                _snapRb.MovePosition(np);
                _snapRb.MoveRotation(nr);

                if ((np - _snapPos).sqrMagnitude < 0.000004f && Quaternion.Angle(nr, _snapRot) < 0.5f)
                {
                    _locked = _snapping;
                    if (_log) Debug.Log($"[Стыковка] «{_snapping.Def?.Title}» встала на место", this);
                    _snapping = null; _snapRb = null;
                }
                return;
            }

            // Ищем, кого притянуть.
            Cookware c = FindNearest(_magnetRadius);
            if (c == null) return;
            Rigidbody rb = c.GetComponent<Rigidbody>();
            if (rb == null) return;
            if (rb.isKinematic || IsJointed(rb) || IgnoredForCrane(rb)) return;   // в руках, на тросе, у крана
            if (rb.linearVelocity.sqrMagnitude > 4f) return;        // ещё летит

            NetworkObject nob = c.NetworkObject;
            if (nob != null && nob.Owner.IsValid) nob.RemoveOwnership();

            _snapRot = TargetRotation(rb.rotation, c);
            _snapPos = TargetPosition(c, rb, _snapRot);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;          // дальше ведём сами, физика не мешает
            if (_instantSnap)
            {
                // Сразу в точку: без подъёма снизу и без шанса провалиться по пути.
                rb.position = _snapPos;
                rb.rotation = _snapRot;
                c.transform.SetPositionAndRotation(_snapPos, _snapRot);
                _locked = c;
                if (_log) Debug.Log($"[Стыковка] «{c.Def?.Title}» встала на место (сразу)", this);
                return;
            }
            _snapping = c;
            _snapRb = rb;
        }

        /// <summary>
        /// Поставить тару в точку СРАЗУ и зафиксировать (кнопка «поставить на огонь», тест-клавиши):
        /// дном вниз (запомненная поза тары), дно — на пол/подставку под точкой, центр дна — в точке.
        /// Большая тара (духовка-бочка) не успевает «долететь» до магнита — ставим напрямую.
        /// </summary>
        public bool ServerPlace(Cookware c)
        {
            if (c == null || !InstanceFinder.IsServerStarted) return false;
            NetworkObject nob = c.NetworkObject;
            if (nob != null && nob.Owner.IsValid) nob.RemoveOwnership();     // позой теперь владеет сервер
            Rigidbody rb = c.GetComponent<Rigidbody>();
            Quaternion rot = TargetRotation(c.transform.rotation, c);
            Vector3 pos = TargetPosition(c, rb, rot);
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.position = pos;
                rb.rotation = rot;
            }
            c.transform.SetPositionAndRotation(pos, rot);
            _snapping = null; _snapRb = null;
            _locked = c;
            if (_log) Debug.Log($"[Стыковка] «{c.Def?.Title}» поставлена в «{name}» (ровно, зафиксирована)", this);
            return true;
        }

        /// <summary>
        /// Целевой поворот тары.
        ///   1. «Дном вниз»: если у тары запомнена поза — берём её (надёжно). Иначе —
        ///      Up Axis тары, иначе угадываем ближайшую к вертикали ось.
        ///   2. Носик: из разрешённых направлений точки выбираем то, до которого
        ///      тару повернуть ближе всего. Поворот — только вокруг вертикали.
        /// </summary>
        private Quaternion TargetRotation(Quaternion current, Cookware c)
        {
            Quaternion upright = c.HasUprightPose ? c.UprightRotation : Level(current, c.LocalUp);

            Vector3 nose = Vector3.ProjectOnPlane(upright * c.NoseLocal, Vector3.up);
            if (nose.sqrMagnitude < 1e-6f) nose = Vector3.ProjectOnPlane(upright * Vector3.forward, Vector3.up);
            if (nose.sqrMagnitude < 1e-6f) return upright;
            nose.Normalize();

            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 r = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            Quaternion best = upright;
            float bestAngle = float.MaxValue;
            bool any = false;
            void Try(bool allowed, Vector3 dir)
            {
                if (!allowed || dir.sqrMagnitude < 1e-6f) return;
                any = true;
                Quaternion q = Quaternion.AngleAxis(Vector3.SignedAngle(nose, dir, Vector3.up), Vector3.up) * upright;
                float a = Quaternion.Angle(current, q);
                if (a < bestAngle) { bestAngle = a; best = q; }
            }
            Try(_noseForward, f);
            Try(_noseBack, -f);
            Try(_noseRight, r);
            Try(_noseLeft, -r);
            if (!any || !_alignYaw) return any ? best : upright;
            return best;
        }

        /// <summary>Поставить вертикально ось модели «верх» (задана или угадана).</summary>
        private static Quaternion Level(Quaternion q, Vector3 modelUp)
        {
            Vector3 upAxis;
            if (modelUp != Vector3.zero) upAxis = q * modelUp;
            else
            {
                Vector3[] axes = { q * Vector3.right, q * Vector3.up, q * Vector3.forward };
                upAxis = Vector3.up;
                float bestDot = -1f;
                foreach (Vector3 a in axes)
                {
                    float d = Vector3.Dot(a, Vector3.up);
                    if (Mathf.Abs(d) > bestDot) { bestDot = Mathf.Abs(d); upAxis = d >= 0f ? a : -a; }
                }
            }
            return Quaternion.FromToRotation(upAxis, Vector3.up) * q;
        }

        /// <summary>
        /// Куда поставить опору тары, чтобы в ЦЕЛЕВОМ повороте её дно стояло на полу,
        /// а центр дна — в точке стыковки.
        ///
        /// Габариты берутся в осях самой модели (не мировая рамка в текущем положении):
        /// раньше у банки, лежащей на боку, «дном» считалась боковая стенка, и после
        /// поворота опора уезжала под пол.
        /// </summary>
        private Vector3 TargetPosition(Cookware c, Rigidbody rb, Quaternion targetRot)
        {
            Transform root = c.transform;
            bool any = false;
            Vector3 lmin = Vector3.one * float.MaxValue, lmax = Vector3.one * float.MinValue;

            foreach (MeshFilter mf in c.GetComponentsInChildren<MeshFilter>())
            {
                // Кучка внутри тары и прочие авто-объекты в габарит не входят.
                if (mf.sharedMesh == null || (mf.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                Renderer r = mf.GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    // Угол меша → мир → оси корня тары (без масштаба корня).
                    Vector3 local = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    lmin = Vector3.Min(lmin, local);
                    lmax = Vector3.Max(lmax, local);
                    any = true;
                }
            }

            Vector3 dock = FloorAtDock(root);
            if (!any) return dock;

            // Габарит в целевом повороте: 8 углов коробки модели.
            Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, targetRot, root.lossyScale);
            Vector3 wmin = Vector3.one * float.MaxValue, wmax = Vector3.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? lmin.x : lmax.x,
                                             (i & 2) == 0 ? lmin.y : lmax.y,
                                             (i & 4) == 0 ? lmin.z : lmax.z);
                Vector3 w = m.MultiplyPoint3x4(corner);
                wmin = Vector3.Min(wmin, w);
                wmax = Vector3.Max(wmax, w);
            }
            // Опора = точка стыковки минус (центр дна относительно опоры).
            Vector3 bottomCentre = new Vector3((wmin.x + wmax.x) * 0.5f, wmin.y, (wmin.z + wmax.z) * 0.5f);
            // Центр по горизонтали — не по габариту (у сковороды ручка тянет его вбок), а по ЧАШЕ:
            // дочерний «DockCenter» (поставь руками, если надо точно) или центр дна из Cookware Fill Tuner.
            Vector3 centreW;
            if (BowlCentre(c, out centreW))
            {
                Vector3 w = m.MultiplyPoint3x4(root.InverseTransformPoint(centreW));
                bottomCentre.x = w.x; bottomCentre.z = w.z;
            }
            return dock - bottomCentre + new Vector3(0f, _floorClearance, 0f);
        }

        /// <summary>Центр чаши тары в мире: DockCenter (дочерний объект) → Cookware Fill Tuner → нет.</summary>
        private static bool BowlCentre(Cookware c, out Vector3 world)
        {
            Transform dc = c.transform.Find("DockCenter");
            if (dc != null) { world = dc.position; return true; }
            CookwareFillTuner t = c.GetComponent<CookwareFillTuner>();
            if (t != null) { world = t.HeapCenterWorld; return true; }
            world = Vector3.zero;
            return false;
        }

        /// <summary>Настоящая высота пола под точкой стыковки (луч вниз, мимо самой тары).</summary>
        private Vector3 FloorAtDock(Transform ignore)
        {
            Vector3 p = transform.position;
            if (!_snapToFloor) return p;
            RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 4f, ~0,
                                                   QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 floor = p;
            foreach (RaycastHit h in hits)
            {
                if (ignore != null && h.transform.IsChildOf(ignore)) continue;
                if (h.collider.GetComponentInParent<FoodHeap>() != null) continue;
                if (h.distance < best) { best = h.distance; floor = new Vector3(p.x, h.point.y, p.z); }
            }
            return floor;
        }

        /// <summary>Висит ли тело на суставе (крюк крана, цепь). Список обновляется раз в секунду.</summary>
        private bool IsJointed(Rigidbody rb)
        {
            if (Time.time >= _jointScan)
            {
                _jointScan = Time.time + 1f;
                _jointed.Clear();
                foreach (Joint j in FindObjectsByType<Joint>(FindObjectsSortMode.None))
                {
                    if (j == null) continue;
                    if (j.connectedBody != null) _jointed.Add(j.connectedBody);
                    Rigidbody own = j.GetComponent<Rigidbody>();
                    if (own != null) _jointed.Add(own);
                }
            }
            return _jointed.Contains(rb);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 p = transform.position;
            DrawCircle(p, _radius, new Color(0.3f, 1f, 0.4f, 0.9f));
            if (_magnet) DrawCircle(p, _magnetRadius, new Color(1f, 0.85f, 0.2f, 0.7f));
            // Стрелки направлений носика: зелёная — можно, красная — нельзя.
            Vector3 fw = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 rt = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            DrawNose(p, fw, _noseForward);
            DrawNose(p, -fw, _noseBack);
            DrawNose(p, rt, _noseRight);
            DrawNose(p, -rt, _noseLeft);
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.35f);
            Gizmos.DrawLine(p, p + Vector3.up * _heightAbove);
        }

        private void DrawNose(Vector3 p, Vector3 dir, bool allowed)
        {
            Gizmos.color = allowed ? new Color(0.3f, 1f, 0.4f, 1f) : new Color(1f, 0.25f, 0.2f, 1f);
            Vector3 tip = p + dir * _radius * 1.1f + Vector3.up * 0.02f;
            Gizmos.DrawLine(p + Vector3.up * 0.02f, tip);
            Gizmos.DrawSphere(tip, _radius * 0.06f);
        }

        private static void DrawCircle(Vector3 c, float r, Color col)
        {
            Gizmos.color = col;
            Vector3 prev = c + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                Vector3 next = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
