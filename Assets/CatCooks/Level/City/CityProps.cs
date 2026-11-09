using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CatCooks.Level
{
    /// <summary>Что это за пропс и куда его ставить.</summary>
    public enum PropKind
    {
        None = 0,
        RoofSmall = 1,      // кондиционер, грибок вентиляции, ведро, тарелка, ТВ-антенна, флюгер
        RoofDuct = 2,       // короб вентиляции — вдоль края крыши
        RoofBig = 3,        // водонапорная башня, солнечная панель, грядка, большой короб
        RoofMast = 4,       // мачта
        RoofLandmark = 5,   // большой щит на крыше, вышка — один-два на модуль, на самых высоких
        RoofHelipad = 6,    // вертолётная площадка — на большую крышу небоскрёба
        RoofLounge = 7,     // шезлонг, зонт — набором
        WallSign = 10,      // вывеска на кронштейне (поперёк стены)
        WallFlat = 11,      // плоская вывеска над первым этажом
        WallBanner = 12,    // высокий баннер на стене
        WallGondola = 13,   // люлька мойщиков с крыши
        StreetLamp = 20,    // фонарь — с шагом вдоль бордюра
        StreetTree = 21,    // дерево — между фонарями
        TrafficLight = 22,  // светофор — на углах
        StreetSign = 23,    // дорожный знак — у перекрёстка
        StreetSmall = 24,   // гидрант, урна, почтовый ящик, паркомат
        Bollard = 25,       // столбики — группами у углов
        StreetBig = 26,     // остановка, электрошкаф
        StreetBillboard = 27, // щит на столбе
        Vendor = 28,        // тележка с хот-догами — на тротуаре
        Car = 30,           // машина — стоянка у бордюра и движение
        FoodTruck = 31,     // фудтрак — на стоянке
        Cone = 32           // конусы — кучкой на дороге
    }

    public enum PropAxis { Height = 0, Length = 1 }
    /// <summary>Какая ось модели — «перёд» (у машины — куда едет; у вывески/щита/остановки — лицом к улице).</summary>
    public enum PropFront { PlusZ = 0, PlusX = 1, MinusZ = 2, MinusX = 3 }

    [System.Serializable]
    public sealed class PropEntry
    {
        public GameObject Prefab;
        public PropKind Kind = PropKind.None;
        [Tooltip("Целевой размер в метрах (по оси Axis). 0 — как в модели")]
        [Min(0f)] public float Size = 0f;
        public PropAxis Axis = PropAxis.Height;
        public PropFront Front = PropFront.PlusZ;
        [Min(0f)] public float Weight = 1f;
        [Tooltip("Тип/размер/перёд выставлены руками — «Определить типы» их не трогает")]
        public bool Manual = false;
        [Tooltip("Можно лепить на стены (кондиционеры, трубы, ящики-огородики)")]
        public bool OnWalls = false;
        [Tooltip("Размер на стене, м (по длине). 0 — как Size")]
        [Min(0f)] public float WallSize = 0f;
    }

    /// <summary>
    /// ПРОПСЫ И МАШИНЫ — только ближний слой (модули «слой 1»). Средний и дальний не трогаем.
    /// Размер каждого пропса подгоняется сам (метры × «единиц в метре» города). Крыши: мелочь, короба вдоль края,
    /// одна крупная вещь, редкие щиты/вышки на самых высоких. Фасады к улице: вывески, баннеры, люльки.
    /// Тротуар: фонари с шагом, деревья между ними, светофоры и знаки на углах, мелочь, остановки.
    /// Дорога: машины на стоянке у бордюра, фудтраки, конусы и примитивное движение по кругу квартала (CityTraffic).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CatCooks/Город/Пропсы и машины (ближний слой)")]
    public sealed class CityProps : MonoBehaviour
    {
        public const string PropsName = "Props (generated)";
        public const string TrafficName = "Traffic";
        public const string ExtrasName = "Extras (generated)";

        public CityBlockGenerator City;
        public List<PropEntry> Props = new List<PropEntry>();
        [Tooltip("ПАПКА ПРОПСОВ: если указана — ставится ТОЛЬКО то, что в ней лежит. Перед каждой расстановкой список сверяется с папкой: " +
                 "новое добавляется, удалённое из папки — убирается из списка, ручные настройки (Manual) сохраняются")]
        public Object SourceFolder;
        [Tooltip("Брать и из подпапок")]
        public bool SourceSubfolders = true;
        [Tooltip("Расставлять заново вместе с застройкой («Застроить всё», перегенерация модуля)")]
        public bool AutoWithCity = true;
        public int Seed = 5;
        [Tooltip("Единиц сцены в метре. 0 — само: средний дом ближнего слоя ≈ 16 м")]
        [Min(0f)] public float UnitsPerMeter = 0f;
        [Tooltip("Материал на все пропсы крыш и фасадов (атлас props). Пусто — как в модели")]
        public Material RoofMaterial;
        [Tooltip("Материал на уличные пропсы и машины (атлас street props). Пусто — как в модели")]
        public Material StreetMaterial;

        [Header("Что ставить кодом (остальное — руками)")]
        public bool DoRoofs = true;
        public bool DoFacades = true;
        public bool DoWallDecor = true;
        [Tooltip("Уличное повторяемое: фонари, деревья, светофоры, знаки, урны, гидранты, почтовые ящики, столбики")]
        public bool DoStreetFurniture = true;
        [Tooltip("Уличное штучное: остановки, тележки с едой и кафе, электрошкафы, щиты на столбах, мелочь «прочее»")]
        public bool DoStreetUnique = true;
        [Tooltip("Машины на стоянке у бордюра, фудтраки, конусы, паркоматы")]
        public bool DoParking = true;
        [Tooltip("Машины в движении вокруг квартала")]
        public bool DoTraffic = true;

        [Header("Экономия: только то, что видно с уровня")]
        [Tooltip("Не ставить пропсы на сторонах модуля, которые с уровня не видно (задняя сторона). " +
                 "Модуль сбоку от уровня — 3 стороны (боковые частично), угловой — 2 передние")]
        public bool CullHidden = true;
        [Tooltip("Боковые стороны (вдоль улицы от уровня): какая доля длины со стороны уровня застраивается")]
        [Range(0.2f, 1f)] public float SideKeep = 0.65f;

        [Header("Крыши")]
        [Range(0f, 1f)] public float RoofChance = 0.9f;
        [Range(0, 12)] public int RoofSmallMax = 6;
        [Range(0f, 1f)] public float RoofDuctChance = 0.45f;
        [Range(0f, 1f)] public float RoofBigChance = 0.5f;
        [Range(0f, 1f)] public float RoofMastChance = 0.15f;
        [Range(0f, 1f)] public float LoungeChance = 0.12f;
        [Tooltip("Шанс щита/вышки на самом высоком доме модуля")]
        [Range(0f, 1f)] public float LandmarkChance = 0.6f;
        [Range(0f, 1f)] public float HelipadChance = 0.35f;

        [Header("Фасады (только стороны к улице)")]
        [Range(0f, 1f)] public float WallSignChance = 0.45f;
        [Range(0f, 1f)] public float FlatSignChance = 0.35f;
        [Range(0f, 1f)] public float BannerChance = 0.15f;
        [Range(0f, 1f)] public float GondolaChance = 0.2f;
        [Tooltip("Кондиционеры, трубы, ящики-огородики на стенах: штук на 150 м² открытой стены")]
        [Range(0f, 4f)] public float WallDecorDensity = 1f;
        [Tooltip("Щитов на крышах на модуль (на самых высоких, лицом к игроку)")]
        [Range(0, 4)] public int Billboards = 2;

        [Header("Модули рядом с уровнем — больше приколюх")]
        [Tooltip("Во сколько раз больше вывесок, украшений стен, тележек и кафе в модулях, которые граничат с уровнем")]
        [Range(1f, 4f)] public float NearBoost = 2f;
        [Tooltip("Шанс столиков с зонтом у тележки с едой")]
        [Range(0f, 1f)] public float CafeChance = 0.6f;

        [Header("Тротуар")]
        [Tooltip("Шаг фонарей, м")]
        [Min(5f)] public float LampSpacing = 24f;
        [Range(0f, 1f)] public float TreeChance = 0.6f;
        [Range(0f, 1f)] public float TrafficLightChance = 0.8f;
        [Range(0f, 1f)] public float StreetSignChance = 0.5f;
        [Tooltip("Мелочи (гидранты, урны, ящики, паркоматы) на 100 м тротуара")]
        [Range(0f, 30f)] public float SmallPer100m = 6f;
        [Range(0f, 1f)] public float BollardChance = 0.4f;
        [Range(0f, 1f)] public float BusStopChance = 0.25f;
        [Range(0f, 1f)] public float ElectroboxChance = 0.3f;
        [Range(0f, 1f)] public float VendorChance = 0.35f;
        [Range(0f, 1f)] public float StreetBillboardChance = 0.3f;

        [Header("Дорога")]
        [Tooltip("Доля занятых мест на стоянке у бордюра")]
        [Range(0f, 1f)] public float ParkChance = 0.5f;
        [Range(0f, 1f)] public float FoodTruckChance = 0.12f;
        [Range(0f, 1f)] public float ConeChance = 0.25f;
        [Tooltip("Машин в движении вокруг каждого квартала (мин, макс)")]
        public Vector2Int MovingCars = new Vector2Int(2, 4);
        [Tooltip("Скорость, м/с (мин, макс)")]
        public Vector2 Speed = new Vector2(5f, 9f);

        [Header("Добавить к уже стоящему (ничего не убирая)")]
        [Tooltip("Вокруг чего добавлять (пусто — центр уровня)")]
        public Transform ExtrasCenter;
        [Tooltip("Радиус вокруг центра, м")]
        public float ExtrasRadius = 180f;
        [Tooltip("Солнечные панели (пусто — из списка: sunbat/solar)")]
        public List<GameObject> ExtraPanels = new List<GameObject>();
        [Tooltip("Доля крыш, на которые ставятся солнечные панели")]
        [Range(0f, 1f)] public float PanelRoofChance = 0.35f;
        [Tooltip("Панелей в ряду на крыше (от и до)")]
        public Vector2Int PanelsPerRoof = new Vector2Int(2, 6);
        [Tooltip("Вывески (пусто — из списка: little billboard / вывески на стену)")]
        public List<GameObject> ExtraSigns = new List<GameObject>();
        [Tooltip("Доля фасадов к улице, на которые вешаются вывески")]
        [Range(0f, 1f)] public float SignFacadeChance = 0.4f;
        [Tooltip("Вывесок на фасад (от и до)")]
        public Vector2Int SignsPerFacade = new Vector2Int(1, 2);
        [Tooltip("Вывески только на стороны к улице")]
        public bool SignsStreetOnly = true;
        [Tooltip("Вывески не вешать поверх уже стоящего (выкл — вешаются и поверх, потом поправишь руками)")]
        public bool SignsAvoidExisting = false;
        public int ExtrasSeed = 11;

        [HideInInspector] public int LastProps, LastCars, LastModules, LastCulled;

        // ───────────────────────── каталог по именам ─────────────────────────

        /// <summary>Тип, размер (м), ось и перёд по имени модели. Подобрано под папки props / street props.</summary>
        public static void Guess(PropEntry e)
        {
            if (e == null || e.Prefab == null || e.Manual) return;
            string n = e.Prefab.name.ToLowerInvariant().Replace('_', ' ');          // little_billboard1 = little billboard1
            PropKind k = PropKind.None; float size = 0f; PropAxis ax = PropAxis.Height; PropFront fr = PropFront.PlusZ;
            bool walls = false; float wsize = 0f;
            if (n.Contains("ac block")) { k = PropKind.RoofBig; size = 3f; }
            else if (n == "ac" || n.StartsWith("ac ") || n.StartsWith("ac.") || n.StartsWith("ac_")) { k = PropKind.RoofSmall; size = 1.1f; walls = true; }
            else if (n.Contains("vent head")) { k = PropKind.RoofSmall; size = 1.3f; }
            else if (n.Contains("vent")) { k = PropKind.RoofDuct; size = n.Contains("2") ? 9f : 6f; ax = PropAxis.Length; walls = true; wsize = n.Contains("2") ? 5f : 4f; }
            else if (n.Contains("bucket")) { k = PropKind.RoofSmall; size = 0.9f; }
            else if (n.Contains("antenna1")) { k = PropKind.RoofMast; size = 22f; }
            else if (n.Contains("antenna2")) { k = PropKind.RoofLandmark; size = 18f; }
            else if (n.Contains("antenna3")) { k = PropKind.RoofMast; size = 11f; }
            else if (n.Contains("antenna")) { k = PropKind.RoofSmall; size = 2.2f; }
            else if (n.Contains("little billboard5")) { k = PropKind.WallFlat; size = 6f; ax = PropAxis.Length; }
            else if (n.Contains("little billboard1")) { k = PropKind.WallSign; size = 3f; ax = PropAxis.Length; fr = PropFront.PlusX; }
            else if (n.Contains("little billboard")) { k = PropKind.WallSign; size = 4.5f; fr = PropFront.PlusX; }
            else if (n.Contains("roadbillboard")) { k = PropKind.StreetBillboard; size = 8f; }
            else if (n.Contains("billboard1")) { k = PropKind.WallBanner; size = 12f; }
            else if (n.Contains("billboard")) { k = PropKind.RoofLandmark; size = 15f; ax = PropAxis.Length; }
            else if (n.Contains("chair") || n.Contains("umbrella")) { k = PropKind.RoofLounge; size = n.Contains("umbrella") ? 2.4f : 1f; }
            else if (n.Contains("garden")) { k = PropKind.RoofBig; size = 5f; ax = PropAxis.Length; walls = true; wsize = 2.4f; }
            else if (n.Contains("helicopter")) { k = PropKind.RoofHelipad; size = 18f; ax = PropAxis.Length; }
            else if (n.Contains("sunbat") || n.Contains("solar")) { k = PropKind.RoofBig; size = 2.6f; }
            else if (n.Contains("water")) { k = PropKind.RoofBig; size = 8f; }
            else if (n.Contains("window cleaning")) { k = PropKind.WallGondola; size = 7f; ax = PropAxis.Length; }
            else if (n.Contains("ladder") || n.Contains("platform")) { k = PropKind.None; }
            else if (n.Contains("bus stop")) { k = PropKind.StreetBig; size = 2.8f; }
            else if (n.Contains("electro")) { k = PropKind.StreetBig; size = 1.8f; }
            else if (n.Contains("hotdog") || n.Contains("kiosk") || n.Contains("cart")) { k = PropKind.Vendor; size = 2.6f; ax = PropAxis.Length; fr = PropFront.MinusX; }
            else if (n.Contains("taco") || n.Contains("food")) { k = PropKind.FoodTruck; size = 6f; ax = PropAxis.Length; fr = PropFront.MinusX; }
            else if (n.Contains("van") || n.Contains("minibus")) { k = PropKind.Car; size = 5.2f; ax = PropAxis.Length; fr = PropFront.MinusX; }
            else if (n.Contains("bus")) { k = PropKind.Car; size = 10f; ax = PropAxis.Length; fr = PropFront.MinusX; }
            else if (n.Contains("truck")) { k = PropKind.Car; size = 7f; ax = PropAxis.Length; fr = PropFront.MinusX; }
            else if (n.Contains("taxi") || n.Contains("jeep") || n.Contains("pickup") || n.Contains("police") || n.Contains("ambul")) { k = PropKind.Car; size = 4.8f; ax = PropAxis.Length; fr = PropFront.MinusX; }
            else if (n.Contains("car")) { k = PropKind.Car; size = 4.6f; ax = PropAxis.Length; fr = PropFront.MinusX; }
            else if (n.Contains("hydrant")) { k = PropKind.StreetSmall; size = 0.9f; }
            else if (n.Contains("lamp")) { k = PropKind.StreetLamp; size = 7.5f; fr = PropFront.PlusX; }
            else if (n.Contains("streetlight") || n.Contains("street light") || n.Contains("street_light")) { k = PropKind.StreetLamp; size = 7.5f; fr = PropFront.PlusX; }
            else if (n.Contains("pole") || n.Contains("column")) { k = PropKind.StreetLamp; size = 8f; }          // столбы — с шагом вдоль бордюра, как фонари
            else if (n.Contains("bench")) { k = PropKind.StreetSmall; size = 1.8f; ax = PropAxis.Length; }
            else if (n.Contains("bush") || n.Contains("shrub")) { k = PropKind.StreetSmall; size = 1.2f; }
            else if (n.Contains("palm")) { k = PropKind.StreetTree; size = 8f; }
            else if (n.Contains("planter") || n.Contains("flower") || n.Contains("pot")) { k = PropKind.StreetSmall; size = 1f; }
            else if (n.Contains("mailbox") || n.Contains("mail box")) { k = PropKind.StreetSmall; size = 1.3f; }
            else if (n.Contains("phone") || n.Contains("booth")) { k = PropKind.StreetBig; size = 2.4f; }
            else if (n.Contains("bike") || n.Contains("bicycle")) { k = PropKind.StreetSmall; size = 1.7f; ax = PropAxis.Length; }
            else if (n.Contains("parking")) { k = PropKind.StreetSmall; size = 1.4f; }
            else if (n.Contains("pillar")) { k = PropKind.Bollard; size = 1f; }
            else if (n.Contains("post")) { k = PropKind.StreetSmall; size = 1.3f; }
            else if (n.Contains("trash")) { k = PropKind.StreetSmall; size = 1.1f; }
            else if (n.Contains("conus") || n.Contains("cone")) { k = PropKind.Cone; size = 0.75f; }
            else if (n.Contains("sign")) { k = PropKind.StreetSign; size = 3f; }
            else if (n.Contains("trafic") || n.Contains("traffic")) { k = PropKind.TrafficLight; size = 5.5f; }
            else if (n.Contains("tree")) { k = PropKind.StreetTree; size = 6.5f; }
            e.Kind = k; e.Size = size; e.Axis = ax; e.Front = fr; e.OnWalls = walls; e.WallSize = wsize;
        }

        public static bool IsRoof(PropKind k) { return k >= PropKind.RoofSmall && k <= PropKind.RoofLounge || k >= PropKind.WallSign && k <= PropKind.WallGondola; }

        // ───────────────────────── общее ─────────────────────────

        private struct R4
        {
            public float X0, Z0, X1, Z1;
            public R4(float x0, float z0, float x1, float z1) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; }
            public bool Overlaps(R4 o, float pad) { return X0 < o.X1 + pad && X1 > o.X0 - pad && Z0 < o.Z1 + pad && Z1 > o.Z0 - pad; }
            public float W { get { return X1 - X0; } }
            public float D { get { return Z1 - Z0; } }
        }

        private sealed class House
        {
            public GameObject Go;
            public R4 Rect;
            public float Height;
        }

        private sealed class Ctx
        {
            public System.Random R;
            public Transform Module, Props;
            public float M;                     // единиц в метре
            public float S, H, Sw;              // модуль, полдороги, тротуар
            public R4 Lot;
            public List<House> Houses = new List<House>();
            public List<R4> Busy = new List<R4>();
            public Vector2 Player;
            public Vector2 PlayerPos;            // центр уровня в осях модуля
            public bool Near;                    // модуль граничит с уровнем
            public int Count;
            public int Culled;                   // сторон пропущено (не видно с уровня)
            public float Rnd() { return (float)R.NextDouble(); }
            public float Range(float a, float b) { return a + (b - a) * (float)R.NextDouble(); }
        }

        private readonly Dictionary<GameObject, Bounds> _bounds = new Dictionary<GameObject, Bounds>();

        private bool B(GameObject p, out Bounds b)
        {
            if (_bounds.TryGetValue(p, out b)) return true;
            if (!TileGridGenerator.MeasurePrefab(p, out b)) return false;
            _bounds[p] = b;
            return true;
        }

        /// <summary>Множитель размера: Size (м) × единиц в метре / габарит модели.</summary>
        private float Scale(PropEntry e, float m)
        {
            Bounds b;
            if (e.Size <= 0f || !B(e.Prefab, out b)) return 1f;
            float have = e.Axis == PropAxis.Height ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            return have > 1e-4f ? e.Size * m / have : 1f;
        }

        public float Meter()
        {
            if (UnitsPerMeter > 0f) return UnitsPerMeter;
            CityBlockGenerator g = City != null ? City : GetComponent<CityBlockGenerator>();
            if (g == null) return 3.5f;
            float s = Mathf.Min(g.ModuleSize().x, g.ModuleSize().y);
            List<float> hs = new List<float>();
            foreach (BuildingEntry e in g.Layer1.Buildings)
            {
                Bounds b;
                if (e == null || e.Prefab == null || !TileGridGenerator.MeasurePrefab(e.Prefab, out b)) continue;
                BuildingKind k = e.Kind != BuildingKind.Auto ? e.Kind : CityBlockGenerator.AutoKind(b, s);
                if (k == BuildingKind.Mid || k == BuildingKind.Large) hs.Add(b.size.y);
            }
            if (hs.Count == 0) return 3.5f;
            hs.Sort();
            return hs[hs.Count / 2] / 16f;
        }

        private static float FrontOffset(PropFront f)
        {
            switch (f) { case PropFront.PlusX: return -90f; case PropFront.MinusZ: return 180f; case PropFront.MinusX: return 90f; default: return 0f; }
        }

        /// <summary>Поворот, при котором «перёд» модели смотрит по d (оси модуля).</summary>
        public static float YawTo(PropEntry e, Vector2 d) { return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + FrontOffset(e.Front); }

        private List<PropEntry> Of(PropKind k)
        {
            List<PropEntry> r = new List<PropEntry>();
            foreach (PropEntry e in Props) if (e != null && e.Prefab != null && e.Kind == k && e.Weight > 0f) r.Add(e);
            return r;
        }

        private static PropEntry Pick(Ctx c, List<PropEntry> list)
        {
            if (list == null || list.Count == 0) return null;
            float t = 0f;
            foreach (PropEntry e in list) t += e.Weight;
            float p = c.Rnd() * t;
            foreach (PropEntry e in list) { p -= e.Weight; if (p <= 0f) return e; }
            return list[list.Count - 1];
        }

        /// <summary>Поставить пропс: опора в точке (оси модуля, y — высота), поворот, масштаб; модель — дном в опору.</summary>
        private GameObject Put(Ctx c, Transform parent, PropEntry e, Vector3 pos, float yaw, float k)
        {
            Bounds b;
            if (!B(e.Prefab, out b)) return null;
            GameObject pivot = new GameObject("P_" + e.Prefab.name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = pos;
            pivot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            pivot.transform.localScale = Vector3.one * k;
            GameObject go = CityBlockGenerator.Spawn(e.Prefab, pivot.transform);
            go.transform.localRotation = e.Prefab.transform.localRotation;
            go.transform.localScale = e.Prefab.transform.localScale;
            go.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z);
            Material mat = e.Kind >= PropKind.StreetLamp ? StreetMaterial : RoofMaterial;
            if (mat != null)
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) ms[i] = mat;
                    r.sharedMaterials = ms;
                }
            c.Count++;
            return pivot;
        }

        /// <summary>Габарит по X/Z в осях модуля (поворот округляется до 90°).</summary>
        private Vector2 Foot(PropEntry e, float yaw, float k)
        {
            Bounds b;
            if (!B(e.Prefab, out b)) return Vector2.zero;
            int q = ((Mathf.RoundToInt(yaw / 90f) % 4) + 4) % 4;
            return q % 2 == 0 ? new Vector2(b.size.x * k, b.size.z * k) : new Vector2(b.size.z * k, b.size.x * k);
        }

        private bool Free(Ctx c, Vector2 p, Vector2 fp, float pad)
        {
            R4 r = new R4(p.x - fp.x * 0.5f, p.y - fp.y * 0.5f, p.x + fp.x * 0.5f, p.y + fp.y * 0.5f);
            foreach (R4 o in c.Busy) if (r.Overlaps(o, pad)) return false;
            return true;
        }

        private void Take(Ctx c, Vector2 p, Vector2 fp)
        {
            c.Busy.Add(new R4(p.x - fp.x * 0.5f, p.y - fp.y * 0.5f, p.x + fp.x * 0.5f, p.y + fp.y * 0.5f));
        }

        private static int Hash(int seed, int x, int z)
        {
            unchecked { int h = seed * 73856093 ^ x * 19349663 ^ z * 83492791; h ^= h >> 13; h *= 1274126177; h ^= h >> 16; return h; }
        }

        // ───────────────────────── модули ─────────────────────────

        public void ClearAll()
        {
            CityBlockGenerator g = City != null ? City : GetComponent<CityBlockGenerator>();
            if (g == null || g.Container() == null) return;
            foreach (Transform m in g.Container()) ClearIn(m);
#if UNITY_EDITOR
            CityRoadClosure.PropsCleared(g);
#endif
        }

        private static void ClearIn(Transform m)
        {
            List<GameObject> kill = new List<GameObject>();
            foreach (Transform t in m) if (t.name == PropsName || t.name == TrafficName) kill.Add(t.gameObject);
            foreach (GameObject go in kill)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) { Undo.DestroyObjectImmediate(go); continue; }
