// CatCooks — стёкла окон, мультяшные. Непрозрачный и дешёвый.
// ДЕНЬ: яркий градиент (сверху светлее), отражение неба по френелю, 1–2 жирные косые полосы-блика НА КАЖДОЕ окно,
//   тень от рамы сверху и сбоку. Яркость следует за освещением сцены (Mood Director) — на закате/в тучах темнее.
// НОЧЬ (_CC_Night от моста «Окна ночью»): часть окон горит тёплым HDR (Bloom), часть — холодным ТВ, остальные тёмные.
// ДОЖДЬ (_CatWetness из Atmosphere): подтёки и капли.
//
// Каждое окно — своё: кнопка «Атласы» размечает стёкла моделей (каждый кусок стекла = окно: свои координаты 0..1 и
// случайное число — в UV3/UV4). Большие сплошные стёкла днём — одно цельное стекло (градиент и крупные блики через
// весь фасад, без швов); ночью по сетке Big Cell горят отдельные окна. Неразмеченные модели — сеткой Cell Size.
Shader "CatCooks/City/Window Glass"
{
    Properties
    {
        [Header(Day)]
        _GlassTop ("Стекло — верх", Color) = (0.80, 0.97, 1.00, 1)
        _GlassBottom ("Стекло — низ", Color) = (0.42, 0.78, 0.86, 1)
        _SkyColor ("Небо в отражении", Color) = (0.60, 0.88, 1.00, 1)
        _HorizonColor ("Горизонт в отражении (тёплый)", Color) = (1.00, 0.90, 0.76, 1)
        _ReflMix ("Сила отражения", Range(0, 1)) = 0.45
        _FresnelPower ("Френель — резкость", Range(0.5, 8)) = 3
        _Brightness ("Яркость", Range(0.5, 2)) = 1.15
        _SceneFollow ("Темнеть вместе со сценой", Range(0, 1)) = 0.7

        [Header(Stripes)]
        _StripeColor ("Блик — цвет", Color) = (1, 1, 1, 1)
        _StripeStrength ("Блик — сила", Range(0, 1)) = 0.55
        _StripeWidth ("Блик — ширина", Range(0.02, 0.4)) = 0.14
        _StripeSlant ("Блик — наклон", Range(0, 2)) = 0.8
        _FrameShadow ("Тень от рамы", Range(0, 0.5)) = 0.18

        [Header(Windows grid)]
        _CellSize ("Окно (неразмеченные), м: ширина, этаж", Vector) = (2.5, 3, 0, 0)
        _BigIsland ("Стекло шире (м) — считать сплошным фасадом", Float) = 15
        _BigCell ("Окно на сплошном фасаде, м", Vector) = (6, 6, 0, 0)
        _OfficeCell ("Ночью: окно на большом стекле (лента, фасад), м", Vector) = (2.6, 3.3, 0, 0)
        _OfficeFrame ("Ночью: переплёт окна на большом стекле", Range(0, 0.3)) = 0.08
        _OfficeBright ("Ночью: яркость окон на большом стекле", Range(0.3, 1.5)) = 0.8
        _BigStripe ("Блики на сплошном фасаде — шаг, м", Float) = 40
        [HideInInspector] _CC_Office ("Office", Float) = 0
        [HideInInspector] _CC_TopY ("Office top Y", Float) = 0
        [HideInInspector] _CC_BandH ("Office band", Float) = 0
        [HideInInspector] _CC_FloorH ("Office floor", Float) = 0

        [Header(Night)]
        [HDR] _NightColor ("Свет окна ночью", Color) = (5, 2.9, 1.3, 1)
        [HDR] _NightColor2 ("Холодный (ТВ)", Color) = (1.2, 1.8, 3.2, 1)
        _ColdShare ("Доля холодных", Range(0, 1)) = 0.05
        _Palette ("Палитра оттенков (0 — один цвет)", Range(0, 1)) = 0.85
        _NightDark ("Негорящие ночью", Color) = (0.06, 0.08, 0.14, 1)

        [Header(Rooms)]
        _Interior ("Комнаты за окнами (фальшивая глубина)", Range(0, 1)) = 1
        _RoomDepth ("Глубина комнаты, м", Range(1, 8)) = 3.5
        _RoomFar ("Комнаты видно до, м", Float) = 120
        _Curtains ("Шторы и жалюзи", Range(0, 1)) = 0.7

        [Header(Rain)]
        _RainAmount ("Капли и подтёки в дождь", Range(0, 1)) = 0.8
        _RainSpeed ("Скорость подтёков", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _GlassTop, _GlassBottom, _SkyColor, _HorizonColor;
            half _ReflMix, _FresnelPower, _Brightness, _SceneFollow;
            half4 _StripeColor;
            half _StripeStrength, _StripeWidth, _StripeSlant, _FrameShadow;
            float4 _CellSize, _BigCell, _OfficeCell;
            float _OfficeFrame, _OfficeBright;
            float _BigIsland, _BigStripe;
            float _CC_Office, _CC_TopY, _CC_BandH, _CC_FloorH;   // офис-небоскрёб (MaterialPropertyBlock от CityOfficeTower)
            half4 _NightColor, _NightColor2, _NightDark;
            half _ColdShare, _Palette;
            half _RainAmount, _RainSpeed;
            half _Interior, _RoomDepth, _Curtains;
            float _RoomFar;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardGlass"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CityCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv3 : TEXCOORD2;      // координаты внутри окна 0..1 (разметка)
                float4 uv4 : TEXCOORD3;      // x — случайное окна, y — ширина, z — 1 если размечено, w — высота
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 wuv : TEXCOORD2;
                nointerpolation float4 info : TEXCOORD3;   // x — случайное, y — 1: своё окно, z — seed объекта, w — 1: размечено
                half fog : TEXCOORD4;
                nointerpolation float3 bigWin : TEXCOORD5;  // x — 1: окно из разметки огромное (лента/фасад); yz — ширина и высота окна (мир)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                float3 objPos = TransformObjectToWorld(float3(0, 0, 0));
                float seed = CC_Hash31(floor(objPos * 0.37)) * 97.0;
                // ширина окна в мире: разметка в единицах меша × масштаб объекта
                float sx = length(TransformObjectToWorld(float3(1, 0, 0)) - objPos);
                float baked = v.uv4.z > 0.5 ? 1.0 : 0.0;
                // uv4.z: 3 — окно (разметка v3), 2 — сплошной стеклянный фасад, 1 — старая разметка (решает Big Island)
                float own = (abs(v.uv4.z - 3.0) < 0.5 || (abs(v.uv4.z - 1.0) < 0.5 && v.uv4.y * sx < _BigIsland)) ? 1.0 : 0.0;
                o.wuv = v.uv3;
                o.info = float4(v.uv4.x, own, seed, baked);
                // размер окна из разметки (uv4.y — ширина, uv4.w — высота, в единицах меша) → метры
                float sy = length(TransformObjectToWorld(float3(0, 1, 0)) - objPos);
                float mtr = _CC_Meter > 0.0 ? _CC_Meter : 3.5;
                o.bigWin = float3((own > 0.5 && (v.uv4.y * sx / mtr > 6.0 || v.uv4.w * sy / mtr > 6.0)) ? 1.0 : 0.0, v.uv4.y * sx, v.uv4.w * sy);

                return o;
            }

            half Stripe(half s, half center, half width)
            {
                half d = abs(s - center);
                return smoothstep(width, width * 0.55h, d);                 // жёсткий мультяшный край
            }

            // ── КОМНАТА ЗА ОКНОМ (interior mapping без текстур): луч из точки стекла внутрь коробки комнаты
            // W×H (размер окна, м) × D (глубина) → задняя стена с мебелью и картиной, боковые стены (дверь),
            // пол в доски, потолок; у каждого окна свой цвет обоев, мебели. lampK — свет люстры (для ночи).
            half3 RoomColor(float2 wuv, float3 vts, float2 winM, float rnd, out half lampK)
            {
                float W = max(winM.x, 0.3), H = max(winM.y, 0.3), D = max((float)_RoomDepth, 0.5);
                float3 o = float3(saturate(wuv.x) * W, saturate(wuv.y) * H, 0.0);
                float3 d = float3(vts.x, vts.y, max(vts.z, 0.06));
                float tx = d.x > 1e-4 ? (W - o.x) / d.x : (d.x < -1e-4 ? -o.x / d.x : 1e5);
                float ty = d.y > 1e-4 ? (H - o.y) / d.y : (d.y < -1e-4 ? -o.y / d.y : 1e5);
                float tz = (D - o.z) / d.z;
                float t = min(tx, min(ty, tz));
                float3 h = o + d * t;
                float3 hn = float3(h.x / W, h.y / H, h.z / D);
                float r1 = frac(rnd * 3.17 + 0.13), r2 = frac(rnd * 7.31 + 0.71), r3 = frac(rnd * 13.7 + 0.29);
                // обои: крем, мята, персик, голубой, сирень, оливка
                half3 pal[6] = { half3(0.93h, 0.87h, 0.74h), half3(0.74h, 0.88h, 0.80h), half3(0.95h, 0.78h, 0.66h),
                                 half3(0.72h, 0.82h, 0.93h), half3(0.84h, 0.76h, 0.92h), half3(0.80h, 0.82h, 0.62h) };
                half3 wall = pal[min(5, (int)(r1 * 6.0))];
                half3 floorC = lerp(half3(0.42h, 0.28h, 0.18h), half3(0.62h, 0.46h, 0.30h), (half)r2);
                half3 ceilC = half3(0.93h, 0.91h, 0.87h);
                half3 furn = lerp(half3(0.35h, 0.22h, 0.30h), half3(0.20h, 0.32h, 0.42h), (half)r3) * 1.1h;
                half3 c;
                if (t >= tz - 1e-3)
                {
                    c = wall * 0.9h;                                                     // задняя стена
                    float fx0 = 0.12 + 0.35 * r2, fw = 0.3 + 0.25 * r3;
                    if (hn.y < 0.3 && hn.x > fx0 && hn.x < fx0 + fw) c = furn * (hn.y > 0.24 ? 1.15h : 1.0h);   // диван / комод
                    float px = 0.15 + 0.55 * r1;
                    if (hn.y > 0.5 && hn.y < 0.72 && hn.x > px && hn.x < px + 0.16)
                        c = lerp(half3(0.95h, 0.75h, 0.35h), half3(0.35h, 0.65h, 0.85h), (half)r3);           // картина
                    if (r3 > 0.6 && hn.x > 0.82 && hn.y < 0.85) c = furn * 0.8h * (0.85h + 0.15h * (half)step(0.5, frac(hn.y * 7.0)));   // шкаф/полки
                }
                else if (t >= ty - 1e-3)
                {
                    if (d.y > 0.0) c = ceilC;                                            // потолок
                    else c = floorC * (0.86h + 0.14h * (half)step(0.5, frac(hn.x * 5.0 + hn.z * 0.3)));   // пол в доски
                }
                else
                {
                    c = wall * 0.72h;                                                    // боковые стены темнее
                    if (r2 > 0.55 && hn.z > 0.35 && hn.z < 0.6 && hn.y < 0.78) c = lerp(furn, half3(0.55h, 0.40h, 0.28h), 0.6h);   // дверь
                }
                c *= lerp(1.0h, 0.62h, (half)saturate(hn.z));                            // в глубине темнее
                lampK = (half)(saturate(1.0 - length(float2(hn.x - 0.5, (hn.z - 0.45) * 1.3)) * 1.25) * (0.55 + 0.45 * hn.y));
                return c;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS);
                half3 v = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                half night = saturate(_CC_Night);
                float2 fcM = CC_FacadeCoord(i.positionWS, n);               // фасад в метрах

                // ── окно: свои координаты 0..1 + случайное ──
                bool own = i.info.y > 0.5;
                float2 wuv; float rnd; float2 cell;
                float yKey = fcM.y;                                           // высота (для офисной полосы)
                float gradV;                                                  // градиент — по всему куску стекла, без швов
                if (own)
                {
                    wuv = saturate(i.wuv);
                    rnd = i.info.x;
                    // ключ — случайное ОКНА: разметка (кнопка «Починить окна») склеивает стёкла одного окна
                    // в одно число, так окно горит целиком, всей поверхностью, без своих «рам» поверх настоящих
                    cell = float2(rnd * 1000.0, 0);
                    gradV = wuv.y;
                    if (CC_GlassEar(i.wuv)) discard;                         // верхние углы за аркой рамы
                }
                else
                {
                    float2 cs = i.info.w > 0.5 ? _BigCell.xy : _CellSize.xy;  // размеченное, но большое — крупная сетка
                    float2 fc = (fcM - float2(0.0, _CC_GroundY)) / max(cs, 0.05);  // этажи — от земли
                    cell = floor(fc);
                    wuv = frac(fc);                                           // клетки — только для ночных окон
                    rnd = CC_Hash21(cell + i.info.z);
                    gradV = i.info.w > 0.5 ? saturate(i.wuv.y) : 0.55;        // размечено — вдоль всего фасада
                }

                // ── день: мультяшный градиент + отражение + блики + тень рамы ──
                half3 col = lerp(_GlassBottom.rgb, _GlassTop.rgb, (half)gradV);
                half3 r = reflect(-v, n);
                half3 sky = lerp(_HorizonColor.rgb, _SkyColor.rgb, saturate(r.y * 2.0h + 0.3h));
                sky *= 1.0h - saturate(_GlobalCloudDarkness) * 0.5h;
                half fres = pow(1.0h - saturate(dot(n, v)), _FresnelPower);
                col = lerp(col, sky, saturate(_ReflMix * (0.4h + fres)));

                half stripes;
                if (own)
                {
                    half s = (half)(wuv.x + wuv.y * _StripeSlant) / (1.0h + _StripeSlant);
                    half c1 = 0.32h + (half)rnd * 0.25h;
                    stripes = Stripe(s, c1, _StripeWidth) + 0.6h * Stripe(s, c1 + _StripeWidth * 2.4h, _StripeWidth * 0.45h);
                    half top = smoothstep(1.0h - _FrameShadow, 1.0h, (half)wuv.y);      // тень от рамы сверху
                    half side = smoothstep(_FrameShadow * 0.6h, 0.0h, (half)wuv.x);     // и сбоку
                    col *= 1.0h - max(top, side) * 0.35h;
                }
                else
                {
                    // сплошной фасад: крупные блики через весь фасад, без швов
                    half s = (half)frac((fcM.x + fcM.y * _StripeSlant) / max(_BigStripe, 0.5));
                    stripes = Stripe(s, 0.5h, _StripeWidth * 0.8h) + 0.5h * Stripe(s, 0.5h + _StripeWidth * 2.0h, _StripeWidth * 0.35h);
                }
                col += _StripeColor.rgb * saturate(stripes) * _StripeStrength * (1.0h - night);

                // яркость: мультяшно ярко, но вместе со сценой (закат, тучи, ночь — темнее)
                half shLum = dot(SampleSH(half3(0, 1, 0)), half3(0.333h, 0.333h, 0.333h));
                half sun = dot(_MainLightColor.rgb, half3(0.333h, 0.333h, 0.333h)) * saturate(dot(n, _MainLightPosition.xyz) * 0.5h + 0.5h);
                half level = saturate(shLum * 1.4h + sun * 0.35h);
                col *= _Brightness * lerp(1.0h, level, _SceneFollow);

                // ── комнаты за окнами ──
                float mtrR = _CC_Meter > 0.0 ? _CC_Meter : 3.5;
                float2 csR = i.info.w > 0.5 ? _BigCell.xy : _CellSize.xy;
                float2 uvC = own ? i.wuv : (fcM - float2(0.0, _CC_GroundY)) / max(csR, 0.05);   // непрерывные координаты окна
                float3 dpx = ddx(i.positionWS), dpy = ddy(i.positionWS);
                float2 dux = ddx(uvC), duy = ddy(uvC);
                float3 Tg = dpx * duy.y - dpy * dux.y;                                    // касательная «вправо по окну»
                float3 Bg = dpy * dux.x - dpx * duy.x;                                    // «вверх по окну»
                half roomK = 0.0h, roomLamp = 0.0h;
                half3 roomAlb = 0;
                bool officeBig = _CC_Office > 0.5 && ((!own && i.info.w > 0.5) || i.bigWin.x > 0.5);
                if (_Interior > 0.001h && !officeBig && abs(n.y) < 0.5h && dot(Tg, Tg) > 1e-12 && dot(Bg, Bg) > 1e-12)
                {
                    float distM = distance(i.positionWS, _WorldSpaceCameraPos.xyz) / mtrR;
                    roomK = _Interior * (half)(1.0 - smoothstep(_RoomFar * 0.6, max(_RoomFar, 1.0), distM));
                    if (roomK > 0.001h)
                    {
                        float3 vin = -(float3)v;
                        float3 vts = float3(dot(vin, normalize(Tg)), dot(vin, normalize(Bg)), dot(vin, -(float3)n));
                        float2 winM = own ? (i.bigWin.y > 0.01 ? float2(i.bigWin.y, i.bigWin.z) / mtrR : float2(1.5, 2.0)) : csR;
                        roomAlb = RoomColor(wuv, vts, winM, rnd, roomLamp);
                        // шторы по бокам и жалюзи у части окон — в плоскости окна
                        half cr = (half)frac(rnd * 17.3);
                        half3 curtC = lerp(half3(0.85h, 0.35h, 0.30h), half3(0.95h, 0.88h, 0.70h), (half)frac(rnd * 5.9));
                        float cwL = 0.06 + 0.16 * frac(rnd * 23.1), cwR = 0.06 + 0.16 * frac(rnd * 29.7);
                        half curt = (half)((wuv.x < cwL || wuv.x > 1.0 - cwR) ? 1.0 : 0.0) * step(0.25h, cr) * _Curtains;
                        roomAlb = lerp(roomAlb, curtC * (0.85h + 0.15h * (half)sin(wuv.x * 70.0)), curt);
                        half blinds = step(0.8h, cr) * (half)step(1.0 - 0.35 * frac(rnd * 41.0), wuv.y) * _Curtains;
                        roomAlb = lerp(roomAlb, half3(0.9h, 0.88h, 0.82h) * (0.8h + 0.2h * (half)step(0.5, frac(wuv.y * 18.0))), blinds);
                        // днём комната видна сквозь стекло: тусклая, поверх — отражение и блики
                        half glassK = saturate(0.42h + fres * 0.7h + saturate(stripes) * 0.6h);
                        half3 dayRoom = roomAlb * level * 0.55h * _Brightness;
                        col = lerp(col, dayRoom, roomK * (1.0h - glassK) * (1.0h - night));
                    }
                }

                // ── ночь ──
                // большое стекло (лента этажа / сплошной фасад): своя офисная сетка с переплётами, окна горят группами
                // офисная сетка — только у небоскрёбов с компонентом офиса; у остальных домов — прежняя разметка
                bool bigGlass = _CC_Office > 0.5 && ((!own && i.info.w > 0.5) || i.bigWin.x > 0.5);
                float mtrF = _CC_Meter > 0.0 ? _CC_Meter : 3.5;
                float2 ncell = cell; half nrnd = (half)rnd; half frameK = 0.0h; half ngrad = (half)wuv.y;
                half lit;
                if (bigGlass)
                {
                    float2 ocs = float2(_OfficeCell.x * mtrF, _CC_FloorH > 0.01 ? _CC_FloorH : _OfficeCell.y * mtrF);   // этаж — из модели
                    float2 oc = (fcM - float2(0.0, _CC_GroundY)) / max(ocs, 0.5);
                    ncell = floor(oc) + float2(floor(i.info.z * 97.0) * 13.0, 0.0);
                    nrnd = (half)CC_Hash21(ncell + 0.37);
                    float2 fo = frac(oc);
                    frameK = (half)max(step(fo.x, _OfficeFrame), step(fo.y, _OfficeFrame * 1.4));
                    ngrad = (half)fo.y;
                    lit = CC_OfficeCellLit(ncell, i.info.z);
                }
                else lit = CC_WindowLit(cell, i.info.z);
                half band;
                if (_CC_Office > 3.5)
                {
                    // свой стиль небоскрёба: этажи целиком / окна вразнобой + верх
                    float yFloor = yKey;
                    if (own && i.bigWin.z < _CC_FloorH * 1.6) yFloor = yKey - wuv.y * i.bigWin.z + i.bigWin.z * 0.5;   // лента — по её середине
                    float2 key = (own && !bigGlass) ? cell : ncell;
                    lit = CC_TowerLit(yKey, yFloor, key, i.info.z, (half)_CC_Office, _CC_TopY, _CC_BandH, _CC_FloorH, band);
                }
                else lit = CC_OfficeLit(lit, yKey, ncell, i.info.z, bigGlass ? 0.0h : (half)_CC_Office, _CC_TopY, _CC_BandH, band);
                half cold = step(frac(nrnd * 5.17h), _ColdShare * 0.4h) * (1.0h - band);   // синих — меньше
                half3 lamp = lerp(_NightColor.rgb, _NightColor2.rgb, cold) * lerp(0.6h + 0.8h * frac(nrnd * 3.3h), 1.1h, band);
                lamp *= lerp(half3(1, 1, 1), CC_WindowTint(frac(nrnd * 11.3h), frac(nrnd * 2.7h)), _Palette * (1.0h - cold));   // разные оттенки
                if (bigGlass) lamp *= _OfficeBright * (0.65h + 0.35h * frac(nrnd * 7.1h));
                half3 nightOn = lamp * (0.7h + 0.3h * ngrad) * (1.0h - frameK * 0.75h);
                // особые окна: фитолампа / вечеринка (только ближний слой)
                half3 spCol;
                half sp = CC_SpecialWindow((own && !bigGlass) ? cell : ncell, i.info.z, i.positionWS, wuv, spCol);
                if (sp > 0.5h) { lit = 1.0h; nightOn = spCol * (1.0h - frameK * 0.75h); }

                half3 nightOff = _NightDark.rgb + sky * fres * 0.08h;
                if (roomK > 0.001h)
                {
                    // ночью горит КОМНАТА: обои, мебель, свет люстры; негорящие — едва видны
                    half3 roomOn = roomAlb * lamp * (0.35h + 0.95h * roomLamp) + lamp * 0.05h;
                    if (sp > 0.5h) roomOn = lerp(roomOn, nightOn, 0.6h);                  // фитолампа/вечеринка — их цвет главнее
                    nightOn = lerp(nightOn, roomOn, roomK * 0.9h);
                    nightOff = lerp(nightOff, nightOff + roomAlb * 0.025h, roomK);
                }
                col = lerp(col, lerp(nightOff, nightOn, lit), night);

                // ── дождь ──
                half wet = saturate(_CatWetness) * _RainAmount * (half)CC_WxMask(i.positionWS.xz);
                if (wet > 0.001h)
                {
                    half w = CC_RainOnGlass(fcM * 0.5, _Time.y * _RainSpeed) * wet;
                    col = lerp(col, col * 0.75h + sky * 0.35h * (1.0h - night * 0.8h) + lamp * lit * night * 0.15h, w);
                }

                col += (half3)_CC_Flash.rgb * (half)_CC_Flash.a * 0.45h;               // молния бликом в стекле
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            #include "CityCommon.hlsl"
            struct A { float4 positionOS : POSITION; float2 uv3 : TEXCOORD2; float4 uv4 : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 wuv : TEXCOORD0; nointerpolation float own : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vertDepth(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.wuv = v.uv3;
                o.own = (abs(v.uv4.z - 3.0) < 0.5 || abs(v.uv4.z - 1.0) < 0.5) ? 1.0 : 0.0;
                return o;
            }
            half fragDepth(V i) : SV_Target { if (i.own > 0.5 && CC_GlassEar(i.wuv)) discard; return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            #include "CityCommon.hlsl"
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv3 : TEXCOORD2; float4 uv4 : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; float2 wuv : TEXCOORD1; nointerpolation float own : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vertDN(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.wuv = v.uv3;
                o.own = (abs(v.uv4.z - 3.0) < 0.5 || abs(v.uv4.z - 1.0) < 0.5) ? 1.0 : 0.0;
                return o;
            }
            half4 fragDN(V i) : SV_Target { if (i.own > 0.5 && CC_GlassEar(i.wuv)) discard; return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
