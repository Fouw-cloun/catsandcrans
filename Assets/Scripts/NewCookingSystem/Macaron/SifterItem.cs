using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ГРОХОТ — строительное сито: рама с сеткой и ручками. Просеивает муку для макарон.
    ///
    ///   1. Поставь грохот где угодно и мастерком кидай в него (ПКМ) миндальную муку и сахар —
    ///      это обычная тара (Cookware), на сетке растёт горка.
    ///   2. Возьми грохот (F) и держи над ведром.
    ///   3. Зажми ЛКМ и тряси мышью влево-вправо (камера стоит, игрок стоит) — сквозь сетку сыплется
    ///      пыль, в ведре под грохотом растёт смесь:
    ///        • мука миндальная + сахар (по порции того и другого) → «Миндальная смесь» (2 порции);
    ///        • что-то одно (сахар, какао, мука) — просто просеивается как есть.
    ///      Под грохотом нет тары — трясёшь впустую (мука не теряется, в логе подсказка).
    ///
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · Cookware (Def «Грохот»: хранит,
    /// не глотает, без рецептов) · этот компонент · коллайдеры рамы и сетки · Inside (триггер над сеткой) ·
    /// Pile — горка на сетке (меш-купол, материал Food Heap). Делает меню «Макарон → 3».
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class SifterItem : PickupItem
    {
        [Header("Продукты (пусто — по названию)")]
        [SerializeField] private ProductDef _almond;
        [SerializeField] private ProductDef _sugar;
        [SerializeField] private ProductDef _mix;

        [Header("Вместимость")]
        [Tooltip("v23.55: сколько мастерков влезает в грохот за раз (1 мастерок = 2 порции). Тара «Грохот» поднимается до этого сама")]
        [Range(1, 30)][SerializeField] private int _trowels = 10;

        [Header("Тряска")]
        [Tooltip("Сколько «пути мыши» (влево-вправо) на одну просеянную порцию")]
        [SerializeField] private float _shakePerPortion = 2.2f;
        [Tooltip("Не чаще, секунд")]
        [SerializeField] private float _minInterval = 0.08f;
        [Tooltip("Как далеко вниз искать тару под грохотом, м")]
        [SerializeField] private float _dropReach = 3f;
        [Tooltip("Размах тряски в руке, м")]
        [SerializeField] private float _shakeAmp = 0.05f;

        [Header("Вид")]
        [Tooltip("Горка на сетке (растёт от содержимого). Пусто — дочерний «Pile»")]
        [SerializeField] private Transform _pile;
        [Tooltip("Откуда сыплется пыль (низ сетки). Пусто — центр грохота")]
        [SerializeField] private Transform _mesh;

        [SerializeField] private bool _log = true;

        private readonly SyncVar<bool> _shaking = new SyncVar<bool>(false);

        private Cookware _cw;
        private readonly HeldToolLook _look = new HeldToolLook();
        private float _acc, _lastX, _phase, _nextCmd;
        private bool _lmb;
        private float _serverNext, _hintAt;
        private int _sifted;
        private Vector3 _pileScale;
        private MaterialPropertyBlock _mpb;
        private Renderer _pileR;
        private readonly RaycastHit[] _hits = new RaycastHit[12];
        // v23.53: тучки при просеивании (у всех игроков)
        private float _curtainAcc, _topAcc, _belowAt;
        private Vector3 _belowPoint;
        private bool _belowHasCw;
        private Renderer _meshR;

        public bool IsShaking => base.IsOwner ? _lmb : _shaking.Value;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            _cw = GetComponent<Cookware>();
            // v23.55: 10 мастерков за раз — поднять вместимость тары «Грохот» (ассет общий только для грохотов)
            if (_cw != null && _cw.Def != null && _cw.Def.Capacity < _trowels * 2)
            {
                int was = _cw.Def.Capacity;
                _cw.Def.Capacity = _trowels * 2;
                if (_log) Debug.Log($"[Грохот] «{name}»: вместимость {was} → {_cw.Def.Capacity} порций ({_trowels} мастерков)", this);
            }
            if (_pile == null) _pile = transform.Find("Pile");
            if (_mesh == null) _mesh = transform.Find("Mesh");
            if (_pile != null) { _pileScale = _pile.localScale; _pileR = _pile.GetComponent<Renderer>(); }
            if (_mesh != null) _meshR = _mesh.GetComponent<Renderer>();
        }

        /// <summary>v23.53: радиус сетки (м) — по модели сетки, иначе по грохоту.</summary>
        private float MeshRadius()
        {
            if (_meshR != null) { Bounds b = _meshR.bounds; return Mathf.Max(0.03f, Mathf.Min(b.extents.x, b.extents.z) * 0.85f); }
            Renderer r = GetComponentInChildren<Renderer>();
            if (r != null) { Bounds b = r.bounds; return Mathf.Max(0.03f, Mathf.Min(b.extents.x, b.extents.z) * 0.7f); }
            return 0.25f;
        }

        /// <summary>v23.53: пока трясут — сквозь сетку сыплется завеса мультяшных тучек, мука подпрыгивает на сетке. Только вид.</summary>
        private void SiftFx()
        {
            if (Application.isBatchMode || !IsShaking || _cw == null || _cw.IsEmpty) { _curtainAcc = 0f; _topAcc = 0f; return; }
            Vector3 from = _mesh != null ? _mesh.position : transform.position;
            if (Time.time >= _belowAt)
            {
                _belowAt = Time.time + 0.2f;
                Cookware c = FindBelow(out _belowPoint);
                _belowHasCw = c != null;
                if (c != null) _belowPoint = c.MouthPoint;
            }
            float rad = MeshRadius();
            float size = Mathf.Clamp(rad * 2f, 0.08f, 1f);
            Color col = TopColor();
            _curtainAcc += Time.deltaTime * 55f;
            int n = Mathf.Min(8, (int)_curtainAcc);
            _curtainAcc -= n;
            for (int i = 0; i < n; i++) CCPowderFx.SiftCurtain(from, _belowPoint, rad, col, size);
            _topAcc += Time.deltaTime * 9f;
            int m = Mathf.Min(3, (int)_topAcc);
            _topAcc -= m;
            for (int i = 0; i < m; i++) CCPowderFx.SiftTop(from + Vector3.up * size * 0.05f, rad, col, size);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            Resolve();
            _cw.ContentsChanged += RefreshPile;
            RefreshPile();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_cw != null) _cw.ContentsChanged -= RefreshPile;
        }

        private void Resolve()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            if (_almond == null) _almond = MacaronTitles.Find(db, CookRecipeBook.Macaron.AlmondFlour);
            if (_sugar == null) _sugar = MacaronTitles.Find(db, "Сахар");
            if (_mix == null) _mix = MacaronTitles.Find(db, CookRecipeBook.Macaron.AlmondMix);
        }

        // ───────────────────────── ВЛАДЕЛЕЦ: ТРЯСКА ─────────────────────────

        private void Update()
        {
            if (!base.IsOwner || !_look.Held)
            {
                if (_lmb) { _lmb = false; _acc = 0f; if (base.IsOwner) CmdSetShaking(false); }
                return;
            }
            bool lmb = Input.GetMouseButton(0);
            if (lmb != _lmb) SetShaking(lmb);
            if (!lmb) return;
            float x = Input.GetAxis("Mouse X");
            float step = Mathf.Abs(x) + Mathf.Abs(Input.GetAxis("Mouse Y")) * 0.3f;
            // смена направления — тряска «засчитывается» сильнее (тряс, а не вёл)
            if (Mathf.Sign(x) != Mathf.Sign(_lastX) && Mathf.Abs(x) > 0.05f) step *= 1.6f;
            if (Mathf.Abs(x) > 0.02f) _lastX = x;
            _acc += Mathf.Min(step, 3f);
            _phase += step * 2.5f;
            if (_acc >= _shakePerPortion && Time.time >= _nextCmd)
            {
                _acc -= _shakePerPortion;
                _nextCmd = Time.time + _minInterval;
                CmdShake();
            }
        }

        private void SetShaking(bool on)
        {
            _lmb = on;
            _acc = 0f;
            CmdSetShaking(on);
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdSetShaking(bool on) { if (_shaking.Value != on) _shaking.Value = on; }

        private void LateUpdate()
        {
            _look.Late(base.IsOwner && _lmb, base.IsOwner && _lmb);
            SiftFx();                                                            // v23.53
            PlaceHeap();                                                         // v23.54
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            _look.Pose(handAnchor);
            base.UpdateHeldPose(handAnchor);
            if (base.IsOwner && _lmb)
            {
                // трясётся в руке: влево-вправо и чуть вверх-вниз (все видят через NetworkTransform)
                Vector3 side = handAnchor.right * Mathf.Sin(_phase) * _shakeAmp + Vector3.up * Mathf.Abs(Mathf.Cos(_phase)) * _shakeAmp * 0.3f;
                transform.position += side;
            }
        }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            _shaking.Value = false;
            _look.Release();
            base.ServerOnDrop(hand, tossDirection, force);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        [ServerRpc(RequireOwnership = true)]
        private void CmdShake()
        {
            if (Time.time < _serverNext) return;
            _serverNext = Time.time + _minInterval * 0.8f;
            Resolve();
            if (_cw.IsEmpty) { Hint("грохот пустой — насыпь мастерком муку миндальную и сахар"); return; }
            Cookware below = FindBelow(out Vector3 at);
            Vector3 from = _mesh != null ? _mesh.position : transform.position;
            if (below == null)
            {
                Hint("под грохотом нет тары — держи над ведром");
                RpcDust(from, at, TopColor(), false);
                return;
            }
            int passed;
            if (_almond != null && _sugar != null && _mix != null && _cw.CountOf(_almond.Id) > 0 && _cw.CountOf(_sugar.Id) > 0)
            {
                _cw.ServerTake(_almond.Id, 1);
                _cw.ServerTake(_sugar.Id, 1);
                passed = below.ServerAddContent(_mix.Id, _mix.StartState, 2, 0);
            }
            else
            {
                int took = _cw.ServerTakeTop(1, out ushort p, out FoodState st, out byte cook);
                if (took <= 0) return;
                passed = below.ServerAddContent(p, st, took, cook);
            }
            _sifted += passed;
            RpcDust(from, below.MouthPoint, TopColor(), true);
            if (_log && (_sifted % 4 == 0 || _cw.IsEmpty))
                Debug.Log($"[Грохот] «{name}»: просеяно {_sifted} порц. в «{(below.Def != null ? below.Def.Title : below.name)}»" +
                          (_cw.IsEmpty ? " — грохот пуст" : $", на сетке ещё {_cw.TotalCount}"), this);
            if (_cw.IsEmpty) _sifted = 0;
        }

        /// <summary>Тара под грохотом: луч вниз (через триггеры полостей), свои коллайдеры — мимо.</summary>
        private Cookware FindBelow(out Vector3 at)
        {
            Vector3 o = (_mesh != null ? _mesh.position : transform.position);
            float reach = _dropReach * Mathf.Max(1f, transform.lossyScale.y);
            at = o + Vector3.down * reach;
            int n = Physics.RaycastNonAlloc(o, Vector3.down, _hits, reach, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Cookware found = null;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                Cookware c = h.collider.GetComponentInParent<Cookware>();
                if (c == null || c == _cw) { if (!h.collider.isTrigger) { best = h.distance; at = h.point; found = null; } continue; }
                best = h.distance; at = h.point; found = c;
            }
            return found;
        }

        private Color TopColor()
        {
            FoodDatabase db = FoodDatabase.Instance;
            for (int i = _cw.ViewCount - 1; i >= 0; i--)
            {
                BagView v = _cw.GetView(i);
                if (v.Count <= 0) continue;
                ProductDef d = db != null ? db.Get(v.Product) : null;
                return d != null ? d.BaseColor : new Color(0.95f, 0.9f, 0.8f);
            }
            return new Color(0.95f, 0.9f, 0.8f);
        }

        [ObserversRpc]
        private void RpcDust(Vector3 from, Vector3 to, Color color, bool intoCookware)
        {
            // v23.53: порция долетела — «пых» мультяшными тучками из тары (или облачко на полу, если трясёшь впустую)
            float size = Mathf.Clamp(MeshRadius() * 2f, 0.08f, 1f);
            CCPowderFx.SiftLand(to, color, intoCookware ? size : size * 0.7f);
        }

        private void Hint(string text)
        {
            if (!_log || Time.time < _hintAt) return;
            _hintAt = Time.time + 4f;
            Debug.Log($"[Грохот] «{name}»: {text}", this);
        }

        // ───────────────────────── ВИД: горка на сетке ─────────────────────────

        // v23.54: ОБЪЁМНАЯ КУЧКА на сетке — тем же кодом, что кучка на мастерке (FoodSubstanceMesh), а не плоский купол.
        private Transform _heapT;
        private MeshFilter _heapMf;
        private MeshRenderer _heapMr;
        private Mesh _heapMesh;
        private int _heapKey = -1;
        private float _heapBounce;

        /// <summary>Свои «рисовальщики» содержимого у грохота (жидкость/кучка тары) — выключить: кучку рисует грохот.</summary>
        private void MuteCookwareVisuals()
        {
            foreach (CookwareLiquidVisual lv in GetComponentsInChildren<CookwareLiquidVisual>(true))
            {
                if (lv.enabled) lv.enabled = false;
                for (int i = 0; i < lv.transform.childCount; i++)
                {
                    Transform c = lv.transform.GetChild(i);
                    if (c != _heapT && c.name.EndsWith("(auto)") && c.gameObject.activeSelf) c.gameObject.SetActive(false);
                }
            }
            foreach (CookwareHeapVisual hv in GetComponentsInChildren<CookwareHeapVisual>(true))
            {
                if (hv.enabled) hv.enabled = false;
                FoodHeapRenderer fh = hv.GetComponent<FoodHeapRenderer>();
                if (fh != null && fh.enabled) fh.enabled = false;
                if (hv.gameObject != gameObject) foreach (Renderer r in hv.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
        }

        /// <summary>v23.54: построить кучку по содержимому. false — вещество не нашлось (тогда старый купол Pile).</summary>
        private bool RefreshHeap()
        {
            MuteCookwareVisuals();
            FoodDatabase db = FoodDatabase.Instance;
            int total = _cw.TotalCount;
            BagView top = default(BagView);
            bool has = false;
            for (int i = _cw.ViewCount - 1; i >= 0; i--) { BagView v = _cw.GetView(i); if (v.Count > 0) { top = v; has = true; break; } }
            if (!has || total <= 0 || db == null)
            {
                if (_heapT != null) _heapT.gameObject.SetActive(false);
                _heapKey = -1;
                return true;
            }
            ProductDef def = db.Get(top.Product);
            FoodSubstance sub = def != null ? FoodSubstanceTable.Resolve(def, top.State) : null;
            if (sub == null) return false;
            int key = total * 4096 + top.Product * 8 + (int)top.State % 8;
            if (key == _heapKey && _heapT != null && _heapT.gameObject.activeSelf) return true;
            _heapKey = key;
            if (_heapT == null)
            {
                var go = new GameObject("SifterHeap (auto)");
                go.layer = gameObject.layer;
                _heapT = go.transform;
                _heapT.SetParent(transform, false);
                _heapMf = go.AddComponent<MeshFilter>();
                _heapMr = go.AddComponent<MeshRenderer>();
                _heapMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _heapMesh = new Mesh { name = "SifterHeap (auto)", hideFlags = HideFlags.DontSave };
                _heapMesh.MarkDynamic();
                _heapMf.sharedMesh = _heapMesh;
            }
            _heapT.gameObject.SetActive(true);
            // размер: от сетки; чем больше насыпано — тем шире и выше (как горка с мастерка, объёмная)
            float k = _cw.Capacity > 0 ? Mathf.Clamp01(total / (float)_cw.Capacity) : 0.5f;
            float rad = MeshRadius();
            var d = new FoodSubstanceMesh.Dims();
            d.Rx = d.Rz = rad * Mathf.Lerp(0.32f, 0.8f, Mathf.Sqrt(k));
            d.H = d.Rx * Mathf.Lerp(0.55f, 0.8f, k) * Mathf.Max(0.6f, sub.FloorSlope * sub.BladeHeight);
            d.Edge = 0f;
            d.RectN = 2f;
            Color col = sub.ColorFor(FoodColors.Of(def, top.State));
            int seed = (base.NetworkObject != null && base.NetworkObject.IsSpawned ? base.NetworkObject.ObjectId * 17 : 7) + total;
            if (sub.Look == SubstanceLook.Crystals) FoodSubstanceMesh.Crystals(_heapMesh, d, sub.CrystalSize, sub.CrystalDensity, col, seed, sub.Salt);
            else FoodSubstanceMesh.Powder(_heapMesh, d, sub.Wonky, col, seed, sub.Sugar);
            Material m = sub.GetMaterial();
            if (m != null) _heapMr.sharedMaterial = m;
            PlaceHeap();
            return true;
        }

        /// <summary>Кучка — на сетке, в метрах мира (грохот бывает отмасштабирован), чуть подпрыгивает, когда трясут.</summary>
        private void PlaceHeap()
        {
            if (_heapT == null || !_heapT.gameObject.activeSelf) return;
            Vector3 c = _meshR != null ? _meshR.bounds.center : (_mesh != null ? _mesh.position : transform.position);
            _heapBounce = IsShaking ? Mathf.Abs(Mathf.Sin(Time.time * 22f)) : Mathf.MoveTowards(_heapBounce, 0f, Time.deltaTime * 4f);
            float rad = MeshRadius();
            _heapT.position = c + transform.up * (rad * 0.03f * _heapBounce);
            _heapT.rotation = transform.rotation;
            Vector3 ls = transform.lossyScale;
            float sq = 1f - 0.08f * _heapBounce;                                 // трясут — кучка приседает
            _heapT.localScale = new Vector3(1f / Mathf.Max(1e-5f, Mathf.Abs(ls.x)) / Mathf.Sqrt(sq), sq / Mathf.Max(1e-5f, Mathf.Abs(ls.y)), 1f / Mathf.Max(1e-5f, Mathf.Abs(ls.z)) / Mathf.Sqrt(sq));
        }

        private void RefreshPile()
        {
            if (RefreshHeap())                                                  // v23.54: объёмная кучка вместо купола
            {
                if (_pile != null && _pile.gameObject.activeSelf) _pile.gameObject.SetActive(false);
                return;
            }
            if (_pile == null) return;
            float k = _cw.Capacity > 0 ? Mathf.Clamp01(_cw.TotalCount / (float)_cw.Capacity) : 0f;
            _pile.gameObject.SetActive(k > 0.001f);
            if (k <= 0.001f) return;
            float r = Mathf.Lerp(0.45f, 1f, Mathf.Sqrt(k));
            _pile.localScale = new Vector3(_pileScale.x * r, _pileScale.y * Mathf.Lerp(0.25f, 1f, k), _pileScale.z * r);
            if (_pileR == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _pileR.GetPropertyBlock(_mpb);
            Color c = TopColor(); c.a = 1f;
            _mpb.SetColor("_BaseColor", c);
            _pileR.SetPropertyBlock(_mpb);
        }

        [ContextMenu("Тест: насыпать муку миндальную и сахар (Play, хост)")]
        private void TestFill()
        {
            if (!NetTest.ServerReady(this, "Грохот")) return;
            Resolve();
            if (_almond != null) _cw.ServerAddPinch(_almond.Id, _almond.StartState, 8, 0);
            if (_sugar != null) _cw.ServerAddPinch(_sugar.Id, _sugar.StartState, 8, 0);
        }
    }

    /// <summary>Поиск продукта по точному названию в базе.</summary>
    public static class MacaronTitles
    {
        public static ProductDef Find(FoodDatabase db, string title)
        {
            if (db == null || db.Table == null) return null;
            string want = title.Trim().ToLowerInvariant().Replace('ё', 'е');
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Table.GetProduct((ushort)id);
                if (p != null && !string.IsNullOrEmpty(p.Title) && p.Title.Trim().ToLowerInvariant().Replace('ё', 'е') == want) return p;
            }
            return null;
        }
    }
}
