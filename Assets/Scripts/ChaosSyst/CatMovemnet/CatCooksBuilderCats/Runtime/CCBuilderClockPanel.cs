using UnityEngine;
namespace CatCooks.Builders
{
    [RequireComponent(typeof(GameClock))]
    public sealed class CCBuilderClockPanel : MonoBehaviour
    {
        private GameClock _clock;
        private bool _open,_pendingScrub;
        private float _scrub,_nextScrub;
        private static readonly float[] Rates={1,4,12,60};
        private void Awake() { _clock=GetComponent<GameClock>(); }
        private void Update()
        {
            if(!_clock.IsServerInitialized) { Close();return; }
            if(Input.GetKeyDown(KeyCode.F8)) { if(_open) Close();else { _open=true;_scrub=_clock.Hours;UiModal.Push(); } }
        }
        private void Close() { if(_open) { if(_pendingScrub && _clock.IsServerInitialized) _clock.ServerSetTime(_scrub);_pendingScrub=false;_open=false;UiModal.Pop(); } }
        private void OnDisable() { Close(); }
        private void OnGUI()
        {
            if(!_open || !_clock.IsServerInitialized) return;
            GUILayout.BeginArea(new Rect(20,20,370,300),GUI.skin.box);
            GUILayout.Label("Часы стройки · Host · F8 закрыть");
            GUILayout.Label("День "+_clock.Day+"  "+_clock.TimeString+"  x"+_clock.Rate);
            float next=GUILayout.HorizontalSlider(_scrub,0,23.99f);
            if(Mathf.Abs(next-_scrub)>.001f) { _pendingScrub=true;_scrub=next;if(Time.unscaledTime>=_nextScrub) { _clock.ServerSetTime(_scrub);_nextScrub=Time.unscaledTime+.1f; } }
            if(_pendingScrub && Event.current.rawType==EventType.MouseUp) { _clock.ServerSetTime(_scrub);_pendingScrub=false; }
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Утро")) Set(8);
            if(GUILayout.Button("День")) Set(12);
            if(GUILayout.Button("Вечер")) Set(19);
            if(GUILayout.Button("Ночь")) Set(23);
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();
            foreach(float rate in Rates) if(GUILayout.Button("x"+rate)) _clock.ServerSetRate(rate);
            GUILayout.EndHorizontal();
            if(GUILayout.Button(_clock.Paused?"Продолжить часы":"Пауза часов")) _clock.ServerSetPaused(!_clock.Paused);
            if(GUILayout.Button("+1 час")) { _clock.ServerSkipHours(1);_scrub=_clock.Hours; }
            if(GUILayout.Button("+1 день")) _clock.ServerSkipHours(24);
            GUILayout.Label("x1: сутки за "+_clock.DayLengthMinutes+" реальных минут. Физика не ускоряется.");
            GUILayout.EndArea();
        }
        private void Set(float h) { _clock.ServerSetTime(h);_scrub=h; }
    }
}
