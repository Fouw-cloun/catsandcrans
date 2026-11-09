using UnityEngine;

/// <summary>
/// СМИНАНИЕ ПРОДУКТА ПО ХОДУ ОБРАБОТКИ. Вешается на FoodItem, рядом с FoodVisual.
///
/// Пока копится прогресс, продукт приседает в лепёшку: становится ниже, шире
/// и слегка заваливается. К моменту растворения от картофелины визуально мало
/// что остаётся, и превращение в пюре читается как продолжение, а не как
/// подмена объекта.
///
/// МНЁТСЯ ВЕСЬ КОМ ЦЕЛИКОМ, А НЕ ОТДЕЛЬНЫЕ КУСКИ.
///   Модель картошки склеена из четырёх мешей. Двигать их по отдельности —
///   значит показать, что она РАСПАЛАСЬ на куски, а не смялась: между ними
///   появляются щели, и вместо лепёшки получается рассыпанная горка. Поэтому
///   все меши мнутся как одно тело, сохраняя взаимное положение.
///
/// РАБОТАЕТ ЧЕРЕЗ ПРОМЕЖУТОЧНЫЙ КОНТЕЙНЕР, а не через масштаб корня.
///   Корень трогать нельзя по двум причинам сразу. Его позицией распоряжается
///   Rigidbody — запись туда каждый кадр держала бы предмет висящим в
///   воздухе. А его масштаб перетирает NetworkTransform, если включена
///   синхронизация Scale, и сминания не видно вовсе.
///   Контейнер — обычный дочерний объект: физике он не принадлежит, по сети
///   не ходит, мять его можно свободно.
///
/// ВЕРШИНЫ НЕ ТРОГАЕМ. Настоящая деформация меша требует Read/Write на
/// модели — это вторая копия меша в памяти на КАЖДУЮ картофелину. Кастрюля в
/// сцене одна, продуктов сотни, и цена совершенно разная. Приседание с
/// расползанием вширь читается с полутора метров ничуть не хуже.
///
/// Чисто локальный компонент: берёт уже синхронизированный Progress01 и
/// ничего не шлёт. Все клиенты видят одно и то же, потому что источник общий.
/// </summary>
public class FoodSquashVisual : MonoBehaviour
{
    [Header("Что мнём")]
    [Tooltip("Контейнер с мешами продукта.\n\n" +
             "ПУСТО = соберётся сам: если все меши уже лежат под одним общим " +
             "родителем, возьмётся он, иначе создастся новый и меши переедут " +
             "в него. Заполнять руками нужно только в необычной иерархии.")]
    [SerializeField] private Transform _container;

    [Header("Форма лепёшки")]
    [Tooltip("ВДОЛЬ КАКОЙ ОСИ ПЛЮЩИТЬ.\n\n" +
             "Ось ЛОКАЛЬНАЯ, то есть меша, а не мира. Blender отдаёт модели с " +
             "произвольной ориентацией, и у картошки «вверх» запросто " +
             "оказывается X или Z — тогда она плющится вбок вместо того, " +
             "чтобы оседать. Перебери три значения и возьми то, при котором " +
             "лепёшка ложится правильно.\n\n" +
             "Локальная, а не мировая, намеренно: смятая картошка должна " +
             "оставаться смятой, когда её катают и переворачивают, а не " +
             "переориентироваться на ходу.")]
    [SerializeField] private Axis _squashAxis = Axis.Y;

    [Tooltip("До какой доли размера приседает продукт на полном прогрессе. " +
             "0.15 = от картофелины остаётся шестая часть толщины.")]
    [Range(0.02f, 1f)][SerializeField] private float _flattenTo = 0.15f;

    [Tooltip("Насколько расползается вширь по двум другим осям. Объём должен " +
             "куда-то деваться — просто сплющенный предмет выглядит как " +
             "ошибка масштаба.")]
    [Range(1f, 4f)][SerializeField] private float _spreadTo = 2.2f;

    [Tooltip("Насколько ком расползается НЕРАВНОМЕРНО: по одной оси шире, " +
             "чем по другой. Идеально круглая лепёшка выглядит выточенной, " +
             "перекошенная — раздавленной.")]
    [Range(0f, 0.6f)][SerializeField] private float _unevenness = 0.25f;

    [Tooltip("На сколько градусов заваливается ком. Ровно осевшая лепёшка " +
             "читается как аккуратно поставленная.")]
    [Range(0f, 45f)][SerializeField] private float _tilt = 12f;

    [Tooltip("Опустить ком вниз при сминании, чтобы он не парил на высоте " +
             "своего прежнего центра. Доля от исходной высоты.")]
    [Range(0f, 1f)][SerializeField] private float _sink = 0.5f;

    [Tooltip("Насколько мягко масштаб догоняет цель. Больше — резче.")]
    [SerializeField] private float _followSpeed = 8f;

    [Header("Тряска от ударов")]
    [Tooltip("Амплитуда дрожи, доля размера. Заметна, только пока прогресс " +
             "растёт: остановился инструмент — успокоился и продукт.")]
    [Range(0f, 0.2f)][SerializeField] private float _jitter = 0.06f;

    [SerializeField] private float _jitterSpeed = 28f;

    [Tooltip("Сколько секунд после последнего роста прогресса ещё трясётся")]
    [SerializeField] private float _jitterHold = 0.25f;

    [Header("Отладка")]
    [Tooltip("Написать в консоль, что нашлось и что создалось. Включи, если " +
             "сминание не видно.")]
    [SerializeField] private bool _debugReport;

    public enum Axis { X, Y, Z }

    private FoodItem _item;
    private Vector3 _baseScale;
    private Vector3 _basePos;
    private Quaternion _baseRot;
    private float _baseHeight = 1f;

