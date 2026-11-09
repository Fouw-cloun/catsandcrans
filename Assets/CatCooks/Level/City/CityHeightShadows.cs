using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CatCooks.Level
{
    /// <summary>
    /// ТЕНИ И ЗАТЕНЕНИЕ ГОРОДА ПО КАРТЕ ВЫСОТ. Карта высот (вид сверху) запекается один раз
    /// (меню «Тени города: запечь карту высот»), дальше шейдеры домов (Building, Toon, Far) сами считают:
    ///   • тени от соседних домов по направлению солнца — солнце ходит, тени ходят; на любом расстоянии,
    ///     без карт теней (вблизи остаются настоящие тени URP, дальше — эти);
    ///   • затенение: переулки, стены напротив высоких домов, асфальт у подножия (контактная тень).
    /// Стоит почти ничего: 8 + 6 выборок маленькой текстуры на пиксель.
    /// ПОГОДА: в плохую погоду (пасмурно, дождь, снег, гроза, туман) тени от солнца плавно гаснут —
    /// и настоящие (у солнца выключаются совсем — экономия целого прохода теней), и по карте высот.
    /// Затенение (AO) остаётся и чуть усиливается — в пасмурный день так и выглядит: мягкая тень в переулках.
    /// Мерило — приглушение солнца в смеси погоды (sunDim пресета): ясно 1, пасмурно 0.7, ливень 0.4.
    /// </summary>
    [ExecuteAlways]
    public class CityHeightShadows : MonoBehaviour
    {
        [Tooltip("Карта высот города (R — высота, мир Y). Ставит запекание")]
        public Texture2D HeightMap;
        [Tooltip("Карта в мире: x, z — минимум, z, w — размер")]
        public Vector4 Rect = new Vector4(0, 0, 100, 100);

        [Header("Сила")]
        [Range(0f, 1f)] public float ShadowStrength = 0.85f;
        [Range(0f, 1f)] public float AOStrength = 0.7f;
        [Tooltip("Радиус затенения, м")]
        public float AORadius = 5f;
        [Tooltip("Смещение (против «грязи» на крышах), м")]
        public float Bias = 0.3f;

        [Header("Переход с настоящих теней")]
        [Tooltip("Брать дистанцию теней из URP (переход — на последней четверти)")]
        public bool AutoFade = true;
        [Tooltip("Если не авто: с какого и до какого расстояния, м, вместо настоящих теней — карта высот")]
        public Vector2 FadeMeters = new Vector2(30f, 45f);
        [Tooltip("Тени по карте высот ВЕЗДЕ, и вблизи тоже (вместе с настоящими — берётся темнее). Включи, если настоящих теней нет или мало")]
        public bool Everywhere = true;

        [Header("Погода: тени гаснут в плохую погоду")]
        [Tooltip("Плавно гасить тени солнца в плохую погоду (только в игре)")]
        public bool WeatherShadows = true;
        [Tooltip("Приглушение солнца погоды (sunDim), при котором тени гаснут совсем (пасмурно 0.7, хмуро 0.55, ливень 0.4)")]
        [Range(0f, 1f)] public float SunDimOff = 0.68f;
        [Tooltip("Приглушение солнца, при котором тени полные (ясно 1, перед грозой 0.9)")]
        [Range(0f, 1f)] public float SunDimFull = 0.86f;
        [Tooltip("Секунд на угасание/появление (смесь погоды и так перетекает медленно, это — сверху)")]
        [Range(0.1f, 20f)] public float FadeSeconds = 4f;
        [Tooltip("Насколько усилить затенение (AO) в пасмурную погоду")]
        [Range(0f, 1f)] public float OvercastAOBoost = 0.35f;
        [Tooltip("Выключать тени у солнца совсем, когда они погасли (экономит проход теней)")]
        public bool TurnOffSunShadows = true;

        /// <summary>Сила теней от погоды сейчас: 1 — ясно, 0 — тени погашены.</summary>
        public float WeatherShadow01 { get { return _w; } }

        private float _w = 1f;
        private Light _sun;
        private float _sunBase = -1f;
        private LightShadows _sunType = LightShadows.Soft;
        private bool _weOff;
        private CatCooksMoodDirector _mood;
        private float _findAt;

        static readonly int IdMap = Shader.PropertyToID("_CC_HMap");
        static readonly int IdRect = Shader.PropertyToID("_CC_HMapRect");
        static readonly int IdP = Shader.PropertyToID("_CC_HMapP");
        static readonly int IdP2 = Shader.PropertyToID("_CC_HMapP2");
        static readonly int IdMeter = Shader.PropertyToID("_CC_Meter");

        private void OnDisable()
        {
            Shader.SetGlobalVector(IdP2, Vector4.zero);
            RestoreSun();
        }

        private void RestoreSun()
        {
            if (_sun != null && _sunBase >= 0f)
            {
                _sun.shadowStrength = _sunBase;
                if (_weOff) _sun.shadows = _sunType;
            }
            _weOff = false;
            _sunBase = -1f;
            _w = 1f;
        }

        /// <summary>Цель по погоде: 1 — ясно, 0 — плохая погода.</summary>
        private float WeatherTarget()
        {
            if (_mood == null && Time.realtimeSinceStartup >= _findAt)
            {
                _findAt = Time.realtimeSinceStartup + 2f;
                _mood = FindFirstObjectByType<CatCooksMoodDirector>();
            }
            if (_mood == null || _mood.CurrentWeatherMix == null) return 1f;
            float d = _mood.CurrentWeatherMix.sunDim;
            return Mathf.Clamp01((d - SunDimOff) / Mathf.Max(0.01f, SunDimFull - SunDimOff));
        }

        private void UpdateWeather()
        {
            if (!WeatherShadows || !Application.isPlaying) { if (_sunBase >= 0f) RestoreSun(); _w = 1f; return; }
            float t = WeatherTarget();
            _w = Mathf.MoveTowards(_w, t, Time.deltaTime / Mathf.Max(0.1f, FadeSeconds));
            if (_sun == null)
            {
                _sun = RenderSettings.sun;
                if (_sun == null)
                    foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.isActiveAndEnabled) { _sun = l; break; }
                if (_sun == null) return;
            }
            if (_sunBase < 0f)
            {
                _sunBase = _sun.shadowStrength;
                _sunType = _sun.shadows == LightShadows.None ? LightShadows.Soft : _sun.shadows;
                if (_sun.shadows == LightShadows.None) return;                       // теней у солнца не было — не трогаем
            }
            _sun.shadowStrength = _sunBase * _w;
            if (TurnOffSunShadows)
            {
                if (_w <= 0.01f && !_weOff && _sun.shadows != LightShadows.None) { _sun.shadows = LightShadows.None; _weOff = true; }
                else if (_w > 0.01f && _weOff) { _sun.shadows = _sunType; _weOff = false; }
            }
        }

        private float Meter()
        {
            if (CityRain.Instance != null) return CityRain.Instance.Meter;
            float m = Shader.GetGlobalFloat(IdMeter);
            return m > 0f ? m : 3.5f;
        }

        private void LateUpdate()
        {
            UpdateWeather();
            if (HeightMap == null || Rect.z <= 0f || Rect.w <= 0f) { Shader.SetGlobalVector(IdP2, Vector4.zero); return; }
            float m = Meter();
            float f0, f1;
            UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (AutoFade && urp != null) { f1 = urp.shadowDistance; f0 = f1 * 0.75f; }
            else { f0 = FadeMeters.x * m; f1 = FadeMeters.y * m; }
            if (Everywhere) { f0 = -2f; f1 = -1f; }                                     // вес карты = 1 на любом расстоянии
            Shader.SetGlobalTexture(IdMap, HeightMap);
            Shader.SetGlobalVector(IdRect, new Vector4(Rect.x, Rect.y, 1f / Rect.z, 1f / Rect.w));
            float ao = Mathf.Clamp01(AOStrength * (1f + OvercastAOBoost * (1f - _w)));
            Shader.SetGlobalVector(IdP, new Vector4(ShadowStrength * _w, ao, f0, f1));
            Shader.SetGlobalVector(IdP2, new Vector4(m, Bias * m, AORadius * m, 1f));
        }
    }
}
