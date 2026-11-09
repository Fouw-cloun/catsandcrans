// CatCooks — дома города: тёплый стилизованный свет (мягкая ступенька свет/тень, тёплый свет, тёплая тень),
// градиентная подсветка (небо сверху, отражённый тёплый свет снизу) — яркость берётся из освещения сцены (SH),
// поэтому день/закат/ночь из Atmosphere (Mood Director) работают сами. Туман — полноэкранный из Atmosphere.
// Ночью окна в атласе (по цвету стекла) зажигаются, на стенах — тёплые пятна. В дождь стены темнеют.
// URP, SRP Batcher, GPU Instancing. Один проход + тени + глубина. Доп. источники света не считаем (фон).
Shader "CatCooks/City/Building"
{
    Properties
    {
        [MainTexture] _BaseMap ("Атлас", 2D) = "white" {}
        [MainColor] _BaseColor ("Цвет", Color) = (1,1,1,1)
        _Saturation ("Насыщенность", Range(0, 2)) = 1.1
        _Warmth ("Теплота (сдвиг к оранжевому)", Range(-1, 1)) = 0.15

        [Header(Light)]
        _LightTint ("Свет — оттенок", Color) = (1, 0.93, 0.80, 1)
        _ShadowColor ("Тень — цвет", Color) = (0.62, 0.45, 0.42, 1)
        _Wrap ("Обволакивание света", Range(0, 1)) = 0.35
        _RampCenter ("Граница света", Range(0, 1)) = 0.45
        _RampSoft ("Мягкость границы", Range(0.001, 0.5)) = 0.22
        _ShadowStrength ("Сила теней от солнца", Range(0, 1)) = 0.85

        [Header(Ambient)]
        _SkyAmbient ("Подсветка сверху (небо)", Color) = (0.75, 0.82, 0.95, 1)
        _GroundAmbient ("Подсветка снизу (тёплый отражённый)", Color) = (0.78, 0.55, 0.42, 1)
        _AmbientStrength ("Сила подсветки", Range(0, 2)) = 0.55
        _SHMix ("Цвет подсветки из сцены (SH)", Range(0, 1)) = 0.4
        _WetDarken ("Темнеть в дождь", Range(0, 1)) = 0.35

        [Header(Rim)]
        _RimColor ("Ободок — цвет", Color) = (1, 0.78, 0.55, 1)
        _RimPower ("Ободок — узость", Range(0.5, 8)) = 3.5
        _RimStrength ("Ободок — сила", Range(0, 1)) = 0.25

        [Header(Night windows in atlas)]
        [Toggle(_ATLAS_WINDOWS)] _AtlasWindows ("Окна в атласе светятся ночью", Float) = 1
        _WinKey ("Цвет стекла в атласе (подбирает «Атласы»)", Color) = (0.486, 0.565, 0.565, 1)
        _WinTol ("Допуск цвета", Range(0.02, 0.6)) = 0.12
        _WinFill ("Ночью: заполнять окно целиком (texels атласа, 0 — выкл)", Range(0, 16)) = 6
        _WinDay ("Окна днём как стекло", Range(0, 1)) = 1
        _WinGlass ("Стекло днём", Color) = (0.62, 0.90, 0.97, 1)
        _WinSky ("Небо в отражении", Color) = (0.70, 0.93, 1.0, 1)
        _WinGlint ("Шаг косых бликов, м", Float) = 7
        _WinCell ("Размер окна, м (X — ширина, Y — этаж)", Vector) = (2.5, 3, 0, 0)
        [HDR] _WinGlow ("Свет окна ночью", Color) = (4, 2.4, 1.1, 1)
        [HideInInspector] _CC_Office ("Office", Float) = 0
        [HideInInspector] _CC_TopY ("Office top Y", Float) = 0
        [HideInInspector] _CC_BandH ("Office band", Float) = 0
        [HideInInspector] _CC_FloorH ("Office floor", Float) = 0
        _NightSpill ("Тёплые пятна света на стенах ночью", Range(0, 1)) = 0.35

        [Header(Volume)]
        _BaseShade ("Тень у земли (как AO)", Range(0, 1)) = 0.3
        _BaseShadeHeight ("Тень у земли — высота, м", Float) = 3
        _TopLight ("Светлее кверху", Range(0, 0.5)) = 0.1
        _TopHeight ("Светлее кверху — до высоты, м", Float) = 45
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Saturation, _Warmth;
            half4 _LightTint, _ShadowColor;
            half _Wrap, _RampCenter, _RampSoft, _ShadowStrength;
            half4 _SkyAmbient, _GroundAmbient;
            half _AmbientStrength, _SHMix, _WetDarken;
            half4 _RimColor;
            half _RimPower, _RimStrength;
            half4 _WinKey;
            half _WinTol, _WinDay;
            float _WinFill;
            float4 _BaseMap_TexelSize;
            half4 _WinGlass, _WinSky;
            float _WinGlint;
            float4 _WinCell;
            half4 _WinGlow;
            half _NightSpill;
            half _BaseShade, _TopLight;
            float _BaseShadeHeight, _TopHeight;
            float _CC_Office, _CC_TopY, _CC_BandH, _CC_FloorH;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            #pragma shader_feature_local_fragment _ATLAS_WINDOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CityCommon.hlsl"
            #include "CityHeightShadow.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half fog : TEXCOORD3;
                nointerpolation float seed : TEXCOORD4;
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
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                float3 objPos = TransformObjectToWorld(float3(0, 0, 0));
                o.seed = CC_Hash31(floor(objPos * 0.37)) * 97.0;
                return o;
            }

            half3 Grade(half3 c)
            {
                half l = dot(c, half3(0.299, 0.587, 0.114));
                c = lerp(l.xxx, c, _Saturation);
                c *= lerp(half3(1, 1, 1), _Warmth > 0 ? half3(1.10, 1.0, 0.86) : half3(0.92, 0.98, 1.08), abs(_Warmth));
                return c;
            }

            half WinKeyMask(half3 c)
            {
                return smoothstep(0.0h, 0.5h, saturate(1.0h - length(c - _WinKey.rgb) / _WinTol));
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = Grade(tex.rgb * _BaseColor.rgb);
                half3 n = normalize(i.normalWS);
                half3 v = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                half night = saturate(_CC_Night);

                // солнце: мягкая ступенька свет/тень (тень от солнца тоже в ступеньку)
                Light ml = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half sh = lerp(1.0h, ml.shadowAttenuation, _ShadowStrength);
                // тени соседних домов по карте высот города — дальше дистанции настоящих теней (CityHeightShadows)
                half hsh = CC_HeightShadow(i.positionWS, n, ml.direction);
                sh = min(sh, lerp(1.0h, hsh, CC_HeightFar(i.positionWS) * (half)_CC_HMapP.x * _ShadowStrength));
                half hao = lerp(1.0h, CC_HeightAO(i.positionWS, n), (half)_CC_HMapP.y);
                half wrap = saturate((dot(n, ml.direction) + _Wrap) / (1.0h + _Wrap));
                half ramp = smoothstep(_RampCenter - _RampSoft, _RampCenter + _RampSoft, wrap) * sh;
                half3 sunCol = ml.color * _LightTint.rgb;
                half3 sh3 = SampleSH(n);
                half shLum = max(dot(SampleSH(half3(0, 1, 0)), half3(0.333h, 0.333h, 0.333h)), 0.0h);   // яркость неба сцены — ночью мала
                // цвет тени тоже темнеет вместе со сценой (ночью тени не светятся)
                half3 direct = lerp(_ShadowColor.rgb * saturate(shLum * 1.6h), sunCol, ramp);

                // подсветка: сверху небо, снизу тёплый отражённый от улиц; чуть SH сцены
                half up = n.y * 0.5h + 0.5h;
                half3 amb = lerp(_GroundAmbient.rgb, _SkyAmbient.rgb, up) * shLum;      // наш тёплый градиент × яркость сцены
                amb = lerp(amb, sh3, _SHMix) * _AmbientStrength * hao;
                direct *= lerp(1.0h, hao, 0.35h);                                        // затенение чуть и на свету

                half3 col = albedo * (direct + amb);
                {
                    float mtrS = _CC_Meter > 0.0 ? _CC_Meter : 3.5;
                    col = CC_SnowApply(col, CC_SnowMask(n.y, i.positionWS.xz / mtrS) * (half)CC_WxMask(i.positionWS.xz), direct + amb);   // снег на крышах и карнизах
                }

                // тёплый ободок со стороны света
                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower) * _RimStrength * (0.35h + 0.65h * ramp) * (1.0h - night * 0.8h);
                col += rim * _RimColor.rgb * albedo * 1.5h;

                // ночь: окна в атласе (по цвету стекла) + тёплые пятна света на стенах
                // окна, нарисованные в атласе: днём — мультяшное стекло (светлое, отражает небо, косые блики)
                #if defined(_ATLAS_WINDOWS)
                    half wmask = WinKeyMask(tex.rgb);
                    if (wmask > 0.001h && _WinDay > 0.001h)
                    {
                        float2 fm = CC_FacadeCoord(i.positionWS, n);
                        half3 r = reflect(-v, n);
                        half fr = pow(1.0h - saturate(dot(n, v)), 3.0h);
                        half3 glass = lerp(_WinGlass.rgb, _WinSky.rgb, saturate(0.3h + fr + r.y * 0.5h));
                        half s = (half)frac((fm.x + fm.y * 0.8) / max(_WinGlint, 0.5));
                        half glint = smoothstep(0.08h, 0.045h, abs(s - 0.5h)) + 0.5h * smoothstep(0.03h, 0.015h, abs(s - 0.62h));
                        glass += glint * 0.45h;
                        half shLumW = dot(SampleSH(half3(0, 1, 0)), half3(0.333h, 0.333h, 0.333h));
                        half sunW = dot(ml.color, half3(0.333h, 0.333h, 0.333h)) * saturate(dot(n, ml.direction) * 0.5h + 0.5h);
                        glass *= saturate(shLumW * 1.4h + sunW * 0.35h) * 1.1h;
                        col = lerp(col, glass, wmask * _WinDay * (1.0h - night));
                    }
                #endif

                if (night > 0.001h)
                {
                    float2 fcW = CC_FacadeCoord(i.positionWS, n);
                    float2 fc = (fcW - float2(0.0, _CC_GroundY)) / max(_WinCell.xy, 0.05);
                    // v3: окна атласа решают по «квартире» (крупная клетка от земли) — окно больше не режется
                    // сеткой пополам, соседние окна квартиры горят вместе
                    float2 cell = CC_AptCell(fcW);
                    half lit = CC_WindowLit(cell, i.seed);
                    half band;
                    if (_CC_Office > 3.5) lit = CC_TowerLit(fcW.y, fcW.y, cell, i.seed, (half)_CC_Office, _CC_TopY, _CC_BandH, _CC_FloorH, band);
                    else lit = CC_OfficeLit(lit, fcW.y, cell, i.seed, (half)_CC_Office, _CC_TopY, _CC_BandH, band);
                    half vertical = saturate(1.0h - abs(n.y) * 3.0h);
                    #if defined(_ATLAS_WINDOWS)
                        // НОЧЬ: окно атласа горит ЦЕЛИКОМ. В атласе внутри стекла бывают шторы, блики, переплёты —
                        // по цвету стекла они «дырки», окно светилось кусками. Закрытие дыр: пиксель внутри окна,
                        // если стекло есть по обе стороны от него (слева и справа или сверху и снизу) — край окна не растёт.
                        half mask = wmask;
                        if (_WinFill > 0.5 && night > 0.001h)
                        {
                            float2 tsz = _BaseMap_TexelSize.xy * _WinFill;
                            half inner = 0.0h;
                            [unroll] for (int k = 1; k <= 2; k++)
                            {
                                float2 o = tsz * (k * 0.5);
                                half l = WinKeyMask(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv - float2(o.x, 0)).rgb);
                                half r2 = WinKeyMask(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv + float2(o.x, 0)).rgb);
                                half d = WinKeyMask(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv - float2(0, o.y)).rgb);
                                half u = WinKeyMask(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv + float2(0, o.y)).rgb);
                                inner = max(inner, max(min(l, r2), min(d, u)));
                            }
                            mask = max(mask, inner);
                        }
                        half warm = 0.75h + 0.5h * CC_Hash21(cell + 3.1);
                        half3 glowC = _WinGlow.rgb * warm * CC_WindowTint(CC_Hash21(cell * 1.9 + i.seed + 7.7), CC_Hash21(cell + 1.3));   // разные оттенки
                        half3 spC;
                        if (CC_SpecialWindow(cell, i.seed, i.positionWS, float2(0.5, 0.5), spC) > 0.5h) { lit = 1.0h; glowC = spC; }
                        col = lerp(col, col * 0.35h, mask * night * (1.0h - lit));          // негорящие стекла ночью темнее
                        col += glowC * mask * lit * night * vertical;
                    #endif
                    // пятна: свет из соседних окон на стену (мягкий круг в клетке побольше)
                    float2 big = fc * 0.5;
                    float2 bc = floor(big);
                    half2 f = frac(big) - 0.5h;
                    half blob = saturate(1.0h - dot(f, f) * 3.2h);
                    half bl = CC_WindowLit(bc + 17.0, i.seed) * (1.0h - step(0.5h, (half)_CC_Office));   // пятна на стене — только у жилых (у небоскрёбов давали «сыпь»)
                    col += albedo * _WinGlow.rgb * 0.12h * blob * blob * bl * _NightSpill * night * vertical;
                }

                // дождь: стены темнеют и чуть насыщаются (вода)
                half wet = saturate(_CatWetness) * _WetDarken * (half)CC_WxMask(i.positionWS.xz);
                // объём: у земли темнее (контакт, как AO — пропсы и низ домов «стоят»), кверху чуть светлее
                {
                    float mtr = _CC_Meter > 0.0 ? _CC_Meter : 3.5;
                    float hy = (i.positionWS.y - _CC_GroundY) / mtr;
                    half baseK = 1.0h - _BaseShade * (1.0h - (half)smoothstep(0.0, max(_BaseShadeHeight, 0.1), hy));
                    half topK = 1.0h + _TopLight * (half)saturate(hy / max(_TopHeight, 1.0));
                    col *= baseK * topK;
                }
                col = lerp(col, col * col * 1.6h, wet * 0.5h) * (1.0h - wet * 0.35h);
                col += CC_Flash(albedo, n);                                       // молния
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
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
                float3 pws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 ld = normalize(_LightPosition - pws);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pws, nws, ld));
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
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vertDepth(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half fragDepth(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vertDN(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 fragDN(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Simple Lit"
}
