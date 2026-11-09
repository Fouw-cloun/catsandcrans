using UnityEngine;

// Put on a child Animator when the main dust receiver lives on the player root.
[AddComponentMenu("CatCooks/Dust and smoke/Animation event relay")]
public sealed class CatCooksDustAnimationRelay : MonoBehaviour
{
    public CatCooksDustPlayer receiver;
    void Awake() { if(receiver==null) receiver=GetComponentInParent<CatCooksDustPlayer>(); }
    public void DustFootstep(int foot) { if(receiver!=null) receiver.Footstep(foot); }
    public void DustJump() { if(receiver!=null) receiver.JumpDust(); }
    public void DustLand(float speed) { if(receiver!=null) receiver.LandingDust(speed); }
}
