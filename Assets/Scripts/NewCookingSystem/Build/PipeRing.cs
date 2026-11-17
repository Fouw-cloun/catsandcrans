using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// v23.52: КОЛЬЦО ИЗ ТРУБЫ — отпиленный обрубок (стальной, торцы жёлтые). Берётся в руки (F), бросается.
    /// Брошенное в шаблон «Противень для макарон» (этап колец) — встаёт в своё гнездо (BuildManager).
    ///
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · Box Collider · этот компонент. Модель строится сама.
    /// Делает меню «Стройка → 5» (объект для префаба: сохрани префабом, добавь в Spawnable Prefabs, перетащи в трубу → Ring Prefab).
    /// </summary>
    public sealed class PipeRing : PickupItem
    {
        [SerializeField] private float _innerRadius = 0.09f;
        [SerializeField] private float _outerRadius = 0.1f;
        [SerializeField] private float _height = 0.03f;
        [Tooltip("Материал (цвет в вершинах). Пусто — свой")]
        [SerializeField] private Material _material;

        // размеры приходят от трубы (сервер ставит до спавна — клиенты получают при появлении)
        private readonly SyncVar<Vector3> _dims = new SyncVar<Vector3>(Vector3.zero);

        /// <summary>Уже ушло в стройку (сервер).</summary>
        public bool Consumed { get; set; }
        /// <summary>Сейчас в руках (сервер).</summary>
        public bool IsHeldNow { get; private set; }

        private MeshFilter _mf;
        private Vector3 _shown;

        protected override void Awake()
        {
            BoxCollider bc = GetComponent<BoxCollider>();
            if (bc == null) bc = gameObject.AddComponent<BoxCollider>();
            base.Awake();
            _canPickup = true;
            Build(Dims);
        }

        private Vector3 Dims => _dims.Value.sqrMagnitude > 1e-10f ? _dims.Value : new Vector3(_innerRadius, _outerRadius, _height);

        /// <summary>Сервер, до спавна: размеры от трубы.</summary>
        public void Setup(float ri, float ro, float h, Material mat)
        {
            _dims.Value = new Vector3(ri, ro, h);
            if (mat != null) _material = mat;
            Build(Dims);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _dims.OnChange += OnDims;
            Build(Dims);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _dims.OnChange -= OnDims;
        }

        private void OnDims(Vector3 a, Vector3 b, bool asServer) => Build(Dims);

        public override void ServerOnPickup(PlayerHand hand) { base.ServerOnPickup(hand); IsHeldNow = true; }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            base.ServerOnDrop(hand, tossDirection, force);
            IsHeldNow = false;
        }

        private void Build(Vector3 d)
        {
            if (d == _shown && _mf != null) return;
            _shown = d;
            if (_mf == null)
            {
                Transform t = transform.Find("Ring (auto)");
                if (t == null)
                {
                    var go = new GameObject("Ring (auto)");
                    go.layer = gameObject.layer;
                    go.transform.SetParent(transform, false);
                    t = go.transform;
                    t.gameObject.AddComponent<MeshFilter>();
                    MeshRenderer mr = t.gameObject.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _material != null ? _material : AutoMat();
                }
                _mf = t.GetComponent<MeshFilter>();
            }
            if (_mf.sharedMesh != null && _mf.sharedMesh.name.EndsWith("(auto)")) Destroy(_mf.sharedMesh);
            Mesh m = MacaronMesh.Tube(d.x, d.y, d.z, PipeItem.PipeColor, PipeItem.CutColor, 32);
            m.name = "PipeRing (auto)";
            m.hideFlags = HideFlags.DontSave;
            _mf.sharedMesh = m;
            _mf.transform.localPosition = new Vector3(0f, -d.z * 0.5f, 0f);            // центр кольца — в центре объекта
            BoxCollider bc = GetComponent<BoxCollider>();
            if (bc != null) { bc.center = Vector3.zero; bc.size = new Vector3(d.y * 2f, d.z, d.y * 2f); }
        }

        private static Material _auto;
        private static Material AutoMat()
        {
            if (_auto != null) return _auto;
            Shader sh = Shader.Find("CatCooks/Food Heap");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            _auto = new Material(sh) { name = "PipeRing (auto)", hideFlags = HideFlags.DontSave };
            if (_auto.HasProperty("_BaseColor")) _auto.SetColor("_BaseColor", Color.white);
            if (_auto.HasProperty("_TexDetail")) _auto.SetFloat("_TexDetail", 0f);
            return _auto;
        }

#if UNITY_EDITOR
        public void EditorSetup(float ri, float ro, float h, Material mat)
        {
            _innerRadius = ri; _outerRadius = ro; _height = h; _material = mat;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
