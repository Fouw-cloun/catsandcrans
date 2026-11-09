using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ОСКОЛОК — кусочек разбитого кувалдой продукта. Только визуал.
    /// Летит, быстро вертится, от пола отскакивает один раз и за долю секунды
    /// сжимается в ничто. Создаёт FoodShatterFx.
    /// </summary>
    public sealed class FoodShardFx : MonoBehaviour
    {
        private Vector3 _vel, _spin, _scale;
        private float _life, _age;
        private bool _bounced;

        public void Init(Vector3 velocity, Vector3 spin, Vector3 scale, float life)
        {
            _vel = velocity; _spin = spin; _scale = scale; _life = life;
            transform.localScale = scale;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age >= _life) { Destroy(gameObject); return; }

            _vel += Physics.gravity * dt;
            Vector3 step = _vel * dt;
            float len = step.magnitude;
            if (len > 1e-5f && Physics.Raycast(transform.position, step / len, out RaycastHit hit, len,
                                                ~0, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point + hit.normal * 0.005f;
                if (!_bounced)
                {
                    _vel = Vector3.Reflect(_vel, hit.normal) * 0.35f;
                    _spin *= 0.5f;
                    _bounced = true;
                }
                else _vel = Vector3.zero;
            }
            else transform.position += step;

            transform.Rotate(_spin * dt, Space.Self);
            // Последняя треть жизни — сжимается.
            float k = Mathf.Clamp01((_life - _age) / (_life * 0.35f));
            transform.localScale = _scale * k;
        }
    }
}
