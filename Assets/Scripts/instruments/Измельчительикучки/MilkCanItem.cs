using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// БИДОН С МОЛОКОМ.
    ///
    /// Держишь ПКМ — бидон наклоняется, из носика льётся струя. Лить можно где
    /// угодно:
    ///   • струя падает в тару (или рядом с горловиной тары в пределах
    ///     Capture Radius) — молоко идёт в ближайшую тару;
    ///   • тары нет — на полу растёт лужа (LiquidPuddle): брызги, рябь, блик.
    ///
    /// Струя честно вылетает из носика по его синей оси (Z) со скоростью Pour
    /// Speed и падает по дуге. Куда упала — решает и сервер (молоко), и каждый
    /// игрок (брызги) одинаково.
    ///
    /// Если бидон наклоняется не в ту сторону — поменяй знак Tilt Angle или ось Tilt Axis.
    ///
    /// v2 (струи Pour v1): характер струи — LiquidPourProfile (пусто — молоко по цвету);
    /// горло бидона широкое — струя может идти «языком» шириной Neck Width; пока
    /// бидон доклоняется, струя нарастает, а не появляется сразу; пустой бидон не
    /// льёт и визуально. Сервер считает дугу той же скоростью, что и струя-меш.
    ///
    /// v9 — ДВА РЕЖИМА (переключение: клавиша Mode Key у держащего или галка Two Players):
    ///   • ОДИН ИГРОК: бидон тяжёлый (медленно идёшь, бидон запаздывает и раскачивается),
    ///     несёшь сбоку внизу — камеру не закрывает; ПКМ — наклон ВБОК (до 45°),
    ///     молоко льётся через край горла сбоку.
    ///   • ДВОЕ: первый берёт бидон (F), второй — F на бидон → берётся за вторую ручку;
    ///     несут вместе (бидон между ними). Льётся, только когда ОБА держат ПКМ —
    ///     бидон наклоняется вперёд. Второй отпускает — F на бидон ещё раз.
    ///   Струя тяжелее и цельнее (Stream Weight, Stream Steady), сервер считает падение так же.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody (масса 15–25), Collider.
    /// Spout — дочерний объект у носика, СИНЯЯ ось (Z) смотрит из носика наружу.
    /// </summary>
    public class MilkCanItem : PickupItem
    {
        public override bool UseHeldBlocking => false;     // v17.3: своя логика столкновений/позы
        [Header("Что льёт")]
        [SerializeField] private ProductDef _milk;
        [Min(1)][SerializeField] private int _capacity = 40;
        [Tooltip("Бесконечный — для тестов")]
        [SerializeField] private bool _infinite = true;
        [Tooltip("Порций в секунду")]
        [Min(0.1f)][SerializeField] private float _pourRate = 2f;

        [Header("Наклон")]
        [Tooltip("Вокруг какой оси бидона наклоняется (в осях бидона). Обычно X — красная.")]
        [SerializeField] private Vector3 _tiltAxis = Vector3.right;
        [Tooltip("Угол наклона, градусы. Льёт назад, на себя — поставь со знаком минус.")]
        [SerializeField] private float _tiltAngle = 65f;
        [Tooltip("Скорость наклона")]
        [SerializeField] private float _tiltSpeed = 3f;

        [Header("Куда льётся")]
        [Tooltip("Носик: СИНЯЯ ось (Z) смотрит из носика наружу")]
        [SerializeField] private Transform _spout;
        [Tooltip("Скорость струи из носика, м/с")]
        [SerializeField] private float _pourSpeed = 1.6f;
        [Tooltip("Тара, горловина которой ближе этого к месту падения струи, м, — ловит молоко")]
        [SerializeField] private float _captureRadius = 0.6f;
        [Tooltip("Обо что струя разбивается (пол, стол, тара)")]
        [SerializeField] private LayerMask _surfaceMask = ~0;
        [Tooltip("Префаб лужи (NetworkObject + LiquidPuddle). Пусто — на пол не льётся.")]
        [SerializeField] private LiquidPuddle _puddlePrefab;

        [Header("Вид")]
        [Tooltip("Цвет струи берётся из продукта (Base Color у Food_Молоко). Выключи — будет Milk Color ниже")]
        [SerializeField] private bool _colorFromProduct = true;
        [Tooltip("Свой цвет струи — только если Color From Product выключен")]
        [SerializeField] private Color _milkColor = new Color(0.97f, 0.97f, 0.94f);
        [Tooltip("Толщина струи у носика, м")]
        [SerializeField] private float _streamWidth = 0.05f;
        [Tooltip("v7: струя толще во столько раз (поверх Stream Width)")]
        [Range(0.5f, 4f)][SerializeField] private float _streamThick = 1.8f;
        [Tooltip("v7: струя расширяется к концу конусом, как из шланга: 0 — сужается как раньше, 1 — к концу вдвое толще")]
        [Range(0f, 2.5f)][SerializeField] private float _streamFlare = 1.1f;
        [Tooltip("v7: брызг-капель в секунду со струи (как у шланга)")]
        [Range(0f, 120f)][SerializeField] private float _streamSpray = 30f;
        [Tooltip("Брызг в секунду там, куда падает струя")]
        [SerializeField] private float _splashPerSecond = 50f;
        [Tooltip("Принудительный характер струи. Обычно ПУСТО — берётся по продукту (молоко → Milk)")]
        [SerializeField] private LiquidPourProfile _pourProfile;
        [Tooltip("Ширина «языка» с края горла, м. 0 — круглая струя. Бидон: 0.04–0.07")]
        [Range(0f, 0.2f)][SerializeField] private float _neckWidth = 0.05f;
        [Tooltip("С какой доли наклона (0..1) начинает литься. Выше — струя нарастает до полной")]
        [Range(0.2f, 0.95f)][SerializeField] private float _startTilt = 0.6f;


        [Header("v9: режимы")]
        [Tooltip("Вдвоём: второй игрок берётся за ручку (F на бидон), несут вместе, льют, когда ОБА держат ПКМ. Выкл — режим одного игрока")]
        [SerializeField] private bool _twoPlayers = false;
        [Tooltip("Клавиша переключения режима (у того, кто держит бидон)")]
        [SerializeField] private KeyCode _modeKey = KeyCode.Alpha7;
        [Tooltip("Подсказки на экране у держащего и у напарника")]
        [SerializeField] private bool _showHints = true;
        [Tooltip("Доворот модели, когда бидон несут (если модель повёрнута боком), градусы")]
        [SerializeField] private Vector3 _carryEuler = Vector3.zero;

        [Header("v9: один игрок — тяжёлый, льёт вбок")]
        [Tooltip("Скорость игрока с бидоном (1 — без штрафа)")]
        [Range(0.2f, 1f)][SerializeField] private float _soloSpeed = 0.55f;
        [Tooltip("Где бидон относительно руки (по курсу игрока): вправо, вниз, вперёд, м. Низко и сбоку — не закрывает камеру")]
        [SerializeField] private Vector3 _soloHoldOffset = new Vector3(0.45f, -0.75f, 0.55f);
        [Tooltip("Наклон вбок при наливании, градусы (не больше 45)")]
        [Range(10f, 45f)][SerializeField] private float _soloTiltAngle = 45f;
        [Tooltip("v9.1: наклонять К ЦЕНТРУ — перед собой, где видно (бидон справа → наклон влево). Выкл — наружу, от себя")]
        [SerializeField] private bool _soloPourInward = true;
        [Tooltip("Центр горла бидона (сверху). Пусто — берётся Spout")]
        [SerializeField] private Transform _neck;
        [Tooltip("Радиус горла, м — молоко льётся через край горла сбоку")]
        [Range(0.02f, 0.3f)][SerializeField] private float _neckRadius = 0.09f;
        [Tooltip("Скорость струи при наливании вбок (доля Pour Speed)")]
        [Range(0.3f, 1.5f)][SerializeField] private float _soloPourSpeed = 0.8f;
        [Tooltip("Тяжесть: насколько бидон запаздывает за рукой, с")]
        [Range(0.02f, 0.5f)][SerializeField] private float _heavyLag = 0.16f;
        [Tooltip("Тяжесть: раскачка от рывков, градусы")]
        [Range(0f, 30f)][SerializeField] private float _heavySway = 9f;

        [Tooltip("v9.2: бидон не подходит к камере ближе этого (м, от его края) — голова не окажется внутри")]
        [Range(0f, 1f)][SerializeField] private float _cameraClearance = 0.25f;
        [Tooltip("v12.2: бидон в руках не проходит сквозь тару (банку, ведро) и стены — выталкивается")]
        [SerializeField] private bool _pushOut = true;

        [Header("v9: вдвоём")]
        [Tooltip("Скорость, когда несут вдвоём")]
        [Range(0.2f, 1f)][SerializeField] private float _duoSpeed = 0.8f;
        [Tooltip("Скорость, если в режиме «вдвоём» несёт один (очень тяжело)")]
        [Range(0.1f, 1f)][SerializeField] private float _duoAloneSpeed = 0.35f;
        [Tooltip("Дальше этого напарник отпускает ручку, м")]
        [SerializeField] private float _duoMaxDistance = 2.6f;
        [Tooltip("Насколько бидон висит ниже рук, м")]
        [SerializeField] private float _duoHang = 0.3f;
        [Tooltip("Радиус, в котором напарник может схватить бидон (F), м")]
        [SerializeField] private float _grabRadius = 0.5f;

        [Header("v9: струя тяжелее")]
        [Tooltip("«Вес» струи — множитель тяжести (1 — как раньше). Сервер считает падение так же")]
        [Range(1f, 2.5f)][SerializeField] private float _streamWeight = 1.4f;
        [Tooltip("Цельность струи 0..1 — без дрожи, разрывов и «ломкости»")]
        [Range(0f, 1f)][SerializeField] private float _streamSteady = 0.75f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ushort> _left = new SyncVar<ushort>(0);
        private readonly SyncVar<bool> _pouring = new SyncVar<bool>(false);
        private readonly SyncVar<bool> _duo = new SyncVar<bool>(false);
        private readonly SyncVar<NetworkObject> _helper = new SyncVar<NetworkObject>(null);
        /// <summary>Кто держит ПКМ: 1 — несущий, 2 — напарник (для подсказки).</summary>
        private readonly SyncVar<byte> _wants = new SyncVar<byte>(0);

        // сервер
        private bool _ownerWants, _helperWants, _duoLast;
        private NetworkObject _holderServer;
        // владелец / напарник
        private int _heldFrame = -10;
        private bool _helperRmbWas;
        private FirstPersonController _fpc, _helperFpc;
        private Transform _fpcFor;
        private Vector3 _posS, _posVel, _velPrev, _swayAcc;
        private Quaternion _rotS = Quaternion.identity;
        private bool _poseInit;
        private SphereCollider _grab;
        private Vector3 _bodyCenterLocal;
        private float _bodyRadius, _bodyRad, _bodyHalfH;
        private readonly Collider[] _pushHits = new Collider[24];

        private float _pourAcc;
        private float _tilt;
        private bool _rmbWas;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        protected override void Awake()
        {
            base.Awake();
            LiquidPuddle.RegisterDefault(_puddlePrefab);              // v12.2: общий префаб лужи для тары без своего
            _canPickup = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _left.Value = (ushort)Mathf.Clamp(_capacity, 0, ushort.MaxValue);
            _duo.Value = _twoPlayers;
            _duoLast = _twoPlayers;
        }

        private void Start()
        {
            // v9: «ручка» для напарника — триггер (обычные коллайдеры в руке гаснут, луч F их не видит)
            GameObject g = new GameObject("CoCarryGrab (auto)") { layer = gameObject.layer, hideFlags = HideFlags.DontSave };
            g.transform.SetParent(transform, false);
            Bounds b = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            g.transform.position = any ? b.center : transform.position;
            if (any)
            {
                _bodyCenterLocal = transform.InverseTransformPoint(b.center);
                _bodyRadius = b.extents.magnitude;
                _bodyRad = Mathf.Max(b.extents.x, b.extents.z) * 0.9f;
                _bodyHalfH = b.extents.y;
            }
            _grab = g.AddComponent<SphereCollider>();
            _grab.isTrigger = true;
            _grab.radius = _grabRadius / Mathf.Max(1e-4f, transform.lossyScale.x);
            _grab.enabled = false;
        }

        /// <summary>Скорость игрока с бидоном — зависит от режима и напарника.</summary>
        public override float CarrySpeedMultiplier
            => !_duo.Value ? _soloSpeed : (_helper.Value != null ? _duoSpeed : _duoAloneSpeed);

        private bool HeldHere => Time.frameCount - _heldFrame <= 2;

        private void Update()
        {
            if (base.IsOwner) OwnerInput();
            HelperInput();
            _tilt = Mathf.MoveTowards(_tilt, _pouring.Value ? 1f : 0f, Time.deltaTime * _tiltSpeed);
            TrackSpoutVelocity();
            if (base.IsServerInitialized) { ServerModeAndHelper(); ServerPour(); }
            if (_grab != null) _grab.enabled = _duo.Value && _helper.Value == null && base.Owner != null && base.Owner.IsValid;
            UpdateVisual();
        }

        private void OwnerInput()
        {
            bool held = HeldHere;
            bool rmb = held && Input.GetMouseButton(1);
            if (rmb != _rmbWas) { _rmbWas = rmb; CmdPour(rmb); }
            if (held && _modeKey != KeyCode.None && Input.GetKeyDown(_modeKey)) CmdToggleMode();
        }

        /// <summary>Напарник (держит вторую ручку): свой ПКМ и своя скорость.</summary>
        private void HelperInput()
        {
            NetworkObject h = _helper.Value;
            bool meHelper = h != null && h.IsOwner;
            if (meHelper)
            {
                bool rmb = Input.GetMouseButton(1);
                if (rmb != _helperRmbWas) { _helperRmbWas = rmb; CmdHelperPour(rmb); }
                if (_helperFpc == null) _helperFpc = h.GetComponentInChildren<FirstPersonController>();
                if (_helperFpc != null) _helperFpc.SpeedMultiplier = _duoSpeed;
            }
            else
            {
                if (_helperFpc != null) { _helperFpc.SpeedMultiplier = 1f; _helperFpc = null; }
                _helperRmbWas = false;
            }
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdPour(bool on)
        {
            _ownerWants = on;
            RecomputePour();
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdHelperPour(bool on, FishNet.Connection.NetworkConnection conn = null)
        {
            NetworkObject h = _helper.Value;
            if (h == null || h.Owner != conn) return;
            _helperWants = on;
            RecomputePour();
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdToggleMode()
        {
            _twoPlayers = !_duo.Value;
            ApplyMode(_twoPlayers);
        }

        [Server]
        private void ApplyMode(bool duo)
        {
            _duoLast = duo;
            if (_duo.Value == duo) return;
            _duo.Value = duo;
            if (!duo) ReleaseHelper(null);
            RecomputePour();
            if (_log) Debug.Log(duo ? "[Бидон] режим: ВДВОЁМ — второй игрок жмёт F на бидон и берётся за ручку; лить — оба держат ПКМ"
                                    : "[Бидон] режим: ОДИН ИГРОК — тяжёлый, ПКМ — наклон вбок", this);
        }

        [Server]
        private void RecomputePour()
        {
            bool want = _duo.Value ? (_ownerWants && _helper.Value != null && _helperWants) : _ownerWants;
            byte w = (byte)((_ownerWants ? 1 : 0) | (_helperWants && _helper.Value != null ? 2 : 0));
            if (_wants.Value != w) _wants.Value = w;
            if (_pouring.Value != want) _pouring.Value = want;
            if (!want) _pourAcc = 0f;
        }

        [Server]
        private void ReleaseHelper(string why)
        {
            if (_helper.Value == null) return;
            if (_log && why != null) Debug.Log("[Бидон] напарник отпустил ручку: " + why, this);
            _helper.Value = null;
            _helperWants = false;
            RecomputePour();
        }

        /// <summary>Сервер: режим из инспектора (можно щёлкать прямо в Play) и проверка напарника.</summary>
        [Server]
        private void ServerModeAndHelper()
        {
            if (_twoPlayers != _duoLast) ApplyMode(_twoPlayers);
            NetworkObject h = _helper.Value;
            if (h == null) return;
            if (!h.IsSpawned) { ReleaseHelper("вышел"); return; }
            if (_holderServer == null) { ReleaseHelper("бидон поставили"); return; }
            if ((h.transform.position - transform.position).magnitude > _duoMaxDistance + 0.5f) { ReleaseHelper("слишком далеко"); return; }
            PlayerHand ph = h.GetComponentInChildren<PlayerHand>();
            if (ph != null && ph.HasItem) ReleaseHelper("взял другой предмет");
        }

        /// <summary>F на бидон: в режиме «вдвоём» второй игрок берётся за вторую ручку (или отпускает).</summary>
        public override void ServerInteract(PlayerInteractor interactor)
        {
            if (interactor == null) return;
            NetworkObject me = interactor.NetworkObject;
            if (_helper.Value != null && _helper.Value == me) { ReleaseHelper("сам"); return; }
            if (_duo.Value && _holderServer != null && _holderServer != me)
            {
                if (_helper.Value != null) return;                                   // обе ручки заняты
                if (interactor.Hand != null && interactor.Hand.HasItem) return;
                if ((me.transform.position - transform.position).magnitude > _duoMaxDistance) return;
                _helper.Value = me;
                RecomputePour();
                if (_log) Debug.Log("[Бидон] напарник взялся за вторую ручку — несёте вдвоём; лить — оба держат ПКМ", this);
                return;
            }
            if (_holderServer != null) return;                                       // уже в чужих руках
            base.ServerInteract(interactor);
        }

        public override void ServerOnPickup(PlayerHand hand)
        {
            base.ServerOnPickup(hand);
            _holderServer = hand != null ? hand.NetworkObject : null;
        }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            base.ServerOnDrop(hand, tossDirection, force);
            _holderServer = null;
            _ownerWants = false;
            ReleaseHelper("бидон поставили");
            RecomputePour();
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            _ownerWants = false;
            if (_pouring.Value) _pouring.Value = false;      // бросили — перестал лить
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void ServerPour()
        {
            if (!_pouring.Value || _milk == null || _tilt < _startTilt) return;
            if (!HasMilk) return;

            _pourAcc += Time.deltaTime * _pourRate * Flow01;
            while (_pourAcc >= 1f)
            {
                _pourAcc -= 1f;
                Deliver();
                if (!_infinite && _left.Value > 0) _left.Value = (ushort)(_left.Value - 1);
            }
        }

        private void Deliver()
        {
            if (!Trace(out Vector3 point, out Cookware direct)) return;
            Cookware cw = direct != null ? direct : NearestCookware(point);
            if (cw != null)
            {
                cw.ServerAddContent(_milk.Id, _milk.StartState, 1, 0);
                cw.ServerAgitate(0.35f);
                if (_log && cw.Def != null) Debug.Log($"[Бидон] +1 молоко в «{cw.Def.Title}»", this);
                return;
            }
            if (_puddlePrefab != null)
            {
                LiquidPuddle.ServerAddAt(_puddlePrefab, _milk.Id, point, 1);
                if (_log) Debug.Log("[Бидон] +1 молоко на пол", this);
            }
        }

        // ───────────────────────── КУДА ПАДАЕТ СТРУЯ ─────────────────────────

        /// <summary>Проследить дугу струи от носика. Попала в тару — вернуть её.</summary>
        // Скорость носика (игрок идёт с бидоном) — струя-меш её наследует, сервер тоже.
        private Vector3 _spoutLast, _spoutVel;
        private bool _spoutHas;

        private void TrackSpoutVelocity()
        {
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            PourOrigin(out Vector3 p, out Vector3 pd, out Vector3 pt);
            if (_spoutHas)
            {
                Vector3 v = (p - _spoutLast) / dt;
                if (v.sqrMagnitude > 100f) v = _spoutVel;                // телепорт
                _spoutVel = Vector3.Lerp(_spoutVel, v, 1f - Mathf.Exp(-dt / 0.05f));
            }
            _spoutLast = p; _spoutHas = true;
        }

        private bool HasMilk => _infinite || _left.Value > 0;

        /// <summary>0..1 — насколько бидон докланился после точки, где начинает литься.</summary>
        private float Flow01 => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_startTilt, 1f, _tilt) * 0.7f + 0.3f);

        /// <summary>Цвет струи: из продукта или свой.</summary>
        private Color StreamColor => _colorFromProduct && _milk != null ? _milk.BaseColor : _milkColor;

        /// <summary>Характер струи: принудительный или по продукту.</summary>
        private LiquidPourProfile Profile
            => _pourProfile != null ? _pourProfile : LiquidPourProfile.ForProduct(_milk, StreamColor);

        /// <summary>
        /// v9: откуда и куда льётся. Вдвоём — из носика по его синей оси (наклон вперёд);
        /// один — через край горла с той стороны, куда наклонён бидон (вбок).
        /// Считается по позе бидона — сервер (поза приходит по сети) и все игроки видят одно и то же.
        /// </summary>
        private void PourOrigin(out Vector3 pos, out Vector3 dir, out Vector3 tangent)
        {
            Transform s = _spout != null ? _spout : transform;
            if (_duo.Value) { pos = s.position; dir = s.forward; tangent = s.right; return; }
            Transform n = _neck != null ? _neck : s;
            Vector3 up = transform.up;
            Vector3 lean = Vector3.ProjectOnPlane(up, Vector3.up);
            Vector3 side = lean.sqrMagnitude > 1e-4f ? lean.normalized : transform.right * SoloSideSign;
            Vector3 lip = Vector3.ProjectOnPlane(side, up);
            lip = lip.sqrMagnitude > 1e-6f ? lip.normalized : side;
            pos = n.position + lip * _neckRadius;
            dir = (lip + Vector3.down * 0.15f).normalized;
            tangent = Vector3.Cross(up, lip).normalized;
        }

        /// <summary>Скорость вылета — та же, что получает струя-меш (с вязкостью).</summary>
        private Vector3 PourVelocity(Vector3 dir)
            => dir * (_pourSpeed * Mathf.Lerp(0.6f, 1f, Flow01) * (_duo.Value ? 1f : _soloPourSpeed));

        private bool Trace(out Vector3 point, out Cookware direct)
        {
            direct = null;
            PourOrigin(out Vector3 pos, out Vector3 dir, out Vector3 tangent);
            // как у струи-меша: вязкость, наследование скорости (тяжёлая — меньше), «вес»
            Vector3 vel = PourVelocity(dir) * Profile.SpeedScale + _spoutVel * Mathf.Lerp(0.85f, 0.45f, _streamSteady);
            Vector3 g = Physics.gravity * _streamWeight;
            const float dt = 0.02f;
            for (int i = 0; i < 150; i++)
            {
                Vector3 next = pos + vel * dt + 0.5f * g * dt * dt;
                vel += g * dt;
                Vector3 seg = next - pos;
                float len = seg.magnitude;
                if (len > 1e-5f && NearestHit(pos, seg / len, len, out RaycastHit h))
                {
                    point = h.point;
                    direct = h.collider.GetComponentInParent<Cookware>();
                    return true;
                }
                pos = next;
            }
            point = pos;
            return false;
        }

        private bool NearestHit(Vector3 from, Vector3 dir, float len, out RaycastHit best)
        {
            int n = Physics.RaycastNonAlloc(from, dir, _hits, len, _surfaceMask, QueryTriggerInteraction.Collide);
            best = default(RaycastHit);
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform)) continue;                            // сам бидон
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;   // игрок
                if (h.collider.isTrigger && h.collider.GetComponentInParent<Cookware>() == null) continue;
                if (h.collider.GetComponentInParent<LiquidPuddle>() != null) continue;
                if (h.distance >= bestD) continue;
                bestD = h.distance; best = h; found = true;
            }
            return found;
        }

        /// <summary>Ближайшая тара, горловина которой рядом с местом падения.</summary>
        private Cookware NearestCookware(Vector3 point)
        {
            Cookware best = null;
            float bestD = _captureRadius;
            foreach (Cookware c in Cookware.All)
            {
                if (c == null) continue;
                Vector3 m = c.MouthPoint;
                if (point.y > m.y + 0.2f) continue;                                // струя упала выше тары — мимо
                float d = new Vector2(m.x - point.x, m.z - point.z).magnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // ───────────────────────── ВИД (у всех) ─────────────────────────

        private LiquidStreamRibbon _ribbon;

        private void UpdateVisual()
        {
            bool on = _pouring.Value && _tilt >= _startTilt && HasMilk;
            if (on && _ribbon == null) _ribbon = LiquidStreamRibbon.Create(transform);
            if (_ribbon == null) return;

            PourOrigin(out Vector3 sp, out Vector3 sdir, out Vector3 stan);
            float f = on ? Flow01 : 0f;
            float width = _streamWidth * _streamThick * Mathf.Lerp(0.45f, 1f, f);
            // Широкое горло: «язык» с края, поперёк — касательная к краю горла.
            _ribbon.Profile = Profile;
            _ribbon.Flare = _streamFlare * f;
            _ribbon.SprayPerSecond = _streamSpray * f;
            _ribbon.GravityScale = _streamWeight;
            _ribbon.Steady = _streamSteady;
            if (_neckWidth > 0f) _ribbon.SetLip(stan, Mathf.Max(width, _neckWidth * f));
            // Струя-меш по той же дуге, что и расчёт сервера (вязкость ribbon учтёт сам).
            _ribbon.Tick(on, sp, PourVelocity(sdir), width, StreamColor, transform, _splashPerSecond * f);
            if (!on || !_ribbon.Landed) return;

            // Куда упала: круги на поверхности тары или на луже.
            Vector3 p = _ribbon.LandingPoint;
            Cookware c = NearestCookware(p);
            CookwareLiquidVisual lv = c != null ? c.GetComponentInChildren<CookwareLiquidVisual>() : null;
            if (lv != null) lv.Ripple(p);
            else
            {
                LiquidPuddle near = LiquidPuddle.FindNear(p);
                if (near != null) near.Ripple(p);
            }
        }

        private void OnDestroy()
        {
            if (_ribbon != null) Destroy(_ribbon.gameObject);
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            _heldFrame = Time.frameCount;
            if (_fpcFor != handAnchor) { _fpcFor = handAnchor; _fpc = handAnchor.GetComponentInParent<FirstPersonController>(); }
            if (_fpc != null) _fpc.SpeedMultiplier = CarrySpeedMultiplier;     // тяжесть — каждый кадр (режим/напарник меняются)

            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            Quaternion yaw = Quaternion.Euler(0f, handAnchor.eulerAngles.y, 0f);
            bool duo = _duo.Value;
            NetworkObject helper = _helper.Value;
            Vector3 pos;
            Quaternion rot;
            if (duo && helper != null)
            {
                // вдвоём: бидон между руками, ручки — по линии игрок↔напарник
                Vector3 a = handAnchor.position, b = HelperGrip(helper);
                Vector3 ab = b - a; ab.y = 0f;
                if (ab.sqrMagnitude < 1e-4f) ab = yaw * Vector3.right;
                ab.Normalize();
                pos = (a + b) * 0.5f + Vector3.down * _duoHang;
                rot = Quaternion.LookRotation(Vector3.Cross(ab, Vector3.up), Vector3.up) * Quaternion.Euler(_carryEuler);
            }
            else if (duo)
            {
                base.UpdateHeldPose(handAnchor);                                  // один в режиме «вдвоём» — как раньше
                pos = transform.position; rot = transform.rotation;
            }
            else
            {
                // один игрок: сбоку внизу, стоит ровно (не кивает за камерой) — не закрывает вид
                pos = handAnchor.position + yaw * _soloHoldOffset;
                rot = yaw * Quaternion.Euler(_carryEuler);
            }

            // тяжесть: запаздывает за рукой и раскачивается от рывков
            if (!_poseInit || (pos - _posS).sqrMagnitude > 4f)
            { _posS = pos; _posVel = Vector3.zero; _velPrev = Vector3.zero; _swayAcc = Vector3.zero; _rotS = rot; _poseInit = true; }
            Vector3 prev = _posS;
            _posS = Vector3.SmoothDamp(_posS, pos, ref _posVel, _heavyLag, Mathf.Infinity, dt);
            _rotS = Quaternion.Slerp(_rotS, rot, 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, _heavyLag)));
            Vector3 vel = (_posS - prev) / dt;
            Vector3 acc = Vector3.ClampMagnitude((vel - _velPrev) / dt, 20f);
            _velPrev = vel;
            _swayAcc = Vector3.Lerp(_swayAcc, acc, 1f - Mathf.Exp(-dt / 0.08f));
            Vector3 accH = Vector3.ProjectOnPlane(_swayAcc, Vector3.up);
            Quaternion sway = Quaternion.identity;
            if (accH.sqrMagnitude > 1e-4f && _heavySway > 0f)
            {
                float ang = Mathf.Min(_heavySway, accH.magnitude * _heavySway * 0.12f);
                sway = Quaternion.AngleAxis(-ang, Vector3.Cross(accH.normalized, Vector3.up));
            }
            Quaternion r = sway * _rotS;

            // наливание
            float tk = Mathf.SmoothStep(0f, 1f, _tilt);
            if (!duo)
            {
                // вбок (от игрока), не больше 45°
                Vector3 side = yaw * (Vector3.right * SoloSideSign);
                float a = Mathf.Min(_soloTiltAngle, 45f) * tk * Mathf.Deg2Rad;
                Vector3 tiltedUp = Vector3.up * Mathf.Cos(a) + side * Mathf.Sin(a);
                r = Quaternion.FromToRotation(Vector3.up, tiltedUp) * r;
            }
            else if (tk > 0.001f)
            {
                Vector3 axis = _tiltAxis.sqrMagnitude > 1e-6f ? _tiltAxis.normalized : Vector3.right;
                r *= Quaternion.AngleAxis(_tiltAngle * tk, axis);
            }
            // v12.2: бидон не проходит сквозь тару и стены — выталкивается из коллайдеров
            // (у предмета в руке свои коллайдеры выключены, поэтому считаем сами — двумя шарами по высоте)
            if (_bodyRadius > 0f && _pushOut) PushOutOfColliders(r);

            // v9.2: камера не залезает внутрь — бидон отодвигается от камеры (вниз и в сторону, не вверх)
            Camera cam = Camera.main;
            if (cam != null && _bodyRadius > 0f && _cameraClearance > 0f)
            {
                Vector3 center = _posS + r * Vector3.Scale(_bodyCenterLocal, transform.lossyScale);
                Vector3 d = center - cam.transform.position;
                float need = _bodyRadius * 0.85f + _cameraClearance;
                float dist = d.magnitude;
                if (dist < need)
                {
                    Vector3 away = d;
                    if (away.y > 0f) away.y = 0f;
                    if (away.sqrMagnitude < 1e-6f) away = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                    if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
                    _posS += away.normalized * (need - dist);
                }
            }
            transform.SetPositionAndRotation(_posS, r);
        }

        /// <summary>В какую сторону (по курсу) наклонять: +1 вправо, −1 влево. К центру — против того боку, где несёшь.</summary>
        private float SoloSideSign
        {
            get
            {
                float holdSide = _soloHoldOffset.x >= 0f ? 1f : -1f;
                return _soloPourInward ? -holdSide : holdSide;
            }
        }

        /// <summary>v12.2: вытолкнуть бидон (позу _posS) из чужих коллайдеров — два шара: низ и верх корпуса.</summary>
        private void PushOutOfColliders(Quaternion r)
        {
            Vector3 up = r * Vector3.up;
            float rad = Mathf.Max(0.05f, _bodyRad);
            float off = Mathf.Max(0f, _bodyHalfH - rad);
            for (int it = 0; it < 2; it++)
            {
                Vector3 center = _posS + r * Vector3.Scale(_bodyCenterLocal, transform.lossyScale);
                Vector3 push = Vector3.zero;
                for (int k = -1; k <= 1; k += 2)
                {
                    Vector3 c = center + up * (off * k);
                    int n = Physics.OverlapSphereNonAlloc(c, rad, _pushHits, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                    {
                        Collider col = _pushHits[i];
                        if (col == null || col.transform.IsChildOf(transform)) continue;                 // сам бидон
                        if (col is CharacterController || col.GetComponentInParent<PlayerInteractor>() != null) continue;
                        Rigidbody rb = col.attachedRigidbody;
                        if (rb != null && !rb.isKinematic && rb.mass < 5f) continue;                 // мелочь не мешает
                        MeshCollider mc = col as MeshCollider;
                        if (mc != null && !mc.convex) continue;                                      // ClosestPoint не умеет
                        Vector3 cp = col.ClosestPoint(c);
                        Vector3 d = c - cp;
                        float dist = d.magnitude;
                        if (dist >= rad) continue;
                        Vector3 dir = dist > 1e-4f ? d / dist : (c - col.bounds.center).normalized;
                        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
                        Vector3 p = dir * (rad - dist);
                        // копим по максимуму вдоль каждой оси (без двойного счёта)
                        push.x = Mathf.Abs(p.x) > Mathf.Abs(push.x) ? p.x : push.x;
                        push.y = Mathf.Abs(p.y) > Mathf.Abs(push.y) ? p.y : push.y;
                        push.z = Mathf.Abs(p.z) > Mathf.Abs(push.z) ? p.z : push.z;
                    }
                }
                if (push.sqrMagnitude < 1e-8f) break;
                _posS += push;
            }
        }

        /// <summary>Где рука напарника (у удалённых игроков — примерно у груди).</summary>
        private static Vector3 HelperGrip(NetworkObject helper)
        {
            PlayerHand ph = helper.GetComponentInChildren<PlayerHand>();
            if (ph != null && ph.HandAnchor != null) return ph.HandAnchor.position;
            return helper.transform.position + Vector3.up * 1.1f;
        }

        // ───────────────────────── ПОДСКАЗКИ ─────────────────────────

        private static GUIStyle _hintStyle;

        private void OnGUI()
        {
            if (!_showHints) return;
            string t = null;
            NetworkObject h = _helper.Value;
            bool meHelper = h != null && h.IsOwner;
            if (base.IsOwner && HeldHere)
            {
                string key = _modeKey != KeyCode.None ? _modeKey.ToString().Replace("Alpha", "") : "—";
                if (!_duo.Value)
                    t = "Бидон (один игрок): тяжёлый. ПКМ — наклонить ВБОК и лить.   " + key + " — режим «вдвоём»";
                else if (h == null)
                    t = "Бидон (вдвоём): второй игрок — F на бидон, взяться за ручку. Лить — оба держат ПКМ.   " + key + " — режим «один»";
                else
                    t = "Несёте вдвоём. Лить — оба держат ПКМ.   Ты: " + ((_wants.Value & 1) != 0 ? "✔" : "…") +
                        "   Напарник: " + ((_wants.Value & 2) != 0 ? "✔" : "…");
            }
            else if (meHelper)
            {
                t = "Ты держишь вторую ручку бидона. ПКМ — наклонить вместе (нужно обоим). F на бидон — отпустить.   Напарник: " +
                    ((_wants.Value & 1) != 0 ? "✔" : "…");
            }
            if (t == null) return;
            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _hintStyle.normal.textColor = Color.white;
            }
            float w = Mathf.Min(720f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height - 110f, w, 44f), t, _hintStyle);
        }
    }
}
