using UnityEngine;

// ============================================================================
//  CatCooksCloudShadows v3 — тени от облаков БЕЗ cookie.
//
//  ПОЧЕМУ ПЕРЕПИСАНО. Старая версия отдавала маску в Light.cookie
//  направленного света. Это путь с оговорками: RenderTexture там
//  принимается не во всех версиях URP, Texture2D требует точных настроек
//  импорта, а когда что-то не сходится, тени просто НЕ ПОЯВЛЯЮТСЯ —
//  молча, без единой ошибки в консоли. Отладка сводится к перебору,
//  что мы и делали три подхода подряд.
//
//  ТЕПЕРЬ: полноэкранный проход берёт мировую позицию из буфера глубины
//  и умножает цвет на маску облаков по мировым XZ. Зависит только от
//  Depth Texture, которая и так нужна туману, воде и мягким краям карт.
//
//  ПОБОЧНЫЕ ВЫГОДЫ, которых у cookie не было:
//   • Тень идеально ложится на любой рельеф — считается для той точки,
//     что реально видна в пикселе, а не проецируется плоскостью.
//   • Не растягивается в полосу на закате: не зависит от угла солнца.
//   • Размер пятна задаётся прямо в метрах, а не в долях тайла.
//
//  УСТАНОВКА: нужна Full Screen Pass Renderer Feature с материалом
//  Hidden/CatCooks/Cloud Shadow Pass. Сборщик сцены добавляет её сам.
//
//  Сеть: косметика, НЕ NetworkObject.
// ============================================================================
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Atmosphere/Cloud Shadows")]
public class CatCooksCloudShadows : MonoBehaviour
{
    [Header("Размер")]
    [Tooltip("Размер пятна тени В МЕТРАХ. Для эффекта «идёшь, и тебя " +
             "накрывает» нужно 300–700: под таким пятном идёшь несколько " +
             "секунд. Меньше 150 — рябь под ногами, читается как грязь " +
             "на земле, а не как облако.")]
    public float patchMeters = 480f;

    [Header("Плотность")]
    [Tooltip("Сколько земли накрыто тенью. Ведёт погодный пресет.")]
    [Range(0f, 1f)] public float coverage = 0.45f;

    [Tooltip("Насколько темнеет земля под облаком. Для заметного эффекта " +
             "нужно 0.45–0.7; на 0.2 это уже не читается.")]
    [Range(0f, 1f)] public float strength = 0.6f;

    [Tooltip("Минимум даже в ясную погоду. Совсем без теней поле выглядит " +
             "стерильно освещённым.")]
    [Range(0f, 1f)] public float minStrength = 0.22f;

    [Tooltip("РАСТЯЖКА ДИАПАЗОНА МАСКИ. Главная ручка «пятна вместо ровной " +
             "полутени»: произведение двух нормированных шумов живёт около " +
             "середины, порог режет его почти поровну, и вся земля " +
             "оказывается в одинаковой тени. 1.6–2.2 разводит светлое " +
             "и тёмное по краям диапазона.")]
    [Range(0.5f, 3f)] public float contrast = 1.8f;

    [Tooltip("Цвет тени. Холодный синеватый читается естественнее серого: " +
             "в тени поверхность освещена небом, а небо голубое. " +
             "Серая тень выглядит грязью на земле.")]
    public Color shadowColor = new Color(0.42f, 0.48f, 0.64f);

    [Header("Граница")]
    [Tooltip("Резкость ядра. Малое значение = именно НАКРЫВАЕТ, " +
             "а не освещение слегка проседает.")]
    [Range(0.01f, 0.4f)] public float coreEdge = 0.07f;

    [Tooltip("Ширина полутени вокруг ядра")]
    [Range(0.05f, 1f)] public float penumbra = 0.35f;

    [Header("Дальность")]
    [Tooltip("Дальше этого тени гаснут.\n\n" +
             "ЭТО САМАЯ ЧАСТАЯ ПРИЧИНА «ТЕНЕЙ НЕТ». Значение должно " +
             "покрывать всю территорию: если карта 1000×1000 м, а здесь " +
             "стоит 600, то при взгляде сверху или с горы вся земля " +
             "окажется за порогом и затемнения не будет нигде. " +
             "Кнопка «Подогнать под террейн» выставит его сама.")]
    public float maxDistance = 2500f;

