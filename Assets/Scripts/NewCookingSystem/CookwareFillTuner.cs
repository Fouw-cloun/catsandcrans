using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// НАСТРОЙКА ГОРКИ И ЖИДКОСТИ ТАРЫ — руками, с видом в сцене. Сковорода, казан, любая круглая тара,
    /// у которой дно уже края (трапеция сбоку).
    ///
    /// Вешается на корень тары (меню «Борщ → 1» ставит само и заполняет по замеру). Двигаешь поля —
    /// сразу меняются якоря:
    ///   HeapAnchor   — Food Heap Renderer (горка): центр, радиус у дна, у края, высота, «с горкой» над краем;
    ///   LiquidAnchor — Cookware Liquid Visual (жидкость): центр, радиус у дна, у края, высота.
    /// В сцене (выдели тару): ОРАНЖЕВАЯ трапеция — горка, ГОЛУБАЯ — жидкость, полупрозрачная голубая заливка —
    /// уровень жидкости из поля «Превью: уровень». Горка видна сама (жёлтое превью Food Heap Renderer) —
    /// её размер — поле «Превью: порций».
    /// «Жидкость как горка» — жидкость повторяет горку (двигаешь одно).
    ///
    /// В игре ничего не делает: только редактор. Порции жидкости пересчитываются под вместимость Cookware.
    /// ⋮ → «Снять с якорей» — прочитать то, что сейчас стоит у якорей (если правил их руками).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CookwareFillTuner : MonoBehaviour
    {
        [Header("Горка (HeapAnchor)")]
        [Tooltip("Центр дна горки, в осях тары (м). Двигай, пока оранжевый круг не ляжет на дно")]
        [SerializeField] private Vector3 _heapCenter;
        [Tooltip("Радиус горки у дна, м (мир)")]
        [Min(0.01f)][SerializeField] private float _heapBottomR = 0.3f;
        [Tooltip("Радиус горки у края тары, м (мир). Больше дна — трапеция")]
        [Min(0.01f)][SerializeField] private float _heapTopR = 0.4f;
        [Tooltip("Высота до края, м (мир) — до неё горка заполняет ровно")]
        [Min(0.01f)][SerializeField] private float _heapHeight = 0.2f;
        [Tooltip("Насколько горка может подняться куполом над краем (доля высоты)")]
        [Range(0f, 1.5f)][SerializeField] private float _heapAboveRim = 0.4f;
        [Tooltip("Превью горки в редакторе: сколько порций показать")]
        [Range(0f, 40f)][SerializeField] private float _previewPortions = 8f;

        [Header("Жидкость (LiquidAnchor)")]
        [Tooltip("Жидкость повторяет горку (центр, радиусы, высота)")]
        [SerializeField] private bool _liquidLikeHeap = true;
        [SerializeField] private Vector3 _liquidCenter;
        [Min(0.01f)][SerializeField] private float _liquidBottomR = 0.3f;
        [Min(0.01f)][SerializeField] private float _liquidTopR = 0.4f;
        [Min(0.02f)][SerializeField] private float _liquidHeight = 0.2f;
        [Tooltip("Превью: уровень жидкости в сцене (голубая заливка), доля высоты")]
        [Range(0f, 1f)][SerializeField] private float _previewLevel = 0.5f;
        [Tooltip("Суп: кусочки в жидкости поднимают её и красят (кастрюля). Сковорода — ВЫКЛ: масло отдельно, овощи — горкой")]
        [SerializeField] private bool _soup = false;

        [Header("Вид в сцене")]
        [SerializeField] private bool _gizmos = true;

        public Vector3 HeapCenterWorld => transform.TransformPoint(_heapCenter);
        /// <summary>Горка: радиус у дна, у края и высота до края, м (мир).</summary>
        public float HeapBottomR => _heapBottomR;
        public float HeapTopR => _heapTopR;
        public float HeapHeight => _heapHeight;
        public Vector3 LiquidCenterWorld => transform.TransformPoint(_liquidLikeHeap ? _heapCenter : _liquidCenter);

#if UNITY_EDITOR
        /// <summary>Из меню: выставить по замеру и применить.</summary>
        public void EditorSet(Vector3 bottomWorld, float bottomR, float topR, float height)
        {
            _heapCenter = transform.InverseTransformPoint(bottomWorld);
            _heapBottomR = bottomR * 0.96f; _heapTopR = topR * 0.96f; _heapHeight = height * 0.9f;
            _liquidCenter = _heapCenter;
            _liquidBottomR = bottomR * 0.97f; _liquidTopR = topR * 0.97f; _liquidHeight = height * 0.92f;
            _liquidLikeHeap = false;
            UnityEditor.EditorUtility.SetDirty(this);
            EditorApply();
        }

        private void OnValidate()
        {
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall -= DelayedApply;
            UnityEditor.EditorApplication.delayCall += DelayedApply;
        }

        private void DelayedApply()
        {
            if (this == null || Application.isPlaying) return;
            if (UnityEditor.EditorUtility.IsPersistent(this)) return;          // префаб-ассет в Project — только в сцене / режиме префаба
            EditorApply();
        }

        public void EditorApply()
        {
            Vector3 lc; float lb, lt, lh;
            if (_liquidLikeHeap) { lc = _heapCenter; lb = _heapBottomR; lt = _heapTopR; lh = _heapHeight; }
            else { lc = _liquidCenter; lb = _liquidBottomR; lt = _liquidTopR; lh = _liquidHeight; }

            // ── горка ──
            Transform heapT = Child("HeapAnchor");
            heapT.SetPositionAndRotation(transform.TransformPoint(_heapCenter), Quaternion.identity);
            FoodHeapRenderer fr = heapT.GetComponent<FoodHeapRenderer>();
            if (fr == null) fr = heapT.gameObject.AddComponent<FoodHeapRenderer>();
            if (heapT.GetComponent<CookwareHeapVisual>() == null) heapT.gameObject.AddComponent<CookwareHeapVisual>();
            var hso = new UnityEditor.SerializedObject(fr);
            Int(hso, "_shape", (int)FoodHeapRenderer.Shape.Fill);
            Bool(hso, "_fillRect", false);
            Bool(hso, "_worldUpright", true);
            Bool(hso, "_ignoreParentScale", true);
            Bool(hso, "_preview", true);
            V2(hso, "_footprint", new Vector2(_heapBottomR, _heapBottomR));
            F(hso, "_maxHeight", _heapHeight);
            F(hso, "_flareTop", Mathf.Clamp(_heapTopR / Mathf.Max(0.01f, _heapBottomR), 0.5f, 2.5f));
            F(hso, "_aboveRim", _heapAboveRim);
            F(hso, "_previewPortions", _previewPortions);
            hso.ApplyModifiedProperties();
            Poke(fr);

            // ── жидкость ──
            Transform liqT = Child("LiquidAnchor");
            liqT.SetPositionAndRotation(transform.TransformPoint(lc), Quaternion.identity);
            CookwareLiquidVisual lv = liqT.GetComponent<CookwareLiquidVisual>();
            if (lv == null) lv = liqT.gameObject.AddComponent<CookwareLiquidVisual>();
            var lso = new UnityEditor.SerializedObject(lv);
            Int(lso, "_form", 0);
            F(lso, "_radiusBottom", lb);
            F(lso, "_radiusTop", lt);
            F(lso, "_maxHeight", lh);
            V2(lso, "_halfSize", new Vector2(lt, lt));
            Bool(lso, "_ignoreParentScale", true);
            Bool(lso, "_soup", _soup);
            UnityEditor.SerializedProperty wp = lso.FindProperty("_wallProfile"); if (wp != null) wp.arraySize = 0;   // ровная трапеция по полям выше
            UnityEditor.SerializedProperty bp = lso.FindProperty("_boxProfile"); if (bp != null) bp.arraySize = 0;
            Cookware cw = GetComponent<Cookware>();
            int cap = cw != null && cw.Def != null ? Mathf.Max(1, cw.Def.Capacity) : 20;
            F(lso, "_portionVolume", Mathf.PI * (lb * lb + lb * lt + lt * lt) / 3f * lh / cap);
            lso.ApplyModifiedProperties();
            Poke(lv);

            UnityEditor.EditorUtility.SetDirty(gameObject);
        }

        /// <summary>
        /// Радиусы по НАСТОЯЩИМ стенкам модели: у дна и у края (жидкость «Подогнать под стенки модели» — медиана по секторам,
        /// ручка не мешает). Горка — чуть уже (×0.97), чтобы не торчала сквозь стенку. Высота и центр — как стоят.
        /// </summary>
        [ContextMenu("Подогнать радиусы под стенки модели (дно и край)")]
        public void EditorFitWalls()
        {
            EditorApply();                                         // якоря на месте, высота выставлена
            Transform liqT = transform.Find("LiquidAnchor");
            CookwareLiquidVisual lv = liqT != null ? liqT.GetComponent<CookwareLiquidVisual>() : null;
            if (lv == null) return;
            var so0 = new UnityEditor.SerializedObject(lv);
            float lh = so0.FindProperty("_maxHeight").floatValue;
            System.Reflection.MethodInfo m = typeof(CookwareLiquidVisual).GetMethod("FitToModelWalls", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (m == null) { Debug.LogWarning("[Горка и жидкость] нет «Подогнать под стенки модели» у Cookware Liquid Visual", this); return; }
            m.Invoke(lv, null);
            var so = new UnityEditor.SerializedObject(lv);
            float rb = so.FindProperty("_radiusBottom").floatValue, rt = so.FindProperty("_radiusTop").floatValue;
            if (rb < 0.01f || rt < 0.01f) return;
            UnityEditor.Undo.RecordObject(this, "Радиусы по стенкам");
            if (_liquidLikeHeap) { _liquidCenter = _heapCenter; _liquidHeight = _heapHeight; }
            _liquidLikeHeap = false;
            _liquidBottomR = rb * 0.98f; _liquidTopR = rt * 0.98f; _liquidHeight = lh;
            _heapBottomR = rb * 0.95f; _heapTopR = rt * 0.95f;
            UnityEditor.EditorUtility.SetDirty(this);
            EditorApply();
            Debug.Log($"[Горка и жидкость] «{name}»: по стенкам модели — у дна Ø{rb * 2f:0.00} м, у края Ø{rt * 2f:0.00} м (горка ×0.95, жидкость ×0.98)", this);
        }

        [ContextMenu("Снять с якорей (прочитать, что стоит сейчас)")]
        private void ReadFromAnchors()
        {
            Transform h = transform.Find("HeapAnchor"), l = transform.Find("LiquidAnchor");
            UnityEditor.Undo.RecordObject(this, "Снять с якорей");
            if (h != null && h.GetComponent<FoodHeapRenderer>() != null)
            {
                var so = new UnityEditor.SerializedObject(h.GetComponent<FoodHeapRenderer>());
                _heapCenter = transform.InverseTransformPoint(h.position);
                _heapBottomR = Mathf.Max(0.01f, so.FindProperty("_footprint").vector2Value.x);
                UnityEditor.SerializedProperty fl = so.FindProperty("_flareTop");
                _heapTopR = _heapBottomR * (fl != null ? fl.floatValue : 1f);
                _heapHeight = so.FindProperty("_maxHeight").floatValue;
            }
            if (l != null && l.GetComponent<CookwareLiquidVisual>() != null)
            {
                var so = new UnityEditor.SerializedObject(l.GetComponent<CookwareLiquidVisual>());
                _liquidCenter = transform.InverseTransformPoint(l.position);
                _liquidBottomR = so.FindProperty("_radiusBottom").floatValue;
                _liquidTopR = so.FindProperty("_radiusTop").floatValue;
                _liquidHeight = so.FindProperty("_maxHeight").floatValue;
                _liquidLikeHeap = false;
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Найти лишние горки и жидкости (двойники)")]
        private void FindExtra()
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (FoodHeapRenderer r in GetComponentsInChildren<FoodHeapRenderer>(true))
                if (r.transform.parent != transform || r.name != "HeapAnchor") { sb.AppendLine("горка: " + Path(r.transform)); n++; }
            foreach (CookwareLiquidVisual r in GetComponentsInChildren<CookwareLiquidVisual>(true))
                if (r.transform.parent != transform || r.name != "LiquidAnchor") { sb.AppendLine("жидкость: " + Path(r.transform)); n++; }
            Debug.Log(n == 0 ? $"[Горка и жидкость] «{name}»: двойников нет" : $"[Горка и жидкость] «{name}»: лишние ({n}) — выключи или удали:\n{sb}", this);
        }

        private string Path(Transform t)
        {
            string s = t.name;
            for (Transform p = t.parent; p != null && p != transform.parent; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        private Transform Child(string n)
        {
            Transform t = transform.Find(n);
            if (t != null) return t;
            var go = new GameObject(n);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, n);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        /// <summary>Сказать компоненту «поля поменялись» — превью горки перестроится сразу.</summary>
        private static void Poke(Object o)
        {
            System.Reflection.MethodInfo m = o.GetType().GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (m != null) { try { m.Invoke(o, null); } catch (System.Exception) { } }
        }

        private static void F(UnityEditor.SerializedObject so, string n, float v) { UnityEditor.SerializedProperty p = so.FindProperty(n); if (p != null) p.floatValue = v; }
        private static void Int(UnityEditor.SerializedObject so, string n, int v) { UnityEditor.SerializedProperty p = so.FindProperty(n); if (p != null) p.intValue = v; }
        private static void Bool(UnityEditor.SerializedObject so, string n, bool v) { UnityEditor.SerializedProperty p = so.FindProperty(n); if (p != null) p.boolValue = v; }
        private static void V2(UnityEditor.SerializedObject so, string n, Vector2 v) { UnityEditor.SerializedProperty p = so.FindProperty(n); if (p != null) p.vector2Value = v; }
#endif

        // ───────────────────────── вид в сцене ─────────────────────────

        private static Mesh _frustum;

        private void OnDrawGizmosSelected()
        {
            if (!_gizmos) return;
            Frustum(HeapCenterWorld, _heapBottomR, _heapTopR, _heapHeight, new Color(1f, 0.55f, 0.1f, 0.95f));
            Vector3 lc = LiquidCenterWorld;
            float lb = _liquidLikeHeap ? _heapBottomR : _liquidBottomR, lt = _liquidLikeHeap ? _heapTopR : _liquidTopR, lh = _liquidLikeHeap ? _heapHeight : _liquidHeight;
            Frustum(lc + Vector3.up * 0.002f, lb, lt, lh, new Color(0.3f, 0.8f, 1f, 0.95f));
            // уровень жидкости — полупрозрачная заливка
            if (_previewLevel > 0.01f)
            {
                if (_frustum == null) _frustum = BuildFrustum();
                float y = lh * _previewLevel, rTop = Mathf.Lerp(lb, lt, _previewLevel);
                Gizmos.color = new Color(0.3f, 0.75f, 1f, 0.35f);
                Gizmos.matrix = Matrix4x4.TRS(lc, Quaternion.identity, Vector3.one);
                // меш — единичный конус-срез: низ r=1, верх r=k; растягиваем по высоте и радиусу низа
                Matrix4x4 m = Matrix4x4.TRS(lc, Quaternion.identity, new Vector3(lb, y, lb));
                Gizmos.matrix = m;
                ScaleTop(rTop / Mathf.Max(0.001f, lb));
                Gizmos.DrawMesh(_frustum);
                Gizmos.matrix = Matrix4x4.identity;
            }
        }

        private static void Frustum(Vector3 c, float rb, float rt, float h, Color col)
        {
            Gizmos.color = col;
            Gizmos.matrix = Matrix4x4.identity;
            const int Seg = 40;
            for (int k = 0; k <= 2; k++)
            {
                float t = k / 2f, y = h * t, r = Mathf.Lerp(rb, rt, t);
                for (int i = 0; i < Seg; i++)
                {
                    float a0 = i / (float)Seg * Mathf.PI * 2f, a1 = (i + 1) / (float)Seg * Mathf.PI * 2f;
                    Gizmos.DrawLine(c + new Vector3(Mathf.Cos(a0) * r, y, Mathf.Sin(a0) * r), c + new Vector3(Mathf.Cos(a1) * r, y, Mathf.Sin(a1) * r));
                }
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Gizmos.DrawLine(c + d * rb, c + d * rt + Vector3.up * h);
            }
        }

        private static float _shownTop = -1f;

        /// <summary>Перестроить верх единичного среза конуса (радиус верха k при радиусе низа 1).</summary>
        private static void ScaleTop(float k)
        {
            if (_frustum == null || Mathf.Abs(k - _shownTop) < 0.001f) return;
            _shownTop = k;
            const int Seg = 40;
            var v = new Vector3[(Seg + 1) * 2 + 2];
            for (int i = 0; i <= Seg; i++)
            {
                float a = i / (float)Seg * Mathf.PI * 2f;
                v[i * 2] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2 + 1] = new Vector3(Mathf.Cos(a) * k, 1f, Mathf.Sin(a) * k);
            }
            v[v.Length - 2] = Vector3.zero; v[v.Length - 1] = Vector3.up;
            _frustum.vertices = v;
            _frustum.RecalculateNormals(); _frustum.RecalculateBounds();
        }

        private static Mesh BuildFrustum()
        {
            const int Seg = 40;
            var m = new Mesh { name = "FillTunerFrustum", hideFlags = HideFlags.DontSave };
            var v = new Vector3[(Seg + 1) * 2 + 2];
            for (int i = 0; i <= Seg; i++)
            {
                float a = i / (float)Seg * Mathf.PI * 2f;
                v[i * 2] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2 + 1] = new Vector3(Mathf.Cos(a), 1f, Mathf.Sin(a));
            }
            int bc = v.Length - 2, tc = v.Length - 1;
            v[bc] = Vector3.zero; v[tc] = Vector3.up;
            var t = new System.Collections.Generic.List<int>();
            for (int i = 0; i < Seg; i++)
            {
                int a = i * 2, b = a + 2;
                t.Add(a); t.Add(a + 1); t.Add(b); t.Add(b); t.Add(a + 1); t.Add(b + 1);      // бок
                t.Add(a); t.Add(b); t.Add(a + 1); t.Add(b); t.Add(b + 1); t.Add(a + 1);      // бок изнутри
                t.Add(a + 1); t.Add(tc); t.Add(b + 1);                                       // верх (уровень)
                t.Add(a + 1); t.Add(b + 1); t.Add(tc);
            }
            m.vertices = v; m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            _shownTop = 1f;
            return m;
        }
    }
}
