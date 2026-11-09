using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// РЕЖИССЁР СВЕТА. Держит цену освещения в рамках.
///
/// Один компонент на сцену. Работает сам, ничего настраивать не обязательно.
///
/// ═══ ЧТО ИМЕННО СТОИТ ДОРОГО ═══
///
/// Вопреки ожиданиям — НЕ количество источников. В Forward+ (по умолчанию в
/// Unity 6) дополнительные светы обсчитываются по тайлам экрана, и сотня
/// лампочек стоит немногого.
///
/// Дорого стоят ТЕНИ. У каждого теневого источника свой проход отрисовки всей
/// геометрии в его радиусе. Десять теневых точечных светов на стройке — это
/// десять лишних проходов по экскаваторам и башне.
///
/// Поэтому здесь два РАЗНЫХ бюджета, и теневой в разы меньше светового.
///
/// В классическом Forward (если ты не на Forward+) ситуация другая: там жёсткий
/// лимит источников на объект, и лишние светы просто НЕ РИСУЮТСЯ — причём
/// какие именно, решает Unity, и решение меняется от кадра к кадру. Выглядит
/// это как мигающие лампы. Световой бюджет здесь избавляет и от этого: раз
/// выбор всё равно будет сделан, лучше сделать его осознанно и стабильно.
///
/// ═══ КАК ВЫБИРАЕТ ═══
///
///     важность = яркость × дальность × приоритет / (расстояние² + 1)
///
/// Квадрат расстояния в знаменателе не случаен: свет и падает по квадрату.
/// Источник вдвое дальше вносит вчетверо меньше — значит и в бюджете должен
/// стоять вчетверо дешевле.
///
/// Пересчёт идёт не каждый кадр: сортировка сотни источников это работа, а
/// расстановка ламп на стройке за десять кадров не меняется.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Свет/Light Director")]
public class CatCooksLightDirector : MonoBehaviour
{
    [Header("━━━ Бюджеты ━━━")]
    [Tooltip("Сколько источников может гореть одновременно.\n\n" +
             "В Forward+ можно ставить щедро: 32–64. В классическом Forward " +
             "смысл имеет 8–16, иначе Unity начнёт выбирать сама и лампы " +
             "будут мигать при движении камеры.")]
    [Min(1)][SerializeField] private int _lightBudget = 32;

    [Tooltip("Сколько источников могут отбрасывать тени.\n\n" +
             "Это главный ползунок производительности во всей системе. " +
             "Каждая тень — отдельный проход отрисовки. Четыре это уже " +
             "много, восемь — заметно.")]
    [Min(0)][SerializeField] private int _shadowBudget = 4;

    [Header("━━━ Обновление ━━━")]
    [Tooltip("Как часто пересчитывать бюджет, секунды. Каждый кадр не нужно: " +
             "расстановка ламп не меняется так быстро.")]
    [Range(0.02f, 2f)][SerializeField] private float _interval = 0.2f;

    [Tooltip("Пересчитать немедленно, если камера сдвинулась дальше чем на " +
             "столько метров. Телепорт и смена ракурса не должны ждать " +
             "своей очереди.")]
    [Min(0f)][SerializeField] private float _forceOnCameraMove = 6f;

    [Header("━━━ Камера ━━━")]
    [Tooltip("ОСТАВЬ ПУСТЫМ в сетевой игре.\n\n" +
             "Камера спавнится вместе с префабом игрока, и в момент старта " +
             "сцены её ещё нет — назначить в инспекторе физически нечего. " +
             "Режиссёр ищет её сам и продолжает искать, пока не найдёт.\n\n" +
             "Из кода после спавна локального игрока лучше сказать прямо:\n" +
             "  CatCooksLightDirector.AssignCamera(cam.transform);")]
    [SerializeField] private Transform _camera;

