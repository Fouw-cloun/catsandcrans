using System.Collections.Generic;
using System.IO;
using System.Text;
using CatCooks.Food.Supply;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;

namespace CatCooks.Food.SupplyEditor
{
    /// <summary>
    /// v24.1 (латинские имена префабов): CatCooks → ДОСТАВКА
    ///   1 — модели и префабы ящика и площадки (меши, материалы; знаки рисуются в игре);
    ///   2 — каталог продуктов: все префабы с продуктом + префабы из объектов-продуктов в сцене (мешки, банки, бидон,
    ///       пучки, яйца, рёбра…) + ведро для жидкого; отчёт, чем приедет каждое блюдо меню;
    ///   3 — поставить «Доставку» в открытую сцену;
    ///   Проверка — что готово, чего не хватает.
    /// </summary>
    public static class CatCooksSupplySetup
    {
        const string Root = "Assets/CatCooks/Supply";
        const string ModelDir = Root + "/Models", MatDir = Root + "/Materials", PrefabDir = Root + "/Prefabs",
                     ProductDir = Root + "/Products", ResDir = Root + "/Resources";
        const string CatalogPath = ResDir + "/SupplyCatalog.asset";

        // размеры ящика (м)
        // v24.5: три размера ящика (ширина × высота стенки × глубина, м; масса, кг). Полозья RH снизу — под вилы погрузчика.
        static readonly (string tag, float w, float h, float d, float mass)[] CrateSizes =
        {
            ("S", 1.4f, 0.9f, 1.0f, 45f),
            ("M", 2.0f, 1.1f, 1.4f, 90f),
            ("L", 2.6f, 1.3f, 1.8f, 150f),
        };
        const float RH = 0.14f;                                                  // высота полозьев

        // ───────────────────────── 1. ящик и площадка ─────────────────────────

        [MenuItem("CatCooks/Доставка/1. Ящик и площадка (модели и префабы)", priority = 1)]
        public static void BuildCrateAndPlatform()
        {
            var log = new StringBuilder("ДОСТАВКА → 1. Ящик и площадка\n");
            Dir(ModelDir); Dir(MatDir); Dir(PrefabDir); Dir(ResDir);

            Material wood = Mat("Mat_SupplyWood", new Color(0.66f, 0.47f, 0.29f), 0.25f, log);
            Material woodDark = Mat("Mat_SupplyWoodDark", new Color(0.42f, 0.28f, 0.17f), 0.2f, log);
            Material steel = Mat("Mat_SupplySteel", new Color(0.46f, 0.49f, 0.52f), 0.45f, log, 0.6f);
            Material yellow = Mat("Mat_SupplyYellow", new Color(0.98f, 0.76f, 0.1f), 0.35f, log);
            Material dark = Mat("Mat_SupplyDark", new Color(0.13f, 0.13f, 0.14f), 0.3f, log);
            Material band = Mat("Mat_SupplyBand", Color.white, 0.3f, log);
            Material emblem = EmblemMat(log);

            // ── ящики трёх размеров (v24.5), на полозьях — под вилы погрузчика ──
            var crateNobs = new List<NetworkObject>();
            foreach (var (tag, w, h, d, mass) in CrateSizes)
            {
                GameObject pf = BuildCrate(tag, w, h, d, mass, wood, woodDark, emblem, band, log);
                if (pf != null) crateNobs.Add(pf.GetComponent<NetworkObject>());
            }
            if (crateNobs.Count > 0)
            {
                Catalog(log).CratePrefab = crateNobs[0];
                Catalog(log).CrateSizes = crateNobs;
            }

            // ── площадка ──
            var pgo = new GameObject("SupplyPlatform");
            try
            {
                SupplyPlatform plat = pgo.AddComponent<SupplyPlatform>();
                Mesh deck = Save(PlatformMesh(plat.DeckSize, plat.DeckHeight), "SupplyPlatform_Deck", log);
                DS.AddByName(pgo, "NetworkObject", log);
                var dgo = new GameObject("Deck");
                dgo.transform.SetParent(pgo.transform, false);
                dgo.AddComponent<MeshFilter>().sharedMesh = deck;
                dgo.AddComponent<MeshRenderer>().sharedMaterials = new[] { steel, yellow, dark };
                Box(pgo, new Vector3(0f, plat.DeckHeight * 0.5f, 0f), new Vector3(plat.DeckSize.x, plat.DeckHeight, plat.DeckSize.y));
                string platPath = PrefabDir + "/SupplyPlatform.prefab";
                GameObject platPrefab = PrefabUtility.SaveAsPrefabAsset(pgo, platPath);
                log.AppendLine($"  ＋ префаб {platPath}");
                Catalog(log).PlatformPrefab = platPrefab.GetComponent<NetworkObject>();
            }
            finally { Object.DestroyImmediate(pgo); }

            EditorUtility.SetDirty(Catalog(log));
            AssetDatabase.SaveAssets();
            log.AppendLine("\nДАЛЬШЕ: «Доставка → 2» (сцена → папка), «→ 3» (каталог), «→ 4» (в сцену).");
            log.AppendLine("Оба префаба должны попасть в Spawnable Prefabs (NetworkManager → DefaultPrefabObjects) — FishNet обычно добавляет сам.");
            Done(log);
        }

