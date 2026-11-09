#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Хлеб → Духовка-бочка: коллайдеры низа и решётка.
    ///
    /// Выдели НИЖНЮЮ половину бочки (меш корпуса, например Cylinder). Бочка лежит на боку, духовка стоит ровно.
    ///   • «Сделать коллайдеры низа» — дно из N плоских коробок по дуге (по умолчанию 4) + торцы по бокам.
    ///     Кладутся в ребёнка корня «BodyColliders». Старые коллайдеры меша низа выключаются (галка).
    ///   • «Сделать решётку» — меш из прутьев-арматуры (с рёбрышками) + тонкий Box Collider, ребёнок корня «Grill».
    ///     Меш сохраняется в Assets/Models/Bread/OvenGrill.asset, материал Mat_Rebar.
    /// Повторный запуск перестраивает то же самое (старое удаляется). Всё с Undo. Префабы не создаются.
    /// </summary>
    public sealed class CatCooksOvenBarrelBuilder : EditorWindow
    {
        const string ModelDir = "Assets/Models/Bread";
        const string BodyName = "BodyColliders";
        const string GrillName = "Grill";
        static readonly string[] AxisNames = { "Авто", "Вдоль красной оси корня (X)", "Вдоль синей оси корня (Z)" };

        [SerializeField] GameObject _body;
        [SerializeField] int _axis;
        [SerializeField] int _faces = 4;
        [SerializeField] float _wall = 0.03f;
        [SerializeField] bool _caps = true;
        [SerializeField] bool _disableOld = true;
        [SerializeField] float _grillHeight = 0.45f;
        [SerializeField] float _spacing = 0.045f;
        [SerializeField] float _barD = 0.012f;
        [SerializeField] float _margin = 0.015f;

        [MenuItem("CatCooks/Хлеб/Духовка-бочка: коллайдеры низа и решётка", priority = 92)]
        static void Open()
        {
            var w = GetWindow<CatCooksOvenBarrelBuilder>(true, "Бочка-духовка");
            w.minSize = new Vector2(380f, 360f);
            if (Selection.activeGameObject != null) w._body = Selection.activeGameObject;
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Выдели НИЖНЮЮ половину бочки (меш корпуса, например Cylinder) и перетащи сюда. " +
                                    "Духовка должна стоять ровно. Всё отменяется Ctrl+Z.", MessageType.Info);
            _body = (GameObject)EditorGUILayout.ObjectField("Низ бочки (меш)", _body, typeof(GameObject), true);
            _axis = EditorGUILayout.Popup("Ось бочки", _axis, AxisNames);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Коллайдеры низа", EditorStyles.boldLabel);
            _faces = EditorGUILayout.IntSlider("Граней на дне", _faces, 2, 8);
            _wall = Mathf.Max(0.005f, EditorGUILayout.FloatField("Толщина стенки, м", _wall));
            _caps = EditorGUILayout.Toggle("Торцы (по бокам)", _caps);
            _disableOld = EditorGUILayout.Toggle("Выключить старые коллайдеры меша", _disableOld);
            if (GUILayout.Button("Сделать коллайдеры низа", GUILayout.Height(26))) Run(false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Решётка-арматура", EditorStyles.boldLabel);
            _grillHeight = EditorGUILayout.Slider("Высота от дна (доля радиуса)", _grillHeight, 0.15f, 0.95f);
            _spacing = Mathf.Max(0.015f, EditorGUILayout.FloatField("Шаг прутьев, м", _spacing));
            _barD = Mathf.Clamp(EditorGUILayout.FloatField("Толщина прута, м", _barD), 0.004f, 0.05f);
            _margin = Mathf.Max(0f, EditorGUILayout.FloatField("Зазор до стенки, м", _margin));
            if (GUILayout.Button("Сделать решётку", GUILayout.Height(26))) Run(true);
        }

        // ───────────────────────── запуск ─────────────────────────

        void Run(bool grill)
        {
            var log = new StringBuilder();
            if (_body == null) { Msg("Перетащи в поле нижнюю половину бочки (меш корпуса)."); return; }
            if (_body.GetComponent<BakeOven>() != null || _body.transform.Find("Chamber") != null || _body.transform.Find("LidHinge") != null)
            { Msg("Выделен КОРЕНЬ духовки. Нужен именно меш нижней половины (например Cylinder) — иначе замерится вся бочка с крышкой."); return; }

            Transform root = FindRoot(_body.transform);
            if (!Measure(_body, root, _axis, out Quaternion F, out Vector3 C, out float R, out float L, log)) { Done(root, log, false); return; }

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(grill ? "Решётка духовки" : "Коллайдеры низа духовки");
            if (grill) BuildGrill(root, F, C, R, L, log);
            else BuildBody(root, F, C, R, L, log);
            Undo.CollapseUndoOperations(group);
            Done(root, log, true);
        }

        static Transform FindRoot(Transform body)
        {
            BakeOven o = body.GetComponentInParent<BakeOven>();
            if (o != null) return o.transform;
            return body.parent != null ? body.parent : body;
        }

        // ───────────────────────── замер бочки ─────────────────────────

        /// <summary>
        /// Рамка F (мировые единицы): Y — вверх (мир), Z — ось бочки, X — поперёк. Начало — позиция корня.
        /// C — центр круга на оси (на уровне разреза), R — радиус, L — длина.
        /// </summary>
        static bool Measure(GameObject body, Transform root, int axisMode, out Quaternion F, out Vector3 C, out float R, out float L, StringBuilder log)
        {
            F = Quaternion.identity; C = Vector3.zero; R = 0f; L = 0f;
            List<Vector3> pts = Points(body);
            if (pts.Count == 0) { log.AppendLine("✖ у выделенного нет мешей (MeshFilter)"); return false; }

            Vector3 up = Vector3.up;
            Vector3 fH = Vector3.ProjectOnPlane(root.forward, up);
            if (fH.sqrMagnitude < 1e-4f) fH = Vector3.ProjectOnPlane(root.up, up);   // модель из Blender «лежит» на −90°
            if (fH.sqrMagnitude < 1e-4f) fH = Vector3.forward;
            Quaternion B = Quaternion.LookRotation(fH.normalized, up);

            Bounds bb = BoundsIn(pts, B, root.position);
            float ex = bb.size.x, ez = bb.size.z, h = bb.size.y;
            bool axisX;
            if (axisMode == 1) axisX = true;
            else if (axisMode == 2) axisX = false;
            else axisX = CrossErr(ex, h) > CrossErr(ez, h);
            F = axisX ? B * Quaternion.Euler(0f, 90f, 0f) : B;

            bb = BoundsIn(pts, F, root.position);
            R = bb.size.x * 0.5f;
            L = bb.size.z;
            C = new Vector3(bb.center.x, bb.min.y + R, bb.center.z);
            if (R < 0.01f || L < 0.01f) { log.AppendLine($"✖ странный размер: радиус {R:0.000} м, длина {L:0.000} м"); return false; }

            float hr = bb.size.y / R;
            log.AppendLine($"Корень «{root.name}», низ «{body.name}»: радиус {R:0.000} м, длина {L:0.000} м, " +
                           $"ось — {(axisMode == 0 ? "авто, " : "")}{(axisX ? "красная (X)" : "синяя (Z)")} корня");
            if (hr > 1.5f) log.AppendLine("ℹ меш похож на ЦЕЛУЮ бочку (высота ≈ 2 радиуса) — делаю только низ, от середины");
            else if (hr < 0.6f) log.AppendLine("⚠ меш ниже половины круга — проверь, что выделен весь низ бочки");
            if (Mathf.Abs(R * 2f - L) / Mathf.Max(R, 1e-3f) < 0.15f && axisMode == 0)
                log.AppendLine("⚠ длина почти равна диаметру — ось могла определиться неверно; если коллайдеры легли поперёк, выбери ось руками");
            return true;
        }

        static float CrossErr(float e, float h)
            => Mathf.Min(Mathf.Abs(e - 2f * h), Mathf.Abs(e - h)) / Mathf.Max(h, 1e-4f);

        static List<Vector3> Points(GameObject body)
        {
            var pts = new List<Vector3>();
            foreach (MeshFilter mf in body.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || IsOurs(mf.transform)) continue;
                Mesh m = mf.sharedMesh;
                Vector3[] vs = null;
                try { if (m.isReadable) vs = m.vertices; } catch { vs = null; }
                if (vs != null && vs.Length > 0)
                {
                    int step = Mathf.Max(1, vs.Length / 6000);
                    for (int i = 0; i < vs.Length; i += step) pts.Add(mf.transform.TransformPoint(vs[i]));
                }
                else
                {
                    Bounds b = m.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                        pts.Add(mf.transform.TransformPoint(c));
                    }
                }
            }
            return pts;
        }

        static Bounds BoundsIn(List<Vector3> world, Quaternion frame, Vector3 origin)
        {
            Quaternion inv = Quaternion.Inverse(frame);
            Bounds b = new Bounds(inv * (world[0] - origin), Vector3.zero);
            for (int i = 1; i < world.Count; i++) b.Encapsulate(inv * (world[i] - origin));
            return b;
        }

        static bool IsOurs(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.name == BodyName || p.name == GrillName) return true;
            return false;
        }

        /// <summary>Ребёнок корня с поворотом рамки F и мировым масштабом 1 — внутри всё в метрах.</summary>
        static Transform MakeHolder(Transform root, string name, Quaternion F, StringBuilder log)
        {
            var go = new GameObject(name);
            Transform t = go.transform;
            t.SetParent(root, false);
            t.SetPositionAndRotation(root.position, F);
            Vector3 ls = root.lossyScale;
            float mx = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)), mn = Mathf.Min(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            if (mn < 1e-6f) mn = 1f;
            if (mx / mn > 1.01f)
            {
                t.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
                log.AppendLine($"⚠ у корня неравный масштаб {ls} — коробки могут слегка перекоситься; лучше масштаб корня (1,1,1) или одинаковый");
            }
            else t.localScale = Vector3.one / ls.x;
            return t;
        }

        static void RemoveOld(Transform root, string name)
        {
            Transform old = root.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        // ───────────────────────── коллайдеры низа ─────────────────────────

        void BuildBody(Transform root, Quaternion F, Vector3 C, float R, float L, StringBuilder log)
        {
            if (_disableOld)
            {
                int off = 0;
                foreach (Collider c in _body.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger || !c.enabled || IsOurs(c.transform)) continue;
                    Undo.RecordObject(c, "выключить старый коллайдер");
                    c.enabled = false;
                    off++;
                    log.AppendLine($"－ выключен старый {c.GetType().Name} на «{c.name}»");
                }
                if (off == 0) log.AppendLine("ℹ старых коллайдеров на меше низа не было");
            }

            RemoveOld(root, BodyName);
            Transform h = MakeHolder(root, BodyName, F, log);

            int n = _faces;
            float t = _wall;
            float dA = Mathf.PI / n;
            float chord = 2f * R * Mathf.Sin(dA * 0.5f);
            float dist = R * Mathf.Cos(dA * 0.5f) - t * 0.5f;     // наружная сторона коробки — по хорде круга
            for (int i = 0; i < n; i++)
            {
                float th = Mathf.PI + (i + 0.5f) * dA;            // нижняя половина: 180°…360°
                Vector3 d = new Vector3(Mathf.Cos(th), Mathf.Sin(th), 0f);
                Box(h, $"Floor_{i + 1}", C + d * dist, Quaternion.LookRotation(Vector3.forward, -d), new Vector3(chord + t, t, L));
            }
            log.AppendLine($"＋ дно: {n} плоскост{(n < 5 ? "и" : "ей")} по дуге, толщина {t:0.000} м");

            if (_caps)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = C.z + s * (L * 0.5f - t * 0.5f);
                    string side = s < 0 ? "A" : "B";
                    // полукруг торца — две ступеньки: широкая сверху, поуже снизу (ровное дно — бочка не катается)
                    Box(h, $"Cap{side}_Top", new Vector3(C.x, C.y - R * 0.25f, z), Quaternion.identity, new Vector3(2f * R, R * 0.5f, t));
                    Box(h, $"Cap{side}_Low", new Vector3(C.x, C.y - R * 0.75f, z), Quaternion.identity, new Vector3(2f * R * 0.866f, R * 0.5f, t));
                }
                log.AppendLine("＋ торцы по бокам (по 2 коробки)");
            }
            Undo.RegisterCreatedObjectUndo(h.gameObject, BodyName);
            Selection.activeGameObject = h.gameObject;
        }

        static void Box(Transform parent, string name, Vector3 localPos, Quaternion localRot, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.AddComponent<BoxCollider>().size = size;
        }

        // ───────────────────────── решётка ─────────────────────────

        void BuildGrill(Transform root, Quaternion F, Vector3 C, float R, float L, StringBuilder log)
        {
            float r = _barD * 0.5f;
            float yTop = C.y - R + _grillHeight * R;               // верх решётки
            float dy = yTop - C.y;
            float hw = Mathf.Sqrt(Mathf.Max(0f, R * R - dy * dy)) - _wall - _margin;
            float len = L - 2f * _wall - 2f * _margin;
            if (hw < 0.03f || len < 0.05f) { log.AppendLine($"✖ решётка не помещается: ширина {hw * 2f:0.000} м, длина {len:0.000} м — подними выше или уменьши зазоры"); return; }

            // меш: старый ассет переиспользуем, чтобы не плодить файлы
            Mesh mesh = null;
            Transform old = root.Find(GrillName);
            if (old != null)
            {
                MeshFilter omf = old.GetComponent<MeshFilter>();
                if (omf != null && omf.sharedMesh != null && AssetDatabase.Contains(omf.sharedMesh)) mesh = omf.sharedMesh;
            }
            RemoveOld(root, GrillName);

            Transform g = MakeHolder(root, GrillName, F, log);
            g.position = root.position + F * new Vector3(C.x, yTop - r, C.z);   // центр поперечных прутьев

            var mb = new MeshBits();
            int cross = Mathf.Max(2, Mathf.FloorToInt(len / _spacing) + 1);
            for (int i = 0; i < cross; i++)
            {
                float z = -len * 0.5f + i * len / (cross - 1);
                mb.Bar(new Vector3(-hw, 0f, z), new Vector3(hw, 0f, z), r);
            }
            float yRail = -_barD * 0.95f;                          // продольные прутья — под поперечными
            float rail = hw - r * 1.5f;
            mb.Bar(new Vector3(-rail, yRail, -len * 0.5f - r), new Vector3(-rail, yRail, len * 0.5f + r), r * 1.15f);
            mb.Bar(new Vector3(rail, yRail, -len * 0.5f - r), new Vector3(rail, yRail, len * 0.5f + r), r * 1.15f);
            mb.Bar(new Vector3(0f, yRail, -len * 0.5f - r), new Vector3(0f, yRail, len * 0.5f + r), r * 1.15f);

            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            mb.Fill(mesh);
            mesh.name = "OvenGrill";
            string path;
            if (isNew)
            {
                EnsureDir(ModelDir);
                path = AssetDatabase.GenerateUniqueAssetPath($"{ModelDir}/OvenGrill.asset");
                AssetDatabase.CreateAsset(mesh, path);
            }
            else { path = AssetDatabase.GetAssetPath(mesh); EditorUtility.SetDirty(mesh); }
            AssetDatabase.SaveAssets();

            g.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.gameObject.AddComponent<MeshRenderer>().sharedMaterial = RebarMat(log);
            BoxCollider bc = g.gameObject.AddComponent<BoxCollider>();
            float top = r * 1.18f;
            bc.center = new Vector3(0f, top - 0.01f, 0f);         // верх коробки = верх прутьев
            bc.size = new Vector3(hw * 2f, 0.02f, len);

            Undo.RegisterCreatedObjectUndo(g.gameObject, GrillName);
            Selection.activeGameObject = g.gameObject;
            log.AppendLine($"＋ решётка {hw * 2f:0.00}×{len:0.00} м на высоте {_grillHeight * R:0.00} м от дна: {cross} поперечных прутьев Ø{_barD * 1000f:0} мм + 3 продольных, " +
                           $"{mesh.vertexCount} вершин; меш «{path}», коллайдер — тонкий Box Collider");
            log.AppendLine("ℹ решётка должна быть внутри Chamber (триггер полости) — иначе на ней ничего не печётся");
        }

        static Material RebarMat(StringBuilder log)
        {
            string path = $"{ModelDir}/Mat_Rebar.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = "Mat_Rebar" };
            Color c = new Color(0.36f, 0.29f, 0.25f);             // тёмная сталь с ржавчиной
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.7f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.35f);
            EnsureDir(ModelDir);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"＋ материал {path}");
            return m;
        }

        /// <summary>Сборка мешa из прутьев: шестигранный цилиндр с рёбрышками (как у арматуры) и заглушками.</summary>
        sealed class MeshBits
        {
            const int Sides = 6;
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int> _t = new List<int>();

            public void Bar(Vector3 a, Vector3 b, float r)
            {
                Vector3 dir = b - a;
                float len = dir.magnitude;
                if (len < 1e-4f) return;
                dir /= len;
                Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 w = Vector3.Cross(dir, u);
                float step = Mathf.Max(r * 3.5f, 0.012f);
                int ribs = Mathf.Max(1, Mathf.RoundToInt(len / step));
                int rings = ribs * 2 + 1;
                int baseIdx = _v.Count;
                for (int k = 0; k < rings; k++)
                {
                    float f = k / (float)(rings - 1);
                    float rad = (k & 1) == 1 ? r * 1.18f : r;           // рёбрышко через кольцо
                    Vector3 c = a + dir * (len * f);
                    for (int s = 0; s <= Sides; s++)
                    {
                        float ang = s / (float)Sides * Mathf.PI * 2f;
                        Vector3 nr = Mathf.Cos(ang) * u + Mathf.Sin(ang) * w;
                        _v.Add(c + nr * rad); _n.Add(nr); _uv.Add(new Vector2(s / (float)Sides, f * len * 10f));
                    }
                }
                int row = Sides + 1;
                for (int k = 0; k < rings - 1; k++)
                    for (int s = 0; s < Sides; s++)
                    {
                        int i0 = baseIdx + k * row + s, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                        _t.Add(i0); _t.Add(i1); _t.Add(i2);
                        _t.Add(i1); _t.Add(i3); _t.Add(i2);
                    }
                Cap(a, -dir, u, w, r, false);
                Cap(b, dir, u, w, r, true);
            }

            void Cap(Vector3 c, Vector3 nrm, Vector3 u, Vector3 w, float r, bool end)
            {
                int ci = _v.Count;
                _v.Add(c); _n.Add(nrm); _uv.Add(new Vector2(0.5f, 0.5f));
                for (int s = 0; s < Sides; s++)
                {
                    float ang = s / (float)Sides * Mathf.PI * 2f;
                    Vector3 d = Mathf.Cos(ang) * u + Mathf.Sin(ang) * w;
                    _v.Add(c + d * r); _n.Add(nrm); _uv.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
                }
                for (int s = 0; s < Sides; s++)
                {
                    int a = ci + 1 + s, b = ci + 1 + (s + 1) % Sides;
                    if (end) { _t.Add(ci); _t.Add(a); _t.Add(b); }
                    else { _t.Add(ci); _t.Add(b); _t.Add(a); }
                }
            }

            public void Fill(Mesh m)
            {
                m.Clear();
                m.indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.SetUVs(0, _uv);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
            }
        }

        // ───────────────────────── помощники ─────────────────────────

        static void EnsureDir(string dir)
        {
            string[] parts = dir.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string nx = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(nx)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = nx;
            }
        }

        static void Msg(string t) => EditorUtility.DisplayDialog("Бочка-духовка", t, "Ок");

        static void Done(Transform root, StringBuilder log, bool ok)
        {
            if (ok && root != null)
            {
                EditorUtility.SetDirty(root.gameObject);
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                log.AppendLine("\nСохрани духовку (если префаб — Overrides → Apply All).");
            }
            Debug.Log("[Бочка-духовка]\n" + log, root);
            EditorUtility.DisplayDialog("Бочка-духовка", log.ToString(), "Ок");
        }
    }
}
#endif
