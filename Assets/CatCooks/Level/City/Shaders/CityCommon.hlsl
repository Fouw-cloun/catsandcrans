// CatCooks — общий код шейдеров города (дома, стёкла): ночь для окон, хэши окон, дождь.
// Туман, свет, небо, день/ночь в проекте делает Atmosphere (Mood Director, Fog Controller — туман
// полноэкранный по глубине, на эти шейдеры ложится сам). Здесь только то, чего там нет:
//   _CC_Night / _CC_LitRatio / _CC_WindowFlicker — ставит CityAtmosphere (мост от CatCooksDayCycle.Night01);
//   _CatWetness — пишет CatCooksWeatherFX (дождь), _GlobalCloudDarkness — SkyController (тучи).
// Нет их на сцене — всё по нулям: день, сухо.
#ifndef CATCOOKS_CITY_COMMON_INCLUDED
#define CATCOOKS_CITY_COMMON_INCLUDED

half  _CC_Night;             // 0 — день, 1 — ночь
half  _CC_LitRatio;          // доля горящих окон ночью (0..1)
float _CC_GroundY;           // высота земли города (мир) — ставит CityAtmosphere
float _CC_Meter;             // единиц сцены в метре (0 — 3.5)
half  _CC_WindowFlicker;     // как часто окна переключаются (в секунду, доля)
float4 _CC_Apt;              // xy — «квартира» в единицах сцены, z — доля поздних окон в офисах, w — доля горящих у небоскрёбов (от жилых)
#ifndef CATCOOKS_WET_INCLUDED
float _CatWetness;           // 0..1 — дождь (CatCooksWeatherFX)
#endif
half  _GlobalCloudDarkness;  // 0..1 — тучи (SkyController)
float4 _CC_Flash;            // вспышка молнии: rgb цвет, a сила (0 — нет) — ставит CityStorm
float4 _CC_FlashDir;         // xyz — направление на молнию (мир)
float  _CC_SnowCover;        // снег лёг (0..1) — ставит CityWinter; верхние грани белеют пятнами → сплошь
float4 _CC_SpecialZone;      // особые окна: xy — центр (XZ), z — радиус (0 — везде), w — сколько (0 — нет) — ставит CityAtmosphere

// ЗОНЫ ПОГОДЫ (CityWeatherArea, C#): красные (полная погода) и жёлтые (лёгкая) коробки по XZ. Вне них осадков, мокрого, льда,
// града и жары нет — остаются туман и облака. _CC_WxN = 0 — зон нет, погода везде (как раньше).
float4 _CC_WxBox[8];         // xy — центр XZ, zw — полуразмер
float4 _CC_WxInfo[8];        // x — вид (1 красная, 2 жёлтая), y — сила жёлтой, z — плавный край (ед. сцены)
float  _CC_WxN;              // сколько зон
float4 _CC_WxCam;            // x — сила погоды у камеры, y — доля «красной» у камеры
float  _CC_WxLite;           // 1 — в жёлтой зоне экранный проход облегчённый

// x — общая сила погоды в точке (0..1), y — доля полной (красной) погоды (0..1)
float2 CC_WxMask2(float2 p)
{
    if (_CC_WxN < 0.5) return float2(1.0, 1.0);
    float2 r = float2(0.0, 0.0);
    for (int i = 0; i < 8; i++)
    {
        if (i >= (int)_CC_WxN) break;
        float2 d = abs(p - _CC_WxBox[i].xy) - _CC_WxBox[i].zw;
        float o = max(d.x, d.y);
        float k = 1.0 - smoothstep(0.0, max(_CC_WxInfo[i].z, 1e-3), o);
        if (_CC_WxInfo[i].x < 1.5) { r.x = max(r.x, k); r.y = max(r.y, k); }
        else r.x = max(r.x, k * _CC_WxInfo[i].y);
    }
    return r;
}
float CC_WxMask(float2 p) { return CC_WxMask2(p).x; }