#endif
                Destroy(go);
            }
        }

        /// <summary>Убрать пропсы и движение с одного модуля.</summary>
        public void ClearModule(int x, int z)
        {
            CityBlockGenerator g = City != null ? City : GetComponent<CityBlockGenerator>();
            if (g == null || g.Container() == null) return;
            Transform m = g.Container().Find(CityBlockGenerator.ModuleName(x, z));
            if (m != null) ClearIn(m);
        }

        /// <summary>Убрать пропсы со всех готовых модулей ближнего слоя. Возвращает число модулей.</summary>
        public int ClearLayer1()
        {
            CityBlockGenerator g = City != null ? City : GetComponent<CityBlockGenerator>();
            if (g == null || g.Container() == null) return 0;
            int n = 0;
            for (int z = 0; z < g.Depth; z++)
                for (int x = 0; x < g.Width; x++)
                {
                    if (g.GetRole(x, z) != CityRole.Ring1) continue;
                    Transform m = g.Container().Find(CityBlockGenerator.ModuleName(x, z));
                    if (m == null) continue;
                    ClearIn(m); n++;
                }
#if UNITY_EDITOR
            CityRoadClosure.PropsCleared(g);
#endif
            Debug.Log("[Застройка/пропсы] убраны пропсы с модулей ближнего слоя: " + n, this);
            return n;
        }

        /// <summary>Пресет «только улица»: кодом — фонари, деревья, светофоры, знаки, мелочь; остальное руками.</summary>
        public void StreetOnlyPreset()
        {
            DoRoofs = false; DoFacades = false; DoWallDecor = false; DoStreetUnique = false; DoParking = false;
            DoStreetFurniture = true; DoTraffic = true;
        }

