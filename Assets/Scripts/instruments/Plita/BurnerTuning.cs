using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// МИНИ-ИГРА ВОССТАНОВЛЕНИЯ НАСТРОЙКИ. Вешается на конфорку, рядом со StoveBurner.
///
/// КОГДА ОНА ВООБЩЕ ЗАПУСКАЕТСЯ: только когда конфорку СБИЛО
/// (StoveBurner.NeedsTuning). Пока всё в порядке, ЛКМ по ручке просто крутит
/// температуру. Игра — это починка, а не способ включить плиту.
///
/// ПРАВИЛА:
///   по шкале плывёт МЕТКА — то, куда рвётся пламя;
///   игрок держит ЛКМ, зона ползёт вверх; отпустил — падает;
///   метка внутри зоны — копится фиксация;
///   набралась — ручка возвращается на прежнюю уставку;
///   не успел за отведённое время — ручку СРЫВАЕТ, и конфорка идёт вразнос.
///
/// ЧТО ИЗМЕНИЛОСЬ ПРОТИВ ПРОШЛОЙ ВЕРСИИ:
///   - дрейф уехал в StoveBurner. Ручкой владеет конфорка, и уводить её должна
///     тоже она; здесь оставалась половина логики, которая дёргала чужое поле;
///   - появился ПРОВАЛ. Раньше игру можно было только выиграть или бросить,
///     то есть риска в ней не было вообще;
///   - метка плывёт шумом, а не скачет сменой направления;
///   - положения шлются 20 раз в секунду, а не каждый кадр.
///
/// СЕТЬ: игру ведёт тот, кто её открыл (плавно, каждый кадр), результат уходит
/// на сервер. Метка и зона синхронизируются, чтобы напарник видел, как ты
/// мучаешься.
/// </summary>
[RequireComponent(typeof(StoveBurner))]
public class BurnerTuning : NetworkBehaviour
{
    [Header("Управление")]
    [Tooltip("Держать, чтобы зона ползла вверх")]
    [SerializeField] private KeyCode _holdKey = KeyCode.Mouse0;

    [Tooltip("Бросить настройку без последствий")]
    [SerializeField] private KeyCode _cancelKey = KeyCode.Escape;

    [Tooltip("Дальность, дальше которой настройка сама отваливается, метры")]
    [SerializeField] private float _reach = 2.5f;

    [Header("Зона игрока")]
    [Tooltip("Высота зоны в долях шкалы. Больше — легче.")]
    [Range(0.05f, 0.5f)][SerializeField] private float _zoneSize = 0.22f;

    [Tooltip("Ускорение вверх при зажатой клавише")]
    [SerializeField] private float _riseAccel = 1.9f;

    [Tooltip("Ускорение вниз, когда отпустил")]
    [SerializeField] private float _fallAccel = 1.4f;

    [Tooltip("Трение: гасит разгон, иначе зона летает как мяч")]
    [Range(0f, 8f)][SerializeField] private float _damping = 4.2f;

    [Tooltip("Потолок скорости зоны, доли шкалы в секунду")]
    [SerializeField] private float _zoneMaxSpeed = 0.9f;

    [Header("Метка")]
    [Tooltip("Как резво метка ходит по шкале")]
    [SerializeField] private float _markSpeed = 0.16f;

    [Tooltip("Насколько далеко метка отходит от центра, доли шкалы")]
    [Range(0.1f, 0.5f)][SerializeField] private float _markRange = 0.32f;

    [Header("Фиксация")]
    [Tooltip("Секунд удержания метки в зоне, чтобы зафиксировать")]
    [Min(0.3f)][SerializeField] private float _secondsToLock = 1.5f;

    [Tooltip("Как быстро откатывается, если метка вышла из зоны. Заметно " +
             "медленнее набора: мелкий промах не должен стирать всё сразу.")]
    [SerializeField] private float _loseSpeed = 0.35f;

    [Header("Провал")]
    [Tooltip("Сколько секунд даётся на всю игру. Не успел — ручку срывает, " +
             "конфорка идёт вразнос.")]
    [Min(3f)][SerializeField] private float _secondsToFail = 14f;

    [Header("Сеть")]
    [Tooltip("Сколько раз в секунду слать напарнику положение метки и зоны. " +
             "Раньше слалось КАЖДЫЙ КАДР — под 150 ServerRpc в секунду на " +
             "конфорку, отсюда и дёрганье шкалы у клиента.")]
    [Min(5f)][SerializeField] private float _reportRate = 20f;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud = true;

    // ───────── сеть ─────────
    private readonly SyncVar<bool> _tuning = new SyncVar<bool>(false);

    /// <summary>Положение метки и зоны, 0..255 — чтобы напарник видел то же.</summary>
    private readonly SyncVar<byte> _markPos = new SyncVar<byte>(128);
    private readonly SyncVar<byte> _zonePos = new SyncVar<byte>(0);
    private readonly SyncVar<byte> _lockProgress = new SyncVar<byte>(0);

    private StoveBurner _burner;

