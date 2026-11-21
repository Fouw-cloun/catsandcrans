Shader "CatCooks/Pipes/Discharge"
{
    Properties
    {
        _Deep ("Body",Color)=(.06,.43,.57,1)
        _Crest ("Shallows",Color)=(.45,.81,.85,1)
        _Foam ("Aerated edge",Color)=(.9,.98,1,1)
        _FoamAmount ("Aeration",Range(0,1))=.55
        _Gloss ("Reflection",Range(0,1))=.8
        _Mud ("Sediment",Range(0,1))=0
        _Intensity ("Pressure",Range(0,1))=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-15" }
        Pass
        {
            Name "LiquidLayers"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep,_Crest,_Foam;
                float _Intensity,_Gloss,_Mud,_FoamAmount;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; float2 state:TEXCOORD1; half4 color:COLOR; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; half3 n:TEXCOORD1; float2 uv:TEXCOORD2; float2 state:TEXCOORD3; half4 color:COLOR; half fog:TEXCOORD4; };
            V Vert(A v)
            {
                V o;
                o.world=TransformObjectToWorld(v.p.xyz);o.n=TransformObjectToWorldNormal(v.n);
                o.p=TransformWorldToHClip(o.world);o.uv=v.uv;o.state=v.state;o.color=v.color;
                o.fog=ComputeFogFactor(o.p.z);return o;
            }
            float hash(float3 p)
            {
                p=frac(p*.1031);
                p+=dot(p,p.yzx+33.33);
                return frac((p.x+p.y)*p.z);
            }
            float noise3(float3 p)
            {
                float3 i=floor(p), f=frac(p);f=f*f*(3-2*f);
                float4 a=float4(hash(i),hash(i+float3(1,0,0)),hash(i+float3(0,1,0)),hash(i+float3(1,1,0)));
                float4 b=float4(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),hash(i+float3(0,1,1)),hash(i+1));
                float4 c=lerp(a,b,f.z);
                return lerp(lerp(c.x,c.y,f.x),lerp(c.z,c.w,f.x),f.y);
            }
            half4 Frag(V i):SV_Target
            {
                float age=saturate(i.state.x), layer=i.state.y;
                float sheet=step(.5,layer), contact=step(1.5,layer);
                float ang=i.uv.x*6.2831853;
                float2 ring=float2(cos(ang),sin(ang));
                float clock=_Time.y*lerp(.9,3.6,_Intensity);
                float offset=sheet*i.color.r*11;
                float3 coord=float3(ring*2.1,i.uv.y*.87-clock+offset);
                if(layer>.5 && layer<1.5)coord=float3(i.uv.x*2.4,offset,i.uv.y*.82-clock);
                if(contact>.5)coord=float3(ring*lerp(4.,6.5,i.uv.y),i.uv.y*1.9-_Time.y*1.35);
                float broad=noise3(coord);
                float middle=noise3(coord*2.41+float3(broad*2.2,4.7,broad-.37*clock));
                float detail=noise3(coord*5.7+middle*1.6);
                float field=.50*broad+.34*middle+.16*detail;
                // Fwidth keeps the small foam cells stable when the jet is viewed from across the site.
                float vein=abs(middle-.48+.14*(broad-.5));
                float aa=max(.009,fwidth(vein));
                float lace=(1-smoothstep(.017,.042+aa,vein))*smoothstep(.46,.66,field);
                float bubbles=smoothstep(.62,.77,field+.13*detail);
                float foam=saturate((lace*.60+bubbles)*(.34+age*.86+.34*sheet)*(.45+_FoamAmount));
                half3 n=normalize(i.n);
                half3 view=GetWorldSpaceNormalizeViewDir(i.world);
                n*=dot(view,n)<0?-1:1;
                // Microfacets follow local water movement, breaking up the reflection across the surface.
                float3 tangent=cross(n,abs(n.y)<.9?float3(0,1,0):float3(1,0,0));
                tangent=normalize(tangent);
                n=normalize(n+tangent*((middle-.5)*.59)+cross(n,tangent)*((detail-.5)*.41));
                Light sun=GetMainLight();
                float fres=pow(1-saturate(dot(n,view)),2.8);
                float gloss=pow(saturate(dot(n,SafeNormalize(view+sun.direction))),lerp(65.,20.,_Mud));
                float sky=saturate(n.y*.5+.5);
                half3 col=lerp(_Deep.rgb,_Crest.rgb,saturate(.12+.46*field+.32*fres));
                col*=.82+.18*saturate(dot(n,sun.direction));
                col+=_Gloss*(.14*fres*sky*_Crest.rgb+.63*gloss*sun.color);
                col=lerp(col,_Foam.rgb,foam*.91);
                float baseAlpha=lerp(.48,.20,age);
                float alpha=lerp(baseAlpha,.86-.12*age,_Mud)+.18*fres+.48*foam;
                if(layer<.5){
                    alpha*=lerp(1,smoothstep(.20,.53,field),age*.86);
                }else if(layer<1.5){
                    float flank=1-abs(2*i.uv.x-1);
                    float island=smoothstep(.28+age*.12,.53+age*.08,field+.075*flank);
                    alpha*=smoothstep(.0,.24,flank)*island;
                    col=lerp(col,_Foam.rgb,lace*.31);
                }else if(layer<2.5){
                    float lobe=smoothstep(.18,.54,field+(1-i.uv.y)*.4);
                    alpha=(.48+.4*foam)*lobe*i.color.a;
                    col=lerp(col,_Foam.rgb,.24+.5*foam);
                }else if(layer<3.5){
                    alpha=i.color.a*(.20+.58*bubbles+.32*lace);
                    col=lerp(_Deep.rgb,_Foam.rgb,saturate(.16+.76*bubbles+.6*lace));
                }else{
                    col=lerp(_Crest.rgb,_Foam.rgb,.63);
                    alpha=i.color.a*smoothstep(.28,.58,field)*(.44+.35*detail);
                }
                clip(alpha-.008);
                return half4(MixFog(col,i.fog),saturate(alpha));
            }
            ENDHLSL
        }
    }
}
