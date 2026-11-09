using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// НАЛИТЬ ВОДЫ В ПОСУДУ СТРУЁЙ. Вешается туда же, где WaterJet — на
/// брандспойт или на выход гидранта.
///
/// Струя попала в горловину — вода полилась. Никаких кнопок: подставил
/// кастрюлю под колонку и держишь.
///
/// Вода — обычный ингредиент со своим IngredientDef, поэтому она честно
/// участвует во вкусе: разбавляет, снижает солёность, добавляет массу.
/// Отдельного «уровня воды» в кастрюле нет и не нужно.
///
/// СЕТЬ: наливает только сервер. Клиенты видят результат через весы —
/// масса и так синхронизируется.
/// </summary>
public class WaterJetFiller : NetworkBehaviour
{
    [Header("Что наливаем")]
    [Tooltip("Ингредиент воды. Form = Bulk, TastePerKg всё нули: вода " +
             "не добавляет вкуса, она его разбавляет.")]
    [SerializeField] private IngredientDef _water;

    [Tooltip("Граммов в секунду при полном напоре")]
    [SerializeField] private int _gramsPerSecond = 900;

    [Tooltip("Ниже этого напора не льётся")]
    [Range(0f, 1f)][SerializeField] private float _minFlow = 0.15f;

    [Header("Струя")]
    [Tooltip("Оставь пустым — найдётся сам: на себе, в детях, в родителях.")]
    [SerializeField] private WaterJet _jet;

    [Tooltip("Радиус захвата вокруг линии струи")]
    [SerializeField] private float _hitRadius = 0.3f;

    [Tooltip("Слой посуды")]
    [SerializeField] private LayerMask _vesselMask = ~0;

    [Tooltip("Как часто пересканировать струю, сек")]
    [SerializeField] private float _scanInterval = 0.1f;

    [Tooltip("Наливать ТОЛЬКО через триггер-горловину, а не через любой " +
             "коллайдер посуды. Иначе струя, задевшая борт снаружи, тоже " +
             "наливала бы воду — вода лилась бы мимо, а кастрюля наполнялась.")]
    [SerializeField] private bool _requireIntakeTrigger = true;

    private readonly Collider[] _hits = new Collider[24];
    private readonly HashSet<int> _seen = new HashSet<int>();
    private readonly List<CookingVessel> _under = new List<CookingVessel>();

    private float _nextScan;
    private float _lastScan;
    private float _carry;

    private int _lastRejectId = -1;
    private float _lastRejectTime;

    private void Awake()
    {
        if (_jet == null) _jet = GetComponent<WaterJet>();
        if (_jet == null) _jet = GetComponentInChildren<WaterJet>(true);
        if (_jet == null) _jet = GetComponentInParent<WaterJet>();

        if (_jet == null)
            Debug.LogError($"[Налив] на {name} и вокруг не найден WaterJet", this);
    }

    private void Update()
    {
        if (!base.IsServerInitialized) return;
        if (_jet == null || _water == null) return;

        if (!_jet.IsFlowing || _jet.Flow01 < _minFlow)
        {
            _under.Clear();
            _lastScan = 0f;
            return;
        }

        if (Time.time < _nextScan) return;

        float dt = _lastScan > 0f ? Mathf.Min(Time.time - _lastScan, 0.25f) : _scanInterval;
        _lastScan = Time.time;
        _nextScan = Time.time + _scanInterval;

        Scan();
        Pour(dt);
    }

    private void Scan()
    {
        _under.Clear();
        _seen.Clear();

        Vector3[] pts = _jet.PathPointsWorld;
        int count = _jet.PathCount;
        if (pts == null || count < 2) return;

        for (int i = 1; i < count; i++)
        {
            int n = Physics.OverlapCapsuleNonAlloc(pts[i - 1], pts[i], _hitRadius,
                                                   _hits, _vesselMask,
                                                   QueryTriggerInteraction.Collide);
            for (int k = 0; k < n; k++)
            {
                // Только горловина: она у кастрюли триггер, а корпус — нет.
                if (_requireIntakeTrigger && !_hits[k].isTrigger) continue;

                CookingVessel v = _hits[k].GetComponentInParent<CookingVessel>();
                if (v == null || !v.IsSpawned) continue;

                if (v.IsSealed) { Reject(v, "ёмкость закрыта (Sealed)"); continue; }

                // Накрытая кастрюля воду не принимает — крышка мешает.
                PotLidSocket lid = v.GetComponent<PotLidSocket>();
                if (lid != null && lid.HasLid) { Reject(v, "накрыта крышкой"); continue; }

                // Пробитая не держит воду: нальётся и тут же вытечет.
                PotThermal pot = v.GetComponent<PotThermal>();
                if (pot != null && pot.IsRuptured)
                {
                    Reject(v, "пробита — сначала заварить");
                    continue;
                }

                if (v.FreeSpaceG <= 0) { Reject(v, "полная"); continue; }

                // Одна кастрюля легко попадает под несколько отрезков струи —
                // без этой проверки она получала бы двойную порцию.
                if (!_seen.Add(v.NetworkObject.ObjectId)) continue;

                _under.Add(v);
            }
        }
    }

    /// <summary>Один лог на причину, а не поток каждый кадр: иначе консоль
    /// заливает, а понять всё равно ничего нельзя.</summary>
    private void Reject(CookingVessel v, string why)
    {
        int id = v.NetworkObject.ObjectId;
        if (_lastRejectId == id && Time.time - _lastRejectTime < 2f) return;

        _lastRejectId = id;
        _lastRejectTime = Time.time;
        Debug.Log($"[Налив] «{v.name}» воду не принимает: {why}", v);
    }

    [Server]
    private void Pour(float dt)
    {
        if (_under.Count == 0) return;

        _carry += _gramsPerSecond * _jet.Flow01 * dt;
        int total = Mathf.FloorToInt(_carry);
        if (total <= 0) return;
        _carry -= total;

        // Струя одна: если под неё подставили две кастрюли, воды больше
        // не станет — она поделится.
        int each = Mathf.Max(1, total / _under.Count);

        foreach (CookingVessel v in _under)
        {
            if (v == null || !v.IsSpawned) continue;

            int room = v.FreeSpaceG;
            if (room <= 0) continue;

            FoodPortion water = FoodPortion.FromIngredient(_water, Mathf.Min(each, room));
            if (water != null) v.TryAdd(water);
        }
    }
}
