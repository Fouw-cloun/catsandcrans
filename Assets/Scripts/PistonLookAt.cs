using UnityEngine;

/// <summary>Cosmetic two-piece cylinder. Fixed-length parts telescope by moving their end mounts,
/// not by stretching the metal. Mount targets must be children of the two rigid links, never of a driven piston mesh.</summary>
[DefaultExecutionOrder(150)]
public class PistonLookAt : MonoBehaviour
{
    public Transform basePart,baseTarget,rodPart,rodTarget;
    public float baseOffsetX,rodOffsetX;
    public bool invertBase,invertRod;
    public bool smooth;
    public float smoothSpeed=12f;
    public bool showDebugLines;
    [Tooltip("Preserve the FBX bind rotation and roll. Turn off only for old manually tuned LookRotation setups.")]
    public bool preserveBindPose;
    private Quaternion _baseRest,_rodRest;
    private Vector3 _baseLine,_rodLine;
    private bool _bound;
    void OnEnable(){_bound=false;}
    public void Rebind(){_bound=false;}
    void LateUpdate()
    {
        if(basePart==null || baseTarget==null || rodPart==null || rodTarget==null)return;
        if(!_bound)
        {
            _baseRest=basePart.localRotation;_rodRest=rodPart.localRotation;
            _baseLine=LocalDirection(basePart,baseTarget.position-basePart.position);
            _rodLine=LocalDirection(rodPart,rodTarget.position-rodPart.position);
            if(_baseLine.sqrMagnitude<1e-8f || _rodLine.sqrMagnitude<1e-8f)return;
            _bound=true;
        }
        Aim(basePart,baseTarget,_baseRest,_baseLine,baseOffsetX,invertBase);
        Aim(rodPart,rodTarget,_rodRest,_rodLine,rodOffsetX,invertRod);
        if(showDebugLines){Debug.DrawLine(basePart.position,baseTarget.position,Color.green);Debug.DrawLine(rodPart.position,rodTarget.position,Color.cyan);}
    }
    static Vector3 LocalDirection(Transform t,Vector3 world)=>t.parent!=null?t.parent.InverseTransformDirection(world):world;
    void Aim(Transform part,Transform target,Quaternion rest,Vector3 restLine,float offset,bool invert)
    {
        Vector3 d=target.position-part.position;if(d.sqrMagnitude<1e-8f)return;
        Quaternion wanted;
        if(preserveBindPose)
            wanted=Quaternion.FromToRotation(restLine,LocalDirection(part,d))*rest;
        else
        {
            if(invert)d=-d;
            Vector3 up=Mathf.Abs(Vector3.Dot(d.normalized,Vector3.up))>.98f?Vector3.forward:Vector3.up;
            Quaternion world=Quaternion.LookRotation(d,up)*Quaternion.Euler(offset,0,0);
            wanted=part.parent!=null?Quaternion.Inverse(part.parent.rotation)*world:world;
        }
        part.localRotation=smooth?Quaternion.Slerp(part.localRotation,wanted,1f-Mathf.Exp(-smoothSpeed*Time.deltaTime)):wanted;
    }
}
