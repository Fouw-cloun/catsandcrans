using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ПОДДОН — лопается от кувалды на доски.
    ///
    ///   • каждый удар — треск, щепки, поддон вздрагивает;
    ///   • последний удар (Hits To Break) — поддон разлетается: щепки, пыль,
    ///     на его месте — Planks Out досок с разлётом. Их собираешь и несёшь
    ///     в кострище.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, коллайдер (обычный). Rigidbody — по желанию.
    /// Plank Prefab — префаб доски: NetworkObject, NetworkTransform (Client
    /// Authoritative), Rigidbody, коллайдер, PickupItem, BonfireFuel. В Spawnable Prefabs.
    /// </summary>
    public sealed class BreakablePallet : NetworkBehaviour, IHammerTarget
    {
        [SerializeField] private NetworkObject _plankPrefab;
        [Range(1, 10)][SerializeField] private int _hitsToBreak = 3;
        [Range(1, 12)][SerializeField] private int _planksOut = 5;
        [Tooltip("Разлёт досок при поломке, м/с")]
        [SerializeField] private float _burstSpeed = 3f;
        [SerializeField] private Color _woodColor = new Color(0.72f, 0.55f, 0.34f);
        [SerializeField] private Color _woodDark = new Color(0.45f, 0.32f, 0.2f);
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _hits = new SyncVar<byte>(0);
        private float _shake;
        private Vector3 _basePos;
        private bool _baseCaptured;

        public void ServerHammerHit(Vector3 point, Vector3 dir)
        {
            if (!base.IsServerInitialized) return;
            _hits.Value = (byte)Mathf.Min(255, _hits.Value + 1);
            if (_hits.Value < _hitsToBreak)
            {
                RpcHit(point);
                if (_log) Debug.Log($"[Поддон] удар {_hits.Value}/{_hitsToBreak}", this);
                return;
            }
            Break(point);
        }

        private void Break(Vector3 point)
        {
            Bounds b = BoundsOf();
            RpcBreak(b.center, Mathf.Max(b.size.x, b.size.z));
            if (_plankPrefab != null)
            {
                for (int i = 0; i < _planksOut; i++)
                {
                    Vector3 at = b.center + new Vector3(Random.Range(-b.extents.x, b.extents.x) * 0.7f,
                                                        b.extents.y * 0.5f + i * 0.05f,
                                                        Random.Range(-b.extents.z, b.extents.z) * 0.7f);
                    NetworkObject nob = Instantiate(_plankPrefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    base.Spawn(nob.gameObject);
                    Rigidbody rb = nob.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        Vector3 v = (at - point).normalized + Vector3.up * 0.8f;
                        rb.linearVelocity = v.normalized * _burstSpeed * Random.Range(0.6f, 1.1f);
                        rb.angularVelocity = Random.insideUnitSphere * 6f;
                    }
                }
            }
            if (_log) Debug.Log($"[Поддон] сломан — {_planksOut} досок", this);
            base.Despawn(base.NetworkObject);
        }

        private Bounds BoundsOf()
        {
            Renderer[] rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(transform.position, Vector3.one);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        [ObserversRpc]
        private void RpcHit(Vector3 point)
        {
            _shake = 1f;
            CCRuntimeParticles.Burst(point, Vector3.up, _woodColor, _woodDark, 22, CCRuntimeParticles.Look.Chips.Scaled(3f));
            CCRuntimeParticles.Burst(point, Vector3.up, _woodColor, Color.white, 10, CCRuntimeParticles.Look.Dust.Scaled(3f));
        }

        [ObserversRpc]
        private void RpcBreak(Vector3 centre, float size)
        {
            FoodShatterFx.Play(centre, size * 0.5f, _woodColor, _woodDark, 26, 0, gameObject.layer);
            CCRuntimeParticles.Burst(centre, Vector3.up, _woodColor, Color.white, 40, CCRuntimeParticles.Look.Dust.Scaled(5f));
        }

        private void Update()
        {
            // Вздрагивает от удара (визуал, у всех).
            if (_shake <= 0f) return;
            if (!_baseCaptured) { _basePos = transform.localPosition; _baseCaptured = true; }
            _shake = Mathf.Max(0f, _shake - Time.deltaTime * 6f);
            transform.localPosition = _basePos + Random.insideUnitSphere * 0.02f * _shake;
            if (_shake <= 0f) transform.localPosition = _basePos;
        }
    }
}