    private float _shown;
    private float _lastProgress;
    private float _lastGrowTime = -99f;

    private void Awake()
    {
        _item = GetComponent<FoodItem>() ?? GetComponentInParent<FoodItem>();

        if (_item == null)
        {
            Debug.LogError($"[Сминание] «{name}»: рядом нет FoodItem. Компонент " +
                           "вешается на сам продукт.", this);
            enabled = false;
            return;
        }

        if (_container == null) _container = ResolveContainer();

        if (_container == null)
        {
            enabled = false;
            return;
        }

        _basePos = _container.localPosition;
        _baseRot = _container.localRotation;
        _baseScale = _container.localScale;

        // Нулевой масштаб означал бы, что продукта не видно никогда, сколько
        // его ни мни.
        if (_baseScale.sqrMagnitude < 1e-8f) _baseScale = Vector3.one;

        // Высота нужна, чтобы опускать ком ровно настолько, насколько он
        // просел, а не на выдуманное число метров. Меряем МИРОВУЮ высоту:
        // оседать ком должен вниз по миру, какой бы локальной осью его ни
        // плющило.
        Renderer r = _container.GetComponentInChildren<Renderer>();
        if (r != null) _baseHeight = r.bounds.size.y;
    }

    /// <summary>
    /// Найти или создать контейнер, в котором лежат ВСЕ меши продукта.
    ///
    /// Если меши уже под одним общим родителем — берём его как есть, ничего
    /// не перестраивая. Если разбросаны — создаём контейнер и переносим их
    /// туда. Переезжают только рендереры: коллайдеры остаются на своих
    /// местах, поэтому физика продукта не меняется ни на йоту.
    /// </summary>
    private Transform ResolveContainer()
    {
        MeshRenderer[] mr = GetComponentsInChildren<MeshRenderer>(true);

        if (mr.Length == 0)
        {
            Debug.LogWarning($"[Сминание] «{name}»: не нашлось ни одного MeshRenderer — " +
                             "мять нечего.", this);
            return null;
        }

        // Все меши уже под одним родителем, и это не сам корень — годится.
        Transform common = mr[0].transform.parent;
        bool shared = common != null && common != transform;

        if (shared)
            for (int i = 1; i < mr.Length; i++)
                if (mr[i].transform.parent != common) { shared = false; break; }

        if (shared)
        {
            if (_debugReport)
                Debug.Log($"[Сминание] «{name}»: мешей {mr.Length}, общий родитель " +
                          $"«{common.name}» — мну его.", this);
            return common;
        }

        // Общего родителя нет — заводим свой и переносим туда меши.
        GameObject go = new GameObject("SquashContainer (auto)");
        go.transform.SetParent(transform, false);

        for (int i = 0; i < mr.Length; i++)
            mr[i].transform.SetParent(go.transform, true);   // мировые позиции сохраняем

        if (_debugReport)
            Debug.Log($"[Сминание] «{name}»: мешей {mr.Length}, общего родителя не " +
                      "было — создал контейнер и перенёс их в него.", this);

        return go.transform;
    }

    private void LateUpdate()
    {
        if (_item == null || _container == null) return;

        // Продукт уже поглощён ёмкостью и ждёт деспавна — трогать его незачем.
        if (_item.Consumed) return;

        float p = _item.Progress01;

        // Прогресс РАСТЁТ — значит по продукту сейчас работают. Именно рост, а
        // не само значение: застывший на 60% предмет трястись не должен.
        if (p > _lastProgress + 0.0001f) _lastGrowTime = Time.time;
        _lastProgress = p;

        _shown = Mathf.Lerp(_shown, p, 1f - Mathf.Exp(-_followSpeed * Time.deltaTime));

        float flat = Mathf.Lerp(1f, _flattenTo, _shown);

        // Две «широкие» оси расползаются по-разному — перекошенная лепёшка
        // выглядит раздавленной, а идеально круглая выточенной.
        float wideA = Mathf.Lerp(1f, _spreadTo * (1f + _unevenness), _shown);
        float wideB = Mathf.Lerp(1f, _spreadTo * (1f - _unevenness), _shown);

        // Приседает выбранная ось, расползаются две оставшиеся.
        Vector3 mul = _squashAxis switch
        {
            Axis.X => new Vector3(flat, wideA, wideB),
            Axis.Z => new Vector3(wideA, wideB, flat),
            _ => new Vector3(wideA, flat, wideB)
        };

        Vector3 scale = new Vector3(_baseScale.x * mul.x,
                                    _baseScale.y * mul.y,
                                    _baseScale.z * mul.z);

        if (Time.time - _lastGrowTime < _jitterHold && _jitter > 0.001f)
        {
            // Дрожь тем мельче, чем меньше осталось от продукта: размазанная
            // каша уже не подпрыгивает. Три разные частоты, иначе тряска
            // выглядит ровной пульсацией.
            float amp = _jitter * (1f - _shown * 0.6f);
            float t = Time.time * _jitterSpeed;

            scale.x *= 1f + Mathf.Sin(t) * amp;
            scale.y *= 1f + Mathf.Sin(t * 1.7f + 1.1f) * amp;
            scale.z *= 1f + Mathf.Sin(t * 1.3f + 2.3f) * amp;
        }

        _container.localScale = scale;
        _container.localRotation = _baseRot * Quaternion.Euler(_tilt * _shown, 0f, _tilt * 0.6f * _shown);

        // Оседание: просел ком — опускаем его ровно на столько же, иначе
        // лепёшка парила бы на высоте бывшего центра картофелины.
        float drop = _baseHeight * (1f - flat) * 0.5f * _sink;
        _container.localPosition = _basePos - new Vector3(0f, drop, 0f);
    }
}