    [Header("━━━ Общее ━━━")]
    [Tooltip("Общий множитель яркости всех источников. Для перехода дня и " +
             "ночи: вечером лампы разгораются одной ручкой.")]
    [Range(0f, 4f)][SerializeField] private float _globalIntensity = 1f;

    [Tooltip("Отключить всё управление: все источники горят как есть. " +
             "Для отладки — сразу видно, что делает режиссёр, а что нет.")]
    [SerializeField] private bool _bypass = false;

    [Header("━━━ Отладка ━━━")]
    [SerializeField] private bool _showReadout = false;
    [SerializeField] private bool _drawGizmos = false;

    private float _timer;
    private Vector3 _lastCameraPos;

    private readonly List<Entry> _scored = new List<Entry>();

    private int _activeCount, _shadowCount;

    private struct Entry
    {
        public CatCooksLight light;
        public float score;
        public float distance;
    }

    /// <summary>
    /// Общий множитель яркости, который читают все источники.
    ///
    /// Статический, а не через ссылку на режиссёра: иначе каждая лампа
    /// держала бы ссылку и её пришлось бы восстанавливать при пересборке
    /// сцены. Единица по умолчанию — без режиссёра ничего не ломается.
    /// </summary>
    public static float Global = 1f;

    /// <summary>Общий множитель яркости. Меняй для суток: вечером все лампы
    /// разгораются одной ручкой.</summary>
    public float GlobalIntensity
    {
        get => _globalIntensity;
        set { _globalIntensity = Mathf.Max(0f, value); Global = _globalIntensity; }
    }

    public int ActiveLights => _activeCount;
    public int ShadowLights => _shadowCount;

    private void OnEnable()
    {
        _timer = 0f;
        _lastCameraPos = Vector3.positiveInfinity;
        Global = _globalIntensity;
    }

    // Без режиссёра множитель обязан вернуться к единице, иначе выключение
    // компонента погасило бы всю сцену.
    private void OnDisable() => Global = 1f;

    private void OnValidate() => Global = Mathf.Max(0f, _globalIntensity);

    private void LateUpdate()
    {
        Transform cam = ResolveCamera();

        // Камеры ещё нет — в сетевой игре это нормальное состояние первых
        // секунд. Бюджет считать не от чего, поэтому просто разрешаем всем
        // гореть: лучше лишний свет, чем чёрная сцена.
        if (cam == null)
        {
            AllowEverything();
            return;
        }

        _timer -= Time.deltaTime;

        // Резкое перемещение камеры пересчитываем немедленно: иначе после
        // телепорта или смены ракурса пятая часть секунды будет гореть не то.
        bool moved = (cam.position - _lastCameraPos).sqrMagnitude >
                     _forceOnCameraMove * _forceOnCameraMove;

        if (_timer > 0f && !moved) return;

        _timer = _interval;
        _lastCameraPos = cam.position;
        Global = _globalIntensity;

        Evaluate(cam.position);
    }

    /// <summary>
    /// Назначить камеру из кода. Зови после спавна локального игрока.
    ///
    /// Статический, чтобы не искать сам режиссёр: в сетевой игре порядок
    /// инициализации не гарантирован, и объект игрока может подняться раньше
    /// или позже режиссёра.
    /// </summary>
    public static void AssignCamera(Transform cam)
    {
        _assigned = cam;
    }

    private static Transform _assigned;

    private void AllowEverything()
    {
        IReadOnlyList<CatCooksLight> all = CatCooksLight.Registry;

        for (int i = 0; i < all.Count; i++)
            if (all[i] != null) all[i].SetBudget(true, all[i].WantsShadows, 0f);

        _activeCount = all.Count;
    }

