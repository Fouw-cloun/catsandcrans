// =============================================================
//  CatCooksWet.hlsl
//  Подключается в твои Toon-шейдеры, чтобы они реагировали на дождь.
//
//  КАК ПОДКЛЮЧИТЬ (три строки на шейдер):
//
//   1. Рядом с остальными include:
//        #include "Assets/.../CatCooksWet.hlsl"
//
//   2. В блоке Properties материала:
//        _WetAmount ("Wet Response", Range(0,1)) = 1
//      и в CBUFFER: float _WetAmount;
//      Это множитель на материал: асфальт мокнет сильно, ткань слабо,
//      а на еде и интерфейсе поставишь ноль.
//
//   3. Перед возвратом цвета:
//        col.rgb = CatCooksApplyWet(col.rgb, normalWS, positionWS, _WetAmount);
//
//  ЧТО ДЕЛАЕТ МОКРАЯ ПОВЕРХНОСТЬ (и почему именно так)
//
//   • ТЕМНЕЕТ. Плёнка воды ловит свет внутрь и отдаёт меньше обратно.
//     Это главный признак, он один читается сразу.
//   • НАСЫЩАЕТСЯ. Заодно цвет становится глубже — сухая пыль
//     обесцвечивает, вода её смывает.
//   • БЛЕСТИТ СВЕРХУ. Горизонтальные поверхности собирают воду,
//     вертикальные — почти нет. Поэтому блик привязан к нормали:
//     без этого мокрыми выглядят и стены, а так не бывает.
//   • ЛУЖИ В НИЗИНАХ. Плоские участки заливает раньше склонов.
//
//  Всё берётся из глобального _CatWetness — его пишет CatCooksWeatherFX.
//  Шейдер ничего не знает про погоду и ни на что не ссылается.
// =============================================================
#ifndef CATCOOKS_WET_INCLUDED
#define CATCOOKS_WET_INCLUDED

float _CatWetness;      // 0…1, пишет CatCooksWeatherFX
float _CatOpenSky;      // 1 под открытым небом, 0 под крышей

float CatCooksHash(float2 p)
{
    p = frac(p * float2(127.1, 311.7));
    p += dot(p, p + 34.345);
    return frac(p.x * p.y * 43758.5453);
}

float CatCooksNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = CatCooksHash(i);
    float b = CatCooksHash(i + float2(1, 0));
    float c = CatCooksHash(i + float2(0, 1));
    float d = CatCooksHash(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

/// <summary>
/// wetResponse — насколько материал вообще впитывает: камень и металл 1,
/// ткань 0.4, еда и интерфейс 0.
/// </summary>
float3 CatCooksApplyWet(float3 col, float3 normalWS, float3 positionWS,
                        float wetResponse)
{
    float wet = saturate(_CatWetness) * saturate(wetResponse);
    if (wet <= 0.001) return col;

    // Вверх смотрящие поверхности мокнут, вертикальные почти нет.
    // Без этой проверки «мокрыми» становятся и стены, а так не бывает.
    float up = saturate(dot(normalWS, float3(0, 1, 0)));
    float facing = smoothstep(0.15, 0.75, up);

    // Темнеет и насыщается: плёнка воды ловит свет внутрь.
    float lum = dot(col, float3(0.299, 0.587, 0.114));
    float3 wetCol = lerp(lum.xxx, col, 1.35);          // насыщеннее
    wetCol *= lerp(1.0, 0.62, wet * lerp(0.45, 1.0, facing));  // темнее

    col = lerp(col, wetCol, wet);

    // ЛУЖИ. Заливают плоские низины и приходят позже общей мокроты:
    // сначала земля просто темнеет, и только потом стоит вода.
    float puddleMask = CatCooksNoise(positionWS.xz * 0.15);
    float flat = smoothstep(0.86, 0.98, up);
    float puddle = smoothstep(0.55, 0.25, puddleMask)
                 * flat * smoothstep(0.35, 0.85, wet);

    if (puddle > 0.001)
    {
        // Вода в луже почти зеркальна: сильно темнее и с холодным
        // отражением неба.
        float3 water = col * 0.45 + float3(0.10, 0.13, 0.18);
        col = lerp(col, water, puddle * 0.85);
    }

    return col;
}

/// <summary>Упрощённая версия без луж — для мелких предметов.</summary>
float3 CatCooksApplyWetSimple(float3 col, float3 normalWS, float wetResponse)
{
    float wet = saturate(_CatWetness) * saturate(wetResponse);
    if (wet <= 0.001) return col;

    float up = saturate(dot(normalWS, float3(0, 1, 0)));
    float facing = smoothstep(0.15, 0.75, up);

    float lum = dot(col, float3(0.299, 0.587, 0.114));
    float3 wetCol = lerp(lum.xxx, col, 1.3);
    wetCol *= lerp(1.0, 0.66, wet * lerp(0.5, 1.0, facing));
    return lerp(col, wetCol, wet);
}

#endif
