using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ЖАРА (визуал) — тем же проходом, что мокрость/снег/град (City Rain Surface):
    ///   • МАРЕВО: воздух дрожит, картинка колышется и течёт вверх — над полом вскользь и у горизонта;
    ///   • МИРАЖИ: вдали на полу «лужи» отражённого неба и домов;
    ///   • ЗНОЙ: тёплый пересвет, насыщенность, выбеленная даль; СОЛНЕЧНЫЙ ОРЕОЛ с бликом (гаснет, если солнце закрыто);
    ///   • ПЕКЛО: всё сразу на максимум + оранжевые дрожащие края экрана.
    /// Сила — по таблице «Погода → жара» или тест (меню P → Город → ЖАРА). В мороз и снег не работает.
    /// </summary>
    [ExecuteAlways]
    public class CityHeat : MonoBehaviour
    {
        public static CityHeat Instance { get; private set; }

        [Serializable]
        public class WeatherHeat
        {
            [Tooltip("Кусок имени/названия погоды (CW_05_HeatHaze → heathaze)")] public string Weather;
            [Tooltip("Марево (дрожь воздуха)")] [Range(0f, 1f)] public float Haze;
            [Tooltip("Миражи на полу вдали")] [Range(0f, 1f)] public float Mirage;
            [Tooltip("Солнечный ореол")] [Range(0f, 1f)] public float Glare;
            [Tooltip("Зной: тёплый пересвет (≥0.6 — пекло, оранжевые края)")] [Range(0f, 1f)] public float Grade;
            public WeatherHeat() { }
            public WeatherHeat(string w, float h, float mi, float gl, float gr) { Weather = w; Haze = h; Mirage = mi; Glare = gl; Grade = gr; }
        }

        [Header("Погода → жара (не нашлась — нет)")]
        public List<WeatherHeat> WeatherTable = DefaultTable();
        public float WeatherBlendSeconds = 8f;

        [Header("Тест (меню P)")]
        public bool TestOverride;
        [Range(0f, 1f)] public float TestHaze = 0.7f;
        [Range(0f, 1f)] public float TestMirage = 0.6f;
        [Range(0f, 1f)] public float TestGlare = 0.6f;
        [Range(0f, 1f)] public float TestGrade = 0.5f;

        [Header("Марево")]
        [Tooltip("Сила дрожи, пикселей (при марево = 1)")]
        public float HazePixels = 5f;
        [Tooltip("Размер волн, пикселей")]
        public float WaveSize = 26f;
        [Tooltip("Скорость дрожи")]
        public float WaveSpeed = 1f;

        [Tooltip("Солнце (пусто — RenderSettings.sun или первый направленный свет)")]
        public Light Sun;
        public bool Log = true;

        public Vector4 Now { get; private set; }   // x марево, y миражи, z ореол, w зной

        private WeatherHeat _row;
        private CatCooksWeatherPreset _lastPreset;
        private Vector4 _cur;
        private float _sunSearch;

        static readonly int IdHeatOn = Shader.PropertyToID("_CC_HeatOn");
        static readonly int IdHeat = Shader.PropertyToID("_CC_Heat");
        static readonly int IdHeatP = Shader.PropertyToID("_CC_HeatP");
        static readonly int IdSun = Shader.PropertyToID("_CC_SunScreen");

        public static List<WeatherHeat> DefaultTable()
        {
            return new List<WeatherHeat>
            {
                new WeatherHeat("scorcher", 1f, 1f, 1f, 1f),          // пекло
                new WeatherHeat("heathaze", 0.7f, 0.65f, 0.6f, 0.5f), // жара, марево
                new WeatherHeat("duststorm", 0.35f, 0f, 0.2f, 0.25f), // пыльная буря — горячий ветер
                new WeatherHeat("smog", 0.2f, 0.1f, 0.15f, 0.2f),     // смог — душно
                new WeatherHeat("summerleaves", 0.15f, 0f, 0.3f, 0.1f),
                new WeatherHeat("clear", 0.15f, 0.1f, 0.35f, 0f),     // ясно — лёгкое марево вдали
                new WeatherHeat("sunbreaks", 0.1f, 0f, 0.35f, 0f),
                new WeatherHeat("pinkhaze", 0.1f, 0f, 0.3f, 0.1f),
                new WeatherHeat("rainbow", 0f, 0f, 0.3f, 0f),
                new WeatherHeat("afterstorm", 0f, 0f, 0.25f, 0f),
            };
        }

        private void MergeTable()
        {
            if (WeatherTable == null) WeatherTable = new List<WeatherHeat>();
            List<WeatherHeat> d = DefaultTable();
            for (int i = 0; i < d.Count; i++)
            {
                bool has = false;
                foreach (WeatherHeat r in WeatherTable) if (r != null && r.Weather == d[i].Weather) { has = true; break; }
                if (!has) WeatherTable.Insert(Mathf.Min(i, WeatherTable.Count), d[i]);
            }
        }

        /// <summary>Строка для погоды (самый длинный совпавший ключ).</summary>
        public WeatherHeat ForWeather(CatCooksWeatherPreset w)
        {
            if (w == null || WeatherTable == null) return null;
            string key = (w.name + " " + w.title).ToLowerInvariant().Replace("_", "");
            WeatherHeat best = null;
            int bestLen = 0;
            foreach (WeatherHeat r in WeatherTable)
            {
                if (r == null || string.IsNullOrEmpty(r.Weather)) continue;
                string k = r.Weather.ToLowerInvariant().Replace("_", "");
                if (k.Length > bestLen && key.Contains(k)) { best = r; bestLen = k.Length; }
            }
            return best;
        }

        private void OnEnable() { Instance = this; MergeTable(); }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalFloat(IdHeatOn, 0f);
            Shader.SetGlobalVector(IdHeat, Vector4.zero);
        }

        private Light FindSun()
        {
            if (Sun != null) return Sun;
            if (RenderSettings.sun != null) return RenderSettings.sun;
            if (Time.realtimeSinceStartup < _sunSearch) return null;
            _sunSearch = Time.realtimeSinceStartup + 3f;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.isActiveAndEnabled) return l;
            return null;
        }

        private void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : 0f;
            CatCooksWeatherPreset cur = CatCooksWeatherState.Current;
            if (cur != _lastPreset)
            {
                _lastPreset = cur; _row = ForWeather(cur);
                if (Log && _row != null && Application.isPlaying && (_row.Haze + _row.Grade) > 0.01f)
                    Debug.Log("[Жара] погода «" + (cur != null ? cur.title : "?") + "» → марево " + _row.Haze.ToString("0.00") + ", миражи " + _row.Mirage.ToString("0.00") + ", ореол " + _row.Glare.ToString("0.00") + ", зной " + _row.Grade.ToString("0.00"), this);
            }
            Vector4 target = TestOverride ? new Vector4(TestHaze, TestMirage, TestGlare, TestGrade)
                : (_row != null ? new Vector4(_row.Haze, _row.Mirage, _row.Glare, _row.Grade) : Vector4.zero);
            // в мороз/снег жары нет
            if (CityWinter.Instance != null && (CityWinter.Instance.Frost > 0.05f || CityWinter.Instance.SnowCover > 0.05f || CityWinter.Instance.SnowNow > 0.05f)) target = Vector4.zero;
            _cur = Application.isPlaying ? Vector4.MoveTowards(_cur, target, dt / Mathf.Max(0.1f, WeatherBlendSeconds)) : target;
            Now = _cur;
            bool on = _cur.x + _cur.y + _cur.z + _cur.w > 0.002f;
            Shader.SetGlobalFloat(IdHeatOn, on ? 1f : 0f);
            Shader.SetGlobalVector(IdHeat, _cur);
            if (!on) return;
            Shader.SetGlobalVector(IdHeatP, new Vector4(HazePixels, WaveSize, WaveSpeed, 0f));

            // направление на солнце (для ореола; на экран проецирует шейдер — так же, как отражения)
            Vector4 sun = Vector4.zero;
            Light l = FindSun();
            if (l != null)
            {
                Vector3 toSun = -l.transform.forward;
                if (toSun.y > -0.05f) sun = new Vector4(toSun.x, toSun.y, toSun.z, 1f);
            }
            Shader.SetGlobalVector(IdSun, sun);
        }
    }
}
