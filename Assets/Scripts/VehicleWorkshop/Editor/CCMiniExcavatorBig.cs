#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// v28.5: мини-экскаватор ×2 и дисковая пила.
///
/// Размер: масштаб КОРНЯ машины → все модели, коллайдеры, насадки, кончики, ковш-тара, точки посадки/выхода/камеры
/// растут вместе. То, что задано в метрах в настройках, пересчитывается: подвеска (длина, радиус, пружины, демпфер),
/// масса (×4), тяга, центр масс, грузоподъёмность, скорость (×1.4). Дальности насадок код сам умножает на масштаб.
/// Повторный запуск ничего не удваивает: целевой размер ставится от текущего.
/// </summary>
public static class CCMiniExcavatorBig
{
    private const float Target = 2f;

    [MenuItem("CatCooks/Vehicle Workshop/06b Мини-экскаватор: увеличить ×2 + дисковая пила")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Мини-экскаватор", "Выйди из Play Mode.", "OK"); return; }
        var log = new StringBuilder("МИНИ-ЭКСКАВАТОР → ×2 + дисковая пила\n");
        int n = 0;

        // 1) префабы
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject a = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (a == null || a.GetComponentInChildren<CCMiniExcavatorVehicle>(true) == null) continue;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var v in root.GetComponentsInChildren<CCMiniExcavatorVehicle>(true))
                {
                    log.AppendLine($"• префаб {path}:");
                    Resize(v, log);
                    AddSaw(v, log);
                    n++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // 2) сцена: свои (не из префаба) — целиком; экземпляры префаба — только сброс своего масштаба на префабный
        foreach (var v in Object.FindObjectsByType<CCMiniExcavatorVehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (PrefabUtility.IsPartOfPrefabInstance(v))
            {
                Transform r = PrefabUtility.GetOutermostPrefabInstanceRoot(v.gameObject).transform;
                var so = new SerializedObject(r);
                var sp = so.FindProperty("m_LocalScale");
                if (sp != null && sp.prefabOverride) { PrefabUtility.RevertPropertyOverride(sp, InteractionMode.UserAction); log.AppendLine($"• сцена «{v.name}»: свой масштаб сброшен → как у префаба"); }
                else log.AppendLine($"• сцена «{v.name}»: из префаба — обновился вместе с ним");
                EditorSceneManager.MarkSceneDirty(v.gameObject.scene);
                n++;
                continue;
            }
            log.AppendLine($"• сцена «{v.name}»:");
            Undo.RegisterFullObjectHierarchyUndo(v.gameObject, "Mini excavator x2");
            Resize(v, log);
            AddSaw(v, log);
            EditorUtility.SetDirty(v);
            EditorSceneManager.MarkSceneDirty(v.gameObject.scene);
            n++;
        }

        AssetDatabase.SaveAssets();
        if (n == 0) log.AppendLine("⚠ мини-экскаватор не найден (ни префаба, ни в сцене)");
        else log.AppendLine("\nДальше: сохрани сцену. В игре: Tab — работа, 9 — дисковая пила, Y — крутить. Траншеекопатель (4) тоже пилит тушу.");
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Мини-экскаватор", n == 0 ? "Не нашёл мини-экскаватор." : $"Готово: {n}. Подробно — в консоли.", "OK");
    }

    private static void Resize(CCMiniExcavatorVehicle v, StringBuilder log)
    {
        Transform t = v.transform;
        float cur = t.localScale.x;
        if (Mathf.Abs(cur - Target) < 1e-3f) { log.AppendLine($"  уже ×{Target:0.#} — размер не трогаю"); return; }
        float f = Target / Mathf.Max(.01f, cur);
        t.localScale = t.localScale * f;

        v.restLength *= f;
        v.trackRadius *= f;
        float m2 = f * f;                               // масса ×f² — машина тяжелее, но не «чугунная»
        v.springStrength *= m2 / f;                      // прогиб в метрах ×f (пропорционально размеру)
        v.springDamper *= Mathf.Sqrt((m2 / f) * m2);
        v.driveForce *= m2;
        v.turnForce *= m2;
        v.maxForwardSpeed *= Mathf.Sqrt(f);
        v.maxReverseSpeed *= Mathf.Sqrt(f);
        v.maxCargoMass *= m2 * f;
        var so = new SerializedObject(v);
        var com = so.FindProperty("_centerOfMass");
        if (com != null) com.vector3Value *= f;
        so.ApplyModifiedPropertiesWithoutUndo();
        Rigidbody rb = v.GetComponent<Rigidbody>();
        if (rb != null) { rb.mass *= m2; EditorUtility.SetDirty(rb); }
        EditorUtility.SetDirty(v); EditorUtility.SetDirty(t);
        log.AppendLine($"  размер ×{f:0.##} → {Target:0.#}: подвеска {v.restLength:0.00}/{v.trackRadius:0.00} м, масса {(rb != null ? rb.mass : 0):0} кг, " +
                       $"скорость {v.maxForwardSpeed:0.0} м/с, груз до {v.maxCargoMass:0} кг");
    }

