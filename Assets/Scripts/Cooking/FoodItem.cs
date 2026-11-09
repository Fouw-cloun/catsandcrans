using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ПРЕДМЕТ-ПРОДУКТ в мире. Наследник InteractableItem — F/G и физика работают
/// без правок.
///
///   F   — взять / положить (база)
///   ЛКМ — ЗАРЯЖЕННЫЙ БРОСОК: зажал, накопил силу, отпустил
///   G   — бросить (база)
///
/// ДВА СЛОЯ ДАННЫХ:
///   Portion   — СЕРВЕРНАЯ правда: журнал обработок, масса, вкус. По сети не ходит.
///   SyncVar   — минимум для картинки: состояние и прогресс текущей обработки.
///
/// ПОСТЕПЕННАЯ ОБРАБОТКА: инструмент не применяет обработку мгновенно, а копит
/// прогресс 0..1 (ServerAddProgress). Когда накопилось — обработка применяется
/// и состояние переключается по графу формы. Прогресс синхронизируется, чтобы
/// FoodVisual плавно перетягивал материал.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody. Рядом — FoodVisual.
/// </summary>
public class FoodItem : InteractableItem
{
    [Header("Продукт")]
    [SerializeField] private IngredientDef _ingredient;

    [Tooltip("Форма: меш и граф его состояний")]
    [SerializeField] private FoodFormDef _form;

    [Tooltip("Масса в граммах. 0 = взять UnitMassG из справочника.")]
    [SerializeField] private int _massOverrideG = 0;

    [Tooltip("В каком радиусе искать ёмкость, когда продукт дошёл до " +
             "состояния-массы, метры")]
    [SerializeField] private float _dissolveSearchRadius = 0.6f;

    [Header("Бросок (ЛКМ зажать-отпустить)")]
    [SerializeField] private float _minThrowForce = 3f;
    [SerializeField] private float _maxThrowForce = 16f;

    // ───────── сеть ─────────
    private readonly SyncVar<byte> _state = new SyncVar<byte>(0);

    /// <summary>Прогресс текущей обработки, 0..255. 0 = ничего не идёт.</summary>
    private readonly SyncVar<byte> _progress = new SyncVar<byte>(0);

    /// <summary>В какое состояние ведёт текущая обработка — нужно визуалу,
    /// чтобы знать, к какому материалу перетягивать.</summary>
    private readonly SyncVar<byte> _progressTo = new SyncVar<byte>(0);

    // ───────── сервер ─────────
    /// <summary>Содержимое. Валидно ТОЛЬКО на сервере.</summary>
    public FoodPortion Portion { get; private set; }

    /// <summary>Поглощён ёмкостью, ждёт деспавна. Составной коллайдер спокойно
    /// выдаёт OnTriggerEnter несколько раз — без флага одна картошка
    /// записалась бы в чан дважды.</summary>
    public bool Consumed { get; private set; }

    private ProcessDef _activeProcess;   // какая обработка копится сейчас
    private bool _stateOverridden;       // состояние задано снаружи до спавна
    private float _progress01;           // точный прогресс (SyncVar огрублён до байта)
    private float _qualitySum;           // качество, взвешенное по вкладу
    private float _qualityWeight;

    public IngredientDef Ingredient => _ingredient;
    public FoodFormDef Form => _form;
    public byte StateIndex => _state.Value;
    public byte ProgressTo => _progressTo.Value;
    public float Progress01 => _progress.Value / 255f;
    public bool IsRotten => _state.Value == FoodFormDef.RottenIndex;

    public override bool ChargedPrimaryUse => true;

    /// <summary>Визуал подписывается сюда. Второй аргумент — секунды на плавный
    /// переход, 0 = мгновенно.</summary>
    public event System.Action<byte, float> StateChanged;
    public event System.Action ProgressChanged;

    private float _pendingBlend;

    // ───────────────────────── ЖИЗНЕННЫЙ ЦИКЛ ─────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (_ingredient == null || _form == null)
        {
            Debug.LogError($"[FoodItem] Не назначен Ingredient или Form на {name}", this);
            return;
        }

