using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// НАРЕЗКА ПРОДУКТОВ БЕНЗОПИЛОЙ.
/// Вешается на корень пилы, туда же, где ChainsawItem.
///
/// КАК ИГРАЕТСЯ: завёл пилу (ЛКМ), поднёс к продукту, подержал секунду —
/// кусок отвалился. Не отводя, держишь дальше — отваливается следующий.
/// Ни мыши, ни фокуса, ни замка: подошёл и рубишь.
///
/// ЧЕМ ОТЛИЧАЕТСЯ ОТ НОЖОВКИ. Там важна траектория мыши, поэтому нужен захват:
/// без него прицел уезжал бы и пропил срывался. Здесь важна только выдержка,
/// и весь механизм захвата оказывается лишним — вместе со всеми его поломками.
///
/// СОСТОЯНИЯ, КОТОРОЕ МОЖНО ПОТЕРЯТЬ, НЕТ. Владелец копит время, пока луч
/// упирается в продукт; отвёл — время обнулилось, и это ровно то, чего ждёшь.
/// Сервер получает готовую команду «режь».
///
/// РЕЖЕТ НЕ ТОЛЬКО ЕДУ. Цель — любой ICuttable: картошка (SliceableFood) или
/// бревно (CuttableLog). Второй процессор рядом поставить было нельзя — оба
/// тянули бы инструмент вперёд и оба рисовали бы щепу, перебивая друг друга.
/// Выдержку, зажим и цвет крошек теперь диктует ЦЕЛЬ: перепилить бревно и
/// разрезать картошку — работа очень разного объёма, а пила одна.
/// </summary>
public class ChainsawFoodProcessor : NetworkBehaviour
{
    [Header("Что делает инструмент")]
    [Tooltip("Обработка, которая пишется в журнал при отрезании куска")]
    [SerializeField] private ProcessDef _cutProcess;

    [Tooltip("Секунд удержания на продукте до отрезания одного куска")]
    [Min(0.1f)][SerializeField] private float _secondsPerCut = 1f;

    [Tooltip("Минимум секунд между кусками НА СЕРВЕРЕ. Чуть меньше выдержки — " +
             "просто страховка от накрутки.")]
    [SerializeField] private float _minSecondsPerCut = 0.6f;

    [Header("Прицел")]
    [Tooltip("Дальность луча от камеры, метры. Пила рубит вблизи.")]
    [SerializeField] private float _range = 1.8f;

    [Tooltip("Слой еды и брёвен — всего, что можно пилить")]
    [SerializeField] private LayerMask _foodMask = ~0;

    [Header("Вытягивание рук")]
    [Tooltip("На сколько пила подаётся вперёд к продукту, метры")]
    [SerializeField] private float _leanDistance = 0.28f;

    [SerializeField] private float _leanSpeed = 1.2f;

    [Tooltip("Мелкая дрожь работающей пилы у продукта, метры")]
    [SerializeField] private float _shake = 0.01f;

    [Header("Крошки")]
    [SerializeField] private bool _crumbsFromIngredient = true;
    [SerializeField] private float _crumbsPerSecond = 90f;
    [SerializeField] private float _crumbSize = 0.01f;
    [SerializeField] private float _crumbSpeed = 2.2f;

    [Header("ОТЛАДКА (выключить в релизе)")]
    [SerializeField] private bool _debugHud = true;

    // ───────── сеть ─────────
    /// <summary>Продукт, которого пила касается прямо сейчас. Нужен всем:
    /// по нему рисуются крошки, по нему же сервер понимает, что резать.</summary>
    private readonly SyncVar<NetworkObject> _contact = new SyncVar<NetworkObject>();

    private ISawTool _tool;
    private ParticleSystem _crumbs;
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    // ───────── владелец ─────────
    private ICuttable _target;
    private int _targetPart = -1;          // туша: какой кусок пилим (IPartCuttable)
    private RaycastHit _aimHit;
    private float _hold;           // сколько держим на цели
    private float _need = 1f;      // сколько надо держать именно эту цель
    private float _lean;

    // ───────── сервер ─────────
    private float _lastCutTime;

    private float _crumbCarry;
    private string _status = "—";
    private Color _statusColor = Color.gray;

    private void Awake()
    {
        _tool = GetComponent<ISawTool>();
        if (_tool == null)
            Debug.LogError($"[Бензопила] на {name} нет компонента с ISawTool. " +
                           "Допиши интерфейс в ChainsawItem.", this);
        CreateCrumbs();
    }

    private void OnDestroy()
    {
        if (_crumbs != null) Destroy(_crumbs.gameObject);
    }

