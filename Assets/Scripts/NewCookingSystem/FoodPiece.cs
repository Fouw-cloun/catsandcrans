using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Перехватчик работы над продуктом. Если на объекте есть компонент с этим
    /// интерфейсом, FoodPiece сперва отдаёт работу ему.
    ///
    /// Зачем: у картошки из слайсов «Резать» — это не смена состояния целиком,
    /// а выпадение слайсов по одному (FoodSliceable). Любой инструмент с
    /// процессом «Резать» — FoodTool-зона или бензопила — придёт сюда и
    /// сам ничего про слайсы знать не будет.
    /// </summary>
    public interface IFoodWorkHandler
    {
        /// <summary>true — работа принята и обработана здесь, FoodPiece её не трогает.</summary>
        bool ServerHandleWork(FoodPiece piece, FoodProcess process, float seconds, float quality01);
        /// <summary>Работу прекратили (вынули из зоны).</summary>
        void ServerStopWork(FoodPiece piece);
    }

    /// <summary>
    /// ПКМ у продукта в руке. Пример: пампушку ПКМ по решётке духовки — кладём ровно рядом с соседками.
    /// true — обработано. Сервер.
    /// </summary>
    public interface IFoodSecondaryHandler
    {
        bool ServerHandleSecondary(FoodPiece piece, UseContext ctx);
    }

    /// <summary>
    /// ПРОДУКТ В МИРЕ — СЕТЕВАЯ ВЕРСИЯ.
    ///
    /// ═══ ЧЕМ ОТЛИЧАЕТСЯ ОТ ПРОШЛОГО FoodPiece ═══
    ///
    /// Прошлый был обычным MonoBehaviour: в кооперативе каждый клиент варил
    /// свою картошку сам, и через минуту у хоста она была варёной, а у друга
    /// сырой. Теперь правда одна — на сервере. Клиенты видят SyncVar и ничего
    /// не считают.
    ///
    ///   сервер   — CookTimer, переходы, шкалы, грязь при падении
    ///   все      — SyncVar: продукт, состояние, три шкалы, прогресс байтом
    ///
    /// ═══ ПОЧЕМУ НАСЛЕДНИК PickupItem ═══
    ///
    /// Продукт берут в руку, носят и бросают — это уже умеет PickupItem,
    /// включая составные коллайдеры и гашение их в руке у ВСЕХ клиентов.
    /// Заряженный бросок по ЛКМ — как у старого FoodItem.
    ///
    /// ═══ API ДЛЯ СТАРЫХ ОБРАБОТЧИКОВ ═══
    ///
    /// Пила, отбойник, болгарка, мойка звали у FoodItem:
    ///     CanApplyNow(ProcessDef)  и  ServerAddProgress(ProcessDef, delta01, quality)
    /// Здесь то же самое на FoodProcess:
    ///     CanApplyNow(FoodProcess) и  ServerAddWork(FoodProcess, seconds, quality)
    /// Имя второго метода ИЗМЕНЕНО НАМЕРЕННО: раньше передавалась доля 0..1,
    /// теперь секунды работы. Компилятор заставит пройтись по каждому вызову,
    /// и ни один не перенесётся молча с неверным смыслом.
    ///
    /// Требует: NetworkObject, NetworkTransform (Client Authoritative),
    /// Rigidbody, Collider. Рядом — FoodStateVisual.
    /// </summary>
    public class FoodPiece : PickupItem
    {
        [Header("Что это")]
        [Tooltip("Продукт. По сети летит его Id, а не ссылка.")]
        [SerializeField] private ProductDef _product;

        [Tooltip("Сколько порций в этом куске")]
        [Range(1, 255)][SerializeField] private int _count = 1;

        [Tooltip("Состояние при появлении. «— нет —» = стартовое состояние группы \n" +
                 "(у овощей «Грязное»).\n\n" +
                 "Здесь ставят исключения: гнилая картошка из ящика — «Порча/Испортилось», \n" +
                 "уже мытая для теста — «Подготовка/Мытое».")]
        [SerializeField] private FoodState _startState = FoodState.None;

        [Header("Бросок (ЛКМ зажать-отпустить)")]
        [SerializeField] private float _minThrowForce = 3f;
        [SerializeField] private float _maxThrowForce = 16f;

        [Header("Грязь при падении")]
        [Tooltip("С какой скорости удара продукт пачкается, м/с")]
        [SerializeField] private float _dirtySpeed = 3f;
        [Tooltip("Насколько падает чистота за удар, 0..1")]
        [Range(0f, 1f)][SerializeField] private float _dirtyPerHit = 0.35f;
        [Tooltip("Первые секунды после спавна падения не считаются: продукт \n" +
                 "появился над столом и приземлился — это не вина игрока.")]
        [SerializeField] private float _spawnGrace = 1.5f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        // ───────── сеть ─────────
        private readonly SyncVar<ushort> _productId = new SyncVar<ushort>(0);
        private readonly SyncVar<byte> _state = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _countNet = new SyncVar<byte>(1);
        private readonly SyncVar<byte> _clean = new SyncVar<byte>(100);
        private readonly SyncVar<byte> _cook = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _formScore = new SyncVar<byte>(100);
        /// <summary>Прогресс до готовности 0..255. Огрублён: полоске хватает.</summary>
        private readonly SyncVar<byte> _progress = new SyncVar<byte>(0);

        // ───────── только сервер ─────────
        private CookTimer _timer;
        private float _qualitySum, _qualityWeight;
        private bool _held;
        private IFoodWorkHandler _handler;
        private bool _handlerSearched;
        private bool _consumed;
        private float _spawnTime;
        private float _cleanCarry;

        // Значения, заданные ДО спавна (отрезанный кусок наследует состояние).
        private bool _preset;
        private FoodState _presetState;
        private byte _presetClean = 100, _presetCook, _presetForm = 100;

        /// <summary>Состояние сменилось. Приходит на КАЖДОМ клиенте один раз
        /// (на хосте — тоже один, а не два). prev, next.</summary>
        public event Action<FoodState, FoodState> StateChanged;

        /// <summary>Сменились шкалы или прогресс — для полосок и подсказок.</summary>
        public event Action ScalesChanged;

        // ───────── чтение, работает везде ─────────
        public ushort ProductId => _productId.Value;
        public FoodState State => (FoodState)_state.Value;
        public byte StateIndex => _state.Value;
        public int Count => _countNet.Value;
        public byte Clean => _clean.Value;
        public byte Cook => _cook.Value;
        public byte FormScore => _formScore.Value;
        public float Progress01 => _progress.Value / 255f;
        public bool IsConsumed => _consumed;

        public ProductDef Def
        {
            get
            {
                FoodDatabase db = FoodDatabase.Instance;
                ProductDef d = db != null ? db.Get(_productId.Value) : null;
                return d != null ? d : _product;
            }
        }

        public override bool ChargedPrimaryUse => true;

        // ───────────────────────── ЖИЗНЕННЫЙ ЦИКЛ ─────────────────────────

        public override void OnStartServer()
        {
            base.OnStartServer();
            _spawnTime = Time.time;

            if (_product == null && _productId.Value == 0)
            {
                Debug.LogError($"[Еда] «{name}»: не назначен ProductDef", this);
                return;
            }
            if (_product != null) _productId.Value = _product.Id;

            _countNet.Value = (byte)Mathf.Clamp(_count, 1, 255);

            if (_preset)
            {
                _state.Value = (byte)_presetState;
                _clean.Value = _presetClean;
                _cook.Value = _presetCook;
                _formScore.Value = _presetForm;
            }
            else if (_startState != FoodState.None)
            {
                _state.Value = (byte)_startState;
            }
            else
            {
                ProductDef d = Def;
                _state.Value = (byte)(d != null ? d.StartState : FoodState.Raw);
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            FoodLookCache.Note(this);            // вид куска — для «кусков в супе» (CookwareFloaters)
            _state.OnChange += OnStateSync;
            _clean.OnChange += OnScaleSync;
            _cook.OnChange += OnScaleSync;
            _formScore.OnChange += OnScaleSync;
            _progress.OnChange += OnScaleSync;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            FoodLookCache.Note(this);            // проглотила тара — вид остаётся плавать в супе
            _state.OnChange -= OnStateSync;
            _clean.OnChange -= OnScaleSync;
            _cook.OnChange -= OnScaleSync;
            _formScore.OnChange -= OnScaleSync;
            _progress.OnChange -= OnScaleSync;
        }

        private void OnStateSync(byte prev, byte next, bool asServer)
        {
            // На хосте колбэк приходит дважды: серверный и клиентский.
            // Визуалу нужен один — клиентский. На выделенном сервере — серверный.
            if (asServer && base.IsClientInitialized) return;
            StateChanged?.Invoke((FoodState)prev, (FoodState)next);
        }

        private void OnScaleSync(byte prev, byte next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ScalesChanged?.Invoke();
        }

        /// <summary>
        /// Задать состояние ДО Spawn. Для отрезанных кусков: кусок от чищеной
        /// картошки обязан быть чищеным, а не начинать с грязного.
        ///
        /// БЕЗ атрибута [Server] намеренно: объект в этот момент ещё НЕ
        /// заспавнен, и FishNet отклонил бы вызов как «сервер не запущен» —
        /// кусок родился бы без продукта («не назначен ProductDef» в логе).
        /// Метод вызывает только серверный код, перед Spawn.
        /// </summary>
        public void ServerPreset(ProductDef product, FoodState state, byte clean, byte cook, byte form)
        {
            if (product != null) _product = product;
            _preset = true;
            _presetState = state;
            _presetClean = clean;
            _presetCook = cook;
            _presetForm = form;
        }

        // ───────────────────────── РУКА И БРОСОК ─────────────────────────

        public override void ServerOnPickup(PlayerHand hand)
        {
            base.ServerOnPickup(hand);
            _held = true;
            // Взял из-под инструмента — процесс прерван, фиксируем, что вышло.
            ServerStopWork();
        }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            base.ServerOnDrop(hand, tossDirection, force);
            _held = false;
        }

        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (ctx.Hand == null || ctx.Hand.HeldItem != this) return;
            float force = Mathf.Lerp(_minThrowForce, _maxThrowForce, ctx.Charge);
            ctx.Hand.ServerDrop(ctx.Direction, force);
        }

        /// <summary>ПКМ — свой у продукта (пампушка: положить на решётку), иначе ничего.</summary>
        public override void ServerSecondaryUse(UseContext ctx)
        {
            if (ctx.Hand == null || ctx.Hand.HeldItem != this) return;
            IFoodSecondaryHandler h = GetComponent<IFoodSecondaryHandler>();
            if (h != null && h.ServerHandleSecondary(this, ctx)) return;
            base.ServerSecondaryUse(ctx);
        }

        private IFoodWorkHandler Handler
        {
            get
            {
                if (!_handlerSearched) { _handler = GetComponent<IFoodWorkHandler>(); _handlerSearched = true; }
                return _handler;
            }
        }

        /// <summary>В руке у игрока. Тара не глотает то, что держат.</summary>
        public bool IsHeld => _held;

        // ───────────────────────── ОБРАБОТКА ─────────────────────────

        /// <summary>
        /// Есть ли ход по таблице. Работает и на клиенте — для подсветки
        /// инструмента и подсказки «что будет».
        /// </summary>
        public bool CanApplyNow(FoodProcess process)
        {
            if (_consumed || process == FoodProcess.None) return false;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return false;

            // Уже в окне этим же процессом — продолжать можно, хотя правила
            // из «Варёного» нет. Именно это и сломало окно в прошлой версии.
            if (_timer.IsRunning && _timer.Process == process && _timer.InWindow) return true;

            return db.CanApply(_productId.Value, State, process);
        }

        /// <summary>
        /// Поработать над продуктом. Зовётся каждый кадр контакта.
        /// seconds — секунды работы (Time.deltaTime × скорость инструмента).
        /// quality01 — насколько хорошо работает инструмент прямо сейчас
        /// (обороты, давление струи). Идёт в шкалу «форма» для ручных операций.
        /// Возвращает true в кадр, когда СМЕНИЛОСЬ состояние.
        /// </summary>
        [Server]
        public bool ServerAddWork(FoodProcess process, float seconds, float quality01 = 1f)
        {
            if (_consumed || seconds <= 0f) return false;

            IFoodWorkHandler h = Handler;
            if (h != null && h.ServerHandleWork(this, process, seconds, quality01)) return false;

            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return false;

            if (_timer.Process != process) { _qualitySum = 0f; _qualityWeight = 0f; }
            _qualitySum += Mathf.Clamp01(quality01) * seconds;
            _qualityWeight += seconds;

            FoodState state = State;
            FoodState was = state;
            CookEvent ev = CookTimer.Tick(ref _timer, ref state, _productId.Value, process,
                                          seconds, db.Table, out float acc);

            switch (ev)
            {
                case CookEvent.NotApplicable:
                    SetProgress(0f);
                    return false;

                case CookEvent.Done:
                    if (IsHandShaping(process))
                        _formScore.Value = ToByte(_qualityWeight > 0f ? _qualitySum / _qualityWeight : 1f);
                    break;

                case CookEvent.Ready:
                case CookEvent.None:
                    if (_timer.InWindow) _cook.Value = ToByte(acc);
                    break;

                case CookEvent.Spoiled:
                    _cook.Value = 0;
                    break;
            }

            SetProgress(CookTimer.Progress01(_timer, _productId.Value, db.Table));
            return CommitState(was, state);
        }

        /// <summary>
        /// Работа прекращена: вынули из зоны инструмента, взяли в руку.
        /// Здесь рождается недоготовленное — это решение игрока.
        /// </summary>
        [Server]
        public void ServerStopWork()
        {
            IFoodWorkHandler h = Handler;
            if (h != null) h.ServerStopWork(this);
            if (!_timer.IsRunning) return;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) { _timer.Clear(); return; }

            bool wasInWindow = _timer.InWindow;
            FoodState state = State;
            FoodState was = state;
            CookEvent ev = CookTimer.Stop(ref _timer, ref state, _productId.Value, db.Table, out float acc);

            if (wasInWindow) _cook.Value = ToByte(acc);
            if (ev == CookEvent.Undercooked) _cook.Value = 0;

            SetProgress(0f);
            CommitState(was, state);
        }

        /// <summary>Уронили. Единственная причина грязи.</summary>
        [Server]
        public void ServerDirty(float severity01)
        {
            int v = _clean.Value - Mathf.RoundToInt(Mathf.Clamp01(severity01) * 100f);
            _clean.Value = (byte)Mathf.Max(0, v);
        }

        /// <summary>Помыли. Чистоту вернуть можно, в отличие от прожарки.</summary>
        [Server]
        public void ServerWash() => _clean.Value = 100;

        /// <summary>Отмывать постепенно: струя возвращает чистоту за несколько секунд.</summary>
        [Server]
        public void ServerAddClean(float amount01)
        {
            if (_clean.Value >= 100 || amount01 <= 0f) return;
            _cleanCarry += amount01 * 100f;
            int add = Mathf.FloorToInt(_cleanCarry);
            if (add <= 0) return;
            _cleanCarry -= add;
            _clean.Value = (byte)Mathf.Min(100, _clean.Value + add);
        }

        /// <summary>Сменить продукт и/или состояние уже живого куска (тесто испеклось в булку). Сервер.</summary>
        [Server]
        public void ServerSetProductState(ProductDef product, FoodState state)
        {
            if (product != null) { _product = product; _productId.Value = product.Id; }
            _state.Value = (byte)state;
        }

        /// <summary>Сколько порций в куске. Меняет нарезка: у картошки из 6 слайсов
        /// после каждого отрезанного остаётся на порцию меньше.</summary>
        [Server]
        public void ServerSetCount(int count) => _countNet.Value = (byte)Mathf.Clamp(count, 1, 255);

        /// <summary>Тара проглотила продукт. После этого его деспавнят —
        /// флаг защищает от двойного OnTriggerEnter составного коллайдера.</summary>
        [Server]
        public bool ServerTryConsume()
        {
            if (_consumed) return false;
            _consumed = true;
            _timer.Clear();
            return true;
        }

        // ───────────────────────── ГРЯЗЬ ПРИ ПАДЕНИИ ─────────────────────────

        private void OnCollisionEnter(Collision c)
        {
            if (!base.IsServerInitialized || _held || _consumed) return;
            if (Time.time - _spawnTime < _spawnGrace) return;
            if (c.relativeVelocity.magnitude < _dirtySpeed) return;

            // В таре и на другой еде не пачкается: грязь — это пол и стены.
            if (c.collider.GetComponentInParent<Cookware>() != null) return;
            if (c.collider.GetComponentInParent<FoodPiece>() != null) return;
            if (c.collider.GetComponentInParent<FoodCleanSurface>() != null) return;   // подстилка, стол — чисто

            ServerDirty(_dirtyPerHit);
            if (_log) Debug.Log($"[Еда] {this} — упало, испачкалось", this);
        }

        // ───────────────────────── ВНУТРЕННЕЕ ─────────────────────────

        private bool CommitState(FoodState was, FoodState now)
        {
            if (now == was) return false;
            _state.Value = (byte)now;
            if (FoodStateInfo.IsSpoiled(now)) _cook.Value = 0;
            if (_log)
                Debug.Log($"[{(Def != null ? Def.Title : "?")}] {FoodStateInfo.NameOf(was)} → " +
                          $"{FoodStateInfo.NameOf(now)} (прожарка {_cook.Value})", this);
            return true;
        }

        private void SetProgress(float p01)
        {
            byte p = ToByte(p01, 255f);
            // Не дёргаем SyncVar каждый кадр: десяток картошек под диском
            // забил бы канал. Только заметное изменение, ноль и полная.
            if (p != _progress.Value && (p == 0 || p == 255 || Mathf.Abs(p - _progress.Value) >= 4))
                _progress.Value = p;
        }

        private static byte ToByte(float v01, float scale = 100f)
            => (byte)Mathf.RoundToInt(Mathf.Clamp01(v01) * scale);

        /// <summary>Ручные операции, где важна аккуратность: резать, тереть,
        /// толочь, молоть, раскатывать, формовать (коды 20–25).</summary>
        private static bool IsHandShaping(FoodProcess p)
        {
            byte v = (byte)p;
            return v >= 20 && v <= 25;
        }

        public override string ToString()
        {
            ProductDef d = Def;
            return $"{(d != null ? d.Title : "?")} · {FoodStateInfo.NameOf(State)} · ×{Count} · " +
                   $"чистота {Clean} прожарка {Cook} форма {FormScore}";
        }
    }
}
