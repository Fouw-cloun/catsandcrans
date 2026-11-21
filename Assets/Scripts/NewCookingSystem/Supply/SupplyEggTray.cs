using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Supply
{
    /// <summary>
    /// v24.9: ЛОТОК ЯИЦ — источник яиц для поставки (как мешок для сахара).
    /// Яйца в игре крупные, и 60 штук россыпью занимали ~10 ящиков; лоток кладёт 30 яиц в один предмет.
    ///
    /// F с пустой рукой — из лотка достаётся одно настоящее яйцо (префаб Egg Prefab) прямо в руку.
    /// Пустой лоток можно взять в руки и унести (F). Яйца в лотке — только вид (уменьшенные копии модели яйца),
    /// их число = сколько осталось.
    ///
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · Collider · этот компонент;
    /// дети: «Slots» — по объекту на ячейку (меш яйца). Собирает меню CatCooks → Доставка → 5б.
    /// Доставка выставляет запас (Capacity) до спавна — ровно сколько нужно по меню.
    /// </summary>
    public sealed class SupplyEggTray : PickupItem
    {
        [Header("Лоток яиц")]
        [Tooltip("Продукт (Яйцо) — для каталога доставки")]
        [SerializeField] private ProductDef _product;
        [Tooltip("Настоящее яйцо, которое выдаётся в руку (префаб с EggItem)")]
        [SerializeField] private NetworkObject _eggPrefab;
        [Tooltip("Сколько яиц в лотке (доставка ставит сама по меню)")]
        [Min(1)][SerializeField] private int _capacity = 30;
        [Tooltip("Бесконечный (для тестов)")]
        [SerializeField] private bool _infinite;
        [Tooltip("Ячейки с яйцами (вид): включено столько, сколько яиц осталось")]
        [SerializeField] private Transform _slots;

        private readonly SyncVar<int> _left = new SyncVar<int>(0);

        public ProductDef Product => _product;
        public int Left => _left.Value;

        protected override void Awake()
        {
            base.Awake();
            SupplyCrate.MakeInteractable(gameObject);                        // v24.9: F игрока видит только слои Interactable/Food/…
            _canPickup = false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _left.Value = _capacity;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _left.OnChange += OnLeft;
            ShowSlots();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _left.OnChange -= OnLeft;
        }

        private void OnLeft(int a, int b, bool asServer) => ShowSlots();

        /// <summary>F: достать яйцо в руку; пустой лоток — взять сам лоток.</summary>
        public override void ServerInteract(PlayerInteractor interactor)
        {
            if (interactor == null || interactor.Hand == null) return;
            if (_left.Value <= 0 && !_infinite)
            {
                _canPickup = true;
                base.ServerInteract(interactor);
                return;
            }
            if (interactor.Hand.HasItem || _eggPrefab == null)
            {
                if (_eggPrefab == null) Debug.LogWarning("[Доставка] у лотка не задан Egg Prefab", this);
                return;
            }
            Collider bc = GetComponent<Collider>();
            Vector3 p = bc != null ? new Vector3(transform.position.x, bc.bounds.max.y + 0.5f, transform.position.z) : transform.position + transform.up * 1.2f;
            NetworkObject egg = Instantiate(_eggPrefab, p, transform.rotation);
            ServerManager.Spawn(egg.gameObject);
            InteractableItem item = egg.GetComponent<InteractableItem>();
            if (item == null || !interactor.Hand.ServerTryHold(item))
            {
                // не взялось в руку — яйцо остаётся лежать на лотке
            }
            if (!_infinite) _left.Value = Mathf.Max(0, _left.Value - 1);
            if (_left.Value <= 0) _canPickup = true;
        }

        private void ShowSlots()
        {
            if (_slots == null) return;
            int left = _infinite ? _slots.childCount : _left.Value;
            for (int i = 0; i < _slots.childCount; i++)
            {
                GameObject s = _slots.GetChild(i).gameObject;
                bool on = i < left;
                if (s.activeSelf != on) s.SetActive(on);
            }
        }

        // ───────────── подсказка ─────────────
        private static GUIStyle _style;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            Camera cam = Camera.main;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 25f) return;
            if (!Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit h, 4f, ~0, QueryTriggerInteraction.Ignore)) return;
            if (h.collider.GetComponentInParent<SupplyEggTray>() != this) return;
            string s = _left.Value > 0 || _infinite
                ? $"Яйца: {(_infinite ? "∞" : _left.Value.ToString())}   ·   F — взять яйцо"
                : "Ящик для яиц пустой   ·   F — взять";
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            Rect rc = new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.5f + 40f, 520f, 40f);
            _style.normal.textColor = Color.black; GUI.Label(new Rect(rc.x + 1, rc.y + 1, rc.width, rc.height), s, _style);
            _style.normal.textColor = new Color(1f, 0.95f, 0.8f); GUI.Label(rc, s, _style);
        }
    }
}
