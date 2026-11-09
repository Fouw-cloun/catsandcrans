using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ОКНА ГОРОДА НОЧЬЮ — мост от Atmosphere к шейдерам города.
    /// Туман, свет, небо, погоду и день/ночь делает Atmosphere (Mood Director, Day Cycle, Fog Controller) — тут их нет.
    /// Этот компонент только берёт «ночь» из CatCooksDayCycle (Night01, плюс чуть раньше — на закате, Golden01)
    /// и отдаёт шейдерам окон: _CC_Night, сколько окон горит, как часто переключаются.
    /// Дождь и тучи шейдеры города берут сами (_CatWetness, _GlobalCloudDarkness из Atmosphere).
    /// Один на сцену: CatCooks → Застройка → Окна ночью (мост к Day Cycle). Работает и в редакторе.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("CatCooks/Город/Окна ночью (мост к Day Cycle)")]
    public sealed class CityAtmosphere : MonoBehaviour
    {
        public enum NightSource { DayCycle = 0, Manual = 1 }

        [Header("Ночь")]
        public NightSource Source = NightSource.DayCycle;
        [Tooltip("Day Cycle из Atmosphere. Пусто — найдётся сам")]
        public CatCooksDayCycle DayCycle;
        [Tooltip("Ручная ночь (Source = Manual или нет Day Cycle): 0 — день, 1 — ночь")]
        [Range(0f, 1f)] public float Night = 0f;
        [Tooltip("Зажигать окна уже на закате/рассвете: сколько «золотого часа» прибавить к ночи")]
        [Range(0f, 1f)] public float FromGolden = 0.35f;
        [Tooltip("Подкрутить кривую: >1 — окна загораются позже и резче, <1 — раньше и плавнее")]
        [Range(0.3f, 3f)] public float Curve = 1f;

        [Header("Окна")]
        [Tooltip("Доля горящих окон ночью")]
        [Range(0f, 1f)] public float LitRatio = 0.45f;
        [Tooltip("Как часто окна включаются/гаснут (у ~15% окон)")]
        [Range(0f, 0.5f)] public float WindowFlicker = 0.03f;
        [Tooltip("«Квартира», м: ширина и этаж. Окна одной квартиры горят вместе; этажи отсчитываются от земли")]
        public Vector2 ApartmentSize = new Vector2(6f, 3.2f);
        [Tooltip("Небоскрёбы-офисы ночью: доля окон, где ещё работают (ниже светящейся полосы)")]
        [Range(0f, 0.3f)] public float OfficeLateRatio = 0.03f;
        [Tooltip("Особые окна в ближнем слое: фиолетовые фитолампы и вечеринки (цвет мигает). 0 — нет, 1 — пара-тройка, 3 — много")]
        [Range(0f, 3f)] public float SpecialWindows = 1f;
        [Tooltip("Небоскрёбы (режим «Частично»): сколько окон горит — доля от жилых домов")]
        [Range(0f, 1f)] public float OfficeLitShare = 0.6f;
        [Tooltip("Тёмная погода (затмение, шторм, гроза) зажигает окна и днём: 0 — нет, 1 — да")]
        [Range(0f, 1f)] public float WeatherLights = 1f;

        [Header("Небо: ярусы облаков (по погоде сами; тут — сила каждого)")]
        [Tooltip("Верхний ярус — перистые пряди (ясно, перистые, перед грозой)")]
        [Range(0f, 2f)] public float SkyCirrus = 1f;
        [Tooltip("Нижняя пелена — слоистые пласты (пасмурно, снег)")]
        [Range(0f, 2f)] public float SkySheet = 1f;
        [Tooltip("Рваные тёмные тучи низко (гроза, шторм, ураган)")]
        [Range(0f, 2f)] public float SkyScud = 1f;
        [Tooltip("Кучевые комки — плотность")]
        [Range(0f, 2f)] public float SkyCumulus = 1f;

        [Header("Тени облаков — только над игровой зоной")]
        [Tooltip("Радиус зоны теней в модулях вокруг центра уровня (0 — не трогать Cloud Shadows)")]
        [Range(0f, 6f)] public float ShadowZoneModules = 1.5f;

        [Header("Небо (шейдер Sky Toon) — эти настройки погода и стиль не перетирают")]
        [Tooltip("Скорость облаков: 1 — как в первой версии, 0.25 — вчетверо медленнее")]
        [Range(0f, 2f)] public float SkySpeed = 0.25f;
        [Tooltip("Больше — облака выше и мельче")]
        [Range(0.3f, 4f)] public float SkyCloudsHigher = 1.4f;
        [Tooltip("Сколько облаков от покрытия погоды: 1 — как велит погода, 0.6 — реже")]
        [Range(0f, 1.5f)] public float SkyCloudsAmount = 0.75f;
        [Tooltip("Размер скоплений: больше — облака равномернее по всему небу, меньше — большие ясные «окна»")]
        [Range(0.3f, 3f)] public float SkyClusterSpread = 1.6f;

        private static readonly int IdNight = Shader.PropertyToID("_CC_Night");
        private static readonly int IdLit = Shader.PropertyToID("_CC_LitRatio");
        private static readonly int IdFlick = Shader.PropertyToID("_CC_WindowFlicker");
        private static readonly int IdGround = Shader.PropertyToID("_CC_GroundY");
        private static readonly int IdMeter = Shader.PropertyToID("_CC_Meter");
        private static readonly int IdApt = Shader.PropertyToID("_CC_Apt");
        private static readonly int IdSkyW = Shader.PropertyToID("_CC_SkyW");
        private static readonly int IdSkyWOn = Shader.PropertyToID("_CC_SkyWOn");
        private static readonly int IdSkyLayers = Shader.PropertyToID("_CC_SkyLayers");
        private CatCooksMoodDirector _mood;
        private float _groundY, _meter = 3.5f, _measureAt = -1f;
        private Vector3 _specialCenter; private float _specialRadius;
        private static readonly int IdSpecial = Shader.PropertyToID("_CC_SpecialZone");
        private static readonly int IdSkyOff = Shader.PropertyToID("_CC_SkyOffset");
        private static readonly int IdSkyEvo = Shader.PropertyToID("_CC_SkyEvolveT");
        private static readonly int IdSkyDriven = Shader.PropertyToID("_CC_SkyDriven");
        private static readonly int IdSkySize = Shader.PropertyToID("_CC_SkySize");
        private static readonly int IdSkyAmount = Shader.PropertyToID("_CC_SkyAmount");
        private static readonly int IdSkyForm = Shader.PropertyToID("_CC_SkyForm");
        private static readonly int IdWind = Shader.PropertyToID("_Wind");
        private static readonly int IdEvolve = Shader.PropertyToID("_CloudEvolve");
        private Vector2 _skyOff;
        private float _skyEvo, _lastT = -1f;

        /// <summary>Текущая ночь для окон 0..1 (фонари, звуки — можно читать отсюда).</summary>
        public float CurrentNight { get; private set; }

        private float _findAt;

        private void OnEnable() { _measureAt = -1f; Push(); CatCooksWeatherMenu.CityTab = MenuTab; }
        private void OnValidate() { _measureAt = -1f; Push(); }

        /// <summary>Высота земли и «единиц в метре» города — для тени у земли в шейдере домов. Редко: замер не бесплатный.</summary>
        private void Measure()
        {
            CityBlockGenerator g = GetComponent<CityBlockGenerator>();
            if (g == null) g = FindFirstObjectByType<CityBlockGenerator>();
            if (g != null)
            {
                Vector3 c; Quaternion r;
                g.ModuleFrame(0, 0, out c, out r);
                _groundY = c.y;
                CityProps p = g.GetComponent<CityProps>();
                _meter = p != null ? p.Meter() : 3.5f;
                ShadowZone(g);
                // зона особых окон — ближний слой вокруг уровня
                Vector3 lc;
                if (g.LevelCenter(out lc))
                {
                    Vector2 ms = g.ModuleSize();
                    _specialCenter = lc;
                    _specialRadius = Mathf.Max(ms.x, ms.y) * (1.2f + g.Ring1Count);
                }
            }
            _measureAt = Time.realtimeSinceStartup + (Application.isPlaying ? 1e6f : 5f);
        }
        private void Update() { Push(); }

        /// <summary>Тени облаков (Atmosphere Cloud Shadows) — только над центром уровня, а не по всему городу.</summary>
        private void ShadowZone(CityBlockGenerator g)
        {
            if (ShadowZoneModules <= 0f) return;
            CatCooksCloudShadows cs = FindFirstObjectByType<CatCooksCloudShadows>();
            if (cs == null || cs.zoneCenter != null) return;
            Vector3 lc;
            if (!g.LevelCenter(out lc)) return;
            float m = Mathf.Min(g.ModuleSize().x, g.ModuleSize().y);
            cs.zoneCenterXZ = new Vector2(lc.x, lc.z);
            cs.zoneRadius = m * ShadowZoneModules;
            cs.zoneFeather = m * 0.6f;
        }
        private void OnDisable()
        {
            Shader.SetGlobalFloat(IdNight, 0f); Shader.SetGlobalFloat(IdSkyDriven, 0f);
            if (CatCooksWeatherMenu.CityTab == (System.Func<float, bool, float>)MenuTab) CatCooksWeatherMenu.CityTab = null;
        }

        // ─────────────── вкладка «ГОРОД» в меню P (4) ───────────────
        private CityWeatherLink _link;
        private const float RowH = 26f;

        /// <summary>Рисует вкладку «ГОРОД» меню P: связка погоды и стиля, окна ночью, ярусы неба, тени облаков.</summary>
        private float MenuTab(float w, bool draw)
        {
            if (_link == null) _link = FindFirstObjectByType<CityWeatherLink>();
            int rows = 21 + (_link != null ? 2 : 0) + (CityRain.Instance != null ? 14 : 0) + (CityStorm.Instance != null ? 14 : 0) + (CityWinter.Instance != null ? 8 : 0) + (CityHail.Instance != null ? 5 : 0) + (CityHeat.Instance != null ? 6 : 0) + (CityFog.Instance != null ? 5 : 0);
            if (!draw) return rows * RowH + 20f;
            float y = 0f;
            Head(ref y, w, "ПОГОДА (стиль идёт за ней сам)");
            if (_link != null)
            {
                _link.StyleSeconds = Slider(ref y, w, "Смена стиля, с", _link.StyleSeconds, 2f, 120f);
                _link.WeatherSeconds = Slider(ref y, w, "Смена погоды, с", _link.WeatherSeconds, 2f, 120f);
            }
            else { GUI.Label(new Rect(4, y, w - 8, RowH), "нет CityWeatherLink — меню «Погода ↔ стиль: связать»"); y += RowH; }

            CityRain rain = CityRain.Instance;
            if (rain != null)
            {
                Head(ref y, w, "ДОЖДЬ · сейчас: " + (rain.Intensity > 0.002f ? rain.CurrentName + " " + (rain.Intensity * 100f).ToString("0") + "%" : "нет"));
                if (GUI.Button(new Rect(6, y, w - 12, RowH - 4), "Вид: " + rain.ModeLabel + "  (клик — следующий)")) rain.NextMode();
                y += RowH;
                rain.TestOverride = GUI.Toggle(new Rect(6, y, w - 12, RowH - 4), rain.TestOverride, "  Тест: своя сила дождя (вместо погоды)");
                y += RowH;
                rain.TestIntensity = Slider(ref y, w, "Сила дождя (тест)", rain.TestIntensity, 0f, 1f);
                rain.StreakAmount = Slider(ref y, w, "Капли", rain.StreakAmount, 0f, 2f);
                rain.SplashAmount = Slider(ref y, w, "Всплески", rain.SplashAmount, 0f, 2f);
                rain.CurtainAmount = Slider(ref y, w, "Завеса вдали", rain.CurtainAmount, 0f, 2f);
                GUI.Label(new Rect(6, y + 2, w * 0.46f, RowH - 4), "Мокро " + (rain.Wetness * 100f).ToString("0") + "% · " + CityRain.WetStage(rain.Wetness));
                if (GUI.Button(new Rect(w * 0.48f, y, w * 0.24f, RowH - 4), "Намочить")) rain.SetWetness(1f);
                if (GUI.Button(new Rect(w * 0.74f, y, w * 0.24f, RowH - 4), "Высушить")) rain.SetWetness(0f);
                y += RowH;
                rain.WetOnlyMarked = GUI.Toggle(new Rect(6, y, w - 12, RowH - 4), rain.WetOnlyMarked, "  Мокнет только отмеченное (модули этажа: " + CityWetSurface.All.Count + ")");
                y += RowH;
                rain.PuddleAmount = Slider(ref y, w, "Лужи", rain.PuddleAmount, 0f, 2f);
                rain.RippleStrength = Slider(ref y, w, "Рябь", rain.RippleStrength, 0f, 2f);
                rain.Reflections = Slider(ref y, w, "Отражения в лужах", rain.Reflections, 0f, 1.5f);
                rain.DripAmount = Slider(ref y, w, "Капель с краёв (точек " + rain.DripPoints + ")", rain.DripAmount, 0f, 2f);
                rain.SteamAmount = Slider(ref y, w, "Пар после дождя" + (rain.Drying > 0.5f ? " · сохнет" : ""), rain.SteamAmount, 0f, 2f);
            }

            CityStorm storm = CityStorm.Instance;
            if (storm != null)
            {
                Head(ref y, w, "ШТОРМ · гроза " + (storm.Activity * 100f).ToString("0") + "% · ветер " + (storm.Windiness * 100f).ToString("0") + "% · порыв " + (CityStorm.GustNow * 100f).ToString("0") + "%");
                storm.TestOverride = GUI.Toggle(new Rect(6, y, w - 12, RowH - 4), storm.TestOverride, "  Тест: своя гроза и ветер (вместо погоды)");
                y += RowH;
                storm.TestLightning = Slider(ref y, w, "Гроза (тест)", storm.TestLightning, 0f, 1f);
                storm.TestWind = Slider(ref y, w, "Ветер (тест)", storm.TestWind, 0f, 1f);
                float bw = (w - 12f) / 3f;
                if (GUI.Button(new Rect(6, y, bw - 4, RowH - 4), "Молния!")) storm.StrikeNow(true);
                if (GUI.Button(new Rect(6 + bw, y, bw - 4, RowH - 4), "Зарница")) storm.StrikeNow(false);
                if (GUI.Button(new Rect(6 + bw * 2f, y, bw - 4, RowH - 4), "Порыв!")) storm.GustNowStart();
                y += RowH;
                if (GUI.Button(new Rect(6, y, w - 12, RowH - 4), "Молния куда смотрю! (клавиша " + storm.StrikeKey + ")")) storm.StrikeAtView();
                y += RowH;
                storm.LightningAmount = Slider(ref y, w, "Молнии (частота)", storm.LightningAmount, 0f, 2f);
                storm.FlashStrength = Slider(ref y, w, "Вспышка по городу", storm.FlashStrength, 0f, 3f);
                storm.GustAmount = Slider(ref y, w, "Линии ветра в порыв", storm.GustAmount, 0f, 2f);
                storm.TrashOverlay = GUI.Toggle(new Rect(6, y, w - 12, RowH - 4), storm.TrashOverlay, "  Тонна мусора поверх любой погоды");
                y += RowH;
                storm.JunkAmount = Slider(ref y, w, "Мусор: пакеты, газеты, зонтики", storm.JunkAmount, 0f, 2f);
                storm.LeavesAmount = Slider(ref y, w, "Листья (падают на пол)", storm.LeavesAmount, 0f, 2f);
                storm.DustCloudAmount = Slider(ref y, w, "Тучки пыли", storm.DustCloudAmount, 0f, 2f);
                storm.MotesAmount = Slider(ref y, w, "Пылинки в солнце", storm.MotesAmount, 0f, 2f);
            }

            CityWinter winter = CityWinter.Instance;
            if (winter != null)
            {
                Head(ref y, w, "ЗИМА · мороз " + (winter.Frost * 100f).ToString("0") + "% · снег идёт " + (winter.SnowNow * 100f).ToString("0") + "% · лежит " + (winter.SnowCover * 100f).ToString("0") + "% · буран " + (winter.BlizzardNow * 100f).ToString("0") + "%");
                winter.TestOverride = GUI.Toggle(new Rect(6, y, w - 12, RowH - 4), winter.TestOverride, "  Тест: своя зима (вместо погоды)");
                y += RowH;
                winter.TestFrost = Slider(ref y, w, "Мороз (лёд, сосульки, пар)", winter.TestFrost, 0f, 1f);
                winter.TestSnow = Slider(ref y, w, "Снег", winter.TestSnow, 0f, 1f);
                winter.TestBlizzard = Slider(ref y, w, "Буран", winter.TestBlizzard, 0f, 1f);
                winter.BlizzardVisibility = Slider(ref y, w, "Буран: видимость, м", winter.BlizzardVisibility, 3f, 60f);
                winter.SnowTile = Slider(ref y, w, "Снег: размер барханов, м", winter.SnowTile, 1f, 6f);
                float bw3 = (w - 12f) / 3f;
                if (GUI.Button(new Rect(6, y, bw3 - 4, RowH - 4), "Заморозить")) winter.FreezeNow();
                if (GUI.Button(new Rect(6 + bw3, y, bw3 - 4, RowH - 4), "Засыпать снегом")) winter.CoverNow();
                if (GUI.Button(new Rect(6 + bw3 * 2f, y, bw3 - 4, RowH - 4), "Растопить")) winter.MeltNow();
                y += RowH;
            }

            CityHail hail = CityHail.Instance;
            if (hail != null)
            {
                Head(ref y, w, "ГРАД · идёт " + (hail.Amount * 100f).ToString("0") + "% · лежит " + (hail.Cover * 100f).ToString("0") + "%");
                hail.TestOverride = GUI.Toggle(new Rect(6, y, w - 12, RowH - 4), hail.TestOverride, "  Тест: свой град (вместо погоды)");
                y += RowH;
                hail.TestAmount = Slider(ref y, w, "Град: сила", hail.TestAmount, 0f, 1f);
                hail.TestSize = Slider(ref y, w, "Град: размер градин", hail.TestSize, 0.3f, 3f);
                float bw2 = (w - 12f) / 2f;
                if (GUI.Button(new Rect(6, y, bw2 - 4, RowH - 4), "Засыпать градом")) hail.CoverNow();
                if (GUI.Button(new Rect(6 + bw2, y, bw2 - 4, RowH - 4), "Убрать град")) hail.ClearNow();
                y += RowH;
            }

            CityHeat heat = CityHeat.Instance;
            if (heat != null)
            {
                Vector4 hn = heat.Now;
                Head(ref y, w, "ЖАРА · марево " + (hn.x * 100f).ToString("0") + "% · миражи " + (hn.y * 100f).ToString("0") + "% · ореол " + (hn.z * 100f).ToString("0") + "% · зной " + (hn.w * 100f).ToString("0") + "%");
                heat.TestOverride = GUI.Toggle(new Rect(6, y, w - 12, RowH - 4), heat.TestOverride, "  Тест: своя жара (вместо погоды)");
                y += RowH;
                heat.TestHaze = Slider(ref y, w, "Марево (дрожь воздуха)", heat.TestHaze, 0f, 1f);
                heat.TestMirage = Slider(ref y, w, "Миражи на полу вдали", heat.TestMirage, 0f, 1f);
                heat.TestGlare = Slider(ref y, w, "Солнечный ореол", heat.TestGlare, 0f, 1f);
                heat.TestGrade = Slider(ref y, w, "Зной (≥ 0.6 — пекло)", heat.TestGrade, 0f, 1f);
            }

            CityFog fog = CityFog.Instance;
            if (fog != null)
            {
                Vector3 fn = fog.Now;
                Head(ref y, w, "ТУМАН · низкий " + (fn.x * 100f).ToString("0") + "% · вокруг " + (fn.y * 100f).ToString("0") + "% · густой " + (fn.z * 100f).ToString("0") + "%");
                fog.TestOverride = GUI.Toggle(new Rect(6, y, w - 12, RowH - 4), fog.TestOverride, "  Тест: свой туман (вместо погоды)");
                y += RowH;
                fog.TestGround = Slider(ref y, w, "Низкий (по пол-игрока)", fog.TestGround, 0f, 1f);
                fog.TestAround = Slider(ref y, w, "Вокруг этажа", fog.TestAround, 0f, 1f);
                fog.TestDense = Slider(ref y, w, "Густой везде", fog.TestDense, 0f, 1f);
            }

            Head(ref y, w, "ОКНА НОЧЬЮ");
            LitRatio = Slider(ref y, w, "Горящих окон", LitRatio, 0f, 1f);
            OfficeLateRatio = Slider(ref y, w, "Офисы: поздних окон", OfficeLateRatio, 0f, 0.3f);
            OfficeLitShare = Slider(ref y, w, "Небоскрёбы: горящих окон", OfficeLitShare, 0f, 1f);
            WindowFlicker = Slider(ref y, w, "Переключение окон (как часто)", WindowFlicker, 0f, 0.5f);
            SpecialWindows = Slider(ref y, w, "Фитолампы и вечеринки", SpecialWindows, 0f, 3f);
            FromGolden = Slider(ref y, w, "Зажигать на закате", FromGolden, 0f, 1f);
            WeatherLights = Slider(ref y, w, "Тёмная погода зажигает", WeatherLights, 0f, 1f);

            Head(ref y, w, "НЕБО");
            SkySpeed = Slider(ref y, w, "Скорость облаков", SkySpeed, 0f, 2f);
            SkyCloudsAmount = Slider(ref y, w, "Облаков (от погоды)", SkyCloudsAmount, 0f, 1.5f);
            SkyCloudsHigher = Slider(ref y, w, "Выше и мельче", SkyCloudsHigher, 0.3f, 4f);
            SkyCumulus = Slider(ref y, w, "Кучевые комки", SkyCumulus, 0f, 2f);
            SkyCirrus = Slider(ref y, w, "Перистые пряди", SkyCirrus, 0f, 2f);
            SkySheet = Slider(ref y, w, "Слоистая пелена", SkySheet, 0f, 2f);
            SkyScud = Slider(ref y, w, "Рваные тучи", SkyScud, 0f, 2f);

            Head(ref y, w, "ТЕНИ ОБЛАКОВ");
            float z = Slider(ref y, w, "Зона, модулей (0 — везде)", ShadowZoneModules, 0f, 6f);
            if (!Mathf.Approximately(z, ShadowZoneModules)) { ShadowZoneModules = z; _measureAt = -1f; ForceZone(); }
            return y + 8f;
        }

        private void ForceZone()
        {
            CatCooksCloudShadows cs = FindFirstObjectByType<CatCooksCloudShadows>();
            if (cs == null) return;
            if (ShadowZoneModules <= 0f) { cs.zoneRadius = 0f; return; }
            CityBlockGenerator g = GetComponent<CityBlockGenerator>();
            if (g == null) g = FindFirstObjectByType<CityBlockGenerator>();
            if (g != null && cs.zoneCenter == null) cs.zoneRadius = Mathf.Min(g.ModuleSize().x, g.ModuleSize().y) * ShadowZoneModules;
        }

        private static void Head(ref float y, float w, string t)
        {
            y += 4f;
            GUI.Label(new Rect(4, y, w - 8, RowH - 6), "— " + t + " —");
            y += RowH - 4f;
        }

        private static float Slider(ref float y, float w, string label, float v, float min, float max)
        {
            float lw = w * 0.46f;
            GUI.Label(new Rect(6, y + 2, lw, RowH - 4), label);
            float nv = GUI.HorizontalSlider(new Rect(lw + 8, y + 8, w - lw - 60, 14), v, min, max);
            GUI.Label(new Rect(w - 46, y + 2, 44, RowH - 4), nv.ToString(max <= 1.01f ? "0.00" : "0.0"));
            y += RowH;
            return nv;
        }

        /// <summary>
        /// Небо Sky Toon: сдвиг облаков ветром и «кипение» копим по кадрам (ветер и скорость смены формы меняет погода —
        /// при «ветер × время» каждое такое изменение дёргало всё небо).
        /// </summary>
        private void SkyDrift()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Application.isPlaying ? Time.deltaTime : (_lastT < 0f ? 0f : Mathf.Min(now - _lastT, 0.1f));
            _lastT = now;
            Material m = RenderSettings.skybox;
            if (m == null || !m.HasProperty(IdWind)) { Shader.SetGlobalFloat(IdSkyDriven, 0f); return; }
            Vector4 w = m.GetVector(IdWind);
            float evo = m.HasProperty(IdEvolve) ? m.GetFloat(IdEvolve) : 0.05f;
            _skyOff += new Vector2(w.x, w.y) * (dt * 2f * SkySpeed);
            _skyEvo += evo * 6f * dt * SkySpeed;
            if (_skyOff.sqrMagnitude > 1e8f) _skyOff = Vector2.zero;           // точность float
            if (_skyEvo > 1e5f) _skyEvo = 0f;
            Shader.SetGlobalVector(IdSkyOff, _skyOff);
            Shader.SetGlobalFloat(IdSkyEvo, _skyEvo);
            Shader.SetGlobalFloat(IdSkyDriven, 1f);
            Shader.SetGlobalFloat(IdSkySize, SkyCloudsHigher);
            Shader.SetGlobalFloat(IdSkyAmount, SkyCloudsAmount);
            Shader.SetGlobalFloat(IdSkyForm, SkyClusterSpread);
            Shader.SetGlobalVector(IdSkyLayers, new Vector4(SkyCirrus, SkySheet, SkyScud, SkyCumulus));
            // форма облаков по погоде: плавная смесь у пульта (переходы без рывков)
            if (_mood == null && Time.frameCount % 60 == 0) _mood = FindFirstObjectByType<CatCooksMoodDirector>();
            CatCooksWeatherPreset wm = _mood != null ? _mood.CurrentWeatherMix : null;
            if (wm != null)
            {
                Shader.SetGlobalVector(IdSkyW, new Vector4(wm.severity, wm.cloudStretch, wm.cloudWisp, wm.cloudWidth));
                Shader.SetGlobalFloat(IdSkyWOn, 1f);
            }
            else Shader.SetGlobalFloat(IdSkyWOn, 0f);
        }

        private float ComputeNight()
        {
            if (Source == NightSource.Manual) return Mathf.Clamp01(Night);
            if (DayCycle == null && Time.realtimeSinceStartup >= _findAt)
            {
                _findAt = Time.realtimeSinceStartup + 2f;
                DayCycle = FindFirstObjectByType<CatCooksDayCycle>();
            }
            if (DayCycle == null) return Mathf.Clamp01(Night);
            float n = Mathf.Clamp01(DayCycle.Night01 + DayCycle.Golden01 * FromGolden);
            n = Mathf.Pow(n, Curve);
            // тёмная погода зажигает окна и днём: затмение, шторм, гроза (по приглушению солнца в смеси погоды)
            if (WeatherLights > 0f && _mood != null && _mood.CurrentWeatherMix != null)
                n = Mathf.Max(n, Mathf.Clamp01((0.55f - _mood.CurrentWeatherMix.sunDim) / 0.4f) * WeatherLights);
            return n;
        }

        private void Push()
        {
            float n = ComputeNight();
            CurrentNight = n;
            Shader.SetGlobalFloat(IdNight, n);
            Shader.SetGlobalFloat(IdLit, LitRatio);
            Shader.SetGlobalFloat(IdFlick, WindowFlicker);
            if (Time.realtimeSinceStartup >= _measureAt) Measure();
            Shader.SetGlobalFloat(IdGround, _groundY);
            Shader.SetGlobalFloat(IdMeter, _meter);
            float mu = _meter > 0f ? _meter : 3.5f;
            Shader.SetGlobalVector(IdApt, new Vector4(ApartmentSize.x * mu, ApartmentSize.y * mu, OfficeLateRatio, OfficeLitShare));
            Shader.SetGlobalVector(IdSpecial, new Vector4(_specialCenter.x, _specialCenter.z, _specialRadius, SpecialWindows));
            SkyDrift();
        }
    }
}
