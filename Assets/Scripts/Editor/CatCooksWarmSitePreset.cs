#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// ПАЛИТРА И СТИЛЬ ПО РАЗБОРУ РЕФЕРЕНСА.
///
/// Добавляется в УЖЕ СУЩЕСТВУЮЩУЮ систему атмосферы: те же CatCooksPalette и
/// CatCooksSkyStyle, тот же MoodDirector. Ничего не заменяет и не ломает —
/// просто ещё один пресет в списке рядом с Candy, Ghibli, Amber и остальными.
///
/// ═══ ЧТО ДАЛ РАЗБОР КАДРОВ ═══
///
/// ТЕНИ ТЁПЛЫЕ. Освещённая сторона балки #D08A5E, теневая #9C6A50 —
/// коричневый, а не синий. Обычно тень уводят в синеву, «так физичнее», и
/// картинка немедленно становится холодной. Здесь в холодном только небо.
///
/// ГОРИЗОНТ ПОЧТИ БЕЛЫЙ. Синева живёт в верхней трети, ниже — кремовая
/// пелена. Не плавный голубой градиент до земли, а быстрый уход в тёплый
/// свет. Отсюда высокий контраст между зенитом и линией горизонта.
///
/// ДЫМКА ПЛОТНАЯ И ТЁПЛАЯ. Здание в двухстах метрах съедено на две трети.
/// Это одновременно и уют, и оптимизация: за такой дымкой можно ставить
/// что угодно.
///
/// НАСЫЩЕННОСТЬ УМЕРЕННАЯ. Цвет там даёт не яркость, а РАЗДЕЛЕНИЕ: небо
/// холодное, всё остальное тёплое. Выкрутишь насыщенность — получишь
/// кислотность вместо уюта.
///
/// Меню: CatCooks ▸ Атмосфера ▸ Добавить пресет «Тёплая стройка»
/// </summary>
public static class CatCooksWarmSitePreset
{
    // ПУТИ ДОЛЖНЫ СОВПАДАТЬ С ФАБРИКОЙ ПРЕСЕТОВ.
    //
    // Она читает папку целиком через FindAssets, а не по списку имён. Значит
    // достаточно положить ассеты туда же — и они подхватятся сами, наравне с
    // родными.
    //
    // В прошлой версии я писал на уровень выше, в Atmosphere/Styles вместо
    // Atmosphere/Presets/Styles. Ассеты создавались, работали через своё меню,
    // но для фабрики их не существовало.
    private const string Root = "Assets/CatCooks/Atmosphere/Presets";
    private const string PalDir = Root + "/Palettes";
    private const string StyleDir = Root + "/Styles";
    private const string WeatherDir = Root + "/Weather";

    // Куда они попали в прошлый раз. Чистим, иначе останутся дубли, которые
    // никто не читает, но которые путают при поиске.
    private const string OldPalDir = "Assets/CatCooks/Atmosphere/Palettes";
    private const string OldStyleDir = "Assets/CatCooks/Atmosphere/Styles";

