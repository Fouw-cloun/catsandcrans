using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Бетономешалка. СТАЦИОНАРНАЯ машина — в руки не берётся. Управление через
/// части-IVehiclePart (кнопки/ручка): наводишь камеру, жмёшь F.
///
///   Зелёная кнопка (F)  → барабан крутится (по своей оси, обычно Z).
///   Красная кнопка (F)  → барабан останавливается.
///   Ручка (зажать F + водить камерой влево/вправо) → бочка ПОВОРАЧИВАЕТСЯ
///       пропорционально движению камеры (в ту сторону, куда тянешь).
///
/// СЕТЬ (по паттерну проекта):
///   - _spinning — SyncVar; вращение барабана считается локально у всех по флагу
///     (как диск болгарки), с плавным пуском/остановкой.
///   - _turnAngle — SyncVar (абсолютный угол поворота бочки). Грабер (кто держит
///     ручку) считает угол ЛОКАЛЬНО по движению камеры и шлёт абсолютное значение
///     (идемпотентно); у остальных угол сглаживается MoveTowards.
///   - _grabber — SyncVar: кто сейчас держит ручку.
///
/// СТРУКТУРА (важно):
///   BarrelPivot (пустышка, ПОВОРАЧИВАЕТСЯ ручкой)
///     └─ Drum (меш барабана, КРУТИТСЯ по кнопкам)
///   Барабан — ребёнок пивота, поэтому крутится и поворачивается вместе. Нельзя
///   вешать вращение и поворот на ОДИН объект (поворот затрёт накопленное
///   вращение). Оси вращения/поворота — в инспекторе (готча проекта).
///
/// Требует: NetworkObject. Кнопки/ручка — дочерние объекты с коллайдерами и
/// компонентами MixerButtonPart / MixerHandlePart.
/// </summary>
public class ConcreteMixer : NetworkBehaviour
{
    [Header("Барабан (крутится по кнопкам)")]
    [Tooltip("Меш барабана, который крутится. Должен быть ребёнком Barrel Pivot.")]
    [SerializeField] private Transform _drum;
    [Tooltip("Ось вращения барабана в ЕГО локальных координатах (обычно Z)")]
    [SerializeField] private Vector3 _drumSpinAxis = new Vector3(0f, 0f, 1f);
    [Tooltip("Скорость вращения барабана, град/сек")]
    [SerializeField] private float _drumSpinSpeed = 180f;
    [SerializeField] private float _spinUpTime = 0.6f;
    [SerializeField] private float _spinDownTime = 1.0f;

    [Header("Поворот бочки (ручка)")]
    [Tooltip("Пивот поворота — РОДИТЕЛЬ барабана. Его крутит ручка.")]
    [SerializeField] private Transform _barrelPivot;
    [Tooltip("Ось поворота бочки в локальных координатах пивота (подгони под модель)")]
    [SerializeField] private Vector3 _barrelTurnAxis = new Vector3(0f, 1f, 0f);
    [SerializeField] private float _turnMin = -90f;
    [SerializeField] private float _turnMax = 90f;
    [Tooltip("Чувствительность: градусов поворота на единицу движения камеры")]
    [SerializeField] private float _turnSensitivity = 2f;
    [Tooltip("Скорость сглаживания поворота у не-грабера, град/сек")]
    [SerializeField] private float _turnSmoothSpeed = 180f;
    [Tooltip("Если грабер отошёл дальше — ручка отпускается")]
    [SerializeField] private float _grabRange = 5f;

    // ───────── СЕТЕВОЕ СОСТОЯНИЕ ─────────
    private readonly SyncVar<bool> _spinning = new(false);
    private readonly SyncVar<float> _turnAngle = new(0f);
    private readonly SyncVar<NetworkObject> _grabber = new(null);

    // ───────── ЛОКАЛЬНОЕ ─────────
    private float _spinScale;         // 0..1 раскрутка барабана
    private Quaternion _barrelBaseRot; // базовый поворот пивота (готча #3)
    private float _localTurn;         // угол у грабера (без задержки)
    private float _displayTurn;       // сглаженный угол у остальных
    private const float MaxDt = 0.05f;

    private void Awake()
    {
        if (_barrelPivot != null) _barrelBaseRot = _barrelPivot.localRotation;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _grabber.OnChange += OnGrabberChanged;
        _localTurn = _turnAngle.Value;
        _displayTurn = _turnAngle.Value;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _grabber.OnChange -= OnGrabberChanged;
    }

