// ============================================================================
//  CatCooks/God Ray  —  объёмный солнечный луч (beam card), URP / Unity 6
//
//  Техника: вытянутый квад, ось Y = направление луча (вдоль солнца).
//  Вершинный шейдер поворачивает его вокруг оси к камере (axis billboard) —
//  луч выглядит объёмным с любого ракурса, кроме взгляда точно вдоль оси
//  (там он честно гаснет через angle fade, а не «схлопывается» плоскостью).
//
//  • аддитивный, очень дешёвый (1 квад на луч)
//  • мягкие края + затухание к концу луча
//  • живые «переливы» — шум ползёт вдоль луча
//  • DEPTH FADE: тает при пересечении с горами/зданиями
//  • CAMERA FADE: растворяется при подлёте камеры
//  • глобальные _CatRayIntensity/_CatRayTint — управляются SunRayLayer
//
//  ТРЕБОВАНИЕ: Depth Texture в URP Asset (уже включён для воды/облаков).
// ============================================================================
Shader "CatCooks/God Ray"
{
    Properties
    {
        [Header(Look)]
        _Color ("Ray Color", Color) = (1, 0.93, 0.78, 1)
        _Intensity ("Intensity", Range(0, 3)) = 1
        [Header(Shape)]
        _EdgeSoftness ("Edge Softness", Range(0.05, 1)) = 0.45
        _TipFade ("Tip Fade (конец луча)", Range(0.1, 1)) = 0.65
        _RootFade ("Root Fade (начало луча)", Range(0, 0.5)) = 0.12
        [Header(Animation)]
        _NoiseScale ("Noise Scale", Range(0.5, 10)) = 3
        _NoiseSpeed ("Noise Speed", Range(0, 2)) = 0.25
        _NoiseAmount ("Noise Amount", Range(0, 1)) = 0.5
        [Header(Fades)]
        _DepthFade ("Depth Fade Distance", Range(1, 100)) = 25
        _CameraFade ("Camera Fade Distance", Range(1, 150)) = 40
        _AngleFade ("Angle Fade Power", Range(0.5, 6)) = 2
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        Blend One One            // аддитив: лучи складываются со сценой и друг с другом
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GodRay"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // глобальные — ставит SunRayLayer
            float  _CatRayIntensity;
            float4 _CatRayTint;

            // per-card — ставит SunRayLayer через MaterialPropertyBlock
            float _RaySeed;

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity;
                float  _EdgeSoftness;
                float  _TipFade;
                float  _RootFade;
                float  _NoiseScale;
                float  _NoiseSpeed;
                float  _NoiseAmount;
                float  _DepthFade;
                float  _CameraFade;
                float  _AngleFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  camDist    : TEXCOORD2;
                float  angleFade  : TEXCOORD3;
            };

            float hash(float2 p){ p = frac(p * float2(123.34, 345.45)); p += dot(p, p + 34.345); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p); float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1,0)), u.x),
                            lerp(hash(i + float2(0,1)), hash(i + float2(1,1)), u.x), u.y);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;

                // axis billboard: ось луча = локальный Y объекта
                float3 centerWS = TransformObjectToWorld(float3(0,0,0));
                float3 axisWS = normalize(TransformObjectToWorldDir(float3(0,1,0)));
                float sx = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float sy = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));

                float3 toCam = GetCameraPositionWS() - centerWS;
                float3 right = cross(axisWS, normalize(toCam));
                float rl = length(right);
                // если смотрим точно вдоль оси — right вырожден; берём любой перпендикуляр
                right = rl > 0.001 ? right / rl : normalize(cross(axisWS, float3(1, 0, 0)));

                float3 posWS = centerWS + right * v.positionOS.x * sx + axisWS * v.positionOS.y * sy;

                o.positionCS = TransformWorldToHClip(posWS);
                o.screenPos  = ComputeScreenPos(o.positionCS);
                o.uv = v.uv;
                o.camDist = length(toCam);
                // гасим луч при взгляде вдоль его оси (иначе видно, что это карта)
                o.angleFade = pow(saturate(rl), _AngleFade);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // --- профиль луча ---
                float xd = abs(i.uv.x - 0.5) * 2.0;                       // 0 центр -> 1 край
                float core = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, xd);
                core *= core;

                // затухание вдоль длины: ярко у истока (uv.y = 1), тает к концу
                float len = smoothstep(0.0, _TipFade, i.uv.y)
                          * smoothstep(1.0, 1.0 - max(_RootFade, 0.001), i.uv.y);

                // --- живые переливы: шум ползёт вдоль луча ---
                float2 nuv = float2(i.uv.x * _NoiseScale + _RaySeed,
                                    i.uv.y * _NoiseScale * 0.5 - _Time.y * _NoiseSpeed + _RaySeed * 3.0);
                float n = vnoise(nuv) * 0.65 + vnoise(nuv * 2.7) * 0.35;
                float shimmer = lerp(1.0, n * 1.6, _NoiseAmount);

                float a = core * len * shimmer * i.angleFade;

                // --- depth fade ---
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                a *= saturate((sceneEye - i.screenPos.w) / _DepthFade);

                // --- camera fade ---
                a *= saturate(i.camDist / _CameraFade);

                float3 col = _Color.rgb * _CatRayTint.rgb;
                float g = max(_CatRayIntensity, 0.0);
                // аддитив: цвет * альфа, HDR-яркость > 1 в ядре дружит с Bloom
                return half4(col * a * _Intensity * g, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
