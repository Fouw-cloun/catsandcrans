using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// «ВОДИТЬ ПО МЯСУ» — общее для мастерка (растереть специи), кисти (соус) и рубанка (плёнка).
    /// Зажал ЛКМ на рёбрах — камера стоит (инструмент сам зовёт HeldToolLook), а мышь двигает
    /// точку по поверхности рёбер: влево-вправо — вдоль пласта, вперёд-назад — вдоль костей.
    /// Раз в Send Every секунд — «сколько провёл» (метры): инструмент шлёт это серверу.
    /// Стоишь на месте — ничего не намазывается: надо именно водить.
    /// Только клиент-владелец инструмента.
    /// </summary>
    /// <summary>
    /// Луч «куда смотрю» для инструментов: пропускает сам инструмент и СВОЕГО игрока (уши, лапы, тело кота —
    /// под углом вниз луч упирался в них, и инструмент «срабатывал только под определённым углом»).
    /// Дальность растёт с масштабом мяса (куски крупные).
    /// </summary>
    public static class ToolRay
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        public static float Reach(float reach) => Mathf.Max(reach, Mathf.Clamp(1.5f * MeatCutSettings.WorldScale, 3f, 8f));

        public static bool Cast(Camera cam, float reach, Transform tool, out RaycastHit hit, QueryTriggerInteraction q = QueryTriggerInteraction.Ignore)
        {
            hit = new RaycastHit();
            if (cam == null) return false;
            Transform me = cam.transform.root;
            bool mine = me != null && me != cam.transform && me.GetComponentInChildren<Camera>() == cam;
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, Hits, Reach(reach), ~0, q);
            float best = float.MaxValue; bool found = false;
            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i].collider;
                if (c == null || Hits[i].distance >= best) continue;
                Transform t = c.transform;
                if (tool != null && t.IsChildOf(tool)) continue;
                if (mine && t.IsChildOf(me)) continue;                      // свой игрок (камера внутри него)
                if (Hits[i].distance < 0.2f) continue;                        // впритык к камере — своя голова/уши
                if (c is CharacterController) continue;
                best = Hits[i].distance; hit = Hits[i]; found = true;
            }
            return found;
        }
    }

    public sealed class RackStroke
    {
        public RibsRack Rack { get; private set; }
        public int Side { get; private set; }
        public Vector3 Local;                       // точка на рёбрах (их оси)
        public bool Active => Rack != null;
        public float Sensitivity = 0.0032f;         // м на единицу оси мыши
        public float SendEvery = 0.08f;

        private float _acc, _sendAt;

        /// <summary>ЛКМ нажали: начать, если смотришь на рёбра и allow(рёбра) разрешает.</summary>
        public bool TryBegin(Camera cam, float reach, System.Func<RibsRack, bool> allow, out RibsRack aimed, Transform tool = null)
        {
            aimed = null;
            if (cam == null) return false;
            if (!ToolRay.Cast(cam, reach, tool, out RaycastHit hit)) return false;
            RibsRack r = hit.collider != null ? hit.collider.GetComponentInParent<RibsRack>() : null;
            aimed = r;
            if (r == null || (allow != null && !allow(r))) return false;
            Rack = r;
            Side = r.UpSide;
            Local = r.transform.InverseTransformPoint(hit.point);
            _acc = 0f; _sendAt = Time.time + SendEvery;
            return true;
        }

        public void End() => Rack = null;

        /// <summary>Каждый кадр, пока ЛКМ зажата. true — пора отправить stroke (метры, сколько провёл).</summary>
        public bool Tick(Camera cam, out float stroke)
        {
            stroke = 0f;
            if (Rack == null || cam == null || !Rack.isActiveAndEnabled) { End(); return false; }
            float mx = Input.GetAxis("Mouse X"), my = Input.GetAxis("Mouse Y");
            Vector3 right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up);
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            Vector3 wd = (right.normalized * mx + fwd.normalized * my) * Sensitivity;
            Vector3 ld = Rack.transform.InverseTransformVector(wd);
            ld.y = 0f;
            Bounds b = Rack.LocalBounds;
            Vector3 was = Local;
            Local.x = Mathf.Clamp(Local.x + ld.x, b.min.x, b.max.x);
            Local.z = Mathf.Clamp(Local.z + ld.z, b.min.z, b.max.z);
            Local.y = Side == 0 ? b.max.y * 0.85f : b.min.y * 0.85f;
            _acc += new Vector2(Local.x - was.x, Local.z - was.z).magnitude;
            if (Time.time < _sendAt) return false;
            _sendAt = Time.time + SendEvery;
            if (_acc < 0.0005f) { _acc = 0f; return false; }
            stroke = _acc; _acc = 0f;
            return true;
        }

        /// <summary>Где сейчас «кончик» инструмента (мир) и куда смотрит поверхность.</summary>
        public Vector3 WorldPoint => Rack != null ? Rack.transform.TransformPoint(Local) : Vector3.zero;
        public Vector3 WorldNormal => Rack != null ? (Side == 0 ? Rack.transform.up : -Rack.transform.up) : Vector3.up;

        /// <summary>Поза инструмента в руке, пока водишь: над точкой, чуть приподнят.</summary>
        public void Pose(Transform tool, float hover, float follow = 0.75f)
        {
            if (Rack == null || tool == null) return;
            Vector3 n = WorldNormal;
            if (Vector3.Dot(n, Vector3.up) < 0f) n = Vector3.up;      // костяная сторона вверху — тоже сверху
            tool.position = Vector3.Lerp(tool.position, WorldPoint + n * hover, follow);
        }
    }
}
