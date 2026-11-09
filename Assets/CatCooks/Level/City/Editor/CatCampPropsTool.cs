using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// СТОЛОВАЯ И БЫТОВКИ КОТОВ — генератор мультяшных пропсов из простых коробок и цилиндров (цвет в вершинах,
    /// один материал CatCooks/City/Toon на всё → SRP Batcher, дёшево). Меши сохраняются ассетами
    /// (Assets/Generated/City/CatCamp), в сцену ставятся обычные объекты — префабы не создаются, их делаешь сам.
    /// Повторная генерация перезаписывает тот же меш — всё, что уже стоит в сцене, обновится.
    /// </summary>
    public class CatCampPropsTool : EditorWindow
    {
        public enum CabinColor { Blue, White, Yellow, Green }
        public enum ColliderMode { None, Box, Mesh }

        private const string Folder = "Assets/Generated/City/CatCamp";
        private const string RootName = "CatCamp (props)";

        private float _scale = 1f;
        private int _seats = 6;
        private bool _dishes = true;
        private CabinColor _cabin = CabinColor.Blue;
        private int _rows = 3, _cols = 2;
        private ColliderMode _col = ColliderMode.Box;
        private bool _shadows = true;
        private Vector2 _scroll;

        // палитра
        private static readonly Color Wood = new Color(0.80f, 0.57f, 0.34f);
        private static readonly Color WoodDark = new Color(0.58f, 0.39f, 0.23f);
        private static readonly Color Metal = new Color(0.58f, 0.63f, 0.68f);
        private static readonly Color MetalLight = new Color(0.78f, 0.81f, 0.84f);
        private static readonly Color MetalDark = new Color(0.30f, 0.33f, 0.37f);
        private static readonly Color White = new Color(0.94f, 0.94f, 0.91f);
        private static readonly Color GlassC = new Color(0.30f, 0.42f, 0.56f);
        private static readonly Color GuardC = new Color(0.70f, 0.86f, 0.92f);
        private static readonly Color Tray = new Color(0.97f, 0.58f, 0.26f);
        private static readonly Color Bowl = new Color(0.92f, 0.33f, 0.30f);
        private static readonly Color Food = new Color(0.98f, 0.86f, 0.45f);
        private static readonly Color Black = new Color(0.14f, 0.14f, 0.16f);
        private static readonly Color Teal = new Color(0.30f, 0.72f, 0.72f);
        private static readonly Color GreenL = new Color(0.40f, 0.85f, 0.40f);
        private static readonly Color BinC = new Color(0.30f, 0.58f, 0.36f);

        [MenuItem("CatCooks/Застройка/Столовая и бытовки котов…", priority = 962)]
        public static void Open()
        {
            CatCampPropsTool w = GetWindow<CatCampPropsTool>("Столовая и бытовки");
            w.minSize = new Vector2(320, 420);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("Ставит объект в центр вида сцены (или рядом с выделенным). Размеры в метрах для котов «человеческого» роста — " +
                                    "подгони «Масштабом». Меши общие: сгенерировал заново — обновились все копии в сцене. Префабы делай сам, перетащив объект в папку.", MessageType.Info);
            Color ob = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.55f, 1f, 0.6f);
            if (GUILayout.Button("★ Сгенерировать весь набор (по одному, в ряд)", GUILayout.Height(36))) All();
            GUI.backgroundColor = ob;
            EditorGUILayout.LabelField("Один клик: все столы, раздача, бак, 4 бытовки, туалет, душ, умывальник. Дальше Ctrl+D и расставляй руками.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            _scale = EditorGUILayout.Slider("Масштаб", _scale, 0.3f, 4f);
            _col = (ColliderMode)EditorGUILayout.EnumPopup("Коллайдер", _col);
            _shadows = EditorGUILayout.Toggle("Тени", _shadows);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("По отдельности (если нужен один предмет)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Столовая", EditorStyles.miniBoldLabel);
            _seats = EditorGUILayout.IntSlider("Мест с каждой стороны", _seats, 2, 16);
            _dishes = EditorGUILayout.Toggle("Подносы и миски на столе", _dishes);
            if (GUILayout.Button("Длинный стол с лавками (" + _seats * 2 + " котов)")) Put("LongTable", LongTable(_seats, _dishes), true);
            if (GUILayout.Button("Круглый столик + 4 табуретки")) Put("RoundTable", RoundTable(_dishes), true);
            if (GUILayout.Button("Раздача (прилавок с кастрюлями)")) Put("ServingCounter", Counter(), true);
            if (GUILayout.Button("Мусорный бак")) Put("TrashBin", Bin(), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                _rows = EditorGUILayout.IntField("Рядов", Mathf.Clamp(_rows, 1, 20));
                _cols = EditorGUILayout.IntField("Столов в ряду", Mathf.Clamp(_cols, 1, 10));
            }
            if (GUILayout.Button("Зал столовой: " + _rows + "×" + _cols + " столов (" + _rows * _cols * _seats * 2 + " котов)")) Hall();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Бытовки", EditorStyles.boldLabel);
            _cabin = (CabinColor)EditorGUILayout.EnumPopup("Цвет бытовки", _cabin);
            if (GUILayout.Button("Бытовка (вагончик-контейнер)")) Put("Cabin_" + _cabin, Cabin(_cabin), true);
            if (GUILayout.Button("Биотуалет")) Put("BioToilet", Toilet(), true);
            if (GUILayout.Button("Душ (с баком на крыше)")) Put("Shower", Shower(), true);
            if (GUILayout.Button("Умывальник на 2 крана")) Put("Washstand", Washstand(), true);

            EditorGUILayout.EndScrollView();
        }

        // ══════════════ ПОСТАНОВКА ══════════════
        private static Material Mat()
        {
            MakeFolder(Folder);
            string path = Folder + "/CC_CatCamp.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find("CatCooks/City/Toon");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Сохранить меш ассетом (тот же путь — обновить на месте, ссылки в сцене не теряются).</summary>
        private static Mesh Save(Mesh m)
        {
            MakeFolder(Folder);
            string path = Folder + "/" + m.name + ".asset";
            Mesh old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old == null) { AssetDatabase.CreateAsset(m, path); return m; }
            EditorUtility.CopySerialized(m, old);
            old.name = m.name;
            Object.DestroyImmediate(m);
            EditorUtility.SetDirty(old);
            return old;
        }

        private Vector3 Spot()
        {
            if (Selection.activeTransform != null) return Selection.activeTransform.position + Selection.activeTransform.right * 3f * _scale;
            SceneView sv = SceneView.lastActiveSceneView;
            return sv != null ? sv.pivot : Vector3.zero;
        }

        private static Transform Root()
        {
            GameObject r = GameObject.Find(RootName);
            if (r == null) { r = new GameObject(RootName); Undo.RegisterCreatedObjectUndo(r, "Пропсы котов"); }
            return r.transform;
        }

        private GameObject Make(string name, Mesh mesh, Vector3 pos, Quaternion rot)
        {
            GameObject go = CityToonMesh.Make("CC_" + name, Root(), mesh, Mat(), _shadows);
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = Vector3.one * _scale;
            if (_col == ColliderMode.Box) go.AddComponent<BoxCollider>();
            else if (_col == ColliderMode.Mesh) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            Undo.RegisterCreatedObjectUndo(go, "Пропс котов");
            return go;
        }

        private void Put(string name, CityToonMesh t, bool select)
        {
            Mesh m = Save(t.Build("CC_" + name));
            GameObject go = Make(name, m, Spot(), Quaternion.identity);
            AssetDatabase.SaveAssets();
            if (select) Selection.activeObject = go;
            Debug.Log("[Пропсы котов] поставил " + go.name + " (меш " + Folder + "/" + m.name + ".asset, " + m.vertexCount + " вершин).", go);
        }

        private void Hall()
        {
            Mesh m = Save(LongTable(_seats, _dishes).Build("CC_LongTable"));
            float L = (_seats * 0.62f + 0.3f) * _scale, step = 2.9f * _scale;
            Vector3 c = Spot();
            GameObject hall = new GameObject("CC_CanteenHall_" + _rows + "x" + _cols);
            hall.transform.SetParent(Root(), false);
            hall.transform.position = c;
            Undo.RegisterCreatedObjectUndo(hall, "Зал столовой");
            for (int r = 0; r < _rows; r++)
                for (int k = 0; k < _cols; k++)
                {
                    Vector3 p = c + new Vector3((k - (_cols - 1) * 0.5f) * (L + 1.4f * _scale), 0f, (r - (_rows - 1) * 0.5f) * step);
                    GameObject go = Make("LongTable", m, p, Quaternion.identity);
                    Undo.SetTransformParent(go.transform, hall.transform, "Зал");
                }
            AssetDatabase.SaveAssets();
            Selection.activeObject = hall;
            Debug.Log("[Пропсы котов] зал столовой: " + _rows * _cols + " столов, мест " + _rows * _cols * _seats * 2 + ".", hall);
        }

        /// <summary>Весь набор по одному, в ряд, одной группой — дальше дублируешь и расставляешь руками.</summary>
        private void All()
        {
            Vector3 c = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            string[] names = { "LongTable", "RoundTable", "ServingCounter", "TrashBin", "Cabin_Blue", "Cabin_White", "Cabin_Yellow", "Cabin_Green", "BioToilet", "Shower", "Washstand" };
            CityToonMesh[] ts =
            {
                LongTable(_seats, _dishes), RoundTable(_dishes), Counter(), Bin(),
                Cabin(CabinColor.Blue), Cabin(CabinColor.White), Cabin(CabinColor.Yellow), Cabin(CabinColor.Green),
                Toilet(), Shower(), Washstand()
            };
            GameObject set = new GameObject("CC_CatCampSet");
            set.transform.SetParent(Root(), false);
            set.transform.position = c;
            Undo.RegisterCreatedObjectUndo(set, "Набор пропсов котов");
            float x = 0f;
            for (int i = 0; i < names.Length; i++)
            {
                Mesh m = Save(ts[i].Build("CC_" + names[i]));
                float w = m.bounds.size.x * _scale;
                GameObject go = Make(names[i], m, c + Vector3.right * (x + w * 0.5f), Quaternion.identity);
                Undo.SetTransformParent(go.transform, set.transform, "Набор");
                x += w + 1f * _scale;
            }
            AssetDatabase.SaveAssets();
            Selection.activeObject = set;
            Debug.Log("[Пропсы котов] набор готов: " + names.Length + " предметов в ряд у центра вида (группа CC_CatCampSet). Дублируй нужные (Ctrl+D) и расставляй руками.", set);
        }

        // ══════════════ СТОЛОВАЯ ══════════════
        /// <summary>Длинный стол на козлах + две лавки, подносы с мисками у каждого места.</summary>
        private static CityToonMesh LongTable(int seats, bool dishes)
        {
            CityToonMesh t = new CityToonMesh();
            float sw = 0.62f, L = seats * sw + 0.3f, H = 0.76f, D = 0.84f;
            t.Box(new Vector3(0, H - 0.03f, 0), new Vector3(L, 0.06f, D), Wood);
            t.Box(new Vector3(0, H - 0.075f, D * 0.5f - 0.04f), new Vector3(L - 0.1f, 0.04f, 0.04f), WoodDark);   // царга
            t.Box(new Vector3(0, H - 0.075f, -D * 0.5f + 0.04f), new Vector3(L - 0.1f, 0.04f, 0.04f), WoodDark);
            int legs = Mathf.Max(2, Mathf.CeilToInt(L / 1.9f) + 1);
            for (int i = 0; i < legs; i++)
            {
                float x = Mathf.Lerp(-L * 0.5f + 0.18f, L * 0.5f - 0.18f, (float)i / (legs - 1));
                Trestle(t, x, H - 0.06f, D * 0.8f, Metal);
                // лавки: опоры под тем же местом
                for (int s = -1; s <= 1; s += 2) t.Box(new Vector3(x, 0.215f, s * 0.78f), new Vector3(0.05f, 0.43f, 0.26f), Metal);
            }
            t.Box(new Vector3(0, 0.25f, 0), new Vector3(L - 0.36f, 0.05f, 0.05f), MetalDark);                  // стяжка
            for (int s = -1; s <= 1; s += 2)
            {
                t.Box(new Vector3(0, 0.455f, s * 0.78f), new Vector3(L, 0.05f, 0.32f), Wood);
                t.Box(new Vector3(0, 0.415f, s * 0.78f), new Vector3(L - 0.1f, 0.03f, 0.06f), WoodDark);
            }
            if (dishes)
            {
                System.Random rnd = new System.Random(seats * 17 + 3);
                for (int s = -1; s <= 1; s += 2)
                    for (int i = 0; i < seats; i++)
                    {
                        if (rnd.NextDouble() < 0.2) continue;                                        // кое-где пусто
                        float x = -L * 0.5f + 0.15f + sw * (i + 0.5f) + (float)(rnd.NextDouble() - 0.5) * 0.08f;
                        float z = s * (D * 0.5f - 0.2f);
                        Dish(t, new Vector3(x, H, z), rnd);
                    }
            }
            return t;
        }

        private static void Trestle(CityToonMesh t, float x, float top, float span, Color c)
        {
            float h = top;
            t.Beam(new Vector3(x, 0f, -span * 0.5f), new Vector3(x, h, -span * 0.18f), 0.05f, c);
            t.Beam(new Vector3(x, 0f, span * 0.5f), new Vector3(x, h, span * 0.18f), 0.05f, c);
            t.Box(new Vector3(x, h - 0.02f, 0f), new Vector3(0.06f, 0.04f, span * 0.6f), c);
            t.Box(new Vector3(x, 0.02f, 0f), new Vector3(0.08f, 0.04f, span + 0.06f), MetalDark);
        }

        private static void Dish(CityToonMesh t, Vector3 p, System.Random rnd)
        {
            t.Box(p + new Vector3(0, 0.012f, 0), new Vector3(0.36f, 0.024f, 0.26f), Tray);
            Color b = rnd.NextDouble() < 0.5 ? Bowl : new Color(0.35f, 0.6f, 0.95f);
            t.Cylinder(p + new Vector3(-0.06f, 0.024f, 0), 0.085f, 0.055f, 10, b);
            t.Disc(p + new Vector3(-0.06f, 0.081f, 0), Vector3.up, 0.07f, 10, Food);
            t.Cylinder(p + new Vector3(0.11f, 0.024f, 0.04f), 0.04f, 0.09f, 8, White);              // стаканчик
            if (rnd.NextDouble() < 0.6) t.Box(p + new Vector3(0.1f, 0.03f, -0.07f), new Vector3(0.12f, 0.012f, 0.03f), MetalLight);   // вилка-ложка
        }

        /// <summary>Круглый столик на одной ноге + 4 табуретки.</summary>
        private static CityToonMesh RoundTable(bool dishes)
        {
            CityToonMesh t = new CityToonMesh();
            t.Cylinder(new Vector3(0, 0.71f, 0), 0.55f, 0.05f, 18, Wood);
            t.Cylinder(new Vector3(0, 0.68f, 0), 0.5f, 0.03f, 18, WoodDark);
            t.Cylinder(new Vector3(0, 0.03f, 0), 0.05f, 0.66f, 8, Metal);
            t.Cylinder(new Vector3(0, 0f, 0), 0.3f, 0.04f, 14, MetalDark);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.85f;
                t.Cylinder(p + new Vector3(0, 0.43f, 0), 0.18f, 0.05f, 12, Wood);
                t.Cylinder(p + new Vector3(0, 0.02f, 0), 0.03f, 0.41f, 6, Metal);
                t.Cylinder(p, 0.14f, 0.02f, 10, MetalDark);
            }
            if (dishes)
            {
                System.Random rnd = new System.Random(5);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                    Dish(t, new Vector3(Mathf.Cos(a) * 0.3f, 0.76f, Mathf.Sin(a) * 0.3f), rnd);
                }
            }
            return t;
        }

        /// <summary>Раздача: металлический прилавок, полка для подносов, стекло-щиток, кастрюли, меню на стойке.</summary>
        private static CityToonMesh Counter()
        {
            CityToonMesh t = new CityToonMesh();
            float L = 3f, H = 0.9f, D = 0.7f;
            t.Box(new Vector3(0, 0.05f, 0), new Vector3(L - 0.1f, 0.1f, D - 0.1f), MetalDark);          // цоколь
            t.Box(new Vector3(0, H * 0.5f + 0.05f, 0), new Vector3(L, H - 0.1f, D), Metal);
            t.Box(new Vector3(0, H + 0.02f, 0), new Vector3(L + 0.06f, 0.04f, D + 0.06f), MetalLight);
            for (int i = 0; i < 4; i++) t.Box(new Vector3(-L * 0.5f + 0.375f + i * 0.75f, H * 0.5f, D * 0.5f + 0.005f), new Vector3(0.6f, H - 0.3f, 0.02f), MetalLight);  // дверцы
            // полка для подносов (сторона котов, +Z)
            t.Box(new Vector3(0, H - 0.08f, D * 0.5f + 0.2f), new Vector3(L, 0.03f, 0.32f), MetalLight);
            for (int i = 0; i < 4; i++)
            {
                float x = -L * 0.5f + 0.1f + i * (L - 0.2f) / 3f;
                t.Beam(new Vector3(x, H - 0.3f, D * 0.5f), new Vector3(x, H - 0.1f, D * 0.5f + 0.3f), 0.03f, Metal);
            }
            // стекло-щиток
            for (int s = -1; s <= 1; s += 2) t.Box(new Vector3(s * (L * 0.5f - 0.05f), H + 0.25f, 0.15f), new Vector3(0.04f, 0.46f, 0.04f), MetalLight);
            t.Box(new Vector3(0, H + 0.42f, 0.15f), new Vector3(L - 0.1f, 0.3f, 0.02f), GuardC);
            t.Box(new Vector3(0, H + 0.58f, 0.15f), new Vector3(L - 0.02f, 0.03f, 0.05f), MetalLight);
            // кастрюли и гастроёмкости
            Color[] pots = { new Color(0.85f, 0.85f, 0.88f), Bowl, new Color(0.85f, 0.85f, 0.88f) };
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = new Vector3(-0.9f + i * 0.9f, H + 0.04f, -0.08f);
                t.Cylinder(p, 0.2f, 0.28f, 14, pots[i]);
                t.Disc(p + Vector3.up * 0.27f, Vector3.up, 0.18f, 14, i == 1 ? new Color(0.95f, 0.65f, 0.3f) : Food);
                t.Box(p + new Vector3(0.2f, 0.2f, 0), new Vector3(0.12f, 0.03f, 0.04f), MetalDark);       // ручки
                t.Box(p + new Vector3(-0.2f, 0.2f, 0), new Vector3(0.12f, 0.03f, 0.04f), MetalDark);
            }
            t.Beam(new Vector3(0.25f, H + 0.32f, -0.08f), new Vector3(0.55f, H + 0.62f, -0.15f), 0.025f, MetalDark);   // половник
            // меню на стойке сзади
            t.Box(new Vector3(L * 0.5f + 0.35f, 0.75f, -0.1f), new Vector3(0.05f, 1.5f, 0.05f), MetalDark);
            t.Box(new Vector3(L * 0.5f + 0.35f, 1.6f, -0.1f), new Vector3(0.7f, 0.9f, 0.05f), Black);
            for (int i = 0; i < 4; i++) t.Box(new Vector3(L * 0.5f + 0.35f, 1.85f - i * 0.17f, -0.07f), new Vector3(0.5f - (i % 2) * 0.12f, 0.04f, 0.01f), i == 0 ? Food : White);
            t.Box(new Vector3(L * 0.5f + 0.35f, 0.02f, -0.1f), new Vector3(0.4f, 0.04f, 0.4f), MetalDark);
            return t;
        }

        private static CityToonMesh Bin()
        {
            CityToonMesh t = new CityToonMesh();
            t.Cylinder(Vector3.zero, 0.28f, 0.8f, 14, BinC);
            t.Cylinder(new Vector3(0, 0.78f, 0), 0.31f, 0.06f, 14, new Color(0.22f, 0.45f, 0.28f));
            t.Box(new Vector3(0, 0.88f, 0), new Vector3(0.2f, 0.04f, 0.05f), MetalDark);
            t.Box(new Vector3(0, 0.45f, 0.285f), new Vector3(0.2f, 0.2f, 0.01f), White);           // наклейка
            return t;
        }

        // ══════════════ БЫТОВКИ ══════════════
        private static Color CabinBody(CabinColor c)
        {
            switch (c)
            {
                case CabinColor.White: return new Color(0.9f, 0.9f, 0.87f);
                case CabinColor.Yellow: return new Color(0.97f, 0.78f, 0.25f);
                case CabinColor.Green: return new Color(0.4f, 0.66f, 0.42f);
                default: return new Color(0.27f, 0.52f, 0.82f);
            }
        }

        /// <summary>Бытовка-контейнер 6 × 2.45 × 2.6 м: рёбра профлиста, дверь, окна с решёткой, кондиционер, ступенька.</summary>
        private static CityToonMesh Cabin(CabinColor cc)
        {
            CityToonMesh t = new CityToonMesh();
            float L = 6f, W = 2.45f, H = 2.6f;
            Color body = CabinBody(cc), rib = body * 0.85f, frame = cc == CabinColor.White ? new Color(0.35f, 0.4f, 0.48f) : White;
            rib.a = 1f;
            t.Box(new Vector3(0, H * 0.5f, 0), new Vector3(L - 0.1f, H - 0.2f, W - 0.1f), body);
            // рама: стойки и обвязка
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    t.Box(new Vector3(sx * (L * 0.5f - 0.07f), H * 0.5f, sz * (W * 0.5f - 0.07f)), new Vector3(0.14f, H, 0.14f), frame);
            for (int k = 0; k < 2; k++)
            {
                float y = k == 0 ? 0.07f : H - 0.07f;
                for (int sz = -1; sz <= 1; sz += 2) t.Box(new Vector3(0, y, sz * (W * 0.5f - 0.07f)), new Vector3(L, 0.14f, 0.14f), frame);
                for (int sx = -1; sx <= 1; sx += 2) t.Box(new Vector3(sx * (L * 0.5f - 0.07f), y, 0), new Vector3(0.14f, 0.14f, W), frame);
            }
            t.Box(new Vector3(0, H + 0.03f, 0), new Vector3(L + 0.04f, 0.06f, W + 0.04f), frame);
            // проёмы фасада +Z: дверь и 2 окна; сзади (−Z) — одно окно
            float doorX = -1.9f;
            float[] winF = { 0.2f, 1.85f };
            float[] winB = { 0.6f };
            Ribs(t, L, W, H, 1, rib, doorX, winF);
            Ribs(t, L, W, H, -1, rib, 99f, winB);
            for (int sx = -1; sx <= 1; sx += 2)                                                    // торцы
                for (int i = 0; i < 6; i++)
                    t.Box(new Vector3(sx * (L * 0.5f - 0.02f), H * 0.5f, -W * 0.5f + 0.4f + i * 0.33f), new Vector3(0.06f, H - 0.32f, 0.08f), rib);
            // дверь
            float zf = W * 0.5f;
            t.Box(new Vector3(doorX, 1.1f, zf - 0.02f), new Vector3(0.95f, 2.05f, 0.08f), frame);
            t.Box(new Vector3(doorX, 1.1f, zf + 0.01f), new Vector3(0.82f, 1.92f, 0.06f), body * 0.75f + new Color(0, 0, 0, 0.25f));
            t.Box(new Vector3(doorX + 0.3f, 1.05f, zf + 0.06f), new Vector3(0.14f, 0.04f, 0.05f), MetalDark);
            t.Box(new Vector3(doorX, 1.65f, zf + 0.045f), new Vector3(0.4f, 0.3f, 0.02f), GlassC);
            // крылечко
            t.Box(new Vector3(doorX, 0.11f, zf + 0.32f), new Vector3(1.1f, 0.22f, 0.55f), Metal);
            t.Box(new Vector3(doorX, 0.225f, zf + 0.32f), new Vector3(1.12f, 0.01f, 0.57f), MetalDark);
            // окна
            foreach (float wx in winF) Window(t, wx, zf, 1);
            foreach (float wx in winB) Window(t, wx, -zf, -1);
            // кондиционер и козырёк над дверью
            t.Box(new Vector3(1.0f, 2.2f, zf + 0.14f), new Vector3(0.75f, 0.42f, 0.24f), White);
            t.Disc(new Vector3(1.0f, 2.2f, zf + 0.265f), Vector3.forward, 0.15f, 12, MetalDark);
            t.Beam(new Vector3(0.62f, 2.05f, zf + 0.1f), new Vector3(0.62f, 0.3f, zf + 0.06f), 0.03f, White);   // трубка дренажа
            t.Box(new Vector3(doorX, 2.25f, zf + 0.25f), new Vector3(1.2f, 0.05f, 0.5f), frame);
            // табличка над дверью
            t.Box(new Vector3(doorX + 0.95f, 2.0f, zf + 0.02f), new Vector3(0.5f, 0.22f, 0.02f), White);
            return t;
        }

        private static void Ribs(CityToonMesh t, float L, float W, float H, int side, Color rib, float doorX, float[] wins)
        {
            float z = side * (W * 0.5f - 0.02f);
            for (float x = -L * 0.5f + 0.35f; x < L * 0.5f - 0.3f; x += 0.32f)
            {
                if (Mathf.Abs(x - doorX) < 0.62f) continue;
                bool underWin = false;
                foreach (float w in wins) if (Mathf.Abs(x - w) < 0.68f) underWin = true;
                if (underWin)
                {
                    t.Box(new Vector3(x, 0.6f, z), new Vector3(0.08f, 0.9f, 0.06f), rib);           // под окном
                    t.Box(new Vector3(x, H - 0.33f, z), new Vector3(0.08f, 0.38f, 0.06f), rib);     // над окном
                    continue;
                }
                t.Box(new Vector3(x, H * 0.5f, z), new Vector3(0.08f, H - 0.32f, 0.06f), rib);
            }
        }

        private static void Window(CityToonMesh t, float x, float z, int side)
        {
            t.Box(new Vector3(x, 1.5f, z - side * 0.02f), new Vector3(1.15f, 0.92f, 0.08f), White);
            t.Box(new Vector3(x, 1.5f, z + side * 0.01f), new Vector3(1.0f, 0.76f, 0.06f), GlassC);
            t.Box(new Vector3(x, 1.5f, z + side * 0.045f), new Vector3(0.03f, 0.76f, 0.02f), White);    // переплёт
            for (int i = -2; i <= 2; i++) t.Box(new Vector3(x + i * 0.2f, 1.5f, z + side * 0.075f), new Vector3(0.025f, 0.86f, 0.025f), MetalDark);   // решётка
            t.Box(new Vector3(x, 1.02f, z + side * 0.06f), new Vector3(1.2f, 0.04f, 0.12f), White);    // отлив
        }

        /// <summary>Биотуалет: синяя будка, белая крыша, дверь с вентиляцией и индикатором «свободно», труба.</summary>
        private static CityToonMesh Toilet()
        {
            CityToonMesh t = new CityToonMesh();
            float W = 1.15f, D = 1.2f;
            Color blue = new Color(0.22f, 0.45f, 0.82f), blueD = new Color(0.16f, 0.34f, 0.66f);
            t.Box(new Vector3(0, 0.05f, 0), new Vector3(W + 0.06f, 0.1f, D + 0.06f), White);
            t.Box(new Vector3(0, 1.1f, 0), new Vector3(W, 2.0f, D), blue);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int i = -1; i <= 1; i++) t.Box(new Vector3(sx * (W * 0.5f + 0.015f), 1.1f, i * 0.36f), new Vector3(0.04f, 1.9f, 0.08f), blueD);
            for (int i = -1; i <= 1; i++) t.Box(new Vector3(i * 0.34f, 1.1f, -D * 0.5f - 0.015f), new Vector3(0.08f, 1.9f, 0.04f), blueD);
            for (int sx = -1; sx <= 1; sx += 2)                                                   // уголки
                for (int sz = -1; sz <= 1; sz += 2) t.Box(new Vector3(sx * (W * 0.5f - 0.02f), 1.1f, sz * (D * 0.5f - 0.02f)), new Vector3(0.07f, 2.0f, 0.07f), White);
            t.Box(new Vector3(0, 2.15f, 0), new Vector3(W + 0.1f, 0.1f, D + 0.1f), White);
            t.Box(new Vector3(0, 2.25f, 0), new Vector3(W - 0.15f, 0.1f, D - 0.15f), White);
            float zf = D * 0.5f;
            t.Box(new Vector3(0, 1.05f, zf + 0.02f), new Vector3(W - 0.2f, 1.85f, 0.04f), blueD);
            for (int i = 0; i < 4; i++) t.Box(new Vector3(0, 1.62f + i * 0.07f, zf + 0.045f), new Vector3(0.45f, 0.025f, 0.015f), new Color(0.1f, 0.2f, 0.4f));
            t.Box(new Vector3(0.33f, 0.98f, zf + 0.06f), new Vector3(0.05f, 0.18f, 0.05f), MetalLight);
            t.Box(new Vector3(0.33f, 1.14f, zf + 0.05f), new Vector3(0.1f, 0.04f, 0.02f), GreenL);
            t.Disc(new Vector3(0f, 1.42f, zf + 0.045f), Vector3.forward, 0.09f, 12, White);        // кружок-значок
            t.Cylinder(new Vector3(W * 0.5f - 0.22f, 2.1f, -D * 0.5f + 0.22f), 0.06f, 0.48f, 8, MetalDark);
            t.Cylinder(new Vector3(W * 0.5f - 0.22f, 2.58f, -D * 0.5f + 0.22f), 0.1f, 0.05f, 8, MetalDark);
            return t;
        }

        /// <summary>Душ: поддон с решёткой, стенки, шторка, бак на крыше, лейка.</summary>
        private static CityToonMesh Shower()
        {
            CityToonMesh t = new CityToonMesh();
            float W = 1.05f, D = 1.05f, H = 2.15f;
            t.Box(new Vector3(0, 0.06f, 0), new Vector3(W, 0.12f, D), White);
            for (int i = 0; i < 6; i++) t.Box(new Vector3(-W * 0.5f + 0.13f + i * 0.155f, 0.135f, 0), new Vector3(0.1f, 0.03f, D - 0.12f), Wood);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2) t.Box(new Vector3(sx * (W * 0.5f - 0.03f), H * 0.5f, sz * (D * 0.5f - 0.03f)), new Vector3(0.06f, H, 0.06f), Metal);
            Color panel = new Color(0.82f, 0.9f, 0.88f);
            t.Box(new Vector3(0, 1.15f, -D * 0.5f + 0.02f), new Vector3(W - 0.08f, 1.75f, 0.03f), panel);
            for (int sx = -1; sx <= 1; sx += 2) t.Box(new Vector3(sx * (W * 0.5f - 0.02f), 1.15f, 0), new Vector3(0.03f, 1.75f, D - 0.08f), panel);
            // шторка гармошкой (сторона +Z), открыта на треть
            t.Box(new Vector3(0, H - 0.08f, D * 0.5f - 0.03f), new Vector3(W, 0.03f, 0.03f), MetalLight);
            for (int i = 0; i < 7; i++)
            {
                float x = -W * 0.5f + 0.08f + i * 0.095f;
                float z = D * 0.5f - 0.04f + (i % 2 == 0 ? 0.03f : -0.01f);
                t.Box(new Vector3(x, 1.2f, z), new Vector3(0.1f, 1.6f, 0.015f), i % 2 == 0 ? Teal : White);
            }
            // крыша-рама и бак
            for (int sz = -1; sz <= 1; sz += 2) t.Box(new Vector3(0, H, sz * (D * 0.5f - 0.03f)), new Vector3(W, 0.06f, 0.06f), Metal);
            for (int sx = -1; sx <= 1; sx += 2) t.Box(new Vector3(sx * (W * 0.5f - 0.03f), H, 0), new Vector3(0.06f, 0.06f, D), Metal);
            t.Cylinder(new Vector3(0, H + 0.03f, 0), 0.42f, 0.55f, 16, Black);
            t.Cylinder(new Vector3(0, H + 0.58f, 0), 0.12f, 0.06f, 8, MetalDark);
            // труба и лейка
            t.Beam(new Vector3(0, H, -0.3f), new Vector3(0, 1.95f, -0.3f), 0.035f, MetalLight);
            t.Beam(new Vector3(0, 1.95f, -0.3f), new Vector3(0, 1.95f, -0.12f), 0.035f, MetalLight);
            t.Cylinder(new Vector3(0, 1.9f, -0.12f), 0.1f, 0.05f, 12, MetalLight);
            t.Box(new Vector3(0.38f, 1.3f, -0.15f), new Vector3(0.1f, 0.03f, 0.03f), MetalLight);      // вентиль
            t.Box(new Vector3(-W * 0.5f - 0.03f, 1.6f, 0.1f), new Vector3(0.03f, 0.5f, 0.3f), new Color(0.98f, 0.55f, 0.5f));   // полотенце снаружи
            return t;
        }

        /// <summary>Умывальник на 2 крана: столешница, раковины, бак сверху, зеркала, полотенце.</summary>
        private static CityToonMesh Washstand()
        {
            CityToonMesh t = new CityToonMesh();
            float L = 1.6f, D = 0.5f, H = 0.85f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2) t.Box(new Vector3(sx * (L * 0.5f - 0.05f), H * 0.5f, sz * (D * 0.5f - 0.05f)), new Vector3(0.05f, H, 0.05f), Metal);
            t.Box(new Vector3(0, H, 0), new Vector3(L, 0.05f, D), White);
            t.Box(new Vector3(0, 0.2f, 0), new Vector3(L - 0.1f, 0.03f, D - 0.1f), Metal);
            t.Box(new Vector3(0, 1.4f, -D * 0.5f + 0.02f), new Vector3(L, 1.1f, 0.04f), White);
            for (int i = -1; i <= 1; i += 2)
            {
                float x = i * 0.4f;
                t.Cylinder(new Vector3(x, H + 0.02f, 0.03f), 0.18f, 0.02f, 14, MetalLight);
                t.Disc(new Vector3(x, H + 0.045f, 0.03f), Vector3.up, 0.13f, 12, new Color(0.65f, 0.7f, 0.75f));
                t.Beam(new Vector3(x, 1.75f, -D * 0.5f + 0.06f), new Vector3(x, 1.75f, -0.05f), 0.04f, MetalLight);   // кран
                t.Beam(new Vector3(x, 1.75f, -0.05f), new Vector3(x, 1.65f, -0.05f), 0.04f, MetalLight);
                t.Box(new Vector3(x, 1.3f, -D * 0.5f + 0.045f), new Vector3(0.45f, 0.5f, 0.01f), GuardC);           // зеркало
                t.Box(new Vector3(x, 1.3f, -D * 0.5f + 0.042f), new Vector3(0.5f, 0.55f, 0.008f), WoodDark);
            }
            t.Box(new Vector3(0, 2.1f, -D * 0.5f + 0.17f), new Vector3(L - 0.1f, 0.4f, 0.3f), new Color(0.27f, 0.52f, 0.82f));   // бак
            t.Cylinder(new Vector3(0.55f, 2.3f, -D * 0.5f + 0.17f), 0.06f, 0.05f, 8, White);
            t.Box(new Vector3(L * 0.5f + 0.02f, 0.75f, 0), new Vector3(0.03f, 0.04f, 0.3f), MetalLight);           // вешалка
            t.Box(new Vector3(L * 0.5f + 0.04f, 0.55f, 0), new Vector3(0.02f, 0.4f, 0.26f), new Color(0.98f, 0.82f, 0.35f));    // полотенце
            return t;
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
