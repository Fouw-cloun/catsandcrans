using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CatCooks.Level
{
    /// <summary>Что в модуле (клетке сетки улиц).</summary>
    public enum CityRole { Empty = 0, Level = 1, Ring1 = 2, Ring2 = 3 }

    /// <summary>Тип дома. Auto — по габариту (высокий и узкий — небоскрёб, длинный — рядовой и т. д.).</summary>
    public enum BuildingKind { Auto = 0, Small = 1, Mid = 2, Large = 3, Tower = 4, Row = 5 }

    /// <summary>Роль дома в ближнем слое: ключевой (выбирается руками на модуль) или база (заполняет всё остальное).</summary>
    public enum KeyRole { Auto = 0, Key = 1, Base = 2 }

    /// <summary>План модуля ближнего слоя: какие ключевые здания ставить (пусто — случайные или никаких).</summary>
    [System.Serializable]
    public sealed class ModulePlan
    {
        public int X, Z;
        [Tooltip("Если ключевые не выбраны — поставить случайные (по настройкам слоя: небоскрёбы, крупные на углы)")]
        public bool RandomKeys = true;
        [Tooltip("Ключевые здания этого модуля (модели из списка слоя 1)")]
        public List<GameObject> Keys = new List<GameObject>();
    }

    /// <summary>Куда смотрит фасад модели (в её собственных осях). Auto: длинный по Z — фасад по X, иначе +Z.</summary>
    public enum FacadeDir { Auto = -1, PlusZ = 0, PlusX = 90, MinusZ = 180, MinusX = 270 }

    /// <summary>Рабочие стороны дома (с окнами) в осях модели: Front = +Z, Right = +X, Back = −Z, Left = −X.</summary>
    [System.Flags]
    public enum FacadeSides { None = 0, Front = 1, Right = 2, Back = 4, Left = 8 }

    [System.Serializable]
    public sealed class BuildingEntry
    {
        public GameObject Prefab;
        [Min(0f)] public float Weight = 1f;
        public BuildingKind Kind = BuildingKind.Auto;
        public FacadeDir Facade = FacadeDir.Auto;
        [Tooltip("Материал стен для частей без текстуры (ставит кнопка «Атласы»; пусто — как в модели)")]
        public Material Material;
        [Tooltip("Стороны с окнами (рабочие) — к улице и к игроку; глухие — к соседям и во двор. Находит кнопка «Рабочие стороны»")]
        public FacadeSides Sides = FacadeSides.None;
        [Tooltip("Стороны выставлены руками — автопоиск их не трогает")]
        public bool SidesManual = false;
        [Tooltip("Уникальный дом (отсылка, ориентир): один на весь город, у улицы со стороны уровня, ничем не загорожен")]
        public bool Unique = false;
        [Tooltip("Ближний слой: Авто — ключевой, если небоскрёб, крупный, upper middle или уникальный; Ключевой / База — вручную")]
        public KeyRole Role = KeyRole.Auto;
        [Tooltip("Ориентир с подсветкой: верхние окна горят всегда, сферы тускло светятся (космическая башня)")]
        public bool LandmarkGlow = false;
        [Tooltip("Свой стиль ночных окон для этой модели (иначе небоскрёбы — «Частично», жилые — как обычно)")]
        public bool CustomNight = false;
        [Tooltip("Стиль: Окна — вразнобой; Этажи — целые этажи; Верх весь + частично; Только полоса; Частично")]
        public CityOfficeTower.NightMode NightStyle = CityOfficeTower.NightMode.Floors;
        [Tooltip("Высота этажа модели, м (для «Окна» / «Этажи»)")]
        [Min(1f)] public float FloorHeight = 3.5f;
        [Tooltip("Сколько верхних этажей горит всегда")]
        [Range(0, 10)] public int TopFloors = 1;
    }

    /// <summary>Набор домов и пропсов для одного слоя застройки + его настройки.</summary>
    [System.Serializable]
    public sealed class CityLayer
    {
        public string Name = "Слой";
        public List<BuildingEntry> Buildings = new List<BuildingEntry>();
        [Tooltip("На крыши: кондиционеры, антенны, вентиляция, солнечные панели, рекламные щиты…")]
        public List<GameObject> RoofProps = new List<GameObject>();
        [Tooltip("Тротуар: фонари, деревья, урны, гидранты, остановки…")]
        public List<GameObject> StreetProps = new List<GameObject>();
        [Tooltip("Машины у бордюра (на краю дороги)")]
        public List<GameObject> Cars = new List<GameObject>();
        [Tooltip("Папка с домами этого слоя (перетащи папку из Project) — кнопка «Загрузить» в инспекторе")]
        public Object SourceFolder;
        [Tooltip("Искать и в подпапках")]
        public bool IncludeSubfolders = true;
        [Tooltip("Загружать и пропсы (папки props / street props). Выключено — только дома, пропсы пропускаются")]
        public bool LoadProps = false;
        [Tooltip("После загрузки — сразу подобрать атласы из папки слоя для белых моделей (импорт FBX)")]
        public bool AutoAtlas = true;
        [Tooltip("Атласы — материалы на шейдере города (CatCooks/City/Building: тёплый свет, дымка, ночь)")]
        public bool UseCityShader = true;
        [Tooltip("Окна нарисованы в атласе (нет отдельных стёкол) — светятся ночью по цвету стекла")]
        public bool AtlasWindows = false;

        [Header("Застройка")]
        [Tooltip("Сколько площади участка занять домами (0.3 — просторно, 0.75 — плотно)")]
        [Range(0.1f, 0.95f)] public float Density = 0.5f;
        [Tooltip("Небоскрёбов во дворе модуля: от и до")]
        public Vector2Int Towers = new Vector2Int(0, 2);
        [Tooltip("Шанс поделить модуль на 2–4 квартала с переулками")]
        [Range(0f, 1f)] public float SubdivideChance = 0.45f;
        [Tooltip("Шанс переулка между домами в ряду")]
        [Range(0f, 1f)] public float AlleyChance = 0.12f;
        [Tooltip("ПОРЯДОК (для ближнего слоя): сплошные ровные ряды без зазоров и сдвигов, дома не крутятся, " +
                 "одинаковые идут парами-тройками, углы — угловыми домами, двор не застраивается, небоскрёб — по центру квартала")]
        public bool Tidy = false;
        [Tooltip("Крупных домов (upper middle и т. п., основание в 1.5 раза больше среднего) на модуль — ставятся первыми на угол квартала, окнами на обе улицы. " +
                 "Нужно, потому что в рядах они не помещаются по глубине, а двор при «Порядке» не застраивается")]
        public Vector2Int LargePerModule = new Vector2Int(1, 2);
        [Tooltip("Мелкие дома ставить башенкой из 2–3 одинаковых (один цвет) — как модуль средней этажки")]
        [Range(0f, 1f)] public float StackSmallChance = 0.65f;
        [Tooltip("Сколько этажей-копий в башенке (мин, макс)")]
        public Vector2Int StackLevels = new Vector2Int(2, 3);
        [Tooltip("Веса типов домов в рядах по краю квартала: мелкие, средние, крупные, рядовые (длинные)")]
        public Vector4 RowKindWeights = new Vector4(0.7f, 1.4f, 0.7f, 1.2f);
        [Tooltip("Веса типов во дворе: мелкие, средние, крупные")]
        public Vector3 InnerKindWeights = new Vector3(0.15f, 1f, 1.2f);

        [Header("Пропсы")]
        [Range(0f, 1f)] public float RoofPropChance = 0.7f;
        [Range(0, 12)] public int RoofPropsMax = 5;
        [Tooltip("Пропсов на тротуаре на 100 м края (в метрах модели: на 100 единиц)")]
        [Range(0f, 20f)] public float StreetPropsPer100 = 5f;
        [Range(0f, 1f)] public float CarChance = 0.35f;
    }

    /// <summary>
    /// ГЕНЕРАТОР ЗАСТРОЙКИ ПО СЛОЯМ — расставляет дома на модулях сетки улиц (Tile Grid Generator).
    ///
    ///   • Карта ролей модулей (рисуется в инспекторе): Уровень (не трогаем) · Слой 1 · Слой 2 · Пусто.
    ///     По умолчанию: центр 2×2 — два уровня по диагонали и два модуля слоя 1; кольцо вокруг — слой 1; дальше — слой 2.
    ///   • На модуле строим только внутри тротуара — дорога по краям (Road Margin) свободна.
    ///   • Застройка «кварталами»: модуль иногда делится на 2–4 квартала с переулками; по краю квартала — РЯД домов
    ///     фасадом на улицу (мелкие/средние/длинные подряд, разной ширины и высоты, иногда переулок), во дворе —
    ///     небоскрёбы и крупные дома, часть двора пустая. Лёгкий хаос: отступы, повороты на пару градусов, высота.
    ///   • Пропсы: на крышах (луч сверху ищет настоящую крышу), на тротуаре (фонари, деревья, урны…), машины у бордюра.
    ///   • Перегенерация всего (новый Seed) или одного модуля (ПКМ по клетке карты — новый вариант только там).
    ///
    /// Дома — в «City (generated)/Module_x_z». Перегенерация модуля пересобирает только его.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBlockGenerator : MonoBehaviour
    {
        public const string ContainerName = "City (generated)";

        [Header("Сетка улиц")]
        [Tooltip("Генератор плиток (8×8 модулей). Размер модуля и позиции берутся из него")]
        public TileGridGenerator Grid;
        [Tooltip("Дорога по краю модуля — доля размера модуля с каждой стороны (там не строим)")]
        [Range(0f, 0.3f)] public float RoadMargin = 0.085f;
        [Tooltip("Тротуар перед домами — доля модуля (фонари, деревья)")]
        [Range(0f, 0.15f)] public float Sidewalk = 0.035f;
        [Tooltip("Ширина переулка между кварталами — доля модуля")]
        [Range(0f, 0.15f)] public float AlleyWidth = 0.035f;

        [Header("Слои")]
        public CityLayer Layer1 = new CityLayer { Name = "Слой 1 (рядом с уровнем)" };
        public CityLayer Layer2 = new CityLayer { Name = "Слой 2 (внешний)", AtlasWindows = true, Density = 0.55f, Towers = new Vector2Int(0, 1), SubdivideChance = 0.55f };
        [Tooltip("Сколько колец вокруг центра — слой 1 (дальше — слой 2)")]
        [Min(0)] public int Ring1Count = 1;
        [Tooltip("Ближний слой вручную: «Застроить всё» его не трогает; каждый модуль слоя 1 собирается и рандомизируется отдельно " +
                 "(окно «Модуль ближнего слоя»), ключевые здания выбираются руками, остальное — база")]
        public bool ManualLayer1 = true;
        [Tooltip("Планы модулей ближнего слоя (ключевые здания) — заполняет окно «Модуль ближнего слоя»")]
        public List<ModulePlan> Plans = new List<ModulePlan>();

        [Header("Хаос")]
        [Tooltip("Поворот домов ± градусов")]
        [Range(0f, 10f)] public float YawJitter = 2.5f;
        [Tooltip("Отступ от тротуара — случайно до этой доли модуля")]
        [Range(0f, 0.05f)] public float SetbackJitter = 0.012f;
        [Tooltip("Высота домов (не небоскрёбов) ×(1 ± это)")]
        [Range(0f, 0.4f)] public float HeightJitter = 0.12f;
        [Tooltip("Общий размер ×(1 ± это)")]
        [Range(0f, 0.2f)] public float ScaleJitter = 0.05f;
        [Tooltip("Небоскрёбы можно чуть растягивать по высоте")]
        [Range(0f, 0.4f)] public float TowerHeightJitter = 0.15f;
        [Tooltip("Небоскрёбы — офисы: ночью горит только верхняя полоса (CityOfficeTower), жилые — как обычно")]
        public bool OfficeTowers = true;
        [Tooltip("Насколько можно ужать дом, чтобы влез в ряд")]
        [Range(0.5f, 1f)] public float MinFitScale = 0.82f;

        [Header("Прочее")]
        public int Seed = 7;
        [Tooltip("Оставить MeshCollider'ы на домах (иначе — только на время поиска крыш)")]
        public bool KeepColliders = false;
        [Tooltip("Материал стёкол: на части модели с именем glass… / материалом стекла (ставит кнопка «Атласы»)")]
        public Material GlassMaterial;
        [Tooltip("Материал свечения ориентиров (сферы космической башни) — ставит меню «Космическая башня: подсветка»")]
        public Material LandmarkGlowMaterial;
        [Tooltip("Размеченные стёкла (каждое окно своё) — ставит кнопка «Атласы»")]
        public CityGlassMeshes GlassMeshes;
        public bool MarkStatic = true;
        public bool Log = true;

        [SerializeField, HideInInspector] private CityRole[] _roles = new CityRole[0];
        [SerializeField, HideInInspector] private int[] _reroll = new int[0];
        [SerializeField, HideInInspector] private int _rolesW, _rolesD;

        public int Width { get { return Grid != null ? Grid.Width : 8; } }
        public int Depth { get { return Grid != null ? Grid.Depth : 8; } }

        // ───────────────────────── РОЛИ ─────────────────────────

        public CityRole GetRole(int x, int z)
        {
            EnsureRoles();
            if (x < 0 || z < 0 || x >= Width || z >= Depth) return CityRole.Empty;
            return _roles[z * Width + x];
        }

        public void SetRole(int x, int z, CityRole r)
        {
            EnsureRoles();
            if (x < 0 || z < 0 || x >= Width || z >= Depth) return;
            _roles[z * Width + x] = r;
        }

        private void EnsureRoles()
        {
            int w = Width, d = Depth;
            if (_roles != null && _rolesW == w && _rolesD == d && _roles.Length == w * d && _reroll != null && _reroll.Length == w * d) return;
            ResetRoles();
        }

        /// <summary>Роли по кольцам: центр 2×2 — уровни по диагонали + слой 1; кольца — слой 1, дальше — слой 2.</summary>
        public void ResetRoles()
        {
            int w = Width, d = Depth;
            _roles = new CityRole[w * d]; _reroll = new int[w * d]; _rolesW = w; _rolesD = d;
            int cx = (w - 1) / 2, cz = (d - 1) / 2;          // левый-нижний из центральных
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                {
                    int rx = x <= cx ? cx - x : x - (w - 1 - cx);
                    int rz = z <= cz ? cz - z : z - (d - 1 - cz);
                    int ring = Mathf.Max(Mathf.Max(rx, rz), 0);
                    CityRole r;
                    if (ring == 0) r = ((x - cx) == (z - cz)) ? CityRole.Level : CityRole.Ring1;
                    else r = ring <= Ring1Count ? CityRole.Ring1 : CityRole.Ring2;
                    _roles[z * w + x] = r;
                }
        }

        /// <summary>Новый вариант одного модуля (при следующей генерации модуля).</summary>
        public void Reroll(int x, int z)
        {
            EnsureRoles();
            _reroll[z * Width + x]++;
        }

        // ───────────────────────── ГЕОМЕТРИЯ МОДУЛЯ ─────────────────────────

        public Vector2 ModuleSize() { return Grid != null ? Grid.CellSize() : new Vector2(250f, 250f); }

        /// <summary>Центр модуля на земле (мир) и поворот сетки.</summary>
        public void ModuleFrame(int x, int z, out Vector3 center, out Quaternion rot)
        {
            Transform gt = Grid != null ? Grid.transform : transform;
            Vector3 local = Grid != null ? Grid.CellLocal(x, z) : new Vector3((x - (Width - 1) * 0.5f) * 250f, 0f, (z - (Depth - 1) * 0.5f) * 250f);
            center = gt.TransformPoint(local) + gt.up * GroundOffset();
            rot = gt.rotation;
        }

        /// <summary>Высота верха плитки над объектом сетки (плитка ставится низом на землю).</summary>
        private float GroundOffset()
        {
            if (Grid == null || Grid.Variants == null) return 0f;
            for (int i = 0; i < Grid.Variants.Count; i++)
            {
                if (Grid.Variants[i] == null || Grid.Variants[i].Prefab == null) continue;
                Bounds b;
                if (TileGridGenerator.MeasurePrefab(Grid.Variants[i].Prefab, out b)) return Grid.SnapToGround ? b.size.y : b.max.y;
            }
            return 0f;
        }

        // ───────────────────────── ГЕНЕРАЦИЯ ─────────────────────────

        private struct Info
        {
            public GameObject Prefab;
            public Bounds B;            // в осях корня модели (с её поворотом/масштабом), без позиции
            public BuildingKind Kind;
            public float Weight;
            public float FacadeYaw;     // поворот, после которого фасад смотрит в +Z
            public Material Body;
            public int Mask;            // рабочие стороны (FacadeSides); 0 — неизвестно
            public bool Unique;
            public int Entry;
            public bool Key;            // ключевой (ближний слой)
            public bool Glow;           // ориентир с подсветкой
            public BuildingEntry E;     // запись списка (стиль окон)
        }

        private struct Rect2
        {
            public float X0, Z0, X1, Z1;
            public Rect2(float x0, float z0, float x1, float z1) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; }
            public float W { get { return X1 - X0; } }
            public float D { get { return Z1 - Z0; } }
            public bool Overlaps(Rect2 o, float pad) { return X0 < o.X1 + pad && X1 > o.X0 - pad && Z0 < o.Z1 + pad && Z1 > o.Z0 - pad; }
            public bool Inside(Rect2 o) { return X0 >= o.X0 - 0.001f && X1 <= o.X1 + 0.001f && Z0 >= o.Z0 - 0.001f && Z1 <= o.Z1 + 0.001f; }
        }

        private sealed class Ctx
        {
            public System.Random R;
            public CityLayer L;
            public List<Info> Infos = new List<Info>();
            public List<Rect2> Placed = new List<Rect2>();
            public List<GameObject> Buildings = new List<GameObject>();
            public List<Rect2> BuildingRects = new List<Rect2>();
            public List<BuildingKind> BuildingKinds = new List<BuildingKind>();
            public Transform Parent;
            public float S;            // размер модуля (меньшая сторона)
            public float Area;         // занято домами
            public int Towers, Rows, Inner, Props, Cars, Street, Stacked, Turned, Large;
            public GameObject LastPrefab;
            public Vector2 Player;
            public int Force = -1;              // поставить именно этот дом (ключевой из плана)
            public bool NoKeys, OnlyKeys;       // ближний слой: база без ключевых / только ключевые
            public bool NoStack;                // замена дома руками — без башенок из мелких
            public int RunPick = -1, RunLeft;   // «порядок»: один дом подряд 2–3 раза     // направление к уровню (игроку) в осях модуля, 0 — нет уровня
            public float Rnd() { return (float)R.NextDouble(); }
            public float Range(float a, float b) { return a + (b - a) * (float)R.NextDouble(); }
        }

        public Transform Container() { return transform.Find(ContainerName); }

        private Transform EnsureContainer()
        {
            Transform c = Container();
            if (c != null) return c;
            GameObject go = new GameObject(ContainerName);
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(go, "Застройка");
#endif
            go.transform.SetParent(transform, false);
            go.isStatic = MarkStatic;
            return go.transform;
        }

        public void ClearAll()
        {
            Transform c = Container();
            if (c != null) Kill(c.gameObject);
        }

        public void ClearModule(int x, int z)
        {
            Transform c = Container();
            if (c == null) return;
            Transform m = c.Find(ModuleName(x, z));
            if (m != null) Kill(m.gameObject);
        }

        public static string ModuleName(int x, int z) { return "Module_" + x + "_" + z; }

        private static void Kill(GameObject go)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { Undo.DestroyObjectImmediate(go); return; }
#endif
            Destroy(go);
        }

        /// <summary>Застроить все модули слоёв 1 и 2. Уровень и пустые не трогаем.</summary>
        public void GenerateAll()
        {
            EnsureRoles();
            if (!ManualLayer1) ClearAll();
            int mods = 0, houses = 0, kept = 0;
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                {
                    CityRole r = GetRole(x, z);
                    if (ManualLayer1 && r == CityRole.Ring1) { kept++; continue; }          // ближний слой — только вручную
                    if (r != CityRole.Ring1 && r != CityRole.Ring2) { if (ManualLayer1) ClearModule(x, z); continue; }
                    houses += GenerateModule(x, z, false);
                    mods++;
                }
            if (Log) Debug.Log($"[Застройка] «{name}»: модулей {mods}, домов {houses} · seed {Seed}" +
                               (ManualLayer1 ? $" · ближний слой не тронут ({kept} модулей) — окно «Модуль ближнего слоя»" : ""), this);
        }

        // ───────────────────────── БЛИЖНИЙ СЛОЙ ВРУЧНУЮ ─────────────────────────

        /// <summary>План модуля (ключевые здания). create — завести, если нет.</summary>
        public ModulePlan GetPlan(int x, int z, bool create)
        {
            if (Plans == null) Plans = new List<ModulePlan>();
            for (int i = 0; i < Plans.Count; i++) if (Plans[i] != null && Plans[i].X == x && Plans[i].Z == z) return Plans[i];
            if (!create) return null;
            ModulePlan p = new ModulePlan { X = x, Z = z };
            Plans.Add(p);
            return p;
        }

        /// <summary>Новый случайный вариант модуля (база и, если ключевые не выбраны, случайные ключевые) и сразу собрать.</summary>
        public int RandomizeModule(int x, int z)
        {
            Reroll(x, z);
            return GenerateModule(x, z, true);
        }

        public bool ModuleExists(int x, int z)
        {
            Transform c = Container();
            return c != null && c.Find(ModuleName(x, z)) != null;
        }

        /// <summary>Модуль по объекту в сцене (дом или сам модуль): ищем «Module_x_z» вверх по иерархии.</summary>
        public static bool FindModule(Transform t, out int x, out int z)
        {
            x = z = -1;
            for (; t != null; t = t.parent)
            {
                if (!t.name.StartsWith("Module_")) continue;
                string[] a = t.name.Split('_');
                if (a.Length >= 3 && int.TryParse(a[1], out x) && int.TryParse(a[2], out z)) return true;
            }
            return false;
        }

        private static void NightFromEntry(CityOfficeTower ot, BuildingEntry e)
        {
            ot.Mode = e.NightStyle;
            ot.FloorHeight = e.FloorHeight;
            ot.TopFloors = e.TopFloors;
        }

        /// <summary>Стили ночных окон из списка домов → на все уже стоящие дома (без перегенерации). Возвращает число.</summary>
        public int ApplyNightStyles()
        {
            Transform cont = Container();
            if (cont == null) return 0;
            Dictionary<string, BuildingEntry> byName = new Dictionary<string, BuildingEntry>();
            foreach (CityLayer l in new[] { Layer1, Layer2 })
                foreach (BuildingEntry e in l.Buildings)
                    if (e != null && e.Prefab != null && e.CustomNight && !byName.ContainsKey(e.Prefab.name)) byName[e.Prefab.name] = e;
            int n = 0;
            foreach (Transform m in cont)
                foreach (Transform b in m)
                {
                    if (!b.name.StartsWith("B_")) continue;
                    BuildingEntry e;
                    if (!byName.TryGetValue(b.name.Substring(2), out e)) continue;
                    CityOfficeTower ot = b.GetComponent<CityOfficeTower>();
                    if (ot == null) ot = b.gameObject.AddComponent<CityOfficeTower>();
                    NightFromEntry(ot, e);
                    ot.Apply();
                    n++;
                }
            if (Log) Debug.Log($"[Застройка] стили ночных окон применены: домов {n} (моделей со своим стилем {byName.Count})", this);
            return n;
        }

        /// <summary>Ключевой ли дом списка (Авто: небоскрёб, крупный, upper middle, уникальный).</summary>
        public bool IsKeyEntry(BuildingEntry e)
        {
            if (e == null || e.Prefab == null) return false;
            if (e.Role == KeyRole.Key) return true;
            if (e.Role == KeyRole.Base) return false;
            if (e.Unique) return true;
            BuildingKind k = e.Kind;
            if (k == BuildingKind.Auto)
            {
                Bounds b;
                if (TileGridGenerator.MeasurePrefab(e.Prefab, out b)) k = AutoKind(b, Mathf.Min(ModuleSize().x, ModuleSize().y));
            }
            if (k == BuildingKind.Tower || k == BuildingKind.Large) return true;
            string n = e.Prefab.name.ToLowerInvariant();
            return n.Contains("upper") || n.Contains("uppermiddle");
        }

        /// <summary>
        /// Заменить поставленный дом (опору «B_…») другой моделью из слоя: то же место и поворот, ужимается под место.
        /// Возвращает новую опору.
        /// </summary>
        public GameObject ReplaceBuilding(GameObject pivot, GameObject prefab)
        {
            if (pivot == null || prefab == null) return null;
            int mx, mz;
            if (!FindModule(pivot.transform, out mx, out mz)) return null;
            CityLayer layer = GetRole(mx, mz) == CityRole.Ring2 ? Layer2 : Layer1;
            int entry = -1;
            for (int i = 0; i < layer.Buildings.Count; i++) if (layer.Buildings[i] != null && layer.Buildings[i].Prefab == prefab) { entry = i; break; }
            Ctx c = new Ctx();
            c.L = layer;
            c.R = new System.Random(pivot.GetInstanceID());
            c.NoStack = true;
            BuildInfos(c);
            int pi = -1;
            for (int i = 0; i < c.Infos.Count; i++) if (c.Infos[i].Entry == entry || (entry < 0 && c.Infos[i].Prefab == prefab)) { pi = i; break; }
            if (pi < 0) return null;
            Info f = c.Infos[pi];

            // старое место: габарит моделей в осях опоры (с её масштабом)
            Transform pt = pivot.transform;
            Vector2 old = new Vector2(f.B.size.x, f.B.size.z);
            Renderer[] rs = pivot.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Vector3 mn = Vector3.one * float.MaxValue, mxv = Vector3.one * float.MinValue;
                foreach (Renderer r in rs)
                {
                    Bounds b = r.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 corner = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                        Vector3 l = pt.InverseTransformPoint(corner);
                        mn = Vector3.Min(mn, l); mxv = Vector3.Max(mxv, l);
                    }
                }
                Vector3 sc = pt.localScale;
                old = new Vector2((mxv.x - mn.x) * Mathf.Abs(sc.x), (mxv.z - mn.z) * Mathf.Abs(sc.z));
            }
            float u = Mathf.Min(1f, Mathf.Min(old.x / Mathf.Max(f.B.size.x, 1e-3f), old.y / Mathf.Max(f.B.size.z, 1e-3f)));
            u = Mathf.Max(u, MinFitScale * 0.6f);

            c.Parent = pt.parent;
            Vector3 lp = pt.localPosition;
            Quaternion lr = pt.localRotation;
            int sib = pt.GetSiblingIndex();
            bool tower = f.Kind == BuildingKind.Tower;
            Rect2 r2 = new Rect2(lp.x - old.x * 0.5f, lp.z - old.y * 0.5f, lp.x + old.x * 0.5f, lp.z + old.y * 0.5f);
            Place(c, pi, new Vector2(lp.x, lp.z), lr.eulerAngles.y, u, r2, tower);
            GameObject np = c.Buildings[c.Buildings.Count - 1];
            np.transform.localRotation = lr;                                        // без случайного доворота
            np.transform.localScale = new Vector3(u, u, u);
            np.transform.SetSiblingIndex(sib);
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(np, "Заменить дом");
#endif
            if (MarkStatic) SetStatic(np);
            Kill(pivot);
            if (Log) Debug.Log($"[Застройка] модуль {mx},{mz}: дом заменён на «{prefab.name}» (масштаб {u:0.00})", this);
            return np;
        }

        /// <summary>Застроить один модуль (старое в нём удаляется). Возвращает число домов.</summary>
        public int GenerateModule(int x, int z, bool logIt)
        {
            EnsureRoles();
            ClearModule(x, z);
            CityRole role = GetRole(x, z);
            if (role != CityRole.Ring1 && role != CityRole.Ring2) return 0;
            CityLayer layer = role == CityRole.Ring1 ? Layer1 : Layer2;

            Ctx c = new Ctx();
            c.L = layer;
            c.R = new System.Random(Hash(Seed, x, z, _reroll[z * Width + x]));
            BuildInfos(c);
            if (c.Infos.Count == 0)
            {
                if (Log) Debug.LogWarning($"[Застройка] «{name}»: в «{layer.Name}» нет домов — модуль {x},{z} пустой. Заполни список (меню «Заполнить слой из папки»)", this);
                return 0;
            }

            Vector3 center; Quaternion rot;
            ModuleFrame(x, z, out center, out rot);
            Transform cont = EnsureContainer();
            GameObject mgo = new GameObject(ModuleName(x, z));
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(mgo, "Застройка");
#endif
            mgo.transform.SetParent(cont, true);
            mgo.transform.SetPositionAndRotation(center, rot);
            mgo.isStatic = MarkStatic;
            c.Parent = mgo.transform;

            Vector2 ms = ModuleSize();
            c.S = Mathf.Min(ms.x, ms.y);
            float hx = ms.x * (0.5f - RoadMargin), hz = ms.y * (0.5f - RoadMargin);
            Rect2 lot = new Rect2(-hx, -hz, hx, hz);
            float sw = Sidewalk * c.S;
            Rect2 build = new Rect2(lot.X0 + sw, lot.Z0 + sw, lot.X1 - sw, lot.Z1 - sw);

            Vector3 lc;
            if (LevelCenter(out lc))
            {
                Vector3 lp = c.Parent.InverseTransformPoint(lc);
                Vector2 pd = new Vector2(lp.x, lp.z);
                c.Player = pd.sqrMagnitude > 1e-4f ? pd.normalized : Vector2.zero;
            }

            // кварталы
            List<Rect2> blocks = new List<Rect2>();
            Split(c, build, 0, blocks);
            bool manual = ManualLayer1 && role == CityRole.Ring1;
            if (manual)
            {
                // ближний слой вручную: сначала ключевые из плана (или случайные ключевые), дальше — только база
                ModulePlan plan = GetPlan(x, z, false);
                if (plan != null && plan.Keys != null && plan.Keys.Count > 0) PlaceKeys(c, plan, blocks, x, z);
                else if (plan == null || plan.RandomKeys)
                {
                    c.OnlyKeys = true;
                    int kt = c.R.Next(Mathf.Min(layer.Towers.x, layer.Towers.y), Mathf.Max(layer.Towers.x, layer.Towers.y) + 1);
                    for (int t = 0; t < kt; t++) PlaceInner(c, blocks, true);
                    int kl = c.R.Next(Mathf.Min(layer.LargePerModule.x, layer.LargePerModule.y), Mathf.Max(layer.LargePerModule.x, layer.LargePerModule.y) + 1);
                    for (int t = 0; t < kl; t++) PlaceLarge(c, blocks);
                    c.OnlyKeys = false;
                }
                c.NoKeys = true;
            }
            else
            {
            // уникальный дом (отсылка) — первым, у улицы к уровню
            int hero = HeroEntryFor(layer, x, z);
            if (hero >= 0) PlaceHero(c, hero, x, z, build, blocks);
            // небоскрёбы — сначала, во дворах (до рядов), чтобы им хватило места
            int towers = c.R.Next(Mathf.Min(layer.Towers.x, layer.Towers.y), Mathf.Max(layer.Towers.x, layer.Towers.y) + 1);
            for (int t = 0; t < towers; t++) PlaceInner(c, blocks, true);
            // ряды по краям кварталов
            // крупные — на углы кварталов (до рядов)
            int large = c.R.Next(Mathf.Min(layer.LargePerModule.x, layer.LargePerModule.y), Mathf.Max(layer.LargePerModule.x, layer.LargePerModule.y) + 1);
            for (int t = 0; t < large; t++) PlaceLarge(c, blocks);
            }
            for (int b = 0; b < blocks.Count; b++) PerimeterRows(c, blocks[b]);
            // двор
            float target = layer.Density * build.W * build.D;
            int attempts = 0;
            while (!layer.Tidy && c.Area < target && attempts < 60) { PlaceInner(c, blocks, false); attempts++; }

            // крыши, тротуар, машины
            CityProps cp = GetComponent<CityProps>();
            bool newProps = cp != null && cp.enabled;
            if (!newProps)                     // старые пропсы слоёв — только если нет компонента «Пропсы и машины»
            {
                RoofProps(c);
                StreetProps(c, lot, sw);
                Cars(c, lot, ms);
            }

            if (MarkStatic) SetStatic(mgo);
            if (newProps && cp.AutoWithCity && role == CityRole.Ring1) cp.GenerateModule(x, z);
            if (Log && logIt) Debug.Log($"[Застройка] «{name}»: модуль {x},{z} ({layer.Name}) — кварталов {blocks.Count}, домов {c.Buildings.Count} (небоскрёбов {c.Towers}, в рядах {c.Rows}, во дворе {c.Inner}, крупных на углах {c.Large}, башенок из мелких {c.Stacked}, повёрнуто окнами к улице {c.Turned}), на крышах {c.Props}, на тротуаре {c.Street}, машин {c.Cars}", this);
            return c.Buildings.Count;
        }

        /// <summary>Ключевые здания из плана: небоскрёбы — во двор, остальные — на угол квартала (не влезли — во двор).</summary>
        private void PlaceKeys(Ctx c, ModulePlan plan, List<Rect2> blocks, int x, int z)
        {
            foreach (GameObject key in plan.Keys)
            {
                if (key == null) continue;
                int pi = -1;
                for (int i = 0; i < c.Infos.Count; i++) if (c.Infos[i].Prefab == key) { pi = i; break; }
                if (pi < 0) { if (Log) Debug.LogWarning($"[Застройка] модуль {x},{z}: ключевого «{key.name}» нет в списке слоя 1", this); continue; }
                int before = c.Buildings.Count;
                c.Force = pi;
                if (c.Infos[pi].Kind == BuildingKind.Tower) PlaceInner(c, blocks, true);
                else
                {
                    PlaceLarge(c, blocks);
                    if (c.Buildings.Count == before) { c.Force = pi; PlaceInner(c, blocks, false); }
                }
                c.Force = -1;
                if (c.Buildings.Count == before && Log) Debug.LogWarning($"[Застройка] модуль {x},{z}: ключевой «{key.name}» не влез — убери лишние ключевые или «Случайно» другой вариант", this);
            }
        }

        private static int Hash(int seed, int x, int z, int k)
        {
            unchecked
            {
                int h = seed * 73856093 ^ x * 19349663 ^ z * 83492791 ^ k * 2654435;
                h ^= h >> 13; h *= 1274126177; h ^= h >> 16;
                return h;
            }
        }

        private void BuildInfos(Ctx c)
        {
            float s = Mathf.Min(ModuleSize().x, ModuleSize().y);
            for (int i = 0; i < c.L.Buildings.Count; i++)
            {
                BuildingEntry e = c.L.Buildings[i];
                if (e == null || e.Prefab == null || e.Weight <= 0f) continue;
                Bounds b;
                if (!TileGridGenerator.MeasurePrefab(e.Prefab, out b) || b.size.x < 0.001f || b.size.z < 0.001f) continue;
                Info inf = new Info();
                inf.Prefab = e.Prefab; inf.B = b; inf.Weight = e.Weight; inf.Body = e.Material;
                inf.Kind = e.Kind != BuildingKind.Auto ? e.Kind : AutoKind(b, s);
                if (e.Facade == FacadeDir.Auto) inf.FacadeYaw = b.size.z > b.size.x * 1.5f ? -90f : 0f;
                else inf.FacadeYaw = -(float)(int)e.Facade;
                inf.Mask = (int)e.Sides & 15;
                if (inf.Mask == 0 && e.Facade != FacadeDir.Auto) inf.Mask = 1 << (((int)e.Facade / 90) & 3);
                inf.Unique = e.Unique;
                inf.Entry = i;
                inf.Key = IsKeyEntry(e);
                inf.Glow = e.LandmarkGlow;
                inf.E = e;
                c.Infos.Add(inf);
            }
        }

        /// <summary>Тип по габариту (s — размер модуля).</summary>
        public static BuildingKind AutoKind(Bounds b, float s)
        {
            float w = Mathf.Max(b.size.x, b.size.z), n = Mathf.Min(b.size.x, b.size.z), h = b.size.y;
            if (h > 2f * w && h > 0.45f * s) return BuildingKind.Tower;
            if (w >= 2.5f * n && w > 0.25f * s) return BuildingKind.Row;
            if (w < 0.09f * s) return BuildingKind.Small;
            if (w < 0.25f * s) return BuildingKind.Mid;
            return BuildingKind.Large;
        }

        /// <summary>Делим участок на кварталы (до 2 уровней), между ними — переулок.</summary>
        private void Split(Ctx c, Rect2 r, int depth, List<Rect2> outList)
        {
            float alley = AlleyWidth * c.S;
            float minSide = 0.33f * c.S;
            bool canX = r.W >= minSide * 2f + alley, canZ = r.D >= minSide * 2f + alley;
            if (depth < 2 && (canX || canZ) && c.Rnd() < c.L.SubdivideChance * (depth == 0 ? 1f : 0.6f))
            {
                bool alongX = canX && (!canZ || (r.W > r.D ? c.Rnd() < 0.7f : c.Rnd() < 0.3f));
                float t = c.Range(0.38f, 0.62f);
                if (alongX)
                {
                    float m = r.X0 + (r.W - alley) * t;
                    Split(c, new Rect2(r.X0, r.Z0, m, r.Z1), depth + 1, outList);
                    Split(c, new Rect2(m + alley, r.Z0, r.X1, r.Z1), depth + 1, outList);
                }
                else
                {
                    float m = r.Z0 + (r.D - alley) * t;
                    Split(c, new Rect2(r.X0, r.Z0, r.X1, m), depth + 1, outList);
                    Split(c, new Rect2(r.X0, m + alley, r.X1, r.Z1), depth + 1, outList);
                }
                return;
            }
            outList.Add(r);
        }

        // ── выбор дома ──

        private int Pick(Ctx c, bool row, bool tower)
        {
            if (c.Force >= 0 && c.Force < c.Infos.Count) return c.Force;
            float total = 0f;
            float[] w = new float[c.Infos.Count];
            for (int i = 0; i < c.Infos.Count; i++)
            {
                Info f = c.Infos[i];
                float k = 0f;
                if (f.Unique) { w[i] = 0f; continue; }
                if ((c.NoKeys && f.Key) || (c.OnlyKeys && !f.Key)) { w[i] = 0f; continue; }
                if (tower) k = f.Kind == BuildingKind.Tower ? 1f : 0f;
                else if (row)
                {
                    if (f.Kind == BuildingKind.Small) k = c.L.RowKindWeights.x;
                    else if (f.Kind == BuildingKind.Mid) k = c.L.RowKindWeights.y;
                    else if (f.Kind == BuildingKind.Large) k = c.L.RowKindWeights.z;
                    else if (f.Kind == BuildingKind.Row) k = c.L.RowKindWeights.w;
                }
                else
                {
                    if (f.Kind == BuildingKind.Small) k = c.L.InnerKindWeights.x;
                    else if (f.Kind == BuildingKind.Mid) k = c.L.InnerKindWeights.y;
                    else if (f.Kind == BuildingKind.Large) k = c.L.InnerKindWeights.z;
                }
                if (f.Prefab == c.LastPrefab) k *= 0.25f;        // одинаковые подряд — реже
                w[i] = Mathf.Max(0f, k * f.Weight);
                total += w[i];
            }
            if (total <= 0f) return -1;
            float p = c.Rnd() * total;
            for (int i = 0; i < w.Length; i++) { p -= w[i]; if (p <= 0f && w[i] > 0f) return i; }
            for (int i = w.Length - 1; i >= 0; i--) if (w[i] > 0f) return i;
            return -1;
        }

        /// <summary>Габарит по X/Z в осях модуля при повороте yaw (кратно 90°) и масштабе.</summary>
        private static Vector2 Foot(Info f, float yaw, float u)
        {
            int q = ((Mathf.RoundToInt(yaw / 90f) % 4) + 4) % 4;
            return q % 2 == 0 ? new Vector2(f.B.size.x * u, f.B.size.z * u) : new Vector2(f.B.size.z * u, f.B.size.x * u);
        }

        // ── рабочие стороны ──

        private static Vector2 Snap(Vector2 d)
        {
            return Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Sign(d.x), 0f) : new Vector2(0f, Mathf.Sign(d.y));
        }

        /// <summary>
        /// Поворот (кратно 90°), при котором стороны с окнами смотрят на улицу (a, b — вес 3) и к игроку (вес playerW),
        /// а глухие — к соседям и во двор. Стороны неизвестны или все рабочие — прежний поворот fallback.
        /// </summary>
        private float ChooseYaw(Ctx c, Info f, float fallback, Vector2 a, Vector2 b, float playerW)
        {
            if (f.Mask == 0 || f.Mask == 15) return fallback;
            float best = float.MinValue; int bq = 0;
            int start = c.R.Next(4);
            for (int k = 0; k < 4; k++)
            {
                int q = (start + k) % 4;
                float sc = 0f;
                for (int i = 0; i < 4; i++)
                {
                    float ang = (i + q) * 90f * Mathf.Deg2Rad;
                    Vector2 d = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
                    bool work = (f.Mask & (1 << i)) != 0;
                    if (a != Vector2.zero && Vector2.Dot(d, a) > 0.7f) sc += work ? 3f : -3f;
                    if (b != Vector2.zero && Vector2.Dot(d, b) > 0.7f) sc += work ? 3f : -3f;
                    if (playerW > 0f && c.Player != Vector2.zero) sc += (work ? playerW : -playerW) * Mathf.Max(0f, Vector2.Dot(d, c.Player));
                }
                if (sc > best + 1e-3f) { best = sc; bq = q; }
            }
            float yaw = bq * 90f;
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, fallback)) > 1f) c.Turned++;
            return yaw;
        }

        /// <summary>Центр уровня (среднее модулей «уровень»), в мире.</summary>
        public bool LevelCenter(out Vector3 p)
        {
            p = Vector3.zero; int n = 0;
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                    if (GetRole(x, z) == CityRole.Level) { Vector3 cc; Quaternion r; ModuleFrame(x, z, out cc, out r); p += cc; n++; }
            if (n == 0) return false;
            p /= n;
            return true;
        }

        // ── уникальные дома ──

        private static bool Near(CityBlockGenerator g, int x, int z, CityRole r)
        {
            return (x > 0 && g.GetRole(x - 1, z) == r) || (x < g.Width - 1 && g.GetRole(x + 1, z) == r) ||
                   (z > 0 && g.GetRole(x, z - 1) == r) || (z < g.Depth - 1 && g.GetRole(x, z + 1) == r);
        }

        /// <summary>Какой уникальный дом слоя (индекс в списке) ставить в модуль x,z; −1 — никакой. Детерминировано от seed.</summary>
        public int HeroEntryFor(CityLayer layer, int x, int z)
        {
            CityRole role = layer == Layer1 ? CityRole.Ring1 : CityRole.Ring2;
            CityRole near = layer == Layer1 ? CityRole.Level : CityRole.Ring1;
            List<int> mods = new List<int>();
            for (int zz = 0; zz < Depth; zz++)
                for (int xx = 0; xx < Width; xx++)
                    if (GetRole(xx, zz) == role && Near(this, xx, zz, near)) mods.Add(zz * Width + xx);
            if (mods.Count == 0)
                for (int zz = 0; zz < Depth; zz++)
                    for (int xx = 0; xx < Width; xx++)
                        if (GetRole(xx, zz) == role) mods.Add(zz * Width + xx);
            System.Random r = new System.Random(Hash(Seed, 911, layer == Layer1 ? 1 : 2, 0));
            for (int i = 0; i < layer.Buildings.Count; i++)
            {
                BuildingEntry e = layer.Buildings[i];
                if (e == null || e.Prefab == null || !e.Unique || mods.Count == 0) continue;
                int k = r.Next(mods.Count);
                int m = mods[k];
                mods.RemoveAt(k);
                if (m == z * Width + x) return i;
            }
            return -1;
        }

        /// <summary>Уникальный дом: у края участка, смотрящего на уровень, по центру квартала; без разброса размера и поворота.</summary>
        private void PlaceHero(Ctx c, int entry, int x, int z, Rect2 build, List<Rect2> blocks)
        {
            int pi = -1;
            for (int i = 0; i < c.Infos.Count; i++) if (c.Infos[i].Entry == entry) { pi = i; break; }
            if (pi < 0) return;
            Info f = c.Infos[pi];
            Vector2 d = c.Player != Vector2.zero ? Snap(c.Player) : new Vector2(0, 1);
            float yaw = f.Mask != 0 && f.Mask != 15 ? ChooseYaw(c, f, 0f, d, Vector2.zero, 1f) : Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + f.FacadeYaw;
            float u = 1f;
            Vector2 fp = Foot(f, yaw, u);
            // квартал у нужного края
            Rect2 blk = build;
            float bestEdge = float.MinValue;
            for (int i = 0; i < blocks.Count; i++)
            {
                Rect2 b = blocks[i];
                float e = d.x > 0.5f ? b.X1 : d.x < -0.5f ? -b.X0 : d.y > 0.5f ? b.Z1 : -b.Z0;
                float room = Mathf.Min(b.W / Mathf.Max(fp.x, 1e-3f), b.D / Mathf.Max(fp.y, 1e-3f));
                if (room >= 1f && e > bestEdge + 0.01f) { bestEdge = e; blk = b; }
            }
            float fit = Mathf.Min(1f, Mathf.Min(blk.W / Mathf.Max(fp.x, 1e-3f), blk.D / Mathf.Max(fp.y, 1e-3f)));
            if (fit < 1f) { blk = build; fit = Mathf.Min(1f, Mathf.Min(build.W / fp.x, build.D / fp.y)); }
            u *= fit; fp *= fit;
            float cx = (blk.X0 + blk.X1) * 0.5f, cz = (blk.Z0 + blk.Z1) * 0.5f;
            if (d.x > 0.5f) cx = blk.X1 - fp.x * 0.5f; else if (d.x < -0.5f) cx = blk.X0 + fp.x * 0.5f;
            else if (d.y > 0.5f) cz = blk.Z1 - fp.y * 0.5f; else cz = blk.Z0 + fp.y * 0.5f;
            Rect2 r = new Rect2(cx - fp.x * 0.5f, cz - fp.y * 0.5f, cx + fp.x * 0.5f, cz + fp.y * 0.5f);
            Place(c, pi, new Vector2(cx, cz), yaw, u, r, true);
            c.Towers++;
            if (Log) Debug.Log($"[Застройка] уникальный дом «{f.Prefab.name}» → модуль {x},{z}, фасадом к уровню", this);
        }

        // ── ряды по краю квартала ──

        private void PerimeterRows(Ctx c, Rect2 blk)
        {
            float maxDepth = Mathf.Min(blk.W, blk.D) * 0.48f;
            Corners(c, blk, maxDepth);
            // стороны: 0 — юг (−Z), 1 — восток (+X), 2 — север (+Z), 3 — запад (−X); фасад — наружу
            int start = c.R.Next(4);
            for (int k = 0; k < 4; k++)
            {
                int side = (start + k) % 4;
                Vector2 a, t, n;
                float len;
                switch (side)
                {
                    case 0: a = new Vector2(blk.X0, blk.Z0); t = new Vector2(1, 0); n = new Vector2(0, -1); len = blk.W; break;
                    case 1: a = new Vector2(blk.X1, blk.Z0); t = new Vector2(0, 1); n = new Vector2(1, 0); len = blk.D; break;
                    case 2: a = new Vector2(blk.X1, blk.Z1); t = new Vector2(-1, 0); n = new Vector2(0, 1); len = blk.W; break;
                    default: a = new Vector2(blk.X0, blk.Z1); t = new Vector2(0, -1); n = new Vector2(-1, 0); len = blk.D; break;
                }
                float facing = Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg;      // yaw, при котором +Z смотрит по n
                bool sideX = Mathf.Abs(t.x) > 0.5f;
                bool tidy = c.L.Tidy;
                float cur = tidy ? 0f : c.Range(0f, 0.02f) * c.S;
                c.RunPick = -1; c.RunLeft = 0;
                float limit = len, limitEnd = -1f;                           // впереди помеха: заполнить до неё, потом перепрыгнуть
                int fails = 0;
                while (cur < len - 0.04f * c.S && fails < 12)
                {
                    float room = limit - cur;
                    if (room < 0.045f * c.S)
                    {
                        if (limitEnd >= 0f) { cur = limitEnd + (tidy ? 0f : c.Range(0f, 0.004f) * c.S); limit = len; limitEnd = -1f; continue; }
                        break;
                    }
                    int pi;
                    if (tidy && c.RunLeft > 0 && c.RunPick >= 0) { pi = c.RunPick; c.RunLeft--; }
                    else
                    {
                        pi = Pick(c, true, false);
                        if (tidy) { c.RunPick = pi; c.RunLeft = c.R.Next(1, 3); }
                    }
                    if (pi < 0) return;
                    Info f = c.Infos[pi];
                    float yaw = ChooseYaw(c, f, facing + f.FacadeYaw, n, Vector2.zero, 0.8f);
                    float u = 1f + (tidy ? 0f : c.Range(-ScaleJitter, ScaleJitter));
                    Vector2 fp = Foot(f, yaw, u);
                    float front = sideX ? fp.x : fp.y, depth = sideX ? fp.y : fp.x;
                    if (front > room)
                    {
                        float fit = room / front;
                        if (fit < (tidy ? Mathf.Min(MinFitScale, 0.7f) : MinFitScale))
                        {
                            fails++; c.RunLeft = 0;
                            if (fails % 4 == 0 && limitEnd >= 0f) { cur = limitEnd; limit = len; limitEnd = -1f; }
                            continue;
                        }
                        u *= fit; front *= fit; depth *= fit;
                    }
                    if (depth > maxDepth) { fails++; continue; }
                    float setback = tidy ? 0f : c.Range(0f, SetbackJitter) * c.S;
                    Vector2 mid = a + t * (cur + front * 0.5f) - n * (setback + depth * 0.5f);
                    float fx = sideX ? front : depth, fz = sideX ? depth : front;
                    Rect2 r = new Rect2(mid.x - fx * 0.5f, mid.y - fz * 0.5f, mid.x + fx * 0.5f, mid.y + fz * 0.5f);
                    if (!r.Inside(blk)) { fails++; continue; }
                    float s0 = float.MaxValue, e0 = -1f;
                    bool hit = false;
                    for (int i = 0; i < c.Placed.Count; i++)
                    {
                        if (!r.Overlaps(c.Placed[i], -0.01f)) continue;              // впритык — можно (сплошной ряд)
                        hit = true;
                        float ps, pe;
                        Along(c.Placed[i], a, t, out ps, out pe);
                        s0 = Mathf.Min(s0, ps); e0 = Mathf.Max(e0, pe);
                    }
                    if (hit)
                    {
                        if (s0 > cur + 0.01f) { limit = Mathf.Min(limit, s0); limitEnd = Mathf.Max(limitEnd, e0); }
                        else { cur = e0 + 0.001f; limit = len; limitEnd = -1f; }
                        fails++;
                        continue;
                    }
                    Place(c, pi, mid, yaw, u, r, false);
                    c.Rows++;
                    fails = 0;
                    cur += front;
                    if (tidy) { }
                    else if (c.Rnd() < c.L.AlleyChance) cur += AlleyWidth * c.S * c.Range(0.6f, 1.2f);
                    else cur += c.Range(0f, 0.004f) * c.S;
                }
            }
        }

        /// <summary>Отрезок прямоугольника вдоль края (от точки a по направлению t).</summary>
        private static void Along(Rect2 p, Vector2 a, Vector2 t, out float s, out float e)
        {
            if (t.x > 0.5f) { s = p.X0 - a.x; e = p.X1 - a.x; }
            else if (t.x < -0.5f) { s = a.x - p.X1; e = a.x - p.X0; }
            else if (t.y > 0.5f) { s = p.Z0 - a.y; e = p.Z1 - a.y; }
            else { s = a.y - p.Z1; e = a.y - p.Z0; }
        }

        /// <summary>Угловые дома квартала — сначала, ровно в углы (фасад на одну из двух улиц).</summary>
        private void Corners(Ctx c, Rect2 blk, float maxDepth)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                for (int tries = 0; tries < (c.L.Tidy ? 14 : 6); tries++)
                {
                    int pi = PickCorner(c);
                    if (pi < 0) return;
                    Info f = c.Infos[pi];
                    int ex = (corner == 0 || corner == 3) ? -1 : 1, ez = (corner == 0 || corner == 1) ? -1 : 1;
                    Vector2 n = c.Rnd() < 0.5f ? new Vector2(ex, 0) : new Vector2(0, ez);
                    float yaw = ChooseYaw(c, f, Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg + f.FacadeYaw, new Vector2(ex, 0), new Vector2(0, ez), 0.8f);
                    float u = 1f + (c.L.Tidy ? 0f : c.Range(-ScaleJitter, ScaleJitter));
                    Vector2 fp = Foot(f, yaw, u);
                    if (fp.x > maxDepth || fp.y > maxDepth) continue;
                    float sx = c.L.Tidy ? 0f : c.Range(0f, SetbackJitter) * c.S, sz = c.L.Tidy ? 0f : c.Range(0f, SetbackJitter) * c.S;
                    float x0 = ex < 0 ? blk.X0 + sx : blk.X1 - sx - fp.x;
                    float z0 = ez < 0 ? blk.Z0 + sz : blk.Z1 - sz - fp.y;
                    Rect2 r = new Rect2(x0, z0, x0 + fp.x, z0 + fp.y);
                    if (Hits(c, r, 0.004f * c.S)) continue;
                    Place(c, pi, new Vector2((r.X0 + r.X1) * 0.5f, (r.Z0 + r.Z1) * 0.5f), yaw, u, r, false);
                    c.Rows++;
                    break;
                }
            }
        }

        private int PickCorner(Ctx c)
        {
            float total = 0f;
            float[] w = new float[c.Infos.Count];
            for (int i = 0; i < c.Infos.Count; i++)
            {
                Info f = c.Infos[i];
                float k = f.Unique ? 0f : f.Kind == BuildingKind.Small ? c.L.RowKindWeights.x : f.Kind == BuildingKind.Mid ? c.L.RowKindWeights.y : f.Kind == BuildingKind.Large ? c.L.RowKindWeights.z : 0f;
                if ((c.NoKeys && f.Key) || (c.OnlyKeys && !f.Key)) k = 0f;
                if (f.Prefab == c.LastPrefab) k *= 0.25f;
                if (c.L.Tidy && Adjacent2(f.Mask)) k *= 4f;                 // на угол — дома с окнами на две соседние стороны
                w[i] = Mathf.Max(0f, k * f.Weight); total += w[i];
            }
            if (total <= 0f) return -1;
            float p = c.Rnd() * total;
            for (int i = 0; i < w.Length; i++) { p -= w[i]; if (p <= 0f && w[i] > 0f) return i; }
            for (int i = w.Length - 1; i >= 0; i--) if (w[i] > 0f) return i;
            return -1;
        }

        /// <summary>
        /// Крупный дом на угол квартала: в ряды не влезает по глубине, поэтому ставится первым — вплотную к двум улицам,
        /// окнами на них; если велик для квартала — чуть уменьшается (не меньше MinFitScale × 0.9).
        /// </summary>
        private void PlaceLarge(Ctx c, List<Rect2> blocks)
        {
            if (blocks.Count == 0) return;
            float total = 0f;
            List<int> ids = new List<int>();
            // «крупные» — по площади основания: в 1.5 раза больше обычного среднего дома слоя (upper middle и т. п.)
            List<float> areas = new List<float>();
            for (int i = 0; i < c.Infos.Count; i++)
            {
                Info f = c.Infos[i];
                if (f.Kind == BuildingKind.Mid || f.Kind == BuildingKind.Large) areas.Add(f.B.size.x * f.B.size.z);
            }
            if (areas.Count == 0) return;
            areas.Sort();
            float big = areas[areas.Count / 2] * 1.5f;
            for (int i = 0; i < c.Infos.Count; i++)
            {
                Info f = c.Infos[i];
                if (f.Unique || f.Weight <= 0f || f.Kind == BuildingKind.Tower || f.Kind == BuildingKind.Row || f.Kind == BuildingKind.Small) continue;
                if (f.B.size.x * f.B.size.z < big) continue;
                if ((c.NoKeys && f.Key) || (c.OnlyKeys && !f.Key)) continue;
                ids.Add(i); total += f.Weight;
            }
            if (c.Force >= 0 && c.Force < c.Infos.Count)
            {
                ids.Clear(); ids.Add(c.Force); total = Mathf.Max(c.Infos[c.Force].Weight, 0.01f);   // ключевой из плана — именно он
            }
            if (ids.Count == 0) return;
            for (int tries = 0; tries < 10; tries++)
            {
                float p = c.Rnd() * total; int pi = ids[ids.Count - 1];
                foreach (int i in ids) { p -= c.Infos[i].Weight; if (p <= 0f) { pi = i; break; } }
                Info f = c.Infos[pi];
                Rect2 blk = blocks[c.R.Next(blocks.Count)];
                int corner = c.R.Next(4);
                int ex = (corner == 0 || corner == 3) ? -1 : 1, ez = (corner == 0 || corner == 1) ? -1 : 1;
                float yaw = ChooseYaw(c, f, Mathf.Atan2(ex, 0f) * Mathf.Rad2Deg + f.FacadeYaw, new Vector2(ex, 0), new Vector2(0, ez), 0.8f);
                float u = 1f;
                Vector2 fp = Foot(f, yaw, u);
                float fit = Mathf.Min(1f, Mathf.Min(blk.W * 0.95f / fp.x, blk.D * 0.95f / fp.y));
                if (fit < MinFitScale * 0.9f) continue;
                u *= fit; fp *= fit;
                float x0 = ex < 0 ? blk.X0 : blk.X1 - fp.x, z0 = ez < 0 ? blk.Z0 : blk.Z1 - fp.y;
                Rect2 r = new Rect2(x0, z0, x0 + fp.x, z0 + fp.y);
                if (Hits(c, r, 0.004f * c.S)) continue;
                Place(c, pi, new Vector2((r.X0 + r.X1) * 0.5f, (r.Z0 + r.Z1) * 0.5f), yaw, u, r, false);
                c.Rows++; c.Large++;
                return;
            }
        }

        /// <summary>Окна ровно на двух соседних сторонах (угловой дом).</summary>
        private static bool Adjacent2(int m)
        {
            return m == 3 || m == 6 || m == 12 || m == 9;
        }

        // ── двор ──

        private void PlaceInner(Ctx c, List<Rect2> blocks, bool tower)
        {
            if (blocks.Count == 0) return;
            for (int tries = 0; tries < 18; tries++)
            {
                int pi = Pick(c, false, tower);
                if (pi < 0) return;
                Info f = c.Infos[pi];
                Rect2 blk = blocks[c.R.Next(blocks.Count)];
                if (c.L.Tidy)
                {
                    // самый большой квартал, ещё без небоскрёба
                    float ba = -1f;
                    for (int b = 0; b < blocks.Count; b++)
                    {
                        Rect2 bb = blocks[b];
                        Rect2 core = new Rect2((bb.X0 + bb.X1) * 0.5f - 1f, (bb.Z0 + bb.Z1) * 0.5f - 1f, (bb.X0 + bb.X1) * 0.5f + 1f, (bb.Z0 + bb.Z1) * 0.5f + 1f);
                        if (bb.W * bb.D > ba && !Hits(c, core, 0f)) { ba = bb.W * bb.D; blk = bb; }
                    }
                    if (ba < 0f) return;
                }
                float yaw = f.Mask != 0 && f.Mask != 15 && c.Player != Vector2.zero
                    ? ChooseYaw(c, f, 0f, Snap(c.Player), Vector2.zero, 0f)
                    : c.R.Next(4) * 90f;
                float u = 1f + (c.L.Tidy ? 0f : c.Range(-ScaleJitter, ScaleJitter));
                Vector2 fp = Foot(f, yaw, u);
                if (fp.x > blk.W || fp.y > blk.D) continue;
                float mx = c.L.Tidy ? (blk.X0 + blk.X1) * 0.5f : c.Range(blk.X0 + fp.x * 0.5f, blk.X1 - fp.x * 0.5f);
                float mz = c.L.Tidy ? (blk.Z0 + blk.Z1) * 0.5f : c.Range(blk.Z0 + fp.y * 0.5f, blk.Z1 - fp.y * 0.5f);
                Rect2 r = new Rect2(mx - fp.x * 0.5f, mz - fp.y * 0.5f, mx + fp.x * 0.5f, mz + fp.y * 0.5f);
                if (Hits(c, r, 0.025f * c.S)) continue;
                Place(c, pi, new Vector2(mx, mz), yaw, u, r, tower);
                if (tower) c.Towers++; else c.Inner++;
                return;
            }
        }

        private static bool Hits(Ctx c, Rect2 r, float pad)
        {
            for (int i = 0; i < c.Placed.Count; i++) if (r.Overlaps(c.Placed[i], pad)) return true;
            return false;
        }

        /// <summary>Поставить дом: пустышка-опора (центр габарита на земле, поворот, масштаб) → внутри модель.</summary>
        private void Place(Ctx c, int pi, Vector2 mid, float yaw, float u, Rect2 r, bool tower)
        {
            Info f = c.Infos[pi];
            float hj = tower ? TowerHeightJitter : HeightJitter;
            float hy = 1f + (hj > 0f ? c.Range(-hj * 0.5f, hj) : 0f);
            float jy = YawJitter > 0f ? c.Range(-YawJitter, YawJitter) : 0f;
            if (f.Unique) { hy = 1f; jy = 0f; }
            if (c.L.Tidy) { jy = 0f; hy = 1f + c.Range(-0.03f, 0.05f); }
            // мелкий дом — башенкой из одинаковых (один цвет), как модуль средней этажки
            int levels = 1;
            if (!tower && !c.NoStack && !f.Unique && f.Kind == BuildingKind.Small && c.Rnd() < c.L.StackSmallChance)
            {
                int lo = Mathf.Max(2, Mathf.Min(c.L.StackLevels.x, c.L.StackLevels.y)), hi = Mathf.Max(lo, Mathf.Max(c.L.StackLevels.x, c.L.StackLevels.y));
                levels = c.R.Next(lo, hi + 1);
                hy = 1f;
            }

            GameObject pivot = new GameObject("B_" + f.Prefab.name);
            pivot.transform.SetParent(c.Parent, false);
            pivot.transform.localPosition = new Vector3(mid.x, 0f, mid.y);
            pivot.transform.localRotation = Quaternion.Euler(0f, yaw + jy, 0f);
            pivot.transform.localScale = new Vector3(u, u * hy, u);

            GameObject go = Spawn(f.Prefab, pivot.transform);
            go.transform.localRotation = f.Prefab.transform.localRotation;
            go.transform.localScale = f.Prefab.transform.localScale;
            go.transform.localPosition = -new Vector3(f.B.center.x, f.B.min.y, f.B.center.z);
            ApplyMaterials(go, f.Body, GlassMaterial, GlassMeshes);
            for (int k = 1; k < levels; k++)
            {
                GameObject up = Spawn(f.Prefab, pivot.transform);
                up.name = f.Prefab.name + " (этаж " + (k + 1) + ")";
                up.transform.localRotation = f.Prefab.transform.localRotation;
                up.transform.localScale = f.Prefab.transform.localScale;
                up.transform.localPosition = go.transform.localPosition + Vector3.up * (f.B.size.y * k);
                ApplyMaterials(up, f.Body, GlassMaterial, GlassMeshes);
            }
            if (levels > 1) c.Stacked++;
            if ((tower && OfficeTowers) || (f.E != null && f.E.CustomNight))
            {
                CityOfficeTower ot = pivot.AddComponent<CityOfficeTower>();
                if (f.E != null && f.E.CustomNight) { NightFromEntry(ot, f.E); ot.Apply(); }
            }
            if (f.Glow)
            {
                CityLandmarkGlow lg = pivot.AddComponent<CityLandmarkGlow>();
                if (LandmarkGlowMaterial != null) { lg.GlowMaterial = LandmarkGlowMaterial; lg.Apply(); }
            }

            c.Placed.Add(r);
            c.Buildings.Add(pivot);
            c.BuildingRects.Add(r);
            c.BuildingKinds.Add(f.Kind);
            c.Area += r.W * r.D;
            c.LastPrefab = f.Prefab;
        }

        /// <summary>Стёкла (часть glass… или материал стекла) — материал стекла; части без текстуры — материал стен.</summary>
        public static void ApplyMaterials(GameObject go, Material body, Material glass, CityGlassMeshes meshes)
        {
            if (body == null && glass == null) return;
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                string nm = rs[i].gameObject.name.ToLowerInvariant();
                bool glassObj = nm.StartsWith("glass") || nm.Contains("стекл") || nm.Contains("window");
                Material[] m = rs[i].sharedMaterials;
                bool ch = false;
                for (int k = 0; k < m.Length; k++)
                {
                    bool glassMat = IsGlassMat(m[k]);
                    if ((glassObj || glassMat) && glass != null) { if (m[k] != glass) { m[k] = glass; ch = true; } }
                    else if (body != null && !glassObj && !glassMat && !Textured(m[k])) { m[k] = body; ch = true; }
                }
                if (ch) rs[i].sharedMaterials = m;
                // размеченный меш стекла (у каждого окна свои координаты и случайное)
                if (meshes != null && (glassObj || HasGlassMat(m)))
                {
                    MeshFilter mf = rs[i].GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && !meshes.IsBaked(mf.sharedMesh))
                    {
                        Mesh bm = meshes.Get(mf.sharedMesh);
                        if (bm != null) mf.sharedMesh = bm;
                    }
                }
            }
        }

        /// <summary>Материал стекла: шейдер стекла или имя со «glass / стекл / uv_grid» (TT_checker_512x512_UV_GRID у стёкол
        /// моделей). НЕ просто «checker» — атлас небоскрёбов называется TT_checker_map_gravity, это стены.</summary>
        public static bool IsGlassMat(Material m)
        {
            if (m == null) return false;
            if (m.shader != null && m.shader.name == "CatCooks/City/Window Glass") return true;
            if (m.shader != null && m.shader.name == "CatCooks/City/Building") return false;
            string mn = m.name.ToLowerInvariant();
            return mn.Contains("glass") || mn.Contains("стекл") || mn.Contains("uv_grid");
        }

        private static bool HasGlassMat(Material[] ms)
        {
            for (int k = 0; k < ms.Length; k++)
            {
                if (ms[k] == null) continue;
                if (IsGlassMat(ms[k])) return true;
            }
            return false;
        }

        public static bool Textured(Material m)
        {
            if (m == null) return false;
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return true;
            if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) return true;
            return false;
        }

        public static GameObject Spawn(GameObject prefab, Transform parent)
        {
            GameObject go = null;
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(prefab))
                go = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
