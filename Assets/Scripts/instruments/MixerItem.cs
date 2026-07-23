using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Миксер (строительный/кухонный). Инструмент в руке.
///
///   ЛКМ (удержание) — крутится насадка (плавный разгон/торможение).
///   G              — бросить (база).
///
/// «ПРИКОЛЬНАЯ ШТУКА» вместо вылета диска:
///   Когда насадка (щуп) попадает в заданные слои (материал/земля), момент
///   «уходит в рукоятку»: игрок начинает крутиться вокруг заданной оси, а САМ
///   МИКСЕР ПРИКАЛЫВАЕТСЯ В МИРЕ на месте застревания — насадка стоит в земле и
///   НЕ уезжает по дуге вслед за игроком. Из подвижных частей крутятся только
///   «ручки» (_handles). Пока крутит — у игрока отключён FirstPersonController,
///   поэтому нет гравитации: не падает и не проваливается. Закрутка защёлкивается
///   (держится, пока зажата ЛКМ). Отпустил — игрок выпрямляется, миксер снова
///   едет за рукой.
///
/// НЕ ТОНЕТ В ЗЕМЛЮ: пока миксер в руке (и не приколот), кончик насадки не
/// пускается ниже поверхности земли (луч по _groundMask).
///
/// СЕТЬ: _spinning/_stuck — SyncVar. Вращение насадки/ручек — локальный визуал
/// у всех по флагам; закрутка тела и приколка — у владельца, позиция расходится
/// через client-authoritative NetworkTransform.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody (Collision Detection = Continuous желательно).
/// </summary>
public class MixerItem : InteractableItem
{
    [Header("Миксер — насадка (крутится в обычном режиме)")]
    [Tooltip("Дочерний меш вращающейся насадки")]
    [SerializeField] private Transform _attachment;
    [Tooltip("Ось вращения насадки в ЕЁ локальных координатах")]
    [SerializeField] private Vector3 _attachmentSpinAxis = Vector3.up;
    [Tooltip("Макс. скорость вращения насадки, град/сек")]
    [SerializeField] private float _spinSpeed = 1500f;
    [SerializeField] private float _spinUpTime = 0.4f;
    [SerializeField] private float _spinDownTime = 0.7f;

    [Header("Ручки (крутятся, ПОКА крутится игрок)")]
    [Tooltip("Часть-ручки. Положи обе рукоятки под один пустой объект и назначь " +
             "его сюда. Пусто = при закрутке игрока миксер полностью статичен.")]
    [SerializeField] private Transform _handles;
    [Tooltip("Ось вращения ручек в ИХ локальных координатах")]
    [SerializeField] private Vector3 _handlesSpinAxis = Vector3.up;
    [Tooltip("Скорость вращения ручек, град/сек")]
    [SerializeField] private float _handlesSpinSpeed = 260f;

    [Header("Ввод")]
    [Tooltip("ВКЛ — крутить, пока ЛКМ зажата. ВЫКЛ — ЛКМ переключает кручение.")]
    [SerializeField] private bool _holdToSpin = true;

    [Header("Щуп застревания (сфера в точке насадки)")]
    [Tooltip("Точка проверки — кончик насадки (дочерний пустой объект)")]
    [SerializeField] private Transform _probePoint;
    [SerializeField] private float _probeRadius = 0.15f;
    [Tooltip("Слои материала, в которых насадка застревает и запускает закрутку")]
    [SerializeField] private LayerMask _stuckMask;

    [Header("Не тонуть в землю (пока в руке)")]
    [SerializeField] private bool _keepAboveGround = true;
    [Tooltip("Слой(и) земли для проверки высоты")]
    [SerializeField] private LayerMask _groundMask;
    [SerializeField] private float _groundCheckUp = 0.6f;
    [SerializeField] private float _groundCheckDown = 2f;