        static GameObject BuildCrate(string tag, float W, float H, float D, float mass, Material wood, Material woodDark, Material emblem, Material band, StringBuilder log)
        {
            Mesh body = Save(CrateBody(W, H, D), "SupplyCrate_" + tag + "_Body", log);
            Mesh lid = Save(CrateLid(W, D), "SupplyCrate_" + tag + "_Lid", log);
            var go = new GameObject("SupplyCrate_" + tag);
            try
            {
                DS.AddByName(go, "NetworkObject", log);
                DS.AddByName(go, "NetworkTransform", log);
                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                var b = new GameObject("Body");
                b.transform.SetParent(go.transform, false);
                b.AddComponent<MeshFilter>().sharedMesh = body;
                b.AddComponent<MeshRenderer>().sharedMaterials = new[] { wood, woodDark };
                // полозья (три поперёк — вилы заходят сбоку по X между ними), дно и стенки — отдельные коллайдеры
                foreach (float z in new[] { -D * 0.5f + 0.07f, 0f, D * 0.5f - 0.07f }) Box(b, new Vector3(0f, RH * 0.5f, z), new Vector3(W, RH, 0.14f));
                Box(b, new Vector3(0f, RH + 0.015f, 0f), new Vector3(W, 0.03f, D));
                Box(b, new Vector3(0f, RH + H * 0.5f, D * 0.5f - 0.0125f), new Vector3(W, H, 0.025f));
                Box(b, new Vector3(0f, RH + H * 0.5f, -D * 0.5f + 0.0125f), new Vector3(W, H, 0.025f));
                Box(b, new Vector3(W * 0.5f - 0.0125f, RH + H * 0.5f, 0f), new Vector3(0.025f, H, D));
                Box(b, new Vector3(-W * 0.5f + 0.0125f, RH + H * 0.5f, 0f), new Vector3(0.025f, H, D));

                var pivot = new GameObject("LidPivot");
                pivot.transform.SetParent(go.transform, false);
                pivot.transform.localPosition = new Vector3(0f, RH + H, -D * 0.5f);
                var l = new GameObject("Lid");
                l.transform.SetParent(pivot.transform, false);
                l.AddComponent<MeshFilter>().sharedMesh = lid;
                l.AddComponent<MeshRenderer>().sharedMaterials = new[] { wood, woodDark };
                Box(l, new Vector3(0f, 0.015f, D * 0.5f), new Vector3(W, 0.03f, D));

                SupplyCrate crate = go.AddComponent<SupplyCrate>();
                var so = new SerializedObject(crate);
                so.FindProperty("_lidPivot").objectReferenceValue = pivot.transform;
                so.FindProperty("_emblemMaterial").objectReferenceValue = emblem;
                so.FindProperty("_bandMaterial").objectReferenceValue = band;
                so.FindProperty("_outer").vector3Value = new Vector3(W, RH + H, D);
                so.FindProperty("_innerBottom").vector3Value = new Vector3(0f, RH + 0.035f, 0f);
                so.FindProperty("_innerSize").vector3Value = new Vector3(W - 0.08f, H - 0.06f, D - 0.08f);
                so.ApplyModifiedPropertiesWithoutUndo();
                string path = PrefabDir + "/SupplyCrate_" + tag + ".prefab";
                GameObject pf = PrefabUtility.SaveAsPrefabAsset(go, path);
                log.AppendLine($"  ＋ ящик {tag}: {W:0.0}×{H + RH:0.0}×{D:0.0} м, внутри {W - 0.08f:0.00}×{H - 0.06f:0.00}×{D - 0.08f:0.00}, {mass:0} кг → {path}");
                return pf;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ───────────────────────── 2. каталог ─────────────────────────

        static readonly string[] SourceTypes = { "BulkSack", "LiquidDoseJarItem", "PowderShakerItem", "MilkCanItem", "HerbBunchItem", "LiquidPourSource", "SupplyEggTray" };
        static readonly string[] ProductTypes = { "FoodPiece", "BulkSack", "LiquidDoseJarItem", "PowderShakerItem", "MilkCanItem", "HerbBunchItem", "EggItem", "SupplyEggTray" };
        static readonly string[] SkipTypes = { "Cookware", "SupplyCrate", "SupplyPlatform", "MacaronShell", "CutletPatty", "SpongePiece", "DoughBlob" };

        // v24.8: убрано из меню (старый каталог по всему проекту) — теперь «3. Каталог из папки»
        public static void BuildCatalog()
        {
            var log = new StringBuilder("ДОСТАВКА → 2. Каталог продуктов\n");
            Dir(ProductDir); Dir(ResDir);
            PurgeOldProducts(log);
            _usedPaths.Clear();
            SupplyCatalog cat = Catalog(log);
            var entries = new List<SupplyCatalog.Entry>();
            var have = new HashSet<string>();                                   // путь префаба — без дублей

            // а) префабы проекта с продуктом
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.StartsWith("Assets/") || path.Contains("/Plugins/") || path.Contains("/FishNet/")) continue;
                GameObject pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (pf == null || pf.GetComponent<NetworkObject>() == null) continue;
                if (Has(pf, SkipTypes)) continue;
                ProductDef p = ProductOf(pf, out string kind);
                if (p == null) continue;
                entries.Add(MakeEntry(pf, p, kind));
                have.Add(path);
                log.AppendLine($"  ✔ «{p.Title}» ← {path} ({kind})");
            }

            // б) объекты-продукты в сцене без префаба → свой префаб в Supply/Products
            var covered = new HashSet<ProductDef>();
            foreach (var e in entries) covered.Add(e.Product);
            var done = new HashSet<GameObject>();
            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null || System.Array.IndexOf(ProductTypes, mb.GetType().Name) < 0 && !(mb is FoodPiece)) continue;
                NetworkObject nob = TopNob(mb.gameObject);
                if (nob == null || done.Contains(nob.gameObject)) continue;
                done.Add(nob.gameObject);
                GameObject src = nob.gameObject;
                if (Has(src, SkipTypes)) continue;
                ProductDef p = ProductOf(src, out string kind);
                if (p == null || covered.Contains(p)) continue;
                // экземпляр префаба — берём сам префаб
                GameObject asset = PrefabUtility.IsPartOfPrefabInstance(src) ? PrefabUtility.GetCorrespondingObjectFromSource(src) : null;
                string apath = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
                if (asset != null && asset.GetComponent<NetworkObject>() != null && !have.Contains(apath))
                {
                    entries.Add(MakeEntry(asset, p, kind)); have.Add(apath); covered.Add(p);
                    log.AppendLine($"  ✔ «{p.Title}» ← префаб объекта сцены {apath}");
                    continue;
                }
                GameObject saved = SaveCopy(src, Unique($"{Safe(p.Title)}_{kind}"), log);
                if (saved == null) continue;
                entries.Add(MakeEntry(saved, p, kind)); covered.Add(p);
                log.AppendLine($"  ＋ «{p.Title}» ← из сцены «{src.name}» → {AssetDatabase.GetAssetPath(saved)}");
            }

            // в) ведро для жидкого без префаба
            if (cat.BucketPrefab == null)
            {
                foreach (Cookware cw in Object.FindObjectsByType<Cookware>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (cw.Def == null || cw.Def.Title == null || !cw.Def.Title.ToLowerInvariant().Contains("ведро")) continue;
                    NetworkObject nob = TopNob(cw.gameObject);
                    if (nob == null) continue;
                    GameObject asset = PrefabUtility.IsPartOfPrefabInstance(nob.gameObject) ? PrefabUtility.GetCorrespondingObjectFromSource(nob.gameObject) : null;
                    GameObject saved = asset != null && asset.GetComponent<NetworkObject>() != null ? asset : SaveCopy(nob.gameObject, Unique("SupplyBucket"), log);
                    if (saved == null) continue;
                    cat.BucketPrefab = saved.GetComponent<NetworkObject>();
                    log.AppendLine($"  ＋ ведро для жидкого: «{cw.name}» → {AssetDatabase.GetAssetPath(saved)}");
                    break;
                }
                if (cat.BucketPrefab == null) log.AppendLine("  ⚠ ведра (Cookware «Ведро») в сцене нет — продукты без префаба не приедут. Поставь ведро в сцену или задай Bucket Prefab руками.");
            }

