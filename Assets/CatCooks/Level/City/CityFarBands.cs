using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ДАЛЬНИЕ ПОЯСА ГОРОДА — дешёвый фон вокруг сетки модулей (Tile Grid + застройка City):
    ///   • Пояс 3 — кольцо из модулей сразу за сеткой (дороги по краям, дома рядами по улицам, высотки во дворах).
    ///   • Пояс 4 — без модулей: сплошной «лес» высоток квадратной рамкой вокруг всего, к внешнему краю выше.
    /// Формы домов: уступы (2–3 яруса), Г-образные, восьмигранные башни, двускатные крыши у низких, карнизы,
    /// шпили, коробки на крышах. Цвета мягкие и тают в дымку тем сильнее, чем дальше пояс.
    /// Окна процедурные (шейдер CatCooks/City/Far), ночью горят от моста «Окна ночью».
    /// Теней, коллайдеров и Update нет. Собирается в редакторе кнопкой, меши — ассеты.
    /// Размеры — в долях модуля (S).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityFarBands : MonoBehaviour
    {
        public const string ContainerName = "Far Bands (generated)";

        [Tooltip("Застройка (City): от неё берётся сетка, размер модуля, дорога")]
        public CityBlockGenerator City;
        public Material Material;
        public int Seed = 11;

        [Header("Пояс 3 — кольцо модулей")]
        public bool Band3 = true;
        [Range(1, 3)] public int Band3Rings = 1;
        [Tooltip("Высота домов в рядах, доля модуля (от, до)")]
        public Vector2 Band3Heights = new Vector2(0.2f, 0.62f);
        [Tooltip("Ширина домов в ряду, доля модуля")]
        public Vector2 Band3Width = new Vector2(0.09f, 0.2f);
        [Tooltip("Глубина ряда, доля модуля")]
        public Vector2 Band3Depth = new Vector2(0.16f, 0.26f);
        [Range(0f, 1f)] public float Band3TowerChance = 0.6f;
        public Vector2 Band3TowerHeights = new Vector2(0.75f, 1.15f);
        [Tooltip("Насколько цвета пояса 3 тают в дымку (0 — как есть)")]
        [Range(0f, 1f)] public float Band3Haze = 0.06f;
        [Tooltip("Доля домов пояса 3 с окнами (остальные — глухие стены)")]
        [Range(0f, 1f)] public float Band3Windows = 0.7f;
        public bool Band3Ground = true;

        [Header("Пояс 4 — стена высоток")]
        public bool Band4 = true;
        [Tooltip("Толщина пояса, в модулях")]
        [Range(0.25f, 4f)] public float Band4Thickness = 0.75f;
        [Tooltip("Основание корпуса, доля модуля")]
        public Vector2 Band4Size = new Vector2(0.12f, 0.22f);
        [Tooltip("Высота у внутреннего края, доля модуля")]
        public Vector2 Band4NearHeights = new Vector2(0.55f, 0.9f);
        [Tooltip("Высота у внешнего края (стена), доля модуля")]
        public Vector2 Band4FarHeights = new Vector2(0.75f, 1.2f);
        [Range(0f, 1f)] public float Band4Haze = 0.16f;
        [Range(0f, 1f)] public float Band4Windows = 0.5f;
        public bool Band4Ground = true;

        [Header("Формы")]
        [Tooltip("Уступы у высоких (2–3 яруса)")]
        [Range(0f, 1f)] public float TierChance = 0.45f;
        [Tooltip("Восьмигранные башни среди высоких")]
        [Range(0f, 1f)] public float OctagonChance = 0.18f;
        [Tooltip("Г-образные корпуса")]
        [Range(0f, 1f)] public float LShapeChance = 0.2f;
        [Tooltip("Двускатные крыши у низких")]
        [Range(0f, 1f)] public float GableChance = 0.35f;
        [Tooltip("Карниз по верху")]
        [Range(0f, 1f)] public float CorniceChance = 0.5f;
        [Tooltip("Шпиль на самых высоких")]
        [Range(0f, 1f)] public float SpireChance = 0.25f;
        [Tooltip("Коробки на плоских крышах (вентиляция, баки), до штук")]
        [Range(0, 4)] public int RoofClutter = 2;

        [Header("Цвета — сняты с атласов города (2layer: little / middle / plane)")]
        [Tooltip("Стены. Чаще встречается тот цвет, что повторён")]
        public Color[] WallColors =
        {
            new Color(0.77f, 0.51f, 0.36f), new Color(0.87f, 0.61f, 0.46f), new Color(0.66f, 0.34f, 0.26f),
            new Color(0.69f, 0.44f, 0.37f), new Color(0.76f, 0.67f, 0.55f), new Color(0.67f, 0.57f, 0.46f),
            new Color(0.89f, 0.79f, 0.66f), new Color(0.55f, 0.42f, 0.34f), new Color(0.70f, 0.66f, 0.61f),
            new Color(0.81f, 0.56f, 0.41f), new Color(0.52f, 0.27f, 0.19f), new Color(0.42f, 0.50f, 0.50f),
            new Color(0.77f, 0.51f, 0.36f), new Color(0.76f, 0.67f, 0.55f), new Color(0.46f, 0.58f, 0.49f)
        };
        [Tooltip("Карнизы, шпили, коробки на крышах, верхние ярусы")]
        public Color[] TrimColors =
        {
            new Color(0.90f, 0.82f, 0.70f), new Color(0.77f, 0.74f, 0.70f), new Color(0.44f, 0.22f, 0.16f),
            new Color(0.35f, 0.42f, 0.42f), new Color(0.40f, 0.34f, 0.28f)
        };
        [Tooltip("Разброс оттенка/насыщенности/яркости у каждого дома")]
        [Range(0f, 1f)] public float ColorJitter = 0.5f;
        [Tooltip("Шанс, что верхний ярус уступчатого дома другого цвета")]
        [Range(0f, 1f)] public float TierColorChance = 0.35f;
        [Tooltip("Цвет дымки, в который чуть тают дальние пояса")]
        public Color HazeColor = new Color(0.82f, 0.80f, 0.82f);

        [HideInInspector] public string BakeFolder;
        [HideInInspector] public int LastHouses, LastTriangles, LastRenderers;
    }
}
