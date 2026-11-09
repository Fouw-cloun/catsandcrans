using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ПОГОДА ↔ СТИЛЬ. Сменилась погода — пульт сам плавно переводит и стиль:
    /// жара → «Янтарь» (тёпло, сочно), пасмурно/хмуро/снег → «Северный» (холодно, приглушённо),
    /// перед грозой/гроза/шторм/ураган → «Грозовой фронт», после грозы → «Комикс — тёплый»,
    /// утренняя дымка → «Candy утро», розовая дымка → «Candy закат», ясно/кучевые → базовый (CityRef).
    /// Таблица — в инспекторе: «погода» и «стиль» — куски имени/названия (без учёта регистра).
    /// Плюс медленные переходы: в игре Instant Switch у пульта выключается, время смены погоды и стиля — отсюда.
    /// Повесь на объект пульта (Mood Director) или любой объект в сцене.
    /// </summary>
    [DisallowMultipleComponent]
    public class CityWeatherLink : MonoBehaviour
    {
        [System.Serializable]
        public class Pair
        {
            [Tooltip("Кусок имени файла или названия погоды (CW_05_HeatHaze → heathaze)")] public string Weather;
            [Tooltip("Кусок названия стиля (Янтарь → янтарь)")] public string Style;
            public Pair() { }
            public Pair(string w, string s) { Weather = w; Style = s; }
        }

        public CatCooksMoodDirector Mood;

        [Header("Стиль за погодой")]
        public bool LinkStyle = true;
        [Tooltip("Стиль, если погода не нашлась в таблице")]
        public string DefaultStyle = "cityref";
        public List<Pair> Pairs = Defaults();

        [Header("Медленные переходы")]
        [Tooltip("В игре выключить Instant Switch у пульта (погода и стиль всегда перетекают)")]
        public bool SmoothAlways = true;
        [Tooltip("Секунд на смену погоды (0 — из пресета)")]
        [Range(0f, 180f)] public float WeatherSeconds = 45f;
        [Tooltip("Секунд на смену стиля вслед за погодой")]
        [Range(0f, 180f)] public float StyleSeconds = 55f;

        public bool Log = true;

        private int _lastWeather = -1;

        public static List<Pair> Defaults()
        {
            return new List<Pair>
            {
                new Pair("scorcher", "янтарь"),
                new Pair("heathaze", "янтарь"),
                new Pair("duststorm", "янтарь"),
                new Pair("morningmist", "candy утро"),
                new Pair("pinkhaze", "candy закат"),
                new Pair("overcast", "северный"),
                new Pair("gloomy", "северный"),
                new Pair("snowclouds", "северный"),
                new Pair("prestorm", "грозовой фронт"),
                new Pair("thunderstorm", "грозовой фронт"),
                new Pair("tempest", "грозовой фронт"),
                new Pair("hurricane", "грозовой фронт"),
                new Pair("cozyrain", "комикс — тёплый"),
                new Pair("drizzle", "северный"),
                new Pair("steadyrain", "северный"),
                new Pair("downpour", "грозовой фронт"),
                new Pair("sunshower", "cityref"),
                new Pair("flashfreeze", "северный"),
                new Pair("blizzard", "северный"),
                new Pair("hailthunder", "грозовой фронт"),
                new Pair("hailstorm", "грозовой фронт"),
                new Pair("hail", "грозовой фронт"),
                new Pair("smog", "янтарь"),
                new Pair("groundfog", "северный"),
                new Pair("fogisland", "северный"),
                new Pair("densefog", "северный"),
                new Pair("fogbank", "северный"),
                new Pair("eclipse", "неоновая ночь"),
                new Pair("rainbow", "комикс — тёплый"),
                new Pair("afterstorm", "комикс — тёплый"),
                new Pair("sunbreaks", "комикс — тёплый"),
                new Pair("clear", "cityref"),
                new Pair("cirrus", "cityref"),
                new Pair("cumulus", "cityref"),
                new Pair("highwind", "cityref"),
                new Pair("autumnleaves", "северный"),
                new Pair("summerleaves", "комикс — тёплый"),
                new Pair("trashstorm", "cityref"),
                new Pair("snowfall", "северный"),
            };
        }

        private void Start()
        {
            if (Mood == null) Mood = FindFirstObjectByType<CatCooksMoodDirector>();
            if (Mood == null) { Debug.LogWarning("[Погода↔стиль] Mood Director не найден", this); enabled = false; return; }
            if (SmoothAlways) Mood.instantSwitch = false;
            Mood.weatherTransition = WeatherSeconds;
            if (StyleSeconds > 0.01f) Mood.styleTransition = StyleSeconds;
            _lastWeather = -1;
            MergeDefaults();
            Hook();
        }

        private void OnEnable() { if (Mood != null) Hook(); }
        private void OnDisable()
        {
            CatCooksWeatherMenu.HideStyles = false;
            if (CatCooksWeatherMenu.WeatherNote == (System.Func<int, string>)NoteFor) CatCooksWeatherMenu.WeatherNote = null;
        }

        /// <summary>Стиль — часть погоды: в меню P вкладки «Стиль» нет, у каждой погоды подписан её стиль.</summary>
        private void Hook()
        {
            CatCooksWeatherMenu.HideStyles = LinkStyle;
            CatCooksWeatherMenu.WeatherNote = NoteFor;
        }

        private string NoteFor(int weatherIndex)
        {
            if (Mood == null || weatherIndex < 0 || weatherIndex >= Mood.weathers.Count || Mood.weathers[weatherIndex] == null) return null;
            int si = StyleFor(Mood.weathers[weatherIndex]);
            return si >= 0 && Mood.styles[si] != null ? Mood.styles[si].title : null;
        }

        /// <summary>Таблица в сцене сохранена старой — новые погоды (заморозки, метель…) дописываем, правки не трогаем.</summary>
        public void MergeDefaults()
        {
            List<Pair> d = Defaults();
            int added = 0;
            for (int i = 0; i < d.Count; i++)
            {
                bool has = false;
                for (int k = 0; k < Pairs.Count; k++)
                    if (Pairs[k] != null && Pairs[k].Weather == d[i].Weather) { has = true; break; }
                if (!has) { Pairs.Insert(Mathf.Min(i, Pairs.Count), d[i]); added++; }
            }
            if (added > 0 && Log) Debug.Log("[Погода↔стиль] в таблицу добавлено новых погод: " + added, this);
        }

        private void Update()
        {
            if (Mood == null || !LinkStyle) return;
            if (SmoothAlways && Mood.instantSwitch) Mood.instantSwitch = false;
            if (Mood.weatherIndex == _lastWeather) return;
            bool first = _lastWeather < 0;
            _lastWeather = Mood.weatherIndex;
            if (_lastWeather < 0 || _lastWeather >= Mood.weathers.Count) return;
            CatCooksWeatherPreset w = Mood.weathers[_lastWeather];
            if (w == null) return;
            int si = StyleFor(w);
            if (si < 0 || si == Mood.styleIndex) return;
            Mood.SetStyle(si, first ? 0f : (CatCooksWeatherMenu.TestFast ? CatCooksWeatherMenu.TestSeconds : StyleSeconds));
            if (Log) Debug.Log("[Погода↔стиль] «" + w.title + "» → стиль «" + Mood.styles[si].title + "»", this);
        }

        /// <summary>Индекс стиля для погоды (−1 — не нашёлся).</summary>
        public int StyleFor(CatCooksWeatherPreset w)
        {
            string key = (w.name + " " + w.title).ToLowerInvariant().Replace("_", "");
            string style = DefaultStyle;
            for (int i = 0; i < Pairs.Count; i++)
            {
                Pair p = Pairs[i];
                if (p == null || string.IsNullOrEmpty(p.Weather)) continue;
                if (key.Contains(p.Weather.ToLowerInvariant().Replace("_", ""))) { style = p.Style; break; }
            }
            return FindStyle(style);
        }

        public int FindStyle(string part)
        {
            if (string.IsNullOrEmpty(part)) return -1;
            string k = part.ToLowerInvariant();
            for (int i = 0; i < Mood.styles.Count; i++)
            {
                CatCooksSkyStyle s = Mood.styles[i];
                if (s == null) continue;
                if ((s.title != null && s.title.ToLowerInvariant().Contains(k)) || s.name.ToLowerInvariant().Contains(k)) return i;
            }
            return -1;
        }
    }
}
