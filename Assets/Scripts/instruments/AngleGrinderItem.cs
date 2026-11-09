using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Болгарка (угловая шлифмашина). Инструмент в руке.
///
///   ЛКМ (удержание) — крутится диск. Пока держишь — диск раскручивается и
///                     вращается. Отпустил — тормозит. Разгон/торможение плавные.
///   R              — перезарядка: поставить новый диск (когда старый слетел).
///   G              — бросить (как любой предмет, база).
///
/// МЕХАНИКА СЛЁТА ДИСКА:
///   В случайный момент кручения диск слетает — прячется с болгарки и (если
///   назначен префаб) в мир спавнится улетающий диск, летящий в случайную
///   сторону. Пока диска нет — болгарка НЕ крутится, нужно нажать R.
///
/// СЕТЕВАЯ МОДЕЛЬ (по паттерну проекта):
///   - _spinning / _hasDisc — SyncVar, состояние знают все клиенты.
///   - Само вращение диска — ЛОКАЛЬНЫЙ визуал у каждого клиента, считается по
///     Time.deltaTime (с клампом сверху), НЕ синхронизируется по тикам —
///     синхронизируем только булев флаг "крутится/нет".
///   - Слёт диска и спавн улетающего диска решает СЕРВЕР (единое событие для
///     всех). Владелец только шлёт идемпотентные команды "крутить/не крутить".
///
/// БУДУЩЕЕ (кабель/розетка): здесь же добавится проверка "запитана ли болгарка"
/// перед разрешением кручения — в CmdSetSpinning/CmdToggleSpin добавить
/// условие вроде `if (!IsPowered) return;`.
///
/// Требует на объекте: NetworkObject, NetworkTransform (Client Authoritative),
/// Collider, Rigidbody (как у обычного предмета — чтобы падал/брался в руку).
/// </summary>
public class AngleGrinderItem : InteractableItem, ISpinningTool
{
    [Header("Болгарка — диск (визуал)")]
    [Tooltip("Дочерний меш вращающегося диска. Крутится вокруг своей оси.")]
    [SerializeField] private Transform _disc;
    [Tooltip("Ось вращения диска в ЕГО локальных координатах. Подгони под модель " +
             "(часто Z=(0,0,1) или X=(1,0,0)), не трогая код.")]
    [SerializeField] private Vector3 _discSpinAxis = Vector3.forward;
    [Tooltip("Макс. скорость вращения диска, град/сек (при полном раскруте)")]
    [SerializeField] private float _spinSpeed = 2400f;
    [Tooltip("Время разгона до полной скорости, сек")]
    [SerializeField] private float _spinUpTime = 0.35f;
    [Tooltip("Время торможения до полной остановки, сек")]
    [SerializeField] private float _spinDownTime = 0.8f;

    [Header("Ввод")]
    [Tooltip("ВКЛ — крутить, пока ЛКМ ЗАЖАТА (отпустил = стоп). " +
             "ВЫКЛ — ЛКМ ПЕРЕКЛЮЧАЕТ кручение по клику.")]
    [SerializeField] private bool _holdToSpin = true;

    [Header("Слёт диска (случайный)")]
    [Tooltip("Мин. суммарное время кручения до слёта диска, сек")]
    [SerializeField] private float _minSpinBeforeBreak = 3f;
    [Tooltip("Макс. суммарное время кручения до слёта диска, сек")]
    [SerializeField] private float _maxSpinBeforeBreak = 12f;
    [Tooltip("Префаб улетающего диска (сетевой: NetworkObject + Rigidbody + " +
             "NetworkTransform + GrinderDiscProjectile). Пусто = только прятать диск.")]
    [SerializeField] private GameObject _discProjectilePrefab;
    [Tooltip("Базовая скорость выброса улетающего диска")]
    [SerializeField] private float _breakLaunchSpeed = 7f;
    [Tooltip("Случайный разброс скорости выброса (±)")]
    [SerializeField] private float _breakLaunchSpeedRandom = 3f;

    [Header("Искры при слёте диска")]
    [Tooltip("Сколько искр вылетает в момент слёта")]
    [SerializeField] private int _sparkCount = 120;

    [Tooltip("Скорость разлёта искр")]
    [SerializeField] private float _sparkSpeed = 7f;

    [Tooltip("Цвет сердцевины — раскалённый жёлтый")]
    [SerializeField] private Color _sparkHot = new Color(1f, 0.95f, 0.4f);

    [Tooltip("Цвет остывающего хвоста — оранжевый")]
    [SerializeField] private Color _sparkCool = new Color(1f, 0.42f, 0.05f);

    [Tooltip("Наклон струи искр вверх от горизонта, градусы")]
    [Range(0f, 80f)][SerializeField] private float _sparkElevation = 35f;

    [Tooltip("Разброс струи, градусы. Маленький — узкий сноп, большой — веер.")]
    [Range(3f, 90f)][SerializeField] private float _sparkSpread = 20f;

    [Tooltip("Сторона вылета выбирается случайно. Выключи — всегда вправо " +
             "относительно инструмента.")]
    [SerializeField] private bool _sparkRandomSide = true;

