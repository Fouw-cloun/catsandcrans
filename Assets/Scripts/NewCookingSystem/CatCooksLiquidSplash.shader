// ============================================================================
//  CatCooks/Liquid Splash — корона-всплеск и пенное пятно в месте падения струи
// ----------------------------------------------------------------------------
//  Приём «аниме-всплеска»: кольцо-меш растёт и рассыпается ячеистым шумом
//  (верх рвётся первым), пенное пятно — концентрические кольца пены и закрутка.
//   _Mode 0 — корона: uv.x — обхват, uv.y — высота 0..1
//   _Mode 1 — пятно:  uv.x — угол 0..1,  uv.y — радиус 0..1
//   _Age 0..1 — возраст (MaterialPropertyBlock), 1 — исчезло
// ============================================================================
Shader "CatCooks/Liquid Splash"
{
    Properties
    {
        _Deep ("Тёмный", Color) = (0.10, 0.45, 0.55, 1)
        _Mid ("Основной", Color) = (0.25, 0.70, 0.78, 1)
        _Light ("Светлый", Color) = (0.65, 0.92, 0.95, 1)
        _FoamColor ("Пена", Color) = (0.97, 1, 1, 1)
        _Opacity ("Непрозрачность", Range(0, 1)) = 0.95
        _Age ("Возраст (скрипт)", Range(0, 1)) = 0
        _Mode ("0 — корона, 1 — пятно", Float) = 0
        _Seed ("Случайность", Float) = 0
        _Spin ("Закрутка пятна", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Splash"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Mid, _Light, _FoamColor;
                half _Opacity, _Age, _Mode, _Seed, _Spin;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 nWS : TEXCOORD1; half fog : TEXCOORD2; };

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float2 hash22(float2 p) { float n = hash21(p); return float2(n, hash21(p + n + 17.17)); }
            // ячейки с периодом по x (бесшовно по кругу)
            float cellsWrap(float2 p, float period)
            {
                float2 i = floor(p), f = frac(p);
                float d = 8;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 c = i + g; c.x = fmod(c.x + period * 64, period);
                    float2 o = hash22(c + _Seed) * 0.8 + 0.1;
                    d = min(d, length(g + o - f));
                }
                return d;
            }

            V vert(A v)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = v.uv;
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float t = _Time.y;
                half3 col;
                half a = _Opacity;
                if (_Mode < 0.5)
                {
                    // ── корона ──
                    float h = i.uv.y;
                    float c = cellsWrap(float2(i.uv.x * 9, h * 3 - _Age * 2), 9);
                    // рассыпается сверху вниз по мере возраста: ячейки «съедаются»
                    // видно, где ячейка «толще» порога; порог растёт с возрастом и к верху
                    clip(c - (_Age * 0.9 + h * 0.35 - 0.1));
                    col = lerp(_Mid.rgb, _Light.rgb, step(0.35, h));
                    col = lerp(col, _FoamColor.rgb, step(0.62, h + c * 0.3));
                    col = lerp(_Deep.rgb, col, step(0.12, h));
                }
                else
                {
                    // ── пенное пятно ──
                    float r = i.uv.y;
                    float ang = i.uv.x + r * 0.35 * _Spin + t * 0.05 * _Spin;
                    float c = cellsWrap(float2(ang * 14, r * 5 - t * 0.8), 14);
                    float ringsF = frac(r * 3.2 - t * 0.9 + c * 0.35);
                    half foam = step(0.62, ringsF) * step(r, 0.92);
                    half core = step(r, 0.28 + c * 0.12);
                    col = lerp(_Light.rgb, _FoamColor.rgb, saturate(foam + core));
                    a *= saturate(foam + core + 0.001) * (1 - smoothstep(0.8, 1.0, r));
                    clip(a - 0.3 - _Age * 0.7);
                    a = saturate(a + 0.3);
                }
                Light L = GetMainLight();
                col *= lerp(half3(1, 1, 1), L.color, 0.5);
                col = MixFog(col, i.fog);
                return half4(col, a * (1 - _Age * 0.3));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
