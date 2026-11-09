using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>Метка на объекте со слитыми мешами: помнит, что было выключено при слиянии, чтобы «Снять слияние» вернуло ровно это.</summary>
    [DisallowMultipleComponent]
    public sealed class CityMergedMarker : MonoBehaviour
    {
        public GameObject Source;
        public List<MeshRenderer> Disabled = new List<MeshRenderer>();
        public List<GameObject> Hidden = new List<GameObject>();
        public int SourceRenderers;
        public int MergedMeshes;
    }
}
