#ifndef CATCOOKS_FIRE_NOISE_INCLUDED
#define CATCOOKS_FIRE_NOISE_INCLUDED
// Continuous object-shape noise: no texture dependency and no cylindrical UV seam.
float CCFHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}
float CCFNoise(float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(CCFHash(i), CCFHash(i + float3(1,0,0)), f.x);
    float b = lerp(CCFHash(i + float3(0,1,0)), CCFHash(i + float3(1,1,0)), f.x);
    float c = lerp(CCFHash(i + float3(0,0,1)), CCFHash(i + float3(1,0,1)), f.x);
    float d = lerp(CCFHash(i + float3(0,1,1)), CCFHash(i + float3(1,1,1)), f.x);
    return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
}
#endif
