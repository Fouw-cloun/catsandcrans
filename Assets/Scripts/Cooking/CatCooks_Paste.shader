// ============================================================================
//  CatCooks/Paste
//
//  Шейдер пасты: пюре, фарш, тесто. Ставится на материал PasteVisual.
//
//  ГЛАВНОЕ, ЧТО ОН ДЕЛАЕТ — УБИРАЕТ КВАДРАТ.
//    Сетка PasteVisual построена на всю тару целиком. Без обрезки она и
//    рисуется целиком, и в банке лежит ровный бежевый прямоугольник от стенки
//    до стенки — что бы там ни было на самом деле.
//    Здесь каждая вершина несёт ПОКРЫТИЕ (красный канал вершинного цвета):
//    сколько пасты реально под ней. Где покрытия нет — фрагмент
//    выбрасывается. Остаются только сами кучки, с краями по своей форме.
//
//    Порог обрезки ещё и дрожит шумом, поэтому граница не гладкая дуга, а
//    рваный край с языками и отдельными брызгами поодаль. Идеально круглая
//    лужа мгновенно выдаёт формулу.
//
//  ОСТАЛЬНОЕ, ЧЕГО НЕ ДАСТ ОБЫЧНЫЙ LIT
//    КОМКИ в двух масштабах: крупные бугры и мелкая крупа поверх. Рисуются
//      шумом в объектных координатах — крапины сидят НА МЕСТЕ и не плывут,
//      когда поверхность качается от удара. Плывущие пятна сразу выдают
//      шейдер вместо вещества.
//
//    ЦВЕТ КОМКОВ ОТ ЦВЕТА ПАСТЫ. Отдельным полем его задавать нельзя:
//      PasteVisual подставляет цвет из состава, и белые крапины на свекольном
//      пюре выглядели бы мусором. Комки — та же база, темнее и светлее.
//
//    ВЛАЖНЫЙ БЛЕСК отдельным слоем поверх основного. У края слоя и у жидкой
//      пасты плёнка ровнее, поэтому блик там резче — по этому бликующему
//      ободку глаз и опознаёт мокрое.
//
//    ГРЯЗЬ ВО ВПАДИНАХ. Тонкий слой темнее толстого: под ним просвечивает
//      дно. Берётся из зелёного канала вершинного цвета.
//
//    ПРОСВЕТ. У пюре свет чуть проходит сквозь верхний слой — дешёвое
//      подобие подповерхностного рассеяния через обратный френель. Без него
//      паста читается как крашеный пластилин.
//
//  Цвет и густота подаются из кода через MaterialPropertyBlock, поэтому один
//  материал обслуживает все тары: и пюре, и фарш, и тесто.
// ============================================================================
Shader "CatCooks/Paste"
{
    Properties
    {
        [Header(Base)]
        [MainColor] _BaseColor ("Цвет (подставляет PasteVisual)", Color) = (0.93, 0.88, 0.72, 1)

        [Header(Kray)]
        _Cutoff     ("Порог края", Range(0.01, 0.99)) = 0.4
        _EdgeNoise  ("Рваность края", Range(0, 0.5)) = 0.18
        _EdgeScale  ("Частота рваности", Float) = 34
        _EdgeDarken ("Затемнение у края", Range(0, 1)) = 0.35

        [Header(Komki)]
        _ChunkAmount   ("Сколько комков", Range(0, 1)) = 0.6
        _ChunkScale    ("Крупинок поперёк кучки", Float) = 26
        _LumpScale     ("Бугров поперёк кучки", Float) = 6
        _NoiseScale    ("Нормировка (ставит PasteVisual)", Float) = 1
        _ChunkContrast ("Контраст комков", Range(0, 1)) = 0.4
        _ChunkHue      ("Сдвиг тона комков", Range(-0.2, 0.2)) = 0.05
        _BumpStrength  ("Рельеф от комков", Range(0, 2)) = 0.9

        [Header(Konsistenciya)]
        _Thickness  ("Густота (1 = глина, 0 = жижа)", Range(0, 1)) = 1
        _GlossThin  ("Блеск жидкой", Range(0, 1)) = 0.8
        _GlossThick ("Блеск густой", Range(0, 1)) = 0.25

        [Header(Vlaga)]
        _SheenStrength ("Сила влажного блика", Range(0, 3)) = 0.9
        _SheenPower    ("Резкость влажного блика", Range(4, 128)) = 42
        [HDR] _SheenColor ("Цвет влажного блика", Color) = (1, 0.98, 0.92, 1)

        [Header(Glubina)]
        _DepthDarken ("Затемнение тонкого слоя", Range(0, 1)) = 0.45

        [Header(Prosvet)]
        _SSSColor    ("Цвет просвета", Color) = (1, 0.85, 0.6, 1)
        _SSSStrength ("Сила просвета", Range(0, 2)) = 0.5
        _SSSPower    ("Резкость просвета", Range(1, 8)) = 3
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "AlphaTest"
        }

        // Поверхность пасты — одиночный лист без дна и стенок. Односторонний
        // меш исчезает, если смотреть на него снизу или сбоку через край тары,
        // и кажется, что паста пропала.
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half  _Cutoff;
            half  _EdgeNoise;
            half  _EdgeScale;
            half  _EdgeDarken;
            half  _ChunkAmount;
            half  _ChunkScale;
            half  _LumpScale;
            float _NoiseScale;
            half  _ChunkContrast;
            half  _ChunkHue;
            half  _BumpStrength;
            half  _Thickness;
            half  _GlossThin;
            half  _GlossThick;
            half  _SheenStrength;
            half  _SheenPower;
            half4 _SheenColor;
            half  _DepthDarken;
            half4 _SSSColor;
            half  _SSSStrength;
            half  _SSSPower;
        CBUFFER_END

        // Хеш-шум без текстуры: дёшево и не тянет за собой ассет, который
        // придётся не забыть положить в сборку.
        float Hash(float3 p)
        {
            p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);   // сглаживание, иначе видны кубы

            float x00 = lerp(Hash(i + float3(0,0,0)), Hash(i + float3(1,0,0)), f.x);
            float x10 = lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x);
            float x01 = lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x);
            float x11 = lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x);

            return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
        }

        // ВСЕ ЧАСТОТЫ НОРМИРОВАНЫ на размер кучки — множитель приходит из
        // PasteVisual. Объектные координаты у тары порядка тысячных, и без
        // нормировки любое разумное число частоты давало одно ровное пятно
        // на всю поверхность: ни крупы, ни рваного края.
        float3 NoiseP(float3 posOS) { return posOS * _NoiseScale; }

        // Рваность края: чем крупнее зерно, тем длиннее языки по контуру.
        float EdgeJitter(float3 posOS)
        {
            return (ValueNoise(NoiseP(posOS) * _EdgeScale) - 0.5) * _EdgeNoise;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float4 color      : TEXCOORD3;
                half   fogCoord   : TEXCOORD4;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = n.normalWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.color      = IN.color;
                OUT.fogCoord   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN, FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // ───── КРАЙ ─────
                // R вершинного цвета — покрытие: сколько пасты под этой точкой.
                // Где его нет, фрагмента нет. Это и убирает бежевый квадрат:
                // сетка на всю тару, а рисуется только то, что реально лежит.
                float cover = IN.color.r;
                float depth = IN.color.g;

                float jit = EdgeJitter(IN.positionOS);
                clip(cover - _Cutoff + jit);

                // Насколько мы у самой кромки слоя. У края паста тоньше,
                // темнее и глянцевее — стекающая плёнка ровнее середины.
                float edge = 1.0 - saturate((cover - _Cutoff + jit) / 0.35);

                // ───── КОМКИ ─────
                // Два масштаба: крупные бугры и мелкая крупа поверх. Один
                // масштаб даёт либо равномерную рябь, либо пятна без деталей.
                float3 np = NoiseP(IN.positionOS);
                float lumps  = ValueNoise(np * _LumpScale);
                float grain  = ValueNoise(np * _ChunkScale);
                float c = lumps * 0.6 + grain * 0.4;

                // Густая паста рыхлая, у неё комки резче. Жидкая их
                // растворяет — крупа расходится в однородную массу.
                float amount = _ChunkAmount * lerp(0.3, 1.0, _Thickness);
                float chunk  = saturate((c - 0.5) * (1.0 + _ChunkContrast * 6.0) + 0.5);
                chunk = lerp(0.5, chunk, amount);

                half3 baseCol = _BaseColor.rgb;
                half3 dark = baseCol * (1.0 - _ChunkContrast * 0.5);
                half3 light = saturate(baseCol * (1.0 + _ChunkContrast * 0.45)
                                       + half3(_ChunkHue, _ChunkHue * 0.4, -_ChunkHue * 0.6));
                half3 albedo = lerp(dark, light, chunk);

                // Тонкий слой темнее толстого: сквозь него просвечивает дно.
                albedo *= lerp(1.0 - _DepthDarken, 1.0, depth);

                // У кромки ещё темнее — там паста сходит на нет.
                albedo *= lerp(1.0, 1.0 - _EdgeDarken, edge);

                // ───── РЕЛЬЕФ ─────
                // Нормаль гнём производными шума: комки получают объём и
                // ловят блик, а не выглядят наклейкой.
                float3 nrm = normalize(IN.normalWS) * IS_FRONT_VFACE(cullFace, 1.0, -1.0);
                float3 grad = normalize(float3(ddx(c), ddy(c), 0.02));
                nrm = normalize(nrm + grad * _BumpStrength * lerp(0.4, 1.0, _Thickness));

                float3 viewDir = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // Жидкая блестит, густая матовая.
                half smoothness = lerp(_GlossThin, _GlossThick, _Thickness);

                InputData inputData = (InputData)0;
                inputData.positionWS      = IN.positionWS;
                inputData.normalWS        = nrm;
                inputData.viewDirectionWS = viewDir;
                inputData.shadowCoord     = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord        = IN.fogCoord;
                inputData.bakedGI         = SampleSH(nrm);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo     = albedo;
                surface.metallic   = 0.0;
                surface.smoothness = smoothness;
                surface.occlusion  = lerp(0.7, 1.0, depth);   // во впадинах темнее
                surface.alpha      = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surface);

                Light main = GetMainLight(inputData.shadowCoord);

                // ───── ВЛАЖНЫЙ БЛИК ─────
                // Отдельным слоем поверх основного: у края слоя и у жидкой
                // пасты плёнка ровнее, блик резче. Именно по этому бликующему
                // ободку глаз и опознаёт мокрое.
                float3 H = normalize(main.direction + viewDir);
                half spec = pow(saturate(dot(nrm, H)), _SheenPower);
                half wet = lerp(0.35, 1.0, 1.0 - _Thickness) + edge * 0.6;
                color.rgb += _SheenColor.rgb * spec * _SheenStrength * wet
                             * main.color * main.shadowAttenuation;

                // ───── ПРОСВЕТ ─────
                // Свет чуть проходит сквозь верхний слой. Обратный френель —
                // дешёвое подобие подповерхностного рассеяния; без него паста
                // читается как крашеный пластилин.
                half back = pow(saturate(dot(viewDir, -main.direction)), _SSSPower);
                half thin = lerp(1.0, 0.4, depth);   // тонкий слой просвечивает
                color.rgb += _SSSColor.rgb * baseCol * back * _SSSStrength * thin * main.color;

                color.rgb = MixFog(color.rgb, IN.fogCoord);
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }

        // Тень должна повторять форму кучек, а не квадрат сетки.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct AttributesS
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct VaryingsS
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float4 color      : TEXCOORD1;
            };

            VaryingsS shadowVert (AttributesS IN)
            {
                VaryingsS OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);

                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = cs;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.color = IN.color;
                return OUT;
            }

            half4 shadowFrag (VaryingsS IN) : SV_Target
            {
                clip(IN.color.r - _Cutoff + EdgeJitter(IN.positionOS));
                return 0;
            }
            ENDHLSL
        }

        // Глубина — для SSAO и мягких контактов у стенок тары.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag

            struct AttributesD { float4 positionOS : POSITION; float4 color : COLOR; };
            struct VaryingsD
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float4 color      : TEXCOORD1;
            };

            VaryingsD depthVert (AttributesD IN)
            {
                VaryingsD OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.color = IN.color;
                return OUT;
            }

            half depthFrag (VaryingsD IN) : SV_Target
            {
                clip(IN.color.r - _Cutoff + EdgeJitter(IN.positionOS));
                return IN.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
