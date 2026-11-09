using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// ============================================================================
//  CatCooks → Skyline → Генератор дальнего города
//
//  Плотная разнообразная застройка самого дальнего ряда (средний и ближний
//  делает художник). Правила стиля:
//   • высоты «волной»: кластеры высоток и низины, плюс несколько героев;
//   • 6 типов домов: башня с уступами, пластина, ар-деко с короной и шпилем,
//     восьмигранная стеклянная, двойная, низкий кирпичный с бочкой;
//   • крыши: водонапорные бочки, кондиционеры, антенны, щиты (только в
//     ближнем поясе фона — вдали не видно);
//   • палитра «Комикс»: крем, песок, охра, терракота, кирпич, серо-бирюза;
//   • окна процедурные в шейдере, вдали сливаются, ночью горят;
//   • дымка по дальности — в шейдере, цвет из погоды.
//  Всё склеено в меши по кускам ~260 м: несколько десятков вызовов
//  отрисовки на весь фон, без теней, со своей глубиной.
// ============================================================================
public class CatCooksSkylineGenerator : EditorWindow
{
    const string RootName = "CatCooks Skyline";
    const string GenDir = "Assets/CatCooks/Skyline/Generated";

    Transform _center;
    int _seed = 12;
    float _inner = 200f, _mid = 700f, _outer = 1000f;
    float _centerAsphalt = 100f;
    float _block = 64f, _street = 16f;
    float _minH = 16f, _maxH = 260f, _cluster = 320f;
    float _midMin = 45f, _midMax = 115f;
    [Range(2, 12)] int _towerEvery = 6;          // 1 небоскрёб на N средних
    float _infill = 0.55f;                       // доля улиц, «заткнутых» домами (нет сквозных пустот)
    float _jitter = 0.55f;
    [Range(0, 12)] int _heroes = 5;
    float _props = 0.6f, _chunk = 260f;
    bool _blockBases = true;
    Vector2 _scroll;

    // ── средний слой: готовые дома (художника и CC_Mid_*) + улицы с декорациями
    bool _midOn = true;
    float _midOuter = 550f;
    float _midBlockX = 170f, _midBlockZ = 110f, _midStreet = 14f;
    float _midCornerChance = 1f;
    float _tallShare = 0.15f;           // доля высоток среди домов среднего слоя
    float _tallFrom = 0.5f;             // высотки только во внешней части слоя (доля ширины слоя)
    bool _roofProps = true, _fireEscapes = true, _cars = true;
    static readonly string[] FlatRoof = { "brownstone", "NYC_tenenment", "castiron", "BrickWarehouse", "CastIronLoft", "UCourtyard", "hotel" };
    static readonly string[] BrickNames = { "brownstone", "NYC_tenenment", "BrickWarehouse", "UCourtyard" };
    int _accentMax = 6; float _accentSpacing = 220f;
    Material _atlas;
    List<GameObject> _midPrefabs = new List<GameObject>();
    static readonly string[] AccentNames = { "Chrysler", "Cathedral", "ClockTower", "Flatiron", "Ansonia", "TwinTowers", "ArtDeco" };

    static readonly (string hex, float w)[] Facades =
    {
        ("#EAD9B5", 0.20f), ("#D9B98A", 0.18f), ("#E8A832", 0.12f), ("#C8643C", 0.14f),
        ("#A8563C", 0.12f), ("#6E9A9A", 0.10f), ("#7C8CA0", 0.08f), ("#F0B48C", 0.06f),
    };
    static readonly string[] HeroColors = { "#4E8FA8", "#B8734A", "#D9C08A", "#6E9A9A" };

