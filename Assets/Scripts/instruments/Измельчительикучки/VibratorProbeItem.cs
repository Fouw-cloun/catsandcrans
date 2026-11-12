using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// РУЧНОЙ ВИБРАТОР — ЩУП. Берёшь в руку (F), он соединён кабелем с установкой.
    ///
    ///   • установка включена — щуп дрожит;
    ///   • держишь ЛКМ, наведясь на тару (ведро), — щуп опускается в неё;
    ///   • ВОДИШЬ МЫШЬЮ — взбиваешь (как ручной пилой: работа = движение мыши).
    ///     Стоишь неподвижно — ничего не происходит.
    ///
    /// Что происходит в таре — задают правила Churn Rules, по порядку:
    ///   Молоко → Сливки   (2 молока дают 1 сливки)
    ///   Сливки → Масло    (2 сливок дают 1 масло; начинается, когда сливок уже хватает)
    /// Сначала на молоке появляется слой сливок, потом со дна всплывают комки масла.
    /// Пока взбиваешь — пена, пузыри, дрожь поверхности (CookwareLiquidVisual).
    /// v3: щуп закручивает поверхность воронкой (CookwareLiquidVisual.Stir),
    /// сливки собираются вокруг щупа и расползаются шапкой; брызги — капли, не квадраты.
    ///
    /// v4: ЩУП ХОДИТ ТАМ, КУДА СМОТРИШЬ внутри ведра (а не стоит в центре), и ПРАВИЛО «ПО КРУГУ»:
    /// водишь щупом по кругу у стенок — взбивается быстро и ровно (качество сливок 100);
    /// тычешь в одно место или дёргаешь туда-сюда — медленнее и хуже (до 60). Качество — в журнале
    /// тары («оценка»), подсказка — там же. Эпицентр (горб бурления, капли, туча пены) рисует жидкость.
    ///
    /// Решает сервер. Кабель длиннее Cable Length у установки — щуп не работает.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
    /// Station — установка. Tip — кончик щупа (он опускается в тару).
    /// Cable Point — где крепится кабель (пусто — корень).
    /// </summary>
    public class VibratorProbeItem : PickupItem
    {
        public override bool UseHeldBlocking => false;     // v17.3: своя логика столкновений/позы
        [Serializable]
        public struct ChurnRule
        {
            [Tooltip("Что взбиваем")]
            public ProductDef From;
            [Tooltip("Что получается")]
            public ProductDef To;
            [Tooltip("Сколько порций From уходит на 1 порцию To")]
            [Min(1)] public int FromCount;
            [Tooltip("Сколько работы (движения мыши) на одну порцию To")]
            [Min(0.1f)] public float Work;
            [Tooltip("Начинать, только когда From в таре не меньше этого (сливки → масло: сначала пусть наберётся слой)")]
            [Min(0)] public int MinFrom;
            [Tooltip("Ждать, пока предыдущее правило закончится (всё молоко стало сливками — только потом масло)")]
            public bool AfterPrevious;
        }

        [Header("Вибрация")]
        [Tooltip("Держишь ЛКМ — щуп включён и дрожит, независимо от прицела")]
        [SerializeField] private Transform _tip;
        [Tooltip("Где к щупу крепится кабель от коробки. Пусто — корень.")]
        [SerializeField] private Transform _cablePoint;

        /// <summary>Точка крепления кабеля — к ней тянется кабель коробки.</summary>
        public Vector3 CablePoint => _cablePoint != null ? _cablePoint.position : transform.position;

        [Header("Взбивание")]
        [SerializeField] private ChurnRule[] _rules;
        [Tooltip("Работа в секунду, пока щуп в таре (держишь ЛКМ, наведён на ведро). " +
                 "При Work = 1 у правила — 1 порция в секунду.")]
        [SerializeField] private float _workPerSecond = 1f;
        [Tooltip("Всё ведро по правилу — за столько секунд взбивания, сколько бы порций ни было. " +
                 "0 — по Work каждого правила (порция за порцией).")]
        [SerializeField] private float _wholeBatchSeconds = 5f;
        [Tooltip("Сливки → масло (паста): всё ведро за столько секунд. Густеет медленнее, чем сбиваются сливки.")]
        [SerializeField] private float _thickenSeconds = 10f;
        [Tooltip("Добавка за движение мыши (водишь — быстрее). 0 — мышь не нужна.")]
        [SerializeField] private float _workScale = 0.5f;
        [Tooltip("Больше этого движения мыши за кадр не засчитывается (нельзя «рвануть» и сбить сразу)")]
        [SerializeField] private float _maxMousePerFrame = 1.5f;

        [Header("Взбивание по кругу (v4)")]
        [Tooltip("Щуп считается «у стенок», если дальше этой доли радиуса от центра")]
        [Range(0.1f, 0.85f)][SerializeField] private float _circleMinRadius = 0.42f;
        [Tooltip("Хорошая скорость обхода, оборотов в секунду (0.4 — круг за 2.5 с)")]
        [Range(0.1f, 2f)][SerializeField] private float _circleTurnsPerSecond = 0.4f;
        [Tooltip("Работа без кругового движения — доля от полной (0.45 — вдвое медленнее)")]
        [Range(0.1f, 1f)][SerializeField] private float _offCircleWork = 0.45f;
        [Tooltip("Качество, если совсем не водить по кругу (полностью по кругу — 100)")]
        [Range(0f, 100f)][SerializeField] private float _worstQuality = 60f;
        [Tooltip("Доля работы «по кругу», с которой качество уже 100")]
        [Range(0.3f, 1f)][SerializeField] private float _idealCircleShare = 0.8f;

        [Header("Прицел")]
        [SerializeField] private float _range = 2.4f;
        [SerializeField] private LayerMask _cookwareMask = ~0;

        [Header("Вид")]
        [Tooltip("Дрожь щупа, когда установка включена")]
        [Range(0f, 0.02f)][SerializeField] private float _vibration = 0.004f;
        [Tooltip("Насколько щуп опускается к горловине тары при фокусе, м")]
        [Range(0f, 1f)][SerializeField] private float _dip = 0.35f;
        [SerializeField] private float _dipSpeed = 8f;
        [Tooltip("v7: щуп не ездит в руке за прицелом, а НАКЛОНЯЕТСЯ кончиком к точке взбивания. Наибольший наклон, градусы")]
        [Range(0f, 60f)][SerializeField] private float _aimTilt = 35f;
        [Tooltip("v7: насколько далеко щуп может сдвинуться в руке к центру ведра, м (было «Dip» до 0.35 — инструмент летал у камеры)")]
        [Range(0f, 0.3f)][SerializeField] private float _maxDip = 0.12f;
        [Tooltip("v8: щуп входит в тару СВЕРХУ через горловину и кончиком погружается в жидкость — не проходит сквозь стенку. Выключи — наклон v7")]
        [SerializeField] private bool _immerse = true;
        [Tooltip("v8: на сколько кончик уходит под поверхность, м")]
        [Range(0f, 0.15f)][SerializeField] private float _sinkDepth = 0.04f;
        [Tooltip("v8: насколько круто щуп входит в горловину (0 — как держишь, 1 — отвесно)")]
        [Range(0f, 1f)][SerializeField] private float _steep = 0.55f;
        [SerializeField] private float _splashPerSecond = 40f;


        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        // ───────── сеть ─────────
        private readonly SyncVar<NetworkObject> _focus = new SyncVar<NetworkObject>();
        /// <summary>Сейчас реально взбивает (двигают мышью) — для брызг у всех.</summary>
        private readonly SyncVar<bool> _churning = new SyncVar<bool>(false);
        /// <summary>v4: где щуп в таре — доля радиуса в осях тары (x, z), видят все.</summary>
        private readonly SyncVar<Vector2> _stirN = new SyncVar<Vector2>(Vector2.zero);

        // v4: владелец — точка щупа; все — сглаженная; сервер — статистика кругов по таре
        private Vector2 _ownStir, _stirShown;
        private Vector3 _aimPoint, _mouthC, _mouthUp = Vector3.up;
        private float _mouthR = 0.15f, _immerseK;
        private bool _hasAim;
        private Quaternion _tiltQ = Quaternion.identity;
        private Cookware _aimCw;
        private float _aimR = 0.15f;
        private Vector2 _aimHalf = new Vector2(0.15f, 0.15f);       // v12.1: полуоси горловины (банка — эллипс)
        private Quaternion _aimRot = Quaternion.identity;
        private sealed class CircleStats
        {
            public float LastAng, Dir, Good, All, LastTime;
            public bool Has;
            public float HintAt;
        }
        private readonly Dictionary<Cookware, CircleStats> _circle = new Dictionary<Cookware, CircleStats>();

        // ───────── владелец ─────────
        private NetworkObject _lastFocus;
        private float _mouseAcc;
        private bool _lmbLogged;
        private float _sendTimer;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        // ───────── сервер ─────────
        private readonly Dictionary<Cookware, float[]> _work = new Dictionary<Cookware, float[]>();
        // Сколько было сырья, когда начали взбивать (для «всё ведро за N секунд»).
        private readonly Dictionary<Cookware, int[]> _batch = new Dictionary<Cookware, int[]>();
        private float _churnUntil;

        // ───────── вид ─────────
        private Vector3 _dipOffset;
        private ParticleSystem _splash;
        private NetworkObject _lvFor;
        private CookwareLiquidVisual _lv;
        private float _plopT;

        /// <summary>Держат ЛКМ — щуп включён (видят все).</summary>
        private readonly SyncVar<bool> _held2 = new SyncVar<bool>(false);
        public bool StationOn => _held2.Value;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
        }

        private void Update()
        {
            if (base.IsOwner) OwnerInput();
            if (base.IsServerInitialized && _churning.Value && Time.time > _churnUntil) _churning.Value = false;
            UpdateVisual();
        }

        // ───────────────────────── ВЛАДЕЛЕЦ ─────────────────────────

        private bool _lmbWas;

        private void OwnerInput()
        {
            bool lmb = Input.GetMouseButton(0);
            if (lmb != _lmbWas) { _lmbWas = lmb; CmdSetHeld(lmb); }

            NetworkObject focus = null;
            if (lmb)
            {
                Cookware cw = AimedCookware();
                if (cw != null) focus = cw.NetworkObject;
                if (cw != null) AimStir(cw);
            }
            if (focus != _lastFocus)
            {
                _lastFocus = focus;
                _mouseAcc = 0f;
                CmdFocus(focus);
            }

            if (lmb && !_lmbLogged)
            {
                _lmbLogged = true;
                if (_log) Debug.Log(focus != null
                    ? $"[Вибратор] щуп в «{focus.name}» — взбиваю"
                    : $"[Вибратор] ЛКМ, но луч не попал в тару (Range {_range} м, Cookware Mask). Наведись на ведро.", this);
            }
            if (!lmb) _lmbLogged = false;
            if (focus == null) return;
            // Работа: пока щуп в таре — само; водишь мышью — быстрее.
            float m = Mathf.Abs(Input.GetAxis("Mouse X")) + Mathf.Abs(Input.GetAxis("Mouse Y"));
            _mouseAcc += Time.deltaTime * _workPerSecond + Mathf.Min(m, _maxMousePerFrame) * _workScale;

            _sendTimer -= Time.deltaTime;
            if (_sendTimer <= 0f)
            {
                _sendTimer = 0.1f;
                if (_mouseAcc > 0.01f) CmdChurn(focus, _mouseAcc, _ownStir);
                _mouseAcc = 0f;
            }
        }

        /// <summary>
        /// v4: куда в таре смотрит прицел — точка на плоскости чуть ниже горловины (у поверхности),
        /// в долях радиуса (не дальше 0.85 — щуп не лезет в стенку).
        /// </summary>
        private void AimStir(Cookware cw)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            if (cw != _aimCw)
            {
                _aimCw = cw;
                CookwareLiquidVisual lv0 = cw.GetComponentInChildren<CookwareLiquidVisual>(true);
                _aimR = lv0 != null ? Mathf.Max(0.04f, lv0.RadiusAtRimWorld) : 0.15f;
            }
            StirFrame(cw, out _aimRot, out _aimHalf);
            _aimR = Mathf.Min(_aimHalf.x, _aimHalf.y);
            Vector3 mouth = cw.MouthPoint, up = _aimRot * Vector3.up;
            var plane = new Plane(up, mouth - up * Mathf.Min(0.12f, _aimR * 0.6f));
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!plane.Raycast(ray, out float e)) return;
            Vector3 l = Quaternion.Inverse(_aimRot) * (ray.GetPoint(e) - mouth);
            // v12.1: в долях полуосей — у вытянутой банки щуп ходит по эллипсу во всю ширину
            Vector2 n = Vector2.ClampMagnitude(new Vector2(l.x / _aimHalf.x, l.z / _aimHalf.y), 0.85f);
            _ownStir = Vector2.Lerp(_ownStir, n, 1f - Mathf.Exp(-Time.deltaTime * 20f));
        }

        /// <summary>
        /// v12.1: оси и полуоси горловины для щупа. Круглая тара — радиус жидкости; некруглая
        /// (банка «по форме модели», корыто) — настоящий размер по триггеру приёма Receive
        /// (его строит CookwareAutoSetup), иначе — размер жидкости.
        /// </summary>
        private void StirFrame(Cookware cw, out Quaternion rot, out Vector2 half)
        {
            rot = cw.transform.rotation;
            CookwareLiquidVisual lv = _lv != null && _lvFor == cw.NetworkObject ? _lv : cw.GetComponentInChildren<CookwareLiquidVisual>(true);
            if (lv != null && lv.Shape != CookwareLiquidVisual.Form.Box)
            {
                float R = Mathf.Max(0.04f, lv.RadiusAtRimWorld);
                half = new Vector2(R, R);
                return;
            }
            Transform rt = cw.transform.Find("Receive");
            BoxCollider bc = rt != null ? rt.GetComponent<BoxCollider>() : null;
            if (bc != null)
            {
                Vector3 ls = rt.lossyScale;
                half = new Vector2(Mathf.Max(0.04f, Mathf.Abs(bc.size.x * ls.x) * 0.5f), Mathf.Max(0.04f, Mathf.Abs(bc.size.z * ls.z) * 0.5f));
                rot = rt.rotation;
                return;
            }
            if (lv != null)
            {
                float s = lv.RadiusAtRimWorld / Mathf.Max(1e-4f, lv.RadiusAtRim);
                Vector2 h = lv.HalfSize * s;
                half = new Vector2(Mathf.Max(0.04f, h.x), Mathf.Max(0.04f, h.y));
                rot = lv.transform.rotation;
                return;
            }
            half = new Vector2(_aimR, _aimR);
        }

        private Cookware AimedCookware()
        {
            Camera cam = Camera.main;
            if (cam == null) return null;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, _cookwareMask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Cookware found = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                Cookware c = _hits[i].collider.GetComponentInParent<Cookware>();
                if (c == null) continue;
                best = _hits[i].distance; found = c;
            }
            return found;
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        [ServerRpc(RequireOwnership = true)]
        private void CmdFocus(NetworkObject target)
        {
            if (_focus.Value != target) _focus.Value = target;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdSetHeld(bool held)
        {
            if (_held2.Value != held) _held2.Value = held;
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            if (_focus.Value != null) _focus.Value = null;
            _churning.Value = false;
            _held2.Value = false;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdChurn(NetworkObject target, float mouse, Vector2 stir)
        {
            stir = Vector2.ClampMagnitude(stir, 0.9f);
            if ((_stirN.Value - stir).sqrMagnitude > 0.0004f) _stirN.Value = stir;
            if (target == null) { WarnOnce("сервер: пустая цель"); return; }
            if (!_held2.Value) { WarnOnce("сервер: щуп не включён (ЛКМ не дошла до сервера)"); return; }
            float far = (target.transform.position - transform.position).magnitude;
            if (far > _range + 2f) { WarnOnce($"сервер: тара дальше {_range + 2f:0.#} м ({far:0.#} м)"); return; }
            Cookware cw = CookwareLookup.Of(target, transform.position);
            if (cw == null) { WarnOnce($"сервер: на «{target.name}» нет Cookware (ни в корне, ни в детях)"); return; }

            // Не быстрее двойной скорости (мышью можно ускорить, но не «рвануть»).
            float work = Mathf.Min(mouse, 0.2f * _workPerSecond + 0.02f);
            // v4: по кругу — полная работа, иначе медленнее; копим долю «кругов» для качества.
            float circle = CircleFactor(cw, stir);
            work *= Mathf.Lerp(_offCircleWork, 1f, circle);
            cw.ServerAgitate(Mathf.Clamp01(0.6f + mouse * 2f));
            _churning.Value = true;
            _churnUntil = Time.time + 0.3f;

            // Вибратор — ТОЛЬКО ВЗБИВАНИЕ молочки (молоко → сливки → масло). Замес смесей
            // (мороженое и др.) — миксером. Посторонняя добавка в таре — не взбиваем.
            string foreign = ForeignInside(cw);
            if (foreign != null)
            {
                WarnOnce($"в «{cw.name}» есть «{foreign}» — вибратор только взбивает молоко и сливки. Смесь мешай миксером");
                return;
            }

            int n = _rules != null ? _rules.Length : 0;
            // Последний слот — «само»: Liquid → TopLayer из списка продуктов жидкости в таре.
            if (!_work.TryGetValue(cw, out float[] acc) || acc.Length != n + 1) { acc = new float[n + 1]; _work[cw] = acc; }
            if (!_batch.TryGetValue(cw, out int[] batch) || batch.Length != n + 1) { batch = new int[n + 1]; _batch[cw] = batch; }

            bool anyRule = false;
            for (int i = 0; i < n; i++)
            {
                ChurnRule r = _rules[i];
                if (r.From == null || r.To == null) continue;
                // «После предыдущего»: пока в таре есть сырьё прошлого правила — ждём.
                if (r.AfterPrevious && i > 0 && _rules[i - 1].From != null
                    && cw.CountOf(_rules[i - 1].From.Id) >= Mathf.Max(1, _rules[i - 1].FromCount)) continue;
                if (Step(cw, r.From, r.To, Mathf.Max(1, r.FromCount), r.MinFrom, r.Work, work, acc, batch, i, _wholeBatchSeconds)) anyRule = true;
            }

            if (anyRule) ApplyQuality(cw);
            if (!anyRule)
            {
                // Правила не подошли (или их нет) — берём пару из жидкости: молоко (Liquid) → сливки (TopLayer).
                CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>(true);
                if (lv != null && lv.TryGetChurnPair(cw, out ProductDef from, out ProductDef to, out bool thick))
                {
                    string key = "auto:" + from.Id;
                    if (_warned != key) { _warned = key; if (_log) Debug.Log($"[Вибратор] взбиваю по LiquidAnchor: {from.Title} → {to.Title}" + (thick ? " (густеет)" : ""), this); }
                    Step(cw, from, to, 1, 0, 1f, work, acc, batch, n, thick ? _thickenSeconds : _wholeBatchSeconds);
                    ApplyQuality(cw);
                    return;
                }
                WarnOnce($"в «{cw.name}» нечего взбивать: всё уже взбито, или правила ({n}) не подходят, " +
                         "а на LiquidAnchor нет пар Liquid → TopLayer → Paste");
            }
            else if (_warned == null || !_warned.StartsWith("auto")) _warned = null;
        }

        /// <summary>
        /// v4: насколько сейчас «по кругу» (0..1): щуп у стенок (дальше Circle Min Radius) и обходит
        /// ведро с хорошей скоростью в одну сторону. Копит статистику для качества.
        /// </summary>
        private float CircleFactor(Cookware cw, Vector2 stir)
        {
            if (!_circle.TryGetValue(cw, out CircleStats st)) { st = new CircleStats(); _circle[cw] = st; }
            float now = Time.time;
            if (st.Has && now - st.LastTime > 8f) { st.Good = st.All = 0f; st.Has = false; st.Dir = 0f; }  // перерыв — новая партия
            float dt = st.Has ? Mathf.Clamp(now - st.LastTime, 0.02f, 0.5f) : 0.1f;
            float r = stir.magnitude;
            float ang = Mathf.Atan2(stir.y, stir.x);
            float g = 0f;
            if (st.Has && r > 0.05f)
            {
                float d = Mathf.DeltaAngle(st.LastAng * Mathf.Rad2Deg, ang * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float turns = Mathf.Abs(d) / (Mathf.PI * 2f) / dt;
                st.Dir = Mathf.Lerp(st.Dir, Mathf.Sign(d), 0.25f);
                float rK = Mathf.Clamp01((r - _circleMinRadius * 0.6f) / Mathf.Max(0.01f, _circleMinRadius * 0.4f));
                float sK = Mathf.Clamp01(turns / Mathf.Max(0.05f, _circleTurnsPerSecond));
                float dirK = Mathf.Clamp01(Mathf.Abs(st.Dir) * 1.3f);                 // туда-сюда — не круг
                g = rK * sK * dirK;
            }
            st.LastAng = ang; st.LastTime = now; st.Has = true;
            st.Good += g * dt; st.All += dt;
            return g;
        }

        /// <summary>v4: качество взбивания → оценка в журнале тары; подсказка, если не водят по кругу.</summary>
        private void ApplyQuality(Cookware cw)
        {
            if (!_circle.TryGetValue(cw, out CircleStats st) || st.All < 0.3f) return;
            float share = st.Good / st.All;
            float q = Mathf.Lerp(_worstQuality, 100f, Mathf.Clamp01(share / Mathf.Max(0.05f, _idealCircleShare)));
            CookwareJournal j = CookwareJournal.Of(cw);
            j.Score = q;
            if (share < _idealCircleShare * 0.6f && st.All > 1.5f && Time.time >= st.HintAt)
            {
                st.HintAt = Time.time + 4f;
                j.Hint = $"Води щупом ПО КРУГУ у стенок ведра — так сливки взбиваются быстрее и ровнее (сейчас {q:0} из 100)";
                if (_log) Debug.Log($"[Вибратор] «{cw.name}»: по кругу {share * 100f:0}% работы → качество {q:0}", this);
            }
        }

        /// <summary>Одно правило: копим работу, превращаем порции. true — правило применимо.</summary>
        private bool Step(Cookware cw, ProductDef fromDef, ProductDef toDef, int need, int minFrom, float ruleWork,
                          float work, float[] acc, int[] batch, int i, float batchSeconds)
        {
            int have = cw.CountOf(fromDef.Id);
            if (have < need || have < minFrom) { batch[i] = 0; return false; }
            // Работа на порцию: своя (Work) или так, чтобы всё сырьё ушло за Whole Batch Seconds.
            if (have > batch[i]) batch[i] = have;                   // долили — партия больше
            float per = ruleWork;
            if (batchSeconds > 0f)
                per = batchSeconds * _workPerSecond / Mathf.Max(1f, batch[i] / (float)need);
            per = Mathf.Max(0.001f, per);
            acc[i] += work;
            int guard = 0;
            while (acc[i] >= per && cw.CountOf(fromDef.Id) >= need && guard++ < 64)
            {
                acc[i] -= per;
                int taken = cw.ServerTake(fromDef.Id, need);
                if (taken < need) break;
                cw.ServerAddContent(toDef.Id, toDef.StartState, 1, 0);
                CookRecipeBook.ReportNative(cw, CookDriver.Vibrator, fromDef, toDef,
                                            1f - cw.CountOf(fromDef.Id) / (float)Mathf.Max(1, batch[i]));
                if (cw.CountOf(fromDef.Id) < need)
                {
                    float q = QualityOf(cw);
                    CookRecipeBook.FinishNative(cw, CookDriver.Vibrator, fromDef, toDef,
                        $"{toDef.Title} ×{cw.CountOf(toDef.Id)}" + (q >= 0f ? $" · качество {q:0}" + (q < 85f ? " (водите щупом по кругу)" : "") : ""));
                    _circle.Remove(cw);
                }
                if (_log) Debug.Log($"[Вибратор] {fromDef.Title} ×{need} → {toDef.Title} · в таре {fromDef.Title}: " +
                                    $"{cw.CountOf(fromDef.Id)}, {toDef.Title}: {cw.CountOf(toDef.Id)}", cw);
            }
            return true;
        }

        private float QualityOf(Cookware cw)
        {
            if (!_circle.TryGetValue(cw, out CircleStats st) || st.All < 0.3f) return -1f;
            return Mathf.Lerp(_worstQuality, 100f, Mathf.Clamp01(st.Good / st.All / Mathf.Max(0.05f, _idealCircleShare)));
        }

        /// <summary>Что в таре, кроме молока, сливок и масла (иначе — не взбивать). null — только молочка.</summary>
        private static string ForeignInside(Cookware cw)
        {
            FoodDatabase db = UnityEngine.Object.FindAnyObjectByType<FoodDatabase>();
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count == 0) continue;
                ProductDef d = db != null ? db.Get(v.Product) : null;
                string t = d != null && !string.IsNullOrEmpty(d.Title) ? d.Title.ToLowerInvariant() : "";
                if (t.Contains("молок") || t.Contains("сливк") || t.Contains("масл")) continue;
                return d != null ? d.Title : "#" + v.Product;
            }
            return null;
        }

        private string _warned;
        private void WarnOnce(string msg)
        {
            if (!_log || _warned == msg) return;
            _warned = msg;
            Debug.Log("[Вибратор] ⚠ " + msg, this);
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void UpdateVisual()
        {
            NetworkObject f = _focus.Value;
            Vector3 want = Vector3.zero;
            // v4: щуп ходит туда, куда смотрит владелец (у остальных — по сети, сглажено)
            Vector2 sn = base.IsOwner ? _ownStir : _stirN.Value;
            _stirShown = Vector2.Lerp(_stirShown, sn, 1f - Mathf.Exp(-Time.deltaTime * 12f));
            _hasAim = false;
            if (f != null)
            {
                Cookware cw = CookwareLookup.Of(f, transform.position);
                Vector3 mouth = cw != null ? cw.MouthPoint : f.transform.position;
                if (cw != null)
                {
                    // v7: точка взбивания — сюда НАКЛОНЯЕТСЯ кончик (см. UpdateHeldPose); сам щуп
                    // лишь немного опускается к центру горловины и не мотается у камеры.
                    CookwareLiquidVisual lvA = _lv != null && _lvFor == f ? _lv : null;
                    Quaternion srot; Vector2 shalf;
                    StirFrame(cw, out srot, out shalf);                                 // v12.1: эллипс горловины
                    float R = Mathf.Min(shalf.x, shalf.y);
                    Vector3 p = mouth + srot * new Vector3(_stirShown.x * shalf.x, 0f, _stirShown.y * shalf.y);
                    if (lvA != null && lvA.TryGetSurfaceY(p, out float sy0))
                        p.y = _immerse ? Mathf.Min(mouth.y, sy0 - _sinkDepth) : Mathf.Lerp(mouth.y, sy0, 0.6f);
                    else if (_immerse) p -= cw.transform.up * Mathf.Min(0.12f, R * 0.5f);
                    _aimPoint = p;
                    _mouthC = mouth;
                    _mouthUp = srot * Vector3.up;
                    _mouthR = R;
                    _hasAim = true;
                }
                Vector3 from = _tip != null ? _tip.position - _dipOffset : transform.position - _dipOffset;
                Vector3 d = mouth - from;
                float len = d.magnitude;
                if (len > 0.001f && !(_immerse && _hasAim)) want = d / len * Mathf.Min(Mathf.Min(_dip, _maxDip), len);
            }
            _dipOffset = Vector3.Lerp(_dipOffset, want, 1f - Mathf.Exp(-_dipSpeed * Time.deltaTime));

            // Жидкость в таре, куда опущен щуп.
            if (f != _lvFor) { _lvFor = f; _lv = f != null ? f.GetComponentInChildren<CookwareLiquidVisual>() : null; }
            CookwareLiquidVisual lv = _lv != null && _lv.HasLiquid ? _lv : null;
            Vector3 tip = _tip != null ? _tip.position : transform.position;

            bool splash = _churning.Value && f != null;
            // Взбивают — воронка, закрутка сливок, пена. Просто включён в таре — лёгкая дрожь.
            if (lv != null && (splash || StationOn)) lv.Stir(tip, splash ? 1f : 0.3f);

            Vector3 at = tip;
            if (lv != null && lv.TryGetSurfaceY(tip, out float sy)) at.y = Mathf.Min(tip.y, sy + 0.005f);
            Color c0 = lv != null ? lv.LiquidColor : Color.white;
            // Паста не брызжет — только пучится (это рисует сама жидкость).
            bool wet = lv == null || lv.PasteAmount < 0.6f;
            if (splash && wet && _splash == null) _splash = LiquidFx.CreateStream(transform, 0.012f, 1.1f);
            if (_splash != null)
                CCRuntimeParticles.SetStream(_splash, splash && wet, at, Vector3.up, Color.Lerp(c0, Color.white, 0.3f), c0, _splashPerSecond);

            // Плюхи у щупа — вспышка и корона капель.
            _plopT -= Time.deltaTime;
            if (splash && wet && _plopT <= 0f)
            {
                _plopT = UnityEngine.Random.Range(0.12f, 0.25f);
                LiquidFx.ImpactSplash(at, Vector3.up, c0, UnityEngine.Random.Range(0.25f, 0.55f), 0.8f);
            }
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            // Базовая поза в руке — от PickupItem (его Hold Offset), к ней — наше движение.
            base.UpdateHeldPose(handAnchor);
            Vector3 pos = transform.position + _dipOffset;
            Quaternion rot = transform.rotation;

            // v7: кончик наклоняется к точке взбивания (поворот вокруг рукоятки, без сдвига)
            Quaternion tiltWant = Quaternion.identity;
            if (_hasAim && _focus.Value != null && _tip != null && _aimTilt > 0f)
            {
                Vector3 tipW = pos + (_tip.position - transform.position);
                Vector3 a = tipW - pos, b = _aimPoint - pos;
                if (a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f)
                {
                    Quaternion q = Quaternion.FromToRotation(a, b);
                    float ang = Quaternion.Angle(Quaternion.identity, q);
                    tiltWant = ang > _aimTilt ? Quaternion.Slerp(Quaternion.identity, q, _aimTilt / ang) : q;
                }
            }
            bool immersing = _immerse && _hasAim && _focus.Value != null && _tip != null;
            if (immersing) tiltWant = Quaternion.identity;
            _tiltQ = Quaternion.Slerp(_tiltQ, tiltWant, 1f - Mathf.Exp(-Time.deltaTime * 14f));
            rot = _tiltQ * rot;

            // v8: ПОГРУЖЕНИЕ СВЕРХУ. Кончик — в точку взбивания под поверхностью; ось щупа
            // проходит через горловину (точка входа — со стороны игрока, внутри 70% радиуса),
            // поэтому щуп не режет стенку и не «летает» — вход и погружение без конфликтов.
            _immerseK = Mathf.MoveTowards(_immerseK, immersing ? 1f : 0f, Time.deltaTime * 6f);
            if (_immerseK > 0.001f && _tip != null)
            {
                Vector3 tipOff = _tip.position - transform.position;            // от корня к кончику при базовой позе
                Vector3 up = _mouthUp.sqrMagnitude > 1e-6f ? _mouthUp.normalized : Vector3.up;
                Vector3 side = Vector3.ProjectOnPlane(pos - _mouthC, up);
                Vector3 entry = _mouthC + (side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.zero) * (_mouthR * 0.7f);
                Vector3 axis = entry - _aimPoint;                                // от кончика вверх к рукоятке
                if (axis.sqrMagnitude < 1e-6f) axis = up;
                axis.Normalize();
                axis = Vector3.Slerp(axis, up, _steep * 0.6f).normalized;        // круче — дальше от кромки
                if (Vector3.Dot(axis, up) < 0.35f) axis = (axis + up * (0.35f - Vector3.Dot(axis, up)) * 2f).normalized;
                if (tipOff.sqrMagnitude > 1e-6f)
                {
                    Quaternion q = Quaternion.FromToRotation(tipOff, -axis * tipOff.magnitude);
                    Quaternion rotI = q * rot;
                    Vector3 posI = _aimPoint - q * tipOff;
                    float k = Mathf.SmoothStep(0f, 1f, _immerseK);
                    pos = Vector3.Lerp(pos, posI, k);
                    rot = Quaternion.Slerp(rot, rotI, k);
                }
            }

            if (StationOn && _vibration > 0f)
            {
                float k = _churning.Value ? 1.8f : 1f;
                pos += UnityEngine.Random.insideUnitSphere * _vibration * k;
                rot *= Quaternion.Euler(UnityEngine.Random.insideUnitSphere * 2f * k);
            }
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
