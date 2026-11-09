using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ПЛИТКА ШОКОЛАДА — вид. Сама плитка — обычный FoodPiece «Шоколад» (порций — сколько было в форме),
    /// этот компонент только подгоняет толщину под число порций: полная форма — полная плитка,
    /// половина — плитка вдвое тоньше (меш «Mesh» и Box Collider на корне).
    ///
    /// Кинь плитку в ковш — она уйдёт туда порциями «Шоколад». Погрей ковш горелкой / на огне —
    /// растает в «Шоколадную пасту» (FoodHardening), её снова можно мастерком в форму.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class ChocolateBar : MonoBehaviour
    {
        [Tooltip("Сколько порций — плитка полной толщины (как Full Portions у формы)")]
        [Min(1)] public int FullPortions = 16;
        [Tooltip("Меш плитки. Пусто — дочерний «Mesh»")]
        [SerializeField] private Transform _mesh;
        [SerializeField] private bool _log = true;

        private FoodPiece _fp;
        private BoxCollider _box;
        private Vector3 _meshScale, _boxSize, _boxCenter;
        private int _shown = -1;

        private void Awake()
        {
            _fp = GetComponent<FoodPiece>();
            _box = GetComponent<BoxCollider>();
            if (_mesh == null) _mesh = transform.Find("Mesh");
            if (_mesh != null) _meshScale = _mesh.localScale;
            if (_box != null) { _boxSize = _box.size; _boxCenter = _box.center; }
        }

        private void Update()
        {
            if (_fp == null) return;
            int n = _fp.Count;
            if (n <= 0 || n == _shown) return;
            bool first = _shown < 0;
            _shown = n;
            float k = Mathf.Clamp(n / (float)Mathf.Max(1, FullPortions), 0.25f, 1.25f);
            if (_mesh != null) _mesh.localScale = new Vector3(_meshScale.x, _meshScale.y * k, _meshScale.z);
            if (_box != null)
            {
                // низ коллайдера остаётся на месте, растёт вверх
                float bottom = _boxCenter.y - _boxSize.y * 0.5f;
                _box.size = new Vector3(_boxSize.x, _boxSize.y * k, _boxSize.z);
                _box.center = new Vector3(_boxCenter.x, bottom + _boxSize.y * k * 0.5f, _boxCenter.z);
            }
            if (_log && first) Debug.Log($"[Плитка] «{name}»: шоколад ×{n} порций (толщина {k * 100f:0}% от полной)", this);
        }
    }
}
