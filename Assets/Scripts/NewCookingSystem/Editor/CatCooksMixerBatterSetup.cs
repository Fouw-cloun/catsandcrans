#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Хлеб → «Бетономешалка: точка для тары + уровень пониже (выдели её)».
    ///   • PourDock — точка для тары рядом с бетономешалкой (Cookware Dock, как у измельчителя): ставишь туда
    ///     ведро или опалубку (ЛКМ с тарой в руках, глядя на пол у точки, или просто отпусти рядом — магнит),
    ///     поворачиваешь бочку — тесто льётся в неё. Сама бетономешалка магнитом не тянется.
    ///   • Жидкость в бочке визуально ниже и уже у горловины (Full Level, Rim Inset, верх стенки) —
    ///     чтобы гладь не вылезала за модель бочки. Старые значения — в логе.
    /// Префабы не создаются; меняются компоненты выделенной бетономешалки.
    /// </summary>
    public static class CatCooksMixerBatterSetup
    {
        [MenuItem("CatCooks/Хлеб/Бетономешалка: точка для тары + уровень пониже (выдели её)", priority = 92)]
        public static void Setup()
        {
            var log = new StringBuilder();
            GameObject sel = Selection.activeGameObject;
            MixerDough md = sel != null ? (sel.GetComponentInParent<MixerDough>() ?? sel.GetComponentInChildren<MixerDough>()) : null;
            if (md == null)
            {
                EditorUtility.DisplayDialog("Бетономешалка", "Выдели бетономешалку (корень с Mixer Dough). Нет Mixer Dough — сначала «Хлеб → Авто: бетономешалка».", "Ок");
                return;
            }
            Transform root = md.transform;
            Cookware cw = md.GetComponent<Cookware>();

            // ── точка для тары ──
            Transform dockT = root.Find("PourDock");
            bool made = dockT == null;
            if (made)
            {
                var go = new GameObject("PourDock");
                Undo.RegisterCreatedObjectUndo(go, "PourDock");
                go.transform.SetParent(root, false);
                dockT = go.transform;

                Vector3 mouth = cw != null ? cw.MouthPoint : root.position + root.forward;
                Vector3 flat = Vector3.ProjectOnPlane(mouth - root.position, Vector3.up);
                if (flat.sqrMagnitude < 0.01f) flat = Vector3.ProjectOnPlane(root.forward, Vector3.up);
                float dist = Mathf.Max(0.9f, flat.magnitude + 0.6f);
                Vector3 p = root.position + flat.normalized * dist;
                // на пол под точкой (мимо самой бетономешалки)
                float y = root.position.y;
                foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.transform.IsChildOf(root)) continue;
                    if (h.point.y > y - 5f) { y = h.point.y; break; }
                }
                dockT.position = new Vector3(p.x, y, p.z);
                dockT.rotation = Quaternion.LookRotation(-flat.normalized, Vector3.up);   // «перед» тары — к бочке
            }
            CookwareDock dock = dockT.GetComponent<CookwareDock>();
            if (dock == null) dock = Undo.AddComponent<CookwareDock>(dockT.gameObject);
            var dso = new SerializedObject(dock);
            SetF(dso, "_radius", 0.6f);
            SetF(dso, "_magnetRadius", 0.8f);
            dso.ApplyModifiedProperties();
            var mso = new SerializedObject(md);
            SerializedProperty pd = mso.FindProperty("_pourDock");
            if (pd != null) pd.objectReferenceValue = dock;
            mso.ApplyModifiedProperties();
            log.AppendLine(made ? $"＋ PourDock — точка для тары рядом с бочкой ({dockT.position.x:0.00}, {dockT.position.y:0.00}, {dockT.position.z:0.00}). " +
                                  "Передвинь её, если надо: зелёный круг — зона подключения, жёлтый — магнит"
                                : "✔ PourDock уже есть — настроен");
            log.AppendLine("  Mixer Dough → Pour Dock = PourDock: тара в точке — бочка льёт в неё");

            // ── уровень жидкости в бочке пониже и уже у горловины ──
            CookwareLiquidVisual lv = root.GetComponentInChildren<CookwareLiquidVisual>(true);
            if (lv == null) log.AppendLine("⚠ в бочке нет Cookware Liquid Visual (LiquidAnchor) — «Хлеб → Авто: бетономешалка»");
            else
            {
                Undo.RecordObject(lv, "Mixer level");
                var lso = new SerializedObject(lv);
                float full = Get(lso, "_fullLevel"), rim = Get(lso, "_rimInset"), maxH = Get(lso, "_maxHeight"), rTop = Get(lso, "_radiusTop"), rBot = Get(lso, "_radiusBottom");
                float nFull = Mathf.Min(full, 0.62f);
                float nRim = Mathf.Clamp(Mathf.Max(rim, maxH * 0.15f), 0f, 0.15f);
                SetF(lso, "_fullLevel", nFull);
                SetF(lso, "_rimInset", nRim);
                SerializedProperty wall = lso.FindProperty("_wallProfile");
                bool again = full <= 0.62f + 1e-3f;          // меню уже нажимали — горлышко не сужаем второй раз
                if (again) log.AppendLine("  горлышко уже сужено раньше — не трогаю");
                else if (wall != null && wall.arraySize > 1)
                {
                    int n = wall.arraySize;
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)(n - 1);
                        float k = Mathf.Lerp(1f, 0.75f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, t)));
                        SerializedProperty e = wall.GetArrayElementAtIndex(i);
                        e.floatValue *= k;
                    }
                    log.AppendLine($"  стенки (Wall Profile, {n} точек): верх сужен до 75% — горлышко уже");
                }
                else
                {
                    float nTop = Mathf.Min(rTop, rBot) * 0.8f;
                    SetF(lso, "_radiusTop", nTop);
                    log.AppendLine($"  Radius Top: {rTop:0.000} → {nTop:0.000} — горлышко уже");
                }
                lso.ApplyModifiedProperties();
                EditorUtility.SetDirty(lv);
                log.AppendLine($"✔ уровень в бочке ниже: Full Level {full:0.00} → {nFull:0.00}, Rim Inset {rim:0.000} → {nRim:0.000} м");
                log.AppendLine("  (вернуть как было — эти поля у Cookware Liquid Visual в LiquidAnchor бочки)");
            }

            EditorUtility.SetDirty(md);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeGameObject = dockT.gameObject;
            log.AppendLine("\nДАЛЬШЕ: если бетономешалка — префаб, Overrides → Apply All. Сохрани сцену.\n" +
                           "Проверка: поставь ведро/опалубку в точку → замес → красная кнопка → поверни бочку ручкой → льётся в тару в точке.");
            Debug.Log("[Бетономешалка]\n" + log);
            EditorUtility.DisplayDialog("Бетономешалка", log.ToString(), "Ок");
        }

        private static float Get(SerializedObject so, string n) { SerializedProperty p = so.FindProperty(n); return p != null ? p.floatValue : 0f; }
        private static void SetF(SerializedObject so, string n, float v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.floatValue = v; }
    }
}
#endif
