using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Level
{
    /// <summary>
    /// ОБЛЕГЧИТЬ ДОМА ФОНА — меню CatCooks → Застройка → «Город: дома — …». Ничего не генерирует заново, работает с уже стоящими домами:
    ///   1. Тени только от крупных частей: мелочь (рамы, карнизы, балконы, кондиционеры, трубы) не рисуется в карту теней
    ///      (она рисуется туда по разу на каждый каскад) — тени на вид те же, проход теней в разы дешевле;
    ///   2. Авто-LOD: на каждый дом — встроенный компонент Unity LOD Group: вблизи дом целиком, дальше — без мелочи.
    ///      Сам дом не пропадает никогда (силуэт города целый);
    ///   3. Окна: комнаты за стеклом (самая тяжёлая часть шейдера окон) — только у ближних домов, дальше — обычное стекло.
    /// Дом = объект «B_…» генератора кварталов (пропы «S_…», дороги и земля не трогаются);
    /// без генератора — выделенные объекты: дом = корень префаба, иначе — родитель. Мелочь — деталь меньше доли размера дома.
    /// Всё отменяется (Ctrl+Z). Повторный запуск — пересобирает.
    /// </summary>
    public static class CityHouseLodTool
    {
        /// <summary>Мелочь — деталь, у которой самый большой размер меньше этой доли от размера дома.</summary>
        public static float DetailFraction = 0.12f;
        /// <summary>Мелочь пропадает, когда дом занимает на экране меньше этой доли высоты.</summary>
        public static float DetailScreen = 0.18f;
        /// <summary>Комнаты за окнами видно до, м (в шейдере окон), дальше — обычное стекло.</summary>
        public static float RoomMeters = 60f;

        public class House
        {
            public Transform Root;
            public List<Renderer> Big = new List<Renderer>();
            public List<Renderer> Detail = new List<Renderer>();
        }

        // ── поиск домов ──
        private static bool Skip(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.GetComponent<CityFarBands>() != null || p.GetComponent<CityFloorGenerator>() != null || p.GetComponent<CityFakeFloors>() != null) return true;
                string n = p.name;
                if (n == CityFloorGenerator.RootName) return true;
            }
            return false;
        }

        private static Transform HouseRoot(Transform t, Transform top, bool generator)
        {
            for (Transform p = t; p != null && p != top; p = p.parent)
                if (p.name.StartsWith("B_")) return p;
            if (generator) return null;                                         // в генераторе дом — только «B_…» (пропы, дороги, земля — мимо)
            GameObject pr = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
            if (pr != null && pr.transform != top) return pr.transform;
            return t.parent != null && t.parent != top ? t.parent : t;
        }

        /// <summary>Дома: из генераторов кварталов; если их нет — из выделенных объектов.</summary>
        public static List<House> Houses(out string where)
        {
            List<Transform> tops = new List<Transform>();
            foreach (CityBlockGenerator g in Object.FindObjectsByType<CityBlockGenerator>(FindObjectsSortMode.None)) tops.Add(g.transform);
            where = "генераторы кварталов";
            if (tops.Count == 0)
            {
                foreach (Transform t in Selection.transforms) tops.Add(t);
                where = "выделенные объекты";
            }
            Dictionary<Transform, House> map = new Dictionary<Transform, House>();
            List<House> list = new List<House>();
            HashSet<Renderer> seen = new HashSet<Renderer>();
            foreach (Transform top in tops)
                foreach (MeshRenderer r in top.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r == null || seen.Contains(r) || Skip(r.transform)) continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    seen.Add(r);
                    Transform hr = HouseRoot(r.transform, top, where == "генераторы кварталов");
                    if (hr == null) continue;
                    House h;
                    if (!map.TryGetValue(hr, out h)) { h = new House(); h.Root = hr; map[hr] = h; list.Add(h); }
                    h.Big.Add(r);
                }
            // мелочь — по доле от размера дома
            foreach (House h in list)
            {
                Bounds hb = new Bounds();
                bool any = false;
                foreach (Renderer r in h.Big) { if (!any) { hb = r.bounds; any = true; } else hb.Encapsulate(r.bounds); }
                float hs = Mathf.Max(hb.size.x, Mathf.Max(hb.size.y, hb.size.z));
                if (hs <= 0f || h.Big.Count < 2) continue;
                for (int i = h.Big.Count - 1; i >= 0; i--)
                {
                    Renderer r = h.Big[i];
                    if (IsGlass(r)) continue;                                      // стекло окон остаётся всегда (иначе вдали — дыры)
                    Vector3 s = r.bounds.size;
                    if (Mathf.Max(s.x, Mathf.Max(s.y, s.z)) < hs * DetailFraction) { h.Big.RemoveAt(i); h.Detail.Add(r); }
                }
                if (h.Big.Count == 0) { h.Big.AddRange(h.Detail); h.Detail.Clear(); }   // весь дом — мелочь? значит, это и есть дом
            }
            return list;
        }

        /// <summary>Стекло / прозрачное: не мелочь, теней не отбрасывает.</summary>
        public static bool IsGlass(Renderer r)
        {
            Material m = r.sharedMaterial;
            if (m == null) return false;
            return m.renderQueue >= 2500 || (m.shader != null && m.shader.name == "CatCooks/City/Window Glass");
        }

        /// <summary>Все мелкие детали домов (для проверки теней — им тени не включать).</summary>
        public static HashSet<Renderer> DetailRenderers()
        {
            string w;
            HashSet<Renderer> set = new HashSet<Renderer>();
            foreach (House h in Houses(out w)) foreach (Renderer r in h.Detail) set.Add(r);
            return set;
        }

        // ══ всё три ══
        [MenuItem("CatCooks/Застройка/Город: дома — облегчить (тени, LOD, окна)", priority = 975)]
        public static void All()
        {
            DetailShadows();
            AutoLod();
            WindowRooms();
        }

        // ══ 1. тени только от крупных частей ══
        [MenuItem("CatCooks/Застройка/Город: дома — тени только от крупных частей", priority = 976)]
        public static void DetailShadows()
        {
            string where;
            List<House> hs = Houses(out where);
            if (hs.Count == 0) { Debug.LogWarning("[Город] дома не найдены: нет генератора кварталов — выдели дома в иерархии и запусти снова"); return; }
            int off = 0, on = 0;
            foreach (House h in hs)
            {
                foreach (Renderer r in h.Detail)
                    if (r.shadowCastingMode != ShadowCastingMode.Off) { Undo.RecordObject(r, "Тени мелочи"); r.shadowCastingMode = ShadowCastingMode.Off; off++; }
                foreach (Renderer r in h.Big)
                    if (r.shadowCastingMode == ShadowCastingMode.Off && !IsGlass(r)) { Undo.RecordObject(r, "Тени мелочи"); r.shadowCastingMode = ShadowCastingMode.On; on++; }
            }
            Debug.Log("[Город] тени только от крупных частей (" + where + "): домов " + hs.Count + ", мелочи без теней " + off + ", крупным включено " + on + ". Принимать тени мелочь продолжает.");
        }

        // ══ 2. авто-LOD ══
        [MenuItem("CatCooks/Застройка/Город: дома — авто-LOD (мелочь пропадает вдали)", priority = 977)]
        public static void AutoLod()
        {
            string where;
            List<House> hs = Houses(out where);
            if (hs.Count == 0) { Debug.LogWarning("[Город] дома не найдены: нет генератора кварталов — выдели дома в иерархии и запусти снова"); return; }
            int made = 0, skipped = 0, solid = 0;
            foreach (House h in hs)
            {
                LODGroup g = h.Root.GetComponent<LODGroup>();
                if (g != null && g.GetLODs().Length > 0 && !IsOurs(h.Root)) { skipped++; continue; }   // свои LOD у модели — не трогаем
                if (HasLodBelow(h.Root)) { skipped++; continue; }
                if (h.Detail.Count == 0)
                {
                    if (g != null) Undo.DestroyObjectImmediate(g);
                    solid++;
                    continue;
                }
                if (g == null) g = Undo.AddComponent<LODGroup>(h.Root.gameObject);
                else Undo.RecordObject(g, "Авто-LOD");
                List<Renderer> all = new List<Renderer>(h.Big);
                all.AddRange(h.Detail);
                LOD[] lods = new LOD[2];
                lods[0] = new LOD(DetailScreen, all.ToArray());
                lods[1] = new LOD(0f, h.Big.ToArray());                        // 0 — дом не пропадает никогда
                g.fadeMode = LODFadeMode.None;
                g.SetLODs(lods);
                g.RecalculateBounds();
                EditorUtility.SetDirty(h.Root.gameObject);
                made++;
            }
            Debug.Log("[Город] авто-LOD (" + where + "): домов с LOD " + made + ", цельных (без мелочи, LOD не нужен) " + solid + ", пропущено (свои LOD у модели) " + skipped
                + ". Мелочь пропадает, когда дом меньше " + (DetailScreen * 100f).ToString("0") + "% высоты экрана. Поправить — полоска LOD Group на объекте дома.");
        }

        private static bool HasLodBelow(Transform root)
        {
            foreach (LODGroup g in root.GetComponentsInChildren<LODGroup>(true)) if (g.transform != root) return true;
            return false;
        }

        // «наш» LOD Group: ровно 2 уровня и последний — 0 (у LOD из моделей последний уровень почти всегда больше 0)
        private static bool IsOurs(Transform root)
        {
            LODGroup g = root.GetComponent<LODGroup>();
            if (g == null) return true;
            LOD[] l = g.GetLODs();
            return l.Length == 2 && l[1].screenRelativeTransitionHeight <= 0f;
        }

        /// <summary>Снять авто-LOD со всех домов.</summary>
        [MenuItem("CatCooks/Застройка/Город: дома — снять авто-LOD", priority = 979)]
        public static void RemoveLod()
        {
            string where;
            int n = 0;
            foreach (House h in Houses(out where))
            {
                LODGroup g = h.Root.GetComponent<LODGroup>();
                if (g != null && IsOurs(h.Root)) { Undo.DestroyObjectImmediate(g); n++; }
            }
            Debug.Log("[Город] авто-LOD снят с домов: " + n);
        }

        // ══ 3. окна: комнаты только у ближних домов ══
        [MenuItem("CatCooks/Застройка/Город: дома — окна: комнаты только вблизи", priority = 978)]
        public static void WindowRooms()
        {
            string where;
            HashSet<Material> mats = new HashSet<Material>();
            foreach (House h in Houses(out where))
            {
                foreach (Renderer r in h.Big) foreach (Material m in r.sharedMaterials) if (m != null) mats.Add(m);
                foreach (Renderer r in h.Detail) foreach (Material m in r.sharedMaterials) if (m != null) mats.Add(m);
            }
            int n = 0, withRooms = 0;
            foreach (Material m in mats)
            {
                if (m.shader == null || m.shader.name != "CatCooks/City/Window Glass" || !m.HasProperty("_RoomFar")) continue;
                Undo.RecordObject(m, "Комнаты за окнами");
                m.SetFloat("_RoomFar", RoomMeters);
                EditorUtility.SetDirty(m);
                n++;
                if (m.HasProperty("_Interior") && m.GetFloat("_Interior") > 0.001f) withRooms++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Город] окна: материалов окон " + n + " (с комнатами " + withRooms + "): комнаты за стеклом видно до " + RoomMeters.ToString("0") + " м, дальше — обычное стекло без расчёта комнат. "
                + "Включить/выключить комнаты — ползунок «Комнаты за окнами» в материале окна.");
        }
    }
}