            // сохранить ручные правки (свой тип ящика, «на площадку») у уже бывших записей
            foreach (var e in entries)
                foreach (var old in cat.Entries)
                    if (old != null && old.Prefab != null && e.Prefab == old.Prefab)
                    { e.OverrideCategory = old.OverrideCategory; e.Category = old.Category; /* v24.4: «на площадку» не переносится — решает доставка по размеру */ }
            cat.Entries = entries;
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            log.AppendLine($"\nВ каталоге записей: {entries.Count}.");
            ReportMenu(cat, log);
            log.AppendLine("\nНовые префабы в Supply/Products должны попасть в Spawnable Prefabs (DefaultPrefabObjects) — FishNet обычно добавляет сам.");
            Done(log);
        }

        /// <summary>
        /// v24.6: СОХРАНИТЬ ВЫДЕЛЕННОЕ В ПАПКУ БАЗОВЫХ ПРЕФАБОВ. Выдели в сцене продукты-предметы (кусок мяса, мешок
        /// сахара, банку соли, бидон, яйцо, ведро…) — каждый сохранится отдельным префабом с латинским именем.
        /// Объекты в сцене не меняются (сохраняется копия).
        /// </summary>
        [MenuItem("CatCooks/Доставка/2. Выделенное в сцене → в папку базовых префабов", priority = 2)]
        public static void SaveSelectionToBase()
        {
            var log = new StringBuilder("ДОСТАВКА → 2. В папку базовых префабов\n");
            SupplyCatalog cat = Catalog(log);
            string folder = BaseFolderOf(cat, log);
            Dir(folder);
            _usedPaths.Clear();
            int n = 0;
            foreach (GameObject sel in Selection.gameObjects)
            {
                if (sel == null || EditorUtility.IsPersistent(sel)) continue;
                NetworkObject nob = TopNob(sel);
                GameObject src = nob != null ? nob.gameObject : sel;
                ProductDef p = ProductOf(src, out string kind);
                string name = p != null ? $"{Safe(p.Title)}_{kind}" : Safe(src.name);
                string path = $"{folder}/{name}.prefab";
                for (int k = 2; AssetDatabase.LoadAssetAtPath<GameObject>(path) != null || _usedPaths.Contains(path); k++) path = $"{folder}/{name}_{k}.prefab";
                _usedPaths.Add(path);
                GameObject saved = SaveCopy(src, path, log);
                if (saved == null) continue;
                n++;
                string what = p != null ? $"«{p.Title}» ({kind})" : (src.GetComponentInChildren<Cookware>() != null ? "тара" : "⚠ продукт не найден");
                log.AppendLine($"  ＋ {path} — {what}{(saved.GetComponent<NetworkObject>() == null ? "  ⚠ НЕТ NetworkObject" : "")}");
            }
            log.AppendLine(n == 0 ? "  ничего не выделено (выдели объекты в сцене)" : $"\nСохранено: {n}. Дальше — «Доставка → 3».");
            Done(log);
        }

        /// <summary>
        /// v24.6: КАТАЛОГ ИЗ ПАПКИ БАЗОВЫХ ПРЕФАБОВ — источник правды. Только префабы из папки (и подпапок):
        /// продукт берётся из компонента (Food Piece, мешок, банка, солонка, бидон, пучок, яйцо), ведро — из Cookware «Ведро».
        /// Старые записи каталога заменяются. Отчёт: что найдено, что без продукта, что без NetworkObject.
        /// </summary>
        [MenuItem("CatCooks/Доставка/3. Каталог из папки базовых префабов (+ латиница)", priority = 3)]
        public static void BuildCatalogFromFolder()
        {
            var log = new StringBuilder("ДОСТАВКА → 3. Каталог из папки\n");
            SupplyCatalog cat = Catalog(log);
            string folder = BaseFolderOf(cat, log);
            if (!AssetDatabase.IsValidFolder(folder)) { Dir(folder); log.AppendLine($"  создал папку {folder} — сложи туда префабы (или «→ 2») и запусти ещё раз"); Done(log); return; }
            RenameFolderToLatin(folder, log);   // v24.7: кириллические имена → одинаковый хеш FishNet
            AddSackThrow(folder, log);          // v25.1: мешки — взять, кинуть, поймать, порвать
            var entries = new List<SupplyCatalog.Entry>();
            var products = new Dictionary<string, List<string>>();
            int bad = 0;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                GameObject pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (pf == null) continue;
                string file = Path.GetFileName(path);
                if (pf.GetComponent<NetworkObject>() == null) { log.AppendLine($"  ✖ {file}: нет NetworkObject на корне — доставка не сможет его заспавнить"); bad++; continue; }
                Cookware cw = pf.GetComponentInChildren<Cookware>();
                if (cw != null && cw.Def != null && cw.Def.Title != null && cw.Def.Title.ToLowerInvariant().Contains("ведро") && ProductOf(pf, out _) == null)
                {
                    cat.BucketPrefab = pf.GetComponent<NetworkObject>();
                    log.AppendLine($"  ✔ {file}: ведро для жидкого");
                    continue;
                }
                ProductDef p = ProductOf(pf, out string kind);
                if (p == null) { log.AppendLine($"  ⚠ {file}: не понял, какой это продукт (нет Food Piece / мешка / банки с продуктом)"); bad++; continue; }
                SupplyCatalog.Entry e = MakeEntry(pf, p, kind);
                entries.Add(e);
                if (!products.TryGetValue(p.Title, out List<string> l)) products[p.Title] = l = new List<string>();
                l.Add($"{file} ({kind}{(e.Source ? ", источник" : "")}{(e.PortionsPerItem > 0 ? $", 1 шт = {e.PortionsPerItem} порц." : "")}, {e.Size.x:0.00}×{e.Size.y:0.00}×{e.Size.z:0.00} м)");
            }
            foreach (var old in cat.Entries)
                foreach (var e in entries)
                    if (old != null && old.Prefab != null && e.Prefab == old.Prefab)
                    {
                        e.OverrideCategory = old.OverrideCategory; e.Category = old.Category; e.OnPallet = e.OnPallet || old.OnPallet;
                        if (old.PortionsPerItem > 0 && !(old.PortionsPerItem == 8 && e.PortionsPerItem == 48)) e.PortionsPerItem = old.PortionsPerItem;   // (8 у целой туши — старое авто v24.8)   // v24.8: ручные правки в каталоге не теряются
                    }
            cat.Entries = entries;
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            log.AppendLine($"\nПродуктов: {products.Count}, префабов: {entries.Count}, с проблемами: {bad}");
            foreach (var kv in products) log.AppendLine($"  ✔ {kv.Key}: {string.Join("; ", kv.Value)}");
            log.AppendLine("\nПотом: Fish-Networking → Refresh Default Prefabs и «Доставка → Проверка ужина дня».");
            Done(log);
        }

        static SupplyCatalog.Entry MakeEntry(GameObject pf, ProductDef p, string kind)
        {
            Vector3 size = SupplyDelivery.MeasurePrefab(pf);
            bool source = System.Array.IndexOf(SourceTypes, kind) >= 0;
            // v25.1: мешки, бидоны и лотки яиц — всегда на площадке (их носят и кидают руками), остальное — в ящиках
            bool pallet = kind == "BulkSack" || kind == "MilkCanItem" || kind == "SupplyEggTray";
            // v24.8: крупный кусок мяса (часть туши, брискет, рёбра) — не одна порция, а на много котлет/фарша
            int per = 0;
            string n = pf.name.ToLowerInvariant();
            if (!source && (n.Contains("carcass") || n.Contains("tush") || n.Contains("brisket") || n.Contains("ribs") || n.Contains("shank") || n.Contains("loin")))
                per = 8;
            // v24.9: ЦЕЛАЯ туша (FoodCarcass: 6 частей) — 6 × 8 = 48 порций
            foreach (MonoBehaviour mb in pf.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name == "FoodCarcass") { per = 48; break; }
            return new SupplyCatalog.Entry { Product = p, Prefab = pf.GetComponent<NetworkObject>(), Source = source, OnPallet = pallet, Size = size, PortionsPerItem = per };
        }

        /// <summary>Продукт предмета: яйцо — «Яйцо»; иначе первое поле ProductDef (_product, _milk, _content…).</summary>
        static ProductDef ProductOf(GameObject go, out string kind)
        {
            kind = null;
            foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string tn = mb.GetType().Name;
                bool known = System.Array.IndexOf(ProductTypes, tn) >= 0 || mb is FoodPiece;
                if (!known) continue;
                kind = mb is FoodPiece ? "FoodPiece" : tn;
                if (tn == "EggItem" || tn == "SupplyEggTray") return DS.Find("Яйцо");
                var so = new SerializedObject(mb);
                foreach (string n in new[] { "_product", "_milk", "_content" })
                {
                    SerializedProperty sp = so.FindProperty(n);
                    if (sp != null && sp.propertyType == SerializedPropertyType.ObjectReference && sp.objectReferenceValue is ProductDef pd) return pd;
                }
                SerializedProperty it = so.GetIterator();
                while (it.NextVisible(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is ProductDef pd2) return pd2;
            }
            return null;
        }

        static bool Has(GameObject go, string[] typeNames)
        {
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && System.Array.IndexOf(typeNames, mb.GetType().Name) >= 0) return true;
            return false;
        }

        static NetworkObject TopNob(GameObject go)
        {
            NetworkObject top = null;
            for (Transform t = go.transform; t != null; t = t.parent)
            {
                NetworkObject n = t.GetComponent<NetworkObject>();
                if (n != null) top = n;
            }
            return top;
        }

        static GameObject SaveCopy(GameObject src, string path, StringBuilder log)
        {
            GameObject clone = Object.Instantiate(src);
            try
            {
                clone.name = Path.GetFileNameWithoutExtension(path);
                clone.transform.SetPositionAndRotation(Vector3.zero, src.transform.rotation);
                clone.transform.localScale = src.transform.lossyScale;
                clone.SetActive(true);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(clone, path, out bool ok);
                if (!ok || saved == null) { log.AppendLine($"  ✖ не сохранился префаб {path}"); return null; }
                return saved;
            }
            finally { Object.DestroyImmediate(clone); }
        }

        /// <summary>
        /// v24.7: папка базовых префабов. Если в окне Project выделена папка — берём её и запоминаем в каталоге
        /// (Base Folder), иначе — то, что записано в каталоге.
        /// </summary>
        static string BaseFolderOf(SupplyCatalog cat, StringBuilder log)
        {
            string sel = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            if (!string.IsNullOrEmpty(sel) && AssetDatabase.IsValidFolder(sel))
            {
                if (cat.BaseFolder != sel) { cat.BaseFolder = sel; EditorUtility.SetDirty(cat); log.AppendLine($"  папка базовых префабов = {sel} (взял выделенную в Project)"); }
                return sel;
            }
            return string.IsNullOrEmpty(cat.BaseFolder) ? Root + "/BasePrefabs" : cat.BaseFolder.TrimEnd('/');
        }

        /// <summary>v25.1: на каждый мешок папки (BulkSack на корне) — SupplySackThrow: F взять, ЛКМ кинуть, рвётся от удара.</summary>
        static void AddSackThrow(string folder, StringBuilder log)
        {
            int n = 0;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                GameObject pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (pf == null || pf.GetComponent<CatCooks.Food.Tools.BulkSack>() == null || pf.GetComponent<SupplySackThrow>() != null) continue;
                if (pf.GetComponent<NetworkObject>() == null || pf.GetComponent<Rigidbody>() == null)
                { log.AppendLine($"  ⚠ {Path.GetFileName(path)}: мешку нужны NetworkObject и Rigidbody на корне — кидать нельзя"); continue; }
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<InteractableItem>() != null) { log.AppendLine($"  · {Path.GetFileName(path)}: уже есть свой предмет-в-руку — не трогаю"); continue; }
                    root.AddComponent<SupplySackThrow>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    n++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            if (n > 0) log.AppendLine($"  ✋ мешков стало можно брать и кидать: {n} (F — взять, ЛКМ — кинуть, ловят руками, рвутся от удара)");
        }

        static bool IsLatin(string s)
        {
            foreach (char ch in s)
                if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-')) return false;
            return true;
        }

        /// <summary>
        /// v24.7: переименовать все префабы папки (и подпапок) в латиницу: «дрожжи» → «Drozhzhi», «картошка » → «Kartoshka».
        /// FishNet считает хеш префаба по пути, выкидывая всё кроме a–z/0–9 — у кириллических файлов в одной папке хеш одинаковый.
        /// Ссылки на префабы (GUID) не ломаются, в сцене ничего не меняется.
        /// </summary>
        static int RenameFolderToLatin(string folder, StringBuilder log)
        {
            int n = 0;
            // защита: не трогаем корень проекта и чужие папки (FishNet, сцены, плагины)
            string f = folder.TrimEnd('/');
            if (f == "Assets" || f.Contains("/FishNet") || f.Contains("/Plugins") || f.Contains("/Scenes") || f.Contains("/Resources"))
            {
                log.AppendLine($"  ✖ папку «{f}» переименовывать не буду — выдели именно папку с базовыми префабами продуктов");
                return 0;
            }
            var taken = new HashSet<string>();
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (string g in guids) taken.Add(AssetDatabase.GUIDToAssetPath(g).ToLowerInvariant());
            int todo = 0;
            foreach (string g in guids) if (!IsLatin(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)))) todo++;
            if (todo == 0) return 0;
            if (!EditorUtility.DisplayDialog("Латиница в именах префабов",
                $"Переименовать {todo} префаб(ов) в папке\n{f}\nв латиницу? Ссылки не сломаются.", "Переименовать", "Отмена"))
            { log.AppendLine("  отменено"); return 0; }
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string name = Path.GetFileNameWithoutExtension(path);
                if (IsLatin(name)) continue;
                string dir = Path.GetDirectoryName(path).Replace('\\', '/');
                string baseName = Safe(name), target = baseName;
                for (int k = 2; taken.Contains($"{dir}/{target}.prefab".ToLowerInvariant()); k++) target = $"{baseName}_{k}";
                string err = AssetDatabase.RenameAsset(path, target);
                if (!string.IsNullOrEmpty(err)) { log.AppendLine($"  ✖ не переименовал {name}: {err}"); continue; }
                taken.Add($"{dir}/{target}.prefab".ToLowerInvariant());
                log.AppendLine($"  ✎ {name}.prefab → {target}.prefab");
                n++;
            }
            // кириллица в названии самой папки тоже выкидывается из хеша — предупреждаем
            if (!IsLatin(Path.GetFileName(folder))) log.AppendLine($"  ⚠ в названии папки «{Path.GetFileName(folder)}» не латиница — лучше переименуй её");
            if (n > 0) { AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); log.AppendLine($"  переименовано в латиницу: {n}. Потом: Fish-Networking → Refresh Default Prefabs"); }
            return n;
        }

        // v24.8: делается автоматически в «3. Каталог из папки»
        public static void RenameBaseToLatin()
        {
            var log = new StringBuilder("ДОСТАВКА → 2в. Латиница в именах префабов\n");
            SupplyCatalog cat = Catalog(log);
            string folder = BaseFolderOf(cat, log);
            if (!AssetDatabase.IsValidFolder(folder)) { log.AppendLine($"  нет папки {folder} — выдели папку в окне Project"); Done(log); return; }
            if (RenameFolderToLatin(folder, log) == 0) log.AppendLine("  все имена уже латиницей");
            Done(log);
        }

        /// <summary>
        /// Имя файла только латиницей: FishNet считает хеш префаба по пути, выкидывая всё кроме a–z и 0–9,
        /// и кириллические имена («Сахар_BulkSack», «Мука_BulkSack») давали одинаковый хеш → ошибка «same assetPath hash».
        /// </summary>
        static string Safe(string s)
        {
            const string ru = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
            string[] la = { "a","b","v","g","d","e","e","zh","z","i","y","k","l","m","n","o","p","r","s","t","u","f","kh","ts","ch","sh","shch","","y","","e","yu","ya" };
            var sb = new StringBuilder();
            foreach (char ch in s.ToLowerInvariant())
            {
                int i = ru.IndexOf(ch);
                if (i >= 0) sb.Append(la[i]);
                else if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            string r = sb.ToString().Trim('_');
            if (r.Length == 0) r = "product";
            return char.ToUpperInvariant(r[0]) + r.Substring(1);
        }

        static readonly HashSet<string> _usedPaths = new HashSet<string>();
        /// <summary>Свободный путь в этом прогоне (одинаковая транслитерация → _2, _3…).</summary>
        static string Unique(string baseName)
        {
            string path = $"{ProductDir}/{baseName}.prefab";
            for (int n = 2; _usedPaths.Contains(path); n++) path = $"{ProductDir}/{baseName}_{n}.prefab";
            _usedPaths.Add(path);
            return path;
        }

        /// <summary>Убрать префабы прошлых прогонов с не-латинскими именами (они и давали конфликт хешей).</summary>
        static void PurgeOldProducts(StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(ProductDir)) return;
            int n = 0;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { ProductDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string file = Path.GetFileNameWithoutExtension(path);
                bool latin = true;
                foreach (char ch in file) if (ch > 127) { latin = false; break; }
                if (latin) continue;
                if (AssetDatabase.DeleteAsset(path)) n++;
            }
            if (n > 0) log.AppendLine($"  − удалено старых префабов с русскими именами: {n}");
        }

        /// <summary>Чем приедет каждое блюдо меню: префаб / Food Piece / ведро / нет.</summary>
        static void ReportMenu(SupplyCatalog cat, StringBuilder log)
        {
            log.AppendLine("\nМЕНЮ — чем приедет сырьё:");
            foreach (SupplyDish d in SupplyMenuBook.Dishes)
            {
                log.AppendLine($"  ■ {d.Title}");
                foreach (SupplyLine l in d.Lines)
                {
                    ProductDef p = DS.Find(l.Product);
                    if (p == null) { log.AppendLine($"     ✖ {l.Product} — НЕТ В БАЗЕ"); continue; }
                    List<SupplyCatalog.Entry> es = cat.For(p);
                    string how = es.Count > 0 ? $"префаб «{es[0].Prefab.name}»" + (es.Count > 1 ? $" (+{es.Count - 1} вар.)" : "") + (es[0].OnPallet ? ", на площадке" : $", ящик «{SupplyCategories.Title(cat.CategoryOf(p))}»")
                                : cat.BucketPrefab != null ? "⚠ в ВЕДРЕ (своего префаба нет)" : "✖ НЕ ПРИЕДЕТ (нет префаба и ведра)";
                    log.AppendLine($"     {(es.Count > 0 ? "✔" : "·")} {l.Product} ×{l.Count} — {how}");
                }
            }
        }

        // ───────────────────────── 3. в сцену ─────────────────────────

        [MenuItem("CatCooks/Доставка/4. Поставить доставку в сцену", priority = 4)]
        public static void PlaceInScene()
        {
            var log = new StringBuilder("ДОСТАВКА → 3. В сцену\n");
            SupplyDelivery have = Object.FindFirstObjectByType<SupplyDelivery>(FindObjectsInactive.Include);
            if (have != null) { Selection.activeObject = have; log.AppendLine($"  ✔ уже есть: «{have.name}»"); Done(log); return; }
            var go = new GameObject("SupplyDelivery");
            Undo.RegisterCreatedObjectUndo(go, "SupplyDelivery");
            DS.AddByName(go, "NetworkObject", log);
            SupplyDelivery sd = Undo.AddComponent<SupplyDelivery>(go);
            var so = new SerializedObject(sd);
            so.FindProperty("_catalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SupplyCatalog>(CatalogPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeObject = go;
            log.AppendLine("  ＋ «SupplyDelivery» (NetworkObject + Supply Delivery). Сохрани сцену.");
            log.AppendLine("  В игре: при старте — меню (обед/ужин) и площадка с ящиками рядом с игроком. Хост: Home — новая доставка, Shift+Home — убрать.");
            Done(log);
        }

        // ───────────────────────── 4–5. пресеты ─────────────────────────

        const string PresetDir = Root + "/Presets";

        struct PL { public string P; public float A; public SupplyUnit U; public string N; public PL(string p, float a, SupplyUnit u, string n = "") { P = p; A = a; U = u; N = n; } }

        /// <summary>
        /// Макарон «Солёная каска» — сырьё на 24 штуки (3 замеса теста = 48 скорлупок, 2 ганаша по 12):
        ///   меренга ×2 (по 4 белка + 2 маст. сахара) → 30 порц. нужно на 3 теста по 10;
        ///   миндальная смесь 12 маст. (грохот 1:1) → 6 маст. миндальной муки + 6 маст. сахара;
        ///   тёртое какао 3 маст. (по 1 на тесто);
        ///   шоколадная масса ×2 (по 3 маст. шоколада + 1 маст. масла) → 4 маст. на ганаш ×2;
        ///   карамель (3 маст. сахара) → солёная карамель (+1 маст. масла, +1 маст. соли) → по 2 маст. на ганаш.
        /// </summary>
        // v24.8: убрано из меню (тестовый пресет)
        public static void MacaronPreset()
        {
            var log = new StringBuilder("ДОСТАВКА → 4. Пресет макарон\n");
            string cocoa = DS.Find("Тёртое какао") != null ? "Тёртое какао" : "Тертое какао";
            SupplyPreset pr = MakePreset("Макарон «Солёная каска»", "Preset_Macaron", SupplyCourse.Dessert, 30, 24, log,
                new PL("Яйцо", 8, SupplyUnit.Pieces, "меренга ×2: белки"),
                new PL("Сахар", 4, SupplyUnit.Trowels, "меренга ×2"),
                new PL("Мука миндальная", 6, SupplyUnit.Trowels, "миндальная смесь (грохот 1:1)"),
                new PL("Сахар", 6, SupplyUnit.Trowels, "миндальная смесь (грохот 1:1)"),
                new PL(cocoa, 3, SupplyUnit.Trowels, "тесто ×3"),
                new PL("Шоколад", 6, SupplyUnit.Trowels, "шоколадная масса ×2"),
                new PL("Сливочное масло", 2, SupplyUnit.Trowels, "шоколадная масса ×2"),
                new PL("Сахар", 3, SupplyUnit.Trowels, "карамель"),
                new PL("Сливочное масло", 1, SupplyUnit.Trowels, "солёная карамель"),
                new PL("Соль", 1, SupplyUnit.Trowels, "солёная карамель"));
            if (pr != null)
            {
                SupplyDelivery sd = Object.FindFirstObjectByType<SupplyDelivery>(FindObjectsInactive.Include);
                if (sd != null) { SupplyPresetEditor.SetOnDelivery(sd, "_testPreset", pr, false); log.AppendLine("  ✔ стал тестовым в Supply Delivery: в игре (хост) End — привезти. Сохрани сцену."); }
                else log.AppendLine("  ⚠ в сцене нет Supply Delivery («→ 4») — потом нажми у пресета «Тестовый в сцене (End)»");
                Selection.activeObject = pr;
                log.AppendLine("  Расчёт ящиков — в инспекторе пресета (поменяй «Порций блюда» — пересчитается).");
            }
            Done(log);
        }

        /// <summary>Основные блюда из старого меню кода → пресеты (на 4 порции; источники — по 10 порций, ПРОВЕРЬ количества) и в меню уровня.</summary>
        // v24.8: убрано из меню (пресеты блюд — через окно пресета)
        public static void MenuPresets()
        {
            var log = new StringBuilder("ДОСТАВКА → 5. Пресеты основных блюд\n");
            SupplyCatalog cat = AssetDatabase.LoadAssetAtPath<SupplyCatalog>(CatalogPath);
            SupplyDelivery sd = Object.FindFirstObjectByType<SupplyDelivery>(FindObjectsInactive.Include);
            foreach (SupplyDish d in SupplyMenuBook.Dishes)
            {
                var lines = new List<PL>();
                foreach (SupplyLine l in d.Lines)
                {
                    ProductDef p = DS.Find(l.Product);
                    bool src = p == null || cat == null || cat.IsSource(p);
                    lines.Add(src ? new PL(l.Product, 10 * l.Count, SupplyUnit.Portions, "проверь количество") : new PL(l.Product, l.Count, SupplyUnit.Pieces));
                }
                SupplyPreset pr = MakePreset(d.Title, "Preset_" + Safe(d.Title), SupplyCourse.Hot, 4, 4, log, lines.ToArray());
                if (pr != null && sd != null) SupplyPresetEditor.SetOnDelivery(sd, "_presets", pr, true);
            }
            log.AppendLine(sd != null ? "  ✔ добавлены в меню уровня (Supply Delivery → Presets). Сохрани сцену." : "  ⚠ нет Supply Delivery в сцене — добавь пресеты в меню кнопкой у пресета.");
            log.AppendLine("  Мешки/банки записаны по 10 порций — поправь под рецепты; пресеты в " + PresetDir);
            Done(log);
        }

        /// <summary>
        /// v24.3: ТЕСТОВЫЙ УЖИН ДНЯ — котлеты с пюре + Аляска + морс, на 40 котов (строки записаны прямо на 40, запас 15 %).
        /// Создаёт продукт «Клюква», если его нет; ставит три пресета ужином в Supply Delivery (меню руками).
        /// Количества — порциями сырья на весь ужин (расчёт цепочек — в документе «Тестовый день»).
        /// </summary>
        [MenuItem("CatCooks/Доставка/6. Ужин дня: котлеты с пюре · Аляска · морс", priority = 6)]
        public static void DinnerDay()
        {
            var log = new StringBuilder("ДОСТАВКА → 6. Ужин дня (тест)\n");
            // клюква — единственный новый продукт дня (модель и префаб — «Доставка → 5»)
            if (DS.Find("Клюква") == null)
            {
                CatCooksCranberrySetup.EnsureProduct(log);
                log.AppendLine("  ⚠ «Клюква» — новый продукт: запусти «Доставка → 5» (модель, префаб, база, каталог), иначе приедет в ведре");
            }
            else if (AssetDatabase.LoadAssetAtPath<GameObject>(CatCooksCranberrySetup.PrefabPath) == null)
                log.AppendLine("  ⚠ у клюквы нет префаба — запусти «Доставка → 5»");
            string sugar = "Сахар", milk = "Молоко";
            string crumbs = DS.Find("Панировочные сухари") != null ? "Панировочные сухари" : "Панировочные";
            // v24.9: ПОЛУФАБРИКАТЫ НЕ ВОЗИМ — сливки, сливочное масло и хлеб делаются на кухне из сырья.
            // Пересчёт по рецептам полуфабрикатов (CatCooksSubRecipes / тесто дрожжевое):
            //   сливки = MilkPerCream молока (отстаиваются), масло = CreamPerButter сливок (взбиваются),
            //   буханки — из теста: замес = вода 1 ведро (гидрант) + мука 8 маст. + дрожжи 1 доза + соль 1 маст.
            const float MilkPerCream = 3f, CreamPerButter = 3f;
            const int LoavesPerBatch = 3;
            float butterHot = 8f;                                  // в пюре; жарка — на подсолнечном
            float butterDessert = 10f;                             // шоколадная масса
            float creamDessert = 70f;                              // мороженое
            float creamTotal = creamDessert + (butterHot + butterDessert) * CreamPerButter;
            int batches = Mathf.CeilToInt(3f / LoavesPerBatch);
            log.AppendLine($"  полуфабрикаты: масло {butterHot + butterDessert} порц. ← сливки {(butterHot + butterDessert) * CreamPerButter}; " +
                           $"сливки всего {creamTotal} ← молоко {creamTotal * MilkPerCream}; хлеб 3 буханки ← тесто ×{batches}");
            SupplyPreset hot = MakePreset("Домашние котлеты с пюре", "Preset_Day_Cutlets", SupplyCourse.Hot, 40, 40, log,
                new PL("Говядина", 32, SupplyUnit.Portions, "фарш 2 части"),
                new PL("Свинина", 32, SupplyUnit.Portions, "фарш 2 части"),
                new PL("Мука пшеничная", 16 * batches, SupplyUnit.Portions, $"тесто на хлеб ×{batches} → мякиш"),
                new PL("Дрожжи", 1 * batches, SupplyUnit.Portions, "тесто на хлеб"),
                new PL("Соль", 2 * batches, SupplyUnit.Portions, "тесто на хлеб"),
                new PL(milk, 8, SupplyUnit.Portions, "хлебная масса"),
                new PL("Лук репчатый", 3, SupplyUnit.Pieces, "фарш 1 часть"),
                new PL("Соль", 4, SupplyUnit.Portions, "фарш и пюре"),
                new PL("Перец чёрный", 2, SupplyUnit.Portions, "фарш"),
                new PL(crumbs, 8, SupplyUnit.Portions, "панировка"),
                new PL("Масло подсолнечное", 16, SupplyUnit.Portions, "жарка"),
                new PL("Картофель", 11, SupplyUnit.Pieces, "пюре (8 частей)"),
                new PL(milk, 12, SupplyUnit.Portions, "пюре (2 части)"),
                new PL(milk, butterHot * CreamPerButter * MilkPerCream, SupplyUnit.Portions, "→ сливки → масло в пюре"));
            SupplyPreset dessert = MakePreset("Запечённая Аляска", "Preset_Day_Alaska", SupplyCourse.Dessert, 40, 40, log,
                new PL("Яйцо", 56, SupplyUnit.Pieces, "24 — бисквит, 32 — меренга (желтки → мороженое)"),
                new PL(sugar, 16, SupplyUnit.Portions, "яичная масса"),
                new PL(sugar, 32, SupplyUnit.Portions, "меренга ×8 партий"),
                new PL(sugar, 14, SupplyUnit.Portions, "мороженое"),
                new PL("Шоколад", 30, SupplyUnit.Portions, "шоколадная масса ×5"),
                new PL("Мука пшеничная", 24, SupplyUnit.Portions, "бисквит ×4 опалубки"),
                new PL(milk, butterDessert * CreamPerButter * MilkPerCream, SupplyUnit.Portions, "→ сливки → масло в шоколадную массу"),
                new PL(milk, creamDessert * MilkPerCream, SupplyUnit.Portions, "→ сливки для мороженого"));
            SupplyPreset drink = MakePreset("Морс клюквенный", "Preset_Day_Mors", SupplyCourse.Drink, 40, 40, log,
                new PL("Клюква", 40, SupplyUnit.Portions, "сок + отвар жмыха"),
                new PL(sugar, 20, SupplyUnit.Portions, "в отвар"));
            foreach (SupplyPreset pr in new[] { hot, dessert, drink }) if (pr != null) pr.ReservePercent = 15;
            AssetDatabase.SaveAssets();
            SupplyDelivery sd = Object.FindFirstObjectByType<SupplyDelivery>(FindObjectsInactive.Include);
            if (sd != null)
            {
                foreach (SupplyPreset pr in new[] { hot, dessert, drink }) SupplyPresetEditor.SetOnDelivery(sd, "_presets", pr, true);
                var so = new SerializedObject(sd);
                so.FindProperty("_forceHot").stringValue = hot != null ? hot.Title : "";
                so.FindProperty("_forceDessert").stringValue = dessert != null ? dessert.Title : "";
                so.FindProperty("_forceDrink").stringValue = drink != null ? drink.Title : "";
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(sd.gameObject.scene);
                log.AppendLine("  ✔ в Supply Delivery: ужин дня задан руками (Force Hot / Dessert / Drink). Сохрани сцену. Хост: Home — новая доставка.");
            }
            else log.AppendLine("  ⚠ в сцене нет Supply Delivery — «→ 4»");
            log.AppendLine("  Вода для морса — из гидранта, в поставку не входит.");
            Selection.activeObject = hot;
            Done(log);
        }

        static SupplyPreset MakePreset(string title, string file, SupplyCourse course, int portions, int baseServ, StringBuilder log, params PL[] lines)
        {
            Dir(PresetDir);
            string path = $"{PresetDir}/{file}.asset";
            SupplyPreset pr = AssetDatabase.LoadAssetAtPath<SupplyPreset>(path);
            bool fresh = pr == null;
            if (fresh) pr = ScriptableObject.CreateInstance<SupplyPreset>();
            pr.Title = title; pr.Course = course; pr.IsDessert = course == SupplyCourse.Dessert; pr.InMenu = true;
            pr.Portions = portions; pr.BaseServings = baseServ; pr.ReservePercent = 10; pr.LimitedStock = true;
            pr.Lines = new List<SupplyPreset.Line>();
            foreach (PL l in lines)
            {
                ProductDef p = DS.Find(l.P);
                if (p == null) { log.AppendLine($"  ✖ «{title}»: нет продукта «{l.P}» в базе — строка пропущена"); continue; }
                pr.Lines.Add(new SupplyPreset.Line { Product = p, Amount = l.A, Unit = l.U, Note = l.N });
            }
            if (fresh) AssetDatabase.CreateAsset(pr, path); else EditorUtility.SetDirty(pr);
            AssetDatabase.SaveAssets();
            log.AppendLine($"  {(fresh ? "＋" : "✔")} {path}: {pr.Lines.Count} строк, {portions} порц.");
            return pr;
        }

        // ───────────────────────── проверка ─────────────────────────

        // v24.8: вызывается из «Проверки» ниже
        public static void Check()
        {
            var log = new StringBuilder("ДОСТАВКА → проверка\n");
            SupplyCatalog cat = AssetDatabase.LoadAssetAtPath<SupplyCatalog>(CatalogPath);
            if (cat == null) { log.AppendLine("  ✖ нет каталога — «Доставка → 1» и «→ 3»"); Done(log); return; }
            log.AppendLine(cat.CratePrefab != null ? $"  ✔ ящики: {cat.CrateKinds().Count} размера" : "  ✖ нет префаба ящика — «→ 1»");
            log.AppendLine(cat.PlatformPrefab != null ? "  ✔ площадка" : "  ✖ нет префаба площадки — «→ 1»");
            log.AppendLine(cat.BucketPrefab != null ? "  ✔ ведро для жидкого" : "  ⚠ нет ведра — продукты без префаба не приедут");
            log.AppendLine($"  записей в каталоге: {cat.Entries.Count}");
            // v24.8: каталог собран не из папки базовых префабов? (старые записи из всего проекта)
            string bf = (cat.BaseFolder ?? "").TrimEnd('/') + "/";
            int outside = 0;
            foreach (var e in cat.Entries)
                if (e != null && e.Prefab != null && !AssetDatabase.GetAssetPath(e.Prefab).StartsWith(bf)) outside++;
            log.AppendLine(outside == 0 ? $"  ✔ все префабы из папки {cat.BaseFolder}"
                : $"  ⚠ {outside} из {cat.Entries.Count} префабов НЕ из папки «{cat.BaseFolder}» — каталог старый: выдели папку в Project и «Доставка → 3»");
            log.AppendLine(Object.FindFirstObjectByType<SupplyDelivery>(FindObjectsInactive.Include) != null ? "  ✔ доставка в сцене" : "  ✖ доставки в сцене нет — «→ 4»");
            ReportMenu(cat, log);
            Done(log);
        }

        /// <summary>
        /// v24.4: ПРОВЕРКА УЖИНА ДНЯ — без запуска игры: что, сколько, каким префабом (путь и размер), в ящик или на
        /// площадку, сколько ящиков. Внизу — что едет ведром (своего префаба нет) и чего нет вовсе.
        /// </summary>
        [MenuItem("CatCooks/Доставка/7. Проверка: ящики, каталог, ужин дня", priority = 20)]
        public static void CheckDinner()
        {
            Check();                                                            // v24.8: сначала общая проверка
            var log = new StringBuilder("ДОСТАВКА → проверка ужина дня\n");
            SupplyCatalog cat = AssetDatabase.LoadAssetAtPath<SupplyCatalog>(CatalogPath);
            SupplyDelivery sd = Object.FindFirstObjectByType<SupplyDelivery>(FindObjectsInactive.Include);
            if (cat == null || sd == null) { log.AppendLine("  ✖ нужны каталог («→ 1», «→ 3») и доставка в сцене («→ 4»)"); Done(log); return; }
            var so = new SerializedObject(sd);
            int cats = so.FindProperty("_cats").intValue;
            var presets = new List<SupplyPreset>();
            SerializedProperty arr = so.FindProperty("_presets");
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue is SupplyPreset sp) presets.Add(sp);
            var order = new List<(SupplyPreset, int)>();
            foreach (var (field, course) in new[] { ("_forceHot", SupplyCourse.Hot), ("_forceDessert", SupplyCourse.Dessert), ("_forceDrink", SupplyCourse.Drink) })
            {
                string t = so.FindProperty(field).stringValue;
                SupplyPreset pr = presets.Find(x => x != null && x.Course == course && x.Title == t) ?? presets.Find(x => x != null && x.Course == course && x.InMenu);
                if (pr != null) order.Add((pr, cats));
                log.AppendLine($"  {(pr != null ? "✔" : "✖")} {SupplyCourseName(course)}: {(pr != null ? pr.Title : "нет пресета")}");
            }
            log.AppendLine($"  котов: {cats} (в игре — по числу игроков, если Scale By Players)\n");
            SupplyPlan plan = SupplyPlanner.Plan(order, cat);
            var buckets = new List<string>(); var none = new List<string>();
            foreach (SupplyPlanLine l in plan.Lines)
            {
                List<SupplyCatalog.Entry> es = cat.For(l.Product);
                string pf = es.Count > 0 ? $"{AssetDatabase.GetAssetPath(es[0].Prefab)} {es[0].Size.x:0.00}×{es[0].Size.y:0.00}×{es[0].Size.z:0.00} м" : "своей записи нет";
                if (l.Problem != null) { none.Add(l.Product?.Title); log.AppendLine($"  ✖ {l.Product?.Title} — {l.NeedRounded} {(l.Pieces ? "шт." : "порц.")}: {l.Problem}"); continue; }
                if (l.Via.StartsWith("⚠")) buckets.Add(l.Product.Title);
                log.AppendLine($"  {(l.Via.StartsWith("⚠") ? "⚠" : "✔")} {l.Product.Title} — {l.NeedRounded} {(l.Pieces ? "шт." : "порц.")} → {l.Via}{(l.Pallet ? " · НА ПЛОЩАДКЕ" : "")}\n       {pf}");
            }
            var pallet = new List<SupplyItem>();
            var crates = SupplyDelivery.PackCrates(plan.Items, 120, pallet, cat.CrateKinds());
            log.AppendLine($"\n  ЯЩИКОВ: {crates.Count}, на площадке: {pallet.Count}, предметов всего: {plan.Items.Count}");
            foreach (var c in crates) log.AppendLine($"    ▪ {SupplyCategories.Title(c.cat)} — {(c.kind.Prefab != null ? c.kind.Prefab.name : "ящик")}: {c.items.Count} шт.");
            if (buckets.Count > 0) log.AppendLine($"\n  ⚠ ЕДЕТ ВЕДРОМ (нет своего префаба): {string.Join(", ", buckets)}");
            if (none.Count > 0) log.AppendLine($"  ✖ НЕ ПРИЕДЕТ: {string.Join(", ", none)}");
            log.AppendLine("\n  Свой префаб: поставь продукт в сцену как предмет (Food Piece / мешок / банка), «Доставка → 2» и «→ 3».");
            Done(log);
        }

        static string SupplyCourseName(SupplyCourse c) => c == SupplyCourse.Hot ? "горячее" : c == SupplyCourse.Dessert ? "десерт" : "напиток";

        // ───────────────────────── меши ─────────────────────────

        sealed class MB
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector3> n = new List<Vector3>(); readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int>[] tris;
            public MB(int subs) { tris = new List<int>[subs]; for (int i = 0; i < subs; i++) tris[i] = new List<int>(); }
            /// <summary>Коробка min..max, материал sub; UV — по метрам (дерево ложится полосами).</summary>
            public void Box(Vector3 min, Vector3 max, int sub)
            {
                Vector3 c = (min + max) * 0.5f, e = (max - min) * 0.5f;
                Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
                for (int a = 0; a < 3; a++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 nn = axes[a] * s;
                        Vector3 u = axes[(a + 1) % 3], w = axes[(a + 2) % 3];
                        if (s < 0) { Vector3 t = u; u = w; w = t; }
                        float eu = Vector3.Scale(e, u).magnitude, ew = Vector3.Scale(e, w).magnitude;
                        Vector3 fc = c + Vector3.Scale(e, nn).magnitude * nn;
                        int b0 = v.Count;
                        Vector3[] q = { fc - u * eu - w * ew, fc + u * eu - w * ew, fc + u * eu + w * ew, fc - u * eu + w * ew };
                        foreach (Vector3 p in q)
                        {
                            v.Add(p); n.Add(nn);
                            float pu = Vector3.Dot(p, u), pw = Vector3.Dot(p, w);
                            uv.Add(new Vector2(pu * 2f, pw * 2f));
                        }
                        tris[sub].AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
                    }
            }
            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
                m.subMeshCount = tris.Length;
                for (int i = 0; i < tris.Length; i++) m.SetTriangles(tris[i], i);
                m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
        }

        static Mesh CrateBody(float W, float H, float D)
        {
            var b = new MB(2);
            const float t = 0.025f, post = 0.06f, gap = 0.012f;
            // полозья — три тёмных бруса поперёк (под вилы погрузчика)
            foreach (float z in new[] { -D * 0.5f + 0.07f, 0f, D * 0.5f - 0.07f })
                b.Box(new Vector3(-W * 0.5f, 0f, z - 0.07f), new Vector3(W * 0.5f, RH, z + 0.07f), 1);
            // дно — доски вдоль X
            int nb = Mathf.Max(4, Mathf.RoundToInt(D / 0.16f));
            float bw = (D - (nb - 1) * gap) / nb;
            for (int i = 0; i < nb; i++) { float z0 = -D * 0.5f + i * (bw + gap); b.Box(new Vector3(-W * 0.5f, RH, z0), new Vector3(W * 0.5f, RH + 0.03f, z0 + bw), 0); }
            // угловые стойки
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    Vector3 c = new Vector3(sx * (W * 0.5f - post * 0.5f), RH, sz * (D * 0.5f - post * 0.5f));
                    b.Box(c + new Vector3(-post * 0.5f - 0.002f, 0f, -post * 0.5f - 0.002f), c + new Vector3(post * 0.5f + 0.002f, H, post * 0.5f + 0.002f), 1);
                }
            // стенки — доски с щелями (по высоте ~0.15 м каждая)
            int np = Mathf.Max(3, Mathf.RoundToInt(H / 0.16f));
            float ph = (H - 0.03f - (np - 1) * gap) / np;
            for (int i = 0; i < np; i++)
            {
                float y0 = RH + 0.03f + i * (ph + gap), y1 = y0 + ph;
                foreach (float sz in new[] { -1f, 1f })
                    b.Box(new Vector3(-W * 0.5f + post, y0, sz > 0 ? D * 0.5f - t : -D * 0.5f), new Vector3(W * 0.5f - post, y1, sz > 0 ? D * 0.5f : -D * 0.5f + t), 0);
                foreach (float sx in new[] { -1f, 1f })
                    b.Box(new Vector3(sx > 0 ? W * 0.5f - t : -W * 0.5f, y0, -D * 0.5f + post), new Vector3(sx > 0 ? W * 0.5f : -W * 0.5f + t, y1, D * 0.5f - post), 0);
            }
            // тёмные планки-ручки на торцах
            foreach (float sx in new[] { -1f, 1f })
                b.Box(new Vector3(sx > 0 ? W * 0.5f : -W * 0.5f - 0.012f, RH + H - 0.14f, -0.15f), new Vector3(sx > 0 ? W * 0.5f + 0.012f : -W * 0.5f, RH + H - 0.1f, 0.15f), 1);
            return b.Build("SupplyCrate_Body");
        }

        static Mesh CrateLid(float W, float D)
        {
            // в осях петли: z от 0 (петля, задняя кромка) до D, y от 0 до 0.03
            var b = new MB(2);
            const float gap = 0.006f;
            int n = Mathf.Max(4, Mathf.RoundToInt(W / 0.3f));
            float pw = (W - (n - 1) * gap) / n;
            for (int i = 0; i < n; i++) { float x0 = -W * 0.5f + i * (pw + gap); b.Box(new Vector3(x0, 0f, 0f), new Vector3(x0 + pw, 0.03f, D), 0); }
            foreach (float sx in new[] { -1f, 1f })
                b.Box(new Vector3(sx * (W * 0.5f - 0.1f) - 0.04f, 0.03f, 0.02f), new Vector3(sx * (W * 0.5f - 0.1f) + 0.04f, 0.055f, D - 0.02f), 1);
            // петли
            foreach (float sx in new[] { -0.3f * W, 0f, 0.3f * W })
                b.Box(new Vector3(sx - 0.05f, -0.012f, -0.012f), new Vector3(sx + 0.05f, 0.035f, 0.02f), 1);
            return b.Build("SupplyCrate_Lid");
        }

        static Mesh PlatformMesh(Vector2 size, float h)
        {
            var b = new MB(3);                                                  // 0 сталь, 1 жёлтый, 2 тёмный
            float X = size.x * 0.5f, Z = size.y * 0.5f;
            b.Box(new Vector3(-X, 0.06f, -Z), new Vector3(X, h, Z), 0);         // настил
            for (int i = -1; i <= 1; i++) b.Box(new Vector3(-X + 0.1f, 0f, i * (Z - 0.3f) - 0.1f), new Vector3(X - 0.1f, 0.06f, i * (Z - 0.3f) + 0.1f), 2);   // балки снизу
            // предупредительная полоса по кромке сверху
            const float lw = 0.09f, up = 0.003f;
            b.Box(new Vector3(-X, h, -Z), new Vector3(X, h + up, -Z + lw), 1);
            b.Box(new Vector3(-X, h, Z - lw), new Vector3(X, h + up, Z), 1);
            b.Box(new Vector3(-X, h, -Z), new Vector3(-X + lw, h + up, Z), 1);
            b.Box(new Vector3(X - lw, h, -Z), new Vector3(X, h + up, Z), 1);
            // «зебра» на боковых гранях
            const float seg = 0.3f, th = 0.004f;
            int k = 0;
            for (float x = -X; x < X - 1e-3f; x += seg, k++)
            {
                float x1 = Mathf.Min(X, x + seg);
                int sub = (k & 1) == 0 ? 1 : 2;
                b.Box(new Vector3(x, 0.07f, -Z - th), new Vector3(x1, h - 0.01f, -Z), sub);
                b.Box(new Vector3(x, 0.07f, Z), new Vector3(x1, h - 0.01f, Z + th), sub);
            }
            k = 0;
            for (float z = -Z; z < Z - 1e-3f; z += seg, k++)
            {
                float z1 = Mathf.Min(Z, z + seg);
                int sub = (k & 1) == 0 ? 1 : 2;
                b.Box(new Vector3(-X - th, 0.07f, z), new Vector3(-X, h - 0.01f, z1), sub);
                b.Box(new Vector3(X, 0.07f, z), new Vector3(X + th, h - 0.01f, z1), sub);
            }
            // проушины под стропы крана по углам
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    Vector3 c = new Vector3(sx * (X - 0.12f), h, sz * (Z - 0.12f));
                    b.Box(c + new Vector3(-0.06f, 0f, -0.015f), c + new Vector3(-0.03f, 0.16f, 0.015f), 2);
                    b.Box(c + new Vector3(0.03f, 0f, -0.015f), c + new Vector3(0.06f, 0.16f, 0.015f), 2);
                    b.Box(c + new Vector3(-0.06f, 0.13f, -0.015f), c + new Vector3(0.06f, 0.16f, 0.015f), 2);
                }
            return b.Build("SupplyPlatform_Deck");
        }

        // ───────────────────────── помощники ─────────────────────────

        static void Box(GameObject go, Vector3 center, Vector3 size)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.center = center; bc.size = size;
        }

        static Mesh Save(Mesh m, string name, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".asset";
            m.name = name;
            Mesh have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null) { EditorUtility.CopySerialized(m, have); Object.DestroyImmediate(m); EditorUtility.SetDirty(have); log.AppendLine($"  ✔ меш {path} обновлён"); return have; }
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ меш {path}");
            return m;
        }

        static Material Mat(string name, Color c, float smooth, StringBuilder log, float metal = 0f)
        {
            string path = MatDir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ материал {path}");
            return m;
        }

        static Material EmblemMat(StringBuilder log)
        {
            string path = MatDir + "/Mat_SupplyEmblem.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) { log.AppendLine("  ⚠ нет шейдера URP/Unlit — знаки возьмут материал из кода"); return null; }
            m = new Material(sh) { name = "Mat_SupplyEmblem" };
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 2f);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ материал {path} (знаки: Unlit + отсечка по альфе)");
            return m;
        }

        static SupplyCatalog _cat;
        static SupplyCatalog Catalog(StringBuilder log)
        {
            if (_cat != null) return _cat;
            _cat = AssetDatabase.LoadAssetAtPath<SupplyCatalog>(CatalogPath);
            if (_cat != null) return _cat;
            Dir(ResDir);
            _cat = ScriptableObject.CreateInstance<SupplyCatalog>();
            AssetDatabase.CreateAsset(_cat, CatalogPath);
            log.AppendLine($"  ＋ каталог {CatalogPath}");
            return _cat;
        }

        static void Dir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Dir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void Done(StringBuilder log)
        {
            Debug.Log(log.ToString());
            EditorUtility.DisplayDialog("CatCooks — Доставка", log.Length > 2500 ? log.ToString(0, 2500) + "\n… (полностью — в Console)" : log.ToString(), "OK");
        }
    }
}
