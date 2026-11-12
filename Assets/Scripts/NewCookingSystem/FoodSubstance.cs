using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>Как рисуется вещество (свой рисовальщик). Renderer — оставить старый вид (пресет кучки).</summary>
    public enum SubstanceLook : byte
    {
        [InspectorName("Как раньше (пресет кучки)")]       Renderer = 0,
        [InspectorName("Порошок (комки, матовый)")]         Powder = 1,
        [InspectorName("Масло (рваное, фермерское)")]        Butter = 2,
        [InspectorName("Кристаллы (сахар, соль)")]            Crystals = 3,
        [InspectorName("Паста (гладкая, глянец, завиток — нутелла)")] Spread = 4,
    }

    /// <summary>Поведение: что делает вещество, когда тару наклоняют.</summary>
    public enum SubstanceFlow : byte
    {
        [InspectorName("Льётся (жидкость)")]   Pours = 0,
        [InspectorName("Сыплется (кучкой)")]   Spills = 1,
        [InspectorName("Держит ком")]           HoldsLump = 2,
    }

    /// <summary>
    /// v18: ВЕЩЕСТВО — один профиль на вид продукта: «Какао-порошок», «Сливочное масло»…
    ///
    /// Один и тот же профиль рисует продукт везде: в таре (LiquidAnchor и HeapAnchor),
    /// кучкой на полу, на полотне мастерка и лопаты, и решает эффекты (пыль, капли, пар),
    /// звуки и поведение (льётся / сыплется / держит ком).
    ///
    /// Какой профиль у продукта — решает таблица <see cref="FoodSubstanceTable"/>
    /// (ассет «FoodSubstances» в папке Resources). Спросить: FoodSubstance.Of(продукт, состояние).
    ///
    /// Создать: меню CatCooks → Вещества → 1. Создать профили и таблицу (масло, какао-порошок).
    /// Новый вид — скопировать похожий ассет и поменять ползунки, код не нужен.
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Вещество (вид продукта)", fileName = "Substance_New")]
    public sealed class FoodSubstance : ScriptableObject
    {
        [Header("Вид")]
        [Tooltip("Название для логов и отчёта")]
        public string Title = "Вещество";
        [Tooltip("Чем рисовать")]
        public SubstanceLook Look = SubstanceLook.Renderer;
        [Tooltip("Если рисует старый рисовальщик — какой пресет кучки (и для тех, кто спрашивает FoodHeapLook)")]
        public FoodHeapRenderer.HeapPreset FallbackPreset = FoodHeapRenderer.HeapPreset.Custom;
        [Tooltip("Цвет из продукта (вкл) или свой (выкл)")]
        public bool UseProductColor = true;
        public Color Color = new Color(0.33f, 0.19f, 0.11f);
        [Tooltip("Чуть светлее/темнее цвета продукта (1 — как есть)")]
        [Range(0.5f, 1.5f)] public float Brightness = 1f;
        [Tooltip("Свой материал. Пусто — создастся сам (Powder Heap / Butter)")]
        public Material Material;

        [Header("В таре")]
        [Tooltip("Рисовать поверхность в таре по этому профилю (вместо роли на таре)")]
        public bool InCookware = true;
        [Tooltip("Рельеф поверхности в таре (1 — как задумано)")]
        [Range(0f, 3f)] public float CookwareRelief = 1f;
        [Tooltip("Масло: сколько мазков мастерком на пласте")]
        [Range(0, 9)] public int Smears = 5;
        [Tooltip("Масло: глубина мазков")]
        [Range(0f, 2f)] public float SmearDepth = 1f;

        [Header("Кучка на полу")]
        [Tooltip("Высота горки к радиусу: 0.4 — пологая, 0.9 — острая")]
        [Range(0.25f, 1.2f)] public float FloorSlope = 0.6f;
        [Tooltip("Кривизна: 0 — ровная, 1 — кривая, бугристая")]
        [Range(0f, 1f)] public float Wonky = 0.5f;

        [Header("На мастерке / лопате")]
        [Tooltip("Высота горки на полотне (1 — как у кучки)")]
        [Range(0.3f, 2.5f)] public float BladeHeight = 1.2f;
        [Tooltip("Масло: завиток-«пика», как будто срезали ножом (0 — нет)")]
        [Range(0f, 1f)] public float Curl = 0.8f;
        [Tooltip("Дрожь (джигл) при ходьбе и рывках: 0 — твёрдое, 1 — желе")]
        [Range(0f, 1f)] public float Jiggle = 0.5f;
        [Tooltip("v18.2: горка на полотне всегда занимает хотя бы эту долю полотна (0.6 — больше половины)")]
        [Range(0.2f, 1f)] public float BladeCover = 0.6f;
        [Tooltip("v18.2: во сколько раз горка на полотне «объёмнее» порций (только вид)")]
        [Range(1f, 10f)] public float BladeVolume = 4f;
        [Tooltip("v18.2: кубик продукта (брусок масла, нарезанный кубиками) на мастерке — мазком-кучкой, а не кубиком; " +
                 "и с мастерка на пол — кучкой")]
        public bool SmearNotPiece = true;

        [Header("Эффекты (вес и вид не меняют)")]
        [Tooltip("Пыль, когда зачерпнули (порошок)")]
        public bool DustOnScoop;
        [Tooltip("Сколько пыли, 1 — обычно")]
        [Range(0f, 3f)] public float DustAmount = 1f;
        [Tooltip("Капли/крошки с края полотна, когда дёргаешь мастерок")]
        public bool EdgeDrips;
        [Tooltip("Чувствительность: при каком рывке падает капля (м/с²). Меньше — чаще")]
        [Range(2f, 60f)] public float DripShake = 18f;
        [Tooltip("Капли: комочки (масло) или крошки (порошок) — размер, доля полотна")]
        [Range(0.02f, 0.3f)] public float DripSize = 0.08f;
        [Tooltip("Лёгкий пар над полотном и кучкой (горячее)")]
        public bool Steam;

        [Header("Поведение")]
        public SubstanceFlow Flow = SubstanceFlow.Spills;
        [Tooltip("v19: липнет к таре — тару перевернули, а это остаётся внутри (масло, паста). Порошок — выкл")]
        public bool StickToCookware;

        [Header("Кристаллы (сахар, соль)")]
        [Tooltip("Размер кристаллика, доля радиуса кучки")]
        [Range(0.01f, 0.15f)] public float CrystalSize = 0.05f;
        [Tooltip("Сколько кристалликов на кучке (густота)")]
        [Range(0.1f, 2f)] public float CrystalDensity = 1f;

        [Header("Пропорции (смесь по рецепту)")]
        [Tooltip("v19: результат рецепта «перемешать» считается по ПРОПОРЦИЯМ: в допуске рецепта — норма (хуже только оценка), " +
                 "сильнее — масса получается ИСПОРЧЕННОЙ (видно), её можно исправить, досыпав недостающее и перемешав снова")]
        public bool Blend;
        [Tooltip("Насколько сильный перебор ещё кодируется точно (доля); больше — считается как этот максимум")]
        [Range(0.2f, 1f)] public float BlendMaxExcess = 0.6f;

        [Header("Звуки (пусто — без звука)")]
        public AudioClip[] ScoopSounds;
        public AudioClip[] DropSounds;
        [Range(0f, 1f)] public float Volume = 0.7f;

        // ───────────────────────── ВЫЗОВЫ ─────────────────────────

        /// <summary>Профиль вещества для продукта в состоянии. null — профиля нет (старые правила).</summary>
        public static FoodSubstance Of(ProductDef p, FoodState s) => FoodSubstanceTable.Resolve(p, s);

        /// <summary>Рисует ли этот профиль сам (а не старый пресет).</summary>
        public bool OwnLook => Look != SubstanceLook.Renderer;

        /// <summary>Цвет вещества с учётом настройки (продукт или свой).</summary>
        public Color ColorFor(Color product)
        {
            Color c = UseProductColor ? product : Color;
            c *= Brightness;
            c.a = 1f;
            return c;
        }

        private static Material _autoPowder, _autoButter, _autoSugar, _autoSpread;
        private static bool _warned;

        /// <summary>Материал вещества: свой или созданный по виду.</summary>
        public Material GetMaterial()
        {
            if (Material != null) return Material;
            return AutoMaterial(Look);
        }

        public static Material AutoMaterial(SubstanceLook look)
        {
            if (look == SubstanceLook.Powder)
            {
                if (_autoPowder == null) _autoPowder = Make("CatCooks/Powder Heap", "CC Substance Powder (auto)");
                return _autoPowder;
            }
            if (look == SubstanceLook.Butter)
            {
                if (_autoButter == null) _autoButter = Make("CatCooks/Toon Food", "CC Substance Butter (auto)");   // v19.1: тун с обводкой
                return _autoButter;
            }
            if (look == SubstanceLook.Crystals)
            {
                if (_autoSugar == null) _autoSugar = Make("CatCooks/Sugar", "CC Substance Sugar (auto)");
                return _autoSugar;
            }
            if (look == SubstanceLook.Spread)
            {
                if (_autoSpread == null)
                {
                    _autoSpread = Make("CatCooks/Toon Food", "CC Substance Spread (auto)");
                    if (_autoSpread != null) SpreadGloss(_autoSpread);
                }
                return _autoSpread;
            }
            return null;
        }

        private static Material Make(string shader, string name)
        {
            Shader sh = Shader.Find(shader);
            if (sh == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning($"[Вещества] нет шейдера «{shader}» — вид будет простым. Меню CatCooks → Вещества → 1 (или назначь материал в профиле)");
                }
                sh = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (sh == null) return null;
            }
            return new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
        }

        /// <summary>Паста-нутелла: тот же шейдер масла, но сильный глянец, холодная тень.</summary>
        public static void SpreadGloss(Material m)
        {
            if (m.HasProperty("_Sheen")) m.SetFloat("_Sheen", 0.9f);
            if (m.HasProperty("_SheenSize")) m.SetFloat("_SheenSize", 60f);
            if (m.HasProperty("_Warm")) m.SetFloat("_Warm", 0.12f);
            if (m.HasProperty("_Rim")) m.SetFloat("_Rim", 0.28f);
            if (m.HasProperty("_ShadowTint")) m.SetColor("_ShadowTint", new Color(0.55f, 0.4f, 0.36f, 1f));
            if (m.HasProperty("_Marble")) m.SetFloat("_Marble", 0.65f);          // v19.1: разводы смешивания
            if (m.HasProperty("_MarbleScale")) m.SetFloat("_MarbleScale", 11f);
            if (m.HasProperty("_MarbleLight")) m.SetFloat("_MarbleLight", 1.45f);
        }

        /// <summary>Сыграть случайный звук из списка (только у себя, в точке).</summary>
        public void Play(AudioClip[] clips, Vector3 at)
        {
            if (clips == null || clips.Length == 0 || Volume <= 0f || !Application.isPlaying) return;
            AudioClip c = clips[Random.Range(0, clips.Length)];
            if (c != null) AudioSource.PlayClipAtPoint(c, at, Volume);
        }
    }
}
