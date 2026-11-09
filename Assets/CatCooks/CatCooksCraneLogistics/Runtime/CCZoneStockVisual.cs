using UnityEngine;
namespace CatCooks.CraneLogistics
{
    [DisallowMultipleComponent]
    public sealed class CCZoneStockVisual:MonoBehaviour
    {
        public CCWorkZone Zone;
        public Transform VisualRoot;
        public GameObject[] Slots=new GameObject[0];
        [Min(1)] public int UnitsPerSlot=1;
        int _last=int.MinValue;
        float _next;
        CCWorkTerritory _territory;
        void Awake(){if(Zone==null)Zone=GetComponent<CCWorkZone>();_territory=Zone!=null?Zone.Owner:null;}
        void OnEnable(){_next=0;_last=int.MinValue;}
        void Update()
        {
            if(Time.unscaledTime<_next)return;_next=Time.unscaledTime+.5f;
            if(Zone==null)return;
            int count=Zone.InitialStock;
            if(_territory!=null&&_territory.NetworkObject!=null&&(_territory.IsServerInitialized||_territory.IsClientInitialized))count=_territory.DisplayStock(Zone);
            Apply(count);
        }
        public void PreviewInitial(){if(!Application.isPlaying&&Zone!=null)Apply(Zone.InitialStock);}
        void Apply(int count)
        {
            if(count==_last)return;_last=count;
            int visible=Mathf.Clamp(Mathf.CeilToInt(count/(float)Mathf.Max(1,UnitsPerSlot)),0,Slots.Length);
            for(int i=0;i<Slots.Length;i++)if(Slots[i]!=null&&Slots[i].activeSelf!=(i<visible))Slots[i].SetActive(i<visible);
        }
    }
}
