// CatCooks — завеса дождя вдали: два цилиндра вокруг камеры (64 сегмента, без меша), по ним бегут
// полосы струй с наклоном по ветру и «простыни» порывов. Дальние кварталы тонут в дожде — в шторм стена,
// в морось почти не видно. Здания перед завесой закрывают её сами (тест глубины).
Shader "CatCooks/City/Rain Curtain"
{
    Properties
    {
        _Color ("Цвет завесы (умножается на свет неба)", Color) = (0.82, 0.86, 0.92, 1)
        _StreakScale ("Ширина струй (160 — стандарт)", Range(20, 400)) = 160
        _SheetScale ("Размер порывов", Range(0.5, 12)) = 3
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "RainCurtain"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CityRain.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _StreakScale, _SheetScale;
            CBUFFER_END

            #define SEG 64

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;      // x — вокруг (0..1), y — высота (0 низ — 1 верх)
                float layer : TEXCOORD1;
                float3 view : TEXCOORD2;
                float3 world : TEXCOORD3;   // x — дуга по кольцу (ед.), y — высота мира, z — радиус
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                if (_CC_RainCurtain.x < 0.002) { o.positionCS = float4(2, 2, 2, 1); return o; }
                uint seg = vid / 6;
                float2 c = kCorner[vid % 6];
                float a = (seg + c.x) / SEG * 6.2831853;
                float3 cam = GetCameraPositionWS();
                float R = _CC_RainCurtain.y * (iid == 0 ? 1.0 : 1.7);
                float H = _CC_RainCurtain.z;
                float y0 = cam.y - H * 0.55, y1 = cam.y + H * 0.45;
                float3 wp = float3(cam.x + cos(a) * R, lerp(y0, y1, c.y), cam.z + sin(a) * R);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = float2((seg + c.x) / SEG, c.y);
                o.layer = iid;
                o.view = normalize(cam - wp);
                o.world = float3(a * R, wp.y, R);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float lay = i.layer;
                float m = _CC_RainMeter;
                // струи движутся ровно с той же скоростью и наклоном, что и капли (общее накопленное падение)
                float ang = i.uv.x * 6.2831853;
                float2 tang = float2(-sin(ang), cos(ang));
                float slant = dot(_CC_RainWind.xz, tang) / max(_CC_RainP0.y, 1e-3);
                float vW = i.world.y - _CC_RainFall.y;
                float uW = i.world.x + vW * slant;
                float stripeW = (0.35 + lay * 0.4) * m * (_StreakScale / 160.0) ;
                float stripeL = max(_CC_RainCurtain.w, 0.1 * m) * 5.0;
                float2 q = float2(uW / stripeW, vW / stripeL);
                float s = CR_Noise(float2(q.x, q.y * 0.6)) * 0.6 + CR_Noise(float2(q.x * 2.3 + 7.0, q.y * 1.1)) * 0.4;
                float streak = smoothstep(0.52, 0.78, s);
                // «простыни» порывов: крупные пятна плотности ползут по ветру
                float sheetS = 30.0 * m * _SheetScale;
                float drift = dot(_CC_RainWind.xz, tang) * t;
                float sheet = CR_Noise(float2((i.world.x - drift) / sheetS, (i.world.y - _CC_RainFall.y * 0.15) / (sheetS * 1.5))) * 0.65
                            + CR_Noise(float2((i.world.x - drift) / (sheetS * 0.45) + 3.0, (i.world.y - _CC_RainFall.y * 0.2) / (sheetS * 0.7))) * 0.35;
                sheet = smoothstep(0.3, 0.85, sheet);
                float haze = 0.35 + 0.65 * sheet;
                float vert = smoothstep(0.0, 0.25, i.uv.y) * (1.0 - smoothstep(0.7, 1.0, i.uv.y));
                float a = _CC_RainCurtain.x * vert * (haze * 0.45 + streak * sheet * 0.35) * (lay > 0.5 ? 0.8 : 0.6);
                float3 col = CR_Light(i.view) * _Color.rgb;
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
