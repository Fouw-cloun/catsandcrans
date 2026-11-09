using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// ТОЛЧЕНИЕ ОТБОЙНЫМ МОЛОТКОМ. Вешается на КОРЕНЬ молотка, туда же, где
/// JackhammerItem. Заменяет собой блендер и толкушку.
///
/// КАК ИДЁТ ПРОЦЕСС
///   в банке лежат ПРЕДМЕТЫ — картофелины, кусочки;
///   долбишь — у каждого копится прогресс, меш проседает (FoodSquashVisual);
///   прогресс дошёл — предмет переходит по графу формы в следующее состояние;
///   состояние помечено DissolvesIntoVessel — предмет растворяется в таре
///   массой, и вот она уже пюре.
///
/// БЬЁТ ПО ПРЕДМЕТАМ, А НЕ ПО ТАРЕ. Прошлая версия звала CookingVessel.ServerCook
/// и обрабатывала абстрактную массу — тогда картошка исчезала в момент касания
/// банки и долбить было нечего. Теперь тара это хранилище
/// (VesselDef.AbsorbOnContact = false), продукты в ней настоящие, и молоток
/// работает по ним поштучно.
///
/// СКОРОСТЬ НЕ ЗДЕСЬ. Кусочки разбиваются быстрее целой картофелины, но знает
/// об этом ГРАФ (FoodTransition.WorkMultiplier), а не молоток. Инструменту
/// незачем держать таблицу «что про какой продукт знаю» — иначе каждый новый
/// ингредиент правил бы этот скрипт.
///
/// СФЕРА, А НЕ ЛУЧ. У болгарки луч оправдан: там надо ВЕСТИ инструментом,
/// точность и есть игра. Здесь долбить надо тупо и часто, а целиться в
/// картофелину на дне ведра — мучение. Сфера прощает промах и накрывает
/// несколько продуктов разом, что для толчения и правильно.
///
/// СЕТЬ: владелец копит работу и шлёт 20 раз в секунду; сервер сам ищет цели
/// вокруг присланной точки и ограничивает объём работы сверху.
/// </summary>
public class JackhammerFoodPounder : NetworkBehaviour
{
    [Header("Что делаем")]
    [Tooltip("Процесс, который записывается в журнал. Группа Pressing, " +
             "инструмент Jackhammer. В графе формы продукта должно быть ребро " +
             "по этому процессу — иначе долбить будет нечего.")]
    [SerializeField] private ProcessDef _process;

    [Header("Область работы")]
    [Tooltip("Радиус сферы вокруг носика, метры. Щедро: долбить надо часто, " +
             "а не метко.")]
    [SerializeField] private float _workRadius = 0.35f;

    [Tooltip("Слои, на которых лежат продукты")]
    [SerializeField] private LayerMask _foodMask = ~0;

    [Tooltip("Сдвиг центра сферы вперёд от носика, метры")]
    [SerializeField] private float _forwardOffset = 0.05f;

    [Tooltip("Сколько продуктов обрабатывать одним ударом. Молоток бьёт " +
             "площадью, и накрыть три картофелины сразу — нормально.")]
    [Min(1)][SerializeField] private int _maxTargets = 6;

    [Header("Скорость")]
    [Tooltip("Долей прогресса в секунду по ОДНОМУ продукту при множителе 1. " +
             "Итоговая скорость умножается на WorkMultiplier ребра графа.")]
    [Min(0.01f)][SerializeField] private float _workPerSecond = 0.3f;

    [Tooltip("Потолок на присланную клиентом работу за пакет. Защита от " +
             "подкрученного клиента.")]
    [Min(0.01f)][SerializeField] private float _maxWorkPerPacket = 0.1f;

    [Tooltip("Сколько раз в секунду слать накопленное")]
    [Min(5f)][SerializeField] private float _sendRate = 20f;

    [Header("Качество")]
    [Tooltip("Качество на полной работе. Отбойник — инструмент грубый: " +
             "идеального пюре им не сделать, и это честно.")]
    [Range(0f, 1f)][SerializeField] private float _quality = 0.75f;

    [Header("Брызги")]
    [SerializeField] private int _splashPerBurst = 5;
    [SerializeField] private float _splashInterval = 0.09f;

    [Header("ОТЛАДКА (выключить в релизе)")]
    [SerializeField] private bool _debugHud = true;
    [SerializeField] private bool _debugGizmo = true;

    private JackhammerItem _tool;
    private Transform _probe;

    private float _pending;
    private float _nextSend;
    private float _nextSplash;

