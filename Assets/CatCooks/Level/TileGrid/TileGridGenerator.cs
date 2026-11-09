using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CatCooks.Level
{
    /// <summary>Форма сетки.</summary>
    public enum TileGridShape
    {
        Rectangle = 0,   // прямоугольник
        Ellipse = 1,     // круг / овал
        Ring = 2,        // кольцо (толщина — Thickness клеток)
        Diamond = 3,     // ромб
        Cross = 4,       // крест (ширина плеча — Arm)
        LShape = 5,      // буква Г (ширина — Arm)
        UShape = 6,      // буква П (ширина — Arm)
        Triangle = 7,    // треугольник (остриё вверх по Z)
        Frame = 8,       // рамка прямоугольника (толщина — Thickness)
        Custom = 20,     // нарисовать мышью в инспекторе
        TextureMask = 21 // по картинке: светлое/непрозрачное — плитка
    }

    /// <summary>Вариант плитки: модель/префаб и вес (чаще/реже).</summary>
    [System.Serializable]
    public sealed class TileVariant
    {
        public GameObject Prefab;
        [Min(0f)] public float Weight = 1f;
    }

    /// <summary>
    /// ГЕНЕРАТОР СЕТКИ ИЗ ПЛИТОК — раскладывает модели (тротуарная плитка, пол, настил…) сеткой нужной формы.
    ///
    ///   • Варианты: одна или несколько моделей с весами — раскладываются вперемешку (Seed — тот же узор каждый раз).
    ///   • Размер клетки — САМ по габариту первой модели (или вручную), плюс зазор. Смещённый пивот модели не мешает:
    ///     плитка ставится центром габарита в центр клетки и низом на землю.
    ///   • Формы: прямоугольник, круг/овал, кольцо, рамка, ромб, крест, Г, П, треугольник, СВОЯ (рисуется мышью
    ///     в инспекторе) и ПО КАРТИНКЕ (светлое/непрозрачное = плитка). Галка Invert — наоборот (дырка нужной формы).
    ///   • Случайный поворот на 90° (квадратные плитки) — швы не повторяются.
    ///   • «Объединить» — все плитки в один меш на материал (+ MeshCollider), меш сохраняется ассетом — тогда
    ///     объект можно сохранить префабом руками, меш не потеряется.
    ///
    /// Плитки — дочерний объект «Tiles (generated)». Перегенерация его пересобирает (ручные правки внутри теряются).
    /// Создать: CatCooks → Сетка плиток → Создать генератор (выдели модель в Project — подставится сама).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TileGridGenerator : MonoBehaviour
    {
        public const string ContainerName = "Tiles (generated)";

        [Header("Плитки")]
        public List<TileVariant> Variants = new List<TileVariant>();
        [Tooltip("Размер клетки берётся по габариту первой модели (X/Z). Выключи — задай вручную")]
        public bool AutoSize = true;
        [Tooltip("Размер клетки вручную, м (X — ширина, Y — глубина по Z)")]
        public Vector2 ManualSize = new Vector2(2.5f, 2.5f);
        [Tooltip("Зазор между плитками, м (можно отрицательный — внахлёст, убрать щели)")]
        public float Gap = 0f;

        [Header("Форма")]
        public TileGridShape Shape = TileGridShape.Rectangle;
        [Min(1)] public int Width = 8;
        [Min(1)] public int Depth = 8;
        [Tooltip("Кольцо и рамка: толщина, клеток")]
        [Min(1)] public int Thickness = 2;
        [Tooltip("Крест, Г, П: ширина плеча, клеток")]
        [Min(1)] public int Arm = 2;
        [Tooltip("Наоборот: плитки там, где формы нет (дырка нужной формы в прямоугольнике)")]
        public bool Invert;
        [Tooltip("По картинке: любая текстура (Read/Write не нужен). Плитка там, где яркость×альфа выше порога")]
        public Texture2D MaskTexture;
        [Range(0.01f, 0.99f)] public float MaskThreshold = 0.5f;

        [Header("Раскладка")]
        [Tooltip("Центр сетки — в этом объекте (иначе угол: сетка растёт в +X и +Z)")]
        public bool CenterPivot = true;
        [Tooltip("Низ плитки — на высоту объекта")]
        public bool SnapToGround = true;
        [Tooltip("Случайно крутить плитки на 90° (для квадратных; прямоугольные — только 0/180°)")]
        public bool RandomRotate = true;
        public int Seed = 1;
        [Tooltip("Плитки — Static (для запекания света и батчинга)")]
        public bool MarkStatic = true;

        [Header("Объединение")]
        [Tooltip("На объединённый меш — MeshCollider")]
        public bool CombinedCollider = true;
        [Tooltip("Куда сохранять объединённые меши (ассеты)")]
        public string MeshFolder = "Assets/Generated/TileGrids";

        [Header("Отладка")]
        public bool Log = true;

        // своя форма (рисуется в инспекторе): Width × Depth, строка за строкой по Z
        [SerializeField, HideInInspector] private bool[] _mask = new bool[0];
        [SerializeField, HideInInspector] private int _maskW, _maskD;

        [System.NonSerialized] private float[] _texCache;
        [System.NonSerialized] private Texture2D _texCacheOf;
        [System.NonSerialized] private int _texCacheW, _texCacheD;

        // ───────────────────────── ФОРМА ─────────────────────────

        /// <summary>Есть ли плитка в клетке (x — по X, z — по Z, от угла).</summary>
        public bool CellOn(int x, int z)
        {
            if (x < 0 || z < 0 || x >= Width || z >= Depth) return false;
            bool on;
            if (Shape == TileGridShape.Custom) on = MaskGet(x, z);
            else if (Shape == TileGridShape.TextureMask) on = TextureOn(x, z);
            else on = ShapeOn(Shape, x, z, Width, Depth, Thickness, Arm);
            return Invert ? !on : on;
        }

        /// <summary>Геометрическая форма (без своей и картинки). Чистая функция — проверяется без Unity.</summary>
        public static bool ShapeOn(TileGridShape shape, int x, int z, int w, int d, int thick, int arm)
        {
            float cx = x + 0.5f - w * 0.5f, cz = z + 0.5f - d * 0.5f;     // центр клетки от центра сетки, в клетках
            float rx = w * 0.5f, rz = d * 0.5f;
            switch (shape)
            {
                case TileGridShape.Ellipse:
                    return (cx * cx) / (rx * rx) + (cz * cz) / (rz * rz) <= 1.0001f;
                case TileGridShape.Ring:
                {
                    if ((cx * cx) / (rx * rx) + (cz * cz) / (rz * rz) > 1.0001f) return false;
                    float ix = rx - thick, iz = rz - thick;
                    if (ix <= 0f || iz <= 0f) return true;
                    return (cx * cx) / (ix * ix) + (cz * cz) / (iz * iz) >= 1f;
                }
                case TileGridShape.Frame:
                    return x < thick || z < thick || x >= w - thick || z >= d - thick;
                case TileGridShape.Diamond:
                    return Mathf.Abs(cx) / rx + Mathf.Abs(cz) / rz <= 1.0001f;
                case TileGridShape.Cross:
                {
                    int ax = Mathf.Min(arm, w), az = Mathf.Min(arm, d);
                    int x0 = (w - ax) / 2, z0 = (d - az) / 2;
                    return (x >= x0 && x < x0 + ax) || (z >= z0 && z < z0 + az);
                }
                case TileGridShape.LShape:
                    return x < arm || z < arm;
                case TileGridShape.UShape:
                    return x < arm || x >= w - arm || z < arm;
                case TileGridShape.Triangle:
                    return Mathf.Abs(cx) <= rx * (1f - (z + 0.5f) / d) + 0.5f;
                default:
                    return true;
            }
        }

        public bool MaskGet(int x, int z)
        {
            if (_mask == null || _maskW != Width || _maskD != Depth) ResizeMask();
            return _mask[z * Width + x];
        }

        public void MaskSet(int x, int z, bool on)
        {
            if (x < 0 || z < 0 || x >= Width || z >= Depth) return;
            if (_mask == null || _maskW != Width || _maskD != Depth) ResizeMask();
            _mask[z * Width + x] = on;
        }

        /// <summary>Своя форма ← текущая (любая) форма: дальше дорисовать мышью.</summary>
        public void BakeShapeToMask()
        {
            bool[] m = new bool[Width * Depth];
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++) m[z * Width + x] = CellOn(x, z);
            _mask = m; _maskW = Width; _maskD = Depth;
            Shape = TileGridShape.Custom; Invert = false;
        }

        public void MaskFill(bool on)
        {
            ResizeMask();
            for (int i = 0; i < _mask.Length; i++) _mask[i] = on;
        }

        public void MaskInvert()
        {
            ResizeMask();
            for (int i = 0; i < _mask.Length; i++) _mask[i] = !_mask[i];
        }

        /// <summary>Поменяли Width/Depth — рисунок сохраняется в пересечении (растёт от угла).</summary>
        private void ResizeMask()
        {
            if (_mask != null && _maskW == Width && _maskD == Depth && _mask.Length == Width * Depth) return;
            bool[] m = new bool[Width * Depth];
            bool fresh = _mask == null || _mask.Length == 0;
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                    m[z * Width + x] = fresh || (x < _maskW && z < _maskD && _mask != null && z * _maskW + x < _mask.Length && _mask[z * _maskW + x]);
            _mask = m; _maskW = Width; _maskD = Depth;
        }

        private bool TextureOn(int x, int z)
        {
            if (MaskTexture == null) return true;
            if (_texCache == null || _texCacheOf != MaskTexture || _texCacheW != Width || _texCacheD != Depth) ReadTexture();
            return _texCache != null && _texCache[z * Width + x] >= MaskThreshold;
        }

        /// <summary>Картинка → значения по клеткам: блит в маленькую RenderTexture (Read/Write у текстуры не нужен).</summary>
        private void ReadTexture()
        {
            _texCacheOf = MaskTexture; _texCacheW = Width; _texCacheD = Depth;
            _texCache = new float[Width * Depth];
            RenderTexture rt = RenderTexture.GetTemporary(Width, Depth, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture was = RenderTexture.active;
            Graphics.Blit(MaskTexture, rt);
            RenderTexture.active = rt;
            Texture2D tmp = new Texture2D(Width, Depth, TextureFormat.RGBA32, false, true);
            tmp.ReadPixels(new Rect(0, 0, Width, Depth), 0, 0);
            tmp.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = tmp.GetPixels32();
            for (int i = 0; i < px.Length && i < _texCache.Length; i++)
            {
                Color32 c = px[i];
                float lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                _texCache[i] = lum * (c.a / 255f);
            }
            DestroyImmediate(tmp);
        }

        public void ForgetTexture() { _texCache = null; }

        public int CountCells()
        {
            int n = 0;
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++) if (CellOn(x, z)) n++;
            return n;
        }

        // ───────────────────────── РАЗМЕР ПЛИТКИ ─────────────────────────

        /// <summary>Габарит модели в осях её корня (с поворотом и масштабом корня, без позиции). false — мешей нет.</summary>
        public static bool MeasurePrefab(GameObject prefab, out Bounds b)
        {
            b = new Bounds(Vector3.zero, Vector3.zero);
            if (prefab == null) return false;
            Transform root = prefab.transform;
            Matrix4x4 rootNoPos = Matrix4x4.TRS(Vector3.zero, root.localRotation, root.localScale);
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            bool any = false;
            MeshFilter[] mfs = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < mfs.Length; i++)
            {
                if (mfs[i] == null || mfs[i].sharedMesh == null) continue;
                Matrix4x4 m = rootNoPos * toRoot * mfs[i].transform.localToWorldMatrix;
                Bounds mb = mfs[i].sharedMesh.bounds;
                Vector3 mn = mb.min, mx = mb.max;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = new Vector3((k & 1) == 0 ? mn.x : mx.x, (k & 2) == 0 ? mn.y : mx.y, (k & 4) == 0 ? mn.z : mx.z);
                    p = m.MultiplyPoint3x4(p);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            SkinnedMeshRenderer[] sk = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < sk.Length; i++)
            {
                if (sk[i] == null || sk[i].sharedMesh == null) continue;
                Matrix4x4 m = rootNoPos * toRoot * sk[i].transform.localToWorldMatrix;
                Bounds mb = sk[i].sharedMesh.bounds;
                Vector3 mn = mb.min, mx = mb.max;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = new Vector3((k & 1) == 0 ? mn.x : mx.x, (k & 2) == 0 ? mn.y : mx.y, (k & 4) == 0 ? mn.z : mx.z);
                    p = m.MultiplyPoint3x4(p);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return any;
        }

        private GameObject FirstPrefab()
        {
            if (Variants == null) return null;
            for (int i = 0; i < Variants.Count; i++)
                if (Variants[i] != null && Variants[i].Prefab != null) return Variants[i].Prefab;
            return null;
        }

        /// <summary>Размер плитки по X/Z, м (без зазора).</summary>
        public Vector2 TileSize()
        {
            if (!AutoSize) return new Vector2(Mathf.Max(0.01f, ManualSize.x), Mathf.Max(0.01f, ManualSize.y));
            Bounds b;
            if (MeasurePrefab(FirstPrefab(), out b) && b.size.x > 0.001f && b.size.z > 0.001f)
                return new Vector2(b.size.x, b.size.z);
            return new Vector2(Mathf.Max(0.01f, ManualSize.x), Mathf.Max(0.01f, ManualSize.y));
        }

        public Vector2 CellSize()
        {
            Vector2 t = TileSize();
            return new Vector2(Mathf.Max(0.01f, t.x + Gap), Mathf.Max(0.01f, t.y + Gap));
        }

        /// <summary>Центр клетки в осях генератора.</summary>
        public Vector3 CellLocal(int x, int z)
        {
            Vector2 c = CellSize();
            if (CenterPivot) return new Vector3((x - (Width - 1) * 0.5f) * c.x, 0f, (z - (Depth - 1) * 0.5f) * c.y);
            return new Vector3((x + 0.5f) * c.x, 0f, (z + 0.5f) * c.y);
        }

        // ───────────────────────── ГЕНЕРАЦИЯ ─────────────────────────

        public Transform Container()
        {
            Transform t = transform.Find(ContainerName);
            return t;
        }

        /// <summary>Удалить сгенерированное.</summary>
        public void Clear()
        {
            Transform c = Container();
            if (c == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { Undo.DestroyObjectImmediate(c.gameObject); return; }
#endif
            Destroy(c.gameObject);
        }

        /// <summary>Разложить плитки заново. Возвращает, сколько поставлено.</summary>
        public int Generate()
        {
            ForgetTexture();
            float total = 0f;
            List<TileVariant> vs = new List<TileVariant>();
            if (Variants != null)
                for (int i = 0; i < Variants.Count; i++)
                    if (Variants[i] != null && Variants[i].Prefab != null && Variants[i].Weight > 0f) { vs.Add(Variants[i]); total += Variants[i].Weight; }
            if (vs.Count == 0)
            {
                if (Log) Debug.LogWarning($"[Сетка плиток] «{name}»: нет ни одной модели в Variants — нечего раскладывать", this);
                return 0;
            }
            int cells = Width * Depth;
            if (cells > 40000)
            {
                if (Log) Debug.LogWarning($"[Сетка плиток] «{name}»: {Width}×{Depth} = {cells} клеток — слишком много (максимум 40 000)", this);
                return 0;
            }

            Clear();
            GameObject cgo = new GameObject(ContainerName);
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(cgo, "Сетка плиток");
#endif
            Transform cont = cgo.transform;
            cont.SetParent(transform, false);
            cgo.layer = gameObject.layer;
            cgo.isStatic = MarkStatic;

            // габарит каждого варианта — один раз
            Bounds[] bounds = new Bounds[vs.Count];
            for (int i = 0; i < vs.Count; i++)
            {
                Bounds b;
                if (!MeasurePrefab(vs[i].Prefab, out b)) b = new Bounds(Vector3.zero, Vector3.zero);
                bounds[i] = b;
            }
            Vector2 tile = TileSize();
            bool square = Mathf.Abs(tile.x - tile.y) <= 0.02f * Mathf.Max(tile.x, tile.y);

            System.Random rnd = new System.Random(Seed);
            int placed = 0;
            for (int z = 0; z < Depth; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    // случайные числа тянем для КАЖДОЙ клетки — узор не «едет» при смене формы
                    double pick = rnd.NextDouble() * total;
                    int rotStep = rnd.Next(4);
                    if (!CellOn(x, z)) continue;
                    int vi = 0;
                    double acc = 0;
                    for (int i = 0; i < vs.Count; i++) { acc += vs[i].Weight; if (pick <= acc) { vi = i; break; } vi = i; }
                    float yaw = 0f;
                    if (RandomRotate) yaw = square ? rotStep * 90f : (rotStep % 2) * 180f;
                    Place(vs[vi].Prefab, bounds[vi], cont, CellLocal(x, z), yaw, x, z);
                    placed++;
                }
            }
            if (Log) Debug.Log($"[Сетка плиток] «{name}»: форма {Shape}{(Invert ? " (наоборот)" : "")}, {Width}×{Depth}, поставлено {placed} плиток · клетка {CellSize().x:0.###}×{CellSize().y:0.###} м · вариантов {vs.Count}", this);
            return placed;
        }

        private void Place(GameObject prefab, Bounds b, Transform cont, Vector3 cell, float yaw, int x, int z)
        {
            GameObject go = null;
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(prefab))
                go = PrefabUtility.InstantiatePrefab(prefab, cont) as GameObject;
#endif
            if (go == null) go = Instantiate(prefab, cont);
            go.name = $"Tile_{x}_{z}";
            Transform t = go.transform;
            Quaternion spin = Quaternion.Euler(0f, yaw, 0f);
            Vector3 off = new Vector3(b.center.x, SnapToGround ? b.min.y : 0f, b.center.z);
            t.localRotation = spin * prefab.transform.localRotation;
            t.localScale = prefab.transform.localScale;
            t.localPosition = cell - spin * off;
            if (MarkStatic) SetStatic(go);
        }

        private static void SetStatic(GameObject go)
        {
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.isStatic = true;
        }

        // ───────────────────────── ОБЪЕДИНЕНИЕ ─────────────────────────

        /// <summary>
        /// Все плитки → один меш на материал (дочерние «Combined_…»), плитки удаляются. В редакторе меши сохраняются
        /// ассетами в Mesh Folder (иначе при сохранении префаба меш потеряется). Возвращает число мешей.
        /// </summary>
        public int Combine()
        {
            Transform cont = Container();
            if (cont == null) { if (Log) Debug.LogWarning($"[Сетка плиток] «{name}»: сначала «Сгенерировать»", this); return 0; }
            Dictionary<Material, List<CombineInstance>> byMat = new Dictionary<Material, List<CombineInstance>>();
            List<Material> order = new List<Material>();
            Matrix4x4 toCont = cont.worldToLocalMatrix;
            MeshFilter[] mfs = cont.GetComponentsInChildren<MeshFilter>(false);
            int verts = 0;
            for (int i = 0; i < mfs.Length; i++)
            {
                MeshFilter mf = mfs[i];
                if (mf == null || mf.sharedMesh == null || mf.name.StartsWith("Combined_")) continue;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                Material[] mats = mr.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    Material mat = s < mats.Length ? mats[s] : (mats.Length > 0 ? mats[mats.Length - 1] : null);
                    if (mat == null) continue;
                    List<CombineInstance> list;
                    if (!byMat.TryGetValue(mat, out list)) { list = new List<CombineInstance>(); byMat[mat] = list; order.Add(mat); }
                    CombineInstance ci = new CombineInstance();
                    ci.mesh = mf.sharedMesh; ci.subMeshIndex = s; ci.transform = toCont * mf.transform.localToWorldMatrix;
                    list.Add(ci);
                }
                verts += mf.sharedMesh.vertexCount;
            }
            if (order.Count == 0) { if (Log) Debug.LogWarning($"[Сетка плиток] «{name}»: нечего объединять", this); return 0; }

            // плитки (с их коллайдерами) убираем, оставляем объединённое
            List<GameObject> kill = new List<GameObject>();
            for (int i = 0; i < cont.childCount; i++) kill.Add(cont.GetChild(i).gameObject);

            int made = 0;
            for (int k = 0; k < order.Count; k++)
            {
                Material mat = order[k];
                Mesh mesh = new Mesh();
                mesh.name = $"TileGrid_{name}_{mat.name}";
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(byMat[mat].ToArray(), true, true);
                mesh.RecalculateBounds();
#if UNITY_EDITOR
                if (!Application.isPlaying) mesh = SaveMeshAsset(mesh);
#endif
                GameObject go = new GameObject("Combined_" + mat.name);
#if UNITY_EDITOR
                if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(go, "Сетка плиток");
#endif
                go.layer = gameObject.layer;
                go.transform.SetParent(cont, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                if (CombinedCollider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (MarkStatic) go.isStatic = true;
                made++;
            }
            for (int i = 0; i < kill.Count; i++)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) { Undo.DestroyObjectImmediate(kill[i]); continue; }
#endif
                Destroy(kill[i]);
            }
            if (Log) Debug.Log($"[Сетка плиток] «{name}»: объединено {mfs.Length} мешей ({verts} вершин) → {made} (по материалам)" +
                               (CombinedCollider ? " + MeshCollider" : ""), this);
            return made;
        }