    [Tooltip("Пиковая яркость искр. Выше 1 уводит цвет в HDR — с Bloom в " +
             "пост-обработке искры начинают светиться. Без Bloom всё, что " +
             "выше 1, просто упрётся в белый.")]
    [Range(0.2f, 8f)][SerializeField] private float _sparkIntensity = 2.5f;

    [Header("Положение в руке (регулировка под модель)")]
    [Tooltip("Смещение относительно точки кисти (в системе координат руки)")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [Tooltip("Поворот относительно ориентации руки, градусы (Euler)")]
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    // ───────── СЕТЕВОЕ СОСТОЯНИЕ ─────────
    // Крутится ли диск прямо сейчас (задаёт владелец, знают все).
    private readonly SyncVar<bool> _spinning = new(false);
    // Есть ли диск на болгарке (false после слёта, true после R).
    private readonly SyncVar<bool> _hasDisc = new(true);

    // ───────── ЛОКАЛЬНЫЙ ВИЗУАЛ (у каждого клиента) ─────────
    private float _currentSpinScale;   // 0..1 текущая раскрутка (плавный разгон/торм.)
    private bool _lastHeld;            // предыдущее состояние ЛКМ у владельца

    // ───────── СЕРВЕР ─────────
    private float _spinAccum;          // накопленное время кручения
    private float _breakAt;            // порог слёта (случайный)
    private bool _breakScheduled;      // назначен ли порог слёта

    private const float MaxDt = 0.05f; // кламп кадра (готча №2 из отчёта)

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true; // болгарку берём в руку
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _hasDisc.OnChange += OnHasDiscChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _hasDisc.OnChange -= OnHasDiscChanged;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Синхронизируем видимость диска с текущим состоянием (важно для тех,
        // кто подключился позже, когда диск уже слетел).
        if (_disc != null) _disc.gameObject.SetActive(_hasDisc.Value);
    }

    // ───────── КАЖДЫЙ КАДР ─────────

    private void Update()
    {
        // Визуал вращения — у ВСЕХ клиентов (по синхронизированному флагу).
        UpdateDiscVisual();

        // Таймер слёта — только на сервере (единое событие для всех).
        if (base.IsServerInitialized) ServerTickBreak();

        // Ввод — только у владельца (держателя). Ownership выдаётся при подборе,
        // поэтому base.IsOwner == держатель. На земле объект серверный → не читаем.
        if (base.IsOwner) HandleOwnerInput();
    }

    /// <summary>Плавный разгон/торможение и само вращение диска. Считается
    /// локально по Time.deltaTime (клампится), НЕ по сетевым тикам.</summary>
    private void UpdateDiscVisual()
    {
        if (_disc == null) return;

        float dt = Mathf.Min(Time.deltaTime, MaxDt);
        float target = (_spinning.Value && _hasDisc.Value) ? 1f : 0f;

        float time = target > _currentSpinScale ? _spinUpTime : _spinDownTime;
        float step = time > 0.0001f ? dt / time : 1f;
        _currentSpinScale = Mathf.MoveTowards(_currentSpinScale, target, step);

        if (_currentSpinScale <= 0.0001f) return;

        float angle = _spinSpeed * _currentSpinScale * dt;
        // Space.Self — инкрементальный поворот относительно текущего, базовый
        // оффсет модели из блендера сохраняется автоматически (готча №3).
        _disc.Rotate(_discSpinAxis.normalized * angle, Space.Self);
    }

    private void HandleOwnerInput()
    {
        // R — перезарядка (только если диска нет).
        if (Input.GetKeyDown(KeyCode.R) && !_hasDisc.Value)
            CmdReload();

        if (_holdToSpin)
        {
            // Крутим, пока ЛКМ зажата и есть диск. Шлём команду только на смену
            // состояния (идемпотентно, без спама каждый кадр).
            bool held = Input.GetMouseButton(0) && _hasDisc.Value;
            if (held != _lastHeld)
            {
                _lastHeld = held;
                CmdSetSpinning(held);
            }
        }
        else
        {
            // Клик ЛКМ — переключить кручение.
            if (Input.GetMouseButtonDown(0) && _hasDisc.Value)
                CmdToggleSpin();
        }
    }

    // ───────── СЕРВЕР: КРУЧЕНИЕ ─────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetSpinning(bool value)
    {
        // Без диска крутить нельзя (будущее: тут же проверка питания от кабеля).
        if (value && !_hasDisc.Value) return;
        if (_spinning.Value == value) return;

        _spinning.Value = value;
        if (value) ScheduleBreak();
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdToggleSpin()
    {
        if (!_hasDisc.Value) return;
        _spinning.Value = !_spinning.Value;
        if (_spinning.Value) ScheduleBreak();
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdReload()
    {
        if (_hasDisc.Value) return;
        _hasDisc.Value = true;   // поставили новый диск
        // _spinning остаётся false — после перезарядки надо снова нажать ЛКМ.
    }

    /// <summary>Назначить случайный момент слёта диска при старте кручения.</summary>
    private void ScheduleBreak()
    {
        _spinAccum = 0f;
        _breakAt = Random.Range(_minSpinBeforeBreak, _maxSpinBeforeBreak);
        _breakScheduled = true;
    }

    /// <summary>Сервер копит время кручения; по достижению порога — слёт.</summary>
    private void ServerTickBreak()
    {
        if (!_spinning.Value || !_hasDisc.Value || !_breakScheduled) return;

        _spinAccum += Mathf.Min(Time.deltaTime, MaxDt);
        if (_spinAccum >= _breakAt)
        {
            _breakScheduled = false;
            ServerBreakDisc();
        }
    }

    /// <summary>Слёт диска: стоп кручения, диск исчезает, в мир летит обломок.</summary>
    private void ServerBreakDisc()
    {
        _spinning.Value = false;
        _hasDisc.Value = false;

        // Искры — отдельным RPC, а не реакцией на смену _hasDisc. Через
        // OnChange залп мог бы сыграть у того, кто подключился уже после слёта:
        // для него первичная синхронизация выглядит как переход true -> false.
        // Разовое событие надо и слать разово.
        Vector3 at = _disc != null ? _disc.position : transform.position;

        // Струя вбок и вверх — как от настоящей шлифовки. Сторону выбирает
        // СЕРВЕР и шлёт готовое направление: если бы каждый клиент кидал
        // кубик сам, искры полетели бы у всех в разные стороны.
        float side = _sparkRandomSide && Random.value < 0.5f ? -1f : 1f;
        Vector3 dir = (transform.right * side
                     + Vector3.up * Mathf.Tan(_sparkElevation * Mathf.Deg2Rad)).normalized;

        RpcDiscBrokeSparks(at, dir);

        if (_discProjectilePrefab != null && _disc != null)
            SpawnBrokenDisc();
    }

    /// <summary>
    /// Залп искр у ВСЕХ клиентов. Чистый визуал: частицы считаются локально,
    /// по сети летит только сам факт события и точка.
    /// </summary>
    [ObserversRpc]
    private void RpcDiscBrokeSparks(Vector3 position, Vector3 direction)
    {
        SparkBurst.Play(position, direction, _sparkSpread,
                        _sparkCount, _sparkSpeed, _sparkHot, _sparkCool,
                        _sparkIntensity);
    }

    private void SpawnBrokenDisc()
    {
        // Спавним в позиции/ориентации диска.
        Vector3 pos = _disc.position;
        Quaternion rot = _disc.rotation;
        GameObject go = Instantiate(_discProjectilePrefab, pos, rot);

        // Сервер регистрирует объект в сети (владелец — сервер, connection = null).
        // ВАЖНО: префаб должен быть в Spawnable Prefabs у NetworkManager.
        base.ServerManager.Spawn(go, null);

        // Случайное направление: вбок/вверх/вперёд-назад с разбросом.
        Vector3 dir = (transform.right * Random.Range(-1f, 1f)
                     + transform.up * Random.Range(0.3f, 1f)
                     + transform.forward * Random.Range(-1f, 1f)).normalized;
        float speed = _breakLaunchSpeed + Random.Range(-_breakLaunchSpeedRandom, _breakLaunchSpeedRandom);
        Vector3 spin = Random.insideUnitSphere * 25f; // случайное кувыркание

        // Задаём импульс на сервере (авторитет физики обломка — сервер).
        GrinderDiscProjectile proj = go.GetComponent<GrinderDiscProjectile>();
        if (proj != null)
        {
            proj.ServerLaunch(dir * speed, spin);
        }
        else
        {
            // Фолбэк, если на префабе нет скрипта — хотя бы толкнём Rigidbody.
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = dir * speed;
                rb.angularVelocity = spin;
            }
        }
    }

    // ───────── РЕАКЦИЯ НА СМЕНУ СОСТОЯНИЯ ─────────

    private void OnHasDiscChanged(bool prev, bool next, bool asServer)
    {
        // Показать/спрятать диск на болгарке.
        if (_disc != null) _disc.gameObject.SetActive(next);
        // Диск исчез — мгновенно сбрасываем локальную раскрутку.
        if (!next) _currentSpinScale = 0f;
    }

    // ───────── БРОСОК: перестаём крутить ─────────

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        if (_spinning.Value) _spinning.Value = false; // стоп при выпуске из руки
        _lastHeld = false;
        base.ServerOnDrop(hand, tossDirection, force);
    }

    // ───────── ПОЗА В РУКЕ (регулируется в инспекторе) ─────────

    public override void UpdateHeldPose(Transform handAnchor)
    {
        // Оффсет позиции — в системе координат руки; поворот — относительно руки.
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }

    // ─── ISpinningTool: для GrinderFoodProcessor ───

    /// <summary>Крутится и диск на месте — значит можно обрабатывать еду.</summary>
    public bool IsActive => _spinning.Value && _hasDisc.Value;

    /// <summary>Раскрутка 0..1. Идёт в качество обработки.</summary>
    public float Power01 => _currentSpinScale;

    /// <summary>Рабочая точка — центр диска.</summary>
    public Transform WorkPoint => _disc;
}
