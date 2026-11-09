using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CatCooks.Level
{
    /// <summary>
    /// Редактор дома из модулей: рисование клеток мышкой в сцене, сборка клеток из модулей (балочный + стекло / кирпич),
    /// заливка прямоугольником, копирование и отражение этажей, фейк-этажи под домом.
    /// Пересобираются только тронутые клетки и их соседи — остальное не трогается. Всё через Undo (Ctrl+Z).
    /// </summary>
    [CustomEditor(typeof(CityModuleHouse))]
    public class CityModuleHouseEditor : Editor
    {
        private static readonly int[] DX = { 1, -1, 0, 0 };
        private static readonly int[] DZ = { 0, 0, 1, -1 };
        private static readonly string[] ToolNames = { "Кисть", "Ластик", "Стена", "Замок" };

        private static int s_fillW = 10, s_fillD = 10, s_fillX, s_fillZ;
        private static int s_copyFrom, s_copyTo0 = -1, s_copyTo1 = -1;
        private static bool s_mirX, s_mirZ;
        private static GUIStyle s_wrap;

        private int _lastX = int.MinValue, _lastZ = int.MinValue, _lastS = -1, _group = -1;
        private bool _hover; private int _hx, _hz, _hs;

        private void OnEnable() { Undo.undoRedoPerformed += OnUndo; }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            Tools.hidden = false;
        }

        private void OnUndo()
        {
            CityModuleHouse h = target as CityModuleHouse;
            if (h != null) { h.Reindex(); SceneView.RepaintAll(); }
        }

        // ══════════════ ИНСПЕКТОР ══════════════
        public override void OnInspectorGUI()
        {
            CityModuleHouse h = (CityModuleHouse)target;
            h.Reindex();
            EditorGUILayout.HelpBox("Карта этажа — вид сверху (Z вверх, X вправо), клетка (0, 0) — левый нижний угол = позиция объекта.\n" +
                                    "Выбери модуль и щёлкай по квадратам (можно вести мышью), ПКМ — стереть. Полоски между квадратами — стены: " +
                                    "щёлкни — поставить выбранную стену, ПКМ — «Авто». Ctrl+ЛКМ по квадрату — заморозить (править руками).", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Подставить модули и замерить")) { AutoAssign(h); h.Measure(); SceneView.RepaintAll(); }
                if (GUILayout.Button("Пересобрать всё")) RebuildAll(h);
            }

            DrawFloorBar(h);
            DrawPalette(h);
            DrawMap(h);

            EditorGUILayout.Space();
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = h.Painting ? new Color(0.5f, 1f, 0.6f) : old;
            if (GUILayout.Button(h.Painting ? "✎ Рисую в сцене (нажми, чтобы выйти)" : "✎ Рисовать мышкой в сцене (по желанию)"))
            {
                Undo.RecordObject(h, "Рисование");
                h.Painting = !h.Painting;
                if (h.Painting) { AutoAssign(h); if (h.Cells.Count == 0) h.Measure(); }
                Tools.hidden = h.Painting;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = old;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Залить прямоугольник на текущем этаже (" + h.ActiveFloor + ") типом «" + CityModuleHouse.KindName(h.Brush) + "»", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                s_fillW = Mathf.Max(1, EditorGUILayout.IntField("Ширина X", s_fillW));
                s_fillD = Mathf.Max(1, EditorGUILayout.IntField("Глубина Z", s_fillD));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                s_fillX = EditorGUILayout.IntField("С клетки X", s_fillX);
                s_fillZ = EditorGUILayout.IntField("С клетки Z", s_fillZ);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Залить")) Fill(h, s_fillX, s_fillZ, s_fillW, s_fillD, false);
                if (GUILayout.Button("Стереть прямоугольник")) Fill(h, s_fillX, s_fillZ, s_fillW, s_fillD, true);
                if (GUILayout.Button("Очистить этаж")) ClearFloor(h, h.ActiveFloor);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Копировать этаж (нижние «зеркальные» этажи — настоящие модули)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                s_copyFrom = EditorGUILayout.IntField("С этажа", s_copyFrom);
                s_copyTo0 = EditorGUILayout.IntField("На этажи от", s_copyTo0);
                s_copyTo1 = EditorGUILayout.IntField("до", s_copyTo1);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                s_mirX = EditorGUILayout.ToggleLeft("Отразить по X", s_mirX);
                s_mirZ = EditorGUILayout.ToggleLeft("Отразить по Z", s_mirZ);
                if (GUILayout.Button("Копировать")) CopyFloors(h, s_copyFrom, s_copyTo0, s_copyTo1, s_mirX, s_mirZ);
            }
            if (GUILayout.Button("Отразить текущий этаж на месте (X: " + (s_mirX ? "да" : "нет") + ", Z: " + (s_mirZ ? "да" : "нет") + ")"))
                CopyFloors(h, h.ActiveFloor, h.ActiveFloor, h.ActiveFloor, s_mirX, s_mirZ);

            EditorGUILayout.Space();
            if (GUILayout.Button("Фейк-этажи под домом: снять нижний этаж и вырастить вниз", GUILayout.Height(24))) MakeFakes(h);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Разобрать постройку (клетки остаются)")) DestroyBuilt(h);
                if (GUILayout.Button("Удалить все клетки") && EditorUtility.DisplayDialog("Дом из модулей", "Удалить все клетки и постройку?", "Удалить", "Отмена"))
                {
                    DestroyBuilt(h);
                    Undo.RecordObject(h, "Удалить клетки");
                    h.Cells.Clear(); h.Reindex();
                    EditorUtility.SetDirty(h);
                }
            }
            int lo, hi; h.FloorRange(out lo, out hi);
            EditorGUILayout.LabelField("Клеток: " + h.Cells.Count + (h.Cells.Count > 0 ? ", этажи " + lo + "…" + hi : "") + ". Клетка " + h.Pitch.ToString("0.##") + " м, этаж " + h.Height.ToString("0.##") + " м.", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
                EditorGUILayout.HelpBox("Поменял размер/модули/стены по умолчанию — нажми «Пересобрать всё».", MessageType.None);
            }
        }

        // ══════════════ КАРТА ЭТАЖА В ИНСПЕКТОРЕ ══════════════
        private static readonly string[] KindShort = { "Комн", "Пол", "Карк", "Кол", "Плита" };
        private static GUIStyle s_cellLabel;
        private bool _mapDrag;
        private int _mapBtn, _mapGroup = -1;
        private string _mapLast;

        private static Color WallColor(CityModuleHouse.WallKind w)
        {
            switch (w)
            {
                case CityModuleHouse.WallKind.Auto: return new Color(0.62f, 0.62f, 0.62f);
                case CityModuleHouse.WallKind.None: return new Color(0.16f, 0.16f, 0.16f);
                case CityModuleHouse.WallKind.Glass: return new Color(0.55f, 0.88f, 1f);
                case CityModuleHouse.WallKind.Brick: return new Color(0.85f, 0.42f, 0.28f);
                default: return new Color(0.55f, 0.18f, 0.16f);
            }
        }

        private static bool ColorButton(string label, Color c, bool sel)
        {
            Color ob = GUI.backgroundColor;
            GUI.backgroundColor = sel ? c : Color.Lerp(c, new Color(0.4f, 0.4f, 0.4f), 0.65f);
            bool r = GUILayout.Button(sel ? "● " + label : label, sel ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(22), GUILayout.MinWidth(30));
            GUI.backgroundColor = ob;
            return r;
        }

        private void DrawFloorBar(CityModuleHouse h)
        {
            EditorGUILayout.Space();
            int lo, hi; h.FloorRange(out lo, out hi);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Этаж", EditorStyles.boldLabel, GUILayout.Width(40));
                if (GUILayout.Button("−", GUILayout.Width(24))) SetFloor(h, h.ActiveFloor - 1);
                for (int f = Mathf.Min(lo, h.ActiveFloor); f <= Mathf.Max(hi, h.ActiveFloor); f++)
                {
                    bool sel = f == h.ActiveFloor;
                    Color ob = GUI.backgroundColor;
                    if (sel) GUI.backgroundColor = new Color(0.5f, 0.9f, 1f);
                    if (GUILayout.Button(f.ToString(), sel ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Width(30), GUILayout.Height(20))) SetFloor(h, f);
                    GUI.backgroundColor = ob;
                }
                if (GUILayout.Button("+", GUILayout.Width(24))) SetFloor(h, h.ActiveFloor + 1);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Копия этажа ниже сюда", GUILayout.Width(160))) CopyFloors(h, h.ActiveFloor - 1, h.ActiveFloor, h.ActiveFloor, false, false);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Карта", GUILayout.Width(40));
                int w = EditorGUILayout.IntField(h.MapW, GUILayout.Width(36));
                GUILayout.Label("×", GUILayout.Width(12));
                int d = EditorGUILayout.IntField(h.MapD, GUILayout.Width(36));
                if (w != h.MapW || d != h.MapD) { Undo.RecordObject(h, "Карта"); h.MapW = Mathf.Clamp(w, 1, 40); h.MapD = Mathf.Clamp(d, 1, 40); }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Залить этаж выбранным модулем", GUILayout.Width(220)))
                {
                    if (h.Tool == 1) ClearFloor(h, h.ActiveFloor); else Fill(h, 0, 0, h.MapW, h.MapD, false);
                }
            }
        }

        private void DrawPalette(CityModuleHouse h)
        {
            GUILayout.Label("Модуль — щелчок по квадрату", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (ColorButton("Пусто", new Color(0.45f, 0.45f, 0.45f), h.Tool == 1)) { Undo.RecordObject(h, "Кисть"); h.Tool = 1; }
                for (int k = 0; k < 5; k++)
                {
                    CityModuleHouse.CellKind ck = (CityModuleHouse.CellKind)k;
                    if (ColorButton(CityModuleHouse.KindName(ck), CityModuleHouse.KindColor(ck), h.Tool != 1 && h.Brush == ck)) { Undo.RecordObject(h, "Кисть"); h.Tool = 0; h.Brush = ck; }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Полоски на карте:", EditorStyles.miniBoldLabel, GUILayout.Width(110));
                if (ColorButton("Стены", new Color(0.85f, 0.42f, 0.28f), h.EdgeMode == 0)) { Undo.RecordObject(h, "Режим"); h.EdgeMode = 0; }
                if (ColorButton("Балки и колонны", new Color(0.9f, 0.3f, 0.22f), h.EdgeMode == 1)) { Undo.RecordObject(h, "Режим"); h.EdgeMode = 1; }
            }
            if (h.EdgeMode == 1)
            {
                EditorGUILayout.HelpBox("Балки: ЛКМ по полоске — убрать балку на этой стороне, ПКМ — вернуть. ЛКМ по квадрату — убрать/вернуть среднюю балку. " +
                                        "Оранжевые квадратики на перекрёстках — колонны: ЛКМ — убрать, ПКМ — вернуть. Красное/оранжевое — есть, тёмное — убрано. Модули в этом режиме не ставятся.", MessageType.None);
                return;
            }
            GUILayout.Label("Стена — щелчок по полоске между квадратами («Нет» — убрать стену)", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int w = 0; w < 5; w++)
                {
                    CityModuleHouse.WallKind wk = (CityModuleHouse.WallKind)w;
                    if (ColorButton(CityModuleHouse.WallName(wk), WallColor(wk), h.WallBrush == wk)) { Undo.RecordObject(h, "Стена"); h.WallBrush = wk; }
                }
            }
        }

        private void DrawMap(CityModuleHouse h)
        {
            int F = h.ActiveFloor;
            int x0 = 0, z0 = 0, x1 = h.MapW - 1, z1 = h.MapD - 1;
            foreach (CityModuleHouse.Cell c in h.Cells)
            {
                if (c == null) continue;
                x0 = Mathf.Min(x0, c.X); x1 = Mathf.Max(x1, c.X); z0 = Mathf.Min(z0, c.Z); z1 = Mathf.Max(z1, c.Z);
            }
            int W = x1 - x0 + 1, D = z1 - z0 + 1;
            const float g = 8f;
            float avail = EditorGUIUtility.currentViewWidth - 40f;
            float cs = Mathf.Clamp((avail - g) / W - g, 14f, 44f), st = cs + g;
            float totalW = W * st + g, totalH = D * st + g;
            Rect area = GUILayoutUtility.GetRect(totalW, totalH, GUILayout.ExpandWidth(true));
            float left = area.x + Mathf.Max(0f, (area.width - totalW) * 0.5f), top = area.y;
            if (s_cellLabel == null)
            {
                s_cellLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
                s_cellLabel.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
            }

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(left, top, totalW, totalH), new Color(0.1f, 0.1f, 0.11f));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Rect r = new Rect(left + g + (x - x0) * st, top + g + (z1 - z) * st, cs, cs);
                        CityModuleHouse.Cell c = h.Find(x, F, z);
                        if (c == null)
                        {
                            EditorGUI.DrawRect(r, new Color(0.24f, 0.24f, 0.25f));
                            CityModuleHouse.Cell b = h.Find(x, F - 1, z);
                            if (b != null)
                            {
                                Color kb = CityModuleHouse.KindColor(b.Kind); kb.a = 0.3f;
                                EditorGUI.DrawRect(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), kb);       // этаж ниже — тусклым
                            }
                            if (x == 0 && z == 0) GUI.Label(r, "0,0", s_cellLabel);
                            continue;
                        }
                        if (c.Locked) EditorGUI.DrawRect(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(0.75f, 0.5f, 1f));
                        EditorGUI.DrawRect(r, CityModuleHouse.KindColor(c.Kind));
                        if (h.EdgeMode == 1 && CityModuleHouse.HasBeams(c.Kind))
                        {
                            Color mb = c.BeamRemoved(4) ? new Color(0.2f, 0.2f, 0.2f, 0.5f) : new Color(0.9f, 0.3f, 0.22f);
                            EditorGUI.DrawRect(new Rect(r.x + 3, r.center.y - 2, r.width - 6, 4), mb);   // средняя балка
                        }
                        GUI.Label(r, cs >= 26f ? KindShort[(int)c.Kind] : KindShort[(int)c.Kind].Substring(0, 1), s_cellLabel);
                    }
                // стены: вертикальные полоски (между x−1 и x) и горизонтальные (между z−1 и z)
                for (int z = z0; z <= z1; z++)
                    for (int i = x0; i <= x1 + 1; i++)
                    {
                        Color col;
                        if (!EdgeColor(h, h.EdgeMode, h.Find(i - 1, F, z), 0, h.Find(i, F, z), 1, out col)) continue;
                        EditorGUI.DrawRect(new Rect(left + (i - x0) * st + 1, top + g + (z1 - z) * st, g - 2, cs), col);
                    }
                for (int j = z0; j <= z1 + 1; j++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Color col;
                        if (!EdgeColor(h, h.EdgeMode, h.Find(x, F, j - 1), 2, h.Find(x, F, j), 3, out col)) continue;
                        EditorGUI.DrawRect(new Rect(left + g + (x - x0) * st, top + (z1 + 1 - j) * st + 1, cs, g - 2), col);
                    }
                if (h.EdgeMode == 1)                                                    // колонны — квадратики на перекрёстках
                    for (int j = z0; j <= z1 + 1; j++)
                        for (int i = x0; i <= x1 + 1; i++)
                        {
                            int st2 = ColumnState(h, i, j, F);
                            if (st2 == 0) continue;
                            Rect cr = new Rect(left + (i - x0) * st - 2, top + (z1 + 1 - j) * st - 2, g + 4, g + 4);
                            EditorGUI.DrawRect(cr, new Color(0.05f, 0.05f, 0.05f));
                            EditorGUI.DrawRect(new Rect(cr.x + 1, cr.y + 1, cr.width - 2, cr.height - 2), st2 == 1 ? new Color(1f, 0.55f, 0.2f) : new Color(0.32f, 0.32f, 0.34f));
                        }
            }
            GUILayout.Label("квадрат — модуль · полоска — стена (голубая стекло, рыжая кирпич, бордовая глухая) · тусклый квадрат — клетка этажа ниже · фиолетовая рамка — заморожена", EditorStyles.wordWrappedMiniLabel);

            // мышь
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Rect grid = new Rect(left, top, totalW, totalH);
            if (e.type == EventType.MouseDown && grid.Contains(e.mousePosition) && (e.button == 0 || e.button == 1))
            {
                GUIUtility.hotControl = id;
                Undo.IncrementCurrentGroup();
                _mapGroup = Undo.GetCurrentGroup();
                _mapDrag = true; _mapBtn = e.button; _mapLast = null;
                MapHit(h, e.mousePosition, left, top, st, g, x0, z1, W, D, e.control && e.button == 0);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && _mapDrag && GUIUtility.hotControl == id)
            {
                if (grid.Contains(e.mousePosition)) MapHit(h, e.mousePosition, left, top, st, g, x0, z1, W, D, false);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && _mapDrag)
            {
                _mapDrag = false;
                if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
                if (_mapGroup >= 0) Undo.CollapseUndoOperations(_mapGroup);
                _mapGroup = -1;
                e.Use();
            }
        }

        /// <summary>Цвет полоски-стены между клетками a (сторона sa) и b (сторона sb). false — рисовать нечего.</summary>
        private static bool EdgeColor(CityModuleHouse h, int mode, CityModuleHouse.Cell a, int sa, CityModuleHouse.Cell b, int sb, out Color col)
        {
            col = Color.clear;
            if (a == null && b == null) return false;
            if (mode == 1)
            {
                bool ha = a != null && CityModuleHouse.HasBeams(a.Kind), hb = b != null && CityModuleHouse.HasBeams(b.Kind);
                if (!ha && !hb) return false;
                bool on = (ha && !a.BeamRemoved(sa)) || (hb && !b.BeamRemoved(sb));
                col = on ? new Color(0.9f, 0.3f, 0.22f) : new Color(0.3f, 0.3f, 0.32f);
                return true;
            }
            CityModuleHouse.WallKind wa = a != null ? h.ResolveWall(a, sa) : CityModuleHouse.WallKind.None;
            CityModuleHouse.WallKind wb = b != null ? h.ResolveWall(b, sb) : CityModuleHouse.WallKind.None;
            CityModuleHouse.WallKind w = wa != CityModuleHouse.WallKind.None ? wa : wb;
            col = WallColor(w);
            if (w == CityModuleHouse.WallKind.None) col = new Color(0.3f, 0.3f, 0.32f);
            return true;
        }

        private void MapHit(CityModuleHouse h, Vector2 mp, float left, float top, float st, float g, int x0, int z1, int W, int D, bool lockToggle)
        {
            float lx = mp.x - left, ly = mp.y - top;
            int ix = Mathf.FloorToInt(lx / st), iy = Mathf.FloorToInt(ly / st);
            bool edgeX = lx - ix * st < g, edgeY = ly - iy * st < g;
            int F = h.ActiveFloor;
            string key;
            if (edgeX && edgeY)                                               // перекрёсток полосок = угловая колонна
            {
                if (h.EdgeMode != 1) return;
                int i = x0 + ix, j = z1 + 1 - iy;
                key = "k" + i + "_" + j;
                if (key == _mapLast) return;
                _mapLast = key;
                if (ColumnState(h, i, j, F) == 0) return;
                bool rem = _mapBtn != 1;
                Undo.RecordObject(h, rem ? "Убрать колонну" : "Вернуть колонну");
                SetCorner(h.Find(i - 1, F, j - 1), 1, 1, rem);
                SetCorner(h.Find(i, F, j - 1), -1, 1, rem);
                SetCorner(h.Find(i - 1, F, j), 1, -1, rem);
                SetCorner(h.Find(i, F, j), -1, -1, rem);
                EditorUtility.SetDirty(h);
                RebuildRegion(h, i - 1, i, F, F, j - 1, j);
                return;
            }
            if (edgeX)
            {
                if (iy >= D) return;
                int i = x0 + ix, z = z1 - iy;
                key = "v" + i + "_" + z;
                if (key == _mapLast) return;
                _mapLast = key;
                if (h.EdgeMode == 1) SetBeamEdge(h, h.Find(i - 1, F, z), 0, h.Find(i, F, z), 1); else SetEdge(h, h.Find(i - 1, F, z), 0, h.Find(i, F, z), 1);
                RebuildRegion(h, i - 1, i, F, F, z, z);
                return;
            }
            if (edgeY)
            {
                if (ix >= W) return;
                int x = x0 + ix, j = z1 + 1 - iy;
                key = "h" + x + "_" + j;
                if (key == _mapLast) return;
                _mapLast = key;
                if (h.EdgeMode == 1) SetBeamEdge(h, h.Find(x, F, j - 1), 2, h.Find(x, F, j), 3); else SetEdge(h, h.Find(x, F, j - 1), 2, h.Find(x, F, j), 3);
                RebuildRegion(h, x, x, F, F, j - 1, j);
                return;
            }
            if (ix >= W || iy >= D) return;
            int cx = x0 + ix, cz = z1 - iy;
            key = "c" + cx + "_" + cz;
            if (key == _mapLast) return;
            _mapLast = key;
            CityModuleHouse.Cell c = h.Find(cx, F, cz);
            if (lockToggle)
            {
                if (c == null) return;
                Undo.RecordObject(h, "Замок");
                c.Locked = !c.Locked;
                EditorUtility.SetDirty(h);
                return;
            }
            if (h.EdgeMode == 1)
            {
                if (c == null || !CityModuleHouse.HasBeams(c.Kind)) return;
                Undo.RecordObject(h, "Средняя балка");
                c.SetBeam(4, _mapBtn == 1 ? false : !c.BeamRemoved(4));
                EditorUtility.SetDirty(h);
                RebuildRegion(h, cx, cx, F, F, cz, cz);
                return;
            }
            bool erase = _mapBtn == 1 || h.Tool == 1;
            if (erase)
            {
                if (c == null) return;
                Undo.RecordObject(h, "Стереть клетку");
                h.Remove(cx, F, cz);
            }
            else
            {
                if (c != null && c.Kind == h.Brush) return;
                Undo.RecordObject(h, "Клетка");
                h.Add(cx, F, cz, h.Brush);
            }
            EditorUtility.SetDirty(h);
            RebuildRegion(h, cx, cx, F, F, cz, cz);
        }

        private static void SetCorner(CityModuleHouse.Cell c, int sx, int sz, bool removed)
        {
            if (c != null) c.SetBeam(CityModuleHouse.Cell.CornerBit(sx, sz), removed);
        }

        /// <summary>Колонна на перекрёстке (i, j) — угол между клетками i−1…i, j−1…j: 0 — нет клеток с колоннами, 1 — стоит, 2 — убрана.</summary>
        private static int ColumnState(CityModuleHouse h, int i, int j, int F)
        {
            bool any = false, removed = false;
            int[] xs = { i - 1, i, i - 1, i }, zs = { j - 1, j - 1, j, j };
            int[] sxs = { 1, -1, 1, -1 }, szs = { 1, 1, -1, -1 };
            for (int k = 0; k < 4; k++)
            {
                CityModuleHouse.Cell c = h.Find(xs[k], F, zs[k]);
                if (c == null || !CityModuleHouse.HasColumns(c.Kind)) continue;
                any = true;
                if (c.BeamRemoved(CityModuleHouse.Cell.CornerBit(sxs[k], szs[k]))) removed = true;
            }
            return !any ? 0 : removed ? 2 : 1;
        }

        /// <summary>Балка на общей стороне клеток a и b: ЛКМ — убрать, ПКМ — вернуть (у обеих — балка общая).</summary>
        private void SetBeamEdge(CityModuleHouse h, CityModuleHouse.Cell a, int sa, CityModuleHouse.Cell b, int sb)
        {
            if (a == null && b == null) return;
            bool removed = _mapBtn != 1;
            Undo.RecordObject(h, removed ? "Убрать балку" : "Вернуть балку");
            if (a != null) a.SetBeam(sa, removed);
            if (b != null) b.SetBeam(sb, removed);
            EditorUtility.SetDirty(h);
        }

        /// <summary>Стена между клетками a (сторона sa) и b (сторона sb): видимая — у одной, «Нет»/«Авто» — у обеих.</summary>
        private void SetEdge(CityModuleHouse h, CityModuleHouse.Cell a, int sa, CityModuleHouse.Cell b, int sb)
        {
            if (a == null && b == null) return;
            CityModuleHouse.WallKind w = _mapBtn == 1 ? CityModuleHouse.WallKind.Auto : h.WallBrush;
            Undo.RecordObject(h, "Стена");
            bool visible = w != CityModuleHouse.WallKind.None && w != CityModuleHouse.WallKind.Auto;
            if (visible)
            {
                if (a != null) { a.SetWall(sa, w); if (b != null) b.SetWall(sb, CityModuleHouse.WallKind.Auto); }
                else b.SetWall(sb, w);
            }
            else
            {
                if (a != null) a.SetWall(sa, w);
                if (b != null) b.SetWall(sb, w);
            }
            EditorUtility.SetDirty(h);
        }

        /// <summary>Инструменты и кисти (в инспекторе и в окошке в сцене).</summary>
        private static void DrawToolbar(CityModuleHouse h, bool compact)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Этаж", GUILayout.Width(36));
                if (GUILayout.Button("−", GUILayout.Width(24))) SetFloor(h, h.ActiveFloor - 1);
                GUILayout.Label(h.ActiveFloor.ToString(), EditorStyles.boldLabel, GUILayout.Width(26));
                if (GUILayout.Button("+", GUILayout.Width(24))) SetFloor(h, h.ActiveFloor + 1);
                bool so = GUILayout.Toggle(h.ShowOtherFloors, "другие этажи", GUILayout.Width(compact ? 96 : 110));
                if (so != h.ShowOtherFloors) { Undo.RecordObject(h, "Этажи"); h.ShowOtherFloors = so; SceneView.RepaintAll(); }
            }
            int t = GUILayout.Toolbar(h.Tool, ToolNames);
            if (t != h.Tool) { Undo.RecordObject(h, "Инструмент"); h.Tool = t; }
            if (h.Tool == 0)
            {
                string[] kinds = { "Комната", "Каркас+плита", "Каркас", "Колонны", "Плита" };
                int k = GUILayout.SelectionGrid((int)h.Brush, kinds, compact ? 3 : 5);
                if (k != (int)h.Brush) { Undo.RecordObject(h, "Кисть"); h.Brush = (CityModuleHouse.CellKind)k; }
            }
            else if (h.Tool == 2)
            {
                string[] walls = { "Авто", "Нет", "Стекло", "Кирпич", "Глухая" };
                int w = GUILayout.SelectionGrid((int)h.WallBrush, walls, compact ? 3 : 5);
                if (w != (int)h.WallBrush) { Undo.RecordObject(h, "Стена"); h.WallBrush = (CityModuleHouse.WallKind)w; }
            }
            if (!compact) EditorGUILayout.HelpBox(ToolHelp(h.Tool), MessageType.None);
        }

        private static string ToolHelp(int t)
        {
            switch (t)
            {
                case 0: return "Комната — каркас, плита сверху, стены снаружи (по умолчанию стекло). Каркас+плита — пол/терраса без стен. Каркас — колонны и балки. Колонны — только колонны. Плита — только перекрытие.";
                case 1: return "ЛКМ — стереть клетку (соседи сами достроят свои колонны и стены).";
                case 2: return "ЛКМ по краю клетки — поставить стену этого типа на ближнюю сторону. «Авто» — снаружи у комнат по умолчанию, «Нет» — открытая сторона. Shift+ЛКМ — вернуть «Авто». Стену можно поставить и внутри дома (перегородка).";
                default: return "ЛКМ — заморозить клетку: пересборка её больше не трогает, внутри можно удалять и двигать куски руками. Shift+ЛКМ — разморозить.";
            }
        }

        private static void SetFloor(CityModuleHouse h, int f)
        {
            Undo.RecordObject(h, "Этаж");
            h.ActiveFloor = f;
            SceneView.RepaintAll();
        }

        // ══════════════ СЦЕНА: РИСОВАНИЕ ══════════════
        private void OnSceneGUI()
        {
            CityModuleHouse h = (CityModuleHouse)target;
            if (h == null) return;
            h.Reindex();
            DrawFloorCells(h);
            if (!h.Painting) { if (Tools.hidden) Tools.hidden = false; return; }
            Tools.hidden = true;

            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Rect panel = new Rect(10, 10, 320, h.Tool == 0 || h.Tool == 2 ? 184 : 138);

            // окошко с инструментами
            Handles.BeginGUI();
            GUILayout.BeginArea(panel, EditorStyles.helpBox);
            GUILayout.Label("Дом из модулей — " + ToolNames[h.Tool] + (h.Tool == 0 ? ": " + CityModuleHouse.KindName(h.Brush) : h.Tool == 2 ? ": " + CityModuleHouse.WallName(h.WallBrush) : ""), EditorStyles.boldLabel);
            DrawToolbar(h, true);
            if (s_wrap == null) s_wrap = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            GUILayout.Label(HoverInfo(h), s_wrap);
            GUILayout.EndArea();
            Handles.EndGUI();

            bool overPanel = panel.Contains(e.mousePosition);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.PageUp) { SetFloor(h, h.ActiveFloor + 1); e.Use(); }
                else if (e.keyCode == KeyCode.PageDown) { SetFloor(h, h.ActiveFloor - 1); e.Use(); }
                else if (e.keyCode == KeyCode.Escape) { Undo.RecordObject(h, "Рисование"); h.Painting = false; Tools.hidden = false; Repaint(); e.Use(); return; }
            }

            int cx = 0, cz = 0, side = 0;
            _hover = !overPanel && Pick(h, e.mousePosition, out cx, out cz, out side);
            if (_hover) { _hx = cx; _hz = cz; _hs = side; DrawCursor(h, cx, cz, side, e.shift, e.control); }
            if (e.type == EventType.MouseMove) SceneView.RepaintAll();

            if (overPanel || e.alt) return;
            if (e.type == EventType.MouseDown && e.button == 0 && _hover)
            {
                GUIUtility.hotControl = id;
                Undo.IncrementCurrentGroup();
                _group = Undo.GetCurrentGroup();
                _lastX = int.MinValue; _lastZ = int.MinValue; _lastS = -1;
                Apply(h, cx, cz, side, e.shift, e.control);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                if (_hover && !e.control) Apply(h, cx, cz, side, e.shift, false);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                if (_group >= 0) Undo.CollapseUndoOperations(_group);
                _group = -1;
                e.Use();
            }
        }

        private string HoverInfo(CityModuleHouse h)
        {
            if (!_hover) return "Наведи на сетку этажа " + h.ActiveFloor + ".";
            CityModuleHouse.Cell c = h.Find(_hx, h.ActiveFloor, _hz);
            string s = "Клетка " + _hx + ", " + _hz + ": ";
            if (c == null) return s + "пусто";
            return s + CityModuleHouse.KindName(c.Kind) + (c.Locked ? " (заморожена)" : "") + "; стены +X " + CityModuleHouse.WallName(c.PX) + ", −X " + CityModuleHouse.WallName(c.NX) +
                   ", +Z " + CityModuleHouse.WallName(c.PZ) + ", −Z " + CityModuleHouse.WallName(c.NZ);
        }

        /// <summary>Мышь → клетка на текущем этаже (+ ближняя сторона для стен).</summary>
        private static bool Pick(CityModuleHouse h, Vector2 mouse, out int cx, out int cz, out int side)
        {
            cx = 0; cz = 0; side = 0;
            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            Transform t = h.transform;
            Plane pl = new Plane(t.up, t.TransformPoint(new Vector3(0f, h.ActiveFloor * h.Height, 0f)));
            float d;
            if (!pl.Raycast(ray, out d)) return false;
            Vector3 lp = t.InverseTransformPoint(ray.GetPoint(d));
            float P = Mathf.Max(0.01f, h.Pitch);
            cx = Mathf.RoundToInt(lp.x / P); cz = Mathf.RoundToInt(lp.z / P);
            float fx = lp.x / P - cx, fz = lp.z / P - cz;
            side = Mathf.Abs(fx) > Mathf.Abs(fz) ? (fx > 0f ? 0 : 1) : (fz > 0f ? 2 : 3);
            return true;
        }

        private static void DrawCursor(CityModuleHouse h, int cx, int cz, int side, bool shift, bool ctrl)
        {
            float P = h.Pitch, H = h.Height;
            Handles.matrix = h.transform.localToWorldMatrix;
            Vector3 c = h.LocalPos(cx, h.ActiveFloor, cz);
            Color col = h.Tool == 1 || shift ? new Color(1f, 0.3f, 0.3f) : ctrl ? Color.white : h.Tool == 0 ? CityModuleHouse.KindColor(h.Brush) : h.Tool == 3 ? new Color(0.8f, 0.6f, 1f) : Color.yellow;
            Handles.color = col;
            if (h.Tool == 2)
            {
                Vector3 n = new Vector3(DX[side], 0f, DZ[side]), r = new Vector3(-n.z, 0f, n.x);
                Vector3 e = c + n * P * 0.5f;
                Vector3 a = e - r * P * 0.5f, b = e + r * P * 0.5f;
                Handles.DrawAAPolyLine(6f, a, b, b + Vector3.up * H, a + Vector3.up * H, a);
                Handles.DrawWireCube(c + Vector3.up * 0.05f, new Vector3(P, 0.1f, P));
            }
            else
            {
                Handles.DrawWireCube(c + Vector3.up * H * 0.5f, new Vector3(P, H, P));
                Color f = col; f.a = 0.15f;
                Vector3[] q = { c + new Vector3(-P, 0, -P) * 0.5f, c + new Vector3(-P, 0, P) * 0.5f, c + new Vector3(P, 0, P) * 0.5f, c + new Vector3(P, 0, -P) * 0.5f };
                Handles.DrawSolidRectangleWithOutline(q, f, col);
            }
            // сетка вокруг курсора
            Handles.color = new Color(1f, 1f, 1f, 0.12f);
            int R = 6;
            for (int i = -R; i <= R + 1; i++)
            {
                float k = (i - 0.5f) * P;
                Handles.DrawLine(c + new Vector3(k, 0, -(R + 0.5f) * P), c + new Vector3(k, 0, (R + 0.5f) * P));
                Handles.DrawLine(c + new Vector3(-(R + 0.5f) * P, 0, k), c + new Vector3((R + 0.5f) * P, 0, k));
            }
            Handles.matrix = Matrix4x4.identity;
        }

        /// <summary>Клетки текущего этажа — заливкой на полу (видно, что где).</summary>
        private static void DrawFloorCells(CityModuleHouse h)
        {
            if (!h.Painting) return;
            float P = h.Pitch;
            Handles.matrix = h.transform.localToWorldMatrix;
            foreach (CityModuleHouse.Cell c in h.Cells)
            {
                if (c == null || c.Y != h.ActiveFloor) continue;
                Vector3 p = h.LocalPos(c.X, c.Y, c.Z) + Vector3.up * 0.03f;
                Color k = CityModuleHouse.KindColor(c.Kind);
                Color f = k; f.a = c.Locked ? 0.4f : 0.18f;
                float s = P * 0.47f;
                Vector3[] q = { p + new Vector3(-s, 0, -s), p + new Vector3(-s, 0, s), p + new Vector3(s, 0, s), p + new Vector3(s, 0, -s) };
                Handles.DrawSolidRectangleWithOutline(q, f, c.Locked ? new Color(0.8f, 0.6f, 1f) : k);
            }
            Handles.matrix = Matrix4x4.identity;
        }

        private void Apply(CityModuleHouse h, int x, int z, int side, bool shift, bool ctrl)
        {
            int y = h.ActiveFloor;
            if (x == _lastX && z == _lastZ && (h.Tool != 2 || side == _lastS)) return;
            _lastX = x; _lastZ = z; _lastS = side;
            CityModuleHouse.Cell c = h.Find(x, y, z);
            if (ctrl)
            {
                if (c != null) { Undo.RecordObject(h, "Взять тип"); h.Brush = c.Kind; h.Tool = 0; Repaint(); }
                return;
            }
            switch (h.Tool)
            {
                case 0:
                    if (shift) { if (c == null) return; Undo.RecordObject(h, "Стереть клетку"); h.Remove(x, y, z); }
                    else { if (c != null && c.Kind == h.Brush) return; Undo.RecordObject(h, "Клетка"); h.Add(x, y, z, h.Brush); }
                    break;
                case 1:
                    if (c == null) return;
                    Undo.RecordObject(h, "Стереть клетку"); h.Remove(x, y, z);
                    break;
                case 2:
                    {
                        if (c == null) return;
                        Undo.RecordObject(h, "Стена");
                        CityModuleHouse.WallKind w = shift ? CityModuleHouse.WallKind.Auto : h.WallBrush;
                        c.SetWall(side, w);
                        CityModuleHouse.Cell n = h.Find(x + DX[side], y, z + DZ[side]);
                        if (n != null && w != CityModuleHouse.WallKind.Auto && w != CityModuleHouse.WallKind.None) n.SetWall(CityModuleHouse.Opp(side), CityModuleHouse.WallKind.Auto);   // не дублировать
                        break;
                    }
                default:
                    if (c == null) return;
                    Undo.RecordObject(h, "Замок");
                    c.Locked = !shift;
                    EditorUtility.SetDirty(h);
                    return;                                      // заморозка ничего не пересобирает
            }
            EditorUtility.SetDirty(h);
            RebuildRegion(h, x, x, y, y, z, z);
        }

        // ══════════════ ОПЕРАЦИИ С КЛЕТКАМИ ══════════════
        private static void Fill(CityModuleHouse h, int x0, int z0, int w, int d, bool erase)
        {
            Undo.IncrementCurrentGroup();
            int g = Undo.GetCurrentGroup();
            Undo.RecordObject(h, erase ? "Стереть прямоугольник" : "Залить");
            int y = h.ActiveFloor;
            for (int x = x0; x < x0 + w; x++)
                for (int z = z0; z < z0 + d; z++)
                {
                    if (erase) h.Remove(x, y, z); else h.Add(x, y, z, h.Brush);
                }
            EditorUtility.SetDirty(h);
            RebuildRegion(h, x0, x0 + w - 1, y, y, z0, z0 + d - 1);
            Undo.CollapseUndoOperations(g);
        }

        private static void ClearFloor(CityModuleHouse h, int y)
        {
            int x0, x1, z0, z1;
            if (!FloorBox(h, y, out x0, out x1, out z0, out z1)) return;
            Undo.IncrementCurrentGroup();
            int g = Undo.GetCurrentGroup();
            Undo.RecordObject(h, "Очистить этаж");
            h.Cells.RemoveAll(c => c != null && c.Y == y);
            h.Reindex();
            EditorUtility.SetDirty(h);
            RebuildRegion(h, x0, x1, y, y, z0, z1);
            Undo.CollapseUndoOperations(g);
        }

        private static bool FloorBox(CityModuleHouse h, int y, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = int.MaxValue; x1 = int.MinValue; z0 = int.MaxValue; z1 = int.MinValue;
            foreach (CityModuleHouse.Cell c in h.Cells)
            {
                if (c == null || c.Y != y) continue;
                x0 = Mathf.Min(x0, c.X); x1 = Mathf.Max(x1, c.X); z0 = Mathf.Min(z0, c.Z); z1 = Mathf.Max(z1, c.Z);
            }
            return x0 <= x1;
        }

        /// <summary>Скопировать этаж src на этажи t0…t1 (с отражением по X/Z относительно габарита этажа).</summary>
        private static void CopyFloors(CityModuleHouse h, int src, int t0, int t1, bool mx, bool mz)
        {
            int x0, x1, z0, z1;
            if (!FloorBox(h, src, out x0, out x1, out z0, out z1)) { Debug.LogWarning("[Дом-модули] на этаже " + src + " нет клеток.", h); return; }
            if (t1 < t0) { int tt = t0; t0 = t1; t1 = tt; }
            List<CityModuleHouse.Cell> from = new List<CityModuleHouse.Cell>();
            foreach (CityModuleHouse.Cell c in h.Cells) if (c != null && c.Y == src) from.Add(c.Copy());
            Undo.IncrementCurrentGroup();
            int g = Undo.GetCurrentGroup();
            Undo.RecordObject(h, "Копировать этаж");
            int bx0 = x0, bx1 = x1, bz0 = z0, bz1 = z1;
            for (int t = t0; t <= t1; t++)
            {
                if (t == src && !(mx || mz)) continue;
                int tx0, tx1, tz0, tz1;
                if (FloorBox(h, t, out tx0, out tx1, out tz0, out tz1)) { bx0 = Mathf.Min(bx0, tx0); bx1 = Mathf.Max(bx1, tx1); bz0 = Mathf.Min(bz0, tz0); bz1 = Mathf.Max(bz1, tz1); }
                int tl = t;
                h.Cells.RemoveAll(c => c != null && c.Y == tl);
                h.Reindex();
                foreach (CityModuleHouse.Cell f in from)
                {
                    CityModuleHouse.Cell n = f.Copy();
                    n.Y = t;
                    n.Locked = false;
                    if (mx) { n.X = x0 + x1 - f.X; n.PX = f.NX; n.NX = f.PX; n.BeamOff = SwapBits(SwapBits(SwapBits(n.BeamOff, 0, 1), 5, 6), 7, 8); }
                    if (mz) { n.Z = z0 + z1 - f.Z; n.PZ = f.NZ; n.NZ = f.PZ; n.BeamOff = SwapBits(SwapBits(SwapBits(n.BeamOff, 2, 3), 5, 7), 6, 8); }
                    h.Cells.Add(n);
                }
                h.Reindex();
            }
            EditorUtility.SetDirty(h);
            RebuildRegion(h, bx0, bx1, Mathf.Min(t0, src), Mathf.Max(t1, src), bz0, bz1);
            Undo.CollapseUndoOperations(g);
            Debug.Log("[Дом-модули] этаж " + src + " → " + (t0 == t1 ? t0.ToString() : t0 + "…" + t1) + (mx ? ", отражён по X" : "") + (mz ? ", отражён по Z" : "") + ".", h);
        }

        private static int SwapBits(int v, int a, int b)
        {
            bool ba = (v & (1 << a)) != 0, bb = (v & (1 << b)) != 0;
            v &= ~((1 << a) | (1 << b));
            if (ba) v |= 1 << b;
            if (bb) v |= 1 << a;
            return v;
        }

        // ══════════════ СБОРКА ══════════════
        public static void AutoAssign(CityModuleHouse h)
        {
            if (h.BulkModule != null && h.GlassModule != null && h.BrickModule != null) return;
            Undo.RecordObject(h, "Модули");
            CityFloorGenerator gen = Object.FindFirstObjectByType<CityFloorGenerator>();
            if (h.BulkModule == null) h.BulkModule = gen != null && gen.BulkModule != null ? gen.BulkModule : ByGuid(CityFloorGenerator.GuidBulk);
            if (h.GlassModule == null) h.GlassModule = gen != null && gen.GlassModule != null ? gen.GlassModule : ByGuid(CityFloorGenerator.GuidGlass);
            if (h.BrickModule == null) h.BrickModule = gen != null && gen.BrickModule != null ? gen.BrickModule : ByGuid(CityFloorGenerator.GuidBrick);
            EditorUtility.SetDirty(h);
            if (h.BulkModule == null) Debug.LogWarning("[Дом-модули] не нашёл балочный модуль — укажи его в поле Bulk Module.", h);
        }

        private static GameObject ByGuid(string guid)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(p);
        }

        private static Transform Root(CityModuleHouse h, bool create)
        {
            Transform r = h.transform.Find(CityModuleHouse.BuiltName);
            if (r != null || !create) return r;
            GameObject go = new GameObject(CityModuleHouse.BuiltName);
            go.transform.SetParent(h.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Дом");
            return go.transform;
        }

        private static string CellName(int x, int y, int z) { return "C_" + x + "_" + y + "_" + z; }

        public static void DestroyBuilt(CityModuleHouse h)
        {
            Transform r = Root(h, false);
            if (r != null) Undo.DestroyObjectImmediate(r.gameObject);
        }

        public static void RebuildAll(CityModuleHouse h)
        {
            AutoAssign(h);
            if (h.BulkModule == null) return;
            h.Measure();
            h.Reindex();
            Transform r = Root(h, true);
            // сироты и незамороженные — снести
            List<GameObject> kill = new List<GameObject>();
            foreach (Transform ch in r)
            {
                int x, y, z;
                CityModuleHouse.Cell c = ParseName(ch.name, out x, out y, out z) ? h.Find(x, y, z) : null;
                if (c == null || !c.Locked) kill.Add(ch.gameObject);
            }
            foreach (GameObject k in kill) Undo.DestroyObjectImmediate(k);
            if (h.ApplyMaterials) EnsureMaterials(h);
            int n = 0;
            foreach (CityModuleHouse.Cell c in h.Cells)
            {
                if (c == null) continue;
                if (c.Locked && r.Find(CellName(c.X, c.Y, c.Z)) != null) continue;
                GameObject go = BuildCell(h, c, r);
                if (go != null) { Undo.RegisterCreatedObjectUndo(go, "Дом"); n++; }
            }
            if (h.Log) Debug.Log("[Дом-модули] пересобрано клеток: " + n + " (всего " + h.Cells.Count + ").", h);
        }

        private static bool ParseName(string name, out int x, out int y, out int z)
        {
            x = y = z = 0;
            if (!name.StartsWith("C_")) return false;
            string[] p = name.Substring(2).Split('_');
            return p.Length == 3 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y) && int.TryParse(p[2], out z);
        }

        /// <summary>Пересобрать клетки в коробке + соседей (колонны, балки, стены) + этаж над ней (пол снизу).</summary>
        public static void RebuildRegion(CityModuleHouse h, int x0, int x1, int y0, int y1, int z0, int z1)
        {
            AutoAssign(h);
            if (h.BulkModule == null) return;
            h.Reindex();
            Transform r = Root(h, true);
            if (h.ApplyMaterials) EnsureMaterials(h);
            for (int y = y0; y <= y1 + 1; y++)
                for (int x = x0 - 1; x <= x1 + 1; x++)
                    for (int z = z0 - 1; z <= z1 + 1; z++)
                    {
                        CityModuleHouse.Cell c = h.Find(x, y, z);
                        Transform old = r.Find(CellName(x, y, z));
                        if (c != null && c.Locked && old != null) continue;
                        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
                        if (c == null) continue;
                        GameObject go = BuildCell(h, c, r);
                        if (go != null) Undo.RegisterCreatedObjectUndo(go, "Дом");
                    }
        }

        private static int Hash(int x, int y, int z) { unchecked { return x * 73856093 ^ y * 19349663 ^ z * 83492791; } }

        private static GameObject BuildCell(CityModuleHouse h, CityModuleHouse.Cell c, Transform root)
        {
            GameObject cg = new GameObject(CellName(c.X, c.Y, c.Z));
            cg.transform.SetParent(root, false);
            cg.transform.localPosition = h.LocalPos(c.X, c.Y, c.Z);
            System.Random rnd = new System.Random(Hash(c.X, c.Y, c.Z));
            CityModuleHouse.CellKind k = c.Kind;

            // каркас: колонны / балки / плита сверху
            GameObject b = Spawn(h.BulkModule, cg.transform, 0, h.ModuleScale, "Bulk");
            KeepBulkParts(b, CityModuleHouse.HasColumns(k), CityModuleHouse.HasBeams(k), CityModuleHouse.HasSlab(k));
            if (h.HideRoofPlates && h.Find(c.X, c.Y + 1, c.Z) == null) RemoveRoofPlates(h, cg.transform, b);
            Dedupe(h, cg.transform, b, c);
            if (h.ApplyMaterials) Paint(h, b, h.BulkMaterial, rnd);
            FinishSlabs(h, b);

            // пол снизу (если под клеткой нет плиты)
            if (h.FloorUnder && CityModuleHouse.WantsFloor(k))
            {
                CityModuleHouse.Cell below = h.Find(c.X, c.Y - 1, c.Z);
                if (below == null || !CityModuleHouse.HasSlab(below.Kind))
                {
                    GameObject f = Spawn(h.BulkModule, cg.transform, 0, h.ModuleScale, "FloorUnder");
                    f.transform.localPosition = Vector3.down * h.SlabTop;
                    OnlySlab(f);
                    FitSlab(h, cg.transform, f);
                    if (h.ApplyMaterials) Paint(h, f, h.BulkMaterial, rnd);
                    FinishSlabs(h, f);
                }
            }

            // стены
            for (int s = 0; s < 4; s++)
            {
                CityModuleHouse.WallKind w = h.ResolveWall(c, s);
                if (w == CityModuleHouse.WallKind.None || w == CityModuleHouse.WallKind.Auto) continue;
                bool glass = w == CityModuleHouse.WallKind.Glass;
                GameObject src = glass ? h.GlassModule : h.BrickModule;
                if (src == null) continue;
                GameObject wg = Spawn(src, cg.transform, rnd.Next(4), h.ModuleScale, "Wall_" + SideName(s));
                if (!glass) OrientBrick(h, cg.transform, wg, src, s, w != CityModuleHouse.WallKind.BrickBlank);
                int mask = 0;
                if (s < 2)
                {
                    if (h.ResolveWall(c, 3) != CityModuleHouse.WallKind.None) mask |= 1;   // угол со стороны −Z
                    if (h.ResolveWall(c, 2) != CityModuleHouse.WallKind.None) mask |= 2;   // угол со стороны +Z
                }
                KeepSide(h, cg.transform, wg, s, mask);
                if (h.ApplyMaterials) Paint(h, wg, glass ? h.GlassMaterial : h.BrickMaterial, rnd);
                if (h.AddColliders) AddBoxes(wg);
            }
            return cg;
        }

        private static string SideName(int s) { return s == 0 ? "PX" : s == 1 ? "NX" : s == 2 ? "PZ" : "NZ"; }

        private static GameObject Spawn(GameObject src, Transform parent, int rot90, float scale, string name)
        {
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            if (go == null) go = Object.Instantiate(src, parent);
            if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, rot90 * 90f, 0f) * src.transform.localRotation;
            go.transform.localScale = Vector3.Scale(src.transform.localScale, Vector3.one * scale);
            return go;
        }

        /// <summary>Центр рендерера в осях клетки (клетка — дочерний объект дома без поворота).</summary>
        private static Vector3 InCell(Transform cell, Renderer r) { return cell.InverseTransformPoint(r.bounds.center); }

        /// <summary>Габарит рендерера в осях клетки (по углам меша — точно и при повороте дома).</summary>
        private static Bounds LocalBounds(Transform cell, Renderer r)
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            Bounds mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(r.transform.InverseTransformPoint(r.bounds.center), Vector3.zero);
            Matrix4x4 m = cell.worldToLocalMatrix * r.transform.localToWorldMatrix;
            Bounds o = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                o.Encapsulate(m.MultiplyPoint3x4(p));
            }
            return o;
        }

        /// <summary>0 — колонна, 1 — балка, 2 — плита/пластина, −1 — неизвестно.</summary>
        private static int PartOf(Renderer r)
        {
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("vertical")) return 0;
            if (n.Contains("horizontal")) return 1;
            if (n.Contains("beton") || n.Contains("profile") || n.Contains("floor") || n.Contains("connector") || n.Contains("plate")) return 2;
            Bounds b = r.bounds;
            if (b.size.y > Mathf.Max(b.size.x, b.size.z)) return 0;
            if (b.size.y < 0.05f * Mathf.Max(b.size.x, b.size.z)) return 2;
            return 1;
        }

        private static void KeepBulkParts(GameObject mod, bool columns, bool beams, bool slab)
        {
            List<GameObject> kill = new List<GameObject>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                int p = PartOf(r);
                bool keep = p == 0 ? columns : p == 1 ? beams : slab;
                if (!keep) kill.Add(r.gameObject);
            }
            foreach (GameObject k in kill) if (k != null && k != mod) Object.DestroyImmediate(k);
        }

        /// <summary>Пластины-коннекторы наверху колонн (над крышей торчат «крестиками») — убрать.</summary>
        private static void RemoveRoofPlates(CityModuleHouse h, Transform cell, GameObject mod)
        {
            List<GameObject> kill = new List<GameObject>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                if (!n.Contains("connector") && !(n.Contains("plate") && !n.Contains("beton"))) continue;
                if (InCell(cell, r).y > h.Height * 0.6f) kill.Add(r.gameObject);
            }
            foreach (GameObject k in kill) if (k != null && k != mod) Object.DestroyImmediate(k);
        }

        private static void OnlySlab(GameObject mod)
        {
            List<GameObject> kill = new List<GameObject>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
                if (!r.gameObject.name.ToLowerInvariant().Contains("beton")) kill.Add(r.gameObject);
            foreach (GameObject k in kill) if (k != null && k != mod) Object.DestroyImmediate(k);
        }

        private static bool Occ(CityModuleHouse h, int x, int y, int z, int part)
        {
            CityModuleHouse.Cell c = h.Find(x, y, z);
            if (c == null) return false;
            return part == 0 ? CityModuleHouse.HasColumns(c.Kind) : part == 1 ? CityModuleHouse.HasBeams(c.Kind) : CityModuleHouse.HasSlab(c.Kind);
        }

        /// <summary>Общие колонны/балки/пластины — только у одной клетки (как у генератора этажа), с учётом типа соседей.</summary>
        private static void Dedupe(CityModuleHouse h, Transform cell, GameObject mod, CityModuleHouse.Cell c)
        {
            float half = h.Pitch * 0.5f;
            int x = c.X, y = c.Y, z = c.Z;
            List<GameObject> kill = new List<GameObject>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.name.ToLowerInvariant().Contains("beton")) continue;
                int part = PartOf(r);
                Vector3 lc = InCell(cell, r);
                float nx = lc.x / half, nz = lc.z / half;
                bool ex = Mathf.Abs(nx) > 0.6f, ez = Mathf.Abs(nz) > 0.6f;
                int sx = nx > 0f ? 1 : -1, sz = nz > 0f ? 1 : -1;
                bool keep = true;
                if (ex && ez)
                {
                    int kx = x + (sx > 0 ? 1 : 0), kz = z + (sz > 0 ? 1 : 0);
                    int[] cxs = { kx - 1, kx, kx - 1, kx }, czs = { kz - 1, kz - 1, kz, kz };
                    int ox = int.MinValue, oz = int.MinValue;
                    for (int i = 0; i < 4; i++) if (Occ(h, cxs[i], y, czs[i], part)) { ox = cxs[i]; oz = czs[i]; break; }
                    keep = ox == x && oz == z;
                    if (keep && (part == 0 || part == 2) && c.BeamRemoved(CityModuleHouse.Cell.CornerBit(sx, sz))) keep = false;   // колонна убрана на карте (и пластина на ней)
                }
                else if (ex) keep = sx > 0 || !Occ(h, x - 1, y, z, part);
                else if (ez) keep = sz > 0 || !Occ(h, x, y, z - 1, part);
                if (keep && part == 1)                                               // балки, убранные на карте
                {
                    if (ex && !ez) keep = !c.BeamRemoved(sx > 0 ? 0 : 1);
                    else if (ez && !ex) keep = !c.BeamRemoved(sz > 0 ? 2 : 3);
                    else if (!ex && !ez) keep = !c.BeamRemoved(4);
                }
                if (!keep) kill.Add(r.gameObject);
            }
            foreach (GameObject k in kill) if (k != null && k != mod) Object.DestroyImmediate(k);
            FitSlab(h, cell, mod);
        }

        /// <summary>Плита — ровно в клетку (соседние плиты не налезают и не мерцают).</summary>
        private static void FitSlab(CityModuleHouse h, Transform cell, GameObject mod)
        {
            List<Renderer> slabs = new List<Renderer>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name.ToLowerInvariant().Contains("beton")) slabs.Add(r);
            if (slabs.Count == 0) return;
            Bounds sb = LocalBounds(cell, slabs[0]);
            foreach (Renderer r in slabs) sb.Encapsulate(LocalBounds(cell, r));
            float P = h.Pitch;
            float fx = sb.size.x > P * 1.005f ? P / sb.size.x : 1f, fz = sb.size.z > P * 1.005f ? P / sb.size.z : 1f;
            if (fx >= 1f && fz >= 1f) return;
            GameObject fit = new GameObject("SlabFit");
            fit.transform.SetParent(mod.transform, false);
            fit.transform.position = cell.TransformPoint(new Vector3(0f, sb.center.y, 0f));
            fit.transform.rotation = cell.rotation;
            fit.transform.localScale = Vector3.one;
            Vector3 ls = fit.transform.lossyScale;
            foreach (Renderer r in slabs) r.transform.SetParent(fit.transform, true);
            // масштаб в осях клетки: делим на масштаб родителя, чтобы получить ровно fx / fz
            fit.transform.localScale = new Vector3(fx, 1f, fz);
            if (Mathf.Abs(ls.x - ls.z) > 0.001f) Debug.LogWarning("[Дом-модули] у модуля неравный масштаб по X/Z — плита может встать неточно.", mod);
        }

        private static void KeepSide(CityModuleHouse h, Transform cell, GameObject mod, int s, int cornerMask)
        {
            float half = h.Pitch * 0.5f;
            List<GameObject> kill = new List<GameObject>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                Vector3 lc = InCell(cell, r);
                float nx = lc.x / half, nz = lc.z / half;
                bool cx = Mathf.Abs(nx) > 0.85f, cz = Mathf.Abs(nz) > 0.85f;
                bool keep = false;
                if (cx && cz)
                {
                    bool ourX = (s == 0 && nx > 0f) || (s == 1 && nx < 0f);
                    if (s < 2 && ourX) keep = (nz < 0f && (cornerMask & 1) != 0) || (nz > 0f && (cornerMask & 2) != 0);
                }
                else if (cx && !cz) keep = (s == 0 && nx > 0f) || (s == 1 && nx < 0f);
                else if (cz && !cx) keep = (s == 2 && nz > 0f) || (s == 3 && nz < 0f);
                if (!keep) kill.Add(r.gameObject);
            }
            foreach (GameObject k in kill) if (k != null && k != mod) Object.DestroyImmediate(k);
        }

        private static void OrientBrick(CityModuleHouse h, Transform cell, GameObject mod, GameObject src, int s, bool wantWindow)
        {
            float half = h.Pitch * 0.5f;
            for (int k = 0; k < 4; k++)
            {
                mod.transform.localRotation = Quaternion.Euler(0f, k * 90f, 0f) * src.transform.localRotation;
                foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
                {
                    string n = r.gameObject.name.ToLowerInvariant();
                    if (!n.Contains("wall")) continue;
                    Vector3 lc = InCell(cell, r);
                    float nx = lc.x / half, nz = lc.z / half;
                    bool cx = Mathf.Abs(nx) > 0.85f, cz = Mathf.Abs(nz) > 0.85f;
                    bool onSide = (cx && !cz && ((s == 0 && nx > 0f) || (s == 1 && nx < 0f))) || (cz && !cx && ((s == 2 && nz > 0f) || (s == 3 && nz < 0f)));
                    if (!onSide) continue;
                    if (n.Contains("window") == wantWindow) return;
                }
            }
        }

        private static void FinishSlabs(CityModuleHouse h, GameObject mod)
        {
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.gameObject.name.ToLowerInvariant().Contains("beton")) continue;
                if (h.AddColliders && r.GetComponent<Collider>() == null) r.gameObject.AddComponent<BoxCollider>();
                if (h.MarkWet && r.GetComponent<CityWetSurface>() == null)
                {
                    CityWetSurface w = r.gameObject.AddComponent<CityWetSurface>();
                    w.IncludeChildren = false;
                }
            }
        }

        private static void AddBoxes(GameObject mod)
        {
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponent<Collider>() != null) continue;
                r.gameObject.AddComponent<BoxCollider>();
            }
        }

        // ══════════════ МАТЕРИАЛЫ (общие с генератором этажа) ══════════════
        private const string MatFolder = "Assets/Generated/City/Floor";

        private static void EnsureMaterials(CityModuleHouse h)
        {
            if (h.ClearGlassMaterial == null)
            {
                MakeFolder(MatFolder);
                string cp = MatFolder + "/CC_House_GlassClear.mat";
                Material cm = AssetDatabase.LoadAssetAtPath<Material>(cp);
                Shader cs = Shader.Find("CatCooks/City/Glass Clear");
                if (cm == null && cs != null) { cm = new Material(cs); AssetDatabase.CreateAsset(cm, cp); }
                if (cm == null) Debug.LogWarning("[Дом-модули] шейдер CatCooks/City/Glass Clear не найден — положи CityGlassClear.shader в Shaders города.", h);
                Undo.RecordObject(h, "Стекло");
                h.ClearGlassMaterial = cm;
                EditorUtility.SetDirty(h);
            }
            if (h.BulkMaterial != null && h.BrickMaterial != null && h.GlassMaterial != null && h.WindowMaterial != null) return;
            MakeFolder(MatFolder);
            Undo.RecordObject(h, "Материалы");
            CityFloorGenerator gen = Object.FindFirstObjectByType<CityFloorGenerator>();
            if (h.BulkMaterial == null) h.BulkMaterial = gen != null && gen.BulkMaterial != null ? gen.BulkMaterial : ToonMat("CC_Floor_Bulk", Tex(CityFloorGenerator.GuidTexBulk, "bulk_module_AlbedoTransparency"));
            if (h.BrickMaterial == null) h.BrickMaterial = gen != null && gen.BrickMaterial != null ? gen.BrickMaterial : ToonMat("CC_Floor_Brick", Tex(CityFloorGenerator.GuidTexBrick, "brick module_AlbedoTransparency"));
            if (h.GlassMaterial == null) h.GlassMaterial = gen != null && gen.GlassMaterial != null ? gen.GlassMaterial : ToonMat("CC_Floor_GlassFrame", Tex(CityFloorGenerator.GuidTexGlass, "glass_module_AlbedoTransparency"));
            if (h.WindowMaterial == null)
            {
                if (gen != null && gen.WindowMaterial != null) h.WindowMaterial = gen.WindowMaterial;
                else
                {
                    string path = MatFolder + "/CC_Floor_Window.mat";
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    Shader sh = Shader.Find("CatCooks/City/Window Glass");
                    if (m == null && sh != null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                    h.WindowMaterial = m;
                }
            }
            EditorUtility.SetDirty(h);
            AssetDatabase.SaveAssets();
        }

        private static Texture2D Tex(string guid, string search)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D t = string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t != null) return t;
            foreach (string gd in AssetDatabase.FindAssets(search + " t:Texture2D"))
            {
                t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(gd));
                if (t != null) return t;
            }
            return null;
        }

        private static Material ToonMat(string name, Texture2D tex)
        {
            string path = MatFolder + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && tex == null) return m;
            Shader sh = Shader.Find("CatCooks/City/Toon");
            if (sh == null) { Debug.LogError("[Дом-модули] шейдер CatCooks/City/Toon не найден"); return m; }
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            if (tex != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Стёкла окон: прозрачные (дом котов) или городские окна с фейковой комнатой.</summary>
        private static void Paint(CityModuleHouse h, GameObject mod, Material frame, System.Random rnd)
        {
            bool clear = h.Windows == CityModuleHouse.GlassMode.Clear;
            Paint(mod, frame, clear ? h.ClearGlassMaterial : h.WindowMaterial, rnd, !clear);
        }

        private static void Paint(GameObject mod, Material frame, Material window, System.Random rnd, bool cityWindow)
        {
            List<Renderer> wins = new List<Renderer>();
            foreach (Renderer r in mod.GetComponentsInChildren<Renderer>(true))
            {
                if (IsPane(r.gameObject.name)) { wins.Add(r); continue; }
                if (frame == null) continue;
                Material[] ms = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < ms.Length; i++) ms[i] = frame;
                r.sharedMaterials = ms;
            }
            if (window == null) return;
            foreach (Renderer r in wins)
            {
                Material[] ms = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < ms.Length; i++) ms[i] = window;
                r.sharedMaterials = ms;
                if (cityWindow) MarkPane(r.GetComponent<MeshFilter>(), rnd);
                else { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            }
        }

        private static void MarkPane(MeshFilter mf, System.Random rnd)
        {
            if (mf == null || mf.sharedMesh == null) return;
            Mesh src = mf.sharedMesh;
            Vector3[] v = src.vertices;
            if (v == null || v.Length == 0) return;
            Mesh m = Object.Instantiate(src);
            m.name = src.name + "_CCWindow";
            Bounds b = src.bounds;
            bool thinX = b.size.x <= b.size.z && b.size.x <= b.size.y;
            bool thinZ = !thinX && b.size.z <= b.size.y;
            float w = thinX ? b.size.z : b.size.x;
            float hh = thinX || thinZ ? b.size.y : b.size.z;
            Vector2[] u3 = new Vector2[v.Length];
            List<Vector4> u4 = new List<Vector4>(v.Length);
            float rr = (float)rnd.NextDouble();
            for (int i = 0; i < v.Length; i++)
            {
                float a = thinX ? (v[i].z - b.min.z) / Mathf.Max(w, 1e-5f) : (v[i].x - b.min.x) / Mathf.Max(w, 1e-5f);
                float c = (thinX || thinZ) ? (v[i].y - b.min.y) / Mathf.Max(hh, 1e-5f) : (v[i].z - b.min.z) / Mathf.Max(hh, 1e-5f);
                u3[i] = new Vector2(Mathf.Lerp(0.1f, 0.9f, a), c * 0.97f);
                u4.Add(new Vector4(rr, w, 3f, hh));
            }
            m.uv3 = u3;
            m.SetUVs(3, u4);
            mf.sharedMesh = m;
        }

        private static bool IsPane(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("window") && !n.Contains("wall");
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

        // ══════════════ ФЕЙК-ЭТАЖИ ══════════════
        private static Vector3 SideN(int s) { return new Vector3(DX[s], 0f, DZ[s]); }

        private static int SideOf(Vector3 d)
        {
            for (int s = 0; s < 4; s++) if (Vector3.Dot(SideN(s), d) > 0.9f) return s;
            return 0;
        }

        /// <summary>Снять 4 фасада нижнего этажа дома и построить под ним стопку фейк-этажей (CityFakeFloors).</summary>
        public static void MakeFakes(CityModuleHouse h)
        {
            h.Reindex();
            int lo, hi; h.FloorRange(out lo, out hi);
            Transform root = Root(h, false);
            if (root == null || h.Cells.Count == 0) { Debug.LogWarning("[Дом-модули] сначала нарисуй и собери дом.", h); return; }
            List<Renderer> rs = new List<Renderer>();
            List<GameObject> objs = new List<GameObject>();
            foreach (Transform ch in root)
            {
                int x, y, z;
                if (!ParseName(ch.name, out x, out y, out z) || y != lo) continue;
                objs.Add(ch.gameObject);
                rs.AddRange(ch.GetComponentsInChildren<Renderer>());
            }
            if (rs.Count == 0) { Debug.LogWarning("[Дом-модули] на нижнем этаже " + lo + " нет собранных клеток — «Пересобрать всё».", h); return; }
            Transform ht = h.transform;
            Bounds bb = LocalBounds(ht, rs[0]);
            foreach (Renderer r in rs) bb.Encapsulate(LocalBounds(ht, r));
            float H = h.Height, P = h.Pitch, baseY = lo * H;
            int ppu = Mathf.Max(4, h.FakePixelsPerUnit);

            const int layer = 31;
            Dictionary<GameObject, int> old = new Dictionary<GameObject, int>();
            foreach (GameObject o in objs) foreach (Transform t in o.GetComponentsInChildren<Transform>(true)) { old[t.gameObject] = t.gameObject.layer; t.gameObject.layer = layer; }
            Texture2D[] sides = new Texture2D[4];
            float[] along = new float[4];
            try
            {
                for (int s = 0; s < 4; s++)
                {
                    along[s] = s < 2 ? bb.size.z : bb.size.x;
                    float depth = s < 2 ? bb.extents.x : bb.extents.z;
                    Vector3 cw = ht.TransformPoint(new Vector3(bb.center.x, baseY + H * 0.5f, bb.center.z));
                    sides[s] = Capture(h, cw, ht.TransformDirection(SideN(s)), ht.up, depth, along[s], H, ppu, layer);
                }
            }
            finally
            {
                foreach (KeyValuePair<GameObject, int> kv in old) if (kv.Key != null) kv.Key.layer = kv.Value;
            }

            int W = 1, rowH = sides[0].height + 4;
            int[] sw = new int[4];
            for (int s = 0; s < 4; s++) { sw[s] = sides[s].width; W = Mathf.Max(W, sw[s]); }
            int AH = rowH * 4;
            Texture2D atlas = new Texture2D(W, AH, TextureFormat.RGBA32, false);
            Color32[] fill = new Color32[W * AH];
            Color32 bg = h.FakeBackground;
            for (int i = 0; i < fill.Length; i++) fill[i] = bg;
            atlas.SetPixels32(fill);
            for (int s = 0; s < 4; s++)
            {
                atlas.SetPixels(0, s * rowH + 2, sides[s].width, sides[s].height, sides[s].GetPixels());
                Object.DestroyImmediate(sides[s]);
            }
            CityFakeTone.Apply(atlas, bg);
            atlas.Apply();
            MakeFolder(MatFolder);
            string tag = "House_" + System.Text.RegularExpressions.Regex.Replace(h.gameObject.name, "[^A-Za-z0-9_]", "") + "_" + CityFakeTone.UniqueId(h);
            string texPath = MatFolder + "/CC_FakeFacade_" + tag + ".png";
            System.IO.File.WriteAllBytes(texPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            TextureImporter ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (ti != null) { ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = 4096; ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None; ti.SaveAndReimport(); }
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            Material mat = ToonMat("CC_FakeFloors_" + tag, tex);
            if (mat != null && mat.HasProperty("_Unlit")) { mat.SetFloat("_Unlit", h.FakeUnlit); EditorUtility.SetDirty(mat); }

            Transform ft = h.transform.Find(CityModuleHouse.FakeName);
            if (ft == null)
            {
                GameObject go = new GameObject(CityModuleHouse.FakeName);
                Undo.RegisterCreatedObjectUndo(go, "Фейк-этажи");
                go.transform.SetParent(h.transform, false);
                ft = go.transform;
            }
            ft.localPosition = Vector3.zero; ft.localRotation = Quaternion.identity; ft.localScale = Vector3.one;
            CityFakeFloors ff = ft.GetComponent<CityFakeFloors>();
            if (ff == null) ff = Undo.AddComponent<CityFakeFloors>(ft.gameObject);
            Undo.RecordObject(ff, "Фейк-этажи");

            // кольцо стен по контуру нижнего этажа
            float[] gridFace = { -1e9f, -1e9f, -1e9f, -1e9f };
            foreach (CityModuleHouse.Cell c in h.Cells)
            {
                if (c == null || c.Y != lo) continue;
                for (int s = 0; s < 4; s++) gridFace[s] = Mathf.Max(gridFace[s], Vector3.Dot(h.LocalPos(c.X, 0, c.Z) + SideN(s) * P * 0.5f, SideN(s)));
            }
            float[] bbFace = { bb.max.x, -bb.min.x, bb.max.z, -bb.min.z };
            float[] wallOut = new float[4];
            for (int s = 0; s < 4; s++) wallOut[s] = Mathf.Max(0f, bbFace[s] - gridFace[s]);
            ff.Edges.Clear(); ff.EdgeUV.Clear(); ff.EdgeN.Clear();
            foreach (CityModuleHouse.Cell c in h.Cells)
            {
                if (c == null || c.Y != lo) continue;
                for (int s = 0; s < 4; s++)
                {
                    if (h.Find(c.X + DX[s], lo, c.Z + DZ[s]) != null) continue;
                    Vector3 n = SideN(s);
                    Vector3 r = Vector3.Cross(Vector3.up, -n).normalized;
                    Vector3 cc = h.LocalPos(c.X, 0, c.Z);
                    Vector3 ec = cc + n * (P * 0.5f + wallOut[s]);
                    Vector3 a = ec - r * P * 0.5f, b = ec + r * P * 0.5f;
                    int sl = SideOf(-r), sr = SideOf(r);
                    if (h.Find(c.X + DX[sl], lo, c.Z + DZ[sl]) == null) a -= r * wallOut[sl];
                    if (h.Find(c.X + DX[sr], lo, c.Z + DZ[sr]) == null) b += r * wallOut[sr];
                    float L = Mathf.Max(along[s], 1e-3f);
                    Vector3 bc = new Vector3(bb.center.x, 0f, bb.center.z);
                    float ua = Vector3.Dot(a - bc, r) / L + 0.5f, ub = Vector3.Dot(b - bc, r) / L + 0.5f;
                    float k = (float)sw[s] / W;
                    float v0 = (s * rowH + 2f + 0.5f) / AH, v1 = (s * rowH + 2f + (rowH - 4) - 0.5f) / AH;
                    ff.Edges.Add(new Vector4(a.x, a.z, b.x, b.z));
                    ff.EdgeUV.Add(new Vector4(Mathf.Clamp01(ua) * k, Mathf.Clamp01(ub) * k, v0, v1));
                    ff.EdgeN.Add(n);
                }
            }
            if (h.FakeFloors <= 0) { Undo.RecordObject(h, "Фейк-этажи"); h.FakeFloors = 6; }
            ff.TopY = baseY;
            ff.FloorH = H;
            ff.Floors = h.FakeFloors;
            ff.Material = mat;
            EditorUtility.SetDirty(ff);
            ff.Rebuild();
            Debug.Log("[Дом-модули] фейк-этажи: " + ff.Floors + " под этажом " + lo + ", стен в кольце " + ff.Edges.Count + " (" + texPath + "). Сколько этажей — поле Floors у объекта FakeFloors.", ff);
        }

        private static Texture2D Capture(CityModuleHouse h, Vector3 center, Vector3 n, Vector3 up, float depth, float along, float H, int ppu, int layer)
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(along * ppu), 16, 4096), hh = Mathf.Clamp(Mathf.RoundToInt(H * ppu), 16, 2048);
            GameObject cg = new GameObject("CC_FacadeCam");
            cg.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = cg.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = H * 0.5f;
            cam.aspect = along / H;
            cg.transform.position = center + n * (depth + 2f);
            cg.transform.rotation = Quaternion.LookRotation(-n, up);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = depth * 2f + 4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = h.FakeBackground;
            cam.cullingMask = 1 << layer;
            UniversalAdditionalCameraData ad = cam.GetUniversalAdditionalCameraData();
            if (ad != null) ad.renderPostProcessing = false;
            RenderTexture rt = new RenderTexture(w, hh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(w, hh, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, hh), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(cg);
            return tex;
        }

        // ══════════════ МЕНЮ ══════════════
        [MenuItem("CatCooks/Застройка/Дом из модулей (кубиками): поставить", priority = 961)]
        public static void Place()
        {
            GameObject go = new GameObject("Cat House (Modules)");
            Undo.RegisterCreatedObjectUndo(go, "Дом из модулей");
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv != null) go.transform.position = sv.pivot;
            CityModuleHouse h = go.AddComponent<CityModuleHouse>();
            AutoAssign(h);
            h.Measure();
            h.Painting = true;
            Selection.activeObject = go;
            Debug.Log("[Дом-модули] дом поставлен. В инспекторе «Залить» 10×10 или рисуй мышкой в сцене.", h);
        }
    }
}