    [MenuItem("CatCooks/Город/Генератор города (средний + дальний)")]
    static void Open() => GetWindow<CatCooksSkylineGenerator>("Skyline");

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.HelpBox("Самый дальний ряд города. Внутри Inner Radius — зона художника (не трогается).", MessageType.Info);
        _center = (Transform)EditorGUILayout.ObjectField("Центр города", _center, typeof(Transform), true);
        if (_center == null)
        {
            var fl = Object.FindFirstObjectByType<CatCooksCityFogLayers>();
            if (fl != null) _center = fl.transform;
        }
        _seed = EditorGUILayout.IntField("Seed", _seed);
        _centerAsphalt = EditorGUILayout.Slider("Центр: просто асфальт до, м", _centerAsphalt, 0f, 400f);
        _inner = EditorGUILayout.Slider("Средний слой: от (до него — ближние слои художника)", _inner, _centerAsphalt, 1500f);
        _outer = EditorGUILayout.Slider("Дальний слой: до (край города)", _outer, _inner + 100f, 4000f);
        _mid = _inner;   // детальный пояс процедурных домов больше не нужен: дальний слой — минимальная детализация
        _block = EditorGUILayout.Slider("Квартал, м", _block, 30f, 140f);
        _street = EditorGUILayout.Slider("Улица, м", _street, 6f, 40f);
        _minH = EditorGUILayout.Slider("Мин. высота (редкие низкие)", _minH, 6f, 60f);
        _midMin = EditorGUILayout.Slider("Средние: от", _midMin, 20f, 150f);
        _midMax = EditorGUILayout.Slider("Средние: до", _midMax, _midMin + 5f, 220f);
        _maxH = EditorGUILayout.Slider("Небоскрёбы: до", _maxH, _midMax + 20f, 500f);
        _towerEvery = EditorGUILayout.IntSlider("1 небоскрёб на N средних", _towerEvery, 2, 12);
        _infill = EditorGUILayout.Slider("Заполнение улиц (нет сквозных проходов)", _infill, 0f, 1f);
        _jitter = EditorGUILayout.Slider("Сдвиг кварталов (ломает прямые улицы)", _jitter, 0f, 1f);
        _cluster = EditorGUILayout.Slider("Размер кластеров высоток", _cluster, 80f, 1200f);
        _heroes = EditorGUILayout.IntSlider("Герои (уникальные башни)", _heroes, 0, 12);
        _props = EditorGUILayout.Slider("Детали на крышах", _props, 0f, 1f);
        _chunk = EditorGUILayout.Slider("Кусок склейки, м", _chunk, 120f, 600f);
        _blockBases = EditorGUILayout.Toggle("Тротуары кварталов", _blockBases);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Средний слой: готовые дома + улицы", EditorStyles.boldLabel);
        _midOn = EditorGUILayout.Toggle("Строить средний слой", _midOn);
        using (new EditorGUI.DisabledScope(!_midOn))
        {
            _midOuter = EditorGUILayout.Slider("Средний слой: до (дальше — дальний слой)", _midOuter, _inner + 60f, _outer - 50f);
            _midCornerChance = EditorGUILayout.Slider("Угловые дома", _midCornerChance, 0f, 1f);
            _tallShare = EditorGUILayout.Slider("Доля высоток", _tallShare, 0f, 0.5f);
            _tallFrom = EditorGUILayout.Slider("Высотки дальше чем (доля слоя)", _tallFrom, 0f, 0.95f);
            _roofProps = EditorGUILayout.Toggle("Крыши: короба, бочки, выходы", _roofProps);
            _fireEscapes = EditorGUILayout.Toggle("Пожарные лестницы", _fireEscapes);
            _cars = EditorGUILayout.Toggle("Машины на улицах", _cars);
            _midBlockX = EditorGUILayout.Slider("Квартал: длина", _midBlockX, 60f, 260f);
            _midBlockZ = EditorGUILayout.Slider("Квартал: глубина", _midBlockZ, 40f, 120f);
            _midStreet = EditorGUILayout.Slider("Улица (узко = плотно)", _midStreet, 8f, 24f);
            _accentMax = EditorGUILayout.IntSlider("Акцентов максимум", _accentMax, 0, 20);
            _accentSpacing = EditorGUILayout.Slider("Между акцентами, м", _accentSpacing, 60f, 600f);
            _atlas = (Material)EditorGUILayout.ObjectField("Материал домов (пусто — создастся сам)", _atlas, typeof(Material), false);
            if (GUILayout.Button("Взять все дома из CatCooks/City/Buildings")) FindMidPrefabs();
            for (int i = 0; i < _midPrefabs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _midPrefabs[i] = (GameObject)EditorGUILayout.ObjectField(_midPrefabs[i], typeof(GameObject), false);
                if (GUILayout.Button("−", GUILayout.Width(22))) { _midPrefabs.RemoveAt(i); i--; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ добавить слот")) _midPrefabs.Add(null);
        }
        EditorGUILayout.Space(6);
        if (GUILayout.Button("Сгенерировать", GUILayout.Height(30))) Generate();
        if (GUILayout.Button("Удалить сгенерированный город")) Clear();
        EditorGUILayout.HelpBox("Старый дальний город (CCFC_CityV2 и т.п.) выключи для сравнения. Новый — объект «" + RootName + "».", MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    // ───────────────────────────────── меш
    class MB
    {
        public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Color> c = new List<Color>(); public readonly List<Vector2> uv = new List<Vector2>(), uv2 = new List<Vector2>();
        public readonly List<int> t = new List<int>();

        public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 nrm, Color col, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, float style, float seed)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            for (int k = 0; k < 4; k++) { n.Add(nrm); c.Add(col); uv2.Add(new Vector2(style, seed)); }
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }

        /// <summary>Коробка: дно в p (центр), размеры w×d×h, поворот rot, yBase — высота нижнего края от земли дома (для этажей).</summary>
        public void Box(Vector3 p, float w, float d, float h, float rot, Color col, Color roof, float style, float seed, float yBase)
        {
            var q = Quaternion.Euler(0, rot, 0);
            Vector3 X = q * Vector3.right * (w * 0.5f), Z = q * Vector3.forward * (d * 0.5f), Y = Vector3.up * h;
            Vector3[] c0 = { p - X - Z, p + X - Z, p + X + Z, p - X + Z };
            float[] len = { w, d, w, d };
            for (int s = 0; s < 4; s++)
            {
                Vector3 a = c0[s], b = c0[(s + 1) % 4];
                Vector3 nrm = Vector3.Cross(Vector3.up, b - a).normalized;
                Quad(a, b, b + Y, a + Y, nrm, col, new Vector2(0, yBase), new Vector2(len[s], yBase), new Vector2(len[s], yBase + h), new Vector2(0, yBase + h), style, seed);
            }
            Quad(c0[0] + Y, c0[1] + Y, c0[2] + Y, c0[3] + Y, Vector3.up, roof, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0, seed);
        }

        public void Prism(Vector3 p, float r, float h, int sides, Color col, Color roof, float style, float seed, float yBase, float topScale = 1f)
        {
            float per = 2f * Mathf.PI * r / sides, u = 0;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 nrm = ((d0 + d1) * 0.5f).normalized;
                Quad(p + d0 * r, p + d1 * r, p + d1 * r * topScale + Vector3.up * h, p + d0 * r * topScale + Vector3.up * h, nrm, col,
                     new Vector2(u, yBase), new Vector2(u + per, yBase), new Vector2(u + per, yBase + h), new Vector2(u, yBase + h), style, seed);
                u += per;
            }
            // крышка
            Vector3 top = p + Vector3.up * h;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                int i = v.Count;
                v.Add(top); v.Add(top + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r * topScale); v.Add(top + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r * topScale);
                for (int k = 0; k < 3; k++) { n.Add(Vector3.up); c.Add(roof); uv.Add(Vector2.zero); uv2.Add(new Vector2(0, seed)); }
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
        }

        public void Pyramid(Vector3 p, float w, float d, float h, float rot, Color col)
        {
            var q = Quaternion.Euler(0, rot, 0);
            Vector3 X = q * Vector3.right * (w * 0.5f), Z = q * Vector3.forward * (d * 0.5f), apex = p + Vector3.up * h;
            Vector3[] c0 = { p - X - Z, p + X - Z, p + X + Z, p - X + Z };
            for (int s = 0; s < 4; s++)
            {
                Vector3 a = c0[s], b = c0[(s + 1) % 4];
                Vector3 nrm = Vector3.Cross(b - a, apex - a).normalized;
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(apex);
                for (int k = 0; k < 3; k++) { n.Add(-nrm); c.Add(col); uv.Add(Vector2.zero); uv2.Add(Vector2.zero); }
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetUVs(1, uv2); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    // ───────────────────────────────── генерация
    System.Random _r;
    float R() => (float)_r.NextDouble();
    float R(float a, float b) => a + (b - a) * R();
    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out Color c); return c; }

    Color PickFacade()
    {
        float x = R(), acc = 0;
        foreach (var f in Facades) { acc += f.w; if (x <= acc) return Jitter(Hex(f.hex)); }
        return Jitter(Hex(Facades[0].hex));
    }
    Color Jitter(Color c) { float k = R(0.94f, 1.05f); return new Color(c.r * k, c.g * k, c.b * k, 1); }
    static Color RoofOf(Color c) => new Color(c.r * 0.78f, c.g * 0.74f, c.b * 0.72f, 1);

    float Noise(Vector2 p) => Mathf.PerlinNoise(p.x + _seed * 13.1f, p.y - _seed * 7.7f);

    /// <summary>Средняя этажность высокая, каждый N-й — небоскрёб (чаще в кластерах), редкие низкие.</summary>
    float HeightAt(Vector2 p, out bool tower)
    {
        float n = Noise(p / _cluster) * 0.65f + Noise(p / (_cluster * 0.37f) + Vector2.one * 31f) * 0.35f;  // 0..1, кластеры
        float towerChance = (1f / (_towerEvery + 1f)) * Mathf.Lerp(0.4f, 2.2f, n);
        tower = R() < towerChance;
        if (tower) return R(Mathf.Lerp(_midMax * 1.2f, _maxH * 0.7f, n), _maxH);
        if (R() < 0.1f) return R(_minH, _midMin);
        return Mathf.Lerp(_midMin, _midMax, Mathf.Clamp01(n * 0.7f + R() * 0.5f));
    }

    void Clear()
    {
        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
    }

    void Generate()
    {
        var sh = Shader.Find("CatCooks/Skyline");
        if (sh == null) { EditorUtility.DisplayDialog("Skyline", "Нет шейдера CatCooks/Skyline — проверь консоль.", "Ок"); return; }
        Clear();
        _r = new System.Random(_seed);
        Vector3 c0 = _center != null ? _center.position : Vector3.zero;

        Directory.CreateDirectory(GenDir + "/Meshes");
        AssetDatabase.Refresh();
        string matPath = GenDir + "/M_Skyline.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, matPath); }
        foreach (var old in Directory.GetFiles(GenDir + "/Meshes", "*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));

        var chunks = new Dictionary<Vector2Int, MB>();
        MB Chunk(Vector3 p) { var k = new Vector2Int(Mathf.FloorToInt((p.x - c0.x) / _chunk), Mathf.FloorToInt((p.z - c0.z) / _chunk));
                              if (!chunks.TryGetValue(k, out var mb)) chunks[k] = mb = new MB(); return mb; }

        // герои: заранее выбранные точки в ближней части пояса
        var heroes = new List<Vector2>();
        for (int i = 0; i < _heroes; i++)
        {
            float a = R() * Mathf.PI * 2f, d = R(_inner * 1.15f, Mathf.Lerp(_inner, _mid, 0.8f));
            heroes.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d);
        }

        float step = _block + _street;
        int cells = Mathf.CeilToInt(_outer / step);
        int built = 0;
        Color sidewalk = Hex("#C9B79A");
        for (int gx = -cells; gx <= cells; gx++)
            for (int gz = -cells; gz <= cells; gz++)
            {
                // ряды сдвинуты через один, кварталы смещены шумом — прямых сквозных улиц нет
                Vector2 bc = new Vector2(gx * step + ((gz & 1) == 0 ? 0f : step * 0.5f * _jitter), gz * step);
                bc += new Vector2(R(-1f, 1f), R(-1f, 1f)) * (_street * 0.6f * _jitter);
                float dist = bc.magnitude;
                float keep = _midOn ? _midOuter : _inner;
                if (dist < keep || dist > _outer) continue;                                  // дальний слой — кольцо
                bool detail = false;                                                          // минимальная детализация
                Vector3 bp = c0 + new Vector3(bc.x, 0, bc.y);
                var mb = Chunk(bp);
                if (_blockBases && detail) mb.Box(bp, _block, _block, 0.3f, 0, sidewalk, sidewalk, 0, 0, 0);

                // квартал → 1..4 участка
                int split = detail ? (R() < 0.5f ? 2 : (R() < 0.6f ? 4 : 1)) : (R() < 0.6f ? 1 : 2);
                var lots = new List<(Vector2 c, float w, float d)>();
                float pad = 3f, inner = _block - pad * 2f;
                if (split == 1) lots.Add((bc, inner, inner));
                else if (split == 2) { bool xs = R() < 0.5f; float a = inner * R(0.4f, 0.6f), b = inner - a - 2f;
                    if (xs) { lots.Add((bc + new Vector2(-inner * 0.5f + a * 0.5f, 0), a, inner)); lots.Add((bc + new Vector2(inner * 0.5f - b * 0.5f, 0), b, inner)); }
                    else { lots.Add((bc + new Vector2(0, -inner * 0.5f + a * 0.5f), inner, a)); lots.Add((bc + new Vector2(0, inner * 0.5f - b * 0.5f), inner, b)); } }
                else { float hw = inner * 0.5f - 1f; for (int k = 0; k < 4; k++) lots.Add((bc + new Vector2((k & 1) == 0 ? -hw * 0.5f - 0.5f : hw * 0.5f + 0.5f, (k & 2) == 0 ? -hw * 0.5f - 0.5f : hw * 0.5f + 0.5f), hw, hw)); }

                // «пробки» в улицах: дом прямо на проезде справа и сверху от квартала
                if (R() < _infill) lots.Add((bc + new Vector2(_block * 0.5f + _street * 0.5f, R(-1f, 1f) * _block * 0.3f), _street * 0.9f + R(2f, 8f), R(14f, 30f)));
                if (R() < _infill) lots.Add((bc + new Vector2(R(-1f, 1f) * _block * 0.3f, _block * 0.5f + _street * 0.5f), R(14f, 30f), _street * 0.9f + R(2f, 8f)));
                if (!detail && R() < _infill) lots.Add((bc + new Vector2(_block * 0.5f + _street * 0.5f, _block * 0.5f + _street * 0.5f), _street + 6f, _street + 6f));

                foreach (var lot in lots)
                {
                    float h = HeightAt(lot.c, out bool tower);
                    bool hero = false;
                    foreach (var hp in heroes) if ((hp - lot.c).sqrMagnitude < step * step * 0.3f) { hero = true; h = Mathf.Max(h, _maxH * R(1.25f, 1.6f)); }
                    Vector3 p = c0 + new Vector3(lot.c.x, 0, lot.c.y);
                    Building(Chunk(p), p, lot.w, lot.d, h, hero, detail, tower);
                    built++;
                }
            }

        // в сцену
        var root = new GameObject(RootName);
        if (_midOn && _midPrefabs.Count == 0) FindMidPrefabs();
        Undo.RegisterCreatedObjectUndo(root, "Skyline");
        root.transform.position = Vector3.zero;
        root.AddComponent<CatCooksSkylineGlobals>().groundY = c0.y;
        // земля: асфальт по всей карте (одна плоскость), город стоит на ней
        {
            var gmb = new MB(); float R0 = _outer * 1.15f; Color asph = Hex("#55524E");
            gmb.Quad(c0 + new Vector3(-R0, -0.03f, -R0), c0 + new Vector3(R0, -0.03f, -R0), c0 + new Vector3(R0, -0.03f, R0), c0 + new Vector3(-R0, -0.03f, R0),
                     Vector3.up, asph, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0, 0);
            // перевернём порядок (Quad рассчитан на стены): нормаль вверх
            gmb.t.Clear(); gmb.t.AddRange(new[] { 0, 2, 1, 0, 3, 2 });
            var gm = gmb.ToMesh("City_Ground"); AssetDatabase.CreateAsset(gm, GenDir + "/Meshes/City_Ground.asset");
            var gg = new GameObject("City Ground"); gg.transform.SetParent(root.transform, false);
            gg.AddComponent<MeshFilter>().sharedMesh = gm; var gr = gg.AddComponent<MeshRenderer>(); gr.sharedMaterial = mat;
            gr.shadowCastingMode = ShadowCastingMode.Off;
        }
        if (_midOn) GenerateMid(root.transform, c0, mat, CityMaterial());
        int tris = 0;
        foreach (var kv in chunks)
        {
            if (kv.Value.v.Count == 0) continue;
            var mesh = kv.Value.ToMesh("Skyline_" + kv.Key.x + "_" + kv.Key.y);
            tris += kv.Value.t.Count / 3;
            AssetDatabase.CreateAsset(mesh, GenDir + "/Meshes/" + mesh.name + ".asset");
            var go = new GameObject(mesh.name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        Debug.Log($"[Skyline] домов: {built}, кусков: {chunks.Count}, треугольников: {tris:N0}.", root);
    }

    static readonly Color Metal = new Color(0.35f, 0.29f, 0.27f), Tank = new Color(0.54f, 0.42f, 0.31f), Ac = new Color(0.74f, 0.70f, 0.65f);

    // ═══════════════════════════════ СРЕДНИЙ СЛОЙ
    const string BuildingsDir = "Assets/CatCooks/City/Buildings";

    void FindMidPrefabs()
    {
        _midPrefabs = new List<GameObject>();
        if (!AssetDatabase.IsValidFolder(BuildingsDir)) { Debug.LogWarning("[Город] нет папки " + BuildingsDir); return; }
        foreach (var g in AssetDatabase.FindAssets("t:GameObject", new[] { BuildingsDir }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            if (go != null && go.GetComponentsInChildren<Renderer>().Length > 0) _midPrefabs.Add(go);
        }
        Debug.Log("[Город] домов для среднего слоя: " + _midPrefabs.Count);
    }

    /// <summary>Общий материал домов: шейдер CatCooks/City Building + атлас из папки домов.</summary>
    Material CityMaterial()
    {
        if (_atlas != null) return _atlas;
        string path = GenDir + "/M_CityBuildings.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("CatCooks/City Building");
            if (sh == null) { Debug.LogWarning("[Город] нет шейдера CatCooks/City Building"); return null; }
            m = new Material(sh);
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BuildingsDir + "/CityAtlas_Albedo.png");
            string np = BuildingsDir + "/CityAtlas_Normal.png";
            if (AssetImporter.GetAtPath(np) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
            var nm = AssetDatabase.LoadAssetAtPath<Texture2D>(np);
            if (nm != null) m.SetTexture("_BumpMap", nm);
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    struct Fit { public GameObject prefab; public Vector3 size; public bool accent, corner, tall, flat, brick; }

    List<Fit> Measure()
    {
        var pool = new List<Fit>();
        foreach (var pf in _midPrefabs)
        {
            if (pf == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            var rs = inst.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                bool acc = false; foreach (var a in AccentNames) if (pf.name.Contains(a)) acc = true;
                bool corner = pf.name.Contains("RoundCorner") || pf.name.Contains("Flatiron");
                bool flat = false; foreach (var n in FlatRoof) if (pf.name.Contains(n)) flat = true;
                bool brick = false; foreach (var n in BrickNames) if (pf.name.Contains(n)) brick = true;
                pool.Add(new Fit { prefab = pf, size = b.size, accent = acc, corner = corner, tall = b.size.y > 110f, flat = flat, brick = brick });
            }
            DestroyImmediate(inst);
        }
        return pool;
    }

    void GenerateMid(Transform root, Vector3 c0, Material skyMat, Material cityMat)
    {
        var pool = Measure();
        if (pool.Count == 0) { Debug.LogWarning("[Город] средний слой: нет домов (папка " + BuildingsDir + ")."); return; }
        var corners = pool.FindAll(f => f.corner);
        var midRoot = new GameObject("Mid City"); midRoot.transform.SetParent(root, false);
        var streets = new Dictionary<Vector2Int, MB>();
        MB SChunk(Vector3 p) { var k = new Vector2Int(Mathf.FloorToInt((p.x - c0.x) / _chunk), Mathf.FloorToInt((p.z - c0.z) / _chunk));
                               if (!streets.TryGetValue(k, out var mb)) streets[k] = mb = new MB(); return mb; }
        var accents = new List<Vector3>();
        GameObject last = null;
        int placed = 0, tallCount = 0;
        MB curMB = null;        // кусок улиц для крыш/лестниц текущего квартала
        float sx = _midBlockX + _midStreet, sz = _midBlockZ + _midStreet;
        int nx = Mathf.CeilToInt(_midOuter / sx) + 1, nz = Mathf.CeilToInt(_midOuter / sz) + 1;
        const float walkW = 3.5f;

        // поставить дом: позиция центра участка, «вперёд» = направление на улицу; масштаб — подогнать под участок
        void Place(Fit f, Vector3 lotCenter, Vector3 front, float maxAlong, float maxDeep, bool alongIsX)
        {
            bool longX = f.size.x >= f.size.z;
            float along = longX ? f.size.x : f.size.z, deep = longX ? f.size.z : f.size.x;
            float sc = Mathf.Min(1.15f, Mathf.Min(maxAlong / along, maxDeep / deep));
            sc = Mathf.Max(sc, 0.5f);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(f.prefab);
            Undo.RegisterCreatedObjectUndo(inst, "Mid building");
            inst.transform.SetParent(midRoot.transform, true);
            // длинной стороной вдоль улицы, фасадом (случайно +/−) на улицу
            float yawFront = Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg;
            float yaw = yawFront + (longX ? 0f : 90f) + (R() < 0.5f ? 0f : 180f);
            inst.transform.rotation = Quaternion.Euler(0, yaw, 0) * f.prefab.transform.rotation;
            inst.transform.localScale = f.prefab.transform.localScale * sc;
            inst.transform.position = lotCenter;
            var rs = inst.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var rr in rs) b.Encapsulate(rr.bounds);
            inst.transform.position += new Vector3(lotCenter.x - b.center.x, c0.y - b.min.y, lotCenter.z - b.center.z);
            if (cityMat != null) foreach (var rr in rs) { var ms = rr.sharedMaterials; for (int m = 0; m < ms.Length; m++) ms[m] = cityMat; rr.sharedMaterials = ms; }
            GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            if (f.accent) accents.Add(lotCenter);
            if (f.tall) tallCount++;
            last = f.prefab; placed++;
            b = rs[0].bounds; foreach (var rr in rs) b.Encapsulate(rr.bounds);
            // крыши: короба кондиционеров, вентиляция, бочки, антенны, щиты (только плоские крыши)
            if (_roofProps && f.flat && curMB != null)
                Props(curMB, new Vector3(b.center.x, b.max.y, b.center.z), b.size.x * 0.6f, b.size.z * 0.6f, f.brick, b.size.y, true);
            // пожарная лестница на фасаде, обращённом к улице (кирпичные дома)
            if (_fireEscapes && f.brick && curMB != null && R() < 0.45f)
            {
                Vector3 side = new Vector3(front.z, 0, -front.x);                          // вдоль фасада
                float halfDeep = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(front.x), 0, Mathf.Abs(front.z))));
                Vector3 face = new Vector3(b.center.x, c0.y, b.center.z) + front * (halfDeep + 0.7f) + side * R(-0.25f, 0.25f) * b.size.magnitude * 0.3f;
                FireEscape(curMB, face, side, front, Mathf.Min(b.size.y - 2f, 45f));
            }
        }

        Fit Pick(Vector3 at, float maxAlong, float maxDeep, List<Fit> from)
        {
            Fit best = default; float bestScore = -1f;
            float band = Mathf.InverseLerp(_inner, _midOuter, Vector3.Distance(at, c0));        // 0 — у стройки, 1 — край слоя
            for (int t = 0; t < 14; t++)
            {
                var f = from[_r.Next(from.Count)];
                if (f.prefab == last && from.Count > 2) continue;
                // высотки: не стеной перед камерой — только во внешней части слоя и не больше своей доли
                if (f.tall && (band < _tallFrom || tallCount > (placed + 1) * _tallShare)) continue;
                if (f.accent && (accents.Count >= _accentMax || accents.Exists(a => (a - at).sqrMagnitude < _accentSpacing * _accentSpacing))) continue;
                float along = Mathf.Max(f.size.x, f.size.z), deep = Mathf.Min(f.size.x, f.size.z);
                float sc = Mathf.Min(maxAlong / along, maxDeep / deep);
                // лучше те, что встают почти без масштабирования (0.8–1.15)
                float score = sc >= 0.8f ? (sc <= 1.15f ? 2f : 1.2f) : sc;
                score += R() * 0.6f;
                if (score > bestScore) { bestScore = score; best = f; }
            }
            return best;
        }

        for (int ix = -nx; ix <= nx; ix++)
            for (int iz = -nz; iz <= nz; iz++)
            {
                Vector2 bc = new Vector2(ix * sx + ((iz & 1) == 0 ? 0f : sx * 0.33f), iz * sz);
                float d = bc.magnitude;
                if (d < _inner + _midBlockZ * 0.5f || d > _midOuter) continue;          // кольцо среднего слоя
                Vector3 bp = c0 + new Vector3(bc.x, 0, bc.y);
                curMB = SChunk(bp);
                Street(curMB, bp, _midBlockX, _midBlockZ, _midStreet);

                float hx = _midBlockX * 0.5f - walkW, hz = _midBlockZ * 0.5f - walkW;      // внутренняя граница тротуара
                float depth = Mathf.Min(hz, hx);                                          // глубина участка (до середины квартала)
                // 1) углы
                float cornerSize = depth;
                bool[] cornerBuilt = new bool[4];
                for (int k = 0; k < 4; k++)
                {
                    if (R() > _midCornerChance) continue;
                    float cxs = (k & 1) == 0 ? -1 : 1, czs = (k & 2) == 0 ? -1 : 1;
                    Vector3 lc = bp + new Vector3(cxs * (hx - cornerSize * 0.5f), 0, czs * (hz - cornerSize * 0.5f));
                    var list = corners.Count > 0 && R() < 0.5f ? corners : pool;
                    var f = Pick(lc, cornerSize, cornerSize, list);
                    if (f.prefab == null) continue;
                    Place(f, lc, new Vector3(cxs, 0, czs).normalized, cornerSize, cornerSize, true);
                    cornerBuilt[k] = true;
                }
                // 2) стороны: вдоль улицы, вплотную, между углами
                for (int side = 0; side < 4; side++)
                {
                    bool alongX = side < 2;                         // 0: юг, 1: север (вдоль X); 2: запад, 3: восток (вдоль Z)
                    float sgn = (side & 1) == 0 ? -1f : 1f;
                    float half = alongX ? hx : hz;
                    float deep = alongX ? Mathf.Min(depth, hz) : Mathf.Min(depth, hx);
                    if (!alongX) deep = Mathf.Min(deep, hx - cornerSize * 0.0f);
                    float start = -half + cornerSize, end = half - cornerSize;
                    if (!alongX) { start = -hz + cornerSize; end = hz - cornerSize; deep = Mathf.Min(cornerSize, hx - cornerSize); }
                    float cur = start;
                    Vector3 front = alongX ? new Vector3(0, 0, sgn) : new Vector3(sgn, 0, 0);
                    while (end - cur > 8f)
                    {
                        float room = end - cur;
                        float want = Mathf.Min(room, R(22f, 60f));
                        Vector3 probe = alongX ? bp + new Vector3(cur + want * 0.5f, 0, sgn * (hz - deep * 0.5f))
                                               : bp + new Vector3(sgn * (hx - deep * 0.5f), 0, cur + want * 0.5f);
                        var f = Pick(probe, room, deep, pool);
                        if (f.prefab == null) { cur += 8f; continue; }
                        float along = Mathf.Max(f.size.x, f.size.z), dpp = Mathf.Min(f.size.x, f.size.z);
                        float sc = Mathf.Clamp(Mathf.Min(room / along, deep / dpp), 0.5f, 1.15f);
                        float w = along * sc;
                        if (w > room + 0.5f) { cur += 6f; continue; }
                        Vector3 lc = alongX ? bp + new Vector3(cur + w * 0.5f, 0, sgn * (hz - dpp * sc * 0.5f))
                                            : bp + new Vector3(sgn * (hx - dpp * sc * 0.5f), 0, cur + w * 0.5f);
                        Place(f, lc, front, room, deep, alongX);
                        cur += w + R(0.3f, 1.5f);
                    }
                }
            }

        foreach (var kv in streets)
        {
            if (kv.Value.v.Count == 0) continue;
            var mesh = kv.Value.ToMesh("Streets_" + kv.Key.x + "_" + kv.Key.y);
            AssetDatabase.CreateAsset(mesh, GenDir + "/Meshes/" + mesh.name + ".asset");
            var go = new GameObject(mesh.name); go.transform.SetParent(midRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = skyMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }
        Debug.Log($"[Город] средний слой: домов {placed}, акцентов {accents.Count}.", midRoot);
    }

    static Color H(string h) { ColorUtility.TryParseHtmlString(h, out Color c); return c; }

    /// <summary>Пожарная лестница: площадки каждые 3.5 м, перила, марши зигзагом.</summary>
    void FireEscape(MB mb, Vector3 face, Vector3 along, Vector3 outDir, float h)
    {
        Color iron = H("#2E2A2C");
        float w = 4f, dep = 1.3f;
        int n = 0;
        for (float y = 4.5f; y < h; y += 3.5f, n++)
        {
            Vector3 p = face + Vector3.up * y;
            bool ax = Mathf.Abs(along.x) > 0.5f;
            mb.Box(p, ax ? w : dep, ax ? dep : w, 0.12f, 0, iron, iron, 0, 0, 0);                               // площадка
            mb.Box(p + outDir * dep * 0.5f, ax ? w : 0.06f, ax ? 0.06f : w, 1.0f, 0, iron, iron, 0, 0, 0);        // перила
            Vector3 st = p + along * ((n & 1) == 0 ? 0.9f : -0.9f) + Vector3.up * 1.7f;
            mb.Box(st, ax ? 2.6f : 0.6f, ax ? 0.6f : 2.6f, 0.1f, 0, iron, iron, 0, 0, 0);                         // марш
        }
    }

    void Car(MB mb, Vector3 p, bool alongX)
    {
        Color[] cols = { H("#E8A832"), H("#C8443C"), H("#4E8FA8"), H("#EAD9B5"), H("#6E9A52"), H("#3C3A40") };
        Color c = cols[_r.Next(cols.Length)], gl = H("#9FB8C8"), tyre = H("#222024");
        bool taxi = R() < 0.25f; if (taxi) c = H("#F2C230");
        float L = taxi ? 4.6f : R(3.8f, 4.8f), W = 1.8f;
        mb.Box(p + Vector3.up * 0.35f, alongX ? L : W, alongX ? W : L, 0.75f, 0, c, c, 0, 0, 0);
        mb.Box(p + Vector3.up * 1.1f, alongX ? L * 0.55f : W * 0.9f, alongX ? W * 0.9f : L * 0.55f, 0.6f, 0, gl, c, 0, 0, 0);
        mb.Box(p, alongX ? L * 0.8f : W * 1.02f, alongX ? W * 1.02f : L * 0.8f, 0.35f, 0, tyre, tyre, 0, 0, 0);
    }

    /// <summary>
    /// Квартал с улицами вокруг: каждый квартал кладёт свою половину улицы с каждой стороны —
    /// соседи стыкуются в полную улицу. Тротуар с бордюром, разметка, зебры на всех углах,
    /// фонари, деревья, гидранты, киоски, метро, остановки, люки.
    /// </summary>
    void Street(MB mb, Vector3 bp, float bx, float bz, float st)
    {
        Color asphalt = H("#4A4A52"), walk = H("#CDBFA8"), curb = H("#E6DCC8"), line = H("#E8D9A0"), white = H("#EEE8DC");
        Color metal = H("#3C3A40"), green = H("#2E6B4E"), red = H("#C8443C"), glass = H("#9FB8C8"), leaf = H("#6E9A52"), trunk = H("#6B4A34");
        float hs = st * 0.5f;
        // асфальт: полоса половины улицы по периметру
        mb.Box(bp + new Vector3(0, -0.02f, bz * 0.5f + hs * 0.5f), bx + st, hs, 0.06f, 0, asphalt, asphalt, 0, 0, 0);
        mb.Box(bp + new Vector3(0, -0.02f, -bz * 0.5f - hs * 0.5f), bx + st, hs, 0.06f, 0, asphalt, asphalt, 0, 0, 0);
        mb.Box(bp + new Vector3(bx * 0.5f + hs * 0.5f, -0.02f, 0), hs, bz, 0.06f, 0, asphalt, asphalt, 0, 0, 0);
        mb.Box(bp + new Vector3(-bx * 0.5f - hs * 0.5f, -0.02f, 0), hs, bz, 0.06f, 0, asphalt, asphalt, 0, 0, 0);
        // осевая разметка (на краю нашей половины = середина улицы), только с двух сторон, чтобы не дублировать
        for (float x = -bx * 0.5f; x < bx * 0.5f; x += 7f)
            mb.Box(bp + new Vector3(x, 0.03f, bz * 0.5f + hs), 3.5f, 0.22f, 0.03f, 0, line, line, 0, 0, 0);
        for (float z = -bz * 0.5f; z < bz * 0.5f; z += 7f)
            mb.Box(bp + new Vector3(bx * 0.5f + hs, 0.03f, z), 0.22f, 3.5f, 0.03f, 0, line, line, 0, 0, 0);
        // зебры на четырёх углах
        for (int c = 0; c < 4; c++)
        {
            float cx = (c & 1) == 0 ? -1 : 1, cz = (c & 2) == 0 ? -1 : 1;
            for (int k = 0; k < 5; k++)
                mb.Box(bp + new Vector3(cx * (bx * 0.5f - 1.5f - k * 1.5f), 0.03f, cz * (bz * 0.5f + hs * 0.5f)), 0.75f, hs * 0.9f, 0.03f, 0, white, white, 0, 0, 0);
        }
        // машины: на своей половине улицы, в обе стороны
        if (_cars)
        {
            for (float x = -bx * 0.5f + 4f; x < bx * 0.5f - 4f; x += R(7f, 22f))
                if (R() < 0.55f) Car(mb, bp + new Vector3(x, 0.04f, bz * 0.5f + hs * 0.5f), true);
            for (float z = -bz * 0.5f + 4f; z < bz * 0.5f - 4f; z += R(7f, 22f))
                if (R() < 0.45f) Car(mb, bp + new Vector3(bx * 0.5f + hs * 0.5f, 0.04f, z), false);
        }
        // тротуар: бордюр по периметру + плитка
        mb.Box(bp, bx, bz, 0.22f, 0, curb, curb, 0, 0, 0);
        mb.Box(bp, bx - 0.6f, bz - 0.6f, 0.26f, 0, walk, walk, 0, 0, 0);
        // фонари, деревья, мелочь — по всем сторонам
        for (int side = 0; side < 4; side++)
        {
            bool alongX = side < 2; float sgn = (side & 1) == 0 ? -1f : 1f;
            float len = alongX ? bx : bz, off = (alongX ? bz : bx) * 0.5f - 0.9f;
            for (float u = -len * 0.5f + 8f; u < len * 0.5f - 6f; u += 16f)
            {
                Vector3 e = bp + (alongX ? new Vector3(u, 0.26f, sgn * off) : new Vector3(sgn * off, 0.26f, u));
                Vector3 outDir = alongX ? new Vector3(0, 0, sgn) : new Vector3(sgn, 0, 0);
                mb.Box(e, 0.22f, 0.22f, 6f, 0, metal, metal, 0, 0, 0);
                mb.Box(e + Vector3.up * 5.8f + outDir * 0.7f, alongX ? 0.18f : 1.4f, alongX ? 1.4f : 0.18f, 0.18f, 0, metal, metal, 0, 0, 0);
                mb.Box(e + Vector3.up * 5.5f + outDir * 1.3f, 0.5f, 0.5f, 0.35f, 0, line, line, 0, 0, 0);
                Vector3 t = e + (alongX ? new Vector3(8f, 0, 0) : new Vector3(0, 0, 8f));
                if (R() < 0.75f)
                {
                    mb.Box(t, 1.4f, 1.4f, 0.1f, 0, trunk, trunk, 0, 0, 0);                           // приствольная решётка
                    mb.Box(t, 0.32f, 0.32f, 2.6f, 0, trunk, trunk, 0, 0, 0);
                    mb.Prism(t + Vector3.up * 2.4f, R(1.5f, 2.1f), R(2.6f, 3.6f), 7, leaf, leaf, 0, 0, 0, 0.55f);
                    mb.Prism(t + Vector3.up * 4.2f, R(1.0f, 1.4f), R(1.4f, 2.0f), 7, leaf, leaf, 0, 0, 0, 0.3f);
                }
                if (R() < 0.1f) mb.Box(e + (alongX ? new Vector3(4f, 0, 0) : new Vector3(0, 0, 4f)), 0.4f, 0.4f, 0.8f, 0, red, red, 0, 0, 0);
                if (R() < 0.05f) mb.Box(e + (alongX ? new Vector3(-5f, 0, 0) : new Vector3(0, 0, -5f)) - outDir * 1.2f, 2.2f, 1.4f, 2.4f, 0, green, green, 0, 0, 0);
            }
        }
        // вход в метро (на углу) и остановка
        if (R() < 0.3f)
        {
            Vector3 m = bp + new Vector3(bx * 0.5f - 7f, 0.26f, bz * 0.5f - 2.2f);
            mb.Box(m, 6f, 0.15f, 1.1f, 0, green, green, 0, 0, 0); mb.Box(m + new Vector3(0, 0, -2.6f), 6f, 0.15f, 1.1f, 0, green, green, 0, 0, 0);
            mb.Box(m + new Vector3(-3f, 0, -1.3f), 0.15f, 2.7f, 1.1f, 0, green, green, 0, 0, 0);
            mb.Box(m + new Vector3(0.5f, 0.02f, -1.3f), 4.5f, 2.3f, 0.02f, 0, metal, metal, 0, 0, 0);
            for (int g = 0; g < 2; g++)
            {
                mb.Box(m + new Vector3(3f, 0, -g * 2.6f), 0.2f, 0.2f, 3f, 0, green, green, 0, 0, 0);
                mb.Prism(m + new Vector3(3f, 3f, -g * 2.6f), 0.35f, 0.6f, 6, H("#7FD08A"), H("#7FD08A"), 0, 0, 0, 0.6f);
            }
        }
        if (R() < 0.35f)
        {
            Vector3 s0 = bp + new Vector3(R(-bx, bx) * 0.3f, 0.26f, -(bz * 0.5f - 1.3f));
            mb.Box(s0 + Vector3.up * 2.6f, 4.5f, 1.8f, 0.15f, 0, metal, metal, 0, 0, 0);
            mb.Box(s0 + new Vector3(0, 0, 0.8f), 4.5f, 0.1f, 2.6f, 0, glass, glass, 0, 0, 0);
            mb.Box(s0 + new Vector3(-2.2f, 0, 0), 0.12f, 0.12f, 2.6f, 0, metal, metal, 0, 0, 0);
            mb.Box(s0 + new Vector3(2.2f, 0, 0), 0.12f, 0.12f, 2.6f, 0, metal, metal, 0, 0, 0);
            mb.Box(s0 + new Vector3(2.7f, 0, -0.5f), 0.15f, 0.15f, 3.2f, 0, metal, metal, 0, 0, 0);
            mb.Box(s0 + new Vector3(2.7f, 3f, -0.5f), 0.7f, 0.1f, 0.7f, 0, red, red, 0, 0, 0);
        }
    }

    void Building(MB mb, Vector3 p, float w, float d, float h, bool hero, bool detail, bool tower)
    {
        float seed = R() * 100f;
        Color col = hero ? Jitter(Hex(HeroColors[_r.Next(HeroColors.Length)])) : PickFacade();
        Color roof = RoofOf(col);
        float fw = Mathf.Max(8f, Mathf.Min(w, d * 2f)), fd = Mathf.Max(8f, Mathf.Min(d, w * 2f));
        float style = new[] { 1f, 1f, 2f, 3f, 4f }[_r.Next(5)];
        Vector3 top; float tw, td;

        int kind;
        if (hero) kind = new[] { 2, 3, 6 }[_r.Next(3)];
        else if (tower || h > _midMax * 1.1f) kind = new[] { 0, 2, 3, 6, 4 }[_r.Next(5)];
        else if (h < _midMin) kind = R() < 0.7f ? 5 : 1;
        else kind = new[] { 7, 7, 8, 9, 1, 4, 0, 10 }[_r.Next(8)];

        switch (kind)
        {
            case 0: // башня с уступами и карнизами
            {
                int tiers = _r.Next(2, 5); float y = 0, cw = fw * R(0.8f, 0.95f), cd = fd * R(0.8f, 0.95f);
                for (int i = 0; i < tiers && y < h; i++)
                {
                    float th = i == tiers - 1 ? Mathf.Max(8f, h - y) : h * (i == 0 ? R(0.45f, 0.6f) : R(0.15f, 0.25f));
                    mb.Box(p + Vector3.up * y, cw, cd, th, 0, col, roof, style, seed, y);
                    if (detail) Cornices(mb, p, cw, cd, y, y + th, roof);
                    y += th; cw *= R(0.72f, 0.86f); cd *= R(0.72f, 0.86f);
                }
                top = p + Vector3.up * y; tw = cw / 0.8f; td = cd / 0.8f;
                break;
            }
            case 1: // пластина
            {
                bool along = fw >= fd; float sw = Mathf.Max(fw, fd) * 0.95f, sd = Mathf.Min(fw, fd) * R(0.55f, 0.75f);
                tw = along ? sw : sd; td = along ? sd : sw;
                mb.Box(p, tw, td, h, 0, col, roof, 2, seed, 0);
                if (detail) Cornices(mb, p, tw, td, 0, h, roof);
                top = p + Vector3.up * h;
                break;
            }
            case 2: // ар-деко: уступы, корона, шпиль
            {
                float y = 0, cw = fw * 0.88f, cd = fd * 0.88f;
                for (int i = 0; i < 5 && y < h; i++)
                {
                    float th = i == 0 ? h * 0.48f : h * 0.13f;
                    mb.Box(p + Vector3.up * y, cw, cd, th, 0, col, roof, 3, seed, y);
                    // «плечи» по углам уступа
                    if (i > 0 && detail) for (int k = 0; k < 4; k++)
                        mb.Box(p + Vector3.up * y + new Vector3(((k & 1) == 0 ? -1 : 1) * cw * 0.42f, 0, ((k & 2) == 0 ? -1 : 1) * cd * 0.42f), cw * 0.12f, cd * 0.12f, th * 0.6f, 0, roof, roof, 0, 0, 0);
                    y += th; cw *= 0.8f; cd *= 0.8f;
                }
                mb.Pyramid(p + Vector3.up * y, cw, cd, h * 0.1f, 0, roof);
                mb.Box(p + Vector3.up * (y + h * 0.08f), 0.9f, 0.9f, h * 0.2f, 0, Metal, Metal, 0, 0, 0);
                return;
            }
            case 3: // восьмигранная стеклянная с куполом
            {
                float r = Mathf.Min(fw, fd) * 0.46f;
                mb.Prism(p, r * 1.25f, h * 0.12f, 8, roof, roof, 1, seed, 0);            // подиум
                mb.Prism(p + Vector3.up * h * 0.12f, r, h * 0.74f, 8, col, roof, 4, seed, h * 0.12f, 0.92f);
                mb.Prism(p + Vector3.up * h * 0.86f, r * 0.92f, h * 0.1f, 8, roof, roof, 0, seed, 0, 0.3f);
                mb.Box(p + Vector3.up * h * 0.95f, 0.7f, 0.7f, h * 0.15f, 0, Metal, Metal, 0, 0, 0);
                return;
            }
            case 4: // двойная
            {
                float half = fw * 0.47f; Color col2 = R() < 0.5f ? col : PickFacade();
                mb.Box(p + new Vector3(-fw * 0.25f, 0, 0), half, fd * 0.9f, h, 0, col, roof, style, seed, 0);
                float h2 = h * R(0.55f, 0.8f);
                mb.Box(p + new Vector3(fw * 0.25f, 0, 0), half, fd * 0.75f, h2, 0, col2, RoofOf(col2), 2, seed + 3, 0);
                if (detail) Props(mb, p + new Vector3(fw * 0.25f, h2, 0), half, fd * 0.75f, false, h2);
                top = p + new Vector3(-fw * 0.25f, h, 0); tw = half; td = fd * 0.9f;
                break;
            }
            case 6: // башня на подиуме
            {
                float ph = R(10f, 22f);
                mb.Box(p, fw, fd, ph, 0, roof, RoofOf(roof), 2, seed, 0);
                float tw0 = fw * R(0.45f, 0.6f), td0 = fd * R(0.45f, 0.6f);
                mb.Box(p + Vector3.up * ph, tw0, td0, h - ph, 0, col, roof, 4, seed, ph);
                if (detail) Cornices(mb, p, tw0, td0, ph, h, roof);
                top = p + Vector3.up * h; tw = tw0; td = td0;
                break;
            }
            case 7: // средний дом с карнизами и пентхаусом
            {
                tw = fw * 0.94f; td = fd * 0.94f;
                mb.Box(p, tw, td, h, 0, col, roof, style, seed, 0);
                Cornices(mb, p, tw, td, 0, h, roof, detail ? 1f : 2.2f);
                mb.Box(p + Vector3.up * h, tw * R(0.3f, 0.5f), td * R(0.3f, 0.5f), R(4f, 8f), 0, col, roof, 1, seed, h);
                top = p + Vector3.up * h;
                break;
            }
            case 8: // Г-образный
            {
                float a = fw * 0.94f, b = fd * 0.94f;
                mb.Box(p + new Vector3(0, 0, -b * 0.25f), a, b * 0.5f, h, 0, col, roof, style, seed, 0);
                float h2 = h * R(0.65f, 1.15f);
                mb.Box(p + new Vector3(-a * 0.25f, 0, b * 0.25f), a * 0.5f, b * 0.5f, h2, 0, col, roof, style, seed, 0);
                top = p + new Vector3(0, h, -b * 0.25f); tw = a; td = b * 0.5f;
                break;
            }
            case 9: // дом со скатной (шатровой) крышей
            {
                tw = fw * 0.9f; td = fd * 0.9f;
                mb.Box(p, tw, td, h, 0, col, roof, style, seed, 0);
                if (detail) Cornices(mb, p, tw, td, 0, h, roof);
                mb.Pyramid(p + Vector3.up * h, tw * 1.04f, td * 1.04f, Mathf.Min(tw, td) * R(0.25f, 0.4f), 0, Jitter(Hex("#8A4A34")));
                return;
            }
            case 10: // ступенчатый «свадебный торт» средней высоты
            {
                float y = 0, cw = fw * 0.95f, cd = fd * 0.95f;
                for (int i = 0; i < 3; i++)
                {
                    float th = h * (i == 0 ? 0.55f : 0.225f);
                    mb.Box(p + Vector3.up * y, cw, cd, th, 0, col, roof, style, seed, y);
                    y += th; cw *= 0.82f; cd *= 0.82f;
                }
                top = p + Vector3.up * y; tw = cw / 0.82f; td = cd / 0.82f;
                break;
            }
            default: // низкий кирпичный
            {
                float lh = Mathf.Clamp(h, 12f, 36f);
                tw = fw * 0.92f; td = fd * 0.92f;
                mb.Box(p, tw, td, lh, 0, col, roof, 1, seed, 0);
                top = p + Vector3.up * lh;
                Props(mb, top, tw, td, true, lh);
                return;
            }
        }
        Props(mb, top, tw, td, false, h, detail);
        // баннер на фасаде (видно издалека)
        if (R() < (detail ? 0.12f : 0.06f) && h > 30f)
        {
            Color bb = Hex(new[] { "#E8A832", "#C8643C", "#4E8FA8", "#F0B48C", "#E8E0D0" }[_r.Next(5)]);
            float side = R() < 0.5f ? -1f : 1f;
            mb.Box(p + new Vector3(tw * 0.5f * side + side * 0.4f, h * R(0.35f, 0.6f), R(-0.2f, 0.2f) * td), 0.5f, td * R(0.3f, 0.5f), h * R(0.15f, 0.3f), 0, bb, bb, 0, 0, 0);
        }
    }

    /// <summary>Карнизы-пояса через несколько этажей: фасад перестаёт быть гладкой коробкой.</summary>
    void Cornices(MB mb, Vector3 p, float w, float d, float y0, float y1, Color c, float spacingMul = 1f)
    {
        float step = R(12f, 20f) * spacingMul;
        for (float y = y0 + step; y < y1 - 3f; y += step)
            mb.Box(p + Vector3.up * y, w + 0.8f, d + 0.8f, 0.6f, 0, c, c, 0, 0, 0);
        mb.Box(p + Vector3.up * (y1 - 0.2f), w + 1.0f, d + 1.0f, 1.0f, 0, c, c, 0, 0, 0);   // венчающий карниз
    }

    void Props(MB mb, Vector3 top, float w, float d, bool tankLikely, float h, bool detail = true)
    {
        float k = detail ? _props : _props * 0.25f;
        // кондиционеры и вентиляция (короба + трубы-«грибки»)
        if (detail)
        {
            int n = _r.Next(1, 5);
            for (int i = 0; i < n; i++)
                if (R() < k) mb.Box(top + new Vector3(R(-w, w) * 0.32f, 0, R(-d, d) * 0.32f), R(2f, 4.5f), R(2f, 4.5f), R(1.2f, 2.4f), 0, Ac, Ac, 0, 0, 0);
            int vents = _r.Next(0, 4);
            for (int i = 0; i < vents; i++)
                if (R() < k) mb.Prism(top + new Vector3(R(-w, w) * 0.35f, 0, R(-d, d) * 0.35f), R(0.4f, 0.9f), R(1.5f, 3f), 6, Metal, Metal, 0, 0, 0, 1.4f);
        }
        // водонапорная бочка
        if (R() < (tankLikely ? 0.65f : 0.3f) * (0.4f + k))
        {
            Vector3 tp = top + new Vector3(R(-w, w) * 0.25f, 0, R(-d, d) * 0.25f);
            mb.Box(tp, 3.4f, 3.4f, 2.6f, 0, Metal, Metal, 0, 0, 0);
            mb.Prism(tp + Vector3.up * 2.6f, 2.3f, 4.2f, 10, Tank, Tank, 0, 0, 0);
            mb.Prism(tp + Vector3.up * 6.8f, 2.4f, 1.5f, 10, Metal, Metal, 0, 0, 0, 0.1f);
        }
        // антенны (пучком)
        if (R() < 0.35f * (0.5f + k))
        {
            int a = _r.Next(1, 4);
            for (int i = 0; i < a; i++) mb.Box(top + new Vector3(R(-w, w) * 0.2f, 0, R(-d, d) * 0.2f), 0.45f, 0.45f, R(6f, 20f), 0, Metal, Metal, 0, 0, 0);
        }
        // щит на крыше
        if (R() < 0.15f)
        {
            Color bb = Hex(new[] { "#E8A832", "#C8643C", "#4E8FA8", "#F0B48C" }[_r.Next(4)]);
            bool x = w > d;
            mb.Box(top + Vector3.up * 3f, x ? w * 0.7f : 0.6f, x ? 0.6f : d * 0.7f, R(5f, 8f), 0, bb, bb, 0, 0, 0);
            mb.Box(top, 0.6f, 0.6f, 3f, 0, Metal, Metal, 0, 0, 0);
        }
        // кран на крыше (стройка в городе — силуэт видно издалека)
        if (h > 60f && R() < 0.06f)
        {
            Color yel = Hex("#E8A832");
            Vector3 cp = top + new Vector3(R(-w, w) * 0.2f, 0, R(-d, d) * 0.2f);
            float mast = R(18f, 32f), jib = R(18f, 30f), ang = R(0f, 360f);
            mb.Box(cp, 1.4f, 1.4f, mast, 0, yel, yel, 0, 0, 0);
            var q = Quaternion.Euler(0, ang, 0);
            mb.Box(cp + Vector3.up * mast + q * Vector3.forward * (jib * 0.35f), 1f, jib, 1f, ang, yel, yel, 0, 0, 0);
            mb.Box(cp + Vector3.up * (mast - 1.5f) - q * Vector3.forward * (jib * 0.2f), 2.2f, 4f, 1.6f, ang, Metal, Metal, 0, 0, 0);
            mb.Box(cp + Vector3.up * mast, 0.8f, 0.8f, 5f, 0, yel, yel, 0, 0, 0);
        }
    }
}