// Подсветка вспышкой молнии: грани, смотрящие на разряд, светятся сильнее
half3 CC_Flash(half3 albedo, half3 n)
{
    if (_CC_Flash.a <= 0.001) return 0;
    half k = 0.3h + 0.7h * saturate(dot(n, (half3)_CC_FlashDir.xyz) * 0.5h + 0.5h);
    return albedo * (half3)_CC_Flash.rgb * (half)_CC_Flash.a * k;
}

float CC_Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float CC_Hash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

// Координаты на фасаде по нормали: (горизонталь вдоль стены, высота).
float2 CC_FacadeCoord(float3 posWS, float3 nWS)
{
    float3 n = nWS; n.y = 0;
    float len2 = dot(n, n);
    n = len2 > 1e-4 ? n * rsqrt(len2) : float3(0, 0, 1);
    float3 t = float3(n.z, 0, -n.x);
    return float2(dot(posWS, t), posWS.y);
}

// Горит ли окно в клетке (0/1); ~30% окон время от времени переключаются. seed — от объекта.
half CC_WindowLit(float2 cell, float seed)
{
    float h = CC_Hash21(cell + seed);
    float slot = floor(_Time.y * max(_CC_WindowFlicker, 1e-4) * 1.8 + h * 7.0);
    float h2 = CC_Hash21(cell * 1.37 + seed + slot * 0.618);
    float pick = lerp(h, h2, step(0.7, h));                 // ~30% окон время от времени включаются / выключаются
    return step(pick, _CC_LitRatio);
}

// Офисная сетка на большом стекле (лента этажа, сплошной фасад): окна горят ГРУППАМИ — «отдел» 3×2 окна
// занят (горит почти весь) или пуст; внутри занятого — отдельные окна гаснут и включаются. Естественные пятна,
// без «вся сторона горит / вся тёмная».
half CC_OfficeCellLit(float2 cell, float seed)
{
    float2 grp = floor(cell / float2(3.0, 2.0));
    half busy = step(CC_Hash21(grp * 1.31 + seed + 3.7), saturate((half)_CC_LitRatio * 1.15h));
    half one = step(CC_Hash21(cell * 0.73 + seed + 9.1), 0.78h);
    float slot = floor(_Time.y * max(_CC_WindowFlicker, 1e-4) * 0.5 + CC_Hash21(cell + seed) * 9.0);
    half flick = step(0.12h, CC_Hash21(cell * 1.9 + seed + slot * 0.618));      // изредка гаснут
    half lone = step(CC_Hash21(cell * 2.9 + seed + 1.3), 0.06h);                   // одиночные «засидевшиеся»
    return max(busy * one * flick, lone);
}

// Клетка «квартиры»: этажи от земли, окна одной квартиры — одно состояние (без половинок и «кубиков»).
float2 CC_AptCell(float2 fc)
{
    float2 apt = _CC_Apt.x > 0.01 ? _CC_Apt.xy : float2(21.0, 11.2);
    fc.y -= _CC_GroundY;
    return floor(fc / apt);
}

// Офис (небоскрёб) — режим с рендерера (CityOfficeTower, MaterialPropertyBlock), жилые дома — как есть:
//   1 — горит только верхняя полоса, ниже редкие «засидевшиеся» окна;
//   2 — частично: часть окон (доля _CC_Apt.w от жилых), включаются и выключаются как у жилых;
//   3 — верхняя полоса горит вся, ниже — частично (ориентиры, космическая башня).
half CC_OfficeLit(half lit, float y, float2 cell, float seed, half office, float topY, float bandH, out half band)
{
    band = 0;
    if (office < 0.5h) return lit;
    half share = _CC_Apt.w > 0.001 ? (half)_CC_Apt.w : 0.6h;
    half partial = lit * step(CC_Hash21(cell * 2.3 + seed + 11.0), share);
    if (office > 1.5h && office < 2.5h) return partial;
    band = step(topY - bandH, y) * step(y, topY + 0.01);
    if (office > 2.5h) return max(band, partial);
    half late = step(CC_Hash21(cell * 1.7 + seed + 5.0), _CC_Apt.z);
    return max(band, late);
}

