// CatCooks — МУЛЬТЯШНЫЙ ШЕЙДЕР С ПОКАЧИВАНИЕМ для дешёвой «жизни»: голуби (крылья машут) и бельё на верёвках (колышется).
// Цвет — в вершинах (rgb), a — вес движения (0 — стоит, 1 — двигается полностью).
// Сдвиг = ось (_SwayAxis, в осях объекта) × sin(время × скорость + фаза объекта + рябь по X) × размах × вес × ветер.
// У каждого объекта своя фаза (по его позиции) — стайка не машет в такт, бельё не качается синхронно.
// Склеенный меш (всё бельё одним куском): uv1 = (ось движения xyz, фаза), uv2.x — координата вдоль верёвки (для ряби);
// если ось в uv1 нулевая — берётся _SwayAxis и X объекта (голуби, отдельные объекты).
// Свет: тёплая ступенька + подсветка из сцены, двусторонний (бельё тонкое). Без теней. URP, SRP Batcher, инстансинг.
Shader "CatCooks/City/Sway Toon"
{
    Properties
    {
        _BaseColor ("Цвет", Color) = (1, 1, 1, 1)
        _ShadowColor ("Тень — цвет", Color) = (0.62, 0.52, 0.58, 1)
        _Ambient ("Подсветка", Range(0, 2)) = 0.7
        _SwayAxis ("Ось движения (в осях объекта)", Vector) = (0, 0, 1, 0)
        _SwaySpeed ("Скорость", Float) = 1.5
        _SwayAmp ("Размах (ед. объекта)", Float) = 0.1
        _SwayRipple ("Рябь вдоль X (волна)", Float) = 0.5
        _WindK ("Ветер погоды усиливает", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _ShadowColor;
            float4 _SwayAxis;
            half _Ambient;
            float _SwaySpeed, _SwayAmp, _SwayRipple, _WindK;
        CBUFFER_END
        float _CatWindStrength;

        float3 Sway(float3 p, half w, float4 ax, float along)
        {
            bool own = dot(ax.xyz, ax.xyz) > 0.25;
            float3 axis = own ? ax.xyz : _SwayAxis.xyz;
            float3 o = UNITY_MATRIX_M._m03_m13_m23;
            float ph = frac(sin(dot(o.xz, float2(12.9898, 78.233))) * 43758.5453) * 6.2832 + (own ? ax.w : 0.0);
            float wind = 1.0 + saturate(_CatWindStrength) * _WindK;
            float s = sin(_Time.y * _SwaySpeed * (0.85 + 0.3 * frac(ph * 0.1591)) + ph + (own ? along : p.x) * _SwayRipple);
            return p + axis * (s * _SwayAmp * w * wind);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardSway"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float4 ax : TEXCOORD1;
                float2 al : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half4 color : TEXCOORD1;
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
                float3 p = Sway(v.positionOS.xyz, v.color.a, v.ax, v.al.x);
                o.positionCS = TransformObjectToHClip(p);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 nw = i.normalWS;
                nw = dot(nw, nw) > 1e-10 ? normalize(nw) : float3(0, 1, 0);
                half3 n = (half3)nw * (front ? 1.0h : -1.0h);
                Light ml = GetMainLight();
                half ramp = smoothstep(0.25h, 0.55h, saturate((dot(n, ml.direction) + 0.35h) / 1.35h));
                half shLum = max(dot(SampleSH(half3(0, 1, 0)), half3(0.333h, 0.333h, 0.333h)), 0.0h);
                half3 light = lerp(_ShadowColor.rgb * saturate(shLum * 1.6h), ml.color, ramp) + SampleSH(n) * _Ambient * 0.5h;
                half3 col = i.color.rgb * _BaseColor.rgb * light;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float4 ax : TEXCOORD1; float2 al : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(Sway(v.positionOS.xyz, v.color.a, v.ax, v.al.x));
                return o;
            }
            half frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float4 ax : TEXCOORD1; float2 al : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 n : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(Sway(v.positionOS.xyz, v.color.a, v.ax, v.al.x));
                o.n = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
    Fallback Off
}
