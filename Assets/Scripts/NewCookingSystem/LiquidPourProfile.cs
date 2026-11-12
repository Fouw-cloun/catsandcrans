using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КАК ЛЬЁТСЯ ЖИДКОСТЬ — вид и «характер» струи (вода, молоко, масло, мёд, битум...).
    ///
    /// Стиль — мультяшный, как у стилизованных водопадов: цвет ступеньками (тёмный край,
    /// основной, светлые прожилки, белая пена), прожилки бегут вдоль струи, у кромки и
    /// в месте падения — пена, хвост рвётся на капли, при ударе — корона-всплеск и
    /// пенное пятно.
    ///
    /// Создать: Assets → Create → CatCooks → Liquid Pour Profile. Или не создавать вовсе:
    /// если профиля нет, он подбирается сам по цвету жидкости (см. For(color)).
    /// Готовые варианты — статические Water(), Milk(), Oil(), Honey(), Bitumen() и т.д.
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Liquid Pour Profile", fileName = "PourProfile")]
    public sealed class LiquidPourProfile : ScriptableObject
    {
        [Header("Цвета (ступеньки тона)")]
        [Tooltip("Если включено — Deep/Mid/Light/Foam считаются из цвета жидкости автоматически")]
        public bool FromLiquidColor = true;
        public Color Deep = new Color(0.10f, 0.45f, 0.55f);
        public Color Mid = new Color(0.25f, 0.70f, 0.78f);
        public Color Light = new Color(0.65f, 0.92f, 0.95f);
        public Color Foam = new Color(0.97f, 1.00f, 1.00f);
        [Range(0f, 1f)] public float Opacity = 0.9f;

        [Header("Характер")]
        [Tooltip("0 — вода, 0.5 — сливки/масло, 1 — мёд/битум: медленнее, толще, меньше брызг, тянется")]
        [Range(0f, 1f)] public float Viscosity = 0f;
        [Tooltip("Скорость прожилок вдоль струи")]
        public float FlowSpeed = 2.2f;
        [Tooltip("Частота прожилок: поперёк / вдоль")]
        public Vector2 StreakScale = new Vector2(3f, 10f);
        [Tooltip("Дрожание поверхности струи (доля радиуса)")]
        [Range(0f, 0.6f)] public float Wobble = 0.18f;
        [Tooltip("Пена у кромки (доля длины) и у места падения")]
        [Range(0f, 0.5f)] public float LipFoam = 0.08f;
        [Range(0f, 0.5f)] public float BottomFoam = 0.12f;
        [Tooltip("Сколько светлых прожилок и пены по всей струе (0..1)")]
        [Range(0f, 1f)] public float StreakAmount = 0.55f;
        [Tooltip("Широкая «простыня» с края сворачивается в струю за это время, с")]
        [Range(0.05f, 2f)] public float Necking = 0.35f;
        [Tooltip("Хвост струи рвётся на куски/капли (0 — ровный, 1 — сильно)")]
        [Range(0f, 1f)] public float BreakUp = 0.5f;
        [Tooltip("Блик-полоска вдоль струи")]
        [Range(0f, 2f)] public float Gloss = 0.8f;

        [Header("Удар и брызги")]
        [Tooltip("Размер короны-всплеска (× толщина струи)")]
        public float SplashSize = 3.2f;
        [Tooltip("Капли, отрывающиеся от струи, в секунду")]
        public float DropsPerSecond = 10f;
        [Tooltip("Пенное пятно на месте падения (× толщина)")]
        public float FoamPatch = 4f;

        // ───────────── вычисленные цвета ─────────────

        /// <summary>Ступеньки тона для цвета жидкости: тёмный край теплее/насыщеннее, светлый — к белому.</summary>
        public void Resolve(Color liquid, out Color deep, out Color mid, out Color light, out Color foam)
        {
            if (!FromLiquidColor)
            {
                deep = Deep; mid = Mid; light = Light; foam = Foam;
                return;
            }
            Color.RGBToHSV(liquid, out float h, out float s, out float v);
            mid = liquid;
            // тень: темнее и насыщеннее, чуть сдвинута по тону (мультяшная цветная тень)
            deep = Color.HSVToRGB(Mathf.Repeat(h + 0.015f, 1f), Mathf.Clamp01(s * 1.15f + 0.08f), v * 0.62f);
            // v10: молоко/сливки (светлое, почти без цвета) — тень ХОЛОДНАЯ голубоватая, а не грязно-бежевая
            float milky = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.92f, v)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.32f, s)));
            deep = Color.Lerp(deep, new Color(0.72f, 0.78f, 0.9f) * v, milky);
            light = Color.Lerp(liquid, Color.white, 0.45f);
            // пена: у светлых жидкостей почти белая, у тёмных (битум, соус) — светлая версия самой жидкости,
            // иначе под битумом появится белая «молочная» лужа
            float white = Mathf.Lerp(0.3f, 0.88f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.6f, v)));
            foam = Color.Lerp(liquid, Color.white, white);
            deep.a = mid.a = light.a = foam.a = 1f;
        }

        // ───────────── готовые профили ─────────────

        private static LiquidPourProfile Make(string name, float visc, float opacity, float flow, float wobble,
                                              float streaks, float neck, float breakUp, float splash, float drops, float gloss)
        {
            var p = CreateInstance<LiquidPourProfile>();
            p.name = name;
            p.hideFlags = HideFlags.DontSave;
            p.Viscosity = visc; p.Opacity = opacity; p.FlowSpeed = flow; p.Wobble = wobble;
            p.StreakAmount = streaks; p.Necking = neck; p.BreakUp = breakUp;
            p.SplashSize = splash; p.DropsPerSecond = drops; p.Gloss = gloss;
            return p;
        }

        private static LiquidPourProfile _water, _milk, _cream, _oil, _juice, _honey, _bitumen, _broth, _sourCream;

        /// <summary>Сметана: очень густая, белая, сползает медленно и рвётся шлепками.</summary>
        public static LiquidPourProfile SourCream() => _sourCream != null ? _sourCream
            : (_sourCream = Make("Sour cream (auto)", 0.92f, 1f, 0.45f, 0.06f, 0.12f, 1.1f, 0.75f, 0.55f, 0.6f, 1.5f));

        public static LiquidPourProfile Water() => _water != null ? _water : (_water = WaterMake());
        private static LiquidPourProfile WaterMake()
        {
            var p = Make("Water (auto)", 0f, 0.8f, 2.6f, 0.22f, 0.7f, 0.45f, 0.65f, 3.6f, 14f, 1.1f);
            p.FromLiquidColor = false;
            return p;
        }
        // v7: молоко «с телом» — гуще воды (медленнее, толще, меньше рвётся и брызжет), матовое (мягкий блик)
        public static LiquidPourProfile Milk() => _milk != null ? _milk : (_milk = Make("Milk (auto)", 0.3f, 1f, 1.7f, 0.08f, 0.35f, 0.55f, 0.15f, 3.6f, 6f, 0.35f));
        public static LiquidPourProfile Cream() => _cream != null ? _cream : (_cream = Make("Cream (auto)", 0.4f, 1f, 1.5f, 0.1f, 0.35f, 0.5f, 0.3f, 2.4f, 5f, 0.7f));
        public static LiquidPourProfile Oil() => _oil != null ? _oil : (_oil = Make("Oil (auto)", 0.45f, 0.85f, 1.4f, 0.08f, 0.4f, 0.55f, 0.25f, 2f, 4f, 1.6f));
        public static LiquidPourProfile Juice() => _juice != null ? _juice : (_juice = Make("Juice (auto)", 0.05f, 0.88f, 2.4f, 0.2f, 0.6f, 0.3f, 0.6f, 3.4f, 12f, 1f));
        public static LiquidPourProfile Honey() => _honey != null ? _honey : (_honey = Make("Honey (auto)", 1f, 0.95f, 0.6f, 0.04f, 0.25f, 1.2f, 0.05f, 1.2f, 0.5f, 1.8f));
        public static LiquidPourProfile Bitumen() => _bitumen != null ? _bitumen : (_bitumen = Make("Bitumen (auto)", 0.9f, 1f, 0.7f, 0.05f, 0.15f, 1f, 0.1f, 1.4f, 1f, 2f));
        public static LiquidPourProfile Broth() => _broth != null ? _broth : (_broth = Make("Broth (auto)", 0.05f, 0.8f, 2.4f, 0.2f, 0.55f, 0.3f, 0.6f, 3.4f, 12f, 1f));

        /// <summary>
        /// Профиль по цвету жидкости, если своего нет: светлое непрозрачное → молоко, тёмное → битум,
        /// тёплое жёлто-оранжевое → масло/мёд, остальное → вода/сок.
        /// </summary>
        /// <summary>Заданный профиль или подобранный по цвету — одна точка выбора для вида и сервера.</summary>
        public static LiquidPourProfile Pick(LiquidPourProfile assigned, Color liquid)
            => assigned != null ? assigned : For(liquid);

        /// <summary>
        /// Во сколько раз вязкая жидкость вытекает медленнее (вода 1, мёд 0.5).
        /// Одна формула для струи-меша и для серверного расчёта, куда падает порция:
        /// что видишь — туда и попало.
        /// </summary>
        public float SpeedScale => Mathf.Lerp(1f, 0.5f, Viscosity);

        /// <summary>
        /// Как льётся ЭТОТ продукт: по названию → по цвету.
        /// Так из ведра с водой льётся вода, с молоком — молоко.
        /// </summary>
        public static LiquidPourProfile ForProduct(ProductDef p, Color fallbackColor)
        {
            if (p == null) return For(fallbackColor);
            LiquidPourProfile byName = ByName(p.Title);
            return byName != null ? byName : For(p.BaseColor);
        }

        private static LiquidPourProfile ByName(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            string t = title.ToLowerInvariant();
            if (t.Contains("вод")) return Water();
            if (t.Contains("молок")) return Milk();
            if (t.Contains("сметан")) return SourCream();
            if (t.Contains("сливк")) return Cream();
            if (t.Contains("бульон") || t.Contains("суп")) return Broth();
            if (t.Contains("мёд") || t.StartsWith("мед") || t.Contains("сироп")) return Honey();
            if (t.Contains("соус")) return Honey();                 // острый соус — тянется, как мёд
            if (t.Contains("острая смесь")) return Juice();
            if (t.Contains("битум") || t.Contains("смол")) return Bitumen();
            if (t.Contains("сок")) return Juice();
            if (t.Contains("уксус")) return Water();
            if (t.Contains("растительн") || t.Contains("оливк") || t.Contains("подсолн")) return Oil();
            return null;
        }

        public static LiquidPourProfile For(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (v < 0.22f) return Bitumen();
            if (h > 0.45f && h < 0.65f && s > 0.12f) return Water();        // голубое — вода (раньше уходило в «сливки»)
            if (s < 0.18f && v > 0.8f) return Milk();
            if (s < 0.3f && v > 0.75f) return Cream();
            if (h > 0.08f && h < 0.17f && s > 0.5f) return v > 0.7f ? Oil() : Honey();
            if (h > 0.45f && h < 0.6f) return Water();
            return Juice();
        }
    }
}
