using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// Инспектор застройки: карта ролей модулей (ЛКМ — сменить роль, ПКМ — перестроить только этот модуль),
    /// кнопки генерации, заполнение слоёв из папок. Меню CatCooks → Застройка.
    /// </summary>
    [CustomEditor(typeof(CityBlockGenerator))]
    public sealed class CityBlockGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CityBlockGenerator g = (CityBlockGenerator)target;
            serializedObject.Update();

            if (g.Grid == null) EditorGUILayout.HelpBox("Укажи Grid — генератор плиток (сетка модулей 8×8).", MessageType.Warning);

            // ── карта ролей ──
            EditorGUILayout.LabelField("Карта модулей (вид сверху, Z вверх, X вправо)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("ЛКМ — роль: пусто → уровень → слой 1 → слой 2 · ПКМ — перестроить этот модуль (новый вариант)", EditorStyles.miniLabel);
            if (g.ManualLayer1) EditorGUILayout.LabelField("Ctrl+ЛКМ по модулю слоя 1 — открыть его в окне «Модуль ближнего слоя»", EditorStyles.miniLabel);
            DrawRoles(g);
            using (new EditorGUILayout.HorizontalScope())
            {
                Legend(CityRole.Level, "уровень (не трогаем)");
                Legend(CityRole.Ring1, "слой 1");
                Legend(CityRole.Ring2, "слой 2");
                Legend(CityRole.Empty, "пусто");
            }
            if (GUILayout.Button("Роли по кольцам заново (центр 2×2: уровни по диагонали)"))
            {
                Undo.RecordObject(g, "Застройка — роли"); g.ResetRoles(); EditorUtility.SetDirty(g); SceneView.RepaintAll();
            }

            // ── ближний слой вручную ──
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                bool man = EditorGUILayout.ToggleLeft("Ближний слой вручную (по модулю, ключевые здания руками)", g.ManualLayer1);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(g, "Застройка — ближний слой"); g.ManualLayer1 = man; Dirty(g); }
                GUI.backgroundColor = new Color(1f, 0.85f, 0.55f);
                if (GUILayout.Button("Окно модуля", GUILayout.Width(110))) CityModuleWindow.Open(g, -1, -1);
                GUI.backgroundColor = Color.white;
            }

            // ── кнопки ──
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button(g.ManualLayer1 ? "Застроить всё (кроме ближнего)" : "Застроить всё", GUILayout.Height(30))) { CityFacadeTool.DetectAll(g, false); g.GenerateAll(); Dirty(g); }
                GUI.backgroundColor = new Color(1f, 0.95f, 0.6f);
                if (GUILayout.Button("Новый вариант (seed+1)", GUILayout.Height(30)))
                {
                    Undo.RecordObject(g, "Застройка"); g.Seed++; CityFacadeTool.DetectAll(g, false); g.GenerateAll(); Dirty(g);
                }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Очистить", GUILayout.Height(30))) { g.ClearAll(); Dirty(g); }
            }
            GUI.backgroundColor = new Color(0.6f, 0.85f, 1f);
            if (GUILayout.Button("Починить окна (все дома: стёкла, разметка, цвет окон в атласе)", GUILayout.Height(24))) { CityAtlasTool.FixAll(g); EnsureNightBridge(g); Dirty(g); }
            if (GUILayout.Button("Рабочие стороны домов (найти заново: где окна — к улице, глухие — к соседям)", GUILayout.Height(22))) { CityFacadeTool.DetectAll(g, true); Dirty(g); }
            if (GUILayout.Button("Окна небоскрёбов: применить стили из списка домов (Custom Night)", GUILayout.Height(22))) { Undo.RecordObject(g, "Окна"); g.ApplyNightStyles(); Dirty(g); }
            if (GUILayout.Button("Офисы: отметить небоскрёбы (ночью — частично, без перегенерации)", GUILayout.Height(22)))
            {
                Vector2 ms = g.ModuleSize();
                CityOfficeTower.MarkAll(g.transform, Mathf.Min(ms.x, ms.y), 2f, false);
                Dirty(g);
            }
            GUI.backgroundColor = new Color(1f, 0.8f, 0.9f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Стиль: тёплый сочный (как в референсе)", GUILayout.Height(24))) { CityLookTool.Apply(g, CityLookTool.Juicy); Dirty(g); }
                if (GUILayout.Button("мягкий", GUILayout.Width(70), GUILayout.Height(24))) { CityLookTool.Apply(g, CityLookTool.Soft); Dirty(g); }
                if (GUILayout.Button("Вернуть", GUILayout.Width(70), GUILayout.Height(24))) CityLookTool.Restore();
            }
            GUI.backgroundColor = new Color(0.6f, 0.85f, 1f);
            if (GUILayout.Button("Ближний слой по-городски (без мелких, + средние из слоя 2, ровные ряды)", GUILayout.Height(22))) { NearLayerCity(g); Dirty(g); }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Загрузка моделей", EditorStyles.boldLabel);
            LoaderUI(g, true);
            LoaderUI(g, false);
            Stats(g.Layer1, g); Stats(g.Layer2, g);
            EditorGUILayout.Space(8);
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

        }

        /// <summary>
        /// Ближний слой «по-городски»: мелкие домики не ставим (вес 0), из слоя 2 добавляем средние/крупные красивые
        /// (не длинные рядовые, не низкие, не небоскрёбы), включаем «Порядок». Списки домов не удаляются — можно вернуть.
        /// </summary>
        public static void NearLayerCity(CityBlockGenerator g)
        {
            Undo.RecordObject(g, "Ближний слой по-городски");
            Vector2 ms = g.ModuleSize();
            float s = Mathf.Min(ms.x, ms.y);
            CityLayer near = g.Layer1, far = g.Layer2;
            near.RowKindWeights.x = 0f;
            near.InnerKindWeights.x = 0f;
            near.Tidy = true;
            // эталон высоты — средние дома ближнего слоя
            List<float> hs = new List<float>();
            foreach (BuildingEntry e in near.Buildings)
            {
                Bounds b;
                if (e == null || e.Prefab == null || !TileGridGenerator.MeasurePrefab(e.Prefab, out b)) continue;
                BuildingKind k = e.Kind != BuildingKind.Auto ? e.Kind : CityBlockGenerator.AutoKind(b, s);
                if (k == BuildingKind.Mid || k == BuildingKind.Large) hs.Add(b.size.y);
            }
            hs.Sort();
            float href = hs.Count > 0 ? hs[hs.Count / 2] : 0f;
            List<string> added = new List<string>(), skipped = new List<string>();
            foreach (BuildingEntry e in far.Buildings)
            {
                Bounds b;
                if (e == null || e.Prefab == null || !TileGridGenerator.MeasurePrefab(e.Prefab, out b)) continue;
                BuildingKind k = e.Kind != BuildingKind.Auto ? e.Kind : CityBlockGenerator.AutoKind(b, s);
                bool has = false;
                foreach (BuildingEntry n in near.Buildings) if (n != null && n.Prefab == e.Prefab) { has = true; break; }
                if (has) continue;
                float w = Mathf.Max(b.size.x, b.size.z), d = Mathf.Min(b.size.x, b.size.z);
                bool ok = (k == BuildingKind.Mid || k == BuildingKind.Large) && w < d * 1.8f && b.size.y >= href * 0.45f;
                if (!ok) { skipped.Add(e.Prefab.name + " (" + k + ")"); continue; }
                near.Buildings.Add(new BuildingEntry
                {
                    Prefab = e.Prefab, Weight = 0.6f, Kind = e.Kind, Facade = e.Facade, Material = e.Material,
                    Sides = e.Sides, SidesManual = e.SidesManual
                });
                added.Add(e.Prefab.name);
            }
            Debug.Log($"[Застройка] ближний слой по-городски: мелкие выключены (веса Small = 0), «Порядок» включён, из слоя 2 добавлено {added.Count}: " +
                      string.Join(", ", added.ToArray()) + "\n  не взяты (низкие/длинные/небоскрёбы/мелкие): " + string.Join(", ", skipped.ToArray()) +
                      "\n  Вес добавленных 0.6 — меняй в списке «Слой 1». Нажми «Застроить всё».", g);
        }

        private static void Stats(CityLayer l, CityBlockGenerator g)
        {
            Vector2 ms = g.ModuleSize();
            float s = Mathf.Min(ms.x, ms.y);
            int sm = 0, mid = 0, lg = 0, tw = 0, row = 0;
            for (int i = 0; i < l.Buildings.Count; i++)
            {
                BuildingEntry e = l.Buildings[i];
                if (e == null || e.Prefab == null) continue;
                BuildingKind k = e.Kind;
                if (k == BuildingKind.Auto)
                {
                    Bounds b;
                    if (!TileGridGenerator.MeasurePrefab(e.Prefab, out b)) continue;
                    k = CityBlockGenerator.AutoKind(b, s);
                }
                if (k == BuildingKind.Small) sm++; else if (k == BuildingKind.Mid) mid++; else if (k == BuildingKind.Large) lg++;
                else if (k == BuildingKind.Tower) tw++; else row++;
            }
            EditorGUILayout.LabelField($"{l.Name}: мелких {sm}, средних {mid}, крупных {lg}, рядовых {row}, небоскрёбов {tw} · крыши {l.RoofProps.Count}, тротуар {l.StreetProps.Count}, машины {l.Cars.Count}", EditorStyles.miniLabel);
        }

        private static void Legend(CityRole r, string text)
        {
            Rect rr = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
            rr.y += 3; rr.height = 10;
            EditorGUI.DrawRect(rr, CityBlockGenerator.RoleColor(r));
            GUILayout.Label(text, EditorStyles.miniLabel);
        }

        private void DrawRoles(CityBlockGenerator g)
        {
            int w = g.Width, d = g.Depth;
            float avail = EditorGUIUtility.currentViewWidth - 40f;
            float cell = Mathf.Clamp(Mathf.Floor(avail / w), 10f, 30f);
            Rect area = GUILayoutUtility.GetRect(cell * w, cell * d, GUILayout.ExpandWidth(false));
            area.x += Mathf.Max(0f, (avail - cell * w) * 0.5f);
            area.width = cell * w; area.height = cell * d;
            Event e = Event.current;
            if (area.Contains(e.mousePosition) && e.type == EventType.MouseDown)
            {
                int x = Mathf.Clamp((int)((e.mousePosition.x - area.x) / cell), 0, w - 1);
                int z = Mathf.Clamp(d - 1 - (int)((e.mousePosition.y - area.y) / cell), 0, d - 1);
                if (e.button == 0 && e.control && g.ManualLayer1 && g.GetRole(x, z) == CityRole.Ring1)
                {
                    CityModuleWindow.Open(g, x, z);
                }
                else if (e.button == 0)
                {
                    Undo.RecordObject(g, "Застройка — роль");
                    CityRole r = g.GetRole(x, z);
                    r = r == CityRole.Empty ? CityRole.Level : r == CityRole.Level ? CityRole.Ring1 : r == CityRole.Ring1 ? CityRole.Ring2 : CityRole.Empty;
                    g.SetRole(x, z, r);
                    EditorUtility.SetDirty(g);
                    SceneView.RepaintAll();
                }
                else if (e.button == 1)
                {
                    Undo.RecordObject(g, "Застройка — модуль");
                    g.Reroll(x, z);
                    g.GenerateModule(x, z, true);
                    Dirty(g);
                }
                e.Use();
            }
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, new Color(0.1f, 0.1f, 0.1f));
                for (int z = 0; z < d; z++)
                    for (int x = 0; x < w; x++)
                    {
                        Rect r = new Rect(area.x + x * cell + 1, area.y + (d - 1 - z) * cell + 1, cell - 2, cell - 2);
                        EditorGUI.DrawRect(r, CityBlockGenerator.RoleColor(g.GetRole(x, z)));
                    }
            }
        }

        private static void Dirty(CityBlockGenerator g)
        {
            EditorUtility.SetDirty(g);
            if (!UnityEditor.EditorApplication.isPlaying && g.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(g.gameObject.scene);
        }

        // ───────────────────────── ЗАГРУЗКА МОДЕЛЕЙ ─────────────────────────

        /// <summary>Папка слоя (перетащить из Project) + «Загрузить», и поле, куда можно бросить модели/папки мышью.</summary>
        private void LoaderUI(CityBlockGenerator g, bool layer1)
        {
            CityLayer l = layer1 ? g.Layer1 : g.Layer2;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(l.Name, EditorStyles.miniBoldLabel);
                EditorGUI.BeginChangeCheck();
                Object folder = EditorGUILayout.ObjectField("Папка с домами", l.SourceFolder, typeof(DefaultAsset), false);
                bool sub = EditorGUILayout.Toggle("С подпапками", l.IncludeSubfolders);
                bool props = EditorGUILayout.Toggle("И пропсы (props, street props)", l.LoadProps);
                bool atlas = EditorGUILayout.Toggle("Атласы сразу при загрузке", l.AutoAtlas);
                bool city = EditorGUILayout.Toggle("Шейдер города (тёплый)", l.UseCityShader);
                bool awin = EditorGUILayout.Toggle("Окна нарисованы в атласе", l.AtlasWindows);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(g, "Застройка — папка");
                    if (folder != null && !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder))) folder = null;
                    l.SourceFolder = folder; l.IncludeSubfolders = sub; l.LoadProps = props; l.AutoAtlas = atlas; l.UseCityShader = city; l.AtlasWindows = awin;
                    Dirty(g);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = l.SourceFolder != null;
                    if (GUILayout.Button("Загрузить из папки (заменить список домов)"))
                    {
                        string path = AssetDatabase.GetAssetPath(l.SourceFolder);
                        Load(g, l, layer1, new List<string> { path }, null, true);
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button("Атласы", GUILayout.Width(70))) { CityAtlasTool.Apply(g, l, true); EnsureNightBridge(g); Dirty(g); }
                    if (GUILayout.Button("Очистить дома", GUILayout.Width(110)))
                    {
                        Undo.RecordObject(g, "Застройка — очистить"); l.Buildings.Clear(); Dirty(g);
                    }
                }
                // бросить мышью: модели и/или папки → добавить в дома (выделение в Project не меняется)
                Rect drop = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
                GUI.Box(drop, "Перетащи сюда модели или папки из Project → добавить в дома", EditorStyles.helpBox);
                Event e = Event.current;
                if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && drop.Contains(e.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (e.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        List<string> folders = new List<string>();
                        List<GameObject> models = new List<GameObject>();
                        foreach (Object o in DragAndDrop.objectReferences)
                        {
                            string p = AssetDatabase.GetAssetPath(o);
                            if (string.IsNullOrEmpty(p)) continue;
                            if (AssetDatabase.IsValidFolder(p)) folders.Add(p);
                            else if (o is GameObject) models.Add((GameObject)o);
                        }
                        Load(g, l, layer1, folders, models, false);
                    }
                    e.Use();
                }
            }
        }

        /// <summary>
        /// Загрузить модели в слой. Папки — со всеми подпапками (если включено). Без «И пропсы» пропускаются папки
        /// props / street props / street tile и мелочь (меньше дома по габариту). С пропсами: props — на крыши,
        /// street props — тротуар (car/truck — машины). replace — заменить список домов, иначе добавить (без повторов).
        /// </summary>
        private static void Load(CityBlockGenerator g, CityLayer l, bool layer1, List<string> folders, List<GameObject> models, bool replace)
        {
            Undo.RecordObject(g, "Застройка — загрузить");
            if (replace) l.Buildings.Clear();
            if (l.LoadProps && replace) { l.RoofProps.Clear(); l.StreetProps.Clear(); l.Cars.Clear(); }
            List<string> paths = new List<string>();
            if (folders != null && folders.Count > 0)
            {
                string[] guids = AssetDatabase.FindAssets("t:GameObject", folders.ToArray());
                foreach (string guid in guids)
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!l.IncludeSubfolders)
                    {
                        string dir = System.IO.Path.GetDirectoryName(p).Replace('\\', '/');
                        bool direct = false;
                        foreach (string f in folders) if (dir == f.TrimEnd('/')) { direct = true; break; }
                        if (!direct) continue;
                    }
                    if (!paths.Contains(p)) paths.Add(p);
                }
            }
            if (models != null)
                foreach (GameObject m in models)
                {
                    string p = AssetDatabase.GetAssetPath(m);
                    if (!string.IsNullOrEmpty(p) && !paths.Contains(p)) paths.Add(p);
                }

            Vector2 ms = g.ModuleSize();
            float s = Mathf.Min(ms.x, ms.y);
            int b = 0, rp = 0, sp = 0, cars = 0, skipped = 0, dup = 0;
            foreach (string path in paths)
            {
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                string lp = path.ToLowerInvariant().Replace('\\', '/');
                string nm = System.IO.Path.GetFileNameWithoutExtension(lp);
                bool isTile = lp.Contains("/street tile/") || nm.Contains("street tile") || nm.Contains("street_tile");
                bool isStreet = lp.Contains("/street props/") || lp.Contains("/street_props/");
                string dirl = System.IO.Path.GetDirectoryName(lp).Replace('\\', '/');
                bool isRoof = !isStreet && (lp.Contains("/props/") || dirl.Contains("prop") || dirl.Contains("проп"));
                if (isTile) { skipped++; continue; }
                if (isStreet || isRoof)
                {
                    if (!l.LoadProps) { skipped++; continue; }
                    if (isStreet)
                    {
                        if (nm.StartsWith("car") || nm.Contains("truck")) { if (!l.Cars.Contains(go)) { l.Cars.Add(go); cars++; } }
                        else if (nm.Contains("traficlight") || nm.Contains("roadconus") || nm.Contains("roadsign")) skipped++;
                        else if (!l.StreetProps.Contains(go)) { l.StreetProps.Add(go); sp++; }
                    }
                    else
                    {
                        if (nm.Contains("window cleaning") || nm.Contains("ladder")) skipped++;
                        else if (!l.RoofProps.Contains(go)) { l.RoofProps.Add(go); rp++; }
                    }
                    continue;
                }
                // дом? мелочь (машина, урна, фонарь…) не берём
                Bounds bb;
                if (!TileGridGenerator.MeasurePrefab(go, out bb)) { skipped++; continue; }
                if (Mathf.Max(bb.size.x, bb.size.z) < 0.035f * s || bb.size.y < 0.05f * s) { skipped++; continue; }
                bool has = false;
                foreach (BuildingEntry e in l.Buildings) if (e != null && e.Prefab == go) { has = true; break; }
                if (has) { dup++; continue; }
                l.Buildings.Add(new BuildingEntry { Prefab = go, Weight = 1f });
                b++;
            }
            if (!layer1 && l.LoadProps && l.RoofProps.Count == 0 && l.StreetProps.Count == 0)
            {
                l.RoofProps.AddRange(g.Layer1.RoofProps); l.StreetProps.AddRange(g.Layer1.StreetProps); l.Cars.AddRange(g.Layer1.Cars);
            }
            Dirty(g);
            string from = (folders != null && folders.Count > 0 ? string.Join(", ", folders.ToArray()) : "") + (models != null && models.Count > 0 ? (folders != null && folders.Count > 0 ? " + " : "") + models.Count + " моделей" : "");
            Debug.Log($"[Застройка] «{l.Name}» ← {from}: домов +{b} (всего {l.Buildings.Count})" + (l.LoadProps ? $", на крыши +{rp}, тротуар +{sp}, машин +{cars}" : "") +
                      (skipped > 0 ? $", пропущено {skipped} (пропсы/плитка/мелочь)" : "") + (dup > 0 ? $", уже были {dup}" : ""), g);
            if (l.AutoAtlas && b + rp + sp + cars > 0) { CityAtlasTool.Apply(g, l, true); EnsureNightBridge(g); }
            if (b == 0 && paths.Count == 0)
                EditorUtility.DisplayDialog("Застройка", "Моделей не нашлось. Проверь папку (FBX или префабы внутри).", "Ок");
        }

        // ───────────────────────── МЕНЮ ─────────────────────────

        [MenuItem("CatCooks/Застройка/Окна ночью (мост к Day Cycle)", priority = 911)]
        public static void CreateAtmosphere()
        {
            CityAtmosphere at = Object.FindFirstObjectByType<CityAtmosphere>(FindObjectsInactive.Include);
            if (at == null)
            {
                CityBlockGenerator city = Object.FindFirstObjectByType<CityBlockGenerator>(FindObjectsInactive.Include);
                if (city == null) { EditorUtility.DisplayDialog("Окна ночью", "Сначала создай генератор застройки (объект City).", "Ок"); return; }
                at = EnsureNightBridge(city);
            }
            Selection.activeGameObject = at.gameObject;
        }

        /// <summary>Мост «Окна ночью» — компонентом на объекте City (новых объектов не создаём).</summary>
        public static CityAtmosphere EnsureNightBridge(CityBlockGenerator city)
        {
            CityAtmosphere at = Object.FindFirstObjectByType<CityAtmosphere>(FindObjectsInactive.Include);
            if (at != null) return at;
            at = Undo.AddComponent<CityAtmosphere>(city.gameObject);
            at.DayCycle = Object.FindFirstObjectByType<CatCooksDayCycle>(FindObjectsInactive.Include);
            if (!UnityEditor.EditorApplication.isPlaying && city.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(city.gameObject.scene);
            Debug.Log(at.DayCycle != null
                ? $"[Застройка] на «{city.name}» добавлен мост «Окна ночью» → Day Cycle «{at.DayCycle.name}»: окна зажгутся, когда стемнеет"
                : $"[Застройка] на «{city.name}» добавлен мост «Окна ночью», но Day Cycle не нашёлся — пока по ползунку Night", city);
            return at;
        }

        [MenuItem("CatCooks/Застройка/Дальние пояса 3 и 4 (кольцо модулей + стена высоток)", priority = 912)]
        public static void CreateFar()
        {
            CityBlockGenerator city = Object.FindFirstObjectByType<CityBlockGenerator>(FindObjectsInactive.Include);
            if (city == null) { EditorUtility.DisplayDialog("Дальние пояса", "Сначала создай генератор застройки (объект City).", "Ок"); return; }
            CityFarBands f = city.GetComponent<CityFarBands>();
            if (f == null) { f = Undo.AddComponent<CityFarBands>(city.gameObject); f.City = city; }
            Selection.activeGameObject = city.gameObject;
            CityFarBuilder.Build(f);
        }

        [MenuItem("CatCooks/Застройка/Создать генератор застройки (рядом с сеткой плиток)", priority = 910)]
        public static void Create()
        {
            TileGridGenerator grid = null;
            if (Selection.activeGameObject != null) grid = Selection.activeGameObject.GetComponentInParent<TileGridGenerator>();
            if (grid == null) grid = Object.FindFirstObjectByType<TileGridGenerator>();
            GameObject go = new GameObject("City");
            Undo.RegisterCreatedObjectUndo(go, "Застройка");
            if (grid != null) { go.transform.SetPositionAndRotation(grid.transform.position, grid.transform.rotation); }
            CityBlockGenerator c = go.AddComponent<CityBlockGenerator>();
            c.Grid = grid;
            c.ResetRoles();
            Selection.activeGameObject = go;
            if (!UnityEditor.EditorApplication.isPlaying && go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log(grid != null
                ? $"[Застройка] создан «City» для сетки «{grid.name}» — перетащи папку 1layer в «Папка с домами» слоя 1, 2layer — в слой 2, нажми «Загрузить», потом «Застроить всё»"
                : "[Застройка] создан «City», но сетки плиток в сцене нет — укажи Grid вручную", go);
        }
    }
}