    // ── дисковая пила: рядом с траншеекопателем, на том же креплении ──
    private static void AddSaw(CCMiniExcavatorVehicle v, StringBuilder log)
    {
        var list = new List<CCMiniExcavatorAttachment>(v.attachments ?? new CCMiniExcavatorAttachment[0]);
        foreach (var a in list) if (a != null && a.kind == CCMiniTool.Saw) { log.AppendLine("  дисковая пила уже есть"); return; }
        CCMiniExcavatorAttachment trencher = list.Find(a => a != null && a.kind == CCMiniTool.Trencher);
        if (trencher == null) { log.AppendLine("  ⚠ нет траншеекопателя — не к чему прицепить пилу"); return; }
        while (list.Count < (int)CCMiniTool.Saw) list.Add(null);

        Material metal = null, dark = null;
        foreach (Renderer r in trencher.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterial == null) continue;
            if (metal == null) metal = r.sharedMaterial;
            else if (dark == null && r.sharedMaterial != metal) dark = r.sharedMaterial;
        }
        if (dark == null) dark = metal;

        var saw = new GameObject("Saw");
        saw.layer = trencher.gameObject.layer;
        saw.transform.SetParent(trencher.transform.parent, false);
        saw.transform.localPosition = trencher.transform.localPosition;
        saw.transform.localRotation = trencher.transform.localRotation;
        saw.transform.localScale = Vector3.one;

        Vector3 center = new Vector3(0f, -.42f, .40f);
        const float R = .3f;
        // кронштейн от крепления к оси диска
        Prim(PrimitiveType.Cube, "Saw_Arm", saw.transform, center * .5f + new Vector3(.07f, 0f, 0f),
             Quaternion.LookRotation(center.normalized, Vector3.up), new Vector3(.07f, .09f, center.magnitude + .08f), dark);
        Prim(PrimitiveType.Cylinder, "Saw_Hub", saw.transform, center + new Vector3(.06f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f),
             new Vector3(.12f, .035f, .12f), dark);
        // кожух над диском
        Prim(PrimitiveType.Cube, "Saw_Guard", saw.transform, center + new Vector3(0f, .17f, -.03f), Quaternion.Euler(20f, 0f, 0f),
             new Vector3(.05f, .05f, R * 1.7f), dark);

        var drive = new GameObject("Saw_Drive").transform;
        drive.SetParent(saw.transform, false);
        drive.localPosition = center;
        drive.gameObject.layer = saw.layer;
        Prim(PrimitiveType.Cylinder, "Saw_Disc", drive, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(R * 2f, .008f, R * 2f), metal);
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f;
            Vector3 p = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * (R + .012f);
            Quaternion q = Quaternion.AngleAxis(-a * Mathf.Rad2Deg, Vector3.right) * Quaternion.Euler(0f, 0f, 0f);
            Prim(PrimitiveType.Cube, $"Saw_Tooth{i:00}", drive, p, q * Quaternion.Euler(35f, 0f, 0f), new Vector3(.012f, .03f, .022f), dark);
        }

        var tip = new GameObject("Saw_Tip").transform;
        tip.SetParent(saw.transform, false);
        tip.localPosition = center + new Vector3(0f, -.62f, .62f) * R;          // нижний передний край диска
        tip.gameObject.layer = saw.layer;

        var tool = saw.AddComponent<CCMiniExcavatorAttachment>();
        tool.kind = CCMiniTool.Saw;
        tool.reach = .32f;
        tool.tip = tip;
        tool.drivePart = drive;
        if (list.Count > (int)CCMiniTool.Saw) list[(int)CCMiniTool.Saw] = tool; else list.Add(tool);
        v.attachments = list.ToArray();
        saw.SetActive(false);
        var help = v.GetComponent<CCVehicleHelp>();
        if (help != null && help.text != null) { help.text = help.text.Replace("1–8: насадки", "1–9: насадки (9 — дисковая пила)"); EditorUtility.SetDirty(help); }
        EditorUtility.SetDirty(v);
        log.AppendLine("  ＋ дисковая пила (насадка 9): диск ⌀0.6 м (×масштаб), 24 зуба, кончик — нижний передний край");
    }

    private static void Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());        // без коллайдеров: пила не толкает тушу, пилит по кончику
        g.layer = parent.gameObject.layer;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localRotation = rot;
        g.transform.localScale = scale;
        if (mat != null) g.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
#endif
