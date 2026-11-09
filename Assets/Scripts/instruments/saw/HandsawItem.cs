using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// НОЖОВКА — предмет в руке, режет продукты на куски.
///
///   F   — взять / положить (база)
///   ЛКМ — ДЕРЖАТЬ: пока зажата, идёт пиление
///
/// Сам по себе инструмент ничего не режет: он только сообщает, что пилит, и
/// где у него полотно. Всю механику ведёт SawFoodProcessor на этом же объекте.
/// Разделение то же, что у болгарки: железка знает про себя, обработчик —
/// про еду.
///
/// СЕТЬ: состояние «пилю» — один SyncVar, команда шлётся только на смену
/// состояния, а не каждый кадр.
/// </summary>
/// <remarks>
/// Порядок выполнения поздний: позу камеры надо перебивать ПОСЛЕ того, как
/// её выставил контроллер игрока, иначе фиксация не сработает.
/// </remarks>
[DefaultExecutionOrder(1000)]
public class HandsawItem : InteractableItem, ISawTool
{
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Полотно")]
    [Tooltip("Дочерний объект — само полотно. От него считается подача вперёд " +
             "и место вылета крошек.")]
    [SerializeField] private Transform _blade;

    [Header("Звук")]
    [SerializeField] private AudioSource _sawLoop;

    private readonly SyncVar<bool> _sawing = new SyncVar<bool>(false);
    private bool _lastHeld;

    private FirstPersonController _holderFpc;
    private Transform _holderCam;
    private int _poseFrame = -10;      // когда рука последний раз вела предмет
    private bool _speedTaken;

    private Quaternion _frozenLook;
    private bool _lookFrozen;

    /// <summary>
    /// У ВЛАДЕЛЬЦА берём локальный флаг: остановка и старт пиления должны
    /// срабатывать в тот же кадр, а SyncVar приезжает только через круг до
    /// сервера и обратно. Остальным — синхронизированное значение.
    /// </summary>
    public bool IsSawing => base.IsOwner ? (_lastHeld || _sawing.Value) : _sawing.Value;
    public Transform Blade => _blade != null ? _blade : transform;
    public Transform AimOrigin => _holderCam;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        if (_blade == null) _blade = transform;
    }

    private void Update()
    {
        if (base.IsOwner)
        {
            OwnerInput();
            UpdateHolderSpeed();
        }
        UpdateSound();
    }

    /// <summary>
    /// ИГРОК СТОИТ, ПОКА ПИЛИТ.
    ///
    /// PlayerHand ставит SpeedMultiplier ОДИН РАЗ, в момент взятия предмета,
    /// а не каждый кадр. Поэтому вернуть ноль из CarrySpeedMultiplier
    /// недостаточно — его никто не перечитает. Пишем скорость сами.
    ///
    /// Отпускаем обратно в единицу, когда перестали пилить или выронили пилу,
    /// иначе игрок остался бы обездвижен навсегда.
    /// </summary>
    private void UpdateHolderSpeed()
    {
        bool held = Time.frameCount - _poseFrame <= 2;   // рука ведёт предмет
        bool freeze = held && _lastHeld;

        if (_holderFpc == null)
        {
            if (freeze) return;
            _speedTaken = false;
            return;
        }

        if (freeze)
        {
            _holderFpc.SpeedMultiplier = 0f;
            _speedTaken = true;
        }
        else if (_speedTaken)
        {
            _holderFpc.SpeedMultiplier = CarrySpeedMultiplier;
            _speedTaken = false;
            if (!held) _holderFpc = null;
        }
    }

    private void OwnerInput()
    {
        // Шлём команду ТОЛЬКО на смену состояния — идемпотентно, без спама
        // каждый кадр. Тот же приём, что у болгарки.
        bool held = Input.GetMouseButton(0);
        if (held == _lastHeld) return;

        _lastHeld = held;
        CmdSetSawing(held);

        Debug.Log($"[Ножовка] ЛКМ {(held ? "зажата" : "отпущена")}, " +
                  $"контроллер игрока {(_holderFpc != null ? "найден" : "НЕ НАЙДЕН")}");
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetSawing(bool value)
    {
        if (_sawing.Value == value) return;
        _sawing.Value = value;
    }

    /// <summary>
    /// КАМЕРА СТОИТ, ПОКА ПИЛИШЬ. Мышь при этом целиком уходит в ход пилы.
    ///
    /// Раньше камеру каждый кадр возвращали силой, а контроллер тем временем копил
    /// наклон от движений мыши; отпуская, звали ResetLookToCurrent — он обнуляет наклон,
    /// и камера улетала на горизонт. Теперь просим контроллер не крутить обзор
    /// (HoldLook каждый кадр) — углы не меняются, и после ЛКМ камера там же, куда смотрел.
    /// </summary>
    private void UpdateLookFreeze()
    {
        bool held = IsHeld;
        bool freeze = held && _lastHeld && _holderCam != null;

        if (freeze)
        {
            if (!_lookFrozen)
            {
                _frozenLook = _holderCam.rotation;
                _lookFrozen = true;
                if (_holderFpc == null)
                    Debug.LogWarning("[Ножовка] нет First Person Controller у держателя — камеру держу вручную", this);
            }
            if (_holderFpc != null) _holderFpc.HoldLook();       // обзор на паузе, углы целы
            else _holderCam.rotation = _frozenLook;              // запасной путь
            return;
        }

        if (!_lookFrozen) return;
        _lookFrozen = false;                                     // HoldLook больше не зовём — обзор вернётся сам, без рывка
    }

    /// <summary>Пила сейчас в руке (рука вела её в последние кадры).</summary>
    public bool IsHeld => Time.frameCount - _poseFrame <= 2;

    private void LateUpdate()
    {
        if (base.IsOwner || _lookFrozen) UpdateLookFreeze();   // _lookFrozen — чтобы отпустить и после потери владения
    }

    /// <summary>Звук — локальная реакция на состояние, по сети не гоняется.</summary>
    private void UpdateSound()
    {
        if (_sawLoop == null) return;

        if (_sawing.Value && !_sawLoop.isPlaying) _sawLoop.Play();
        else if (!_sawing.Value && _sawLoop.isPlaying) _sawLoop.Stop();
    }

    /// <summary>Выронил из рук — пиление прекращается само.</summary>
    public override void ServerOnDrop(PlayerHand hand, Vector3 dir, float force = 0f)
    {
        _sawing.Value = false;
        _lastHeld = false;

        // Вернуть подвижность: иначе выронив пилу в момент пиления,
        // игрок остался бы стоять столбом.
        if (_holderFpc != null)
        {
            _holderFpc.SpeedMultiplier = 1f;
            _lookFrozen = false;                 // обзор вернётся сам — HoldLook больше не зовётся
            _holderFpc = null;
            _holderCam = null;
            _speedTaken = false;
        }
        base.ServerOnDrop(hand, dir, force);
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        // Вызывается PlayerHand каждый кадр, пока предмет в руке — по этому
        // и понимаем, что нас держат, и через кого добраться до контроллера.
        _poseFrame = Time.frameCount;
        if (_holderFpc == null)
            _holderFpc = handAnchor.GetComponentInParent<FirstPersonController>();

        // Камеру берём через держателя — так это точно его камера, а не первая
        // попавшаяся в сцене.
        if (_holderCam == null && _holderFpc != null)
        {
            Camera c = _holderFpc.GetComponentInChildren<Camera>(true);
            if (c != null) _holderCam = c.transform;
        }

        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }
}