float CC_VNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(CC_Hash21(i), CC_Hash21(i + float2(1, 0)), f.x), lerp(CC_Hash21(i + float2(0, 1)), CC_Hash21(i + float2(1, 1)), f.x), f.y);
}

// Снег на верхних гранях (крыши, карнизы, верх пропсов): ny — нормаль.y, p — координаты в метрах (XZ). 0..1
half CC_SnowMask(half ny, float2 p)
{
    if (_CC_SnowCover <= 0.001) return 0;
    half upk = smoothstep(0.55h, 0.8h, ny);
    if (upk <= 0.001h) return 0;
    float sn = CC_VNoise(p / 1.4) * 0.55 + CC_VNoise(p / 0.35) * 0.45;
    float thr = 1.05 - _CC_SnowCover * 1.3;
    return upk * (half)saturate((sn - thr) / 0.05 + 0.5);
}

half3 CC_SnowApply(half3 col, half s, half3 light)
{
    return lerp(col, half3(0.93h, 0.96h, 1.0h) * light * 1.05h, s);
}

// «Ушки» стекла: у некоторых моделей прямоугольник стекла больше арочного проёма рамы — верхние углы торчат
// над аркой. Обрезаем верхние углы окна (суперэллипс, ~15% по углу). wuv — координаты внутри окна 0..1.
bool CC_GlassEar(float2 wuv)
{
    if (wuv.y < 0.5) return false;
    float2 q = float2(abs(wuv.x * 2.0 - 1.0), (wuv.y - 0.5) * 2.0);
    q = saturate(q);
    float2 q2 = q * q;
    return q2.x * q2.x * q2.x + q2.y * q2.y * q2.y > 1.0;
}

// Палитра света в окнах: тёплый белый, янтарь, жёлтый, персиковый, мятно-белый, изредка холодный белый.
// h — какой оттенок, h2 — яркость. Множитель к цвету окна материала.
half3 CC_WindowTint(float h, float h2)
{
    half3 c = h < 0.28 ? half3(1.0h, 0.93h, 0.78h)        // тёплый белый
            : h < 0.48 ? half3(1.0h, 0.77h, 0.45h)        // янтарь
            : h < 0.64 ? half3(1.0h, 0.9h, 0.58h)         // жёлтый
            : h < 0.78 ? half3(1.0h, 0.76h, 0.68h)        // персиковый
            : h < 0.9  ? half3(0.93h, 1.0h, 0.82h)        // мятно-белый
            : h < 0.96 ? half3(1.0h, 0.85h, 0.85h)        // розоватый
            :            half3(0.88h, 0.93h, 1.0h);        // холодный белый (редко)
    return c * (0.85h + 0.25h * (half)h2);
}

// Особые окна (редкие, только в зоне _CC_SpecialZone): 1 — фитолампа (фиолетовая), 2 — вечеринка
// (цвет меняется ~2 раза в секунду: оранжевый, бирюзовый, синий, фиолетовый, розовый; по окну бегут косые блики).
// key — ключ окна, wuv — координаты внутри окна (0..1; нет — передай 0.5). col — цвет света.
half CC_SpecialWindow(float2 key, float seed, float3 posWS, float2 wuv, out half3 col)
{
    col = 0;
    if (_CC_SpecialZone.w <= 0.0) return 0;
    if (_CC_SpecialZone.z > 0.0 && distance(posWS.xz, _CC_SpecialZone.xy) > _CC_SpecialZone.z) return 0;
    float h = CC_Hash21(key * 3.17 + seed + 21.7);
    float pGrow = 0.0035 * _CC_SpecialZone.w, pParty = 0.003 * _CC_SpecialZone.w;
    if (h < pGrow)
    {
        col = half3(0.82h, 0.28h, 1.0h) * (half)(1.25 + 0.1 * sin(_Time.y * 0.6 + h * 500.0));
        return 1;
    }
    if (h < pGrow + pParty)
    {
        float t = _Time.y * 2.2 + h * 3100.0;
        float k = floor(t);
        int ci = (int)fmod(k, 5.0);
        half3 c = ci == 0 ? half3(1.0h, 0.45h, 0.15h) : ci == 1 ? half3(0.15h, 1.0h, 0.8h) : ci == 2 ? half3(0.25h, 0.45h, 1.0h)
                : ci == 3 ? half3(0.75h, 0.3h, 1.0h) : half3(1.0h, 0.3h, 0.8h);
        // мягкие косые блики бегут по стеклу (без квадратных «искр»)
        float bs = frac((wuv.x * 1.4 + wuv.y) * 1.6 - _Time.y * 1.9 + h * 70.0);
        half beam = (half)(smoothstep(0.55, 0.7, bs) * (1.0 - smoothstep(0.82, 0.95, bs)));
        col = c * (1.1h + 0.8h * beam);
        return 2;
    }
    return 0;
}

