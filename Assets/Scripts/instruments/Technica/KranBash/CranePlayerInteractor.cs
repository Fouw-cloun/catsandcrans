using FishNet.Object;
using UnityEngine;

/// <summary>H по крюку/точке зацепа; Shift+H по свободному крюку — подвесить себя.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInteractor), typeof(CraneRider))]
public sealed class CranePlayerInteractor : NetworkBehaviour
{
    [SerializeField] private KeyCode _interactKey = KeyCode.H;
    [SerializeField] private bool _showHint = true;
    private PlayerInteractor _interactor;
    private FirstPersonController _controller;
    private CraneRider _rider;
    private CraneHook _aimedHook;
    private CraneLiftPoint _aimedPoint;
    private float _nextRequest;
    private readonly RaycastHit[] _hits = new RaycastHit[32];

    private void Awake()
    {
        _interactor = GetComponent<PlayerInteractor>();
        _controller = GetComponent<FirstPersonController>();
        _rider = GetComponent<CraneRider>();
    }
    private void Update()
    {
        _aimedHook = null; _aimedPoint = null;
        if (!IsOwner || UiModal.AnyOpen || !_interactor.enabled || _rider.IsHanging ||
            !_controller.enabled || _controller.IsClimbing || _controller.CartSteering) return;
        if (_interactor.Hand != null && _interactor.Hand.HasItem) return;
        Transform eye = _interactor.AimOrigin;
        int count = Physics.RaycastNonAlloc(eye.position, eye.forward, _hits, _interactor.Reach, ~0, QueryTriggerInteraction.Collide);
        RaycastHit[] hits = _hits;
        if (count == _hits.Length) { hits = Physics.RaycastAll(eye.position, eye.forward, _interactor.Reach, ~0, QueryTriggerInteraction.Collide); count = hits.Length; }
        float closest = float.MaxValue; RaycastHit hit = new RaycastHit();
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (c.isTrigger && c.GetComponentInParent<CraneLiftPoint>() == null && c.GetComponentInParent<CraneHook>() == null) continue;
            if (hits[i].distance < closest) { closest = hits[i].distance; hit = hits[i]; }
        }
        if (hit.collider == null) return;
        _aimedPoint = hit.collider.GetComponentInParent<CraneLiftPoint>();
        _aimedHook = hit.collider.GetComponentInParent<CraneHook>();
        if (!Input.GetKeyDown(_interactKey)) return;
        bool self = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (_aimedHook != null) CmdUseHook(_aimedHook.NetworkObject, self);
        else if (_aimedPoint != null && _aimedPoint.Load != null)
            CmdUsePoint(_aimedPoint.Load.NetworkObject, _aimedPoint.Index);
    }
    private bool ServerCanReach(Vector3 point, Transform targetRoot)
    {
        if (Time.unscaledTime < _nextRequest || _rider.IsHanging) return false;
        if (targetRoot.gameObject.scene != gameObject.scene) return false;
        foreach (NetworkVehicle vehicle in FindObjectsByType<NetworkVehicle>(FindObjectsSortMode.None))
            if (vehicle.IsDriver(NetworkObject)) return false;
        if (_interactor.Hand != null && _interactor.Hand.HasItem) return false;
        // Сервер проверяет расстояние от тела: наклон локальной камеры не реплицируется.
        if (Vector3.Distance(transform.position, point) > _interactor.Reach + 1.5f) return false;
        Vector3 eye = transform.position + Vector3.up * 1.4f;
        Vector3 ray = point - eye;
        foreach (RaycastHit hit in Physics.RaycastAll(eye, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(targetRoot)) continue;
            return false;
        }
        _nextRequest = Time.unscaledTime + 0.25f;
        return true;
    }
    [ServerRpc]
    private void CmdUseHook(NetworkObject target, bool self)
    {
        if (target == null || !target.IsSpawned) return;
        CraneHook hook = target.GetComponent<CraneHook>();
        if (hook == null || !ServerCanReach(hook.LatchWorld, hook.transform)) return;
        if (self) hook.ServerAttachRider(_rider); else hook.ServerToggleNearest();
    }
    [ServerRpc]
    private void CmdUsePoint(NetworkObject target, int index)
    {
        if (target == null || !target.IsSpawned) return;
        CraneLoad load = target.GetComponent<CraneLoad>();
        if (load == null || index < 0 || index >= load.Points.Length) return;
        CraneLiftPoint point = load.Points[index];
        if (point == null || !ServerCanReach(point.WorldPoint, load.transform)) return;
        if (load.IsAttached)
        {
            if (load.Hook != null) load.Hook.ServerDetach();
            return;
        }
        CraneHook best = null; float distance = float.MaxValue;
        foreach (CraneHook hook in CraneHook.Active)
        {
            if (hook == null || !hook.IsSpawned || hook.Occupied) continue;
            float d = Vector3.Distance(hook.LatchWorld, point.WorldPoint);
            if (d <= point.WorldRadius && d < distance) { distance = d; best = hook; }
        }
        if (best != null) best.ServerAttach(point);
    }
    private void OnGUI()
    {
        if (!IsOwner || !_showHint || UiModal.AnyOpen) return;
        string hint = null;
        if (_rider.IsHanging) hint = "WASD — раскачиваться   |   Пробел — отпустить крюк";
        else if (_aimedHook != null) hint = _aimedHook.Occupied
            ? "H — отцепить груз" : "H — зацепить ближайшую точку   |   Shift+H — повиснуть";
        else if (_aimedPoint != null) hint = "H — зацепить / отцепить (подведи крюк к сфере)";
        if (hint != null) GUI.Box(new Rect(Screen.width * 0.5f - 280f, Screen.height - 84f, 560f, 30f), hint);
    }
}
