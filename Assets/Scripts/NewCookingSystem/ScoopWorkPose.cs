using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>v23.4: инструмент-черпалка сообщает свою «рабочую точку» (полотно мастерка/лопаты, чаша черпака).</summary>
    public interface IScoopPoseTool
    {
        Transform ScoopPoint { get; }
    }

    /// <summary>
    /// v23.4: РАБОЧАЯ ПОЗА ЧЕРПАЛКИ НАД ТАРОЙ (мастерок, лопата, ложка, черпак) — как у миксера.
    /// Навёлся на тару ближе Reach — полотно само «зависает» над содержимым в точке прицела,
    /// внутри стенок (Inner Radius), чуть выше поверхности. Стенки не бьёт, к лицу не едет, сквозь тару не проходит.
    /// Поворот — как в руке (свои движения инструмента сохраняются), двигается только положение.
    /// Пока зажата ПКМ (рогатка) или ЛКМ дольше мгновения (растирание) — поза отпускает инструмент.
    /// </summary>
    public sealed class ScoopWorkPose
    {
        public static bool Enabled = false;                 // v23.5: выключено — «магнит» к таре был лишним (теперь ScoopAnimator)
        public static float Reach = 3.2f;
        public static float InnerRadius = 0.5f;
        public static float BlendSpeed = 6f;

        private Cookware _cw;
        private float _k, _lostT, _lmbT;
        private Vector3 _target;
        private bool _init;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        public bool Active => _k > 0.01f;

        public bool Apply(Transform cam, Transform item, Transform point)
        {
            if (!Enabled || cam == null || item == null || point == null) { _k = 0f; _cw = null; return false; }
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _lmbT = Input.GetMouseButton(0) ? _lmbT + dt : 0f;
            bool suspend = Input.GetMouseButton(1) || _lmbT > 0.3f;

            Cookware aim = suspend ? null : AimCookware(cam, item);
            if (aim != null) { _cw = aim; _lostT = 0f; }
            else if (_cw != null) { _lostT += dt; if (suspend || _lostT > 0.25f || Vector3.Distance(cam.position, _cw.MouthPoint) > Reach * 1.15f) _cw = null; }

            bool want = _cw != null && Upright(_cw);
            _k = Mathf.MoveTowards(_k, want ? 1f : 0f, dt * BlendSpeed);
            if (_k <= 0.001f || _cw == null) { _init = false; return false; }

            Vector3 mouth = _cw.MouthPoint;
            float R = 0.3f, D = 0.5f;
            Collider iv = _cw.InsideVolume;
            if (iv != null)
            {
                Vector3 e = iv.bounds.extents;
                R = Mathf.Max(0.04f, Mathf.Min(e.x, e.z));
                D = Mathf.Max(0.05f, e.y * 2f);
            }
            Vector3 o = cam.position, f = cam.forward;
            Vector3 onPlane = mouth;
            if (Mathf.Abs(f.y) > 1e-3f)
            {
                float t = (mouth.y - o.y) / f.y;
                if (t > 0f) onPlane = o + f * t;
            }
            Vector3 off = onPlane - mouth; off.y = 0f;
            off = Vector3.ClampMagnitude(off, R * InnerRadius);
            // высота: чуть выше содержимого (или середина тары, если пусто), не выше горловины
            float surf = mouth.y - D * 0.6f;
            CookwareLiquidVisual lv = _cw.GetComponentInChildren<CookwareLiquidVisual>();
            float sy;
            if (lv != null && lv.HasLiquid && lv.TryGetSurfaceY(mouth + off, out sy)) surf = sy;
            float y = Mathf.Clamp(surf + D * 0.06f, mouth.y - D * 0.85f, mouth.y + D * 0.05f);
            Vector3 tgt = mouth + off; tgt.y = y;
            _target = _init ? Vector3.Lerp(_target, tgt, 1f - Mathf.Exp(-dt * 12f)) : tgt;
            _init = true;

            Vector3 delta = _target - point.position;                     // двигаем весь инструмент так, чтобы полотно встало в точку
            float k = Mathf.SmoothStep(0f, 1f, _k);
            item.position += delta * k;
            return true;
        }

        private static bool Upright(Cookware cw)
        {
            Collider iv = cw.InsideVolume;
            Vector3 up = iv != null ? iv.transform.up : cw.transform.up;
            return Vector3.Dot(up, Vector3.up) > 0.6f || iv == null;
        }

        private Cookware AimCookware(Transform cam, Transform item)
        {
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, Reach, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Cookware res = null;
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i].collider;
                if (c == null || c.transform.IsChildOf(item)) continue;
                if (c.GetComponentInParent<FirstPersonController>() != null) continue;
                if (_hits[i].distance >= best) continue;
                Cookware cw = c.GetComponentInParent<Cookware>();
                if (cw == null && c.isTrigger) continue;
                best = _hits[i].distance;
                res = cw;
            }
            if (res != null && Vector3.Distance(cam.position, res.MouthPoint) > Reach) return null;
            return res;
        }

        /// <summary>Рабочая точка черпалки: интерфейс, иначе полотно (дочерний FoodHeapRenderer у мастерка/лопаты).</summary>
        public static Transform FindPoint(Component item)
        {
            if (item == null) return null;
            IScoopPoseTool t = item as IScoopPoseTool;
            if (t != null) return t.ScoopPoint;
            if (item.GetType().Name != "ScoopItem") return null;
            FoodHeapRenderer hr = item.GetComponentInChildren<FoodHeapRenderer>(true);
            if (hr != null && hr.transform != item.transform) return hr.transform;
            Transform b = item.transform.Find("Blade (auto)");
            return b;
        }
    }
}
