using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЛЮК ВЕНТИЛЯЦИИ НА КРЫШКЕ ДУХОВКИ. F по люку (Oven Hatch Part) — открыть / закрыть.
    /// Открыт — пар из духовки выходит через него, давление не копится.
    ///
    /// Вешается на КОРЕНЬ крышки (рядом с PotLidItem и NetworkObject). Hatch — пивот-петля,
    /// сам люк — его ребёнок; Vent — точка в дырке (откуда пар). Всё создаёт
    /// CatCooks → Хлеб → Авто: духовка. Проверить открывание в редакторе — «⋮» → «Открыть/закрыть (проверка)».
    /// </summary>
    public sealed class OvenVentHatch : NetworkBehaviour
    {
        [Tooltip("Пивот петли люка (люк — его ребёнок)")]
        [SerializeField] private Transform _hatch;
        [Tooltip("Ось петли в осях пивота")]
        [SerializeField] private Vector3 _hingeAxis = Vector3.forward;
        [Tooltip("На сколько градусов открывается (минус — в другую сторону)")]
        [SerializeField] private float _openAngle = 110f;
        [Tooltip("Скорость, град/с")]
        [SerializeField] private float _speed = 300f;
        [Tooltip("Дырка вентиляции — отсюда идёт пар. Пусто — центр люка")]
        [SerializeField] private Transform _vent;
        [SerializeField] private bool _startOpen = false;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<bool> _open = new SyncVar<bool>(false);
        private Quaternion _base;
        private float _angle;
        private bool _baseSet;

        public bool IsOpen => _open.Value;
        public Vector3 VentPoint => _vent != null ? _vent.position : _hatch != null ? _hatch.position : transform.position;

        private void Awake() { CacheBase(); }

        private void CacheBase()
        {
            if (_baseSet || _hatch == null) return;
            _base = _hatch.localRotation;
            _baseSet = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _open.Value = _startOpen;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _angle = _open.Value ? _openAngle : 0f;
            Apply();
        }

        [Server]
        public void ServerToggle()
        {
            _open.Value = !_open.Value;
            if (_log) Debug.Log($"[Люк] «{name}»: {(_open.Value ? "открыли — пар выходит" : "закрыли")}", this);
        }

        private void Update()
        {
            if (_hatch == null) return;
            float target = _open.Value ? _openAngle : 0f;
            if (Mathf.Approximately(_angle, target)) return;
            _angle = Mathf.MoveTowards(_angle, target, _speed * Time.deltaTime);
            Apply();
        }

        private void Apply()
        {
            if (_hatch == null) return;
            CacheBase();
            _hatch.localRotation = _base * Quaternion.AngleAxis(_angle, _hingeAxis.normalized);
        }

#if UNITY_EDITOR
        private bool _previewOpen;
        private Quaternion _previewBase;

        [ContextMenu("Открыть/закрыть (проверка)")]
        private void EditorPreview()
        {
            if (_hatch == null) return;
            if (!_previewOpen) { _previewBase = _hatch.localRotation; _hatch.localRotation = _previewBase * Quaternion.AngleAxis(_openAngle, _hingeAxis.normalized); }
            else _hatch.localRotation = _previewBase;
            _previewOpen = !_previewOpen;
        }
#endif
    }
}
