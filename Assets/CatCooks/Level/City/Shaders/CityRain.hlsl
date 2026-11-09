// CatCooks — дождь города: общие глобали и функции (струи, завеса, всплески).
// Всё пишет CityRain (C#) каждый кадр; размеры уже в единицах сцены (метры × _CC_Meter).
#ifndef CATCOOKS_CITY_RAIN_INCLUDED
#define CATCOOKS_CITY_RAIN_INCLUDED
#include "CityCommon.hlsl"     // _CC_GroundY, _CC_Night

float4 _CC_RainP0;      // x сила 0..1 (доля видимых капель), y скорость падения, z длина капли, w толщина
float4 _CC_RainP1;      // x прозрачность струй, y блеск на солнце (грибной), z ширина коробки капель, w высота коробки
float4 _CC_RainWind;    // xyz снос ветром (ед/с), w порывистость
float4 _CC_RainTint;    // rgb оттенок капель, a яркость
float4 _CC_RainSplash;  // x доля всплесков, y жизнь всплеска (с), z радиус кольца, w высота короны (0 — без короны)
float4 _CC_RainSplash2; // x брызги-капельки 0..1, y размер клетки сетки, z клеток по стороне, w клеток × на клетку
float4 _CC_RainCurtain; // x сила завесы, y радиус, z высота, w длина капли (масштаб полос)
float4 _CC_RainFall;    // xyz накопленное падение и снос ветром (ед.) — общее для капель и завесы
float4 _CC_RainOccRect; // карта укрытий зоны: xy минимум XZ, zw 1/размер
float  _CC_RainOccOn;   // 1 — карта есть
float  _CC_RainMeter;   // единиц сцены в метре
float  _CC_Rain;        // сила дождя сейчас 0..1
TEXTURE2D(_CC_RainOcc); SAMPLER(sampler_CC_RainOcc);

// Укрытия-«зонты» (CityRainBlocker): до 8 штук, xyz — центр обода купола (x, высота, z), w — радиус. Купол выше обода на 0.28 r.
float4 _CC_RainBlock[8];
float  _CC_RainBlockN;

// ── хэши (без текстур) ──
float CR_Hash11(float n) { return frac(sin(n * 12.9898) * 43758.5453); }
float CR_Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
float3 CR_Hash31(float n)
{
    float3 p = frac(n * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yzx + 33.33);
    return frac((p.xxy + p.yzz) * p.zyx);
}
float3 CR_Hash32(float2 q)
{
    float3 p = frac(float3(q.xyx) * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yzz) * p.zyx);
}

float CR_Noise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(CR_Hash21(i), CR_Hash21(i + float2(1, 0)), u.x), lerp(CR_Hash21(i + float2(0, 1)), CR_Hash21(i + float2(1, 1)), u.x), u.y);
}

// ── карта укрытий: высота первой поверхности сверху (внутри зоны). Вне зоны — открытое небо. ──
bool CR_OccHeight(float3 p, out float h)
{
    h = -1e6;
    if (_CC_RainOccOn < 0.5) return false;
    float2 uv = (p.xz - _CC_RainOccRect.xy) * _CC_RainOccRect.zw;
    if (any(uv < 0.0) || any(uv > 1.0)) return false;
    h = SAMPLE_TEXTURE2D_LOD(_CC_RainOcc, sampler_CC_RainOcc, uv, 0).r;
    return h > -1e5;
}

// Высота купола зонтов над точкой xz (самый высокий из накрывающих). false — не под зонтом.
bool CR_BlockHeight(float2 xz, out float h)
{
    h = -1e6;
    bool any = false;
    int n = (int)_CC_RainBlockN;
    [loop] for (int i = 0; i < n && i < 8; i++)
    {
        float4 b = _CC_RainBlock[i];
        float d2 = dot(xz - b.xz, xz - b.xz) / max(b.w * b.w, 1e-4);
        if (d2 < 1.0) { h = max(h, b.y + (1.0 - d2) * b.w * 0.28); any = true; }
    }
    return any;
}

// Капля в точке p под навесом / крышей (внутри зоны) или под зонтом?
bool CR_Covered(float3 p)
{
    float hb;
    if (CR_BlockHeight(p.xz, hb) && p.y < hb) return true;
    float h;
    if (!CR_OccHeight(p, h)) return false;
    return p.y < h - 0.05 * _CC_RainMeter;
}

// Чёткий мультяшный край постоянной ширины в пикселях
float CR_AAStep(float edge, float x)
{
    float w = max(fwidth(x), 1e-5);
    return saturate((x - edge) / w + 0.5);
}

// Освещение капли: небо сцены (SH) + солнце/луна; ночью — чуть тёплого света города
float3 CR_Light(float3 viewDir)
{
    float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
    Light ml = GetMainLight();
    float3 c = amb * 1.1 + ml.color * 0.22;
    c += _CC_Flash.rgb * _CC_Flash.a * 0.9;            // молния: капли вспыхивают
    return c * _CC_RainTint.rgb * _CC_RainTint.a;
}

#endif
