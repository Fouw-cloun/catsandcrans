using System.Collections.Generic;
using UnityEngine;

// ============================================================================
//  CatCooksWeatherSchedule — погода по игровому времени.
//
//  Каждые несколько ИГРОВЫХ часов выбирает следующую погоду:
//    • по весам и по части суток (утренний туман, дневная гроза, ясная ночь);
//    • цепочкой — не прыгает из ясного сразу в грозу: следующая погода
//      отличается по «силе непогоды» не больше чем на Max Severity Step;
//    • детерминированно по Seed — один и тот же день всегда одинаковый.
//
//  Решает только сервер (хост). Смена уходит через Mood Director.SetWeather,
//  а он уже синхронизирует индекс клиентам.
//
//  Ручная смена погоды (меню P, Mood Director) ставит расписание на паузу
//  на Manual Hold Hours игровых часов — чтобы оно не перебило тебя сразу.
// ============================================================================
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Weather Schedule")]
public class CatCooksWeatherSchedule : MonoBehaviour
{
    [System.Flags]
    public enum DayPart { Night = 1, Morning = 2, Day = 4, Evening = 8, Any = 15 }

    [System.Serializable]
    public class Entry
    {
        public CatCooksWeatherPreset weather;
        [Tooltip("Вес выбора. 0 = никогда.")]
        [Range(0f, 5f)] public float weight = 1f;
        [Tooltip("Когда эта погода может начаться")]
        public DayPart when = DayPart.Any;
        [Tooltip("Сколько игровых часов держится (от и до)")]
        public Vector2 hours = new Vector2(2f, 5f);
    }

    [Header("━━━ Связи ━━━")]
    public CatCooksMoodDirector director;
    public CatCooksDayCycle dayCycle;

    [Header("━━━ Расписание ━━━")]
    public bool scheduleEnabled = true;
    [Tooltip("Пусто — берутся все погоды из Mood Director с весом 1.")]
    public List<Entry> pool = new List<Entry>();

    [Tooltip("Зерно. Одно и то же зерно — одна и та же погода по дням.")]
    public int seed = 20260930;

    [Tooltip("Насколько следующая погода может быть «злее» или «добрее» текущей (severity 0..1)")]
    [Range(0.1f, 1f)] public float maxSeverityStep = 0.35f;

    [Tooltip("Секунд перехода между погодами из расписания. 0 — из пресета.")]
    public float transitionSeconds = 40f;

    [Tooltip("После ручной смены погоды — пауза расписания, игровых часов")]
    public float manualHoldHours = 3f;

    [Header("━━━ Старт сцены ━━━")]
    [Tooltip("Погода при запуске сцены (по умолчанию — «ясно, облака у горизонта», как Shady Job)")]
    public CatCooksWeatherPreset startupWeather;
    [Tooltip("Сколько РЕАЛЬНЫХ секунд держать стартовую погоду, потом начинается динамика")]
    public float startupHoldSeconds = 120f;

    [Header("━━━ Прогресс ━━━")]
    [Tooltip("Максимальная сила непогоды по игровому дню (X — день, Y — 0..1). Ранние дни мягкие, дальше злее.")]
    public AnimationCurve maxSeverityByDay = new AnimationCurve(new Keyframe(1, 0.45f), new Keyframe(3, 0.7f), new Keyframe(6, 1f));

    [Header("━━━ Сейчас (только чтение) ━━━")]
    [SerializeField] string _current = "-";
    [SerializeField] float _hoursLeft;

    double _endsAt = -1, _holdUntil = -1, _lastAbs = -1;
    float _startTime = -1f; bool _startupDone;
    int _lastSetIndex = -1, _step;
    System.Random _rng;

    public string CurrentName => _current;
    public float HoursLeft => _hoursLeft;

    void Reset()
    {
        director = FindFirstObjectByType<CatCooksMoodDirector>();
        dayCycle = FindFirstObjectByType<CatCooksDayCycle>();
    }

