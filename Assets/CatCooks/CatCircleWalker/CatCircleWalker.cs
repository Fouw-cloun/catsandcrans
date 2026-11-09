using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CatCooks.Level
{
    /// <summary>
    /// КОТ ХОДИТ ПО КРУГУ. Повесь на корень кота. Кот идёт по окружности с заданной скоростью, поворачивается носом по ходу
    /// и проигрывает анимацию ходьбы, подогнанную под скорость (ноги не скользят).
    /// Анимация — любым из двух способов:
    ///  1) перетащи клип ходьбы в «Walk Clip» — скрипт проиграет его сам, Animator Controller не нужен;
    ///  2) или оставь клип пустым и используй свой Animator Controller: скрипт будет писать скорость в параметр «Speed Param»
    ///     (число) и при желании — в «Walk Bool Param» (true, пока идёт).
    /// Круг: если Center пуст, центр берётся так, чтобы кот в начале стоял там, где ты его поставил.
    /// </summary>
    [DisallowMultipleComponent]
    public class CatCircleWalker : MonoBehaviour
    {
        [Header("Круг")]
        [Tooltip("Центр круга. Пусто — круг строится от места, где стоит кот")]
        public Transform Center;
        [Min(0.2f)] public float Radius = 4f;
        [Tooltip("Скорость по земле, м/с")]
        [Min(0f)] public float Speed = 1.2f;
        public bool Clockwise = true;
        [Tooltip("Идёт ли кот сейчас (можно выключать из других скриптов)")]
        public bool Moving = true;
        [Range(0.1f, 20f)] public float StartStopSmooth = 4f;

        [Header("Поворот модели")]
        [Tooltip("Если кот идёт боком или спиной — подкрути (обычно 0, 90, -90 или 180)")]
        public float ModelYaw = 0f;

        [Header("Земля")]
        public bool SnapToGround = false;
        public LayerMask GroundMask = ~0;
        [Min(0.1f)] public float GroundRayHeight = 2f;
        public float HeightOffset = 0f;

        [Header("Анимация")]
        public Animator Anim;
        [Tooltip("Клип ходьбы (если нет Animator Controller)")]
        public AnimationClip WalkClip;
        [Tooltip("С какой скоростью (м/с) клип ходьбы выглядит естественно при скорости проигрывания 1. Подбирай, пока ноги не скользят")]
        [Min(0.05f)] public float ClipNaturalSpeed = 1.2f;
        public bool MatchClipToSpeed = true;
        [Tooltip("Имя числового параметра Animator Controller, куда писать скорость (пусто — не писать)")]
        public string SpeedParam = "Speed";
        [Tooltip("Имя bool-параметра «идёт» (пусто — не писать)")]
        public string WalkBoolParam = "";

        public bool Log = true;

        private Vector3 _center;
        private float _angle;
        private float _cur;          // текущая скорость (плавно)
        private float _groundY;
        private PlayableGraph _graph;
        private AnimationClipPlayable _clipPlayable;
        private bool _graphOn;
        private int _speedHash, _boolHash;
        private bool _hasSpeed, _hasBool;

        private void Start()
        {
            if (Anim == null) Anim = GetComponentInChildren<Animator>(true);
            Vector3 pos = transform.position;
            _groundY = pos.y;
            float yaw = transform.eulerAngles.y;
            if (Center != null)
            {
                _center = Center.position;
                Vector3 d = pos - _center; d.y = 0f;
                _angle = d.sqrMagnitude > 0.0001f ? Mathf.Atan2(d.z, d.x) : 0f;
            }
            else
            {
                // кот стоит в точке круга, смотря по касательной в сторону движения: центр — слева/справа от него
                float dir = Clockwise ? -1f : 1f;
                Vector3 fwd = Quaternion.Euler(0f, yaw - ModelYaw, 0f) * Vector3.forward;
                // касательная при угле a: dir*(-sin a, cos a) = fwd → вектор от центра к коту r = (cos a, sin a)
                float a = Mathf.Atan2(-dir * fwd.x, dir * fwd.z);
                _angle = a;
                _center = pos - new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Radius;
                _center.y = pos.y;
            }
            if (Anim != null)
            {
                Anim.applyRootMotion = false;
                _speedHash = Animator.StringToHash(SpeedParam);
                _boolHash = Animator.StringToHash(WalkBoolParam);
                _hasSpeed = HasParam(SpeedParam, AnimatorControllerParameterType.Float);
                _hasBool = HasParam(WalkBoolParam, AnimatorControllerParameterType.Bool);
            }
            if (WalkClip != null) StartClip();
            else if (Anim == null && Log) Debug.LogWarning("[Кот] не найден Animator — кот будет ходить без анимации.", this);
            else if (Anim != null && Anim.runtimeAnimatorController == null && Log)
                Debug.LogWarning("[Кот] у Animator нет Controller, а клип ходьбы не назначен — анимации не будет. Перетащи клип в «Walk Clip».", this);
        }

        private bool HasParam(string name, AnimatorControllerParameterType type)
        {
            if (string.IsNullOrEmpty(name) || Anim == null || Anim.runtimeAnimatorController == null) return false;
            AnimatorControllerParameter[] ps = Anim.parameters;
            for (int i = 0; i < ps.Length; i++) if (ps[i].name == name && ps[i].type == type) return true;
            if (Log) Debug.LogWarning("[Кот] в Animator Controller нет параметра «" + name + "» (" + type + ") — пропускаю.", this);
            return false;
        }

        private void StartClip()
        {
            if (Anim == null) { if (Log) Debug.LogWarning("[Кот] для клипа нужен компонент Animator на коте или внутри него.", this); return; }
            _graph = PlayableGraph.Create("CatCircleWalker");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Walk", Anim);
            _clipPlayable = AnimationClipPlayable.Create(_graph, WalkClip);
            _clipPlayable.SetApplyFootIK(false);
            output.SetSourcePlayable(_clipPlayable);
            _graph.Play();
            _graphOn = true;
            if (Anim.runtimeAnimatorController != null && Log)
                Debug.Log("[Кот] и клип, и Animator Controller заданы — играет клип ходьбы (поверх Controller).", this);
        }

        private void OnDestroy()
        {
            if (_graphOn && _graph.IsValid()) _graph.Destroy();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float target = Moving ? Speed : 0f;
            _cur = Mathf.MoveTowards(_cur, target, Mathf.Max(0.01f, Speed) * StartStopSmooth * dt);

            if (_cur > 0.0001f)
            {
                float dir = Clockwise ? -1f : 1f;
                _angle += dir * (_cur / Radius) * dt;
                Vector3 p = _center + new Vector3(Mathf.Cos(_angle), 0f, Mathf.Sin(_angle)) * Radius;
                p.y = transform.position.y;
                Vector3 tan = dir * new Vector3(-Mathf.Sin(_angle), 0f, Mathf.Cos(_angle));
                transform.rotation = Quaternion.LookRotation(tan, Vector3.up) * Quaternion.Euler(0f, ModelYaw, 0f);
                p.y = GroundY(p);
                transform.position = p;
            }

            if (_graphOn && _clipPlayable.IsValid())
            {
                float mul = MatchClipToSpeed ? _cur / Mathf.Max(0.05f, ClipNaturalSpeed) : (_cur > 0.001f ? 1f : 0f);
                _clipPlayable.SetSpeed(mul);
                double len = WalkClip.length;
                if (len > 0.001 && _clipPlayable.GetTime() >= len) _clipPlayable.SetTime(_clipPlayable.GetTime() % len);
            }
            if (Anim != null)
            {
                if (_hasSpeed) Anim.SetFloat(_speedHash, _cur);
                if (_hasBool) Anim.SetBool(_boolHash, _cur > 0.05f);
            }
        }

        private float GroundY(Vector3 p)
        {
            if (!SnapToGround) return _groundY + HeightOffset;
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * GroundRayHeight, Vector3.down, out hit, GroundRayHeight * 2f + 5f, GroundMask, QueryTriggerInteraction.Ignore))
            {
                float y = hit.point.y + HeightOffset;
                return Mathf.Lerp(transform.position.y, y, 0.35f);
            }
            return transform.position.y;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 c = Application.isPlaying ? _center : (Center != null ? Center.position : transform.position);
            Gizmos.color = new Color(1f, 0.8f, 0.1f, 1f);
            Vector3 prev = c + new Vector3(Radius, 0f, 0f);
            for (int i = 1; i <= 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                Vector3 n = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Radius;
                Gizmos.DrawLine(prev, n);
                prev = n;
            }
        }
    }
}
