using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ФОРМА ДЛЯ ХЛЕБА (багет / буханка-кирпичик). Носится в руках как обычный предмет.
    ///
    ///   1. Кинь в форму КУСОК подошедшего теста (после шпателя) — он ложится внутрь
    ///      (видно тесто в форме, бледное, невысокое). Целый шар не ложится — сначала шпатель.
    ///   2. Поставь форму в ДУХОВКУ (BakeOven) на огонь: от Bake From C тесто растёт и
    ///      заполняет форму, румянится; в конце — материал хлеба (корочка) и пар.
    ///      Без крышки — печётся вдвое медленнее.
    ///   3. Готово — F по форме: хлеб выскакивает отдельным предметом (Bread Prefab), горячий, с паром.
    ///      F по пустой форме или с сырым тестом — просто взять форму.
    ///
    /// Внутри формы — дочерний объект Fill (меш хлеба этой формы, низ — у дна формы):
    /// это «тесто в форме» и «хлеб в форме». Его материал = материал готового хлеба.
    /// На корне — триггер-коллайдер внутри формы (ловит кусок теста) и обычный коллайдер корпуса.
    /// </summary>
    public class BreadMold : PickupItem
    {
        [Header("Что печём")]
        [Tooltip("Продукт хлеба (Багет / Буханка)")]
        [SerializeField] private ProductDef _breadProduct;
        [Tooltip("Сетевой префаб готового хлеба (FoodPiece + Food Hot Steam)")]
        [SerializeField] private GameObject _breadPrefab;

        [Header("Тесто в форме")]
        [Tooltip("Меш хлеба внутри формы (низ — у дна). Пусто — дочерний «Fill»")]
        [SerializeField] private Transform _fill;
        [Tooltip("Материал сырого теста. Пусто — материал Fill, только бледнее")]
        [SerializeField] private Material _doughMaterial;
        [Tooltip("Высота сырого теста от высоты хлеба (0.55 — чуть больше половины формы)")]
        [Range(0.2f, 1f)][SerializeField] private float _rawHeight = 0.55f;
        [Tooltip("Цвет сырого теста")]
        [SerializeField] private Color _doughTint = new Color(1f, 0.95f, 0.85f);
        [Tooltip("Цвет на середине выпечки")]
        [SerializeField] private Color _goldTint = new Color(0.95f, 0.72f, 0.42f);
        [Tooltip("Цвет корочки к концу. Если у материала хлеба свой цвет (не белый) — берётся он, чтобы не было скачка")]
        [SerializeField] private Color _crustColor = new Color(0.78f, 0.49f, 0.23f);

        [Header("Выпечка")]
        [Tooltip("С какой температуры духовки печётся, °C")]
        [SerializeField] private float _bakeFromC = 140f;
        [Tooltip("Сколько секунд печь при ~180 °C")]
        [Min(1f)][SerializeField] private float _bakeSeconds = 45f;
        [Tooltip("Пар от хлеба после выпечки, с")]
        [SerializeField] private float _steamSeconds = 20f;

        [Header("Постановка (ЛКМ с формой в руке)")]
        [Tooltip("Как далеко можно поставить, м")]
        [SerializeField] private float _placeReach = 3f;
        [Tooltip("Зазор над решёткой/столом, м")]
        [SerializeField] private float _placeLift = 0.004f;
        [Tooltip("Подсказка «ЛКМ — поставить ровно», пока форма в руках")]
        [SerializeField] private bool _placeHint = true;
        [SerializeField, HideInInspector] private Quaternion _uprightRot = Quaternion.identity;
        [SerializeField, HideInInspector] private bool _hasUpright;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        public enum MoldState : byte { Empty = 0, Dough = 1, Baked = 2 }

        private readonly SyncVar<byte> _state = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _bake = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _portions = new SyncVar<byte>(0);

        private Renderer[] _fillR;
        private Material _breadMat;
        private Vector3 _fillScale;
        private MaterialPropertyBlock _mpb;
        private string _colorProp;
        private Color _crustEnd;
        private float _progress, _hintAt, _steamUntil, _steamAcc;
        private int _lastQuarter = -1;
        private bool _heating;

        public MoldState State => (MoldState)_state.Value;
        public float Bake01 => _bake.Value / 255f;
        /// <summary>Сейчас печётся (для давления в духовке).</summary>
        public bool IsBakingNow => base.IsServerInitialized && _heating;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_fill == null) _fill = transform.Find("Fill");
            if (_fill != null)
            {
                _fillScale = _fill.localScale;
                _fillR = _fill.GetComponentsInChildren<Renderer>(true);
                if (_fillR.Length > 0) _breadMat = _fillR[0].sharedMaterial;
                if (_breadMat != null) _colorProp = _breadMat.HasProperty("_BaseColor") ? "_BaseColor" : _breadMat.HasProperty("_Color") ? "_Color" : null;
            }
            // конец выпечки = цвет материала хлеба, если он задан (не белый), — тогда смена материала без скачка
            Color bc = BakeColor.BaseOf(_breadMat);
            _crustEnd = bc.r + bc.g + bc.b < 2.7f ? bc : _crustColor;
            if (!_hasUpright) { _uprightRot = transform.rotation; _hasUpright = true; }
            else Debug.LogWarning($"[Форма] «{name}»: нет Fill (меша хлеба внутри формы) — тесто в форме видно не будет", this);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _state.OnChange += OnStateChanged;
            _bake.OnChange += OnBakeChanged;
            ApplyLook();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _state.OnChange -= OnStateChanged;
            _bake.OnChange -= OnBakeChanged;
        }

        private void OnStateChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            if ((MoldState)next == MoldState.Baked && (MoldState)prev != MoldState.Baked) _steamUntil = Time.time + _steamSeconds;
            ApplyLook();
        }

        private void OnBakeChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ApplyLook();
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void OnTriggerEnter(Collider other) => TryTakeDough(other);
        private void OnTriggerStay(Collider other) => TryTakeDough(other);

        private void TryTakeDough(Collider other)
        {
            if (!base.IsServerInitialized || State != MoldState.Empty) return;
            DoughBlob d = other.GetComponentInParent<DoughBlob>();
            if (d == null) return;
            FoodPiece fp = d.GetComponent<FoodPiece>();
            if (fp == null || fp.IsHeld || fp.IsConsumed) return;
            // тесто должно быть ВНУТРИ формы, а не просто касаться её снаружи
            if (_fill != null && (other.bounds.center - _fill.position).sqrMagnitude > Sq(SizeOfMold() * 0.6f + other.bounds.extents.magnitude)) return;
            if (!d.IsProofed || !d.IsPiece)
            {
                if (_log && Time.time >= _hintAt)
                {
                    _hintAt = Time.time + 4f;
                    Debug.Log(!d.IsProofed ? $"[Форма] «{name}»: тесто ещё не подошло — сначала в банку"
                                           : $"[Форма] «{name}»: целый шар не влезет — раздели шпателем на {d.Parts} куска", this);
                }
                return;
            }
            int n = fp.Count;
            if (!fp.ServerTryConsume()) return;
            fp.NetworkObject.Despawn();
            _portions.Value = (byte)Mathf.Clamp(n, 1, 255);
            _progress = 0f; _bake.Value = 0; _lastQuarter = -1;
            _state.Value = (byte)MoldState.Dough;
            if (_log) Debug.Log($"[Форма] «{name}»: тесто в форме — ставь в духовку на огонь", this);
        }

        private static float Sq(float v) => v * v;

        private float SizeOfMold()
        {
            Collider c = GetComponent<Collider>();
            if (c != null) { Vector3 s = c.bounds.size; return Mathf.Max(s.x, Mathf.Max(s.y, s.z)); }
            return 0.5f;
        }

        private void Update()
        {
            if (base.IsServerInitialized) ServerTick(Time.deltaTime);
            SteamTick(Time.deltaTime);
        }

        private void ServerTick(float dt)
        {
            _heating = false;
            if (State != MoldState.Dough || dt <= 0f) return;
            BakeOven oven = BakeOven.Find(transform.position);
            if (oven == null) return;
            float t = oven.TemperatureC;
            if (t < _bakeFromC)
            {
                if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 6f; Debug.Log($"[Форма] «{name}» в духовке: {t:0} °C, печётся с {_bakeFromC:0} °C", this); }
                return;
            }
            _heating = true;
            float speed = Mathf.Clamp(t / 180f, 0.6f, 1.5f) * oven.BakeSpeed;
            _progress = Mathf.Min(1f, _progress + dt * speed / _bakeSeconds);
            byte b = (byte)Mathf.RoundToInt(_progress * 255f);
            if (b != _bake.Value) _bake.Value = b;
            int q = Mathf.FloorToInt(_progress * 4f);
            if (_log && q != _lastQuarter && q < 4 && q > 0) Debug.Log($"[Форма] «{name}»: печётся {q * 25}% ({t:0} °C{(oven.DoorOpen ? ", крышки нет — медленнее" : "")})", this);
            _lastQuarter = q;
            if (_progress >= 1f)
            {
                _state.Value = (byte)MoldState.Baked;
                if (_log) Debug.Log($"[Форма] «{name}»: ИСПЁКСЯ — F по форме, чтобы достать хлеб", this);
            }
        }

        /// <summary>F по форме: готовый хлеб — достать, иначе — взять форму.</summary>
        public override void ServerInteract(PlayerInteractor interactor)
        {
            if (State == MoldState.Baked) { ServerPopBread(); return; }
            base.ServerInteract(interactor);
        }

        [Server]
        private void ServerPopBread()
        {
            _state.Value = (byte)MoldState.Empty;
            _bake.Value = 0;
            if (_breadPrefab == null)
            {
                Debug.LogError($"[Форма] «{name}»: не задан Bread Prefab — хлеб пропал", this);
                return;
            }
            Transform at = _fill != null ? _fill : transform;
            GameObject go = Instantiate(_breadPrefab, at.position + Vector3.up * 0.05f, at.rotation);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp != null) fp.ServerPreset(_breadProduct, FoodState.Baked, 100, 100, 100);
            base.Spawn(go);
            if (fp != null && _portions.Value > 0) fp.ServerSetCount(_portions.Value);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.AddForce(Vector3.up * 2.2f + Random.insideUnitSphere * 0.3f, ForceMode.VelocityChange);
            if (_log) Debug.Log($"[Форма] «{name}»: хлеб «{(_breadProduct != null ? _breadProduct.Title : go.name)}» достали", this);
        }

        // ───────────────────────── ПОСТАНОВКА (ЛКМ) ─────────────────────────

        /// <summary>
        /// ЛКМ с формой в руке: поставить РОВНО. Смотришь в духовку — форма встаёт дном на решётку,
        /// длинной стороной вдоль/поперёк прутьев, не вылезая за край. Смотришь на стол/пол — ставится ровно туда.
        /// </summary>
        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (ctx.Hand == null) return;
            if (!FindPlace(ctx, out Vector3 point, out Transform frame, out Vector2 halfArea, out BakeOven oven))
            {
                if (_log) Debug.Log($"[Форма] «{name}»: ЛКМ — некуда поставить: смотри на решётку духовки или на ровную поверхность ближе {_placeReach:0.#} м", this);
                return;
            }
            Quaternion rot = UprightRot(frame);
            ctx.Hand.ServerDrop(Vector3.zero, 0f);
            PlaceAt(point, rot, frame, halfArea);
            if (_log) Debug.Log(oven != null ? $"[Форма] «{name}» поставлена ровно на решётку духовки «{oven.name}»"
                                             : $"[Форма] «{name}» поставлена ровно", this);
        }

        private Vector3 _grillCenter;

        private bool FindPlace(UseContext ctx, out Vector3 point, out Transform frame, out Vector2 halfArea, out BakeOven oven)
        {
            point = Vector3.zero; frame = null; halfArea = Vector2.zero; oven = null;
            Vector3 dir = ctx.Direction.sqrMagnitude > 1e-6f ? ctx.Direction.normalized : Vector3.forward;
            RaycastHit[] hits = Physics.RaycastAll(ctx.Origin, dir, _placeReach, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            Transform player = ctx.Hand != null ? ctx.Hand.transform.root : null;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (player != null && h.collider.transform.IsChildOf(player)) continue;

                oven = h.collider.GetComponentInParent<BakeOven>();
                if (oven == null) oven = BakeOven.Find(h.point + Vector3.up * 0.05f);
                if (oven != null && oven.TryGetGrill(out Collider g))
                {
                    if (g is BoxCollider bc)
                    {
                        // в осях решётки: верх коробки, точка прицела зажата в её пределах
                        Transform gt = g.transform;
                        Vector3 lp = gt.InverseTransformPoint(h.point);
                        Vector3 c = bc.center, e = bc.size * 0.5f;
                        lp.y = c.y + e.y;                                           // верх решётки
                        point = gt.TransformPoint(lp);
                        frame = gt;
                        Vector3 ls = gt.lossyScale;
                        halfArea = new Vector2(Mathf.Abs(e.x * ls.x), Mathf.Abs(e.z * ls.z));
                        _grillCenter = gt.TransformPoint(c);
                    }
                    else
                    {
                        Bounds b = g.bounds;
                        point = new Vector3(h.point.x, b.max.y, h.point.z);
                        frame = null;                                                // мировые оси
                        halfArea = new Vector2(b.extents.x, b.extents.z);
                        _grillCenter = b.center;
                    }
                    return true;
                }
                oven = null;
                if (h.normal.y > 0.6f) { point = h.point; halfArea = Vector2.zero; return true; }      // стол, пол, полка
                return false;                                                  // стена — не ставим
            }
            return false;
        }

        /// <summary>Поза «стоит ровно», повёрнутая вокруг вертикали: по решётке — кратно 90° к прутьям, иначе — как держал.</summary>
        private Quaternion UprightRot(Transform frame)
        {
            Vector3 axis = LongAxisLocal();
            Vector3 fu = Vector3.ProjectOnPlane(_uprightRot * axis, Vector3.up);
            Vector3 fc = Vector3.ProjectOnPlane(transform.rotation * axis, Vector3.up);
            if (fu.sqrMagnitude < 1e-6f) fu = Vector3.forward;
            if (fc.sqrMagnitude < 1e-6f) fc = fu;
            float upYaw = Mathf.Atan2(fu.x, fu.z) * Mathf.Rad2Deg;
            float curYaw = Mathf.Atan2(fc.x, fc.z) * Mathf.Rad2Deg;
            float yaw = curYaw;
            if (frame != null)
            {
                Vector3 ff = Vector3.ProjectOnPlane(frame.forward, Vector3.up);
                float fy = ff.sqrMagnitude > 1e-6f ? Mathf.Atan2(ff.x, ff.z) * Mathf.Rad2Deg : 0f;
                yaw = fy + Mathf.Round(Mathf.DeltaAngle(fy, curYaw) / 90f) * 90f;
            }
            return Quaternion.Euler(0f, yaw - upYaw, 0f) * _uprightRot;
        }

        /// <summary>Длинная горизонтальная ось формы (в её осях) в позе «стоит ровно».</summary>
        private Vector3 LongAxisLocal()
        {
            Bounds lb = LocalMeshBounds();
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
            float[] ext = { lb.extents.x, lb.extents.y, lb.extents.z };
            Vector3 best = Vector3.forward; float bestE = -1f;
            for (int i = 0; i < 3; i++)
            {
                float horiz = 1f - Mathf.Abs(Vector3.Dot((_uprightRot * ax[i]).normalized, Vector3.up));
                if (horiz < 0.7f) continue;
                if (ext[i] > bestE) { bestE = ext[i]; best = ax[i]; }
            }
            return best;
        }

        private Bounds LocalMeshBounds()
        {
            bool any = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                    Vector3 p = transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>Поставить: дно — на уровень точки, центр — в точку (на решётке — не вылезая за край).</summary>
        private void PlaceAt(Vector3 point, Quaternion rot, Transform frame, Vector2 halfArea)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            transform.rotation = rot;
            // рамка — поворот решётки вокруг вертикали (или мир); всё считаем в ней от текущей позиции формы
            Quaternion fr = frame != null ? Quaternion.Euler(0f, frame.eulerAngles.y, 0f) : Quaternion.identity;
            Quaternion inv = Quaternion.Inverse(fr);
            Vector3 o = transform.position;
            bool any = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                    Vector3 p = inv * (mf.transform.TransformPoint(c) - o);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            Vector3 tp = inv * (point - o);
            if (halfArea.sqrMagnitude > 0f)
            {
                // центр формы — в пределах решётки с учётом её размера
                Vector3 gc = inv * (_grillCenter - o);
                float mx = Mathf.Max(0f, halfArea.x - b.extents.x), mz = Mathf.Max(0f, halfArea.y - b.extents.z);
                tp.x = gc.x + Mathf.Clamp(tp.x - gc.x, -mx, mx);
                tp.z = gc.z + Mathf.Clamp(tp.z - gc.z, -mz, mz);
            }
            Vector3 bottom = new Vector3(b.center.x, b.min.y, b.center.z);
            Vector3 pos = o + fr * (tp - bottom + Vector3.up * _placeLift);
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                rb.position = pos; rb.rotation = rot;
            }
            transform.SetPositionAndRotation(pos, rot);
        }

        [ContextMenu("Запомнить позу: так форма стоит ровно")]
        private void CaptureUpright()
        {
            _uprightRot = transform.rotation; _hasUpright = true;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            Debug.Log($"[Форма] «{name}»: поза запомнена", this);
        }

        private void OnGUI()
        {
            if (!_placeHint || !base.IsOwner) return;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null || !rb.isKinematic) return;                 // не в руках
            GUI.Label(new Rect(Screen.width * 0.5f - 120f, Screen.height * 0.62f, 240f, 24f), "ЛКМ — поставить ровно");
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void ApplyLook()
        {
            if (_fill == null) return;
            MoldState st = State;
            bool show = st != MoldState.Empty;
            foreach (Renderer r in _fillR) if (r != null) r.enabled = show;
            if (!show) return;

            float k = st == MoldState.Baked ? 1f : Bake01;
            float s = k * k * (3f - 2f * k);
            float h = Mathf.Lerp(_rawHeight, 1f, s);
            float w = Mathf.Lerp(0.9f, 1f, s);
            _fill.localScale = new Vector3(_fillScale.x * w, _fillScale.y * h, _fillScale.z * w);

            // Цвет: сырое (материал теста) → золотистое → корочка, плавно; в самом конце — материал хлеба.
            // Готовый хлеб — материал хлеба без покраски (его собственный цвет и текстура).
            bool baked = st == MoldState.Baked || k >= 0.985f;
            foreach (Renderer r in _fillR)
            {
                if (r == null) continue;
                Material want = baked || _doughMaterial == null ? _breadMat : _doughMaterial;
                if (want != null && r.sharedMaterial != want) r.sharedMaterial = want;
                if (baked) { BakeColor.Tint(r, ref _mpb, null); continue; }
                BakeColor.Tint(r, ref _mpb, BakeColor.Ramp(_doughTint, _goldTint, _crustEnd, k));
            }
        }

        private void SteamTick(float dt)
        {
            if (_fill == null || Time.time > _steamUntil || State != MoldState.Baked) return;
            _steamAcc += dt * 3f;
            if (_steamAcc < 1f) return;
            _steamAcc -= 1f;
            float fade = Mathf.Clamp01((_steamUntil - Time.time) / _steamSeconds);
            Vector3 top = _fill.position + Vector3.up * 0.1f;
            CCRuntimeParticles.Burst(top, Vector3.up, new Color(1f, 1f, 1f, 0.5f * fade + 0.1f), new Color(0.9f, 0.9f, 0.9f, 0.3f), 2,
                                     CCRuntimeParticles.Look.Dust.Scaled(1.5f));
        }
    }
}
