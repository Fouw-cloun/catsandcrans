Shader "CatSite/Dust/Toon Mesh URP"
{
 Properties
 {
  _AgeTint("Late lifetime tint",Color)=(.88,.91,.95,1)
  _Variation("Per puff shade variation",Range(0,.3))=.12
  _DetailShade("Swirl shading strength",Range(0,.4))=.12
  [Toggle] _DitherAlpha("Mesh alpha via screen dithering (optional)",Float)=0
  _ColorGain("Overall brightness",Range(.1,1.5))=.8
  _SwirlStrength("Erosion swirl",Range(0,.5))=.18
  _NoiseMap("Erosion noise (linear)",2D)="white"{}
  _ShadeTint("Shadow tint",Color)=(.80,.68,.60,1)
  _Highlight("Highlight",Range(1,1.5))=1.1
  _BandSoftness("Toon band softness",Range(.01,.5))=.25
  _LightWrap("Light wrap",Range(0,1))=.35
  _ErodeStart("Erosion starts at lifetime",Range(.3,.95))=.68
  [Toggle] _ErosionEnabled("Enable erosion after visibility check",Float)=0
  _SceneLight("Scene light influence",Range(0,1))=.35
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout"}
  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
  CBUFFER_START(UnityPerMaterial)
   float4 _ShadeTint,_AgeTint;
   float _Variation,_DetailShade,_DitherAlpha;
   float _Highlight,_BandSoftness,_LightWrap,_ErodeStart,_ErosionEnabled,_SceneLight,_ColorGain,_SwirlStrength;
  CBUFFER_END
  struct Attributes
  {
   float4 positionOS:POSITION;float3 normalOS:NORMAL;half4 color:COLOR;float4 data:TEXCOORD0;
   UNITY_VERTEX_INPUT_INSTANCE_ID
  };
  struct Varyings
  {
   float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;float4 data:TEXCOORD1;
   half4 color:COLOR;half fog:TEXCOORD2;
   UNITY_VERTEX_OUTPUT_STEREO
  };
  Varyings Vert(Attributes v)
  {
   Varyings o=(Varyings)0;
   UNITY_SETUP_INSTANCE_ID(v);
   UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
   o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
   o.normalWS=TransformObjectToWorldNormal(v.normalOS);
   o.data=v.data;o.color=v.color;o.fog=ComputeFogFactor(o.positionCS.z);
   return o;
  }
  void DustClip(Varyings i)
  {
   // Identical clipping in depth and color passes is essential with depth priming.
   // Disabled by default: the authored size curve still shrinks particles to zero.
   // Optional coverage alpha: same pattern in color, depth and normals passes.
   if(_DitherAlpha>.5)
   {
    float dither=frac(52.9829189*frac(dot(floor(i.positionCS.xy),float2(.06711056,.00583715))));
    clip(saturate(i.color.a)-max(dither,.001));
   }
   if(_ErosionEnabled>.5)
   {
    float age=saturate(i.data.z);
    float threshold=saturate((age-_ErodeStart)/max(1-_ErodeStart,.01));
    float2 uv=i.data.xy;
    uv.x+=sin(uv.y*6.283+age*3+i.data.w*6.283)*_SwirlStrength;
    float noise=SAMPLE_TEXTURE2D(_NoiseMap,sampler_NoiseMap,uv*2+i.data.w*3.17).r;
    clip(noise-threshold*1.01);
   }
  }
  ENDHLSL
  Pass
  {
   Name "DustForward"
   Tags {"LightMode"="UniversalForwardOnly"}
   Cull Back ZWrite On ZTest LEqual
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   half4 Frag(Varyings i):SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    DustClip(i);
    Light key=GetMainLight();
    float3 keyRaw=lerp(normalize(float3(-.45,.8,-.35)),key.direction,_SceneLight);
    float3 keyDir=keyRaw*rsqrt(max(dot(keyRaw,keyRaw),1e-6));
    float3 n=i.normalWS*rsqrt(max(dot(i.normalWS,i.normalWS),1e-6));
    half wrapped=saturate((dot(n,keyDir)+_LightWrap)/(1+_LightWrap));
    half band=smoothstep(.45-_BandSoftness,.45+_BandSoftness,wrapped);
    half top=smoothstep(.78,.94,wrapped);
    half3 color=i.color.rgb*lerp(_ShadeTint.rgb,half3(1,1,1),band)*lerp(1,_Highlight,top);
    float age=saturate(i.data.z);
    float2 detailUV=i.data.xy*2+float2(i.data.w*3.17,age*.12);
    detailUV.x+=sin(detailUV.y*3.14159+age*2)*_SwirlStrength;
    half detail=SAMPLE_TEXTURE2D(_NoiseMap,sampler_NoiseMap,detailUV).r;
    color*=lerp(1-_DetailShade,1,smoothstep(.25,.7,detail));
    color*=lerp(1-_Variation,1,i.data.w);
    color*=lerp(half3(1,1,1),_AgeTint.rgb,smoothstep(.25,1,age));
    color*=lerp(half3(1,1,1),clamp(key.color,half3(.35,.35,.35),half3(1,1,1)),_SceneLight);
    return half4(MixFog(color*_ColorGain,i.fog),1);
   }
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly"
   Tags {"LightMode"="DepthOnly"}
   Cull Back ZWrite On ZTest LEqual ColorMask R
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vert
   #pragma fragment DepthFrag
   half4 DepthFrag(Varyings i):SV_Target {DustClip(i);return half4(i.positionCS.z,0,0,0);}
   ENDHLSL
  }
  Pass
  {
   Name "DepthNormals"
   Tags {"LightMode"="DepthNormals"}
   Cull Back ZWrite On ZTest LEqual
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vert
   #pragma fragment NormalsFrag
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   half4 NormalsFrag(Varyings i):SV_Target
   {
    DustClip(i);float3 n=normalize(i.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
    float2 oct=PackNormalOctQuadEncode(n);
    return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
    #else
    return half4(n,0);
    #endif
   }
   ENDHLSL
  }

  Pass
  {
   Name "DepthNormalsOnly"
   Tags {"LightMode"="DepthNormalsOnly"}
   Cull Back ZWrite On ZTest LEqual
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vert
   #pragma fragment NormalsFrag
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   half4 NormalsFrag(Varyings i):SV_Target
   {
    DustClip(i);float3 n=normalize(i.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
    float2 oct=PackNormalOctQuadEncode(n);
    return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
    #else
    return half4(n,0);
    #endif
   }
   ENDHLSL
  }

 }
 Fallback Off
}
