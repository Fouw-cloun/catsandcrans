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

        public bool IsShaking => base.IsOwner ? _lmb : _shaking.Value;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            _cw = GetComponent<Cookware>();
            if (_pile == null) _pile = transform.Find("Pile");
            if (_mesh == null) _mesh = transform.Find("Mesh");
            if (_pile != null) { _pileScale = _pile.localScale; _pileR = _pile.GetComponent<Renderer>(); }
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
            Color c2 = Color.Lerp(color, Color.white, 0.4f);
            Vector3 d = to - from;
            float len = d.magnitude;
            Vector3 dir = len > 1e-4f ? d / len : Vector3.down;
            // сыплется струйкой пыли сквозь сетку
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = from + dir * (len * (i + 0.5f) / 3f) + Random.insideUnitSphere * 0.04f;
                CCRuntimeParticles.Burst(p, Vector3.down, color, c2, 3, CCRuntimeParticles.Look.Dust.Scaled(0.25f));
            }
            if (intoCookware) CCRuntimeParticles.Burst(to, Vector3.up, color, c2, 4, CCRuntimeParticles.Look.Dust.Scaled(0.35f));
        }

        private void Hint(string text)
        {
            if (!_log || Time.time < _hintAt) return;
            _hintAt = Time.time + 4f;
            Debug.Log($"[Грохот] «{name}»: {text}", this);
        }

        // ───────────────────────── ВИД: горка на сетке ─────────────────────────

        private void RefreshPile()
        {
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
