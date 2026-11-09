#ifndef CATCOOKS_FIRE_BODY_PASS_INCLUDED
#define CATCOOKS_FIRE_BODY_PASS_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "CatCooksFireNoise.hlsl"
CBUFFER_START(UnityPerMaterial)
    float4 _Outer, _Middle, _Inner, _Core, _Wind;
    float _Emission, _Sway, _Erosion, _Softness, _Intensity, _FlowTime;
    float4 _Obstacle;       // xyz — центр дна кастрюли (object space), w — радиус
    float4 _ObstacleShape;  // x — высота, y — разлёт, z — облизывание боков, w — 1 = есть
CBUFFER_END
struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    float4 data : TEXCOORD1; // phase, radius, height, kind
    float3 shape : TEXCOORD2;
    float4 color : COLOR;
};
struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float4 data : TEXCOORD3;
    float3 shape : TEXCOORD4;
    float4 color : COLOR;
};
Varyings Vert(Attributes v)
{
    Varyings o;
    float3 p = v.positionOS.xyz;
    if (v.data.w < 0.5)
    {
        float t = saturate(v.uv.y), weight = t * t;
        float phase = v.data.x, clock = _FlowTime;
        float2 wave = float2(sin(clock * 3.0 + phase + t * 5.0),
            cos(clock * 2.3 + phase * 1.7 + t * 4.0));
        // Per-axis maximum: .385 * height + .45 * radius.
        // Bottom stays anchored. Dimensions never use camera distance.
        float amplitude = min(v.data.y * .45 + v.data.z * .065, v.data.y * .9);
        p.xz += weight * (_Wind.xz * v.data.z + wave * amplitude * saturate(_Sway));
        p.y -= weight * (.5 + .5 * sin(clock * 3.7 + phase)) * v.data.z * .065 * saturate(_Sway);

        // ── кастрюля над огнём ──
        // Язык, дотянувшийся до дна, не проходит сквозь: верх языка уходит в
        // сторону от центра — к краю дна и немного за него (разлёт), и дальше
        // вверх вдоль стенки (облизывание), а не внутрь кастрюли.
        if (_ObstacleShape.w > 0.5)
        {
            float r = _Obstacle.w;
            float bottom = _Obstacle.y;
            float2 d = p.xz - _Obstacle.xz;
            float dist = length(d);
            float2 dir = dist > 1e-4 ? d / dist : float2(cos(phase), sin(phase));
            float over = p.y - bottom;                       // насколько вершина выше дна
            if (over > -r * .15 && dist < r * (1.0 + _ObstacleShape.y))
            {
                float lickTop = _ObstacleShape.x * saturate(_ObstacleShape.z);
                // Внутри радиуса — вытолкнуть к краю с разлётом; чем выше, тем ближе к стенке.
                float targetR = r * (1.0 + _ObstacleShape.y * saturate(1.0 - over / max(lickTop, 1e-3)));
                float pushK = smoothstep(-r * .15, r * .1, over);
                float nd = lerp(dist, max(dist, targetR), pushK);
                p.xz = _Obstacle.xz + dir * nd;
                // Выше «облизывания» языку места нет — прижать к высоте lickTop.
                p.y = min(p.y, bottom + lickTop + (dist - r) * .5);
            }
        }
    }
    o.positionWS = TransformObjectToWorld(p);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.uv = v.uv; o.data = v.data; o.shape = v.shape; o.color = v.color;
    return o;
}
void ApplyFireClip(Varyings i, out float noise, out float isCoal)
{
    float t = saturate(i.uv.y), phase = i.data.x;
    float3 np = i.shape * float3(2.3, 3.6, 2.3) + phase;
    np.y -= _FlowTime * 1.45;
    float n = CCFNoise(np);
    float secondary = CCFNoise(np * 1.7 + float3(4, -_FlowTime * .35, 1));
    noise = n * .75 + secondary * .25;
    isCoal = step(1.5, i.data.w);
    float isFragment = step(.5, i.data.w) * (1.0 - isCoal);
    float presence = lerp(saturate(_Intensity), saturate(i.color.a), isFragment);
    // At intensity 1 the lower 40% cannot dissolve. Erosion lives at the tips.
    float field = noise + .20 - smoothstep(.40, .95, t) * saturate(_Erosion) * 1.05;
    field -= (1.0 - presence) * 1.45;
    clip(field - .005);
}

half4 Frag(Varyings i) : SV_Target
{
    float noise, isCoal;
    ApplyFireClip(i, noise, isCoal);
    float t = saturate(i.uv.y);
    float3 normal = normalize(i.normalWS);
    float facing = saturate(abs(dot(normal, GetWorldSpaceNormalizeViewDir(i.positionWS))));
    float warmth = facing * .45 + noise * .32 + t * .30;
    float edge = max(fwidth(warmth), lerp(.012, .065, saturate(_Softness)));
    float3 rgb = lerp(_Outer.rgb, _Middle.rgb, smoothstep(.40 - edge, .40 + edge, warmth));
    rgb = lerp(rgb, _Inner.rgb, smoothstep(.68 - edge, .68 + edge, warmth));
    rgb = lerp(rgb, _Core.rgb, smoothstep(.87 - edge, .87 + edge, warmth) * .65);
    // Coals are warm accents, not a white floor under the entire fire.
    float3 coal = lerp(_Outer.rgb * .22, _Middle.rgb * .8, noise * noise);
    rgb = lerp(rgb, coal, isCoal);
    return half4(clamp(rgb, 0, 2) * clamp(_Emission, .3, 2), 1);
}

half4 DepthFrag(Varyings i) : SV_Target
{
    float noise, isCoal;
    ApplyFireClip(i, noise, isCoal);
    return 0;
}
half4 NormalsFrag(Varyings i) : SV_Target
{
    float noise, isCoal;
    ApplyFireClip(i, noise, isCoal);
    float3 normal = normalize(i.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 oct = saturate(PackNormalOctQuadEncode(normal) * .5 + .5);
        return half4(PackFloat2To888(oct), 0);
    #else
        return half4(normal, 0);
    #endif
}

#endif
