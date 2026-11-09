using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КУЧКА ВНУТРИ ТАРЫ. Читает снимок содержимого Cookware (он синхронизирован)
    /// и показывает воксельную горку на дне. Никакой сети сам не шлёт.
    ///
    /// ═══ КАК ВЕШАТЬ ═══
    /// Дочерний пустой объект тары → «HeapAnchor»:
    ///   • поставь его на ДНО внутри тары, по центру;
    ///   • синяя ось вверх не нужна — нужна зелёная (Y) вверх;
    ///   • на него: FoodHeapRenderer (Shape = Fill, Footprint = полуразмеры дна,
    ///     Max Height = глубина тары) и этот компонент.
    /// </summary>
    [RequireComponent(typeof(FoodHeapRenderer))]
    public sealed class CookwareHeapVisual : MonoBehaviour
    {
        [Tooltip("Тара. Пусто — найдётся в родителях.")]
        [SerializeField] private Cookware _cookware;
        [Tooltip("Эти продукты горкой НЕ рисуются. Жидкости из Cookware Liquid Visual той же тары " +
                 "пропускаются САМИ — сюда можно ничего не добавлять. Список — для особых случаев.")]
        [SerializeField] private ProductDef[] _ignore;
        [Tooltip("Кусочки лежат в жидкости (суп): горка ниже — осела на дно, жидкость поверх неё. 1 — как без жидкости")]
        [Range(0.2f, 1f)][SerializeField] private float _inLiquidScale = 0.6f;

        private CookwareLiquidVisual _liquid;

        private FoodHeapRenderer _heap;
        private int _seed;

        private void Awake()
        {
            _heap = GetComponent<FoodHeapRenderer>();
            if (_cookware == null) _cookware = GetComponentInParent<Cookware>();
            // Жидкость в той же таре — её продукты горкой не рисуем.
            if (_cookware != null) _liquid = _cookware.GetComponentInChildren<CookwareLiquidVisual>(true);
        }

        private void OnEnable()
        {
            if (_cookware != null) _cookware.ContentsChanged += Refresh;
        }

        private void OnDisable()
        {
            if (_cookware != null) _cookware.ContentsChanged -= Refresh;
        }

        private void Start() => Refresh();

        /// <summary>Куда в таре сыплется (мир). Зовёт измельчитель через RPC:
        /// новые частицы упадут над этой точкой, а не в центр.</summary>
        public void SetPourPoint(Vector3 world)
        {
            if (_heap != null) _heap.SetPourPoint(world);
        }

        /// <summary>Переполнение: кусочки срываются с края через борт (визуал).</summary>
        public void SpillFx(Vector3 rim, Vector3 outward, int drops)
        {
            if (_heap != null) _heap.SpawnDrops(rim, outward + Vector3.up * 0.3f, drops);
        }

        private bool IsIgnored(ushort id)
        {
            if (_liquid != null && _liquid.Handles(id)) return true;
            if (_ignore == null) return false;
            foreach (ProductDef d in _ignore) if (d != null && d.Id == id) return true;
            return false;
        }

        private void Refresh()
        {
            if (_cookware == null) return;
            FoodDatabase db = FoodDatabase.Instance;

            int total = 0, most = 0, liquid = 0;
            ushort mainProduct = 0;
            FoodState mainState = FoodState.None;
            float r = 0f, g = 0f, b = 0f;
            for (int i = 0; i < _cookware.ViewCount; i++)
            {
                BagView v = _cookware.GetView(i);
                if (IsIgnored(v.Product)) { liquid += v.Count; continue; }
                // Жидкость горкой не рисуем никогда (даже если у тары нет LiquidAnchor).
                if (FoodHeapLook.IsLiquid(db != null ? db.Get(v.Product) : null, v.State)) { liquid += v.Count; continue; }
                total += v.Count;
                // чего больше всего — по тому и вид кучки (порошок, кубики, стружка...)
                if (v.Count > most) { most = v.Count; mainProduct = v.Product; mainState = v.State; }
                Color c = FoodColors.Of(db != null ? db.Get(v.Product) : null, v.State);
                r += c.r * v.Count; g += c.g * v.Count; b += c.b * v.Count;
            }

            Color col = total > 0 ? new Color(r / total, g / total, b / total) : Color.gray;
            // Seed от сетевого Id: одинаковая раскладка у всех игроков.
            _seed = _cookware.NetworkObject != null && _cookware.IsSpawned
                ? _cookware.NetworkObject.ObjectId * 7919 + 13
                : 12345;
            // Высота горки — от доли заполнения тары: полная тара = полный купол.
            _heap.SetFillCapacity(_cookware.Capacity);
            _heap.FollowParentRotation(true);        // наклонили ведро — горка наклоняется вместе с ним
            if (total > 0) FoodHeapLook.Apply(_heap, db != null ? db.Get(mainProduct) : null, mainState);
            // В жидкости (суп) — кусочки осели на дно, сверху жидкость их цвета и плавающие кусочки.
            float shown = liquid > 0 && _liquid != null ? total * _inLiquidScale : total;
            _heap.Show(shown, col, _seed);
        }
    }
}
