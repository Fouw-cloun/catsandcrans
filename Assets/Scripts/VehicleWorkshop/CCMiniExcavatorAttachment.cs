using UnityEngine;
using Cookware = CatCooks.Food.Cookware;

public enum CCMiniTool
{
    Bucket, Clamp, Grapple, Trencher, Mulcher, Forks, Hammer, Mixer
}

public sealed class CCMiniExcavatorAttachment : MonoBehaviour
{
    public Transform tip;
    public Transform drivePart;
    public Transform leftJaw;
    public Transform rightJaw;
    public CCMiniTool kind;
    [Min(.05f)] public float reach = .35f;
    public Cookware bucketContainer;
    public Collider[] bucketColliders;
    public Renderer[] bucketRenderers;

    private float _phase, _grip, _drive;
    private Quaternion _leftRest, _rightRest, _driveRest;
    private Vector3 _strokeRest;

    private void Awake()
    {
        if (leftJaw != null) _leftRest = leftJaw.localRotation;
        if (rightJaw != null) _rightRest = rightJaw.localRotation;
        if (drivePart == null) return;
        _driveRest = drivePart.localRotation;
        _strokeRest = drivePart.localPosition;
    }

    public void Show(bool visible)
    {
        if (kind != CCMiniTool.Bucket)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            return;
        }

        // The bucket's Cookware stays initialized on the parent NetworkObject.
        if (bucketContainer != null) bucketContainer.enabled = visible;
        foreach (var collider in bucketColliders)
            if (collider != null) collider.enabled = visible;
        foreach (var renderer in bucketRenderers)
            if (renderer != null) renderer.enabled = visible;
    }

    public void Animate(bool power, bool closed, float dt)
    {
        _grip = Mathf.MoveTowards(_grip, closed ? 1f : 0f, dt * 4.2f);
        if (leftJaw != null)
            leftJaw.localRotation = _leftRest * Quaternion.Euler(0, 0, 38f * _grip);
        if (rightJaw != null)
            rightJaw.localRotation = _rightRest * Quaternion.Euler(0, 0, -38f * _grip);
        if (drivePart == null) return;

        _drive = Mathf.MoveTowards(_drive, power ? 1f : 0f, 4f * dt);
        _phase = Mathf.Repeat(_phase + dt * _drive * (kind == CCMiniTool.Hammer ? 1440f : 500f), 360f);
        if (kind == CCMiniTool.Hammer)
        {
            float stroke = (.5f - .5f * Mathf.Cos(_phase * Mathf.Deg2Rad)) * .14f * _drive;
            drivePart.localPosition = _strokeRest + Vector3.down * stroke;
        }
        else
        {
            Vector3 axis = kind == CCMiniTool.Mixer ? Vector3.up : Vector3.right;
            drivePart.localRotation = _driveRest * Quaternion.AngleAxis(_phase, axis);
        }
    }
}
