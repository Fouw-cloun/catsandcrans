using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food.EditorTools
{
    /// <summary>
    /// v23.5: СТЕНД СТИЛЯ ВЕЩЕСТВ в сцену. Берёт ВЫДЕЛЕННОЕ ведро (или первое тару со словом «Ведро» в имени)
    /// и делает его копии в ряд (как Ctrl+D) — по одной на жидкость/полужидкость рецепта аляски;
    /// на старте каждое наполнится само. Рядом — ряд «мастерков» с горками сыпучего и масла.
    /// Префабов не создаёт. Убрать — удалить объект «CC Style Stand» и вёдра «Stand_…».
    /// </summary>
    public static class CatCooksStyleStandSetup
    {
        // жидкости и полужидкости (по названию, точно или частью); второй — сверху (желтки на белке)
        static readonly string[][] Liquids =
        {
            // v23.10: вся цепочка аляски по порядку (бисквит → мороженое → меренга), потом остальное
            new[] { "Яичный белок", "Яичный желток" }, new[] { "Яичный желток", "" }, new[] { "Яичный белок", "" },
            new[] { "Яичная масса", "" }, new[] { "Шоколад", "" }, new[] { "Шоколадная масса", "" }, new[] { "Шоколадная паста", "" },
            new[] { "Бисквитное тесто", "" },
            new[] { "Молоко", "" }, new[] { "Сливки", "" }, new[] { "Смесь для мороженого", "" }, new[] { "Мороженое", "" },
            new[] { "Меренга", "" }, new[] { "Меренга перебитая", "" },
            new[] { "Вода", "" }, new[] { "Сметана", "" }, new[] { "Мёд", "" }, new[] { "Карамель", "" },
        };

        [MenuItem("CatCooks/Тест/Стенд стиля веществ (вёдра + мастерки)", priority = 200)]
        public static void Make()
        {
            var log = new StringBuilder("[Стенд] стенд стиля:\n");
            Cookware src = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<Cookware>() : null;
            if (src == null)
                foreach (Cookware c in Object.FindObjectsByType<Cookware>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    string n = c.name.ToLowerInvariant();
                    if (n.Contains("ведро") || n.Contains("bucket")) { src = c; break; }
                }
            if (src == null) { Debug.LogWarning("[Стенд] выдели ведро (тару) в сцене и запусти меню ещё раз"); return; }

            SceneView sv = SceneView.lastActiveSceneView;
            Renderer sr = src.GetComponentInChildren<Renderer>();
            float w = sr != null ? Mathf.Max(sr.bounds.size.x, sr.bounds.size.z) : 1f;
            float step = w * 1.5f;
            Vector3 right = sv != null ? Vector3.ProjectOnPlane(sv.rotation * Vector3.right, Vector3.up).normalized : Vector3.right;
            if (right.sqrMagnitude < 0.1f) right = Vector3.right;
            Vector3 fwd = Vector3.Cross(right, Vector3.up);

            // v23.5b: стенд — рядом с выбранным ведром, на его высоте (на полу), а не в точке камеры сцены;
            // вёдра и «мастерки» — ДОЧЕРНИЕ объекты стенда: двигаешь «CC Style Stand» — едет всё
            var root = new GameObject("CC Style Stand");
            Undo.RegisterCreatedObjectUndo(root, "Стенд стиля");
            root.transform.position = src.transform.position + fwd * (w * 2.5f);
            root.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            CCStyleStand stand = root.AddComponent<CCStyleStand>();
            stand.PlateSize = new Vector2(w * 0.42f, w * 0.34f);
            stand.PlateStep = w * 0.8f;
            stand.PlateRowZ = -w * 1.6f;
            stand.BucketStep = step;
            log.AppendLine($"  ✔ ряд «мастерков» — объект «CC Style Stand»");

            // v23.13: вёдра — ТОЛЬКО ВИД (копия модели образца + поверхность жидкости), строятся при запуске.
            // Сетевые копии ведра больше не делаем — FishNet их терял, и ряд был пустым.
            stand.BucketModel = src.gameObject;
            int made = stand.Liquids.Count;
            // старые сетевые вёдра стенда (если остались от прошлых версий) — убрать
            foreach (CCStyleStandSlot old in Object.FindObjectsByType<CCStyleStandSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (old != null && old.gameObject != src.gameObject) Undo.DestroyObjectImmediate(old.gameObject);
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(src.gameObject.scene);
            log.AppendLine($"  ✔ вёдер: {made} — появятся при запуске (копия модели «{src.name}» + жидкость, без сети). Список — поле Liquids у стенда");
            log.AppendLine("  Вёдра и «мастерки» — внутри «CC Style Stand»: двигай/крути его — едет весь стенд. ПКМ по компоненту → «Выстроить вёдра в ряд» / «Поставить всё на пол».");
            log.AppendLine("  Продуктов, которых нет в базе, просто не будет (в консоли — «нет продукта»).");
            log.AppendLine("  Сохрани сцену. Ведро-образец не трогается (оно остаётся обычным ведром).");
            Debug.Log(log.ToString());
        }
    }
}
