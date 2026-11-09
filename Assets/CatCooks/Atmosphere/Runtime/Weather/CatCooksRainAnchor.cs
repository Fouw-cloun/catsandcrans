using UnityEngine;

// ============================================================================
//  CatCooksRainAnchor — «дождь идёт надо мной».
//
//  ЗАЧЕМ ОТДЕЛЬНЫЙ КОМПОНЕНТ
//   Автопоиск камеры оказался ненадёжным. В сцене живут камеры техники,
//   камеры-заготовки на префабах, отключённые камеры чужих игроков —
//   и дождь регулярно цеплялся не за того. Любая эвристика «выбрать
//   активную камеру» рано или поздно промахивается, потому что решает
//   за разработчика то, что он знает точно.
//
//   Здесь наоборот: компонент вешается на игрока и САМ говорит дождю
//   «иди за мной». Промахнуться нельзя — цель названа явно.
//
//  КУДА ВЕШАТЬ: на корень префаба игрока (или на его камеру — не важно,
//  главное чтобы объект двигался вместе с ним).
//
//  КООП: под FishNet компонент проверяет владение и включается ТОЛЬКО
//  у своего игрока. Дождь не синхронизируется по сети и не должен —
//  он рисуется вокруг камеры того, кто на него смотрит.
// ============================================================================
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Rain Anchor (на игрока)")]
public class CatCooksRainAnchor : MonoBehaviour
{
    [Header("Кому это принадлежит")]
    [Tooltip("В коопе привязывать дождь только у ВЛАДЕЛЬЦА объекта. " +
             "Иначе последний заспавнившийся игрок перетянет дождь на себя " +
             "у всех остальных.")]
    public bool onlyIfOwner = true;

    [Tooltip("Смещение от объекта. Если вешаешь на ноги персонажа, подними " +
             "на рост — дождь считает это точкой обзора.")]
    public Vector3 offset = new Vector3(0f, 1.6f, 0f);

    [Header("Что привязывать")]
    public bool driveRain = true;

    [Tooltip("Росчерки ветра тоже крутятся вокруг игрока")]
    public bool driveWindStreaks = true;

    [Tooltip("Проверка «под крышей ли я» для капель и намокания")]
    public bool driveWeatherFX = true;

    private Transform _proxy;

    void OnEnable()
    {
        // ОЖИДАНИЕ ВЛАДЕЛЬЦА, А НЕ ОТКАЗ С ПЕРВОГО РАЗА.
        //
        // Здесь была гонка: FishNet назначает владельца не обязательно
        // до первого OnEnable компонентов на префабе. Промахнувшись,
        // старый код выключал себя НАВСЕГДА — и у клиента, чей игрок это
        // на самом деле, дождь так и не забирался. Проявлялось редко
        // и невоспроизводимо: зависело от того, успел ли прийти пакет
        // спавна до включения объекта.
        //
        // Теперь ждём до ownerWaitSeconds и сдаёмся только если владелец
        // за это время определился и оказался чужим.
        _waited = 0f;
        _bound = false;

        // Прокси-объект вместо самого игрока: так работает смещение,
        // и дождь не наследует его вращение и масштаб.
        _proxy = new GameObject("~ RainAnchorPoint").transform;
        _proxy.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
        _proxy.SetParent(transform, false);
        _proxy.localPosition = offset;

        Bind();
    }

    void OnDisable()
    {
        // Отпускаем цели: после смерти или смены персонажа колонна дождя
        // осталась бы висеть на месте прошлого тела.
        if (_bound)
        {
            var rain = FindFirstObjectByType<CatCooksRain>();
            if (rain != null && rain.followTarget == _proxy) rain.SetTarget(null);
            var fx = FindFirstObjectByType<CatCooksWeatherFX>();
            if (fx != null && fx.player == _proxy) fx.player = null;
            _bound = false;
        }

        if (_proxy != null)
        {
            if (Application.isPlaying) Destroy(_proxy.gameObject);
            else DestroyImmediate(_proxy.gameObject);
        }
    }

    void Start()
    {
        // Повторная привязка на старте: объекты погоды могли создаться
        // позже игрока, порядок спавна в коопе не гарантирован.
        Bind();
    }

    [Tooltip("Сколько секунд ждать назначения владельца и появления " +
             "объектов погоды, прежде чем сдаться.")]
    public float ownerWaitSeconds = 6f;

    private float _waited;
    private bool _bound;

    void Update()
    {
        if (_bound) return;

        _waited += Time.deltaTime;

        if (onlyIfOwner && !IsOwnerObject())
        {
            // Владельца ещё нет — ждём. Определился и чужой — уходим тихо.
            if (_waited < ownerWaitSeconds) return;
            enabled = false;
            return;
        }

        Bind();

        // Объекты погоды могли ещё не появиться — «★ Пересобрать всё»
        // создаёт их отдельно от префаба игрока. Повторяем, пока не
        // привяжемся или пока не выйдет время.
        if (_bound || _waited >= ownerWaitSeconds) enabled = _bound || !onlyIfOwner;
    }

    void Bind()
    {
        if (_proxy == null) return;
        bool any = false;

        if (driveRain)
        {
            var rain = FindFirstObjectByType<CatCooksRain>();
            if (rain != null) { rain.SetTarget(_proxy); any = true; }
        }

        if (driveWindStreaks)
        {
            var streaks = FindFirstObjectByType<CatCooksWindStreaks>();
            if (streaks != null)
            {
                var cam = GetComponentInChildren<Camera>();
                // Перепривязываем ВСЕГДА, а не только когда пусто: после
                // респавна там висит камера прошлого тела, и росчерки
                // ветра остаются у трупа.
                if (cam != null) { streaks.targetCamera = cam; any = true; }
            }
        }

        if (driveWeatherFX)
        {
            var fx = FindFirstObjectByType<CatCooksWeatherFX>();
            if (fx != null) { fx.player = _proxy; any = true; }
        }

        _bound = any;
    }

    /// <summary>
    /// Владеем ли мы этим объектом. Без FishNet всегда да — в одиночной
    /// игре вопрос не стоит.
    /// </summary>
    bool IsOwnerObject()
    {
#if FISHNET
        var no = GetComponentInParent<FishNet.Object.NetworkObject>();
        // Объект не сетевой либо игра ещё не запущена — считаем своим.
        if (no == null || !Application.isPlaying) return true;
        return no.IsOwner;
#else
        return true;
#endif
    }

    /// <summary>Перепривязать вручную — если игрока переспавнили.</summary>
    public void Rebind() => Bind();
}