    void Update()
    {
        if (!Application.isPlaying || !scheduleEnabled || director == null || dayCycle == null) return;
        if (!director.HasAuthority) return;          // решает только сервер
        if (director.weathers.Count == 0) return;

        // старт: держим стартовую погоду, потом — динамика
        if (_startTime < 0f)
        {
            _startTime = Time.time;
            int si = startupWeather != null ? director.weathers.IndexOf(startupWeather) : -1;
            if (si >= 0) { director.SetWeather(si, 0f); _lastSetIndex = si; _current = Name(si) + " (старт)"; }
        }
        if (!_startupDone)
        {
            if (Time.time - _startTime < startupHoldSeconds) { _hoursLeft = 0; return; }
            _startupDone = true; _endsAt = -1;
        }

        double now = dayCycle.AbsoluteHours;
        if (_rng == null) _rng = new System.Random(seed);

        // Время прыгнуло назад (перемотка/новая сессия) — начинаем заново
        if (_lastAbs >= 0 && now < _lastAbs - 0.01) _endsAt = -1;
        _lastAbs = now;

        // Кто-то сменил погоду руками — пауза
        if (_lastSetIndex >= 0 && director.weatherIndex != _lastSetIndex)
        {
            _lastSetIndex = director.weatherIndex;
            _holdUntil = now + manualHoldHours;
            _endsAt = _holdUntil;
            _current = Name(director.weatherIndex) + " (вручную)";
        }

        _hoursLeft = (float)System.Math.Max(0, _endsAt - now);
        if (_endsAt >= 0 && now < _endsAt) return;

        PickNext(now);
    }

    void PickNext(double now)
    {
        var entries = Entries();
        if (entries.Count == 0) return;

        DayPart part = PartOf(dayCycle.Hour);
        var cur = director.weathers[Mathf.Clamp(director.weatherIndex, 0, director.weathers.Count - 1)];
        float curSev = cur != null ? cur.severity : 0f;

        // детерминированно: зерно + номер шага
        _rng = new System.Random(seed * 7919 + _step++);

        float total = 0f;
        var cand = new List<(Entry e, float w)>();
        foreach (var e in entries)
        {
            if (e.weather == null || e.weight <= 0f) continue;
            if ((e.when & part) == 0) continue;
            if (e.weather.unlockDay > dayCycle.Day) continue;                                  // ещё не открыта
            if (e.weather.severity > maxSeverityByDay.Evaluate(dayCycle.Day) + 0.001f) continue;  // слишком рано для такой
            float step = Mathf.Abs(e.weather.severity - curSev);
            if (step > maxSeverityStep && entries.Count > 1) continue;
            // чуть реже повторяем ту же погоду подряд
            float w = e.weight * (e.weather == cur ? 0.6f : 1f);
            cand.Add((e, w)); total += w;
        }
        if (cand.Count == 0)
        {
            // ничего не подошло по цепочке — ближайшая по силе
            Entry best = null; float bd = 9f;
            foreach (var e in entries)
            {
                if (e.weather == null) continue;
                float d = Mathf.Abs(e.weather.severity - curSev);
                if (d < bd) { bd = d; best = e; }
            }
            if (best == null) return;
            cand.Add((best, 1f)); total = 1f;
        }

        double r = _rng.NextDouble() * total;
        Entry pick = cand[cand.Count - 1].e;
        foreach (var c in cand) { r -= c.w; if (r <= 0) { pick = c.e; break; } }

        int idx = director.weathers.IndexOf(pick.weather);
        if (idx < 0) return;
        float dur = Mathf.Lerp(pick.hours.x, pick.hours.y, (float)_rng.NextDouble());
        _endsAt = now + Mathf.Max(0.25f, dur);
        if (idx != director.weatherIndex)
            director.SetWeather(idx, transitionSeconds > 0f ? transitionSeconds : -1f);
        _lastSetIndex = idx;
        _current = Name(idx);
        _hoursLeft = dur;
    }

    List<Entry> Entries()
    {
        if (pool != null && pool.Count > 0) return pool;
        var list = new List<Entry>();
        foreach (var w in director.weathers)
            if (w != null) list.Add(new Entry { weather = w, weight = 1f, when = DayPart.Any, hours = new Vector2(2f, 5f) });
        return list;
    }

    string Name(int i) => (i >= 0 && i < director.weathers.Count && director.weathers[i] != null)
        ? director.weathers[i].title : "-";

    public static DayPart PartOf(float hour)
    {
        if (hour >= 5f && hour < 10f) return DayPart.Morning;
        if (hour >= 10f && hour < 17f) return DayPart.Day;
        if (hour >= 17f && hour < 21.5f) return DayPart.Evening;
        return DayPart.Night;
    }

    /// <summary>Сбросить и выбрать погоду прямо сейчас (для отладки).</summary>
    [ContextMenu("Следующая погода по расписанию")]
    public void ForceNext() { _endsAt = -1; _holdUntil = -1; }
}
