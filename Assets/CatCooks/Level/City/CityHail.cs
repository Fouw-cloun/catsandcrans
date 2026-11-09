using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ГРАД (визуал). Градины падают быстро, бьются о пол, крыши, столы и ящики (высота — из карты укрытий дождя),
    /// отскакивают дважды с разлётом ледяной крошки, лежат и тают. На полу копится россыпь ледяных бусин
    /// (тем же проходом, что мокрость и снег — City Rain Surface), после града тает.
    /// Погоды: «Град» (с дождём), «Буря с градом» (сухо, ветер, зарницы), «Гроза с градом» (ливень, молнии, град).
    /// Сила — по таблице «Погода → град» или тест (меню P → Город → ГРАД).
    /// </summary>
    [ExecuteAlways]
    public class CityHail : MonoBehaviour
    {
        public static CityHail Instance { get; private set; }

        [Serializable]
        public class WeatherHail
        {
            [Tooltip("Кусок имени/названия погоды (CW_34_HailStorm → hailstorm)")] public string Weather;
            [Range(0f, 1f)] public float Amount;
            [Tooltip("Размер градин (×)")] public float Size = 1f;
            public WeatherHail() { }
            public WeatherHail(string w, float a, float s) { Weather = w; Amount = a; Size = s; }
        }

        [Header("Материал (ставит меню «Град: поставить в сцену»)")]
        public Material HailMaterial;

        [Header("Погода → град (не нашлась — нет града)")]
        public List<WeatherHail> WeatherTable = DefaultTable();
        public float WeatherBlendSeconds = 4f;

        [Header("Тест (меню P)")]
        public bool TestOverride;
        [Range(0f, 1f)] public float TestAmount = 0.8f;
        [Range(0.3f, 3f)] public float TestSize = 1f;

        [Header("Градины")]
        [Range(200, 12000)] public int MaxStones = 1700;
        [Tooltip("Коробка вокруг камеры, м: ширина, высота падения")]
        public Vector2 Box = new Vector2(22f, 14f);
        [Tooltip("Радиус градины, м")]
        public float StoneSize = 0.048f;
        [Tooltip("Скорость падения, м/с")]
        public float FallSpeed = 11f;
        [Tooltip("Высота отскока, м")]
        public float BounceHeight = 0.35f;
        [Tooltip("Сколько градина лежит, с (потом тает)")]
        public float LieSeconds = 3f;
        [Tooltip("Выдержка следа падения, с")]
        public float StreakShutter = 0.025f;

        [Header("Град на полу")]
        [Tooltip("За сколько секунд сильного града пол густо засыпает")]
        public float AccumulateSeconds = 35f;
        [Tooltip("За сколько секунд тает после града")]
        public float MeltSeconds = 50f;
        [Tooltip("За сколько секунд высыхают мокрые пятна от растаявших градин")]
        public float WetSeconds = 120f;

        public bool Log = true;

        [SerializeField, HideInInspector] private int _version;
        private const int Version = 2;

        public float Amount { get; private set; }
        public float Cover { get; private set; }
        public float Wet { get; private set; }

        private WeatherHail _row;
        private CatCooksWeatherPreset _lastPreset;
        private float _target, _sizeMul = 1f;
        private Vector2 _windSm;

        static readonly int IdHailP = Shader.PropertyToID("_CC_HailP");
        static readonly int IdHailP2 = Shader.PropertyToID("_CC_HailP2");
        static readonly int IdHailWind = Shader.PropertyToID("_CC_HailWind");
        static readonly int IdHailCover = Shader.PropertyToID("_CC_HailCover");
        static readonly int IdHailOn = Shader.PropertyToID("_CC_HailOn");
        static readonly int IdHailWet = Shader.PropertyToID("_CC_HailWet");
        static readonly int IdAmb = Shader.PropertyToID("_CC_Amb");
        static readonly int IdMeter = Shader.PropertyToID("_CC_Meter");
        private static readonly Vector3[] _upDir = { Vector3.up };
        private static readonly Color[] _ambRes = new Color[1];

        public static List<WeatherHail> DefaultTable()
        {
            return new List<WeatherHail>
            {
                new WeatherHail("hailthunder", 0.85f, 1.1f),   // гроза с градом: ливень + крупный град
                new WeatherHail("hailstorm", 1f, 1.3f),        // буря с градом: сухо, ветер, самый сильный и крупный
                new WeatherHail("hail", 0.6f, 0.9f),           // град (с дождём)
            };
        }

        private void MergeTable()
        {
            if (WeatherTable == null) WeatherTable = new List<WeatherHail>();
            List<WeatherHail> d = DefaultTable();
            for (int i = 0; i < d.Count; i++)
            {
                bool has = false;
                foreach (WeatherHail r in WeatherTable) if (r != null && r.Weather == d[i].Weather) { has = true; break; }
                if (!has) WeatherTable.Insert(Mathf.Min(i, WeatherTable.Count), d[i]);
            }
        }

        /// <summary>Строка для погоды: побеждает самый длинный совпавший ключ («hailstorm» точнее, чем «hail»).</summary>
        public WeatherHail ForWeather(CatCooksWeatherPreset w)
        {
            if (w == null || WeatherTable == null) return null;
            string key = (w.name + " " + w.title).ToLowerInvariant().Replace("_", "");
            WeatherHail best = null;
            int bestLen = 0;
            foreach (WeatherHail r in WeatherTable)
            {
                if (r == null || string.IsNullOrEmpty(r.Weather)) continue;
                string k = r.Weather.ToLowerInvariant().Replace("_", "");
                if (k.Length > bestLen && key.Contains(k)) { best = r; bestLen = k.Length; }
            }
            return best;
        }

        private void OnEnable()
        {
            Instance = this; MergeTable();
            if (_version < Version)
            {
                // v1.2: градины втрое крупнее, их втрое меньше (на уже стоящем объекте)
                StoneSize = 0.048f; MaxStones = 1700; BounceHeight = 0.35f;
                _version = Version;
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalFloat(IdHailOn, 0f);
            Shader.SetGlobalFloat(IdHailCover, 0f);
            Shader.SetGlobalFloat(IdHailWet, 0f);
        }

        private float Meter()
        {
            if (CityRain.Instance != null) return CityRain.Instance.Meter;
            float m = Shader.GetGlobalFloat(IdMeter);
            return m > 0f ? m : 3.5f;
        }

        // ── кнопки меню P ──
        public void CoverNow() { Cover = 1f; }
        public void ClearNow() { Cover = 0f; Wet = 0f; }

        private void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : 0f;
            float m = Meter();
            CityWeatherArea.Refresh();
            CatCooksWeatherPreset cur = CatCooksWeatherState.Current;
            if (cur != _lastPreset)
            {
                _lastPreset = cur; _row = ForWeather(cur);
                if (Log && _row != null && Application.isPlaying) Debug.Log("[Град] погода «" + (cur != null ? cur.title : "?") + "» → град " + _row.Amount.ToString("0.00") + ", размер ×" + _row.Size.ToString("0.0"), this);
            }
            _target = TestOverride ? TestAmount : (_row != null ? _row.Amount : 0f);
            float sizeT = TestOverride ? TestSize : (_row != null ? _row.Size : 1f);
            if (Application.isPlaying)
            {
                float k = dt / Mathf.Max(0.1f, WeatherBlendSeconds);
                Amount = Mathf.MoveTowards(Amount, _target, k);
                _sizeMul = Mathf.MoveTowards(_sizeMul, sizeT, k);
                if (Amount > 0.01f) Cover = Mathf.Min(Mathf.Clamp01(Amount * 1.2f), Cover + dt * Amount / Mathf.Max(1f, AccumulateSeconds));
                else Cover = Mathf.Max(0f, Cover - dt / Mathf.Max(1f, MeltSeconds));
                // мокрые пятна: пока лежит град — не меньше его, потом сохнут медленнее, чем тает лёд
                Wet = Cover > Wet ? Cover : Mathf.Max(0f, Wet - dt / Mathf.Max(1f, WetSeconds));
            }
            else { Amount = _target; _sizeMul = sizeT; }

            bool on = Amount > 0.002f || Cover > 0.002f || Wet > 0.002f;
            Shader.SetGlobalFloat(IdHailOn, on ? 1f : 0f);
            Shader.SetGlobalFloat(IdHailCover, Cover);
            Shader.SetGlobalFloat(IdHailWet, Wet);
            if (!on) return;

            // окружение (то же, что у снега): в полноэкранном проходе своего SH нет
            RenderSettings.ambientProbe.Evaluate(_upDir, _ambRes);
            Color a = _ambRes[0];
            Shader.SetGlobalVector(IdAmb, new Vector4(Mathf.Max(0f, a.r), Mathf.Max(0f, a.g), Mathf.Max(0f, a.b), 1f));

            Vector2 wd = CatCooksWeatherState.WindDir.sqrMagnitude > 1e-4f ? CatCooksWeatherState.WindDir.normalized : new Vector2(0.8f, 0.6f);
            float windMs = (0.3f + CatCooksWeatherState.Wind * 2.5f) * (1f + CityStorm.GustNow * 1.2f);
            _windSm = Vector2.Lerp(_windSm, wd * windMs * m, 1f - Mathf.Exp(-Mathf.Max(dt, 0.016f) * 2f));
            float qk = CityWeatherArea.CamFull > 0.5f ? 1f : 0.35f;                          // жёлтая зона: градин втрое меньше
            float thin = Mathf.Min(1f, CityWeatherArea.CamK / qk);
            Shader.SetGlobalVector(IdHailP, new Vector4(Mathf.Clamp01(Amount) * thin, FallSpeed * m, StoneSize * _sizeMul * m, Box.x * m));
            Shader.SetGlobalVector(IdHailP2, new Vector4(Box.y * m, BounceHeight * m * Mathf.Lerp(0.8f, 1.2f, _sizeMul - 0.5f), LieSeconds, 9.8f * m));
            Shader.SetGlobalVector(IdHailWind, new Vector4(_windSm.x, _windSm.y, StreakShutter, 0f));

            if (HailMaterial != null && Amount > 0.002f && CityWeatherArea.CamK > 0.01f)
            {
                Camera cam = Camera.main;
                Vector3 cp = cam != null ? cam.transform.position : transform.position;
                RenderParams rp = new RenderParams(HailMaterial) { worldBounds = new Bounds(cp, Vector3.one * 1e5f), shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 12, Mathf.Max(1, Mathf.RoundToInt(MaxStones * qk)));   // 6 — градина, 6 — крошка при ударе
            }
        }
    }
}
