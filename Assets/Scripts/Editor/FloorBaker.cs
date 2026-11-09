#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ЗАПЕКАТЕЛЬ ЭТАЖА.
///
/// ═══ ЧТО ЛЕЧИТ ═══
///
/// Модуль собран из четырнадцати материалов. Площадь из двухсот модулей — это
/// три тысячи рендереров и до трёх тысяч переключений материала за кадр.
/// Поток отрисовки упирается в процессор задолго до того, как видеокарта
/// вообще начнёт работать: рисовать нечего, всё время уходит на команды.
///
/// Никакой шейдер этого не исправит. Идеальный шейдер, вызванный три тысячи
/// раз, идёт ровно так же плохо, как плохой.
///
/// ═══ ЧТО ДЕЛАЕТ ═══
///
/// Склеивает геометрию ПО МАТЕРИАЛАМ ВНУТРИ СЕКЦИЙ. Двести модулей по
/// четырнадцать материалов превращаются в (число секций × материалы в
/// секции) — обычно это в полсотни раз меньше.
///
/// ═══ ПОЧЕМУ СЕКЦИЯМИ, А НЕ ЦЕЛИКОМ ═══
///
/// Соблазн склеить этаж в один меш велик, и это ошибка. Отсечение по пирамиде
/// видимости работает ПООБЪЕКТНО: меш либо целиком в кадре, либо целиком нет.
/// Склеенный этаж всегда «в кадре» — стоя в углу, ты будешь рисовать всю
/// площадь, включая то, что за спиной.
///
/// Получится размен: выиграл на командах, проиграл на геометрии. Секции — это
/// середина. Размер подбирается так, чтобы из любой точки было видно примерно
/// половину.
///
/// ═══ ПОЧЕМУ ИСХОДНИК НЕ УДАЛЯЕТСЯ ═══
///
/// Рендереры ВЫКЛЮЧАЮТСЯ, объекты остаются. На них висят коллайдеры, и снос
/// геометрии унёс бы всю физику этажа. К тому же запекание должно быть
/// обратимым: правишь модули, жмёшь «Восстановить», правишь ещё, печёшь
/// заново.
///
/// Меню: CatCooks ▸ Запекатель этажа
/// </summary>
public class FloorBaker : EditorWindow
{
    private Transform _source;

    private float _sectionSize = 12f;
    private string _folder = "Assets/BakedFloors";

    private bool _skipInteractive = true;
    private LayerMask _skipLayers = 0;
    private string _skipNames = "";

    private bool _stripShadows = false;
    private bool _markStatic = true;
    private bool _mergeColliders = false;

    private Vector2 _scroll;

