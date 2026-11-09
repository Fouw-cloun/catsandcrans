using UnityEngine;

// ============================================================================
//  CatCooksWeatherMenu — экранный выбор погоды.
//
//  Нажимаешь P — выезжает ОДНА панель с вкладками «Погода · Стиль · Время»
//  + вкладка «ГОРОД» (её рисует город) и галочка «Тест»: всё перетекает
//  за 7 с, автосмена листает погоды. 1–4 — вкладки. Клик — плавный переход.
//  Внизу — что сейчас. Нажимаешь P снова — панель уезжает.
//
//  ПОЧЕМУ IMGUI, А НЕ UGUI
//   UGUI потребовал бы Canvas, EventSystem, префабы кнопок и раскладку —
//   и всё это надо держать в сцене и чинить при каждой правке. Здесь
//   ноль объектов: положил компонент, он работает. Для панели, которой
//   пользуется разработчик и тестировщик, это правильный размен.
//   Понадобится в релизном интерфейсе — перенесём в UGUI.
//
//  ПЕРЕХОД. Меню ВРЕМЕННО отключает Instant Switch у пульта: смысл
//  кнопок именно в том, чтобы погода перетекала. Ручной режим пульта
//  при этом не портится — прежнее значение возвращается при закрытии.
// ============================================================================
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Weather Menu")]
public class CatCooksWeatherMenu : MonoBehaviour
{
    [Header("Пульт")]
    public CatCooksMoodDirector mood;

    [Header("Клавиши")]
    public KeyCode toggleKey = KeyCode.P;

    [Tooltip("Вкладка «Стиль».")]
    public bool showStyles = true;

    [Tooltip("Вкладка «Время»: время суток и варианты закатов/ночей (предпросмотр Day Cycle).")]
    public bool showTime = true;
    public CatCooksDayCycle dayCycle;

    [Header("Переход")]
    [Tooltip("За сколько секунд погода перетекает при выборе из меню. " +
             "0 — взять время из самого пресета.")]
    public float transitionSeconds = 0f;

    [Header("Вид")]
    public int panelWidth = 320;
    [Tooltip("Максимальная высота панели (больше — прокрутка)")]
    public int maxPanelHeight = 640;
    public int rowHeight = 30;

    [Tooltip("Отступ от левого верхнего угла экрана")]
    public Vector2 margin = new Vector2(24f, 24f);

    [Range(0f, 1f)] public float backgroundAlpha = 0.82f;

    [Tooltip("Скорость выезда панели")]
    public float slideSpeed = 8f;

    [Header("Цвета")]
    public Color accent = new Color(0.55f, 0.85f, 0.95f);
    public Color panelColor = new Color(0.10f, 0.11f, 0.15f);
    public Color textColor = new Color(0.90f, 0.92f, 0.96f);

    [Header("Тест (галочка в меню P)")]
    [Tooltip("Тест: за сколько секунд перетекают погода, стиль и атмосфера")]
    public float testSeconds = 7f;
    [Tooltip("Тест-автосмена: новая погода каждые столько секунд")]
    public float testCycleSeconds = 14f;

    /// <summary>Тест включён: все переходы (погода, стиль, стиль за погодой) — за TestSeconds.</summary>
    public static bool TestFast;
    public static float TestSeconds = 7f;
    /// <summary>
    /// Вкладка «ГОРОД» (4) — её рисует город (CityAtmosphere): (ширина, рисовать?) → высота содержимого.
    /// Так Atmosphere ничего не знает о городе, а всё собрано в одном меню.
    /// </summary>
    public static System.Func<float, bool, float> CityTab;
    public static string CityTabName = "ГОРОД";
    /// <summary>Стиль идёт за погодой (город, CityWeatherLink) — вкладки «Стиль» нет.</summary>
    public static bool HideStyles;
    /// <summary>Подпись к погоде в списке (город: её стиль).</summary>
    public static System.Func<int, string> WeatherNote;

