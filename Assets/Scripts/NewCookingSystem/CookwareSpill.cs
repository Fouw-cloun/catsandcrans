using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ВЫПЛЁСКИВАНИЕ ЖИДКОСТИ ЧЕРЕЗ КРАЙ.
    ///
    /// ═══ ДВА РЕЖИМА (v3) ═══
    ///   • ЛЬЁМ — тару наклонили (ПКМ-переливание, кран, упала набок): жидкость
    ///     выше края даже без плеска. Ровная стабильная струя с нижней точки
    ///     кромки; сила — по высоте над краем (как из настоящего ведра: полное
    ///     льётся толще и дальше). Точка кромки сглажена — струя не скачет.
    ///   • ПЛЕЩЕТ — несёшь полное ведро и дёрнул: через край перелетает волна.
    ///     Никакой струи — яркие брызги и вспышки капель (их рисует
    ///     CookwareLiquidVisual сам, у каждого игрока). Порции при этом
    ///     теряются по-настоящему (сервер), капли падают лужей.
    ///
    /// Геометрию (уровень с сохранением объёма, наклон, плеск) считает
    /// CookwareLiquidVisual.ComputeOverflow — одна формула и для вида, и для
    /// сервера: что видишь, то и льётся.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// На корне тары рядом с Cookware. LiquidAnchor с Cookware Liquid Visual
    /// (его ставит Cookware Auto Setup). Puddle Prefab — префаб лужи.
    /// </summary>
    public sealed class CookwareSpill : NetworkBehaviour
    {
        [SerializeField] private CookwareLiquidVisual _liquid;
        [Tooltip("Префаб лужи (NetworkObject + LiquidPuddle)")]
        [SerializeField] private LiquidPuddle _puddlePrefab;

        [Header("Сколько льётся")]
        [Tooltip("Порций в секунду, когда жидкость выше края на всю высоту тары")]
        [SerializeField] private float _maxRate = 8f;
        [Tooltip("Меньше этого превышения над краем (доля высоты) — не льётся")]
        [Range(0f, 0.2f)][SerializeField] private float _threshold = 0.01f;
        [Tooltip("Плеск на ходу: во сколько раз медленнее теряются порции, чем при выливании")]
        [Range(0f, 1f)][SerializeField] private float _sloshLossScale = 0.35f;
        [Tooltip("Льётся струёй, только если тару наклонили сильнее этого, градусы. " +
                 "Качается в руках при ходьбе — только брызги, без потерь.")]
        [Range(0f, 90f)][SerializeField] private float _minPourTilt = 40f;
        [Header("Густое (сметана)")]
        [Tooltip("Сметана начинает сползать только при таком наклоне, градусы")]
        [Range(30f, 120f)][SerializeField] private float _thickPourTilt = 75f;
        [Tooltip("Насколько медленнее льётся сметана (доля от Max Rate)")]
        [Range(0.05f, 1f)][SerializeField] private float _thickRate = 0.22f;
        [Header("Опустошить до дна")]
        [Tooltip("При таком наклоне выливается/высыпается ВСЁ, что осталось на дне (остатки сметаны, кусочки) — " +
                 "тара очищается и годится заново")]
        [Range(45f, 150f)][SerializeField] private float _drainTilt = 70f;
        [Tooltip("Скорость опустошения остатков (доля от Max Rate)")]
        [Range(0.05f, 1f)][SerializeField] private float _drainRate = 0.6f;

        [Header("Струя")]
        [Tooltip("Скорость струи с края, м/с (полное ведро — до ×1.6)")]
        [SerializeField] private float _streamSpeed = 0.9f;
        [Tooltip("Толщина струи при полном выливании, м")]
        [SerializeField] private float _streamWidth = 0.06f;
        [Tooltip("Принудительный характер струи для ЛЮБОЙ жидкости в этой таре. Обычно ПУСТО: " +
                 "струя берёт вид того, что налито (вода — вода, молоко — молоко, бульон — бульон)")]
        [SerializeField] private LiquidPourProfile _pourProfile;
        [Tooltip("Насколько широким «языком» льётся с края при сильном наклоне (доля диаметра кромки)")]
        [Range(0f, 1.5f)][SerializeField] private float _lipSpread = 0.8f;
        [Tooltip("Тара, горловина которой ближе этого к месту падения, — ловит, м")]
        [SerializeField] private float _captureRadius = 0.5f;
        [Tooltip("Сглаживание точки кромки, с — больше = спокойнее струя")]
        [Range(0f, 0.3f)][SerializeField] private float _lipSmoothing = 0.07f;

        [Tooltip("Закрытая крышка (Pot Lid Socket) держит жидкость — не выплёскивается")]
        [SerializeField] private bool _lidHolds = true;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private PotLidSocket _lid;
        private CarriedContainer _carry;
        // итог выливания для консоли (вместо строки на каждую порцию)
        private int _sumN;
        private Cookware _sumTo;
        private bool _sumFloor;
        private readonly SyncVar<byte> _flow = new SyncVar<byte>(0);      // 0..255 — сила
        private readonly SyncVar<byte> _dirAngle = new SyncVar<byte>(0);  // сторона кромки в осях тары, 0..255 = 0..360°
        private readonly SyncVar<bool> _pouring = new SyncVar<bool>(false); // true — льём (струя), false — плеск (брызги)

        private Cookware _cw;
        private float _acc, _refreshT;
        private LiquidStreamRibbon _stream;
        private ThickOozeFx _ooze;
        private readonly RaycastHit[] _hits = new RaycastHit[12];

        // вид
        private float _flowS;
        private Vector3 _lipLocal, _outLocal = Vector3.forward;
        private bool _lipInit;

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _lid = GetComponent<PotLidSocket>();
            _carry = GetComponent<CarriedContainer>();
            if (_liquid == null) _liquid = GetComponentInChildren<CookwareLiquidVisual>(true);
        }

        private bool LidClosed => _lidHolds && _lid != null && _lid.HasLid;

        private float StreamSpeed(float over)
        {
            float h = _liquid != null ? Mathf.Max(0.02f, _liquid.RimHeight) : 0.25f;
            return _streamSpeed * Mathf.Lerp(0.55f, 1.6f, Mathf.Clamp01(over / (0.3f * h)));
        }

        /// <summary>
        /// Скорость вылета с кромки — ОДНА формула для струи у всех игроков и для
        /// сервера, который решает, куда попала порция. Раньше сервер считал без
        /// «прижатия» к горизонту, и струя визуально падала ближе, чем молоко.
        /// </summary>
        private Vector3 LipVelocity(Vector3 outward, float over)
        {
            Vector3 flat = Vector3.ProjectOnPlane(outward, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f) outward = Vector3.Lerp(outward, flat.normalized, 0.5f).normalized;
            return outward * StreamSpeed(over) + Vector3.down * 0.1f;
        }

        // ── ЧТО льётся: одинаково у сервера и у всех игроков ──
        // Содержимое тары (BagView) приходит всем по сети, поэтому каждый сам
        // находит верхнюю льющуюся жидкость — та же, что сервер снимает порцией.

        /// <summary>Верхняя жидкость, которая может литься (Жидкость или Слой сверху). 0 — нечему.</summary>
        private ushort PourProduct(out FoodState state)
        {
            state = FoodState.None;
            if (_cw == null || _liquid == null) return 0;
            for (int i = _cw.ViewCount - 1; i >= 0; i--)
            {
                BagView v = _cw.GetView(i);
                if (v.Count == 0) continue;
                if (_liquid.TryGetRole(v.Product, out CookwareLiquidVisual.Role role)
                    && role != CookwareLiquidVisual.Role.Floating && role != CookwareLiquidVisual.Role.Paste
                    && role != CookwareLiquidVisual.Role.IceCream)                    // сметана льётся медленно (см. Thick), мороженое — нет
                { state = v.State; return v.Product; }
            }
            return 0;
        }

        private ushort _lookId = ushort.MaxValue;
        private ProductDef _lookDef;

        private ProductDef DefOf(ushort id)
        {
            if (id == 0) return null;
            if (id != _lookId)
            {
                _lookId = id;
                FoodDatabase db = FoodDatabase.Instance;
                _lookDef = db != null ? db.Get(id) : null;
            }
            return _lookDef;
        }

        /// <summary>Цвет и характер струи — по тому, что налито.</summary>
        private void PourLook(out Color color, out LiquidPourProfile profile)
        {
            ProductDef def = DefOf(PourProduct(out _));
            color = def != null ? def.BaseColor : (_liquid != null ? _liquid.LiquidColor : Color.white);
            profile = _pourProfile != null ? _pourProfile : LiquidPourProfile.ForProduct(def, color);
        }

        /// <summary>
        /// Высыпать одну порцию не-жидкого через край. Летит по той же дуге, что и струя:
        /// попала в тару (ведро → бетономешалка, миска) — туда; нет — кучкой рядом, как раньше. Сервер.
        /// </summary>
        private bool DumpOne(Vector3 rim, Vector3 outward, float over)
        {
            int took = _cw.ServerTakeTop(1, out ushort p, out FoodState s, out byte c);
            if (took <= 0) return false;
            Vector3 vel = LipVelocity(outward, over) + (_liquid != null ? _liquid.ContainerVelocity * 0.85f : Vector3.zero);
            Vector3 land = Trace(rim + outward * 0.012f, vel, out Cookware direct);
            Cookware target = direct != null && direct != _cw ? direct : Nearest(land);
            if (target != null)
            {
                int put = target.ServerAddContent(p, s, took, c);
                if (put > 0)
                {
                    Tally(target);
                    if (put < took) _cw.ServerAddPinch(p, s, took - put, c, false);     // не влезло — обратно
                    if (_log && _cw.IsEmpty) Debug.Log($"[Выплеск] «{name}» высыпано до дна в «{Nm(target)}»", this);
                    return true;
                }
            }
            _cw.ServerSpillOver(p, s, took, c);
            if (_log && _cw.IsEmpty) Debug.Log($"[Выплеск] «{name}» опустошено до дна", this);
            return true;
        }

        /// <summary>Верхняя льющаяся — сметана (роль «Сметана»)?</summary>
        private bool TopIsThick()
        {
            ushort p = PourProduct(out _);
            return p != 0 && _liquid.TryGetRole(p, out CookwareLiquidVisual.Role r) && r == CookwareLiquidVisual.Role.Thick;
        }

        /// <summary>Лужа этой тары — для перелива через край (Cookware).</summary>
        public LiquidPuddle PuddlePrefab => _puddlePrefab;

        /// <summary>Профиль, которым сейчас льётся.</summary>
        public LiquidPourProfile CurrentProfile { get { PourLook(out _, out LiquidPourProfile p); return p; } }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void Update()
        {
            if (base.IsServerInitialized) ServerTick(Time.deltaTime);
            UpdateVisual(Time.deltaTime);
        }

        private void ServerTick(float dt)
        {
            if (_cw == null || _liquid == null) return;
            // На выделенном сервере вид не получает событий — уровень пересчитываем сами.
            _refreshT -= dt;
            if (_refreshT <= 0f) { _refreshT = 0.25f; _liquid.ForceRefresh(); }

            float h = Mathf.Max(0.01f, _liquid.RimHeight);
            float overS = _liquid.ComputeOverflow(false, out Vector3 rimS, out Vector3 outS);
            float overD = _liquid.ComputeOverflow(true, out Vector3 rimD, out Vector3 outD);
            if (LidClosed) { overS = -1f; overD = -1f; }
            if (!_liquid.CanPour) { overS = -1f; overD = -1f; }          // паста не льётся — только мастерком
            // Ведро черпает (ПКМ) — оно опущено в тару, обратно не выливается.
            bool scooping = _carry != null && _carry.CurrentAction == CarriedContainer.Action.Scoop;
            if (scooping) { overS = -1f; overD = -1f; }

            Transform tl = _liquid.transform;
            float tilt = Vector3.Angle(tl.up, Vector3.up);
            // Сверху сметана: при ходьбе не выплёскивается, а сползает только при сильном наклоне и медленно.
            bool thick = TopIsThick();
            float needTilt = thick ? Mathf.Max(_minPourTilt, _thickPourTilt) : _minPourTilt;
            bool pour = tilt >= needTilt && overS / h > _threshold;
            bool slosh = !thick && !pour && tilt >= _minPourTilt && overD / h > _threshold;
            // Остатки на дне: перелива уже нет, а тару держат сильно наклонённой — всё сползает/высыпается.
            bool drain = !pour && !scooping && !LidClosed && tilt >= Mathf.Max(_drainTilt, needTilt)
                         && overS / h <= _threshold && !_cw.IsEmpty;
            if (drain)
            {
                pour = true;
                slosh = false;
                overS = Mathf.Max(overS, 0.08f * h);
            }
            if (!pour && !slosh)
            {
                if (_flow.Value != 0) _flow.Value = 0;
                _acc = 0f;
                FlushLog();
                return;
            }

            float over = pour ? overS : overD;
            Vector3 rim = pour ? rimS : rimD, outward = pour ? outS : outD;
            float flow01 = Mathf.Clamp01(over / h);
            if (pour != _pouring.Value) _pouring.Value = pour;
            byte fb = (byte)Mathf.Clamp(Mathf.RoundToInt(flow01 * 255f), 1, 255);
            if (Mathf.Abs(fb - _flow.Value) > 6 || _flow.Value == 0) _flow.Value = fb;
            Vector3 ol = Quaternion.Inverse(transform.rotation) * outward;
            float ang = Mathf.Atan2(ol.z, ol.x) * Mathf.Rad2Deg;
            byte ab = (byte)Mathf.RoundToInt(Mathf.Repeat(ang, 360f) / 360f * 255f);
            if (ab != _dirAngle.Value) _dirAngle.Value = ab;

            float rate = drain ? _maxRate * _drainRate * (thick ? Mathf.Max(_thickRate, 0.3f) : 1f)
                               : _maxRate * flow01 * (pour ? 1f : _sloshLossScale) * (thick ? _thickRate : 1f);
            // Выливают из рук (ЛКМ) — полная тара уходит за Pour Time ведра (по умолчанию 2 с).
            if (_carry != null && _carry.CurrentAction == CarriedContainer.Action.Pour && pour)
                rate = Mathf.Max(rate, _carry.PourRate);
            _acc += dt * rate;
            while (_acc >= 1f)
            {
                _acc -= 1f;
                if (SpillOne(rim, outward, over)) continue;
                // Не жидкость (масло комком, кусочки, кучка) — высыпается через край кучкой.
                if (drain && DumpOne(rim, outward, over)) continue;
                _acc = 0f;
                break;
            }
        }

        /// <summary>Одна порция жидкости через край: в тару под струёй или лужей.</summary>
        private bool SpillOne(Vector3 rim, Vector3 outward, float over)
        {
            // Сперва верхний слой (сливки), потом основную — та же жидкость, что видна в струе.
            ushort product = PourProduct(out FoodState state);
            if (product == 0) return false;
            if (_cw.ServerTake(product, 1) <= 0) return false;

            // Как у струи-меша: вязкость замедляет вылет, скорость ведра наследуется.
            Vector3 vel = LipVelocity(outward, over) * CurrentProfile.SpeedScale + _liquid.ContainerVelocity * 0.85f;
            Vector3 land = Trace(rim + outward * 0.012f, vel, out Cookware direct);
            Cookware target = direct != null && direct != _cw ? direct : Nearest(land);
            if (target != null)
            {
                target.ServerAddContent(product, state, 1, 0);
                Tally(target);
                return true;
            }
            if (_puddlePrefab != null) LiquidPuddle.ServerAddAt(_puddlePrefab, product, land, 1);
            Tally(null);
            return true;
        }

        /// <summary>Считаем вылитое; в консоль — одной строкой, когда перестали лить или сменилась цель.</summary>
        private void Tally(Cookware to)
        {
            if (_sumN > 0 && (to != _sumTo || (to == null) != _sumFloor)) FlushLog();
            _sumTo = to; _sumFloor = to == null;
            _sumN++;
        }

        private static string Nm(Cookware c) =>
            c == null ? "?" : c.Def != null && !string.IsNullOrEmpty(c.Def.Title) ? $"{c.Def.Title}/{c.name}" : c.name;

        private void FlushLog()
        {
            if (_sumN <= 0) return;
            if (_log)
            {
                string mine = $"в «{Nm(_cw)}» осталось {_cw.TotalCount}/{_cw.Capacity} порц.";
                if (_sumTo != null)
                {
                    int cap = _sumTo.Def != null ? _sumTo.Def.Capacity : _sumTo.Capacity;
                    Debug.Log($"[Выплеск] «{Nm(_cw)}» → «{Nm(_sumTo)}»: {_sumN} порц. ({FoodUnits.Describe(_sumN, CookUnit.Bucket)}). " +
                              $"В «{Nm(_sumTo)}» теперь {_sumTo.TotalCount} порц. ({FoodUnits.Describe(_sumTo.TotalCount, CookUnit.Bucket)}), " +
                              $"вмещает {cap} ({FoodUnits.Describe(cap, CookUnit.Bucket)}); {mine}", this);
                }
                else Debug.Log($"[Выплеск] «{Nm(_cw)}» → пол: {_sumN} порц. ({FoodUnits.Describe(_sumN, CookUnit.Bucket)}); {mine}", this);
            }
            _sumN = 0; _sumTo = null; _sumFloor = false;
        }

        private Vector3 Trace(Vector3 pos, Vector3 vel, out Cookware direct)
        {
            direct = null;
            const float dt = 0.02f;
            for (int i = 0; i < 120; i++)
            {
                Vector3 next = pos + vel * dt + 0.5f * Physics.gravity * dt * dt;
                vel += Physics.gravity * dt;
                Vector3 seg = next - pos;
                float len = seg.magnitude;
                int n = len > 1e-5f ? Physics.RaycastNonAlloc(pos, seg / len, _hits, len, ~0, QueryTriggerInteraction.Collide) : 0;
                float bd = float.MaxValue; bool found = false; RaycastHit best = default;
                for (int j = 0; j < n; j++)
                {
                    RaycastHit h = _hits[j];
                    if (h.transform.IsChildOf(transform)) continue;
                    if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                    if (h.collider.isTrigger && h.collider.GetComponentInParent<Cookware>() == null) continue;
                    if (h.distance < bd) { bd = h.distance; best = h; found = true; }
                }
                if (found) { direct = best.collider.GetComponentInParent<Cookware>(); return best.point; }
                pos = next;
            }
            return pos;
        }

        private Cookware Nearest(Vector3 p)
        {
            Cookware best = null;
            float bd = _captureRadius;
            foreach (Cookware c in Cookware.All)
            {
                if (c == null || c == _cw) continue;
                Vector3 m = c.MouthPoint;
                if (p.y > m.y + 0.2f) continue;
                float d = new Vector2(m.x - p.x, m.z - p.z).magnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // ───────────────────────── ВИД (у всех) ─────────────────────────

        private void UpdateVisual(float dt)
        {
            float target = _pouring.Value && !LidClosed ? _flow.Value / 255f : 0f;
            // Сила струи сглажена: сеть шлёт ступеньками, струя течёт ровно.
            _flowS = Mathf.MoveTowards(_flowS, target, dt * (target > _flowS ? 3f : 2f));
            if (_flowS <= 0.001f && _stream == null) return;
            if (_stream == null) _stream = LiquidStreamRibbon.Create(transform);

            bool on = _flowS > 0.02f && target > 0f && _liquid != null;
            Vector3 rim = transform.position, outward = transform.forward;
            float over = 0f;
            if (on)
            {
                over = _liquid.ComputeOverflow(false, out Vector3 r, out Vector3 o);
                Vector3 rl = Quaternion.Inverse(transform.rotation) * (r - transform.position);
                Vector3 odl = Quaternion.Inverse(transform.rotation) * o;
                if (over <= 0f)
                {
                    // У этого клиента чуть иначе — берём сторону с сервера.
                    float ang = _dirAngle.Value / 255f * Mathf.PI * 2f;
                    odl = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    over = 0.02f * _liquid.RimHeight;
                }
                // Сглаживание в осях тары: кромка едет вместе с ведром без отставания,
                // но между соседними точками края переходит плавно.
                float k = _lipSmoothing > 0f ? 1f - Mathf.Exp(-dt / _lipSmoothing) : 1f;
                if (!_lipInit) { _lipLocal = rl; _outLocal = odl; _lipInit = true; }
                else
                {
                    _lipLocal = Vector3.Lerp(_lipLocal, rl, k);
                    _outLocal = Vector3.Slerp(_outLocal, odl, k);
                }
                rim = transform.position + transform.rotation * _lipLocal;
                outward = (transform.rotation * _outLocal).normalized;
            }
            else _lipInit = false;

            PourLook(out Color col, out LiquidPourProfile prof);
            Vector3 vel = LipVelocity(outward, over);
            float width = _streamWidth * (0.3f + 0.7f * Mathf.Sqrt(_flowS));
            // Слив широким краем: чем сильнее наклон (больше перелив), тем шире «простыня» с кромки.
            float rimR = _liquid != null ? _liquid.RadiusAtRimWorld : 0.15f;
            float strength = _liquid != null ? Mathf.Clamp01(over / Mathf.Max(0.01f, 0.25f * _liquid.RimHeight)) : 0f;
            float lipW = rimR * 2f * _lipSpread * strength * Mathf.Sqrt(_flowS);
            // Сметана (густое) не льётся струёй, а сползает «языком» и шлёпается кусками.
            bool thick = _liquid != null && TopIsThick();
            bool liquidTop = PourProduct(out _) != 0;          // высыпают кусочки/кучку — без струи
            if (thick || _ooze != null)
            {
                if (_ooze == null) _ooze = ThickOozeFx.Create(transform);
                float rimW = Mathf.Max(width * 1.6f, 0.05f);
                _ooze.Tick(on && thick, rim, outward, col, rimW, _flowS);
            }
            _stream.Profile = prof;
            _stream.SetLip(Vector3.Cross(Vector3.up, outward), Mathf.Max(width, lipW));
            _stream.Tick(on && !thick && liquidTop, rim + outward * 0.012f, vel, width, col, transform, 50f * _flowS);

            // Струя упала в тару — круги на её поверхности; на лужу — рябь.
            if (on && !thick && _stream.Landed)
            {
                Cookware c = Nearest(_stream.LandingPoint);
                CookwareLiquidVisual lv = c != null ? c.GetComponentInChildren<CookwareLiquidVisual>() : null;
                if (lv != null) lv.Ripple(_stream.LandingPoint);
                else LiquidPuddle.FindNear(_stream.LandingPoint)?.Ripple(_stream.LandingPoint);
            }
        }

        private void OnDestroy()
        {
            if (_stream != null) SafeDestroy.Kill(_stream.gameObject);
            if (_ooze != null) SafeDestroy.Kill(_ooze.gameObject);
        }
    }
}
