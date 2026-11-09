using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ДОЖДЬ ГОРОДА — один на сцену. Рисует (без частиц и без мешей, всё считает видеокарта):
    ///   • струи — капли вокруг камеры, снос ветром, гаснут под навесами зоны;
    ///   • завесу — дальние полосы и «простыни» дождя кольцом вокруг города;
    ///   • всплески — кольца и короны с капельками везде, куда падает дождь (пол этажа, крыши, улицы).
    /// Вид дождя — профиль: морось, уютный дождик, грибной (на солнце, капли блестят), дождь, ливень, шторм.
    /// «Авто»: вид выбирается по силе дождя из погоды (плавно), грибной — если дождь при солнце.
    /// Сила — из погоды (CatCooksWeatherState.Rain) или тестовая (меню P → «Город» → Дождь).
    /// Старый дождь Atmosphere (Rain, Rain FX, Rain Splashes) при старте выключается — чтобы не было двух дождей.
    /// Ставится меню «CatCooks/Застройка/Дождь: поставить в сцену» (материалы, зона).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CityRain : MonoBehaviour
    {
        [System.Serializable]
        public class Profile
        {
            public string Name = "Дождь";
            [Tooltip("Доля капель при полной силе (0..1 от Max Drops)")] [Range(0f, 1f)] public float Drops = 0.7f;
            [Tooltip("Скорость падения, м/с (реальный дождь 7–9; для читаемости в игре 10–20)")] public float Speed = 14f;
            [Tooltip("Длина капли, м")] public float Length = 0.6f;
            [Tooltip("Толщина капли, м")] public float Width = 0.016f;
            [Range(0f, 1f)] public float Alpha = 0.38f;
            [Tooltip("Доля всплесков (0..1)")] [Range(0f, 1f)] public float Splash = 0.6f;
            [Tooltip("Радиус кольца всплеска, м")] public float Ring = 0.16f;
            [Tooltip("Высота короны (0 — только кольца)")] [Range(0f, 1.5f)] public float Crown = 0.8f;
            [Tooltip("Капельки из короны")] [Range(0f, 1f)] public float Droplets = 0.6f;
            [Tooltip("Жизнь всплеска, с")] public float SplashLife = 0.36f;
            [Tooltip("Завеса вдали")] [Range(0f, 1f)] public float Curtain = 0.35f;
            [Tooltip("Снос ветром ×")] public float Wind = 1f;
            [Tooltip("Порывистость")] [Range(0f, 1f)] public float Gusts = 0.2f;
            [Tooltip("Блеск на солнце (грибной дождь)")] [Range(0f, 1f)] public float Glint = 0f;
            public Color Tint = new Color(0.85f, 0.9f, 1f, 1f);
            [Range(0.2f, 2f)] public float Brightness = 1f;

            public Profile Clone() { return (Profile)MemberwiseClone(); }
        }

        public enum Variant { Auto = -1, Drizzle = 0, Cozy = 1, SunShower = 2, Rain = 3, Downpour = 4, Storm = 5 }

        [System.Serializable]
        public class WeatherRain
        {
            [Tooltip("Кусок имени/названия погоды (CW_29_Downpour → downpour)")] public string Weather;
            public Variant Rain = Variant.Rain;
            public WeatherRain() { }
            public WeatherRain(string w, Variant v) { Weather = w; Rain = v; }
        }

        [Header("Сила и вид")]
        public Variant Mode = Variant.Auto;
        [Tooltip("Тест: своя сила дождя вместо погоды")]
        public bool TestOverride = false;
        [Range(0f, 1f)] public float TestIntensity = 0.6f;
        [Tooltip("Секунд на нарастание/затихание")]
        public float RampSeconds = 4f;
        [Tooltip("Секунд на смену вида дождя (скорость, длина, цвет перетекают)")]
        public float VariantBlendSeconds = 3f;
        [Tooltip("Погода → вид дождя. Не нашлась — вид по силе дождя (морось → … → шторм)")]
        public List<WeatherRain> WeatherVariants = DefaultWeatherVariants();

        [Header("Сколько (множители)")]
        [Range(0f, 2f)] public float StreakAmount = 1f;
        [Range(0f, 2f)] public float SplashAmount = 1f;
        [Range(0f, 2f)] public float CurtainAmount = 1f;

        [Header("Мокрость и лужи")]
        [Tooltip("Секунд под полным дождём до полной мокрости")]
        public float SoakSeconds = 30f;
        [Tooltip("Секунд на высыхание (солнце и ветер быстрее, пасмурно и ночью медленнее)")]
        public float DrySeconds = 140f;
        [Tooltip("Сила дождя → до какой мокрости доходит. 0.3 — тёмные капли, 0.5 — весь пол тёмный, 0.75 — лужицы, 1 — всё залито")]
        public AnimationCurve WetCap = DefaultWetCap();
        [Tooltip("Мокнет только то, на чём стоит City Wet Surface (модули этажа). Выкл — мокнет всё, что смотрит вверх")]
        public bool WetOnlyMarked = true;
        [Range(0f, 2f)] public float PuddleAmount = 1f;
        [Tooltip("Размер пятен луж, м")]
        public float PuddleSize = 2.4f;
        [Range(0f, 2f)] public float RippleStrength = 1f;
        [Range(0f, 1.5f)] public float Darken = 1f;
        [Range(0f, 2f)] public float Gloss = 1f;
        [Range(0f, 1.5f)] public float Reflections = 1f;
        [Tooltip("Дальность мокрости и луж, м")]
        public float WetDistance = 140f, PuddleDistance = 70f;
        [Tooltip("Мокрые дома города (_CatWetness) — от этой же мокрости")]
        public bool DriveCityWetness = true;

        [Header("Капель с краёв (зонты — сами, балки/навесы — City Drip Edge)")]
        [Range(0f, 2f)] public float DripAmount = 1f;
        [Tooltip("Радиус капли, м")]
        public float DripSize = 0.035f;
        [Tooltip("Точек капели максимум")]
        [Range(100, 4000)] public int MaxDripPoints = 1500;
        [Tooltip("Шаг капели по краю зонта, м")]
        public float UmbrellaDripSpacing = 0.35f;
        [Tooltip("Дальше этого капель не рисуется, м")]
        public float DripDistance = 45f;
        [Tooltip("Слои, о которые капля разбивается (луч вниз до пола)")]
        public LayerMask DripFloorMask = Physics.DefaultRaycastLayers;

        [Header("После дождя")]
        [Tooltip("Пар от мокрого пола, когда дождь кончился и светит солнце")]
        [Range(0f, 2f)] public float SteamAmount = 1f;
        [Range(8, 40)] public int SteamGrid = 22;
        [Tooltip("Клетка пара, м")]
        public float SteamCell = 1.6f;
        [Tooltip("Жизнь клубка пара, с")]
        public float SteamLife = 3.2f;

        [Header("Бюджет")]
        [Tooltip("Капель максимум (видно — доля по силе и виду)")]
        [Range(1000, 30000)] public int MaxDrops = 12000;
        [Tooltip("Коробка капель вокруг камеры, м: ширина, высота")]
        public Vector2 DropBox = new Vector2(34f, 26f);
        [Tooltip("Сетка всплесков: клеток по стороне (радиус = клетки × размер / 2)")]
        [Range(10, 80)] public int SplashGrid = 38;
        [Tooltip("Размер клетки всплесков, м")]
        public float SplashCell = 1.1f;
        [Range(1, 4)] public int SplashPerCell = 2;
        [Tooltip("Радиус завесы, м")]
        public float CurtainRadius = 90f;

        [Header("Зоны дождя")]
        [Tooltip("Пусто — все зоны сцены (обычно так и оставь). Зону ставь в красный модуль: объект + Size")]
        public List<CityRainZone> Zones = new List<CityRainZone>();

        [Header("Материалы (ставит меню)")]
        public Material StreakMaterial;
        public Material SplashMaterial;
        public Material CurtainMaterial;
        public Material DripMaterial;
        public Material SteamMaterial;

        [Header("Виды дождя")]
        public List<Profile> Profiles = Defaults();
        public bool DisableLegacyRain = true;
        private const int ProfilesVersion = 2;
        [SerializeField, HideInInspector] private int _profilesVersion = 0;

        [ContextMenu("Виды дождя: сбросить на стандартные")]
        public void ResetProfiles() { Profiles = Defaults(); _profilesVersion = ProfilesVersion; }
        public bool Log = true;

        public static CityRain Instance { get; private set; }
        public float Intensity { get; private set; }
        public string CurrentName { get; private set; }

        private float _shown;
        private Profile _mix = new Profile();
        private Profile _base = new Profile();
        private Profile _target = new Profile();
        private bool _mixInit;
        private float _meter = 3.5f;
        private float _measureAt = -1f;
        private bool _legacyDone;
        private CityRainZone _focus;
        private readonly CityRainDrips _drips = new CityRainDrips();
        private float _zoneMax, _drySm, _dripNow, _steamNow;
        static readonly int IdWetDry = Shader.PropertyToID("_CC_WetDry");
        static readonly int IdDrip = Shader.PropertyToID("_CC_Drip");
        static readonly int IdSteam = Shader.PropertyToID("_CC_Steam");

        /// <summary>Единиц движка в метре (масштаб города).</summary>
        public float Meter { get { return _meter; } }
        /// <summary>Сохнет ли сейчас (0..1) — дождь кончился, пол мокрый.</summary>
        public float Drying { get { return _drySm; } }

        static readonly int IdP0 = Shader.PropertyToID("_CC_RainP0");
        static readonly int IdP1 = Shader.PropertyToID("_CC_RainP1");
        static readonly int IdWind = Shader.PropertyToID("_CC_RainWind");
        static readonly int IdTint = Shader.PropertyToID("_CC_RainTint");
        static readonly int IdSplash = Shader.PropertyToID("_CC_RainSplash");
        static readonly int IdSplash2 = Shader.PropertyToID("_CC_RainSplash2");
        static readonly int IdCurtain = Shader.PropertyToID("_CC_RainCurtain");
        static readonly int IdOcc = Shader.PropertyToID("_CC_RainOcc");
        static readonly int IdOccRect = Shader.PropertyToID("_CC_RainOccRect");
        static readonly int IdOccOn = Shader.PropertyToID("_CC_RainOccOn");
        static readonly int IdMeter = Shader.PropertyToID("_CC_RainMeter");
        static readonly int IdRain = Shader.PropertyToID("_CC_Rain");
        static readonly int IdFall = Shader.PropertyToID("_CC_RainFall");
        static readonly int IdWet = Shader.PropertyToID("_CC_Wet");
        static readonly int IdWetOn = Shader.PropertyToID("_CC_WetOn");
        static readonly int IdWetMap = Shader.PropertyToID("_CC_WetMap");
        static readonly int IdWetMapOn = Shader.PropertyToID("_CC_WetMapOn");
        static readonly int IdWetRect = Shader.PropertyToID("_CC_WetRect");
        static readonly int IdWetP = Shader.PropertyToID("_CC_WetP");
        static readonly int IdWetP2 = Shader.PropertyToID("_CC_WetP2");
        static readonly int IdSkyZ = Shader.PropertyToID("_CC_SkyZenith");
        static readonly int IdSkyH = Shader.PropertyToID("_CC_SkyHorizon");
        static readonly int IdBlock = Shader.PropertyToID("_CC_RainBlock");
        static readonly int IdBlockN = Shader.PropertyToID("_CC_RainBlockN");
        static readonly int IdCatWet = Shader.PropertyToID("_CatWetness");
        static readonly int IdWetMaskOn = Shader.PropertyToID("_CC_WetMaskOn");
        private bool _warnedNoMarked;

        public static AnimationCurve DefaultWetCap()
        {
            // морось 0.18 → тёмные капли, уютный 0.3 → пол темнеет, грибной 0.35 → почти весь тёмный,
            // дождь 0.55 → первые лужицы, ливень 0.85+ → всё залито
            return new AnimationCurve(
                new Keyframe(0f, 0.2f), new Keyframe(0.18f, 0.3f), new Keyframe(0.3f, 0.45f), new Keyframe(0.4f, 0.55f),
                new Keyframe(0.55f, 0.7f), new Keyframe(0.7f, 0.85f), new Keyframe(0.85f, 1f), new Keyframe(1f, 1f));
        }

        /// <summary>Предел мокрости для текущей силы дождя (0, если дождя нет).</summary>
        public float WetCapNow
        {
            get { return Intensity > 0.01f ? Mathf.Clamp01(WetCap != null && WetCap.length > 0 ? WetCap.Evaluate(Intensity) : 1f) : 0f; }
        }

        /// <summary>Этап мокрости словами (меню P).</summary>
        public static string WetStage(float w)
        {
            if (w < 0.02f) return "сухо";
            if (w < 0.33f) return "тёмные капли";
            if (w < 0.5f) return "мокрый пол";
            if (w < 0.72f) return "лужицы";
            if (w < 0.95f) return "лужи";
            return "всё залито";
        }
        private float _wet;                                  // общая мокрость (вне зоны, дома города)
        private readonly Vector4[] _blocks = new Vector4[8];
        public float Wetness { get { return _wet; } }
        private Vector3 _fall;          // накопленное падение и снос (ед.) — капли не прыгают при смене скорости/ветра
        private Vector2 _windSm;        // сглаженный ветер (ед/с)

        public static List<Profile> Defaults()
        {
            return new List<Profile>
            {
                new Profile { Name = "Морось", Drops = 0.6f, Speed = 7f, Length = 0.22f, Width = 0.007f, Alpha = 0.26f, Splash = 0.22f, Ring = 0.08f, Crown = 0f, Droplets = 0f, SplashLife = 0.45f, Curtain = 0.12f, Wind = 1.6f, Gusts = 0.3f, Tint = new Color(0.86f, 0.9f, 0.97f) },
                new Profile { Name = "Уютный дождик", Drops = 0.42f, Speed = 11f, Length = 0.42f, Width = 0.012f, Alpha = 0.34f, Splash = 0.38f, Ring = 0.12f, Crown = 0.45f, Droplets = 0.3f, SplashLife = 0.4f, Curtain = 0.22f, Wind = 0.7f, Gusts = 0.1f, Tint = new Color(0.9f, 0.9f, 0.95f) },
                new Profile { Name = "Грибной дождь", Drops = 0.32f, Speed = 10f, Length = 0.45f, Width = 0.014f, Alpha = 0.5f, Splash = 0.32f, Ring = 0.13f, Crown = 0.6f, Droplets = 0.5f, SplashLife = 0.4f, Curtain = 0.05f, Wind = 0.6f, Gusts = 0.1f, Glint = 1f, Tint = new Color(1f, 0.94f, 0.78f), Brightness = 1.25f },
                new Profile { Name = "Дождь", Drops = 0.7f, Speed = 14f, Length = 0.62f, Width = 0.016f, Alpha = 0.38f, Splash = 0.62f, Ring = 0.16f, Crown = 0.8f, Droplets = 0.6f, SplashLife = 0.36f, Curtain = 0.38f, Wind = 1f, Gusts = 0.2f },
                new Profile { Name = "Ливень", Drops = 1f, Speed = 17f, Length = 0.8f, Width = 0.02f, Alpha = 0.42f, Splash = 0.92f, Ring = 0.2f, Crown = 1f, Droplets = 1f, SplashLife = 0.32f, Curtain = 0.7f, Wind = 1.2f, Gusts = 0.35f, Tint = new Color(0.82f, 0.86f, 0.92f) },
                new Profile { Name = "Шторм", Drops = 1f, Speed = 20f, Length = 1f, Width = 0.022f, Alpha = 0.46f, Splash = 1f, Ring = 0.22f, Crown = 1.1f, Droplets = 1f, SplashLife = 0.28f, Curtain = 1f, Wind = 2.4f, Gusts = 0.8f, Tint = new Color(0.78f, 0.82f, 0.9f), Brightness = 0.9f },
            };
        }

        private void OnEnable() { Instance = this; _legacyDone = false; _mixInit = false; MergeWeatherVariants(); }
        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalFloat(IdRain, 0f);
            Shader.SetGlobalVector(IdP0, Vector4.zero);
            Shader.SetGlobalVector(IdSplash, Vector4.zero);
            Shader.SetGlobalVector(IdCurtain, Vector4.zero);
            Shader.SetGlobalVector(IdDrip, Vector4.zero);
            Shader.SetGlobalVector(IdSteam, Vector4.zero);
            Shader.SetGlobalFloat(IdWetDry, 0f);
            _drips.Release();
        }

        private void Update()
        {
            if (Application.isPlaying && DisableLegacyRain && !_legacyDone) DisableLegacy();
            if (Time.realtimeSinceStartup >= _measureAt) Measure();

            float target = TestOverride ? TestIntensity : Mathf.Clamp01(CatCooksWeatherState.Rain);
            float dt = Application.isPlaying ? Time.deltaTime : 0.05f;
            _shown = Mathf.MoveTowards(_shown, target, dt / Mathf.Max(0.1f, RampSeconds));
            if (!Application.isPlaying && !TestOverride) _shown = target;
            Intensity = _shown;

            CityWeatherArea.Refresh();
            BuildMix();
            Camera cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : transform.position;
            UpdateZones(cp);
            UpdateWetness(dt, cp);
            UpdateAfterRain(dt);
            Push();
            if (Intensity > 0.002f) Draw(cp);
            DrawAfterRain(cp);
        }

        private void LateUpdate()
        {
            // после Weather FX Atmosphere: дома города мокнут от нашей мокрости (у той — луч «под крышей» на весь мир)
            if (DriveCityWetness) Shader.SetGlobalFloat(IdCatWet, _wet);
        }

        // ─────────────── мокрость, лужи, зонты ───────────────
        private void UpdateWetness(float dt, Vector3 cam)
        {
            if (!Application.isPlaying) dt = 0f;
            float soak = Intensity > 0.01f ? Intensity / Mathf.Max(1f, SoakSeconds) : 0f;
            float sun = Mathf.Clamp01(CatCooksWeatherState.Day01) * (1f - Mathf.Clamp01(CatCooksWeatherState.Overcast));
            float dry = (1f / Mathf.Max(5f, DrySeconds)) * Mathf.Lerp(0.4f, 1.8f, sun) * (1f + CatCooksWeatherState.Wind * 0.5f);
            float cap = WetCapNow;
            if (soak > 0f && _wet < cap) _wet = Mathf.Min(cap, _wet + soak * dt);
            else _wet = Mathf.Max(cap, _wet - dry * dt);      // дождь ослаб — лужи уходят до нового предела

            // зонты: ближайшие 8 к камере
            int nb = 0;
            List<CityRainBlocker> bl = CityRainBlocker.All;
            bl.Sort((a, b) => (a.WorldCenter - cam).sqrMagnitude.CompareTo((b.WorldCenter - cam).sqrMagnitude));
            for (int i = 0; i < bl.Count && nb < 8; i++)
            {
                CityRainBlocker b = bl[i];
                if (b == null || !b.Active || !b.isActiveAndEnabled) continue;
                _blocks[nb++] = b.Packed;
            }
            for (int i = nb; i < 8; i++) _blocks[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(IdBlock, _blocks);
            Shader.SetGlobalFloat(IdBlockN, nb);

            float zoneMax = 0f;
            if (_focus != null)
            {
                if (dt > 0f) _focus.TickWet(dt, soak, dry, cap, bl);
                zoneMax = _focus.WetMax;
                if (_focus.WetMap != null)
                {
                    Shader.SetGlobalTexture(IdWetMap, _focus.WetMap);
                    Shader.SetGlobalVector(IdWetRect, _focus.OccRect);
                    Shader.SetGlobalFloat(IdWetMapOn, 1f);
                }
                else Shader.SetGlobalFloat(IdWetMapOn, 0f);
            }
            else Shader.SetGlobalFloat(IdWetMapOn, 0f);

            _zoneMax = zoneMax;
            float m = _meter;
            Shader.SetGlobalFloat(IdWet, _wet);
            Shader.SetGlobalFloat(IdWetMaskOn, WetOnlyMarked ? 1f : 0f);
            if (WetOnlyMarked && CityWetSurface.All.Count == 0 && (_wet > 0.05f || zoneMax > 0.05f) && !_warnedNoMarked && Application.isPlaying)
            {
                _warnedNoMarked = true;
                Debug.LogWarning("[Дождь] мокнуть нечему: ни на одном объекте нет City Wet Surface. Выдели модули этажа → CatCooks → Застройка → «Мокнет: отметить выделенное» (или сними галку Wet Only Marked на City Rain)", this);
            }
            Shader.SetGlobalFloat(IdWetOn, (_wet > 0.002f || zoneMax > 0.002f) ? 1f : 0f);
            Shader.SetGlobalVector(IdWetP, new Vector4(PuddleAmount, PuddleSize * m, RippleStrength, WetDistance * m));
            Shader.SetGlobalVector(IdWetP2, new Vector4(Darken, Gloss, Reflections, PuddleDistance * m));
            Shader.SetGlobalColor(IdSkyZ, CatCooksWeatherState.SkyZenith);
            Shader.SetGlobalColor(IdSkyH, CatCooksWeatherState.SkyHorizon);
        }

        // ─────────────── капель и «после дождя» ───────────────
        private void UpdateAfterRain(float dt)
        {
            float m = _meter;
            bool raining = Intensity > 0.01f;
            float wetAny = Mathf.Max(_wet, _zoneMax);
            _drySm = Mathf.MoveTowards(_drySm, (!raining && wetAny > 0.02f) ? 1f : 0f, dt / 4f);
            Shader.SetGlobalFloat(IdWetDry, _drySm);

            // капает в дождь; после — реже и затихает, пока края сохнут
            float drip = raining ? Mathf.Lerp(0.35f, 1f, Intensity) : Mathf.Clamp01((wetAny - 0.08f) / 0.5f) * 0.45f;
            _dripNow = drip * DripAmount * CityWeatherArea.CamFull;                      // капель с краёв — только в красной зоне
            Shader.SetGlobalVector(IdDrip, new Vector4(_dripNow, DripSize * m, DripDistance * m, 9.81f * m));
            if (_dripNow > 0.001f) _drips.Tick(dt, m, MaxDripPoints, UmbrellaDripSpacing, DripFloorMask);

            // пар: дождь кончился + солнце + ещё мокро
            float sun = Mathf.Clamp01(CatCooksWeatherState.Day01) * (1f - Mathf.Clamp01(CatCooksWeatherState.Overcast));
            _steamNow = _drySm * Mathf.Clamp01((sun - 0.25f) / 0.5f) * Mathf.Clamp01(wetAny * 2f) * SteamAmount * CityWeatherArea.CamFull;
            Shader.SetGlobalVector(IdSteam, new Vector4(_steamNow, SteamCell * m, SteamGrid, Mathf.Max(0.5f, SteamLife)));
        }

        private void DrawAfterRain(Vector3 cam)
        {
            Bounds big = new Bounds(cam, Vector3.one * 4000f * _meter / 3.5f);
            if (_dripNow > 0.001f) _drips.Draw(DripMaterial, big);
            if (_steamNow > 0.001f && SteamMaterial != null)
            {
                RenderParams rp = new RenderParams(SteamMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, SteamGrid * SteamGrid);
            }
        }

        /// <summary>Сколько точек капели сейчас (меню P).</summary>
        public int DripPoints { get { return _drips.Count; } }

        /// <summary>Намочить / высушить всё сразу (меню P).</summary>
        public void SetWetness(float w)
        {
            _wet = Mathf.Clamp01(w);
            foreach (CityRainZone z in CityRainZone.All) if (z != null) z.SetAllWet(_wet);
        }

        // ─────────────── вид дождя ───────────────
        private void BuildMix()
        {
            if (Profiles == null || Profiles.Count < 6 || _profilesVersion < ProfilesVersion)
            {
                Profiles = Defaults();
                _profilesVersion = ProfilesVersion;
                if (Log) Debug.Log("[Дождь] виды дождя обновлены до новых настроек (скорости, длины)", this);
            }
            float k = Intensity;
            float dt = Application.isPlaying ? Time.deltaTime : 1f;
            Profile target = _target;
            float scale;                                   // доля «полноты» вида (нарастание и затихание дождя)
            Variant v = Mode;
            float weatherRain = Mathf.Clamp01(CatCooksWeatherState.Rain);
            if (v == Variant.Auto && !TestOverride) v = VariantForWeather(CatCooksWeatherState.Current);
            if (v != Variant.Auto)
            {
                // вид задан погодой (таблица «Погода → вид дождя») или вручную в меню
                Copy(Profiles[(int)v], target);
                CurrentName = Profiles[(int)v].Name;
                float full = TestOverride || Mode != Variant.Auto ? 1f : Mathf.Max(weatherRain, 0.05f);
                scale = Mode != Variant.Auto || TestOverride ? k : Mathf.Clamp01(k / full);
            }
            else
            {
                // лестница по силе: морось 0.12 → уютный 0.3 → дождь 0.55 → ливень 0.8 → шторм 1
                int[] order = { 0, 1, 3, 4, 5 };
                float[] at = { 0.12f, 0.3f, 0.55f, 0.8f, 1f };
                int a = 0;
                while (a < at.Length - 2 && k > at[a + 1]) a++;
                float t = Mathf.Clamp01(Mathf.InverseLerp(at[a], at[a + 1], k));
                Lerp(Profiles[order[a]], Profiles[order[a + 1]], t, target);
                CurrentName = t < 0.5f ? Profiles[order[a]].Name : Profiles[order[a + 1]].Name;
                scale = Mathf.Clamp01(k / at[0]);
                float sunny = SunShowerFactor();
                if (sunny > 0.01f)
                {
                    Profile g = Profiles[(int)Variant.SunShower];
                    float drops = target.Drops;
                    Lerp(target, g, sunny, target);
                    target.Drops = Mathf.Lerp(drops, Mathf.Min(drops, g.Drops), sunny);   // на солнце капли реже, но крупные и блестят
                    if (sunny > 0.5f) CurrentName = g.Name;
                }
            }
            // вид дождя меняется плавно (скорость, длина, цвет — без рывков при смене погоды)
            if (!_mixInit) { Copy(target, _base); _mixInit = true; }
            Lerp(_base, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, VariantBlendSeconds)), _base);
            Copy(_base, _mix);
            _mix.Drops *= scale;
            _mix.Splash *= Mathf.Lerp(0.3f, 1f, scale) * Mathf.Clamp01(scale * 4f);
            _mix.Curtain *= scale;
        }

        /// <summary>Вид дождя для погоды по таблице (Auto — не задан, тогда по силе дождя).</summary>
        public Variant VariantForWeather(CatCooksWeatherPreset w)
        {
            if (w == null || WeatherVariants == null) return Variant.Auto;
            string key = (w.name + " " + w.title).ToLowerInvariant().Replace("_", "");
            Variant best = Variant.Auto;
            int bestLen = 0;
            for (int i = 0; i < WeatherVariants.Count; i++)                 // побеждает самый длинный ключ
            {
                WeatherRain r = WeatherVariants[i];
                if (r == null || string.IsNullOrEmpty(r.Weather)) continue;
                string k = r.Weather.ToLowerInvariant().Replace("_", "");
                if (k.Length > bestLen && key.Contains(k)) { best = r.Rain; bestLen = k.Length; }
            }
            return best;
        }

        public static List<WeatherRain> DefaultWeatherVariants()
        {
            return new List<WeatherRain>
            {
                new WeatherRain("cozyrain", Variant.Cozy),
                new WeatherRain("sunshower", Variant.SunShower),
                new WeatherRain("drizzle", Variant.Drizzle),
                new WeatherRain("gloomy", Variant.Drizzle),
                new WeatherRain("prestorm", Variant.Drizzle),
                new WeatherRain("steadyrain", Variant.Rain),
                new WeatherRain("hailthunder", Variant.Downpour),
                new WeatherRain("hail", Variant.Rain),
                new WeatherRain("downpour", Variant.Downpour),
                new WeatherRain("thunderstorm", Variant.Downpour),
                new WeatherRain("tempest", Variant.Storm),
                new WeatherRain("hurricane", Variant.Storm),
            };
        }

        private void MergeWeatherVariants()
        {
            if (WeatherVariants == null) WeatherVariants = new List<WeatherRain>();
            List<WeatherRain> d = DefaultWeatherVariants();
            for (int i = 0; i < d.Count; i++)
            {
                bool has = false;
                foreach (WeatherRain r in WeatherVariants) if (r != null && r.Weather == d[i].Weather) { has = true; break; }
                if (!has) WeatherVariants.Insert(Mathf.Min(i, WeatherVariants.Count), d[i]);
            }
        }

        private float SunShowerFactor()
        {
            CatCooksWeatherPreset w = CatCooksWeatherState.Current;
            if (w != null)
            {
                string n = (w.name + " " + w.title).ToLowerInvariant();
                if (n.Contains("грибн") || n.Contains("sunshower") || n.Contains("mushroom")) return 1f;
                if (n.Contains("уют") || n.Contains("cozy")) return 0f;
            }
            float clear = 1f - Mathf.Clamp01(Mathf.Max(CatCooksWeatherState.Overcast * 1.6f, CatCooksWeatherState.Darkness * 2f));
            float day = Mathf.Clamp01(CatCooksWeatherState.Day01);
            return Mathf.Clamp01((clear - 0.55f) / 0.3f) * day;
        }

        private static void Copy(Profile a, Profile d) { Lerp(a, a, 0f, d); d.Name = a.Name; }

        private static void Lerp(Profile a, Profile b, float t, Profile d)
        {
            d.Drops = Mathf.Lerp(a.Drops, b.Drops, t); d.Speed = Mathf.Lerp(a.Speed, b.Speed, t);
            d.Length = Mathf.Lerp(a.Length, b.Length, t); d.Width = Mathf.Lerp(a.Width, b.Width, t);
            d.Alpha = Mathf.Lerp(a.Alpha, b.Alpha, t); d.Splash = Mathf.Lerp(a.Splash, b.Splash, t);
            d.Ring = Mathf.Lerp(a.Ring, b.Ring, t); d.Crown = Mathf.Lerp(a.Crown, b.Crown, t);
            d.Droplets = Mathf.Lerp(a.Droplets, b.Droplets, t); d.SplashLife = Mathf.Lerp(a.SplashLife, b.SplashLife, t);
            d.Curtain = Mathf.Lerp(a.Curtain, b.Curtain, t); d.Wind = Mathf.Lerp(a.Wind, b.Wind, t);
            d.Gusts = Mathf.Lerp(a.Gusts, b.Gusts, t); d.Glint = Mathf.Lerp(a.Glint, b.Glint, t);
            d.Tint = Color.Lerp(a.Tint, b.Tint, t); d.Brightness = Mathf.Lerp(a.Brightness, b.Brightness, t);
        }

        // ─────────────── зоны и карта укрытий ───────────────
        private void UpdateZones(Vector3 cam)
        {
            CityRainZone best = null;
            List<CityRainZone> list = CityRainZone.All;
            if (Zones != null && Zones.Count > 0) { Zones.RemoveAll(x => x == null); if (Zones.Count > 0) list = Zones; }
            foreach (CityRainZone z in list)
            {
                if (z == null) continue;
                z.UpdateActive(cam);
                if (!z.IsActive || z.Kind == CityRainZone.ZoneKind.Light) continue;   // жёлтая зона — без карты укрытий и мокрости (лёгкая)
                // фокус — зона, где камера; иначе первая всегда-активная (зона 1)
                if (z.Contains(cam, z.EnterMargin)) { best = z; break; }
                if (best == null) best = z;
            }
            _focus = best;
            if (_focus != null && (Application.isPlaying || Intensity > 0.002f)) _focus.TickOcclusion();
            if (_focus != null && _focus.HasOcclusion)
            {
                Shader.SetGlobalTexture(IdOcc, _focus.Occlusion);
                Shader.SetGlobalVector(IdOccRect, _focus.OccRect);
                Shader.SetGlobalFloat(IdOccOn, 1f);
            }
            else Shader.SetGlobalFloat(IdOccOn, 0f);
        }

        private void Measure()
        {
            _measureAt = Time.realtimeSinceStartup + (Application.isPlaying ? 1e6f : 5f);
            CityProps p = FindFirstObjectByType<CityProps>();
            _meter = p != null ? p.Meter() : 3.5f;
            if (_meter <= 0f) _meter = 3.5f;
        }

        // ─────────────── в шейдеры ───────────────
        private void Push()
        {
            float m = _meter;
            Profile p = _mix;
            Vector2 wd = CatCooksWeatherState.WindDir.sqrMagnitude > 1e-4f ? CatCooksWeatherState.WindDir.normalized : new Vector2(0.8f, 0.6f);
            float windMs = (0.6f + CatCooksWeatherState.Wind * 7f) * p.Wind;
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            float tt = Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartup);
            float gust = 1f + p.Gusts * (Mathf.PerlinNoise(tt * 0.35f, 0.37f) - 0.5f) * 1.8f;
            gust *= 1f + CityStorm.GustNow * (CityStorm.Instance != null ? CityStorm.Instance.RainGustBoost : 1.3f);                                           // порыв шторма — дождь косит сильнее
            Vector2 wTarget = wd * windMs * m * gust;
            _windSm = Vector2.Lerp(_windSm, wTarget, 1f - Mathf.Exp(-dt * 2f));
            float speedU = p.Speed * m;
            _fall += new Vector3(_windSm.x, -speedU, _windSm.y) * dt;
            if (Mathf.Abs(_fall.y) > 2e5f || Mathf.Abs(_fall.x) > 2e5f || Mathf.Abs(_fall.z) > 2e5f) _fall = Vector3.zero;
            Shader.SetGlobalVector(IdFall, new Vector4(_fall.x, _fall.y, _fall.z, 0f));
            Shader.SetGlobalFloat(IdMeter, m);
            Shader.SetGlobalFloat(IdRain, Intensity);
            float qk = CityWeatherArea.CamFull > 0.5f ? 1f : 0.35f;                  // в жёлтой зоне капель-экземпляров втрое меньше
            float thin = Mathf.Min(1f, CityWeatherArea.CamK / qk);
            Shader.SetGlobalVector(IdP0, new Vector4(Mathf.Clamp01(p.Drops * StreakAmount) * thin, p.Speed * m, p.Length * m, p.Width * m));
            Shader.SetGlobalVector(IdP1, new Vector4(p.Alpha, p.Glint, DropBox.x * m, DropBox.y * m));
            Shader.SetGlobalVector(IdWind, new Vector4(_windSm.x, 0f, _windSm.y, p.Gusts));
            Shader.SetGlobalVector(IdTint, new Vector4(p.Tint.r, p.Tint.g, p.Tint.b, p.Brightness));
            Shader.SetGlobalVector(IdSplash, new Vector4(Mathf.Clamp01(p.Splash * SplashAmount) * CityWeatherArea.CamFull, p.SplashLife, p.Ring * m, p.Crown));
            Shader.SetGlobalVector(IdSplash2, new Vector4(p.Droplets, SplashCell * m, SplashGrid, SplashPerCell));
            Shader.SetGlobalVector(IdCurtain, new Vector4(Mathf.Clamp01(p.Curtain * CurtainAmount * Mathf.Min(1f, Intensity * 1.5f)) * CityWeatherArea.CamFull, CurtainRadius * m, 80f * m, p.Length * m));
        }

        private void Draw(Vector3 cam)
        {
            float camK = CityWeatherArea.CamK, camFull = CityWeatherArea.CamFull;
            if (camK < 0.01f) return;                                             // вне зон погоды капель нет вообще
            Bounds big = new Bounds(cam, Vector3.one * 4000f * _meter / 3.5f);
            if (StreakMaterial != null)
            {
                RenderParams rp = new RenderParams(StreakMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                int n = Mathf.Max(1, camFull > 0.5f ? MaxDrops : Mathf.RoundToInt(MaxDrops * 0.35f));
                Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, n);
            }
            if (SplashMaterial != null && camFull > 0.01f)
            {
                RenderParams rp = new RenderParams(SplashMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 12, SplashGrid * SplashGrid * SplashPerCell);
            }
            if (CurtainMaterial != null && camFull > 0.01f)
            {
                RenderParams rp = new RenderParams(CurtainMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 64 * 6, 2);
            }
        }

        // ─────────────── старый дождь Atmosphere — выключить ───────────────
        private void DisableLegacy()
        {
            _legacyDone = true;
            int n = 0;
            n += Off(FindObjectsByType<CatCooksRain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            n += Off(FindObjectsByType<CatCooksRainFX>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            n += Off(FindObjectsByType<CatCooksRainSplashes>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            n += Off(FindObjectsByType<CatCooksPuddles>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            if (n > 0 && Log) Debug.Log("[Дождь] старый дождь Atmosphere выключен (компонентов: " + n + ") — работает дождь города", this);
        }

        private static int Off<T>(T[] list) where T : MonoBehaviour
        {
            int n = 0;
            if (list == null) return 0;
            foreach (T c in list)
            {
                if (c == null || !c.enabled) continue;
                c.enabled = false;
                foreach (ParticleSystem ps in c.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                n++;
            }
            return n;
        }

        // ─────────────── меню P (вкладка «Город») ───────────────
        public static readonly string[] ModeNames = { "Авто (по погоде)", "Морось", "Уютный дождик", "Грибной дождь", "Дождь", "Ливень", "Шторм" };

        public void NextMode()
        {
            int i = (int)Mode + 1;
            if (i > (int)Variant.Storm) i = -1;
            Mode = (Variant)i;
        }

        public string ModeLabel { get { return ModeNames[(int)Mode + 1]; } }

    }
}
