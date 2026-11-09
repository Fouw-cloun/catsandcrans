#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// РЕДАКТОР ФОРМ ЕДЫ. Меню: CatCooks -> Редактор форм еды.
///
/// Рисует граф состояний мышкой, чтобы не заполнять индексы From/To руками
/// в инспекторе — там при десятке состояний ошибиться проще, чем попасть.
///
/// Управление:
///   ЛКМ по узлу и тащить   — двигать состояние
///   ЛКМ по пустому и тащить — двигать полотно
///   ПКМ по узлу            — начать переход / удалить состояние
///   ПКМ по пустому         — добавить состояние
///   ЛКМ по подписи ребра   — выделить переход (справа появятся настройки)
///
/// Лежит в папке Editor — обязательно, UnityEditor в билд не входит.
/// </summary>
public class FoodFormWindow : EditorWindow
{
    private const float NodeW = 150f;
    private const float NodeH = 52f;
    private const float PanelW = 260f;

    private FoodFormDef _form;
    private SerializedObject _so;

    private Vector2 _pan;
    private int _dragNode = -1;
    private Vector2 _dragOffset;
    private bool _panning;

    private int _linkFrom = -1;      // от какого узла тянем новое ребро
    private int _selState = -1;
    private int _selTrans = -1;

    [MenuItem("CatCooks/Редактор форм еды")]
    public static void Open()
    {
        FoodFormWindow w = GetWindow<FoodFormWindow>("Формы еды");
        w.minSize = new Vector2(720f, 420f);
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (_form == null)
        {
            EditorGUILayout.HelpBox(
                "Выбери ассет формы сверху.\n\n" +
                "Нет ни одного? Create -> CatCooks -> Food Form.",
                MessageType.Info);
            return;
        }

        if (_so == null || _so.targetObject != _form) _so = new SerializedObject(_form);
        _so.Update();

        Rect graphRect = new Rect(0f, 20f, position.width - PanelW, position.height - 20f);
        Rect panelRect = new Rect(position.width - PanelW, 20f, PanelW, position.height - 20f);

        DrawGraph(graphRect);

        // DrawGraph мог изменить _form.Transitions/_States НАПРЯМУЮ (создание
        // и удаление переходов работает через прямую мутацию массива, не через
        // SerializedProperty — так проще для геометрии графа). Без повторного
        // Update() снимок SerializedObject на кадр отстаёт от реального массива:
        // GetArrayElementAtIndex на новом/удалённом индексе вернёт null,
        // и панель ниже упадёт в NullReferenceException.
        _so.Update();

        DrawPanel(panelRect);

        _so.ApplyModifiedProperties();
    }

    // ───────────────────────── ВЕРХНЯЯ ПАНЕЛЬ ─────────────────────────

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUI.BeginChangeCheck();
        _form = (FoodFormDef)EditorGUILayout.ObjectField(
            _form, typeof(FoodFormDef), false, GUILayout.Width(220f));
        if (EditorGUI.EndChangeCheck())
        {
            _so = null; _selState = -1; _selTrans = -1; _linkFrom = -1;
        }

        if (_form != null)
        {
            GUILayout.Label($"состояний: {Count(_form.States)}   переходов: {Count(_form.Transitions)}",
                            EditorStyles.miniLabel);
        }

        GUILayout.FlexibleSpace();

        if (_linkFrom >= 0)
        {
            GUILayout.Label("выбери, КУДА ведёт переход (Esc — отмена)", EditorStyles.miniLabel);
        }

        if (GUILayout.Button("Разложить", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            AutoLayout();

        EditorGUILayout.EndHorizontal();
    }

    private static int Count<T>(T[] a) => a?.Length ?? 0;

    // ───────────────────────── ГРАФ ─────────────────────────

    private void DrawGraph(Rect area)
    {
        GUI.Box(area, GUIContent.none);
        GUI.BeginClip(area);

        DrawGrid(area.size, 20f, 0.12f);
        DrawGrid(area.size, 100f, 0.2f);

        DrawTransitions();
        DrawNodes();
        DrawPendingLink();

        HandleGraphEvents(area);

        GUI.EndClip();
    }

    private void DrawGrid(Vector2 size, float step, float alpha)
    {
        Handles.BeginGUI();
        Handles.color = new Color(0f, 0f, 0f, alpha);

        Vector2 off = new Vector2(_pan.x % step, _pan.y % step);
        for (float x = off.x; x < size.x; x += step)
            Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, size.y));
        for (float y = off.y; y < size.y; y += step)
            Handles.DrawLine(new Vector3(0f, y), new Vector3(size.x, y));