    // ───────────────────────── ЦИКЛ ─────────────────────────

    private void Update()
    {
        // Сторож: пила заглохла или выпала, а касание висит — снимаем.
        // Зажим отпустить ОБЯЗАТЕЛЬНО, иначе бревно останется кинематическим
        // навсегда: висит в воздухе, не падает, не поднимается.
        if (base.IsServerInitialized && _contact.Value != null &&
            (_tool == null || !_tool.IsSawing))
        {
            ServerClamp(_contact.Value, false);
            _contact.Value = null;
        }

        if (base.IsOwner) OwnerTick();

        DrawCrumbs();
    }

    private void OwnerTick()
    {
        if (_tool == null) { SetStatus("нет ISawTool", Color.red); return; }
        if (_cutProcess == null) { SetStatus("не назначен Cut Process", Color.red); return; }

        if (!_tool.IsSawing)
        {
            Clear("пила не заведена");
            return;
        }

        Transform aim = _tool.AimOrigin;
        if (aim == null) { SetStatus("не найдена камера прицела", Color.red); return; }

        TargetTick(aim);
    }

    private void TargetTick(Transform aim)
    {
        ICuttable found = FindTarget(aim, out _aimHit);

        if (found == null)
        {
            Clear("поднеси пилу к цели");
            return;
        }

        // Сменили цель — выдержка начинается заново. Это честно: перенёс
        // пилу на другое бревно, значит и пилить его начал с нуля.
        if (!ReferenceEquals(found, _target) || !TargetAlive())
        {
            _target = found;
            _hold = 0f;

            // Время держит ЦЕЛЬ, а не инструмент: бревно ~2.5 сек, палено ~1.2,
            // картошка отдаёт ноль и получает общее время пилы.
            _need = found.SecondsPerCut > 0f ? found.SecondsPerCut : _secondsPerCut;
            _targetPart = -1;

            CmdSetContact(found.NetworkObject);
        }

        // Туша: пилится КОНКРЕТНЫЙ кусок под пилой и только по шву.
        if (_target is IPartCuttable pc)
        {
            int part = pc.PartAt(_aimHit.collider, _aimHit.point);
            if (!pc.CanCutPart(part, _aimHit.point, out string why))
            {
                _hold = 0f;
                SetStatus($"«{((Component)_target).name}»: {why}", Color.yellow);
                return;
            }
            if (part != _targetPart)
            {
                _targetPart = part;
                _hold = 0f;                      // перевёл пилу на другой кусок — с нуля
                CmdFocus(part);
            }
        }

        _hold += Time.deltaTime;

        if (_hold >= _need)
        {
            _hold -= _need;
            if (_target is IPartCuttable) CmdCutPart(_targetPart, _aimHit.point);
            else if (_target is CatCooks.Food.ICuttableAim) CmdCutAt(_aimHit.point);   // кусок мяса, рёбра: рез там, куда смотришь
            else CmdCut();
        }

        SetStatus($"РУБИТ «{((Component)_target).name}» — {_hold / _need * 100f:0}%   " +
                  $"{_target.CutLabel}", Color.green);
    }

    /// <summary>Отвели пилу — выдержка обнуляется. Копить нечего, и это ровно
    /// то поведение, которого ждёшь от «поднёс и держи».</summary>
    private void Clear(string status)
    {
        if (_target != null)
        {
            _target = null;
            _hold = 0f;
            CmdSetContact(null);
        }
        SetStatus(status, Color.gray);
    }

    /// <summary>
    /// Жива ли цель.
    ///
    /// ICuttable — интерфейс, а на интерфейсной ссылке НЕ РАБОТАЕТ подменённый
    /// Unity оператор сравнения с null: уничтоженный компонент через неё
    /// выглядит живым. Бревно деспавнится на последнем распиле, так что случай
    /// не редкий — приведение к Component возвращает нормальную проверку.
    /// </summary>
    private bool TargetAlive() => _target != null && (Component)_target != null;

    private ICuttable FindTarget(Transform aim, out RaycastHit bestHit)
    {
        ICuttable t = FindTargetMask(aim, _foodMask, out bestHit);
        if (t != null) return t;
        // еда не на слое Food (пласт бисквита, кубик) — ещё раз по всем слоям, только то, что можно пилить
        return FindTargetMask(aim, ~0, out bestHit);
    }

