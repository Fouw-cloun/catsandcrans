using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food.EditorTools
{
    /// <summary>
    /// v18: ВЕЩЕСТВА — автонастройка.
    ///   1. Создать профили и таблицу — ассеты «Какао-порошок» и «Сливочное масло», их материалы
    ///      и таблицу Resources/FoodSubstances; правила по продуктам и словам; старые слова порошков —
    ///      выключенным правилом (включишь, когда захочешь такой же вид у сахара, муки…).
    ///      Уже созданные профили НЕ перезаписываются (твои ползунки целы), в таблицу только добавляется.
    ///   2. Проверка — какие продукты получат какой профиль, какие префабы рисуют кучки (пол, тара,
    ///      полотна лопат/мастерков), есть ли общий префаб кучки в Spawnable Prefabs, джигл грозди.
    ///      Префабы НЕ меняются. Отчёт — в консоль и в файл рядом с таблицей.
    /// </summary>
    public static class CatCooksSubstanceSetup
    {
        const string Dir = "Assets/CatCooks/Food/Substances";
        const string ResDir = Dir + "/Resources";
        const string TablePath = ResDir + "/" + FoodSubstanceTable.ResourceName + ".asset";
        const string PowderPath = Dir + "/Substance_CocoaPowder.asset";
        const string ButterPath = Dir + "/Substance_Butter.asset";
        const string PowderMatPath = Dir + "/Substance_CocoaPowder.mat";
        const string ButterMatPath = Dir + "/Substance_Butter.mat";
        const string ReportPath = Dir + "/Отчёт_вещества.txt";
        const string SugarPath = Dir + "/Substance_Sugar.asset";
        const string SugarMatPath = Dir + "/Substance_Sugar.mat";
        const string PastePath = Dir + "/Substance_ChocoPaste.asset";
        const string PasteMatPath = Dir + "/Substance_ChocoPaste.mat";

        const string CocoaWords = "тёртое какао, тертое какао, какао-порошок, какао порошок";
        const string ButterWords = "сливочное масло, масло сливочное";
        const string ButterExcept = "горел, шалфе, растоплен";
        const string OldPowderWords = "закваск, сахар, мук, соль, перец молот, паприк, ванил, специ, крахмал, дрожж, сода, миндальная смесь, сухой руб, чеснок суш";

        [MenuItem("CatCooks/Вещества/1. Создать профили и таблицу (масло, какао-порошок)", priority = 90)]
        public static void CreateAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[Вещества] создание:");
            EnsureFolder(Dir);
            EnsureFolder(ResDir);

            Material powderMat = EnsureMaterial(PowderMatPath, "CatCooks/Powder Heap", log);
            Material butterMat = EnsureMaterial(ButterMatPath, "CatCooks/Butter", log);
            FoodSubstance powder = EnsurePowder(powderMat, log);
            FoodSubstance butter = EnsureButter(butterMat, log);

            FoodSubstanceTable table = AssetDatabase.LoadAssetAtPath<FoodSubstanceTable>(TablePath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<FoodSubstanceTable>();
                AssetDatabase.CreateAsset(table, TablePath);
                log.AppendLine($"  ✔ таблица {TablePath}");
            }
            else log.AppendLine($"  • таблица уже есть — только добавляю недостающее");

            Undo.RecordObject(table, "Вещества");
            ProductDef cocoa = FindProduct("Тёртое какао");
            if (cocoa != null && !HasProductRule(table, cocoa))
            {
                table.Products.Add(new FoodSubstanceTable.ByProduct { Product = cocoa, State = FoodState.None, Substance = powder });
                log.AppendLine("  ✔ правило: продукт «Тёртое какао» (любое состояние) → Какао-порошок");
            }
            else if (cocoa == null) log.AppendLine("  • нет продукта «Тёртое какао» — сработает правило по словам (меню CatCooks → Шоколад → 1 создаёт продукт)");
            AddName(table, CocoaWords, "", false, powder, true, log);
            // масло — по словам: жидкие состояния (растоплено) пропускаются, там жидкость
            AddName(table, ButterWords, ButterExcept, true, butter, true, log);
            // перенос старых правил: слова порошков из FoodHeapLook — выключенным правилом
            AddName(table, OldPowderWords, "масло, паст", false, powder, false, log);

            // ── v19: сахар (кристаллы) и шоколадная паста (нутелла, пропорции, липнет) ──
            Material sugarMat = EnsureMaterial(SugarMatPath, "CatCooks/Sugar", log);
            Material pasteMat = AssetDatabase.LoadAssetAtPath<Material>(PasteMatPath);
            if (pasteMat == null)
            {
                pasteMat = EnsureMaterial(PasteMatPath, "CatCooks/Butter", log);
                if (pasteMat != null) { FoodSubstance.SpreadGloss(pasteMat); EditorUtility.SetDirty(pasteMat); }
            }
            // v19.1: масло и паста — на тун-шейдер с обводками (свойства те же)
            Shader toon = Shader.Find("CatCooks/Toon Food");
            if (toon != null)
                foreach (Material mm in new[] { butterMat, pasteMat })
                    if (mm != null && mm.shader != toon)
                    {
                        Undo.RecordObject(mm, "Тун-шейдер еды");
                        mm.shader = toon;
                        if (mm == pasteMat) FoodSubstance.SpreadGloss(mm);
                        EditorUtility.SetDirty(mm);
                        log.AppendLine($"  ✔ материал «{mm.name}» → CatCooks/Toon Food (обводки, тун-свет)");
                    }
            FoodSubstance sugar = EnsureSugar(sugarMat, log);
            FoodSubstance paste = EnsurePaste(pasteMat, log);
            ProductDef sugarP = FindProduct("Сахар");
            if (sugarP != null && !HasProductRule(table, sugarP))
            {
                table.Products.Add(new FoodSubstanceTable.ByProduct { Product = sugarP, State = FoodState.None, Substance = sugar });
                log.AppendLine("  ✔ правило: продукт «Сахар» → Сахар (кристаллы)");
            }
            else if (sugarP == null) AddName(table, "сахар", "пудр, карамел", true, sugar, true, log);
            ProductDef pasteP = FindProduct("Шоколадная паста");
            if (pasteP != null && !HasProductRule(table, pasteP))
            {
                table.Products.Add(new FoodSubstanceTable.ByProduct { Product = pasteP, State = FoodState.None, Substance = paste });
                log.AppendLine("  ✔ правило: продукт «Шоколадная паста» → Шоколадная паста (нутелла)");
            }
            else if (pasteP == null) AddName(table, "шоколадная паста", "", true, paste, true, log);
            // паста больше не застывает в шоколад (как нутелла — всегда паста)
            if (pasteP != null && pasteP.HardensInto != null)
            {
                Undo.RecordObject(pasteP, "Паста не застывает");
                pasteP.HardensInto = null;
                EditorUtility.SetDirty(pasteP);
                log.AppendLine("  ✔ «Шоколадная паста» больше не застывает в шоколад (Hardens Into очищено)");
            }
            // масло липнет к таре (перевернул ковш — не выпадает)
            if (butter != null && !butter.StickToCookware)
            {
                Undo.RecordObject(butter, "Масло липнет");
                butter.StickToCookware = true;
                EditorUtility.SetDirty(butter);
                log.AppendLine("  ✔ «Сливочное масло»: липнет к таре (Stick To Cookware)");
            }

            if (table.FloorHeapPrefab == null)
            {
                List<string> heaps = HeapPrefabs();
                if (heaps.Count > 0)
                {
                    table.FloorHeapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(heaps[0]).GetComponent<FoodHeap>();
                    log.AppendLine($"  ✔ общий префаб кучки на полу: {heaps[0]}" + (heaps.Count > 1 ? $" (всего найдено {heaps.Count}, взял первый)" : ""));
                }
                else log.AppendLine("  ✖ не найден ни один префаб с FoodHeap — назначь Floor Heap Prefab в таблице вручную");
            }
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            FoodSubstanceTable.Reload();
            CatCooksFireFxSetup.IncludeShaders();                          // шейдеры порошка, масла и пыли — в сборку
            Selection.activeObject = table;
            log.AppendLine("Готово. Дальше: CatCooks → Вещества → 2. Проверка");
            Debug.Log(log.ToString());
        }

        [MenuItem("CatCooks/Вещества/2. Проверка (отчёт, префабы не меняются)", priority = 91)]
        public static void Check()
        {
            var r = new StringBuilder();
            int err = 0, warn = 0;
            r.AppendLine("ОТЧЁТ: вещества (v18.10) — " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            r.AppendLine();

            // ── шейдеры ──
            r.AppendLine("■ Шейдеры");
            foreach (string sh in new[] { "CatCooks/Powder Heap", "CatCooks/Butter", "CatCooks/Toon Dust", "CatCooks/Sugar", "CatCooks/Toon Food" })
            {
                bool ok = Shader.Find(sh) != null;
                r.AppendLine((ok ? "  ✔ " : "  ✖ ") + sh + (ok ? "" : " — не найден (не скомпилирован?)"));
                if (!ok) err++;
            }

            // ── таблица ──
            r.AppendLine();
            r.AppendLine("■ Таблица");
            FoodSubstanceTable.Reload();
            FoodSubstanceTable table = Resources.Load<FoodSubstanceTable>(FoodSubstanceTable.ResourceName);
            if (table == null)
            {
                r.AppendLine("  ✖ нет Resources/FoodSubstances — сначала меню «1. Создать профили и таблицу»");
                err++;
            }
            else
            {
                r.AppendLine($"  ✔ {AssetDatabase.GetAssetPath(table)}: по продукту {table.Products.Count}, по словам {table.Names.Count}, по состоянию {table.States.Count}");
                foreach (FoodSubstanceTable.ByName n in table.Names)
                    r.AppendLine($"     {(n.On ? "вкл " : "выкл")}  «{n.Words}» → {(n.Substance != null ? n.Substance.Title : "—")}");
                if (table.FloorHeapPrefab == null) { r.AppendLine("  ⚠ Floor Heap Prefab не задан — тара будет искать кучку в Spawnable Prefabs"); warn++; }
                else
                {
                    bool spawnable = InSpawnables(table.FloorHeapPrefab.gameObject);
                    r.AppendLine($"  {(spawnable ? "✔" : "⚠")} общий префаб кучки: «{table.FloorHeapPrefab.name}»" + (spawnable ? " (в Spawnable Prefabs)" : " — НЕ найден в списках Spawnable Prefabs FishNet: добавь, иначе кучка не появится по сети"));
                    if (!spawnable) warn++;
                }
            }

            // ── продукты ──
            r.AppendLine();
            r.AppendLine("■ Какие продукты получат профиль (проверены состояния: — нет —, Сырое, Тёртое, Зачерпнуто, В таре, Налито, Взбито, Растоплено)");
            FoodState[] states = { FoodState.None, FoodState.Raw, FoodState.Grated, FoodState.Scooped, FoodState.InContainer, FoodState.Poured, FoodState.Whipped, FoodState.Melted };
            int mapped = 0;
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (p == null) continue;
                var parts = new List<string>();
                foreach (FoodState s in states)
                {
                    FoodSubstance sub = FoodSubstance.Of(p, s);
                    if (sub != null) parts.Add($"{FoodStateInfo.NameOf(s)} → {sub.Title}");
                }
                if (parts.Count == 0) continue;
                mapped++;
                r.AppendLine($"  • «{p.Title}»: " + string.Join("; ", parts.ToArray()));
            }
            if (mapped == 0) { r.AppendLine("  ⚠ ни один продукт не получил профиль — проверь слова в таблице"); warn++; }

            // v18.9: все «масляные» продукты — кто получил профиль, а кто нет (частая причина «ничего не поменялось»)
            r.AppendLine();
            r.AppendLine("■ Все продукты со словом «масл» (состояние «— нет —»)");
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (p == null || string.IsNullOrEmpty(p.Title) || p.Title.ToLowerInvariant().IndexOf("масл") < 0) continue;
                FoodSubstance sub = FoodSubstance.Of(p, FoodState.None);
                r.AppendLine($"  {(sub != null ? "✔" : "•")} «{p.Title}» (id {p.Id}) → {(sub != null ? sub.Title : "профиля нет")}");
            }

            // ── префабы ──
            r.AppendLine();
            r.AppendLine("■ Префабы, где рисуются кучки (не меняются)");
            int blades = 0, floors = 0, taras = 0;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.StartsWith("Assets/")) continue;
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                if (go.GetComponent<FoodHeap>() != null)
                {
                    floors++;
                    bool sp = InSpawnables(go);
                    r.AppendLine($"  {(sp ? "✔" : "⚠")} кучка на полу: {path}" + (sp ? "" : " — нет в Spawnable Prefabs"));
                    if (go.GetComponentInChildren<FoodHeapRenderer>(true) == null) { r.AppendLine("     ✖ нет FoodHeapRenderer внутри — нечем рисовать"); err++; }
                }

                MonoBehaviour scoop = FindByTypeName(go, "ScoopItem");
                if (scoop != null)
                {
                    blades++;
                    r.AppendLine($"  ✔ лопата/мастерок (префаб): {path}");
                    r.Append(DescribeScoop(scoop));
                }

                Cookware cw = go.GetComponent<Cookware>();
                if (cw != null)
                {
                    taras++;
                    CookwareLiquidVisual lv = go.GetComponentInChildren<CookwareLiquidVisual>(true);
                    CookwareHeapVisual hv = go.GetComponentInChildren<CookwareHeapVisual>(true);
                    string where = (lv != null ? "LiquidAnchor" : "") + (lv != null && hv != null ? " + " : "") + (hv != null ? "HeapAnchor" : "");
                    if (where.Length == 0) where = "нет ни LiquidAnchor, ни HeapAnchor";
                    string roles = lv != null ? RolesFor(lv) : "";
                    r.AppendLine($"  • тара: {path} — {where}{roles}");
                    if (lv == null && hv == null) warn++;
                }

                FoodSoftBend bend = go.GetComponentInChildren<FoodSoftBend>(true);
                if (bend != null)
                {
                    bool roast = go.GetComponentInChildren<FoodRoastable>(true) != null || go.GetComponentInParent<FoodRoastable>() != null;
                    r.AppendLine($"  {(roast ? "✔" : "•")} мягкий изгиб (джигл): {path} — " + (roast ? "гнётся только сырым (есть FoodRoastable)" : "нет FoodRoastable — гнётся всегда"));
                }
            }
            r.AppendLine($"  Итого в префабах: кучек на полу {floors}, лопат/мастерков {blades}, тары {taras}");

            // ── v18.3: открытые сцены (лопаты, мастерки, тара часто стоят прямо в сцене, не префабами) ──
            r.AppendLine();
            r.AppendLine("■ Открытые сцены (не меняются)");
            int sBlades = 0, sTaras = 0;
            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null || EditorUtility.IsPersistent(mb)) continue;
                string sp = mb.gameObject.scene.name + "/" + PathOf(mb.transform);
                if (mb.GetType().Name == "ScoopItem")
                {
                    sBlades++;
                    r.AppendLine($"  ✔ лопата/мастерок: {sp}");
                    r.Append(DescribeScoop(mb));
                }
                else if (mb is Cookware)
                {
                    sTaras++;
                    CookwareLiquidVisual lv = mb.GetComponentInChildren<CookwareLiquidVisual>(true);
                    CookwareHeapVisual hv = mb.GetComponentInChildren<CookwareHeapVisual>(true);
                    string where = (lv != null ? "LiquidAnchor" : "") + (lv != null && hv != null ? " + " : "") + (hv != null ? "HeapAnchor" : "");
                    if (where.Length == 0) { where = "нет ни LiquidAnchor, ни HeapAnchor"; warn++; }
                    r.AppendLine($"  • тара: {sp} — {where}{(lv != null ? RolesFor(lv) : "")}");
                    r.Append(CollidersOf((Cookware)mb));
                }
            }
            r.AppendLine($"  Итого в сценах: лопат/мастерков {sBlades}, тары {sTaras}");

            r.AppendLine();
            r.AppendLine($"Ошибок: {err}, предупреждений: {warn}");
            EnsureFolder(Dir);
            File.WriteAllText(ReportPath, r.ToString(), new UTF8Encoding(true));
            AssetDatabase.ImportAsset(ReportPath);
            if (err > 0) Debug.LogError(r.ToString());
            else if (warn > 0) Debug.LogWarning(r.ToString());
            else Debug.Log(r.ToString());
            Debug.Log($"[Вещества] отчёт сохранён: {ReportPath}");
        }

        // ───────────────────────── ПРОФИЛИ ─────────────────────────

        static FoodSubstance EnsurePowder(Material mat, StringBuilder log)
        {
            FoodSubstance s = AssetDatabase.LoadAssetAtPath<FoodSubstance>(PowderPath);
            if (s != null) { log.AppendLine("  • профиль «Какао-порошок» уже есть — не трогаю"); if (s.Material == null && mat != null) { s.Material = mat; EditorUtility.SetDirty(s); } return s; }
            s = ScriptableObject.CreateInstance<FoodSubstance>();
            s.Title = "Какао-порошок";
            s.Look = SubstanceLook.Powder;
            s.FallbackPreset = FoodHeapRenderer.HeapPreset.Powder;
            s.UseProductColor = true;
            s.Material = mat;
            s.InCookware = true;
            s.CookwareRelief = 1f;
            s.FloorSlope = 0.62f;
            s.Wonky = 0.55f;
            s.BladeHeight = 1.25f;
            s.Curl = 0f;
            s.Jiggle = 0.05f;
            s.DustOnScoop = true;
            s.DustAmount = 1f;
            s.EdgeDrips = true;
            s.DripShake = 22f;
            s.DripSize = 0.07f;
            s.Steam = false;
            s.Flow = SubstanceFlow.Spills;
            AssetDatabase.CreateAsset(s, PowderPath);
            log.AppendLine($"  ✔ профиль {PowderPath}");
            return s;
        }

        static FoodSubstance EnsureButter(Material mat, StringBuilder log)
        {
            FoodSubstance s = AssetDatabase.LoadAssetAtPath<FoodSubstance>(ButterPath);
            if (s != null) { log.AppendLine("  • профиль «Сливочное масло» уже есть — не трогаю"); if (s.Material == null && mat != null) { s.Material = mat; EditorUtility.SetDirty(s); } return s; }
            s = ScriptableObject.CreateInstance<FoodSubstance>();
            s.Title = "Сливочное масло";
            s.Look = SubstanceLook.Butter;
            s.FallbackPreset = FoodHeapRenderer.HeapPreset.Dollop;
            s.UseProductColor = true;
            s.Material = mat;
            s.InCookware = true;
            s.CookwareRelief = 1f;
            s.Smears = 5;
            s.SmearDepth = 1f;
            s.FloorSlope = 0.5f;
            s.Wonky = 0.2f;
            s.BladeHeight = 1.1f;
            s.Curl = 0.85f;
            s.Jiggle = 0.55f;
            s.DustOnScoop = false;
            s.EdgeDrips = true;
            s.DripShake = 32f;
            s.DripSize = 0.06f;
            s.Steam = false;
            s.Flow = SubstanceFlow.HoldsLump;
            AssetDatabase.CreateAsset(s, ButterPath);
            log.AppendLine($"  ✔ профиль {ButterPath}");
            return s;
        }

        static FoodSubstance EnsureSugar(Material mat, StringBuilder log)
        {
            FoodSubstance s = AssetDatabase.LoadAssetAtPath<FoodSubstance>(SugarPath);
            if (s != null) { log.AppendLine("  • профиль «Сахар» уже есть — не трогаю"); return s; }
            s = ScriptableObject.CreateInstance<FoodSubstance>();
            s.Title = "Сахар";
            s.Look = SubstanceLook.Crystals;
            s.FallbackPreset = FoodHeapRenderer.HeapPreset.Powder;
            s.UseProductColor = true;
            s.Brightness = 1.05f;
            s.Material = mat;
            s.InCookware = true;
            s.FloorSlope = 0.5f;
            s.Wonky = 0.3f;
            s.BladeHeight = 1.1f;
            s.Jiggle = 0f;
            s.CrystalSize = 0.05f;
            s.CrystalDensity = 1f;
            s.DustOnScoop = false;
            s.EdgeDrips = true;
            s.DripShake = 20f;
            s.DripSize = 0.05f;
            s.Flow = SubstanceFlow.Spills;
            s.StickToCookware = false;
            AssetDatabase.CreateAsset(s, SugarPath);
            log.AppendLine($"  ✔ профиль {SugarPath}");
            return s;
        }

        static FoodSubstance EnsurePaste(Material mat, StringBuilder log)
        {
            FoodSubstance s = AssetDatabase.LoadAssetAtPath<FoodSubstance>(PastePath);
            if (s != null) { log.AppendLine("  • профиль «Шоколадная паста» уже есть — не трогаю"); return s; }
            s = ScriptableObject.CreateInstance<FoodSubstance>();
            s.Title = "Шоколадная паста";
            s.Look = SubstanceLook.Spread;
            s.FallbackPreset = FoodHeapRenderer.HeapPreset.Dollop;
            s.UseProductColor = true;
            s.Material = mat;
            s.InCookware = true;
            s.CookwareRelief = 1f;
            s.FloorSlope = 0.55f;
            s.BladeHeight = 1.1f;
            s.Curl = 0.8f;
            s.Jiggle = 0.35f;
            s.EdgeDrips = true;
            s.DripShake = 35f;
            s.DripSize = 0.05f;
            s.Flow = SubstanceFlow.HoldsLump;
            s.StickToCookware = true;
            s.SmearNotPiece = true;
            s.Blend = true;
            s.BlendMaxExcess = 0.6f;
            AssetDatabase.CreateAsset(s, PastePath);
            log.AppendLine($"  ✔ профиль {PastePath} (пропорции: перебор → испорчено, липнет к таре)");
            return s;
        }

        static Material EnsureMaterial(string path, string shader, StringBuilder log)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find(shader);
            if (sh == null) { log.AppendLine($"  ✖ нет шейдера «{shader}» — материал не создан (профиль создаст его в игре сам)"); return null; }
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ✔ материал {path}");
            return m;
        }

        // ───────────────────────── ТАБЛИЦА ─────────────────────────

        static bool HasProductRule(FoodSubstanceTable t, ProductDef p)
        {
            foreach (FoodSubstanceTable.ByProduct r in t.Products) if (r.Product == p) return true;
            return false;
        }

        static void AddName(FoodSubstanceTable t, string words, string except, bool skipLiquid, FoodSubstance sub, bool on, StringBuilder log)
        {
            foreach (FoodSubstanceTable.ByName r in t.Names) if (r.Words == words) return;
            t.Names.Add(new FoodSubstanceTable.ByName { On = on, Words = words, Except = except, State = FoodState.None, SkipLiquid = skipLiquid, Substance = sub });
            log.AppendLine($"  ✔ правило по словам ({(on ? "вкл" : "выкл")}): «{words}» → {(sub != null ? sub.Title : "—")}");
        }

        static ProductDef FindProduct(string title)
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null && p.Title == title) return p;
            }
            return null;
        }

        // ───────────────────────── ПРЕФАБЫ ─────────────────────────

        static List<string> HeapPrefabs()
        {
            var list = new List<string>();
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.StartsWith("Assets/")) continue;
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.GetComponent<FoodHeap>() != null) list.Add(path);
            }
            // сначала те, что в Spawnable Prefabs
            list.Sort((a, b) => (InSpawnables(AssetDatabase.LoadAssetAtPath<GameObject>(b)) ? 1 : 0) - (InSpawnables(AssetDatabase.LoadAssetAtPath<GameObject>(a)) ? 1 : 0));
            return list;
        }

        static List<GameObject> _spawnCache;

        static bool InSpawnables(GameObject go)
        {
            if (go == null) return false;
            if (_spawnCache == null)
            {
                _spawnCache = new List<GameObject>();
                foreach (string g in AssetDatabase.FindAssets("t:SinglePrefabObjects"))
                {
                    var spo = AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.SinglePrefabObjects>(AssetDatabase.GUIDToAssetPath(g));
                    if (spo == null) continue;
                    foreach (FishNet.Object.NetworkObject nob in spo.Prefabs) if (nob != null) _spawnCache.Add(nob.gameObject);
                }
                EditorApplication.delayCall += ClearSpawnCache;
            }
            return _spawnCache.Contains(go);
        }

        static void ClearSpawnCache() { _spawnCache = null; }

        static MonoBehaviour FindByTypeName(GameObject go, string type)
        {
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == type) return mb;
            return null;
        }

        /// <summary>Какая роль у масла и какао на LiquidAnchor (по сериализованному списку Products).</summary>
        static string RolesFor(CookwareLiquidVisual lv)
        {
            var so = new SerializedObject(lv);
            SerializedProperty arr = so.FindProperty("_products");
            if (arr == null || !arr.isArray) return "";
            var found = new List<string>();
            for (int i = 0; i < arr.arraySize; i++)
            {
                SerializedProperty e = arr.GetArrayElementAtIndex(i);
                ProductDef p = e.FindPropertyRelative("Product").objectReferenceValue as ProductDef;
                if (p == null) continue;
                FoodSubstance sub = FoodSubstance.Of(p, FoodState.None);
                if (sub == null) continue;
                SerializedProperty role = e.FindPropertyRelative("Role");
                string rn = role != null && role.enumValueIndex >= 0 && role.enumValueIndex < role.enumDisplayNames.Length ? role.enumDisplayNames[role.enumValueIndex] : "?";
                found.Add($"«{p.Title}» ({rn}) → {sub.Title}");
            }
            return found.Count > 0 ? "; в LiquidAnchor: " + string.Join(", ", found.ToArray())
                                   : "; масла/какао в списке LiquidAnchor нет — нарисует HeapAnchor (тем же профилем)";
        }

        /// <summary>v19: твёрдые стенки тары — есть ли, на каком слое, сталкиваются ли с предметами (Interactable, Food, Default).</summary>
        static string CollidersOf(Cookware cw)
        {
            var sb = new StringBuilder();
            int solid = 0, trig = 0, off = 0;
            var layers = new List<int>();
            foreach (Collider c in cw.GetComponentsInChildren<Collider>(true))
            {
                if (!c.enabled || !c.gameObject.activeInHierarchy) { off++; continue; }
                if (c.isTrigger) { trig++; continue; }
                solid++;
                if (!layers.Contains(c.gameObject.layer)) layers.Add(c.gameObject.layer);
            }
            Rigidbody rb = cw.GetComponentInParent<Rigidbody>();
            string[] others = { "Interactable", "Food", "Default" };
            var bad = new List<string>();
            foreach (int l in layers)
                foreach (string o in others)
                {
                    int ol = LayerMask.NameToLayer(o);
                    if (ol >= 0 && Physics.GetIgnoreLayerCollision(l, ol)) bad.Add($"«{LayerMask.LayerToName(l)}» не сталкивается с «{o}»");
                }
            if (solid == 0 || bad.Count > 0 || cw.name.ToLowerInvariant().Contains("bucket") && cw.transform.root != cw.transform)
            {
                sb.AppendLine($"     стенки: твёрдых {solid}, триггеров {trig}, выключено {off}; слои: " +
                              string.Join(", ", layers.ConvertAll(LayerMask.LayerToName).ToArray()) +
                              $"; Rigidbody родителя: {(rb != null ? (rb.isKinematic ? "кинематический" : "обычный") + " «" + rb.name + "»" : "нет")}");
                if (solid == 0) sb.AppendLine("     ✖ нет твёрдых коллайдеров — всё проходит насквозь (нужна «Полость → Собрать по форме» или свои коллайдеры)");
                foreach (string b in bad) sb.AppendLine("     ✖ " + b + " — предметы проходят сквозь (Project Settings → Physics → матрица слоёв)");
            }
            return sb.ToString();
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        /// <summary>Полотно лопаты: объект Blade, какая его ось смотрит вверх (по ней ляжет горка), размер полотна.</summary>
        static string DescribeScoop(MonoBehaviour scoop)
        {
            var sb = new StringBuilder();
            var so = new SerializedObject(scoop);
            SerializedProperty bp = so.FindProperty("_blade");
            SerializedProperty cp = so.FindProperty("_capacity");
            Transform blade = bp != null ? bp.objectReferenceValue as Transform : null;
            int cap = cp != null ? cp.intValue : 6;
            if (blade == null)
            {
                sb.AppendLine("     ⚠ Blade не задан — горка будет над центром предмета. Создай пустой дочерний объект на полотне и укажи его в Blade");
                return sb.ToString();
            }
            FoodHeapRenderer own = blade.GetComponent<FoodHeapRenderer>();
            Vector2 fp = cap <= 2 ? new Vector2(0.06f, 0.045f) : new Vector2(0.11f, 0.09f);
            Vector3[] axes = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            string[] names = { "+Y", "−Y", "+X", "−X", "+Z", "−Z" };
            int bi = 0; float best = -2f;
            for (int i = 0; i < axes.Length; i++)
            {
                float d = Vector3.Dot(blade.TransformDirection(axes[i]), Vector3.up);
                if (d > best) { best = d; bi = i; }
            }
            sb.AppendLine($"     Blade «{blade.name}»: сейчас вверх смотрит ось {names[bi]}" + (bi == 0 ? "" : " (не Y — ничего страшного: v18.2 сам берёт ту ось, что смотрела вверх при зачерпывании)") +
                          $"; вместимость {cap} → полотно {fp.x * 2f:0.##}×{fp.y * 2f:0.##} м" + (own != null ? " (свой FoodHeapRenderer на Blade — размер из его Footprint)" : " (горка создаётся сама)"));
            Vector3 ls = blade.lossyScale;
            if (Mathf.Abs(ls.x - ls.y) > 0.01f * Mathf.Abs(ls.x) || Mathf.Abs(ls.x - ls.z) > 0.01f * Mathf.Abs(ls.x))
                sb.AppendLine($"     ⚠ у Blade неравный масштаб {ls} — горка может сплющиться; лучше одинаковый по X/Y/Z");
            return sb.ToString();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
