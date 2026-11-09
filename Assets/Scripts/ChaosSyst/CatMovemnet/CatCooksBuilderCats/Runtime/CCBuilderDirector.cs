using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace CatCooks.Builders
{
    [Serializable]
    public sealed class CCBuilderStep
    {
        public string Name;
        public CCBuilderPlace Place = CCBuilderPlace.Work;
        public CCBuilderActivity Activity = CCBuilderActivity.Working;
        public Vector2 Duration = new Vector2(10,16);
    }
    [DisallowMultipleComponent]
    public sealed class CCBuilderDirector : MonoBehaviour
    {
        public CCBuilderPoint[] Points = new CCBuilderPoint[0];
        public Material[] GroupMaterials = new Material[0];
        public CCBuilderStep[] Schedule = new CCBuilderStep[0];
        public bool RunSchedule = true;
        [Range(.1f,1f)] public float ThinkInterval = .2f;
        [Range(1,8)] public int PathBudgetPerThink = 2;
        [Min(5f)] public float TravelTimeout = 30f;
        public bool AllowOtherPointTypes = true;
        public int Seed = 418;
        public int ServerCatCount => _cats.Count;
        public int ServerGroupCount => _groups.Count;
        public int NavigationFailures
        {
            get { int n=0;foreach(CCBuilderCat cat in _cats) if(cat!=null && !cat.NavigationReady)n++;return n; }
        }
        private sealed class Group
        {
            public CCBuilderCat[] Members;
            public int Id, Step, Point=-1, LastPoint=-1;
            public bool Traveling, Acting;
            public float Deadline, RetryAt;
        }
        private readonly List<CCBuilderCat> _cats = new List<CCBuilderCat>(32);
        private readonly List<Group> _groups = new List<Group>(8);
        private int[] _owners;
        private System.Random _random;
        private float _nextThink, _rebuildAt;
        private int _pathCursor;
        private bool _dirty, _paused, _warned, _usingWorksite;
        private CCBuilderWorksite _worksite;
        private void Awake() { _worksite=GetComponent<CCBuilderWorksite>(); }
        private static readonly ProfilerMarker ThinkMarker = new ProfilerMarker("CatCooks.Builders.Think");
        public void Register(CCBuilderCat cat)
        {
            if (cat==null || !cat.IsServerInitialized || _cats.Contains(cat))return;
            _cats.Add(cat);_dirty=true;_rebuildAt=Time.time+.25f;
        }
        public void Unregister(CCBuilderCat cat) { if(_worksite!=null) _worksite.Remove(cat);_cats.Remove(cat);_dirty=true;_rebuildAt=Time.time+.1f; }
        private void Update()
        {
            if (_cats.Count==0) { if(_groups.Count>0)_groups.Clear();return; }
            if (!RunSchedule)
            {
                if (!_paused) { if(_worksite!=null) _worksite.ResetServer();StopCats();_groups.Clear();_dirty=true;_paused=true; }
                return;
            }
            _paused=false;
            if (Time.time<_nextThink)return;
            _nextThink=Time.time+Mathf.Max(.1f,ThinkInterval);
            using(ThinkMarker.Auto())
            {
                bool useWorksite=_worksite!=null && _worksite.isActiveAndEnabled;
                if(useWorksite!=_usingWorksite)
                { if(_worksite!=null) _worksite.ResetServer();_usingWorksite=useWorksite;_dirty=true; }
                if (_dirty)
                {
                    if (Time.time<_rebuildAt)return;
                    BuildGroups();
                }
                if (_usingWorksite) _worksite.ServerTick(_cats);
                else
                {
                if (Points==null || Points.Length==0 || Schedule==null || Schedule.Length==0)
                {
                    if (!_warned) { Debug.LogWarning("[Коты] Назначь Points и Schedule в CCBuilderDirector.",this);_warned=true; }
                    return;
                }
                if (_owners==null || _owners.Length!=Points.Length) { _dirty=true;return; }
                foreach(Group group in _groups) ThinkGroup(group);
                }
                // Round robin avoids starving later cats if early paths fail.
                int examined=0,budget=Mathf.Max(1,PathBudgetPerThink);
                while(examined<_cats.Count && budget>0)
                {
                    _pathCursor%= _cats.Count;
                    CCBuilderCat cat=_cats[_pathCursor++];examined++;
                    if (cat==null || !cat.NeedsPath)continue;
                    cat.ProcessPath();budget--;
                }
            }
        }
        public static int[] Partition(int count)
        {
            if (count<=0)return new int[0];
            // 20 -> 4,4,3,3,3,3. For 6+ cats all groups contain 3 or 4.
            int groupCount=Mathf.Max(1,Mathf.RoundToInt(count/3.5f));
            groupCount=Mathf.Clamp(groupCount,Mathf.CeilToInt(count/4f),Mathf.Max(1,count/3));
            int[] sizes=new int[groupCount];
            for(int i=0;i<groupCount;i++)sizes[i]=count/groupCount+(i<count%groupCount?1:0);
            return sizes;
        }
        private void BuildGroups()
        {
            if(_worksite!=null) _worksite.ResetServer();
            StopCats();_groups.Clear();
            _cats.RemoveAll(cat=>cat==null || !cat.IsServerInitialized);
            _cats.Sort((a,b)=>a.CatId.CompareTo(b.CatId));
            _owners=new int[Points==null?0:Points.Length];
            for(int i=0;i<_owners.Length;i++)_owners[i]=-1;
            _random=new System.Random(Seed);
            int index=0;int[] sizes=Partition(_cats.Count);
            for(int i=0;i<sizes.Length;i++)
            {
                Group g=new Group { Id=i,Members=new CCBuilderCat[sizes[i]],Step=Schedule!=null && Schedule.Length>0?i%Schedule.Length:0,RetryAt=Time.time+i*.25f };
                for(int slot=0;slot<sizes[i];slot++) { g.Members[slot]=_cats[index++];g.Members[slot].AssignGroup(i,slot); }
                _groups.Add(g);
            }
            _dirty=false;_warned=false;_pathCursor=0;
            if(NavigationFailures>0) Debug.LogWarning("[Коты] Не на NavMesh: "+NavigationFailures+". Проверь Bake, Agent Type и высоту капсул.",this);
        }
        private void ThinkGroup(Group g)
        {
            if (g.Point>=0 && (g.Point>=Points.Length || Points[g.Point]==null || !Points[g.Point].isActiveAndEnabled))
            { Release(g);foreach(CCBuilderCat c in g.Members)c.HoldPosition();g.Traveling=g.Acting=false; }
            if (g.Acting)
            {
                if(Time.time<g.Deadline)return;
                Release(g);g.Acting=false;g.Step=(g.Step+1)%Schedule.Length;g.RetryAt=Time.time+.3f;
            }
            if (g.Traveling)
            {
                int arrived=0;bool pending=false;
                foreach(CCBuilderCat cat in g.Members) { if(cat.Arrived)arrived++;if(cat.NeedsPath)pending=true; }
                if(arrived==g.Members.Length || Time.time>=g.Deadline)
                {
                    CCBuilderStep step=Schedule[g.Step%Schedule.Length];
                    g.Traveling=false;g.Acting=true;
                    float lo=step==null?5f:Mathf.Max(.5f,Mathf.Min(step.Duration.x,step.Duration.y));
                    float hi=step==null?8f:Mathf.Max(lo,Mathf.Max(step.Duration.x,step.Duration.y));
                    g.Deadline=Time.time+lo+(float)_random.NextDouble()*(hi-lo);
                    Vector3 center=Points[g.Point].transform.position;
                    CCBuilderActivity act=step==null?CCBuilderActivity.Resting:step.Activity;
                    if(act==CCBuilderActivity.Walking || act==CCBuilderActivity.Stranded || act==CCBuilderActivity.Gathering)act=CCBuilderActivity.Chatting;
                    foreach(CCBuilderCat cat in g.Members)
                        cat.SetActivity(cat.Arrived ? act : CCBuilderActivity.Stranded,center);
                    return;
                }
                // Entire group could not find a path: free the point and try another step.
                bool allFailed=!pending;
                foreach(CCBuilderCat cat in g.Members) if(!cat.PathFailed)allFailed=false;
                if(allFailed)
                {
                    Release(g);g.Traveling=false;g.Step=(g.Step+1)%Schedule.Length;g.RetryAt=Time.time+2f;
                }
                return;
            }
            if(Time.time<g.RetryAt)return;
            CCBuilderStep next=Schedule[g.Step%Schedule.Length];
            int node=PickPoint(g,next==null?CCBuilderPlace.Any:next.Place);
            if(node<0) { g.RetryAt=Time.time+1f;return; }
            _owners[node]=g.Id;g.Point=node;g.Traveling=true;
            g.Deadline=Time.time+Mathf.Max(TravelTimeout,_cats.Count/Mathf.Max(1f,PathBudgetPerThink)*ThinkInterval+5f);
            CCBuilderPoint point=Points[node];
            for(int slot=0;slot<g.Members.Length;slot++)g.Members[slot].RequestMove(point.Slot(slot,g.Members.Length),point.transform.position);
        }
        private int PickPoint(Group g,CCBuilderPlace kind)
        {
            int start=g.LastPoint>=0?(g.LastPoint+1)%Points.Length:g.Id%Points.Length;
            // Prefer a different free point of the desired kind, then optional fallback.
            for(int pass=0;pass<(AllowOtherPointTypes?2:1);pass++)
                for(int n=0;n<Points.Length;n++)
                {
                    int i=(start+n)%Points.Length;CCBuilderPoint p=Points[i];
                    if(p==null || !p.isActiveAndEnabled || _owners[i]>=0 || (i==g.LastPoint && Points.Length>1))continue;
                    if(pass==0 && (p.Kind&kind)==0)continue;
                    return i;
                }
            // Single usable point must still allow a cycle instead of deadlocking.
            int last=g.LastPoint;
            if(last>=0 && last<Points.Length && Points[last]!=null && Points[last].isActiveAndEnabled && _owners[last]<0 &&
                (AllowOtherPointTypes || (Points[last].Kind&kind)!=0))return last;
            return -1;
        }
        private void Release(Group g)
        {
            if(g.Point>=0 && _owners!=null && g.Point<_owners.Length && _owners[g.Point]==g.Id)_owners[g.Point]=-1;
            g.LastPoint=g.Point;g.Point=-1;
        }
        private void StopCats() { foreach(CCBuilderCat cat in _cats)if(cat!=null && !cat.ExternalBusy)cat.HoldPosition(); }
        private void OnDisable() { if(_worksite!=null) _worksite.ResetServer();StopCats();_groups.Clear();_dirty=true; }
    }
}
