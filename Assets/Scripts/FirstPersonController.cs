using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Движение и обзор от первого лица. Owner-authoritative: ввод читает только
/// владелец, NetworkTransform (Client Authoritative) синхронизирует позицию и
/// поворот тела остальным. Наклон камеры (pitch) — локальный, не синхронизируется.
///
/// ЗАМЕНЯЕТ старые скрипты PlayerMovement.cs и OwnerCamera.cs — удали их с префаба.
///
/// Структура префаба игрока:
///   Player (CharacterController, NetworkObject, NetworkTransform, этот скрипт)
///    └─ CameraRoot (пустой объект на высоте головы, ~1.6 по Y)
///        └─ Main Camera (тег MainCamera, + AudioListener)
/// Перетащи CameraRoot в поле Camera Root.
///
/// ВВОД: старый Input Manager. Если включён только новый Input System —
/// Project Settings → Player → Active Input Handling → Both.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : NetworkBehaviour
{
    [Header("Движение")]
    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField] private float _gravity = -20f;

    [Header("Мышь")]
    [Tooltip("Пустой объект на высоте головы, к нему прикреплена камера")]
    [SerializeField] private Transform _cameraRoot;
    [SerializeField] private float _mouseSensitivity = 2f;
    [SerializeField] private float _minPitch = -80f;
    [SerializeField] private float _maxPitch = 80f;

    [Header("Толчок/шланг (вышибание, упор)")]
    [Tooltip("Как быстро гаснет толчок от струи, ед/сек")]
    [SerializeField] private float _knockbackDrag = 8f;

    private Vector3 _externalVelocity; // толчок струёй, затухает
    private CharacterController _controller;
    private Camera _camera;
    private AudioListener _listener;
    private float _pitch;
    private float _verticalVelocity;

    /// <summary>Множитель скорости (1 = норма). Снижается при переноске тяжёлого.</summary>
    [System.NonSerialized] public float SpeedMultiplier = 1f;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (_cameraRoot != null)
        {
            _camera = _cameraRoot.GetComponentInChildren<Camera>(true);
            _listener = _cameraRoot.GetComponentInChildren<AudioListener>(true);
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        bool owner = base.IsOwner;

        // Камера и AudioListener активны ТОЛЬКО у владельца, иначе будет
        // конфликт нескольких камер и слушателей звука в сцене.
        if (_camera != null) _camera.enabled = owner;
        if (_listener != null) _listener.enabled = owner;

        if (!owner)
        {
            // Чужим персонажем не управляем — выключаем скрипт.
            enabled = false;
            return;
        }

        LockCursor(true);
    }

    private void Update()
    {
        if (!base.IsOwner) return;

        // Esc — освободить курсор, ЛКМ — снова захватить (удобно при тесте).
        if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

        if (Cursor.lockState == CursorLockMode.Locked) Look();
        Move();
    }

    private void Look()
    {
        float mouseX = Input.GetAxisRaw("Mouse X") * _mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * _mouseSensitivity;

        // Горизонталь — поворот всего тела (синхронизируется NetworkTransform).
        transform.Rotate(Vector3.up, mouseX);

        // Вертикаль — наклон только камеры (локально).
        _pitch = Mathf.Clamp(_pitch - mouseY, _minPitch, _maxPitch);
        if (_cameraRoot != null)
            _cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void Move()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 move = transform.right * h + transform.forward * v;
        if (move.sqrMagnitude > 1f) move.Normalize();

        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -1f;
        _verticalVelocity += _gravity * Time.deltaTime;

        Vector3 velocity = move * (_moveSpeed * SpeedMultiplier);
        velocity.y = _verticalVelocity;
        velocity += _externalVelocity;          // толчок от струи (вышибание)
        _controller.Move(velocity * Time.deltaTime);

        // Затухание толчка.
        _externalVelocity = Vector3.MoveTowards(_externalVelocity, Vector3.zero,
                                                _knockbackDrag * Time.deltaTime);
    }

    private void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    /// <summary>Выровнять взгляд после выхода из техники: убрать крен/тангаж
    /// тела (оставить только рыскание по Y) и обнулить наклон камеры. Нужно,
    /// потому что пока игрок сидел, его transform приравнивался к сиденью,
    /// которое могло крутиться/наклоняться (башня крана) — без сброса камеру
    /// после выхода мотает и переворачивает.</summary>
    public void ResetLookToCurrent()
    {
        // Тело — только горизонтальное направление, без крена и тангажа.
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

        // Наклон камеры — в ноль, и синхронизируем внутреннее поле _pitch.
        _pitch = 0f;
        if (_cameraRoot != null)
            _cameraRoot.localRotation = Quaternion.identity;

        _verticalVelocity = 0f;
    }

    // ───────── ШЛАНГ / ВЫШИБАНИЕ ─────────

    /// <summary>ПОСТОЯННАЯ реактивная тяга (полёт на струе брандспойта).
    /// Зовётся каждый FixedUpdate с сервера, пока льёт вода.</summary>
    public void ServerApplyThrust(Vector3 accel)
    {
        TargetThrust(base.Owner, accel);
    }

    [TargetRpc]
    private void TargetThrust(NetworkConnection conn, Vector3 accel)
    {
        // Прибавляем к внешней скорости; она же гасится дрэгом в Move().
        _externalVelocity += accel * Time.fixedDeltaTime;
        // Пока тянет — гасим падение, иначе гравитация съедает подъём.
        if (accel.y > 0.1f && _verticalVelocity < 0f) _verticalVelocity = 0f;
    }

    /// <summary>Толчок игрока струёй (вышибание при снятии кольца под напором).
    /// Зовётся на СЕРВЕРЕ — отправляем владельцу, движение считает он.</summary>
    public void ServerApplyKnockback(Vector3 velocity)
    {
        TargetKnockback(base.Owner, velocity);
    }

    [TargetRpc]
    private void TargetKnockback(NetworkConnection conn, Vector3 velocity)
    {
        _externalVelocity += velocity; // добавится к движению и плавно погаснет
    }

    /// <summary>Не пустить дальше длины шланга — вернуть на границу (упор как в
    /// стенку). Обычно зовётся уже у владельца (из NozzleGunItem.Update); если
    /// вызвано на сервере — переправляем владельцу.</summary>
    public void ServerClampPosition(Vector3 worldPos)
    {
        if (base.IsOwner) ApplyClamp(worldPos);
        else TargetClamp(base.Owner, worldPos);
    }

    [TargetRpc]
    private void TargetClamp(NetworkConnection conn, Vector3 worldPos) => ApplyClamp(worldPos);

    private void ApplyClamp(Vector3 worldPos)
    {
        // Двигаем CharacterController обратно к границе по горизонтали.
        Vector3 delta = worldPos - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude > 0.0000001f)
            _controller.Move(delta);
    }
}