#if UNITY_EDITOR
        [System.NonSerialized] private float _syncAt = -10f;
#endif
        /// <summary>Сверить список с папкой пропсов (если она указана): только то, что в папке. Возвращает, сколько добавлено и убрано.</summary>
        public void SyncFolder(bool force, out int added, out int removed, out List<string> unknown)
        {
            added = 0; removed = 0; unknown = new List<string>();
#if UNITY_EDITOR
            if (SourceFolder == null) return;
            if (!force && Time.realtimeSinceStartup - _syncAt < 3f) return;      // застройка зовёт по модулю — сверяем раз в несколько секунд
            _syncAt = Time.realtimeSinceStartup;
            string folder = AssetDatabase.GetAssetPath(SourceFolder);
            if (!AssetDatabase.IsValidFolder(folder)) return;
            HashSet<GameObject> inFolder = new HashSet<GameObject>();
            List<GameObject> order = new List<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!SourceSubfolders && System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != folder) continue;
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && inFolder.Add(go)) order.Add(go);
            }
            for (int i = Props.Count - 1; i >= 0; i--)
                if (Props[i] == null || Props[i].Prefab == null || !inFolder.Contains(Props[i].Prefab)) { Props.RemoveAt(i); removed++; }
            HashSet<GameObject> have = new HashSet<GameObject>();
            foreach (PropEntry e in Props) have.Add(e.Prefab);
            foreach (GameObject go in order)
            {
                if (have.Contains(go)) continue;
                PropEntry ne = new PropEntry { Prefab = go };
                Guess(ne);
                Props.Add(ne);
                added++;
            }
            foreach (PropEntry e in Props) if (e.Kind == PropKind.None) unknown.Add(e.Prefab.name);
            if (added > 0 || removed > 0)
            {
                EditorUtility.SetDirty(this);
                Debug.Log("[Застройка/пропсы] папка «" + folder + "»: добавлено " + added + ", убрано (нет в папке) " + removed + ", в списке " + Props.Count +
                          (unknown.Count > 0 ? ". Тип не определён (не ставятся, выбери Kind в списке и поставь Manual): " + string.Join(", ", unknown.ToArray()) : ""), this);
            }
