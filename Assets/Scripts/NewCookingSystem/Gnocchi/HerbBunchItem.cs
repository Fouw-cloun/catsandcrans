using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ПУЧОК ЗЕЛЕНИ (шалфей, «веник») — рвёт дозу над тарой.
    ///
    /// Пучок в руке. Смотришь на тару и жмёшь ПКМ (один раз): рука подносит пучок к горловине,
    /// кот дёргает-перекручивает его — с пучка срываются листья и падают в тару. Это ОДНА доза.
    /// Доза — щепотка: места в таре не занимает (как у баночки специй).
    /// Смотришь мимо тары — листья падают на пол (ничего не добавляется).
    ///
    /// По сети: доза добавляется на сервере, рывок и листья видят все.
    ///
    /// ═══ ТРЕБУЕТ ═══ NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
    /// ═══ НАСТРОЙКА ═══ CatCooks → Ньокки → 10в: выдели модель пучка — меню добавит всё сразу.
    /// </summary>
    public class HerbBunchItem : PickupItem
    {
        [Header("Что рвёт")]
        [Tooltip("Продукт: Шалфей (или любая зелень)")]
        [SerializeField] private ProductDef _product;
        [Tooltip("Порций продукта в одной дозе (одно нажатие ПКМ)")]
        [Min(1)][SerializeField] private int _portionsPerDose = 1;
        [Tooltip("Бесконечный пучок")]
        [SerializeField] private bool _infinite = true;
        [Tooltip("Сколько доз в пучке, если не бесконечный")]
        [Min(1)][SerializeField] private int _doses = 12;
        [Tooltip("Сколько длится доза (поднести + рывок), с")]
        [Range(0.5f, 3f)][SerializeField] private float _doseSeconds = 1.1f;

        [Header("Рука и прицел")]
        [Tooltip("Докуда тянется рука от камеры, м")]
        [SerializeField] private float _reach = 2.2f;
        [Tooltip("На какой высоте над горловиной держать пучок, м")]
        [SerializeField] private float _aboveMouth = 0.25f;
        [Tooltip("Слои, где искать тару прицелом")]
        [SerializeField] private LayerMask _aimMask = ~0;
        [Tooltip("Размах рывка-перекрута, градусы")]
        [Range(0f, 90f)][SerializeField] private float _twist = 40f;

        [Header("Листья")]
        [Tooltip("Цвет листьев. Прозрачный — Base Color продукта")]
        [SerializeField] private Color _leafColor = new Color(0f, 0f, 0f, 0f);
        [Range(1, 12)][SerializeField] private int _leavesPerDose = 6;
        [Tooltip("Размер листа, м")]
        [SerializeField] private float _leafSize = 0.07f;
        [Tooltip("Верх пучка (откуда рвутся листья). Пусто — центр модели")]
        [SerializeField] private Transform _top;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ushort> _left = new SyncVar<ushort>(0);
        private readonly SyncVar<bool> _tearing = new SyncVar<bool>(false);
        private readonly SyncVar<NetworkObject> _target = new SyncVar<NetworkObject>();

        private Cookware _aimCw, _doseCw;
        private float _t, _doseT, _startAt;
        private bool _delivered, _tearWas, _leavesThrown;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private struct FallLeaf { public Transform T; public Vector3 V; public float Spin, Floor, Age; }
        private readonly List<FallLeaf> _fall = new List<FallLeaf>();
        private static Mesh _leafMesh;
        private Material _leafMat;
        private static GUIStyle _st;

        private const float TearAt = 0.55f;    // доля дозы: рывок (листья срываются)

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _left.Value = (ushort)Mathf.Clamp(_doses, 0, ushort.MaxValue);
            if (_product == null) Debug.LogWarning($"[Пучок] «{name}»: не задан продукт (Шалфей) — меню Ньокки → 10в", this);
        }

        private bool HasLeaves => _infinite || _left.Value > 0;
        private Transform Top => _top != null ? _top : transform;
        private Color LeafColor => _leafColor.a > 0.01f ? _leafColor : (_product != null ? _product.BaseColor : new Color(0.45f, 0.58f, 0.38f));

        private void Update()
        {
            if (base.IsOwner) OwnerInput();
            bool on = _tearing.Value;
            if (on && !_tearWas) { _startAt = Time.time; _leavesThrown = false; }
            _tearWas = on;
            _t = Mathf.MoveTowards(_t, on ? 1f : 0f, Time.deltaTime * 5f);
            if (on && !_leavesThrown && Time.time - _startAt >= _doseSeconds * TearAt) { _leavesThrown = true; ThrowLeaves(); }
            if (base.IsServerInitialized) ServerTick(Time.deltaTime);
            TickLeaves(Time.deltaTime);
        }

        private void OwnerInput()
        {
            if (!_tearing.Value) _aimCw = AimCookware();
            if (Input.GetMouseButtonDown(1) && !_tearing.Value)
            {
                _doseCw = _aimCw;
                CmdTear(_doseCw != null ? _doseCw.NetworkObject : null);
            }
        }

        private Cookware AimCookware()
        {
            Camera cam = Camera.main;
            if (cam == null) return null;
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _hits, _reach, _aimMask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue; Cookware res = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(transform)) continue;
                if (_hits[i].collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                Cookware cw = _hits[i].collider.GetComponentInParent<Cookware>();
                if (cw == null || _hits[i].distance >= best) continue;
                best = _hits[i].distance; res = cw;
            }
            return res;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdTear(NetworkObject target)
        {
            if (_tearing.Value || _product == null) return;
            if (!HasLeaves) { if (_log) Debug.Log($"[Пучок] «{name}»: листья кончились", this); return; }
            _target.Value = target;
            _tearing.Value = true;
            _doseT = 0f; _delivered = false;
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            if (_tearing.Value) _tearing.Value = false;
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void ServerTick(float dt)
        {
            if (!_tearing.Value) return;
            _doseT += dt;
            if (!_delivered && _doseT >= _doseSeconds * (TearAt + 0.15f))     // листья долетели
            {
                _delivered = true;
                Cookware cw = null;
                if (_target.Value != null)
                {
                    Cookware t = CookwareLookup.Of(_target.Value, Top.position);
                    if (t != null && Vector3.Distance(t.MouthPoint, Top.position) < 1.5f) cw = t;
                }
                if (cw == null) { if (_log) Debug.Log($"[Пучок] {_product.Title}: мимо тары — листья на пол", this); }
                else
                {
                    cw.ServerAddPinch(_product.Id, _product.StartState, _portionsPerDose, 0);
                    if (_log) Debug.Log($"[Пучок] +{_portionsPerDose} {_product.Title} в «{(cw.Def != null ? cw.Def.Title : cw.name)}» (там теперь {cw.CountOf(_product.Id)})", this);
                }
                if (!_infinite && _left.Value > 0) _left.Value = (ushort)(_left.Value - 1);
            }
            if (_doseT >= _doseSeconds) _tearing.Value = false;
        }

        // ───────────────────────── ВИД ─────────────────────────

        public override void UpdateHeldPose(Transform handAnchor)
        {
            base.UpdateHeldPose(handAnchor);
            if (_t <= 0.001f) return;
            float k = Mathf.SmoothStep(0f, 1f, _t);
            Camera cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : transform.forward;
            Cookware cw = _tearing.Value ? (_doseCw != null ? _doseCw : CookwareLookup.Of(_target.Value, transform.position)) : _aimCw;
            Vector3 aim = cw != null ? cw.MouthPoint + Vector3.up * _aboveMouth
                                     : (cam != null ? cam.transform.position + fwd * 0.8f + Vector3.down * 0.1f : transform.position);
            // рывок: резкий перекрут вокруг вертикали и подёргивание вверх в момент отрыва
            float ph = _tearing.Value ? (Time.time - _startAt) / Mathf.Max(0.1f, _doseSeconds) : 1f;
            float jerk = Mathf.Exp(-Mathf.Pow((ph - TearAt) / 0.08f, 2f));
            Quaternion tw = Quaternion.AngleAxis(_twist * jerk * Mathf.Sin(ph * 30f), Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, tw * transform.rotation, k);
            transform.position += (aim - Top.position) * k + Vector3.up * (0.06f * jerk * k);
        }

        private void ThrowLeaves()
        {
            if (_leafMat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                if (s == null) s = Shader.Find("Standard");
                _leafMat = new Material(s) { name = "HerbLeaf (runtime)" };
                Color c = LeafColor; c.a = 1f;
                if (_leafMat.HasProperty("_BaseColor")) _leafMat.SetColor("_BaseColor", c);
                _leafMat.color = c;
                if (_leafMat.HasProperty("_Cull")) _leafMat.SetFloat("_Cull", 0f);
            }
            Cookware cw = CookwareLookup.Of(_target.Value, Top.position);
            float floor = cw != null ? cw.MouthPoint.y - 0.15f : Top.position.y - 1.5f;
            for (int i = 0; i < _leavesPerDose; i++)
            {
                var go = new GameObject("HerbLeaf (auto)");
                go.AddComponent<MeshFilter>().sharedMesh = LeafMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _leafMat;
                Transform t = go.transform;
                t.position = Top.position + Random.insideUnitSphere * 0.05f;
                t.rotation = Random.rotation;
                t.localScale = Vector3.one * _leafSize * Random.Range(0.75f, 1.2f);
                _fall.Add(new FallLeaf { T = t, V = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.2f, 0.6f), Random.Range(-0.3f, 0.3f)), Spin = Random.Range(-360f, 360f), Floor = floor, Age = 0f });
            }
        }

        private void TickLeaves(float dt)
        {
            for (int i = _fall.Count - 1; i >= 0; i--)
            {
                FallLeaf f = _fall[i];
                if (f.T == null) { _fall.RemoveAt(i); continue; }
                f.Age += dt;
                f.V += Physics.gravity * (0.35f * dt);
                f.V *= 1f - 2.2f * dt;                                          // лист парит
                f.V.x += Mathf.Sin(f.Age * 7f + i) * 0.6f * dt;                // покачивается
                f.T.position += f.V * dt;
                f.T.Rotate(Vector3.up, f.Spin * dt, Space.World);
                if (f.T.position.y <= f.Floor || f.Age > 3f)
                {
                    f.T.localScale *= Mathf.Max(0f, 1f - 8f * dt);              // упал — растворился
                    if (f.T.localScale.x < 0.002f) { Destroy(f.T.gameObject); _fall.RemoveAt(i); continue; }
                }
                _fall[i] = f;
            }
        }

        private static Mesh LeafMesh
        {
            get
            {
                if (_leafMesh != null) return _leafMesh;
                const int N = 8;
                var v = new List<Vector3>(); var t = new List<int>();
                for (int i = 0; i <= N; i++)
                {
                    float z = i / (float)N - 0.5f, w = Mathf.Sqrt(Mathf.Max(0f, 1f - 4f * z * z)) * 0.3f;
                    v.Add(new Vector3(-w, 0f, z)); v.Add(new Vector3(0f, 0.04f, z)); v.Add(new Vector3(w, 0f, z));
                }
                for (int i = 0; i < N; i++)
                {
                    int a = i * 3;
                    for (int k = 0; k < 2; k++)
                    {
                        int p0 = a + k, p1 = a + k + 1, p2 = a + 3 + k, p3 = a + 4 + k;
                        t.Add(p0); t.Add(p2); t.Add(p1); t.Add(p1); t.Add(p2); t.Add(p3);
                        t.Add(p0); t.Add(p1); t.Add(p2); t.Add(p1); t.Add(p3); t.Add(p2);
                    }
                }
                _leafMesh = new Mesh { name = "HerbLeaf", hideFlags = HideFlags.DontSave };
                _leafMesh.SetVertices(v); _leafMesh.SetTriangles(t, 0); _leafMesh.RecalculateNormals(); _leafMesh.RecalculateBounds();
                return _leafMesh;
            }
        }

        private void OnDestroy()
        {
            foreach (FallLeaf f in _fall) if (f.T != null) Destroy(f.T.gameObject);
            _fall.Clear();
            if (_leafMat != null) Destroy(_leafMat);
        }

        private void OnGUI()
        {
            if (!base.IsOwner || _product == null || _tearing.Value || Event.current.type != EventType.Repaint) return;
            Cookware cw = _aimCw;
            if (cw == null) return;
            string left = _infinite ? "" : $" · в пучке {_left.Value}";
            string t = $"ПКМ — оторвать дозу: {_product.Title} → {(cw.Def != null ? cw.Def.Title : cw.name)} (там {cw.CountOf(_product.Id)}){left}";
            if (_st == null) _st = new GUIStyle(GUI.skin.box) { fontSize = 14 };
            Vector2 size = _st.CalcSize(new GUIContent(t));
            GUI.Box(new Rect(Screen.width * 0.5f - size.x * 0.5f - 6f, Screen.height * 0.5f + 34f, size.x + 12f, 24f), t, _st);
        }

#if UNITY_EDITOR
        public void EditorSetup(ProductDef product, Transform top)
        {
            UnityEditor.Undo.RecordObject(this, "Пучок");
            _product = product; _top = top;
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
#endif
    }
}
