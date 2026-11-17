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
        [InspectorName("Мороженое (2–3 шарика, след ложки)")] IceCream = 5,
        [InspectorName("Меренга (безе-розочка с пикой)")] Meringue = 6,
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
        [Tooltip("v23.62: смесь с сахаром — по порошку крошечные блестящие кубики (0 — нет). Миндальная смесь ставит сама")]
        [Range(0f, 2f)] public float Sugar = 0f;

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
        private static Material[] _anime = new Material[7];
        private static bool _animeWarned;

        /// <summary>
        /// v23.6: все кучки (порошок, масло, паста, сахар) рисуются аниме-шейдером CatCooks/Anime Heap.
        /// Свой материал профиля берётся, если он уже на этом шейдере; иначе — общий аниме-материал вида.
        /// false — как раньше (свой материал профиля / старые шейдеры).
        /// </summary>
        public static bool AnimeHeaps = true;
        public const string AnimeShader = "CatCooks/Anime Heap";
        private static bool _warned;

        /// <summary>Материал вещества: свой или созданный по виду.</summary>
        public Material GetMaterial()
        {
            if (AnimeHeaps && Look == SubstanceLook.Powder && Sugar > 0.001f)
            {
                Material sm = SugarMixMaterial();                                   // v23.62: мука с искрами сахара
                if (sm != null) return sm;
            }
            if (AnimeHeaps && OwnLook)
            {
                if (Material != null && Material.shader != null && Material.shader.name == AnimeShader) return Material;
                Material a = AnimeMaterial(Look);
                if (a != null) return a;
            }
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
            if (look == SubstanceLook.Butter || look == SubstanceLook.IceCream || look == SubstanceLook.Meringue)
            {
                if (_autoButter == null) { _autoButter = Make("CatCooks/Toon Food", "CC Substance Butter (auto)"); ToonPreset(_autoButter, SubstanceLook.Butter); }   // v19.2: Toon Food v3
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

        private static Material _sugarMix;

        /// <summary>v23.62: порошок-смесь с сахаром: тот же аниме-порошок, но с искорками и лёгким бликом на гранях кубиков.</summary>
        public static Material SugarMixMaterial()
        {
            if (_sugarMix != null) return _sugarMix;
            Material b = AnimeMaterial(SubstanceLook.Powder);
            if (b == null) return null;
            _sugarMix = new Material(b) { name = "CC Anime Heap PowderSugar (auto)", hideFlags = HideFlags.DontSave };
            SetF(_sugarMix, "_HSparkle", 0.45f);
            SetF(_sugarMix, "_HSpec", 0.35f);
            SetF(_sugarMix, "_HSpecSize", 0.975f);
            return _sugarMix;
        }

        /// <summary>v23.6: общий аниме-материал вида (создаётся один раз, настройки — <see cref="AnimePreset"/>).</summary>
        public static Material AnimeMaterial(SubstanceLook look)
        {
            int i = (int)look;
            if (i <= 0 || i >= _anime.Length) return null;
            if (_anime[i] != null) return _anime[i];
            Shader sh = Shader.Find(AnimeShader);
            if (sh == null)
            {
                if (!_animeWarned) { _animeWarned = true; Debug.LogWarning("[Вещества] нет шейдера «" + AnimeShader + "» — кучки рисуются старыми шейдерами"); }
                return null;
            }
            var m = new Material(sh) { name = "CC Anime Heap " + look + " (auto)", hideFlags = HideFlags.DontSave };
            AnimePreset(m, look);
            _anime[i] = m;
            return m;
        }

        /// <summary>v23.6: настройки CatCooks/Anime Heap под вид вещества (подобраны по превью).</summary>
        public static void AnimePreset(Material m, SubstanceLook look)
        {
            if (m == null) return;
            if (look == SubstanceLook.Powder)
            {
                // v23.6: чисто — две краски + обводка (свет почти без третьей краски, глубокая — только у пола)
                SetF(m, "_HKind", 0f); SetF(m, "_HSpec", 0f); SetF(m, "_HSparkle", 0f); SetF(m, "_HRim", 0.3f);
                SetF(m, "_HLayers", 1f); SetF(m, "_HSpeck", 1f); SetF(m, "_HTermGlow", 0.2f); SetF(m, "_HBrush", 0.1f);
                SetF(m, "_HInk", 0.35f); SetF(m, "_HT1", 0.16f); SetF(m, "_HT3", 0.9f); SetF(m, "_HShadeDepth", 0.74f);
                SetF(m, "_HContact", 0.5f); SetF(m, "_HSmooth", 0.15f); SetF(m, "_HOutline", 3f);   // v23.9: обводка толще                       // v23.8: комья — в модели, свет по ним
            }
            else if (look == SubstanceLook.Butter)
            {
                SetF(m, "_HKind", 1f); SetF(m, "_HShadeDepth", 0.86f); SetF(m, "_HDeep", 0.64f); SetF(m, "_HTermGlow", 0.3f);
                SetF(m, "_HInk", 0.3f); SetF(m, "_HSat", 1.15f); SetF(m, "_HSpec", 0.6f); SetF(m, "_HSpecSize", 0.965f);
                SetF(m, "_HSparkle", 0.12f); SetF(m, "_HRim", 0.5f); SetF(m, "_HBounceAmt", 0.35f); SetF(m, "_HLayers", 0f);
                SetF(m, "_HBrush", 0.1f);
            }
            else if (look == SubstanceLook.Spread)
            {
                // v23.11: глянцевая «нутелла»: сильный блеск длинными полосами, тёплая тень, карамельный отсвет на краях
                SetF(m, "_HKind", 2f); SetF(m, "_HShadeDepth", 0.66f); SetF(m, "_HDeep", 0.4f); SetF(m, "_HSpec", 0.9f);
                SetF(m, "_HSpecSize", 0.986f); SetF(m, "_HSpecBreak", 0.15f); SetF(m, "_HBounceAmt", 0.5f); SetF(m, "_HTermGlow", 0.55f);
                SetF(m, "_HLayers", 0f); SetF(m, "_HSparkle", 0.05f); SetF(m, "_HBrush", 0.08f); SetF(m, "_HInk", 0.15f);
                SetF(m, "_HSat", 1.3f); SetF(m, "_HRim", 0.55f); SetF(m, "_HSss", 0.5f); SetF(m, "_HOutline", 1.6f);
            }
            else if (look == SubstanceLook.IceCream)
            {
                // v23.35: мороженое — матовое, мягкая холодная тень, обводка, без блеска и искр
                SetF(m, "_HKind", 1f); SetF(m, "_HShadeDepth", 0.84f); SetF(m, "_HDeep", 0.66f); SetF(m, "_HTermGlow", 0.15f);
                SetF(m, "_HInk", 0.3f); SetF(m, "_HSat", 1.05f); SetF(m, "_HSpec", 0.12f); SetF(m, "_HSpecSize", 0.96f);
                SetF(m, "_HSparkle", 0f); SetF(m, "_HRim", 0.45f); SetF(m, "_HBounceAmt", 0.3f); SetF(m, "_HLayers", 0f);
                SetF(m, "_HBrush", 0.12f); SetF(m, "_HOutline", 2f); SetF(m, "_HSss", 0f);
                if (m.HasProperty("_HShadeCool")) m.SetColor("_HShadeCool", new Color(0.8f, 0.88f, 1f, 1f));
            }
            else if (look == SubstanceLook.Meringue)
            {
                // v23.51: меренга — белая, нежная: мягкая сиреневая тень, лёгкий атласный блик, обводка
                SetF(m, "_HKind", 1f); SetF(m, "_HShadeDepth", 0.86f); SetF(m, "_HDeep", 0.7f); SetF(m, "_HTermGlow", 0.1f);
                SetF(m, "_HInk", 0.25f); SetF(m, "_HSat", 1f); SetF(m, "_HSpec", 0.25f); SetF(m, "_HSpecSize", 0.97f);
                SetF(m, "_HSparkle", 0f); SetF(m, "_HRim", 0.5f); SetF(m, "_HBounceAmt", 0.35f); SetF(m, "_HLayers", 0f);
                SetF(m, "_HBrush", 0.06f); SetF(m, "_HOutline", 2f); SetF(m, "_HSss", 0.2f);
                if (m.HasProperty("_HShadeCool")) m.SetColor("_HShadeCool", new Color(0.86f, 0.84f, 1f, 1f));
            }
            else if (look == SubstanceLook.Crystals)
            {
                SetF(m, "_HKind", 3f); SetF(m, "_HShadeDepth", 0.86f); SetF(m, "_HDeep", 0.6f); SetF(m, "_HInk", 0.2f);
                SetF(m, "_HSpec", 0.9f); SetF(m, "_HSpecSize", 0.975f); SetF(m, "_HSparkle", 0.8f); SetF(m, "_HRim", 0.6f);
                SetF(m, "_HLayers", 0.5f); SetF(m, "_HTermGlow", 0.15f); SetF(m, "_HOutline", 2.2f); SetF(m, "_HBrush", 0.05f);
                if (m.HasProperty("_HShadeCool")) m.SetColor("_HShadeCool", new Color(0.78f, 0.84f, 1f, 1f));
            }
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

        /// <summary>v19.2: старое имя — теперь пресет тун-шейдера для пасты.</summary>
        public static void SpreadGloss(Material m) { ToonPreset(m, SubstanceLook.Spread); }

        /// <summary>
        /// v19.2: настройки CatCooks/Toon Food v3 для вида вещества: цвет из вершин (sRGB), тун-свет,
        /// силуэт цветом еды, чернильный контур склонов; масло — мягкий блеск и «подсветка изнутри»,
        /// паста — сильный глянец, глазурь и разводы.
        /// </summary>
        public static void ToonPreset(Material m, SubstanceLook look)
        {
            if (m == null) return;
            SetF(m, "_VertexColorTint", 1f); SetF(m, "_VertexColorSRGB", 1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", UnityEngine.Color.white);
            SetF(m, "_ShadowThreshold", 0.38f); SetF(m, "_ShadowSoftness", 0.08f); SetF(m, "_LightWrap", 0.3f);
            SetF(m, "_ContourOn", 0f); SetF(m, "_ContourWidth", 0.3f);   // v19.3: на бугристом рельефе контур рисовал «топографические» волны — выключен, рельеф читается силуэтом SetF(m, "_ContourDark", 0.62f); SetF(m, "_ContourLightMask", 0.4f);
            SetF(m, "_OutlineWidth", 1.4f); SetF(m, "_OutlineVertexColor", 1f); SetF(m, "_OutlineTextureBlend", 0f); SetF(m, "_OutlineMode", 0f);
            SetF(m, "_RimOn", 0f); SetF(m, "_SpecularOn", 1f); SetF(m, "_SparkleOn", 0f);
            if (look == SubstanceLook.Spread)
            {
                SetC(m, "_ShadowTint", new Color(0.62f, 0.44f, 0.4f));
                SetF(m, "_SssOn", 0f);
                SetF(m, "_SpecSize", 0.45f); SetF(m, "_SpecIntensity", 1.1f); SetF(m, "_SheenIntensity", 0.5f);
                SetF(m, "_GlazeOn", 1f); SetF(m, "_GlazeIntensity", 0.4f);
                SetF(m, "_MarbleAmount", 0.6f); SetF(m, "_MarbleScale", 11f); SetF(m, "_MarbleLight", 1.45f);
            }
            else
            {
                SetC(m, "_ShadowTint", new Color(0.9f, 0.68f, 0.45f));
                SetF(m, "_SssOn", 1f); SetC(m, "_SSSColor", new Color(1f, 0.8f, 0.4f)); SetF(m, "_SSSStrength", 0.4f);
                SetF(m, "_SpecSize", 0.3f); SetF(m, "_SpecIntensity", 0.5f); SetF(m, "_SheenIntensity", 0.25f);
                SetF(m, "_GlazeOn", 0f); SetF(m, "_MarbleAmount", 0f);
            }
        }

        private static void SetF(Material m, string n, float v) { if (m.HasProperty(n)) m.SetFloat(n, v); }
        private static void SetC(Material m, string n, Color c) { if (m.HasProperty(n)) m.SetColor(n, c); }

        /// <summary>Сыграть случайный звук из списка (только у себя, в точке).</summary>
        public void Play(AudioClip[] clips, Vector3 at)
        {
            if (clips == null || clips.Length == 0 || Volume <= 0f || !Application.isPlaying) return;
            AudioClip c = clips[Random.Range(0, clips.Length)];
            if (c != null) AudioSource.PlayClipAtPoint(c, at, Volume);
        }
    }
}
