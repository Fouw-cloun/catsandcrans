using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// СБОРКА ТАРЫ ОДНОЙ КНОПКОЙ.
    ///
    /// Вешается на корень тары (ведро, банка, корыто) рядом с Cookware.
    /// Поставь тару РОВНО (дном вниз) в сцене или в префабе и нажми
    /// ⋮ → «Подогнать и собрать». Скрипт:
    ///   1. измеряет модель (габариты мешей, без ручки — её можно исключить);
    ///   2. считает внутренний объём: минус толщина стенок и дна;
    ///   3. создаёт дочерние объекты с правильными размерами и позициями:
    ///        AutoColliders  — дно + стенки (у круглой тары — кольцо из сегментов);
    ///        Receive        — триггер приёма внутри;
    ///        Mouth          — центр горловины сверху;
    ///        HeapAnchor     — Food Heap Renderer + Cookware Heap Visual (горка);
    ///        LiquidAnchor   — Cookware Liquid Visual (молоко и т.п.), если включено;
    ///   4. прописывает в Cookware: Mouth и Inside Volume.
    ///
    /// Нажимать можно сколько угодно раз — старые авто-объекты заменяются.
    /// Размеры, которые получились, видны в полях Inner … — это и есть «пресет»
    /// этой тары. Их можно поправить руками и нажать «Собрать по размерам»
    /// (без повторного замера).
    ///
    /// Все созданные объекты сохраняются в префаб как обычные.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CookwareAutoSetup : MonoBehaviour
    {
        public enum Form : byte
        {
            [InspectorName("Прямоугольная (корыто, ящик)")] Box = 0,
            [InspectorName("Круглая (ведро, кастрюля, банка)")] Round = 1,
            [InspectorName("По форме модели (ковш экскаватора/бульдозера, кривое корыто)")] Scan = 2,
        }

        [Header("Форма")]
        [SerializeField] private Form _form = Form.Round;
        [Tooltip("Эти меши не учитываются при замере — ручка ведра, крышка, петли")]
        [SerializeField] private Renderer[] _ignoreRenderers;

        [Header("Стенки")]
        [Tooltip("Толщина стенок, м")]
        [Min(0.001f)][SerializeField] private float _wall = 0.02f;
        [Tooltip("Толщина дна, м")]
        [Min(0.001f)][SerializeField] private float _bottom = 0.02f;
        [Tooltip("Круглая: радиус у дна относительно радиуса у края. Ведро сужается книзу — 0.85.")]
        [Range(0.5f, 1.2f)][SerializeField] private float _bottomRadiusFactor = 0.88f;
        [Tooltip("Круглая: сколько сегментов в кольце стенок. Больше — ровнее круг.")]
        [Range(6, 24)][SerializeField] private int _roundSegments = 12;

        [Header("Что собрать")]
        [SerializeField] private bool _makeColliders = true;
        [SerializeField] private bool _makeHeap = true;
        [Tooltip("Жидкость внутри (молоко, сливки, масло). Для ведра — да, для корыта — обычно нет.")]
        [SerializeField] private bool _makeLiquid = true;
        [Tooltip("Выключить старые НЕ-триггерные коллайдеры на самом корне (их заменят авто-стенки)")]
        [SerializeField] private bool _disableRootColliders = true;

        [Header("По форме модели (ковш)")]
        [Tooltip("Сама найти, куда смотрит горловина (ковш может лежать как угодно). Выкл — горловина вверх, как стоит")]
        [SerializeField] private bool _autoUp = true;
        [Tooltip("Точность поиска полости: клеток по длинной стороне")]
        [Range(24, 128)][SerializeField] private int _scanResolution = 64;
        [Tooltip("Коробок-коллайдеров по длинной стороне (меньше — проще физика)")]
        [Range(4, 32)][SerializeField] private int _colliderGrid = 12;
        [Tooltip("Насколько ниже кромки перелива держать «полную» тару, доля глубины")]
        [Range(0f, 0.3f)][SerializeField] private float _rimMargin = 0.06f;
        [SerializeField, HideInInspector] private Vector4[] _scanProfile = new Vector4[0];
        [SerializeField, HideInInspector] private Quaternion _scanFrameLocal = Quaternion.identity;

        [Header("Итог замера — «пресет» тары (м, мир)")]
        [Tooltip("Внутренняя ширина (X) / высота (Y) / глубина (Z)")]
        [SerializeField] private Vector3 _innerSize = new Vector3(0.3f, 0.3f, 0.3f);
        [Tooltip("Центр ДНА внутри, в осях корня тары")]
        [SerializeField] private Vector3 _innerBottomLocal;
        [Tooltip("Круглая: внутренний радиус у края")]
        [SerializeField] private float _radiusTop = 0.15f;

        private const string CollName = "AutoColliders";
        private const string ReceiveName = "Receive";
        private const string MouthName = "Mouth";
        private const string HeapName = "HeapAnchor";
        private const string LiquidName = "LiquidAnchor";

        public Vector3 InnerSize => _innerSize;

#if UNITY_EDITOR
        [ContextMenu("Подогнать и собрать")]
        private void FitAndBuild()
        {
            if (_form == Form.Scan) { ScanAndBuild(); return; }
            if (!Measure()) return;
            Build();
        }

        [ContextMenu("Собрать по размерам (без замера)")]
        private void Build()
        {
            if (_form == Form.Scan) { ScanAndBuild(); return; }
            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Сборка тары");
            Transform root = transform;
            Vector3 bottomW = root.TransformPoint(_innerBottomLocal);
            float w = _innerSize.x, h = _innerSize.y, d = _innerSize.z;
            float rTop = _form == Form.Round ? _radiusTop : 0f;
            float rBot = rTop * _bottomRadiusFactor;

            // ── стенки и дно ──
            if (_makeColliders)
            {
                Transform cols = Recreate(CollName);
                if (_form == Form.Box)
                {
                    AddBox(cols, "Bottom", bottomW + Vector3.down * _bottom * 0.5f, Quaternion.identity,
                           new Vector3(w + _wall * 2f, _bottom, d + _wall * 2f), false);
                    AddBox(cols, "WallL", bottomW + new Vector3(-(w + _wall) * 0.5f, h * 0.5f, 0f), Quaternion.identity,
                           new Vector3(_wall, h, d + _wall * 2f), false);
                    AddBox(cols, "WallR", bottomW + new Vector3((w + _wall) * 0.5f, h * 0.5f, 0f), Quaternion.identity,
                           new Vector3(_wall, h, d + _wall * 2f), false);
                    AddBox(cols, "WallB", bottomW + new Vector3(0f, h * 0.5f, -(d + _wall) * 0.5f), Quaternion.identity,
                           new Vector3(w, h, _wall), false);
                    AddBox(cols, "WallF", bottomW + new Vector3(0f, h * 0.5f, (d + _wall) * 0.5f), Quaternion.identity,
                           new Vector3(w, h, _wall), false);
                }
                else
                {
                    AddBox(cols, "Bottom", bottomW + Vector3.down * _bottom * 0.5f, Quaternion.identity,
                           new Vector3(rBot * 2f, _bottom, rBot * 2f), false);
                    // Кольцо из сегментов; сегмент наклонён, если ведро сужается.
                    float rMid = (rTop + rBot) * 0.5f + _wall * 0.5f;
                    float segW = 2f * Mathf.PI * rMid / _roundSegments * 1.08f;
                    float tilt = Mathf.Atan2(rTop - rBot, h) * Mathf.Rad2Deg;
                    for (int i = 0; i < _roundSegments; i++)
                    {
                        float a = i / (float)_roundSegments * 360f;
                        Quaternion q = Quaternion.Euler(0f, a, 0f);
                        Vector3 outward = q * Vector3.forward;
                        Vector3 c = bottomW + outward * rMid + Vector3.up * h * 0.5f;
                        // Положительный поворот вокруг X наклоняет верх сегмента наружу (+Z).
                        Quaternion rot = q * Quaternion.Euler(tilt, 0f, 0f);
                        AddBox(cols, "Wall" + i, c, rot, new Vector3(segW, h / Mathf.Cos(tilt * Mathf.Deg2Rad), _wall), false);
                    }
                }
                if (_disableRootColliders)
                    foreach (Collider c in root.GetComponents<Collider>())
                        if (!c.isTrigger) c.enabled = false;
            }

            // ── приём ──
            Transform recv = Recreate(ReceiveName);
            Vector3 recvSize = _form == Form.Box
                ? new Vector3(w * 0.96f, h * 0.9f, d * 0.96f)
                : new Vector3(rBot * 1.4f, h * 0.9f, rBot * 1.4f);
            BoxCollider rc = AddBox(recv, null, bottomW + Vector3.up * h * 0.45f, Quaternion.identity, recvSize, true);
            recv.gameObject.AddComponent<CookwareTriggerRelay>();

            // ── горловина ──
            Transform mouth = Recreate(MouthName);
            mouth.position = bottomW + Vector3.up * h;
            mouth.rotation = Quaternion.identity;

            // ── тара ──
            Cookware cw = GetComponent<Cookware>();
            if (cw != null)
            {
                var so = new UnityEditor.SerializedObject(cw);
                SetRef(so, "_mouth", mouth);
                SetRef(so, "_insideVolume", rc);
                so.ApplyModifiedProperties();
            }

            // ── горка ──
            if (_makeHeap)
            {
                Transform heap = FindOrCreate(HeapName);
                heap.position = bottomW;
                heap.rotation = Quaternion.identity;
                FoodHeapRenderer fr = heap.GetComponent<FoodHeapRenderer>();
                if (fr == null) fr = heap.gameObject.AddComponent<FoodHeapRenderer>();
                if (heap.GetComponent<CookwareHeapVisual>() == null) heap.gameObject.AddComponent<CookwareHeapVisual>();
                var so = new UnityEditor.SerializedObject(fr);
                SetEnum(so, "_shape", (int)FoodHeapRenderer.Shape.Fill);
                SetBool(so, "_worldUpright", true);
                SetBool(so, "_ignoreParentScale", true);
                SetBool(so, "_fillRect", _form == Form.Box);
                Vector2 fp = _form == Form.Box ? new Vector2(w * 0.48f, d * 0.48f) : new Vector2(rBot * 0.96f, rBot * 0.96f);
                SetVec2(so, "_footprint", fp);
                SetFloat(so, "_maxHeight", h * 0.9f);
                so.ApplyModifiedProperties();
            }

            // ── жидкость ──
            if (_makeLiquid)
            {
                Transform liq = FindOrCreate(LiquidName);
                liq.position = bottomW;
                liq.rotation = Quaternion.identity;
                CookwareLiquidVisual lv = liq.GetComponent<CookwareLiquidVisual>();
                if (lv == null) lv = liq.gameObject.AddComponent<CookwareLiquidVisual>();
                var so = new UnityEditor.SerializedObject(lv);
                float rb = _form == Form.Box ? Mathf.Min(w, d) * 0.5f : rBot;
                float rt = _form == Form.Box ? rb : rTop;
                SetFloat(so, "_radiusBottom", rb * 0.97f);
                SetFloat(so, "_radiusTop", rt * 0.97f);
                SetEnum(so, "_form", _form == Form.Box ? 1 : 0);
                SetVec2(so, "_halfSize", new Vector2(w * 0.485f, d * 0.485f));
                SetFloat(so, "_maxHeight", h * 0.92f);
                SetBool(so, "_ignoreParentScale", true);
                // Объём одной порции — чтобы полная тара (Capacity) была полной до края.
                int cap = cw != null && cw.Def != null ? Mathf.Max(1, cw.Def.Capacity) : 12;
                float vol = _form == Form.Box ? w * d * h * 0.92f
                                              : Mathf.PI * (rb * rb + rb * rt + rt * rt) / 3f * h * 0.92f;
                SetFloat(so, "_portionVolume", vol / cap);
                so.ApplyModifiedProperties();

                // Выплёскивание — сразу рядом с Cookware.
                if (GetComponent<CookwareSpill>() == null) UnityEditor.Undo.AddComponent<CookwareSpill>(gameObject);
            }

            UnityEditor.EditorUtility.SetDirty(gameObject);
            Debug.Log($"[Тара] «{name}» собрана: {_form}, внутри {w:0.###}×{h:0.###}×{d:0.###} м" +
                      (_form == Form.Round ? $", радиус {rBot:0.###}…{rTop:0.###}" : ""), this);
        }

        /// <summary>
        /// Ковш: ищем полость «заливкой водой» (CookwareCavityScan) и собираем всё в её осях —
        /// коллайдеры по форме дна и стенок, приём, горловину, горку и жидкость с профилем ширины.
        /// </summary>
        private void ScanAndBuild()
        {
            var rs = new List<Renderer>();
            var skip = new HashSet<Renderer>();
            if (_ignoreRenderers != null) foreach (Renderer r in _ignoreRenderers) if (r != null) skip.Add(r);
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (skip.Contains(r) || !r.enabled || r is ParticleSystemRenderer) continue;
                if ((r.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                if (IsAutoPart(r.transform)) continue;
                if (r.GetComponentInParent<CookwareLiquidVisual>() != null) continue;
                rs.Add(r);
            }
            if (rs.Count == 0) { Debug.LogError($"[Тара] «{name}»: нет мешей для поиска полости", this); return; }

            Quaternion frame;
            string axis = "+Y";
            if (_autoUp) frame = CookwareCavityScan.BestFrame(transform, rs, out axis, out _);
            else frame = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up).sqrMagnitude > 1e-4f
                ? Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized : Vector3.forward, Vector3.up);
            CookwareCavityScan.Result res = CookwareCavityScan.Scan(transform, rs, frame, _scanResolution, 12, _colliderGrid, Mathf.Max(0.01f, _wall));
            if (!res.Ok) { Debug.LogError($"[Тара] «{name}»: {res.Why}", this); return; }

            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Сборка ковша");
            float depth = res.Depth * (1f - _rimMargin);
            Vector3 up = frame * Vector3.up;

            // ── коллайдеры ──
            if (_makeColliders)
            {
                Transform cols = Recreate(CollName);
                for (int i = 0; i < res.BoxCenters.Count; i++)
                    AddBox(cols, "Box" + i, res.BoxCenters[i], frame, res.BoxSizes[i], false);
                if (IsVehiclePart())
                    Debug.Log($"[Тара] «{name}» — часть техники (Rigidbody у родителя): свои коллайдеры ковша НЕ выключаю, " +
                              "копать он будет как раньше. Network Object / Network Transform / Rigidbody на сам ковш не вешай", this);
                else if (_disableRootColliders)
                    foreach (Collider c in GetComponents<Collider>())
                        if (!c.isTrigger) c.enabled = false;
                foreach (MeshCollider mc in GetComponentsInChildren<MeshCollider>(true))
                    if (mc.enabled && !mc.convex && !mc.isTrigger && !IsAutoPart(mc.transform))
                        Debug.LogWarning($"[Тара] «{name}»: на «{mc.name}» невыпуклый Mesh Collider — с Rigidbody он не работает " +
                                         "и мешает авто-коллайдерам. Выключи его (стенки и дно теперь в AutoColliders)", mc);
            }

            // ── приём: коробка по воде на уровне перелива ──
            Transform recv = Recreate(ReceiveName);
            Vector3 recvCentre = res.Anchor + frame * new Vector3(res.MouthCenter.x, depth * 0.5f, res.MouthCenter.y);
            BoxCollider rc = AddBox(recv, null, recvCentre, frame,
                                    new Vector3(res.MouthHalf.x * 2f * 0.94f, depth * 0.95f, res.MouthHalf.y * 2f * 0.94f), true);
            recv.gameObject.AddComponent<CookwareTriggerRelay>();

            // ── горловина ──
            Transform mouth = Recreate(MouthName);
            mouth.SetPositionAndRotation(res.Anchor + frame * new Vector3(res.MouthCenter.x, depth, res.MouthCenter.y), frame);

            Cookware cw = GetComponent<Cookware>();
            if (cw != null)
            {
                var so = new UnityEditor.SerializedObject(cw);
                SetRef(so, "_mouth", mouth);
                SetRef(so, "_insideVolume", rc);
                SetEnum(so, "_upAxis", UpAxisIndex(axis));
                so.ApplyModifiedProperties();
            }

            // ── горка ──
            Vector4 mid = res.Profile[res.Profile.Length / 2];
            if (_makeHeap)
            {
                Transform heap = FindOrCreate(HeapName);
                heap.SetPositionAndRotation(res.Anchor + frame * new Vector3(mid.z, 0f, mid.w), frame);
                FoodHeapRenderer fr = heap.GetComponent<FoodHeapRenderer>();
                if (fr == null) fr = heap.gameObject.AddComponent<FoodHeapRenderer>();
                if (heap.GetComponent<CookwareHeapVisual>() == null) heap.gameObject.AddComponent<CookwareHeapVisual>();
                var so = new UnityEditor.SerializedObject(fr);
                SetEnum(so, "_shape", (int)FoodHeapRenderer.Shape.Fill);
                SetBool(so, "_worldUpright", true);
                SetBool(so, "_ignoreParentScale", true);
                SetBool(so, "_fillRect", true);
                SetVec2(so, "_footprint", new Vector2(mid.x * 0.9f, mid.y * 0.9f));
                SetFloat(so, "_maxHeight", depth * 0.9f);
                so.ApplyModifiedProperties();
            }

            // ── жидкость: прямоугольная с профилем ширины по высоте ──
            if (_makeLiquid)
            {
                Transform liq = FindOrCreate(LiquidName);
                liq.SetPositionAndRotation(res.Anchor, frame);
                CookwareLiquidVisual lv = liq.GetComponent<CookwareLiquidVisual>();
                if (lv == null) lv = liq.gameObject.AddComponent<CookwareLiquidVisual>();
                var so = new UnityEditor.SerializedObject(lv);
                Vector4 topB = res.Profile[res.Profile.Length - 1], botB = res.Profile[0];
                SetEnum(so, "_form", 1);
                SetVec2(so, "_halfSize", new Vector2(topB.x, topB.y));
                SetFloat(so, "_radiusBottom", Mathf.Max(0.01f, Mathf.Min(botB.x, botB.y)));
                SetFloat(so, "_radiusTop", Mathf.Max(0.01f, Mathf.Min(topB.x, topB.y)));
                SetFloat(so, "_maxHeight", res.Depth);
                SetBool(so, "_ignoreParentScale", true);
                SerializedPropertyVec4Array(so, "_boxProfile", res.Profile);
                int cap = cw != null && cw.Def != null ? Mathf.Max(1, cw.Def.Capacity) : 12;
                SetFloat(so, "_portionVolume", res.Volume / cap);
                so.ApplyModifiedProperties();
                if (GetComponent<CookwareSpill>() == null) UnityEditor.Undo.AddComponent<CookwareSpill>(gameObject);
            }

            _innerSize = new Vector3(res.MouthHalf.x * 2f, res.Depth, res.MouthHalf.y * 2f);
            _innerBottomLocal = transform.InverseTransformPoint(res.Anchor);
            _radiusTop = Mathf.Min(res.MouthHalf.x, res.MouthHalf.y);
            _scanProfile = res.Profile;
            _scanFrameLocal = Quaternion.Inverse(transform.rotation) * frame;
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.EditorUtility.SetDirty(gameObject);

            float buckets = cw != null && cw.Def != null ? cw.Def.Capacity / (float)FoodUnits.Bucket : 0f;
            Debug.Log($"[Тара] «{name}» (ковш) собран: горловина смотрит по оси {axis}" +
                      (Vector3.Angle(up, Vector3.up) > 30f ? " — сейчас ковш стоит НЕ горловиной вверх, для готовки поставь его ровно" : "") +
                      $", глубина {res.Depth:0.###} м, вода {res.MouthHalf.x * 2f:0.###}×{res.MouthHalf.y * 2f:0.###} м, " +
                      $"объём {res.Volume * 1000f:0.#} л, коробок-коллайдеров {res.BoxCenters.Count}, клеток воды {res.Wet}" +
                      (cw != null && cw.Def != null ? $". Вместимость Cookware Def = {cw.Def.Capacity} порц. (~{buckets:0.#} ведра)" : ". Нет Cookware Def — порции не посчитать"), this);
        }

        /// <summary>Ковш — часть машины: своего Rigidbody нет, а у родителя есть.</summary>
        private bool IsVehiclePart()
            => GetComponent<Rigidbody>() == null && transform.parent != null && transform.parent.GetComponentInParent<Rigidbody>() != null;

        private static int UpAxisIndex(string axis)
        {
            switch (axis)
            {
                case "+Y": return (int)Cookware.UpAxis.PlusY;
                case "+Z": return (int)Cookware.UpAxis.PlusZ;
                case "−Z": return (int)Cookware.UpAxis.MinusZ;
                case "+X": return (int)Cookware.UpAxis.PlusX;
                case "−X": return (int)Cookware.UpAxis.MinusX;
                case "−Y": return (int)Cookware.UpAxis.MinusY;
                default: return (int)Cookware.UpAxis.Auto;
            }
        }

        private static void SerializedPropertyVec4Array(UnityEditor.SerializedObject so, string p, Vector4[] v)
        {
            var sp = so.FindProperty(p);
            if (sp == null) { Debug.LogWarning($"[Тара] нет поля {p} — обнови CookwareLiquidVisual.cs из архива"); return; }
            sp.arraySize = v.Length;
            for (int i = 0; i < v.Length; i++) sp.GetArrayElementAtIndex(i).vector4Value = v[i];
        }

        /// <summary>Замер модели: внешние габариты мешей в мире → внутренний объём.</summary>
        private bool Measure()
        {
            var skip = new HashSet<Renderer>();
            if (_ignoreRenderers != null) foreach (Renderer r in _ignoreRenderers) if (r != null) skip.Add(r);

            bool any = false;
            Bounds b = default;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (skip.Contains(r) || !r.enabled) continue;
                if ((r.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                if (IsAutoPart(r.transform)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any)
            {
                Debug.LogError($"[Тара] «{name}»: нет мешей для замера", this);
                return false;
            }

            float w = Mathf.Max(0.01f, b.size.x - _wall * 2f);
            float d = Mathf.Max(0.01f, b.size.z - _wall * 2f);
            float h = Mathf.Max(0.01f, b.size.y - _bottom);
            _innerSize = new Vector3(w, h, d);
            _radiusTop = Mathf.Min(w, d) * 0.5f;
            Vector3 bottomW = new Vector3(b.center.x, b.min.y + _bottom, b.center.z);
            _innerBottomLocal = transform.InverseTransformPoint(bottomW);
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[Тара] «{name}» замер: снаружи {b.size.x:0.###}×{b.size.y:0.###}×{b.size.z:0.###} м", this);
            return true;
        }

        private bool IsAutoPart(Transform t)
        {
            for (Transform p = t; p != null && p != transform; p = p.parent)
            {
                string n = p.name;
                if (n == CollName || n == ReceiveName || n == MouthName || n == HeapName || n == LiquidName) return true;
                if (n.EndsWith("(auto)")) return true;
            }
            return false;
        }

        private Transform Recreate(string childName)
        {
            Transform old = transform.Find(childName);
            if (old != null) UnityEditor.Undo.DestroyObjectImmediate(old.gameObject);
            GameObject go = new GameObject(childName);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Сборка тары");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private Transform FindOrCreate(string childName)
        {
            Transform t = transform.Find(childName);
            if (t != null) return t;
            GameObject go = new GameObject(childName);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Сборка тары");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        /// <summary>Коробка-коллайдер в мировых размерах, как дочерний объект.</summary>
        private BoxCollider AddBox(Transform parent, string childName, Vector3 worldCentre, Quaternion worldRot,
                                   Vector3 worldSize, bool trigger)
        {
            Transform t = parent;
            if (childName != null)
            {
                GameObject go = new GameObject(childName);
                go.layer = gameObject.layer;
                go.transform.SetParent(parent, false);
                t = go.transform;
            }
            t.SetPositionAndRotation(worldCentre, worldRot);
            BoxCollider bc = t.gameObject.AddComponent<BoxCollider>();
            // Размер в мире → в осях объекта (снять масштаб родителей).
            Vector3 ls = t.lossyScale;
            bc.size = new Vector3(worldSize.x / Mathf.Max(1e-5f, Mathf.Abs(ls.x)),
                                  worldSize.y / Mathf.Max(1e-5f, Mathf.Abs(ls.y)),
                                  worldSize.z / Mathf.Max(1e-5f, Mathf.Abs(ls.z)));
            bc.isTrigger = trigger;
            return bc;
        }

        private static void SetRef(UnityEditor.SerializedObject so, string p, Object v)
        { var sp = so.FindProperty(p); if (sp != null) sp.objectReferenceValue = v; }
        private static void SetBool(UnityEditor.SerializedObject so, string p, bool v)
        { var sp = so.FindProperty(p); if (sp != null) sp.boolValue = v; }
        private static void SetFloat(UnityEditor.SerializedObject so, string p, float v)
        { var sp = so.FindProperty(p); if (sp != null) sp.floatValue = v; }
        private static void SetEnum(UnityEditor.SerializedObject so, string p, int v)
        { var sp = so.FindProperty(p); if (sp != null) sp.enumValueIndex = v; }
        private static void SetVec2(UnityEditor.SerializedObject so, string p, Vector2 v)
        { var sp = so.FindProperty(p); if (sp != null) sp.vector2Value = v; }
#endif

        private void OnDrawGizmosSelected()
        {
            Vector3 b = transform.TransformPoint(_innerBottomLocal);
            Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.8f);
            if (_form == Form.Scan)
            {
                if (_scanProfile == null || _scanProfile.Length < 2) return;
                Gizmos.matrix = Matrix4x4.TRS(b, transform.rotation * _scanFrameLocal, Vector3.one);
                for (int k = 0; k < _scanProfile.Length; k++)
                {
                    float y = _innerSize.y * k / (_scanProfile.Length - 1);
                    Vector4 p = _scanProfile[k];
                    Gizmos.DrawWireCube(new Vector3(p.z, y, p.w), new Vector3(p.x * 2f, 0.001f, p.y * 2f));
                }
                Gizmos.matrix = Matrix4x4.identity;
                return;
            }
            if (_form == Form.Box)
                Gizmos.DrawWireCube(b + Vector3.up * _innerSize.y * 0.5f, _innerSize);
            else
            {
                float rB = _radiusTop * _bottomRadiusFactor;
                for (int i = 0; i < 32; i++)
                {
                    float a0 = i / 32f * Mathf.PI * 2f, a1 = (i + 1) / 32f * Mathf.PI * 2f;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    Gizmos.DrawLine(b + d0 * rB, b + d1 * rB);
                    Vector3 top = b + Vector3.up * _innerSize.y;
                    Gizmos.DrawLine(top + d0 * _radiusTop, top + d1 * _radiusTop);
                    if (i % 8 == 0) Gizmos.DrawLine(b + d0 * rB, top + d0 * _radiusTop);
                }
            }
        }
    }
}
