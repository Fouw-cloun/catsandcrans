using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ВРЕМЕННЫЙ ТЕСТ КОСТРА И КАСТРЮЛИ. Только хост. Удалить, когда не нужен.
    ///
    ///   F9  — СКОВОРОДУ на огонь; ещё раз — обратно на место спавна
    ///   F10 — кастрюлю на огонь (в точку Pot Dock костра); ещё раз — обратно на место спавна
    ///   F11 — накидать доски
    ///   F12 — поджечь
    ///   F3  — раздуть (воздух)
    ///   U   — крышка кастрюли
    ///   O   — ДУХОВКУ на огонь (в ту же точку костра); ещё раз — обратно на место спавна
    ///   L   — крышка духовки: откинуть / закрыть
    ///   I   — люк на крышке духовки: открыть / закрыть
    ///   Всё сразу — клавиша All Key (по умолчанию нет) или галка Auto On Start.
    ///
    /// Вешается на любой объект сцены (удобно — на сам костёр). Доски кладутся
    /// как обычные, поджиг — BonfirePit.ServerDebugIgnite (без горелки).
    ///
    /// МЕСТО СПАВНА: при старте сервера запоминается, где стоят вся тара и духовки. Что стояло на огне,
    /// когда туда ставят другое, — уезжает на своё место (а не «вбок», где его снова хватал магнит).
    /// Повторное нажатие той же клавиши — вернуть на место. Точка огня отпускает тару (ReleaseForCrane),
    /// духовка снимается с огня (ServerUnseat), скорость обнуляется, кинематика — как была при старте.
    /// </summary>
    public sealed class BonfireTestKeys : MonoBehaviour
    {
        [Header("Что тестируем (пусто — найдётся само)")]
        [Tooltip("Костёр. Пусто — на этом объекте или ближайший к игроку.")]
        [SerializeField] private BonfirePit _bonfire;
        [Tooltip("Точка, куда ставится кастрюля. Пусто — Pot Dock костра.")]
        [SerializeField] private CookwareDock _dock;
        [Tooltip("Кастрюля. Пусто — ближайшая к игроку тара с Cookware Heat (не сковорода).")]
        [SerializeField] private Cookware _pot;
        [Tooltip("Сковорода. Пусто — ближайшая тара «Сковорода» (Def плоская или в названии pan/сковор).")]
        [SerializeField] private Cookware _pan;

        [Header("Клавиши")]
        [Tooltip("Сковороду на огонь / обратно на место")]
        [SerializeField] private KeyCode _panKey = KeyCode.F9;
        [SerializeField] private KeyCode _potKey = KeyCode.F10;
        [SerializeField] private KeyCode _fillKey = KeyCode.F11;
        [SerializeField] private KeyCode _igniteKey = KeyCode.F12;
        [SerializeField] private KeyCode _airKey = KeyCode.F3;
        [Tooltip("Накрыть кастрюлю крышкой / снять крышку")]
        [SerializeField] private KeyCode _lidKey = KeyCode.U;
        [Tooltip("Префаб крышки — если свободной крышки в сцене нет, создастся из него")]
        [SerializeField] private GameObject _lidPrefab;
        [Header("Духовка")]
        [SerializeField] private KeyCode _ovenKey = KeyCode.O;
        [SerializeField] private KeyCode _ovenLidKey = KeyCode.L;
        [SerializeField] private KeyCode _ovenHatchKey = KeyCode.I;
        [Header("Хлеб (котлеты)")]
        [Tooltip("Готовый хлеб перед камерой — чтобы не печь каждый тест. Shift — следующий вид (буханка / багет)")]
        [SerializeField] private KeyCode _breadKey = KeyCode.Alpha6;
        [Tooltip("Префабы хлеба. Пусто — берутся из форм для хлеба (Bread Mold) в сцене")]
        [SerializeField] private GameObject[] _breadPrefabs;
        [Tooltip("Всё сразу: кастрюля + доски + поджог")]
        [SerializeField] private KeyCode _allKey = KeyCode.None;

        [Header("Сколько")]
        [Range(1, 60)][SerializeField] private int _planks = 6;
        [Range(0.05f, 1f)][SerializeField] private float _air = 0.6f;

        [Header("Авто")]
        [Tooltip("Через пару секунд после старта сам ставит кастрюлю, кладёт доски и поджигает")]
        [SerializeField] private bool _autoOnStart;
        [SerializeField] private float _autoDelay = 3f;

        [Header("Экран")]
        [SerializeField] private bool _hud = true;

        private float _autoAt;
        private bool _autoDone;
        private string _last = "";

        private void Start() => _autoAt = Time.time + _autoDelay;

        private void Update()
        {
            if (!InstanceFinder.IsServerStarted) { _homesTaken = false; return; }
            if (!_homesTaken) TakeHomes();

            if (_autoOnStart && !_autoDone && Time.time >= _autoAt) { _autoDone = true; All(); }

            if (Down(_panKey)) PanToFire();
            if (Down(_potKey)) PotToFire();
            if (Down(_fillKey)) Fill();
            if (Down(_igniteKey)) Ignite();
            if (Down(_airKey)) Blow();
            if (Down(_allKey)) All();
            if (Down(_lidKey)) ToggleLid(Pot());
            if (Down(_ovenKey)) OvenToFire();
            if (Down(_ovenLidKey)) ToggleOvenLid();
            if (Down(_ovenHatchKey)) ToggleHatch();
            if (Down(_breadKey)) SpawnBread(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        // ───────────────────────── хлеб по кнопке ─────────────────────────

        private int _breadIdx;

        private List<GameObject> BreadPrefabs()
        {
            var list = new List<GameObject>();
            if (_breadPrefabs != null) foreach (GameObject g in _breadPrefabs) if (g != null && !list.Contains(g)) list.Add(g);
            if (list.Count > 0) return list;
            var f = typeof(BreadMold).GetField("_breadPrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null)
                foreach (BreadMold m in Object.FindObjectsByType<BreadMold>(FindObjectsSortMode.None))
                {
                    GameObject g = f.GetValue(m) as GameObject;
                    if (g != null && !list.Contains(g)) list.Add(g);
                }
            return list;
        }

        /// <summary>Готовый (испечённый) хлеб перед камерой. Shift — следующий вид.</summary>
        private void SpawnBread(bool next)
        {
            List<GameObject> list = BreadPrefabs();
            if (list.Count == 0) { Say("хлеба нет: в сцене нет форм для хлеба (Bread Mold) и пусто Bread Prefabs у Bonfire Test Keys"); return; }
            if (next) _breadIdx++;
            GameObject prefab = list[((_breadIdx % list.Count) + list.Count) % list.Count];
            Camera cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3 at = (cam != null ? cam.transform.position : Vector3.zero) + fwd * 1.5f + Vector3.up * 0.2f;
            GameObject go = Instantiate(prefab, at, Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, Vector3.up).normalized + Vector3.forward * 1e-4f, Vector3.up));
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp != null) fp.ServerPreset(null, FoodState.Baked, 100, 100, 100);
            InstanceFinder.ServerManager.Spawn(go);
            Say($"хлеб «{prefab.name}» — испечённый (Shift+{_breadKey} — другой вид)");
        }

        private static bool Down(KeyCode k) => k != KeyCode.None && Input.GetKeyDown(k);

        private void All() { PotToFire(); Fill(); Ignite(); }

        // ───────────────────────── поиск ─────────────────────────

        private static Vector3 Me => Camera.main != null ? Camera.main.transform.position : Vector3.zero;

        private BonfirePit Pit()
        {
            if (_bonfire != null) return _bonfire;
            BonfirePit own = GetComponentInParent<BonfirePit>();
            if (own != null) return own;
            BonfirePit best = null; float bd = float.MaxValue;
            foreach (BonfirePit p in BonfirePit.All)
            {
                if (p == null) continue;
                float d = (p.transform.position - Me).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        private CookwareDock Dock(BonfirePit pit)
        {
            if (_dock != null) return _dock;
            if (pit == null) return null;
            CookwareDock d = pit.PotDock;
            return d != null ? d : pit.GetComponentInChildren<CookwareDock>();
        }

        private Cookware Pot()
        {
            if (_pot != null) return _pot;
            Cookware best = null; float bd = float.MaxValue;
            foreach (Cookware c in Cookware.All)
            {
                if (c == null || c.GetComponent<CookwareHeat>() == null || c.GetComponent<BakeOven>() != null || IsPan(c)) continue;
                if (c.GetComponent<MixerDough>() != null || BakeOven.Holds(c)) continue;
                float d = (c.transform.position - Me).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        private Cookware Pan()
        {
            if (_pan != null) return _pan;
            Cookware best = null; float bd = float.MaxValue;
            foreach (Cookware c in Cookware.All)
            {
                if (c == null || !IsPan(c) || BakeOven.Holds(c)) continue;
                float d = (c.transform.position - Me).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        /// <summary>Сковорода: Def плоская (Flat) или «Сковорода», или в имени объекта pan / skovorod / сковор.</summary>
        public static bool IsPan(Cookware c)
        {
            if (c == null) return false;
            if (c.Def != null)
            {
                if (c.Def.Kind == CookwareKind.Flat) return true;
                if (!string.IsNullOrEmpty(c.Def.Title) && c.Def.Title.ToLowerInvariant().Contains("сковор")) return true;
            }
            string n = c.name.ToLowerInvariant();
            return n.Contains("pan") || n.Contains("skovorod") || n.Contains("сковор");
        }

        // ───────────────────────── место спавна ─────────────────────────

        private struct Home { public Vector3 Pos; public Quaternion Rot; public bool Kin; }
        private readonly Dictionary<Transform, Home> _home = new Dictionary<Transform, Home>();
        private bool _homesTaken;

        private void TakeHomes()
        {
            _homesTaken = true;
            _home.Clear();
            foreach (Cookware c in Cookware.All) if (c != null) Remember(c.transform);
            foreach (BakeOven o in BakeOven.All) if (o != null) Remember(o.transform);
            Debug.Log($"[Тест костра] запомнил места спавна: {_home.Count} шт. (тара и духовки) — повторная клавиша вернёт туда", this);
        }

        private void Remember(Transform t)
        {
            if (t == null || _home.ContainsKey(t)) return;
            Rigidbody rb = t.GetComponent<Rigidbody>();
            _home[t] = new Home { Pos = t.position, Rot = t.rotation, Kin = rb != null && rb.isKinematic };
        }

        /// <summary>Стоит ли тара в точке огня (поставлена, едет магнитом или просто подключена).</summary>
        private static bool OnFire(Cookware c, CookwareDock dock)
        {
            if (c == null || dock == null) return false;
            Rigidbody rb = c.GetComponent<Rigidbody>();
            if (rb != null && dock.HasCraneClaim(rb)) return true;
            return dock.Current == c;
        }

        /// <summary>Вернуть на место спавна: отпустить из точек огня, снять духовку с огня, поставить как было при старте.</summary>
        private bool GoHome(Transform t)
        {
            if (t == null) return false;
            if (!_home.TryGetValue(t, out Home h))
            {
                Say($"«{t.name}»: место спавна не запомнено (появилась после старта) — убираю вбок");
                return false;
            }
            NetworkObject nob = t.GetComponent<NetworkObject>();
            if (nob != null && nob.Owner.IsValid) nob.RemoveOwnership();
            Rigidbody rb = t.GetComponent<Rigidbody>();
            if (rb != null)
                foreach (CookwareDock d in FindObjectsByType<CookwareDock>(FindObjectsSortMode.None))
                    if (d != null && d.HasCraneClaim(rb)) d.ReleaseForCrane(rb);       // точка отпускает и пару секунд не хватает снова
            BakeOven oven = t.GetComponent<BakeOven>();
            if (oven != null && oven.IsSeated) oven.ServerUnseat();
            if (rb != null)
            {
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                rb.isKinematic = true;
                rb.position = h.Pos; rb.rotation = h.Rot;
            }
            t.SetPositionAndRotation(h.Pos, h.Rot);
            if (rb != null)
            {
                rb.isKinematic = h.Kin;
                if (!h.Kin) { rb.useGravity = true; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.WakeUp(); }
            }
            Physics.SyncTransforms();
            Say($"«{t.name}» → на место спавна");
            return true;
        }

        /// <summary>Убрать с огня: на место спавна, а если его нет — вбок.</summary>
        private void Clear(Cookware c, Vector3 seat, int i)
        {
            if (c == null) return;
            if (GoHome(c.transform)) return;
            Vector3 side = Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.right : Vector3.right, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Vector3 to = seat + side * (2.5f + i * 0.8f) + Vector3.up * 0.5f;
            Rigidbody orb = c.GetComponent<Rigidbody>();
            if (orb != null)
            {
                foreach (CookwareDock d in FindObjectsByType<CookwareDock>(FindObjectsSortMode.None))
                    if (d != null && d.HasCraneClaim(orb)) d.ReleaseForCrane(orb);
                orb.isKinematic = false; orb.useGravity = true; orb.position = to; orb.linearVelocity = Vector3.zero; orb.angularVelocity = Vector3.zero;
            }
            c.transform.position = to;
            Say($"«{c.name}» убрана с огня вбок");
        }

        // ───────────────────────── действия ─────────────────────────

        private BakeOven Oven()
        {
            BakeOven best = null; float bd = float.MaxValue;
            foreach (BakeOven o in BakeOven.All)
            {
                if (o == null) continue;
                float d = (o.transform.position - Me).sqrMagnitude;
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        private void PotToFire()
        {
            Cookware pot = Pot();
            if (pot == null) { Say("не найдена кастрюля с Cookware Heat"); return; }
            Toggle(pot, "кастрюля");
        }

        private void PanToFire()
        {
            Cookware pan = Pan();
            if (pan == null) { Say("не найдена сковорода — выдели её и нажми CatCooks → Борщ → 1 (или впиши в поле Pan)"); return; }
            if (pan.GetComponent<CookwareHeat>() == null) Say($"⚠ у «{pan.name}» нет Cookware Heat — греться не будет (Борщ → 1)");
            Toggle(pan, "сковорода");
        }

        /// <summary>Не на огне — поставить; уже на огне — вернуть на место спавна.</summary>
        private void Toggle(Cookware c, string what)
        {
            Remember(c.transform);
            CookwareDock dock = Dock(Pit());
            if (OnFire(c, dock))
            {
                NetworkObject nob = c.NetworkObject;
                if (nob != null && nob.Owner.IsValid) { Say($"{what} у игрока в руках — положи её (G) и нажми ещё раз"); return; }
                if (!GoHome(c.transform)) Clear(c, dock.transform.position, 0);
                return;
            }
            PlaceOnFire(c);
        }

        private void OvenToFire()
        {
            BakeOven oven = Oven();
            if (oven == null) { Say("не найдена духовка (Bake Oven)"); return; }
            Remember(oven.transform);
            if (oven.IsSeated) { if (!GoHome(oven.transform)) { oven.ServerUnseat(); Say("духовка снята с огня (места спавна нет — стоит, где стояла)"); } return; }
            BonfirePit pit = Pit();
            if (pit == null) { Say("костёр не найден"); return; }
            // точка посадки: пустышка «OvenSeat» у костра (центр дна духовки, синяя ось — перед), иначе Pot Dock
            Transform seat = null;
            foreach (Transform t in pit.GetComponentsInChildren<Transform>(true)) if (t.name == "OvenSeat") { seat = t; break; }
            CookwareDock dock = Dock(pit);
            if (seat == null && dock != null) seat = dock.transform;
            if (seat == null) seat = pit.transform;
            // всё, что стоит на огне (кастрюля, сковорода), — на место спавна, иначе духовка встанет в них
            int moved = 0;
            var near = new List<Cookware>();
            foreach (Cookware c in Cookware.All)
            {
                if (c == null || c.transform.IsChildOf(oven.transform) || BakeOven.Holds(c) || c.GetComponent<BakeOven>() != null) continue;
                Vector3 d = c.transform.position - seat.position;
                if (new Vector2(d.x, d.z).magnitude > 1.5f || Mathf.Abs(d.y) > 2f) continue;
                near.Add(c);
            }
            foreach (Cookware c in near) Clear(c, seat.position, moved++);
            oven.ServerPlaceAt(seat);
            Say($"духовка → на огонь («{seat.name}»)");
        }

        /// <summary>Поставить тару в точку костра: магнит дальше сам выровняет.</summary>
        private void PlaceOnFire(Cookware pot)
        {
            BonfirePit pit = Pit();
            CookwareDock dock = Dock(pit);
            if (dock == null) { Say("нет точки Pot Dock у костра"); return; }
            if (pot == null) { Say("тара не найдена"); return; }

            NetworkObject nob = pot.NetworkObject;
            if (nob != null && nob.Owner.IsValid)
            {
                Say("кастрюля у игрока в руках — положи её (G) и нажми ещё раз");
                return;
            }

            // На огне стоит духовка — её на место спавна (иначе тара встанет в неё).
            BakeOven ov = Oven();
            if (ov != null && ov.IsSeated && BakeOven.SeatedAt(dock.transform.position))
                if (!GoHome(ov.transform)) { ov.ServerUnseat(); Say("духовка снята с огня"); }
            // В точке уже стоит другая тара (кастрюля, когда ставим сковороду) — на её место спавна.
            Cookware other = dock.Current;
            if (other != null && other != pot) Clear(other, dock.transform.position, 0);
            foreach (Cookware c in Cookware.All)
            {
                if (c == null || c == pot || c == other) continue;
                Rigidbody crb = c.GetComponent<Rigidbody>();
                if (crb != null && dock.HasCraneClaim(crb)) Clear(c, dock.transform.position, 1);
            }
            // Точка сама ставит ровно: дном вниз, на подставку, центр дна — в точке, и фиксирует.
            if (!dock.ServerPlace(pot)) { Say("не получилось поставить"); return; }
            Say($"«{pot.name}» → на огонь (стоит ровно)");
        }

        private void Fill()
        {
            BonfirePit pit = Pit();
            if (pit == null) { Say("костёр не найден"); return; }
            var go = new GameObject("TestFuel (temp)");
            go.transform.position = pit.transform.position;
            BonfireFuel fuel = go.AddComponent<BonfireFuel>();
            fuel.ServerSetPlanks(_planks);
            pit.ServerAccept(fuel);
            if (go != null) Destroy(go);
            Say($"доски: {pit.Planks}");
        }

        private void Ignite()
        {
            BonfirePit pit = Pit();
            if (pit == null) { Say("костёр не найден"); return; }
            if (pit.IsBurning) { Say("уже горит"); return; }

            if (pit.Planks < 1) Fill();
            pit.ServerDebugIgnite();
            Say("костёр подожжён");
        }

        /// <summary>L: откидная крышка — открыть/закрыть; старая снимаемая — снять/надеть.</summary>
        private void ToggleOvenLid()
        {
            BakeOven oven = Oven();
            if (oven == null) { Say("не найдена духовка"); return; }
            if (oven.HingedLid != null)
            {
                oven.HingedLid.ServerToggle();
                Say(oven.HingedLid.IsOpen ? "духовка открыта (крышка откинута)" : "духовка закрыта");
                return;
            }
            Say("у духовки нет откидной крышки (Oven Hinged Lid)");
        }

        private void ToggleHatch()
        {
            BakeOven oven = Oven();
            if (oven == null) { Say("не найдена духовка"); return; }
            if (oven.HingedLid != null)
            {
                OvenVentHatch vh = oven.GetComponentInChildren<OvenVentHatch>(true);
                if (vh == null) { Say("у духовки нет люка (Oven Vent Hatch)"); return; }
                vh.ServerToggle();
                Say(vh.IsOpen ? "люк духовки открыт" : "люк духовки закрыт");
                return;
            }
            OvenVentHatch h = oven.Vent;
            if (h == null) { Say("у духовки нет люка (Oven Vent Hatch)"); return; }
            h.ServerToggle();
            Say(h.IsOpen ? "люк духовки открыт" : "люк духовки закрыт");
        }

        private void ToggleLid(Cookware pot)
        {
            PotLidSocket sock = pot != null ? pot.GetComponent<PotLidSocket>() : null;
            if (sock == null) { Say(pot == null ? "тара не найдена" : $"у «{pot.name}» нет Pot Lid Socket"); return; }

            if (sock.HasLid)
            {
                PotLidItem off = sock.ServerDetach();
                if (off != null)
                {
                    // Положить рядом, а не на кастрюлю — чтобы не мешала.
                    Vector3 side = Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.right : Vector3.right, Vector3.up).normalized;
                    Vector3 at = pot.transform.position + side * 0.9f + Vector3.up * 0.3f;
                    off.transform.position = at;
                    Rigidbody lrb = off.GetComponent<Rigidbody>();
                    if (lrb != null) { lrb.position = at; lrb.linearVelocity = Vector3.zero; lrb.angularVelocity = Vector3.zero; }
                }
                Say("крышка снята");
                return;
            }

            // Своя крышка кастрюли (Pot Lid Socket → Assigned Lid) — её в первую очередь.
            PotLidItem best = sock.AssignedLid;
            if (best != null && (best.IsAttached || (best.NetworkObject != null && best.NetworkObject.Owner.IsValid)))
            {
                Say($"своя крышка «{best.name}» сейчас {(best.IsAttached ? "на другой кастрюле" : "в руках")}");
                return;
            }
            if (best == null) best = FindFreeLid(pot);
            if (best == null && _lidPrefab != null)
            {
                GameObject go = Instantiate(_lidPrefab, sock.Socket.position, sock.Socket.rotation);
                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
                InstanceFinder.ServerManager.Spawn(go);
                best = go.GetComponent<PotLidItem>();
            }
            if (best == null) { Say("нет свободной крышки — назначь крышку в Pot Lid Socket → Assigned Lid (или Lid Prefab у Bonfire Test Keys)"); return; }
            AttachLid(sock, best);
        }

        private PotLidItem FindFreeLid(Cookware pot)
        {
            PotLidItem best = null; float bd = 9f * 9f;
            foreach (PotLidItem l in FindObjectsByType<PotLidItem>(FindObjectsSortMode.None))
            {
                if (l == null || l.IsAttached) continue;
                if (l.NetworkObject == null || !l.NetworkObject.IsSpawned) continue;      // не в сети — не годится
                if (l.NetworkObject.Owner.IsValid) continue;                             // в чьих-то руках
                float d = (l.transform.position - pot.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = l; }
            }
            if (best == null && _lidPrefab == null)
                foreach (PotLidItem l in FindObjectsByType<PotLidItem>(FindObjectsSortMode.None))
                    if (l != null && !l.IsAttached && l.NetworkObject != null && l.NetworkObject.IsSpawned && !l.NetworkObject.Owner.IsValid) { best = l; break; }
            return best;
        }

        private void AttachLid(PotLidSocket sock, PotLidItem best)
        {
            bool ok = sock.ServerAttach(best);
            if (ok)
            {
                sock.GetLidPose(out Vector3 lp, out Quaternion lr);
                best.transform.SetPositionAndRotation(lp, lr);
                Bounds b = default(Bounds); bool any = false;
                foreach (Renderer r in best.GetComponentsInChildren<Renderer>())
                { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                Say($"кастрюля накрыта крышкой «{best.name}» · точка {lp:F2} · размер крышки {(any ? b.size.ToString("F2") : "НЕТ МЕША")}");
            }
            else Say("не получилось накрыть");
        }

        private void Blow()
        {
            BonfirePit pit = Pit();
            if (pit == null) { Say("костёр не найден"); return; }
            if (!pit.IsBurning) { Say("сначала поджечь"); return; }
            pit.ServerAddAir(_air, Vector3.up);
            Say("раздули");
        }

        private void Say(string s)
        {
            _last = s;
            Debug.Log("[Тест костра] " + s, this);
        }

        // ───────────────────────── экран ─────────────────────────

        private GUIStyle _style;

        /// <summary>Сколько пикселей снизу занимает панель теста (аналитик тары рисуется над ней, а не поверх).</summary>
        public static float ReservedBottom { get; private set; }
        public static int ReservedFrame { get; private set; } = -10;

        private void OnGUI()
        {
            // F4 прячет и аналитик, и эту панель — одна кнопка на весь отладочный экран кухни
            if (!_hud || !CookwareAnalyzerHud.Visible) return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = true };

            string text;
            if (!InstanceFinder.IsServerStarted) text = "Тест костра: только у хоста";
            else
            {
                BonfirePit pit = Pit();
                Cookware pot = Pot();
                CookwareHeat heat = pot != null ? pot.GetComponent<CookwareHeat>() : null;
                Cookware pan = Pan();
                CookwareHeat panHeat = pan != null ? pan.GetComponent<CookwareHeat>() : null;
                BakeOven oven = Oven();
                text = $"ТЕСТ КУХНИ (F4 скрыть) · тара под прицелом: J +ведро воды · Shift+J до полной · K опустошить\n" +
                       $"Костёр: {_fillKey} доски · {_igniteKey} поджечь · {_airKey} раздуть · Кастрюля: {_potKey} на огонь/на место · {_lidKey} крышка · Сковорода: {_panKey} на огонь/на место\n" +
                       $"Духовка: {_ovenKey} на огонь/на место · {_ovenLidKey} открыть/закрыть · {_ovenHatchKey} люк · Хлеб: {_breadKey} (Shift — другой)" +
                       (oven != null ? $"  — {oven.TemperatureC:0}°C, {(oven.DoorOpen ? "открыта" : oven.VentOpen ? "люк открыт" : "закрыта")}, печётся {oven.BakingCount}, давление {oven.Pressure01 * 100f:0}%\n" : "  — духовки нет\n") +
                       (pit != null
                           ? $"Костёр: {pit.FireState}, досок {pit.Planks}, сила {Mathf.RoundToInt(pit.Intensity01 * 100f)}%, огонь {pit.PotTemperatureC:0}°C\n"
                           : "Костёр не найден\n") +
                       (heat != null ? $"Кастрюля «{pot.name}»: {heat.TemperatureC:0}°C{(heat.ServerBoiling ? " — КИПИТ" : "")} · огонь под ней {heat.FireC:0}°C · жидкости {heat.LiquidPortions} порц.\n" : "") +
                       (panHeat != null ? $"Сковорода «{pan.name}»: {panHeat.TemperatureC:0}°C · огонь под ней {panHeat.FireC:0}°C · {(pan.Def != null ? pan.Def.HeatProcess.ToString() : "—")}\n" : "") +
                       _last;
            }
            float w = 620f;
            float h = _style.CalcHeight(new GUIContent(text), w) + 6f;
            GUI.Box(new Rect(10, Screen.height - h - 10f, w, h), text, _style);
            ReservedBottom = h + 14f;
            ReservedFrame = Time.frameCount;
        }
    }
}
