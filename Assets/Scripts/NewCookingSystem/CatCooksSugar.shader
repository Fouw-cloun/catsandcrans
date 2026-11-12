// ============================================================================
//  CatCooks / Sugar   (v19)
//
//  САХАР (и соль): белые кристаллики с лёгким блеском, мультяшно.
//    • мягкая ступенька света, голубоватая тень;
//    • ЗЁРНА: мелкие «ячейки» в мировых координатах, у каждой своя грань — где грань ловит свет,
//      вспыхивает искорка (мерцает, когда смотришь под разными углами);
//    • щели между зёрнами чуть темнее — видно, что это россыпь, а не гладкий снег.
//  Цвет — из цвета вершин (цвет продукта). Работает и на кучке-меше (кубики), и на поверхности в таре.
// ============================================================================
Shader "CatCooks/Sugar"
{
    Properties
    {
        _GrainScale ("Зёрна: на метр", Float) = 140
        _Sparkle ("Искорки: сила", Range(0, 3)) = 1.2
        _SparkleSharp ("Искорки: острота", Range(20, 600)) = 220
        _SparkleAmount ("Искорки: сколько зёрен блестит", Range(0, 1)) = 0.35
        _Crevice ("Тёмные щели между зёрнами", Range(0, 1)) = 0.3
        _ShadowTint ("Цвет тени", Color) = (0.72, 0.78, 0.9, 1)
        _Ambient ("Окружение", Range(0, 2)) = 0.9
        _Sheen ("Общий лёгкий блеск", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }
        Cull Back

        Pass
        {
            Name "Sugar"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _GrainScale, _SparkleSharp;
                half _Sparkle, _SparkleAmount, _Crevice, _Ambient, _Sheen;
                half4 _ShadowTint;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half4 color : COLOR; };

            float3 hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half3 alb = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    alb = SRGBToLinear(alb);
                #endif
                float3 n = normalize(i.normalWS);
                float3 p = i.positionWS * _GrainScale;
                // ближайшее зерно (3D ячейки) и расстояние до границы — щели
                float3 ip = floor(p), fp = frac(p);
                float d1 = 8, d2 = 8; float3 id = 0;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 o = float3(x, y, z);
                    float3 r = o + hash33(ip + o) - fp;
                    float d = dot(r, r);
                    if (d < d1) { d2 = d1; d1 = d; id = ip + o; }
                    else if (d < d2) d2 = d;
                }
                float edge = saturate((sqrt(d2) - sqrt(d1)) * 3.0);        // 0 — на границе зёрен
                float3 rnd = hash33(id * 1.37 + 5.1);
                alb *= lerp(1.0 - _Crevice * 0.5, 1.0, edge);
                alb *= 0.94 + rnd.x * 0.1;

                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, L.direction);
                half lit = smoothstep(-0.05, 0.4, ndl) * L.shadowAttenuation;
                half3 shade = lerp(_ShadowTint.rgb * 0.62, 1.0, lit);
                half3 col = alb * (L.color * shade + SampleSH(n) * _Ambient * 0.5);

                // искорки: у каждого зерна своя наклонённая грань
                float3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 fn = normalize(n + (rnd - 0.5) * 1.6);
                float3 h = normalize(L.direction + vdir);
                half sp = pow(saturate(dot(fn, h)), _SparkleSharp);
                half on = step(1.0 - _SparkleAmount, rnd.y);
                col += L.color * sp * _Sparkle * on * L.shadowAttenuation * edge;
                // общий лёгкий блеск
                half gl = pow(saturate(dot(n, h)), 24.0) * _Sheen * lit;
                col += L.color * gl * 0.5;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
