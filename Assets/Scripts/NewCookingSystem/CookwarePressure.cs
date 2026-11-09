using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ДАВЛЕНИЕ, ВЗРЫВ И РЕМОНТ НОВОЙ КАСТРЮЛИ. Замена старому PotThermal для тары на Cookware.
    ///
    /// ═══ КАК ИГРАЕТСЯ ═══
    ///   • Крышка закрыта (PotLidSocket) и вода кипит — копится давление. Чем сильнее огонь,
    ///     тем быстрее. Под давлением вода греется выше 100 °C.
    ///   • С Overheat At — перегрев: кастрюля трясётся, крышка дребезжит, пар свистит
    ///     из-под крышки. Ещё можно спастись: снять крышку (E, LidRemover) — давление стравится.
    ///   • Давление 1 — ВЗРЫВ: крышку срывает вверх, корпус корёжит (DeformableMesh),
    ///     в стенках дыры и трещины (PotDamage), всё содержимое — на пол (лужа/кучки),
    ///     кастрюлю подбрасывает, рядом лежащие предметы отталкивает.
    ///   • Разорванная кастрюля течёт через дыры: налитое вытекает лужей.
    ///   • РЕМОНТ: дыры — сварка (WeldingTorchItem), форма — молоток/кувалда
    ///     (HammerItem, PotHammerRepair). Когда и то, и другое — кастрюля снова целая.
    ///
    /// ═══ СЕТЬ ═══
    /// Сервер считает; по сети — байт давления и байт состояния. Температура — из CookwareHeat.
    ///
    /// ═══ КУДА ВЕШАТЬ ═══
    /// На корень кастрюли рядом с Cookware и CookwareHeat.
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    [RequireComponent(typeof(CookwareHeat))]
    public sealed class CookwarePressure : NetworkBehaviour, IPotBody
    {
        [Header("Давление")]
        [Tooltip("Как быстро копится давление при кипении под крышкой (доля в секунду при слабом огне). " +
                 "0.02 — около минуты до взрыва")]
        [Range(0.001f, 0.5f)][SerializeField] private float _pressureRate = 0.015f;
        [Tooltip("Насколько сильный огонь ускоряет давление: ×(1 + это) при огне 240 °C")]
        [Range(0f, 5f)][SerializeField] private float _fireBoost = 1.5f;
        [Tooltip("Как быстро стравливается без крышки или без кипения (доля в секунду)")]
        [Range(0.01f, 2f)][SerializeField] private float _ventRate = 0.5f;
        [Tooltip("С какого давления перегрев: тряска, дребезг крышки, свист пара")]
        [Range(0.1f, 0.95f)][SerializeField] private float _overheatAt = 0.6f;
        [Tooltip("На сколько градусов поднимается кипение при полном давлении")]
        [SerializeField] private float _pressureBoilBonusC = 40f;

        [Header("Шкала градусника")]
        [SerializeField] private float _ambientC = 20f;
        [Tooltip("Верх шкалы градусника, °C")]
        [SerializeField] private float _maxTempC = 300f;

        [Header("Взрыв")]
        [Tooltip("Подбросить саму кастрюлю, импульс")]
        [SerializeField] private float _selfKick = 4f;
        [Tooltip("Радиус, в котором отталкивает предметы, м")]
        [SerializeField] private float _blastRadius = 2.5f;
        [Tooltip("Сила толчка предметов рядом")]
        [SerializeField] private float _blastForce = 6f;
        [Tooltip("Содержимое вылетает на пол при взрыве")]
        [SerializeField] private bool _dumpContents = true;

        [Header("Течь разорванной")]
        [Tooltip("Сколько порций в секунду вытекает через открытые дыры")]
        [Min(0f)][SerializeField] private float _leakPortionsPerSecond = 3f;

        [Header("Отладка")]
        [Tooltip("Взорвать кастрюлю, на которую смотришь (только хост): ТОЛЬКО вместе с Shift, " +
                 "чтобы не рвануть случайно. None — выкл.")]
        [SerializeField] private KeyCode _debugRuptureKey = KeyCode.None;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _pressureNet = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _conditionNet = new SyncVar<byte>((byte)PotCondition.Cold);

        private Cookware _cw;
        private CookwareHeat _heat;
        private PotLidSocket _lid;
        private DeformableMesh _shape;
        private PotDamage _holes;
        private Rigidbody _rb;

        private float _press;
        private float _leakAcc;

        // ───────── IPotBody ─────────
        public float TemperatureC => _heat != null ? _heat.TemperatureC : _ambientC;
        public float Temp01 => Mathf.Clamp01(Mathf.InverseLerp(_ambientC, _maxTempC, TemperatureC));
        public float Pressure01 => base.IsServerInitialized ? _press : _pressureNet.Value / 255f;
        public PotCondition Condition => (PotCondition)_conditionNet.Value;
        public bool IsRuptured => Condition == PotCondition.Ruptured;
        public event Action Ruptured;

        /// <summary>Крышка закрыта.</summary>
        public bool LidClosed => (_lid != null && _lid.HasLid) || (_hinged != null && !_hinged.IsOpen);
        private OvenHingedLid _hinged;      // откидная крышка духовки

        /// <summary>
        /// Пар не от кипящей воды, а «снаружи» (духовка: хлеб печётся — идёт пар). Сколько источников пара
        /// (0 — нет). Пока больше нуля и крышка закрыта — давление копится, как при кипении. Ставит сервер (BakeOven).
        /// </summary>
        public float ExternalSteam { get; set; }

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _heat = GetComponent<CookwareHeat>();
            _lid = GetComponent<PotLidSocket>();
            _hinged = GetComponentInChildren<OvenHingedLid>(true);
            _shape = GetComponent<DeformableMesh>();
            if (_shape == null) _shape = GetComponentInChildren<DeformableMesh>();
            _holes = GetComponent<PotDamage>();
            if (_holes == null) _holes = GetComponentInChildren<PotDamage>();
            _rb = GetComponent<Rigidbody>();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _conditionNet.OnChange += OnConditionChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _conditionNet.OnChange -= OnConditionChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (!_log) return;
            Debug.Log($"[Кастрюля] «{name}»: " +
                      (_lid != null ? "крышка-гнездо есть" : _hinged != null ? "откидная крышка есть" : "НЕТ PotLidSocket — давление копиться не будет") +
                      (_shape != null ? ", вмятины есть" : ", НЕТ DeformableMesh — корёжить нечего") +
                      (_holes != null ? ", дыры есть" : ", НЕТ PotDamage — дыр и течи не будет"), this);
        }

        private void OnConditionChanged(byte prev, byte next, bool asServer)
        {
            // У хоста приходит дважды: asServer = true, потом false.
            if (asServer && base.IsClientInitialized) return;
            if ((PotCondition)next == PotCondition.Ruptured && (PotCondition)prev != PotCondition.Ruptured)
                Ruptured?.Invoke();
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void Update()
        {
            if (!base.IsServerInitialized || _heat == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);

            if (IsRuptured)
            {
                _press = 0f;
                _heat.BoilBonusC = 0f;
                Leak(dt);
            }
            else
            {
                bool cooking = LidClosed && (_heat.ServerBoiling || ExternalSteam > 0f);
                if (cooking)
                {
                    float fire = Mathf.InverseLerp(100f, 240f, _heat.FireC);
                    float steam = _heat.ServerBoiling ? 1f : Mathf.Clamp(ExternalSteam, 0f, 4f) * 0.5f;
                    _press += _pressureRate * (1f + fire * _fireBoost) * Mathf.Max(steam, 0.25f) * dt;
                }
                else _press = Mathf.Max(0f, _press - _ventRate * dt);

                _heat.BoilBonusC = _press * _pressureBoilBonusC;
                if (_press >= 1f) { _why = "давление под крышкой"; ServerRupture(); return; }
                SetCondition(Evaluate());
            }

            byte p = (byte)Mathf.RoundToInt(Mathf.Clamp01(_press) * 255f);
            if (Mathf.Abs(p - _pressureNet.Value) >= 3 || (p == 0) != (_pressureNet.Value == 0)) _pressureNet.Value = p;

            DebugKey();
        }

        private PotCondition Evaluate()
        {
            if (_press >= _overheatAt) return PotCondition.Overheat;
            if (_heat.ServerBoiling) return PotCondition.Boiling;
            if (_heat.TemperatureC >= _ambientC + 25f) return PotCondition.Warm;
            return PotCondition.Cold;
        }

        private void SetCondition(PotCondition c)
        {
            if (_conditionNet.Value != (byte)c) _conditionNet.Value = (byte)c;
        }

        // ───────────────────────── ВЗРЫВ ─────────────────────────

        /// <summary>Рвануло: крышка вверх, корпус корёжит, дыры, содержимое на пол.</summary>
        [Server]
        public void ServerRupture()
        {
            if (IsRuptured) return;
            _press = 0f;
            _heat.BoilBonusC = 0f;
            float t = _heat.TemperatureC;

            if (_lid != null) _lid.ServerBlowOff(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            if (_hinged != null) _hinged.ServerBlowOpen();
            if (_shape != null) _shape.ServerDamage(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            if (_dumpContents) DumpContents();

            SetCondition(PotCondition.Ruptured);       // PotDamage ловит Ruptured и пробивает дыры
            Blast();
            RpcBoom(_cw != null ? _cw.MouthPoint : transform.position);
            if (_log) Debug.Log($"[Кастрюля] «{name}»: ВЗРЫВ при {t:0} °C — причина: {_why ?? "вызов извне"}", this);
            _why = null;
        }

        /// <summary>Всё содержимое — через край на пол (лужа для жидкого, кучки для остального).</summary>
        private void DumpContents()
        {
            if (_cw == null) return;
            for (int guard = 0; guard < 64 && _cw.TotalCount > 0; guard++)
            {
                int took = _cw.ServerTakeTop(999, out ushort product, out FoodState state, out byte cook);
                if (took <= 0) break;
                _cw.ServerSpillOver(product, state, took, cook);
            }
        }

        private void Blast()
        {
            Vector3 c = transform.position;
            if (_rb != null && !_rb.isKinematic)
                _rb.AddForce((Vector3.up + UnityEngine.Random.insideUnitSphere * 0.3f).normalized * _selfKick, ForceMode.Impulse);
            if (_blastRadius <= 0f || _blastForce <= 0f) return;
            var seen = new HashSet<Rigidbody>();
            foreach (Collider col in Physics.OverlapSphere(c, _blastRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                Rigidbody rb = col.attachedRigidbody;
                if (rb == null || rb == _rb || rb.isKinematic || !seen.Add(rb)) continue;
                rb.AddExplosionForce(_blastForce, c, _blastRadius, 0.6f, ForceMode.Impulse);
            }
        }

        [ObserversRpc]
        private void RpcBoom(Vector3 at)
        {
            // Пар и брызги — у всех. Звук и большой клуб пара — в PotEffects (по событию Ruptured).
            LiquidFx.Splash(at, Vector3.up, new Color(0.85f, 0.92f, 1f), 30, 0.04f, 3.5f);
            CCRuntimeParticles.Burst(at, Vector3.up, new Color(1f, 0.8f, 0.4f), Color.white, 20,
                                     CCRuntimeParticles.Look.Chips.Scaled(1.5f));
        }

        // ───────────────────────── ТЕЧЬ ─────────────────────────

        private void Leak(float dt)
        {
            if (_cw == null || _cw.TotalCount <= 0) { _leakAcc = 0f; return; }
            // Дыры заварены — уже не течёт (форма может быть ещё мятой).
            if (_holes != null && _holes.IsSealed) { _leakAcc = 0f; return; }
            _leakAcc += _leakPortionsPerSecond * dt;
            int n = Mathf.FloorToInt(_leakAcc);
            if (n <= 0) return;
            _leakAcc -= n;
            int took = _cw.ServerTakeTop(n, out ushort product, out FoodState state, out byte cook);
            if (took > 0) _cw.ServerSpillOver(product, state, took, cook);
        }

        // ───────────────────────── РЕМОНТ ─────────────────────────

        /// <summary>
        /// Сварка и молоток зовут это после каждого удачного действия. Кастрюля снова цела,
        /// только когда И дыры заварены, И форма выправлена — порядок не важен.
        /// </summary>
        [Server]
        public void ServerCheckFullyRepaired()
        {
            if (!IsRuptured) return;
            bool holesOk = _holes == null || _holes.IsSealed;
            bool shapeOk = _shape == null || !_shape.IsBroken;
            if (!holesOk || !shapeOk)
            {
                if (_log) Debug.Log($"[Кастрюля] «{name}»: ещё не готова — " +
                                    (!holesOk ? $"дыр открыто {_holes.OpenHoles}" : "") +
                                    (!holesOk && !shapeOk ? ", " : "") +
                                    (!shapeOk ? "форма мятая (молоток)" : ""), this);
                return;
            }
            _press = 0f;
            _leakAcc = 0f;
            SetCondition(PotCondition.Cold);
            if (_log) Debug.Log($"[Кастрюля] «{name}» починена и снова держит давление", this);
        }

        private void DebugKey()
        {
            if (_debugRuptureKey == KeyCode.None || !Input.GetKeyDown(_debugRuptureKey)) return;
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)) return;
            if (hit.collider.GetComponentInParent<CookwarePressure>() != this) return;
            _why = $"отладка (Shift+{_debugRuptureKey})";
            ServerRupture();
        }

        private string _why;
    }
}
