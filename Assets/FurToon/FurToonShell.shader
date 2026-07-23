// ============================================================================
//  FurToonShell v3 MAX — Anime Toon + Shell Fur для URP (ПК / консоли)
//  Использовать вместе со скриптом CatFurController.cs (v2)
//
//  Слой 0 = "кожа" (toon), слои 1..N-1 = оболочки меха.
//
//  Fur Control Map (_FurMask, каналы):
//    R = длина меха   (0 = меха нет, 1 = полная длина _FurLength)
//    G = контур       (0 = аутлайн выключен — усы, ресницы; 1 = включен)
//    B = подвижность  (0 = мех жёсткий, 1 = полностью колышется)
//
//  Flow Map (_FlowMap, опционально):
//    RG = направление укладки шерсти в касательном пространстве
//    (0.5, 0.5) = нейтрально. Без карты мех растёт по нормали.
//
//  Новое в v3:
//   * Трёхступенчатая аниме-рампа (свет / полутон / тень) с тонировкой полутона
//   * Анизотропный блик Kajiya-Kay вдоль ворса ("шёлковый" отблеск шерсти)
//   * Fake-SSS: контровый свет просвечивает мех по краям
//   * Завиток ворсинок (curl) + индивидуальное трепетание каждой ворсинки
//   * Порывы ветра (амплитудная модуляция) + "дыхание" меха в покое
//   * Вариация цвета по ворсинкам + выгоревшие кончики (_TipColor)
//   * Смещение центров ворсинок (ломает регулярную сетку)
//   * MSAA-сглаживание ворса (AlphaToMask + fwidth)
//   * Контур: ширина адаптируется к дистанции, цвет подмешивает альбедо
// ============================================================================
Shader "Toon/FurToonShell"
{
    Properties
    {
        [Header(Base)]
        _BaseMap        ("Albedo", 2D) = "white" {}
        _BaseColor      ("Tint", Color) = (1,1,1,1)
        _FurMask        ("Fur Control Map (R len, G outline, B motion)", 2D) = "white" {}
        _FlowMap        ("Flow Map (RG = comb dir, optional)", 2D) = "gray" {}
        _CombStrength   ("Comb Strength", Range(0,2)) = 0

        [Header(Toon Ramp)]
        _ShadeColor     ("Shadow Tint", Color) = (0.58, 0.48, 0.72, 1)
        _MidColor       ("Halftone Tint", Color) = (0.9, 0.72, 0.72, 1)
        _ShadowThreshold("Shadow Threshold", Range(0,1)) = 0.35
        _MidThreshold   ("Halftone Threshold", Range(0,1)) = 0.62
        _ShadowSoftness ("Band Softness", Range(0.001,0.5)) = 0.07
        _AmbientBoost   ("Ambient Boost", Range(0,2)) = 0.6

        [Header(Anime Specular and Rim)]
        _SpecColor2     ("Specular Color", Color) = (1, 0.96, 0.87, 1)
        _SpecSize       ("Specular Size", Range(0.001,0.5)) = 0.08
        _SpecStrength   ("Specular Strength", Range(0,1)) = 0.25
        _RimColor       ("Rim Color", Color) = (1, 0.85, 0.6, 1)
        _RimPower       ("Rim Power", Range(0.5,10)) = 3.5
        _RimStrength    ("Rim Strength", Range(0,2)) = 0.55

        [Header(Fur Sheen Kajiya Kay)]
        _AnisoColor     ("Sheen Color", Color) = (1, 0.92, 0.72, 1)
        _AnisoStrength  ("Sheen Strength", Range(0,2)) = 0.5
        _AnisoExponent  ("Sheen Sharpness", Range(4, 256)) = 64

        [Header(Fake Subsurface Backlight)]
        _SSSColor       ("Backlight Color", Color) = (1, 0.5, 0.25, 1)
        _SSSStrength    ("Backlight Strength", Range(0,2)) = 0.6
        _SSSPower       ("Backlight Power", Range(1,16)) = 5
        _SSSDistort     ("Backlight Distortion", Range(0,1)) = 0.4

        [Header(Fur Shells)]
        _FurLength      ("Fur Length (m)", Range(0, 0.2)) = 0.03
        _FurDensity     ("Fur Density", Range(50, 3000)) = 700
        _FurThickness   ("Strand Thickness", Range(0.1, 2)) = 1.0
        _FurShape       ("Strand Taper", Range(0.5, 4)) = 1.5
        _CellJitter     ("Strand Position Jitter", Range(0,1)) = 0.6
        _CurlAmount     ("Strand Curl", Range(0,2)) = 0.5
        _RootShadow     ("Root Occlusion", Range(0,1)) = 0.35
        _TipBrighten    ("Tip Brighten", Range(0,1)) = 0.12
        _StrandVariation("Strand Color Variation", Range(0,1)) = 0.25
        _TipColor       ("Tip Color (bleached)", Color) = (1, 0.93, 0.78, 1)
        _TipColorStrength("Tip Color Strength", Range(0,1)) = 0.3

        [Header(Motion)]
        _WindDir        ("Wind Direction (xyz)", Vector) = (1, 0, 0.4, 0)
        _WindAmp        ("Wind Amplitude", Range(0,2)) = 0.3
        _WindFreq       ("Wind Frequency", Range(0,20)) = 6
        _GustDepth      ("Wind Gustiness", Range(0,1)) = 0.45
        _Gravity        ("Gravity Sag", Range(0,2)) = 0.25
        _StrandFlutter  ("Strand Flutter", Range(0,2)) = 0.5
        _FlutterSpeed   ("Flutter Speed", Range(0,30)) = 9
        _BreathAmp      ("Idle Breathing", Range(0,0.3)) = 0.05
        _BreathFreq     ("Breathing Speed", Range(0,6)) = 1.6

        [Header(Outline)]
        _OutlineWidth   ("Outline Width (m)", Range(0, 0.01)) = 0.0018
        _OutlineColor   ("Outline Color", Color) = (0.16, 0.09, 0.06, 1)
        _OutlineAlbedoMix("Outline Albedo Mix", Range(0,1)) = 0.5
        _OutlineDistScale("Outline Distance Scale", Range(0,3)) = 1

        // --- Заполняются скриптом CatFurController через MaterialPropertyBlock ---
        [HideInInspector]_ShellIndex ("Shell Index", Float) = 0
        [HideInInspector]_ShellCount ("Shell Count", Float) = 16
        [HideInInspector]_PhysOffset ("Physics Offset", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
        TEXTURE2D(_FurMask);  SAMPLER(sampler_FurMask);
        TEXTURE2D(_FlowMap);  SAMPLER(sampler_FlowMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            float  _CombStrength;
            half4  _ShadeColor, _MidColor;
            float  _ShadowThreshold, _MidThreshold, _ShadowSoftness, _AmbientBoost;
            half4  _SpecColor2;
            float  _SpecSize, _SpecStrength;
            half4  _RimColor;
            float  _RimPower, _RimStrength;
            half4  _AnisoColor;
            float  _AnisoStrength, _AnisoExponent;
            half4  _SSSColor;
            float  _SSSStrength, _SSSPower, _SSSDistort;
            float  _FurLength, _FurDensity, _FurThickness, _FurShape;
            float  _CellJitter, _CurlAmount;
            float  _RootShadow, _TipBrighten;
            float  _StrandVariation;
            half4  _TipColor;
            float  _TipColorStrength;
            float4 _WindDir;
            float  _WindAmp, _WindFreq, _GustDepth, _Gravity;
            float  _StrandFlutter, _FlutterSpeed, _BreathAmp, _BreathFreq;
            float  _OutlineWidth;
            half4  _OutlineColor;
            float  _OutlineAlbedoMix, _OutlineDistScale;
            float  _ShellIndex, _ShellCount;
            float4 _PhysOffset;
        CBUFFER_END

        float ShellHeight()
        {
            return (_ShellCount > 1.0) ? _ShellIndex / (_ShellCount - 1.0) : 0.0;
        }

        float3 SampleFurCtrlLod(float2 uv)
        {
            return SAMPLE_TEXTURE2D_LOD(_FurMask, sampler_FurMask, uv, 0).rgb;
        }

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float2 Hash22(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.xx + p3.yz) * p3.zy);
        }

        // ------------------------------------------------------------------
        // Полное смещение вершины оболочки + приблизительное направление
        // ворса (для анизотропного блика).
        // ------------------------------------------------------------------
        void ApplyFurOffset(inout float3 posWS, float3 nWS, float3 combWS,
                            float2 uv, out float3 strandDirWS)
        {
            float h = ShellHeight();
            float3 ctrl   = SampleFurCtrlLod(uv);
            float  lenMul = ctrl.r;
            float  motion = ctrl.b;

            // "Дыхание" меха: едва заметная пульсация длины в покое
            float breath = 1.0 + _BreathAmp * sin(_Time.y * _BreathFreq
                                                  + posWS.y * 0.7);
            float len = _FurLength * lenMul * breath;

            // Порывистый ветер: две частоты + медленная амплитудная модуляция
            float  phase   = dot(posWS, float3(2.1, 1.3, 1.7));
            float  gust01  = 0.5 + 0.5 * sin(_Time.y * 0.9  + phase * 0.21)
                                       * sin(_Time.y * 0.37 + 1.7);
            float  gustAmp = _WindAmp * lerp(1.0 - _GustDepth,
                                             1.0 + _GustDepth * 0.5, gust01);
            float  sway    = sin(_Time.y * _WindFreq + phase)
                           + 0.5 * sin(_Time.y * _WindFreq * 2.33 + phase * 1.7);
            float3 windDir = normalize(_WindDir.xyz + float3(1e-4, 0, 0));

            float3 bend = (windDir * (sway * gustAmp) + _PhysOffset.xyz) * motion
                        + float3(0, -1, 0) * _Gravity;

            // Выдавливание по нормали + укладка (comb, линейно по h)
            // + изгиб ветром/гравитацией/инерцией (квадратично: корни стоят)
            posWS += nWS * (len * h);
            posWS += combWS * (_CombStrength * len * h);
            posWS += bend * (len * h * h);

            strandDirWS = normalize(nWS
                                    + combWS * _CombStrength * 0.8
                                    + bend * 0.35);
        }

        // ------------------------------------------------------------------
        // Ворсинки. Возвращает альфу ворсинки (с AA-краем) и её случайное
        // число (для вариации цвета). Для слоя 0 возвращает 1.
        // ------------------------------------------------------------------
        float FurStrand(float2 uv, float h, out float strandRnd)
        {
            strandRnd = 0.5;
            if (_ShellIndex < 0.5) return 1.0;

            half lenMul = SAMPLE_TEXTURE2D(_FurMask, sampler_FurMask, uv).r;
            clip(lenMul - 0.02);

            float2 uvD  = uv * _FurDensity;
            float2 cell = floor(uvD);
            float2 f    = (frac(uvD) - 0.5) * 2.0;      // -1..1 внутри ячейки

            float2 rnd2 = Hash22(cell);
            strandRnd   = rnd2.x;

            // Сдвиг центра ворсинки — ломает регулярную сетку
            f += (Hash22(cell + 7.31) - 0.5) * (2.0 * _CellJitter);

            // Завиток: каждая ворсинка уводит кончик в свою сторону
            float  curlAng = strandRnd * 6.2831;
            float2 curlDir = float2(cos(curlAng), sin(curlAng));
            f += curlDir * (h * h * _CurlAmount);

            // Индивидуальное трепетание кончика на ветру
            float t = _Time.y * _FlutterSpeed + strandRnd * 6.2831;
            f += float2(sin(t), cos(t * 1.31)) * (h * h * _StrandFlutter * 0.5);

            // Ворсинка живёт, пока её "рост" выше текущей высоты оболочки
            float taper = pow(abs(h), _FurShape);
            float life  = strandRnd - taper;
            clip(life);

            float radius = _FurThickness * saturate(life / max(strandRnd, 1e-4));
            float d      = length(f);

            // Сглаженный край (при включённом MSAA даёт мягкий ворс)
            float aa    = max(fwidth(d) * 1.5, 1e-4);
            float alpha = saturate((radius - d) / aa);
            clip(alpha - 0.05);
            return alpha;
        }
        ENDHLSL

        // ====================================================================
        // PASS 1: Toon Forward (кожа + оболочки меха)
        // ====================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  shellH     : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                float3 strandWS   : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS   = TransformObjectToWorldNormal(v.normalOS);
                float2 uv    = TRANSFORM_TEX(v.uv, _BaseMap);

                // Направление укладки шерсти из flow map (касательное простр.)
                float3 tWS  = TransformObjectToWorldDir(v.tangentOS.xyz);
                float3 bWS  = cross(nWS, tWS) * v.tangentOS.w;
                float2 flow = SAMPLE_TEXTURE2D_LOD(_FlowMap, sampler_FlowMap, uv, 0).rg * 2.0 - 1.0;
                float3 combWS = tWS * flow.x + bWS * flow.y;

                float3 strandWS;
                ApplyFurOffset(posWS, nWS, combWS, uv, strandWS);

                o.positionCS = TransformWorldToHClip(posWS);
                o.positionWS = posWS;
                o.normalWS   = nWS;
                o.uv         = uv;
                o.shellH     = ShellHeight();
                o.strandWS   = strandWS;
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float strandRnd;
                float strandAlpha = FurStrand(i.uv, i.shellH, strandRnd);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb
                             * _BaseColor.rgb;

                half furAmount = SAMPLE_TEXTURE2D(_FurMask, sampler_FurMask, i.uv).r;
                bool isShell = _ShellIndex > 0.5;

                // --- Вариация цвета по ворсинкам + выгоревшие кончики ---
                if (isShell)
                {
                    albedo *= lerp(1.0 - _StrandVariation * 0.35,
                                   1.0 + _StrandVariation * 0.35, strandRnd);
                    albedo = lerp(albedo, _TipColor.rgb,
                                  i.shellH * i.shellH * _TipColorStrength);
                }

                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 T = normalize(i.strandWS);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);
                float3 L = mainLight.direction;

                // --- Трёхступенчатая аниме-рампа: тень / полутон / свет ---
                float ndl   = dot(N, L) * 0.5 + 0.5;
                float shade = ndl * mainLight.shadowAttenuation;
                float dark  = smoothstep(_ShadowThreshold - _ShadowSoftness,
                                         _ShadowThreshold + _ShadowSoftness, shade);
                float lit   = smoothstep(_MidThreshold - _ShadowSoftness,
                                         _MidThreshold + _ShadowSoftness, shade);

                half3 rampTint = lerp(_ShadeColor.rgb, _MidColor.rgb, dark);
                rampTint = lerp(rampTint, half3(1, 1, 1), lit);

                half3 color = albedo * rampTint * mainLight.color;
                color += albedo * SampleSH(N) * _AmbientBoost;

                // --- Аниме-блик (жёсткая ступенька Blinn-Phong) ---
                float3 H    = normalize(L + V);
                float  ndh  = saturate(dot(N, H));
                float  spec = smoothstep(1.0 - _SpecSize, 1.0 - _SpecSize + 0.02, ndh) * lit;
                color += _SpecColor2.rgb * (spec * _SpecStrength) * mainLight.color;

                // --- Kajiya-Kay: шёлковый отблеск вдоль ворса (только мех) ---
                float TdotH  = dot(T, H);
                float sinTH  = sqrt(saturate(1.0 - TdotH * TdotH));
                float sheen  = pow(sinTH, _AnisoExponent)
                             * smoothstep(-1.0, 0.0, TdotH);
                color += _AnisoColor.rgb * sheen * _AnisoStrength
                       * mainLight.color * (0.25 + 0.75 * lit)
                       * furAmount * (0.3 + 0.7 * i.shellH);

                // --- Fake-SSS: контровый свет просвечивает мех ---
                float3 Lt   = normalize(-L + N * _SSSDistort);
                float  sss  = pow(saturate(dot(V, Lt)), _SSSPower);
                color += _SSSColor.rgb * albedo * sss * _SSSStrength
                       * mainLight.color * furAmount * (0.35 + 0.65 * i.shellH);

                // --- Rim light ---
                float fres = pow(1.0 - saturate(dot(N, V)), _RimPower);
                float rim  = smoothstep(0.45, 0.65, fres) * _RimStrength;
                color += _RimColor.rgb * rim * (0.35 + 0.65 * lit);

                // --- Дополнительные источники света ---
                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    float a = saturate(dot(N, l.direction))
                            * l.distanceAttenuation * l.shadowAttenuation;
                    color += albedo * l.color * smoothstep(0.35, 0.55, a);
                }
                #endif

                // --- Объём меха: тёмные корни, светлые кончики ---
                float depthFactor = lerp(1.0,
                                         lerp(1.0 - _RootShadow, 1.0 + _TipBrighten, i.shellH),
                                         furAmount);
                color *= depthFactor;

                color = MixFog(color, i.fogFactor);
                return half4(color, isShell ? strandAlpha : 1.0);
            }
            ENDHLSL
        }

        // ====================================================================
        // PASS 2: Контур (inverted hull, SRPDefaultUnlit).
        // Ширина адаптируется к дистанции, цвет подмешивает альбедо.
        // G-канал карты = 0 полностью убирает контур (усы, ресницы).
        // ====================================================================
        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);

                float3 ctrl = SampleFurCtrlLod(o.uv);

                if (_ShellIndex > 0.5 || _OutlineWidth <= 0.0 || ctrl.g < 0.5)
                {
                    o.positionCS = float4(0, 0, -9999, 1);
                    o.fogFactor = 0;
                    return o;
                }

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS   = TransformObjectToWorldNormal(v.normalOS);

                // Ширина растёт с дистанцией (в пределах x1..x3),
                // чтобы контур не исчезал вдали и не был жирным вблизи
                float dist  = distance(_WorldSpaceCameraPos, posWS);
                float scale = clamp(1.0 + dist * _OutlineDistScale * 0.25, 1.0, 3.0);

                posWS += nWS * (_OutlineWidth * scale + _FurLength * ctrl.r * 0.6);
                o.positionCS = TransformWorldToHClip(posWS);
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half g = SAMPLE_TEXTURE2D(_FurMask, sampler_FurMask, i.uv).g;
                clip(g - 0.5);

                // Цветной контур: смесь заданного цвета и затемнённого альбедо
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                half3 c = lerp(_OutlineColor.rgb, albedo * 0.32, _OutlineAlbedoMix);

                c = MixFog(c, i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }

        // ====================================================================
        // PASS 3: ShadowCaster — тень отбрасывает только слой 0
        // ====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                if (_ShellIndex > 0.5)
                {
                    o.positionCS = float4(0, 0, -9999, 1);
                    return o;
                }
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS   = TransformObjectToWorldNormal(v.normalOS);
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = posCS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        // ====================================================================
        // PASS 4: DepthOnly — глубина только от слоя 0
        // ====================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings  { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                if (_ShellIndex > 0.5)
                {
                    o.positionCS = float4(0, 0, -9999, 1);
                    return o;
                }
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
