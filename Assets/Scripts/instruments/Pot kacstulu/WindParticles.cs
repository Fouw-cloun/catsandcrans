using UnityEngine;

/// <summary>
/// ЧАСТИЦЫ ВЕТРА. Самособирающиеся: систему частиц целиком строит и настраивает
/// код, руками в инспекторе крутить ничего не надо.
///
/// Повесь на объект, выстави ось и параметры — при старте появится готовая
/// система. Залп вызывается методом Blast(сила 0..1).
///
/// ═══ ПОЧЕМУ ИЗ КОДА, А НЕ РУКАМИ ═══
///
/// У системы частиц полсотни настроек в десятке модулей. Собранная руками, она
/// живёт только в том префабе, где её собрали: чтобы сделать вторую шину или
/// вентилятор, надо копировать префаб и не забыть поправить всё, что зависит от
/// размера и направления.
///
/// Здесь всё выражено полями этого компонента. Скопировал компонент — получил
/// точно такую же систему. Поменял ось — она перестроилась.
///
/// Плюс код нельзя «случайно поправить в инспекторе и забыть»: настройки,
/// которые важны для работы (нулевая эмиссия, мировое пространство), выставятся
/// заново при каждом запуске.
///
/// ═══ ПРО ОСЬ НАПРАВЛЕНИЯ ═══
///
/// Unity выпускает частицы из конуса вдоль ЛОКАЛЬНОЙ +Z системы, а не вдоль Y.
/// Именно поэтому созданная через меню Particle System приходит повёрнутой на
/// −90° по X: чтобы её +Z смотрел вверх.
///
/// Помнить об этом каждый раз неудобно и легко ошибиться, поэтому здесь ось
/// задаётся полем в понятном виде (по умолчанию +Y, «вверх»), а компонент сам
/// создаёт ДОЧЕРНИЙ объект и разворачивает его так, чтобы +Z совпал с этой
/// осью.
///
/// Дочерний, а не этот же самый: на текущем объекте обычно висит меш, и
/// разворот системы развернул бы и его.
///
/// ═══ МИРОВОЕ ПРОСТРАНСТВО ═══
///
/// Simulation Space = World выставляется принудительно. В локальном частицы
/// таскались бы за объектом: пнул шину — и уже выпущенный воздух поехал вместе
/// с ней, как приклеенный. Выпущенный воздух не должен знать, что стало с
/// соплом.
/// </summary>
[DisallowMultipleComponent]
public class WindParticles : MonoBehaviour
{
    public enum EmitShape
    {
        /// <summary>Конус — обычный ветер из сопла.</summary>
        Cone = 0,

        /// <summary>Форма по мешу. Сюда и кладётся конус от огня.</summary>
        Mesh = 1,

        /// <summary>Полусфера — рассеянный выхлоп во все стороны вперёд.</summary>
        Hemisphere = 2
    }

    [Header("Направление")]
    [Tooltip("ОСЬ ВЫДУВА в локальных координатах этого объекта.\n\n" +
             "(0,1,0) — вверх, (0,0,1) — вперёд, (1,0,0) — вправо.\n\n" +
             "Компонент сам развернёт систему частиц так, чтобы её выпуск " +
             "совпал с этой осью — помнить про то, что Unity выпускает вдоль " +
             "+Z, не нужно.")]
    [SerializeField] private Vector3 _axis = Vector3.up;

    [Header("Форма")]
    [SerializeField] private EmitShape _shape = EmitShape.Cone;

    [Tooltip("Половина угла раствора конуса, градусы")]
    [Range(0f, 89f)][SerializeField] private float _coneAngle = 25f;

    [Tooltip("Радиус устья, метры")]
    [Min(0.01f)][SerializeField] private float _radius = 0.35f;

    [Tooltip("Меш формы — сюда кладётся конус от огня. Нужен только при " +
             "Shape = Mesh.")]
    [SerializeField] private Mesh _shapeMesh;

    [Tooltip("Масштаб меша формы")]
    [SerializeField] private Vector3 _meshScale = Vector3.one;

    [Header("Полёт")]
    [Tooltip("Скорость частиц при ПОЛНОЙ силе залпа, м/с.\n\n" +
             "ДАЛЬНОСТЬ ≈ СКОРОСТЬ × ВРЕМЯ ЖИЗНИ, минус торможение. 20 и 1.4 " +
             "дают порядка двадцати метров при нулевом Drag и метров " +
             "двенадцать при обычном.")]
    [SerializeField] private float _speed = 20f;

    [Tooltip("Сколько живёт частица, сек. Вторая половина формулы дальности: " +
             "хочешь дальше — поднимай вместе со скоростью, одной скорости " +
             "мало.")]
    [Min(0.05f)][SerializeField] private float _lifetime = 1.4f;

