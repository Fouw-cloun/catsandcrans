// =============================================================
//  CatCooks_LightningBolt.shader
//  Канал молнии: лента, развёрнутая к камере вокруг своего направления.
//
//  ПОЧЕМУ АДДИТИВНОЕ СМЕШИВАНИЕ, А НЕ АЛЬФА
//   Разряд — это источник света, а не поверхность. При альфа-смешивании
//   он замещает фон и на светлом небе выглядит серой лентой. Аддитивное
//   прибавляет к тому, что уже есть: на тёмном грозовом небе канал
//   слепит, на светлом читается мягче — ровно как в жизни.
//
//  ПРОФИЛЬ ПОПЕРЁК КАНАЛА. Две составляющие: узкое белое ядро и широкий
//  цветной ореол. Одна только полоса ровного цвета читается как лапша;
//  ощущение раскалённого канала даёт именно пересвеченная середина.
//
//  РАЗВОРОТ. Лента крутится вокруг направления отрезка, а не вокруг
//  оси камеры: канал должен оставаться в своей плоскости, иначе при
//  движении камеры он проворачивается как лопасть.
//
//  ZWrite выключен, ZTest обычный: разряд прячется за рельеф, но сам
//  ничего не загораживает.
//
//  URP 17 / Unity 6.
// =============================================================
Shader "CatCooks/Lightning Bolt"
{
    Properties
    {
        [Header(Color)]
        _CoreColor ("Core", Color) = (1, 1, 1, 1)
        _GlowColor ("Glow", Color) = (0.55, 0.68, 1, 1)

        [Header(Profile)]
        // Доля полуширины, которую занимает белое ядро.
        _CoreWidth ("Core Width", Range(0.02, 0.8)) = 0.22

        // Спад ореола. Ниже единицы — ореол широкий и мягкий,
        // выше — жмётся к каналу.
        _GlowFalloff ("Glow Falloff", Range(0.5, 6)) = 2.2
        _GlowStrength ("Glow Strength", Range(0, 4)) = 1.6

        [Header(Drive)]
        // Ставится из CatCooksLightningBolt каждый кадр через
        // MaterialPropertyBlock: это и есть мерцание разряда.
        _Intensity ("Intensity (runtime)", Range(0, 4)) = 1

        // Общая яркость канала. Отдельно от _Intensity, чтобы можно было
        // подстроить под экспозицию сцены, не трогая анимацию.
        _Brightness ("Brightness", Range(0, 20)) = 6

        [Header(Shape)]
        // Кончик в облаке гасим: канал должен тонуть в облачности,
        // а не обрываться отрезком в воздухе.
        _TopFade ("Top Fade", Range(0, 1)) = 0.35

        // Минимальная толщина в пикселях. Без неё дальний разряд тоньше
        // пикселя и превращается в мерцающий пунктир.
        _MinPixels ("Min Screen Width (px)", Range(0, 8)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Name "CatCooksLightningBolt"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One          // аддитивно: разряд светит, а не красит
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _GlowColor;
                float  _CoreWidth, _GlowFalloff, _GlowStrength;
                float  _Intensity, _Brightness, _TopFade, _MinPixels;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;   // x = сторона 0/1, y = вдоль 0..1
                float3 segDir     : TEXCOORD1;   // направление отрезка
                float2 shape      : TEXCOORD2;   // x = полуширина, y = яркость
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  bright     : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 dir   = normalize(TransformObjectToWorldDir(v.segDir));
                float3 view  = GetCameraPositionWS() - posWS;
                float  dist  = length(view);
                view = view / max(dist, 1e-4);

                // Поперечная ось ленты: перпендикулярна и каналу, и взгляду.
                // Если смотришь строго вдоль канала, векторное произведение
                // вырождается — берём любую перпендикулярную ось, всё равно
                // в этом ракурсе канал вырожден в точку.
                float3 side = cross(dir, view);
                float  sl = length(side);
                side = sl > 1e-4 ? side / sl : normalize(cross(dir, float3(0, 1, 0) + 1e-3));

                float halfW = v.shape.x;

                // ПОЛ ПО ЭКРАННОЙ ТОЛЩИНЕ. На большой дальности канал уходит
                // тоньше пикселя, растеризатор ловит его через раз, и разряд
                // рассыпается в пунктир. Считаем, сколько метров приходится
                // на пиксель на этой глубине, и не даём ленте стать уже.
                // Вертикальный размер кадра на этой глубине = 2*dist/P._m11,
                // делим на высоту экрана в пикселях. UNITY_MATRIX_P есть
                // всегда, в отличие от unity_CameraInvProjection.
                float metersPerPixel = dist * 2.0
                                     / (max(UNITY_MATRIX_P._m11, 1e-4)
                                        * max(_ScreenParams.y, 1.0));
                halfW = max(halfW, _MinPixels * metersPerPixel * 0.5);

                float sideSign = v.uv.x * 2.0 - 1.0;
                posWS += side * sideSign * halfW;

                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = v.uv;
                o.bright = v.shape.y;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 0 в середине ленты, 1 у края.
                float across = abs(i.uv.x * 2.0 - 1.0);

                float core = 1.0 - smoothstep(_CoreWidth, _CoreWidth + 0.12, across);
                float glow = pow(saturate(1.0 - across), _GlowFalloff);

                float3 col = _CoreColor.rgb * core
                           + _GlowColor.rgb * glow * _GlowStrength;

                // Верхний конец растворяем в облаке: uv.y = 0 — это облако,
                // 1 — земля. У ветвей uv.y = 1 везде, они не гаснут у корня.
                col *= smoothstep(0.0, max(_TopFade, 1e-3), i.uv.y);

                col *= i.bright * _Intensity * _Brightness;

                // Аддитивно: альфа в смешивании не участвует (Blend One One),
                // но держим её осмысленной на случай смены режима.
                return half4(col, saturate(core + glow));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
