#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using DS = CatCooks.Food.CatCooksDessertSetup;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Шоколад (v1.8.2):
    ///   5. Форма для плитки в сцену — стальной лоток с жёлтыми ручками и полосой «стройка»; плитка на один
    ///      цикл ковша (16 порций). Выдели ковш — подгоню размер под него. Компоненты: Cookware «Форма для шоколада»,
    ///      Inside, Mouth, Fill (Liquid + Solid), коллайдеры, Rigidbody, сеть, Chocolate Mold.
    ///   6. Плитка шоколада (объект для префаба) — меш плитки 4×2 квадратика, Box Collider, Rigidbody, сеть,
    ///      Food Piece «Шоколад» ×16, Chocolate Bar. Сохрани префабом руками → Spawnable Prefabs → Bar Prefab формы.
    ///   7. Проверить форму и плитку — ✔/⚠/✖, ничего не меняет.
    ///   8. Вид шоколада (v1.8.5) — текстуры с пузырьками/порами и картами нормалей, материалы пасты и
    ///      застывшего, меши с узором; формам в сцене — материалы; ковшу/вёдрам с шоколадом — Chocolate Surface Look.
    /// Префабы не создаются. Меши и материал — в Assets/Models/Dessert.
    /// </summary>
    public static class CatCooksChocolateMoldSetup
    {
        const string Paste = "Шоколадная паста", Choc = "Шоколад";
        const int FullPortions = 16;

        private static Vector3 At() => SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        /// <summary>Размер плитки: под выделенный ковш (≈55% его ширины) или по умолчанию 0.36 м.</summary>
        private static void BarDims(out float L, out float W, out float H, StringBuilder log)
        {
            GameObject sel = Selection.activeGameObject;
            Cookware bucket = sel != null ? sel.GetComponentInParent<Cookware>() : null;
            if (bucket != null && bucket.GetComponent<ChocolateMold>() == null)
            {
                Bounds b = new Bounds(bucket.transform.position, Vector3.zero);
                bool any = false;
                foreach (Renderer r in bucket.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                L = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.55f, 0.12f, 1.4f);
                log.AppendLine($"Под ковш «{bucket.transform.root.name}» ({Mathf.Max(b.size.x, b.size.z):0.00} м): плитка {L:0.00} м" +
                               (bucket.Def != null ? $" · ёмкость ковша {bucket.Def.Capacity} порций, форма — на {FullPortions} (один цикл пасты)" : ""));
            }
            else
            {
                L = 0.36f;
                log.AppendLine($"Ковш не выделен — плитка {L:0.00} м (выдели ковш и нажми снова — подгоню под него)");
            }
            W = L * 0.5f;
            H = L * 0.085f;
        }

        // ───────────────────────── 5. ФОРМА ─────────────────────────

        [MenuItem("CatCooks/Шоколад/5. Форма для плитки в сцену", priority = 74)]
        public static void MakeMold()
        {
            var log = new StringBuilder();
            BarDims(out float L, out float W, out float H, log);
            float c = W * 0.015f;                      // зазор плитки
            float t = W * 0.06f;                       // стенка
            float tb = t * 0.8f;                       // дно
            float D = H * 1.45f;                       // глубина полости
            float Li = L + 2f * c, Wi = W + 2f * c;
            float Lo = Li + 2f * t, Wo = Wi + 2f * t, Ho = tb + D;

            Color steel = new Color(0.66f, 0.69f, 0.73f, 0f), steelD = new Color(0.47f, 0.5f, 0.55f, 0f), inner = new Color(0.56f, 0.59f, 0.63f, 0f);
            Color yellow = new Color(0.99f, 0.76f, 0.12f, 0f), black = new Color(0.13f, 0.13f, 0.14f, 0f), bolt = new Color(0.36f, 0.38f, 0.42f, 0f);

            var vb = new QB();
            // дно и стенки
            vb.Box(new Vector3(0f, tb * 0.5f, 0f), new Vector3(Lo, tb, Wo), steelD);
            vb.Box(new Vector3(0f, tb + 0.0005f, 0f), new Vector3(Li, 0.001f, Wi), inner);                 // светлое дно полости
            vb.Box(new Vector3(0f, tb + D * 0.5f, Wo * 0.5f - t * 0.5f), new Vector3(Lo, D, t), steel);
            vb.Box(new Vector3(0f, tb + D * 0.5f, -Wo * 0.5f + t * 0.5f), new Vector3(Lo, D, t), steel);
            vb.Box(new Vector3(Lo * 0.5f - t * 0.5f, tb + D * 0.5f, 0f), new Vector3(t, D, Wi), steel);
            vb.Box(new Vector3(-Lo * 0.5f + t * 0.5f, tb + D * 0.5f, 0f), new Vector3(t, D, Wi), steel);
            // жёлтая кромка по верху
            float rim = t * 0.35f;
            vb.Box(new Vector3(0f, Ho + rim * 0.5f, Wo * 0.5f - t * 0.5f), new Vector3(Lo, rim, t * 1.02f), yellow);
            vb.Box(new Vector3(0f, Ho + rim * 0.5f, -Wo * 0.5f + t * 0.5f), new Vector3(Lo, rim, t * 1.02f), yellow);
            vb.Box(new Vector3(Lo * 0.5f - t * 0.5f, Ho + rim * 0.5f, 0f), new Vector3(t * 1.02f, rim, Wi), yellow);
            vb.Box(new Vector3(-Lo * 0.5f + t * 0.5f, Ho + rim * 0.5f, 0f), new Vector3(t * 1.02f, rim, Wi), yellow);
            // полоса «стройка» по длинным бокам: жёлто-чёрные плашки
            int seg = 12;
            float bandH = D * 0.32f, bandY = tb + D * 0.42f, out0 = t * 0.12f;
            for (int i = 0; i < seg; i++)
            {
                float x = -Lo * 0.5f + Lo * (i + 0.5f) / seg;
                Color col = (i & 1) == 0 ? yellow : black;
                vb.Box(new Vector3(x, bandY, Wo * 0.5f + out0 * 0.5f), new Vector3(Lo / seg, bandH, out0), col);
                vb.Box(new Vector3(x, bandY, -Wo * 0.5f - out0 * 0.5f), new Vector3(Lo / seg, bandH, out0), col);
            }
            // болты по углам
            float bs = t * 0.45f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    vb.Box(new Vector3(sx * (Lo * 0.5f - t * 0.9f), bandY + bandH * 0.5f + bs, sz * (Wo * 0.5f + bs * 0.25f)), new Vector3(bs, bs, bs * 0.5f), bolt);
            // ручки на торцах: скоба, жёлто-чёрная обмотка
            float hl = W * 0.16f, hw = Wi * 0.55f, hth = t * 0.9f, hy = tb + D * 0.7f;
            for (int s = -1; s <= 1; s += 2)
            {
                float x0 = s * Lo * 0.5f;
                vb.Box(new Vector3(x0 + s * hl * 0.5f, hy, hw * 0.5f - hth * 0.5f), new Vector3(hl, hth, hth), steelD);
                vb.Box(new Vector3(x0 + s * hl * 0.5f, hy, -hw * 0.5f + hth * 0.5f), new Vector3(hl, hth, hth), steelD);
                int wraps = 5;
                for (int i = 0; i < wraps; i++)
                {
                    float z = -hw * 0.5f + hw * (i + 0.5f) / wraps;
                    vb.Box(new Vector3(x0 + s * (hl + hth * 0.5f), hy, z), new Vector3(hth * 1.25f, hth * 1.25f, hw / wraps), (i & 1) == 0 ? yellow : black);
                }
            }
            Mesh mesh = DS.SaveMesh(vb.Build("ChocolateMold_Mesh"), "ChocolateMold_Mesh", log);
            Mesh liquid = DS.SaveMesh(LiquidMesh(L, W, H), "ChocolateMold_Liquid", log);
            Mesh barMesh = DS.SaveMesh(BarMesh(L, W, H), "ChocolateBar_Mesh", log);
            Material mat = DS.PlateMat(log);
            Material choc = ChocoMat(log);

            var go = new GameObject("ChocolateMold");
            Undo.RegisterCreatedObjectUndo(go, "ChocolateMold");
            go.transform.position = At() + Vector3.up * 0.1f;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            Transform cols = DS.FindOrCreate(go.transform, "MoldColliders");
            ColBox(cols, "Bottom", new Vector3(0f, tb * 0.5f, 0f), new Vector3(Lo, tb, Wo));
            ColBox(cols, "WallN", new Vector3(0f, tb + D * 0.5f, Wo * 0.5f - t * 0.5f), new Vector3(Lo, D, t));
            ColBox(cols, "WallS", new Vector3(0f, tb + D * 0.5f, -Wo * 0.5f + t * 0.5f), new Vector3(Lo, D, t));
            ColBox(cols, "WallE", new Vector3(Lo * 0.5f - t * 0.5f, tb + D * 0.5f, 0f), new Vector3(t, D, Wi));
            ColBox(cols, "WallW", new Vector3(-Lo * 0.5f + t * 0.5f, tb + D * 0.5f, 0f), new Vector3(t, D, Wi));
            ColBox(cols, "HandleE", new Vector3(Lo * 0.5f + hl * 0.5f + hth * 0.4f, hy, 0f), new Vector3(hl + hth, hth * 1.3f, hw));
            ColBox(cols, "HandleW", new Vector3(-Lo * 0.5f - hl * 0.5f - hth * 0.4f, hy, 0f), new Vector3(hl + hth, hth * 1.3f, hw));

            Transform inside = DS.FindOrCreate(go.transform, "Inside");
            inside.localPosition = new Vector3(0f, tb + D * 0.65f, 0f);
            BoxCollider ib = DS.Need<BoxCollider>(inside.gameObject);
            ib.isTrigger = true; ib.size = new Vector3(Li, D * 1.3f, Wi); ib.center = Vector3.zero;
            Transform mouth = DS.FindOrCreate(go.transform, "Mouth");
            mouth.localPosition = new Vector3(0f, Ho, 0f);

            Transform fill = DS.FindOrCreate(go.transform, "Fill");
            fill.localPosition = new Vector3(0f, tb + 0.001f, 0f);
            fill.localRotation = Quaternion.identity; fill.localScale = Vector3.one;
            Transform lq = Child(fill, "Liquid", liquid, PasteMat(log));
            Transform sd = Child(fill, "Solid", barMesh, choc);
            sd.GetComponent<MeshRenderer>().enabled = false;
            lq.GetComponent<MeshRenderer>().enabled = false;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(L * W * 30f, 0.6f, 12f); rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            Cookware cw = go.AddComponent<Cookware>();
            var cso = new SerializedObject(cw);
            DS.SetObj(cso, "Def", MoldDef(log));
            SerializedProperty cr = cso.FindProperty("_cooksRecipes"); if (cr != null) cr.boolValue = false;
            DS.SetObj(cso, "_insideVolume", ib);
            DS.SetObj(cso, "_mouth", mouth);
            cso.ApplyModifiedProperties();
            ChocolateMold mold = go.AddComponent<ChocolateMold>();
            var mso = new SerializedObject(mold);
            DS.SetObj(mso, "_paste", DS.Find(Paste));
            DS.SetObj(mso, "_choc", DS.Find(Choc));
            DS.SetObj(mso, "_fill", fill);
            DS.SetInt(mso, "_fullPortions", FullPortions);
            GameObject barPrefab = FindBarPrefab();
            if (barPrefab != null) DS.SetObj(mso, "_barPrefab", barPrefab);
            mso.ApplyModifiedProperties();

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"＋ «ChocolateMold» {Lo:0.00}×{Wo:0.00}×{Ho + rim:0.00} м: стальной лоток, жёлтая кромка, жёлто-чёрная полоса, болты, ручки с обмоткой");
            log.AppendLine($"  полость под плитку {L:0.00}×{W:0.00}×{H:0.000} м = {FullPortions} порций (один цикл пасты в ковше), тара на {MoldDef(log).Capacity} — с запасом");
            log.AppendLine("  компоненты: Cookware «Форма для шоколада» (не глотает, без рецептов), Inside (триггер), Mouth, Fill/Liquid + Fill/Solid, коллайдеры, Rigidbody, сеть, Chocolate Mold");
            if (DS.Find(Paste) == null) log.AppendLine("  ⚠ нет «Шоколадная паста» — сначала Шоколад → 1 (продукты найдутся и сами, по названию)");
            log.AppendLine(barPrefab != null ? $"  ✔ Bar Prefab = «{barPrefab.name}»"
                                             : "  ⚠ Bar Prefab пуст — меню 6 (плитка), сохрани префабом, добавь в Spawnable Prefabs и перетащи в Chocolate Mold → Bar Prefab");
            log.AppendLine("\nДАЛЬШЕ: перетащи ChocolateMold в Project → префаб (и в Spawnable Prefabs, если спавнишь) или оставь в сцене.\n" +
                           "Слой: если у тары свой слой — поставь его ChocolateMold со всеми детьми (как у ковша).");
            DS.Done("Форма для шоколада", log);
        }

        private static CookwareDef MoldDef(StringBuilder log)
        {
            string path = $"{DS.DefDir}/Cookware_ChocolateMold.asset";
            CookwareDef d = AssetDatabase.LoadAssetAtPath<CookwareDef>(path);
            if (d != null) return d;
            DS.EnsureDir(DS.DefDir);
            d = ScriptableObject.CreateInstance<CookwareDef>();
            d.Title = "Форма для шоколада"; d.Kind = CookwareKind.Storage; d.Capacity = FullPortions + 2;
            d.HeatProcess = FoodProcess.None; d.NeedsHeatSource = true; d.Absorbs = false; d.BlendsColor = false;
            AssetDatabase.CreateAsset(d, path);
            log.AppendLine($"  ＋ тара {path}: «Форма для шоколада», {d.Capacity} порций, не глотает");
            return d;
        }

        // ───────────────────────── 6. ПЛИТКА ─────────────────────────

        [MenuItem("CatCooks/Шоколад/6. Плитка шоколада (объект для префаба)", priority = 75)]
        public static void MakeBar()
        {
            var log = new StringBuilder();
            ProductDef choc = DS.Find(Choc);
            if (choc == null) { EditorUtility.DisplayDialog("Плитка шоколада", "Сначала «Шоколад → 1. Создать продукты и рецепты».", "Ок"); return; }
            float L, W, H;
            ChocolateMold mold = Object.FindFirstObjectByType<ChocolateMold>();
            MeshFilter solid = mold != null ? mold.transform.Find("Fill/Solid")?.GetComponent<MeshFilter>() : null;
            Mesh mesh;
            if (solid != null && solid.sharedMesh != null)
            {
                mesh = solid.sharedMesh;
                Vector3 s = mesh.bounds.size;
                L = s.x; W = s.z; H = s.y;
                log.AppendLine($"Плитка — как в форме «{mold.name}»: {L:0.00}×{W:0.00} м");
            }
            else
            {
                BarDims(out L, out W, out H, log);
                mesh = DS.SaveMesh(BarMesh(L, W, H), "ChocolateBar_Mesh", log);
            }

            var go = new GameObject("ChocolateBar");
            Undo.RegisterCreatedObjectUndo(go, "ChocolateBar");
            go.transform.position = At() + Vector3.up * 0.3f;
            Child(go.transform, "Mesh", mesh, ChocoMat(log));
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(L, H, W); bc.center = new Vector3(0f, H * 0.5f, 0f);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(L * W * H * 1200f, 0.15f, 5f); rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            FoodPiece fp = go.AddComponent<FoodPiece>();
            var so = new SerializedObject(fp);
            DS.SetObj(so, "_product", choc);
            DS.SetInt(so, "_count", FullPortions);
            so.ApplyModifiedProperties();
            ChocolateBar bar = go.AddComponent<ChocolateBar>();
            bar.FullPortions = FullPortions;
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"＋ «ChocolateBar» {L:0.00}×{W:0.00}×{H:0.000} м: 4×2 квадратика, Mesh, Box Collider, Rigidbody, сеть, Food Piece «{Choc}» ×{FullPortions}, Chocolate Bar");
            log.AppendLine("\nДАЛЬШЕ РУКАМИ:\n1. Перетащи ChocolateBar в Project → префаб ChocolateBar.\n2. Добавь его в Spawnable Prefabs (NetworkManager → DefaultPrefabObjects).\n" +
                           "3. Chocolate Mold → Bar Prefab = ChocolateBar.\n4. Удали ChocolateBar из сцены.\n" +
                           "Слой: если у еды свой слой (Food) — поставь его плитке со всеми детьми.");
            DS.Done("Плитка шоколада", log);
        }

        // ───────────────────────── 7. ПРОВЕРКА ─────────────────────────

        private static StringBuilder _sb;
        private static int _err, _warn;
        private static void Ok(string t) => _sb.AppendLine("  ✔ " + t);
        private static void Wr(string t) { _warn++; _sb.AppendLine("  ⚠ " + t); }
        private static void Er(string t) { _err++; _sb.AppendLine("  ✖ " + t); }

        [MenuItem("CatCooks/Шоколад/7. Проверить форму и плитку", priority = 76)]
        public static void Check()
        {
            _sb = new StringBuilder(); _err = 0; _warn = 0;
            _sb.AppendLine("■ Продукты");
            ProductDef paste = DS.Find(Paste), choc = DS.Find(Choc);
            if (paste == null) Er($"нет «{Paste}» → Шоколад → 1"); else Ok($"«{Paste}» Id {paste.Id}");
            if (choc == null) Er($"нет «{Choc}» → Шоколад → 1"); else Ok($"«{Choc}» Id {choc.Id}" + (choc.Solid ? " (твёрдый)" : ""));
            if (paste != null)
            {
                if (paste.HardensInto != choc) Er($"у «{Paste}» Hardens Into не «{Choc}» — не застынет и не будет таять обратно (Шоколад → 1)");
                else Ok($"застывает за {paste.HardenSeconds:0} с, тает от горелки/огня (≥ {FoodHardening.MeltFromC:0} °C) за {FoodHardening.MeltSeconds:0} с");
            }

            Material pm = AssetDatabase.LoadAssetAtPath<Material>(DS.ModelDir + "/Mat_ChocolatePaste.mat");
            if (pm == null || !pm.HasProperty("_BumpMap") || pm.GetTexture("_BumpMap") == null) Wr("нет материала пасты с пузырьками (Mat_ChocolatePaste) — меню 8");
            else Ok("вид шоколада: паста с пузырьками, застывший пористый (меню 8 сделано)");

            _sb.AppendLine("\n■ Форма в сцене");
            ChocolateMold[] molds = Object.FindObjectsByType<ChocolateMold>(FindObjectsSortMode.None);
            if (molds.Length == 0) Wr("в сцене нет формы — меню 5 (или перетащи префаб формы)");
            foreach (ChocolateMold m in molds)
            {
                int e0 = _err;
                Cookware cw = m.GetComponent<Cookware>();
                if (cw == null || cw.Def == null) Er($"«{m.name}»: нет Cookware / Def");
                else if (cw.Def.Absorbs) Wr($"«{m.name}»: тара глотает предметы — плитка будет проваливаться обратно в форму");
                var so = new SerializedObject(m);
                if (cw != null && new SerializedObject(cw).FindProperty("_insideVolume")?.objectReferenceValue == null) Er($"«{m.name}»: у Cookware нет Inside Volume — мастерок не попадёт");
                Transform fill = so.FindProperty("_fill").objectReferenceValue as Transform ?? m.transform.Find("Fill");
                if (fill == null) Er($"«{m.name}»: нет Fill — шоколад в форме не видно");
                else if (fill.Find("Liquid") == null || fill.Find("Solid") == null) Wr($"«{m.name}»: в Fill нет Liquid/Solid — паста и плитка будут одним мешем");
                GameObject bp = so.FindProperty("_barPrefab").objectReferenceValue as GameObject;
                if (bp == null) Er($"«{m.name}»: Bar Prefab пуст — F не достанет плитку (меню 6 → префаб → сюда)");
                else
                {
                    if (PrefabUtility.GetPrefabAssetType(bp) == PrefabAssetType.NotAPrefab || !AssetDatabase.Contains(bp)) Er($"«{m.name}»: Bar Prefab «{bp.name}» — объект из сцены, нужен префаб из Project");
                    FoodPiece fp = bp.GetComponent<FoodPiece>();
                    if (fp == null) Er($"«{bp.name}»: нет Food Piece");
                    else
                    {
                        var pp = new SerializedObject(fp).FindProperty("_product").objectReferenceValue as ProductDef;
                        if (pp == null || pp.Title != Choc) Er($"«{bp.name}»: Food Piece → Product должен быть «{Choc}»");
                    }
                    if (bp.GetComponent<FishNet.Object.NetworkObject>() == null) Er($"«{bp.name}»: нет Network Object");
                    if (bp.GetComponent<ChocolateBar>() == null) Wr($"«{bp.name}»: нет Chocolate Bar — толщина плитки не зависит от порций");
                    Wr($"«{bp.name}»: проверь, что он в Spawnable Prefabs (иначе FishNet не заспавнит)");
                }
                if (_err == e0) Ok($"«{m.name}»: форма на {so.FindProperty("_fullPortions").intValue} порций, тара на {cw.Def.Capacity}");
            }

            _sb.AppendLine("\n■ Чем топить");
            int heats = 0;
            foreach (CookwareHeat h in Object.FindObjectsByType<CookwareHeat>(FindObjectsSortMode.None))
            {
                heats++;
                Cookware cw = h.GetComponent<Cookware>();
                bool absorbs = cw != null && cw.Def != null && cw.Def.Absorbs;
                if (absorbs) Ok($"«{h.transform.root.name}»: с нагревом и глотает предметы — кинь плитку, погрей горелкой/на огне");
                if (h.GetComponentInParent<ChocolateSurfaceLook>() == null && h.GetComponentInChildren<ChocolateSurfaceLook>() == null)
                    Wr($"«{h.transform.root.name}»: нет Chocolate Surface Look — шоколад в ней будет блестеть как масло (меню 8)");
                else Wr($"«{h.transform.root.name}»: с нагревом, но не глотает предметы — плитка в неё не уйдёт");
            }
            if (heats == 0) Wr("в сцене нет тары с Cookware Heat (ковш) — плитку растопить негде (форма тает только от горелки напрямую)");

            string head = _err > 0 ? $"ОШИБОК {_err}, предупреждений {_warn}" : _warn > 0 ? $"ошибок нет, предупреждений {_warn}" : "всё в порядке";
            Debug.Log("[Форма и плитка — проверка] " + head + "\n" + _sb);
            EditorUtility.DisplayDialog("Форма и плитка — проверка", head + "\n" + _sb, "Ок");
        }

        // ───────────────────────── 8. ВИД ШОКОЛАДА ─────────────────────────

        [MenuItem("CatCooks/Шоколад/8. Вид шоколада: текстуры, форма, ковш", priority = 77)]
        public static void MakeLook()
        {
            var log = new StringBuilder();
            log.AppendLine("■ Текстуры и материалы");
            Material paste = PasteMat(log), solidSurf = SolidSurfaceMat(log), bar = ChocoMat(log);

            log.AppendLine("\n■ Меши с узором (пузырьки/поры видны только там, где альфа вершин = 1)");
            RegenMesh("ChocolateBar_Mesh", true, log);
            RegenMesh("ChocolateMold_Liquid", false, log);

            log.AppendLine("\n■ Продукты (цвет в ковше)");
            ProductDef pp = DS.Find(Paste), cp = DS.Find(Choc);
            if (pp != null) { pp.BaseColor = new Color(0.47f, 0.27f, 0.14f, 1f); EditorUtility.SetDirty(pp); log.AppendLine($"  ✔ «{Paste}»: тёплый молочно-коричневый"); }
            else log.AppendLine($"  ✖ нет «{Paste}» — Шоколад → 1");
            if (cp != null) { cp.BaseColor = new Color(0.27f, 0.14f, 0.075f, 1f); EditorUtility.SetDirty(cp); log.AppendLine($"  ✔ «{Choc}»: тёмный горький"); }

            log.AppendLine("\n■ Формы в сцене");
            int molds = 0;
            foreach (ChocolateMold m in Object.FindObjectsByType<ChocolateMold>(FindObjectsSortMode.None))
            {
                molds++;
                Transform lq = m.transform.Find("Fill/Liquid"), sd = m.transform.Find("Fill/Solid");
                if (lq != null && lq.GetComponent<MeshRenderer>() != null) { Undo.RecordObject(lq.GetComponent<MeshRenderer>(), "choc"); lq.GetComponent<MeshRenderer>().sharedMaterial = paste; }
                if (sd != null && sd.GetComponent<MeshRenderer>() != null) { Undo.RecordObject(sd.GetComponent<MeshRenderer>(), "choc"); sd.GetComponent<MeshRenderer>().sharedMaterial = bar; }
                EditorUtility.SetDirty(m.gameObject);
                log.AppendLine(lq != null && sd != null ? $"  ✔ «{m.name}»: Liquid = {paste.name}, Solid = {bar.name}"
                                                        : $"  ⚠ «{m.name}»: нет Fill/Liquid или Fill/Solid — пересоздай форму меню 5");
            }
            if (molds == 0) log.AppendLine("  ⚠ в сцене нет формы — перетащи префаб формы в сцену и нажми снова");

            log.AppendLine("\n■ Тара с шоколадом (ковш, вёдра): Chocolate Surface Look");
            // выделенная тара — сначала шоколад в её LiquidAnchor (как меню 2), чтобы точно попала в список
            foreach (GameObject sel in Selection.gameObjects)
                foreach (CookwareLiquidVisual slv in sel.GetComponentsInChildren<CookwareLiquidVisual>(true))
                {
                    EnsureEntry(slv, pp, log);
                    EnsureEntry(slv, cp, log);
                }
            int cws = 0;
            foreach (CookwareLiquidVisual lv in Object.FindObjectsByType<CookwareLiquidVisual>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(lv);
                SerializedProperty arr = so.FindProperty("_products");
                bool has = false;
                for (int i = 0; arr != null && i < arr.arraySize; i++)
                {
                    var p = arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue as ProductDef;
                    if (p != null && (p == pp || p == cp)) has = true;
                }
                if (!has) continue;
                Cookware cw = lv.GetComponentInParent<Cookware>();
                GameObject host = cw != null ? cw.gameObject : lv.gameObject;
                ChocolateSurfaceLook look = host.GetComponent<ChocolateSurfaceLook>();
                if (look == null) look = Undo.AddComponent<ChocolateSurfaceLook>(host);
                look.EditorSetup(paste, solidSurf);
                EditorUtility.SetDirty(host);
                cws++;
                log.AppendLine($"  ✔ «{host.name}»: Chocolate Surface Look (паста — {paste.name}, застывший — {solidSurf.name})");
            }
            if (cws == 0) log.AppendLine("  ⚠ нет тары, где в LiquidAnchor есть шоколад — выдели ковш и нажми 8 ещё раз");

            AssetDatabase.SaveAssets();
            if (molds + cws > 0) EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            log.AppendLine("\nДАЛЬШЕ: у форм и ковша в сцене — Overrides → Apply All (если они префабы), сохрани сцену.\n" +
                           "Плитка (префаб ChocolateBar) берёт новый материал и меш сама — ассеты обновлены на месте.");
            DS.Done("Вид шоколада", log);
        }

        private static void EnsureEntry(CookwareLiquidVisual lv, ProductDef p, StringBuilder log)
        {
            if (lv == null || p == null) return;
            var so = new SerializedObject(lv);
            SerializedProperty arr = so.FindProperty("_products");
            if (arr == null) return;
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue == p) return;
            arr.arraySize++;
            SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            e.FindPropertyRelative("Product").objectReferenceValue = p;
            e.FindPropertyRelative("Role").intValue = (int)CookwareLiquidVisual.Role.Paste;
            e.FindPropertyRelative("Color").colorValue = new Color(0, 0, 0, 0);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(lv);
            log.AppendLine($"  ＋ «{lv.transform.root.name}/{lv.name}»: «{p.Title}» — густая паста");
        }

        /// <summary>Перестроить меш плитки/глади с узором, тех же размеров (ассет обновляется на месте — ссылки целы).</summary>
        private static void RegenMesh(string name, bool bar, StringBuilder log)
        {
            Mesh have = AssetDatabase.LoadAssetAtPath<Mesh>(DS.ModelDir + "/" + name + ".asset");
            if (have == null) { log.AppendLine($"  — {name}: ещё нет (сделается меню 5/6)"); return; }
            Vector3 sz = have.bounds.size;
            if (sz.x <= 0f || sz.y <= 0f || sz.z <= 0f) { log.AppendLine($"  ⚠ {name}: пустой меш"); return; }
            DS.SaveMesh(bar ? BarMesh(sz.x, sz.z, sz.y) : LiquidMesh(sz.x, sz.z, sz.y), name, log);
        }

        // ───────────────────────── помощники ─────────────────────────

        private static GameObject FindBarPrefab()
        {
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null && p.GetComponent<ChocolateBar>() != null) return p;
            }
            return null;
        }

        private static void ColBox(Transform h, string n, Vector3 c, Vector3 s)
        {
            Transform x = DS.FindOrCreate(h, n);
            x.localPosition = c; x.localRotation = Quaternion.identity; x.localScale = Vector3.one;
            DS.Need<BoxCollider>(x.gameObject).size = s;
        }

        private static Transform Child(Transform parent, string n, Mesh mesh, Material mat)
        {
            Transform t = DS.FindOrCreate(parent, n);
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            DS.Need<MeshFilter>(t.gameObject).sharedMesh = mesh;
            DS.Need<MeshRenderer>(t.gameObject).sharedMaterial = mat;
            return t;
        }

        /// <summary>Застывший шоколад (плитка, «Solid» в форме): тёмный, матовый, пористый. С обводкой.</summary>
        internal static Material ChocoMat(StringBuilder log) => StyledMat("Mat_ChocolateBar", false, 0.004f, log);
        /// <summary>Жидкая паста (форма, ковш): тёплая, с пузырьками, мягкий влажный блик. Без обводки.</summary>
        internal static Material PasteMat(StringBuilder log) => StyledMat("Mat_ChocolatePaste", true, 0f, log);
        /// <summary>Застывший шоколад на поверхности в ковше: как плитка, но без обводки.</summary>
        internal static Material SolidSurfaceMat(StringBuilder log) => StyledMat("Mat_ChocolateSolidSurface", false, 0f, log);

        private static Material StyledMat(string name, bool liquid, float outline, StringBuilder log)
        {
            string path = DS.ModelDir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool made = m == null;
            Shader sh = Shader.Find("CatCooks/Food Heap");
            if (made)
            {
                DS.EnsureDir(DS.ModelDir);
                if (sh == null) { sh = Shader.Find("Universal Render Pipeline/Lit"); log.AppendLine($"  ⚠ нет шейдера CatCooks/Food Heap — {name} на URP/Lit"); }
                m = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (sh != null && m.shader != sh) m.shader = sh;
            Texture2D albedo = liquid ? PasteTexture(log) : SolidTexture(log);
            Texture2D normal = liquid ? PasteNormal(log) : SolidNormal(log);
            Color c = liquid ? new Color(0.47f, 0.27f, 0.14f) : new Color(0.27f, 0.14f, 0.075f);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", normal);
            if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", liquid ? 0.55f : 1.1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_TexScale")) m.SetFloat("_TexScale", liquid ? 7f : 9f);
            if (m.HasProperty("_TexDetail")) m.SetFloat("_TexDetail", 1f);
            if (m.HasProperty("_DetailBoost")) m.SetFloat("_DetailBoost", liquid ? 1.12f : 1.18f);
            if (m.HasProperty("_UseMeshUV")) m.SetFloat("_UseMeshUV", 0f);
            if (m.HasProperty("_GlintStrength")) m.SetFloat("_GlintStrength", liquid ? 0.3f : 0.05f);
            if (m.HasProperty("_GlintSize")) m.SetFloat("_GlintSize", liquid ? 0.965f : 0.9f);
            if (m.HasProperty("_Saturation")) m.SetFloat("_Saturation", 1.1f);
            if (m.HasProperty("_Brightness")) m.SetFloat("_Brightness", liquid ? 1.12f : 1.05f);
            if (m.HasProperty("_Rim")) m.SetFloat("_Rim", liquid ? 0.12f : 0.05f);
            if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", outline);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", liquid ? 0.55f : 0.15f);
            EditorUtility.SetDirty(m);
            log.AppendLine(made ? $"  ＋ материал {path}" : $"  ✔ материал {path} обновлён");
            return m;
        }

        // ── текстуры шоколада (256×256, бесшовные) ──
        // Цвет даёт материал/вершины, текстура — светлота (Food Heap берёт яркость) и рельеф (карта нормалей).

        private const int TexN = 256;

        /// <summary>Бесшовная волна: сумма синусов с целыми частотами (−1..1).</summary>
        private static readonly Dictionary<int, Vector4[]> _waves = new Dictionary<int, Vector4[]>();

        private static float Wave(float u, float v, int seed, int terms, int kMin, int kMax)
        {
            int key = seed * 7919 + terms * 131 + kMin * 17 + kMax;
            if (!_waves.TryGetValue(key, out Vector4[] w))
            {
                var rnd = new System.Random(seed);
                w = new Vector4[terms];
                for (int i = 0; i < terms; i++)
                {
                    int kx = rnd.Next(kMin, kMax + 1) * (rnd.Next(2) == 0 ? 1 : -1);
                    int ky = rnd.Next(kMin, kMax + 1) * (rnd.Next(2) == 0 ? 1 : -1);
                    w[i] = new Vector4(kx, ky, (float)rnd.NextDouble() * 6.2832f, 1f / (1f + i * 0.35f));
                }
                _waves[key] = w;
            }
            float sum = 0f, amp = 0f;
            for (int i = 0; i < w.Length; i++)
            {
                sum += w[i].w * Mathf.Sin(6.2832f * (w[i].x * u + w[i].y * v) + w[i].z);
                amp += w[i].w;
            }
            return sum / Mathf.Max(1e-4f, amp);
        }

        /// <summary>
        /// Пузырьки по ячейкам (бесшовно): для каждой точки — ближайший пузырь, d = расстояние/радиус.
        /// dome — купол над поверхностью (жидкая паста), иначе — пора-ямка (застывший).
        /// </summary>
        private static void Bubbles(float[] h, float[] lum, int cells, float rMin, float rMax, float chance, bool dome, float depth, float darkPit, int seed)
        {
            var rnd = new System.Random(seed);
            int nc = cells * cells;
            var cx = new float[nc]; var cy = new float[nc]; var cr = new float[nc];
            for (int i = 0; i < nc; i++)
            {
                cx[i] = (float)rnd.NextDouble(); cy[i] = (float)rnd.NextDouble();
                cr[i] = rnd.NextDouble() < chance ? Mathf.Lerp(rMin, rMax, (float)rnd.NextDouble()) : 0f;
            }
            float cell = TexN / (float)cells;
            for (int y = 0; y < TexN; y++)
                for (int x = 0; x < TexN; x++)
                {
                    float fx = x / cell, fy = y / cell;
                    int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
                    float best = 9f;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int gx = ix + dx, gy = iy + dy;
                            int id = (((gy % cells) + cells) % cells) * cells + (((gx % cells) + cells) % cells);
                            if (cr[id] <= 0f) continue;
                            float px = gx + cx[id], py = gy + cy[id];
                            float d = Mathf.Sqrt((fx - px) * (fx - px) + (fy - py) * (fy - py)) / cr[id];
                            if (d < best) best = d;
                        }
                    int k = y * TexN + x;
                    if (best < 1f)
                    {
                        float s = Mathf.Sqrt(1f - best * best);
                        if (dome)
                        {
                            h[k] += depth * s;
                            lum[k] *= 1f + 0.08f * s - 0.12f * Mathf.Pow(best, 6f);      // светлая макушка, тёмный ободок
                        }
                        else
                        {
                            h[k] -= depth * Mathf.Pow(s, 0.7f);
                            lum[k] *= Mathf.Lerp(darkPit, 1f, best * best * best);   // тёмное дно поры
                        }
                    }
                    else if (!dome && best < 1.22f) lum[k] *= 1f + 0.07f * (1.22f - best) / 0.22f;     // светлый край поры
                }
        }

        private static void PasteFields(out float[] h, out float[] lum)
        {
            h = new float[TexN * TexN]; lum = new float[TexN * TexN];
            for (int y = 0; y < TexN; y++)
                for (int x = 0; x < TexN; x++)
                {
                    float u = x / (float)TexN, v = y / (float)TexN;
                    float swirl = Wave(u, v, 11, 5, 1, 3);
                    // разводы, как когда паста стекает и перемешивается: светлые и тёмные прожилки
                    float marble = Mathf.Sin(6.2832f * (2f * u + v) + swirl * 3.2f);
                    float fine = Wave(u, v, 23, 6, 5, 11);
                    float big = Wave(u, v, 29, 4, 1, 2);                   // крупные пятна светлее/темнее
                    int k = y * TexN + x;
                    lum[k] = 0.85f + 0.04f * marble + 0.025f * fine + 0.05f * big;
                    h[k] = 0.12f * swirl + 0.04f * marble + 0.1f * big;
                }
            Bubbles(h, lum, 16, 0.12f, 0.3f, 0.3f, true, 0.8f, 1f, 37);       // редкие пузырьки на глади
            Bubbles(h, lum, 34, 0.12f, 0.26f, 0.2f, true, 0.4f, 1f, 41);      // мелочь
        }

        private static void SolidFields(out float[] h, out float[] lum)
        {
            h = new float[TexN * TexN]; lum = new float[TexN * TexN];
            var rnd = new System.Random(5);
            for (int y = 0; y < TexN; y++)
                for (int x = 0; x < TexN; x++)
                {
                    float u = x / (float)TexN, v = y / (float)TexN;
                    float mott = Wave(u, v, 53, 5, 2, 5);                 // пятна темнее/светлее — какао неравномерно
                    float grain = (float)rnd.NextDouble() - 0.5f;          // сухое зерно
                    int k = y * TexN + x;
                    lum[k] = 0.83f + 0.06f * mott + 0.035f * grain;
                    h[k] = 0.18f * mott + 0.05f * grain;
                }
            Bubbles(h, lum, 12, 0.22f, 0.44f, 0.75f, false, 1.2f, 0.42f, 61);   // крупные поры (пористый шоколад)
            Bubbles(h, lum, 28, 0.18f, 0.40f, 0.65f, false, 0.7f, 0.55f, 67);   // средние
            Bubbles(h, lum, 60, 0.2f, 0.42f, 0.5f, false, 0.35f, 0.7f, 71);     // мелкие
        }

        private static Texture2D PasteTexture(StringBuilder log) { PasteFields(out _, out float[] l); return SaveLum("Tex_ChocolatePaste", l, log); }
        private static Texture2D PasteNormal(StringBuilder log) { PasteFields(out float[] h, out _); return SaveNormal("Tex_ChocolatePaste_N", h, 2.2f, log); }
        private static Texture2D SolidTexture(StringBuilder log) { SolidFields(out _, out float[] l); return SaveLum("Tex_ChocolateSolid", l, log); }
        private static Texture2D SolidNormal(StringBuilder log) { SolidFields(out float[] h, out _); return SaveNormal("Tex_ChocolateSolid_N", h, 3f, log); }

        private static Texture2D SaveLum(string name, float[] lum, StringBuilder log)
        {
            string path = DS.ModelDir + "/" + name + ".png";
            Texture2D have = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (have != null) return have;
            var px = new Color32[TexN * TexN];
            for (int i = 0; i < px.Length; i++)
            {
                byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(lum[i]) * 255f);
                px[i] = new Color32(b, b, b, 255);
            }
            return WritePng(path, px, false, log);
        }

        private static Texture2D SaveNormal(string name, float[] h, float strength, StringBuilder log)
        {
            string path = DS.ModelDir + "/" + name + ".png";
            Texture2D have = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (have != null) return have;
            var px = new Color32[TexN * TexN];
            for (int y = 0; y < TexN; y++)
                for (int x = 0; x < TexN; x++)
                {
                    float hl = h[y * TexN + (x + TexN - 1) % TexN], hr = h[y * TexN + (x + 1) % TexN];
                    float hd = h[((y + TexN - 1) % TexN) * TexN + x], hu = h[((y + 1) % TexN) * TexN + x];
                    Vector3 n = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
                    px[y * TexN + x] = new Color32((byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                                                   (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                                                   (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 255);
                }
            return WritePng(path, px, true, log);
        }

        private static Texture2D WritePng(string path, Color32[] px, bool normal, StringBuilder log)
        {
            DS.EnsureDir(DS.ModelDir);
            var tex = new Texture2D(TexN, TexN, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = !normal;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
            log.AppendLine($"  ＋ текстура {path}{(normal ? " (карта нормалей)" : "")}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Плитка: основание + 4×2 квадратика со скосом. Низ — y = 0, по центру. Цвет вершин — светотень.</summary>
        internal static Mesh BarMesh(float L, float W, float H)
        {
            var vb = new QB();
            float hb = H * 0.42f, g = W * 0.05f, bev = W * 0.035f;
            // альфа вершин = сила узора (пузырьки/поры текстуры)
            Color face = new Color(1f, 1f, 1f, 1f), groove = new Color(0.74f, 0.72f, 0.71f, 1f), side = new Color(0.88f, 0.86f, 0.85f, 1f);
            vb.Box(new Vector3(0f, hb * 0.5f, 0f), new Vector3(L, hb, W), side);
            vb.Box(new Vector3(0f, hb + 0.0004f, 0f), new Vector3(L - g * 0.5f, 0.0008f, W - g * 0.5f), groove);
            int nx = 4, nz = 2;
            float cx = (L - g) / nx, cz = (W - g) / nz;
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    Vector3 c = new Vector3(-L * 0.5f + g * 0.5f + cx * (ix + 0.5f), 0f, -W * 0.5f + g * 0.5f + cz * (iz + 0.5f));
                    float ax = cx * 0.5f - g * 0.5f, az = cz * 0.5f - g * 0.5f;
                    vb.Frustum(c, ax, az, ax - bev, az - bev, hb, H, face, side);
                }
            return vb.Build("ChocolateBar_Mesh");
        }

        /// <summary>Паста в форме: ровная плита со скруглённой (скошенной) кромкой. Низ — y = 0.</summary>
        internal static Mesh LiquidMesh(float L, float W, float H)
        {
            var vb = new QB();
            float bev = W * 0.025f;
            Color face = new Color(1f, 1f, 1f, 1f), side = new Color(0.9f, 0.88f, 0.87f, 1f);
            vb.Box(new Vector3(0f, (H - bev) * 0.5f, 0f), new Vector3(L, H - bev, W), side);
            vb.Frustum(Vector3.zero, L * 0.5f, W * 0.5f, L * 0.5f - bev, W * 0.5f - bev, H - bev, H, face, side);
            return vb.Build("ChocolateMold_Liquid");
        }

        /// <summary>
        /// Сборщик меша из четырёхугольников с плоскими нормалями и цветом в вершинах.
        /// Обход сам разворачивается так, чтобы лицевая сторона смотрела наружу (Cross(b−a, c−a) по «наружу»).
        /// </summary>
        private sealed class QB
        {
            private readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
            private readonly List<Color> _c = new List<Color>();
            private readonly List<int> _t = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color col)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, outward) < 0f) { Vector3 tmp = b; b = d; d = tmp; n = -n; }
                n = n.sqrMagnitude > 1e-12f ? n.normalized : outward.normalized;
                int i0 = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(col); }
                _t.Add(i0); _t.Add(i0 + 1); _t.Add(i0 + 2);
                _t.Add(i0); _t.Add(i0 + 2); _t.Add(i0 + 3);
            }

            public void Box(Vector3 c, Vector3 s, Color col)
            {
                Vector3 h = s * 0.5f;
                Quad(P(c, h, -1, 1, -1), P(c, h, 1, 1, -1), P(c, h, 1, 1, 1), P(c, h, -1, 1, 1), Vector3.up, col);
                Quad(P(c, h, -1, -1, -1), P(c, h, 1, -1, -1), P(c, h, 1, -1, 1), P(c, h, -1, -1, 1), Vector3.down, col);
                Quad(P(c, h, 1, -1, -1), P(c, h, 1, 1, -1), P(c, h, 1, 1, 1), P(c, h, 1, -1, 1), Vector3.right, col);
                Quad(P(c, h, -1, -1, -1), P(c, h, -1, 1, -1), P(c, h, -1, 1, 1), P(c, h, -1, -1, 1), Vector3.left, col);
                Quad(P(c, h, -1, -1, 1), P(c, h, 1, -1, 1), P(c, h, 1, 1, 1), P(c, h, -1, 1, 1), Vector3.forward, col);
                Quad(P(c, h, -1, -1, -1), P(c, h, 1, -1, -1), P(c, h, 1, 1, -1), P(c, h, -1, 1, -1), Vector3.back, col);
            }

            private static Vector3 P(Vector3 c, Vector3 h, float x, float y, float z) => c + new Vector3(h.x * x, h.y * y, h.z * z);

            /// <summary>Усечённая пирамида: низ ax×az на y0, верх bx×bz на y1 (полуразмеры), центр c по XZ.</summary>
            public void Frustum(Vector3 c, float ax, float az, float bx, float bz, float y0, float y1, Color top, Color side)
            {
                Vector3 b00 = new Vector3(c.x - ax, y0, c.z - az), b10 = new Vector3(c.x + ax, y0, c.z - az);
                Vector3 b11 = new Vector3(c.x + ax, y0, c.z + az), b01 = new Vector3(c.x - ax, y0, c.z + az);
                Vector3 t00 = new Vector3(c.x - bx, y1, c.z - bz), t10 = new Vector3(c.x + bx, y1, c.z - bz);
                Vector3 t11 = new Vector3(c.x + bx, y1, c.z + bz), t01 = new Vector3(c.x - bx, y1, c.z + bz);
                Quad(t00, t10, t11, t01, Vector3.up, top);
                Quad(b10, b11, t11, t10, Vector3.right, side);
                Quad(b00, b01, t01, t00, Vector3.left, side);
                Quad(b01, b11, t11, t01, Vector3.forward, side);
                Quad(b00, b10, t10, t00, Vector3.back, side);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (_v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(_v); m.SetNormals(_n); m.SetColors(_c); m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
#endif
