using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// АЗОТНЫЙ ПИСТОЛЕТ на шланге от баллона.
    ///
    ///   F        — взять пистолет;
    ///   R        — открыть/закрыть вентиль баллона (наведись на баллон; или свой баллон рядом);
    ///   ЛКМ зажать — струя морозного пара. Тара в струе копит холод и замерзает:
    ///              по рецепту — мороженое, случайное — лёд, который потом оттает (FoodFreeze).
    ///
    /// Шланг рисуется сам (провисающая трубка от баллона к пистолету). Длина
    /// ограничена: отойдёшь дальше — баллон поедет за тобой (если его никто не держит),
    /// а струя не идёт, пока шланг перетянут сильнее, чем на треть.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
    /// Tank — баллон (NitrogenTank). Nozzle — дуло, СИНЯЯ ось (Z) по струе.
    /// Hose Socket — где шланг входит в пистолет (пусто — у рукояти).
    /// </summary>
    public class NitrogenGunItem : PickupItem
    {
        [Header("Связи")]
        [SerializeField] private NitrogenTank _tank;
        [Tooltip("Дуло: синяя ось по струе")]
        [SerializeField] private Transform _nozzle;
        [Tooltip("Где шланг входит в пистолет")]
        [SerializeField] private Transform _hoseSocket;

        [Header("Шланг")]
        [SerializeField] private float _hoseLength = 4f;
        [Tooltip("Толщина шланга, м")]
        [SerializeField] private float _hoseRadius = 0.018f;
        [SerializeField] private Color _hoseColor = new Color(0.16f, 0.22f, 0.32f);
        [Tooltip("Сила, с которой шланг тянет баллон, если отошёл дальше длины")]
        [SerializeField] private float _pullForce = 60f;

        [Header("Струя")]
        [Tooltip("Дальность струи, м")]
        [SerializeField] private float _range = 1.8f;
        [Tooltip("Ширина конуса, градусы")]
        [Range(5f, 60f)][SerializeField] private float _coneAngle = 26f;
        [Tooltip("Холод в секунду для тары в струе (тара замерзает на Freeze Seconds у FoodFreeze)")]
        [SerializeField] private float _chillPerSecond = 1f;

        [Header("Управление")]
        [SerializeField] private KeyCode _valveKey = KeyCode.R;
        [SerializeField] private float _valveReach = 3f;

        [Header("Вид")]
        [Tooltip("Клубов пара в секунду")]
        [SerializeField] private float _vaporPerSecond = 22f;
        [Tooltip("Размер клубов")]
        [Range(0.2f, 3f)][SerializeField] private float _vaporScale = 1f;

        [Header("Отладка")]
        [Header("v19.5: шланг и баллон как у кровельной горелки")]
        [Tooltip("Баллон стоит, где поставили, — шланг его не тащит. Отошёл дальше длины шланга ×1.25 — струи нет (подойди к баллону)")]
        [SerializeField] private bool _tankStays = true;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<bool> _spraying = new SyncVar<bool>(false);
        // v23.3: куда бьёт струя — туда, куда смотрит держащий (прицел). Шлёт владелец; у остальных и на сервере — из сети.
        private readonly SyncVar<Vector3> _aim = new SyncVar<Vector3>(Vector3.zero);
        private Vector3 _aimSent;
        private float _aimSentT;

        /// <summary>Направление струи: у держащего — взгляд камеры (прицел), у остальных — присланное им; иначе ось Nozzle.</summary>
        private Vector3 JetDir
        {
            get
            {
                if (base.IsOwner && CCHands.HeldByLocal(this))
                {
                    Camera cam = Camera.main;
                    if (cam != null) return cam.transform.forward;
                }
                Vector3 a = _aim.Value;
                return a.sqrMagnitude > 0.5f ? a.normalized : Nozzle.forward;
            }
        }
        private float _noTargetLogT;
        private bool _lmbWas;
        private float _vaporAcc, _groundT;
        private ParticleSystem _crystals;
        private HoseTube _hose;
        private Rigidbody _tankRb;
        private readonly RaycastHit[] _hits = new RaycastHit[8];

        private Transform Nozzle => _nozzle != null ? _nozzle : transform;
        private Vector3 SocketPos => _hoseSocket != null ? _hoseSocket.position : transform.position;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_tank != null) _tankRb = _tank.GetComponent<Rigidbody>();
        }

        /// <summary>Насколько шланг перетянут (1 — ровно длина).</summary>
        private float Stretch => _tank == null ? 99f : Vector3.Distance(_tank.Outlet.position, SocketPos) / Mathf.Max(0.5f, _hoseLength);
        private bool Flowing => _spraying.Value && _tank != null && _tank.IsOpen && Stretch < (_tankStays ? 1.25f : 1.33f);
        /// <summary>v19.5: дальность и холод струи — от давления на редукторе баллона.</summary>
        private float JetRange => _range * (_tank != null ? _tank.PressureK : 1f);

        private void Update()
        {
            if (base.IsOwner) OwnerInput();
            if (base.IsServerInitialized && Flowing) ServerSpray(Time.deltaTime);
            UpdateVisual(Time.deltaTime);
        }

        private void OwnerInput()
        {
            bool lmb = Input.GetMouseButton(0);
            if (lmb != _lmbWas) { _lmbWas = lmb; CmdSpray(lmb); }
            if (lmb && Time.time >= _aimSentT)
            {
                Vector3 d = JetDir;
                if (Vector3.Angle(d, _aimSent) > 2f) { _aimSent = d; _aimSentT = Time.time + 0.08f; CmdAim(d); }
            }
            float wheel = Input.mouseScrollDelta.y;                  // v19.5: давление на редукторе 1…5
            if (Mathf.Abs(wheel) > 0.01f && _tank != null) CmdPressure(wheel > 0f ? 1 : -1);
            if (lmb && _tank != null && _tank.IsOpen && Stretch >= 1.25f && _tankStays && Time.time > _farHintT)
            {
                _farHintT = Time.time + 2f;
                Debug.Log("[Азот] шланг натянут — подойди к баллону (или возьми баллон F и переставь G)", this);
            }

            if (Input.GetKeyDown(_valveKey))
            {
                NitrogenTank target = null;
                Camera cam = Camera.main;
                if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit h, _valveReach,
                                                   ~0, QueryTriggerInteraction.Ignore))
                    target = h.collider.GetComponentInParent<NitrogenTank>();
                if (target == null && _tank != null && Vector3.Distance(_tank.transform.position, transform.position) <= _valveReach)
                    target = _tank;
                if (target != null) CmdToggleValve(target.NetworkObject);
            }
        }

        private float _farHintT;

        [ServerRpc(RequireOwnership = true)]
        private void CmdPressure(int d)
        {
            if (_tank != null) _tank.ServerAddPressure(d);
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdAim(Vector3 dir)
        {
            if (dir.sqrMagnitude > 0.5f) _aim.Value = dir.normalized;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdSpray(bool on)
        {
            if (_spraying.Value != on) _spraying.Value = on;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdToggleValve(NetworkObject tankObj)
        {
            if (tankObj == null) return;
            if (Vector3.Distance(tankObj.transform.position, transform.position) > _valveReach + 2f) return;
            NitrogenTank t = tankObj.GetComponent<NitrogenTank>();
            if (t != null) t.ServerToggleValve();
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            if (_spraying.Value) _spraying.Value = false;     // бросили — струя прекратилась
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void ServerSpray(float dt)
        {
            FoodFreeze ff = FoodFreeze.Instance;
            if (ff == null) return;
            Transform n = Nozzle;
            float tan = Mathf.Tan(_coneAngle * Mathf.Deg2Rad);
            int hit = 0;
            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null) continue;
                // Тара в струе: горловина близко к оси струи (с запасом на ширину тары), не дальше Range.
                Vector3 to = cw.MouthPoint - n.position;
                Vector3 jd = JetDir;
                float along = Vector3.Dot(to, jd);
                if (along < -0.1f || along > JetRange + 0.4f) continue;
                float off = (to - jd * along).magnitude;
                float allow = Mathf.Max(0.12f, along * tan) + 0.3f;          // радиус струи + полтары
                if (off > allow) continue;
                ff.ServerChill(cw, dt * _chillPerSecond * (_tank != null ? _tank.PressureK : 1f));
                hit++;
            }
            if (hit == 0 && _log && Time.time > _noTargetLogT)
            {
                _noTargetLogT = Time.time + 2f;
                Debug.Log($"[Азот] струя ни в одну тару не попадает — наведи дуло на горловину ближе {_range:0.#} м " +
                          "(синяя ось Nozzle должна смотреть по струе)", this);
            }
        }

        private void FixedUpdate()
        {
            // Шланг короче — баллон едет за пистолетом (если его не держат). v19.5: по умолчанию баллон стоит.
            if (_tankStays || !base.IsServerInitialized || _tank == null || _tankRb == null || _tankRb.isKinematic) return;
            Vector3 to = SocketPos - _tank.Outlet.position;
            float excess = to.magnitude - _hoseLength;
            if (excess <= 0.05f) return;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) return;
            _tankRb.AddForce(to.normalized * Mathf.Min(_pullForce * excess, _pullForce * 2f), ForceMode.Force);
        }

        // ───────────────────────── ВИД (у всех) ─────────────────────────

        private void UpdateVisual(float dt)
        {
            if (_tank != null)
            {
                // v19.5: шланг как у кровельной горелки — провисает и ложится на пол, натянулся — прямой и краснеет
                if (_gasHose == null) _gasHose = GasHose.Create(transform);
                _gasHose.Length = _hoseLength;
                _gasHose.Radius = _hoseRadius;
                _gasHose.Color = _hoseColor;
                Vector3 sockDir = _hoseSocket != null ? -_hoseSocket.forward : -Nozzle.forward;
                _gasHose.Tick(SocketPos, sockDir, _tank.Outlet.position, _tank.Outlet.up, transform, _tank.transform);
            }

            bool on = Flowing;
            Transform n = Nozzle;
            if (false && on && _crystals == null)                      // v19.5: «кубики» заменены снежинками (CCFrostFx)
            {
                var look = new CCRuntimeParticles.Look
                {
                    Lifetime = new Vector2(0.25f, 0.5f), Speed = new Vector2(3.5f, 6.5f),
                    Size = new Vector2(0.004f, 0.009f), Gravity = 0.2f, ConeAngle = _coneAngle * 0.6f,
                    Collide = false, Tumble = 360f, Drag = 3f, Stretch = 1.2f,
                };
                _crystals = CCRuntimeParticles.CreateStream(transform, look);
            }
            if (_crystals != null)
                CCRuntimeParticles.SetStream(_crystals, on, n.position, n.forward, Color.white, new Color(0.75f, 0.9f, 1f), 140f);
            if (!on) { _vaporAcc = 0f; _flakeAcc = 0f; return; }

            // v23.2: луч заморозки «как у Мэй»: ледяной пар вылетает и тормозит, яркое ядро, разные снежинки потоком, искры
            float pk = _tank != null ? Mathf.InverseLerp(0.4f, 2.2f, _tank.PressureK) : 0.6f;
            CCFreezeFx.Beam(n.position, JetDir, JetRange * _vaporScale, _coneAngle, pk, dt, ref _beamAcc);

            // Где струя упирается — пар стелется и растекается (азот тяжелее воздуха).
            _groundT -= dt;
            if (_groundT <= 0f)
            {
                _groundT = 0.08f;
                int hits = Physics.RaycastNonAlloc(n.position, JetDir, _hits, JetRange, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue; RaycastHit bh = default(RaycastHit); bool found = false;
                for (int i = 0; i < hits; i++)
                {
                    if (_hits[i].transform.IsChildOf(transform)) continue;
                    if (_hits[i].distance < best) { best = _hits[i].distance; bh = _hits[i]; found = true; }
                }
                if (found)
                {
                    CCFreezeFx.Impact(bh.point + bh.normal * 0.02f, bh.normal, JetRange * 0.06f * _vaporScale);   // v23.2: пар кольцом по поверхности, снежинки, иней
                    // круги и «дрожь» на жидкости, если бьёт в тару
                    Cookware hitCw = bh.collider.GetComponentInParent<Cookware>();
                    CookwareLiquidVisual lv = hitCw != null ? hitCw.GetComponentInChildren<CookwareLiquidVisual>() : null;
                    if (lv != null && lv.HasLiquid && lv.TryGetSurfaceY(bh.point, out float sy))
                        lv.Ripple(new Vector3(bh.point.x, sy, bh.point.z));
                }
            }
        }

        private float _flakeAcc;
        private Vector4 _beamAcc;
        private GasHose _gasHose;

        private void OnDestroy()
        {
            if (_crystals != null) Destroy(_crystals.gameObject);
            if (_hose != null) Destroy(_hose.gameObject);
            if (_gasHose != null) Destroy(_gasHose.gameObject);
        }
    }

    /// <summary>Морозный пар: мультяшные клубы (Dust System), если есть, иначе мягкие частицы.</summary>
    public static class NitroFx
    {
        private static readonly Color Cold = new Color(0.88f, 0.95f, 1f);

        public static void Vapor(Vector3 at, Vector3 velocity, float scale)
        {
            // v23.2: свой мягкий ледяной пар (без кубиков и без нового объекта на каждый клуб)
            Color c = Color.Lerp(CCFreezeFx.IceBlue, CCFreezeFx.IceWhite, Random.Range(0.4f, 1f)); c.a = 0.6f;
            CCFreezeFx.Mist(at, velocity, 0.12f * scale, Random.Range(0.6f, 1.1f), c);
        }
    }

    /// <summary>
    /// Шланг: провисающая трубка между двумя точками. Считается у каждого игрока сам
    /// по позициям концов (по сети ничего не идёт). Чем ближе концы — тем сильнее провис.
    /// </summary>
    public sealed class HoseTube : MonoBehaviour
    {
        private const int Seg = 24, Sides = 8;
        private Mesh _mesh;
        private Vector3[] _v;
        private float _r;
        private readonly Vector3[] _pts = new Vector3[Seg + 1];

        public static HoseTube Create(Transform owner, float radius, Color color)
        {
            var go = new GameObject("Hose (auto)") { hideFlags = HideFlags.DontSave, layer = owner.gameObject.layer };
            var h = go.AddComponent<HoseTube>();
            h._r = radius;
            h._mesh = new Mesh { name = "Hose", hideFlags = HideFlags.DontSave };
            h._mesh.MarkDynamic();
            h._v = new Vector3[(Seg + 1) * Sides];
            var tri = new int[Seg * Sides * 6];
            int t = 0;
            for (int s = 0; s < Seg; s++)
                for (int k = 0; k < Sides; k++)
                {
                    int a = s * Sides + k, b = s * Sides + (k + 1) % Sides, c = a + Sides, d = b + Sides;
                    tri[t++] = a; tri[t++] = c; tri[t++] = b;
                    tri[t++] = b; tri[t++] = c; tri[t++] = d;
                }
            h._mesh.vertices = h._v;
            h._mesh.triangles = tri;
            go.AddComponent<MeshFilter>().sharedMesh = h._mesh;
            var mr = go.AddComponent<MeshRenderer>();
            Shader sh = Shader.Find("Custom/HoseURP");
            if (sh == null) sh = Shader.Find("CatCooks/Toon Props");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh) { name = "Hose (auto)", hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            mr.sharedMaterial = m;
            return h;
        }

        public void Tick(Vector3 a, Vector3 b, float length)
        {
            float d = Vector3.Distance(a, b);
            // провис параболой: длина шланга минус расстояние → глубина
            float sag = d < length ? Mathf.Sqrt(Mathf.Max(0f, length * length - d * d)) * 0.35f : 0f;
            float floorY = Mathf.Min(a.y, b.y) - 5f;
            Vector3 mid = (a + b) * 0.5f;
            if (Physics.Raycast(mid + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                floorY = hit.point.y + _r;
            for (int i = 0; i <= Seg; i++)
            {
                float t = i / (float)Seg;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= sag * 4f * t * (1f - t);
                if (p.y < floorY) p.y = floorY;          // лежит на полу, а не уходит под него
                _pts[i] = p;
            }
            for (int i = 0; i <= Seg; i++)
            {
                Vector3 fwd = (i < Seg ? _pts[i + 1] - _pts[i] : _pts[i] - _pts[i - 1]);
                if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 side = Vector3.Cross(fwd, Vector3.up);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                side.Normalize();
                Vector3 up = Vector3.Cross(side, fwd);
                for (int k = 0; k < Sides; k++)
                {
                    float ang = k / (float)Sides * Mathf.PI * 2f;
                    _v[i * Sides + k] = _pts[i] + (side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * _r;
                }
            }
            _mesh.vertices = _v;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }
    }
}
