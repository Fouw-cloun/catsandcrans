using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ВЕДРО-ВЫРУБКА — пельменные кружки из раскатанного теста.
    ///
    /// Вешается на корень ведра рядом с Cookware и CarriedContainer (меню CatCooks → Пельмени → 4, выдели ведро).
    /// Ведро в руке ПУСТОЕ, смотришь на раскатанный пельменный пласт:
    ///   • на тесте — призрак-кольцо (зелёное — можно, красное — нельзя, внизу подсказка почему);
    ///   • ЛКМ ДЕРЖАТЬ — ведро переворачивается и прижимается кромкой к тесту;
    ///   • не отпуская, веди мышь влево/вправо — камера СТОИТ, проворачивается ведро;
    ///     провернул на Twist Needed градусов — хрусть, кружок вырезан. Отпустил раньше — не прорезал.
    /// Ведро не пустое — работает как обычно (ЛКМ льёт). Решает сервер (DoughSheet.ServerStampCircle).
    ///
    /// Радиус — кромка ведра: поле Radius (меню снимает с модели), 0 — сам (LiquidAnchor или габарит).
    /// Кружок «в самый раз» ~0.4 м радиусом при масштабе мира 4: ведро шире/уже — пельмени крупнее/мельче.
    /// </summary>
    [DefaultExecutionOrder(1000)]                      // после PlayerHand (поза в руке) — сверху кладём замах
    [RequireComponent(typeof(Cookware))]
    public sealed class BucketDoughCutter : NetworkBehaviour
    {
        [Tooltip("Радиус кромки ведра = радиус кружка, м. 0 — сам (LiquidAnchor или габарит модели)")]
        [SerializeField] private float _radius;
        [Tooltip("Как далеко достаёт, м (от камеры)")]
        [SerializeField] private float _range = 3.5f;
        [Tooltip("Пауза между ударами, с")]
        [SerializeField] private float _cooldown = 0.7f;
        [Tooltip("Сколько провернуть ведро (поворотом мыши), градусы")]
        [Range(20f, 360f)][SerializeField] private float _twistNeeded = 90f;
        [Tooltip("Градусов проворота ведра на единицу движения мыши (камера в это время стоит)")]
        [Range(0.5f, 20f)][SerializeField] private float _twistPerMouse = 4f;
        [Tooltip("Переворот и прижать к тесту, с")]
        [SerializeField] private float _pressTime = 0.22f;
        [Tooltip("Вернуть в руку, с")]
        [SerializeField] private float _liftTime = 0.25f;
        [SerializeField] private bool _showGhost = true;
        [SerializeField] private bool _log = true;

        private Cookware _cw;
        private float _autoR = -1f, _rimLocalY = 0.3f;
        private LineRenderer _ghost;
        private Material _ghostMat;
        private PlayerHand _hand;
        private float _handScan;
        private DoughSheet _aim;
        private Vector3 _aimPoint;
        private bool _ok;
        private string _why;
        private float _nextAt;
        private bool _pressing, _sent;
        private float _pressT, _liftT = -1f, _twist;
        private FirstPersonController _fpc;
        private DoughSheet _slamSheet;
        private Vector3 _slamPoint;
        private string _resultMsg;
        private bool _resultOk;
        private float _resultUntil;
        private static GUIStyle _st;

        private float S => Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;

        /// <summary>Радиус кружка, м.</summary>
        public float Radius
        {
            get
            {
                if (_radius > 0.01f) return _radius;
                if (_autoR < 0f) MeasureAuto();
                return _autoR;
            }
        }

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            MeasureRim();
        }

        private void MeasureAuto()
        {
            CookwareLiquidVisual lv = GetComponentInChildren<CookwareLiquidVisual>(true);
            if (lv != null && lv.RadiusAtRimWorld > 0.03f) { _autoR = lv.RadiusAtRimWorld; return; }
            Bounds b = LocalBounds(out bool any);
            float sc = Mathf.Abs(transform.lossyScale.x) > 1e-4f ? Mathf.Abs(transform.lossyScale.x) : 1f;
            _autoR = any ? Mathf.Max(b.extents.x, b.extents.z) * sc * 0.95f : 0.4f * S;
        }

        /// <summary>Где кромка в осях ведра (верх модели) — перевёрнутое ведро ставим кромкой на тесто.</summary>
        private void MeasureRim()
        {
            Bounds b = LocalBounds(out bool any);
            float sc = Mathf.Abs(transform.lossyScale.y) > 1e-4f ? Mathf.Abs(transform.lossyScale.y) : 1f;
            _rimLocalY = any ? b.max.y * sc : 0.3f * S;
        }

        private Bounds LocalBounds(out bool any)
        {
            any = false;
            Bounds res = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || (mf.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                bool skip = false;
                for (Transform p = mf.transform; p != null && p != transform; p = p.parent)
                    if (p.name == "Fill" || p.name == "LiquidAnchor") { skip = true; break; }
                if (skip) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                    Vector3 p = transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { res = new Bounds(p, Vector3.zero); any = true; } else res.Encapsulate(p);
                }
            }
            return res;
        }

        private void OnDestroy()
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            if (_ghostMat != null) Destroy(_ghostMat);
        }

        // ───────────────────────── ВЛАДЕЛЕЦ ─────────────────────────

        /// <summary>Ведро в МОЕЙ руке (у локального игрока).</summary>
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

        private void Update()
        {
            if (_liftT >= 0f) { _liftT += Time.deltaTime; if (_liftT >= _liftTime) _liftT = -1f; }
            if (!HeldByMe()) { ShowGhost(false); _aim = null; _pressing = false; _liftT = -1f; return; }

            if (_pressing)
            {
                _pressT += Time.deltaTime;
                // камера стоит (HoldLook), мышь влево-вправо крутит ведро — после отпускания взгляд там же, без рывка
                if (_fpc == null && _hand != null)
                {
                    _fpc = _hand.GetComponentInParent<FirstPersonController>();
                    if (_fpc == null) _fpc = _hand.GetComponentInChildren<FirstPersonController>();
                }
                if (_fpc != null) _fpc.HoldLook();
                if (_pressT >= _pressTime) _twist += Input.GetAxisRaw("Mouse X") * _twistPerMouse;   // крутим, когда ведро уже на тесте
                ShowGhost(false);
                Camera cam = Camera.main;
                bool far = cam != null && (cam.transform.position - _slamPoint).sqrMagnitude > (_range + 1.5f) * (_range + 1.5f);
                if (!_sent && Mathf.Abs(_twist) >= _twistNeeded)
                {
                    _sent = true;
                    if (_slamSheet != null) CmdStamp(_slamSheet.NetworkObject, _slamPoint);
                }
                if (!Input.GetMouseButton(0) || far || _slamSheet == null)
                {
                    if (!_sent)
                    {
                        _resultOk = false; _resultUntil = Time.time + 2.5f;
                        _resultMsg = far ? "отошёл — ведро сорвалось" : $"не провернул ({Mathf.Abs(_twist):0}° из {_twistNeeded:0}°) — зажми ЛКМ и поворачивай мышью";
                        if (_log) Debug.Log($"[Ведро-вырубка] «{name}»: {_resultMsg}", this);
                    }
                    _pressing = false; _liftT = 0f;
                    _nextAt = Time.time + _cooldown;
                }
                return;
            }

            if (_cw != null && !_cw.IsEmpty) { ShowGhost(false); _aim = null; return; }
            Aim();
            if (_liftT < 0f && _aim != null && Input.GetMouseButtonDown(0) && Time.time >= _nextAt)
            {
                if (!_ok)
                {
                    _resultOk = false; _resultMsg = _why; _resultUntil = Time.time + 2f;
                    if (_log) Debug.Log($"[Ведро-вырубка] «{name}»: тут не вырубить — {_why}", this);
                    return;
                }
                _pressing = true; _sent = false; _pressT = 0f; _twist = 0f;
                _slamSheet = _aim; _slamPoint = _aimPoint;
                _pressFromPos = transform.position; _pressFromRot = transform.rotation;
            }
        }

        private Vector3 _pressFromPos;
        private Quaternion _pressFromRot;

        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private void Aim()
        {
            _aim = null;
            Camera cam = Camera.main;
            if (cam == null) { ShowGhost(false); return; }
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.collider == null || h.transform.IsChildOf(transform) || h.distance >= best) continue;
                best = h.distance;
                DoughSheet ds = h.collider.GetComponentInParent<DoughSheet>();
                _aim = ds != null && ds.Kind == DoughKind.Pelmeni ? ds : null;
                _aimPoint = h.point;
            }
            if (_aim == null) { ShowGhost(false); return; }
            _ok = _aim.PreviewStamp(_aimPoint, Radius, out _why);
            DrawGhost(_aim, _aimPoint, Radius, _ok);
        }

        private void DrawGhost(DoughSheet ds, Vector3 at, float r, bool ok)
        {
            if (!_showGhost) return;
            if (_ghost == null)
            {
                var go = new GameObject("BucketCutterGhost (auto)") { hideFlags = HideFlags.DontSave };
                _ghost = go.AddComponent<LineRenderer>();
                _ghost.loop = true; _ghost.useWorldSpace = true; _ghost.positionCount = 48;
                _ghost.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _ghost.receiveShadows = false;
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                _ghostMat = new Material(sh);
                _ghost.sharedMaterial = _ghostMat;
            }
            ShowGhost(true);
            float w = 0.035f * S;
            _ghost.startWidth = w; _ghost.endWidth = w;
            Color c = ok ? new Color(0.45f, 1f, 0.4f, 0.9f) : new Color(1f, 0.35f, 0.25f, 0.9f);
            _ghost.startColor = c; _ghost.endColor = c;
            if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", c);
            if (_ghostMat.HasProperty("_Color")) _ghostMat.SetColor("_Color", c);
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                Vector3 p = new Vector3(at.x + Mathf.Cos(a) * r, at.y, at.z + Mathf.Sin(a) * r);
                float y = ds.HeightAt(p, out float h) ? ds.transform.position.y + h : at.y;
                p.y = y + 0.02f * S;
                _ghost.SetPosition(i, p);
            }
        }

        private void ShowGhost(bool on)
        {
            if (_ghost != null && _ghost.gameObject.activeSelf != on) _ghost.gameObject.SetActive(on);
        }

        /// <summary>Поверх позы в руке: ведро перевёрнуто кромкой на тесте и проворачивается вместе с поворотом мыши.</summary>
        private void LateUpdate()
        {
            if (!base.IsOwner || _slamSheet == null || (!_pressing && _liftT < 0f)) return;
            Vector3 handPos = transform.position;
            Quaternion handRot = transform.rotation;
            float surf = _slamSheet.HeightAt(_slamPoint, out float hh) ? _slamSheet.transform.position.y + hh : _slamPoint.y;
            Vector3 down = new Vector3(_slamPoint.x, surf + _rimLocalY, _slamPoint.z);        // кромка на тесте
            // перевёрнутое ведро — от позы в момент нажатия, по оси «вправо от взгляда»; провёрнуто на _twist вокруг вертикали
            Vector3 fwd = Vector3.ProjectOnPlane(_pressFromRot * Vector3.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd.normalized);
            Quaternion flip = Quaternion.AngleAxis(_twist, Vector3.up) * Quaternion.AngleAxis(180f, right) * _pressFromRot;
            Vector3 pos; Quaternion rot;
            if (_pressing)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_pressT / Mathf.Max(0.05f, _pressTime)));
                Vector3 above = down + Vector3.up * (0.3f * S);
                pos = k < 0.7f ? Vector3.Lerp(handPos, above, k / 0.7f) : Vector3.Lerp(above, down, (k - 0.7f) / 0.3f);
                rot = Quaternion.Slerp(handRot, flip, Mathf.Clamp01(k / 0.7f));
            }
            else
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_liftT / Mathf.Max(0.05f, _liftTime)));
                pos = Vector3.Lerp(down, handPos, k); rot = Quaternion.Slerp(flip, handRot, k);
            }
            transform.SetPositionAndRotation(pos, rot);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !base.IsOwner) return;
            string text = null;
            if (Time.time < _resultUntil && !string.IsNullOrEmpty(_resultMsg))
                text = (_resultOk ? "<color=#9BE37A>кружок ✔</color> " : "<color=#FF8A5A>не вырубил:</color> ") + _resultMsg;
            else if (_pressing)
            {
                float p = Mathf.Clamp01(Mathf.Abs(_twist) / Mathf.Max(1f, _twistNeeded));
                int bars = Mathf.RoundToInt(p * 10f);
                text = _sent ? "<color=#9BE37A>прорезал ✔</color> — отпусти ЛКМ"
                             : $"крути мышью влево/вправо: <color=#9BE37A>{new string('█', bars)}</color>{new string('░', 10 - bars)} {Mathf.Abs(_twist):0}°/{_twistNeeded:0}°";
            }
            else if (_aim != null)
                text = _ok ? $"ЛКМ держать — прижать ведро, мышью провернуть (Ø {Radius * 200f:0} см)" : $"<color=#FF8A5A>{_why}</color>";
            if (text == null) return;
            if (_st == null) _st = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
            Rect r = new Rect(Screen.width * 0.5f - 300f, Screen.height * 0.70f, 600f, 30f);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(r, text, _st);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        [ServerRpc(RequireOwnership = true)]
        private void CmdStamp(NetworkObject target, Vector3 point)
        {
            DoughSheet ds = target != null ? target.GetComponent<DoughSheet>() : null;
            if (ds == null) return;
            if ((point - transform.position).sqrMagnitude > (_range + 4f) * (_range + 4f)) return;
            if (_cw != null && !_cw.IsEmpty) { TargetResult(base.Owner, false, "ведро не пустое — вылей"); return; }
            bool ok = ds.ServerStampCircle(point, Radius, out string msg);
            if (_log) Debug.Log($"[Ведро-вырубка] «{name}»: {(ok ? "провернул — " : "не вырубил: ")}{msg}", this);
            TargetResult(base.Owner, ok, msg);
        }

        [TargetRpc]
        private void TargetResult(NetworkConnection conn, bool ok, string msg)
        {
            _resultOk = ok; _resultMsg = msg; _resultUntil = Time.time + 2.5f;
        }

#if UNITY_EDITOR
        public float EditorRadius => _radius;
        public void EditorSetup(float radius)
        {
            UnityEditor.Undo.RecordObject(this, "Ведро-вырубка");
            _radius = radius;
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
#endif
    }
}
