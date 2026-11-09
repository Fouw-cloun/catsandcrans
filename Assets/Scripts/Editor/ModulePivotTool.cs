#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ПИВОТЫ МОДУЛЕЙ — привести к одному правилу.
///
/// ═══ ЗАЧЕМ ═══
///
/// Числа в инспекторе — это положение ПИВОТА объекта, а не того, что видно на
/// экране. Между пивотом и геометрией может быть какое угодно расстояние: оно
/// накапливается само, пока модуль собирают — что-то подвинули в Blender,
/// что-то дособрали в сцене, корень создали не там, где оказался этаж.
///
/// Пока модуль один, это никого не трогает. Как только модулей два, начинается
/// то самое: одинаковые числа ставят их в разные места, разные числа — в одно.
/// Собрать из таких модулей здание нельзя, потому что «поставить второй этаж
/// ровно над первым» перестаёт быть арифметикой.
///
/// ═══ ЧТО ДЕЛАЕТ ИНСТРУМЕНТ ═══
///
/// Двигает КОРЕНЬ в выбранную точку габаритов и на столько же двигает детей в
/// обратную сторону. Картинка не шевелится — меняется только точка отсчёта.
/// После этого у всех модулей пивот означает одно и то же, и координаты
/// наконец сравнимы.
///
/// ═══ ЧТО ВЫБРАТЬ ═══
///
/// Для этажей и модулей, которые ставятся друг на друга, — НИЗ, УГОЛ. Тогда
/// «поставить этаж на этаж» это прибавить высоту этажа к Y, и ничего больше.
/// Центр удобен для отдельных предметов, но для сборки хуже: у модулей разной
/// высоты центры оказываются на разной высоте, и стыковка снова превращается
/// в подгонку.
///
/// Меню: CatCooks ▸ Пивоты модулей
/// </summary>
public class ModulePivotTool : EditorWindow
{
    private enum Anchor
    {
        [InspectorName("Низ, угол (для этажей)")] BottomMinCorner = 0,
        [InspectorName("Низ, центр")] BottomCenter = 1,
        [InspectorName("Центр габаритов")] BoundsCenter = 2,
        [InspectorName("Угол габаритов")] MinCorner = 3,
    }

    private Anchor _anchor = Anchor.BottomMinCorner;

    private bool _snap = true;
    private Vector3 _grid = new Vector3(1f, 1f, 1f);
    private bool _includeInactive = true;

    private Vector2 _scroll;

    [MenuItem("CatCooks/Пивоты модулей")]
    private static void Open()
    {
        GetWindow<ModulePivotTool>("Пивоты модулей").minSize = new Vector2(360, 380);
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.HelpBox(
            "Числа в инспекторе — это положение ПИВОТА, а не геометрии.\n\n" +
            "Если у двух модулей пивоты стоят по-разному относительно " +
            "постройки, одинаковые координаты поставят их в разные места. " +
            "Инструмент переносит пивот в одну и ту же точку габаритов у всех " +
            "выделенных объектов, не сдвигая саму картинку.",
            MessageType.Info);

        EditorGUILayout.Space(6);

        _anchor = (Anchor)EditorGUILayout.EnumPopup("Куда ставить пивот", _anchor);

        _includeInactive = EditorGUILayout.Toggle(
            new GUIContent("Считать выключенные",
                "Учитывать выключенные рендереры в габаритах. Включено — " +
                "габариты не поедут, если что-то временно спрятано."),
            _includeInactive);

        EditorGUILayout.Space(4);

        _snap = EditorGUILayout.Toggle(
            new GUIContent("Ставить на сетку",
                "После переноса пивота округлить координаты корня.\n\n" +
                "Это второе, без чего модульная сборка не живёт: пивот в " +
                "правильном месте, но по координате 2613.6 всё равно не " +
                "стыкуется с 2614.0."),
            _snap);

        using (new EditorGUI.DisabledScope(!_snap))
            _grid = EditorGUILayout.Vector3Field("Шаг сетки", _grid);

        EditorGUILayout.Space(10);

        Transform[] sel = Selection.transforms;

        EditorGUILayout.LabelField($"Выделено: {sel.Length}", EditorStyles.boldLabel);

        if (sel.Length == 0)
            EditorGUILayout.HelpBox("Выдели корни модулей в иерархии.", MessageType.None);

        using (new EditorGUI.DisabledScope(sel.Length == 0))
        {
            if (GUILayout.Button("Показать, что не так", GUILayout.Height(24)))
                Report(sel);

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Перенести пивоты", GUILayout.Height(30)))
                Apply(sel);
        }

        EditorGUILayout.Space(8);

        EditorGUILayout.HelpBox(
            "ПРЕФАБЫ. Если модуль — экземпляр префаба, правь его в режиме " +
            "префаба, а не в сцене. Иначе сдвиг детей ляжет переопределениями " +
            "на один экземпляр, и следующий встанет по-старому — то есть " +
            "ровно та же беда, только теперь ещё и невидимая.",
            MessageType.Warning);

        EditorGUILayout.EndScrollView();
    }

    // ───────────────────────── диагностика ─────────────────────────