    // игру ведёт тот, кто её открыл — локально
    private float _zone;
    private float _zoneVel;
    private float _mark = 0.5f;
    private float _markSeed;
    private float _lock;
    private bool _playing;
    private float _deadline;
    private float _nextReport;

    public bool IsTuning => _tuning.Value;

    /// <summary>Нужна ли настройка. Спрашиваем у конфорки: состояние ручки —
    /// её дело, а не наше.</summary>
    public bool NeedsTuning => _burner != null && _burner.NeedsTuning;

    public float MarkPos01 => _markPos.Value / 255f;
    public float ZonePos01 => _zonePos.Value / 255f;
    public float ZoneSize => _zoneSize;
    public float LockProgress01 => _lockProgress.Value / 255f;

    /// <summary>Сколько секунд осталось. Для полоски таймера у того, кто играет.</summary>
    public float TimeLeft01 => _playing
        ? Mathf.Clamp01((_deadline - Time.time) / Mathf.Max(1f, _secondsToFail))
        : 0f;

    private void Awake() => _burner = GetComponent<StoveBurner>();

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Новая сессия — никакой недоигранной настройки с прошлого раза.
        _tuning.Value = false;
        _lockProgress.Value = 0;
    }

    // ───────────────────────── ИГРА У ИГРОКА ─────────────────────────

    private void Update()
    {
        if (!_playing) return;

        // Отошёл от плиты — настройка сама отваливается. Без последствий:
        // наказывать за то, что тебя позвали в другую комнату, незачем.
        if (!InReach()) { CancelTuning(); return; }
        if (Input.GetKeyDown(_cancelKey)) { CancelTuning(); return; }

        PlayTick();
    }

    private bool InReach()
    {
        Camera cam = Camera.main;
        if (cam == null) return true;       // нет камеры — не мешаем игре

        // Полтора запаса, чтобы настройка не срывалась от шага назад.
        return Vector3.Distance(cam.transform.position, transform.position) <= _reach * 1.5f;
    }

    /// <summary>
    /// Начать настройку. Зовётся StoveKnobUser, когда игрок зажал ЛКМ на
    /// сбившейся ручке. Проверку прицела делает вызывающая сторона.
    /// </summary>
    public void BeginTuning()
    {
        if (_playing) return;

        // Кто-то уже крутит эту конфорку. Без проверки двое игроков вели бы
        // одну шкалу, забивая друг другу метку через CmdReport.
        if (_tuning.Value) return;

        // Играть незачем, если ручка и так на месте.
        if (_burner == null || !_burner.NeedsTuning) return;
        if (_burner.IsBroken || _burner.IsDead) return;

        _playing = true;
        _zone = 0.15f;
        _zoneVel = 0f;
        _markSeed = Random.value * 100f;
        _mark = 0.5f;
        _lock = 0f;
        _deadline = Time.time + _secondsToFail;
        _nextReport = 0f;

        CmdSetTuning(true);
    }

    /// <summary>Бросить настройку. Без штрафа — ручка просто остаётся сбитой.</summary>
    public void CancelTuning()
    {
        if (!_playing) return;
        _playing = false;
        CmdSetTuning(false);
    }

    private void PlayTick()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        MoveZone(dt);
        MoveMark(dt);

        // Метка внутри зоны — копим фиксацию, иначе откатываем.
        float half = _zoneSize * 0.5f;
        bool inside = Mathf.Abs(_mark - _zone) <= half;

        _lock += inside ? dt / Mathf.Max(0.3f, _secondsToLock)
                        : -dt * _loseSpeed;
        _lock = Mathf.Clamp01(_lock);

        // Шлём с фиксированной частотой, а не каждый кадр.
        if (Time.time >= _nextReport)
        {
            _nextReport = Time.time + 1f / Mathf.Max(5f, _reportRate);
            CmdReport(_mark, _zone, _lock);
        }

        if (_lock >= 1f)
        {
            _playing = false;
            CmdFinish(true);
            return;
        }

        // Время вышло — ручку срывает. Именно ЛИМИТ ВРЕМЕНИ, а не «упустил
        // метку»: мгновенный проигрыш за одну ошибку в игре на удержание
        // ощущается как подлость, а часы на стене видно и можно поднажать.
        if (Time.time >= _deadline)
        {
            _playing = false;
            CmdFinish(false);
        }
    }

    /// <summary>
    /// Зона ползёт вверх при удержании и падает, когда отпустил.
    ///
    /// Через ускорение и трение, а не напрямую позицией: так у зоны есть
    /// инерция, её надо вести, а не ставить. В этом вся возня.
    /// </summary>
    private void MoveZone(float dt)
    {
        float accel = Input.GetKey(_holdKey) ? _riseAccel : -_fallAccel;

        _zoneVel += accel * dt;

        // Трение по ЭКСПОНЕНТЕ, а не вычитанием.
        //
        // Это была главная причина, по которой игру трясло. При записи
        // vel -= vel * damping * dt множитель (1 - damping*dt) на просадке
        // кадров уходит в минус — скорость каждый кадр меняет знак, и зона
        // дребезжит сама по себе. Exp(-damping*dt) отрицательным не станет
        // никогда и от частоты кадров почти не зависит.
        _zoneVel *= Mathf.Exp(-_damping * dt);
        _zoneVel = Mathf.Clamp(_zoneVel, -_zoneMaxSpeed, _zoneMaxSpeed);

        _zone += _zoneVel * dt;

        // У краёв зона останавливается, а не отскакивает: отскок сбивал бы
        // прицел в самый неподходящий момент.
        float half = _zoneSize * 0.5f;
        if (_zone < half) { _zone = half; _zoneVel = Mathf.Max(0f, _zoneVel); }
        else if (_zone > 1f - half) { _zone = 1f - half; _zoneVel = Mathf.Min(0f, _zoneVel); }
    }

    /// <summary>
    /// Метка гуляет по шкале ШУМОМ ПЕРЛИНА, а не сменой направления.
    ///
    /// Было: раз в 0.4–1.6 секунды направление мгновенно переворачивалось.
    /// Метка дёргалась рывками, догнать её инерционной зоной было делом удачи,
    /// и игра ощущалась нечестной — движение нельзя было предсказать даже на
    /// полсекунды вперёд. Тот же приём, что у дрейфа конфорки, и по той же
    /// причине.
    ///
    /// Держится в средней части шкалы: у самого края её не поймать зоной,
    /// которая туда упирается.
    /// </summary>
    private void MoveMark(float dt)
    {
        float n = Mathf.PerlinNoise(Time.time * _markSpeed, _markSeed) - 0.5f;
        float target = 0.5f + n * 2f * _markRange;

        _mark = Mathf.Lerp(_mark, target, 1f - Mathf.Exp(-3.5f * dt));
        _mark = Mathf.Clamp(_mark, 0.05f, 0.95f);
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void CmdSetTuning(bool value)
    {
        _tuning.Value = value;
        if (!value) _lockProgress.Value = 0;
    }

    /// <summary>Положения для показа напарнику. Два байта, но 20 раз в секунду.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void CmdReport(float mark, float zone, float lockProgress)
    {
        _markPos.Value = (byte)Mathf.RoundToInt(Mathf.Clamp01(mark) * 255f);
        _zonePos.Value = (byte)Mathf.RoundToInt(Mathf.Clamp01(zone) * 255f);
        _lockProgress.Value = (byte)Mathf.RoundToInt(Mathf.Clamp01(lockProgress) * 255f);
    }

    /// <summary>
    /// Итог игры. Что именно делать с ручкой, решает КОНФОРКА — здесь мы
    /// только сообщаем результат. Иначе мини-игра лезла бы в чужое состояние
    /// и знала бы про поломку, искры и уставку, к которым отношения не имеет.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void CmdFinish(bool success)
    {
        _tuning.Value = false;
        _lockProgress.Value = 0;

        if (_burner == null) return;

        if (success) _burner.ServerTuningSucceeded();
        else _burner.ServerTuningFailed();
    }

    /// <summary>Оборвать настройку с сервера: кастрюлю сорвало, конфорку
    /// починили, партия закончилась.</summary>
    [Server]
    public void ServerAbort()
    {
        if (_tuning.Value) _tuning.Value = false;
        _lockProgress.Value = 0;
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void OnGUI()
    {
        if (!_debugHud || !_playing) return;

        // Вертикальная шкала сбоку от прицела — как удочка в Stardew.
        float w = 44f, h = 260f;
        float x = Screen.width * 0.5f + 220f;
        float y = Screen.height * 0.5f - h * 0.5f;

        GUI.Box(new Rect(x, y, w, h), GUIContent.none);

        // Зона игрока
        float zh = _zoneSize * h;
        float zy = y + h - (_zone + _zoneSize * 0.5f) * h;
        DrawRect(new Rect(x + 4f, zy, w - 8f, zh), new Color(0.3f, 0.9f, 0.45f, 0.55f));

        // Метка
        float my = y + h - _mark * h;
        DrawRect(new Rect(x + 2f, my - 4f, w - 4f, 8f), new Color(1f, 0.8f, 0.2f, 0.95f));

        // Прогресс фиксации — слева
        DrawRect(new Rect(x - 14f, y + h * (1f - _lock), 9f, h * _lock),
                 new Color(0.4f, 1f, 0.5f, 0.85f));

        // Оставшееся время — справа. Краснеет к концу, чтобы провал не
        // случался «вдруг».
        float t = TimeLeft01;
        DrawRect(new Rect(x + w + 5f, y + h * (1f - t), 9f, h * t),
                 Color.Lerp(new Color(1f, 0.35f, 0.2f, 0.9f),
                            new Color(0.5f, 0.8f, 1f, 0.8f), t));

        GUIStyle st = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        st.normal.textColor = Color.white;
        GUI.Label(new Rect(x - 40f, y + h + 6f, 240f, 24f),
                  $"держи ЛКМ — {_lock * 100f:0}%   {(_deadline - Time.time):0.0} с", st);
    }

    private static Texture2D _white;

    private static void DrawRect(Rect r, Color c)
    {
        if (_white == null)
        {
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
        }

        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, _white);
        GUI.color = prev;
    }
}