    [Tooltip("НА СКОЛЬКО МЕТРОВ ВЫШЕ ЗЕМЛИ тени ещё есть.\n\n" +
             "Раньше это была абсолютная высота, и в сцене, которая живёт " +
             "на Y=160, теней не было вовсе: всё оказывалось «выше порога». " +
             "Теперь порог отсчитывается от уровня земли.")]
    public float heightFadeAboveGround = 400f;

    [Tooltip("Уровень земли. Подбирается лучом при старте.")]
    public float groundLevel = 0f;

    public bool autoGroundLevel = true;
    public LayerMask groundMask = ~0;

    [Header("Отладка")]
    [Tooltip("Залить тени чёрным на полную густоту — чтобы просто увидеть, " +
             "есть они вообще или нет. Для настройки, не для игры.")]
    public bool debugSolid = false;

    [Tooltip("Отключить затухание по дальности и высоте. Если с этой " +
             "галкой тени появились — значит дело было в порогах, " +
             "а не в самой системе.")]
    public bool debugIgnoreFades = false;

    [Header("Зона (v2)")]
    [Tooltip("Тени только над игровой зоной: центр. Пусто — по всей карте (как раньше). " +
             "В городе ставит сам CityAtmosphere (центр уровня).")]
    public Transform zoneCenter;
    [Tooltip("Или центр числом (мир XZ), если Zone Center пуст и Zone Radius > 0")]
    public Vector2 zoneCenterXZ;
    [Tooltip("Радиус зоны в единицах сцены. 0 — без ограничения")]
    public float zoneRadius = 0f;
    [Tooltip("Мягкость края зоны")]
    public float zoneFeather = 150f;

    [Header("Движение")]
    public bool useGlobalWind = true;
    public float speed = 7f;
    [Range(0f, 360f)] public float directionDegrees = 45f;

    [Tooltip("Во сколько раз медленнее ползёт второй слой. Разница " +
             "скоростей заставляет пятна перестраиваться, а не просто ехать.")]
    [Range(0.05f, 1f)] public float secondLayerSpeed = 0.35f;

    // ───────── внутреннее ─────────
    private Vector2 _offsetA, _offsetB;
    private double _lastEditorTime;

    static readonly int ParamsId = Shader.PropertyToID("_CatCS_Params");
    static readonly int OffsetId = Shader.PropertyToID("_CatCS_Offset");
    static readonly int ColorId = Shader.PropertyToID("_CatCS_Color");
    static readonly int PenumbraId = Shader.PropertyToID("_CatCS_Penumbra");
    static readonly int HeightId = Shader.PropertyToID("_CatCS_HeightFade");
    static readonly int MaxDistId = Shader.PropertyToID("_CatCS_MaxDistance");
    static readonly int ContrastId = Shader.PropertyToID("_CatCS_Contrast");
    static readonly int ZoneId = Shader.PropertyToID("_CatCS_Zone");
    static readonly int WindDirId = Shader.PropertyToID("_CatWindDir");
    static readonly int WindStrId = Shader.PropertyToID("_CatWindStrength");

    void OnEnable()
    {
        Push();
#if UNITY_EDITOR
        _lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        // Гасим за собой: выключенный компонент не должен оставлять
        // сцену затенённой — это читалось бы как баг шейдера.
        Shader.SetGlobalVector(ParamsId, new Vector4(patchMeters, coverage, coreEdge, 0f));
    }

    void OnValidate() => Push();

    void LateUpdate() { if (Application.isPlaying) Tick(Time.deltaTime); }

#if UNITY_EDITOR
    void EditorTick()
    {
        if (Application.isPlaying || this == null || !isActiveAndEnabled) return;
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastEditorTime);
        _lastEditorTime = now;
        if (dt <= 0f || dt > 0.5f) return;
        Tick(dt);
        UnityEditor.SceneView.RepaintAll();
    }
