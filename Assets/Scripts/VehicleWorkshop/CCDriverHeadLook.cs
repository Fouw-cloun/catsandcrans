using UnityEngine;

/// <summary>Local mouse look relative to the moving cabin or console.</summary>
[DisallowMultipleComponent]
public sealed class CCDriverHeadLook : MonoBehaviour
{
    private Transform reference;
    private Quaternion neutral, originalLocal;
    private Vector3 originalPosition;
    private FirstPersonController playerLook;
    private float yaw, pitch, yawLimit, pitchLimit, sensitivity, height;
    private bool running;

    public static CCDriverHeadLook Begin(Camera camera, Transform frame, Quaternion forward,
        float heightOffset = 0f, float yawRange = 100f, float pitchRange = 60f,
        float mouseSensitivity = 1f, FirstPersonController suppressLook = null, float startingPitch = 0f)
    {
        if (!camera || !frame || camera.transform == frame || frame.IsChildOf(camera.transform))
            return null;
        if (!camera.TryGetComponent<CCDriverHeadLook>(out var look))
            look = camera.gameObject.AddComponent<CCDriverHeadLook>();
        if (look.running) return look; // The host can receive the same seat transition twice.

        Transform eye = camera.transform;
        look.reference = frame;
        look.originalLocal = eye.localRotation;
        look.originalPosition = eye.localPosition;
        look.neutral = Quaternion.Inverse(frame.rotation) * forward;
        look.yawLimit = Mathf.Clamp(yawRange, 0f, 100f);
        look.pitchLimit = Mathf.Clamp(pitchRange, 0f, 89f);
        look.sensitivity = Mathf.Max(0f, mouseSensitivity);
        look.height = heightOffset;
        look.playerLook = suppressLook;
        look.yaw = 0f;
        look.pitch = Mathf.Clamp(startingPitch, -look.pitchLimit, look.pitchLimit);
        look.running = true;
        look.enabled = true;
        eye.rotation = forward * Quaternion.Euler(look.pitch, 0f, 0f);
        if (heightOffset != 0f) eye.position += frame.up * heightOffset;
        if (suppressLook) suppressLook.HoldLook();
        return look;
    }

    private void LateUpdate()
    {
        if (!running) return;
        if (!reference) { EndLook(); return; }
        // On the scissor lift the standing player's controller remains active.
        if (playerLook) playerLook.HoldLook();
        if (!UiModal.AnyOpen && Application.isFocused)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float gain = sensitivity * GameSettings.MouseSensitivity;
                yaw = Mathf.Clamp(yaw + Input.GetAxisRaw("Mouse X") * gain, -yawLimit, yawLimit);
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * gain, -pitchLimit, pitchLimit);
            }
            else if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        transform.rotation = reference.rotation * neutral * Quaternion.Euler(pitch, yaw, 0f);
    }

    public void EndLook()
    {
        Restore();
        enabled = false;
    }

    private void OnDisable() => Restore();

    private void Restore()
    {
        if (!running) return;
        running = false;
        transform.localRotation = originalLocal;
        if (height != 0f) transform.localPosition = originalPosition;
        playerLook = null;
        reference = null;
    }
}
