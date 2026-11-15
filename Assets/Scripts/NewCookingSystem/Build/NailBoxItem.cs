using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// v22: КОРОБКА ГВОЗДЕЙ. Берёшь в руки (F), наводишься на светящуюся точку стройки — ЛКМ: гвоздь встаёт
    /// и торчит, дальше его забивают молотком. В коробке Count гвоздей (видно в подсказке).
    /// Модель рисуется сама (картонная коробка с гвоздями сверху), если у объекта нет своего меша.
    /// Создать: меню CatCooks → Стройка → 2. Коробка гвоздей в сцену.
    /// </summary>
    public sealed class NailBoxItem : PickupItem
    {
        [Header("Гвозди")]
        [Range(1, 250)][SerializeField] private int _startCount = 60;
        [Tooltip("Секунд между гвоздями")]
        [Range(0.05f, 1f)][SerializeField] private float _interval = 0.2f;
        [SerializeField] private Color _boxColor = new Color(0.78f, 0.6f, 0.38f);
        [SerializeField] private bool _hint = true;
        [Tooltip("v23: во сколько раз увеличить коробку (коллайдер и авто-модель). 1 — как в v22.")]
        [Range(0.5f, 6f)][SerializeField] private float _sizeK = 3f;

        private readonly SyncVar<int> _count = new SyncVar<int>(60);
        private float _next;
        private string _status = "";

        public override void OnStartServer()
        {
            base.OnStartServer();
            _count.Value = _startCount;
        }

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            bool auto = GetComponentInChildren<MeshRenderer>() == null;
            if (!Mathf.Approximately(_sizeK, 1f))
            {
                BoxCollider bc = GetComponent<BoxCollider>();
                if (auto && bc != null) { bc.size *= _sizeK; bc.center *= _sizeK; }   // v23: коробка крупнее
                else if (!auto) transform.localScale *= _sizeK;                           // своя модель — масштабом
            }
            if (auto) BuildModel();
        }

        private void Update()
        {
            if (!base.IsOwner || !IsHeldByLocal()) { _status = ""; return; }
            BuildManager bm = BuildManager.Instance;
            Camera cam = Camera.main;
            if (bm == null || cam == null) { _status = "нет стройки в сцене (CatCooks → Стройка → 1)"; return; }
            int site, nail;
            bool aim = bm.AimNail(new Ray(cam.transform.position, cam.transform.forward), false, out site, out nail);
            _status = _count.Value <= 0 ? "коробка пуста" : aim ? "ЛКМ — вбить гвоздь сюда" : "наведись на светящуюся точку формы";
            if (!aim || _count.Value <= 0 || Time.time < _next || !Input.GetMouseButtonDown(0)) return;
            _next = Time.time + _interval;
            CmdNail(site, nail);
        }

        private bool IsHeldByLocal()
        {
            PlayerHand h = GetComponentInParent<PlayerHand>();
            if (h != null) return h.IsOwner && h.HeldItem == this;
            return CCHands.HeldByLocal(this);                                    // v23.2 (FPS): без поиска по сцене каждый кадр
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdNail(int site, int nail)
        {
            if (_count.Value <= 0) return;
            BuildManager bm = BuildManager.Instance;
            if (bm == null) return;
            if (bm.ServerPlaceNail(site, nail)) _count.Value--;
        }

        private void OnGUI()
        {
            if (!_hint || string.IsNullOrEmpty(_status)) return;
            GUIStyle st = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            GUI.Box(new Rect(Screen.width * 0.5f - 210f, Screen.height - 110f, 420f, 28f), $"Гвозди: {_count.Value} · {_status}", st);
        }

        /// <summary>Картонная коробка (открытая сверху) с торчащими гвоздями.</summary>
        private void BuildModel()
        {
            var root = new GameObject("Model (auto)").transform;
            root.SetParent(transform, false);
            Shader sh = Shader.Find("CatCooks/Toon Props");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            var box = new Material(sh) { name = "NailBox (auto)" };
            if (box.HasProperty("_BaseColor")) box.SetColor("_BaseColor", _boxColor); else box.color = _boxColor;
            var nailM = new Material(sh) { name = "Nail (auto)" };
            Color steel = new Color(0.62f, 0.64f, 0.68f);
            if (nailM.HasProperty("_BaseColor")) nailM.SetColor("_BaseColor", steel); else nailM.color = steel;
            if (nailM.HasProperty("_Smoothness")) nailM.SetFloat("_Smoothness", 0.7f);

            BoxCollider bc = GetComponent<BoxCollider>();
            Vector3 size = bc != null ? bc.size : new Vector3(0.16f, 0.09f, 0.11f) * _sizeK;
            Vector3 c = bc != null ? bc.center : Vector3.zero;
            float t = Mathf.Min(size.x, size.z) * 0.06f;
            // дно и 4 стенки
            Part(root, c + new Vector3(0f, -size.y * 0.5f + t * 0.5f, 0f), new Vector3(size.x, t, size.z), box);
            Part(root, c + new Vector3(0f, 0f, size.z * 0.5f - t * 0.5f), new Vector3(size.x, size.y, t), box);
            Part(root, c + new Vector3(0f, 0f, -size.z * 0.5f + t * 0.5f), new Vector3(size.x, size.y, t), box);
            Part(root, c + new Vector3(size.x * 0.5f - t * 0.5f, 0f, 0f), new Vector3(t, size.y, size.z), box);
            Part(root, c + new Vector3(-size.x * 0.5f + t * 0.5f, 0f, 0f), new Vector3(t, size.y, size.z), box);
            // гвозди внутри — кучей, остриями в разные стороны, шляпки торчат
            var rnd = new System.Random(7);
            float len = size.y * 0.9f;
            for (int i = 0; i < 18; i++)
            {
                var g = new GameObject("Nail");
                g.transform.SetParent(root, false);
                float x = ((float)rnd.NextDouble() - 0.5f) * (size.x - t * 3f);
                float z = ((float)rnd.NextDouble() - 0.5f) * (size.z - t * 3f);
                g.transform.localPosition = c + new Vector3(x, size.y * 0.5f - len * 0.25f, z);
                g.transform.localRotation = Quaternion.Euler(90f + ((float)rnd.NextDouble() - 0.5f) * 70f, (float)rnd.NextDouble() * 360f, 0f);
                g.transform.localScale = Vector3.one * len;
                g.AddComponent<MeshFilter>().sharedMesh = BuildSiteView.NailMesh;
                g.AddComponent<MeshRenderer>().sharedMaterial = nailM;
            }
        }

        private static void Part(Transform root, Vector3 pos, Vector3 size, Material m)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(g.GetComponent<Collider>());
            g.name = "Box";
            g.transform.SetParent(root, false);
            g.transform.localPosition = pos;
            g.transform.localScale = size;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
    }
}