    [Header("Закрутка игрока")]
    [Tooltip("Ось закрутки игрока (в инспектор, как оси техники). Дефолт Z.")]
    [SerializeField] private Vector3 _playerSpinAxis = new Vector3(0f, 0f, 1f);
    [Tooltip("World (фикс. ось мира) или Self (ось игрока)")]
    [SerializeField] private Space _playerSpinSpace = Space.World;
    [Tooltip("Скорость закрутки, град/сек (минус = в другую сторону)")]
    [SerializeField] private float _playerSpinSpeed = 260f;
    [Tooltip("Время раскрутки до полной скорости, сек")]
    [SerializeField] private float _playerSpinRampTime = 0.5f;

    [Header("Положение в руке (регулировка под модель)")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    // ───────── СЕТЕВОЕ СОСТОЯНИЕ ─────────
    private readonly SyncVar<bool> _spinning = new(false);
    private readonly SyncVar<bool> _stuck = new(false); // «замерла/приколота» для остальных

    // ───────── ЛОКАЛЬНОЕ ─────────
    private float _attachmentSpinScale; // 0..1 раскрутка насадки
    private float _handleSpinScale;     // 0..1 раскрутка ручек
    private bool _lastHeld;
    private bool _localStuck;
    private bool _spinLocked;           // защёлка закрутки игрока
    private float _playerSpinScale;     // 0..1 раскрутка игрока
    private Vector3 _lockedPos;         // приколотая поза миксера (насадка в земле)
    private Quaternion _lockedRot;
    private Transform _holderRoot;
    private FirstPersonController _holderFpc;
    private Transform _handAnchor;

    private const float MaxDt = 0.05f;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (_attachment != null) _attachment.gameObject.SetActive(true);
    }

    // ───────── КАЖДЫЙ КАДР ─────────

    private void Update()
    {
        UpdateVisual();

        if (base.IsOwner)
        {
            HandleOwnerInput();
            OwnerStuckAndSpin();
        }
    }

    /// <summary>Насадка крутится в обычном режиме; при закрутке игрока насадка
    /// замирает, а вместо неё крутятся ручки. Считается локально у всех клиентов
    /// по синхронизированным флагам.</summary>
    private void UpdateVisual()
    {
        float dt = Mathf.Min(Time.deltaTime, MaxDt);

        bool spinningNow = _spinning.Value;
        // frozen = «игрок крутится, миксер приколот»: у владельца сразу, у
        // остальных — по SyncVar.
        bool frozen = base.IsOwner ? _spinLocked : _stuck.Value;

        // Насадка — только когда НЕ заморожено.
        if (_attachment != null)
        {
            float target = (spinningNow && !frozen) ? 1f : 0f;
            float time = target > _attachmentSpinScale ? _spinUpTime : _spinDownTime;
            float step = time > 0.0001f ? dt / time : 1f;
            _attachmentSpinScale = Mathf.MoveTowards(_attachmentSpinScale, target, step);
            if (_attachmentSpinScale > 0.0001f)
                _attachment.Rotate(
                    _attachmentSpinAxis.normalized * (_spinSpeed * _attachmentSpinScale * dt),
                    Space.Self);
        }

        // Ручки — только когда ЗАМОРОЖЕНО (крутится игрок).
        if (_handles != null)
        {
            float target = (spinningNow && frozen) ? 1f : 0f;
            float time = target > _handleSpinScale ? _playerSpinRampTime : _spinDownTime;
            float step = time > 0.0001f ? dt / time : 1f;
            _handleSpinScale = Mathf.MoveTowards(_handleSpinScale, target, step);
            if (_handleSpinScale > 0.0001f)
                _handles.Rotate(
                    _handlesSpinAxis.normalized * (_handlesSpinSpeed * _handleSpinScale * dt),
                    Space.Self);
        }
    }

    private void HandleOwnerInput()
    {
        if (_holdToSpin)
        {
            bool held = Input.GetMouseButton(0);
            if (held != _lastHeld)
            {
                _lastHeld = held;
                CmdSetSpinning(held);
            }
        }
        else
        {
            if (Input.GetMouseButtonDown(0))
                CmdToggleSpin();
        }
    }

