using CatCooks.Food;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ОТБОЙНИК В ТАРЕ — шаг рецепта «Измельчить» (острый соус: воду, чили, чеснок,
/// соль и уксус пробивают отбойником в однородную острую смесь).
///
/// Вешается на КОРЕНЬ отбойника рядом с JackhammerItem. Ничего больше не требует.
///
/// КАК ЗАСЧИТЫВАЕТСЯ
///   Зажал ЛКМ (молоток работает) и:
///     • носик (Probe Point молотка или Tip) внутри тары / у горловины — долбишь в таре;
///     • или (Aim Too) просто смотришь на тару ближе Aim Range — тоже долбишь.
///   Каждую секунду работы — секунда шага рецепта «Измельчить». Если сейчас в таре
///   не рецепт (или не этот шаг) — содержимое просто толчётся (процесс «Толочь»),
///   как и раньше.
///
/// ВИД (у всех): брызги цветом содержимого, круги и бурление поверхности.
///
/// СЕТЬ: владелец находит тару и шлёт секунды работы 10 раз в секунду, сервер
/// проверяет расстояние и сам двигает рецепт.
/// </summary>
public class JackhammerGrinder : NetworkBehaviour
{
    [Header("Носик")]
    [Tooltip("Кончик носика. Пусто — Probe Point у Jackhammer Item, а нет и его — корень")]
    [SerializeField] private Transform _tip;
    [Tooltip("Насколько носик может быть выше горловины и всё равно «в таре», м (умножается на размер тары)")]
    [SerializeField] private float _tipReach = 0.25f;

    [Header("Прицел (запасной способ)")]
    [Tooltip("Засчитывать, если просто смотришь на тару (не надо попадать носиком внутрь)")]
    [SerializeField] private bool _aimToo = true;
    [Tooltip("Дальность прицела от камеры, м")]
    [SerializeField] private float _aimRange = 3.5f;
    [SerializeField] private LayerMask _cookwareMask = ~0;

    [Header("Работа")]
    [Tooltip("Не шаг рецепта — толочь содержимое процессом «Толочь» (картошка → пюре), как раньше")]
    [SerializeField] private bool _mashContents = true;
    [Tooltip("Множитель скорости: 1 — секунда работы = секунда шага")]
    [Range(0.2f, 5f)][SerializeField] private float _speed = 1f;
    [Tooltip("Бурление поверхности при работе, 0..1")]
    [Range(0f, 1f)][SerializeField] private float _agitation = 0.9f;

    [Header("Вид")]
    [Tooltip("Брызг в секунду")]
    [SerializeField] private float _splashPerSecond = 14f;
    [Tooltip("Размер брызг (умножается на размер тары)")]
    [SerializeField] private float _splashSize = 0.03f;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud = true;
    [SerializeField] private bool _log = true;

    private readonly SyncVar<NetworkObject> _target = new SyncVar<NetworkObject>();

    private JackhammerItem _jack;
    private float _acc, _nextSend, _splashAcc, _serverIdle, _logAt;
    private Cookware _ownerCw;
    private string _status = "—";
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    private Transform Tip
    {
        get
        {
            if (_tip != null) return _tip;
            if (_jack != null && _jack.ProbePoint != null) return _jack.ProbePoint;
            return transform;
        }
    }

    private void Awake()
    {
        _jack = GetComponent<JackhammerItem>();
        if (_jack == null) _jack = GetComponentInParent<JackhammerItem>();
        if (_jack == null) Debug.LogError($"[Отбойник-тара] «{name}»: нет JackhammerItem рядом — повесь на корень отбойника", this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (base.IsOwner) OwnerTick(dt);
        if (base.IsServerInitialized) ServerTick(dt);
        UpdateFx(dt);
    }

    // ───────────────────────── ВЛАДЕЛЕЦ ─────────────────────────

    private void OwnerTick(float dt)
    {
        if (_jack == null || !_jack.IsRunning) { _acc = 0f; _ownerCw = null; _status = "зажми ЛКМ"; return; }

        Cookware cw = FindByTip();
        string how = "носик в таре";
        if (cw == null && _aimToo) { cw = FindByAim(); how = "прицел на тару"; }
        _ownerCw = cw;
        if (cw == null) { _acc = 0f; _status = "поднеси носик к таре (или наведись на неё)"; return; }

        int raw = CatCooks.Food.FoodRoastable.UnroastedPercentIn(cw);
        _status = CatCooks.Food.FoodRoastable.AnyRoastedIn(cw) ? $"толчёт какао-зёрна в «{Title(cw)}» ({how})"
                : raw >= 0 ? $"гроздь обжарена на {raw}% — дожарь (горелкой или ковш ≥120 °C), потом толки"
                : cw.IsEmpty ? $"«{Title(cw)}» пустая" : $"долбит «{Title(cw)}» ({how})";
        _acc += dt * _speed;
        if (Time.time < _nextSend) return;
        _nextSend = Time.time + 0.1f;
        CmdGrind(cw.NetworkObject, Mathf.Min(_acc, 0.3f));
        _acc = 0f;
    }

    private Cookware FindByTip()
    {
        Vector3 p = Tip.position;
        Cookware best = null;
        float bestD = float.MaxValue;
        foreach (Cookware cw in Cookware.All)
        {
            if (cw == null || cw.transform.IsChildOf(transform)) continue;
            if (cw.IsInside(p)) return cw;
            // У горловины: по горизонтали внутри полости, по высоте — не выше горловины + запас.
            Vector3 m = cw.MouthPoint;
            float size = SizeOf(cw);
            Vector2 d2 = new Vector2(p.x - m.x, p.z - m.z);
            if (d2.magnitude > size * 0.6f) continue;
            if (p.y > m.y + _tipReach * Mathf.Max(1f, size)) continue;
            if (p.y < m.y - size * 1.5f) continue;
            float d = d2.magnitude;
            if (d < bestD) { bestD = d; best = cw; }
        }
        return best;
    }

    private Cookware FindByAim()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;
        int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _hits, _aimRange,
                                        _cookwareMask, QueryTriggerInteraction.Collide);
        float best = float.MaxValue;
        Cookware res = null;
        for (int i = 0; i < n; i++)
        {
            if (_hits[i].transform.IsChildOf(transform)) continue;
            Cookware cw = _hits[i].collider.GetComponentInParent<Cookware>();
            if (cw == null || _hits[i].distance >= best) continue;
            best = _hits[i].distance;
            res = cw;
        }
        return res;
    }

    /// <summary>Характерный размер тары, м — по полости (Receive) или по мешам.</summary>
    private static float SizeOf(Cookware cw)
    {
        Collider c = cw.InsideVolume;
        if (c != null) { Vector3 s = c.bounds.size; return Mathf.Max(0.2f, Mathf.Max(s.x, s.z)); }
        Renderer r = cw.GetComponentInChildren<Renderer>();
        if (r != null) { Vector3 s = r.bounds.size; return Mathf.Max(0.2f, Mathf.Max(s.x, s.z)); }
        return 0.5f;
    }

    private static string Title(Cookware cw) => cw.Def != null ? cw.Def.Title : cw.name;

    // ───────────────────────── СЕРВЕР ─────────────────────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdGrind(NetworkObject nob, float seconds)
    {
        if (nob == null || _jack == null || !_jack.IsRunning) return;
        Cookware cw = CookwareLookup.Of(nob, Tip.position);
        if (cw == null) { if (_log) Debug.LogWarning($"[Отбойник-тара] на «{nob.name}» нет Cookware", nob); return; }
        float size = SizeOf(cw);
        float maxD = _aimRange + size * 2f + 3f;
        if ((cw.MouthPoint - transform.position).sqrMagnitude > maxD * maxD) return;
        seconds = Mathf.Clamp(seconds, 0f, 0.3f);
        if (seconds <= 0f) return;

        if (_target.Value != nob) _target.Value = nob;
        _serverIdle = 0f;

        // Обжаренная гроздь какао лежит в таре куском — толчём её в какао-порошок («Тёртое какао»).
        if (CatCooks.Food.FoodRoastable.ServerGrindIn(cw, seconds))
        {
            cw.ServerAgitate(_agitation);
            if (_log && Time.time >= _logAt) { _logAt = Time.time + 2f; Debug.Log($"[Отбойник-тара] «{Title(cw)}»: толчём обжаренные какао-зёрна в порошок", cw); }
            return;
        }
        if (cw.IsEmpty)
        {
            int raw = CatCooks.Food.FoodRoastable.UnroastedPercentIn(cw);
            if (_log && Time.time >= _logAt)
            {
                _logAt = Time.time + 2f;
                Debug.Log(raw >= 0 ? $"[Отбойник-тара] «{Title(cw)}»: гроздь обжарена только на {raw}% — отбойник толчёт обжаренную (100%). Дожарь горелкой или на огне (ковш ≥120 °C)"
                                   : $"[Отбойник-тара] «{Title(cw)}»: пусто — толочь нечего", cw);
            }
            return;
        }

        cw.ServerAgitate(_agitation);
        bool step = CookRecipeBook.ServerProcess(cw, CookStepKind.Grind, seconds);
        // Не «измельчить» — может, «перемешать» (шоколадная паста: какао + сахар + масло).
        if (!step) step = CookRecipeBook.ServerProcess(cw, CookStepKind.Mix, seconds);
        if (!step && _mashContents) cw.ServerTick(FoodProcess.Mash, seconds);
        if (_log && Time.time >= _logAt)
        {
            _logAt = Time.time + 2f;
            CookwareJournal j = CookwareJournal.Peek(cw);
            string rec = j != null && j.Recipe != null ? $"рецепт {j.Recipe.Title}, шаг {j.StepIndex + 1}" :
                         (j != null && !string.IsNullOrEmpty(j.Hint) ? "не по рецепту: " + j.Hint : "рецепта нет — просто толчём");
            Debug.Log($"[Отбойник-тара] «{Title(cw)}»: {(step ? "ИЗМЕЛЬЧЕНИЕ по рецепту" : "толчём")} — {rec}", cw);
        }
    }

    private void ServerTick(float dt)
    {
        if (_target.Value == null) return;
        _serverIdle += dt;
        if (_serverIdle > 0.4f || _jack == null || !_jack.IsRunning) _target.Value = null;
    }

    // ───────────────────────── ВИД ─────────────────────────

    private void UpdateFx(float dt)
    {
        NetworkObject nob = _target.Value;
        if (nob == null || _jack == null || !_jack.IsRunning) return;
        Cookware cw = CookwareLookup.Of(nob, Tip.position);
        if (cw == null || cw.IsEmpty) return;

        _splashAcc += dt * _splashPerSecond;
        if (_splashAcc < 1f) return;
        int n = Mathf.Min(4, Mathf.FloorToInt(_splashAcc));
        _splashAcc -= n;

        float size = SizeOf(cw);
        CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>();
        Vector3 tip = Tip.position;
        Vector3 at = new Vector3(tip.x, cw.MouthPoint.y, tip.z);
        Color c = new Color(0.8f, 0.3f, 0.15f);
        if (lv != null && lv.HasLiquid)
        {
            c = lv.LiquidColor;
            if (lv.TryGetSurfaceY(at, out float sy)) at.y = sy;
            lv.Ripple(at);
        }
        for (int i = 0; i < n; i++)
            LiquidFx.Splash(at + Random.insideUnitSphere * size * 0.08f, Vector3.up, c, 10,
                            _splashSize * Mathf.Max(1f, size / 0.5f), 2.2f);
    }

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner) return;
        var st = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        st.normal.textColor = _ownerCw != null ? Color.green : Color.gray;
        GUI.Box(new Rect(12f, 316f, 660f, 26f), $"Отбойник в таре → {_status}", st);
    }

    private void OnDrawGizmosSelected()
    {
        Transform t = _tip != null ? _tip : transform;
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(t.position, 0.05f);
    }
}
