using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТЕМПЕРАТУРА СОДЕРЖИМОГО ТАРЫ И ЕЁ ВИД: кастрюля на костре.
    ///
    /// Костёр (BonfirePit) присылает таре температуру огня под дном. Этот компонент
    /// считает, как греется ТО, ЧТО ВНУТРИ:
    ///   • пустая кастрюля раскаляется быстро — со дна идёт дымок;
    ///   • вода греется тем дольше, чем её больше; выше 100 °C не поднимается — кипит;
    ///   • холодное, брошенное в кипяток, остужает его (5 морковок — кипение пропадёт
    ///     на время, потом вернётся);
    ///   • налили воду в раскалённую пустую кастрюлю — шипит, пар столбом.
    ///
    /// Вид (у каждого игрока сам, по одному числу температуры):
    ///   60+ °C — лёгкий пар; 70+ — со дна редкие пузырьки; 95+ — пузырей много;
    ///   кипение — поверхность бурлит (как при взбивании), пузыри крупные, пар клубами.
    ///
    /// Тара варит (Cookware: продукты меняют состояние), когда содержимое ≥ Active From C
    /// у Cookware (95). Пока вода кипит — идёт шаг рецепта «Варить» (бульон).
    ///
    /// Сеть: сервер считает, по сети — 1 байт температуры.
    ///
    /// ═══ КУДА ВЕШАТЬ ═══
    /// На корень кастрюли, рядом с Cookware. Старый PotThermal с этой кастрюли убрать.
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class CookwareHeat : NetworkBehaviour, ITemperatureSource
    {
        [Header("Нагрев")]
        [Tooltip("Комнатная температура, °C — к ней остывает")]
        [SerializeField] private float _ambientC = 20f;
        [Tooltip("Как быстро содержимое догоняет огонь (1/с) у почти пустой тары. Больше — быстрее закипает")]
        [Range(0.005f, 0.5f)][SerializeField] private float _heatSpeed = 0.06f;
        [Tooltip("Ускорение нагрева большой кастрюли: 1 — как было (4 ведра воды кипели ~2 мин), 3 — ~40 с")]
        [Range(1f, 6f)][SerializeField] private float _heatBoost = 3f;
        [Tooltip("Остывание без огня (1/с)")]
        [Range(0.001f, 0.2f)][SerializeField] private float _coolSpeed = 0.02f;
        [Tooltip("Сколько порций содержимого = «ещё одна тара по теплоёмкости». Больше воды — дольше греется. " +
                 "30 = ведро воды удваивает время закипания")]
        [Min(1f)][SerializeField] private float _portionsPerMass = 30f;
        [Tooltip("Температура кипения, °C")]
        [SerializeField] private float _boilC = 100f;
        [Tooltip("Температура, если тару греет плита (флаг Heat On у Cookware), °C")]
        [SerializeField] private float _stoveC = 200f;

        [Header("Пузыри и пар")]
        [Tooltip("С какой температуры идёт пар")]
        [SerializeField] private float _steamFromC = 60f;
        [Tooltip("С какой температуры со дна поднимаются пузырьки")]
        [SerializeField] private float _bubblesFromC = 70f;
        [Tooltip("Пузырей в секунду при кипении")]
        [Min(0f)][SerializeField] private float _bubblesPerSecond = 45f;
        [Tooltip("Размер пузыря при кипении, м")]
        [SerializeField] private float _bubbleSize = 0.03f;
        [Tooltip("Клубов пара в секунду при кипении")]
        [Min(0f)][SerializeField] private float _steamPerSecond = 9f;
        [Tooltip("Размер клуба пара")]
        [SerializeField] private float _steamScale = 0.35f;
        [SerializeField] private Color _steamColor = new Color(0.95f, 0.97f, 1f, 1f);
        [Tooltip("Насколько бурлит поверхность при кипении (как взбивание, 0..1)")]
        [Range(0f, 1f)][SerializeField] private float _boilAgitation = 0.35f;

        [Tooltip("Красивое кипение (пузыри-куполки с кольцами по воде, мягкий пар). Выкл — старые частицы.")]
        [SerializeField] private bool _prettyFx = true;
        [Tooltip("Размер клубов пара относительно кастрюли (меньше — пар не вылезает сквозь стенки)")]
        [Range(0.1f, 1.5f)][SerializeField] private float _steamSizeK = 0.5f;

        [Header("Сухая тара")]
        [Tooltip("Пустая (или без жидкости) тара выше этой температуры дымит со дна")]
        [SerializeField] private float _drySmokeFromC = 130f;
        [Min(0f)][SerializeField] private float _smokePerSecond = 5f;
        [SerializeField] private Color _smokeColor = new Color(0.45f, 0.43f, 0.42f, 1f);
        [Tooltip("Глубина дна под горловиной, м (откуда идёт дым у пустой тары)")]
        [SerializeField] private float _bottomDepth = 0.25f;

        [Header("v14: марево и лёгкий пар от горячей тары")]
        [Tooltip("Марево (воздух дрожит) над горячей тарой — и пустой, и с едой")]
        [SerializeField] private bool _haze = true;
        [Tooltip("С какой температуры содержимого/дна идёт марево, °C")]
        [SerializeField] private float _hazeFromC = 70f;
        [Tooltip("При какой температуре марево в полную силу, °C")]
        [SerializeField] private float _hazeFullC = 220f;
        [Range(0f, 2f)][SerializeField] private float _hazeStrength = 1f;
        [Tooltip("Сухая (пустая) тара: с какой температуры идёт лёгкий полупрозрачный парок (до дыма), °C")]
        [SerializeField] private float _lightWispFromC = 100f;
        [SerializeField] private Color _lightWispColor = new Color(0.95f, 0.95f, 0.97f, 0.14f);

        [Header("Рецепт")]
        [Tooltip("Пока кипит — идёт шаг рецепта «Варить» (бульон)")]
        [SerializeField] private bool _recipeStep = true;
        [Tooltip("Сухое содержимое (без жидкости) жарится от этой температуры — шаг рецепта «Обжарить» (какао), °C")]
        [SerializeField] private float _roastFromC = 120f;
        private float _scorchLogAt;
        [Tooltip("С какой температуры идёт шаг рецепта «Растопить» (шоколад с маслом), °C")]
        [SerializeField] private float _meltFromC = 40f;
        [Tooltip("С какой температуры сухой сахар карамелизуется (рецепт «Карамель»)")]
        [SerializeField] private float _caramelFromC = 160f;
        [Tooltip("С какой температуры карамель пригорает (рецепт-ошибка «Карамель горелая»)")]
        [SerializeField] private float _scorchFromC = 200f;
        [Tooltip("Борщ: с какой температуры идёт шаг «Жарить» (пассеровка в сковороде), °C")]
        [SerializeField] private float _fryFromC = 95f;
        [Tooltip("Борщ: с какой температуры идёт шаг «Тушить» (свёкла, зажарка), °C")]
        [SerializeField] private float _stewFromC = 85f;
        [Tooltip("Борщ: «Уваривать» (томатная паста) — с этой температуры содержимого, °C")]
        [SerializeField] private float _simmerFromC = 70f;
        [Tooltip("Борщ: «Уваривать» — огонь под тарой не выше этого, °C. Костёр без раздува ≈ 140 °C, с раздувом до 240 — пригорает")]
        [SerializeField] private float _simmerMaxFireC = 175f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _tempNet = new SyncVar<byte>(20);

        private Cookware _cw;
        private CookwareLiquidVisual _lv;
        private PotLidSocket _lid;

        // сервер
        private float _t = 20f;
        private int _lastTotal;
        private bool _wasBoiling;
        private float _logAt;

        // вид
        private float _bubbleAcc, _steamAcc, _smokeAcc;
        private float _shownPrev = 20f;

        /// <summary>Температура огня под дном, °C (сервер). 0 — не на огне.</summary>
        public float FireC { get; private set; }
        /// <summary>Порций жидкости внутри (сервер).</summary>
        public int LiquidPortions { get; private set; }
        /// <summary>Надбавка к точке кипения от давления под крышкой, °C (ставит CookwarePressure, сервер).</summary>
        public float BoilBonusC { get; set; }
        /// <summary>Точка кипения сейчас (с давлением), °C. Сервер.</summary>
        public float BoilPointC => _boilC + BoilBonusC;
        /// <summary>Кипит ли на сервере (жидкость, огонь, точка кипения).</summary>
        public bool ServerBoiling { get; private set; }

        /// <summary>Температура содержимого, °C (сервер — точная, клиенты — по сети).</summary>
        public float TemperatureC => base.IsServerInitialized ? _t : _tempNet.Value;

        /// <summary>Кипит ли (жидкость и ≥ кипения; карамель — с 160 °C).</summary>
        public bool IsBoiling => TemperatureC >= BoilNowC - 0.6f && (SyrupOnly || HasLiquidShown());

        // ───────── карамель («сироп») ─────────
        // Карамель — не вода: не упирается в 100 °C, кипит с Caramel From (160 °C), ниже — не кипит.
        // Считается по названию («…карамел…»), одинаково на сервере и у клиентов.
        private int _syrupN, _waterN;
        private float _countAt = -1f;

        private static bool IsSyrup(ProductDef p) =>
            p != null && !string.IsNullOrEmpty(p.Title) && p.Title.ToLowerInvariant().Contains("карамел");

        private void RecountSyrup()
        {
            if (Time.time < _countAt || _cw == null) return;
            _countAt = Time.time + 0.2f;
            FoodDatabase db = FoodDatabase.Instance;
            int s = 0, w = 0;
            for (int i = 0; i < _cw.ViewCount; i++)
            {
                BagView v = _cw.GetView(i);
                if (v.Count <= 0) continue;
                ProductDef d = db != null ? db.Get(v.Product) : null;
                if (IsSyrup(d)) s += v.Count;
                else if (IsLiquid(v.Product, v.State, db)) w += v.Count;
            }
            _syrupN = s; _waterN = w;
        }

        /// <summary>В таре карамель и нет воды — кипит с 160 °C, а не с 100.</summary>
        public bool SyrupOnly { get { RecountSyrup(); return _syrupN > 0 && _waterN == 0; } }

        /// <summary>Точка кипения содержимого сейчас: карамель — Caramel From, вода — Boil Point.</summary>
        public float BoilNowC => SyrupOnly ? _caramelFromC : BoilPointC;

        /// <summary>Статус карамели для анализатора (F4) и лога. null — в таре нет сахара/карамели под рецепт.</summary>
        public string CaramelStatus()
        {
            if (_cw == null) return null;
            float t = TemperatureC;
            bool cooking = CookRecipeBook.Wants(_cw, CookStepKind.Caramelize);
            RecountSyrup();
            if (cooking && _syrupN == 0)
            {
                if (t < _caramelFromC)
                    return $"Карамель: сахар {t:0} °C — карамелизуется с {_caramelFromC:0} °C" +
                           (FireC > 0f && FireC < _caramelFromC + 5f ? $" (огонь {FireC:0} °C — раздуй F3)" : FireC <= 0f ? " (поставь на огонь)" : "");
                return $"Карамель: {t:0} °C — КАРАМЕЛИЗУЕТСЯ";
            }
            if (_syrupN > 0)
            {
                string name = "карамель";
                FoodDatabase db = FoodDatabase.Instance;
                for (int i = 0; i < _cw.ViewCount; i++)
                {
                    ProductDef d = db != null ? db.Get(_cw.GetView(i).Product) : null;
                    if (IsSyrup(d)) { name = d.Title; break; }
                }
                if (name.ToLowerInvariant().Contains("горел")) return $"{name}: {t:0} °C — пригорела, вычерпай и свари заново";
                if (t >= _scorchFromC) return $"{name}: {t:0} °C — ПРИГОРАЕТ! Сними с огня или перечерпай мастерком в ведро";
                if (t >= _caramelFromC) return $"{name}: {t:0} °C — кипит, готова. Пригорит с {_scorchFromC:0} °C — сними или перечерпай в ведро";
                return $"{name}: {t:0} °C — остывает, не пригорит";
            }
            return null;
        }

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _lv = GetComponentInChildren<CookwareLiquidVisual>(true);
            _lid = GetComponent<PotLidSocket>();
            _t = _ambientC;
            if (GetComponent("PotThermal") != null)
                Debug.LogWarning($"[Нагрев] «{name}»: на кастрюле ещё старый PotThermal — убери его, иначе костёр греет через него, а не через CookwareHeat", this);
        }

        private float _overheatLogAt;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _t = _ambientC;
            _lastTotal = _cw != null ? _cw.TotalCount : 0;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (base.IsServerInitialized) ServerTick(dt);
            if (!base.IsClientInitialized && base.IsServerInitialized) return;   // выделенный сервер — без картинки
            UpdateFx(dt);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void ServerTick(float dt)
        {
            if (_cw == null || dt <= 0f) return;

            float fire = _cw.ExternalTemperatureC;
            if (fire <= 0f && _cw.HeatOn) fire = _stoveC;
            bool onFire = fire > _ambientC + 1f;
            FireC = onFire ? fire : 0f;

            int total = _cw.TotalCount;
            _countAt = -1f; RecountSyrup();
            int liquid = _waterN;                 // вода и прочее жидкое — без карамели
            int syrup = _syrupN;
            LiquidPortions = liquid;
            float boilAt = BoilPointC;

            // Положили холодное — смесь остывает (по количеству порций).
            if (total > _lastTotal && total > 0)
                _t = (_t * _lastTotal + _ambientC * (total - _lastTotal)) / total;
            _lastTotal = total;

            float target = onFire ? fire : _ambientC;
            float mass = 1f + total / _portionsPerMass;
            float k = (target > _t ? _heatSpeed * _heatBoost : _coolSpeed) / mass;
            float wasT = _t;
            _t = Mathf.Lerp(_t, target, 1f - Mathf.Exp(-k * dt));
            if (liquid > 0 && _t > boilAt) _t = boilAt;     // вода выше кипения не греется (под крышкой — выше)

            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(_t), 0, 255);
            if (b != _tempNet.Value) _tempNet.Value = b;

            bool waterBoil = liquid > 0 && onFire && _t >= boilAt - 0.5f;
            bool syrupBoil = liquid == 0 && syrup > 0 && _t >= _caramelFromC;     // карамель кипит с 160 °C, пока горячая
            bool boiling = waterBoil || syrupBoil;
            ServerBoiling = boiling;
            if (boiling)
            {
                _cw.ServerAgitate(_boilAgitation * (syrupBoil ? 0.6f : 1f) * Random.Range(0.75f, 1f));
                if (_recipeStep && waterBoil) CookRecipeBook.ServerProcess(_cw, CookStepKind.Heat, dt);
            }
            else if (liquid > 0 && _t > 85f)
                _cw.ServerAgitate(_boilAgitation * 0.3f * Mathf.InverseLerp(85f, _boilC, _t));   // «шумит» перед кипением

            // Сухое на огне (какао-крупка в ковше) — шаг рецепта «Обжарить».
            if (_recipeStep && liquid == 0 && total > 0 && onFire && _t >= _roastFromC)
                CookRecipeBook.ServerProcess(_cw, CookStepKind.Roast, dt);

            // Тёплое на огне — шаг рецепта «Растопить» (шоколад с маслом). Только если в таре основа такого рецепта.
            if (_recipeStep && total > 0 && onFire && _t >= _meltFromC && CookRecipeBook.Wants(_cw, CookStepKind.Melt))
                CookRecipeBook.ServerProcess(_cw, CookStepKind.Melt, dt);

            // Сухой сахар — карамель (окно температуры: с 160 °C варится, с 200 °C пригорает — даже снятая, пока горячая).
            if (_recipeStep && total > 0 && onFire && _t >= _caramelFromC && CookRecipeBook.Wants(_cw, CookStepKind.Caramelize))
                CookRecipeBook.ServerProcess(_cw, CookStepKind.Caramelize, dt);
            if (_recipeStep && total > 0 && _t >= _scorchFromC && CookRecipeBook.Wants(_cw, CookStepKind.Scorch))
            {
                if (_log && Time.time >= _scorchLogAt) { _scorchLogAt = Time.time + 2f; Debug.Log($"[Нагрев] «{name}»: {_t:0} °C — карамель ПРИГОРАЕТ, сними с огня!", this); }
                CookRecipeBook.ServerProcess(_cw, CookStepKind.Scorch, dt);
            }

            // Борщ: жарить (пассеровка), тушить (свёкла, зажарка), уваривать на слабом огне (томатная паста).
            if (_recipeStep && total > 0 && onFire)
            {
                // жарить и тушить — только в сковороде (кастрюля с бульоном не «жарит» лук)
                bool pan = _cw.Def != null && (_cw.Def.Kind == CookwareKind.Flat || _cw.Def.HeatProcess == FoodProcess.Fry);
                if (pan && _t >= _fryFromC && CookRecipeBook.Wants(_cw, CookStepKind.Fry))
                    CookRecipeBook.ServerProcess(_cw, CookStepKind.Fry, dt);
                if (pan && _t >= _stewFromC && CookRecipeBook.Wants(_cw, CookStepKind.Stew))
                    CookRecipeBook.ServerProcess(_cw, CookStepKind.Stew, dt);
                bool simmer = CookRecipeBook.Wants(_cw, CookStepKind.Simmer);
                if (fire > _simmerMaxFireC && (simmer || CookRecipeBook.Wants(_cw, CookStepKind.Burn)))
                {
                    if (_log && Time.time >= _overheatLogAt)
                    {
                        _overheatLogAt = Time.time + 3f;
                        Debug.Log($"[Нагрев] «{name}»: огонь {fire:0} °C — слишком сильный для уваривания (до {_simmerMaxFireC:0}), ПРИГОРАЕТ! Не раздувай", this);
                    }
                    CookRecipeBook.ServerOverheat(_cw, dt);
                }
                else if (simmer && _t >= _simmerFromC)
                    CookRecipeBook.ServerProcess(_cw, CookStepKind.Simmer, dt);
                // Телятина по-орловски: бешамель — варить помешивая (миксером), иначе комки
                if (_t >= _simmerFromC && CookRecipeBook.Wants(_cw, CookStepKind.SimmerStir))
                    CookRecipeBook.ServerProcess(_cw, CookStepKind.SimmerStir, dt);
            }

            // Шаг рецепта «Выдержать» идёт сам, просто по времени (острый соус после варки).
            if (_recipeStep)
            {
                CookwareJournal j = CookwareJournal.Peek(_cw);
                if (j != null && j.Recipe != null && j.StepIndex < j.Recipe.Steps.Count &&
                    j.Recipe.Steps[j.StepIndex].Kind == CookStepKind.Rest)
                    CookRecipeBook.ServerProcess(_cw, CookStepKind.Rest, dt);
            }

            if (_log)
            {
                string caramel = CaramelStatus();
                if (boiling != _wasBoiling)
                {
                    if (syrup > 0 && liquid == 0)
                        Debug.Log(boiling ? $"[Нагрев] «{name}»: карамель кипит ({_t:0} °C) — {caramel}" : $"[Нагрев] «{name}»: карамель перестала кипеть ({_t:0} °C < {_caramelFromC:0})", this);
                    else
                        Debug.Log(boiling ? $"[Нагрев] «{name}»: ЗАКИПЕЛО" : $"[Нагрев] «{name}»: кипение прекратилось ({_t:0} °C)", this);
                }
                else if ((onFire || caramel != null) && Time.time >= _logAt && Mathf.Abs(_t - wasT) > 0f)
                {
                    _logAt = Time.time + (caramel != null ? 3f : 5f);
                    string dry = caramel != null ? " — " + caramel
                        : liquid == 0 && total > 0
                        ? (_t >= _roastFromC ? " — сухое, ЖАРИТСЯ" : $" — сухое, жарка с {_roastFromC:0} °C" + (fire < _roastFromC ? " (огонь слабый — раздуй/добавь досок)" : ""))
                        : "";
                    Debug.Log($"[Нагрев] «{name}»: {_t:0} °C (огонь {fire:0} °C, внутри {total} порц., жидкость {liquid}){dry}", this);
                }
            }
            _wasBoiling = boiling;
        }

        /// <summary>Сколько порций жидкости в таре (сервер).</summary>
        private int CountLiquid()
        {
            FoodDatabase db = FoodDatabase.Instance;
            int n = 0;
            for (int i = 0; i < _cw.ViewCount; i++)
            {
                BagView v = _cw.GetView(i);
                if (v.Count > 0 && IsLiquid(v.Product, v.State, db)) n += v.Count;
            }
            return n;
        }

        private bool IsLiquid(ushort product, FoodState state, FoodDatabase db)
        {
            if (_lv != null && _lv.TryGetRole(product, out CookwareLiquidVisual.Role role))
                return role == CookwareLiquidVisual.Role.Liquid || role == CookwareLiquidVisual.Role.TopLayer;
            return FoodHeapLook.IsLiquid(db != null ? db.Get(product) : null, state);
        }

        // ───────────────────────── ВИД ─────────────────────────

        private bool HasLiquidShown() => _lv != null && _lv.HasLiquid && _lv.CanPour;

        private BoilFx _fx;
        private float _fxScale = 1f;

        private void UpdateFx(float dt)
        {
            if (_cw == null) return;
            if (_prettyFx && _fx == null) _fx = BoilFx.For(transform);
            // Масштаб эффектов под размер кастрюли: все размеры заданы для кастрюли радиусом 15 см.
            float rim = _lv != null ? _lv.RadiusAtRimWorld : 0.15f;
            _fxScale = Mathf.Clamp(rim / 0.15f, 1f, 6f);
            if (_fx != null) _fx.SetScale(_fxScale);
            UpdateBoilSurface();
            float t = TemperatureC;
            bool syrup = SyrupOnly;                         // карамель: кипит с 160 °C, ниже — тихо
            bool liquid = HasLiquidShown() || syrup;
            float bc = BoilNowC;
            HeatAirFx(dt, t, liquid);

            // Налили в раскалённую пустую кастрюлю — шипение, пар столбом.
            if (liquid && !syrup && _shownPrev > _drySmokeFromC && t <= _boilC + 5f)
                SteamBurst(14);
            _shownPrev = t;

            if (liquid && _lid != null && _lid.HasLid)
            {
                // Под крышкой: поверхности не видно — пар бьёт из-под края крышки.
                _bubbleAcc = 0f; _smokeAcc = 0f;
                float st = syrup ? (t >= bc ? 1f : 0f) : Mathf.InverseLerp(_steamFromC + 20f, _boilC, t);
                _steamAcc += dt * _steamPerSecond * 0.6f * st * (syrup ? 0.4f : 1f);
                if (_steamAcc >= 1f) { _steamAcc -= 1f; EmitLidSteam(st); }
                return;
            }

            if (liquid)
            {
                float bub = syrup ? 0f : Mathf.InverseLerp(_bubblesFromC, bc, t);   // карамель: до 160 °C не пузырится вовсе
                bool boiling = t >= bc - 0.6f;
                float rate = _bubblesPerSecond * (boiling ? (syrup ? 0.6f : 1f) : bub * bub * 0.45f);
                _bubbleAcc += dt * rate * Mathf.Clamp(_fxScale, 1f, 5f);     // большая кастрюля — больше пузырей
                int n = Mathf.Min(30, (int)_bubbleAcc);
                _bubbleAcc -= n;
                for (int i = 0; i < n; i++) EmitBubble(boiling ? Random.Range(0.45f, 1f) : Random.Range(0.15f, 0.4f));

                float st = syrup ? 0f : Mathf.InverseLerp(_steamFromC, bc, t);
                float srate = _steamPerSecond * (boiling ? (syrup ? 0.35f : 1f) : st * 0.5f);
                _steamAcc += dt * srate;
                if (_steamAcc >= 1f) { _steamAcc -= 1f; EmitSteam(boiling ? 1f : 0.4f + st * 0.4f); }
                _smokeAcc = 0f;
            }
            else
            {
                _bubbleAcc = _steamAcc = 0f;
                if (t > _drySmokeFromC)
                {
                    float k = Mathf.InverseLerp(_drySmokeFromC, _drySmokeFromC + 100f, t);
                    _smokeAcc += dt * _smokePerSecond * (0.35f + k);
                    if (_smokeAcc >= 1f) { _smokeAcc -= 1f; EmitDrySmoke(k); }
                }
                else _smokeAcc = 0f;
            }
        }

        /// <summary>Бурлящая поверхность: с 75 °C «шумит», к кипению — волны и белые гребни.</summary>
        private void UpdateBoilSurface()
        {
            if (_fx == null) return;
            bool liquid = HasLiquidShown() && !(_lid != null && _lid.HasLid);
            float t = TemperatureC;
            bool syrup = SyrupOnly;
            float bc = BoilNowC;
            float k = liquid && !syrup ? Mathf.Clamp01(Mathf.InverseLerp(75f, bc, t)) : 0f;      // карамель: до 160 °C гладкая
            k = k * k * 0.6f + (t >= bc - 0.6f && liquid ? (syrup ? 0.55f : 0.4f) : 0f);
            Vector3 c = _cw.MouthPoint;
            float r = _lv != null ? _lv.RadiusAtRimWorld * 0.95f : 0.15f;
            if (_lv != null && _lv.TryGetSurfaceY(c, out float y)) c.y = y; else k = 0f;
            _fx.Surface(c, r, k, _lv != null ? _lv.LiquidColor : Color.white);
        }

        /// <summary>Точка на поверхности жидкости (случайная, в пределах горловины).</summary>
        private bool SurfacePoint(float spread, out Vector3 at)
        {
            Vector3 c = _cw.MouthPoint;
            float r = _lv != null ? _lv.RadiusAtRimWorld : 0.15f;
            Vector2 d = Random.insideUnitCircle * r * spread;
            at = c + new Vector3(d.x, 0f, d.y);
            if (_lv != null && _lv.TryGetSurfaceY(at, out float y)) { at.y = y; return true; }
            return false;
        }

        private void EmitBubble(float k)
        {
            if (!SurfacePoint(0.8f, out Vector3 at)) return;
            Color c = _lv != null ? _lv.LiquidColor : Color.white;
            if (_fx != null)
            {
                float size = _bubbleSize * _fxScale * (0.6f + 1.3f * k) * Random.Range(0.7f, 1.4f);
                // Изредка при бурном кипении — большой «бульк».
                if (k > 0.6f && Random.value < 0.08f) size *= 2.2f;
                _fx.Bubble(at, size, c);
                // Пузырь толкает воду — по поверхности бежит кольцо.
                if (_lv != null && k > 0.5f && Random.value < 0.45f) _lv.Ripple(at);
                return;
            }
            LiquidFx.Bubble(at, Vector3.up, _bubbleSize * k, c, _lv != null ? _lv.ContainerVelocity : Vector3.zero);
        }

        private void EmitSteam(float k)
        {
            if (!SurfacePoint(0.55f, out Vector3 at)) at = _cw.MouthPoint;
            // Пар рождается НАД горловиной (не у воды внутри): крупные клубы иначе торчат сквозь стенки.
            at.y = Mathf.Max(at.y, _cw.MouthPoint.y) + _steamScale * _fxScale * _steamSizeK * 0.35f;
            Puff(at, new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(0.25f, 0.5f), Random.Range(-0.05f, 0.05f)),
                 _steamScale * Random.Range(0.7f, 1.2f) * (0.6f + 0.4f * k), 0.25f + 0.25f * k, _steamColor);
        }

        private void EmitLidSteam(float k)
        {
            Transform s = _lid.Socket;
            float r = _lv != null ? _lv.RadiusAtRimWorld : 0.15f;
            float a = Random.value * Mathf.PI * 2f;
            Vector3 side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 at = s.position + side * r;
            Puff(at, side * Random.Range(0.15f, 0.35f) + Vector3.up * Random.Range(0.1f, 0.3f),
                 _steamScale * Random.Range(0.4f, 0.7f), 0.2f + 0.2f * k, _steamColor);
        }

        private void SteamBurst(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!SurfacePoint(0.6f, out Vector3 at)) at = _cw.MouthPoint;
                at.y = Mathf.Max(at.y, _cw.MouthPoint.y) + _steamScale * _fxScale * _steamSizeK * 0.3f;
                Puff(at, new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.5f, 1.1f), Random.Range(-0.15f, 0.15f)),
                     _steamScale * Random.Range(0.9f, 1.6f), 0.6f, _steamColor);
            }
            if (_lv != null) _lv.Ripple(_cw.MouthPoint);
        }

        private SurfaceHeatGlow _glow;
        private bool _glowChecked;
        private float _lightWispAcc;

        /// <summary>v14: марево над горячей тарой и лёгкий парок у сухой (до дыма).</summary>
        private void HeatAirFx(float dt, float t, bool liquid)
        {
            if (!_glowChecked) { _glowChecked = true; _glow = GetComponent<SurfaceHeatGlow>(); }
            float rim = _lv != null ? _lv.RadiusAtRimWorld : 0.15f * _fxScale;
            Vector3 mouth = _cw.MouthPoint;
            bool lid = _lid != null && _lid.HasLid;
            if (_haze && _glow == null && t > _hazeFromC && !lid)                     // у ковша с раскалённым металлом марево рисует SurfaceHeatGlow
            {
                float k = Mathf.InverseLerp(_hazeFromC, _hazeFullC, t) * _hazeStrength;
                Vector3 bottom = mouth;
                if (liquid) { if (_lv != null && _lv.TryGetSurfaceY(mouth, out float y)) bottom.y = y; }
                else bottom.y -= _bottomDepth * _fxScale * 0.7f;                     // пустая — от дна
                CCHeatFx.Haze(bottom, rim * 2.2f, rim * 3.2f + (mouth.y - bottom.y), k * (liquid ? 0.6f : 1f));
            }
            if (!liquid && !lid && t > _lightWispFromC && t <= _drySmokeFromC + 40f)
            {
                float k = Mathf.InverseLerp(_lightWispFromC, _drySmokeFromC + 40f, t);
                _lightWispAcc += dt * (0.8f + 2.2f * k);
                if (_lightWispAcc >= 1f)
                {
                    _lightWispAcc -= 1f;
                    Vector2 d = Random.insideUnitCircle * rim * 0.55f;
                    Vector3 at = mouth + new Vector3(d.x, -_bottomDepth * _fxScale * 0.5f, d.y);
                    float s = rim * Random.Range(0.5f, 0.9f);
                    Color c = _lightWispColor; c.a *= 0.6f + 0.8f * k;
                    CCHeatFx.Wisp(at, new Vector3(Random.Range(-0.03f, 0.03f), Random.Range(0.2f, 0.4f), Random.Range(-0.03f, 0.03f)) * Mathf.Sqrt(_fxScale),
                                  s, c, Random.Range(1.1f, 1.7f));
                }
            }
            else _lightWispAcc = 0f;
        }

        private void EmitDrySmoke(float k)
        {
            Vector3 c = _cw.MouthPoint;
            float r = _lv != null ? _lv.RadiusAtRimWorld * 0.6f : 0.1f;
            Vector2 d = Random.insideUnitCircle * r;
            Vector3 at = c + new Vector3(d.x, -_bottomDepth * _fxScale, d.y);
            Color col = Color.Lerp(_smokeColor, _smokeColor * 0.55f, k);
            col.a = 1f;
            Puff(at, new Vector3(Random.Range(-0.04f, 0.04f), Random.Range(0.15f, 0.35f), Random.Range(-0.04f, 0.04f)),
                 _steamScale * Random.Range(0.6f, 1f), 0.2f + 0.25f * k, col);
        }

        private void Puff(Vector3 at, Vector3 vel, float scale, float amount, Color color)
        {
            scale *= _fxScale * _steamSizeK;
            vel *= Mathf.Sqrt(_fxScale);
            if (_fx != null)
            {
                _fx.Steam(at, vel * 0.8f, scale * Random.Range(0.75f, 1.15f), Mathf.Clamp01(0.15f + amount * 0.5f), color);
                return;
            }
            CatCooksDustSystem ds = CatCooksDustSystem.Active;
            if (ds != null)
                ds.Emit(CatCooksDustSystem.Effect.Smoke, at, Vector3.up, vel, 0.3f, null, scale, amount, color);
            else
                CCRuntimeParticles.Burst(at, Vector3.up, color, Color.white, 1, CCRuntimeParticles.Look.Dust.Scaled(scale));
        }
    }
}
