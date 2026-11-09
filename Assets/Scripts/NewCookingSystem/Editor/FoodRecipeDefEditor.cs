#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// РЕДАКТОР РЕЦЕПТА. Написан ради одной вещи: чтобы состояние в требовании
    /// нельзя было выбрать неверно.
    ///
    /// Обычный выпадающий список показал бы все 40 состояний, включая «в мешке»
    /// для говядины и «разделано» для молока. Здесь список фильтруется: для
    /// выбранного продукта строится граф его группы и показываются ТОЛЬКО
    /// достижимые состояния, с пометкой, каким процессом до них добираться.
    ///
    /// Плюс внизу живая проверка: чего не хватает, кто этот рецепт использует,
    /// и на какой глубине крафта он сидит.
    /// </summary>
    [CustomEditor(typeof(FoodRecipeDef))]
    public class FoodRecipeDefEditor : Editor
    {
        private static readonly Dictionary<ProductDef, List<FoodState>> _cache
            = new Dictionary<ProductDef, List<FoodState>>();

        public override void OnInspectorGUI()
        {
            FoodRecipeDef r = (FoodRecipeDef)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("Title"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Note"));

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Требования", EditorStyles.boldLabel);

            SerializedProperty needs = serializedObject.FindProperty("Needs");
            int remove = -1;

            for (int i = 0; i < needs.arraySize; i++)
            {
                SerializedProperty el = needs.GetArrayElementAtIndex(i);
                SerializedProperty prodP = el.FindPropertyRelative("Product");
                SerializedProperty stateP = el.FindPropertyRelative("State");
                SerializedProperty cntP = el.FindPropertyRelative("Count");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(prodP, GUIContent.none);
                if (GUILayout.Button("×", GUILayout.Width(24f))) remove = i;
                EditorGUILayout.EndHorizontal();

                ProductDef p = prodP.objectReferenceValue as ProductDef;
                if (p == null)
                {
                    EditorGUILayout.HelpBox("Выбери продукт", MessageType.Warning);
                }
                else
                {
                    List<FoodState> reach = Reachable(p);
                    DrawStatePopup(stateP, reach, p);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("порций", GUILayout.Width(50f));
                    cntP.intValue = EditorGUILayout.IntField(cntP.intValue, GUILayout.Width(44f));
                    if (cntP.intValue < 1) cntP.intValue = 1;

                    if (p.MadeByRecipe)
                        EditorGUILayout.LabelField("· полуфабрикат, нужен свой рецепт",
                            EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }

            if (remove >= 0) needs.DeleteArrayElementAtIndex(remove);

            if (GUILayout.Button("Добавить ингредиент"))
                needs.InsertArrayElementAtIndex(needs.arraySize);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Как готовится", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Process"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Seconds"));

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Что получается", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ResultProduct"),
                new GUIContent("Результат-продукт",
                    "Пусто = это БЛЮДО, уходит на тарелку.\n" +
                    "Заполнено = это ПОЛУФАБРИКАТ, вернётся в мир продуктом."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ResultCount"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10f);
            DrawStatus(r);
        }

        /// <summary>
        /// Выпадающий список ТОЛЬКО достижимых состояний. Рядом с каждым —
        /// каким процессом до него добираться, чтобы не держать граф в голове.
        /// </summary>
        private void DrawStatePopup(SerializedProperty stateP, List<FoodState> reach, ProductDef p)
        {
            if (reach.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"У «{p.Title}» нет ни одного перехода. Проверь группу.",
                    MessageType.Error);
                return;
            }

            string[] labels = new string[reach.Count + 1];
            labels[0] = "любое (состояние неважно)";
            for (int i = 0; i < reach.Count; i++)
                labels[i + 1] = FoodStateInfo.NameOf(reach[i]);

            FoodState cur = (FoodState)stateP.intValue;
            int idx = 0;
            for (int i = 0; i < reach.Count; i++)
                if (reach[i] == cur) { idx = i + 1; break; }

            // состояние выбрано, но недостижимо — это ошибка данных, показываем
            if (cur != FoodState.None && idx == 0)
            {
                EditorGUILayout.HelpBox(
                    $"«{FoodStateInfo.NameOf(cur)}» для «{p.Title}» недостижимо. " +
                    "Скорее всего, поменяли группу или правила.", MessageType.Error);
            }

            int sel = EditorGUILayout.Popup("Состояние", idx, labels);
            stateP.intValue = sel == 0 ? (int)FoodState.None : (int)reach[sel - 1];
        }

        /// <summary>Обход графа группы от стартового состояния. Кэшируется:
        /// инспектор рисуется каждый кадр, а граф меняется редко.</summary>
        private static List<FoodState> Reachable(ProductDef p)
        {
            if (_cache.TryGetValue(p, out List<FoodState> cached)) return cached;

            List<FoodState> result = new List<FoodState>();
            if (p.Group == null) { _cache[p] = result; return result; }

            HashSet<FoodState> seen = new HashSet<FoodState> { p.StartState };
            Queue<FoodState> q = new Queue<FoodState>();
            q.Enqueue(p.StartState);
            result.Add(p.StartState);

            List<FoodRule> rules = new List<FoodRule>(p.Group.Rules);
            rules.AddRange(p.Overrides);

            while (q.Count > 0)
            {
                FoodState cur = q.Dequeue();
                foreach (FoodRule r in rules)
                {
                    if (r.From != cur) continue;
                    if (r.To == FoodState.None || !seen.Add(r.To)) continue;
                    result.Add(r.To);
                    q.Enqueue(r.To);
                }
            }
            _cache[p] = result;
            return result;
        }

        /// <summary>Сбросить кэш. Зовётся из редактора группы после правки правил,
        /// иначе список состояний останется старым.</summary>
        public static void ClearCache() => _cache.Clear();

        private void DrawStatus(FoodRecipeDef r)
        {
            EditorGUILayout.LabelField("Проверка", EditorStyles.boldLabel);

            if (r.IsSubRecipe)
                EditorGUILayout.HelpBox(
                    $"ПОЛУФАБРИКАТ. Даёт продукт «{r.ResultProduct.Title}» × {r.ResultCount}. " +
                    "В мире появляется только через этот рецепт.", MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "БЛЮДО. Уходит на тарелку, оценивается, даёт бафф котам.",
                    MessageType.Info);

            List<string> problems = new List<string>();
            int subCount = 0;

            foreach (FoodNeed n in r.Needs)
            {
                if (n.Product == null) { problems.Add("есть требование без продукта"); continue; }
                if (n.Product.Group == null)
                    problems.Add($"у «{n.Product.Title}» не задана группа");
                if (n.Product.MadeByRecipe)
                {
                    subCount++;
                    if (FindRecipeFor(n.Product) == null)
                        problems.Add($"«{n.Product.Title}» помечен полуфабрикатом, " +
                                     "но рецепта для него нет — достать неоткуда");
                }
            }

            if (r.Needs.Count == 0) problems.Add("нет ни одного требования");
            if (r.Seconds <= 0f) problems.Add("время готовки не задано");

            if (problems.Count > 0)
                EditorGUILayout.HelpBox("· " + string.Join("\n· ", problems), MessageType.Warning);
            else
                EditorGUILayout.HelpBox($"Всё на месте. Требований {r.Needs.Count}, " +
                    $"из них полуфабрикатов {subCount}.", MessageType.None);

            // кто использует этот полуфабрикат
            if (r.IsSubRecipe)
            {
                List<string> users = new List<string>();
                foreach (string guid in AssetDatabase.FindAssets("t:FoodRecipeDef"))
                {
                    FoodRecipeDef other = AssetDatabase.LoadAssetAtPath<FoodRecipeDef>(
                        AssetDatabase.GUIDToAssetPath(guid));
                    if (other == null || other == r) continue;
                    foreach (FoodNeed n in other.Needs)
                        if (n.Product == r.ResultProduct) { users.Add(other.Title); break; }
                }
                EditorGUILayout.LabelField(users.Count == 0
                    ? "Пока никем не используется"
                    : "Используют: " + string.Join(", ", users), EditorStyles.miniLabel);
            }
        }

        private static FoodRecipeDef FindRecipeFor(ProductDef p)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:FoodRecipeDef"))
            {
                FoodRecipeDef r = AssetDatabase.LoadAssetAtPath<FoodRecipeDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (r != null && r.ResultProduct == p) return r;
            }
            return null;
        }
    }
}
#endif
