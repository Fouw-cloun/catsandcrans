using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Builders
{
    // Scene NetworkObject. The root represents a ground point, with a model child above it.
    // Held position is reconstructed from the cat socket; only transitions cross the network.
    [DefaultExecutionOrder(100), DisallowMultipleComponent, RequireComponent(typeof(NetworkObject))]
    public sealed class CCBuilderWorkItem : NetworkBehaviour
    {
        public CCBuilderItemKind Kind;
        public Transform ModelSlot;
        public CCBuilderMaterialRoute Route;
        [Range(0,1)] public int InitialEnd;
        [Min(0)] public int RouteSlot;
        [Range(1,4)] public int BundleCount=4;
        public int CurrentEnd => _state.Value.End;
        public bool CanServe(CCBuilderWorkstation station) => Route!=null
            ? Route.Ready && station.Route==Route && station.RouteEnd!=CurrentEnd
            : station.Route==null;
        public void SetEnd(int end) { if(IsServerInitialized) { State s=_state.Value;s.End=end;_state.Value=s; } }
        public Vector3 HeldPosition;
        public Vector3 HeldEuler;
        [Tooltip("Поворот лежащего предмета относительно его исходного положения.")]
        public Vector3 GroundEuler;
        public struct State
        {
            public bool Ready, Delivered;
            public int End;
            public NetworkObject Holder;
            public Vector3 Position;
            public Quaternion Rotation;
        }
        private readonly SyncVar<State> _state = new(new SyncTypeSettings(0f));
        private CCBuilderCat _reservedBy, _cachedHolder;
        private NetworkObject _cachedObject;
        private Vector3 _home;
        private Quaternion _homeRotation;
        private float _restockAt;
        private bool _groundApplied;
        private Vector3 _appliedPosition;
        private Quaternion _appliedRotation;
        public bool IsTool => Kind==CCBuilderItemKind.Shovel || Kind==CCBuilderItemKind.Hammer;
        public bool Available => IsServerInitialized && isActiveAndEnabled && _state.Value.Ready &&
            !_state.Value.Delivered && _state.Value.Holder==null && (_reservedBy==null || !_reservedBy.IsServerInitialized);
        public bool IsReservedBy(CCBuilderCat cat) => cat!=null && _reservedBy==cat && IsServerInitialized && isActiveAndEnabled;
        public override void OnStartServer()
        {
            base.OnStartServer();if(Route!=null && Route.Ready) transform.position=Route.Location(InitialEnd,RouteSlot);_home=transform.position;_homeRotation=transform.rotation;
            _reservedBy=null;_restockAt=0;
            _state.Value=new State { Ready=true,End=InitialEnd,Position=_home,Rotation=_homeRotation*Quaternion.Euler(GroundEuler) };
            ApplyGround(_state.Value);
        }
        public bool Reserve(CCBuilderCat cat)
        {
            if(!Available || cat==null || !cat.IsServerInitialized) return false;
            _reservedBy=cat;return true;
        }
        public bool Take(CCBuilderCat cat)
        {
            if(!IsReservedBy(cat) || _state.Value.Delivered || _state.Value.Holder!=null ||
                Vector3.Distance(cat.transform.position,_state.Value.Position)>1.25f) return false;
            State state=_state.Value;state.Holder=cat.NetworkObject;_state.Value=state;return true;
        }
        public bool HeldBy(CCBuilderCat cat) => cat!=null && _state.Value.Holder==cat.NetworkObject;
        public void Release(CCBuilderCat cat,Vector3 ground,bool delivered=false,float restockSeconds=0)
        {
            if(!IsServerInitialized || _reservedBy!=cat) return;
            if(cat==null) ground=_home;
            State state=_state.Value;
            if(state.Holder!=null)
            {
                state.Holder=null;state.Position=ground;state.Rotation=_homeRotation*Quaternion.Euler(GroundEuler);
                state.Delivered=delivered;_state.Value=state;
                ApplyGround(state);
                _restockAt=delivered && (Route!=null || restockSeconds>0) ? Time.time+(Route!=null ? Mathf.Max(.1f,Route.PickupDelay) : restockSeconds) : 0;
            }
            _reservedBy=null;
        }
        public void ServerMaintenance()
        {
            if(!IsServerInitialized) return;
            if(_reservedBy!=null && (!_reservedBy.IsServerInitialized || !_reservedBy.isActiveAndEnabled))
            { Release(_reservedBy,_home); }
            // A holder can disappear on disconnect/despawn before an orderly release.
            if(_state.Value.Holder!=null && (!_state.Value.Holder.IsSpawned || _reservedBy==null))
            {
                _state.Value=new State { Ready=true,End=InitialEnd,Position=_home,Rotation=_homeRotation*Quaternion.Euler(GroundEuler) };
                ApplyGround(_state.Value);
                _reservedBy=null;
            }
            if(_restockAt>0 && Time.time>=_restockAt)
            {
                _restockAt=0;
                if(Route!=null) { State s=_state.Value;s.Delivered=false;_state.Value=s;return; }
                _state.Value=new State { Ready=true,End=InitialEnd,Position=_home,Rotation=_homeRotation*Quaternion.Euler(GroundEuler) };
                ApplyGround(_state.Value);
            }
        }
        private void LateUpdate()
        {
            if(!IsServerInitialized && !IsClientInitialized) return;
            State state=_state.Value;if(!state.Ready) return;
            if(state.Holder==null) { ApplyGround(state);return; }
            _groundApplied=false;
            if(_cachedObject!=state.Holder || _cachedHolder==null)
            { _cachedObject=state.Holder;_cachedHolder=state.Holder.GetComponent<CCBuilderCat>(); }
            if(_cachedHolder==null) return;
            Transform socket=IsTool ? _cachedHolder.RightHandSocket : _cachedHolder.CarrySocket;
            if(socket==null) socket=_cachedHolder.transform;
            float swing=0;
            if(_cachedHolder.Activity==CCBuilderActivity.Digging) swing=Mathf.Sin(_cachedHolder.ActivityAge*4.5f)*35f;
            else if(_cachedHolder.Activity==CCBuilderActivity.Hammering) swing=Mathf.Sin(_cachedHolder.ActivityAge*8f)*45f;
            transform.SetPositionAndRotation(socket.TransformPoint(HeldPosition),socket.rotation*Quaternion.Euler(HeldEuler+Vector3.right*swing));
        }
        private void ApplyGround(State state)
        {
            if(_groundApplied && _appliedPosition==state.Position && _appliedRotation==state.Rotation) return;
            transform.SetPositionAndRotation(state.Position,state.Rotation);
            _appliedPosition=state.Position;_appliedRotation=state.Rotation;_groundApplied=true;
        }
        public override void OnStopServer() { _reservedBy=null;_restockAt=0;base.OnStopServer(); }
        public override void OnStopNetwork() { _cachedHolder=null;_cachedObject=null;_groundApplied=false;base.OnStopNetwork(); }
    }
}
