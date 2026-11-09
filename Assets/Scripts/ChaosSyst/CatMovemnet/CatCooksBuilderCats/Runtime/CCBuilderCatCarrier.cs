using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Builders
{
    // Add in the EDITOR to the player prefab root, beside PlayerInteractor/PlayerHand.
    // Owner-only RPC: the client requests an action, the server chooses and validates the result.
    [DefaultExecutionOrder(-50), DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInteractor),typeof(PlayerHand))]
    public sealed class CCBuilderCatCarrier : NetworkBehaviour
    {
        public KeyCode CarryKey = KeyCode.B;
        [Min(.5f)] public float Reach = 2.5f;
        public Vector3 CatOffset = new Vector3(0,.65f,.75f);
        public Vector3 CatEuler = new Vector3(0,180,0);
        public bool ShowHint = true;
        [Tooltip("Пол и препятствия; слой котов также должен попадать в маску прицела.")]
        public LayerMask InteractionMask = ~0;
        [Tooltip("Дополнительные обработчики ввода, например CarryGrabber. Восстанавливаются после отпускания.")]
        public MonoBehaviour[] AdditionalBlockedBehaviours = new MonoBehaviour[0];
        private readonly SyncVar<NetworkObject> _held = new(null);
        private PlayerInteractor _interactor;
        private PlayerHand _hand;
        private FirstPersonController _controller;
        private CCBuilderCat _serverCat, _aimed;
        private bool _blocked, _wasEnabled;
        private bool[] _extraWasEnabled;
        private MonoBehaviour[] _extraTargets;
        private float _nextRequest, _nextAim;
        private readonly RaycastHit[] _hits = new RaycastHit[32];
        public bool HasCat => _held.Value!=null;
        // Body-relative pose is shared by server and remote clients; no unreplicated camera pitch.
        public Vector3 CatPosition => transform.position+transform.rotation*CatOffset;
        public Quaternion CatRotation => transform.rotation*Quaternion.Euler(CatEuler);
        private void Awake()
        {
            _interactor=GetComponent<PlayerInteractor>();_hand=GetComponent<PlayerHand>();
            _controller=GetComponent<FirstPersonController>();
        }
        public override void OnStartNetwork()
        { base.OnStartNetwork();_held.OnChange+=HeldChanged; }
        public override void OnStartClient() { base.OnStartClient();ApplyInputBlock(); }
        private void HeldChanged(NetworkObject oldValue,NetworkObject newValue,bool asServer) { ApplyInputBlock(); }
        private void ApplyInputBlock()
        {
            if(!IsOwner || _interactor==null) return;
            if(HasCat && !_blocked)
            {
                _wasEnabled=_interactor.enabled;_interactor.enabled=false;_blocked=true;
                int count=AdditionalBlockedBehaviours==null ? 0 : AdditionalBlockedBehaviours.Length;
                _extraWasEnabled=new bool[count];
                _extraTargets=new MonoBehaviour[count];
                for(int i=0;i<count;i++)
                {
                    MonoBehaviour b=AdditionalBlockedBehaviours[i];
                    if(b==null || b==this || b==_interactor) continue;
                    bool duplicate=false;
                    for(int j=0;j<i;j++) if(_extraTargets[j]==b) { duplicate=true;break; }
                    if(duplicate) continue;
                    _extraTargets[i]=b;
                    _extraWasEnabled[i]=b.enabled;b.enabled=false;
                }
            }
            else if(!HasCat) RestoreInput();
        }
        private void RestoreInput()
        {
            if(_blocked && _interactor!=null) _interactor.enabled=_wasEnabled;
            if(_blocked && _extraWasEnabled!=null && _extraTargets!=null)
                for(int i=0;i<_extraTargets.Length;i++)
                {
                    MonoBehaviour b=_extraTargets[i];
                    if(b!=null && b!=this && b!=_interactor) b.enabled=_extraWasEnabled[i];
                }
            _extraWasEnabled=null;_extraTargets=null;
            _blocked=false;
        }
        private bool LocalCanTake()
        {
            return _interactor!=null && _interactor.enabled && _hand!=null && !_hand.HasItem &&
                (_controller==null || (_controller.enabled && !_controller.IsClimbing && !_controller.CartSteering));
        }
        private void Update()
        {
            if(IsServerInitialized && _serverCat!=null)
            {
                if(!_serverCat.IsServerInitialized || _serverCat.Carrier!=NetworkObject)
                { _serverCat=null;_held.Value=null; }
                else if(_hand.HasItem) ReleaseServer(true); // handles a pre-existing/in-flight item pickup
            }
            else if(IsServerInitialized && _held.Value!=null) _held.Value=null;
            if(!IsOwner || !IsClientInitialized) return;
            ApplyInputBlock();
            if(UiModal.AnyOpen) { _aimed=null;return; }
            if(HasCat)
            { if(Input.GetKeyDown(CarryKey)) CmdDrop();return; }
            if(!LocalCanTake()) { _aimed=null;return; }
            if(Time.unscaledTime>=_nextAim || Input.GetKeyDown(CarryKey))
            { _nextAim=Time.unscaledTime+.08f;_aimed=FindAimedCat(); }
            if(Input.GetKeyDown(CarryKey) && _aimed!=null) CmdTake(_aimed.NetworkObject);
        }
        private CCBuilderCat FindAimedCat()
        {
            Transform eye=_interactor.AimOrigin;
            int n=Physics.RaycastNonAlloc(eye.position,eye.forward,_hits,Reach,InteractionMask,QueryTriggerInteraction.Collide);
            if(n==_hits.Length) return null; // crowded query fails closed, no per-frame allocation
            float nearest=float.MaxValue;CCBuilderCat result=null;
            for(int i=0;i<n;i++)
            {
                Collider c=_hits[i].collider;if(c==null || c.transform.IsChildOf(transform)) continue;
                CCBuilderCat cat=c.GetComponentInParent<CCBuilderCat>();
                if(c.isTrigger && cat==null) continue;
                if(_hits[i].distance<nearest) { nearest=_hits[i].distance;result=cat; }
            }
            return result!=null && !result.IsCarried ? result : null;
        }
        private bool ServerCanTake(CCBuilderCat cat)
        {
            if(cat==null || !cat.IsServerInitialized || !cat.isActiveAndEnabled || cat.IsCarried || HasCat || _hand.HasItem ||
                cat.gameObject.scene!=gameObject.scene || Vector3.Distance(transform.position,cat.transform.position)>Reach+.75f) return false;
            // Existing vehicle code has no registry: scan only on the explicit pickup request.
            foreach(NetworkVehicle vehicle in FindObjectsByType<NetworkVehicle>(FindObjectsSortMode.None))
                if(vehicle.IsDriver(NetworkObject)) return false;
            foreach(CooperativeCarry load in FindObjectsByType<CooperativeCarry>(FindObjectsSortMode.None))
                if(load.Holds(NetworkObject)) return false;
            CraneRider rider=GetComponent<CraneRider>();if(rider!=null && rider.IsHanging) return false;
            Vector3 eye=transform.position+Vector3.up*1.2f,delta=cat.transform.position+Vector3.up*.4f-eye;
            int n=Physics.RaycastNonAlloc(eye,delta.normalized,_hits,delta.magnitude,InteractionMask,QueryTriggerInteraction.Ignore);
            if(n==_hits.Length) return false;
            for(int i=0;i<n;i++)
            {
                Transform t=_hits[i].transform;
                if(t.IsChildOf(transform) || t.IsChildOf(cat.transform)) continue;
                return false;
            }
            return true;
        }
        [ServerRpc]
        private void CmdTake(NetworkObject target)
        {
            if(!isActiveAndEnabled) return;
            if(Time.unscaledTime<_nextRequest) return;_nextRequest=Time.unscaledTime+.25f;
            if(target==null || !target.IsSpawned) return;
            CCBuilderCat cat=target.GetComponent<CCBuilderCat>();
            if(!ServerCanTake(cat) || !cat.ServerTake(this)) return;
            _serverCat=cat;_held.Value=cat.NetworkObject;
        }
        [ServerRpc]
        private void CmdDrop()
        {
            if(Time.unscaledTime<_nextRequest) return;_nextRequest=Time.unscaledTime+.25f;
            ReleaseServer(false);
        }
        private void ReleaseServer(bool force)
        {
            if(!IsServerInitialized) return;
            if(_serverCat==null) { _held.Value=null;return; }
            if(_serverCat.Carrier!=NetworkObject || _serverCat.ServerRelease(transform.position+transform.forward*.95f,force))
            { _serverCat=null;_held.Value=null; }
        }
        private void OnGUI()
        {
            if(!IsOwner || !ShowHint || UiModal.AnyOpen) return;
            string hint=HasCat ? CarryKey+" — поставить кота на землю" : _aimed!=null ? CarryKey+" — взять кота на руки" : null;
            if(hint!=null) GUI.Box(new Rect(Screen.width*.5f-220,Screen.height-122,440,28),hint);
        }
        private void OnDisable() { ReleaseServer(true);RestoreInput();_aimed=null; }
        public override void OnStopServer() { ReleaseServer(true);base.OnStopServer(); }
        public override void OnStopNetwork()
        { _held.OnChange-=HeldChanged;RestoreInput();_serverCat=null;_aimed=null;base.OnStopNetwork(); }
    }
}