    [Tooltip("Размер частицы при полной силе, метры")]
    [Min(0.01f)][SerializeField] private float _size = 0.55f;

    [Tooltip("Во сколько раз частица вырастает к концу жизни. Воздух " +
             "расширяется, расходясь — без роста струя выглядит очередью " +
             "из пулемёта.")]
    [Range(1f, 6f)][SerializeField] private float _growth = 2.4f;

    [Tooltip("СОПРОТИВЛЕНИЕ ВОЗДУХА. Замедляет постепенно, по ходу полёта.\n\n" +
             "ЭТО ГЛАВНАЯ РУЧКА ДАЛЬНОСТИ. 0 — частицы летят по прямой до " +
             "конца жизни и улетают максимально далеко. 1–2 — заметно " +
             "тормозят к концу. Выше 4 гаснут почти сразу.")]
    [Range(0f, 10f)][SerializeField] private float _drag = 0.8f;

    [Tooltip("Влияние гравитации. Для воздуха почти ноль, для пыли побольше.")]
    [Range(-1f, 1f)][SerializeField] private float _gravity = 0.02f;

    [Header("Количество")]
    [Tooltip("Сколько частиц в залпе при ПОЛНОЙ силе")]
    [Min(1)][SerializeField] private int _burstMax = 45;

    [Tooltip("Потолок одновременно живых частиц. Защита от спама залпов.")]
    [Min(4)][SerializeField] private int _maxParticles = 300;

    [Header("Вид")]
    [Tooltip("Материал частиц. Пусто — создастся на шейдере CatCooks/Air Puff, " +
             "который даёт распад на клочья вместо равномерного затухания.")]
    [SerializeField] private Material _material;

    [Tooltip("ВЫТЯГИВАТЬ КЛУБ ПО ДВИЖЕНИЮ.\n\n" +
             "Круглые клубы читаются как облачко, вытянутые — как стремительный " +
             "порыв. Для выдуваемого воздуха нужно второе.")]
    [SerializeField] private bool _stretched = true;

    [Tooltip("Насколько вытягивать по скорости. Больше — длиннее полосы. " +
             "Выше 1.5 клуб превращается в макаронину.")]
    [Range(0f, 2f)][SerializeField] private float _stretchBySpeed = 0.6f;

    [Tooltip("Базовое вытягивание, независимо от скорости")]
    [Range(0.5f, 3f)][SerializeField] private float _stretchLength = 1.2f;

    [SerializeField] private Color _colorStart = new Color(0.9f, 0.92f, 0.95f, 0.55f);
    [SerializeField] private Color _colorEnd = new Color(0.85f, 0.87f, 0.9f, 0f);

    [Tooltip("Порядок отрисовки. Больше — рисуется поверх.")]
    [SerializeField] private int _sortingOrder = 0;

    [Header("Автозапуск")]
    [Tooltip("Дуть непрерывно, а не залпами. Для вентилятора или трубы.")]
    [SerializeField] private bool _continuous = false;

    [Tooltip("Частиц в секунду при непрерывном режиме")]
    [Min(0f)][SerializeField] private float _continuousRate = 30f;

    private ParticleSystem _ps;
    private Transform _root;

    /// <summary>Готовая система — если понадобится что-то доснастить снаружи.</summary>
    public ParticleSystem System => _ps;

    private void Awake() => Build();

    /// <summary>
    /// Собрать систему. Вынесено отдельно, чтобы можно было пересобрать после
    /// правки полей — например из кнопки в инспекторе.
    /// </summary>
    public void Build()
    {
        EnsureRoot();
        Apply();
    }

    /// <summary>Дочерний объект под систему: разворачиваем ЕГО, а не себя,
    /// иначе развернётся и меш, который висит на этом же объекте.</summary>
    private void EnsureRoot()
    {
        if (_root == null)
        {
            Transform found = transform.Find("__WindParticles");

            if (found != null)
            {
                _root = found;
            }
            else
            {
                GameObject go = new GameObject("__WindParticles");
                go.transform.SetParent(transform, false);
                _root = go.transform;
            }
        }

        Vector3 dir = _axis.sqrMagnitude < 0.0001f ? Vector3.up : _axis.normalized;

        // LookRotation падает, если направление совпадает с подсказкой "вверх".
        // Для оси +Y это ровно наш случай по умолчанию, поэтому подсказку
        // выбираем заведомо не параллельную.
        Vector3 hint = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f
            ? Vector3.forward
            : Vector3.up;

        _root.localRotation = Quaternion.LookRotation(dir, hint);
        _root.localPosition = Vector3.zero;

        _ps = _root.GetComponent<ParticleSystem>();
        if (_ps == null) _ps = _root.gameObject.AddComponent<ParticleSystem>();
    }

