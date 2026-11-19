using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.SiteTools
{
    [DefaultExecutionOrder(-65)]
    public sealed class CCInspectionHatch : NetworkBehaviour, IVehiclePart
    {
        public Transform hinge;
        public WaterJet coldJet;
        public Vector3Int gridCell;
        readonly SyncVar<bool> open = new(false);
        readonly SyncVar<bool> ruptured = new(false);
        public struct CoverPose
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }
        readonly SyncVar<CoverPose> coverPose = new();
        List<CCColdPipeValve> valves;
        Transform loose;
        Rigidbody body;
        float nextPose;
        public NetworkObject OwningNetworkObject => NetworkObject;
        public int PartId => 0;
        public bool CanBurst => coldJet != null && !ruptured.Value;

        public void ServerUse(PlayerInteractor player)
        {
            if (coldJet == null && IsServerInitialized && player != null &&
                Vector3.Distance(player.transform.position,transform.position)<4f) open.Value=!open.Value;
        }

        float Supply()
        {
            if (valves == null) valves = CCColdPipeValve.ConnectedTo(this);
            float pressure = 1f;
            foreach (var valve in valves)
                if (valve != null && valve.isActiveAndEnabled && valve.nut != null)
                    pressure = Mathf.Min(pressure,valve.nut.Open01);
            return pressure;
        }

        void MakeLooseCover(bool simulate)
        {
            var copy = Instantiate(hinge.gameObject,hinge.position,hinge.rotation);
            copy.name = "Сорванная крышка";
            global::UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy, gameObject.scene);
            loose = copy.transform;
            loose.localScale = hinge.lossyScale;
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            hinge.gameObject.SetActive(false);
            if (simulate){
                body = copy.AddComponent<Rigidbody>();
                body.mass=2.5f;
                body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                body.interpolation=RigidbodyInterpolation.Interpolate;
            } else {
                foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
            }
        }

        public void ServerBurst()
        {
            if (!IsServerInitialized || !CanBurst) return;
            float pressure=Supply();
            MakeLooseCover(true);
            Transform mount=hinge.parent;
            loose.position+=mount.up*.12f;
            var launch=(Vector3.up+mount.up*.55f+mount.right*.7f).normalized;
            body.AddForce(launch*Mathf.Lerp(1.7f,7.2f,pressure),ForceMode.VelocityChange);
            body.AddTorque(mount.forward*5.5f,ForceMode.VelocityChange);
            PublishPose();
            ruptured.Value=true;
        }

        void PublishPose()
        {
            coverPose.Value=new CoverPose{Position=loose.position,Rotation=loose.rotation};
        }

        void Update()
        {
            if (coldJet == null){
                if(hinge!=null) hinge.localRotation=Quaternion.RotateTowards(hinge.localRotation,
                    Quaternion.Euler(open.Value?-105:0,0,0),170f*Time.deltaTime);
                return;
            }
            if (!ruptured.Value || !(IsClientInitialized || IsServerInitialized)) return;
            if (IsServerInitialized){
                if (loose!=null && Time.unscaledTime>=nextPose){
                    nextPose=Time.unscaledTime+.075f;
                    var pose=coverPose.Value;
                    if ((pose.Position-loose.position).sqrMagnitude>.000001f || Quaternion.Angle(pose.Rotation,loose.rotation)>.1f) PublishPose();
                }
            } else {
                var pose=coverPose.Value;
                if (Quaternion.Dot(pose.Rotation,pose.Rotation)<.5f) return;
                if (loose==null){
                    MakeLooseCover(false);
                    loose.SetPositionAndRotation(pose.Position,pose.Rotation);
                } else {
                    float t=1f-Mathf.Exp(-20f*Time.deltaTime);
                    loose.SetPositionAndRotation(Vector3.Lerp(loose.position,pose.Position,t),Quaternion.Slerp(loose.rotation,pose.Rotation,t));
                }
            }
            // A slight horizontal component gives the returning stream a landing point beside the hatch.
            Vector3 side=Vector3.ProjectOnPlane(hinge.parent.up,Vector3.up);
            if (side.sqrMagnitude<.001f) side=Vector3.ProjectOnPlane(hinge.parent.right,Vector3.up);
            coldJet.transform.rotation=Quaternion.LookRotation((Vector3.up+side.normalized*.18f).normalized);
            if (!coldJet.gameObject.activeSelf) coldJet.gameObject.SetActive(true);
            coldJet.SetExternalFlow(true,Supply());
        }

        public override void OnStopNetwork()
        {
            if (loose!=null) Destroy(loose.gameObject);
            loose=null;
            body=null;
            if (hinge!=null) hinge.gameObject.SetActive(true);
            if (coldJet!=null){coldJet.SetExternalFlow(false,0f);coldJet.gameObject.SetActive(false);}
            valves=null;
            base.OnStopNetwork();
        }
    }
}
