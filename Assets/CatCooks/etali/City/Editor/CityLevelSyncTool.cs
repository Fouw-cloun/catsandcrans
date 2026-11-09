using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// СИНХРОНИЗАЦИЯ ВЫСОТ ЭТАЖЕЙ. Находит все генераторы этажей и дома из модулей в сцене, показывает масштаб, шаг,
    /// высоту этажа и Y, берёт один из них за эталон: копирует ему ModuleScale / CellSize / FloorHeight на остальных
    /// и опускает/поднимает их так, чтобы пол попадал на уровень эталона + целое число этажей.
    /// Ничего не создаёт кроме пересборки самих домов; префабы не трогает.
    /// </summary>
    public class CityLevelSyncTool : EditorWindow
    {
        private struct Item
        {
            public Component C;
            public float Scale, Pitch, Height, Y;
        }

        private readonly List<Item> _items = new List<Item>();
        private int _ref;
        private bool _copyScale = true, _snapY = true, _rebuild = true, _fakes;
        private Vector2 _scroll;
        private float _groundY = 0f;

        [MenuItem("CatCooks/Застройка/Этажи: синхронизировать высоты (столовая, кирпич, стройка)", priority = 964)]
        public static void Open()
        {
            CityLevelSyncTool w = GetWindow<CityLevelSyncTool>("Высоты этажей");
            w.minSize = new Vector2(520, 320);
            w.Scan();
        }

        private void Scan()
        {
            _items.Clear();
            CityFloorGenerator[] gs = FindObjectsByType<CityFloorGenerator>(FindObjectsSortMode.None);
            CityModuleHouse[] hs = FindObjectsByType<CityModuleHouse>(FindObjectsSortMode.None);
            for (int i = 0; i < gs.Length; i++) _items.Add(Make(gs[i], gs[i].ModuleScale, gs[i].Pitch, gs[i].Height));
            for (int i = 0; i < hs.Length; i++) _items.Add(Make(hs[i], hs[i].ModuleScale, hs[i].Pitch, hs[i].Height));
            _items.Sort(delegate (Item a, Item b) { return string.CompareOrdinal(a.C.name, b.C.name); });
            if (_ref >= _items.Count) _ref = 0;
        }

        private static Item Make(Component c, float s, float p, float h)
        {
            Item it = new Item();
            it.C = c; it.Scale = s; it.Pitch = p; it.Height = h; it.Y = c.transform.position.y;
            return it;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Эталон — тот этаж, по которому равняются остальные (обычно рабочий этаж игрока). У остальных станет тот же масштаб модуля и высота этажа, а Y — эталон + целое число этажей.", MessageType.Info);
            if (GUILayout.Button("Найти заново")) Scan();
            if (_items.Count == 0) { EditorGUILayout.LabelField("В сцене нет ни генераторов этажа, ни домов из модулей."); return; }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _items.Count; i++)
            {
                Item it = _items[i];
                if (it.C == null) continue;
                EditorGUILayout.BeginHorizontal();
                bool r = GUILayout.Toggle(_ref == i, "эталон", GUILayout.Width(60));
                if (r) _ref = i;
                string kind = it.C is CityFloorGenerator ? "[этаж] " : "[дом] ";
                if (GUILayout.Button(kind + it.C.name, EditorStyles.label)) Selection.activeObject = it.C.gameObject;
                GUILayout.Label("масштаб " + it.Scale.ToString("0.###") + "  шаг " + it.Pitch.ToString("0.###") + "  высота " + it.Height.ToString("0.###") + "  Y " + it.Y.ToString("0.###"), GUILayout.Width(330));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(64);
                if (GUILayout.Button("На землю + 1 фейк-этаж", GUILayout.Width(170))) { ToGround(it.C); Scan(); }
                if (GUILayout.Button("▲ на этаж вверх", GUILayout.Width(120))) { Shift(it.C, 1); Scan(); }
                if (GUILayout.Button("▼ на этаж вниз", GUILayout.Width(120))) { Shift(it.C, -1); Scan(); }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            _groundY = EditorGUILayout.FloatField("Y земли", _groundY);
            if (Selection.activeTransform != null && GUILayout.Button("Взять Y выделенного", GUILayout.Width(150))) _groundY = Selection.activeTransform.position.y;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("▲ ВСЕ вверх на этаж", GUILayout.Height(26))) { CityLevelStack st = FindOrMakeStack(AllComps()); CityLevelStackEditor.RaiseAll(st, 1); Scan(); }
            if (GUILayout.Button("▼ ВСЕ вниз на этаж", GUILayout.Height(26))) { CityLevelStack st = FindOrMakeStack(AllComps()); CityLevelStackEditor.RaiseAll(st, -1); Scan(); }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Привязать пропсы к этажам (дети «Props», едут вместе)")) { CityLevelStack st = FindOrMakeStack(AllComps()); CityLevelStackEditor.BindAll(st); }
            if (GUILayout.Button("Все на землю: по 1 фейк-этажу (фоткой снизу)", GUILayout.Height(26)))
            {
                for (int i = 0; i < _items.Count; i++) if (_items[i].C != null) ToGround(_items[i].C);
                Scan();
            }

            _copyScale = EditorGUILayout.ToggleLeft("Скопировать масштаб модуля, шаг и высоту этажа с эталона", _copyScale);
            _snapY = EditorGUILayout.ToggleLeft("Подогнать Y: эталон + целое число этажей (ближайшее)", _snapY);
            _rebuild = EditorGUILayout.ToggleLeft("Пересобрать после правки", _rebuild);
            _fakes = EditorGUILayout.ToggleLeft("И пересоздать зеркальные нижние этажи (фейк-этажи)", _fakes);

            if (GUILayout.Button("Синхронизировать", GUILayout.Height(30))) Apply();
        }

        private void Apply()
        {
            if (_ref < 0 || _ref >= _items.Count || _items[_ref].C == null) return;
            Item r = _items[_ref];
            float h = r.Height;
            if (h < 0.01f) { Debug.LogWarning("[Высоты] у эталона нулевая высота этажа — сначала собери его."); return; }
            int changed = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (i == _ref || _items[i].C == null) continue;
                Component c = _items[i].C;
                Undo.RecordObject(c, "Синхронизация высот");
                Undo.RecordObject(c.transform, "Синхронизация высот");
                CityFloorGenerator g = c as CityFloorGenerator;
                CityModuleHouse m = c as CityModuleHouse;
                if (_copyScale)
                {
                    float cs = r.C is CityFloorGenerator ? ((CityFloorGenerator)r.C).CellSize : ((CityModuleHouse)r.C).CellSize;
                    float fh = r.C is CityFloorGenerator ? ((CityFloorGenerator)r.C).FloorHeight : ((CityModuleHouse)r.C).FloorHeight;
                    if (g != null) { g.ModuleScale = r.Scale; g.CellSize = cs; g.FloorHeight = fh; }
                    if (m != null) { m.ModuleScale = r.Scale; m.CellSize = cs; m.FloorHeight = fh; }
                }
                if (_snapY)
                {
                    Vector3 p = c.transform.position;
                    float n = Mathf.Round((p.y - r.Y) / h);
                    CityLevelStackEditor.MoveWithPassengers(c, r.Y + n * h - p.y, AllComps());
                }
                EditorUtility.SetDirty(c);
                if (_rebuild)
                {
                    if (g != null) { CityFloorGeneratorEditor.Build(g); if (_fakes) CityFloorGeneratorEditor.MakeFakes(g); }
                    if (m != null) { CityModuleHouseEditor.RebuildAll(m); if (_fakes) CityModuleHouseEditor.MakeFakes(m); }
                }
                changed++;
                Debug.Log("[Высоты] «" + c.name + "» подогнан под эталон «" + r.C.name + "», Y = " + c.transform.position.y.ToString("0.###"), c);
            }
            Debug.Log("[Высоты] готово, подогнано объектов: " + changed + ". Высота этажа эталона " + h.ToString("0.###") + " м.");
            Scan();
        }

        private static int FakeCount(Component c)
        {
            CityFloorGenerator g = c as CityFloorGenerator;
            if (g != null) return g.FakeFloors;
            return ((CityModuleHouse)c).FakeFloors;
        }

        private static void SetFakeCount(Component c, int n)
        {
            CityFloorGenerator g = c as CityFloorGenerator;
            if (g != null) g.FakeFloors = Mathf.Clamp(n, 0, 60);
            else ((CityModuleHouse)c).FakeFloors = Mathf.Clamp(n, 0, 60);
        }

        /// <summary>Самая нижняя точка собранной геометрии (без фейк-этажей).</summary>
        private static bool BuiltMinY(Component c, out float minY)
        {
            minY = 0f;
            Transform root = c is CityFloorGenerator ? c.transform.Find(CityFloorGenerator.RootName) : c.transform.Find(CityModuleHouse.BuiltName);
            if (root == null) return false;
            Renderer[] rs = root.GetComponentsInChildren<Renderer>();
            bool any = false;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] is ParticleSystemRenderer) continue;
                float y = rs[i].bounds.min.y;
                if (!any || y < minY) { minY = y; any = true; }
            }
            return any;
        }

        private List<Component> AllComps()
        {
            List<Component> l = new List<Component>();
            for (int i = 0; i < _items.Count; i++) if (_items[i].C != null) l.Add(_items[i].C);
            return l;
        }

        private void ToGround(Component c)
        {
            CityLevelStackEditor.ToGround(c, _groundY, AllComps());
        }

        private static CityLevelStack FindOrMakeStack(List<Component> all)
        {
            CityLevelStack s = Object.FindFirstObjectByType<CityLevelStack>();
            if (s == null)
            {
                GameObject go = new GameObject("CC_LevelStack");
                Undo.RegisterCreatedObjectUndo(go, "Общий рост этажей");
                s = go.AddComponent<CityLevelStack>();
            }
            Undo.RecordObject(s, "Общий рост этажей");
            s.Targets.Clear();
            for (int i = 0; i < all.Count; i++) s.Targets.Add(all[i]);
            EditorUtility.SetDirty(s);
            return s;
        }

        /// <summary>Поднять/опустить один этаж на n этажей; фейк-этажей снизу столько же прибавляется/убирается, пропсы внутри едут вместе.</summary>
        private void Shift(Component c, int n)
        {
            float H = CityLevelStack.HeightOf(c);
            int cur = CityLevelStack.FakesOf(c);
            if (n < 0 && cur + n < 0) { Debug.LogWarning("[Высоты] «" + c.name + "»: ниже земли опускать некуда (фейк-этажей уже 0).", c); return; }
            Undo.RecordObject(c, "Сдвиг на этаж");
            CityLevelStackEditor.MoveWithPassengers(c, n * H, AllComps());
            Transform ft = c.transform.Find("FakeFloors");
            CityFakeFloors ff = ft != null ? ft.GetComponent<CityFakeFloors>() : null;
            if (ff != null) Undo.RecordObject(ff, "Сдвиг на этаж");
            CityLevelStack.SetFakes(c, cur + n);
            EditorUtility.SetDirty(c);
            if (ff == null && cur + n > 0) MakeFakes(c);
            Debug.Log("[Высоты] «" + c.name + "» " + (n > 0 ? "поднят" : "опущен") + " на " + Mathf.Abs(n) + " этаж, фейк-этажей снизу: " + (cur + n) + ". Y = " + c.transform.position.y.ToString("0.###"), c);
        }

        private static void MakeFakes(Component c)
        {
            if (c is CityFloorGenerator) CityFloorGeneratorEditor.MakeFakes((CityFloorGenerator)c);
            else CityModuleHouseEditor.MakeFakes((CityModuleHouse)c);
        }
    }
}
