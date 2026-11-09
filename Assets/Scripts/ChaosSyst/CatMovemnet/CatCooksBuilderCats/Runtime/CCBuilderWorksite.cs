using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Builders
{
    // Called by CCBuilderDirector on the server, before its shared path queue.
    [DisallowMultipleComponent, RequireComponent(typeof(CCBuilderDirector))]
    public sealed class CCBuilderWorksite : MonoBehaviour
    {
        public CCBuilderWorkstation[] Stations = new CCBuilderWorkstation[0];
        public CCBuilderWorkItem[] Items = new CCBuilderWorkItem[0];
        [Header("Смена, секунды игрового времени")]
        [Min(10)] public float WorkSeconds = 120;
        [Min(2)] public float BreakSeconds = 25;
        [Range(1,8)] public int WorkBlocksPerShift = 3;
        [Min(2)] public float BetweenShiftsSeconds = 50;
        [Min(0)] public float CrewStartOffset = 12;
        [Header("Поиск и перемещение")]
        [Min(1)] public float SearchRadius = 45;
        [Min(1)] public float RetrySeconds = 3;
        [Min(2)] public float FailedTargetCooldown = 20;
        [Min(5)] public float TravelTimeout = 35;
        public bool ShowDebugLabels;
        public bool UseGameClock;
        [Range(0,24)] public float WakeHour=6, SleepHour=22;
        [Min(0)] public float LunchMinutes=35, CrewMorningDelayMinutes=4;
        public CCBuilderPoint EveningMeeting, SleepingArea;
        [Range(0,1)] public float TripChancePerJourney=.16f;
        [Min(0)] public float TripCooldown=45;
        public int ActiveJobs { get; private set; }
        public int WaitingCats { get; private set; }
        public int CompletedJobs { get; private set; }
        public enum ShiftPhase { Work, Break, BetweenShifts, Evening, Sleeping }
        private enum Stage { Searching, ToItem, ToStation, Working, ToRest, Resting, Falling, GettingUp, ToBrick, Collecting }
        private sealed class Worker
        {
            public CCBuilderCat Cat;
            public CCBuilderWorkstation Station, LastStation;
            public CCBuilderWorkItem Item;
            public int Slot;
            public Stage Stage;
            public ShiftPhase Phase;
            public float Deadline, RetryAt, NextScoop, TripAt, LastTrip=-999;
            public CCBuilderBrickSpill Spill;
            public int Brick;
            public bool PhaseReady, Interrupted;
            public Vector3 RestCenter;
            public readonly Dictionary<CCBuilderWorkstation,float> BlockedStations = new Dictionary<CCBuilderWorkstation,float>();
            public readonly Dictionary<CCBuilderWorkItem,float> BlockedItems = new Dictionary<CCBuilderWorkItem,float>();
        }
        private readonly Dictionary<CCBuilderCat,Worker> _workers = new Dictionary<CCBuilderCat,Worker>();
        private CCBuilderDirector _director;
        private System.Random _random;
        private float _startedAt;
        private bool _started;
        private int _recoveryBudget;

        public static ShiftPhase PhaseAt(float elapsed,float work,float pause,int blocks,float between)
        {
            work=Mathf.Max(10,work);pause=Mathf.Max(2,pause);blocks=Mathf.Clamp(blocks,1,8);between=Mathf.Max(2,between);
            float cycle=work+pause, inShift=Mathf.Repeat(elapsed,cycle*blocks+between);
            if(inShift>=cycle*blocks) return ShiftPhase.BetweenShifts;
            return Mathf.Repeat(inShift,cycle)<work ? ShiftPhase.Work : ShiftPhase.Break;
        }
        public ShiftPhase PhaseFor(int group)
        {
            if(!UseGameClock) return PhaseAt(Time.time-_startedAt+group*Mathf.Max(0,CrewStartOffset),WorkSeconds,BreakSeconds,WorkBlocksPerShift,BetweenShiftsSeconds);
            GameClock clock=GameClock.Instance;if(clock==null) return ShiftPhase.Break;
            float h=clock.Hours;
            if(GameClock.InRange(h,SleepHour,WakeHour)) return ShiftPhase.Sleeping;
            if(h>=clock.WorkEndHour) return ShiftPhase.Evening;
            if(h<clock.WorkStartHour+group*CrewMorningDelayMinutes/60f) return ShiftPhase.Break;
            float lunch=clock.LunchHour+group*CrewMorningDelayMinutes/60f;
            if(h>=lunch && h<lunch+LunchMinutes/60f) return ShiftPhase.Break;
            return ShiftPhase.Work;
        }
        public void ServerTick(List<CCBuilderCat> cats)
        {
            if(!isActiveAndEnabled || cats.Count==0) return;
            if(!_started)
            {
                _director=GetComponent<CCBuilderDirector>();_random=new System.Random(_director.Seed);
                _startedAt=Time.time;_started=true;
            }
            if(Items!=null) foreach(CCBuilderWorkItem item in Items) if(item!=null) item.ServerMaintenance();
            ActiveJobs=WaitingCats=0;
            _recoveryBudget=1;
            foreach(CCBuilderCat cat in cats)
            {
                if(cat==null || !cat.IsServerInitialized) continue;
                if(!_workers.TryGetValue(cat,out Worker w))
                { w=new Worker { Cat=cat,RetryAt=Time.time+(cat.CatId%6)*.15f };_workers.Add(cat,w); }
                Tick(w);
                if(w.Station!=null) ActiveJobs++; else if(!cat.IsCarried && w.Phase==ShiftPhase.Work) WaitingCats++;
            }
        }
        private void Tick(Worker w)
        {
            CCBuilderCat cat=w.Cat;
            if(cat.ExternalBusy) { if(!w.Interrupted) { Release(w);w.Interrupted=true;w.PhaseReady=false; } return; }
            if(!cat.isActiveAndEnabled || cat.IsCarried)
            {
                if(!w.Interrupted) { Release(w);cat.HoldPosition();w.Interrupted=true;w.PhaseReady=false; }
                return;
            }
            if(w.Interrupted) { w.Interrupted=false;w.RetryAt=Time.time+.7f; }
            if(!cat.NavigationReady)
            {
                if(Time.time<w.RetryAt || _recoveryBudget==0) return;
                _recoveryBudget--;Release(w);cat.ServerRecover();w.PhaseReady=false;
                w.Stage=Stage.Searching;w.RetryAt=Time.time+Mathf.Max(1,RetrySeconds);return;
            }
            ShiftPhase phase=PhaseFor(cat.GroupId);
            if(!w.PhaseReady || phase!=w.Phase)
            {
                Release(w);cat.HoldPosition();w.Phase=phase;w.PhaseReady=true;
                w.Stage=Stage.Searching;w.RetryAt=Time.time;
                if(phase!=ShiftPhase.Work) { BeginRest(w);return; }
            }
            if(phase!=ShiftPhase.Work)
            {
                if(w.Stage==Stage.ToRest)
                {
                    if(cat.Arrived || cat.PathFailed || Time.time>w.Deadline)
                    { w.Stage=Stage.Resting;cat.SetActivity(phase==ShiftPhase.Sleeping?CCBuilderActivity.Sleeping:phase==ShiftPhase.Break?CCBuilderActivity.Chatting:CCBuilderActivity.Resting,w.RestCenter); }
                }
                else if(w.Stage!=Stage.Resting) BeginRest(w);
                return;
            }
            if(w.Station!=null && (!w.Station.Owns(cat,w.Slot) || w.Item==null || !w.Item.IsReservedBy(cat)))
            { Fail(w);return; }
            switch(w.Stage)
            {
                case Stage.Searching:
                    if(Time.time>=w.RetryAt) FindJob(w);
                    break;
                case Stage.ToItem:
                    if(cat.Arrived)
                    {
                        if(w.Item==null || !w.Item.Take(cat)) { Fail(w);break; }
                        w.Stage=Stage.ToStation;Move(w,w.Station.SlotPosition(w.Slot),w.Station.Focus);
                        w.TripAt=Time.time-w.LastTrip>=TripCooldown && w.Item.GetComponent<CCBuilderBrickSpill>()!=null && _random.NextDouble()<TripChancePerJourney
                            ? Time.time+2+(float)_random.NextDouble()*3 : float.PositiveInfinity;
                    }
                    else if(cat.PathFailed || Time.time>w.Deadline) Fail(w);
                    break;
                case Stage.ToStation:
                    if(Time.time>=w.TripAt && cat.Activity==CCBuilderActivity.Walking && !cat.Arrived)
                    {
                        w.Spill=w.Item.GetComponent<CCBuilderBrickSpill>();w.TripAt=float.PositiveInfinity;
                        if(w.Spill!=null && w.Spill.isActiveAndEnabled)
                        {
                            w.LastTrip=Time.time;w.Spill.Scatter(cat);cat.HoldPosition();cat.SetActivity(CCBuilderActivity.Fallen,cat.transform.position+cat.transform.forward);
                            w.Stage=Stage.Falling;w.Deadline=Time.time+1.6f;break;
                        }
                    }
                    if(cat.Arrived)
                    {
                        w.Stage=Stage.Working;w.NextScoop=Time.time+Mathf.Max(.5f,w.Station.ScoopInterval);Vector2 seconds=w.Station.WorkSeconds;
                        float lo=Mathf.Max(.5f,Mathf.Min(seconds.x,seconds.y)),hi=Mathf.Max(lo,Mathf.Max(seconds.x,seconds.y));
                        w.Deadline=Time.time+lo+(float)_random.NextDouble()*(hi-lo);
                        cat.SetActivity(w.Station.Job==CCBuilderJob.Dig ? CCBuilderActivity.Digging :
                            w.Station.Job==CCBuilderJob.Hammer ? CCBuilderActivity.Hammering : CCBuilderActivity.Working,w.Station.Focus);
                    }
                    else if(cat.PathFailed || Time.time>w.Deadline) Fail(w);
                    break;
                case Stage.Falling:
                    if(w.Spill==null || !w.Spill.isActiveAndEnabled) { Fail(w);break; }
                    if(Time.time>=w.Deadline) { w.Spill.Settle();w.Stage=Stage.GettingUp;w.Deadline=Time.time+.65f;cat.SetActivity(CCBuilderActivity.GettingUp,cat.transform.position+cat.transform.forward); }
                    break;
                case Stage.GettingUp:
                    if(Time.time>=w.Deadline) { w.Brick=0;GoToBrick(w); } break;
                case Stage.ToBrick:
                    if(w.Spill==null || !w.Spill.isActiveAndEnabled) { Fail(w);break; }
                    if(cat.Arrived) { w.Stage=Stage.Collecting;w.Deadline=Time.time+.7f;cat.SetActivity(CCBuilderActivity.Collecting,w.Spill.Ground(w.Brick)); }
                    else if(cat.PathFailed || Time.time>w.Deadline) Fail(w);
                    break;
                case Stage.Collecting:
                    if(w.Spill==null || !w.Spill.isActiveAndEnabled) { Fail(w);break; }
                    if(Time.time>=w.Deadline)
                    {
                        w.Spill.Collect(++w.Brick);
                        if(w.Brick<w.Spill.Count) GoToBrick(w);
                        else { w.Spill.Finish();w.Spill=null;w.Stage=Stage.ToStation;Move(w,w.Station.SlotPosition(w.Slot),w.Station.Focus); }
                    } break;
                case Stage.Working:
                    if(w.Station.Job==CCBuilderJob.Dig && w.Station.SandSource!=null)
                    {
                        bool throwing=Time.time>=w.NextScoop-.6f;
                        cat.Face(throwing && w.Station.SandTarget!=null ? w.Station.SandTarget.transform.position : w.Station.Focus);
                        if(Time.time>=w.NextScoop)
                        {
                            w.Station.SandSource.TransferTo(w.Station.SandTarget,w.Station.SandPerScoop);
                            w.NextScoop=Time.time+Mathf.Max(.5f,w.Station.ScoopInterval);
                        }
                        if(!w.Station.HasSand) w.Deadline=Time.time;
                    }
                    if(Time.time>=w.Deadline)
                    {
                        if(w.Station.Job==CCBuilderJob.Deliver)
                        {
                            Vector3 drop=w.Station.NextDeliveryPosition();
                            if(w.Station.Route!=null) { drop=w.Station.Route.Location(w.Station.RouteEnd,w.Item.RouteSlot);w.Item.SetEnd(w.Station.RouteEnd); }
                            w.Item.Release(cat,drop,true,w.Station.RestockSeconds);
                            w.Item=null;
                        }
                        w.Station.Complete(cat);CompletedJobs++;Release(w);cat.HoldPosition();
                        w.Stage=Stage.Searching;w.RetryAt=Time.time+.5f+(float)_random.NextDouble();
                    }
                    break;
                default: w.Stage=Stage.Searching;break;
            }
        }
        private void GoToBrick(Worker w)
        {
            if(w.Spill==null || !w.Spill.isActiveAndEnabled) { Fail(w);return; }
            w.Stage=Stage.ToBrick;Move(w,w.Spill.Ground(w.Brick),w.Spill.Ground(w.Brick));
        }
        private void FindJob(Worker w)
        {
            w.RetryAt=Time.time+Mathf.Max(1,RetrySeconds);
            if(Stations==null || Items==null) return;
            CCBuilderWorkstation best=null;CCBuilderWorkItem bestItem=null;int bestSlot=-1;float bestScore=float.MaxValue;
            float limit=SearchRadius*SearchRadius;
            foreach(CCBuilderWorkstation station in Stations)
            {
                if(station==null || station.gameObject.scene!=w.Cat.gameObject.scene ||
                    (w.BlockedStations.TryGetValue(station,out float until) && until>Time.time)) continue;
                if(station.Job==CCBuilderJob.Dig && station.SandSource!=null && !station.HasSand) continue;
                int slot=station.FreeSlot();if(slot<0) continue;
                if((station.SlotPosition(slot)-w.Cat.transform.position).sqrMagnitude>limit) continue;
                CCBuilderWorkItem chosen=null;float distance=float.MaxValue;
                foreach(CCBuilderWorkItem item in Items)
                {
                    if(item==null || !item.Available || item.Kind!=station.NeededItem || !item.CanServe(station) || item.gameObject.scene!=w.Cat.gameObject.scene ||
                        (w.BlockedItems.TryGetValue(item,out float itemUntil) && itemUntil>Time.time)) continue;
                    float d=(item.transform.position-w.Cat.transform.position).sqrMagnitude;
                    if(d>limit) continue;
                    float route=Mathf.Sqrt(d)+Vector3.Distance(item.transform.position,station.SlotPosition(slot));
                    if(route<distance) { distance=route;chosen=item; }
                }
                if(chosen==null) continue;
                float score=distance+(station==w.LastStation ? 8f : 0f)+(float)_random.NextDouble()*4f;
                if(score<bestScore) { bestScore=score;best=station;bestItem=chosen;bestSlot=slot; }
            }
            if(best==null) { w.Cat.SetActivity(CCBuilderActivity.Idle,w.Cat.transform.position+w.Cat.transform.forward);return; }
            if(!best.Reserve(w.Cat,bestSlot)) return;
            if(!bestItem.Reserve(w.Cat)) { best.Release(w.Cat);return; }
            w.Station=best;w.Slot=bestSlot;w.Item=bestItem;w.Stage=Stage.ToItem;
            Move(w,bestItem.transform.position,bestItem.transform.position+Vector3.up*.2f);
        }
        private void Move(Worker w,Vector3 target,Vector3 focus)
        {
            w.Cat.RequestMove(target,focus);
            w.Deadline=Time.time+Mathf.Max(TravelTimeout,_workers.Count/Mathf.Max(1f,_director.PathBudgetPerThink)*_director.ThinkInterval+5);
        }
        private void Fail(Worker w)
        {
            if(w.Station!=null) w.BlockedStations[w.Station]=Time.time+Mathf.Max(2,FailedTargetCooldown);
            if(w.Stage==Stage.ToItem && w.Item!=null) w.BlockedItems[w.Item]=Time.time+Mathf.Max(2,FailedTargetCooldown);
            Release(w);w.Cat.HoldPosition();w.Stage=Stage.Searching;w.RetryAt=Time.time+Mathf.Max(1,RetrySeconds);
        }
        private void Release(Worker w)
        {
            if(w.Spill!=null) { w.Spill.Finish();w.Spill=null; }
            // Keep tool/material at a validated ground point when the cat is taken by a player.
            if(w.Item!=null)
            {
                Vector3 ground=w.Cat!=null ? w.Cat.LastGroundPosition : w.Item.transform.position;
                w.Item.Release(w.Cat,ground);w.Item=null;
            }
            if(w.Station!=null) { w.LastStation=w.Station;w.Station.Release(w.Cat);w.Station=null; }
        }
        private void BeginRest(Worker w)
        {
            CCBuilderPoint point=w.Phase==ShiftPhase.Sleeping ? SleepingArea : w.Phase==ShiftPhase.Evening ? EveningMeeting : null;int count=point!=null?1:0;
            if(point==null && _director.Points!=null)
            {
                foreach(CCBuilderPoint p in _director.Points) if(p!=null && p.isActiveAndEnabled && (p.Kind&(CCBuilderPlace.Rest|CCBuilderPlace.Meeting))!=0) count++;
                int pick=count>0 ? w.Cat.GroupId%count : -1;
                foreach(CCBuilderPoint p in _director.Points)
                    if(p!=null && p.isActiveAndEnabled && (p.Kind&(CCBuilderPlace.Rest|CCBuilderPlace.Meeting))!=0 && pick--==0) { point=p;break; }
            }
            if(point==null)
            { w.Stage=Stage.Resting;w.Cat.SetActivity(w.Phase==ShiftPhase.Sleeping?CCBuilderActivity.Sleeping:CCBuilderActivity.Resting,w.Cat.transform.position+w.Cat.transform.forward);return; }
            w.Stage=Stage.ToRest;w.RestCenter=point.transform.position;
            // Additional concentric rings keep multiple crews at a shared rest point apart.
            Vector3 target=point.Slot(w.Cat.GroupSlot,4);
            Vector3 offset=target-point.transform.position;
            target+=offset.normalized*(w.Cat.GroupId/Mathf.Max(1,count))*1.1f;
            Move(w,target,point.transform.position);
        }
        public void Interrupt(CCBuilderCat cat)
        {
            if(cat==null || !cat.IsServerInitialized) return;
            if(_workers.TryGetValue(cat,out Worker w))
            { Release(w);w.Interrupted=true;w.PhaseReady=false;w.Stage=Stage.Searching; }
        }
        public void Remove(CCBuilderCat cat)
        {
            if(cat==null) return;
            if(_workers.TryGetValue(cat,out Worker w)) { Release(w);_workers.Remove(cat); }
        }
        public void ResetServer()
        {
            foreach(Worker w in _workers.Values)
            { Release(w);if(w.Cat!=null && !w.Cat.ExternalBusy) w.Cat.HoldPosition(); }
            _workers.Clear();_started=false;ActiveJobs=WaitingCats=0;
        }
        private void OnDisable() { ResetServer(); }
        public string Status(CCBuilderCat cat)
        {
            if(!_workers.TryGetValue(cat,out Worker w)) return "нет серверного задания";
            if(cat.IsCarried) return "на руках";
            return w.Phase+" / "+w.Stage+(w.Station!=null ? " / "+w.Station.name : "");
        }
    }
}
