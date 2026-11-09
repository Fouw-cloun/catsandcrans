using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class GameClock : NetworkBehaviour
{
    public static GameClock Instance { get; private set; }
    [Header("Сутки: реальных минут при скорости x1")]
    [SerializeField,Min(.1f)] private float _dayLengthMinutes=24;
    [SerializeField,Range(0,24)] private float _startHour=8;
    [Header("Дневная рабочая смена")]
    [SerializeField,Range(0,24)] private float _workStartHour=8,_workEndHour=19,_lunchHour=13;
    [Header("Сеть")]
    [SerializeField,Min(.1f)] private float _syncInterval=1;
    [SerializeField,Min(.01f)] private float _snapThresholdHours=.5f;
    public struct ClockState
    {
        public bool Ready,Paused;
        public double AbsoluteHours;
        public float Rate,DayMinutes;
        public uint Revision,Tick;
    }
    private readonly SyncVar<ClockState> _clock=new(new SyncTypeSettings(0f));
    private double _hours,_anchor;
    private float _receivedAt,_nextSync;
    private uint _revision;
    private bool _received;
    public float Hours => (float)(_hours%24.0);
    public int Day => (int)System.Math.Floor(_hours/24.0)+1;
    public int Hour => Mathf.FloorToInt(Hours);
    public int Minute => Mathf.Clamp(Mathf.FloorToInt((Hours-Hour)*60),0,59);
    public float Day01 => Hours/24;
    public string TimeString => string.Format("{0:00}:{1:00}",Hour,Minute);
    public float WorkStartHour => _workStartHour;
    public float WorkEndHour => _workEndHour;
    public float LunchHour => _lunchHour;
    public bool IsWorkTime => InRange(Hours,_workStartHour,_workEndHour);
    public float Work01 => Mathf.Clamp01((Hours-_workStartHour)/Mathf.Max(.01f,_workEndHour-_workStartHour));
    public float Lunch01 => Mathf.Clamp01((_lunchHour-_workStartHour)/Mathf.Max(.01f,_workEndHour-_workStartHour));
    public float MinutesToLunch => (_lunchHour-Hours)*60;
    public float HoursLeftInShift => Mathf.Max(0,_workEndHour-Hours);
    public float Rate => _clock.Value.Ready?_clock.Value.Rate:1;
    public bool Paused => _clock.Value.Paused;
    public float DayLengthMinutes => _clock.Value.Ready?_clock.Value.DayMinutes:_dayLengthMinutes;
    public float HoursPerRealSecond => Paused?0:24/Mathf.Max(6,DayLengthMinutes*60)*Rate;
    public static bool InRange(float h,float start,float end) => start<=end?h>=start && h<end:h>=start || h<end;
    private void Awake() { _hours=Mathf.Repeat(_startHour,24); }
    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if(Instance!=null && Instance!=this) Debug.LogError("[Clock] В сценах два GameClock. Оставь один общий.",this);
        else Instance=this;
        _clock.OnChange+=Changed;
    }
    public override void OnStartServer()
    {
        base.OnStartServer();_hours=Mathf.Repeat(_startHour,24);_revision=0;
        _clock.Value=new ClockState { Ready=true,Rate=1,DayMinutes=Mathf.Clamp(_dayLengthMinutes,.1f,1440) };Publish(true);
    }
    public override void OnStartClient() { base.OnStartClient();if(!IsServerInitialized) Receive(_clock.Value); }
    private void Changed(ClockState old,ClockState next,bool asServer) { if(!asServer && !IsServerInitialized) Receive(next); }
    private void Receive(ClockState s)
    {
        if(!s.Ready) return;
        double transit=TimeManager!=null?System.Math.Min(5,System.Math.Max(0,TimeManager.TimePassed(s.Tick))):0;
        _anchor=s.AbsoluteHours+(s.Paused?0:transit*24/System.Math.Max(6,s.DayMinutes*60)*s.Rate);
        _receivedAt=Time.unscaledTime;
        if(!_received || _revision!=s.Revision || System.Math.Abs(_hours-_anchor)>_snapThresholdHours) _hours=_anchor;
        _revision=s.Revision;_received=true;
    }
    private void Update()
    {
        if(IsServerInitialized)
        {
            _hours+=HoursPerRealSecond*Time.unscaledDeltaTime;
            if(Time.unscaledTime>=_nextSync) Publish(false);
        }
        else if(IsClientInitialized && _received)
        {
            double target=_anchor+(Time.unscaledTime-_receivedAt)*HoursPerRealSecond;
            _hours+=HoursPerRealSecond*Time.unscaledDeltaTime;
            _hours+=(target-_hours)*Mathf.Clamp01(Time.unscaledDeltaTime*4);
        }
    }
    private void Publish(bool changed)
    {
        ClockState s=_clock.Value;s.AbsoluteHours=_hours;s.Ready=true;s.Tick=TimeManager.Tick;
        if(changed) s.Revision=++_revision;
        _clock.Value=s;_nextSync=Time.unscaledTime+Mathf.Max(.1f,_syncInterval);
    }
    private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    [Server] public void ServerSetTime(float hour) { if(!Finite(hour)) return;_hours=System.Math.Floor(_hours/24)*24+Mathf.Repeat(hour,24);Publish(true); }
    [Server] public void ServerSkipHours(float hours) { if(!Finite(hours)) return;_hours=System.Math.Max(0,_hours+hours);Publish(true); }
    [Server] public void ServerSetPaused(bool paused) { ClockState s=_clock.Value;s.Paused=paused;_clock.Value=s;Publish(true); }
    [Server] public void ServerSetRate(float rate) { if(!Finite(rate)) return;ClockState s=_clock.Value;s.Rate=Mathf.Clamp(rate,0,180);_clock.Value=s;Publish(true); }
    [Server] public void ServerSetDayLength(float minutes) { if(!Finite(minutes)) return;ClockState s=_clock.Value;s.DayMinutes=Mathf.Clamp(minutes,.1f,1440);_clock.Value=s;Publish(true); }
    public override void OnStopNetwork() { _clock.OnChange-=Changed;_received=false;if(Instance==this) Instance=null;base.OnStopNetwork(); }
}
