using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Кабина оператора автокрана. Сесть можно только когда опоры ОПУЩЕНЫ.
///   A/D — поворот башни вокруг своей оси,
///   W/S — наклон всей стрелы вверх/вниз,
///   Q/E — выдвижение секций стрелы (3 сегмента, вложены, едут одновременно),
///   R/G — крюк вверх/вниз (длина троса; крюк болтается физикой),
///   F — выйти.
///
/// Структура стрелы: _boomBase наклоняется целиком (W/S). Внутри неё три
/// выдвижных сегмента (_sections[]) — все едут по локальной Y одновременно и
/// равномерно (Q/E). Поршень наклона — отдельный визуальный PistonLookAt.
/// Крюк — внешнее физическое тело на ConfigurableJoint (как у башенного крана),
/// трос рисуется LineRenderer.
///
/// Архитектура значений — как у остальной техники: локальный расчёт у водителя
/// по Time.deltaTime, идемпотентная отправка абсолютных значений на сервер,
/// сглаживание у не-водителя.
/// </summary>
public class CraneOperatorSeat : NetworkVehicle
{
    [Header("Кран")]
    [SerializeField] private CraneState _craneState;

    [Header("Башня и стрела")]
    [Tooltip("Башня — вращается вокруг своей оси")]
    [SerializeField] private Transform _towerBase;
    [Tooltip("Локальная ось вращения башни")]
    [SerializeField] private Vector3 _towerAxis = Vector3.up;
    [Tooltip("Стрела целиком — наклоняется вверх/вниз")]
    [SerializeField] private Transform _boomBase;
    [Tooltip("Локальная ось наклона стрелы")]
    [SerializeField] private Vector3 _boomAxis = Vector3.right;

    [Header("Выдвижные секции стрелы (телескоп)")]
    [Tooltip("ТОЛЬКО выдвижные сегменты, БЕЗ неподвижной основы! Порядок = " +
             "порядок выдвижения (сначала [0], потом [1]...). Для крана с 3 " +
             "секциями (основа + 2 выдвижные) сюда кладём 2 выдвижные.")]
    [SerializeField] private Transform[] _sections;
    [Tooltip("Для каждой выдвижной секции: локальная Y задвинуто (X) и выдвинуто (Y)")]
    [SerializeField] private Vector2[] _sectionYRange;

    [Header("Крюк (физический, на тросе)")]
    [SerializeField] private ConfigurableJoint _hookJoint;
    [SerializeField] private LineRenderer _rope;
    [SerializeField] private Transform _ropeTopPoint;
    [SerializeField] private Transform _hookTransform;

    [Header("Скорости")]
    [SerializeField] private float _rotateSpeed = 30f;
    [SerializeField] private float _pitchSpeed = 20f;
    [SerializeField] private float _extendSpeed = 0.4f;  // 0..1 в секунду
    [SerializeField] private float _hookSpeed = 4f;
    [SerializeField] private float _visualSmoothSpeed = 60f;

    [Header("Пределы")]
    [SerializeField] private Vector2 _pitchLimit = new Vector2(-60f, 15f);
    [SerializeField] private Vector2 _hookLimit = new Vector2(1f, 30f);

    // Авторитетные (серверные) значения.
    private readonly SyncVar<float> _rotate = new(0f);
    private readonly SyncVar<float> _pitch = new(0f);
    private readonly SyncVar<float> _extend = new(0f); // 0..1
    private readonly SyncVar<float> _hookLen = new(2f);

    // Локальные (у водителя, без задержки).
    private float _localRotate, _localPitch, _localExtend, _localHook = 2f;
    // Визуальные (сглаженные у не-водителя).
    private float _visRotate, _visPitch, _visExtend, _visHook;

    private Quaternion _towerBaseRot, _boomBaseRot;
    private bool _baseCaptured;

    protected override bool CanEnter(PlayerInteractor interactor)
    {
        // В кабину крана садимся ВСЕГДА (просто посидеть можно всегда).
        return true;
    }

    // Управлять краном можно только с ОПУЩЕННЫМИ опорами (иначе кран без опор
    // раскачивал бы корпус). Без опор сидим, но рычаги не работают.
    private bool CanOperate => _craneState != null && _craneState.IsDeployed;

