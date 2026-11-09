using FishNet.Object;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// РУЧНОЙ ВИБРАТОР — КОРОБКА. Без кнопки: щуп работает по ЛКМ сам.
    ///
    ///   • от гнезда коробки к щупу тянется провисающий кабель;
    ///   • ушёл со щупом дальше Cable Length — коробку ТАЩИТ за тобой по полу
    ///     (как пылесос за шлангом); чем дальше, тем сильнее тянет;
    ///   • коробку можно и просто взять в руки (F) и перенести.
    ///
    /// Тянет сервер (физика коробки на нём). Кабель рисует каждый игрок сам.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody (масса 3–8), Collider.
    /// Socket — гнездо кабеля. Probe — щуп из сцены.
    /// </summary>
    public class VibratorStation : PickupItem
    {
        [Header("Вибратор")]
        [SerializeField] private VibratorProbeItem _probe;
        [Tooltip("Откуда выходит кабель")]
        [SerializeField] private Transform _socket;
        [Tooltip("Длина кабеля, м. Дальше — коробка тащится за щупом.")]
        [Min(0.5f)][SerializeField] private float _cableLength = 3f;

        [Header("Тяга за кабель")]
        [Tooltip("Жёсткость: насколько сильно тянет на каждый метр сверх длины")]
        [SerializeField] private float _pull = 40f;
        [Tooltip("Гашение раскачки")]
        [SerializeField] private float _damping = 4f;
        [Tooltip("Если щуп унесли дальше этого × длина — коробка подтягивается рывком (не отстаёт навсегда)")]
        [SerializeField] private float _snapFactor = 1.8f;

        [Header("Кабель (вид)")]
        [SerializeField] private float _cableWidth = 0.02f;
        [SerializeField] private Color _cableColor = new Color(0.12f, 0.12f, 0.14f);
        [Tooltip("Провисание кабеля, доля длины")]
        [Range(0f, 0.5f)][SerializeField] private float _sag = 0.2f;

        private LineRenderer _line;
        private Rigidbody _body;
        private const int CablePoints = 16;

        public Vector3 SocketPosition => _socket != null ? _socket.position : transform.position;

        protected override void Awake()
        {
            base.Awake();
            _body = GetComponent<Rigidbody>();
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = CablePoints;
            _line.useWorldSpace = true;
            _line.widthMultiplier = _cableWidth;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Cable (auto)" };
        }

        private void FixedUpdate()
        {
            if (!base.IsServerInitialized || _probe == null || _body == null) return;
            if (_body.isKinematic) return;                          // в руках — не тянем

            Vector3 a = SocketPosition, b = _probe.CablePoint;
            Vector3 d = b - a;
            float dist = d.magnitude;
            if (dist <= _cableLength || dist < 1e-4f) return;

            // Сервер берёт коробку себе, иначе тягу перебил бы прежний владелец.
            if (base.NetworkObject.Owner.IsValid) base.NetworkObject.RemoveOwnership();

            Vector3 dir = d / dist;
            if (dist > _cableLength * _snapFactor)
            {
                // Совсем отстала — подтянуть до длины кабеля.
                _body.position += dir * (dist - _cableLength);
                _body.linearVelocity = Vector3.zero;
                return;
            }
            Vector3 flat = new Vector3(dir.x, Mathf.Max(0f, dir.y) * 0.3f, dir.z);
            _body.AddForce(flat * (dist - _cableLength) * _pull - _body.linearVelocity * _damping,
                           ForceMode.Acceleration);
        }

        private void LateUpdate()
        {
            if (_probe == null || _line == null) { if (_line != null) _line.enabled = false; return; }
            _line.enabled = true;
            Vector3 a = SocketPosition, b = _probe.CablePoint;
            float dist = Vector3.Distance(a, b);
            float slack = Mathf.Max(0f, _cableLength - dist) * _sag + 0.03f;
            for (int i = 0; i < CablePoints; i++)
            {
                float t = i / (float)(CablePoints - 1);
                _line.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * slack));
            }
            _line.startColor = _cableColor; _line.endColor = _cableColor;
        }
    }
}