    private ICuttable FindTargetMask(Transform aim, int mask, out RaycastHit bestHit)
    {
        bestHit = default(RaycastHit);
        int n = Physics.RaycastNonAlloc(aim.position, aim.forward, _hits, _range,
                                        mask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        ICuttable best = null;

        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _hits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance >= nearest) continue;

            ICuttable s = h.collider.GetComponentInParent<ICuttable>();
            if (s == null || s.NetworkObject == null || !s.NetworkObject.IsSpawned) continue;
            if (!s.CanCut) continue;

            nearest = h.distance;
            best = s;
            bestHit = h;
        }
        return best;
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetContact(NetworkObject target)
    {
        // Прежнюю цель отпускаем ВСЕГДА и первым делом. Иначе бревно, с
        // которого просто отвели пилу, осталось бы кинематическим навсегда —
        // висело бы в воздухе и не поднималось.
        ServerClamp(_contact.Value, false);

        _contact.Value = target;

        ServerClamp(target, true);
    }

    /// <summary>Зажать или отпустить цель, если она сама этого просит.
    /// Картошка не просит: её и так держит рука.</summary>
    [Server]
    private void ServerClamp(NetworkObject target, bool value)
    {
        if (target == null) return;

        ICuttable c = target.GetComponent<ICuttable>();
        if (c == null || !c.ClampWhileCutting) return;

        c.ServerSetClamped(value);
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdFocus(int part)
    {
        NetworkObject t = _contact.Value;
        if (t == null) return;
        IPartCuttable pc = t.GetComponent<IPartCuttable>();
        if (pc != null) pc.ServerSetFocus(part);
    }

    /// <summary>Туша: отпилить кусок part, пилу держат в точке point (сервер перепроверяет шов).</summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdCutPart(int part, Vector3 point)
    {
        if (_cutProcess == null) return;
        NetworkObject t = _contact.Value;
        if (t == null) return;
        IPartCuttable pc = t.GetComponent<IPartCuttable>();
        if (pc == null) { CmdCutLocal(t); return; }
        if (Time.time - _lastCutTime < _minSecondsPerCut) return;
        if (!pc.CanCutPart(part, point, out string why))
        {
            Debug.Log($"[Бензопила/сервер] не режется: {why}");
            return;
        }
        _lastCutTime = Time.time;
        pc.ServerCutPart(part, point, _cutProcess, 1f);
        if (t == null || !t.IsSpawned) { _contact.Value = null; return; }
        if (!pc.CanCut) { ServerClamp(t, false); _contact.Value = null; }
    }

    [Server]
    private void CmdCutLocal(NetworkObject t)
    {
        ICuttable s = t.GetComponent<ICuttable>();
        if (s == null || !s.CanCut) return;
        if (Time.time - _lastCutTime < _minSecondsPerCut) return;
        _lastCutTime = Time.time;
        s.ServerCutOne(_cutProcess, 1f);
    }

    /// <summary>Кусок с прицелом (мясо, рёбра): сначала куда целится пила, потом рез.</summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdCutAt(Vector3 aim)
    {
        NetworkObject t = _contact.Value;
        if (t != null && t.GetComponent<ICuttable>() is CatCooks.Food.ICuttableAim a) a.ServerAimAt(aim);
        ServerDoCut();
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdCut() => ServerDoCut();

    [Server]
    private void ServerDoCut()
    {
        if (_cutProcess == null) return;

        NetworkObject t = _contact.Value;
        if (t == null) return;

        ICuttable s = t.GetComponent<ICuttable>();
        if (s == null || !s.CanCut)
        {
            ServerClamp(t, false);
            _contact.Value = null;
            return;
        }

        // Единственное ограничение: не чаще разумного.
        if (Time.time - _lastCutTime < _minSecondsPerCut) return;
        _lastCutTime = Time.time;

        s.ServerCutOne(_cutProcess, 1f);
        Debug.Log($"[Бензопила/сервер] отпилено, {s.CutLabel}");

        // ВАЖНО: после последнего распила объект деспавнится прямо внутри
        // ServerCutOne, и t уже мёртвый. Обращаться к s.CanCut на нём нельзя.
        if (t == null || !t.IsSpawned)
        {
            _contact.Value = null;
            return;
        }

        if (!s.CanCut)
        {
            ServerClamp(t, false);
            _contact.Value = null;
        }
    }

    // ───────────────────────── ВЫТЯГИВАНИЕ РУК ─────────────────────────

    /// <summary>
    /// Пила подаётся вперёд к продукту и мелко дрожит. Идёт в LateUpdate —
    /// ПОСЛЕ того, как PlayerHand выставил позу, иначе поза перезатёрла бы
    /// смещение.
    ///
    /// Только у владельца: остальным поза приезжает готовой через
    /// NetworkTransform, и повторное смещение применилось бы дважды.
    /// </summary>
    private void LateUpdate()
    {
        if (!base.IsOwner || _tool == null) return;

        Transform aim = _tool.AimOrigin;
        if (aim == null) return;

        bool reaching = TargetAlive() && _tool.IsSawing;
        _lean = Mathf.MoveTowards(_lean, reaching ? _leanDistance : 0f,
                                  _leanSpeed * Time.deltaTime);

        Vector3 offset = aim.forward * _lean;

        // Дрожь только когда реально вгрызаемся — на холостом ходу пила
        // трясётся своим скриптом, дублировать незачем.
        if (reaching && _shake > 0.0001f)
        {
            float t = Time.time * 40f;
            offset += new Vector3(Mathf.PerlinNoise(t, 0.1f) - 0.5f,
                                  Mathf.PerlinNoise(t, 5.3f) - 0.5f,
                                  Mathf.PerlinNoise(t, 9.7f) - 0.5f) * (2f * _shake);
        }

        if (offset.sqrMagnitude > 0.0000001f) transform.position += offset;
    }

    // ───────────────────────── КРОШКИ ─────────────────────────

    private void DrawCrumbs()
    {
        NetworkObject t = _contact.Value;
        if (t == null || _crumbs == null) return;

        ICuttable s = t.GetComponent<ICuttable>();
        if (s == null) return;
        // v13.4: цель рисует свои эффекты (какао-боб — объёмные ошмётки) — квадратные крошки не сыплем
        if (s is CatCooks.Food.ISawOwnFx) return;

        // Цвет диктует цель: у картошки он берётся с ингредиента, у бревна
        // задан на самом бревне. Пила про это ничего не знает.
        Color color = new Color(0.78f, 0.66f, 0.45f);
        if (_crumbsFromIngredient && s.CrumbColor.a > 0.01f) color = s.CrumbColor;

        _crumbCarry += _crumbsPerSecond * Time.deltaTime;
        int count = Mathf.FloorToInt(_crumbCarry);
        if (count <= 0) return;
        _crumbCarry -= count;
        if (count > 16) count = 16;

        Vector3 at = s.CutPoint();
        Vector3 away = _tool != null && _tool.Blade != null
            ? (at - _tool.Blade.position).normalized : Vector3.up;

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            ep.position = at + Random.insideUnitSphere * 0.025f;
            ep.startColor = color * Random.Range(0.7f, 1.15f);
            ep.startSize = _crumbSize * Random.Range(0.6f, 1.6f);
            ep.startLifetime = Random.Range(0.4f, 1.1f);

            // Бензопила выбрасывает щепу веером назад, а не роняет её вниз.
            Vector3 dir = (-away + Vector3.up * 0.5f + Random.insideUnitSphere * 0.8f)
                          .normalized;
            ep.velocity = dir * _crumbSpeed * Random.Range(0.5f, 1.3f);
            _crumbs.Emit(ep, 1);
        }
    }

    private void CreateCrumbs()
    {
        GameObject go = new GameObject("ChainsawCrumbs (auto)");

        // ВЫКЛЮЧЕННЫМ: система играет сразу при AddComponent, и часть свойств
        // на играющей менять нельзя.
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 2000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startSize = _crumbSize;
        main.startLifetime = 1f;
        main.gravityModifier = 1.2f;

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
        if (sh != null) r.material = new Material(sh) { name = "ChainsawCrumbs (auto)" };

        go.SetActive(true);
        ps.Play();
        _crumbs = ps;
    }

    /// <summary>Щепка — плоская пластинка. Объёмная, а не квад: квад пропадал
    /// бы, повернувшись ребром, а щепа кувыркается.</summary>
    private static Mesh BuildCrumbMesh()
    {
        Mesh m = new Mesh { name = "Chip" };
        const float x = 0.5f, y = 0.15f, z = 0.3f;
        Vector3[] c =
        {
            new Vector3(-x,-y,-z), new Vector3( x,-y,-z), new Vector3( x,-y, z), new Vector3(-x,-y, z),
            new Vector3(-x, y,-z), new Vector3( x, y,-z), new Vector3( x, y, z), new Vector3(-x, y, z)
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

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner) return;

        GUIStyle st = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14
        };
        st.normal.textColor = _statusColor;

        GUI.Box(new Rect(12f, 174f, 660f, 44f),
                $"Бензопила → {_status}\n" +
                $"выдержка {_hold:0.00} / {_need:0.00} сек   " +
                $"дальность {_range:0.0} м", st);
    }
}
