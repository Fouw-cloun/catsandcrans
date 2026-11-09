#ifndef CATCOOKS_PUFF_STYLE_INCLUDED
#define CATCOOKS_PUFF_STYLE_INCLUDED
float CCPHash(float3 p)
{
    p = frac(p * .1031); p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}
float CCPNoise(float3 p)
{
    float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    float a = lerp(CCPHash(i), CCPHash(i + float3(1,0,0)), f.x);
    float b = lerp(CCPHash(i + float3(0,1,0)), CCPHash(i + float3(1,1,0)), f.x);
    float c = lerp(CCPHash(i + float3(0,0,1)), CCPHash(i + float3(1,0,1)), f.x);
    float d = lerp(CCPHash(i + float3(0,1,1)), CCPHash(i + float3(1,1,1)), f.x);
    return lerp(lerp(a,b,f.y), lerp(c,d,f.y), f.z);
}
float3 CCPColour(float3 tint, float3 normal, float3 view, float softness)
{
    float light = dot(normal, normalize(float3(-.42,.82,-.38))) * .5 + .5;
    float edge = max(fwidth(light), lerp(.016,.10,saturate(softness)));
    float3 shade = tint * float3(.64,.69,.78);
    float3 colour = lerp(shade, tint, smoothstep(.38-edge,.38+edge,light));
    float3 highlight = lerp(tint,min(1,tint*1.35+.035),.65);
    colour = lerp(colour, highlight, smoothstep(.78-edge,.78+edge,light));
    float facing = saturate(dot(normal,view));
    return colour * lerp(.88,1,smoothstep(.02,.24,facing));
}
float CCPDissolve(float3 shape, float phase, float age, float erosion)
{
    float n = CCPNoise(shape * 5 + float3(phase,age * -.6,phase * .3));
    float progress = smoothstep(.48,1,saturate(age)) * saturate(erosion);
    float threshold = lerp(-.18,1.12,progress);
    float edge = max(fwidth(n),.045);
    return smoothstep(threshold-edge,threshold+edge,n);
}
// Per-particle settings: pattern ID, contrast, spatial scale, animation time.
float3 CCPPattern(float3 colour, float3 shape, float phase, float4 style)
{
    if (style.x < .5 || style.y < .001) return colour;
    float3 p = shape * clamp(style.z,.5,12);
    float t = style.w;
    float n = CCPNoise(p + float3(phase,-t,phase*.31));
    float signal = n;
    if (style.x > 1.5 && style.x < 2.5)
    {
        // Broad curved bands through the volume, independent of camera and UV seams.
        signal = sin(p.y*2.2 + (p.x*p.x+p.z*p.z)*.65 + n*4 - t*1.6)*.5+.5;
    }
    else if (style.x > 2.5 && style.x < 3.5)
    {
        float fine = CCPNoise(p*2.3 + phase);
        float aa = max(fwidth(fine),.03);
        signal = smoothstep(.65-aa,.65+aa,fine);
    }
    else if (style.x > 3.5)
        signal = sin(p.y*3 + n*2.5 - t)*.5+.5;
    float edge = max(fwidth(signal),.055);
    float patches = smoothstep(.43-edge,.57+edge,signal);
    return saturate(colour * lerp(1,lerp(.66,1.19,patches),saturate(style.y)));
}
#endif
