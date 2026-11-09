using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CatCooks.FoodLab
{
    // RMB orbit; distance is controlled by the lab UI, leaving the wheel for tool height.
    public sealed class CCFoodLabCamera : MonoBehaviour
    {
        public Vector3 target=new Vector3(.2f,.6f,0);
        [Range(.55f,8)] public float distance=5.4f;
        public float yaw=-10,pitch=28;
        Vector3 smoothTarget;float smoothDistance;
        void OnEnable(){smoothTarget=target;smoothDistance=distance;}
        public void Focus(Vector3 centre){target=centre+Vector3.up*.09f;distance=1.05f;}
        public void Overview(){target=new Vector3(.2f,.6f,0);distance=5.4f;pitch=28;yaw=-10;}
        void LateUpdate()
        {
            Vector2 delta=Vector2.zero;bool orbit=false;
            #if ENABLE_INPUT_SYSTEM
            if(Mouse.current!=null){orbit=Mouse.current.rightButton.isPressed;delta=Mouse.current.delta.ReadValue()*.16f;}
            #elif ENABLE_LEGACY_INPUT_MANAGER
            orbit=Input.GetMouseButton(1);delta=new Vector2(Input.GetAxis("Mouse X"),Input.GetAxis("Mouse Y"))*3;
            #endif
            if(orbit){yaw+=delta.x;pitch=Mathf.Clamp(pitch-delta.y,8,85);}
            float blend=1-Mathf.Exp(-Time.unscaledDeltaTime*12);
            smoothTarget=Vector3.Lerp(smoothTarget,target,blend);smoothDistance=Mathf.Lerp(smoothDistance,distance,blend);
            Quaternion rotation=Quaternion.Euler(pitch,yaw,0);
            transform.position=smoothTarget+rotation*Vector3.back*smoothDistance;
            transform.rotation=rotation;
        }
    }
}
