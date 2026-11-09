using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// КОСМИЧЕСКАЯ БАШНЯ (ориентир с подсветкой): выдели модель в Project (FBX) или дом в сцене → меню.
    ///   • модели включается Read/Write (чтобы найти сферы в меше);
    ///   • в списке домов генератора у неё включается Landmark Glow (новые генерации ставят подсветку сами);
    ///   • на все уже стоящие такие дома (B_<имя>) вешается CityLandmarkGlow.
    /// </summary>
    public static class CityLandmarkTool
    {
        public const string MatPath = "Assets/Generated/City/CC_LandmarkGlow.mat";

        [MenuItem("CatCooks/Застройка/Космическая башня: подсветка (выдели модель или дом)", priority = 958)]
        public static void Mark()
        {
            GameObject sel = Selection.activeGameObject;
            if (sel == null) { Debug.LogWarning("[Ориентир] выдели модель башни в Project или её дом в сцене"); return; }
            GameObject prefab = sel;
            string name = sel.name;
            if (!EditorUtility.IsPersistent(sel))
            {
                Transform t = sel.transform;
                while (t != null && !t.name.StartsWith("B_")) t = t.parent;
                if (t != null) name = t.name.Substring(2);
                Object src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(sel);
                prefab = src as GameObject;
            }
            Material mat = EnsureMaterial();

            // Read/Write у модели
            string path = prefab != null ? AssetDatabase.GetAssetPath(prefab) : null;
            if (!string.IsNullOrEmpty(path))
            {
                ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi != null && !mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); Debug.Log("[Ориентир] «" + name + "»: включён Read/Write (для поиска сфер)"); }
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                name = prefab != null ? prefab.name : name;
            }

            // в список домов генератора
            int marked = 0;
            CityBlockGenerator g = Object.FindFirstObjectByType<CityBlockGenerator>();
            if (g != null)
            {
                Undo.RecordObject(g, "Ориентир");
                g.LandmarkGlowMaterial = mat;
                foreach (CityLayer l in new[] { g.Layer1, g.Layer2 })
                    foreach (BuildingEntry e in l.Buildings)
                        if (e != null && e.Prefab != null && (e.Prefab == prefab || e.Prefab.name == name)) { e.LandmarkGlow = true; marked++; }
                EditorUtility.SetDirty(g);
            }

            // на уже стоящие дома
            int placed = 0;
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name != "B_" + name) continue;
                CityLandmarkGlow lg = t.GetComponent<CityLandmarkGlow>();
                if (lg == null) lg = Undo.AddComponent<CityLandmarkGlow>(t.gameObject);
                lg.GlowMaterial = mat;
                lg.Apply();
                placed++;
            }
            Debug.Log("[Ориентир] «" + name + "»: в списке домов отмечено " + marked + ", подсветка на стоящих домах — " + placed +
                      ". Верхние окна горят всегда, сферы светятся (настройки — компонент City Landmark Glow на доме)");
        }

        private static Material EnsureMaterial()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m != null) return m;
            Shader sh = Shader.Find("CatCooks/City/Landmark Glow");
            if (sh == null) return null;
            if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
            if (!AssetDatabase.IsValidFolder("Assets/Generated/City")) AssetDatabase.CreateFolder("Assets/Generated", "City");
            m = new Material(sh) { name = "CC_LandmarkGlow" };
            AssetDatabase.CreateAsset(m, MatPath);
            return m;
        }
    }
}