    // ───────── внутреннее ─────────
    private bool _testAuto;
    private float _testTimer;
    private bool _testSaved;
    private float _savedWT, _savedST;
    private bool _open;
    private float _slide;              // 0 закрыто … 1 открыто
    private bool _savedInstant;
    private bool _instantSaved;

    private GUIStyle _title, _row, _rowActive, _hint;
    private Texture2D _panelTex, _rowTex, _rowHoverTex, _accentTex;
    private bool _stylesReady;

    void Awake()
    {
        if (mood == null) mood = FindFirstObjectByType<CatCooksMoodDirector>();
        if (dayCycle == null) dayCycle = FindFirstObjectByType<CatCooksDayCycle>();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) Toggle();
        TestTick();

        float target = _open ? 1f : 0f;
        _slide = Mathf.MoveTowards(_slide, target, Time.unscaledDeltaTime * slideSpeed);
    }

    public void Toggle()
    {
        _open = !_open;

        if (mood == null) return;

        if (_open)
        {
            // Мгновенное переключение удобно при подборе, но здесь оно
            // убивает весь смысл: погода должна ПЕРЕТЕКАТЬ.
            if (!_instantSaved) { _savedInstant = mood.instantSwitch; _instantSaved = true; }
            mood.instantSwitch = false;
        }
        else if (_instantSaved)
        {
            mood.instantSwitch = TestFast ? false : _savedInstant;   // в тесте переходы остаются плавными
            _instantSaved = false;
        }
    }

    // ───────── тест: быстрые переходы и автосмена ─────────
    void SetTest(bool on)
    {
        if (mood == null || on == TestFast) return;
        TestFast = on;
        TestSeconds = Mathf.Max(0.5f, testSeconds);
        if (on)
        {
            _savedWT = mood.weatherTransition; _savedST = mood.styleTransition; _testSaved = true;
            if (!_instantSaved) { _savedInstant = mood.instantSwitch; }
            mood.weatherTransition = TestSeconds;
            mood.styleTransition = TestSeconds;
            mood.instantSwitch = false;
            Debug.Log("[Меню P] тест: переходы за " + TestSeconds + " с");
        }
        else
        {
            if (_testSaved) { mood.weatherTransition = _savedWT; mood.styleTransition = _savedST; _testSaved = false; }
            _testAuto = false;
            Debug.Log("[Меню P] тест выключен");
        }
    }

    void TestTick()
    {
        if (!TestFast || !_testAuto || mood == null || mood.weathers.Count < 2) return;
        _testTimer -= Time.unscaledDeltaTime;
        if (_testTimer > 0f) return;
        _testTimer = Mathf.Max(testCycleSeconds, TestSeconds + 1f);
        mood.SetWeather((mood.weatherIndex + 1) % mood.weathers.Count, TestSeconds);
    }

    void OnDisable() { if (TestFast) SetTest(false); }

    // ==================================================================== GUI
    // ОДНА ПАНЕЛЬ (v3): вкладки «Погода · Стиль · Время», список с прокруткой, внизу — что сейчас.
    // Клавиши при открытом меню: 1–4 — вкладки, колесо мыши — прокрутка.
    static readonly string[] TabNames = { "ПОГОДА", "СТИЛЬ", "ВРЕМЯ", "ГОРОД" };
    private int _tab;
    private Vector2 _scroll;

    void OnGUI()
    {
        if (_slide <= 0.001f) return;
        if (mood == null)
        {
            GUI.Label(new Rect(20, 20, 400, 24), "Mood Director не найден");
            return;
        }

        EnsureStyles();

        if (Event.current.type == EventType.KeyDown)
        {
            int k = Event.current.keyCode == KeyCode.Alpha1 ? 0 : Event.current.keyCode == KeyCode.Alpha2 ? 1 : Event.current.keyCode == KeyCode.Alpha3 ? 2 : Event.current.keyCode == KeyCode.Alpha4 ? 3 : -1;
            // цифра — по порядку видимых вкладок (без «Стиля»: 1 погода, 2 время, 3 город)
            int nth = -1;
            for (int t = 0; t < 4 && k >= 0; t++) if (TabVisible(t)) { nth++; if (nth == k) { _tab = t; _scroll = Vector2.zero; Event.current.Use(); break; } }
        }
        if (!TabVisible(_tab)) _tab = 0;

        float ease = 1f - Mathf.Pow(1f - _slide, 3f);
        float w = panelWidth;
        float x = margin.x - (1f - ease) * (w + margin.x);
        float y = margin.y;
        float h = Mathf.Min(Screen.height - margin.y * 2f, maxPanelHeight);

        var prev = GUI.color;
        GUI.color = new Color(1, 1, 1, ease);

        Rect panel = new Rect(x, y, w, h);
        GUI.DrawTexture(new Rect(panel.x - 3, panel.y - 3, panel.width + 6, panel.height + 6), _panelTex);
        GUI.DrawTexture(panel, _panelTex);
        GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 3), _accentTex);

        // ---- вкладки ----
        int vis = 0;
        for (int t = 0; t < 4; t++) if (TabVisible(t)) vis++;
        float tw = (w - 16f - (vis - 1) * 4f) / Mathf.Max(1, vis);
        float tx = x + 8f;
        int num = 0;
        for (int t = 0; t < 4; t++)
        {
            if (!TabVisible(t)) continue;
            num++;
            if (Row(new Rect(tx, y + 10, tw, 28), num + " " + (t == 3 ? CityTabName : TabNames[t]), _tab == t)) { _tab = t; _scroll = Vector2.zero; }
            tx += tw + 4f;
        }

        // ---- список ----
        float footH = 50f;
        Rect view = new Rect(x + 6, y + 46, w - 12, h - 46 - footH);
        float rowW = view.width - 18f;
        float contentH = _tab == 0 ? ContentWeather(rowW, false) : _tab == 1 ? ContentStyle(rowW, false) : _tab == 2 ? ContentTime(rowW, false) : CityTab(rowW, false);
        _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, rowW, contentH), false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        if (_tab == 0) ContentWeather(rowW, true);
        else if (_tab == 1) ContentStyle(rowW, true);
        else if (_tab == 2) ContentTime(rowW, true);
        else CityTab(rowW, true);
        GUI.EndScrollView();

        // ---- низ: что сейчас ----
        float fy = y + h - footH + 4;
        GUI.DrawTexture(new Rect(x + 8, fy - 4, w - 16, 1), _rowTex);
        string wName = Title(mood.weathers, mood.weatherIndex);
        string sName = Title(mood.styles, mood.styleIndex);       // стиль виден внизу, даже когда его вкладки нет
        string time = dayCycle != null ? dayCycle.TimeString : "—";
        GUI.Label(new Rect(x + 12, fy, w - 20, 20), wName + "  ·  " + sName + "  ·  " + time + (TestFast ? "  ·  ТЕСТ" : ""), _row);
        GUI.Label(new Rect(x + 12, fy + 20, w - 20, 18), toggleKey + " — закрыть   цифры — вкладки", _hint);

        GUI.color = prev;
    }

    bool TabVisible(int t)
    {
        if (t == 1) return showStyles && !HideStyles && mood != null && mood.styles.Count > 0;
        if (t == 2) return showTime && dayCycle != null;
        if (t == 3) return CityTab != null;
        return true;
    }

    static string Title<T>(System.Collections.Generic.List<T> list, int i) where T : Object
    {
        if (list == null || i < 0 || i >= list.Count || list[i] == null) return "—";
        var f = list[i].GetType().GetField("title");
        object v = f != null ? f.GetValue(list[i]) : null;
        return v != null ? v.ToString() : list[i].name;
    }

    float Transition() { return TestFast ? TestSeconds : (transitionSeconds > 0.01f ? transitionSeconds : -1f); }

    float ContentWeather(float rw, bool draw)
    {
        int n = mood.weathers.Count;
        if (!draw) return (n + 2) * rowHeight + 12;
        float ry = 0;
        float half = (rw - 4f) * 0.5f;
        // тест: всё перетекает за testSeconds; автосмена — сама листает погоды
        if (Row(new Rect(0, ry, half, rowHeight - 3), (TestFast ? "☑" : "☐") + " Тест: за " + testSeconds.ToString("0") + " с", TestFast))
            SetTest(!TestFast);
        if (Row(new Rect(half + 4f, ry, half, rowHeight - 3), (_testAuto ? "☑" : "☐") + " Автосмена " + testCycleSeconds.ToString("0") + " с", _testAuto))
        {
            if (!TestFast) SetTest(true);
            _testAuto = !_testAuto; _testTimer = 0.5f;
        }
        ry += rowHeight + 8;
        for (int i = 0; i < n; i++)
        {
            var wp = mood.weathers[i];
            string label = wp != null ? $"{i:00}   {wp.title}" : $"{i:00}   (пусто)";
            string note = WeatherNote != null ? WeatherNote(i) : null;
            if (!string.IsNullOrEmpty(note)) label += "   · " + note;
            if (Row(new Rect(0, ry, rw, rowHeight - 3), label, i == mood.weatherIndex)) mood.SetWeather(i, Transition());
            ry += rowHeight;
        }
        return ry;
    }

    float ContentStyle(float rw, bool draw)
    {
        int n = mood.styles.Count;
        if (!draw) return n * rowHeight + 4;
        float ry = 0;
        for (int i = 0; i < n; i++)
        {
            var st = mood.styles[i];
            string label = st != null ? $"{i:00}   {st.title}" : $"{i:00}   (пусто)";
            if (Row(new Rect(0, ry, rw, rowHeight - 3), label, i == mood.styleIndex)) mood.SetStyle(i, Transition());
            ry += rowHeight;
        }
        return ry;
    }

    // ───────── время суток и варианты (предпросмотр) ─────────
    static readonly string[] TimeNames = { "Рассвет", "Утро", "Полдень", "День", "Вечер", "Закат", "Сумерки", "Ночь" };

    float ContentTime(float rw, bool draw)
    {
        var d = dayCycle;
        int dk = d.duskVariants != null ? d.duskVariants.Length : 0, nt = d.nightVariants != null ? d.nightVariants.Length : 0;
        float half = (rw - 4f) * 0.5f;
        float total = 22 + 4 * rowHeight + 22 + 2 * rowHeight + (dk > 0 ? 22 + dk * rowHeight : 0) + (nt > 0 ? 22 + nt * rowHeight : 0) + rowHeight + 8;
        if (!draw) return total;

        float[] hours = { d.sunrise + 0.3f, d.sunrise + 2f, 13f, 15.5f, d.sunset - 2.5f, d.sunset - 0.4f, d.sunset + 0.6f, 23.5f };
        bool manual = d.source == CatCooksDayCycle.TimeSource.Manual;
        float ry = 0;
        Section(ref ry, "ЧАС");
        for (int i = 0; i < hours.Length; i++)
        {
            float hh = Mathf.Repeat(hours[i], 24f);
            bool act = manual && d.manualHoursPerSecond == 0f && Mathf.Abs(Mathf.DeltaAngle(d.Hour * 15f, hh * 15f)) < 3f;
            Rect r = new Rect((i % 2) * (half + 4f), ry + (i / 2) * rowHeight, half, rowHeight - 3);
            if (Row(r, $"{TimeNames[i]}  {Mathf.FloorToInt(hh):00}:{Mathf.FloorToInt((hh % 1f) * 60f):00}", act))
            { d.manualHoursPerSecond = 0f; d.SetManualHour(hh); }
        }
        ry += 4 * rowHeight;

        Section(ref ry, "ХОД ВРЕМЕНИ");
        if (Row(new Rect(0, ry, half, rowHeight - 3), "▶ Бежит (1 ч / 2 с)", manual && d.manualHoursPerSecond > 0f))
        { d.SetManualHour(d.Hour); d.manualHoursPerSecond = 0.5f; }
        if (Row(new Rect(half + 4f, ry, half, rowHeight - 3), "❚❚ Стоп", manual && d.manualHoursPerSecond == 0f))
        { d.SetManualHour(d.Hour); d.manualHoursPerSecond = 0f; }
        ry += rowHeight;
        if (Row(new Rect(0, ry, rw, rowHeight - 3), "Время игры (GameClock)", !manual))
        { d.manualHoursPerSecond = 0f; d.source = CatCooksDayCycle.TimeSource.GameClock; }
        ry += rowHeight;

        if (dk > 0)
        {
            Section(ref ry, "ЗАКАТ / РАССВЕТ  (повторный клик — снять)");
            for (int i = 0; i < dk; i++)
            {
                var p = d.duskVariants[i];
                if (Row(new Rect(0, ry, rw, rowHeight - 3), p != null ? Pretty(p.name) : "(пусто)", d.forceDusk == i)) d.forceDusk = d.forceDusk == i ? -1 : i;
                ry += rowHeight;
            }
        }
        if (nt > 0)
        {
            Section(ref ry, "НОЧЬ");
            for (int i = 0; i < nt; i++)
            {
                var p = d.nightVariants[i];
                if (Row(new Rect(0, ry, rw, rowHeight - 3), p != null ? Pretty(p.name) : "(пусто)", d.forceNight == i)) d.forceNight = d.forceNight == i ? -1 : i;
                ry += rowHeight;
            }
        }
        if (Row(new Rect(0, ry + 4, rw, rowHeight - 3), "Варианты по дню (как в игре)", d.forceDusk < 0 && d.forceNight < 0))
        { d.forceDusk = -1; d.forceNight = -1; d.forceDawn = -1; }
        return total;
    }

    void Section(ref float ry, string title)
    {
        GUI.Label(new Rect(4, ry + 2, 400, 18), title, _title);
        ry += 22;
    }

    static string Pretty(string n)
    {
        n = n.Replace("_", " ");
        if (n.StartsWith("Day ")) n = n.Substring(4);
        return n;
    }

    void DrawPanel(Rect r, string title)
    {
        GUI.DrawTexture(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), _panelTex);
        GUI.DrawTexture(r, _panelTex);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 3), _accentTex);
        GUI.Label(new Rect(r.x + 12, r.y + 8, r.width - 20, 24), title, _title);
    }

    bool Row(Rect r, string label, bool active)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        GUI.DrawTexture(r, active ? _accentTex : (hover ? _rowHoverTex : _rowTex));

        // Активный пункт помечается полосой слева, а не только цветом:
        // на цветном фоне заливка читается хуже, чем силуэт.
        if (active) GUI.DrawTexture(new Rect(r.x, r.y, 3, r.height), _panelTex);

        GUI.Label(new Rect(r.x + 12, r.y + 4, r.width - 16, r.height),
                  label, active ? _rowActive : _row);

        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    // =============================================================== стили GUI
    void EnsureStyles()
    {
        if (_stylesReady && _panelTex != null) return;

        _panelTex = Solid(new Color(panelColor.r, panelColor.g, panelColor.b, backgroundAlpha));
        _rowTex = Solid(new Color(1, 1, 1, 0.05f));
        _rowHoverTex = Solid(new Color(accent.r, accent.g, accent.b, 0.22f));
        _accentTex = Solid(new Color(accent.r, accent.g, accent.b, 0.92f));

        _title = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _title.normal.textColor = new Color(accent.r, accent.g, accent.b, 0.95f);

        _row = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        _row.normal.textColor = textColor;

        _rowActive = new GUIStyle(_row) { fontStyle = FontStyle.Bold };
        _rowActive.normal.textColor = new Color(0.07f, 0.08f, 0.11f);

        _hint = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        _hint.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.45f);

        _stylesReady = true;
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnDestroy()
    {
        // Текстуры созданы кодом и ассетами не являются — Unity их
        // сама не уберёт, и за сессию накопится утечка.
        foreach (var t in new[] { _panelTex, _rowTex, _rowHoverTex, _accentTex })
            if (t != null) { if (Application.isPlaying) Destroy(t); else DestroyImmediate(t); }
    }
}
