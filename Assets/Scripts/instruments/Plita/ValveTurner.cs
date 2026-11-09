using FishNet.Object;
using UnityEngine;

/// <summary>
/// ОТКРЫТЬ И ЗАКРЫТЬ ВЕНТИЛЬ. Вешается на префаб ИГРОКА, рядом с PlayerInteractor.
///
/// Наводишься на ЛЮБОЕ место баллона, жмёшь клавишу — вентиль поворачивается.
/// Ещё раз — закрывается обратно.
///
/// Крутить мышью и целиться в маленький винтик не надо: баллон крупный, попасть
/// по нему легко, а вентиль доворачивается сам за пару секунд. Успеть закрыть,
/// пока не рвануло, при этом всё равно надо — поворот не мгновенный.
///
/// Прицел считает КЛИЕНТ — сервер не знает наклон камеры.
/// </summary>
public class ValveTurner : NetworkBehaviour
{
    [Header("Управление")]
    [Tooltip("Открыть и закрыть вентиль")]
    [SerializeField] private KeyCode _key = KeyCode.R;

    [Tooltip("Дальность, метры")]
    [SerializeField] private float _reach = 3f;

    [Tooltip("Слои, где искать баллоны. Можно оставить всё: компонент всё " +
             "равно проверяет наличие GasCylinder.")]
    [SerializeField] private LayerMask _mask = ~0;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud = true;

    private Camera _cam;
    private GasCylinder _hover;
    private bool _warnedNoCam;

    /// <summary>
    /// Проверка в Awake, а НЕ в OnStartClient.
    ///
    /// NetworkBehaviour без NetworkObject вообще не регистрируется в сети, и
    /// сетевые события у него не вызываются — предупреждение из OnStartClient
    /// никогда бы не появилось. «Нет никаких логов» — это и есть главный
    /// симптом того, что компонент висит не на том объекте.
    /// </summary>
    private void Awake()
    {
        if (GetComponentInParent<NetworkObject>() == null)
        {
            Debug.LogError($"[Вентиль] «{name}»: ни на этом объекте, ни выше нет " +
                           "NetworkObject. Компонент должен висеть на КОРНЕ префаба " +
                           "игрока, там же, где PlayerInteractor.", this);
            return;
        }

        Debug.Log($"[Вентиль] ValveTurner найден на «{name}», клавиша {_key}", this);
    }

    private void Update()
    {
        if (!base.IsOwner) return;

        if (_cam == null) _cam = Camera.main;
        if (_cam == null)
        {
            if (_warnedNoCam) return;
            _warnedNoCam = true;
            Debug.LogError("[Вентиль] не найдена Camera.main — у камеры игрока " +
                           "должен стоять тег MainCamera", this);
            return;
        }

        _hover = FindCylinder();

        if (_hover == null || !Input.GetKeyDown(_key)) return;

        CmdToggle(_hover.NetworkObject);
    }

    /// <summary>Баллон под прицелом. Ищем через GetComponentInParent, поэтому
    /// годится попадание в любую его часть — хоть в корпус, хоть в вентиль.</summary>
    private GasCylinder FindCylinder()
    {
        if (!Physics.Raycast(_cam.transform.position, _cam.transform.forward,
                             out RaycastHit hit, _reach, _mask,
                             QueryTriggerInteraction.Ignore))
            return null;

        return hit.collider.GetComponentInParent<GasCylinder>();
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdToggle(NetworkObject cylinder)
    {
        if (cylinder == null) return;

        // Проверка на вменяемость присланных данных: крутить вентиль на другом
        // конце карты нельзя.
        if (Vector3.Distance(cylinder.transform.position, transform.position) > _reach + 2f)
            return;

        GasCylinder c = cylinder.GetComponent<GasCylinder>();
        if (c != null) c.ServerToggleValve();
    }

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner || _hover == null) return;

        GUIStyle st = new GUIStyle(GUI.skin.box) { fontSize = 15 };
        st.normal.textColor = new Color(0.6f, 1f, 0.7f);

        string txt = $"{_key} — {(_hover.IsOpen ? "закрыть" : "открыть")} вентиль " +
                     $"({_hover.Open01 * 100f:0}%)";

        Vector2 size = st.CalcSize(new GUIContent(txt));
        GUI.Box(new Rect(Screen.width * 0.5f - size.x * 0.5f - 8f,
                         Screen.height * 0.5f + 40f, size.x + 16f, 26f), txt, st);
    }
}
