Shader "CatCooks/Painted Backdrop/Square City"
{
 Properties
 {
  [MainTexture] _BaseMap("Painted city RGBA",2D)="white" {}
  [MainColor] _Tint("Tint (start white)",Color)=(1,1,1,1)
  _Brightness("Paint brightness",Range(.2,2))=1
  _Saturation("Paint saturation",Range(0,1.5))=.95
  _DetailSoftness("Texture softening - mip bias",Range(0,2))=.6
  _AirColor("Painted air colour",Color)=(.75,.76,.78,1)
  _AirBlend("Extra painted air (0 with strong weather fog)",Range(0,1))=0
  _WeatherInfluence("Follow main light and ambient brightness",Range(0,1))=.75
  _Cutoff("Skyline alpha cutoff",Range(.05,.95))=.4
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"}
  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
  CBUFFER_START(UnityPerMaterial)
   float4 _Tint,_AirColor;
   float _Brightness,_Saturation,_DetailSoftness,_AirBlend,_WeatherInfluence,_Cutoff;
  CBUFFER_END
  struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
  struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half illumination:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
  V Vert(A v)
  {
   UNITY_SETUP_INSTANCE_ID(v);V o=(V)0;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
   o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;
   Light sun=GetMainLight();
   float lightLevel=dot(sun.color,float3(.2126,.7152,.0722))*.55+dot(max(0,SampleSH(float3(0,1,0))),float3(.2126,.7152,.0722))*.65;
   o.illumination=lerp(1,clamp(lightLevel,.08,1.1),_WeatherInfluence);
   return o;
  }
  half4 ReadPaint(float2 uv)
  {
   // The alpha coverage used by forward and depth is exactly the same.
   half4 paint=SAMPLE_TEXTURE2D_BIAS(_BaseMap,sampler_BaseMap,uv,_DetailSoftness);
   clip(paint.a-_Cutoff);return paint;
  }
  half4 Frag(V i):SV_Target
  {
   UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
   half4 paint=ReadPaint(i.uv);
   float3 col=paint.rgb*_Tint.rgb;float lum=dot(col,float3(.2126,.7152,.0722));
   col=max(0,lerp(lum.xxx,col,_Saturation));col=lerp(col,_AirColor.rgb,_AirBlend);
   return half4(col*_Brightness*i.illumination,1);
  }
  half4 DepthFrag(V i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);ReadPaint(i.uv);return 0;}
  ENDHLSL
  Pass
  {
   Name "PaintedCity" Tags {"LightMode"="UniversalForwardOnly"}
   Cull Off ZWrite On ZTest LEqual
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags {"LightMode"="DepthOnly"}
   Cull Off ZWrite On ZTest LEqual ColorMask R
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment DepthFrag
   #pragma multi_compile_instancing
   ENDHLSL
  }
  // DepthNormals: чтобы при SSAO нарисованный город был в текстуре глубины.
  Pass
  {
   Name "DepthNormals" Tags {"LightMode"="DepthNormals"}
   Cull Off ZWrite On ZTest LEqual
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment DNFrag
   #pragma multi_compile_instancing
   half4 DNFrag(V i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);ReadPaint(i.uv);return half4(0,1,0,0);}
   ENDHLSL
  }
 }
 Fallback Off
}
