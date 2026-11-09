#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Хлеб → Авто: бетономешалка / Духовка: подключить компоненты — настраивают выделенное САМИ:
    /// добавляют компоненты и ссылки. Префабы не создают — потом
    /// сохрани их руками (Overrides → Apply All или перетащи в Project).
    /// </summary>
    public static class CatCooksBreadAutoSetup
    {
        const string DefDir = "Assets/CatCooks/Food/Cookware";

        // ───────────────────────── БЕТОНОМЕШАЛКА ─────────────────────────

        [MenuItem("CatCooks/Хлеб/Авто: бетономешалка (выдели её)", priority = 90)]
        public static void SetupMixer()
        {
            var log = new StringBuilder();
            ConcreteMixer mixer = null;
            foreach (GameObject g in Selection.gameObjects)
            {
                mixer = g.GetComponentInParent<ConcreteMixer>();
                if (mixer == null) mixer = g.GetComponentInChildren<ConcreteMixer>();
                if (mixer != null) break;
            }
            if (mixer == null) { Dialog("Выдели бетономешалку (объект с Concrete Mixer) и нажми ещё раз."); return; }
            GameObject root = mixer.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(root, "Авто: бетономешалка");

            var mso = new SerializedObject(mixer);
            Transform drum = mso.FindProperty("_drum").objectReferenceValue as Transform;
            Transform pivot = mso.FindProperty("_barrelPivot").objectReferenceValue as Transform;
            Vector3 spin = mso.FindProperty("_drumSpinAxis").vector3Value;
            if (drum == null) { Dialog("У Concrete Mixer не задан Drum (меш барабана) — задай и нажми ещё раз."); return; }
            if (pivot == null) pivot = drum.parent != null ? drum.parent : root.transform;
            if (spin.sqrMagnitude < 1e-6f) spin = Vector3.forward;
            log.AppendLine($"Бочка: Drum «{drum.name}», Barrel Pivot «{pivot.name}», ось вращения {spin}");

            // вершины барабана в осях пивота
            List<Vector3> pts = CollectPoints(drum, pivot);
            if (pts.Count < 8) { Dialog($"У «{drum.name}» не нашёл меш (MeshFilter внутри Drum)."); return; }
            Vector3 axis = pivot.InverseTransformDirection(drum.TransformDirection(spin)).normalized;
            Vector3 c = Vector3.zero; foreach (Vector3 p in pts) c += p; c /= pts.Count;
            float tMin = float.MaxValue, tMax = float.MinValue;
            foreach (Vector3 p in pts) { float t = Vector3.Dot(p - c, axis); tMin = Mathf.Min(tMin, t); tMax = Mathf.Max(tMax, t); }
            float L = tMax - tMin;
            float rLo = 0f, rHi = 0f, rMid = 0f; int nLo = 0, nHi = 0, nMid = 0;
            foreach (Vector3 p in pts)
            {
                float t = Vector3.Dot(p - c, axis);
                float r = (p - c - axis * t).magnitude;
                if (t < tMin + L * 0.15f) { rLo += r; nLo++; }
                else if (t > tMax - L * 0.15f) { rHi += r; nHi++; }
                else if (Mathf.Abs(t - (tMin + tMax) * 0.5f) < L * 0.2f) { rMid += r; nMid++; }
            }
            rLo /= Mathf.Max(1, nLo); rHi /= Mathf.Max(1, nHi); rMid /= Mathf.Max(1, nMid);
            // горловина — конец, который в сцене смотрит ВВЕРХ (бетономешалка стоит горловиной кверху,
            // иначе тесто выпадало бы само); бочка лежит ровно — более узкий конец
            float sign;
            float yHi = pivot.TransformPoint(c + axis * tMax).y, yLo = pivot.TransformPoint(c + axis * tMin).y;
            float lenW = Vector3.Distance(pivot.TransformPoint(c + axis * tMax), pivot.TransformPoint(c + axis * tMin));
            if (Mathf.Abs(yHi - yLo) > 0.15f * lenW) sign = yHi > yLo ? 1f : -1f;
            else sign = Mathf.Abs(rLo - rHi) > 0.1f * Mathf.Max(rLo, rHi) ? (rHi < rLo ? 1f : -1f) : 1f;
            float tEnd = sign > 0 ? tMax : tMin;
            float wk = Mathf.Abs(pivot.lossyScale.x) > 1e-6f ? Mathf.Abs(pivot.lossyScale.x) : 1f;
            log.AppendLine($"Горловина — {(sign > 0 ? "+" : "−")} конец оси (радиусы концов {rLo * wk:0.00} / {rHi * wk:0.00} м, середина {rMid * wk:0.00} м, длина {L * wk:0.00} м; масштаб пивота ×{wk:0.##})");

            // Mouth
            Transform mouth = FindOrCreate(pivot, "Mouth");
            mouth.localPosition = c + axis * (tEnd - sign * L * 0.03f);
            mouth.localRotation = Quaternion.LookRotation(axis * sign, Perp(axis));
            // Receive
            Transform recT = FindOrCreate(pivot, "Receive");
            recT.localPosition = c - axis * sign * L * 0.05f;
            recT.localRotation = Quaternion.LookRotation(axis, Perp(axis));
            recT.localScale = Vector3.one;
            BoxCollider rec = Need<BoxCollider>(recT.gameObject);
            rec.isTrigger = true;
            rec.center = Vector3.zero;
            // ВСЁ ЗДЕСЬ — в единицах пивота. У модели бочки масштаб бывает ×100 (сантиметры) и ещё ×4 у корня:
            // минимум «0.1» в единицах пивота был 40 м в мире — триггер глотал всё вокруг.
            float pk = Mathf.Abs(pivot.lossyScale.x) > 1e-6f ? Mathf.Abs(pivot.lossyScale.x) : 1f;
            float side = Mathf.Max(0.1f / pk, Mathf.Min(rMid, Mathf.Max(rLo, rHi)) * 1.25f);
            rec.size = new Vector3(side, side, L * 0.7f);
            if (recT.GetComponent<CookwareTriggerRelay>() == null) Undo.AddComponent<CookwareTriggerRelay>(recT.gameObject);
            log.AppendLine($"＋ Receive (триггер {side * pk:0.00}×{side * pk:0.00}×{L * 0.7f * pk:0.00} м в мире) и Mouth — дети «{pivot.name}» (поворачиваются с бочкой, не крутятся)");

            // Cookware
            Cookware cw = Need<Cookware>(root);
            CookwareDef def = cw.Def != null ? cw.Def : EnsureDef("Cookware_Mixer", "Бетономешалка", 120, true, FoodProcess.None, true, log);
            var cso = new SerializedObject(cw);
            cso.FindProperty("Def").objectReferenceValue = def;
            cso.FindProperty("_mouth").objectReferenceValue = mouth;
            cso.FindProperty("_insideVolume").objectReferenceValue = rec;
            cso.ApplyModifiedProperties();
            log.AppendLine($"✔ Cookware: Def «{def.Title}» ({def.Capacity} порц.), Mouth, Inside Volume");

            // Mixer Dough
            MixerDough md = Need<MixerDough>(root);
            var dso = new SerializedObject(md);
            dso.FindProperty("_mouth").objectReferenceValue = mouth;
            ProductDef dough = FindProduct("Тесто дрожжевое");
            if (dough != null) dso.FindProperty("_dough").objectReferenceValue = dough;
            if (dso.FindProperty("_ballPrefab").objectReferenceValue == null)
            {
                GameObject ball = FindPrefabWith<DoughBlob>();
                if (ball != null) { dso.FindProperty("_ballPrefab").objectReferenceValue = ball; log.AppendLine($"✔ Ball Prefab = «{ball.name}»"); }
                else log.AppendLine("⚠ не нашёл префаб шара теста (с Dough Blob) — сделай DoughBall (гайд п. 4) и нажми ещё раз");
            }
            dso.ApplyModifiedProperties();
            if (dough == null) log.AppendLine("⚠ нет продукта «Тесто дрожжевое» — сначала CatCooks → Хлеб → 1");
            log.AppendLine("✔ Mixer Dough");

            // вода / тесто в бочке и горка муки — по форме барабана (оси и радиусы уже посчитаны)
            BuildMixerVisuals(root, pivot, pts, c, axis, sign, tMin, tMax, L, log);

            Done(root, log, "Сохрани бетономешалку (если она префаб — Overrides → Apply All).\n" +
                            "Проверка: повернул бочку ручкой — синяя ось Mouth (выдели его) должна смотреть из горловины наружу.");
        }

        // ───────────────────────── ДУХОВКА ─────────────────────────
        // Духовка — НЕ тара. Меню НИЧЕГО не перестраивает и не двигает: только убирает с корня
        // старые «тарные» компоненты, вешает нужные и подключает ссылки по именам детей
        // (Chamber, LidHinge, HatchHinge, Vent). Чего не нашло — пишет, что сделать руками.

        static readonly string[] OvenObsolete =
        {
            "CarriedContainer", "CookwareSpill", "PotLidSocket", "CookwarePressure",
            "CookwareLiquidVisual", "CookwareHeapVisual", "CookwareHeat", "Cookware",
        };

        [MenuItem("CatCooks/Хлеб/Духовка: подключить компоненты (выдели корень)", priority = 91)]
        public static void SetupOven()
        {
            var log = new StringBuilder();
            GameObject go = Selection.activeGameObject;
            if (go == null) { Dialog("Выдели КОРЕНЬ духовки — объект, под которым корпус, Chamber и LidHinge."); return; }
            BakeOven up = go.GetComponentInParent<BakeOven>();
            if (up != null && up.gameObject != go) { go = up.gameObject; log.AppendLine($"ℹ взял корень «{go.name}» (на нём Bake Oven)"); }
            Transform root = go.transform;
            Undo.RegisterFullObjectHierarchyUndo(go, "Духовка: компоненты");
            log.AppendLine($"Духовка «{go.name}»");

            // 1) духовка больше не тара — убрать тарное с корня (в порядке зависимостей)
            foreach (string tn in OvenObsolete)
                foreach (Component c in go.GetComponents<Component>())
                    if (c != null && c.GetType().Name == tn)
                    {
                        try { Undo.DestroyObjectImmediate(c); log.AppendLine($"－ убран {tn} (духовка не тара)"); }
                        catch (Exception e) { log.AppendLine($"⚠ не смог убрать {tn}: {e.Message} — убери руками (Remove Component)"); }
                    }

            // 2) нужное на корне
            AddByName(go, "NetworkObject", log);
            AddByName(go, "NetworkTransform", log);
            Rigidbody rb = Need<Rigidbody>(go);
            if (rb.mass < 5f) { rb.mass = 30f; log.AppendLine("✔ Rigidbody: Mass 30"); }
            BakeOven oven = Need<BakeOven>(go);
            OvenHingedLid lid = go.GetComponentInChildren<OvenHingedLid>(true);
            if (lid == null) { lid = Undo.AddComponent<OvenHingedLid>(go); log.AppendLine("＋ Oven Hinged Lid"); }
            OvenVentHatch vh = go.GetComponentInChildren<OvenVentHatch>(true);
            if (vh == null) { vh = Undo.AddComponent<OvenVentHatch>(go); log.AppendLine("＋ Oven Vent Hatch"); }

            // 3) полость
            Transform chamberT = FindDeep(root, "Chamber");
            Collider chamber = chamberT != null ? chamberT.GetComponent<Collider>() : null;
            if (chamber != null)
            {
                chamber.isTrigger = true;
                foreach (Component c in chamberT.GetComponents<Component>())
                    if (c != null && c.GetType().Name == "CookwareTriggerRelay") { Undo.DestroyObjectImmediate(c); log.AppendLine("－ убран Cookware Trigger Relay с Chamber"); break; }
                var so = new SerializedObject(oven);
                if (so.FindProperty("_chamber").objectReferenceValue == null)
                { so.FindProperty("_chamber").objectReferenceValue = chamber; so.ApplyModifiedProperties(); }
                log.AppendLine($"✔ Chamber «{chamberT.name}» — триггер полости");
            }
            else log.AppendLine("⚠ нет ребёнка «Chamber» с Box Collider — создай пустышку Chamber, Box Collider Is Trigger по пустоте внутри и укажи в Bake Oven → Chamber");

            // 4) крышка на петле
            Transform lidHinge = FindDeep(root, "LidHinge");
            Transform hatchHinge = FindDeep(root, "HatchHinge");
            if (lidHinge != null)
            {
                var so = new SerializedObject(lid);
                if (so.FindProperty("_hinge").objectReferenceValue == null)
                { so.FindProperty("_hinge").objectReferenceValue = lidHinge; so.ApplyModifiedProperties(); }
                Collider lc = FirstSolid(lidHinge, hatchHinge);
                if (lc == null) log.AppendLine("⚠ под LidHinge нет обычного коллайдера — повесь Box/Mesh(Convex) Collider на меш крышки, иначе F по ней не попасть");
                else if (lc.GetComponent<OvenLidPart>() == null) { Undo.AddComponent<OvenLidPart>(lc.gameObject); log.AppendLine($"＋ Oven Lid Part на «{lc.name}» (F — открыть/закрыть)"); }
                log.AppendLine("✔ крышка: Hinge = LidHinge. Ось и угол проверь: Oven Hinged Lid ⋮ → «Открыть/закрыть (проверка)»");
            }
            else log.AppendLine("⚠ нет пустышки «LidHinge» — создай её на линии петли и перетащи меш крышки внутрь");

            // 5) люк
            Transform vent = FindDeep(root, "Vent");
            {
                var so = new SerializedObject(vh);
                if (hatchHinge != null && so.FindProperty("_hatch").objectReferenceValue == null) so.FindProperty("_hatch").objectReferenceValue = hatchHinge;
                if (vent != null && so.FindProperty("_vent").objectReferenceValue == null) so.FindProperty("_vent").objectReferenceValue = vent;
                so.ApplyModifiedProperties();
            }
            if (hatchHinge != null)
            {
                Collider hc = FirstSolid(hatchHinge, null);
                if (hc == null) log.AppendLine("⚠ под HatchHinge нет коллайдера — повесь Box Collider на меш люка");
                else if (hc.GetComponent<OvenHatchPart>() == null) { Undo.AddComponent<OvenHatchPart>(hc.gameObject); log.AppendLine($"＋ Oven Hatch Part на «{hc.name}» (F — люк)"); }
                log.AppendLine("✔ люк: Hatch = HatchHinge" + (vent != null ? ", Vent = Vent" : ""));
            }
            else log.AppendLine("ℹ нет «HatchHinge» — люка не будет (можно без него: пар выходит только при открытой крышке)");

            Done(go, log,
                "Решётка — просто обычные коллайдеры внутри Chamber (тонкий Box Collider).\n" +
                "Духовка стоит ровно → Bake Oven ⋮ → «Запомнить позу».\n" +
                "Сохрани духовку (префаб — Overrides → Apply All).");
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        private static Collider FirstSolid(Transform under, Transform except)
        {
            foreach (Collider c in under.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                if (except != null && c.transform.IsChildOf(except)) continue;
                return c;
            }
            return null;
        }

        // ───────────────────────── помощники ─────────────────────────

        private static void Dialog(string t) => EditorUtility.DisplayDialog("Хлеб — автонастройка", t, "Ок");

        private static void Done(GameObject go, StringBuilder log, string tail)
        {
            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine("\n" + tail);
            Debug.Log("[Хлеб — авто]\n" + log, go);
            EditorUtility.DisplayDialog("Хлеб — автонастройка", log.ToString(), "Ок");
        }

        /// <summary>
        /// LiquidAnchor (вода, тесто) и HeapAnchor (мука, соль) внутри бочки: дети Barrel Pivot
        /// (наклоняются с бочкой, не крутятся), ось Y — от дна к горловине, профиль стенки — по мешу барабана.
        /// </summary>
        private static void BuildMixerVisuals(GameObject root, Transform pivot, List<Vector3> pts, Vector3 c, Vector3 axis, float sign,
                                              float tMin, float tMax, float L, StringBuilder log)
        {
            float k = Mathf.Abs(pivot.lossyScale.x) > 1e-5f ? Mathf.Abs(pivot.lossyScale.x) : 1f;   // локальные метры пивота → мир
            const int N = 10;
            float[] sum = new float[N]; int[] cnt = new int[N];
            foreach (Vector3 p in pts)
            {
                float t = Vector3.Dot(p - c, axis);
                float sAlong = sign > 0 ? t - tMin : tMax - t;            // 0 — дно, L — горловина
                int bin = Mathf.Clamp((int)(sAlong / Mathf.Max(1e-5f, L) * N), 0, N - 1);
                sum[bin] += (p - c - axis * t).magnitude; cnt[bin]++;
            }
            float[] prof = new float[N];
            float last = 0f;
            for (int i = 0; i < N; i++)
            {
                float r = cnt[i] > 0 ? sum[i] / cnt[i] : last;
                prof[i] = Mathf.Max(0.02f, r * 0.9f * k);                 // внутренняя стенка чуть уже меша
                if (cnt[i] > 0) last = r;
            }
            for (int i = 0; i < N; i++) if (cnt[i] == 0 && i + 1 < N && cnt[i + 1] > 0) prof[i] = prof[i + 1];
            float H = L * 0.95f * k;
            Vector3 bottom = c + axis * (sign > 0 ? tMin : tMax) + axis * sign * L * 0.03f;
            Vector3 up = axis * sign;
            Vector3 fwd = Vector3.ProjectOnPlane(Perp(axis), up).normalized;
            Quaternion rot = Quaternion.LookRotation(fwd, up);                   // Y — к горловине

            // ── жидкость ──
            Transform la = FindOrCreate(pivot, "LiquidAnchor");
            la.localPosition = bottom; la.localRotation = rot; la.localScale = Vector3.one;
            CookwareLiquidVisual lv = la.GetComponent<CookwareLiquidVisual>();
            if (lv == null) lv = Undo.AddComponent<CookwareLiquidVisual>(la.gameObject);
            var so = new SerializedObject(lv);
            so.FindProperty("_form").intValue = (int)CookwareLiquidVisual.Form.Round;
            so.FindProperty("_maxHeight").floatValue = H;
            so.FindProperty("_radiusBottom").floatValue = prof[0];
            so.FindProperty("_radiusTop").floatValue = prof[N - 1];
            SerializedProperty wp = so.FindProperty("_wallProfile");
            wp.arraySize = N;
            for (int i = 0; i < N; i++) wp.GetArrayElementAtIndex(i).floatValue = prof[i];
            so.FindProperty("_fillByCapacity").boolValue = true;
            so.FindProperty("_ignoreParentScale").boolValue = true;
            SerializedProperty arr = so.FindProperty("_products");
            AddLiquid(arr, FindProduct("Вода"), (int)CookwareLiquidVisual.Role.Liquid);
            AddLiquid(arr, FindProduct("Тесто дрожжевое"), (int)CookwareLiquidVisual.Role.Paste);
            so.ApplyModifiedProperties();
            log.AppendLine($"✔ LiquidAnchor в бочке: глубина {H:0.00} м, радиус {prof[0]:0.00}…{prof[N - 1]:0.00} м — вода и тесто видны, наклоняются с бочкой");

            // ── горка муки/соли ──
            Transform ha = FindOrCreate(pivot, "HeapAnchor");
            ha.localPosition = bottom; ha.localRotation = rot; ha.localScale = Vector3.one;
            FoodHeapRenderer fr = ha.GetComponent<FoodHeapRenderer>();
            if (fr == null) fr = Undo.AddComponent<FoodHeapRenderer>(ha.gameObject);
            if (ha.GetComponent<CookwareHeapVisual>() == null) Undo.AddComponent<CookwareHeapVisual>(ha.gameObject);
            var hso = new SerializedObject(fr);
            float rb = Mathf.Max(prof[0], prof[Mathf.Min(2, N - 1)]);
            SetIf(hso, "_shape", (int)FoodHeapRenderer.Shape.Fill);
            SetIf(hso, "_footprint", new Vector2(rb * 0.75f, rb * 0.75f));
            SetIf(hso, "_fillRect", false);
            SetIf(hso, "_maxHeight", H * 0.6f);
            SetIf(hso, "_preview", false);
            SetIf(hso, "_worldUpright", false);
            SetIf(hso, "_ignoreParentScale", true);
            hso.ApplyModifiedProperties();
            log.AppendLine("✔ HeapAnchor в бочке: мука и соль видны горкой на дне");
        }

        private static void AddLiquid(SerializedProperty arr, ProductDef p, int role)
        {
            if (p == null) return;
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue == p) return;
            arr.arraySize++;
            SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            e.FindPropertyRelative("Product").objectReferenceValue = p;
            e.FindPropertyRelative("Role").intValue = role;
            e.FindPropertyRelative("Color").colorValue = new Color(0f, 0f, 0f, 0f);
        }

        private static void SetIf(SerializedObject so, string name, int v) { SerializedProperty p = so.FindProperty(name); if (p != null) p.intValue = v; }
        private static void SetIf(SerializedObject so, string name, float v) { SerializedProperty p = so.FindProperty(name); if (p != null) p.floatValue = v; }
        private static void SetIf(SerializedObject so, string name, bool v) { SerializedProperty p = so.FindProperty(name); if (p != null) p.boolValue = v; }
        private static void SetIf(SerializedObject so, string name, Vector2 v) { SerializedProperty p = so.FindProperty(name); if (p != null) p.vector2Value = v; }

        private static Transform FindOrCreate(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>Компонент есть — вернуть, нет — добавить (в редакторе GetComponent возвращает «фальшивый null», ?? не работает).</summary>
        private static T Need<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            if (c == null) c = Undo.AddComponent<T>(go);
            return c;
        }

        private static Vector3 Perp(Vector3 a) => Mathf.Abs(a.y) < 0.9f ? Vector3.up : Vector3.forward;
        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>Вершины всех мешей под from (в осях space). Нечитаемый меш — углы его габаритов.</summary>
        private static List<Vector3> CollectPoints(Transform from, Transform space)
        {
            var list = new List<Vector3>();
            foreach (MeshFilter mf in from.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null) continue;
                Matrix4x4 mat = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Vector3[] v = null;
                try { v = m.vertices; } catch (Exception) { v = null; }
                if (v == null || v.Length == 0)
                {
                    Bounds b = m.bounds;
                    v = new Vector3[8];
                    for (int k = 0; k < 8; k++)
                        v[k] = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                }
                int step = Mathf.Max(1, v.Length / 4000);
                for (int i = 0; i < v.Length; i += step) list.Add(mat.MultiplyPoint3x4(v[i]));
            }
            return list;
        }

        /// <summary>Габариты мешей под go в осях space; except (и его дети) не считаются, если skipExcept.</summary>
        private static Bounds LocalBounds(Transform space, GameObject go, Transform except, bool includeExcept)
        {
            bool any = false;
            Bounds b = new Bounds();
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (!includeExcept && except != null && mf.transform.IsChildOf(except)) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 mat = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = mat.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static float Volume(GameObject g)
        {
            bool any = false; Bounds b = new Bounds();
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
            { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return any ? b.size.x * b.size.y * b.size.z : 0f;
        }

        private static GameObject FindChildByName(Transform t, params string[] keys)
        {
            foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
            {
                if (c == t) continue;
                string n = c.name.ToLowerInvariant();
                foreach (string k in keys) if (n.Contains(k)) return c.gameObject;
            }
            return null;
        }

        private static Transform CommonAncestor(Transform a, Transform b)
        {
            for (Transform x = a; x != null; x = x.parent)
                if (b == x || b.IsChildOf(x)) return x;
            return null;
        }

        /// <summary>Полая коробка из 5 плоских Box Collider: дно (или верх) и 4 стенки.</summary>
        private static void ShellBoxes(Transform space, string name, Bounds b, bool floor, bool roof, StringBuilder log)
        {
            Transform holder = space.Find(name);
            if (holder != null) Undo.DestroyObjectImmediate(holder.gameObject);
            holder = FindOrCreate(space, name);
            holder.localPosition = Vector3.zero; holder.localRotation = Quaternion.identity; holder.localScale = Vector3.one;
            Vector3 c = b.center, e = b.extents;
            float sk = Mathf.Abs(space.lossyScale.x) > 1e-6f ? Mathf.Abs(space.lossyScale.x) : 1f;   // 1 см в мире — в единицах space
            float t = Mathf.Max(0.01f / sk, Mathf.Min(e.x, Mathf.Min(e.y, e.z)) * 0.08f);
            if (floor) ShellBox(holder, "Floor", new Vector3(c.x, c.y - e.y + t * 0.5f, c.z), new Vector3(e.x * 2f, t, e.z * 2f));
            if (roof) ShellBox(holder, "Roof", new Vector3(c.x, c.y + e.y - t * 0.5f, c.z), new Vector3(e.x * 2f, t, e.z * 2f));
            ShellBox(holder, "WallXMin", new Vector3(c.x - e.x + t * 0.5f, c.y, c.z), new Vector3(t, e.y * 2f, e.z * 2f));
            ShellBox(holder, "WallXMax", new Vector3(c.x + e.x - t * 0.5f, c.y, c.z), new Vector3(t, e.y * 2f, e.z * 2f));
            ShellBox(holder, "WallZMin", new Vector3(c.x, c.y, c.z - e.z + t * 0.5f), new Vector3(e.x * 2f, e.y * 2f, t));
            ShellBox(holder, "WallZMax", new Vector3(c.x, c.y, c.z + e.z - t * 0.5f), new Vector3(e.x * 2f, e.y * 2f, t));
            log.AppendLine($"＋ {name}: полые стенки из коробок (толщина {t * sk:0.00} м)");
        }

        private static void ShellBox(Transform holder, string n, Vector3 center, Vector3 size)
        {
            var go = new GameObject(n);
            Undo.RegisterCreatedObjectUndo(go, n);
            go.transform.SetParent(holder, false);
            go.transform.localPosition = center;
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = size;
        }

        /// <summary>Сплошной коллайдер, внутри которого центр полости, — выключить (он забил бы полость).</summary>
        private static void DisableSolidInside(GameObject go, Transform except, Vector3 worldCenter, StringBuilder log)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || !c.enabled) continue;
                if (except != null && c.transform.IsChildOf(except)) continue;
                if (c.transform.parent != null && (c.transform.parent.name == "OvenColliders" || c.transform.parent.name == "LidColliders")) continue;
                if (!c.bounds.Contains(worldCenter)) continue;
                Undo.RecordObject(c, "выключить сплошной коллайдер");
                c.enabled = false;
                log.AppendLine($"ℹ выключил сплошной коллайдер «{c.name}» ({c.GetType().Name}) — он заполнял полость; вместо него — стенки");
            }
        }

        private static bool HasSolidCollider(GameObject go, Transform except)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                if (except != null && c.transform.IsChildOf(except)) continue;
                return true;
            }
            return false;
        }

        private static void Unpack(GameObject go, StringBuilder log)
        {
            GameObject outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (outer == null) return;
            PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);
            log.AppendLine($"ℹ «{outer.name}» был префабом — распакован (крышку надо вынести отдельно), потом сохрани заново");
        }

        /// <summary>Добавить компонент по имени типа (FishNet NetworkObject / NetworkTransform), если его нет.</summary>
        private static void AddByName(GameObject go, string typeName, StringBuilder log)
        {
            foreach (Component c in go.GetComponents<Component>()) if (c != null && c.GetType().Name == typeName) return;
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                foreach (Type x in types)
                    if (x != null && x.Name == typeName && typeof(Component).IsAssignableFrom(x) && !x.IsAbstract && x.Namespace != null && x.Namespace.StartsWith("FishNet")) { t = x; break; }
                if (t != null) break;
            }
            if (t == null) { log.AppendLine($"⚠ не нашёл тип {typeName} — добавь руками на «{go.name}»"); return; }
            Undo.AddComponent(go, t);
            log.AppendLine($"＋ {typeName} на «{go.name}»");
        }

        private static CookwareDef EnsureDef(string file, string title, int capacity, bool absorbs, FoodProcess heat, bool needsHeat, StringBuilder log)
        {
            string path = $"{DefDir}/{file}.asset";
            CookwareDef d = AssetDatabase.LoadAssetAtPath<CookwareDef>(path);
            if (d != null) return d;
            string[] parts = DefDir.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++) { string nx = cur + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(nx)) AssetDatabase.CreateFolder(cur, parts[i]); cur = nx; }
            d = ScriptableObject.CreateInstance<CookwareDef>();
            d.Title = title; d.Capacity = capacity; d.Absorbs = absorbs; d.HeatProcess = heat; d.NeedsHeatSource = needsHeat;
            AssetDatabase.CreateAsset(d, path);
            AssetDatabase.SaveAssets();
            log.AppendLine($"＋ тара {path} («{title}», {capacity} порц.)");
            return d;
        }

        private static ProductDef FindProduct(string title)
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.Title == title) return d;
            }
            return null;
        }

        private static GameObject FindPrefabWith<T>() where T : Component
        {
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (go != null && go.GetComponent<T>() != null) return go;
            }
            return null;
        }
    }
}
#endif
