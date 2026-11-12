#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CatCooks.Food.EditorTools
{
    /// <summary>
    /// ВЕДРО: ЗАМЕНИТЬ МОДЕЛЬ — новая модель внутрь твоих вёдер, все компоненты остаются.
    ///
    ///   1. Модель (FBX/префаб) → в поле «Новая модель».
    ///   2. Выдели вёдра в сцене (или «все вёдра»), нажми «Заменить модель».
    /// Для каждого ведра:
    ///   • старые меши (корпус, старая ручка) ВЫКЛЮЧАЮТСЯ — не удаляются;
    ///   • новая модель встаёт дочерним объектом «Model (v4)» — дно на место старого дна,
    ///     по центру, по высоте как старое ведро (галка «Подогнать размер»);
    ///   • дужка ищется по имени (handle, ручк, дужк, bail…): создаётся HandlePivot на оси петель,
    ///     дужка переезжает в него, на корень — BucketHandle (болтается, держат за неё);
    ///   • CookwareAutoSetup пересобирает стенки, приём, горловину, кучку и жидкость под новую
    ///     модель (дужка не учитывается), жидкость подгоняется под стенки модели;
    ///   • Cookware, CarriedContainer, CookwareSpill, NetworkObject и всё остальное — как было.
    ///
    /// «Новое ведро из выделенной модели» — для модели, уже стоящей в сцене: дублирует образец
    /// (как Ctrl+D — FishNet сам выдаст новый Id), ставит на место модели и переносит модель внутрь.
    ///
    /// Всё через Undo. Префабы не создаются. Меню: CatCooks → Кулинария → Ведро: заменить модель…
    ///
    /// v12 — ЛЮБАЯ ТАРА (ведро, банка для бетона…): пресет «Банка» — без дужки, стенки/кучка/жидкость
    /// собираются AutoSetup «По форме модели» (поиск полости заливкой — для неровной, вытянутой,
    /// низкополигональной банки). Меню: CatCooks → Кулинария → Банка: заменить модель…
    /// </summary>
    public sealed class CatCooksBucketModelSwap : EditorWindow
    {
        private const string ModelName = "Model (v4)";
        private const string PivotName = "HandlePivot";
        private static readonly string[] AutoNames = { "AutoColliders", "Receive", "Mouth", "HeapAnchor", "LiquidAnchor", ModelName, PivotName };

        private GameObject _model;
        private GameObject _template;
        private Vector3 _euler = Vector3.zero;
        private bool _fitSize = true;
        private bool _setupHandle = true;
        private bool _rebuild = true;
        private string _handleWords = "handle, ручк, дужк, bail, hand, ушк";
        private Vector2 _scroll;
        // v12: какая тара и как собирать
        private string _targetWords = "ведр, bucket, vedro";
        private string _what = "Ведро";
        private int _formMode = 0;                    // 0 — как было, 1 — коробка, 2 — круглая, 3 — по форме модели
        private static readonly string[] FormNames = { "Как было в CookwareAutoSetup", "Прямоугольная (Box)", "Круглая (Round) + подгонка граней", "По форме модели (Scan) — неровная/вытянутая тара" };

        [MenuItem("CatCooks/Кулинария/Ведро: заменить модель…", priority = 301)]
        private static void Open()
        {
            CatCooksBucketModelSwap w = GetWindow<CatCooksBucketModelSwap>("Тара: модель");
            w.minSize = new Vector2(460f, 380f);
            w.PresetBucket();
            w.Show();
        }

        [MenuItem("CatCooks/Кулинария/Банка: заменить модель…", priority = 302)]
        private static void OpenJar()
        {
            CatCooksBucketModelSwap w = GetWindow<CatCooksBucketModelSwap>("Тара: модель");
            w.minSize = new Vector2(460f, 380f);
            w.PresetJar();
            w.Show();
        }

        private void PresetBucket()
        {
            _what = "Ведро"; _setupHandle = true; _formMode = 0; _targetWords = "ведр, bucket, vedro";
        }

        private void PresetJar()
        {
            _what = "Банка"; _setupHandle = false; _formMode = 3; _targetWords = "банк, jar, бетон, banka";
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("Меняет модель у твоих вёдер: старые меши выключаются, новая встаёт внутрь, " +
                                    "компоненты ведра остаются. Дужка — болтается (BucketHandle), держат за неё. " +
                                    "Перед запуском — коммит в git. Всё через Undo.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Пресет: " + _what, EditorStyles.boldLabel);
            if (GUILayout.Button("Ведро", GUILayout.Width(80))) PresetBucket();
            if (GUILayout.Button("Банка", GUILayout.Width(80))) PresetJar();
            EditorGUILayout.EndHorizontal();
            _model = (GameObject)EditorGUILayout.ObjectField("Новая модель", _model, typeof(GameObject), true);
            _euler = EditorGUILayout.Vector3Field(new GUIContent("Поворот модели", "Если модель лежит на боку — 90 / -90 по X"), _euler);
            _fitSize = EditorGUILayout.ToggleLeft("Подогнать размер под старое ведро (по высоте)", _fitSize);
            _setupHandle = EditorGUILayout.ToggleLeft("Настроить дужку (болтается, держат за неё)", _setupHandle);
            _handleWords = EditorGUILayout.TextField(new GUIContent("Дужка: слова в имени", "Через запятую, без регистра"), _handleWords);
            _rebuild = EditorGUILayout.ToggleLeft("Пересобрать стенки, кучку и жидкость (CookwareAutoSetup)", _rebuild);
            _formMode = EditorGUILayout.Popup(new GUIContent("Форма для сборки", "Банка неровная — «По форме модели»: полость ищется заливкой, стенки, горка и жидкость — по мешу"), _formMode, FormNames);
            _targetWords = EditorGUILayout.TextField(new GUIContent("«Все такие в сцене»: слова в названии", "Через запятую: по названию тары (Cookware Def) или объекта"), _targetWords);

            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(_model == null))
            {
                if (GUILayout.Button("Заменить модель у ВЫДЕЛЕННЫХ (" + _what + ")", GUILayout.Height(26))) Run(SelectedBuckets());
                if (GUILayout.Button("Заменить модель у ВСЕХ в сцене (" + _what + ")", GUILayout.Height(22))) Run(AllBuckets(_targetWords));
            }
            EditorGUILayout.Space(4);
            if (GUILayout.Button("Только настроить дужку у выделенных (модель уже новая)")) RunHandleOnly(SelectedBuckets());
            if (GUILayout.Button("Только пересобрать стенки, кучку и жидкость у выделенных")) RunRebuildOnly(SelectedBuckets());

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Новое ведро из модели, стоящей в сцене", EditorStyles.boldLabel);
            _template = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Образец ведра", "Готовое ведро из сцены — его компоненты переедут"), _template, typeof(GameObject), true);
            using (new EditorGUI.DisabledScope(_template == null))
                if (GUILayout.Button("Сделать вёдра из ВЫДЕЛЕННЫХ моделей по образцу")) MakeFromSelectedModels();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Сборка игры", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("Добавить мультяшные шейдеры жидкости в сборку",
                "CatCooks/Toon Drop, Milk Puddle и Toon Poof → Project Settings → Graphics → Always Included Shaders. " +
                "Их ищут по имени — без этого в собранной игре капли и лужи будут по-старому.")))
                IncludeShaders();
            if (GUILayout.Button(new GUIContent("Включить Depth Texture в URP (лужи не ложатся на дыры и швы)",
                "Шейдер лужи читает глубину сцены и не рисуется там, где пол ниже лужи (швы, люки, дыры). " +
                "Для этого в URP Asset нужна галка Depth Texture. Кнопка ставит её во всех URP Asset проекта.")))
                EnableDepthTexture();
            EditorGUILayout.EndScrollView();
        }

        // ───────────────────────── выбор вёдер ─────────────────────────

        private static List<GameObject> SelectedBuckets()
        {
            var list = new List<GameObject>();
            foreach (GameObject go in Selection.gameObjects)
            {
                // v12: любая тара — корень с Cookware (ведро, банка, корыто)
                Cookware cw = go.GetComponentInParent<Cookware>();
                if (cw != null && !list.Contains(cw.gameObject)) list.Add(cw.gameObject);
            }
            if (list.Count == 0) Debug.LogWarning("[Тара] Выдели тару в сцене (объект с Cookware: ведро, банка…).");
            return list;
        }

        private static List<GameObject> AllBuckets(string wordsCsv)
        {
            var list = new List<GameObject>();
            string[] words = (wordsCsv ?? "").ToLowerInvariant().Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (Cookware cw in Object.FindObjectsByType<Cookware>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string t = ((cw.Def != null ? cw.Def.Title : "") + " " + cw.name).ToLowerInvariant();
                foreach (string w0 in words)
                {
                    string w = w0.Trim();
                    if (w.Length > 0 && t.Contains(w)) { list.Add(cw.gameObject); break; }
                }
            }
            if (list.Count == 0) Debug.LogWarning("[Тара] В сцене нет тары со словами «" + wordsCsv + "» в названии");
            return list;
        }

        // ───────────────────────── замена ─────────────────────────

        private void Run(List<GameObject> buckets)
        {
            if (buckets.Count == 0 || _model == null) return;
            var log = new StringBuilder("[Ведро] ЗАМЕНА МОДЕЛИ\n");
            foreach (GameObject b in buckets) SwapOne(b, _model, log);
            Debug.Log(log.ToString());
        }

        private void SwapOne(GameObject b, GameObject modelSource, StringBuilder log)
        {
            Undo.RegisterFullObjectHierarchyUndo(b, "Ведро: заменить модель");
            Transform root = b.transform;

            // прошлая замена — убрать (старые меши остаются выключенными)
            Transform prev = root.Find(ModelName);
            if (prev != null) Undo.DestroyObjectImmediate(prev.gameObject);
            Transform prevPivot = root.Find(PivotName);
            if (prevPivot != null) Undo.DestroyObjectImmediate(prevPivot.gameObject);

            // старые меши: габарит и выключить
            Bounds oldB = new Bounds();
            bool hasOld = false;
            int off = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsVisual(r, root)) continue;
                // размер — по корпусу, без старой ручки
                if (!_setupHandle || !NameIsHandle(r.transform, root))
                {
                    Bounds lb = LocalBounds(root, r);
                    if (!hasOld) { oldB = lb; hasOld = true; } else oldB.Encapsulate(lb);
                }
                if (r.enabled) { Undo.RecordObject(r, "Ведро: старый меш"); r.enabled = false; off++; }
            }

            // новая модель
            GameObject inst;
            if (EditorUtility.IsPersistent(modelSource)) inst = (GameObject)PrefabUtility.InstantiatePrefab(modelSource);
            else inst = Object.Instantiate(modelSource);
            Undo.RegisterCreatedObjectUndo(inst, "Ведро: новая модель");
            if (PrefabUtility.IsPartOfPrefabInstance(inst))
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = ModelName;
            inst.transform.SetParent(root, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.Euler(_euler);
            inst.transform.localScale = Vector3.one;
            // у модели свои коллайдеры не нужны — стенки соберёт AutoSetup
            foreach (Collider c in inst.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(c);

            Bounds nb;
            Transform newHandle = _setupHandle ? FindHandle(inst.transform) : null;      // v12: у банки дужки нет
            Renderer[] skipH = newHandle != null ? newHandle.GetComponentsInChildren<Renderer>(true) : null;
            if (!ModelBounds(root, inst.transform, out nb, skipH)) { log.AppendLine($"  ✖ «{b.name}»: в модели нет мешей"); return; }
            if (_fitSize && hasOld && nb.size.y > 1e-4f)
            {
                float k = oldB.size.y / nb.size.y;
                if (Mathf.Abs(k - 1f) < 0.03f) k = 1f;                                   // и так в размер — не трогать
                inst.transform.localScale = Vector3.one * k;
                ModelBounds(root, inst.transform, out nb, skipH);
            }
            Vector3 target = hasOld ? new Vector3(oldB.center.x, oldB.min.y, oldB.center.z) : Vector3.zero;
            Vector3 cur = new Vector3(nb.center.x, nb.min.y, nb.center.z);
            inst.transform.localPosition += target - cur;
            log.AppendLine($"  ✔ «{b.name}»: старых мешей выключено {off}, новая модель {nb.size.x:0.00}×{nb.size.y:0.00}×{nb.size.z:0.00} м" +
                           (_fitSize && hasOld ? $" (подогнана под высоту {oldB.size.y:0.00} м)" : ""));

            Renderer[] handleR = new Renderer[0];
            if (_setupHandle) handleR = SetupHandle(b, inst.transform, log);
            if (_rebuild) Rebuild(b, handleR, log, _formMode);
            EditorSceneManager.MarkSceneDirty(b.scene);
        }

        private void RunRebuildOnly(List<GameObject> list)
        {
            var log = new StringBuilder("[Тара] ПЕРЕСБОРКА\n");
            foreach (GameObject b in list)
            {
                Undo.RegisterFullObjectHierarchyUndo(b, "Тара: пересборка");
                Renderer[] handleR = new Renderer[0];
                Transform piv = b.transform.Find(PivotName);
                if (piv != null) handleR = piv.GetComponentsInChildren<Renderer>(true);
                log.AppendLine("  «" + b.name + "»");
                Rebuild(b, handleR, log, _formMode);
                EditorSceneManager.MarkSceneDirty(b.scene);
            }
            Debug.Log(log.ToString());
        }

        private void RunHandleOnly(List<GameObject> buckets)
        {
            var log = new StringBuilder("[Ведро] ДУЖКА\n");
            foreach (GameObject b in buckets)
            {
                Undo.RegisterFullObjectHierarchyUndo(b, "Ведро: дужка");
                Transform model = b.transform.Find(ModelName);
                SetupHandle(b, model != null ? model : b.transform, log);
                EditorSceneManager.MarkSceneDirty(b.scene);
            }
            Debug.Log(log.ToString());
        }

        /// <summary>Дужка: найти по имени, поставить ось на петли, BucketHandle на корень.</summary>
        private Renderer[] SetupHandle(GameObject b, Transform searchIn, StringBuilder log)
        {
            Transform root = b.transform;
            Transform handle = FindHandle(searchIn);
            if (handle == null)
            {
                log.AppendLine($"    ⚠ «{b.name}»: дужка не найдена по словам «{_handleWords}» — переименуй объект дужки в модели или поправь слова");
                return new Renderer[0];
            }
            if (PrefabUtility.IsPartOfPrefabInstance(handle))
                PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(handle), PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Renderer[] rs = handle.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { log.AppendLine($"    ⚠ «{b.name}»: у «{handle.name}» нет меша"); return rs; }
            Bounds hb = LocalBounds(root, rs[0]);
            for (int i = 1; i < rs.Length; i++) hb.Encapsulate(LocalBounds(root, rs[i]));

            // ось петель: внизу дужки (поднятой), вдоль длинной горизонтальной стороны
            bool alongX = hb.size.x >= hb.size.z;
            float pivotY = hb.min.y + hb.size.y * 0.06f;
            // модельер поставил точку опоры дужки на петли — берём её (точнее, чем по габариту)
            Vector3 ho = root.InverseTransformPoint(handle.position);
            if (Mathf.Abs(ho.y - hb.min.y) < hb.size.y * 0.15f) pivotY = ho.y;
            var pivotGo = new GameObject(PivotName);
            Undo.RegisterCreatedObjectUndo(pivotGo, "Ведро: ось дужки");
            Transform pivot = pivotGo.transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(hb.center.x, pivotY, hb.center.z);
            pivot.localRotation = alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            Undo.SetTransformParent(handle, pivot, "Ведро: дужка в ось");
            float length = Mathf.Max(0.02f, hb.max.y - pivotY);

            BucketHandle bh = b.GetComponent<BucketHandle>();
            if (bh == null) bh = Undo.AddComponent<BucketHandle>(b);
            Undo.RecordObject(bh, "Ведро: дужка");
            bh.Setup(pivot, length);
            EditorUtility.SetDirty(bh);

            // поднята ли дужка в модели: верх заметно выше кромки?
            Bounds body;
            bool tall = true;
            Transform model = root.Find(ModelName);
            float lim = 88f;
            if (model != null && ModelBounds(root, model, out body, rs))
            {
                tall = hb.max.y > body.max.y + hb.size.y * 0.25f;
                // длинная дужка (длиннее радиуса ведра) свисает СНАРУЖИ вдоль стенки, короткая — ложится на кромку
                float rOut = Mathf.Max(body.extents.x, body.extents.z);
                if (length > rOut * 1.05f) lim = 180f - Mathf.Asin(Mathf.Clamp01(rOut / length)) * Mathf.Rad2Deg - 4f;
            }
            var hso = new SerializedObject(bh);
            SerializedProperty limP = hso.FindProperty("_limits");
            if (limP != null) { limP.vector2Value = new Vector2(-lim, lim); hso.ApplyModifiedProperties(); }
            log.AppendLine($"    ✔ дужка в сложенном виде: до ±{lim:0}°" + (lim > 95f ? " (свисает снаружи вдоль стенки)" : " (ложится на кромку)"));
            log.AppendLine($"    ✔ дужка «{handle.name}»: ось на высоте {pivotY:0.00} м, длина {length:0.00} м" +
                           (tall ? "" : " — ⚠ в модели дужка НЕ поднята вверх: подними её в модели или поверни HandlePivot, чтобы дужка смотрела вверх"));
            return rs;
        }

        private Transform FindHandle(Transform t)
        {
            string[] words = _handleWords.ToLowerInvariant().Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
            {
                if (c == t) continue;
                string n = c.name.ToLowerInvariant();
                foreach (string w in words)
                {
                    string ww = w.Trim();
                    if (ww.Length > 0 && n.Contains(ww) && c.GetComponentInChildren<Renderer>(true) != null) return c;
                }
            }
            return FindHandleByShape(t);
        }

        /// <summary>
        /// Имя не подсказало (Cube.002) — ищем по форме: самый большой меш — корпус; дужка — другой меш,
        /// чей верх заметно выше корпуса, а ширина почти как у корпуса.
        /// </summary>
        private static Transform FindHandleByShape(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length < 2) return null;
            Transform root = t.parent != null ? t.parent : t;
            Renderer body = null; float bestVol = -1f;
            foreach (Renderer r in rs)
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                Bounds b = LocalBounds(root, r);
                float vol = b.size.x * b.size.y * b.size.z;
                if (vol > bestVol) { bestVol = vol; body = r; }
            }
            if (body == null) return null;
            Bounds bb = LocalBounds(root, body);
            Renderer best = null; float bestTop = bb.max.y + bb.size.y * 0.08f;
            foreach (Renderer r in rs)
            {
                if (r == body || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                Bounds b = LocalBounds(root, r);
                float span = Mathf.Max(b.size.x, b.size.z), bodySpan = Mathf.Max(bb.size.x, bb.size.z);
                if (b.max.y > bestTop && span > bodySpan * 0.6f) { bestTop = b.max.y; best = r; }
            }
            return best != null ? best.transform : null;
        }

        private bool NameIsHandle(Transform t, Transform root)
        {
            string[] words = _handleWords.ToLowerInvariant().Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (Transform c = t; c != null && c != root; c = c.parent)
            {
                string n = c.name.ToLowerInvariant();
                foreach (string w in words) { string ww = w.Trim(); if (ww.Length > 0 && n.Contains(ww)) return true; }
            }
            return false;
        }

        /// <summary>CookwareAutoSetup: дужку не мерить, «Подогнать и собрать», жидкость — под стенки модели.</summary>
        private static void Rebuild(GameObject b, Renderer[] handle, StringBuilder log, int formMode)
        {
            CookwareAutoSetup au = b.GetComponent<CookwareAutoSetup>();
            if (au == null) au = Undo.AddComponent<CookwareAutoSetup>(b);
            var so = new SerializedObject(au);
            // v12: форма сборки (AutoSetup: Box = 0, Round = 1, Scan = 2)
            SerializedProperty fp = so.FindProperty("_form");
            if (fp != null && formMode > 0)
            {
                fp.intValue = formMode == 1 ? 0 : formMode == 2 ? 1 : 2;
                if (formMode == 3)
                {
                    SerializedProperty up = so.FindProperty("_autoUp");
                    if (up != null) up.boolValue = false;                      // банка стоит ровно — ось вверх известна
                    SerializedProperty res = so.FindProperty("_scanResolution");
                    if (res != null && res.intValue < 96) res.intValue = 96;   // точнее по неровной форме
                }
            }
            bool scan = fp != null && fp.intValue == 2;
            SerializedProperty ig = so.FindProperty("_ignoreRenderers");
            if (ig != null)
            {
                ig.arraySize = handle.Length;
                for (int i = 0; i < handle.Length; i++) ig.GetArrayElementAtIndex(i).objectReferenceValue = handle[i];
            }
            so.ApplyModifiedProperties();
            MethodInfo fit = typeof(CookwareAutoSetup).GetMethod("FitAndBuild", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fit != null) { fit.Invoke(au, null); log.AppendLine("    ✔ стенки, приём, горловина, кучка и жидкость пересобраны (CookwareAutoSetup)"); }
            else log.AppendLine("    ⚠ CookwareAutoSetup: нет «Подогнать и собрать» — нажми его руками (⋮ на компоненте)");

            if (scan)
            {
                log.AppendLine("    ✔ форма «По форме модели»: полость найдена заливкой — стенки, приём, горловина, кучка и жидкость (профиль ширины) по мешу");
                return;
            }
            CookwareLiquidVisual lv = b.GetComponentInChildren<CookwareLiquidVisual>(true);
            MethodInfo walls = typeof(CookwareLiquidVisual).GetMethod("FitToModelWalls", BindingFlags.NonPublic | BindingFlags.Instance);
            if (lv != null && walls != null)
            {
                walls.Invoke(lv, null);
                // AutoSetup меряет по габариту (снаружи) — толстые стенки он не знает. Подвинуть стенки-коллайдеры
                // к настоящей внутренней стенке (по жидкости) и собрать ещё раз, жидкость — подогнать снова.
                var lso = new SerializedObject(lv);
                float inTop = lso.FindProperty("_radiusTop").floatValue, inBot = lso.FindProperty("_radiusBottom").floatValue;
                var aso = new SerializedObject(au);
                SerializedProperty rt = aso.FindProperty("_radiusTop"), bf = aso.FindProperty("_bottomRadiusFactor");
                MethodInfo build = typeof(CookwareAutoSetup).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance);
                if (rt != null && bf != null && build != null && inTop > 0.01f && rt.floatValue > inTop + 0.004f)
                {
                    float was = rt.floatValue;
                    rt.floatValue = inTop;
                    bf.floatValue = Mathf.Clamp(inBot / inTop, 0.5f, 1.2f);
                    aso.ApplyModifiedProperties();
                    build.Invoke(au, null);
                    walls.Invoke(lv, null);
                    log.AppendLine($"    ✔ стенки-коллайдеры подвинуты к внутренней стенке: радиус {was:0.###} → {inTop:0.###} м");
                }
                var lso2 = new SerializedObject(lv);
                int sides = lso2.FindProperty("_wallSides") != null ? lso2.FindProperty("_wallSides").intValue : 0;
                log.AppendLine("    ✔ жидкость подогнана под грани стенок" + (sides >= 3 ? $" ({sides} граней)" : " (круглая)"));
            }
        }

        // ───────────────────────── новое ведро из модели в сцене ─────────────────────────

        private void MakeFromSelectedModels()
        {
            if (_template == null) return;
            var models = new List<GameObject>();
            foreach (GameObject go in Selection.gameObjects)
                if (go != _template && go.GetComponentInParent<CarriedContainer>() == null && !EditorUtility.IsPersistent(go)) models.Add(go);
            if (models.Count == 0) { Debug.LogWarning("[Ведро] Выдели в сцене модели (без Carried Container), из которых сделать вёдра."); return; }
            var log = new StringBuilder("[Ведро] НОВЫЕ ВЁДРА ПО ОБРАЗЦУ\n");
            foreach (GameObject m in models)
            {
                // дубль образца — как Ctrl+D (FishNet сам выдаст новый Id сцены)
                Selection.activeGameObject = _template;
                if (!EditorApplication.ExecuteMenuItem("Edit/Duplicate")) { log.AppendLine("  ✖ не вышло продублировать образец"); break; }
                GameObject dup = Selection.activeGameObject;
                if (dup == null || dup == _template) { log.AppendLine("  ✖ дубль не появился"); break; }
                Undo.RecordObject(dup.transform, "Ведро: на место модели");
                dup.name = m.name + " (bucket)";
                dup.transform.position = m.transform.position;
                dup.transform.rotation = Quaternion.Euler(0f, m.transform.eulerAngles.y, 0f);
                GameObject keep = _model;
                Vector3 keepEuler = _euler;
                _euler = Vector3.zero;
                SwapOne(dup, m, log);
                _euler = keepEuler;
                _model = keep;
                Undo.RecordObject(m, "Ведро: исходная модель");
                m.SetActive(false);
                log.AppendLine($"    исходная модель «{m.name}» выключена (копия — внутри ведра)");
            }
            Debug.Log(log.ToString());
        }

        private static void EnableDepthTexture()
        {
            var log = new StringBuilder("[Ведро] Depth Texture в URP:\n");
            int changed = 0, total = 0;
            string[] guids = AssetDatabase.FindAssets("t:RenderPipelineAsset");
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                Object a = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (a == null) continue;
                System.Reflection.PropertyInfo pi = a.GetType().GetProperty("supportsCameraDepthTexture");
                if (pi == null || !pi.CanWrite) continue;
                total++;
                bool on = (bool)pi.GetValue(a, null);
                if (on) { log.AppendLine("  ✔ " + path + " — уже включено"); continue; }
                Undo.RecordObject(a, "Depth Texture");
                pi.SetValue(a, true, null);
                EditorUtility.SetDirty(a);
                changed++;
                log.AppendLine("  ＋ " + path + " — включено");
            }
            if (total == 0) log.AppendLine("  ✖ URP Asset не найден — включи вручную: URP Asset → Rendering → Depth Texture");
            AssetDatabase.SaveAssets();
            log.AppendLine("Проверь ещё камеру игрока: Camera → Rendering → Depth Texture = Use Pipeline Settings (или On).");
            Debug.Log(log.ToString());
        }

        private static void IncludeShaders()
        {
            string[] names = { "CatCooks/Toon Drop", "CatCooks/Milk Puddle", "CatCooks/Toon Poof", "CatCooks/Liquid Surface" };
            Object gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null) { Debug.LogWarning("[Ведро] Не нашёл ProjectSettings/GraphicsSettings.asset"); return; }
            var so = new SerializedObject(gs);
            SerializedProperty arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) { Debug.LogWarning("[Ведро] В Graphics Settings нет Always Included Shaders"); return; }
            var log = new StringBuilder("[Ведро] Шейдеры в сборке:\n");
            foreach (string n in names)
            {
                Shader sh = Shader.Find(n);
                if (sh == null) { log.AppendLine($"  ✖ «{n}» не найден — файл шейдера на месте?"); continue; }
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { has = true; break; }
                if (has) { log.AppendLine($"  ✔ «{n}» уже есть"); continue; }
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                log.AppendLine($"  ＋ «{n}» добавлен");
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        // ───────────────────────── мелочи ─────────────────────────

        private static bool IsAutoPart(Transform t, Transform root)
        {
            for (Transform c = t; c != null && c != root; c = c.parent)
                foreach (string n in AutoNames) if (c.name == n) return true;
            return false;
        }

        private static bool IsVisual(Renderer r, Transform root)
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) return false;
            if ((r.gameObject.hideFlags & HideFlags.DontSave) != 0) return false;
            if (IsAutoPart(r.transform, root)) return false;
            if (r.GetComponentInParent<CookwareLiquidVisual>() != null) return false;
            if (r.GetComponentInParent<FoodHeapRenderer>() != null) return false;
            return r is MeshRenderer || r is SkinnedMeshRenderer;
        }

        private static bool ModelBounds(Transform root, Transform model, out Bounds b, Renderer[] skip = null)
        {
            b = new Bounds();
            bool has = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (skip != null && Array.IndexOf(skip, r) >= 0) continue;
                Bounds lb = LocalBounds(root, r);
                if (!has) { b = lb; has = true; } else b.Encapsulate(lb);
            }
            return has;
        }

        /// <summary>Габарит рендерера в осях корня (по 8 углам локального меша).</summary>
        private static Bounds LocalBounds(Transform root, Renderer r)
        {
            Bounds lb;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            SkinnedMeshRenderer sk = r as SkinnedMeshRenderer;
            if (mf != null && mf.sharedMesh != null) lb = mf.sharedMesh.bounds;
            else if (sk != null && sk.sharedMesh != null) lb = sk.sharedMesh.bounds;
            else
            {
                Bounds wb = r.bounds;
                Bounds o = new Bounds(root.InverseTransformPoint(wb.center), Vector3.zero);
                o.Encapsulate(root.InverseTransformPoint(wb.min));
                o.Encapsulate(root.InverseTransformPoint(wb.max));
                return o;
            }
            Vector3 c = lb.center, e = lb.extents;
            Bounds res = new Bounds(root.InverseTransformPoint(r.transform.TransformPoint(c)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                res.Encapsulate(root.InverseTransformPoint(r.transform.TransformPoint(corner)));
            }
            return res;
        }
    }
}
#endif
