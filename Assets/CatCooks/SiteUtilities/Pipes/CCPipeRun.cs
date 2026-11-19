using System.Collections.Generic;
using UnityEngine;
namespace CatCooks.SiteTools
{
    public sealed class CCPipeRun : MonoBehaviour
    {
        public List<Vector3> points=new List<Vector3>();
        [Min(.08f)] public float spacing=.56f;
        public Transform geometry;
    }
}
