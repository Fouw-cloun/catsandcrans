using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>Инспектор общего роста этажей и общие редакторские помощники (пассажиры этажа, на землю, цвет фейк-этажей).</summary>
    [CustomEditor(typeof(CityLevelStack))]
    public class CityLevelStackEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CityLevelStack s = (CityLevelStack)target;
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (GUILayout.Button("Найти все этажи и дома в сцене")) { Undo.RecordObject(s, "Этажи"); s.Collect(); EditorUtility.SetDirty(s); }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("▲ ВСЕ на этаж вверх", GUILayout.Height(30))) RaiseAll(s, 1);
            if (GUILayout.Button("▼ ВСЕ на этаж вниз", GUILayout.Height(30))) RaiseAll(s, -1);
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Все на землю (по 1 фейк-этажу)")) { GroundAll(s); }
            if (GUILayout.Button("Привязать пропсы к этажам (станут детьми «Props»)")) BindAll(s);
            EditorGUILayout.HelpBox("Подъём двигает все этажи сразу и добавляет по фейк-этажу снизу. Пропсы, лежащие внутри габарита этажа, едут вместе; чтобы ехали и в игре, нажми «Привязать пропсы».", MessageType.None);
        }

        // ─────────── редакторские операции ───────────
        public static void RaiseAll(CityLevelStack s, int n)
        {
            int a = s.Allowed(n);
            if (a == 0) { Debug.LogWarning("[Рост этажей] дальше некуда (уровень " + s.Level + ", предел " + s.MaxLevel + ", ниже земли нельзя).", s); return; }
            List<Component> all = s.Targets;
            Dictionary<Component, List<Transform>> pass = new Dictionary<Component, List<Transform>>();
            for (int i = 0; i < all.Count; i++) if (all[i] != null) pass[all[i]] = Passengers(all[i], all);
            Undo.RecordObject(s, "Рост этажей");
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                Undo.RecordObject(all[i], "Рост этажей");
                Undo.RecordObject(all[i].transform, "Рост этажей");
            }
            bool cb = s.CarryBodies;
            s.CarryBodies = false; // пассажиров двигаем сами, ниже
            s.Raise(n);
            s.CarryBodies = cb;
            int moved = 0;
            foreach (KeyValuePair<Component, List<Transform>> kv in pass)
            {
                float dy = a * CityLevelStack.HeightOf(kv.Key);
                for (int i = 0; i < kv.Value.Count; i++) { Undo.RecordObject(kv.Value[i], "Рост этажей"); kv.Value[i].position += new Vector3(0f, dy, 0f); moved++; }
            }
            EditorUtility.SetDirty(s);
            Debug.Log("[Рост этажей] вместе с этажами сдвинуто объектов-пассажиров: " + moved, s);
        }

        public static void GroundAll(CityLevelStack s)
        {
            for (int i = 0; i < s.Targets.Count; i++) if (s.Targets[i] != null) ToGround(s.Targets[i], s.GroundY, s.Targets);
            Undo.RecordObject(s, "На землю");
            s.Level = 0;
        }

        public static void BindAll(CityLevelStack s)
        {
            int n = 0;
            for (int i = 0; i < s.Targets.Count; i++) if (s.Targets[i] != null) n += Bind(s.Targets[i], s.Targets);
            Debug.Log("[Рост этажей] привязано пропсов: " + n + ". Теперь они дети объекта «Props» у своего этажа и едут вместе с ним.", s);
        }

        /// <summary>Опустить так, чтобы низ единственного фейк-этажа стоял на земле; пропсы едут с этажом.</summary>
        public static void ToGround(Component c, float groundY, List<Component> all)
        {
            Bounds fp;
            if (!CityLevelStack.Footprint(c, out fp)) { Debug.LogWarning("[Высоты] «" + c.name + "» не собран — сначала собери.", c); return; }
            float H = CityLevelStack.HeightOf(c);
            float dy = groundY + H - fp.min.y;
            List<Transform> pass = Passengers(c, all);
            Undo.RecordObject(c, "На землю");
            Undo.RecordObject(c.transform, "На землю");
            c.transform.position += new Vector3(0f, dy, 0f);
            for (int i = 0; i < pass.Count; i++) { Undo.RecordObject(pass[i], "На землю"); pass[i].position += new Vector3(0f, dy, 0f); }
            CityLevelStack.SetFakes(c, 1);
            EditorUtility.SetDirty(c);
            if (c is CityFloorGenerator) CityFloorGeneratorEditor.MakeFakes((CityFloorGenerator)c);
            else CityModuleHouseEditor.MakeFakes((CityModuleHouse)c);
            Debug.Log("[Высоты] «" + c.name + "» на земле (Y земли " + groundY.ToString("0.###") + "), снизу 1 фейк-этаж, пассажиров сдвинуто: " + pass.Count + ".", c);
        }

        /// <summary>Двигать один этаж (и его пассажиров) на dy метров.</summary>
        public static void MoveWithPassengers(Component c, float dy, List<Component> all)
        {
            List<Transform> pass = Passengers(c, all);
            Undo.RecordObject(c.transform, "Сдвиг этажа");
            c.transform.position += new Vector3(0f, dy, 0f);
            for (int i = 0; i < pass.Count; i++) { Undo.RecordObject(pass[i], "Сдвиг этажа"); pass[i].position += new Vector3(0f, dy, 0f); }
        }

        /// <summary>Объекты сцены, которые стоят внутри габарита этажа (не дети него) — «пассажиры».</summary>
        public static List<Transform> Passengers(Component target, List<Component> all)
        {
            List<Transform> res = new List<Transform>();
            Bounds fp;
            if (!CityLevelStack.Footprint(target, out fp)) return res;
            fp.Expand(new Vector3(2f, 0f, 2f));
            float y0 = fp.min.y - 0.5f, y1 = fp.max.y + 40f;
            Transform[] ts = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            HashSet<Transform> inside = new HashSet<Transform>();
            for (int i = 0; i < ts.Length; i++)
            {
                Transform t = ts[i];
                if (t == null || !t.gameObject.scene.IsValid() || Related(t, all)) continue;
                Vector3 p = t.position;
                if (p.x < fp.min.x || p.x > fp.max.x || p.z < fp.min.z || p.z > fp.max.z || p.y < y0 || p.y > y1) continue;
                inside.Add(t);
            }
            foreach (Transform t in inside)
            {
                if (t.parent != null && inside.Contains(t.parent)) continue;
                if (t.GetComponentInChildren<Camera>() != null || t.GetComponentInChildren<Light>() != null || t.GetComponentInChildren<Terrain>() != null) continue;
                Renderer[] rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    for (int k = 1; k < rs.Length; k++) b.Encapsulate(rs[k].bounds);
                    if (b.size.x > fp.size.x + 4f || b.size.z > fp.size.z + 4f) continue;
                }
                res.Add(t);
            }
            return res;
        }

        private static bool Related(Transform t, List<Component> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                Transform tt = all[i].transform;
                if (t == tt || t.IsChildOf(tt) || tt.IsChildOf(t)) return true;
            }
            return false;
        }

        /// <summary>Сделать пассажиров детьми объекта «Props» этого этажа (мировая позиция сохраняется).</summary>
        public static int Bind(Component c, List<Component> all)
        {
            List<Transform> pass = Passengers(c, all);
            if (pass.Count == 0) return 0;
            Transform props = c.transform.Find("Props");
            if (props == null)
            {
                GameObject go = new GameObject("Props");
                Undo.RegisterCreatedObjectUndo(go, "Пропсы этажа");
                go.transform.SetParent(c.transform, false);
                props = go.transform;
            }
            for (int i = 0; i < pass.Count; i++) Undo.SetTransformParent(pass[i], props, "Привязать пропсы");
            return pass.Count;
        }
    }

    /// <summary>Подкраска «фоток» для фейк-этажей: чуть насыщеннее и контрастнее; уникальный номер, чтобы одноимённые дома не затирали текстуры друг друга.</summary>
    public static class CityFakeTone
    {
        public static string UniqueId(Object o)
        {
            ulong id = GlobalObjectId.GetGlobalObjectIdSlow(o).targetObjectId;
            if (id == 0UL) id = (ulong)Mathf.Abs(o.GetInstanceID());
            return id.ToString();
        }

        public static void Apply(Texture2D atlas, Color32 bg)
        {
            Color32[] px = atlas.GetPixels32();
            double sr = 0, sg = 0, sb = 0; long n = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color32 p = px[i];
                if (p.r == bg.r && p.g == bg.g && p.b == bg.b) continue;
                sr += p.r; sg += p.g; sb += p.b; n++;
            }
            if (n == 0) return;
            float ar = (float)(sr / n), ag = (float)(sg / n), ab = (float)(sb / n);
            float al = ar * 0.3f + ag * 0.59f + ab * 0.11f;
            for (int i = 0; i < px.Length; i++)
            {
                Color32 p = px[i];
                if (p.r == bg.r && p.g == bg.g && p.b == bg.b) continue;
                float r = p.r, g = p.g, b = p.b;
                float l = r * 0.3f + g * 0.59f + b * 0.11f;
                r = l + (r - l) * 1.25f; g = l + (g - l) * 1.25f; b = l + (b - l) * 1.25f;      // насыщеннее
                r = al + (r - al) * 1.12f; g = al + (g - al) * 1.12f; b = al + (b - al) * 1.12f; // контрастнее
                px[i] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(r), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(g), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(b), 0, 255), p.a);
            }
            atlas.SetPixels32(px);
        }
    }
}