#endif
            if (go == null) go = Instantiate(prefab, parent);
            return go;
        }

        // ── пропсы на крышах ──

        private void RoofProps(Ctx c)
        {
            List<GameObject> props = Valid(c.L.RoofProps);
            if (props.Count == 0 || c.L.RoofPropsMax <= 0) return;
            // временные коллайдеры — чтобы луч нашёл настоящую крышу (уступы, парапеты, шпили)
            List<MeshCollider> temp = new List<MeshCollider>();
            for (int i = 0; i < c.Buildings.Count; i++)
            {
                MeshFilter[] mfs = c.Buildings[i].GetComponentsInChildren<MeshFilter>(true);
                for (int k = 0; k < mfs.Length; k++)
                {
                    if (mfs[k].sharedMesh == null || mfs[k].GetComponent<Collider>() != null) continue;
                    MeshCollider mc = mfs[k].gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mfs[k].sharedMesh;
                    temp.Add(mc);
                }
            }
            Physics.SyncTransforms();
            Dictionary<GameObject, Bounds> pb = new Dictionary<GameObject, Bounds>();
            for (int i = 0; i < props.Count; i++)
            {
                Bounds b;
                if (TileGridGenerator.MeasurePrefab(props[i], out b)) pb[props[i]] = b;
            }

            for (int i = 0; i < c.Buildings.Count; i++)
            {
                if (c.Rnd() > c.L.RoofPropChance) continue;
                Rect2 r = c.BuildingRects[i];
                bool tower = c.BuildingKinds[i] == BuildingKind.Tower;
                int n = 1 + c.R.Next(Mathf.Max(1, tower ? Mathf.Min(3, c.L.RoofPropsMax) : c.L.RoofPropsMax));
                List<Vector3> used = new List<Vector3>();
                List<float> usedR = new List<float>();
                for (int k = 0; k < n * 3 && used.Count < n; k++)
                {
                    GameObject p = props[c.R.Next(props.Count)];
                    Bounds b;
                    if (!pb.TryGetValue(p, out b)) continue;
                    float pr = Mathf.Max(b.size.x, b.size.z) * 0.5f;
                    if (pr * 2f > Mathf.Min(r.W, r.D) * 0.8f) continue;       // большой щит на маленький дом — нет
                    float lx = c.Range(r.X0 + pr, r.X1 - pr), lz = c.Range(r.Z0 + pr, r.Z1 - pr);
                    Vector3 top = c.Parent.TransformPoint(new Vector3(lx, 0f, lz)) + c.Parent.up * 2000f;
                    RaycastHit hit;
                    if (!RoofHit(c.Buildings[i], top, -c.Parent.up, out hit)) continue;
                    if (Vector3.Dot(hit.normal, c.Parent.up) < 0.9f) continue;
                    // по краю тоже должна быть крыша на той же высоте (не свешиваемся с уступа)
                    if (!SameLevel(c.Buildings[i], hit.point, c.Parent, pr)) continue;
                    bool clash = false;
                    for (int q = 0; q < used.Count; q++) if ((used[q] - hit.point).magnitude < usedR[q] + pr) { clash = true; break; }
                    if (clash) continue;
                    GameObject pivot = new GameObject("P_" + p.name);
                    pivot.transform.SetParent(c.Buildings[i].transform, true);
                    pivot.transform.position = hit.point;
                    pivot.transform.rotation = c.Parent.rotation * Quaternion.Euler(0f, c.R.Next(4) * 90f + c.Range(-4f, 4f), 0f);
                    Vector3 ls = pivot.transform.lossyScale;
                    pivot.transform.localScale = new Vector3(1f / Mathf.Max(0.001f, ls.x), 1f / Mathf.Max(0.001f, ls.y), 1f / Mathf.Max(0.001f, ls.z));
                    GameObject go = Spawn(p, pivot.transform);
                    go.transform.localRotation = p.transform.localRotation;
                    go.transform.localScale = p.transform.localScale;
                    go.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z);
                    used.Add(hit.point); usedR.Add(pr);
                    c.Props++;
                }
            }
            if (!KeepColliders)
                for (int i = 0; i < temp.Count; i++) if (temp[i] != null) DestroyImmediate(temp[i]);
        }

        private static bool RoofHit(GameObject building, Vector3 from, Vector3 dir, out RaycastHit best)
        {
            best = new RaycastHit();
            RaycastHit[] hits = Physics.RaycastAll(from, dir, 4000f, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue; bool ok = false;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null || !hits[i].collider.transform.IsChildOf(building.transform)) continue;
                if (hits[i].distance < bd) { bd = hits[i].distance; best = hits[i]; ok = true; }
            }
            return ok;
        }

        private static bool SameLevel(GameObject building, Vector3 p, Transform frame, float r)
        {
            Vector3 up = frame.up;
            for (int k = 0; k < 4; k++)
            {
                Vector3 off = frame.rotation * (k == 0 ? new Vector3(r, 0, 0) : k == 1 ? new Vector3(-r, 0, 0) : k == 2 ? new Vector3(0, 0, r) : new Vector3(0, 0, -r));
                RaycastHit h;
                if (!RoofHit(building, p + off + up * 5f, -up, out h)) return false;
                if (Mathf.Abs(Vector3.Dot(h.point - p, up)) > r * 0.25f + 0.2f) return false;
            }
            return true;
        }

        // ── тротуар ──

        private void StreetProps(Ctx c, Rect2 lot, float sw)
        {
            List<GameObject> props = Valid(c.L.StreetProps);
            if (props.Count == 0 || c.L.StreetPropsPer100 <= 0f || sw <= 0f) return;
            Dictionary<GameObject, Bounds> pb = new Dictionary<GameObject, Bounds>();
            for (int i = 0; i < props.Count; i++) { Bounds b; if (TileGridGenerator.MeasurePrefab(props[i], out b)) pb[props[i]] = b; }
            List<Rect2> mine = new List<Rect2>();
            for (int side = 0; side < 4; side++)
            {
                bool sx = side % 2 == 0;
                float len = sx ? lot.W : lot.D;
                int count = Mathf.RoundToInt(len / 100f * c.L.StreetPropsPer100 * c.Range(0.7f, 1.3f));
                for (int k = 0; k < count; k++)
                {
                    GameObject p = props[c.R.Next(props.Count)];
                    Bounds b;
                    if (!pb.TryGetValue(p, out b)) continue;
                    float along = c.Range(0.06f, 0.94f) * len;
                    float off = sw * c.Range(0.3f, 0.7f);
                    Vector2 pos;
                    float yaw;
                    switch (side)
                    {
                        case 0: pos = new Vector2(lot.X0 + along, lot.Z0 + off); yaw = 180f; break;
                        case 1: pos = new Vector2(lot.X1 - off, lot.Z0 + along); yaw = 90f; break;
                        case 2: pos = new Vector2(lot.X1 - along, lot.Z1 - off); yaw = 0f; break;
                        default: pos = new Vector2(lot.X0 + off, lot.Z1 - along); yaw = 270f; break;
                    }
                    Vector2 fp = Foot(new Info { B = b }, yaw, 1f);
                    Rect2 r = new Rect2(pos.x - fp.x * 0.5f, pos.y - fp.y * 0.5f, pos.x + fp.x * 0.5f, pos.y + fp.y * 0.5f);
                    if (Hits(c, r, 0.2f)) continue;
                    bool clash = false;
                    for (int q = 0; q < mine.Count; q++) if (r.Overlaps(mine[q], 0.5f)) { clash = true; break; }
                    if (clash) continue;
                    mine.Add(r);
                    PlaceProp(c, p, b, pos, yaw);
                    c.Street++;
                }
            }
        }

        // ── машины у бордюра (на краю дороги) ──

        private void Cars(Ctx c, Rect2 lot, Vector2 ms)
        {
            List<GameObject> cars = Valid(c.L.Cars);
            if (cars.Count == 0 || c.L.CarChance <= 0f || RoadMargin <= 0f) return;
            float road = RoadMargin * c.S;
            for (int side = 0; side < 4; side++)
            {
                bool sx = side % 2 == 0;
                float len = sx ? lot.W : lot.D;
                int slots = Mathf.Max(1, Mathf.FloorToInt(len / (0.05f * c.S)));
                List<Rect2> mine = new List<Rect2>();
                for (int k = 0; k < slots; k++)
                {
                    if (c.Rnd() > c.L.CarChance * 0.5f) continue;
                    GameObject p = cars[c.R.Next(cars.Count)];
                    Bounds b;
                    if (!TileGridGenerator.MeasurePrefab(p, out b)) continue;
                    bool longX = b.size.x >= b.size.z;
                    float along = (k + 0.5f) / slots * len;
                    float off = road * 0.28f;
                    Vector2 pos; float yaw;
                    float flip = c.Rnd() < 0.5f ? 0f : 180f;
                    switch (side)
                    {
                        case 0: pos = new Vector2(lot.X0 + along, lot.Z0 - off); yaw = (longX ? 0f : 90f) + flip; break;
                        case 1: pos = new Vector2(lot.X1 + off, lot.Z0 + along); yaw = (longX ? 90f : 0f) + flip; break;
                        case 2: pos = new Vector2(lot.X1 - along, lot.Z1 + off); yaw = (longX ? 0f : 90f) + flip; break;
                        default: pos = new Vector2(lot.X0 - off, lot.Z1 - along); yaw = (longX ? 90f : 0f) + flip; break;
                    }
                    Vector2 fp = Foot(new Info { B = b }, yaw, 1f);
                    Rect2 r = new Rect2(pos.x - fp.x * 0.5f, pos.y - fp.y * 0.5f, pos.x + fp.x * 0.5f, pos.y + fp.y * 0.5f);
                    bool clash = false;
                    for (int q = 0; q < mine.Count; q++) if (r.Overlaps(mine[q], 0.6f)) { clash = true; break; }
                    if (clash) continue;
                    mine.Add(r);
                    PlaceProp(c, p, b, pos, yaw + c.Range(-3f, 3f));
                    c.Cars++;
                }
            }
        }

        private void PlaceProp(Ctx c, GameObject p, Bounds b, Vector2 pos, float yaw)
        {
            GameObject pivot = new GameObject("S_" + p.name);
            pivot.transform.SetParent(c.Parent, false);
            pivot.transform.localPosition = new Vector3(pos.x, 0f, pos.y);
            pivot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            GameObject go = Spawn(p, pivot.transform);
            go.transform.localRotation = p.transform.localRotation;
            go.transform.localScale = p.transform.localScale;
            go.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z);
        }

        private static List<GameObject> Valid(List<GameObject> src)
        {
            List<GameObject> r = new List<GameObject>();
            if (src == null) return r;
            for (int i = 0; i < src.Count; i++) if (src[i] != null) r.Add(src[i]);
            return r;
        }

        private static void SetStatic(GameObject go)
        {
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.isStatic = true;
        }

        // ───────────────────────── ГИЗМО ─────────────────────────

        public static Color RoleColor(CityRole r)
        {
            switch (r)
            {
                case CityRole.Level: return new Color(0.95f, 0.30f, 0.25f);
                case CityRole.Ring1: return new Color(1f, 0.70f, 0.20f);
                case CityRole.Ring2: return new Color(0.35f, 0.65f, 1f);
                default: return new Color(0.35f, 0.35f, 0.35f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            EnsureRoles();
            Vector2 ms = ModuleSize();
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                {
                    Vector3 c; Quaternion r;
                    ModuleFrame(x, z, out c, out r);
                    Gizmos.matrix = Matrix4x4.TRS(c, r, Vector3.one);
                    Color col = RoleColor(GetRole(x, z));
                    Gizmos.color = new Color(col.r, col.g, col.b, 0.9f);
                    Gizmos.DrawWireCube(Vector3.up * 0.5f, new Vector3(ms.x * (1f - 2f * RoadMargin), 1f, ms.y * (1f - 2f * RoadMargin)));
                    Gizmos.color = new Color(col.r, col.g, col.b, 0.18f);
                    Gizmos.DrawCube(Vector3.up * 0.2f, new Vector3(ms.x * (1f - 2f * RoadMargin), 0.4f, ms.y * (1f - 2f * RoadMargin)));
                }
            Gizmos.matrix = Matrix4x4.identity;
        }

        private void Reset()
        {
            Grid = GetComponent<TileGridGenerator>();
            if (Grid == null) Grid = FindFirstObjectByType<TileGridGenerator>();
        }
    }
}
