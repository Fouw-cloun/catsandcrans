using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ПЛОСКОГУБЦЫ — лепка пельменей на раскатанном пласте (кружки вырублены ведром, начинка мастерком).
    ///
    ///   • ЛКМ ДЕРЖАТЬ, глядя на кружок — кружок СКЛАДЫВАЕТСЯ пополам (сгиб — от тебя, шов — к тебе).
    ///   • Не отпуская ЛКМ — ВЕДИ прицел по шву (по дуге края) от одного конца до другого:
    ///     где прошёл — край зажат рифлёным защипом. Внизу экрана — сколько шва зажато.
    ///   • Отпустил — шов зажат (если прошёл ≥75% края; меньше — зажми ещё раз и доведи).
    ///     Ровность — насколько близко к краю вёл. Без начинки — пустышка, много начинки — хуже.
    ///   • ПКМ по защипнутому — концы загибаются «ушком», пельмень готов.
    ///
    /// Решает сервер (DoughSheet.ServerFold / ServerSeal / ServerCurl), анимацию видит сразу тот, кто лепит.
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform (Client Authoritative) · Rigidbody · Collider · этот компонент.
    /// Меню: CatCooks → Пельмени → 6 (выдели модель плоскогубцев или сделаю простые).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class PliersItem : PickupItem
    {
        [Header("Лепка")]
        [SerializeField] private float _range = 3f;
        [Tooltip("За сколько секунд складывается кружок")]
        [SerializeField] private float _foldTime = 0.5f;
        [Tooltip("Насколько далеко от края шва ещё засчитывается, доля радиуса кружка")]
        [Range(0.1f, 0.6f)][SerializeField] private float _seamTolerance = 0.3f;
        [Tooltip("Загиб «ушком», с")]
        [SerializeField] private float _curlTime = 0.35f;

        [Header("Вид в руке")]
        [Tooltip("Насколько плоскогубцы тянутся к прицелу, когда зажимаешь шов (0 — висят в руке)")]
        [Range(0f, 1f)][SerializeField] private float _reachToSeam = 0.85f;
        [SerializeField] private bool _log = true;

        private PlayerHand _hand;
        private float _handScan;
        private DoughSheet _sheet;
        private DoughSheet _hs;          // тесто, с которым идёт сессия ЛКМ / ПКМ
        private int _circle = -1;
        private Vector3 _aimPoint;

        // сессия ЛКМ
        private bool _holding, _foldSent;
        private float _foldT, _yaw;
        private float _cx, _cz;
        private int _mask;
        private float _distSum; private int _distN;
        private Vector3 _pinchPoint;
        private bool _pinchOn;

        // ПКМ
        private float _curlT = -1f;
        private float _curlYaw;

        private string _msg; private bool _msgOk; private float _msgUntil;
        private static GUIStyle _st;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        [Header("Физика (чтобы не улетали)")]
        [Tooltip("Быстрее этого плоскогубцы лежащими не летают, м/с (выталкивание из пола / стола — главная причина «улетели»)")]
        [SerializeField] private float _maxSpeed = 10f;
        [Tooltip("Упали ниже этой высоты (сквозь пол) — вернуть туда, где лежали в начале")]
        [SerializeField] private float _fallLimitY = -30f;

        private Vector3 _homePos;
        private Quaternion _homeRot;
        private bool _homeSet;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_rb != null)
            {
                _rb.maxDepenetrationVelocity = 0.5f;                                   // застрял в полу — вылезает медленно, а не выстреливает
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        private void Start()
        {
            _homePos = transform.position; _homeRot = transform.rotation; _homeSet = true;
        }

        /// <summary>Лежат (не в руке): скорость не выше Max Speed, провалились — домой. Где физика считается — там и правим.</summary>
        private void FixedUpdate()
        {
            if (_rb == null || _rb.isKinematic) return;
            if (!base.IsServerInitialized && !base.IsOwner) return;
            Vector3 v = _rb.linearVelocity;
            if (v.sqrMagnitude > _maxSpeed * _maxSpeed) _rb.linearVelocity = v.normalized * _maxSpeed;
            if (_rb.angularVelocity.sqrMagnitude > 400f) _rb.angularVelocity = _rb.angularVelocity.normalized * 20f;
            if (_homeSet && transform.position.y < _fallLimitY)
            {
                _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero;
                _rb.position = _homePos + Vector3.up * 0.5f; _rb.rotation = _homeRot;
                if (_log) Debug.LogWarning($"[Плоскогубцы] «{name}» провалились под пол — вернул на место. Проверь коллайдер (не внутри пола/стола) и слой", this);
            }
        }

        private bool HeldByMe()
        {
            if (!base.IsOwner) return false;
            if (_hand == null && Time.time >= _handScan)
            {
                _handScan = Time.time + 1f;
                foreach (PlayerHand h in FindObjectsByType<PlayerHand>(FindObjectsSortMode.None))
                    if (h != null && h.IsOwner) { _hand = h; break; }
            }
            return _hand != null && _hand.HeldItem != null && _hand.HeldItem.gameObject == gameObject;
        }

        // ───────────────────────── ВЛАДЕЛЕЦ ─────────────────────────

        private void Update()
        {
            if (!HeldByMe()) { EndLocal(); return; }
            Aim();

            // ПКМ — ушко (короткая анимация у себя, сервер подтверждает)
            if (_curlT >= 0f)
            {
                _curlT += Time.deltaTime;
                if (_hs != null) _hs.LocalAnim(DoughSheet.AnimCurl, _cx, _cz, _curlYaw, _curlT / Mathf.Max(0.05f, _curlTime), 0);
                if (_curlT >= _curlTime) { _curlT = -1f; if (_hs != null) _hs.LocalAnimStop(); }
                return;
            }

            if (!_holding)
            {
                if (Input.GetMouseButtonDown(0) && _sheet != null && _circle >= 0) BeginHold();
                else if (Input.GetMouseButtonDown(1) && _sheet != null && _circle >= 0) Curl();
                return;
            }

            // держим ЛКМ: складывание, потом защип по шву
            if (_hs == null) { EndLocal(); return; }
            DoughSheet.DoughCircle o = CurrentCircle(out bool ok);
            if (!ok) { EndLocal(); return; }
            if (_foldT < 1f)
            {
                _foldT = Mathf.Min(1f, _foldT + Time.deltaTime / Mathf.Max(0.05f, _foldTime));
                _hs.LocalAnim(DoughSheet.AnimFold, _cx, _cz, _yaw, _foldT, 0);
                if (_foldT >= 1f && !_foldSent) { _foldSent = true; CmdFold(_hs.NetworkObject, _cx, _cz, _yaw); }
            }
            else
            {
                TrackSeam(o);
                _hs.LocalAnim(DoughSheet.AnimSeal, _cx, _cz, _yaw, 1f, _mask);
            }
            if (!Input.GetMouseButton(0))
            {
                if (_foldT >= 1f)
                {
                    int bins = 0; for (int b = 0; b < DoughSheet.SealBins; b++) if ((_mask & (1 << b)) != 0) bins++;
                    float cover = bins / (float)DoughSheet.SealBins;
                    float quality = _distN > 0 ? Mathf.Clamp01(1f - (_distSum / _distN) / Mathf.Max(0.01f, _seamTolerance)) : 0f;
                    CmdSeal(_hs.NetworkObject, _cx, _cz, cover, quality);
                    Say(cover >= 0.75f ? $"шов зажат на {cover * 100f:0}%" : $"шов зажат только на {cover * 100f:0}% — зажми ещё раз и веди по всему краю", cover >= 0.75f);
                }
                else Say("отпустил рано — кружок не сложился", false);
                EndLocal(true);
            }
        }

        private DoughSheet.DoughCircle CurrentCircle(out bool ok)
        {
            ok = false;
            for (int i = 0; i < _hs.CircleCount; i++)
            {
                DoughSheet.DoughCircle c = _hs.GetCircle(i);
                if (Mathf.Abs(c.X - _cx) < 0.05f && Mathf.Abs(c.Z - _cz) < 0.05f) { ok = true; return c; }
            }
            return default(DoughSheet.DoughCircle);
        }

        private void BeginHold()
        {
            DoughSheet.DoughCircle o = _sheet.GetCircle(_circle);
            if (o.State == DoughSheet.CircleShaped) { Say("этот пельмень уже готов", true); return; }
            if (o.State == DoughSheet.CircleFlat) { Say("лепёшка — брак", false); return; }
            if (o.State == DoughSheet.CircleSealed) { Say("шов уже зажат — ПКМ: загнуть ушком", true); return; }
            _holding = true; _hs = _sheet; _cx = o.X; _cz = o.Z; _mask = 0; _distSum = 0f; _distN = 0;
            if (o.State == DoughSheet.CircleFolded)
            {
                _foldT = 1f; _foldSent = true; _yaw = o.Dir / 255f * Mathf.PI * 2f;     // уже сложен — сразу защип
            }
            else
            {
                Camera cam = Camera.main;
                Vector3 toMe = cam != null ? cam.transform.position - new Vector3(o.X, cam.transform.position.y, o.Z) : Vector3.back;
                _yaw = Mathf.Atan2(toMe.x, toMe.z);                                          // шов — к игроку
                _foldT = 0f; _foldSent = false;
                if (o.Fill == 0) Say("без начинки — будет пустышка (начинку мастерком)", false);
            }
        }

        private void Curl()
        {
            DoughSheet.DoughCircle o = _sheet.GetCircle(_circle);
            if (o.State != DoughSheet.CircleSealed)
            {
                Say(o.State == DoughSheet.CircleCut ? "сначала сложи: ЛКМ держать по кружку" : o.State == DoughSheet.CircleFolded ? "сначала зажми шов: ЛКМ держать и веди по краю" : o.State == DoughSheet.CircleShaped ? "уже готов" : "лепёшка — брак", false);
                return;
            }
            _hs = _sheet; _cx = o.X; _cz = o.Z; _curlYaw = o.Dir / 255f * Mathf.PI * 2f; _curlT = 0f;
            CmdCurl(_hs.NetworkObject, _cx, _cz);
        }

        /// <summary>Прицел у шва: какая часть дуги (12 участков) пройдена и насколько близко к краю.</summary>
        private void TrackSeam(DoughSheet.DoughCircle o)
        {
            _pinchOn = false;
            if (_hs == null) return;
            float r = o.R * 0.97f, cy = Mathf.Cos(_yaw), sy = Mathf.Sin(_yaw);
            Vector3 d = _aimPoint - new Vector3(o.X, 0f, o.Z);
            float lx = d.x * cy - d.z * sy, lz = d.x * sy + d.z * cy;               // в осях пельменя: x — вдоль сгиба, z — к шву
            float best = float.MaxValue, bs = 0f;
            for (int k = 0; k <= 24; k++)
            {
                float s = -1f + 2f * k / 24f;
                float sx = s * r, sz = r * Mathf.Sqrt(Mathf.Max(0f, 1f - s * s));
                float dd = new Vector2(lx - sx, lz - sz).magnitude;
                if (dd < best) { best = dd; bs = s; }
            }
            float norm = best / Mathf.Max(0.01f, o.R);
            if (norm > _seamTolerance) return;
            int bin = Mathf.Clamp(Mathf.FloorToInt((bs + 1f) * 0.5f * DoughSheet.SealBins), 0, DoughSheet.SealBins - 1);
            _mask |= 1 << bin;
            _distSum += norm; _distN++;
            _pinchPoint = _hs.SeamPoint(o, _yaw, bs);
            _pinchOn = true;
        }

        private void Aim()
        {
            _sheet = null; _circle = -1;
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.collider == null || h.transform.IsChildOf(transform) || h.distance >= best) continue;
                best = h.distance;
                DoughSheet ds = h.collider.GetComponentInParent<DoughSheet>();
                _sheet = ds != null && ds.Kind == DoughKind.Pelmeni ? ds : null;
                _aimPoint = h.point;
            }
            if (_holding && _hs != null && _sheet != _hs)
            {
                // прицел соскочил с теста (пол в дырке, другой кусок) — ведём по плоскости этого теста
                Plane pl = new Plane(Vector3.up, _hs.transform.position + Vector3.up * 0.05f);
                if (pl.Raycast(ray, out float e) && e < _range * 1.5f) { _sheet = _hs; _aimPoint = ray.GetPoint(e); }
            }
            if (_sheet != null) _circle = _sheet.CircleAt(_aimPoint, _holding ? 1.5f : 1.05f);
        }

        private void EndLocal(bool keepAim = false)
        {
            if (_hs != null) _hs.LocalAnimStop();
            _holding = false; _pinchOn = false; _curlT = -1f; _hs = null;
            if (!keepAim) { _sheet = null; _circle = -1; }
        }

        private void Say(string m, bool ok) { _msg = m; _msgOk = ok; _msgUntil = Time.time + 2.5f; if (_log) Debug.Log($"[Плоскогубцы] {m}", this); }

        // ───────────────────────── ВИД В РУКЕ ─────────────────────────

        public override void UpdateHeldPose(Transform handAnchor)
        {
            base.UpdateHeldPose(handAnchor);
            if (!_holding || !_pinchOn || _reachToSeam <= 0f) return;
            Vector3 pos = Vector3.Lerp(transform.position, _pinchPoint + Vector3.up * 0.08f, _reachToSeam);
            Quaternion rot = Quaternion.LookRotation(Vector3.down, handAnchor.forward);       // губками вниз, на шов
            transform.SetPositionAndRotation(pos, Quaternion.Slerp(transform.rotation, rot, _reachToSeam));
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !base.IsOwner || !HeldByMe()) return;
            string text = null;
            if (Time.time < _msgUntil && !string.IsNullOrEmpty(_msg) && !_holding)
                text = (_msgOk ? "<color=#9BE37A>" : "<color=#FF8A5A>") + _msg + "</color>";
            else if (_holding && _foldT < 1f) text = "складываю… держи ЛКМ";
            else if (_holding)
            {
                int bins = 0; for (int b = 0; b < DoughSheet.SealBins; b++) if ((_mask & (1 << b)) != 0) bins++;
                var sb = new System.Text.StringBuilder();
                for (int b = 0; b < DoughSheet.SealBins; b++) sb.Append((_mask & (1 << b)) != 0 ? "█" : "░");
                text = $"веди плоскогубцы по ШВУ (краю): <color=#9BE37A>{sb}</color> {bins * 100 / DoughSheet.SealBins}% · отпусти ЛКМ, когда весь";
            }
            else if (_sheet != null && _circle >= 0)
            {
                DoughSheet.DoughCircle o = _sheet.GetCircle(_circle);
                switch (o.State)
                {
                    case DoughSheet.CircleCut: text = o.Fill > 0 ? "ЛКМ держать — сложить, потом вести по шву" : "ЛКМ держать — сложить (без начинки будет пустышка)"; break;
                    case DoughSheet.CircleFolded: text = "ЛКМ держать — зажать шов (вести по краю)"; break;
                    case DoughSheet.CircleSealed: text = "ПКМ — загнуть ушком"; break;
                    case DoughSheet.CircleShaped: text = "готов ✔"; break;
                }
            }
            if (text == null) return;
            if (_st == null) _st = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
            Rect r = new Rect(Screen.width * 0.5f - 320f, Screen.height * 0.70f, 640f, 30f);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(r, text, _st);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private DoughSheet SheetOf(NetworkObject no, float x, float z)
        {
            DoughSheet ds = no != null ? no.GetComponent<DoughSheet>() : null;
            if (ds == null) return null;
            if ((new Vector3(x, transform.position.y, z) - transform.position).sqrMagnitude > (_range + 4f) * (_range + 4f)) return null;
            return ds;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdFold(NetworkObject sheet, float x, float z, float yaw)
        {
            DoughSheet ds = SheetOf(sheet, x, z);
            if (ds == null) return;
            bool ok = ds.ServerFold(x, z, yaw, out string msg);
            if (!ok && !string.IsNullOrEmpty(msg)) TargetSay(base.Owner, msg, false);
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdSeal(NetworkObject sheet, float x, float z, float cover, float quality)
        {
            DoughSheet ds = SheetOf(sheet, x, z);
            if (ds == null) return;
            bool ok = ds.ServerSeal(x, z, cover, quality, out string msg);
            if (!string.IsNullOrEmpty(msg)) TargetSay(base.Owner, msg, ok);
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdCurl(NetworkObject sheet, float x, float z)
        {
            DoughSheet ds = SheetOf(sheet, x, z);
            if (ds == null) return;
            bool ok = ds.ServerCurl(x, z, out string msg);
            if (!string.IsNullOrEmpty(msg)) TargetSay(base.Owner, msg, ok);
        }

        [TargetRpc]
        private void TargetSay(NetworkConnection conn, string msg, bool ok) => Say(msg, ok);
    }
}
