using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// НАРЕЗКА ПРОДУКТОВ ПИЛЯЩИМ ИНСТРУМЕНТОМ.
/// Вешается на корень ножовки, туда же, где HandsawItem.
///
/// КАК ИГРАЕТСЯ:
///   навёлся на продукт, зажал ЛКМ — камера встаёт, игрок останавливается,
///   продукт зажимается. Водишь мышью вперёд-назад: полотно ходит следом,
///   и на каждое полное движение отваливается кусок. Отпустил ЛКМ — свободен,
///   камера остаётся там, куда смотрел.
///   ПЛАСТ (бисквит): жёлтая рамка показывает кубик под прицелом; один кубик на
///   одно нажатие ЛКМ — отпилил, отпусти, наведись на следующий, пили снова.
///
/// ═══ ПОЧЕМУ ЭТО УСТРОЕНО ИМЕННО ТАК ═══
///
/// Прошлые версии копили состояние в трёх местах сразу: счётчик движений у
/// владельца, счётчик на сервере и захват с проверками дистанции. Каждое могло
/// сбиться независимо от других, и сбой одного выглядел как «пила не работает».
///
/// Здесь состояния, которое можно потерять, НЕТ ВООБЩЕ:
///   - владелец копит путь мыши и, набрав нужное, говорит «режь»;
///   - сервер получает готовую команду и просто режет;
///   - между командами ничего не хранится ни там, ни там.
///
/// Терять по дороге нечего — значит, и сбиваться нечему. Захват держится ровно
/// пока зажата ЛКМ: никаких срывов по дистанции, никаких перезахватов.
///
/// Работу считает клиент. Для кооперативной игры это верный размен: надёжность
/// важнее защиты от накрутки, а сервер всё равно проверяет, что резать можно
/// и что делается это не чаще разумного.
/// </summary>
public class SawFoodProcessor : NetworkBehaviour
{
    [Header("Что делает инструмент")]
    [Tooltip("Обработка, которая пишется в журнал при отрезании куска")]
    [SerializeField] private ProcessDef _cutProcess;

    [Tooltip("Сколько надо провести мышью В КАЖДУЮ СТОРОНУ на один кусок.\n\n" +
             "Кусок отваливается, только когда пройдено и вперёд, и назад. " +
             "Возить в одну сторону бесполезно, сколько ни води.\n\n" +
             "Смотри в отладке поля «вперёд» и «назад».")]
    [Min(0.5f)][SerializeField] private float _mousePerDirection = 8f;

    [Tooltip("Мёртвая зона на кадр: меньше этого не считается за движение")]
    [SerializeField] private float _mouseDeadzone = 0.005f;

    [Tooltip("Максимум за кадр: рывок мышью не должен давать фору")]
    [SerializeField] private float _mouseMaxStep = 1.5f;

    [Tooltip("Минимум секунд между кусками. Защита от накрутки и от того, " +
             "чтобы вся картошка не рассыпалась за один взмах.")]
    [SerializeField] private float _minSecondsPerCut = 0.4f;

    [Header("Прицел")]
    [Tooltip("Дальность луча от камеры, метры")]
    [SerializeField] private float _range = 2.5f;

    [Tooltip("Слой еды")]
    [SerializeField] private LayerMask _foodMask = ~0;

    [Header("Пласт (кубики)")]
    [Tooltip("Один кубик на одно нажатие ЛКМ: отпилил — отпусти ЛКМ, наведись на следующий и пили снова.\n" +
             "Выключи — будет пилить кубики подряд, не отпуская ЛКМ (старое поведение).")]
    [SerializeField] private bool _oneCutPerAim = true;
    [Tooltip("Рамка вокруг кубика, который отрежется, — пока целишься (ЛКМ не зажата). Во время пиления не видна. Только у тебя на экране.")]
    [SerializeField] private bool _showAimBox = true;
    [SerializeField] private Color _aimBoxColor = new Color(1f, 0.85f, 0.25f, 0.9f);
    [SerializeField] private float _aimBoxWidth = 0.006f;