        // Portion уже задана, если объект пришёл извне (отрезанный кусок):
        // там её ставят ДО Spawn. Перезаписывать нельзя — потеряется журнал.
        if (Portion == null)
        {
            int mass = _massOverrideG > 0 ? _massOverrideG : _ingredient.UnitMassG;
            Portion = FoodPortion.FromIngredient(_ingredient, mass);
        }

        // Состояние тоже могли задать заранее: отрезанный кусок должен
        // унаследовать грязь или чистоту от той картошки, от которой отвалился.
        if (!_stateOverridden) _state.Value = 0;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _state.OnChange += OnStateChanged;
        _progress.OnChange += OnProgressChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _state.OnChange -= OnStateChanged;
        _progress.OnChange -= OnProgressChanged;
    }

    // ───────────────────────── БРОСОК ─────────────────────────

    public override void ServerPrimaryUse(UseContext ctx)
    {
        // Предмет мог потеряться, пока копился заряд — подстрахуемся.
        if (ctx.Hand == null || ctx.Hand.HeldItem != this) return;

        float force = Mathf.Lerp(_minThrowForce, _maxThrowForce, ctx.Charge);
        ctx.Hand.ServerDrop(ctx.Direction, force);
    }

    // ───────────────────────── ОБРАБОТКА ─────────────────────────

    /// <summary>
    /// Есть ли смысл применять эту обработку прямо сейчас. Инструмент спрашивает
    /// ДО того, как копить прогресс: нет хода в графе — нечего и чистить.
    /// </summary>
    public bool CanApplyNow(ProcessDef process)
    {
        if (process == null || _form == null || Consumed || IsRotten) return false;
        return _form.FindTransition(_state.Value, process, out _) >= 0;
    }

    /// <summary>
    /// Накопить прогресс обработки. Зовётся инструментом каждый кадр контакта.
    /// Когда прогресс дойдёт до 1, обработка применится сама.
    /// </summary>
    [Server]
    public bool ServerAddProgress(ProcessDef process, float delta01, float quality01)
    {
        if (!CanApplyNow(process) || delta01 <= 0f) return false;

        int to = _form.FindTransition(_state.Value, process, out _, out float workMul);
        if (to < 0) return false;

        // СКОРОСТЬ БЕРЁТСЯ С РЕБРА ГРАФА, а не из инструмента. Кусочки
        // разбиваются быстрее целой картофелины одним и тем же молотком —
        // и эта разница описана в данных, рядом с самим переходом, а не
        // таблицей внутри скрипта, которую пришлось бы править на каждый
        // новый продукт.
        delta01 *= workMul;

        // Сменили инструмент на полпути — начинаем заново. Иначе прогресс
        // от чистки достался бы, например, нарезке.
        if (_activeProcess != process)
        {
            _activeProcess = process;
            _progress01 = 0f;
            _qualitySum = 0f;
            _qualityWeight = 0f;
            _progressTo.Value = (byte)to;
        }

        _progress01 += delta01;

        // Качество взвешиваем по вкладу: секунда на полных оборотах весит
        // больше, чем секунда на подвисающих.
        _qualitySum += Mathf.Clamp01(quality01) * delta01;
        _qualityWeight += delta01;

        if (_progress01 >= 1f)
        {
            float quality = _qualityWeight > 0f ? _qualitySum / _qualityWeight : 1f;
            ResetProgress();
            ServerApplyProcess(process, quality);
            return true;
        }

        byte p = (byte)Mathf.RoundToInt(Mathf.Clamp01(_progress01) * 255f);

        // Не дёргаем SyncVar каждый кадр — только на заметное изменение.
        // Иначе десяток картошек под диском забьёт канал мусором.
        if (p != _progress.Value && (p == 0 || Mathf.Abs(p - _progress.Value) >= 4))
            _progress.Value = p;

        return true;
    }

    [Server]
    private void ResetProgress()
    {
        _activeProcess = null;
        _progress01 = 0f;
        _qualitySum = 0f;
        _qualityWeight = 0f;
        if (_progress.Value != 0) _progress.Value = 0;
    }

    /// <summary>
    /// Мгновенно применить обработку. Точка входа для всего: инструментов,
    /// струи, отладки. Отсутствие перехода — НЕ ошибка: обработка запишется
    /// в журнал, просто вид не изменится (варка вида не меняет, а для рецепта важна).
    /// </summary>
    [Server]
    public bool ServerApplyProcess(ProcessDef process, float quality01)
    {
        if (process == null || Portion == null || _form == null) return false;
        if (Consumed || IsRotten) return false;

        Portion.ApplyProcess(process, quality01);

        int to = _form.FindTransition(_state.Value, process, out float blend);
        if (to >= 0)
        {
            ResetProgress();
            _pendingBlend = blend;
            _state.Value = (byte)to;

            // Дошли до состояния, в котором предмет перестаёт быть предметом.
            if (_form.DissolvesAt((byte)to)) ServerTryDissolve();
        }
        return true;
    }

    /// <summary>
    /// Раствориться в ёмкости, если предмет в ней лежит.
    ///
    /// Ёмкость ищем ПО МИРУ вокруг себя, а не храним ссылку: продукт мог
    /// попасть в тару как угодно — уронили, закатился, переложили из другой, —
    /// и ни один из этих путей не обязан был ссылку проставить.
    ///
    /// Не нашли тары — предмет остаётся лежать в конечном состоянии. Это не
    /// ошибка: растолочь картошку на полу можно, просто пюре некуда деться.
    /// </summary>
    [Server]
    private void ServerTryDissolve()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, _dissolveSearchRadius,
                                                ~0, QueryTriggerInteraction.Collide);
        CookingVessel best = null;
        float nearest = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            CookingVessel v = hits[i].GetComponentInParent<CookingVessel>();
            if (v == null || !v.IsSpawned) continue;

            float d = (v.transform.position - transform.position).sqrMagnitude;
            if (d >= nearest) continue;

            nearest = d;
            best = v;
        }

        if (best == null)
        {
            Debug.Log($"[Продукт] «{name}» доведён до конечного состояния, но " +
                      "ёмкости рядом нет — остаётся лежать", this);
            return;
        }

        best.ServerDissolve(this);
    }

    /// <summary>Испортить. Вне графа — обработкой в это состояние не попасть.</summary>
    [Server]
    public void ServerSetRotten()
    {
        ResetProgress();
        _pendingBlend = 0f;
        _state.Value = FoodFormDef.RottenIndex;
    }

    [Server]
    public void ServerSetPortion(FoodPortion portion) => Portion = portion;

    /// <summary>
    /// Задать состояние ДО спавна. Нужно отрезанным кускам: кусок, отпиленный
    /// от чищеной картошки, обязан выглядеть чищеным, а не начинать с грязного.
    ///
    /// Работает, только если у куска ТА ЖЕ форма (FoodFormDef), что у целого
    /// продукта — иначе индексы состояний означают разное.
    /// </summary>
    [Server]
    public void ServerSetInitialState(byte state)
    {
        _stateOverridden = true;
        _state.Value = state;
    }

    [Server]
    public void ServerMarkConsumed() => Consumed = true;

    // ───────────────────────── СОБЫТИЯ ─────────────────────────

    /// <summary>
    /// У ХОСТА (сервер и клиент в одном процессе) OnChange приходит ДВАЖДЫ:
    /// сперва asServer = true, потом asServer = false. Пропускаем серверный
    /// вызов, если мы ещё и клиент, иначе визуал отработает дважды.
    /// </summary>
    private void OnStateChanged(byte prev, byte next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;

        // На клиенте длительность перехода известна из графа: ассет формы лежит
        // в префабе, значит одинаков у всех. Гонять её по сети незачем.
        float blend = asServer ? _pendingBlend : LookupBlend(prev, next);

        StateChanged?.Invoke(next, blend);
        _pendingBlend = 0f;
    }

    private void OnProgressChanged(byte prev, byte next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;
        ProgressChanged?.Invoke();
    }

    private float LookupBlend(byte from, byte to)
    {
        if (_form == null || _form.Transitions == null) return 0f;
        foreach (FoodTransition t in _form.Transitions)
            if (t.From == from && t.To == to) return t.BlendSeconds;
        return 0f;
    }
}