// Свой стиль окон небоскрёба (CityOfficeTower): office 4 — ЭТАЖИ целиком + верх, 5 — ОКНА вразнобой + верх.
// y — высота точки (мир), yFloor — высота для выбора этажа (у ленты стекла — её середина, чтобы этаж не резался),
// key — ключ окна (своё окно или клетка), floorH — высота этажа (мир), bandH — сколько сверху горит всегда (мир).
half CC_TowerLit(float y, float yFloor, float2 key, float seed, half office, float topY, float bandH, float floorH, out half band)
{
    float fh = max(floorH, 0.05);
    float fl = floor((topY - yFloor) / fh);
    band = step(topY - bandH, yFloor) * step(yFloor, topY + 0.01) * step(0.001, bandH);
    half share = _CC_Apt.w > 0.001 ? (half)_CC_Apt.w : 0.6h;
    half ratio = saturate((half)_CC_LitRatio * (0.6h + share));
    float sp = max(_CC_WindowFlicker, 1e-4);
    half lit;
    if (office < 4.5h)
    {
        // этаж горит весь (со всех сторон) или нет; изредка этажи переключаются
        float h = CC_Hash21(float2(fl, seed * 0.173) + 4.1);
        float slot = floor(_Time.y * sp * 0.35 + h * 9.0);
        float h2 = CC_Hash21(float2(fl * 1.37, seed + slot * 0.618));
        lit = step(lerp(h, h2, step(0.8, h)), ratio);
    }
    else
    {
        float h = CC_Hash21(key * 1.13 + seed + 2.3);
        float slot = floor(_Time.y * sp + h * 7.0);
        float h2 = CC_Hash21(key * 1.71 + seed + slot * 0.618);
        lit = step(lerp(h, h2, step(0.82, h)), ratio);
    }
    return max(lit, band);
}

// Капли и подтёки на вертикальном стекле: 0..1 (сила «воды» в точке). uv — координаты стены в метрах.
half CC_RainOnGlass(float2 uv, float t)
{
    // подтёки: узкие полосы, ползут вниз с разной скоростью
    float col = floor(uv.x * 6.0);
    float r = CC_Hash21(float2(col, 7.1));
    float y = uv.y * 1.5 + t * (0.6 + r * 1.4) + r * 13.0;
    float fx = frac(uv.x * 6.0) - 0.5;
    half streak = smoothstep(0.08, 0.0, abs(fx + sin(uv.y * 3.0 + r * 6.0) * 0.12)) * smoothstep(0.0, 0.3, frac(y)) * step(0.45, r);
    // точки-капли
    float2 g = uv * float2(9.0, 7.0);
    float2 gi = floor(g);
    float2 gf = frac(g) - 0.5;
    float rr = CC_Hash21(gi);
    half drop = smoothstep(0.18, 0.05, length(gf - (rr - 0.5) * 0.5)) * step(0.55, rr);
    return saturate(streak + drop);
}

#endif
