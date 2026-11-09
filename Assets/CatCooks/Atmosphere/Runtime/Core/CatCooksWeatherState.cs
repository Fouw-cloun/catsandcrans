using UnityEngine;

// ============================================================================
//  CatCooksWeatherState — текущая погода одной табличкой для всех эффектов.
//  Пишет Mood Director (каждое применение), читают эффекты: дождь, лужи,
//  молнии по зонам, песчаная буря, слои тумана в городе.
//  Так эффекты не зависят от пульта напрямую и их легко включать/выключать.
// ============================================================================
public static class CatCooksWeatherState
{
    public static float Rain;          // 0..1 сила дождя
    public static float Wind;          // 0..1
    public static Vector2 WindDir = new Vector2(0.8f, 0.6f);   // по земле (x, z)
    public static float Dust;          // 0..1 песок
    public static float Leaves;        // 0..1
    public static float Storm;         // 0..1 молнии
    public static float Overcast;      // 0..1
    public static float Darkness;      // 0..1
    public static float FogDensity;    // 0..1
    public static float Day01 = 1f;
    public static Color FogNear = new Color(0.9f, 0.85f, 0.8f);
    public static Color FogFar = new Color(0.85f, 0.9f, 0.95f);
    public static Color CloudLit = Color.white;
    public static Color CloudShadow = new Color(0.75f, 0.8f, 0.9f);
    public static Color SunColor = Color.white;
    public static Color Ambient = new Color(0.6f, 0.7f, 0.8f);
    public static Color SkyZenith = new Color(0.25f, 0.6f, 0.85f);
    public static Color SkyHorizon = new Color(0.7f, 0.85f, 0.92f);

    // ── геймплей: погода влияет на сложность и прогресс
    /// <summary>Текущая погода (пресет). Меняется пультом.</summary>
    public static CatCooksWeatherPreset Current;
    /// <summary>Сложность текущей погоды 0..1 (из пресета: Difficulty, иначе Severity).</summary>
    public static float Difficulty;
    /// <summary>Флаги опасностей текущей погоды (дождь, ветер, буря, туман, гроза…).</summary>
    public static CatCooksWeatherPreset.Hazard Hazards;
    /// <summary>Погода сменилась (старая, новая). Подписывайся из геймплея: штрафы, бонусы, задания.</summary>
    public static event System.Action<CatCooksWeatherPreset, CatCooksWeatherPreset> WeatherChanged;
    internal static void RaiseChanged(CatCooksWeatherPreset from, CatCooksWeatherPreset to)
    {
        Current = to;
        Difficulty = to != null ? (to.difficulty >= 0f ? to.difficulty : to.severity) : 0f;
        Hazards = to != null ? to.hazards : 0;
        WeatherChanged?.Invoke(from, to);
    }

    /// <summary>Мокрость мира 0..1 (копится в дождь, сохнет после). Считают лужи.</summary>
    public static float Wetness;

    public static Vector3 WindDir3 => new Vector3(WindDir.x, 0f, WindDir.y);

    /// <summary>Позиция «игрока» для зон эффектов: главная камера.</summary>
    public static bool TryGetPlayer(out Vector3 pos)
    {
        var cam = Camera.main;
        if (cam != null) { pos = cam.transform.position; return true; }
        pos = Vector3.zero;
        return false;
    }
}
