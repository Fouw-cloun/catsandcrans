// CatCooks — общий код ветра для эффектов шторма (линии, мусор, пыль, пылинки). Пишет CityStorm.
//   _CC_Gust      x сила порыва (сколько эффектов), yz направление ветра (XZ)
//   _CC_GustP     x радиус вокруг точки взгляда, y длина линии, z толщина (ед.), w жизнь линии (с)
//   _CC_GustFocus xyz — куда смотрит камера (эффекты там, на том же этаже)
//   _CC_GustWave  x фронт волны порыва (координата вдоль ветра), y ширина полосы, z идёт ли волна, w доля «фона»
//   _CC_Motes     x пылинки (сила), y радиус коробки, z размер (ед.)
//   _CC_WindFx    x летающие предметы (пакеты, газеты…), y ветреность, z листья на пол, w тучки пыли
//   _CC_WindFx2   x тонна мусора (со всех сторон), y листья осенние, z листья летние, w листья ложатся (1) / тают в воздухе (0)
#ifndef CATCOOKS_CITY_WIND_INCLUDED
#define CATCOOKS_CITY_WIND_INCLUDED

float4 _CC_Gust, _CC_GustP, _CC_GustFocus, _CC_GustWave, _CC_Motes, _CC_WindFx, _CC_WindFx2;
float  _CC_SnowNow;          // идёт снег (0..1) — пыль в порывах становится снежной позёмкой (CityWinter)

float3x3 CW_RotAxis(float3 a, float ang)
{
    float s = sin(ang), c = cos(ang), t = 1.0 - c;
    return float3x3(t * a.x * a.x + c, t * a.x * a.y - s * a.z, t * a.x * a.z + s * a.y,
                    t * a.x * a.y + s * a.z, t * a.y * a.y + c, t * a.y * a.z - s * a.x,
                    t * a.x * a.z - s * a.y, t * a.y * a.z + s * a.x, t * a.z * a.z + c);
}

// Мягкое «включение» элемента: доля strength, без резкого появления/исчезновения
float CW_Gate(float strength, float h) { return saturate((strength - h) * 6.0); }

float3 CW_Dir()
{
    return normalize(float3(_CC_Gust.y, 0, _CC_Gust.z) + float3(1e-4, 0, 0));
}

// Волна порыва: фронт бежит по ветру, перед ним резко, позади — длинный хвост. 1 — эффектов полная доля.
float CW_Wave(float2 xz)
{
    if (_CC_GustWave.z < 0.5) return 1.0;
    float3 d3 = CW_Dir();
    float d = dot(xz, d3.xz) - _CC_GustWave.x;
    float W = max(_CC_GustWave.y, 1e-3);
    float band = d > 0.0 ? exp(-(d * d) / (W * W * 0.36)) : exp(d / (W * 1.6));
    return max(_CC_GustWave.w, band);
}

// Высота пола под точкой: пол зоны (тот же этаж, что и точка взгляда) или высота точки взгляда
float CW_Floor(float2 xz)
{
    float h;
    float m = _CC_RainMeter;
    if (CR_OccHeight(float3(xz.x, 0, xz.y), h) && abs(h - _CC_GustFocus.y) < 6.0 * m) return h;
    return _CC_GustFocus.y;
}

#endif
