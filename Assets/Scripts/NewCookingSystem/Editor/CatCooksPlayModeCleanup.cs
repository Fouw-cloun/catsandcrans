#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Струи, пена, брызги и прочие «(auto)»-объекты создаются в Play с флагом DontSave.
    /// Такие объекты Unity НЕ удаляет при выходе из Play — они оставались висеть
    /// на сцене застывшими струями. После выхода из Play этот скрипт их убирает.
    /// Трогает только корневые объекты с «(auto)» в имени и флагом DontSave.
    /// </summary>
    [InitializeOnLoad]
    internal static class CatCooksPlayModeCleanup
    {
        static CatCooksPlayModeCleanup()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredEditMode) return;
            int n = 0;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.transform.parent != null) continue;
                if ((go.hideFlags & HideFlags.DontSaveInEditor) == 0) continue;
                if (EditorUtility.IsPersistent(go)) continue;               // ассет/префаб — не трогаем
                if (!go.name.Contains("(auto)")) continue;
                Object.DestroyImmediate(go);
                n++;
            }
            if (n > 0) Debug.Log($"[Струи] После Play убрано авто-объектов: {n}");
        }
    }
}
#endif
