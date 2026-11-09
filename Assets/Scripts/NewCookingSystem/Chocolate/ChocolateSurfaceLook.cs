using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ВИД ШОКОЛАДА В ТАРЕ (ковш, ведро): когда внутри почти только шоколад, поверхность жидкости
    /// рисуется шоколадными материалами вместо «масляного» глянца:
    ///   • жидкая паста — Paste Material (тёплый молочно-коричневый, мелкие пузырьки, мягкий блик),
    ///     гладкие наплывы (Paste Relief) — течёт и закручивается под венчиком;
    ///   • застывший шоколад — Solid Material (тёмный, матовый, пористый), бугры крупнее (Solid Relief),
    ///     под венчиком не течёт — летят крошки.
    /// Вешать на тару рядом с Cookware (или на её LiquidAnchor). Ставит меню CatCooks → Шоколад → 8.
    /// Только вид, у каждого игрока свой; по сети ничего не шлёт.
    /// </summary>
    public sealed class ChocolateSurfaceLook : MonoBehaviour
    {
        [SerializeField] private Material _pasteMaterial;
        [SerializeField] private Material _solidMaterial;
        [Tooltip("Цвет жидкой пасты в таре (перебивает цвет из Products у LiquidAnchor)")]
        [SerializeField] private Color _pasteColor = new Color(0.47f, 0.27f, 0.14f, 1f);
        [Tooltip("Цвет застывшего шоколада в таре")]
        [SerializeField] private Color _solidColor = new Color(0.27f, 0.14f, 0.075f, 1f);
        [Tooltip("Насколько бугристая жидкая паста (1 — как масло)")]
        [Range(0f, 1.5f)][SerializeField] private float _pasteRelief = 0.4f;
        [Tooltip("Насколько бугристый застывший шоколад")]
        [Range(0f, 1.5f)][SerializeField] private float _solidRelief = 0.65f;
        [Tooltip("С какой доли шоколада в таре включается шоколадный вид")]
        [Range(0.3f, 1f)][SerializeField] private float _share = 0.75f;
        [SerializeField] private bool _log = true;

        private Cookware _cw;
        private CookwareLiquidVisual _lv;
        private int _shown = -1;            // -1 нет, 0 паста, 1 застывший

        private void Awake()
        {
            _cw = GetComponentInParent<Cookware>();
            if (_cw == null) _cw = GetComponentInChildren<Cookware>();
            _lv = GetComponentInChildren<CookwareLiquidVisual>(true);
            if (_lv == null && _cw != null) _lv = _cw.GetComponentInChildren<CookwareLiquidVisual>(true);
            if (_lv == null) Debug.LogWarning($"[Шоколад] «{name}»: нет Cookware Liquid Visual (LiquidAnchor) — вид шоколада не включится", this);
        }

        private void Update()
        {
            if (_cw == null || _lv == null) return;
            int total = _cw.TotalCount;
            ChocolateWork.Count(_cw, out _, out int pn, out _, out int sn);
            int want = -1;
            if (total > 0 && pn + sn >= total * _share) want = sn > pn ? 1 : 0;
            if (want == _shown) return;
            _shown = want;
            if (want < 0)
            {
                _lv.SurfaceOverride = null;
                _lv.PasteReliefK = 1f;
                _lv.PasteTint = Color.clear;
            }
            else
            {
                _lv.SurfaceOverride = want == 1 ? _solidMaterial : _pasteMaterial;
                _lv.PasteReliefK = want == 1 ? _solidRelief : _pasteRelief;
                _lv.PasteTint = want == 1 ? _solidColor : _pasteColor;
                if (want == 0 && _pasteMaterial == null && _log) Debug.LogWarning($"[Шоколад] «{_cw.name}»: не задан Paste Material — меню Шоколад → 8", this);
            }
            if (_log && want >= 0)
                Debug.Log($"[Шоколад] «{_cw.name}»: вид — {(want == 1 ? "застывший (матовый, пористый)" : "жидкая паста (тёплая, с пузырьками)")}", this);
        }

        private void OnDisable()
        {
            if (_lv != null) { _lv.SurfaceOverride = null; _lv.PasteReliefK = 1f; _lv.PasteTint = Color.clear; }
            _shown = -1;
        }

#if UNITY_EDITOR
        public void EditorSetup(Material paste, Material solid)
        {
            _pasteMaterial = paste; _solidMaterial = solid;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
