using UnityEngine;

[DisallowMultipleComponent]
public sealed class CCBulldozerDriveCheck : MonoBehaviour
{
    public TrackedVehicle drive;
    private bool _visible = true;
    private readonly RaycastHit[] _hits = new RaycastHit[64];
    private string _report = "";
    private float _refresh;

    private void Update()
    {
        if (drive == null || !drive.LocalDriver) return;
        if (Input.GetKeyDown(KeyCode.F8))
        {
            _visible = !_visible;
            Debug.Log(GetReport(), drive);
        }
        if (_visible && Time.unscaledTime >= _refresh)
        {
            _refresh = Time.unscaledTime + .15f;
            _report = GetReport();
        }
    }

    private void OnGUI()
    {
        if (!_visible || drive == null || !drive.LocalDriver) return;
        GUI.Box(new Rect(12, 12, 650, 148), _report);
    }

    private string GetReport()
    {
        Rigidbody body = drive.GetComponent<Rigidbody>();
        if (body == null) return "02G • На корне нет Rigidbody";
        int nearby = 0;
        float distance = float.PositiveInfinity;
        Collider floor = null;
        Scan(drive.leftTrackPoints, body, ref nearby, ref distance, ref floor);
        Scan(drive.rightTrackPoints, body, ref nearby, ref distance, ref floor);
        string ground = floor != null
            ? $"{floor.name} • слой {LayerMask.LayerToName(floor.gameObject.layer)} ({floor.gameObject.layer}) • {distance:F2} м"
            : "не найдено до 3 м";
        return $"02G • F8: скрыть / записать показания в Console\n" +
            $"Газ {Input.GetAxisRaw("Vertical"):F1}   Руль {Input.GetAxisRaw("Horizontal"):F1}   Меню {UiModal.AnyOpen}   Тормоз {Input.GetKey(KeyCode.Space)}\n" +
            $"Owner {drive.IsOwner}   Kinematic {body.isKinematic}   Ограничения {body.constraints}   Скорость {body.linearVelocity.magnitude:F2} м/с\n" +
            $"Опоры {drive.GroundContacts}/8   Поверхность под лучами {nearby}/8   Луч {drive.restLength + drive.trackRadius:F2} м\n" +
            $"Под машиной: {ground}";
    }

    private void Scan(Transform[] points, Rigidbody body, ref int nearby, ref float distance, ref Collider floor)
    {
        if (points == null) return;
        foreach (var point in points)
        {
            if (point == null || !CCVehicleGround.Cast(drive.transform, body, point.position, -drive.transform.up,
                    3f, ~0, _hits, out RaycastHit hit)) continue;
            nearby++;
            if (hit.distance >= distance) continue;
            distance = hit.distance;
            floor = hit.collider;
        }
    }
}