    private readonly Collider[] _hits = new Collider[24];
    private readonly List<FoodItem> _found = new List<FoodItem>();

    private string _status = "—";
    private Color _statusColor = Color.gray;
    private int _lastTargetCount;

    private float _nextShake;

    /// <summary>Центр рабочей сферы — носик молотка с небольшим выносом.</summary>
    public Vector3 WorkCenter =>
        _probe != null ? _probe.position + _probe.forward * _forwardOffset
                       : transform.position;

    private void Awake()
    {
        _tool = GetComponent<JackhammerItem>();

        if (_tool == null)
            Debug.LogError($"[Толчение] «{name}»: рядом нет JackhammerItem. " +
                           "Компонент вешается на корень молотка.", this);

        if (_process == null)
            Debug.LogError($"[Толчение] «{name}»: не назначен ProcessDef.", this);
        else if (_process.Group != ProcessGroup.Pressing)
            Debug.LogWarning($"[Толчение] «{name}»: у процесса «{_process.DisplayName}» " +
                             $"группа {_process.Group}, а ожидалась Pressing.", this);

        _probe = _tool != null ? _tool.ProbePoint : null;

        if (_probe == null)
            Debug.LogWarning($"[Толчение] «{name}»: у молотка не назначен Probe Point — " +
                             "работаю от центра инструмента, это неточно.", this);
    }

    private void Update()
    {
        if (base.IsOwner) OwnerTick();
    }

    // ───────────────────────── ВЛАДЕЛЕЦ ─────────────────────────

    private void OwnerTick()
    {
        if (_tool == null || !_tool.IsRunning)
        {
            _pending = 0f;
            _lastTargetCount = 0;
            SetStatus("зажми ЛКМ", Color.gray);
            return;
        }

        int n = CollectTargets();
        _lastTargetCount = n;

        if (n == 0)
        {
            _pending = 0f;
            SetStatus(DescribeMiss(), new Color(1f, 0.6f, 0f));
            return;
        }

        _pending += _workPerSecond * Mathf.Min(Time.deltaTime, 0.1f);

        FoodItem first = _found[0];
        SetStatus($"ТОЛЧЁТ {n} шт — «{first.name}» {first.Progress01 * 100f:0}%",
                  Color.green);

        Splash();
        ShakePaste();

        if (Time.time < _nextSend || _pending <= 0f) return;

        _nextSend = Time.time + 1f / Mathf.Max(5f, _sendRate);
        CmdPound(WorkCenter, _pending);
        _pending = 0f;
    }

    /// <summary>
    /// Продукты в сфере, которым этот процесс сейчас вообще что-то даёт.
    ///
    /// CanApplyNow отсеивает уже растолчённое и то, для чего в графе нет
    /// ребра: сырое яйцо под отбойником прогресса набирать не должно, даже
    /// если лежит в той же банке.
    /// </summary>
    private int CollectTargets()
    {
        _found.Clear();
        if (_process == null) return 0;

        int n = Physics.OverlapSphereNonAlloc(WorkCenter, _workRadius, _hits,
                                              _foodMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < n && _found.Count < _maxTargets; i++)
        {
            if (_hits[i] == null) continue;
            if (_hits[i].transform.IsChildOf(transform)) continue;   // сам молоток

            FoodItem f = _hits[i].GetComponentInParent<FoodItem>();
            if (f == null || !f.IsSpawned || f.Consumed) continue;
            if (_found.Contains(f)) continue;                        // составной коллайдер
            if (!f.CanApplyNow(_process)) continue;

            _found.Add(f);
        }
        return _found.Count;
    }

