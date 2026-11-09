using FishNet.Object;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система со
// своими глобальными FoodState и FoodTransition.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ОТЛАДКА: нажал 5 — из тебя вылетает продукт в нужном состоянии.
    /// По умолчанию — картошка сразу чищеная, готовая к измельчителю.
    ///
    /// Работает у любого игрока: нажатие уходит на сервер, сервер спавнит
    /// продукт перед камерой нажавшего и бросает его вперёд.
    ///
    /// ═══ КАК ПОСТАВИТЬ ═══
    /// Пустой объект в игровой сцене → NetworkObject + DebugFoodSpawner.
    /// Prefab = префаб целой картошки (Картофель_NEW). Он должен быть в Spawnable Prefabs.
    ///
    /// Это временный инструмент: потом удалить объект из сцены
    /// (строчка в УДАЛИТЬ_ПОТОМ.md).
    /// </summary>
    public sealed class DebugFoodSpawner : NetworkBehaviour
    {
        [Tooltip("Что спавнить: префаб продукта с FoodPiece")]
        [SerializeField] private FoodPiece _prefab;
        [Tooltip("В каком состоянии. Для измельчителя картошка должна быть «Чищеное».")]
        [SerializeField] private FoodState _state = FoodState.Peeled;
        [SerializeField] private KeyCode _key = KeyCode.Alpha5;
        [SerializeField] private KeyCode _altKey = KeyCode.Keypad5;

        [Header("Бросок")]
        [Tooltip("На каком расстоянии перед камерой появляется, м")]
        [SerializeField] private float _spawnDistance = 1.2f;
        [Tooltip("Скорость броска вперёд, м/с")]
        [SerializeField] private float _throwSpeed = 4f;
        [Tooltip("Добавка вверх, м/с — лёгкая дуга")]
        [SerializeField] private float _throwUp = 1.5f;
        [Tooltip("Не чаще, с")]
        [SerializeField] private float _cooldown = 0.2f;

        private float _nextLocal;
        private float _nextServer;

        private void Update()
        {
            if (!base.IsClientInitialized || _prefab == null) return;
            if (!Input.GetKeyDown(_key) && !Input.GetKeyDown(_altKey)) return;
            if (Time.time < _nextLocal) return;
            _nextLocal = Time.time + _cooldown;

            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 dir = cam.transform.forward;
            CmdSpawn(cam.transform.position + dir * _spawnDistance, dir);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdSpawn(Vector3 at, Vector3 dir)
        {
            if (Time.time < _nextServer || _prefab == null) return;
            _nextServer = Time.time + _cooldown * 0.5f;

            FoodPiece piece = Instantiate(_prefab, at, Random.rotation);
            // До Spawn: продукт берётся из префаба, состояние — наше.
            piece.ServerPreset(null, _state, 100, 0, 100);
            base.Spawn(piece.gameObject);

            Rigidbody rb = piece.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = dir.normalized * _throwSpeed + Vector3.up * _throwUp;
                rb.angularVelocity = Random.insideUnitSphere * 4f;
            }
        }
    }
}
