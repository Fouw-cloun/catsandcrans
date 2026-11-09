using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// СКОРЛУПКА МАКАРОН (и собранный макарон) — один предмет, без подмены префаба.
    ///
    ///   • Выпала из противня: купол вверх. Размер и оценка — от того, как попал в кольцо;
    ///     «юбочка» снизу — значит, всё получилось; треснувшая/растёкшаяся/подгоревшая — видно сразу.
    ///   • Шприц с ганашем, смотришь на скорлупку, держишь ЛКМ — скорлупка переворачивается
    ///     плоской стороной вверх, на ней растёт шапочка начинки (идеально ~100%).
    ///   • Вторую скорлупку берёшь в руку (F) и ЛКМ по скорлупке с начинкой — накрыл, «щёлк»:
    ///     это уже «Макарон «Солёная каска»». Оценка = скорлупки + насколько половинки одного
    ///     размера + сколько начинки.
    ///   • Без цели ЛКМ — как у любой еды (бросок).
    ///
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · Box Collider · этот компонент
    /// (вместо Food Piece — он и есть Food Piece). Вид строится сам (дети Bottom/Filling/Top).
    /// Продукт — «Скорлупка макарон», Dish — «Макарон «Солёная каска»» (пусто — по названию).
    /// </summary>
    public sealed class MacaronShell : FoodPiece
    {
        [Header("Макарон")]
        [Tooltip("Во что превращается собранный (пусто — «Макарон «Солёная каска»»)")]
        [SerializeField] private ProductDef _dish;
        [Tooltip("Радиус скорлупки, если положили в сцену руками (противень ставит свой)")]
        [SerializeField] private float _defaultRadius = 0.25f;
        [SerializeField] private Material _mat;
        [SerializeField] private Color _shellColor = new Color(0.78f, 0.58f, 0.40f);
        [SerializeField] private Color _burntColor = new Color(0.42f, 0.27f, 0.16f);
        [Tooltip("Дальность «накрыть» второй скорлупкой, м")]
        [SerializeField] private float _assembleReach = 3f;
        [SerializeField] private float _maxFilling = 1.5f;

        [Header("Подсказка начинки (шприц)")]
        [Tooltip("Над скорлупкой, на которую смотришь со шприцем: сколько ганаша и хватит ли")]
        [SerializeField] private bool _showFillHint = true;
        [Tooltip("Начинка: ниже — «мало» (−10…−25 к оценке)")]
        [SerializeField] private float _fillPerfectMin = 0.8f;
        [Tooltip("Начинка: выше — «перебор» (−15)")]
        [SerializeField] private float _fillPerfectMax = 1.3f;

        [Header("Готово — золотые звёздочки")]
        [SerializeField] private bool _stars = true;
        [Range(5, 60)][SerializeField] private int _starCount = 18;

        private readonly SyncVar<float> _radius = new SyncVar<float>(0f);
        private readonly SyncVar<byte> _size = new SyncVar<byte>(100);
        private readonly SyncVar<byte> _flags = new SyncVar<byte>(MacaronTray.Feet);
        private readonly SyncVar<byte> _quality = new SyncVar<byte>(100);
        private readonly SyncVar<byte> _filling = new SyncVar<byte>(0);
        private readonly SyncVar<ushort> _fillProduct = new SyncVar<ushort>(0);
        private readonly SyncVar<bool> _assembled = new SyncVar<bool>(false);
        private readonly SyncVar<byte> _topSize = new SyncVar<byte>(100);
        private readonly SyncVar<byte> _topFlags = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _score = new SyncVar<byte>(0);

        // до спавна
        private bool _shellPreset;
        private float _pRadius, _pSize;
        private byte _pFlags, _pQuality;

        private Transform _bottom, _fill, _top;
        private MeshRenderer _bottomR, _fillR, _topR;
        private MaterialPropertyBlock _mpb;
        private readonly RaycastHit[] _hits = new RaycastHit[12];

        public float Radius => _radius.Value > 0f ? _radius.Value : _defaultRadius;
        public float Size01 => _size.Value / 100f;
        public byte Flags => _flags.Value;
        public float Filling01 => _filling.Value / 255f * _maxFilling;
        public bool Assembled => _assembled.Value;
        public int Score => _score.Value;

        protected override void Awake()
        {
            base.Awake();
            MeshRenderer own = GetComponent<MeshRenderer>();
            if (own != null) { if (_mat == null) _mat = own.sharedMaterial; own.enabled = false; }   // вид — у детей
            _bottom = Part("Bottom", out _bottomR);
            _fill = Part("Filling", out _fillR);
            _top = Part("Top", out _topR);
            _fill.GetComponent<MeshFilter>().sharedMesh = MacaronMesh.Puck();
            ApplyLook();
        }

        private Transform Part(string n, out MeshRenderer r)
        {
            var go = new GameObject(n);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            return go.transform;
        }

        /// <summary>Противень: до Spawn — радиус (мир, м), размер от кольца, флаги и оценка.</summary>
        public void ServerSetup(float radius, float sizeK, byte flags, byte quality)
        {
            _shellPreset = true;
            _pRadius = radius; _pSize = sizeK; _pFlags = flags; _pQuality = quality;
            // вид сразу (до прихода SyncVar)
            ApplyLookWith(radius, sizeK, flags, 0f, false, 0, 1f);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_shellPreset)
            {
                // радиус в осях объекта (у пласта масштаб может быть не 1)
                Vector3 s = transform.lossyScale;
                float k = Mathf.Max(1e-4f, Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)));
                _radius.Value = _pRadius / k;
                _size.Value = (byte)Mathf.Clamp(Mathf.RoundToInt(_pSize * 100f), 1, 255);
                _flags.Value = _pFlags;
                _quality.Value = _pQuality;
            }
            if (_dish == null) _dish = MacaronTitles.Find(FoodDatabase.Instance, CookRecipeBook.Macaron.Dish);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _radius.OnChange += OnF; _size.OnChange += OnB; _flags.OnChange += OnB; _filling.OnChange += OnB;
            _assembled.OnChange += OnBool; _topSize.OnChange += OnB; _fillProduct.OnChange += OnU;
            ApplyLook();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _radius.OnChange -= OnF; _size.OnChange -= OnB; _flags.OnChange -= OnB; _filling.OnChange -= OnB;
            _assembled.OnChange -= OnBool; _topSize.OnChange -= OnB; _fillProduct.OnChange -= OnU;
        }

        private void OnF(float a, float b, bool s) { if (s && base.IsClientInitialized) return; ApplyLook(); }
        private void OnB(byte a, byte b, bool s) { if (s && base.IsClientInitialized) return; ApplyLook(); }
        private void OnU(ushort a, ushort b, bool s) { if (s && base.IsClientInitialized) return; ApplyLook(); }
        private void OnBool(bool a, bool b, bool s) { if (s && base.IsClientInitialized) return; ApplyLook(); }

        // ───────────────────────── НАЧИНКА (шприц) ─────────────────────────

        /// <summary>Шприц давит начинку amount порций. Возвращает, сколько ушло. Сервер.</summary>
        [Server]
        public float ServerAddFilling(ushort product, float amount, out string why)
        {
            why = null;
            if (_assembled.Value) { why = "макарон уже собран"; return 0f; }
            if (IsHeld) { why = "скорлупка в руке — положи её"; return 0f; }
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef d = db != null ? db.Get(product) : null;
            if (d != null && (d.Title == CookRecipeBook.Macaron.Batter || d.Title == CookRecipeBook.Macaron.BatterOver))
            { why = "тесто — в кольца противня, а на скорлупку — ганаш"; return 0f; }
            if (_filling.Value > 0 && _fillProduct.Value != 0 && _fillProduct.Value != product) { why = "на скорлупке уже другая начинка"; return 0f; }
            float f = Filling01;
            float use = Mathf.Min(amount, _maxFilling - f);
            if (use <= 0f) { why = "начинки уже с горкой"; return 0f; }
            _fillProduct.Value = product;
            float nf = f + use;
            _filling.Value = (byte)Mathf.Clamp(Mathf.RoundToInt(nf / _maxFilling * 255f), 0, 255);
            return use;
        }

        // ───────────────────────── СБОРКА (ЛКМ скорлупкой в руке) ─────────────────────────

        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (ctx.Hand != null && ctx.Hand.HeldItem == this && !_assembled.Value && FindBase(ctx, out MacaronShell target))
            {
                ctx.Hand.ServerDrop(Vector3.zero, 0f);
                target.ServerAssemble(this);
                return;
            }
            base.ServerPrimaryUse(ctx);
        }

        private bool FindBase(UseContext ctx, out MacaronShell target)
        {
            target = null;
            float reach = _assembleReach * Mathf.Max(1f, transform.lossyScale.x);
            int n = Physics.RaycastNonAlloc(ctx.Origin, ctx.Direction, _hits, reach, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                MacaronShell s = h.collider.GetComponentInParent<MacaronShell>();
                if (s == null || s == this || s.Assembled || s.Filling01 < 0.3f) continue;
                best = h.distance; target = s;
            }
            return target != null;
        }

        [Server]
        public void ServerAssemble(MacaronShell top)
        {
            if (top == null || _assembled.Value || top.IsConsumed) return;
            float sa = Size01, sb = top.Size01;
            float fill = Filling01;
            int q = Mathf.RoundToInt((_quality.Value + top._quality.Value) * 0.5f);
            int sizePen = Mathf.RoundToInt(Mathf.Abs(sa - sb) * 120f);
            int fillPen = fill < 0.6f ? 25 : fill < 0.8f ? 10 : fill > 1.3f ? 15 : 0;
            int score = Mathf.Clamp(q - sizePen - fillPen, 5, 100);
            _topSize.Value = top._size.Value;
            _topFlags.Value = top._flags.Value;
            _assembled.Value = true;
            _score.Value = (byte)score;
            if (_dish == null) _dish = MacaronTitles.Find(FoodDatabase.Instance, CookRecipeBook.Macaron.Dish);
            if (_dish != null) ServerSetProductState(_dish, _dish.StartState);
            Debug.Log($"[Макарон] «{name}» собран: оценка {score}/100 (скорлупки {q}, половинки {sa:0.00}/{sb:0.00} → −{sizePen}, " +
                      $"начинка {fill * 100f:0}% → −{fillPen})" + (_dish == null ? " ⚠ нет продукта «Макарон «Солёная каска»» — меню «Макарон → 1»" : ""), this);
            top.ServerTryConsume();
            top.NetworkObject.Despawn();
            RpcStars((byte)score);
        }

        /// <summary>Макарон собран — золотые звёздочки (больше за высокую оценку). Только вид.</summary>
        [ObserversRpc]
        private void RpcStars(byte score)
        {
            if (!_stars) return;
            float r = Radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
            Vector3 at = transform.position + Vector3.up * r * 0.9f;
            int n = Mathf.RoundToInt(_starCount * Mathf.Lerp(0.4f, 1.2f, score / 100f));
            Color c = score >= 70 ? CelebrationStars.Gold : Color.Lerp(CelebrationStars.Gold, new Color(0.75f, 0.75f, 0.78f), 0.6f);   // слабая оценка — серебро
            CelebrationStars.Burst(at, Mathf.Clamp(r / 0.25f, 0.3f, 2f) * 0.7f, c, n);
            Debug.Log($"[Макарон] «{name}»: ★ звёздочки — оценка {score}/100", this);
        }

        // ───────────────────────── ПОДСКАЗКА НАЧИНКИ ─────────────────────────

        public string FillVerdict(float f) =>
            f < 0.02f ? "держи ЛКМ — давить ганаш" : f < _fillPerfectMin ? "— мало" : f <= _fillPerfectMax ? "— ИДЕАЛЬНО" : "— ПЕРЕБОР";

        private static GUIStyle _hintStyle;

        /// <summary>Над скорлупкой, на которую смотрит игрок со шприцем: «ганаш 85% — ИДЕАЛЬНО».</summary>
        private void OnGUI()
        {
            if (!_showFillHint || Event.current.type != EventType.Repaint) return;
            if (PipingBagItem.LocalAimShell != this || _assembled.Value) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            float r = Radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * (r * 1.1f + 0.05f));
            if (sp.z <= 0f) return;
            float f = Filling01;
            string s = f < 0.02f ? $"начинка: {FillVerdict(f)}" : $"начинка {f * 100f:0}% {FillVerdict(f)}";
            if (f >= _fillPerfectMin && f <= _fillPerfectMax) s += " · накрой второй скорлупкой";
            Color c = f < 0.02f ? Color.white : f < _fillPerfectMin ? new Color(1f, 0.85f, 0.3f) : f <= _fillPerfectMax ? new Color(0.4f, 1f, 0.45f) : new Color(1f, 0.4f, 0.3f);
            if (_hintStyle == null) _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Rect rc = new Rect(sp.x - 200f, Screen.height - sp.y - 30f, 400f, 28f);
            _hintStyle.normal.textColor = Color.black;
            GUI.Label(new Rect(rc.x + 1, rc.y + 1, rc.width, rc.height), s, _hintStyle);
            _hintStyle.normal.textColor = c;
            GUI.Label(rc, s, _hintStyle);
            // полоска 0..150%: зелёная зона — идеал
            float w = 160f, x0 = sp.x - w * 0.5f, y0 = rc.yMax + 2f;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(new Rect(x0 - 1, y0 - 1, w + 2, 8), Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 0.8f, 0.35f, 0.45f);
            GUI.DrawTexture(new Rect(x0 + w * _fillPerfectMin / _maxFilling, y0, w * (_fillPerfectMax - _fillPerfectMin) / _maxFilling, 6), Texture2D.whiteTexture);
            GUI.color = c; GUI.DrawTexture(new Rect(x0, y0, w * Mathf.Clamp01(f / _maxFilling), 6), Texture2D.whiteTexture);
            GUI.color = old;
        }

        [ContextMenu("Тест: звёздочки")]
        private void TestStars()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Макарон] звёздочки — только в Play", this); return; }
            CelebrationStars.Burst(transform.position + Vector3.up * Radius, Mathf.Clamp(Radius / 0.25f, 0.3f, 2f) * 0.7f, CelebrationStars.Gold, _starCount);
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void ApplyLook() =>
            ApplyLookWith(Radius, Size01, _flags.Value, Filling01, _assembled.Value, _topFlags.Value, _topSize.Value / 100f);

        private void ApplyLookWith(float r, float size, byte flags, float fill, bool assembled, byte topFlags, float topSize)
        {
            if (_bottom == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            bool spread = (flags & MacaronTray.Spread) != 0;
            float hK = spread ? 0.45f : 0.78f;
            float shellH = 0.62f * r * hK;                                // высота скорлупки
            _bottom.GetComponent<MeshFilter>().sharedMesh = MacaronMesh.Shell((flags & MacaronTray.Cracked) != 0);
            bool flipped = fill > 0.01f || assembled;
            if (flipped)
            {
                // перевёрнута плоской стороной вверх — под начинку
                _bottom.localRotation = Quaternion.Euler(180f, 0f, 0f);
                _bottom.localPosition = new Vector3(0f, shellH, 0f);
            }
            else
            {
                _bottom.localRotation = Quaternion.identity;
                _bottom.localPosition = Vector3.zero;
            }
            _bottom.localScale = new Vector3(r, r * hK, r);
            Tint(_bottomR, (flags & MacaronTray.Burnt) != 0 ? _burntColor : _shellColor, 0.12f);

            float fh = 0f;
            _fillR.enabled = fill > 0.01f;
            if (fill > 0.01f)
            {
                float fr = r * 0.9f * Mathf.Clamp(Mathf.Sqrt(fill), 0.35f, 1.12f);
                fh = r * 0.32f * Mathf.Clamp(fill, 0.2f, 1.4f);
                _fill.localPosition = new Vector3(0f, shellH, 0f);
                _fill.localScale = new Vector3(fr, fh, fr);
                FoodDatabase db = FoodDatabase.Instance;
                ProductDef d = db != null ? db.Get(_fillProduct.Value) : null;
                Tint(_fillR, d != null ? d.BaseColor : new Color(0.42f, 0.24f, 0.12f), 0.6f);
            }

            _topR.enabled = assembled;
            float total = shellH + fh;
            if (assembled)
            {
                bool tSpread = (topFlags & MacaronTray.Spread) != 0;
                float tk = tSpread ? 0.45f : 0.78f;
                float tr = r * Mathf.Clamp(topSize / Mathf.Max(0.05f, size), 0.6f, 1.5f);
                _top.GetComponent<MeshFilter>().sharedMesh = MacaronMesh.Shell((topFlags & MacaronTray.Cracked) != 0);
                _top.localRotation = Quaternion.identity;
                _top.localPosition = new Vector3(0f, shellH + fh * 0.8f, 0f);
                _top.localScale = new Vector3(tr, tr * tk, tr);
                Tint(_topR, (topFlags & MacaronTray.Burnt) != 0 ? _burntColor : _shellColor, 0.12f);
                total = shellH + fh * 0.8f + 0.62f * tr * tk;
            }
            BoxCollider bc = GetComponent<BoxCollider>();
            if (bc != null)
            {
                bc.size = new Vector3(r * 2f, Mathf.Max(total, r * 0.2f), r * 2f);
                bc.center = new Vector3(0f, Mathf.Max(total, r * 0.2f) * 0.5f, 0f);
            }
        }

        private void Tint(MeshRenderer r, Color c, float glint)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            c.a = 1f;
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetFloat("_GlintStrength", glint);
            r.SetPropertyBlock(_mpb);
        }

#if UNITY_EDITOR
        public void EditorSetup(ProductDef dish, float radius, Material mat)
        {
            if (dish != null) _dish = dish;
            _defaultRadius = radius; _mat = mat;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