    [Header("Вид пилы")]
    [Tooltip("На сколько пила тянется к продукту при захвате, метры")]
    [SerializeField] private float _leanDistance = 0.18f;

    [SerializeField] private float _leanSpeed = 0.8f;

    [Tooltip("Размах хода полотна вперёд-назад, метры")]
    [SerializeField] private float _bladeStroke = 0.05f;

    [Tooltip("Сколько провести мышью, чтобы полотно дошло от края до края")]
    [SerializeField] private float _sawRange = 3f;

    [Header("Крошки")]
    [SerializeField] private bool _crumbsFromIngredient = true;
    [SerializeField] private float _crumbsPerSecond = 45f;
    [SerializeField] private float _crumbSize = 0.008f;
    [SerializeField] private float _crumbSpeed = 1.4f;

    [Header("ОТЛАДКА (выключить в релизе)")]
    [SerializeField] private bool _debugHud = true;

    [Tooltip("Отрезать кусок принудительно этой клавишей")]
    [SerializeField] private KeyCode _forceCutKey = KeyCode.K;

    // ───────── сеть: на что наведена пила ─────────
    /// <summary>Зажатый продукт. Нужен всем: по нему рисуются крошки, и по
    /// нему же сервер понимает, что резать.</summary>
    private readonly SyncVar<NetworkObject> _focus = new SyncVar<NetworkObject>();

    private ISawTool _tool;
    private HandsawItem _hand;
    private bool _cutDone;                 // кубик уже отпилен за это нажатие — ждём отпускания ЛКМ
    private LineRenderer _box;
    private bool _boxWanted;
    private readonly Vector3[] _corners = new Vector3[8];
    private readonly Vector3[] _boxPts = new Vector3[16];
    private ParticleSystem _crumbs;
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    // ───────── владелец ─────────
    private ICuttable _target;          // старая еда (SliceableFood) или новая (FoodSliceable: пласт бисквита, картошка)

    /// <summary>Что режет эта пила на объекте: старый SliceableFood или новый FoodSliceable (только еда).</summary>
    private static ICuttable CuttableOn(Component c)
    {
        if (c == null) return null;
        SliceableFood old = c.GetComponentInParent<SliceableFood>();
        if (old != null) return old;
        CatCooks.Food.FoodSliceable nw = c.GetComponentInParent<CatCooks.Food.FoodSliceable>();
        if (nw != null) return nw;
        // любая другая еда, которую можно пилить (пласт бисквита FoodGridSlab и т.п.)
        return c.GetComponentInParent<ICuttable>();
    }

    private static string NameOf(ICuttable c) => c is Component k && k != null ? k.name : "?";
    private static bool Alive(ICuttable c) => c != null && c.NetworkObject != null && c.NetworkObject.IsSpawned;
    private float _fwd;            // путь мыши вперёд с прошлого куска
    private float _back;           // путь мыши назад
    private float _pos;            // положение полотна, только для картинки
    private float _lean;
    private float _lastRaw;

    // ───────── сервер ─────────
    private float _lastCutTime;

    private float _crumbCarry;
    private string _status = "—";
    private Color _statusColor = Color.gray;

    private void Awake()
    {
        _tool = GetComponent<ISawTool>();
        _hand = GetComponent<HandsawItem>();
        if (_tool == null)
            Debug.LogError($"[Пила] на {name} нет компонента с ISawTool. " +
                           "Добавь HandsawItem.", this);
        CreateCrumbs();
    }

    private void OnDestroy()
    {
        if (_crumbs != null) Destroy(_crumbs.gameObject);
        if (_box != null) Destroy(_box.gameObject);
    }

    // ───────────────────────── ЦИКЛ ─────────────────────────

    private void Update()
    {
        // Сторож: пила больше не пилит, а продукт всё ещё зажат — отпускаем.
        // Единственная защита, которая тут нужна: она чинит и обрыв связи,
        // и выпадение пилы из рук.
        if (base.IsServerInitialized && _focus.Value != null &&
            (_tool == null || !_tool.IsSawing))
            ServerRelease();

        _boxWanted = false;
        if (base.IsOwner) OwnerTick();
        ApplyBox();

        DrawCrumbs();
    }

