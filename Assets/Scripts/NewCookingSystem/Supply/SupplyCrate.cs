using System.Collections;
using System.Collections.Generic;
using System.Text;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Supply
{
    /// <summary>Один предмет поставки: что спавнить и куда (ящик или площадка).</summary>
    public struct SupplyItem
    {
        public NetworkObject Prefab;
        public ProductDef Product;
        /// <summary>Префаба у продукта нет — едет ведро (Prefab — ведро) с Portions порций продукта.</summary>
        public bool Bucket;
        public int Portions;
        public Vector3 Size;
        public bool Pallet;
        public SupplyCategory Category;
        /// <summary>v24.2: запас источника (мешок, банка, бидон, пучок) в порциях; ≤0 — как в префабе.</summary>
        public int Stock;
        /// <summary>v24.3: для какого блюда (или нескольких через «·») — маркировка ящика.</summary>
        public string Dish;
    }

    /// <summary>
    /// v24.2: ЯЩИК ПОСТАВКИ (маркировка «для чего»; запас источников) — деревянный ящик с крышкой на петлях. На боках и крышке — знак типа (мясо, овощи, фрукты,
    /// специи, морепродукты, молочка, бакалея), цветная полоса и надпись.
    ///
    ///   F по закрытому ящику — крышка откидывается, продукты появляются внутри (по одному, сверху — без «взрыва» физики).
    ///   Смотришь на ящик — подсказка: тип и что внутри.
    ///   Игрок ящики не таскает. ДЛЯ ТЕСТОВ (галка Carry For Tests): F по ОТКРЫТОМУ ящику — взять в руки.
    ///
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · этот компонент; дети: корпус (меш + коллайдеры
    /// дна и стенок), петля крышки (Lid Pivot) → крышка (меш + коллайдер). Собирает меню CatCooks → Доставка → 1.
    /// Знаки, полосы и надписи создаются в игре кодом (текстуры рисуются сами — ассеты не нужны).
    /// </summary>
    public sealed class SupplyCrate : PickupItem
    {
        [Header("Ящик")]
        [SerializeField] private Transform _lidPivot;
        [SerializeField] private float _openAngle = 112f;
        [Tooltip("Внутренность: центр дна и размер (в осях ящика)")]
        [SerializeField] private Vector3 _innerBottom = new Vector3(0f, 0.035f, 0f);
        [SerializeField] private Vector3 _innerSize = new Vector3(0.82f, 0.44f, 0.52f);
        [Tooltip("Наружные размеры (для знаков и полос)")]
        [SerializeField] private Vector3 _outer = new Vector3(0.9f, 0.5f, 0.6f);
        [Tooltip("Материал знака (Unlit с отсечкой по альфе) — ставит меню")]
        [SerializeField] private Material _emblemMaterial;
        [Tooltip("Материал полосы (Lit, цвет красится сам)")]
        [SerializeField] private Material _bandMaterial;

        [Header("Тесты")]
        [Tooltip("Для тестов: открытый ящик можно взять в руки (F). В игре — выключить")]
        [SerializeField] private bool _carryForTests = true;
        [SerializeField] private float _spawnInterval = 0.06f;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _category = new SyncVar<byte>((byte)SupplyCategory.Grocery);
        private readonly SyncVar<bool> _open = new SyncVar<bool>(false);
        private readonly SyncVar<string> _manifest = new SyncVar<string>("");
        private readonly SyncVar<string> _tag = new SyncVar<string>("");        // v24.2: «для чего» — Макарон ×30

        private readonly List<SupplyItem> _contents = new List<SupplyItem>();   // сервер
        private float _lidT;                                                    // 0 закрыта … 1 открыта (вид)
        private bool _decorated;
        private MaterialPropertyBlock _mpb;
        private readonly List<Renderer> _bands = new List<Renderer>();
        private readonly List<Renderer> _emblems = new List<Renderer>();
        private readonly List<TextMesh> _labels = new List<TextMesh>();

        public SupplyCategory Category => (SupplyCategory)_category.Value;
        public Vector3 InnerSize => _innerSize;
        public Vector3 Outer => _outer;
        public Vector3 InnerBottom => _innerBottom;
        public static readonly Vector3 DefaultInner = new Vector3(1.32f, 0.84f, 0.92f);

        /// <summary>
        /// v24.4: РАСКЛАДКА В ЯЩИКЕ — одна на всех (упаковка при доставке и выкладка при открытии):
        /// ряды по X, ряды по Z, слои вверх по реальной высоте. Предмет можно повернуть на 90°, если так влезает.
        /// false — не влезло. centers — центры в осях ящика (от центра дна), turned — повёрнут ли.
        /// </summary>
        public static bool Layout(IList<Vector3> sizes, Vector3 inner, List<Vector3> centers, List<bool> turned)
        {
            centers?.Clear(); turned?.Clear();
            const float gap = 0.02f;
            float x0 = -inner.x * 0.5f, z0 = -inner.z * 0.5f;
            float x = x0, z = z0, rowDepth = 0f, layerY = 0f, layerH = 0f;
            foreach (Vector3 s0 in sizes)
            {
                float w = Mathf.Max(0.04f, s0.x) + gap, d = Mathf.Max(0.04f, s0.z) + gap, h = Mathf.Max(0.04f, s0.y);
                bool turn = false;
                if (w > inner.x + 1e-3f && d <= inner.x + 1e-3f && w <= inner.z + 1e-3f) { float t = w; w = d; d = t; turn = true; }
                if (w > inner.x + 1e-3f || d > inner.z + 1e-3f || h > inner.y + 1e-3f) return false;   // не влезет и в пустой
                if (x + w > -x0 + 1e-3f) { x = x0; z += rowDepth; rowDepth = 0f; }
                if (z + d > -z0 + 1e-3f) { x = x0; z = z0; rowDepth = 0f; layerY += layerH; layerH = 0f; }
                if (layerY + h > inner.y + 1e-3f) return false;
                centers?.Add(new Vector3(x + w * 0.5f, layerY + h * 0.5f, z + d * 0.5f));
                turned?.Add(turn);
                x += w; rowDepth = Mathf.Max(rowDepth, d); layerH = Mathf.Max(layerH, h);
            }
            return true;
        }

        /// <summary>Влезет ли предмет хотя бы в пустой ящик (иначе — едет на площадке).</summary>
        /// <summary>
        /// v24.9: луч F игрока (PlayerInteractor) бьёт только по слоям Interactable / Food / Cookware / Cuttable —
        /// ящики и лотки, собранные меню, были на Default, и F проходил сквозь них («не открываются»).
        /// </summary>
        public static void MakeInteractable(GameObject go)
        {
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer < 0) return;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.layer == 0) t.gameObject.layer = layer;
        }

        public static bool FitsEmpty(Vector3 size, Vector3 inner) => Layout(new[] { size }, inner, null, null);
        public bool IsOpen => _open.Value;
        public string Manifest => _manifest.Value;
        public override float CarrySpeedMultiplier => 0.6f;

        protected override void Awake()
        {
            base.Awake();
            SupplyCrate.MakeInteractable(gameObject);                        // v24.9: F игрока видит только слои Interactable/Food/…
            _canPickup = _carryForTests;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _category.OnChange += OnCategory;
            _tag.OnChange += OnTag;
            Decorate();
            ApplyCategory();
            _lidT = _open.Value ? 1f : 0f;
            ApplyLid();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _category.OnChange -= OnCategory;
            _tag.OnChange -= OnTag;
        }

        private void OnTag(string a, string b, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ApplyCategory();
        }

        public string Tag => _tag.Value;

        private void OnCategory(byte a, byte b, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ApplyCategory();
        }

        // ───────────────────────── сервер ─────────────────────────

        /// <summary>Сервер: тип и содержимое (до или сразу после Spawn).</summary>
        /// v24.8: БЕЗ [Server] — FishNet по этому атрибуту молча выходил из метода, пока ящик ещё не заспавнен,
        /// и содержимое терялось → все ящики «БАКАЛЕЯ · пустой». Вызывается только кодом сервера (доставка).
        public void ServerSetup(SupplyCategory cat, List<SupplyItem> items, bool carryForTests, string tag = "")
        {
            // v24.5: значения SyncVar, выставленные ДО Spawn, FishNet может не донести — запоминаем и
            // выставляем в OnStartServer (после Spawn — сразу)
            _contents.Clear();
            if (items != null) _contents.AddRange(items);
            _carryForTests = carryForTests;
            _canPickup = carryForTests;
            _pendingCategory = (byte)cat;
            _pendingTag = DishTag(items, tag);
            _pendingManifest = BuildManifest(_contents);
            _hasPending = true;
            if (base.IsServerInitialized) ApplyPending();
        }

        private bool _hasPending;
        private byte _pendingCategory;
        private string _pendingTag = "", _pendingManifest = "";

        private void ApplyPending()
        {
            if (!_hasPending) return;
            _hasPending = false;
            _category.Value = _pendingCategory;
            _tag.Value = _pendingTag;
            _manifest.Value = _pendingManifest;
            if (base.IsClientInitialized || !base.IsServerInitialized) ApplyCategory();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ApplyPending();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyCategory();                                                    // хост и поздние клиенты — знак и надписи по текущему типу
        }

        /// <summary>Маркировка: блюда, для которых лежит содержимое ящика; нет данных — общий тег поставки.</summary>
        private static string DishTag(List<SupplyItem> items, string fallback)
        {
            var dishes = new List<string>();
            if (items != null)
                foreach (SupplyItem it in items)
                {
                    if (string.IsNullOrEmpty(it.Dish)) continue;
                    foreach (string d in it.Dish.Split('·'))
                    {
                        string t = d.Trim();
                        if (t.Length > 0 && !dishes.Contains(t)) dishes.Add(t);
                    }
                }
            return dishes.Count > 0 ? string.Join(" · ", dishes) : (fallback ?? "");
        }

        private static string BuildManifest(List<SupplyItem> items)
        {
            var order = new List<string>();
            var n = new Dictionary<string, int>();
            foreach (SupplyItem it in items)
            {
                string t = it.Product != null ? it.Product.Title : (it.Prefab != null ? it.Prefab.name : "?");
                if (it.Bucket) t += $" (ведро {it.Portions})";
                else if (it.Stock > 0) t += $" ({it.Stock} порц.)";
                if (!n.ContainsKey(t)) { n[t] = 0; order.Add(t); }
                n[t]++;
            }
            var sb = new StringBuilder();
            foreach (string t in order) { if (sb.Length > 0) sb.Append(" · "); sb.Append(n[t] > 1 ? $"{t} ×{n[t]}" : t); }
            return sb.ToString();
        }

        /// <summary>F: закрыт — открыть; открыт — (только для тестов) взять в руки.</summary>
        public override void ServerInteract(PlayerInteractor interactor)
        {
            if (!_open.Value) { ServerOpen(); return; }
            if (_carryForTests) base.ServerInteract(interactor);
        }

        [Server]
        public void ServerOpen()
        {
            if (_open.Value) return;
            _open.Value = true;
            if (_log) Debug.Log($"[Доставка] ящик «{SupplyCategories.Title(Category)}» открыт: {_manifest.Value} (на сервере предметов: {_contents.Count})", this);
            StartCoroutine(SpawnContents());
        }

        private IEnumerator SpawnContents()
        {
            yield return new WaitForSeconds(0.25f);                             // крышка успела откинуться
            // v24.4: та же раскладка, что при упаковке (Layout), — предметы не налезают друг на друга и на стенки
            var items = new List<SupplyItem>(_contents);
            _contents.Clear();
            var sizes = new List<Vector3>(items.Count);
            foreach (SupplyItem it in items) sizes.Add(it.Size);
            var centers = new List<Vector3>(); var turned = new List<bool>();
            bool ok = Layout(sizes, _innerSize, centers, turned);
            for (int i = 0; i < items.Count; i++)
            {
                Vector3 local;
                bool turn = false;
                if (ok && i < centers.Count) { local = _innerBottom + centers[i] + Vector3.up * 0.02f; turn = turned[i]; }
                else local = _innerBottom + new Vector3(0f, _innerSize.y + 0.1f + i * 0.05f, 0f);   // не по плану (старый ящик) — сверху по одному
                Quaternion rot = transform.rotation * Quaternion.Euler(0f, turn ? 90f : 0f, 0f);
                SupplyDelivery.ServerSpawnItem(this, items[i], transform.TransformPoint(local), rot);
                yield return new WaitForSeconds(_spawnInterval);
            }
        }

        // ───────────────────────── вид ─────────────────────────

        private void Update()
        {
            float want = _open.Value ? 1f : 0f;
            if (Mathf.Abs(_lidT - want) < 1e-3f) return;
            _lidT = Mathf.MoveTowards(_lidT, want, Time.deltaTime / 0.45f);
            ApplyLid();
        }

        private void ApplyLid()
        {
            if (_lidPivot == null) return;
            float k = 1f - (1f - _lidT) * (1f - _lidT);                          // быстро в начале, мягко в конце
            _lidPivot.localRotation = Quaternion.Euler(-_openAngle * k, 0f, 0f);
        }

        /// <summary>Знаки на четырёх боках и крышке, полоса по периметру, надписи — создаются кодом (у всех игроков).</summary>
        private void Decorate()
        {
            if (_decorated || Application.isBatchMode) return;
            _decorated = true;
            float W = _outer.x, H = _outer.y, D = _outer.z;
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material emblemMat = _emblemMaterial;
            if (emblemMat == null && unlit != null)
            {
                emblemMat = new Material(unlit) { name = "CC Supply Emblem (auto)", hideFlags = HideFlags.DontSave };
                emblemMat.SetFloat("_AlphaClip", 1f); emblemMat.SetFloat("_Cutoff", 0.5f); emblemMat.EnableKeyword("_ALPHATEST_ON");
            }
            Material bandMat = _bandMaterial;
            if (bandMat == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null) bandMat = new Material(lit) { name = "CC Supply Band (auto)", hideFlags = HideFlags.DontSave };
            }
            float e = Mathf.Min(H, D) * 0.62f;                                   // размер знака на торце
            // знаки: перед/зад (±Z) и бока (±X)
            AddQuad("Emblem +Z", new Vector3(-W * 0.18f, H * 0.52f, D * 0.5f + 0.004f), Quaternion.Euler(0f, 180f, 0f), e, emblemMat, _emblems);
            AddQuad("Emblem −Z", new Vector3(W * 0.18f, H * 0.52f, -D * 0.5f - 0.004f), Quaternion.identity, e, emblemMat, _emblems);
            AddQuad("Emblem +X", new Vector3(W * 0.5f + 0.004f, H * 0.52f, 0f), Quaternion.Euler(0f, -90f, 0f), e * 0.9f, emblemMat, _emblems);
            AddQuad("Emblem −X", new Vector3(-W * 0.5f - 0.004f, H * 0.52f, 0f), Quaternion.Euler(0f, 90f, 0f), e * 0.9f, emblemMat, _emblems);
            // на крышке — крупно (едет с крышкой)
            if (_lidPivot != null)
            {
                float lidTop = 0.032f;
                Vector3 c = _lidPivot.InverseTransformPoint(transform.TransformPoint(new Vector3(0f, H + lidTop + 0.004f, 0f)));
                AddQuad("Emblem Lid", c, Quaternion.Euler(90f, 0f, 0f), Mathf.Min(W, D) * 0.7f, emblemMat, _emblems, _lidPivot);
            }
            // полоса цвета типа по периметру (тонкие пластины)
            float bh = H * 0.12f, by = H * 0.18f, t = 0.006f;
            AddBand(new Vector3(0f, by, D * 0.5f + t), new Vector3(W * 0.98f, bh, t), bandMat);
            AddBand(new Vector3(0f, by, -D * 0.5f - t), new Vector3(W * 0.98f, bh, t), bandMat);
            AddBand(new Vector3(W * 0.5f + t, by, 0f), new Vector3(t, bh, D * 0.98f), bandMat);
            AddBand(new Vector3(-W * 0.5f - t, by, 0f), new Vector3(t, bh, D * 0.98f), bandMat);
            // надписи рядом со знаком на переднем и заднем торце
            AddLabel(new Vector3(W * 0.2f, H * 0.52f, D * 0.5f + 0.006f), Quaternion.Euler(0f, 180f, 0f));
            AddLabel(new Vector3(-W * 0.2f, H * 0.52f, -D * 0.5f - 0.006f), Quaternion.identity);
        }

        private void AddQuad(string n, Vector3 localPos, Quaternion localRot, float size, Material mat, List<Renderer> list, Transform parent = null)
        {
            if (mat == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = n;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            list.Add(r);
        }

        private void AddBand(Vector3 localPos, Vector3 size, Material mat)
        {
            if (mat == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Band";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _bands.Add(r);
        }

        private void AddLabel(Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var tm = go.AddComponent<TextMesh>();
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f != null) { tm.font = f; go.GetComponent<MeshRenderer>().sharedMaterial = f.material; }
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = 0.012f;
            tm.fontStyle = FontStyle.Bold;
            _labels.Add(tm);
        }

        private void ApplyCategory()
        {
            if (!_decorated) return;
            SupplyCategory c = Category;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            Texture2D tex = SupplyCategories.Emblem(c);
            foreach (Renderer r in _emblems)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetTexture("_BaseMap", tex);
                _mpb.SetTexture("_MainTex", tex);
                _mpb.SetColor("_BaseColor", Color.white);
                r.SetPropertyBlock(_mpb);
            }
            Color col = SupplyCategories.Tint(c);
            foreach (Renderer r in _bands)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.Clear();
                _mpb.SetColor("_BaseColor", col);
                _mpb.SetColor("_Color", col);
                r.SetPropertyBlock(_mpb);
            }
            string title = SupplyCategories.Title(c);
            string tag = _tag.Value;
            if (!string.IsNullOrEmpty(tag) && tag.Length > 26) tag = tag.Substring(0, 25) + "…";
            foreach (TextMesh tm in _labels)
            {
                if (tm == null) continue;
                tm.text = string.IsNullOrEmpty(tag) ? title : $"{title}\n<size=30>{tag}</size>";
                tm.color = new Color(0.1f, 0.08f, 0.06f);
                tm.characterSize = 0.012f * Mathf.Clamp(6f / Mathf.Max(4, title.Length), 0.55f, 1.2f);
            }
            _mpb.Clear();
        }

        // ───────────────────────── подсказка (у каждого игрока) ─────────────────────────

        private static GUIStyle _style;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            Camera cam = Camera.main;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 25f) return;
            if (!Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit h, 4f, ~0, QueryTriggerInteraction.Ignore)) return;
            if (h.collider.GetComponentInParent<SupplyCrate>() != this) return;
            string head = $"Ящик: {SupplyCategories.Title(Category)}" + (string.IsNullOrEmpty(_tag.Value) ? "" : $"  ·  для: {_tag.Value}");
            string act = !_open.Value ? "F — открыть" : (_carryForTests ? "F — взять ящик (тест)" : "открыт");
            string body = string.IsNullOrEmpty(_manifest.Value) ? "пустой" : _manifest.Value;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, wordWrap = true };
            Rect rc = new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.5f + 40f, 520f, 90f);
            string s = $"{head}   ·   {act}\n<size=13>{body}</size>";
            _style.richText = true;
            _style.normal.textColor = Color.black;
            GUI.Label(new Rect(rc.x + 1, rc.y + 1, rc.width, rc.height), s, _style);
            _style.normal.textColor = SupplyCategories.Tint(Category) * 0.4f + Color.white * 0.6f;
            GUI.Label(rc, s, _style);
        }
    }
}
