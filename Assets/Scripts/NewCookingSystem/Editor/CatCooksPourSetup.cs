#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using CatCooks.Food.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Food
{
    /// <summary>
    /// СТРУИ — быстрая установка (Pour v1).
    ///
    /// CatCooks → Струи →
    ///   1. Подключить шейдеры — добавляет Liquid Stream / Splash / Surface в
    ///      Always Included и ищет одноимённые шейдеры (конфликт со старой системой).
    ///   2. Настроить выделенное — бидон (Milk Can Item) и/или ведро (Cookware Spill):
    ///      носик, ссылки, рекомендуемые значения. Работает и с объектом в сцене,
    ///      и с префабом в окне Project.
    ///   3. Витрина струй — объект Pour Showcase на 1.2 м перед камерой сцены.
    ///   4. Найти старую систему жидкостей — где в сценах и префабах ещё висят
    ///      компоненты jijasimulation (PotSpill, PotLiquidVisual, LiquidStream...).
    ///      Запусти ПЕРЕД удалением папки, чтобы не получить Missing Script.
    /// </summary>
    public static class CatCooksPourSetup
    {
        private static readonly string[] Shaders =
        {
            "CatCooks/Liquid Stream", "CatCooks/Liquid Splash", "CatCooks/Liquid Surface",
        };

        // ───────────────────────── 1. ШЕЙДЕРЫ ─────────────────────────

        [MenuItem("CatCooks/Струи/1. Подключить шейдеры", priority = 1)]
        public static void IncludeShaders()
        {
            var log = new StringBuilder();
            var gs = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SerializedProperty list = gs.FindProperty("m_AlwaysIncludedShaders");
            int added = 0;

            foreach (string name in Shaders)
            {
                List<Shader> found = FindShadersByName(name);
                if (found.Count == 0) { log.AppendLine($"✖ {name} — не найден в проекте"); continue; }
                if (found.Count > 1)
                {
                    log.AppendLine($"⚠ {name} — ОДНОИМЁННЫХ ШЕЙДЕРОВ {found.Count}, Shader.Find вернёт любой:");
                    foreach (Shader s in found) log.AppendLine("     " + AssetDatabase.GetAssetPath(s));
                }

                Shader sh = found[0];
                bool already = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == sh) { already = true; break; }
                if (already) { log.AppendLine($"✔ {name} — уже в Always Included"); continue; }

                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = sh;
                added++;
                log.AppendLine($"＋ {name} — добавлен");
            }

            gs.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[Струи] Шейдеры:\n" + log);
            EditorUtility.DisplayDialog("Струи — шейдеры",
                (added > 0 ? $"Добавлено: {added}\n\n" : "") + log, "OK");
        }

        private static List<Shader> FindShadersByName(string name)
        {
            var res = new List<Shader>();
            foreach (string guid in AssetDatabase.FindAssets("t:Shader"))
            {
                Shader s = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null && s.name == name) res.Add(s);
            }
            return res;
        }

        // ───────────────────────── 2. БИДОН / ВЕДРО ─────────────────────────

        [MenuItem("CatCooks/Струи/2. Настроить выделенное (бидон, ведро)", priority = 2)]
        public static void SetupSelected()
        {
            GameObject[] sel = Selection.gameObjects;
            if (sel.Length == 0)
            {
                EditorUtility.DisplayDialog("Струи", "Выдели бидон или ведро — в сцене или префаб в Project.", "OK");
                return;
            }

            var log = new StringBuilder();
            foreach (GameObject go in sel)
            {
                if (EditorUtility.IsPersistent(go))
                {
                    // Префаб в окне Project: открыть содержимое, настроить, сохранить.
                    string path = AssetDatabase.GetAssetPath(go);
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        bool changed = SetupOne(root, log, false);
                        if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                else SetupOne(go, log, true);
            }

            Debug.Log("[Струи] Настройка:\n" + log);
            EditorUtility.DisplayDialog("Струи — настройка", log.Length > 0 ? log.ToString() : "Нечего настраивать.", "OK");
        }

        [MenuItem("CatCooks/Струи/2. Настроить выделенное (бидон, ведро)", true)]
        private static bool SetupSelectedValidate() => Selection.gameObjects.Length > 0;

        private static bool SetupOne(GameObject go, StringBuilder log, bool undo)
        {
            bool changed = false;
            MilkCanItem can = go.GetComponentInChildren<MilkCanItem>(true);
            CookwareSpill spill = go.GetComponentInChildren<CookwareSpill>(true);

            if (can == null && spill == null)
            {
                log.AppendLine($"«{go.name}»: нет ни Milk Can Item, ни Cookware Spill — пропущен.");
                return false;
            }
            if (can != null) changed |= SetupCan(can, log, undo);
            if (spill != null) changed |= SetupBucket(spill, log, undo);
            return changed;
        }

        private static bool SetupCan(MilkCanItem can, StringBuilder log, bool undo)
        {
            log.AppendLine($"БИДОН «{can.name}»:");
            var so = new SerializedObject(can);
            SerializedProperty spout = so.FindProperty("_spout");

            if (spout.objectReferenceValue == null)
            {
                Transform t = FindChild(can.transform, "Spout", "Mouth", "Носик");
                if (t == null)
                {
                    t = new GameObject("Spout").transform;
                    if (undo) Undo.RegisterCreatedObjectUndo(t.gameObject, "Spout");
                    t.SetParent(can.transform, false);
                    // Верх модели, край горла со стороны наклона (бидон клонится вперёд, к синей оси).
                    Bounds b = LocalBounds(can.transform);
                    float neckR = 0.3f * Mathf.Min(b.extents.x, b.extents.z);
                    t.localPosition = new Vector3(b.center.x, b.max.y, b.center.z + neckR);
                    t.localRotation = Quaternion.identity;
                    log.AppendLine("  ＋ создан Spout на краю горла (проверь: синяя ось — куда льётся, красная — вдоль края)");
                }
                else log.AppendLine($"  ✔ найден носик «{t.name}»");
                spout.objectReferenceValue = t;
            }
            else log.AppendLine("  ✔ Spout уже назначен");

            SerializedProperty milk = so.FindProperty("_milk");
            if (milk != null && milk.objectReferenceValue == null)
                log.AppendLine("  ⚠ Milk (ProductDef) пусто — струя видна, но молоко никуда не попадёт");

            SerializedProperty neck = so.FindProperty("_neckWidth");
            if (neck != null && neck.floatValue <= 0f) { neck.floatValue = 0.05f; log.AppendLine("  · Neck Width = 0.05"); }

            if (so.FindProperty("_puddlePrefab")?.objectReferenceValue == null)
                log.AppendLine("  · Puddle Prefab пусто — мимо тары молоко просто пропадает (лужи не будет)");

            so.ApplyModifiedProperties();   // сам пишет Undo
            return true;
        }

        private static bool SetupBucket(CookwareSpill spill, StringBuilder log, bool undo)
        {
            log.AppendLine($"ВЕДРО «{spill.name}»:");
            var so = new SerializedObject(spill);

            SerializedProperty liquid = so.FindProperty("_liquid");
            if (liquid.objectReferenceValue == null)
            {
                CookwareLiquidVisual lv = spill.GetComponentInChildren<CookwareLiquidVisual>(true);
                if (lv != null) { liquid.objectReferenceValue = lv; log.AppendLine($"  ＋ Liquid → «{lv.name}»"); }
                else log.AppendLine("  ✖ нет Cookware Liquid Visual (дочерний LiquidAnchor) — струи не будет");
            }
            else log.AppendLine("  ✔ Liquid назначен");

            Set(so, "_minPourTilt", 40f, log);
            Set(so, "_lipSpread", 0.8f, log);
            Set(so, "_streamWidth", 0.06f, log);

            if (spill.GetComponent<Cookware>() == null) log.AppendLine("  ✖ нет Cookware на том же объекте");
            if (spill.GetComponent<CarriedContainer>() == null)
                log.AppendLine("  ⚠ нет Carried Container (вместо Pickup Item) — ПКМ не будет наклонять ведро");
            if (so.FindProperty("_puddlePrefab")?.objectReferenceValue == null)
                log.AppendLine("  · Puddle Prefab пусто — вылитое на пол просто пропадёт");
            log.AppendLine("  → на LiquidAnchor: ⋮ → «Подогнать под стенки модели», если ещё не делал");

            so.ApplyModifiedProperties();   // сам пишет Undo
            return true;
        }

        private static void Set(SerializedObject so, string prop, float v, StringBuilder log)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p == null) { log.AppendLine($"  ✖ нет поля {prop} — CookwareSpill старой версии?"); return; }
            if (Mathf.Abs(p.floatValue - v) < 1e-4f) return;
            log.AppendLine($"  · {prop}: {p.floatValue:0.###} → {v:0.###}");
            p.floatValue = v;
        }

        private static Transform FindChild(Transform root, params string[] names)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                foreach (string n in names)
                    if (t != root && t.name.Equals(n, System.StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        /// <summary>Габариты всех мешей в осях корня.</summary>
        private static Bounds LocalBounds(Transform root)
        {
            bool any = false;
            Bounds res = new Bounds(Vector3.zero, Vector3.one * 0.3f);
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { res = new Bounds(p, Vector3.zero); any = true; }
                    else res.Encapsulate(p);
                }
            }
            return res;
        }

        // ───────────────────────── 3. ВИТРИНА ─────────────────────────

        [MenuItem("CatCooks/Струи/3. Витрина струй в сцене", priority = 3)]
        public static void CreateShowcase()
        {
            var go = new GameObject("PourShowcase");
            Undo.RegisterCreatedObjectUndo(go, "Витрина струй");
            Vector3 pos = new Vector3(0f, 1.2f, 0f);
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                Vector3 f = Vector3.ProjectOnPlane(sv.camera.transform.forward, Vector3.up).normalized;
                pos = sv.camera.transform.position + f * 4f;
                pos.y = 1.2f;
            }
            go.transform.position = pos;
            go.AddComponent<PourShowcase>();
            Selection.activeGameObject = go;
            Debug.Log("[Струи] Витрина создана — жми Play.", go);
        }
        // ───────────────────────── 4. СТАРАЯ СИСТЕМА ─────────────────────────

        // GUID скриптов, шейдеров и ассета старой папки instruments/jijasimulation.
        private static readonly Dictionary<string, string> OldGuids = new Dictionary<string, string>
        {
            { "9554b2fcbc219284fac45c2aefd4ebaa", "LiquidBlender.cs" },
            { "b3b025e2b24a0334db1999da52dea6ce", "LiquidPreset.cs" },
            { "eaca06bfab71c4248af47af1121b8e49", "LiquidPresetLibrary.cs" },
            { "11b83184caf605f48b92f0c7034f9f29", "LiquidPresetMap.cs" },
            { "a391ea6c96bacb248b8f75f9136f6009", "LiquidPresetMap.asset" },
            { "8ce679c8ec470a44e81cf8547dbdd650", "LiquidPuddle.cs (старая)" },
            { "af54682ec9cd6de498b1485cccb29215", "LiquidStream.cs" },
            { "2a8b73629f1ac644086eb93ee20709aa", "LiquidTestConsole.cs" },
            { "52f9bbe47b9b79d4089e834e9e422b7b", "PotLiquidVisual.cs" },
            { "489dcdcd5213eb14f85790ea1c162af7", "PotSpill.cs" },
            { "b5006d91a08bdca4dabaa43bb5ac8826", "CatCooks_LiquidBody.shader" },
            { "326a7ab135a3bfd43a2a71e89257b065", "CatCooks_LiquidPuddle.shader" },
            { "372e3ffde1def9b41b98f0716281b55e", "CatCooks_LiquidStream.shader (старая)" },
        };

        [MenuItem("CatCooks/Струи/4. Найти старую систему жидкостей (перед удалением)", priority = 20)]
        public static void FindOldLiquidSystem()
        {
            var log = new StringBuilder();
            int files = 0, hits = 0;
            string[] exts = { ".unity", ".prefab", ".asset", ".mat", ".controller" };
            foreach (string path in System.IO.Directory.GetFiles("Assets", "*.*", System.IO.SearchOption.AllDirectories))
            {
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (System.Array.IndexOf(exts, ext) < 0) continue;
                string p = path.Replace('\\', '/');
                if (p.Contains("/jijasimulation/")) continue;               // сама старая папка
                files++;
                string text;
                try { text = System.IO.File.ReadAllText(p); } catch { continue; }
                if (text.Length > 0 && text[0] != '%') continue;           // бинарный ассет — не YAML
                var found = new List<string>();
                foreach (var kv in OldGuids)
                    if (text.Contains(kv.Key)) found.Add(kv.Value);
                if (found.Count == 0) continue;
                hits++;
                log.AppendLine($"• {p}\n     {string.Join(", ", found)}");
            }

            string head = hits == 0
                ? $"Чисто: в {files} сценах/префабах/ассетах нет ссылок на старую систему.\n\n" +
                  "Можно удалять папку instruments/jijasimulation (при закрытом Unity, вместе с .meta).\n" +
                  "градусник.fbx перед этим перенеси в instruments/Plita вместе с .meta — его использует градусник.prefab."
                : $"Ссылки на старую систему: {hits} файл(ов). Убери эти компоненты/материалы перед удалением, " +
                  "иначе будет Missing Script:\n\n";
            Debug.Log("[Струи] Старая система жидкостей:\n" + head + log);
            EditorUtility.DisplayDialog("Старая система жидкостей", head + log, "OK");
        }
    }
}
#endif
