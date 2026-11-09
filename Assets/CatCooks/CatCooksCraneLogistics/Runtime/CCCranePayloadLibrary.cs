using UnityEngine;
namespace CatCooks.CraneLogistics
{
    public sealed class CCCranePayloadLibrary:MonoBehaviour
    {
        [System.Serializable] public class Entry
        {
            public CCZoneResource Resource;
            public GameObject Visual;
            public Vector2 Footprint=new Vector2(4,3);
            public float Clearance=1.6f;
        }
        public Entry[] Entries=new Entry[0];
        int _last=int.MinValue;
        public Entry Find(int resource){foreach(var e in Entries)if(e!=null&&(int)e.Resource==resource)return e;return null;}
        public void Apply(int resource)
        {
            if(_last==resource)return;_last=resource;
            foreach(var e in Entries)if(e!=null&&e.Visual!=null)e.Visual.SetActive((int)e.Resource==resource);
        }
    }
}