    protected override void DriverUpdate()
    {
        // Опоры подняты — можно сидеть, но краном не управляем.
        if (!CanOperate) return;

        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        float rot = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float pit = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        float ext = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        float hk = (Input.GetKey(KeyCode.R) ? 1f : 0f) - (Input.GetKey(KeyCode.G) ? 1f : 0f);

        _localRotate += rot * _rotateSpeed * dt;
        _localPitch = Mathf.Clamp(_localPitch + pit * _pitchSpeed * dt, _pitchLimit.x, _pitchLimit.y);
        _localExtend = Mathf.Clamp01(_localExtend + ext * _extendSpeed * dt);
        _localHook = Mathf.Clamp(_localHook + hk * _hookSpeed * dt, _hookLimit.x, _hookLimit.y);
    }

    protected override void DriverTick()
    {
        CmdCraneInput(_localRotate, _localPitch, _localExtend, _localHook);
    }

    [ServerRpc]
    private void CmdCraneInput(float rotate, float pitch, float extend, float hook)
    {
        _rotate.Value = rotate;
        _pitch.Value = Mathf.Clamp(pitch, _pitchLimit.x, _pitchLimit.y);
        _extend.Value = Mathf.Clamp01(extend);
        _hookLen.Value = Mathf.Clamp(hook, _hookLimit.x, _hookLimit.y);
    }

    protected override void ApplyVisuals()
    {
        if (!_baseCaptured)
        {
            if (_towerBase != null) _towerBaseRot = _towerBase.localRotation;
            if (_boomBase != null) _boomBaseRot = _boomBase.localRotation;
            _visRotate = _localRotate = _rotate.Value;
            _visPitch = _localPitch = _pitch.Value;
            _visExtend = _localExtend = _extend.Value;
            _visHook = _localHook = _hookLen.Value;
            _baseCaptured = true;
        }

        float dt = Time.deltaTime;
        if (_isLocalDriver)
        {
            _visRotate = _localRotate;
            _visPitch = _localPitch;
            _visExtend = _localExtend;
            _visHook = _localHook;
        }
        else
        {
            _visRotate = Mathf.MoveTowards(_visRotate, _rotate.Value, _visualSmoothSpeed * dt);
            _visPitch = Mathf.MoveTowards(_visPitch, _pitch.Value, _visualSmoothSpeed * dt);
            _visExtend = Mathf.MoveTowards(_visExtend, _extend.Value, dt / Mathf.Max(0.01f, _extendSpeed));
            _visHook = Mathf.MoveTowards(_visHook, _hookLen.Value, _hookSpeed * 4f * dt);
        }

        // Поворот башни.
        if (_towerBase != null)
            _towerBase.localRotation = _towerBaseRot * Quaternion.AngleAxis(_visRotate, _towerAxis);

        // Наклон всей стрелы.
        if (_boomBase != null)
            _boomBase.localRotation = _boomBaseRot * Quaternion.AngleAxis(_visPitch, _boomAxis);

        // Секции: выдвигаются ПОСЛЕДОВАТЕЛЬНО по локальной Y. _visExtend 0..1
        // делится на N равных фаз: сначала полностью выезжает секция 0, потом 1
        // и т.д. (эффект телескопа-трубочки: средняя, затем крайняя).
        if (_sections != null && _sectionYRange != null)
        {
            int n = Mathf.Min(_sections.Length, _sectionYRange.Length);
            for (int i = 0; i < n; i++)
            {
                if (_sections[i] == null) continue;

                // Фаза i-й секции: её "локальный" прогресс 0..1 внутри своего
                // отрезка общего хода [i/n .. (i+1)/n].
                float phase = Mathf.Clamp01(_visExtend * n - i);
                float y = Mathf.Lerp(_sectionYRange[i].x, _sectionYRange[i].y, phase);
                Vector3 lp = _sections[i].localPosition;
                lp.y = y;
                _sections[i].localPosition = lp;
            }
        }

        // Длина троса крюка.
        if (_hookJoint != null)
        {
            SoftJointLimit limit = _hookJoint.linearLimit;
            limit.limit = _visHook;
            _hookJoint.linearLimit = limit;
        }

        // Верёвка LineRenderer.
        if (_rope != null && _ropeTopPoint != null && _hookTransform != null)
        {
            _rope.positionCount = 2;
            _rope.SetPosition(0, _ropeTopPoint.position);
            _rope.SetPosition(1, _hookTransform.position);
        }
    }
}
