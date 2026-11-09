using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КОТЛЕТА — от комка фарша до панировки (части 2–3; жарка — часть 4).
    /// Префаб-заготовка: меню Котлеты → 4 (сохранить префабом), Котлеты → 5.
    ///
    /// ЧАСТЬ 2 — ФОРМОВКА:
    ///   • Мастерок «Фарш отбитый» бросить (ПКМ) на стол/доску — кучка сама становится КОМКОМ (этот предмет).
    ///     Ещё мастерок в комок — комок больше (от Min Portions до Max Portions).
    ///   • Мастерок в руке, смотришь на комок, ЛКМ ЗАЖАТЬ и водить мышью ПО КРУГУ: камера стоит, мастерок ходит по котлете
    ///     кругами, комок округляется и плющится. Turns Needed кругов → «Котлета сформована».
    /// ЧАСТЬ 4 — ЖАРКА:
    ///   • В сковороду — сливочное масло и подсолнечное (мастерок / нажатие бутылки). Котлету в руке — ПКМ по сковороде:
    ///     ложится ровно на дно. Жарится НИЖНЯЯ сторона (Side Seconds до золотой; зелёная зона 80–125%, с 160% — горит).
    ///   • R — перевернуть, дожарить вторую. Обе стороны в зелёной зоне → «Котлета жареная». Без масла — жарится быстрее и пригорает.
    /// ЧАСТЬ 3 — ПАНИРОВКА:
    ///   • Мастерком «Панировочные сухари» (из мешка или из ведра) — бросить (ПКМ) на котлету: сухари ложатся горкой сверху.
    ///   • ЛКМ по котлете мастерком — ПРИХЛОПНУТЬ: каждый хлопок вдавливает сухари (+25% на эту сторону).
    ///   • R (смотришь на котлету) — ПЕРЕВЕРНУТЬ, панировать вторую сторону. Обе стороны от 80% → «В панировке».
    /// Вид — свой меш у каждого игрока по SyncVar'ам: комок → котлета; сверху и снизу — корочка из сухарей по %.
    /// </summary>
    [DefaultExecutionOrder(10000)]          // поза мастерка — после того, как рука поставила его на место
    [RequireComponent(typeof(FoodPiece))]
    public sealed class CutletPatty : NetworkBehaviour, IScoopReceiver, IFoodWorkHandler, IFoodPutHandler, IFoodSecondaryHandler
    {
        [Header("Масса")]
        [Range(1, 20)][SerializeField] private int _minPortions = 4;
        [Range(2, 24)][SerializeField] private int _maxPortions = 8;
        [Tooltip("Радиус котлеты из Min Portions порций, м (при масштабе мира 4)")]
        [SerializeField] private float _pattyRadius = 0.64f;

        [Header("Формовка")]
        [Tooltip("Сколько полных кругов мышью до ровной котлеты")]
        [Range(1f, 12f)][SerializeField] private float _turnsNeeded = 4f;
        [Tooltip("Дальность, м")]
        [SerializeField] private float _reach = 4f;

        [Header("Панировка")]
        [Tooltip("Порций сухарей на одну сторону (мастерок = 2)")]
        [Range(1, 8)][SerializeField] private int _crumbsPerSide = 2;
        [Tooltip("Хлопков на сторону")]
        [Range(1, 10)][SerializeField] private int _patsPerSide = 4;
        [SerializeField] private KeyCode _flipKey = KeyCode.R;

        [Header("Жарка")]
        [Tooltip("Секунд на сторону до золотой корочки (сковорода на огне)")]
        [Min(2f)][SerializeField] private float _sideSeconds = 20f;
        [Tooltip("Без масла — во сколько раз быстрее и пригорает")]
        [Range(1f, 3f)][SerializeField] private float _dryPanSpeed = 1.7f;

        [Header("Вид")]
        [Tooltip("Материал с цветом в вершинах (мультяшный материал игры)")]
        [SerializeField] private Material _material;
        [SerializeField] private bool _hud = true;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _shape = new SyncVar<byte>(0);      // 0..255 — комок → ровная котлета
        private readonly SyncVar<byte> _loose = new SyncVar<byte>(0);      // сухари горкой сверху, в половинках порции
        private readonly SyncVar<byte> _coatTop = new SyncVar<byte>(0);    // 0..255 — панировка сверху
        private readonly SyncVar<byte> _coatBot = new SyncVar<byte>(0);    // 0..255 — снизу
        private readonly SyncVar<byte> _fryTop = new SyncVar<byte>(0);     // 0..255 = 0..2 (1 — золотая)
        private readonly SyncVar<byte> _fryBot = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _fat = new SyncVar<byte>(0);        // 1 — сливочное масло было, 2 — подсолнечное
        public const float GoldFrom = 0.8f, GoldTo = 1.25f, BurnAt = 1.6f;

        private FoodPiece _food;
        private Transform _model;
        private Mesh _mesh;
        private BoxCollider _box;
        private int _builtKey = -1;
        private float _shapeAcc, _sendAt;
        private bool _shaping;
        private float _prevAngle, _toolAngle, _toolBlend, _patT = -1f, _flipT = -1f;
        private bool _hasPrev;
        private FirstPersonController _fpc;
        private Transform _trowel;

        public float Shape01 => _shape.Value / 255f;
        public int Portions => _food != null ? _food.Count : _minPortions;
        public bool Shaped => _shape.Value >= 255;
        public float CoatTop01 => _coatTop.Value / 255f;
        public float CoatBot01 => _coatBot.Value / 255f;
        public bool Breaded => CoatTop01 >= 0.8f && CoatBot01 >= 0.8f;
        public float FryTop => _fryTop.Value / 255f * 2f;
        public float FryBot => _fryBot.Value / 255f * 2f;
        public bool FriedBoth => FryTop >= GoldFrom && FryBot >= GoldFrom;
        public bool Burnt => FryTop >= BurnAt || FryBot >= BurnAt;
        public bool InGoldBoth => FriedBoth && FryTop <= GoldTo && FryBot <= GoldTo;
        public bool BothFats => (_fat.Value & 3) == 3;
        private float _fryLogAt;
        private string _fryWhy;

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _box = GetComponent<BoxCollider>();
            MeshRenderer own = GetComponent<MeshRenderer>();
            if (own != null) own.enabled = false;
        }

        // ───────────────────────── МАСТЕРОК: ФАРШ / СУХАРИ ─────────────────────────

        public int ServerReceiveScoop(ushort product, FoodState state, int count, Vector3 point, Vector3 normal, out string why)
        {
            why = null;
            ProductDef d = FoodDatabase.Instance != null ? FoodDatabase.Instance.Get(product) : null;
            string t = d != null && d.Title != null ? d.Title.Trim().ToLowerInvariant() : "";
            if (t.Contains("сухар"))
            {
                if (!Shaped) { why = "сначала сформуй котлету (мастерок, ЛКМ зажать и по кругу), потом сухари"; return 0; }
                int roomL = Mathf.Max(0, _crumbsPerSide * 2 * 2 - _loose.Value);                 // до двух сторон в запас
                if (roomL <= 0) { why = "сухарей сверху и так горка — прихлопни мастерком (ЛКМ)"; return 0; }
                int tookC = Mathf.Min(count, Mathf.CeilToInt(roomL / 2f));
                _loose.Value = (byte)Mathf.Min(255, _loose.Value + tookC * 2);
                if (_log) Debug.Log($"[Котлета] «{name}»: сухари сверху +{tookC} порц. — прихлопни мастерком (ЛКМ)", this);
                return tookC;
            }
            if (t == "фарш для котлет") { why = "фарш ещё не отбит — кувалдой по ведру 8 ударов"; return 0; }
            if (t != "фарш отбитый") { why = "на котлету — «Фарш отбитый» (комок) или «Панировочные сухари» (сформованная)"; return 0; }
            if (_shape.Value > 40) { why = "котлету уже формуют — новый фарш брось рядом, отдельным комком"; return 0; }
            int room = _maxPortions - _food.Count;
            if (room <= 0) { why = $"комок и так большой ({_food.Count} порц.) — остальное брось рядом"; return 0; }
            int took = Mathf.Min(room, count);
            _food.ServerSetCount(_food.Count + took);
            if (_log) Debug.Log($"[Котлета] «{name}»: +{took} порц. фарша — {_food.Count} (нужно от {_minPortions})", this);
            return took;
        }

        // ───────────────────────── ВВОД (у того, кто держит мастерок) ─────────────────────────

        private void Update()
        {
            OwnerInput();
            Rebuild();
            if (base.IsServerInitialized) ServerFryTick(Time.deltaTime);
            SampleGraph();
        }

        private void OwnerInput()
        {
            if (_food == null || _food.IsConsumed || _food.IsHeld) { StopShaping(); return; }
            Camera cam = Camera.main;
            if (cam == null) { StopShaping(); return; }
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            // переворот — R, глядя на котлету
            if (Shaped && Input.GetKeyDown(_flipKey) && AimedPatty(ray) == this && !HoldsTool(cam, "SanderItem"))
            {
                _flipT = 0f;
                CmdFlip();
            }

            if (Shaped)
            {
                StopShaping();
                // хлопок мастерком — ЛКМ по котлете
                if (Input.GetMouseButtonDown(0) && AimedPatty(ray) == this && HoldsTool(cam, "ScoopItem"))
                {
                    _patT = 0f;
                    CmdPat();
                }
                return;
            }

            if (!Input.GetMouseButton(0)) { StopShaping(); return; }
            if (!_shaping)
            {
                if (!Input.GetMouseButtonDown(0)) return;
                if (AimedPatty(ray) != this) return;
                if (!HoldsTool(cam, "ScoopItem"))
                {
                    if (_log) Debug.Log("[Котлета] формуют МАСТЕРКОМ: возьми мастерок (F) и зажми ЛКМ на комке", this);
                    return;
                }
                if (_food.Count < _minPortions)
                {
                    if (_log) Debug.Log($"[Котлета] мало фарша: {_food.Count} из {_minPortions} порц. — брось ещё мастерок на комок (ПКМ)", this);
                    return;
                }
                _shaping = true; _hasPrev = false;
                if (_log) Debug.Log($"[Котлета] «{name}»: формую — води мышью по кругу (форма {Mathf.RoundToInt(Shape01 * 100f)}%)", this);
            }
            // круги мышью: копим поворот направления движения (полный круг = 360°); мастерок ходит по кругу так же
            Vector2 d = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            if (d.sqrMagnitude > 0.0004f)
            {
                float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                if (_hasPrev)
                {
                    float signed = Mathf.DeltaAngle(_prevAngle, a);
                    float turn = Mathf.Abs(signed);
                    if (turn < 90f) { _shapeAcc += turn / 360f; _toolAngle += signed * Mathf.Deg2Rad; }
                }
                _prevAngle = a; _hasPrev = true;
            }
            if (Time.time >= _sendAt && _shapeAcc > 0.02f)
            {
                _sendAt = Time.time + 0.1f;
                CmdShape(_shapeAcc);
                _shapeAcc = 0f;
            }
        }

        private void StopShaping()
        {
            if (!_shaping) return;
            _shaping = false; _hasPrev = false;
            if (_fpc != null) { _fpc.SpeedMultiplier = 1f; _fpc.ResetLookToCurrent(); }
            if (_shapeAcc > 0f) { CmdShape(_shapeAcc); _shapeAcc = 0f; }
        }

        private void LateUpdate()
        {
            // камера стоит, пока формуешь (как у пилы и растирания специй)
            if (_shaping && _fpc != null) { _fpc.HoldLook(); _fpc.SpeedMultiplier = 0f; }
            AnimateTrowel();
        }

        /// <summary>
        /// Мастерок в руке: при формовке — ходит кругами по котлете, лезвием по фаршу; при хлопке — шлёп сверху.
        /// Ставим ПОСЛЕ руки (порядок выполнения 10000); по сети позу разносит NetworkTransform мастерка.
        /// </summary>
        private void AnimateTrowel()
        {
            bool pat = _patT >= 0f;
            if (pat) { _patT += Time.deltaTime; if (_patT > 0.3f) _patT = -1f; }
            if (_flipT >= 0f) { _flipT += Time.deltaTime; if (_flipT > 0.35f) _flipT = -1f; }
            float want = _shaping || pat ? 1f : 0f;
            _toolBlend = Mathf.MoveTowards(_toolBlend, want, Time.deltaTime * 8f);
            if (_toolBlend <= 0.001f || _trowel == null || _mesh == null) return;
            Bounds b = _mesh.bounds;
            float R = Mathf.Max(b.extents.x, b.extents.z) * Mathf.Abs(transform.lossyScale.x);
            Vector3 top = transform.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            Vector3 pos; Quaternion rot;
            if (pat && !_shaping)
            {
                float k = _patT / 0.3f;
                float h = k < 0.4f ? Mathf.Lerp(R * 0.5f, 0.01f, k / 0.4f) : Mathf.Lerp(0.01f, R * 0.5f, (k - 0.4f) / 0.6f);
                pos = top + Vector3.up * h;
                Vector3 fwd = Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.forward : Vector3.forward, Vector3.up);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                rot = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(80f, 0f, 0f);    // лезвие плашмя
            }
            else
            {
                float a = _toolAngle;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                pos = top + radial * R * 0.55f + Vector3.up * (R * 0.06f);
                rot = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(60f, 0f, -20f);
            }
            _trowel.position = Vector3.Lerp(_trowel.position, pos, _toolBlend);
            _trowel.rotation = Quaternion.Slerp(_trowel.rotation, rot, _toolBlend);
        }

        private static readonly List<CutletPatty> _all = new List<CutletPatty>();
        private void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
        private void OnDisable() { _all.Remove(this); if (_shaping) StopShaping(); }

        /// <summary>Котлета, к центру которой луч проходит ближе всех (в пределах досягаемости и радиуса). null — ни одной.</summary>
        private CutletPatty AimedPatty(Ray ray)
        {
            CutletPatty best = null; float bd = float.MaxValue;
            for (int i = 0; i < _all.Count; i++)
            {
                CutletPatty p = _all[i];
                if (p == null || p._food == null || p._food.IsConsumed || p._food.IsHeld) continue;
                Bounds b = p._mesh != null ? p._mesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.3f);
                Vector3 c = p.transform.TransformPoint(b.center);
                Vector3 to = c - ray.origin;
                float along = Vector3.Dot(to, ray.direction);
                if (along < 0f || along > p._reach + b.extents.magnitude) continue;
                float off = (to - ray.direction * along).magnitude;
                float allow = Mathf.Max(0.12f, Mathf.Max(b.extents.x, b.extents.z) * Mathf.Abs(p.transform.lossyScale.x) * 1.3f);
                if (off > allow) continue;
                float score = off / allow + along * 0.01f;
                if (score < bd) { bd = score; best = p; }
            }
            return best;
        }

        /// <summary>В руке у этого игрока предмет такого класса (ScoopItem — мастерок/лопата)? По предмету, которым он владеет.</summary>
        private bool HoldsTool(Camera cam, string typeName)
        {
            if (_fpc == null) _fpc = cam.GetComponentInParent<FirstPersonController>();
            if (_fpc == null) _fpc = Object.FindFirstObjectByType<FirstPersonController>();
            foreach (InteractableItem it in Object.FindObjectsByType<InteractableItem>(FindObjectsSortMode.None))
            {
                if (it == null || !it.IsOwner || it.GetType().Name != typeName) continue;
                if (typeName == "ScoopItem") _trowel = it.transform;
                return true;
            }
            return false;
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        [ServerRpc(RequireOwnership = false)]
        private void CmdShape(float turns)
        {
            if (_food == null || _food.IsConsumed || Shaped || _food.Count < _minPortions) return;
            turns = Mathf.Clamp(turns, 0f, 1f);
            int was = Mathf.FloorToInt(Shape01 * 4f);
            int v = Mathf.Clamp(_shape.Value + Mathf.RoundToInt(turns / _turnsNeeded * 255f), 0, 255);
            _shape.Value = (byte)v;
            int now = Mathf.FloorToInt(Shape01 * 4f);
            if (_log && now > was && now < 4) Debug.Log($"[Котлета] «{name}»: форма {now * 25}%", this);
            if (Shaped)
            {
                _food.ServerSetProductState(null, FoodState.Shaped);
                if (_log) Debug.Log($"[Котлета] «{name}»: СФОРМОВАНА — ровная круглая ({_food.Count} порц.). Дальше: сухари мастерком сверху, ЛКМ — прихлопнуть, R — перевернуть", this);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdPat()
        {
            if (!Shaped || _food == null || _food.IsConsumed) return;
            if (_loose.Value == 0)
            {
                if (_log) Debug.Log($"[Котлета] «{name}»: хлопать нечего — брось сухари мастерком (ПКМ) на котлету", this);
                return;
            }
            int per = Mathf.Max(1, _patsPerSide);
            int use = Mathf.Max(1, Mathf.RoundToInt(_crumbsPerSide * 2f / per));           // половинок порции за хлопок
            use = Mathf.Min(use, _loose.Value);
            float add = use / (_crumbsPerSide * 2f);
            _loose.Value = (byte)(_loose.Value - use);
            int top = Mathf.Clamp(_coatTop.Value + Mathf.RoundToInt(add * 255f), 0, 255);
            bool wasBreaded = Breaded;
            _coatTop.Value = (byte)top;
            if (_log) Debug.Log($"[Котлета] «{name}»: прихлопнул — сверху {Mathf.RoundToInt(CoatTop01 * 100f)}%, снизу {Mathf.RoundToInt(CoatBot01 * 100f)}%" +
                                (CoatTop01 >= 0.8f && CoatBot01 < 0.8f ? " · переверни (R)" : ""), this);
            if (!wasBreaded && Breaded)
            {
                _food.ServerSetProductState(null, FoodState.Breaded);
                if (_log) Debug.Log($"[Котлета] «{name}»: В ПАНИРОВКЕ — с двух сторон. Дальше — сковорода с маслом, ПКМ по ней котлетой", this);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdFlip()
        {
            if (!Shaped || _food == null || _food.IsConsumed) return;
            byte t = _coatTop.Value; _coatTop.Value = _coatBot.Value; _coatBot.Value = t;
            byte f = _fryTop.Value; _fryTop.Value = _fryBot.Value; _fryBot.Value = f;
            _flips.Value = (byte)(_flips.Value + 1);
            _fryAcc = 0f;
            if (_loose.Value > 0)
            {
                if (_log) Debug.Log($"[Котлета] «{name}»: перевернул — несхваченные сухари осыпались ({_loose.Value / 2f:0.#} порц.)", this);
                _loose.Value = 0;
            }
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) rb.AddForce(Vector3.up * 1.2f, ForceMode.VelocityChange);
            if (_log) Debug.Log(FryTop > 0.01f || FryBot > 0.01f
                ? $"[Котлета] «{name}»: перевернул — жарится сторона {Mathf.RoundToInt(FryBot * 100f)}%, сверху {Mathf.RoundToInt(FryTop * 100f)}%"
                : $"[Котлета] «{name}»: перевернул — сверху {Mathf.RoundToInt(CoatTop01 * 100f)}%, снизу {Mathf.RoundToInt(CoatBot01 * 100f)}%", this);
        }

        // ───────────────────────── ЖАРКА — сама, пока лежит в таре на огне (сковорода или кастрюля с маслом) ─────────────────────────

        private const byte FS_None = 0, FS_Cold = 1, FS_Dry = 2, FS_Butter = 3, FS_Oil = 4, FS_Both = 5, FS_Water = 6, FS_Raw = 7;
        private readonly SyncVar<byte> _fryState = new SyncVar<byte>(0);   // что сейчас с котлетой в таре (для HUD у всех)
        private readonly SyncVar<byte> _flips = new SyncVar<byte>(0);      // чётность — какая сторона внизу (для графика)
        private Cookware _pan;
        private float _panAt;
        private float _fryAcc;                                             // сервер: недокопленная доля прожарки нижней стороны

        /// <summary>Сковорода/кастрюля сама шлёт «Жарить/Варить» кускам — котлета это глотает и жарится своим тиком (ниже).</summary>
        public bool ServerHandleWork(FoodPiece piece, FoodProcess process, float seconds, float quality01)
        {
            return process == FoodProcess.Fry || process == FoodProcess.Bake || process == FoodProcess.Boil || process == FoodProcess.Torch;
        }

        public void ServerStopWork(FoodPiece piece) { }

        private void SetFS(byte v) { if (_fryState.Value != v) _fryState.Value = v; }

        /// <summary>Сервер, каждый кадр: тара под котлетой на огне → жарится нижняя сторона.</summary>
        private void ServerFryTick(float dt)
        {
            if (_food == null || _food.IsConsumed || _food.IsHeld) { _pan = null; SetFS(FS_None); return; }
            if (Time.time >= _panAt) { _panAt = Time.time + 0.25f; _pan = PanUnder(); }
            if (_pan == null) { SetFS(FS_None); return; }
            if (!_pan.IsHeating) { SetFS(FS_Cold); return; }
            if (!Shaped) { SetFS(FS_Raw); FryHint("сначала сформуй котлету, потом жарь"); return; }
            if (Burnt) return;
            int fat = Fats(_pan);
            if ((fat & 3) == 0 && (fat & 4) != 0) { SetFS(FS_Water); FryHint("в воде/молоке котлету не жарят — нужна сковорода или кастрюля с маслом"); return; }
            fat &= 3;
            if (fat != 0 && (_fat.Value | fat) != _fat.Value) _fat.Value = (byte)(_fat.Value | fat);
            float speed = fat == 0 ? _dryPanSpeed : 1f;
            if (fat == 0) { SetFS(FS_Dry); FryHint("сухая тара — пригорает! налей масло (сливочное и подсолнечное)"); }
            else if (fat == 1) { SetFS(FS_Butter); FryHint("жарится на сливочном — добавь и подсолнечное (за звезду)"); }
            else if (fat == 2) { SetFS(FS_Oil); FryHint("жарится на подсолнечном — добавь и сливочное (за звезду)"); }
            else SetFS(FS_Both);
            // Копим дробь на сервере: за кадр прибавка ~0.0008 — меньше шага байта (1/127), без накопителя жарка стояла на 0%.
            float was = FryBot;
            _fryAcc += dt * speed / _sideSeconds;
            float want = Mathf.Min(2f, was + _fryAcc);
            byte b = (byte)Mathf.Clamp(Mathf.FloorToInt(want / 2f * 255f), 0, 255);
            if (b != _fryBot.Value) { _fryBot.Value = b; _fryAcc = want - FryBot; }
            float now = FryBot;
            if (_log && Mathf.FloorToInt(was * 4f) != Mathf.FloorToInt(now * 4f))
            {
                if (now >= BurnAt) Debug.Log($"[Котлета] «{name}»: СГОРЕЛА снизу", this);
                else if (now >= GoldFrom && was < GoldFrom) Debug.Log($"[Котлета] «{name}»: низ золотой — " + (FryTop >= GoldFrom ? "готова, снимай!" : "переверни (R)"), this);
                else if (now > GoldTo && was <= GoldTo) Debug.Log($"[Котлета] «{name}»: низ темнеет — переверни/сними, а то сгорит", this);
                else if (now < GoldFrom) Debug.Log($"[Котлета] «{name}»: жарится в «{_pan.name}» {Mathf.RoundToInt(now * 100f)}%", this);
            }
            if (Burnt) _food.ServerSetProductState(null, FoodState.Burnt);
            else if (FriedBoth && _food.State != FoodState.Fried) { _food.ServerSetProductState(null, FoodState.Fried); if (_log) Debug.Log($"[Котлета] «{name}»: ЖАРЕНАЯ — обе стороны. На тарелку", this); }
        }

        private void FryHint(string why)
        {
            if (_log && (why != _fryWhy || Time.time >= _fryLogAt)) { _fryLogAt = Time.time + 6f; Debug.Log($"[Котлета] «{name}»: {why}", this); }
            _fryWhy = why;
        }

        /// <summary>Тара с огнём, в которой лежит котлета (сковорода, кастрюля). Тарелки/миски без огня — не в счёт.</summary>
        private Cookware PanUnder()
        {
            Vector3 p = _mesh != null ? transform.TransformPoint(_mesh.bounds.center) : transform.position + Vector3.up * 0.05f;
            Cookware best = null; float bd = float.MaxValue;
            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null || cw.Def == null || !cw.Def.NeedsHeatSource) continue;
                Collider iv = cw.InsideVolume;
                bool inside = cw.IsInside(p);
                Collider c = iv != null ? iv : cw.GetComponent<Collider>();
                if (!inside && c != null)
                {
                    Bounds bb = c.bounds;
                    bb.Expand(new Vector3(0.1f, 0.4f, 0.1f));       // котлета лежит на самом дне — чуть ниже «полости»
                    inside = bb.Contains(p);
                }
                if (!inside) continue;
                float d = (cw.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = cw; }
            }
            return best;
        }


        /// <summary>Жиры в таре: 1 — сливочное масло, 2 — подсолнечное/растительное.</summary>
        private static int Fats(Cookware cw)
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return 3;
            int f = 0;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count <= 0) continue;
                ProductDef d = db.Get(v.Product);
                if (d == null || string.IsNullOrEmpty(d.Title)) continue;
                string t = d.Title.ToLowerInvariant();
                if (t.Contains("сливочное масло") || t.Contains("масло сливоч")) f |= 1;
                else if (t.Contains("масло") && (t.Contains("подсолнеч") || t.Contains("растител"))) f |= 2;
                else if (t == "вода" || t.Contains("молоко") || t.Contains("бульон") || t.Contains("сливки")) f |= 4;
            }
            return f;
        }

        /// <summary>В любой таре котлета лежит собой (не растворяется в порции) — жарится, её можно достать.</summary>
        public bool ServerHandlePut(Cookware cw, out bool put)
        {
            put = true;
            if (cw.Def != null && !cw.Def.Absorbs) return false;     // сковорода и так держит куском
            cw.ServerAdoptLoose(_food);
            return true;
        }

        /// <summary>ПКМ котлетой в руке по сковороде — ровно на дно (рядом с другими котлетами, не внахлёст).</summary>
        public bool ServerHandleSecondary(FoodPiece piece, UseContext ctx)
        {
            if (ctx.Hand == null) return false;
            Vector3 dir = ctx.Direction.sqrMagnitude > 1e-6f ? ctx.Direction.normalized : Vector3.forward;
            Cookware pan = null;
            RaycastHit[] hits = Physics.RaycastAll(ctx.Origin, dir, 5f, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.transform.IsChildOf(transform) || h.distance >= best) continue;
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                Cookware cw = h.collider.GetComponentInParent<Cookware>();
                if (cw == null) continue;
                best = h.distance; pan = cw;
            }
            if (pan == null) { if (_log) Debug.Log("[Котлета] ПКМ — смотри на сковороду (котлета ляжет на дно)", this); return false; }
            CookwareFillTuner tuner = pan.GetComponent<CookwareFillTuner>();
            Vector3 c; float rPan;
            if (tuner != null) { c = tuner.HeapCenterWorld; rPan = tuner.HeapBottomR; }
            else
            {
                Bounds bb = pan.InsideVolume != null ? pan.InsideVolume.bounds : new Bounds(pan.transform.position, Vector3.one * 0.5f);
                c = new Vector3(bb.center.x, bb.min.y, bb.center.z); rPan = Mathf.Min(bb.extents.x, bb.extents.z);
            }
            Bounds mb = _mesh != null ? _mesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.5f);
            float halfL = mb.extents.x, halfW = mb.extents.z;
            Vector3 right = Vector3.ProjectOnPlane(Quaternion.LookRotation(dir) * Vector3.right, Vector3.up);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 fwd = Vector3.Cross(right, Vector3.up);
            // свободное место: сетка вокруг центра — бок о бок поперёк и в ряд вдоль; ближайшее к центру свободное
            Vector3 target = c; bool found = false; float bestD = float.MaxValue;
            float stepW = halfW * 2.3f, stepL = halfL * 2.15f;
            for (int i = -3; i <= 3; i++)
                for (int j = -2; j <= 2; j++)
                {
                    Vector3 cand = c + fwd * (i * stepW) + right * (j * stepL);
                    float dc = Vector3.Distance(cand, c);
                    if (dc >= bestD || (dc > rPan * 1.05f && (i != 0 || j != 0))) continue;
                    bool free = true;
                    foreach (CutletPatty o in _all)
                    {
                        if (o == null || o == this || o._food == null || o._food.IsHeld || o._food.IsConsumed) continue;
                        Vector3 op = o.transform.position; op.y = cand.y;
                        Vector3 rel = op - cand;
                        if (Mathf.Abs(Vector3.Dot(rel, fwd)) < halfW * 1.9f && Mathf.Abs(Vector3.Dot(rel, right)) < halfL * 1.9f) { free = false; break; }
                    }
                    if (free) { bestD = dc; target = cand; found = true; }
                }
            if (!found && _log) Debug.Log("[Котлета] в сковороде тесно — легла в центр поверх", this);
            ctx.Hand.ServerDrop(dir, 0f);
            Rigidbody rb = GetComponent<Rigidbody>();
            Quaternion rot = Quaternion.FromToRotation(Vector3.right, right);                                // длинная ось (X) — поперёк взгляда
            Vector3 pos = target + Vector3.up * 0.01f;
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.position = pos; rb.rotation = rot; }
            transform.SetPositionAndRotation(pos, rot);
            if (_log) Debug.Log($"[Котлета] «{name}»: на сковороду «{pan.name}»" + (Fats(pan) == 0 ? " — ⚠ масла нет, пригорит" : ""), this);
            return true;
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void Rebuild()
        {
            int key = (((((_shape.Value * 64 + Mathf.Clamp(Portions, 0, 63)) * 17 + _coatTop.Value / 16) * 17 + _coatBot.Value / 16) * 64 + Mathf.Min(63, (int)_loose.Value)) * 33 + _fryTop.Value / 8) * 33 + _fryBot.Value / 8;
            if (key == _builtKey) return;
            _builtKey = key;
            if (_model == null)
            {
                var go = new GameObject("CutletModel (auto)") { hideFlags = HideFlags.DontSave };
                go.layer = gameObject.layer;
                _model = go.transform; _model.SetParent(transform, false);
                _mesh = new Mesh { name = "Cutlet", hideFlags = HideFlags.DontSave };
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _material != null ? _material : Fallback();
            }
            float scale = Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
            float mass = Mathf.Pow(Mathf.Max(1, Portions) / (float)_minPortions, 1f / 3f);
            BuildMesh(_mesh, _pattyRadius * scale * mass, Shape01, CoatTop01, CoatBot01, _loose.Value, FryTop, FryBot, GetInstanceID());
            if (_box != null)
            {
                Bounds b = _mesh.bounds;
                _box.center = b.center;
                _box.size = new Vector3(Mathf.Max(0.02f, b.size.x), Mathf.Max(0.02f, b.size.y), Mathf.Max(0.02f, b.size.z));
            }
        }

        private struct Shp { public Vector3[] Bump; public float[] BumpA; public float R, K; }

        /// <summary>
        /// Точка поверхности по направлению (th: 0 — верх, π — низ; ph — вокруг). Комок (бугристый, высокий) ↔ «лодочка»
        /// (вытянутый овал, сужается к концам, купол сверху, плоское дно). Низ на y = 0.
        /// </summary>
        private static Vector3 Surf(Shp s, float th, float ph)
        {
            float R = s.R;
            Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
            float n = 1f;
            for (int b = 0; b < s.Bump.Length; b++) { float dd = Vector3.Dot(d, s.Bump[b]); if (dd > 0.6f) n += s.BumpA[b] * (dd - 0.6f) / 0.4f; }
            n += 0.06f * Mathf.Sin(ph * 5f + th * 3f) + 0.05f * Mathf.Sin(ph * 9f - th * 7f);
            Vector3 lump = new Vector3(d.x * R * 0.72f * n, (d.y * 0.5f + 0.5f) * R * 1.15f * n, d.z * R * 0.72f * n);
            float ct = Mathf.Cos(th), st = Mathf.Sin(th);
            float sy = Mathf.Sign(ct) * Mathf.Pow(Mathf.Abs(ct), 0.6f);
            float rxz = Mathf.Pow(Mathf.Max(0f, st), 0.6f);
            float cp = Mathf.Cos(ph), sp = Mathf.Sin(ph);
            float L = R * 1.45f, W = R * 0.8f * (1f - 0.22f * cp * cp);              // к концам уже — «лодочка»
            float H = R * 0.85f * (1f - 0.2f * cp * cp);                            // к концам ниже
            Vector3 boat = new Vector3(cp * rxz * L, (sy * 0.5f + 0.5f) * H, sp * rxz * W);
            if (sy < 0f) boat.y *= 0.6f;                                              // дно приплюснуто
            return Vector3.Lerp(lump, boat, s.K);
        }

        /// <summary>Корочка по жарке: 0 — сырая, 1 — золотая, 1.6+ — горелая.</summary>
        private static Color Fry(Color c, float f)
        {
            if (f <= 0.001f) return c;
            Color gold = new Color(0.78f, 0.50f, 0.22f), burnt = new Color(0.22f, 0.13f, 0.07f);
            Color g = Color.Lerp(c, Color.Lerp(c * 0.6f, gold, 0.7f), Mathf.Clamp01(f));
            return f <= GoldTo ? g : Color.Lerp(g, burnt, Mathf.Clamp01((f - GoldTo) / (BurnAt - GoldTo)));
        }

        /// <summary>
        /// Меш котлеты: поверхность (комок → лодочка); панировка — сотни мелких крошек по поверхности (доля = %),
        /// под ними — светлая основа (не розовое мясо); несхваченные сухари — тонкий слой крошек сверху.
        /// Жарка красит сторону: золотистая → коричневая → горелая.
        /// </summary>
        private static void BuildMesh(Mesh m, float R, float k, float coatTop, float coatBot, int loose, float fryTop, float fryBot, int seed)
        {
            const int La = 18, Lo = 36;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            var rnd = new System.Random(seed);
            var sh = new Shp { Bump = new Vector3[6], BumpA = new float[6], R = R, K = k };
            for (int i = 0; i < 6; i++)
            {
                Vector3 d = new Vector3((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 1.4f - 0.4f, (float)rnd.NextDouble() * 2 - 1);
                sh.Bump[i] = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.up;
                sh.BumpA[i] = 0.25f + 0.3f * (float)rnd.NextDouble();
            }
            Color meat = new Color(0.82f, 0.45f, 0.42f), dark = new Color(0.68f, 0.32f, 0.30f), onion = new Color(0.95f, 0.92f, 0.85f);
            Color crumbBase = new Color(0.86f, 0.72f, 0.50f);
            for (int i = 0; i <= La; i++)
            {
                float th = Mathf.PI * i / La;
                for (int j = 0; j <= Lo; j++)
                {
                    float ph = 2f * Mathf.PI * (j % Lo) / Lo;
                    v.Add(Surf(sh, th, ph));
                    float dy = Mathf.Cos(th);
                    float coat = dy > 0.1f ? coatTop : dy < -0.1f ? coatBot : (coatTop + coatBot) * 0.5f;
                    float fry = dy > 0.1f ? fryTop : dy < -0.1f ? fryBot : Mathf.Max(fryTop, fryBot) * 0.7f;
                    float sp2 = Mathf.Sin(ph * 13f + th * 17f + seed * 0.1f) * Mathf.Sin(ph * 7f - th * 11f);
                    Color col = sp2 > 0.75f ? onion : Color.Lerp(meat, dark, Mathf.Abs(Mathf.Sin(ph * 4f + th * 6f)) * 0.5f);
                    col = Color.Lerp(col, crumbBase, Mathf.Clamp01(coat * 1.3f));        // под панировкой — светлая основа
                    c.Add(Fry(col, fry));
                }
            }
            for (int i = 0; i < La; i++)
                for (int j = 0; j < Lo; j++)
                {
                    int a = i * (Lo + 1) + j, b = a + Lo + 1;
                    t.Add(a); t.Add(a + 1); t.Add(b);
                    t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            // крошки панировки — по всей покрытой поверхности
            Color c1 = new Color(0.94f, 0.82f, 0.56f), c2 = new Color(0.86f, 0.67f, 0.38f), c3 = new Color(0.74f, 0.54f, 0.30f);
            for (int side = 0; side < 2; side++)
            {
                float coat = side == 0 ? coatTop : coatBot, fry = side == 0 ? fryTop : fryBot;
                int nC = Mathf.RoundToInt(coat * 520f);
                for (int q = 0; q < nC; q++)
                {
                    // верх: th 0..0.58π, низ: 0.42π..π (с бортиком)
                    float u = (float)rnd.NextDouble();
                    float th = side == 0 ? Mathf.Acos(1f - u * 1.25f) : Mathf.PI - Mathf.Acos(1f - u * 1.25f);
                    float ph = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    Vector3 p = Surf(sh, th, ph);
                    Vector3 nrm = (Surf(sh, Mathf.Max(0.001f, th - 0.02f), ph) - Surf(sh, Mathf.Min(Mathf.PI - 0.001f, th + 0.02f), ph));
                    Vector3 tan = Surf(sh, th, ph + 0.02f) - Surf(sh, th, ph - 0.02f);
                    Vector3 nn = Vector3.Cross(nrm, tan).normalized;
                    if (Vector3.Dot(nn, p - new Vector3(0f, R * 0.3f, 0f)) < 0f) nn = -nn;
                    float s = R * (0.010f + 0.012f * (float)rnd.NextDouble());
                    float w = (float)rnd.NextDouble();
                    Color col = w < 0.4f ? c1 : w < 0.8f ? c2 : c3;
                    Crumb(v, c, t, p + nn * s * 0.3f, nn, s, Fry(col, fry), rnd);
                }
            }
            // несхваченные сухари — тонкий слой крошек по верху
            if (loose > 0)
            {
                int nL = Mathf.Min(420, loose * 70);
                for (int q = 0; q < nL; q++)
                {
                    float u = (float)rnd.NextDouble();
                    float th = Mathf.Acos(1f - u * 0.9f);
                    float ph = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    Vector3 p = Surf(sh, th, ph) + Vector3.up * R * (0.01f + 0.025f * (float)rnd.NextDouble());
                    float s = R * (0.009f + 0.01f * (float)rnd.NextDouble());
                    float w = (float)rnd.NextDouble();
                    Crumb(v, c, t, p, Vector3.up, s, w < 0.5f ? c1 : c2, rnd);
                }
            }
            m.Clear();
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
        }

        /// <summary>Крошка: неровный приплюснутый многогранник, лежит на поверхности (нормаль nn).</summary>
        private static void Crumb(List<Vector3> v, List<Color> c, List<int> t, Vector3 p, Vector3 nn, float s, Color col, System.Random rnd)
        {
            Vector3 a = Vector3.Cross(nn, Mathf.Abs(nn.y) > 0.9f ? Vector3.right : Vector3.up).normalized, b = Vector3.Cross(nn, a);
            float rot = (float)rnd.NextDouble() * 6.283f;
            Vector3 ax = a * Mathf.Cos(rot) + b * Mathf.Sin(rot), bz = Vector3.Cross(nn, ax);
            int i0 = v.Count;
            for (int k = 0; k < 8; k++)
            {
                float jx = 0.7f + 0.6f * (float)rnd.NextDouble(), jz = 0.7f + 0.6f * (float)rnd.NextDouble();
                Vector3 q = p + ax * ((k & 1) == 0 ? -s : s) * jx + nn * ((k & 2) == 0 ? -s * 0.35f : s * 0.35f) + bz * ((k & 4) == 0 ? -s : s) * jz * 0.8f;
                v.Add(q); c.Add(k >= 2 && (k & 2) != 0 ? col : col * 0.9f);
            }
            int[] f = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
            // ориентация граней по факту: наружу от центра крошки
            for (int k = 0; k < f.Length; k += 3)
            {
                Vector3 A = v[i0 + f[k]], B = v[i0 + f[k + 1]], C = v[i0 + f[k + 2]];
                Vector3 fc = (A + B + C) / 3f;
                if (Vector3.Dot(Vector3.Cross(B - A, C - A), fc - p) >= 0f) { t.Add(i0 + f[k]); t.Add(i0 + f[k + 1]); t.Add(i0 + f[k + 2]); }
                else { t.Add(i0 + f[k]); t.Add(i0 + f[k + 2]); t.Add(i0 + f[k + 1]); }
            }
        }

        private static Material _fb;
        private static Material Fallback()
        {
            if (_fb != null) return _fb;
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _fb = new Material(sh) { hideFlags = HideFlags.DontSave };
            return _fb;
        }

#if UNITY_EDITOR
        public void EditorSetMaterial(Material m) { _material = m; }
#endif

        private void OnDestroy()
        {
            if (_shaping && _fpc != null) { _fpc.SpeedMultiplier = 1f; _fpc.ResetLookToCurrent(); }
            if (_mesh != null) Destroy(_mesh);
        }

        // ───────────────────────── ПОДСКАЗКА ─────────────────────────

        private static GUIStyle _style, _small;
        private const int GraphN = 100;                       // точек графика (по 0.4 с — 40 с)
        private readonly float[] _hA = new float[GraphN], _hB = new float[GraphN];
        private int _hN;
        private float _hAt;

        /// <summary>У каждого игрока: копим историю прожарки обеих сторон для графика над котлетой.</summary>
        private void SampleGraph()
        {
            byte fs = _fryState.Value;
            bool active = fs == FS_Dry || fs == FS_Butter || fs == FS_Oil || fs == FS_Both;
            if (!active || Time.time < _hAt) return;
            _hAt = Time.time + 0.4f;
            bool odd = (_flips.Value & 1) != 0;
            if (_hN == GraphN)
            {
                System.Array.Copy(_hA, 1, _hA, 0, GraphN - 1);
                System.Array.Copy(_hB, 1, _hB, 0, GraphN - 1);
                _hN--;
            }
            _hA[_hN] = odd ? FryTop : FryBot;          // сторона 1 — та, что легла вниз первой
            _hB[_hN] = odd ? FryBot : FryTop;
            _hN++;
        }

        private string FryText()
        {
            int fb = Mathf.RoundToInt(FryBot * 100f), ft = Mathf.RoundToInt(FryTop * 100f);
            if (Burnt) return "Котлета <color=#FF8A6A>сгорела</color>";
            if (InGoldBoth) return "Котлета жареная <color=#7CFC6A>✔</color> — на тарелку!";
            if (FryBot >= GoldFrom && FryTop < GoldFrom) return $"Низ золотой ✔ — <color=#FFD36A>переверни ({_flipKey})</color> · верх {ft}%";
            if (FryBot > GoldTo) return $"<color=#FF8A6A>Низ темнеет {fb}%</color> — переверни/сними!";
            if (FriedBoth) return $"Котлета жареная ✔ — снимай (низ {fb}%, верх {ft}%)";
            return $"Жарится низ {fb}% · ещё ~{Mathf.CeilToInt(Mathf.Max(0f, GoldFrom - FryBot) * _sideSeconds)} с · верх {ft}%";
        }

        private void OnGUI()
        {
            if (!_hud || Event.current.type != EventType.Repaint || _food == null || _food.IsConsumed || _food.IsHeld) return;
            Camera cam = Camera.main;
            if (cam == null || _mesh == null) return;
            Bounds mb = _mesh.bounds;
            Vector3 w = transform.TransformPoint(new Vector3(mb.center.x, mb.max.y, mb.center.z)) + Vector3.up * 0.3f;
            if ((w - cam.transform.position).sqrMagnitude > 100f) return;
            Vector3 s = cam.WorldToScreenPoint(w);
            if (s.z <= 0f) return;
            string text; float bar = -1f;
            int top = Mathf.RoundToInt(CoatTop01 * 100f), bot = Mathf.RoundToInt(CoatBot01 * 100f);
            byte fs = _fryState.Value;
            bool fried = FryTop > 0.01f || FryBot > 0.01f;
            bool active = fs == FS_Dry || fs == FS_Butter || fs == FS_Oil || fs == FS_Both;
            bool graph = _hN > 1 || active;
            if (fs == FS_Water) text = "<color=#FF8A6A>В воде/молоке котлету не жарят</color> — нужна сковорода/кастрюля с маслом";
            else if (fs == FS_Raw) text = "Сначала сформуй котлету (мастерок, ЛКМ зажать и по кругу)";
            else if (fs == FS_Cold) text = (fried ? FryText() : "Котлета в сковороде") + "\n<color=#FFD36A>Огня нет — поставь на огонь (или ещё греется)</color>";
            else if (active || fried)
            {
                text = FryText();
                if (active && !Burnt)
                {
                    if (fs == FS_Dry) text += "\n<color=#FF8A6A>Сухо — пригорает! Налей масло</color>";
                    else if (fs == FS_Butter) text += "\n<color=#FFD36A>Только сливочное — добавь подсолнечное (★)</color>";
                    else if (fs == FS_Oil) text += "\n<color=#FFD36A>Только подсолнечное — добавь сливочное (★)</color>";
                }
            }
            else if (Breaded) text = "Котлета в панировке <color=#7CFC6A>✔</color> — на сковороду с маслом (ПКМ по сковороде)";
            else if (Shaped)
            {
                bar = (CoatTop01 + CoatBot01) * 0.5f;
                if (_loose.Value > 0) text = $"Сухари сверху — прихлопни мастерком (ЛКМ) · сверху {top}% · снизу {bot}%";
                else if (CoatTop01 >= 0.8f) text = $"Сверху {top}% ✔ — переверни ({_flipKey}) и вторую сторону · снизу {bot}%";
                else text = $"Панировка: сухари мастерком на котлету (ПКМ) · сверху {top}% · снизу {bot}%";
            }
            else if (_food.Count < _minPortions) text = $"Комок фарша {_food.Count}/{_minPortions} порц. — брось ещё мастерок (ПКМ по комку)";
            else { bar = Shape01; text = _shaping ? $"Формуешь… {Mathf.RoundToInt(Shape01 * 100f)}% — води по кругу" : $"Формовать: мастерок, ЛКМ зажать и водить по кругу — {Mathf.RoundToInt(Shape01 * 100f)}%"; }

            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = false, richText = true };
            if (_small == null) _small = new GUIStyle(GUI.skin.label) { fontSize = 10, richText = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0) };
            Vector2 size = _style.CalcSize(new GUIContent(text));
            const float GW = 250f, GH = 74f;
            float bw = Mathf.Max(size.x + 12f, graph ? GW + 16f : 160f);
            float gh = graph ? GH + 22f : 0f;
            float bh = size.y + (bar >= 0f ? 14f : 4f) + gh;
            Rect box = new Rect(s.x - bw * 0.5f, Screen.height - s.y - bh, bw, bh);
            Color was = GUI.color;
            if (graph)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(box.x, box.y, box.width, gh), Texture2D.whiteTexture);
                GUI.color = was;
                DrawGraph(new Rect(box.x + (box.width - GW) * 0.5f, box.y + 4f, GW, GH));
                box = new Rect(box.x, box.y + gh, box.width, bh - gh);
            }
            GUI.Box(box, text, _style);
            if (bar < 0f) return;
            Rect r = new Rect(box.x + 6f, box.yMax - 9f, box.width - 12f, 6f);
            GUI.color = new Color(0f, 0f, 0f, 0.65f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Shaped ? new Color(0.90f, 0.70f, 0.40f) : new Color(0.95f, 0.6f, 0.2f);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(bar), r.height), Texture2D.whiteTexture);
            GUI.color = was;
        }

        /// <summary>График прожарки: по X — время (40 с), по Y — прожарка 0..180%. Зелёная полоса — золотая корочка, красная — горит.
        /// Оранжевая линия — сторона 1, жёлтая — сторона 2; толстая — та, что сейчас внизу (жарится).</summary>
        private void DrawGraph(Rect g)
        {
            const float YMax = 1.8f;
            Color was = GUI.color;
            GUI.color = new Color(0.12f, 0.10f, 0.08f, 0.9f); GUI.DrawTexture(g, Texture2D.whiteTexture);
            float yGold0 = g.yMax - g.height * (GoldFrom / YMax), yGold1 = g.yMax - g.height * (GoldTo / YMax), yBurn = g.yMax - g.height * (BurnAt / YMax);
            GUI.color = new Color(0.30f, 0.75f, 0.30f, 0.45f); GUI.DrawTexture(new Rect(g.x, yGold1, g.width, yGold0 - yGold1), Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.25f, 0.15f, 0.45f); GUI.DrawTexture(new Rect(g.x, g.y, g.width, yBurn - g.y), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            for (int k = 1; k < 4; k++) GUI.DrawTexture(new Rect(g.x + g.width * k / 4f, g.y, 1f, g.height), Texture2D.whiteTexture);   // каждые 10 с
            GUI.color = was;
            bool odd = (_flips.Value & 1) != 0;                       // внизу сейчас сторона 2?
            Color cA = new Color(1f, 0.55f, 0.15f), cB = new Color(1f, 0.92f, 0.35f);
            float dx = g.width / (GraphN - 1);
            for (int i = 1; i < _hN; i++)
            {
                Vector2 a0 = new Vector2(g.x + (i - 1) * dx, g.yMax - g.height * Mathf.Min(_hA[i - 1], YMax) / YMax);
                Vector2 a1 = new Vector2(g.x + i * dx, g.yMax - g.height * Mathf.Min(_hA[i], YMax) / YMax);
                Vector2 b0 = new Vector2(g.x + (i - 1) * dx, g.yMax - g.height * Mathf.Min(_hB[i - 1], YMax) / YMax);
                Vector2 b1 = new Vector2(g.x + i * dx, g.yMax - g.height * Mathf.Min(_hB[i], YMax) / YMax);
                Line(b0, b1, odd ? 3f : 1.5f, cB);
                Line(a0, a1, odd ? 1.5f : 3f, cA);
            }
            if (_hN > 0)
            {
                float x = g.x + (_hN - 1) * dx;
                GUI.color = new Color(1f, 1f, 1f, 0.5f); GUI.DrawTexture(new Rect(x, g.y, 1f, g.height), Texture2D.whiteTexture); GUI.color = was;
            }
            GUI.Label(new Rect(g.x + 3f, yGold0 - 12f, 80f, 12f), "<color=#BFFFBF>золотая</color>", _small);
            GUI.Label(new Rect(g.x + 3f, g.y + 1f, 80f, 12f), "<color=#FFB0A0>горит</color>", _small);
            float ly = g.yMax + 3f;
            int a = Mathf.RoundToInt((odd ? FryTop : FryBot) * 100f), b = Mathf.RoundToInt((odd ? FryBot : FryTop) * 100f);
            GUI.Label(new Rect(g.x, ly, g.width * 0.5f, 14f), $"<color=#FF8C26>━ сторона 1: {a}%</color>" + (odd ? "" : " ▼"), _small);
            GUI.Label(new Rect(g.x + g.width * 0.5f, ly, g.width * 0.5f, 14f), $"<color=#FFEB59>━ сторона 2: {b}%</color>" + (odd ? " ▼" : ""), _small);
        }

        private static void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            Matrix4x4 m = GUI.matrix;
            Color was = GUI.color;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len + 0.5f, width), Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = was;
        }
    }
}
