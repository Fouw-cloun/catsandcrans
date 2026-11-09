using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КАПЛЯ-ЭФФЕКТ: комочек пюре или брусочек, сорвавшийся с лопаты.
    /// Только визуал, у каждого игрока свой, по сети не ходит.
    ///
    /// Падает с гравитацией и кувыркается. Коснулся чего-нибудь — шлёпается
    /// (сплющивается и растекается) и тает. Через Life секунд исчезает в любом случае.
    ///
    /// Создаёт FoodHeapRenderer.SpawnDrops — вешать руками не нужно.
    /// </summary>
    public sealed class FoodDropFx : MonoBehaviour
    {
        private Vector3 _vel;
        private Vector3 _spin;
        private Vector3 _baseScale;
        private float _life;
        private float _splat = -1f;              // -1 — ещё летит, 0..1 — шлёпается
        private bool _viscous;
        private LayerMask _mask;
        private MaterialPropertyBlock _mpb;
        private MeshRenderer _mr;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static void Spawn(Vector3 at, Vector3 velocity, Mesh mesh, Material mat, Color color,
                                 Vector3 scale, bool viscous, int layer)
        {
            if (mesh == null || mat == null) return;
            GameObject go = new GameObject("FoodDrop (fx)") { layer = layer };
            go.transform.position = at;
            go.transform.rotation = Random.rotation;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            FoodDropFx fx = go.AddComponent<FoodDropFx>();
            fx._vel = velocity;
            fx._spin = Random.insideUnitSphere * (viscous ? 90f : 540f);
            fx._baseScale = scale;
            fx._life = 2.5f;
            fx._viscous = viscous;
            fx._mask = ~0;
            fx._mr = mr;
            fx._mpb = new MaterialPropertyBlock();
            fx._mpb.SetColor(BaseColorId, color);
            mr.SetPropertyBlock(fx._mpb);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f) { Destroy(gameObject); return; }

            if (_splat < 0f)
            {
                // Полёт: гравитация, кувыркание, проверка удара.
                _vel += Physics.gravity * dt;
                Vector3 step = _vel * dt;
                float len = step.magnitude;
                if (len > 1e-5f && Physics.Raycast(transform.position, step / len, out RaycastHit hit,
                                                    len + _baseScale.y * 0.5f, _mask, QueryTriggerInteraction.Ignore))
                {
                    transform.position = hit.point + hit.normal * (_baseScale.y * 0.15f);
                    // Шлёп: ровно по поверхности, куда упала.
                    transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) *
                                         Quaternion.Euler(0f, Random.value * 360f, 0f);
                    _splat = 0f;
                    return;
                }
                transform.position += step;
                transform.Rotate(_spin * dt, Space.Self);
                return;
            }

            // Шлёпается: пюре растекается лепёшкой, брусок просто оседает и тает.
            _splat = Mathf.Min(1f, _splat + dt * (_viscous ? 2.2f : 3.5f));
            float k = _splat;
            Vector3 s = _viscous
                ? new Vector3(_baseScale.x * (1f + 0.8f * k), _baseScale.y * (1f - 0.75f * k), _baseScale.z * (1f + 0.8f * k))
                : _baseScale * (1f - 0.9f * k);
            transform.localScale = s;
            if (_splat >= 1f) Destroy(gameObject);
        }
    }
}
