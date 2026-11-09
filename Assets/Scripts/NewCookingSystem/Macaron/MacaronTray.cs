using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ПРОТИВЕНЬ С КОЛЬЦАМИ — стальной лист с приваренными обрубками трубы. Кольцо = размер скорлупки.
    ///
    ///   1. ОТСАДКА. Шприцем (мешок для затирки) с тестом для макарон: смотришь в кольцо, держишь ЛКМ —
    ///      тесто растёт. Цель — ровно по кольцу:
    ///        меньше 85% — «маленькая»; 85–112% — ИДЕАЛЬНО; больше — перелил, растечётся за край.
    ///      Над кольцом подсказка: сколько налито.
    ///   2. ПОДСУШИВАНИЕ. После последней отсадки ~30 с — тесто матовеет (блик гаснет): корочка.
    ///      Поставил в духовку раньше — скорлупки треснут.
    ///   3. ВЫПЕЧКА. ЛКМ с противнем в руке — встаёт ровно на решётку духовки. От 130 °C, ~40 с.
    ///      Выше 190 °C — подгорят. Перемешанное тесто — растечётся блинами.
    ///      Получилось — у скорлупки снизу кружевная «юбочка».
    ///   4. F по готовому противню — скорлупки выпадают отдельными предметами (Shell Prefab),
    ///      каждая со своим размером и оценкой. Противень снова пуст.
    ///
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · этот компонент (он же «в руки») ·
    /// коллайдеры листа и колец. Кольца (центры, радиус) записывает меню «Макарон → 5».
    /// Духовка считает противень «пекущимся» (давление, как у хлеба).
    /// </summary>
    public sealed class MacaronTray : PickupItem, IOvenBaking
    {
        [Header("Что печём (пусто — по названию)")]
        [SerializeField] private ProductDef _batter;
        [SerializeField] private ProductDef _batterOver;
        [SerializeField] private ProductDef _shell;
        [Tooltip("Сетевой префаб скорлупки (MacaronShell)")]
        [SerializeField] private GameObject _shellPrefab;

        [Header("Кольца (ставит меню)")]
        [SerializeField] private Vector3[] _rings = new Vector3[0];
        [Tooltip("Внутренний радиус кольца (в осях противня)")]
        [SerializeField] private float _ringRadius = 0.25f;
        [SerializeField] private float _ringHeight = 0.08f;

        [Header("Точность отсадки (доля кольца)")]
        [SerializeField] private float _perfectMin = 0.85f;
        [SerializeField] private float _perfectMax = 1.12f;
        [Tooltip("Больше этого в кольцо не влезет — льётся мимо")]
        [SerializeField] private float _maxFill = 1.45f;

        [Header("Подсушивание и выпечка")]
        [Tooltip("Сколько секунд сохнет корочка после последней отсадки")]
        [SerializeField] private float _drySeconds = 30f;
        [Tooltip("Сколько корочки нужно, чтобы не треснуть (0..1)")]
        [Range(0f, 1f)][SerializeField] private float _skinNeeded = 0.75f;
        [SerializeField] private float _bakeFromC = 130f;
        [SerializeField] private float _bakeSeconds = 40f;
        [Tooltip("Выше этой температуры скорлупки подгорают")]
        [SerializeField] private float _burnAboveC = 190f;

        [Header("Вид")]
        [Tooltip("Материал теста/скорлупок (Food Heap, Mat_Macaron)")]
        [SerializeField] private Material _batterMat;
        [SerializeField] private Color _rawColor = new Color(0.80f, 0.62f, 0.46f);
        [SerializeField] private Color _bakedColor = new Color(0.78f, 0.58f, 0.40f);
        [SerializeField] private Color _burntColor = new Color(0.42f, 0.27f, 0.16f);
        [SerializeField] private bool _showFillHint = true;

        [Header("Постановка (ЛКМ в руке)")]
        [SerializeField] private float _placeReach = 3f;
        [SerializeField] private float _placeLift = 0.004f;
        [SerializeField, HideInInspector] private Quaternion _uprightRot = Quaternion.identity;
        [SerializeField, HideInInspector] private bool _hasUpright;

        [SerializeField] private bool _log = true;

        public enum TrayState : byte { Raw = 0, Baking = 1, Baked = 2 }
        // флаги результата кольца
        public const byte Cracked = 1, Spread = 2, Small = 4, Burnt = 8, Feet = 16;

        private readonly SyncList<byte> _fill = new SyncList<byte>();      // 0..255 = 0..MaxFill
        private readonly SyncList<byte> _result = new SyncList<byte>();    // флаги после выпечки
        private readonly SyncVar<byte> _state = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _skin = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _bake = new SyncVar<byte>(0);

        // сервер
        private float _skinT, _progress, _maxT, _skinAtBake, _hintAt;
        private bool _overmixed, _heating;
        private byte[] _quality;
        private int _lastQuarter = -1;

        // вид
        private Transform[] _pucks;
        private MeshRenderer[] _puckR;
        private MaterialPropertyBlock _mpb;

        public TrayState State => (TrayState)_state.Value;
        public bool IsBakingNow => base.IsServerInitialized && _heating;
        public int RingCount => _rings != null ? _rings.Length : 0;
        public float RingRadius => _ringRadius;
        public float Skin01 => _skin.Value / 255f;

        public float FillOf(int i) => i >= 0 && i < _fill.Count ? _fill[i] / 255f * _maxFill : 0f;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (!_hasUpright) { _uprightRot = transform.rotation; _hasUpright = true; }
            BuildPucks();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Resolve();
            _fill.Clear(); _result.Clear();
            for (int i = 0; i < RingCount; i++) { _fill.Add(0); _result.Add(0); }
            _quality = new byte[RingCount];
            if (_log)
            {
                string warn = "";
                if (RingCount == 0) warn += " ✖ нет колец — меню «Макарон → 5»;";
                if (_shellPrefab == null) warn += " ⚠ не задан Shell Prefab — скорлупки не достать;";
                if (_batter == null) warn += " ⚠ нет продукта «Тесто для макарон» — меню «Макарон → 1»;";
                Debug.Log($"[Противень] «{name}»: колец {RingCount}, радиус {_ringRadius:0.00}{warn}", this);
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _fill.OnChange += OnListChanged;
            _result.OnChange += OnListChanged;
            _state.OnChange += OnByte;
            _skin.OnChange += OnByte;
            ApplyLook();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _fill.OnChange -= OnListChanged;
            _result.OnChange -= OnListChanged;
            _state.OnChange -= OnByte;
            _skin.OnChange -= OnByte;
        }

        private void OnListChanged(SyncListOperation op, int index, byte a, byte b, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ApplyLook();
        }

        private void OnByte(byte a, byte b, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ApplyLook();
        }

        private void Resolve()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            if (_batter == null) _batter = MacaronTitles.Find(db, CookRecipeBook.Macaron.Batter);
            if (_batterOver == null) _batterOver = MacaronTitles.Find(db, CookRecipeBook.Macaron.BatterOver);
            if (_shell == null) _shell = MacaronTitles.Find(db, CookRecipeBook.Macaron.Shell);
        }

        // ───────────────────────── КОЛЬЦА ─────────────────────────

        /// <summary>Какое кольцо под точкой (мир). −1 — ни одного рядом.</summary>
        public int RingAt(Vector3 world)
        {
            Vector3 lp = transform.InverseTransformPoint(world);
            int best = -1; float bd = _ringRadius * _ringRadius * 1.7f;
            for (int i = 0; i < RingCount; i++)
            {
                float dx = lp.x - _rings[i].x, dz = lp.z - _rings[i].z;
                float d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public Vector3 RingWorld(int i) => i >= 0 && i < RingCount ? transform.TransformPoint(_rings[i]) : transform.position;

        /// <summary>Можно ли сейчас отсаживать это (подсказка — почему нет).</summary>
        public bool Accepts(ushort product, out string why)
        {
            Resolve();
            why = null;
            if (State != TrayState.Raw) { why = State == TrayState.Baking ? "противень печётся" : "противень готов — F, чтобы достать скорлупки"; return false; }
            if ((_batter != null && product == _batter.Id) || (_batterOver != null && product == _batterOver.Id)) return true;
            why = "в кольца — только «Тесто для макарон»";
            return false;
        }

        /// <summary>Шприц давит в кольцо amount порций. Возвращает, сколько ушло. Сервер.</summary>
        [Server]
        public float ServerPipe(int ring, ushort product, float amount)
        {
            if (ring < 0 || ring >= RingCount || amount <= 0f) return 0f;
            if (!Accepts(product, out string why)) { Hint(why); return 0f; }
            float f = FillOf(ring);
            float use = Mathf.Min(amount, _maxFill - f);
            if (use <= 0f) { Hint($"кольцо {ring + 1} переполнено — переходи к следующему"); return 0f; }
            if (_batterOver != null && product == _batterOver.Id && !_overmixed)
            {
                _overmixed = true;
                if (_log) Debug.Log($"[Противень] «{name}»: тесто перемешанное — скорлупки растекутся", this);
            }
            float nf = f + use;
            _fill[ring] = (byte)Mathf.Clamp(Mathf.RoundToInt(nf / _maxFill * 255f), 0, 255);
            _skinT = 0f;                                              // корочка сохнет заново после последней отсадки
            if (_skin.Value != 0) _skin.Value = 0;
            if (_log && Mathf.FloorToInt(f / 0.25f) != Mathf.FloorToInt(nf / 0.25f))
                Debug.Log($"[Противень] кольцо {ring + 1}: {nf * 100f:0}% {Verdict(nf)}", this);
            return use;
        }

        public string Verdict(float f) =>
            f < 0.02f ? "" : f < _perfectMin ? "— мало" : f <= _perfectMax ? "— ИДЕАЛЬНО" : "— ПЕРЕЛИЛ";

        // ───────────────────────── СЕРВЕР: сушка и выпечка ─────────────────────────

        private void Update()
        {
            if (base.IsServerInitialized)
            {
                ServerTick(Time.deltaTime);
                if (base.IsClientInitialized) TestKeys();         // хост: тестовая клавиша
            }
        }

        /// <summary>
        /// Духовка, в которой стоит противень. Противень широкий: корень может торчать за полость,
        /// поэтому проверяем корень, кольца, середину модели и полость с запасом.
        /// </summary>
        private BakeOven FindOven()
        {
            BakeOven o = BakeOven.Find(transform.position + transform.up * 0.05f);
            if (o != null) return o;
            for (int i = 0; i < RingCount; i++)
                if ((o = BakeOven.Find(RingWorld(i) + Vector3.up * 0.03f)) != null) return o;
            Renderer r = GetComponentInChildren<Renderer>();
            if (r != null && (o = BakeOven.Find(r.bounds.center)) != null) return o;
            foreach (BakeOven ov in BakeOven.All)
            {
                if (ov == null) continue;
                Bounds b = ov.ChamberBounds;
                if (b.size.sqrMagnitude < 1e-6f) continue;
                b.Expand(0.3f);
                if (b.Contains(transform.position)) return ov;
                for (int i = 0; i < RingCount; i++) if (b.Contains(RingWorld(i))) return ov;
            }
            return null;
        }

        private BakeOven NearOven(float maxDist)
        {
            BakeOven best = null;
            float bd = maxDist * maxDist;
            foreach (BakeOven o in BakeOven.All)
            {
                if (o == null) continue;
                float d = (o.transform.position - transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        private void ServerTick(float dt)
        {
            _heating = false;
            if (dt <= 0f || _quality == null) return;
            TrayState st = State;
            if (st == TrayState.Baked) return;
            BakeOven oven = FindOven();

            if (st == TrayState.Raw)
            {
                if (!AnyBatter()) return;
                bool hot = oven != null && oven.TemperatureC >= _bakeFromC;
                if (!hot)
                {
                    // сохнет на воздухе (в холодной духовке тоже)
                    float was = _skinT;
                    _skinT = Mathf.Min(1f, _skinT + dt / Mathf.Max(1f, _drySeconds));
                    byte b = (byte)Mathf.RoundToInt(_skinT * 255f);
                    if (b != _skin.Value) _skin.Value = b;
                    if (_log && was < _skinNeeded && _skinT >= _skinNeeded) Debug.Log($"[Противень] «{name}»: корочка схватилась — можно в духовку", this);
                    if (oven != null) Hint($"в духовке {oven.TemperatureC:0} °C — печётся с {_bakeFromC:0} °C (раздуй огонь F3)");
                    else
                    {
                        BakeOven near = NearOven(4f);
                        if (near != null && _skinT >= _skinNeeded)
                            Hint($"корочка готова, но противень не внутри духовки «{near.name}» — L открыть, ЛКМ противнем на решётку, L закрыть");
                    }
                    return;
                }
                _skinAtBake = _skinT;
                _progress = 0f; _maxT = oven.TemperatureC; _lastQuarter = -1;
                _state.Value = (byte)TrayState.Baking;
                if (_log) Debug.Log($"[Противень] «{name}»: в духовку, корочка {_skinT * 100f:0}%" +
                                    (_skinT < _skinNeeded ? " — РАНО, скорлупки треснут" : " — хорошо"), this);
                return;
            }

            // печётся
            if (oven == null) { Hint($"не в духовке — выпечка на паузе ({_progress * 100f:0}%)"); return; }
            float t = oven.TemperatureC;
            if (t < _bakeFromC) { Hint($"остыла духовка: {t:0} °C — выпечка на паузе"); return; }
            _heating = true;
            _maxT = Mathf.Max(_maxT, t);
            if (_log && t > _burnAboveC) Hint($"{t:0} °C — слишком жарко, скорлупки подгорают!");
            float speed = Mathf.Clamp(t / 150f, 0.6f, 1.5f) * oven.BakeSpeed;
            _progress = Mathf.Min(1f, _progress + dt * speed / Mathf.Max(1f, _bakeSeconds));
            byte pb = (byte)Mathf.RoundToInt(_progress * 255f);
            if (pb != _bake.Value) _bake.Value = pb;
            int q = Mathf.FloorToInt(_progress * 4f);
            if (_log && q != _lastQuarter && q > 0 && q < 4) Debug.Log($"[Противень] «{name}»: скорлупки пекутся {q * 25}% ({t:0} °C)", this);
            _lastQuarter = q;
            if (_progress >= 1f) Finish();
        }

        private bool AnyBatter()
        {
            for (int i = 0; i < _fill.Count; i++) if (_fill[i] > 4) return true;
            return false;
        }

        private void Finish()
        {
            int feet = 0, cracked = 0, spread = 0, small = 0, burnt = 0, total = 0;
            for (int i = 0; i < RingCount; i++)
            {
                float f = FillOf(i);
                if (f < 0.1f) { _result[i] = 0; continue; }
                total++;
                byte fl = 0;
                if (_skinAtBake < _skinNeeded) fl |= Cracked;
                if (f > _perfectMax || _overmixed) fl |= Spread;
                if (f < _perfectMin) fl |= Small;
                if (_maxT > _burnAboveC) fl |= Burnt;
                if ((fl & (Cracked | Spread | Small | Burnt)) == 0) fl |= Feet;
                int q = 100 - Mathf.RoundToInt(Mathf.Abs(f - 1f) * 40f);
                if ((fl & Cracked) != 0) q -= 30;
                if ((fl & Spread) != 0) q -= 25;
                if ((fl & Small) != 0) q -= 15;
                if ((fl & Burnt) != 0) q -= 35;
                _quality[i] = (byte)Mathf.Clamp(q, 10, 100);
                _result[i] = fl;
                if ((fl & Feet) != 0) feet++;
                if ((fl & Cracked) != 0) cracked++;
                if ((fl & Spread) != 0) spread++;
                if ((fl & Small) != 0) small++;
                if ((fl & Burnt) != 0) burnt++;
            }
            _state.Value = (byte)TrayState.Baked;
            if (_log) Debug.Log($"[Противень] «{name}»: СКОРЛУПКИ ГОТОВЫ — {total} шт.: с юбочкой {feet}, треснули {cracked}, растеклись {spread}, " +
                                $"маленькие {small}, подгорели {burnt}. F по противню — достать", this);
        }

        /// <summary>F: готовые скорлупки — выпадают предметами; иначе — взять противень.</summary>
        public override void ServerInteract(PlayerInteractor interactor)
        {
            if (State == TrayState.Baked) { ServerPop(); return; }
            base.ServerInteract(interactor);
        }

        [Server]
        private void ServerPop()
        {
            Resolve();
            if (_shellPrefab == null) { Debug.LogError($"[Противень] «{name}»: не задан Shell Prefab — скорлупки не достать", this); return; }
            int n = 0;
            for (int i = 0; i < RingCount; i++)
            {
                float f = FillOf(i);
                if (f < 0.1f) continue;
                byte fl = _result[i];
                float sizeK = f <= 1f ? Mathf.Lerp(0.7f, 1f, Mathf.Sqrt(f)) : 1f + (f - 1f) * 0.6f;
                if ((fl & Spread) != 0) sizeK *= 1.08f;
                Vector3 at = RingWorld(i) + transform.up * (_ringHeight * Mathf.Abs(transform.lossyScale.y) + 0.02f);
                GameObject go = Instantiate(_shellPrefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                MacaronShell sh = go.GetComponent<MacaronShell>();
                if (sh != null) sh.ServerSetup(_ringRadius * MaxScaleXZ() * sizeK, sizeK, fl, _quality[i]);
                FoodPiece fp = go.GetComponent<FoodPiece>();
                if (fp != null) fp.ServerPreset(_shell, FoodState.Baked, 100, 100, _quality[i]);
                base.Spawn(go);
                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.AddForce(Vector3.up * 1.2f + Random.insideUnitSphere * 0.4f, ForceMode.VelocityChange);
                _fill[i] = 0; _result[i] = 0;
                n++;
            }
            _state.Value = (byte)TrayState.Raw;
            _skin.Value = 0; _bake.Value = 0; _skinT = 0f; _overmixed = false;
            if (_log) Debug.Log($"[Противень] «{name}»: достали {n} скорлупок — шприцем ганаш на половинку, второй накрыть (ЛКМ скорлупкой в руке)", this);
        }

        private float MaxScaleXZ()
        {
            Vector3 s = transform.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        }

        private void Hint(string text)
        {
            if (!_log || Time.time < _hintAt || string.IsNullOrEmpty(text)) return;
            _hintAt = Time.time + 5f;
            Debug.Log($"[Противень] «{name}»: {text}", this);
        }

        // ───────────────────────── ПОСТАНОВКА (ЛКМ) ─────────────────────────

        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (ctx.Hand == null) return;
            if (!GrillPlacement.Find(ctx, transform, _placeReach, out GrillPlacement.Spot spot))
            {
                if (_log) Debug.Log($"[Противень] «{name}»: ЛКМ — некуда поставить: смотри на решётку духовки или на стол ближе {_placeReach:0.#} м", this);
                return;
            }
            Quaternion rot = GrillPlacement.FitRot(transform, GrillPlacement.UprightRot(transform, _uprightRot, spot.Frame), spot);
            ctx.Hand.ServerDrop(Vector3.zero, 0f);
            GrillPlacement.PlaceAt(transform, GetComponent<Rigidbody>(), rot, spot, _placeLift);
            if (_log) Debug.Log(spot.Oven != null ? $"[Противень] «{name}» стоит ровно на решётке «{spot.Oven.name}»" : $"[Противень] «{name}» поставлен ровно", this);
        }

        [ContextMenu("Запомнить позу: так противень стоит ровно")]
        private void CaptureUpright()
        {
            _uprightRot = transform.rotation; _hasUpright = true;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void BuildPucks()
        {
            int n = RingCount;
            _pucks = new Transform[n];
            _puckR = new MeshRenderer[n];
            if (n == 0) return;
            var holder = new GameObject("Batter (auto)").transform;
            holder.SetParent(transform, false);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Puck_" + i);
                go.layer = gameObject.layer;
                go.transform.SetParent(holder, false);
                go.transform.localPosition = _rings[i];
                go.AddComponent<MeshFilter>().sharedMesh = MacaronMesh.Puck();
                _puckR[i] = go.AddComponent<MeshRenderer>();
                _puckR[i].sharedMaterial = _batterMat;
                _puckR[i].enabled = false;
                _pucks[i] = go.transform;
            }
        }

        private void ApplyLook()
        {
            if (_pucks == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            TrayState st = State;
            float skin = Skin01;
            for (int i = 0; i < _pucks.Length; i++)
            {
                if (_pucks[i] == null) continue;
                float f = FillOf(i);
                bool show = f > 0.02f;
                _puckR[i].enabled = show;
                if (!show) continue;
                byte fl = i < _result.Count ? _result[i] : (byte)0;
                float r = _ringRadius;
                MeshFilter mf = _pucks[i].GetComponent<MeshFilter>();
                Color col;
                float glint;
                if (st == TrayState.Baked)
                {
                    mf.sharedMesh = MacaronMesh.Shell((fl & Cracked) != 0);
                    float k = f <= 1f ? Mathf.Lerp(0.7f, 1f, Mathf.Sqrt(f)) : 1f + (f - 1f) * 0.6f;
                    bool spread = (fl & Spread) != 0;
                    _pucks[i].localScale = new Vector3(r * k * (spread ? 1.08f : 1f), r * (spread ? 0.45f : 0.78f), r * k * (spread ? 1.08f : 1f));
                    col = (fl & Burnt) != 0 ? _burntColor : _bakedColor;
                    glint = 0.12f;
                }
                else
                {
                    mf.sharedMesh = MacaronMesh.Puck();
                    float rad, h;
                    if (f <= 1f) { rad = r * 0.97f * Mathf.Lerp(0.35f, 1f, Mathf.Sqrt(f)); h = r * 0.42f * Mathf.Lerp(0.35f, 1f, f); }
                    else { rad = r * (0.97f + (f - 1f) * 1.1f); h = r * 0.42f * Mathf.Max(0.45f, 1f - (f - 1f) * 0.9f); }   // перелил — растеклось за кольцо
                    _pucks[i].localScale = new Vector3(rad, h, rad);
                    col = Color.Lerp(_rawColor, _bakedColor, st == TrayState.Baking ? _bake.Value / 255f : 0f);
                    glint = Mathf.Lerp(0.7f, 0.06f, skin);                 // мокрое блестит, подсохшее — матовое
                }
                _puckR[i].GetPropertyBlock(_mpb);
                col.a = 1f;
                _mpb.SetColor("_BaseColor", col);
                _mpb.SetFloat("_GlintStrength", glint);
                _puckR[i].SetPropertyBlock(_mpb);
            }
        }

        private void LateUpdate()
        {
            // выпечка меняет цвет плавно (байт приходит часто — без колбэка)
            if (State == TrayState.Baking && _pucks != null && Time.frameCount % 10 == 0) ApplyLook();
        }

        /// <summary>Подсказка над кольцом, в которое смотрит игрок со шприцем.</summary>
        private void OnGUI()
        {
            if (!_showFillHint || State != TrayState.Raw) return;
            Camera cam = Camera.main;
            if (cam == null || PipingBagItem.LocalAimTray != this) return;
            int i = PipingBagItem.LocalAimRing;
            if (i < 0 || i >= RingCount) return;
            float f = FillOf(i);
            Vector3 sp = cam.WorldToScreenPoint(RingWorld(i) + Vector3.up * _ringRadius);
            if (sp.z <= 0f) return;
            string s = $"кольцо {i + 1}: {f * 100f:0}% {Verdict(f)}";
            Color c = f < _perfectMin ? new Color(1f, 0.85f, 0.3f) : f <= _perfectMax ? new Color(0.4f, 1f, 0.45f) : new Color(1f, 0.4f, 0.3f);
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.black;
            Rect rc = new Rect(sp.x - 120f, Screen.height - sp.y - 14f, 240f, 28f);
            GUI.Label(new Rect(rc.x + 1, rc.y + 1, rc.width, rc.height), s, style);
            style.normal.textColor = c;
            GUI.Label(rc, s, style);
        }

        // ───────────────────────── ТЕСТ ─────────────────────────

        // ───────── тестовая клавиша (хост) ─────────
        //   P (смотришь на противень) — залить все кольца идеально и сразу подсушить (можно в духовку);
        //   Shift+P — испечь сразу (F — достать скорлупки).
        [Header("Тест (хост)")]
        [Tooltip("Клавиша: смотришь на противень — залить кольца и подсушить; с Shift — испечь сразу")]
        [SerializeField] private KeyCode _testKey = KeyCode.P;

        private void TestKeys()
        {
            if (_testKey == KeyCode.None || !Input.GetKeyDown(_testKey)) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit h, 6f, ~0, QueryTriggerInteraction.Collide)) return;
            if (!h.collider.transform.IsChildOf(transform)) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (State == TrayState.Baked) { Debug.Log($"[Противень] «{name}»: тест — уже испечено, F — достать скорлупки", this); return; }
            if (State == TrayState.Raw && !AnyBatter()) TestFillAll();
            if (shift) TestBake();
            else
            {
                TestDry();
                Debug.Log($"[Противень] «{name}»: тест — кольца залиты идеально, корочка готова. Ставь в духовку (≥ {_bakeFromC:0} °C) или Shift+P — испечь сразу", this);
            }
        }

        [ContextMenu("Тест: залить все кольца идеально (Play, хост)")]
        private void TestFillAll()
        {
            if (!NetTest.ServerReady(this, "Противень")) return;
            Resolve();
            if (_batter == null) { Debug.LogWarning("[Противень] нет «Тесто для макарон»", this); return; }
            for (int i = 0; i < RingCount; i++) ServerPipe(i, _batter.Id, 1f - FillOf(i));
        }

        [ContextMenu("Тест: подсушить сразу")]
        private void TestDry()
        {
            if (!NetTest.ServerReady(this, "Противень")) return;
            _skinT = 1f; _skin.Value = 255;
        }

        [ContextMenu("Тест: испечь сразу")]
        private void TestBake()
        {
            if (!NetTest.ServerReady(this, "Противень")) return;
            if (!AnyBatter()) { Debug.LogWarning($"[Противень] «{name}»: тест — в кольцах нет теста, сначала «залить все кольца»", this); return; }
            _skinAtBake = Mathf.Max(_skinT, _skinNeeded); _maxT = 160f;
            Finish();
            Debug.Log($"[Противень] «{name}»: тест — испечено сразу. F по противню — скорлупки выпадут", this);
        }

#if UNITY_EDITOR
        public void EditorSetup(Vector3[] rings, float radius, float height, Material batterMat, GameObject shellPrefab)
        {
            _rings = rings; _ringRadius = radius; _ringHeight = height; _batterMat = batterMat;
            if (shellPrefab != null) _shellPrefab = shellPrefab;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
