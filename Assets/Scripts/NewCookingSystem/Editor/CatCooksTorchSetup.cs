#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Горелки:
    ///   1. Настроить выделенное — на выделенных горелках/баллонах:
    ///      • газовая горелка (баллончик с трубкой, например «gaslighter»): вешает Blow Torch Item,
    ///        если его нет, создаёт дочерний «Nozzle» на конце трубки (синяя ось — куда бьёт пламя),
    ///        подставляет процесс карамелизации, если он есть в проекте;
    ///      • кровельная горелка: проверяет сопло (часть Bell), штуцер, баллон рядом;
    ///      • баллон: проверяет редуктор и штуцер шланга;
    ///      • всем — слой Interactable (иначе F может не брать), проверка NetworkObject/Rigidbody/Collider.
    ///   2. Сопло горелки — на другой конец трубки (если пламя пошло не с той стороны).
    /// Префабы не создаёт: правит выделенное, сохранишь сам.
    /// </summary>
    public static class CatCooksTorchSetup
    {
        [MenuItem("CatCooks/Горелки/1. Настроить выделенное (горелка, кровельная, баллон)", priority = 80)]
        public static void Setup()
        {
            var log = new StringBuilder();
            if (Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Горелки", "Выдели горелку, кровельную горелку и/или баллон (в сцене) и нажми ещё раз.", "Ок");
                return;
            }
            int interactable = LayerMask.NameToLayer("Interactable");
            foreach (GameObject go in Selection.gameObjects)
            {
                log.AppendLine($"— «{go.name}»");
                RoofingTorchItem roof = go.GetComponent<RoofingTorchItem>();
                TorchGasCylinder cyl = go.GetComponent<TorchGasCylinder>();
                BlowTorchItem blow = go.GetComponent<BlowTorchItem>();

                if (roof == null && cyl == null && blow == null)
                {
                    InteractableItem other = go.GetComponent<InteractableItem>();
                    if (other != null)
                    {
                        log.AppendLine($"  ✖ на нём уже {other.GetType().Name} — горелку не вешаю");
                        continue;
                    }
                    blow = Undo.AddComponent<BlowTorchItem>(go);
                    log.AppendLine("  ✔ добавлен Blow Torch Item (ЛКМ — огонь, ПКМ — фиксатор, G с фиксатором — поставить)");
                }

                CheckBasics(go, interactable, log);
                if (blow != null) SetupBlow(blow, log);
                if (roof != null) CheckRoof(roof, log);
                if (cyl != null) CheckCylinder(cyl, log);
                EditorUtility.SetDirty(go);
            }
            CheckHeatTargets(log);
            log.AppendLine("Сохрани сцену/префаб (Ctrl+S, Overrides → Apply).");
            Debug.Log("[Горелки]\n" + log);
            EditorUtility.DisplayDialog("Горелки", log.ToString(), "Ок");
        }

        [MenuItem("CatCooks/Горелки/2. Сопло горелки — на другой конец", priority = 81)]
        public static void FlipNozzle()
        {
            var log = new StringBuilder();
            foreach (GameObject go in Selection.gameObjects)
            {
                BlowTorchItem blow = go.GetComponentInParent<BlowTorchItem>();
                if (blow == null) continue;
                var so = new SerializedObject(blow);
                Transform n = so.FindProperty("_nozzle").objectReferenceValue as Transform;
                if (n == null || n == blow.transform) { log.AppendLine($"«{blow.name}»: нет сопла — сначала «1. Настроить выделенное»"); continue; }
                Bounds b = BoundsOf(blow.transform);
                Vector3 f = n.forward;
                Vector3 p = n.position;
                Undo.RecordObject(n, "Сопло на другой конец");
                n.position = p - 2f * Vector3.Dot(p - b.center, f) * f;
                n.rotation = Quaternion.LookRotation(-f, Vector3.up);
                log.AppendLine($"«{blow.name}»: сопло перенесено на другой конец (оранжевая линия гизмо — куда пламя)");
            }
            if (log.Length == 0) log.AppendLine("Выдели газовую горелку (с Blow Torch Item).");
            Debug.Log("[Горелки] " + log);
        }

        private static void CheckBasics(GameObject go, int interactable, StringBuilder log)
        {
            if (interactable >= 0 && go.layer != interactable)
            {
                Undo.RecordObject(go, "Слой Interactable");
                go.layer = interactable;
                log.AppendLine("  ✔ слой → Interactable (как у остальных предметов — F берёт)");
            }
            if (go.GetComponent<FishNet.Object.NetworkObject>() == null) log.AppendLine("  ✖ нет NetworkObject — по сети работать не будет");
            if (go.GetComponent<Rigidbody>() == null) log.AppendLine("  ✖ нет Rigidbody");
            if (go.GetComponentInChildren<Collider>() == null) log.AppendLine("  ✖ нет коллайдера — не взять");
        }

        private static void SetupBlow(BlowTorchItem blow, StringBuilder log)
        {
            var so = new SerializedObject(blow);
            SerializedProperty nz = so.FindProperty("_nozzle");
            Transform n = nz.objectReferenceValue as Transform;
            if (n == null || n == blow.transform)
            {
                if (BlowTorchItem.GuessNozzlePose(blow.transform, out Vector3 pos, out Quaternion rot))
                {
                    var g = new GameObject("Nozzle");
                    Undo.RegisterCreatedObjectUndo(g, "Сопло горелки");
                    g.transform.SetParent(blow.transform, true);
                    g.transform.SetPositionAndRotation(pos, rot);
                    g.transform.localScale = Vector3.one;
                    nz.objectReferenceValue = g.transform;
                    log.AppendLine("  ✔ создано сопло «Nozzle» на конце трубки сверху. Выдели горелку: оранжевая линия — куда бьёт пламя. " +
                                   "Не тот конец — «2. Сопло горелки — на другой конец» или подвинь Nozzle руками");
                }
                else log.AppendLine("  ✖ у горелки нет мешей — сопло не угадать, создай пустой «Nozzle» на кончике (синяя ось наружу)");
            }
            else log.AppendLine($"  ✔ сопло: «{n.name}»");

            SerializedProperty pr = so.FindProperty("_process");
            if (pr != null && pr.objectReferenceValue == null)
            {
                ProcessDef caramel = FindCaramel();
                if (caramel != null) { pr.objectReferenceValue = caramel; log.AppendLine($"  ✔ обработка: «{caramel.name}» (карамелизация продуктов)"); }
                else log.AppendLine("  · Process не задан — продукты не карамелизует, но ковш греет");
            }
            so.ApplyModifiedProperties();
        }

        private static ProcessDef FindCaramel()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ProcessDef"))
            {
                ProcessDef p = AssetDatabase.LoadAssetAtPath<ProcessDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (p == null) continue;
                string s = (p.name + " " + p.DisplayName).ToLowerInvariant();
                if (s.Contains("карамел") || s.Contains("caramel")) return p;
            }
            return null;
        }

        private static void CheckRoof(RoofingTorchItem roof, StringBuilder log)
        {
            var so = new SerializedObject(roof);
            Transform nz = so.FindProperty("_nozzle").objectReferenceValue as Transform;
            if (nz != null) log.AppendLine($"  ✔ сопло: «{nz.name}»");
            else log.AppendLine(HasPart(roof.transform, "bell") ? "  ✔ сопло найдётся само (часть Bell)"
                                                                 : "  ✖ нет части Bell и не задан Nozzle — создай пустой «Nozzle» на краю стакана (синяя ось наружу) и назначь");
            if (so.FindProperty("_hosePort").objectReferenceValue == null)
                log.AppendLine(HasPart(roof.transform, "hosebarb") || HasPart(roof.transform, "handle") ? "  ✔ штуцер шланга найдётся сам" : "  · штуцера нет — шланг из центра");
            TorchGasCylinder c = so.FindProperty("_cylinder").objectReferenceValue as TorchGasCylinder;
            float reach = so.FindProperty("_autoConnect").floatValue;
            float hose = so.FindProperty("_hoseLength").floatValue;
            if (c == null)
            {
                float best = float.MaxValue;
                foreach (TorchGasCylinder g in Object.FindObjectsByType<TorchGasCylinder>(FindObjectsSortMode.None))
                {
                    float d = Vector3.Distance(g.transform.position, roof.transform.position);
                    if (d < best) { best = d; c = g; }
                }
                if (c == null) log.AppendLine("  ✖ в сцене нет баллона (Torch Gas Cylinder) — не загорится");
                else if (best > reach) log.AppendLine($"  ✖ ближайший баллон «{c.name}» в {best:0.0} м — дальше Auto Connect ({reach:0.#} м): поставь ближе");
                else log.AppendLine($"  ✔ баллон «{c.name}» в {best:0.0} м — подключится сам (шланг {hose:0.#} м)");
            }
            else log.AppendLine($"  ✔ баллон задан: «{c.name}»");
        }

        private static void CheckCylinder(TorchGasCylinder cyl, StringBuilder log)
        {
            var so = new SerializedObject(cyl);
            bool reducer = so.FindProperty("_reducer").objectReferenceValue != null || HasChild(cyl.transform, "reducer") || HasChild(cyl.transform, "редуктор");
            log.AppendLine(reducer ? "  ✔ редуктор есть" : "  · редуктора нет — шланг крепится к вентилю/верху баллона (гореть будет)");
            log.AppendLine(so.FindProperty("_infinite").boolValue ? "  ✔ газ бесконечный (тест)" : "  · газ кончается");
        }

        /// <summary>Что в сцене греется горелкой: ковши с Surface Heat Glow.</summary>
        private static void CheckHeatTargets(StringBuilder log)
        {
            SurfaceHeatGlow[] all = Object.FindObjectsByType<SurfaceHeatGlow>(FindObjectsSortMode.None);
            log.AppendLine(all.Length == 0 ? "⚠ в сцене нет ни одного Surface Heat Glow — горелкой нечего греть (ковш не нагреется)"
                                           : $"Греется горелкой (Surface Heat Glow): {all.Length} шт.");
            foreach (SurfaceHeatGlow g in all)
            {
                bool cw = g.GetComponent<Cookware>() != null, heat = g.GetComponent<CookwareHeat>() != null;
                log.AppendLine($"  {(cw && heat ? "✔" : "✖")} «{g.transform.root.name}/{g.name}»" +
                               (cw ? "" : " — нет Cookware рядом") + (heat ? "" : " — нет Cookware Heat рядом (содержимое не нагреется)"));
            }
        }

        private static bool HasPart(Transform root, string key)
        {
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.ToLowerInvariant().Contains(key)) return true;
            return false;
        }

        private static bool HasChild(Transform root, string key)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name.ToLowerInvariant().Contains(key)) return true;
            return false;
        }

        private static Bounds BoundsOf(Transform root)
        {
            Bounds b = new Bounds(root.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if ((r.gameObject.hideFlags & HideFlags.DontSave) != 0 || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
#endif
