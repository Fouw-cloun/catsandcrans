using System;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food.Supply
{
    /// <summary>
    /// v24.0: КАТАЛОГ ПОСТАВКИ — какой префаб привозить для продукта.
    /// Лежит в Resources («SupplyCatalog»). Заполняет меню CatCooks → Доставка → 2: берёт все префабы с продуктом
    /// (Food Piece, мешки, банки, бидон, пучки, яйца) и делает префабы из таких же объектов в сцене, если их ещё нет.
    ///
    /// Нет записи для продукта → в игре ищется префаб Food Piece с этим продуктом среди Spawnable Prefabs;
    /// нет и его → продукт приезжает в ВЕДРЕ (Bucket Prefab) с содержимым — так приедет всё, что есть в базе.
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Доставка/Каталог продуктов", fileName = "SupplyCatalog")]
    public sealed class SupplyCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Продукт из базы")] public ProductDef Product;
            [Tooltip("Сетевой префаб (NetworkObject). Несколько записей одного продукта — варианты, берётся случайный")]
            public NetworkObject Prefab;
            [Tooltip("Источник: мешок, банка, бидон, пучок — из него черпают сколько надо. В меню «1» = один источник; " +
                     "обед и ужин не складываются, берётся больше")]
            public bool Source;
            [Tooltip("Всегда на площадку, а не в ящик (руками). Без галки на площадку едет только то, что не влезает в ящик — туши")]
            public bool OnPallet;
            [Tooltip("Габарит предмета, м (меню считает само)")]
            public Vector3 Size = new Vector3(0.15f, 0.15f, 0.15f);
            [Tooltip("v24.8: сколько порций даёт ОДИН такой предмет (кусок туши — на много котлет). 0 — как в префабе (Food Piece → Count)")]
            [Min(0)] public int PortionsPerItem;
            [Tooltip("Свой тип ящика (иначе — по названию и группе продукта)")]
            public bool OverrideCategory;
            public SupplyCategory Category;
        }

        [Header("Папка базовых префабов продуктов (меню «Доставка → 2 и 3»)")]
        [Tooltip("Отсюда каталог берёт префабы продуктов: всё, что в папке (и подпапках), — по одному префабу на продукт или несколько вариантов")]
        public string BaseFolder = "Assets/CatCooks/Supply/BasePrefabs";

        [Header("Префабы поставки (ставит меню «Доставка → 1»)")]
        public NetworkObject CratePrefab;
        [Tooltip("v24.5: ящики разных размеров (S, M, L). Доставка берёт самый маленький, в который влезает партия")]
        public List<NetworkObject> CrateSizes = new List<NetworkObject>();
        public NetworkObject PlatformPrefab;
        [Tooltip("Ведро (Cookware) для продуктов без своего префаба — приезжает с содержимым")]
        public NetworkObject BucketPrefab;
        [Tooltip("Сколько порций в ведре поставки (1 ведро = 20 порций)")]
        [Range(1, 60)] public int BucketPortions = 20;
        [Tooltip("Крупнее этого (м, по самой длинной стороне) — на площадку, не в ящик")]
        [HideInInspector] public float PalletAbove = 0.5f;                     // v24.4 не используется: на площадку — только то, что не влезает в ящик

        /// <summary>Вид ящика: префаб и размеры (внутри и снаружи).</summary>
        public struct CrateKind
        {
            public NetworkObject Prefab;
            public Vector3 Inner, Outer;
            public float Volume => Inner.x * Inner.y * Inner.z;
        }

        /// <summary>Все размеры ящиков, от маленького к большому.</summary>
        public List<CrateKind> CrateKinds()
        {
            var res = new List<CrateKind>();
            void Add(NetworkObject n)
            {
                if (n == null || res.Exists(k => k.Prefab == n)) return;
                SupplyCrate c = n.GetComponent<SupplyCrate>();
                if (c != null) res.Add(new CrateKind { Prefab = n, Inner = c.InnerSize, Outer = c.Outer });
            }
            if (CrateSizes != null) foreach (NetworkObject n in CrateSizes) Add(n);
            Add(CratePrefab);
            if (res.Count == 0) res.Add(new CrateKind { Prefab = null, Inner = SupplyCrate.DefaultInner, Outer = SupplyCrate.DefaultInner + new Vector3(0.08f, 0.2f, 0.08f) });
            res.Sort((x, y) => x.Volume.CompareTo(y.Volume));
            return res;
        }

        /// <summary>Внутренний размер САМОГО БОЛЬШОГО ящика — что в него не влезает, едет на площадке.</summary>
        public Vector3 CrateInner
        {
            get { List<CrateKind> k = CrateKinds(); return k[k.Count - 1].Inner; }
        }

        /// <summary>v24.4: едет на площадке, только если отмечено руками или не влезает даже в пустой ящик (туша).</summary>
        public bool OnPallet(Entry e, Vector3 size) => (e != null && e.OnPallet) || !SupplyCrate.FitsEmpty(size, CrateInner);

        [Header("Продукты")]
        public List<Entry> Entries = new List<Entry>();

        private static SupplyCatalog _instance;
        public static SupplyCatalog Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<SupplyCatalog>("SupplyCatalog");
                return _instance;
            }
        }

        private static string Key(string t) => (t ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');

        /// <summary>Записи продукта (по ссылке или по совпадению названия — у базы бывают дубли с одним названием).</summary>
        public List<Entry> For(ProductDef p)
        {
            var res = new List<Entry>();
            if (p == null) return res;
            string k = Key(p.Title);
            foreach (Entry e in Entries)
                if (e != null && e.Prefab != null && e.Product != null && (e.Product == p || Key(e.Product.Title) == k)) res.Add(e);
            return res;
        }

        /// <summary>Приезжает ли продукт источником (мешок, банка, бидон, пучок) — или ведром (тоже источник).</summary>
        public bool IsSource(ProductDef p)
        {
            List<Entry> es = For(p);
            if (es.Count == 0) return FindPiecePrefab(p) == null;            // ведро — источник
            foreach (Entry e in es) if (e.Source) return true;
            return false;
        }

        // ───────────────────────── запасной поиск в Spawnable Prefabs ─────────────────────────

        private static Dictionary<ushort, NetworkObject> _pieceCache;

        /// <summary>Префаб Food Piece с этим продуктом среди Spawnable Prefabs FishNet (нет — null).</summary>
        public static NetworkObject FindPiecePrefab(ProductDef p)
        {
            if (p == null) return null;
            if (_pieceCache == null)
            {
                _pieceCache = new Dictionary<ushort, NetworkObject>();
                FishNet.Managing.NetworkManager nm = FishNet.InstanceFinder.NetworkManager;
                if (nm != null && nm.SpawnablePrefabs != null)
                {
                    int n = nm.SpawnablePrefabs.GetObjectCount();
                    for (int i = 0; i < n; i++)
                    {
                        NetworkObject nob = nm.SpawnablePrefabs.GetObject(true, i);
                        FoodPiece fp = nob != null ? nob.GetComponent<FoodPiece>() : null;
                        ProductDef d = fp != null ? fp.Def : null;
                        if (d != null && !_pieceCache.ContainsKey(d.Id)) _pieceCache[d.Id] = nob;
                    }
                }
            }
            return _pieceCache.TryGetValue(p.Id, out NetworkObject res) ? res : null;
        }

        /// <summary>Тип ящика для продукта (свой из записи или по названию/группе).</summary>
        public SupplyCategory CategoryOf(ProductDef p)
        {
            foreach (Entry e in For(p)) if (e.OverrideCategory) return e.Category;
            return SupplyCategories.Of(p);
        }
    }
}
