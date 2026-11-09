// Раскалённый металл (ковш на горелке). Рисуется поверх корпуса: цвет — из цветов вершин
// (их считает SurfaceHeatGlow), здесь только пересвет для Bloom, мерцание и ободок по краю.
Shader "CatCooks/Heat Glow"
{
    Properties
    {
        _Boost ("Яркость (HDR)", Float) = 2.4
        _Flicker ("Мерцание", Range(0, 0.5)) = 0.12
        _RimPower ("Ободок", Range(0.5, 8)) = 2.5
        _RimBoost ("Сила ободка", Range(0, 2)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "HeatGlow"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Boost;
                float _Flicker;
                float _RimPower;
                float _RimBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = pw;
                o.positionCS = TransformWorldToHClip(pw);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = i.color.a;
                clip(a - 0.01);
                float3 n = normalize(i.normalWS);
                float3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0 - saturate(abs(dot(n, vdir))), _RimPower) * _RimBoost;
                // Жар «дышит»: медленная волна по металлу.
                half fl = 1.0 + _Flicker * sin(_Time.y * 6.0 + dot(i.positionWS, float3(3.1, 2.3, 2.7)))
                              + _Flicker * 0.5 * sin(_Time.y * 13.0 + i.positionWS.y * 7.0);
                half3 c = i.color.rgb * _Boost * fl * (1.0 + rim);
                return half4(c, saturate(a * (1.0 + rim * 0.5)));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
