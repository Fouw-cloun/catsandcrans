#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CatCooks.Food.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;
using B = CatCooks.Food.CookRecipeBook.Borsch;

namespace CatCooks.Food
{
    /// <summary>
    /// БОРЩ, этапы 1–3. Префабы НЕ создаём — только ассеты (продукты, тара, материалы, меши), компоненты
    /// на выделенных объектах и объекты в сцене. Сохранить префабом — руками, если нужно.
    ///   1. Сковорода — настроить выделенную: свой замер чаши (без ручки) → коллайдеры, приём, горка, жидкость по центру чаши.
    ///   2. Продукты и правила: свёкла, капуста, лавровый лист, масло подсолнечное, сало, «Бульон для борща»;
    ///      правила: свёклу запекать и тереть, капусту тереть; горелкой опалять кости, голяшку, лук, морковь.
    ///   3. Настроить выделенные: свёкла, капуста (FoodPiece + вид по состоянию). Кости с туши — ничего не надо.
    ///   4. Капуста — простая модель в сцену (если своей нет). Свёкла — только своя модель.
    ///   5. Бутылка масла и баночка лавра — в сцену.
    ///   6. Проверка.
    /// </summary>
    public static class CatCooksBorschSetup
    {
        private const string Root = "CatCooks/Борщ/";
        private const string VesselDir = "Assets/CatCooks/Food/Vessels";
        private const string ModelDir = "Assets/CatCooks/Food/Borsch";
        private const string PanDefName = "Cookware_PanBorsch";

        // ───────────────────────── 1. СКОВОРОДА ─────────────────────────

        [MenuItem(Root + "1. Сковорода — настроить выделенную", priority = 230)]
        public static void SetupPan()
        {
            var sel = new List<GameObject>();
            foreach (GameObject g in Selection.gameObjects) if (g != null && g.scene.IsValid()) sel.Add(g);
            if (sel.Count == 0) { EditorUtility.DisplayDialog("Борщ — сковорода", "Выдели в сцене сковороду (корень модели) и нажми ещё раз.", "Ок"); return; }
            var log = new StringBuilder();
            CookwareDef def = EnsurePanDef(log);
            foreach (GameObject go in sel)
            {
                log.AppendLine($"\n«{go.name}»:");
                Undo.RegisterFullObjectHierarchyUndo(go, "Сковорода");
                Cookware cw = DS.Need<Cookware>(go);
                Undo.RecordObject(cw, "Сковорода");
                string was = cw.Def != null ? cw.Def.name : "—";
                cw.Def = def;
                EditorUtility.SetDirty(cw);
                log.AppendLine(was == def.name ? $"  ✔ Cookware → {def.name}" : $"  ✔ Cookware: Def {was} → {def.name} («{def.Title}», {def.Capacity} порц., жарит, глотает, цвета не смешивает)");

                CookwareHeat heat = DS.Need<CookwareHeat>(go);
                var hso = new SerializedObject(heat);
                DS.SetF(hso, "_heatSpeed", 0.15f);                     // сковорода греется быстрее кастрюли
                hso.ApplyModifiedProperties();
                log.AppendLine("  ✔ Cookware Heat — греется от костра (быстрее кастрюли)");

                PickupItem old = go.GetComponent<PickupItem>();
                if (old != null && !(old is CarriedContainer))
                    log.AppendLine($"  ⚠ на корне «{old.GetType().Name}» — оставил. Если сковорода не берётся как тара (ЛКМ вылить) — удали его руками и нажми ещё раз");
                else if (old == null) { DS.Need<CarriedContainer>(go); log.AppendLine("  ＋ Carried Container (в руках как тара: ЛКМ вылить, ПКМ зачерпнуть)"); }
                if (go.GetComponent<CookwareSpill>() == null) { DS.Need<CookwareSpill>(go); log.AppendLine("  ＋ Cookware Spill (выплёскивание/вываливание через край)"); }

                Rigidbody rb = DS.Need<Rigidbody>(go);
                if (rb.mass < 1f) rb.mass = 6f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                DS.AddByName(go, "NetworkObject", log);
                DS.AddByName(go, "NetworkTransform", log);
                ClientAuthNT(go, log);

                // полость, коллайдеры, горка, жидкость — сборка тары одной кнопкой
                CookwareAutoSetup au = go.GetComponent<CookwareAutoSetup>();
                bool fresh = au == null;
                if (fresh) au = Undo.AddComponent<CookwareAutoSetup>(go);
                // Замер сами: дно — самые нижние вершины (ручка до низа не достаёт), край — медиана по секторам
                // (ручка торчит в паре секторов и в медиану не попадает). Дальше — «Собрать по размерам».
                if (!MeasurePan(go, out PanSize ps, log)) { log.AppendLine("  ✖ не смог замерить — сковорода должна стоять ровно, дном вниз"); continue; }
                var aso = new SerializedObject(au);
                SerializedProperty f = aso.FindProperty("_form"); if (f != null) f.intValue = 1;          // круглая
                DS.SetF(aso, "_wall", ps.Wall);
                DS.SetF(aso, "_bottom", ps.Bottom);
                DS.SetF(aso, "_bottomRadiusFactor", Mathf.Clamp(ps.RBot / ps.RTop, 0.5f, 1.2f));
                SerializedProperty segs = aso.FindProperty("_roundSegments"); if (segs != null) segs.intValue = 16;
                DS.SetV3(aso, "_innerSize", new Vector3(ps.RTop * 2f, ps.Depth, ps.RTop * 2f));
                DS.SetV3(aso, "_innerBottomLocal", go.transform.InverseTransformPoint(ps.BottomW));
                DS.SetF(aso, "_radiusTop", ps.RTop);
                SerializedProperty liq = aso.FindProperty("_makeLiquid"); if (liq != null) liq.boolValue = true;
                SerializedProperty heap = aso.FindProperty("_makeHeap"); if (heap != null) heap.boolValue = true;
                SerializedProperty col = aso.FindProperty("_makeColliders"); if (col != null) col.boolValue = true;
                SerializedProperty sp = aso.FindProperty("_scanProfile"); if (sp != null) sp.arraySize = 0;      // хвост от «По форме модели»
                aso.ApplyModifiedProperties();
                // ось «вверх» у Cookware — та ось корня, что сейчас смотрит вверх (старый «скан» мог поставить другую)
                var cso = new SerializedObject(cw);
                SerializedProperty upP = cso.FindProperty("_upAxis");
                if (upP != null) { upP.intValue = UpAxisOf(go.transform); cso.ApplyModifiedProperties(); }
                FieldInfo ff = typeof(CookwareAutoSetup).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                if (ff != null) ff.SetValue(au, CookwareAutoSetup.Form.Round);                          // точно круглая (иначе Build уходит в «скан»)
                MethodInfo build = typeof(CookwareAutoSetup).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic);
                try
                {
                    if (build != null) build.Invoke(au, null);
                    foreach (CookwareLiquidVisual lv in go.GetComponentsInChildren<CookwareLiquidVisual>(true))
                    {
                        var lso = new SerializedObject(lv);
                        SerializedProperty bp = lso.FindProperty("_boxProfile"); if (bp != null && bp.arraySize > 0) { bp.arraySize = 0; lso.ApplyModifiedProperties(); }
                    }
                    log.AppendLine($"  ✔ замер: чаша Ø{ps.RTop * 2f:0.00} м у края, Ø{ps.RBot * 2f:0.00} у дна, глубина {ps.Depth:0.00}, " +
                                   $"стенка {ps.Wall * 100f:0.#} см, дно {ps.Bottom * 100f:0.#} см, центр смещён от опоры на {ps.Offset:0.00} м" +
                                   (ps.HandleSectors > 0 ? $" · ручка в {ps.HandleSectors} секторах — не учтена" : ""));
                    log.AppendLine("  ✔ собрано по замеру: AutoColliders (дно + 16 стенок), Receive, Mouth, HeapAnchor, LiquidAnchor — всё по центру ЧАШИ");
                }
                catch (System.Exception e) { log.AppendLine($"  ✖ сборка Auto Setup не удалась ({e.GetBaseException().Message}) — горку и жидкость ставлю сам"); }
                PanVisuals(go, ps, log);
                EditorUtility.SetDirty(go);
            }
            AcceptInDocks(def, log);
            EditorSceneManager.MarkSceneDirty(sel[0].scene);
            log.AppendLine("\nДальше: Play (хост) → F11 доски, F12 поджечь, F9 сковорода на огонь, F9 ещё раз — обратно на место.");
            log.AppendLine("Повторное нажатие меню 1 перемеряет и пересоберёт. Кнопку «Подогнать и собрать» у Auto Setup на сковороде НЕ жми — она мерит вместе с ручкой.");
            log.AppendLine("Подправить руками: поля Inner Size / Inner Bottom Local / Radius Top у Auto Setup → ⋮ → «Собрать по размерам (без замера)».");
            log.AppendLine("Сохрани сцену (и префаб сковороды руками, если нужно).");
            DS.Done("Борщ — сковорода", log);
        }

        private struct PanSize { public Vector3 BottomW; public float RTop, RBot, Depth, Wall, Bottom, Offset; public int HandleSectors; }

