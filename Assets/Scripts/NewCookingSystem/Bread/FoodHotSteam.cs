using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Горячий — идёт пар (свежий хлеб из формы). Только картинка у каждого игрока, по сети не ходит:
    /// пар идёт Seconds секунд после появления и постепенно слабеет.
    /// Вешается на префаб хлеба.
    /// </summary>
    public sealed class FoodHotSteam : MonoBehaviour
    {
        [SerializeField] private float _seconds = 25f;
        [Tooltip("Облачков в секунду в начале")]
        [SerializeField] private float _rate = 4f;
        [SerializeField] private Color _color = new Color(1f, 1f, 1f, 0.5f);
        [SerializeField] private float _size = 1.3f;

        private float _t, _acc;
        private Renderer _r;

        private void Awake() => _r = GetComponentInChildren<Renderer>();

        private void Update()
        {
            _t += Time.deltaTime;
            if (_t > _seconds) { enabled = false; return; }
            float k = 1f - _t / _seconds;
            _acc += Time.deltaTime * _rate * k;
            if (_acc < 1f) return;
            _acc -= 1f;
            Vector3 top = _r != null ? _r.bounds.center + Vector3.up * _r.bounds.extents.y : transform.position;
            Vector3 jitter = _r != null ? Vector3.Scale(Random.insideUnitSphere, _r.bounds.extents) * 0.6f : Vector3.zero;
            jitter.y = 0f;
            Color c = _color; c.a *= 0.3f + 0.7f * k;
            CCRuntimeParticles.Burst(top + jitter, Vector3.up, c, c * 0.9f, 2, CCRuntimeParticles.Look.Dust.Scaled(_size));
        }
    }
}
