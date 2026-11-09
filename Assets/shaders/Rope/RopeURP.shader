// Продвинутый URP-шейдер верёвки. Много визуальных настроек:
// цвет + градиент вдоль длины, износ/грязь, волокна с кручением, ворсистость,
// мягкий свет, аутлайн, приём/отброс теней. Ключевые параметры per-instance (RopeStyle).
Shader "Custom/RopeURP"
{
    Properties
    {
        [Header(Color)]
        _BaseColor   ("Base Color", Color) = (0.55, 0.45, 0.25, 1)
        _TipColor    ("Tip Color", Color) = (0.4, 0.32, 0.18, 1)
        _Gradient    ("Gradient Along Length", Range(0,1)) = 0

        [Header(Surface)]
        _Smoothness  ("Smoothness", Range(0,1)) = 0.2
        _Wrap        ("Light Wrap (softness)", Range(0,1)) = 0.5

        [Header(Fiber)]
        _Fiber       ("Fiber Detail", Range(0,1)) = 0.4
        _FiberTiling ("Fiber Tiling", Range(0,40)) = 12
        _FiberTwist  ("Fiber Twist", Range(0,20)) = 4

        [Header(Wear and Dirt)]
        _Wear        ("Wear Amount", Range(0,1)) = 0
        _WearColor   ("Wear Color", Color) = (0.05,0.04,0.03,1)
        _WearScale   ("Wear Scale", Range(1,60)) = 20

        [Header(Fuzz)]
        _Fuzz        ("Fuzz Amount", Range(0,1)) = 0.2
        _FuzzColor   ("Fuzz Color", Color) = (0.7, 0.6, 0.4, 1)
        _FuzzPower   ("Fuzz Power", Range(1,8)) = 3

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width", Range(0,0.05)) = 0.008
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 300

        Pass  // АУТЛАЙН (inverted hull)
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front ZWrite On
            HLSLPROGRAM
            #pragma vertex vo
            #pragma fragment fo
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor,_TipColor,_FuzzColor,_WearColor,_OutlineColor;
                float _Gradient,_Smoothness,_Wrap,_Fiber,_FiberTiling,_FiberTwist,_Wear,_WearScale,_Fuzz,_FuzzPower,_OutlineWidth;
            CBUFFER_END
            struct A{float4 p:POSITION;float3 n:NORMAL;}; struct V{float4 h:SV_POSITION;};
            V vo(A i){ V o; float3 w=TransformObjectToWorld(i.p.xyz); float3 nw=TransformObjectToWorldNormal(i.n);
                       w+=normalize(nw)*_OutlineWidth; o.h=TransformWorldToHClip(w); return o; }
            half4 fo(V i):SV_Target{ return _OutlineColor; }
            ENDHLSL
        }

        Pass  // ОСВЕЩЕНИЕ
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor,_TipColor,_FuzzColor,_WearColor,_OutlineColor;
                float _Gradient,_Smoothness,_Wrap,_Fiber,_FiberTiling,_FiberTwist,_Wear,_WearScale,_Fuzz,_FuzzPower,_OutlineWidth;
            CBUFFER_END

            struct A{ float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; float2 uv2:TEXCOORD1; };
            struct V{ float4 h:SV_POSITION; float3 wp:TEXCOORD0; float3 wn:TEXCOORD1; float2 uv:TEXCOORD2; float len:TEXCOORD3; };

            float hash21(float2 p){ p=frac(p*float2(123.34,456.21)); p+=dot(p,p+45.32); return frac(p.x*p.y); }
            float noise(float2 p){ float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
                float a=hash21(i),b=hash21(i+float2(1,0)),c=hash21(i+float2(0,1)),d=hash21(i+float2(1,1));
                return lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y); }

            V vert(A i){ V o; VertexPositionInputs vp=GetVertexPositionInputs(i.p.xyz);
                o.h=vp.positionCS; o.wp=vp.positionWS; o.wn=TransformObjectToWorldNormal(i.n);
                o.uv=i.uv; o.len=i.uv2.x; return o; }

            half4 frag(V i):SV_Target
            {
                float3 N=normalize(i.wn);
                float3 Vd=normalize(GetCameraPositionWS()-i.wp);

                // цвет + градиент вдоль длины
                float3 albedo=lerp(_BaseColor.rgb,_TipColor.rgb,i.len*_Gradient);

                // волокна с кручением
                float strand=frac(i.uv.x*4.0 + i.uv.y*_FiberTiling + i.uv.x*_FiberTwist);
                float groove=abs(strand-0.5)*2.0;
                albedo*=lerp(1.0,0.7,groove*_Fiber);
                albedo*=lerp(1.0,0.85+0.15*hash21(i.uv*float2(40,120)),_Fiber);
                N=normalize(N+normalize(cross(N,float3(0,1,0))+1e-4)*(strand-0.5)*_Fiber*0.5);

                // износ/грязь (пятна шумом)
                float dirt=noise(i.uv*float2(_WearScale,_WearScale*0.4));
                albedo=lerp(albedo,_WearColor.rgb,saturate(dirt*_Wear));

                // мягкий свет + тени
                float4 sc=TransformWorldToShadowCoord(i.wp);
                Light m=GetMainLight(sc);
                float nl=dot(N,m.direction);
                float w=saturate(lerp(saturate(nl),nl*0.5+0.5,_Wrap));
                float3 light=m.color*(w*m.shadowAttenuation)+SampleSH(N);
                #ifdef _ADDITIONAL_LIGHTS
                uint c=GetAdditionalLightsCount();
                for(uint li=0;li<c;li++){ Light l=GetAdditionalLight(li,i.wp);
                    float a=saturate(lerp(saturate(dot(N,l.direction)),dot(N,l.direction)*0.5+0.5,_Wrap));
                    light+=l.color*(a*l.distanceAttenuation*l.shadowAttenuation); }
                #endif

                float3 H=normalize(m.direction+Vd);
                float spec=pow(saturate(dot(N,H)),lerp(6.0,90.0,_Smoothness))*_Smoothness;
                float3 color=albedo*light + spec*m.color*m.shadowAttenuation;

                // ворсистость
                float fres=pow(1.0-saturate(dot(N,Vd)),_FuzzPower);
                color+=_FuzzColor.rgb*(fres*_Fuzz*(0.6+0.4*hash21(i.uv*float2(80,30))));

                return half4(color,1.0);
            }
            ENDHLSL
        }

        Pass  // ТЕНИ
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex sv
            #pragma fragment sf
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A{float4 p:POSITION;float3 n:NORMAL;}; struct V{float4 h:SV_POSITION;};
            V sv(A i){ V o; float3 w=TransformObjectToWorld(i.p.xyz); float3 nw=TransformObjectToWorldNormal(i.n);
                float4 h=TransformWorldToHClip(ApplyShadowBias(w,nw,_LightDirection));
                #if UNITY_REVERSED_Z
                    h.z=min(h.z,UNITY_NEAR_CLIP_VALUE);
                #else
                    h.z=max(h.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                o.h=h; return o; }
            half4 sf(V i):SV_Target{ return 0; }
            ENDHLSL
        }

        Pass  // DEPTH
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex dv
            #pragma fragment df
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A{float4 p:POSITION;}; struct V{float4 h:SV_POSITION;};
            V dv(A i){ V o; o.h=TransformObjectToHClip(i.p.xyz); return o; }
            half4 df(V i):SV_Target{ return 0; }
            ENDHLSL
        }
    }
}
