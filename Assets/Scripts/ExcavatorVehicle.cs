using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Экскаватор. Гусеничная физика (TrackedVehicle). Два режима по Tab:
///   DRIVE — WASD движет машину на гусеницах (A/D — поворот дифференциалом).
///   WORK  — движение заблокировано (CanDrive=false), управляем рукой:
///       A/D — башня, W/S — стрела, Q/E — рукоять, R/T — ковш.
///   F — выйти.
///
/// АРХИТЕКТУРА РУКИ (та же схема, что и у кузова самосвала): углы считаются
/// ЛОКАЛЬНО у водителя каждый Update по Time.deltaTime (не по сетевым тикам —
/// иначе короткое нажатие может дать скачок при "досрочных" тиках после лагов).
/// На сервер уходит АБСОЛЮТНОЕ значение каждый тик, идемпотентно. Водитель
/// видит свою руку без задержки, остальные — сглаженно по SyncVar.
/// </summary>
public class ExcavatorVehicle : TrackedVehicle
{
    [Header("Подвижные части (рука)")]
    [SerializeField] private Transform _tower;
    [Tooltip("Локальная ось вращения башни. У этой модели — Z (0,0,1). " +
             "Если башня крутится не туда — поменяй ось здесь.")]
    [SerializeField] private Vector3 _towerAxis = new Vector3(0f, 0f, 1f);
    [SerializeField] private Transform _boom;
    [SerializeField] private Transform _arm;
    [SerializeField] private Transform _bucket;

    [Header("Скорости частей (градусы/сек)")]
    [SerializeField] private float _towerSpeed = 40f;
    [SerializeField] private float _armSpeed = 30f;
    [Tooltip("Скорость визуального сглаживания для НЕ-водителя (градусы/сек)")]
    [SerializeField] private float _visualSmoothSpeed = 120f;

    [Header("Пределы углов")]
    [SerializeField] private Vector2 _boomLimit = new Vector2(-45, 45);
    [SerializeField] private Vector2 _armLimit = new Vector2(-60, 60);
    [SerializeField] private Vector2 _bucketLimit = new Vector2(-120, 120);

    // Авторитетные (серверные) значения — синхронизируются всем.
    private readonly SyncVar<bool> _workMode = new(false);
    private readonly SyncVar<float> _towerAngle = new(0f);
    private readonly SyncVar<float> _boomAngle = new(0f);
    private readonly SyncVar<float> _armAngle = new(0f);
    private readonly SyncVar<float> _bucketAngle = new(0f);

    // Локальные значения у водителя — считаются каждый кадр, без сетевой задержки.
    private float _localTower, _localBoom, _localArm, _localBucket;

    // Визуальные значения — у водителя = локальные напрямую, у остальных
    // плавно догоняют SyncVar.
    private float _visTower, _visBoom, _visArm, _visBucket;

    // Базовые (импортные) локальные повороты частей руки — захватываются один
    // раз, дальше крутим ОТНОСИТЕЛЬНО них.
    private Quaternion _towerBaseRot, _boomBaseRot, _armBaseRot, _bucketBaseRot;
    private bool _armBaseCaptured;

    private readonly SyncVar<NetworkObject> _npc=new(null);
    private float _npcMove,_npcTurn,_npcExpires;
    public bool HasNpcDriver => _npc.Value!=null;
    public Transform NpcSeat => _seatPoint;
    public Transform NpcExit => _exitPoint;
    public Transform TowerPart => _tower;
    public Transform BoomPart => _boom;
    public Transform ArmPart => _arm;
    public Transform BucketPart => _bucket;
    public Vector3 TowerAxis => _towerAxis;
    public float BodySpeed => _body!=null?_body.linearVelocity.magnitude:0;
    public Vector4 CurrentArmPose => new Vector4(_isLocalDriver?_localTower:_towerAngle.Value,
        _isLocalDriver?_localBoom:_boomAngle.Value,_isLocalDriver?_localArm:_armAngle.Value,_isLocalDriver?_localBucket:_bucketAngle.Value);
    public Vector4 ClampNpcPose(Vector4 p) => new Vector4(p.x,Mathf.Clamp(p.y,_boomLimit.x,_boomLimit.y),
        Mathf.Clamp(p.z,_armLimit.x,_armLimit.y),Mathf.Clamp(p.w,_bucketLimit.x,_bucketLimit.y));
    public override void OnStartServer()
    {
        base.OnStartServer();_npc.Value=null;_npcMove=_npcTurn=0;_npcExpires=0;
    }
    public bool ServerAcquireNpc(NetworkObject cat)
    {
        if(!IsServerInitialized || HasDriver || HasNpcDriver || cat==null || !cat.IsSpawned || _isStatic || _body==null) return false;
        NetworkObject.RemoveOwnership();_npc.Value=cat;_npcMove=_npcTurn=0;_npcExpires=0;_workMode.Value=true;return true;
    }
    public void ServerReleaseNpc(NetworkObject cat)
    {
        if(!IsServerInitialized || HasDriver || _npc.Value!=cat) return;
        _npc.Value=null;_npcMove=_npcTurn=0;_npcExpires=0;_workMode.Value=false;
    }
    public bool ServerNpcControl(NetworkObject cat,Vector4 pose,float move,float turn,bool drive,float delta)
    {
        if(!IsServerInitialized || cat==null || _npc.Value!=cat || HasDriver || !cat.IsSpawned) return false;
        pose=ClampNpcPose(pose);float dt=Mathf.Clamp(delta,0,.2f);
        _towerAngle.Value=Mathf.MoveTowardsAngle(_towerAngle.Value,pose.x,Mathf.Max(0,_towerSpeed)*dt);
        _boomAngle.Value=Mathf.MoveTowards(_boomAngle.Value,pose.y,Mathf.Max(0,_armSpeed)*dt);
        _armAngle.Value=Mathf.MoveTowards(_armAngle.Value,pose.z,Mathf.Max(0,_armSpeed)*dt);
        _bucketAngle.Value=Mathf.MoveTowards(_bucketAngle.Value,pose.w,Mathf.Max(0,_armSpeed)*dt);
        _npcMove=drive?Mathf.Clamp(move,-1,1):0;_npcTurn=drive?Mathf.Clamp(turn,-1,1):0;
        _npcExpires=Time.time+.6f;_workMode.Value=!drive;return true;
    }
    protected override bool TryAutomatedInput(out float move,out float turn)
    {
        bool valid=IsServerInitialized && _npc.Value!=null && _npc.Value.IsSpawned && !HasDriver && Time.time<_npcExpires;
        move=valid?_npcMove:0;turn=valid?_npcTurn:0;return valid;
    }
    protected override bool CanEnter(PlayerInteractor interactor) => !HasNpcDriver && base.CanEnter(interactor);
    protected override void ApplyDriverState(NetworkObject player,bool entering)
    {
        if(entering) { _localTower=_towerAngle.Value;_localBoom=_boomAngle.Value;_localArm=_armAngle.Value;_localBucket=_bucketAngle.Value; }
        base.ApplyDriverState(player,entering);
    }

