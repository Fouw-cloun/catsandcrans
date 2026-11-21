using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CatCooks.Thief
{
    /// <summary>
    /// КОТ-ВОРИШКА — анимации (у каждого клиента свои, по состоянию из CatThief).
    ///
    /// Клипы из Assets/CatCaper/Models: Walk (ходьба), Sneak (на цыпочках), Lookout (осмотр),
    /// Pickup (хват с пола, однократно, кадр 42 — лапы у пола), Escape (бег с поднятыми лапами).
    /// Переходы — плавные (Fade). Скорость шага подгоняется под реальную скорость кота.
    ///
    /// ПАДЕНИЕ (толкнули) — без отдельного клипа: кот кувыркается набок всей моделью
    /// (Tumble), лапы — в позе осмотра (пригнулся), полежал, встал. Так оно стыкуется с любым клипом.
    ///
    /// Сокет между запястьями (Socket) — туда доставка-ворюга вешает мясо (сервер ставит кусок сюда).
    /// </summary>
    [RequireComponent(typeof(Animator)), DisallowMultipleComponent]
    public sealed class CatThiefAnimator : MonoBehaviour
    {
        public enum Move { Walk, Sneak, Lookout, Pickup, Escape, Fall, Eat, Idle }

        [Header("Клипы (вложенные AnimationClip из FBX)")]
        public AnimationClip walk;
        public AnimationClip sneak, lookout, pickup, escape;

        [Header("Переходы")]
        [Range(0.05f, 0.6f)] public float fade = 0.22f;
        [Tooltip("Скорость кота (м/с), при которой клип Walk играет 1:1 — дальше шаг ускоряется пропорционально")]
        public float walkSpeedRef = 1.1f;
        public float sneakSpeedRef = 0.55f;
        public float escapeSpeedRef = 3.6f;

        [Header("v27: ПОЗЫ ПОВЕРХ ТВОЕЙ ХОДЬБЫ (вместо клипов нейронки)")]
        [Tooltip("Вкл — все движения строятся из ТВОЕЙ ходьбы + наклон спины, лапы, голова (код, в мировых осях — " +
                 "не зависит от позы покоя костей). Выкл — клипы Sneak/Lookout/Pickup/Escape как есть")]
        public bool proceduralPoses = true;
        [Tooltip("Кадр ходьбы (с), который держится, когда кот стоит (осмотр, подбор, еда)")]
        public float standFrame = 0f;
        public CatPose sneakPose = new CatPose { spine = 18f, armForward = 55f, armIn = 18f, forearm = 70f, head = -8f, step = 0.55f };
        public CatPose lookPose = new CatPose { spine = 10f, armForward = 50f, armIn = 18f, forearm = 75f, head = -5f, step = 0f };
        public CatPose reachPose = new CatPose { spine = 60f, armForward = 80f, armIn = 22f, forearm = 10f, head = 15f, step = 0f };
        public CatPose escapePose = new CatPose { spine = 14f, armForward = 165f, armIn = 25f, forearm = 25f, head = -12f, step = 1.8f };
        public CatPose eatPose = new CatPose { spine = 22f, armForward = 80f, armIn = 30f, forearm = 105f, head = 12f, step = 0f };
        [Tooltip("Осмотр: на сколько градусов кот вертит головой влево-вправо")]
        public float lookYaw = 55f;

        [System.Serializable]
        public struct CatPose
        {
            [Tooltip("Наклон спины вперёд, °")] public float spine;
            [Tooltip("Лапы вперёд-вверх от висящих, ° (90 — вперёд, 180 — над головой)")] public float armForward;
            [Tooltip("Лапы к середине, °")] public float armIn;
            [Tooltip("Сгиб в локте, °")] public float forearm;
            [Tooltip("Голова вниз (+) / вверх (−), °")] public float head;
            [Tooltip("Темп ходьбы ×; 0 — стоит")] public float step;
            public static CatPose Lerp(CatPose a, CatPose b, float t) => new CatPose
            {
                spine = Mathf.Lerp(a.spine, b.spine, t), armForward = Mathf.Lerp(a.armForward, b.armForward, t), armIn = Mathf.Lerp(a.armIn, b.armIn, t),
                forearm = Mathf.Lerp(a.forearm, b.forearm, t), head = Mathf.Lerp(a.head, b.head, t), step = Mathf.Lerp(a.step, b.step, t)
            };
        }

        [Header("Рост (v26.7)")]
        [Tooltip("Расстояние голова–ступни (м) у котов из клипов Cat_Caper в позе Sneak. Меню «Кот-воришка → 1» ставит само. " +
                 "В игре, после первой анимации, модель подгоняется под этот рост (модель художника в других единицах — был кот-великан). 0 — не подгонять")]
        public float targetSpan;

        [Header("Голова")]
        [Range(0f, 40f)] public float glanceDegrees = 18f;

        [Header("Падение (толкнули)")]
        [Tooltip("Сколько длится падение целиком: опрокинулся → лежит → встал")]
        public float fallDuration = 1.8f;
        [Tooltip("Насколько высоко подлетает при толчке (× роста)")]
        public float fallHop = 0.25f;

        public Transform Socket { get; private set; }
        public Move Current { get; private set; } = Move.Idle;
        public float PickupGrabTime => 41f / 30f;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _a, _b;          // _a — текущий, _b — уходящий
        private float _blend = 1f;
        private Animator _animator;
        private Transform _head, _handL, _handR;
        private float _stateTime, _glanceTimer, _glance, _glanceTarget, _glanceVel, _speed;
        private Vector3 _baseLocalPos;
        private Quaternion _baseLocalRot;
        private float _height = 1f;
        private int _fallSide = 1;

        [Tooltip("v27.1: держать масштаб модели и всех костей как в префабе (анимация не может растянуть/сжать кота)")]
        public bool lockScale = true;
        private Transform[] _bones;
        private Vector3[] _boneScale;
        private Vector3 _rootScale;
        private Quaternion[] _poseSaved;
        private Transform[] _poseBones;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _rootScale = transform.localScale;
            _bones = GetComponentsInChildren<Transform>(true);
            _boneScale = new Vector3[_bones.Length];
            for (int i = 0; i < _bones.Length; i++) _boneScale[i] = _bones[i].localScale;
            _animator.applyRootMotion = false;
            foreach (Transform b in GetComponentsInChildren<Transform>(true))
            {
                if (b.name == "DEF-spine.006") _head = b;
                else if (b.name == "DEF-hand.L") _handL = b;
                else if (b.name == "DEF-hand.R") _handR = b;
            }
            Socket = new GameObject("CatThiefSocket").transform;
            Socket.SetParent(transform.parent != null ? transform.parent : transform, false);
            _baseLocalPos = transform.localPosition;
            _baseLocalRot = transform.localRotation;
            Bounds bb = default; bool any = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
            }
            if (any) _height = Mathf.Max(0.2f, bb.size.y);
        }

        private void OnEnable()
        {
            _graph = PlayableGraph.Create("Cat Thief");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "Cat", _animator);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            Current = Move.Idle;
            Play(Move.Walk, true);
        }

        private int _fitFrames = -1;
        private bool _fitted;

        /// <summary>Голова − ступни по вертикали, м (мир).</summary>
        public static float SpanOf(Transform root)
        {
            Transform head = null; float foot = float.MaxValue; bool anyFoot = false;
            foreach (Transform b in root.GetComponentsInChildren<Transform>(true))
            {
                if (b.name == "DEF-spine.006") head = b;
                else if (b.name.StartsWith("DEF-foot") || b.name.StartsWith("DEF-toe")) { foot = Mathf.Min(foot, b.position.y); anyFoot = true; }
            }
            return head != null && anyFoot ? head.position.y - foot : 0f;
        }

        private void FitHeight()
        {
            _fitted = true;
            if (targetSpan <= 0.01f) return;
            float span = SpanOf(transform);
            if (span <= 0.01f) return;
            float k = targetSpan / span;
            if (k > 0.85f && k < 1.18f) return;
            transform.localScale *= k;
            _rootScale = transform.localScale;
            _baseLocalPos *= k;
            transform.localPosition = _baseLocalPos;
            _height *= k;
            Debug.Log($"[Кот-воришка] «{name}»: рост подогнан ×{k:0.###} (голова–ступни {span:0.00} → {targetSpan:0.00} м)", this);
        }

        private Quaternion _probeRest;
        private Transform _probeBone;
        private float _checkAt = -1f;

        private void Start()
        {
            // v26.1: диагностика — почему кот в Т-позе
            string miss = "";
            if (walk == null) miss += " Walk"; if (sneak == null) miss += " Sneak"; if (lookout == null) miss += " Lookout";
            if (pickup == null) miss += " Pickup"; if (escape == null) miss += " Escape";
            if (miss.Length > 0) Debug.LogWarning($"[Кот-воришка] «{name}»: не назначены клипы:{miss} — меню «Кот-воришка → 1» заново", this);
            if (_animator.avatar == null) Debug.LogWarning($"[Кот-воришка] «{name}»: у Animator нет Avatar — у Cat_Caper_Model.fbx поставь Rig → Generic, Create From This Model", this);
            _probeBone = _handL != null ? _handL : _head;
            if (_probeBone != null) { _probeRest = _probeBone.localRotation; _checkAt = Time.time + 1.5f; }
            else Debug.LogWarning($"[Кот-воришка] «{name}»: не нашёл костей DEF-hand / DEF-spine.006 — это не та модель (нужен скелет gamerig)", this);
        }

        private void OnDisable()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        private void OnDestroy()
        {
            if (Socket != null) Destroy(Socket.gameObject);
        }

        private AnimationClip ClipFor(Move m)
        {
            if (proceduralPoses && walk != null) return walk;                   // v27: всё — из твоей ходьбы
            switch (m)
            {
                case Move.Sneak: return sneak != null ? sneak : walk;
                case Move.Lookout: return lookout;
                case Move.Eat: return lookout;
                case Move.Pickup: return pickup;
                case Move.Escape: return escape;
                case Move.Fall: return lookout;          // пригнулся — лапы поджаты, модель кувыркается
                case Move.Idle: return lookout;
                default: return walk != null ? walk : sneak;
            }
        }

        /// <summary>Переключить движение (плавно). Повторный вызов того же — ничего не делает.</summary>
        public void Play(Move m, bool instant = false)
        {
            if (m == Current && !instant) return;
            Current = m;
            _stateTime = 0f;
            _glanceTimer = 0f;
            AnimationClip clip = ClipFor(m);
            if (!_graph.IsValid() || clip == null) return;

            // старый «уходящий» — выкидываем, текущий становится уходящим
            if (_b.IsValid()) { _graph.Disconnect(_mixer, 1); _b.Destroy(); }
            if (_a.IsValid())
            {
                _graph.Disconnect(_mixer, 0);
                _b = _a;
                _graph.Connect(_b, 0, _mixer, 1);
            }
            _a = AnimationClipPlayable.Create(_graph, clip);
            _a.SetApplyFootIK(false);
            _a.SetTime(0);
            if (m == Move.Fall) { _a.SetTime(proceduralPoses && walk != null ? standFrame : 0.45f); _a.SetSpeed(0); }   // поза «пригнулся», замерли
            if (m == Move.Pickup) _a.SetDuration(clip.length);
            _graph.Connect(_a, 0, _mixer, 0);
            _blend = instant || !_b.IsValid() ? 1f : 0f;
            ApplyWeights();
            if (m == Move.Fall) _fallSide = Random.value < 0.5f ? -1 : 1;
        }

        /// <summary>Реальная скорость кота (м/с) — для темпа шага/бега.</summary>
        public void SetSpeed(float metersPerSecond) => _speed = metersPerSecond;

        private void ApplyWeights()
        {
            _mixer.SetInputWeight(0, _blend);
            _mixer.SetInputWeight(1, _b.IsValid() ? 1f - _blend : 0f);
        }

        private void Update()
        {
            if (!_graph.IsValid()) return;
            // v27.1: вернуть кости поз как были до наклона — если ходьба их не анимирует, повороты не копятся кадр за кадром
            if (_poseBones != null)
                for (int i = 0; i < _poseBones.Length; i++) if (_poseBones[i] != null) _poseBones[i].localRotation = _poseSaved[i];
            _stateTime += Time.deltaTime;
            if (_blend < 1f)
            {
                _blend = Mathf.MoveTowards(_blend, 1f, Time.deltaTime / Mathf.Max(0.01f, fade));
                ApplyWeights();
                if (_blend >= 1f && _b.IsValid()) { _graph.Disconnect(_mixer, 1); _b.Destroy(); }
            }
            // темп под скорость
            if (_a.IsValid() && Current != Move.Fall && proceduralPoses && walk != null)
            {
                // v27: темп ходьбы по позе; стоит — держим один кадр
                float st = TargetPose().step;
                if (Current == Move.Walk) st = Mathf.Clamp(_speed / Mathf.Max(0.05f, walkSpeedRef), 0.6f, 2.2f);
                else if (st > 0.01f) st *= Mathf.Clamp(_speed / Mathf.Max(0.05f, Current == Move.Escape ? escapeSpeedRef : sneakSpeedRef), 0.5f, 1.6f);
                if (st < 0.01f) { _a.SetSpeed(0); _a.SetTime(standFrame); } else _a.SetSpeed(st);
            }
            else if (_a.IsValid() && Current != Move.Fall)
            {
                float k = 1f;
                if (Current == Move.Walk) k = walk != null ? Mathf.Clamp(_speed / Mathf.Max(0.05f, walkSpeedRef), 0.6f, 2.2f)
                                                           : Mathf.Clamp(_speed / Mathf.Max(0.05f, sneakSpeedRef), 0.8f, 2.4f);   // нет ходьбы — цыпочки побыстрее
                else if (Current == Move.Sneak) k = Mathf.Clamp(_speed / Mathf.Max(0.05f, sneakSpeedRef), 0.5f, 1.8f);
                else if (Current == Move.Escape) k = Mathf.Clamp(_speed / Mathf.Max(0.05f, escapeSpeedRef), 0.7f, 1.8f);
                else if (Current == Move.Eat) k = 1.6f;                         // жуёт — быстрее «осматривается»
                _a.SetSpeed(k);
            }
        }

        // ── v27: кости для поз ──
        private Transform[] _spine;
        private Transform _armL, _armR, _foreL, _foreR;
        private CatPose _pose;

        private void FindPoseBones()
        {
            var sp = new System.Collections.Generic.List<Transform>();
            foreach (Transform b in GetComponentsInChildren<Transform>(true))
            {
                if (b.name == "DEF-spine.001" || b.name == "DEF-spine.002" || b.name == "DEF-spine.003") sp.Add(b);
                else if (b.name == "DEF-upper_arm.L") _armL = b;
                else if (b.name == "DEF-upper_arm.R") _armR = b;
                else if (b.name == "DEF-forearm.L") _foreL = b;
                else if (b.name == "DEF-forearm.R") _foreR = b;
            }
            _spine = sp.ToArray();
        }

        /// <summary>Поза для текущего движения (подбор — по времени: тянется вниз → хват → поднял над головой).</summary>
        private CatPose TargetPose()
        {
            switch (Current)
            {
                case Move.Sneak: return sneakPose;
                case Move.Lookout: case Move.Idle: return lookPose;
                case Move.Eat: return eatPose;
                case Move.Escape: return escapePose;
                case Move.Fall: return lookPose;
                case Move.Pickup:
                {
                    float g = PickupGrabTime;
                    if (_stateTime < g) return CatPose.Lerp(lookPose, reachPose, Mathf.SmoothStep(0f, 1f, _stateTime / g));
                    float up = Mathf.Clamp01((_stateTime - g) / 0.9f);
                    CatPose p = CatPose.Lerp(reachPose, escapePose, Mathf.SmoothStep(0f, 1f, up)); p.step = 0f; return p;
                }
                default: return default;
            }
        }

        private void ApplyPose()
        {
            if (_spine == null) FindPoseBones();
            CatPose want = TargetPose();
            float k = 1f - Mathf.Exp(-Time.deltaTime * 10f);
            _pose = CatPose.Lerp(_pose, want, Current == Move.Pickup ? 1f : k);
            Transform root = transform.parent != null ? transform.parent : transform;
            Vector3 right = root.right, up = root.up;
            // спина — наклон, поровну по трём позвонкам
            if (_spine.Length > 0 && Mathf.Abs(_pose.spine) > 0.1f)
            {
                Quaternion r = Quaternion.AngleAxis(_pose.spine / _spine.Length, right);
                foreach (Transform b in _spine) b.rotation = r * b.rotation;
            }
            // лапы: вперёд-вверх (вокруг «вправо»), к середине (вокруг «вверх»), локоть
            ArmPose(_armL, _foreL, right, up, +1f);
            ArmPose(_armR, _foreR, right, up, -1f);
            // голова — кивок
            if (_head != null && Mathf.Abs(_pose.head) > 0.1f) _head.rotation = Quaternion.AngleAxis(_pose.head, right) * _head.rotation;
            // осмотр — голова влево-вправо, с паузами
            if (_head != null && (Current == Move.Lookout || Current == Move.Idle))
            {
                float t = _stateTime * 1.6f;
                float yaw = Mathf.Clamp(Mathf.Sin(t) * 1.4f, -1f, 1f) * lookYaw;
                _head.rotation = Quaternion.AngleAxis(yaw, up) * _head.rotation;
            }
            if (_head != null && Current == Move.Eat)                         // жуёт
                _head.rotation = Quaternion.AngleAxis(Mathf.Sin(_stateTime * 14f) * 4f, right) * _head.rotation;
        }

        private void ArmPose(Transform arm, Transform fore, Vector3 right, Vector3 up, float side)
        {
            if (arm != null)
            {
                Quaternion fwd = Quaternion.AngleAxis(-_pose.armForward, right);
                Quaternion inw = Quaternion.AngleAxis(_pose.armIn * side, up);
                arm.rotation = inw * fwd * arm.rotation;
            }
            if (fore != null && Mathf.Abs(_pose.forearm) > 0.1f)
                fore.rotation = Quaternion.AngleAxis(-_pose.forearm, right) * fore.rotation;
        }

        private void LateUpdate()
        {
            if (lockScale)                                                       // v27.1: масштаб — как в префабе
            {
                if (transform.localScale != _rootScale) transform.localScale = _rootScale;
                for (int i = 0; i < _bones.Length; i++)
                    if (_bones[i] != null && _bones[i] != transform && _bones[i].localScale != _boneScale[i]) _bones[i].localScale = _boneScale[i];
            }
            if (proceduralPoses && walk != null)
            {
                if (_spine == null) FindPoseBones();
                if (_poseBones == null)
                {
                    var l = new System.Collections.Generic.List<Transform>(_spine) { _armL, _armR, _foreL, _foreR, _head };
                    _poseBones = l.ToArray(); _poseSaved = new Quaternion[_poseBones.Length];
                }
                for (int i = 0; i < _poseBones.Length; i++) if (_poseBones[i] != null) _poseSaved[i] = _poseBones[i].localRotation;
                ApplyPose();
            }
            // голова — случайный доворот на цыпочках и при осмотре
            if (_head != null && glanceDegrees > 0f && (Current == Move.Sneak || Current == Move.Walk))
            {
                _glanceTimer -= Time.deltaTime;
                if (_glanceTimer <= 0f)
                {
                    _glanceTarget = Random.Range(-1f, 1f) * glanceDegrees * (Current == Move.Sneak ? 1f : 0.4f);
                    _glanceTimer = 0.45f + Random.value * 1.3f;
                }
                _glance = Mathf.SmoothDamp(_glance, _glanceTarget, ref _glanceVel, 0.12f, 160f, Time.deltaTime);
                _head.rotation = Quaternion.AngleAxis(_glance, transform.up) * _head.rotation;
            }
            // падение: кувырок всей моделью
            if (Current == Move.Fall)
            {
                float t = _stateTime / Mathf.Max(0.3f, fallDuration);
                float tip = t < 0.22f ? Mathf.SmoothStep(0f, 1f, t / 0.22f)
                          : t < 0.7f ? 1f + Mathf.Sin((t - 0.22f) * 40f) * 0.04f * (1f - (t - 0.22f) / 0.48f)   // дрожит лапами
                          : Mathf.SmoothStep(1f, 0f, (t - 0.7f) / 0.3f);
                float hop = t < 0.22f ? Mathf.Sin(t / 0.22f * Mathf.PI) * fallHop * _height : 0f;
                transform.localRotation = _baseLocalRot * Quaternion.AngleAxis(88f * tip * _fallSide, Vector3.forward);
                transform.localPosition = _baseLocalPos + Vector3.up * (hop + tip * _height * 0.18f);
            }
            else if (transform.localRotation != _baseLocalRot || transform.localPosition != _baseLocalPos)
            {
                transform.localRotation = Quaternion.Slerp(transform.localRotation, _baseLocalRot, Time.deltaTime * 12f);
                transform.localPosition = Vector3.Lerp(transform.localPosition, _baseLocalPos, Time.deltaTime * 12f);
            }
            if (!_fitted && ++_fitFrames >= 3) FitHeight();                    // после пары кадров анимации
            if (_checkAt > 0f && Time.time >= _checkAt)
            {
                _checkAt = -1f;
                Bounds nb = default; bool nany = false;
                foreach (Renderer r in GetComponentsInChildren<Renderer>())
                {
                    if (!nany) { nb = r.bounds; nany = true; } else nb.Encapsulate(r.bounds);
                }
                if (nany && (nb.size.y > _height * 1.8f || nb.size.y < _height * 0.5f))
                    Debug.LogWarning($"[Кот-воришка] «{name}»: анимация меняет рост кота ({_height:0.00} → {nb.size.y:0.00} м) — клипы не под эту модель. " +
                                     "Пересобери префаб меню «Кот-воришка → 1» (клипы *_Safe переносятся на модель)", this);
                if (_probeBone != null && Quaternion.Angle(_probeBone.localRotation, _probeRest) < 0.5f)
                    Debug.LogWarning($"[Кот-воришка] «{name}»: кости НЕ двигаются (Т-поза). Проверь: у Cat_Caper_*.fbx Rig = Generic, Optimize Game Objects выкл; " +
                                     "клипы из той же модели (кости gamerig/DEF-…); Animator включён. Можно выделить FBX и нажать Reimport.", this);
            }
            // сокет — между запястьями
            if (Socket != null && _handL != null && _handR != null)
                Socket.SetPositionAndRotation((_handL.position + _handR.position) * 0.5f, transform.rotation);
        }

        /// <summary>Pickup доигран?</summary>
        public bool PickupDone => Current == Move.Pickup && pickup != null && _stateTime >= pickup.length;
        public float StateTime => _stateTime;
    }
}