    private Transform ResolveCamera()
    {
        // Назначенная из кода — главнее всего: в кооперативе камера у каждого
        // своя, и угадывать её тегом ненадёжно.
        if (_assigned != null) return _assigned;

        if (_camera != null) return _camera;

        // Camera.main ищет по тегу MainCamera. Если на камере в префабе
        // игрока тег не проставлен, здесь всегда будет null — это самая
        // частая причина, по которой режиссёр «ничего не делает».
        Camera c = Camera.main;

        // Запасной путь: любая включённая камера. Лучше управлять светом по
        // чужой камере, чем не управлять вовсе.
        if (c == null && Camera.allCamerasCount > 0) c = Camera.allCameras[0];

        if (c != null) _camera = c.transform;

        return _camera;
    }

    private void Evaluate(Vector3 eye)
    {
        IReadOnlyList<CatCooksLight> all = CatCooksLight.Registry;

        _scored.Clear();
        _activeCount = 0;

        for (int i = 0; i < all.Count; i++)
        {
            CatCooksLight l = all[i];
            if (l == null) continue;

            float dist = Vector3.Distance(eye, l.transform.position);

            if (_bypass)
            {
                l.SetBudget(true, l.WantsShadows, dist);
                continue;
            }

            // Солнце вне бюджета полностью: у направленного света нет ни
            // положения, ни дальности, и «экономить» на нём означает погасить
            // сцену целиком.
            if (l.IsSun)
            {
                l.SetBudget(true, l.WantsShadows, 0f);
                _activeCount++;
                continue;
            }

            // За границей отсечения источник не нужен вообще. Дешевле
            // отбросить его здесь, чем сортировать вместе с остальными.
            if (!l.NeverCull && dist > l.EffectiveCullDistance)
            {
                l.SetBudget(false, false, dist);
                continue;
            }

            // Квадрат расстояния: свет падает по квадрату, значит источник
            // вдвое дальше вносит вчетверо меньше и стоить в бюджете должен
            // вчетверо дешевле.
            float score = l.BaseIntensity * l.Range * Mathf.Max(0.01f, l.Priority)
                        / (dist * dist + 1f);

            // Защищённые источники всегда впереди очереди. Фонарь игрока не
            // должен соревноваться с лампочкой на башне.
            if (l.NeverCull) score += 1e6f;

            _scored.Add(new Entry { light = l, score = score, distance = dist });
        }

        if (_bypass) { _activeCount = _scored.Count; return; }

        // Сортировка по убыванию важности.
        _scored.Sort((a, b) => b.score.CompareTo(a.score));

        _shadowCount = 0;

        for (int i = 0; i < _scored.Count; i++)
        {
            Entry e = _scored[i];

            bool allowed = e.light.NeverCull || i < _lightBudget;

            // Тени раздаются ТОЛЬКО из головы очереди и только тем, кто их
            // просит. Это самый дорогой ресурс во всей системе.
            bool shadows = allowed && e.light.WantsShadows && _shadowCount < _shadowBudget;

            e.light.SetBudget(allowed, shadows, e.distance);

            if (allowed) _activeCount++;
            if (shadows) _shadowCount++;
        }
    }

    /// <summary>Пересчитать немедленно. Зови после спавна большой группы
    /// ламп или смены сцены.</summary>
    public void ForceUpdate()
    {
        _timer = 0f;

        Transform cam = ResolveCamera();
        if (cam != null) Evaluate(cam.position);
    }

    /// <summary>
    /// Общая сводка по всем источникам сцены. Правой кнопкой по компоненту.
    ///
    /// Отвечает на вопрос «свет не работает вообще или только у одного
    /// объекта» — а это две совершенно разные проблемы.
    /// </summary>
    [ContextMenu("★ СВОДКА ПО ВСЕМ ИСТОЧНИКАМ")]
    public void DiagnoseAll()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("═══ СВОДКА ПО СВЕТУ ═══\n");

