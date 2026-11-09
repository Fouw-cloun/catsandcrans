using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Одна позиция содержимого. СЕРВЕРНАЯ правда, по сети не ходит целиком.
    /// У каждой свой таймер — картошка за 20 секунд не сбрасывает мясо на 60.
    /// </summary>
    [Serializable]
    public struct BagEntry
    {
        public ushort Product;
        public FoodState State;
        public int Count;
        [Tooltip("Прожарка 0..100 — точность попадания в окно")]
        public byte Cook;
        [Tooltip("Свой таймер у каждой позиции")]
        public CookTimer Timer;
    }

    /// <summary>
    /// То, что видят клиенты: без таймеров, только результат.
    /// Обновляется, когда что-то реально поменялось, а не каждый кадр.
    /// </summary>
    public struct BagView : IEquatable<BagView>
    {
        public ushort Product;
        public FoodState State;
        public ushort Count;
        public byte Cook;
        /// <summary>Прогресс до готовности 0..255 — для будильника над тарой.</summary>
        public byte Progress;
        /// <summary>Позиция в окне: 0 = вне окна, 1..255 = от нижней границы до верхней.</summary>
        public byte Window;

        public bool Equals(BagView o) =>
            Product == o.Product && State == o.State && Count == o.Count &&
            Cook == o.Cook && Progress == o.Progress && Window == o.Window;
    }

    /// <summary>
    /// СОДЕРЖИМОЕ ТАРЫ. Логика без сети — чтобы её можно было гонять в тестах.
    /// </summary>
    [Serializable]
    public class ContentBag
    {
        public List<BagEntry> Entries = new List<BagEntry>();

        public int TotalCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Entries.Count; i++) n += Entries[i].Count;
                return n;
            }
        }

        public bool IsEmpty => Entries.Count == 0;

        /// <summary>Добавить. Складываются только одинаковые И не начавшие
        /// готовиться: иначе сырая картошка унаследовала бы таймер варёной.</summary>
        public void Add(ushort product, FoodState state, int count, byte cook)
        {
            if (count <= 0) return;
            for (int i = 0; i < Entries.Count; i++)
            {
                BagEntry e = Entries[i];
                if (e.Product == product && e.State == state && !e.Timer.IsRunning)
                {
                    e.Cook = WeightedCook(e.Cook, e.Count, cook, count);
                    e.Count += count;
                    Entries[i] = e;
                    return;
                }
            }
            Entries.Add(new BagEntry { Product = product, State = state, Count = count, Cook = cook });
        }

        /// <summary>Прогнать содержимое через процесс. Каждая позиция по своему
        /// таймеру. Возвращает true, если хоть одна сменила состояние.</summary>
        public bool Tick(FoodProcess proc, float dt, FoodTransitionTable table)
        {
            bool changed = false;
            for (int i = 0; i < Entries.Count; i++)
            {
                BagEntry e = Entries[i];
                FoodState s = e.State;

                // Процесс к позиции неприменим — её таймер НЕ трогаем:
                // соль в супе не варится, но и не должна терять ничего.
                if (!e.Timer.IsRunning && !table.TryGet(e.Product, s, proc, out _)) continue;
                if (e.Timer.IsRunning && e.Timer.Process != proc) continue;

                CookEvent ev = CookTimer.Tick(ref e.Timer, ref s, e.Product, proc, dt, table, out float acc);
                if (e.Timer.InWindow || ev == CookEvent.Ready)
                    e.Cook = (byte)Mathf.RoundToInt(Mathf.Clamp01(acc) * 100f);
                if (ev == CookEvent.Spoiled) e.Cook = 0;

                if (s != e.State) { e.State = s; changed = true; }
                Entries[i] = e;
            }
            if (changed) Merge();
            return changed;
        }

        /// <summary>
        /// Схлопнуть одинаковые позиции. Только если таймеры совместимы:
        /// обе стоят, или обе идут одним процессом из одного исходного состояния.
        /// Иначе слияние подменило бы кому-то время готовки.
        /// </summary>
        private void Merge()
        {
            for (int i = 0; i < Entries.Count; i++)
                for (int j = Entries.Count - 1; j > i; j--)
                {
                    BagEntry a = Entries[i], b = Entries[j];
                    if (a.Product != b.Product || a.State != b.State) continue;
                    if (a.Timer.IsRunning != b.Timer.IsRunning) continue;
                    if (a.Timer.IsRunning &&
                        (a.Timer.Process != b.Timer.Process || a.Timer.Origin != b.Timer.Origin ||
                         a.Timer.InWindow != b.Timer.InWindow)) continue;

                    a.Cook = WeightedCook(a.Cook, a.Count, b.Cook, b.Count);
                    a.Count += b.Count;
                    if (b.Timer.Spent > a.Timer.Spent) a.Timer.Spent = b.Timer.Spent;
                    Entries[i] = a;
                    Entries.RemoveAt(j);
                }
        }

        /// <summary>Убрать порции с конца списка — утечка, выливание.</summary>
        public int Remove(int portions)
        {
            int left = portions;
            for (int i = Entries.Count - 1; i >= 0 && left > 0; i--)
            {
                BagEntry e = Entries[i];
                int take = Mathf.Min(e.Count, left);
                e.Count -= take;
                left -= take;
                if (e.Count <= 0) Entries.RemoveAt(i);
                else Entries[i] = e;
            }
            return portions - left;
        }

        public Color BlendColor(FoodTransitionTable table)
        {
            float r = 0f, g = 0f, b = 0f, total = 0f;
            for (int i = 0; i < Entries.Count; i++)
            {
                ProductDef d = table.GetProduct(Entries[i].Product);
                if (d == null) continue;
                float w = Entries[i].Count;
                Color c = FoodStateInfo.IsSpoiled(Entries[i].State) ? d.BurntColor : d.BaseColor;
                r += c.r * w; g += c.g * w; b += c.b * w; total += w;
            }
            if (total <= 0.001f) return Color.gray;
            return new Color(r / total, g / total, b / total);
        }

        public void Clear() => Entries.Clear();

        /// <summary>Хватает ли содержимого на рецепт. State = None в требовании
        /// значит «в любом состоянии».</summary>
        public bool Satisfies(FoodRecipeDef r)
        {
            if (r == null || r.Needs == null || r.Needs.Count == 0) return false;
            for (int n = 0; n < r.Needs.Count; n++)
            {
                FoodNeed need = r.Needs[n];
                if (need.Product == null) return false;
                if (CountOf(need.Product.Id, need.State) < Mathf.Max(1, need.Count)) return false;
            }
            return true;
        }

        public int CountOf(ushort product, FoodState state)
        {
            int c = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                BagEntry e = Entries[i];
                if (e.Product == product && (state == FoodState.None || e.State == state)) c += e.Count;
            }
            return c;
        }

        /// <summary>Списать требования рецепта. Средняя прожарка списанного
        /// возвращается — пригодится для оценки блюда.</summary>
        public byte Consume(FoodRecipeDef r)
        {
            int cookSum = 0, taken = 0;
            for (int n = 0; n < r.Needs.Count; n++)
            {
                FoodNeed need = r.Needs[n];
                int left = Mathf.Max(1, need.Count);
                for (int i = Entries.Count - 1; i >= 0 && left > 0; i--)
                {
                    BagEntry e = Entries[i];
                    if (e.Product != need.Product.Id) continue;
                    if (need.State != FoodState.None && e.State != need.State) continue;
                    int take = Mathf.Min(left, e.Count);
                    cookSum += e.Cook * take;
                    taken += take;
                    e.Count -= take;
                    left -= take;
                    if (e.Count <= 0) Entries.RemoveAt(i); else Entries[i] = e;
                }
            }
            return taken > 0 ? (byte)(cookSum / taken) : (byte)0;
        }

        private static byte WeightedCook(byte a, int na, byte b, int nb)
        {
            int n = na + nb;
            return n <= 0 ? (byte)0 : (byte)Mathf.RoundToInt((a * na + b * nb) / (float)n);
        }
    }

    /// <summary>
    /// ТАРА В СЦЕНЕ — СЕТЕВАЯ ВЕРСИЯ.
    ///
    /// Сервер держит ContentBag с таймерами и шлёт клиентам компактный снимок
    /// (BagView) несколько раз в секунду, и только изменившиеся строки.
    ///
    /// ═══ ДВА РЕЖИМА ═══
    ///
    /// Absorbs = да (котёл, казан): продукт исчезает как объект (деспавн) и
    ///   становится строкой в Bag. Греется всё содержимое.
    /// Absorbs = нет (сковорода, духовка, вертел): куски лежат физически,
    ///   тара только греет тех, кто внутри триггера.
    ///
    /// ═══ НАГРЕВ ═══
    ///
    /// Если на объекте есть ITemperatureSource (PotThermal после переноса) —
    /// тара варит, пока температура не ниже порога. Иначе — флаг HeatOn,
    /// который ставит плита/костёр (или галка в инспекторе для проверки).
    ///
    /// Требует: NetworkObject. Коллайдер-триггер приёма продуктов и отдельный
    /// коллайдер полости (Inside Volume) для проверки погружения инструментов.
    /// </summary>
    public sealed class Cookware : NetworkBehaviour, IVesselContents
    {
        [Header("Описание")]
        public CookwareDef Def;

        [Header("Нагрев")]
        [Tooltip("Включён ли нагрев, если источника температуры нет. Ставит сервер.")]
        [SerializeField] private bool _heatOn;

        [Tooltip("С какой температуры тара начинает готовить (если есть ITemperatureSource). " +
                 "Для кастрюли ~95, для сковороды ~160.")]
        [SerializeField] private float _activeFromC = 95f;

        [Header("Горловина")]
        [Tooltip("Центр отверстия сверху — сюда сыплют станки и отсюда растёт горка. " +
                 "Пусто — центр полости (Inside Volume) или сам объект.")]
        [SerializeField] private Transform _mouth;

        public enum UpAxis : byte
        {
            [InspectorName("Угадать (ось, ближайшая к верху сейчас)")] Auto = 0,
            [InspectorName("+Y (зелёная)")] PlusY = 1,
            [InspectorName("+Z (синяя) — частый случай у моделей из Blender")] PlusZ = 2,
            [InspectorName("−Z")] MinusZ = 3,
            [InspectorName("+X (красная)")] PlusX = 4,
            [InspectorName("−X")] MinusX = 5,
            [InspectorName("−Y")] MinusY = 6,
        }

        [Tooltip("Какая ось МОДЕЛИ тары смотрит вверх, когда тара стоит правильно. " +
                 "Магнит у станка ставит тару этой осью вверх — даже если она упала набок. " +
                 "Включи режим Local у стрелок и посмотри, какая стрелка торчит из горловины.")]
        [SerializeField] private UpAxis _upAxis = UpAxis.Auto;

        [Header("Переполнение")]
        [Tooltip("С какой заполненности тара больше не принимает, доля от вместимости. " +
                 "1 — до краёв, 0.8 — на 80%. Всё сверх этого осыпается через борт.")]
        [Range(0.2f, 1f)][SerializeField] private float _overflowAt = 1f;
        [Tooltip("Префаб кучки (тот же FoodHeap, что у измельчителя) — в неё на полу " +
                 "собирается то, что осыпалось. Пусто — лишнее просто пропадает.")]
        [SerializeField] private FoodHeap _spillHeapPrefab;
        /// <summary>Префаб кучки этой тары — запасной для лопаты, если у неё свой не задан.</summary>
        public FoodHeap SpillHeapPrefab => _spillHeapPrefab;
        [Tooltip("Слой кучек — чтобы досыпать в уже лежащую у тары")]
        [SerializeField] private LayerMask _spillHeapMask = ~0;
        [Tooltip("Сколько кусочков срывается через борт при каждом переполнении (визуал)")]
        [Range(0, 8)][SerializeField] private int _spillDrops = 3;

        [Header("Поза для стыковки (надёжно)")]
        [Tooltip("Носик / острый край тары: пустой дочерний объект у носика. Магнит повернёт тару " +
                 "носиком только туда, куда разрешено на точке стыковки (например, не в станок).")]
        [SerializeField] private Transform _nose;
        [SerializeField, HideInInspector] private Quaternion _uprightRot = Quaternion.identity;
        [SerializeField, HideInInspector] private bool _hasUpright;

        /// <summary>Запомнена ли правильная поза («дном вниз»). Если да — магнит ставит
        /// тару ровно в неё и только поворачивает вокруг вертикали. Up Axis тогда не нужен.</summary>
        public bool HasUprightPose => _hasUpright;
        public Quaternion UprightRotation => _uprightRot;

        /// <summary>Направление на носик в осях модели (от опоры). Нет носика — вперёд модели.</summary>
        public Vector3 NoseLocal
        {
            get
            {
                if (_nose == null) return Vector3.forward;
                Vector3 d = transform.InverseTransformPoint(_nose.position);
                return d.sqrMagnitude > 1e-8f ? d : Vector3.forward;
            }
        }

        /// <summary>Позы нет — запомнить текущую (так тару поставили в сцене). Для духовки и другой большой тары.</summary>
        public void EnsureUprightPose()
        {
            if (_hasUpright) return;
            _uprightRot = transform.rotation;
            _hasUpright = true;
        }

        [ContextMenu("Запомнить: так тара стоит правильно (дном вниз)")]
        private void CaptureUpright()
        {
            // Поворот вокруг вертикали не важен — магнит его всё равно выберет сам.
            _uprightRot = transform.rotation;
            _hasUpright = true;
            Debug.Log($"[Тара] «{name}»: поза запомнена. Магнит будет ставить тару только так, " +
                      "поворачивая вокруг вертикали.", this);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        [ContextMenu("Забыть позу")]
        private void ForgetUpright()
        {
            _hasUpright = false;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        [Header("Внутренний объём")]
        [Tooltip("Коллайдер ПОЛОСТИ, а не стенок. Пусто — любой коллайдер на объекте.")]
        [SerializeField] private Collider _insideVolume;
        [SerializeField] private float _insideMargin = 0.03f;

        [Header("Приём")]
        [Tooltip("Что мгновенно происходит с продуктом при попадании в глотающую тару.\n" +
                 "Бутылку молока бросили в кастрюлю — оно «Налито», мешок муки — «Зачерпнуто». " +
                 "Без этого рецепт, которому нужно налитое молоко, не увидел бы бутылку.")]
        [SerializeField] private FoodProcess[] _applyOnEnter = { FoodProcess.Pour, FoodProcess.Scoop };

        [Header("Рецепты")]
        [Tooltip("Искать ли рецепты в этой таре. Тарелке и холодильнику не нужно.")]
        [SerializeField] private bool _cooksRecipes = true;

        [Header("Сеть")]
        [Tooltip("Сколько раз в секунду рассылать снимок содержимого")]
        [Range(1f, 20f)][SerializeField] private float _publishRate = 4f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        // ───────── сервер ─────────
        [SerializeField] private ContentBag _bag = new ContentBag();
        private readonly HashSet<FoodPiece> _loose = new HashSet<FoodPiece>();
        private CookwareSoupFloat _soupFloat;

        /// <summary>Куски, которые лежат/плавают в таре собой (не проглочены): сковорода, кастрюля-суп. Сервер.</summary>
        public IEnumerable<FoodPiece> Loose => _loose;

        /// <summary>Кусок лежит/плавает в таре — взять в «куски» (кастрюля-суп находит их сама, триггер не нужен). Сервер.</summary>
        public void ServerAdoptLoose(FoodPiece p)
        {
            if (p == null || p.IsConsumed || p.IsHeld || _loose.Contains(p)) return;
            _loose.Add(p);
            if (_log) Debug.Log($"[{Def?.Title}] «{p.Def?.Title}» в кастрюле куском — плавает, варится, в рецепте считается", this);
        }

        /// <summary>Убрать кусок (вынесли из кастрюли). Сервер.</summary>
        public void ServerDropLoose(FoodPiece p)
        {
            if (p != null && _loose.Remove(p)) p.ServerStopWork();
        }

        /// <summary>
        /// Сколько порций продукта (или Alt) лежит в таре КУСКАМИ (не проглочено): для рецептов кастрюли-супа.
        /// need — только в этом состоянии (None — любое); dirty — грязные/испорченные (не в счёт, если cleanOnly).
        /// </summary>
        public int LooseCount(ushort product, ushort alt, FoodState need, bool cleanOnly, out int dirty)
        {
            dirty = 0;
            int have = 0;
            foreach (FoodPiece p in _loose)
            {
                if (p == null || p.IsConsumed || p.IsHeld) continue;
                ushort id = p.ProductId;
                if (id != product && (alt == 0 || id != alt)) continue;
                FoodState s = p.State;
                if (need != FoodState.None && id == product && s != need) continue;
                if (cleanOnly && (s == FoodState.Dirty || FoodStateInfo.IsSpoiled(s))) { dirty += p.Count; continue; }
                have += p.Count;
            }
            return have;
        }
        private readonly List<FoodPiece> _looseTmp = new List<FoodPiece>();
        private ITemperatureSource _temp;
        private float _publishTimer;

        // ───────── сеть ─────────
        private readonly SyncList<BagView> _view = new SyncList<BagView>();
        private readonly SyncVar<bool> _heatingNet = new SyncVar<bool>(false);
        /// <summary>Насколько содержимое сейчас взбалтывают 0..255 (вибратор, миксер) —
        /// для пены и пузырей у всех игроков. Сам затухает.</summary>
        private readonly SyncVar<byte> _agitationNet = new SyncVar<byte>(0);
        private float _agitation;

        /// <summary>Взбалтывание 0..1 (видят все) — пена, пузыри, дрожь поверхности.</summary>
        public float Agitation01 => _agitationNet.Value / 255f;

        /// <summary>Сколько порций продукта внутри (любое состояние). Работает везде.</summary>
        public int CountOf(ushort product)
        {
            if (base.IsServerInitialized) return _bag.CountOf(product, FoodState.None);
            int n = 0;
            for (int i = 0; i < _view.Count; i++) if (_view[i].Product == product) n += _view[i].Count;
            return n;
        }

        /// <summary>Взбалтывают: strength 0..1. Сервер.</summary>
        [Server]
        public void ServerAgitate(float strength)
        {
            _agitation = Mathf.Max(_agitation, Mathf.Clamp01(strength));
        }

        /// <summary>Забрать count порций КОНКРЕТНОГО продукта (любое состояние). Возвращает,
        /// сколько взято. Для взбивания: молоко → сливки → масло.</summary>
        [Server]
        public int ServerTake(ushort product, int count)
        {
            int left = count;
            for (int i = _bag.Entries.Count - 1; i >= 0 && left > 0; i--)
            {
                BagEntry e = _bag.Entries[i];
                if (e.Product != product) continue;
                int take = Mathf.Min(left, e.Count);
                e.Count -= take;
                left -= take;
                if (e.Count <= 0) _bag.Entries.RemoveAt(i); else _bag.Entries[i] = e;
            }
            int taken = count - left;
            if (taken > 0) Publish(true);
            return taken;
        }
        /// <summary>Какой рецепт идёт: индекс в FoodDatabase.Recipes, −1 — никакой.</summary>
        private readonly SyncVar<short> _recipeNet = new SyncVar<short>(-1);
        /// <summary>Прогресс рецепта 0..255.</summary>
        private readonly SyncVar<byte> _recipeProgressNet = new SyncVar<byte>(0);

        // сервер: идущий рецепт
        private FoodRecipeDef _recipe;
        private float _recipeSpent;
        private float _recipeIdle;

        /// <summary>Содержимое сменилось (для визуала жидкости и HUD).</summary>
        public event Action ContentsChanged;

        // ───────── чтение ─────────
        /// <summary>Сколько строк в снимке содержимого. Работает с любой стороны.</summary>
        public int ViewCount => _view.Count;

        /// <summary>Строка снимка: что, в каком состоянии, сколько, прогресс.</summary>
        public BagView GetView(int index) => _view[index];
        /// <summary>Серверная правда. На клиенте пусто.</summary>
        public ContentBag Bag => _bag;
        /// <summary>Идёт ли готовка прямо сейчас (видят все).</summary>
        public bool IsHeating => _heatingNet.Value;

        public bool HeatOn => _heatOn;

        /// <summary>Какой рецепт сейчас готовится (видят все). null — никакой.</summary>
        public FoodRecipeDef ActiveRecipe
        {
            get { FoodDatabase db = FoodDatabase.Instance; return db != null ? db.RecipeAt(_recipeNet.Value) : null; }
        }

        /// <summary>Прогресс рецепта 0..1 (видят все).</summary>
        public float RecipeProgress01 => _recipeProgressNet.Value / 255f;

        /// <summary>Рецепт завершён. Сервер. Аргументы: рецепт, средняя прожарка
        /// списанного. Сюда подпишутся звук, оценка блюда, FloorMess.</summary>
        public event Action<FoodRecipeDef, byte> ServerRecipeDone;

        // ───────── IVesselContents ─────────
        /// <summary>Сколько реально принимает: вместимость × порог переполнения.</summary>
        public int Capacity => Def != null ? Mathf.Max(1, Mathf.FloorToInt(Def.Capacity * _overflowAt)) : 0;
        public int TotalCount
        {
            get
            {
                if (base.IsServerInitialized) return _bag.TotalCount;
                int n = 0;
                for (int i = 0; i < _view.Count; i++) n += _view[i].Count;
                return n;
            }
        }
        public bool IsEmpty => TotalCount == 0;
        public int FreeSpace => Mathf.Max(0, Capacity - TotalCount);
        public float Fill01 => Capacity > 0 ? Mathf.Clamp01(TotalCount / (float)Capacity) : 0f;
        public bool IsFull => Capacity > 0 && TotalCount >= Capacity;

        /// <summary>Вся тара в сцене — станкам не нужно искать физикой: у тары
        /// в руках коллайдеры выключены, и OverlapSphere её бы не увидел.</summary>
        public static readonly System.Collections.Generic.List<Cookware> All =
            new System.Collections.Generic.List<Cookware>();

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        /// <summary>Ось модели, которая должна смотреть вверх. Vector3.zero — угадывать.</summary>
        public Vector3 LocalUp
        {
            get
            {
                switch (_upAxis)
                {
                    case UpAxis.PlusY: return Vector3.up;
                    case UpAxis.PlusZ: return Vector3.forward;
                    case UpAxis.MinusZ: return Vector3.back;
                    case UpAxis.PlusX: return Vector3.right;
                    case UpAxis.MinusX: return Vector3.left;
                    case UpAxis.MinusY: return Vector3.down;
                    default: return Vector3.zero;
                }
            }
        }

        /// <summary>Триггер полости (Receive). Может быть null.</summary>
        public Collider InsideVolume => _insideVolume;

        /// <summary>Центр горловины в мире.</summary>
        public Vector3 MouthPoint
        {
            get
            {
                if (_mouth != null) return _mouth.position;
                if (_insideVolume != null)
                {
                    Bounds b = _insideVolume.bounds;
                    return new Vector3(b.center.x, b.max.y, b.center.z);
                }
                return transform.position;
            }
        }

        private void Awake()
        {
            _temp = GetComponent<ITemperatureSource>();
            _soupFloat = GetComponent<CookwareSoupFloat>();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _view.OnChange += OnViewChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _view.OnChange -= OnViewChanged;
        }

        private void OnViewChanged(SyncListOperation op, int index, BagView oldItem, BagView newItem, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ContentsChanged?.Invoke();
        }

        // ───────────────────────── ПОГРУЖЕНИЕ ─────────────────────────

        /// <summary>Точка внутри полости, с отступом от стенок? Считается
        /// одинаково везде — геометрия общая.</summary>
        public bool IsInside(Vector3 worldPoint)
        {
            Collider c = _insideVolume != null ? _insideVolume : GetComponent<Collider>();
            if (c == null) return false;

            Vector3 closest = c.ClosestPoint(worldPoint);
            if ((closest - worldPoint).sqrMagnitude > 0.0000001f) return false;

            if (_insideMargin <= 0f) return true;
            Bounds b = c.bounds;
            b.Expand(-_insideMargin * 2f);
            return b.Contains(worldPoint);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        /// <summary>Включить/выключить огонь. Зовёт плита, костёр, отладка.
        /// Выключение НЕ сбрасывает таймеры: вернёшь на огонь — доварится.</summary>
        [Server]
        public void ServerSetHeat(bool on) => _heatOn = on;

        /// <summary>Снять с огня. Позиции помнят своё время.</summary>
        [Server]
        public void ServerTakeOff() => _heatOn = false;

        /// <summary>Положить продукт из мира.</summary>
        [Server]
        public bool ServerPut(FoodPiece item)
        {
            if (Def == null || item == null || item.IsHeld || item.IsConsumed) return false;

            // Продукт сам решает, как ему попасть в тару (какао-зёрна: сырые лежат
            // куском и жарятся, обжаренные уходят в тару порциями).
            IFoodPutHandler putHandler = item.GetComponent<IFoodPutHandler>();
            if (putHandler != null && putHandler.ServerHandlePut(this, out bool handledPut)) return handledPut;

            if (!Def.Absorbs)
            {
                _loose.Add(item);
                return true;
            }
            // Кастрюля-суп (Cookware Soup Float): целые куски не глотаем — плавают собой, варятся, их можно достать.
            if (_soupFloat != null && _soupFloat.KeepsWhole(item))
            {
                _loose.Add(item);
                if (_log) Debug.Log($"[{Def.Title}] «{item.Def?.Title}» плавает куском (суп) — варится, можно достать", this);
                return true;
            }

            int over = _bag.TotalCount + item.Count - Capacity;
            if (over > 0 && !ServerDisplaceLiquid(over))
            {
                if (_log) Debug.Log($"[{Def.Title}] не влезло: {item}", this);
                return false;
            }

            if (!item.ServerTryConsume()) return false;

            // Продукт мог быть под инструментом — фиксируем, что успело выйти.
            item.ServerStopWork();
            _bag.Add(item.ProductId, EnterState(item.ProductId, item.State), item.Count, item.Cook);
            if (_log)
            {
                Vector3 ip = item.transform.position;
                string where = _insideVolume != null
                    ? $" · коллайдер «{_putVia}», {Vector3.Distance(ip, _insideVolume.bounds.center):0.00} м от центра полости «{_insideVolume.name}»"
                    : "";
                Debug.Log($"[{Def.Title}] положили {item} · внутри {_bag.TotalCount}{where}", this);
            }
            _putVia = null;

            // Сетевой объект уничтожается ТОЛЬКО деспавном. Destroy на
            // клиентах оставил бы «призраков», а на сервере сломал бы спавн.
            item.NetworkObject.Despawn();
            Publish(true);
            return true;
        }

        /// <summary>
        /// Вытеснение: кинули в полную кастрюлю морковку — столько же воды выплёскивается
        /// через край (брызги, лужа). Только жидкость; если жидкости не хватает — false, ничего не трогаем.
        /// </summary>
        [Server]
        private bool ServerDisplaceLiquid(int portions)
        {
            if (portions <= 0) return true;
            FoodDatabase db = FoodDatabase.Instance;
            int liquid = 0;
            foreach (BagEntry e in _bag.Entries)
                if (IsLiquidHere(e.Product, db != null ? db.Get(e.Product) : null, e.State)) liquid += e.Count;
            if (liquid < portions) return false;

            int left = portions;
            for (int i = _bag.Entries.Count - 1; i >= 0 && left > 0; i--)
            {
                BagEntry e = _bag.Entries[i];
                if (!IsLiquidHere(e.Product, db != null ? db.Get(e.Product) : null, e.State)) continue;
                int take = Mathf.Min(left, e.Count);
                e.Count -= take;
                left -= take;
                if (e.Count <= 0) _bag.Entries.RemoveAt(i); else _bag.Entries[i] = e;
                ServerSpillOver(e.Product, e.State, take, e.Cook);
            }
            if (_log) Debug.Log($"[{Def?.Title}] вытеснено через край: {portions} порц. жидкости", this);
            return true;
        }

        /// <summary>
        /// Положить содержимое без предмета: кучка упала в тару, лопата
        /// высыпала, измельчитель выдал.
        ///
        /// spillOverflow = true (обычно): что не влезло — осыпается через борт
        /// (кусочки летят с края, на полу рядом растёт кучка). Возвращает ВСЁ
        /// количество — тара «забрала» его: часть внутрь, часть на пол.
        /// spillOverflow = false: возвращает, сколько влезло, остальное — у того, кто сыпал.
        /// </summary>
        [Server]
        public int ServerAddContent(ushort product, FoodState state, int count, byte cook, bool spillOverflow = true)
        {
            if (Def == null || count <= 0) return 0;
            // Добавки (сахар, мука, соль, закваска, яйца) места не занимают — влезают и в полную тару.
            FoodDatabase adb = FoodDatabase.Instance;
            if (FoodUnits.IsAdditive(adb != null ? adb.Get(product) : null, state))
                return ServerAddPinch(product, state, count, cook);
            int free = Capacity - _bag.TotalCount;
            int add = Mathf.Clamp(free, 0, count);
            if (add > 0)
            {
                _bag.Add(product, EnterState(product, state), add, cook);
                if (_log) Debug.Log($"[{Def.Title}] +{add} · внутри {_bag.TotalCount}/{Capacity}", this);
                Publish(true);
            }
            int rest = count - add;
            if (rest <= 0 || !spillOverflow) return add;

            ServerSpillOver(product, state, rest, cook);
            return count;
        }

        /// <summary>
        /// ЩЕПОТКА: закваска, соль, специи — места в таре не занимает, поэтому
        /// сыпется и в полное ведро. Ещё — замена 1:1 (сливки → сметана), когда
        /// тара полная и обычное добавление не влезло бы.
        /// </summary>
        [Server]
        public int ServerAddPinch(ushort product, FoodState state, int count, byte cook = 0, bool log = true)
        {
            if (Def == null || count <= 0) return 0;
            _bag.Add(product, EnterState(product, state), count, cook);
            if (_log && log) Debug.Log($"[{Def.Title}] +{count} (щепотка/замена) · внутри {_bag.TotalCount}/{Capacity}", this);
            Publish(true);
            return count;
        }

        /// <summary>
        /// Переполнено: лишнее осыпается через борт. Все видят, как кусочки
        /// срываются с края; на полу у тары растёт (или досыпается) кучка.
        /// </summary>
        [Server]
        public void ServerSpillOver(ushort product, FoodState state, int count, byte cook)
        {
            if (count <= 0) return;
            if (_log) Debug.Log($"[{Def?.Title}] переполнено — {count} порц. через борт", this);

            // Сторона, через которую сыплется: случайная по горизонтали.
            float a = UnityEngine.Random.value * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float half = HalfSizeAlong(dir);
            Vector3 mouth = MouthPoint;
            Vector3 rim = mouth + dir * (half * 0.9f);

            // Замешанное большое тесто (пельменное) — вываливается КОМОМ теста (катать катком), а не кучкой.
            if (DoughSheetDump.ServerTryDump(this, product, state, count, rim, dir)) return;

            // Жидкость через край — лужа и брызги, а не кучка.
            FoodDatabase ldb = FoodDatabase.Instance;
            ProductDef pdef = ldb != null ? ldb.Get(product) : null;
            if (IsLiquidHere(product, pdef, state))
            {
                RpcLiquidSpillFx(rim, dir, Mathf.Min(4 + count * 2, 14));
                CookwareSpill spill = GetComponent<CookwareSpill>();
                LiquidPuddle puddle = spill != null ? spill.PuddlePrefab : null;
                if (puddle != null) LiquidPuddle.ServerAddAt(puddle, product, FloorBeside(mouth + dir * (half + 0.15f)), count);
                return;
            }

            if (_spillDrops > 0) RpcSpillFx(rim, dir, Mathf.Min(_spillDrops + count / 3, 8));

            if (_spillHeapPrefab == null) return;
            Vector3 floor = FloorBeside(mouth + dir * (half + 0.15f));
            FoodHeap.ServerDropOnFloor(_spillHeapPrefab, product, state, count, cook, 50, floor, 0.3f, _spillHeapMask);
        }

        /// <summary>Жидкость тут: LiquidAnchor тары считает продукт жидкостью, или это «налитое».</summary>
        private bool IsLiquidHere(ushort product, ProductDef def, FoodState state)
        {
            CookwareLiquidVisual lv = GetComponentInChildren<CookwareLiquidVisual>();
            if (lv != null && lv.TryGetRole(product, out CookwareLiquidVisual.Role role))
                return role != CookwareLiquidVisual.Role.Paste && role != CookwareLiquidVisual.Role.Floating;
            return FoodHeapLook.IsLiquid(def, state);
        }

        /// <summary>Перелив жидкости: брызги и капли с края (у всех).</summary>
        [ObserversRpc]
        private void RpcLiquidSpillFx(Vector3 rim, Vector3 dir, int drops)
        {
            CookwareLiquidVisual lv = GetComponentInChildren<CookwareLiquidVisual>();
            Color c = lv != null ? lv.LiquidColor : Color.white;
            LiquidFx.Splash(rim, (dir + Vector3.up * 0.6f).normalized, c, drops, 0.02f, 1.2f);
            if (lv != null) lv.Ripple(rim - dir * 0.05f);
        }

        [ObserversRpc]
        private void RpcSpillFx(Vector3 rim, Vector3 dir, int drops)
        {
            CookwareHeapVisual v = GetComponentInChildren<CookwareHeapVisual>();
            if (v != null) v.SpillFx(rim, dir, drops);
        }

        /// <summary>Полуразмер тары по горизонтали в сторону dir (по габаритам мешей).</summary>
        private float HalfSizeAlong(Vector3 dir)
        {
            Bounds b = default;
            bool any = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if ((r.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return 0.3f;
            Vector3 e = b.extents;
            return Mathf.Abs(dir.x) * e.x + Mathf.Abs(dir.z) * e.z;
        }

        /// <summary>Пол рядом с тарой (луч вниз мимо самой тары).</summary>
        private Vector3 FloorBeside(Vector3 from)
        {
            RaycastHit[] hits = Physics.RaycastAll(from + Vector3.up * 0.5f, Vector3.down, 6f, ~0,
                                                   QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 p = from;
            foreach (RaycastHit h in hits)
            {
                if (h.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<FoodHeap>() != null) continue;
                if (h.distance < best) { best = h.distance; p = h.point; }
            }
            return p;
        }

        /// <summary>
        /// Зачерпнуть сверху: лопата, мастерок, половник. Берёт из ОДНОЙ
        /// позиции (верхней — последней добавленной), чтобы в лопате не
        /// смешалось сырое с тёртым. Возвращает, сколько взято.
        /// </summary>
        [Server]
        public int ServerTakeTop(int max, out ushort product, out FoodState state, out byte cook)
        {
            product = 0; state = FoodState.None; cook = 0;
            if (max <= 0 || _bag.IsEmpty) return 0;

            int i = _bag.Entries.Count - 1;
            BagEntry e = _bag.Entries[i];
            int take = Mathf.Min(max, e.Count);
            product = e.Product;
            state = e.State;
            cook = e.Cook;

            e.Count -= take;
            if (e.Count <= 0) _bag.Entries.RemoveAt(i);
            else _bag.Entries[i] = e;

            if (_log) Debug.Log($"[{Def?.Title}] зачерпнули {take} · осталось {_bag.TotalCount}", this);
            Publish(true);
            return take;
        }

        /// <summary>Прогнать содержимое процессом снаружи — миксер в ведре,
        /// отбойник в тазике. Зовёт FoodTool на сервере.</summary>
        [Server]
        public bool ServerTick(FoodProcess proc, float dt)
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || _bag.IsEmpty) return false;
            // Рецепт важнее переходов: пока миксер взбивает сливки в масло,
            // сами сливки не должны успеть превратиться во что-то своё.
            if (TickRecipe(proc, dt, db)) return true;
            bool changed = _bag.Tick(proc, dt, db.Table);
            if (changed && _log) Debug.Log($"[{Def?.Title}] {Describe()}", this);
            if (changed) Publish(true);
            return changed;
        }

        public void ServerClear()
        {
            if (!base.IsServerInitialized) return;
            _bag.Clear();
            Publish(true);
        }

        public int ServerSpill(float fraction01)
        {
            if (!base.IsServerInitialized || _bag.IsEmpty) return 0;
            int n = Mathf.CeilToInt(_bag.TotalCount * Mathf.Clamp01(fraction01));
            int removed = _bag.Remove(n);
            if (removed > 0) Publish(true);
            return removed;
        }

        private void Update()
        {
            if (!base.IsServerInitialized || Def == null) return;

            // Взбалтывание затухает само за ~полсекунды без подпитки.
            if (_agitation > 0f) _agitation = Mathf.Max(0f, _agitation - Time.deltaTime * 2f);
            byte ag = (byte)Mathf.RoundToInt(_agitation * 255f);
            if (Mathf.Abs(ag - _agitationNet.Value) > 12 || (ag == 0 && _agitationNet.Value != 0)) _agitationNet.Value = ag;

            bool heating = IsHeatActive();
            if (_heatingNet.Value != heating) _heatingNet.Value = heating;

            if (heating && Def.HeatProcess != FoodProcess.None)
            {
                float dt = Time.deltaTime;
                FoodDatabase db = FoodDatabase.Instance;
                if (db != null && !_bag.IsEmpty && TickRecipe(Def.HeatProcess, dt, db)) { }
                else if (db != null && !_bag.IsEmpty && _bag.Tick(Def.HeatProcess, dt, db.Table))
                {
                    if (_log) Debug.Log($"[{Def.Title}] {Describe()}", this);
                    Publish(true);
                }
                TickLoose(dt);
            }
            else if (_loose.Count > 0)
            {
                // Огонь погас под сковородой — куски снимаем с процесса,
                // иначе они «доварились» бы, когда снова включат.
                StopLoose();
            }

            // Рецепт, который перестали «делать» (выключили миксер, сняли с огня),
            // через секунду бросается. Время НЕ копится втихую.
            if (_recipe != null)
            {
                _recipeIdle += Time.deltaTime;
                if (_recipeIdle > 1f) StopRecipe();
            }

            _publishTimer -= Time.deltaTime;
            if (_publishTimer <= 0f)
            {
                _publishTimer = 1f / Mathf.Max(1f, _publishRate);
                Publish(false);
            }
        }

        private bool IsHeatActive()
        {
            if (!Def.NeedsHeatSource) return true;
            // Своя температура (PotThermal у кастрюли) — главнее: её греют костёр/плита.
            if (_temp != null) return _temp.TemperatureC >= _activeFromC;
            // Иначе костёр присылает температуру напрямую (затухает за полсекунды без неё).
            if (Time.time < _externalUntil) return _externalC >= _activeFromC;
            return _heatOn;
        }

        private float _externalC;
        private float _externalUntil;

        /// <summary>Температура тары от внешнего источника (костёр), °C. Для HUD и оценки.</summary>
        public float ExternalTemperatureC => Time.time < _externalUntil ? _externalC : 0f;

        /// <summary>Костёр греет тару: присылать каждый кадр/тик, пока тара стоит над огнём. Сервер.</summary>
        [Server]
        public void ServerSetExternalTemperature(float celsius)
        {
            _externalC = celsius;
            _externalUntil = Time.time + 0.5f;
        }

        private void TickLoose(float dt)
        {
            if (_loose.Count == 0) return;
            _looseTmp.Clear();
            _looseTmp.AddRange(_loose);
            foreach (FoodPiece p in _looseTmp)
            {
                if (p == null || p.IsConsumed || p.IsHeld) { _loose.Remove(p); continue; }
                p.ServerAddWork(Def.HeatProcess, dt);
            }
        }

        private void StopLoose()
        {
            foreach (FoodPiece p in _loose)
                if (p != null) p.ServerStopWork();
        }

        /// <summary>Состояние продукта сразу после попадания в тару.</summary>
        private FoodState EnterState(ushort product, FoodState state)
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || _applyOnEnter == null) return state;
            for (int i = 0; i < _applyOnEnter.Length; i++)
                if (db.Table.TryGet(product, state, _applyOnEnter[i], out FoodTransition t))
                    return t.To;
            return state;
        }

        /// <summary>
        /// Шаг рецепта. true — рецепт идёт и забрал этот процесс себе.
        ///
        /// Рецепт стартует, когда содержимого хватает и процесс совпал.
        /// Докинули лишнего — не мешает. Вынули нужное — рецепт сброшен.
        /// </summary>
        private bool TickRecipe(FoodProcess proc, float dt, FoodDatabase db)
        {
            if (!_cooksRecipes || proc == FoodProcess.None) return false;

            if (_recipe != null && (_recipe.Process != proc || !_bag.Satisfies(_recipe)))
                StopRecipe();

            if (_recipe == null)
            {
                IReadOnlyList<FoodRecipeDef> all = db.Recipes;
                for (int i = 0; i < all.Count; i++)
                {
                    FoodRecipeDef r = all[i];
                    if (r == null || r.Process != proc || !_bag.Satisfies(r)) continue;
                    // Есть такой же рецепт в новой книге (CookRecipeBook) — варит она, старый молчит.
                    if (CookRecipeBook.HasBookRecipe(r.Title)) continue;
                    _recipe = r;
                    _recipeSpent = 0f;
                    _recipeNet.Value = (short)i;
                    if (_log) Debug.Log($"[{Def?.Title}] начат рецепт «{r.Title}» ({r.Seconds:0} с)", this);
                    break;
                }
                if (_recipe == null) return false;
            }

            _recipeIdle = 0f;
            _recipeSpent += dt;
            float p = _recipe.Seconds > 0.01f ? Mathf.Clamp01(_recipeSpent / _recipe.Seconds) : 1f;
            byte pb = (byte)Mathf.RoundToInt(p * 255f);
            if (Mathf.Abs(pb - _recipeProgressNet.Value) >= 3 || pb == 255) _recipeProgressNet.Value = pb;

            if (p < 1f) return true;

            FoodRecipeDef done = _recipe;
            byte cook = _bag.Consume(done);
            if (done.ResultProduct != null)
                _bag.Add(done.ResultProduct.Id, done.ResultProduct.StartState, Mathf.Max(1, done.ResultCount), cook);

            if (_log) Debug.Log($"[{Def?.Title}] ГОТОВО «{done.Title}» · внутри: {DescribeServer(db)}", this);
            StopRecipe();
            ServerRecipeDone?.Invoke(done, cook);
            Publish(true);
            return true;
        }

        private void StopRecipe()
        {
            _recipe = null;
            _recipeSpent = 0f;
            _recipeIdle = 0f;
            if (_recipeNet.Value != -1) _recipeNet.Value = -1;
            if (_recipeProgressNet.Value != 0) _recipeProgressNet.Value = 0;
        }

        private string DescribeServer(FoodDatabase db)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (BagEntry e in _bag.Entries)
            {
                ProductDef d = db.Get(e.Product);
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"{(d != null ? d.Title : "?")} ({FoodStateInfo.NameOf(e.State)}) ×{e.Count}");
            }
            return sb.Length > 0 ? sb.ToString() : "пусто";
        }

        /// <summary>Разослать снимок. force — содержимое точно поменялось,
        /// шлём сразу, не дожидаясь таймера.</summary>
        private void Publish(bool force)
        {
            if (!base.IsServerInitialized) return;
            FoodDatabase db = FoodDatabase.Instance;
            FoodTransitionTable table = db != null ? db.Table : null;

            int n = _bag.Entries.Count;
            for (int i = 0; i < n; i++)
            {
                BagEntry e = _bag.Entries[i];
                float wp = CookTimer.WindowPosition(e.Timer, e.Product, table);
                BagView v = new BagView
                {
                    Product = e.Product,
                    State = e.State,
                    Count = (ushort)Mathf.Clamp(e.Count, 0, ushort.MaxValue),
                    Cook = e.Cook,
                    Progress = (byte)Mathf.RoundToInt(CookTimer.Progress01(e.Timer, e.Product, table) * 255f),
                    Window = float.IsNaN(wp) ? (byte)0 : (byte)Mathf.Clamp(Mathf.RoundToInt((wp + 1f) * 127f) + 1, 1, 255),
                };

                if (i < _view.Count)
                {
                    // Прогресс и окно шлём по таймеру, структуру — сразу.
                    if (!_view[i].Equals(v) && (force || !SameShape(_view[i], v) || ProgressMoved(_view[i], v)))
                        _view[i] = v;
                }
                else _view.Add(v);
            }
            while (_view.Count > n) _view.RemoveAt(_view.Count - 1);
        }

        private static bool SameShape(BagView a, BagView b) =>
            a.Product == b.Product && a.State == b.State && a.Count == b.Count;

        private static bool ProgressMoved(BagView a, BagView b) =>
            Mathf.Abs(a.Progress - b.Progress) >= 3 || Mathf.Abs(a.Window - b.Window) >= 3 || a.Cook != b.Cook;

        public string Describe()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || TotalCount == 0) return "пусто";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < _view.Count; i++)
            {
                BagView e = _view[i];
                ProductDef d = db.Get(e.Product);
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"{(d != null ? d.Title : "?")} ({FoodStateInfo.NameOf(e.State)}) ×{e.Count}");
            }
            return sb.ToString();
        }

        // ───────────────────────── ПРИЁМ ─────────────────────────

        // Триггер приёма может сидеть под чужим Rigidbody (ковш — часть экскаватора):
        // тогда Unity шлёт события не сюда, а корню машины. CookwareTriggerRelay на Receive
        // пересылает их через эти три метода.
        public void RelayTriggerEnter(Collider other) => OnTriggerEnter(other);
        public void RelayTriggerStay(Collider other) => OnTriggerStay(other);
        public void RelayTriggerExit(Collider other) => OnTriggerExit(other);

        /// <summary>
        /// Продукт действительно у полости? Unity шлёт события ВСЕХ триггеров объекта (и его Rigidbody):
        /// у бетономешалки/машины есть и другие триггеры — без проверки тара «засасывала» всё,
        /// что лежит рядом. Пропускаем только то, что касается полости (Inside Volume).
        /// </summary>
        private bool NearCavity(Collider other)
        {
            if (_insideVolume == null || other == null) return true;
            if (!_insideVolume.enabled || !_insideVolume.gameObject.activeInHierarchy) return false;
            if (!_insideVolume.bounds.Intersects(other.bounds)) return false;
            // точно: продукт пересекается с полостью (а не просто рядом с её габаритами)
            Transform a = _insideVolume.transform, o = other.transform;
            if (Physics.ComputePenetration(_insideVolume, a.position, a.rotation, other, o.position, o.rotation, out _, out _)) return true;
            return IsInside(other.bounds.center);
        }

        /// <summary>Откуда пришёл продукт — для лога «положили».</summary>
        private string _putVia;

        private void OnTriggerEnter(Collider other)
        {
            if (!base.IsServerInitialized || !NearCavity(other)) return;
            FoodPiece item = other.GetComponentInParent<FoodPiece>();
            if (item == null) return;
            _putVia = other.name;
            ServerPut(item);
        }

        private void OnTriggerStay(Collider other)
        {
            // Продукт вошёл в триггер, пока был в руке, и его бросили уже
            // внутри — OnTriggerEnter второй раз не придёт. Для глотающей
            // тары подбираем такие на Stay.
            if (!base.IsServerInitialized || Def == null || !Def.Absorbs || !NearCavity(other)) return;
            FoodPiece item = other.GetComponentInParent<FoodPiece>();
            if (item == null || item.IsHeld || item.IsConsumed) return;
            _putVia = other.name;
            ServerPut(item);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!base.IsServerInitialized) return;
            FoodPiece item = other.GetComponentInParent<FoodPiece>();
            if (item == null) return;
            if (_soupFloat != null && _soupFloat.Inside(item)) return;      // кастрюля-суп: всплыл выше триггера — всё ещё в кастрюле
            if (!_loose.Remove(item)) return;
            item.ServerStopWork();
        }
    }
}
