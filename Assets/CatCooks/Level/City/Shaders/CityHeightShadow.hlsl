// CatCooks — ТЕНИ И ЗАТЕНЕНИЕ ГОРОДА ПО КАРТЕ ВЫСОТ (CityHeightShadows).
// Карта высот города (вид сверху, запекается один раз кнопкой) → в шейдере:
//   • CC_HeightShadow — тень от соседних домов: 8 шагов к солнцу по карте; солнце ходит — тени ходят сами,
//     работают на любом расстоянии, без карты теней (дальше дистанции настоящих теней — только они);
//   • CC_HeightAO — затенение: переулки, стены напротив высоких домов, асфальт у подножия (контактная тень);
//   • CC_HeightFar — вес: вблизи — настоящие тени URP, дальше — карта высот.
// Подключать после Core.hlsl. Свои переменные — только здесь (в CityCommon их нет).
#ifndef CC_HEIGHT_SHADOW_INCLUDED
#define CC_HEIGHT_SHADOW_INCLUDED

TEXTURE2D(_CC_HMap); SAMPLER(sampler_CC_HMap);
float4 _CC_HMapRect;   // xy — минимум XZ карты, zw — 1/размер
float4 _CC_HMapP;      // x сила теней, y сила затенения (AO), z начало перехода к карте (ед. от камеры), w конец
float4 _CC_HMapP2;     // x метр (ед.), y смещение (ед.), z радиус AO (ед.), w включено (1)

float CC_HMapAt(float2 xz)
{
    float2 uv = (xz - _CC_HMapRect.xy) * _CC_HMapRect.zw;
    if (any(uv < 0.0) || any(uv > 1.0)) return -1e5;
    return SAMPLE_TEXTURE2D_LOD(_CC_HMap, sampler_CC_HMap, uv, 0).r;
}

// 1 — свет, 0 — в тени соседних домов
half CC_HeightShadow(float3 p, half3 n, float3 L)
{
    if (_CC_HMapP2.w < 0.5 || L.y < 0.03) return 1.0h;
    float m = max(_CC_HMapP2.x, 0.01);
    float2 dir = L.xz;
    float dl = length(dir);
    if (dl < 1e-3) return 1.0h;                                   // солнце в зените — теней почти нет
    dir /= dl;
    float slope = L.y / dl;
    float3 o = p + float3(n.x, 0.0, n.z) * (0.4 * m);             // выйти из «следа» своего дома на карте
    float s = 0.8 * m;
    float occ = 0.0;
    [unroll] for (int k = 0; k < 8; k++)
    {
        float h = CC_HMapAt(o.xz + dir * s);
        float ry = o.y + s * slope;
        occ = max(occ, saturate((h - ry - _CC_HMapP2.y) / (0.6 * m)));
        s *= 1.75;
    }
    return 1.0h - (half)occ;
}

// 1 — открыто, меньше — затенено (переулок, стена напротив высокого дома, низ у соседей)
half CC_HeightAO(float3 p, half3 n)
{
    if (_CC_HMapP2.w < 0.5) return 1.0h;
    float m = max(_CC_HMapP2.x, 0.01);
    float R = max(_CC_HMapP2.z, 0.5 * m);
    float2 nxz = float2(n.x, n.z);
    float nl = length(nxz);
    float2 nd = nl > 1e-3 ? nxz / nl : float2(0, 0);
    float3 o = p + float3(n.x, 0.0, n.z) * (0.3 * m);
    float a = 0.0, ws = 0.0;
    [unroll] for (int k = 0; k < 6; k++)
    {
        float an = k * 1.0472 + 0.3;
        float2 d = float2(cos(an), sin(an));
        float rr = (k % 2 == 1) ? R : R * 0.45;
        float w = lerp(1.0, saturate(dot(d, nd) * 0.9 + 0.25), saturate(nl * 1.5));   // стена смотрит только перед собой
        float h = CC_HMapAt(o.xz + d * rr);
        a += saturate((h - o.y) / max(rr * 1.4, 1e-3)) * w;
        ws += w;
    }
    a /= max(ws, 1e-3);
    return (half)saturate(1.0 - a * (1.0 - saturate(n.y) * 0.5));
}

// вес карты высот: 0 — вблизи (там настоящие тени), 1 — дальше
half CC_HeightFar(float3 p)
{
    float d = distance(p, _WorldSpaceCameraPos.xyz);
    return (half)smoothstep(_CC_HMapP.z, max(_CC_HMapP.w, _CC_HMapP.z + 1.0), d);
}

#endif
