// CatCooks — ПРОЗРАЧНОЕ МУЛЬТЯШНОЕ СТЕКЛО для домов, внутри которых живут коты (дом из модулей).
// Сквозь стекло видно комнату. Лёгкий голубой оттенок, по краям (под углом) стекло плотнее и отражает небо,
// по стеклу идут две косые мультяшные полосы-блика (по миру — у соседних окон совпадают). Чуть темнеет в
// плохую погоду через подсветку сцены. Без теней, ZWrite выключен, один проход. URP, SRP Batcher.
Shader "CatCooks/City/Glass Clear"
{
    Properties
    {
        _Tint ("Оттенок (a — плотность в лоб)", Color) = (0.72, 0.9, 1, 0.12)
        _EdgeAlpha ("Плотность по краям (под углом)", Range(0, 1)) = 0.55
        _EdgePower ("Резкость края", Range(1, 8)) = 3
        _SkyColor ("Отражение неба", Color) = (0.8, 0.93, 1, 1)
        _Streak ("Блики-полосы", Range(0, 1)) = 0.45
        _StreakScale ("Частота бликов", Float) = 0.12
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Отсечение", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardGlass"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint, _SkyColor;
                half _EdgeAlpha, _EdgePower, _Streak;
                float _StreakScale, _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float fog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 nw = i.normalWS;
                nw = dot(nw, nw) > 1e-10 ? normalize(nw) : float3(0, 0, 1);
                float3 vd = normalize(GetWorldSpaceViewDir(i.positionWS));
                half fres = pow(1.0h - saturate(abs(dot((half3)nw, (half3)vd))), _EdgePower);

                // косые полосы-блики (широкая + узкая), по миру
                float s = frac((i.positionWS.x + i.positionWS.z) * _StreakScale + i.positionWS.y * _StreakScale * 0.8);
                half streak = (step(0.08, s) * step(s, 0.2) + step(0.26, s) * step(s, 0.3)) * _Streak;

                Light ml = GetMainLight();
                half3 amb = SampleSH(half3(0, 1, 0));
                half lum = saturate(dot(amb + ml.color * 0.5h, half3(0.333h, 0.333h, 0.333h)));
                half3 sky = _SkyColor.rgb * lerp(0.55h, 1.0h, lum);

                half3 col = lerp(_Tint.rgb * lerp(0.6h, 1.0h, lum), sky, fres) + streak * lerp(0.4h, 1.0h, lum);
                half a = saturate(_Tint.a + fres * _EdgeAlpha + streak * 0.35h);
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
