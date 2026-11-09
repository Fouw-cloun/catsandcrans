using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>Форма пасты, которую даёт фильера.</summary>
    public enum PastaShape : byte
    {
        [InspectorName("Спагетти")] Spaghetti = 0,
        [InspectorName("Пенне")] Penne = 1,
        [InspectorName("Ригатони")] Rigatoni = 2,
        [InspectorName("Фузилли")] Fusilli = 3,
        [InspectorName("Лингвини")] Linguine = 4,
        [InspectorName("Звёздочки")] Stelline = 5,
    }

    /// <summary>
    /// ФИЛЬЕРА растворонасоса для пасты — сменная насадка (бронзовый диск с отверстиями).
    /// Пока только данные: какая форма и как называется. Механика насоса — потом.
    /// Ставит меню CatCooks → Паста → 1.
    /// </summary>
    public sealed class PastaDie : MonoBehaviour
    {
        [SerializeField] private PastaShape _shape;
        [Tooltip("Цвет метки на ободе — чтобы различать фильеры издалека")]
        [SerializeField] private Color _tagColor = Color.white;

        public PastaShape Shape => _shape;
        public Color TagColor => _tagColor;

        public static string Title(PastaShape s)
        {
            switch (s)
            {
                case PastaShape.Spaghetti: return "Спагетти";
                case PastaShape.Penne: return "Пенне";
                case PastaShape.Rigatoni: return "Ригатони";
                case PastaShape.Fusilli: return "Фузилли";
                case PastaShape.Linguine: return "Лингвини";
                default: return "Звёздочки";
            }
        }

#if UNITY_EDITOR
        public void EditorSet(PastaShape s, Color tag) { _shape = s; _tagColor = tag; }
#endif
    }
}
