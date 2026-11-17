using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v23.37: БИСКВИТ ДРОЖИТ КАК ЖЕЛЕ — только вид, у каждого игрока свой (по движению объекта, без сети).
    ///
    /// Несут опалубку, двигают пласт, ставят на стол — верх мягко отстаёт и раскачивается, низ стоит:
    /// сдвиг вершины ∝ (высота над низом)², плюс лёгкое «приседание» при толчке вверх-вниз. Затухает за ~1 с.
    /// Меш должен быть свой (не общий): владелец после перестройки меша зовёт <see cref="Rebase"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpongeJiggle : MonoBehaviour
    {
        [Tooltip("Насколько сильно качается от толчка (резко сдвинули на 1 м/с — наклон ≈ 2,5 этого значения)")]
        [Range(0f, 0.2f)] public float Strength = 0.035f;
        [Tooltip("Частота качания, Гц")]
        [Range(0.5f, 8f)] public float Frequency = 2.6f;
        [Tooltip("Затухание (0 — вечно, 1 — без качания)")]
        [Range(0.02f, 1f)] public float Damping = 0.18f;
        [Tooltip("Предел наклона верха (доля высоты)")]
        [Range(0.01f, 0.3f)] public float MaxLean = 0.1f;

        private MeshFilter _mf;
        private Mesh _mesh;
        private Vector3[] _base, _work;
        private float _minY, _height;
        private Vector3 _prevPos, _prevVel, _s, _sv;
        private bool _hasPrev, _deformed;

        /// <summary>Меш перестроен — запомнить новые исходные вершины.</summary>
        public void Rebase()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            _mesh = _mf != null ? _mf.sharedMesh : null;
            _base = null; _deformed = false;
            if (_mesh == null || !_mesh.isReadable) return;
            _base = _mesh.vertices;
            _work = (Vector3[])_base.Clone();
            _minY = float.MaxValue; float maxY = float.MinValue;
            for (int i = 0; i < _base.Length; i++) { if (_base[i].y < _minY) _minY = _base[i].y; if (_base[i].y > maxY) maxY = _base[i].y; }
            _height = Mathf.Max(1e-4f, maxY - _minY);
        }

        private void OnEnable() { _hasPrev = false; Rebase(); }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 1e-5f) return;
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mf == null) return;
            if (_mf.sharedMesh != _mesh || _base == null || (_mesh != null && _mesh.vertexCount != _base.Length)) Rebase();
            if (_base == null) return;

            Vector3 pos = transform.position;
            Vector3 vel = _hasPrev ? (pos - _prevPos) / dt : Vector3.zero;
            Vector3 dv = _hasPrev ? Vector3.ClampMagnitude(vel - _prevVel, 3f) : Vector3.zero;
            _prevPos = pos; _prevVel = vel; _hasPrev = true;

            // пружина в осях объекта (доля высоты): толчок — верх отстаёт и раскачивается
            _sv -= transform.InverseTransformDirection(dv) * (Strength * 12f);
            float w = Frequency * 6.2832f;
            Vector3 force = -_s * (w * w) - _sv * (2f * Damping * w);
            _sv += force * dt;
            _s += _sv * dt;
            _s = Vector3.ClampMagnitude(_s, MaxLean);

            if (_s.sqrMagnitude < 1e-7f && _sv.sqrMagnitude < 1e-6f)
            {
                if (_deformed) { _mesh.vertices = _base; _deformed = false; }
                return;
            }
            float h = _height;
            for (int i = 0; i < _base.Length; i++)
            {
                Vector3 p = _base[i];
                float t = (p.y - _minY) / h;
                float t2 = t * t;
                p.x += _s.x * h * t2;
                p.z += _s.z * h * t2;
                p.y += _s.y * h * t2 * 0.5f;
                _work[i] = p;
            }
            _mesh.vertices = _work;
            _deformed = true;
        }

        private void OnDisable()
        {
            if (_deformed && _mesh != null && _base != null) { _mesh.vertices = _base; _deformed = false; }
        }
    }
}
