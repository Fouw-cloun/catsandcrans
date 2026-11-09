using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ЁМКОСТЬ С ЕДОЙ: чан, казан, сковорода, противень, ведро, миска.
///
/// ПОЧЕМУ ОТДЕЛЬНЫЙ КОМПОНЕНТ, А НЕ НАСЛЕДНИК InteractableItem:
///   Ёмкостью может быть что угодно — переносимая кастрюля (BalanceItem),
///   стационарный чан на земле, барабан бетономешалки (часть техники).
///   Наследование заставило бы плодить CookingBalanceItem, CookingVehiclePart
///   и так далее. Композиция: вешаем этот компонент РЯДОМ с любым
///   InteractableItem — как ты уже сделал с IVehiclePart.
///
/// СЕТЬ (по паттерну проекта):
///   - _content     — СЕРВЕРНАЯ правда, полные данные, по сети не ходит.
///   - _view        — SyncList слепков, только для UI и визуала.
///   - _totalMassG  — SyncVar, это и есть показания весов.
///   Индексы _view зеркалят _content. Любое изменение одного — сразу второго.
///
/// Требует на объекте: NetworkObject. Плюс триггер-коллайдер (горловина),
/// в который кидают продукты.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CookingVessel : NetworkBehaviour
{
    [Header("Ёмкость")]
    [SerializeField] private VesselDef _def;

    [Tooltip("Триггер-коллайдер горловины. Если пусто — ищется на этом объекте " +
             "первый коллайдер с isTrigger.")]
    [SerializeField] private Collider _intake;

    // ───────── серверная правда ─────────
    private readonly List<FoodPortion> _content = new List<FoodPortion>();

    private ProcessDef _cookProcess;
    private float _cookProgress;
    private float _cookQualitySum;
    private float _cookWeight;

    // ───────── что видят клиенты ─────────
    private readonly SyncList<FoodPortionView> _view = new SyncList<FoodPortionView>();
    private readonly SyncVar<int> _totalMassG = new SyncVar<int>(0);

    /// <summary>Прогресс готовки для показа. Сама готовка считается на сервере.</summary>
    private readonly SyncVar<byte> _cookShown = new SyncVar<byte>(0);
    private readonly SyncVar<bool> _sealed = new SyncVar<bool>(false);

    public VesselDef Def => _def;
    public IReadOnlyList<FoodPortionView> View => _view;

    /// <summary>Показания весов, кг. Наружу ВСЕГДА килограммы: 18000 г в UI
    /// выглядит глупо и заодно палит внутреннюю точность системы.</summary>
    public float MassKg => _totalMassG.Value / 1000f;

    public int FreeSpaceG => _def != null ? _def.CapacityG - _totalMassG.Value : 0;
    public bool IsSealed => _sealed.Value;
    public bool IsEmpty => _totalMassG.Value <= 0;

    private void Awake()
    {
        if (_intake == null)
        {
            foreach (Collider c in GetComponents<Collider>())
                if (c.isTrigger) { _intake = c; break; }
        }
        if (_def == null)
            Debug.LogError($"[CookingVessel] Не назначен VesselDef на {name}", this);
    }

    // ───────────────────────── ПРИЁМ ЕДЫ ─────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!base.IsServerInitialized) return;

        FoodItem item = other.GetComponentInParent<FoodItem>();
        if (item == null || item.Consumed || item.Portion == null) return;
        if (!item.IsSpawned) return;

        // ХРАНИЛИЩЕ НЕ ГЛОТАЕТ. Банка и тазик держат продукты физическими
        // предметами: их видно, их можно долбить, резать и доставать обратно.
        // Растворяются они позже и по своей воле — дойдя до состояния с
        // флагом DissolvesIntoVessel (см. ServerDissolve).
        if (_def != null && !_def.AbsorbOnContact) return;

        // ПОРЯДОК ВАЖЕН. Сначала проверяем и добавляем, и только потом убиваем
        // предмет. Если сделать наоборот, при полном чане морковка исчезнет в
        // никуда.
        if (!TryAdd(item.Portion.Clone())) return;

        item.ServerMarkConsumed();

        // Владение снимаем ДО деспавна. Иначе клиент-владелец ещё кадр-другой
        // шлёт позицию уже мёртвого объекта через client-authoritative
        // NetworkTransform, и в лог сыплются ошибки.
        item.NetworkObject.RemoveOwnership();
        base.Despawn(item.NetworkObject);
    }

    /// <summary>
    /// Растворить предмет в этой ёмкости: масса и журнал уходят во фракцию,
    /// сам объект деспавнится.
    ///
    /// Отдельно от OnTriggerEnter, потому что повод другой: там продукт попал
    /// в кастрюлю, здесь — доработался до состояния, в котором предметом быть
    /// перестал. Пюре не бывает отдельной картофелиной.
    ///
    /// Порядок тот же и по той же причине: сначала пробуем добавить, и только
    /// если получилось — убиваем предмет. Иначе при полной таре продукт
    /// исчезнет в никуда.
    /// </summary>
    [Server]
    public bool ServerDissolve(FoodItem item)
    {
        if (item == null || item.Consumed || item.Portion == null) return false;
        if (!item.IsSpawned) return false;

        int massG = item.Portion.MassG;
        Vector3 where = item.transform.position;

        if (!TryAdd(item.Portion.Clone())) return false;

        // Сообщаем визуалу, ГДЕ именно легла масса — от этого зависит, в
        // каком месте тары вырастет кучка. Отдельным RPC, а не SyncVar:
        // событие мгновенное и никого не интересует после того, как кучка
        // выросла. Опоздавший игрок увидит уже слитую массу, и это правильно —
        // к его приходу кучки всё равно растеклись бы.
        RpcDeposited(where, massG);

        item.ServerMarkConsumed();

        // Владение снимаем ДО деспавна: иначе клиент-владелец ещё кадр-другой
        // шлёт позицию уже мёртвого объекта, и в лог сыплются ошибки.
        item.NetworkObject.RemoveOwnership();
        base.Despawn(item.NetworkObject);

        Debug.Log($"[Ёмкость] «{name}»: «{item.name}» растворился во фракцию", this);
        return true;
    }

    /// <summary>Показать всем, что в этом месте тары легла порция массы.</summary>
    [ObserversRpc(BufferLast = false)]
    private void RpcDeposited(Vector3 worldPos, int massG)
    {
        PasteVisual paste = GetComponent<PasteVisual>() ?? GetComponentInChildren<PasteVisual>();
        if (paste != null) paste.AddDeposit(worldPos, massG);
    }

    /// <summary>
    /// Добавить фракцию. Возвращает false, если не влезло или ёмкость закрыта —
    /// вызывающий должен оставить предмет в мире, а не удалять его.
    /// </summary>
    [Server]
    public bool TryAdd(FoodPortion incoming)
    {
        if (incoming == null || incoming.MassG <= 0) return false;
        if (_def == null) return false;
        if (_sealed.Value) return false;
        if (incoming.MassG > FreeSpaceG) return false;

        // Ищем фракцию с ТЕМ ЖЕ ингредиентом и ТОЙ ЖЕ цепочкой обработок.
        for (int i = 0; i < _content.Count; i++)
        {
            if (!FoodPortion.CanMerge(_content[i], incoming)) continue;

            _content[i].MergeIn(incoming);
            _view[i] = _content[i].ToView();
            RecalcTotals();
            return true;
        }

        _content.Add(incoming);
        _view.Add(incoming.ToView());
        RecalcTotals();
        return true;
    }

    // ───────────────────────── ОТБОР ЕДЫ ─────────────────────────

    /// <summary>
    /// Отлить/отсыпать часть содержимого. Забирает пропорционально ОТ ВСЕХ
    /// фракций — то есть половник зачерпывает смесь, а не выборочно мясо.
    /// Нужно для фасовки по мискам и для воровства котами.
    /// </summary>
    [Server]
    public List<FoodPortion> TakeMass(int massG)
    {
        List<FoodPortion> taken = new List<FoodPortion>();
        if (massG <= 0 || _totalMassG.Value <= 0) return taken;

        float k = Mathf.Clamp01((float)massG / _totalMassG.Value);

        for (int i = _content.Count - 1; i >= 0; i--)
        {
            FoodPortion src = _content[i];
            FoodPortion part = src.Clone();
            part.ScaleMass(k);                 // вкус уезжает вместе с массой
            if (part.MassG <= 0) continue;

            src.ScaleMass(1f - k);
            taken.Add(part);

            if (src.MassG <= 0)
            {
                _content.RemoveAt(i);
                _view.RemoveAt(i);
            }
            else
            {
                _view[i] = src.ToView();
            }
        }

        RecalcTotals();
        return taken;
    }

    [Server]
    public void ServerClear()
    {
        _content.Clear();
        _view.Clear();
        _sealed.Value = false;
        RecalcTotals();
    }

    // ───────────────────────── СЛУЖЕБНОЕ ─────────────────────────

    [Server]
    private void RecalcTotals()
    {
        int sum = 0;
        for (int i = 0; i < _content.Count; i++) sum += _content[i].MassG;
        _totalMassG.Value = sum;
    }

    /// <summary>Умеет ли эта ёмкость данную группу обработок.
    /// Сковорода не сварит бульон, чан не спассерует лук.</summary>
    public bool Supports(ProcessGroup group)
    {
        if (_def == null || _def.SupportedProcesses == null) return false;
        foreach (ProcessGroup g in _def.SupportedProcesses)
            if (g == group) return true;
        return false;
    }

    /// <summary>
    /// Есть ли внутри хоть один продукт, для которого этот процесс разрешён.
    ///
    /// ЭТО ВТОРОЙ, НЕЗАВИСИМЫЙ ФИЛЬТР — проверяет не ёмкость, а содержимое.
    /// Suppports(group) отвечает «умеет ли ЧАН кипятить», этот метод — «есть
    /// ли там ХОТЬ ЧТО-ТО, что вообще можно кипятить». Чан может поддерживать
    /// LiquidHeat и при этом быть набит одной солью — оба ответа важны
    /// по отдельности, и инструмент должен уметь объяснить игроку, какой
    /// именно из них не выполнен.
    /// </summary>
    public bool AnyContentAllows(ProcessDef process)
    {
        if (process == null) return false;
        for (int i = 0; i < _content.Count; i++)
            if (_content[i].CanApply(process)) return true;
        return false;
    }

    /// <summary>Суммарный вкус. Только сервер — клиенту он не нужен.</summary>
    [Server]
    public TasteVector TotalTaste()
    {
        TasteVector t = TasteVector.Zero;
        for (int i = 0; i < _content.Count; i++) t += _content[i].Taste;
        return t;
    }

    /// <summary>Серверное содержимое для будущего распознавателя рецептов.</summary>
    public IReadOnlyList<FoodPortion> ServerContent => _content;

    // ───────────────────────── ГОТОВКА ─────────────────────────

    /// <summary>
    /// Готовить ВСЁ содержимое разом: варить, жарить, томить.
    ///
    /// Прогресс копится на самой ёмкости, а не на каждой фракции: варится
    /// суп целиком, а не морковка отдельно от картошки. Когда набралось —
    /// обработка записывается в журнал каждой фракции с накопленным качеством.
    ///
    /// ПРИМЕНЯЕТСЯ ТОЛЬКО К ТЕМ ФРАКЦИЯМ, КОТОРЫМ ЭТОТ ПРОЦЕСС РАЗРЕШЁН
    /// (FoodPortion.CanApply). Соль в супе не толчётся вместе с картошкой,
    /// даже если инструмент бьёт по всей кастрюле разом — она останется солью,
    /// просто не получит запись в журнал. Раньше процесс лился на всё подряд:
    /// тара проверяла себя, продукт — никто.
    ///
    /// Качество усредняется ПО ВРЕМЕНИ: минута идеальной температуры и минута
    /// кипятка дадут середину, а не последнее значение.
    /// </summary>
    [Server]
    public bool ServerCook(ProcessDef process, float delta01, float quality01)
    {
        if (process == null || _content.Count == 0 || delta01 <= 0f) return false;

        // Ни один продукт внутри этот процесс не принимает — нечего и
        // начинать копить прогресс. Без этой проверки игрок долбил бы пустышку
        // до победного, а на 100% ничего бы не произошло — узнать причину
        // раньше, чем потратить время, куда честнее.
        if (!AnyContentAllows(process))
        {
            Debug.Log($"[Ёмкость] «{name}»: ни один продукт внутри не принимает " +
                      $"«{process.DisplayName}»", this);
            return false;
        }

        // Сменили процесс на полпути — начинаем заново. Иначе прогресс варки
        // достался бы, например, жарке.
        if (_cookProcess != process)
        {
            _cookProcess = process;
            _cookProgress = 0f;
            _cookQualitySum = 0f;
            _cookWeight = 0f;
        }

        _cookProgress += delta01;
        _cookQualitySum += Mathf.Clamp01(quality01) * delta01;
        _cookWeight += delta01;

        if (_cookProgress < 1f)
        {
            byte p = (byte)Mathf.RoundToInt(Mathf.Clamp01(_cookProgress) * 255f);
            if (Mathf.Abs(p - _cookShown.Value) >= 4) _cookShown.Value = p;
            return false;
        }

        float quality = _cookWeight > 0f ? _cookQualitySum / _cookWeight : 1f;
        int affected = 0;

        for (int i = 0; i < _content.Count; i++)
        {
            // Продукт, которому процесс не разрешён, остаётся нетронутым —
            // не получает ни записи в журнал, ни изменения массы или вкуса.
            // Соль, случайно попавшая в банку с картошкой под молотком,
            // так и останется солью.
            if (!_content[i].CanApply(process)) continue;

            _content[i].ApplyProcess(process, quality);
            _view[i] = _content[i].ToView();
            affected++;
        }

        RecalcTotals();
        ServerResetCook();

        Debug.Log($"[Ёмкость] «{name}»: {process.DisplayName} готово для " +
                  $"{affected} из {_content.Count} фракций, качество {quality * 100f:0}%", this);
        return true;
    }

    [Server]
    public void ServerResetCook()
    {
        _cookProcess = null;
        _cookProgress = 0f;
        _cookQualitySum = 0f;
        _cookWeight = 0f;
        if (_cookShown.Value != 0) _cookShown.Value = 0;
    }

    /// <summary>Насколько готово текущее блюдо, 0..1 — для подсказки игроку.</summary>
    public float CookProgress01 => _cookShown.Value / 255f;
}
