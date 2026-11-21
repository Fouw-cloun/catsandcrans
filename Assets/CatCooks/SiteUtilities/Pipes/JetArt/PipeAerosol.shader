Shader "CatCooks/Pipes/Aerosol"
{
    Properties { _Shape ("Mist / ice spray / odor / droplets",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Shape;
            CBUFFER_END
            struct A {float4 p:POSITION;half4 c:COLOR;float2 uv:TEXCOORD0;};
            struct V {float4 p:SV_POSITION;half4 c:COLOR;float2 uv:TEXCOORD0;half fog:TEXCOORD1;};
            V Vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.c=i.c;o.uv=i.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
            float hash2(float2 p){float3 a=frac(p.xyx*.1031);a+=dot(a,a.yzx+33.33);return frac((a.x+a.y)*a.z);}
            float cloud(float2 p){
                float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(hash2(i),hash2(i+float2(1,0)),f.x),lerp(hash2(i+float2(0,1)),hash2(i+1),f.x),f.y);
            }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1; float a;
                half3 col=i.c.rgb;
                if(_Shape<.5 || (_Shape>1.5 && _Shape<2.5)){
                    float drift=_Time.y*.32+i.c.r*8;
                    float coarse=cloud(p*2.2+float2(drift*.31,-drift));
                    float wisps=cloud(p*5.2+coarse*1.7+drift*.29);
                    float density=coarse*.62+wisps*.38;
                    if(_Shape<.5){
                        float shell=length(p*float2(1.0,.87))+.42*(.52-density);
                        a=(1-smoothstep(.24,.98,shell))*smoothstep(.10,.72,density)*.87;
                        col*=.82+.28*wisps;
                    }else{
                        float stem=p.x-.36*sin(p.y*3.1+drift+coarse)-.21*(wisps-.5);
                        float width=.13+.20*coarse;
                        a=(1-smoothstep(width*.25,width+.10,abs(stem)))*(1-smoothstep(.38,.99,abs(p.y)))*density;
                    }
                }else{
                    float2 b=p*float2(1.10,.96); b.x+=.13*b.y*b.y;
                    float r=dot(b,b),rim=smoothstep(.26,.81,r);
                    a=(1-smoothstep(.74,1.,r))*(.48+.44*rim);
                    float flash=exp2(-22*dot(b-float2(-.29,.31),b-float2(-.29,.31)));
                    col=col*(.64+.32*rim)+flash*.70;
                    if(_Shape<1.5){
                        float2 q=abs(p-float2(-.24,.24));
                        float sparkle=exp2(-43*min(q.x+.14*q.y,q.y+.14*q.x));
                        col+=sparkle*.35;a=saturate(a+sparkle*.12)*(1-smoothstep(.83,1.,r));
                    }
                }
                return half4(MixFog(col,i.fog),a*i.c.a);
            }
            ENDHLSL
        }
    }
}