        UniversalRenderPipelineAsset urp =
            GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        if (urp == null)
        {
            sb.AppendLine("❌❌ Активный пайплайн — НЕ URP.");
        }
        else
        {
            sb.AppendLine($"URP-ассет: {urp.name}");
            sb.AppendLine($"  Additional Lights: {urp.additionalLightsRenderingMode}");
            sb.AppendLine($"  Лимит на объект: {urp.maxAdditionalLightsCount}");
            sb.AppendLine($"  Тени доп. источников: {urp.supportsAdditionalLightShadows}");

            if (urp.additionalLightsRenderingMode == LightRenderingMode.Disabled)
                sb.AppendLine("\n❌❌ ДОП. ИСТОЧНИКИ ВЫКЛЮЧЕНЫ. Ни один Point и Spot " +
                              "в проекте не светит, скрипты ни при чём.\n" +
                              "   URP-ассет → Lighting → Additional Lights → Per Pixel");
        }

        Transform cam = ResolveCamera();
        sb.AppendLine($"\nКамера: {(cam != null ? cam.name : "НЕ НАЙДЕНА")}");

        if (cam == null)
            sb.AppendLine("   У камеры игрока нет тега MainCamera, либо она ещё не " +
                          "заспавнилась. Пока камеры нет, бюджет не применяется и " +
                          "гореть разрешено всем.");

        sb.AppendLine($"Общая яркость: ×{Global:0.00}");
        sb.AppendLine($"Бюджеты: свет {_lightBudget}, тени {_shadowBudget}" +
                      (_bypass ? "   [BYPASS: режиссёр выключен]" : ""));

        IReadOnlyList<CatCooksLight> all = CatCooksLight.Registry;
        sb.AppendLine($"\n── ИСТОЧНИКОВ: {all.Count} ──");

        int lit = 0;

        for (int i = 0; i < all.Count; i++)
        {
            CatCooksLight cl = all[i];
            if (cl == null) continue;

            Light u = cl.GetComponent<Light>();
            bool on = u != null && u.enabled && u.intensity > 0.001f;
            if (on) lit++;

            float d = cam != null ? Vector3.Distance(cam.position, cl.transform.position) : -1f;

            sb.AppendLine($"  {(on ? "✔" : "✖")} {cl.name}   " +
                          $"{(u != null ? u.type.ToString() : "нет Light")}   " +
                          $"яркость {(u != null ? u.intensity : 0f):0.00}   " +
                          $"дальность {cl.Range:0}   " +
                          $"до камеры {(d >= 0f ? d.ToString("0") + " м" : "?")}   " +
                          $"отсечение {cl.EffectiveCullDistance:0}");
        }

        sb.AppendLine($"\nГОРИТ: {lit} из {all.Count}");

        if (all.Count > 0 && lit == 0)
            sb.AppendLine("\nНЕ ГОРИТ НИ ОДИН. Смотри настройки URP выше — " +
                          "если там Per Pixel, проверь отдельный источник " +
                          "через «★ ПОЧЕМУ НЕ СВЕТИТ».");

        Debug.Log(sb.ToString(), this);
    }

    private void OnGUI()
    {
        if (!_showReadout) return;

        GUI.Label(new Rect(10, 500, 620, 56),
            $"СВЕТ  всего {CatCooksLight.Registry.Count}   " +
            $"горит {_activeCount}/{_lightBudget}   " +
            $"с тенями {_shadowCount}/{_shadowBudget}{(_bypass ? "   [режиссёр выключен]" : "")}\n" +
            $"общая яркость ×{_globalIntensity:0.00}   пересчёт раз в {_interval:0.00} сек");
    }

    private void OnDrawGizmos()
    {
        if (!_drawGizmos) return;

        IReadOnlyList<CatCooksLight> all = CatCooksLight.Registry;

        for (int i = 0; i < all.Count; i++)
        {
            CatCooksLight l = all[i];
            if (l == null) continue;

            Light u = l.GetComponent<Light>();
            bool on = u != null && u.enabled;

            Gizmos.color = on
                ? new Color(1f, 0.9f, 0.4f, 0.9f)
                : new Color(0.4f, 0.4f, 0.45f, 0.5f);

            Gizmos.DrawWireSphere(l.transform.position, 0.4f);
        }
    }
}
