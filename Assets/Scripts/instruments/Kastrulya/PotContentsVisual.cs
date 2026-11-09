using UnityEngine;

/// <summary>
/// ЖИДКОСТЬ ВНУТРИ ПОСУДЫ. Вешается на кастрюлю рядом с CookingVessel.
///
/// Диск, который поднимается по мере заполнения и красится в цвет содержимого.
/// Меш собирается из кода, моделировать ничего не надо.
///
/// ЦВЕТ БЕРЁТСЯ ИЗ СОДЕРЖИМОГО: вода голубая, борщ бордовый — цвет смешивается
/// из DebrisColor всех фракций по их массе. Отдельно задавать ничего не нужно,
/// а новый продукт подкрасит суп сам.
///
/// Чисто локальная штука: уровень и состав уже синхронизированы в CookingVessel.
/// </summary>
[RequireComponent(typeof(CookingVessel))]
public class PotContentsVisual : MonoBehaviour
{
    [Header("Геометрия")]
    [Tooltip("Радиус поверхности жидкости, метры. Чуть меньше внутреннего " +
             "радиуса кастрюли, иначе будет торчать сквозь стенки.")]
    [SerializeField] private float _radius = 0.11f;

    [Tooltip("Высота дна от центра объекта, метры")]
    [SerializeField] private float _bottom = 0.02f;

    [Tooltip("Высота полной кастрюли от центра объекта, метры")]
    [SerializeField] private float _top = 0.2f;

    [Tooltip("Насколько плавно поднимается уровень")]
    [SerializeField] private float _smooth = 6f;

    [Header("Вид")]
    [Tooltip("Цвет, если содержимое ничего своего не задаёт")]
    [SerializeField] private Color _defaultColor = new Color(0.45f, 0.72f, 0.95f, 0.9f);

    [Tooltip("Прозрачность поверхности")]
    [Range(0.2f, 1f)][SerializeField] private float _alpha = 0.92f;

    [Tooltip("Рябь при кипении, метры")]
    [SerializeField] private float _boilRipple = 0.006f;

    private CookingVessel _vessel;
    private PotThermal _thermal;

    private Transform _surface;
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _mpb;
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorIdLegacy = Shader.PropertyToID("_Color");

    private float _shownFill;

    private void Awake()
    {
        _vessel = GetComponent<CookingVessel>();
        _thermal = GetComponent<PotThermal>();
        Build();
    }

    /// <summary>Доля заполнения посуды, 0..1.</summary>
    private float Fill01
    {
        get
        {
            if (_vessel == null || _vessel.Def == null || _vessel.Def.CapacityG <= 0) return 0f;
            return Mathf.Clamp01(
                (float)(_vessel.Def.CapacityG - _vessel.FreeSpaceG) / _vessel.Def.CapacityG);
        }
    }

    private void Build()
    {
        GameObject go = new GameObject("Contents (auto)");

        // ДОЧЕРНИЙ объект: поверхность должна ездить вместе с кастрюлей,
        // когда её несут или наклоняют.
        go.transform.SetParent(transform, false);
        _surface = go.transform;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildDisc(_radius, 24);

        _renderer = go.AddComponent<MeshRenderer>();
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.sharedMaterial = BuildMaterial();

        _mpb = new MaterialPropertyBlock();
        go.SetActive(false);

        if (_renderer.sharedMaterial == null)
            Debug.LogError($"[Вода] на {name} не нашёлся шейдер для поверхности — " +
                           "жидкость будет невидимой", this);
    }

    private void LateUpdate()
    {
        if (_surface == null) return;

        float target = Fill01;

        // Плавно: при наливе струёй уровень иначе дёргался бы ступеньками
        // по каждому тику налива.
        _shownFill = Mathf.Lerp(_shownFill, target,
                                1f - Mathf.Exp(-_smooth * Time.deltaTime));

        bool show = _shownFill > 0.002f;
        if (_surface.gameObject.activeSelf != show) _surface.gameObject.SetActive(show);
        if (!show) return;

        float y = Mathf.Lerp(_bottom, _top, _shownFill);

        // Рябь при кипении — поверхность ходит ходуном.
        if (_thermal != null && _boilRipple > 0.0001f)
        {
            PotState st = _thermal.State;
            if (st == PotState.Boiling || st == PotState.Overheat)
            {
                float amp = st == PotState.Overheat ? 2.5f : 1f;
                y += Mathf.Sin(Time.time * 9f) * _boilRipple * amp;
            }
        }

        _surface.localPosition = new Vector3(0f, y, 0f);
        _surface.localRotation = Quaternion.identity;

        ApplyColor();
    }

