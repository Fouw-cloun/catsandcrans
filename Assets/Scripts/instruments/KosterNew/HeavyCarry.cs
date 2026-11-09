using FishNet.Object;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ТЯЖЁЛЫЙ ПРЕДМЕТ — поддон, мешок, бочка. Вешается рядом с Pickup Item.
    ///
    ///   • в руках предмет раскачивается, и чем дольше несёшь — тем сильнее;
    ///   • у несущего покачивается камера (устал);
    ///   • через Max Carry Seconds руки не выдерживают — предмет выскальзывает.
    /// Замедление — в Pickup Item → Carry Speed (для поддона 0.35–0.45).
    ///
    /// Раскачивание и камера — у каждого сам; выскальзывание решает сервер.
    /// </summary>
    [DefaultExecutionOrder(10000)]   // после того как рука поставила предмет
    public sealed class HeavyCarry : NetworkBehaviour
    {
        [Tooltip("Сколько секунд можно нести")]
        [Min(0.5f)][SerializeField] private float _maxCarrySeconds = 6f;
        [Tooltip("Раскачивание предмета в начале / в конце, градусы")]
        [SerializeField] private Vector2 _sway = new Vector2(4f, 16f);
        [Tooltip("Покачивание камеры несущего в конце, градусы (0 — не трогать камеру)")]
        [SerializeField] private float _cameraSway = 3f;
        [SerializeField] private float _swaySpeed = 2.2f;
        [SerializeField] private bool _log = true;

        private Rigidbody _rb;
        private float _held;
        private float _scan;
        private PlayerHand _holder;

        private void Awake() => _rb = GetComponent<Rigidbody>();

        private bool Held => _rb != null && _rb.isKinematic;

        private void Update()
        {
            _held = Held ? _held + Time.deltaTime : 0f;
            if (!base.IsServerInitialized || !Held) { _holder = null; return; }
            if (_held < _maxCarrySeconds) return;

            if (_holder == null && Time.time >= _scan)
            {
                _scan = Time.time + 0.3f;
                foreach (PlayerHand h in FindObjectsByType<PlayerHand>(FindObjectsSortMode.None))
                    if (h != null && h.HeldItem != null && h.HeldItem.gameObject == gameObject) { _holder = h; break; }
            }
            if (_holder == null) return;
            if (_log) Debug.Log($"[Тяжело] «{name}» выскользнул из рук", this);
            _holder.ServerDrop(_holder.transform.forward, 0.5f);
            _holder = null;
            _held = 0f;
        }

        private void LateUpdate()
        {
            if (!Held || _held <= 0f) return;
            float k = Mathf.Clamp01(_held / _maxCarrySeconds);
            float amp = Mathf.Lerp(_sway.x, _sway.y, k * k);
            float t = Time.time * _swaySpeed;
            transform.rotation = Quaternion.Euler(Mathf.Sin(t * 1.3f) * amp * 0.5f, 0f, Mathf.Sin(t) * amp) * transform.rotation;

            if (base.IsOwner && _cameraSway > 0f)
            {
                Camera cam = Camera.main;
                if (cam != null) cam.transform.localRotation *= Quaternion.Euler(0f, 0f, Mathf.Sin(t) * _cameraSway * k * k);
            }
        }
    }
}
