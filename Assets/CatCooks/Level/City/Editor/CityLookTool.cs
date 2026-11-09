using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// СТИЛЬ КАК В РЕФЕРЕНСЕ — одной кнопкой, без перегенерации (дома и пропсы остаются на местах):
    ///  1) материалы пропсов и домов, которые ещё не на шейдере города, получают «близнеца» на CatCooks/City/Building
    ///     (тот же атлас) — тёплый свет, тень у земли, мягкие цвета;
    ///  2) все материалы города (дома, стёкла, дальние пояса) — мягкие приглушённые значения: ниже насыщенность,
    ///     сиреневатые тени, тёплый свет, стекло темнее бирюзовое, тень у земли (резервная копия — «Вернуть как было»);
    ///  3) атмосфера (твой Mood Director): стиль и погода «CityRef» — тёплая розово-бежевая дальняя дымка
    ///     (только по расстоянию, у земли тумана нет), цветокоррекция: насыщенность −15, теплее, мягче контраст, блум.
    /// </summary>
    public static class CityLookTool
    {
        public const string Folder = "Assets/Generated/City/Look";

        /// <summary>Набор значений стиля. Juicy — тёплый сочный (как в референсе: солнце, тёплые тени, дымка только вдали). Soft — приглушённый.</summary>
        public sealed class Look
        {
            public string Name;
            public float Sat, Warmth, Ambient, RampSoft, Rim, BaseShade, TopLight;
            public Color Light, Shadow, SkyAmb, GroundAmb, WinGlass, WinSky;
            public Color GlassTop, GlassBottom, GlassSky; public float GlassBright, GlassStripe;
            public float FarSat, FarContrast;
            public Color FogNear, FogFar; public float FogStart, FogEnd, FogDensity, HorizonMix;
            public Color SunLight, AmbSky, AmbEq, AmbGround;
            public float PostSat, PostContrast, PostTemp, PostBloom, PostVignette, PostExposure, AmbBoost;
        }

        public static readonly Look Juicy = new Look
        {
            Name = "тёплый сочный",
            Sat = 1.08f, Warmth = 0.16f, Ambient = 0.5f, RampSoft = 0.16f, Rim = 0.2f, BaseShade = 0.22f, TopLight = 0.12f,
            Light = new Color(1f, 0.90f, 0.74f), Shadow = new Color(0.66f, 0.46f, 0.50f),
            SkyAmb = new Color(0.72f, 0.80f, 0.95f), GroundAmb = new Color(0.86f, 0.62f, 0.50f),
            WinGlass = new Color(0.55f, 0.82f, 0.86f), WinSky = new Color(0.68f, 0.90f, 0.98f),
            GlassTop = new Color(0.74f, 0.94f, 0.98f), GlassBottom = new Color(0.36f, 0.68f, 0.76f), GlassSky = new Color(0.60f, 0.86f, 1f),
            GlassBright = 1.05f, GlassStripe = 0.5f,
            FarSat = 0.95f, FarContrast = 0.85f,
            FogNear = new Color(0.98f, 0.88f, 0.78f), FogFar = new Color(0.94f, 0.80f, 0.72f), FogStart = 1.2f, FogEnd = 8f, FogDensity = 0.7f, HorizonMix = 0.2f,
            SunLight = new Color(1f, 0.88f, 0.70f), AmbSky = new Color(0.66f, 0.74f, 0.88f), AmbEq = new Color(0.80f, 0.66f, 0.58f), AmbGround = new Color(0.62f, 0.48f, 0.40f),
            PostSat = 8f, PostContrast = 10f, PostTemp = 12f, PostBloom = 0.35f, PostVignette = 0.12f, PostExposure = 0f, AmbBoost = 1f
        };

        public static readonly Look Soft = new Look
        {
            Name = "мягкий приглушённый",
            Sat = 0.92f, Warmth = 0.1f, Ambient = 0.6f, RampSoft = 0.24f, Rim = 0.12f, BaseShade = 0.28f, TopLight = 0.1f,
            Light = new Color(1f, 0.92f, 0.80f), Shadow = new Color(0.60f, 0.50f, 0.58f),
            SkyAmb = new Color(0.70f, 0.76f, 0.90f), GroundAmb = new Color(0.80f, 0.62f, 0.55f),
            WinGlass = new Color(0.48f, 0.72f, 0.76f), WinSky = new Color(0.64f, 0.84f, 0.92f),
            GlassTop = new Color(0.68f, 0.88f, 0.92f), GlassBottom = new Color(0.32f, 0.58f, 0.64f), GlassSky = new Color(0.56f, 0.80f, 0.92f),
            GlassBright = 0.98f, GlassStripe = 0.45f,
            FarSat = 0.85f, FarContrast = 0.8f,
            FogNear = new Color(0.95f, 0.88f, 0.82f), FogFar = new Color(0.90f, 0.81f, 0.77f), FogStart = 0.8f, FogEnd = 6f, FogDensity = 0.75f, HorizonMix = 0.3f,
            SunLight = new Color(1f, 0.92f, 0.80f), AmbSky = new Color(0.64f, 0.70f, 0.84f), AmbEq = new Color(0.74f, 0.66f, 0.62f), AmbGround = new Color(0.58f, 0.48f, 0.44f),
            PostSat = -5f, PostContrast = 0f, PostTemp = 8f, PostBloom = 0.35f, PostVignette = 0.12f, PostExposure = 0.05f, AmbBoost = 1.05f
        };
        private const string Backup = Folder + "/Backup";

        [MenuItem("CatCooks/Застройка/Стиль: тёплый сочный (как в референсе)", priority = 920)]
        private static void Menu() { MenuWith(Juicy); }

        [MenuItem("CatCooks/Застройка/Стиль: мягкий приглушённый", priority = 922)]
        private static void MenuSoft() { MenuWith(Soft); }

        private static void MenuWith(Look look)
        {
            CityBlockGenerator g = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<CityBlockGenerator>() : null;
            if (g == null) g = Object.FindFirstObjectByType<CityBlockGenerator>();
            if (g == null) { EditorUtility.DisplayDialog("Стиль", "Нет генератора застройки (City) в сцене.", "Ок"); return; }
            Apply(g, look);
        }

        [MenuItem("CatCooks/Застройка/Стиль: вернуть материалы как было", priority = 921)]
        private static void MenuRestore() { Restore(); }

        public static void Apply(CityBlockGenerator g) { Apply(g, Juicy); }

        public static void Apply(CityBlockGenerator g, Look L)
        {
            MakeFolder(Folder); MakeFolder(Backup); MakeFolder(Folder + "/Twins");
            int twins = ToCityShader(g);
            int mats = Materials(L);
            string atm = Atmosphere(g, L);
            CityBlockGeneratorEditor.EnsureNightBridge(g);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Застройка/стиль] «{L.Name}» — готово без перегенерации: переведено на шейдер города материалов {twins}, " +
                      $"настроено материалов города {mats}. Атмосфера: {atm}. Вернуть материалы — меню «Стиль: вернуть материалы как было»", g);
        }

        // ───────────────────────── 1. на шейдер города ─────────────────────────

        private static int ToCityShader(CityBlockGenerator g)
        {
            List<GameObject> models = new List<GameObject>();
            CityProps cp = g.GetComponent<CityProps>();
            if (cp != null) foreach (PropEntry e in cp.Props) if (e != null && e.Prefab != null && !models.Contains(e.Prefab)) models.Add(e.Prefab);
            foreach (CityLayer l in new[] { g.Layer1, g.Layer2 })
                foreach (BuildingEntry e in l.Buildings) if (e != null && e.Prefab != null && !models.Contains(e.Prefab)) models.Add(e.Prefab);
            Shader city = Shader.Find("CatCooks/City/Building");
            if (city == null) { Debug.LogWarning("[Застройка/стиль] нет шейдера CatCooks/City/Building"); return 0; }
            int n = 0;
            foreach (GameObject m in models)
            {
                string path = AssetDatabase.GetAssetPath(m);
                ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                Dictionary<AssetImporter.SourceAssetIdentifier, Object> ext = mi.GetExternalObjectMap();
                Dictionary<string, Material> eff = new Dictionary<string, Material>();
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path)) { Material em = o as Material; if (em != null && !eff.ContainsKey(em.name)) eff[em.name] = em; }
                foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, Object> kv in ext)
                    if (kv.Key.type == typeof(Material)) eff[kv.Key.name] = kv.Value as Material;
                bool changed = false;
                foreach (KeyValuePair<string, Material> kv in eff)
                {
                    Material src = kv.Value;
                    if (src == null || src.shader == null) continue;
                    string sn = src.shader.name;
                    if (sn.StartsWith("CatCooks/City/")) continue;                       // уже наш (дома, стекло, тун)
                    string low = kv.Key.ToLowerInvariant();
                    if (low.Contains("glass") || low.Contains("стекл") || low.Contains("uv_grid")) continue;
                    Texture tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : (src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null);
                    if (tex == null) continue;                                           // белые — дело кнопки «Атласы»
                    Material twin = Twin(src, tex, city);
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), twin);
                    changed = true; n++;
                }
                if (changed) { EditorUtility.SetDirty(mi); mi.SaveAndReimport(); }
            }
            // материал стен у домов в списке — тоже на шейдер города
            foreach (CityLayer l in new[] { g.Layer1, g.Layer2 })
                foreach (BuildingEntry e in l.Buildings)
                {
                    if (e == null || e.Material == null || e.Material.shader == null || e.Material.shader.name.StartsWith("CatCooks/City/")) continue;
                    Texture tex = e.Material.HasProperty("_BaseMap") ? e.Material.GetTexture("_BaseMap") : (e.Material.HasProperty("_MainTex") ? e.Material.GetTexture("_MainTex") : null);
                    if (tex == null) continue;
                    Undo.RecordObject(g, "Стиль");
                    e.Material = Twin(e.Material, tex, city);
                    n++;
                }
            // материалы, проставленные на расставленные дома/пропсы (поля RoofMaterial/StreetMaterial, материал стен) —
            // меняем прямо в сцене на близнецов, ничего не пересобирая
            if (cp != null)
            {
                Undo.RecordObject(cp, "Стиль");
                cp.RoofMaterial = CityTwinOrSame(cp.RoofMaterial, city);
                cp.StreetMaterial = CityTwinOrSame(cp.StreetMaterial, city);
                EditorUtility.SetDirty(cp);
            }
            Transform cont = g.Container();
            if (cont != null)
                foreach (Renderer r in cont.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] ms = r.sharedMaterials;
                    bool ch = false;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        Material t = CityTwinOrSame(ms[i], city);
                        if (t != ms[i]) { ms[i] = t; ch = true; }
                    }
                    if (ch) { Undo.RecordObject(r, "Стиль"); r.sharedMaterials = ms; n++; }
                }
            EditorUtility.SetDirty(g);
            return n;
        }

        /// <summary>Текстурный материал не на шейдере города → близнец; стекло, наши шейдеры и белые — как есть.</summary>
        private static Material CityTwinOrSame(Material m, Shader city)
        {
            if (m == null || m.shader == null || m.shader.name.StartsWith("CatCooks/City/")) return m;
            string low = m.name.ToLowerInvariant();
            if (low.Contains("glass") || low.Contains("стекл") || low.Contains("uv_grid")) return m;
            Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : (m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null);
            return tex == null ? m : Twin(m, tex, city);
        }

        private static Material Twin(Material src, Texture tex, Shader city)
        {
            string path = Folder + "/Twins/" + Safe(src.name) + " (city).mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(city);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", tex);
            Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : (src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white);
            m.SetColor("_BaseColor", c);
            m.DisableKeyword("_ATLAS_WINDOWS");
            m.SetFloat("_AtlasWindows", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ───────────────────────── 2. материалы города ─────────────────────────

        private static List<Material> CityMaterials(string shaderName)
        {
            List<Material> r = new List<Material>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.StartsWith(Backup)) continue;
                Material m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m != null && m.shader != null && m.shader.name == shaderName) r.Add(m);
            }
            return r;
        }

        /// <summary>Резервная копия (один раз — «как было до стиля»), имя — GUID оригинала.</summary>
        private static void Keep(Material m)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m));
            string bp = Backup + "/" + guid + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(bp) != null) return;
            Material copy = new Material(m);
            AssetDatabase.CreateAsset(copy, bp);
        }

        private static int Materials(Look L)
        {
            int n = 0;
            foreach (Material m in CityMaterials("CatCooks/City/Building"))
            {
                Keep(m); Undo.RecordObject(m, "Стиль");
                m.SetFloat("_Saturation", L.Sat);
                m.SetFloat("_Warmth", L.Warmth);
                m.SetColor("_LightTint", L.Light);
                m.SetColor("_ShadowColor", L.Shadow);
                m.SetColor("_SkyAmbient", L.SkyAmb);
                m.SetColor("_GroundAmbient", L.GroundAmb);
                m.SetFloat("_AmbientStrength", L.Ambient);
                m.SetFloat("_RampSoft", L.RampSoft);
                m.SetFloat("_RimStrength", L.Rim);
                m.SetColor("_WinGlass", L.WinGlass);
                m.SetColor("_WinSky", L.WinSky);
                m.SetFloat("_BaseShade", L.BaseShade);
                m.SetFloat("_BaseShadeHeight", 3f);
                m.SetFloat("_TopLight", L.TopLight);
                EditorUtility.SetDirty(m); n++;
            }
            foreach (Material m in CityMaterials("CatCooks/City/Window Glass"))
            {
                Keep(m); Undo.RecordObject(m, "Стиль");
                m.SetColor("_GlassTop", L.GlassTop);
                m.SetColor("_GlassBottom", L.GlassBottom);
                m.SetColor("_SkyColor", L.GlassSky);
                m.SetFloat("_Brightness", L.GlassBright);
                m.SetFloat("_ReflMix", 0.4f);
                m.SetFloat("_StripeStrength", L.GlassStripe);
                EditorUtility.SetDirty(m); n++;
            }
            foreach (Material m in CityMaterials("CatCooks/City/Far"))
            {
                Keep(m); Undo.RecordObject(m, "Стиль");
                m.SetFloat("_Saturation", L.FarSat);
                m.SetFloat("_Contrast", L.FarContrast);
                EditorUtility.SetDirty(m); n++;
            }
            return n;
        }

        public static void Restore()
        {
            int n = 0;
            if (!AssetDatabase.IsValidFolder(Backup)) { Debug.Log("[Застройка/стиль] резервных копий нет — стиль ещё не применялся"); return; }
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Backup }))
            {
                string bp = AssetDatabase.GUIDToAssetPath(guid);
                Material b = AssetDatabase.LoadAssetAtPath<Material>(bp);
                string orig = AssetDatabase.GUIDToAssetPath(System.IO.Path.GetFileNameWithoutExtension(bp));
                Material m = string.IsNullOrEmpty(orig) ? null : AssetDatabase.LoadAssetAtPath<Material>(orig);
                if (b == null || m == null) continue;
                Undo.RecordObject(m, "Вернуть");
                m.CopyPropertiesFromMaterial(b);
                EditorUtility.SetDirty(m); n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Застройка/стиль] материалы возвращены как были: {n} (атмосферу верни, выбрав прежний стиль/погоду в Mood Director)");
        }

        // ───────────────────────── 3. атмосфера ─────────────────────────

        private static T Copy<T>(T src, string path) where T : ScriptableObject
        {
            T a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = src != null ? Object.Instantiate(src) : ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        private static string Atmosphere(CityBlockGenerator g, Look L)
        {
            float S = Mathf.Min(g.ModuleSize().x, g.ModuleSize().y);
            Color fogNear = L.FogNear, fogFar = L.FogFar;
            CatCooksFogController fog = Object.FindFirstObjectByType<CatCooksFogController>();
            if (fog != null)
            {
                Undo.RecordObject(fog, "Стиль");
                fog.usePreset = false;
                fog.heightAmount = 0f;                 // только по расстоянию — у земли тумана нет
                fog.curve = 1.35f;
                fog.toon = 0.12f;
                fog.bands = 6f;
                fog.skyBand = 0.06f;
                fog.sunAmount = 0.35f;
                EditorUtility.SetDirty(fog);
            }
            CatCooksMoodDirector d = Object.FindFirstObjectByType<CatCooksMoodDirector>();
            if (d == null || d.styles.Count == 0 || d.weathers.Count == 0)
            {
                if (fog == null) return "нет Mood Director и Fog Controller — дымку и цвет не трогал";
                fog.matchSkyHorizon = false;
                fog.colorNear = fogNear; fog.colorFar = fogFar;
                fog.start = L.FogStart * S; fog.end = L.FogEnd * S; fog.density = L.FogDensity;
                fog.Apply();
                return "Mood Director не найден — дымка настроена прямо в Fog Controller, цветокоррекцию поставь в Volume руками";
            }
            Undo.RecordObject(d, "Стиль");
            CatCooksSkyStyle cur = d.styles[Mathf.Clamp(d.styleIndex, 0, d.styles.Count - 1)];
            CatCooksWeatherPreset curW = d.weathers[Mathf.Clamp(d.weatherIndex, 0, d.weathers.Count - 1)];
            CatCooksSkyStyle st = Copy(cur, Folder + "/CityRef_Style.asset");
            CatCooksPalette pal = Copy(cur != null ? cur.palette : null, Folder + "/CityRef_Palette.asset");
            CatCooksWeatherPreset w = Copy(curW, Folder + "/CityRef_Weather.asset");

            pal.fogNear = fogNear;
            pal.fogFar = fogFar;
            // горизонт неба — светлая версия зенита, чуть к цвету дымки (не копится при повторных нажатиях)
            Color baseH = Color.Lerp(pal.zenith, Color.white, 0.6f);
            pal.horizon = Color.Lerp(baseH, fogFar, L.HorizonMix);
            pal.sunLight = L.SunLight;
            pal.ambientSky = L.AmbSky;
            pal.ambientEquator = L.AmbEq;
            pal.ambientGround = L.AmbGround;
            EditorUtility.SetDirty(pal);

            st.title = "CityRef — как в референсе";
            st.palette = pal;
            st.saturation = L.PostSat;
            st.contrast = L.PostContrast;
            st.temperature = L.PostTemp;
            st.bloom = L.PostBloom;
            st.vignette = L.PostVignette;
            st.postExposure = L.PostExposure;
            st.ambientBoost = L.AmbBoost;
            EditorUtility.SetDirty(st);

            // погода — от солнечной (ясное небо, тени от солнца), а не от той, что была выбрана
            CatCooksWeatherPreset sunny = null;
            foreach (CatCooksWeatherPreset x in d.weathers)
            {
                if (x == null || x == w) continue;
                string nm = x.name.ToLowerInvariant();
                if (nm.Contains("sunny") || nm.Contains("clear")) { sunny = x; if (nm.Contains("sunny")) break; }
            }
            if (sunny != null) EditorUtility.CopySerialized(sunny, w);
            w.title = "CityRef — дальняя дымка";
            w.fogEnabled = true;
            w.fogDensity = L.FogDensity;
            w.fogStart = L.FogStart * S;
            w.fogEnd = L.FogEnd * S;
            w.fogHeightTop = 100f * S;                                         // высота не влияет (heightAmount = 0)
            EditorUtility.SetDirty(w);

            if (!d.styles.Contains(st)) d.styles.Add(st);
            if (!d.weathers.Contains(w)) d.weathers.Add(w);
            d.SetStyle(d.styles.IndexOf(st), 0f);
            d.SetWeather(d.weathers.IndexOf(w), 0f);
            d.Apply();
            EditorUtility.SetDirty(d);
            return $"в Mood Director добавлены и выбраны стиль «CityRef_Style» и погода «CityRef_Weather» (дымка {L.FogStart * S:0}–{L.FogEnd * S:0} ед.)";
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

        private static string Safe(string s)
        {
            char[] bad = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in s) { bool ok = true; foreach (char b in bad) if (b == ch) { ok = false; break; } sb.Append(ok ? ch : '_'); }
            return sb.ToString();
        }
    }
}
