using UnityEngine;

[DefaultExecutionOrder(160)]
public sealed class CCDumpTruckHydraulics : MonoBehaviour
{
    public Transform bottom, top;
    public Transform[] sections;
    private Quaternion[] _rotations;
    private Vector3[] _offsets;
    private Vector3 _axis;
    private float _length;
    private bool _bound;

    private void LateUpdate()
    {
        if (bottom == null || top == null || sections == null || sections.Length < 2) return;
        var frame = transform;
        Vector3 a = frame.InverseTransformPoint(bottom.position);
        Vector3 line = frame.InverseTransformPoint(top.position) - a;
        float length = line.magnitude;
        if (length < .0001f) return;
        if (!_bound)
        {
            _rotations = new Quaternion[sections.Length];
            _offsets = new Vector3[sections.Length];
            for (int i = 0; i < sections.Length; i++)
            {
                if (sections[i] == null) continue;
                _offsets[i] = frame.InverseTransformPoint(sections[i].position) - a;
                _rotations[i] = Quaternion.Inverse(frame.rotation) * sections[i].rotation;
            }
            _axis = line / length;
            _length = length;
            _bound = true;
        }
        Vector3 axis = line / length;
        Quaternion q = Quaternion.FromToRotation(_axis, axis);
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] == null) continue;
            float fraction = i / (float)(sections.Length - 1);
            Vector3 pos = a + q * _offsets[i] + axis * ((length - _length) * fraction);
            sections[i].SetPositionAndRotation(frame.TransformPoint(pos), frame.rotation * q * _rotations[i]);
        }
    }
}
