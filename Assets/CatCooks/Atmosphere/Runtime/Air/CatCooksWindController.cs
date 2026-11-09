using UnityEngine;

// ============================================================================
//  CatCooksWindController — версия 2. Один ветер на всю сцену.
//
//  Пишет глобальные шейдерные параметры, которые читают все шейдеры,
//  подключившие CatCooksWind.hlsl: крона, ствол, трава, ленты ветра.
//
//  ═══ ГЛАВНОЕ ДОБАВЛЕНИЕ: SampleWind() ═══
//
//  Раньше ветер существовал ТОЛЬКО в шейдерах. Всё, что должно было ему
//  подчиняться на стороне C# — падающие листья, тачка, брезент, залпы мехов —
//  заводило собственное направление и силу. В итоге трава качалась в одну
//  сторону, листья летели в другую, а ощущение единой погоды разваливалось.
//
//  Теперь формула повторена в C# один в один с CatSampleWind из HLSL. Любой
//  скрипт спрашивает ветер в своей точке и получает ТО ЖЕ значение, что видит
//  шейдер рядом.
//
//  Дублирование формулы в двух языках неприятно, но альтернатива хуже:
//  читать обратно из GPU нельзя, а расхождение направлений видно сразу.
//
//  ═══ ПРОФИЛЬ ПО ВЫСОТЕ ═══
//
//  Внизу ветер гасят здания и рельеф, наверху он свободен. Для игры про
//  стройку небоскрёба это основной способ дать почувствовать высоту: ничего
//  не написано на экране, но на сороковом этаже видно, что дует иначе.
//
//  Сеть: чистая косметика, НЕ NetworkObject. Ветер детерминирован по _Time.y и
//  мировым координатам, поэтому у всех игроков совпадает без синхронизации.
// ============================================================================
[ExecuteAlways]
[DisallowMultipleComponent]
public class CatCooksWindController : MonoBehaviour
{
    public enum WindPreset
    {
        Calm,        // штиль, листва еле дышит
        Light,       // лёгкий бриз — дефолт
        Fresh,       // свежий ветер, заметные волны
        Strong,      // сильный, деревья гнутся
        Storm        // шторм
    }

    [Header("Пресет")]
    public WindPreset preset = WindPreset.Light;
    [Tooltip("Применять пресет автоматически. Сними, чтобы крутить ползунки вручную.")]
    public bool usePreset = true;

    [Header("Направление")]
    [Tooltip("Угол по компасу, градусы. 0 = вдоль +Z")]
    [Range(0f, 360f)] public float directionDegrees = 45f;

    [Tooltip("МЕДЛЕННЫЙ ПОВОРОТ ВЕТРА, градусов в секунду.\n\n" +
             "Ветер, дующий строго в одну сторону часами, выдаёт декорацию. " +
             "Даже 1–3 градуса в секунду делают погоду живой, оставаясь " +
             "незаметными в моменте.")]
    [Range(0f, 15f)] public float directionDrift = 1.5f;

    [Header("Параметры (если usePreset выключен)")]
    [Range(0f, 2f)] public float strength = 0.5f;
    [Range(0f, 3f)] public float speed = 1.0f;

    [Header("Порывы — крупный вал")]
    [Tooltip("Размер волн, бегущих по площадке. Меньше = крупнее волны.")]
    [Range(0.002f, 0.15f)] public float gustScale = 0.02f;
    [Range(0f, 4f)] public float gustSpeed = 1.2f;

    [Header("Порывы — мелкая рябь")]
    [Tooltip("ВТОРОЙ СЛОЙ. Идёт под углом к первому и ломает край волны.\n\n" +
             "Один слой всегда читается как повтор: глаз ловит период за " +
             "десяток секунд. Два с несоизмеримыми периодами повтора не дают.")]
    [Range(0.002f, 0.4f)] public float gustScale2 = 0.09f;
    [Range(0f, 6f)] public float gustSpeed2 = 2.6f;
    [Range(0f, 1f)] public float gustMix = 0.55f;

    [Header("Профиль по высоте")]
    [Tooltip("Уровень земли, метры. Обычно 0.")]
    public float groundY = 0f;

    [Tooltip("На какой высоте над землёй ветер выходит на полную силу, метры.\n\n" +
             "Для небоскрёба ставь примерно на уровень середины: тогда нижние " +
             "этажи заметно спокойнее верхних.")]
    [Min(1f)] public float referenceHeight = 60f;