    [MenuItem("CatCooks/Атмосфера/Добавить пресет «Тёплая стройка»", false, 7)]
    private static void Create()
    {
        EnsureFolder(PalDir);
        EnsureFolder(StyleDir);

        CatCooksPalette noon = MakePalette("P_WarmSite_Noon",
            "Тёплый полдень по разбору референса. Синева только наверху, " +
            "горизонт кремовый, ТЕНИ ТЁПЛЫЕ — это главное.",
            zenith:   H("4E9CC4"),   // синева только в зените
            horizon:  H("F0E6DA"),   // кремовая пелена
            ground:   H("C6B5A6"),
            cloudLit: H("FFFAF2"),
            cloudSh:  H("D8C8BC"),   // тень облака ТЁПЛАЯ, не синяя
            ambTop:   H("BCD2E0"),
            ambBot:   H("C8B49E"),
            cloudBase: H("B49C8A"),
            sunDisc:  H("FFF2D8"),
            sunLight: H("FFEFD4"),
            aSky:     H("A8C4D8"),
            aEq:      H("D4C2AC"),   // экватор тёплый — он и красит бока
            aGnd:     H("9C8470"),   // отражённый свет земли, тёплый
            fogNear:  H("F2E8DC"),
            fogFar:   H("E0D0C0"),
            ray:      H("FFEFD0"),
            streak:   H("FFF8EC"));

        CatCooksPalette evening = MakePalette("P_WarmSite_Evening",
            "Золотой вечер той же гаммы. Солнце низкое, всё уходит в янтарь, " +
            "синева остаётся узкой полосой у самого зенита.",
            zenith:   H("4A86AE"),
            horizon:  H("FFD9A8"),
            ground:   H("B89476"),
            cloudLit: H("FFE8C4"),
            cloudSh:  H("C8A088"),
            ambTop:   H("C4B0A8"),
            ambBot:   H("C09068"),
            cloudBase: H("A07E68"),
            sunDisc:  H("FFD290"),
            sunLight: H("FFCE94"),
            aSky:     H("B0AEB4"),
            aEq:      H("D4A87C"),
            aGnd:     H("8C6E58"),
            fogNear:  H("FFE4C4"),
            fogFar:   H("EAC49C"),
            ray:      H("FFD8A0"),
            streak:   H("FFF0DC"));

        MakeStyle("S_WarmSite_Noon", "Тёплая стройка — полдень", noon,
            sunIntensity: 1.25f, sunElevation: 46f, sunAzimuth: 35f,
            sunDisc: 3.4f, sunGlow: 1.2f,
            // Контраст в ПЛЮС, а не в минус.
            //
            // Уют легко перепутать с вялостью. В референсе контраст низкий
            // ТОЛЬКО вдали, и делает его дымка. Общий контраст там обычный:
            // ближние балки отчётливо делятся на свет и тень.
            //
            // Сядешь на минус — и тёплый сдвиг, тонировка теней и туман
            // сложатся в ровное бурое поле без глубины.
            exposure: 0.12f, saturation: 14f, contrast: 6f, temperature: 16f,
            bloom: 0.42f, vignette: 0.12f,
            cloudToon: 0.4f, cloudSteps: 4f, lightWrap: 0.6f, silver: 1.3f);

        MakeStyle("S_WarmSite_Evening", "Тёплая стройка — вечер", evening,
            sunIntensity: 1.0f, sunElevation: 14f, sunAzimuth: 285f,
            sunDisc: 4.5f, sunGlow: 1.8f,
            exposure: 0.05f, saturation: 20f, contrast: 4f, temperature: 28f,
            bloom: 0.7f, vignette: 0.2f,
            cloudToon: 0.35f, cloudSteps: 4f, lightWrap: 0.7f, silver: 2.0f);

        CleanOldFolders();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int total = Rebuild();

        Debug.Log($"[Атмосфера] Пресеты «Тёплая стройка» созданы. " +
                  $"В пульте теперь стилей: {total}.\n" +
                  "Выбери нужный через Style Index.");
    }

    /// <summary>
    /// ПЕРЕСОБРАТЬ СПИСОК ПУЛЬТА С ДИСКА, БЕЗУСЛОВНО.
    ///
    /// ═══ ПОЧЕМУ НЕ ПОЛЬЗУЕМСЯ «ЗАПОЛНИТЬ ПУЛЬТ» ═══
    ///
    /// Там стоит условие:
    ///
    ///     if (dir.styles.Count == 0 || dir.styles.Count &lt; styles.Count)
    ///
    /// То есть решение принимается по КОЛИЧЕСТВУ, а не по составу. Стоит в
    /// пульте оказаться лишнему элементу — и список навсегда перестаёт
    /// обновляться: он не пуст и не короче того, что на диске.
    ///
    /// Ровно это и произошло, когда прошлая версия дописала два своих стиля
    /// напрямую: стало четырнадцать против двенадцати на диске, условие
    /// замкнулось, и базовые пресеты в пульт больше не попадали.
    ///
    /// Здесь список собирается заново из папки целиком. Порядок по имени
    /// файла — тот же, что у фабрики, поэтому номера стилей не разъезжаются.
    /// </summary>
    private static int Rebuild()
    {
        CatCooksMoodDirector dir = Object.FindFirstObjectByType<CatCooksMoodDirector>();

        if (dir == null)
        {
            Debug.LogWarning("[Атмосфера] Пульт не найден на сцене. Сначала " +
                             "«Собрать сцену», потом запусти это ещё раз.");
            return 0;
        }

        Undo.RecordObject(dir, "Пересбор пресетов");

        dir.styles = LoadAll<CatCooksSkyStyle>(StyleDir);

        // Погоды не трогаем: их мы не добавляли, и лезть в чужой список без
        // причины — лишний способ что-нибудь сломать.
        dir.weathers.RemoveAll(x => x == null);

        if (dir.weathers.Count == 0)
            dir.weathers = LoadAll<CatCooksWeatherPreset>(WeatherDir);

        dir.styleIndex = Mathf.Clamp(dir.styleIndex, 0, Mathf.Max(dir.styles.Count - 1, 0));
        dir.weatherIndex = Mathf.Clamp(dir.weatherIndex, 0, Mathf.Max(dir.weathers.Count - 1, 0));

        EditorUtility.SetDirty(dir);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(dir.gameObject.scene);

        dir.Apply();

        for (int i = 0; i < dir.styles.Count; i++)
            Debug.Log($"  [{i}] {(dir.styles[i] != null ? dir.styles[i].title : "ПУСТО")}");

        return dir.styles.Count;
    }

