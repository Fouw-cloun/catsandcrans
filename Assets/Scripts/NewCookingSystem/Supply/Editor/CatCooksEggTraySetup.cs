using System.Text;
using CatCooks.Food.Supply;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;

namespace CatCooks.Food.SupplyEditor
{
    /// <summary>
    /// v25.2: ЯЩИК ДЛЯ ЯИЦ — меню CatCooks → Доставка → 5б.
    /// Берёт префаб яйца (EggItem) из папки базовых префабов и собирает «EggCrate»: длинный деревянный ящик
    /// 8×3 ячейки в 2 слоя = 48 яиц НАСТОЯЩЕГО размера (между слоями — картонная прокладка), SupplyEggTray
    /// (F — яйцо в руку, верхний слой пустеет первым). Едет на площадке. Потом «Доставка → 3».
    ///
    /// v25.2: размер яйца считается С МАСШТАБОМ корня префаба (раньше его теряли — яйца в лотке были крошечные).
    /// </summary>
    public static class CatCooksEggTraySetup
    {
        const string Root = "Assets/CatCooks/Supply";
        const int Cols = 8, Rows = 3, Layers = 2;
        const float Floor = 0.08f, Board = 0.05f, Sheet = 0.03f;

        [MenuItem("CatCooks/Доставка/5б. Ящик для яиц (48 шт., полный размер) из префаба яйца", priority = 5)]
        public static void Build()
        {
            var log = new StringBuilder("ДОСТАВКА → 5б. Ящик для яиц\n");
            SupplyCatalog cat = AssetDatabase.LoadAssetAtPath<SupplyCatalog>(Root + "/Resources/SupplyCatalog.asset");
            string folder = cat != null && !string.IsNullOrEmpty(cat.BaseFolder) ? cat.BaseFolder.TrimEnd('/') : Root + "/BasePrefabs";
            if (!AssetDatabase.IsValidFolder(folder)) { log.AppendLine($"✖ нет папки {folder} — сначала «Доставка → 3»"); Debug.Log(log); return; }

            // 1. префаб яйца в папке
            GameObject egg = null;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                GameObject pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (pf == null || pf.GetComponent<NetworkObject>() == null) continue;
                foreach (MonoBehaviour mb in pf.GetComponents<MonoBehaviour>())
                    if (mb != null && mb.GetType().Name == "EggItem") { egg = pf; break; }
                if (egg != null) break;
            }
            if (egg == null) { log.AppendLine($"✖ в {folder} нет префаба яйца (EggItem + NetworkObject)"); Debug.Log(log); return; }
            log.AppendLine($"  яйцо: {AssetDatabase.GetAssetPath(egg)}");

            // 2. меш яйца в осях корня, НО С масштабом корня (как яйцо выглядит в мире)
            MeshFilter emf = null;
            foreach (MeshFilter mf in egg.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) { emf = mf; break; }
            if (emf == null) { log.AppendLine("✖ у яйца нет меша"); Debug.Log(log); return; }
            MeshRenderer emr = emf.GetComponent<MeshRenderer>();
            Transform er = egg.transform;
            Matrix4x4 m = Matrix4x4.Rotate(Quaternion.Inverse(er.rotation)) * Matrix4x4.Translate(-er.position) * emf.transform.localToWorldMatrix;
            Bounds eb = new Bounds(); bool any = false;
            Bounds mb0 = emf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb0.center + Vector3.Scale(mb0.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 w = m.MultiplyPoint3x4(c);
                if (!any) { eb = new Bounds(w, Vector3.zero); any = true; } else eb.Encapsulate(w);
            }
            float cell = Mathf.Max(eb.size.x, eb.size.z) + 0.06f;
            float eggH = eb.size.y;
            float layerH = eggH + Sheet;
            log.AppendLine($"  размер яйца: {eb.size.x:0.00}×{eb.size.y:0.00}×{eb.size.z:0.00} м");