    /// <summary>
    /// Почему не толчём. Причины выглядят одинаково — «ничего не происходит», —
    /// поэтому разбираем явно.
    /// </summary>
    private string DescribeMiss()
    {
        int n = Physics.OverlapSphereNonAlloc(WorkCenter, _workRadius, _hits,
                                              ~0, QueryTriggerInteraction.Collide);

        FoodItem anyFood = null;
        CookingVessel anyVessel = null;

        for (int i = 0; i < n; i++)
        {
            if (_hits[i] == null || _hits[i].transform.IsChildOf(transform)) continue;

            if (anyFood == null)
            {
                FoodItem f = _hits[i].GetComponentInParent<FoodItem>();
                if (f != null && !f.Consumed) anyFood = f;
            }
            if (anyVessel == null)
                anyVessel = _hits[i].GetComponentInParent<CookingVessel>();
        }

        if (anyFood == null)
        {
            if (anyVessel != null && anyVessel.IsEmpty)
                return $"«{anyVessel.name}» пустая — положи продукт";
            return "поднеси носик к продукту";
        }

        int layer = anyFood.gameObject.layer;
        if (((1 << layer) & _foodMask.value) == 0)
            return $"«{anyFood.name}» на слое «{LayerMask.LayerToName(layer)}», " +
                   "а его нет в Food Mask";

        if (_process != null && !anyFood.CanApplyNow(_process))
            return $"«{anyFood.name}»: в графе формы нет перехода по " +
                   $"«{_process.DisplayName}» — добавь ребро в редакторе форм";

        return "поднеси носик к продукту";
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    /// <summary>
    /// Засчитать работу. Цели сервер ищет САМ вокруг присланной точки — верить
    /// клиентскому списку нельзя, иначе можно было бы растолочь склад одним
    /// пакетом.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdPound(Vector3 center, float work)
    {
        if (_process == null) return;

        work = Mathf.Clamp(work, 0f, _maxWorkPerPacket);
        if (work <= 0f) return;

        // Сфера чуть шире клиентской: за время полёта пакета молоток мог
        // сдвинуться, и на границе честные удары иначе терялись бы.
        int n = Physics.OverlapSphereNonAlloc(center, _workRadius * 1.25f, _hits,
                                              _foodMask, QueryTriggerInteraction.Collide);

        int done = 0;
        for (int i = 0; i < n && done < _maxTargets; i++)
        {
            if (_hits[i] == null) continue;
            if (_hits[i].transform.IsChildOf(transform)) continue;

            FoodItem f = _hits[i].GetComponentInParent<FoodItem>();
            if (f == null || !f.IsSpawned || f.Consumed) continue;

            // Прогресс копит сам продукт: он же знает своё состояние, своё
            // ребро в графе и его множитель скорости. Растворение в таре, если
            // состояние конечное, тоже делает он.
            if (f.ServerAddProgress(_process, work, _quality)) done++;
        }
    }

    // ───────────────────────── ВИЗУАЛ ─────────────────────────

    private void Splash()
    {
        if (Time.time < _nextSplash || _found.Count == 0) return;
        _nextSplash = Time.time + _splashInterval;

        FoodItem f = _found[Random.Range(0, _found.Count)];

        // Цвет ошмётка берём у самого продукта: у картошки летит светлое,
        // у свёклы бордовое. Одна система частиц на все овощи.
        Color c = f.Ingredient != null ? f.Ingredient.DebrisColor : Color.white;

        SparkBurst.Play(f.transform.position + Vector3.up * 0.04f, Vector3.up, 55f,
                        _splashPerBurst, 2.2f, c, c * 0.65f, 1f);
    }

    /// <summary>
    /// Толкнуть пасту в таре, по которой бьём.
    ///
    /// Ищем PasteVisual рядом с продуктами, а не держим ссылку: тара могла
    /// смениться, продукт могли переложить, а лишнее поле пришлось бы
    /// сбрасывать в трёх местах.
    ///
    /// Импульсы шлём с той же частотой, что и брызги: пружины и так
    /// интегрируются каждый кадр, а сто толчков в секунду просто загонят
    /// амплитуду в потолок.
    /// </summary>
    private void ShakePaste()
    {
        if (Time.time < _nextShake) return;
        _nextShake = Time.time + _splashInterval;

        int n = Physics.OverlapSphereNonAlloc(WorkCenter, _workRadius * 2f, _hits,
                                              ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            if (_hits[i] == null) continue;

            PasteVisual paste = _hits[i].GetComponentInParent<PasteVisual>();
            if (paste == null) continue;

            paste.AddImpulse(1f);
            return;                     // одной таре одного импульса хватит
        }
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void SetStatus(string text, Color color)
    {
        _status = text;
        _statusColor = color;
    }

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner) return;

        GUIStyle st = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14
        };
        st.normal.textColor = _statusColor;

        string proc = _process != null ? _process.DisplayName : "НЕТ ПРОЦЕССА";

        GUI.Box(new Rect(12f, 272f, 660f, 40f),
                $"Толчение → {_status}   [{proc}, радиус {_workRadius:0.00} м]", st);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_debugGizmo) return;

        Gizmos.color = _lastTargetCount > 0
            ? new Color(0.4f, 1f, 0.5f, 0.5f)
            : new Color(1f, 0.8f, 0.3f, 0.35f);

        Gizmos.DrawWireSphere(WorkCenter, _workRadius);
    }
}
