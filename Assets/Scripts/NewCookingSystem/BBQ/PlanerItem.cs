using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЭЛЕКТРОРУБАНОК — зачистка мяса: снимает плёнку, жир и шкуру.
    ///   РЁБРА: ЛКМ зажать на СЫРЫХ рёбрах и водить мышью — камера стоит, плёнка уходит там, где провёл.
    ///          Переверни (R) — и вторую сторону.
    ///   КУСКИ НОВОЙ ТУШИ (Meat Cut): ЛКМ зажать на сыром куске и водить ВЗГЛЯДОМ по нему —
    ///          шкура (свинина) или жир с плёнкой (говядина) снимаются, под ними чистое мясо.
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · коллайдер · этот компонент.
    /// Blade — по желанию: барабан с ножами (крутится, пока работает).
    /// </summary>
    public sealed class PlanerItem : PickupItem
    {
        [SerializeField] private Transform _blade;
        [SerializeField] private float _reach = 3f;
        [SerializeField] private float _bladeSpin = 2400f;
        [SerializeField] private Color _shavingColor = new Color(0.95f, 0.92f, 0.86f);
        [SerializeField] private bool _log = true;

        private readonly SyncVar<bool> _working = new SyncVar<bool>(false);
        private readonly HeldToolLook _look = new HeldToolLook();
        private readonly RackStroke _stroke = new RackStroke();
        private ParticleSystem _chips;
        private float _chipsAcc;
        private Vector3 _chipsAt;
        private MeatCut _meat;
        private Vector3 _meatLast, _meatPoint, _meatNormal;
        private float _meatAcc, _meatSend;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_blade == null) _blade = transform.Find("Blade");
        }

        private void Update()
        {
            if (base.IsOwner && _look.Held) OwnerInput();
            else if (base.IsOwner && (_stroke.Active || _meat != null)) { _stroke.End(); _meat = null; CmdWork(false, Vector3.zero); }
            if (_blade != null && _working.Value) _blade.Rotate(Vector3.right, _bladeSpin * Time.deltaTime, Space.Self);
            Chips();
        }

        private void OwnerInput()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            if (_stroke.Active)
            {
                if (!Input.GetMouseButton(0)) { _stroke.End(); CmdWork(false, Vector3.zero); return; }
                if (_stroke.Tick(cam, out float st) && _stroke.Rack != null)
                    CmdPlane(_stroke.Rack.NetworkObject, _stroke.Local, (byte)_stroke.Side, st, _stroke.WorldPoint);
                return;
            }
            if (_meat != null) { MeatInput(cam); return; }
            if (Input.GetMouseButtonDown(0))
            {
                if (_stroke.TryBegin(cam, _reach, r => !r.IsCooking, out RibsRack aimed, transform)) { CmdWork(true, _stroke.WorldPoint); return; }
                if (aimed != null) { if (_log) Debug.Log("[Рубанок] рубанком — только сырые рёбра", this); return; }
                if (AimMeat(cam, out MeatCut m, out RaycastHit h))
                {
                    if (m.IsCookedState) { if (_log) Debug.Log("[Рубанок] рубанком — только сырое мясо", this); return; }
                    _meat = m; _meatLast = h.point; _meatPoint = h.point; _meatNormal = h.normal; _meatAcc = 0f; _meatSend = Time.time + 0.08f;
                    CmdWork(true, h.point);
                }
            }
        }

        private bool AimMeat(Camera cam, out MeatCut m, out RaycastHit hit)
        {
            m = null;
            if (!ToolRay.Cast(cam, _reach, transform, out hit) || hit.collider == null) return false;
            m = hit.collider.GetComponentInParent<MeatCut>();
            return m != null;
        }

        /// <summary>Кусок мяса: камера свободна — водишь взглядом, точка бежит по куску.</summary>
        private void MeatInput(Camera cam)
        {
            if (!Input.GetMouseButton(0) || _meat == null || !_meat.isActiveAndEnabled) { _meat = null; CmdWork(false, Vector3.zero); return; }
            if (AimMeat(cam, out MeatCut m, out RaycastHit h) && m == _meat)
            {
                _meatAcc += Vector3.Distance(h.point, _meatLast);
                _meatLast = h.point; _meatPoint = h.point; _meatNormal = h.normal;
            }
            if (Time.time < _meatSend) return;
            _meatSend = Time.time + 0.08f;
            if (_meatAcc < 0.0005f) { _meatAcc = 0f; return; }
            CmdPlaneMeat(_meat.NetworkObject, _meatPoint, Mathf.Min(_meatAcc, 0.5f));
            _meatAcc = 0f;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdPlaneMeat(NetworkObject meat, Vector3 world, float stroke)
        {
            if (meat == null) return;
            MeatCut m = meat.GetComponent<MeatCut>();
            if (m == null || (m.transform.position - transform.position).sqrMagnitude > 144f) return;
            if (!_working.Value) _working.Value = true;
            if (m.ServerCleanAt(world, stroke, out string hint)) RpcChips(world, stroke);
            else if (hint != null && _log) Debug.Log("[Рубанок] " + hint, this);
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdWork(bool on, Vector3 at)
        {
            if (_working.Value != on) _working.Value = on;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdPlane(NetworkObject rack, Vector3 local, byte side, float stroke, Vector3 world)
        {
            if (rack == null) return;
            RibsRack r = rack.GetComponent<RibsRack>();
            if (r == null || (r.transform.position - transform.position).sqrMagnitude > 144f) return;
            if (!_working.Value) _working.Value = true;
            if (r.ServerCleanAt(local, side, Mathf.Min(stroke, 0.5f))) RpcChips(world, stroke);
        }

        [ObserversRpc]
        private void RpcChips(Vector3 at, float stroke)
        {
            _chipsAt = at;
            _chipsAcc += Mathf.Clamp(stroke * 260f, 2f, 18f);
        }

        private void Chips()
        {
            if (_chipsAcc < 1f) return;
            if (_chips == null) _chips = MakeChips();
            if (_chips == null) return;
            int n = Mathf.Min(20, Mathf.FloorToInt(_chipsAcc));
            _chipsAcc -= n;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = _chipsAt + Random.insideUnitSphere * 0.03f;
                ep.velocity = (Vector3.up * 1.2f + Random.insideUnitSphere * 1.1f);
                ep.startSize = Random.Range(0.012f, 0.03f);
                ep.startLifetime = Random.Range(0.7f, 1.3f);
                ep.rotation3D = Random.insideUnitSphere * 180f;
                ep.startColor = _shavingColor * Random.Range(0.85f, 1.05f);
                _chips.Emit(ep, 1);
            }
        }

        private ParticleSystem MakeChips()
        {
            var go = new GameObject("PlanerShavings (auto)");
            go.SetActive(false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var m = ps.main;
            m.playOnAwake = false; m.loop = false; m.maxParticles = 600;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.gravityModifier = 1.2f;
            m.startRotation3D = true;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var rl = ps.rotationOverLifetime; rl.enabled = true; rl.separateAxes = true;
            rl.x = new ParticleSystem.MinMaxCurve(-6f, 6f); rl.y = new ParticleSystem.MinMaxCurve(-6f, 6f); rl.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = ShavingMesh();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Shader shd = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (shd != null) r.sharedMaterial = new Material(shd) { name = "PlanerShavings (auto)" };
            go.SetActive(true);
            return ps;
        }

        /// <summary>Стружка — завиток из полоски.</summary>
        private static Mesh ShavingMesh()
        {
            var m = new Mesh { name = "Shaving (auto)" };
            const int seg = 8;
            var v = new Vector3[(seg + 1) * 2];
            var t = new int[seg * 12];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 1.4f, r = 0.5f - i * 0.03f;
                Vector3 p = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                v[i * 2] = p + Vector3.forward * 0.25f; v[i * 2 + 1] = p - Vector3.forward * 0.25f;
            }
            int k = 0;
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 2;
                t[k++] = a; t[k++] = b; t[k++] = a + 1; t[k++] = b; t[k++] = b + 1; t[k++] = a + 1;
                t[k++] = a; t[k++] = a + 1; t[k++] = b; t[k++] = b; t[k++] = a + 1; t[k++] = b + 1;   // обе стороны
            }
            m.vertices = v; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            _look.Pose(handAnchor);
            base.UpdateHeldPose(handAnchor);
            if (_stroke.Active)
            {
                _stroke.Pose(transform, 0.05f);
                transform.position += Random.insideUnitSphere * 0.002f;      // жужжит
            }
            else if (_meat != null && base.IsOwner)
            {
                transform.position = Vector3.Lerp(transform.position, _meatPoint + _meatNormal * 0.05f, 0.7f);
                transform.position += Random.insideUnitSphere * 0.002f;
            }
        }

        private void LateUpdate()
        {
            if (base.IsOwner) _look.Late(_stroke.Active, _stroke.Active);
        }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            _working.Value = false;
            _meat = null;
            _look.Release();
            base.ServerOnDrop(hand, tossDirection, force);
        }

        private void OnGUI()
        {
            if (!base.IsOwner || !_look.Held || Event.current.type != EventType.Repaint) return;
            GUI.Label(new Rect(Screen.width * 0.5f - 330f, Screen.height * 0.66f, 660f, 24f),
                      "рубанок · ЛКМ на сырых рёбрах — води мышью · на куске мяса — води взглядом · снять плёнку/шкуру");
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform blade) { _blade = blade; UnityEditor.EditorUtility.SetDirty(this); }
#endif
    }
}
