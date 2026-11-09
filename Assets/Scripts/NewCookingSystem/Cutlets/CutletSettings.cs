using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Настройки котлет — ассет Resources/CutletSettings (делает меню Котлеты → 5).
    ///   Patty Prefab — префаб котлеты (FoodPiece «Котлета» + Cutlet Patty): им становится кучка отбитого фарша на столе.
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Котлеты — настройки", fileName = "CutletSettings")]
    public sealed class CutletSettings : ScriptableObject
    {
        [Tooltip("Префаб котлеты (сохранённый Cutlet_Piece из меню Котлеты → 4). Должен быть в Spawnable Prefabs")]
        public GameObject PattyPrefab;
        [Tooltip("С какой кучки отбитого фарша получается комок (порций; мастерок = 2)")]
        [Min(1)] public int MinHeapPortions = 2;
        [Tooltip("Сколько порций максимум в одном комке — остальное остаётся кучкой")]
        [Min(2)] public int MaxPortions = 8;

        private static CutletSettings _inst;
        private static bool _tried;

        public static CutletSettings Instance
        {
            get
            {
#if UNITY_EDITOR
                if (_inst == null) _tried = false;
#endif
                if (_inst == null && !_tried) { _tried = true; _inst = Resources.Load<CutletSettings>("CutletSettings"); }
                return _inst;
            }
        }
    }
}
