using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// СУТКИ, НЕБО, ОБЛАКА — инструменты поверх Atmosphere (Mood Director, Day Cycle, Sky Controller).
    ///  • «Сутки: собрать» — оставляет в пульте только нужные стили, из остальных палитр собирает
    ///    утро, 3 заката (оранжевый, розовый, янтарный) и 3 ночи (синяя, с яркой луной, неоновая);
    ///    Day Cycle каждый игровой день берёт свой закат и свою ночь.
    ///  • «Небо: мультяшные облака» — материал скайбокса на шейдер CatCooks/City/Sky Toon (те же параметры).
    ///  • Отдельные облака (Clouds) убраны — будут новые; чистка папки Atmosphere — CityAtmosphereCleanup.
    /// </summary>
    public static class CityDayTool
    {
        public const string Folder = "Assets/Generated/City/Day";
        private static readonly string[] Keep = { "candy закат", "candy утро", "северный", "янтарь", "грозовой фронт", "неоновая ночь", "комикс — тёплый", "cityref" };

        [MenuItem("CatCooks/Застройка/Сутки: собрать утро, закаты и ночи", priority = 930)]
        public static void BuildDay()
        {
            CatCooksMoodDirector d = Object.FindFirstObjectByType<CatCooksMoodDirector>();
            CatCooksDayCycle dc = Object.FindFirstObjectByType<CatCooksDayCycle>();
            if (d == null) { EditorUtility.DisplayDialog("Сутки", "Нет Mood Director в сцене.", "Ок"); return; }
            MakeFolder(Folder);
            Undo.RecordObject(d, "Сутки");

            // все стили проекта (и те, что уже убраны из пульта)
            List<CatCooksSkyStyle> all = new List<CatCooksSkyStyle>();
            foreach (string g in AssetDatabase.FindAssets("t:CatCooksSkyStyle"))
            {
                CatCooksSkyStyle st = AssetDatabase.LoadAssetAtPath<CatCooksSkyStyle>(AssetDatabase.GUIDToAssetPath(g));
                if (st != null && !all.Contains(st)) all.Add(st);
            }
            foreach (CatCooksSkyStyle st in d.styles) if (st != null && !all.Contains(st)) all.Add(st);

            CatCooksSkyStyle morning = Find(all, "candy утро"), dusk = Find(all, "candy закат"), amber = Find(all, "янтарь"),
                             neon = Find(all, "неоновая ночь"), warm = Find(all, "комикс — тёплый"), cityRef = Find(all, "cityref");

            // 1) пульт: только нужные стили, остальные убираем из списка (ассеты не удаляются)
            List<string> removed = new List<string>();
            CatCooksSkyStyle current = d.styles.Count > 0 ? d.styles[Mathf.Clamp(d.styleIndex, 0, d.styles.Count - 1)] : null;
            for (int i = d.styles.Count - 1; i >= 0; i--)
                if (d.styles[i] == null || !Kept(d.styles[i])) { removed.Add(d.styles[i] != null ? d.styles[i].title : "пусто"); d.styles.RemoveAt(i); }
            foreach (CatCooksSkyStyle st in all) if (Kept(st) && !d.styles.Contains(st)) d.styles.Add(st);
            CatCooksSkyStyle day = cityRef != null ? cityRef : warm;
            int di = day != null ? d.styles.IndexOf(day) : d.styles.IndexOf(current);
            if (di < 0) di = 0;
            d.SetStyle(di, 0f);
            EditorUtility.SetDirty(d);

            // 2) палитры суток
            CatCooksPalette pMorning = Copy(morning != null ? morning.palette : null, "Dawn_Morning");
            CatCooksPalette pOrange = Copy(dusk != null ? dusk.palette : null, "Dusk_Orange");
            CatCooksPalette pAmber = Copy(amber != null ? amber.palette : null, "Dusk_Amber");
            CatCooksPalette pPink = Copy(pOrange, "Dusk_Pink");
            Tint(pPink, new Color(0.98f, 0.70f, 0.72f), new Color(0.45f, 0.42f, 0.72f), new Color(1f, 0.62f, 0.58f), new Color(1f, 0.72f, 0.66f),
                 new Color(1f, 0.80f, 0.82f), new Color(0.60f, 0.46f, 0.64f), new Color(0.98f, 0.80f, 0.80f), new Color(0.90f, 0.70f, 0.76f),
                 new Color(0.70f, 0.60f, 0.78f), new Color(0.82f, 0.62f, 0.66f), new Color(0.50f, 0.38f, 0.44f));
            CatCooksPalette nightSrc = FindPalette("SJ_P_Night") ?? FindPalette("CX_P_Night_Blue");
            CatCooksPalette pBlue = Copy(nightSrc, "Night_Blue");
            CatCooksPalette pMoon = Copy(pBlue, "Night_Moon");
            Tint(pMoon, new Color(0.30f, 0.40f, 0.62f), new Color(0.12f, 0.20f, 0.42f), new Color(0.88f, 0.92f, 1f), new Color(0.62f, 0.72f, 1f),
                 new Color(0.78f, 0.84f, 0.98f), new Color(0.24f, 0.29f, 0.45f), new Color(0.32f, 0.38f, 0.55f), new Color(0.27f, 0.33f, 0.50f),
                 new Color(0.34f, 0.42f, 0.64f), new Color(0.26f, 0.30f, 0.45f), new Color(0.12f, 0.13f, 0.20f));
            CatCooksPalette pNeon = Copy(neon != null ? neon.palette : null, "Night_Neon");
            AssetDatabase.SaveAssets();

            string dayInfo = "Day Cycle не найден — палитры созданы в " + Folder + ", назначь руками";
            if (dc != null)
            {
                Undo.RecordObject(dc, "Сутки");
                dc.dawn = pMorning;
                dc.dawnVariants = new[] { pMorning };
                dc.dusk = pOrange;
                dc.duskVariants = new[] { pOrange, pPink, pAmber };
                dc.night = pBlue;
                dc.nightVariants = new[] { pBlue, pMoon, pNeon };
                dc.nightVariantMoon = new[] { 0.25f, 0.6f, 0.12f };
                EditorUtility.SetDirty(dc);
                dayInfo = "Day Cycle: утро — Candy утро; закаты — оранжевый / розовый / янтарный; ночи — синяя / яркая луна / неоновая (каждый игровой день свои)";
            }
            d.Apply();
            Debug.Log($"[Сутки] днём — «{(day != null ? day.title : "текущий")}». {dayInfo}. Убраны из пульта ({removed.Count}): {string.Join(", ", removed.ToArray())}. " +
                      "Ассеты стилей не удалены — вернуть можно, добавив в список Styles у Mood Director", d);
        }

        private static bool Kept(CatCooksSkyStyle st)
        {
            string t = ((st.title ?? "") + " " + st.name).ToLowerInvariant();
            foreach (string k in Keep) if (t.Contains(k)) return true;
            return false;
        }

        private static CatCooksSkyStyle Find(List<CatCooksSkyStyle> all, string key)
        {
            foreach (CatCooksSkyStyle st in all) if (st != null && ((st.title ?? "") + " " + st.name).ToLowerInvariant().Contains(key)) return st;
            return null;
        }

        private static CatCooksPalette FindPalette(string name)
        {
            foreach (string g in AssetDatabase.FindAssets(name + " t:CatCooksPalette"))
            {
                CatCooksPalette p = AssetDatabase.LoadAssetAtPath<CatCooksPalette>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null && p.name == name) return p;
            }
            return null;
        }

        /// <summary>Копия палитры в папку суток (если уже есть — обновляем из источника).</summary>
        private static CatCooksPalette Copy(CatCooksPalette src, string name)
        {
            string path = Folder + "/" + name + ".asset";
            CatCooksPalette p = AssetDatabase.LoadAssetAtPath<CatCooksPalette>(path);
            if (p == null)
            {
                p = src != null ? Object.Instantiate(src) : ScriptableObject.CreateInstance<CatCooksPalette>();
                AssetDatabase.CreateAsset(p, path);
            }
            else if (src != null && src != p) EditorUtility.CopySerialized(src, p);
            EditorUtility.SetDirty(p);
            return p;
        }

        private static void Tint(CatCooksPalette p, Color horizon, Color zenith, Color sunDisc, Color sunLight, Color cloudLit, Color cloudShadow,
                                 Color fogNear, Color fogFar, Color ambSky, Color ambEq, Color ambGround)
        {
            p.horizon = horizon; p.zenith = zenith; p.sunDisc = sunDisc; p.sunLight = sunLight;
            p.cloudLit = cloudLit; p.cloudShadow = cloudShadow; p.fogNear = fogNear; p.fogFar = fogFar;
            p.ambientSky = ambSky; p.ambientEquator = ambEq; p.ambientGround = ambGround;
            p.rayTint = Color.Lerp(sunLight, Color.white, 0.3f);
            EditorUtility.SetDirty(p);
        }

        // ───────────────────────── небо ─────────────────────────

        [MenuItem("CatCooks/Застройка/Небо: мультяшные облака (шейдер Sky Toon)", priority = 931)]
        public static void SkyToon()
        {
            SkyController sky = Object.FindFirstObjectByType<SkyController>();
            Material m = sky != null ? sky.skyMaterial : RenderSettings.skybox;
            Shader sh = Shader.Find("CatCooks/City/Sky Toon");
            if (m == null || sh == null) { Debug.LogWarning("[Небо] нет материала неба или шейдера CatCooks/City/Sky Toon"); return; }
            string key = "CatCooks.SkyToon.Prev." + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m));
            if (m.shader != sh) EditorPrefs.SetString(key, m.shader.name);
            Undo.RecordObject(m, "Небо");
            m.shader = sh;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Небо] «{m.name}» на шейдере Sky Toon: кучевые комки с плоским тёмным дном вместо разводов. " +
                      "Крутить: Комки — размер / шевеление, Тёмное дно, Крупные формы, Coverage. Вернуть — меню «Небо: вернуть прежний шейдер»", m);
        }

        [MenuItem("CatCooks/Застройка/Небо: вернуть прежний шейдер", priority = 932)]
        public static void SkyRevert()
        {
            SkyController sky = Object.FindFirstObjectByType<SkyController>();
            Material m = sky != null ? sky.skyMaterial : RenderSettings.skybox;
            if (m == null) return;
            string key = "CatCooks.SkyToon.Prev." + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m));
            Shader prev = Shader.Find(EditorPrefs.GetString(key));
            if (prev == null) prev = Shader.Find("CatCooks/CityV2/Art Sky");
            if (prev == null) return;
            Undo.RecordObject(m, "Небо");
            m.shader = prev;
            EditorUtility.SetDirty(m);
            Debug.Log("[Небо] вернул шейдер «" + prev.name + "»", m);
        }

        // ───────────────────────── облака ─────────────────────────

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
