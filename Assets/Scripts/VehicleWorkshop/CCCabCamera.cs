using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// v11 (замена патча 10): камера кабины.
/// 1) При посадке берёт ВСЕ настройки камеры игрока: пост-обработку (цвета, тон, блум), сглаживание, тени, рендерер URP,
///    маску слоёв, слой объёмов, FOV, ближнюю/дальнюю плоскость — картинка за рулём такая же, как пешком.
/// 2) Мышь — оглядеться: −100…+100° влево-вправо, −60…+60° вверх-вниз, относительно кабины (поворот/наклон машины
///    двигает границы вместе с кабиной).
/// Камеру игрока, его контроллер и курсор НЕ трогает. При выходе камера кабины возвращается как была.
/// </summary>
[DisallowMultipleComponent]
public sealed class CCCabCamera : MonoBehaviour
{
    public float yawLimit = 100f;
    public float pitchLimit = 60f;
    [Tooltip("Множитель к чувствительности мыши из настроек игры")]
    public float sensitivity = 1f;

    private Transform _frame;
    private Quaternion _neutral, _restLocal;
    private float _yaw, _pitch;
    private bool _running, _look;

    public static CCCabCamera Begin(GameObject cabRoot, Camera player, Transform frame, bool mouseLook)
    {
        if (cabRoot == null) return null;
        Camera cab = cabRoot.GetComponentInChildren<Camera>(true);
        if (cab == null) return null;
        var c = cab.GetComponent<CCCabCamera>();
        if (c == null) c = cab.gameObject.AddComponent<CCCabCamera>();
        if (player != null && player != cab) CopyLook(cab, player);
        if (c._running) return c;                                       // хост может получить посадку дважды
        Transform t = cab.transform;
        c._restLocal = t.localRotation;
        c._frame = frame != null ? frame : (t.parent != null ? t.parent : t);
        c._neutral = Quaternion.Inverse(c._frame.rotation) * t.rotation;
        c._yaw = 0f; c._pitch = 0f;
        c._look = mouseLook;
        c._running = true;
        c.enabled = true;
        return c;
    }

    public void End()
    {
        if (!_running) return;
        _running = false;
        transform.localRotation = _restLocal;
    }

    private void OnDisable() => End();

    private void LateUpdate()
    {
        if (!_running || !_look) return;
        if (_frame == null) { End(); return; }
        if (!UiModal.AnyOpen && Cursor.lockState == CursorLockMode.Locked)
        {
            float gain = sensitivity * GameSettings.MouseSensitivity;
            _yaw = Mathf.Clamp(_yaw + Input.GetAxisRaw("Mouse X") * gain, -yawLimit, yawLimit);
            _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * gain, -pitchLimit, pitchLimit);
        }
        transform.rotation = _frame.rotation * _neutral * Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private static FieldInfo _rendererIndex;

    /// <summary>Картинка как у камеры игрока (без её положения и без её стека оверлеев).</summary>
    private static void CopyLook(Camera cab, Camera player)
    {
        cab.clearFlags = player.clearFlags;
        cab.backgroundColor = player.backgroundColor;
        cab.cullingMask = player.cullingMask;
        cab.fieldOfView = player.fieldOfView;
        cab.nearClipPlane = Mathf.Min(player.nearClipPlane, .05f);       // в тесной кабине не резать стойки у лица
        cab.farClipPlane = player.farClipPlane;
        cab.allowHDR = player.allowHDR;
        cab.allowMSAA = player.allowMSAA;
        cab.useOcclusionCulling = player.useOcclusionCulling;
        cab.depth = player.depth;

        UniversalAdditionalCameraData src = player.GetUniversalAdditionalCameraData();
        UniversalAdditionalCameraData dst = cab.GetUniversalAdditionalCameraData();
        if (src == null || dst == null) return;
        dst.renderType = CameraRenderType.Base;
        dst.renderPostProcessing = src.renderPostProcessing;
        dst.antialiasing = src.antialiasing;
        dst.antialiasingQuality = src.antialiasingQuality;
        dst.stopNaN = src.stopNaN;
        dst.dithering = src.dithering;
        dst.renderShadows = src.renderShadows;
        dst.volumeLayerMask = src.volumeLayerMask;
        dst.volumeTrigger = null;                                      // объёмы — по положению камеры кабины
        dst.requiresColorOption = src.requiresColorOption;
        dst.requiresDepthOption = src.requiresDepthOption;
        if (_rendererIndex == null)
            _rendererIndex = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        if (_rendererIndex != null) dst.SetRenderer((int)_rendererIndex.GetValue(src));
    }
}
