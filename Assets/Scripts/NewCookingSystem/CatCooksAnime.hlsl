// ============================================================================
//  CatCooks / Anime — общий «аниме-свет» для еды и веществ (v23.4)
//
//  Подключается в шейдерах после Lighting.hlsl:  #include "CatCooksAnime.hlsl"
//
//  Как в аниме-рисовке (слои «сэндвича»):
//    1. База — плоский цвет продукта;
//    2. Тень — НЕ серая: светлое уходит в холодный голубовато-сиреневый, тёплое (шоколад, карамель,
//       мёд) — в насыщенный тёмно-тёплый. Граница тени — ступенька (мягкость задаётся);
//    3. Свет — третья ступенька, чуть теплее базы;
//    4. «Живописные пятна» — крупные мягкие пятна тона (как у Ghibli-текстур), 2 ступени;
//    5. Каустика (вода) — световая сетка из двух ячеистых шумов, порог — чёткие линии;
//    6. Контурный свет (рим) и резкий блик — ступенькой, белым.
//  Всё процедурно, без текстур.
// ============================================================================
#ifndef CATCOOKS_ANIME_INCLUDED
#define CATCOOKS_ANIME_INCLUDED

float CCA_Hash21(float2 p) { p = frac(p * float2(233.34, 851.73)); p += dot(p, p + 23.45); return frac(p.x * p.y); }
float2 CCA_Hash22(float2 p) { float n = CCA_Hash21(p); return float2(n, CCA_Hash21(p + n + 31.7)); }
float CCA_Noise(float2 p)
{
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(CCA_Hash21(i), CCA_Hash21(i + float2(1, 0)), f.x),
                lerp(CCA_Hash21(i + float2(0, 1)), CCA_Hash21(i + float2(1, 1)), f.x), f.y);
}
float CCA_Fbm(float2 p)
{
    float s = 0, a = 0.55;
    [unroll] for (int k = 0; k < 3; k++) { s += CCA_Noise(p) * a; p = p * 2.07 + float2(5.1, 2.3); a *= 0.5; }
    return s / 0.9625;
}
// F1 и F2 ячеистого шума (для линий каустики: F2 − F1 мало = граница ячеек)
float2 CCA_Cells2(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float d1 = 8, d2 = 8;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(x, y);
        float2 o = CCA_Hash22(i + g) * 0.8 + 0.1;
        float d = length(g + o - f);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
    }
    return float2(d1, d2);
}

// Цвет тени как множитель к цвету продукта: светлое → холодная сиреневая, тёплое → насыщенная тёплая.
half3 CCA_ShadowMul(half3 baseC, half depth)
{
    half warm = saturate((baseC.r - baseC.b) * 1.8h);
    half3 cool = half3(0.68h, 0.72h, 0.94h);                 // молоко, сливки, мука, белок
    half3 warmT = half3(0.80h, 0.58h, 0.50h);                // шоколад, карамель, мёд, тесто
    half3 m = lerp(cool, warmT, warm);
    return m * lerp(1.0h, 0.9h, depth);
}

// Ступенчатый свет: множитель к цвету продукта. soft — мягкость ступени (0.01 резко … 0.12 мягко), bands 2 или 3.
half3 CCA_Cel(half3 baseC, float3 N, Light L, half soft, half bands, half depth)
{
    half ndl = dot(N, L.direction) * 0.5h + 0.5h;
    half cast = smoothstep(0.35h, 0.65h, L.shadowAttenuation);
    half b1 = smoothstep(0.45h - soft, 0.45h + soft, ndl) * cast;
    half3 m = lerp(CCA_ShadowMul(baseC, depth), half3(1, 1, 1), b1);
    if (bands > 2.5h)
    {
        half b2 = smoothstep(0.8h - soft, 0.8h + soft, ndl) * cast;
        m = lerp(m, half3(1.07h, 1.05h, 1.0h), b2 * 0.85h);
    }
    half3 lc = min(L.color, half3(1.25h, 1.25h, 1.25h));
    return m * lerp(half3(1, 1, 1), lc, 0.6h);
}

// Крупные «живописные» пятна тона: −1..1, две ступени (не шум, а «мазки»).
half CCA_Paint(float2 p)
{
    float n = CCA_Fbm(p);
    return (smoothstep(0.56, 0.6, n) - smoothstep(0.42, 0.38, n)) * 0.5h;
}

// Каустика: чёткая световая сетка (две «сетки» едут навстречу), 0..1.
half CCA_Caustic(float2 p, float t)
{
    float2 a = CCA_Cells2(p + float2(t * 0.35, t * 0.2));
    float2 b = CCA_Cells2(p * 1.37 + float2(-t * 0.27, t * 0.31) + 7.7);
    half la = 1.0h - smoothstep(0.015h, 0.035h, a.y - a.x);   // тонкие линии
    half lb = 1.0h - smoothstep(0.015h, 0.04h, b.y - b.x);
    return saturate(la * 0.8h + lb * 0.45h);
}

// Контурный свет: ступенька по френелю. width 0.25 узко … 0.6 широко.
half CCA_Rim(float3 N, float3 V, half width)
{
    half f = 1.0h - saturate(dot(N, V));
    return smoothstep(1.0h - width, 1.0h - width + 0.04h, f);
}

// Резкий блик-пятно (Блинн, ступенькой). size 0.9 крупный … 0.995 точка.
half CCA_Spec(float3 N, float3 V, float3 Ld, half size)
{
    half nh = saturate(dot(N, normalize(Ld + V)));
    return smoothstep(size, size + 0.006h, nh);
}

