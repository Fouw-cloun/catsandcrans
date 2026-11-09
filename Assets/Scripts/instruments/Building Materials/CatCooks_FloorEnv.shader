// ============================================================================
//  CatCooks/Floor Environment  —  окружение рабочего этажа
//  Балки, стены, пол, колонны, перекрытия. Unity 6 / URP.
// ----------------------------------------------------------------------------
//  ЧТО ЗДЕСЬ СОЗНАТЕЛЬНО ВЫБРОШЕНО И ПОЧЕМУ
//
//  ЦВЕТО-ГРЕЙДИНГ. В Toon Props его двадцать шесть операций на пиксель:
//  экспозиция, гамма, контраст, насыщенность, vibrance, сдвиг тона, баланс
//  белого, тонировка теней и светов. Всё это — работа ОДНОГО прохода
//  пост-обработки, где она делается один раз на пиксель экрана. В шейдере
//  объекта она делается на каждый пиксель КАЖДОГО объекта, то есть столько
//  раз, сколько слоёв геометрии перекрывают эту точку.
//
//  Выигрыш от переноса тем больше, чем плотнее сцена, — а этажи как раз
//  и стоят один за другим. Всё это настраивается в Volume: Color Adjustments,
//  White Balance, Shadows Midtones Highlights. Выглядит так же, стоит в
//  десятки раз меньше и правится один раз на всю игру, а не по материалам.
//
//  MATCAP, ОТРАЖЕНИЯ, GRIME, DUST, EDGE WEAR, ДВОЙНОЙ СПЕКУЛЯР. Прекрасны на
//  инструменте, который игрок держит перед лицом. На стене, мимо которой он
//  пробегает, их не видно, а платить за них приходится с каждого пикселя.
//
//  99 СВОЙСТВ В CBUFFER. Столько данных материала приходится подавать на
//  каждую отрисовку. Здесь их около двадцати.
//
//  ЧТО ОСТАВЛЕНО: ступенчатый свет, тёплая тень, мягкий блик, контур,
//  вертексный цвет и туман. Всё, из чего складывается мультяшность.
// ============================================================================
Shader "CatCooks/Floor Environment"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (1,1,1,1)

        [Tooltip(Vertex color darkens the mesh. Bake AO into it in Blender and get free corners without a texture)]
        _VertexColorTint ("Vertex Color Tint", Range(0,1)) = 0

        [Space(6)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        [Header(Toon Light)]
        [Tooltip(Where light turns into shadow)]
        _ShadowThreshold ("Shadow Edge", Range(-1,1)) = 0.05

        [Tooltip(Zero is a hard cartoon edge. Higher blurs it)]
        _ShadowSoftness ("Shadow Softness", Range(0.001,0.6)) = 0.06

        [Tooltip(Second step. Zero disables it and leaves two tone shading)]
        _MidStep ("Mid Step", Range(0,1)) = 0.55
        _MidSoftness ("Mid Softness", Range(0.001,0.6)) = 0.08

        _ShadowColor ("Shadow Color", Color) = (0.42,0.45,0.62,1)
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.75

        [Tooltip(Light bleeding around the terminator. Keeps corners from going flat black)]
        _LightWrap ("Light Wrap", Range(0,1)) = 0.25

        [Header(Ambient)]
        [Tooltip(Sky color reaching upward faces)]
        _SkyTint ("Sky Tint", Color) = (0.55,0.68,0.85,1)
        [Tooltip(Bounce color reaching downward faces)]
        _GroundTint ("Ground Tint", Color) = (0.32,0.30,0.26,1)
        _AmbientStrength ("Ambient Strength", Range(0,2)) = 1

        [Header(Specular)]
        [Toggle(_SPECULAR_ON)] _UseSpecular ("Specular", Float) = 0
        _SpecColor2 ("Specular Color", Color) = (1,1,1,1)
        _SpecSize ("Specular Size", Range(0.5,64)) = 24
        _SpecSoftness ("Specular Softness", Range(0.001,0.5)) = 0.05
        _SpecStrength ("Specular Strength", Range(0,2)) = 0.5

        [Header(Rim)]
        [Toggle(_RIM_ON)] _UseRim ("Rim Light", Float) = 0
        _RimColor ("Rim Color", Color) = (1,0.96,0.85,1)
        _RimPower ("Rim Power", Range(0.5,16)) = 4
        _RimStrength ("Rim Strength", Range(0,2)) = 0.5

        [Header(Normal Map)]
        [Toggle(_NORMALMAP)] _UseNormalMap ("Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0,3)) = 1

        [Tooltip(How much the normal map moves the toon band edge. Keep it low. See the note in the shader header)]
        _NormalBandInfluence ("Normal Affects Shading Band", Range(0,1)) = 0.15

        [Header(Outline)]
        [Tooltip(Zero still costs a draw call. Use the Filler shader on floors that need no outline)]
        _OutlineWidth ("Outline Width (px)", Range(0,8)) = 1.4
        _OutlineColor ("Outline Color", Color) = (0.11,0.09,0.12,1)
        _OutlineFadeStart ("Outline Fade Start (m)", Float) = 30
        _OutlineFadeEnd ("Outline Fade End (m)", Float) = 70

        [Header(Performance)]
        [Toggle(_RECEIVE_SHADOWS_OFF)] _NoShadows ("Do Not Receive Shadows", Float) = 0
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

        // ВСЕ свойства материала в одном CBUFFER — иначе SRP Batcher не
        // соберёт объекты в пакет, и каждый кусок этажа поедет отдельной
        // отрисовкой. На здании из сотен деталей это и есть разница между
        // «идёт» и «не идёт».
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _VertexColorTint;
            half   _Cutoff;
            half   _BumpScale;
            half   _NormalBandInfluence;

            half   _ShadowThreshold;
            half   _ShadowSoftness;
            half   _MidStep;
            half   _MidSoftness;
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

            half4  _RimColor;
            half   _RimPower;
            half   _RimStrength;

            half   _OutlineWidth;
            half4  _OutlineColor;
            float  _OutlineFadeStart;
            float  _OutlineFadeEnd;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);
        ENDHLSL

        // ==================================================================
        //  FORWARD
        // ==================================================================
        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _SPECULAR_ON
            #pragma shader_feature_local_fragment _RIM_ON
            #pragma shader_feature_local_fragment _RECEIVE_SHADOWS_OFF

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

                // ОКРУЖАЮЩИЙ СВЕТ СЧИТАЕТСЯ В ВЕРШИНЕ.
                //
                // Он меняется плавно и по нормали — на треугольнике размером
                // с балку разница между вершинным и пиксельным расчётом
                // невидима. А вершин у стены на три порядка меньше, чем
                // пикселей.
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

                // Небо сверху, отражённый свет земли снизу. Две константы
                // вместо сферических гармоник: на стилизованной картинке
                // разницы не видно, а операций втрое меньше.
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

                // ── ОСНОВНОЙ СВЕТ ──
                #if defined(_RECEIVE_SHADOWS_OFF)
                    Light main = GetMainLight();
                    half shadow = 1.0h;
                #else
                    float4 sc = TransformWorldToShadowCoord(IN.positionWS);
                    Light main = GetMainLight(sc);
                    half shadow = main.shadowAttenuation;
                #endif

                half ndl = dot(Nband, main.direction);

                // Свет заворачивается за терминатор. Без этого у мультяшной
                // заливки углы проваливаются в плоский чёрный, и вся стена
                // читается как дырка.
                ndl = (ndl + _LightWrap) / (1.0h + _LightWrap);

                half lit = smoothstep(_ShadowThreshold - _ShadowSoftness,
                                      _ShadowThreshold + _ShadowSoftness,
                                      ndl * shadow);

                // Вторая ступень. Ноль в _MidStep её выключает — тогда
                // заливка ровно двухтоновая.
                half mid = _MidStep > 0.001h
                    ? smoothstep(_MidStep - _MidSoftness, _MidStep + _MidSoftness, ndl * shadow)
                    : lit;

                half band = saturate(lit * 0.65h + mid * 0.35h);

                half3 shadeCol = lerp(half3(1,1,1), _ShadowColor.rgb, _ShadowStrength);
                half3 lighting = lerp(shadeCol, half3(1,1,1), band) * main.color;

                half3 col = albedo * (lighting + IN.ambient);

                // ── БЛИК ──
                #if defined(_SPECULAR_ON)
                    float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                    float3 H = normalize(main.direction + V);

                    half spec = pow(saturate(dot(N, H)), _SpecSize);
                    spec = smoothstep(0.5h - _SpecSoftness, 0.5h + _SpecSoftness, spec);

                    col += _SpecColor2.rgb * spec * _SpecStrength * band * main.color;
                #endif

                // ── ОБВОДКА СВЕТОМ ──
                #if defined(_RIM_ON)
                    float3 Vr = normalize(GetWorldSpaceViewDir(IN.positionWS));
                    half rim = pow(1.0h - saturate(dot(N, Vr)), _RimPower);
                    col += _RimColor.rgb * rim * _RimStrength;
                #endif

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
                half   fogFactor  : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Нулевая нормаль после normalize даёт NaN, а NaN в позиции
                // вершины растягивает треугольник на весь экран чёрным клином.
                float nLen2 = dot(IN.normalOS, IN.normalOS);
                float3 nOS = nLen2 > 1e-8 ? IN.normalOS * rsqrt(nLen2) : float3(0,1,0);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float dist = distance(posWS, GetCameraPositionWS());

                float fadeEnd = max(_OutlineFadeEnd, _OutlineFadeStart + 0.01);
                float fade = saturate(1.0 - smoothstep(_OutlineFadeStart, fadeEnd, dist));
                float width = _OutlineWidth * fade;

                float4 posCS = TransformObjectToHClip(IN.positionOS.xyz);

                float3 nVS = TransformWorldToViewDir(TransformObjectToWorldNormal(nOS));
                float2 nxy = nVS.xy;
                float xyLen2 = dot(nxy, nxy);
                nxy = xyLen2 > 1e-8 ? nxy * rsqrt(xyLen2) : float2(0,0);

                posCS.xy += nxy * width * 2.0 / _ScreenParams.xy * posCS.w;

                // Погасший контур не выбрасываем вершиной, а уводим за
                // поверхность по глубине: выброс вершины с w = -1 растягивает
                // треугольник через плоскость w = 0 на пол-экрана.
                float hide = (1.0 - fade) * 0.02;

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
                return half4(MixFog(_OutlineColor.rgb, IN.fogFactor), 1);
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
        //  DEPTH NORMALS  (нужен SSAO и контурам пост-обработки)
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
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

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
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DNVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 DNFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