    /// <summary>
    /// ЧИТАЕТ ПАПКУ, А ЕСЛИ ТАМ ПУСТО — ВЕСЬ ПРОЕКТ.
    ///
    /// ═══ ПОЧЕМУ ЗАПАСНОЙ ПУТЬ ═══
    ///
    /// Поиск по папке ломается от любой мелочи: ассеты создали на уровень
    /// выше, папку переименовали, кто-то перетащил их мышкой. Каждый раз
    /// снаружи это выглядит одинаково и одинаково непонятно — «список пуст».
    ///
    /// Поиск по типу во всём проекте от расположения не зависит вовсе.
    /// Медленнее, но зовётся он раз в жизни, по кнопке.
    ///
    /// Порядок при этом сохраняем по имени файла — тем же, что у фабрики,
    /// иначе номера стилей разойдутся с её нумерацией.
    /// </summary>
    private static System.Collections.Generic.List<T> LoadAll<T>(string folder)
        where T : Object
    {
        var list = Search<T>(AssetDatabase.IsValidFolder(folder) ? new[] { folder } : null);

        if (list.Count > 0) return list;

        // Папка пуста или её нет. Ищем по всему проекту.
        list = Search<T>(null);

        if (list.Count > 0)
        {
            Debug.LogWarning($"[Атмосфера] В «{folder}» ассетов типа " +
                             $"{typeof(T).Name} нет, но в проекте нашлось " +
                             $"{list.Count}. Взял их. Лежат они здесь:\n  " +
                             string.Join("\n  ", PathsOf(list)));
        }
        else
        {
            Debug.LogError($"[Атмосфера] Ассетов типа {typeof(T).Name} нет " +
                           "НИГДЕ в проекте. Значит их не создали: запусти " +
                           "«CatCooks ▸ Атмосфера ▸ Создать пресеты».");
        }

        return list;
    }

    private static System.Collections.Generic.List<T> Search<T>(string[] folders)
        where T : Object
    {
        var list = new System.Collections.Generic.List<T>();

        string[] guids = folders == null
            ? AssetDatabase.FindAssets("t:" + typeof(T).Name)
            : AssetDatabase.FindAssets("t:" + typeof(T).Name, folders);

        var paths = new System.Collections.Generic.List<string>();
        foreach (string g in guids) paths.Add(AssetDatabase.GUIDToAssetPath(g));

        // По имени файла, а не по полному пути: иначе ассеты из разных папок
        // перемешаются не в том порядке, и номера стилей поедут.
        paths.Sort((a, b) => string.CompareOrdinal(
            System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b)));

        foreach (string pth in paths)
        {
            T a = AssetDatabase.LoadAssetAtPath<T>(pth);
            if (a != null) list.Add(a);
        }