    /// <summary>
    /// Цвет смешивается из фракций по их массе. Через MaterialPropertyBlock,
    /// чтобы не плодить копию материала на каждую кастрюлю.
    /// </summary>
    private void ApplyColor()
    {
        Color c = _defaultColor;

        var view = _vessel.View;
        if (view != null && view.Count > 0)
        {
            Color sum = Color.black;
            float mass = 0f;
            FoodRegistry reg = FoodRegistry.Instance;

            for (int i = 0; i < view.Count; i++)
            {
                FoodPortionView v = view[i];
                IngredientDef def = reg != null ? reg.GetIngredient(v.IngredientId) : null;

                Color ic = def != null && def.DebrisColor.a > 0.01f
                    ? def.DebrisColor : _defaultColor;

                sum += ic * v.MassG;
                mass += v.MassG;
            }

            if (mass > 0f) c = sum / mass;
        }

        c.a = _alpha;

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(ColorId, c);
        _mpb.SetColor(ColorIdLegacy, c);
        _renderer.SetPropertyBlock(_mpb);
    }

    /// <summary>
    /// Круг из треугольного веера, ДВУСТОРОННИЙ.
    ///
    /// Стороны дублируются намеренно. Обычный веер по кругу даёт лицевую
    /// сторону ВНИЗ: при взгляде сверху вершины идут против часовой, а Unity
    /// считает лицевой обход по часовой — и поверхность воды становится
    /// невидимой ровно с того ракурса, с которого на неё и смотрят.
    ///
    /// Двусторонний меш решает это раз и навсегда, независимо от того, какой
    /// шейдер подхватится и стоит ли у него отсечение граней.
    /// </summary>
    private static Mesh BuildDisc(float radius, int segments)
    {
        Mesh m = new Mesh { name = "LiquidSurface" };

        // Два набора вершин: для верхней стороны и для нижней. Отдельные,
        // потому что нормали у них противоположные.
        int per = segments + 1;
        Vector3[] verts = new Vector3[per * 2];
        Vector2[] uv = new Vector2[per * 2];

        verts[0] = Vector3.zero;
        uv[0] = new Vector2(0.5f, 0.5f);
        verts[per] = Vector3.zero;
        uv[per] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            float cx = Mathf.Cos(a), cz = Mathf.Sin(a);

            Vector3 v = new Vector3(cx * radius, 0f, cz * radius);
            Vector2 t = new Vector2(cx * 0.5f + 0.5f, cz * 0.5f + 0.5f);

            verts[i + 1] = v;
            uv[i + 1] = t;
            verts[per + i + 1] = v;
            uv[per + i + 1] = t;
        }

        int[] tris = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            int a = i + 1;
            int b = (i + 1) % segments + 1;

            // Вверх
            tris[i * 6] = 0;
            tris[i * 6 + 1] = b;
            tris[i * 6 + 2] = a;

            // Вниз — обратный обход
            tris[i * 6 + 3] = per;
            tris[i * 6 + 4] = per + a;
            tris[i * 6 + 5] = per + b;
        }

        m.vertices = verts;
        m.uv = uv;
        m.triangles = tris;

        // Нормали ставим вручную: RecalculateNormals усреднил бы их по общим
        // вершинам и обе стороны вышли бы боковыми.
        Vector3[] normals = new Vector3[verts.Length];
        for (int i = 0; i < per; i++) normals[i] = Vector3.up;
        for (int i = per; i < normals.Length; i++) normals[i] = Vector3.down;
        m.normals = normals;

        m.RecalculateBounds();
        return m;
    }

    private static Material BuildMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                 ?? Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Standard");

        if (sh == null) return null;

        Material m = new Material(sh) { name = "PotContents (auto)" };

        // Полупрозрачность: сквозь бульон должно быть видно дно.
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_SrcBlend"))
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend"))
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);

        // Отсечение граней выключаем — вода видна и сверху, и снизу.
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);

        // В URP одних свойств мало: без этих ключевых слов шейдер продолжает
        // рисоваться как непрозрачный, и вода выглядит сплошной заглушкой.
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.SetOverrideTag("RenderType", "Transparent");

        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }
}
