// ============================================================================
//  CatCooks/Floor Filler  —  холостые этажи
//  Стекло и перекрытия между подъездом и двумя рабочими этажами. URP.
// ----------------------------------------------------------------------------
//  ЗАЧЕМ ОТДЕЛЬНЫЙ ШЕЙДЕР
//
//  Холостых этажей МНОГО, и их количество растёт всю партию. Игрок при этом
//  почти никогда на них не смотрит: он либо внизу у подъезда, либо наверху на
//  рабочих этажах, а холостые — это то, что между. Их видно снаружи, издали и
//  под острым углом.
//
//  Значит платить за них надо соответственно. Здесь нет ни одной вещи,
//  которую нельзя разглядеть с сорока метров.
//
//  ЧЕГО ЗДЕСЬ НЕТ И СКОЛЬКО ЭТО ЭКОНОМИТ
//
//  ПАССА ТЕНЕЙ. Самое крупное. Каждый этаж переставал бы быть одним объектом
//  и становился двумя: один раз в карту теней, второй на экран. Двадцать
//  этажей — сорок проходов вместо двадцати. А тень от двадцатого этажа падает
//  на девятнадцатый, которого всё равно не видно.
//
//  ПАССА КОНТУРА. Ещё одно удвоение, теперь ещё и с вывернутой оболочкой.
//  Контур на этаже, занимающем на экране двадцать пикселей, — это линия
//  толщиной больше самого этажа.
//
//  ПАССА DEPTH NORMALS. Нужен только SSAO. Затенение полостей на гладкой
//  стеклянной коробке не даёт ничего.
//
//  ПОПИКСЕЛЬНОГО СВЕТА. Свет считается в ВЕРШИНЕ и размазывается по
//  треугольнику. На панели два на три метра это неотличимо, а стоит на три
//  порядка меньше: вершин у неё восемь, пикселей — тысячи.
//
//  ЧТО ОСТАЛОСЬ: цвет, ступенчатая заливка, блик неба по вертикали, туман.
//  Издали этого достаточно, чтобы этаж читался как стеклянный.
// ============================================================================
Shader "CatCooks/Floor Filler"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (0.78,0.88,0.96,1)

        [Header(Cheap Toon Light)]
        [Tooltip(Direction of the fake sun. No cost since nothing is sampled)]
        _ShadowThreshold ("Shadow Edge", Range(-1,1)) = 0.0
        _ShadowSoftness ("Shadow Softness", Range(0.001,0.8)) = 0.15
        _ShadowColor ("Shadow Color", Color) = (0.45,0.52,0.68,1)
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.6

        [Header(Fake Glass)]
        [Tooltip(Sky reflection faked by vertical gradient. Costs two operations)]
        _SkyTop ("Sky Top", Color) = (0.72,0.86,1,1)
        _SkyBottom ("Sky Bottom", Color) = (0.9,0.92,0.88,1)
        _SkyStrength ("Sky Strength", Range(0,2)) = 0.7

        [Tooltip(Grazing angles go brighter. This is what reads as glass from far away)]
        _FresnelPower ("Fresnel Power", Range(0.5,8)) = 3
        _FresnelStrength ("Fresnel Strength", Range(0,2)) = 0.6

        [Header(Depth Fade)]
        [Tooltip(Floors fade toward the sky tint with distance. Hides the repetition of identical floors)]
        _HazeStart ("Haze Start (m)", Float) = 60
        _HazeEnd ("Haze End (m)", Float) = 220
        _HazeColor ("Haze Color", Color) = (0.75,0.83,0.92,1)

        [Header(Setup)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "FillerForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            // Ни одной ветки компиляции сверх необходимого. Каждый
            // multi_compile умножает число вариантов, а варианты — это время
            // сборки, размер билда и заминки при первом появлении объекта.
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;

                half   _ShadowThreshold;
                half   _ShadowSoftness;
                half4  _ShadowColor;
                half   _ShadowStrength;

                half4  _SkyTop;
                half4  _SkyBottom;
                half   _SkyStrength;

                half   _FresnelPower;
                half   _FresnelStrength;

                float  _HazeStart;
                float  _HazeEnd;
                half4  _HazeColor;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

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

                // ВЕСЬ СВЕТ УЖЕ ПОСЧИТАН И ЛЕЖИТ ЗДЕСЬ.
                //
                // В пиксель приходит готовый цвет — ему остаётся умножить его
                // на текстуру. Это и есть главная экономия: работа перенесена
                // с тысяч пикселей на восемь вершин панели.
                half3  lighting   : TEXCOORD1;

                half   haze       : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
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
                float3 N = normalize(TransformObjectToWorldNormal(IN.normalOS));

                OUT.positionCS = pos.positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                // ── ЗАЛИВКА ──
                //
                // Тень берётся без обращения к карте теней: холостой этаж
                // всё равно закрыт соседними, и настоящая тень на нём — это
                // почти всегда просто «сторона, отвёрнутая от солнца».
                Light main = GetMainLight();

                half ndl = dot(N, main.direction);
                half lit = smoothstep(_ShadowThreshold - _ShadowSoftness,
                                      _ShadowThreshold + _ShadowSoftness, ndl);

                half3 shade = lerp(half3(1,1,1), _ShadowColor.rgb, _ShadowStrength);
                half3 col = lerp(shade, half3(1,1,1), lit) * main.color;

                // ── ПОДДЕЛЬНОЕ СТЕКЛО ──
                //
                // Ни отражений, ни кубмапы, ни чтения экрана. Вертикальный
                // градиент плюс френель — этого хватает, чтобы издали панель
                // читалась как стеклянная, и стоит это четыре операции.
                half up = N.y * 0.5h + 0.5h;
                col += lerp(_SkyBottom.rgb, _SkyTop.rgb, up) * _SkyStrength;

                float3 V = normalize(GetWorldSpaceViewDir(pos.positionWS));
                half fres = pow(1.0h - saturate(dot(N, V)), _FresnelPower);
                col += fres * _FresnelStrength;

                OUT.lighting = col;

                // ── ДЫМКА ──
                //
                // Отдельно от тумана: туман настроен на всю сцену, а
                // одинаковые этажи надо гасить раньше и сильнее. Иначе
                // повторяемость двадцати одинаковых коробок бьёт в глаза.
                float dist = distance(pos.positionWS, GetCameraPositionWS());
                float hazeEnd = max(_HazeEnd, _HazeStart + 0.01);
                OUT.haze = saturate(smoothstep(_HazeStart, hazeEnd, dist));

                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // Весь фрагментный шейдер — одна выборка и три умножения.
                half3 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb
                          * _BaseColor.rgb * IN.lighting;

                col = lerp(col, _HazeColor.rgb, IN.haze);
                col = MixFog(col, IN.fogFactor);

                return half4(col, 1);
            }
            ENDHLSL
        }

        // ==================================================================
        //  DEPTH ONLY
        //
        //  Единственный дополнительный пасс, который оставлен. Он нужен
        //  глубине сцены: без него холостые этажи выпадут из тумана по
        //  глубине, из фокуса и из мягкого затенения, и станут заметнее, чем
        //  надо. Стоит он при этом почти ничего — ни света, ни текстур.
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
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack Off
}