    /// <summary>
    /// Печатает, НАСКОЛЬКО пивот разошёлся с геометрией у каждого выделенного.
    /// Именно эта величина и есть ответ на вопрос «почему одинаковые числа
    /// ставят модули в разные места»: сравнивать надо не координаты, а вот эти
    /// смещения.
    /// </summary>
    private void Report(Transform[] sel)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ ПИВОТЫ МОДУЛЕЙ ═══\n");

        Vector3 first = Vector3.zero;
        bool hasFirst = false;

        foreach (Transform t in sel)
        {
            if (t == null) continue;

            sb.AppendLine($"── {t.name} ──");

            if (t.parent != null)
            {
                sb.AppendLine($"   ⚠ лежит внутри «{t.parent.name}».");
                sb.AppendLine("     Инспектор показывает МЕСТНЫЕ координаты, " +
                              "от родителя. У модулей с разными родителями " +
                              "числа несравнимы в принципе, даже если пивоты " +
                              "стоят правильно.");
            }

            sb.AppendLine($"   пивот в мире: {Fmt(t.position)}");

            if (!TryBounds(t, out Bounds b))
            {
                sb.AppendLine("   ❌ нет ни одного рендерера — габариты неизвестны\n");
                continue;
            }

            Vector3 off = b.center - t.position;

            sb.AppendLine($"   габариты: {Fmt(b.size)}");
            sb.AppendLine($"   пивот от центра постройки: {Fmt(off)}");
            sb.AppendLine($"   пивот от нижнего угла:     {Fmt(b.min - t.position)}");

            if (!hasFirst) { first = b.min - t.position; hasFirst = true; }
            else
            {
                Vector3 diff = (b.min - t.position) - first;

                if (diff.magnitude > 0.01f)
                {
                    sb.AppendLine($"   ❗ расходится с первым выделенным на {Fmt(diff)}");
                    sb.AppendLine("      Ровно на столько и промахиваются " +
                                  "одинаковые координаты.");
                }
                else
                {
                    sb.AppendLine("   ✔ совпадает с первым выделенным");
                }
            }

            sb.AppendLine();
        }

        Debug.Log(sb.ToString());
    }

    // ───────────────────────── перенос ─────────────────────────

    private void Apply(Transform[] sel)
    {
        int done = 0;

        foreach (Transform root in sel)
        {
            if (root == null) continue;

            if (!TryBounds(root, out Bounds b))
            {
                Debug.LogWarning($"[Пивоты] «{root.name}»: нет рендереров, " +
                                 "переносить не от чего.", root);
                continue;
            }

            Vector3 target = AnchorPoint(b, _anchor);

            if (_snap)
            {
                target.x = Snap(target.x, _grid.x);
                target.y = Snap(target.y, _grid.y);
                target.z = Snap(target.z, _grid.z);
            }

            MovePivot(root, target);
            done++;
        }

        Debug.Log($"[Пивоты] перенесено: {done}. Картинка не двигалась — " +
                  "сменилась только точка отсчёта.");
    }

    /// <summary>
    /// ПЕРЕНОС ПИВОТА.
    ///
    /// Мировые позы детей запоминаются ДО того, как корень поедет, и
    /// восстанавливаются после. Так дети остаются ровно там, где были, а
    /// сдвинулась только точка отсчёта.
    ///
    /// Считать смещение и вычитать его из детей было бы короче и хуже:
    /// поворот и масштаб корня сделали бы это вычитание неверным. Запись
    /// готовой мировой позы работает при любом корне.
    /// </summary>
    private static void MovePivot(Transform root, Vector3 worldAnchor)
    {
        List<Transform> kids = new List<Transform>();
        foreach (Transform c in root) kids.Add(c);

        Undo.RecordObject(root, "Перенос пивота");
        foreach (Transform c in kids) Undo.RecordObject(c, "Перенос пивота");

        Vector3[] pos = new Vector3[kids.Count];
        Quaternion[] rot = new Quaternion[kids.Count];

        for (int i = 0; i < kids.Count; i++)
        {
            pos[i] = kids[i].position;
            rot[i] = kids[i].rotation;
        }

        root.position = worldAnchor;

        for (int i = 0; i < kids.Count; i++)
        {
            kids[i].position = pos[i];
            kids[i].rotation = rot[i];
        }

        EditorUtility.SetDirty(root);
    }

    // ───────────────────────── вспомогательное ─────────────────────────

    private static Vector3 AnchorPoint(Bounds b, Anchor a)
    {
        switch (a)
        {
            case Anchor.BottomMinCorner: return new Vector3(b.min.x, b.min.y, b.min.z);
            case Anchor.BottomCenter: return new Vector3(b.center.x, b.min.y, b.center.z);
            case Anchor.MinCorner: return b.min;
            default: return b.center;
        }
    }

    private bool TryBounds(Transform root, out Bounds b)
    {
        b = new Bounds();
        bool any = false;

        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(_includeInactive))
        {
            if (r == null) continue;

            // Частицы и следы в габариты не берём: их объём живёт своей
            // жизнью и в редакторе зависит от того, когда на них посмотрели.
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;

            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }

        return any;
    }

    private static float Snap(float v, float step)
        => step <= 0.0001f ? v : Mathf.Round(v / step) * step;

    private static string Fmt(Vector3 v)
        => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
}
#endif
