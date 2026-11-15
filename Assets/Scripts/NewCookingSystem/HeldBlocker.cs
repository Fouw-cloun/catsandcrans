using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v16: ПРЕДМЕТ В РУКЕ НЕ ПРОХОДИТ СКВОЗЬ МИР — общий помощник для инструментов (горелки).
    ///
    /// Габарит предмета — коробка по его мешам. Каждый кадр ищется, насколько далеко от камеры
    /// к руке предмет может встать, ни во что не влезая (бисекция по ходу 0..1): упёрся — отъезжает
    /// к камере, отвёл — догоняет руку. Игроки и сам предмет не мешают.
    ///
    /// Использование: в UpdateHeldPose — pos = blocker.Solve(pos, rot, камера).
    /// </summary>
    public sealed class HeldBlocker
    {
        public LayerMask Mask = ~0;
        /// <summary>v23.4: насколько МАКСИМУМ отъезжает к камере (0.75 — всего на четверть пути; раньше 0.3 — почти в лицо).</summary>
        public float RetractTo = 0.75f;
        /// <summary>v23.4: дальше не отъезжает, а «опускается» вниз из кадра (доля расстояния до камеры) — как оружие у стены в шутерах.</summary>
        public float LowerBy = 0.6f;
        /// <summary>v23.4: и наклоняется кончиком вниз, градусы (только SolvePose).</summary>
        public float TiltDeg = 40f;
        /// <summary>Скорость возврата в руку, доля в секунду (упирается — сразу, отпускает — плавно).</summary>
        public float FreeSpeed = 4f;
        /// <summary>Габарит чуть меньше модели — не цепляться краешком.</summary>
        public float Shrink = 0.88f;
        /// <summary>v17.3: не упираться в тару и продукты (чтобы черпать, мешать, долбить).</summary>
        public bool IgnoreWorkTargets;

        private readonly Transform _root;
        private bool _ready;
        private Vector3 _c, _h;
        private float _p;                      // 0 — в руке, 1 — максимально «спрятан»
        private readonly Collider[] _ov = new Collider[24];

        public HeldBlocker(Transform root) { _root = root; }

        /// <summary>Упёрся ли сейчас (для подсказок/звука).</summary>
        public bool Blocked { get; private set; }

        /// <summary>
        /// Только позиция (старые вызовы: отбойник, горелки). v23.4: больше НЕ летит в экран —
        /// чуть отъезжает и опускается вниз из кадра.
        /// </summary>
        public Vector3 Solve(Vector3 want, Quaternion rot, Vector3 cam)
        {
            Camera mc = Camera.main;
            Vector3 up = mc != null ? mc.transform.up : Vector3.up;
            Vector3 right = mc != null ? mc.transform.right : Vector3.right;
            Quaternion r = rot;
            return SolvePose(want, ref r, cam, up, right, false);
        }

        /// <summary>
        /// v23.4: поза без проходов сквозь мир. Ход p 0..1: до 0.35 — немного к камере (не дальше Retract To),
        /// дальше — вниз из кадра и (tilt) кончиком вниз. Ищем наименьший p, при котором предмет ни во что не влез.
        /// Упёрся — сразу, отпустило — плавно назад в руку.
        /// </summary>
        public Vector3 SolvePose(Vector3 want, ref Quaternion rot, Vector3 cam, Vector3 camUp, Vector3 camRight, bool tilt)
        {
            if (_root == null) return want;
            if (!_ready) Build();
            Quaternion baseRot = rot;
            float need;
            Vector3 pos; Quaternion r;
            Pose(want, baseRot, cam, camUp, camRight, tilt, 0f, out pos, out r);
            if (!Hit(pos, r)) need = 0f;
            else
            {
                Pose(want, baseRot, cam, camUp, camRight, tilt, 1f, out pos, out r);
                if (Hit(pos, r)) need = 1f;
                else
                {
                    float lo = 0f, hi = 1f;
                    for (int i = 0; i < 7; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        Pose(want, baseRot, cam, camUp, camRight, tilt, mid, out pos, out r);
                        if (Hit(pos, r)) lo = mid; else hi = mid;
                    }
                    need = hi;
                }
            }
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _p = need >= _p ? need : Mathf.MoveTowards(_p, need, FreeSpeed * dt);
            Blocked = need > 0.001f;
            Pose(want, baseRot, cam, camUp, camRight, tilt, _p, out pos, out r);
            rot = r;
            return pos;
        }

        private void Pose(Vector3 want, Quaternion rot, Vector3 cam, Vector3 camUp, Vector3 camRight, bool tilt, float p, out Vector3 pos, out Quaternion r)
        {
            float a = Mathf.Clamp01(p / 0.35f);
            float b = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, p));
            Vector3 back = cam + (want - cam) * RetractTo;
            float dist = (want - cam).magnitude;
            pos = Vector3.Lerp(want, back, a) - camUp * (dist * LowerBy * b);
            r = tilt && b > 0f ? Quaternion.AngleAxis(TiltDeg * b, camRight) * rot : rot;
        }

        private bool Hit(Vector3 pos, Quaternion rot)
        {
            Vector3 ls = _root.lossyScale;
            Vector3 c = pos + rot * Vector3.Scale(_c, ls);
            Vector3 h = Vector3.Scale(_h, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z))) * Shrink;
            int n = Physics.OverlapBoxNonAlloc(c, h, _ov, rot, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider col = _ov[i];
                if (col == null || !col.enabled) continue;
                if (col.transform.IsChildOf(_root)) continue;
                if (col.GetComponentInParent<CharacterController>() != null) continue;
                if (col.GetComponentInParent<FirstPersonController>() != null) continue;
                if (IgnoreWorkTargets && (IsSmallCookware(col) || col.GetComponentInParent<FoodPiece>() != null)) continue;
                return true;
            }
            return false;
        }

        // v19: сквозь мелкую тару (ведро, кастрюля) инструмент проходит — чтобы черпать и мешать;
        // крупная (ковш экскаватора, бетономешалка) — твёрдая, сквозь неё не пройти.
        public static float BigCookwareSize = 1.2f;
        private static readonly System.Collections.Generic.Dictionary<Cookware, bool> _small = new System.Collections.Generic.Dictionary<Cookware, bool>();

        private static bool IsSmallCookware(Collider col)
        {
            Cookware cw = col.GetComponentInParent<Cookware>();
            if (cw == null) return false;
            bool small;
            if (_small.TryGetValue(cw, out small)) return small;
            Bounds b = new Bounds(cw.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in cw.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float size = any ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : 0.5f;
            small = size < BigCookwareSize;
            _small[cw] = small;
            if (!small) Debug.Log($"[В руке] «{cw.name}» крупная тара ({size:0.0} м) — инструменты сквозь неё не проходят");
            return small;
        }

        private void Build()
        {
            _ready = true;
            bool any = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            Vector3 ls = _root.lossyScale;
            Vector3 inv = new Vector3(1f / Mathf.Max(1e-5f, Mathf.Abs(ls.x)), 1f / Mathf.Max(1e-5f, Mathf.Abs(ls.y)), 1f / Mathf.Max(1e-5f, Mathf.Abs(ls.z)));
            Quaternion ir = Quaternion.Inverse(_root.rotation);
            foreach (MeshFilter mf in _root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || (mf.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                Renderer r = mf.GetComponent<Renderer>();
                if (r != null && !r.enabled) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 v = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    Vector3 l = ir * (mf.transform.TransformPoint(v) - _root.position);
                    if (!any) { b = new Bounds(l, Vector3.zero); any = true; } else b.Encapsulate(l);
                }
            }
            if (!any) b = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            _c = Vector3.Scale(b.center, inv);
            _h = Vector3.Scale(b.extents, inv);
            Debug.Log($"[В руке] «{_root.name}»: габарит {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00} м — сквозь предметы не проходит", _root);
        }
    }
}
