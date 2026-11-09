using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Level
{
    /// <summary>
    /// ФОН: ДИАГНОСТИКА И СЛИЯНИЕ. Окно из трёх частей:
    ///  1) Диагностика выбранного объекта (или всей сцены): сколько рендереров, сколько разных пар меш+материал,
    ///     сколько теней, динамических, с тенями от мелочи, сколько частиц и ламп. Пишет в консоль по-русски.
    ///  2) Облегчение: статика для батчинга и GPU Resident Drawer, выключить тени у мелочи, пробы света/отражений
    ///     и векторы движения у фоновых рендереров.
    ///  3) Слияние: свести все меши под объектом по материалам и ячейкам сетки в немногие большие меши
    ///     (меши — ассеты в Assets/Generated/City/Merged; префабов не создаёт). Оригиналы остаются в сцене,
    ///     у них выключается только MeshRenderer (коллайдеры живы), «Снять слияние» всё возвращает.
    /// </summary>
    public class CityBackgroundTool : EditorWindow
    {
        private GameObject _root;
        private float _smallSize = 1.2f;
        private float _cell = 64f;
        private float _radius = 0f;
        private bool _smallNoShadow = true;
        private bool _hideLeaves = true;
        private Vector2 _scroll;
        private string _report = "";

        [MenuItem("CatCooks/Застройка/Фон: диагностика, облегчение, слияние…", priority = 965)]
        public static void Open()
        {
            CityBackgroundTool w = GetWindow<CityBackgroundTool>("Фон");
            w.minSize = new Vector2(460, 460);
        }

        private Renderer[] Collect()
        {
            if (_root != null) return _root.GetComponentsInChildren<Renderer>(false);
            return FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        }

        private void OnGUI()
        {
            _root = (GameObject)EditorGUILayout.ObjectField("Объект (пусто — вся сцена)", _root, typeof(GameObject), true);
            if (Selection.activeGameObject != null && GUILayout.Button("Взять выделенный объект")) _root = Selection.activeGameObject;

            EditorGUILayout.Space();
            if (GUILayout.Button("1. Диагностика", GUILayout.Height(26))) Diagnose();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. Облегчение", EditorStyles.boldLabel);
            _smallSize = EditorGUILayout.Slider("Мелочь — меньше, м", _smallSize, 0.2f, 4f);
            if (GUILayout.Button("Облегчить фоновые рендереры", GUILayout.Height(26))) Lighten();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3. Слияние мешей", EditorStyles.boldLabel);
            _cell = EditorGUILayout.Slider("Размер ячейки, м", _cell, 16f, 256f);
            _radius = EditorGUILayout.Slider("Радиус от объекта, м (0 — все)", _radius, 0f, 500f);
            _smallNoShadow = EditorGUILayout.ToggleLeft("Выключать тень у мелочи (порог из блока 2)", _smallNoShadow);
            _hideLeaves = EditorGUILayout.ToggleLeft("Выключать пустые объекты-оригиналы целиком (меньше нагрузки на иерархию)", _hideLeaves);
            if (GUILayout.Button("Слить выделенные объекты (можно несколько: пропсы, дома)", GUILayout.Height(30))) MergeButton();
            if (GUILayout.Button("Снять слияние у выделенных")) UnmergeButton();
            if (GUILayout.Button("Снять ВСЕ слияния в сцене (старые и новые)")) UnmergeAll();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("4. Подвижное", EditorStyles.boldLabel);
            if (GUILayout.Button("Починить подвижное: снять статику, вынуть из слияния (самолёты, машины, этажи…)", GUILayout.Height(26))) FixMovers();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Найти застывших летающих")) FindStrayFlyers(false);
            if (GUILayout.Button("Удалить застывших летающих")) FindStrayFlyers(true);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Слияние идёт по выделенным в иерархии объектам (или по полю «Объект» выше, если ничего не выделено). Не сливается: скрипты, Rigidbody, Animator, LODGroup, игровые (CityLevelRider), этажи и дома из генераторов.", MessageType.None);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_report, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        private static bool Skip(Renderer r)
        {
            return r is ParticleSystemRenderer || r is SkinnedMeshRenderer || r is TrailRenderer || r is LineRenderer;
        }

        private void Diagnose()
        {
            Renderer[] rs = Collect();
            HashSet<string> pairs = new HashSet<string>();
            int mesh = 0, stat = 0, dyn = 0, shadows = 0, smallShadows = 0, noInst = 0, skinned = 0, parts = 0, tris = 0;
            Dictionary<string, int> top = new Dictionary<string, int>();
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r is ParticleSystemRenderer) { parts++; continue; }
                if (r is SkinnedMeshRenderer) { skinned++; continue; }
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                mesh++;
                tris += mf.sharedMesh.triangles.Length / 3;
                Material[] ms = r.sharedMaterials;
                for (int k = 0; k < ms.Length; k++)
                {
                    pairs.Add(mf.sharedMesh.GetInstanceID() + "/" + (ms[k] != null ? ms[k].GetInstanceID() : 0));
                    if (ms[k] != null && !ms[k].enableInstancing) noInst++;
                }
                if (r.gameObject.isStatic) stat++; else dyn++;
                if (r.shadowCastingMode != ShadowCastingMode.Off)
                {
                    shadows++;
                    if (r.bounds.size.magnitude < _smallSize) smallShadows++;
                }
                Transform t = r.transform;
                while (t.parent != null && t.parent != (_root != null ? _root.transform : null) && t.parent.parent != null) t = t.parent;
                string key = t.name;
                int n; top.TryGetValue(key, out n); top[key] = n + 1;
            }
            Light[] lights = _root != null ? _root.GetComponentsInChildren<Light>(false) : FindObjectsByType<Light>(FindObjectsSortMode.None);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Рендереров с мешем: " + mesh + ", разных пар меш+материал: " + pairs.Count);
            sb.AppendLine("Треугольников суммарно: " + tris);
            sb.AppendLine("Статичных: " + stat + ", динамичных: " + dyn);
            sb.AppendLine("Бросают тень: " + shadows + " (из них мелочь: " + smallShadows + ")");
            sb.AppendLine("Материалов без включённого GPU Instancing: " + noInst);
            sb.AppendLine("Частиц: " + parts + ", скиннед-мешей: " + skinned + ", ламп: " + lights.Length);
            if (pairs.Count > 0) sb.AppendLine("Слияние может сократить примерно с " + mesh + " до порядка " + Mathf.Max(pairs.Count, mesh / 20) + " вызовов.");
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(top);
            list.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
            sb.AppendLine("Больше всего рендереров в группах:");
            for (int i = 0; i < list.Count && i < 12; i++) sb.AppendLine("  " + list[i].Key + " — " + list[i].Value);
            _report = sb.ToString();
            Debug.Log("[Фон] диагностика\n" + _report);
        }

        private void Lighten()
        {
            Renderer[] rs = Collect();
            int n = 0, sh = 0, st = 0;
            Undo.IncrementCurrentGroup();
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (Skip(r)) continue;
                Undo.RecordObject(r, "Облегчение фона");
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                r.allowOcclusionWhenDynamic = true;
                if (r.bounds.size.magnitude < _smallSize && r.shadowCastingMode != ShadowCastingMode.Off)
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    sh++;
                }
                MeshRenderer mrr = r as MeshRenderer;
                bool neg;
                if (mrr != null && !r.gameObject.isStatic && Why(mrr, _root != null ? _root.transform : r.transform.root, out neg) == null)
                {
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    st++;
                }
                EditorUtility.SetDirty(r);
                n++;
            }
            _report = "Облегчено рендереров: " + n + ". Тени выключены у мелочи: " + sh + ". Помечено статикой: " + st + ".\nНе трогал статику у подвижного и управляемого скриптами (Rigidbody, Animator, скрипты, игровые, этажи из генераторов).";
            Debug.Log("[Фон] " + _report);
        }

        // ───────────── слияние ─────────────
        // по именам типов, без ссылок на сборку с генераторами этажей (она может быть другой)
        private static string BlockedByType(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                MonoBehaviour[] mbs = t.GetComponents<MonoBehaviour>();
                for (int i = 0; i < mbs.Length; i++)
                {
                    if (mbs[i] == null) continue;
                    string n = mbs[i].GetType().Name;
                    if (n == "CityLevelRider") return "игровой (CityLevelRider)";
                    if (n == "CityFloorGenerator" || n == "CityModuleHouse" || n == "CityFakeFloors") return "этаж/дом из генератора (едет с этажами)";
                }
            }
            return null;
        }

        private static string Why(MeshRenderer r, Transform root, out bool negative)
        {
            negative = false;
            if (r.HasPropertyBlock()) return "свой MaterialPropertyBlock";
            if (r.GetComponentInParent<LODGroup>() != null) return "LODGroup";
            if (r.GetComponentInParent<Rigidbody>() != null) return "Rigidbody";
            if (r.GetComponentInParent<Animator>() != null) return "Animator";
            string byType = BlockedByType(r.transform);
            if (byType != null) return byType;
            Transform t = r.transform;
            while (t != null && t != root)
            {
                MonoBehaviour[] mbs = t.GetComponents<MonoBehaviour>();
                for (int i = 0; i < mbs.Length; i++) if (mbs[i] != null) return "есть скрипт";
                t = t.parent;
            }
            if (r.transform.localToWorldMatrix.determinant < 0f) { negative = true; return "отрицательный масштаб"; }
            return null;
        }

        private void MergeButton()
        {
            GameObject[] sel = Selection.gameObjects;
            List<GameObject> roots = new List<GameObject>();
            if (sel.Length > 0) roots.AddRange(sel); else if (_root != null) roots.Add(_root);
            if (roots.Count == 0) { _report = "Выдели объект(ы) с пропсами или домами."; return; }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < roots.Count; i++) sb.AppendLine(MergeOne(roots[i]));
            _report = sb.ToString();
            Debug.Log("[Фон] слияние\n" + _report);
        }

        private string MergeOne(GameObject root)
        {
            UnmergeOne(root);
            Vector3 c = root.transform.position;
            MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(false);
            Dictionary<string, List<CombineInstance>> groups = new Dictionary<string, List<CombineInstance>>();
            Dictionary<string, Material> mats = new Dictionary<string, Material>();
            Dictionary<string, int[]> info = new Dictionary<string, int[]>();   // cx, cz, shadow, layer
            List<MeshRenderer> used = new List<MeshRenderer>();
            Dictionary<string, int> skipped = new Dictionary<string, int>();
            for (int i = 0; i < mrs.Length; i++)
            {
                MeshRenderer r = mrs[i];
                if (!r.enabled) continue;
                if (r.GetComponentInParent<CityMergedMarker>() != null) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                bool neg;
                string why = Why(r, root.transform, out neg);
                if (why != null) { int k; skipped.TryGetValue(why, out k); skipped[why] = k + 1; continue; }
                Vector3 p = r.bounds.center;
                if (_radius > 0.01f && (new Vector2(p.x - c.x, p.z - c.z)).magnitude > _radius) continue;
                Material[] ms = r.sharedMaterials;
                int sub = Mathf.Min(ms.Length, mf.sharedMesh.subMeshCount);
                int cx = Mathf.FloorToInt(p.x / _cell), cz = Mathf.FloorToInt(p.z / _cell);
                int sh = (int)r.shadowCastingMode;
                if (sh != 0 && _smallNoShadow && r.bounds.size.magnitude < _smallSize) sh = 0;
                int lay = r.gameObject.layer;
                bool any = false;
                for (int s = 0; s < sub; s++)
                {
                    if (ms[s] == null) continue;
                    string key = ms[s].GetInstanceID() + "|" + cx + "|" + cz + "|" + sh + "|" + lay;
                    List<CombineInstance> l;
                    if (!groups.TryGetValue(key, out l)) { l = new List<CombineInstance>(); groups[key] = l; mats[key] = ms[s]; info[key] = new int[] { cx, cz, sh, lay }; }
                    CombineInstance ci = new CombineInstance();
                    ci.mesh = mf.sharedMesh; ci.subMeshIndex = s; ci.transform = r.transform.localToWorldMatrix;
                    l.Add(ci);
                    any = true;
                }
                if (any) used.Add(r);
            }
            StringBuilder sk = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in skipped) sk.Append("  пропущено (" + kv.Key + "): " + kv.Value + "\n");
            if (used.Count == 0) return "«" + root.name + "»: нечего сливать.\n" + sk;

            string dir = "Assets/Generated/City/Merged";
            if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
            if (!AssetDatabase.IsValidFolder("Assets/Generated/City")) AssetDatabase.CreateFolder("Assets/Generated", "City");
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Generated/City", "Merged");

            GameObject outRoot = new GameObject(root.name + "_Merged");
            Undo.RegisterCreatedObjectUndo(outRoot, "Слияние фона");
            outRoot.transform.SetParent(root.transform.parent, false);
            outRoot.layer = root.layer;
            CityMergedMarker mk = outRoot.AddComponent<CityMergedMarker>();
            mk.Source = root;
            int made = 0, verts = 0;
            foreach (KeyValuePair<string, List<CombineInstance>> kv in groups)
            {
                Mesh m = new Mesh();
                int total = 0;
                for (int i = 0; i < kv.Value.Count; i++) total += kv.Value[i].mesh.GetSubMesh(kv.Value[i].subMeshIndex).indexCount > 0 ? kv.Value[i].mesh.vertexCount : 0;
                m.indexFormat = total > 60000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                m.CombineMeshes(kv.Value.ToArray(), true, true);
                m.RecalculateBounds();
                int[] inf = info[kv.Key];
                string nm = root.name + "_" + mats[kv.Key].name + "_" + inf[0] + "_" + inf[1] + (inf[2] == 0 ? "_ns" : "");
                foreach (char ch in System.IO.Path.GetInvalidFileNameChars()) nm = nm.Replace(ch, '_');
                string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + nm + ".asset");
                AssetDatabase.CreateAsset(m, path);
                GameObject go = new GameObject(nm);
                go.transform.SetParent(outRoot.transform, false);
                go.layer = inf[3];
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[kv.Key];
                mr.shadowCastingMode = (ShadowCastingMode)inf[2];
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                made++; verts += m.vertexCount;
            }
            int hidden = 0;
            for (int i = 0; i < used.Count; i++)
            {
                MeshRenderer r = used[i];
                Undo.RecordObject(r, "Слияние фона");
                r.enabled = false;
                mk.Disabled.Add(r);
                if (_hideLeaves && r.transform.childCount == 0 && r.GetComponents<Component>().Length == 3 && r.gameObject.activeSelf)
                {
                    Undo.RecordObject(r.gameObject, "Слияние фона");
                    r.gameObject.SetActive(false);
                    mk.Hidden.Add(r.gameObject);
                    hidden++;
                }
            }
            mk.SourceRenderers = used.Count; mk.MergedMeshes = made;
            AssetDatabase.SaveAssets();
            return "«" + root.name + "»: слито рендереров " + used.Count + " → мешей " + made + " (вершин " + verts + "), выключено пустых объектов " + hidden + ".\n" + sk;
        }

        private void UnmergeButton()
        {
            GameObject[] sel = Selection.gameObjects;
            List<GameObject> roots = new List<GameObject>();
            if (sel.Length > 0) roots.AddRange(sel); else if (_root != null) roots.Add(_root);
            int n = 0;
            for (int i = 0; i < roots.Count; i++) n += UnmergeOne(roots[i]);
            _report = n > 0 ? "Слияние снято у объектов: " + n + ". Оригиналы снова видны, служебные меши удалены." : "У выбранных нет слияния.";
            Debug.Log("[Фон] " + _report);
        }

        private int UnmergeOne(GameObject root)
        {
            List<GameObject> kill = new List<GameObject>();
            CityMergedMarker[] marks = Object.FindObjectsByType<CityMergedMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < marks.Length; i++) if (marks[i].Source == root) kill.Add(marks[i].gameObject);
            for (int i = 0; i < kill.Count; i++)
            {
                CityMergedMarker mk = kill[i].GetComponent<CityMergedMarker>();
                for (int k = 0; k < mk.Disabled.Count; k++) if (mk.Disabled[k] != null) { Undo.RecordObject(mk.Disabled[k], "Снять слияние"); mk.Disabled[k].enabled = true; }
                for (int k = 0; k < mk.Hidden.Count; k++) if (mk.Hidden[k] != null) { Undo.RecordObject(mk.Hidden[k], "Снять слияние"); mk.Hidden[k].SetActive(true); }
                MeshFilter[] mfs = kill[i].GetComponentsInChildren<MeshFilter>(true);
                for (int k = 0; k < mfs.Length; k++)
                {
                    string ap = mfs[k].sharedMesh != null ? AssetDatabase.GetAssetPath(mfs[k].sharedMesh) : "";
                    if (ap.StartsWith("Assets/Generated/City/Merged/")) AssetDatabase.DeleteAsset(ap);
                }
                Undo.DestroyObjectImmediate(kill[i]);
            }
            int count = kill.Count;
            // слияние старой версии (без метки): объект «<имя>_Merged» рядом, оригиналы с выключенным MeshRenderer
            List<GameObject> legacy = new List<GameObject>();
            string lname = root.name + "_Merged";
            GameObject[] scan = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < scan.Length; i++)
                if (scan[i].name == lname && scan[i].scene == root.scene && scan[i].GetComponent<CityMergedMarker>() == null) legacy.Add(scan[i]);
            if (legacy.Count > 0)
            {
                MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
                for (int k = 0; k < mrs.Length; k++)
                    if (!mrs[k].enabled && mrs[k].GetComponent<MeshFilter>() != null && mrs[k].GetComponentInParent<CityMergedMarker>() == null) { Undo.RecordObject(mrs[k], "Снять слияние"); mrs[k].enabled = true; }
                for (int i = 0; i < legacy.Count; i++)
                {
                    MeshFilter[] mfs = legacy[i].GetComponentsInChildren<MeshFilter>(true);
                    for (int k = 0; k < mfs.Length; k++)
                    {
                        string ap = mfs[k].sharedMesh != null ? AssetDatabase.GetAssetPath(mfs[k].sharedMesh) : "";
                        if (ap.StartsWith("Assets/Generated/City/Merged/")) AssetDatabase.DeleteAsset(ap);
                    }
                    Undo.DestroyObjectImmediate(legacy[i]);
                    count++;
                }
                Debug.Log("[Фон] убрано старое слияние «" + lname + "»; включены все выключенные MeshRenderer под «" + root.name + "» (в старой версии метки не было, вручную выключенные тоже включатся).");
            }
            return count;
        }

        // ───────────── подвижное ─────────────
        private static readonly string[] Movers =
        {
            "CitySky", "CityTraffic", "CityStorm", "CityWinter", "CityPigeons", "CityLaundry", "CityUmbrella", "CityLandmarkGlow",
            "CityFloorGenerator", "CityModuleHouse", "CityFakeFloors", "CityLevelRider", "CatCircleWalker"
        };

        private static bool IsMover(Component c)
        {
            if (c is Animator || c is Rigidbody || c is Animation) return true;
            MonoBehaviour mb = c as MonoBehaviour;
            if (mb == null) return false;
            string n = mb.GetType().Name;
            for (int i = 0; i < Movers.Length; i++) if (Movers[i] == n) return true;
            return false;
        }

        private static bool UnderAny(Transform t, HashSet<Transform> roots)
        {
            for (; t != null; t = t.parent) if (roots.Contains(t)) return true;
            return false;
        }

        /// <summary>Всё, что двигается (небо, машины, этажи, игровые, аниматоры): снять статику (иначе статический батчинг
        /// «замораживает» его на месте) и вынуть из слияния, если туда попало.</summary>
        private void FixMovers()
        {
            HashSet<Transform> roots = new HashSet<Transform>();
            Component[] cs = Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Dictionary<string, int> kinds = new Dictionary<string, int>();
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i] == null || !IsMover(cs[i])) continue;
                roots.Add(cs[i].transform);
                string k = cs[i].GetType().Name; int n; kinds.TryGetValue(k, out n); kinds[k] = n + 1;
            }
            int cleared = 0;
            foreach (Transform r in roots)
            {
                Transform[] ts = r.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < ts.Length; i++)
                {
                    GameObject go = ts[i].gameObject;
                    if (GameObjectUtility.GetStaticEditorFlags(go) == 0) continue;
                    Undo.RecordObject(go, "Снять статику");
                    GameObjectUtility.SetStaticEditorFlags(go, 0);
                    cleared++;
                }
            }
            // вынуть из слияния: включить оригиналы и пересобрать слияние без них
            List<GameObject> remerge = new List<GameObject>();
            int freed = 0;
            CityMergedMarker[] marks = Object.FindObjectsByType<CityMergedMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < marks.Length; i++)
            {
                bool hit = false;
                for (int k = 0; k < marks[i].Disabled.Count; k++)
                    if (marks[i].Disabled[k] != null && UnderAny(marks[i].Disabled[k].transform, roots)) { hit = true; freed++; }
                if (hit && marks[i].Source != null && !remerge.Contains(marks[i].Source)) remerge.Add(marks[i].Source);
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < remerge.Count; i++) sb.AppendLine(MergeOne(remerge[i]));
            // старые слияния без метки
            List<string> legacy = new List<string>();
            GameObject[] all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.EndsWith("_Merged") && all[i].GetComponent<CityMergedMarker>() == null && all[i].scene.IsValid()) legacy.Add(all[i].name);

            StringBuilder rep = new StringBuilder();
            rep.AppendLine("Подвижных корней: " + roots.Count + ", снята статика у объектов: " + cleared + ".");
            foreach (KeyValuePair<string, int> kv in kinds) rep.AppendLine("  " + kv.Key + ": " + kv.Value);
            rep.AppendLine("Вынуто из слияния подвижных рендереров: " + freed + (remerge.Count > 0 ? ", слияние пересобрано у: " + remerge.Count : "") + ".");
            if (sb.Length > 0) rep.Append(sb);
            if (legacy.Count > 0)
                rep.AppendLine("ВНИМАНИЕ: старые слияния без метки (первая версия, туда могли попасть самолёты): " + string.Join(", ", legacy.ToArray()) + ". Выдели их ИСХОДНЫЕ объекты и нажми «Снять слияние у выделенных», потом слей заново.");
            rep.AppendLine("Небо после этого лучше пересобрать: объект с CitySky → «Убрать» → «Собрать».");
            _report = rep.ToString();
            Debug.Log("[Фон] подвижное\n" + _report);
        }

        private void UnmergeAll()
        {
            int n = 0;
            CityMergedMarker[] marks = Object.FindObjectsByType<CityMergedMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i] == null) continue;
                if (marks[i].Source != null) { n += UnmergeOne(marks[i].Source); continue; }
                CityMergedMarker mk = marks[i];
                for (int k = 0; k < mk.Disabled.Count; k++) if (mk.Disabled[k] != null) { Undo.RecordObject(mk.Disabled[k], "Снять слияние"); mk.Disabled[k].enabled = true; }
                for (int k = 0; k < mk.Hidden.Count; k++) if (mk.Hidden[k] != null) { Undo.RecordObject(mk.Hidden[k], "Снять слияние"); mk.Hidden[k].SetActive(true); }
                Undo.DestroyObjectImmediate(mk.gameObject);
                n++;
            }
            // старые без метки: ищем исходник по имени без «_Merged»
            for (int pass = 0; pass < 64; pass++)
            {
                GameObject[] all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                bool any = false;
                for (int i = 0; i < all.Length; i++)
                {
                    GameObject g = all[i];
                    if (g == null || !g.scene.IsValid() || !g.name.EndsWith("_Merged") || g.GetComponent<CityMergedMarker>() != null) continue;
                    string src = g.name.Substring(0, g.name.Length - "_Merged".Length);
                    GameObject s0 = null;
                    for (int k = 0; k < all.Length; k++) if (all[k] != null && all[k] != g && all[k].name == src && all[k].scene == g.scene) { s0 = all[k]; break; }
                    if (s0 != null) n += UnmergeOne(s0);
                    else { Undo.DestroyObjectImmediate(g); n++; Debug.LogWarning("[Фон] удалён «" + g.name + "»: исходник «" + src + "» не найден, выключенные рендереры включи вручную, если что-то пропало."); }
                    any = true;
                    break;
                }
                if (!any) break;
            }
            _report = "Снято слияний: " + n + ". Сохрани сцену и перезапусти её (иначе GPU Resident Drawer может ругаться на пропавшие меши).";
            Debug.Log("[Фон] " + _report);
        }

        /// <summary>Самолёты, шары, дирижабли, птицы, оторванные от неба (CitySky): висят и не летают. Найти и выделить или удалить.</summary>
        private void FindStrayFlyers(bool delete)
        {
            string[] names = { "Airliner", "SmallPlane", "Blimp", "Balloon", "Birds" };
            List<GameObject> found = new List<GameObject>();
            StringBuilder sb = new StringBuilder();
            GameObject[] all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                GameObject g = all[i];
                if (!g.scene.IsValid()) continue;
                bool nm = false;
                for (int k = 0; k < names.Length; k++) if (g.name == names[k]) { nm = true; break; }
                if (!nm) continue;
                Transform p = g.transform.parent;
                bool inSky = false;
                for (Transform t = p; t != null; t = t.parent)
                {
                    MonoBehaviour[] mbs = t.GetComponents<MonoBehaviour>();
                    for (int k = 0; k < mbs.Length; k++) if (mbs[k] != null && mbs[k].GetType().Name == "CitySky") { inSky = true; break; }
                    if (inSky) break;
                }
                // живой — прямой ребёнок CitySky; всё остальное — оторванное
                if (inSky && p != null && p.GetComponent("CitySky") != null) continue;
                found.Add(g);
                sb.AppendLine("  " + Path(g.transform));
            }
            if (found.Count == 0)
            {
                _report = "Оторванных от неба самолётов/шаров не нашлось. Значит, застывшие — внутри слитых мешей: нажми «Снять ВСЕ слияния в сцене», потом «Починить подвижное», потом слей заново.";
            }
            else if (delete)
            {
                for (int i = 0; i < found.Count; i++) Undo.DestroyObjectImmediate(found[i]);
                _report = "Удалено застывших летающих: " + found.Count + "\n" + sb;
            }
            else
            {
                Selection.objects = found.ToArray();
                _report = "Найдено застывших летающих: " + found.Count + " (выделены в иерархии):\n" + sb + "Если это они — «Удалить застывших летающих».";
            }
            Debug.Log("[Фон] " + _report);
        }

        private static string Path(Transform t)
        {
            string s = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
