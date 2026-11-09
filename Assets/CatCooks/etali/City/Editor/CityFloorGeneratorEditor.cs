using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CatCooks.Level
{
    /// <summary>
    /// Кнопки генератора рабочего этажа + сама сборка (только в редакторе: модули ставятся как распакованные копии FBX,
    /// лишние части — внутренние стены, углы, недостроенные балки/плиты — удаляются).
    /// </summary>
    [CustomEditor(typeof(CityFloorGenerator))]
    public class CityFloorGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CityFloorGenerator g = (CityFloorGenerator)target;
            EditorGUILayout.HelpBox("Позиция генератора = пол игрока (верх плиты подвала). Этаж 1 — подвал под ним, этаж 2 — каркас на уровне игрока.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Сгенерировать", GUILayout.Height(28))) Build(g);
                if (GUILayout.Button("Новый сид + сгенерировать", GUILayout.Height(28)))
                {
                    Undo.RecordObject(g, "Сид");
                    g.Seed = Random.Range(1, 99999);
                    Build(g);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Очистить")) Clear(g);
                if (GUILayout.Button("Подставить модули и замерить")) { AutoAssign(g); g.Measure(); g.MakePlan(); SceneView.RepaintAll(); }
            }
            if (GUILayout.Button("Фейк-этажи под домом: снять фасады подвала и вырастить", GUILayout.Height(24))) MakeFakes(g);
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck()) { g.MakePlan(); SceneView.RepaintAll(); }
        }

        [MenuItem("CatCooks/Застройка/Этаж: поставить генератор рабочего этажа", priority = 960)]
        public static void Place()
        {
            CityFloorGenerator g = Object.FindFirstObjectByType<CityFloorGenerator>();
            if (g == null)
            {
                GameObject go = new GameObject("City Floor Generator");
                Undo.RegisterCreatedObjectUndo(go, "Этаж");
                SceneView sv = SceneView.lastActiveSceneView;
                if (sv != null) go.transform.position = sv.pivot;
                g = go.AddComponent<CityFloorGenerator>();
            }
            AutoAssign(g);
            g.Measure();
            g.MakePlan();
            Selection.activeObject = g.gameObject;
            Debug.Log("[Этаж] генератор на месте. Поставь его на уровень пола игрока и нажми «Сгенерировать» в инспекторе.", g);
        }

        public static void AutoAssign(CityFloorGenerator g)
        {
            Undo.RecordObject(g, "Модули");
            if (g.BulkModule == null) g.BulkModule = ByGuid(CityFloorGenerator.GuidBulk);
            if (g.GlassModule == null) g.GlassModule = ByGuid(CityFloorGenerator.GuidGlass);
            if (g.BrickModule == null) g.BrickModule = ByGuid(CityFloorGenerator.GuidBrick);
            EditorUtility.SetDirty(g);
            if (g.BulkModule == null) Debug.LogWarning("[Этаж] не нашёл балочный модуль (full bulk module) — укажи его в поле Bulk Module.", g);
        }

        private static GameObject ByGuid(string guid)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(p);
        }

        public static void Clear(CityFloorGenerator g)
        {
            Transform old = g.transform.Find(CityFloorGenerator.RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        // стороны клетки: 0 +X, 1 −X, 2 +Z, 3 −Z
        private static readonly int[] DX = { 1, -1, 0, 0 };
        private static readonly int[] DZ = { 0, 0, 1, -1 };

        public static void Build(CityFloorGenerator g)
        {
            AutoAssign(g);
            if (g.BulkModule == null) return;
            g.Measure();
            g.MakePlan();
            Clear(g);
            GameObject root = new GameObject(CityFloorGenerator.RootName);
            Undo.RegisterCreatedObjectUndo(root, "Этаж");
            root.transform.SetParent(g.transform, false);
            System.Random rnd = new System.Random(g.Seed * 7919 + 13);
            Transform f1 = Group(root.transform, "Floor1_Basement");
            Transform f2 = Group(root.transform, "Floor2_Frame");
            int walls1 = 0, walls2 = 0, ceil2 = 0, upper = 0, cells = 0;

            // ── этаж 1: балочный модуль в каждой клетке + стены по внешнему краю ──
            int[,,] wallType = PlanWalls(g, rnd);          // 0 нет, 1 стекло, 2 кирпич
            int blankSide = rnd.Next(4);                   // глухая кирпичная стена (без окон) — только с этой стороны дома
            if (g.ApplyMaterials) EnsureMaterials(g);
            float glassUp = g.ResolvedStyle() == CityFloorGenerator.WallStyle.BrickAndBeams ? 0f : (g.ResolvedStyle() == CityFloorGenerator.WallStyle.GlassAndBeams ? 1f : 0.8f);
            bool[,] occ = new bool[g.PlanW, g.PlanD];
            for (int x = 0; x < g.PlanW; x++) for (int z = 0; z < g.PlanD; z++) occ[x, z] = g.Has(x, z);
            for (int x = 0; x < g.PlanW; x++)
                for (int z = 0; z < g.PlanD; z++)
                {
                    if (!g.Has(x, z)) continue;
                    cells++;
                    Vector3 c = g.CellPos(x, z, 0);
                    GameObject b = Spawn(g.BulkModule, f1, c, 0, g.ModuleScale, "Cell_" + x + "_" + z);
                    Dedupe(g, b, c, x, z, occ);
                    if (g.ApplyMaterials) Paint(b, g.BulkMaterial, null, rnd);
                    FinishSlabs(g, b);
                    for (int s = 0; s < 4; s++)
                    {
                        int t = wallType[x, z, s];
                        if (t == 0) continue;
                        GameObject src = t == 1 ? g.GlassModule : g.BrickModule;
                        if (src == null) continue;
                        bool corners = s < 2;                                      // углы ставят стены ±X
                        GameObject w = Spawn(src, f1, c, rnd.Next(4), g.ModuleScale, "Wall_" + x + "_" + z + "_" + SideName(s));
                        if (t == 2) OrientBrick(g, w, src, c, s, s != blankSide);           // окна — наружу; глухая — только с одной стороны
                        KeepSide(g, w, c, s, corners ? CornerMask(g, x, z, s) : 0);
                        if (g.ApplyMaterials) Paint(w, t == 1 ? g.GlassMaterial : g.BrickMaterial, g.WindowMaterial, rnd);
                        if (g.AddColliders) AddBoxes(w);
                        walls1++;
                    }
                }

            // ── этаж 2: каркас «в процессе»: колонны везде, балки местами, плита кусками, куски стен по краю ──
            for (int x = 0; x < g.PlanW; x++)
                for (int z = 0; z < g.PlanD; z++)
                {
                    if (!g.Has(x, z)) continue;
                    Vector3 c = g.CellPos(x, z, 1);
                    bool beams = rnd.NextDouble() < g.BeamShare;
                    bool ceiling = beams && rnd.NextDouble() < g.CeilingShare;
                    GameObject b = Spawn(g.BulkModule, f2, c, 0, g.ModuleScale, "Frame_" + x + "_" + z);
                    KeepBulkParts(b, true, beams, ceiling);
                    Dedupe(g, b, c, x, z, occ);
                    if (g.ApplyMaterials) Paint(b, g.BulkMaterial, null, rnd);
                    if (ceiling) { FinishSlabs(g, b); ceil2++; }
                    for (int s = 0; s < 4; s++)
                    {
                        if (g.Has(x + DX[s], z + DZ[s])) continue;
                        if (rnd.NextDouble() >= g.WallPieceShare) continue;
                        bool glass = rnd.NextDouble() < glassUp;
                        GameObject src = glass ? g.GlassModule : g.BrickModule;
                        if (src == null) continue;
                        GameObject w = Spawn(src, f2, c, rnd.Next(4), g.ModuleScale, "WallPiece_" + x + "_" + z + "_" + SideName(s));
                        if (!glass) OrientBrick(g, w, src, c, s, s != blankSide);
                        KeepSide(g, w, c, s, 0);
                        if (g.ApplyMaterials) Paint(w, glass ? g.GlassMaterial : g.BrickMaterial, g.WindowMaterial, rnd);
                        if (g.AddColliders) AddBoxes(w);
                        walls2++;
                    }
                }

            // ── этажи 3+: редкие колонны и балки ──
            for (int k = 0; k < g.UpperFloors; k++)
            {
                Transform fk = Group(root.transform, "Floor" + (k + 3) + "_Sparse");
                float share = g.UpperShare * Mathf.Pow(0.6f, k);
                bool[,] occK = new bool[g.PlanW, g.PlanD];                         // сначала — какие клетки, потом ставим (чтобы стыки не дублировались)
                for (int x = 0; x < g.PlanW; x++) for (int z = 0; z < g.PlanD; z++) occK[x, z] = g.Has(x, z) && rnd.NextDouble() < share;
                for (int x = 0; x < g.PlanW; x++)
                    for (int z = 0; z < g.PlanD; z++)
                    {
                        if (!occK[x, z]) continue;
                        Vector3 ck = g.CellPos(x, z, k + 2);
                        GameObject b = Spawn(g.BulkModule, fk, ck, 0, g.ModuleScale, "Frame_" + x + "_" + z);
                        KeepBulkParts(b, true, rnd.NextDouble() < 0.5, false);
                        Dedupe(g, b, ck, x, z, occK);
                        if (g.ApplyMaterials) Paint(b, g.BulkMaterial, null, rnd);
                        upper++;
                    }
            }
            Selection.activeObject = g.gameObject;
            if (g.Log) Debug.Log("[Этаж] собрано: " + g.PlanW + "×" + g.PlanD + " (" + cells + " клеток" + (cells < g.PlanW * g.PlanD ? ", буквой Г" : "") + ", шаг " + g.Pitch.ToString("0.##") + "). " +
                                 "Дом: " + StyleName(g.ResolvedStyle()) + ". Подвал: стен по краю " + walls1 + "; каркас: перекрытий " + ceil2 + ", кусков стен " + walls2 + (g.UpperFloors > 0 ? "; выше — модулей " + upper : "") + ". Сид " + g.Seed + ".", g);
        }

        private static Transform Group(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static string SideName(int s) { return s == 0 ? "PX" : s == 1 ? "NX" : s == 2 ? "PZ" : "NZ"; }

        private static GameObject Spawn(GameObject src, Transform parent, Vector3 pos, int rot90, float scale, string name)
        {
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            if (go == null) { go = Object.Instantiate(src, parent); }
            if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, rot90 * 90f, 0f) * src.transform.rotation;
            go.transform.localScale = Vector3.Scale(src.transform.localScale, Vector3.one * scale);
            return go;
        }

        /// <summary>Типы стен по краю: прямые отрезки одного типа длиной WallRun клеток.</summary>
        private static int[,,] PlanWalls(CityFloorGenerator g, System.Random rnd)
        {
            int[,,] t = new int[g.PlanW, g.PlanD, 4];
            int runLeft = 0, cur = 1;
            // стороны ±X: идём вдоль Z
            for (int s = 0; s < 2; s++)
                for (int x = 0; x < g.PlanW; x++)
                {
                    runLeft = 0;
                    for (int z = 0; z < g.PlanD; z++)
                    {
                        if (!g.Has(x, z) || g.Has(x + DX[s], z)) { runLeft = 0; continue; }
                        if (runLeft <= 0) { cur = rnd.NextDouble() < g.GlassShareNow() ? 1 : 2; runLeft = rnd.Next(Mathf.Max(1, g.WallRun.x), Mathf.Max(g.WallRun.x, g.WallRun.y) + 1); }
                        t[x, z, s] = cur; runLeft--;
                    }
                }
            // стороны ±Z: идём вдоль X
            for (int s = 2; s < 4; s++)
                for (int z = 0; z < g.PlanD; z++)
                {
                    runLeft = 0;
                    for (int x = 0; x < g.PlanW; x++)
                    {
                        if (!g.Has(x, z) || g.Has(x, z + DZ[s])) { runLeft = 0; continue; }
                        if (runLeft <= 0) { cur = rnd.NextDouble() < g.GlassShareNow() ? 1 : 2; runLeft = rnd.Next(Mathf.Max(1, g.WallRun.x), Mathf.Max(g.WallRun.x, g.WallRun.y) + 1); }
                        t[x, z, s] = cur; runLeft--;
                    }
                }
            return t;
        }

        /// <summary>Какие углы оставить у стены ±X: бит 1 — со стороны −Z, бит 2 — со стороны +Z (если там тоже край — наружный угол).</summary>
        private static int CornerMask(CityFloorGenerator g, int x, int z, int s)
        {
            int m = 0;
            if (!g.Has(x, z - 1)) m |= 1;
            if (!g.Has(x, z + 1)) m |= 2;
            return m;
        }

        /// <summary>Оставить в модуле стен только сторону s (+ углы по маске), остальное удалить.</summary>
        private static void KeepSide(CityFloorGenerator g, GameObject mod, Vector3 c, int s, int cornerMask)
        {
            float half = g.Pitch * 0.5f;
            List<GameObject> kill = new List<GameObject>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = r.bounds;
                float nx = (b.center.x - c.x) / half, nz = (b.center.z - c.z) / half;
                bool cx = Mathf.Abs(nx) > 0.85f, cz = Mathf.Abs(nz) > 0.85f;
                bool keep = false;
                if (cx && cz)
                {
                    // угол: только у стены ±X, на нашей стороне, если там наружный угол
                    bool ourX = (s == 0 && nx > 0f) || (s == 1 && nx < 0f);
                    if (s < 2 && ourX) keep = (nz < 0f && (cornerMask & 1) != 0) || (nz > 0f && (cornerMask & 2) != 0);
                }
                else if (cx && !cz) keep = (s == 0 && nx > 0f) || (s == 1 && nx < 0f);
                else if (cz && !cx) keep = (s == 2 && nz > 0f) || (s == 3 && nz < 0f);
                if (!keep) kill.Add(r.gameObject);
            }
            foreach (GameObject k in kill) if (k != null && k != mod) Object.DestroyImmediate(k);
        }

        private static bool Occ(bool[,] o, int x, int z) { return x >= 0 && z >= 0 && x < o.GetLength(0) && z < o.GetLength(1) && o[x, z]; }

        /// <summary>
        /// Стыки соседних балочных модулей без дублей: у модуля колонны и балки по всем 4 краям, у соседей они совпадают.
        /// Угол (колонна, пластина) остаётся только у одной клетки из соседних (первой по порядку), край (балка) — у клетки
        /// со стороны «+», а со стороны «−» — только если там соседа нет (край дома). Плита ужимается ровно в шаг сетки,
        /// чтобы плиты соседей не налезали друг на друга (не мерцали).
        /// </summary>
        private static void Dedupe(CityFloorGenerator g, GameObject mod, Vector3 c, int x, int z, bool[,] occ)
        {
            float half = g.Pitch * 0.5f;
            List<GameObject> kill = new List<GameObject>();
            List<Renderer> slabs = new List<Renderer>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                if (n.Contains("beton")) { slabs.Add(r); continue; }
                Bounds b = r.bounds;
                float nx = (b.center.x - c.x) / half, nz = (b.center.z - c.z) / half;
                bool ex = Mathf.Abs(nx) > 0.6f, ez = Mathf.Abs(nz) > 0.6f;
                int sx = nx > 0f ? 1 : -1, sz = nz > 0f ? 1 : -1;
                bool keep = true;
                if (ex && ez)
                {
                    // угол: соседние клетки по этому углу; хозяин — первая существующая по порядку
                    int kx = x + (sx > 0 ? 1 : 0), kz = z + (sz > 0 ? 1 : 0);
                    int ox = -1, oz = -1;
                    int[] cxs = { kx - 1, kx, kx - 1, kx }, czs = { kz - 1, kz - 1, kz, kz };
                    for (int i = 0; i < 4; i++) if (Occ(occ, cxs[i], czs[i])) { ox = cxs[i]; oz = czs[i]; break; }
                    keep = ox == x && oz == z;
                }
                else if (ex) keep = sx > 0 || !Occ(occ, x - 1, z);                 // балка по краю X
                else if (ez) keep = sz > 0 || !Occ(occ, x, z - 1);                 // балка по краю Z
                if (!keep) kill.Add(r.gameObject);
            }
            foreach (GameObject k in kill) if (k != null && k != mod) Object.DestroyImmediate(k);
            // плита — ровно в клетку
            if (slabs.Count == 0) return;
            Bounds sb = slabs[0].bounds;
            foreach (Renderer r in slabs) sb.Encapsulate(r.bounds);
            float fx = sb.size.x > g.Pitch * 1.005f ? g.Pitch / sb.size.x : 1f, fz = sb.size.z > g.Pitch * 1.005f ? g.Pitch / sb.size.z : 1f;
            if (fx >= 1f && fz >= 1f) return;
            GameObject fit = new GameObject("SlabFit");
            fit.transform.SetParent(mod.transform, true);
            fit.transform.position = new Vector3(c.x, sb.center.y, c.z);
            fit.transform.rotation = Quaternion.identity;
            fit.transform.localScale = Vector3.one;
            foreach (Renderer r in slabs) r.transform.SetParent(fit.transform, true);
            fit.transform.localScale = new Vector3(fit.transform.localScale.x * fx, fit.transform.localScale.y, fit.transform.localScale.z * fz);
        }

        /// <summary>Части балочного модуля: колонны / балки / плита (+ профиль и коннекторы).</summary>
        private static void KeepBulkParts(GameObject mod, bool columns, bool beams, bool slab)
        {
            List<GameObject> kill = new List<GameObject>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                bool keep;
                if (n.Contains("vertical")) keep = columns;
                else if (n.Contains("horizontal")) keep = beams;
                else if (n.Contains("beton") || n.Contains("profile") || n.Contains("floor")) keep = slab;
                else if (n.Contains("connector") || n.Contains("plate")) keep = slab;
                else
                {
                    // неизвестная часть — по форме: высокая тонкая — колонна, плоская широкая — плита, остальное — балка
                    Bounds b = r.bounds;
                    if (b.size.y > Mathf.Max(b.size.x, b.size.z)) keep = columns;
                    else if (b.size.y < 0.05f * Mathf.Max(b.size.x, b.size.z)) keep = slab;
                    else keep = beams;
                }
                if (!keep) kill.Add(r.gameObject);
            }
            foreach (GameObject k in kill) if (k != null && k != mod) Object.DestroyImmediate(k);
        }

        /// <summary>Плита пола: коллайдер и метка «мокнет / засыпает снегом».</summary>
        private static void FinishSlabs(CityFloorGenerator g, GameObject mod)
        {
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                if (!n.Contains("beton")) continue;
                if (g.AddColliders && r.GetComponent<Collider>() == null) r.gameObject.AddComponent<BoxCollider>();
                if (g.MarkWet && r.GetComponent<CityWetSurface>() == null)
                {
                    CityWetSurface w = r.gameObject.AddComponent<CityWetSurface>();
                    w.IncludeChildren = false;
                }
            }
        }

        /// <summary>Коллайдеры стен по габариту мешей (окна-стёкла пропускаем — они внутри стены).</summary>
        private static void AddBoxes(GameObject mod)
        {
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponent<Collider>() != null) continue;
                if (IsPane(r.gameObject.name)) continue;
                r.gameObject.AddComponent<BoxCollider>();
            }
        }

        private static string StyleName(CityFloorGenerator.WallStyle st)
        {
            return st == CityFloorGenerator.WallStyle.BrickAndBeams ? "кирпич + балки" : st == CityFloorGenerator.WallStyle.GlassAndBeams ? "стекло + балки" : "вперемешку";
        }

        public const string MatFolder = "Assets/Generated/City/Floor";

        /// <summary>Материалы этажа: мультяшный шейдер города с атласами модулей, окна — шейдер окон города.</summary>
        public static void EnsureMaterials(CityFloorGenerator g)
        {
            MakeFolder(MatFolder);
            Undo.RecordObject(g, "Материалы");
            if (g.BulkMaterial == null) g.BulkMaterial = ToonMat("CC_Floor_Bulk", Tex(CityFloorGenerator.GuidTexBulk, "bulk_module_AlbedoTransparency"));
            if (g.BrickMaterial == null) g.BrickMaterial = ToonMat("CC_Floor_Brick", Tex(CityFloorGenerator.GuidTexBrick, "brick module_AlbedoTransparency"));
            if (g.GlassMaterial == null) g.GlassMaterial = ToonMat("CC_Floor_GlassFrame", Tex(CityFloorGenerator.GuidTexGlass, "glass_module_AlbedoTransparency"));
            if (g.WindowMaterial == null)
            {
                string path = MatFolder + "/CC_Floor_Window.mat";
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                Shader sh = Shader.Find("CatCooks/City/Window Glass");
                if (m == null && sh != null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                g.WindowMaterial = m;
            }
            EditorUtility.SetDirty(g);
            AssetDatabase.SaveAssets();
        }

        private static Texture2D Tex(string guid, string search)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D t = string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t != null) return t;
            foreach (string gd in AssetDatabase.FindAssets(search + " t:Texture2D"))
            {
                t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(gd));
                if (t != null) return t;
            }
            Debug.LogWarning("[Этаж] не нашёл атлас «" + search + "» — материал будет без текстуры.");
            return null;
        }

        private static Material ToonMat(string name, Texture2D tex)
        {
            string path = MatFolder + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader sh = Shader.Find("CatCooks/City/Toon");
            if (sh == null) { Debug.LogError("[Этаж] шейдер CatCooks/City/Toon не найден"); return m; }
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            if (tex != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Покрасить модуль: всё — материалом frame; стёкла окон (window*) — шейдером окон города.</summary>
        private static void Paint(GameObject mod, Material frame, Material window, System.Random rnd)
        {
            List<Renderer> wins = new List<Renderer>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                if (IsPane(r.gameObject.name)) { wins.Add(r); continue; }
                if (frame == null) continue;
                Material[] ms = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < ms.Length; i++) ms[i] = frame;
                r.sharedMaterials = ms;
            }
            if (window == null) return;
            // стёкла окон — свой объект в модели: ставим шейдер окон города и размечаем стекло как ОДНО окно
            // (копия меша с uv3/uv4 — тогда шейдер не рисует свою сетку «арок» поверх, а ночью окно горит целиком)
            foreach (Renderer r in wins)
            {
                Material[] ms = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < ms.Length; i++) ms[i] = window;
                r.sharedMaterials = ms;
                MarkPane(r.GetComponent<MeshFilter>(), rnd);
            }
        }

        /// <summary>Повернуть кирпичный модуль так, чтобы на стороне s оказалась стена с окном (или глухая).</summary>
        private static void OrientBrick(CityFloorGenerator g, GameObject mod, GameObject src, Vector3 c, int s, bool wantWindow)
        {
            float half = g.Pitch * 0.5f;
            for (int k = 0; k < 4; k++)
            {
                mod.transform.rotation = Quaternion.Euler(0f, k * 90f, 0f) * src.transform.rotation;
                foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
                {
                    string n = r.gameObject.name.ToLowerInvariant();
                    if (!n.Contains("wall")) continue;                                   // только сами стены (не окна, не углы)
                    Bounds b = r.bounds;
                    float nx = (b.center.x - c.x) / half, nz = (b.center.z - c.z) / half;
                    bool cx = Mathf.Abs(nx) > 0.85f, cz = Mathf.Abs(nz) > 0.85f;
                    bool onSide = (cx && !cz && ((s == 0 && nx > 0f) || (s == 1 && nx < 0f))) || (cz && !cx && ((s == 2 && nz > 0f) || (s == 3 && nz < 0f)));
                    if (!onSide) continue;
                    if (n.Contains("window") == wantWindow) return;                      // нашли нужный поворот
                }
            }
        }

        /// <summary>Разметка стекла как одного окна: копия меша + uv3 (внутри окна) и uv4 (случайное, ширина, тип 3, высота).</summary>
        private static void MarkPane(MeshFilter mf, System.Random rnd)
        {
            if (mf == null || mf.sharedMesh == null) return;
            Mesh src = mf.sharedMesh;
            Vector3[] v = src.vertices;
            if (v == null || v.Length == 0) return;                                      // меш не читается — останется сетка по миру
            Mesh m = Object.Instantiate(src);
            m.name = src.name + "_CCWindow";
            Bounds b = src.bounds;
            bool thinX = b.size.x <= b.size.z && b.size.x <= b.size.y;
            bool thinZ = !thinX && b.size.z <= b.size.y;
            float w = thinX ? b.size.z : b.size.x;
            float h = thinX || thinZ ? b.size.y : b.size.z;
            Vector2[] u3 = new Vector2[v.Length];
            List<Vector4> u4 = new List<Vector4>(v.Length);
            float rr = (float)rnd.NextDouble();
            for (int i = 0; i < v.Length; i++)
            {
                float a = thinX ? (v[i].z - b.min.z) / Mathf.Max(w, 1e-5f) : (v[i].x - b.min.x) / Mathf.Max(w, 1e-5f);
                float c = (thinX || thinZ) ? (v[i].y - b.min.y) / Mathf.Max(h, 1e-5f) : (v[i].z - b.min.z) / Mathf.Max(h, 1e-5f);
                u3[i] = new Vector2(Mathf.Lerp(0.1f, 0.9f, a), c * 0.97f);               // без вырезов «ушей» по углам
                u4.Add(new Vector4(rr, w, 3f, h));
            }
            m.uv3 = u3;
            m.SetUVs(3, u4);
            mf.sharedMesh = m;
        }

        /// <summary>Стекло окна (window2…), а не стена «outer brick wall with window».</summary>
        private static bool IsPane(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("window") && !n.Contains("wall");
        }

        // ══ ФЕЙК-ЭТАЖИ ══
        private static Vector3 SideN(int s) { return new Vector3(DX[s], 0f, DZ[s]); }

        private static int SideOf(Vector3 d)
        {
            for (int s = 0; s < 4; s++) if (Vector3.Dot(SideN(s), d) > 0.9f) return s;
            return 0;
        }

        /// <summary>Снять 4 фасада подвала (ортокамера, только подвал), склеить в атлас и построить стопку фейк-этажей.</summary>
        public static void MakeFakes(CityFloorGenerator g)
        {
            Transform root = g.transform.Find(CityFloorGenerator.RootName);
            Transform f1 = root != null ? root.Find("Floor1_Basement") : null;
            if (f1 == null) { Build(g); root = g.transform.Find(CityFloorGenerator.RootName); f1 = root != null ? root.Find("Floor1_Basement") : null; }
            if (f1 == null) { Debug.LogWarning("[Этаж] нет подвала — сначала «Сгенерировать».", g); return; }
            g.MakePlan();
            Renderer[] rs = f1.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds bb = rs[0].bounds;
            foreach (Renderer r in rs) bb.Encapsulate(r.bounds);
            float baseY = g.CellPos(0, 0, 0).y, H = g.Height;
            int ppu = Mathf.Max(4, g.FakePixelsPerUnit);

            // временно — подвал на свой слой, камера видит только его
            const int layer = 31;
            Dictionary<GameObject, int> old = new Dictionary<GameObject, int>();
            foreach (Transform t in f1.GetComponentsInChildren<Transform>(true)) { old[t.gameObject] = t.gameObject.layer; t.gameObject.layer = layer; }
            Texture2D[] sides = new Texture2D[4];
            float[] along = new float[4];
            try
            {
                for (int s = 0; s < 4; s++)
                {
                    Vector3 n = SideN(s);
                    along[s] = s < 2 ? bb.size.z : bb.size.x;
                    float depth = s < 2 ? bb.extents.x : bb.extents.z;
                    sides[s] = Capture(g, new Vector3(bb.center.x, baseY + H * 0.5f, bb.center.z), n, depth, along[s], H, ppu, layer);
                }
            }
            finally
            {
                foreach (KeyValuePair<GameObject, int> kv in old) if (kv.Key != null) kv.Key.layer = kv.Value;
            }

            // атлас: 4 строки (фасады +X, −X, +Z, −Z), отступы между строками
            int W = 1, rowH = sides[0].height + 4;
            int[] sw = new int[4];
            for (int s = 0; s < 4; s++) { sw[s] = sides[s].width; W = Mathf.Max(W, sw[s]); }
            int AH = rowH * 4;
            Texture2D atlas = new Texture2D(W, AH, TextureFormat.RGBA32, false);
            Color32[] fill = new Color32[W * AH];
            Color32 bg = g.FakeBackground;
            for (int i = 0; i < fill.Length; i++) fill[i] = bg;
            atlas.SetPixels32(fill);
            for (int s = 0; s < 4; s++)
            {
                atlas.SetPixels(0, s * rowH + 2, sides[s].width, sides[s].height, sides[s].GetPixels());
                Object.DestroyImmediate(sides[s]);
            }
            CityFakeTone.Apply(atlas, bg);
            atlas.Apply();
            MakeFolder(MatFolder);
            string tag = g.gameObject.name.Replace(" ", "") + "_" + g.Seed + "_" + CityFakeTone.UniqueId(g);
            string texPath = MatFolder + "/CC_FakeFacade_" + tag + ".png";
            System.IO.File.WriteAllBytes(texPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            TextureImporter ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (ti != null) { ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = 4096; ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None; ti.SaveAndReimport(); }
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            Material mat = ToonMat("CC_FakeFloors_" + tag, tex);
            if (mat != null && mat.HasProperty("_Unlit")) { mat.SetFloat("_Unlit", g.FakeUnlit); EditorUtility.SetDirty(mat); }

            // стопка: кольцо стен по контуру (по клеткам края), стены — на внешней грани подвала
            Transform ft = g.transform.Find("FakeFloors");
            if (ft == null)
            {
                GameObject go = new GameObject("FakeFloors");
                Undo.RegisterCreatedObjectUndo(go, "Фейк-этажи");
                go.transform.SetParent(g.transform, false);
                ft = go.transform;
            }
            ft.position = g.transform.position; ft.rotation = Quaternion.identity; ft.localScale = Vector3.one;
            CityFakeFloors ff = ft.GetComponent<CityFakeFloors>();
            if (ff == null) ff = Undo.AddComponent<CityFakeFloors>(ft.gameObject);
            Undo.RecordObject(ff, "Фейк-этажи");
            float P = g.Pitch;
            float[] gridFace = { -1e9f, -1e9f, -1e9f, -1e9f };
            for (int x = 0; x < g.PlanW; x++)
                for (int z = 0; z < g.PlanD; z++)
                    if (g.Has(x, z))
                        for (int s = 0; s < 4; s++) gridFace[s] = Mathf.Max(gridFace[s], Vector3.Dot(g.CellPos(x, z, 0) + SideN(s) * P * 0.5f, SideN(s)));
            float[] bbFace = { bb.max.x, -bb.min.x, bb.max.z, -bb.min.z };
            float[] wallOut = new float[4];
            for (int s = 0; s < 4; s++) wallOut[s] = Mathf.Max(0f, bbFace[s] - gridFace[s]);
            ff.Edges.Clear(); ff.EdgeUV.Clear(); ff.EdgeN.Clear();
            for (int x = 0; x < g.PlanW; x++)
                for (int z = 0; z < g.PlanD; z++)
                {
                    if (!g.Has(x, z)) continue;
                    for (int s = 0; s < 4; s++)
                    {
                        if (g.Has(x + DX[s], z + DZ[s])) continue;
                        Vector3 n = SideN(s);
                        Vector3 r = Vector3.Cross(Vector3.up, -n).normalized;                     // вправо, если смотреть снаружи
                        Vector3 c = g.CellPos(x, z, 0);
                        Vector3 ec = c + n * (P * 0.5f + wallOut[s]);
                        Vector3 a = ec - r * P * 0.5f, b = ec + r * P * 0.5f;
                        // наружный угол — продлить до соседней грани
                        int sl = SideOf(-r), sr = SideOf(r);
                        if (!g.Has(x + DX[sl], z + DZ[sl])) a -= r * wallOut[sl];
                        if (!g.Has(x + DX[sr], z + DZ[sr])) b += r * wallOut[sr];
                        float L = Mathf.Max(along[s], 1e-3f);
                        float ua = Vector3.Dot(a - bb.center, r) / L + 0.5f, ub = Vector3.Dot(b - bb.center, r) / L + 0.5f;
                        float k = (float)sw[s] / W;                                              // доля строки атласа, занятая этим фасадом
                        float v0 = (s * rowH + 2f + 0.5f) / AH, v1 = (s * rowH + 2f + (rowH - 4) - 0.5f) / AH;
                        Vector3 la = ft.InverseTransformPoint(a), lb = ft.InverseTransformPoint(b);
                        ff.Edges.Add(new Vector4(la.x, la.z, lb.x, lb.z));
                        ff.EdgeUV.Add(new Vector4(Mathf.Clamp01(ua) * k, Mathf.Clamp01(ub) * k, v0, v1));
                        ff.EdgeN.Add(n);
                    }
                }
            ff.TopY = baseY - ft.position.y;
            ff.FloorH = H;
            ff.Floors = g.FakeFloors;
            ff.Material = mat;
            EditorUtility.SetDirty(ff);
            ff.Rebuild();
            Debug.Log("[Этаж] фейк-этажи: " + ff.Floors + " под домом, стен в кольце " + ff.Edges.Count + ", атлас фасадов " + W + "×" + AH + " (" + texPath + "). Расти по прогрессу: CityFakeFloors.SetFloors(n) / Grow(1).", ff);
        }

        private static Texture2D Capture(CityFloorGenerator g, Vector3 center, Vector3 n, float depth, float along, float H, int ppu, int layer)
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(along * ppu), 16, 4096), h = Mathf.Clamp(Mathf.RoundToInt(H * ppu), 16, 2048);
            GameObject cg = new GameObject("CC_FacadeCam");
            cg.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = cg.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = H * 0.5f;
            cam.aspect = along / H;
            cg.transform.position = center + n * (depth + 2f);
            cg.transform.rotation = Quaternion.LookRotation(-n, Vector3.up);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = depth * 2f + 4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = g.FakeBackground;
            cam.cullingMask = 1 << layer;
            UniversalAdditionalCameraData ad = cam.GetUniversalAdditionalCameraData();
            if (ad != null) ad.renderPostProcessing = false;
            RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(cg);
            return tex;
        }

        private static void MakeFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
