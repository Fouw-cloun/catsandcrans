using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>Зона дождя: точка (объект) + размер в инспекторе. Двигаешь обычным инструментом Move.</summary>
    [CustomEditor(typeof(CityRainZone))]
    public class CityRainZoneEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CityRainZone z = (CityRainZone)target;
            if (z.Kind == CityRainZone.ZoneKind.Light)
                EditorGUILayout.HelpBox("ЖЁЛТАЯ зона (лёгкая): рядом с этажом, внутри неё погода слабее (Light Strength) и дешевле: " +
                                        "капель и хлопьев втрое меньше, без всплесков, завес, капели, паров и сугробов, экран считается облегчённо. " +
                                        "Красная зона должна лежать внутри жёлтой. Вне всех зон осадков, мокрого, льда и града нет — остаются туман и облака.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Зона — коробка вокруг этого объекта. Поставь объект в центр красного модуля (можно дочерним к модулю) " +
                                        "и задай Size: ширина X, высота, глубина Z. Двигай обычным инструментом Move. " +
                                        "Красная зона — вся погода; вокруг неё можно поставить жёлтую (кнопка у City Rain).", MessageType.Info);
            DrawDefaultInspector();
            if (z.Kind == CityRainZone.ZoneKind.Light)
            {
                CityRainZone red = null;
                foreach (CityRainZone o in Object.FindObjectsByType<CityRainZone>(FindObjectsSortMode.None))
                    if (o != z && o.Kind == CityRainZone.ZoneKind.Full && (red == null || o.AlwaysActive)) red = o;
                using (new EditorGUI.DisabledScope(red == null))
                    if (GUILayout.Button("Охватить красную зону с запасом 60 м" + (red != null ? " («" + red.name + "»)" : " — красной зоны нет"))) FitAround(z, red, 60f);
                return;
            }
            if (GUILayout.Button("Подогнать размер по коллайдерам внутри рамки"))
            {
                Undo.RecordObjects(new Object[] { z, z.transform }, "Зона дождя");
                FitToColliders(z);
            }
        }

        /// <summary>Жёлтая зона: рамка красной + запас вокруг (в метрах), красную можно не трогать.</summary>
        public static void FitAround(CityRainZone light, CityRainZone red, float padMeters)
        {
            float m = 3.5f;
            CityProps p = Object.FindFirstObjectByType<CityProps>();
            if (p != null && p.Meter() > 0.01f) m = p.Meter();
            Undo.RecordObjects(new Object[] { light, light.transform }, "Жёлтая зона");
            light.transform.position = red.transform.position;
            light.Size = new Vector3(red.Size.x + 2f * padMeters * m, red.Size.y, red.Size.z + 2f * padMeters * m);
            light.Fade = Mathf.Max(5f, padMeters * 0.5f * m);
        }

        /// <summary>Рамка по коллайдерам, которые сейчас внутри неё (этаж, навесы).</summary>
        public static void FitToColliders(CityRainZone z)
        {
            Bounds cur = z.WorldBounds;
            bool any = false;
            Bounds b = new Bounds();
            foreach (Collider col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (col.isTrigger || !col.enabled || !cur.Intersects(col.bounds)) continue;
                if (!any) { b = col.bounds; any = true; } else b.Encapsulate(col.bounds);
            }
            if (!any) { Debug.LogWarning("[Дождь] внутри рамки нет коллайдеров — подвинь рамку на этаж", z); return; }
            b.Expand(2f);
            z.transform.position = b.center;
            z.Size = b.size;
            Debug.Log("[Дождь] рамка подогнана по коллайдерам: " + b.size, z);
        }
    }

    /// <summary>City Rain: какие зоны есть в сцене + создать зону.</summary>
    [CustomEditor(typeof(CityRain))]
    public class CityRainInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Зоны дождя в сцене", EditorStyles.boldLabel);
            CityRainZone[] all = Object.FindObjectsByType<CityRainZone>(FindObjectsSortMode.None);
            if (all.Length == 0) EditorGUILayout.HelpBox("Зон нет — нажми «+ Зона 1» и поставь её в красный модуль.", MessageType.Warning);
            foreach (CityRainZone z in all)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(z.name + (z.Kind == CityRainZone.ZoneKind.Light ? " — ЖЁЛТАЯ (лёгкая)" : z.AlwaysActive ? " — всегда" : " — когда игрок внутри") + "   " + z.Size.x.ToString("0") + "×" + z.Size.y.ToString("0") + "×" + z.Size.z.ToString("0"));
                if (GUILayout.Button("Выбрать", GUILayout.Width(70))) Selection.activeObject = z.gameObject;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Зона 1 (всегда)")) Add(true);
            if (GUILayout.Button("+ Зона 2 (по входу)")) Add(false);
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("+ Жёлтая зона (лёгкая, вокруг красной)")) AddLight();
            EditorGUILayout.Space(6);
            DrawDefaultInspector();
        }

        /// <summary>Жёлтая зона вокруг первой красной (или в центре вида), с запасом 60 м.</summary>
        public static CityRainZone AddLight()
        {
            CityRainZone[] all = Object.FindObjectsByType<CityRainZone>(FindObjectsSortMode.None);
            CityRainZone red = null;
            foreach (CityRainZone o in all) if (o.Kind == CityRainZone.ZoneKind.Full && (red == null || o.AlwaysActive)) red = o;
            SceneView sv = SceneView.lastActiveSceneView;
            GameObject go = new GameObject("Rain Zone Yellow");
            Undo.RegisterCreatedObjectUndo(go, "Жёлтая зона");
            CityRainZone z = go.AddComponent<CityRainZone>();
            z.Kind = CityRainZone.ZoneKind.Light;
            z.AlwaysActive = true;
            if (red != null) { CityRainZoneEditor.FitAround(z, red, 60f); }
            else
            {
                go.transform.position = sv != null ? sv.pivot : Vector3.zero;
                z.Size = new Vector3(600f, 400f, 600f);
                Debug.LogWarning("[Погода] красной зоны нет — жёлтая поставлена в центр вида. Поставь «+ Зона 1» и нажми «Охватить красную зону».", z);
            }
            Selection.activeObject = go;
            return z;
        }

        public static CityRainZone Add(bool always)
        {
            SceneView sv = SceneView.lastActiveSceneView;
            Vector3 c = sv != null ? sv.pivot : Vector3.zero;
            float m = 250f;
            CityBlockGenerator g = Object.FindFirstObjectByType<CityBlockGenerator>();
            if (g != null) m = Mathf.Min(g.ModuleSize().x, g.ModuleSize().y);
            int n = Object.FindObjectsByType<CityRainZone>(FindObjectsSortMode.None).Length + 1;
            GameObject go = new GameObject("Rain Zone " + n);
            Undo.RegisterCreatedObjectUndo(go, "Зона дождя");
            go.transform.position = c;
            CityRainZone z = go.AddComponent<CityRainZone>();
            z.Size = new Vector3(m, m * 1.4f, m);
            z.AlwaysActive = always;
            Selection.activeObject = go;
            return z;
        }
    }
}
