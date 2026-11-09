using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CatCooks.FoodLab
{
    // Standalone laboratory UI; no dependency on the game's player, UI or recipe system.
    public sealed class CCFoodLabControls : MonoBehaviour
    {
        public CCFoodWorld world;
        public CCFoodSurface surface;
        public Camera viewCamera;
        public CCFoodLabCamera cameraRig;
        public LineRenderer marker;
        public Transform shovelVisual;
        public Transform[] spawnPoints;
        public CCFoodBoil[] boiling;
        int profile,station,mode;
        float massKg=3.5f,radius=.13f,height=.69f,power=1,nextCommand;
        bool held;Vector3 point;
        readonly string[] modes={"Тянуть","Лопата","Миксер","Надавить","Закрепить","Освободить"};
        readonly string[] stations={"Кастрюля 1","Кастрюля 2","Сковорода","Стол / тесто","Банка 1","Банка 2"};
        Rect panel=new Rect(12,12,315,690);
        Vector2 scroll;
        void Update()
        {
            if(world==null||viewCamera==null)return;
            Vector2 mouse=Vector2.zero;bool down=false,up=false,pressed=false;float wheel=0;
            #if ENABLE_INPUT_SYSTEM
            if(Mouse.current!=null){mouse=Mouse.current.position.ReadValue();down=Mouse.current.leftButton.wasPressedThisFrame;up=Mouse.current.leftButton.wasReleasedThisFrame;pressed=Mouse.current.leftButton.isPressed;wheel=Mouse.current.scroll.ReadValue().y/120;}
            #elif ENABLE_LEGACY_INPUT_MANAGER
            mouse=Input.mousePosition;down=Input.GetMouseButtonDown(0);up=Input.GetMouseButtonUp(0);pressed=Input.GetMouseButton(0);wheel=Input.mouseScrollDelta.y;
            #endif
            bool overUI=panel.Contains(new Vector2(mouse.x,Screen.height-mouse.y));
            if(!overUI)height=Mathf.Clamp(height+wheel*.035f,.42f,2.1f);
            Ray ray=viewCamera.ScreenPointToRay(mouse);Plane plane=new Plane(Vector3.up,new Vector3(0,height,0));
            if(plane.Raycast(ray,out float distance))point=ray.GetPoint(distance);
            if(marker!=null)
            {
                marker.enabled=!overUI;
                for(int i=0;i<48;i++){float a=i*Mathf.PI*2/48;marker.SetPosition(i,point+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}
            }
            if(shovelVisual!=null){shovelVisual.gameObject.SetActive(mode==1&&!overUI);shovelVisual.position=point-Vector3.up*(radius*.55f);shovelVisual.localScale=Vector3.one*(radius/.13f);}
            if(up||held&&!pressed){Send(FoodCommandKind.Release);held=false;}
            if(overUI||!world.domain.Contains(point))return;
            if(down)
            {
                if(mode<2){Send(mode==0?FoodCommandKind.Grab:FoodCommandKind.Scoop);held=true;}
                else if(mode>=4)Send(mode==4?FoodCommandKind.Pin:FoodCommandKind.Unpin);
            }
            if(pressed&&Time.unscaledTime>=nextCommand)
            {
                nextCommand=Time.unscaledTime+.05f;
                if(held)Send(FoodCommandKind.Drag);
                else if(mode==2)Send(FoodCommandKind.Stir);
                else if(mode==3)Send(FoodCommandKind.Push);
            }
        }
        void OnDisable(){if(world!=null&&held)Send(FoodCommandKind.Release);held=false;}
        void OnApplicationFocus(bool focus){if(!focus&&world!=null&&held){Send(FoodCommandKind.Release);held=false;}}
        void Send(FoodCommandKind kind)
        {world.Submit(new FoodCommand{kind=kind,point=point,radius=radius,strength=power,profile=profile,grams=Mathf.RoundToInt(massKg*1000)});}
        void OnGUI()
        {
            if(world==null||world.Profiles==null)return;
            panel.height=Mathf.Min(Screen.height-24,740);
            GUILayout.BeginArea(panel,GUI.skin.box);
            scroll=GUILayout.BeginScrollView(scroll);
            GUILayout.Label("CATCOOKS — FOOD LAB");
            GUILayout.Label(world.networkControlled?(world.simulate?"Сеть: сервер":"Сеть: клиент") : "Локальный стенд");
            string[] names=new string[world.Profiles.Length];for(int i=0;i<names.Length;i++)names[i]=world.Profiles[i].displayName;
            profile=GUILayout.SelectionGrid(profile,names,2);
            GUILayout.Space(5);station=GUILayout.SelectionGrid(station,stations,2);
            if(cameraRig!=null)
            {
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("К посуде"))cameraRig.Focus(spawnPoints[station].position);
                if(GUILayout.Button("Общий вид"))cameraRig.Overview();
                GUILayout.EndHorizontal();
                GUILayout.Label("Камера: ПКМ — вращение; дистанция");
                cameraRig.distance=GUILayout.HorizontalSlider(cameraRig.distance,.55f,7);
            }
            GUILayout.Label("Масса порции: "+massKg.ToString("F2")+" кг");massKg=GUILayout.HorizontalSlider(massKg,.35f,7);
            if(GUILayout.Button("Добавить порцию"))
            {
                point=spawnPoints[Mathf.Clamp(station,0,spawnPoints.Length-1)].position;Send(FoodCommandKind.Spawn);height=point.y;
            }
            mode=GUILayout.SelectionGrid(mode,modes,2);
            GUILayout.Label("Радиус инструмента: "+radius.ToString("F2")+" м");radius=GUILayout.HorizontalSlider(radius,.04f,.3f);
            GUILayout.Label("Высота: "+height.ToString("F2")+" м — колесо мыши");height=GUILayout.HorizontalSlider(height,.42f,1.8f);
            GUILayout.Label("Сила миксера / нажатия");power=GUILayout.HorizontalSlider(power,.2f,3);
            GUILayout.Label("ЛКМ: воздействие. Лопата: удерживай ЛКМ,\nподними колесом, перенеси и отпусти.\nТесто: закрепи край, затем тяни другой.");
            GUI.enabled=world.simulate;
            var preset=world.Profiles[profile];
            GUILayout.Label("Вязкость выбранного пресета: "+preset.viscosity.ToString("F1"));preset.viscosity=GUILayout.HorizontalSlider(preset.viscosity,0,20);
            if(preset.UsesBonds){GUILayout.Label("Предел разрыва: "+preset.tearStretch.ToString("F1"));preset.tearStretch=GUILayout.HorizontalSlider(preset.tearStretch,1.2f,4);}
            world.paused=GUILayout.Toggle(world.paused,"Пауза физики (сервер)");GUI.enabled=true;
            if(GUILayout.Button("Кипение выбранной посуды: вкл / выкл"))
                foreach(var fx in boiling)if(fx!=null&&fx.station==station)fx.amount=fx.amount>0?0:.65f;
            if(GUILayout.Button("Очистить стенд")){point=world.domain.center;Send(FoodCommandKind.Clear);}
            GUILayout.Label("Частицы: "+world.Solver.particles.Count+" / "+world.maxParticles+"    Масса: "+(world.Solver.TotalGrams/1000f).ToString("F3")+" кг");
            GUILayout.Label("Физика: "+world.lastStepMs.ToString("F2")+" ms   Поверхность: "+(surface==null?"—":surface.lastBuildMs.ToString("F2"))+" ms");
            GUILayout.Label("Связи: "+world.Solver.bonds.Count+"   Разрывы: "+world.Solver.brokenBonds);
            if(surface!=null&&surface.surfaceBudgetExceeded)GUILayout.Label("Превышен бюджет поверхности: показаны частицы");
            GUILayout.Label(world.status);GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void OnDrawGizmos()
        {if(!Application.isPlaying)return;Gizmos.color=mode==1?Color.yellow:Color.cyan;Gizmos.DrawWireSphere(point,radius);}
    }
}
