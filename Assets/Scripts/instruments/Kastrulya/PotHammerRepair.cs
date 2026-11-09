using FishNet.Object;
using UnityEngine;

/// <summary>
/// КУВАЛДОЙ — ВЫПРАВИТЬ КАСТРЮЛЮ. Мост между новой кувалдой и старой
/// системой вмятин (DeformableMesh): каждый удар по мятой кастрюле — шаг
/// починки формы. Когда и форма выправлена, и дыры заварены — кастрюля (CookwarePressure / PotThermal)
/// объявляет себя годной.
///
/// Вешается на корень кастрюли рядом с DeformableMesh и CookwarePressure.
/// Старый молоток (HammerItem) работает как раньше — это только для кувалды.
/// </summary>
public sealed class PotHammerRepair : NetworkBehaviour, CatCooks.Food.Tools.IHammerTarget
{
    [SerializeField] private bool _log = true;

    private DeformableMesh _shape;
    private CatCooks.Food.IPotBody _thermal;

    private void Awake()
    {
        _shape = GetComponent<DeformableMesh>();
        if (_shape == null) _shape = GetComponentInChildren<DeformableMesh>();
        _thermal = GetComponent<CatCooks.Food.IPotBody>();
    }

    public void ServerHammerHit(Vector3 point, Vector3 dir)
    {
        if (!base.IsServerInitialized || _shape == null) return;
        bool changed = _shape.ServerHammerHit();
        RpcClang(point);
        if (_log) Debug.Log($"[Кастрюля] удар кувалдой{(changed ? " — форма выправлена" : "")}", this);
        if (_thermal != null) _thermal.ServerCheckFullyRepaired();
    }

    [ObserversRpc]
    private void RpcClang(Vector3 point)
    {
        CatCooks.Food.CCRuntimeParticles.Burst(point, Vector3.up, new Color(1f, 0.85f, 0.4f), Color.white, 14,
                                               CatCooks.Food.CCRuntimeParticles.Look.Chips.Scaled(1.5f));
    }
}