#endif
        }

        private void SyncFolderQuiet()
        {
            int a, r; List<string> u;
            SyncFolder(false, out a, out r, out u);
        }

        public void GenerateAll()
        {
#if UNITY_EDITOR
            _syncAt = -10f;
#endif
            SyncFolderQuiet();
            CityBlockGenerator g = City != null ? City : GetComponent<CityBlockGenerator>();
            if (g == null) { Debug.LogWarning("[Застройка/пропсы] нет генератора застройки (City)", this); return; }
            City = g;
            int props = 0, cars = 0, mods = 0;
            LastProps = 0; LastCars = 0; LastCulled = 0;
            for (int z = 0; z < g.Depth; z++)
                for (int x = 0; x < g.Width; x++)
                {
                    Transform m = g.Container() != null ? g.Container().Find(CityBlockGenerator.ModuleName(x, z)) : null;
                    if (m == null) continue;
                    if (g.GetRole(x, z) != CityRole.Ring1) { ClearIn(m); continue; }
                    int cc;
                    props += Build(g, x, z, m, out cc);
                    cars += cc; mods++;
                }
            LastProps = props; LastCars = cars; LastModules = mods;
            Debug.Log($"[Застройка/пропсы] ближний слой: модулей {mods}, пропсов {props}, машин в движении {cars} · {Meter():0.##} ед./м" +
                      (CullHidden ? $" · задних сторон без пропсов: {LastCulled}" : ""), this);
#if UNITY_EDITOR
            CityRoadClosure.AfterProps(g);
#endif
        }

        public void GenerateModule(int x, int z)
        {
            SyncFolderQuiet();
            CityBlockGenerator g = City != null ? City : GetComponent<CityBlockGenerator>();
            if (g == null || g.Container() == null) return;
            City = g;
            Transform m = g.Container().Find(CityBlockGenerator.ModuleName(x, z));
            if (m == null) return;
            int cc;
            Build(g, x, z, m, out cc);
#if UNITY_EDITOR
            CityRoadClosure.AfterProps(g);
#endif
        }

        /// <summary>Контекст модуля: размеры, участок, дома (габарит в осях модуля), занятые места (метро).</summary>
        private Ctx MakeCtx(CityBlockGenerator g, int x, int z, Transform module, int seed)
        {
            Ctx c = new Ctx();
            c.R = new System.Random(seed);
            c.Module = module;
            c.M = Meter();
            Vector2 ms = g.ModuleSize();
            c.S = Mathf.Min(ms.x, ms.y);
            c.H = g.RoadMargin * c.S;
            c.Sw = g.Sidewalk * c.S;
            float hx = ms.x * (0.5f - g.RoadMargin), hz = ms.y * (0.5f - g.RoadMargin);
            c.Lot = new R4(-hx, -hz, hx, hz);
            c.Near = (x > 0 && g.GetRole(x - 1, z) == CityRole.Level) || (x < g.Width - 1 && g.GetRole(x + 1, z) == CityRole.Level) ||
                     (z > 0 && g.GetRole(x, z - 1) == CityRole.Level) || (z < g.Depth - 1 && g.GetRole(x, z + 1) == CityRole.Level);
            Vector3 lc;
            if (g.LevelCenter(out lc))
            {
                Vector3 lp = module.InverseTransformPoint(lc);
                Vector2 pd = new Vector2(lp.x, lp.z);
                if (pd.sqrMagnitude > 1e-4f) c.Player = pd.normalized;
                c.PlayerPos = pd;
            }
            // дома модуля: габарит в осях модуля
            foreach (Transform t in module)
            {
                if (!t.name.StartsWith("B_")) continue;
                Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                bool any = false; Vector3 mn = Vector3.zero, mx = Vector3.zero;
                foreach (Renderer r in rs)
                {
                    if (UnderProp(r.transform, t)) continue;
                    Bounds wb = r.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 p = new Vector3((k & 1) == 0 ? wb.min.x : wb.max.x, (k & 2) == 0 ? wb.min.y : wb.max.y, (k & 4) == 0 ? wb.min.z : wb.max.z);
                        p = module.InverseTransformPoint(p);
                        if (!any) { mn = p; mx = p; any = true; } else { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
                    }
                }
                if (!any) continue;
                House h = new House { Go = t.gameObject, Rect = new R4(mn.x, mn.z, mx.x, mx.z), Height = mx.y };
                c.Houses.Add(h);
                c.Busy.Add(h.Rect);
            }
            // входы в метро — пропсы их обходят (запас 1.5 м)
            foreach (CityMetroEntrance me in FindObjectsByType<CityMetroEntrance>(FindObjectsSortMode.None))
            {
                float mm = me.Meter();
                float hw = me.Width * mm * 0.5f + 1.5f * c.M, l0 = -1.5f * c.M, l1 = me.Length * mm + 1.5f * c.M;
                bool any = false; Vector3 mn = Vector3.zero, mx = Vector3.zero;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = me.transform.TransformPoint(new Vector3((k & 1) == 0 ? -hw : hw, 0f, (k & 2) == 0 ? l0 : l1));
                    p = module.InverseTransformPoint(p);
                    if (!any) { mn = p; mx = p; any = true; } else { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
                }
                R4 mr = new R4(mn.x, mn.z, mx.x, mx.z);
                if (mr.Overlaps(new R4(c.Lot.X0 - c.H, c.Lot.Z0 - c.H, c.Lot.X1 + c.H, c.Lot.Z1 + c.H), 0f)) c.Busy.Add(mr);
            }

            return c;
        }

        private int Build(CityBlockGenerator g, int x, int z, Transform module, out int moving)
        {
            moving = 0;
            ClearIn(module);
            if (Props.Count == 0) return 0;
            Ctx c = MakeCtx(g, x, z, module, Hash(Seed, x, z));

            GameObject root = new GameObject(PropsName);
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(root, "Пропсы");
#endif
            root.transform.SetParent(module, false);
            c.Props = root.transform;

            List<MeshCollider> temp = TempColliders(c);
            try
            {
                if (DoRoofs) Roofs(c);
                if (DoFacades) Facades(c);
                if (DoWallDecor) WallDecor(c);
            }
            finally
            {
                foreach (MeshCollider mc in temp) if (mc != null) DestroyImmediate(mc);
            }
            if (DoStreetFurniture || DoStreetUnique) Sidewalks(c);
            if (DoParking) Parking(c);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = g.MarkStatic;
            moving = DoTraffic ? Traffic(c) : 0;
            LastCulled += c.Culled;
            return c.Count;
        }

        // ───────────────────────── добавить к стоящему ─────────────────────────

        private List<PropEntry> ExtraList(List<GameObject> own, bool panels)
        {
            List<PropEntry> r = new List<PropEntry>();
            foreach (GameObject go in own)
            {
                if (go == null) continue;
                PropEntry found = null;
                foreach (PropEntry e in Props) if (e != null && e.Prefab == go) { found = e; break; }
                if (found == null) { found = new PropEntry { Prefab = go }; Guess(found); }
                r.Add(found);
            }
            if (r.Count > 0) return r;
            foreach (PropEntry e in Props)
            {
                if (e == null || e.Prefab == null) continue;
                string n = e.Prefab.name.ToLowerInvariant().Replace('_', ' ');
                if (panels ? (n.Contains("sunbat") || n.Contains("solar")) : (e.Kind == PropKind.WallSign || e.Kind == PropKind.WallFlat || n.Contains("little billboard"))) r.Add(e);
            }
            return r;
        }

        /// <summary>Добавить солнечные панели на крыши и вывески на фасады вокруг центра — к уже стоящему, ничего не убирая.
        /// Всё добавленное — в «Extras (generated)» модулей (перерасстановка пропсов его не трогает).</summary>
        public int AddExtras(out int panels, out int signs, out string why)
        {
            panels = 0; signs = 0; why = "";
            _dHouses = _dRoofChance = _dRoofTry = _dRoofNoHit = _dRoofSlope = _dRoofUneven = _dRoofClash = _dSmall = _dFacades = _dNotStreet = _dSignTry = _dSignClash = _dSignWall = 0;
            int modsNear = 0, modsSkippedRole = 0, housesFar = 0;
            CityBlockGenerator g = City != null ? City : GetComponent<CityBlockGenerator>();
            if (g == null || g.Container() == null) { why = "нет генератора застройки"; return 0; }
            City = g;
            List<PropEntry> pl = ExtraList(ExtraPanels, true), sl = ExtraList(ExtraSigns, false);
            for (int i = 0; i < sl.Count; i++)
            {
                PropEntry e = sl[i];
                if (e.Kind == PropKind.WallSign || e.Kind == PropKind.WallFlat) continue;
                PropEntry cp = new PropEntry { Prefab = e.Prefab };                // копия — список пропсов не меняем
                Guess(cp);
                if (cp.Kind != PropKind.WallSign && cp.Kind != PropKind.WallFlat) { cp.Kind = PropKind.WallSign; cp.Size = 3.5f; cp.Front = PropFront.PlusX; }
                sl[i] = cp;
            }
            if (pl.Count == 0 && sl.Count == 0) { why = "нет моделей: перетащи sunbatery и little_billboard1–5 в «Extra Panels / Extra Signs» или в список пропсов"; return 0; }
            Vector3 center;
            if (ExtrasCenter != null) center = ExtrasCenter.position;
            else if (!g.LevelCenter(out center)) center = transform.position;
            float M = Meter();
            float R = ExtrasRadius * M;
            Vector2 ms = g.ModuleSize();
            int mods = 0;
            for (int z = 0; z < g.Depth; z++)
                for (int x = 0; x < g.Width; x++)
                {
                    Transform module = g.Container().Find(CityBlockGenerator.ModuleName(x, z));
                    if (module == null) continue;
                    Vector3 d = module.position - center; d.y = 0f;
                    if (d.magnitude > R + Mathf.Max(ms.x, ms.y) * 0.75f) continue;
                    modsNear++;
                    if (g.GetRole(x, z) == CityRole.Empty) { modsSkippedRole++; continue; }
                    Ctx c = MakeCtx(g, x, z, module, Hash(ExtrasSeed, x, z));
                    // дома только в радиусе
                    int before = c.Houses.Count;
                    c.Houses.RemoveAll(h => { Vector3 hp = module.TransformPoint(new Vector3((h.Rect.X0 + h.Rect.X1) * 0.5f, 0f, (h.Rect.Z0 + h.Rect.Z1) * 0.5f)); hp -= center; hp.y = 0f; return hp.magnitude > R; });
                    housesFar += before - c.Houses.Count;
                    _dHouses += c.Houses.Count;
                    if (c.Houses.Count == 0) continue;
                    Transform root = module.Find(ExtrasName);
                    if (root == null)
                    {
                        GameObject go = new GameObject(ExtrasName);
#if UNITY_EDITOR
                        if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(go, "Пропсы: добавить");
#endif
                        go.transform.SetParent(module, false);
                        root = go.transform;
                    }
                    c.Props = root;
                    List<Vector3> existing = Existing(module);                    // уже стоящее (не ставить поверх)
                    List<MeshCollider> temp = TempColliders(c);
                    try
                    {
                        if (pl.Count > 0) panels += Panels(c, pl, existing);
                        if (sl.Count > 0) signs += Signs(c, sl, existing);
                    }
                    finally { foreach (MeshCollider mc in temp) if (mc != null) DestroyImmediate(mc); }
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = g.MarkStatic;
                    mods++;
                }
            why = "\n  модулей рядом " + modsNear + (modsSkippedRole > 0 ? " (пустых " + modsSkippedRole + ")" : "") + ", домов в радиусе " + _dHouses + (housesFar > 0 ? " (дальше радиуса " + housesFar + ")" : "") +
                  ", моделей: панелей " + pl.Count + ", вывесок " + sl.Count +
                  "\n  крыши: не выпало по частоте " + _dRoofChance + ", попыток " + _dRoofTry + ", луч не попал в крышу " + _dRoofNoHit + ", наклон " + _dRoofSlope + ", неровно " + _dRoofUneven + ", занято " + _dRoofClash + ", крыша мала " + _dSmall +
                  "\n  фасады: подходящих " + _dFacades + ", не к улице " + _dNotStreet + ", попыток " + _dSignTry + ", занято " + _dSignClash + ", не нашлась стена " + _dSignWall;
            if (_dHouses == 0) why += "\n  → домов («B_…» в модулях генератора) в радиусе нет: увеличь Extras Radius или перетащи в Extras Center объект в центре уровня";
            else if (_dRoofTry > 0 && _dRoofNoHit == _dRoofTry * 1) why += "\n  → лучи не попадают в крыши: у домов нет мешей с MeshFilter (или они не под «B_…»)";
            return mods;
        }

        /// <summary>Убрать всё, что добавлено «к стоящему».</summary>
        public int ClearExtras()
        {
            CityBlockGenerator g = City != null ? City : GetComponent<CityBlockGenerator>();
            if (g == null || g.Container() == null) return 0;
            int n = 0;
            foreach (Transform m in g.Container())
            {
                Transform t = m.Find(ExtrasName);
                if (t == null) continue;
#if UNITY_EDITOR
                if (!Application.isPlaying) { Undo.DestroyObjectImmediate(t.gameObject); n++; continue; }
#endif
                Destroy(t.gameObject); n++;
            }
            return n;
        }

        /// <summary>Пропсы, что уже стоят в модуле (центры «P_…» в осях модуля, w — радиус).</summary>
        private List<Vector3> Existing(Transform module)
        {
            List<Vector3> r = new List<Vector3>();
            _exR.Clear(); _exH.Clear();
            foreach (Transform t in module.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("P_") || !t.gameObject.activeInHierarchy) continue;
                // габарит — только по включённым рендерам модели (выключенные и свет пропса дают «нулевой» габарит в начале координат
                // и раздували «занято» на весь город)
                bool any = false; Bounds b = new Bounds();
                foreach (Renderer rr in t.GetComponentsInChildren<Renderer>(false))
                {
                    if (rr == null || !rr.enabled || rr.name.StartsWith(CityPropGlow.GlowName) || rr.bounds.size.sqrMagnitude < 1e-6f) continue;
                    if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds);
                }
                if (!any) continue;
                if (b.extents.magnitude > 40f * Meter()) continue;                  // что-то огромное — не пропс
                Vector3 lp = module.InverseTransformPoint(b.center);
                r.Add(lp);
                _exR.Add(Mathf.Max(b.extents.x, b.extents.z));
                _exH.Add(b.extents.y);
            }
            return r;
        }
        private readonly List<float> _exR = new List<float>(), _exH = new List<float>();
        // диагностика «почему не поставилось»
        private int _dHouses, _dRoofChance, _dRoofTry, _dRoofNoHit, _dRoofSlope, _dRoofUneven, _dRoofClash, _dSmall, _dFacades, _dNotStreet, _dSignTry, _dSignClash, _dSignWall;

        private bool Clashes(List<Vector3> existing, Vector3 p, float r, float h)
        {
            for (int i = 0; i < existing.Count; i++)
            {
                Vector3 e = existing[i];
                float er = i < _exR.Count ? _exR[i] : 0f, eh = i < _exH.Count ? _exH[i] : 0f;
                if (new Vector2(e.x - p.x, e.z - p.z).magnitude < er + r && Mathf.Abs(e.y - p.y) < eh + h) return true;
            }
            return false;
        }

        /// <summary>Солнечные панели рядком на крыше, все в одну сторону.</summary>
        private int Panels(Ctx c, List<PropEntry> pl, List<Vector3> existing)
        {
            int n = 0;
            foreach (House h in c.Houses)
            {
                if (c.Rnd() > PanelRoofChance) { _dRoofChance++; continue; }
                PropEntry e = Pick(c, pl);
                if (e == null) continue;
                float k = Scale(e, c.M);
                Bounds b;
                if (!B(e.Prefab, out b)) continue;
                float yaw = (h.Rect.W >= h.Rect.D ? 0f : 90f) + (c.Rnd() < 0.5f ? 180f : 0f);
                Vector2 fp = Foot(e, yaw, k);
                bool rowX = h.Rect.W >= h.Rect.D;                                  // ряд — вдоль длинной стороны
                float step = (rowX ? fp.x : fp.y) + 0.4f * c.M;
                int want = c.R.Next(Mathf.Min(PanelsPerRoof.x, PanelsPerRoof.y), Mathf.Max(PanelsPerRoof.x, PanelsPerRoof.y) + 1);
                float len = rowX ? h.Rect.W : h.Rect.D;
                want = Mathf.Min(want, Mathf.FloorToInt((len * 0.85f) / Mathf.Max(step, 1e-3f)));
                if (want <= 0) { _dSmall++; continue; }
                for (int tries = 0; tries < 6; tries++)
                {
                    _dRoofTry++;
                    float rowLen = want * step;
                    float sx = rowX ? c.Range(h.Rect.X0 + fp.x * 0.5f + 0.6f * c.M, Mathf.Max(h.Rect.X0 + fp.x * 0.5f + 0.6f * c.M, h.Rect.X1 - rowLen)) : c.Range(h.Rect.X0 + fp.x * 0.5f + 0.6f * c.M, h.Rect.X1 - fp.x * 0.5f - 0.6f * c.M);
                    float sz = rowX ? c.Range(h.Rect.Z0 + fp.y * 0.5f + 0.6f * c.M, h.Rect.Z1 - fp.y * 0.5f - 0.6f * c.M) : c.Range(h.Rect.Z0 + fp.y * 0.5f + 0.6f * c.M, Mathf.Max(h.Rect.Z0 + fp.y * 0.5f + 0.6f * c.M, h.Rect.Z1 - rowLen));
                    List<Vector3> spots = new List<Vector3>();
                    bool ok = true;
                    float y0 = float.NaN;
                    for (int i = 0; i < want && ok; i++)
                    {
                        float x = rowX ? sx + i * step : sx, z = rowX ? sz : sz + i * step;
                        Vector3 p, nrm;
                        if (!Hit(c, h, new Vector3(x, h.Height + 50f * c.M, z), Vector3.down, h.Height + 200f * c.M, out p, out nrm)) { _dRoofNoHit++; ok = false; break; }
                        if (nrm.y < 0.9f) { _dRoofSlope++; ok = false; break; }
                        if (!Level(c, h, p, fp) || (!float.IsNaN(y0) && Mathf.Abs(p.y - y0) > 0.3f * c.M)) { _dRoofUneven++; ok = false; break; }
                        y0 = p.y;
                        if (Clashes(existing, p + Vector3.up * b.extents.y * k, Mathf.Max(fp.x, fp.y) * 0.5f, b.extents.y * k)) { _dRoofClash++; ok = false; break; }
                        spots.Add(p);
                    }
                    if (!ok || spots.Count == 0) continue;
                    foreach (Vector3 p in spots)
                        if (Put(c, c.Props, e, p, yaw, k) != null) { n++; existing.Add(p + Vector3.up * b.extents.y * k); _exR.Add(Mathf.Max(fp.x, fp.y) * 0.5f); _exH.Add(b.extents.y * k); }
                    break;
                }
            }
            return n;
        }

        /// <summary>Вывески на фасады к улице: на кронштейне — выше, плоские — над первым этажом. Не поверх уже висящих.</summary>
        private int Signs(Ctx c, List<PropEntry> sl, List<Vector3> existing)
        {
            int n = 0;
            float edge = c.Sw + 0.06f * c.S;
            foreach (House h in c.Houses)
                for (int side = 0; side < 4; side++)
                {
                    Vector2 nn = side == 0 ? new Vector2(0, -1) : side == 1 ? new Vector2(1, 0) : side == 2 ? new Vector2(0, 1) : new Vector2(-1, 0);
                    float dist = side == 0 ? h.Rect.Z0 - c.Lot.Z0 : side == 1 ? c.Lot.X1 - h.Rect.X1 : side == 2 ? c.Lot.Z1 - h.Rect.Z1 : h.Rect.X0 - c.Lot.X0;
                    if (SignsStreetOnly && dist > edge) { _dNotStreet++; continue; }
                    _dFacades++;
                    if (c.Rnd() > SignFacadeChance) continue;
                    float a0 = side % 2 == 0 ? h.Rect.X0 : h.Rect.Z0, a1 = side % 2 == 0 ? h.Rect.X1 : h.Rect.Z1;
                    float width = a1 - a0;
                    if (width < 4f * c.M) continue;
                    int cnt = c.R.Next(Mathf.Min(SignsPerFacade.x, SignsPerFacade.y), Mathf.Max(SignsPerFacade.x, SignsPerFacade.y) + 1);
                    List<PropEntry> flat = new List<PropEntry>(), across = new List<PropEntry>();
                    foreach (PropEntry q in sl) if (q.Kind == PropKind.WallFlat) flat.Add(q); else across.Add(q);
                    for (int i = 0, tries = 0; i < cnt && tries < cnt * 4; tries++)
                    {
                        // по очереди: над навесом (плоская, если есть) и на углу (на кронштейне)
                        bool corner = (i + tries) % 2 == 1 ? true : c.Rnd() < 0.5f;
                        List<PropEntry> pool = corner ? (across.Count > 0 ? across : flat) : (flat.Count > 0 ? flat : across);
                        PropEntry e = Pick(c, pool);
                        if (e == null) break;
                        float along;
                        float[] ys;
                        if (corner)
                        {
                            float inset = Mathf.Min(width * 0.08f + 0.6f * c.M, width * 0.3f);
                            along = c.Rnd() < 0.5f ? a0 + inset : a1 - inset;
                            ys = new float[] { Mathf.Max(4.8f * c.M, h.Height * 0.32f), Mathf.Max(5.5f * c.M, h.Height * 0.45f), 4.8f * c.M, 6.5f * c.M };
                        }
                        else
                        {
                            along = a0 + width * c.Range(0.25f, 0.75f);
                            ys = new float[] { 4.3f * c.M, 5.1f * c.M, 5.9f * c.M, 6.8f * c.M };   // над навесом / козырьком первого этажа
                        }
                        float fx = side == 1 ? h.Rect.X1 : side == 3 ? h.Rect.X0 : along, fz = side == 0 ? h.Rect.Z0 : side == 2 ? h.Rect.Z1 : along;
                        _dSignTry++;
                        if (SignsAvoidExisting && Clashes(existing, new Vector3(fx, ys[0], fz), 2.5f * c.M, 2f * c.M)) { _dSignClash++; continue; }
                        bool placed = false;
                        foreach (float y in ys)
                        {
                            if (y > h.Height - 1.5f * c.M) continue;
                            int before = c.Props.childCount;
                            if (Wall(c, h, e, nn, along, y, width, false, 0f) && c.Props.childCount > before) { placed = true; break; }
                        }
                        if (!placed) { _dSignWall++; continue; }
                        Transform t = c.Props.GetChild(c.Props.childCount - 1);
                        existing.Add(c.Module.InverseTransformPoint(t.position)); _exR.Add(2f * c.M); _exH.Add(2f * c.M);
                        n++; i++;
                    }
                }
            return n;
        }

        private static bool UnderProp(Transform x, Transform stop)
        {
            for (Transform p = x; p != null && p != stop; p = p.parent) if (p.name.StartsWith("P_")) return true;
            return false;
        }

        private static List<MeshCollider> TempColliders(Ctx c)
        {
            List<MeshCollider> temp = new List<MeshCollider>();
            foreach (House h in c.Houses)
                foreach (MeshFilter mf in h.Go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                    MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    temp.Add(mc);
                }
            Physics.SyncTransforms();
            return temp;
        }

        private static bool Hit(Ctx c, House h, Vector3 fromLocal, Vector3 dirLocal, float dist, out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero; normal = Vector3.up;
            Vector3 from = c.Module.TransformPoint(fromLocal), dir = c.Module.TransformDirection(dirLocal).normalized;
            RaycastHit[] hits = Physics.RaycastAll(from, dir, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool ok = false;
            foreach (RaycastHit rh in hits)
            {
                if (rh.collider == null || !rh.collider.transform.IsChildOf(h.Go.transform)) continue;
                if (rh.distance < best) { best = rh.distance; point = c.Module.InverseTransformPoint(rh.point); normal = c.Module.InverseTransformDirection(rh.normal); ok = true; }
            }
            return ok;
        }

        // ───────────────────────── крыши ─────────────────────────

        private void Roofs(Ctx c)
        {
            List<PropEntry> small = Of(PropKind.RoofSmall), duct = Of(PropKind.RoofDuct), big = Of(PropKind.RoofBig), mast = Of(PropKind.RoofMast),
                            land = Of(PropKind.RoofLandmark), heli = Of(PropKind.RoofHelipad), lounge = Of(PropKind.RoofLounge);
            if (c.Houses.Count == 0) return;
            List<float> hs = new List<float>();
            foreach (House h in c.Houses) hs.Add(h.Height);
            hs.Sort();
            float median = hs[hs.Count / 2];
            // щиты — на самых высоких, и лучше на тех, что ближе к игроку (видно с уровня)
            List<House> rank = new List<House>(c.Houses);
            rank.Sort((p, q) => Score(c, q).CompareTo(Score(c, p)));
            int nb = Mathf.Min(rank.Count, c.Near ? Billboards + 1 : Billboards);
            List<House> boards = rank.GetRange(0, nb);

            foreach (House h in c.Houses)
            {
                if (c.Rnd() > RoofChance) continue;
                List<Vector3> used = new List<Vector3>(); List<float> usedR = new List<float>();
                bool tower = h.Height > median * 1.8f;
                if (boards.Contains(h) && c.Rnd() < LandmarkChance) Roof(c, h, Pick(c, land), used, usedR, 8, true);
                if (tower && c.Rnd() < HelipadChance) Roof(c, h, Pick(c, heli), used, usedR, 4, false);
                if (c.Rnd() < RoofBigChance) Roof(c, h, Pick(c, big), used, usedR, 6, false);
                if (c.Rnd() < RoofMastChance) Roof(c, h, Pick(c, mast), used, usedR, 4, false);
                if (c.Rnd() < RoofDuctChance) Roof(c, h, Pick(c, duct), used, usedR, 8, false);
                if (!tower && lounge.Count > 0 && c.Rnd() < LoungeChance)
                    for (int i = 0; i < Mathf.Min(3, lounge.Count + 1); i++) Roof(c, h, lounge[i % lounge.Count], used, usedR, 6, false);
                float area = h.Rect.W * h.Rect.D / (c.M * c.M);
                int n = Mathf.Clamp(Mathf.RoundToInt(area / 60f), 1, RoofSmallMax);
                n = c.R.Next(Mathf.Max(1, n / 2), n + 1);
                for (int i = 0; i < n; i++) Roof(c, h, Pick(c, small), used, usedR, 5, false);
            }
        }

        private static float Score(Ctx c, House h)
        {
            Vector2 ctr = new Vector2((h.Rect.X0 + h.Rect.X1) * 0.5f, (h.Rect.Z0 + h.Rect.Z1) * 0.5f);
            float toward = c.Player != Vector2.zero ? Vector2.Dot(ctr.normalized, c.Player) : 0f;
            return h.Height * (1f + 0.35f * toward);
        }

        /// <summary>Поворот «лицом к игроку» (точно, не по 90°) из точки p.</summary>
        private static float FaceYaw(Ctx c, PropEntry e, Vector2 p)
        {
            Vector2 d = c.PlayerPos - p;
            if (d.sqrMagnitude < 1e-3f) d = c.Player != Vector2.zero ? c.Player : new Vector2(0, 1);
            return YawTo(e, d.normalized);
        }

        /// <summary>Пропс на крышу: луч сверху, ровная площадка под всем пропсом, не налезать на соседние.</summary>
        private bool Roof(Ctx c, House h, PropEntry e, List<Vector3> used, List<float> usedR, int tries, bool facePlayer)
        {
            if (e == null) return false;
            float k = Scale(e, c.M);
            Bounds b;
            if (!B(e.Prefab, out b)) return false;
            for (int t = 0; t < tries; t++)
            {
                float yaw;
                if (facePlayer && c.Player != Vector2.zero) yaw = YawTo(e, Snap(c.Player));
                else if (e.Kind == PropKind.RoofDuct) yaw = (h.Rect.W >= h.Rect.D ? 0f : 90f) + (c.Rnd() < 0.5f ? 180f : 0f);
                else yaw = c.R.Next(4) * 90f;
                Vector2 fp = Foot(e, yaw, k);
                float fit = Mathf.Min(h.Rect.W * 0.9f / Mathf.Max(fp.x, 1e-3f), h.Rect.D * 0.9f / Mathf.Max(fp.y, 1e-3f));
                if (fit < 1f)
                {
                    if (fit < 0.75f) return false;             // большой щит на маленький дом — нет
                    k *= fit; fp *= fit;
                }
                float r = Mathf.Max(fp.x, fp.y) * 0.5f;
                float x, z;
                if (e.Kind == PropKind.RoofDuct)
                {
                    // вдоль длинного края, у парапета
                    bool alongX = h.Rect.W >= h.Rect.D;
                    float edge = c.Rnd() < 0.5f ? -1f : 1f;
                    x = alongX ? c.Range(h.Rect.X0 + fp.x * 0.5f, h.Rect.X1 - fp.x * 0.5f) : (edge < 0 ? h.Rect.X0 + fp.x * 0.5f + 0.8f * c.M : h.Rect.X1 - fp.x * 0.5f - 0.8f * c.M);
                    z = alongX ? (edge < 0 ? h.Rect.Z0 + fp.y * 0.5f + 0.8f * c.M : h.Rect.Z1 - fp.y * 0.5f - 0.8f * c.M) : c.Range(h.Rect.Z0 + fp.y * 0.5f, h.Rect.Z1 - fp.y * 0.5f);
                }
                else
                {
                    x = c.Range(h.Rect.X0 + fp.x * 0.5f, h.Rect.X1 - fp.x * 0.5f);
                    z = c.Range(h.Rect.Z0 + fp.y * 0.5f, h.Rect.Z1 - fp.y * 0.5f);
                }
                Vector3 p, nrm;
                if (!Hit(c, h, new Vector3(x, h.Height + 50f * c.M, z), Vector3.down, h.Height + 200f * c.M, out p, out nrm)) continue;
                if (nrm.y < 0.9f) continue;
                if (!Level(c, h, p, fp)) continue;
                bool clash = false;
                for (int q = 0; q < used.Count; q++) if (new Vector2(used[q].x - p.x, used[q].z - p.z).magnitude < usedR[q] + r) { clash = true; break; }
                if (clash) continue;
                if (facePlayer && c.Player != Vector2.zero) yaw = FaceYaw(c, e, new Vector2(p.x, p.z));
                else if (e.Kind == PropKind.RoofSmall) yaw += c.Range(-6f, 6f);
                if (Put(c, c.Props, e, p, yaw, k) == null) return false;
                used.Add(p); usedR.Add(r);
                return true;
            }
            return false;
        }

        /// <summary>Под углами пропса крыша на той же высоте (не свешиваемся с уступа, не стоим на парапете).</summary>
        private static bool Level(Ctx c, House h, Vector3 p, Vector2 fp)
        {
            for (int k = 0; k < 4; k++)
            {
                Vector3 o = new Vector3((k & 1) == 0 ? -fp.x * 0.5f : fp.x * 0.5f, 0f, (k & 2) == 0 ? -fp.y * 0.5f : fp.y * 0.5f);
                Vector3 q, n;
                if (!Hit(c, h, p + o + Vector3.up * 3f * c.M, Vector3.down, 10f * c.M, out q, out n)) return false;
                if (Mathf.Abs(q.y - p.y) > 0.6f * c.M) return false;
            }
            return true;
        }

        private static Vector2 Snap(Vector2 d)
        {
            return Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Sign(d.x), 0f) : new Vector2(0f, Mathf.Sign(d.y));
        }

        // ───────────────────────── фасады ─────────────────────────

        private void Facades(Ctx c)
        {
            List<PropEntry> sign = Of(PropKind.WallSign), flat = Of(PropKind.WallFlat), banner = Of(PropKind.WallBanner), gond = Of(PropKind.WallGondola);
            float edge = c.Sw + 0.06f * c.S;
            foreach (House h in c.Houses)
            {
                for (int side = 0; side < 4; side++)
                {
                    Vector2 n = side == 0 ? new Vector2(0, -1) : side == 1 ? new Vector2(1, 0) : side == 2 ? new Vector2(0, 1) : new Vector2(-1, 0);
                    float dist = side == 0 ? h.Rect.Z0 - c.Lot.Z0 : side == 1 ? c.Lot.X1 - h.Rect.X1 : side == 2 ? c.Lot.Z1 - h.Rect.Z1 : h.Rect.X0 - c.Lot.X0;
                    if (dist > edge) continue;                                     // не к улице
                    if (!SideOn(c, side)) continue;                                // задняя сторона — с уровня не видно
                    float a0 = side % 2 == 0 ? h.Rect.X0 : h.Rect.Z0, a1 = side % 2 == 0 ? h.Rect.X1 : h.Rect.Z1;
                    float width = a1 - a0;
                    float bst = c.Near ? NearBoost : 1f;
                    if (c.Rnd() < Mathf.Min(1f, FlatSignChance * bst)) Wall(c, h, Pick(c, flat), n, a0 + width * c.Range(0.35f, 0.65f), 3.6f * c.M, width, false);
                    if (c.Rnd() < Mathf.Min(1f, WallSignChance * bst))
                    {
                        float at = c.Rnd() < 0.5f ? a0 + width * c.Range(0.1f, 0.25f) : a1 - width * c.Range(0.1f, 0.25f);
                        Wall(c, h, Pick(c, sign), n, at, Mathf.Max(4f * c.M, h.Height * c.Range(0.3f, 0.55f)), width, false);
                    }
                    if (h.Height > 30f * c.M && c.Rnd() < BannerChance) Wall(c, h, Pick(c, banner), n, a0 + width * c.Range(0.3f, 0.7f), h.Height * c.Range(0.45f, 0.65f), width, false);
                    if (h.Height > 28f * c.M && c.Rnd() < GondolaChance) Wall(c, h, Pick(c, gond), n, a0 + width * c.Range(0.3f, 0.7f), 0f, width, true);
                }
            }
        }

        /// <summary>На стену: луч с улицы к дому на высоте y; вывеска на кронштейне — поперёк стены, плоские — лицом к улице.</summary>
        private void Wall(Ctx c, House h, PropEntry e, Vector2 n, float along, float y, float facadeWidth, bool fromRoof)
        {
            Wall(c, h, e, n, along, y, facadeWidth, fromRoof, 0f);
        }

        private bool Wall(Ctx c, House h, PropEntry e, Vector2 n, float along, float y, float facadeWidth, bool fromRoof, float kOverride)
        {
            if (e == null) return false;
            float k = kOverride > 0f ? kOverride : Scale(e, c.M);
            Bounds b;
            if (!B(e.Prefab, out b)) return false;
            float yaw = YawTo(e, n);
            float sx = b.size.x * k, sy = b.size.y * k, sz = b.size.z * k;
            bool across = e.Kind == PropKind.WallSign;
            float flatW = across ? sz : (e.Front == PropFront.PlusX || e.Front == PropFront.MinusX ? sz : sx);
            if (!across && flatW > facadeWidth * 0.9f) { k *= facadeWidth * 0.9f / flatW; sx *= facadeWidth * 0.9f / flatW; sy *= facadeWidth * 0.9f / flatW; sz *= facadeWidth * 0.9f / flatW; }
            if (sy + 2.8f * c.M > h.Height) return false;
            if (fromRoof) y = h.Height - 0.3f * c.M - sy * 0.5f;
            y = Mathf.Clamp(y, sy * 0.5f + 2.5f * c.M, h.Height - sy * 0.5f - 0.3f * c.M);
            Vector3 probe = new Vector3(n.x, 0f, n.y);
            Vector3 start = (n.x != 0f ? new Vector3(n.x > 0 ? h.Rect.X1 : h.Rect.X0, y, along) : new Vector3(along, y, n.y > 0 ? h.Rect.Z1 : h.Rect.Z0)) + probe * (6f * c.M);
            Vector3 p, nrm;
            if (!Hit(c, h, start, -probe, 30f * c.M, out p, out nrm)) return false;
            if (Vector3.Dot(nrm, probe) < 0.7f) return false;                    // не стена (козырёк, угол)
            float depth = across ? (e.Front == PropFront.PlusZ || e.Front == PropFront.MinusZ ? sz : sx) : (e.Front == PropFront.PlusX || e.Front == PropFront.MinusX ? sx : sz);
            Vector3 pos = p + probe * (depth * 0.5f + 0.1f * c.M);
            pos.y = y - sy * 0.5f;
            return Put(c, c.Props, e, pos, yaw, k) != null;
        }

        /// <summary>Украшения стен: кондиционеры, трубы, ящики-огородики — на открытые стены (к улице и не впритык к соседу).</summary>
        private void WallDecor(Ctx c)
        {
            List<PropEntry> deco = new List<PropEntry>();
            foreach (PropEntry e in Props) if (e != null && e.Prefab != null && e.OnWalls && e.Weight > 0f) deco.Add(e);
            if (deco.Count == 0 || WallDecorDensity <= 0f) return;
            float boost = c.Near ? NearBoost : 1f;
            float floor = 3.2f * c.M;
            foreach (House h in c.Houses)
            {
                if (h.Height < 6f * c.M) continue;
                for (int side = 0; side < 4; side++)
                {
                    if (Covered(c, h, side)) continue;
                    Vector2 n = side == 0 ? new Vector2(0, -1) : side == 1 ? new Vector2(1, 0) : side == 2 ? new Vector2(0, 1) : new Vector2(-1, 0);
                    float a0 = side % 2 == 0 ? h.Rect.X0 : h.Rect.Z0, a1 = side % 2 == 0 ? h.Rect.X1 : h.Rect.Z1;
                    float width = a1 - a0;
                    if (width < 4f * c.M) continue;
                    float area = width * h.Height / (c.M * c.M);
                    int cnt = Mathf.Clamp(Mathf.RoundToInt(area / 150f * WallDecorDensity * boost * c.Range(0.6f, 1.4f)), 0, 14);
                    int floors = Mathf.Max(1, Mathf.FloorToInt(h.Height / floor) - 1);
                    List<Vector3> used = new List<Vector3>();
                    for (int i = 0, tries = 0; i < cnt && tries < cnt * 4; tries++)
                    {
                        PropEntry e = Pick(c, deco);
                        Bounds b;
                        if (e == null || !B(e.Prefab, out b)) break;
                        float k = e.WallSize > 0f ? e.WallSize * c.M / Mathf.Max(Mathf.Max(b.size.x, b.size.z), 1e-3f) : Scale(e, c.M);
                        float hwid = Mathf.Max(b.size.x, b.size.z) * k * 0.5f, hh = b.size.y * k * 0.5f;
                        if (hwid * 2f > width * 0.8f) continue;
                        float along = c.Range(a0 + hwid + 0.5f * c.M, a1 - hwid - 0.5f * c.M);
                        float y = (1 + c.R.Next(floors)) * floor + 0.4f * c.M + hh;
                        bool clash = false;
                        foreach (Vector3 u in used) if (Mathf.Abs(u.x - along) < u.z + hwid && Mathf.Abs(u.y - y) < floor * 0.8f) { clash = true; break; }
                        if (clash) continue;
                        if (Wall(c, h, e, n, along, y, width, false, k)) { used.Add(new Vector3(along, y, hwid)); i++; }
                    }
                }
            }
        }

        /// <summary>Сторона дома прижата к соседнему (стена не видна)?</summary>
        private static bool Covered(Ctx c, House h, int side)
        {
            float gap = 1.5f * c.M;
            foreach (House o in c.Houses)
            {
                if (o == h) continue;
                float ov;
                switch (side)
                {
                    case 0: if (o.Rect.Z1 < h.Rect.Z0 - gap || o.Rect.Z1 > h.Rect.Z0 + gap) continue; ov = Mathf.Min(h.Rect.X1, o.Rect.X1) - Mathf.Max(h.Rect.X0, o.Rect.X0); break;
                    case 1: if (o.Rect.X0 > h.Rect.X1 + gap || o.Rect.X0 < h.Rect.X1 - gap) continue; ov = Mathf.Min(h.Rect.Z1, o.Rect.Z1) - Mathf.Max(h.Rect.Z0, o.Rect.Z0); break;
                    case 2: if (o.Rect.Z0 > h.Rect.Z1 + gap || o.Rect.Z0 < h.Rect.Z1 - gap) continue; ov = Mathf.Min(h.Rect.X1, o.Rect.X1) - Mathf.Max(h.Rect.X0, o.Rect.X0); break;
                    default: if (o.Rect.X1 < h.Rect.X0 - gap || o.Rect.X1 > h.Rect.X0 + gap) continue; ov = Mathf.Min(h.Rect.Z1, o.Rect.Z1) - Mathf.Max(h.Rect.Z0, o.Rect.Z0); break;
                }
                float w = side % 2 == 0 ? h.Rect.W : h.Rect.D;
                if (ov > w * 0.5f && o.Height > h.Height * 0.5f) return true;
            }
            return false;
        }

        // ───────────────────────── тротуар ─────────────────────────

        private static void Side(Ctx c, int side, out Vector2 a, out Vector2 t, out Vector2 n, out float len)
        {
            R4 L = c.Lot;
            switch (side)
            {
                case 0: a = new Vector2(L.X0, L.Z0); t = new Vector2(1, 0); n = new Vector2(0, -1); len = L.W; break;
                case 1: a = new Vector2(L.X1, L.Z0); t = new Vector2(0, 1); n = new Vector2(1, 0); len = L.D; break;
                case 2: a = new Vector2(L.X1, L.Z1); t = new Vector2(-1, 0); n = new Vector2(0, 1); len = L.W; break;
                default: a = new Vector2(L.X0, L.Z1); t = new Vector2(0, -1); n = new Vector2(-1, 0); len = L.D; break;
            }
        }

        /// <summary>Поставить на тротуар/дорогу, если свободно. pos — оси модуля (x, z).</summary>
        private bool Ground(Ctx c, PropEntry e, Vector2 pos, float yaw, float k, float pad)
        {
            if (e == null) return false;
            bool pole = e.Kind == PropKind.StreetLamp || e.Kind == PropKind.TrafficLight || e.Kind == PropKind.StreetSign || e.Kind == PropKind.StreetBillboard || e.Kind == PropKind.Bollard;
            Vector2 fp = pole ? new Vector2(0.8f * c.M, 0.8f * c.M) : Foot(e, yaw, k);   // у столбов — только основание
            if (!Free(c, pos, fp, pad)) return false;
            if (Put(c, c.Props, e, new Vector3(pos.x, 0f, pos.y), yaw, k) == null) return false;
            Take(c, pos, fp);
            return true;
        }

        /// <summary>Подгруппа уличной мелочи по имени: гидрант, урна, почтовый ящик, паркомат, прочее.</summary>
        private static string Sub(PropEntry e)
        {
            string n = e.Prefab.name.ToLowerInvariant();
            if (n.Contains("hydrant")) return "hydrant";
            if (n.Contains("trash") || n.Contains("bin") && !n.Contains("post")) return "trash";
            if (n.Contains("post") || n.Contains("mail")) return "mail";
            if (n.Contains("parking") || n.Contains("meter")) return "meter";
            return "misc";
        }

        private List<PropEntry> SubOf(List<PropEntry> small, string sub)
        {
            List<PropEntry> r = new List<PropEntry>();
            foreach (PropEntry e in small) if (Sub(e) == sub) r.Add(e);
            return r;
        }

        // ── видимость сторон модуля с уровня ──

        /// <summary>Видимость стороны: dot(наружу, к уровню). < −0.3 — задняя (не видно), 0.5+ — передняя, между — боковая.</summary>
        private float SideVis(Ctx c, int side)
        {
            if (!CullHidden || c.Player == Vector2.zero) return 1f;
            Vector2 a, t, n; float len;
            Side(c, side, out a, out t, out n, out len);
            return Vector2.Dot(n, c.Player);
        }

        private bool SideOn(Ctx c, int side) { return SideVis(c, side) >= -0.3f; }

        /// <summary>Точка s на стороне видна с уровня? У боковой стороны — только часть длины со стороны уровня (SideKeep).</summary>
        private bool AlongOK(Ctx c, int side, float s, float len)
        {
            float v = SideVis(c, side);
            if (v < -0.3f) return false;
            if (v >= 0.5f) return true;
            Vector2 a, t, n; float l;
            Side(c, side, out a, out t, out n, out l);
            return Vector2.Dot(t, c.Player) >= 0f ? s >= len * (1f - SideKeep) : s <= len * SideKeep;
        }

        /// <summary>
        /// ТРОТУАР ПО ПРАВИЛАМ (не случайно): у бордюра — линия «уличной мебели», посередине — линия деревьев, у стен — шкафы.
        /// Фонари ровным шагом по центру стороны, деревья строго между фонарями (одна порода на сторону), урна у каждого
        /// второго фонаря, на углах — светофор, столбики и урна, у начала стороны — знак и почтовый ящик, у конца — гидрант,
        /// остановка по центру стороны, тележки с едой и кафе — в промежутках между фонарями. Паркоматы — у мест стоянки.
        /// </summary>
        private void Sidewalks(Ctx c)
        {
            if (c.Sw <= 0f) return;
            List<PropEntry> lamp = Of(PropKind.StreetLamp), tree = Of(PropKind.StreetTree), light = Of(PropKind.TrafficLight), sign = Of(PropKind.StreetSign),
                            small = Of(PropKind.StreetSmall), boll = Of(PropKind.Bollard), big = Of(PropKind.StreetBig), bill = Of(PropKind.StreetBillboard),
                            vend = Of(PropKind.Vendor), lounge = Of(PropKind.RoofLounge);
            List<PropEntry> trash = SubOf(small, "trash"), hydrant = SubOf(small, "hydrant"), mail = SubOf(small, "mail"), misc = SubOf(small, "misc");
            List<PropEntry> stops = new List<PropEntry>(), boxes = new List<PropEntry>();
            foreach (PropEntry e in big) { if (e.Prefab.name.ToLowerInvariant().Contains("bus")) stops.Add(e); else boxes.Add(e); }
            float pad = 0.3f * c.M;
            float cz = c.Sw * 1.6f;                      // угол — для перехода, только светофор и столбики
            float curb = c.Sw * 0.12f, mid = c.Sw * 0.42f;
            float step = Mathf.Max(5f, LampSpacing) * c.M;

            // ── углы ──
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 a, t, n, a2, t2, n2; float len, len2;
                Side(c, corner, out a, out t, out n, out len);
                Side(c, (corner + 1) % 4, out a2, out t2, out n2, out len2);
                Vector2 q = a2;                           // угол участка
                bool cornerOK = AlongOK(c, corner, len, len) || AlongOK(c, (corner + 1) % 4, 0f, len2);
                if (!cornerOK || !DoStreetFurniture) continue;
                if (c.Rnd() < TrafficLightChance)
                {
                    PropEntry e = Pick(c, light);
                    if (e != null) Ground(c, e, q - n * (c.Sw * 0.3f) - n2 * (c.Sw * 0.3f), YawTo(e, (n + n2).normalized), Scale(e, c.M), 0f);
                }
                if (c.Rnd() < BollardChance)
                {
                    PropEntry e = Pick(c, boll);
                    if (e != null)
                        for (int i = 1; i <= 2; i++)
                        {
                            Ground(c, e, q - n * curb - n2 * curb - t * (c.Sw * 0.3f + i * 1.3f * c.M), YawTo(e, n), Scale(e, c.M), 0f);
                            Ground(c, e, q - n * curb - n2 * curb + t2 * (c.Sw * 0.3f + i * 1.3f * c.M), YawTo(e, n2), Scale(e, c.M), 0f);
                        }
                }
                if (corner % 2 == 0)
                {
                    PropEntry e = Pick(c, trash);
                    if (e != null) Ground(c, e, q - n * (c.Sw * 0.75f) - n2 * (c.Sw * 0.75f), YawTo(e, (n + n2).normalized), Scale(e, c.M), pad);
                }
            }

            // ── стороны ──
            List<float>[] gaps = new List<float>[4];
            for (int side = 0; side < 4; side++)
            {
                Vector2 a, t, n; float len;
                Side(c, side, out a, out t, out n, out len);
                gaps[side] = new List<float>();
                if (!SideOn(c, side)) { c.Culled++; continue; }                      // задняя сторона — с уровня не видно
                int count = Mathf.Max(1, Mathf.FloorToInt((len - 2f * cz) / step) + 1);
                float start = (len - (count - 1) * step) * 0.5f;
                // остановка — по центру стороны (вместо ближайшего фонаря)
                int stopAt = -1;
                if (DoStreetUnique && stops.Count > 0 && c.Rnd() < BusStopChance && AlongOK(c, side, len * 0.5f, len))
                {
                    PropEntry e = Pick(c, stops);
                    float k = Scale(e, c.M);
                    Vector2 fp = Foot(e, YawTo(e, n), k);
                    float d = Mathf.Min(fp.x, fp.y);
                    if (d > c.Sw * 0.8f) { k *= c.Sw * 0.8f / d; d = c.Sw * 0.8f; }
                    stopAt = count / 2;
                    if (!Ground(c, e, a + t * (start + stopAt * step) - n * (c.Sw * 0.5f), YawTo(e, n), k, pad)) stopAt = -1;
                }
                // фонари и урны у каждого второго
                for (int i = 0; i < count; i++)
                {
                    if (i == stopAt) continue;
                    float s = start + i * step;
                    if (!DoStreetFurniture || !AlongOK(c, side, s, len)) continue;
                    PropEntry e = Pick(c, lamp);
                    if (e != null) Ground(c, e, a + t * s - n * curb, YawTo(e, n), Scale(e, c.M), 0f);
                    if (i % 2 == 0 && trash.Count > 0)
                    {
                        PropEntry b = Pick(c, trash);
                        Ground(c, b, a + t * (s + 1.4f * c.M) - n * curb, YawTo(b, n), Scale(b, c.M), 0f);
                    }
                }
                // деревья — строго между фонарями, одна порода на сторону; частота: все промежутки / через один / нет
                PropEntry kind = Pick(c, tree);
                int every = TreeChance >= 0.66f ? 1 : TreeChance >= 0.33f ? 2 : 0;
                for (int i = 0; i + 1 < count; i++)
                {
                    float s = start + (i + 0.5f) * step;
                    if (!AlongOK(c, side, s, len)) continue;
                    if (DoStreetFurniture && every > 0 && i % every == 0 && i != stopAt && i + 1 != stopAt) Tree(c, kind, a + t * s, n, mid);
                    else gaps[side].Add(s);
                }
                // знак и почтовый ящик — у начала стороны (где едущие подъезжают к перекрёстку), гидрант — у конца
                if (DoStreetFurniture && c.Rnd() < StreetSignChance && AlongOK(c, side, cz * 0.7f, len))
                {
                    PropEntry e = Pick(c, sign);
                    if (e != null) Ground(c, e, a + t * (cz * 0.7f) - n * curb, YawTo(e, t), Scale(e, c.M), 0f);
                }
                if (DoStreetFurniture && mail.Count > 0 && c.Rnd() < 0.7f && AlongOK(c, side, cz + 1.6f * c.M, len))
                {
                    PropEntry e = Pick(c, mail);
                    Ground(c, e, a + t * (cz + 1.6f * c.M) - n * curb, YawTo(e, n), Scale(e, c.M), pad);
                }
                if (DoStreetFurniture && hydrant.Count > 0 && AlongOK(c, side, len - cz - 1.6f * c.M, len))
                {
                    PropEntry e = Pick(c, hydrant);
                    Ground(c, e, a + t * (len - cz - 1.6f * c.M) - n * curb, YawTo(e, n), Scale(e, c.M), pad);
                }
                // электрошкаф — к стене, в предпоследнем промежутке
                if (DoStreetUnique && boxes.Count > 0 && c.Rnd() < ElectroboxChance && count > 2 && AlongOK(c, side, start + (count - 1.5f) * step, len))
                {
                    PropEntry e = Pick(c, boxes);
                    float k = Scale(e, c.M);
                    Vector2 fp = Foot(e, YawTo(e, n), k);
                    float d = Mathf.Min(Mathf.Min(fp.x, fp.y), c.Sw * 0.5f);
                    Ground(c, e, a + t * (start + (count - 1.5f) * step) - n * (c.Sw - d * 0.5f - 0.15f * c.M), YawTo(e, n), k, pad);
                }
                // прочая мелочь — одна на сторону, в свободный промежуток у бордюра
                if (DoStreetUnique && misc.Count > 0 && gaps[side].Count > 0 && c.Rnd() < Mathf.Clamp01(SmallPer100m / 10f))
                {
                    PropEntry e = Pick(c, misc);
                    Ground(c, e, a + t * gaps[side][c.R.Next(gaps[side].Count)] - n * curb, YawTo(e, n), Scale(e, c.M), pad);
                }
            }

            // ── тележки с едой и кафе — в свободных промежутках между фонарями ──
            int vendors = !DoStreetUnique ? 0 : (c.Rnd() < VendorChance ? 1 : 0) + (c.Near ? 1 + c.R.Next(Mathf.Max(1, Mathf.RoundToInt(NearBoost))) : 0);
            for (int v = 0; v < vendors; v++)
            {
                PropEntry e = Pick(c, vend);
                if (e == null) break;
                int side = c.R.Next(4);
                if (gaps[side].Count == 0) continue;
                int gi = c.R.Next(gaps[side].Count);
                float s = gaps[side][gi];
                gaps[side].RemoveAt(gi);
                Vector2 a, t, n; float len;
                Side(c, side, out a, out t, out n, out len);
                if (!Ground(c, e, a + t * s - n * mid, YawTo(e, t), Scale(e, c.M), pad)) continue;
                if (c.Rnd() < CafeChance && gaps[side].Count > 0)
                {
                    // кафе — в соседнем свободном промежутке
                    int ci = 0; float best = float.MaxValue;
                    for (int q = 0; q < gaps[side].Count; q++) { float dd = Mathf.Abs(gaps[side][q] - s); if (dd < best) { best = dd; ci = q; } }
                    Cafe(c, lounge, a, t, n, gaps[side][ci], len, cz);
                    gaps[side].RemoveAt(ci);
                }
            }
            // щит на столбе — у угла одной стороны, лицом к игроку
            if (DoStreetUnique && c.Rnd() < StreetBillboardChance)
            {
                PropEntry e = Pick(c, bill);
                int bs = c.R.Next(4);
                for (int k = 0; k < 4 && !SideOn(c, bs); k++) bs = (bs + 1) % 4;
                if (e != null && SideOn(c, bs))
                {
                    Vector2 a, t, n; float len;
                    Side(c, bs, out a, out t, out n, out len);
                    Vector2 at = a + t * (len - cz * 1.3f) - n * mid;
                    Ground(c, e, at, FaceYaw(c, e, at), Scale(e, c.M), pad);
                }
            }
        }

        /// <summary>Столики у тележки: зонт и 2–3 стула вокруг (шезлонги/стулья и зонты из «зоны отдыха»).</summary>
        private void Cafe(Ctx c, List<PropEntry> lounge, Vector2 a, Vector2 t, Vector2 n, float s, float len, float cz)
        {
            if (lounge.Count == 0 || s < cz || s > len - cz) return;
            List<PropEntry> umb = new List<PropEntry>(), chairs = new List<PropEntry>();
            foreach (PropEntry e in lounge) { if (e.Prefab.name.ToLowerInvariant().Contains("umbrella")) umb.Add(e); else chairs.Add(e); }
            Vector2 at = a + t * s - n * (c.Sw * 0.45f);
            if (!Free(c, at, new Vector2(3f * c.M, 3f * c.M), 0f)) return;
            PropEntry u = Pick(c, umb);
            if (u != null) { float k = Scale(u, c.M); Bounds b; if (B(u.Prefab, out b)) Put(c, c.Props, u, new Vector3(at.x, 0f, at.y), c.Range(0f, 360f), k); }
            int nc = c.R.Next(2, 4);
            for (int i = 0; i < nc; i++)
            {
                PropEntry ch = Pick(c, chairs);
                if (ch == null) break;
                float ang = (i * 360f / nc + c.Range(-20f, 20f)) * Mathf.Deg2Rad;
                Vector2 p = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 1.3f * c.M;
                Put(c, c.Props, ch, new Vector3(p.x, 0f, p.y), YawTo(ch, (at - p).normalized), Scale(ch, c.M));
            }
            Take(c, at, new Vector2(3.2f * c.M, 3.2f * c.M));
        }

        /// <summary>Дерево: крона не должна сильно лезть в фасад — если лезет, дерево поменьше или пропуск.</summary>
        private void Tree(Ctx c, PropEntry e, Vector2 at, Vector2 n, float off)
        {
            if (e == null) return;
            float k = Scale(e, c.M);
            Bounds b;
            if (!B(e.Prefab, out b)) return;
            float room = c.Sw - off + 0.8f * c.M;                         // до фасада + чуть можно залезть
            float crown = Mathf.Max(b.size.x, b.size.z) * k * 0.5f;
            if (crown > room) { float f = room / crown; if (f < 0.6f) return; k *= f; }
            Vector2 pos = at - n * off;
            Vector2 trunk = new Vector2(1.2f * c.M, 1.2f * c.M);
            if (!Free(c, pos, trunk, 0.3f * c.M)) return;
            Put(c, c.Props, e, new Vector3(pos.x, 0f, pos.y), c.Range(0f, 360f), k * c.Range(0.95f, 1.05f));
            Take(c, pos, trunk);
        }

        // ───────────────────────── дорога ─────────────────────────

        /// <summary>Машине не шире ~трети полудороги (мультяшно, но в полосу влезает).</summary>
        private float CarScale(Ctx c, PropEntry e)
        {
            float k = Scale(e, c.M);
            Bounds b;
            if (!B(e.Prefab, out b)) return k;
            float w = Mathf.Min(b.size.x, b.size.z) * k;
            if (w > c.H * 0.36f) k *= c.H * 0.36f / w;
            return k;
        }

        private void Parking(Ctx c)
        {
            List<PropEntry> cars = Of(PropKind.Car), trucks = Of(PropKind.FoodTruck), cones = Of(PropKind.Cone);
            if (c.H <= 0f || cars.Count == 0 && trucks.Count == 0) return;
            float czr = c.H * 1.2f;
            for (int side = 0; side < 4; side++)
            {
                Vector2 a, t, n; float len;
                Side(c, side, out a, out t, out n, out len);
                if (!SideOn(c, side)) continue;                                        // задняя сторона — с уровня не видно
                PropEntry probe = cars.Count > 0 ? cars[0] : trucks[0];
                Bounds pb;
                if (!B(probe.Prefab, out pb)) continue;
                float slot = Mathf.Max(pb.size.x, pb.size.z) * CarScale(c, probe) * 1.35f;
                float park = c.H * 0.2f;
                int slots = Mathf.Max(0, Mathf.FloorToInt((len - 2f * czr) / slot));
                float s0p = (len - slots * slot) * 0.5f;
                // паркоматы — у бордюра на границах мест стоянки (через одну)
                List<PropEntry> meters = SubOf(Of(PropKind.StreetSmall), "meter");
                if (meters.Count > 0)
                    for (int b = 0; b <= slots; b += 2)
                    {
                        if (!AlongOK(c, side, s0p + b * slot, len)) continue;
                        PropEntry me = Pick(c, meters);
                        Ground(c, me, a + t * (s0p + b * slot) - n * (c.Sw * 0.08f), YawTo(me, n), Scale(me, c.M), 0f);
                    }
                for (int si = 0; si < slots; si++)
                {
                    float s = s0p + (si + 0.5f) * slot;
                    if (c.Rnd() > ParkChance || !AlongOK(c, side, s, len)) continue;
                    PropEntry e = trucks.Count > 0 && c.Rnd() < FoodTruckChance ? Pick(c, trucks) : Pick(c, cars);
                    if (e == null) continue;
                    float k = CarScale(c, e);
                    Vector2 pos = a + t * (s + c.Range(-0.04f, 0.04f) * slot) + n * park;
                    Put(c, c.Props, e, new Vector3(pos.x, 0f, pos.y), YawTo(e, -t) + c.Range(-2f, 2f), k);
                }
                // конусы кучкой у бордюра
                if (cones.Count > 0 && c.Rnd() < ConeChance)
                {
                    float s0 = c.Range(czr, len - czr);
                    int nc = c.R.Next(3, 6);
                    for (int i = 0; i < nc; i++)
                    {
                        PropEntry e = Pick(c, cones);
                        Vector2 pos = a + t * (s0 + i * 1.6f * c.M) + n * (c.H * c.Range(0.1f, 0.35f));
                        Put(c, c.Props, e, new Vector3(pos.x, 0f, pos.y), c.Range(0f, 360f), Scale(e, c.M));
                    }
                }
            }
        }

        /// <summary>Машины в движении — по кругу вокруг квартала, правостороннее (бордюр справа), ровный шаг — не догоняют друг друга.</summary>
        private int Traffic(Ctx c)
        {
            List<PropEntry> cars = Of(PropKind.Car);
            if (cars.Count == 0 || c.H <= 0f) return 0;
            int n = c.R.Next(Mathf.Min(MovingCars.x, MovingCars.y), Mathf.Max(MovingCars.x, MovingCars.y) + 1);
            if (n <= 0) return 0;
            GameObject root = new GameObject(TrafficName);
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(root, "Машины");
#endif
            root.transform.SetParent(c.Module, false);
            CityTraffic tr = root.AddComponent<CityTraffic>();
            tr.X0 = c.Lot.X0; tr.Z0 = c.Lot.Z0; tr.X1 = c.Lot.X1; tr.Z1 = c.Lot.Z1;
            tr.Lane = c.H * 0.62f;
            tr.Speed = c.Range(Mathf.Min(Speed.x, Speed.y), Mathf.Max(Speed.x, Speed.y)) * c.M;
            float per = tr.Perimeter();
            for (int i = 0; i < n; i++)
            {
                PropEntry e = Pick(c, cars);
                GameObject pivot = Put(c, root.transform, e, Vector3.zero, 0f, CarScale(c, e));
                if (pivot == null) continue;
                c.Count--;                                           // машины в движении считаем отдельно
                tr.Cars.Add(pivot.transform);
                tr.Dist.Add((i + c.Range(0f, 0.35f)) * per / n);
                tr.YawOffset.Add(FrontOffset(e.Front));
            }
            tr.Apply();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            return tr.Cars.Count;
        }

        private void Reset()
        {
            City = GetComponent<CityBlockGenerator>();
        }
    }
}
