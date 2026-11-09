using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ВИД ПРОДУКТА ПО СОСТОЯНИЮ. Замена старого FoodVisual.
    ///
    /// Чисто локальный: читает синхронизированные State, Cook и Progress01 у
    /// FoodPiece и перекрашивает модель. По сети ничего не шлёт.
    ///
    ///   • в ProductDef.Looks есть это состояние → ставит его материал/меш;
    ///   • испорченное без своего материала → тянет цвет к BurntColor;
    ///   • идёт обработка → лёгкое осветление по прогрессу (как в старом FoodVisual);
    ///     в пламени горелки (FoodTorchHeat) — наоборот, темнеет по прогрессу.
    ///   • НАГРЕВ БЕЗ СВОЕЙ МОДЕЛИ (Heat Tint ✔): опалено, запечено, жарено, варено, тушено, копчено,
    ///     пере-/недоготовлено, сгорело — если у этого состояния нет СВОЕГО материала, цвет модели сам
    ///     подкрашивается (опалено — тёмно-коричневое, запечено — темнее и гуще, варено — чуть бледнее…).
    ///     Материал, общий с «сырым» состоянием (чищеная свёкла → тот же материал у «Запечённой»),
    ///     своим не считается — подкраска поверх него.
    ///   • Грязное (Dirty Tint > 0): без своего материала — темнее и землистее. По умолчанию выкл.
    ///
    /// Материалы назначаются как sharedMaterial — без копий на каждый продукт.
    /// Подкраска идёт через MaterialPropertyBlock, тоже без копий.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class FoodStateVisual : MonoBehaviour
    {
        [Tooltip("Что перекрашивать. Пусто — все MeshRenderer в иерархии, включая выключенные.")]
        [SerializeField] private Renderer[] _renderers;

        [Tooltip("Чей меш менять, если у состояния свой меш. Пусто — меш не трогаем.")]
        [SerializeField] private MeshFilter _meshTarget;

        [Header("Свой вид этой модели")]
        [Tooltip("Виды состояний ИМЕННО ЭТОЙ модели — важнее, чем у продукта. Нужны, когда у одного продукта " +
                 "разные модели: жёлтый и красный лук — свои материалы «грязный/мытый/гнилой», а чищеный — " +
                 "общий меш. Пустой материал/меш — берётся у продукта (или не меняется).")]
        [SerializeField] private List<StateLook> _localLooks = new List<StateLook>();

        [Header("Подкраска")]
        [Tooltip("Насколько испорченное тянется к BurntColor, если своего материала нет")]
        [Range(0f, 1f)][SerializeField] private float _spoiledTint = 0.75f;

        [Tooltip("Осветление при обработке на пике прогресса. 0 — выключено.")]
        [Range(0f, 0.6f)][SerializeField] private float _progressGlow = 0.25f;

        [Tooltip("Кривая осветления: 1 — равномерно, 3 — почти только под конец")]
        [Range(1f, 4f)][SerializeField] private float _glowCurve = 2f;

        [Header("Нагрев и грязь без своих материалов")]
        [Tooltip("Состояния нагрева без своего материала подкрашивают модель (опалено, запечено, жарено…)")]
        [SerializeField] private bool _heatTint = true;
        [Tooltip("Сила подкраски нагрева: 1 — как задумано, 0.5 — вдвое слабее")]
        [Range(0f, 1.5f)][SerializeField] private float _heatTintStrength = 1f;
        [Tooltip("Грязное без своего материала — темнее (0 — выключено). Свёкла, капуста — 0.35")]
        [Range(0f, 1f)][SerializeField] private float _dirtyTint = 0f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private FoodPiece _piece;
        private MaterialPropertyBlock _mpb;
        private Color _base = Color.white;
        private float _shownGlow = -1f;
        private bool _spoiled;

        private void Awake()
        {
            _piece = GetComponent<FoodPiece>();
            _mpb = new MaterialPropertyBlock();
            if (_renderers == null || _renderers.Length == 0)
                _renderers = GetComponentsInChildren<MeshRenderer>(true);
        }

        private void OnEnable() => _piece.StateChanged += OnStateChanged;
        private void OnDisable() => _piece.StateChanged -= OnStateChanged;

        private void Start()
        {
            // Поздний вход: продукт уже мог быть почищен — догоняем без событий.
            ApplyState(_piece.State);
        }

        private void OnStateChanged(FoodState prev, FoodState next) => ApplyState(next);

        private void ApplyState(FoodState s)
        {
            ProductDef def = _piece.Def;
            if (def == null) return;

            _spoiled = FoodStateInfo.IsSpoiled(s);
            bool hasLook = TryLocalLook(s, out StateLook look);
            if (hasLook && (look.Material == null || look.Mesh == null) && def.TryGetLook(s, out StateLook pl))
            {
                // своё — главное, недостающее добираем у продукта
                if (look.Material == null) { look.Material = pl.Material; look.Slot = pl.Slot; }
                if (look.Mesh == null) look.Mesh = pl.Mesh;
            }
            if (!hasLook) hasLook = def.TryGetLook(s, out look);

            if (hasLook && look.Material != null)
                foreach (Renderer r in _renderers)
                    if (r != null) SetSlot(r, look.Material, look.Slot);

            if (hasLook && look.Mesh != null && _meshTarget != null) _meshTarget.sharedMesh = look.Mesh;

            // Основа для подкраски: цвет материала, если он есть, иначе белый
            // (белый в PropertyBlock = «как в материале»).
            _base = Color.white;
            Renderer first = _renderers.Length > 0 ? _renderers[0] : null;
            if (first != null && first.sharedMaterial != null)
            {
                Material m = first.sharedMaterial;
                if (m.HasProperty(BaseColorId)) _base = m.GetColor(BaseColorId);
                else if (m.HasProperty(ColorId)) _base = m.GetColor(ColorId);
            }

            bool ownMat = hasLook && look.Material != null && (!IsHeat(s) || !SharedWithRaw(def, look.Material));   // общий с сырым — только для нагрева
            if (_spoiled && !(hasLook && look.Material != null))
                _base = Color.Lerp(_base, def.BurntColor, _spoiledTint);
            else if (!ownMat)
            {
                if (_heatTint) _base = HeatTint(_base, s, def, _heatTintStrength);
                if (s == FoodState.Dirty && _dirtyTint > 0f) _base = Color.Lerp(_base, new Color(0.33f, 0.24f, 0.15f, _base.a), _dirtyTint);
            }

            _shownGlow = -1f;       // заставить Update перекрасить
        }

        /// <summary>Нагрев: какой цвет, если своего материала у состояния нет.</summary>
        public static Color HeatTint(Color c, FoodState s, ProductDef def, float k)
        {
            float a = c.a;
            Color r = c;
            switch (s)
            {
                case FoodState.Torched:     r = Color.Lerp(c * 0.8f, new Color(0.26f, 0.15f, 0.08f), 0.45f * k); break;
                case FoodState.Fried:
                case FoodState.Spitted:     r = Color.Lerp(c, new Color(0.62f, 0.38f, 0.16f), 0.38f * k); break;
                case FoodState.Baked:       r = Color.Lerp(c, Color.Lerp(c * 0.55f, new Color(0.42f, 0.24f, 0.14f), 0.25f), 0.8f * k); break;
                case FoodState.Boiled:      r = Color.Lerp(c, Color.Lerp(c * 0.72f, new Color(0.58f, 0.46f, 0.32f), 0.35f), 0.5f * k); break;   // сварилось — темнее, пропиталось бульоном
                case FoodState.Stewed:      r = Color.Lerp(c, Gray(c) * 0.75f, 0.3f * k); break;
                case FoodState.Smoked:      r = Color.Lerp(c, new Color(0.52f, 0.30f, 0.14f), 0.42f * k); break;
                case FoodState.Undercooked: r = Color.Lerp(c, new Color(0.62f, 0.45f, 0.30f), 0.12f * k); break;
                case FoodState.Overcooked:  r = Color.Lerp(c, new Color(0.34f, 0.24f, 0.17f), 0.5f * k); break;
                case FoodState.Burnt:       r = Color.Lerp(c, def != null ? def.BurntColor : new Color(0.1f, 0.07f, 0.05f), Mathf.Clamp01(0.85f * k)); break;
            }
            r.a = a;
            return r;
        }

        private static Color Gray(Color c) { float g = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f; return new Color(g, g, g, c.a); }

        private static bool IsHeat(FoodState s) => (int)s >= 40 && (int)s <= 59;

        /// <summary>Этот материал стоит и у «холодного» состояния (чищеное, мытое…) — значит, он не свой у нагрева.</summary>
        private bool SharedWithRaw(ProductDef def, Material m)
        {
            if (m == null) return false;
            if (_localLooks != null)
                for (int i = 0; i < _localLooks.Count; i++)
                    if (_localLooks[i].Material == m && !IsHeat(_localLooks[i].State)) return true;
            if (def != null && def.Looks != null)
                for (int i = 0; i < def.Looks.Count; i++)
                    if (def.Looks[i].Material == m && !IsHeat(def.Looks[i].State)) return true;
            return false;
        }

        private bool TryLocalLook(FoodState s, out StateLook look)
        {
            if (_localLooks != null)
                for (int i = 0; i < _localLooks.Count; i++)
                    if (_localLooks[i].State == s) { look = _localLooks[i]; return true; }
            look = default;
            return false;
        }

#if UNITY_EDITOR
        public void EditorSetLocalLook(FoodState s, Material m, int slot, Mesh mesh)
        {
            if (_localLooks == null) _localLooks = new List<StateLook>();
            for (int i = 0; i < _localLooks.Count; i++)
                if (_localLooks[i].State == s)
                {
                    StateLook l = _localLooks[i];
                    if (m != null) { l.Material = m; l.Slot = slot; }
                    if (mesh != null) l.Mesh = mesh;
                    _localLooks[i] = l;
                    return;
                }
            _localLooks.Add(new StateLook { State = s, Material = m, Slot = slot, Mesh = mesh });
        }

        public MeshFilter EditorMeshTarget { get => _meshTarget; set => _meshTarget = value; }

        public void EditorClearLocalLooks() { if (_localLooks != null) _localLooks.Clear(); }

        public float EditorDirtyTint { get => _dirtyTint; set => _dirtyTint = value; }
#endif

        /// <summary>Поставить материал в слот. Массив материалов берётся копией —
        /// sharedMaterials всегда отдаёт новый массив, сам ассет не трогается.</summary>
        private static void SetSlot(Renderer r, Material m, int slot)
        {
            Material[] mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) { r.sharedMaterial = m; return; }
            if (slot < 0)
            {
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
            }
            else
            {
                if (slot >= mats.Length) return;       // у этого куска меньше слотов — не трогаем
                if (mats[slot] == m) return;
                mats[slot] = m;
            }
            r.sharedMaterials = mats;
        }

        private bool _shownTorch, _stickyTorch;

        private void Update()
        {
            // в пламени горелки — темнеет по прогрессу (а не светлеет, как при варке/нарезке)
            // пламя ушло, а прогресс опаления остался (водят с перерывами) — продолжаем показывать темнее
            if (FoodTorchHeat.IsHeating(_piece)) _stickyTorch = true;
            else if (_piece.Progress01 <= 0.001f) _stickyTorch = false;
            bool torch = _stickyTorch;
            float glow = torch ? Mathf.Pow(_piece.Progress01, 1.3f) * 0.5f
                       : _progressGlow > 0f ? Mathf.Pow(_piece.Progress01, _glowCurve) * _progressGlow : 0f;
            if (Mathf.Abs(glow - _shownGlow) < 0.004f && torch == _shownTorch) return;
            _shownGlow = glow;
            _shownTorch = torch;

            Color c = Color.Lerp(_base, torch ? new Color(0.24f, 0.14f, 0.07f) : Color.white, glow);
            c.a = _base.a;
            foreach (Renderer r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                _mpb.SetColor(ColorId, c);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
