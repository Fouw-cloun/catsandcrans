using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ГРАДУСНИК НАД ПОСУДОЙ. Вешается на кастрюлю рядом с PotThermal.
///
/// Собирается ИЗ КОДА: холст, три картинки и подпись создаются в рантайме.
/// Возиться с Canvas в префабе не нужно, а добавить градусник новой посуде —
/// это перетащить один компонент.
///
/// Показывается только когда есть что показывать: холодная целая кастрюля
/// градусник не рисует и не мозолит глаза.
///
/// ЧИСТО ЛОКАЛЬНАЯ ШТУКА: значения берутся из уже синхронизированных полей
/// PotThermal, по сети ничего не гоняется.
/// </summary>
public class WorldThermometer : MonoBehaviour
{
    [Header("Спрайты")]
    [SerializeField] private Sprite _back;
    [SerializeField] private Sprite _frame;
    [SerializeField] private Sprite _fill;

    [Header("Размещение")]
    [Tooltip("На сколько поднять над центром кастрюли, метры")]
    [SerializeField] private float _height = 0.42f;

    [Tooltip("Высота градусника в мире, метры")]
    [SerializeField] private float _size = 0.26f;

    [Tooltip("Дальше этого не показывать, метры")]
    [SerializeField] private float _maxDistance = 12f;

    [Header("Цвета шкалы")]
    [SerializeField] private Color _coldColor = new Color(0.45f, 0.75f, 1f);
    [SerializeField] private Color _hotColor = new Color(1f, 0.65f, 0.15f);
    [SerializeField] private Color _dangerColor = new Color(1f, 0.25f, 0.15f);

    [Header("Когда показывать")]
    [Tooltip("Ниже этой доли шкалы градусник скрыт — холодную посуду " +
             "разглядывать незачем. При 0.02 появляется примерно на 28 градусах.")]
    [Range(0f, 0.5f)][SerializeField] private float _showAbove = 0.02f;

    [Header("ОТЛАДКА")]
    [Tooltip("Показывать ВСЕГДА, даже на холодной. Включи, если градусника " +
             "не видно — так сразу понятно, дело в показе или в самом UI.")]
    [SerializeField] private bool _alwaysShow;

    [Tooltip("Писать в консоль, что происходит с градусником")]
    [SerializeField] private bool _debugLog = true;

    private CatCooks.Food.IPotBody _pot;
    private Canvas _canvas;
    private Image _fillImage;
    private Image _frameImage;
    private Text _label;
    private Transform _root;

    private bool _loggedOnce;

    private void Awake()
    {
        _pot = GetComponent<CatCooks.Food.IPotBody>();

        // Без спрайтов Image рисует пустоту — подставляем простые белые,
        // чтобы забытая настройка не выглядела как «градусник не работает».
        if (_back == null || _frame == null || _fill == null)
        {
            Debug.LogWarning($"[Градусник] на {name} не назначены спрайты — " +
                             "рисую заглушки. Проверь, что у png стоит " +
                             "Texture Type = Sprite (2D and UI).", this);
            Sprite fallback = MakeWhiteSprite();
            if (_back == null) _back = fallback;
            if (_frame == null) _frame = fallback;
            if (_fill == null) _fill = fallback;
        }

        Build();
    }

    /// <summary>Белый квадрат на случай, если спрайты не назначены.</summary>
    private static Sprite MakeWhiteSprite()
    {
        Texture2D tex = new Texture2D(4, 4);
        Color[] px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
    }

    private void OnDestroy()
    {
        if (_root != null) Destroy(_root.gameObject);
    }

