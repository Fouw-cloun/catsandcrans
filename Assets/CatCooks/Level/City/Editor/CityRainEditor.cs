using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CatCooks.Level
{
    /// <summary>Дождь города: поставить в сцену (материалы, объект City Rain, погодная зона 1).</summary>
    public static class CityRainEditor
    {
        public const string Folder = "Assets/Generated/City/Rain";

        [MenuItem("CatCooks/Застройка/Дождь: поставить в сцену", priority = 950)]
        public static void Setup()
        {
            MakeFolder(Folder);
            Material streak = Mat("CC_RainStreaks", "CatCooks/City/Rain Streaks");
            Material splash = Mat("CC_RainSplash", "CatCooks/City/Rain Splash");
            Material curtain = Mat("CC_RainCurtain", "CatCooks/City/Rain Curtain");
            Material drips = Mat("CC_RainDrips", "CatCooks/City/Rain Drips");
            Material steam = Mat("CC_RainSteam", "CatCooks/City/Rain Steam");

            CityRain rain = Object.FindFirstObjectByType<CityRain>();
            if (rain == null)
            {
                GameObject go = new GameObject("City Rain");
                Undo.RegisterCreatedObjectUndo(go, "Дождь");
                rain = go.AddComponent<CityRain>();
            }
            Undo.RecordObject(rain, "Дождь");
            rain.StreakMaterial = streak; rain.SplashMaterial = splash; rain.CurtainMaterial = curtain;
            rain.DripMaterial = drips; rain.SteamMaterial = steam;
            EditorUtility.SetDirty(rain);

            string zoneNote;
            CityRainZone[] zones = Object.FindObjectsByType<CityRainZone>(FindObjectsSortMode.None);
            if (zones.Length == 0)
            {
                CityBlockGenerator g = Object.FindFirstObjectByType<CityBlockGenerator>();
                Vector3 c = Vector3.zero; float m = 250f;
                if (g != null) { Vector3 lc; if (g.LevelCenter(out lc)) c = lc; m = Mathf.Min(g.ModuleSize().x, g.ModuleSize().y); }
                GameObject zg = new GameObject("Rain Zone 1");
                Undo.RegisterCreatedObjectUndo(zg, "Дождь");
                zg.transform.position = c + Vector3.up * (m * 0.7f);
                CityRainZone zone = zg.AddComponent<CityRainZone>();
                zone.Size = new Vector3(m, m * 1.4f, m);
                zoneNote = "создана «Rain Zone 1» в центре уровня — поставь её в красный модуль, размер — Size в инспекторе";
            }
            else zoneNote = "зон в сцене: " + zones.Length;
            int feats = EnsureSurfaceFeature();
            zoneNote += feats > 0 ? "; проход «City Rain Surface» добавлен в URP Renderer (" + feats + ")" : "; проход мокрости уже стоит";
            AssetDatabase.SaveAssets();
            Selection.activeObject = rain.gameObject;
            Debug.Log("[Дождь] готово: материалы в " + Folder + ", объект «City Rain»; " + zoneNote +
                      ". Проверка: Play → P → «Город» → Тест + Вид. В URP Asset должна быть включена Depth Texture (для всплесков вне зоны).", rain);
        }

        /// <summary>Добавить проход мокрых поверхностей во все URP Renderer'ы проекта. Возвращает, сколько добавлено.</summary>
        public static int EnsureSurfaceFeature()
        {
            int added = 0;
            List<UniversalRenderPipelineAsset> pipes = new List<UniversalRenderPipelineAsset>();
            UniversalRenderPipelineAsset d = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (d != null) pipes.Add(d);
            for (int q = 0; q < QualitySettings.names.Length; q++)
            {
                UniversalRenderPipelineAsset a = QualitySettings.GetRenderPipelineAssetAt(q) as UniversalRenderPipelineAsset;
                if (a != null && !pipes.Contains(a)) pipes.Add(a);
            }
            FieldInfo listF = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo mapF = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", BindingFlags.NonPublic | BindingFlags.Instance);
            Shader sh = Shader.Find("Hidden/CatCooks/City/Rain Surface");
            Shader shMask = Shader.Find("Hidden/CatCooks/City/Wet Mask");
            foreach (UniversalRenderPipelineAsset p in pipes)
            {
                ScriptableRendererData[] list = listF != null ? listF.GetValue(p) as ScriptableRendererData[] : null;
                if (list == null) continue;
                foreach (ScriptableRendererData rd in list)
                {
                    if (rd == null) continue;
                    bool has = false;
                    foreach (ScriptableRendererFeature f in rd.rendererFeatures)
                    {
                        CityRainSurfaceFeature cf = f as CityRainSurfaceFeature;
                        if (cf == null) continue;
                        has = true;
                        // уже стоит (шаг 2) — дописать шейдеры, чтобы попали в билд и маска заработала
                        if (cf.MaskShader == null || cf.Shader == null)
                        {
                            if (cf.Shader == null) cf.Shader = sh;
                            if (cf.MaskShader == null) cf.MaskShader = shMask;
                            cf.Create();
                            EditorUtility.SetDirty(cf);
                            EditorUtility.SetDirty(rd);
                            Debug.Log("[Дождь] в «" + rd.name + "» проходу мокрости добавлена маска «что мокнет»");
                        }
                        break;
                    }
                    if (has) continue;
                    CityRainSurfaceFeature feat = ScriptableObject.CreateInstance<CityRainSurfaceFeature>();
                    feat.name = "City Rain Surface";
                    feat.Shader = sh;
                    feat.MaskShader = shMask;
                    AssetDatabase.AddObjectToAsset(feat, rd);
                    rd.rendererFeatures.Add(feat);
                    string guid; long id;
                    if (mapF != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out guid, out id))
                    {
                        List<long> map = mapF.GetValue(rd) as List<long>;
                        if (map != null) map.Add(id);
                    }
                    MethodInfo dirty = typeof(ScriptableRendererData).GetMethod("SetDirty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (dirty != null) dirty.Invoke(rd, null);
                    EditorUtility.SetDirty(rd);
                    added++;
                }
            }
            if (pipes.Count == 0) Debug.LogWarning("[Дождь] URP Asset не найден — добавь проход руками: URP Renderer → Add Renderer Feature → City Rain Surface");
            return added;
        }

        [MenuItem("CatCooks/Застройка/Зонт: поставить (курортный, раскрывается)", priority = 951)]
        public static void Umbrella()
        {
            MakeFolder(Folder);
            Material mat = Mat("CC_Umbrella", "CatCooks/City/Toon");
            SceneView sv = SceneView.lastActiveSceneView;
            Vector3 pos = sv != null ? sv.pivot : Vector3.zero;
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out hit, 500f)) pos = hit.point;   // на пол
            int n = Object.FindObjectsByType<CityUmbrella>(FindObjectsSortMode.None).Length + 1;
            GameObject go = new GameObject("Umbrella " + n);
            Undo.RegisterCreatedObjectUndo(go, "Зонт");
            go.transform.position = pos;
            CityUmbrella u = go.AddComponent<CityUmbrella>();
            u.Material = mat;
            Selection.activeObject = go;
            Debug.Log("[Зонт] поставлен. Раскрыть/сложить: галка Is Open, клик по шесту в игре, клавиша Key или «Сам открываться в дождь».", go);
        }

        private static Material Mat(string name, string shader)
        {
            string path = Folder + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader sh = Shader.Find(shader);
            if (sh == null) { Debug.LogError("[Дождь] шейдер не найден: " + shader); return m; }
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) { m.shader = sh; EditorUtility.SetDirty(m); }
            return m;
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

        [MenuItem("CatCooks/Застройка/Мокнет: отметить выделенное (модули этажа)", priority = 952)]
        public static void MarkWet()
        {
            int n = 0, r = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                if (go == null) continue;
                CityWetSurface ws = go.GetComponent<CityWetSurface>();
                if (ws == null) { ws = Undo.AddComponent<CityWetSurface>(go); n++; }
                foreach (Renderer rr in go.GetComponentsInChildren<Renderer>(true)) Undo.RecordObject(rr, "Мокнет");
                ws.Refresh();
                r += ws.CountMarked();
            }
            if (Selection.gameObjects.Length == 0) Debug.LogWarning("[Дождь] выдели модули этажа в иерархии и повтори");
            else Debug.Log("[Дождь] мокнет: отмечено объектов " + n + " (новых), рендереров всего " + r + ". На City Rain должна стоять галка Wet Only Marked");
        }

        [MenuItem("CatCooks/Застройка/Мокнет: снять отметку с выделенного", priority = 953)]
        public static void UnmarkWet()
        {
            int n = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                if (go == null) continue;
                CityWetSurface ws = go.GetComponent<CityWetSurface>();
                if (ws == null) continue;
                foreach (Renderer rr in go.GetComponentsInChildren<Renderer>(true)) Undo.RecordObject(rr, "Не мокнет");
                Undo.DestroyObjectImmediate(ws);
                n++;
            }
            Debug.Log("[Дождь] снята отметка «мокнет» с объектов: " + n);
        }

        [MenuItem("CatCooks/Застройка/Капает: отметить выделенное (балки, навесы)", priority = 954)]
        public static void MarkDrip()
        {
            int n = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                if (go == null || go.GetComponent<CityDripEdge>() != null) continue;
                Undo.AddComponent<CityDripEdge>(go);
                n++;
            }
            if (Selection.gameObjects.Length == 0) Debug.LogWarning("[Дождь] выдели балки/навесы/козырьки в иерархии и повтори");
            else Debug.Log("[Дождь] капает с краёв: добавлено объектов " + n + ". Режим Bottom Edges — нижний периметр, Line — свой отрезок A→B (виден при выделении)");
        }

        [MenuItem("CatCooks/Застройка/Шторм: поставить в сцену (молнии, порывы)", priority = 955)]
        public static void SetupStorm()
        {
            MakeFolder(Folder);
            Material bolt = Mat("CC_Lightning", "CatCooks/City/Lightning");
            Material streaks = Mat("CC_WindStreaks", "CatCooks/City/Wind Streaks");
            Material debris = Mat("CC_WindDebris", "CatCooks/City/Wind Debris");
            Material scorch = Mat("CC_Scorch", "CatCooks/City/Scorch");
            Material dust = Mat("CC_WindDust", "CatCooks/City/Wind Dust");
            Material motes = Mat("CC_WindMotes", "CatCooks/City/Wind Motes");
            Material impact = Mat("CC_LightningImpact", "CatCooks/City/Lightning Impact");
            Material junk = Mat("CC_WindJunk", "CatCooks/City/Wind Junk");
            Material leaves = Mat("CC_WindLeaves", "CatCooks/City/Wind Leaves");
            Material clouds = Mat("CC_WindClouds", "CatCooks/City/Wind Clouds");
            CityStorm storm = Object.FindFirstObjectByType<CityStorm>();
            if (storm == null)
            {
                GameObject go = new GameObject("City Storm");
                Undo.RegisterCreatedObjectUndo(go, "Шторм");
                storm = go.AddComponent<CityStorm>();
            }
            Undo.RecordObject(storm, "Шторм");
            storm.LightningMaterial = bolt;
            storm.StreakMaterial = streaks;
            storm.DebrisMaterial = debris;
            storm.ScorchMaterial = scorch;
            storm.DustMaterial = dust;
            storm.MotesMaterial = motes;
            storm.ImpactMaterial = impact;
            storm.JunkMaterial = junk;
            storm.LeavesMaterial = leaves;
            storm.DustCloudMaterial = clouds;
            EditorUtility.SetDirty(storm);
            AssetDatabase.SaveAssets();
            Selection.activeObject = storm.gameObject;
            Debug.Log("[Шторм] готово: объект «City Storm», материалы в " + Folder +
                      ". Проверка: Play → P → «Город» → ШТОРМ → Тест, кнопки «Молния!», «Зарница», «Порыв!», «Удар в игровую зону!». Молнии сами — в грозу и шторм по погоде.", storm);
        }

        [MenuItem("CatCooks/Застройка/Зима: поставить в сцену (лёд, снег, буран)", priority = 956)]
        public static void SetupWinter()
        {
            MakeFolder(Folder);
            Material snow = Mat("CC_Snow", "CatCooks/City/Snow");
            Material ice = Mat("CC_Icicles", "CatCooks/City/Icicles");
            Material breath = Mat("CC_Breath", "CatCooks/City/Breath");
            Material pile = Mat("CC_SnowPile", "CatCooks/City/Snow Pile");
            CityWinter w = Object.FindFirstObjectByType<CityWinter>();
            if (w == null)
            {
                GameObject go = new GameObject("City Winter");
                Undo.RegisterCreatedObjectUndo(go, "Зима");
                w = go.AddComponent<CityWinter>();
            }
            Undo.RecordObject(w, "Зима");
            w.SnowMaterial = snow; w.IcicleMaterial = ice; w.BreathMaterial = breath; w.PileMaterial = pile;
            w.AutoTextures();                                                         // текстуры льда Ice004 (по GUID)
            if (w.IceToon == null) Debug.LogWarning("[Зима] не нашёл текстуру льда (Textures/CC_Ice_Toon.png) — лёд будет без трещин.", w);
            EditorUtility.SetDirty(w);
            int feats = EnsureSurfaceFeature();
            AssetDatabase.SaveAssets();
            Selection.activeObject = w.gameObject;
            Debug.Log("[Зима] готово: объект «City Winter», материалы в " + Folder + (feats > 0 ? "; проход «City Rain Surface» добавлен в URP Renderer" : "") +
                      ". Проверка: Play → P → «Город» → ЗИМА → Тест, «Заморозить», «Засыпать снегом». Сосульки — на краях с City Drip Edge и у зонтов.", w);
        }

        [MenuItem("CatCooks/Застройка/Град: поставить в сцену (градины, отскок, россыпь на полу)", priority = 957)]
        public static void SetupHail()
        {
            MakeFolder(Folder);
            Material hail = Mat("CC_Hail", "CatCooks/City/Hail");
            CityHail h = Object.FindFirstObjectByType<CityHail>();
            if (h == null)
            {
                GameObject go = new GameObject("City Hail");
                Undo.RegisterCreatedObjectUndo(go, "Град");
                h = go.AddComponent<CityHail>();
            }
            Undo.RecordObject(h, "Град");
            h.HailMaterial = hail;
            EditorUtility.SetDirty(h);
            int feats = EnsureSurfaceFeature();
            AssetDatabase.SaveAssets();
            Selection.activeObject = h.gameObject;
            Debug.Log("[Град] готово: объект «City Hail», материал в " + Folder + (feats > 0 ? "; проход «City Rain Surface» добавлен в URP Renderer" : "") +
                      ". Погоды: «Град», «Буря с градом», «Гроза с градом» (новые — меню «Погоды: собрать лестницу»). Проверка: Play → P → «Город» → ГРАД → Тест.", h);
        }

        [MenuItem("CatCooks/Застройка/Жара: поставить в сцену (марево, миражи, зной)", priority = 958)]
        public static void SetupHeat()
        {
            CityHeat h = Object.FindFirstObjectByType<CityHeat>();
            if (h == null)
            {
                GameObject go = new GameObject("City Heat");
                Undo.RegisterCreatedObjectUndo(go, "Жара");
                h = go.AddComponent<CityHeat>();
            }
            int feats = EnsureSurfaceFeature();
            AssetDatabase.SaveAssets();
            Selection.activeObject = h.gameObject;
            Debug.Log("[Жара] готово: объект «City Heat»" + (feats > 0 ? "; проход «City Rain Surface» добавлен в URP Renderer" : "") +
                      ". Погоды: «Жара, марево» и новое «Пекло» (меню «Погоды: добавить новые»). Проверка: Play → P → «Город» → ЖАРА → Тест.", h);
        }

        [MenuItem("CatCooks/Застройка/Туман: поставить в сцену (низкий, вокруг этажа, густой)", priority = 959)]
        public static void SetupFog()
        {
            CityFog f = Object.FindFirstObjectByType<CityFog>();
            if (f == null)
            {
                GameObject go = new GameObject("City Fog");
                Undo.RegisterCreatedObjectUndo(go, "Туман");
                f = go.AddComponent<CityFog>();
            }
            int feats = EnsureSurfaceFeature();
            AssetDatabase.SaveAssets();
            Selection.activeObject = f.gameObject;
            Debug.Log("[Туман] готово: объект «City Fog»" + (feats > 0 ? "; проход «City Rain Surface» добавлен в URP Renderer" : "") +
                      ". Погоды: «Низкий туман», «Туман вокруг», «Густой туман» (меню «Погоды: добавить новые»). Проверка: Play → P → «Город» → ТУМАН → Тест.", f);
        }
    }
}
