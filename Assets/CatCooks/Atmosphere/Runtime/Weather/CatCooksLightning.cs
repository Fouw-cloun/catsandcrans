using UnityEngine;
using UnityEngine.Rendering;
// ColorAdjustments и прочие эффекты Volume лежат в Universal, а не
// в Rendering. Сам Volume — в Rendering, поэтому он находится и без
// этой строки, а эффекты нет.
using UnityEngine.Rendering.Universal;

// ============================================================================
//  CatCooksLightning — удары молнии.
//
//  Молния — это не картинка разряда, а ВСПЫШКА ОСВЕЩЕНИЯ. Разряд в кадре
//  виден одну шестую секунды и чаще всего вообще за облаками; читается
//  именно то, что на мгновение стало светло, а потом громыхнуло.
//  Поэтому здесь работа идёт со светом и ambient, а не с геометрией.
//
//  ФОРМА ВСПЫШКИ. Реальный разряд почти всегда многоимпульсный: яркий
//  скачок, спад, ещё один-два слабее. Одиночный ровный импульс читается
//  как «кто-то дёрнул яркость» — узнаваемость даёт именно дробность.
//
//  ГРОМ приходит с задержкой по расстоянию: три секунды на километр.
//  Задержка и делает вспышку далёкой или близкой.
//
//  Сеть: под FishNet сервер решает, когда бить, и рассылает всем —
//  иначе у каждого игрока молнии сверкали бы в своё время.
// ============================================================================
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Lightning")]
public class CatCooksLightning : MonoBehaviour
{
    [Header("Связи")]
    [Tooltip("Засветка кадра в момент разряда. Пусто — найдётся сам.")]
    public CatCooksWeatherFX weatherFX;

    [Header("Свет")]
    [Tooltip("Directional Light сцены. Пусто — найдётся сам.")]
    public Light sun;

    [Tooltip("Volume, у которого на миг поднимается экспозиция.")]
    public Volume postVolume;

    [Header("Частота")]
    [Tooltip("0 = молний нет, 1 = гроза. Ведёт погодный пресет.")]
    [Range(0f, 1f)] public float activity = 0f;

    [Tooltip("Пауза между ударами в слабую и сильную грозу, секунд.")]
    public Vector2 intervalRange = new Vector2(22f, 4f);

    [Header("Вспышка")]
    [Tooltip("Насколько ярче становится направленный свет на пике.")]
    [Range(0f, 6f)] public float lightBoost = 2.2f;

    [Tooltip("Подъём экспозиции на пике. Держи небольшим: вспышка должна " +
             "бить по глазам мгновение, а не выжигать кадр.")]
    [Range(0f, 1.5f)] public float exposureBoost = 0.5f;

    [Tooltip("Холодный цвет разряда")]
    public Color flashColor = new Color(0.82f, 0.88f, 1f);

    [Tooltip("Длительность одного удара со всеми импульсами, секунд.")]
    public float flashDuration = 0.45f;

    [Tooltip("Импульсов в ударе. Одиночный читается как рывок яркости.")]
    public Vector2Int pulses = new Vector2Int(2, 4);

    [Header("Разряд в землю")]
    [Tooltip("Компонент, рисующий сам канал. Пусто — найдётся сам. " +
             "Без него молния остаётся тем, чем была: вспышкой освещения.")]
    public CatCooksLightningBolt bolt;
    [Tooltip("Зоны ударов: рядом с игроком (с местом удара) и за игровой зоной. Пусто — как раньше.")]
    public CatCooksLightningZones zones;

    [Tooltip("Доля ударов, которые бьют в землю. Остальные — внутри " +
             "облаков, только вспышка.\n\n" +
             "Держи НИЖЕ половины. В грозу разрядов облако-облако в разы " +
             "больше, чем облако-земля, и если каждая вспышка будет " +
             "втыкаться в рельеф, гроза станет похожа на обстрел.")]
    [Range(0f, 1f)] public float groundStrikeChance = 0.35f;

    [Tooltip("Ниже этой силы грозы в землю не бьёт вообще: далёкая гроза " +
             "видна зарницами за горизонтом, а не ударами под ногами.")]
    [Range(0f, 1f)] public float groundStrikeThreshold = 0.35f;

