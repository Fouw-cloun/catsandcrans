// ============================================================================
//  CatCooks/Toon Foliage   —   Unity 6 (URP 17)
//
//  Крона дерева: меш из квадов, каждый разворачивается к камере вокруг
//  своего центра. Плюс ветер, toon-свет, просвет листвы и вариация
//  между деревьями без единого лишнего префаба.
//
//  ТРЕБОВАНИЯ К МЕШУ (делается в Blender):
//   • крона состоит из квадов
//   • КАЖДЫЙ квад развёрнут на весь UV 0..1 (все квады лежат друг на друге)
//   • экспорт с тангентами
//   • vertex color (необязательно, но желательно):
//       R = маска трепыхания   (1 = мягкая листва, 0 = жёсткая)
//       G = не используется
//       B = маска изгиба       (0 у ствола, 1 у макушки)
//       A = размер билборда    (1 = как задано в материале)
//     Если цвета не покрашены, всё будет 1 — тоже рабочий вариант.
//
//  ПРОИЗВОДИТЕЛЬНОСТЬ: главный расход не в вершинах, а в overdraw.
//  Для дальних LOD выключай Billboard / Translucency / Specular галками —
//  они компилируются прочь, а не просто множатся на ноль.
// ============================================================================
Shader "CatCooks/Toon Foliage"
{
    Properties
    {
        [Header(Textures)]
        _BaseMap ("Leaf cluster (RGB + Alpha)", 2D) = "white" {}
        _BaseTint ("Оттенок", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0.01, 0.9)) = 0.4

        [Header(Distance LOD)]
        // Главная причина исчезающей вдали листвы: мипмапы усредняют альфу,
        // и на дальних мипах она падает НИЖЕ порога отсечения. Поэтому
        // порог надо опускать вместе с расстоянием.
        _CutoffFar ("Alpha cutoff вдали", Range(0.005, 0.9)) = 0.10
        _CutoffFadeStart ("Cutoff падает с (м)", Float) = 35
        _CutoffFadeEnd ("Cutoff минимален на (м)", Float) = 140
        _FarMipBias ("Mip bias вдали", Range(-3, 1)) = -0.75
        _FarSimplify ("Упрощать дальше (м)", Float) = 90
        _FarThicken ("Утолщать крону вдали", Range(0, 1)) = 0.35

        [Header(Billboard)]
        [Toggle(_BILLBOARD_ON)] _BillboardOn ("Разворот к камере", Float) = 1
        _BillboardSize ("Размер квада", Float) = 1
        _BillboardAmount ("Сила разворота", Range(0,1)) = 1
        _BillboardUpLock ("Держать вертикаль", Range(0,1)) = 0.25

        [Header(Wind)]
        _WindMult ("Множитель ветра", Range(0,3)) = 1
        _BendStart ("Изгиб начинается с Y (м)", Float) = 0.5
        _TreeHeight ("Высота дерева (м)", Float) = 6
        _FlutterSpeed ("Скорость трепыхания", Range(0,12)) = 3.5
        _FlutterStrength ("Сила трепыхания", Range(0,0.5)) = 0.07
        _WindFadeStart ("Ветер слабеет с (м)", Float) = 60
        _WindFadeEnd ("Ветер выключен на (м)", Float) = 140

        [Header(Shape and Normals)]
        _Spherify ("Сферические нормали", Range(0,1)) = 0.85
        _CanopyAO ("Самозатенение кроны", Range(0,1)) = 0.75
        // Для готовых ассетов без vertex color: затенение считается процедурно
        // по расстоянию от центра кроны, а не читается из меша.
        [Toggle(_PROC_AO)] _ProcAO ("Затенение без vertex color", Float) = 0
        _CanopyRadius ("Радиус кроны (м)", Float) = 2.0
        _CanopyCenterY ("Центр кроны по Y (м)", Float) = 4

        [Header(Toon Lighting)]
        _ShadeSteps ("Ступеней света", Range(1,6)) = 3
        _ShadeFloor ("Пол тени (яркость)", Range(0,1)) = 0.55
        _LightWrap ("Заворот света", Range(0,1)) = 0.5
        _ShadowTint ("Оттенок тени", Color) = (0.55, 0.66, 0.92, 1)
        _ShadowStrength ("Сила падающих теней", Range(0,1)) = 0.55
        _AmbientBoost ("Ambient", Range(0,3)) = 1.35
        _LightBoost ("Прямой свет", Range(0,3)) = 1.1

        [Header(Translucency)]
        [Toggle(_TRANSLUCENCY_ON)] _TransOn ("Просвет листвы", Float) = 1
        _TransColor ("Цвет просвета", Color) = (0.75, 0.95, 0.45, 1)
        _TransStrength ("Сила просвета", Range(0,3)) = 1.1
        _TransPower ("Узость просвета", Range(1,16)) = 4

        [Header(Toon Specular)]
        [Toggle(_TOONSPEC_ON)] _SpecOn ("Блик", Float) = 1
        _SpecColor ("Цвет блика", Color) = (1, 0.98, 0.85, 1)
        _SpecStrength ("Сила блика", Range(0,2)) = 0.35
        _SpecSize ("Размер блика", Range(0.001, 0.5)) = 0.12

        [Header(Per Quad Variation)]
        _QuadFlip ("Зеркалить карточки", Range(0,1)) = 1
        _QuadTintVar ("Разброс тона карточек", Range(0,0.5)) = 0.16
        _QuadBrightVar ("Разброс яркости карточек", Range(0,0.5)) = 0.18

        [Header(Per Tree Variation)]
        _VarTint ("Цвет вариации", Color) = (0.85, 0.95, 0.7, 1)
        _VarAmount ("Разброс цвета", Range(0,1)) = 0.35
        _VarBrightness ("Разброс яркости", Range(0,0.6)) = 0.18

        [Header(Distance)]
        _DetailFadeStart ("Детали гаснут с (м)", Float) = 45
        _DetailFadeEnd ("Детали выключены (м)", Float) = 120
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "CatCooksWind.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseTint, _ShadowTint, _TransColor, _SpecColor, _VarTint;
            float _Cutoff;
            float _BillboardOn, _TransOn, _SpecOn;
            float _BillboardSize, _BillboardAmount, _BillboardUpLock;
            float _WindMult, _BendStart, _TreeHeight;
            float _FlutterSpeed, _FlutterStrength;
            float _WindFadeStart, _WindFadeEnd;
            float _Spherify, _CanopyCenterY, _CanopyAO;
            float _ProcAO, _CanopyRadius;
            float _ShadeSteps, _ShadeFloor, _LightWrap, _ShadowStrength;
            float _AmbientBoost, _LightBoost;
            float _TransStrength, _TransPower;
            float _SpecStrength, _SpecSize;
            float _VarAmount, _VarBrightness;
            float _QuadFlip, _QuadTintVar, _QuadBrightVar;
            float _DetailFadeStart, _DetailFadeEnd;
            float _CutoffFar, _CutoffFadeStart, _CutoffFadeEnd;
            float _FarMipBias, _FarSimplify, _FarThicken;
        CBUFFER_END

        // ------------------------------------------------------------------
        //  Порог отсечения, падающий с расстоянием.
        //  Обязан быть ОДИНАКОВЫМ во всех проходах, иначе тень и глубина
        //  разойдутся с картинкой.
        // ------------------------------------------------------------------
        float CatFoliageCutoff(float3 posWS)
        {
            float d = distance(GetCameraPositionWS(), posWS);
            float k = saturate((d - _CutoffFadeStart) /
                               max(0.001, _CutoffFadeEnd - _CutoffFadeStart));
            return lerp(_Cutoff, _CutoffFar, k);
        }

        // ------------------------------------------------------------------
        //  Общая для всех проходов подготовка вершины.
        //  billboardEnabled = 0 в shadow pass: тень считается по исходной
        //  геометрии, иначе она пляшет вслед за камерой.
        // ------------------------------------------------------------------
        void CatFoliageVertex(float3 positionOS, float3 normalOS, float4 tangentOS,
                              float2 uv, float4 vcolor, float billboardEnabled,
                              out float3 posWS, out float3 normalWS, out float treeHash,
                              out float quadHash, out float procAO)
        {
            float2 q = uv - 0.5;
            float3 nOS = normalize(normalOS);
            float3 tOS = normalize(tangentOS.xyz);
            float3 bOS = normalize(cross(nOS, tOS) * tangentOS.w);

            float size = _BillboardSize * vcolor.a;

            // центр квада восстанавливаем из UV и тангентов — ничего запекать не надо
            float3 pivotOS = positionOS - (tOS * q.x + bOS * q.y) * size;
            float3 pivotWS = TransformObjectToWorld(pivotOS);
            float3 flatWS  = TransformObjectToWorld(positionOS);
            float3 originWS = CatObjectOriginWS();

            float objScale = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));

            posWS = flatWS;

            #ifdef _BILLBOARD_ON
            {
                float3 look = normalize(GetCameraPositionWS() - pivotWS);
                float3 camUp = float3(UNITY_MATRIX_V._m10, UNITY_MATRIX_V._m11, UNITY_MATRIX_V._m12);
                float3 upRef = lerp(camUp, float3(0, 1, 0), _BillboardUpLock);

                // ЗАЩИТА ОТ ВЫРОЖДЕНИЯ.
                // Если upRef почти совпадает с look, cross даёт нулевой вектор,
                // normalize возвращает NaN, и квад схлопывается в точку —
                // карточка просто пропадает. Ловится это ровно тогда, когда
                // смотришь на дерево под определённым углом, отчего и кажется,
                // что листва исчезает «когда наводишься».
                float upLen = length(upRef);
                upRef = upLen > 1e-4 ? upRef / upLen : float3(0, 1, 0);
                if (abs(dot(upRef, look)) > 0.999)
                    upRef = abs(look.y) > 0.9 ? float3(0, 0, 1) : float3(0, 1, 0);

                float3 rightRaw = cross(upRef, look);
                float rLen = length(rightRaw);
                float3 right = rLen > 1e-5 ? rightRaw / rLen : float3(1, 0, 0);
                float3 up = cross(look, right);

                // вдали слегка раздуваем карточки: крона перестаёт «худеть»,
                // когда мелкие детали съедаются мипами
                float dFar = distance(GetCameraPositionWS(), pivotWS);
                float grow = 1.0 + _FarThicken *
                             saturate((dFar - _CutoffFadeStart) /
                                      max(0.001, _CutoffFadeEnd - _CutoffFadeStart));

                float3 billWS = pivotWS + (right * q.x + up * q.y) * size * objScale * grow;
                posWS = lerp(flatWS, billWS, _BillboardAmount * billboardEnabled);
            }
            #endif

            // ---------------- ветер ----------------
            float dist = distance(GetCameraPositionWS(), pivotWS);
            float windFade = 1.0 - saturate((dist - _WindFadeStart) /
                                            max(0.001, _WindFadeEnd - _WindFadeStart));
            windFade *= _WindMult;

            // Дальше _WindFadeEnd ветер и так нулевой — не считаем его вовсе.
            // Динамическая ветка экономит ALU на сотнях деревьев фона.
            UNITY_BRANCH if (windFade > 0.001)
            {
                float hMask = saturate((positionOS.y - _BendStart) /
                                       max(0.001, _TreeHeight - _BendStart)) * vcolor.b;
                posWS += CatWindMainBend(posWS, originWS, hMask, 1.0) * windFade;
                posWS += CatWindFlutter(pivotWS, vcolor.r, _FlutterSpeed, _FlutterStrength) * windFade;
            }

            // ---------------- нормали ----------------
            // У развёрнутого к камере квада собственная нормаль смотрит в камеру
            // и для света бесполезна. Берём направление от центра кроны наружу —
            // тогда крона читается как один объём, а не сотня плоских карточек.
            float3 centerWS = TransformObjectToWorld(float3(0, _CanopyCenterY, 0));
            float3 sphereN = normalize(posWS - centerWS + float3(0, 0.0001, 0));
            float3 geoN = TransformObjectToWorldNormal(nOS);
            normalWS = normalize(lerp(geoN, sphereN, _Spherify));

            treeHash = CatHash13(floor(originWS * 7.31));
            // хэш конкретной карточки — из её центра. Одна карточка = одно
            // стабильное случайное число, кадр в кадр одинаковое.
            quadHash = CatHash13(floor(pivotOS * 53.7) + 0.5);

            // Процедурное самозатенение: карточка тем темнее, чем ближе она
            // к центру кроны. Заменяет G-канал vertex color у чужих ассетов.
            float3 cOS = float3(0, _CanopyCenterY, 0);
            procAO = saturate(length(pivotOS - cOS) / max(0.05, _CanopyRadius));
            procAO = procAO * procAO * (3.0 - 2.0 * procAO);
        }
        ENDHLSL

        // ===================== ОСНОВНОЙ ПРОХОД =====================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #pragma shader_feature_local _BILLBOARD_ON
            #pragma shader_feature_local _PROC_AO
            #pragma shader_feature_local_fragment _TRANSLUCENCY_ON
            #pragma shader_feature_local_fragment _TOONSPEC_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float treeHash : TEXCOORD4;
                float canopyOcc : TEXCOORD5;
                float quadHash : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS, nWS; float hash, qhash, pao;
                CatFoliageVertex(IN.positionOS.xyz, IN.normalOS, IN.tangentOS,
                                 IN.uv, IN.color, 1.0, posWS, nWS, hash, qhash, pao);

                OUT.quadHash = qhash;
                OUT.positionWS = posWS;
                OUT.normalWS = nWS;
                OUT.treeHash = hash;
                // G vertex color = запечённое самозатенение: 0 внутри, 1 снаружи.
                // Если ассет без vertex color — берём процедурное.
                #ifdef _PROC_AO
                    OUT.canopyOcc = pao;
                #else
                    OUT.canopyOcc = IN.color.g;
                #endif
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif

                // Зеркалим карточку по хэшу — 4 варианта из одной текстуры.
                // Пивот уже посчитан по исходным UV, так что билборд не поедет.
                float2 uv = IN.uv;
                if (_QuadFlip > 0.5)
                {
                    if (frac(IN.quadHash * 7.13) > 0.5) uv.x = 1.0 - uv.x;
                    if (frac(IN.quadHash * 3.71) > 0.5) uv.y = 1.0 - uv.y;
                }

                // Отрицательный mip bias вдали: берём мип поменьше, альфа
                // меньше размывается, листва не «худеет».
                half4 tex = SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, uv, _FarMipBias);
                clip(tex.a - CatFoliageCutoff(IN.positionWS));

                half3 albedo = tex.rgb * _BaseTint.rgb;

                // разброс тона и яркости между отдельными карточками
                half qh = IN.quadHash;
                albedo *= 1.0 + (qh - 0.5) * 2.0 * _QuadBrightVar;
                albedo.r *= 1.0 + (frac(qh * 5.3) - 0.5) * _QuadTintVar;
                albedo.b *= 1.0 + (frac(qh * 9.7) - 0.5) * _QuadTintVar;

                // ---------- вариация между деревьями ----------
                half h = IN.treeHash;
                albedo = lerp(albedo, albedo * _VarTint.rgb * 1.6, h * _VarAmount);
                albedo *= 1.0 + (h - 0.5) * 2.0 * _VarBrightness;

                float3 posWS = IN.positionWS;
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetCameraPositionWS() - posWS);

                // двусторонний квад: нормаль всегда к зрителю
                N *= sign(dot(N, V) + 0.001);

                float dist = distance(GetCameraPositionWS(), posWS);
                half detail = 1.0 - saturate((dist - _DetailFadeStart) /
                                             max(0.001, _DetailFadeEnd - _DetailFadeStart));

                // ---------- toon-свет ----------
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);
                Light main = GetMainLight(shadowCoord, posWS, half4(1, 1, 1, 1));

                float ndl = dot(N, main.direction);
                float t = saturate(lerp(ndl, ndl * 0.5 + 0.5, _LightWrap));
                t = min(t, lerp(1.0, main.shadowAttenuation, _ShadowStrength));

                float ls = max(1.0, floor(_ShadeSteps));
                float scaled = t * ls;
                float aa = saturate(fwidth(scaled) * 0.7);
                float band = saturate((floor(scaled) + smoothstep(0.5 - aa, 0.5 + aa, frac(scaled))) / ls);

                float lit = lerp(_ShadeFloor, 1.0, band);
                half3 tint = lerp(_ShadowTint.rgb, half3(1, 1, 1), band);

                // Самозатенение кроны — то, что превращает набор карточек в объём.
                // На ambient влияет полностью, на прямой свет частично.
                half occ = lerp(1.0, IN.canopyOcc, _CanopyAO);
                half3 col = albedo * main.color * lit * tint * _LightBoost * lerp(1.0, occ, 0.6);
                col += albedo * SampleSH(N) * _AmbientBoost * occ;

                // ---------- просвет листвы ----------
                // Свет, проходящий сквозь лист насквозь. Именно он делает крону
                // «живой» на просвет со стороны солнца.
                #ifdef _TRANSLUCENCY_ON
                {
                    float back = pow(saturate(dot(-V, main.direction)), _TransPower);
                    back = smoothstep(0.05, 0.55, back);          // ступенька = toon
                    col += _TransColor.rgb * albedo * back
                         * _TransStrength * main.shadowAttenuation * detail * occ;
                }
                #endif

                // ---------- блик ----------
                #ifdef _TOONSPEC_ON
                {
                    float3 H = normalize(main.direction + V);
                    float s = pow(saturate(dot(N, H)), 32.0);
                    s = smoothstep(0.5 - _SpecSize, 0.5 + _SpecSize, s);
                    col += _SpecColor.rgb * s * _SpecStrength * band * detail;
                }
                #endif

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ===================== ТЕНЬ =====================
        //  Билбординг здесь выключен (billboardEnabled = 0):
        //  тень строится по исходной геометрии и не дёргается за камерой.
        //  Ветер применяется — иначе тень отвяжется от кроны.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local _BILLBOARD_ON
            #pragma shader_feature_local _PROC_AO
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float3 _LightDirection;

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;   // нужен для дистанционного порога
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            V ShadowVert(A IN)
            {
                V OUT = (V)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS, nWS; float hash, qhash, pao;
                CatFoliageVertex(IN.positionOS.xyz, IN.normalOS, IN.tangentOS,
                                 IN.uv, IN.color, 0.0, posWS, nWS, hash, qhash, pao);

                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = cs;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.positionWS = posWS;
                return OUT;
            }

            half4 ShadowFrag(V IN) : SV_Target
            {
                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif
                clip(SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, IN.uv, _FarMipBias).a
                     - CatFoliageCutoff(IN.positionWS));
                return 0;
            }
            ENDHLSL
        }

        // ===================== DEPTH =====================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask 0 Cull Off

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local _BILLBOARD_ON
            #pragma shader_feature_local _PROC_AO
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;   // нужен для дистанционного порога
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            V DepthVert(A IN)
            {
                V OUT = (V)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS, nWS; float hash, qhash, pao;
                CatFoliageVertex(IN.positionOS.xyz, IN.normalOS, IN.tangentOS,
                                 IN.uv, IN.color, 1.0, posWS, nWS, hash, qhash, pao);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.positionWS = posWS;
                return OUT;
            }

            half4 DepthFrag(V IN) : SV_Target
            {
                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif
                clip(SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, IN.uv, _FarMipBias).a
                     - CatFoliageCutoff(IN.positionWS));
                return 0;
            }
            ENDHLSL
        }

        // ===================== DEPTH NORMALS (для SSAO и обводки) =====================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Off

            HLSLPROGRAM
            #pragma vertex NVert
            #pragma fragment NFrag
            #pragma shader_feature_local _BILLBOARD_ON
            #pragma shader_feature_local _PROC_AO
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;   // нужен для дистанционного порога
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            V NVert(A IN)
            {
                V OUT = (V)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS, nWS; float hash, qhash, pao;
                CatFoliageVertex(IN.positionOS.xyz, IN.normalOS, IN.tangentOS,
                                 IN.uv, IN.color, 1.0, posWS, nWS, hash, qhash, pao);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = nWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.positionWS = posWS;
                return OUT;
            }

            half4 NFrag(V IN) : SV_Target
            {
                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif
                clip(SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, IN.uv, _FarMipBias).a
                     - CatFoliageCutoff(IN.positionWS));
                return half4(normalize(IN.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
