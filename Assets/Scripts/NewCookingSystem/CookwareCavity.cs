using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ПОЛОСТЬ ТАРЫ СЛОЖНОЙ ФОРМЫ — ковш экскаватора/бульдозера, рюмка для бетона, воронка.
    /// Замена Cookware Auto Setup для фигур, где «коробка по габаритам» не подходит.
    ///
    /// Полость задаётся несколькими числами, которые видно и можно таскать ручками в сцене:
    ///   • точка дна (самая глубокая), ось «вверх» (куда смотрит горловина), глубина;
    ///   • ширина воды у дна и у края (ковш — по X и Z; рюмка — радиус);
    ///   • сдвиг центра горловины относительно дна (ковш: горловина смещена к зубьям);
    ///   • «изгиб»: как растёт ширина с высотой (меньше 1 — круглое дно ковша, 1 — прямой конус).
    ///
    /// Кнопки (в инспекторе или ⋮):
    ///   1. «Найти полость» — «наливает воду в модель» (только в меши самой ёмкости — Body Renderers)
    ///      и заполняет числа. Ручки и рамки сразу показывают, что нашлось.
    ///   2. Поправить ручками, если надо.
    ///   3. «Собрать» — по числам: коллайдеры (дно + наклонные стенки по форме), приём (Receive + Relay),
    ///      горловина, горка, жидкость (ковш — прямоугольная с профилем, рюмка — круглая с профилем стенок).
    ///
    /// Работает и для ковша на машине: свои коллайдеры ковша не трогает, Network-компоненты не нужны.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CookwareCavity : MonoBehaviour
    {
        public enum Shape : byte
        {
            [InspectorName("Ковш / корыто (прямоугольная вода, своя форма)")] Trough = 0,
            [InspectorName("Рюмка / конус / воронка (круглая вода)")] Cone = 1,
        }

        public enum Up : byte
        {
            [InspectorName("Найти самой")] Auto = 0,
            [InspectorName("+Y корня")] PlusY = 1,
            [InspectorName("−Y корня")] MinusY = 2,
            [InspectorName("+Z корня")] PlusZ = 3,
            [InspectorName("−Z корня")] MinusZ = 4,
            [InspectorName("+X корня")] PlusX = 5,
            [InspectorName("−X корня")] MinusX = 6,
        }

        [Header("Форма")]
        [SerializeField] private Shape _shape = Shape.Trough;

        [Header("Из чего полость (для «Найти»)")]
        [Tooltip("Меши САМОЙ ёмкости: корпус ковша, чаша рюмки. Без проушин, пальцев, штоков, ног и рамы. " +
                 "Пусто — все меши, кроме Ignore")]
        [SerializeField] private Renderer[] _bodyRenderers;
        [Tooltip("Не учитывать (если Body Renderers пусто): проушины, гидроцилиндры, ноги, рама")]
        [SerializeField] private Renderer[] _ignoreRenderers;
        [Tooltip("Куда смотрит горловина. «Найти самой» — где больше воды держится")]
        [SerializeField] private Up _up = Up.Auto;
        [Tooltip("Заделать щели в модели шириной в столько клеток (зазоры между листами ковша)")]
        [Range(0, 4)][SerializeField] private int _closeGaps = 1;
        [Tooltip("Точность: клеток по длинной стороне")]
        [Range(24, 160)][SerializeField] private int _resolution = 80;
        [Tooltip("Рюмка/воронка: дно полости — на уровне дырки внизу (узкий патрубок/затвор под ней в полость не входит, " +
                 "дырку закрывает дно-коллайдер)")]
        [SerializeField] private bool _floorAtHole = true;

        [Header("Полость — правится ручками в сцене")]
        [Tooltip("Точка дна (самая глубокая), в осях корня")]
        [SerializeField] private Vector3 _bottomLocal;
        [Tooltip("Поворот рамки полости относительно корня (Y рамки — куда смотрит горловина)")]
        [SerializeField] private Quaternion _frameLocal = Quaternion.identity;
        [Tooltip("Глубина: от дна до края перелива, м")]
        [Min(0.01f)][SerializeField] private float _depth = 0.4f;
        [Tooltip("Полуширина воды у дна (X, Z рамки), м. Рюмка — X = радиус")]
        [SerializeField] private Vector2 _bottomHalf = new Vector2(0.15f, 0.1f);
        [Tooltip("Полуширина воды у края (X, Z рамки), м. Рюмка — X = радиус")]
        [SerializeField] private Vector2 _topHalf = new Vector2(0.3f, 0.3f);
        [Tooltip("Ковш: на сколько центр горловины сдвинут от точки дна (X, Z рамки), м")]
        [SerializeField] private Vector2 _topShift = Vector2.zero;
        [Tooltip("Как растёт ширина с высотой: 0.5 — круглое дно (ковш), 1 — прямые наклонные стенки (конус), 2 — узкий низ и резкое расширение")]
        [Range(0.25f, 2.5f)][SerializeField] private float _curve = 0.6f;
        [Tooltip("«Полная» тара ниже края перелива на эту долю глубины")]
        [Range(0f, 0.3f)][SerializeField] private float _rimMargin = 0.05f;

        [Header("Что собрать")]
        [SerializeField] private bool _makeColliders = true;
        [Tooltip("Толщина стенок-коллайдеров, м")]
        [Min(0.005f)][SerializeField] private float _wall = 0.04f;
        [Tooltip("Ярусов стенок по высоте (больше — точнее форма)")]
        [Range(2, 12)][SerializeField] private int _bands = 6;
        [Tooltip("Рюмка: сегментов в кольце")]
        [Range(8, 32)][SerializeField] private int _segments = 16;
        [SerializeField] private bool _makeHeap = true;
        [SerializeField] private bool _makeLiquid = true;
        [Tooltip("Выключить старые не-триггерные коллайдеры на корне (у ковша на машине — никогда)")]
        [SerializeField] private bool _disableRootColliders = false;

        [Header("Вид в редакторе")]
        [SerializeField] private bool _gizmo = true;

        private const string CollName = "AutoColliders", ReceiveName = "Receive", MouthName = "Mouth",
                             HeapName = "HeapAnchor", LiquidName = "LiquidAnchor";
        private const int ProfileN = 12;
        private const int ScanBands = 24;

        public Shape Form => _shape;

        // ───────────────────────── ГЕОМЕТРИЯ ПО ЧИСЛАМ ─────────────────────────

        /// <summary>Мировая рамка полости (Y — вверх полости).</summary>
        public Quaternion FrameWorld => transform.rotation * _frameLocal;
        public Vector3 BottomWorld => transform.TransformPoint(_bottomLocal);

        /// <summary>Доля ширины на высоте t (0 — дно, 1 — край).</summary>
        private float K(float t) => Mathf.Pow(Mathf.Clamp01(t), _curve);

        /// <summary>Полуширина воды на высоте t (м, X/Z рамки). Рюмка — x = радиус.</summary>
        public Vector2 HalfAt(float t)
        {
            float k = K(t);
            Vector2 h = Vector2.Lerp(_bottomHalf, _topHalf, k);
            return new Vector2(Mathf.Max(0.005f, h.x), Mathf.Max(0.005f, _shape == Shape.Cone ? h.x : h.y));
        }

        /// <summary>Центр воды на высоте t относительно точки дна (X, Z рамки).</summary>
        public Vector2 CenterAt(float t) => _shape == Shape.Cone ? Vector2.zero : _topShift * K(t);

        /// <summary>Точка полости в мир: t — доля глубины, (x, z) — в осях рамки от центра на этой высоте.</summary>
        public Vector3 PointAt(float t, float x, float z)
        {
            Vector2 c = CenterAt(t);
            return BottomWorld + FrameWorld * new Vector3(c.x + x, t * _depth, c.y + z);
        }

        /// <summary>Объём до края, м³ (численно).</summary>
        public float Volume
        {
            get
            {
                const int N = 48;
                float v = 0f;
                for (int i = 0; i < N; i++)
                {
                    Vector2 h = HalfAt((i + 0.5f) / N);
                    v += (_shape == Shape.Cone ? Mathf.PI * h.x * h.x : 4f * h.x * h.y) * _depth / N;
                }
                return v;
            }
        }

#if UNITY_EDITOR
        // ───────────────────────── 1. НАЙТИ ─────────────────────────

        [ContextMenu("1. Найти полость (скан модели)")]
        public void EditorScan()
        {
            List<Renderer> rs = BodyRenderers();
            if (rs.Count == 0) { Debug.LogError($"[Полость] «{name}»: нет мешей ёмкости", this); return; }

            Quaternion frame;
            string axis;
            if (_up == Up.Auto) frame = CookwareCavityScan.BestFrame(transform, rs, _closeGaps, out axis, out _);
            else { frame = CookwareCavityScan.FrameFor(transform, LocalAxis(_up)); axis = _up.ToString(); }

            CookwareCavityScan.Result r = CookwareCavityScan.Scan(transform, rs, frame, _resolution, ScanBands, 4, 0.01f, _closeGaps);
            if (!r.Ok)
            {
                Debug.LogError($"[Полость] «{name}»: {r.Why}. Проверь Body Renderers (только корпус), Up, Close Gaps", this);
                return;
            }

            UnityEditor.Undo.RecordObject(this, "Найти полость");
            _frameLocal = Quaternion.Inverse(transform.rotation) * frame;
            _depth = r.Depth;
            Vector4 bot = r.Profile[0], top = r.Profile[r.Profile.Length - 1];
            if (_shape == Shape.Cone)
            {
                // Ось конуса — через центр горловины; радиусы — по площади воды на высоте.
                Vector2 axisXZ = r.FloorCenter + r.MouthCenter;
                int n = r.Area.Length;
                var rad = new float[n];
                for (int i = 0; i < n; i++) rad[i] = Mathf.Sqrt(Mathf.Max(1e-6f, r.Area[i]) / Mathf.PI);
                float rt = rad[n - 1];
                // Дырка внизу: снизу идёт узкий патрубок (радиус почти не растёт) — дно ставим там,
                // где начинается раструб; ниже — не полость.
                int cutI = 0;
                if (_floorAtHole && rt > rad[0] * 1.6f)
                    while (cutI + 1 < n - 2 && rad[cutI + 1] <= rad[0] * 1.12f + r.Depth * 0.01f) cutI++;
                float c = cutI / (float)(n - 1);
                _depth = r.Depth * (1f - c);
                _bottomLocal = transform.InverseTransformPoint(r.Origin + frame * new Vector3(axisXZ.x, r.FloorY + r.Depth * c, axisXZ.y));
                float rb = rad[cutI];
                _bottomHalf = new Vector2(rb, rb);
                _topHalf = new Vector2(rt, rt);
                _topShift = Vector2.zero;
                _curve = FitCurveFrom(rad, cutI, rb, rt);
                if (cutI > 0)
                    Debug.Log($"[Полость] «{name}»: внизу дырка/патрубок (радиус {rad[0]:0.###} м) — дно поднято на {r.Depth * c:0.###} м, " +
                              "до начала раструба; дырку закроет дно-коллайдер", this);
            }
            else
            {
                _bottomLocal = transform.InverseTransformPoint(r.Anchor);
                _bottomHalf = new Vector2(bot.x, bot.y);
                _topHalf = new Vector2(top.x, top.y);
                _topShift = new Vector2(top.z, top.w);
                _curve = FitCurveTrough(r.Profile);
            }
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[Полость] «{name}»: найдена ({axis}) — глубина {_depth:0.###} м, " +
                      (_shape == Shape.Cone ? $"радиус {_bottomHalf.x:0.###} → {_topHalf.x:0.###} м" :
                                              $"вода {_bottomHalf.x * 2f:0.##}×{_bottomHalf.y * 2f:0.##} → {_topHalf.x * 2f:0.##}×{_topHalf.y * 2f:0.##} м, сдвиг {_topShift}") +
                      $", изгиб {_curve:0.##}, объём {Volume * 1000f:0} л (скан {r.Volume * 1000f:0} л), мешей {rs.Count}. " +
                      "Проверь рамки в сцене, поправь ручками и нажми «2. Собрать»", this);
        }

        /// <summary>Изгиб по радиусам выше среза cut (срез — новое дно).</summary>
        private static float FitCurveFrom(float[] rad, int cut, float b, float t)
        {
            int n = rad.Length;
            if (n - cut < 3 || Mathf.Abs(t - b) < 1e-4f) return 1f;
            float sum = 0f; int k = 0;
            for (int i = cut + 1; i < n - 1; i++)
            {
                float s = (i - cut) / (float)(n - 1 - cut);
                float f = (rad[i] - b) / (t - b);
                if (f <= 0.03f || f >= 0.97f || s < 0.08f || s > 0.92f) continue;
                sum += Mathf.Log(f) / Mathf.Log(s); k++;
            }
            return k > 0 ? Mathf.Clamp(sum / k, 0.25f, 2.5f) : 1f;
        }

        private static float FitCurve(float[] area, bool radius, float b, float t)
        {
            if (area == null || area.Length < 3 || Mathf.Abs(t - b) < 1e-4f) return 1f;
            float sum = 0f; int n = 0;
            for (int i = 1; i < area.Length - 1; i++)
            {
                float tt = i / (float)(area.Length - 1);
                float v = radius ? Mathf.Sqrt(area[i] / Mathf.PI) : area[i];
                float f = (v - b) / (t - b);
                if (f <= 0.03f || f >= 0.97f || tt < 0.08f || tt > 0.92f) continue;
                sum += Mathf.Log(f) / Mathf.Log(tt); n++;
            }
            return n > 0 ? Mathf.Clamp(sum / n, 0.25f, 2.5f) : 1f;
        }

        private static float FitCurveTrough(Vector4[] p)
        {
            float sum = 0f; int n = 0;
            int last = p.Length - 1;
            for (int a = 0; a < 2; a++)
            {
                float b = p[0][a], t = p[last][a];
                if (Mathf.Abs(t - b) < 1e-4f) continue;
                for (int i = 1; i < last; i++)
                {
                    float tt = i / (float)last;
                    float f = (p[i][a] - b) / (t - b);
                    if (f <= 0.03f || f >= 0.97f || tt < 0.08f || tt > 0.92f) continue;
                    sum += Mathf.Log(f) / Mathf.Log(tt); n++;
                }
            }
            return n > 0 ? Mathf.Clamp(sum / n, 0.25f, 2.5f) : 0.7f;
        }

        private List<Renderer> BodyRenderers()
        {
            var list = new List<Renderer>();
            if (_bodyRenderers != null && _bodyRenderers.Length > 0)
            {
                foreach (Renderer r in _bodyRenderers) if (r != null) list.Add(r);
                return list;
            }
            var skip = new HashSet<Renderer>();
            if (_ignoreRenderers != null) foreach (Renderer r in _ignoreRenderers) if (r != null) skip.Add(r);
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (skip.Contains(r) || !r.enabled || r is ParticleSystemRenderer) continue;
                if ((r.gameObject.hideFlags & HideFlags.DontSave) != 0 || IsAutoPart(r.transform)) continue;
                if (r.GetComponentInParent<CookwareLiquidVisual>() != null) continue;
                if (r.GetComponent<MeshFilter>() == null) continue;
                list.Add(r);
            }
            return list;
        }

        private static Vector3 LocalAxis(Up u)
        {
            switch (u)
            {
                case Up.MinusY: return Vector3.down;
                case Up.PlusZ: return Vector3.forward;
                case Up.MinusZ: return Vector3.back;
                case Up.PlusX: return Vector3.right;
                case Up.MinusX: return Vector3.left;
                default: return Vector3.up;
            }
        }

        // ───────────────────────── 2. СОБРАТЬ ─────────────────────────

        [ContextMenu("2. Собрать по форме")]
        public void EditorBuild()
        {
            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Собрать полость");
            Quaternion F = FrameWorld;
            Vector3 B = BottomWorld;
            Vector3 up = F * Vector3.up;
            float D = _depth;
            bool vehicle = GetComponent<Rigidbody>() == null && transform.parent != null && transform.parent.GetComponentInParent<Rigidbody>() != null;

            // ── коллайдеры ──
            if (_makeColliders)
            {
                Transform cols = Recreate(CollName);
                if (_shape == Shape.Trough) BuildTroughWalls(cols, F, D);
                else BuildConeWalls(cols, F, D);
                if (!vehicle && _disableRootColliders)
                    foreach (Collider c in GetComponents<Collider>())
                        if (!c.isTrigger) c.enabled = false;
            }

            // ── приём ──
            Transform recv = Recreate(ReceiveName);
            float tLow = _shape == Shape.Cone ? 0.3f : 0.15f;
            Vector2 hr = HalfAt(_shape == Shape.Cone ? tLow : 0.5f);
            Vector2 cr = CenterAt(0.5f);
            float k = _shape == Shape.Cone ? 1.35f : 1.9f;                // круг → вписанный квадрат; ковш — почти вся ширина
            float yMid = (tLow + 0.95f) * 0.5f * D;
            BoxCollider rc = AddBox(recv, null, B + F * new Vector3(cr.x, yMid, cr.y), F,
                                    new Vector3(hr.x * k, (0.95f - tLow) * D, hr.y * k), true);
            recv.gameObject.AddComponent<CookwareTriggerRelay>();

            // ── горловина ──
            float full = 1f - _rimMargin;
            Transform mouth = Recreate(MouthName);
            mouth.SetPositionAndRotation(PointAt(full, 0f, 0f), F);

            Cookware cw = GetComponent<Cookware>();
            if (cw != null)
            {
                var so = new UnityEditor.SerializedObject(cw);
                SetRef(so, "_mouth", mouth);
                SetRef(so, "_insideVolume", rc);
                SetEnum(so, "_upAxis", UpAxisEnum(Quaternion.Inverse(transform.rotation) * up));
                so.ApplyModifiedProperties();
            }

            // ── горка ──
            if (_makeHeap)
            {
                Transform heap = FindOrCreate(HeapName);
                Vector2 c0 = CenterAt(0.3f);
                heap.SetPositionAndRotation(B + F * new Vector3(c0.x, 0f, c0.y), F);
                FoodHeapRenderer fr = heap.GetComponent<FoodHeapRenderer>();
                if (fr == null) fr = heap.gameObject.AddComponent<FoodHeapRenderer>();
                if (heap.GetComponent<CookwareHeapVisual>() == null) heap.gameObject.AddComponent<CookwareHeapVisual>();
                Vector2 h3 = HalfAt(0.4f);
                var so = new UnityEditor.SerializedObject(fr);
                SetEnum(so, "_shape", (int)FoodHeapRenderer.Shape.Fill);
                SetBool(so, "_worldUpright", false);    // горка лежит на дне тары и наклоняется с ней (не «съезжает»)
                SetBool(so, "_ignoreParentScale", true);
                SetBool(so, "_fillRect", _shape == Shape.Trough);
                SetVec2(so, "_footprint", h3 * 0.9f);
                SetFloat(so, "_maxHeight", D * 0.9f);
                SetBool(so, "_preview", false);          // жёлтая «кучка-превью» в редакторе путала с жидкостью
                so.ApplyModifiedProperties();
            }

            // ── жидкость ──
            if (_makeLiquid)
            {
                Transform liq = FindOrCreate(LiquidName);
                liq.SetPositionAndRotation(B, F);
                CookwareLiquidVisual lv = liq.GetComponent<CookwareLiquidVisual>();
                if (lv == null) lv = liq.gameObject.AddComponent<CookwareLiquidVisual>();
                var so = new UnityEditor.SerializedObject(lv);
                SetFloat(so, "_maxHeight", D);
                SetBool(so, "_ignoreParentScale", true);
                SetFloat(so, "_radiusBottom", Mathf.Max(0.01f, Mathf.Min(HalfAt(0f).x, HalfAt(0f).y)));
                SetFloat(so, "_radiusTop", Mathf.Max(0.01f, Mathf.Min(HalfAt(1f).x, HalfAt(1f).y)));
                if (_shape == Shape.Cone)
                {
                    SetEnum(so, "_form", 0);
                    var wp = so.FindProperty("_wallProfile");
                    if (wp != null)
                    {
                        wp.arraySize = ProfileN;
                        for (int i = 0; i < ProfileN; i++) wp.GetArrayElementAtIndex(i).floatValue = HalfAt(i / (float)(ProfileN - 1)).x;
                    }
                    var bp = so.FindProperty("_boxProfile");
                    if (bp != null) bp.arraySize = 0;
                }
                else
                {
                    SetEnum(so, "_form", 1);
                    SetVec2(so, "_halfSize", HalfAt(1f));
                    var bp = so.FindProperty("_boxProfile");
                    if (bp == null) Debug.LogWarning("[Полость] у Cookware Liquid Visual нет поля Box Profile — обнови CookwareLiquidVisual.cs из архива", this);
                    else
                    {
                        bp.arraySize = ProfileN;
                        for (int i = 0; i < ProfileN; i++)
                        {
                            float t = i / (float)(ProfileN - 1);
                            Vector2 h = HalfAt(t), c = CenterAt(t);
                            bp.GetArrayElementAtIndex(i).vector4Value = new Vector4(h.x, h.y, c.x, c.y);
                        }
                    }
                    var wp = so.FindProperty("_wallProfile");
                    if (wp != null) wp.arraySize = 0;
                }
                int cap = cw != null && cw.Def != null ? Mathf.Max(1, cw.Def.Capacity) : 12;
                SetFloat(so, "_portionVolume", Volume / cap);
                so.ApplyModifiedProperties();
                if (GetComponent<CookwareSpill>() == null) UnityEditor.Undo.AddComponent<CookwareSpill>(gameObject);
            }

            UnityEditor.EditorUtility.SetDirty(gameObject);
            Debug.Log($"[Полость] «{name}» собрана ({(_shape == Shape.Cone ? "рюмка" : "ковш")}): глубина {D:0.###} м, объём {Volume * 1000f:0} л" +
                      (vehicle ? " — часть техники: свои коллайдеры ковша не трогал" : "") +
                      (cw != null && cw.Def != null ? $", Capacity {cw.Def.Capacity} порц." : ", нет Cookware/Def"), this);
        }

        /// <summary>Ковш: дно + на каждом ярусе 4 стенки, наклонённые по форме (без ступенек).</summary>
        private void BuildTroughWalls(Transform cols, Quaternion F, float D)
        {
            Vector3 B = BottomWorld;
            Vector2 h0 = HalfAt(0f), c0 = CenterAt(0f);
            AddBox(cols, "Bottom", B + F * new Vector3(c0.x, -_wall * 0.5f, c0.y), F,
                   new Vector3(h0.x * 2f + _wall * 2f, _wall, h0.y * 2f + _wall * 2f), false);
            for (int i = 0; i < _bands; i++)
            {
                float t0 = i / (float)_bands, t1 = (i + 1) / (float)_bands;
                Vector2 ha = HalfAt(t0), hb = HalfAt(t1), ca = CenterAt(t0), cb = CenterAt(t1);
                float dy = (t1 - t0) * D;
                for (int side = -1; side <= 1; side += 2)
                {
                    // стенки ±X (поворот вокруг Z рамки)
                    float xa = ca.x + side * (ha.x + _wall * 0.5f), xb = cb.x + side * (hb.x + _wall * 0.5f);
                    float zc = (ca.y + cb.y) * 0.5f, zl = (ha.y + hb.y) + _wall * 2f;
                    Wall(cols, $"X{(side > 0 ? "+" : "-")}{i}", F, new Vector3(xa, t0 * D, zc), new Vector3(xb, t1 * D, zc), dy, zl, true);
                    // стенки ±Z (поворот вокруг X рамки)
                    float za = ca.y + side * (ha.y + _wall * 0.5f), zb = cb.y + side * (hb.y + _wall * 0.5f);
                    float xc = (ca.x + cb.x) * 0.5f, xl = (ha.x + hb.x) + _wall * 2f;
                    Wall(cols, $"Z{(side > 0 ? "+" : "-")}{i}", F, new Vector3(xc, t0 * D, za), new Vector3(xc, t1 * D, zb), dy, xl, false);
                }
            }
        }

        /// <summary>Наклонная стенка от точки a до точки b (оси рамки), шириной width поперёк.</summary>
        private void Wall(Transform cols, string n, Quaternion F, Vector3 a, Vector3 b, float dy, float width, bool alongX)
        {
            Vector3 d = b - a;
            float len = d.magnitude + _wall * 0.5f;              // нахлёст между ярусами
            Vector3 mid = (a + b) * 0.5f;
            Quaternion tilt = alongX
                ? Quaternion.AngleAxis(-Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, Vector3.forward)
                : Quaternion.AngleAxis(Mathf.Atan2(d.z, d.y) * Mathf.Rad2Deg, Vector3.right);
            Vector3 size = alongX ? new Vector3(_wall, len, width) : new Vector3(width, len, _wall);
            AddBox(cols, n, BottomWorld + F * mid, F * tilt, size, false);
        }

        /// <summary>Рюмка: дно + кольца сегментов на каждом ярусе, наклон по образующей.</summary>
        private void BuildConeWalls(Transform cols, Quaternion F, float D)
        {
            Vector3 B = BottomWorld;
            // Дно — «звезда» из 8 полос: круг без торчащих углов квадрата, закрывает дырку целиком.
            float R = HalfAt(0f).x + _wall * 0.5f;
            for (int k = 0; k < 8; k++)
                AddBox(cols, "Bottom" + k, B + F * new Vector3(0f, -_wall * 0.5f, 0f), F * Quaternion.Euler(0f, k * 22.5f, 0f),
                       new Vector3(R * 2f, _wall, R * 0.42f), false);
            for (int i = 0; i < _bands; i++)
            {
                float t0 = i / (float)_bands, t1 = (i + 1) / (float)_bands;
                float ra = HalfAt(t0).x + _wall * 0.5f, rb = HalfAt(t1).x + _wall * 0.5f;
                float dy = (t1 - t0) * D;
                float tilt = Mathf.Atan2(rb - ra, dy) * Mathf.Rad2Deg;
                float len = Mathf.Sqrt(dy * dy + (rb - ra) * (rb - ra)) + _wall * 0.5f;
                float rm = (ra + rb) * 0.5f;
                float segW = 2f * Mathf.PI * Mathf.Max(ra, rb) / _segments * 1.1f;
                for (int s = 0; s < _segments; s++)
                {
                    Quaternion q = Quaternion.Euler(0f, s / (float)_segments * 360f, 0f);
                    Vector3 c = q * Vector3.forward * rm + Vector3.up * (t0 + t1) * 0.5f * D;
                    AddBox(cols, $"R{i}_{s}", B + F * c, F * q * Quaternion.Euler(tilt, 0f, 0f), new Vector3(segW, len, _wall), false);
                }
            }
        }

        private static int UpAxisEnum(Vector3 localUp)
        {
            Vector3 a = new Vector3(Mathf.Abs(localUp.x), Mathf.Abs(localUp.y), Mathf.Abs(localUp.z));
            if (a.y >= a.x && a.y >= a.z) return localUp.y > 0 ? (int)Cookware.UpAxis.PlusY : (int)Cookware.UpAxis.MinusY;
            if (a.z >= a.x) return localUp.z > 0 ? (int)Cookware.UpAxis.PlusZ : (int)Cookware.UpAxis.MinusZ;
            return localUp.x > 0 ? (int)Cookware.UpAxis.PlusX : (int)Cookware.UpAxis.MinusX;
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
            var go = new GameObject(childName);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Полость");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private Transform FindOrCreate(string childName)
        {
            Transform t = transform.Find(childName);
            if (t != null) return t;
            var go = new GameObject(childName);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Полость");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private BoxCollider AddBox(Transform parent, string childName, Vector3 worldCentre, Quaternion worldRot, Vector3 worldSize, bool trigger)
        {
            Transform t = parent;
            if (childName != null)
            {
                var go = new GameObject(childName) { layer = gameObject.layer };
                go.transform.SetParent(parent, false);
                t = go.transform;
            }
            t.SetPositionAndRotation(worldCentre, worldRot);
            BoxCollider bc = t.gameObject.AddComponent<BoxCollider>();
            Vector3 ls = t.lossyScale;
            bc.size = new Vector3(worldSize.x / Mathf.Max(1e-5f, Mathf.Abs(ls.x)),
                                  worldSize.y / Mathf.Max(1e-5f, Mathf.Abs(ls.y)),
                                  worldSize.z / Mathf.Max(1e-5f, Mathf.Abs(ls.z)));
            bc.isTrigger = trigger;
            return bc;
        }

        private static void SetRef(UnityEditor.SerializedObject so, string p, Object v) { var sp = so.FindProperty(p); if (sp != null) sp.objectReferenceValue = v; }
        private static void SetBool(UnityEditor.SerializedObject so, string p, bool v) { var sp = so.FindProperty(p); if (sp != null) sp.boolValue = v; }
        private static void SetFloat(UnityEditor.SerializedObject so, string p, float v) { var sp = so.FindProperty(p); if (sp != null) sp.floatValue = v; }
        private static void SetEnum(UnityEditor.SerializedObject so, string p, int v) { var sp = so.FindProperty(p); if (sp != null) sp.enumValueIndex = v; }
        private static void SetVec2(UnityEditor.SerializedObject so, string p, Vector2 v) { var sp = so.FindProperty(p); if (sp != null) sp.vector2Value = v; }
#endif

        // ───────────────────────── РАМКИ В СЦЕНЕ ─────────────────────────

        private void OnDrawGizmosSelected()
        {
            if (!_gizmo) return;
            Quaternion F = FrameWorld;
            const int Lv = 6;
            for (int i = 0; i <= Lv; i++)
            {
                float t = i / (float)Lv;
                Gizmos.color = i == Lv ? new Color(1f, 0.85f, 0.2f, 0.95f) : new Color(0.3f, 0.9f, 1f, 0.7f);
                Vector2 h = HalfAt(t);
                if (_shape == Shape.Cone)
                {
                    for (int s = 0; s < 32; s++)
                    {
                        float a0 = s / 32f * Mathf.PI * 2f, a1 = (s + 1) / 32f * Mathf.PI * 2f;
                        Gizmos.DrawLine(PointAt(t, Mathf.Cos(a0) * h.x, Mathf.Sin(a0) * h.x), PointAt(t, Mathf.Cos(a1) * h.x, Mathf.Sin(a1) * h.x));
                    }
                }
                else
                {
                    Vector3 p00 = PointAt(t, -h.x, -h.y), p10 = PointAt(t, h.x, -h.y), p11 = PointAt(t, h.x, h.y), p01 = PointAt(t, -h.x, h.y);
                    Gizmos.DrawLine(p00, p10); Gizmos.DrawLine(p10, p11); Gizmos.DrawLine(p11, p01); Gizmos.DrawLine(p01, p00);
                }
            }
            // образующие
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.5f);
            for (int c = 0; c < 4; c++)
            {
                float sx = (c & 1) == 0 ? -1f : 1f, sz = (c & 2) == 0 ? -1f : 1f;
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= 12; i++)
                {
                    float t = i / 12f;
                    Vector2 h = HalfAt(t);
                    Vector3 p = _shape == Shape.Cone ? PointAt(t, c < 2 ? sx * h.x : 0f, c >= 2 ? sz * h.x : 0f)
                                                     : PointAt(t, sx * h.x, sz * h.y);
                    if (i > 0) Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(BottomWorld, Mathf.Max(0.02f, _depth * 0.04f));
        }
    }
}
