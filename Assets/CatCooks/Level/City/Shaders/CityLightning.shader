// CatCooks — МОЛНИЯ (мультяшная). Меш строит CityStorm: зигзаг с ветками (ленты к камере) + свечение в тучах
// + вспышка у земли. uv0: x сторона (-1..1), y доля пути сверху вниз, z тип, w ширина.
// Типы: 0 — ствол и ветки (белое ядро + сияние), 2 — второй цветной разряд вокруг ствола, 3 — аура (широкое мягкое
// сияние ствола), 1 — круглое свечение / «бусина» (к камере), 4 — плоская вспышка-диск в туче, 5 — диск на полу
// (вершины уже в мире, рваный край), 6 — разряды-рикошеты по полу от точки удара (разбегаются от центра, трещат).
// В момент удара разряд потрескивает (мелко дрожит).
// Цвет вершины: rgb — множитель, a — яркость ветки. _CC_Bolt: x сила сейчас (мерцает), y возраст (с), z «прорисовано»
// сверху вниз (0..1), w толщина (×, в момент удара больше). Ядро белое с чётким краем, вокруг — цветное свечение (под bloom).
Shader "CatCooks/City/Lightning"
{
    Properties
    {
        _CoreColor ("Ядро", Color) = (1, 1, 1, 1)
        _GlowColor ("Свечение", Color) = (0.55, 0.62, 1, 1)
        _Glow2Color ("Второй разряд (вьётся вокруг)", Color) = (0.85, 0.45, 1, 1)
        _Crackle ("Потрескивание в момент удара", Range(0, 3)) = 1
        _CoreBoost ("Яркость ядра (HDR)", Range(1, 12)) = 5
        _CoreWidth ("Ширина ядра (доля ленты)", Range(0.05, 0.6)) = 0.24
        _CloudGlow ("Свечение туч", Range(0, 2)) = 0.8
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Lightning"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _GlowColor, _Glow2Color;
                float _CoreBoost, _CoreWidth, _CloudGlow, _Crackle;
            CBUFFER_END
            float4 _CC_Bolt;
            float4 _CC_BoltT;      // x — длительность лидера (с): после неё бегут рикошеты по полу

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;      // касательная пути
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;         // x сторона, y путь, z тип, w —
                float4 color : COLOR;
            };

            float Hash11(float n) { return frac(sin(n * 12.9898) * 43758.5453); }
            float Noise1(float x) { float i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f); return lerp(Hash11(i), Hash11(i + 1.0), f); }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 cam = GetCameraPositionWS();
                float type = v.uv.z;
                float w = v.uv.w;
                bool arc = type > 5.5;
                bool ribbon = type < 0.5 || (type > 1.5 && type < 3.5) || arc;
                if (ribbon)
                {
                    if (arc)
                    {
                        // рикошет: появляется после удара и разбегается от центра за ~0.12 с
                        float ta = _CC_Bolt.y - _CC_BoltT.x;
                        if (ta < 0.0 || v.uv.y > saturate(ta / 0.12) + 0.002) { o.positionCS = float4(2, 2, 2, 1); return o; }
                    }
                    else if (v.uv.y > _CC_Bolt.z + 0.002) { o.positionCS = float4(2, 2, 2, 1); return o; }   // ещё не «дорисована»
                    float3 T = normalize(TransformObjectToWorldDir(v.normalOS));
                    float3 V = normalize(cam - wp);
                    float3 S = cross(T, V);
                    float sl = length(S);
                    S = sl > 1e-4 ? S / sl : float3(1, 0, 0);
                    float4 cs0 = TransformWorldToHClip(wp);
                    float pix = 2.0 * max(cs0.w, 0.01) / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                    float thick = max(_CC_Bolt.w, 1.0);
                    w *= thick;                                                       // в момент удара толще
                    float minPx = arc ? 2.5 : (type > 2.5 ? 10.0 : (type > 1.5 ? 2.5 : 4.0));
                    w = max(w, pix * minPx / max(_CoreWidth, 0.05));
                    // потрескивание: точки пути мелко прыгают поперёк, пока идёт удар
                    float cr = (Hash11(floor(v.uv.y * 70.0) * 1.7 + floor(_CC_Bolt.y * 28.0) * 13.1) - 0.5);
                    float crk = arc ? 1.0 : saturate(thick - 1.0);                    // рикошеты трещат всё время
                    wp += S * cr * v.uv.w * 1.6 * _Crackle * crk;
                    wp += S * v.uv.x * w;
                }
                else if (type < 1.5 || (type > 4.5 && type < 5.5))
                {
                    if (type < 1.5)
                    {
                        float3 R = UNITY_MATRIX_V[0].xyz, U = UNITY_MATRIX_V[1].xyz;
                        wp += (R * v.uv.x + U * v.uv.y) * w;
                    }
                    // тип 5 — диск на полу: вершины уже на месте
                }
                else
                {
                    // плоская вспышка в туче: широкая по горизонтали, тонкая по вертикали
                    float3 V = normalize(cam - wp);
                    float3 R = normalize(cross(float3(0, 1, 0), V) + 1e-5);
                    float3 U = cross(V, R);
                    wp += (R * v.uv.x + U * v.uv.y * 0.22) * w;
                }
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float I = _CC_Bolt.x;
                if (I <= 0.001) discard;
                float type = i.uv.z;
                float s = abs(i.uv.x);
                if (type < 0.5)
                {
                    // ствол и ветки: белое ядро с чётким краем + цветное сияние
                    float fw = max(fwidth(s), 1e-4);
                    float core = saturate((_CoreWidth - s) / fw + 0.5);
                    float glow = pow(saturate(1.0 - s), 2.5) * 0.55;
                    float b = i.color.a;
                    float fadeB = b > 0.99 ? 1.0 : exp(-max(_CC_Bolt.y - 0.12, 0.0) * 6.0);   // ветки гаснут раньше
                    float k = I * b * fadeB;
                    float3 col = (_CoreColor.rgb * core * _CoreBoost + _GlowColor.rgb * glow) * k * i.color.rgb;
                    return half4(col, 0);
                }
                if (type < 1.5)
                {
                    // круглое свечение / бусина: мягкое пятно ступеньками + горячая серединка
                    float d = length(i.uv.xy);
                    float g = saturate(1.0 - d);
                    g = g * g;
                    g = lerp(g, floor(g * 4.0 + 0.5) / 4.0, 0.35);
                    float hot = saturate((g - 0.55) / max(fwidth(g), 1e-4) + 0.5) * i.color.r;   // r — доля горячей серединки
                    float3 col = (_GlowColor.rgb * g * _CloudGlow + _CoreColor.rgb * hot * _CoreBoost * 0.5) * I * i.color.a;
                    return half4(col, 0);
                }
                if (type < 2.5)
                {
                    // второй разряд: цветное ядро потоньше
                    float fw = max(fwidth(s), 1e-4);
                    float core = saturate((_CoreWidth * 1.3 - s) / fw + 0.5);
                    float glow = pow(saturate(1.0 - s), 2.0) * 0.6;
                    float k = I * i.color.a * exp(-max(_CC_Bolt.y - 0.2, 0.0) * 3.0);
                    float3 col = (lerp(_Glow2Color.rgb, _CoreColor.rgb, 0.35) * core * _CoreBoost * 0.7 + _Glow2Color.rgb * glow) * k;
                    return half4(col, 0);
                }
                if (type > 5.5)
                {
                    // рикошет: ядро цвета второго разряда, вспыхивает рывками, гаснет за ~0.5 с
                    float fw = max(fwidth(s), 1e-4);
                    float core = saturate((_CoreWidth * 1.2 - s) / fw + 0.5);
                    float glow = pow(saturate(1.0 - s), 2.0) * 0.6;
                    float ta = max(_CC_Bolt.y - _CC_BoltT.x, 0.0);
                    float blink = step(0.3, Hash11(floor(_CC_Bolt.y * 30.0) * 3.1 + i.color.g * 57.0));
                    float k = max(_CC_Bolt.x, 0.6) * i.color.a * exp(-ta * 4.0) * lerp(0.35, 1.0, blink);
                    float3 col = (lerp(_Glow2Color.rgb, _CoreColor.rgb, 0.5) * core * _CoreBoost * 0.8 + lerp(_GlowColor.rgb, _Glow2Color.rgb, 0.5) * glow) * k;
                    return half4(col, 0);
                }
                if (type < 3.5)
                {
                    // аура: широкое мягкое сияние вокруг ствола
                    float g = pow(saturate(1.0 - s), 2.2) * 0.22;
                    return half4(_GlowColor.rgb * g * I * i.color.a, 0);
                }
                // 4 — плоская вспышка в туче: яркая серединка, мягкий край
                float d = length(i.uv.xy);
                float fwd = max(fwidth(d), 1e-4);
                if (type < 4.5)
                {
                    float g4 = pow(saturate(1.0 - d), 2.0);
                    float core4 = saturate((0.22 - d) / fwd + 0.5);
                    return half4((_GlowColor.rgb * g4 + _CoreColor.rgb * core4 * _CoreBoost * 0.6) * I * i.color.a, 0);
                }
                // 5 — пятно света на полу: рваная звезда (не идеальный круг), лучи разной длины, две ступени яркости
                float ang = atan2(i.uv.y, i.uv.x);
                float sd = i.color.g * 50.0;
                float rays = Noise1(ang * 2.2 + sd) * 0.6 + Noise1(ang * 6.0 + sd * 1.7) * 0.4;
                float edge = 0.45 + 0.45 * rays * rays;
                float dd = d / edge;
                float fw5 = max(fwidth(dd), 1e-4);
                float outer = saturate((1.0 - dd) / fw5 + 0.5);
                float inner = saturate((0.45 - dd) / fw5 + 0.5);
                float g5 = pow(saturate(1.0 - dd), 1.5);
                float3 col5 = (_GlowColor.rgb * (g5 * 0.8 + outer * 0.25) + _CoreColor.rgb * inner * _CoreBoost * 0.5) * I * i.color.a;
                return half4(col5, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
