using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// РЕЕСТР В ИГРЕ. Держит таблицу переходов и продукты.
    ///
    /// Создаёт себя сам при первом обращении и живёт между сценами — так же,
    /// как служба ошмётков. Причина та же: продукты спавнятся по сети, и ссылку
    /// на сценный объект в префаб не положишь.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FoodDatabase : MonoBehaviour
    {
        [Tooltip("Все продукты игры. Заполняется кнопкой " +
                 "CatCooks → Еда → Собрать список продуктов, либо вручную.")]
        [SerializeField] private List<ProductDef> _products = new List<ProductDef>();

        [Tooltip("Все рецепты: полуфабрикаты и блюда. Собираются той же кнопкой. " +
                 "Тара ищет среди них подходящий к своему содержимому.")]
        [SerializeField] private List<FoodRecipeDef> _recipes = new List<FoodRecipeDef>();

        [Tooltip("Печатать отчёт проверки при старте. Держи включённым, " +
                 "пока данные меняются: ловит ошибки до того, как они всплывут в игре.")]
        [SerializeField] private bool _validateOnStart = true;

        private static FoodDatabase _instance;
        private static bool _quitting;

        private readonly FoodTransitionTable _table = new FoodTransitionTable();
        private bool _built;

        public static FoodDatabase Instance
        {
            get
            {
                if (_quitting) return null;
                if (_instance != null) return _instance;

                _instance = FindAnyObjectByType<FoodDatabase>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("CatCooks Food Database (auto)");
                    _instance = go.AddComponent<FoodDatabase>();
                    DontDestroyOnLoad(go);
                }
                _instance.EnsureBuilt();
                return _instance;
            }
        }

        public FoodTransitionTable Table { get { EnsureBuilt(); return _table; } }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // Пустая автобаза (создалась в сцене-загрузчике) — уступает настоящей базе сцены.
                if (_instance._products.Count == 0 && _products.Count > 0)
                {
                    Debug.Log("[FoodDatabase] пустая автобаза заменена базой сцены", this);
                    Destroy(_instance.gameObject);
                }
                else
                {
                    // Дубликат: убираем ТОЛЬКО компонент, а не весь объект —
                    // на нём могут быть другие важные вещи.
                    Destroy(this);
                    return;
                }
            }
            _instance = this;
            _built = false;
            EnsureBuilt();
        }

        private void OnApplicationQuit() => _quitting = true;
        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            _table.Build(_products);
            if (_validateOnStart) Debug.Log(_table.Validate());
        }

        public ProductDef Get(ushort id) => Table.GetProduct(id);

        /// <summary>Все рецепты. Порядок = приоритет: при двух подходящих
        /// побеждает тот, что выше в списке.</summary>
        public IReadOnlyList<FoodRecipeDef> Recipes => _recipes;

        public int IndexOf(FoodRecipeDef r) => _recipes.IndexOf(r);
        public FoodRecipeDef RecipeAt(int i) => i >= 0 && i < _recipes.Count ? _recipes[i] : null;

        /// <summary>Есть ли у этого продукта в этом состоянии переход под такой
        /// процесс. Инструменты спрашивают это, чтобы понять, применимы ли они.</summary>
        public bool CanApply(ushort product, FoodState state, FoodProcess proc)
        {
            return Table.TryGet(product, state, proc, out _);
        }

#if UNITY_EDITOR
        /// <summary>Собрать список продуктов из проекта. Только в редакторе:
        /// в сборке ассеты ищутся не так, и список уже должен быть заполнен.</summary>
        public void CollectFromProject()
        {
            _products.Clear();
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = UnityEditor.AssetDatabase.LoadAssetAtPath<ProductDef>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (p != null) _products.Add(p);
            }
            _recipes.Clear();
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:FoodRecipeDef"))
            {
                FoodRecipeDef r = UnityEditor.AssetDatabase.LoadAssetAtPath<FoodRecipeDef>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (r != null) _recipes.Add(r);
            }
            // Сложные рецепты (больше требований) вперёд: «масло из сливок» не должно
            // перехватывать «пюре Робюшона», где сливки — лишь часть состава.
            _recipes.Sort((a, b) => b.Needs.Count.CompareTo(a.Needs.Count));
            _built = false;
        }
#endif
    }
}
