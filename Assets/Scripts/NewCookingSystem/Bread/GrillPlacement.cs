using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// «ПОСТАВИТЬ РОВНО» (ЛКМ с предметом в руке) — общее для формы, опалубки и прочей посуды.
    ///   • Смотришь в духовку — предмет встаёт дном на решётку, длинной стороной вдоль/поперёк прутьев
    ///     (кратно 90° — к тому, как держал), и не вылезает за край решётки.
    ///   • Смотришь на стол/пол (ровная поверхность) — ставится ровно туда, поворот как держал.
    ///   • Стена или ничего — не ставит (false).
    /// Только сервер. Поза «стоит ровно» — у предмета (запоминается при старте).
    /// </summary>
    public static class GrillPlacement
    {
        public struct Spot
        {
            public Vector3 Point;        // куда встаёт центр дна
            public Transform Frame;      // решётка (её поворот по вертикали) или null — мир
            public Vector2 HalfArea;     // полуразмер решётки в её осях (0 — без ограничений)
            public Vector3 Center;       // центр решётки (мир)
            public BakeOven Oven;        // духовка, если ставим в неё
        }

        /// <summary>Куда смотрит игрок: решётка духовки или ровная поверхность.</summary>
        public static bool Find(UseContext ctx, Transform item, float reach, out Spot spot)
        {
            spot = default;
            Vector3 dir = ctx.Direction.sqrMagnitude > 1e-6f ? ctx.Direction.normalized : Vector3.forward;
            RaycastHit[] hits = Physics.RaycastAll(ctx.Origin, dir, reach, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            Transform player = ctx.Hand != null ? ctx.Hand.transform.root : null;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(item)) continue;
                if (player != null && h.collider.transform.IsChildOf(player)) continue;

                BakeOven oven = h.collider.GetComponentInParent<BakeOven>();
                if (oven == null) oven = BakeOven.Find(h.point + Vector3.up * 0.05f);
                if (oven != null && oven.TryGetGrill(out Collider g))
                {
                    spot.Oven = oven;
                    if (g is BoxCollider bc)
                    {
                        Transform gt = g.transform;
                        Vector3 lp = gt.InverseTransformPoint(h.point);
                        Vector3 c = bc.center, e = bc.size * 0.5f;
                        lp.y = c.y + e.y;                                    // верх решётки
                        spot.Point = gt.TransformPoint(lp);
                        spot.Frame = gt;
                        Vector3 ls = gt.lossyScale;
                        spot.HalfArea = new Vector2(Mathf.Abs(e.x * ls.x), Mathf.Abs(e.z * ls.z));
                        spot.Center = gt.TransformPoint(c);
                    }
                    else
                    {
                        Bounds b = g.bounds;
                        spot.Point = new Vector3(h.point.x, b.max.y, h.point.z);
                        spot.Frame = null;
                        spot.HalfArea = new Vector2(b.extents.x, b.extents.z);
                        spot.Center = b.center;
                    }
                    return true;
                }
                if (h.normal.y > 0.6f) { spot.Point = h.point; return true; }     // стол, пол, полка
                return false;                                                       // стена — не ставим
            }
            return false;
        }

        /// <summary>Поза «стоит ровно», повёрнутая вокруг вертикали: по решётке — кратно 90° к ней, иначе — как держал.</summary>
        public static Quaternion UprightRot(Transform item, Quaternion upright, Transform frame)
        {
            Vector3 axis = LongAxisLocal(item, upright);
            Vector3 fu = Vector3.ProjectOnPlane(upright * axis, Vector3.up);
            Vector3 fc = Vector3.ProjectOnPlane(item.rotation * axis, Vector3.up);
            if (fu.sqrMagnitude < 1e-6f) fu = Vector3.forward;
            if (fc.sqrMagnitude < 1e-6f) fc = fu;
            float upYaw = Mathf.Atan2(fu.x, fu.z) * Mathf.Rad2Deg;
            float yaw = Mathf.Atan2(fc.x, fc.z) * Mathf.Rad2Deg;
            if (frame != null)
            {
                Vector3 ff = Vector3.ProjectOnPlane(frame.forward, Vector3.up);
                float fy = ff.sqrMagnitude > 1e-6f ? Mathf.Atan2(ff.x, ff.z) * Mathf.Rad2Deg : 0f;
                yaw = fy + Mathf.Round(Mathf.DeltaAngle(fy, yaw) / 90f) * 90f;
            }
            return Quaternion.Euler(0f, yaw - upYaw, 0f) * upright;
        }

        /// <summary>
        /// Влезает ли предмет на решётку в этом повороте; нет — пробуем повёрнутым на 90° (длинная опалубка
        /// во всю решётку встаёт только вдоль). Возвращает поворот, при котором меньше всего торчит.
        /// </summary>
        public static Quaternion FitRot(Transform item, Quaternion rot, Spot spot)
        {
            if (spot.HalfArea.sqrMagnitude <= 0f) return rot;
            Quaternion keep = item.rotation;
            Quaternion fr = spot.Frame != null ? Quaternion.Euler(0f, spot.Frame.eulerAngles.y, 0f) : Quaternion.identity;
            Quaternion inv = Quaternion.Inverse(fr);
            Quaternion best = rot; float bestOver = float.MaxValue;
            foreach (Quaternion r in new[] { rot, Quaternion.Euler(0f, 90f, 0f) * rot })
            {
                item.rotation = r;
                Bounds b = MeshBoundsIn(item, inv, item.position);
                float over = Mathf.Max(0f, b.extents.x - spot.HalfArea.x) + Mathf.Max(0f, b.extents.z - spot.HalfArea.y);
                if (over < bestOver - 1e-4f) { bestOver = over; best = r; }
            }
            item.rotation = keep;
            return best;
        }

        /// <summary>Поставить: дно — на уровень точки, центр — в точку (на решётке — не вылезая за край).</summary>
        public static void PlaceAt(Transform item, Rigidbody rb, Quaternion rot, Spot spot, float lift)
        {
            item.rotation = rot;
            Quaternion fr = spot.Frame != null ? Quaternion.Euler(0f, spot.Frame.eulerAngles.y, 0f) : Quaternion.identity;
            Quaternion inv = Quaternion.Inverse(fr);
            Vector3 o = item.position;
            Bounds b = MeshBoundsIn(item, inv, o);
            Vector3 tp = inv * (spot.Point - o);
            if (spot.HalfArea.sqrMagnitude > 0f)
            {
                Vector3 gc = inv * (spot.Center - o);
                float mx = Mathf.Max(0f, spot.HalfArea.x - b.extents.x), mz = Mathf.Max(0f, spot.HalfArea.y - b.extents.z);
                tp.x = gc.x + Mathf.Clamp(tp.x - gc.x, -mx, mx);
                tp.z = gc.z + Mathf.Clamp(tp.z - gc.z, -mz, mz);
            }
            Vector3 bottom = new Vector3(b.center.x, b.min.y, b.center.z);
            Vector3 pos = o + fr * (tp - bottom + Vector3.up * lift);
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                rb.position = pos; rb.rotation = rot;
            }
            item.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Длинная горизонтальная ось предмета (в его осях) в позе «стоит ровно».</summary>
        private static Vector3 LongAxisLocal(Transform item, Quaternion upright)
        {
            Bounds lb = MeshBoundsIn(item, Quaternion.Inverse(item.rotation), item.position);
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
            float[] ext = { lb.extents.x, lb.extents.y, lb.extents.z };
            Vector3 best = Vector3.forward; float bestE = -1f;
            for (int i = 0; i < 3; i++)
            {
                float horiz = 1f - Mathf.Abs(Vector3.Dot((upright * ax[i]).normalized, Vector3.up));
                if (horiz < 0.7f) continue;
                if (ext[i] > bestE) { bestE = ext[i]; best = ax[i]; }
            }
            return best;
        }

        /// <summary>Габарит мешей предмета в рамке (inv — обратный поворот рамки, o — начало).</summary>
        private static Bounds MeshBoundsIn(Transform item, Quaternion inv, Vector3 o)
        {
            bool any = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter mf in item.GetComponentsInChildren<MeshFilter>(true))
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
            return b;
        }
    }
}
