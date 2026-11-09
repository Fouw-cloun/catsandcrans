using FishNet.Object;
using UnityEngine;

/// <summary>
/// ПИНОК. Вешается на префаб игрока, рядом с PlayerInteractor.
///
/// Наводишься на предмет, жмёшь Z — предмет получает толчок и, главное,
/// РАЗВОРАЧИВАЕТСЯ. Поднять его при этом нельзя, только пинать.
///
/// ═══ ЗАЧЕМ ЭТО ═══
///
/// Шину-меха не поднять руками — она большая и тяжёлая. Но её надо КУДА-ТО
/// НАПРАВЛЯТЬ: ветер бьёт конусом туда, куда повёрнуто сопло, и поставленная
/// не той стороной шина бесполезна.
///
/// Пинок и есть способ её развернуть. Поэтому здесь ГЛАВНОЕ — момент вращения,
/// а не сила толчка: пнул с краю, шина провернулась, конус посмотрел в костёр.
///
/// ═══ ПОЧЕМУ РАЗВОРОТ, А НЕ ПРОСТО ТОЛЧОК ═══
///
/// Чистый импульс в центр масс двигает предмет, но не крутит: чтобы повернуть
/// шину, пришлось бы толкать её по кругу, обегая, — возня без всякого смысла.
///
/// Поэтому импульс прикладывается В ТОЧКУ ПОПАДАНИЯ (AddForceAtPosition), а не
/// в центр. Попал с краю — предмет провернётся сам, по законам физики, безо
/// всякого спецкода. Плюс сверху добавляется явный момент, чтобы разворот был
/// заметным даже при попадании близко к центру.
///
/// ═══ ПОЧЕМУ ЧЕРЕЗ СЕРВЕР ═══
///
/// Пинать можно ЧУЖИЕ предметы — те, которыми клиент не владеет. Толкнуть их
/// локально означало бы, что у пинающего они улетели, а у остальных стоят на
/// месте. Клиент только сообщает, куда попал; решение принимает сервер.
///
/// Дистанцию сервер проверяет сам: доверять присланной клиентом точке нельзя,
/// иначе пнуть можно будет что угодно через всю карту.
/// </summary>
public class PlayerKick : NetworkBehaviour
{
    [Header("Наведение")]
    [Tooltip("Камера игрока. Пусто — ищется в детях.")]
    [SerializeField] private Camera _camera;

    [Tooltip("Клавиша пинка")]
    [SerializeField] private KeyCode _kickKey = KeyCode.Z;

    [Tooltip("Дальность пинка, метры")]
    [SerializeField] private float _range = 3f;

    [Tooltip("Что можно пинать. Убери отсюда слой самого игрока.")]
    [SerializeField] private LayerMask _kickMask = ~0;

    [Header("Сила")]
    [Tooltip("Импульс толчка. Прикладывается В ТОЧКУ ПОПАДАНИЯ, поэтому " +
             "попадание с краю разворачивает предмет само собой.")]
    [SerializeField] private float _kickImpulse = 4f;

    [Tooltip("ДОБАВОЧНЫЙ МОМЕНТ ВРАЩЕНИЯ вокруг вертикали.\n\n" +
             "Нужен, чтобы разворот был заметным даже при попадании почти в " +
             "центр — иначе точный пинок в середину шины её просто отодвинет, " +
             "и развернуть её станет вопросом везения.")]
    [SerializeField] private float _kickTorque = 3.5f;

    [Tooltip("Насколько пинок направлен вверх. Небольшой подъём нужен, чтобы " +
             "предмет не втыкался в землю и мог провернуться.")]
    [Range(0f, 1f)][SerializeField] private float _upwardBias = 0.25f;

    [Header("Ограничения")]
    [Tooltip("Пауза между пинками, сек. Без неё Z можно задолбить и разогнать " +
             "предмет до неприличных скоростей.")]
    [Min(0.05f)][SerializeField] private float _cooldown = 0.45f;

    [Tooltip("Тяжелее этого не пинается, кг. Ноль — ограничения нет.\n\n" +
             "Защита от пинания бревна или котла: физика позволит, но " +
             "выглядеть это будет глупо.")]
    [SerializeField] private float _maxMassKg = 0f;

    [Header("Отдача")]
    [Tooltip("Насколько пинающего отталкивает назад, м/с. Ноль — не " +
             "отталкивает. Небольшая отдача не даёт пинать вплотную и толкать " +
             "предмет собой.")]
    [SerializeField] private float _selfRecoil = 1.2f;