    private void Apply()
    {
        if (_ps == null) return;

        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // ── основной модуль ──
        ParticleSystem.MainModule main = _ps.main;
        main.duration = 1f;
        main.loop = _continuous;
        main.playOnAwake = _continuous;
        main.startLifetime = _lifetime;
        main.startSpeed = _speed;
        main.startSize = _size;
        main.startColor = _colorStart;
        main.gravityModifier = _gravity;
        main.maxParticles = _maxParticles;

        // МИРОВОЕ пространство: выпущенный воздух не должен ездить за соплом.
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Небольшой разброс поворота, чтобы клубы не выглядели штампованными.
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        // ── эмиссия ──
        ParticleSystem.EmissionModule em = _ps.emission;
        em.enabled = true;
        // В залповом режиме постоянная эмиссия строго НОЛЬ: частицы выпускает
        // только Blast(). Иначе из сопла будет непрерывно сифонить.
        em.rateOverTime = _continuous ? _continuousRate : 0f;

        // ── форма ──
        ParticleSystem.ShapeModule shape = _ps.shape;
        shape.enabled = true;
        shape.scale = Vector3.one;

        switch (_shape)
        {
            case EmitShape.Mesh:
                shape.shapeType = ParticleSystemShapeType.Mesh;
                shape.mesh = _shapeMesh;
                shape.scale = _meshScale;
                // По объёму, а не только по вершинам: с вершин частицы полезли
                // бы решёткой по рёбрам меша.
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;

                if (_shapeMesh == null)
                    Debug.LogWarning($"[Ветер] «{name}»: Shape = Mesh, но меш не " +
                                     "назначен — частиц не будет.", this);
                break;

            case EmitShape.Hemisphere:
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = _radius;
                break;

            default:
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = _coneAngle;
                shape.radius = _radius;
                shape.radiusThickness = 1f;

                // Частая путаница: меш назначен, а форма стоит конусом — тогда
                // меш не используется вообще, и непонятно, почему он «не
                // работает». Молчать об этом хуже, чем сказать.
                if (_shapeMesh != null)
                    Debug.LogWarning($"[Ветер] «{name}»: в Shape Mesh лежит " +
                                     $"«{_shapeMesh.name}», но Shape = Cone — " +
                                     "меш игнорируется. Поставь Shape = Mesh " +
                                     "или очисти поле.", this);
                break;
        }

        // ── торможение ──
        // Воздух гасится о среду быстро. Без этого частицы уходят по прямой за
        // горизонт, и струя выглядит лучом, а не порывом.
        // ── торможение ──
        //
        // ЗДЕСЬ БЫЛА ОШИБКА, ИЗ-ЗА КОТОРОЙ ЧАСТИЦЫ НИКУДА НЕ ЛЕТЕЛИ.
        // Раньше стояло limit = 0.1, а это ЖЁСТКИЙ ПОТОЛОК СКОРОСТИ 0.1 м/с:
        // частица гасилась почти до нуля в первые миллисекунды, и ни Speed, ни
        // Lifetime на дальность уже не влияли. Симптом «как ни крути, летит
        // коротко» ровно отсюда.
        //
        // Правильный инструмент — поле drag того же модуля: оно замедляет
        // ПОСТЕПЕННО, как сопротивление воздуха, а не обрезает скорость сверху.
        // Потолок при этом ставим заведомо недостижимый, чтобы он не вмешивался.
        ParticleSystem.LimitVelocityOverLifetimeModule limit = _ps.limitVelocityOverLifetime;
        limit.enabled = _drag > 0.01f;
        limit.limit = 10000f;      // фактически без ограничения
        limit.dampen = 0f;
        limit.drag = _drag;
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = true;

        // ── рост ──
        ParticleSystem.SizeOverLifetimeModule size = _ps.sizeOverLifetime;
        size.enabled = true;

        AnimationCurve grow = new AnimationCurve(
            new Keyframe(0f, 1f / _growth),
            new Keyframe(1f, 1f));
        size.size = new ParticleSystem.MinMaxCurve(_growth, grow);

        // ── затухание ──
        ParticleSystem.ColorOverLifetimeModule col = _ps.colorOverLifetime;
        col.enabled = true;

        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(_colorStart, 0f),
                new GradientColorKey(_colorEnd, 1f)
            },
            new[]
            {
                // Быстрый выход на видимость и долгий уход — так порыв
                // читается как удар, а не как ровное свечение.
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(_colorStart.a, 0.15f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        // ── рендер ──
        ParticleSystemRenderer r = _root.GetComponent<ParticleSystemRenderer>();
        if (r != null)
        {
            if (_stretched)
            {
                // Вытянутый билборд разворачивает частицу ВДОЛЬ её скорости.
                // Именно это отличает «порыв» от «облачка»: круглые клубы
                // выглядят ватой, вытянутые — струёй.
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = _stretchBySpeed;
                r.lengthScale = _stretchLength;
                r.cameraVelocityScale = 0f;
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
            }

            r.sortMode = ParticleSystemSortMode.YoungestInFront;
            r.sortingOrder = _sortingOrder;
            r.material = _material != null ? _material : DefaultMaterial();

            // Частицам воздуха не нужны ни тени, ни свет.
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        if (_continuous) _ps.Play();
    }

    /// <summary>
    /// Материал по умолчанию, если свой не назначен. Не «чтобы было красиво», а
    /// чтобы система не рисовалась розовым при первом запуске и не создавала
    /// впечатление поломки.
    /// </summary>
    private Material DefaultMaterial()
    {
        // Сначала свой шейдер пара: он даёт распад на клочья. Встроенный
        // Particles/Unlit гасит альфу равномерно, и клуб выглядит мутным
        // пятном — ради этого шейдер и писался.
        Shader sh = Shader.Find("CatCooks/Air Puff");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) return null;

        Material m = new Material(sh) { name = "WindParticles (auto)" };

        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);      // Transparent
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);          // Alpha
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);

        m.renderQueue = 3000;
        return m;
    }

    // ───────────────────────── ЗАЛП ─────────────────────────

    /// <summary>
    /// Пальнуть. strength01 — сила 0..1: от неё зависят и количество частиц, и
    /// их скорость, и размер.
    ///
    /// Все три сразу, а не только количество: слабый залп должен выглядеть
    /// слабым, а не «таким же, но реже».
    /// </summary>
    public void Blast(float strength01)
    {
        if (_ps == null) Build();
        if (_ps == null) return;

        float s = Mathf.Clamp01(strength01);
        if (s <= 0.001f) return;

        ParticleSystem.MainModule main = _ps.main;

        // Слабый залп раньше резал скорость почти вдвое (0.45), и вместе со
        // сломанным тормозом это давало совсем короткий плевок. Нижнюю границу
        // подняли: слабый порыв должен быть КОРОЧЕ, а не «никакой».
        main.startSpeed = _speed * Mathf.Lerp(0.7f, 1f, s);
        main.startSize = _size * Mathf.Lerp(0.6f, 1f, s);

        int count = Mathf.Max(1, Mathf.RoundToInt(_burstMax * s));
        _ps.Emit(count);
    }

    /// <summary>Включить/выключить непрерывный обдув — для вентилятора.</summary>
    public void SetContinuous(bool on)
    {
        if (_ps == null) Build();
        if (_ps == null) return;

        ParticleSystem.EmissionModule em = _ps.emission;
        em.rateOverTime = on ? _continuousRate : 0f;

        if (on && !_ps.isPlaying) _ps.Play();
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    [ContextMenu("Пересобрать систему")]
    private void EditorRebuild() => Build();

    [ContextMenu("Тестовый залп")]
    private void EditorTestBlast() => Blast(1f);

    /// <summary>
    /// Прикинуть, куда долетят частицы при текущих настройках.
    ///
    /// Без этого дальность подбирается вслепую: три поля влияют на неё
    /// одновременно, и по отдельности ни одно ничего не говорит.
    ///
    /// Формула приближённая: при постоянном сопротивлении скорость падает
    /// экспоненциально, поэтому путь = v / drag * (1 - e^(-drag * t)). При
    /// нулевом сопротивлении это вырождается в обычное v * t.
    /// </summary>
    [ContextMenu("Прикинуть дальность")]
    private void EditorEstimateRange()
    {
        float v = _speed;
        float t = _lifetime;

        float reach = _drag < 0.01f
            ? v * t
            : v / _drag * (1f - Mathf.Exp(-_drag * t));

        Debug.Log($"[Ветер] «{name}»: при скорости {v:0.0} м/с, времени жизни " +
                  $"{t:0.00} сек и сопротивлении {_drag:0.00} частицы улетят " +
                  $"примерно на {reach:0.0} метров.\n" +
                  "Мало — поднимай Speed и Lifetime вместе, а Drag опускай " +
                  "к нулю: именно он гасит дальность сильнее всего.", this);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 dir = _axis.sqrMagnitude < 0.0001f ? Vector3.up : _axis.normalized;
        Vector3 world = transform.TransformDirection(dir);

        Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.9f);
        Gizmos.DrawRay(transform.position, world * 2f);
        Gizmos.DrawWireSphere(transform.position + world * 2f, 0.12f);
    }
}
