using UnityEngine;

[DisallowMultipleComponent, AddComponentMenu("CatCooks/Dust and smoke/Surface")]
public sealed class CatCooksDustSurface : MonoBehaviour
{
    public CatCooksDustProfile profile;
    [Tooltip("0 = no dust on this surface, e.g. clean metal or wet ground.")]
    [Range(0, 2)] public float amount = 1;
}
