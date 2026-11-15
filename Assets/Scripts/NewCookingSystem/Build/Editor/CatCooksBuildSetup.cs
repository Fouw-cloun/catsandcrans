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
