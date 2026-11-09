#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КУХОННАЯ КНИГА — окно, где видно всю базу разом.
    ///
    /// Ассеты лежат в Assets/CatCooks/Food, но лазить по папкам неудобно:
    /// продукт не показывает свои переходы, а рецепт не показывает, кто его
    /// использует. Здесь всё связано.
    ///
    /// Открывается: CatCooks → Еда → Кухонная книга
    /// </summary>
    public class CatCooksFoodBrowser : EditorWindow
    {
        private enum Tab { Products, Groups, Recipes }
        private Tab _tab = Tab.Products;
        private Vector2 _scroll;
        private string _filter = "";
        private ProductDef _selected;

        [MenuItem("CatCooks/Еда/Кухонная книга", priority = 1)]
        public static void Open()
        {
            CatCooksFoodBrowser w = GetWindow<CatCooksFoodBrowser>(false, "Кухонная книга");
            w.minSize = new Vector2(520f, 480f);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_tab == Tab.Products, "Продукты", EditorStyles.toolbarButton))
                _tab = Tab.Products;
            if (GUILayout.Toggle(_tab == Tab.Groups, "Группы", EditorStyles.toolbarButton))
                _tab = Tab.Groups;
            if (GUILayout.Toggle(_tab == Tab.Recipes, "Рецепты", EditorStyles.toolbarButton))
                _tab = Tab.Recipes;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            _filter = EditorGUILayout.TextField("Поиск", _filter);
            EditorGUILayout.Space(4f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Products: DrawProducts(); break;
                case Tab.Groups: DrawGroups(); break;
                case Tab.Recipes: DrawRecipes(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private bool Match(string s)
        {
            return string.IsNullOrEmpty(_filter)
                || s.ToLower().Contains(_filter.ToLower());
        }

        private void DrawProducts()
        {
            List<ProductDef> all = Load<ProductDef>();
            EditorGUILayout.LabelField($"Продуктов: {all.Count}", EditorStyles.boldLabel);

            foreach (ProductDef p in all)
            {
                if (!Match(p.Title)) continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField($"#{p.Id}", GUILayout.Width(38f));
                if (GUILayout.Button(p.Title, EditorStyles.label, GUILayout.Width(150f)))
                {
                    Selection.activeObject = p;
                    EditorGUIUtility.PingObject(p);
                    _selected = _selected == p ? null : p;
                }
                EditorGUILayout.LabelField(p.Group != null ? p.Group.Title : "БЕЗ ГРУППЫ",
                    GUILayout.Width(150f));
                if (p.MadeByRecipe) EditorGUILayout.LabelField("полуфабрикат", GUILayout.Width(90f));
                EditorGUILayout.EndHorizontal();

                if (_selected == p) DrawProductDetail(p);
                EditorGUILayout.EndVertical();
            }
        }

        /// <summary>Разворот продукта: весь его путь по состояниям.</summary>
        private void DrawProductDetail(ProductDef p)
        {
            if (p.Group == null)
            {
                EditorGUILayout.HelpBox("Группа не задана — переходов нет вовсе",
                    MessageType.Error);
                return;
            }

            List<FoodRule> rules = new List<FoodRule>(p.Group.Rules);
            rules.AddRange(p.Overrides);

            HashSet<FoodState> seen = new HashSet<FoodState> { p.StartState };
            Queue<FoodState> q = new Queue<FoodState>();
            q.Enqueue(p.StartState);
            List<string> lines = new List<string>();

            while (q.Count > 0)
            {
                FoodState cur = q.Dequeue();
                foreach (FoodRule r in rules)
                {
                    if (r.From != cur || r.To == FoodState.None) continue;
                    string time = r.Seconds > 0.01f ? $"{r.Seconds:0.#}с" : "сразу";
                    string win = r.HasWindow ? $", окно ±{r.Window * 100f:0}%" : "";
                    lines.Add($"    {FoodStateInfo.NameOf(cur)} " +
                              $"--[{FoodStateInfo.NameOf(r.Process)} {time}{win}]--> " +
                              FoodStateInfo.NameOf(r.To));
                    if (seen.Add(r.To)) q.Enqueue(r.To);
                }
            }

            EditorGUILayout.LabelField($"Состояний достижимо: {seen.Count}",
                EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(string.Join("\n", lines), MessageType.None);

            // в каких рецептах участвует
            List<string> used = new List<string>();
            foreach (FoodRecipeDef r in Load<FoodRecipeDef>())
                foreach (FoodNeed n in r.Needs)
                    if (n.Product == p) { used.Add(r.Title); break; }

            EditorGUILayout.LabelField(used.Count == 0
                ? "Ни в одном рецепте не используется"
                : "В рецептах: " + string.Join(", ", used), EditorStyles.miniLabel);
        }

        private void DrawGroups()
        {
            List<FoodGroupDef> all = Load<FoodGroupDef>();
            List<ProductDef> prods = Load<ProductDef>();
            EditorGUILayout.LabelField($"Групп: {all.Count}", EditorStyles.boldLabel);

            foreach (FoodGroupDef g in all)
            {
                if (!Match(g.Title)) continue;
                int n = 0;
                foreach (ProductDef p in prods) if (p.Group == g) n++;

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                if (GUILayout.Button(g.Title, EditorStyles.label, GUILayout.Width(200f)))
                {
                    Selection.activeObject = g;
                    EditorGUIUtility.PingObject(g);
                }
                EditorGUILayout.LabelField($"правил {g.Rules.Count}", GUILayout.Width(80f));
                EditorGUILayout.LabelField($"продуктов {n}", GUILayout.Width(100f));
                EditorGUILayout.LabelField($"старт: {FoodStateInfo.NameOf(g.StartState)}");
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawRecipes()
        {
            List<FoodRecipeDef> all = Load<FoodRecipeDef>();
            EditorGUILayout.LabelField($"Рецептов: {all.Count}", EditorStyles.boldLabel);

            foreach (FoodRecipeDef r in all)
            {
                if (!Match(r.Title)) continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(r.Title, EditorStyles.label, GUILayout.Width(190f)))
                {
                    Selection.activeObject = r;
                    EditorGUIUtility.PingObject(r);
                }
                EditorGUILayout.LabelField(r.IsSubRecipe ? "полуфабрикат" : "блюдо",
                    GUILayout.Width(100f));
                EditorGUILayout.LabelField(FoodStateInfo.NameOf(r.Process), GUILayout.Width(120f));
                EditorGUILayout.LabelField($"{r.Seconds:0}с", GUILayout.Width(50f));
                EditorGUILayout.EndHorizontal();

                List<string> parts = new List<string>();
                foreach (FoodNeed n in r.Needs)
                {
                    string t = n.Product != null ? n.Product.Title : "???";
                    string st = n.State == FoodState.None
                        ? "любое" : FoodStateInfo.NameOf(n.State);
                    parts.Add($"{t} ({st}) ×{n.Count}");
                }
                EditorGUILayout.LabelField("  " + string.Join(" + ", parts),
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
        }

        private static List<T> Load<T>() where T : Object
        {
            List<T> list = new List<T>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                T a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null) list.Add(a);
            }
            return list;
        }
    }
}
#endif