    [Header("Гром")]
    public AudioSource thunderSource;
    public AudioClip[] thunderClips;

    [Tooltip("Расстояние до разряда, км. Гром идёт три секунды на километр.")]
    public Vector2 distanceKm = new Vector2(0.3f, 4f);

    [Range(0f, 1f)] public float thunderVolume = 0.7f;

    // ───────── внутреннее ─────────
    private float _timer;
    private float _flash;          // текущая яркость вспышки 0…1
    private float _flashAge;
    private bool _flashing;
    private int _pulseCount;
    private float _thunderAt = -1f;
    private int _thunderClip;

    private float _strikePower = 1f;
    private float _thunderKm = 1f;
    private float _sunBaseIntensity;
    private Color _sunBaseColor;
    private bool _sunSaved;
    private ColorAdjustments _ca;
    private float _caBase;

    void OnEnable()
    {
        if (sun == null)
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude,
                                                       FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        }
        if (postVolume == null)
        {
            foreach (var v in FindObjectsByType<Volume>(FindObjectsInactive.Exclude,
                                                        FindObjectsSortMode.None))
                if (v.isGlobal) { postVolume = v; break; }
        }
        if (weatherFX == null) weatherFX = FindFirstObjectByType<CatCooksWeatherFX>();
        if (bolt == null) bolt = FindFirstObjectByType<CatCooksLightningBolt>();
        _timer = NextInterval();
    }

    void OnDisable() => RestoreSun();

    void Update()
    {
        if (!Application.isPlaying) return;

        float dt = Time.deltaTime;

        if (_flashing) TickFlash(dt);
        else if (activity > 0.01f)
        {
            _timer -= dt;
            if (_timer <= 0f) Strike();
        }

        if (_thunderAt > 0f)
        {
            _thunderAt -= dt;
            if (_thunderAt <= 0f) PlayThunder();
        }
    }

    /// <summary>Ударить прямо сейчас. Можно звать из своей логики.</summary>
    [ContextMenu("Ударить")]
    public void Strike() => Strike(Random.Range(int.MinValue, int.MaxValue));

    /// <summary>
    /// Ударить с заданным seed. Форма разряда и точка удара определяются
    /// ИМ ОДНИМ, поэтому для синхронной грозы по сети достаточно разослать
    /// одно целое число — сервер решает когда бить и с каким seed, клиенты
    /// вызывают это. Позиции игроков синхронизировать не нужно: точка
    /// считается от камеры каждого, но форма совпадает.
    /// </summary>
    public void Strike(int seed)
    {
        if (sun == null) return;

        if (!_sunSaved)
        {
            _sunBaseIntensity = sun.intensity;
            _sunBaseColor = sun.color;
            _sunSaved = true;
        }

        var rng = new System.Random(seed);

        _flashing = true;
        _flashAge = 0f;
        _pulseCount = pulses.x + rng.Next(Mathf.Max(1, pulses.y - pulses.x + 1));
        _timer = NextInterval();

        // РАЗРЯД В ЗЕМЛЮ. Не каждый удар: большинство разрядов в грозу
        // идут внутри облаков, и для них хватает вспышки.
        bool hitGround = bolt != null
                      && activity >= groundStrikeThreshold
                      && rng.NextDouble() < groundStrikeChance
                      && (zones != null ? zones.Fire(seed, activity) : bolt.Fire(seed, activity));

        // Гром: три секунды на километр. Именно задержка делает
        // вспышку далёкой или близкой, а не громкость.
        //
        // Для удара в землю расстояние берём НАСТОЯЩЕЕ — иначе канал
        // втыкается в холм в полусотне метров, а гром приходит через
        // девять секунд, будто било за горизонтом.
        float km = hitGround
            ? Mathf.Max(bolt.LastDistance, 20f) / 1000f
            : Mathf.Lerp(distanceKm.y, distanceKm.x, activity)
              * Mathf.Lerp(0.6f, 1.4f, (float)rng.NextDouble());
        _thunderAt = km * 3f;

        // Удар в землю бьёт по глазам сильнее дальней зарницы.
        _strikePower = hitGround ? 1f : 0.65f;
        _thunderClip = thunderClips != null && thunderClips.Length > 0
                     ? rng.Next(thunderClips.Length) : -1;
        _thunderKm = km;

        // Засветка кадра идёт вместе со светом: разряд бьёт и по сцене,
        // и по «сенсору камеры». Без второго вспышка выглядит так, будто
        // кто-то дёрнул ползунок яркости солнца.
        if (weatherFX != null)
            weatherFX.Flash((0.35f + activity * 0.45f) * _strikePower);

        var report = CatCooksWeatherReport.Instance;
        if (report != null) report.ReportStrike(activity);
    }

    void TickFlash(float dt)
    {
        _flashAge += dt;
        float t = _flashAge / Mathf.Max(flashDuration, 0.05f);

        if (t >= 1f)
        {
            _flashing = false;
            _flash = 0f;
            RestoreSun();
            return;
        }

        // МНОГОИМПУЛЬСНАЯ форма. Пила по числу импульсов, наложенная на
        // общий спад: яркий скачок, спад, ещё один-два слабее. Ровный
        // одиночный импульс не читается как молния.
        float saw = 1f - Mathf.Repeat(t * _pulseCount, 1f);
        float envelope = 1f - t;
        _flash = Mathf.Pow(saw, 2.2f) * Mathf.Pow(envelope, 0.8f);

        // Первый импульс всегда сильнее остальных.
        if (t < 1f / _pulseCount) _flash = Mathf.Max(_flash, Mathf.Pow(1f - t * _pulseCount, 1.5f));

        ApplyFlash(_flash);
    }

    void ApplyFlash(float f)
    {
        if (sun != null)
        {
            sun.intensity = _sunBaseIntensity + lightBoost * f * _strikePower;
            sun.color = Color.Lerp(_sunBaseColor, flashColor,
                                   Mathf.Clamp01(f * _strikePower));
        }

        if (postVolume != null && postVolume.profile != null)
        {
            if (_ca == null) postVolume.profile.TryGet(out _ca);
            if (_ca != null)
            {
                if (f > 0.001f && !_flashApplied) { _caBase = _ca.postExposure.value; _flashApplied = true; }
                _ca.postExposure.Override(_caBase + exposureBoost * f * _strikePower);
            }
        }
    }

    private bool _flashApplied;

    void RestoreSun()
    {
        if (_sunSaved && sun != null)
        {
            sun.intensity = _sunBaseIntensity;
            sun.color = _sunBaseColor;
        }
        _sunSaved = false;

        // Экспозицию обязательно возвращаем: иначе пульт при следующем
        // применении заберёт задранное значение как базовое, и кадр
        // останется пересвеченным навсегда.
        if (_flashApplied && _ca != null) _ca.postExposure.Override(_caBase);
        _flashApplied = false;
    }

    void PlayThunder()
    {
        _thunderAt = -1f;
        if (thunderSource == null || _thunderClip < 0) return;

        thunderSource.clip = thunderClips[_thunderClip];
        // Близкий гром — резкий треск, дальний — раскат. Дистанция
        // рулит и громкостью, и тоном: далёкий звук теряет верха, и
        // понижение тона имитирует это дешевле любого фильтра.
        float near = 1f - Mathf.Clamp01(_thunderKm / 4f);
        thunderSource.volume = thunderVolume
                             * Mathf.Lerp(0.35f, 1f, near)
                             * Mathf.Lerp(0.6f, 1f, activity);
        thunderSource.pitch = Mathf.Lerp(0.75f, 1.08f, near)
                            * Random.Range(0.96f, 1.04f);
        thunderSource.Play();
    }

    float NextInterval()
    {
        float basei = Mathf.Lerp(intervalRange.x, intervalRange.y, Mathf.Clamp01(activity));
        return basei * Random.Range(0.55f, 1.45f);
    }

    /// <summary>Дёргать из погодных пресетов: 0 = ясно, 1 = гроза.</summary>
    public void SetActivity(float t)
    {
        activity = Mathf.Clamp01(t);
        if (activity <= 0.01f && _flashing)
        {
            _flashing = false;
            RestoreSun();
        }
    }
}
