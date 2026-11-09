using FishNet;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КАСТРЮЛЯ-СУП: целые куски (кости, голяшка, луковицы, морковь, картошка) НЕ растворяются в таре, а плавают собой.
    ///   • в воде/бульоне всплывают и тонут по-настоящему (физика): тяжёлое (кости, мясо) — у дна, овощи — у поверхности;
    ///   • медленно кружат по кругу, при кипении — быстрее и подпрыгивают, покачиваются;
    ///   • варятся (кипит — идёт «Варить»): темнеют до «Варёного», передержал — «Переготовлено»;
    ///   • их можно достать руками и использовать дальше (варёная голяшка, варёные овощи);
    ///   • рецепты (Бульон для борща) их считают, хоть они и не проглочены.
    /// Тёртое, жидкое, порошки — как обычно, в таре.
    ///
    /// Вешается на корень кастрюли (меню Борщ → 11). Без жидкости куски просто лежат на дне.
    /// Физику ведёт сервер, позиции у всех — через Network Transform кусков.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CookwareSoupFloat : MonoBehaviour
    {
        [Tooltip("Всплывание овощей: больше 1 — плавают у поверхности")]
        [Range(0.5f, 3f)][SerializeField] private float _vegBuoyancy = 1.35f;
        [Tooltip("Всплывание мяса и костей: меньше 1 — у дна, но покачиваются")]
        [Range(0.2f, 1.5f)][SerializeField] private float _meatBuoyancy = 0.9f;
        [Tooltip("Сопротивление воды (гасит скорость в жидкости)")]
        [Range(0f, 10f)][SerializeField] private float _drag = 2.5f;
        [Tooltip("Кружение по кругу, м/с² — в покое и при кипении")]
        [SerializeField] private Vector2 _swirl = new Vector2(0.25f, 1.4f);
        [Tooltip("Подпрыгивание при кипении (толчки вверх), м/с²")]
        [Range(0f, 10f)][SerializeField] private float _boilKick = 2.5f;

        private Cookware _cw;
        private CookwareLiquidVisual _lv;
        private readonly Collider[] _hits = new Collider[128];
        private readonly System.Collections.Generic.List<FoodPiece> _tmp = new System.Collections.Generic.List<FoodPiece>();
        private float _scanAt;

        /// <summary>
        /// Объём кастрюли: полость (Inside Volume) и над ней ещё полвысоты — всплывший кусок всё равно «в кастрюле».
        /// Кусок внутри — считается и плавает, вне зависимости от того, когда его кинули (до воды или после).
        /// </summary>
        private bool Region(out Bounds b)
        {
            b = new Bounds();
            Collider iv = _cw != null ? _cw.InsideVolume : null;
            if (iv == null) return false;
            b = iv.bounds;
            float h = b.size.y;
            b.SetMinMax(new Vector3(b.min.x - 0.03f, b.min.y - h * 0.15f, b.min.z - 0.03f), new Vector3(b.max.x + 0.03f, b.max.y + h * 0.6f, b.max.z + 0.03f));
            return true;
        }

        public bool Inside(FoodPiece p)
        {
            if (p == null || !Region(out Bounds b)) return false;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            return b.Contains(rb != null ? rb.worldCenterOfMass : p.transform.position);
        }

        /// <summary>Раз в 0.25 с: всё целое внутри кастрюли — в «куски»; что вынесли наружу — убрать.</summary>
        private void Scan()
        {
            if (!Region(out Bounds b)) return;
            int n = Physics.OverlapBoxNonAlloc(b.center, b.extents, _hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i];
                if (c == null) continue;
                FoodPiece p = c.attachedRigidbody != null ? c.attachedRigidbody.GetComponent<FoodPiece>() : c.GetComponentInParent<FoodPiece>();
                if (p == null || p.IsHeld || p.IsConsumed || !KeepsWhole(p) || !Inside(p)) continue;
                _cw.ServerAdoptLoose(p);
            }
            _tmp.Clear();
            foreach (FoodPiece p in _cw.Loose) if (p != null && !p.IsHeld && !Inside(p)) _tmp.Add(p);
            foreach (FoodPiece p in _tmp) _cw.ServerDropLoose(p);
        }

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _lv = GetComponentInChildren<CookwareLiquidVisual>(true);
        }

        /// <summary>Этот кусок плавает собой (целый, не тёртый, не жидкий)?</summary>
        public bool KeepsWhole(FoodPiece p)
        {
            if (p == null) return false;
            FoodVisual v = FoodStateInfo.VisualOf(p.State);
            if (v == FoodVisual.Liquid || v == FoodVisual.Pile || v == FoodVisual.Mass || v == FoodVisual.Hidden) return false;
            return p.GetComponent<Rigidbody>() != null;
        }

        private void FixedUpdate()
        {
            if (_cw == null || !InstanceFinder.IsServerStarted) return;
            if (Time.time >= _scanAt) { _scanAt = Time.time + 0.25f; Scan(); }
            if (_lv == null) _lv = GetComponentInChildren<CookwareLiquidVisual>(true);
            if (_lv == null || !_lv.HasLiquid) return;
            Transform a = _lv.transform;
            float surface = a.position.y + Mathf.Max(0f, _lv.LevelFraction) * _lv.RimHeight;
            Vector3 centre = a.position;
            float boil = Mathf.Clamp01(_cw.Agitation01 * 2.5f);
            float swirl = Mathf.Lerp(_swirl.x, _swirl.y, boil);
            float dt = Time.fixedDeltaTime;
            _tmp.Clear();
            foreach (FoodPiece q in _cw.Loose) _tmp.Add(q);
            foreach (FoodPiece p in _tmp)
            {
                if (p == null || p.IsHeld || p.IsConsumed) continue;
                Rigidbody rb = p.GetComponent<Rigidbody>();
                if (rb == null || rb.isKinematic) continue;
                Collider col = p.GetComponentInChildren<Collider>();
                float half = col != null ? Mathf.Max(0.02f, col.bounds.extents.y) : 0.1f;
                Vector3 pos = rb.worldCenterOfMass;
                float sub = Mathf.Clamp01((surface - pos.y + half) / (2f * half));
                if (sub <= 0f) continue;
                bool heavy = IsHeavy(p.Def);
                float buoy = heavy ? _meatBuoyancy : _vegBuoyancy;
                float m = rb.mass;
                rb.AddForce(-Physics.gravity * m * buoy * sub);
                rb.AddForce(-rb.linearVelocity * m * _drag * sub);
                rb.AddTorque(-rb.angularVelocity * m * _drag * 0.3f * sub);
                Vector3 flat = new Vector3(pos.x - centre.x, 0f, pos.z - centre.z);
                if (flat.sqrMagnitude > 1e-4f)
                {
                    Vector3 tangent = Vector3.Cross(Vector3.up, flat.normalized);
                    rb.AddForce(tangent * m * swirl * sub);
                }
                else rb.AddForce(Random.insideUnitSphere.normalized * m * 0.3f);
                if (boil > 0.05f && Random.value < dt * 3f)
                    rb.AddForce(Vector3.up * m * _boilKick * boil * Random.Range(0.5f, 1.5f) * sub / Mathf.Max(0.02f, dt) * 0.05f, ForceMode.Force);
                rb.AddTorque(Random.insideUnitSphere * m * (0.15f + boil * 0.5f) * sub);
                rb.WakeUp();
            }
        }

        private static bool IsHeavy(ProductDef d)
        {
            if (d == null || string.IsNullOrEmpty(d.Title)) return false;
            string t = d.Title.ToLowerInvariant();
            if (t.Contains("кост") || t.Contains("голяш") || t.Contains("мяс") || t.Contains("говяд") || t.Contains("свин") || t.Contains("рёбр") || t.Contains("ребр") || t.Contains("сало")) return true;
            return d.Group != null && d.Group.name.Contains("Meat");
        }
    }
}