// Пузырьки воздуха внутри (мёд, сироп): круг с тёмной обводкой и бликом. p — в «пузырьках на метр».
half2 CCA_Bubbles(float2 p)
{
    float2 i = floor(p), f = frac(p);
    half body = 0, hl = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(x, y);
        float2 h = CCA_Hash22(i + g);
        if (h.x > 0.35) continue;                              // не в каждой клетке
        float r = 0.08 + h.y * 0.16;
        float2 d = g + h * 0.7 + 0.15 - f;
        float l = length(d) / r;
        body = max(body, 1.0h - smoothstep(0.85h, 1.0h, l));
        hl = max(hl, step(length(d + float2(0.35, -0.35) * r) / r, 0.3) * 1.0h);
    }
    return half2(body, hl);
}

// «Окно»-блик: две дуги у края на стороне, ПРОТИВОПОЛОЖНОЙ свету (классика аниме-жидкости), с разрывами.
// r — доля до стенки 0..1, opp — насколько точка «напротив света» 0..1, ang — угол вокруг центра.
half CCA_ShineArcs(half r, half opp, float ang)
{
    half brk = step(0.32h, CCA_Noise(float2(ang * 3.0, 1.7)));
    half a1 = smoothstep(0.6h, 0.625h, r) * (1.0h - smoothstep(0.69h, 0.715h, r)) * smoothstep(0.5h, 0.75h, opp);
    half a2 = smoothstep(0.735h, 0.75h, r) * (1.0h - smoothstep(0.765h, 0.78h, r)) * smoothstep(0.65h, 0.85h, opp);
    return saturate(a1 + a2 * 0.8h) * brk;
}

// ── v23.5: «радикальный» аниме-слой (по мотивам кулинарного аниме) ──

// Плоские ступени тона: освещённый цвет → 3 «краски» (тень с цветным сдвигом / основа / свет).
// Белое и блики (пена, отсветы — ярче основы в 1.3 раза) не трогаем.
half3 CCA_Celize(half3 col, half3 baseC, half soft)
{
    half Lb = max(dot(baseC, half3(0.299h, 0.587h, 0.114h)), 0.02h);
    half L = dot(col, half3(0.299h, 0.587h, 0.114h));
    half r = L / Lb;
    half b1 = smoothstep(0.8h - soft, 0.8h + soft, r);
    half b2 = smoothstep(1.12h - soft, 1.12h + soft, r);
    half rp = lerp(0.74h, 1.0h, b1);
    rp = lerp(rp, 1.12h, b2);
    half keep = smoothstep(1.3h, 1.45h, r);
    rp = lerp(rp, r, keep);
    half3 o = col * (rp / max(r, 1e-3h));
    return o * lerp(CCA_ShadowMul(baseC, 0.0h), half3(1, 1, 1), max(b1, keep));
}

// Блёстки-«штрихи»: короткие изогнутые светлые чёрточки, плывут по поверхности (как на соусах в аниме).
// p — в «клетках» (координаты × штрихов на метр), dens 0..1 — доля клеток со штрихом.
half CCA_Flecks(float2 p, float t, half dens)
{
    float2 q = p + float2(0.12, 0.05) * t;
    float2 i = floor(q), f = frac(q);
    half m = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(x, y);
        float2 h = CCA_Hash22(i + g);
        if (h.x >= dens) continue;
        float2 h2 = CCA_Hash22(i + g + 5.3);
        float2 d = f - (g + 0.2 + h2 * 0.6);
        float ang = 0.6 + (h.y - 0.5) * 1.2;
        float dx = cos(ang), dy = sin(ang);
        float al = d.x * dx + d.y * dy;
        float pe = -d.x * dy + d.y * dx - 0.9 * al * al;   // дугой
        float l = 0.25 + h2.x * 0.3, w = 0.035 + h2.y * 0.03;
        float wt = w * saturate(1.0 - (al / l) * (al / l));
        m = max(m, (1.0h - smoothstep(wt * 0.6, wt + 1e-4, abs(pe))) * step(abs(al), l));
    }
    return m;
}

// Поры «рыхлого» теста/пасты: овальные дырочки (тёмные) со светлым нижним краем. x — дырка, y — светлый край.
half2 CCA_Pores(float2 p)
{
    float2 i = floor(p), f = frac(p);
    half hole = 0, rim = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(x, y);
        float2 h = CCA_Hash22(i + g);
        if (h.x >= 0.55) continue;
        float2 h2 = CCA_Hash22(i + g + 9.1);
        float r = 0.1 + h2.x * 0.2;
        float2 d = (g + 0.2 + h * 0.6 - f) * float2(1.0, 1.35);
        float l = length(d) / r;
        hole = max(hole, 1.0h - smoothstep(0.8h, 1.0h, l));
        if (d.y < 0)
        {
            float lr = length(d + float2(0, -0.35) * r) / r;
            rim = max(rim, (1.0h - smoothstep(0.95h, 1.15h, lr)) - (1.0h - smoothstep(0.75h, 0.95h, lr)));
        }
    }
    return half2(hole, saturate(rim));
}

// Светящиеся «прожилки» (соусы, карри, кипящий шоколад): тонкие извилистые линии.
half CCA_Veins(float2 p, float t)
{
    float2 w = float2(CCA_Fbm(p * 0.8 + float2(1.3, 7.1) + t * 0.05), CCA_Fbm(p * 0.8 + float2(8.2, 2.4) - t * 0.04));
    float v = 1.0 - abs(CCA_Fbm(p + w * 1.6) * 2.0 - 1.0);
    return smoothstep(0.945h, 0.975h, v);
}

#endif