        /// <summary>
        /// Замер чаши сковороды в мире (сковорода стоит ровно). Вершины всех мешей, кроме авто-объектов.
        ///   дно: вершины в нижних 4% высоты → центр чаши (по габариту) и наружный радиус дна;
        ///   край: 48 секторов вокруг центра, в каждом — самая дальняя вершина; медиана = наружный радиус края
        ///         (ручка — в 2–6 секторах, в медиану не попадает), высота края — медиана высот тех точек;
        ///   внутреннее дно — самая высокая вершина у центра ниже середины глубины.
        /// </summary>
        private static bool MeasurePan(GameObject go, out PanSize ps, StringBuilder log)
        {
            ps = new PanSize();
            var pts = new List<Vector3>();
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || IsAuto(mf.transform, go.transform)) continue;
                Vector3[] vs = mf.sharedMesh.vertices;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                int step = Mathf.Max(1, vs.Length / 20000);
                for (int k = 0; k < vs.Length; k += step) pts.Add(m.MultiplyPoint3x4(vs[k]));
            }
            if (pts.Count < 20) return false;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 p in pts) { if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y; }
            float H = maxY - minY;
            if (H < 1e-4f) return false;
            // дно
            float bx0 = float.MaxValue, bx1 = float.MinValue, bz0 = float.MaxValue, bz1 = float.MinValue; int nb = 0;
            foreach (Vector3 p in pts)
            {
                if (p.y > minY + H * 0.04f) continue;
                nb++;
                if (p.x < bx0) bx0 = p.x; if (p.x > bx1) bx1 = p.x; if (p.z < bz0) bz0 = p.z; if (p.z > bz1) bz1 = p.z;
            }
            if (nb < 3) return false;
            Vector2 c = new Vector2((bx0 + bx1) * 0.5f, (bz0 + bz1) * 0.5f);
            float rBotOut = Mathf.Max(bx1 - bx0, bz1 - bz0) * 0.5f;
            // край — по секторам
            const int Sec = 48;
            var far = new float[Sec]; var farY = new float[Sec];
            for (int k = 0; k < Sec; k++) far[k] = -1f;
            foreach (Vector3 p in pts)
            {
                Vector2 d = new Vector2(p.x - c.x, p.z - c.y);
                float r = d.magnitude;
                int k = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(d.y, d.x) + Mathf.PI) / (2f * Mathf.PI) * Sec), 0, Sec - 1);
                if (r > far[k]) { far[k] = r; farY[k] = p.y; }
            }
            var list = new List<float>();
            for (int k = 0; k < Sec; k++) if (far[k] > 0f) list.Add(far[k]);
            if (list.Count < Sec / 2) return false;
            list.Sort();
            float rTopOut = list[list.Count / 2];
            var ys = new List<float>(); int handle = 0;
            for (int k = 0; k < Sec; k++)
            {
                if (far[k] <= 0f) continue;
                if (far[k] > rTopOut * 1.15f) { handle++; continue; }
                ys.Add(farY[k]);
            }
            ys.Sort();
            float rimY = ys.Count > 0 ? ys[ys.Count / 2] : maxY;
            // внутреннее дно
            float floorY = minY;
            foreach (Vector3 p in pts)
            {
                float r = new Vector2(p.x - c.x, p.z - c.y).magnitude;
                if (r < rBotOut * 0.5f && p.y < minY + (rimY - minY) * 0.5f && p.y > floorY) floorY = p.y;
            }
            float wall = Mathf.Clamp(rTopOut * 0.03f, 0.004f, 0.05f);
            ps.Bottom = Mathf.Clamp(floorY - minY, 0.004f, 0.08f);
            ps.RTop = Mathf.Max(0.02f, rTopOut - wall);
            ps.RBot = Mathf.Max(0.02f, rBotOut - wall);
            ps.Depth = Mathf.Max(0.02f, rimY - floorY);
            ps.Wall = wall;
            ps.BottomW = new Vector3(c.x, floorY, c.y);
            ps.Offset = new Vector2(go.transform.position.x - c.x, go.transform.position.z - c.y).magnitude;
            ps.HandleSectors = handle;
            return true;
        }

        /// <summary>
        /// Горка и жидкость сковороды — КРУГЛЫЕ и ТРАПЕЦИЕЙ (дно уже края): ставлю сам поверх Auto Setup,
        /// чтобы не осталось «прямоугольника» и профиля от «По форме модели».
        /// </summary>
        private static void PanVisuals(GameObject go, PanSize ps, StringBuilder log)
        {
            CookwareFillTuner tuner = DS.Need<CookwareFillTuner>(go);
            tuner.EditorSet(ps.BottomW, ps.RBot, ps.RTop, ps.Depth);
            tuner.EditorFitWalls();                                 // радиусы у дна и у края — по настоящим стенкам
            log.AppendLine($"  ✔ Cookware Fill Tuner — горка и жидкость круглые, трапецией: Ø{ps.RBot * 2f:0.00} у дна → Ø{ps.RTop * 2f:0.00} у края.");
            log.AppendLine("     Подогнать руками: компонент Cookware Fill Tuner на сковороде — оранжевая трапеция (горка) и голубая (жидкость) видны в сцене, поля меняют якоря сразу.");
        }

        private static void SetB(SerializedObject so, string n, bool v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.boolValue = v; }

        private static bool IsAuto(Transform t, Transform root)
        {
            for (Transform p = t; p != null && p != root; p = p.parent)
                if (p.name == "AutoColliders" || p.name == "Receive" || p.name == "Mouth" || p.name == "HeapAnchor" || p.name == "LiquidAnchor") return true;
            return t.GetComponent<FoodHeapRenderer>() != null || t.GetComponentInParent<CookwareLiquidVisual>() != null;
        }

        /// <summary>Какая ось корня смотрит вверх: индекс Cookware.UpAxis.</summary>
        private static int UpAxisOf(Transform t)
        {
            float best = -2f; int idx = 1;
            Vector3[] ax = { t.up, t.forward, -t.forward, t.right, -t.right, -t.up };
            int[] id = { 1, 2, 3, 4, 5, 6 };
            for (int i = 0; i < ax.Length; i++) { float d = Vector3.Dot(ax[i], Vector3.up); if (d > best) { best = d; idx = id[i]; } }
            return idx;
        }

        private static CookwareDef EnsurePanDef(StringBuilder log)
        {
            string path = $"{VesselDir}/{PanDefName}.asset";
            CookwareDef d = AssetDatabase.LoadAssetAtPath<CookwareDef>(path);
            bool fresh = d == null;
            if (fresh)
            {
                DS.EnsureDir(VesselDir);
                d = ScriptableObject.CreateInstance<CookwareDef>();
                d.Capacity = 20;
                AssetDatabase.CreateAsset(d, path);
            }
            d.Title = "Сковорода"; d.Kind = CookwareKind.Flat; d.HeatProcess = FoodProcess.Fry;
            d.NeedsHeatSource = true; d.Absorbs = false; d.BlendsColor = false; d.PortionPerMark = 0;   // целые овощи/мясо лежат собой и жарятся; тёртое, масло, паста — в горку/жидкость
            if (d.Capacity < 8) d.Capacity = 20;
            EditorUtility.SetDirty(d);
            log.AppendLine($"{(fresh ? "＋" : "✔")} тара {path}: «Сковорода», {d.Capacity} порц., жарит. Целые куски (лук, морковь, сало) НЕ глотает — лежат собой и жарятся; " +
                           "тёртое (из измельчителя), масло, паста — горкой и жидкостью");
            return d;
        }

        /// <summary>Точки огня со списком Accept — добавить туда сковороду (пустой список и так принимает всё).</summary>
        private static void AcceptInDocks(CookwareDef def, StringBuilder log)
        {
            foreach (CookwareDock dock in Object.FindObjectsByType<CookwareDock>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(dock);
                SerializedProperty arr = so.FindProperty("_accept");
                if (arr == null || arr.arraySize == 0) continue;
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == def) { has = true; break; }
                if (has) continue;
                arr.arraySize++;
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = def;
                so.ApplyModifiedProperties();
                log.AppendLine($"  ＋ точка «{dock.name}»: сковорода добавлена в Accept");
            }
        }

        internal static void ClientAuthNT(GameObject go, StringBuilder log)
        {
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null || c.GetType().Name != "NetworkTransform") continue;
                var so = new SerializedObject(c);
                SerializedProperty a = so.FindProperty("_clientAuthoritative"); if (a != null) a.boolValue = true;
                SerializedProperty sd = so.FindProperty("_sendToOwner"); if (sd != null) sd.boolValue = true;
                so.ApplyModifiedProperties();
            }
        }

        // ───────────────────────── 2. ПРОДУКТЫ И ПРАВИЛА ─────────────────────────

        [MenuItem(Root + "2. Продукты и правила (свёкла, капуста, лавр, масло, сало, горелка)", priority = 231)]
        public static void MakeProducts()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef veg = Or(DS.GroupOf("Морковь"), DS.FindGroup("Group_Vegetables"));
            FoodGroupDef spice = Or(DS.GroupOf("Перец чёрный"), DS.FindGroup("Group_Spices"));
            FoodGroupDef liquid = Or(DS.GroupOf("Яблочный уксус"), DS.FindGroup("Group_Liquid"));
            FoodGroupDef meat = Or(Or(DS.GroupOf("Свинина"), DS.GroupOf("Кости для бульона")), DS.FindGroup("Group_Meat"));
            FoodGroupDef brothG = Or(Or(DS.GroupOf("Бульон"), DS.FindGroup("Group_SubRecipe")), liquid);

            log.AppendLine("ПРОДУКТЫ:");
            ProductDef beet = DS.Ensure(B.Beet, veg, new Color(0.50f, 0.07f, 0.20f), 0.12f, false, dir, ref next, log);
            ProductDef cab = DS.Ensure(B.Cabbage, veg, new Color(0.80f, 0.88f, 0.62f), 0.2f, false, dir, ref next, log);
            ProductDef bay = DS.Ensure(B.Bay, spice, new Color(0.42f, 0.52f, 0.24f), 0.03f, false, dir, ref next, log);
            ProductDef oil = DS.Ensure(B.Oil, liquid, new Color(0.96f, 0.78f, 0.20f), 0.05f, false, dir, ref next, log);
            ProductDef lard = DS.Ensure(B.Lard, meat, new Color(0.96f, 0.92f, 0.86f), 0.15f, false, dir, ref next, log);
            ProductDef broth = DS.Ensure(B.Broth, brothG, new Color(0.70f, 0.45f, 0.20f), 0.1f, true, dir, ref next, log);
            Debris(beet, new Color(0.55f, 0.05f, 0.22f)); Debris(cab, new Color(0.84f, 0.90f, 0.66f)); Debris(bay, new Color(0.42f, 0.52f, 0.24f));
            if (beet != null) { beet.BurntColor = new Color(0.14f, 0.04f, 0.06f); EditorUtility.SetDirty(beet); }
            ProductDef old = DS.Find("Бульон");
            log.AppendLine(old != null ? "✔ старый «Бульон» не тронут — основа соусов" : "· старого «Бульона» нет — не страшно");

            log.AppendLine("\nПРАВИЛА:");
            if (beet != null)
            {
                RuleW(beet, FoodState.Peeled, FoodProcess.Bake, FoodState.Baked, 40f, 0.35f, FoodState.Undercooked, FoodState.Overcooked, log);
                RuleW(beet, FoodState.Washed, FoodProcess.Bake, FoodState.Baked, 45f, 0.35f, FoodState.Undercooked, FoodState.Overcooked, log);
                RuleW(beet, FoodState.Undercooked, FoodProcess.Bake, FoodState.Baked, 20f, 0.35f, FoodState.None, FoodState.Overcooked, log);
                RuleW(beet, FoodState.Overcooked, FoodProcess.Bake, FoodState.Burnt, 12f, 0f, FoodState.None, FoodState.None, log);
                RuleW(beet, FoodState.Baked, FoodProcess.Grate, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
                RuleW(beet, FoodState.Baked, FoodProcess.Cut, FoodState.Pieces, 2f, 0f, FoodState.None, FoodState.None, log);
            }
            if (cab != null)
            {
                RuleW(cab, FoodState.Washed, FoodProcess.Grate, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
                RuleW(cab, FoodState.Washed, FoodProcess.Cut, FoodState.Pieces, 2f, 0f, FoodState.None, FoodState.None, log);
            }
            // горелка: опалить до «Опалено» (окно ±50%), передержал — «Сгорело»
            // лук (любой: репчатый, красный, белый, жёлтый…) и морковь — опалять ТОЛЬКО чищеные
            int nVeg = 0;
            foreach (ProductDef p in TorchVeg())
            {
                nVeg++;
                RemoveRule(p, FoodState.Washed, FoodProcess.Torch, log);       // в кожуре не опаляется (было в v1.0–1.5)
                RemoveRule(p, FoodState.Dirty, FoodProcess.Torch, log);
                Torch(p, FoodState.Peeled, 6f, log);
            }
            if (nVeg == 0) log.AppendLine("✖ не нашёл ни лука, ни моркови — опалять нечего");
            ProductDef bones = DS.Find(B.Bones), shank = DS.Find(B.Shank);
            if (bones != null) { Torch(bones, FoodState.Butchered, 8f, log); Torch(bones, FoodState.Raw, 8f, log); }
            else log.AppendLine("✖ нет «Кости для бульона» — CatCooks → Еда (срез 10 блюд) / Мясо → кости");
            if (shank != null) { Torch(shank, FoodState.Butchered, 10f, log); Torch(shank, FoodState.Raw, 10f, log); }
            else log.AppendLine("✖ нет «Говяжья голяшка» — Мясо → Новые туши → 1");

            // сало — продукт свиной грудинки новой туши
            log.AppendLine("\nСАЛО:");
            MeatCutSettings st = FindSettings();
            if (st != null && lard != null && st.PorkKinds != null && st.PorkKinds.Length > 1)
            {
                if (st.PorkKinds[1] != lard)
                {
                    log.AppendLine($"✔ свиная грудинка новой туши: продукт «{(st.PorkKinds[1] != null ? st.PorkKinds[1].Title : "—")}» → «Сало» (в фарш не идёт; копчение — потом)");
                    st.PorkKinds[1] = lard; EditorUtility.SetDirty(st);
                }
                else log.AppendLine("✔ свиная грудинка уже даёт «Сало»");
            }
            else log.AppendLine("⚠ нет настроек туши (MeatCutSettings) — Мясо → Новые туши → 1, потом это меню ещё раз");

            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("\n✔ FoodDatabase в сцене пересобран — СОХРАНИ СЦЕНУ");
            }
            else log.AppendLine("\n⚠ в открытой сцене нет FoodDatabase — нажми у него «Collect From Project»");
            BrothLiquid(log);
            CookRecipeBook.Reload();
            log.AppendLine("\nСВЁКЛА: помыть → почистить → в духовку (запечь ~26–54 с, окно) → в измельчитель → тёртая (горкой).");
            log.AppendLine("КАПУСТА: помыть → в измельчитель → тёртая.");
            log.AppendLine("ГОРЕЛКА (ручная или кровельная): пламя на продукт ~3–5 с → «Опалено» (темнеет, подсказка над ним); дольше ~9 с — «Сгорело».");
            log.AppendLine("  Кости 4–12 с, голяшка 5–15 с, лук и морковь 3–9 с. Мощная кровельная — быстрее.");
            log.AppendLine($"\nРЕЦЕПТ «{B.Broth}» (на кастрюлю): кости {B.BonesN} + голяшка {B.ShankN} шайбы + лук {B.OnionN} + морковь {B.CarrotN} (всё опалить горелкой)");
            log.AppendLine($"  + лавровый лист {B.BayDoses} дозы + вода доверху → кипятить {B.BoilSeconds:0} с. Допуск ±50%. Кости/овощи остаются — вынуть.");
            log.AppendLine("\nДальше: 3 (выдели свою свёклу; капусту — если есть своя), 4 (простая капуста), 5 (бутылка масла и баночка лавра), 6 (проверка). Кости с туши настраивать не надо.");
            DS.Done("Борщ — продукты и правила", log);
        }

        /// <summary>Все продукты-лук (кроме зелёного) и морковь: по названию, у кого бы ни была модель.</summary>
        private static List<ProductDef> TorchVeg()
        {
            var list = new List<ProductDef>();
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d == null || string.IsNullOrEmpty(d.Title)) continue;
                string t = d.Title.ToLowerInvariant();
                bool onion = (t.StartsWith("лук") || t.Contains(" лук")) && !t.Contains("зелён") && !t.Contains("зелен");
                bool carrot = t.StartsWith("морков");
                if (onion || carrot) list.Add(d);
            }
            return list;
        }

        private static void RemoveRule(ProductDef p, FoodState from, FoodProcess proc, StringBuilder log)
        {
            for (int i = p.Overrides.Count - 1; i >= 0; i--)
                if (p.Overrides[i].From == from && p.Overrides[i].Process == proc)
                {
                    p.Overrides.RemoveAt(i);
                    EditorUtility.SetDirty(p);
                    log.AppendLine($"－ «{p.Title}»: убрано {FoodStateInfo.NameOf(from)} → {FoodStateInfo.NameOf(proc)}");
                }
        }

        private static void Torch(ProductDef p, FoodState from, float sec, StringBuilder log)
        {
            RuleW(p, from, FoodProcess.Torch, FoodState.Torched, sec, 0.5f, FoodState.None, FoodState.Burnt, log);
            RuleW(p, FoodState.Torched, FoodProcess.Torch, FoodState.Burnt, sec, 0f, FoodState.None, FoodState.None, log);
        }

        /// <summary>Своё правило продукта (Overrides) с окном. Есть такое же From+Process — обновить.</summary>
        internal static void RuleW(ProductDef p, FoodState from, FoodProcess proc, FoodState to, float sec, float window,
                                  FoodState early, FoodState late, StringBuilder log)
        {
            var r = new FoodRule { From = from, Process = proc, To = to, Seconds = sec, Window = window, TooEarly = early, TooLate = late };
            for (int i = 0; i < p.Overrides.Count; i++)
            {
                FoodRule o = p.Overrides[i];
                if (o.From != from || o.Process != proc) continue;
                if (o.To == to && Mathf.Approximately(o.Seconds, sec) && Mathf.Approximately(o.Window, window) && o.TooEarly == early && o.TooLate == late) return;
                p.Overrides[i] = r; EditorUtility.SetDirty(p);
                log.AppendLine($"✔ «{p.Title}»: {FoodStateInfo.NameOf(from)} → {FoodStateInfo.NameOf(proc)} → {FoodStateInfo.NameOf(to)} (обновлено)");
                return;
            }
            p.Overrides.Add(r);
            EditorUtility.SetDirty(p);
            log.AppendLine($"＋ «{p.Title}»: {FoodStateInfo.NameOf(from)} → {FoodStateInfo.NameOf(proc)} → {FoodStateInfo.NameOf(to)}" +
                           (window > 0f ? $" ({sec * (1f - window):0.#}–{sec * (1f + window):0.#} с)" : $" ({sec:0.#} с)"));
        }

        private static void Debris(ProductDef p, Color c)
        {
            if (p == null || p.DebrisColor.a > 0.01f) return;
            c.a = 1f; p.DebrisColor = c; EditorUtility.SetDirty(p);
        }

        internal static MeatCutSettings FindSettings()
        {
            foreach (string g in AssetDatabase.FindAssets("t:MeatCutSettings"))
            {
                MeatCutSettings s = AssetDatabase.LoadAssetAtPath<MeatCutSettings>(AssetDatabase.GUIDToAssetPath(g));
                if (s != null) return s;
            }
            return null;
        }

        private static bool InDb(FoodDatabase db, ProductDef p)
        {
            SerializedProperty arr = new SerializedObject(db).FindProperty("_products");
            if (arr == null) return true;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == p) return true;
            return false;
        }

        internal static T Or<T>(T a, T b) where T : Object { return a != null ? a : b; }

        // ───────────────────────── 3. НАСТРОИТЬ ВЫДЕЛЕННЫЕ ─────────────────────────

        [MenuItem(Root + "3. Настроить выделенные (свёкла, капуста)", priority = 232)]
        public static void SetupSelected()
        {
            if (Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Борщ", "Выдели свои свёклу или капусту — в сцене или префабы в окне Project.", "Ок");
                return;
            }
            var log = new StringBuilder();
            foreach (GameObject sel in Selection.gameObjects)
            {
                if (sel == null) continue;
                string path = AssetDatabase.GetAssetPath(sel);
                bool asset = !string.IsNullOrEmpty(path) && path.EndsWith(".prefab");
                GameObject go = sel;
                if (asset) go = PrefabUtility.LoadPrefabContents(path);
                else Undo.RegisterFullObjectHierarchyUndo(go, "Борщ");
                try
                {
                    string kind = KindOf(go);
                    if (kind == "beet") SetupVeg(go, B.Beet, 6, true, log);
                    else if (kind == "cabbage") SetupVeg(go, B.Cabbage, 12, false, log);
                    else if (kind == "bone" || kind == "veg")
                    {
                        if (go.GetComponent<FoodPiece>() == null) { log.AppendLine($"· «{sel.name}»: нет FoodPiece — пропущен"); continue; }
                        FoodStateVisual v = go.GetComponent<FoodStateVisual>();
                        if (v == null) { go.AddComponent<FoodStateVisual>(); log.AppendLine($"✔ «{sel.name}»: ＋ FoodStateVisual — опалённое темнеет, сгоревшее — уголь"); }
                        else log.AppendLine($"✔ «{sel.name}»: вид по состоянию уже есть (подкраска нагрева включена по умолчанию)");
                    }
                    else { log.AppendLine($"· «{sel.name}»: не понял, что это (свёкла/beet, капуста/cabbage, кость/bone) — пропущен"); continue; }
                    if (asset) { PrefabUtility.SaveAsPrefabAsset(go, path); log.AppendLine($"   (префаб {path} сохранён)"); }
                    else EditorUtility.SetDirty(go);
                }
                finally { if (asset) PrefabUtility.UnloadPrefabContents(go); }
            }
            DS.Done("Борщ — настроить выделенные", log);
        }

        private static string KindOf(GameObject go)
        {
            string n = go.name.ToLowerInvariant();
            MeshFilter mf = go.GetComponentInChildren<MeshFilter>(true);
            if (mf != null && mf.sharedMesh != null) n += " " + mf.sharedMesh.name.ToLowerInvariant() + " " + AssetDatabase.GetAssetPath(mf.sharedMesh).ToLowerInvariant();
            if (n.Contains("свёкл") || n.Contains("свекл") || n.Contains("beet")) return "beet";
            if (n.Contains("капуст") || n.Contains("cabbage")) return "cabbage";
            if (n.Contains("bone") || n.Contains("кост")) return "bone";
            if (n.Contains("морков") || n.Contains("carrot") || n.Contains("лук") || n.Contains("onion")) return "veg";
            return "";
        }

        /// <summary>Свёкла / капуста: FoodPiece (грязная), вид по состоянию, физика, сеть. Модель не меняется.</summary>
        internal static void SetupVeg(GameObject go, string title, int count, bool beet, StringBuilder log)
        {
            ProductDef p = DS.Find(title);
            int food = LayerMask.NameToLayer("Food");
            if (food >= 0) foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = food;
            Get<FishNet.Object.NetworkObject>(go);
            Get<FishNet.Component.Transforming.NetworkTransform>(go);
            ClientAuthNT(go, log);
            Rigidbody rb = Get<Rigidbody>(go);
            if (rb.mass < 0.01f || rb.mass > 30f) rb.mass = beet ? 0.8f : 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            FoodPiece fp = Get<FoodPiece>(go);
            var so = new SerializedObject(fp);
            if (p != null) DS.SetObj(so, "_product", p);
            SerializedProperty st = so.FindProperty("_startState"); if (st != null) st.intValue = (int)FoodState.Dirty;
            DS.SetInt(so, "_count", count);
            so.ApplyModifiedPropertiesWithoutUndo();
            FoodStateVisual vis = Get<FoodStateVisual>(go);
            if (vis.EditorMeshTarget == null) vis.EditorMeshTarget = go.GetComponentInChildren<MeshFilter>(true);
            vis.EditorDirtyTint = 0.35f;
            if (beet)
            {
                // чищеная свёкла — мякоть (свой материал), запечённая — та же мякоть, темнее (подкраска нагрева)
                Renderer br = go.GetComponentInChildren<Renderer>(true);
                Material flesh = FleshFrom(br != null ? br.sharedMaterial : null, log);
                vis.EditorSetLocalLook(FoodState.Peeled, flesh, -1, null);
                vis.EditorSetLocalLook(FoodState.Baked, flesh, -1, null);
                vis.EditorSetLocalLook(FoodState.Undercooked, flesh, -1, null);
                vis.EditorSetLocalLook(FoodState.Overcooked, flesh, -1, null);
            }
            EditorUtility.SetDirty(vis);
            Get<Suckable>(go);
            if (go.GetComponentInChildren<Collider>() == null)
            {
                MeshFilter mf = go.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null && mf.transform == go.transform) { MeshCollider mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = true; }
                else go.AddComponent<SphereCollider>();
            }
            log.AppendLine($"✔ «{go.name}»: {title}" + (p == null ? " (продукта нет — сначала меню 2, потом ещё раз)" : "") +
                           $" · грязная, {count} порц. · вид: грязная темнее" + (beet ? ", чищеная — мякоть, запечённая — темнее" : "") + ", тёртая — горкой");
        }

        internal static T Get<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        /// <summary>
        /// Мякоть чищеной свёклы: КОПИЯ материала твоей свёклы (тот же шейдер, тот же свет), без текстуры кожуры,
        /// цвет мякоти. Голый URP/Lit на ярком солнце сцены выбеливается — поэтому не он.
        /// </summary>
        private static Material FleshFrom(Material src, StringBuilder log)
        {
            string path = ModelDir + "/Mat_Beet_Flesh.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            if (src == null) return Mat("Mat_Beet_Flesh", new Color(0.62f, 0.05f, 0.26f), 0.3f, log);
            DS.EnsureDir(ModelDir);
            m = new Material(src) { name = "Mat_Beet_Flesh" };
            Color c = new Color(0.62f, 0.05f, 0.26f);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ материал {path} (копия материала свёклы «{src.name}», цвет мякоти)");
            return m;
        }

        private static Material Mat(string name, Color c, float smooth, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            DS.EnsureDir(ModelDir);
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ материал {path}");
            return m;
        }

        // ───────────────────────── 4. КАПУСТА — ПРОСТАЯ МОДЕЛЬ ─────────────────────────

        [MenuItem(Root + "4. Капуста — простая модель в сцену (если своей нет)", priority = 233)]
        public static void MakeModels()
        {
            var log = new StringBuilder();
            if (DS.Find(B.Cabbage) == null) { EditorUtility.DisplayDialog("Борщ", "Сначала меню 2 (продукты).", "Ок"); return; }
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            float S = Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
            float D = 0.75f * S;

            Mesh cabMesh = SaveMesh(CabbageMesh(D), "Cabbage_Mesh", log);
            GameObject cab = SceneObj("Cabbage_01", at);
            cab.GetComponent<MeshFilter>().sharedMesh = cabMesh;
            // мультяшный материал игры (цвет — в вершинах), как у бутылок: обычный URP/Lit на ярком солнце сцены выбеливался и «светился»
            cab.GetComponent<MeshRenderer>().sharedMaterials = new[] { DS.PlateMat(log) };
            SphereCollider sc = Get<SphereCollider>(cab); sc.radius = D * 0.5f; sc.center = Vector3.zero;
            SetupVeg(cab, B.Cabbage, 12, false, log);

            Selection.activeGameObject = cab;
            EditorSceneManager.MarkSceneDirty(cab.scene);
            log.AppendLine("\nCabbage_01 в сцене. Нужно больше — дублируй (Ctrl+D) или сохрани префабом и добавь в Spawnable Prefabs.");
            log.AppendLine("Повторный запуск обновит меш и материалы на месте (старое «свечение» уйдёт).");
            DS.Done("Борщ — капуста", log);
        }

        internal static GameObject SceneObj(string name, Vector3 at)
        {
            GameObject go = GameObject.Find(name);
            if (go == null || !go.scene.IsValid())
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, name);
                go.transform.position = at;
            }
            Get<MeshFilter>(go); Get<MeshRenderer>(go);
            return go;
        }

        private static Mesh SaveMesh(Mesh m, string name, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".asset";
            DS.EnsureDir(ModelDir);
            m.name = name;
            Mesh have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null)
            {
                EditorUtility.CopySerialized(m, have);
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(have);
                log.AppendLine($"  ✔ меш {path} обновлён");
                return have;
            }
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ меш {path}");
            return m;
        }

        /// <summary>
        /// Капуста: кочан (сплюснутая сфера с жилками) + пять внешних листьев-чашек (второй материал).
        /// Нормали считаются сами по форме (не RecalculateNormals): у двусторонних листьев общие вершины
        /// давали нулевые нормали — от этого кочан «светился» (NaN в освещении → белое пятно bloom).
        /// Обратная сторона листа — свои вершины с нормалью внутрь. Без вырожденных треугольников на полюсах.
        /// </summary>
        private static Mesh CabbageMesh(float D)
        {
            float R = D * 0.5f;
            const float Squash = 0.88f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var core = new List<int>(); var leaves = new List<int>(); var col = new List<Color>();
            Color heart = new Color(0.82f, 0.90f, 0.62f), veinC = new Color(0.92f, 0.96f, 0.78f);

            // кочан: кольца без полюсов + по вершине на полюсах (веер)
            const int La = 16, Lo = 28;
            int top = v.Count;
            v.Add(new Vector3(0f, R * 0.92f * Squash, 0f)); n.Add(Vector3.up); uv.Add(new Vector2(0.5f, 0f)); col.Add(heart);
            int ring0 = v.Count;
            for (int i = 1; i < La; i++)
            {
                float th = Mathf.PI * i / La;
                for (int j = 0; j <= Lo; j++)
                {
                    float ph = Mathf.PI * 2f * j / Lo;
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    float vein = 1f + 0.025f * Mathf.Abs(Mathf.Sin(ph * 6f + th * 2.5f)) + 0.02f * Mathf.Sin(th * 7f + ph);
                    Vector3 p = d * R * 0.92f * vein; p.y *= Squash;
                    v.Add(p); n.Add(new Vector3(d.x, d.y / Squash, d.z).normalized); uv.Add(new Vector2(j / (float)Lo, i / (float)La));
                    col.Add(Color.Lerp(heart, veinC, Mathf.Clamp01((vein - 1f) * 18f)));
                }
            }
            int bot = v.Count;
            v.Add(new Vector3(0f, -R * 0.92f * Squash, 0f)); n.Add(Vector3.down); uv.Add(new Vector2(0.5f, 1f)); col.Add(heart);
            int rows = La - 1;
            for (int j = 0; j < Lo; j++) { core.Add(top); core.Add(ring0 + j + 1); core.Add(ring0 + j); }
            for (int i = 0; i < rows - 1; i++)
                for (int j = 0; j < Lo; j++)
                {
                    int a = ring0 + i * (Lo + 1) + j, b = a + Lo + 1;
                    core.Add(a); core.Add(a + 1); core.Add(b); core.Add(a + 1); core.Add(b + 1); core.Add(b);
                }
            int last = ring0 + (rows - 1) * (Lo + 1);
            for (int j = 0; j < Lo; j++) { core.Add(bot); core.Add(last + j); core.Add(last + j + 1); }

            for (int k = 0; k < 5; k++)
            {
                float az = k * 72f * Mathf.Deg2Rad + 0.3f;
                Vector3 c = new Vector3(Mathf.Cos(az) * 0.8f, -0.35f, Mathf.Sin(az) * 0.8f).normalized;   // листья обнимают снизу и с боков
                Leaf(v, n, uv, col, leaves, c, R * (1.0f + 0.02f * k), 58f, Squash);
            }

            FixOutward(v, core);
            var m = new Mesh();
            core.AddRange(leaves);                             // один материал — цвет в вершинах
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(col);
            m.SetTriangles(core, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Лист-чашка: лицевая сторона (нормаль наружу) и изнанка (свои вершины, нормаль внутрь).</summary>
        private static void Leaf(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Color> col, List<int> t, Vector3 c, float R, float spreadDeg, float squash)
        {
            Vector3 u = Vector3.Cross(c, Vector3.up); if (u.sqrMagnitude < 1e-4f) u = Vector3.right; u.Normalize();
            Vector3 w = Vector3.Cross(c, u).normalized;
            const int na = 8, nb = 20;
            for (int side = 0; side < 2; side++)
            {
                float sgn = side == 0 ? 1f : -1f;
                float lift = side == 0 ? 1f : 0.985f;            // изнанка чуть внутри — без мерцания
                int tip = v.Count;
                Vector3 pc = c * R * lift; pc.y *= squash;
                Color lc = side == 0 ? new Color(0.50f, 0.70f, 0.34f) : new Color(0.72f, 0.84f, 0.52f);
                v.Add(pc); n.Add(new Vector3(c.x, c.y / squash, c.z).normalized * sgn); uv.Add(new Vector2(0.5f, 0f)); col.Add(lc);
                int start = v.Count;
                for (int i = 1; i <= na; i++)
                {
                    float a = spreadDeg * Mathf.Deg2Rad * i / na;
                    for (int j = 0; j <= nb; j++)
                    {
                        float b = Mathf.PI * 2f * j / nb;
                        float wave = 1f + 0.05f * Mathf.Sin(b * 5f) * (i / (float)na);
                        Vector3 d = Mathf.Cos(a) * c + Mathf.Sin(a) * (Mathf.Cos(b) * u + Mathf.Sin(b) * w);
                        Vector3 p = d * R * wave * lift; p.y *= squash;
                        v.Add(p); n.Add(new Vector3(d.x, d.y / squash, d.z).normalized * sgn); uv.Add(new Vector2(j / (float)nb, i / (float)na));
                        float vein2 = Mathf.Abs(Mathf.Sin(b * 4f)) < 0.12f ? 0.35f : 0f;   // светлые жилки листа
                        col.Add(Color.Lerp(lc, new Color(0.88f, 0.94f, 0.72f), vein2 + 0.15f * i / na));
                    }
                }
                for (int j = 0; j < nb; j++)
                {
                    int a0 = start + j;
                    if (side == 0) { t.Add(tip); t.Add(a0); t.Add(a0 + 1); } else { t.Add(tip); t.Add(a0 + 1); t.Add(a0); }
                }
                for (int i = 0; i < na - 1; i++)
                    for (int j = 0; j < nb; j++)
                    {
                        int a = start + i * (nb + 1) + j, b = a + nb + 1;
                        if (side == 0) { t.Add(a); t.Add(b); t.Add(a + 1); t.Add(a + 1); t.Add(b); t.Add(b + 1); }
                        else { t.Add(a); t.Add(a + 1); t.Add(b); t.Add(a + 1); t.Add(b + 1); t.Add(b); }
                    }
            }
        }

        /// <summary>Большинство граней должно смотреть от центра — иначе развернуть все.</summary>
        private static void FixOutward(List<Vector3> v, List<int> t)
        {
            if (t.Count < 3) return;
            Vector3 ctr = Vector3.zero;
            int good = 0, bad = 0;
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 nn = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(nn, (a + b + c) / 3f - ctr) >= 0f) good++; else bad++;
            }
            if (bad <= good) return;
            for (int i = 0; i < t.Count; i += 3) { int x = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = x; }
        }

        // ───────────────────────── 5. МАСЛО И ЛАВР ─────────────────────────

        [MenuItem(Root + "5. Бутылка масла и баночка лавра — в сцену", priority = 234)]
        public static void MakeBottles()
        {
            var log = new StringBuilder();
            ProductDef oil = DS.Find(B.Oil), bay = DS.Find(B.Bay);
            if (oil == null || bay == null) { EditorUtility.DisplayDialog("Борщ", "Сначала меню 2 (продукты).", "Ок"); return; }
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            Material mat = DS.PlateMat(log);
            var made = new List<Object>();

            var ob = new CatCooksBBQSetup.Bottle
            {
                Name = "Bottle_SunflowerOil", Product = B.Oil, Dose = 2, Segs = 20, H = 0.30f, R = 0.065f, Neck = 0.022f,
                Body = new Color(0.98f, 0.82f, 0.25f), Cap = new Color(0.15f, 0.45f, 0.15f),
                LabelBg = new Color(0.10f, 0.35f, 0.12f), LabelFg = new Color(1f, 0.90f, 0.40f), LabelAccent = new Color(0.99f, 0.80f, 0.10f),
                Icon = CatCooksBBQSetup.Icon.Sunflower, Line1 = "МАСЛО", Line2 = "ПОДСОЛНЕЧНОЕ"
            };
            GameObject og = SceneRoot(ob.Name, at);
            CatCooksBBQSetup.BuildBottle(og, ob, mat, log);
            LiquidDoseJarItem jar = DS.Need<LiquidDoseJarItem>(og);
            var jso = new SerializedObject(jar);
            DS.SetObj(jso, "_product", oil); DS.SetInt(jso, "_portionsPerDose", ob.Dose); DS.SetObj(jso, "_spout", og.transform.Find("Spout"));
            SerializedProperty inf = jso.FindProperty("_infinite"); if (inf != null) inf.boolValue = true;
            jso.ApplyModifiedProperties();
            ClientAuthNT(og, log);
            log.AppendLine($"✔ «{og.name}» — масло подсолнечное, ПКМ над тарой: {ob.Dose} порц.");
            made.Add(og);

            var sb = new CatCooksBBQSetup.Bottle
            {
                Name = "Shaker_BayLeaf", Product = B.Bay, Dose = 1, Segs = 18, H = 0.15f, R = 0.045f, Neck = 0.042f, Jar = true, Shaker = true,
                Body = new Color(0.88f, 0.90f, 0.80f), Cap = new Color(0.35f, 0.50f, 0.22f),
                LabelBg = new Color(0.30f, 0.42f, 0.16f), LabelFg = Color.white, LabelAccent = new Color(0.80f, 0.85f, 0.50f),
                Icon = CatCooksBBQSetup.Icon.Leaf, Line1 = "ЛАВРОВЫЙ", Line2 = "ЛИСТ"
            };
            GameObject sg = SceneRoot(sb.Name, at + Vector3.right * 0.25f);
            CatCooksBBQSetup.BuildBottle(sg, sb, mat, log);
            PowderShakerItem ps = DS.Need<PowderShakerItem>(sg);
            var pso = new SerializedObject(ps);
            DS.SetObj(pso, "_product", bay); DS.SetObj(pso, "_mouth", sg.transform.Find("Spout"));
            SerializedProperty pinf = pso.FindProperty("_infinite"); if (pinf != null) pinf.boolValue = true;
            pso.ApplyModifiedProperties();
            ClientAuthNT(sg, log);
            log.AppendLine($"✔ «{sg.name}» — лавровый лист, ПКМ над тарой: 1 доза (в бульон — 2 дозы)");
            made.Add(sg);

            Selection.objects = made.ToArray();
            EditorSceneManager.MarkSceneDirty(og.scene);
            log.AppendLine("\nПовторный запуск обновляет их на месте. Сохрани префабами, если нужно.");
            DS.Done("Борщ — масло и лавр", log);
        }

        internal static GameObject SceneRoot(string name, Vector3 at)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.parent == null && t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.position = at;
            return go;
        }

        // ───────────────────────── 7. ШАГ 4: ЗАЖАРКА ─────────────────────────

        [MenuItem(Root + "7. Шаг 4: томатная паста и зажарка (продукты, правила, сковорода на 40)", priority = 236)]
        public static void MakeStep4()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef paste = EnsureGroup("Group_Paste", "Пасты (уваренное)", "Густая масса: берётся мастерком, лежит гладкой кучкой.", FoodState.Puree, log);
            FoodGroupDef saute = EnsureGroup("Group_Saute", "Зажарка (сковорода)", "Жареные тёртые овощи — густой массой: видна горкой, берётся мастерком.", FoodState.Puree, log);
            if (saute != null && saute.StartState != FoodState.Puree)
            {
                saute.StartState = FoodState.Puree; EditorUtility.SetDirty(saute);
                log.AppendLine("✔ группа «Зажарка»: старт «Пюре» (было «Тёртое» — в сковороде было невидимо и мастерок не брал)");
            }

            log.AppendLine("\nПРОДУКТЫ:");
            ProductDef p1 = DS.Ensure(B.Paste, paste, new Color(0.62f, 0.08f, 0.05f), 0.08f, true, dir, ref next, log);
            ProductDef p2 = DS.Ensure(B.PasteBurnt, paste, new Color(0.22f, 0.07f, 0.04f), 0.08f, true, dir, ref next, log);
            ProductDef p3 = DS.Ensure(B.Saute, saute, new Color(0.86f, 0.58f, 0.22f), 0.08f, true, dir, ref next, log);
            ProductDef p4 = DS.Ensure(B.BeetStewed, saute, new Color(0.52f, 0.07f, 0.16f), 0.08f, true, dir, ref next, log);
            ProductDef p5 = DS.Ensure(B.Zazharka, saute, new Color(0.66f, 0.15f, 0.08f), 0.08f, true, dir, ref next, log);
            foreach (ProductDef p in new[] { p1, p2, p3, p4, p5 })
            {
                if (p == null) continue;
                if (p.Solid) { p.Solid = false; EditorUtility.SetDirty(p); }
                if (p.DebrisColor.a < 0.01f) { Color c = p.BaseColor; c.a = 1f; p.DebrisColor = c; EditorUtility.SetDirty(p); }
            }
            if (p1 != null && paste != null && p1.Group != paste) { p1.Group = paste; EditorUtility.SetDirty(p1); }

            log.AppendLine("\nПОМИДОРЫ:");
            ProductDef tom = FindTomatoAsset();
            if (tom == null) log.AppendLine("✖ не нашёл помидор (продукт со словом «помидор» или «томат») — томатная паста не соберётся. Напиши, как он называется");
            else
            {
                log.AppendLine($"✔ помидор — «{tom.Title}»");
                RuleW(tom, FoodState.Washed, FoodProcess.Grate, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
                RuleW(tom, FoodState.Peeled, FoodProcess.Grate, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
                RuleW(tom, FoodState.Raw, FoodProcess.Grate, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
            }
            foreach (string t in new[] { B.Oil, B.Beet, B.Onion, B.Carrot })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}» — меню 2");
            log.AppendLine(DS.Find(B.Vinegar) != null || DS.Find(B.AppleVinegar) != null ? "✔ уксус есть" : "✖ нет «Уксус» / «Яблочный уксус»");
            log.AppendLine(DS.Find(B.Lard) != null ? "✔ «Сало» — можно вместо масла в пассеровку" : "· сала нет — только масло");

            // сковорода: зажарка целиком (лук 6 + морковь 7 + свёкла 6 + масло, уксус, паста) в 20 порций не влезает
            CookwareDef def = AssetDatabase.LoadAssetAtPath<CookwareDef>($"{VesselDir}/{PanDefName}.asset");
            if (def != null && def.Capacity < 40) { def.Capacity = 40; EditorUtility.SetDirty(def); log.AppendLine($"\n✔ сковорода {PanDefName}: вместимость → 40 порц. (зажарка целиком)"); }
            if (def != null && def.Absorbs) { def.Absorbs = false; EditorUtility.SetDirty(def); log.AppendLine("✔ сковорода: целые куски больше НЕ глотает — лежат собой и жарятся (раньше превращались в горку/жидкость). Старое содержимое — K (опустошить)"); }

            // целые овощи на сковороде — просто жарятся (темнеют), сало — жарится и трётся в измельчителе (шкварки в зажарку)
            FoodGroupDef vegG = DS.FindGroup("Group_Vegetables");
            if (vegG != null)
            {
                GroupRule(vegG, FoodState.Peeled, FoodProcess.Fry, FoodState.Fried, 16f, 0.35f, FoodState.Undercooked, FoodState.Overcooked, log);
                GroupRule(vegG, FoodState.Washed, FoodProcess.Fry, FoodState.Fried, 18f, 0.35f, FoodState.Undercooked, FoodState.Overcooked, log);
            }
            ProductDef lardP = DS.Find(B.Lard);
            if (lardP != null)
            {
                RuleW(lardP, FoodState.Butchered, FoodProcess.Grate, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
                RuleW(lardP, FoodState.Butchered, FoodProcess.Mince, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
            }
            foreach (CookwareFillTuner t in Object.FindObjectsByType<CookwareFillTuner>(FindObjectsSortMode.None))
            { t.EditorApply(); log.AppendLine($"  ✔ «{t.name}»: жидкость пересчитана под вместимость"); }

            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("\n✔ FoodDatabase пересобран — СОХРАНИ СЦЕНУ");
            }
            CookRecipeBook.Reload();
            log.AppendLine("\nДальше: выдели сковороду (и казан, если пасту там) → CatCooks → Еда → «Тара: принимать ВСЁ» — масло, уксус, бульон станут жидкостью.");
            log.AppendLine("\nТОМАТНАЯ ПАСТА: помидоры помыть → измельчитель → тёртые в сковороду → на огонь БЕЗ раздува 60 с → паста (×0.5). Раздул (огонь > 175 °C) — пригорит.");
            log.AppendLine("  Готовую пасту — мастерком в ведро (сковорода нужна чистой под зажарку).");
            log.AppendLine("ЗАЖАРКА (одна сковорода, по очереди):");
            log.AppendLine("  1) лук тёртый 1 + морковь тёртая 1 + масло 1 нажатие (или сало через измельчитель — шкварки) → жарить 20 с → «Пассеровка»");
            log.AppendLine("  Целые овощи и пласты сала на сковороде просто жарятся (темнеют) — в рецепт идёт только тёртое.");
            log.AppendLine("  2) + свёкла тёртая (запечённая) 1 + уксус 2 нажатия → тушить 30 с → «Свёкла тушёная»");
            log.AppendLine("  3) + томатная паста 2 мастерка → тушить 20 с → «Зажарка для борща»");
            DS.Done("Борщ — шаг 4", log);
        }

        /// <summary>Правило группы (для всех её продуктов). Есть такое From+Process — оставить как есть.</summary>
        private static void GroupRule(FoodGroupDef g, FoodState from, FoodProcess proc, FoodState to, float sec, float win, FoodState early, FoodState late, StringBuilder log)
        {
            foreach (FoodRule r in g.Rules) if (r.From == from && r.Process == proc) return;
            g.Rules.Add(new FoodRule { From = from, Process = proc, To = to, Seconds = sec, Window = win, TooEarly = early, TooLate = late });
            EditorUtility.SetDirty(g);
            log.AppendLine($"＋ группа «{g.Title}»: {FoodStateInfo.NameOf(from)} → {FoodStateInfo.NameOf(proc)} → {FoodStateInfo.NameOf(to)} ({sec * (1f - win):0}–{sec * (1f + win):0} с)");
        }

        private static ProductDef FindTomatoAsset()
        {
            ProductDef exact = DS.Find(B.Tomato);
            if (exact != null) return exact;
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d == null || string.IsNullOrEmpty(d.Title)) continue;
                string s = d.Title.ToLowerInvariant();
                if ((s.Contains("помидор") || s.Contains("томат")) && !s.Contains("паст") && !s.Contains("соус") && !s.Contains("сок")) return d;
            }
            return null;
        }

        /// <summary>Своя группа продуктов (стартовое состояние). Есть — оставить; нет — рядом с другими группами.</summary>
        internal static FoodGroupDef EnsureGroup(string assetName, string title, string note, FoodState start, StringBuilder log)
        {
            FoodGroupDef g = DS.FindGroup(assetName);
            if (g != null) { log.AppendLine($"✔ группа {assetName}"); return g; }
            FoodGroupDef veg = DS.FindGroup("Group_Vegetables");
            string dir = veg != null ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(veg)).Replace('\\', '/') : "Assets/CatCooks/Food/Groups";
            DS.EnsureDir(dir);
            g = ScriptableObject.CreateInstance<FoodGroupDef>();
            g.Title = title; g.Note = note; g.StartState = start;
            AssetDatabase.CreateAsset(g, $"{dir}/{assetName}.asset");
            log.AppendLine($"＋ группа {dir}/{assetName}.asset — «{title}», старт «{FoodStateInfo.NameOf(start)}»");
            return g;
        }

        // ───────────────────────── 8. САЛО И ГОЛЯШКА — МОДЕЛИ ─────────────────────────

        [MenuItem(Root + "8. Сало и голяшка — модели в сцену (потом сохранить префабами)", priority = 237)]
        public static void MakeMeatModels()
        {
            var log = new StringBuilder();
            ProductDef lard = DS.Find(B.Lard), shank = DS.Find(B.Shank);
            if (lard == null || shank == null) { EditorUtility.DisplayDialog("Борщ", "Нет продукта «Сало» или «Говяжья голяшка» — сначала меню 2 (и Мясо → Новые туши → 1).", "Ок"); return; }
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            float WS = Mathf.Max(0.25f, MeatCutSettings.WorldScale);
            Material mat = DS.PlateMat(log);

            float lw = 0.16f * WS, ld = 0.09f * WS, lh = 0.045f * WS;
            Mesh lm = SaveMesh(LardMesh(lw, ld, lh), "Lard_Slab_Mesh", log);
            GameObject lg = SceneObj("Lard_Slab", at);
            lg.GetComponent<MeshFilter>().sharedMesh = lm;
            lg.GetComponent<MeshRenderer>().sharedMaterials = new[] { mat };
            BoxCollider bc = Get<BoxCollider>(lg); bc.center = lm.bounds.center; bc.size = lm.bounds.size;
            MeatPiece(lg, lard, 1.2f, log);
            log.AppendLine($"✔ «Lard_Slab» — пласт сала {lw * 100f:0}×{ld * 100f:0}×{lh * 100f:0} см: снизу мясная прослойка, белый жир, розовая полоска, шкурка сверху");

            float r = 0.055f * WS, h = 0.03f * WS;
            Mesh sm = SaveMesh(ShankMesh(r, h), "Beef_Shank_Mesh", log);
            GameObject sg = SceneObj("Beef_Shank", at + Vector3.right * (lw + r * 2f));
            sg.GetComponent<MeshFilter>().sharedMesh = sm;
            sg.GetComponent<MeshRenderer>().sharedMaterials = new[] { mat };
            MeshCollider mc = Get<MeshCollider>(sg); mc.sharedMesh = sm; mc.convex = true;
            MeatPiece(sg, shank, 1.5f, log);
            log.AppendLine($"✔ «Beef_Shank» — шайба голяшки Ø{r * 200f:0} см, толщина {h * 100f:0} см: мозговая кость в центре, мясо кольцом с прожилками, плёнка по краю");

            Selection.objects = new Object[] { lg, sg };
            EditorSceneManager.MarkSceneDirty(lg.scene);
            log.AppendLine("\nДАЛЬШЕ РУКАМИ:");
            log.AppendLine("  1. Перетащи Lard_Slab и Beef_Shank из сцены в окно Project — два префаба.");
            log.AppendLine("  2. Они должны попасть в Spawnable Prefabs (NetworkManager → DefaultPrefabObjects; обычно FishNet добавляет сам — проверь).");
            log.AppendLine("  3. Выдели оба префаба в Project → Борщ → 9 — туши начнут ронять их вместо обычных кусков.");
            log.AppendLine("  Объекты в сцене после этого можно удалить.");
            DS.Done("Борщ — сало и голяшка", log);
        }

        /// <summary>Кусок мяса своим префабом: FoodPiece (разделано, 1 шт.), вид по состоянию (опалено/жарено темнеет), физика, сеть.</summary>
        internal static void MeatPiece(GameObject go, ProductDef p, float mass, StringBuilder log)
        {
            int food = LayerMask.NameToLayer("Food");
            if (food >= 0) go.layer = food;
            Get<FishNet.Object.NetworkObject>(go);
            Get<FishNet.Component.Transforming.NetworkTransform>(go);
            ClientAuthNT(go, log);
            Rigidbody rb = Get<Rigidbody>(go);
            rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
            FoodPiece fp = Get<FoodPiece>(go);
            var so = new SerializedObject(fp);
            DS.SetObj(so, "_product", p);
            SerializedProperty st = so.FindProperty("_startState"); if (st != null) st.intValue = (int)FoodState.Butchered;
            DS.SetInt(so, "_count", 1);
            so.ApplyModifiedPropertiesWithoutUndo();
            Get<FoodStateVisual>(go);
            Get<Suckable>(go);
        }

        /// <summary>Пласт сала: брусок, бока полосами (мясо снизу, жир, розовая прослойка, жир, шкурка), верх — шкурка.</summary>
        private static Mesh LardMesh(float W, float D, float H)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            Color meat = new Color(0.86f, 0.45f, 0.42f), fat = new Color(0.97f, 0.94f, 0.88f), stripe = new Color(0.90f, 0.55f, 0.50f), skin = new Color(0.86f, 0.66f, 0.47f);
            float[] y = { 0f, 0.18f, 0.55f, 0.63f, 0.92f, 1f };
            Color[] band = { meat, fat, stripe, fat, skin };
            float x0 = -W * 0.5f, x1 = W * 0.5f, z0 = -D * 0.5f, z1 = D * 0.5f;
            // волна по верху и по прослойкам — не идеально ровный брусок
            for (int f = 0; f < 4; f++)
            {
                Vector3 a, b, nn;
                if (f == 0) { a = new Vector3(x0, 0f, z1); b = new Vector3(x1, 0f, z1); nn = Vector3.forward; }
                else if (f == 1) { a = new Vector3(x1, 0f, z1); b = new Vector3(x1, 0f, z0); nn = Vector3.right; }
                else if (f == 2) { a = new Vector3(x1, 0f, z0); b = new Vector3(x0, 0f, z0); nn = Vector3.back; }
                else { a = new Vector3(x0, 0f, z0); b = new Vector3(x0, 0f, z1); nn = Vector3.left; }
                const int seg = 8;
                for (int k = 0; k < band.Length; k++)
                    for (int s = 0; s < seg; s++)
                    {
                        float u0 = s / (float)seg, u1 = (s + 1) / (float)seg;
                        Vector3 p0 = Vector3.Lerp(a, b, u0), p1 = Vector3.Lerp(a, b, u1);
                        float w0a = Wave(p0, k), w1a = Wave(p1, k), w0b = Wave(p0, k + 1), w1b = Wave(p1, k + 1);
                        Quad(v, n, c, t, p0 + Vector3.up * (y[k] * H + w0a * H), p1 + Vector3.up * (y[k] * H + w1a * H),
                             p1 + Vector3.up * (y[k + 1] * H + w1b * H), p0 + Vector3.up * (y[k + 1] * H + w0b * H), nn, band[k]);
                    }
            }
            // верх (шкурка) — сеткой, чтобы волна совпала с боками; низ — мясо
            const int gx = 8, gz = 5;
            for (int i = 0; i < gx; i++)
                for (int j = 0; j < gz; j++)
                {
                    Vector3 p00 = new Vector3(Mathf.Lerp(x0, x1, i / (float)gx), 0f, Mathf.Lerp(z0, z1, j / (float)gz));
                    Vector3 p10 = new Vector3(Mathf.Lerp(x0, x1, (i + 1) / (float)gx), 0f, p00.z);
                    Vector3 p11 = new Vector3(p10.x, 0f, Mathf.Lerp(z0, z1, (j + 1) / (float)gz));
                    Vector3 p01 = new Vector3(p00.x, 0f, p11.z);
                    float yt = H;
                    Quad(v, n, c, t, p01 + Vector3.up * (yt + Wave(p01, 5) * H), p11 + Vector3.up * (yt + Wave(p11, 5) * H),
                         p10 + Vector3.up * (yt + Wave(p10, 5) * H), p00 + Vector3.up * (yt + Wave(p00, 5) * H), Vector3.up, skin * 0.95f);
                    Quad(v, n, c, t, p00, p10, p11, p01, Vector3.down, meat * 0.95f);
                }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals();          // лёгкая волна — нормали по факту (вершины граней свои, швы резкие)
            m.RecalculateBounds();
            return m;
        }

        private static float Wave(Vector3 p, int level)
        {
            if (level <= 0) return 0f;
            return 0.04f * Mathf.Sin(p.x * 9f + level * 1.7f) * Mathf.Cos(p.z * 7f + level);
        }

        /// <summary>Четырёхугольник a-b-c-d (против часовой, если смотреть со стороны нормали) — вершины свои, цвет один.</summary>
        internal static void Quad(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> t, Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 nn, Color col)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            for (int k = 0; k < 4; k++) { n.Add(nn); c.Add(col); }
            Vector3 fn = Vector3.Cross(b - a, cc - a);
            if (Vector3.Dot(fn, nn) >= 0f) { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
            else { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
        }

        /// <summary>
        /// Шайба голяшки (оссобуко): два среза — мозг в центре, кольцо кости, мясо кольцом с прожилками, плёнка по краю;
        /// бок — тёмная плёнка. Край неровный (мышцы).
        /// </summary>
        private static Mesh ShankMesh(float R, float H)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            Color marrow = new Color(0.80f, 0.36f, 0.30f), bone = new Color(0.97f, 0.93f, 0.84f), meat = new Color(0.70f, 0.18f, 0.16f),
                  vein = new Color(0.90f, 0.70f, 0.64f), rim = new Color(0.93f, 0.84f, 0.76f), side = new Color(0.58f, 0.20f, 0.17f);
            const int Seg = 36;
            // кольца: (радиус-доля, цвет внутри кольца до этого радиуса)
            float[] rr = { 0.16f, 0.27f, 0.45f, 0.62f, 0.78f, 0.9f, 1f };
            for (int cap = 0; cap < 2; cap++)
            {
                float y = cap == 0 ? H : 0f;
                Vector3 nn = cap == 0 ? Vector3.up : Vector3.down;
                // центр (мозг) — веер
                int ctr = v.Count; v.Add(new Vector3(0f, y, 0f)); n.Add(nn); c.Add(marrow);
                int ring0 = v.Count;
                for (int j = 0; j < Seg; j++) { v.Add(P(R, rr[0], j, Seg, y)); n.Add(nn); c.Add(marrow); }
                for (int j = 0; j < Seg; j++)
                {
                    int a = ring0 + j, b = ring0 + (j + 1) % Seg;
                    Tri(v, t, ctr, a, b, nn);
                }
                for (int k = 1; k < rr.Length; k++)
                {
                    int inner = v.Count;
                    for (int j = 0; j < Seg; j++) { v.Add(P(R, rr[k - 1], j, Seg, y)); n.Add(nn); c.Add(BandColor(k, j, bone, meat, vein, rim)); }
                    int outer = v.Count;
                    for (int j = 0; j < Seg; j++) { v.Add(P(R, rr[k], j, Seg, y)); n.Add(nn); c.Add(BandColor(k, j + 1, bone, meat, vein, rim)); }
                    for (int j = 0; j < Seg; j++)
                    {
                        int a = inner + j, b = inner + (j + 1) % Seg, a2 = outer + j, b2 = outer + (j + 1) % Seg;
                        Tri(v, t, a, b, a2, nn); Tri(v, t, b, b2, a2, nn);
                    }
                }
            }
            // бок
            int s0 = v.Count;
            for (int j = 0; j <= Seg; j++)
            {
                Vector3 top = P(R, 1f, j % Seg, Seg, H), bot = P(R, 1f, j % Seg, Seg, 0f);
                Vector3 nn = new Vector3(top.x, 0f, top.z).normalized;
                v.Add(bot); n.Add(nn); c.Add(side);
                v.Add(top); n.Add(nn); c.Add(side);
            }
            for (int j = 0; j < Seg; j++)
            {
                int a = s0 + j * 2;
                Tri(v, t, a, a + 1, a + 2, n[a]); Tri(v, t, a + 2, a + 1, a + 3, n[a + 2]);
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Треугольник лицом в сторону nn (порядок вершин подбирается сам).</summary>
        internal static void Tri(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 nn)
        {
            Vector3 fn = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(fn, nn) >= 0f) { t.Add(a); t.Add(b); t.Add(c); } else { t.Add(a); t.Add(c); t.Add(b); }
        }

        /// <summary>Точка кольца: мясо — край неровный, кость и мозг — ровные; центр шайбы — середина высоты не сдвигаем.</summary>
        private static Vector3 P(float R, float frac, int j, int seg, float y)
        {
            float a = j / (float)seg * Mathf.PI * 2f;
            float wob = frac > 0.3f ? 1f + (0.06f * Mathf.Sin(a * 3f + 0.4f) + 0.035f * Mathf.Sin(a * 5f + 1.3f)) * (frac - 0.27f) / 0.73f : 1f;
            float r = R * frac * wob;
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }

        private static Color BandColor(int k, int j, Color bone, Color meat, Color vein, Color rim)
        {
            if (k == 1) return bone;
            if (k == rr6) return rim;
            // мясо с прожилками: светлые лучики по кругу
            float s = Mathf.Sin(j * 1.9f + k * 2.3f);
            return s > 0.75f ? Color.Lerp(meat, vein, 0.6f) : Color.Lerp(meat, meat * 0.85f, (k % 2) * 0.5f);
        }

        private const int rr6 = 6;

        // ───────────────────────── 9. НАЗНАЧИТЬ ПРЕФАБЫ ─────────────────────────

        [MenuItem(Root + "9. Туши: ронять сало и голяшку префабами (выдели оба префаба в Project)", priority = 238)]
        public static void AssignMeatPrefabs()
        {
            var log = new StringBuilder();
            MeatCutSettings st = FindSettings();
            if (st == null) { EditorUtility.DisplayDialog("Борщ", "Нет MeatCutSettings — Мясо → Новые туши → 1.", "Ок"); return; }
            GameObject lard = null, shank = null;
            foreach (Object o in Selection.objects)
            {
                GameObject go = o as GameObject;
                if (go == null || !EditorUtility.IsPersistent(go)) continue;
                FoodPiece fp = go.GetComponent<FoodPiece>();
                string title = "";
                if (fp != null) { ProductDef d = new SerializedObject(fp).FindProperty("_product").objectReferenceValue as ProductDef; if (d != null) title = d.Title; }
                string nm = go.name.ToLowerInvariant();
                if (title == B.Lard || nm.Contains("lard") || nm.Contains("сало")) lard = go;
                else if (title == B.Shank || nm.Contains("shank") || nm.Contains("голяш")) shank = go;
            }
            if (lard == null && shank == null)
            {
                EditorUtility.DisplayDialog("Борщ", "Выдели в окне Project префабы Lard_Slab и/или Beef_Shank (сначала меню 8 и перетащить из сцены в Project).", "Ок");
                return;
            }
            Undo.RecordObject(st, "Сало и голяшка");
            if (lard != null) { st.LardPrefab = lard; log.AppendLine($"✔ свиная грудинка → «{lard.name}» ×{st.LardPerBrisket} (Lard Per Brisket в MeatCutSettings)"); Need(lard, log); }
            if (shank != null) { st.ShankPrefab = shank; log.AppendLine($"✔ говяжья нога → «{shank.name}» ×{st.ShankPerLeg} шайбы (Shank Per Leg)"); Need(shank, log); }
            EditorUtility.SetDirty(st);
            AssetDatabase.SaveAssets();
            log.AppendLine($"\nНастройки: {AssetDatabase.GetAssetPath(st)}. Туши в сцене трогать не нужно — они берут это отсюда.");
            log.AppendLine("Убрать — очисти поле Lard Prefab / Shank Prefab в MeatCutSettings: части снова будут обычными кусками.");
            log.AppendLine("Проверь, что оба префаба в Spawnable Prefabs (NetworkManager → DefaultPrefabObjects).");
            DS.Done("Борщ — куски туши", log);
        }

        private static void Need(GameObject prefab, StringBuilder log)
        {
            if (prefab.GetComponent<FishNet.Object.NetworkObject>() == null) log.AppendLine($"  ✖ у «{prefab.name}» нет Network Object — выпадать не будет");
            if (prefab.GetComponent<FoodPiece>() == null) log.AppendLine($"  ✖ у «{prefab.name}» нет Food Piece");
        }

        // ───────────────────────── 10. ПОМИДОР ─────────────────────────

        [MenuItem(Root + "10. Помидор — настроить выделенный (чистый / грязный / гнилой)", priority = 239)]
        public static void SetupTomato()
        {
            var sel = new List<GameObject>();
            foreach (GameObject g in Selection.gameObjects) if (g != null && g.scene.IsValid()) sel.Add(g);
            if (sel.Count == 0) { EditorUtility.DisplayDialog("Борщ — помидор", "Выдели помидор в сцене (корень модели).", "Ок"); return; }
            var log = new StringBuilder();
            ProductDef tom = FindTomatoAsset();
            if (tom == null)
            {
                ushort next = (ushort)(DS.MaxId() + 1);
                FoodGroupDef veg = Or(DS.GroupOf("Морковь"), DS.FindGroup("Group_Vegetables"));
                tom = DS.Ensure(B.Tomato, veg, new Color(0.86f, 0.16f, 0.10f), 0.1f, false, DS.ProductsDir(), ref next, log);
            }
            if (tom == null) { DS.Done("Борщ — помидор", log); return; }
            if (tom.DebrisColor.a < 0.01f) { tom.DebrisColor = new Color(0.82f, 0.12f, 0.08f, 1f); EditorUtility.SetDirty(tom); }
            RuleW(tom, FoodState.Washed, FoodProcess.Grate, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
            RuleW(tom, FoodState.Peeled, FoodProcess.Grate, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);

            foreach (GameObject go in sel)
            {
                Undo.RegisterFullObjectHierarchyUndo(go, "Помидор");
                SetupVeg(go, tom.Title, 6, false, log);
                FoodStateVisual vis = go.GetComponent<FoodStateVisual>();
                Material clean, dirty, rot;
                FindStateMats(go, out clean, out dirty, out rot, log);
                if (clean != null) { vis.EditorSetLocalLook(FoodState.Washed, clean, -1, null); vis.EditorSetLocalLook(FoodState.Peeled, clean, -1, null); vis.EditorSetLocalLook(FoodState.Raw, clean, -1, null); }
                if (dirty != null) { vis.EditorSetLocalLook(FoodState.Dirty, dirty, -1, null); vis.EditorDirtyTint = 0f; }
                if (rot != null) vis.EditorSetLocalLook(FoodState.Spoiled, rot, -1, null);
                EditorUtility.SetDirty(vis);
                log.AppendLine($"  вид: грязный — {(dirty != null ? "«" + dirty.name + "»" : "темнее (своего материала нет)")}, мытый — {(clean != null ? "«" + clean.name + "»" : "как есть")}, гнилой — {(rot != null ? "«" + rot.name + "»" : "темнее")}");
                log.AppendLine("  сгоревший/жареный — подкраска сама; тёртый — красной горкой");
            }
            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null) { db.CollectFromProject(); EditorUtility.SetDirty(db); EditorSceneManager.MarkSceneDirty(db.gameObject.scene); }
            EditorSceneManager.MarkSceneDirty(sel[0].scene);
            log.AppendLine("\nЕсли материал не угадан — FoodStateVisual → Local Looks: поставь руками (Грязное / Мытое / Испортилось).");
            log.AppendLine("Сохрани помидор префабом (и в Spawnable Prefabs), если нужно больше штук. Сохрани сцену.");
            DS.Done("Борщ — помидор", log);
        }

        /// <summary>
        /// Материалы состояний модели: из папок её материалов и меша (и подпапки Materials). По именам:
        /// грязный — dirty/грязн/dirt/mud, гнилой — rot/гнил/spoil/bad/mold, чистый — clean/чист/fresh/wash (иначе — тот, что стоит сейчас).
        /// </summary>
        private static void FindStateMats(GameObject go, out Material clean, out Material dirty, out Material rot, StringBuilder log)
        {
            clean = null; dirty = null; rot = null;
            var dirs = new HashSet<string>();
            Material current = null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (current == null) current = m;
                    string p = AssetDatabase.GetAssetPath(m);
                    if (!string.IsNullOrEmpty(p)) dirs.Add(System.IO.Path.GetDirectoryName(p).Replace('\\', '/'));
                }
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    string p = AssetDatabase.GetAssetPath(mf.sharedMesh);
                    if (!string.IsNullOrEmpty(p))
                    {
                        string d = System.IO.Path.GetDirectoryName(p).Replace('\\', '/');
                        dirs.Add(d);
                        if (AssetDatabase.IsValidFolder(d + "/Materials")) dirs.Add(d + "/Materials");
                    }
                }
            }
            var mats = new List<Material>();
            foreach (string d in dirs)
            {
                if (!AssetDatabase.IsValidFolder(d)) continue;
                foreach (string g in AssetDatabase.FindAssets("t:Material", new[] { d }))
                {
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                    if (m != null && !mats.Contains(m)) mats.Add(m);
                }
            }
            foreach (Material m in mats)
            {
                string n = m.name.ToLowerInvariant();
                if (dirty == null && (n.Contains("dirty") || n.Contains("грязн") || n.Contains("dirt") || n.Contains("mud"))) dirty = m;
                else if (rot == null && (n.Contains("rot") || n.Contains("гнил") || n.Contains("spoil") || n.Contains("bad") || n.Contains("mold"))) rot = m;
                else if (clean == null && (n.Contains("clean") || n.Contains("чист") || n.Contains("fresh") || n.Contains("wash") || n.Contains("мыт"))) clean = m;
            }
            if (clean == null && current != null && current != dirty && current != rot) clean = current;
            log.AppendLine($"  материалы в папках модели: {mats.Count} — " + string.Join(", ", mats.ConvertAll(x => x.name)));
        }

        // ───────────────────────── 11. КАСТРЮЛЯ-СУП ─────────────────────────

        [MenuItem(Root + "11. Кастрюля-суп: куски плавают и варятся (выдели кастрюлю)", priority = 240)]
        public static void SetupSoupPot()
        {
            var log = new StringBuilder();
            var pots = new List<Cookware>();
            foreach (GameObject g in Selection.gameObjects)
            {
                if (g == null || !g.scene.IsValid()) continue;
                Cookware c = g.GetComponentInParent<Cookware>();
                if (c == null) c = g.GetComponentInChildren<Cookware>();
                if (c != null && !pots.Contains(c)) pots.Add(c);
            }
            if (pots.Count == 0) { EditorUtility.DisplayDialog("Борщ", "Выдели кастрюлю в сцене (на ней Cookware).", "Ок"); return; }
            foreach (Cookware c in pots)
            {
                Undo.RegisterFullObjectHierarchyUndo(c.gameObject, "Кастрюля-суп");
                DS.Need<CookwareSoupFloat>(c.gameObject);
                foreach (CookwareLiquidVisual lv in c.GetComponentsInChildren<CookwareLiquidVisual>(true))
                {
                    var so = new SerializedObject(lv);
                    SetB(so, "_soup", true);
                    so.ApplyModifiedProperties();
                }
                EditorUtility.SetDirty(c.gameObject);
                log.AppendLine($"✔ «{c.name}»: Cookware Soup Float — целые куски плавают собой (всплывают/тонут, кружат), варятся, их можно достать");
                if (c.Def != null && !c.Def.Absorbs) log.AppendLine("  ⚠ у этой тары Def не глотает вообще — и так всё лежит кусками");
            }
            // варка целых кусков: не передержать за 90 с бульона (иначе «переготовлено» = не в счёт)
            log.AppendLine("\nПРАВИЛА ВАРКИ:");
            foreach (ProductDef p in TorchVeg())
            {
                RuleW(p, FoodState.Peeled, FoodProcess.Boil, FoodState.Boiled, 80f, 0.5f, FoodState.None, FoodState.Overcooked, log);
                RuleW(p, FoodState.Torched, FoodProcess.Boil, FoodState.Boiled, 80f, 0.5f, FoodState.None, FoodState.Overcooked, log);
            }
            foreach (string t in new[] { B.Bones, B.Shank, B.Lard })
            {
                ProductDef p = DS.Find(t);
                if (p == null) continue;
                RuleW(p, FoodState.Butchered, FoodProcess.Boil, FoodState.Boiled, 100f, 0.5f, FoodState.None, FoodState.Overcooked, log);
                RuleW(p, FoodState.Torched, FoodProcess.Boil, FoodState.Boiled, 100f, 0.5f, FoodState.None, FoodState.Overcooked, log);
            }
            FoodGroupDef vegG = DS.FindGroup("Group_Vegetables");
            if (vegG != null)
            {
                GroupRule(vegG, FoodState.Peeled, FoodProcess.Boil, FoodState.Boiled, 60f, 0.5f, FoodState.None, FoodState.Overcooked, log);
                GroupRule(vegG, FoodState.Washed, FoodProcess.Boil, FoodState.Boiled, 70f, 0.5f, FoodState.None, FoodState.Overcooked, log);
            }
            BrothLiquid(log);
            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null) { db.CollectFromProject(); EditorUtility.SetDirty(db); }
            EditorSceneManager.MarkSceneDirty(pots[0].gameObject.scene);
            log.AppendLine("\nКости/голяшка варятся 50–150 с, опалённые лук и морковь — 40–120 с (бульон кипит 90 с — успевают, не переваренные).");
            log.AppendLine("Варёные куски темнеют; достать руками — лежат варёными. Сохрани сцену.");
            DS.Done("Борщ — кастрюля-суп", log);
        }

        /// <summary>«Бульон для борща» — жидкость (группа «Жидкое»), и в каждой жидкости тар сцены — роль «Жидкость».</summary>
        private static void BrothLiquid(StringBuilder log)
        {
            LiquidProduct(DS.Find(B.Broth), log);
            LiquidProduct(DS.Find(B.Soup), log);
        }

        /// <summary>Продукт — жидкость (группа «Жидкое») и роль «Жидкость» во всех жидкостях тар сцены.</summary>
        internal static void LiquidProduct(ProductDef broth, StringBuilder log)
        {
            if (broth == null) return;
            FoodGroupDef liq = Or(DS.GroupOf("Вода"), DS.FindGroup("Group_Liquid"));
            if (liq != null && broth.Group != liq) { broth.Group = liq; EditorUtility.SetDirty(broth); log.AppendLine($"✔ «{broth.Title}» → группа «{liq.Title}» (жидкость, льётся, кипит)"); }
            int n = 0;
            foreach (CookwareLiquidVisual lv in Object.FindObjectsByType<CookwareLiquidVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(lv);
                SerializedProperty arr = so.FindProperty("_products");
                if (arr == null) continue;
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue == broth) { has = true; break; }
                if (has) continue;
                arr.arraySize++;
                SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                e.FindPropertyRelative("Product").objectReferenceValue = broth;
                e.FindPropertyRelative("Role").intValue = (int)CookwareLiquidVisual.Role.Liquid;
                e.FindPropertyRelative("Color").colorValue = new Color(0f, 0f, 0f, 0f);
                so.ApplyModifiedProperties();
                n++;
            }
            if (n > 0) log.AppendLine($"✔ «{broth.Title}» — жидкость в {n} тарах сцены");
        }

        // ───────────────────────── 12. ШАГ 5: БОРЩ ─────────────────────────

        [MenuItem(Root + "12. Шаг 5: борщ в кастрюле (продукт, правила)", priority = 241)]
        public static void MakeStep5()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef liq = Or(DS.GroupOf("Вода"), DS.FindGroup("Group_Liquid"));
            ProductDef soup = DS.Ensure(B.Soup, liq, new Color(0.64f, 0.10f, 0.12f), 0.1f, true, dir, ref next, log);
            if (soup != null && soup.Solid) { soup.Solid = false; EditorUtility.SetDirty(soup); }
            foreach (string t in new[] { B.Broth, B.Potato, B.Cabbage, B.Zazharka, B.Salt })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}»");
            BrothLiquid(log);
            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null) { db.CollectFromProject(); EditorUtility.SetDirty(db); EditorSceneManager.MarkSceneDirty(db.gameObject.scene); log.AppendLine("✔ FoodDatabase пересобран — СОХРАНИ СЦЕНУ"); }
            CookRecipeBook.Reload();
            log.AppendLine($"\nРЕЦЕПТ «{B.Soup}» — в кастрюле с «Бульоном для борща» (кости и овощи из бульона можно вынуть, можно оставить):");
            log.AppendLine($"  картофель {B.SoupPotato} порц. (≈1 шт.: целая, кусками или кубиками — любая, кроме грязной)");
            log.AppendLine($"  + капуста ТЁРТАЯ {B.SoupCabbage} порц. (≈1 кочан из измельчителя) + зажарка {B.SoupZazharka} порц. (2 мастерка из сковороды) + соль 1 мастерок (или 1–3 дозы)");
            log.AppendLine($"  кипятить {B.SoupSeconds:0} с → «{B.Soup}» (красный, с оранжевым жирком). Допуск ±50%.");
            DS.Done("Борщ — шаг 5", log);
        }

        // ───────────────────────── 13. ШАГ 6: ПРОДУКТЫ ПОДАЧИ ─────────────────────────

        [MenuItem(Root + "13. Шаг 6: масло чесночное, затирка, пампушки (продукты и правила)", priority = 242)]
        public static void MakeStep6()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef veg = Or(DS.GroupOf("Морковь"), DS.FindGroup("Group_Vegetables"));
            FoodGroupDef liq = Or(DS.GroupOf("Вода"), DS.FindGroup("Group_Liquid"));
            FoodGroupDef paste = Or(DS.FindGroup("Group_Paste"), DS.GroupOf(B.Paste));
            FoodGroupDef bread = Or(Or(DS.GroupOf("Хлеб"), DS.GroupOf("Тесто")), DS.FindGroup("Group_SubRecipe"));
            // чеснок — твой (зубчики): измельчитель → «измельчённый» (тёртое)
            ProductDef garlic = Or(DS.Find(B.Garlic), FindLike("зубчик"));
            if (garlic == null) log.AppendLine("✖ не нашёл «Зубчик чеснока» — нужен для масла и затирки (Острый соус → 1 создаёт его)");
            else
            {
                log.AppendLine($"✔ чеснок — «{garlic.Title}»");
                foreach (FoodState st in new[] { FoodState.Raw, FoodState.Washed, FoodState.Peeled, FoodState.Pieces })
                    RuleW(garlic, st, FoodProcess.Grate, FoodState.Grated, 1.5f, 0f, FoodState.None, FoodState.None, log);
            }
            ProductDef lardP = DS.Find(B.Lard);
            if (lardP != null)
            {
                RuleW(lardP, FoodState.Butchered, FoodProcess.Grate, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
                RuleW(lardP, FoodState.Butchered, FoodProcess.Mince, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
                RuleW(lardP, FoodState.Raw, FoodProcess.Grate, FoodState.Grated, 2.5f, 0f, FoodState.None, FoodState.None, log);
            }
            ProductDef gOil = DS.Ensure(B.GarlicOil, liq, new Color(0.86f, 0.78f, 0.30f), 0.05f, true, dir, ref next, log);
            ProductDef zat = DS.Ensure(B.Zatirka, paste, new Color(0.95f, 0.86f, 0.80f), 0.06f, true, dir, ref next, log);
            ProductDef bun = DS.Ensure(B.Bun, bread, new Color(0.88f, 0.60f, 0.28f), 0.1f, true, dir, ref next, log);
            ProductDef bunG = DS.Ensure(B.BunGarlic, bread, new Color(0.84f, 0.62f, 0.30f), 0.1f, true, dir, ref next, log);
            foreach (ProductDef p in new[] { gOil, zat })
                if (p != null && p.Solid) { p.Solid = false; EditorUtility.SetDirty(p); }
            if (zat != null && paste != null && zat.Group != paste) { zat.Group = paste; EditorUtility.SetDirty(zat); }
            LiquidProduct(gOil, log);
            log.AppendLine(DS.Find(B.SourCream) != null ? "✔ «Сметана» есть (Десерты/Сливки → закваска)" : "✖ нет «Сметана» — нужна на подачу (рецепт сметаны: сливки + закваска)");
            log.AppendLine(DS.Find(B.Dill) != null ? "✔ «Укроп» есть" : "✖ нет «Укроп» — Пельмени → 1 (или петрушка)");
            log.AppendLine(DS.Find(B.Lard) != null ? "✔ «Сало» есть" : "✖ нет «Сало» — Борщ → 2");
            if (DS.Find("Тесто") == null && DS.Find("Тесто дрожжевое") == null) log.AppendLine("⚠ не вижу дрожжевого теста («Тесто») — пампушки делаются из теста бетономешалки (Хлеб → 1/2)");

            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null) { db.CollectFromProject(); EditorUtility.SetDirty(db); EditorSceneManager.MarkSceneDirty(db.gameObject.scene); log.AppendLine("✔ FoodDatabase пересобран — СОХРАНИ СЦЕНУ"); }
            CookRecipeBook.Reload();
            log.AppendLine("\nМАСЛО ЧЕСНОЧНОЕ С ЗЕЛЕНЬЮ: масло 1 нажатие + 2 зубчика через измельчитель + укроп 2 дозы — миксером 4 с.");
            log.AppendLine("ЗАТИРКА: пласт сала через измельчитель + 4–5 зубчиков + соль 1 мастерок + укроп 2 дозы — отбойником или миксером 6 с.");
            log.AppendLine("ПАМПУШКИ: меню 14, 15 → шпатель ПКМ по подошедшему тесту — шарики кучкой в духовку → испеклись и слиплись →");
            log.AppendLine("  кисть: ЛКМ по таре с чесночным маслом (макнуть) → ЛКМ по пампушкам (водить) → шпатель ЛКМ по пампушке — отделить одну из кучи.");
            DS.Done("Борщ — шаг 6", log);
        }

        private static ProductDef FindLike(string part)
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && !string.IsNullOrEmpty(d.Title) && d.Title.ToLowerInvariant().Contains(part)) return d;
            }
            return null;
        }

        /// <summary>Головка чеснока: дольки-доли по кругу, хвостик сверху; цвет — белый с фиолетовыми прожилками (в вершинах).</summary>
        private static Mesh GarlicMesh(float D)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            float R = D * 0.5f;
            const int La = 14, Lo = 32;
            for (int i = 0; i <= La; i++)
            {
                float th = Mathf.PI * i / La;
                for (int j = 0; j <= Lo; j++)
                {
                    float ph = Mathf.PI * 2f * j / Lo;
                    float lobes = 1f + 0.10f * Mathf.Abs(Mathf.Sin(ph * 4f)) - 0.05f;
                    float top = 1f + 0.6f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(th)), 6f);          // вытянут кверху к хвостику
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    Vector3 p = new Vector3(d.x * R * lobes, (d.y * top + 0.85f) * R * 0.8f, d.z * R * lobes);
                    v.Add(p); n.Add(d);
                    float vein = Mathf.Abs(Mathf.Sin(ph * 4f)) < 0.12f ? 1f : 0f;
                    c.Add(Color.Lerp(new Color(0.97f, 0.95f, 0.90f), new Color(0.70f, 0.48f, 0.62f), vein * 0.6f * Mathf.Sin(th)));
                }
            }
            for (int i = 0; i < La; i++)
                for (int j = 0; j < Lo; j++)
                {
                    int a = i * (Lo + 1) + j, b = a + Lo + 1;
                    Tri(v, t, a, a + 1, b, n[a]); Tri(v, t, a + 1, b + 1, b, n[a + 1]);
                }
            // хвостик
            int s0 = v.Count; float yTop = (1.6f + 0.85f) * R * 0.8f;
            for (int j = 0; j <= 8; j++)
            {
                float a = j / 8f * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(new Vector3(0f, yTop - R * 0.1f, 0f) + d * R * 0.08f); n.Add(d); c.Add(new Color(0.80f, 0.72f, 0.55f));
                v.Add(new Vector3(0f, yTop + R * 0.25f, 0f) + d * R * 0.05f); n.Add(d); c.Add(new Color(0.80f, 0.72f, 0.55f));
            }
            for (int j = 0; j < 8; j++) { int a = s0 + j * 2; Tri(v, t, a, a + 1, a + 2, n[a]); Tri(v, t, a + 2, a + 1, a + 3, n[a + 2]); }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ───────────────────────── 14. ПАМПУШКА — ЗАГОТОВКА ─────────────────────────

        [MenuItem(Root + "14. Пампушка — заготовка куска в сцену (из префаба теста бетономешалки)", priority = 243)]
        public static void MakeBunPiece()
        {
            var log = new StringBuilder();
            ProductDef bun = DS.Find(B.Bun);
            if (bun == null) { EditorUtility.DisplayDialog("Борщ", "Сначала меню 13 (продукт «Пампушка»).", "Ок"); return; }
            GameObject src = null;
            foreach (Object o in Selection.objects) { GameObject g = o as GameObject; if (g != null && EditorUtility.IsPersistent(g) && g.GetComponent<DoughBlob>() != null) { src = g; break; } }
            if (src == null)
                foreach (MixerDough md in Object.FindObjectsByType<MixerDough>(FindObjectsSortMode.None))
                    if (md != null && md.BallPrefab != null && md.BallPrefab.GetComponent<DoughBlob>() != null) { src = md.BallPrefab; break; }
            if (src == null) { EditorUtility.DisplayDialog("Борщ", "Не нашёл префаб теста (Food Piece + Dough Blob): выдели его в Project или поставь в сцену бетономешалку с Ball Prefab.", "Ок"); return; }
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            Undo.RegisterCreatedObjectUndo(go, "Пампушка");
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Pampushka_Piece";
            go.transform.position = at;
            DoughBlob db = go.GetComponent<DoughBlob>();
            var so = new SerializedObject(db);
            DS.SetObj(so, "_bakedProduct", bun);
            SerializedProperty cc = so.FindProperty("_crustColor"); if (cc != null) cc.colorValue = new Color(0.80f, 0.46f, 0.17f);
            DS.SetF(so, "_bakeRise", 0.4f);        // в духовке заметно подрастают (и упираются в соседок)
            DS.SetF(so, "_bakeSeconds", 30f);
            DS.SetF(so, "_pieceScale", 1f);
            so.ApplyModifiedProperties();
            DS.Need<PampushkaGlaze>(go);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"✔ «Pampushka_Piece» — из «{src.name}»: печётся в «{bun.Title}», подрастает на 40%, корочка золотая; Pampushka Glaze — в чесночном масле станет «Пампушка с чесноком»");
            log.AppendLine("\nДАЛЬШЕ: перетащи Pampushka_Piece в Project (префаб) → Spawnable Prefabs → выдели префаб → меню 15. Объект в сцене потом удали.");
            DS.Done("Борщ — пампушка", log);
        }

        [MenuItem(Root + "15. Шпатели: ПКМ — пампушка (выдели префаб Pampushka_Piece в Project)", priority = 244)]
        public static void AssignBun()
        {
            var log = new StringBuilder();
            GameObject bun = null;
            foreach (Object o in Selection.objects) { GameObject g = o as GameObject; if (g != null && EditorUtility.IsPersistent(g) && g.GetComponent<DoughBlob>() != null) { bun = g; break; } }
            if (bun == null) { EditorUtility.DisplayDialog("Борщ", "Выдели в Project префаб пампушки (сделан меню 14).", "Ок"); return; }
            int n = 0;
            foreach (Tools.DoughSpatulaItem sp in Object.FindObjectsByType<Tools.DoughSpatulaItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(sp);
                DS.SetObj(so, "_bunPrefab", bun);
                so.ApplyModifiedProperties();
                n++;
                log.AppendLine($"✔ шпатель «{sp.name}»: ПКМ (или Shift+ЛКМ) — пампушка «{bun.name}»");
            }
            if (n == 0) log.AppendLine("✖ в сцене нет шпателей (Dough Spatula Item)");
            else EditorSceneManager.MarkSceneDirty(SceneManagerActive());
            log.AppendLine("\nШпатель — префаб? Overrides → Apply, чтобы и другие копии знали.");
            DS.Done("Борщ — шпатели", log);
        }

        private static UnityEngine.SceneManagement.Scene SceneManagerActive() => UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        // ───────────────────────── 16. ГЛУБОКАЯ МИСКА ─────────────────────────

        [MenuItem(Root + "16. Глубокая миска для борща — в сцену", priority = 245)]
        public static void MakeBowl()
        {
            var log = new StringBuilder();
            if (DS.Find(B.Soup) == null) { EditorUtility.DisplayDialog("Борщ", "Сначала меню 12 (продукт «Борщ»).", "Ок"); return; }
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            float k = Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
            float Rt = 0.40f * k, Rb = 0.17f * k, H = 0.30f * k, t = 0.025f * k, fh = 0.03f * k;
            float innerY = fh + t + 0.01f * k, rInTop = Rt - t, rInBot = Rb + 0.02f * k - t * 0.5f;
            Mesh m = SaveMesh(BowlMesh(Rt, Rb, H, t, fh, k), "BorschBowl_Mesh", log);
            GameObject go = SceneRoot("BorschBowl_01", at);
            Undo.RegisterFullObjectHierarchyUndo(go, "Миска");
            Get<MeshFilter>(go).sharedMesh = m;
            Get<MeshRenderer>(go).sharedMaterials = new[] { DS.PlateMat(log) };
            Rigidbody rb = Get<Rigidbody>(go); rb.mass = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            ClientAuthNT(go, log);
            Cookware cw = Get<Cookware>(go);
            cw.Def = EnsureBowlDef(log);
            EditorUtility.SetDirty(cw);
            if (go.GetComponent<PickupItem>() == null) Get<CarriedContainer>(go);
            CookwareAutoSetup au = Get<CookwareAutoSetup>(go);
            var aso = new SerializedObject(au);
            SerializedProperty f = aso.FindProperty("_form"); if (f != null) f.intValue = 1;
            DS.SetF(aso, "_wall", t); DS.SetF(aso, "_bottom", t);
            DS.SetF(aso, "_bottomRadiusFactor", Mathf.Clamp(rInBot / rInTop, 0.5f, 1.2f));
            SerializedProperty segs = aso.FindProperty("_roundSegments"); if (segs != null) segs.intValue = 16;
            DS.SetV3(aso, "_innerSize", new Vector3(rInTop * 2f, H - innerY, rInTop * 2f));
            DS.SetV3(aso, "_innerBottomLocal", new Vector3(0f, innerY, 0f));
            DS.SetF(aso, "_radiusTop", rInTop);
            SetB(aso, "_makeHeap", false); SetB(aso, "_makeLiquid", true); SetB(aso, "_makeColliders", true);
            aso.ApplyModifiedProperties();
            FieldInfo ff = typeof(CookwareAutoSetup).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
            if (ff != null) ff.SetValue(au, CookwareAutoSetup.Form.Round);
            MethodInfo build = typeof(CookwareAutoSetup).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic);
            try { if (build != null) build.Invoke(au, null); log.AppendLine("✔ полость: коллайдеры (дно + 16 стенок), приём, горловина, жидкость"); }
            catch (System.Exception e) { log.AppendLine($"✖ сборка полости: {e.GetBaseException().Message}"); }
            foreach (CookwareLiquidVisual lv in go.GetComponentsInChildren<CookwareLiquidVisual>(true))
            {
                var lso = new SerializedObject(lv);
                SetB(lso, "_soup", false);
                lso.ApplyModifiedProperties();
            }
            LiquidProduct(DS.Find(B.Soup), log);
            LiquidProduct(DS.Find(B.Broth), log);
            Get<BorschBowl>(go);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"✔ «BorschBowl_01» — глубокая миска Ø{Rt * 200f:0} см, глубина {H * 100f:0} см: белая эмаль с синим ободком, Borsch Bowl (подача, звёзды)");
            log.AppendLine("\nПОДАЧА: борщ ведром (ЛКМ над миской, до половины и больше) → сметана мастерком → затирка мастерком → пампушку бросить рядом — ляжет на край. Всё есть — звёзды.");
            log.AppendLine("Сохрани миску префабом (и в Spawnable Prefabs), если нужно несколько.");
            DS.Done("Борщ — миска", log);
        }

        private static CookwareDef EnsureBowlDef(StringBuilder log)
        {
            string path = $"{VesselDir}/Cookware_DeepBowl.asset";
            CookwareDef d = AssetDatabase.LoadAssetAtPath<CookwareDef>(path);
            if (d == null)
            {
                DS.EnsureDir(VesselDir);
                d = ScriptableObject.CreateInstance<CookwareDef>();
                AssetDatabase.CreateAsset(d, path);
                log.AppendLine($"＋ тара {path}");
            }
            d.Title = "Глубокая миска"; d.Kind = CookwareKind.Storage; d.Capacity = 12; d.HeatProcess = FoodProcess.None;
            d.NeedsHeatSource = false; d.Absorbs = true; d.BlendsColor = false; d.PortionPerMark = 0;
            EditorUtility.SetDirty(d);
            return d;
        }

        /// <summary>Глубокая суповая миска: вращение профиля (ножка, выпуклый бок, ободок, внутренность). Белая эмаль, синий ободок и ножка.</summary>
        private static Mesh BowlMesh(float Rt, float Rb, float H, float t, float fh, float k)
        {
            var prof = new List<Vector2>(); var col = new List<Color>();
            Color white = new Color(0.96f, 0.96f, 0.94f), blue = new Color(0.16f, 0.32f, 0.72f), inner = new Color(0.98f, 0.97f, 0.95f);
            prof.Add(new Vector2(0f, 0f)); col.Add(blue);
            prof.Add(new Vector2(Rb, 0f)); col.Add(blue);
            prof.Add(new Vector2(Rb, fh)); col.Add(blue);
            float r0 = Rb + 0.02f * k, y0 = fh + 0.01f * k;
            const int W = 8;
            for (int i = 0; i <= W; i++)
            {
                float s = i / (float)W;
                float r = r0 + (Rt - r0) * (1f - (1f - s) * (1f - s));
                float y = y0 + (H - y0) * s;
                prof.Add(new Vector2(r, y)); col.Add(y > H * 0.86f ? blue : white);
            }
            prof.Add(new Vector2(Rt - t * 0.5f, H + 0.012f * k)); col.Add(blue);
            float iy = fh + t + 0.01f * k;
            for (int i = W; i >= 0; i--)
            {
                float s = i / (float)W;
                float r = r0 + (Rt - r0) * (1f - (1f - s) * (1f - s)) - t;
                float y = iy + (H - iy) * s;
                prof.Add(new Vector2(Mathf.Max(0.001f, r), y)); col.Add(i == W ? blue : inner);
            }
            prof.Add(new Vector2(0f, iy)); col.Add(inner);

            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var tr = new List<int>();
            const int Seg = 48;
            for (int i = 0; i < prof.Count - 1; i++)
            {
                Vector2 a = prof[i], b = prof[i + 1];
                Vector2 d = b - a;
                if (d.sqrMagnitude < 1e-10f) continue;
                Vector2 n2 = new Vector2(d.y, -d.x).normalized;           // наружу от материала (см. обход профиля)
                int start = v.Count;
                for (int j = 0; j <= Seg; j++)
                {
                    float ang = j / (float)Seg * Mathf.PI * 2f;
                    Vector3 rd = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 nn = rd * n2.x + Vector3.up * n2.y;
                    v.Add(rd * a.x + Vector3.up * a.y); n.Add(nn); c.Add(col[i]);
                    v.Add(rd * b.x + Vector3.up * b.y); n.Add(nn); c.Add(col[i + 1]);
                }
                for (int j = 0; j < Seg; j++)
                {
                    int p0 = start + j * 2;
                    Tri(v, tr, p0, p0 + 1, p0 + 2, n[p0]);
                    Tri(v, tr, p0 + 2, p0 + 1, p0 + 3, n[p0 + 2]);
                }
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(tr, 0);
            m.RecalculateBounds();
            return m;
        }

        // ───────────────────────── 6. ПРОВЕРКА ─────────────────────────

        [MenuItem(Root + "6. Проверка", priority = 235)]
        public static void Check()
        {
            var log = new StringBuilder();
            log.AppendLine("ПРОДУКТЫ:");
            foreach (string t in new[] { B.Beet, B.Cabbage, B.Bay, B.Oil, B.Lard, B.Broth, B.Bones, B.Shank, B.Onion, B.Carrot, B.Water })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}»" + (t == B.Shank ? " — Мясо → Новые туши → 1" : t == B.Bones ? " — кости из набора (Мясо)" : " — Борщ → 2"));
            ProductDef broth = DS.Find(B.Broth);
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db == null) log.AppendLine("✖ в сцене нет FoodDatabase");
            else if (broth != null && !InDb(db, broth)) log.AppendLine("✖ FoodDatabase не знает новые продукты — нажми у него «Collect From Project» (или меню 2) и сохрани сцену");
            else log.AppendLine("✔ FoodDatabase знает новые продукты");

            log.AppendLine("\nГОРЕЛКА (правила «Опалять горелкой»):");
            var torchList = new List<ProductDef>();
            foreach (string t in new[] { B.Bones, B.Shank }) { ProductDef bp = DS.Find(t); if (bp != null) torchList.Add(bp); }
            torchList.AddRange(TorchVeg());
            foreach (ProductDef p in torchList)
            {
                string t = p.Title;
                bool ok = false;
                foreach (FoodRule r in p.Overrides) if (r.Process == FoodProcess.Torch && r.To == FoodState.Torched) { ok = true; break; }
                log.AppendLine(ok ? $"✔ «{t}» опаляется" : $"✖ «{t}» — нет правила горелки (меню 2)");
            }
            ProductDef beet = DS.Find(B.Beet);
            if (beet != null)
            {
                bool bake = false, grate = false;
                foreach (FoodRule r in beet.Overrides) { if (r.Process == FoodProcess.Bake && r.To == FoodState.Baked) bake = true; if (r.Process == FoodProcess.Grate && r.From == FoodState.Baked) grate = true; }
                log.AppendLine(bake && grate ? "✔ свёкла: запекается и трётся" : "✖ свёкла: нет правил запекания/тёрки (меню 2)");
            }

            log.AppendLine("\nСАЛО:");
            MeatCutSettings st = FindSettings();
            ProductDef lard = DS.Find(B.Lard);
            if (st == null) log.AppendLine("⚠ нет MeatCutSettings (новые туши не настроены)");
            else log.AppendLine(st.PorkKinds != null && st.PorkKinds.Length > 1 && st.PorkKinds[1] != null && st.PorkKinds[1] == lard
                ? "✔ свиная грудинка даёт «Сало»" : "✖ свиная грудинка не «Сало» — меню 2");
            if (st != null) log.AppendLine((st.LardPrefab != null ? $"✔ грудинка падает пластами сала «{st.LardPrefab.name}» ×{st.LardPerBrisket}" : "· сало — обычным куском (меню 8 → префаб → 9)") + "\n" +
                                           (st.ShankPrefab != null ? $"✔ говяжьи ноги падают шайбами «{st.ShankPrefab.name}» ×{st.ShankPerLeg}" : "· голяшка — обычным куском, пилить пилой (меню 8 → префаб → 9)"));
            if (st != null && st.CutPrefab == null) log.AppendLine("✖ у MeatCutSettings нет Cut Prefab — туша режется только на старые куски! Мясо → Новые туши → 4, потом 6");

            log.AppendLine("\nСКОВОРОДА:");
            int pans = 0;
            foreach (Cookware c in Object.FindObjectsByType<Cookware>(FindObjectsSortMode.None))
            {
                if (!Tools.BonfireTestKeys.IsPan(c)) continue;
                pans++;
                string s = $"«{c.name}»: Def {(c.Def != null ? c.Def.name : "—")}";
                if (c.Def != null && !c.Def.Absorbs) s += " ✖ не глотает (меню 1)";
                if (c.Def != null && c.Def.HeatProcess != FoodProcess.Fry) s += " ✖ не жарит";
                if (c.GetComponent<CookwareHeat>() == null) s += " ✖ нет Cookware Heat";
                if (c.GetComponent<CarriedContainer>() == null) s += " ⚠ нет Carried Container (не берётся как тара)";
                if (c.transform.Find("AutoColliders") == null && c.transform.Find("Receive") == null) s += " ⚠ полость не собрана (Auto Setup ⋮ → «Подогнать и собрать»)";
                log.AppendLine((s.Contains("✖") ? "" : "✔ ") + s);
            }
            if (pans == 0) log.AppendLine("✖ сковороды в сцене нет — выдели её и меню 1");
            log.AppendLine(Object.FindFirstObjectByType<Tools.BonfireTestKeys>() != null ? "✔ Bonfire Test Keys в сцене (F9 сковорода)" : "⚠ нет Bonfire Test Keys — повесь на костёр, чтобы F9/F10/O работали");

            log.AppendLine("\nВИД (подкраска по состоянию):");
            log.AppendLine("✔ кости с туши — настраивать не надо: вид добавляется сам при первом пламени горелки");
            log.AppendLine("✔ мясо туши (MeatCut) темнеет и усыхает само (жареное −8%, варёное −5%, опалённое −3%)");
            log.AppendLine("\nШАГ 4 (зажарка):");
            foreach (string t in new[] { B.Paste, B.PasteBurnt, B.Saute, B.BeetStewed, B.Zazharka })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}» — меню 7");
            ProductDef tom = FindTomatoAsset();
            if (tom == null) log.AppendLine("✖ помидор не найден");
            else
            {
                bool gr = false;
                foreach (FoodRule r in tom.Overrides) if (r.Process == FoodProcess.Grate && r.To == FoodState.Grated) { gr = true; break; }
                log.AppendLine(gr ? $"✔ «{tom.Title}» трётся в измельчителе" : $"✖ «{tom.Title}» — нет правила тёрки (меню 7)");
            }
            foreach (Cookware c in Object.FindObjectsByType<Cookware>(FindObjectsSortMode.None))
                if (Tools.BonfireTestKeys.IsPan(c) && c.Def != null && c.Def.Capacity < 40) log.AppendLine($"⚠ «{c.name}»: вместимость {c.Def.Capacity} — зажарка целиком не влезет (меню 7 поднимет до 40)");
            log.AppendLine("\nВ Play в консоли: «[Рецепты] в книге: … Бульон для борща, Томатная паста, Пассеровка, Свёкла тушёная, Зажарка для борща», при опалении — «[Горелка] …».");
            DS.Done("Борщ — проверка", log);
        }
    }
}
#endif