    private void OnGrabberChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        // Стал грабером — продолжаем с текущего угла (без рывка).
        if (next != null && next.IsOwner)
            _localTurn = _turnAngle.Value;
    }

    // ───────── КАЖДЫЙ КАДР ─────────

    private void Update()
    {
        UpdateDrumVisual();

        // Если ЭТО я держу ручку — тяну её камерой.
        if (_grabber.Value != null && _grabber.Value.IsOwner)
            GrabberDragHandle();

        UpdateTurnVisual();
    }

    /// <summary>Вращение барабана локально по флагу _spinning, плавный пуск/стоп.</summary>
    private void UpdateDrumVisual()
    {
        if (_drum == null) return;
        float dt = Mathf.Min(Time.deltaTime, MaxDt);
        float goal = _spinning.Value ? 1f : 0f;
        float t = goal > _spinScale ? _spinUpTime : _spinDownTime;
        float step = t > 0.0001f ? dt / t : 1f;
        _spinScale = Mathf.MoveTowards(_spinScale, goal, step);
        if (_spinScale <= 0.0001f) return;
        // Space.Self — инкрементально, базовый оффсет барабана сохраняется.
        _drum.Rotate(_drumSpinAxis.normalized * (_drumSpinSpeed * _spinScale * dt), Space.Self);
    }

    /// <summary>Грабер: пока F зажата — крутим угол по движению камеры (Mouse X);
    /// отпустил F или отошёл — отпускаем ручку.</summary>
    private void GrabberDragHandle()
    {
        // Отошёл слишком далеко — отпустить.
        if (Vector3.Distance(_grabber.Value.transform.position, transform.position) > _grabRange)
        {
            CmdReleaseHandle();
            return;
        }

        if (Input.GetKey(KeyCode.F))
        {
            float dx = Input.GetAxisRaw("Mouse X");
            if (Mathf.Abs(dx) > 0.0001f)
            {
                _localTurn = Mathf.Clamp(_localTurn + dx * _turnSensitivity, _turnMin, _turnMax);
                CmdSetTurn(_localTurn); // идемпотентная отправка абсолютного угла
            }
        }
        else
        {
            CmdReleaseHandle();
        }
    }

    /// <summary>Показ поворота бочки. Грабер — сразу свой угол (без задержки),
    /// остальные — сглаживают к синхронизированному значению.</summary>
    private void UpdateTurnVisual()
    {
        if (_barrelPivot == null) return;

        bool iAmGrabber = _grabber.Value != null && _grabber.Value.IsOwner;
        float target;
        if (iAmGrabber)
        {
            target = _localTurn;
        }
        else
        {
            _displayTurn = Mathf.MoveTowards(_displayTurn, _turnAngle.Value,
                                             _turnSmoothSpeed * Mathf.Min(Time.deltaTime, MaxDt));
            target = _displayTurn;
        }

        _barrelPivot.localRotation = _barrelBaseRot * Quaternion.AngleAxis(target, _barrelTurnAxis.normalized);
        _displayTurn = target; // держим синхроном для плавного перехода после отпускания
    }

    // ───────── ВЫЗЫВАЕТСЯ ЧАСТЯМИ (на сервере) ─────────

    /// <summary>Кнопки: пуск/стоп барабана.</summary>
    public void ServerSetSpinning(bool value)
    {
        if (_spinning.Value != value) _spinning.Value = value;
    }

    /// <summary>Ручка: захват. Записываем грабера (если свободна).</summary>
    public void ServerGrab(PlayerInteractor interactor)
    {
        if (interactor == null) return;
        if (_grabber.Value != null) return; // уже держат
        _grabber.Value = interactor.NetworkObject;
    }

    // ───────── СЕРВЕРНЫЕ RPC ОТ ГРАБЕРА ─────────

    [ServerRpc(RequireOwnership = false)]
    private void CmdSetTurn(float value, NetworkConnection conn = null)
    {
        // Принимаем угол только от того, кто реально держит ручку.
        if (_grabber.Value == null || _grabber.Value.Owner != conn) return;
        _turnAngle.Value = Mathf.Clamp(value, _turnMin, _turnMax);
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdReleaseHandle(NetworkConnection conn = null)
    {
        if (_grabber.Value != null && _grabber.Value.Owner == conn)
            _grabber.Value = null;
    }
}
