using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТАРЕЛКА НЬОККОВ — блюдо собирается прямо на тарелке, одним объектом.
    ///
    /// ═══ ПОДАЧА ═══
    ///   1. Ньокки: лопатой из кастрюли (всплывшие) → ПКМ в тарелку. Ложатся спиралью.
    ///   2. Соус «Масло с шалфеем» (своё масло + пучок шалфея, в ковше на огне): лопатой/мастерком из ковша → ПКМ в тарелку. Растекается лужицей, сверху — листья шалфея.
    ///   3. Сыр тёртый: сектор головки — в измельчитель, тёртое — лопатой → ПКМ в тарелку. Ложится стружкой и подтаивает на горячем.
    ///   Всё есть — тарелка ставит ЗВЁЗДЫ (1–5): сколько ньокков, как сварены, соус (горелое — минус), сыр.
    ///   Нести тарелку — F.
    ///
    /// Всё, что бросили в тарелку, — обычное содержимое Cookware (по сети как у любой тары). Этот компонент только рисует и считает.
    /// ═══ НАСТРОЙКА ═══ CatCooks → Ньокки → 11: выдели свою тарелку (где делали Аляску) — компонент добавится к ней.
    /// С Dessert Plate уживается: подсказка ньокков — только когда в тарелке ньокки/соус/сыр.
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class GnocchiPlate : NetworkBehaviour
    {
        [Tooltip("Центр на поверхности тарелки, Y — вверх")]
        [SerializeField] private Transform _anchor;
        [Tooltip("Радиус тарелки, м")]
        [SerializeField] private float _radius = 0.8f;

        [Header("Сколько нужно на полную порцию")]
        [Min(1)][SerializeField] private int _gnocchiFull = 8;
        [Min(1)][SerializeField] private int _sauceFull = 2;
        [Min(1)][SerializeField] private int _cheeseFull = 2;
        [Tooltip("За сколько секунд сыр подтаивает на горячих ньокках")]
        [SerializeField] private float _meltSeconds = 6f;

        [Header("Звёзды")]
        [SerializeField] private bool _stars = true;
        [Range(5, 80)][SerializeField] private int _starCount = 30;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _starsNet = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _scoreNet = new SyncVar<byte>(0);

        private Cookware _cw;
        private int _g, _over, _raw, _sauce, _burnt, _butter, _cheese;
        private float _avgCook;
        private float _cheeseSince = -1f;
        private int _lastCheese;
        private float _stableAt, _evalAt; private bool _dirty;
        private readonly List<Transform> _gnocchi = new List<Transform>(), _leaves = new List<Transform>(), _shreds = new List<Transform>(),
                                          _crumbs = new List<Transform>(), _flecks = new List<Transform>(), _beads = new List<Transform>();
        private readonly List<Vector3> _gPos = new List<Vector3>();       // ньокки: центр (в метрах от якоря)
        private readonly List<float> _gYaw = new List<float>();
        private readonly List<Vector3> _shredBase = new List<Vector3>();   // стружка: базовый масштаб (без таяния)
        private Transform _root, _puddle;
        private Material _mGnocchi, _mRaw, _mOver, _mSauce, _mLeaf, _mCheese, _mFleck, _mBead;
        private Vector3 _dims;
        private float _poolTop;
        private float _lookAt; private bool _looking;
        private bool _dessert;            // на той же тарелке — Аляска (Dessert Plate)
        private static GUIStyle _st;

        public int Stars => _starsNet.Value;

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _dessert = GetComponent<DessertPlate>() != null;
            if (_anchor == null) _anchor = transform;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            if (_cw != null) _cw.ContentsChanged += Recount;
            _starsNet.OnChange += (a, b, s) => { if (b > a && b > 0 && _stars) CelebrationStars.Burst(_anchor.position + Vector3.up * 0.3f, Mathf.Max(0.5f, _radius), CelebrationStars.Gold, _starCount * b / 5); };
            Recount();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_cw != null) _cw.ContentsChanged -= Recount;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
            foreach (Material m in new[] { _mGnocchi, _mRaw, _mOver, _mSauce, _mLeaf, _mCheese, _mFleck, _mBead }) if (m != null) Destroy(m);
        }

        // ───────────────────────── что в тарелке ─────────────────────────

        private void Recount()
        {
            if (_cw == null) return;
            FoodDatabase db = FoodDatabase.Instance;
            ushort gid = GnocchiProduct.Id;
            _g = _over = _raw = _sauce = _burnt = _butter = _cheese = 0;
            float cookSum = 0f;
            for (int i = 0; i < _cw.ViewCount; i++)
            {
                BagView v = _cw.GetView(i);
                if (v.Count == 0) continue;
                if (gid != 0 && v.Product == gid)
                {
                    if (v.State == GnocchiProduct.Done) { _g += v.Count; cookSum += v.Cook * v.Count; }
                    else if (v.State == GnocchiProduct.Over) _over += v.Count; else _raw += v.Count;
                    continue;
                }
                ProductDef d = db != null ? db.Get(v.Product) : null;
                string t = d != null && d.Title != null ? d.Title : "";
                if (t == CookRecipeBook.Gnocchi.SageButter) _sauce += v.Count;
                else if (t == CookRecipeBook.Gnocchi.BurntButter) _burnt += v.Count;
                else if (t == CookRecipeBook.Gnocchi.Butter) _butter += v.Count;
                else if (t == CookRecipeBook.Gnocchi.Cheese) _cheese += v.Count;
            }
            _avgCook = _g > 0 ? cookSum / _g / 100f : 0f;
            if (_cheese > _lastCheese) _cheeseSince = Time.time;     // свежий сыр — заново подтаивает
            if (_cheese == 0) _cheeseSince = -1f;
            _lastCheese = _cheese;
            _dirty = true; _stableAt = Time.time + 1.5f;
            BuildLook();
        }

        // ───────────────────────── оценка (сервер) ─────────────────────────

        private void Update()
        {
            if (!base.IsServerInitialized) return;
            float melt = Melt01;
            if (melt > 0f && melt < 1f && Time.time >= _evalAt && _starsNet.Value > 0) { _evalAt = Time.time + 1f; _dirty = true; }   // сыр тает — оценка растёт
            if (!_dirty || Time.time < _stableAt) return;
            _dirty = false;
            Evaluate(out int stars, out float score, out string why);
            if (stars != _starsNet.Value) _starsNet.Value = (byte)stars;
            _scoreNet.Value = (byte)Mathf.RoundToInt(score * 100f);
            if (_log && stars > 0) Debug.Log($"[Ньокки] тарелка «{name}»: {new string('★', stars)}{new string('☆', 5 - stars)} ({score * 100f:0}%) — {why}", this);
        }

        private float Melt01 => _cheeseSince < 0f ? 0f : Mathf.Clamp01((Time.time - _cheeseSince) / Mathf.Max(0.5f, _meltSeconds));

        /// <summary>Звёзды: 0 — блюдо не собрано (нет ньокков, соуса или сыра).</summary>
        private void Evaluate(out int stars, out float score, out string why)
        {
            int all = _g + _over + _raw;
            int sauceAll = _sauce + _burnt + _butter;
            if (all == 0 || sauceAll == 0 || _cheese == 0)
            {
                stars = 0; score = 0f;
                why = all == 0 ? "нет ньокков" : sauceAll == 0 ? "нет соуса" : "нет сыра";
                return;
            }
            float portion = Mathf.Clamp01(all / (float)_gnocchiFull);
            float cooked = all > 0 ? _avgCook * _g / all : 0f;                                   // разваренные и сырые — тянут вниз
            float sauce = Mathf.Clamp01(sauceAll / (float)_sauceFull) * (_burnt > 0 ? 0.2f : _sauce > 0 ? 1f : 0.5f);
            float cheese = Mathf.Clamp01(_cheese / (float)_cheeseFull) * (0.8f + 0.2f * Melt01);
            score = Mathf.Clamp01(0.25f * portion + 0.35f * cooked + 0.25f * sauce + 0.15f * cheese);
            stars = Mathf.Clamp(1 + Mathf.FloorToInt(score * 4.999f), 1, 5);
            why = $"ньокки {all}/{_gnocchiFull} (сварены {_avgCook * 100f:0}%" + (_over > 0 ? $", разварились {_over}" : "") + (_raw > 0 ? $", сырые {_raw}" : "") + ")" +
                  $", соус {(_burnt > 0 ? "ГОРЕЛЫЙ" : _sauce > 0 ? "масло с шалфеем" : "просто масло")} {sauceAll}/{_sauceFull}, сыр {_cheese}/{_cheeseFull}";
        }

        // ───────────────────────── вид ─────────────────────────
        //  Всё в метрах: корень «GnocchiDish (auto)» под якорем гасит масштаб тарелки.
        //  Ньокки — венком по кругу (вдоль окружности), лишние — в серединку горкой; под ними — лужица орехового масла
        //  с коричневыми крупинками; сверху — обжаренные листья шалфея и капли масла; поверх всего — стружка пармезана
        //  и крошка, которые подтаивают (оседают, блестят, желтеют).

        private void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("GnocchiDish (auto)").transform;
            _root.SetParent(_anchor, false);
            Vector3 ps = _anchor.lossyScale;
            _root.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(ps.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(ps.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(ps.z)));
        }

        private void BuildLook()
        {
            if (_anchor == null) return;
            EnsureRoot();
            EnsureMats();
            float r = _radius * 0.74f;
            _dims = GnocchiMesh.Dims;
            if (_dims.z * 2.9f > r * 2f) _dims *= r * 2f / (_dims.z * 2.9f);                     // маленькая тарелка — ньокки поменьше
            Vector3 d = _dims;
            int all = _g + _over + _raw;
            int sauceAll = _sauce + _burnt + _butter;
            int seed = base.IsSpawned ? base.ObjectId * 31 + 7 : GetInstanceID();

            // ── лужица соуса ──
            float poolH = sauceAll > 0 ? Mathf.Lerp(0.006f, 0.014f, Mathf.Clamp01(sauceAll / (float)_sauceFull)) : 0f;
            _poolTop = 0.004f + poolH;
            if (_puddle == null) _puddle = MakeChild("SaucePool", GnocchiLooks.Pool(seed), _mSauce);
            _puddle.gameObject.SetActive(sauceAll > 0);
            if (sauceAll > 0)
            {
                float k = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(sauceAll / (float)_sauceFull));
                _puddle.localPosition = new Vector3(0f, 0.003f, 0f);
                _puddle.localRotation = Quaternion.Euler(0f, seed % 360, 0f);
                _puddle.localScale = new Vector3(r * 2.1f * k, poolH, r * 1.95f * k);
                Color c = _burnt > 0 ? GnocchiLooks.ButterBurnt : _sauce > 0 ? GnocchiLooks.Butter : new Color(0.99f, 0.88f, 0.5f);
                GnocchiLooks.SetColor(_mSauce, c);
            }
            // ньокки в масле — золотистые и блестят
            Color gc = sauceAll > 0 ? Color.Lerp(new Color(1f, 0.9f, 0.62f), new Color(0.98f, 0.76f, 0.38f), _sauce > 0 ? 0.45f : 0.2f) : new Color(1f, 0.91f, 0.63f);
            GnocchiLooks.SetColor(_mGnocchi, gc);
            GnocchiLooks.SetSmooth(_mGnocchi, sauceAll > 0 ? 0.78f : 0.5f);

            // ── ньокки венком ──
            int n = Mathf.Min(18, all);
            Layout(n, r, d, seed);
            Sync(_gnocchi, n, "Gnocco", GnocchiMesh.Get(1f), _mGnocchi);
            for (int i = 0; i < n; i++)
            {
                Transform t = _gnocchi[i];
                bool over = i >= _g && i < _g + _over, raw = i >= _g + _over;
                t.GetComponent<MeshRenderer>().sharedMaterial = over ? _mOver : raw ? _mRaw : _mGnocchi;
                t.localPosition = _gPos[i];
                float tilt = Mathf.Sin(i * 1.9f + seed) * 4f;
                t.localRotation = Quaternion.Euler(tilt, _gYaw[i], Mathf.Cos(i * 2.3f + seed) * 4f);
                t.localScale = over ? new Vector3(d.x * 1.25f, d.y * 0.62f, d.z * 1.08f) : raw ? d * 0.96f : d;
            }

            // ── коричневые крупинки в масле ──
            int nf = _sauce + _burnt > 0 ? Mathf.Clamp(8 + (_sauce + _burnt) * 4, 8, 26) : 0;
            Sync(_flecks, nf, "Fleck", GnocchiLooks.Crumb, _mFleck);
            for (int i = 0; i < nf; i++)
            {
                Vector2 p = Spiral(i, nf, r * 0.95f, 0.9f + seed * 0.01f);
                Transform t = _flecks[i];
                t.localPosition = new Vector3(p.x, _poolTop, p.y);
                t.localRotation = Quaternion.Euler(i * 37f, i * 71f, i * 13f);
                t.localScale = Vector3.one * d.x * 0.06f * (0.6f + 0.8f * Hash(i, 3));
            }
            // ── капли масла на ньокках ──
            int nb = n > 0 && sauceAll > 0 ? Mathf.Min(14, sauceAll * 3 + 2) : 0;
            Sync(_beads, nb, "ButterDrop", GnocchiLooks.Bead, _mBead);
            GnocchiLooks.SetColor(_mBead, _burnt > 0 ? GnocchiLooks.ButterBurnt : GnocchiLooks.Butter);
            for (int i = 0; i < nb; i++)
            {
                int gi = i % n;
                Vector3 c = _gPos[gi];
                Vector2 off = new Vector2(Hash(i, 5) - 0.5f, Hash(i, 6) - 0.5f) * d.x * 0.5f;
                Vector3 p = new Vector3(c.x + off.x, 0f, c.z + off.y);
                p.y = TopAt(p.x, p.z) - d.x * 0.01f;
                Transform t = _beads[i];
                t.localPosition = p;
                t.localRotation = Quaternion.identity;
                float sz = d.x * (0.12f + 0.1f * Hash(i, 7));
                t.localScale = new Vector3(sz, sz * 0.35f, sz * 0.8f);
            }
            // ── шалфей: обжаренные листья сверху ──
            int nl = _sauce + _burnt > 0 ? Mathf.Clamp(3 + _sauce + _burnt, 3, 8) : 0;
            Sync(_leaves, nl, "SageLeaf", GnocchiLooks.SageLeaf, _mLeaf);
            GnocchiLooks.SetColor(_mLeaf, _burnt > 0 ? GnocchiLooks.SageBurnt : GnocchiLooks.SageFried);
            for (int i = 0; i < nl; i++)
            {
                Vector2 p = n > 0 ? XZ(_gPos[(i * 3 + 1) % n]) + new Vector2(Hash(i, 8) - 0.5f, Hash(i, 9) - 0.5f) * d.z * 0.4f
                                  : Spiral(i, nl, r * 0.6f, 0.3f);
                Transform t = _leaves[i];
                t.localPosition = new Vector3(p.x, TopAt(p.x, p.y) + 0.004f, p.y);
                t.localRotation = Quaternion.Euler((Hash(i, 10) - 0.5f) * 30f, i * 97f + seed, (Hash(i, 11) - 0.5f) * 24f);
                t.localScale = Vector3.one * d.z * (0.7f + 0.25f * Hash(i, 12));
            }
            // ── пармезан: стружка и крошка поверх всего ──
            int ns = Mathf.Min(34, _cheese * 13);
            int nc = Mathf.Min(48, _cheese * 20);
            while (_shreds.Count < ns) { int v = _shreds.Count; _shreds.Add(MakeChild("CheeseShaving", GnocchiLooks.Shaving(v), _mCheese)); }
            for (int i = 0; i < _shreds.Count; i++) _shreds[i].gameObject.SetActive(i < ns);
            _shredBase.Clear();
            for (int i = 0; i < ns; i++)
            {
                Vector2 p = Spiral(i, ns, r * 0.85f, 0.2f + seed * 0.013f);
                Transform t = _shreds[i];
                float L = d.z * (0.38f + 0.22f * Hash(i, 13));
                t.localPosition = new Vector3(p.x, TopAt(p.x, p.y) + 0.002f, p.y);
                t.localRotation = Quaternion.Euler((Hash(i, 14) - 0.5f) * 40f, i * 53f + seed, (Hash(i, 15) - 0.5f) * 30f);
                _shredBase.Add(Vector3.one * L);
            }
            Sync(_crumbs, nc, "CheeseCrumb", GnocchiLooks.Crumb, _mCheese);
            for (int i = 0; i < nc; i++)
            {
                Vector2 p = Spiral(i, nc, r * 0.9f, 1.3f + seed * 0.007f);
                Transform t = _crumbs[i];
                t.localPosition = new Vector3(p.x, TopAt(p.x, p.y) + 0.001f, p.y);
                t.localRotation = Quaternion.Euler(i * 41f, i * 67f, i * 19f);
                t.localScale = Vector3.one * d.x * 0.07f * (0.6f + 0.8f * Hash(i, 16));
            }
            if (_log && all + sauceAll + _cheese > 0)
                Debug.Log($"[Ньокки] тарелка «{name}»: вид — ньокков {n}, соус {sauceAll} (листьев {nl}), сыр {_cheese} (стружек {ns}, крошек {nc})", this);
        }

        /// <summary>
        /// Раскладка ньокков: венок вдоль окружности (длиной по касательной), сколько влезает в один круг;
        /// остальные — в серединку «цветком» (длиной к центру), если не помещаются — вторым слоем сверху.
        /// </summary>
        private void Layout(int n, float r, Vector3 d, int seed)
        {
            _gPos.Clear(); _gYaw.Clear();
            if (n == 0) return;
            float y0 = _poolTop * 0.5f + d.y * 0.42f;
            float maxR = Mathf.Max(d.x, r - d.x * 0.55f);
            int cap = Mathf.Max(3, Mathf.FloorToInt(2f * Mathf.PI * maxR / (d.z * 1.08f)));
            int ring = n <= 3 ? 0 : Mathf.Min(n, cap);
            if (ring > 0 && n - ring > 0 && n - ring < 2 && ring > 4) ring--;             // одиночка в центре смотрится странно — пусть будет два
            float R = ring > 0 ? Mathf.Clamp(ring * d.z * 1.08f / (2f * Mathf.PI), d.x * 0.95f, maxR) : 0f;
            float a0 = (seed % 360) * Mathf.Deg2Rad;
            for (int i = 0; i < ring; i++)
            {
                float a = a0 + i / (float)ring * Mathf.PI * 2f + (Hash(i, 1) - 0.5f) * 0.08f;
                float rr = R + (Hash(i, 2) - 0.5f) * d.x * 0.12f;
                _gPos.Add(new Vector3(Mathf.Cos(a) * rr, y0, Mathf.Sin(a) * rr));
                // длина (Z ньокка) — по касательной: forward = (−sin a, cos a)
                _gYaw.Add(Mathf.Atan2(-Mathf.Sin(a), Mathf.Cos(a)) * Mathf.Rad2Deg + (Hash(i, 3) - 0.5f) * 14f);
            }
            int m = n - ring;
            float hole = ring > 0 ? R - d.x * 0.55f : r;                                   // свободный круг в середине
            float Rc = m <= 1 ? 0f : Mathf.Max(d.x * 0.62f, m * d.x * 1.05f / (2f * Mathf.PI));
            bool raise = ring > 0 && Rc + d.z * 0.5f > hole;
            for (int i = 0; i < m; i++)
            {
                float a = a0 + 0.4f + i / (float)Mathf.Max(1, m) * Mathf.PI * 2f;
                float rr = m <= 1 ? 0f : Mathf.Min(Rc, Mathf.Max(0f, hole - d.z * 0.3f)) ;
                float y = y0 + (raise ? d.y * 0.55f : 0f) + (raise ? (i % 2) * d.y * 0.2f : 0f);
                _gPos.Add(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr));
                _gYaw.Add(Mathf.Atan2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Rad2Deg + (Hash(i, 4) - 0.5f) * 20f);   // длиной к центру
            }
        }

        /// <summary>Высота верха блюда в точке (м от якоря): ньокки — эллипсоид, вне их — лужица.</summary>
        private float TopAt(float x, float z)
        {
            float top = _poolTop;
            for (int i = 0; i < _gPos.Count && i < _gnocchi.Count; i++)
            {
                Vector3 c = _gPos[i];
                float yaw = _gYaw[i] * Mathf.Deg2Rad;
                float dx = x - c.x, dz = z - c.z;
                // в оси ньокка: forward (Z) = (sin yaw, cos yaw), right (X) = (cos yaw, −sin yaw)
                float lz = dx * Mathf.Sin(yaw) + dz * Mathf.Cos(yaw);
                float lx = dx * Mathf.Cos(yaw) - dz * Mathf.Sin(yaw);
                float u = lx / (_dims.x * 0.5f), w = lz / (_dims.z * 0.5f);
                float q = u * u + w * w;
                if (q >= 1f) continue;
                float h = c.y + _dims.y * 0.5f * Mathf.Sqrt(1f - q) * 0.95f;
                if (h > top) top = h;
            }
            return top;
        }

        private static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>Точка «подсолнухом» внутри круга радиуса R (равномерно, без кучкования).</summary>
        private static Vector2 Spiral(int i, int n, float R, float phase)
        {
            float a = i * 2.39996f + phase;
            float rr = Mathf.Sqrt((i + 0.5f) / Mathf.Max(1, n)) * R;
            return new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr);
        }

        /// <summary>Детерминированный «случай» 0..1 (одинаковый у всех игроков).</summary>
        private static float Hash(int i, int salt)
        {
            float x = Mathf.Sin(i * 12.9898f + salt * 78.233f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }

        private void LateUpdate()
        {
            if (_shreds.Count == 0 && _crumbs.Count == 0) return;
            float m = Melt01;
            for (int i = 0; i < _shreds.Count && i < _shredBase.Count; i++)
            {
                if (_shreds[i] == null || !_shreds[i].gameObject.activeSelf) continue;
                Vector3 b = _shredBase[i];
                _shreds[i].localScale = new Vector3(b.x * (1f + 0.12f * m), b.y * (1f - 0.7f * m), b.z * (1f + 0.45f * m));   // оседают и расплываются
            }
            if (_mCheese != null)
            {
                GnocchiLooks.SetColor(_mCheese, Color.Lerp(GnocchiLooks.Cheese, GnocchiLooks.CheeseMelted, m));
                GnocchiLooks.SetSmooth(_mCheese, Mathf.Lerp(0.2f, 0.72f, m));
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (Time.time >= _lookAt)
            {
                _lookAt = Time.time + 0.2f;
                Camera cam = Camera.main;
                _looking = cam != null && Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit h, 6f, ~0, QueryTriggerInteraction.Collide)
                           && h.collider.GetComponentInParent<GnocchiPlate>() == this;
            }
            if (!_looking) return;
            int all = _g + _over + _raw, sauceAll = _sauce + _burnt + _butter;
            // общая тарелка с Аляской: пустая — молчим (подсказку даст десерт), ньокки/соус/сыр — наша
            if (all + sauceAll + _cheese == 0 && (_dessert || _cw == null || _cw.TotalCount > 0)) return;
            string ok = "<color=#9BE37A>✔</color>", no = "<color=#FF8A5A>✖</color>";
            string sauce = sauceAll == 0 ? $"{no} соус (масло с шалфеем)" : _burnt > 0 ? "<color=#FF6A4A>соус ГОРЕЛЫЙ</color>" : _sauce > 0 ? $"{ok} масло с шалфеем {sauceAll}/{_sauceFull}" : $"<color=#FFD25A>просто масло (без шалфея)</color>";
            string cheese = _cheese == 0 ? $"{no} сыр тёртый" : $"{ok} сыр {_cheese}/{_cheeseFull}" + (Melt01 < 1f ? $" (тает {Melt01 * 100f:0}%)" : " (растаял)");
            string gn = all == 0 ? $"{no} ньокки (из кастрюли лопатой)" : $"{ok} ньокки {all}/{_gnocchiFull}" + (_g > 0 ? $" · сварены {_avgCook * 100f:0}%" : "") + (_over > 0 ? $" · <color=#FF6A4A>разварились {_over}</color>" : "") + (_raw > 0 ? $" · <color=#FF8A5A>сырые {_raw}</color>" : "");
            int st = _starsNet.Value;
            string stars = st > 0 ? $"<color=#FFD23A><size=22>{new string('★', st)}</size></color><color=#888888><size=22>{new string('☆', 5 - st)}</size></color>  <b>{st} из 5</b> · {_scoreNet.Value}%" : "соберите блюдо: ньокки + соус + сыр";
            string text = $"<b>Ньокки с маслом и шалфеем</b>\n{gn}  ·  {sauce}  ·  {cheese}\n{stars}";
            if (_st == null) _st = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            Rect rr = new Rect(Screen.width * 0.5f - 360f, Screen.height * 0.58f, 720f, 74f);
            Color old = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(rr, Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(rr, text, _st);
        }

        // ───────────────────────── мелочи ─────────────────────────

        private void Sync(List<Transform> list, int n, string nm, Mesh mesh, Material mat)
        {
            while (list.Count < n) list.Add(MakeChild(nm, mesh, mat));
            for (int i = 0; i < list.Count; i++) if (list[i] != null) list[i].gameObject.SetActive(i < n);
        }

        private Transform MakeChild(string nm, Mesh mesh, Material mat)
        {
            EnsureRoot();
            var go = new GameObject(nm);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.SetParent(_root, false);
            return go.transform;
        }

        /// <summary>Материалы — свои у каждой тарелки (цвет меняется: соус, таяние сыра).</summary>
        private void EnsureMats()
        {
            if (_mGnocchi != null) return;
            _mGnocchi = new Material(GnocchiLooks.Mat("Plate gnocchi", new Color(1f, 0.91f, 0.63f), 0.55f, GnocchiLooks.DoughTex, 2f));
            _mRaw = new Material(GnocchiLooks.Mat("Plate gnocchi raw", new Color(0.95f, 0.82f, 0.5f), 0.25f, GnocchiLooks.DoughTex, 2f));
            _mOver = new Material(GnocchiLooks.Mat("Plate gnocchi over", new Color(0.97f, 0.95f, 0.86f), 0.7f, GnocchiLooks.DoughTex, 2f));
            _mSauce = new Material(GnocchiLooks.Mat("Plate butter", GnocchiLooks.Butter, 0.93f));
            _mLeaf = new Material(GnocchiLooks.Mat("Plate sage", GnocchiLooks.SageFried, 0.7f));
            _mCheese = new Material(GnocchiLooks.Mat("Plate cheese", GnocchiLooks.Cheese, 0.2f, GnocchiLooks.CheeseTex, 1f));
            _mFleck = new Material(GnocchiLooks.Mat("Plate fleck", GnocchiLooks.Fleck, 0.5f));
            _mBead = new Material(GnocchiLooks.Mat("Plate butter drop", GnocchiLooks.Butter, 0.97f));
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform anchor, float radius)
        {
            UnityEditor.Undo.RecordObject(this, "Тарелка ньокков");
            _anchor = anchor; _radius = radius;
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
#endif
    }
}