    // В режиме работы рукой ездить нельзя.
    protected override bool CanDrive => !_workMode.Value;

    protected override void DriverUpdate()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) CmdToggleMode();

        if (!_workMode.Value) return; // рука активна только в режиме работы

        float dt = Time.deltaTime;
        float tower = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float boom = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        float arm = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        float bucket = (Input.GetKey(KeyCode.T) ? 1f : 0f) - (Input.GetKey(KeyCode.R) ? 1f : 0f);

        _localTower += tower * _towerSpeed * dt; // башня крутится свободно, без предела
        _localBoom = Mathf.Clamp(_localBoom + boom * _armSpeed * dt, _boomLimit.x, _boomLimit.y);
        _localArm = Mathf.Clamp(_localArm + arm * _armSpeed * dt, _armLimit.x, _armLimit.y);
        _localBucket = Mathf.Clamp(_localBucket + bucket * _armSpeed * dt, _bucketLimit.x, _bucketLimit.y);
    }

    protected override void DriverTick()
    {
        if (!_workMode.Value) return;
        // Абсолютные значения каждый тик — идемпотентно, безопасно при "догоняющих" тиках.
        CmdArmInput(_localTower, _localBoom, _localArm, _localBucket);
    }

    [ServerRpc]
    private void CmdToggleMode() => _workMode.Value = !_workMode.Value;

    [ServerRpc]
    private void CmdArmInput(float tower, float boom, float arm, float bucket)
    {
        _towerAngle.Value = tower;
        _boomAngle.Value = Mathf.Clamp(boom, _boomLimit.x, _boomLimit.y);
        _armAngle.Value = Mathf.Clamp(arm, _armLimit.x, _armLimit.y);
        _bucketAngle.Value = Mathf.Clamp(bucket, _bucketLimit.x, _bucketLimit.y);
    }

    protected override void ApplyVisuals()
    {
        if (!_armBaseCaptured)
        {
            if (_tower != null) _towerBaseRot = _tower.localRotation;
            if (_boom != null) _boomBaseRot = _boom.localRotation;
            if (_arm != null) _armBaseRot = _arm.localRotation;
            if (_bucket != null) _bucketBaseRot = _bucket.localRotation;

            _visTower = _localTower = _towerAngle.Value;
            _visBoom = _localBoom = _boomAngle.Value;
            _visArm = _localArm = _armAngle.Value;
            _visBucket = _localBucket = _bucketAngle.Value;
            _armBaseCaptured = true;
        }

        float dt = Time.deltaTime;
        if (_isLocalDriver)
        {
            _visTower = _localTower;
            _visBoom = _localBoom;
            _visArm = _localArm;
            _visBucket = _localBucket;
        }
        else
        {
            _visTower = Mathf.MoveTowards(_visTower, _towerAngle.Value, _visualSmoothSpeed * dt);
            _visBoom = Mathf.MoveTowards(_visBoom, _boomAngle.Value, _visualSmoothSpeed * dt);
            _visArm = Mathf.MoveTowards(_visArm, _armAngle.Value, _visualSmoothSpeed * dt);
            _visBucket = Mathf.MoveTowards(_visBucket, _bucketAngle.Value, _visualSmoothSpeed * dt);
        }

        if (_tower != null) _tower.localRotation = _towerBaseRot * Quaternion.AngleAxis(_visTower, _towerAxis);
        if (_boom != null) _boom.localRotation = _boomBaseRot * Quaternion.Euler(_visBoom, 0f, 0f);
        if (_arm != null) _arm.localRotation = _armBaseRot * Quaternion.Euler(_visArm, 0f, 0f);
        if (_bucket != null) _bucket.localRotation = _bucketBaseRot * Quaternion.Euler(_visBucket, 0f, 0f);
    }
}
