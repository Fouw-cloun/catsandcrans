Shader "CatCooks/Thermo Glass"
{
    Properties
    {
        [Header(Glass)]
        _TintColor ("Tint", Color) = (0.75, 0.85, 0.9, 1)
        _Opacity ("Opacity", Range(0,1)) = 0.18

        [Header(Rim)]
        _RimColor ("Rim", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.0
        _RimStrength ("Rim Strength", Range(0,3)) = 1.1

        [Header(Highlight)]
        _SpecColor2 ("Highlight", Color) = (1, 1, 1, 1)
        _SpecPower ("Highlight Tightness", Range(4, 128)) = 48
        _SpecStrength ("Highlight Strength", Range(0,3)) = 0.9
        _LightDir ("Light Direction", Vector) = (0.4, 1, 0.3, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            // Прозрачное стекло НЕ пишет в буфер глубины: иначе оно закрывало
            // бы столбик внутри себя, и градусник выглядел бы пустым.
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            // Рисуем ОБЕ стороны: у трубки видно и переднюю стенку, и заднюю,
            // и без задней стекло не читается как объёмное.
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float  _Opacity;
                float4 _RimColor;
                float  _RimPower;
                float  _RimStrength;
                float4 _SpecColor2;
                float  _SpecPower;
                float  _SpecStrength;
                float4 _LightDir;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);

                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewWS     = GetWorldSpaceViewDir(posWS);
                return OUT;
            }

            half4 Frag(Varyings IN, bool facing : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(IN.normalWS);

                // У задней стенки нормаль смотрит от нас — разворачиваем,
                // иначе блик и кайма на ней считались бы наизнанку.
                if (!facing) n = -n;

                float3 v = normalize(IN.viewWS);
                float ndv = saturate(dot(n, v));

                // Френель: стекло почти прозрачно в лоб и заметно по краям.
                // Именно это и делает трубку похожей на стекло, а не на плёнку.
                float rim = pow(1.0 - ndv, _RimPower);

                float3 l = normalize(_LightDir.xyz);
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), _SpecPower);

                half3 col = _TintColor.rgb;
                col += _RimColor.rgb * rim * _RimStrength;
                col += _SpecColor2.rgb * spec * _SpecStrength;

                // Прозрачность тоже растёт к краям: в лоб смотрим почти
                // насквозь, вскользь — стекло уплотняется.
                half a = saturate(_Opacity + rim * _RimStrength * 0.5
                                  + spec * _SpecStrength * 0.6);

                return half4(col, a);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
