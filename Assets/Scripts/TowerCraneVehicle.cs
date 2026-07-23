using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Башенный кран. НЕ ездит (стоит на месте), поэтому не наследует
/// колёсную/гусеничную физику — только сиденье + кинематическое управление.
///   A/D — поворот башни, W/S — каретка (слайдер) по стреле вперёд/назад,
///   R/F — крюк вверх/вниз (меняется длина подвеса), F — выйти.
///
/// Крюк — ОТДЕЛЬНОЕ физическое тело (Rigidbody), висящее на каретке и
/// раскачивающееся (болтанка). Управление крюком меняет длину троса
/// (linearLimit у ConfigurableJoint), физика сама даёт качание.
///
/// Архитектура значений — как у остальной техники: считаем ЛОКАЛЬНО у водителя
/// по Time.deltaTime, шлём АБСОЛЮТНЫЕ значения на сервер (идемпотентно),
/// не-водителю сглаживаем. Крюк-физику симулирует authority (водитель/сервер).
/// </summary>
public class TowerCraneVehicle : NetworkVehicle
{
    [Header("Части крана")]
    [Tooltip("Башня со стрелой — вращается вокруг вертикали")]
    [SerializeField] private Transform _tower;
    [Tooltip("Локальная ось вращения башни (у этой модели — Y). Поменяй, если не туда.")]
    [SerializeField] private Vector3 _towerAxis = Vector3.up;
    [Tooltip("Каретка (слайдер) — ездит по стреле")]
    [SerializeField] private Transform _trolley;
    [Tooltip("Локальная ось движения каретки — у тебя X")]
    [SerializeField] private Vector3 _trolleyAxis = Vector3.right;

    [Header("Крюк (физический, болтается на тросе)")]
    [Tooltip("ConfigurableJoint крюка — через его linearLimit меняем длину троса")]
    [SerializeField] private ConfigurableJoint _hookJoint;
    [Tooltip("LineRenderer троса (опционально) — рисуется от каретки до крюка")]
    [SerializeField] private LineRenderer _rope;
    [SerializeField] private Transform _ropeTopPoint;  // верх троса (на каретке)
    [SerializeField] private Transform _hookTransform;  // сам крюк (низ троса)

    [Header("Скорости")]
    [SerializeField] private float _rotateSpeed = 25f;
    [SerializeField] private float _trolleySpeed = 3f;
    [SerializeField] private float _hookSpeed = 4f;
    [SerializeField] private float _visualSmoothSpeed = 90f;

    [Header("Пределы")]
    [Tooltip("Мин/макс позиция каретки по своей оси (локальные единицы)")]
    [SerializeField] private Vector2 _trolleyLimit = new Vector2(2f, 25f);
    [Tooltip("Мин/макс длина троса (linearLimit крюка)")]
    [SerializeField] private Vector2 _hookLimit = new Vector2(1f, 30f);

    // Авторитетные значения.
    private readonly SyncVar<float> _rotate = new(0f);
    private readonly SyncVar<float> _trolley01 = new(0f); // позиция каретки
    private readonly SyncVar<float> _hookLen = new(2f);   // длина троса

    // Локальные (у водителя, без задержки).
    private float _localRotate, _localTrolley, _localHook = 2f;
    // Визуальные (сглаженные у не-водителя).
    private float _visRotate, _visTrolley, _visHook;

    private Quaternion _towerBaseRot;
    private Vector3 _trolleyBasePos;
    private bool _baseCaptured;

    protected override void DriverUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f); // потолок против скачка кадра

        float rot = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float tro = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        // Крюк: R — вверх, G — вниз. F НЕ используем для крюка, т.к. F —
        // это выход из техники (обрабатывается в базовом Update). Развели, чтобы
        // не конфликтовало: одно нажатие F выходит, а крюк на отдельных клавишах.
        float hk = (Input.GetKey(KeyCode.R) ? 1f : 0f) - (Input.GetKey(KeyCode.G) ? 1f : 0f);

        _localRotate += rot * _rotateSpeed * dt;
        _localTrolley = Mathf.Clamp(_localTrolley + tro * _trolleySpeed * dt, _trolleyLimit.x, _trolleyLimit.y);
        _localHook = Mathf.Clamp(_localHook + hk * _hookSpeed * dt, _hookLimit.x, _hookLimit.y);
    }

    protected override void DriverTick()
    {
        CmdCraneInput(_localRotate, _localTrolley, _localHook);
    }

    [ServerRpc]
    private void CmdCraneInput(float rotate, float trolley, float hook)
    {
        _rotate.Value = rotate;
        _trolley01.Value = Mathf.Clamp(trolley, _trolleyLimit.x, _trolleyLimit.y);
        _hookLen.Value = Mathf.Clamp(hook, _hookLimit.x, _hookLimit.y);
    }

    protected override void ApplyVisuals()
    {
        if (!_baseCaptured)
        {
            if (_tower != null) _towerBaseRot = _tower.localRotation;
            if (_trolley != null) _trolleyBasePos = _trolley.localPosition;
            _visRotate = _localRotate = _rotate.Value;
            _visTrolley = _localTrolley = _trolley01.Value;
            _visHook = _localHook = _hookLen.Value;
            _baseCaptured = true;
        }

        float dt = Time.deltaTime;
        if (_isLocalDriver)
        {
            _visRotate = _localRotate;
            _visTrolley = _localTrolley;
            _visHook = _localHook;
        }
        else
        {
            _visRotate = Mathf.MoveTowards(_visRotate, _rotate.Value, _visualSmoothSpeed * dt);
            _visTrolley = Mathf.MoveTowards(_visTrolley, _trolley01.Value, _trolleySpeed * 4f * dt);
            _visHook = Mathf.MoveTowards(_visHook, _hookLen.Value, _hookSpeed * 4f * dt);
        }

        // Поворот башни.
        if (_tower != null)
            _tower.localRotation = _towerBaseRot * Quaternion.AngleAxis(_visRotate, _towerAxis);

        // Каретка по своей оси (от базовой позиции).
        if (_trolley != null)
        {
            Vector3 axis = _trolleyAxis.normalized;
            // Заменяем компоненту вдоль оси на текущее значение, остальное — база.
            Vector3 lp = _trolleyBasePos;
            if (Mathf.Abs(axis.x) > 0.5f) lp.x = _visTrolley;
            else if (Mathf.Abs(axis.y) > 0.5f) lp.y = _visTrolley;
            else lp.z = _visTrolley;
            _trolley.localPosition = lp;
        }

        // Длина троса (крюк болтается физикой сам).
        if (_hookJoint != null)
        {
            SoftJointLimit limit = _hookJoint.linearLimit;
            limit.limit = _visHook;
            _hookJoint.linearLimit = limit;
        }

        // Визуал троса.
        if (_rope != null && _ropeTopPoint != null && _hookTransform != null)
        {
            _rope.positionCount = 2;
            _rope.SetPosition(0, _ropeTopPoint.position);
            _rope.SetPosition(1, _hookTransform.position);
        }
    }
}