        return list;
    }

    private static System.Collections.Generic.List<string> PathsOf<T>(
        System.Collections.Generic.List<T> list) where T : Object
    {
        var r = new System.Collections.Generic.List<string>();
        foreach (T a in list) r.Add(AssetDatabase.GetAssetPath(a));
        return r;
    }

    [MenuItem("CatCooks/Атмосфера/Где лежат пресеты", false, 11)]
    private static void Report()
    {
        var sb = new System.Text.StringBuilder("═══ ПРЕСЕТЫ АТМОСФЕРЫ ═══\n\n");

        foreach (string f in new[] { Root, PalDir, StyleDir, WeatherDir,
                                     OldPalDir, OldStyleDir })
            sb.AppendLine($"{(AssetDatabase.IsValidFolder(f) ? "есть " : "НЕТ  ")} {f}");

        var styles = Search<CatCooksSkyStyle>(null);
        var weathers = Search<CatCooksWeatherPreset>(null);
        var pals = Search<CatCooksPalette>(null);

        sb.AppendLine($"\nВ ПРОЕКТЕ: стилей {styles.Count}, погод " +
                      $"{weathers.Count}, палитр {pals.Count}");

        foreach (string pth in PathsOf(styles)) sb.AppendLine("  " + pth);

        CatCooksMoodDirector dir = Object.FindFirstObjectByType<CatCooksMoodDirector>();

        sb.AppendLine(dir == null
            ? "\nПУЛЬТ: не найден на сцене"
            : $"\nПУЛЬТ: стилей {dir.styles.Count}, погод {dir.weathers.Count}");

        Debug.Log(sb.ToString());
    }

    /// <summary>Снести ассеты, созданные прошлой версией не в той папке.</summary>
    private static void CleanOldFolders()
    {
        foreach (string dir in new[] { OldStyleDir, OldPalDir })
        {
            if (!AssetDatabase.IsValidFolder(dir)) continue;

            AssetDatabase.DeleteAsset(dir);
            Debug.Log($"[Атмосфера] Удалена папка от прошлой версии: {dir}");
        }
    }

    // ═════════════════════════ СБОРКА ═════════════════════════

    private static CatCooksPalette MakePalette(string file, string note,
        Color zenith, Color horizon, Color ground,
        Color cloudLit, Color cloudSh, Color ambTop, Color ambBot,
        Color cloudBase, Color sunDisc, Color sunLight,
        Color aSky, Color aEq, Color aGnd,
        Color fogNear, Color fogFar, Color ray, Color streak)
    {
        string path = $"{PalDir}/{file}.asset";

        CatCooksPalette p = AssetDatabase.LoadAssetAtPath<CatCooksPalette>(path);
        bool isNew = p == null;

        if (isNew) p = ScriptableObject.CreateInstance<CatCooksPalette>();

        p.note = note;
        p.zenith = zenith; p.horizon = horizon; p.ground = ground;
        p.cloudLit = cloudLit; p.cloudShadow = cloudSh; p.cloudBase = cloudBase;
        p.cloudAmbientTop = ambTop; p.cloudAmbientBottom = ambBot;
        p.sunDisc = sunDisc; p.sunLight = sunLight;
        p.ambientSky = aSky; p.ambientEquator = aEq; p.ambientGround = aGnd;
        p.fogNear = fogNear; p.fogFar = fogFar;
        p.rayTint = ray; p.windStreak = streak;

        if (isNew) AssetDatabase.CreateAsset(p, path);
        else EditorUtility.SetDirty(p);

        return p;
    }

    private static void MakeStyle(string file, string title, CatCooksPalette pal,
        float sunIntensity, float sunElevation, float sunAzimuth,
        float sunDisc, float sunGlow,
        float exposure, float saturation, float contrast, float temperature,
        float bloom, float vignette,
        float cloudToon, float cloudSteps, float lightWrap, float silver)
    {
        string path = $"{StyleDir}/{file}.asset";

        CatCooksSkyStyle s = AssetDatabase.LoadAssetAtPath<CatCooksSkyStyle>(path);
        bool isNew = s == null;

        if (isNew) s = ScriptableObject.CreateInstance<CatCooksSkyStyle>();

        s.title = title;
        s.note = pal.note;
        s.palette = pal;

        s.sunIntensity = sunIntensity;
        s.sunElevation = sunElevation;
        s.sunAzimuth = sunAzimuth;
        s.sunDiscIntensity = sunDisc;
        s.sunGlow = sunGlow;

        // Ступеней у неба нет: полосатый градиент хорош для плакатного стиля,
        // а здесь небо должно быть гладким фоном, на котором читается
        // силуэт стройки.
        s.skyBands = 1f;
        s.bandSoftness = 0.6f;

        s.cloudToon = cloudToon;
        s.cloudSteps = cloudSteps;
        s.lightWrap = lightWrap;
        s.silverLining = silver;
        s.cloudBottomShade = 0.38f;
        s.cloudBump = 0.16f;

        s.skyCloudSoft = 0.14f;
        s.skyCloudPuff = 0.88f;
        s.skyCloudWarp = 0.6f;
        s.skyCloudOutline = 0f;
        s.skyCloudScale = 2.2f;

        s.postExposure = exposure;
        s.saturation = saturation;
        s.contrast = contrast;
        s.temperature = temperature;
        s.bloom = bloom;
        s.vignette = vignette;

        if (isNew) AssetDatabase.CreateAsset(s, path);
        else EditorUtility.SetDirty(s);
    }

    private static Color H(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string[] parts = path.Split('/');
        string cur = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{cur}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
#endif
