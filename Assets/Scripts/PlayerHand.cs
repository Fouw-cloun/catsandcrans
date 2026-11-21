using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Рука игрока — слот на один предмет. Живёт на префабе игрока.
/// Хранит держимый предмет в SyncVar, чтобы все клиенты знали, что у этого
/// игрока в руке. На клиенте-владельце каждый кадр ведёт предмет к точке кисти.
///
/// ─────────────────────────────────────────────────────────────────────────
/// ИСПРАВЛЕНО:
///
/// HasItem и HeldItem могли расходиться. HasItem смотрел прямо в SyncVar, а
/// HeldItem заполнялся только в колбэке OnChange. Между установкой значения и
/// приходом колбэка (а на клиенте, который подключился позже, — до первой
/// синхронизации) получалось HasItem == true при HeldItem == null.
///
/// Код по всему проекту написан в расчёте на то, что этого не бывает:
///     if (_hand.HasItem && !_hand.HeldItem.AllowInteractWhileHeld)
/// Такая строка при расхождении падает с NullReferenceException. Сейчас оба
/// свойства выводятся из одного места, и разойтись физически не могут.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class PlayerHand : NetworkBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Точка кисти — куда крепится предмет. Сделай дочерней к CameraRoot.")]
    [SerializeField] private Transform _handAnchor;
    [Tooltip("Контроллер игрока (для штрафа скорости при переноске тяжёлого)")]
    [SerializeField] private FirstPersonController _controller;
    [Tooltip("Коллайдер тела игрока (обычно CharacterController) — нужен, чтобы " +
             "брошенный предмет временно не сталкивался со своим же хозяином")]
    [SerializeField] private Collider _ownerCollider;

    [Header("v19.4: предмет в руке")]
    [Tooltip("v19.5: ВЫКЛЮЧЕНО по умолчанию. Предмет поверх мира отдельной камерой (как оружие в шутере) — " +
             "давал полосу у горизонта (эффекты рендера срабатывали и для второй камеры). Не включай, пока не починим")]
    [SerializeField] private bool _viewModelOverlay = false;
    [Tooltip("Миксер/венчик сам встаёт в тару под прицелом (рабочая поза)")]
    [SerializeField] private bool _workPose = true;

    // Текущий предмет в руке (синхронизируется всем).
    private readonly SyncVar<NetworkObject> _heldItem = new(null);

    // Кэш компонента, чтобы не звать GetComponent каждый кадр.
    private NetworkObject _cachedFor;
    private InteractableItem _cachedItem;

    public Transform HandAnchor => _handAnchor;

    /// <summary>Предмет в руке или null. Выводится напрямую из SyncVar, а не
    /// из колбэка: колбэк может ещё не успеть отработать, а обратиться к
    /// свойству код успевает в тот же кадр.</summary>
    public InteractableItem HeldItem
    {
        get
        {
            NetworkObject no = _heldItem.Value;
            if (no == null)
            {
                _cachedFor = null;
                _cachedItem = null;
                return null;
            }

            if (!ReferenceEquals(no, _cachedFor))
            {
                _cachedFor = no;
                _cachedItem = no.GetComponent<InteractableItem>();
            }
            return _cachedItem;
        }
    }

    /// <summary>Есть ли что-то в руке. Считается от HeldItem, поэтому
    /// «true при null» получиться не может.</summary>
    public bool HasItem => HeldItem != null;

    /// <summary>Коллайдер тела игрока. Если не назначен в инспекторе — пробуем
    /// взять CharacterController автоматически (он сам является Collider).</summary>
    public Collider OwnerCollider
    {
        get
        {
            if (_ownerCollider == null) _ownerCollider = GetComponent<CharacterController>();
            return _ownerCollider;
        }
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _heldItem.OnChange += OnHeldChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _heldItem.OnChange -= OnHeldChanged;
    }

    /// <summary>Колбэк остался только ради побочных эффектов — штрафа скорости.
    /// Само значение HeldItem он больше не выставляет.</summary>
    private void OnHeldChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        // Сбрасываем кэш: объект сменился.
        _cachedFor = null;
        _cachedItem = null;

        // Штраф скорости при переноске (например, кастрюли) — только у владельца.
        if (!base.IsOwner || _controller == null) return;

        InteractableItem item = HeldItem;
        _controller.SpeedMultiplier = item != null ? item.CarrySpeedMultiplier : 1f;
    }

    // v19.3: поза — в LateUpdate. Камера поворачивается в Update контроллера; рука в Update иногда
    // успевала раньше и брала поворот прошлого кадра — предмет «скакал» относительно экрана.
    private void LateUpdate()
    {
        // Предмет ведёт к руке только клиент-владелец (он же владелец предмета).
        if (!base.IsOwner) return;

        InteractableItem item = HeldItem;
        if (_blockCam == null) { Camera c = GetComponentInChildren<Camera>(true); if (c != null) { _blockCam = c.transform; _blockCamC = c; } }
        if (_view == null && _blockCamC != null && _viewModelOverlay) _view = new CatCooks.Food.HeldViewModel(_blockCamC);
        if (item == null) { _blockFor = null; if (_view != null) _view.Tick(null, false); return; }

        item.UpdateHeldPose(_handAnchor);

        // v19.4: РАБОЧАЯ ПОЗА — миксер/венчик сам встаёт в тару под прицелом (вертикально, внутри стенок),
        // не бьётся о стенки и не тянется к камере. Поза привязана к таре.
        bool work = false;
        ISpinningTool spin = item as ISpinningTool;
        MixerItem mixer = item as MixerItem;
        if (_workPose && spin != null && item.UseHeldBlocking && spin.WorkPoint != null && _blockCam != null && (mixer == null || !mixer.PoseLocked))
        {
            if (_work == null) _work = new CatCooks.Food.ToolWorkPose();
            work = _work.Apply(_blockCam, item.transform, spin.WorkPoint, spin.IsActive);
        }

        // v23.5: черпалки (мастерок, лопата, черпак) — в руке, без «магнита»; зачерпнул — боковой черпок,
        // высыпал — бросок, комок летит в тару (только анимация, логика прежняя)
        if (!work && item.UseHeldBlocking && _blockCam != null)
        {
            if (_scoopFor != item)
            {
                _scoopFor = item;
                _scoopPoint = CatCooks.Food.ScoopWorkPose.FindPoint(item);
                if (_scoopAnim == null) _scoopAnim = new CatCooks.Food.ScoopAnimator();
                _scoopAnim.Reset();
            }
            if (_scoopPoint != null)
            {
                int load = -1;
                CatCooks.Food.Tools.ScoopItem si = item as CatCooks.Food.Tools.ScoopItem;
                if (si != null) load = si.LoadCount;
                else
                {
                    CatCooks.Food.Tools.LadleItem li = item as CatCooks.Food.Tools.LadleItem;
                    if (li != null) load = li.Count;
                }
                if (CatCooks.Food.ScoopWorkPose.Enabled)
                {
                    if (_scoop == null) _scoop = new CatCooks.Food.ScoopWorkPose();
                    work = _scoop.Apply(_blockCam, item.transform, _scoopPoint);
                }
                _scoopAnim.Apply(_blockCam, item.transform, _scoopPoint, load);
            }
        }

        // v19.3: как оружие в шутере — рядом нет тары/продуктов → предмет рисуется поверх мира,
        // стоит ровно в руке (не упирается и не отъезжает к лицу). Рядом рабочая цель → в мире, как раньше.
        bool overlay = false;
        if (_viewModelOverlay && !work && _view != null && item.UseHeldBlocking && _view.Available)
            overlay = !_view.NearWork(item.transform);
        if (_view != null) _view.Tick(item, overlay);

        // v17.3: любой предмет в руке не проходит сквозь стены, стол и другие предметы —
        // упирается и отъезжает к камере. Тару и продукты не считаем (черпать, мешать, долбить можно).
        if (!overlay && !work && HeldBlocking && item.UseHeldBlocking)
        {
            if (_blockFor != item) { _blockFor = item; _blocker = new CatCooks.Food.HeldBlocker(item.transform) { IgnoreWorkTargets = true }; }
            if (_blockCam != null)
            {
                // v23.4: упёрся — чуть отъезжает и опускается вниз из кадра кончиком вниз (как оружие у стены), а не летит в экран
                Transform t = item.transform;
                Quaternion r = t.rotation;
                Vector3 p = _blocker.SolvePose(t.position, ref r, _blockCam.position, _blockCam.up, _blockCam.right, true);
                t.SetPositionAndRotation(p, r);
            }
        }
    }

    private void OnDestroy()
    {
        if (_view != null) _view.Dispose();
    }

    /// <summary>v17.3: выключатель общей «несквозности» предметов в руке.</summary>
    public static bool HeldBlocking = true;
    private InteractableItem _blockFor;
    private CatCooks.Food.HeldBlocker _blocker;
    private Transform _blockCam;
    private Camera _blockCamC;
    private CatCooks.Food.HeldViewModel _view;
    private CatCooks.Food.ToolWorkPose _work;
    private CatCooks.Food.ScoopWorkPose _scoop;
    private CatCooks.Food.ScoopAnimator _scoopAnim;
    private InteractableItem _scoopFor;
    private Transform _scoopPoint;

    // ───────── СЕРВЕРНЫЕ МЕТОДЫ ─────────

    /// <summary>Попытаться взять предмет. Возвращает true при успехе.</summary>
    public bool ServerTryHold(InteractableItem item)
    {
        if (item == null) return false;
        if (item.PickupLocked) return false;   // v25.6: застрявшая пила — не взять, только вытянуть
        CraneLoad craneLoad = item.GetComponentInParent<CraneLoad>();
        if (craneLoad != null && craneLoad.IsAttached) return false;
        CraneRider rider = GetComponent<CraneRider>();
        if (rider != null && rider.IsHanging) return false;
        if (_heldItem.Value != null) return false; // рука занята

        _heldItem.Value = item.NetworkObject;
        _cachedFor = item.NetworkObject;
        _cachedItem = item;

        item.ServerOnPickup(this);
        return true;
    }

    /// <summary>Бросить текущий предмет. Force=0 (по умолчанию, G) — предмет
    /// просто падает под гравитацией без полёта. Force>0 — толчок в направлении
    /// tossDirection (заряженный ЛКМ-бросок ингредиентов).</summary>
    public void ServerDrop(Vector3 tossDirection, float force = 0f)
    {
        if (_heldItem.Value == null) return;

        InteractableItem item = HeldItem;

        _heldItem.Value = null;
        _cachedFor = null;
        _cachedItem = null;

        if (item != null) item.ServerOnDrop(this, tossDirection, force);
    }
}
