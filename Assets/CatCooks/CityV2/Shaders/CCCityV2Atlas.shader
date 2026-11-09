Shader "CatCooks/CityV2/Atlas"
{
 Properties
 {
  [MainTexture] _BaseMap("City atlas (sRGB)",2D)="white" {}
  [MainColor] _BaseColor("Atlas multiplier (start with WHITE)",Color)=(1,1,1,1)
  _AlbedoGain("Albedo gain",Range(.5,1.8))=1.0
  _Saturation("Material saturation",Range(0,1.5))=1.08
  _SunGain("Main sun gain",Range(0,2))=1
  _FillGain("Ambient fill gain",Range(0,2))=.75
  _Wrap("Small light wrap",Range(0,.3))=.04
  [Header(Optional Local City Haze)]
  _HazeAmount("Local haze (0 when using fullscreen weather fog)",Range(0,1))=0
  _HazeColor("Haze colour",Color)=(.78,.73,.73,1)
  _HazeStart("Haze start (world metres)",Float)=350
  _HazeEnd("Haze end (world metres)",Float)=1600
  _HazeCurve("Haze curve",Range(.5,4))=1.6
  _HazeBottom("World height - dense air",Float)=0
  _HazeTop("World height - clear roofs",Float)=100
  _HazeHeightInfluence("Height influence",Range(0,1))=.65
  [Header(Distant Texture Softening)]
  _SoftStart("Softening start (world metres)",Float)=700
  _SoftEnd("Softening end (world metres)",Float)=2000
  _MipSoftness("Extra mip bias (0 = normal mipmaps)",Range(0,1))=0
  [Enum(Lit,0,AlbedoOnly,1,LightOnly,2)] _View("Diagnostic view",Float)=0
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
  CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;
   float4 _HazeColor;
   float _HazeAmount,_HazeStart,_HazeEnd,_HazeCurve,_HazeBottom,_HazeTop,_HazeHeightInfluence;
   float _SoftStart,_SoftEnd,_MipSoftness;
   float _AlbedoGain,_Saturation,_SunGain,_FillGain,_Wrap,_View;
  CBUFFER_END
  struct A {float4 pos:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
  struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;half3 light:TEXCOORD1;float3 world:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO};
  V Vert(A v)
  {
   UNITY_SETUP_INSTANCE_ID(v);V o=(V)0;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
   o.pos=TransformObjectToHClip(v.pos.xyz);o.uv=v.uv;o.world=TransformObjectToWorld(v.pos.xyz);
   float3 n=TransformObjectToWorldNormal(v.normal);
   Light sun=GetMainLight();
   float ndl=saturate((dot(n,sun.direction)+_Wrap)/(1+_Wrap));
   // Real main-light direction/colour and sky fill. No fixed artist direction.
   // No shadow map, reflection lookup or extra lights. Local haze is off by default.
   o.light=max(0,SampleSH(n))*_FillGain+sun.color*(ndl*sun.distanceAttenuation*_SunGain);
   return o;
  }
  half4 Frag(V i):SV_Target
  {
   UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
   float distanceToCamera=distance(GetCameraPositionWS(),i.world);
   float soft=saturate((distanceToCamera-_SoftStart)/max(1,_SoftEnd-_SoftStart))*_MipSoftness;
   float3 raw=SAMPLE_TEXTURE2D_BIAS(_BaseMap,sampler_BaseMap,i.uv,soft).rgb*_BaseColor.rgb;
   float luma=dot(raw,float3(.2126,.7152,.0722));
   float3 albedo=max(0,lerp(luma.xxx,raw,_Saturation))*_AlbedoGain;
   float3 result=albedo*i.light;
   float haze=pow(saturate((distanceToCamera-_HazeStart)/max(1,_HazeEnd-_HazeStart)),_HazeCurve);
   float lowAir=1-smoothstep(_HazeBottom,max(_HazeBottom+1,_HazeTop),i.world.y);
   haze*=lerp(1,lowAir,_HazeHeightInfluence)*_HazeAmount;
   result=lerp(result,_HazeColor.rgb,saturate(haze));
   if(_View>.5 && _View<1.5) result=raw;
   else if(_View>1.5) result=i.light;
   return half4(result,1);
  }
  ENDHLSL
  Pass
  {
   Name "CityLit" Tags {"LightMode"="UniversalForwardOnly"}
   Cull Back ZWrite On
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
   Cull Back ZWrite On ColorMask R
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex DepthVert
   #pragma fragment DepthFrag
   #pragma multi_compile_instancing
   struct D {float4 pos:SV_POSITION;UNITY_VERTEX_OUTPUT_STEREO};
   D DepthVert(A v) {UNITY_SETUP_INSTANCE_ID(v);D o=(D)0;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=TransformObjectToHClip(v.pos.xyz);return o;}
   half4 DepthFrag(D i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return 0;}
   ENDHLSL
  }
  // DepthNormals: без него при включённом SSAO дома не попадают в текстуру глубины —
  // туман и SSAO считали их «небом» (полоса горизонта поверх домов, нет дымки вдали).
  Pass
  {
   Name "DepthNormals" Tags {"LightMode"="DepthNormals"}
   Cull Back ZWrite On
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex DNVert
   #pragma fragment DNFrag
   #pragma multi_compile_instancing
   struct DN {float4 pos:SV_POSITION;float3 n:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
   DN DNVert(A v) {UNITY_SETUP_INSTANCE_ID(v);DN o=(DN)0;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=TransformObjectToHClip(v.pos.xyz);o.n=TransformObjectToWorldNormal(v.normal);return o;}
   half4 DNFrag(DN i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return half4(normalize(i.n),0);}
   ENDHLSL
  }
 }
 Fallback Off
}
