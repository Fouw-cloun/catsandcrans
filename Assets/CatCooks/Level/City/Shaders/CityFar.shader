// CatCooks — дальние пояса города (3 — кольцо модулей, 4 — сплошная стена высоток). Максимально дёшево:
//   без текстур — цвет в вершинах; окна процедурные по UV в метрах (UV0 = вдоль фасада, высота) и плавно
//   сливаются в средний тон вдали (без ряби); крыши и земля — по типу (UV1.x); UV1.y — случайное здания.
//   Свет как у домов города: тёплая ступенька от солнца, подсветка из сцены (SH) — день/ночь Mood Director работают;
//   ночью (_CC_Night) окна горят. Теней не отбрасывает и не принимает (фон), туман — полноэкранный из Atmosphere.
// URP, SRP Batcher. Один проход + глубина + нормали.
Shader "CatCooks/City/Far"
{
    Properties
    {
        _Tint ("Оттенок", Color) = (1, 1, 1, 1)
        _Saturation ("Насыщенность", Range(0, 2)) = 1
        _Contrast ("Контраст (меньше — мягче)", Range(0, 1)) = 0.85
        [HideInInspector] _FarVer ("ver", Float) = 3
        _ShadowColor ("Тень — цвет", Color) = (0.58, 0.50, 0.58, 1)
        _LightTint ("Свет — оттенок", Color) = (1, 0.95, 0.88, 1)
        _Ambient ("Подсветка", Range(0, 2)) = 0.75
        _Wrap ("Мягкость света", Range(0, 1)) = 0.35

        [Header(Windows)]
        _WinSize ("Окно: шаг по ширине и этажу, м", Vector) = (5, 4.5, 0, 0)
        _WinFill ("Размер окна в шаге (X, Y)", Vector) = (0.42, 0.5, 0, 0)
        _WinColor ("Окна днём", Color) = (0.42, 0.52, 0.56, 1)
        _WinStrength ("Окна днём — заметность", Range(0, 1)) = 0.55
        [HDR] _WinNight ("Окна ночью", Color) = (3.5, 2.1, 1.0, 1)
        _NightLitBoost ("Ночью горит больше/меньше", Range(0, 2)) = 1

        [Header(Roof and ground)]
        _RoofColor ("Крыши", Color) = (0.45, 0.37, 0.34, 1)
        _RoofMix ("Крыша от цвета дома", Range(0, 1)) = 0.35
        _GroundColor ("Земля / двор", Color) = (0.86, 0.78, 0.64, 1)
        _RoadColor ("Дорога", Color) = (0.38, 0.35, 0.40, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint, _ShadowColor, _LightTint;
            half _Saturation, _Contrast, _FarVer, _Ambient, _Wrap, _RoofMix;
            float4 _WinSize, _WinFill;
            half4 _WinColor, _WinNight;
            half _WinStrength, _NightLitBoost;
            half4 _RoofColor, _GroundColor, _RoadColor;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardFar"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CityCommon.hlsl"
            #include "CityHeightShadow.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;          // фасад: (вдоль, высота) в метрах
                float2 info : TEXCOORD1;        // x — тип: 0 стена, 1 крыша, 2 земля, 3 дорога, 4 стена без окон; y — случайное здания
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                nointerpolation float2 info : TEXCOORD2;
                half3 color : TEXCOORD3;
                half fog : TEXCOORD4;
                float3 positionWS : TEXCOORD5;
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
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.info = v.info;
                o.color = v.color.rgb;
                o.fog = ComputeFogFactor(p.positionCS.z);
                o.positionWS = p.positionWS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS);
                half night = saturate(_CC_Night);
                int kind = (int)(i.info.x + 0.5);
                half3 roof = lerp(_RoofColor.rgb, i.color * 0.82h, _RoofMix) * (0.92h + 0.16h * (half)i.info.y);
                half3 base = kind == 1 ? roof
                           : kind == 2 ? _GroundColor.rgb
                           : kind == 3 ? _RoadColor.rgb
                           : i.color;
                half l = dot(base, half3(0.299h, 0.587h, 0.114h));
                base = lerp(l.xxx, base, _Saturation) * _Tint.rgb;
                // меньше контраста: яркость чуть тянем к среднему, оттенок не трогаем (цвета остаются разными)
                half lb = max(dot(base, half3(0.299h, 0.587h, 0.114h)), 0.02h);
                base *= lerp(sqrt(0.3h / lb), 1.0h, _Contrast);

                // окна (только стены): сетка в метрах; вдали — средний тон, без ряби
                half winMask = 0, winLit = 0;
                if (kind == 0)
                {
                    // у каждого дома свой шаг окон (×1…1.5) — фасады не одинаковые
                    float2 g = i.uv / max(_WinSize.xy * (1.0 + 0.5 * frac(i.info.y * 7.31)), 0.1);
                    float2 q = frac(g);
                    float2 aa = max(fwidth(g), 1e-4);
                    float2 lo = 0.5 - _WinFill.xy * 0.5, hi = 0.5 + _WinFill.xy * 0.5;
                    float2 on = smoothstep(lo - aa, lo + aa, q) * (1.0 - smoothstep(hi - aa, hi + aa, q));
                    half pattern = on.x * on.y;
                    half avg = _WinFill.x * _WinFill.y;
                    half far = saturate((max(aa.x, aa.y) - 0.25) * 2.5);
                    winMask = lerp(pattern, avg, far);
                    float2 cell = floor(g);
                    half lit = CC_WindowLit(cell + i.info.y * 371.0, i.info.y * 97.0);
                    // вдали — доля горящих вместо отдельных окон
                    winLit = lerp(lit, saturate(_CC_LitRatio * _NightLitBoost), far);
                }

                // свет: мягкая ступенька от солнца, подсветка из сцены
                Light ml = GetMainLight();
                half wrap = saturate((dot(n, ml.direction) + _Wrap) / (1.0h + _Wrap));
                // дальний город: тени и затенение только по карте высот (настоящих теней здесь нет)
                half hsh = lerp(1.0h, CC_HeightShadow(i.positionWS, n, ml.direction), (half)_CC_HMapP.x);
                half hao = lerp(1.0h, CC_HeightAO(i.positionWS, n), (half)_CC_HMapP.y);
                half ramp = smoothstep(0.25h, 0.65h, wrap) * hsh;
                half shLum = max(dot(SampleSH(half3(0, 1, 0)), half3(0.333h, 0.333h, 0.333h)), 0.0h);
                half3 light = (lerp(_ShadowColor.rgb * saturate(shLum * 1.6h), ml.color * _LightTint.rgb, ramp)
                            + SampleSH(n) * _Ambient * 0.5h * hao) * lerp(1.0h, hao, 0.3h);
                half3 col = base * light;
                col = CC_SnowApply(col, CC_SnowMask(n.y, i.uv * 0.25) * (half)CC_WxMask(i.positionWS.xz), light);                    // снег на крышах дальнего города
                // окна днём — светлее и холоднее стены; ночью — горят
                half3 winDay = _WinColor.rgb * light;
                col = lerp(col, winDay, winMask * _WinStrength * (1.0h - night));
                col = lerp(col, col * 0.45h, winMask * night * (1.0h - winLit));
                col += _WinNight.rgb * winMask * winLit * night;
                col += CC_Flash(base, n);                                         // молния

                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vertDepth(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half fragDepth(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vertDN(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 fragDN(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
