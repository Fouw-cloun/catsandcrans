using FishNet.Object;
using UnityEngine;

/// <summary>
/// Гвоздь-снаряд из гвоздепистолета. Спавнит и запускает сервер.
///
/// СЕТЬ (как «ничьи» объекты проекта):
///   - Авторитет физики — СЕРВЕР (Rigidbody динамический, летит и падает).
///   - У клиентов Rigidbody кинематичный, визуал ведёт NetworkTransform.
///   - Через _lifetime секунд сервер сам удаляет гвоздь.
///
/// НАСТРОЙКА ПРЕФАБА:
///   - NetworkObject, Rigidbody (Collision Detection = Continuous!), Collider,
///     NetworkTransform — здесь СНЯТЬ «Client Authoritative» (объект серверный).
///   - Этот скрипт.
///   - Добавить префаб в Spawnable Prefabs у NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class NailProjectile : NetworkBehaviour
{
    [Tooltip("Через сколько секунд гвоздь сам удаляется (перебивается пистолетом)")]
    [SerializeField] private float _lifetime = 15f;

    private Rigidbody _rb;
    private float _spawnTime;

    private void Awake() => _rb = GetComponent<Rigidbody>();

    public override void OnStartServer()
    {
        base.OnStartServer();
        _spawnTime = Time.time;
        if (_rb != null)
        {
            _rb.isKinematic = false;
            // Непрерывный детект столкновений — чтобы быстрый гвоздь не прошивал
            // землю. Ставим из кода, чтобы не зависеть от настройки префаба.
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!base.IsServerInitialized && _rb != null)
            _rb.isKinematic = true; // не сервер — не считаем физику, ведёт NT
    }

    /// <summary>Вызывается сервером сразу после спавна: задать скорость, вращение
    /// и время жизни. Только на сервере (авторитет физики).</summary>
    public void ServerLaunch(Vector3 velocity, Vector3 angularVelocity, float lifetime)
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        _lifetime = lifetime;
        _spawnTime = Time.time;
        _rb.isKinematic = false;
        _rb.linearVelocity = velocity;
        _rb.angularVelocity = angularVelocity;
    }

    private void Update()
    {
        if (!base.IsServerInitialized) return;
        if (Time.time - _spawnTime >= _lifetime) base.Despawn();
    }
}
