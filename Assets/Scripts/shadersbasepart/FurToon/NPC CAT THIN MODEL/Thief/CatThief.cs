using System.Collections.Generic;
using CatCooks.Food;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace CatCooks.Thief
{
    /// <summary>
    /// КОТ-ВОРИШКА (NPC). Всё решает сервер, клиенты видят состояние и позицию (NetworkTransform).
    ///
    ///   Бродит (Walk) вокруг дома →
    ///   на полу появился кусок МЯСА (отрезали от туши, выпал) → замер, осматривается (Lookout) →
    ///   крадётся на цыпочках (Sneak), по пути останавливается и осматривается →
    ///   хватает (Pickup, кадр 42) → убегает с мясом в лапах (Escape):
    ///     • за ним НЕ гонятся — бежит в укрытие (CatHideSpot или подальше от игроков) и там СЪЕДАЕТ мясо;
    ///     • гонятся (игрок ближе Chase Radius) — бегает от игрока зигзагами;
    ///     • игрок догнал и нажал ЛКМ (пустые руки, кот в 2 м перед тобой) — кот ПАДАЕТ, мясо выпадает,
    ///       кот удирает с пустыми лапами и какое-то время не ворует (Cooldown).
    ///   Игрок может и просто выхватить кусок из лап (F) — кот остаётся ни с чем.
    ///   Игрок подошёл, пока кот крадётся (Spook Radius), — кот пугается и уходит.
    ///
    /// Движение: NavMeshAgent, если кот стоит на NavMesh; иначе — сам, по прямой, обходя препятствия.
    /// На корне: NetworkObject, NetworkTransform, CatThief, Rigidbody (kinematic), CapsuleCollider;
    /// внутри — модель с Animator + CatThiefAnimator. Собирает меню CatCooks → Кот-воришка → 1.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatThief : NetworkBehaviour
    {
        public enum State : byte { Wander, Notice, Sneak, Look, Pickup, Escape, Evade, Eat, Fall, Flee }

        [Header("Ссылки")]
        [SerializeField] private CatThiefAnimator _anim;

        [Header("Скорости, м/с")]
        [SerializeField] private float _walkSpeed = 1.1f;
        [SerializeField] private float _sneakSpeed = 0.55f;
        [SerializeField] private float _runSpeed = 4.2f;
        [SerializeField] private float _turnSpeed = 540f;

        [Header("Брожение")]
        [Tooltip("Радиус брожения вокруг точки, где кот стоит в начале")]
        [SerializeField] private float _wanderRadius = 8f;
        [SerializeField] private Vector2 _wanderPause = new Vector2(1f, 4f);

        [Header("Мясо")]
        [Tooltip("Как далеко кот замечает мясо на полу")]
        [SerializeField] private float _smellRadius = 20f;
        [Tooltip("Мясо должно пролежать на полу столько секунд — тогда кот его заметит")]
        [SerializeField] private float _noticeDelay = 1.5f;
        [Tooltip("Слова в названии продукта — это мясо (если у продукта нет группы «Мясо»)")]
        [SerializeField] private string[] _meatWords = { "мяс", "говядин", "свинин", "телят", "туш", "рёбр", "ребр", "брискет", "фарш", "котлет", "сало", "голяшк", "корейк", "Beef", "Pork", "Meat" };

        [Tooltip("Хватает, когда до края куска ближе этого, м")]
        [SerializeField] private float _grabReach = 0.6f;

        [Header("Крадётся")]
        [Tooltip("Каждые столько секунд — стоп и осмотреться")]
        [SerializeField] private Vector2 _lookEvery = new Vector2(2.5f, 4.5f);
        [SerializeField] private float _lookTime = 1.6f;
        [Tooltip("Игрок ближе этого, пока кот крадётся, — пугается и уходит")]
        [SerializeField] private float _spookRadius = 2.5f;

        [Header("Погоня")]
        [Tooltip("Игрок ближе этого — погоня, кот бегает зигзагами")]
        [SerializeField] private float _chaseRadius = 7f;
        [Tooltip("Столько секунд без погони — и кот идёт в укрытие есть")]
        [SerializeField] private float _calmTime = 3.5f;
        [Tooltip("Съедает мясо в укрытии за столько секунд")]
        [SerializeField] private float _eatTime = 7f;

        [Header("Толчок (ЛКМ)")]
        [SerializeField] private float _pushReach = 2.2f;
        [Tooltip("После падения не ворует столько секунд")]
        [SerializeField] private float _cooldown = 25f;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _state = new SyncVar<byte>((byte)State.Wander);
        private readonly SyncVar<NetworkObject> _carried = new SyncVar<NetworkObject>();
        private readonly SyncVar<float> _speedNet = new SyncVar<float>(0f, new SyncTypeSettings(0.1f));

        public State Current => (State)_state.Value;

        // ── сервер ──
        private NavMeshAgent _agent;
        private Vector3 _home, _target;
        private bool _hasTarget;
        private float _stateT, _pauseUntil, _nextLook, _lastChased, _cooldownUntil, _scanAt, _evadeAt;
        private FoodPiece _prey;
        private readonly Dictionary<FoodPiece, float> _seenSince = new Dictionary<FoodPiece, float>();
        private Vector3 _lastPos;
        private float _speed;

        private void Awake()
        {
            if (_anim == null) _anim = GetComponentInChildren<CatThiefAnimator>();
            _agent = GetComponent<NavMeshAgent>();
            // v26.2: кот не толкает предметы телом (кусок «уезжал» от него) — коллайдер только для попаданий
            foreach (Collider c in GetComponents<Collider>()) c.isTrigger = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _home = transform.position;
            _lastPos = transform.position;
            if (_agent != null)
            {
                _agent.enabled = NavMesh.SamplePosition(transform.position, out NavMeshHit h, 1.5f, NavMesh.AllAreas);
                if (_agent.enabled) { _agent.Warp(h.position); _agent.updateRotation = false; }
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!base.IsServerInitialized && _agent != null) _agent.enabled = false;   // клиенты — только по NetworkTransform
        }

        // ───────────────────────── СЕРВЕР: МОЗГ ─────────────────────────

        private void Update()
        {
            if (base.IsServerInitialized) ServerThink(Time.deltaTime);
            ClientTick();
        }

        private void SetState(State s)
        {
            if (Current == s) return;
            if (_log) Debug.Log($"[Кот-воришка] {Current} → {s}", this);
            _state.Value = (byte)s;
            _stateT = 0f;
            _hasTarget = false;
            Stop();
        }

        private void ServerThink(float dt)
        {
            _stateT += dt;
            Vector3 pos = transform.position;
            _speed = Mathf.Lerp(_speed, (pos - _lastPos).magnitude / Mathf.Max(1e-4f, dt), 0.25f);
            _lastPos = pos;
            if (Mathf.Abs(_speed - _speedNet.Value) > 0.15f) _speedNet.Value = _speed;

            // кусок отобрали из лап (игрок взял F) — кот ни с чем
            FoodPiece carried = CarriedPiece;
            if (_carried.Value != null && (carried == null || carried.IsHeld || carried.IsConsumed))
            {
                ReleaseCarried(false);
                if (Current == State.Escape || Current == State.Evade || Current == State.Eat) { _cooldownUntil = Time.time + _cooldown * 0.5f; SetState(State.Flee); }
            }

            Transform chaser = NearestPlayer(out float chaserDist);
            switch (Current)
            {
                case State.Wander: ThinkWander(); break;
                case State.Notice:
                    Face(_prey != null ? _prey.transform.position : pos, dt);
                    if (!PreyValid()) { SetState(State.Wander); break; }
                    if (_stateT >= _lookTime) { _nextLook = Time.time + Random.Range(_lookEvery.x, _lookEvery.y); SetState(State.Sneak); }
                    break;
                case State.Sneak:
                    if (!PreyValid()) { SetState(State.Wander); break; }
                    if (chaser != null && chaserDist < _spookRadius) { if (_log) Debug.Log("[Кот-воришка] спугнули", this); _cooldownUntil = Time.time + 6f; SetState(State.Flee); break; }
                    if (Time.time >= _nextLook) { SetState(State.Look); break; }
                    // v26.2: дошёл до КРАЯ куска (не до центра — большой кусок он толкал перед собой бесконечно)
                    {
                        Vector3 edge = PreyEdge(_prey);
                        Vector3 fl = edge - pos; fl.y = 0f;
                        if (fl.magnitude <= _grabReach) { Stop(); SetState(State.Pickup); break; }
                        MoveTo(edge, _sneakSpeed, dt, _grabReach * 0.8f);
                    }
                    break;
                case State.Look:
                    if (!PreyValid()) { SetState(State.Wander); break; }
                    if (chaser != null && chaserDist < _spookRadius) { _cooldownUntil = Time.time + 6f; SetState(State.Flee); break; }
                    if (_stateT >= _lookTime) { _nextLook = Time.time + Random.Range(_lookEvery.x, _lookEvery.y); SetState(State.Sneak); }
                    break;
                case State.Pickup:
                    if (!PreyValid() && _carried.Value == null) { SetState(State.Wander); break; }
                    Face(_prey != null ? _prey.transform.position : pos, dt);
                    if (_carried.Value == null && _stateT >= (_anim != null ? _anim.PickupGrabTime : 1.37f)) Grab(_prey);
                    if (_stateT >= (_anim != null && _anim.pickup != null ? _anim.pickup.length : 2.5f)) { _lastChased = Time.time; SetState(State.Escape); }
                    break;
                case State.Escape:
                    if (_carried.Value == null) { SetState(State.Flee); break; }
                    if (chaser != null && chaserDist < _chaseRadius) { _lastChased = Time.time; SetState(State.Evade); break; }
                    if (!_hasTarget) { _target = HidePoint(); _hasTarget = true; }
                    if (MoveTo(_target, _runSpeed, dt, 0.6f)) SetState(State.Eat);
                    break;
                case State.Evade:
                    if (_carried.Value == null) { SetState(State.Flee); break; }
                    if (chaser != null && chaserDist < _chaseRadius) _lastChased = Time.time;
                    if (Time.time - _lastChased > _calmTime) { SetState(State.Escape); break; }
                    if (chaser != null && (Time.time >= _evadeAt || !_hasTarget)) { _target = EvadePoint(chaser.position); _hasTarget = true; _evadeAt = Time.time + Random.Range(0.5f, 1.1f); }
                    if (_hasTarget && MoveTo(_target, _runSpeed, dt, 0.4f)) _hasTarget = false;
                    break;
                case State.Eat:
                    if (_carried.Value == null) { SetState(State.Wander); break; }
                    if (chaser != null && chaserDist < _chaseRadius * 0.7f) { _lastChased = Time.time; SetState(State.Evade); break; }
                    if (_stateT >= _eatTime) { EatCarried(); SetState(State.Wander); }
                    break;
                case State.Fall:
                    if (_stateT >= (_anim != null ? _anim.fallDuration : 1.8f)) SetState(State.Flee);
                    break;
                case State.Flee:
                    if (!_hasTarget) { _target = chaser != null ? EvadePoint(chaser.position, 9f) : RandomAround(_home, _wanderRadius); _hasTarget = true; }
                    if (MoveTo(_target, _runSpeed * 0.9f, dt, 0.6f) || _stateT > 5f) SetState(State.Wander);
                    break;
            }
            HoldCarried();
        }

        private void ThinkWander()
        {
            if (Time.time >= _cooldownUntil && Time.time >= _scanAt)
            {
                _scanAt = Time.time + 0.5f;
                FoodPiece prey = FindPrey();
                if (prey != null) { _prey = prey; SetState(State.Notice); return; }
            }
            if (!_hasTarget)
            {
                if (Time.time < _pauseUntil) { _speed = 0f; return; }
                _target = RandomAround(_home, _wanderRadius); _hasTarget = true;
            }
            if (MoveTo(_target, _walkSpeed, Time.deltaTime, 0.4f) || _stateT > 15f)
            {
                _hasTarget = false; _stateT = 0f;
                _pauseUntil = Time.time + Random.Range(_wanderPause.x, _wanderPause.y);
            }
        }

        // ── мясо ──

        private bool IsMeat(FoodPiece p)
        {
            ProductDef d = p.Def;
            if (d == null) return false;
            if (d.Group != null && ((d.Group.Title ?? "").ToLowerInvariant().Contains("мяс") || d.Group.name.ToLowerInvariant().Contains("meat"))) return true;
            string t = (d.Title ?? "").ToLowerInvariant();
            foreach (string w in _meatWords) if (!string.IsNullOrEmpty(w) && t.Contains(w.ToLowerInvariant())) return true;
            return false;
        }

        private FoodPiece FindPrey()
        {
            FoodPiece best = null; float bd = _smellRadius * _smellRadius;
            var alive = new HashSet<FoodPiece>();
            foreach (FoodPiece p in FindObjectsByType<FoodPiece>(FindObjectsSortMode.None))
            {
                if (p == null || !p.IsSpawned || p.IsHeld || p.IsConsumed || !IsMeat(p)) continue;
                if (p.GetComponent<FoodCarcass>() != null) continue;                   // целую тушу не утащить
                if (InCookware(p) || TakenByOther(p)) continue;
                alive.Add(p);
                if (!_seenSince.TryGetValue(p, out float since)) { _seenSince[p] = Time.time; continue; }
                if (Time.time - since < _noticeDelay) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            // забыть пропавшие
            var gone = new List<FoodPiece>();
            foreach (var kv in _seenSince) if (kv.Key == null || !alive.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var g in gone) _seenSince.Remove(g);
            if (best != null && _log) Debug.Log($"[Кот-воришка] учуял «{best.Def?.Title}» в {Mathf.Sqrt(bd):0.0} м", this);
            return best;
        }

        private static bool InCookware(FoodPiece p)
        {
            foreach (Cookware c in Cookware.All)
                if (c != null && c.IsInside(p.transform.position)) return true;
            return false;
        }

        private bool TakenByOther(FoodPiece p)
        {
            foreach (CatThief t in FindObjectsByType<CatThief>(FindObjectsSortMode.None))
                if (t != this && (t._prey == p || t._carried.Value == p.NetworkObject)) return true;
            return false;
        }

        /// <summary>Ближайшая к коту точка куска (по его коллайдерам).</summary>
        private Vector3 PreyEdge(FoodPiece p)
        {
            Vector3 me = transform.position, best = p.transform.position; float bd = float.MaxValue;
            foreach (Collider c in p.GetComponentsInChildren<Collider>())
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                Vector3 q = c.ClosestPoint(me);
                float d = (q - me).sqrMagnitude;
                if (d < bd) { bd = d; best = q; }
            }
            return best;
        }

        private bool PreyValid() => _prey != null && _prey.IsSpawned && !_prey.IsHeld && !_prey.IsConsumed && !InCookware(_prey);

        private FoodPiece CarriedPiece => _carried.Value != null ? _carried.Value.GetComponent<FoodPiece>() : null;

        [Server]
        private void Grab(FoodPiece p)
        {
            if (p == null || p.IsHeld || p.IsConsumed) return;
            _carried.Value = p.NetworkObject;
            RpcCarry(p.NetworkObject, true);
            SetCarryPhysics(p.NetworkObject, true);
            if (_log) Debug.Log($"[Кот-воришка] схватил «{p.Def?.Title}»!", this);
        }

        private void HoldCarried()
        {
            if (_carried.Value == null || _anim == null || _anim.Socket == null) return;
            _carried.Value.transform.SetPositionAndRotation(_anim.Socket.position, _anim.Socket.rotation);
        }

        [Server]
        private void ReleaseCarried(bool toss, Vector3 dir = default)
        {
            NetworkObject n = _carried.Value;
            _carried.Value = null;
            _prey = null;
            if (n == null) return;
            FoodPiece p = n.GetComponent<FoodPiece>();
            if (p != null && p.IsHeld) return;                                        // игрок уже держит — не трогаем
            RpcCarry(n, false);
            SetCarryPhysics(n, false);
            Rigidbody rb = n.GetComponent<Rigidbody>();
            if (toss && rb != null) rb.AddForce(dir * 3.5f + Vector3.up * 2.5f, ForceMode.VelocityChange);
        }

        [Server]
        private void EatCarried()
        {
            NetworkObject n = _carried.Value;
            FoodPiece p = CarriedPiece;
            _carried.Value = null;
            _prey = null;
            if (p != null && p.ServerTryConsume()) { RpcCrumbs(transform.position + Vector3.up * 0.5f); if (_log) Debug.Log($"[Кот-воришка] съел «{p.Def?.Title}» ням", this); n.Despawn(); }
        }

        [ObserversRpc(BufferLast = true)]
        private void RpcCarry(NetworkObject n, bool on)
        {
            if (base.IsServerInitialized || n == null) return;
            SetCarryPhysics(n, on);
        }

        private static void SetCarryPhysics(NetworkObject n, bool on)
        {
            Rigidbody rb = n.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (on && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                rb.isKinematic = on;
                if (!on) rb.WakeUp();
            }
            foreach (Collider c in n.GetComponentsInChildren<Collider>())
                if (!c.isTrigger) c.enabled = !on;                                    // в лапах — не цепляется за кота
        }

        [ObserversRpc]
        private void RpcCrumbs(Vector3 at)
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("Cat Crumbs");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.duration = 0.5f; main.loop = false; main.startLifetime = 0.8f; main.startSpeed = 2f;
            main.startSize = 0.08f; main.startColor = new Color(0.75f, 0.3f, 0.3f); main.gravityModifier = 1f;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 25) });
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh != null) go.GetComponent<ParticleSystemRenderer>().sharedMaterial = new Material(sh);
            ps.Play();
        }

        // ── игроки ──

        private Transform NearestPlayer(out float dist)
        {
            dist = float.MaxValue; Transform best = null;
            foreach (PlayerHand h in FindObjectsByType<PlayerHand>(FindObjectsSortMode.None))
            {
                if (h == null) continue;
                float d = Vector3.Distance(h.transform.position, transform.position);
                if (d < dist) { dist = d; best = h.transform; }
            }
            return best;
        }

        // ── точки ──

        private Vector3 RandomAround(Vector3 c, float r)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 v = Random.insideUnitCircle * r;
                Vector3 p = c + new Vector3(v.x, 0f, v.y);
                if (Ground(ref p)) return p;
            }
            return c;
        }

        private Vector3 EvadePoint(Vector3 from, float dist = 5f)
        {
            Vector3 away = transform.position - from; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
            away.y = 0f; away.Normalize();
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = Quaternion.Euler(0f, Random.Range(-75f, 75f), 0f) * away;   // зигзаг
                Vector3 p = transform.position + d * dist;
                if (Ground(ref p) && Clear(transform.position, p)) return p;
            }
            Vector3 q = transform.position + Quaternion.Euler(0f, Random.Range(90f, 270f), 0f) * away * dist;   // упёрся — в сторону
            Ground(ref q);
            return q;
        }

        private Vector3 HidePoint()
        {
            CatHideSpot best = null; float bs = float.MinValue;
            foreach (CatHideSpot s in CatHideSpot.All)
            {
                if (s == null) continue;
                // чем дальше от игроков и ближе к коту — тем лучше
                float score = -Vector3.Distance(s.transform.position, transform.position) * 0.5f;
                Transform pl = null; float pd = float.MaxValue;
                foreach (PlayerHand h in FindObjectsByType<PlayerHand>(FindObjectsSortMode.None))
                {
                    float d = Vector3.Distance(h.transform.position, s.transform.position);
                    if (d < pd) { pd = d; pl = h.transform; }
                }
                if (pl != null) score += Mathf.Min(pd, 30f);
                if (score > bs) { bs = score; best = s; }
            }
            if (best != null) return best.transform.position;
            Transform near = NearestPlayer(out _);
            return near != null ? EvadePoint(near.position, 12f) : RandomAround(_home, _wanderRadius * 1.5f);
        }

        private bool Ground(ref Vector3 p)
        {
            if (_agent != null && _agent.enabled)
            {
                if (!NavMesh.SamplePosition(p, out NavMeshHit h, 2f, NavMesh.AllAreas)) return false;
                p = h.position; return true;
            }
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform)) { p = hit.point; return true; }
            return false;
        }

        private bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 up = Vector3.up * 0.5f;
            Vector3 d = b - a;
            return !Physics.SphereCast(a + up, 0.25f, d.normalized, out RaycastHit h, d.magnitude, ~0, QueryTriggerInteraction.Ignore)
                   || h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<FoodPiece>() != null;
        }

        // ── движение ──

        private bool MoveTo(Vector3 target, float speed, float dt, float arrive)
        {
            Vector3 pos = transform.position;
            Vector3 flat = target - pos; flat.y = 0f;
            if (flat.magnitude <= arrive) { Stop(); return true; }
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.speed = speed;
                _agent.acceleration = speed * 8f;
                _agent.angularSpeed = _turnSpeed;
                if (!_agent.hasPath || (_agent.destination - target).sqrMagnitude > 0.25f) _agent.SetDestination(target);
                Vector3 v = _agent.desiredVelocity; v.y = 0f;
                if (v.sqrMagnitude > 0.01f) Face(pos + v, dt);
                return false;
            }
            // без NavMesh: прямо, обходя препятствия
            Vector3 dir = flat.normalized;
            if (Physics.SphereCast(pos + Vector3.up * 0.5f, 0.3f, dir, out RaycastHit h, 1.2f, ~0, QueryTriggerInteraction.Ignore)
                && !h.collider.transform.IsChildOf(transform) && h.collider.GetComponentInParent<FoodPiece>() == null)
                dir = Quaternion.Euler(0f, 70f, 0f) * dir;
            Face(pos + dir, dt);
            Vector3 next = pos + transform.forward * speed * dt;
            Vector3 g = next;
            if (Physics.Raycast(next + Vector3.up * 1f, Vector3.down, out RaycastHit gh, 3f, ~0, QueryTriggerInteraction.Ignore) && !gh.collider.transform.IsChildOf(transform))
                g.y = gh.point.y;
            transform.position = g;
            return false;
        }

        private void Stop()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.ResetPath();
        }

        private void Face(Vector3 p, float dt)
        {
            Vector3 d = p - transform.position; d.y = 0f;
            if (d.sqrMagnitude < 0.0004f) return;
            Quaternion want = Quaternion.LookRotation(d.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, _turnSpeed * dt);
        }

        // ───────────────────────── ТОЛЧОК ─────────────────────────

        [ServerRpc(RequireOwnership = false)]
        private void CmdPush(Vector3 from, NetworkConnection conn = null)
        {
            if (conn == null || conn.FirstObject == null) return;
            Vector3 pl = conn.FirstObject.transform.position;
            if ((pl - transform.position).sqrMagnitude > (_pushReach + 1.2f) * (_pushReach + 1.2f)) return;
            if (Current == State.Fall) return;
            Vector3 dir = transform.position - pl; dir.y = 0f; dir.Normalize();
            if (_log) Debug.Log("[Кот-воришка] ТОЛКНУЛИ — падает!", this);
            bool had = _carried.Value != null;
            ReleaseCarried(true, dir);
            _cooldownUntil = Time.time + (had ? _cooldown : _cooldown * 0.4f);
            _speed = 0f;
            SetState(State.Fall);
        }

        // ───────────────────────── У КАЖДОГО: анимация и ЛКМ ─────────────────────────

        private static PlayerHand _localHand;
        private static GUIStyle _hint;

        private void ClientTick()
        {
            if (_anim != null)
            {
                _anim.SetSpeed(_speedNet.Value);
                switch (Current)
                {
                    case State.Wander: _anim.Play(_speedNet.Value > 0.15f ? CatThiefAnimator.Move.Walk : CatThiefAnimator.Move.Idle); break;
                    case State.Notice:
                    case State.Look: _anim.Play(CatThiefAnimator.Move.Lookout); break;
                    case State.Sneak: _anim.Play(CatThiefAnimator.Move.Sneak); break;
                    case State.Pickup: _anim.Play(CatThiefAnimator.Move.Pickup); break;
                    case State.Escape:
                    case State.Evade: _anim.Play(CatThiefAnimator.Move.Escape); break;
                    case State.Eat: _anim.Play(CatThiefAnimator.Move.Eat); break;
                    case State.Fall: _anim.Play(CatThiefAnimator.Move.Fall); break;
                    case State.Flee: _anim.Play(CatThiefAnimator.Move.Walk); break;
                }
                if (!base.IsServerInitialized && _carried.Value != null && _anim.Socket != null)
                    _carried.Value.transform.SetPositionAndRotation(_anim.Socket.position, _anim.Socket.rotation);   // без дёрганья у клиентов
            }
            if (!base.IsClientInitialized || Application.isBatchMode) return;
            if (!Input.GetMouseButtonDown(0) || UiModal.AnyOpen || Current == State.Fall) return;
            if (!CanPushNow()) return;
            CmdPush(Camera.main.transform.position);
        }

        /// <summary>Кот в 2 м перед камерой и руки пустые.</summary>
        private bool CanPushNow()
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 to = transform.position + Vector3.up * 0.4f - cam.transform.position;
            if (to.magnitude > _pushReach + 0.8f) return false;
            if (Vector3.Dot(cam.transform.forward, to.normalized) < 0.55f) return false;
            if (_localHand == null)
                foreach (PlayerHand h in FindObjectsByType<PlayerHand>(FindObjectsSortMode.None))
                    if (h.IsOwner) { _localHand = h; break; }
            return _localHand == null || !_localHand.HasItem;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            State s = Current;
            if (s != State.Escape && s != State.Evade && s != State.Pickup) return;
            if (!CanPushNow()) return;
            if (_hint == null) { _hint = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter }; _hint.normal.textColor = Color.white; }
            GUI.Box(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f + 60f, 360f, 36f), "ЛКМ — толкнуть кота!", _hint);
        }
    }
}
