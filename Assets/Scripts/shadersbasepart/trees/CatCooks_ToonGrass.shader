// ============================================================================
//  CatCooks/Toon Grass   —   Unity 6 (URP 17)
//
//  Трава, цветы и мелкий декор на карточках (cross-quads).
//
//  Отличие от Toon Foliage: здесь НЕТ билборда. Карточки стоят как поставлены,
//  а ветер гнёт их ОТ КОРНЯ — низ прибит к земле, макушка гуляет.
//  Маска изгиба берётся из UV.v, поэтому меш может быть любым.
//
//  ТЕКСТУРА: растение растёт снизу вверх, v=0 корень, v=1 макушка.
//
//  Ветер общий с деревьями (CatCooksWind.hlsl + CatCooksWindController).
// ============================================================================
Shader "CatCooks/Toon Grass"
{
    Properties
    {
        [Header(Textures)]
        _BaseMap ("Base (RGB + Alpha)", 2D) = "white" {}
        _BaseTint ("Оттенок", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0.01,0.9)) = 0.4
        [Toggle(_NORMALMAP_ON)] _NormalOn ("Карта нормалей", Float) = 1
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Сила нормалей", Range(0,3)) = 1.0

        [Header(Ground Patch)]
        [Toggle(_GROUND_PATCH)] _GroundPatch ("Пятно на земле", Float) = 0
        _PatchEdge ("Мягкость края", Range(0.01,1)) = 0.35
        _PatchWave ("Волна по пятну", Range(0,0.4)) = 0.06
        _PatchWaveScale ("Частота волны", Range(0.1,6)) = 1.6

        [Header(Ground Blend)]
        _RootColor ("Цвет у корня", Color) = (0.28, 0.34, 0.18, 1)
        _RootBlend ("Втопить корень в землю", Range(0,1)) = 0.45
        _TipBright ("Подсветить макушку", Range(0,1)) = 0.25

        [Header(Wind)]
        // ВАЖНО: у мешей с атласной развёрткой UV.y НЕ отражает высоту растения,
        // поэтому маска изгиба берётся из vertex color. Галку включай только
        // для чужих ассетов без vertex color, где UV идут вдоль растения.
        [Toggle(_BEND_FROM_UV)] _BendFromUV ("Маска изгиба из UV.y", Float) = 0
        _WindMult ("Множитель ветра", Range(0,4)) = 1.4
        _BendPower ("Кривизна изгиба", Range(1,4)) = 2
        _FlutterSpeed ("Скорость дрожания", Range(0,14)) = 5
        _FlutterStrength ("Сила дрожания", Range(0,0.4)) = 0.045
        _WindFadeStart ("Ветер слабеет с (м)", Float) = 35
        _WindFadeEnd ("Ветер выключен (м)", Float) = 80

        [Header(Flower Highlight)]
        // Маска цветка живёт в ЗЕЛЁНОМ канале vertex color: 1 у лепестков
        // и серединки, 0 у травы, листьев и стеблей. Позволяет держать
        // соцветие ярким, пока зелень честно уходит в тень.
        [Toggle(_FLOWER_BOOST)] _FlowerBoostOn ("Выделять цветок", Float) = 1
        _FlowerBright ("Яркость цветка", Range(1,3)) = 1.45
        _FlowerSat ("Насыщенность цветка", Range(0,2.5)) = 1.35
        _FlowerShadowLift ("Цветок не темнеет в тени", Range(0,1)) = 0.6
        _FlowerRim ("Контур цветка", Range(0,2)) = 0.5
        _FlowerRimColor ("Цвет контура", Color) = (1, 0.97, 0.85, 1)

        [Header(Toon Lighting)]
        _ShadeSteps ("Ступеней света", Range(1,6)) = 3
        _ShadeFloor ("Пол тени (яркость)", Range(0,1)) = 0.58
        _LightWrap ("Заворот света", Range(0,1)) = 0.55
        _ShadowTint ("Оттенок тени", Color) = (0.55, 0.66, 0.92, 1)
        _ShadowStrength ("Сила падающих теней", Range(0,1)) = 0.5
        _AmbientBoost ("Ambient", Range(0,3)) = 1.35
        _LightBoost ("Прямой свет", Range(0,3)) = 1.1
        _NormalUp ("Нормаль вверх (объём)", Range(0,1)) = 0.7

        [Header(Translucency)]
        [Toggle(_TRANSLUCENCY_ON)] _TransOn ("Просвет", Float) = 1
        _TransColor ("Цвет просвета", Color) = (0.78, 0.95, 0.42, 1)
        _TransStrength ("Сила просвета", Range(0,3)) = 1.2
        _TransPower ("Узость просвета", Range(1,16)) = 3

        [Header(Per Instance Variation)]
        _VarTint ("Цвет вариации", Color) = (0.9, 1.0, 0.72, 1)
        _VarAmount ("Разброс цвета", Range(0,1)) = 0.35
        _VarBrightness ("Разброс яркости", Range(0,0.6)) = 0.22
        _VarScale ("Разброс высоты", Range(0,0.5)) = 0.18

        [Header(Distance)]
        [Toggle(_DISTANCE_SHRINK)] _DistShrink ("Уменьшать вдали", Float) = 1
        _FadeStart ("Начать исчезать (м)", Float) = 40
        _FadeEnd ("Полностью исчезла (м)", Float) = 70
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
            float4 _BaseTint, _RootColor, _ShadowTint, _TransColor, _VarTint;
            float _Cutoff, _RootBlend, _TipBright;
            float4 _BumpMap_ST;
            float _NormalOn, _BumpScale;
            float _GroundPatch, _PatchEdge, _PatchWave, _PatchWaveScale;
            float _BendFromUV;
            float _WindMult, _BendPower, _FlutterSpeed, _FlutterStrength;
            float _WindFadeStart, _WindFadeEnd;
            float _ShadeSteps, _ShadeFloor, _LightWrap, _ShadowStrength;
            float _AmbientBoost, _LightBoost, _NormalUp;
            float _TransOn, _TransStrength, _TransPower;
            float _VarAmount, _VarBrightness, _VarScale;
            float _FlowerBoostOn, _FlowerBright, _FlowerSat;
            float _FlowerShadowLift, _FlowerRim;
            float4 _FlowerRimColor;
            float _FadeStart, _FadeEnd, _DistShrink;
        CBUFFER_END

        void CatGrassVertex(float3 positionOS, float3 normalOS, float2 uv, float4 vcolor,
                            out float3 posWS, out float3 normalWS,
                            out float instHash, out float bendMask, out float edgeA,
                out float petalMask)
        {
            float3 originWS = CatObjectOriginWS();
            instHash = CatHash13(floor(originWS * 11.7));

            // vertex color B = маска изгиба вдоль растения: 0 у корня, 1 на кончике.
            // Она непрерывна между сегментами, в отличие от атласных UV.
            #ifdef _BEND_FROM_UV
                bendMask = saturate(uv.y);
            #else
                bendMask = saturate(vcolor.b);
            #endif
            edgeA = vcolor.a;
            petalMask = vcolor.g;

            float3 p = positionOS;
            p.y *= 1.0 + (instHash - 0.5) * 2.0 * _VarScale;

            posWS = TransformObjectToWorld(p);
            float dist = distance(GetCameraPositionWS(), originWS);

            // вдали трава оседает в землю вместо резкого исчезновения
            #ifdef _DISTANCE_SHRINK
            {
                float shrink = 1.0 - saturate((dist - _FadeStart) /
                                              max(0.001, _FadeEnd - _FadeStart));
                float3 rootWS = TransformObjectToWorld(float3(p.x, 0, p.z));
                posWS = lerp(rootWS, posWS, shrink);
            }
            #endif

            float wf = 1.0 - saturate((dist - _WindFadeStart) /
                                      max(0.001, _WindFadeEnd - _WindFadeStart));
            wf *= _WindMult;

            float m = pow(bendMask, _BendPower);

            // ПЯТНО НА ЗЕМЛЕ: вместо изгиба вбок — бегущая волна вверх-вниз,
            // как будто по цветам проходит порыв ветра
            #ifdef _GROUND_PATCH
            {
                float3 w = normalize(_CatWindDir.xyz + float3(0.0001, 0, 0));
                float ph = dot(posWS.xz, w.xz) * _PatchWaveScale
                         - _Time.y * _CatWindSpeed * 1.6;
                float wave = sin(ph) * 0.6 + sin(ph * 1.9 + 1.1) * 0.4;
                posWS.y += wave * _PatchWave * _CatWindStrength * wf * vcolor.a;
                posWS.xz += w.xz * wave * _PatchWave * 0.5 * _CatWindStrength * wf * vcolor.a;
            }
            #endif

            posWS += CatWindMainBend(posWS, originWS, m, 1.0) * wf;
            posWS += CatWindFlutter(posWS, vcolor.r * bendMask,
                                    _FlutterSpeed, _FlutterStrength) * wf;

            // Нормаль тянем вверх: у плоской карточки собственная нормаль даёт
            // жёсткое двухцветное освещение, а трава должна читаться как масса.
            float3 geoN = normalize(TransformObjectToWorldNormal(normalOS));
            normalWS = normalize(lerp(geoN, float3(0, 1, 0), _NormalUp));
        }
        ENDHLSL

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
            #pragma shader_feature_local _DISTANCE_SHRINK
            #pragma shader_feature_local _GROUND_PATCH
            #pragma shader_feature_local _BEND_FROM_UV
            #pragma shader_feature_local_fragment _TRANSLUCENCY_ON
            #pragma shader_feature_local_fragment _NORMALMAP_ON
            #pragma shader_feature_local_fragment _FLOWER_BOOST
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

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
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                float hash : TEXCOORD5;
                float bend : TEXCOORD6;
                float edge : TEXCOORD7;
                float petal : TEXCOORD8;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            V Vert(A IN)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, o);
                float3 pw, nw; float h, bm, ea, pm;
                CatGrassVertex(IN.positionOS.xyz, IN.normalOS, IN.uv, IN.color, pw, nw, h, bm, ea, pm);
                o.positionWS = pw; o.normalWS = nw; o.hash = h; o.bend = bm; o.edge = ea; o.petal = pm;
                VertexNormalInputs vn = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                o.tangentWS = float4(vn.tangentWS, IN.tangentOS.w);
                o.positionCS = TransformWorldToHClip(pw);
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(V IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);

                // пятно на земле растворяется по краю через vertex alpha
                half aCut = _Cutoff;
                #ifdef _GROUND_PATCH
                    aCut = lerp(0.95, _Cutoff, smoothstep(0.0, _PatchEdge, IN.edge));
                #endif
                clip(tex.a - aCut);

                half3 albedo = tex.rgb * _BaseTint.rgb;

                half h = IN.hash;
                albedo = lerp(albedo, albedo * _VarTint.rgb * 1.5, h * _VarAmount);
                albedo *= 1.0 + (h - 0.5) * 2.0 * _VarBrightness;

                // корень уходит в цвет земли, макушка светлее -> трава растёт
                // из земли, а не лежит на ней наклейкой.
                // На соцветие это не распространяем — оно не должно грязниться.
                half rootMix = saturate(IN.bend / max(0.01, _RootBlend));
                #ifdef _FLOWER_BOOST
                    rootMix = max(rootMix, saturate(IN.petal));
                #endif
                albedo = lerp(albedo * _RootColor.rgb * 2.0, albedo, rootMix);
                albedo *= 1.0 + saturate(IN.bend - 0.6) * _TipBright;

                #ifdef _FLOWER_BOOST
                {
                    half pm = saturate(IN.petal);
                    half lum = dot(albedo, half3(0.2126, 0.7152, 0.0722));
                    // насыщеннее и ярче ровно там, где лепестки
                    albedo = lerp(albedo, lerp(lum.xxx, albedo, _FlowerSat)
                                          * _FlowerBright, pm);
                }
                #endif

                float3 posWS = IN.positionWS;
                float3 N = normalize(IN.normalWS);

                // Карта нормалей поверх «вертикальной» нормали: мелкий рельеф
                // травинок появляется, но общий объём массы сохраняется.
                #ifdef _NORMALMAP_ON
                {
                    half3 nTS = UnpackNormalScale(
                        SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                    float3 Tw = normalize(IN.tangentWS.xyz);
                    float3 Bw = normalize(cross(normalize(IN.normalWS), Tw) * IN.tangentWS.w);
                    float3 mapped = normalize(nTS.x * Tw + nTS.y * Bw + nTS.z * N);
                    N = normalize(lerp(N, mapped, 0.75));
                }
                #endif

                float3 V_ = normalize(GetCameraPositionWS() - posWS);
                N *= sign(dot(N, V_) + 0.001);

                float4 sc = TransformWorldToShadowCoord(posWS);
                Light main = GetMainLight(sc, posWS, half4(1,1,1,1));

                float ndl = dot(N, main.direction);
                float t = saturate(lerp(ndl, ndl * 0.5 + 0.5, _LightWrap));
                t = min(t, lerp(1.0, main.shadowAttenuation, _ShadowStrength));

                float ls = max(1.0, floor(_ShadeSteps));
                float s2 = t * ls;
                float aa = saturate(fwidth(s2) * 0.7);
                float band = saturate((floor(s2) + smoothstep(0.5 - aa, 0.5 + aa, frac(s2))) / ls);

                float lit = lerp(_ShadeFloor, 1.0, band);
                half3 tint = lerp(_ShadowTint.rgb, half3(1,1,1), band);

                #ifdef _FLOWER_BOOST
                {
                    half pm = saturate(IN.petal);
                    lit  = lerp(lit, lerp(lit, 1.0, _FlowerShadowLift), pm);
                    tint = lerp(tint, lerp(tint, half3(1,1,1), _FlowerShadowLift), pm);
                }
                #endif

                half3 col = albedo * main.color * lit * tint * _LightBoost;
                col += albedo * SampleSH(N) * _AmbientBoost;

                #ifdef _TRANSLUCENCY_ON
                {
                    float back = pow(saturate(dot(-V_, main.direction)), _TransPower);
                    back = smoothstep(0.05, 0.5, back);
                    col += _TransColor.rgb * albedo * back * _TransStrength
                         * main.shadowAttenuation * saturate(IN.bend + 0.25);
                }
                #endif

                #ifdef _FLOWER_BOOST
                {
                    // лёгкий контровой по краю лепестка — соцветие
                    // отделяется от зелени даже на пёстром фоне
                    half rim = pow(saturate(1.0 - saturate(dot(N, V_))), 2.5);
                    col += _FlowerRimColor.rgb * rim * _FlowerRim * saturate(IN.petal);
                }
                #endif

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex SV
            #pragma fragment SF
            #pragma shader_feature_local _DISTANCE_SHRINK
            #pragma shader_feature_local _GROUND_PATCH
            #pragma shader_feature_local _BEND_FROM_UV
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float3 _LightDirection;
            struct A
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0; float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V SV(A IN)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 pw, nw; float h, bm, ea, pm;
                CatGrassVertex(IN.positionOS.xyz, IN.normalOS, IN.uv, IN.color, pw, nw, h, bm, ea, pm);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return o;
            }
            half4 SF(V IN) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex DV
            #pragma fragment DF
            #pragma shader_feature_local _DISTANCE_SHRINK
            #pragma shader_feature_local _GROUND_PATCH
            #pragma shader_feature_local _BEND_FROM_UV
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct A
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0; float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V DV(A IN)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 pw, nw; float h, bm, ea, pm;
                CatGrassVertex(IN.positionOS.xyz, IN.normalOS, IN.uv, IN.color, pw, nw, h, bm, ea, pm);
                o.positionCS = TransformWorldToHClip(pw);
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return o;
            }
            half4 DF(V IN) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex NV
            #pragma fragment NF
            #pragma shader_feature_local _DISTANCE_SHRINK
            #pragma shader_feature_local _GROUND_PATCH
            #pragma shader_feature_local _BEND_FROM_UV
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct A
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0; float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };
            V NV(A IN)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 pw, nw; float h, bm, ea, pm;
                CatGrassVertex(IN.positionOS.xyz, IN.normalOS, IN.uv, IN.color, pw, nw, h, bm, ea, pm);
                o.positionCS = TransformWorldToHClip(pw);
                o.normalWS = nw;
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return o;
            }
            half4 NF(V IN) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a - _Cutoff);
                return half4(normalize(IN.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