    [Tooltip("МНОЖИТЕЛЬ У САМОЙ ЗЕМЛИ.\n\n" +
             "Ноль не ставь: полностью неподвижная трава у ног при бушующем " +
             "небе выглядит поломкой, а не затишьем. 0.35 — внизу заметно " +
             "тише, но живо.")]
    [Range(0f, 1f)] public float groundMultiplier = 0.35f;

    [Tooltip("Кривизна набора силы. 1 — линейно, 2 — почти весь прирост в " +
             "верхней половине.")]
    [Range(0.2f, 4f)] public float heightPower = 1.4f;

    [Header("Отладка")]
    [Tooltip("Остановить ветер (для снятия скриншотов)")]
    public bool freeze = false;

    [Tooltip("Показать профиль по высоте столбиком в сцене")]
    public bool showHeightGizmo = true;

    static readonly int DirId = Shader.PropertyToID("_CatWindDir");
    static readonly int StrId = Shader.PropertyToID("_CatWindStrength");
    static readonly int SpdId = Shader.PropertyToID("_CatWindSpeed");
    static readonly int GScaleId = Shader.PropertyToID("_CatGustScale");
    static readonly int GSpeedId = Shader.PropertyToID("_CatGustSpeed");

    static readonly int GScale2Id = Shader.PropertyToID("_CatGustScale2");
    static readonly int GSpeed2Id = Shader.PropertyToID("_CatGustSpeed2");
    static readonly int GMixId = Shader.PropertyToID("_CatGustMix");

    static readonly int GroundYId = Shader.PropertyToID("_CatWindGroundY");
    static readonly int RefHId = Shader.PropertyToID("_CatWindRefHeight");
    static readonly int GroundMulId = Shader.PropertyToID("_CatWindGroundMul");
    static readonly int HPowId = Shader.PropertyToID("_CatWindHeightPow");

    /// <summary>Единственный контроллер в сцене. Скриптам удобнее спросить
    /// ветер через него, чем искать компонент каждый раз.</summary>
    public static CatCooksWindController Instance { get; private set; }

    // Текущие действующие значения — после пресета и заморозки.
    float _dirDeg, _strength, _speed, _gustSpeed, _gustSpeed2;
    Vector3 _dir = Vector3.forward;

    void OnEnable() { Instance = this; Apply(); }
    void OnDisable() { if (Instance == this) Instance = null; }
    void Update() => Apply();

#if UNITY_EDITOR
    void OnValidate() => UnityEditor.EditorApplication.delayCall += () => { if (this != null) Apply(); };
#endif

    public void Apply()
    {
        float s = strength, sp = speed, gs = gustSpeed, gs2 = gustSpeed2;

        if (usePreset)
        {
            switch (preset)
            {
                case WindPreset.Calm:   s = 0.12f; sp = 0.5f; gs = 0.4f; gs2 = 0.9f; break;
                case WindPreset.Light:  s = 0.45f; sp = 1.0f; gs = 1.0f; gs2 = 2.2f; break;
                case WindPreset.Fresh:  s = 0.85f; sp = 1.5f; gs = 1.6f; gs2 = 3.2f; break;
                case WindPreset.Strong: s = 1.35f; sp = 2.1f; gs = 2.4f; gs2 = 4.4f; break;
                case WindPreset.Storm:  s = 1.9f;  sp = 2.8f; gs = 3.4f; gs2 = 5.6f; break;
            }
        }

        if (freeze) { sp = 0f; gs = 0f; gs2 = 0f; }

        // Медленный дрейф направления. В редакторе вне игры не крутим, иначе
        // сцена «дышит» при каждом кадре инспектора.
        if (Application.isPlaying && !freeze && directionDrift > 0.001f)
            directionDegrees = Mathf.Repeat(
                directionDegrees + directionDrift * Time.deltaTime * Mathf.Sin(Time.time * 0.07f),
                360f);

        float rad = directionDegrees * Mathf.Deg2Rad;
        _dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;

        _dirDeg = directionDegrees;
        _strength = s;
        _speed = sp;
        _gustSpeed = gs;
        _gustSpeed2 = gs2;

        Shader.SetGlobalVector(DirId, _dir);
        Shader.SetGlobalFloat(StrId, s);
        Shader.SetGlobalFloat(SpdId, sp);
        Shader.SetGlobalFloat(GScaleId, gustScale);
        Shader.SetGlobalFloat(GSpeedId, gs);

        Shader.SetGlobalFloat(GScale2Id, gustScale2);
        Shader.SetGlobalFloat(GSpeed2Id, gs2);
        Shader.SetGlobalFloat(GMixId, gustMix);

        Shader.SetGlobalFloat(GroundYId, groundY);
        Shader.SetGlobalFloat(RefHId, referenceHeight);
        Shader.SetGlobalFloat(GroundMulId, groundMultiplier);
        Shader.SetGlobalFloat(HPowId, heightPower);
    }