    private void OwnerTick()
    {
        if (_tool == null) { SetStatus("нет ISawTool", Color.red); return; }
        if (_cutProcess == null) { SetStatus("не назначен Cut Process", Color.red); return; }

        // ── ЛКМ отпущена: полный сброс, никаких хвостов ──
        if (!_tool.IsSawing)
        {
            if (_target != null)
            {
                _target = null;
                _fwd = 0f;
                _back = 0f;
                _pos = 0f;
                CmdRelease();
            }
            _cutDone = false;
            // рамка: какой кубик отрежется, если зажать ЛКМ сейчас
            Transform a0 = ResolveAim();
            bool held = _hand != null ? _hand.IsHeld : _tool.AimOrigin != null;
            if (_showAimBox && held && a0 != null)
            {
                ICuttable look = FindTarget(a0);
                if (look != null) Box(look, _aimPoint);
            }
            SetStatus("наведись на продукт и зажми ЛКМ", Color.gray);
            return;
        }

        Transform aim = ResolveAim();
        if (aim == null) { SetStatus("не найдена камера прицела", Color.red); return; }

        // ── ищем цель, пока её нет ──
        if (_target == null)
        {
            _target = FindTarget(aim);
            if (_target == null)
            {
                SetStatus("под прицелом нет того, что можно резать",
                          new Color(1f, 0.6f, 0f));
                return;
            }

            _fwd = 0f;
            _back = 0f;
            _pos = 0f;
            CmdFocus(_target.NetworkObject, _aimPoint);
            Debug.Log($"[Пила] захват «{NameOf(_target)}»: {_target.CutLabel}");
        }

        // ── цель кончилась ──
        // ЗАМЕТЬ: проверок дистанции тут нет. Захват держится, пока зажата ЛКМ,
        // и точка. Раньше именно проверки дистанции срывали захват от одного
        // случайного кадра и роняли вместе с ним весь пропил.
        if (!Alive(_target) || !_target.CanCut)
        {
            SetStatus(Alive(_target)
                          ? "продукт распилен до конца"
                          : "продукт распилен",
                      new Color(0.4f, 0.7f, 1f));
            _target = null;
            _fwd = 0f;
            _back = 0f;
            CmdRelease();
            return;
        }

        // ── пласт: кубик за это нажатие уже отпилен — ждём, пока отпустят ЛКМ и наведутся заново ──
        if (_cutDone)
        {
            SetStatus($"кубик отрезан — отпусти ЛКМ, наведись на следующий (рамка) и пили снова. {_target.CutLabel}",
                      new Color(0.4f, 0.85f, 1f));
            return;
        }

        TrackAim(aim);                       // точка прицела на пласте — кубик там
        // рамка только пока целишься — во время пиления её нет
        if (Input.GetKeyDown(_forceCutKey)) Cut();

        // ── копим путь ОТДЕЛЬНО в каждую сторону ──
        //
        // Два независимых накопителя вместо распознавания разворотов. Кусок
        // отваливается, только когда оба заполнены, то есть игрок реально
        // провёл пилой туда и обратно. Возить в одну сторону бесполезно.
        //
        // Направление тут определять не нужно вовсе — а значит, и ошибиться
        // в нём невозможно. Именно на распознавании разворотов ломались
        // прошлые версии.
        float d = Input.GetAxis("Mouse Y");
        _lastRaw = d;
        _pos = Mathf.Clamp(_pos + d, -_sawRange, _sawRange);   // ход полотна

        float step = Mathf.Abs(d);
        if (step >= _mouseDeadzone)
        {
            step = Mathf.Min(step, _mouseMaxStep);
            if (d > 0f) _fwd += step;
            else _back += step;

            if (_fwd >= _mousePerDirection && _back >= _mousePerDirection)
            {
                _fwd = 0f;
                _back = 0f;
                Cut();
            }
        }

        SetStatus($"ПИЛИТ «{NameOf(_target)}» — {_target.CutLabel}",
                  Color.green);
    }

