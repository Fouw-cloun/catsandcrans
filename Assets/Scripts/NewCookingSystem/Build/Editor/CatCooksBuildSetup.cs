using System.Text;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food.Tools.EditorTools
{
    /// <summary>
    /// v22: СТРОЙКА — настройка в сцене. Префабов не создаёт: только объекты сцены.
    ///   1. Менеджер стройки в сцену — объект BuildManager (сеть + меню построек на C), сам находит префаб опалубки.
    ///   2. Коробка гвоздей в сцену — предмет «Коробка гвоздей» рядом с камерой сцены.
    ///   4. v23.52: Труба для колец макарон в сцену — режется пилой/бензопилой на кольца.
    ///   5. v23.52: Кольцо из трубы — объект для префаба (сохрани префабом, в Spawnable Prefabs, в трубу → Ring Prefab).
    /// </summary>
    public static class CatCooksBuildSetup
    {
        [MenuItem("CatCooks/Стройка/1. Менеджер стройки в сцену")]
        public static void MakeManager()
        {
            var log = new StringBuilder("[Стройка] менеджер:\n");
            BuildManager bm = Object.FindAnyObjectByType<BuildManager>(FindObjectsInactive.Include);
            if (bm == null)
            {
                var go = new GameObject("BuildManager");
                Undo.RegisterCreatedObjectUndo(go, "Менеджер стройки");
                go.AddComponent<NetworkObject>();
                bm = go.AddComponent<BuildManager>();
                log.AppendLine("  ✔ BuildManager создан (NetworkObject + BuildManager). Меню построек — клавиша C");
            }
            else log.AppendLine($"  • BuildManager уже есть: «{bm.name}»");

            var so = new SerializedObject(bm);
            SerializedProperty pf = so.FindProperty("_formworkPrefab");
            SerializedProperty look = so.FindProperty("_formworkLook");
            if (pf != null && pf.objectReferenceValue == null)
            {
                NetworkObject found = null;
                foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
                {
                    GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (p == null || p.GetComponent<BakeTray>() == null) continue;
                    found = p.GetComponent<NetworkObject>();
                    if (found != null) break;
                }
                if (found != null)
                {
                    pf.objectReferenceValue = found;
                    log.AppendLine($"  ✔ префаб опалубки: «{found.name}» ({AssetDatabase.GetAssetPath(found)})");
                }
                else
                {
                    BakeTray inScene = Object.FindAnyObjectByType<BakeTray>(FindObjectsInactive.Include);
                    if (look != null && inScene != null) look.objectReferenceValue = inScene.gameObject;
                    log.AppendLine("  ✖ ПРЕФАБА ОПАЛУБКИ НЕТ — форма достроится, но настоящая опалубка не появится.");
                    log.AppendLine("    Сделай так: перетащи опалубку из Hierarchy в папку Project (станет префабом), проверь, что она в " +
                                   "Spawnable Prefabs (DefaultPrefabObjects), и нажми это меню ещё раз.");
                    if (inScene != null) log.AppendLine($"    Пока вид шаблона беру с опалубки в сцене: «{inScene.name}»");
                }
                so.ApplyModifiedProperties();
            }
            else if (pf != null) log.AppendLine($"  • префаб опалубки уже задан: «{pf.objectReferenceValue.name}»");

            // v23.52: противень для макарон
            SerializedProperty tp = so.FindProperty("_macaronTrayPrefab");
            SerializedProperty tl = so.FindProperty("_macaronTrayLook");
            if (tp != null && tp.objectReferenceValue == null)
            {
                NetworkObject found = null;
                foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
                {
                    GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (p == null || p.GetComponent<MacaronTray>() == null) continue;
                    found = p.GetComponent<NetworkObject>();
                    if (found != null) break;
                }
                if (found != null)
                {
                    tp.objectReferenceValue = found;
                    log.AppendLine($"  ✔ префаб противня для макарон: «{found.name}» ({AssetDatabase.GetAssetPath(found)})");
                }
                else
                {
                    MacaronTray inScene = Object.FindAnyObjectByType<MacaronTray>(FindObjectsInactive.Include);
                    if (tl != null && inScene != null) tl.objectReferenceValue = inScene.gameObject;
                    log.AppendLine("  ✖ ПРЕФАБА ПРОТИВНЯ ДЛЯ МАКАРОН НЕТ — противень достроится, но настоящий не появится.");
                    log.AppendLine("    Перетащи MacaronTray из Hierarchy в папку Project (станет префабом), добавь в Spawnable Prefabs и нажми это меню ещё раз.");
                    if (inScene != null) log.AppendLine($"    Пока вид шаблона беру с противня в сцене: «{inScene.name}»");
                }
                so.ApplyModifiedProperties();
            }
            else if (tp != null) log.AppendLine($"  • префаб противня уже задан: «{tp.objectReferenceValue.name}»");

            EditorUtility.SetDirty(bm);
            EditorSceneManager.MarkSceneDirty(bm.gameObject.scene);
            Selection.activeGameObject = bm.gameObject;
            log.AppendLine("  Сохрани сцену (FishNet выдаст объекту номер сцены).");
            Debug.Log(log.ToString(), bm);
        }

        [MenuItem("CatCooks/Стройка/2. Коробка гвоздей в сцену")]
        public static void MakeNailBox()
        {
            var go = new GameObject("NailBox (Коробка гвоздей)");
            Undo.RegisterCreatedObjectUndo(go, "Коробка гвоздей");
            SceneView sv = SceneView.lastActiveSceneView;
            go.transform.position = sv != null ? sv.pivot : Vector3.zero;
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0) go.layer = layer;
            go.AddComponent<NetworkObject>();
            System.Type nt = System.Type.GetType("FishNet.Component.Transforming.NetworkTransform, FishNet.Runtime");
            if (nt != null)
            {
                Component c = go.AddComponent(nt);
                var so = new SerializedObject(c);
                SerializedProperty ca = so.FindProperty("_clientAuthoritative");
                if (ca != null) { ca.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.16f, 0.09f, 0.11f);
            go.AddComponent<NailBoxItem>();
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[Стройка] коробка гвоздей поставлена у камеры сцены. Модель рисуется сама при запуске (картонная коробка с гвоздями). " +
                      "Размер — Box Collider Size (или Scale объекта, если мир крупный). " +
                      (nt == null ? "! NetworkTransform не найден — добавь руками (Client Authoritative)." : ""), go);
        }

        // ───────── v23.52: труба и кольца для противня макарон ─────────

        /// <summary>Размер кольца — как у противня (префаб у менеджера, иначе в сцене), иначе 0.18 м.</summary>
        private static void RingDims(out float ri, out float ro, out float h, out int count, StringBuilder log)
        {
            MacaronTray tray = null;
            BuildManager bm = Object.FindAnyObjectByType<BuildManager>(FindObjectsInactive.Include);
            if (bm != null)
            {
                Object pf = new SerializedObject(bm).FindProperty("_macaronTrayPrefab").objectReferenceValue;
                if (pf is NetworkObject nob) tray = nob.GetComponent<MacaronTray>();
            }
            if (tray == null) tray = Object.FindAnyObjectByType<MacaronTray>(FindObjectsInactive.Include);
            if (tray != null && tray.RingCount > 0)
            {
                ri = tray.RingRadius * Mathf.Abs(tray.transform.lossyScale.x);
                count = tray.RingCount;
                log.AppendLine($"  размер — по противню «{tray.name}»: колец {count}, Ø{ri * 2f:0.000} м");
            }
            else
            {
                ri = 0.09f; count = 12;
                log.AppendLine("  ⚠ противня для макарон нет — кольцо Ø0.18 м, 12 шт. (Макарон → 5 сделает противень)");
            }
            ro = ri * 1.14f; h = ri * 0.32f;
        }

        private static GameObject FindPrefabWith<T>() where T : Component
        {
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null && p.GetComponent<T>() != null) return p;
            }
            return null;
        }

        private static void AddNet(GameObject go, StringBuilder log)
        {
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0) go.layer = layer;
            if (go.GetComponent<NetworkObject>() == null) go.AddComponent<NetworkObject>();
            System.Type nt = System.Type.GetType("FishNet.Component.Transforming.NetworkTransform, FishNet.Runtime");
            if (nt != null)
            {
                Component c = go.GetComponent(nt) ?? go.AddComponent(nt);
                var so = new SerializedObject(c);
                SerializedProperty ca = so.FindProperty("_clientAuthoritative");
                if (ca != null) { ca.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            else log.AppendLine("  ! NetworkTransform не найден — добавь руками (Client Authoritative)");
        }

        [MenuItem("CatCooks/Стройка/4. Труба для колец макарон в сцену (резать пилой)")]
        public static void MakePipe()
        {
            var log = new StringBuilder("[Стройка] труба для колец:\n");
            RingDims(out float ri, out float ro, out float h, out int count, log);
            GameObject ringPrefab = FindPrefabWith<PipeRing>();
            Material mat = CatCooksDessertSetup.PlateMat(log);
            int rings = count + 1;                                               // одно — про запас (криво отпилил)
            var go = new GameObject("PipeForRings (Труба для колец)");
            Undo.RegisterCreatedObjectUndo(go, "Труба для колец");
            SceneView sv = SceneView.lastActiveSceneView;
            go.transform.position = (sv != null ? sv.pivot : Vector3.zero) + Vector3.up * (ro + 0.05f);
            AddNet(go, log);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(rings * h * ro * 60f, 1f, 12f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            BoxCollider bc = go.AddComponent<BoxCollider>();
            float L = rings * h + h * 0.4f;
            bc.size = new Vector3(ro * 2f, ro * 2f, L);
            PipeItem pipe = go.AddComponent<PipeItem>();
            pipe.EditorSetup(ri, ro, h, rings, ringPrefab, mat);
            // уже стоящие трубы — подставить кольцо-префаб
            int fixedN = 0;
            if (ringPrefab != null)
                foreach (PipeItem other in Object.FindObjectsByType<PipeItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var so = new SerializedObject(other);
                    SerializedProperty rp = so.FindProperty("_ringPrefab");
                    if (rp != null && rp.objectReferenceValue == null) { rp.objectReferenceValue = ringPrefab; so.ApplyModifiedProperties(); fixedN++; }
                }
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"  ✔ труба: {rings} колец (Ø{ro * 2f:0.000} м, высота кольца {h:0.000} м, длина {L:0.00} м), стальная, торцы жёлтые");
            log.AppendLine(ringPrefab != null ? $"  ✔ Ring Prefab = «{ringPrefab.name}»" + (fixedN > 1 ? $" (и ещё у {fixedN - 1} труб)" : "")
                                              : "  ✖ префаба кольца нет: меню «Стройка → 5», сохрани объект префабом, добавь в Spawnable Prefabs и нажми это меню ещё раз (или перетащи в Pipe Item → Ring Prefab)");
            log.AppendLine("  Пилой или бензопилой целься в конец трубы — каждый распил отваливает кольцо. Сохрани сцену.");
            Debug.Log(log.ToString(), go);
        }

        [MenuItem("CatCooks/Стройка/5. Кольцо из трубы (объект для префаба)")]
        public static void MakeRing()
        {
            var log = new StringBuilder("[Стройка] кольцо из трубы:\n");
            RingDims(out float ri, out float ro, out float h, out _, log);
            var go = new GameObject("PipeRing");
            Undo.RegisterCreatedObjectUndo(go, "Кольцо из трубы");
            SceneView sv = SceneView.lastActiveSceneView;
            go.transform.position = (sv != null ? sv.pivot : Vector3.zero) + Vector3.up * 0.2f;
            AddNet(go, log);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(ro * 2f, h, ro * 2f);
            go.AddComponent<PipeRing>().EditorSetup(ri, ro, h, CatCooksDessertSetup.PlateMat(log));
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"  ✔ «PipeRing» Ø{ro * 2f:0.000} м, высота {h:0.000} м (модель строится при запуске)");
            log.AppendLine("  ДАЛЬШЕ: перетащи PipeRing из Hierarchy в папку Project (префаб), удали из сцены, добавь префаб в Spawnable Prefabs " +
                           "(DefaultPrefabObjects) и нажми «Стройка → 4» — труба подхватит его сама.");
            Debug.Log(log.ToString(), go);
        }

        // ───────── v23.2: опалубка — в руках устойчиво, перевернул — всё вылилось ─────────

        [MenuItem("CatCooks/Стройка/3. Опалубка: устойчиво в руках, выливается при перевороте")]
        public static void FormworkSpill()
        {
            var log = new StringBuilder("[Стройка] опалубка — выливание:\n");
            CookwareSpill sample = null;
            foreach (CookwareSpill sp in Object.FindObjectsByType<CookwareSpill>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(sp);
                SerializedProperty pp = so.FindProperty("_puddlePrefab");
                if (pp != null && pp.objectReferenceValue != null) { sample = sp; break; }
            }
            Object puddle = null;
            if (sample != null) puddle = new SerializedObject(sample).FindProperty("_puddlePrefab").objectReferenceValue;
            log.AppendLine(puddle != null ? $"  лужа — как у «{sample.name}»" : "  ! лужи (Puddle Prefab) ни у одной тары в сцене нет — вылитое просто пропадёт");

            int n = 0;
            foreach (BakeTray bt in Object.FindObjectsByType<BakeTray>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (bt.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(bt.gameObject))
                {
                    Undo.RegisterFullObjectHierarchyUndo(bt.gameObject, "Опалубка — выливание");
                    log.AppendLine($"  ✔ «{bt.name}» (сцена): {SetupFormwork(bt.gameObject, puddle)}");
                    EditorSceneManager.MarkSceneDirty(bt.gameObject.scene);
                    n++;
                }
            }
            // префаб опалубки из менеджера стройки (сам префаб не создаём — меняем существующий)
            BuildManager bm = Object.FindAnyObjectByType<BuildManager>();
            if (bm != null)
            {
                Object pf = new SerializedObject(bm).FindProperty("_formworkPrefab").objectReferenceValue;
                string path = pf != null ? AssetDatabase.GetAssetPath(pf) : null;
                if (!string.IsNullOrEmpty(path))
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        log.AppendLine($"  ✔ префаб «{path}»: {SetupFormwork(root, puddle)}");
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        n++;
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                else log.AppendLine("  • у менеджера стройки нет Formwork Prefab — правлю только сцену");
            }
            if (n == 0) log.AppendLine("  ! опалубок (BakeTray) не найдено");
            log.AppendLine("  Сохрани сцену (Ctrl+S).");
            Debug.Log(log.ToString());
        }

        private static string SetupFormwork(GameObject go, Object puddle)
        {
            var sb = new StringBuilder();
            CookwareSpill sp = go.GetComponent<CookwareSpill>();
            if (sp == null) { sp = go.AddComponent<CookwareSpill>(); sb.Append("добавлен Cookware Spill; "); }
            var so = new SerializedObject(sp);
            SetObj(so, "_puddlePrefab", puddle, false);
            SetF(so, "_minPourTilt", 70f);        // несёшь, чуть наклонил — не льётся
            SetF(so, "_sloshLossScale", 0f);      // плеск при ходьбе — без потерь
            SetF(so, "_drainTilt", 85f);
            SetF(so, "_dumpTilt", 100f);          // перевернул — всё вылилось
            SetF(so, "_dumpSeconds", 1.5f);
            so.ApplyModifiedPropertiesWithoutUndo();
            CookwareLiquidVisual lv = go.GetComponentInChildren<CookwareLiquidVisual>(true);
            if (lv != null)
            {
                var lo = new SerializedObject(lv);
                SetF(lo, "_sloshGain", 0.35f);
                SetF(lo, "_bounceGain", 0.3f);
                SetF(lo, "_motionDeadzone", 3f);
                lo.ApplyModifiedPropertiesWithoutUndo();
                sb.Append("жидкость устойчивее; ");
            }
            else sb.Append("! нет Cookware Liquid Visual внутри; ");
            sb.Append("переворот > 100° — выливается");
            return sb.ToString();
        }

        private static void SetF(SerializedObject so, string name, float v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.floatValue = v;
        }

        private static void SetObj(SerializedObject so, string name, Object v, bool overwrite)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null && v != null && (overwrite || p.objectReferenceValue == null)) p.objectReferenceValue = v;
        }
    }
}
