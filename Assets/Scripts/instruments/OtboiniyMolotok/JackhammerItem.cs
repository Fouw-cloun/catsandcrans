using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Отбойный молоток. Инструмент в руке.
///
///   ЛКМ (удержание) — работает: инструмент дёргается/вибрирует.
///   G              — бросить (база).
///
/// ЩУП НА НОСИКЕ (как у миксера):
///     • НУЖНЫЙ слой (то, что долбим) → вибрирует ТОЛЬКО инструмент.
///     • НЕ нужный слой (камень/металл) → отдача: трясётся ещё и КАМЕРА игрока
///       (инструмент при этом продолжает трястись) — «весь игрок».
///
/// НАДЁЖНАЯ РЕАЛИЗАЦИЯ (после багов):
///   - Тряска инструмента — дрожь добавляется прямо к ПОЗЕ В РУКЕ у владельца,
///     остальным расходится через NetworkTransform. НЕ нужен отдельный меш.
///   - Тряска игрока — дрожим локальной позицией КАМЕРЫ у владельца (FPC крутит
///     наклон камеры поворотом, а мы двигаем позицию — не конфликтует).
///   - Флаги _toolVibrating/_playerVibrating — SyncVar, ставит владелец по щупу.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody (Is Kinematic СНЯТО, Use Gravity ВКЛ — иначе будет висеть в воздухе).
/// </summary>
public class JackhammerItem : InteractableItem
{
    [Header("Ввод")]
    [Tooltip("ВКЛ — работает, пока ЛКМ зажата. ВЫКЛ — ЛКМ переключает.")]
    [SerializeField] private bool _holdToRun = true;

    [Header("Щуп на носике (сфера)")]
    [Tooltip("Точка проверки — кончик носика (дочерний пустой объект)")]
    [SerializeField] private Transform _probePoint;
    [SerializeField] private float _probeRadius = 0.12f;
    [Tooltip("НУЖНЫЙ слой (то, что долбим) — вибрирует только инструмент")]
    [SerializeField] private LayerMask _correctMask;
    [Tooltip("НЕ нужный слой (камень/металл) — отдача, трясётся ещё и камера")]
    [SerializeField] private LayerMask _wrongMask;
    [Tooltip("ВКЛ — инструмент дрожит на ЛКМ всегда. ВЫКЛ — только когда носик в слое.")]
    [SerializeField] private bool _toolVibratesInAir = true;

    [Header("Тряска инструмента (в руке)")]
    [Tooltip("Амплитуда смещения инструмента, метры")]
    [SerializeField] private float _toolShakePos = 0.02f;
    [Tooltip("Амплитуда поворота инструмента, градусы")]
    [SerializeField] private float _toolShakeAngle = 2f;
    [Tooltip("Частота тряски инструмента")]
    [SerializeField] private float _toolShakeFreq = 40f;

    [Header("Тряска игрока/камеры (неправильный слой)")]
    [Tooltip("Амплитуда тряски камеры, метры")]
    [SerializeField] private float _playerShakePos = 0.05f;
    [Tooltip("Частота тряски камеры")]
    [SerializeField] private float _playerShakeFreq = 30f;

    [Header("Положение в руке (регулировка под модель)")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Оседание при спавне")]
    [Tooltip("Сколько секунд после спавна предмет заморожен (нейтрален), чтобы " +
             "не дёргаться при загрузке сцены. Потом включается обычная физика.")]
    [SerializeField] private float _spawnSettleTime = 5f;

    // ───────── СЕТЕВОЕ СОСТОЯНИЕ ─────────
    private readonly SyncVar<bool> _toolVibrating = new(false);
    private readonly SyncVar<bool> _playerVibrating = new(false);

    // ───────── ЛОКАЛЬНОЕ ─────────
    private bool _lastTool, _lastPlayer;
    private bool _runToggle;

    private Transform _holderCam;   // камера держателя (её позицию дрожим)
    private Vector3 _camBasePos;
    private bool _camBaseCached;

    private bool _spawnReleased;    // снята ли заморозка после оседания
    private float _spawnTime;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Физику предмета считает СЕРВЕР; на клиентах держим Rigidbody
        // кинематичным, визуал ведёт NetworkTransform. Иначе динамический RB на
        // клиенте дерётся с NT → предмет дрожит, лёжа на земле. Коллайдер не
        // трогаем, подбор работает. Сервер/хост остаётся динамическим (падает).
        if (!base.IsServerInitialized && _rb != null)
            _rb.isKinematic = true;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        // Замораживаем на _spawnSettleTime секунд, чтобы при загрузке сцены
        // предмет не дёргался от оседания физики. Потом — обычная физика.
        _spawnTime = Time.time;
        _spawnReleased = false;
        if (_rb != null) _rb.isKinematic = true;
    }

    // ───────── КАЖДЫЙ КАДР (только владелец) ─────────

    private void Update()
    {
        if (base.IsServerInitialized) ServerSpawnSettle();

        if (!base.IsOwner) return;
        OwnerDetect();
        UpdateCameraShake();
    }

    /// <summary>После оседания снимаем заморозку — но только если предмет не в
    /// руке (в руке физикой рулит база). Держит предмет нейтральным при спавне.</summary>
    private void ServerSpawnSettle()
    {
        if (_spawnReleased) return;
        if (Time.time - _spawnTime < _spawnSettleTime) return;
        _spawnReleased = true;

        bool held = base.Owner != null && base.Owner.IsValid;
        if (!held && _rb != null) _rb.isKinematic = false;
    }

