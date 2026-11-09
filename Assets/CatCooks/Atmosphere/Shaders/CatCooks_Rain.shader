// =============================================================
//  CatCooks_Rain.shader
//  Дождь: падающие струи и круги-всплески на земле.
//  Один шейдер, два режима — _Mode 0 струи, 1 всплеск.
//
//  ПОЧЕМУ НЕ ЧАСТИЦЫ
//   Дождь — это тысячи объектов. Через ParticleSystem это тысячи
//   пересчётов на процессоре каждый кадр. Здесь один статичный меш
//   из квадов, а падение и зацикливание считает вершинный шейдер:
//   капли просто едут по времени и заворачиваются через frac().
//   Процессор не делает вообще ничего.
//
//  ИНТЕНСИВНОСТЬ. У каждого квада в вершинных данных лежит «ранг»
//  0…1. Если ранг выше _Intensity, квад схлопывается в точку и не
//  рисуется. То есть плотность дождя меняется одним числом, без
//  пересборки меша и без аллокаций.
//
//  СТИЛИЗАЦИЯ. Струя — не линия, а вытянутая капля: сверху тонкая,
//  к низу утолщается и снова сходит на нет. У фотореалистичного
//  дождя струя равномерная, у мультяшного — именно каплевидная.
//
//  URP 17 / Unity 6.
// =============================================================
Shader "CatCooks/Rain"
{
    Properties
    {
        [Header(Mode)]
        // 0 — падающие струи, 1 — круг-всплеск на земле.
        _Mode ("Mode (0 streak 1 splash)", Float) = 0

        [Header(Look)]
        _Color     ("Color", Color) = (0.82, 0.88, 0.95, 1)
        _Opacity   ("Opacity", Range(0, 1)) = 0.5
        _Intensity ("Intensity", Range(0, 1)) = 0.5

        [Header(Streak)]
        _FallSpeed  ("Fall Speed", Range(0, 60)) = 22
        _BoxHeight  ("Column Height", Range(2, 60)) = 26

        // Мировая высота земли. Капли рождаются НАД ней и гаснут,
        // дойдя до неё, — иначе они улетают сквозь рельеф и продолжают
        // падать под землёй, что и видно на склонах.
        _GroundY ("Ground Level (world Y)", Float) = 0
        _Slant      ("Wind Slant", Vector) = (0.15, 0, 0, 0)
        _Taper      ("Drop Taper", Range(0.5, 6)) = 2.2
        _HeadBright ("Head Brightness", Range(0, 3)) = 1.6

        // Яркая нить по центру струи. Капля — это линза: свет проходит
        // насквозь и собирается в середине. Без неё струя читается
        // плоской полоской краски.
        _CoreBright ("Core Highlight", Range(0, 3)) = 1.2
        _CoreWidth  ("Core Width", Range(0.05, 1)) = 0.35

        // Подсветка на просвет: капли между тобой и солнцем вспыхивают.
        // Считается одним dot в вершинном шейдере — бесплатно.
        _Backlight  ("Backlight", Range(0, 3)) = 1.1
        _BacklightPow ("Backlight Focus", Range(1, 24)) = 6

        // Разброс яркости между каплями. Одинаковые читаются как сетка.
        _Sparkle ("Brightness Variance", Range(0, 1)) = 0.45

        [Header(Splash)]
        _RingWidth ("Ring Width", Range(0.02, 0.5)) = 0.16
        _RingFade  ("Ring Fade", Range(0.1, 2)) = 0.8

        // ОТСКОК. Одно кольцо на земле читается как круг на воде, а не
        // как удар капли: настоящий всплеск выбрасывает вверх корону
        // мелких брызг, и именно она даёт ощущение, что дождь БЬЁТ,
        // а не что по земле пошла рябь. Рисуем её тем же квадом —
        // вершины расходятся по конусу и падают обратно по параболе,
        // лишней геометрии не появляется.
        // МНОЖИТЕЛИ РАЗМЕРА КАПЛИ. Длина и ширина запечены в меш при
        // сборке, и менять их полями компонента значило бы пересобирать
        // все две тысячи капель на каждую смену погоды. Здесь они просто
        // домножаются в вершине — смена характера дождя становится
        // бесплатной и плавной.
        _LenScale ("Length Scale", Range(0.2, 3)) = 1
        _WidScale ("Width Scale", Range(0.2, 3)) = 1

        _SplashSpray ("Spray Amount", Range(0, 1)) = 0.65
        _SplashHeight ("Spray Height", Range(0, 2)) = 0.55

        [Header(Fades)]
        _NearFade ("Near Fade", Range(0, 5)) = 0.6
        _FarFade  ("Far Fade", Range(5, 200)) = 45
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "CatCooksRain"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Прогресс всплеска, 0…1. Пишется из C# через MPB.
            float _SplashT;

            CBUFFER_START(UnityPerMaterial)
                float  _Mode;
                float4 _Color;
                float  _Opacity, _Intensity;
                float  _FallSpeed, _BoxHeight, _GroundY;
                float4 _Slant;
                float  _Taper, _HeadBright;
                float  _CoreBright, _CoreWidth, _Backlight, _BacklightPow, _Sparkle;
                float  _RingWidth, _RingFade, _SplashSpray, _SplashHeight;
                float  _LenScale, _WidScale;
                float  _NearFade, _FarFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // xy — угол квада, метры
                float2 uv         : TEXCOORD0;
                float2 cell       : TEXCOORD1;  // смещение капли по XZ
                float2 rankPhase  : TEXCOORD2;  // x = ранг 0..1, y = фаза
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  dist       : TEXCOORD1;
                float  fade       : TEXCOORD2;
                float  glint      : TEXCOORD3;   // яркость капли + подсветка
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.uv = v.uv;

                float rank = v.rankPhase.x;
                float phase = v.rankPhase.y;

                // ИНТЕНСИВНОСТЬ: капля с рангом выше порога схлопывается
                // в точку. Плотность меняется одним числом, без пересборки.
                if (rank > _Intensity)
                {
                    o.positionCS = float4(0, 0, -10, 1);
                    o.dist = 0; o.fade = 0; o.glint = 0;
                    return o;
                }

                float3 origin = TransformObjectToWorld(float3(0, 0, 0));

                if (_Mode < 0.5)
                {
                    // ---------------- СТРУЯ ----------------
                    // Падение целиком в шейдере: frac() заворачивает
                    // каплю наверх, когда она уходит вниз.
                    float speedVar = 0.75 + rank * 0.5;
                    float t = frac(phase + _Time.y * _FallSpeed * speedVar / _BoxHeight);

                    // Колонна стоит НА ЗЕМЛЕ, а не вокруг камеры: низ
                    // ровно на _GroundY, верх на высоту колонны выше.
                    // Раньше центр был на уровне глаз, и половина капель
                    // падала ниже рельефа.
                    float y = _GroundY + (1.0 - t) * _BoxHeight;

                    float3 centre = float3(origin.x + v.cell.x, y,
                                           origin.z + v.cell.y)
                                  + _Slant.xyz * (t * _BoxHeight);

                    // КАПЛЯ ЛЕЖИТ ВДОЛЬ СВОЕЙ ТРАЕКТОРИИ.
                    //
                    // Здесь была причина «струи под углом, а капли
                    // вертикальные»: центр сносило ветром через _Slant,
                    // а квад строился по мировой оси Y. Дождь летел вбок,
                    // но каждая капля при этом стояла строго вертикально —
                    // получалась не косая струя, а лесенка из вертикальных
                    // чёрточек, съезжающая в сторону.
                    //
                    // Направление падения = вниз плюс снос. Вдоль него и
                    // вытягиваем каплю, а поперёк разворачиваем к камере.
                    float3 fall = normalize(float3(_Slant.x, -1.0, _Slant.z));

                    float3 vd = GetCameraPositionWS() - centre;
                    float3 right = cross(fall, vd);
                    float rl = length(right);
                    // Смотрим точно вдоль струи — вырождается; берём любую
                    // перпендикулярную ось, в этом ракурсе капля всё равно
                    // вырождена в точку.
                    right = rl > 1e-4 ? right / rl
                          : normalize(cross(fall, float3(0, 0, 1) + 1e-3));

                    // Хвост капли идёт НАЗАД по траектории, то есть вверх
                    // по ветру, а не просто вверх.
                    float3 along = -fall;

                    float3 posWS = centre
                                 + right * (v.positionOS.x * _WidScale)
                                 + along * (v.positionOS.y * _LenScale);

                    o.positionCS = TransformWorldToHClip(posWS);
                    o.dist = distance(GetCameraPositionWS(), centre);

                    // Гасим у пола и потолка колонны, чтобы капли не
                    // возникали и не исчезали на ровном месте.
                    // Гаснет у самой земли: капля не должна пропадать
                    // резко, но и уходить в грунт ей нельзя.
                    // ДО САМОЙ ЗЕМЛИ. Нижнее затухание стояло на 0.90 —
                    // капля исчезала за десятую часть высоты колонны до
                    // грунта, то есть при высоте 26 м гасла в двух с
                    // половиной метрах над землёй. Дождь буквально
                    // не долетал, и всплески появлялись из ничего.
                    // Сверху затухание нужно (капля не должна возникать
                    // резко), снизу — только чтобы срезать последний
                    // сантиметр в момент удара.
                    o.fade = smoothstep(0.0, 0.10, t) * (1.0 - smoothstep(0.985, 1.0, t));

                    // Подсветка на просвет: смотришь в сторону солнца —
                    // капли вспыхивают. Один dot на вершину, стоит ничего.
                    float3 V = normalize(GetCameraPositionWS() - centre);
                    float3 L = normalize(_MainLightPosition.xyz);
                    float back = pow(saturate(-dot(V, L)), _BacklightPow) * _Backlight;

                    // Разброс яркости: одинаковые капли читаются как сетка.
                    float vary = lerp(1.0 - _Sparkle, 1.0 + _Sparkle,
                                      frac(phase * 7.31));
                    o.glint = vary * (1.0 + back);
                }
                else
                {
                    // ---------------- ВСПЛЕСК ----------------
                    // ЗДЕСЬ БЫЛ БАГ, ИЗ-ЗА КОТОРОГО ВСПЛЕСКОВ НЕ БЫЛО ВООБЩЕ.
                    // Меш кольца лежит в плоскости XZ: вершины (±0.5, 0, ±0.5).
                    // А код брал positionOS.x и positionOS.Y — то есть икс
                    // и НОЛЬ. Все четыре вершины схлопывались в отрезок,
                    // площади не оставалось, рисовать было нечего.
                    // Берём позицию целиком и через матрицу объекта —
                    // тогда работают и поворот кольца, и его масштаб.
                    float grow = 0.35 + _SplashT * 0.65;
                    float3 local = v.positionOS.xyz * grow;

                    // КОРОНА БРЫЗГ. Угол вершины вокруг центра задаёт
                    // направление отскока, высота идёт по параболе:
                    // вверх на первой половине жизни, потом вниз.
                    float ang = atan2(v.positionOS.z, v.positionOS.x);
                    float lift = 4.0 * _SplashT * (1.0 - _SplashT);   // 0→1→0
                    local.y += lift * _SplashHeight * _SplashSpray;

                    // Разброс по кругу неровный: ровная корона выглядит
                    // штампом, брызги должны лететь вразнобой.
                    local.xz *= 1.0 + _SplashSpray * 0.45
                              * (0.6 + 0.4 * sin(ang * 3.0 + origin.x * 7.3));

                    float3 posWS = TransformObjectToWorld(local);

                    o.positionCS = TransformWorldToHClip(posWS);
                    o.dist = distance(GetCameraPositionWS(), origin);
                    o.fade = 1.0 - _SplashT;
                    o.glint = 1.0 + lift * 0.8;   // брызги ловят свет ярче
                }

                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float a;

                if (_Mode < 0.5)
                {
                    // Каплевидный профиль: сверху тонко, к низу утолщается
                    // и сходит на нет. Равномерная полоса выглядит
                    // фотореалистичной штриховкой, а не мультяшной каплей.
                    float along = i.uv.y;                       // 0 низ … 1 верх
                    float width = pow(saturate(1.0 - along), 0.6)
                                * smoothstep(0.0, 0.25, along);
                    float across = abs(i.uv.x - 0.5) * 2.0;
                    a = saturate(1.0 - across / max(width, 0.02));
                    a = pow(a, _Taper);

                    // ЯРКАЯ НИТЬ ПО ЦЕНТРУ. Капля работает как линза: свет
                    // проходит насквозь и собирается посередине. Без этого
                    // струя выглядит плоской полоской краски.
                    float core = saturate(1.0 - across / max(width * _CoreWidth, 0.01));
                    a += pow(core, 2.0) * _CoreBright * 0.35;

                    // Головка капли ярче — она первой ловит свет.
                    a *= 1.0 + (1.0 - along) * (_HeadBright - 1.0) * 0.5;

                    a *= i.glint;
                }
                else
                {
                    // Кольцо: расходится и истончается.
                    float2 dd = i.uv - 0.5;
                    float r = length(dd) * 2.0;
                    float w = _RingWidth * (1.0 - _SplashT * 0.5);
                    a = saturate(1.0 - abs(r - (0.35 + _SplashT * 0.6)) / max(w, 0.01));
                    a = pow(a, _RingFade) * (1.0 - r * 0.3);

                    // ТОЧКИ БРЫЗГ поверх кольца: восемь лепестков по углу,
                    // видимых только пока корона поднята. Рваный силуэт
                    // удара вместо ровной окружности.
                    if (_SplashSpray > 0.01)
                    {
                        float pang = atan2(dd.y, dd.x);
                        float petals = pow(saturate(cos(pang * 8.0) * 0.5 + 0.5), 6.0);
                        float band = saturate(1.0 - abs(r - 0.75) / 0.3);
                        float life = 4.0 * _SplashT * (1.0 - _SplashT);
                        a += petals * band * life * _SplashSpray * 0.9;
                    }
                }

                a *= i.fade * _Opacity;
                a = min(a, 1.0);

                // Не бьём в лицо вплотную и не мельтешим вдали.
                a *= smoothstep(0.0, max(_NearFade, 0.01), i.dist);
                a *= 1.0 - smoothstep(_FarFade * 0.6, _FarFade, i.dist);

                if (a <= 0.004) discard;
                return half4(_Color.rgb, saturate(a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
