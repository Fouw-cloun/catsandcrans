using System.Collections.Generic;
using CatCooks.Food;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система со
// своими глобальными FoodState и FoodTransition. Из глобального
// пространства компилятор взял бы их вместо новых (CatCooks.Food).
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ИЗМЕЛЬЧИТЕЛЬ. Станок: воронка сверху, выход сбоку, две кнопки.
    ///
    /// ═══ КАК РАБОТАЕТ ═══
    ///
    ///   1. Кидаешь продукт в воронку. Если таблица позволяет его «Тереть»
    ///      (у овощей — только ЧИЩЕНОЕ), он исчезает и встаёт в очередь.
    ///      Не позволяет (грязная, немытая, гнилая) — воронка выплёвывает его
    ///      обратно вверх. Сразу видно, что сначала надо почистить.
    ///   2. Зелёная кнопка — вкл, красная — выкл (F, наведясь на кнопку).
    ///      Кидать можно и в выключенный: продукт подождёт в воронке.
    ///   3. Включён и есть очередь — из выхода комками вылетает тёртое
    ///      (кучки FoodHeap). Подставил тару под выход — сыплется в неё.
    ///      Не подставил — на пол, кучкой (и она грязная).
    ///
    /// Сколько выходит: столько порций, сколько было в продукте. Целая картошка
    /// из 6 слайсов = 6 порций тёртой.
    ///
    /// ═══ ИЕРАРХИЯ ═══
    ///   Shredder (корень)          NetworkObject, ShredderStation, коллайдеры корпуса
    ///   ├─ Hopper                  BoxCollider IsTrigger внутри воронки + ShredderHopper
    ///   ├─ Outlet                  пустой, СИНЯЯ ось (Z) смотрит, куда вылетает
    ///   ├─ ButtonOn                коллайдер + ShredderButtonPart (Turn On = да,  Part Id = 0)
    ///   ├─ ButtonOff               коллайдер + ShredderButtonPart (Turn On = нет, Part Id = 1)
    ///   └─ Rotor (необязательно)   то, что крутится внутри
    /// </summary>
    public class ShredderStation : NetworkBehaviour
    {
        [Header("Что делает")]
        [Tooltip("Процесс, который применяется к продукту в воронке")]
        [SerializeField] private FoodProcess _process = FoodProcess.Grate;
        [Tooltip("Мясо (и всё, у чего есть «Молоть в фарш») — в фарш. Говядина по рецепту даёт «Говяжий фарш», " +
                 "остальное — тот же продукт в состоянии «Фарш» (свиной фарш — свинина-фарш)")]
        [SerializeField] private bool _allowMince = true;
        [Tooltip("Секунд на одну порцию")]
        [Min(0.05f)][SerializeField] private float _secondsPerPortion = 0.35f;
        [Tooltip("Порций в одном вылетающем комке. 1 — тонкая струйка, 3 — крупные шлепки.")]
        [Range(1, 6)][SerializeField] private int _portionsPerLump = 2;
        [Tooltip("Сколько порций помещается в воронку")]
        [Min(1)][SerializeField] private int _hopperCapacity = 30;

        [Header("Режим измельчения")]
        [Tooltip("Какую кучку выдаёт станок. Пока нет сменных насадок на модели — " +
                 "переключение вручную, прямо здесь.")]
        [SerializeField] private GrindMode _mode = GrindMode.Grated;

        [Header("Выход")]
        [SerializeField] private Transform _outlet;
        [Tooltip("Префаб кучки для каждого режима — тот же FoodHeap, только с разным " +
                 "пресетом на Food Heap Renderer (Sticks/Cubes/Grated/Puree).")]
        [SerializeField] private FoodHeap _sticksPrefab;
        [SerializeField] private FoodHeap _cubesPrefab;
        [SerializeField] private FoodHeap _gratedPrefab;
        [SerializeField] private FoodHeap _pureePrefab;

        private FoodHeap AnyPrefab => _gratedPrefab != null ? _gratedPrefab : _cubesPrefab != null ? _cubesPrefab
                                     : _pureePrefab != null ? _pureePrefab : _sticksPrefab;

        /// <summary>Префаб кучки для текущего режима.</summary>
        private FoodHeap ActiveHeapPrefab
        {
            get
            {
                switch (_mode)
                {
                    // Вид кучки всё равно выберется по продукту (FoodHeapLook) — префаб режима нужен
                    // только как «тело» кучки, поэтому при пустом слоте берём любой заполненный.
                    case GrindMode.Sticks: return _sticksPrefab != null ? _sticksPrefab : AnyPrefab;
                    case GrindMode.Cubes: return _cubesPrefab != null ? _cubesPrefab : AnyPrefab;
                    case GrindMode.Puree: return _pureePrefab != null ? _pureePrefab : AnyPrefab;
                    default: return _gratedPrefab != null ? _gratedPrefab : AnyPrefab;
                }
            }
        }
        [Tooltip("Скорость вылета комка вдоль синей оси Outlet, м/с")]
        [SerializeField] private float _ejectSpeed = 1.2f;
        [Tooltip("Случайный разброс направления")]
        [Range(0f, 0.5f)][SerializeField] private float _ejectSpread = 0.12f;

        public enum OutputMode : byte
        {
            [InspectorName("Рассчитать падение (надёжно)")] Direct = 0,
            [InspectorName("Физические комки (как раньше)")] Physical = 1,
        }

        [Header("Стыковка тары")]
        [Tooltip("Точка стыковки под выходом (CookwareDock). Есть подключённая тара — " +
                 "всё сыплется прямо в неё. Нет — кучка на полу в этой точке. " +
                 "Пусто — старый способ: расчёт по дуге струи.")]
        [SerializeField] private CookwareDock _dock;

        [Header("Куда падает (если нет стыковки)")]
        [Tooltip("Рассчитать: станок сам прослеживает дугу струи. Попала в тару — " +
                 "содержимое сразу в тару. Попала на пол — кучка ложится в точку падения " +
                 "и растёт там же. Никаких комков, застревающих на корпусе.")]
        [SerializeField] private OutputMode _outputMode = OutputMode.Direct;
        [Tooltip("Во что может упасть струя (пол, тара, столы)")]
        [SerializeField] private LayerMask _landMask = ~0;
        [Tooltip("Слой кучек — чтобы найти уже лежащую и досыпать в неё")]
        [SerializeField] private LayerMask _heapMask = ~0;
        [Tooltip("Досыпать в лежащую кучку ближе этого, м (сверх её радиуса)")]
        [SerializeField] private float _mergeSlack = 0.25f;

        [Header("Отказ")]
        [Tooltip("С какой силой воронка выплёвывает то, что не принимает")]
        [SerializeField] private float _rejectSpeed = 4.5f;

        [Header("Вид")]
        [SerializeField] private Transform _rotor;
        [SerializeField] private Vector3 _rotorAxis = Vector3.up;
        [SerializeField] private float _rotorSpeed = 900f;
        [Tooltip("Что трясётся при работе. Можно сколько угодно частей: корпус, " +
                 "воронка, крышка, ножки — каждая дрожит сама по себе.")]
        [SerializeField] private Transform[] _shakeTargets;
        [Tooltip("Амплитуда тряски, м")]
        [Range(0f, 0.05f)][SerializeField] private float _shake = 0.006f;
        [Tooltip("Дрожание поворотом, градусы")]
        [Range(0f, 5f)][SerializeField] private float _shakeAngle = 0.6f;
        [Tooltip("Вкл — каждая часть трясётся по-своему (дребезжит, как собранное из кусков). " +
                 "Выкл — все части вместе, как одно целое.")]
        [SerializeField] private bool _rattleIndependently = true;
        [SerializeField] private float _chipsPerSecond = 60f;
        [Tooltip("Размер всех эффектов. У тебя мир крупный (Scale 5) — начни с 3.")]
        [Range(0.5f, 8f)][SerializeField] private float _fxScale = 3f;
        [Tooltip("Стружки в секунду в струе")]
        [SerializeField] private float _strandsPerSecond = 140f;
        [Tooltip("Пыль/брызги сока в секунду у выхода")]
        [SerializeField] private float _dustPerSecond = 40f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        /// <summary>Какой вид кучки выдаёт станок. Переключается прямо в инспекторе —
        /// удобно, пока не готова модель с настоящими сменными насадками.</summary>
        public enum GrindMode : byte
        {
            [InspectorName("1. Мягкие бруски")] Sticks = 0,
            [InspectorName("2. Кубики")] Cubes = 1,
            [InspectorName("3. Труха полосками")] Grated = 2,
            [InspectorName("4. Пюре")] Puree = 3,
        }

        private struct Batch
        {
            public ushort Product;
            public FoodState State;
            public byte Cook;
            public int Portions;
        }

        // ───────── сеть ─────────
        private readonly SyncVar<bool> _on = new SyncVar<bool>(false);
        private readonly SyncVar<ushort> _queued = new SyncVar<ushort>(0);
        private readonly SyncVar<bool> _outputting = new SyncVar<bool>(false);
        // Что сейчас сыплется — чтобы стружка у всех была цвета продукта.
        private readonly SyncVar<ushort> _curProduct = new SyncVar<ushort>(0);
        private readonly SyncVar<byte> _curState = new SyncVar<byte>(0);

        // ───────── сервер ─────────
        private readonly List<Batch> _queue = new List<Batch>();
        private float _timer;

        // ───────── локально ─────────
        private float _spin;
        private Vector3[] _shakeBasePos;
        private Quaternion[] _shakeBaseRot;
        private ParticleSystem _chips;
        private ParticleSystem _strands;
        private ParticleSystem _dust;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly Collider[] _near = new Collider[16];
        private Color _lastColor = new Color(0.9f, 0.85f, 0.7f);

        public bool IsOn => _on.Value;
        public int QueuedPortions => _queued.Value;

        // ───────────────────────── КНОПКИ ─────────────────────────

        [Server]
        public void ServerSetOn(bool on)
        {
            if (_on.Value == on) return;
            _on.Value = on;
            if (_log) Debug.Log($"[Измельчитель] {(on ? "ВКЛ" : "ВЫКЛ")} · в воронке {_queued.Value} порц.", this);
        }

        // ───────────────────────── ВОРОНКА ─────────────────────────

        /// <summary>Продукт попал в воронку. Зовёт ShredderHopper на сервере.</summary>
        [Server]
        public void ServerAccept(FoodPiece piece)
        {
            if (piece == null || piece.IsHeld || piece.IsConsumed) return;

            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;

            if (!ResolveOutput(db, piece, out ushort outProduct, out FoodState outState, out string how))
            {
                Reject(piece, $"«{FoodStateInfo.NameOf(piece.State)}» не {FoodStateInfo.NameOf(_process)}" +
                              (_allowMince ? " и не мелется в фарш" : "") + " — сначала подготовь (почисти / разделай)");
                return;
            }
            // мясо в фарш: кусок туши — это много фарша (по размеру куска), а не одна порция
            int portions = piece.Count;
            if ((outState == FoodState.Minced && how.StartsWith("в фарш")) || how.StartsWith("соломкой")) portions = Mathf.Max(portions, MeatPortions(piece));
            portions = Mathf.Min(portions, _hopperCapacity);
            if (_queued.Value + portions > _hopperCapacity)
            {
                Reject(piece, "воронка полная");
                return;
            }
            if (!piece.ServerTryConsume()) return;

            _queue.Add(new Batch { Product = outProduct, State = outState, Cook = piece.Cook, Portions = portions });
            _queued.Value = (ushort)(_queued.Value + portions);
            if (_log) Debug.Log($"[Измельчитель] принял {piece.Def?.Title} ×{piece.Count} ({how}) → {portions} порц. · в воронке {_queued.Value}", this);

            piece.NetworkObject.Despawn();
        }

        /// <summary>
        /// Что выйдет из продукта: 1) рецепт «Молоть в фарш» (Говядина разделанная → Говяжий фарш);
        /// 2) правило таблицы основного процесса (картошка → тёртая); 3) правило «Молоть в фарш»
        /// (свинина разделанная → свинина-фарш).
        /// </summary>
        private bool ResolveOutput(FoodDatabase db, FoodPiece piece, out ushort product, out FoodState state, out string how)
        {
            product = piece.ProductId; state = piece.State; how = "";
            // Бефстроганов: ОТБИТАЯ кувалдой говядина — соломкой («Говядина соломкой»), а не в фарш
            if (piece.State == FoodState.Pounded && BeefStrips(db, piece, out ushort sp))
            { product = sp; state = FoodState.Pieces; how = "соломкой (отбитая говядина)"; return true; }
            if (_allowMince)
                foreach (FoodRecipeDef r in db.Recipes)
                {
                    if (r == null || r.Process != FoodProcess.Mince || r.ResultProduct == null || r.Needs.Count != 1) continue;
                    FoodNeed nd = r.Needs[0];
                    if (nd.Product == null || nd.Product.Id != piece.ProductId) continue;
                    if (nd.State != FoodState.None && nd.State != piece.State) continue;
                    product = r.ResultProduct.Id; state = r.ResultProduct.StartState; how = "в фарш по рецепту";
                    // фарш — всегда МАССА (берётся мастерком), даже если продукт лежит в группе «Полуфабрикат» (там старт — «Готово» = целый кусок)
                    if (!string.IsNullOrEmpty(r.ResultProduct.Title) && r.ResultProduct.Title.ToLowerInvariant().Contains("фарш")) state = FoodState.Minced;
                    return true;
                }
            // Пельмени: любой сырой кусок говядины / свинины (части новой туши — лопатка, шея, корейка…) — в общий фарш
            if (_allowMince && MeatMince(db, piece, out ushort mp))
            { product = mp; state = FoodState.Minced; how = "в фарш (кусок туши)"; return true; }
            if (db.Table.TryGet(piece.ProductId, piece.State, _process, out FoodTransition t))
            { state = t.To; how = FoodStateInfo.NameOf(_process); return true; }
            if (_allowMince && db.Table.TryGet(piece.ProductId, piece.State, FoodProcess.Mince, out FoodTransition m))
            { state = m.To; how = "в фарш"; return true; }
            return false;
        }

        /// <summary>Отбитый кусок говядины → «Говядина соломкой» (если такой продукт есть — меню Бефстроганов → 1).</summary>
        private static bool BeefStrips(FoodDatabase db, FoodPiece piece, out ushort product)
        {
            product = 0;
            ProductDef p = piece.Def;
            if (p == null || string.IsNullOrEmpty(p.Title)) return false;
            string t = p.Title.Trim().ToLowerInvariant();
            bool beef = t.StartsWith("говядин") || t.StartsWith("говяж") || t.Contains("телят");
            MeatCutSettings st = MeatCutSettings.Instance;
            if (!beef && st != null && System.Array.IndexOf(st.BeefKinds, p) >= 0) beef = true;
            if (!beef || t.Contains("фарш") || t.Contains("соломк")) return false;
            for (int id = 1; id < 4096; id++)
            {
                ProductDef d = db.Get((ushort)id);
                if (d != null && !string.IsNullOrEmpty(d.Title) && d.Title.Trim().ToLowerInvariant() == "говядина соломкой") { product = d.Id; return true; }
            }
            return false;
        }

        /// <summary>
        /// Сырой кусок говядины / свинины → «Говяжий фарш» / «Свиной фарш» (если такие продукты есть).
        /// Мясо узнаём по таблице частей туши (MeatCutSettings) или по названию («говяж…», «говядин…», «свин…»).
        /// </summary>
        private static bool MeatMince(FoodDatabase db, FoodPiece piece, out ushort product)
        {
            product = 0;
            ProductDef p = piece.Def;
            if (p == null || string.IsNullOrEmpty(p.Title)) return false;
            string t = p.Title.Trim().ToLowerInvariant();
            if (t.Contains("фарш") || t.Contains("кост") || t.Contains("сало")) return false;   // сало — отдельный продукт (борщ, копчение), не фарш
            FoodState s = piece.State;
            if (s != FoodState.Raw && s != FoodState.Washed && s != FoodState.Butchered && s != FoodState.Pieces && s != FoodState.Pounded) return false;
            int species = -1;                                       // 0 — свинина, 1 — говядина
            MeatCutSettings st = MeatCutSettings.Instance;
            if (st != null)
            {
                if (System.Array.IndexOf(st.BeefKinds, p) >= 0) species = 1;
                else if (System.Array.IndexOf(st.PorkKinds, p) >= 0) species = 0;
            }
            if (species < 0)
            {
                if (t.StartsWith("говяж") || t.StartsWith("говядин")) species = 1;
                else if (t.StartsWith("свин")) species = 0;
            }
            if (species < 0) return false;
            string want = species == 1 ? "говяжий фарш" : "свиной фарш";
            for (int id = 1; id < 4096; id++)
            {
                ProductDef d = db.Get((ushort)id);
                if (d != null && !string.IsNullOrEmpty(d.Title) && d.Title.Trim().ToLowerInvariant() == want) { product = d.Id; return true; }
            }
            return false;
        }

        /// <summary>Сколько порций фарша даёт кусок мяса — по объёму куска (габарит мешей), 4–30. Порция ~ 10 л при масштабе мира 4.</summary>
        private static int MeatPortions(FoodPiece piece)
        {
            Renderer[] rs = piece.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 6;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            float S = Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
            float vol = b.size.x * b.size.y * b.size.z * 0.6f;                    // кусок не коробка — ~60% габарита
            return Mathf.Clamp(Mathf.RoundToInt(vol / (0.01f * S * S * S)), 4, 30);
        }

        private void Reject(FoodPiece piece, string why)
        {
            Rigidbody rb = piece.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                Vector3 up = transform.up + Random.insideUnitSphere * 0.35f;
                rb.linearVelocity = up.normalized * _rejectSpeed;
                rb.angularVelocity = Random.insideUnitSphere * 8f;
            }
            if (_log) Debug.Log($"[Измельчитель] выплюнул {piece.Def?.Title}: {why}", piece);
        }

        // ───────────────────────── КАДР ─────────────────────────

        private void Update()
        {
            if (base.IsServerInitialized) ServerTick();
            UpdateVisual();
        }

        private void ServerTick()
        {
            bool working = _on.Value && _queue.Count > 0;
            if (_outputting.Value != working) _outputting.Value = working;
            if (!working) { _timer = 0f; return; }

            _timer += Time.deltaTime;
            float lumpTime = _secondsPerPortion * _portionsPerLump;
            if (_timer < lumpTime) return;
            _timer -= lumpTime;

            Batch b = _queue[0];
            int n = Mathf.Min(_portionsPerLump, b.Portions);
            b.Portions -= n;
            if (b.Portions <= 0) _queue.RemoveAt(0); else _queue[0] = b;
            _queued.Value = (ushort)Mathf.Max(0, _queued.Value - n);

            if (_curProduct.Value != b.Product) _curProduct.Value = b.Product;
            if (_curState.Value != (byte)b.State) _curState.Value = (byte)b.State;
            SpawnLump(b.Product, b.State, n, b.Cook);
        }

        private void SpawnLump(ushort product, FoodState state, int portions, byte cook)
        {
            if (_outlet == null)
            {
                Debug.LogError("[Измельчитель] не назначен Outlet", this);
                return;
            }

            if (_outputMode == OutputMode.Physical)
            {
                FoodHeap prefab = ActiveHeapPrefab;
                if (prefab == null) { Debug.LogError($"[Измельчитель] для режима «{_mode}» не назначен префаб кучки", this); return; }
                FoodHeap heap = Instantiate(prefab, _outlet.position, _outlet.rotation);
                heap.ServerPreset(product, state, portions, cook, 100);
                base.Spawn(heap.gameObject);
                Vector3 dir = (_outlet.forward + Random.insideUnitSphere * _ejectSpread).normalized;
                heap.ServerLaunch(dir * _ejectSpeed);
                return;
            }

            // ── есть точка стыковки: тара там или пол ──
            if (_dock != null)
            {
                Cookware docked = _dock.Current;
                int rest = portions;
                if (docked != null)
                {
                    int added = docked.ServerAddContent(product, state, rest, cook);
                    rest -= added;
                    if (added > 0) RpcPour(docked.NetworkObject, docked.MouthPoint);
                    if (rest > 0 && _log) Debug.Log($"[Измельчитель] «{docked.Def?.Title}» полна — {rest} порц. мимо", this);
                }
                if (rest > 0)
                {
                    // Мимо тары (её нет или полна) — на пол рядом с точкой стыковки.
                    Vector3 floor = GroundBelow(_dock.transform.position + Vector3.up * 0.5f,
                                                docked != null ? docked.transform : null);
                    FloorAdd(product, state, rest, cook, floor);
                }
                return;
            }

            // ── рассчитать, куда падает струя ──
            if (!TraceLanding(out Cookware cw, out Vector3 point, out bool hitSomething))
            {
                if (_log) Debug.Log("[Измельчитель] струя уходит в пустоту — ничего не выдано", this);
                return;
            }

            int left = portions;
            if (cw != null)
            {
                int added = cw.ServerAddContent(product, state, left, cook);
                left -= added;
                if (added > 0) RpcPour(cw.NetworkObject, point);
                if (left > 0 && _log) Debug.Log($"[Измельчитель] «{cw.Def?.Title}» полна — {left} порц. мимо", this);
                if (left > 0) point = GroundBelow(point + Vector3.up * 0.05f, cw.transform);
            }

            if (left > 0) FloorAdd(product, state, left, cook, point);
        }

        /// <summary>
        /// Проследить дугу струи от выхода: v = синяя ось × Eject Speed, гравитация.
        /// Первое, во что она упирается (кроме самого станка): тара — вернуть её,
        /// пол/стол — точку. Стена — дальше вниз от точки удара.
        /// </summary>
        private bool TraceLanding(out Cookware cw, out Vector3 point, out bool hit)
        {
            cw = null; hit = false;
            Vector3 pos = _outlet.position;
            Vector3 vel = _outlet.forward * _ejectSpeed;
            const float dt = 0.02f;

            for (int step = 0; step < 150; step++)
            {
                Vector3 next = pos + vel * dt + 0.5f * Physics.gravity * dt * dt;
                vel += Physics.gravity * dt;
                Vector3 seg = next - pos;
                float len = seg.magnitude;
                if (len > 1e-5f && NearestHit(pos, seg / len, len, out RaycastHit h))
                {
                    hit = true;
                    cw = h.collider.GetComponentInParent<Cookware>();
                    if (cw != null) { point = h.point; return true; }
                    if (h.normal.y > 0.5f) { point = h.point; return true; }
                    // Стена или склон — падаем вниз от точки удара.
                    point = GroundBelow(h.point + h.normal * 0.02f, null);
                    cw = CookwareAt(point);
                    return true;
                }
                pos = next;
            }

            // Ни во что не попали за 3 секунды полёта — вниз от последней точки.
            point = GroundBelow(pos, null);
            cw = CookwareAt(point);
            return point != pos;
        }

        private bool NearestHit(Vector3 from, Vector3 dir, float len, out RaycastHit best)
        {
            int n = Physics.RaycastNonAlloc(from, dir, _hits, len, _landMask, QueryTriggerInteraction.Collide);
            best = default;
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform)) continue;                // сам станок
                if (h.collider.isTrigger && h.collider.GetComponentInParent<Cookware>() == null) continue;
                if (h.collider.GetComponentInParent<FoodHeap>() != null) continue; // кучки не преграда
                if (h.distance >= bestD) continue;
                bestD = h.distance; best = h; found = true;
            }
            return found;
        }

        /// <summary>Пол под точкой (игнорируя станок и, если задано, тару).</summary>
        private Vector3 GroundBelow(Vector3 from, Transform ignore)
        {
            int n = Physics.RaycastNonAlloc(from + Vector3.up * 0.02f, Vector3.down, _hits, 20f, _landMask,
                                            QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue;
            Vector3 p = from;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform)) continue;
                if (ignore != null && h.transform.IsChildOf(ignore)) continue;
                if (h.collider.GetComponentInParent<FoodHeap>() != null) continue;
                if (h.distance >= bestD) continue;
                bestD = h.distance; p = h.point;
            }
            return p;
        }

        private Cookware CookwareAt(Vector3 point)
        {
            int n = Physics.OverlapSphereNonAlloc(point + Vector3.up * 0.05f, 0.1f, _near, _landMask,
                                                  QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Cookware c = _near[i].GetComponentInParent<Cookware>();
                if (c != null) return c;
            }
            return null;
        }

        /// <summary>На пол: досыпать в лежащую рядом такую же кучку или положить новую.</summary>
        private void FloorAdd(ushort product, FoodState state, int portions, byte cook, Vector3 point)
        {
            int n = Physics.OverlapSphereNonAlloc(point, 1.5f, _near, _heapMask, QueryTriggerInteraction.Collide);
            FoodHeap best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                FoodHeap h = _near[i].GetComponentInParent<FoodHeap>();
                if (h == null || !h.IsSettled || h.ProductId != product || h.State != state) continue;
                float d = Vector3.Distance(h.transform.position, point);
                if (d > h.VisualRadius + _mergeSlack || d >= bestD) continue;
                best = h; bestD = d;
            }

            // С пола — грязное (учёт грязи потом, шкала уже есть).
            const byte floorClean = 50;
            if (best != null)
            {
                best.ServerAdd(portions, cook, floorClean);
                return;
            }

            FoodHeap activePrefab = ActiveHeapPrefab;
            if (activePrefab == null) { Debug.LogError($"[Измельчитель] для режима «{_mode}» не назначен префаб кучки", this); return; }
            FoodHeap heap = Instantiate(activePrefab, point, Quaternion.identity);
            heap.ServerPreset(product, state, portions, cook, floorClean, true);
            base.Spawn(heap.gameObject);
            if (_log) Debug.Log($"[Измельчитель] кучка на полу: {portions} порц.", heap);
        }

        /// <summary>Всем: куда в таре сыплется — горка растёт от этой точки.</summary>
        [ObserversRpc]
        private void RpcPour(NetworkObject cookware, Vector3 point)
        {
            if (cookware == null) return;
            CookwareHeapVisual v = cookware.GetComponentInChildren<CookwareHeapVisual>();
            if (v != null) v.SetPourPoint(point);
        }

        // ───────────────────────── ВИД (у всех) ─────────────────────────

        private void UpdateVisual()
        {
            float target = _on.Value ? 1f : 0f;
            _spin = Mathf.MoveTowards(_spin, target, Time.deltaTime * (_on.Value ? 3f : 1f));

            if (_rotor != null && _spin > 0.001f)
                _rotor.Rotate(_rotorAxis.normalized * (_rotorSpeed * _spin * Time.deltaTime), Space.Self);

            UpdateShake();

            if (_outlet == null) return;
            bool on = _outputting.Value;
            if (on) RefreshChipColor();

            if (on && _strands == null && _strandsPerSecond > 0f)
                _strands = CCRuntimeParticles.CreateStream(transform, CCRuntimeParticles.Look.Strands.Scaled(_fxScale));
            if (on && _dust == null && _dustPerSecond > 0f)
                _dust = CCRuntimeParticles.CreateStream(transform, CCRuntimeParticles.Look.Dust.Scaled(_fxScale));
            if (on && _chips == null && _chipsPerSecond > 0f)
                _chips = CCRuntimeParticles.CreateStream(transform, CCRuntimeParticles.Look.Chips.Scaled(_fxScale));

            // Струя стружки летит по той же дуге, что и расчёт падения:
            // скорость вылета частиц подогнана под Eject Speed.
            CCRuntimeParticles.SetStream(_strands, on, _outlet.position, _outlet.forward,
                                         _lastColor * 0.95f, _lastColor * 1.1f, _strandsPerSecond, _ejectSpeed);
            CCRuntimeParticles.SetStream(_dust, on, _outlet.position, _outlet.forward + Vector3.up * 0.3f,
                                         _lastColor * 1.1f, Color.white * 0.95f, _dustPerSecond);
            CCRuntimeParticles.SetStream(_chips, on, _outlet.position, _outlet.forward,
                                         _lastColor * 0.9f, _lastColor * 1.05f, _chipsPerSecond * 0.5f, _ejectSpeed);
        }

        private void UpdateShake()
        {
            if (_shakeTargets == null || _shakeTargets.Length == 0) return;

            // Исходные позы частей запоминаются один раз — тряска всегда вокруг них.
            if (_shakeBasePos == null || _shakeBasePos.Length != _shakeTargets.Length)
            {
                _shakeBasePos = new Vector3[_shakeTargets.Length];
                _shakeBaseRot = new Quaternion[_shakeTargets.Length];
                for (int i = 0; i < _shakeTargets.Length; i++)
                {
                    if (_shakeTargets[i] == null) continue;
                    _shakeBasePos[i] = _shakeTargets[i].localPosition;
                    _shakeBaseRot[i] = _shakeTargets[i].localRotation;
                }
            }

            float k = _spin * (_outputting.Value ? 1.6f : 1f);
            float amp = _shake * k;
            float ang = _shakeAngle * k;

            Vector3 sharedPos = Random.insideUnitSphere * amp;
            Vector3 sharedRot = Random.insideUnitSphere * ang;

            for (int i = 0; i < _shakeTargets.Length; i++)
            {
                Transform t = _shakeTargets[i];
                if (t == null) continue;
                if (k <= 0.0001f)
                {
                    t.localPosition = _shakeBasePos[i];
                    t.localRotation = _shakeBaseRot[i];
                    continue;
                }
                Vector3 p = _rattleIndependently ? Random.insideUnitSphere * amp : sharedPos;
                Vector3 r = _rattleIndependently ? Random.insideUnitSphere * ang : sharedRot;
                t.localPosition = _shakeBasePos[i] + p;
                t.localRotation = _shakeBaseRot[i] * Quaternion.Euler(r);
            }
        }

private void RefreshChipColor()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || _curProduct.Value == 0) return;
            _lastColor = FoodColors.Of(db.Get(_curProduct.Value), (FoodState)_curState.Value);
        }
    }
}
