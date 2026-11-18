using UnityEngine;
public interface ICCVehicleHelp { string ControlHelp { get; } }
[DisallowMultipleComponent]
public sealed class CCVehicleHelp : MonoBehaviour
{
    [TextArea(3,7)] public string text="WASD: ехать • Space: тормоз • F: выйти";
    private NetworkVehicle _vehicle;
    private bool _show=true;
    void Awake(){_vehicle=GetComponent<NetworkVehicle>();}
    void Update(){if(_vehicle!=null && _vehicle.LocalDriver && !UiModal.AnyOpen && Input.GetKeyDown(KeyCode.H))_show=!_show;}
    void OnGUI()
    {
        if(!_show || _vehicle==null || !_vehicle.LocalDriver || UiModal.AnyOpen)return;
        string s=_vehicle is ICCVehicleHelp e?e.ControlHelp:text;
        GUI.Box(new Rect(16,Screen.height-116,Mathf.Min(950,Screen.width-32),100),s);
    }
}