    // ───────────────────────── ВЫБОРКА ДЛЯ C# ─────────────────────────

    /// <summary>Множитель силы на данной высоте, 0..1. Та же формула, что в
    /// CatWindHeightMul из HLSL.</summary>
    public float HeightMultiplier(float worldY)
    {
        float t = Mathf.Clamp01((worldY - groundY) / Mathf.Max(0.001f, referenceHeight));
        t = Mathf.Pow(t, Mathf.Max(0.05f, heightPower));
        return Mathf.Lerp(groundMultiplier, 1f, t);
    }

    /// <summary>Множитель порыва в точке, примерно 0.5..1.5. Повторяет
    /// CatGustAt из HLSL, включая второй слой под углом к первому.</summary>
    public float GustAt(Vector3 worldPos)
    {
        Vector2 wdir = new Vector2(_dir.x, _dir.z).normalized;
        Vector2 wdir2 = new Vector2(-wdir.y, wdir.x);
        Vector2 p = new Vector2(worldPos.x, worldPos.z);

        float t = Application.isPlaying ? Time.time : 0f;

        float g1 = Mathf.Sin(Vector2.Dot(p, wdir) * gustScale - t * _gustSpeed);
        float g2 = Mathf.Sin(Vector2.Dot(p, wdir2) * gustScale2 - t * _gustSpeed2);

        float g = Mathf.Lerp(g1, g1 * 0.6f + g2 * 0.4f, gustMix);
        g = g * 0.5f + 0.5f;

        return Mathf.Lerp(0.5f, 1.5f, g);
    }

    /// <summary>
    /// ВЕТЕР В ТОЧКЕ — вектор скорости в мире.
    ///
    /// Это то, что должны спрашивать все скрипты вместо собственных полей
    /// направления и силы. Тогда листья, тачка, брезент и трава подчиняются
    /// одному ветру, а не трём похожим.
    /// </summary>
    public Vector3 SampleWind(Vector3 worldPos)
    {
        return _dir * (_strength * HeightMultiplier(worldPos.y) * GustAt(worldPos));
    }

    /// <summary>Статический доступ: вернёт ноль, если контроллера в сцене нет.
    /// Так вызывающему не надо каждый раз проверять на null.</summary>
    public static Vector3 Sample(Vector3 worldPos)
    {
        return Instance != null ? Instance.SampleWind(worldPos) : Vector3.zero;
    }

    /// <summary>Дёргать из пресетов погоды: 0 = штиль, 1 = шторм.</summary>
    public void SetWeather(float t)
    {
        usePreset = false;
        strength = Mathf.Lerp(0.12f, 1.9f, t);
        speed = Mathf.Lerp(0.5f, 2.8f, t);
        gustSpeed = Mathf.Lerp(0.4f, 3.4f, t);
        gustSpeed2 = Mathf.Lerp(0.9f, 5.6f, t);
        Apply();
    }

    void OnDrawGizmosSelected()
    {
        float rad = directionDegrees * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

        Gizmos.color = new Color(0.5f, 0.9f, 1f, 0.9f);
        Gizmos.DrawRay(transform.position, dir * 5f);
        Gizmos.DrawSphere(transform.position + dir * 5f, 0.25f);

        if (!showHeightGizmo) return;

        // Профиль по высоте столбиком: длина стрелки на каждом уровне
        // пропорциональна силе. Сразу видно, где ветер уже «взрослый».
        Vector3 basePos = new Vector3(transform.position.x, groundY, transform.position.z);

        for (int i = 0; i <= 10; i++)
        {
            float h = referenceHeight * 1.2f * i / 10f;
            float m = HeightMultiplier(groundY + h);

            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.25f + m * 0.6f);
            Vector3 p = basePos + Vector3.up * h;
            Gizmos.DrawRay(p, dir * (m * 4f));
        }
    }
}
