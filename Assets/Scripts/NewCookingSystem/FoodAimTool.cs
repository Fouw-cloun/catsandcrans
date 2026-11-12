using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ИНСТРУМЕНТ С ПРИЦЕЛОМ ИЗ КАМЕРЫ.
    ///
    /// Триггер-сфера у диска не годится для ручного инструмента: в руке он
    /// держится не всегда ровно, и до продукта просто не достать. Здесь, как в
    /// старых обработчиках, цель выбирается ЛУЧОМ ИЗ ЦЕНТРА ЭКРАНА:
    ///   навёл прицел на картошку в пределах Range + инструмент работает
    ///   → картошка обрабатывается.
    ///
    /// Визуально инструмент при этом ТЯНЕТСЯ к цели — «вытянутая рука»:
    /// смещается вперёд на Lean Distance, у всех игроков.
    ///
    /// Из точки контакта летит СТРУЖКА — цвет из ProductDef продукта
    /// (Debris Color или Base Color). Частицы собраны из кода.
    ///
    /// ═══ СЕТЬ ═══
    ///   держащий  — луч из своей камеры, шлёт на сервер «моя цель — вот эта»
    ///               только когда цель СМЕНИЛАСЬ;
    ///   сервер    — проверяет дистанцию и сам обрабатывает продукт;
    ///   все       — видят цель и «идёт обработка» через SyncVar: наклон и
    ///               стружка у всех одинаковые.
    ///
    /// ═══ КАК ВЕШАТЬ ═══
    /// На КОРЕНЬ инструмента (тот же объект, где предмет и NetworkObject).
    /// Никаких дочерних зон и триггеров не нужно.
    ///
    /// Заменяет пару FoodTool + FoodToolDriver для инструментов в руке.
    /// FoodTool остаётся для станков (AutoRun) и зон в мире.
    /// </summary>
    public sealed class FoodAimTool : NetworkBehaviour
    {
        public enum Source : byte { Auto, Spinning, Saw, Jackhammer, BlowTorch, Nozzle, AlwaysOn }
        public enum Target : byte { Piece, Vessel, Both }

        [Header("Что делает")]
        public FoodProcess Process = FoodProcess.Peel;
        public Target WorksOn = Target.Piece;
        [Tooltip("Скорость обработки: 1 — секунды правила как есть")]
        [Range(0.1f, 5f)] public float Speed = 1f;

        [Header("Когда работает")]
        [Tooltip("Откуда брать «инструмент включён». Auto — найдёт сам.")]
        [SerializeField] private Source _source = Source.Auto;

        [Header("Прицел")]
        [Tooltip("Дальность от камеры, м. Рука ~1.2–1.6, с запасом 2.2.")]
        [Min(0.3f)][SerializeField] private float _range = 2.2f;
        [SerializeField] private LayerMask _foodMask = ~0;
        [Tooltip("Слой тары (для миксера в миске, отбойника в тазике)")]
        [SerializeField] private LayerMask _vesselMask = 0;
        [Tooltip("Запас дистанции для проверки на сервере (задержка сети, наклон)")]
        [SerializeField] private float _serverSlack = 1.5f;

        [Header("Вытянутая рука")]
        [Tooltip("На сколько инструмент тянется вперёд к цели, м")]
        [Range(0f, 0.6f)][SerializeField] private float _leanDistance = 0.18f;
        [Tooltip("Скорость, с которой рука тянется и возвращается")]
        [Range(1f, 30f)][SerializeField] private float _leanSpeed = 10f;
        [Tooltip("Тянуться, даже когда инструмент выключен, но цель есть")]
        [SerializeField] private bool _leanWhenIdle;

        [Header("Стружка")]
        [SerializeField] private bool _chips = true;
        [Tooltip("Точка, откуда стружка (диск). Пусто — WorkPoint инструмента или корень.")]
        [SerializeField] private Transform _workPoint;
        [SerializeField] private float _chipsPerSecond = 90f;

        [Header("Отладка")]
        [SerializeField] private bool _debugRay;
        [SerializeField] private bool _log = true;

        // ───────── сеть ─────────
        private readonly SyncVar<NetworkObject> _target = new SyncVar<NetworkObject>();
        private readonly SyncVar<bool> _processing = new SyncVar<bool>(false);

        // ───────── источники ─────────
        private ISpinningTool _spin;
        private ISawTool _saw;
        private JackhammerItem _jack;
        private BlowTorchItem _torch;
        private NozzleGunItem _nozzle;

        // ───────── локально ─────────
        private NetworkObject _lastSent;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private Vector3 _ownerHit;
        private bool _ownerHasHit;
        private Vector3 _lean;
        private ParticleSystem _stream;

        // ───────── сервер ─────────
        private FoodPiece _workingPiece;

        /// <summary>Смещение инструмента к цели в мире. Предмет прибавляет его в UpdateHeldPose.</summary>
        public Vector3 LeanOffset => _lean;

        /// <summary>Прямо сейчас реально обрабатывает продукт (видят все).
        /// Для шлифмашинки: забивается только от этого.</summary>
        public bool IsProcessing => _processing.Value;

        /// <summary>На кого наведён (видят все). null — ни на кого.</summary>
        public NetworkObject CurrentTarget => _target.Value;

        private void Awake()
        {
            if (_source == Source.Auto || _source == Source.Spinning) _spin = GetComponentInParent<ISpinningTool>();
            if (_source == Source.Auto || _source == Source.Saw) _saw = GetComponentInParent<ISawTool>();
            if (_source == Source.Auto || _source == Source.Jackhammer) _jack = GetComponentInParent<JackhammerItem>();
            if (_source == Source.Auto || _source == Source.BlowTorch) _torch = GetComponentInParent<BlowTorchItem>();
            if (_source == Source.Auto || _source == Source.Nozzle) _nozzle = GetComponentInParent<NozzleGunItem>();

            if (_source == Source.Auto)
            {
                if (_spin != null) _source = Source.Spinning;
                else if (_saw != null) _source = Source.Saw;
                else if (_jack != null) _source = Source.Jackhammer;
                else if (_torch != null) _source = Source.BlowTorch;
                else if (_nozzle != null) _source = Source.Nozzle;
                else _source = Source.AlwaysOn;
            }

            if (_workPoint == null && _spin != null) _workPoint = _spin.WorkPoint;
            if (_workPoint == null) _workPoint = transform;
        }

        private bool ToolActive
        {
            get
            {
                switch (_source)
                {
                    case Source.Spinning: return _spin != null && _spin.IsActive;
                    case Source.Saw: return _saw != null && _saw.IsSawing;
                    case Source.Jackhammer: return _jack != null && _jack.IsRunning;
                    case Source.BlowTorch: return _torch != null && _torch.IsBurning;
                    case Source.Nozzle: return _nozzle != null && _nozzle.IsFiring;
                    default: return true;
                }
            }
        }

        private float ToolQuality => _source == Source.Spinning && _spin != null ? Mathf.Clamp01(_spin.Power01) : 1f;

        // ───────────────────────── КАДР ─────────────────────────

        private void Update()
        {
            if (base.IsOwner) OwnerAim();
            if (base.IsServerInitialized) ServerWork();
            UpdateLean();
            UpdateChips();
        }

        /// <summary>Держащий: луч из центра экрана.</summary>
        private void OwnerAim()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int mask = 0;
            if (WorksOn != Target.Vessel) mask |= _foodMask;
            if (WorksOn != Target.Piece) mask |= _vesselMask;

            NetworkObject found = null;
            _ownerHasHit = false;

            // Все попадания: первым может оказаться сам инструмент в руке — его пропускаем.
            int count = Physics.RaycastNonAlloc(ray, _hits, _range, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform) || h.distance >= best) continue;

                NetworkObject nob = null;
                if (WorksOn != Target.Vessel)
                {
                    FoodPiece p = h.collider.GetComponentInParent<FoodPiece>();
                    if (p != null && !p.IsHeld) nob = p.NetworkObject;
                }
                if (nob == null && WorksOn != Target.Piece)
                {
                    Cookware c = h.collider.GetComponentInParent<Cookware>();
                    if (c != null) nob = c.NetworkObject;
                }
                if (nob == null) continue;

                best = h.distance;
                found = nob;
                _ownerHit = h.point;
                _ownerHasHit = true;
            }

            if (_debugRay)
                Debug.DrawRay(ray.origin, ray.direction * _range, found != null ? Color.green : Color.red);

            if (found != _lastSent)
            {
                _lastSent = found;
                CmdSetTarget(found);
            }
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdSetTarget(NetworkObject nob)
        {
            if (_target.Value == nob) return;
            ServerStopPiece();
            _target.Value = nob;
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            // Инструмент сменил хозяина (бросили, подобрали) — старая цель недействительна.
            ServerStopPiece();
            if (_target.Value != null) _target.Value = null;
        }

        public override void OnOwnershipClient(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            _lastSent = null;
        }

        /// <summary>Сервер: обработать цель, если инструмент включён и цель рядом.</summary>
        private void ServerWork()
        {
            NetworkObject nob = _target.Value;
            bool processing = false;

            if (nob != null && ToolActive && InReach(nob.transform.position))
            {
                float dt = Time.deltaTime * Speed;
                if (_source == Source.Jackhammer && _jack != null) dt *= Mathf.Clamp(_jack.WorkBoost, 1f, 3f);   // v17: водишь по таре — быстрее

                FoodPiece piece = nob.GetComponent<FoodPiece>();
                if (piece != null && !piece.IsConsumed && !piece.IsHeld)
                {
                    if (piece.CanApplyNow(Process))
                    {
                        if (_workingPiece != piece) { ServerStopPiece(); _workingPiece = piece; }
                        if (piece.ServerAddWork(Process, dt, ToolQuality) && _log)
                            Debug.Log($"[{name}] {piece}", piece);
                        processing = true;
                    }
                }
                else
                {
                    Cookware c = CookwareLookup.Of(nob, transform.position);
                    if (c != null && !c.IsEmpty)
                    {
                        c.ServerTick(Process, dt);
                        processing = true;
                    }
                }
            }

            if (!processing) ServerStopPiece();
            if (_processing.Value != processing) _processing.Value = processing;
        }

        private void ServerStopPiece()
        {
            if (_workingPiece != null) _workingPiece.ServerStopWork();
            _workingPiece = null;
        }

        private bool InReach(Vector3 target)
        {
            float max = _range + _serverSlack;
            return (target - transform.position).sqrMagnitude <= max * max;
        }

        // ───────────────────────── ВИЗУАЛ (у всех) ─────────────────────────

        private void UpdateLean()
        {
            Vector3 want = Vector3.zero;
            NetworkObject nob = _target.Value;
            bool active = _processing.Value || (ToolActive && nob != null) || (_leanWhenIdle && nob != null);

            if (active && nob != null)
            {
                Vector3 aim = base.IsOwner && _ownerHasHit ? _ownerHit : ClosestPoint(nob);
                Vector3 dir = aim - transform.position;
                float d = dir.magnitude;
                if (d > 0.001f) want = dir / d * Mathf.Min(_leanDistance, d * 0.5f);
            }

            _lean = Vector3.Lerp(_lean, want, 1f - Mathf.Exp(-_leanSpeed * Time.deltaTime));
        }

        private float _poundAt, _dentAt;
        private bool _poundLogged;

        private static Cookware FindCookwareNear(Vector3 p)
        {
            foreach (Cookware c in Cookware.All)
            {
                if (c == null) continue;
                if (c.IsInside(p)) return c;
                Collider iv = c.InsideVolume;
                if (iv != null && iv.bounds.Contains(p)) return c;
            }
            return null;
        }

        /// <summary>v17: отбойник в таре — не стружка-квадратики, а пыль и ошмётки цвета содержимого.</summary>
        private bool PoundFx(bool on)
        {
            if (_source != Source.Jackhammer) return false;
            if (_stream != null) CCRuntimeParticles.SetStream(_stream, false, Vector3.zero, Vector3.zero, Color.clear, Color.clear, 0f);
            if (!on) return true;
            NetworkObject nob = _target.Value;
            Cookware cw = nob != null ? CookwareLookup.Of(nob, transform.position) : null;
            if (cw == null) cw = FindCookwareNear(_workPoint != null ? _workPoint.position : transform.position);
            if (cw == null && nob != null && Time.time >= _poundAt)
            {
                // v17.4: долбит кусок не в таре — тоже облачка и ошмётки (а не квадратная стружка)
                _poundAt = Time.time + Random.Range(0.15f, 0.22f);
                Transform wp = _workPoint != null && _workPoint != transform ? _workPoint : (_jack != null && _jack.ProbePoint != null ? _jack.ProbePoint : transform);
                CCPowderFx.Pound(ClosestPoint(nob, wp.position), Vector3.up, ChipColor(nob), 0.2f, 0.5f);
            }
            // в таре пыль и комочки рисует сам отбойник (JackhammerItem) — даже когда рецепт уже готов
            return true;
        }

        private void UpdateChips()
        {
            if (!_chips) return;
            bool on = _processing.Value && _target.Value != null;
            if (PoundFx(on)) return;
            if (on && _stream == null) _stream = CCRuntimeParticles.CreateStream(transform, CCRuntimeParticles.Look.Chips);
            if (_stream == null) return;

            if (!on) { CCRuntimeParticles.SetStream(_stream, false, Vector3.zero, Vector3.zero, Color.clear, Color.clear, 0f); return; }

            NetworkObject nob = _target.Value;
            Vector3 contact = ClosestPoint(nob, _workPoint.position);
            // Стружка летит по касательной: вбок от инструмента и немного вверх.
            Vector3 fromTool = (contact - _workPoint.position);
            Vector3 side = Vector3.Cross(fromTool.sqrMagnitude > 0.0001f ? fromTool : transform.forward, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = transform.right;
            Vector3 dir = side.normalized + Vector3.up * 0.6f;

            Color c = ChipColor(nob);
            CCRuntimeParticles.SetStream(_stream, true, contact, dir, c * 0.85f, c * 1.1f,
                                         _chipsPerSecond * Mathf.Max(0.3f, ToolQuality));
        }

        private static Color ChipColor(NetworkObject nob)
        {
            FoodPiece p = nob != null ? nob.GetComponent<FoodPiece>() : null;
            ProductDef d = p != null ? p.Def : null;
            if (d == null) return new Color(0.6f, 0.5f, 0.35f);
            // Чистим — летит кожура (цвет ошмётков), остальное — цвет продукта.
            return d.CrumbColor;
        }

        private Vector3 ClosestPoint(NetworkObject nob) => ClosestPoint(nob, transform.position);

        private static Vector3 ClosestPoint(NetworkObject nob, Vector3 from)
        {
            if (nob == null) return from;
            Collider c = nob.GetComponentInChildren<Collider>();
            return c != null && c.enabled ? c.ClosestPoint(from) : nob.transform.position;
        }

        private void OnDisable()
        {
            if (_stream != null) CCRuntimeParticles.SetStream(_stream, false, Vector3.zero, Vector3.zero, Color.clear, Color.clear, 0f);
        }
    }
}
