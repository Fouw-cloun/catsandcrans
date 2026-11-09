using System;
using System.Collections.Generic;
using UnityEngine;
namespace CatCooks.CityV2
{
    // Saved scene data only. No per-frame work or renderer.material allocations.
    [DisallowMultipleComponent]
    public sealed class CCCityV2MaterialBackup : MonoBehaviour
    {
        [Serializable] public sealed class Binding
        {
            public MeshRenderer renderer;
            public Material[] before, after;
        }
        public List<Binding> bindings = new List<Binding>();
    }
}
