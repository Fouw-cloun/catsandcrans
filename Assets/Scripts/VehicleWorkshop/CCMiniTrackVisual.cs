using UnityEngine;

public sealed class CCMiniTrackVisual : MonoBehaviour
{
    public Transform[] leftTreads, rightTreads;
    private const float HalfRun = .76f;
    private const float Radius = .218f;
    private const float Arc = Mathf.PI * Radius;
    private const float Run = 2f * HalfRun;
    private const float Loop = 2f * (Run + Arc);

    private Vector3 _previousPosition, _previousForward;
    private float _leftPhase, _rightPhase;

    private void OnEnable()
    {
        _previousPosition = transform.position;
        _previousForward = transform.forward;
    }

    private void LateUpdate()
    {
        Vector3 position = transform.position;
        Vector3 forward = transform.forward;
        Vector3 delta = position - _previousPosition;
        float travel = Vector3.Dot(delta, forward);
        float turn = Vector3.SignedAngle(_previousForward, forward, transform.up) * Mathf.Deg2Rad;
        _previousPosition = position;
        _previousForward = forward;
        if (delta.sqrMagnitude > 2.25f || (Mathf.Abs(travel) < .0002f && Mathf.Abs(turn) < .0002f)) return;
        _leftPhase = Mathf.Repeat(_leftPhase + travel + turn * .61f, Loop);
        _rightPhase = Mathf.Repeat(_rightPhase + travel - turn * .61f, Loop);
        Draw(leftTreads, _leftPhase);
        Draw(rightTreads, _rightPhase);
    }

    private static void Draw(Transform[] treads, float phase)
    {
        if (treads == null) return;
        for (int i = 0; i < treads.Length; i++)
        {
            float path = Mathf.Repeat(phase + Loop * i / treads.Length, Loop);
            float y, z, angle;
            if (path < Run)
            {
                y = Radius;
                z = path - HalfRun;
                angle = 0f;
            }
            else if (path < Run + Arc)
            {
                float t = (path - Run) / Radius;
                y = Radius * Mathf.Cos(t);
                z = HalfRun + Radius * Mathf.Sin(t);
                angle = t * Mathf.Rad2Deg;
            }
            else if (path < 2f * Run + Arc)
            {
                y = -Radius;
                z = HalfRun - (path - Run - Arc);
                angle = 180f;
            }
            else
            {
                float t = (path - 2f * Run - Arc) / Radius;
                y = -Radius * Mathf.Cos(t);
                z = -HalfRun - Radius * Mathf.Sin(t);
                angle = 180f + t * Mathf.Rad2Deg;
            }
            if (treads[i] == null) continue;
            treads[i].localPosition = new Vector3(0f, y, z);
            treads[i].localRotation = Quaternion.Euler(angle, 0f, 0f);
        }
    }
}
