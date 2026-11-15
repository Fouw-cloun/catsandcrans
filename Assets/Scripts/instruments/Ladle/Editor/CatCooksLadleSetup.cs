using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food.Tools.EditorTools
{
    /// <summary>
    /// v23: ВЕДРО-ЧЕРПАК в сцену. Префаб не создаёт — только объект сцены у камеры.
    /// Модель (красное конусное ведро + арматура) рисуется сама при запуске.
    /// </summary>
    public static class CatCooksLadleSetup
    {
        [MenuItem("CatCooks/Инструменты/Ведро-черпак в сцену")]
        public static void MakeLadle()
        {
            var go = new GameObject("FireBucketLadle (Ведро-черпак)");
            Undo.RegisterCreatedObjectUndo(go, "Ведро-черпак");
            SceneView sv = SceneView.lastActiveSceneView;
            go.transform.position = (sv != null ? sv.pivot : Vector3.zero) + Vector3.up * 0.3f;
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
            rb.mass = 1.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // коллайдеры под авто-модель (ручка 0.42 м по +Z, ведро R 0.085, H 0.15)
            BoxCollider hb = go.AddComponent<BoxCollider>();
            hb.center = new Vector3(0f, 0.02f, 0.21f);
            hb.size = new Vector3(0.03f, 0.03f, 0.42f);
            BoxCollider bb = go.AddComponent<BoxCollider>();
            bb.center = new Vector3(0f, 0.02f, 0.505f);
            bb.size = new Vector3(0.15f, 0.15f, 0.15f);
            go.AddComponent<LadleItem>();
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[Черпак] ведро-черпак поставлен у камеры сцены. Модель рисуется сама при запуске. " +
                      "ЛКМ по таре — зачерпнуть/вылить, мимо тары — на пол, ПКМ — попробовать. Сохрани сцену. " +
                      (nt == null ? "! NetworkTransform не найден — добавь руками (Client Authoritative)." : ""), go);
        }
    }
}
