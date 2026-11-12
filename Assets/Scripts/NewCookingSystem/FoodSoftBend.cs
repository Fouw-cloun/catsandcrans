using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// МЯГКИЙ ПРОГИБ, как у мармеладного червяка: гроздь какао-зёрен, сосиска, тесто-жгут.
    ///
    /// Чисто картинка (у каждого игрока своя, по сети не ходит, коллайдер не гнётся).
    /// Меш гнётся по своей длинной оси:
    ///   • дёрнул / несёшь / упал и ударился — концы отстают от движения и пружинят (дуга «U»);
    ///   • крутанул — концы отстают от поворота (лёгкая «S»).
    /// Успокоился — меш возвращается к исходной форме и больше не пересчитывается.
    ///
    /// Вешается на корень предмета (гнёт все меши внутри; можно указать один — Model).
    /// Меш должен быть Read/Write.
    /// </summary>
    public sealed class FoodSoftBend : MonoBehaviour
    {
        [Tooltip("Какой меш гнуть. Пусто — все MeshFilter внутри")]
        [SerializeField] private MeshFilter _model;
        [Tooltip("Ось изгиба в осях меша. Ноль — самая длинная сторона")]
        [SerializeField] private Vector3 _axis = Vector3.zero;

        [Header("Сколько гнуться")]
        [Tooltip("Максимальный прогиб концов, доля половины длины (0.12 — заметно, но не сильно)")]
        [Range(0.01f, 0.4f)][SerializeField] private float _maxBend = 0.12f;
        [Tooltip("Чувствительность к рывкам и ударам")]
        [Range(0f, 0.2f)][SerializeField] private float _accelGain = 0.02f;
        [Tooltip("Чувствительность к вращению")]
        [Range(0f, 0.5f)][SerializeField] private float _spinGain = 0.05f;

        [Header("Пружина")]
        [Tooltip("Жёсткость: больше — быстрее и мельче колышется")]
        [Range(5f, 400f)][SerializeField] private float _stiffness = 90f;
        [Tooltip("Затухание: больше — меньше колыханий после удара")]
        [Range(0.5f, 40f)][SerializeField] private float _damping = 7f;

        [Header("v18: только сырое")]
        [Tooltip("Обжаренное (FoodRoastable) больше не пружинит: гнётся только сырое. По мере обжарки — всё твёрже")]
        [SerializeField] private bool _onlyRaw = true;
        private FoodRoastable _roast;

        private struct Part
        {
            public MeshFilter Filter;
            public Mesh Mesh;
            public Vector3[] Base;
            public Vector3[] Work;
            public Vector3 Axis, Center;
            public float Half;
        }

        private Part[] _parts;
        private Vector3 _prevPos, _prevVel, _acc;
        private Quaternion _prevRot;
        private Vector3 _u, _uVel, _s, _sVel;      // изгиб «U» и «S» в осях корня (доля половины длины)
        private bool _bent, _started;

        private void Start()
        {
            MeshFilter[] filters = _model != null ? new[] { _model } : GetComponentsInChildren<MeshFilter>(true);
            var list = new System.Collections.Generic.List<Part>();
            foreach (MeshFilter mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (!mf.sharedMesh.isReadable)
                {
                    Debug.LogWarning($"[Мягкий изгиб] «{name}/{mf.name}»: меш не Read/Write — не гнётся", this);
                    continue;
                }
                Mesh inst = Instantiate(mf.sharedMesh);
                inst.name = mf.sharedMesh.name + " (bend)";
                inst.MarkDynamic();
                mf.sharedMesh = inst;
                Bounds b = inst.bounds;
                Vector3 ax = _axis.sqrMagnitude > 1e-6f ? _axis.normalized
                    : b.size.x >= b.size.y && b.size.x >= b.size.z ? Vector3.right : b.size.y >= b.size.z ? Vector3.up : Vector3.forward;
                Vector3[] v = inst.vertices;
                list.Add(new Part
                {
                    Filter = mf, Mesh = inst, Base = v, Work = (Vector3[])v.Clone(),
                    Axis = ax, Center = b.center, Half = Mathf.Max(1e-4f, Mathf.Abs(Vector3.Dot(b.extents, ax)))
                });
            }
            _parts = list.ToArray();
            _prevPos = transform.position;
            _prevRot = transform.rotation;
            _roast = GetComponentInParent<FoodRoastable>();
            _started = true;
        }

        private void LateUpdate()
        {
            if (!_started || _parts == null || _parts.Length == 0) return;
            float dt = Time.deltaTime;
            if (dt <= 1e-5f) return;

            // движение предмета (видимое — работает и у тех, кто просто смотрит)
            Vector3 pos = transform.position;
            Vector3 vel = (pos - _prevPos) / dt;
            Vector3 acc = (vel - _prevVel) / dt;
            _acc = Vector3.Lerp(_acc, acc, 1f - Mathf.Exp(-dt * 25f));     // сгладить рывки сетевой интерполяции
            Quaternion dq = transform.rotation * Quaternion.Inverse(_prevRot);
            dq.ToAngleAxis(out float ang, out Vector3 angAxis);
            if (ang > 180f) ang -= 360f;
            Vector3 omega = float.IsNaN(angAxis.x) || Mathf.Abs(ang) > 60f ? Vector3.zero : angAxis * (ang * Mathf.Deg2Rad / dt);   // > 60° за кадр — телепорт
            _prevPos = pos; _prevVel = vel; _prevRot = transform.rotation;

            // телепорт / спавн — не гнуть
            if (vel.sqrMagnitude > 900f) { _acc = Vector3.zero; return; }

            Vector3 accL = transform.InverseTransformDirection(_acc);
            Vector3 omL = transform.InverseTransformDirection(omega);

            // цели: концы отстают от ускорения (U) и от вращения (S)
            Vector3 uT = -accL * _accelGain;
            Vector3 sT = Vector3.zero;
            if (_spinGain > 0f)
            {
                Vector3 ax = transform.InverseTransformDirection(_parts[0].Filter.transform.TransformDirection(_parts[0].Axis)).normalized;
                sT = -Vector3.Cross(omL, ax) * _spinGain;
            }
            // v18: обжаренное — твёрдое (сырое пружинит, по мере обжарки меньше, готовое — совсем нет)
            if (_onlyRaw && _roast != null)
            {
                float soft = _roast.IsRoasted ? 0f : 1f - _roast.Roast01;
                uT *= soft; sT *= soft;
            }
            Spring(ref _u, ref _uVel, uT, dt);
            Spring(ref _s, ref _sVel, sT, dt);

            bool active = _u.sqrMagnitude > 1e-9f || _s.sqrMagnitude > 1e-9f || _uVel.sqrMagnitude > 1e-7f || _sVel.sqrMagnitude > 1e-7f;
            if (!active)
            {
                if (_bent) { Restore(); _bent = false; }
                return;
            }
            Deform();
            _bent = true;
        }

        private void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float dt)
        {
            // полу-неявный Эйлер, шаг дробим — пружина не взрывается на рывке кадра
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = (target - x) * _stiffness - v * _damping;
                v += a * h;
                x += v * h;
            }
            if (x.sqrMagnitude < 1e-10f && v.sqrMagnitude < 1e-8f) { x = Vector3.zero; v = Vector3.zero; }
        }

        private void Deform()
        {
            for (int p = 0; p < _parts.Length; p++)
            {
                Part part = _parts[p];
                Transform mt = part.Filter.transform;
                // изгиб — в осях меша, только поперёк оси; максимум — доля половины длины
                Vector3 u = ToMesh(mt, _u), s = ToMesh(mt, _s);
                u -= part.Axis * Vector3.Dot(u, part.Axis);
                s -= part.Axis * Vector3.Dot(s, part.Axis);
                float lim = _maxBend * part.Half;
                u *= part.Half; s *= part.Half;
                float sum = u.magnitude + s.magnitude;
                if (sum > lim) { float k = lim / sum; u *= k; s *= k; }      // вместе — не больше предела
                Vector3[] b = part.Base, w = part.Work;
                for (int i = 0; i < b.Length; i++)
                {
                    float t = Vector3.Dot(b[i] - part.Center, part.Axis) / part.Half;   // −1..1 вдоль оси
                    w[i] = b[i] + u * (t * t) + s * (t * Mathf.Abs(t));
                }
                part.Mesh.vertices = w;
                part.Mesh.RecalculateBounds();
            }
        }

        private Vector3 ToMesh(Transform meshT, Vector3 rootLocal)
        {
            Vector3 world = transform.TransformDirection(rootLocal);
            return meshT.InverseTransformDirection(world);
        }

        private void Restore()
        {
            foreach (Part part in _parts)
            {
                part.Mesh.vertices = part.Base;
                part.Mesh.RecalculateBounds();
            }
        }

        private void OnDestroy()
        {
            if (_parts == null) return;
            foreach (Part part in _parts) if (part.Mesh != null) SafeDestroy.Kill(part.Mesh);
        }
    }
}