    private void OwnerDetect()
    {
        bool running;
        if (_holdToRun) running = Input.GetMouseButton(0);
        else { if (Input.GetMouseButtonDown(0)) _runToggle = !_runToggle; running = _runToggle; }

        bool inWrong = false, inCorrect = false;
        if (running && _probePoint != null)
        {
            inWrong = Physics.CheckSphere(_probePoint.position, _probeRadius,
                                          _wrongMask, QueryTriggerInteraction.Collide);
            inCorrect = Physics.CheckSphere(_probePoint.position, _probeRadius,
                                            _correctMask, QueryTriggerInteraction.Collide);
        }

        // Инструмент дрожит, пока работает (в воздухе — по галке, либо в любом слое).
        bool wantTool = running && (_toolVibratesInAir || inCorrect || inWrong);
        // Камера трясётся только в НЕ нужном слое (отдача).
        bool wantPlayer = running && inWrong;

        if (wantTool != _lastTool || wantPlayer != _lastPlayer)
        {
            _lastTool = wantTool;
            _lastPlayer = wantPlayer;
            CmdSetVibration(wantTool, wantPlayer);
        }
    }

    /// <summary>Тряска камеры держателя (только позиция — не мешает наклону из
    /// FirstPersonController). Рука с инструментом висит рядом, так что зрительно
    /// «весь игрок» дёргается вместе с видом.</summary>
    private void UpdateCameraShake()
    {
        if (_holderCam == null || !_camBaseCached) return;

        if (_playerVibrating.Value)
            _holderCam.localPosition = _camBasePos + ShakeOffset(_playerShakePos, _playerShakeFreq, 100f);
        else
            _holderCam.localPosition = _camBasePos;
    }

    private static Vector3 ShakeOffset(float amp, float freq, float seed)
    {
        float t = Time.time * freq;
        return new Vector3(
            (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f * amp,
            (Mathf.PerlinNoise(t, seed + 11f) - 0.5f) * 2f * amp,
            (Mathf.PerlinNoise(t, seed + 23f) - 0.5f) * 2f * amp);
    }

    // ───────── СЕРВЕР ─────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetVibration(bool tool, bool player)
    {
        if (_toolVibrating.Value != tool) _toolVibrating.Value = tool;
        if (_playerVibrating.Value != player) _playerVibrating.Value = player;
    }

    // ───────── БРОСОК: стоп ─────────

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        if (_toolVibrating.Value) _toolVibrating.Value = false;
        if (_playerVibrating.Value) _playerVibrating.Value = false;
        _lastTool = false;
        _lastPlayer = false;
        _runToggle = false;
        base.ServerOnDrop(hand, tossDirection, force);
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        RestoreCamera();
        _runToggle = false;
        _lastTool = false;
        _lastPlayer = false;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        RestoreCamera();
    }

    private void RestoreCamera()
    {
        if (_holderCam != null && _camBaseCached)
            _holderCam.localPosition = _camBasePos;
    }

    // ───────── ПОЗА В РУКЕ + тряска инструмента ─────────

    public override void UpdateHeldPose(Transform handAnchor)
    {
        // Кэшируем камеру держателя (её позицию дрожим при отдаче).
        if (_holderCam == null)
        {
            FirstPersonController fpc = handAnchor.GetComponentInParent<FirstPersonController>();
            if (fpc != null)
            {
                Camera cam = fpc.GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    _holderCam = cam.transform;
                    _camBasePos = _holderCam.localPosition;
                    _camBaseCached = true;
                }
            }
        }

        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);

        // Тряска инструмента добавляется прямо к позе в руке (у владельца), NT
        // разошлёт остальным. Не зависит от отдельного меша — работает всегда.
        if (_toolVibrating.Value)
        {
            pos += handAnchor.rotation * ShakeOffset(_toolShakePos, _toolShakeFreq, 0f);
            rot *= Quaternion.Euler(ShakeOffset(_toolShakeAngle, _toolShakeFreq, 50f));
        }

        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────── ГИЗМО ЩУПА ─────────

    private void OnDrawGizmosSelected()
    {
        if (_probePoint == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(_probePoint.position, _probeRadius);
    }

    // ─── Наружу: для JackhammerFoodPounder ───
    //
    // Тот же приём, каким AngleGrinderItem отдаёт себя обработчику еды.
    // Отдаём ИМЕННО _toolVibrating, а не сырое нажатие ЛКМ: работой считается
    // то, что инструмент реально дёргается, и это одинаково видно всем
    // клиентам, а не только владельцу.
    //
    // Щуп отдаём свой, а не заводим второй в компоненте толчения: две точки на
    // одном носике однажды разъедутся при правке модели.

    /// <summary>Молоток работает прямо сейчас.</summary>
    public bool IsRunning => _toolVibrating.Value;

    /// <summary>Отдача — носик в неподходящем слое. Пригодится, если толчение
    /// захочет отличать удар по продукту от удара по дну ведра.</summary>
    public bool IsKicking => _playerVibrating.Value;

    /// <summary>Кончик носика. Тот же, которым проверяются слои.</summary>
    public Transform ProbePoint => _probePoint;
}
