using CatCooks.Food;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public sealed class CCDumpTruckDischarge : NetworkBehaviour
{
    public DumpTruckVehicle truck;
    public Cookware cookware;
    public Transform outlet;
    public LiquidPuddle puddlePrefab;
    public LiquidPourSource stream;
    private readonly SyncVar<bool> _liquidFlow = new(false);
    public LayerMask landingMask = ~0;
    [Min(1)] public float portionsPerSecond = 18;
    private readonly RaycastHit[] _hits = new RaycastHit[64];
    private float _budget, _nextFx, _lastLiquid;

    private void Update()
    {
        if (IsServerInitialized)
        {
            if (truck == null || cookware == null || outlet == null ||
                !truck.DischargeOpen || cookware.ViewCount == 0)
            {
                _budget = 0;
                _liquidFlow.Value = false;
            }
            else
            {
                _budget = Mathf.Min(6, _budget + Time.deltaTime * portionsPerSecond);
                while (_budget >= 1)
                {
                    _budget--;
                    if (!Transfer()) { _budget = 0; break; }
                }
                if (_liquidFlow.Value && Time.time - _lastLiquid > .25f)
                    _liquidFlow.Value = false;
            }
        }
        if (stream != null) stream.Flow = _liquidFlow.Value ? 1 : 0;
    }

    private bool Transfer()
    {
        FoodDatabase db = FoodDatabase.Instance;
        if (db == null || cookware.ViewCount == 0 || !FindLanding(out var landing, out var target)) return false;
        int index = cookware.ViewCount - 1;
        if (cookware.ServerHasSolvent())
        {
            while (index >= 0)
            {
                BagView entry = cookware.GetView(index);
                if (!Cookware.DissolvesIn(db.Get(entry.Product), entry.State)) break;
                index--;
            }
        }
        if (index < 0) return false;
        var top = cookware.GetView(index);
        ProductDef def = db.Get(top.Product);
        bool liquid = FoodHeapLook.IsLiquid(def, top.State);
        FoodHeap heap = cookware.SpillHeapPrefab;
        LiquidPuddle puddle = puddlePrefab != null ? puddlePrefab : LiquidPuddle.DefaultPrefab;
        if (target == null && (liquid ? puddle == null : heap == null)) return false;
        if (target != null && target.IsFull && !FoodUnits.IsAdditive(def, top.State)) return false;

        int taken = cookware.ServerTakeTop(1, out ushort product, out var state, out byte cook);
        if (taken == 0) return false;
        bool accepted = false;
        if (product == top.Product && state == top.State)
        {
            if (target != null) accepted = target.ServerAddContent(product, state, taken, cook, false) == taken;
            else if (liquid) accepted = LiquidPuddle.ServerAddAt(puddle, product, landing.point, taken) != null;
            else accepted = FoodHeap.ServerDropOnFloor(heap, product, state, taken, cook, 50, landing.point, .45f, landingMask) != null;
        }
        if (!accepted) cookware.ServerAddPinch(product, state, taken, cook, false);
        else if (liquid)
        {
            _lastLiquid = Time.time;
            _liquidFlow.Value = true;
        }
        else if (Time.time >= _nextFx)
        {
            _nextFx = Time.time + .20f;
            PlaySolids(outlet.position, landing.point, -truck.transform.forward);
        }
        return accepted;
    }

    private bool FindLanding(out RaycastHit nearest, out Cookware target)
    {
        nearest = default;
        target = null;
        Vector3 rear = Vector3.ProjectOnPlane(-truck.transform.forward, Vector3.up).normalized;
        Vector3 start = outlet.position + rear * .32f + Vector3.up * .10f;
        int count = Physics.RaycastNonAlloc(start, Vector3.down, _hits, 30, landingMask, QueryTriggerInteraction.Collide);
        var hits = count == _hits.Length ? Physics.RaycastAll(start, Vector3.down, 30, landingMask, QueryTriggerInteraction.Collide) : _hits;
        if (hits != _hits) count = hits.Length;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(truck.transform) || hit.distance >= distance) continue;
            var candidate = hit.collider.GetComponentInParent<Cookware>();
            if (hit.collider.isTrigger && candidate == null) continue;
            if (candidate == null && Vector3.Dot(hit.normal, Vector3.up) < .4f) continue;
            if (hit.collider.GetComponentInParent<PlayerInteractor>() != null || hit.collider.GetComponentInParent<FoodPiece>() != null)
                continue;
            nearest = hit;
            target = candidate;
            distance = hit.distance;
        }
        return nearest.collider != null;
    }

    [ObserversRpc]
    private void PlaySolids(Vector3 start, Vector3 finish, Vector3 direction)
    {
        CCPowderFx.Fall(start, direction, finish, new Color(.72f, .48f, .30f), .35f);
    }
}