    [MenuItem("CatCooks/Запекатель этажа")]
    private static void Open()
    {
        GetWindow<FloorBaker>("Запекатель этажа").minSize = new Vector2(400, 560);
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.HelpBox(
            "Склеивает геометрию этажа по материалам внутри секций.\n\n" +
            "Исходные модули остаются на месте — у них лишь выключаются " +
            "рендереры. Коллайдеры продолжают работать, запекание обратимо.",
            MessageType.Info);

        EditorGUILayout.Space(6);

        _source = (Transform)EditorGUILayout.ObjectField(
            new GUIContent("Что печём", "Корень этажа или площади"),
            _source, typeof(Transform), true);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Секции", EditorStyles.boldLabel);

        _sectionSize = EditorGUILayout.Slider(
            new GUIContent("Размер секции, м",
                "Меньше — лучше отсекается невидимое, но больше отрисовок.\n\n" +
                "Ориентир: из любой точки этажа должно быть видно примерно " +
                "половину секций. Для площади 15 на 15 метров это 10–15."),
            _sectionSize, 3f, 60f);

        _folder = EditorGUILayout.TextField(
            new GUIContent("Папка мешей", "Куда сохранять склеенные меши"), _folder);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Что не трогать", EditorStyles.boldLabel);

        _skipInteractive = EditorGUILayout.Toggle(
            new GUIContent("Интерактивное",
                "Пропускать объекты с PickupItem, InteractableItem, " +
                "Rigidbody или Animator.\n\n" +
                "Склеить их нельзя в принципе: склеенный предмет перестаёт " +
                "быть отдельным объектом, а значит его нельзя ни подсветить, " +
                "ни взять, ни спрятать."),
            _skipInteractive);

        _skipLayers = EditorGUILayout.MaskField(
            new GUIContent("Слои-исключения"),
            _skipLayers, UnityEditorInternal.InternalEditorUtility.layers);

        _skipNames = EditorGUILayout.TextField(
            new GUIContent("Имена содержат",
                "Через запятую. Объекты с такими кусками в имени пропускаются."),
            _skipNames);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Дополнительно", EditorStyles.boldLabel);

        _markStatic = EditorGUILayout.Toggle(
            new GUIContent("Пометить Static",
                "Нужно для окклюжена и для GPU Resident Drawer."),
            _markStatic);

        _stripShadows = EditorGUILayout.Toggle(
            new GUIContent("Снять отбрасывание теней",
                "Для холостых этажей — обязательно. Каждый теневой объект " +
                "рисуется ВТОРОЙ раз, в карту теней. На рабочем этаже тени " +
                "нужны, на холостом их всё равно не видно."),
            _stripShadows);

        _mergeColliders = EditorGUILayout.Toggle(
            new GUIContent("Собрать коллайдеры заодно",
                "Ставит MeshCollider на склеенные секции и выключает " +
                "исходные.\n\n" +
                "Осторожно: снимает физику с модулей целиком. Включай только " +
                "если у этажа нет ничего разрушаемого и подвижного."),
            _mergeColliders);

        EditorGUILayout.Space(10);

        using (new EditorGUI.DisabledScope(_source == null))
        {
            if (GUILayout.Button("Посчитать, что получится", GUILayout.Height(24)))
                Analyze();

            EditorGUILayout.Space(4);

            BakedFloor already = _source != null
                ? _source.GetComponentInParent<BakedFloor>() : null;

            if (already == null && _source != null)
                already = FindBakedFor(_source);

            if (already != null)
            {
                EditorGUILayout.HelpBox(
                    "Этот этаж уже запечён. Сначала восстанови, потом печь заново.",
                    MessageType.Warning);

                if (GUILayout.Button("Восстановить исходник", GUILayout.Height(28)))
                    Restore(already);
            }
            else
            {
                if (GUILayout.Button("ЗАПЕЧЬ", GUILayout.Height(32)))
                    Bake();
            }
        }

        EditorGUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "ПОРЯДОК РАБОТЫ\n\n" +
            "1. Собери этаж из модулей как обычно.\n" +
            "2. Запеки.\n" +
            "3. Надо поправить — восстанови, правь, запеки снова.\n\n" +
            "Печь имеет смысл в самом конце, когда расстановка устоялась: " +
            "каждая перепечка перезаписывает меши в папке.",
            MessageType.None);

