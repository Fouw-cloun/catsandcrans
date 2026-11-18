using System.Collections.Generic;
using CatCooks.Food;
using FishNet.Object;
using UnityEngine;

public sealed class CCDumpTruckCargo : NetworkBehaviour
{
    public DumpTruckVehicle truck;
    public Cookware cookware;
    [Min(0)] public float response = 3;
    [Min(0)] public float maxAcceleration = 4;
    private readonly Collider[] _hits = new Collider[128];
    private readonly HashSet<Rigidbody> _bodies = new();
    private Vector3 _lastPosition, _velocity;
    private bool _ready;

    private void FixedUpdate()
    {
        if (!IsServerInitialized || truck == null || cookware == null) { _ready = false; return; }
        Vector3 position = truck.transform.position;
        if (!_ready) { _lastPosition = position; _velocity = Vector3.zero; _ready = true; return; }
        float dt = Time.fixedDeltaTime;
        Vector3 velocity = (position - _lastPosition) / dt;
        _lastPosition = position;
        if (velocity.sqrMagnitude > 1600) { _velocity = Vector3.zero; return; }
        _velocity = Vector3.Lerp(_velocity, velocity, 1 - Mathf.Exp(-8 * dt));
        var volume = cookware.InsideVolume;
        if (volume == null || truck.DischargeOpen) return;
        Bounds b = volume.bounds;
        int n = Physics.OverlapBoxNonAlloc(b.center, b.extents, _hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        _bodies.Clear();
        for (int i = 0; i < n; i++)
        {
            Rigidbody body = _hits[i].attachedRigidbody;
            if (body == null || body.isKinematic || !_bodies.Add(body)) continue;
            var food = body.GetComponent<FoodPiece>();
            if (food == null || food.IsHeld || food.IsConsumed) continue;
            Vector3 point = body.worldCenterOfMass;
            if ((volume.ClosestPoint(point) - point).sqrMagnitude > .0025f) continue;
            Vector3 correction = Vector3.ProjectOnPlane(_velocity - body.linearVelocity, Vector3.up) * response;
            body.AddForce(Vector3.ClampMagnitude(correction, maxAcceleration), ForceMode.Acceleration);
        }
    }
}