#if UNITY_EDITOR
        private Mesh SaveMeshAsset(Mesh mesh)
        {
            string folder = string.IsNullOrEmpty(MeshFolder) ? "Assets/Generated/TileGrids" : MeshFolder.TrimEnd('/');
            if (!folder.StartsWith("Assets")) folder = "Assets/Generated/TileGrids";
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
            string scene = gameObject.scene.IsValid() && !string.IsNullOrEmpty(gameObject.scene.name) ? gameObject.scene.name + "_" : "";
            string file = Safe(scene + mesh.name) + ".asset";
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + file);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            if (Log) Debug.Log($"[Сетка плиток] меш сохранён: {path}", this);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private static string Safe(string s)
        {
            char[] bad = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                bool ok = ch != ' ';
                for (int k = 0; k < bad.Length && ok; k++) if (bad[k] == ch) ok = false;
                sb.Append(ok ? ch : '_');
            }
            return sb.ToString();
        }
#endif

        // ───────────────────────── ГИЗМО ─────────────────────────

        private void OnValidate()
        {
            if (Width < 1) Width = 1;
            if (Depth < 1) Depth = 1;
            ForgetTexture();
        }

        private void OnDrawGizmosSelected()
        {
            if (Width * Depth > 6000) return;
            Vector2 t = TileSize();
            Gizmos.matrix = transform.localToWorldMatrix;
            Vector3 size = new Vector3(t.x * 0.96f, 0.02f, t.y * 0.96f);
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                {
                    bool on = CellOn(x, z);
                    Gizmos.color = on ? new Color(1f, 0.85f, 0.2f, 0.9f) : new Color(1f, 1f, 1f, 0.12f);
                    Gizmos.DrawWireCube(CellLocal(x, z), on ? size : size * 0.5f);
                }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
