using FishNet.Object;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ШИНА-МЕХИ, КОТОРАЯ САМА ЦЕЛИТСЯ В КОСТЁР.
    ///
    /// Шина — отдельный предмет: носится (PickupItem), пинается, катается.
    /// Как только она оказалась рядом с кострищем (ближе Radius):
    ///   • ТОЧКА ВЫДУВА (Wind Origin у Simple Tire Bounce, если это отдельный
    ///     дочерний объект) наводится точно на огонь — у каждого игрока,
    ///     потому что ветер шина считает у каждого сама;
    ///   • САМА ШИНА, когда улеглась (не в руках, почти не движется), плавно
    ///     доворачивается вокруг вертикали нужной стороной к огню. Не
    ///     опрокидывается — только поворот по кругу, как будто подвинули ногой.
    /// Унёс шину от кострища — точка выдува возвращается как была.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// На шине: NetworkObject, NetworkTransform (Client Authoritative), Rigidbody,
    /// коллайдер, PickupItem, SimpleTireBounce и этот компонент.
    /// Лучше всего: у Simple Tire Bounce задан Wind Origin — дочерний пустой
    /// объект у шины, его ось ветра (Wind Axis) и будет смотреть на костёр.
    /// </summary>
    [RequireComponent(typeof(SimpleTireBounce))]
    public sealed class TireAimer : NetworkBehaviour
    {
        [Tooltip("Ближе этого к кострищу (по горизонтали) — шина целится в него, м")]
        [Min(0.5f)][SerializeField] private float _radius = 4f;

        [Header("Точка выдува")]
        [Tooltip("Наводить точку выдува на огонь")]
        [SerializeField] private bool _aimNozzle = true;
        [Tooltip("Скорость наведения, град/с")]
        [SerializeField] private float _nozzleSpeed = 360f;

        [Header("Сама шина")]
        [Tooltip("Доворачивать улёгшуюся шину нужной стороной к огню")]
        [SerializeField] private bool _alignBody = true;
        [Tooltip("Какая сторона шины (в её осях) должна смотреть на огонь. " +
                 "Пусто (0,0,0) — горизонтальная часть ветра.")]
        [SerializeField] private Vector3 _faceAxisLocal = Vector3.zero;
        [Tooltip("Скорость доворота, град/с")]
        [SerializeField] private float _bodySpeed = 90f;
        [Tooltip("Медленнее этого (м/с) шина считается улёгшейся")]
        [SerializeField] private float _settleSpeed = 0.35f;

        [Header("Раздув")]
        [Tooltip("Сколько воздуха (0..1) даёт один прыжок на шине, если она нацелена на кострище")]
        [Range(0f, 1f)][SerializeField] private float _airPerJump = 0.45f;
        [Tooltip("Не чаще, с (защита от двойного счёта)")]
        [SerializeField] private float _jumpCooldown = 0.25f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private int _lastBlasts;
        private float _nextServerJump;

        private SimpleTireBounce _tire;
        private Rigidbody _rb;
        private Transform _body;
        private Quaternion _nozzleRest;
        private bool _restCaptured;
        private BonfirePit _target;

        private void Awake()
        {
            _tire = GetComponent<SimpleTireBounce>();
            // Rigidbody обычно на корне шины, а Simple Tire Bounce — на дочерней зоне прыжка.
            _rb = GetComponentInParent<Rigidbody>();
            _body = _rb != null ? _rb.transform : transform;
        }

        /// <summary>
        /// Центр САМОЙ шины (а не её опоры): в игре — центр массы Rigidbody, в редакторе —
        /// центр коллайдеров/мешей. У шины, собранной со сдвинутой опорой, это разные точки.
        /// </summary>
        private Vector3 Centre()
        {
            if (Application.isPlaying && _rb != null) return _rb.worldCenterOfMass;
            Transform root = _body != null ? _body : (GetComponentInParent<Rigidbody>() != null
                ? GetComponentInParent<Rigidbody>().transform : transform);
            bool any = false; Bounds b = default;
            foreach (Collider c in root.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (!any)
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            return any ? b.center : root.position;
        }

        private BonfirePit Nearest()
        {
            BonfirePit best = null;
            float bd = _radius;
            Vector3 p = Centre();
            foreach (BonfirePit pit in BonfirePit.All)
            {
                if (pit == null) continue;
                Vector3 d = pit.AirPoint - p;
                float h = new Vector2(d.x, d.z).magnitude;
                if (h < bd) { bd = h; best = pit; }
            }
            return best;
        }

        // ── точка выдува: у каждого игрока (ветер шина считает сама у каждого) ──
        private void Update()
        {
            BonfirePit pit = Nearest();
            if (pit != _target)
            {
                _target = pit;
                if (_log && base.IsServerInitialized)
                    Debug.Log(pit != null ? $"[Шина] «{name}» целится в кострище «{pit.name}»"
                                          : $"[Шина] «{name}» отошла от кострища", this);
            }

            // ── прыжок на шине → воздух в кострище ──
            // Шина считает прыжок у того игрока, где он случился (часто не на сервере),
            // поэтому каждый игрок сам сообщает серверу о своих прыжках.
            if (_tire != null)
            {
                int blasts = _tire.WindBlasts;
                if (blasts != _lastBlasts)
                {
                    bool jumped = blasts > _lastBlasts;
                    _lastBlasts = blasts;
                    if (jumped && pit != null)
                    {
                        if (_log) Debug.Log($"[Шина] «{name}»: прыжок → кострище «{pit.name}»", this);
                        if (base.IsServerInitialized) ServerJump();
                        else CmdJump();
                    }
                    else if (jumped && _log)
                        Debug.Log($"[Шина] «{name}»: прыжок, но кострище дальше {_radius:0.0} м (Radius у Tire Aimer)", this);
                }
            }

            if (!_aimNozzle || _tire == null || !_tire.HasSeparateWindOrigin) return;
            Transform n = _tire.WindOrigin;
            if (!_restCaptured) { _nozzleRest = n.localRotation; _restCaptured = true; }

            if (pit == null)
            {
                n.localRotation = Quaternion.RotateTowards(n.localRotation, _nozzleRest, _nozzleSpeed * Time.deltaTime);
                return;
            }
            Vector3 want = pit.AirPoint - n.position;
            if (want.sqrMagnitude < 1e-6f) return;
            Vector3 axisWorld = n.TransformDirection(_tire.WindAxisLocal);
            Quaternion target = Quaternion.FromToRotation(axisWorld, want.normalized) * n.rotation;
            n.rotation = Quaternion.RotateTowards(n.rotation, target, _nozzleSpeed * Time.deltaTime);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdJump() => ServerJump();

        private void ServerJump()
        {
            if (Time.time < _nextServerJump) return;
            _nextServerJump = Time.time + _jumpCooldown;
            BonfirePit pit = _target != null ? _target : Nearest();
            if (pit == null) return;
            if (!pit.IsBurning && _log) Debug.Log("[Шина] кострище не горит — раздувать нечего (сначала горелкой)", this);
            Vector3 from = _tire != null ? _tire.WindOrigin.position : transform.position;
            pit.ServerAddAir(_airPerJump, (pit.AirPoint - from).normalized);
        }

        // ── сама шина: сервер, когда улеглась ──
        private void FixedUpdate()
        {
            if (!_alignBody || !base.IsServerInitialized || _target == null || _rb == null) return;
            if (_rb.isKinematic) return;                                           // в руках
            if (_rb.linearVelocity.magnitude > _settleSpeed || _rb.angularVelocity.magnitude > 1.5f) return;

            Vector3 face = FaceDirectionWorld();
            Vector3 pivot = _rb.worldCenterOfMass;
            Vector3 want = Vector3.ProjectOnPlane(_target.AirPoint - pivot, Vector3.up);
            if (face.sqrMagnitude < 1e-4f || want.sqrMagnitude < 1e-4f) return;
            float angle = Vector3.SignedAngle(face.normalized, want.normalized, Vector3.up);
            if (Mathf.Abs(angle) < 1f) return;

            // Сервер берёт шину себе — иначе поворот перебил бы прежний владелец.
            if (base.NetworkObject.Owner.IsValid) base.NetworkObject.RemoveOwnership();
            float step = Mathf.Clamp(angle, -_bodySpeed * Time.fixedDeltaTime, _bodySpeed * Time.fixedDeltaTime);
            // Поворот вокруг центра массы шины, а не вокруг опоры: если опора
            // сдвинута от модели, поворот вокруг неё уносил бы шину по дуге.
            Quaternion q = Quaternion.AngleAxis(step, Vector3.up);
            _rb.MovePosition(pivot + q * (_rb.position - pivot));
            _rb.MoveRotation(q * _rb.rotation);
        }

        /// <summary>Какая сторона шины должна смотреть на огонь — по горизонтали.</summary>
        private Vector3 FaceDirectionWorld()
        {
            if (_faceAxisLocal.sqrMagnitude > 1e-6f)
                return Vector3.ProjectOnPlane(_body.TransformDirection(_faceAxisLocal), Vector3.up);
            // По умолчанию — горизонтальная часть ветра в ПОКОЕ (до наведения точки выдува).
            if (_tire == null) return _body.forward;
            Transform n = _tire.WindOrigin;
            Quaternion restWorld = _tire.HasSeparateWindOrigin && _restCaptured && n.parent != null
                ? n.parent.rotation * _nozzleRest : n.rotation;
            return Vector3.ProjectOnPlane(restWorld * _tire.WindAxisLocal, Vector3.up);
        }

        private void OnDrawGizmosSelected()
        {
#if UNITY_EDITOR
            Vector3 c = Centre();
            c.y += 0.05f;
            // Радиус — толстым оранжевым кругом с подписью (не путать с конусом ветра шины).
            UnityEditor.Handles.color = new Color(1f, 0.55f, 0.1f, 0.95f);
            UnityEditor.Handles.DrawWireDisc(c, Vector3.up, _radius, 4f);
            UnityEditor.Handles.color = new Color(1f, 0.55f, 0.1f, 0.08f);
            UnityEditor.Handles.DrawSolidDisc(c, Vector3.up, _radius);
            GUIStyle st = new GUIStyle(UnityEditor.EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.6f, 0.15f) } };
            UnityEditor.Handles.Label(c + Vector3.right * _radius + Vector3.up * 0.2f, $"Tire Aimer — радиус {_radius:0.0} м", st);

            // Линия до ближайшего кострища: зелёная — в радиусе, красная — далеко.
            BonfirePit nearest = null; float nd = float.MaxValue;
            foreach (BonfirePit pit in FindObjectsByType<BonfirePit>(FindObjectsSortMode.None))
            {
                Vector3 d = pit.AirPoint - c;
                float h = new Vector2(d.x, d.z).magnitude;
                if (h < nd) { nd = h; nearest = pit; }
            }
            if (nearest != null)
            {
                bool inRange = nd <= _radius;
                UnityEditor.Handles.color = inRange ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.3f, 0.25f);
                UnityEditor.Handles.DrawDottedLine(c, nearest.AirPoint, 4f);
                UnityEditor.Handles.Label((c + nearest.AirPoint) * 0.5f + Vector3.up * 0.3f,
                    inRange ? $"кострище {nd:0.0} м ✓" : $"кострище {nd:0.0} м — дальше радиуса", st);
            }
            // Центр шины (отсюда меряется радиус).
            UnityEditor.Handles.color = new Color(1f, 0.55f, 0.1f, 1f);
            UnityEditor.Handles.DrawWireDisc(c, Vector3.up, 0.15f, 3f);
#endif
        }
    }
}
