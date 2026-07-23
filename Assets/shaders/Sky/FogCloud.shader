// =============================================================
//  FogCloud.shader
//  Огромные туманные полупрозрачные облака в стиле PEAK.
//  Техника: fog cards — большие мягкие билборды с:
//   - процедурным fBm-шумом (форма, живая анимация)
//   - DEPTH FADE (soft particles): где карта пересекает гору/землю,
//     облако плавно тает, а не режется линией
//   - CAMERA FADE: при подлёте камеры вплотную растворяется (нет "хлопка")
//   - лёгкой подсветкой от солнца и грозовым потемнением
//  Очень дёшево (1 квад на облако), выглядит объёмно.
//
//  ТРЕБОВАНИЕ: в URP Asset включи Depth Texture (иначе depth fade
//  не сработает и будут резкие пересечения с геометрией).
//  URP, Unity 2022.3+ / Unity 6.
// =============================================================
Shader "Custom/FogCloud"
{
    Properties
    {
        [Header(Look)]
        _Color       ("Cloud Color", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Tint", Color) = (0.75, 0.80, 0.90, 1)
        _Opacity     ("Max Opacity (translucency)", Range(0, 1)) = 0.45
        _SunTint     ("Sun Tint", Range(0, 1)) = 0.3

        [Header(Shape)]
        _NoiseScale ("Noise Scale", Range(0.5, 8)) = 2.5
        _Coverage   ("Coverage",    Range(0, 1)) = 0.55
        _EdgeSoft   ("Edge Softness", Range(0.05, 1)) = 0.5
        _Wind       ("Wind (uv/sec)", Vector) = (0.01, 0.003, 0, 0)

        [Header(Fades)]
        _DepthFade  ("Depth Fade Distance", Range(1, 150)) = 40
        _CameraFade ("Camera Fade Distance", Range(1, 200)) = 60

        [Header(Storm)]
        _CloudDarkness ("Cloud Darkness", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "FogCloudForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _GlobalCloudDarkness;

            // per-card вариации (ставит FogCloudLayer через MaterialPropertyBlock)
            float  _CardSeed;
            float  _CardRot;
            float2 _CardStretch;

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _ShadowColor;
                float  _Opacity, _SunTint;
                float  _NoiseScale, _Coverage, _EdgeSoft;
                float4 _Wind;
                float  _DepthFade, _CameraFade;
                float  _CloudDarkness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float  camDist    : TEXCOORD3;
                float  seed       : TEXCOORD4; // из позиции объекта -> у каждого облака свой узор
            };

            Varyings vert (Attributes v)
            {
                Varyings o;

                // billboard: квад всегда лицом к камере, масштаб объекта сохраняется
                float3 centerWS = TransformObjectToWorld(float3(0,0,0));
                float3 right = normalize(UNITY_MATRIX_V._m00_m01_m02);
                float3 up    = normalize(UNITY_MATRIX_V._m10_m11_m12);
                float sx = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float sy = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                float3 posWS = centerWS + right * v.positionOS.x * sx + up * v.positionOS.y * sy;

                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.screenPos  = ComputeScreenPos(o.positionCS);
                o.uv = v.uv;
                o.camDist = distance(GetCameraPositionWS(), centerWS);
                o.seed = frac(centerWS.x * 0.137 + centerWS.z * 0.291) * 37.0;
                return o;
            }

            float hash(float2 p){ p = frac(p*float2(123.34,345.45)); p += dot(p,p+34.345); return frac(p.x*p.y); }
            float valueNoise(float2 p)
            {
                float2 i = floor(p); float2 f = frac(p);
                float2 u = f*f*(3.0-2.0*f);
                float a=hash(i), b=hash(i+float2(1,0)), c=hash(i+float2(0,1)), d=hash(i+float2(1,1));
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }
            float fbm(float2 p)
            {
                float v=0, a=0.5;
                for(int k=0;k<4;k++){ v+=a*valueNoise(p); p*=2.02; a*=0.5; }
                return v;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // --- форма: у каждой карты свой поворот, растяжка и рваный контур ---
                float2 p = i.uv - 0.5;

                // поворот карты
                float ca = cos(_CardRot), sa = sin(_CardRot);
                p = float2(p.x * ca - p.y * sa, p.x * sa + p.y * ca);

                // растяжка (одни широкие, другие вытянутые); фолбэк = 1,1
                float2 st = (abs(_CardStretch.x) + abs(_CardStretch.y) < 0.001) ? float2(1,1) : _CardStretch;
                p *= st;

                float r = length(p) * 2.0;
                float ang = atan2(p.y, p.x);

                // рваный неровный контур (без шва по кругу)
                float2 acoord = float2(cos(ang), sin(ang)) * 1.6 + _CardSeed;
                float lump = fbm(acoord);
                float threshold = 0.65 + lump * 0.55;
                float radial = saturate(1.0 - r / threshold);
                radial = radial * radial;

                // детализирующий шум (свой у каждой карты через _CardSeed)
                float2 nuv = i.uv * _NoiseScale + _CardSeed * 7.0 + _Wind.xy * _Time.y;
                float n = fbm(nuv);

                float d = radial * (n * 0.75 + _Coverage * 0.6);
                float alpha = smoothstep(0.28, 0.28 + _EdgeSoft, d);

                // --- DEPTH FADE: тает при пересечении с геометрией ---
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float fragEye  = i.screenPos.w;
                float depthDiff = sceneEye - fragEye;
                alpha *= saturate(depthDiff / _DepthFade);

                // --- CAMERA FADE: растворение при подлёте вплотную ---
                alpha *= saturate(i.camDist / _CameraFade);

                // сильная прозрачность (стиль PEAK)
                alpha *= _Opacity;

                // --- цвет: мягкий градиент верх/низ + чуть солнца ---
                Light mainLight = GetMainLight();
                float shade = saturate(i.uv.y + n * 0.3);
                float3 col = lerp(_ShadowColor.rgb, _Color.rgb, shade);
                col *= lerp(float3(1,1,1), mainLight.color, _SunTint);

                // гроза
                float dark = saturate(_CloudDarkness + _GlobalCloudDarkness);
                col = lerp(col, col * float3(0.35, 0.37, 0.43), dark);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
