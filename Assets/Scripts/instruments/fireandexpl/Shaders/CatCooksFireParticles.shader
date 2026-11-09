Shader "CatCooks/Fire V3/Particles"
{
    Properties
    {
        _Tint ("Smoke tint", Color) = (.27,.25,.29,1)
        _Opacity ("Smoke opacity", Range(0,.7)) = .26
        _Emission ("Spark emission", Range(.3,2)) = 1.15
        [HideInInspector] _Kind ("0 sparks, 1 smoke", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent"
            "Queue"="Transparent+10" "DisableBatching"="True" }
        Pass
        {
            Name "FireParticles"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend One OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // Embedded puff style: this shader can be moved independently of DustSmoke.
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
            #endif
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Opacity, _Emission, _Kind;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                float3 shape : TEXCOORD2;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float age : TEXCOORD3;
                float phase : TEXCOORD4;
                float3 shape : TEXCOORD5;
                float4 color : COLOR;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv; o.age = v.data.z; o.color = v.color;
                o.phase = v.data.x;
                o.shape = float3(v.shape.x * .5, v.shape.y - .5, v.shape.z * .5);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 rgb = clamp(i.color.rgb, 0, 2) * clamp(_Emission, .3, 2);
                float alpha = saturate(i.color.a);
                float distanceToCamera = distance(i.positionWS, GetCameraPositionWS());
                // Only opacity fades near the camera. There is no size adjustment.
                alpha *= smoothstep(.08, .35, distanceToCamera);
                if (_Kind > .5)
                {
                    float3 normal = normalize(i.normalWS);
                    float facing = saturate(dot(normal, GetWorldSpaceNormalizeViewDir(i.positionWS)));
                    float3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    rgb = CCPColour(saturate(_Tint.rgb), normal, view, .22);
                    rgb += float3(.13,.045,.012) * (1.0 - saturate(i.age * 2)) * (1.0 - i.uv.y);
                    alpha *= clamp(_Opacity, 0, .7) * smoothstep(0, .17, facing)
                        * CCPDissolve(i.shape, i.phase, i.age, .7);
                }
                return half4(rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
