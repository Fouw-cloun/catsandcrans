using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Автофокус для Depth of Field (Bokeh) в URP.
/// Пускает рейкаст из центра экрана (или в точку интереса) и плавно
/// подводит Focus Distance — еда в фокусе, фон в мягком боке.
/// Повесь на камеру. В сцене должен быть Volume с включённым Depth Of Field (Bokeh).
/// </summary>
[RequireComponent(typeof(Camera))]
public class AutoFocusDOF : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Volume с профилем, где есть Depth Of Field. Если пусто — найдёт первый глобальный Volume.")]
    public Volume volume;

    [Tooltip("Необязательная цель фокуса (например, блюдо в катсцене). Если задана — рейкаст не используется.")]
    public Transform focusTarget;

    [Header("Настройки")]
    [Tooltip("Слои, по которым ищем фокус (исключи частицы, триггеры и т.п.)")]
    public LayerMask focusLayers = ~0;

    [Tooltip("Максимальная дистанция фокуса, м")]
    public float maxFocusDistance = 30f;

    [Tooltip("Скорость перефокусировки. 5–8 = плавно-киношно, 12+ = резко")]
    public float focusSpeed = 6f;

    [Tooltip("Смещение точки прицела от центра экрана (0.5, 0.4 = чуть ниже центра — удобно для вида сверху)")]
    public Vector2 screenPoint = new Vector2(0.5f, 0.45f);

    private Camera cam;
    private DepthOfField dof;
    private float currentDistance = 5f;

    void Start()
    {
        cam = GetComponent<Camera>();

        if (volume == null)
        {
            foreach (var v in FindObjectsOfType<Volume>())
            {
                if (v.isGlobal) { volume = v; break; }
            }
        }

        if (volume != null && volume.profile != null)
            volume.profile.TryGet(out dof);

        if (dof == null)
            Debug.LogWarning("[AutoFocusDOF] Depth Of Field не найден в Volume Profile.");
    }

    void LateUpdate()
    {
        if (dof == null) return;

        float targetDistance = maxFocusDistance;

        if (focusTarget != null)
        {
            targetDistance = Vector3.Distance(cam.transform.position, focusTarget.position);
        }
        else
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(screenPoint.x, screenPoint.y, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, maxFocusDistance, focusLayers,
                                QueryTriggerInteraction.Ignore))
            {
                targetDistance = hit.distance;
            }
        }

        // Экспоненциальное сглаживание, независимое от FPS
        float t = 1f - Mathf.Exp(-focusSpeed * Time.deltaTime);
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, t);

        dof.focusDistance.Override(currentDistance);
    }
}