        EditorGUILayout.EndScrollView();
    }

    // ═════════════════════════ АНАЛИЗ ═════════════════════════

    /// <summary>
    /// Считает, что выйдет, НЕ ТРОГАЯ сцену.
    ///
    /// Отдельная кнопка, потому что главный вопрос при настройке — размер
    /// секции, а он подбирается разменом: мельче секции — лучше отсекается
    /// невидимое, но больше отрисовок. Крутить ползунок и смотреть число
    /// быстрее, чем печь и мерить.
    /// </summary>
    private void Analyze()
    {
        List<MeshRenderer> src = Collect(_source, out int skipped);

        if (src.Count == 0)
        {
            Debug.LogWarning("[Запекатель] Нечего печь: под корнем нет " +
                             "подходящих MeshRenderer.");
            return;
        }

        Dictionary<long, HashSet<Material>> perSection = new Dictionary<long, HashSet<Material>>();

        int subMeshes = 0;
        int verts = 0;
        int casters = 0;

        foreach (MeshRenderer r in src)
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            verts += mf.sharedMesh.vertexCount;
            subMeshes += mf.sharedMesh.subMeshCount;

            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                casters++;

            long key = SectionKey(r.bounds.center);

            if (!perSection.TryGetValue(key, out HashSet<Material> set))
            {
                set = new HashSet<Material>();
                perSection[key] = set;
            }

            foreach (Material m in r.sharedMaterials)
                if (m != null) set.Add(m);
        }

        int after = 0;
        foreach (KeyValuePair<long, HashSet<Material>> kv in perSection)
            after += kv.Value.Count;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"═══ ЗАПЕКАТЕЛЬ: {_source.name} ═══\n");

        sb.AppendLine($"Рендереров сейчас:      {src.Count}");
        sb.AppendLine($"Из них подмешей:        {subMeshes}   ← столько отрисовок минимум");
        sb.AppendLine($"Пропущено (не трогаем): {skipped}");
        sb.AppendLine($"Отбрасывают тень:       {casters}   ← каждый рисуется ВТОРОЙ раз");
        sb.AppendLine($"Вершин:                 {verts:N0}");

        sb.AppendLine($"\nСекций {_sectionSize:0.#} м:        {perSection.Count}");
        sb.AppendLine($"Станет отрисовок:       {after}");

        if (after > 0)
            sb.AppendLine($"\nВЫИГРЫШ: примерно в {(float)subMeshes / after:0.#} раз");

        // Главный рычаг — не секции, а число материалов. Если в секции их
        // четырнадцать, меньше четырнадцати отрисовок с неё не выйдет, как
        // ни склеивай.
        int maxPerSection = 0;
        foreach (KeyValuePair<long, HashSet<Material>> kv in perSection)
            maxPerSection = Mathf.Max(maxPerSection, kv.Value.Count);

        sb.AppendLine($"\nМатериалов в самой пёстрой секции: {maxPerSection}");

        if (maxPerSection > 6)
        {
            sb.AppendLine("\n⚠ Это и есть потолок. Склейка не может дать меньше " +
                          "одной отрисовки на материал — сколько материалов в " +
                          "секции, столько отрисовок с неё и будет.");
            sb.AppendLine("  Дальше уменьшать имеет смысл только СЛИЯНИЕМ " +
                          "МАТЕРИАЛОВ: свести текстуры в атлас и заменить " +
                          "десяток материалов одним. Это следующий шаг, " +
                          "и он крупнее этого.");
        }

        if (verts > 4_000_000)
            sb.AppendLine($"\n⚠ Вершин очень много. Проверь в Blender, что " +
                          "нормали сглажены: у резких рёбер вершины не " +
                          "переиспользуются, и геометрия утраивается " +
                          "буквально ни за что.");

        Debug.Log(sb.ToString());
    }

    // ═════════════════════════ ЗАПЕКАНИЕ ═════════════════════════

    private void Bake()
    {
        List<MeshRenderer> src = Collect(_source, out int skipped);

        if (src.Count == 0)
        {
            Debug.LogWarning("[Запекатель] Нечего печь.");
            return;
        }

        string folder = EnsureFolder(_folder, _source.name);

        GameObject rootGO = new GameObject($"{_source.name}_BAKED");
        Undo.RegisterCreatedObjectUndo(rootGO, "Запекание этажа");

        Transform root = rootGO.transform;
        root.SetParent(_source.parent, false);
        root.position = _source.position;
        root.rotation = _source.rotation;
        root.localScale = Vector3.one;

        // ── ГРУППИРОВКА ──
        //
        // Ключ — секция плюс материал. Всё, что попало в одну группу, станет
        // одним мешем и одной отрисовкой.
        Dictionary<(long, Material), List<CombineInstance>> groups =
            new Dictionary<(long, Material), List<CombineInstance>>();

        Matrix4x4 toRoot = root.worldToLocalMatrix;

        foreach (MeshRenderer r in src)
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            Mesh mesh = mf.sharedMesh;
            Material[] mats = r.sharedMaterials;
            long key = SectionKey(r.bounds.center);

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                Material mat = sub < mats.Length ? mats[sub] : null;
                if (mat == null) continue;

                var gk = (key, mat);

                if (!groups.TryGetValue(gk, out List<CombineInstance> list))
                {
                    list = new List<CombineInstance>();
                    groups[gk] = list;
                }

                list.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = sub,

                    // Матрица ОТНОСИТЕЛЬНО запечённого корня, а не мировая.
                    // Мировая пришила бы этаж к его нынешнему месту, и
                    // передвинуть его целиком стало бы нельзя.
                    transform = toRoot * r.transform.localToWorldMatrix
                });
            }
        }

        // ── СБОРКА ──
        int made = 0;
        int totalVerts = 0;
        HashSet<long> sections = new HashSet<long>();

        int i = 0;

        foreach (KeyValuePair<(long, Material), List<CombineInstance>> kv in groups)
        {
            EditorUtility.DisplayProgressBar("Запекание этажа",
                $"{kv.Key.Item2.name}", (float)i / groups.Count);
            i++;

            sections.Add(kv.Key.Item1);

            Mesh combined = new Mesh();
            combined.name = $"{SectionName(kv.Key.Item1)}_{Sanitize(kv.Key.Item2.name)}";

            // 32-битный индекс. Склеенная секция легко переваливает за 65535
            // вершин, а на 16 битах это молча превращается в мусор.
            combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            combined.CombineMeshes(kv.Value.ToArray(), true, true);
            combined.RecalculateBounds();

            AssetDatabase.CreateAsset(combined, $"{folder}/{combined.name}.asset");

            GameObject go = new GameObject(combined.name);
            go.transform.SetParent(root, false);

            MeshFilter nf = go.AddComponent<MeshFilter>();
            nf.sharedMesh = combined;

            MeshRenderer nr = go.AddComponent<MeshRenderer>();
            nr.sharedMaterial = kv.Key.Item2;

            if (_stripShadows)
                nr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (_markStatic)
                GameObjectUtility.SetStaticEditorFlags(go,
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.BatchingStatic);

            if (_mergeColliders)
            {
                MeshCollider mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = combined;
            }

            made++;
            totalVerts += combined.vertexCount;
        }

        EditorUtility.ClearProgressBar();

        // ── ПРЯЧЕМ ИСХОДНИК ──
        //
        // Выключаем рендереры, а не объекты: выключение объекта унесло бы с
        // собой коллайдеры, точки появления, триггеры и вообще всё, что на
        // нём висит.
        foreach (MeshRenderer r in src)
        {
            Undo.RecordObject(r, "Запекание этажа");
            r.enabled = false;
        }

        if (_mergeColliders)
        {
            foreach (MeshRenderer r in src)
            {
                Collider c = r.GetComponent<Collider>();
                if (c == null) continue;

                Undo.RecordObject(c, "Запекание этажа");
                c.enabled = false;
            }
        }

        BakedFloor link = rootGO.AddComponent<BakedFloor>();
        link.source = _source;
        link.hidden = src.ToArray();
        link.sections = sections.Count;
        link.meshes = made;
        link.sourceRenderers = src.Count;
        link.vertices = totalVerts;
        link.assetFolder = folder;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.SetDirty(rootGO);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rootGO.scene);

        Selection.activeGameObject = rootGO;

        Debug.Log($"[Запекатель] «{_source.name}»: {src.Count} рендереров → " +
                  $"{made} мешей в {sections.Count} секциях, " +
                  $"{totalVerts:N0} вершин. Меши в {folder}.", rootGO);
    }

    // ═════════════════════════ ВОССТАНОВЛЕНИЕ ═════════════════════════

    private void Restore(BakedFloor baked)
    {
        if (baked == null) return;

        if (baked.hidden != null)
        {
            foreach (Renderer r in baked.hidden)
            {
                if (r == null) continue;

                Undo.RecordObject(r, "Восстановление этажа");
                r.enabled = true;

                Collider c = r.GetComponent<Collider>();

                if (c != null && !c.enabled)
                {
                    Undo.RecordObject(c, "Восстановление этажа");
                    c.enabled = true;
                }
            }
        }

        string folder = baked.assetFolder;
        GameObject go = baked.gameObject;

        Undo.DestroyObjectImmediate(go);

        // Меши сносим вместе с объектом: оставленные, они висели бы в проекте
        // мусором, на который никто не ссылается, и накапливались бы с каждой
        // перепечкой.
        if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.DeleteAsset(folder);
            AssetDatabase.Refresh();
        }

        Debug.Log("[Запекатель] Исходник восстановлен, меши удалены.");
    }

    private BakedFloor FindBakedFor(Transform src)
    {
#if UNITY_2023_1_OR_NEWER
        BakedFloor[] all = Object.FindObjectsByType<BakedFloor>(FindObjectsSortMode.None);
#else
        BakedFloor[] all = Object.FindObjectsOfType<BakedFloor>();
#endif
        foreach (BakedFloor b in all)
            if (b != null && b.source == src) return b;

        return null;
    }

    // ═════════════════════════ ВСПОМОГАТЕЛЬНОЕ ═════════════════════════

    /// <summary>
    /// ЧТО БЕРЁМ В СКЛЕЙКУ.
    ///
    /// Правила исключения не украшение. Склеенный предмет перестаёт быть
    /// отдельным объектом: его нельзя ни подсветить, ни взять, ни спрятать, ни
    /// анимировать. Всё, с чем игрок взаимодействует, обязано остаться собой.
    /// </summary>
    private List<MeshRenderer> Collect(Transform root, out int skipped)
    {
        List<MeshRenderer> list = new List<MeshRenderer>();
        skipped = 0;

        if (root == null) return list;

        string[] names = _skipNames.Split(',');

        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (r == null || !r.enabled) continue;

            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            // Меш без чтения из скрипта склеить нельзя: данные вершин лежат
            // только в видеопамяти. Лечится галкой Read/Write в импорте.
            if (!mf.sharedMesh.isReadable)
            {
                Debug.LogWarning($"[Запекатель] «{mf.sharedMesh.name}» без " +
                                 "Read/Write Enabled — пропущен. Включи галку " +
                                 "в настройках импорта модели.", r);
                skipped++;
                continue;
            }

            if (((1 << r.gameObject.layer) & _skipLayers) != 0) { skipped++; continue; }

            if (_skipInteractive && IsInteractive(r.transform)) { skipped++; continue; }

            bool byName = false;

            foreach (string n in names)
            {
                string trimmed = n.Trim();

                if (trimmed.Length > 0 &&
                    r.name.IndexOf(trimmed, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    byName = true;
                    break;
                }
            }

            if (byName) { skipped++; continue; }

            list.Add(r);
        }

        return list;
    }

    private bool IsInteractive(Transform t)
    {
        if (t.GetComponentInParent<Rigidbody>() != null) return true;
        if (t.GetComponentInParent<Animator>() != null) return true;

        // По имени типа, а не по ссылке: запекатель не должен требовать, чтобы
        // все игровые классы существовали в проекте.
        foreach (MonoBehaviour mb in t.GetComponentsInParent<MonoBehaviour>(true))
        {
            if (mb == null) continue;

            string n = mb.GetType().Name;

            if (n.Contains("Interactable") || n.Contains("Pickup") ||
                n.Contains("Interactive") || n.Contains("Station"))
                return true;
        }

        return false;
    }

    private long SectionKey(Vector3 worldPos)
    {
        Vector3 local = _source != null
            ? _source.InverseTransformPoint(worldPos) : worldPos;

        float s = Mathf.Max(0.5f, _sectionSize);

        long x = (long)Mathf.Floor(local.x / s);
        long z = (long)Mathf.Floor(local.z / s);
        long y = (long)Mathf.Floor(local.y / s);

        return (x + 4096) * 16777216L + (y + 4096) * 4096L + (z + 4096);
    }

    private string SectionName(long key)
    {
        long z = key % 4096L - 4096;
        long y = (key / 4096L) % 4096L - 4096;
        long x = key / 16777216L - 4096;

        return $"S{x}_{y}_{z}";
    }

    private static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');

        return s.Replace(' ', '_');
    }

    private static string EnsureFolder(string baseFolder, string sub)
    {
        if (!AssetDatabase.IsValidFolder(baseFolder))
        {
            string parent = Path.GetDirectoryName(baseFolder).Replace('\\', '/');
            string leaf = Path.GetFileName(baseFolder);

            if (!AssetDatabase.IsValidFolder(parent)) parent = "Assets";

            AssetDatabase.CreateFolder(parent, leaf);
        }

        string full = $"{baseFolder}/{Sanitize(sub)}";

        if (AssetDatabase.IsValidFolder(full))
        {
            AssetDatabase.DeleteAsset(full);
            AssetDatabase.Refresh();
        }

        AssetDatabase.CreateFolder(baseFolder, Sanitize(sub));
        return full;
    }
}
#endif
