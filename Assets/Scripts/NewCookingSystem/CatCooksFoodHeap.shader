// ============================================================================
//  CatCooks / Food Heap   (v2)
//
//  Кучки и стружки еды. URP, Unity 6. Сделан под мультяшную гамму сцены:
//  яркий тёплый свет, цветные (не серые) тени, лёгкий контур у кусочков.
//
//  • Цвет продукта — цвет вершин (его ставит FoodHeapRenderer) или _BaseColor
//    через MaterialPropertyBlock (живые частицы). Состояние (мытое, варёное,
//    грязное) меняет цвет, текстура одна.
//  • Узор: трипланар в осях объекта (масса, горки) ИЛИ UV меша (стружки —
//    бороздки идут вдоль длины). Альфа вершины = сколько узора.
//  • Свет полосами (как у мультяшных материалов проекта): освещённая сторона,
//    мягкий переход, тень ТЁПЛОГО цвета. Тени от других объектов ослаблены —
//    кучка не чернеет от собственных стружек.
//  • Влажный блик: маленькая резкая искра — тёртая картошка сочная.
//  • Контур: отдельный проход, цвет = цвет продукта темнее. Работает и для
//    живых частиц (инстансинг).
// ============================================================================
Shader "CatCooks/Food Heap"
{
    Properties
    {
        [Header(Color)]
        _BaseColor ("Оттенок (умножается)", Color) = (1,1,1,1)
        _Saturation ("Насыщенность", Range(0, 2)) = 1.15
        _Brightness ("Яркость", Range(0.5, 2.5)) = 1.15
        _Flat ("Плоский свет (0 — объём, 1 — ровно как ландшафт)", Range(0, 1)) = 0.55
        _FlatColor ("Цвет плоского света (тёплый)", Color) = (1.05, 1.0, 0.92, 1)
        _SkyTint ("Небесный свет: верх", Color) = (1.12, 1.1, 1.04, 1)
        _GroundTint ("Небесный свет: бока и низ", Color) = (0.78, 0.66, 0.55, 1)

        [Header(Texture)]
        [NoScaleOffset] _BaseMap ("Текстура (альбедо)", 2D) = "white" {}
        [NoScaleOffset][Normal] _BumpMap ("Карта нормалей", 2D) = "bump" {}
        _BumpScale ("Сила нормалей", Range(0, 2)) = 1
        [Toggle] _UseMeshUV ("UV меша вместо трипланара (для стружек)", Float) = 0
        _TexScale ("Плотность узора (трипланар: повторов на метр; UV: повторов)", Float) = 3
        _BlendSharpness ("Резкость смешивания граней", Range(1, 16)) = 6
        _TexDetail ("Сила узора", Range(0, 1)) = 1
        _DetailBoost ("Яркость узора", Range(0.5, 2)) = 1.08

        [Header(Toon Light)]
        _LitColor ("Цвет света (умножается)", Color) = (1, 0.98, 0.94, 1)
        _ShadowColor ("Цвет тени (тёплый)", Color) = (0.86, 0.66, 0.46, 1)
        _Band ("Граница свет/тень", Range(-1, 1)) = -0.05
        _BandSoft ("Мягкость границы", Range(0.001, 0.6)) = 0.22
        _MidBand ("Полутон (0 — нет)", Range(0, 1)) = 0.35
        _ShadowStrength ("Тени от других объектов", Range(0, 1)) = 0.3
        _Ambient ("Окружение сцены", Range(0, 2)) = 0.6
        _AmbientFloor ("Минимум окружения", Color) = (0.55, 0.5, 0.45, 1)

        [Header(Wet Glint)]
        _GlintColor ("Цвет блика", Color) = (1, 1, 0.95, 1)
        _GlintSize ("Размер блика", Range(0.8, 0.999)) = 0.94
        _GlintStrength ("Сила блика", Range(0, 2)) = 0.55

        [Header(Rim)]
        _RimColor ("Цвет подсветки края", Color) = (1, 0.92, 0.75, 1)
        _Rim ("Подсветка края", Range(0, 1)) = 0.25
        _RimPower ("Ширина подсветки (больше — уже)", Range(1, 8)) = 3.5

        [Header(Outline)]
        _OutlineWidth ("Толщина контура, м", Range(0, 0.03)) = 0.004
        _OutlineDarken ("Контур = цвет продукта ×", Range(0, 1)) = 0.45
        _OutlineColor ("Подмешать к контуру", Color) = (0.35, 0.2, 0.08, 1)
        _OutlineTint ("Сколько подмешать", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half  _Saturation;
            half  _Brightness;
            half  _Flat;
            half4 _FlatColor;
            half4 _SkyTint;
            half4 _GroundTint;
            half  _BumpScale;
            half  _UseMeshUV;
            float _TexScale;
            half  _BlendSharpness;
            half  _TexDetail;
            half  _DetailBoost;
            half4 _LitColor;
            half4 _ShadowColor;
            half  _Band;
            half  _BandSoft;
            half  _MidBand;
            half  _ShadowStrength;
            half  _Ambient;
            half4 _AmbientFloor;
            half4 _GlintColor;
            half  _GlintSize;
            half  _GlintStrength;
            half4 _RimColor;
            half  _Rim;
            half  _RimPower;
            float _OutlineWidth;
            half  _OutlineDarken;
            half4 _OutlineColor;
            half  _OutlineTint;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

        half3 Saturate3(half3 c, half s)
        {
            half l = dot(c, half3(0.299h, 0.587h, 0.114h));
            return max(0.0h, lerp(l.xxx, c, s));
        }
        ENDHLSL

        // ─────────────────────────── основной проход ───────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4  color      : TEXCOORD3;
                float2 uv         : TEXCOORD4;
                float4 tangentOS  : TEXCOORD5;
                half   fog        : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs vpi = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = vpi.positionCS;
                o.positionWS = vpi.positionWS;
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.tangentOS = v.tangentOS;
                o.uv = v.uv;
                o.color = v.color;
                o.fog = ComputeFogFactor(vpi.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Матрица объекта у каждой стружки своя (инстансинг) — нужна и здесь.
                UNITY_SETUP_INSTANCE_ID(i);

                float3 nOS = normalize(i.normalOS);
                half3 tex;
                float3 bentOS;

                if (_UseMeshUV > 0.5h)
                {
                    // ── UV меша: у стружки бороздки идут вдоль длины ──
                    float2 uv = i.uv * _TexScale;
                    tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                    half3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
                    float3 t = normalize(i.tangentOS.xyz);
                    float3 b = cross(nOS, t) * (i.tangentOS.w >= 0 ? 1.0 : -1.0);
                    bentOS = normalize(t * tn.x + b * tn.y + nOS * tn.z);
                }
                else
                {
                    // ── трипланар в осях объекта: масса, горки ──
                    float3 w = pow(abs(nOS), _BlendSharpness);
                    w /= max(w.x + w.y + w.z, 1e-4);
                    float3 p = i.positionOS * _TexScale;
                    float2 uvX = p.zy, uvY = p.xz, uvZ = p.xy;
                    tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * w.x
                        + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * w.y
                        + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * w.z;
                    half3 tnX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _BumpScale);
                    half3 tnY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), _BumpScale);
                    half3 tnZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _BumpScale);
                    tnX = half3(tnX.xy + nOS.zy, nOS.x);
                    tnY = half3(tnY.xy + nOS.xz, nOS.y);
                    tnZ = half3(tnZ.xy + nOS.xy, nOS.z);
                    bentOS = normalize(tnX.zyx * w.x + tnY.xzy * w.y + tnZ.xyz * w.z);
                }

                // Узор только там, где альфа вершины > 0: масса и стружки — с узором,
                // гладкие кусочки — без. Нормали от узора — туда же.
                half detail = _TexDetail * i.color.a;
                float3 nFinal = normalize(lerp(nOS, bentOS, detail));
                float3 nWS = normalize(TransformObjectToWorldNormal(nFinal));

                // ── цвет ──
                half lum = dot(tex, half3(0.299h, 0.587h, 0.114h));
                half3 albedo = i.color.rgb * _BaseColor.rgb;
                albedo *= lerp(1.0h, lum * _DetailBoost, detail);
                albedo = Saturate3(albedo, _Saturation);

                // ── мультяшный свет ──
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                half ndl = dot(nWS, light.direction);
                half band = smoothstep(_Band - _BandSoft, _Band + _BandSoft, ndl);
                // Полутон: вторая, более светлая полоса ближе к свету.
                half mid = smoothstep(_Band + 0.35h - _BandSoft, _Band + 0.35h + _BandSoft, ndl);
                half lit = lerp(band, band * (0.82h + 0.18h * mid), _MidBand);
                half shadow = lerp(1.0h, light.shadowAttenuation, _ShadowStrength);
                lit *= shadow;

                half3 lightCol = light.color * _LitColor.rgb;
                half3 shadowCol = _ShadowColor.rgb;                     // тёплая тень, не серая
                half3 ambient = max(SampleSH(nWS) * _Ambient, _AmbientFloor.rgb);
                half3 diffuse = lerp(shadowCol * ambient, lightCol, lit);
                // Плоский свет: ровно освещено, как ландшафт, без впадин и теней.
                diffuse = lerp(diffuse, _FlatColor.rgb, _Flat);
                // Небесный свет: верх светлее (небо), бока и низ теплее и темнее (земля).
                // Объём без настоящих теней — горка не плоская даже при плоском свете.
                half sky = saturate(nWS.y * 0.5h + 0.5h);
                diffuse *= lerp(_GroundTint.rgb, _SkyTint.rgb, sky * sky * (3.0h - 2.0h * sky));

                // ── влажный блик ──
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 Hh = normalize(light.direction + V);
                half glint = smoothstep(_GlintSize, _GlintSize + 0.015h, saturate(dot(nWS, Hh))) * lit;

                // ── подсветка края ──
                half rim = pow(1.0h - saturate(dot(nWS, V)), _RimPower) * _Rim * (0.4h + 0.6h * lit);

                half3 col = albedo * diffuse * _Brightness
                          + _GlintColor.rgb * glint * _GlintStrength * light.color
                          + _RimColor.rgb * rim;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ─────────────────────────── контур ───────────────────────────
        // Отдельный проход: копия формы, чуть раздутая, рисуется изнанкой.
        // Раздувание — в метрах мира, поэтому одинаково у крупных и мелких кусочков.
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertO
            #pragma fragment fragO
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 color : TEXCOORD0; half fog : TEXCOORD1; };

            V vertO(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = normalize(TransformObjectToWorldNormal(v.normalOS));
                // У кубиков нормали граней разные — чтобы на углах не было щелей,
                // смешиваем с направлением от центра объекта.
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 fromC = posWS - centerWS;
                float3 radial = dot(fromC, fromC) > 1e-10 ? normalize(fromC) * 0.6 : float3(0, 0, 0);
                float3 dir = normalize(nWS + radial);
                posWS += dir * _OutlineWidth;
                o.positionCS = TransformWorldToHClip(posWS);
                half3 prod = v.color.rgb * _BaseColor.rgb * _OutlineDarken;
                o.color = lerp(prod, _OutlineColor.rgb, _OutlineTint);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 fragO(V i) : SV_Target
            {
                if (_OutlineWidth <= 0.00001) discard;
                return half4(MixFog(i.color, i.fog), 1);
            }
            ENDHLSL
        }

        // ─────────────────────────── тени ───────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V vertShadow(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }

            half4 fragShadow(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V vertDepth(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half fragDepth(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 nWS : TEXCOORD0; };
            V vertDN(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 fragDN(V i) : SV_Target { return half4(normalize(i.nWS), 0); }
            ENDHLSL
        }
    }

    FallBack Off
}
