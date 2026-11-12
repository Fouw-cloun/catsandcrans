using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v15: ПРОДУКТ МОЖНО СЖЕЧЬ — передержал под горелкой или в раскалённой таре.
    ///
    /// Подходит для любого продукта (вешается рядом с FoodPiece). Пока — какао-боб и какао-зёрна:
    /// меню CatCooks → Огонь и жар → 3. Горение: добавить на выделенные.
    ///
    /// КАК ГОРИТ
    ///   1. Перегрев копится: пламя горелки в продукт (× мощность) или тара горячее Burn From C.
    ///      Убрал жар — понемногу остывает (Cool Rate). У зёрен перегрев копится только ПОСЛЕ обжарки:
    ///      пока жарятся — это обжарка, а не горение.
    ///   2. С Smoke From — темнеет (обугливается) и идёт тёмный дымок: «ещё чуть — и сгорит».
    ///   3. Перегрев 100 % — ВСПЫХИВАЕТ: огонёк «как в Zelda» (CCFireFx), угольки, свет. Дальше горит
    ///      сам Flame Seconds, жар больше не нужен — чернеет, по углю тлеют прожилки.
    ///   4. РАССЫПАЕТСЯ за Crumble Seconds: прогорает дырами (шейдер CatCooks/Burn Dissolve, край
    ///      светится), оседает, летят угольки.
    ///   5. Исчезает. На его месте — КУЧКА ПЕПЛА и тёмный СЛЕД на поверхности (в таре — на дне),
    ///      лежат Ash Lifetime секунд. Порции продукта пропадают (сгорел).
    ///
    /// Сеть: считает сервер; по сети — стадия и перегрев (2 байта), конец — один RPC.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class FoodBurnable : NetworkBehaviour
    {
        [Header("Перегрев")]
        [Tooltip("Секунд под пламенем горелки (мощность 1) до вспышки. Кровельная мощнее — быстрее")]
        [Min(0.5f)][SerializeField] private float _torchSeconds = 8f;
        [Tooltip("Тара горячее этого — продукт в ней перегревается, °C")]
        [SerializeField] private float _burnFromC = 210f;
        [Tooltip("Секунд в такой таре до вспышки (при Burn From C; горячее — быстрее)")]
        [Min(0.5f)][SerializeField] private float _panSeconds = 14f;
        [Tooltip("Остывание перегрева без жара, доля в секунду")]
        [Range(0f, 1f)][SerializeField] private float _coolRate = 0.06f;
        [Tooltip("С какой доли перегрева темнеет и дымит (предупреждение)")]
        [Range(0f, 1f)][SerializeField] private float _smokeFrom = 0.35f;

        [Header("Горение")]
        [Tooltip("Сколько горит сам после вспышки, с")]
        [Min(0.5f)][SerializeField] private float _flameSeconds = 4f;
        [Tooltip("Сколько рассыпается, с")]
        [Min(0.2f)][SerializeField] private float _crumbleSeconds = 1.6f;
        [Tooltip("v17: сколько лежат пепел и след, с (потом оседают и тают за 3 с)")]
        [Min(4f)][SerializeField] private float _ashSeconds = 10f;
        [Tooltip("Горит и в руке (иначе в руке перегрев не копится)")]
        [SerializeField] private bool _burnWhenHeld = false;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        // 0 — цел, 1 — горит, 2 — рассыпается, 3 — сгорел
        private readonly SyncVar<byte> _stage = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _heat = new SyncVar<byte>(0);

        private FoodPiece _food;
        private FoodRoastable _roast;
        private Renderer[] _rends;
        private Material[][] _origMats;
        private Material[] _burnMats;
        private MaterialPropertyBlock _mpb;
        private string[] _colorProp;
        private Color[] _colorFrom;
        private bool _colorsTaken;

        // сервер
        private float _h, _stageT, _logAt;
        private int _lastTenth;
        private Cookware _inCw;

        // вид
        private CCFireFx _fire;
        private float _shownStageT, _smokeAcc;
        private Vector3 _scale0;
        private Vector3 _pos0;
        private bool _swapped;

        public bool IsBurning => _stage.Value >= 1;
        /// <summary>Перегрев 0..1 (у всех).</summary>
        public float Heat01 => _heat.Value / 255f;

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _roast = GetComponent<FoodRoastable>();
            _rends = GetComponentsInChildren<Renderer>(true);
            _scale0 = transform.localScale;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _stage.OnChange += OnStageChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _stage.OnChange -= OnStageChanged;
        }

        private void OnStageChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            _shownStageT = 0f;
            if (next == 1 && _log) Debug.Log($"[Горение] «{name}» ВСПЫХНУЛ — сейчас сгорит", this);
        }

        private void Update()
        {
            if (base.IsServerInitialized) ServerTick(Time.deltaTime);
            if (base.IsClientInitialized || !base.IsServerInitialized) ViewTick(Time.deltaTime);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void ServerTick(float dt)
        {
            if (_food == null || dt <= 0f) return;
            byte st = _stage.Value;
            if (st == 0)
            {
                if (_food.IsConsumed) return;
                float rate = HeatRate(out string from);
                if (rate > 0f) _h += rate * dt;
                else _h = Mathf.Max(0f, _h - _coolRate * dt);
                _h = Mathf.Clamp01(_h);
                byte hb = (byte)Mathf.RoundToInt(_h * 255f);
                if (hb != _heat.Value) _heat.Value = hb;
                int tenth = Mathf.FloorToInt(_h * 10f);
                if (_log && rate > 0f && tenth != _lastTenth && tenth >= 3 && tenth < 10 && Time.time >= _logAt)
                {
                    _logAt = Time.time + 1f;
                    Debug.Log($"[Горение] «{name}»: перегрев {tenth * 10}% ({from}) — убери жар, а то сгорит!", this);
                }
                _lastTenth = tenth;
                if (_h >= 1f) { _stage.Value = 1; _stageT = 0f; }
                return;
            }
            _stageT += dt;
            if (st == 1 && _stageT >= _flameSeconds) { _stage.Value = 2; _stageT = 0f; return; }
            if (st == 2 && _stageT >= _crumbleSeconds)
            {
                // в руке не исчезаем — ждём, пока выпустят (иначе рука держит пустоту)
                if (_food.IsHeld) return;
                Vector3 at; Vector3 n;
                FindSurface(out at, out n);
                float size = SizeOf();
                bool consumed = _food.IsConsumed || _food.ServerTryConsume();
                if (!consumed) return;
                _stage.Value = 3;
                RpcAsh(at, n, size);
                if (_log) Debug.Log($"[Горение] «{name}» СГОРЕЛ — кучка пепла", this);
                base.NetworkObject.RemoveOwnership();
                base.Despawn(base.NetworkObject);
            }
        }

        /// <summary>Насколько быстро копится перегрев (доля в секунду) и от чего.</summary>
        private float HeatRate(out string from)
        {
            from = null;
            if (!_burnWhenHeld && _food.IsHeld) return 0f;
            if (_roast != null && !_roast.IsRoasted) return 0f;           // зёрна ещё жарятся — это обжарка
            float rate = 0f;
            Vector3 p = CenterOf();
            float jet = JetHeat(p);
            if (jet > 0f) { rate += jet / _torchSeconds; from = "горелка"; }
            Cookware cw = FindCookware(p);
            if (cw == null && _inCw != null && Near(_inCw, p)) cw = _inCw;
            if (cw != null)
            {
                _inCw = cw;
                CookwareHeat heat = cw.GetComponent<CookwareHeat>();
                if (heat != null && heat.TemperatureC >= _burnFromC)
                {
                    float k = 1f + (heat.TemperatureC - _burnFromC) / 60f;
                    rate += k / _panSeconds;
                    from = (from == null ? "" : from + " + ") + $"«{cw.name}» {heat.TemperatureC:0} °C";
                }
            }
            return rate;
        }

        private float JetHeat(Vector3 p)
        {
            float best = 0f;
            var jets = HeatJets.All;
            float s = SizeOf();
            for (int i = 0; i < jets.Count; i++)
            {
                IHeatJet j = jets[i];
                if (j == null || (j is Object o && o == null) || !j.JetActive) continue;
                Vector3 d = p - j.JetOrigin;
                float along = Vector3.Dot(d, j.JetDirection);
                float len = Mathf.Max(0.05f, j.JetLength) * 1.15f;
                if (along < 0f || along > len) continue;
                float radial = (d - j.JetDirection * along).magnitude;
                float r = Mathf.Max(0.03f, j.JetRadius) * (0.6f + along / len) + s * 0.5f;
                if (radial > r) continue;
                float k = Mathf.Max(0.2f, j.JetPower) * (1f - 0.5f * along / len);
                if (k > best) best = k;
            }
            return best;
        }

        private static Cookware FindCookware(Vector3 p)
        {
            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null) continue;
                if (cw.IsInside(p)) return cw;
                Collider iv = cw.InsideVolume;
                if (iv != null && iv.bounds.Contains(p)) return cw;
            }
            return null;
        }

        private bool Near(Cookware cw, Vector3 p)
        {
            if (cw == null || cw.InsideVolume == null) return false;
            Bounds b = cw.InsideVolume.bounds;
            b.Expand(SizeOf() * 1.5f + 0.1f);
            return b.Contains(p);
        }

        /// <summary>Куда лечь пеплу: луч вниз из центра (мимо себя) — пол, стол, дно тары.</summary>
        private void FindSurface(out Vector3 at, out Vector3 n)
        {
            Vector3 c = CenterOf();
            float s = SizeOf();
            at = c - Vector3.up * s * 0.5f;
            n = Vector3.up;
            RaycastHit[] hits = Physics.RaycastAll(c + Vector3.up * s * 0.2f, Vector3.down, s * 4f + 1f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<CharacterController>() != null) continue;
                if (h.distance < best) { best = h.distance; at = h.point; n = h.normal; }
            }
        }

        [ObserversRpc]
        private void RpcAsh(Vector3 at, Vector3 n, float size)
        {
            if (_fire != null) { _fire.Burst(CenterOf(), size, 18); _fire.Stop(2f); _fire = null; }
            CCAshMark.Spawn(at, n, size, _ashSeconds);
            for (int i = 0; i < 4; i++)
                CCHeatFx.Wisp(at + Random.insideUnitSphere * size * 0.3f + Vector3.up * size * 0.2f,
                              Vector3.up * Random.Range(0.2f, 0.4f), size * Random.Range(0.6f, 1f),
                              new Color(0.35f, 0.33f, 0.32f, 0.4f), Random.Range(1.2f, 2f));
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void ViewTick(float dt)
        {
            byte st = _stage.Value;
            float h = Heat01;
            _shownStageT += dt;

            // 2. Предупреждение: темнеет и дымит
            if (st == 0)
            {
                if (_fire != null) { _fire.Stop(1.5f); _fire = null; }
                float warn = Mathf.InverseLerp(_smokeFrom, 1f, h);
                Tint(warn * 0.6f);
                if (warn > 0f)
                {
                    _smokeAcc += dt * (1.5f + 5f * warn);
                    if (_smokeAcc >= 1f)
                    {
                        _smokeAcc -= 1f;
                        float s = SizeOf();
                        Color c = Color.Lerp(new Color(0.6f, 0.58f, 0.56f, 0.2f), new Color(0.18f, 0.17f, 0.17f, 0.45f), warn);
                        CCHeatFx.Wisp(CenterOf() + Vector3.up * s * 0.4f + Random.insideUnitSphere * s * 0.2f,
                                      Vector3.up * Random.Range(0.25f, 0.45f) * Mathf.Sqrt(Mathf.Max(0.3f, s / 0.12f)),
                                      s * Random.Range(0.5f, 0.9f), c, Random.Range(1.1f, 1.7f));
                    }
                }
                return;
            }

            // 3. Горит: огонь, обугливается до чёрного
            if (_fire == null && st <= 2)
            {
                _fire = CCFireFx.Attach(_rends);
                SwapToBurn();
            }
            if (st == 1)
            {
                float k = Mathf.Clamp01(_shownStageT / 0.4f);
                if (_fire != null) _fire.Power = k;
                SetBurn(Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(_shownStageT / Mathf.Max(0.5f, _flameSeconds * 0.8f))), 0f);
                return;
            }
            // 4. Рассыпается: прогорает дырами, оседает
            if (st == 2)
            {
                float k = Mathf.Clamp01(_shownStageT / _crumbleSeconds);
                if (_fire != null) _fire.Power = 1f - k * 0.7f;
                SetBurn(1f, k);
                float sink = k * k;
                transform.localScale = Vector3.Scale(_scale0, new Vector3(1f - 0.1f * sink, 1f - 0.35f * sink, 1f - 0.1f * sink));
            }
        }

        /// <summary>Обугливание до вспышки — темнее через MaterialPropertyBlock (материалы не трогаем).</summary>
        private float _tintShown;

        private void Tint(float k)
        {
            if (_swapped || _rends == null) return;
            if (Mathf.Abs(k - _tintShown) < 0.01f) return;           // не трогаем MPB зря (его двигают и другие)
            _tintShown = k;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            if (!_colorsTaken) TakeColors();
            for (int i = 0; i < _rends.Length; i++)
            {
                Renderer r = _rends[i];
                if (r == null || _colorProp[i] == null || r is ParticleSystemRenderer) continue;
                r.GetPropertyBlock(_mpb);
                if (k <= 0.001f) { _mpb.SetColor(_colorProp[i], CurrentBase(i, r)); r.SetPropertyBlock(_mpb); continue; }
                Color b = CurrentBase(i, r);
                Color c = Color.Lerp(b, new Color(0.06f, 0.045f, 0.035f, b.a), k);
                _mpb.SetColor(_colorProp[i], c);
                r.SetPropertyBlock(_mpb);
            }
        }

        // цвет «до обугливания»: у зёрен его двигает обжарка (тоже через MPB) — берём то, что она поставила
        private Color CurrentBase(int i, Renderer r)
        {
            if (_roast != null)
            {
                float t = _roast.Roast01;
                t = t * t * (3f - 2f * t);
                Color rc = _roast.RoastedColor;
                Color c = Color.Lerp(_colorFrom[i], rc, t);
                c.a = _colorFrom[i].a;
                return c;
            }
            return _colorFrom[i];
        }

        private void TakeColors()
        {
            _colorsTaken = true;
            _colorProp = new string[_rends.Length];
            _colorFrom = new Color[_rends.Length];
            for (int i = 0; i < _rends.Length; i++)
            {
                Material m = _rends[i] != null ? _rends[i].sharedMaterial : null;
                if (m != null && m.HasProperty("_BaseColor")) { _colorProp[i] = "_BaseColor"; _colorFrom[i] = m.GetColor("_BaseColor"); }
                else if (m != null && m.HasProperty("_Color")) { _colorProp[i] = "_Color"; _colorFrom[i] = m.GetColor("_Color"); }
                else _colorFrom[i] = Color.white;
            }
        }

        /// <summary>На время горения — шейдер Burn Dissolve (текстура и цвет со старого материала).</summary>
        private void SwapToBurn()
        {
            if (_swapped) return;
            Shader sh = Shader.Find("CatCooks/Burn Dissolve");
            if (sh == null)
            {
                Debug.LogWarning("[Горение] нет шейдера «CatCooks/Burn Dissolve» — продукт не будет рассыпаться, просто исчезнет. Меню CatCooks → Огонь и жар → 2");
                return;
            }
            _swapped = true;
            if (!_colorsTaken) TakeColors();
            _burnMats = new Material[_rends.Length];
            _origMats = new Material[_rends.Length][];
            for (int i = 0; i < _rends.Length; i++)
            {
                Renderer r = _rends[i];
                if (r == null || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if ((r.gameObject.hideFlags & HideFlags.DontSave) != 0 && r.GetComponent<MeshFilter>() == null) continue;
                Material src = r.sharedMaterial;
                var m = new Material(sh) { name = "Burn (auto)", hideFlags = HideFlags.DontSave };
                Texture tex = null;
                if (src != null)
                {
                    if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
                    if (tex == null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
                }
                if (tex != null) m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", _colorProp[i] != null ? CurrentBase(i, r) : Color.white);
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Bounds mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                float ms = Mathf.Max(1e-4f, Mathf.Max(mb.size.x, Mathf.Max(mb.size.y, mb.size.z)));
                m.SetFloat("_NoiseScale", 7f / ms);
                m.SetVector("_MinMaxY", new Vector4(mb.min.y, mb.max.y, 0f, 0f));
                _origMats[i] = r.sharedMaterials;
                var arr = new Material[r.sharedMaterials.Length];
                for (int k = 0; k < arr.Length; k++) arr[k] = m;
                r.SetPropertyBlock(null);
                r.sharedMaterials = arr;
                _burnMats[i] = m;
            }
        }

        private void SetBurn(float charK, float burnK)
        {
            if (_burnMats == null) return;
            foreach (Material m in _burnMats)
            {
                if (m == null) continue;
                m.SetFloat("_Char", charK);
                m.SetFloat("_Burn", burnK);
            }
        }

        private Vector3 CenterOf()
        {
            bool any = false; Bounds b = new Bounds(transform.position, Vector3.zero);
            if (_rends != null)
                foreach (Renderer r in _rends)
                {
                    if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            return b.center;
        }

        private float SizeOf()
        {
            bool any = false; Bounds b = new Bounds(transform.position, Vector3.one * 0.1f);
            if (_rends != null)
                foreach (Renderer r in _rends)
                {
                    if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            return Mathf.Max(0.03f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
        }

        private void OnDestroy()
        {
            if (_fire != null) { _fire.Stop(2f); _fire = null; }
            if (_burnMats != null) foreach (Material m in _burnMats) if (m != null) CCHeatFx.Kill(m);
        }
    }
}