    /// <summary>Собираем холст в мировом пространстве. Отдельным объектом,
    /// а не дочерним: кастрюля кувыркается, а градусник должен стоять ровно.</summary>
    private void Build()
    {
        GameObject go = new GameObject("Thermometer (auto)");
        _root = go.transform;

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler cs = go.AddComponent<CanvasScaler>();
        cs.dynamicPixelsPerUnit = 300f;

        RectTransform rt = _canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(128f, 256f);
        rt.localScale = Vector3.one * (_size / 256f);

        // Порядок создания = порядок отрисовки: подложка, столбик, рамка сверху.
        MakeImage(rt, "Back", _back, Color.white);
        _fillImage = MakeImage(rt, "Fill", _fill, _coldColor);
        _frameImage = MakeImage(rt, "Frame", _frame, Color.white);

        // Заливка растёт снизу вверх — это и есть столбик градусника.
        _fillImage.type = Image.Type.Filled;
        _fillImage.fillMethod = Image.FillMethod.Vertical;
        _fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
        _fillImage.fillAmount = 0f;

        GameObject lg = new GameObject("Label");
        lg.transform.SetParent(rt, false);
        _label = lg.AddComponent<Text>();
        // Имя встроенного шрифта разное в разных версиях Unity, а без шрифта
        // Text молча не рисует ничего — и подписи просто нет.
        _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                   ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_label.font == null)
            Debug.LogWarning($"[Градусник] на {name} не нашёлся встроенный шрифт — " +
                             "число температуры показано не будет", this);
        _label.fontSize = 34;
        _label.alignment = TextAnchor.MiddleCenter;
        _label.color = Color.white;

        RectTransform lrt = _label.rectTransform;
        lrt.anchorMin = new Vector2(0.5f, 0f);
        lrt.anchorMax = new Vector2(0.5f, 0f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -6f);
        lrt.sizeDelta = new Vector2(180f, 46f);

        go.SetActive(false);
    }

    private Image MakeImage(RectTransform parent, string name, Sprite sprite, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;

        RectTransform rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return img;
    }

    private void LateUpdate()
    {
        if (_root == null || _pot == null) return;

        Camera cam = Camera.main;
        if (cam == null) { _root.gameObject.SetActive(false); return; }

        bool worth = _alwaysShow || _pot.Temp01 > _showAbove || _pot.IsRuptured ||
                     _pot.Condition == CatCooks.Food.PotCondition.Overheat;

        float dist = Vector3.Distance(cam.transform.position, transform.position);
        bool show = worth && dist <= _maxDistance;

        if (_debugLog && !_loggedOnce && _pot.Temp01 > _showAbove)
        {
            _loggedOnce = true;
            Debug.Log($"[Градусник] {name}: {_pot.TemperatureC:0}°, шкала " +
                      $"{_pot.Temp01:0.00}, до камеры {dist:0.0} м " +
                      $"(предел {_maxDistance:0.0}) -> показ {show}", this);
        }

        if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
        if (!show) return;

        // Стоит над кастрюлей и всегда развёрнут к камере.
        _root.position = transform.position + Vector3.up * _height;
        _root.rotation = Quaternion.LookRotation(_root.position - cam.transform.position);

        float t = Mathf.Clamp01(_pot.Temp01);
        _fillImage.fillAmount = t;

        // Цвет: от холодного к горячему, а на перегреве мигает тревожным.
        Color c = Color.Lerp(_coldColor, _hotColor, t);
        if (_pot.Condition == CatCooks.Food.PotCondition.Overheat)
            c = Color.Lerp(c, _dangerColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 12f));
        else if (_pot.IsRuptured)
            c = Color.Lerp(c, Color.gray, 0.6f);

        _fillImage.color = c;

        // Рамка тоже подмигивает — так перегрев заметен боковым зрением.
        _frameImage.color = _pot.Condition == CatCooks.Food.PotCondition.Overheat
            ? Color.Lerp(Color.white, _dangerColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 12f))
            : Color.white;

        _label.text = _pot.IsRuptured
            ? "ПРОБИТА"
            : _pot.Condition == CatCooks.Food.PotCondition.Overheat
                ? $"{_pot.TemperatureC:0}°  !"
                : $"{_pot.TemperatureC:0}°";

        _label.color = _pot.Condition == CatCooks.Food.PotCondition.Overheat ? _dangerColor : Color.white;
    }
}
