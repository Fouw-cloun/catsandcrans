using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class CCMobileHookGuide : NetworkBehaviour
{
    public CCMobileCraneWinch winch;
    public CraneOperatorSeat seat;
    public CraneState outriggers;
    public CraneHook hook;
    public Transform grip;

    private static readonly HashSet<CCMobileHookGuide> guides = new();
    private readonly SyncVar<NetworkObject> holder = new();
    private PlayerInteractor local, mutedInteractor, serverHand;
    private Collider[] ignoredPlayer, craneColliders;
    private string notice;
    private float noticeUntil, nextAimHint;
    private bool canClipHere;
    private Collider hookCollider;
    private RaycastHit[] aimHits = new RaycastHit[24];
    private Vector3 handOffset;
    private float nextSend, nextPenalty, nextSearch, nextAction;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        guides.Add(this);
        hookCollider = hook.GetComponent<Collider>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        hookCollider = hook.GetComponent<Collider>();
        // Scene hooks are independent network roots. A crane moved in the editor
        // must start with its hook at the boom, before the first solver step.
        var body = hook.Body;
        body.position = winch.upperBlock.position + Vector3.down * winch.Length;
        Vector3 forward = Vector3.ProjectOnPlane(winch.tower.forward, Vector3.up);
        if (forward.sqrMagnitude > .0001f) body.rotation = Quaternion.LookRotation(forward);
        if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        craneColliders = winch.GetComponentsInChildren<Collider>(true);
        foreach (var c in craneColliders)
            if (c && c.enabled && !c.isTrigger) Physics.IgnoreCollision(hookCollider, c, true);
    }

    public override void OnStopServer()
    {
        ReleaseHand();
        if (craneColliders != null)
            foreach (var c in craneColliders)
                if (c && hookCollider && c.enabled && !c.isTrigger) Physics.IgnoreCollision(hookCollider, c, false);
        craneColliders = null;
        base.OnStopServer();
    }

    public override void OnStopNetwork()
    {
        guides.Remove(this);
        RestoreInput();
        local = null;
        base.OnStopNetwork();
    }

    private static bool From(NetworkObject who, NetworkConnection sender) =>
        who && who.IsSpawned && sender != null && sender.IsActive && who.Owner == sender;

    private bool OnFoot(PlayerInteractor player) =>
        player && player.IsSpawned && player.gameObject.scene == gameObject.scene &&
        player.Hand && !player.Hand.HasItem &&
        player.TryGetComponent<CharacterController>(out var cc) && cc.enabled &&
        !(player.TryGetComponent<CraneRider>(out var rider) && rider.IsHanging);

    // The hand anchor may pitch with the owner's camera. Report its position as a
    // bounded offset from the networked player, so running does not leave the hook behind.
    private static Vector3 HandPosition(PlayerInteractor player) =>
        player.Hand && player.Hand.HandAnchor ? player.Hand.HandAnchor.position :
            player.transform.position + Vector3.up * 1.25f + player.transform.forward * .45f;

    private void Update()
    {
        if (!IsClientInitialized) return;
        if (!local || !local.IsOwner)
        {
            if (Time.unscaledTime < nextSearch) return;
            nextSearch = Time.unscaledTime + .5f;
            foreach (var p in FindObjectsByType<PlayerInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (p.IsSpawned && p.IsOwner && p.gameObject.scene == gameObject.scene) { local = p; break; }
            if (!local || !local.IsOwner) return;
        }
        bool holding = holder.Value == local.NetworkObject;
        if (holding && !mutedInteractor && local.enabled)
        {
            mutedInteractor = local;
            local.enabled = false;
        }
        if (!holding) RestoreInput();

        if (holding)
        {
            // Keep the target live even while the menu is open.
            if (Time.unscaledTime >= nextSend)
            {
                nextSend = Time.unscaledTime + .08f;
                CmdHand(local.NetworkObject, HandPosition(local) - local.transform.position);
            }
            if (UiModal.AnyOpen) return;
            if (Time.unscaledTime >= nextAimHint)
            {
                nextAimHint = Time.unscaledTime + .12f;
                canClipHere = AimSling(local, out var preview, out _) &&
                    preview.Contains(hook.LatchWorld, out var spine, out _) &&
                    ClearToBody(hook.LatchWorld, spine, preview, local.transform);
            }
            if (Input.GetKeyDown(KeyCode.V)) CmdRelease(local.NetworkObject);
            else if (Input.GetMouseButtonDown(1))
            {
                if (AimSling(local, out var sling, out var contact))
                    CmdClip(local.NetworkObject, sling.Load.NetworkObject, sling.transform.InverseTransformPoint(contact));
                else ShowNotice("Наведи прицел вдоль туши со стропами и подойди ближе.");
            }
            return;
        }
        if (UiModal.AnyOpen) return;
        if (seat && seat.LocalDriver)
        {
            if (Input.GetMouseButtonDown(0)) CmdCab(local.NetworkObject);
            return;
        }
        if (Input.GetKeyDown(KeyCode.V) && local.enabled && OnFoot(local) &&
            Aim(local, false, out var aimed) && aimed.collider.GetComponentInParent<CCMobileHookGuide>() == this)
            CmdGrip(local.NetworkObject);
    }

    private void RestoreInput()
    {
        if (mutedInteractor && mutedInteractor.TryGetComponent<CharacterController>(out var cc) && cc.enabled)
            mutedInteractor.enabled = true;
        mutedInteractor = null;
    }

    private bool Aim(PlayerInteractor player, bool ignoreHook, out RaycastHit hit)
    {
        Transform eye = player.AimOrigin;
        return FirstSolid(eye.position, eye.forward, player.Reach, player.transform, ignoreHook, out hit);
    }

    private bool FirstSolid(Vector3 origin, Vector3 direction, float range, Transform player, bool ignoreHook, out RaycastHit nearest)
    {
        nearest = default;
        float best = float.PositiveInfinity;
        var scene = gameObject.scene.GetPhysicsScene();
        int count;
        while ((count = scene.Raycast(origin, direction, aimHits, range, ~0, QueryTriggerInteraction.Ignore)) == aimHits.Length)
            System.Array.Resize(ref aimHits, aimHits.Length * 2);
        for (int i = 0; i < count; i++)
        {
            var hit = aimHits[i];
            if ((player && hit.transform.IsChildOf(player)) || (ignoreHook && hit.transform.IsChildOf(transform))) continue;
            if (hit.collider.gameObject.scene != gameObject.scene || hit.distance >= best) continue;
            nearest = hit;
            best = hit.distance;
        }
        return nearest.collider;
    }

    private bool CanReach(PlayerInteractor player, Vector3 point, Transform target, bool ignoreHook)
    {
        Vector3 eye = player.transform.position + Vector3.up * 1.35f;
        Vector3 delta = point - eye;
        if (!CraneMath.Finite(delta) || delta.magnitude > player.Reach + .8f) return false;
        if (delta.sqrMagnitude < .001f) return true;
        return !FirstSolid(eye, delta.normalized, delta.magnitude + .06f, player.transform, ignoreHook, out var hit)
            || hit.collider.transform.IsChildOf(target);
    }

    // Aim against the same volume used by the latch. Solids still occlude it.
    private bool AimSling(PlayerInteractor player, out CCCarcassSling selected, out Vector3 contact)
    {
        selected = null;
        contact = default;
        Transform eye = player.AimOrigin;
        var ray = new Ray(eye.position, eye.forward);
        float limit = player.Reach;
        CCCarcassSling direct = null;
        if (FirstSolid(ray.origin, ray.direction, limit, player.transform, true, out var solid))
        {
            limit = Mathf.Min(limit, solid.distance + .015f);
            direct = solid.collider.GetComponentInParent<CCCarcassSling>();
        }
        float nearest = float.PositiveInfinity;
        foreach (var load in CraneLoad.Active)
        {
            if (!load || !load.IsSpawned || load.IsAttached || load.gameObject.scene != gameObject.scene ||
                !load.TryGetComponent<CCCarcassSling>(out var sling) || !sling.point ||
                !sling.RayEntry(ray, limit, out float t) || (sling != direct && t >= nearest)) continue;
            Vector3 sample = ray.GetPoint(t);
            if (!sling.Contains(sample, out var spine, out _) ||
                !ClearToBody(eye.position, spine, sling, player.transform)) continue;
            selected = sling;
            contact = sample;
            nearest = t;
            if (sling == direct) break;
        }
        return selected;
    }

    private bool ClearToBody(Vector3 from, Vector3 to, CCCarcassSling sling, Transform player)
    {
        Vector3 delta = to - from;
        return delta.sqrMagnitude < .000001f ||
            !FirstSolid(from, delta.normalized, delta.magnitude, player, true, out var hit) ||
            hit.collider.transform.IsChildOf(sling.transform);
    }

    // Also used by the server joint builder, immediately before committing the attachment.
    public bool CanCapture(CCCarcassSling sling, out float distance)
    {
        distance = float.PositiveInfinity;
        if (!sling || sling.gameObject.scene != gameObject.scene ||
            !sling.Contains(hook.LatchWorld, out var spine, out distance)) return false;
        return ClearToBody(hook.LatchWorld, spine, sling, serverHand ? serverHand.transform : null);
    }

    [TargetRpc]
    private void TargetNotice(NetworkConnection recipient, string message) => ShowNotice(message);

    private void ShowNotice(string message)
    {
        notice = message;
        noticeUntil = Time.unscaledTime + 2.4f;
    }

    private void OnGUI()
    {
        if (!IsClientInitialized || !local || UiModal.AnyOpen) return;
        string hint = null;
        if (Time.unscaledTime < noticeUntil) hint = notice;
        else if (holder.Value == local.NetworkObject) hint = canClipHere
            ? "Туша в зоне крюка    ПКМ — закрепить стропы    V — отпустить"
            : "Подведи крюк к туше и наведи прицел    V — отпустить";
        else if (seat && seat.LocalDriver) hint = "R / G — лебёдка    ЛКМ — зацепить или отцепить тушу";
        if (hint != null) GUI.Box(new Rect(Screen.width * .5f - 235f, Screen.height - 79f, 470f, 26f), hint);
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdGrip(NetworkObject who, NetworkConnection sender = null)
    {
        if (!From(who, sender) || !outriggers || !outriggers.IsDeployed || holder.Value ||
            !who.TryGetComponent<PlayerInteractor>(out var player) || !OnFoot(player) ||
            !CanReach(player, grip.position, transform, false) || Time.unscaledTime < nextAction) return;
        if (hook.Occupied)
        {
            if (SuspendedCarcass()) hook.ServerDetach();
            nextAction = Time.unscaledTime + .25f;
            return;
        }
        foreach (var guide in guides)
            if (guide && guide.holder.Value == who) return;
        holder.Value = who;
        serverHand = player;
        handOffset = Vector3.ClampMagnitude(HandPosition(player) - player.transform.position, 2.8f);
        ignoredPlayer = player.GetComponentsInChildren<Collider>();
        foreach (var c in ignoredPlayer) if (c && c.enabled) Physics.IgnoreCollision(hookCollider, c, true);
        hook.Body.WakeUp();
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdHand(NetworkObject who, Vector3 offset, NetworkConnection sender = null)
    {
        if (From(who, sender) && holder.Value == who && CraneMath.Finite(offset))
            handOffset = Vector3.ClampMagnitude(offset, 2.8f);
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdRelease(NetworkObject who, NetworkConnection sender = null)
    {
        if (From(who, sender) && holder.Value == who) ReleaseHand();
    }

    private void ReleaseHand()
    {
        if (ignoredPlayer != null)
            foreach (var c in ignoredPlayer) if (c && hookCollider) Physics.IgnoreCollision(hookCollider, c, false);
        ignoredPlayer = null;
        serverHand = null;
        holder.Value = null;
    }

    private void FixedUpdate()
    {
        if (!IsServerInitialized || (!serverHand && ignoredPlayer == null)) return;
        if (!OnFoot(serverHand) || !holder.Value || holder.Value.Owner == null || !holder.Value.Owner.IsActive ||
            !outriggers.IsDeployed || hook.Occupied) { ReleaseHand(); return; }

        Vector3 hand = serverHand.transform.position + handOffset;
        Vector3 wantedEye = hand + (hook.RopeAnchorWorld - grip.position);
        Vector3 fromTop = wantedEye - winch.upperBlock.position;
        // When the operator raises the hook beyond the player's grasp, release it.
        if (Mathf.Abs(fromTop.y) > winch.Length + .35f ||
            (hand - grip.position).sqrMagnitude > 12.25f) { ReleaseHand(); return; }
        Vector3 cappedEye = winch.upperBlock.position + Vector3.ClampMagnitude(fromTop, winch.Length);
        Vector3 error = cappedEye - hook.RopeAnchorWorld;
        var body = hook.Body;
        body.AddForce(Vector3.ClampMagnitude(75f * error - 15f * body.linearVelocity - Physics.gravity, 65f),
            ForceMode.Acceleration);
        body.AddTorque(Vector3.ClampMagnitude(Vector3.Cross(transform.up, Vector3.up) * 12f -
            body.angularVelocity * 4f, 16f), ForceMode.Acceleration);
        // Normal running is constrained on the owner. Correct large network divergence as well.
        if (Time.unscaledTime >= nextPenalty)
        {
            Vector3 correction = WalkCorrection(wantedEye);
            if (correction.sqrMagnitude > .35f * .35f &&
                serverHand.TryGetComponent<FirstPersonController>(out var controller))
                controller.ServerClampPosition(serverHand.transform.position + correction);
            nextPenalty = Time.unscaledTime + .2f;
        }
    }

    private Vector3 WalkCorrection(Vector3 wantedEye)
    {
        Vector3 delta = wantedEye - winch.upperBlock.position;
        // If the hook is being reeled out of reach, the server releases the
        // hand instead of teleporting the player underneath the boom.
        if (Mathf.Abs(delta.y) > winch.Length + .35f) return Vector3.zero;
        float horizontalLimit = Mathf.Sqrt(Mathf.Max(0f, winch.Length * winch.Length - delta.y * delta.y));
        delta.y = 0f;
        return Vector3.ClampMagnitude(delta, horizontalLimit) - delta;
    }

    private void LateUpdate()
    {
        if (!IsClientInitialized || !local || holder.Value != local.NetworkObject) return;
        Vector3 eye = HandPosition(local) + (hook.RopeAnchorWorld - grip.position);
        if (local.TryGetComponent<CharacterController>(out var cc) && cc.enabled)
        {
            Vector3 delta = WalkCorrection(eye);
            if (delta.sqrMagnitude > .000001f) cc.Move(delta);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdClip(NetworkObject who, NetworkObject target, Vector3 localContact,
        NetworkConnection sender = null)
    {
        if (!From(who, sender) || holder.Value != who || !OnFoot(serverHand) || !target ||
            !target.IsSpawned || target.gameObject.scene != gameObject.scene || !outriggers.IsDeployed ||
            !target.TryGetComponent<CCCarcassSling>(out var sling) || !sling.point ||
            !CraneMath.Finite(localContact)) return;

        Vector3 contact = sling.transform.TransformPoint(localContact);
        // Pitch is local to the client. Validate the selected contact, reach and
        // sight line on the server without a second precision ray against the mesh.
        Vector3 eye = serverHand.transform.position + Vector3.up * 1.35f;
        if (!sling.Contains(contact, out var spine, out _) ||
            !CanReach(serverHand, contact, sling.transform, true) ||
            !ClearToBody(eye, spine, sling, serverHand.transform))
        {
            TargetNotice(sender, "Подойди к видимой стороне туши и нажми ПКМ.");
            return;
        }
        if (!CanCapture(sling, out _))
        {
            TargetNotice(sender, "Подведи сам крюк ближе к туше — подойдёт любой конец.");
            return;
        }
        if (hook.ServerAttach(sling.point)) ReleaseHand();
        else TargetNotice(sender, "Проверь, что туша свободна, и дай крюку успокоиться.");
    }

    private bool SuspendedCarcass()
    {
        foreach (var load in CraneLoad.Active)
            if (load && load.IsAttached && load.Hook == hook && load.GetComponent<CCCarcassSling>()) return true;
        return false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdCab(NetworkObject who, NetworkConnection sender = null)
    {
        if (!From(who, sender) || !seat.IsDriver(who) || !outriggers.IsDeployed || holder.Value ||
            Time.unscaledTime < nextAction) return;
        nextAction = Time.unscaledTime + .25f;
        if (hook.Occupied)
        {
            if (SuspendedCarcass()) hook.ServerDetach();
            return;
        }
        CCCarcassSling best = null;
        float distance = float.PositiveInfinity;
        foreach (var load in CraneLoad.Active)
        {
            if (!load || !load.TryGetComponent<CCCarcassSling>(out var sling) || !sling.point ||
                !CanCapture(sling, out float d) || !load.CanAttach(hook)) continue;
            if (d < distance) { distance = d; best = sling; }
        }
        if (!best) TargetNotice(sender, "Подведи свободный крюк к любой части туши со стропами.");
        else if (!hook.ServerAttach(best.point)) TargetNotice(sender, "Дай крюку успокоиться и проверь зазор до туши.");
    }
}
