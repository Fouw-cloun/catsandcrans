// =============================================================
//  CatCooksTerrainShadow.hlsl
//  Тени от облаков ПРЯМО В ШЕЙДЕРЕ ЗЕМЛИ.
//
//  ЗАЧЕМ ВТОРОЙ ПУТЬ
//   Полноэкранный проход зависит от цепочки: фича в Renderer Data,
//   буфер глубины, точка врезки, восстановление мировой позиции. Любое
//   звено ломается — и теней нет, причём молча. Мы это уже проходили.
//
//   Здесь тень считает сама земля. Ей не нужны ни глубина, ни фича,
//   ни матрицы: мировая позиция у неё уже есть, она рисует именно себя.
//   Сломаться тут нечему.
//
//   Плюс дешевле: считается только на пикселях земли, а не на всём
//   экране. Небо, здания и техника в расчёте не участвуют вовсе.
//
//  Минус: тень ложится ТОЛЬКО на землю. Здания под облаком не темнеют.
//  Для вида сверху на ландшафт это незаметно, для города — заметно.
//  Поэтому оба пути живут рядом: земле — этот, остальному — композит.
//
//  ПРИВЯЗКА К ПОГОДЕ, А НЕ К ОБЛАКАМ. Пятна не повторяют форму
//  конкретных облаков в небе и не должны: сопоставить их глазом
//  всё равно невозможно, а плата за попытку — общий буфер и жёсткая
//  связка двух систем. Здесь просто маска, которую гонит ветер,
//  а густоту и покрытие задаёт погода.
//
//  ПОДКЛЮЧЕНИЕ К Custom/Terrain/CandyToon — три правки:
//
//   1. В блок HLSLINCLUDE, после Core.hlsl:
//        #include "Assets/.../CatCooksTerrainShadow.hlsl"
//
//   2. В Properties:
//        _CloudShadowResponse ("Cloud shadow response", Range(0,2)) = 1
//      и в CBUFFER_START(UnityPerMaterial):
//        float _CloudShadowResponse;
//
//   3. В Frag, ПЕРЕД строкой с MixFog:
//        col.rgb = CatCooksApplyCloudShadow(col.rgb, IN.positionWS,
//                                           _CloudShadowResponse);
//
//  Порядок важен: до тумана. Тень гасит освещение поверхности,
//  а туман стоит между поверхностью и глазом — если поменять местами,
//  туман окажется затенённым, чего не бывает.
// =============================================================
#ifndef CATCOOKS_TERRAIN_SHADOW_INCLUDED
#define CATCOOKS_TERRAIN_SHADOW_INCLUDED

// Пишет CatCooksCloudShadows. ВНЕ CBUFFER — иначе SRP Batcher
// их не увидит и они всегда будут нулём.
float4 _CatCS_Params;      // x метры, y покрытие, z резкость, w густота
float4 _CatCS_Offset;      // xy слой A, zw слой B
float4 _CatCS_Color;
float  _CatCS_Penumbra;
float  _CatCS_Contrast;
float  _CatCS_HeightFade;
float  _CatCS_MaxDistance;

float CatCS_Hash(float2 p)
{
    p = frac(p * float2(127.1, 311.7));
    p += dot(p, p + 34.345);
    return frac(p.x * p.y * 43758.5453);
}

float CatCS_Noise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = CatCS_Hash(i);
    float b = CatCS_Hash(i + float2(1, 0));
    float c = CatCS_Hash(i + float2(0, 1));
    float d = CatCS_Hash(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Три октавы обычного шума. Не billow: у него среднее около 0.72,
// а не 0.5, и порог, посчитанный от середины, промахивается —
// земля уходит либо целиком в тень, либо целиком на свет.
float CatCS_Fbm(float2 p)
{
    float v = 0, a = 0.5, norm = 0;
    [unroll]
    for (int k = 0; k < 3; k++)
    {
        v += a * CatCS_Noise(p);
        norm += a;
        p = p * 2.03 + 7.1;
        a *= 0.5;
    }
    return v / norm;
}

/// <summary>
/// Доля света, доходящего до точки. 1 — открытое солнце, меньше —
/// под облаком. response: насколько материал ловит тень, 0 выключает.
/// </summary>
float CatCooksCloudShadowFactor(float3 positionWS, float response)
{
    // Невыставленные глобали равны нулю — трактуем как «выключено»,
    // а не считаем по нулевым параметрам.
    float strength = _CatCS_Params.w * response;
    if (strength <= 0.001) return 1.0;

    float patch = _CatCS_Params.x > 1.0 ? _CatCS_Params.x : 480.0;
    float contrast = _CatCS_Contrast > 0.01 ? _CatCS_Contrast : 1.8;
    float penumbra = _CatCS_Penumbra > 0.001 ? _CatCS_Penumbra : 0.35;
    float maxDist = _CatCS_MaxDistance > 1.0 ? _CatCS_MaxDistance : 2000.0;
    float coreEdge = _CatCS_Params.z > 0.001 ? _CatCS_Params.z : 0.07;

    // Маска по МИРОВЫМ XZ: тень приклеена к земле и не плывёт
    // при движении камеры.
    float2 p = positionWS.xz / patch;

    // Складываем, а не перемножаем: произведение сжимает разброс
    // к середине и сдвигает среднее, после чего порог перестаёт
    // что-либо разделять.
    float a = CatCS_Fbm(p + _CatCS_Offset.xy);
    float b = CatCS_Fbm(p * 0.63 + _CatCS_Offset.zw);
    float n = a * 0.6 + b * 0.4;

    n = saturate((n - 0.5) * contrast + 0.5);

    // Порог симметричен относительно 0.5: на покрытии 0.5 ровно
    // половина земли в тени.
    float edge = lerp(0.85, 0.15, _CatCS_Params.y);

    // Резкое ядро плюс широкая полутень: именно этот контраст читается
    // как «накрыло», а не как «освещение слегка просело».
    float core = smoothstep(edge, edge + coreEdge, n);
    float penum = smoothstep(edge - penumbra, edge + penumbra, n);
    float shade = lerp(penum, core, 0.7);

    // Вдали гасим: у горизонта тень превращается в грязь.
    float dist = distance(positionWS, _WorldSpaceCameraPos);
    float far = 1.0 - smoothstep(maxDist * 0.6, maxDist, dist);

    return 1.0 - (1.0 - shade) * strength * far;
}

/// <summary>Готовый множитель на цвет с оттенком тени.</summary>
float3 CatCooksApplyCloudShadow(float3 col, float3 positionWS, float response)
{
    float f = CatCooksCloudShadowFactor(positionWS, response);
    if (f >= 0.999) return col;

    // Цвет тени, а не просто затемнение: в тени поверхность освещена
    // небом, и оно голубое. Серое умножение выглядит грязью.
    float3 tint = dot(_CatCS_Color.rgb, 1.0) > 0.01
                  ? _CatCS_Color.rgb : float3(0.42, 0.48, 0.64);

    return col * lerp(tint, float3(1, 1, 1), f);
}

#endif