        Handles.color = Color.white;
        Handles.EndGUI();
    }

    private Rect NodeRect(int i)
    {
        Vector2 p = _form.States[i].EditorPos + _pan;
        return new Rect(p.x, p.y, NodeW, NodeH);
    }

    private void DrawNodes()
    {
        if (_form.States == null) return;

        for (int i = 0; i < _form.States.Length; i++)
        {
            Rect r = NodeRect(i);
            bool isBase = i == 0;

            Color bg = isBase ? new Color(0.30f, 0.42f, 0.30f)
                              : new Color(0.26f, 0.26f, 0.30f);
            if (i == _selState) bg = new Color(0.36f, 0.44f, 0.58f);

            EditorGUI.DrawRect(r, bg);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2f),
                               isBase ? new Color(0.5f, 0.8f, 0.5f) : new Color(0.45f, 0.45f, 0.5f));

            string label = string.IsNullOrEmpty(_form.States[i].Label)
                ? $"состояние {i}" : _form.States[i].Label;

            GUI.Label(new Rect(r.x + 8f, r.y + 6f, r.width - 16f, 18f),
                      label, EditorStyles.whiteBoldLabel);

            string sub = isBase ? "базовое (спавн)" : $"индекс {i}";
            if (_form.States[i].Material == null) sub += "  БЕЗ МАТЕРИАЛА";
            GUI.Label(new Rect(r.x + 8f, r.y + 26f, r.width - 16f, 18f),
                      sub, EditorStyles.miniLabel);
        }

        // Гниль стоит особняком: в неё нет рёбер, она ставится флагом.
        Rect rot = new Rect(10f, position.height - 92f, NodeW, NodeH);
        EditorGUI.DrawRect(rot, new Color(0.38f, 0.28f, 0.24f));
        GUI.Label(new Rect(rot.x + 8f, rot.y + 6f, rot.width - 16f, 18f),
                  "гнилое", EditorStyles.whiteBoldLabel);
        GUI.Label(new Rect(rot.x + 8f, rot.y + 26f, rot.width - 16f, 18f),
                  "вне графа, флагом", EditorStyles.miniLabel);
    }

    private void DrawTransitions()
    {
        if (_form.Transitions == null || _form.States == null) return;

        Handles.BeginGUI();
        for (int i = 0; i < _form.Transitions.Length; i++)
        {
            FoodTransition t = _form.Transitions[i];
            if (!Valid(t.From) || !Valid(t.To)) continue;

            Rect a = NodeRect(t.From);
            Rect b = NodeRect(t.To);

            Vector2 p0 = new Vector2(a.xMax, a.center.y);
            Vector2 p1 = new Vector2(b.xMin, b.center.y);

            // Петля сама в себя рисуется дугой сверху, иначе её не видно.
            if (t.From == t.To)
            {
                p0 = new Vector2(a.center.x - 20f, a.yMin);
                p1 = new Vector2(a.center.x + 20f, a.yMin);
            }

            Color c = i == _selTrans ? new Color(0.4f, 0.7f, 1f) : new Color(0.75f, 0.75f, 0.8f);
            Handles.DrawBezier(p0, p1,
                               p0 + Vector2.right * 50f, p1 + Vector2.left * 50f,
                               c, null, i == _selTrans ? 3.5f : 2f);

            Vector2 mid = (p0 + p1) * 0.5f + (t.From == t.To ? Vector2.up * -28f : Vector2.zero);
            string name = t.Process != null ? t.Process.DisplayName : "БЕЗ ОБРАБОТКИ";
            Vector2 size = EditorStyles.miniButton.CalcSize(new GUIContent(name));
            Rect lr = new Rect(mid.x - size.x * 0.5f, mid.y - 9f, size.x + 6f, 18f);

            if (GUI.Button(lr, name, EditorStyles.miniButton))
            {
                _selTrans = i; _selState = -1; GUI.FocusControl(null);
            }
        }
        Handles.EndGUI();
    }

    private bool Valid(int i) => _form.States != null && i >= 0 && i < _form.States.Length;

    private void DrawPendingLink()
    {
        if (_linkFrom < 0 || !Valid(_linkFrom)) return;

        Handles.BeginGUI();
        Rect a = NodeRect(_linkFrom);
        Vector2 p0 = new Vector2(a.xMax, a.center.y);
        Vector2 p1 = Event.current.mousePosition;
        Handles.DrawBezier(p0, p1, p0 + Vector2.right * 50f, p1 + Vector2.left * 50f,
                           new Color(1f, 0.8f, 0.3f), null, 2f);
        Handles.EndGUI();
        Repaint();
    }

    // ───────────────────────── МЫШЬ ─────────────────────────

    private void HandleGraphEvents(Rect area)
    {
        Event e = Event.current;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            _linkFrom = -1; e.Use(); Repaint(); return;
        }

        int hit = NodeUnder(e.mousePosition);

        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0:
                if (_linkFrom >= 0)
                {
                    if (hit >= 0) { CreateTransition(_linkFrom, hit); }
                    _linkFrom = -1; e.Use(); break;
                }
                if (hit >= 0)
                {
                    _dragNode = hit;
                    _dragOffset = e.mousePosition - (_form.States[hit].EditorPos + _pan);
                    _selState = hit; _selTrans = -1;
                    GUI.FocusControl(null);
                }
                else { _panning = true; _selState = -1; }
                e.Use();
                break;

            case EventType.MouseDown when e.button == 1:
                if (hit >= 0) NodeMenu(hit);
                else EmptyMenu(e.mousePosition);
                e.Use();
                break;

            case EventType.MouseDrag when e.button == 0:
                if (_dragNode >= 0)
                {
                    Undo.RecordObject(_form, "Двигать состояние");
                    _form.States[_dragNode].EditorPos = e.mousePosition - _dragOffset - _pan;
                    EditorUtility.SetDirty(_form);
                    e.Use(); Repaint();
                }
                else if (_panning) { _pan += e.delta; e.Use(); Repaint(); }
                break;

            case EventType.MouseUp:
                _dragNode = -1; _panning = false;
                break;
        }
    }

    private int NodeUnder(Vector2 p)
    {
        if (_form.States == null) return -1;
        for (int i = _form.States.Length - 1; i >= 0; i--)
            if (NodeRect(i).Contains(p)) return i;
        return -1;
    }

    private void NodeMenu(int i)
    {
        GenericMenu m = new GenericMenu();
        m.AddItem(new GUIContent("Переход отсюда..."), false, () => _linkFrom = i);

        if (i == 0)
            m.AddDisabledItem(new GUIContent("Удалить (базовое нельзя)"));
        else
            m.AddItem(new GUIContent("Удалить состояние"), false, () => DeleteState(i));

        m.ShowAsContext();
    }

    private void EmptyMenu(Vector2 pos)
    {
        GenericMenu m = new GenericMenu();
        m.AddItem(new GUIContent("Добавить состояние"), false, () => AddState(pos - _pan));
        m.ShowAsContext();
    }

    // ───────────────────────── ПРАВКИ ─────────────────────────

    private void AddState(Vector2 pos)
    {
        Undo.RecordObject(_form, "Добавить состояние");

        List<FoodState> list = new List<FoodState>(_form.States ?? new FoodState[0]);
        list.Add(new FoodState { Label = "новое", EditorPos = pos });
        _form.States = list.ToArray();

        _selState = _form.States.Length - 1;
        EditorUtility.SetDirty(_form);
    }

    /// <summary>
    /// Удаление состояния — самое опасное место: индексы всех последующих
    /// состояний сдвигаются, и рёбра начинают указывать не туда. Поэтому
    /// рёбра к удалённому выкидываются, а остальные переиндексируются.
    /// </summary>
    private void DeleteState(int index)
    {
        Undo.RecordObject(_form, "Удалить состояние");

        List<FoodState> states = new List<FoodState>(_form.States);
        states.RemoveAt(index);
        _form.States = states.ToArray();

        List<FoodTransition> keep = new List<FoodTransition>();
        foreach (FoodTransition t in _form.Transitions ?? new FoodTransition[0])
        {
            if (t.From == index || t.To == index) continue;
            FoodTransition u = t;
            if (u.From > index) u.From--;
            if (u.To > index) u.To--;
            keep.Add(u);
        }
        _form.Transitions = keep.ToArray();

        _selState = -1; _selTrans = -1;
        EditorUtility.SetDirty(_form);
    }

    private void CreateTransition(int from, int to)
    {
        Undo.RecordObject(_form, "Добавить переход");

        List<FoodTransition> list = new List<FoodTransition>(_form.Transitions ?? new FoodTransition[0]);
        list.Add(new FoodTransition { From = from, To = to, BlendSeconds = 0f });
        _form.Transitions = list.ToArray();

        _selTrans = _form.Transitions.Length - 1;
        _selState = -1;
        EditorUtility.SetDirty(_form);
    }

    private void DeleteTransition(int index)
    {
        Undo.RecordObject(_form, "Удалить переход");

        List<FoodTransition> list = new List<FoodTransition>(_form.Transitions);
        list.RemoveAt(index);
        _form.Transitions = list.ToArray();

        _selTrans = -1;
        EditorUtility.SetDirty(_form);
    }

    private void AutoLayout()
    {
        if (_form.States == null) return;
        Undo.RecordObject(_form, "Разложить граф");

        for (int i = 0; i < _form.States.Length; i++)
            _form.States[i].EditorPos = new Vector2(40f + (i % 3) * 200f, 40f + (i / 3) * 110f);

        _pan = Vector2.zero;
        EditorUtility.SetDirty(_form);
    }

    // ───────────────────────── БОКОВАЯ ПАНЕЛЬ ─────────────────────────

    private void DrawPanel(Rect area)
    {
        GUILayout.BeginArea(area, EditorStyles.helpBox);

        if (_selTrans >= 0 && _selTrans < Count(_form.Transitions)) DrawTransitionPanel();
        else if (_selState >= 0 && _selState < Count(_form.States)) DrawStatePanel();
        else DrawFormPanel();

        GUILayout.EndArea();
    }

    private void DrawFormPanel()
    {
        EditorGUILayout.LabelField("Форма", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_so.FindProperty("Label"));
        EditorGUILayout.PropertyField(_so.FindProperty("Ingredient"));

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Гнилое", EditorStyles.boldLabel);
        SerializedProperty rot = _so.FindProperty("Rotten");
        EditorGUILayout.PropertyField(rot.FindPropertyRelative("Label"));
        EditorGUILayout.PropertyField(rot.FindPropertyRelative("Material"));

        EditorGUILayout.Space(10f);
        EditorGUILayout.HelpBox(
            "ПКМ по пустому месту — добавить состояние.\n" +
            "ПКМ по узлу — переход отсюда.\n" +
            "ЛКМ по подписи стрелки — настроить переход.",
            MessageType.None);

        Validate();
    }

    private void DrawStatePanel()
    {
        EditorGUILayout.LabelField($"Состояние {_selState}", EditorStyles.boldLabel);
        if (_selState == 0)
            EditorGUILayout.HelpBox("Базовое: продукт таким спавнится.", MessageType.Info);

        SerializedProperty states = _so.FindProperty("States");
        if (states == null || _selState >= states.arraySize) { _selState = -1; return; }

        SerializedProperty st = states.GetArrayElementAtIndex(_selState);
        if (st == null) { _selState = -1; return; }

        EditorGUILayout.PropertyField(st.FindPropertyRelative("Label"));
        EditorGUILayout.PropertyField(st.FindPropertyRelative("Material"));

        EditorGUILayout.Space(10f);

        SerializedProperty dis = st.FindPropertyRelative("DissolvesIntoVessel");
        EditorGUILayout.PropertyField(dis, new GUIContent("Растворяется в таре"));

        if (dis != null && dis.boolValue)
            EditorGUILayout.HelpBox(
                "Дойдя сюда, продукт перестаёт быть предметом: масса и журнал " +
                "уходят во фракцию ёмкости, объект деспавнится. Материал этого " +
                "состояния видно доли секунды.\n\n" +
                "Если ёмкости рядом нет — продукт останется лежать в этом " +
                "состоянии, растворяться некуда.", MessageType.Info);

        EditorGUILayout.Space(10f);
        if (GUILayout.Button("Назад к форме")) _selState = -1;
    }

    private void DrawTransitionPanel()
    {
        EditorGUILayout.LabelField("Переход", EditorStyles.boldLabel);

        SerializedProperty trans = _so.FindProperty("Transitions");

        // Защита от рассинхронизации на случай, если где-то ещё массив и
        // SerializedObject разойдутся на кадр: тихо снимаем выделение вместо
        // падения в NullReferenceException.
        if (trans == null || _selTrans >= trans.arraySize)
        {
            _selTrans = -1;
            return;
        }

        SerializedProperty tr = trans.GetArrayElementAtIndex(_selTrans);
        if (tr == null) { _selTrans = -1; return; }

        FoodTransition t = _form.Transitions[_selTrans];

        EditorGUILayout.LabelField("из", _form.GetLabel((byte)t.From));
        EditorGUILayout.LabelField("в", _form.GetLabel((byte)t.To));

        EditorGUILayout.Space(6f);
        EditorGUILayout.PropertyField(tr.FindPropertyRelative("Process"), new GUIContent("Обработка"));
        EditorGUILayout.PropertyField(tr.FindPropertyRelative("BlendSeconds"),
                                      new GUIContent("Плавно, сек"));

        SerializedProperty wm = tr.FindPropertyRelative("WorkMultiplier");
        EditorGUILayout.PropertyField(wm, new GUIContent("Скорость ×"));

        // Ноль здесь не ошибка, а «поле не заполняли»: Unity на C# 9, где
        // структуре нельзя задать значение по умолчанию, и все рёбра,
        // нарисованные до появления множителя, лежат с нулём.
        if (wm != null)
            EditorGUILayout.LabelField(" ", wm.floatValue <= 0.01f
                ? "0 = обычная скорость"
                : $"в {wm.floatValue:0.##} раза относительно обычной",
                EditorStyles.miniLabel);

        if (t.BlendSeconds > 0.01f && Valid(t.From) && Valid(t.To))
        {
            Material a = _form.States[t.From].Material;
            Material b = _form.States[t.To].Material;
            if (a != null && b != null && a.shader != b.shader)
                EditorGUILayout.HelpBox(
                    "У материалов разные шейдеры — плавно не выйдет, " +
                    "будет мгновенная подмена.", MessageType.Warning);
        }

        EditorGUILayout.Space(10f);
        if (GUILayout.Button("Удалить переход")) DeleteTransition(_selTrans);
        if (GUILayout.Button("Назад к форме")) _selTrans = -1;
    }

    /// <summary>Проверки, которые Unity сама не сделает.</summary>
    private void Validate()
    {
        List<string> problems = new List<string>();

        if (Count(_form.States) == 0) problems.Add("нет ни одного состояния");

        for (int i = 0; i < Count(_form.States); i++)
            if (_form.States[i].Material == null)
                problems.Add($"состояние {i} без материала");

        HashSet<(int, ProcessDef)> seen = new HashSet<(int, ProcessDef)>();
        for (int i = 0; i < Count(_form.Transitions); i++)
        {
            FoodTransition t = _form.Transitions[i];
            if (t.Process == null) { problems.Add($"переход {i} без обработки"); continue; }

            // Два ребра из одного узла по одной обработке — сработает первое,
            // второе станет мёртвым. Unity об этом не скажет.
            if (!seen.Add((t.From, t.Process)))
                problems.Add($"из «{_form.GetLabel((byte)t.From)}» два перехода " +
                             $"по «{t.Process.DisplayName}»");
        }

        if (problems.Count == 0) return;

        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox("• " + string.Join("\n• ", problems), MessageType.Warning);
    }
}
#endif
