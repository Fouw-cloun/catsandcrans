using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v19.4: РАБОЧАЯ ПОЗА ИНСТРУМЕНТА НАД ТАРОЙ («постановка» миксера/венчика).
    ///
    /// Проблема: инструмент висит у камеры; подходишь к ведру — он бьётся о стенки, отъезжает к лицу,
    /// застревает, а когда появятся руки — они будут тянуться и выворачиваться.
    ///
    /// Решение (как в кухонных играх): навёлся на тару ближе Reach — инструмент «встаёт» в тару САМ:
    ///   • стоит ВЕРТИКАЛЬНО над горловиной (чуть наклонён к игроку), рабочий конец — внутри;
    ///   • ходит по кругу внутри тары за прицелом, но не дальше стенок (Inner Radius) — стенки не бьёт;
    ///   • без работы — кончик у поверхности, включил (ЛКМ) — опускается глубже в содержимое;
    ///   • вход и выход — плавно; камера инструмент не толкает, мир его не выталкивает.
    /// Поза привязана к ТАРЕ, а не к камере — руки потом просто берутся за ручку (IK), не растягиваясь:
    /// поза включается только если тара ближе Reach.
    ///
    /// Используется рукой (PlayerHand) для инструментов ISpinningTool без своей логики позы (миксер).
    /// </summary>
    public sealed class ToolWorkPose
    {
        public static bool Enabled = true;
        /// <summary>Дальность до горловины, м (дальше — обычная поза в руке).</summary>
        public static float Reach = 3.0f;
        /// <summary>Доля внутреннего радиуса, куда может уйти кончик (меньше — дальше от стенок).</summary>
        public static float InnerRadius = 0.55f;
        /// <summary>Наклон инструмента к игроку, градусы.</summary>
        public static float TiltToPlayer = 12f;
        /// <summary>Скорость входа/выхода из позы, 1/с.</summary>
        public static float BlendSpeed = 5f;

        private Cookware _cw;
        private float _k;
        private Vector3 _tipTarget;
        private bool _tipInit;
        private float _lostT;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <summary>Сейчас в рабочей позе (хоть частично).</summary>
        public bool Active => _k > 0.01f;
        public Cookware Target => _cw;

        /// <summary>
        /// item — инструмент (его поза в руке уже выставлена), tip — рабочий конец, working — крутится.
        /// Двигает item в рабочую позу (с плавным переходом). true — поза активна.
        /// </summary>
        public bool Apply(Transform cam, Transform item, Transform tip, bool working)
        {
            if (!Enabled || cam == null || item == null || tip == null) { _k = 0f; _cw = null; return false; }
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            Cookware aim = AimCookware(cam, item);
            if (aim != null) { _cw = aim; _lostT = 0f; }
            else if (_cw != null) { _lostT += dt; if (_lostT > 0.25f || !Close(cam, _cw)) _cw = null; }

            bool want = _cw != null && Upright(_cw);
            _k = Mathf.MoveTowards(_k, want ? 1f : 0f, dt * BlendSpeed);
            if (_k <= 0.001f) { _tipInit = false; return false; }
            if (_cw == null) return false;

            // ── геометрия тары ──
            Vector3 mouth = _cw.MouthPoint;
            float R = 0.3f, D = 0.5f;
            Collider iv = _cw.InsideVolume;
            if (iv != null)
            {
                Vector3 e = iv.bounds.extents;
                R = Mathf.Max(0.04f, Mathf.Min(e.x, e.z));
                D = Mathf.Max(0.05f, e.y * 2f);
            }
            float bottom = mouth.y - D;

            // куда смотрит прицел — на плоскости горловины, но внутри круга
            Vector3 o = cam.position, f = cam.forward;
            Vector3 onPlane = mouth;
            if (Mathf.Abs(f.y) > 1e-3f)
            {
                float t = (mouth.y - o.y) / f.y;
                if (t > 0f) onPlane = o + f * t;
            }
            Vector3 off = onPlane - mouth; off.y = 0f;
            off = Vector3.ClampMagnitude(off, R * InnerRadius);

            // высота кончика: у поверхности или глубже при работе
            float surf = mouth.y - D * 0.35f;
            CookwareLiquidVisual lv = _cw.GetComponentInChildren<CookwareLiquidVisual>();
            float sy;
            if (lv != null && lv.HasLiquid && lv.TryGetSurfaceY(mouth + off, out sy)) surf = Mathf.Min(sy, mouth.y);
            float dip = working ? D * 0.28f : D * 0.04f;
            float y = Mathf.Clamp(surf - dip, bottom + D * 0.08f, mouth.y);
            Vector3 want3 = mouth + off; want3.y = y;
            _tipTarget = _tipInit ? Vector3.Lerp(_tipTarget, want3, 1f - Mathf.Exp(-dt * 12f)) : want3;
            _tipInit = true;

            // ── поворот: ось «кончик → ручка» вертикально, чуть к игроку; поворот вокруг оси — как в руке ──
            Vector3 handPos = item.position;
            Quaternion handRot = item.rotation;
            Vector3 tipW = tip.position;
            Vector3 axisNow = handPos - tipW;
            if (axisNow.sqrMagnitude < 1e-6f) axisNow = handRot * Vector3.up;
            Vector3 toPlayer = o - _tipTarget; toPlayer.y = 0f;
            Vector3 up = toPlayer.sqrMagnitude > 1e-6f
                ? Quaternion.AngleAxis(-TiltToPlayer, Vector3.Cross(Vector3.up, toPlayer.normalized)) * Vector3.up
                : Vector3.up;
            Quaternion workRot = Quaternion.FromToRotation(axisNow.normalized, up) * handRot;
            Vector3 tipLocal = Quaternion.Inverse(handRot) * (tipW - handPos);
            Vector3 workPos = _tipTarget - workRot * tipLocal;

            float k = Mathf.SmoothStep(0f, 1f, _k);
            item.SetPositionAndRotation(Vector3.Lerp(handPos, workPos, k), Quaternion.Slerp(handRot, workRot, k));
            return true;
        }

        private bool Close(Transform cam, Cookware cw)
        {
            return cw != null && Vector3.Distance(cam.position, cw.MouthPoint) <= Reach * 1.15f;
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
                if (cw == null && c.isTrigger) continue;          // чужие триггеры не закрывают
                best = _hits[i].distance;
                res = cw;                                           // стена перед тарой — null
            }
            if (res != null && Vector3.Distance(cam.position, res.MouthPoint) > Reach) return null;
            return res;
        }
    }
}
