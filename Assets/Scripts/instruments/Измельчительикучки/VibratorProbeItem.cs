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
    /// Решает сервер. Кабель длиннее Cable Length у установки — щуп не работает.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
    /// Station — установка. Tip — кончик щупа (он опускается в тару).
    /// Cable Point — где крепится кабель (пусто — корень).
    /// </summary>
    public class VibratorProbeItem : PickupItem
    {
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

        [Header("Прицел")]
        [SerializeField] private float _range = 2.4f;
        [SerializeField] private LayerMask _cookwareMask = ~0;

        [Header("Вид")]
        [Tooltip("Дрожь щупа, когда установка включена")]
        [Range(0f, 0.02f)][SerializeField] private float _vibration = 0.004f;
        [Tooltip("Насколько щуп опускается к горловине тары при фокусе, м")]
        [Range(0f, 1f)][SerializeField] private float _dip = 0.35f;
        [SerializeField] private float _dipSpeed = 8f;
        [SerializeField] private float _splashPerSecond = 40f;


        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        // ───────── сеть ─────────
        private readonly SyncVar<NetworkObject> _focus = new SyncVar<NetworkObject>();
        /// <summary>Сейчас реально взбивает (двигают мышью) — для брызг у всех.</summary>
        private readonly SyncVar<bool> _churning = new SyncVar<bool>(false);

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
                if (_mouseAcc > 0.01f) CmdChurn(focus, _mouseAcc);
                _mouseAcc = 0f;
            }
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
        private void CmdChurn(NetworkObject target, float mouse)
        {
            if (target == null) { WarnOnce("сервер: пустая цель"); return; }
            if (!_held2.Value) { WarnOnce("сервер: щуп не включён (ЛКМ не дошла до сервера)"); return; }
            float far = (target.transform.position - transform.position).magnitude;
            if (far > _range + 2f) { WarnOnce($"сервер: тара дальше {_range + 2f:0.#} м ({far:0.#} м)"); return; }
            Cookware cw = CookwareLookup.Of(target, transform.position);
            if (cw == null) { WarnOnce($"сервер: на «{target.name}» нет Cookware (ни в корне, ни в детях)"); return; }

            // Не быстрее двойной скорости (мышью можно ускорить, но не «рвануть»).
            float work = Mathf.Min(mouse, 0.2f * _workPerSecond + 0.02f);
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

            if (!anyRule)
            {
                // Правила не подошли (или их нет) — берём пару из жидкости: молоко (Liquid) → сливки (TopLayer).
                CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>(true);
                if (lv != null && lv.TryGetChurnPair(cw, out ProductDef from, out ProductDef to, out bool thick))
                {
                    string key = "auto:" + from.Id;
                    if (_warned != key) { _warned = key; if (_log) Debug.Log($"[Вибратор] взбиваю по LiquidAnchor: {from.Title} → {to.Title}" + (thick ? " (густеет)" : ""), this); }
                    Step(cw, from, to, 1, 0, 1f, work, acc, batch, n, thick ? _thickenSeconds : _wholeBatchSeconds);
                    return;
                }
                WarnOnce($"в «{cw.name}» нечего взбивать: всё уже взбито, или правила ({n}) не подходят, " +
                         "а на LiquidAnchor нет пар Liquid → TopLayer → Paste");
            }
            else if (_warned == null || !_warned.StartsWith("auto")) _warned = null;
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
                    CookRecipeBook.FinishNative(cw, CookDriver.Vibrator, fromDef, toDef, $"{toDef.Title} ×{cw.CountOf(toDef.Id)}");
                if (_log) Debug.Log($"[Вибратор] {fromDef.Title} ×{need} → {toDef.Title} · в таре {fromDef.Title}: " +
                                    $"{cw.CountOf(fromDef.Id)}, {toDef.Title}: {cw.CountOf(toDef.Id)}", cw);
            }
            return true;
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
            if (f != null)
            {
                Cookware cw = CookwareLookup.Of(f, transform.position);
                Vector3 mouth = cw != null ? cw.MouthPoint : f.transform.position;
                Vector3 from = _tip != null ? _tip.position - _dipOffset : transform.position - _dipOffset;
                Vector3 d = mouth - from;
                float len = d.magnitude;
                if (len > 0.001f) want = d / len * Mathf.Min(_dip, len);
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
