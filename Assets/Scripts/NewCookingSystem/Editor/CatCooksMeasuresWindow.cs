#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CatCooks.Food.EditorTools
{
    /// <summary>
    /// НОВЫЕ МЕРЫ (v2): перевод тары, мастерков, лопат и рецептов.
    ///
    ///   1 доза = 1 порция = ½ мастерка;  мастерок = 2;  лопата = штука = 4;  ведро = 20 (было 30).
    ///   Тара в вёдрах: ведро 1, ковш экскаватора 2 и 4, малая бетономешалка 4, ковш 6,
    ///   банка и рюмка бетона 8, большая бетономешалка 10, сковорода, вок, кастрюля по 32.
    ///
    /// Окно по шагам:
    ///   1. Меры — какие числа сейчас действуют (ассет FoodUnits или значения из кода).
    ///   2. Мастерки и лопаты — вместимость ScoopItem: мастерок 2 порции, лопата 4.
    ///   3. Тара — каждому ассету CookwareDef роль из таблицы размеров → Capacity = вёдра × 20.
    ///      Нагрев (CookwareHeat) масштабируется, чтобы большая тара закипала так же быстро, как раньше.
    ///      Недостающие тары (ковши экскаватора, большая бетономешалка, вок) создаются ассетами.
    ///   4. Рецепты — рецепты из кода записываются ассетами; у всех рецептов порции переводятся
    ///      в мастерки/дозы/вёдра (там, где делится ровно), ингредиентам ставятся строгость,
    ///      «больная сторона» и обязательность (новые допуски), рецепт помечается MeasureVersion = 2.
    ///
    /// Ничего не удаляет. Префабы не создаёт; существующие префабы меняет, только если включено.
    /// Всё через Undo. Галка «Только отчёт» — ничего не менять, только написать, что было бы.
    /// Отчёт — в консоль и в файл Logs/CatCooks_NewMeasures_*.txt в папке проекта.
    ///
    /// Меню: CatCooks → Кулинария → Новые меры: тара, мастерки, рецепты…
    /// </summary>
    public sealed class CatCooksMeasuresWindow : EditorWindow
    {
        private const string MenuPath = "CatCooks/Кулинария/Новые меры: тара, мастерки, рецепты…";
        private const string RecipeFolder = "Assets/CatCooks/Food/Recipes/Resources/CookRecipes";
        private const string CookwareFolder = "Assets/CatCooks/Food/Cookware";
        private const string UnitsFolder = "Assets/CatCooks/Food/Recipes/Resources";
        private const string PrefsKey = "CatCooks.Measures.";

        // ───────────────────────── таблица размеров ─────────────────────────

        private sealed class Size
        {
            public string Key, Title;
            public float Buckets;
            public string[] Words;
            public CookwareKind Kind;
            public FoodProcess Heat;
            public string Template;     // ключ роли, чей ассет брать за образец при создании
            public Size(string key, string title, float buckets, CookwareKind kind, FoodProcess heat, string template, params string[] words)
            { Key = key; Title = title; Buckets = buckets; Kind = kind; Heat = heat; Template = template; Words = words; }
        }

        // Порядок = порядок угадывания: сперва самые точные слова.
        private static readonly Size[] Sizes =
        {
            new Size("ExcavatorSmall", "Ковш экскаватора малый", 2, CookwareKind.Storage, FoodProcess.None, "Bucket", "ковш экскаватора мал", "excavatorsmall", "excavator_small", "excavator small"),
            new Size("ExcavatorBig", "Ковш экскаватора большой", 4, CookwareKind.Storage, FoodProcess.None, "Bucket", "ковш экскаватора бол", "excavatorbig", "excavator_big", "excavator big"),
            new Size("MixerBig", "Бетономешалка большая (машина)", 10, CookwareKind.Pot, FoodProcess.None, "MixerSmall", "бетономешалка большая", "автобетон", "mixertruck", "mixer truck", "mixerbig", "mixer_big"),
            new Size("Wok", "Вок", 32, CookwareKind.Flat, FoodProcess.Fry, "Pan", "вок", "wok"),
            new Size("MixerSmall", "Бетономешалка малая", 4, CookwareKind.Pot, FoodProcess.None, "MixerBig", "бетономешалка", "betonmixer", "mixer"),
            new Size("Shot", "Рюмка бетона", 8, CookwareKind.Measure, FoodProcess.None, "Jar", "рюмка", "concreteshot", "shot"),
            new Size("Jar", "Банка бетона", 8, CookwareKind.Measure, FoodProcess.None, "Shot", "банка", "concretejar", "banka", "jar"),
            new Size("Ladle", "Ковш", 6, CookwareKind.Pot, FoodProcess.Boil, "Pot", "ковш", "kovsh", "ladle"),
            new Size("Pot", "Кастрюля", 32, CookwareKind.Pot, FoodProcess.Boil, "Ladle", "кастрюл", "cookware_pot", "pot"),
            new Size("Pan", "Сковорода", 32, CookwareKind.Flat, FoodProcess.Fry, "Wok", "сковород", "cookware_pan", "pan"),
            new Size("Bucket", "Ведро", 1, CookwareKind.Storage, FoodProcess.None, "", "ведро", "vedro", "bucket"),
        };

        // Роли в выпадающем списке: 0 — не трогать, 1 — пересчитать «как было в вёдрах», 2.. — Sizes.
        private const int RoleSkip = 0, RoleKeepBuckets = 1, RoleFirstSize = 2;
        private static string[] _roleNames;

        private static string[] RoleNames
        {
            get
            {
                if (_roleNames != null) return _roleNames;
                var list = new List<string> { "— не трогать —", "Как было, в вёдрах (×20/30)" };
                foreach (Size s in Sizes) list.Add(s.Title + " — " + s.Buckets.ToString("0.#") + " вёд.");
                _roleNames = list.ToArray();
                return _roleNames;
            }
        }

        // ───────────────────────── состояние окна ─────────────────────────

        private sealed class DefRow
        {
            public CookwareDef Def;
            public string Path, Guid;
            public int Role;
            public readonly List<string> SceneUsers = new List<string>();
            public readonly List<string> PrefabUsers = new List<string>();
            public readonly List<string> PrefabPaths = new List<string>();
        }

        private sealed class ScoopRow
        {
            public Component Item;
            public string Where;
            public int Capacity;
            public int Role;        // 0 — не трогать, 1 — мастерок, 2 — лопата
        }

        private readonly List<DefRow> _defs = new List<DefRow>();
        private readonly List<ScoopRow> _scoops = new List<ScoopRow>();
        private Vector2 _scroll;
        private bool _dryRun;
        private bool _heatScale = true;
        private bool _touchPrefabs;
        private bool _convertUnits = true;
        private bool _applyTolerance = true;
        private bool _redoMigrated;
        private bool _fUnits = true, _fScoops = true, _fCookware = true, _fRecipes = true;
        private bool _scanned;

        [MenuItem(MenuPath, priority = 300)]
        private static void Open()
        {
            CatCooksMeasuresWindow w = GetWindow<CatCooksMeasuresWindow>("Новые меры");
            w.minSize = new Vector2(620f, 480f);
            w.Show();
        }

        private void OnEnable()
        {
            foreach (Size s in Sizes) s.Buckets = EditorPrefs.GetFloat(PrefsKey + s.Key, s.Buckets);
        }

        // ───────────────────────── GUI ─────────────────────────

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox(
                "Новые меры v2: доза = 1 порция = ½ мастерка; мастерок = 2; лопата = штука = 4; ведро = 20 (было 30).\n" +
                "Перед первым запуском — коммит в git. Всё через Undo. Ничего не удаляется, префабы не создаются.",
                MessageType.Info);
            _dryRun = EditorGUILayout.ToggleLeft("Только отчёт — ничего не менять, написать, что было бы", _dryRun);
            if (!_scanned || GUILayout.Button("Пересканировать проект и сцену", GUILayout.Height(22))) Scan();

            EditorGUILayout.Space(6);
            _fUnits = EditorGUILayout.BeginFoldoutHeaderGroup(_fUnits, "1. Меры");
            if (_fUnits) DrawUnits();
            EditorGUILayout.EndFoldoutHeaderGroup();

            _fScoops = EditorGUILayout.BeginFoldoutHeaderGroup(_fScoops, "2. Мастерки и лопаты");
            if (_fScoops) DrawScoops();
            EditorGUILayout.EndFoldoutHeaderGroup();

            _fCookware = EditorGUILayout.BeginFoldoutHeaderGroup(_fCookware, "3. Тара");
            if (_fCookware) DrawCookware();
            EditorGUILayout.EndFoldoutHeaderGroup();

            _fRecipes = EditorGUILayout.BeginFoldoutHeaderGroup(_fRecipes, "4. Рецепты");
            if (_fRecipes) DrawRecipes();
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.EndScrollView();
        }

        private void DrawUnits()
        {
            FoodUnits asset = FindUnitsAsset();
            EditorGUILayout.LabelField($"Сейчас действует: ведро = {FoodUnits.Bucket} порц., мастерок = {FoodUnits.Trowel}, " +
                                       $"лопата = {FoodUnits.Shovel}, доза = {FoodUnits.Dose}, обычная штука = {FoodUnits.StandardPiece}");
            if (asset == null)
                EditorGUILayout.HelpBox("Ассета мер нет — действуют значения из кода (FoodUnits.cs): 20 / 2. Это и есть новые меры.", MessageType.None);
            else
            {
                EditorGUILayout.ObjectField("Ассет мер", asset, typeof(FoodUnits), false);
                if (asset.PortionsPerBucket != 20 || asset.PortionsPerTrowel != 2)
                {
                    EditorGUILayout.HelpBox($"В ассете старые числа: ведро {asset.PortionsPerBucket}, мастерок {asset.PortionsPerTrowel}.", MessageType.Warning);
                    if (GUILayout.Button("Поставить в ассете ведро 20, мастерок 2, штуку 4")) SetUnits(asset);
                }
            }
            if (asset == null && GUILayout.Button("Создать ассет мер (чтобы менять числа без кода)")) CreateUnitsAsset();
            if (FoodUnits.Bucket != 20)
                EditorGUILayout.HelpBox("Ведро сейчас не 20. Пункты 3 и 4 считают по тому, что действует сейчас.", MessageType.Warning);
        }

        private void DrawScoops()
        {
            _touchPrefabs = EditorGUILayout.ToggleLeft(new GUIContent("Менять и существующие префабы (не только сцену)",
                "Мастерки/лопаты и нагрев в префабах-ассетах. Новых префабов не создаёт."), _touchPrefabs);
            if (_scoops.Count == 0) { EditorGUILayout.LabelField("Мастерков и лопат (ScoopItem) не найдено."); return; }
            string[] roles = { "— не трогать —", "Мастерок → " + FoodUnits.Trowel + " порц.", "Лопата → " + FoodUnits.Shovel + " порц." };
            foreach (ScoopRow s in _scoops)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(s.Where, GUILayout.MinWidth(220));
                EditorGUILayout.LabelField("сейчас " + s.Capacity, GUILayout.Width(80));
                s.Role = EditorGUILayout.Popup(s.Role, roles, GUILayout.Width(170));
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Применить вместимость мастерков и лопат", GUILayout.Height(24))) ApplyScoops();
        }

        private void DrawCookware()
        {
            EditorGUILayout.LabelField("Размеры (вёдра). Можно поменять — запомнится в этом редакторе:", EditorStyles.miniBoldLabel);
            foreach (Size s in Sizes)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(s.Title, GUILayout.Width(230));
                float b = EditorGUILayout.FloatField(s.Buckets, GUILayout.Width(50));
                if (!Mathf.Approximately(b, s.Buckets)) { s.Buckets = Mathf.Max(0.5f, b); EditorPrefs.SetFloat(PrefsKey + s.Key, s.Buckets); }
                EditorGUILayout.LabelField("= " + Mathf.RoundToInt(s.Buckets * FoodUnits.Bucket) + " порц.", GUILayout.Width(80));
                DefRow d = FirstDefOf(s.Key);
                EditorGUILayout.LabelField(d != null ? Path.GetFileNameWithoutExtension(d.Path) : "— нет ассета —", GUILayout.MinWidth(120));
                using (new EditorGUI.DisabledScope(d == null))
                    if (GUILayout.Button(new GUIContent("→ выделенным", "Назначить эту тару выделенным объектам с Cookware"), GUILayout.Width(100)))
                        AssignToSelected(d.Def);
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Создать недостающие тары (ассеты CookwareDef)")) CreateMissing();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Ассеты тары и кто ими пользуется:", EditorStyles.miniBoldLabel);
            foreach (DefRow r in _defs)
            {
                if (r.Def == null) continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(r.Def, typeof(CookwareDef), false, GUILayout.Width(200));
                EditorGUILayout.LabelField($"«{r.Def.Title}» {r.Def.Capacity} порц.", GUILayout.MinWidth(150));
                r.Role = EditorGUILayout.Popup(r.Role, RoleNames, GUILayout.Width(250));
                EditorGUILayout.EndHorizontal();
                int nc = NewCapacity(r);
                if (r.Role != RoleSkip && nc != r.Def.Capacity)
                    EditorGUILayout.LabelField($"   станет {nc} порц. ({nc / (float)FoodUnits.Bucket:0.#} вёд.)", EditorStyles.miniLabel);
                if (r.SceneUsers.Count > 0) EditorGUILayout.LabelField("   в сцене: " + Short(r.SceneUsers), EditorStyles.wordWrappedMiniLabel);
                if (r.PrefabUsers.Count > 0) EditorGUILayout.LabelField("   в префабах: " + Short(r.PrefabUsers), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }
            _heatScale = EditorGUILayout.ToggleLeft(new GUIContent("Подогнать нагрев: большая тара закипает так же быстро, как раньше",
                "CookwareHeat «Portions Per Mass» × (новая вместимость / старая)"), _heatScale);
            if (GUILayout.Button("Применить вместимость тары", GUILayout.Height(24))) ApplyCookware();
            if (GUILayout.Button(new GUIContent("Отдельная тара для выделенных объектов",
                "Копия текущего ассета тары у каждого выделенного объекта с Cookware — чтобы менять ему вместимость отдельно (ведро, у которого стоит «Ковш», и т.п.)")))
                SplitSelected();
        }

        private void DrawRecipes()
        {
            if (Object.FindAnyObjectByType<FoodDatabase>() == null)
                EditorGUILayout.HelpBox("В открытой сцене нет FoodDatabase — рецепты из кода не соберутся. Открой 02_Game.", MessageType.Warning);
            if (GUILayout.Button(new GUIContent("Записать рецепты из кода в ассеты (чего ещё нет)",
                "Рецепты, которые книга собирает в коде (борщ, котлеты, десерты…), становятся ассетами в Resources/CookRecipes. " +
                "Книга берёт ассет, если он есть, — код-рецепт с тем же названием больше не собирается.")))
                BakeCodeRecipes();
            EditorGUILayout.Space(4);
            _convertUnits = EditorGUILayout.ToggleLeft("Порции → мастерки / дозы / вёдра (где делится ровно; количество не меняется)", _convertUnits);
            _applyTolerance = EditorGUILayout.ToggleLeft("Новые допуски: строгость, «больная сторона», обязательность (по названиям продуктов)", _applyTolerance);
            _redoMigrated = EditorGUILayout.ToggleLeft("Перезаписать и уже переведённые рецепты (MeasureVersion = 2)", _redoMigrated);
            if (GUILayout.Button("Обновить ингредиенты всех рецептов", GUILayout.Height(24))) UpdateRecipes();
        }

        // ───────────────────────── скан ─────────────────────────

        private void Scan()
        {
            _scanned = true;
            _defs.Clear();
            _scoops.Clear();
            var byGuid = new Dictionary<string, DefRow>();
            foreach (string g in AssetDatabase.FindAssets("t:CookwareDef"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                CookwareDef d = AssetDatabase.LoadAssetAtPath<CookwareDef>(p);
                if (d == null) continue;
                var r = new DefRow { Def = d, Path = p, Guid = g };
                r.Role = GuessRole(d, p);
                _defs.Add(r);
                byGuid[g] = r;
            }
            _defs.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal));

            // сцена
            foreach (Cookware cw in Object.FindObjectsByType<Cookware>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cw == null || cw.Def == null) continue;
                string g = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(cw.Def));
                DefRow r;
                if (byGuid.TryGetValue(g, out r)) r.SceneUsers.Add(cw.gameObject.name);
            }
            // префабы — по тексту (быстро, без загрузки)
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!p.StartsWith("Assets/")) continue;
                string text;
                try { text = File.ReadAllText(p); } catch (Exception) { continue; }
                foreach (DefRow r in _defs)
                    if (text.IndexOf(r.Guid, StringComparison.Ordinal) >= 0) { r.PrefabUsers.Add(Path.GetFileNameWithoutExtension(p)); r.PrefabPaths.Add(p); }
            }

            // мастерки и лопаты
            Type scoopType = FindType("ScoopItem");
            if (scoopType != null)
            {
                foreach (Object o in Object.FindObjectsByType(scoopType, FindObjectsInactive.Include, FindObjectsSortMode.None))
                    AddScoop(o as Component, "сцена: " + ((Component)o).gameObject.name);
                if (_touchPrefabs)
                    foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
                    {
                        string p = AssetDatabase.GUIDToAssetPath(g);
                        if (!p.StartsWith("Assets/")) continue;
                        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                        if (go == null) continue;
                        foreach (Component c in go.GetComponentsInChildren(scoopType, true))
                            AddScoop(c, "префаб: " + Path.GetFileNameWithoutExtension(p) + "/" + c.gameObject.name);
                    }
            }
            Repaint();
        }

        private void AddScoop(Component c, string where)
        {
            if (c == null) return;
            var so = new SerializedObject(c);
            SerializedProperty cap = so.FindProperty("_capacity");
            if (cap == null) return;
            var row = new ScoopRow { Item = c, Where = where, Capacity = cap.intValue };
            string n = c.gameObject.name.ToLowerInvariant();
            if (n.Contains("shelma") || n.Contains("мастер") || n.Contains("trowel") || n.Contains("кельм")) row.Role = 1;
            else if (n.Contains("shovel") || n.Contains("лопат")) row.Role = 2;
            else row.Role = row.Capacity <= FoodUnits.Trowel ? 1 : 2;
            _scoops.Add(row);
        }

        private static int GuessRole(CookwareDef d, string path)
        {
            string s = (Path.GetFileNameWithoutExtension(path) + " " + d.Title).ToLowerInvariant();
            if (s.Contains("опалубк") || s.Contains("formwork") || s.Contains("казан") || s.Contains("kazan")) return RoleKeepBuckets;
            if (s.Contains("тарелк") || s.Contains("plate") || s.Contains("миск") || s.Contains("bowl") || s.Contains("форма") ||
                s.Contains("mold") || s.Contains("грохот") || s.Contains("sifter") || s.Contains("духовк") || s.Contains("oven") ||
                s.Contains("вертел") || s.Contains("spit") || s.Contains("корыт") || s.Contains("tray")) return RoleSkip;
            for (int i = 0; i < Sizes.Length; i++)
                foreach (string w in Sizes[i].Words)
                    if (s.Contains(w)) return RoleFirstSize + i;
            return RoleSkip;
        }

        private DefRow FirstDefOf(string key)
        {
            for (int i = 0; i < Sizes.Length; i++)
            {
                if (Sizes[i].Key != key) continue;
                foreach (DefRow r in _defs) if (r.Role == RoleFirstSize + i && r.Def != null) return r;
            }
            return null;
        }

        private static int NewCapacity(DefRow r)
        {
            if (r.Def == null || r.Role == RoleSkip) return r.Def != null ? r.Def.Capacity : 0;
            if (r.Role == RoleKeepBuckets)
                return Mathf.Max(1, Mathf.RoundToInt(r.Def.Capacity * FoodUnits.Bucket / (float)FoodUnits.OldBucket));
            Size s = Sizes[r.Role - RoleFirstSize];
            return Mathf.Max(1, Mathf.RoundToInt(s.Buckets * FoodUnits.Bucket));
        }

        // ───────────────────────── 1. меры ─────────────────────────

        private static FoodUnits FindUnitsAsset()
        {
            foreach (string g in AssetDatabase.FindAssets("t:FoodUnits"))
            {
                FoodUnits u = AssetDatabase.LoadAssetAtPath<FoodUnits>(AssetDatabase.GUIDToAssetPath(g));
                if (u != null) return u;
            }
            return null;
        }

        private void SetUnits(FoodUnits u)
        {
            if (_dryRun) { Report("Меры: в ассете было бы ведро 20, мастерок 2, штука 4 (было " + u.PortionsPerBucket + " / " + u.PortionsPerTrowel + ")."); return; }
            Undo.RecordObject(u, "Новые меры");
            u.PortionsPerBucket = 20; u.PortionsPerTrowel = 2; u.PortionsPerStandardPiece = 4;
            EditorUtility.SetDirty(u);
            AssetDatabase.SaveAssets();
            FoodUnits.Reload();
            Report("✔ Меры: ведро 20, мастерок 2, штука 4.");
        }

        private void CreateUnitsAsset()
        {
            if (_dryRun) { Report("Меры: был бы создан " + UnitsFolder + "/FoodUnits.asset (20 / 2 / 4)."); return; }
            EnsureFolder(UnitsFolder);
            var u = CreateInstance<FoodUnits>();
            u.PortionsPerBucket = 20; u.PortionsPerTrowel = 2; u.PortionsPerStandardPiece = 4;
            AssetDatabase.CreateAsset(u, UnitsFolder + "/FoodUnits.asset");
            AssetDatabase.SaveAssets();
            FoodUnits.Reload();
            Report("✔ Создан " + UnitsFolder + "/FoodUnits.asset (ведро 20, мастерок 2, штука 4).");
        }

        // ───────────────────────── 2. мастерки и лопаты ─────────────────────────

        private void ApplyScoops()
        {
            var log = new StringBuilder("МАСТЕРКИ И ЛОПАТЫ\n");
            int changed = 0;
            foreach (ScoopRow s in _scoops)
            {
                if (s.Item == null || s.Role == 0) continue;
                int want = s.Role == 1 ? FoodUnits.Trowel : FoodUnits.Shovel;
                if (s.Capacity == want) { log.AppendLine($"  {s.Where}: уже {want}"); continue; }
                log.AppendLine($"  {s.Where}: {s.Capacity} → {want}" + (_dryRun ? " (отчёт)" : ""));
                if (_dryRun) continue;
                bool isPrefabAsset = EditorUtility.IsPersistent(s.Item);
                if (isPrefabAsset && !_touchPrefabs) continue;
                var so = new SerializedObject(s.Item);
                so.FindProperty("_capacity").intValue = want;
                so.ApplyModifiedProperties();
                if (isPrefabAsset) EditorUtility.SetDirty(s.Item);
                else EditorSceneManager.MarkSceneDirty(s.Item.gameObject.scene);
                s.Capacity = want;
                changed++;
            }
            if (!_dryRun && changed > 0) AssetDatabase.SaveAssets();
            log.AppendLine($"Изменено: {changed}.");
            Report(log.ToString());
        }

        // ───────────────────────── 3. тара ─────────────────────────

        private void ApplyCookware()
        {
            var log = new StringBuilder("ТАРА\n");
            int changed = 0;
            foreach (DefRow r in _defs)
            {
                if (r.Def == null || r.Role == RoleSkip) continue;
                int oldCap = r.Def.Capacity, newCap = NewCapacity(r);
                if (oldCap == newCap) { log.AppendLine($"  {Path.GetFileNameWithoutExtension(r.Path)} «{r.Def.Title}»: уже {newCap}"); continue; }
                log.AppendLine($"  {Path.GetFileNameWithoutExtension(r.Path)} «{r.Def.Title}»: {oldCap} → {newCap} порц. " +
                               $"({newCap / (float)FoodUnits.Bucket:0.#} вёд.)" + (_dryRun ? " (отчёт)" : ""));
                if (r.SceneUsers.Count > 0) log.AppendLine("      в сцене: " + string.Join(", ", r.SceneUsers.ToArray()));
                if (_dryRun) continue;
                Undo.RecordObject(r.Def, "Новые меры: тара");
                r.Def.Capacity = newCap;
                EditorUtility.SetDirty(r.Def);
                changed++;
                if (_heatScale) ScaleHeat(r, newCap / (float)Mathf.Max(1, oldCap), log);
            }
            if (!_dryRun) AssetDatabase.SaveAssets();
            log.AppendLine($"Изменено ассетов: {changed}.");
            log.AppendLine("Помни: рецепты «на всю тару» (бульон, борщ) считались под старую кастрюлю — проверь их в пункте 4 (отчёт пометит ⚠).");
            Report(log.ToString());
        }

        /// <summary>Нагрев: mass = 1 + порции / PortionsPerMass. Тара выросла в k раз — PortionsPerMass × k, время закипания то же.</summary>
        private void ScaleHeat(DefRow r, float k, StringBuilder log)
        {
            if (Mathf.Approximately(k, 1f)) return;
            foreach (Cookware cw in Object.FindObjectsByType<Cookware>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cw == null || cw.Def != r.Def) continue;
                CookwareHeat h = cw.GetComponent<CookwareHeat>();
                if (h == null) continue;
                if (ScaleHeatOn(h, k, log, "сцена: " + cw.gameObject.name)) EditorSceneManager.MarkSceneDirty(cw.gameObject.scene);
            }
            if (!_touchPrefabs) return;
            foreach (string p in r.PrefabPaths)
            {
                string name = Path.GetFileNameWithoutExtension(p);
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(p);
                    bool any = false;
                    foreach (Cookware cw in root.GetComponentsInChildren<Cookware>(true))
                    {
                        if (cw.Def != r.Def) continue;
                        CookwareHeat h = cw.GetComponent<CookwareHeat>();
                        if (h != null && ScaleHeatOn(h, k, log, "префаб: " + name)) any = true;
                    }
                    if (any) PrefabUtility.SaveAsPrefabAsset(root, p);
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static bool ScaleHeatOn(CookwareHeat h, float k, StringBuilder log, string where)
        {
            var so = new SerializedObject(h);
            SerializedProperty p = so.FindProperty("_portionsPerMass");
            if (p == null) return false;
            float was = p.floatValue;
            p.floatValue = Mathf.Max(1f, was * k);
            so.ApplyModifiedProperties();
            log.AppendLine($"      нагрев {where}: Portions Per Mass {was:0.#} → {p.floatValue:0.#}");
            return true;
        }

        private void CreateMissing()
        {
            var log = new StringBuilder("НОВЫЕ ТАРЫ\n");
            int made = 0;
            for (int i = 0; i < Sizes.Length; i++)
            {
                Size s = Sizes[i];
                if (FirstDefOf(s.Key) != null) continue;
                DefRow tmpl = string.IsNullOrEmpty(s.Template) ? null : FirstDefOf(s.Template);
                int cap = Mathf.RoundToInt(s.Buckets * FoodUnits.Bucket);
                string path = AssetDatabase.GenerateUniqueAssetPath(CookwareFolder + "/Cookware_" + s.Key + ".asset");
                log.AppendLine($"  {s.Title}: {path}, {cap} порц." + (tmpl != null ? " (по образцу " + Path.GetFileNameWithoutExtension(tmpl.Path) + ")" : "") + (_dryRun ? " (отчёт)" : ""));
                if (_dryRun) continue;
                EnsureFolder(CookwareFolder);
                CookwareDef d = tmpl != null ? Instantiate(tmpl.Def) : CreateInstance<CookwareDef>();
                if (tmpl == null)
                {
                    d.Kind = s.Kind; d.HeatProcess = s.Heat;
                    d.NeedsHeatSource = s.Heat != FoodProcess.None;
                    d.Absorbs = s.Kind == CookwareKind.Pot || s.Kind == CookwareKind.Measure || s.Kind == CookwareKind.Storage;
                    d.BlendsColor = s.Kind == CookwareKind.Pot;
                }
                d.Title = s.Title;
                d.Capacity = cap;
                AssetDatabase.CreateAsset(d, path);
                made++;
            }
            if (!_dryRun && made > 0) { AssetDatabase.SaveAssets(); Scan(); }
            log.AppendLine(made > 0 || _dryRun ? "Назначить модели — выдели объект(ы) с Cookware и нажми «→ выделенным» у нужного размера." : "Все размеры уже есть.");
            Report(log.ToString());
        }

        private void AssignToSelected(CookwareDef def)
        {
            var log = new StringBuilder($"НАЗНАЧИТЬ «{def.Title}» ({def.Capacity} порц.)\n");
            int n = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                Cookware cw = go.GetComponentInParent<Cookware>();
                if (cw == null) { log.AppendLine($"  {go.name}: нет Cookware — пропуск"); continue; }
                log.AppendLine($"  {cw.gameObject.name}: {(cw.Def != null ? cw.Def.name : "—")} → {def.name}" + (_dryRun ? " (отчёт)" : ""));
                if (_dryRun) continue;
                Undo.RecordObject(cw, "Назначить тару");
                cw.Def = def;
                PrefabUtility.RecordPrefabInstancePropertyModifications(cw);
                EditorSceneManager.MarkSceneDirty(cw.gameObject.scene);
                n++;
            }
            if (Selection.gameObjects.Length == 0) log.AppendLine("  Ничего не выделено.");
            Report(log.ToString());
            if (n > 0) Scan();
        }

        private void SplitSelected()
        {
            var log = new StringBuilder("ОТДЕЛЬНАЯ ТАРА\n");
            int n = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                Cookware cw = go.GetComponentInParent<Cookware>();
                if (cw == null || cw.Def == null) { log.AppendLine($"  {go.name}: нет Cookware или тары — пропуск"); continue; }
                string src = AssetDatabase.GetAssetPath(cw.Def);
                string dir = string.IsNullOrEmpty(src) ? CookwareFolder : Path.GetDirectoryName(src).Replace('\\', '/');
                string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/Cookware_" + Safe(cw.gameObject.name) + ".asset");
                log.AppendLine($"  {cw.gameObject.name}: копия {cw.Def.name} → {path}" + (_dryRun ? " (отчёт)" : ""));
                if (_dryRun) continue;
                CookwareDef copy = Instantiate(cw.Def);
                AssetDatabase.CreateAsset(copy, path);
                Undo.RecordObject(cw, "Отдельная тара");
                cw.Def = copy;
                PrefabUtility.RecordPrefabInstancePropertyModifications(cw);
                EditorSceneManager.MarkSceneDirty(cw.gameObject.scene);
                n++;
            }
            if (Selection.gameObjects.Length == 0) log.AppendLine("  Ничего не выделено.");
            if (n > 0) { AssetDatabase.SaveAssets(); Scan(); }
            Report(log.ToString());
        }

        // ───────────────────────── 4. рецепты ─────────────────────────

        private static void ResetBook()
        {
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo rec = typeof(CookRecipeBook).GetField("_recipes", f);
            if (rec != null) rec.SetValue(null, null);
            FieldInfo next = typeof(CookRecipeBook).GetField("_nextTry", f);
            if (next != null) next.SetValue(null, 0f);
        }

        private void BakeCodeRecipes()
        {
            var log = new StringBuilder("РЕЦЕПТЫ ИЗ КОДА → АССЕТЫ\n");
            if (Object.FindAnyObjectByType<FoodDatabase>() == null) { Report("✖ В сцене нет FoodDatabase — открой 02_Game."); return; }
            ResetBook();
            IReadOnlyList<CookRecipeDef> all = CookRecipeBook.Recipes;
            int made = 0, skipped = 0;
            if (!_dryRun) EnsureFolder(RecipeFolder);
            foreach (CookRecipeDef r in all)
            {
                if (r == null || AssetDatabase.Contains(r)) continue;
                string path = RecipeFolder + "/Recipe_" + Safe(r.Title) + ".asset";
                if (File.Exists(path)) { log.AppendLine($"  «{r.Title}»: файл уже есть ({path}) — пропуск"); skipped++; continue; }
                log.AppendLine($"  «{r.Title}» → {path}" + (_dryRun ? " (отчёт)" : ""));
                if (_dryRun) continue;
                CookRecipeDef copy = Instantiate(r);
                copy.hideFlags = HideFlags.None;
                copy.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(copy, path);
                made++;
            }
            if (!_dryRun) { AssetDatabase.SaveAssets(); ResetBook(); }
            log.AppendLine($"Записано: {made}, пропущено: {skipped}. Книга теперь берёт эти ассеты (код с теми же названиями не собирается).");
            Report(log.ToString());
        }

        private void UpdateRecipes()
        {
            var log = new StringBuilder("РЕЦЕПТЫ: НОВЫЕ МЕРЫ И ДОПУСКИ\n");
            int changed = 0, skipped = 0;
            foreach (string g in AssetDatabase.FindAssets("t:CookRecipeDef"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                CookRecipeDef r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(path);
                if (r == null) continue;
                if (r.MeasureVersion >= FoodUnits.Version && !_redoMigrated) { skipped++; continue; }
                if (!_dryRun) Undo.RecordObject(r, "Новые меры: рецепт");
                log.AppendLine($"«{r.Title}» ({Path.GetFileName(path)})" + (_dryRun ? " (отчёт)" : ""));

                // основа
                if (r.Base != null)
                {
                    float a = r.BaseAmount; CookUnit u = r.BaseUnit;
                    if (_convertUnits && ConvertUnit(ref a, ref u, r.Base, r.BaseState))
                    {
                        log.AppendLine($"    основа {r.Base.Title}: {r.BaseAmount:0.##} порц. → {FoodUnits.Describe(FoodUnits.ToPortions(a, u), u)}");
                        if (!_dryRun) { r.BaseAmount = a; r.BaseUnit = u; }
                    }
                    else
                        log.AppendLine($"    основа {r.Base.Title}: " + (r.BaseFillsContainer ? "заполняет тару" : FoodUnits.Describe(FoodUnits.ToPortions(r.BaseAmount, r.BaseUnit), r.BaseUnit)));
                }

                bool bucketBase = r.Base != null && (r.BaseUnit == CookUnit.Bucket || r.BaseFillsContainer) && !r.PerContainer;
                for (int i = 0; i < r.Ingredients.Count; i++)
                {
                    CookRecipeDef.Ingredient ing = r.Ingredients[i];
                    if (ing.Product == null) continue;
                    var line = new StringBuilder("    " + ing.Product.Title + ": ");
                    float a = ing.Amount; CookUnit u = ing.Unit;
                    if (_convertUnits && ConvertUnit(ref a, ref u, ing.Product, ing.State))
                    {
                        line.Append($"{ing.Amount:0.##} порц. → ");
                        ing.Amount = a; ing.Unit = u;
                    }
                    line.Append(ing.Describe(ing.ToPortions(ing.Amount)));
                    if (_applyTolerance && (ing.Strictness == FoodStrictness.Legacy || _redoMigrated))
                    {
                        ing.Strictness = GuessStrictness(ing.Product.Title, r.Title);
                        ing.Pain = ing.Reduces ? FoodPainSide.Less : GuessPain(ing.Product.Title);
                        ing.Need = FoodIngredientNeed.Required;
                    }
                    if (ing.Strictness != FoodStrictness.Legacy)
                    {
                        int need = ing.ToPortions(ing.Amount);
                        int idl, acc;
                        FoodTolerance.Slack(ing.Strictness, need, out idl, out acc);
                        line.Append($" — {StrictWord(ing.Strictness)}, {PainWord(ing.Pain)}; идеал ±{idl}, допустимо ±{acc} порц.");
                    }
                    log.AppendLine(line.ToString());
                    if (!_dryRun) r.Ingredients[i] = ing;
                }
                if (bucketBase && r.Ingredients.Count > 0)
                    log.AppendLine($"    ℹ основа в вёдрах: ведро теперь {FoodUnits.Bucket} порц. (было {FoodUnits.OldBucket}) — доля остальных ингредиентов выросла в {FoodUnits.OldBucket / (float)FoodUnits.Bucket:0.##} раза");
                if (r.PerContainer)
                    log.AppendLine("    ⚠ количества на ВСЮ тару (PerContainer) — считались под старую вместимость; проверь руками под новую кастрюлю");
                if (!_dryRun)
                {
                    r.MeasureVersion = FoodUnits.Version;
                    EditorUtility.SetDirty(r);
                }
                changed++;
            }
            if (!_dryRun) { AssetDatabase.SaveAssets(); ResetBook(); }
            log.AppendLine($"Обновлено рецептов: {changed}, уже были переведены: {skipped}.");
            log.AppendLine("Рецепты, которые ещё собираются в коде, не тронуты — сначала «Записать рецепты из кода в ассеты».");
            Report(log.ToString());
        }

        /// <summary>Порции → натуральная мера продукта, только если делится ровно. Количество порций не меняется.</summary>
        private static bool ConvertUnit(ref float amount, ref CookUnit unit, ProductDef p, FoodState st)
        {
            if (unit != CookUnit.Portion || amount <= 0f) return false;
            int portions = Mathf.RoundToInt(amount);
            if (!Mathf.Approximately(portions, amount)) return false;
            CookUnit nat = FoodUnits.NaturalUnit(p, st);
            if (nat == CookUnit.Bucket && portions % FoodUnits.Bucket == 0) { amount = portions / FoodUnits.Bucket; unit = CookUnit.Bucket; return true; }
            if ((nat == CookUnit.Bucket || nat == CookUnit.Trowel || nat == CookUnit.Shovel) && portions % FoodUnits.Trowel == 0)
            { amount = portions / FoodUnits.Trowel; unit = CookUnit.Trowel; return true; }
            if (nat == CookUnit.Trowel || nat == CookUnit.Dose || nat == CookUnit.Shovel)
            { amount = portions / (float)FoodUnits.Dose; unit = CookUnit.Dose; return true; }
            return false;       // штуки, порции — оставить как есть
        }

        private static readonly string[] StrictWords = { "соль", "дрожж", "закваск", "белок", "ваниль", "уксус", "чили", "желток", "мускат" };
        private static readonly string[] LooseWords = { "лук", "морков", "свёкл", "свекл", "капуст", "картоф", "помидор", "томат", "укроп", "петруш",
                                                        "шалфей", "тимьян", "лавр", "зелен", "вода", "бульон", "кост", "голяшк", "гриб", "шампиньон",
                                                        "чеснок", "зубчик", "сок" };
        private static readonly string[] PainMoreWords = { "соль", "уксус", "чили", "перец", "дрожж", "закваск", "горчиц", "паприк", "чеснок", "зубчик", "мускат", "бурбон" };
        private static readonly string[] DessertWords = { "морож", "меренг", "макарон", "бисквит", "карамел", "ганаш", "шоколад", "яичная масса", "какао", "аляск" };

        private static FoodStrictness GuessStrictness(string product, string recipe)
        {
            string p = (product ?? "").ToLowerInvariant();
            string r = (recipe ?? "").ToLowerInvariant();
            if (Has(p, StrictWords)) return FoodStrictness.Strict;
            if (p.Contains("сахар")) return Has(r, DessertWords) ? FoodStrictness.Strict : FoodStrictness.Normal;
            if (p.Contains("паст") || p.Contains("масло")) return FoodStrictness.Normal;
            if (Has(p, LooseWords)) return FoodStrictness.Loose;
            return FoodStrictness.Normal;
        }

        private static FoodPainSide GuessPain(string product)
        {
            string p = (product ?? "").ToLowerInvariant();
            return Has(p, PainMoreWords) ? FoodPainSide.More : FoodPainSide.Both;
        }

        private static string StrictWord(FoodStrictness s)
        {
            switch (s)
            {
                case FoodStrictness.Strict: return "строгий";
                case FoodStrictness.Loose: return "свободный";
                case FoodStrictness.Normal: return "обычный";
                default: return "старый допуск";
            }
        }

        private static string PainWord(FoodPainSide p)
        {
            switch (p)
            {
                case FoodPainSide.More: return "больнее много";
                case FoodPainSide.Less: return "больнее мало";
                default: return "одинаково";
            }
        }

        // ───────────────────────── мелочи ─────────────────────────

        private static bool Has(string s, string[] words)
        {
            foreach (string w in words) if (s.Contains(w)) return true;
            return false;
        }

        private static string Short(List<string> names)
        {
            if (names.Count <= 6) return string.Join(", ", names.ToArray());
            return string.Join(", ", names.GetRange(0, 6).ToArray()) + $" … и ещё {names.Count - 6}";
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Без_имени";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace('/', '_').Replace('\\', '_').Trim();
        }

        private static Type FindType(string name)
        {
            foreach (Type t in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
                if (t.Name == name) return t;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private void Report(string text)
        {
            string head = "[Новые меры] " + (_dryRun ? "ОТЧЁТ (ничего не изменено)\n" : "\n");
            Debug.Log(head + text);
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "CatCooks_NewMeasures_" + DateTime.Now.ToString("yyyyMMdd") + ".txt");
                File.AppendAllText(file, "\n──── " + DateTime.Now.ToString("HH:mm:ss") + " ────\n" + head + text, Encoding.UTF8);
            }
            catch (Exception e) { Debug.LogWarning("[Новые меры] отчёт в файл не записан: " + e.Message); }
        }
    }
}
#endif
