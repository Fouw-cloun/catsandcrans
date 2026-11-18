using CatCooks.Food;
using FishNet.Object.Synchronizing;
using UnityEngine;

public sealed class CCConcreteTruckVehicle : WheeledVehicle
{
    public Transform drumPivot;
    public Cookware cookware;
    [Min(0)] public float drumSpeed = 40;
    [Min(0)] public float spinAcceleration = 30;
    public Transform mixLever, dischargeLever, chutePivot, chuteOutlet;
    public LiquidPourSource stream;
    [Min(0)] public float flowSpeed = 1.8f;
    [Range(0, 70)] public float chuteArc = 40f;
    [Min(.25f)] public float portionsPerSecond = 6;
    public LiquidPuddle puddlePrefab;

    private readonly SyncVar<int> _direction = new(0);
    private readonly SyncVar<int> _chuteStep = new(0);
    private readonly SyncVar<bool> _pouring = new(false);
    private readonly RaycastHit[] _hits = new RaycastHit[32];
    private Quaternion _drumRest, _chuteRest, _mixRest, _dischargeRest;
    private bool _bound;
    private float _angle, _speed, _chuteAngle, _mixAngle, _dischargeAngle;
    private float _nextUse, _pourBudget, _lastPour;

    public void ServerUseRearPart(CCConcreteTruckRearPart.Action action)
    {
        if (!IsServerInitialized || Time.time < _nextUse) return;
        _nextUse = Time.time + .15f;
        if (action == CCConcreteTruckRearPart.Action.AimChute)
        {
            _chuteStep.Value = (_chuteStep.Value + 1) % 3;
            return;
        }
        // Either lever returns a running drum to neutral. Press again to start in that direction.
        _direction.Value = _direction.Value != 0 ? 0 :
            action == CCConcreteTruckRearPart.Action.Mix ? 1 : -1;
    }

    protected override void ApplyVisuals()
    {
        base.ApplyVisuals();
        if (!_bound)
        {
            if (drumPivot != null) _drumRest = drumPivot.localRotation;
            if (chutePivot != null) _chuteRest = chutePivot.localRotation;
            if (mixLever != null) _mixRest = mixLever.localRotation;
            if (dischargeLever != null) _dischargeRest = dischargeLever.localRotation;
            _bound = true;
        }

        float dt = Mathf.Min(Time.deltaTime, .1f);
        _speed = Mathf.MoveTowards(_speed, _direction.Value * drumSpeed, spinAcceleration * dt);
        _angle = Mathf.Repeat(_angle + _speed * dt, 360);
        if (drumPivot != null)
            drumPivot.localRotation = _drumRest * Quaternion.AngleAxis(_angle, Vector3.forward);

        float target = _chuteStep.Value == 1 ? -chuteArc : _chuteStep.Value == 2 ? chuteArc : 0;
        _chuteAngle = Mathf.MoveTowards(_chuteAngle, target, 75f * dt);
        if (chutePivot != null)
            chutePivot.localRotation = _chuteRest * Quaternion.AngleAxis(_chuteAngle, Vector3.up);
        AnimateLever(mixLever, _mixRest, ref _mixAngle, _direction.Value > 0, dt);
        AnimateLever(dischargeLever, _dischargeRest, ref _dischargeAngle, _direction.Value < 0, dt);

        if (IsServerInitialized && cookware != null)
        {
            if (_direction.Value > 0 && _speed > 5)
            {
                cookware.ServerAgitate(.5f);
                if (!CookRecipeBook.ServerProcessAny(cookware, dt, CookStepKind.Mix, CookStepKind.Whip))
                    ChocolateWork.ServerMix(cookware, dt);
            }
            TickDischarge(dt);
        }
        if (stream != null)
        {
            stream.Flow = _pouring.Value ? 1 : 0;
            var visual = cookware != null ? cookware.GetComponentInChildren<CookwareLiquidVisual>() : null;
            if (visual != null) stream.LiquidColor = visual.LiquidColor;
        }
    }

    private static void AnimateLever(Transform lever, Quaternion rest, ref float angle, bool active, float dt)
    {
        if (lever == null) return;
        angle = Mathf.MoveTowards(angle, active ? 22 : 0, dt * 135);
        lever.localRotation = rest * Quaternion.AngleAxis(angle, Vector3.forward);
    }

    private void TickDischarge(float dt)
    {
        if (_direction.Value != -1 || _speed >= -5 || chuteOutlet == null || cookware.IsEmpty)
        {
            _pourBudget = 0;
            _pouring.Value = false;
            return;
        }

        _pourBudget = Mathf.Min(2, _pourBudget + dt * portionsPerSecond);
        while (_pourBudget >= 1)
        {
            _pourBudget--;
            if (!TryTransfer()) break;
            _lastPour = Time.time;
            _pouring.Value = true;
        }
        if (_pouring.Value && Time.time - _lastPour > Mathf.Max(.25f, 1.25f / portionsPerSecond))
            _pouring.Value = false;
    }

    private bool TryTransfer()
    {
        if (cookware.ViewCount == 0 || FoodDatabase.Instance == null) return false;
        var db = FoodDatabase.Instance;
        int i = cookware.ViewCount - 1;
        bool hasSolvent = cookware.ServerHasSolvent();
        while (i >= 0)
        {
            BagView view = cookware.GetView(i);
            if (!hasSolvent || !Cookware.DissolvesIn(db.Get(view.Product), view.State)) break;
            i--;
        }
        if (i < 0) return false;
        BagView top = cookware.GetView(i);
        if (!FoodHeapLook.IsLiquid(db.Get(top.Product), top.State)) return false;
        if (!FindLanding(out RaycastHit hit, out Cookware target)) return false;

        LiquidPuddle puddle = puddlePrefab != null ? puddlePrefab : LiquidPuddle.DefaultPrefab;
        if (target == null && puddle == null) return false;
        if (target != null && target.IsFull && !FoodUnits.IsAdditive(db.Get(top.Product), top.State))
            return false;

        int taken = cookware.ServerTakeTop(1, out ushort product, out var state, out byte cook);
        if (taken <= 0) return false;
        bool accepted = false;
        if (product == top.Product && state == top.State)
        {
            if (target != null) accepted = target.ServerAddContent(product, state, taken, cook, false) == taken;
            else accepted = LiquidPuddle.ServerAddAt(puddle, product, hit.point, taken) != null;
        }
        if (!accepted) cookware.ServerAddPinch(product, state, taken, cook, false);
        return accepted;
    }

    private bool FindLanding(out RaycastHit landing, out Cookware receiver)
    {
        landing = default;
        receiver = null;
        Vector3 start = chuteOutlet.position;
        Vector3 velocity = chuteOutlet.forward * (flowSpeed * 1.2f);
        Vector3 previous = start;
        const float step = .06f;
        for (int j = 1; j <= 44; j++)
        {
            float t = j * step;
            Vector3 point = start + velocity * t + .5f * Physics.gravity * t * t;
            Vector3 segment = point - previous;
            float length = segment.magnitude;
            if (length < .0001f) continue;
            int count = Physics.RaycastNonAlloc(previous, segment / length, _hits, length, ~0, QueryTriggerInteraction.Collide);
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || hit.transform.IsChildOf(transform) || hit.distance >= closest) continue;
                var target = hit.collider.GetComponentInParent<Cookware>();
                if (hit.collider.isTrigger && target == null) continue;
                closest = hit.distance;
                landing = hit;
                receiver = target;
            }
            if (landing.collider != null)
                return receiver != null || Vector3.Dot(landing.normal, Vector3.up) > .4f;
            previous = point;
        }
        return false;
    }
}
