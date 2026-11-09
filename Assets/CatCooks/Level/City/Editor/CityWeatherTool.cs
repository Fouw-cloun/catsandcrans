using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ПОГОДЫ ГОРОДА — читаемая лестница от ясного неба до урагана + забавные (пыльная буря, снежные тучи, жара,
    /// розовая дымка…). Только небо, облака, свет, дымка и цвет — эффекты (капли, молнии, листья, пыль) пока
    /// выключены: в пресете они 0, задуманная сила записана в заметке (note) — включим, когда займёмся эффектами.
    /// Пресеты: Assets/Generated/City/Weather/CW_xx_*.asset. Список погод в Mood Director заменяется на них
    /// (старые файлы не удаляются). «Исправить зелёную дымку» — дымка в стилях/палитрах суток без зелени.
    /// </summary>
    public static class CityWeatherTool
    {
        public const string Folder = "Assets/Generated/City/Weather";

        private sealed class W
        {
            public string File, Title, Note;
            public float Sev, Cov, Sky, Dark, Over, Stretch = 1.5f, Wisp = 0.2f, Width = 1.1f, Body = 1f, Pop = 0.7f;
            public bool Fog = true; public float FogD = 0.7f, FogS = 1.2f, FogE = 8f;   // в модулях
            public float Rays = 1f, Shadow = 0.45f, Exp, Sat, Con, Temp, Vig, Bloom, SunDim = 1f, Frame = 0.1f, Desat = 0.2f;
            public Color FrameCol = new Color(0.22f, 0.26f, 0.36f);
            public float Wind = 0.3f, Streaks = 0.2f, Evolve = 0.05f, Trans = 40f;
            public CatCooksWeatherPreset.Hazard Haz;
            // задуманные эффекты (пока 0 в пресете)
            public float Rain, Light, Leaves, Dust;
            public float Diff = -1f;      // сложность для игрока (−1 — по силе непогоды)
        }

        private static List<W> Ladder()
        {
            List<W> l = new List<W>();
            l.Add(new W { File = "00_Clear", Title = "Ясно", Sev = 0f, Cov = 0.12f, Sky = 0.15f, Rays = 0.6f, Shadow = 0.2f, Wind = 0.15f, Sat = 4f, FogD = 0.55f, FogS = 1.6f, FogE = 10f, Trans = 15f });
            l.Add(new W { File = "01_Cirrus", Title = "Перистые", Sev = 0.05f, Cov = 0.2f, Sky = 0.35f, Stretch = 4.5f, Wisp = 0.85f, Width = 0.7f, Rays = 0.7f, Wind = 0.35f, FogD = 0.55f, FogS = 1.5f, FogE = 10f });
            l.Add(new W { File = "02_FairCumulus", Title = "Редкие кучевые", Sev = 0.1f, Cov = 0.3f, Sky = 0.32f, Rays = 0.9f, Shadow = 0.4f, Wind = 0.25f, Sat = 3f });
            l.Add(new W { File = "03_Cumulus", Title = "Кучевые", Sev = 0.2f, Cov = 0.45f, Sky = 0.48f, Rays = 1.1f, Shadow = 0.5f, Wind = 0.3f, Pop = 0.8f });
            l.Add(new W { File = "04_Sunbreaks", Title = "Солнце сквозь облака", Sev = 0.3f, Cov = 0.62f, Sky = 0.6f, Rays = 1.8f, Shadow = 0.6f, Con = 6f, Wind = 0.35f, Pop = 0.9f });
            l.Add(new W { File = "05_HeatHaze", Title = "Жара, марево", Sev = 0.25f, Cov = 0.08f, Sky = 0.1f, Rays = 0.5f, Exp = 0.1f, Sat = 14f, Con = 4f, Temp = 16f, Bloom = 0.25f, FogD = 0.85f, FogS = 0.8f, FogE = 6f, Wind = 0.1f, Haz = CatCooksWeatherPreset.Hazard.Heat, Note = "задумано: пыль 0.2" , Dust = 0.2f });
            l.Add(new W { File = "06_MorningMist", Title = "Утренняя дымка", Sev = 0.2f, Cov = 0.3f, Sky = 0.3f, Rays = 1.4f, Exp = 0.05f, Sat = -8f, Temp = -4f, FogD = 0.95f, FogS = 0.4f, FogE = 4f, Wind = 0.05f, Haz = CatCooksWeatherPreset.Hazard.Fog });
            l.Add(new W { File = "07_Overcast", Title = "Пасмурно", Sev = 0.45f, Cov = 0.8f, Sky = 0.85f, Over = 0.55f, Dark = 0.15f, Rays = 0.15f, Shadow = 0.25f, Exp = -0.12f, Sat = -14f, Con = -6f, SunDim = 0.7f, FogD = 0.8f, FogS = 0.9f, FogE = 6.5f, Wind = 0.4f });
            l.Add(new W { File = "08_Gloomy", Title = "Хмуро", Sev = 0.55f, Cov = 0.9f, Sky = 0.95f, Over = 0.8f, Dark = 0.35f, Rays = 0f, Shadow = 0.15f, Exp = -0.25f, Sat = -20f, Con = -4f, Temp = -6f, SunDim = 0.55f, Vig = 0.08f, Wind = 0.5f, Note = "задумано: морось 0.2", Rain = 0.12f });
            l.Add(new W { File = "09_SnowClouds", Title = "Снежные тучи", Sev = 0.55f, Cov = 0.85f, Sky = 0.9f, Over = 0.7f, Dark = 0.1f, Rays = 0.1f, Shadow = 0.2f, Exp = 0.05f, Sat = -22f, Temp = -28f, Con = -8f, SunDim = 0.75f, FogD = 0.9f, FogS = 0.6f, FogE = 5f, Wind = 0.35f, Haz = CatCooksWeatherPreset.Hazard.Cold, Note = "задумано: снег (эффект — потом)" });
            l.Add(new W { File = "10_DustStorm", Title = "Пыльная буря", Sev = 0.6f, Cov = 0.5f, Sky = 0.55f, Over = 0.35f, Dark = 0.05f, Rays = 0.4f, Exp = -0.05f, Sat = -10f, Temp = 24f, Con = 4f, SunDim = 0.7f, FogD = 1f, FogS = 0.25f, FogE = 3f, Wind = 0.85f, Streaks = 0.7f, Evolve = 0.12f, Vig = 0.1f, Haz = CatCooksWeatherPreset.Hazard.Dust | CatCooksWeatherPreset.Hazard.Wind, Note = "задумано: пыль 0.8", Dust = 0.8f });
            l.Add(new W { File = "11_PreStorm", Title = "Перед грозой", Sev = 0.7f, Cov = 0.75f, Sky = 0.8f, Over = 0.4f, Dark = 0.55f, Rays = 1.2f, Shadow = 0.7f, Exp = -0.1f, Sat = -4f, Con = 16f, Temp = 8f, SunDim = 0.9f, Vig = 0.1f, Wind = 0.6f, Evolve = 0.1f, Haz = CatCooksWeatherPreset.Hazard.Wind , Rain = 0.04f});
            l.Add(new W { File = "12_Thunderstorm", Title = "Гроза", Sev = 0.85f, Cov = 0.95f, Sky = 1f, Over = 0.85f, Dark = 0.75f, Rays = 0f, Shadow = 0.2f, Exp = -0.45f, Sat = -24f, Con = 10f, Temp = -10f, SunDim = 0.4f, Vig = 0.18f, Frame = 0.3f, Desat = 0.4f, FogD = 0.85f, FogS = 0.7f, FogE = 5f, Wind = 0.8f, Streaks = 0.5f, Evolve = 0.14f, Haz = CatCooksWeatherPreset.Hazard.Rain | CatCooksWeatherPreset.Hazard.Storm, Note = "задумано: дождь 0.8, молнии 0.6", Rain = 0.75f, Light = 0.6f });
            l.Add(new W { File = "13_Tempest", Title = "Шторм", Sev = 0.95f, Cov = 1f, Sky = 1f, Over = 0.95f, Dark = 0.9f, Rays = 0f, Shadow = 0.1f, Exp = -0.6f, Sat = -30f, Con = 12f, Temp = -14f, SunDim = 0.3f, Vig = 0.25f, Frame = 0.45f, Desat = 0.5f, FogD = 0.9f, FogS = 0.5f, FogE = 4f, Wind = 1f, Streaks = 0.9f, Evolve = 0.22f, Haz = CatCooksWeatherPreset.Hazard.Rain | CatCooksWeatherPreset.Hazard.Storm | CatCooksWeatherPreset.Hazard.Wind, Note = "задумано: ливень 1, молнии 0.9", Rain = 1f, Light = 0.9f });
            l.Add(new W { File = "14_HurricaneWarning", Title = "Ураганное предупреждение", Sev = 1f, Cov = 0.9f, Sky = 0.95f, Over = 0.6f, Dark = 0.6f, Rays = 0.3f, Shadow = 0.5f, Exp = -0.3f, Sat = -8f, Con = 22f, Temp = 12f, SunDim = 0.6f, Vig = 0.3f, Frame = 0.55f, Desat = 0.2f, FrameCol = new Color(0.36f, 0.40f, 0.16f), FogD = 0.75f, FogS = 0.8f, FogE = 5.5f, Wind = 1f, Streaks = 1f, Evolve = 0.3f, Trans = 30f, Haz = CatCooksWeatherPreset.Hazard.Wind | CatCooksWeatherPreset.Hazard.Storm, Note = "жуткое жёлто-зелёное затишье перед ураганом; задумано: порывы, мусор в воздухе" , Rain = 0.4f});
            l.Add(new W { File = "15_AfterStorm", Title = "После грозы", Sev = 0.15f, Cov = 0.4f, Sky = 0.45f, Rays = 2f, Shadow = 0.55f, Exp = 0.08f, Sat = 14f, Con = 8f, Temp = -2f, Bloom = 0.2f, FogD = 0.45f, FogS = 1.8f, FogE = 11f, Wind = 0.25f, Note = "свежо, сочно, длинные лучи; задумано: лужи" });
            l.Add(new W { File = "16_PinkHaze", Title = "Розовая дымка", Sev = 0.1f, Cov = 0.25f, Sky = 0.3f, Rays = 1.2f, Exp = 0.06f, Sat = 6f, Temp = 10f, Bloom = 0.35f, FogD = 0.9f, FogS = 0.7f, FogE = 6f, Wind = 0.1f, Note = "сказочная тёплая дымка (цвет дымки — из стиля, теплее за счёт температуры)" });
            l.Add(new W { File = "17_HighWind", Title = "Сильный ветер", Sev = 0.45f, Cov = 0.45f, Sky = 0.5f, Stretch = 3f, Wisp = 0.5f, Rays = 1f, Con = 6f, Wind = 0.95f, Streaks = 0.9f, Evolve = 0.25f, Haz = CatCooksWeatherPreset.Hazard.Wind, Note = "облака несутся; задумано: листья 0.7", Leaves = 0.7f });
            // ── 18–24: особые события (под трудности для игрока или просто красота) ──
            l.Add(new W { File = "18_FlashFreeze", Title = "Резкие заморозки", Sev = 0.6f, Cov = 0.15f, Sky = 0.2f, Stretch = 2.5f, Wisp = 0.6f, Rays = 1.3f, Shadow = 0.35f, Exp = 0.1f, Sat = -12f, Con = 14f, Temp = -38f, Bloom = 0.3f, SunDim = 0.9f, Vig = 0.12f, Frame = 0.35f, Desat = 0.3f, FrameCol = new Color(0.62f, 0.80f, 0.95f), FogD = 0.6f, FogS = 1.2f, FogE = 8f, Wind = 0.2f, Trans = 25f, Haz = CatCooksWeatherPreset.Hazard.Cold, Diff = 0.7f, Note = "звонкий ледяной день: синий свет, иней по краям экрана, всё блестит; задумано: иней на стёклах, скользкие тротуары, пар изо рта" });
            l.Add(new W { File = "19_Blizzard", Title = "Метель", Sev = 0.9f, Cov = 1f, Sky = 1f, Over = 0.9f, Dark = 0.25f, Stretch = 2f, Rays = 0f, Shadow = 0.05f, Exp = 0.05f, Sat = -35f, Con = -12f, Temp = -30f, SunDim = 0.5f, Vig = 0.2f, Frame = 0.5f, Desat = 0.5f, FrameCol = new Color(0.85f, 0.90f, 0.97f), FogD = 1f, FogS = 0.15f, FogE = 2f, Wind = 1f, Streaks = 1f, Evolve = 0.25f, Haz = CatCooksWeatherPreset.Hazard.Cold | CatCooksWeatherPreset.Hazard.Wind | CatCooksWeatherPreset.Hazard.Fog, Diff = 0.95f, Note = "белая мгла, видно на квартал; задумано: снег стеной, сугробы, замедление" });
            l.Add(new W { File = "20_Hail", Title = "Град", Sev = 0.8f, Cov = 0.95f, Sky = 0.95f, Over = 0.6f, Dark = 0.6f, Rays = 0.3f, Shadow = 0.4f, Exp = -0.25f, Sat = -16f, Con = 14f, Temp = -12f, SunDim = 0.6f, Vig = 0.15f, Frame = 0.3f, FogD = 0.7f, FogS = 0.9f, FogE = 6f, Wind = 0.7f, Streaks = 0.5f, Evolve = 0.18f, Trans = 20f, Haz = CatCooksWeatherPreset.Hazard.Storm | CatCooksWeatherPreset.Hazard.Rain, Diff = 0.8f, Note = "зеленоватые тяжёлые тучи, короткий злой заряд; задумано: градины прыгают по асфальту, стук по крышам машин" , Rain = 0.55f});
            l.Add(new W { File = "21_Smog", Title = "Смог", Sev = 0.5f, Cov = 0.4f, Sky = 0.45f, Over = 0.3f, Dark = 0.1f, Rays = 0.6f, Shadow = 0.2f, Exp = -0.08f, Sat = -18f, Con = -10f, Temp = 20f, Bloom = 0.15f, SunDim = 0.65f, Vig = 0.1f, Frame = 0.2f, FrameCol = new Color(0.45f, 0.38f, 0.22f), FogD = 0.95f, FogS = 0.4f, FogE = 4f, Wind = 0.05f, Haz = CatCooksWeatherPreset.Hazard.Fog | CatCooksWeatherPreset.Hazard.Dust, Diff = 0.5f, Note = "жёлто-бурая городская мгла, солнце — тусклый диск; задумано: кашель, видимость, вывески светятся днём" });
            l.Add(new W { File = "22_FogBank", Title = "Туман-стена", Sev = 0.55f, Cov = 0.3f, Sky = 0.35f, Rays = 0.9f, Shadow = 0.1f, Exp = 0.08f, Sat = -20f, Con = -14f, Temp = -6f, SunDim = 0.75f, FogD = 1f, FogS = 0.05f, FogE = 1.5f, Wind = 0.1f, Evolve = 0.08f, Trans = 35f, Haz = CatCooksWeatherPreset.Hazard.Fog, Diff = 0.6f, Note = "густой туман накатывает с края города, небоскрёбы торчат над ним; задумано: фары и окна в ореолах" });
            l.Add(new W { File = "23_Eclipse", Title = "Затмение", Sev = 0.3f, Cov = 0.05f, Sky = 0.05f, Rays = 0.2f, Shadow = 0.1f, Exp = -0.9f, Sat = -10f, Con = 18f, Temp = -14f, SunDim = 0.12f, Vig = 0.35f, Frame = 0.25f, FrameCol = new Color(0.10f, 0.08f, 0.20f), FogD = 0.5f, FogS = 1.5f, FogE = 10f, Wind = 0.05f, Trans = 30f, Diff = 0.4f, Note = "средь бела дня темнеет: фиолетовые сумерки, загораются окна и фонари; задумано: корона вокруг солнца, птицы садятся" });
            l.Add(new W { File = "24_Rainbow", Title = "Радуга", Sev = 0.05f, Cov = 0.35f, Sky = 0.4f, Rays = 2.2f, Shadow = 0.5f, Exp = 0.1f, Sat = 20f, Con = 8f, Temp = 4f, Bloom = 0.3f, FogD = 0.4f, FogS = 2f, FogE = 12f, Wind = 0.15f, Diff = 0f, Note = "после дождя: умытый сочный город, длинные лучи; задумано: радуга на небе, капли с карнизов, бонус к настроению" });
            // ── 25–26: уютные дожди ──
            l.Add(new W { File = "25_CozyRain", Title = "Уютный дождик", Sev = 0.3f, Cov = 0.85f, Sky = 0.9f, Over = 0.5f, Dark = 0.12f, Rays = 0.1f, Shadow = 0.15f, Exp = -0.05f, Sat = -2f, Con = -4f, Temp = 8f, SunDim = 0.6f, FogD = 0.75f, FogS = 0.9f, FogE = 7f, Wind = 0.12f, Rain = 0.3f, Haz = CatCooksWeatherPreset.Hazard.Rain, Diff = 0.2f, Note = "тихий тёплый дождь, окна светятся, всё мягкое; задумано: лужи, капли с карнизов" });
            l.Add(new W { File = "26_SunShower", Title = "Грибной дождь", Sev = 0.12f, Cov = 0.3f, Sky = 0.35f, Rays = 2f, Shadow = 0.45f, Exp = 0.08f, Sat = 12f, Con = 6f, Temp = 8f, Bloom = 0.3f, FogD = 0.45f, FogS = 1.8f, FogE = 11f, Wind = 0.1f, Rain = 0.35f, Haz = CatCooksWeatherPreset.Hazard.Rain, Diff = 0.05f, Note = "дождь при солнце: капли блестят золотом, длинные лучи; задумано: радуга" });
            // ── 27–29: дожди по силе (вид дождя — CityRain по таблице «погода → вид») ──
            l.Add(new W { File = "27_Drizzle", Title = "Морось", Sev = 0.3f, Cov = 0.85f, Sky = 0.9f, Over = 0.65f, Dark = 0.15f, Rays = 0f, Shadow = 0.1f, Exp = -0.1f, Sat = -12f, Con = -6f, Temp = -4f, SunDim = 0.65f, FogD = 0.85f, FogS = 0.6f, FogE = 5f, Wind = 0.25f, Rain = 0.18f, Haz = CatCooksWeatherPreset.Hazard.Rain | CatCooksWeatherPreset.Hazard.Fog, Diff = 0.25f, Note = "мелкая водяная пыль, всё серое и влажное" });
            l.Add(new W { File = "28_SteadyRain", Title = "Дождь", Sev = 0.55f, Cov = 0.95f, Sky = 1f, Over = 0.8f, Dark = 0.35f, Rays = 0f, Shadow = 0.1f, Exp = -0.22f, Sat = -14f, Con = -2f, Temp = -6f, SunDim = 0.5f, Vig = 0.08f, FogD = 0.8f, FogS = 0.8f, FogE = 6f, Wind = 0.35f, Rain = 0.55f, Haz = CatCooksWeatherPreset.Hazard.Rain, Diff = 0.45f, Note = "ровный дождь, всплески с коронами, окна зажигаются" });
            l.Add(new W { File = "29_Downpour", Title = "Ливень", Sev = 0.75f, Cov = 1f, Sky = 1f, Over = 0.9f, Dark = 0.55f, Rays = 0f, Shadow = 0.05f, Exp = -0.35f, Sat = -18f, Con = 4f, Temp = -8f, SunDim = 0.4f, Vig = 0.14f, Frame = 0.25f, Desat = 0.3f, FogD = 0.9f, FogS = 0.5f, FogE = 4.5f, Wind = 0.55f, Streaks = 0.4f, Evolve = 0.12f, Rain = 0.85f, Haz = CatCooksWeatherPreset.Hazard.Rain, Diff = 0.65f, Note = "стена воды, дальние кварталы тонут в дожде" });
            // ── 30–32: листопады и мусор (эффекты — CityStorm по таблице «погода → …») ──
            l.Add(new W { File = "30_AutumnLeaves", Title = "Листопад (осень)", Sev = 0.25f, Cov = 0.5f, Sky = 0.55f, Over = 0.2f, Dark = 0.05f, Rays = 0.9f, Shadow = 0.4f, Exp = -0.03f, Sat = -6f, Con = 4f, Temp = -10f, SunDim = 0.85f, Desat = 0.25f, FogD = 0.7f, FogS = 1.1f, FogE = 8f, Wind = 0.5f, Streaks = 0.3f, Evolve = 0.08f, Leaves = 1f, Diff = 0.15f, Note = "холодная осень: жёлтые, оранжевые, бурые листья падают и лежат на полу" });
            l.Add(new W { File = "31_SummerLeaves", Title = "Летний листопад", Sev = 0.1f, Cov = 0.25f, Sky = 0.3f, Rays = 1.6f, Shadow = 0.5f, Exp = 0.08f, Sat = 10f, Con = 5f, Temp = 6f, Bloom = 0.2f, FogD = 0.45f, FogS = 1.8f, FogE = 11f, Wind = 0.4f, Streaks = 0.25f, Evolve = 0.06f, Leaves = 0.8f, Diff = 0.05f, Note = "светлое лето: зелёные листики кружат в воздухе" });
            l.Add(new W { File = "32_TrashStorm", Title = "Тонна мусора", Sev = 0.55f, Cov = 0.45f, Sky = 0.5f, Stretch = 3f, Wisp = 0.5f, Rays = 1f, Shadow = 0.4f, Con = 6f, Wind = 0.9f, Streaks = 0.9f, Evolve = 0.25f, Haz = CatCooksWeatherPreset.Hazard.Wind, Diff = 0.5f, Note = "со всех сторон летят пакеты, газеты, стаканчики, картонки, зонтики; есть галка «поверх любой погоды» в меню P" });
            l.Add(new W { File = "33_Snowfall", Title = "Снегопад", Sev = 0.35f, Cov = 0.9f, Sky = 0.95f, Over = 0.55f, Dark = 0.1f, Rays = 0.1f, Shadow = 0.1f, Exp = 0.08f, Sat = -14f, Con = -6f, Temp = -12f, SunDim = 0.65f, Desat = 0.25f, FogD = 0.8f, FogS = 0.8f, FogE = 6f, Wind = 0.2f, Evolve = 0.06f, Haz = CatCooksWeatherPreset.Hazard.Cold, Diff = 0.3f, Note = "тихий снегопад: крупные хлопья, пол засыпает, следы, пар изо рта" });
            // ── 34–35: град в бурю ──
            l.Add(new W { File = "34_HailStorm", Title = "Буря с градом", Sev = 0.85f, Cov = 0.9f, Sky = 0.95f, Over = 0.55f, Dark = 0.55f, Stretch = 2.5f, Rays = 0.4f, Shadow = 0.45f, Exp = -0.2f, Sat = -12f, Con = 16f, Temp = -10f, SunDim = 0.6f, Vig = 0.18f, Frame = 0.35f, Desat = 0.3f, FrameCol = new Color(0.30f, 0.38f, 0.34f), FogD = 0.75f, FogS = 0.8f, FogE = 5.5f, Wind = 0.95f, Streaks = 0.8f, Evolve = 0.25f, Trans = 20f, Haz = CatCooksWeatherPreset.Hazard.Storm | CatCooksWeatherPreset.Hazard.Wind, Diff = 0.85f, Note = "сухая буря: ветер рвёт, зарницы, крупный град скачет по полу и крышам, без дождя" });
            l.Add(new W { File = "35_HailThunder", Title = "Гроза с градом", Sev = 0.95f, Cov = 1f, Sky = 1f, Over = 0.9f, Dark = 0.8f, Rays = 0f, Shadow = 0.1f, Exp = -0.5f, Sat = -24f, Con = 12f, Temp = -12f, SunDim = 0.35f, Vig = 0.22f, Frame = 0.4f, Desat = 0.45f, FogD = 0.9f, FogS = 0.55f, FogE = 4.5f, Wind = 0.85f, Streaks = 0.6f, Evolve = 0.2f, Trans = 20f, Rain = 0.8f, Haz = CatCooksWeatherPreset.Hazard.Rain | CatCooksWeatherPreset.Hazard.Storm, Diff = 0.95f, Note = "ливень, молнии и град разом: градины прыгают в лужах" });
            // ── 36: пекло ──
            l.Add(new W { File = "36_Scorcher", Title = "Пекло", Sev = 0.75f, Cov = 0.03f, Sky = 0.05f, Rays = 0.4f, Shadow = 0.1f, Exp = 0.22f, Sat = 18f, Con = 10f, Temp = 32f, Bloom = 0.5f, SunDim = 1f, Vig = 0.12f, Frame = 0.3f, Desat = 0.1f, FrameCol = new Color(0.85f, 0.45f, 0.15f), FogD = 0.9f, FogS = 0.7f, FogE = 5.5f, Wind = 0.03f, Trans = 30f, Haz = CatCooksWeatherPreset.Hazard.Heat, Diff = 0.75f, Note = "зной на максимум: всё дрожит, миражи на асфальте, солнце слепит, оранжевые края экрана" });
            // ── 37–39: туманы в игровой зоне (холодная палитра) ──
            l.Add(new W { File = "37_GroundFog", Title = "Низкий туман", Sev = 0.35f, Cov = 0.55f, Sky = 0.6f, Over = 0.35f, Dark = 0.05f, Rays = 0.7f, Shadow = 0.2f, Exp = 0.02f, Sat = -18f, Con = -8f, Temp = -14f, SunDim = 0.75f, Desat = 0.2f, FrameCol = new Color(0.70f, 0.80f, 0.90f), FogD = 0.75f, FogS = 0.9f, FogE = 7f, Wind = 0.08f, Evolve = 0.05f, Trans = 35f, Haz = CatCooksWeatherPreset.Hazard.Fog, Diff = 0.45f, Note = "холодный туман стелется по полу до половины роста: под ногами ничего не видно" });
            l.Add(new W { File = "38_FogIsland", Title = "Туман вокруг", Sev = 0.45f, Cov = 0.7f, Sky = 0.75f, Over = 0.5f, Dark = 0.08f, Rays = 0.4f, Shadow = 0.15f, Exp = 0.04f, Sat = -22f, Con = -10f, Temp = -16f, SunDim = 0.65f, Vig = 0.1f, Frame = 0.2f, Desat = 0.3f, FrameCol = new Color(0.72f, 0.80f, 0.88f), FogD = 0.95f, FogS = 0.3f, FogE = 3f, Wind = 0.1f, Evolve = 0.05f, Trans = 35f, Haz = CatCooksWeatherPreset.Hazard.Fog, Diff = 0.55f, Note = "за краем этажа всё тонет в молоке: этаж как остров" });
            l.Add(new W { File = "39_DenseFog", Title = "Густой туман", Sev = 0.6f, Cov = 0.85f, Sky = 0.9f, Over = 0.7f, Dark = 0.1f, Rays = 0.2f, Shadow = 0.08f, Exp = 0.05f, Sat = -26f, Con = -14f, Temp = -18f, SunDim = 0.55f, Vig = 0.14f, Frame = 0.3f, Desat = 0.4f, FrameCol = new Color(0.74f, 0.82f, 0.90f), FogD = 1f, FogS = 0.1f, FogE = 1.5f, Wind = 0.06f, Evolve = 0.04f, Trans = 35f, Haz = CatCooksWeatherPreset.Hazard.Fog, Diff = 0.7f, Note = "туман везде, видно на несколько метров, внутри плывут клубы" });
            return l;
        }

        /// <summary>Погода ↔ стиль: компонент CityWeatherLink на пульт (стиль идёт за погодой, переходы медленные).</summary>
        [MenuItem("CatCooks/Застройка/Погода ↔ стиль: связать (медленные переходы)", priority = 941)]
        public static void Link()
        {
            CatCooksMoodDirector d = Object.FindFirstObjectByType<CatCooksMoodDirector>();
            if (d == null) { Debug.LogWarning("[Погода↔стиль] Mood Director в сцене не найден"); return; }
            CityWeatherLink l = d.GetComponent<CityWeatherLink>();
            if (l == null) l = Undo.AddComponent<CityWeatherLink>(d.gameObject);
            l.Mood = d;
            l.MergeDefaults();
            EditorUtility.SetDirty(l);
            int miss = 0;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (CatCooksWeatherPreset w in d.weathers)
            {
                if (w == null) continue;
                int si = l.StyleFor(w);
                if (si < 0) miss++;
                sb.Append("\n  ").Append(w.title).Append(" → ").Append(si >= 0 ? d.styles[si].title : "(стиль не найден — останется текущий)");
            }
            Debug.Log("[Погода↔стиль] связано (переходы: погода " + l.WeatherSeconds + " с, стиль " + l.StyleSeconds + " с)" + (miss > 0 ? ", без стиля: " + miss : "") + sb);
        }

        [MenuItem("CatCooks/Застройка/Погоды: собрать лестницу от ясного до урагана", priority = 940)]
        public static void Build()
        {
            CatCooksMoodDirector d = Object.FindFirstObjectByType<CatCooksMoodDirector>();
            CityBlockGenerator g = Object.FindFirstObjectByType<CityBlockGenerator>();
            float S = g != null ? Mathf.Min(g.ModuleSize().x, g.ModuleSize().y) : 250f;
            MakeFolder(Folder);
            List<CatCooksWeatherPreset> made = new List<CatCooksWeatherPreset>();
            foreach (W w in Ladder())
            {
                string path = Folder + "/CW_" + w.File + ".asset";
                CatCooksWeatherPreset p = AssetDatabase.LoadAssetAtPath<CatCooksWeatherPreset>(path);
                if (p == null) { p = ScriptableObject.CreateInstance<CatCooksWeatherPreset>(); AssetDatabase.CreateAsset(p, path); }
                Fill(p, w, S);
                EditorUtility.SetDirty(p);
                made.Add(p);
            }
            AssetDatabase.SaveAssets();
            if (d != null)
            {
                Undo.RecordObject(d, "Погоды");
                d.weathers.Clear();
                d.weathers.AddRange(made);
                d.SetWeather(3, 0f);
                d.Apply();
                EditorUtility.SetDirty(d);
                Link();
            }
            FixGreenHaze();
            Debug.Log($"[Погоды] собрано {made.Count}: от «Ясно» до «Ураганного предупреждения» + жара, утренняя дымка, снежные тучи, пыльная буря, " +
                      "после грозы, розовая дымка, сильный ветер, резкие заморозки, метель, град, смог, туман-стена, затмение, радуга, уютный дождик, грибной дождь, морось, дождь, ливень, листопады, мусор, снегопад, буря с градом, гроза с градом. " + (d != null ? "В Mood Director список погод заменён на них (старые файлы на месте), выбрано «Кучевые»." : "Mood Director не найден — добавь пресеты из " + Folder + " руками.") +
                      " Эффекты (капли, молнии, листья, пыль) — 0, задуманное записано в заметке пресета.");
        }

        private static void Fill(CatCooksWeatherPreset p, W w, float S)
        {
            p.title = w.Title;
            p.note = string.IsNullOrEmpty(w.Note) ? "" : w.Note;
            p.severity = w.Sev;
            p.coverage = w.Cov; p.skyCoverage = w.Sky; p.darkness = w.Dark; p.overcast = w.Over;
            p.cloudStretch = w.Stretch; p.cloudWisp = w.Wisp; p.cloudWidth = w.Width; p.cloudBodyScale = w.Body; p.cloudPopulation = w.Pop;
            p.hazeEnabled = false;
            p.fogEnabled = w.Fog; p.fogDensity = w.FogD; p.fogStart = w.FogS * S; p.fogEnd = w.FogE * S; p.fogHeightTop = 100f * S;
            p.rayIntensity = w.Rays;
            p.cloudShadows = w.Shadow > 0.01f; p.shadowStrength = w.Shadow;
            p.exposureShift = w.Exp; p.saturationShift = w.Sat; p.contrastShift = w.Con; p.temperatureShift = w.Temp;
            p.vignetteAdd = w.Vig; p.bloomAdd = w.Bloom; p.sunDim = w.SunDim;
            p.screenFrame = w.Frame; p.frameColor = w.FrameCol; p.edgeDesaturate = w.Desat;
            p.wind = w.Wind; p.windStreaks = w.Streaks; p.cloudEvolve = w.Evolve;
            p.transitionSeconds = w.Trans;
            p.hazards = w.Haz;
            p.difficulty = w.Diff;
            // эффекты пока выключены (задумано — в заметке)
            p.rain = w.Rain; p.lightning = 0f; p.leaves = 0f; p.dust = 0f; p.sunDust = w.Sev < 0.4f ? 0.4f : 0f;
        }

        /// <summary>Только новые погоды (град и т.п.): создаёт недостающие пресеты и дописывает их в Mood Director, старые не трогает.</summary>
        [MenuItem("CatCooks/Застройка/Погоды: добавить новые (не трогая старые)", priority = 942)]
        public static void AddMissing()
        {
            CatCooksMoodDirector d = Object.FindFirstObjectByType<CatCooksMoodDirector>();
            CityBlockGenerator g = Object.FindFirstObjectByType<CityBlockGenerator>();
            float S = g != null ? Mathf.Min(g.ModuleSize().x, g.ModuleSize().y) : 250f;
            MakeFolder(Folder);
            int made = 0, added = 0;
            if (d != null) Undo.RecordObject(d, "Погоды");
            foreach (W w in Ladder())
            {
                string path = Folder + "/CW_" + w.File + ".asset";
                CatCooksWeatherPreset p = AssetDatabase.LoadAssetAtPath<CatCooksWeatherPreset>(path);
                if (p == null)
                {
                    p = ScriptableObject.CreateInstance<CatCooksWeatherPreset>();
                    AssetDatabase.CreateAsset(p, path);
                    Fill(p, w, S);
                    EditorUtility.SetDirty(p);
                    made++;
                    Debug.Log("[Погоды] новая погода: " + w.Title + " (" + path + ")", p);
                }
                if (d != null && !d.weathers.Contains(p)) { d.weathers.Add(p); added++; }
            }
            AssetDatabase.SaveAssets();
            if (d != null) { EditorUtility.SetDirty(d); if (added > 0) Link(); }
            Debug.Log("[Погоды] создано новых пресетов: " + made + ", добавлено в Mood Director: " + added + (d == null ? " (Mood Director не найден)" : ""));
        }

        /// <summary>Дымка без зелени: если в палитре дымка зеленее неба и тёплого — заменяем на смесь горизонта и тёплой дымки.</summary>
        [MenuItem("CatCooks/Застройка/Погоды: исправить зелёную дымку в стилях", priority = 941)]
        public static void FixGreenHaze()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:CatCooksPalette"))
            {
                CatCooksPalette p = AssetDatabase.LoadAssetAtPath<CatCooksPalette>(AssetDatabase.GUIDToAssetPath(guid));
                if (p == null) continue;
                bool ch = false;
                if (Greenish(p.fogFar)) { Undo.RecordObject(p, "Дымка"); p.fogFar = Ungreen(p.fogFar, p.horizon); ch = true; }
                if (Greenish(p.fogNear)) { Undo.RecordObject(p, "Дымка"); p.fogNear = Ungreen(p.fogNear, p.horizon); ch = true; }
                if (ch) { EditorUtility.SetDirty(p); n++; Debug.Log("[Погоды] дымка без зелени: " + p.name, p); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Погоды] исправлено палитр с зелёной дымкой: {n}");
        }

        private static bool Greenish(Color c)
        {
            return c.g > c.r + 0.03f && c.g > c.b + 0.01f;
        }

        private static Color Ungreen(Color c, Color horizon)
        {
            float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            Color warm = new Color(l * 1.06f, l * 0.97f, l * 0.94f);
            Color h = Greenish(horizon) ? warm : horizon;
            Color r = Color.Lerp(warm, h, 0.4f);
            r.a = 1f;
            return r;
        }

        private static void MakeFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
