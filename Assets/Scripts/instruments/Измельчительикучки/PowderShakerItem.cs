using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// БАНОЧКА-ПОСЫПАЛКА — закваска (и любая сыпучая приправа: соль, перец, сахар).
    ///
    /// ОДНА ДОЗА НА НАЖАТИЕ: смотришь на тару и жмёшь ПКМ (один раз, не держать) —
    /// рука вытягивает баночку над горловиной, наклоняет её вбок и трижды
    /// встряхивает. Это ОДНА доза продукта. Сколько доз нужно таре — решает
    /// FoodCulture (ведру — одна; перебор — сметана перекиснет).
    /// Смотришь мимо тары — доза сыплется на пол.
    /// Это щепотка: места в таре не занимает — сыплется и в полное ведро.
    /// Закваска дальше работает сама (FoodCulture).
    ///
    /// Вид у всех игроков одинаковый: струйка порошка, облачка пыли у горлышка
    /// при каждом встряхивании и там, где порошок падает, круги на жидкости.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
    /// Mouth — дочерний объект у горлышка, СИНЯЯ ось (Z) смотрит из горлышка наружу.
    /// Баночка в руке стоит горлышком вверх; наклон — вокруг Tilt Axis.
    /// </summary>
    public class PowderShakerItem : PickupItem
    {
        [Header("Что сыплет")]
        [Tooltip("Продукт: Food_Закваска (или соль, перец, сахар)")]
        [SerializeField] private ProductDef _product;
        [Min(1)][SerializeField] private int _capacity = 30;
        [Tooltip("Бесконечная — для тестов")]
        [SerializeField] private bool _infinite = true;
        [Tooltip("Порций продукта в одной дозе (одно нажатие ПКМ)")]
        [Min(1)][SerializeField] private int _portionsPerDose = 1;
        [Tooltip("Сколько длится доза целиком (вытянуть руку + встряхивания), с")]
        [Range(0.6f, 3f)][SerializeField] private float _doseSeconds = 1.5f;
        [Tooltip("Встряхиваний за одну дозу")]
        [Range(1, 6)][SerializeField] private int _shakesPerDose = 3;

        [Header("Прицел и рука")]
        [Tooltip("Докуда тянется рука с баночкой от камеры, м")]
        [SerializeField] private float _reach = 2.2f;
        [Tooltip("На какой высоте над горловиной тары держится горлышко, м")]
        [SerializeField] private float _aboveMouth = 0.18f;
        [Tooltip("Мимо тары: баночка перед камерой на таком расстоянии, м")]
        [SerializeField] private float _freeDistance = 0.75f;
        [Tooltip("Как быстро рука вытягивается, 1/с")]
        [SerializeField] private float _tiltSpeed = 5f;
        [Tooltip("Слои, где искать тару прицелом")]
        [SerializeField] private LayerMask _aimMask = ~0;
        [Tooltip("Размах тряски, градусы")]
        [Range(0f, 30f)][SerializeField] private float _shakeAngle = 14f;
        [Tooltip("В какую сторону наклонять баночку: −1 влево, +1 вправо")]
        [Range(-1f, 1f)][SerializeField] private float _tiltSide = -1f;
        [Tooltip("Насколько сильно вбок (0 — горлышком строго вниз)")]
        [Range(0f, 1.5f)][SerializeField] private float _sideAmount = 0.7f;

        [Header("Куда сыплется")]
        [Tooltip("Горлышко: СИНЯЯ ось (Z) смотрит наружу")]
        [SerializeField] private Transform _mouth;
        [Tooltip("Тара, горловина которой ближе этого к месту падения, ловит порошок, м")]
        [SerializeField] private float _captureRadius = 0.45f;
        [SerializeField] private LayerMask _surfaceMask = ~0;

        [Header("Вид")]
        [Tooltip("Цвет порошка. Прозрачный — Base Color продукта")]
        [SerializeField] private Color _powderColor = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Крупинок в секунду в струйке")]
        [SerializeField] private float _grainsPerSecond = 90f;
        [Tooltip("Размер облачков пыли")]
        [Range(0.1f, 3f)][SerializeField] private float _puffScale = 0.6f;
        [Tooltip("Облачко там, куда падает порошок, раз в столько секунд")]
        [SerializeField] private float _landPuffEvery = 0.22f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ushort> _left = new SyncVar<ushort>(0);
        private readonly SyncVar<bool> _shaking = new SyncVar<bool>(false);
        /// <summary>Тара под прицелом (её выбирает владелец по камере).</summary>
        private readonly SyncVar<NetworkObject> _target = new SyncVar<NetworkObject>();
        private Cookware _aimCw;           // владелец: что под прицелом сейчас

        private float _tilt, _acc, _shakePhase, _puffT, _mouthPuffT, _doseT, _shakeStart;
        private bool _delivered, _shakingWas;
        private Cookware _doseCw;          // владелец: тара, в которую идёт текущая доза
        private ParticleSystem _grains;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private const float PourFrom = 0.8f;   // с какой доли вытягивания сыплется

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _left.Value = (ushort)Mathf.Clamp(_capacity, 0, ushort.MaxValue);
        }

        private bool HasPowder => _infinite || _left.Value > 0;
        private bool Pouring => _shaking.Value && _tilt >= PourFrom && HasPowder && _product != null;
        private Transform Mouth => _mouth != null ? _mouth : transform;
        private Color PowderColor
            => _powderColor.a > 0.01f ? _powderColor : (_product != null ? _product.BaseColor : new Color(0.95f, 0.93f, 0.88f));

        /// <summary>Доля дозы, отведённая на «вытянуть руку», остальное — встряхивания.</summary>
        private const float ReachPart = 0.3f;

        private void Update()
        {
            if (base.IsOwner) OwnerInput();
            bool sh = _shaking.Value;
            if (sh && !_shakingWas) _shakeStart = Time.time;
            _shakingWas = sh;
            _tilt = Mathf.MoveTowards(_tilt, sh ? 1f : 0f, Time.deltaTime * _tiltSpeed);
            // Фаза встряхиваний: ровно Shakes Per Dose раз после того, как рука вытянулась.
            float shakeTime = _doseSeconds * (1f - ReachPart);
            float tt = Time.time - _shakeStart - _doseSeconds * ReachPart;
            _shakePhase = sh && tt > 0f ? Mathf.Min(tt / shakeTime, 1f) * _shakesPerDose * Mathf.PI * 2f : 0f;
            if (base.IsServerInitialized) ServerDose(Time.deltaTime);
            UpdateVisual(Time.deltaTime);
        }

        private void OwnerInput()
        {
            if (!_shaking.Value) _aimCw = AimCookware();
            if (Input.GetMouseButtonDown(1) && !_shaking.Value)
            {
                _doseCw = _aimCw;
                CmdDose(_doseCw != null ? _doseCw.NetworkObject : null);
            }
        }

        /// <summary>Тара под прицелом камеры в пределах Reach.</summary>
        private Cookware AimCookware()
        {
            Camera cam = Camera.main;
            if (cam == null) return null;
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _hits, _reach, _aimMask,
                                            QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Cookware res = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(transform)) continue;
                if (_hits[i].collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                Cookware cw = _hits[i].collider.GetComponentInParent<Cookware>();
                if (cw == null || _hits[i].distance >= best) continue;
                best = _hits[i].distance; res = cw;
            }
            return res;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdDose(NetworkObject target)
        {
            if (_shaking.Value || !HasPowder || _product == null) return;
            _target.Value = target;
            _shaking.Value = true;
            _doseT = 0f;
            _delivered = false;
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            if (_shaking.Value) _shaking.Value = false;      // бросили — перестала сыпать
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        /// <summary>Одна доза: после третьего встряхивания порции уходят в тару, баночка возвращается.</summary>
        private void ServerDose(float dt)
        {
            if (!_shaking.Value) return;
            _doseT += dt;
            if (!_delivered && _doseT >= _doseSeconds * 0.85f)
            {
                _delivered = true;
                for (int i = 0; i < _portionsPerDose && HasPowder; i++)
                {
                    Deliver();
                    if (!_infinite && _left.Value > 0) _left.Value = (ushort)(_left.Value - 1);
                }
            }
            if (_doseT >= _doseSeconds) _shaking.Value = false;
        }

        private void Deliver()
        {
            // Тара под прицелом — сыплем прямо в неё (рука над ней). Проверка: не дальше вытянутой руки.
            Cookware cw = null;
            if (_target.Value != null)
            {
                Cookware t = CookwareLookup.Of(_target.Value, Mouth.position);
                if (t != null && Vector3.Distance(t.MouthPoint, Mouth.position) < 1.2f) cw = t;
            }
            if (cw == null && Trace(out Vector3 point, out Cookware direct))
                cw = direct != null ? direct : NearestCookware(point);
            if (cw == null)
            {
                if (_log) Debug.Log($"[Посыпалка] {_product.Title} мимо тары — на пол", this);
                return;
            }
            // Щепотка: места не занимает — сыплется и в полное ведро.
            cw.ServerAddPinch(_product.Id, _product.StartState, 1, 0);
            if (_log && cw.Def != null) Debug.Log($"[Посыпалка] +1 {_product.Title} в «{cw.Def.Title}»", this);
        }

        // ───────────────────────── КУДА ПАДАЕТ ─────────────────────────

        /// <summary>Порошок вылетает из горлышка медленно и падает почти отвесно.</summary>
        private bool Trace(out Vector3 point, out Cookware direct)
        {
            direct = null;
            Transform m = Mouth;
            Vector3 pos = m.position;
            Vector3 vel = m.forward * 0.5f;
            const float dt = 0.02f;
            for (int i = 0; i < 150; i++)
            {
                vel += Physics.gravity * dt;
                vel *= 1f - 1.2f * dt;                          // порошок тормозит воздух
                Vector3 next = pos + vel * dt;
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
            best = default;
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                if (h.collider.isTrigger && h.collider.GetComponentInParent<Cookware>() == null) continue;
                if (h.distance >= bestD) continue;
                bestD = h.distance; best = h; found = true;
            }
            return found;
        }

        private Cookware NearestCookware(Vector3 point)
        {
            Cookware best = null;
            float bestD = _captureRadius;
            foreach (Cookware c in Cookware.All)
            {
                if (c == null) continue;
                Vector3 m = c.MouthPoint;
                if (point.y > m.y + 0.2f) continue;
                float d = new Vector2(m.x - point.x, m.z - point.z).magnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // ───────────────────────── ВИД (у всех) ─────────────────────────

        private void UpdateVisual(float dt)
        {
            bool on = Pouring;
            if (on && _grains == null)
            {
                var look = new CCRuntimeParticles.Look
                {
                    Lifetime = new Vector2(0.45f, 0.9f), Speed = new Vector2(0.25f, 0.7f),
                    Size = new Vector2(0.004f, 0.009f), Gravity = 0.9f, ConeAngle = 14f,
                    Collide = true, Tumble = 120f, Drag = 1.2f,
                };
                _grains = CCRuntimeParticles.CreateStream(transform, look);
            }
            if (_grains == null) return;

            Transform m = Mouth;
            Color c = PowderColor;
            Color c2 = Color.Lerp(c, Color.white, 0.4f);
            // Тряска рывками: порошок высыпается толчками, а не ровной струёй.
            // три толчка за дозу: сыплется на каждом рывке вниз
            float pulse = _shakePhase > 0f ? Mathf.Pow(Mathf.Max(0f, Mathf.Sin(_shakePhase)), 2f) : 0f;
            CCRuntimeParticles.SetStream(_grains, on, m.position, m.forward, c, c2, _grainsPerSecond * pulse);
            if (!on) return;

            // облачко у горлышка на каждом встряхивании
            _mouthPuffT -= dt;
            if (_mouthPuffT <= 0f && pulse > 0.9f)
            {
                _mouthPuffT = _doseSeconds * (1f - ReachPart) / Mathf.Max(1, _shakesPerDose) * 0.8f;   // одно облачко на встряхивание
                Puff(m.position + m.forward * 0.02f, m.forward, 0.55f);
            }

            // там, куда падает
            _puffT -= dt;
            if (_puffT <= 0f)
            {
                _puffT = _landPuffEvery * Random.Range(0.8f, 1.25f);
                Cookware tcw = CookwareLookup.Of(_target.Value, transform.position);
                Vector3 p;
                Cookware direct = null;
                bool hitSomething;
                if (tcw != null)
                {
                    // сыплем в тару под прицелом — точка под горлышком на уровне горловины
                    p = new Vector3(Mouth.position.x, tcw.MouthPoint.y, Mouth.position.z);
                    hitSomething = true;
                }
                else hitSomething = Trace(out p, out direct);
                if (hitSomething)
                {
                    Cookware cw = tcw != null ? tcw : (direct != null ? direct : NearestCookware(p));
                    CookwareLiquidVisual lv = cw != null ? cw.GetComponentInChildren<CookwareLiquidVisual>() : null;
                    if (lv != null && lv.HasLiquid && lv.TryGetSurfaceY(p, out float sy))
                    {
                        Vector3 sp = new Vector3(p.x, sy, p.z);
                        lv.Ripple(sp);
                        Puff(sp + Vector3.up * 0.01f, Vector3.up, 0.45f);
                    }
                    else
                    {
                        CookwareHeapVisual hv = cw != null ? cw.GetComponentInChildren<CookwareHeapVisual>() : null;
                        if (hv != null) hv.SetPourPoint(p);
                        Puff(p + Vector3.up * 0.01f, Vector3.up, 0.8f);
                    }
                }
            }
        }

        /// <summary>Облачко пыли: мультяшные клубы, если в сцене есть Dust System, иначе мелкие частицы.</summary>
        private void Puff(Vector3 at, Vector3 dir, float k)
        {
            Color c = Color.Lerp(PowderColor, Color.white, 0.25f);
            CatCooksDustSystem ds = CatCooksDustSystem.Active;
            if (ds != null)
            {
                ds.Emit(CatCooksDustSystem.Effect.Burst, at, Vector3.up, dir * 0.25f,
                        0.35f * k, null, _puffScale * 0.35f, 0.5f * k, c);
                return;
            }
            CCRuntimeParticles.Burst(at, dir, c, Color.Lerp(c, Color.white, 0.5f),
                                     Mathf.RoundToInt(10 * k), CCRuntimeParticles.Look.Dust.Scaled(_puffScale));
        }

        private void OnDestroy()
        {
            if (_grains != null) Destroy(_grains.gameObject);
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            base.UpdateHeldPose(handAnchor);
            if (_tilt <= 0.001f) return;
            float k = Mathf.SmoothStep(0f, 1f, _tilt);

            // Куда вытянуть горлышко: над горловиной тары под прицелом или перед камерой.
            Camera cam = Camera.main;
            Vector3 aimPos;
            Vector3 camFwd = cam != null ? cam.transform.forward : transform.forward;
            Cookware aimCw = _shaking.Value ? _doseCw : _aimCw;
            if (aimCw != null) aimPos = aimCw.MouthPoint + Vector3.up * _aboveMouth;
            else if (cam != null) aimPos = cam.transform.position + camFwd * _freeDistance + Vector3.down * 0.15f;
            else aimPos = transform.position;

            // Горлышко вниз и ВБОК (как сыплют приправу с руки), чуть от себя.
            Vector3 flat = Vector3.ProjectOnPlane(camFwd, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            Vector3 downDir = (Vector3.down + right * (_tiltSide * _sideAmount) + flat * 0.12f).normalized;
            // Встряхивание: короткий рывок наклона дальше вбок и обратно (вокруг взгляда).
            float shake = Mathf.Max(0f, Mathf.Sin(_shakePhase));
            downDir = Quaternion.AngleAxis(-_tiltSide * shake * _shakeAngle, flat) * downDir;

            Quaternion turn = Quaternion.FromToRotation(Mouth.forward, downDir);
            Quaternion rot = Quaternion.Slerp(transform.rotation, turn * transform.rotation, k);
            transform.rotation = rot;
            Vector3 offset = aimPos - Mouth.position;
            transform.position += offset * k + Vector3.down * (shake * 0.02f * k);
        }

        private void OnGUI()
        {
            if (!base.IsOwner || _product == null) return;
            Cookware cw = _aimCw;
            if (cw == null || _shaking.Value) return;
            FoodCulture fc = FoodCulture.Instance;
            string norm = fc != null && fc.Starter == _product ? $"  (норма: {fc.DosesFor(cw)} доз., в таре уже {cw.CountOf(_product.Id)})" : "";
            string t = $"ПКМ — доза: {_product.Title} → {(cw.Def != null ? cw.Def.Title : cw.name)}{norm}";
            var st = new GUIStyle(GUI.skin.box) { fontSize = 14 };
            Vector2 size = st.CalcSize(new GUIContent(t));
            GUI.Box(new Rect(Screen.width * 0.5f - size.x * 0.5f - 6f, Screen.height * 0.5f + 34f, size.x + 12f, 24f), t, st);
        }
    }
}
