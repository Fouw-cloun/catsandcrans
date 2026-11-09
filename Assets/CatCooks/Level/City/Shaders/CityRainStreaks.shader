// CatCooks — струи дождя. Без меша и без частиц: CityRain рисует N экземпляров по 6 вершин
// (Graphics.RenderPrimitives), вершинный шейдер сам ставит каждую каплю: место — хэш номера,
// падение и снос ветром — по времени, коробка капель «приклеена» к миру и заворачивается вокруг камеры
// (капли не едут вместе с камерой). Сила дождя = доля видимых капель — меняется одним числом.
// Под навесами зоны (карта укрытий) капли гаснут. Блеск против солнца — «грибной» дождь.
Shader "CatCooks/City/Rain Streaks"
{
    Properties
    {
        _Soft ("Мягкость кончика", Range(0.05, 1)) = 0.55
        _NearFade ("Гасить у камеры, м", Range(0, 3)) = 0.8
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "RainStreaks"
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
                float _Soft, _NearFade;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;          // x поперёк, y вдоль (0 хвост — 1 голова)
                float4 info : TEXCOORD1;        // x прозрачность, y блеск, z мерцание, w случайное
                float3 view : TEXCOORD2;
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                float3 s = CR_Hash31(iid + 0.5);
                float r4 = CR_Hash11(iid * 1.37 + 3.1);
                // сила дождя: видна только доля капель (у каждой свой порог)
                if (r4 > _CC_RainP0.x) { o.positionCS = float4(2, 2, 2, 1); return o; }

                float3 cam = GetCameraPositionWS();
                float3 B = float3(_CC_RainP1.z, _CC_RainP1.w, _CC_RainP1.z);
                // падение — накопленное в C# (смена скорости и ветра не дёргает капли), у каждой капли свой темп
                float t = _Time.y;
                float spd = 0.85 + 0.3 * s.y;
                float3 vel = float3(_CC_RainWind.x, -_CC_RainP0.y, _CC_RainWind.z) * spd;
                float3 p = s * B + _CC_RainFall.xyz * spd;
                // коробка вокруг камеры: чуть больше места снизу (видно вниз с этажа)
                float3 origin = cam - float3(B.x * 0.5, B.y * 0.45, B.z * 0.5);
                p = origin + frac((p - origin) / B) * B;

                float3 toCam = cam - p;
                float dist = length(toCam);
                if (CR_Covered(p) || dist < 1e-3) { o.positionCS = float4(2, 2, 2, 1); return o; }

                float3 dir = normalize(vel);
                float3 vd = toCam / dist;
                float3 side = cross(dir, vd);
                float sl = length(side);
                side = sl > 1e-4 ? side / sl : float3(1, 0, 0);
                float len = _CC_RainP0.z * (0.7 + 0.6 * s.z);
                // ширина не тоньше ~1.3 пикселя вдали (без мерцания)
                float pix = dist * 2.0 / max(_ScreenParams.y, 1.0) * 1.3;
                float wid = max(_CC_RainP0.w, pix * (unity_OrthoParams.w > 0.5 ? 0.0 : 1.0));

                float2 c = kCorner[vid % 6];
                float3 wp = p - dir * len * (1.0 - c.y) + side * wid * (c.x - 0.5);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;

                // гаснет у камеры и у краёв коробки (по горизонтали)
                float m = _CC_RainMeter;
                float near = smoothstep(_NearFade * m * 0.4, _NearFade * m, dist);
                float2 hx = abs(p.xz - cam.xz) / (B.xz * 0.5);
                float edge = 1.0 - smoothstep(0.7, 1.0, max(hx.x, hx.y));
                float thin = saturate(_CC_RainP0.w / max(wid, 1e-5));       // вдали капля шире пикселя → прозрачнее
                o.info.x = _CC_RainP1.x * near * edge * lerp(0.35, 1.0, thin);

                Light ml = GetMainLight();
                float back = pow(saturate(dot(-vd, ml.direction)), 6.0);     // смотрим против солнца — капли светятся
                o.info.y = _CC_RainP1.y * (0.35 + back * 2.5) * saturate(ml.direction.y * 4.0 + 0.2);
                o.info.z = step(0.965, CR_Hash11(iid + floor(t * 9.0 + s.x * 7.0))) * _CC_RainP1.y;   // искорка
                o.info.w = s.x;
                o.view = vd;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float across = abs(i.uv.x * 2.0 - 1.0);
                float body = 1.0 - CR_AAStep(0.75, across);                      // чёткая мультяшная нить
                float tip = smoothstep(0.0, _Soft, i.uv.y);                      // хвост тает, голова плотная
                float head = smoothstep(0.75, 1.0, i.uv.y) * 0.35;
                float a = saturate(body * (tip + head)) * i.info.x;
                float3 col = CR_Light(i.view);
                Light ml = GetMainLight();
                col += ml.color * (i.info.y + i.info.z * 2.0) * 0.6;
                return half4(col, saturate(a * (1.0 + i.info.z)));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