    private void OwnerStuckAndSpin()
    {
        bool probeStuck = false;
        if (_spinning.Value && _probePoint != null)
        {
            probeStuck = Physics.CheckSphere(
                _probePoint.position, _probeRadius, _stuckMask,
                QueryTriggerInteraction.Collide);
        }
        _localStuck = probeStuck;

        // Защёлка: старт при попадании в слой, держится пока зажата ЛКМ.
        bool shouldLock = _spinning.Value && (probeStuck || _spinLocked);
        if (shouldLock && !_spinLocked) StartSpinLock();
        else if (!shouldLock && _spinLocked) EndSpinLock();

        if (_spinLocked && _holderRoot != null)
        {
            float dt = Mathf.Min(Time.deltaTime, MaxDt);
            float ramp = _playerSpinRampTime > 0.0001f ? dt / _playerSpinRampTime : 1f;
            _playerSpinScale = Mathf.MoveTowards(_playerSpinScale, 1f, ramp);
            float angle = _playerSpinSpeed * _playerSpinScale * dt;
            _holderRoot.Rotate(_playerSpinAxis.normalized, angle, _playerSpinSpace);
        }
    }

    private void StartSpinLock()
    {
        _spinLocked = true;
        // Прикалываем миксер там, где насадка застряла — дальше он не двигается.
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;
        // Глушим контроллер: нет гравитации/ввода → игрок не падает и не тонет.
        if (_holderFpc != null) _holderFpc.enabled = false;
        if (!_stuck.Value) CmdSetStuck(true);
    }

    private void EndSpinLock()
    {
        _spinLocked = false;
        _playerSpinScale = 0f;
        RestoreHolderController();
        if (_stuck.Value) CmdSetStuck(false);
    }

    private void RestoreHolderController()
    {
        if (_holderFpc == null) return;
        _holderFpc.enabled = true;
        _holderFpc.ResetLookToCurrent(); // выпрямить крен/камеру после закрутки
    }

    // ───────── СЕРВЕР ─────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetSpinning(bool value)
    {
        if (_spinning.Value == value) return;
        _spinning.Value = value; // будущее: тут проверка питания от кабеля
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdToggleSpin()
    {
        _spinning.Value = !_spinning.Value;
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetStuck(bool value)
    {
        if (_stuck.Value == value) return;
        _stuck.Value = value;
    }

    // ───────── БРОСОК ─────────

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        if (_spinning.Value) _spinning.Value = false;
        if (_stuck.Value) _stuck.Value = false;
        _lastHeld = false;
        base.ServerOnDrop(hand, tossDirection, force);
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        if (_spinLocked)
        {
            _spinLocked = false;
            _playerSpinScale = 0f;
            RestoreHolderController();
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (_spinLocked)
        {
            _spinLocked = false;
            _playerSpinScale = 0f;
            RestoreHolderController();
        }
    }

    // ───────── ПОЗА В РУКЕ + приколка + «не тонуть» ─────────

    public override void UpdateHeldPose(Transform handAnchor)
    {
        _handAnchor = handAnchor;
        if (_holderFpc == null)
        {
            _holderFpc = handAnchor.GetComponentInParent<FirstPersonController>();
            if (_holderFpc != null) _holderRoot = _holderFpc.transform;
        }

        // Пока крутится игрок — миксер приколот в мире (насадка стоит в земле,
        // не едет по дуге за игроком). Ручки крутятся отдельно в UpdateVisual.
        if (_spinLocked)
        {
            transform.SetPositionAndRotation(_lockedPos, _lockedRot);
            return;
        }

        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);

        if (_keepAboveGround && _probePoint != null)
        {
            Vector3 tip = _probePoint.position;
            Vector3 from = tip + Vector3.up * _groundCheckUp;
            float dist = _groundCheckUp + _groundCheckDown;
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, dist,
                                _groundMask, QueryTriggerInteraction.Ignore))
            {
                if (tip.y < hit.point.y)
                    transform.position = pos + Vector3.up * (hit.point.y - tip.y);
            }
        }
    }

    // ───────── ГИЗМО ЩУПА ─────────

    private void OnDrawGizmosSelected()
    {
        if (_probePoint == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(_probePoint.position, _probeRadius);
    }
}
