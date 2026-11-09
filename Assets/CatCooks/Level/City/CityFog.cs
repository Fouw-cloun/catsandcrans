using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ТУМАН в игровой зоне (небо не трогаем) — тем же проходом, что мокрость/снег/жара (City Rain Surface):
    ///   • НИЗКИЙ: слой от пола до половины роста игрока, верх волнуется и плывёт по ветру; под ногами ничего не видно;
    ///   • ВОКРУГ ЭТАЖА: за краем игровой зоны и ниже пола всё тонет в тумане — этаж как остров в молоке;
    ///   • ГУСТОЙ: туман везде, видно на несколько метров, внутри плывут клубы.
    /// Цвет — холодный, от света неба. Сила — по таблице «Погода → туман» или тест (меню P → Город → ТУМАН).
    /// </summary>
    [ExecuteAlways]
    public class CityFog : MonoBehaviour
    {
        public static CityFog Instance { get; private set; }

        [Serializable]
        public class WeatherFog
        {
            [Tooltip("Кусок имени/названия погоды (CW_37_GroundFog → groundfog)")] public string Weather;
            [Tooltip("Низкий туман (по пол-игрока)")] [Range(0f, 1f)] public float Ground;
            [Tooltip("Туман вокруг этажа")] [Range(0f, 1f)] public float Around;
            [Tooltip("Густой туман везде")] [Range(0f, 1f)] public float Dense;
            public WeatherFog() { }
            public WeatherFog(string w, float g, float a, float d) { Weather = w; Ground = g; Around = a; Dense = d; }
        }

        [Header("Погода → туман (не нашлась — нет)")]
        public List<WeatherFog> WeatherTable = DefaultTable();
        public float WeatherBlendSeconds = 10f;

        [Header("Тест (меню P)")]
        public bool TestOverride;
        [Range(0f, 1f)] public float TestGround = 1f;
        [Range(0f, 1f)] public float TestAround = 0f;
        [Range(0f, 1f)] public float TestDense = 0f;

        [Header("Низкий туман")]
        [Tooltip("Высота слоя, м (0 — половина роста игрока)")]
        public float GroundHeight = 0f;
        [Tooltip("Плотность (больше — пол под туманом совсем не видно)")]
        public float GroundDensity = 5f;
        [Tooltip("Волны верхней кромки 0..1")]
        [Range(0f, 1f)] public float TopWaves = 0.45f;

        [Header("Туман вокруг этажа")]
        [Tooltip("Насколько за краем зоны туман становится стеной, м")]
        public float AroundLength = 5f;
        [Tooltip("Отступ стены от края зоны наружу, м")]
        public float AroundMargin = 1f;

        [Header("Густой туман")]
        [Tooltip("Видимость, м")]
        public float DenseVisibility = 7f;

        [Header("Цвет и движение")]
        [Tooltip("Оттенок тумана (умножается на свет неба) — холодный")]
        public Color Tint = new Color(0.78f, 0.86f, 0.95f, 1f);
        [Tooltip("Яркость тумана")]
        [Range(0.3f, 2f)] public float Brightness = 1.05f;
        [Tooltip("Скорость дрейфа, м/с (направление — ветер)")]
        public float Drift = 0.35f;
        [Tooltip("Игрок (пусто — по тегу Player, иначе CharacterController)")]
        public Transform Player;
        public bool Log = true;

        public Vector3 Now { get; private set; }   // x низкий, y вокруг, z густой

        private WeatherFog _row;
        private CatCooksWeatherPreset _lastPreset;
        private Vector3 _cur;
        private float _baseY, _playerH = 0.5f, _nextFind;
        private bool _baseInit;
        private Vector4 _rect;
        private Vector2 _drift;
        private static readonly Vector3[] _upDir = { Vector3.up };
        private static readonly Color[] _ambRes = new Color[1];

        static readonly int IdFogOn = Shader.PropertyToID("_CC_FogOn");
        static readonly int IdFog = Shader.PropertyToID("_CC_Fog");
        static readonly int IdFogP = Shader.PropertyToID("_CC_FogP");
        static readonly int IdFogP2 = Shader.PropertyToID("_CC_FogP2");
        static readonly int IdFogRect = Shader.PropertyToID("_CC_FogRect");
        static readonly int IdFogCol = Shader.PropertyToID("_CC_FogCol");
        static readonly int IdMeter = Shader.PropertyToID("_CC_Meter");

        public static List<WeatherFog> DefaultTable()
        {
            return new List<WeatherFog>
            {
                new WeatherFog("groundfog", 1f, 0f, 0f),       // низкий туман: по пол-игрока
                new WeatherFog("fogisland", 0.25f, 1f, 0.1f),  // туман вокруг этажа: остров в молоке
                new WeatherFog("densefog", 0.4f, 0.6f, 1f),    // густой туман везде
                new WeatherFog("fogbank", 0.2f, 0.7f, 0.15f),  // туман-стена (была): стена за краем
                new WeatherFog("morningmist", 0.55f, 0.1f, 0.05f), // утренняя дымка (была): лёгкий по полу
                new WeatherFog("drizzle", 0.15f, 0.2f, 0.1f),  // морось: сыро
            };
        }

        private void MergeTable()
        {
            if (WeatherTable == null) WeatherTable = new List<WeatherFog>();
            List<WeatherFog> d = DefaultTable();
            for (int i = 0; i < d.Count; i++)
            {
                bool has = false;
                foreach (WeatherFog r in WeatherTable) if (r != null && r.Weather == d[i].Weather) { has = true; break; }
                if (!has) WeatherTable.Insert(Mathf.Min(i, WeatherTable.Count), d[i]);
            }
        }

        /// <summary>Строка для погоды (самый длинный совпавший ключ).</summary>
        public WeatherFog ForWeather(CatCooksWeatherPreset w)
        {
            if (w == null || WeatherTable == null) return null;
            string key = (w.name + " " + w.title).ToLowerInvariant().Replace("_", "");
            WeatherFog best = null;
            int bestLen = 0;
            foreach (WeatherFog r in WeatherTable)
            {
                if (r == null || string.IsNullOrEmpty(r.Weather)) continue;
                string k = r.Weather.ToLowerInvariant().Replace("_", "");
                if (k.Length > bestLen && key.Contains(k)) { best = r; bestLen = k.Length; }
            }
            return best;
        }

        private void OnEnable() { Instance = this; MergeTable(); _baseInit = false; }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalFloat(IdFogOn, 0f);
            Shader.SetGlobalVector(IdFog, Vector4.zero);
        }

        private float Meter()
        {
            if (CityRain.Instance != null) return CityRain.Instance.Meter;
            float m = Shader.GetGlobalFloat(IdMeter);
            return m > 0f ? m : 3.5f;
        }

        private Transform FindPlayer()
        {
            if (Player != null) return Player;
            if (Time.realtimeSinceStartup < _nextFind) return null;
            _nextFind = Time.realtimeSinceStartup + 2f;
            GameObject go = null;
            try { go = GameObject.FindWithTag("Player"); } catch (UnityException) { }
            if (go != null) { Player = go.transform; return Player; }
            CharacterController cc = FindFirstObjectByType<CharacterController>();
            if (cc != null) { Player = cc.transform; return Player; }
            return null;
        }

        /// <summary>Пол и рост игрока: низ CharacterController/коллайдеров — пол, высота — рост.</summary>
        private bool PlayerFeet(float m, out float feet)
        {
            feet = 0f;
            Transform p = FindPlayer();
            if (p == null) return false;
            CharacterController cc = p.GetComponent<CharacterController>();
            if (cc != null)
            {
                float s = p.lossyScale.y;
                feet = p.position.y + (cc.center.y - cc.height * 0.5f) * s;
                _playerH = cc.height * s;
                return true;
            }
            Collider c = p.GetComponentInChildren<Collider>();
            if (c != null) { feet = c.bounds.min.y; _playerH = c.bounds.size.y; return true; }
            feet = p.position.y;
            _playerH = 0.5f * m;
            return true;
        }

        private Vector4 ZoneRect(float m, Vector3 around)
        {
            CityRainZone best = null;
            foreach (CityRainZone z in FindObjectsByType<CityRainZone>(FindObjectsSortMode.None))
            {
                if (z == null || !z.isActiveAndEnabled || z.Kind == CityRainZone.ZoneKind.Light) continue;   // туман ходит вокруг красной зоны
                if (z.Contains(around, 0f)) { best = z; break; }
                if (best == null) best = z;
            }
            float mg = AroundMargin * m;
            if (best != null)
            {
                Bounds b = best.WorldBounds;
                return new Vector4(b.min.x - mg, b.min.z - mg, b.max.x + mg, b.max.z + mg);
            }
            float r = 12f * m;                                                              // зоны нет — круг вокруг игрока
            return new Vector4(around.x - r, around.z - r, around.x + r, around.z + r);
        }

        private void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : 0f;
            float m = Meter();
            CatCooksWeatherPreset cur = CatCooksWeatherState.Current;
            if (cur != _lastPreset)
            {
                _lastPreset = cur; _row = ForWeather(cur);
                if (Log && _row != null && Application.isPlaying)
                    Debug.Log("[Туман] погода «" + (cur != null ? cur.title : "?") + "» → низкий " + _row.Ground.ToString("0.00") + ", вокруг " + _row.Around.ToString("0.00") + ", густой " + _row.Dense.ToString("0.00"), this);
            }
            Vector3 target = TestOverride ? new Vector3(TestGround, TestAround, TestDense) : (_row != null ? new Vector3(_row.Ground, _row.Around, _row.Dense) : Vector3.zero);
            _cur = Application.isPlaying ? Vector3.MoveTowards(_cur, target, dt / Mathf.Max(0.1f, WeatherBlendSeconds)) : target;
            Now = _cur;
            bool on = _cur.x + _cur.y + _cur.z > 0.002f;
            Shader.SetGlobalFloat(IdFogOn, on ? 1f : 0f);
            Shader.SetGlobalVector(IdFog, new Vector4(_cur.x, _cur.y, _cur.z, 0f));
            if (!on) return;

            // пол — под ногами игрока (плавно), иначе низ зоны
            Camera cam = Camera.main;
            Vector3 around = cam != null ? cam.transform.position : transform.position;
            float feet;
            if (PlayerFeet(m, out feet)) around = new Vector3(Player.position.x, feet, Player.position.z);
            else
            {
                feet = around.y - 1.5f * m;                                                    // игрока нет — пол зоны
                foreach (CityRainZone z in FindObjectsByType<CityRainZone>(FindObjectsSortMode.None))
                    if (z != null && z.Kind != CityRainZone.ZoneKind.Light && z.Contains(around, 0f)) { feet = z.WorldBounds.min.y; break; }
            }
            if (!_baseInit) { _baseY = feet; _baseInit = true; }
            _baseY = Mathf.Lerp(_baseY, feet, Application.isPlaying ? 1f - Mathf.Exp(-dt * 0.8f) : 1f);   // прыжок не дёргает туман
            _rect = ZoneRect(m, around);

            float h = GroundHeight > 0f ? GroundHeight * m : Mathf.Max(0.15f * m, _playerH * 0.5f);
            Vector2 wd = CatCooksWeatherState.WindDir.sqrMagnitude > 1e-4f ? CatCooksWeatherState.WindDir.normalized : new Vector2(0.8f, 0.6f);
            _drift = wd * Drift * (0.5f + CatCooksWeatherState.Wind);                         // в долях шума в секунду (шум ~2 м)
            Shader.SetGlobalVector(IdFogP, new Vector4(_baseY, h, GroundDensity / m, Mathf.Max(0.5f, AroundLength) * m));
            Shader.SetGlobalVector(IdFogP2, new Vector4(Mathf.Max(0.5f, DenseVisibility) * m, TopWaves, _drift.x * 0.4f, _drift.y * 0.4f));
            Shader.SetGlobalVector(IdFogRect, _rect);

            RenderSettings.ambientProbe.Evaluate(_upDir, _ambRes);
            Color a = _ambRes[0];
            Color c = new Color(a.r * Tint.r, a.g * Tint.g, a.b * Tint.b) * Brightness * 1.25f;
            Shader.SetGlobalVector(IdFogCol, new Vector4(c.r, c.g, c.b, 1f));
        }
    }
}
