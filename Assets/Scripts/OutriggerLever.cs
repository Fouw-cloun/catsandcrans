using FishNet.Object;
using UnityEngine;

/// <summary>
/// Внешний рычаг выдвижения опор автокрана. ЧАСТЬ крана без своего NetworkObject
/// (обычный MonoBehaviour + IVehiclePart), поэтому может быть дочерним к крану и
/// не ломать спавн вложенным NetworkObject.
///
/// Игрок стоит снаружи, смотрит на рычаг, жмёт F: PlayerInteractor видит
/// IVehiclePart, поднимается к NetworkObject крана и зовёт ServerUse тут.
/// Рычаг визуально доворачивается по состоянию опор у всех клиентов.
///
/// На объект рычага нужен коллайдер (для наведения) и слой Interactable.
/// НИКАКОГО NetworkObject на рычаге быть не должно.
/// </summary>
public class OutriggerLever : MonoBehaviour, IVehiclePart
{
    [Header("Кран")]
    [SerializeField] private CraneState _craneState;
    [SerializeField] private MobileCraneChassis _chassis;
    [SerializeField] private CraneOperatorSeat _operatorSeat;

    [Header("Визуал рычага")]
    [Tooltip("Меш ручки, который доворачивается")]
    [SerializeField] private Transform _handle;
    [Tooltip("Абсолютные локальные углы (Euler) ручки, а не доворот от базы. " +
             "Включи, если знаешь точные локальные углы (у тебя -80 и 60 по X).")]
    [SerializeField] private bool _useAbsoluteAngles = true;
    [Tooltip("Абсолютный локальный Euler в ПОДНЯТОМ виде (режим езды). У тебя (-80,0,0)")]
    [SerializeField] private Vector3 _eulerUp = new Vector3(-80f, 0f, 0f);
    [Tooltip("Абсолютный локальный Euler в ОПУЩЕННОМ виде (режим крана). У тебя (60,0,0)")]
    [SerializeField] private Vector3 _eulerDown = new Vector3(60f, 0f, 0f);
    [Space(4)]
    [Tooltip("(Только если Use Absolute Angles ВЫКЛ) ось доворота от базы")]
    [SerializeField] private Vector3 _handleAxis = Vector3.right;
    [SerializeField] private float _angleUp = -80f;
    [SerializeField] private float _angleDown = 60f;
    [Space(4)]
    [SerializeField] private float _animSpeed = 7f;

    // Базовый (импортный) поворот ручки — запоминаем один раз, доворачиваем
    // ОТНОСИТЕЛЬНО него (иначе жёсткий Euler стирает ориентацию — рычаг "в углу").
    private Quaternion _handleBaseRot;
    private bool _baseCaptured;

    // Сетевую идентичность берём у крана (его NetworkObject).
    public NetworkObject OwningNetworkObject =>
        _craneState != null ? _craneState.NetworkObject : null;

    public int PartId => 0;

    // Вызывается на СЕРВЕРЕ из PlayerInteractor.
    public void ServerUse(PlayerInteractor interactor)
    {
        if (_craneState == null) return;

        // Не переключаем, если кто-то в любой из кабин.
        if ((_chassis != null && _chassis.HasDriver) ||
            (_operatorSeat != null && _operatorSeat.HasDriver))
            return;

        _craneState.ServerToggle();
    }

    private void Update()
    {
        if (_handle == null || _craneState == null) return;

        if (!_baseCaptured)
        {
            _handleBaseRot = _handle.localRotation;
            _baseCaptured = true;
        }

        Quaternion target;
        if (_useAbsoluteAngles)
        {
            // Абсолютный локальный поворот — ставим ровно (-80,0,0) / (60,0,0).
            target = Quaternion.Euler(_craneState.IsDeployed ? _eulerDown : _eulerUp);
        }
        else
        {
            // Доворот относительно базового положения на заданный угол по оси.
            float a = _craneState.IsDeployed ? _angleDown : _angleUp;
            target = _handleBaseRot * Quaternion.AngleAxis(a, _handleAxis.normalized);
        }

        _handle.localRotation = Quaternion.Slerp(_handle.localRotation, target, Time.deltaTime * _animSpeed);
    }
}