#endif

    void Tick(float dt)
    {
        Vector2 dir;
        float windMul = 1f;

        if (useGlobalWind)
        {
            Vector4 w = Shader.GetGlobalVector(WindDirId);
            dir = new Vector2(w.x, w.z);
            if (dir.sqrMagnitude < 0.0001f) dir = new Vector2(0.7071f, 0.7071f);
            dir.Normalize();
            windMul = Mathf.Max(0.15f, Shader.GetGlobalFloat(WindStrId));
        }
        else
        {
            float rad = directionDegrees * Mathf.Deg2Rad;
            dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        // Смещение в тех же единицах, что и маска: делим на размер пятна.
        float perSec = speed * windMul / Mathf.Max(patchMeters, 1f);
        _offsetA += dir * (perSec * dt);
        _offsetB += new Vector2(-dir.y, dir.x).normalized * (perSec * secondLayerSpeed * dt)
                  + dir * (perSec * secondLayerSpeed * 0.5f * dt);

        Push();
    }

    void Push()
    {
        if (autoGroundLevel && Application.isPlaying) AutoGround();

        float str = debugSolid ? 1f : Mathf.Max(strength, minStrength);
        Shader.SetGlobalVector(ParamsId,
            new Vector4(patchMeters, coverage, coreEdge, str));
        Shader.SetGlobalVector(OffsetId,
            new Vector4(_offsetA.x, _offsetA.y, _offsetB.x, _offsetB.y));
        Shader.SetGlobalColor(ColorId, debugSolid ? Color.black : shadowColor);
        Shader.SetGlobalFloat(PenumbraId, penumbra);
        Shader.SetGlobalFloat(ContrastId, contrast);
        Vector2 zc = zoneCenter != null ? new Vector2(zoneCenter.position.x, zoneCenter.position.z) : zoneCenterXZ;
        Shader.SetGlobalVector(ZoneId, new Vector4(zc.x, zc.y, Mathf.Max(0f, zoneRadius), Mathf.Max(1f, zoneFeather)));
        // В отладке пороги отодвигаются далеко за пределы любой сцены:
        // так сразу видно, система молчит или её просто обрезают.
        Shader.SetGlobalFloat(HeightId, debugIgnoreFades
            ? 100000f : groundLevel + heightFadeAboveGround);
        Shader.SetGlobalFloat(MaxDistId, debugIgnoreFades
            ? 100000f : Mathf.Max(maxDistance, 10f));
    }

    void AutoGround()
    {
        Camera cam = Camera.main;
        Vector3 from = cam != null ? cam.transform.position : transform.position;
        if (Physics.Raycast(from + Vector3.up * 2f, Vector3.down,
                            out RaycastHit hit, 800f, groundMask,
                            QueryTriggerInteraction.Ignore))
        {
            groundLevel = hit.point.y;
            autoGroundLevel = false;
        }
        else groundLevel = from.y - 2f;
    }

    [ContextMenu("Тест: чёрные тени")]
    void TestSolid() { debugSolid = true; debugIgnoreFades = true; Push(); }

    [ContextMenu("Тест: выключить отладку")]
    void TestOff() { debugSolid = false; debugIgnoreFades = false; Push(); }

    /// <summary>Дёргать из погодных пресетов: 0 = ясно, 1 = сплошная облачность.</summary>
    public void SetWeather(float coverage01, float strength01)
    {
        coverage = Mathf.Clamp01(coverage01);
        strength = Mathf.Max(Mathf.Clamp01(strength01), minStrength);
        Push();
    }

#if UNITY_EDITOR
    [ContextMenu("Проверить настройку")]
    void CheckSetup()
    {
        var sb = new System.Text.StringBuilder("[CloudShadows] Проверка\n");
        sb.AppendLine($"  пятно {patchMeters:0} м, густота {Mathf.Max(strength, minStrength):0.00}");

        var urp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline
                  as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp != null && !urp.supportsCameraDepthTexture)
            sb.AppendLine("  ОШИБКА: Depth Texture выключен в URP Asset — проход " +
                          "не сможет узнать, где земля, и теней не будет.");

        if (patchMeters < 150f)
            sb.AppendLine("  ВНИМАНИЕ: пятно мельче 150 м — это рябь под ногами, " +
                          "а не накрывающая тень.");

        sb.AppendLine("  Нужна фича Full Screen Pass с материалом " +
                      "Hidden/CatCooks/Cloud Shadow Pass. Нет — запусти " +
                      "CatCooks → Атмосфера → Собрать сцену.");
        Debug.Log(sb.ToString(), this);
    }
#endif
}