    /// <summary>Отрезать кусок там, куда смотришь. Пласт: один кубик на нажатие ЛКМ.</summary>
    private void Cut()
    {
        CmdCut(_aimPoint);
        if (_oneCutPerAim && _target is CatCooks.Food.ICuttableAim)
        {
            _cutDone = true;
            Debug.Log($"[Пила] «{NameOf(_target)}»: кубик отпилен — отпусти ЛКМ, наведись на следующий кубик и пили снова");
        }
    }

    // ───────────────────────── РАМКА ─────────────────────────

    /// <summary>Кубик, который отрежется (только у себя на экране).</summary>
    private void Box(ICuttable c, Vector3 point)
    {
        if (!(c is CatCooks.Food.ICuttableAim a) || !a.AimBox(point, _corners)) return;
        int[] order = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };   // все 12 рёбер одной линией
        for (int i = 0; i < order.Length; i++) _boxPts[i] = _corners[order[i]];
        _boxWanted = true;
    }

    private void ApplyBox()
    {
        if (!_boxWanted)
        {
            if (_box != null && _box.enabled) _box.enabled = false;
            return;
        }
        if (_box == null)
        {
            var go = new GameObject("SawAimBox (auto)");
            _box = go.AddComponent<LineRenderer>();
            _box.useWorldSpace = true;
            _box.loop = false;
            _box.positionCount = _boxPts.Length;
            _box.numCornerVertices = 2;
            _box.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _box.receiveShadows = false;
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (sh != null) _box.material = new Material(sh) { name = "SawAimBox (auto)" };
        }
        _box.startWidth = _box.endWidth = _aimBoxWidth;
        _box.startColor = _box.endColor = _aimBoxColor;
        _box.SetPositions(_boxPts);
        if (!_box.enabled) _box.enabled = true;
    }

    // ───────────────────────── ЦЕЛЬ ─────────────────────────

    /// <summary>Куда смотрит прицел на цели (мир) — пласт бисквита режет кубик именно там.</summary>
    private Vector3 _aimPoint;

    /// <summary>Пока пилишь — точка прицела на той же цели.</summary>
    private void TrackAim(Transform aim)
    {
        if (!(_target is CatCooks.Food.ICuttableAim)) return;
        if (!TrackAimMask(aim, _foodMask)) TrackAimMask(aim, ~0);   // еда не на слое Food — всё равно ведём прицел
    }

    private bool TrackAimMask(Transform aim, int mask)
    {
        bool any = false;
        int n = Physics.RaycastNonAlloc(aim.position, aim.forward, _hits, _range, mask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _hits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.distance >= nearest) continue;
            if (!ReferenceEquals(CuttableOn(h.collider), _target)) continue;
            nearest = h.distance;
            _aimPoint = h.point;
            any = true;
        }
        return any;
    }

    private ICuttable FindTarget(Transform aim)
    {
        ICuttable t = FindTargetMask(aim, _foodMask);
        if (t != null) return t;
        // не нашли на слое Food (у пласта/кубика другой слой) — ещё раз по всем слоям, но только еду
        t = FindTargetMask(aim, ~0);
        if (t != null && Time.time >= _layerWarnAt)
        {
            _layerWarnAt = Time.time + 10f;
            Debug.Log($"[Пила] «{NameOf(t)}» не на слое из Food Mask — режу всё равно; лучше поставь ему слой Food (с детьми)");
        }
        return t;
    }

    private float _layerWarnAt;

    private ICuttable FindTargetMask(Transform aim, int mask)
    {
        int n = Physics.RaycastNonAlloc(aim.position, aim.forward, _hits, _range,
                                        mask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        ICuttable best = null;

        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _hits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance >= nearest) continue;

            ICuttable s = CuttableOn(h.collider);
            if (s == null || !Alive(s) || !s.CanCut) continue;

            nearest = h.distance;
            best = s;
            _aimPoint = h.point;
        }
        return best;
    }

    /// <summary>Камера держателя — её отдаёт сама пила, полученную через руку.</summary>
    private Transform ResolveAim()
    {
        if (_tool != null && _tool.AimOrigin != null) return _tool.AimOrigin;
        Camera cam = Camera.main;
        return cam != null ? cam.transform : null;
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdFocus(NetworkObject target, Vector3 aim)
    {
        if (target == null) return;

        ICuttable s = CuttableOn(target);
        if (s == null || !s.CanCut)
        {
            Debug.LogWarning("[Пила/сервер] захват отклонён: резать нечего");
            return;
        }
        if (s is CatCooks.Food.ICuttableAim a) a.ServerAimAt(aim);

        _focus.Value = target;
        s.ServerSetClamped(true);
        Debug.Log($"[Пила/сервер] захват принят: «{target.name}», {s.CutLabel}");
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdRelease() => ServerRelease();

    /// <summary>
    /// Отрезать кусок. Вся команда — один вызов без параметров: клиент уже
    /// решил, что пора. Сервер проверяет только то, что действительно важно.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdCut(Vector3 aim)
    {
        if (_cutProcess == null) return;

        NetworkObject t = _focus.Value;
        if (t == null) { Debug.LogWarning("[Пила/сервер] режу, но цели нет"); return; }

        ICuttable s = CuttableOn(t);
        if (s == null || !s.CanCut) { ServerRelease(); return; }

        // Единственное ограничение: не чаще разумного. Иначе одним резким
        // взмахом можно было бы рассыпать всю картошку разом.
        if (Time.time - _lastCutTime < _minSecondsPerCut) return;
        _lastCutTime = Time.time;

        if (s is CatCooks.Food.ICuttableAim a) a.ServerAimAt(aim);   // пласт: кубик там, куда смотришь
        s.ServerCutOne(_cutProcess, 1f);

        // Дорезали до последнего — фокус больше не нужен.
        if (!s.CanCut) ServerRelease();
    }

    /// <summary>Снять зажим и забыть цель. Вызывается и по команде клиента,
    /// и сторожем — чтобы продукт не остался кинематическим навсегда.</summary>
    private void ServerRelease()
    {
        NetworkObject t = _focus.Value;
        if (t != null)
        {
            ICuttable s = CuttableOn(t);
            if (s != null) s.ServerSetClamped(false);
        }
        if (_focus.Value != null) _focus.Value = null;
    }

    // ───────────────────────── ВИД ПИЛЫ ─────────────────────────

    /// <summary>
    /// Подача вперёд и ход полотна. Идёт в LateUpdate — ПОСЛЕ того, как
    /// PlayerHand выставил позу, иначе поза перезатёрла бы смещение.
    ///
    /// Только у владельца: остальным поза приезжает готовой через
    /// NetworkTransform, и повторное смещение применилось бы дважды.
    /// </summary>
    private void LateUpdate()
    {
        if (!base.IsOwner) return;

        Transform aim = ResolveAim();
        if (aim == null) return;

        bool reaching = _target != null && _tool != null && _tool.IsSawing;
        _lean = Mathf.MoveTowards(_lean, reaching ? _leanDistance : 0f,
                                  _leanSpeed * Time.deltaTime);

        Vector3 offset = aim.forward * _lean;

        // Полотно едет ЗА РУКОЙ и упирается в края хода — по нему видно,
        // куда ведёшь. На счёт это не влияет, только на картинку.
        if (reaching && _bladeStroke > 0.0001f && _sawRange > 0.0001f)
            offset += aim.forward * (_pos / _sawRange * _bladeStroke);

        if (offset.sqrMagnitude > 0.0000001f) transform.position += offset;
    }

    // ───────────────────────── КРОШКИ ─────────────────────────

    /// <summary>Крошки у ВСЕХ клиентов по синхронизированной цели.</summary>
    private void DrawCrumbs()
    {
        NetworkObject t = _focus.Value;
        if (t == null || _crumbs == null) return;

        ICuttable s = CuttableOn(t);
        if (s == null) return;

        Color color = new Color(0.78f, 0.66f, 0.45f);
        Color own = s.CrumbColor;
        if (own.a > 0.01f) color = own;                       // новая еда: цвет крошек продукта (бисквит — шоколадные)
        FoodItem f = t.GetComponent<FoodItem>();
        if (_crumbsFromIngredient && f != null && f.Ingredient != null &&
            f.Ingredient.DebrisColor.a > 0.01f)
            color = f.Ingredient.DebrisColor;

        _crumbCarry += _crumbsPerSecond * Time.deltaTime;
        int count = Mathf.FloorToInt(_crumbCarry);
        if (count <= 0) return;
        _crumbCarry -= count;
        if (count > 12) count = 12;

        Vector3 at = s.CutPoint();
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            ep.position = at + Random.insideUnitSphere * 0.02f;
            ep.startColor = color * Random.Range(0.7f, 1.15f);
            ep.startSize = _crumbSize * Random.Range(0.6f, 1.6f);
            ep.startLifetime = Random.Range(0.5f, 1.2f);
            ep.velocity = (Vector3.down * 0.6f + Random.insideUnitSphere)
                          .normalized * _crumbSpeed * Random.Range(0.4f, 1f);
            _crumbs.Emit(ep, 1);
        }
    }

    private void CreateCrumbs()
    {
        GameObject go = new GameObject("SawCrumbs (auto)");

        // ВЫКЛЮЧЕННЫМ: система играет сразу при AddComponent, и часть свойств
        // на играющей менять нельзя.
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 1500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startSize = _crumbSize;
        main.startLifetime = 1f;
        main.gravityModifier = 1.1f;

        // При сцене далеко от нуля Automatic отключил бы систему целиком:
        // её трансформ вне экрана, хотя частицы рядом с игроком.
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = BuildCrumbMesh();
        r.alignment = ParticleSystemRenderSpace.World;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");
        if (sh != null) r.material = new Material(sh) { name = "SawCrumbs (auto)" };

        go.SetActive(true);
        ps.Play();
        _crumbs = ps;
    }

    /// <summary>Крошка — маленький кубик. Объёмная, а не плоская: плоская
    /// пропадала бы, повернувшись ребром.</summary>
    private static Mesh BuildCrumbMesh()
    {
        Mesh m = new Mesh { name = "Crumb" };
        const float s = 0.5f;
        Vector3[] c =
        {
            new Vector3(-s,-s,-s), new Vector3( s,-s,-s), new Vector3( s,-s, s), new Vector3(-s,-s, s),
            new Vector3(-s, s,-s), new Vector3( s, s,-s), new Vector3( s, s, s), new Vector3(-s, s, s)
        };
        int[] faces = { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4,
                        1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7 };

        Vector3[] verts = new Vector3[faces.Length];
        int[] tris = new int[faces.Length];
        for (int i = 0; i < faces.Length; i++) { verts[i] = c[faces[i]]; tris[i] = i; }

        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void SetStatus(string text, Color color)
    {
        _status = text;
        _statusColor = color;
    }

    /// <summary>Наглядная полоска хода пилы и накопленного пути.</summary>
    private string Bar()
    {
        const int W = 21;
        int mid = W / 2;
        float t = _sawRange > 0.0001f ? _pos / _sawRange : 0f;
        int at = Mathf.Clamp(Mathf.RoundToInt(mid + t * mid), 0, W - 1);

        char[] b = new char[W];
        for (int i = 0; i < W; i++) b[i] = '-';
        b[at] = '#';
        return "[" + new string(b) + "]";
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

        GUI.Box(new Rect(12f, 120f, 660f, 44f),
                $"Ножовка → {_status}\n" +
                $"пила {Bar()}   вперёд {_fwd:0.0}/{_mousePerDirection:0.0}   " +
                $"назад {_back:0.0}/{_mousePerDirection:0.0}   " +
                $"мышь {_lastRaw:+0.00;-0.00}", st);
    }
}