            // 3. ящик: длинный, низкий, стенки до середины верхнего слоя — яйца видно
            float W = Cols * cell + Board * 2f, D = Rows * cell + Board * 2f;
            float wallH = Floor + layerH * (Layers - 1) + eggH * 0.55f;
            Material wood = Mat("M_EggCrate_Wood", new Color(0.74f, 0.52f, 0.3f));
            Material dark = Mat("M_EggCrate_Board", new Color(0.5f, 0.33f, 0.18f));
            Material card = Mat("M_EggTray_Cardboard", new Color(0.72f, 0.6f, 0.44f));
            var go = new GameObject("EggCrate");
            try
            {
                DS.AddByName(go, "NetworkObject", log);
                DS.AddByName(go, "NetworkTransform", log);
                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass = 40f; rb.interpolation = RigidbodyInterpolation.Interpolate;

                Part(go, "Floor", new Vector3(0f, Floor * 0.5f, 0f), new Vector3(W, Floor, D), dark);
                Part(go, "Wall +Z", new Vector3(0f, wallH * 0.5f, D * 0.5f - Board * 0.5f), new Vector3(W, wallH, Board), wood);
                Part(go, "Wall -Z", new Vector3(0f, wallH * 0.5f, -D * 0.5f + Board * 0.5f), new Vector3(W, wallH, Board), wood);
                Part(go, "Wall +X", new Vector3(W * 0.5f - Board * 0.5f, wallH * 0.5f, 0f), new Vector3(Board, wallH, D), wood);
                Part(go, "Wall -X", new Vector3(-W * 0.5f + Board * 0.5f, wallH * 0.5f, 0f), new Vector3(Board, wallH, D), wood);
                // рейки-ручки по длинным бокам
                Part(go, "Rail +Z", new Vector3(0f, wallH * 0.7f, D * 0.5f + 0.03f), new Vector3(W * 0.96f, 0.08f, 0.06f), dark);
                Part(go, "Rail -Z", new Vector3(0f, wallH * 0.7f, -D * 0.5f - 0.03f), new Vector3(W * 0.96f, 0.08f, 0.06f), dark);
                for (int l = 1; l < Layers; l++)                                  // картон между слоями
                    Part(go, $"Sheet {l}", new Vector3(0f, Floor + layerH * l - Sheet * 0.5f, 0f), new Vector3(W - Board * 2f, Sheet, D - Board * 2f), card);

                BoxCollider bc = go.AddComponent<BoxCollider>();
                float h = Floor + layerH * (Layers - 1) + eggH;
                bc.center = new Vector3(0f, h * 0.5f, 0f);
                bc.size = new Vector3(W, h, D);

                var slots = new GameObject("Slots").transform;
                slots.SetParent(go.transform, false);
                Vector3 epos = m.GetColumn(3);
                Quaternion erot = m.rotation;
                Vector3 escale = m.lossyScale;
                int idx = 0;
                for (int l = 0; l < Layers; l++)                                  // нижний слой — первые номера: пустеет верхний
                    for (int r = 0; r < Rows; r++)
                        for (int c = 0; c < Cols; c++)
                        {
                            var s = new GameObject($"Egg {++idx}").transform;
                            s.SetParent(slots, false);
                            s.localPosition = new Vector3((c - (Cols - 1) * 0.5f) * cell, Floor + layerH * l - eb.min.y, (r - (Rows - 1) * 0.5f) * cell)
                                              - new Vector3(eb.center.x, 0f, eb.center.z);
                            var e = new GameObject("Mesh");
                            e.transform.SetParent(s, false);
                            e.transform.localPosition = epos;
                            e.transform.localRotation = erot;
                            e.transform.localScale = escale;
                            e.AddComponent<MeshFilter>().sharedMesh = emf.sharedMesh;
                            var rr = e.AddComponent<MeshRenderer>();
                            if (emr != null) rr.sharedMaterials = emr.sharedMaterials;
                        }

                SupplyEggTray tray = go.AddComponent<SupplyEggTray>();
                var so = new SerializedObject(tray);
                so.FindProperty("_product").objectReferenceValue = DS.Find("Яйцо");
                so.FindProperty("_eggPrefab").objectReferenceValue = egg.GetComponent<NetworkObject>();
                so.FindProperty("_capacity").intValue = Cols * Rows * Layers;
                so.FindProperty("_slots").objectReferenceValue = slots;
                so.ApplyModifiedPropertiesWithoutUndo();

                foreach (string old in new[] { folder + "/EggTray_30.prefab", folder + "/EggTray.prefab" })
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(old) != null) { AssetDatabase.DeleteAsset(old); log.AppendLine($"  − удалён старый {old}"); }
                string path = folder + "/EggCrate.prefab";
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
                if (!ok || saved == null) { log.AppendLine($"✖ не сохранился {path}"); Debug.Log(log); return; }
                log.AppendLine($"＋ {path}: {W:0.0}×{h:0.0}×{D:0.0} м, {Cols * Rows * Layers} яиц ({Cols}×{Rows}, {Layers} слоя)");
                Selection.activeObject = saved;
            }
            finally { Object.DestroyImmediate(go); }

            AssetDatabase.SaveAssets();
            log.AppendLine("\nДАЛЬШЕ: выдели папку базовых префабов → «Доставка → 3», потом Fish-Networking → Refresh Default Prefabs.");
            log.AppendLine("Яйца поедут ящиками по 48 на площадке; F по ящику — яйцо в руку.");
            Debug.Log(log);
        }

        static void Part(GameObject root, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = name;
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.transform.SetParent(root.transform, false);
            p.transform.localPosition = pos;
            p.transform.localScale = size;
            p.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Material Mat(string name, Color c)
        {
            string dir = Root + "/Materials", path = dir + "/" + name + ".mat";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root, "Materials");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(sh) { name = name };
            mat.SetColor("_BaseColor", c); mat.color = c;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
