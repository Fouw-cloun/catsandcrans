using UnityEngine;

namespace CatCooks.FoodLab
{
    public enum FoodBehaviour { Fluid, Paste, Dough, Granular }
    [CreateAssetMenu(menuName = "CatCooks/Food Lab/Profile")]
    public sealed class CCFoodProfile : ScriptableObject
    {
        public string displayName = "Sauce";
        public FoodBehaviour behaviour;
        public Color body = new Color(.85f, .25f, .08f);
        public Color fleck = new Color(.22f, .38f, .12f);
        [Range(0, 1)] public float inclusions = .12f;
        [Range(0, 1)] public float roughness = .45f;
        [Range(0, 20)] public float viscosity = 3;
        [Range(0, 1)] public float cohesion = .1f;
        [Range(0, 1)] public float friction = .35f;
        [Tooltip("XPBD compliance: smaller = stiffer bonds. Not a measured food material.")]
        [Range(0, .02f)] public float compliance = .00005f;
        [Range(1.01f, 2)] public float yieldStretch = 1.15f;
        [Range(0, 15)] public float plasticity = 2;
        [Range(1.1f, 5)] public float tearStretch = 2.3f;
        [Tooltip("Only paste renews local bonds. Dough preserves its original topology.")]
        public bool healContacts;
        public bool UsesBonds => behaviour == FoodBehaviour.Paste || behaviour == FoodBehaviour.Dough;
    }
}
