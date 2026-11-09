using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Цвет выпечки: плавный переход «тесто → золотистое → корочка» без скачков.
    /// Красит через MaterialPropertyBlock базовый цвет материала (_BaseColor / _Color),
    /// текстура материала остаётся.
    /// </summary>
    public static class BakeColor
    {
        public static readonly Color DefaultGold = new Color(0.93f, 0.72f, 0.42f);
        public static readonly Color DefaultCrust = new Color(0.78f, 0.49f, 0.23f);

        /// <summary>Имя свойства цвета материала (URP Lit — _BaseColor, Standard — _Color).</summary>
        public static string PropOf(Material m)
        {
            if (m == null) return null;
            if (m.HasProperty("_BaseColor")) return "_BaseColor";
            if (m.HasProperty("_Color")) return "_Color";
            return null;
        }

        /// <summary>Собственный цвет материала (белый, если нет).</summary>
        public static Color BaseOf(Material m)
        {
            string p = PropOf(m);
            return p != null ? m.GetColor(p) : Color.white;
        }

        /// <summary>0 — сырое тесто, 0.6 — золотистое, 1 — корочка. Сглажено.</summary>
        public static Color Ramp(Color dough, Color gold, Color crust, float k)
        {
            k = Mathf.Clamp01(k);
            if (k < 0.6f) return Color.Lerp(dough, gold, Smooth(k / 0.6f));
            return Color.Lerp(gold, crust, Smooth((k - 0.6f) / 0.4f));
        }

        public static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Покрасить рендер (null — снять покраску, будет цвет материала).</summary>
        public static void Tint(Renderer r, ref MaterialPropertyBlock mpb, Color? c)
        {
            if (r == null) return;
            if (c == null) { r.SetPropertyBlock(null); return; }
            string p = PropOf(r.sharedMaterial);
            if (p == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(p, c.Value);
            r.SetPropertyBlock(mpb);
        }
    }
}
