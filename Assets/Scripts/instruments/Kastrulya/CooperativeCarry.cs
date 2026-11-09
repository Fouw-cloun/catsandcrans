using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// СОВМЕСТНЫЙ ПЕРЕНОС. Вешается на крупную посуду вместо PotItem.
///
/// Кастрюля НЕ берётся в руку: игроки цепляются к ней за точки по краям, и
/// пока не соберётся нужное количество, она стоит на месте. Собрались — поплыла
/// за ними; отцепился один — тут же поставили.
///
/// РАССТАНОВКА зависит от того, скольких требует посуда:
///   1 — спереди;
///   2 — по диагонали, спереди и сзади;
///   3 — треугольником;
///   4 — крест-накрест, по одному на каждую сторону.
/// Слоты считаются вокруг центра, поэтому подгонять их руками не нужно.
///
/// ПОЧЕМУ НЕ ЧЕРЕЗ PlayerHand: рука — слот на ОДИН предмет и ведёт его к своей
/// кисти. Для ноши на четверых это не годится в принципе — нужен объект,
/// который слушает сразу нескольких.
///
/// СЕТЬ: список носильщиков в SyncList, позицию считает сервер. Клиенты видят
/// результат через NetworkTransform.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CooperativeCarry : NetworkBehaviour
{
    [Header("Сколько нужно рук")]
    [Tooltip("Сколько игроков должны взяться, чтобы посуда поехала. " +
             "1 — мелкая кастрюля, 4 — чан на бригаду.")]
    [Range(1, 4)][SerializeField] private int _requiredCarriers = 2;

    [Tooltip("На каком расстоянии от центра стоят носильщики, метры")]
    [SerializeField] private float _slotRadius = 0.75f;

    [Header("Захват")]
    [Tooltip("Клавиша: взяться и отпустить")]
    [SerializeField] private KeyCode _grabKey = KeyCode.F;

    [Tooltip("С какого расстояния можно взяться, метры")]
    [SerializeField] private float _grabReach = 2.5f;

    [Tooltip("Отцепить, если игрок отошёл дальше этого, метры")]
    [SerializeField] private float _breakDistance = 2.2f;

    [Header("Перенос")]
    [Tooltip("На какой высоте несут, метры от земли")]
    [SerializeField] private float _carryHeight = 1.05f;

    [Tooltip("Насколько выносить ВПЕРЁД от носильщика, метры.\n\n" +
             "Обязательно для одиночного переноса: там среднее положение — это " +
             "ровно позиция игрока, и без выноса кастрюля оказывается внутри " +
             "него, то есть визуально на голове.")]
    [SerializeField] private float _carryForward = 0.8f;

    [Tooltip("Насколько выносить вперёд, когда несут ВДВОЁМ и больше. " +
             "Там они и так стоят вокруг, так что нужно меньше.")]
    [SerializeField] private float _carryForwardGroup = 0.15f;

    [Tooltip("Насколько шустро посуда догоняет носильщиков")]
    [SerializeField] private float _followSharpness = 9f;

    [Tooltip("Скорость игроков, пока несут")]
    [Range(0.1f, 1f)][SerializeField] private float _carrySpeed = 0.55f;

    [Header("Постановка")]
    [Tooltip("Сколько секунд кастрюля плавно опускается, когда её ставят")]
    [SerializeField] private float _placeDuration = 0.35f;

    [Tooltip("Перед кем ставить, метры от носильщиков")]
    [SerializeField] private float _placeForward = 0.9f;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud = true;

    /// <summary>Кто держит. Порядок в списке = номер слота.</summary>
    private readonly SyncList<NetworkObject> _carriers = new SyncList<NetworkObject>();

    private Rigidbody _rb;
    private bool _wasKinematic;
    private bool _parked;

    private readonly List<FirstPersonController> _slowed = new List<FirstPersonController>();

    // Плавная постановка: сервер ведёт кастрюлю в точку и только потом
    // отдаёт её физике.
    private bool _placing;
    private Vector3 _placeFrom, _placeTo;
    private Quaternion _placeRotFrom, _placeRotTo;
    private float _placeTime;

    public int Required => _requiredCarriers;
    public int Count => _carriers.Count;
    public bool IsCarried => _carriers.Count >= _requiredCarriers;
    public bool NeedsMore => _carriers.Count > 0 && _carriers.Count < _requiredCarriers;

    private void Awake() => _rb = GetComponent<Rigidbody>();

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _carriers.OnChange += OnCarriersChanged;
        ApplyPhysics();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _carriers.OnChange -= OnCarriersChanged;
    }

    private void OnCarriersChanged(SyncListOperation op, int index,
                                   NetworkObject prev, NetworkObject next, bool asServer)
    {
        // У ХОСТА событие приходит дважды: asServer = true, потом false.
        if (asServer && base.IsClientInitialized) return;
        ApplyPhysics();
        ApplySpeed();
    }

    /// <summary>Пока несут — физика выключена у ВСЕХ, иначе у одних посуда
    /// плывёт, а у других падает.</summary>
    private void ApplyPhysics()
    {
        if (_rb == null) return;

        bool carried = IsCarried;
        if (carried && !_parked)
        {
            _parked = true;
            _wasKinematic = _rb.isKinematic;
            _rb.isKinematic = true;
        }
        else if (!carried && _parked)
        {
            _parked = false;
            _rb.isKinematic = _wasKinematic;
            _rb.WakeUp();
        }
    }

    // ───────────────────────── ГЕОМЕТРИЯ СЛОТОВ ─────────────────────────

    /// <summary>
    /// Куда встать носильщику с номером slot. Углы раскладываются по кругу так,
    /// чтобы при четверых вышел крест, а при двоих — диагональ.
    /// </summary>
    private Vector3 SlotOffset(int slot, int total)
    {
        if (total <= 1) return Vector3.forward * _slotRadius;

        // Двое встают друг напротив друга, четверо — крестом. И то и другое
        // получается равномерным делением круга.
        float angle = 360f / total * slot;
        return Quaternion.Euler(0f, angle, 0f) * Vector3.forward * _slotRadius;
    }

    /// <summary>Где должен стоять этот носильщик — для подсказки игроку.</summary>
    public Vector3 SlotWorldPos(int slot)
        => transform.position + SlotOffset(slot, _requiredCarriers);

    // ───────────────────────── СЕРВЕР: ЗАХВАТ ─────────────────────────

    [Server]
    public bool ServerGrab(NetworkObject player)
    {
        if (player == null) return false;
        CraneLoad load = GetComponent<CraneLoad>();
        if (load != null && load.IsAttached) return false;
        CraneRider rider = player.GetComponent<CraneRider>();
        if (rider != null && rider.IsHanging) return false;
        if (_carriers.Contains(player)) return false;
        if (_carriers.Count >= _requiredCarriers) return false;

        _carriers.Add(player);
        ApplyPhysics();
        ApplySpeed();

        Debug.Log($"[Перенос] взялся {player.name}: {_carriers.Count} из {_requiredCarriers}");
        return true;
    }

    [Server]
    public void ServerRelease(NetworkObject player)
    {
        if (player == null || !_carriers.Contains(player)) return;

        _carriers.Remove(player);
        ApplyPhysics();
        ApplySpeed();

        Debug.Log($"[Перенос] отпустил {player.name}: {_carriers.Count} из {_requiredCarriers}");
    }

    public bool Holds(NetworkObject player) => _carriers.Contains(player);

    /// <summary>
    /// Отцепить всех разом, не опуская ношу на пол. Нужно, когда её ставят
    /// на конфорку: позицию задаст сама конфорка, а плавное опускание тут
    /// только помешало бы — оно тянуло бы кастрюлю мимо тарелки.
    /// </summary>
    [Server]
    public void ServerReleaseAll()
    {
        if (_carriers.Count == 0) return;

        _carriers.Clear();
        ApplySpeed();
        ApplyPhysics();
    }

    // ───────────────────────── СЕРВЕР: ДВИЖЕНИЕ ─────────────────────────

    private void Update()
    {
        if (!base.IsServerInitialized) return;

        if (_placing) { StepPlacing(); return; }

        DropRunaways();
        if (!IsCarried) return;

        Vector3 center = Vector3.zero;
        int n = 0;

        for (int i = 0; i < _carriers.Count; i++)
        {
            NetworkObject c = _carriers[i];
            if (c == null) continue;
            center += c.transform.position;
            n++;
        }
        if (n == 0) return;

        center /= n;

        // Средний взгляд носильщиков — вдоль него и выносим ношу вперёд.
        Vector3 look = Vector3.zero;
        for (int i = 0; i < _carriers.Count; i++)
            if (_carriers[i] != null) look += _carriers[i].transform.forward;
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f) look.Normalize();
        else look = transform.forward;

        float fwdDist = n <= 1 ? _carryForward : _carryForwardGroup;
        center += look * fwdDist;

        center.y = GroundY(center) + _carryHeight;

        // Плавно, а не рывком: иначе посуда дёргалась бы на каждом шаге
        // носильщиков, а их четверо и шагают они вразнобой.
        float k = 1f - Mathf.Exp(-_followSharpness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, center, k);

        // Разворот по среднему взгляду — так носильщики естественно доворачивают
        // ношу вместе. Крен нулевой: кастрюлю несут ровно, иначе выльется.
        transform.rotation = Quaternion.Slerp(transform.rotation,
                                              Quaternion.LookRotation(look), k);
    }

    /// <summary>Отошёл слишком далеко — отцепляем. Иначе носильщик, застрявший
    /// за углом, тянул бы посуду сквозь стену.</summary>
    private void DropRunaways()
    {
        for (int i = _carriers.Count - 1; i >= 0; i--)
        {
            NetworkObject c = _carriers[i];
            if (c == null || !c.IsSpawned)
            {
                _carriers.RemoveAt(i);
                continue;
            }

            if (Vector3.Distance(c.transform.position, transform.position) > _breakDistance)
            {
                Debug.Log($"[Перенос] {c.name} отошёл — отцепился");
                _carriers.RemoveAt(i);
            }
        }
    }

    // ───────────────────────── ПОСТАВИТЬ РОВНО ─────────────────────────

    /// <summary>
    /// Аккуратно поставить кастрюлю перед носильщиками: опустить на землю,
    /// выровнять по горизонту и отпустить.
    ///
    /// Без этого поставить ровно почти невозможно: при простом отпускании
    /// кастрюля падает с высоты переноски, подпрыгивает и укатывается —
    /// а с горячим содержимым это ещё и обидно.
    /// </summary>
    [Server]
    public void ServerPlaceDown()
    {
        if (_placing || _carriers.Count == 0) return;

        Vector3 center = Vector3.zero;
        Vector3 look = Vector3.zero;
        int n = 0;

        for (int i = 0; i < _carriers.Count; i++)
        {
            NetworkObject c = _carriers[i];
            if (c == null) continue;
            center += c.transform.position;
            look += c.transform.forward;
            n++;
        }
        if (n == 0) return;

        center /= n;
        look.y = 0f;
        look = look.sqrMagnitude > 0.01f ? look.normalized : transform.forward;

        Vector3 spot = center + look * _placeForward;
        spot.y = GroundY(spot);

        // Все отцепляются сразу: дальше кастрюля идёт вниз сама, и держать
        // её уже незачем.
        _carriers.Clear();
        ApplySpeed();

        _placing = true;
        _placeTime = 0f;
        _placeFrom = transform.position;
        _placeTo = spot;
        _placeRotFrom = transform.rotation;

        // Ставим строго вертикально: крен и наклон обнуляем, иначе содержимое
        // как бы выльется, да и выглядит криво.
        _placeRotTo = Quaternion.LookRotation(look, Vector3.up);

        // Физику держим выключенной, пока опускаем.
        if (_rb != null) { _parked = true; _rb.isKinematic = true; }
    }

    private void StepPlacing()
    {
        _placeTime += Time.deltaTime;
        float t = Mathf.Clamp01(_placeTime / Mathf.Max(0.05f, _placeDuration));

        // Замедление к концу — кастрюля мягко касается земли, а не втыкается.
        float e = 1f - (1f - t) * (1f - t);

        transform.position = Vector3.Lerp(_placeFrom, _placeTo, e);
        transform.rotation = Quaternion.Slerp(_placeRotFrom, _placeRotTo, e);

        if (t < 1f) return;

        _placing = false;

        // Отдаём физике уже стоящую ровно кастрюлю: падать ей неоткуда,
        // подпрыгивать и укатываться нечему.
        if (_rb != null)
        {
            _parked = false;
            _rb.isKinematic = _wasKinematic;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.WakeUp();
        }

        Debug.Log("[Перенос] кастрюля поставлена");
    }

    private float GroundY(Vector3 at)
    {
        if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out RaycastHit h, 8f,
                            ~0, QueryTriggerInteraction.Ignore))
            return h.point.y;
        return transform.position.y - _carryHeight;
    }

    /// <summary>Носильщики идут медленнее. Пишем скорость напрямую: PlayerHand
    /// ставит её только при взятии предмета в руку, а тут руки свободны.</summary>
    private void ApplySpeed()
    {
        foreach (FirstPersonController f in _slowed)
            if (f != null) f.SpeedMultiplier = 1f;
        _slowed.Clear();

        if (!IsCarried) return;

        for (int i = 0; i < _carriers.Count; i++)
        {
            if (_carriers[i] == null) continue;
            FirstPersonController f = _carriers[i].GetComponent<FirstPersonController>();
            if (f == null) continue;

            f.SpeedMultiplier = _carrySpeed;
            _slowed.Add(f);
        }
    }

    // ───────────────────────── ПОДСКАЗКА ─────────────────────────

    private void OnGUI()
    {
        if (!_debugHud || !NeedsMore) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 0.4f);
        if (sp.z <= 0f || Vector3.Distance(cam.transform.position, transform.position) > 8f)
            return;

        GUIStyle st = new GUIStyle(GUI.skin.box) { fontSize = 15 };
        st.normal.textColor = new Color(1f, 0.85f, 0.3f);

        string txt = $"нужно ещё {_requiredCarriers - _carriers.Count} " +
                     $"(держат {_carriers.Count} из {_requiredCarriers})";
        Vector2 size = st.CalcSize(new GUIContent(txt));

        GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y, size.x + 14f, 28f),
                txt, st);
    }
}
