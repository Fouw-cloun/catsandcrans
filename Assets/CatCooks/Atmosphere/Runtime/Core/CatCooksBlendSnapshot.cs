using System.Reflection;
using UnityEngine;

// ============================================================================
//  CatCooksBlendSnapshot — «снимок» недоехавшего перехода.
//
//  Раньше, если новая погода/стиль приходили посреди перехода, пульт
//  начинал новый переход от одного из пресетов-концов — картинка дёргалась
//  назад или вперёд. Теперь пульт снимает ТЕКУЩУЮ смесь в рабочую копию
//  и плавно ведёт от неё. Числа и цвета смешиваются, остальное (строки,
//  ссылки, флаги) берётся с ближнего конца.
// ============================================================================
public static class CatCooksBlendSnapshot
{
    /// <summary>Смесь стилей a→b на t в dst (создаётся, если null). a может быть самим dst.</summary>
    public static CatCooksSkyStyle Style(CatCooksSkyStyle a, CatCooksSkyStyle b, float t, CatCooksSkyStyle dst)
    {
        if (dst == null)
        {
            dst = ScriptableObject.CreateInstance<CatCooksSkyStyle>();
            dst.hideFlags = HideFlags.HideAndDontSave;
            dst.name = "~StyleSnapshot";
        }
        CatCooksPalette pa = a.palette, pb = b.palette;
        CatCooksPalette pal = dst.palette;          // своя копия палитры (ассет не трогаем)
        LerpFields(a, b, t, dst);
        if (pal == null || pal.name != "~PaletteSnapshot")
        {
            pal = ScriptableObject.CreateInstance<CatCooksPalette>();
            pal.hideFlags = HideFlags.HideAndDontSave;
            pal.name = "~PaletteSnapshot";
        }
        if (pa != null && pb != null) CatCooksPalette.Blend(pa, pb, t, pal);
        else if (pb != null) CatCooksPalette.Blend(pb, pb, 1f, pal);
        dst.palette = pal;
        return dst;
    }

    /// <summary>Смесь погод a→b на t в dst (создаётся, если null). a может быть самим dst.</summary>
    public static CatCooksWeatherPreset Weather(CatCooksWeatherPreset a, CatCooksWeatherPreset b, float t, CatCooksWeatherPreset dst)
    {
        if (dst == null)
        {
            dst = ScriptableObject.CreateInstance<CatCooksWeatherPreset>();
            dst.hideFlags = HideFlags.HideAndDontSave;
            dst.name = "~WeatherSnapshot";
        }
        LerpFields(a, b, t, dst);                 // всё, включая то, чего нет в Blend пресета
        CatCooksWeatherPreset.Blend(a, b, t, dst); // и родные правила смешивания (туман, флаги)
        return dst;
    }

    /// <summary>Все публичные поля: float/Color/Vector — плавно, остальное — с ближнего конца.</summary>
    public static void LerpFields(object a, object b, float t, object dst)
    {
        FieldInfo[] fs = dst.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < fs.Length; i++)
        {
            FieldInfo f = fs[i];
            object va = f.GetValue(a), vb = f.GetValue(b);
            System.Type ft = f.FieldType;
            if (ft == typeof(float)) f.SetValue(dst, Mathf.Lerp((float)va, (float)vb, t));
            else if (ft == typeof(Color)) f.SetValue(dst, Color.Lerp((Color)va, (Color)vb, t));
            else if (ft == typeof(Vector2)) f.SetValue(dst, Vector2.Lerp((Vector2)va, (Vector2)vb, t));
            else if (ft == typeof(Vector3)) f.SetValue(dst, Vector3.Lerp((Vector3)va, (Vector3)vb, t));
            else if (ft == typeof(Vector4)) f.SetValue(dst, Vector4.Lerp((Vector4)va, (Vector4)vb, t));
            else f.SetValue(dst, t < 0.5f ? va : vb);
        }
    }
}
