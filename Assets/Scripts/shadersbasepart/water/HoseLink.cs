using FishNet.Object;
using UnityEngine;

/// <summary>
/// СВЯЗКА ШЛАНГА: кольцо ↔ брандспойт. Пока шланг НЕ подключён к гидранту, оба
/// конца связаны между собой — берёшь один, второй ТАЩИТСЯ за ним (не остаётся
/// лежать, шланг не растягивается бесконечно).
///
/// ЛОГИКА (на сервере):
///   - Если один конец ДЕРЖАТ (или он присоединён к гидранту), а второй свободен
///     и расстояние больше длины шланга — свободный ПОДТЯГИВАЕТСЯ физикой к нему
///     (как база пылесоса за дулом).
///   - Если оба свободны (лежат) — ничего не делаем.
///   - Если кольцо присоединено к гидранту, а брандспойт в руках — длину
///     ограничивает сам NozzleGunItem (упор игрока), тут ничего не тянем.
///
/// НАСТРОЙКА: повесь на ЛЮБОЙ из двух объектов (или на отдельный пустой) и
/// заполни обе ссылки. Один экземпляр на пару кольцо+брандспойт.
/// </summary>
public class HoseLink : NetworkBehaviour
{
    [Header("Концы шланга")]
    [SerializeField] private HoseRingItem _ring;
    [SerializeField] private NozzleGunItem _nozzle;

    [Header("Длина и подтягивание")]
    [Tooltip("Длина шланга, м. Дальше свободный конец подтягивается.")]
    [SerializeField] private float _hoseLength = 6f;
    [Tooltip("Запас, прежде чем начать тянуть")]
    [SerializeField] private float _threshold = 0.3f;
    [Tooltip("Сила подтягивания на метр перетяга")]
    [SerializeField] private float _pullForce = 14f;
    [Tooltip("Ограничение силы (чтобы не улетал)")]
    [SerializeField] private float _maxPull = 40f;
    [Tooltip("Максимальная скорость подтягивания")]
    [SerializeField] private float _maxSpeed = 6f;

    private Rigidbody _ringRb;
    private Rigidbody _nozzleRb;

    public HoseRingItem Ring => _ring;
    public NozzleGunItem Nozzle => _nozzle;

    private void Awake()
    {
        if (_ring != null) _ringRb = _ring.GetComponent<Rigidbody>();
        if (_nozzle != null) _nozzleRb = _nozzle.GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (!base.IsServerInitialized) return;
        if (_ring == null || _nozzle == null) return;

        bool ringFixed = _ring.IsAttached || IsHeld(_ring);
        bool nozzleFixed = IsHeld(_nozzle);

        // Оба закреплены (или оба свободны) — тянуть некого.
        if (ringFixed == nozzleFixed) return;

        // Тянем СВОБОДНЫЙ конец к закреплённому.
        Transform anchor = ringFixed ? _ring.transform : _nozzle.transform;
        Transform free = ringFixed ? _nozzle.transform : _ring.transform;
        Rigidbody freeRb = ringFixed ? _nozzleRb : _ringRb;

        if (freeRb == null || freeRb.isKinematic) return;

        Vector3 to = anchor.position - free.position;
        float dist = to.magnitude;
        float excess = dist - _hoseLength;
        if (excess <= _threshold) return;

        // Горизонтальная тяга (чтобы не задирало вверх и не колбасило).
        Vector3 dir = to;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Vector3 v = freeRb.linearVelocity; v.y = 0f;
        if (v.magnitude >= _maxSpeed) return;

        float force = Mathf.Min(excess * _pullForce, _maxPull);
        freeRb.AddForce(dir * force, ForceMode.Acceleration);
    }

    /// <summary>Держит ли предмет игрок (у него есть владелец).</summary>
    private bool IsHeld(InteractableItem item)
    {
        if (item == null) return false;
        NetworkObject nob = item.NetworkObject;
        return nob != null && nob.Owner != null && nob.Owner.IsValid;
    }

    private void OnDrawGizmosSelected()
    {
        if (_ring == null || _nozzle == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(_ring.transform.position, _nozzle.transform.position);
    }
}
