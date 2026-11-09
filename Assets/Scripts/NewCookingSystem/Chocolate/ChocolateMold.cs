using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ФОРМА ДЛЯ ШОКОЛАДНОЙ ПЛИТКИ (стальной лоток с жёлтыми ручками). Носится в руках, это ещё и тара.
    ///
    ///   1. Мастерком переложи в форму «Шоколадную пасту» из ковша — вся паста одного цикла
    ///      (12 тёртого какао + мастерок сахара + мастерок масла = 16 порций) ровно заполняет форму.
    ///   2. Паста в форме застывает сама (как везде — ProductDef → Hardens Into / Harden Seconds):
    ///      глянцевая жидкая → матовая твёрдая. Пока форма горячая (горелка) — не застывает.
    ///   3. Застыло — F по форме: плитка «Шоколад» выскакивает отдельным предметом (Bar Prefab),
    ///      порций в ней — сколько было пасты. F по форме с жидкой пастой / пустой — просто взять форму.
    ///   4. Плитку можно кинуть в ковш (или в любую тару, что глотает продукты) — там она
    ///      «Шоколад» порциями; погрей горелкой или на огне — растает обратно в пасту (FoodHardening).
    ///      Застывший шоколад прямо в форме тоже тает от горелки.
    ///
    /// Внутри — дочерний «Fill» (низ — у дна формы): это шоколад в форме, по высоте — сколько налито;
    /// в нём «Liquid» (ровная гладь пасты) и «Solid» (плитка с квадратиками) — показывается одно из двух. Рядом обязательно Cookware (тара «Форма для шоколада», не глотает).
    /// Модель, коллайдеры и компоненты делает меню CatCooks → Шоколад → 5.
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class ChocolateMold : PickupItem
    {
        [Header("Продукты (пусто — найдутся по названию)")]
        [SerializeField] private ProductDef _paste;
        [SerializeField] private ProductDef _choc;
        [Tooltip("Сетевой префаб плитки (FoodPiece «Шоколад» + Chocolate Bar). Меню Шоколад → 6, сохрани руками")]
        [SerializeField] private GameObject _barPrefab;

        [Header("Объём")]
        [Tooltip("Сколько порций — полная форма (один цикл в ковше = 16)")]
        [Min(1)][SerializeField] private int _fullPortions = 16;
        [Tooltip("Меньше — плитку не достать (слишком тонкая)")]
        [Min(1)][SerializeField] private int _minPortions = 4;

        [Header("Вид")]
        [Tooltip("Меш шоколада в форме. Пусто — дочерний «Fill»")]
        [SerializeField] private Transform _fill;
        [Tooltip("Жидкая паста: тёплый молочно-коричневый")]
        [SerializeField] private Color _liquidColor = new Color(0.47f, 0.27f, 0.14f);
        [Tooltip("Застывший шоколад: тёмный горький")]
        [SerializeField] private Color _solidColor = new Color(0.27f, 0.14f, 0.075f);
        [Tooltip("Блик жидкой пасты / застывшего (Glint Strength материала): жидкая чуть влажная, застывший матовый")]
        [SerializeField] private Vector2 _glint = new Vector2(0.3f, 0.05f);
        [Tooltip("Цвет прочего (не шоколад) в форме")]
        [SerializeField] private Color _otherColor = new Color(0.8f, 0.75f, 0.65f);

        [Header("Поведение жидкой пасты")]
        [Tooltip("Покачивание пасты, когда форму несут/двигают (0 — нет)")]
        [Range(0f, 3f)][SerializeField] private float _slosh = 1f;
        [Tooltip("Плюх и рябь, когда кладут пасту или мешают миксером")]
        [Range(0f, 3f)][SerializeField] private float _wobble = 1f;
        [Tooltip("Предел наклона глади, градусы (чтобы не вылезала за борт)")]
        [Range(0f, 8f)][SerializeField] private float _maxTilt = 3f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _hardK = new SyncVar<byte>(0);   // как далеко застыла паста 0..255
        private readonly SyncVar<byte> _meltK = new SyncVar<byte>(0);   // как далеко растаял шоколад 0..255

        private Cookware _cw;
        private Renderer[] _fillR;
        private Vector3 _fillScale;
        private MaterialPropertyBlock _mpb;
        private float _nextSrv, _hintAt;
        private int _lastLogged = -1;
        private bool _srv, _net;

        // вид: жидкая гладь качается (у всех)
        private Transform _liquidT;
        private float _hard;                 // 0 жидкая … 1 застывшая (последнее посчитанное)
        private bool _solidShown;
        private int _lastTotal;
        private Vector2 _tilt, _tiltV;
        private float _bob, _bobV, _stirUntil, _meltFxAcc;
        private Vector3 _lastPos, _lastVel;
        private bool _hasLast;

        public int Portions => _cw != null ? _cw.TotalCount : 0;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            _cw = GetComponent<Cookware>();
            if (_fill == null) _fill = transform.Find("Fill");
            if (_fill != null)
            {
                _fillScale = _fill.localScale;
                _fillR = _fill.GetComponentsInChildren<Renderer>(true);
                _liquidT = _fill.Find("Liquid");
            }
            else Debug.LogWarning($"[Форма шоколада] «{name}»: нет дочернего Fill — шоколад в форме видно не будет (меню Шоколад → 5)", this);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _net = true;
            Resolve();
            if (_cw != null) _cw.ContentsChanged += ApplyLook;
            _hardK.OnChange += OnKChanged;
            _meltK.OnChange += OnKChanged;
            ApplyLook();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _srv = true;
            Resolve();
            if (_log) Debug.Log($"[Форма шоколада] «{name}»: в сети (сервер). Паста: {(_paste != null ? "✔" : "✖ нет «Шоколадная паста»")}, " +
                                $"шоколад: {(_choc != null ? "✔" : "✖ нет «Шоколад»")}, Bar Prefab: {(_barPrefab != null ? _barPrefab.name : "✖ пуст")}", this);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _srv = false;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _net = false;
            if (_cw != null) _cw.ContentsChanged -= ApplyLook;
            _hardK.OnChange -= OnKChanged;
            _meltK.OnChange -= OnKChanged;
        }

        private void OnKChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ApplyLook();
        }

        private void Resolve()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            if (_paste == null) _paste = MacaronTitles.Find(db, "Шоколадная паста");
            if (_choc == null) _choc = MacaronTitles.Find(db, "Шоколад");
            if (_paste != null && _choc == null) _choc = _paste.HardensInto;
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void Update()
        {
            if (!_srv || Time.time < _nextSrv) return;
            _nextSrv = Time.time + 0.25f;
            Resolve();
            if (_cw == null || _paste == null || _choc == null) return;

            // застывание пасты и таяние шоколада — считает FoodHardening, мы только показываем
            float hk = 0f, mk = 0f;
            if (_cw.CountOf(_paste.Id) > 0 && _paste.HardenSeconds > 0f)
            {
                float age = FoodHardening.AgeOf(_cw, _paste.Id);
                if (age > 0f) hk = Mathf.Clamp01(age / _paste.HardenSeconds);
            }
            if (_cw.CountOf(_choc.Id) > 0)
            {
                float m = FoodHardening.MeltAgeOf(_cw, _choc.Id);
                if (m > 0f) mk = Mathf.Clamp01(m / Mathf.Max(0.1f, FoodHardening.MeltSeconds));
            }
            byte h = (byte)Mathf.RoundToInt(hk * 255f), mm = (byte)Mathf.RoundToInt(mk * 255f);
            if (h != _hardK.Value) _hardK.Value = h;
            if (mm != _meltK.Value) _meltK.Value = mm;

            int n = _cw.TotalCount;
            if (_log && n != _lastLogged)
            {
                _lastLogged = n;
                if (n > 0)
                    Debug.Log($"[Форма шоколада] «{name}»: внутри {n}/{_fullPortions} порций (паста {_cw.CountOf(_paste.Id)}, шоколад {_cw.CountOf(_choc.Id)})" +
                              (n > _fullPortions ? " — больше полной формы, плитка выйдет толще" : n < _minPortions ? $" — для плитки нужно хотя бы {_minPortions}" : ""), this);
            }
        }

        /// <summary>F по форме: застывший шоколад — достать плитку, иначе — взять форму (носить).</summary>
        public override void ServerInteract(PlayerInteractor interactor)
        {
            if (TryPopBar(Vector3.zero)) return;
            if (_log) Debug.Log($"[Форма шоколада] «{name}»: F — взять форму (внутри {Portions}/{_fullPortions}). " +
                                "В руках: ЛКМ — вытряхнуть плитку, когда застынет; F/бросок — поставить", this);
            base.ServerInteract(interactor);
        }

        /// <summary>ЛКМ с формой в руках: застыло — вытряхнуть плитку перед собой; нет — подсказка.</summary>
        public override void ServerPrimaryUse(UseContext ctx)
        {
            Vector3 dir = ctx.Direction.sqrMagnitude > 1e-6f ? ctx.Direction.normalized : transform.forward;
            if (TryPopBar(dir)) return;
            if (!_log || Time.time < _hintAt) return;
            _hintAt = Time.time + 2f;
            Resolve();
            int n = Portions, choc = _choc != null && _cw != null ? _cw.CountOf(_choc.Id) : 0;
            float left = FoodHardening.LeftOf(_cw, _paste);
            Debug.Log(n == 0 ? $"[Форма шоколада] «{name}»: пусто — мастерком переложи пасту из ковша"
                    : choc < n ? $"[Форма шоколада] «{name}»: ещё жидкая{(left >= 0f ? $" — застынет через {left:0} с" : "")} (миксером — быстрее)"
                    : $"[Форма шоколада] «{name}»: шоколада {choc} — тонковато для плитки (нужно {_minPortions})", this);
        }

        /// <summary>Достать плитку. shake = направление (ЛКМ в руках — вытряхнуть вперёд), zero — выпрыгнуть вверх (F).</summary>
        [Server]
        private bool TryPopBar(Vector3 shake)
        {
            Resolve();
            if (_cw == null || _choc == null) return false;
            int choc = _cw.CountOf(_choc.Id), total = _cw.TotalCount;
            if (choc <= 0) return false;                                       // нечего доставать — взять форму
            if (choc < total)
            {
                int paste = _paste != null ? _cw.CountOf(_paste.Id) : 0;
                if (_log && Time.time >= _hintAt)
                {
                    _hintAt = Time.time + 3f;
                    float age = _paste != null ? FoodHardening.AgeOf(_cw, _paste.Id) : -1f;
                    string left = age >= 0f && _paste != null ? $", застынет через {Mathf.Max(0f, _paste.HardenSeconds - age):0} с" : "";
                    Debug.Log(paste > 0 ? $"[Форма шоколада] «{name}»: ещё не всё застыло (жидкой пасты {paste}{left}) — подожди"
                                        : $"[Форма шоколада] «{name}»: в форме не только шоколад — плитку не достать", this);
                }
                return false;                                                  // взять форму (паста донесётся, не выльется)
            }
            if (choc < _minPortions)
            {
                if (_log) Debug.Log($"[Форма шоколада] «{name}»: шоколада {choc} — тонковато для плитки (нужно {_minPortions}), долей пасты", this);
                return false;
            }
            if (_barPrefab == null)
            {
                Debug.LogError($"[Форма шоколада] «{name}»: шоколад застыл, но не задан Bar Prefab — плитку не достать. " +
                               "Меню Шоколад → 6, сохрани ChocolateBar префабом, добавь в Spawnable Prefabs и перетащи в Chocolate Mold → Bar Prefab", this);
                return false;                                                  // хотя бы взять форму в руки
            }

            int n = _cw.ServerTake(_choc.Id, choc);
            if (n <= 0) return false;
            Transform at = _fill != null ? _fill : transform;
            bool shaken = shake.sqrMagnitude > 1e-6f;
            Vector3 spawn = at.position + Vector3.up * 0.06f + (shaken ? shake * 0.25f : Vector3.zero);
            GameObject go = Instantiate(_barPrefab, spawn, at.rotation);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp != null) fp.ServerPreset(_choc, _choc.StartState, 100, 0, 100);
            ChocolateBar bar = go.GetComponent<ChocolateBar>();
            if (bar != null) bar.FullPortions = _fullPortions;
            base.Spawn(go);
            if (fp != null) fp.ServerSetCount(n);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
                rb.AddForce(shaken ? shake * 2.2f + Vector3.up * 0.8f : transform.up * 1.8f + Random.insideUnitSphere * 0.25f, ForceMode.VelocityChange);
            RpcPop(at.position);
            _hardK.Value = 0; _meltK.Value = 0;
            if (_log) Debug.Log($"[Форма шоколада] «{name}»: ПЛИТКА ДОСТАНА — «{_choc.Title}» ×{n} порций. " +
                                "Кинь её в ковш и погрей горелкой — растает обратно в пасту", this);
            return true;
        }

        [ObserversRpc]
        private void RpcPop(Vector3 at)
        {
            CCRuntimeParticles.Burst(at + Vector3.up * 0.03f, Vector3.up, _solidColor, _liquidColor, 6, CCRuntimeParticles.Look.Chips.Scaled(0.6f));
        }

        // ───────────────────────── ТЕСТ ─────────────────────────

        [ContextMenu("Тест: налить полную форму пасты")]
        private void TestFill()
        {
            if (!NetTest.ServerReady(this, "Форма шоколада")) return;
            Resolve();
            if (_paste == null) { Debug.LogWarning("[Форма шоколада] нет продукта «Шоколадная паста» в FoodDatabase", this); return; }
            _cw.ServerAddContent(_paste.Id, _paste.StartState, Mathf.Max(0, _fullPortions - _cw.TotalCount), 0);
            Debug.Log($"[Форма шоколада] «{name}»: тест — налито пасты до {_cw.TotalCount}", this);
        }

        [ContextMenu("Тест: застыть сейчас")]
        private void TestHarden()
        {
            if (!NetTest.ServerReady(this, "Форма шоколада")) return;
            Resolve();
            if (_paste == null || _choc == null) return;
            int n = _cw.ServerTake(_paste.Id, _cw.CountOf(_paste.Id));
            if (n > 0) _cw.ServerAddPinch(_choc.Id, _choc.StartState, n, 0, false);
            Debug.Log($"[Форма шоколада] «{name}»: тест — {n} порций пасты застыли в шоколад, жми F по форме", this);
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void ApplyLook()
        {
            if (_fill == null || _fillR == null || _cw == null) return;
            int total = _cw.TotalCount;
            bool show = total > 0;
            foreach (Renderer r in _fillR) if (r != null) r.enabled = show;
            if (total > _lastTotal && _lastTotal >= 0 && !_solidShown) _bobV += 0.9f * _wobble;    // плюх: положили пасту
            _lastTotal = total;
            if (!show) return;

            float k = Mathf.Clamp(total / (float)Mathf.Max(1, _fullPortions), 0.06f, 1.2f);
            _fill.localScale = new Vector3(_fillScale.x, _fillScale.y * k, _fillScale.z);

            int paste = _paste != null ? _cw.CountOf(_paste.Id) : 0;
            int choc = _choc != null ? _cw.CountOf(_choc.Id) : 0;
            int other = Mathf.Max(0, total - paste - choc);
            // «твёрдость» 0 — жидкая паста, 1 — застывший шоколад; плавно по ходу застывания/таяния
            _hard = (paste * (_hardK.Value / 255f) + choc * (1f - _meltK.Value / 255f)) / Mathf.Max(1, paste + choc);
            float hs = _hard * _hard * (3f - 2f * _hard);
            Color c = Color.Lerp(_liquidColor, _solidColor, hs);
            if (other > 0) c = Color.Lerp(c, _otherColor, other / (float)total);
            float glint = Mathf.Lerp(_glint.x, _glint.y, hs);

            // ровная гладь пасты (Fill/Liquid) ↔ плитка с квадратиками (Fill/Solid); нет детей — один меш
            bool solid = _hard >= 0.5f && other == 0;
            if (solid != _solidShown && _log)
                Debug.Log($"[Форма шоколада] «{name}»: {(solid ? "застыло — плитка (матовая, пористая)" : "жидкая паста (тёплая, с пузырьками)")}", this);
            _solidShown = solid;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            foreach (Renderer r in _fillR)
            {
                if (r == null) continue;
                string nm = r.gameObject.name;
                if (nm == "Liquid") r.enabled = !solid;
                else if (nm == "Solid") r.enabled = solid;
                r.GetPropertyBlock(_mpb);
                Material m = r.sharedMaterial;
                if (m != null && m.HasProperty("_BaseColor")) _mpb.SetColor("_BaseColor", c);
                if (m != null && m.HasProperty("_Color")) _mpb.SetColor("_Color", c);
                if (m != null && m.HasProperty("_Smoothness")) _mpb.SetFloat("_Smoothness", Mathf.Lerp(0.55f, 0.15f, hs));
                if (m != null && m.HasProperty("_GlintStrength")) _mpb.SetFloat("_GlintStrength", glint);
                r.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>Венчик миксера в форме (у всех, зовёт ChocolateWork): жидкая — рябь и покачивание.</summary>
        public void ClientStir(Vector3 whisk, bool solidMostly)
        {
            if (solidMostly || _solidShown || _liquidT == null) return;
            _stirUntil = Time.time + 0.2f;
            Vector3 l = transform.InverseTransformPoint(whisk);
            Vector2 toward = new Vector2(l.x, l.z);
            if (toward.sqrMagnitude > 1e-6f) toward.Normalize();
            float liquid = 1f - _hard;
            _tiltV += (toward * 0.25f + Random.insideUnitCircle * 0.6f) * _wobble * liquid;
            _bobV += Random.Range(-0.35f, 0.35f) * _wobble * liquid;
        }

        /// <summary>
        /// Жидкая паста живая: качается, когда форму двигают/несут (против ускорения), плюхает, когда кладут,
        /// рябит под миксером; чем ближе к застыванию — тем гуще и ленивее. Застывшая — неподвижна.
        /// </summary>
        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _liquidT == null) return;
            Vector3 pos = transform.position;
            Vector3 vel = _hasLast ? (pos - _lastPos) / dt : Vector3.zero;
            Vector3 acc = _hasLast ? (vel - _lastVel) / dt : Vector3.zero;
            _lastPos = pos; _lastVel = vel; _hasLast = true;

            bool liquidShown = !_solidShown && _cw != null && _cw.TotalCount > 0;
            float liquid = liquidShown ? Mathf.Clamp01(1f - _hard) : 0f;
            // густеет — пружина жёстче, затухание сильнее
            float stiff = Mathf.Lerp(90f, 26f, liquid), damp = Mathf.Lerp(18f, 3.2f, liquid);
            Vector3 la = transform.InverseTransformDirection(Vector3.ClampMagnitude(acc, 25f));
            Vector2 push = new Vector2(-la.x, -la.z) * 0.05f * _slosh * liquid;
            if (Time.time < _stirUntil) push += Random.insideUnitCircle * 0.8f * _wobble * liquid;
            _tiltV += (push * stiff * 0.06f - _tilt * stiff) * dt;
            _tiltV *= Mathf.Exp(-damp * dt);
            _tilt += _tiltV * dt;
            _bobV += (-_bob * stiff * 1.6f) * dt;
            _bobV *= Mathf.Exp(-damp * 1.2f * dt);
            _bob += _bobV * dt;
            if (liquid <= 0f) { _tilt = Vector2.Lerp(_tilt, Vector2.zero, 1f - Mathf.Exp(-dt * 12f)); _bob = Mathf.Lerp(_bob, 0f, 1f - Mathf.Exp(-dt * 12f)); }

            float tx = Mathf.Clamp(_tilt.y * 40f, -_maxTilt, _maxTilt), tz = Mathf.Clamp(-_tilt.x * 40f, -_maxTilt, _maxTilt);
            _liquidT.localRotation = Quaternion.Euler(tx, 0f, tz);
            _liquidT.localScale = new Vector3(1f, Mathf.Clamp(1f + _bob * 0.12f, 0.9f, 1.12f), 1f);

            // тает: над гладью лёгкий тёплый парок с какао-запахом
            if (_meltK.Value > 0 && _meltK.Value < 255 && _fill != null)
            {
                _meltFxAcc += dt * 5f;
                if (_meltFxAcc >= 1f)
                {
                    _meltFxAcc -= 1f;
                    Vector3 at = _fill.position + transform.up * (_fillScale.y * 0.02f + 0.02f) + Random.insideUnitSphere * 0.04f;
                    CCRuntimeParticles.Burst(at, Vector3.up, new Color(0.75f, 0.6f, 0.5f, 0.45f), new Color(0.9f, 0.85f, 0.8f, 0.3f), 1, CCRuntimeParticles.Look.Dust.Scaled(0.6f));
                }
            }
        }

        private void OnGUI()
        {
            if (!_net || _cw == null || !base.IsOwner) return;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null || !rb.isKinematic) return;                         // не в руках
            int n = _cw.TotalCount;
            string t = n == 0 ? "Форма пустая — мастерком переложи шоколадную пасту"
                     : _meltK.Value > 0 ? $"Шоколад тает… {_meltK.Value * 100 / 255}%"
                     : _choc != null && _cw.CountOf(_choc.Id) == n ? $"Застыло ({n}/{_fullPortions}) — ЛКМ: вытряхнуть плитку"
                     : $"Паста {n}/{_fullPortions} — застывает {_hardK.Value * 100 / 255}%";
            GUI.Label(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.62f, 360f, 24f), t);
        }
    }
}
