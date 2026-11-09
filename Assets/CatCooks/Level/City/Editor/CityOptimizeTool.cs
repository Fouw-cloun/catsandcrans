using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CatCooks.Level
{
    /// <summary>
    /// ОПТИМИЗАЦИЯ И КРАСОТА ГОРОДА — меню CatCooks → Застройка → «Город: …»:
    ///   1. Тени города: запечь карту высот (тени соседних домов + затенение в шейдерах, CityHeightShadows);
    ///   2. Дома: окклюзия и инстансинг (флаги Occluder/Occludee, GPU Instancing на материалах города);
    ///   3. URP: тени и GPU Resident Drawer (дистанция теней, каскады, SRP Batcher, пачечная отрисовка);
    ///   4. Всё сразу.
    /// </summary>
    public static class CityOptimizeTool
    {
        public const string Folder = "Assets/Generated/City";
        public const string MapPath = Folder + "/CC_CityHeightMap.asset";

        [MenuItem("CatCooks/Застройка/Город: оптимизация и красота — всё сразу", priority = 970)]
        public static void All()
        {
            BakeHeightMap();
            StaticAndInstancing();
            SetupUrp();
            CityHouseLodTool.All();
            FixShadows();
        }

        // ══ 1. карта высот ══
        [MenuItem("CatCooks/Застройка/Город: тени — запечь карту высот (тени домов и затенение)", priority = 971)]
        public static void BakeHeightMap()
        {
            List<Renderer> rs = CityRenderers(true);
            if (rs.Count == 0) { Debug.LogWarning("[Город] не нашёл домов для карты высот."); return; }
            Bounds bb = rs[0].bounds;
            foreach (Renderer r in rs) bb.Encapsulate(r.bounds);
            CityHeightShadows hs = Object.FindFirstObjectByType<CityHeightShadows>();
            int res = 2048;
            float margin = Mathf.Max(bb.size.x, bb.size.z) * 0.02f;
            float sx = bb.size.x + margin * 2f, sz = bb.size.z + margin * 2f;
            float minX = bb.min.x - margin, minZ = bb.min.z - margin;
            float top = bb.max.y + 10f, bottom = bb.min.y - 10f;
            Shader sh = Shader.Find("Hidden/CatCooks/City/HeightBake");
            if (sh == null) { Debug.LogError("[Город] нет шейдера Hidden/CatCooks/City/HeightBake"); return; }
            Material mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture rt = new RenderTexture(res, res, 24, RenderTextureFormat.RFloat) { filterMode = FilterMode.Point };
            rt.Create();
            CommandBuffer cb = new CommandBuffer { name = "CC_HeightBake" };
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, new Color(bottom, 0f, 0f, 0f));
            Vector3 camPos = new Vector3(minX + sx * 0.5f, top, minZ + sz * 0.5f);
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(camPos, Quaternion.Euler(90f, 0f, 0f), Vector3.one).inverse;
            Matrix4x4 proj = Matrix4x4.Ortho(-sx * 0.5f, sx * 0.5f, -sz * 0.5f, sz * 0.5f, 0.1f, top - bottom + 1f);
            cb.SetViewProjectionMatrices(view, GL.GetGPUProjectionMatrix(proj, true));
            int draws = 0;
            foreach (Renderer r in rs)
            {
                int subs = Mathf.Max(1, r.sharedMaterials.Length);
                for (int s = 0; s < subs; s++) { cb.DrawRenderer(r, mat, s, 0); draws++; }
            }
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(res, res, TextureFormat.RFloat, false, true);
            tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(mat);

            // проверка ориентации: высота над самыми высокими домами должна совпасть с их габаритом (иначе — переворот по Z)
            if (ShouldFlip(tex, rs, minX, minZ, sx, sz)) { FlipV(tex); Debug.Log("[Город] карта высот перевёрнута по Z (так у этой видеокарты) — исправлено."); }
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = "CC_CityHeightMap";
            MakeFolder(Folder);
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);
            if (existing != null) { EditorUtility.CopySerialized(tex, existing); Object.DestroyImmediate(tex); tex = existing; EditorUtility.SetDirty(tex); }
            else AssetDatabase.CreateAsset(tex, MapPath);
            AssetDatabase.SaveAssets();

            if (hs == null)
            {
                CityAtmosphere atm = Object.FindFirstObjectByType<CityAtmosphere>();
                GameObject host = atm != null ? atm.gameObject : new GameObject("City Height Shadows");
                if (atm == null) Undo.RegisterCreatedObjectUndo(host, "Тени города");
                hs = Undo.AddComponent<CityHeightShadows>(host);
            }
            Undo.RecordObject(hs, "Тени города");
            hs.HeightMap = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);
            hs.Rect = new Vector4(minX, minZ, sx, sz);
            EditorUtility.SetDirty(hs);
            Debug.Log("[Город] карта высот запечена: " + res + "×" + res + " на " + sx.ToString("0") + "×" + sz.ToString("0") + " ед. (" + (sx / res).ToString("0.00") + " ед./пиксель), домов " + rs.Count + ", отрисовок " + draws +
                      ". Тени соседних домов и затенение включены (компонент City Height Shadows на «" + hs.gameObject.name + "»). Переставил дома — запеки снова.", hs);
        }

        private static float SampleTex(Texture2D t, float u, float v)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(u * (t.width - 1)), 0, t.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(v * (t.height - 1)), 0, t.height - 1);
            return t.GetPixel(x, y).r;
        }

        private static bool ShouldFlip(Texture2D t, List<Renderer> rs, float minX, float minZ, float sx, float sz)
        {
            List<Renderer> tall = new List<Renderer>(rs);
            tall.Sort((a, b) => b.bounds.max.y.CompareTo(a.bounds.max.y));
            float eN = 0f, eF = 0f;
            int n = 0;
            for (int i = 0; i < tall.Count && n < 24; i++)
            {
                Bounds b = tall[i].bounds;
                if (b.size.x < sx * 0.004f || b.size.z < sz * 0.004f) continue;
                float u = (b.center.x - minX) / sx, v = (b.center.z - minZ) / sz;
                eN += Mathf.Abs(SampleTex(t, u, v) - b.max.y);
                eF += Mathf.Abs(SampleTex(t, u, 1f - v) - b.max.y);
                n++;
            }
            return n > 0 && eF < eN * 0.7f;
        }

        private static void FlipV(Texture2D t)
        {
            Color[] px = t.GetPixels();
            int w = t.width, h = t.height;
            Color[] o = new Color[px.Length];
            for (int y = 0; y < h; y++) System.Array.Copy(px, y * w, o, (h - 1 - y) * w, w);
            t.SetPixels(o);
            t.Apply(false, false);
        }

        /// <summary>Рендереры города: дома генератора кварталов (дальние полосы — нет, чтобы карта была чёткой), иначе — все непрозрачные в сцене.</summary>
        private static List<Renderer> CityRenderers(bool opaqueOnly)
        {
            List<Renderer> list = new List<Renderer>();
            HashSet<Renderer> seen = new HashSet<Renderer>();
            List<Transform> roots = new List<Transform>();
            foreach (CityBlockGenerator g in Object.FindObjectsByType<CityBlockGenerator>(FindObjectsSortMode.None)) roots.Add(g.transform);
            if (roots.Count == 0)
                foreach (Renderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) roots.Add(r.transform);
            foreach (Transform t in roots)
                foreach (MeshRenderer r in t.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (r == null || !r.enabled || seen.Contains(r)) continue;
                    if (opaqueOnly)
                    {
                        Material m0 = r.sharedMaterial;
                        if (m0 == null || m0.renderQueue >= 2500) continue;
                    }
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    seen.Add(r);
                    list.Add(r);
                }
            return list;
        }

        // ══ 2. окклюзия и инстансинг ══
        [MenuItem("CatCooks/Застройка/Город: дома — окклюзия и инстансинг", priority = 972)]
        public static void StaticAndInstancing()
        {
            List<Renderer> rs = CityRenderers(false);
            int flags = 0;
            HashSet<Material> mats = new HashSet<Material>();
            foreach (Renderer r in rs)
            {
                StaticEditorFlags f = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                StaticEditorFlags nf = f | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
                if (nf != f) { Undo.RecordObject(r.gameObject, "Окклюзия"); GameObjectUtility.SetStaticEditorFlags(r.gameObject, nf); flags++; }
                foreach (Material m in r.sharedMaterials) if (m != null) mats.Add(m);
            }
            int inst = 0;
            foreach (Material m in mats)
            {
                if (m.shader == null || !m.shader.name.StartsWith("CatCooks/")) continue;
                if (!m.enableInstancing) { Undo.RecordObject(m, "Инстансинг"); m.enableInstancing = true; EditorUtility.SetDirty(m); inst++; }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Город] окклюзия: флаги Occluder/Occludee поставлены " + flags + " объектам; GPU Instancing включён на " + inst + " материалах города. " +
                      "Дальше: Window → Rendering → Occlusion Culling → Bake (один раз) — дома за домами перестанут рисоваться.");
        }

        // ══ 3. URP ══
        [MenuItem("CatCooks/Застройка/Город: URP — тени и GPU Resident Drawer", priority = 973)]
        public static void SetupUrp()
        {
            UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) { Debug.LogWarning("[Город] URP ассет не найден в Graphics Settings."); return; }
            float m = Shader.GetGlobalFloat("_CC_Meter");
            if (m <= 0f) m = 3.5f;
            Undo.RecordObject(urp, "URP");
            float oldDist = urp.shadowDistance;
            urp.shadowDistance = NearShadowDistance();                                   // настоящие тени — ближние слои домов; дальше — карта высот
            urp.shadowCascadeCount = 4;
            urp.useSRPBatcher = true;
            SerializedObject so = new SerializedObject(urp);
            SerializedProperty grd = so.FindProperty("m_GPUResidentDrawerMode");
            string grdMsg;
            if (grd != null) { grd.intValue = 1; so.ApplyModifiedProperties(); grdMsg = "GPU Resident Drawer: Instanced Drawing"; }
            else grdMsg = "GPU Resident Drawer: поле не найдено (версия URP) — включи руками в URP ассете";
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            Debug.Log("[Город] URP: дистанция теней " + oldDist.ToString("0") + " → " + urp.shadowDistance.ToString("0") + " ед., каскадов 2, SRP Batcher вкл., " + grdMsg +
                      ". Для GPU Resident Drawer нужен Rendering Path = Forward+ в Universal Renderer и в Project Settings → Graphics → «BatchRendererGroup Variants» = Keep All.", urp);
        }

        /// <summary>Дистанция настоящих теней: модуль игрока и соседние (≈ 1.6 модуля), не меньше 300 ед.</summary>
        private static float NearShadowDistance()
        {
            CityBlockGenerator g = Object.FindFirstObjectByType<CityBlockGenerator>();
            float ms = g != null ? Mathf.Max(g.ModuleSize().x, g.ModuleSize().y) : 250f;
            return Mathf.Max(300f, ms * 1.6f);
        }

        // ══ 5. тени: проверить и включить ══
        [MenuItem("CatCooks/Застройка/Город: тени — проверить и включить (ближние слои)", priority = 974)]
        public static void FixShadows()
        {
            System.Text.StringBuilder rep = new System.Text.StringBuilder("[Город] тени — проверка:");
            // URP: тени солнца включены, дистанция — ближние слои, 4 каскада, карта 4096
            UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                Undo.RecordObject(urp, "Тени");
                SerializedObject so = new SerializedObject(urp);
                SerializedProperty on = so.FindProperty("m_MainLightShadowsSupported");
                if (on != null && !on.boolValue) { on.boolValue = true; rep.Append("\n  • URP: тени солнца были ВЫКЛЮЧЕНЫ — включил"); }
                SerializedProperty res = so.FindProperty("m_MainLightShadowmapResolution");
                if (res != null && res.intValue < 4096) { res.intValue = 4096; rep.Append("\n  • URP: карта теней 4096"); }
                SerializedProperty soft = so.FindProperty("m_SoftShadowsSupported");
                if (soft != null) soft.boolValue = true;
                so.ApplyModifiedProperties();
                float old = urp.shadowDistance;
                urp.shadowDistance = NearShadowDistance();
                urp.shadowCascadeCount = 4;
                EditorUtility.SetDirty(urp);
                rep.Append("\n  • URP: дистанция теней ").Append(old.ToString("0")).Append(" → ").Append(urp.shadowDistance.ToString("0")).Append(" ед., 4 каскада");
            }
            else rep.Append("\n  • URP ассет не найден!");
            // солнце: мягкие тени
            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
            if (sun != null)
            {
                Undo.RecordObject(sun, "Тени");
                if (sun.shadows == LightShadows.None) rep.Append("\n  • у солнца «").Append(sun.name).Append("» тени были ВЫКЛЮЧЕНЫ — включил");
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = Mathf.Max(sun.shadowStrength, 0.85f);
                EditorUtility.SetDirty(sun);
            }
            else rep.Append("\n  • направленный свет (солнце) не найден!");
            // дома: отбрасывают и получают тени (дальние полосы не трогаем)
            int cast = 0;
            Dictionary<string, int> shaders = new Dictionary<string, int>();
            HashSet<Renderer> details = CityHouseLodTool.DetailRenderers();      // мелочь — без теней (меню «тени только от крупных частей»)
            foreach (Renderer r in CityRenderers(true))
            {
                if (details.Contains(r))
                {
                    if (!r.receiveShadows) { Undo.RecordObject(r, "Тени"); r.receiveShadows = true; }
                }
                else if (r.shadowCastingMode == ShadowCastingMode.Off || !r.receiveShadows)
                {
                    Undo.RecordObject(r, "Тени");
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = true;
                    cast++;
                }
                Material m0 = r.sharedMaterial;
                string sn = m0 != null && m0.shader != null ? m0.shader.name : "(нет материала)";
                int c; shaders.TryGetValue(sn, out c); shaders[sn] = c + 1;
            }
            rep.Append("\n  • домам включены тени: ").Append(cast);
            rep.Append("\n  • шейдеры домов и улиц:");
            foreach (KeyValuePair<string, int> kv in shaders)
            {
                rep.Append("\n      ").Append(kv.Key).Append(" — ").Append(kv.Value);
                if (!kv.Key.StartsWith("CatCooks/") && !kv.Key.StartsWith("Universal Render Pipeline/Lit")) rep.Append("   ← этот шейдер может не принимать тени");
            }
            CityHeightShadows hs = Object.FindFirstObjectByType<CityHeightShadows>();
            rep.Append("\n  • карта высот: ").Append(hs == null ? "НЕТ — запусти «Город: тени — запечь карту высот»" : (hs.HeightMap == null ? "компонент есть, карты нет — запеки" : "есть"));
            AssetDatabase.SaveAssets();
            Debug.Log(rep.ToString());
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
