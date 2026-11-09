// ============================================================================
//  CatCooks / Food Strands
//
//  ОБЪЁМНАЯ СТРУЖКА. Тысячи изогнутых ленточек тёртого продукта, которые
//  строит вершинный шейдер. Никакого геометрического шейдера (его нет на
//  Mac/Metal, и на многих видеокартах он медленный): у каждой ленточки в
//  буфере лежат только корень, направление и несколько чисел, а форму —
//  изгиб, дугу над поверхностью, закрутку, сужение к концам — видеокарта
//  собирает сама из номера вершины. Все ленточки кучки — один вызов отрисовки.
//
//  Сечение ленточки «скруглённое»: нормаль поворачивается поперёк ширины,
//  поэтому ленточка выглядит мягкой, а не плоской бумажкой.
//
//  Свет — тот же мультяшный, что у CatCooks/Food Heap: полосы, тёплая тень,
//  влажный блик, подсветка края.
// ============================================================================
Shader "CatCooks/Food Strands"
{
    Properties
    {
        [Header(Color)]
        _BaseColor ("Оттенок", Color) = (1,1,1,1)
        _Saturation ("Насыщенность", Range(0, 2)) = 1.15
        _Brightness ("Яркость", Range(0.5, 2.5)) = 1.2
        _Flat ("Плоский свет (0 — объём, 1 — ровно как ландшафт)", Range(0, 1)) = 0.55
        _FlatColor ("Цвет плоского света (тёплый)", Color) = (1.05, 1.0, 0.92, 1)
        _SkyTint ("Небесный свет: верх", Color) = (1.12, 1.1, 1.04, 1)
        _GroundTint ("Небесный свет: бока и низ", Color) = (0.78, 0.66, 0.55, 1)

        [Header(Texture)]
        [NoScaleOffset] _BaseMap ("Текстура стружки (вдоль U)", 2D) = "white" {}
        [NoScaleOffset][Normal] _BumpMap ("Нормали стружки", 2D) = "bump" {}
        _BumpScale ("Сила нормалей", Range(0, 2)) = 0.8
        _TexPerMeter ("Повторов текстуры на метр длины", Float) = 12
        _TexDetail ("Сила узора", Range(0, 1)) = 0.8
        _Softness ("Мягкость сечения", Range(0, 1.5)) = 0.9

        [Header(Toon Light)]
        _LitColor ("Цвет света", Color) = (1, 0.98, 0.94, 1)
        _ShadowColor ("Цвет тени (тёплый)", Color) = (0.86, 0.66, 0.46, 1)
        _Band ("Граница свет/тень", Range(-1, 1)) = -0.1
        _BandSoft ("Мягкость границы", Range(0.001, 0.6)) = 0.25
        _ShadowStrength ("Тени от других объектов", Range(0, 1)) = 0.35
        _Ambient ("Окружение сцены", Range(0, 2)) = 0.6
        _AmbientFloor ("Минимум окружения", Color) = (0.55, 0.5, 0.45, 1)
        _DepthDarken ("Глубинная тень (нижние слои темнее)", Range(0, 1)) = 0.35

        [Header(Wet Glint)]
        _GlintColor ("Цвет блика", Color) = (1, 1, 0.95, 1)
        _GlintSize ("Размер блика", Range(0.8, 0.999)) = 0.95
        _GlintStrength ("Сила блика", Range(0, 2)) = 0.5

        [Header(Rim)]
        _RimColor ("Цвет подсветки края", Color) = (1, 0.92, 0.75, 1)
        _Rim ("Подсветка края", Range(0, 1)) = 0.3
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
            float _TexPerMeter;
            half  _TexDetail;
            half  _Softness;
            half4 _LitColor;
            half4 _ShadowColor;
            half  _Band;
            half  _BandSoft;
            half  _ShadowStrength;
            half  _Ambient;
            half4 _AmbientFloor;
            half  _DepthDarken;
            half4 _GlintColor;
            half  _GlintSize;
            half  _GlintStrength;
            half4 _RimColor;
            half  _Rim;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

        // ── данные ленточек (ставит FoodStrandField через MaterialPropertyBlock) ──
        struct Strand
        {
            float3 root;    // корень на поверхности (оси кучки)
            float3 dir;     // направление вдоль поверхности
            float3 nrm;     // нормаль поверхности
            float  len;     // длина, м
            float  width;   // ширина, м
            float  bend;    // изгиб в плоскости поверхности, радиан на всю длину
            float  lift;    // высота дуги над поверхностью, м
            float  shade;   // оттенок 0.85..1.1
            float  twist;   // закрутка вдоль длины, радиан
            float  depth;   // 0 — верхний слой, 1 — нижний
        };
        StructuredBuffer<Strand> _Strands;
        float4x4 _Frame;          // оси кучки → мир
        float4 _Tint;             // цвет продукта (состояние)
        int _Segments;
        float _WidthScale;        // LOD: вдали ленточек меньше, но они шире (ставит FoodStrandField)

        // Угол квада: x — 0/1 вдоль сегмента, y — сторона −1/+1.
        static const float2 kQuad[6] = { float2(0,-1), float2(1,-1), float2(0,1),
                                         float2(1,-1), float2(1,1),  float2(0,1) };

        struct StrandVertex
        {
            float3 posWS;
            float3 nrmWS;
            float3 tanWS;
            float2 uv;
            float  shade;
            float  depth;
        };

        StrandVertex BuildStrand(uint vid, uint iid)
        {
            Strand s = _Strands[iid];
            uint seg = vid / 6u;
            float2 q = kQuad[vid % 6u];
            float t = (seg + q.x) / (float)_Segments;
            float side = q.y;

            float3 T = normalize(s.dir);
            float3 N = normalize(s.nrm);
            float3 B = normalize(cross(N, T));

            // Дуга постоянной кривизны в плоскости поверхности.
            float k = s.bend;
            float3 along;
            float3 tanAt;
            if (abs(k) < 1e-3)
            {
                along = T * (s.len * t);
                tanAt = T;
            }
            else
            {
                float a = k * t;
                along = s.len * (T * (sin(a) / k) + B * ((1.0 - cos(a)) / k));
                tanAt = T * cos(a) + B * sin(a);
            }

            // Поднятие над поверхностью дугой — ленточка лежит «горбиком».
            float arch = s.lift * sin(PI * t);
            float3 p = s.root + along + N * (arch + s.width * 0.15);

            // Поперечное направление с закруткой вокруг оси ленточки.
            float3 lat = normalize(cross(N, tanAt));
            float tw = s.twist * (t - 0.5);
            float3 up2 = N * cos(tw) + lat * sin(tw);
            lat = lat * cos(tw) - N * sin(tw);

            // Сужение к концам — не обрубки.
            float taper = lerp(0.35, 1.0, saturate(min(t, 1.0 - t) * 6.0));
            float w = s.width * max(1.0, _WidthScale);
            p += lat * (side * w * 0.5 * taper);

            // Скруглённое сечение: нормаль уходит вбок у краёв.
            float3 n = normalize(up2 + lat * side * _Softness);

            StrandVertex o;
            o.posWS = mul(_Frame, float4(p, 1.0)).xyz;
            o.nrmWS = normalize(mul((float3x3)_Frame, n));
            o.tanWS = normalize(mul((float3x3)_Frame, tanAt));
            o.uv = float2(t * s.len * _TexPerMeter, side * 0.5 + 0.5);
            o.shade = s.shade;
            o.depth = s.depth;
            return o;
        }

        half3 Saturate3(half3 c, half sat)
        {
            half l = dot(c, half3(0.299h, 0.587h, 0.114h));
            return max(0.0h, lerp(l.xxx, c, sat));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 tangentWS  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float2 shadeDepth : TEXCOORD4;
                half   fog        : TEXCOORD5;
            };

            V vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                StrandVertex s = BuildStrand(vid, iid);
                V o;
                o.positionWS = s.posWS;
                o.positionCS = TransformWorldToHClip(s.posWS);
                o.normalWS = s.nrmWS;
                o.tangentWS = s.tanWS;
                o.uv = s.uv;
                o.shadeDepth = float2(s.shade, s.depth);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                if (!front) n = -n;                        // ленточка двусторонняя
                float3 t = normalize(i.tangentWS - n * dot(i.tangentWS, n));
                float3 b = cross(n, t);

                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                half3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 nWS = normalize(lerp(n, t * tn.x + b * tn.y + n * tn.z, _TexDetail));

                half lum = dot(tex, half3(0.299h, 0.587h, 0.114h));
                half3 albedo = _Tint.rgb * _BaseColor.rgb * i.shadeDepth.x;
                albedo *= lerp(1.0h, lum * 1.08h, _TexDetail);
                albedo *= lerp(1.0h, 1.0h - _DepthDarken * (1.0h - _Flat * 0.8h), i.shadeDepth.y);   // нижние слои темнее
                albedo = Saturate3(albedo, _Saturation);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                half ndl = dot(nWS, light.direction);
                half lit = smoothstep(_Band - _BandSoft, _Band + _BandSoft, ndl);
                lit *= lerp(1.0h, light.shadowAttenuation, _ShadowStrength);

                half3 ambient = max(SampleSH(nWS) * _Ambient, _AmbientFloor.rgb);
                half3 diffuse = lerp(_ShadowColor.rgb * ambient, light.color * _LitColor.rgb, lit);
                // Плоский свет: ровно освещено, как ландшафт, без впадин и теней.
                diffuse = lerp(diffuse, _FlatColor.rgb, _Flat);
                // Небесный свет: верх светлее (небо), бока и низ теплее и темнее (земля).
                // Объём без настоящих теней — горка не плоская даже при плоском свете.
                half sky = saturate(nWS.y * 0.5h + 0.5h);
                diffuse *= lerp(_GroundTint.rgb, _SkyTint.rgb, sky * sky * (3.0h - 2.0h * sky));

                float3 Vd = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 H = normalize(light.direction + Vd);
                half glint = smoothstep(_GlintSize, _GlintSize + 0.015h, saturate(dot(nWS, H))) * lit;
                half rim = pow(1.0h - saturate(dot(nWS, Vd)), 3.5h) * _Rim * (0.4h + 0.6h * lit);

                half3 col = albedo * diffuse * _Brightness
                          + _GlintColor.rgb * glint * _GlintStrength * light.color
                          + _RimColor.rgb * rim;
                return half4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 4.5
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 vertShadow(uint vid : SV_VertexID, uint iid : SV_InstanceID) : SV_POSITION
            {
                StrandVertex s = BuildStrand(vid, iid);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - s.posWS);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(s.posWS, s.nrmWS, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 fragShadow() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma target 4.5
            float4 vertDepth(uint vid : SV_VertexID, uint iid : SV_InstanceID) : SV_POSITION
            {
                return TransformWorldToHClip(BuildStrand(vid, iid).posWS);
            }
            half fragDepth() : SV_Target { return 0; }
            ENDHLSL
        }
        // ─────────── глубина + нормали ───────────
        // Без этого прохода эффекты экрана (контур объектов, SSAO) «не видят»
        // ленточки и рисуют контуры предметов ПОЗАДИ кучки поверх неё.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma target 4.5
            struct VDN { float4 positionCS : SV_POSITION; float3 nWS : TEXCOORD0; };
            VDN vertDN(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                StrandVertex s = BuildStrand(vid, iid);
                VDN o;
                o.positionCS = TransformWorldToHClip(s.posWS);
                o.nWS = s.nrmWS;
                return o;
            }
            half4 fragDN(VDN i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.nWS);
                if (!front) n = -n;
                return half4(n, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
