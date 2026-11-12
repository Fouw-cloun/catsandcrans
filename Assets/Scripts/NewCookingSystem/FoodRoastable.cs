using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Продукт сам решает, как попасть в тару. Cookware спрашивает его перед тем, как «проглотить».
    /// true — решил сам (put: попал ли в тару), false — как обычно.
    /// </summary>
    public interface IFoodPutHandler
    {
        bool ServerHandlePut(Cookware cw, out bool put);
    }

    /// <summary>
    /// ОБЖАРКА КУСКОМ — какао-зёрна (белая гроздь из какао-боба).
    ///
    /// Сырые зёрна в таре НЕ растворяются в порции — лежат куском. Тара на огне
    /// (Cookware Heat ≥ Roast From C) или в зёрна бьёт горелка — они жарятся:
    /// плавно темнеют (цвет материала → Roasted Color), идёт дымок. Готово (100 %) —
    /// гроздь коричневая, «Обжаренные какао-зёрна», и ТАК И ЛЕЖИТ в таре куском.
    /// ОТБОЙНИК носиком в тару — гроздь толчётся: размер НЕ меняется, из-под носика летит какао-пыль
    /// и разлетается порошок (чем дольше долбишь — тем гуще). Через Grind Seconds гроздь
    /// РАЗВАЛИВАЕТСЯ (куски + облако пыли) и становится «Тёртое какао» (порошок, порции в таре).
    /// Гроздь запоминает, в какую тару её положили: отбойник её подпрыгивания не теряет.
    /// Absorb When Roasted ✔ — по-старому: сразу в порции обжаренных зёрен.
    ///
    /// Вешается на префаб зёрен рядом с FoodPiece. Материал не трогается: цвет меняется
    /// через MaterialPropertyBlock (_BaseColor / _Color). Свой материал обжаренных —
    /// поле Roasted Material (включится на 100 %).
    ///
    /// Сеть: жарит сервер, по сети — один байт прогресса и флаг «жарится сейчас».
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class FoodRoastable : NetworkBehaviour, IFoodPutHandler
    {
        [Header("Во что")]
        [Tooltip("Продукт после обжарки (Обжаренные какао-зёрна)")]
        [SerializeField] private ProductDef _roastedProduct;

        [Header("Жар")]
        [Tooltip("С какой температуры тары жарится, °C (Cookware Heat ковша)")]
        [SerializeField] private float _roastFromC = 120f;
        [Tooltip("Сколько секунд жарить на огне до готовности")]
        [Min(1f)][SerializeField] private float _roastSeconds = 20f;
        [Tooltip("Горелка прямо в зёрна жарит во столько раз быстрее (× мощность горелки)")]
        [Min(0f)][SerializeField] private float _torchSpeed = 1.5f;
        [Tooltip("Сырые зёрна в таре лежат куском (жарятся и темнеют). Выкл — сразу уходят в порции, как обычные продукты")]
        [SerializeField] private bool _stayWholeUntilRoasted = true;

        [Header("Отбойник → какао")]
        [Tooltip("Выкл (как надо): обжаренная гроздь лежит куском, в порошок — только отбойником. Вкл — сразу в порции")]
        [SerializeField] private bool _absorbWhenRoasted = false;
        [Tooltip("Во что толчёт отбойник (пусто — «Тёртое какао» по названию)")]
        [SerializeField] private ProductDef _groundProduct;
        [Tooltip("Сколько секунд долбить отбойником до порошка")]
        [Min(0.5f)][SerializeField] private float _grindSeconds = 8f;
        [Tooltip("Цвет какао-порошка (пыль из-под отбойника)")]
        [SerializeField] private Color _powderColor = new Color(0.45f, 0.28f, 0.17f, 1f);

        [Header("Вид")]
        [Tooltip("Цвет обжаренных (к нему плавно идёт цвет материала)")]
        [SerializeField] private Color _roastedColor = new Color(0.30f, 0.16f, 0.08f, 1f);
        [Tooltip("Свой материал обжаренных — включится на 100 %. Пусто — только цвет")]
        [SerializeField] private Material _roastedMaterial;
        [Tooltip("Дымок, пока жарится")]
        [SerializeField] private bool _smoke = true;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _roast = new SyncVar<byte>(0);
        private readonly SyncVar<bool> _heating = new SyncVar<bool>(false);
        private readonly SyncVar<byte> _ground = new SyncVar<byte>(0);

        /// <summary>Все грозди в сцене — отбойник ищет, какая лежит в его таре.</summary>
        private static readonly List<FoodRoastable> _all = new List<FoodRoastable>();
        private float _grind;            // сервер, секунды
        private int _grindQuarter;
        private Cookware _inCw;          // сервер: в какую тару положили (отбойник может подбросить гроздь)

        private FoodPiece _food;
        private Renderer[] _renderers;
        private Color[] _baseColors;
        private string[] _colorProps;
        private MaterialPropertyBlock _mpb;
        private bool _swapped;
        private float _progress;          // сервер, 0..1
        private int _lastQuarter;
        private float _smokeAcc, _hintAt;
        private bool _done;

        /// <summary>Обжарено на 0..1 (у всех).</summary>
        public float Roast01 => _roast.Value / 255f;
        public bool IsRoasted => _roast.Value >= 255;
        /// <summary>Цвет обжаренных (FoodBurnable темнит от него).</summary>
        public Color RoastedColor => _roastedColor;
        /// <summary>v17.5: цвет какао-пыли (для эффектов отбойника).</summary>
        public Color PowderColor => _powderColor;

        private void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
        private void OnDisable() { _all.Remove(this); }

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length];
            _colorProps = new string[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material m = _renderers[i].sharedMaterial;
                if (m != null && m.HasProperty("_BaseColor")) { _colorProps[i] = "_BaseColor"; _baseColors[i] = m.GetColor("_BaseColor"); }
                else if (m != null && m.HasProperty("_Color")) { _colorProps[i] = "_Color"; _baseColors[i] = m.GetColor("_Color"); }
                else _baseColors[i] = Color.white;
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _roast.OnChange += OnRoastChanged;
            _ground.OnChange += OnGroundChanged;
            ApplyLook();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _roast.OnChange -= OnRoastChanged;
            _ground.OnChange -= OnGroundChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _progress = _roast.Value / 255f;
            _lastQuarter = Mathf.FloorToInt(_progress * 4f);
            if (_roastedProduct == null)
                Debug.LogError($"[Обжарка] «{name}»: не задан Roasted Product (Обжаренные какао-зёрна)", this);
        }

        private void OnRoastChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ApplyLook();
        }

        private void Update()
        {
            if (base.IsServerInitialized) ServerTick(Time.deltaTime);
            if (base.IsClientInitialized || !base.IsServerInitialized) SmokeTick(Time.deltaTime);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void ServerTick(float dt)
        {
            if (_food == null || _food.IsConsumed || _done || dt <= 0f) return;

            Vector3 p = CenterOf();
            Cookware inCw = FindCookware(p);
            // лежит в таре, но центр чуть вне полости (низко на дне, у стенки) — всё равно в ней
            if (inCw == null && _inCw != null && Near(_inCw, p)) inCw = _inCw;
            if (inCw != null) _inCw = inCw;
            float rate = 0f;
            string from = null;
            if (inCw != null)
            {
                CookwareHeat heat = inCw.GetComponent<CookwareHeat>();
                if (heat != null && heat.TemperatureC >= _roastFromC) { rate += 1f; from = $"«{inCw.name}» {heat.TemperatureC:0} °C"; }
                else if (_log && Time.time >= _hintAt && heat != null)
                {
                    _hintAt = Time.time + 5f;
                    if (!IsRoasted) Debug.Log($"[Обжарка] «{name}» в «{inCw.name}»: {heat.TemperatureC:0} °C, жарится с {_roastFromC:0} °C — ставь на огонь", this);
                }
            }
            float jet = JetHeat(p);
            if (jet > 0f) { rate += jet * _torchSpeed; from = from == null ? "горелка" : from + " + горелка"; }

            bool heating = rate > 0f && !IsRoasted;
            if (_heating.Value != heating) _heating.Value = heating;

            if (heating)
            {
                _progress = Mathf.Min(1f, _progress + dt * rate / _roastSeconds);
                byte b = (byte)Mathf.RoundToInt(_progress * 255f);
                if (b != _roast.Value) _roast.Value = b;
                int q = Mathf.FloorToInt(_progress * 4f);
                if (_log && q != _lastQuarter && q < 4)
                    Debug.Log($"[Обжарка] «{name}»: {q * 25}% ({from})", this);
                _lastQuarter = q;
                if (_progress >= 1f)
                {
                    if (_log) Debug.Log($"[Обжарка] «{name}»: ОБЖАРЕНО → {(_roastedProduct != null ? _roastedProduct.Title : "?")}", this);
                    _heating.Value = false;
                }
            }

            // Обжарилось, лежит в таре — по-старому сразу в порции (если включено). Иначе — ждёт отбойник.
            if (_absorbWhenRoasted && IsRoasted && inCw != null && !_food.IsHeld) ServerAbsorb(inCw);
        }

        public bool ServerHandlePut(Cookware cw, out bool put)
        {
            put = false;
            if (_inCw != cw && _log) Debug.Log($"[Обжарка] «{name}» лежит в «{cw.name}» куском{(IsRoasted ? " — отбойником в порошок" : " — грей до 120 °C или горелкой")}", this);
            _inCw = cw;
            if (!IsRoasted)
            {
                if (!_stayWholeUntilRoasted) return false;     // как обычный продукт
                return true;                                   // лежит куском, жарится
            }
            if (!_absorbWhenRoasted) return true;              // обжаренная — тоже куском: в порошок отбойником
            put = ServerAbsorb(cw);
            return true;
        }

        // ───────────────────────── ОТБОЙНИК: гроздь → какао-порошок ─────────────────────────

        /// <summary>
        /// Отбойник долбит в тару seconds секунд: обжаренные грозди, лежащие в этой таре, толкутся.
        /// true — в таре была гроздь (работа ушла в неё). Сервер. Зовёт JackhammerGrinder.
        /// </summary>
        public static bool ServerGrindIn(Cookware cw, float seconds)
        {
            if (cw == null || seconds <= 0f) return false;
            bool any = false;
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                FoodRoastable r = _all[i];
                if (r == null || !r.IsServerInitialized || !r.IsRoasted || r._done || r._food == null || r._food.IsHeld || r._food.IsConsumed) continue;
                if (!r.IsIn(cw)) continue;
                r.ServerGrind(cw, seconds);
                any = true;
            }
            return any;
        }

        /// <summary>Лежит ли в таре обжаренная гроздь (для подсказки отбойника, у всех).</summary>
        public static bool AnyRoastedIn(Cookware cw)
        {
            if (cw == null) return false;
            for (int i = 0; i < _all.Count; i++)
            {
                FoodRoastable r = _all[i];
                if (r == null || r._done || !r.IsRoasted || r._food == null || r._food.IsConsumed) continue;
                if (r.IsIn(cw)) return true;
            }
            return false;
        }

        /// <summary>
        /// В таре лежит НЕдожаренная гроздь: сколько процентов (−1 — нет такой). Отбойник подсказывает
        /// «дожарь», а не молчит «пустая».
        /// </summary>
        public static int UnroastedPercentIn(Cookware cw)
        {
            if (cw == null) return -1;
            int best = -1;
            for (int i = 0; i < _all.Count; i++)
            {
                FoodRoastable r = _all[i];
                if (r == null || r._done || r.IsRoasted || r._food == null || r._food.IsConsumed) continue;
                if (!r.IsIn(cw)) continue;
                best = Mathf.Max(best, Mathf.FloorToInt(r.Roast01 * 100f));
            }
            return best;
        }

        /// <summary>Точка у тары: в её полости с запасом на размер грозди (гроздь лежит на дне, у стенки).</summary>
        private bool Near(Cookware cw, Vector3 p)
        {
            if (cw == null) return false;
            Bounds b;
            if (cw.InsideVolume != null) b = cw.InsideVolume.bounds;
            else { Renderer rr = cw.GetComponentInChildren<Renderer>(); if (rr == null) return false; b = rr.bounds; }
            b.Expand(SizeOf() * 1.5f + 0.1f);
            return b.Contains(p);
        }

        /// <summary>Гроздь в этой таре: центр внутри полости, или её туда клали и она рядом (подпрыгнула от отбойника).</summary>
        private bool IsIn(Cookware cw)
        {
            Vector3 p = CenterOf();
            Cookware at = FindCookware(p);
            if (at == cw) { _inCw = cw; return true; }
            if (at != null) return false;                       // лежит в другой таре
            if (!Near(cw, p)) return false;
            _inCw = cw;
            return true;
        }

        private void ServerGrind(Cookware cw, float seconds)
        {
            if (_groundProduct == null) _groundProduct = FindGround();
            if (_groundProduct == null)
            {
                if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 5f; Debug.LogWarning("[Обжарка] нет продукта «Тёртое какао» — CatCooks → Шоколад → 1", this); }
                return;
            }
            _grind = Mathf.Min(_grindSeconds, _grind + seconds);
            float k = _grind / _grindSeconds;
            byte b = (byte)Mathf.RoundToInt(k * 255f);
            if (b != _ground.Value) _ground.Value = b;
            int q = Mathf.FloorToInt(k * 4f);
            if (_log && q != _grindQuarter && q < 4) Debug.Log($"[Обжарка] отбойник толчёт «{name}»: {q * 25}%", this);
            _grindQuarter = q;
            ProductDef from = _food.Def;
            CookRecipeBook.ReportNative(cw, CookDriver.Book, from, _groundProduct, k);
            if (_grind < _grindSeconds) return;

            if (!_food.ServerTryConsume())
            {
                if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 3f; Debug.LogWarning($"[Обжарка] «{name}»: растолчено, но кусок не отдался (в руке/уже съеден?) — отпусти гроздь", this); }
                return;
            }
            _done = true;
            int n = Mathf.Max(1, _food.Count);
            RpcCrumble(CenterOf(), SizeOf());
            int added = cw.ServerAddPinch(_groundProduct.Id, _groundProduct.StartState, n, 0, false);
            CookRecipeBook.FinishNative(cw, CookDriver.Book, from, _groundProduct, $"{_groundProduct.Title} ×{added}");
            if (_log) Debug.Log($"[Обжарка] ГОТОВО — {_groundProduct.Title} ×{added} в «{cw.name}» (какао-порошок)", cw);
            base.NetworkObject.RemoveOwnership();
            base.Despawn(base.NetworkObject);
        }

        private static ProductDef FindGround()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || db.Table == null) return null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Table.GetProduct((ushort)id);
                if (p == null || string.IsNullOrEmpty(p.Title)) continue;
                string t = p.Title.Trim().ToLowerInvariant().Replace('ё', 'е');
                if (t == "тертое какао") return p;
            }
            return null;
        }

        /// <summary>
        /// Толчётся (у всех): размер не меняется — летит какао-пыль вверх и порошок разлетается в стороны,
        /// гуще к концу. Изредка отскакивают крошки.
        /// </summary>
        private void OnGroundChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            if (next <= prev) return;
            float k = next / 255f;
            float s = SizeOf();
            Vector3 at = CenterOf() + Vector3.up * s * 0.35f;
            Color dark = _roastedColor, powder = _powderColor;
            // v17.5: облачка какао-пыли и коричневые ошмётки (а не квадратики), к концу гуще
            CCPowderFx.Pound(at, Vector3.up, Color.Lerp(powder, dark, Random.Range(0f, 0.35f)), Mathf.Max(0.12f, s * 1.2f), 0.4f + 0.6f * k);
        }

        /// <summary>Конец: гроздь разваливается — куски и облако какао-пыли (у всех).</summary>
        [ObserversRpc]
        private void RpcCrumble(Vector3 at, float size)
        {
            // v17.5: распалась на порошок — большое облако какао-пыли и горсть коричневых ошмётков
            CCPowderFx.Impact(at, Vector3.up, _powderColor, Mathf.Max(0.15f, size * 1.3f), 1.6f);
            CCPowderFx.Impact(at, Vector3.up, _roastedColor, Mathf.Max(0.12f, size), 0.6f);
        }

        private bool ServerAbsorb(Cookware cw)
        {
            if (_done || _roastedProduct == null || cw == null || _food.IsHeld || _food.IsConsumed) return false;
            int n = Mathf.Max(1, _food.Count);
            if (cw.Capacity > 0 && cw.TotalCount + n > cw.Capacity)
            {
                if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 3f; Debug.Log($"[Обжарка] «{cw.name}» полная — зёрна не влезли", this); }
                return false;
            }
            if (!_food.ServerTryConsume()) return false;
            _done = true;
            int added = cw.ServerAddPinch(_roastedProduct.Id, _roastedProduct.StartState, n, 0, false);
            if (_log) Debug.Log($"[Обжарка] {_roastedProduct.Title} ×{added} → «{cw.name}» (дальше — отбойником в пыль)", cw);
            base.NetworkObject.RemoveOwnership();
            base.Despawn(base.NetworkObject);
            return true;
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

        /// <summary>Попадает ли пламя горелки в точку: 0 — нет, иначе мощность (сильнее у сопла).</summary>
        private float JetHeat(Vector3 p)
        {
            float best = 0f;
            var jets = HeatJets.All;
            for (int i = 0; i < jets.Count; i++)
            {
                IHeatJet j = jets[i];
                if (j == null || (j is Object o && o == null) || !j.JetActive) continue;
                Vector3 d = p - j.JetOrigin;
                float along = Vector3.Dot(d, j.JetDirection);
                float len = Mathf.Max(0.05f, j.JetLength) * 1.15f;
                if (along < 0f || along > len) continue;
                float radial = (d - j.JetDirection * along).magnitude;
                float r = Mathf.Max(0.03f, j.JetRadius) * (0.6f + along / len) + SizeOf() * 0.5f;
                if (radial > r) continue;
                float k = Mathf.Max(0.2f, j.JetPower) * (1f - 0.5f * along / len);
                if (k > best) best = k;
            }
            return best;
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void ApplyLook()
        {
            if (_renderers == null) return;
            float k = Roast01;
            if (k >= 1f && _roastedMaterial != null)
            {
                if (_swapped) return;
                _swapped = true;
                foreach (Renderer r in _renderers)
                {
                    if (r == null) continue;
                    r.SetPropertyBlock(null);
                    var mats = r.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++) mats[m] = _roastedMaterial;
                    r.sharedMaterials = mats;
                }
                return;
            }
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            float t = k * k * (3f - 2f * k);
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null || _colorProps[i] == null) continue;
                r.GetPropertyBlock(_mpb);
                Color c = Color.Lerp(_baseColors[i], _roastedColor, t);
                c.a = _baseColors[i].a;
                _mpb.SetColor(_colorProps[i], c);
                r.SetPropertyBlock(_mpb);
            }
        }

        private void SmokeTick(float dt)
        {
            if (!_smoke || !_heating.Value) { _smokeAcc = 0f; return; }
            // v14: мягкий дымок (клубки CCHeatFx), к готовности гуще и темнее; поверх — лёгкое марево
            float k = Roast01;
            _smokeAcc += dt * (2.5f + 3.5f * k);
            float s = SizeOf();
            Vector3 c = CenterOf();
            CCHeatFx.Haze(c + Vector3.up * s * 0.2f, s * 1.3f, s * 2.4f, 0.5f + 0.4f * k);
            if (_smokeAcc < 1f) return;
            _smokeAcc -= 1f;
            Vector3 at = c + Vector3.up * s * 0.35f + Random.insideUnitSphere * s * 0.2f;
            Color a = Color.Lerp(new Color(0.93f, 0.92f, 0.9f, 0.18f), new Color(0.42f, 0.38f, 0.35f, 0.32f), k);
            CCHeatFx.Wisp(at, new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(0.25f, 0.45f), Random.Range(-0.05f, 0.05f)) * Mathf.Sqrt(Mathf.Max(0.3f, s / 0.12f)),
                          s * Random.Range(0.45f, 0.8f), a, Random.Range(1.1f, 1.8f));
        }

        private Vector3 CenterOf()
        {
            if (_renderers == null || _renderers.Length == 0 || _renderers[0] == null) return transform.position;
            Bounds b = _renderers[0].bounds;
            for (int i = 1; i < _renderers.Length; i++) if (_renderers[i] != null) b.Encapsulate(_renderers[i].bounds);
            return b.center;
        }

        private float SizeOf()
        {
            if (_renderers == null || _renderers.Length == 0 || _renderers[0] == null) return 0.1f;
            Vector3 s = _renderers[0].bounds.size;
            return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        }
    }
}
