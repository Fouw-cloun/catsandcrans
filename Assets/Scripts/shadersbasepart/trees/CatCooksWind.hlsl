// ============================================================================
//  CatCooksWind.hlsl
//  Общий ветер для кроны и ствола. Оба шейдера подключают этот файл,
//  поэтому дерево гнётся целиком, а не разъезжается на части.
//
//  Кладётся рядом с шейдерами: Assets/Shaders/CatCooksWind.hlsl
//
//  Глобальные параметры пишет CatCooksWindController.cs — один на сцену.
//  Если контроллера нет, работают дефолты ниже.
// ============================================================================
#ifndef CATCOOKS_WIND_INCLUDED
#define CATCOOKS_WIND_INCLUDED

// ---- глобальные (Shader.SetGlobalX из CatCooksWindController) ----
float4 _CatWindDir;        // xyz = направление в мире, w не используется
float  _CatWindStrength;   // общая сила, 0..2
float  _CatWindSpeed;      // общая скорость, 0..3
float  _CatGustScale;      // размер волн порыва по миру
float  _CatGustSpeed;      // скорость бега волн

// Позиция объекта в мире — уникальна для каждого дерева.
// Из неё делаем фазу, чтобы соседние деревья не качались синхронно.
float3 CatObjectOriginWS()
{
    return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
}

// Дешёвый хэш 0..1 из позиции. Тот же вход -> тот же выход,
// поэтому вариация стабильна между кадрами.
float CatHash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

// Две несинхронные синусоиды. Период в разы длиннее, чем у одной,
// поэтому качание не читается как повтор.
float CatSway(float t)
{
    return sin(t) * 0.6 + sin(t * 1.7 + 1.3) * 0.4;
}

// ============================================================================
//  ГЛАВНЫЙ ИЗГИБ — работает и на стволе, и на кроне.
//
//  positionWS  — вершина в мире
//  originWS    — начало координат объекта в мире (низ ствола)
//  heightMask  — 0 у корня, 1 у макушки (обычно vertex color B или y/высота)
//  stiffness   — множитель материала: у ствола ~0.35, у кроны 1.0
// ============================================================================
float3 CatWindMainBend(float3 positionWS, float3 originWS, float heightMask, float stiffness)
{
    float3 wdir = normalize(_CatWindDir.xyz + float3(0.0001, 0, 0));

    // фаза от позиции дерева -> каждое дерево живёт своей жизнью
    float phase = dot(originWS, float3(0.7, 0.13, 0.51));

    // порыв: волна, бегущая по всему лесу вдоль направления ветра
    float gust = sin(dot(originWS.xz, wdir.xz) * _CatGustScale - _Time.y * _CatGustSpeed);
    gust = gust * 0.5 + 0.5;                       // 0..1
    gust = lerp(0.55, 1.45, gust);                 // разброс силы между зонами

    float t = _Time.y * _CatWindSpeed + phase;
    float sway = CatSway(t);

    // квадрат маски = естественная дуга изгиба (низ почти не двигается)
    float amount = heightMask * heightMask * _CatWindStrength * stiffness * gust;

    float3 offset = wdir * sway * amount;

    // макушку слегка тянет вниз при сильном отклонении — стебель не растягивается
    offset.y -= abs(sway) * amount * 0.25;

    return offset;
}

// ============================================================================
//  ТРЕПЫХАНИЕ КЛАСТЕРА — только для кроны.
//  Каждый билборд-квад дрожит вокруг своего центра со своей фазой.
//
//  pivotWS   — центр квада в мире
//  mask      — vertex color R (где листва мягче)
// ============================================================================
float3 CatWindFlutter(float3 pivotWS, float mask, float speed, float strength)
{
    float phase = dot(pivotWS, float3(1.31, 0.77, 1.13));
    float t = _Time.y * speed * max(_CatWindSpeed, 0.15) + phase;

    // три оси с разными частотами -> движение не плоское
    float3 wob = float3(sin(t), sin(t * 1.43 + 2.1) * 0.6, sin(t * 0.87 + 4.7));

    return wob * strength * mask * _CatWindStrength;
}

#endif
