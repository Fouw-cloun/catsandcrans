// ============================================================================
//  CatCooks/Interactive  —  интерактивные детали этажа
//  Щитки, трубы, вентили, рубильники, ящики. URP.
// ----------------------------------------------------------------------------
//  ЗАЧЕМ ОТДЕЛЬНЫЙ ШЕЙДЕР, А НЕ ГАЛОЧКА В ШЕЙДЕРЕ ОКРУЖЕНИЯ
//
//  Интерактивных деталей на этаже единицы, а стен и балок сотни. Если
//  подсветку положить в общий шейдер, за неё будут платить все — включая те
//  сотни, которые никогда не подсвечиваются.
//
//  Разделение даёт и второе, менее очевидное: интерактивную деталь ВИДНО
//  вблизи, игрок на неё смотрит и целится. Ей можно позволить то, чего нельзя
//  позволить стене, — и наоборот, стену можно урезать сильнее, чем позволила
//  бы общая настройка.
//
//  ═══ ПОДСВЕТКА ИДЁТ ЧЕРЕЗ ИНСТАНСИНГ ═══
//
//  _Highlight объявлен per-instance. Это важнее, чем кажется.
//
//  Обычный материал общий на все объекты: изменишь на нём число — засветятся
//  ВСЕ щитки на этаже разом. Чтобы подсветить один, обычно делают ему копию
//  материала — и тем самым выбивают его из пакетной отрисовки, добавляя
//  отдельный вызов на каждый подсвеченный предмет.
//
//  Здесь значение живёт в буфере экземпляра. Ставится через
//  MaterialPropertyBlock, материал при этом не копируется, пакет не рвётся:
//
//      var mpb = new MaterialPropertyBlock();
//      renderer.GetPropertyBlock(mpb);
//      mpb.SetFloat("_Highlight", 1f);
//      renderer.SetPropertyBlock(mpb);
//
//  ═══ ТРИ СОСТОЯНИЯ ═══
//
//    0.0  обычное
//    0.5  доступно — деталь дышит контуром, показывая, что с ней можно
//    1.0  в прицеле — яркая обводка и свечение
//
//  Промежуточные значения смешивают, поэтому подсветку можно плавно
//  включать: резкое переключение читается как мигание интерфейса, плавное —
//  как реакция предмета.
// ============================================================================
Shader "CatCooks/Interactive"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (1,1,1,1)
        _VertexColorTint ("Vertex Color Tint", Range(0,1)) = 0

        [Space(6)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        [Header(Toon Light)]
        _ShadowThreshold ("Shadow Edge", Range(-1,1)) = 0.05
        _ShadowSoftness ("Shadow Softness", Range(0.001,0.6)) = 0.06
        _ShadowColor ("Shadow Color", Color) = (0.42,0.45,0.62,1)
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.7
        _LightWrap ("Light Wrap", Range(0,1)) = 0.25

        _SkyTint ("Sky Tint", Color) = (0.55,0.68,0.85,1)
        _GroundTint ("Ground Tint", Color) = (0.32,0.30,0.26,1)
        _AmbientStrength ("Ambient Strength", Range(0,2)) = 1

        [Header(Specular)]
        _SpecColor2 ("Specular Color", Color) = (1,1,1,1)
        _SpecSize ("Specular Size", Range(0.5,64)) = 28
        _SpecSoftness ("Specular Softness", Range(0.001,0.5)) = 0.04
        _SpecStrength ("Specular Strength", Range(0,2)) = 0.6

        [Header(Normal Map)]
        [Toggle(_NORMALMAP)] _UseNormalMap ("Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0,3)) = 1

        [Tooltip(How much the normal map moves the toon band edge. Keep it low. See the note in the shader header)]
        _NormalBandInfluence ("Normal Affects Shading Band", Range(0,1)) = 0.15

        [Header(Emission)]
        [Tooltip(Indicators and screens. Uses the base map alpha as a mask)]
        _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionPulse ("Emission Pulse", Range(0,8)) = 0

        [Header(Highlight)]
        [Tooltip(Set per object through a MaterialPropertyBlock. Never set it on the material itself or every prop lights up at once)]
        _Highlight ("Highlight (0 to 1)", Range(0,1)) = 0

        _AvailableColor ("Available Color", Color) = (0.4,0.85,1,1)
        _TargetColor ("Targeted Color", Color) = (1,0.85,0.35,1)

        _HighlightRimPower ("Highlight Rim Power", Range(0.5,8)) = 2.5
        _HighlightRimStrength ("Highlight Rim Strength", Range(0,4)) = 1.6

        [Tooltip(Breathing speed of the available state. Zero holds it steady)]
        _HighlightBreath ("Highlight Breath Speed", Range(0,8)) = 2.5

        [Header(Outline)]
        _OutlineWidth ("Outline Width (px)", Range(0,8)) = 1.6
        _OutlineColor ("Outline Color", Color) = (0.11,0.09,0.12,1)

        [Tooltip(How much the outline widens when targeted)]
        _OutlineHighlightGrow ("Outline Grow On Highlight", Range(0,4)) = 1.5

        _OutlineFadeStart ("Outline Fade Start (m)", Float) = 25
        _OutlineFadeEnd ("Outline Fade End (m)", Float) = 55
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _VertexColorTint;
            half   _Cutoff;
            half   _BumpScale;
            half   _NormalBandInfluence;

            half   _ShadowThreshold;
            half   _ShadowSoftness;
            half4  _ShadowColor;
            half   _ShadowStrength;
            half   _LightWrap;

            half4  _SkyTint;
            half4  _GroundTint;
            half   _AmbientStrength;

            half4  _SpecColor2;
            half   _SpecSize;
            half   _SpecSoftness;
            half   _SpecStrength;

            half4  _EmissionColor;
            half   _EmissionPulse;

            half4  _AvailableColor;
            half4  _TargetColor;
            half   _HighlightRimPower;
            half   _HighlightRimStrength;
            half   _HighlightBreath;

            half   _OutlineWidth;
            half4  _OutlineColor;
            half   _OutlineHighlightGrow;
            float  _OutlineFadeStart;
            float  _OutlineFadeEnd;
        CBUFFER_END

        // ЗНАЧЕНИЕ НА ЭКЗЕМПЛЯР, а не на материал. Из-за этого подсветка
        // одного щитка не трогает соседние и не рвёт пакетную отрисовку.
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float, _Highlight)
        UNITY_INSTANCING_BUFFER_END(Props)

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);

        // Цвет подсветки и её сила. Общая для обоих пассов, чтобы обводка и
        // контур не могли разойтись: разойдясь, они выглядят как две разные
        // подсветки на одном предмете.
        void CC_Highlight(float h, out half3 col, out half amount)
        {
            // Дыхание только в состоянии «доступно». В прицеле подсветка
            // должна стоять ровно: мигающая цель читается как неполадка, а не
            // как приглашение.
            half breath = _HighlightBreath > 0.001h
                ? 0.75h + 0.25h * sin(_Time.y * _HighlightBreath)
                : 1.0h;

            half avail = saturate(h * 2.0h);            // 0 → 0.5
            half aimed = saturate(h * 2.0h - 1.0h);     // 0.5 → 1

            col = lerp(_AvailableColor.rgb, _TargetColor.rgb, aimed);
            amount = lerp(avail * breath, 1.0h, aimed);
        }
        ENDHLSL

        // ==================================================================
        //  FORWARD
        // ==================================================================
        Pass
        {
            Name "InteractiveForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local _NORMALMAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;

                // Тангенс нужен только карте нормалей. Без ключа он не
                // читается, не интерполируется и ничего не стоит.
                #if defined(_NORMALMAP)
                    float4 tangentOS : TANGENT;
                #endif

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4  color      : TEXCOORD3;
                half3  ambient    : TEXCOORD4;
                half   fogFactor  : TEXCOORD5;

                #if defined(_NORMALMAP)
                    half4 tangentWS : TEXCOORD6;   // w — знак развёртки
                #endif

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);

                #if defined(_NORMALMAP)
                    VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                    // Знак развёртки хранится в w тангенса и учитывает
                    // зеркальный масштаб объекта. Без него зеркальные части
                    // модели получают вывернутый рельеф.
                    real sgn = IN.tangentOS.w * GetOddNegativeScale();
                    OUT.tangentWS = half4(nrm.tangentWS, sgn);
                #else
                    VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
                #endif

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color = IN.color;

                half up = nrm.normalWS.y * 0.5h + 0.5h;
                OUT.ambient = lerp(_GroundTint.rgb, _SkyTint.rgb, up) * _AmbientStrength;

                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);

                #if defined(_ALPHATEST_ON)
                    clip(tex.a * _BaseColor.a - _Cutoff);
                #endif

                half3 albedo = tex.rgb * _BaseColor.rgb;
                albedo *= lerp(half3(1,1,1), IN.color.rgb, _VertexColorTint);

                // ═══ НОРМАЛЬ: ДВЕ РАЗНЫЕ, И ЭТО НАРОЧНО ═══
                //
                // Мультяшный свет — это СТУПЕНЬКА: smoothstep разрезает
                // освещённость на два-три тона. Карта нормалей шевелит нормаль
                // в каждом пикселе, а значит шевелит и место разреза.
                //
                // На реалистичном материале это дало бы мягкую шероховатость.
                // На ступенчатом это даёт РВАНУЮ ГРАНИЦУ ТОНА: линия раздела
                // света и тени начинает вилять и рассыпаться в крапинку по
                // каждой заклёпке. Чем жёстче ступенька, тем заметнее — а у
                // мультяшной заливки она жёсткая по определению.
                //
                // Поэтому нормалей две:
                //
                //   N     — из карты, для блика и обводки. Там рельеф читается
                //           и выглядит именно так, как задумано.
                //
                //   Nband — почти геометрическая, для ступеньки. Границы тонов
                //           остаются чистыми линиями.
                //
                // Ползунок Normal Affects Shading Band смешивает вторую с
                // первой. Ноль — идеально чистые границы; единица — обычное
                // поведение реалистичного шейдера со всеми его крапинками.
                // Малая доля даёт лёгкую неровность края, которая читается как
                // рисованная, а не как шум.
                float3 Ngeo = normalize(IN.normalWS);
                float3 N = Ngeo;
                float3 Nband = Ngeo;

                #if defined(_NORMALMAP)
                    half3 nTS = UnpackNormalScale(
                        SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);

                    float3 bitangent = IN.tangentWS.w * cross(Ngeo, IN.tangentWS.xyz);
                    float3x3 tbn = float3x3(IN.tangentWS.xyz, bitangent, Ngeo);

                    N = normalize(mul(nTS, tbn));
                    Nband = normalize(lerp(Ngeo, N, _NormalBandInfluence));
                #endif

                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float4 sc = TransformWorldToShadowCoord(IN.positionWS);
                Light main = GetMainLight(sc);

                half ndl = dot(Nband, main.direction);
                ndl = (ndl + _LightWrap) / (1.0h + _LightWrap);

                half lit = smoothstep(_ShadowThreshold - _ShadowSoftness,
                                      _ShadowThreshold + _ShadowSoftness,
                                      ndl * main.shadowAttenuation);

                half3 shade = lerp(half3(1,1,1), _ShadowColor.rgb, _ShadowStrength);
                half3 col = albedo * (lerp(shade, half3(1,1,1), lit) * main.color + IN.ambient);

                // ── БЛИК ──
                float3 H = normalize(main.direction + V);
                half spec = pow(saturate(dot(N, H)), _SpecSize);
                spec = smoothstep(0.5h - _SpecSoftness, 0.5h + _SpecSoftness, spec);
                col += _SpecColor2.rgb * spec * _SpecStrength * lit * main.color;

                // ── СВЕЧЕНИЕ ИНДИКАТОРОВ ──
                //
                // Маска берётся из альфы базовой текстуры: отдельная карта
                // ради нескольких лампочек не окупается ни памятью, ни
                // выборкой.
                half pulse = _EmissionPulse > 0.001h
                    ? 0.65h + 0.35h * sin(_Time.y * _EmissionPulse)
                    : 1.0h;

                col += _EmissionColor.rgb * tex.a * pulse;

                // ── ПОДСВЕТКА ──
                float h = UNITY_ACCESS_INSTANCED_PROP(Props, _Highlight);

                if (h > 0.001)
                {
                    half3 hCol;
                    half hAmt;
                    CC_Highlight(h, hCol, hAmt);

                    // Обводка по краю, а не заливка всей детали. Заливка
                    // съедает форму предмета и его собственный цвет — и деталь
                    // перестаёт быть узнаваемой ровно в тот момент, когда на
                    // неё смотрят.
                    half rim = pow(1.0h - saturate(dot(N, V)), _HighlightRimPower);

                    col += hCol * rim * _HighlightRimStrength * hAmt;
                    col = lerp(col, col * hCol, hAmt * 0.15h);
                }

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ==================================================================
        //  OUTLINE
        // ==================================================================
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  color      : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float nLen2 = dot(IN.normalOS, IN.normalOS);
                float3 nOS = nLen2 > 1e-8 ? IN.normalOS * rsqrt(nLen2) : float3(0,1,0);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float dist = distance(posWS, GetCameraPositionWS());

                float fadeEnd = max(_OutlineFadeEnd, _OutlineFadeStart + 0.01);
                float fade = saturate(1.0 - smoothstep(_OutlineFadeStart, fadeEnd, dist));

                // Подсвеченная деталь толстеет и меняет цвет контура. Это
                // читается даже боковым зрением, в отличие от обводки, которую
                // надо разглядывать.
                float h = UNITY_ACCESS_INSTANCED_PROP(Props, _Highlight);

                half3 hCol;
                half hAmt;
                CC_Highlight(h, hCol, hAmt);

                float width = _OutlineWidth * fade * (1.0 + _OutlineHighlightGrow * hAmt);

                OUT.color = lerp(_OutlineColor.rgb, hCol, hAmt);

                float4 posCS = TransformObjectToHClip(IN.positionOS.xyz);

                float3 nVS = TransformWorldToViewDir(TransformObjectToWorldNormal(nOS));
                float2 nxy = nVS.xy;
                float xyLen2 = dot(nxy, nxy);
                nxy = xyLen2 > 1e-8 ? nxy * rsqrt(xyLen2) : float2(0,0);

                posCS.xy += nxy * width * 2.0 / _ScreenParams.xy * posCS.w;

                float hide = (1.0 - fade) * (1.0 - hAmt) * 0.02;

                #if UNITY_REVERSED_Z
                    posCS.z -= hide * posCS.w;
                #else
                    posCS.z += hide * posCS.w;
                #endif

                OUT.positionCS = posCS;
                OUT.fogFactor = ComputeFogFactor(posCS.z);
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return half4(MixFog(IN.color, IN.fogFactor), 1);
            }
            ENDHLSL
        }

        // ==================================================================
        //  SHADOW CASTER
        // ==================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 L = normalize(_LightPosition - positionWS);
                #else
                    float3 L = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, L));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ==================================================================
        //  DEPTH ONLY
        // ==================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 DepthFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ==================================================================
        //  DEPTH NORMALS
        // ==================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DNVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DNFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