    [Header("Звук")]
    [SerializeField] private AudioSource _kickSound;

    [Header("Отладка")]
    [SerializeField] private bool _debugLog;

    private FirstPersonController _fpc;
    private float _nextKick;

    private void Awake()
    {
        _fpc = GetComponent<FirstPersonController>();
        if (_camera == null) _camera = GetComponentInChildren<Camera>(true);

        if (_camera == null)
            Debug.LogError($"[Пинок] «{name}»: не найдена камера — наводиться " +
                           "будет нечем.", this);
    }

    private void Update()
    {
        if (!base.IsOwner || _camera == null) return;
        if (UiModal.AnyOpen) return;
        if (Time.time < _nextKick) return;
        if (!Input.GetKeyDown(_kickKey)) return;

        Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, _range, _kickMask,
                             QueryTriggerInteraction.Ignore))
        {
            if (_debugLog) Debug.Log("[Пинок] мимо", this);
            return;
        }

        Rigidbody rb = hit.collider.attachedRigidbody;
        if (rb == null || rb.isKinematic)
        {
            if (_debugLog)
                Debug.Log($"[Пинок] «{hit.collider.name}» пинать нельзя — " +
                          "нет Rigidbody или он кинематический", this);
            return;
        }

        // Себя не пинаем.
        if (rb.transform.IsChildOf(transform)) return;

        NetworkObject target = rb.GetComponent<NetworkObject>();
        if (target == null) target = rb.GetComponentInParent<NetworkObject>();
        if (target == null || !target.IsSpawned)
        {
            if (_debugLog)
                Debug.Log($"[Пинок] «{rb.name}» не сетевой — пинок не пройдёт " +
                          "по сети", this);
            return;
        }

        _nextKick = Time.time + _cooldown;

        CmdKick(target, hit.point, ray.direction);

        // Отдача — локально и сразу: это движение своего персонажа.
        if (_selfRecoil > 0f && _fpc != null)
        {
            Vector3 back = -ray.direction;
            back.y = 0f;
            if (back.sqrMagnitude > 0.0001f)
                _fpc.AddPush(back.normalized * _selfRecoil);
        }

        if (_kickSound != null) _kickSound.Play();

        if (_debugLog) Debug.Log($"[Пинок] пнул «{rb.name}»", this);
    }

    /// <summary>
    /// Пинок применяет СЕРВЕР: цель может принадлежать другому клиенту или
    /// вообще никому, и локальный толчок разошёлся бы с тем, что видят
    /// остальные.
    /// </summary>
    [ServerRpc]
    private void CmdKick(NetworkObject target, Vector3 point, Vector3 dir)
    {
        if (target == null || !target.IsSpawned) return;

        // Точке от клиента не верим: без проверки можно пнуть что угодно через
        // всю карту. Запас к дальности — на пинг и на то, что игрок за это
        // время шагнул.
        float d = Vector3.Distance(transform.position, point);
        if (d > _range + 2f)
        {
            Debug.LogWarning($"[Пинок/сервер] «{name}» бьёт с {d:0.0} м — " +
                             "слишком далеко, отклоняю", this);
            return;
        }

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb == null) rb = target.GetComponentInChildren<Rigidbody>();
        if (rb == null || rb.isKinematic) return;

        if (_maxMassKg > 0f && rb.mass > _maxMassKg)
        {
            Debug.Log($"[Пинок/сервер] «{rb.name}» слишком тяжёлый " +
                      $"({rb.mass:0} кг)", this);
            return;
        }

        rb.WakeUp();

        Vector3 push = (dir.normalized + Vector3.up * _upwardBias).normalized;

        // В ТОЧКУ ПОПАДАНИЯ, а не в центр масс: попал с краю — провернулось.
        rb.AddForceAtPosition(push * _kickImpulse, point, ForceMode.Impulse);

        // Добавочный момент вокруг вертикали. Знак — от того, с какой стороны
        // от центра пришёлся удар: пнул слева — крутит влево, справа — вправо.
        // Так поведение совпадает с ожиданием, а не выглядит случайным.
        Vector3 toPoint = point - rb.worldCenterOfMass;
        float side = Vector3.Dot(Vector3.Cross(dir, toPoint), Vector3.up);

        rb.AddTorque(Vector3.up * (Mathf.Sign(side) * _kickTorque), ForceMode.Impulse);
    }
}
