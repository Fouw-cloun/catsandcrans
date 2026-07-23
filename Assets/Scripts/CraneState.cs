using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Общее состояние автокрана: выдвинуты ли опоры (аутригеры). Висит на корне
/// крана. На него ссылаются шасси (нельзя ехать с опорами), кабина крана
/// (нельзя крановать без опор) и рычаг (переключает опоры).
///
/// Опоры двигаются у всех клиентов по SyncVar вдоль настраиваемой локальной оси
/// (у этой модели — Z). Выдвинуты = режим крана, задвинуты = режим езды.
/// </summary>
public class CraneState : NetworkBehaviour
{
    [Header("Опоры")]
    [SerializeField] private Transform[] _supportPistons;
    [Tooltip("Локальная ось выдвижения опор (у этой модели — Z)")]
    [SerializeField] private Vector3 _deployAxis = new Vector3(0f, 0f, 1f);
    [Tooltip("Позиция по оси в ПОДНЯТОМ виде (режим езды). У тебя Z = -0.006")]
    [SerializeField] private float _retractedValue = -0.006f;
    [Tooltip("Позиция по оси в ВЫДВИНУТОМ виде (режим крана). У тебя Z = -0.02")]
    [SerializeField] private float _deployedValue = -0.02f;
    [SerializeField] private float _deploySpeed = 2f;

    private readonly SyncVar<bool> _deployed = new(false);
    private Vector3[] _startLocalPos;

    public bool IsDeployed => _deployed.Value;

    private void Awake()
    {
        if (_supportPistons != null)
        {
            _startLocalPos = new Vector3[_supportPistons.Length];
            for (int i = 0; i < _supportPistons.Length; i++)
                if (_supportPistons[i] != null)
                    _startLocalPos[i] = _supportPistons[i].localPosition;
        }
    }

    private void Update()
    {
        if (_supportPistons == null || _startLocalPos == null) return;

        Vector3 axis = _deployAxis.normalized;
        float targetValue = _deployed.Value ? _deployedValue : _retractedValue;

        for (int i = 0; i < _supportPistons.Length; i++)
        {
            if (_supportPistons[i] == null) continue;

            // Берём стартовую локальную позицию и заменяем компоненту вдоль оси
            // на целевое значение (-0.006 поднято / -0.02 выдвинуто по Z).
            Vector3 target = _startLocalPos[i];
            if (Mathf.Abs(axis.x) > 0.5f) target.x = targetValue;
            else if (Mathf.Abs(axis.y) > 0.5f) target.y = targetValue;
            else target.z = targetValue;

            _supportPistons[i].localPosition = Vector3.MoveTowards(
                _supportPistons[i].localPosition, target, _deploySpeed * Time.deltaTime);
        }
    }

    /// <summary>Переключить опоры (вызывается на сервере из рычага).</summary>
    [Server]
    public void ServerToggle()
    {
        _deployed.Value = !_deployed.Value;
    }
}
