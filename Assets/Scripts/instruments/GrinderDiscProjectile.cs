using FishNet.Object;
using UnityEngine;

/// <summary>
/// Улетающий (слетевший) диск болгарки. Физический обломок, который спавнит
/// сервер при слёте диска и толкает в случайную сторону.
///
/// СЕТЕВАЯ МОДЕЛЬ (как у "ничьих" объектов в проекте):
///   - Авторитет физики — СЕРВЕР: у него Rigidbody динамический, он считает
///     полёт и падение.
///   - У клиентов Rigidbody кинематичный, визуал ведёт NetworkTransform.
///   - Через _lifetime секунд сервер сам удаляет обломок (чтобы не копился мусор).
///
/// НАСТРОЙКА ПРЕФАБА:
///   - NetworkObject
///   - Rigidbody (Collision Detection = Continuous желательно)
///   - Collider
///   - NetworkTransform — здесь, В ОТЛИЧИЕ от техники/предметов, СНЯТЬ галку
///     "Client Authoritative" (объект серверный, авторитет у сервера).
///   - Этот скрипт.
///   - Добавить префаб в Spawnable Prefabs у NetworkManager (иначе не заспавнится).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class GrinderDiscProjectile : NetworkBehaviour
{
    [Tooltip("Через сколько секунд обломок сам удаляется")]
    [SerializeField] private float _lifetime = 15f;

    private Rigidbody _rb;
    private float _spawnTime;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _spawnTime = Time.time;
        // Сервер считает физику обломка.
        if (_rb != null) _rb.isKinematic = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // На чистом клиенте (не хосте) физику не считаем — визуал ведёт NT.
        if (!base.IsServerInitialized && _rb != null)
            _rb.isKinematic = true;
    }

    /// <summary>Вызывается сервером сразу после спавна: задать импульс полёта
    /// и кувыркание. Идёт только на сервере (авторитет физики).</summary>
    public void ServerLaunch(Vector3 velocity, Vector3 angularVelocity)
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = false;
        _rb.linearVelocity = velocity;
        _rb.angularVelocity = angularVelocity;
    }

    private void Update()
    {
        // Таймер удаления — только сервер.
        if (!base.IsServerInitialized) return;
        if (Time.time - _spawnTime >= _lifetime)
            base.Despawn();
    }
}
