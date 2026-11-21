using System.Collections;
using System.Collections.Generic;
using System.Text;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Supply
{
    /// <summary>
    /// v24.0: ДОСТАВКА ПРОДУКТОВ НА ЭТАЖ. v24.2: пресеты блюд, End — тестовый пресет. v24.3: ОДИН УЖИН — горячее + десерт + напиток на каждого кота (Cats, по числу игроков).
    ///
    /// При старте уровня (сервер): случайно выбираются ДВА разных блюда из SupplyMenuBook (без десертов) —
    /// одно на ОБЕД, другое на УЖИН. Их сырьё складывается (штучное — сумма, мешки и банки — больше из двух) и
    /// приезжает к игроку: рядом с ним на том же этаже ставится большая площадка, на ней —
    ///   • закрытые ЯЩИКИ по типам (мясо, овощи, фрукты, специи, морепродукты, молочка, бакалея): F — открыть;
    ///   • крупное прямо на площадке: мешки, бидоны, вёдра с жидким, туши.
    /// Префабы продуктов — из каталога (SupplyCatalog в Resources); нет префаба — продукт едет в ведре.
    ///
    /// Хост: Home — новая доставка с новым меню к тебе; Shift+Home — убрать все доставки (площадки и закрытые ящики).
    /// Меню видят все: плашка «Обед: … · Ужин: …» сверху.
    ///
    /// ═══ В СЦЕНЕ ═══ объект с NetworkObject + этот компонент. Ставит меню CatCooks → Доставка → 3.
    /// Потом площадку будет приносить кран — для этого есть ServerDeliverAt(точка, поворот).
    /// </summary>
    public sealed class SupplyDelivery : NetworkBehaviour
    {
        [Header("Что и откуда")]
        [Tooltip("Пусто — Resources/SupplyCatalog")]
        [SerializeField] private SupplyCatalog _catalog;
        [Tooltip("v24.2: пресеты блюд — из них (В меню, не десерт) выбираются обед и ужин, количество — Portions пресета. " +
                 "Пусто — старое меню из кода (SupplyMenuBook)")]
        [SerializeField] private List<SupplyPreset> _presets = new List<SupplyPreset>();
        [Tooltip("Только эти блюда (названия как в меню). Пусто — все основные")]
        [SerializeField] private string[] _onlyDishes = new string[0];
        [Tooltip("v24.3: ужин дня руками — названия пресетов (пусто — случайно из пресетов этого курса)")]
        [SerializeField] private string _forceHot = "", _forceDessert = "", _forceDrink = "";
        [Header("Сколько котов")]
        [Tooltip("Котов в бригаде (порций каждой позиции ужина)")]
        [Min(1)][SerializeField] private int _cats = 40;
        [Tooltip("Масштаб по числу игроков: 1 → 12, 2 → 20, 3 → 30, 4+ → Cats")]
        [SerializeField] private bool _scaleByPlayers = true;
        [Tooltip("Обед и ужин руками (пусто — случайно)")]

        [Header("Когда и куда")]
        [SerializeField] private bool _deliverOnStart = true;
        [Tooltip("Через сколько секунд после появления первого игрока")]
        [SerializeField] private float _startDelay = 3f;
        [Tooltip("Точка доставки (пусто — рядом с игроком на его этаже)")]
        [SerializeField] private Transform _deliveryPoint;
        [Tooltip("На каком расстоянии от игрока искать место под площадку, м")]
        [SerializeField] private float[] _distances = { 5.5f, 7.5f, 4f, 10f };

        [Header("Ящики")]
        [Tooltip("Для тестов: открытые ящики можно брать в руки")]
        [SerializeField] private bool _cratesCarryable = true;
        [Tooltip("Сколько предметов максимум в одном ящике")]
        [Range(4, 200)][SerializeField] private int _maxPerCrate = 120;
        [SerializeField] private bool _log = true;

        [Header("Тест (хост)")]
        [SerializeField] private KeyCode _testKey = KeyCode.Home;
        [Tooltip("End — привезти этот пресет (его Portions) к себе. Десерты тоже можно")]
        [SerializeField] private SupplyPreset _testPreset;
        [SerializeField] private KeyCode _testPresetKey = KeyCode.End;

        // v24.3: ОДИН ПРИЁМ ПИЩИ — вечерний ужин: горячее + десерт + напиток (на каждого кота)
        private readonly SyncVar<string> _hotTitle = new SyncVar<string>("");
        private readonly SyncVar<string> _dessertTitle = new SyncVar<string>("");
        private readonly SyncVar<string> _drinkTitle = new SyncVar<string>("");
        private readonly SyncVar<int> _catsNet = new SyncVar<int>(0);
        private readonly SyncVar<int> _menuSerial = new SyncVar<int>(0);
        private SupplyPreset _hot, _dessert, _drink;                                // сервер
        private int _bookHot = -1;                                                  // сервер: блюдо старого меню, если пресетов нет

        private readonly List<NetworkObject> _spawned = new List<NetworkObject>();
        private float _bannerUntil;

        public static SupplyDelivery Instance { get; private set; }
        public string HotTitle => _hotTitle.Value;
        public string DessertTitle => _dessertTitle.Value;
        public string DrinkTitle => _drinkTitle.Value;
        public int Cats => _catsNet.Value;
        public SupplyPreset Hot => _hot;
        public SupplyPreset Dessert => _dessert;
        public SupplyPreset Drink => _drink;

        /// <summary>Пресеты курса, из которых собирается меню (В меню, нужный курс, есть строки).</summary>
        private List<SupplyPreset> CoursePresets(SupplyCourse c)
        {
            var res = new List<SupplyPreset>();
            if (_presets != null)
                foreach (SupplyPreset p in _presets)
                    if (p != null && p.InMenu && p.Course == c && p.Lines.Count > 0 &&
                        (_onlyDishes == null || _onlyDishes.Length == 0 || System.Array.IndexOf(_onlyDishes, p.Title) >= 0)) res.Add(p);
            return res;
        }

        /// <summary>Сколько котов кормим: по числу игроков или как задано.</summary>
        public int CatsToFeed()
        {
            if (!_scaleByPlayers) return _cats;
            int n = FindObjectsByType<PlayerInteractor>(FindObjectsSortMode.None).Length;
            int c = n <= 1 ? 12 : n == 2 ? 20 : n == 3 ? 30 : _cats;
            return Mathf.Min(c, _cats);
        }

        private SupplyCatalog Catalog => _catalog != null ? _catalog : SupplyCatalog.Instance;

        private void Awake() => Instance = this;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _menuSerial.OnChange += OnMenu;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _menuSerial.OnChange -= OnMenu;
        }

        private void OnMenu(int a, int b, bool asServer) => _bannerUntil = Time.time + 14f;

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_deliverOnStart) StartCoroutine(AutoDeliver());
        }

        private IEnumerator AutoDeliver()
        {
            float until = Time.time + 60f;
            PlayerInteractor p = null;
            while (Time.time < until)
            {
                if (FoodDatabase.Instance != null && (p = AnyPlayer()) != null) break;
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(_startDelay);
            if (p == null) p = AnyPlayer();
            PickMenu();
            ServerDeliverNear(p);
        }

        private static PlayerInteractor AnyPlayer()
        {
            foreach (PlayerInteractor pi in FindObjectsByType<PlayerInteractor>(FindObjectsSortMode.None))
                if (pi != null && pi.isActiveAndEnabled) return pi;
            return null;
        }

        // ───────────────────────── меню ─────────────────────────

        /// <summary>Сервер: ужин дня — по одному пресету на курс (горячее, десерт, напиток): заданный руками или случайный.</summary>
        [Server]
        public void PickMenu()
        {
            SupplyPreset Pick(SupplyCourse c, string force, SupplyPreset prev)
            {
                List<SupplyPreset> pool = CoursePresets(c);
                if (pool.Count == 0) return null;
                if (!string.IsNullOrEmpty(force)) { SupplyPreset f = pool.Find(x => x.Title == force); if (f != null) return f; }
                if (pool.Count > 1 && prev != null) pool.Remove(prev);                   // новое меню — не то же, что было
                return pool[Random.Range(0, pool.Count)];
            }
            _hot = Pick(SupplyCourse.Hot, _forceHot, _hot);
            _dessert = Pick(SupplyCourse.Dessert, _forceDessert, _dessert);
            _drink = Pick(SupplyCourse.Drink, _forceDrink, _drink);
            _bookHot = -1;
            if (_hot == null && _dessert == null && _drink == null && SupplyMenuBook.Dishes.Length > 0)
                _bookHot = Random.Range(0, SupplyMenuBook.Dishes.Length);             // пресетов нет — старое меню из кода
            _hotTitle.Value = _hot != null ? _hot.Title : _bookHot >= 0 ? SupplyMenuBook.Dishes[_bookHot].Title : "";
            _dessertTitle.Value = _dessert != null ? _dessert.Title : "";
            _drinkTitle.Value = _drink != null ? _drink.Title : "";
            _catsNet.Value = CatsToFeed();
            _menuSerial.Value++;                                                  // плашка показывается и при том же меню
            if (_log) Debug.Log($"[Доставка] УЖИН на {_catsNet.Value} котов: горячее — «{_hotTitle.Value}», десерт — «{_dessertTitle.Value}», напиток — «{_drinkTitle.Value}»", this);
        }

        /// <summary>Сервер: ужин дня задать прямо (уровень). null — курса нет.</summary>
        [Server]
        public void ServerSetMenu(SupplyPreset hot, SupplyPreset dessert, SupplyPreset drink, int cats)
        {
            _hot = hot; _dessert = dessert; _drink = drink; _bookHot = -1;
            _hotTitle.Value = hot != null ? hot.Title : "";
            _dessertTitle.Value = dessert != null ? dessert.Title : "";
            _drinkTitle.Value = drink != null ? drink.Title : "";
            _catsNet.Value = Mathf.Max(1, cats);
            _menuSerial.Value++;
        }

        // ───────────────────────── доставка ─────────────────────────

        /// <summary>Сервер: привезти сырьё текущего меню к игроку (на его этаж).</summary>
        [Server]
        public void ServerDeliverNear(PlayerInteractor player)
        {
            SpotNear(player, out Vector3 pos, out Quaternion rot);
            ServerDeliverAt(pos, rot);
        }

        /// <summary>Сервер: привезти к игроку поставку по пресетам (пресет, сколько порций) — для теста и «заказа».</summary>
        [Server]
        public void ServerDeliverPresets(IList<(SupplyPreset preset, int portions)> order, PlayerInteractor player)
        {
            if (player == null) player = AnyPlayer();
            SpotNear(player, out Vector3 pos, out Quaternion rot);
            SupplyPlan plan = SupplyPlanner.Plan(order, Catalog);
            if (_log) Debug.Log("[Доставка] ПРЕСЕТЫ — " + plan.Report(), this);
            ServerDeliverItems(plan.Items, plan.Tag, null, pos, rot);
        }

        private void SpotNear(PlayerInteractor player, out Vector3 pos, out Quaternion rot)
        {
            if (_deliveryPoint != null) { pos = _deliveryPoint.position; rot = Quaternion.Euler(0f, _deliveryPoint.eulerAngles.y, 0f); }
            else if (player != null)
            {
                Vector3 fwd = player.AimOrigin != null ? player.AimOrigin.forward : player.transform.forward;
                FindSpot(player.transform.position, fwd, out pos, out rot);
            }
            else { pos = transform.position; rot = transform.rotation; }
        }

        /// <summary>Сервер: площадка с поставкой в точке (низ площадки — на pos). Для крана — тот же вызов.</summary>
        [Server]
        public void ServerDeliverAt(Vector3 pos, Quaternion rot)
        {
            SupplyCatalog cat = Catalog;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) { Debug.LogError("[Доставка] нет базы продуктов (FoodDatabase) — доставка отменена", this); return; }
            if (cat == null) Debug.LogWarning("[Доставка] нет каталога (Resources/SupplyCatalog) — меню CatCooks → Доставка → 2. Везу то, что найду сам", this);

            // 1. сырьё обоих блюд: по пресетам (расчёт порций) или по старому меню из кода
            var items = new List<SupplyItem>();
            var missing = new List<string>();
            string tag;
            int cats = Mathf.Max(1, _catsNet.Value > 0 ? _catsNet.Value : CatsToFeed());
            if (_hot != null || _dessert != null || _drink != null)
            {
                var order = new List<(SupplyPreset, int)>();
                foreach (SupplyPreset p in new[] { _hot, _dessert, _drink }) if (p != null) order.Add((p, cats));
                SupplyPlan plan = SupplyPlanner.Plan(order, cat);
                items.AddRange(plan.Items);
                foreach (SupplyPlanLine pl in plan.Lines) if (pl.Problem != null) missing.Add($"{pl.Product?.Title} ({pl.Problem})");
                tag = plan.Tag;
                if (_log) Debug.Log("[Доставка] РАСЧЁТ — " + plan.Report(), this);
            }
            else
            {
                SupplyDish d = _bookHot >= 0 && _bookHot < SupplyMenuBook.Dishes.Length ? SupplyMenuBook.Dishes[_bookHot] : null;
                var lines = SupplyMenuBook.Merge(d, null, t => { ProductDef p = MacaronTitles.Find(db, t); return cat != null ? cat.IsSource(p) : SupplyCatalog.FindPiecePrefab(p) == null; });
                foreach (SupplyLine line in lines) Resolve(line, db, cat, items, missing);
                tag = _hotTitle.Value;
            }
            ServerDeliverItems(items, tag, missing, pos, rot);
        }

        /// <summary>Один ящик поставки: тип (категория), что внутри и какого он размера.</summary>
        public sealed class CratePack
        {
            public SupplyCategory cat;
            public List<SupplyItem> items = new List<SupplyItem>();
            public SupplyCatalog.CrateKind kind;
        }

        /// <summary>
        /// v24.5: УПАКОВКА ПО ЯЩИКАМ РАЗНЫХ РАЗМЕРОВ. Внутри категории (мясо, молочка…) крупное — первым:
        ///   • влезает всё оставшееся в какой-то ящик — берётся самый маленький такой;
        ///   • не влезает — набивается самый большой ящик до отказа (раскладка Layout: ряды, слои) и дальше по кругу.
        /// Не влезает даже в пустой большой — на площадку (туша). Общий для доставки и предпросмотра в редакторе.
        /// </summary>
        public static List<CratePack> PackCrates(List<SupplyItem> items, int maxPerCrate, List<SupplyItem> pallet, List<SupplyCatalog.CrateKind> kinds = null)
        {
            if (kinds == null || kinds.Count == 0)
                kinds = SupplyCatalog.Instance != null ? SupplyCatalog.Instance.CrateKinds()
                      : new List<SupplyCatalog.CrateKind> { new SupplyCatalog.CrateKind { Inner = SupplyCrate.DefaultInner, Outer = SupplyCrate.DefaultInner } };
            SupplyCatalog.CrateKind big = kinds[kinds.Count - 1];
            var byCat = new Dictionary<SupplyCategory, List<SupplyItem>>();
            foreach (SupplyItem it in items)
            {
                if (it.Pallet || !SupplyCrate.FitsEmpty(it.Size, big.Inner)) { pallet?.Add(it); continue; }
                if (!byCat.TryGetValue(it.Category, out List<SupplyItem> l)) byCat[it.Category] = l = new List<SupplyItem>();
                l.Add(it);
            }
            var crates = new List<CratePack>();
            var sizes = new List<Vector3>();
            for (int c = 0; c < SupplyCategories.Count; c++)
            {
                if (!byCat.TryGetValue((SupplyCategory)c, out List<SupplyItem> rest)) continue;
                // крупное вниз и первым; одинаковое рядом
                rest.Sort((x, y) =>
                {
                    int k = (y.Size.x * y.Size.z).CompareTo(x.Size.x * x.Size.z);
                    return k != 0 ? k : string.CompareOrdinal(x.Product != null ? x.Product.Title : "", y.Product != null ? y.Product.Title : "");
                });
                while (rest.Count > 0)
                {
                    var pack = new CratePack { cat = (SupplyCategory)c };
                    // 1) всё оставшееся — в самый маленький подходящий
                    sizes.Clear(); foreach (SupplyItem it in rest) sizes.Add(it.Size);
                    bool done = false;
                    if (rest.Count <= maxPerCrate)
                        foreach (SupplyCatalog.CrateKind k in kinds)
                            if (SupplyCrate.Layout(sizes, k.Inner, null, null)) { pack.kind = k; pack.items.AddRange(rest); rest.Clear(); done = true; break; }
                    // 2) иначе — большой до отказа (пробуем каждый предмет: мелочь доберёт щели в конце)
                    if (!done)
                    {
                        pack.kind = big;
                        sizes.Clear();
                        for (int i = 0; i < rest.Count && pack.items.Count < maxPerCrate; i++)
                        {
                            sizes.Add(rest[i].Size);
                            if (SupplyCrate.Layout(sizes, big.Inner, null, null)) { pack.items.Add(rest[i]); rest.RemoveAt(i); i--; }
                            else sizes.RemoveAt(sizes.Count - 1);
                        }
                        if (pack.items.Count == 0) { pallet?.Add(rest[0]); rest.RemoveAt(0); continue; }   // страховка
                    }
                    crates.Add(pack);
                }
            }
            return crates;
        }

        /// <summary>Сервер: площадка + ящики (маркировка tag) + крупное на площадке.</summary>
        [Server]
        public void ServerDeliverItems(List<SupplyItem> items, string tag, List<string> missing, Vector3 pos, Quaternion rot)
        {
            SupplyCatalog cat = Catalog;

            // v24.9: сначала раскладка ВСЕГО (ящики + крупное) на настиле, потом площадка нужного размера —
            // всё стоит на площадке (её потом поднимает кран), ничего не ставится друг на друга.
            var pallet = new List<SupplyItem>();
            List<SupplyCatalog.CrateKind> kinds = cat != null ? cat.CrateKinds() : null;
            List<CratePack> crates = PackCrates(items, _maxPerCrate, pallet, kinds);
            Vector2 baseDeck = new Vector2(10f, 7f);
            SupplyPlatform platPf = cat != null && cat.PlatformPrefab != null ? cat.PlatformPrefab.GetComponent<SupplyPlatform>() : null;
            if (platPf != null) baseDeck = platPf.DeckSize;
            List<DeckSlot> slots = LayoutDeck(crates, pallet, baseDeck.y - 0.4f, out float needW, out float needD);
            float sx = Mathf.Max(1f, (needW + 0.4f) / baseDeck.x), sz = Mathf.Max(1f, (needD + 0.4f) / baseDeck.y);
            float deckW = baseDeck.x * sx, deckD = baseDeck.y * sz;

            // 2. площадка (растянута под груз)
            SupplyPlatform plat = null;
            if (cat != null && cat.PlatformPrefab != null)
            {
                NetworkObject pn = Instantiate(cat.PlatformPrefab, pos, rot);
                pn.transform.localScale = Vector3.Scale(pn.transform.localScale, new Vector3(sx, 1f, sz));
                FishNet.InstanceFinder.ServerManager.Spawn(pn.gameObject);
                _spawned.Add(pn);
                plat = pn.GetComponent<SupplyPlatform>();
            }
            else Debug.LogWarning("[Доставка] нет префаба площадки — ящики встанут прямо на пол", this);
            if (_log && (sx > 1.001f || sz > 1.001f)) Debug.Log($"[Доставка] площадка растянута под груз: {deckW:0.0}×{deckD:0.0} м (было {baseDeck.x:0}×{baseDeck.y:0})", this);

            // точка слота (метры от угла раскладки) → мир
            Vector3 World(DeckSlot sl, float lift)
            {
                float mx = -deckW * 0.5f + 0.2f + sl.x, mz = -deckD * 0.5f + 0.2f + sl.z;   // метры от центра настила
                if (plat != null) return plat.Top(mx / sx, mz / sz) + plat.transform.up * lift;
                return pos + rot * new Vector3(mx, lift, mz);
            }

            // 3. ящики
            int placed = 0;
            var palletAt = new List<(SupplyItem it, Vector3 p, Quaternion r)>();
            foreach (DeckSlot sl in slots)
            {
                Quaternion r = rot * Quaternion.Euler(0f, sl.turn ? 90f : 0f, 0f);
                if (sl.crate < 0) { palletAt.Add((sl.item, World(sl, sl.item.Size.y * 0.5f + 0.04f), r)); continue; }
                CratePack pack = crates[sl.crate];
                NetworkObject prefab = pack.kind.Prefab != null ? pack.kind.Prefab : cat != null ? cat.CratePrefab : null;
                if (prefab == null) { Debug.LogError("[Доставка] нет префаба ящика — меню CatCooks → Доставка → 1", this); break; }
                NetworkObject cn = Instantiate(prefab, World(sl, 0.02f), r);
                SupplyCrate crate = cn.GetComponent<SupplyCrate>();
                if (crate != null) crate.ServerSetup(pack.cat, pack.items, _cratesCarryable, tag);
                SupplySettle.Calm(cn.gameObject);
                FishNet.InstanceFinder.ServerManager.Spawn(cn.gameObject);
                _spawned.Add(cn);
                placed++;
            }
            if (palletAt.Count > 0) StartCoroutine(SpawnPallet(palletAt));

            if (_log)
            {
                var sb = new StringBuilder();
                sb.Append($"[Доставка] привезли на этаж: ящиков {placed}, на площадке {pallet.Count} крупных, всего предметов {items.Count}");
                foreach (CratePack cr in crates)
                {
                    var names = new List<string>();
                    foreach (SupplyItem it in cr.items) { string t = $"{it.Product?.Title}→{(it.Prefab != null ? it.Prefab.name : "НЕТ ПРЕФАБА")}"; if (!names.Contains(t)) names.Add(t); }
                    sb.Append($"\n  ▪ {SupplyCategories.Title(cr.cat)} ({(cr.kind.Prefab != null ? cr.kind.Prefab.name : "ящик")}): {cr.items.Count} шт. — {string.Join(", ", names)}");
                }
                if (crates.Count == 0 && items.Count == 0) sb.Append("\n  ✖ ПРЕДМЕТОВ 0 — каталог пуст или у продуктов нет префабов: «Доставка → 3» и «Доставка → 7. Проверка»");
                if (missing != null && missing.Count > 0) sb.Append($"\n  ⚠ нет в базе: {string.Join(", ", missing)}");
                Debug.Log(sb.ToString(), this);
            }
        }

        /// <summary>Строка меню → предметы поставки (префаб из каталога / Food Piece из Spawnable / ведро).</summary>
        private static void Resolve(SupplyLine line, FoodDatabase db, SupplyCatalog cat, List<SupplyItem> items, List<string> missing)
        {
            ProductDef p = MacaronTitles.Find(db, line.Product);
            if (p == null) { missing.Add(line.Product); return; }
            List<SupplyCatalog.Entry> es = cat != null ? cat.For(p) : new List<SupplyCatalog.Entry>();
            SupplyCategory c = cat != null ? cat.CategoryOf(p) : SupplyCategories.Of(p);
            Vector3 inner = cat != null ? cat.CrateInner : SupplyCrate.DefaultInner;
            for (int k = 0; k < line.Count; k++)
            {
                if (es.Count > 0)
                {
                    SupplyCatalog.Entry e = es[Random.Range(0, es.Count)];
                    items.Add(new SupplyItem { Prefab = e.Prefab, Product = p, Size = e.Size, Category = c,
                                               Pallet = e.OnPallet || !SupplyCrate.FitsEmpty(e.Size, inner) });
                    continue;
                }
                NetworkObject piece = SupplyCatalog.FindPiecePrefab(p);
                if (piece != null)
                {
                    Vector3 s = MeasurePrefab(piece.gameObject);
                    items.Add(new SupplyItem { Prefab = piece, Product = p, Size = s, Category = c, Pallet = !SupplyCrate.FitsEmpty(s, inner) });
                    continue;
                }
                if (cat != null && cat.BucketPrefab != null)
                {
                    items.Add(new SupplyItem { Prefab = cat.BucketPrefab, Product = p, Bucket = true, Portions = cat.BucketPortions,
                                               Size = MeasurePrefab(cat.BucketPrefab.gameObject), Category = c, Pallet = true });
                    continue;
                }
                missing.Add(line.Product + " (нет префаба и ведра)");
                return;
            }
        }

        private static float MaxSide(Vector3 s) => Mathf.Max(s.x, Mathf.Max(s.y, s.z));

        private static readonly Dictionary<GameObject, Vector3> _sizeCache = new Dictionary<GameObject, Vector3>();

        /// <summary>Габарит префаба (по мешам, в осях корня), м.</summary>
        public static Vector3 MeasurePrefab(GameObject prefab)
        {
            if (prefab == null) return Vector3.one * 0.2f;
            if (Application.isPlaying && _sizeCache.TryGetValue(prefab, out Vector3 s)) return s;   // v25.1: в редакторе — всегда заново (префаб могли пересобрать)
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            Vector3 rootScale = prefab.transform.lossyScale;
            bool any = false; Bounds b = new Bounds();
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
            }
            foreach (SkinnedMeshRenderer sr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (sr.sharedMesh == null) continue;
                Bounds mb = sr.sharedMesh.bounds;
                Vector3 w0 = (toRoot * sr.transform.localToWorldMatrix).MultiplyPoint3x4(mb.center);
                if (!any) { b = new Bounds(w0, mb.size); any = true; } else b.Encapsulate(new Bounds(w0, mb.size));
            }
            s = any ? Vector3.Scale(b.size, new Vector3(Mathf.Abs(rootScale.x), Mathf.Abs(rootScale.y), Mathf.Abs(rootScale.z))) : Vector3.one * 0.2f;
            s = new Vector3(Mathf.Max(0.03f, s.x), Mathf.Max(0.03f, s.y), Mathf.Max(0.03f, s.z));
            _sizeCache[prefab] = s;
            return s;
        }

        /// <summary>Сервер: появился предмет поставки (в ящике или на площадке). Ведро — сразу с содержимым.</summary>
        public static NetworkObject ServerSpawnItem(NetworkBehaviour owner, SupplyItem it, Vector3 pos, Quaternion rot)
        {
            if (it.Prefab == null) { Debug.LogWarning($"[Доставка] «{it.Product?.Title}»: нет префаба — не появился", owner); return null; }
            NetworkObject nob = Instantiate(it.Prefab, pos, rot);
            if (it.Stock > 0) SupplyPlanner.ApplyStock(nob.gameObject, it.Stock);   // v24.2: ровно посчитанный запас (до Spawn — OnStartServer его прочтёт)
            SupplySettle.Calm(nob.gameObject);                                       // v24.4: первые секунды не расталкивает соседей «взрывом»
            FishNet.InstanceFinder.ServerManager.Spawn(nob.gameObject);
            if (it.Bucket && it.Product != null)
            {
                Cookware cw = nob.GetComponentInChildren<Cookware>();
                if (cw != null) cw.ServerAddContent(it.Product.Id, it.Product.StartState, Mathf.Max(1, it.Portions), 0, false);
                else Debug.LogWarning($"[Доставка] у ведра «{it.Prefab.name}» нет Cookware — «{it.Product.Title}» не налить", nob);
            }
            if (Instance != null) Instance._spawnedItems.Add(nob);
            return nob;
        }

        private readonly List<NetworkObject> _spawnedItems = new List<NetworkObject>();

        // ───────────────────────── раскладка ─────────────────────────

        /// <summary>Крупное — по одному, с паузой (места посчитаны в LayoutDeck).</summary>
        private IEnumerator SpawnPallet(List<(SupplyItem it, Vector3 p, Quaternion r)> list)
        {
            foreach (var x in list)
            {
                ServerSpawnItem(this, x.it, x.p, x.r);
                yield return new WaitForSeconds(0.08f);
            }
        }

        /// <summary>Место на настиле: ящик (crate — индекс) или крупный предмет (crate = −1). x, z — центр (м от угла раскладки).</summary>
        public struct DeckSlot { public int crate; public SupplyItem item; public float x, z, w, d; public bool turn; }

        /// <summary>
        /// v24.9: РАСКЛАДКА НАСТИЛА — ящики и крупное вместе, в один слой. Колонки по X глубиной depth (по Z);
        /// крупное — первым; каждый предмет в первую колонку, куда влезает (можно повернуть на 90°).
        /// needW/needD — сколько метров заняло (площадку растягивают под это).
        /// </summary>
        public static List<DeckSlot> LayoutDeck(List<CratePack> crates, List<SupplyItem> pallet, float depth, out float needW, out float needD)
        {
            const float crateGap = 0.35f, itemGap = 0.15f;                     // между ящиками — проход для вил
            var all = new List<DeckSlot>();
            for (int i = 0; i < crates.Count; i++)
            {
                Vector3 o = crates[i].kind.Outer;
                all.Add(new DeckSlot { crate = i, w = o.x + crateGap, d = o.z + crateGap });
            }
            foreach (SupplyItem it in pallet)
                all.Add(new DeckSlot { crate = -1, item = it, w = it.Size.x + itemGap, d = it.Size.z + itemGap });
            all.Sort((a, b) => (b.w * b.d).CompareTo(a.w * a.d));
            float h = depth;
            foreach (DeckSlot s in all) h = Mathf.Max(h, Mathf.Min(s.w, s.d));      // что не влезает даже боком — колонка глубже
            var cols = new List<Vector3>();                                          // x — начало, y — ширина, z — занято вглубь
            float nextX = 0f;
            var res = new List<DeckSlot>(all.Count);
            foreach (DeckSlot s0 in all)
            {
                DeckSlot s = s0;
                bool placed = false;
                for (int c = 0; c < cols.Count && !placed; c++)
                {
                    Vector3 col = cols[c];
                    for (int o = 0; o < 2 && !placed; o++)
                    {
                        float w = o == 0 ? s0.w : s0.d, d = o == 0 ? s0.d : s0.w;
                        if (w > col.y + 1e-3f || col.z + d > h + 1e-3f) continue;
                        s.x = col.x + w * 0.5f; s.z = col.z + d * 0.5f; s.turn = o == 1;
                        col.z += d; cols[c] = col; placed = true;
                    }
                }
                if (!placed)
                {
                    bool longDeep = Mathf.Max(s0.w, s0.d) <= h + 1e-3f;               // длинной стороной вглубь, если влезает
                    float w = longDeep ? Mathf.Min(s0.w, s0.d) : Mathf.Max(s0.w, s0.d);
                    float d = longDeep ? Mathf.Max(s0.w, s0.d) : Mathf.Min(s0.w, s0.d);
                    s.turn = !Mathf.Approximately(w, s0.w);
                    s.x = nextX + w * 0.5f; s.z = d * 0.5f;
                    cols.Add(new Vector3(nextX, w, d));
                    nextX += w;
                }
                res.Add(s);
            }
            needW = nextX;
            needD = 0f;
            foreach (Vector3 c in cols) needD = Mathf.Max(needD, c.z);
            return res;
        }

        /// <summary>Место под площадку на этаже игрока: пол на той же высоте и свободный объём; по кругу и на разных расстояниях.</summary>
        private void FindSpot(Vector3 player, Vector3 fwd, out Vector3 pos, out Quaternion rot)
        {
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            float floorY = player.y;
            if (Physics.Raycast(player + Vector3.up * 0.6f, Vector3.down, out RaycastHit fh, 3f, ~0, QueryTriggerInteraction.Ignore)) floorY = fh.point.y;
            Vector2 deck = new Vector2(6.4f, 4.4f);
            SupplyCatalog cat = Catalog;
            if (cat != null && cat.PlatformPrefab != null)
            {
                SupplyPlatform sp = cat.PlatformPrefab.GetComponent<SupplyPlatform>();
                if (sp != null) deck = sp.DeckSize;
            }
            int[] order = { 0, 1, -1, 2, -2, 3, -3, 4 };
            foreach (float dist in _distances)
                foreach (int k in order)
                {
                    Vector3 dir = Quaternion.Euler(0f, k * 45f, 0f) * fwd;
                    Vector3 c = player + dir * (dist + deck.y * 0.5f);
                    if (!Physics.Raycast(new Vector3(c.x, floorY + 1.6f, c.z), Vector3.down, out RaycastHit h, 3.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(h.point.y - floorY) > 0.45f || h.normal.y < 0.85f) continue;
                    // площадка длинной стороной поперёк взгляда, «лицом» к игроку
                    Quaternion r = Quaternion.LookRotation(-dir, Vector3.up);
                    Vector3 half = new Vector3(deck.x * 0.5f, 0.9f, deck.y * 0.5f);
                    Collider[] hits = Physics.OverlapBox(h.point + Vector3.up * 1.0f, half, r, ~0, QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    foreach (Collider col in hits)
                    {
                        if (col == null || col.GetComponentInParent<PlayerInteractor>() != null) continue;
                        blocked = true; break;
                    }
                    if (blocked) continue;
                    pos = h.point + Vector3.up * 0.01f; rot = r;
                    if (_log) Debug.Log($"[Доставка] место под площадку: {dist:0.#} м от игрока, поворот {k * 45}°", this);
                    return;
                }
            pos = new Vector3(player.x, floorY + 0.01f, player.z) + fwd * (_distances.Length > 0 ? _distances[0] + deck.y * 0.5f : 6f);
            rot = Quaternion.LookRotation(-fwd, Vector3.up);
            Debug.LogWarning("[Доставка] свободного места под площадку рядом с игроком не нашёл — ставлю перед ним как есть (может пересечься со стенами)", this);
        }

        /// <summary>Сервер: убрать площадки и ящики (и всё, что из них ещё лежит).</summary>
        [Server]
        public void ServerClear()
        {
            int n = 0;
            foreach (NetworkObject nob in _spawned) if (nob != null && nob.IsSpawned) { nob.Despawn(); n++; }
            foreach (NetworkObject nob in _spawnedItems) if (nob != null && nob.IsSpawned) { nob.Despawn(); n++; }
            _spawned.Clear(); _spawnedItems.Clear();
            if (_log) Debug.Log($"[Доставка] убрано объектов: {n}", this);
        }

        // ───────────────────────── тест и плашка ─────────────────────────

        private void Update()
        {
            if (!base.IsServerInitialized || !base.IsClientInitialized || UiModal.AnyOpen) return;
            bool menuKey = _testKey != KeyCode.None && Input.GetKeyDown(_testKey);
            bool presetKey = _testPresetKey != KeyCode.None && _testPreset != null && Input.GetKeyDown(_testPresetKey);
            if (!menuKey && !presetKey) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (menuKey && shift) { ServerClear(); return; }
            PlayerInteractor me = LocalPlayer();
            if (presetKey) { ServerDeliverPresets(new List<(SupplyPreset, int)> { (_testPreset, _testPreset.Portions) }, me); return; }
            PickMenu();
            ServerDeliverNear(me != null ? me : AnyPlayer());
        }

        public static PlayerInteractor LocalPlayer()
        {
            foreach (PlayerInteractor pi in FindObjectsByType<PlayerInteractor>(FindObjectsSortMode.None))
                if (pi != null && pi.IsOwner) return pi;
            return AnyPlayer();
        }

        private static GUIStyle _banner, _corner, _sub;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            string h = _hotTitle.Value, d = _dessertTitle.Value, n = _drinkTitle.Value;
            if (string.IsNullOrEmpty(h) && string.IsNullOrEmpty(d) && string.IsNullOrEmpty(n)) return;
            if (_banner == null)
            {
                _banner = new GUIStyle(GUI.skin.box) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
                _corner = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight, richText = true };
                _sub = new GUIStyle(_corner) { alignment = TextAnchor.UpperCenter };
            }
            string Or(string t) => string.IsNullOrEmpty(t) ? "—" : t;
            string text = $"Ужин на {_catsNet.Value}: <color=#FFB27A>{Or(h)}</color> · <color=#FFD9F0>{Or(d)}</color> · <color=#9FD3FF>{Or(n)}</color>";
            if (Time.time < _bannerUntil)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 400f, 64f, 800f, 44f), text, _banner);
                GUI.Label(new Rect(Screen.width * 0.5f - 400f, 110f, 800f, 22f), "продукты на весь ужин привезли на этаж — ящики на площадке (F — открыть)", _sub);
            }
            else
            {
                GUI.Label(new Rect(Screen.width - 620f, 8f, 610f, 22f), text, _corner);
            }
        }
    }
}
