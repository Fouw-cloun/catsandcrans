using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ОТКИДНАЯ КРЫШКА ДУХОВКИ — как футляр для очков: верхняя половина бочки на петле,
    /// откидывается вбок. Часть самой духовки (один объект — можно таскать краном, ничего не отваливается).
    ///
    ///   F по крышке (Oven Lid Part) — открыть / закрыть. Тест-клавиша L — то же.
    ///   Закрыта и люк закрыт — пар от хлеба копит давление (Cookware Pressure).
    ///   Открыта — печётся медленнее, пар уходит вверх, формы можно ставить/доставать.
    ///   Взрыв — крышку с размаху откидывает настежь.
    ///
    /// ═══ ИЕРАРХИЯ ═══
    ///   Духовка (корень: NetworkObject, Rigidbody, Cookware, Bake Oven, ЭТОТ компонент…)
    ///    ├ Body …            (низ бочки)
    ///    ├ Chamber           (Box Collider Is Trigger — пустота внутри)
    ///    └ LidHinge          (пустышка НА ЛИНИИ ПЕТЛИ; Hinge Axis — ось петли в её осях)
    ///        └ Lid           (меш верхней половины + коллайдер + Oven Lid Part)
    ///            ├ HatchHinge → Hatch (люк: коллайдер + Oven Hatch Part)
    ///            └ Vent      (пустышка в дырке люка)
    /// Проверка без игры: ⋮ → «Открыть/закрыть (проверка)».
    /// </summary>
    public sealed class OvenHingedLid : NetworkBehaviour
    {
        [Header("Петля")]
        [Tooltip("Пустышка на линии петли. Крышка (меш) — её ребёнок")]
        [SerializeField] private Transform _hinge;
        [Tooltip("Ось петли в осях пивота LidHinge: петля вдоль красной оси — (1,0,0), вдоль синей — (0,0,1)")]
        [SerializeField] private Vector3 _hingeAxis = Vector3.forward;
        [Tooltip("На сколько градусов откидывается (минус — в другую сторону)")]
        [SerializeField] private float _openAngle = 110f;
        [Tooltip("Скорость, град/с")]
        [SerializeField] private float _speed = 140f;
        [SerializeField] private bool _startOpen = false;

        [Header("Взрыв")]
        [Tooltip("Взрыв откидывает крышку быстрее во столько раз")]
        [SerializeField] private float _blowSpeedK = 6f;

        [SerializeField] private bool _log = true;

        private readonly SyncVar<bool> _open = new SyncVar<bool>(false);
        private readonly SyncVar<bool> _blown = new SyncVar<bool>(false);
        private Quaternion _base;
        private bool _baseSet;
        private float _angle;

        public bool IsOpen => _open.Value;
        /// <summary>Крышка ещё движется.</summary>
        public bool IsMoving => !Mathf.Approximately(_angle, _open.Value ? _openAngle : 0f);

        private void Awake() => CacheBase();

        private void CacheBase()
        {
            if (_baseSet || _hinge == null) return;
            _base = _hinge.localRotation;
            _baseSet = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _open.Value = _startOpen;
            if (_hinge == null) Debug.LogError($"[Крышка духовки] «{name}»: не задан Hinge (пустышка петли, крышка — её ребёнок)", this);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _angle = _open.Value ? _openAngle : 0f;
            Apply();
        }

        [Server]
        public void ServerToggle() => ServerSetOpen(!_open.Value);

        [Server]
        public void ServerSetOpen(bool open)
        {
            if (_open.Value == open) return;
            _open.Value = open;
            if (open) _blown.Value = false;
            if (_log) Debug.Log($"[Крышка духовки] «{name}»: {(open ? "открыли" : "закрыли")}", this);
        }

        /// <summary>Взрыв давления — крышку с размаху откидывает настежь.</summary>
        [Server]
        public void ServerBlowOpen()
        {
            _open.Value = true;
            _blown.Value = true;
            if (_log) Debug.Log($"[Крышка духовки] «{name}»: ВЗРЫВОМ откинуло крышку", this);
        }

        private void Update()
        {
            if (_hinge == null) return;
            float target = _open.Value ? _openAngle : 0f;
            if (Mathf.Approximately(_angle, target)) return;
            float k = _blown.Value ? _blowSpeedK : 1f;
            // мягко у концов, быстро в середине — как тяжёлая крышка на петле
            float near = Mathf.Clamp01(Mathf.Abs(target - _angle) / 25f);
            _angle = Mathf.MoveTowards(_angle, target, _speed * k * Mathf.Lerp(0.35f, 1f, near) * Time.deltaTime);
            Apply();
        }

        private void Apply()
        {
            if (_hinge == null) return;
            CacheBase();
            _hinge.localRotation = _base * Quaternion.AngleAxis(_angle, _hingeAxis.normalized);
        }

#if UNITY_EDITOR
        private bool _previewOpen;
        private Quaternion _previewBase;

        [ContextMenu("Открыть/закрыть (проверка)")]
        private void EditorPreview()
        {
            if (_hinge == null) { Debug.LogWarning("Не задан Hinge", this); return; }
            if (!_previewOpen) { _previewBase = _hinge.localRotation; _hinge.localRotation = _previewBase * Quaternion.AngleAxis(_openAngle, _hingeAxis.normalized); }
            else _hinge.localRotation = _previewBase;
            _previewOpen = !_previewOpen;
        }
#endif

        private void OnDrawGizmosSelected()
        {
            if (_hinge == null) return;
            Vector3 a = _hinge.TransformDirection(_hingeAxis.normalized);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawLine(_hinge.position - a * 0.6f, _hinge.position + a * 0.6f);
            Gizmos.DrawWireSphere(_hinge.position, 0.05f);
        }
    }
}
