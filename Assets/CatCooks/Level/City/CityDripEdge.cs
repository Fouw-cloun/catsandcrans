using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// С ЭТОГО КРАЯ КАПАЕТ — в дождь и немного после (пока мокро). Вешается на балку, навес, козырёк, вывеску.
    ///   • Bottom Edges — капли по нижнему периметру объекта (габарит всех его мешей): балки, козырьки, тенты.
    ///   • Line — по отрезку A→B (в осях объекта): перила, край крыши, провод. Отрезок виден в сцене, когда объект выделен.
    /// Зонтам ничего вешать не надо — они капают с края купола сами.
    /// </summary>
    [ExecuteAlways]
    public class CityDripEdge : MonoBehaviour
    {
        public enum Shape { BottomEdges, Line }
        public static readonly List<CityDripEdge> All = new List<CityDripEdge>();

        public Shape Mode = Shape.BottomEdges;
        [Tooltip("Расстояние между точками капели, м")]
        public float Spacing = 0.45f;
        [Tooltip("Отрезок для режима Line (в осях объекта)")]
        public Vector3 LocalA = new Vector3(-0.5f, 0f, 0f), LocalB = new Vector3(0.5f, 0f, 0f);
        [Tooltip("Как часто капает (множитель)")]
        [Range(0f, 2f)] public float Amount = 1f;

        private static readonly List<Renderer> _rs = new List<Renderer>();

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        /// <summary>Меняется, когда объект сдвинули/повернули или поменяли настройки — тогда точки пересобираются.</summary>
        public int Signature()
        {
            Vector3 p = transform.position, s = transform.lossyScale;
            Quaternion q = transform.rotation;
            unchecked
            {
                int h = (int)Mode * 31 + Mathf.RoundToInt(Spacing * 100f);
                h = h * 31 + Mathf.RoundToInt(p.x * 20f); h = h * 31 + Mathf.RoundToInt(p.y * 20f); h = h * 31 + Mathf.RoundToInt(p.z * 20f);
                h = h * 31 + Mathf.RoundToInt(q.x * 500f); h = h * 31 + Mathf.RoundToInt(q.y * 500f); h = h * 31 + Mathf.RoundToInt(q.w * 500f);
                h = h * 31 + Mathf.RoundToInt(s.x * 50f) + Mathf.RoundToInt(s.y * 70f) + Mathf.RoundToInt(s.z * 90f);
                h = h * 31 + Mathf.RoundToInt(Amount * 50f);
                if (Mode == Shape.Line) h = h * 31 + LocalA.GetHashCode() * 7 + LocalB.GetHashCode();
                return h;
            }
        }

        /// <summary>Точки капели в мире: xyz позиция, w — множитель частоты.</summary>
        public void Collect(List<Vector4> pts, float meter, int max)
        {
            float step = Mathf.Max(0.1f, Spacing) * meter;
            if (Mode == Shape.Line)
            {
                AddLine(pts, transform.TransformPoint(LocalA), transform.TransformPoint(LocalB), step, max);
                return;
            }
            _rs.Clear();
            GetComponentsInChildren<Renderer>(false, _rs);
            bool any = false;
            Bounds b = new Bounds(transform.position, Vector3.zero);
            for (int i = 0; i < _rs.Count; i++)
            {
                Renderer r = _rs[i];
                if (r == null || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            _rs.Clear();
            if (!any) return;
            float y = b.min.y;
            Vector3 a = new Vector3(b.min.x, y, b.min.z), c = new Vector3(b.max.x, y, b.max.z);
            Vector3 p1 = new Vector3(c.x, y, a.z), p3 = new Vector3(a.x, y, c.z);
            if (b.size.z < step * 0.8f) { AddLine(pts, new Vector3(a.x, y, b.center.z), new Vector3(c.x, y, b.center.z), step, max); return; }   // тонкая балка вдоль X
            if (b.size.x < step * 0.8f) { AddLine(pts, new Vector3(b.center.x, y, a.z), new Vector3(b.center.x, y, c.z), step, max); return; }   // вдоль Z
            AddLine(pts, a, p1, step, max);
            AddLine(pts, p1, c, step, max);
            AddLine(pts, c, p3, step, max);
            AddLine(pts, p3, a, step, max);
        }

        private void AddLine(List<Vector4> pts, Vector3 a, Vector3 b, float step, int max)
        {
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / step));
            for (int i = 0; i < n && pts.Count < max; i++)
            {
                float j = Mathf.Repeat(Mathf.Sin((a.x + i * 1.37f) * 12.9898f + a.z * 78.233f) * 43758.5f, 1f);   // неровный шаг
                float t = (i + 0.5f + (j - 0.5f) * 0.5f) / n;
                Vector3 p = Vector3.Lerp(a, b, t);
                pts.Add(new Vector4(p.x, p.y, p.z, Amount));
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.75f, 1f, 0.9f);
            List<Vector4> pts = new List<Vector4>();
            float m = CityRain.Instance != null ? CityRain.Instance.Meter : 3.5f;
            Collect(pts, m, 400);
            float r = 0.04f * m;
            foreach (Vector4 p in pts) Gizmos.DrawLine(new Vector3(p.x, p.y, p.z), new Vector3(p.x, p.y - r * 3f, p.z));
            if (Mode == Shape.Line) Gizmos.DrawLine(transform.TransformPoint(LocalA), transform.TransformPoint(LocalB));
        }
    }
}
