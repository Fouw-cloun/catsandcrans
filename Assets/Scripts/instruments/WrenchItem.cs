using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Гаечный ключ. Предмет в руке.
///
///   F (ключ в руке, целясь в гайку) — насадить ключ на гайку.
///   F (целясь в насаженный ключ)    — забрать ключ обратно в руку.
///   ЛКМ зажата (целясь в ключ) + мышь ПО КРУГУ — крутить гайку.
///
/// ПРИНЦИП:
///   Насаженный ключ КАЖДЫЙ КАДР (LateUpdate) ставится по мировой позе точки
///   сцепки (WrenchSeat). Точка — ребёнок NutPivot, который крутится, поэтому
///   ключ крутится вместе со всей конструкцией (гайка + крышка + ключ).
///   SetParent не используем — в FishNet перепарентивание сетевого объекта
///   нестабильно.
///
///   Пока насажен: NetworkTransform ОТКЛЮЧЁН (иначе шлёт мировую позицию и
///   перетирает посадку), Rigidbody кинематичный, коллайдер ВКЛЮЧЁН (чтобы в
///   ключ можно было целиться).
///
/// ВАЖНО (был баг): храним ссылку на САМ HydrantNut, а НЕ на NetworkObject
/// гидранта. У гидранта 4 гайки, и все они висят на ОДНОМ NetworkObject —
/// по NOB нужную гайку не найти (GetComponent<HydrantNut>() на корне возвращал
/// null, и ключ просто не вставал на место).
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody.
/// </summary>
public class WrenchItem : InteractableItem
{
    [Header("Положение в руке (регулировка под модель)")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Посадка на гайку (крути прямо здесь, видно в Play)")]
    [Tooltip("Смещение относительно точки сцепки (в её локальных осях)")]
    [SerializeField] private Vector3 _mountPositionOffset = Vector3.zero;
    [Tooltip("Доворот ключа на гайке, градусы")]
    [SerializeField] private Vector3 _mountEulerOffset = Vector3.zero;

    /// <summary>Гайка, на которой сидит ключ (null = в руке/на земле).
    /// Ссылка именно на HydrantNut (NetworkBehaviour), а не на NetworkObject!</summary>
    private readonly SyncVar<NetworkBehaviour> _mountedNut = new(null);

    public bool IsMounted => _mountedNut.Value != null;

    /// <summary>Пока ключ на гайке — рука его НЕ ведёт.</summary>
    public override bool FollowHand => _mountedNut.Value == null;

    private NetworkTransform _nt;

    private HydrantNut Nut => _mountedNut.Value as HydrantNut;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        _nt = GetComponent<NetworkTransform>();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _mountedNut.OnChange += OnMountedChanged;
        if (_mountedNut.Value != null) ApplyMountedState(true); // поздний вход в игру
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _mountedNut.OnChange -= OnMountedChanged;
    }

    private void OnMountedChanged(NetworkBehaviour prev, NetworkBehaviour next, bool asServer)
        => ApplyMountedState(next != null);

    /// <summary>Режим «насажен»: глушим NetworkTransform (он перетирал бы позу
    /// мировой синхронизацией) и физику; коллайдер оставляем включённым.</summary>
    private void ApplyMountedState(bool mounted)
    {
        if (_nt != null) _nt.enabled = !mounted;

        if (mounted)
        {
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            if (_col != null) _col.enabled = true; // чтобы можно было прицелиться
        }
    }

    // ───────── КАЖДЫЙ КАДР: ключ сидит на точке сцепки ─────────

    /// <summary>LateUpdate — после всех Update, чтобы рука/физика не перебили позу.
    /// Точка сцепки крутится с пивотом → ключ крутится вместе с гайкой.</summary>
    private void LateUpdate()
    {
        HydrantNut nut = Nut;
        if (nut == null) return;

        Transform seat = nut.WrenchSeat != null ? nut.WrenchSeat : nut.NutPivot;
        if (seat == null) return;

        transform.SetPositionAndRotation(
            seat.position + seat.rotation * _mountPositionOffset,
            seat.rotation * Quaternion.Euler(_mountEulerOffset));

        if (_col != null && !_col.enabled) _col.enabled = true;
    }

    // ───────── СЕРВЕР: НАСАДИТЬ / СНЯТЬ ─────────

    /// <summary>Насадить ключ на КОНКРЕТНУЮ гайку (передаём сам HydrantNut).</summary>
    public void ServerMountTo(HydrantNut nut)
    {
        _mountedNut.Value = nut;
    }

    /// <summary>Снять ключ. hand != null — вернуть в руку; null — освободить (падает).</summary>
    public void ServerUnmount(PlayerHand hand)
    {
        _mountedNut.Value = null;   // OnChange вернёт NetworkTransform

        if (_nt != null) _nt.enabled = true;

        if (hand != null && !hand.HasItem)
        {
            hand.ServerTryHold(this);   // в руку (ownership + физика внутри)
        }
        else
        {
            NetworkObject.RemoveOwnership();
            SetPhysicsHeld(false);      // свободен: физика, падает
        }
    }

    // ───────── F ПО КЛЮЧУ: ЗАБРАТЬ ОБРАТНО ─────────

    public override void ServerInteract(PlayerInteractor interactor)
    {
        HydrantNut nut = Nut;
        if (nut != null)
        {
            if (interactor == null || interactor.Hand == null || interactor.Hand.HasItem)
                return; // рука занята — забрать некуда

            nut.ServerReleaseWrench(interactor.Hand);
            return;
        }

        base.ServerInteract(interactor); // обычный подбор с земли
    }

    // ───────── ПОЗА В РУКЕ ─────────

    public override void UpdateHeldPose(Transform handAnchor)
    {
        if (_mountedNut.Value != null) return; // на гайке — рука не ведёт

        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }
}
