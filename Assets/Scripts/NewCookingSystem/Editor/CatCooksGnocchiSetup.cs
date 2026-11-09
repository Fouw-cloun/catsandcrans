#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;

namespace CatCooks.Food
{
    /// <summary>
    /// НЬОККИ, этап 1: каток и рулон гидроизоляции. Префабы НЕ создаём — только компоненты на выделенном / новом объекте в сцене.
    ///   1. Каток — выдели свою модель катка (корень). Найдёт вальцы, вилку, сиденье, руль по именам
    ///      (roller base / roller / roller.001 / seat / wheel), поставит подвеску, сиденье, камеру, сеть.
    ///   2. Рулон гидроизоляции — сделает рулон в сцене (свой меш) или поставит компоненты на выделенную модель
    ///      (у неё нужен ребёнок «Roll» с осью вдоль X).
    ///   3. Проверить.
    /// </summary>
    public static class CatCooksGnocchiSetup
    {
        private const string Root = "CatCooks/Ньокки/";
        private const string Dir = "Assets/Models/Gnocchi";

        // ───────────────────────── 1. КАТОК ─────────────────────────

        [MenuItem(Root + "1. Каток (выдели модель катка)", priority = 180)]
        public static void SetupRoller()
        {
            var log = new StringBuilder();
            GameObject go = Selection.activeGameObject;
            if (go == null || EditorUtility.IsPersistent(go)) { EditorUtility.DisplayDialog("Каток", "Выдели в СЦЕНЕ корень модели катка (перетащи модель в сцену).", "Ок"); return; }
            Undo.RegisterFullObjectHierarchyUndo(go, "Каток");
            Transform root = go.transform;

            Transform fork = FindExact(root, "roller base");
            Transform front = fork != null ? FindExact(fork, "roller") : null;
            Transform rear = FindExact(root, "roller.001");
            Transform seat = FindContains(root, "seat");
            Transform steer = FindExact(root, "wheel");
            if (front == null || rear == null)
            {
                // запасной путь: два объекта с «roller» (не base) — передний тот, что дальше по Z
                var drums = new List<Transform>();
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (t != root && n.Contains("roller") && !n.Contains("base") && t.GetComponent<MeshFilter>() != null) drums.Add(t);
                    else if (t != root && (n.Contains("drum") || n.Contains("валец") || n.Contains("вальц")) && t.GetComponent<MeshFilter>() != null) drums.Add(t);
                }
                drums.Sort((a, b) => root.InverseTransformPoint(Center(b)).z.CompareTo(root.InverseTransformPoint(Center(a)).z));
                if (drums.Count >= 2) { front = drums[0]; rear = drums[drums.Count - 1]; }
                if (fork == null && front != null && front.parent != root) fork = front.parent;
            }
            if (front == null || rear == null) { EditorUtility.DisplayDialog("Каток", "Не нашёл два вальца. Назови их «roller» (передний, внутри «roller base») и «roller.001» (задний).", "Ок"); return; }

            Bounds bf = BoundsIn(root, front), br = BoundsIn(root, rear), all = DS.LocalBounds(root);
            if (bf.center.z < br.center.z)
                log.AppendLine("⚠ передний валец оказался СЗАДИ по оси Z — каток поедет задом. Поверни корень на 180° по Y (или модель внутри) и нажми меню ещё раз");
            float radius = Mathf.Max(bf.size.y, bf.size.z) * 0.5f;
            float width = bf.size.x;
            float scale = Mathf.Abs(root.lossyScale.x) > 1e-4f ? Mathf.Abs(root.lossyScale.x) : 1f;
            float rWorld = radius * scale;
            log.AppendLine($"Вальцы: «{front.name}» (перед) и «{rear.name}» (зад), радиус {rWorld:0.00} м, ширина {width * scale:0.00} м; вилка «{(fork != null ? fork.name : "—")}», сиденье «{(seat != null ? seat.name : "—")}», руль «{(steer != null ? steer.name : "—")}»");

            // Rigidbody, сеть
            Rigidbody rb = DS.Need<Rigidbody>(go);
            float mass = Mathf.Clamp(900f * Mathf.Pow(rWorld / 0.35f, 3f), 300f, 6000f);
            rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            ClientAuthNT(go, log);

            // корпус — над осями вальцов (лучи подвески из-под него)
            BoxCollider box = DS.Need<BoxCollider>(go);
            float yAxle = (bf.center.y + br.center.y) * 0.5f;
            Vector3 mn = all.min, mx = all.max; mn.y = yAxle;
            box.center = (mn + mx) * 0.5f; box.size = mx - mn;

            RoadRollerVehicle v = DS.Need<RoadRollerVehicle>(go);
            Vector3 sAxis = Vector3.forward, sPivot = Vector3.zero;
            if (steer != null && SteeringAxis(steer, out sAxis, out sPivot))
                log.AppendLine($"Руль «{steer.name}»: ось {sAxis.x:0.00}, {sAxis.y:0.00}, {sAxis.z:0.00} (по ободу), центр обода {sPivot.x:0.000}, {sPivot.y:0.000}, {sPivot.z:0.000}");
            v.EditorSetup(fork, front, rear, steer, rWorld, width * scale, sAxis, sPivot);

            // подвеска: по 2 точки на валец
            float rest = radius * 0.55f;
            Transform anchors = DS.FindOrCreate(root, "WheelAnchors");
            anchors.localPosition = Vector3.zero; anchors.localRotation = Quaternion.identity; anchors.localScale = Vector3.one;
            var pts = new (string n, Vector3 p, bool steerW, bool motor)[]
            {
                ("FL", bf.center + new Vector3(-width * 0.35f, rest * 0.5f, 0f), true, false),
                ("FR", bf.center + new Vector3( width * 0.35f, rest * 0.5f, 0f), true, false),
                ("RL", br.center + new Vector3(-width * 0.35f, rest * 0.5f, 0f), false, true),
                ("RR", br.center + new Vector3( width * 0.35f, rest * 0.5f, 0f), false, true),
            };
            var so = new SerializedObject(v);
            SerializedProperty wheels = so.FindProperty("wheels");
            wheels.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                Transform a = DS.FindOrCreate(anchors, "Anchor_" + pts[i].n);
                a.localPosition = pts[i].p; a.localRotation = Quaternion.identity;
                SerializedProperty w = wheels.GetArrayElementAtIndex(i);
                w.FindPropertyRelative("anchor").objectReferenceValue = a;
                w.FindPropertyRelative("visual").objectReferenceValue = null;     // вальцы крутит сам каток
                w.FindPropertyRelative("steering").boolValue = pts[i].steerW;
                w.FindPropertyRelative("motorized").boolValue = pts[i].motor;
            }
            float restW = rest * scale, rW = rWorld;
            float k = mass * 9.81f / (4f * restW * 0.5f);
            DS.SetF(so, "restLength", restW);
            DS.SetF(so, "wheelRadius", rW);
            DS.SetF(so, "springStrength", k);
            DS.SetF(so, "springDamper", 2f * Mathf.Sqrt(k * mass / 4f) * 0.6f);
            DS.SetF(so, "motorForce", mass * 2.2f / 2f);
            DS.SetF(so, "reverseFactor", 0.8f);
            DS.SetF(so, "maxSteerAngle", 30f);
            DS.SetF(so, "steerSpeed", 3f);
            DS.SetF(so, "tireGrip", 0.95f);
            DS.SetF(so, "engineBrakeDeceleration", 3f);
            DS.SetObj(so, "_body", rb);
            DS.SetV3(so, "_centerOfMass", new Vector3(0f, yAxle, (bf.center.z + br.center.z) * 0.5f));

            // сиденье, выход, камера
            Bounds bs = seat != null ? BoundsIn(root, seat) : new Bounds(new Vector3(0f, all.max.y, all.center.z), Vector3.zero);
            Transform sp = DS.FindOrCreate(root, "SeatPoint");
            sp.localPosition = new Vector3(bs.center.x, bs.max.y, bs.center.z); sp.localRotation = Quaternion.identity;
            Transform ep = DS.FindOrCreate(root, "ExitPoint");
            float groundY = Mathf.Min(bf.min.y, br.min.y);                            // низ вальцов = земля
            ep.localPosition = new Vector3(all.min.x - Mathf.Max(0.6f, all.size.x * 0.6f), groundY + 0.15f, bs.center.z); ep.localRotation = Quaternion.identity;
            Transform cam = DS.FindOrCreate(root, "RollerCamera");
            cam.localPosition = sp.localPosition + Vector3.up * Mathf.Max(0.5f, all.size.y * 0.6f);   // глаза водителя (в игре — по росту игрока)
            cam.localRotation = Quaternion.identity;
            Camera c = DS.Need<Camera>(cam.gameObject);
            c.nearClipPlane = 0.05f; c.fieldOfView = 65f;
            cam.gameObject.SetActive(false);
            DS.SetObj(so, "_seatPoint", sp);
            DS.SetObj(so, "_exitPoint", ep);
            DS.SetObj(so, "_vehicleCamera", cam.gameObject);
            so.ApplyModifiedProperties();
            Record(v);

            log.AppendLine($"Подвеска: 4 точки (перед поворотный, зад ведущий), ход {restW:0.00} м, пружина {k:0}, масса {mass:0} кг, скорость до 2.2 м/с");
            log.AppendLine("Корпус: Box Collider над осями вальцов · сиденье SeatPoint · выход ExitPoint (слева) · камера RollerCamera — глаза водителя (вид от первого лица, мышью оглядываться)");
            log.AppendLine("\nДАЛЬШЕ РУКАМИ:\n1. Перетащи каток в Project — префаб. Network Manager → Spawnable Prefabs.\n" +
                           "2. Play: F по катку — сесть, WASD — ехать, F — выйти.\n" +
                           "3. Камера не там — подвинь RollerCamera; садишься не туда — SeatPoint.");
            EditorSceneManager.MarkSceneDirty(go.scene);
            DS.Done("Каток", log);
        }

        // ───────────────────────── 2. РУЛОН ─────────────────────────

        [MenuItem(Root + "2. Рулон гидроизоляции (выдели модель или сделаю)", priority = 181)]
        public static void SetupRoll()
        {
            var log = new StringBuilder();
            float sc = MeatCutSettings.WorldScale * 0.5f;           // под крупный мир (как мясо), ×2 при масштабе 4
            float width = 1f * sc, outer = 0.14f * sc, core = 0.03f * sc, length = 15f * sc;
            GameObject go = Selection.activeGameObject;
            if (go != null && EditorUtility.IsPersistent(go)) go = null;
            Transform roll = go != null ? go.transform.Find("Roll") : null;
            if (go == null || roll == null)
            {
                go = FindRootByName("WaterproofRoll");
                if (go == null)
                {
                    go = new GameObject("WaterproofRoll");
                    Undo.RegisterCreatedObjectUndo(go, "Рулон");
                    go.transform.position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                    log.AppendLine("＋ «WaterproofRoll» — свой рулон (модели с ребёнком «Roll» не выделено)");
                }
                else log.AppendLine("✔ «WaterproofRoll» уже в сцене — обновлён");
                roll = BuildRollMesh(go.transform, width, outer, core, log);
            }
            else log.AppendLine($"✔ «{go.name}»: твоя модель, ребёнок «Roll» (ось вдоль X)");
            Undo.RegisterFullObjectHierarchyUndo(go, "Рулон");

            Material mat = SheetMat(log);
            Rigidbody rb = DS.Need<Rigidbody>(go);
            rb.mass = 25f * sc; rb.interpolation = RigidbodyInterpolation.Interpolate;
            CapsuleCollider cap = DS.Need<CapsuleCollider>(go);
            cap.direction = 0; cap.radius = outer; cap.height = width + outer * 2f; cap.center = Vector3.zero;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            ClientAuthNT(go, log);
            WaterproofRollItem item = DS.Need<WaterproofRollItem>(go);
            item.EditorSetup(width, length, outer, core, roll, mat);
            foreach (Component pc in go.GetComponents<Component>())
                if (pc != null && pc.GetType() == typeof(PickupItem)) { Undo.DestroyObjectImmediate(pc); log.AppendLine("  － лишний Pickup Item"); }

            log.AppendLine($"Рулон: полотно {width:0.0} м шириной, {length:0} м в рулоне, радиус {outer:0.00} м · материал {mat.name}");
            log.AppendLine("\nКАК: в руке ЛКМ — бросить на пол и катить (иди вперёд), ПКМ держать — отрезать, F — отпустить / взять.\n" +
                           "Рядом с полосой бросил — новая встанет встык, как обои.");
            log.AppendLine("\nДАЛЬШЕ РУКАМИ: перетащи рулон в Project — префаб, Network Manager → Spawnable Prefabs. Слой — как у других предметов в руке.");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            DS.Done("Рулон гидроизоляции", log);
        }

        private static Transform BuildRollMesh(Transform root, float width, float outer, float core, StringBuilder log)
        {
            Transform roll = DS.FindOrCreate(root, "Roll");
            roll.localPosition = Vector3.zero; roll.localRotation = Quaternion.identity; roll.localScale = Vector3.one;
            Mesh m = DS.SaveMesh(Cylinder(width, outer, 0f, 36, true), "Gnocchi_Roll", log);
            DS.Need<MeshFilter>(roll.gameObject).sharedMesh = m;
            DS.Need<MeshRenderer>(roll.gameObject).sharedMaterial = SheetMat(log);
            Transform c = DS.FindOrCreate(root, "Core");
            c.localPosition = Vector3.zero; c.localRotation = Quaternion.identity; c.localScale = Vector3.one;
            Mesh cm = DS.SaveMesh(Cylinder(width * 1.04f, core, core * 0.75f, 20, true), "Gnocchi_RollCore", log);
            DS.Need<MeshFilter>(c.gameObject).sharedMesh = cm;
            DS.Need<MeshRenderer>(c.gameObject).sharedMaterial = CoreMat(log);
            return roll;
        }

        /// <summary>Цилиндр вдоль X: длина len, радиус r, дырка inner (0 — сплошные торцы). UV: по окружности × по длине.</summary>
        private static Mesh Cylinder(float len, float r, float inner, int seg, bool caps)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float h = len * 0.5f;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 d = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                v.Add(new Vector3(-h, 0f, 0f) + d * r); n.Add(d); uv.Add(new Vector2(0f, i / (float)seg * 3f));
                v.Add(new Vector3(h, 0f, 0f) + d * r); n.Add(d); uv.Add(new Vector2(1f, i / (float)seg * 3f));
            }
            for (int i = 0; i < seg; i++) { int a = i * 2; t.Add(a); t.Add(a + 2); t.Add(a + 1); t.Add(a + 1); t.Add(a + 2); t.Add(a + 3); }
            if (caps)
                for (int s = 0; s < 2; s++)
                {
                    float x = s == 0 ? -h : h; Vector3 nn = s == 0 ? Vector3.left : Vector3.right;
                    int b = v.Count;
                    for (int i = 0; i <= seg; i++)
                    {
                        float a = i / (float)seg * Mathf.PI * 2f;
                        Vector3 d = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                        v.Add(new Vector3(x, 0f, 0f) + d * r); n.Add(nn); uv.Add(new Vector2(0.95f, 0.5f));
                        v.Add(new Vector3(x, 0f, 0f) + d * inner); n.Add(nn); uv.Add(new Vector2(0.5f, 0.5f));
                    }
                    for (int i = 0; i < seg; i++)
                    {
                        int a = b + i * 2;
                        if (s == 0) { t.Add(a); t.Add(a + 1); t.Add(a + 2); t.Add(a + 1); t.Add(a + 3); t.Add(a + 2); }
                        else { t.Add(a); t.Add(a + 2); t.Add(a + 1); t.Add(a + 1); t.Add(a + 2); t.Add(a + 3); }
                    }
                }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            // торцы — внешняя сторона: развернуть, если треугольник смотрит внутрь
            int[] tr = m.triangles; Vector3[] vv = m.vertices; Vector3[] nr = m.normals;
            for (int i = 0; i + 2 < tr.Length; i += 3)
            {
                Vector3 c = Vector3.Cross(vv[tr[i + 1]] - vv[tr[i]], vv[tr[i + 2]] - vv[tr[i]]);
                if (Vector3.Dot(c, nr[tr[i]]) < 0f) { int x = tr[i + 1]; tr[i + 1] = tr[i + 2]; tr[i + 2] = x; }
            }
            m.triangles = tr;
            return m;
        }

        private static Material SheetMat(StringBuilder log)
        {
            DS.EnsureDir(Dir);
            string path = Dir + "/Mat_Waterproof.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { name = "Mat_Waterproof" };
            Texture2D tex = WaterproofRollItem.SheetTexture();
            AssetDatabase.CreateAsset(tex, Dir + "/Tex_Waterproof.asset");
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ {path} (тёмно-серое полотно с посыпкой и кромкой)");
            return m;
        }

        private static Material CoreMat(StringBuilder log)
        {
            DS.EnsureDir(Dir);
            string path = Dir + "/Mat_RollCore.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { name = "Mat_RollCore" };
            m.SetColor("_BaseColor", new Color(0.62f, 0.48f, 0.32f)); m.color = new Color(0.62f, 0.48f, 0.32f);
            m.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ {path} (картонная втулка)");
            return m;
        }

        // ───────────────────────── 4. ТЕСТО ─────────────────────────

        [MenuItem(Root + "4. Ком теста в сцену — ньокки", priority = 183)]
        public static void MakeDough() => MakeDough(DoughKind.Gnocchi, "GnocchiDough");

        [MenuItem(Root + "4б. Ком теста в сцену — пельмени", priority = 183)]
        public static void MakeDoughPelmeni() => MakeDough(DoughKind.Pelmeni, "PelmeniDough");

        private static void MakeDough(DoughKind kind, string objName)
        {
            var log = new StringBuilder();
            GameObject go = FindRootByName(objName);
            bool fresh = go == null;
            if (fresh)
            {
                go = new GameObject(objName);
                Undo.RegisterCreatedObjectUndo(go, "Тесто");
                go.transform.position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            }
            else Undo.RegisterFullObjectHierarchyUndo(go, "Тесто");
            go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            DS.Need<MeshFilter>(go);
            MeshRenderer mr = DS.Need<MeshRenderer>(go);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Bread/Mat_Dough.mat");      // как у хлебного теста
            if (mat == null) mat = DS.FoodHeapMat("Mat_GnocchiDough", null, 4f, 0.08f, 0.9f, 0.2f, 0.003f, log);
            mr.sharedMaterial = mat;
            DS.Need<MeshCollider>(go);
            foreach (Rigidbody rb in go.GetComponents<Rigidbody>()) Undo.DestroyObjectImmediate(rb);
            DS.AddByName(go, "NetworkObject", log);
            DoughSheet ds = DS.Need<DoughSheet>(go);
            ProductDef flour = DS.Find("Мука пшеничная") ?? DS.Find("Мука");
            ds.EditorSetup(flour, kind);
            log.AppendLine($"{(fresh ? "＋" : "✔ обновлён")} «{objName}» — ком теста «{DoughSheet.KindTitle(kind)}» (плющится в лепёшку, раскатывается в пласт). Материал {(mat != null ? mat.name : "—")}");
            log.AppendLine(kind == DoughKind.Gnocchi
                ? "Ньокки: толстое жёлтое комковатое, цель 11–17 см, каток тоньше 9 см не раскатает."
                : "Пельмени: тонкое тугое гладкое, ком меньше, цель 3–5 см, каток тоньше 2 см не раскатает.");
            log.AppendLine("Тип теста можно сменить в Dough Sheet → «Тип теста» — поля выставятся сами («Своё» — руками).");
            log.AppendLine("Поставь его над подстилкой — в Play упадёт и сядет на неё. Объект сцены — для теста префаб не нужен.");
            log.AppendLine("\nКАК: кувалда — сплющить · каток — раскатать. Мука — на инструменты (вальцы, кувалда), тесто держи на подстилке.");
            log.AppendLine("Тест-меню (ПКМ по заголовку Dough Sheet, Play): «Тест: снова шар», «Тест: сбросить грязь и прилипания».");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            DS.Done("Тесто — " + DoughSheet.KindTitle(kind), log);
        }

        // ───────────────────────── 3. ПРОВЕРКА ─────────────────────────

        [MenuItem(Root + "5. Мука на инструмент (выдели кувалду)", priority = 184)]
        public static void SetupFlourCoat()
        {
            var sb = new StringBuilder();
            GameObject[] sel = Selection.gameObjects;
            if (sel.Length == 0) { DS.Done("Ньокки — мука на инструмент", new StringBuilder("✖ Выдели в сцене кувалду (или другой инструмент, которым бьют тесто).\nШпателям не нужно — они не липнут.")); return; }
            foreach (GameObject go in sel)
            {
                bool had = go.GetComponent<CatCooks.Food.Tools.ToolFlourCoat>() != null;
                var coat = DS.Need<CatCooks.Food.Tools.ToolFlourCoat>(go);
                // боёк: поле _head у кувалды, если есть
                Transform head = null;
                foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
                {
                    if (mb == null || mb == coat) continue;
                    SerializedProperty hp = new SerializedObject(mb).FindProperty("_head");
                    if (hp != null && hp.objectReferenceValue is Transform t) { head = t; break; }
                }
                string title = go.GetComponent<CatCooks.Food.Tools.SledgehammerItem>() != null ? "кувалда" : go.name;
                Undo.RecordObject(coat, "Мука на инструмент");
                coat.EditorSetup(title, head);
                Record(coat);
                bool net = HasType(go, "NetworkObject");
                sb.AppendLine($"{(net ? "✔" : "✖")} «{go.name}»: Tool Flour Coat {(had ? "уже был — обновлён" : "добавлен")}, «{title}», налипшее висит на {(head != null ? $"«{head.name}» (боёк)" : "центре инструмента (поле Stick Point можно задать)")}" + (net ? "" : " — НЕТ Network Object!"));
            }
            sb.AppendLine("\nВ игре: положи кувалду, возьми мастерком муку (ЛКМ по мешку) и кинь на кувалду (ПКМ) — ляжет налётом, кувалда побелеет.");
            sb.AppendLine("Без муки удар слабее и тесто ПРИЛИПАЕТ к бойку (качество падает). Налипшее — шпателем (ЛКМ по кувалде).");
            sb.AppendLine("Если кувалда — префаб: сохрани префаб (Overrides → Apply), иначе у заспавненных компонента не будет.");
            DS.Done("Ньокки — мука на инструмент", sb);
        }

        [MenuItem(Root + "6. Заготовка куска теста (для нарезки)", priority = 185)]
        public static void MakePieceTemplate()
        {
            var log = new StringBuilder();
            GameObject go = FindRootByName("DoughPieceTemplate");
            bool fresh = go == null;
            if (fresh)
            {
                go = new GameObject("DoughPieceTemplate");
                Undo.RegisterCreatedObjectUndo(go, "Кусок теста");
                go.transform.position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            }
            else Undo.RegisterFullObjectHierarchyUndo(go, "Кусок теста");
            go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            // старая заготовка-брусок (v3.8–3.9) — убрать лишнее
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                string t = c.GetType().Name;
                if (t == "DoughLog" || t == "NetworkTransform" || c is Rigidbody || c is BoxCollider) { Undo.DestroyObjectImmediate(c); log.AppendLine($"  − убрал {t} (от старой заготовки)"); }
            }
            DS.Need<MeshFilter>(go);
            MeshRenderer mr = DS.Need<MeshRenderer>(go);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Bread/Mat_Dough.mat");
            if (mat == null) mat = DS.FoodHeapMat("Mat_GnocchiDough", null, 4f, 0.08f, 0.9f, 0.2f, 0.003f, log);
            mr.sharedMaterial = mat;
            DS.Need<MeshCollider>(go);
            DS.AddByName(go, "NetworkObject", log);
            DoughSheet ds = DS.Need<DoughSheet>(go);
            ds.EditorSetupPieceTemplate();
            log.AppendLine($"{(fresh ? "＋" : "✔ обновлён")} «DoughPieceTemplate» — заготовка куска: такое же тесто (Dough Sheet), только появляется пустым — клетки ему отдаёт пласт при резе.");
            log.AppendLine("Кусок на вид не меняется: клетки копируются один в один, просто становятся отдельным объектом. Без Rigidbody — стабильно.");
            log.AppendLine("\nДАЛЬШЕ (руками):");
            log.AppendLine("  1. Перетащи «DoughPieceTemplate» из сцены в Project — префаб. Из сцены потом удали. (Старый префаб куска v3.8 можно удалить.)");
            log.AppendLine("  2. Network Manager → Spawnable Prefabs — добавь этот префаб.");
            log.AppendLine("  3. Выдели префаб в Project → CatCooks → Ньокки → 7 (назначит всем тестам в сцене).");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            DS.Done("Ньокки — заготовка куска", log);
        }

        [MenuItem(Root + "7. Назначить выделенный префаб куска всем тестам", priority = 186)]
        public static void AssignPiecePrefab()
        {
            var log = new StringBuilder();
            GameObject pf = Selection.activeObject as GameObject;
            DoughSheet pds = pf != null ? pf.GetComponent<DoughSheet>() : null;
            if (pf == null || !EditorUtility.IsPersistent(pf) || pds == null || !pds.IsPieceTemplate)
            {
                DS.Done("Ньокки — префаб куска", new StringBuilder("✖ Выдели в Project ПРЕФАБ куска — сделай его меню 6 и перетащи в Project (на нём Dough Sheet с галкой Start Empty)."));
                return;
            }
            var sheets = Object.FindObjectsByType<DoughSheet>(FindObjectsSortMode.None);
            int k = 0;
            foreach (DoughSheet d in sheets) { if (d.IsPieceTemplate) continue; d.EditorSetPiecePrefab(pf); k++; log.AppendLine($"✔ «{d.name}»: Piece Prefab = «{pf.name}»"); }
            if (k == 0) log.AppendLine("✖ В сцене нет теста (Dough Sheet) — меню 4 / 4б");
            log.AppendLine("\nНе забудь: этот префаб — в Network Manager → Spawnable Prefabs.");
            if (sheets.Length > 0) EditorSceneManager.MarkSceneDirty(sheets[0].gameObject.scene);
            DS.Done("Ньокки — префаб куска", log);
        }

        [MenuItem(Root + "8. Продукт «Ньокки» (для варки)", priority = 187)]
        public static void MakeGnocchiProduct()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef sub = DS.FindGroup("Group_SubRecipe") ?? DS.GroupOf("Меренга") ?? DS.GroupOf("Бульон");
            ProductDef g = DS.Ensure(GnocchiProduct.Title, sub, new Color(0.96f, 0.84f, 0.5f), 0.17f, true, dir, ref next, log);
            if (g != null)
            {
                if (g.DebrisColor.a < 0.01f) g.DebrisColor = new Color(0.96f, 0.84f, 0.5f, 1f);
                g.Solid = false;
                EditorUtility.SetDirty(g);
            }
            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("✔ FoodDatabase в сцене пересобран (сохрани сцену)");
            }
            else log.AppendLine("⚠ в открытой сцене нет FoodDatabase — нажми у него «Collect From Project»");
            log.AppendLine("\nКАК ВАРИТЬ:");
            log.AppendLine("  1. Кастрюля с водой — на огонь, довести до кипения (лог «[Нагрев] … ЗАКИПЕЛО»).");
            log.AppendLine("  2. Лопата ЛКМ по нарезанной полосе — берёт ньокки с конца (до 6 за раз). ПКМ по кастрюле — в воду.");
            log.AppendLine($"  3. Ждать: через ~{GnocchiBoiler.FloatAt:0} с кипения ВСПЛЫВУТ. Идеально — первые ~{GnocchiBoiler.PerfectFor:0} с, через {GnocchiBoiler.OverAt:0} с — разварятся.");
            log.AppendLine("  4. Лопата ЛКМ по кастрюле — вылавливает всплывшие (вода остаётся).");
            log.AppendLine("Кастрюлю настраивать не надо — варка и вид ньокков в воде включаются сами.");
            DS.Done("Ньокки — продукт для варки", log);
        }

        // ───────────────────────── 10–11. СОУС, СЫР, ПОДАЧА ─────────────────────────

        private const string GDir = "Assets/CatCooks/Food/Models/Gnocchi";

        [MenuItem(Root + "10. Соус и сыр: продукты и рецепт", priority = 190)]
        public static void MakeSauceProducts()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef sub = DS.FindGroup("Group_SubRecipe") ?? DS.GroupOf("Меренга") ?? DS.GroupOf("Бульон");
            ProductDef sauce = DS.Ensure(CookRecipeBook.Gnocchi.SageButter, sub, new Color(0.78f, 0.52f, 0.2f), 0.05f, true, dir, ref next, log);
            ProductDef burnt = DS.Ensure(CookRecipeBook.Gnocchi.BurntButter, sub, new Color(0.22f, 0.14f, 0.08f), 0.05f, true, dir, ref next, log);
            foreach (ProductDef p in new[] { sauce, burnt })
                if (p != null && p.DebrisColor.a < 0.01f) { p.DebrisColor = new Color(p.BaseColor.r, p.BaseColor.g, p.BaseColor.b, 1f); EditorUtility.SetDirty(p); }
            foreach (string need in new[] { CookRecipeBook.Gnocchi.Butter, CookRecipeBook.Gnocchi.Sage, CookRecipeBook.Gnocchi.Cheese, GnocchiProduct.Title })
                log.AppendLine(DS.Find(need) != null ? $"✔ «{need}» есть" : $"✖ нет «{need}» — {(need == GnocchiProduct.Title ? "меню 8" : "Еда → Создать продукты")}");
            AssetDatabase.SaveAssets();
            CookRecipeBook.Reload();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null) { db.CollectFromProject(); EditorUtility.SetDirty(db); EditorSceneManager.MarkSceneDirty(db.gameObject.scene); log.AppendLine("✔ FoodDatabase в сцене пересобран (сохрани сцену)"); }
            else log.AppendLine("⚠ в открытой сцене нет FoodDatabase — нажми у него «Collect From Project»");
            log.AppendLine("\nРЕЦЕПТ «Масло с шалфеем» (в книге сам, по названиям): ковш/кастрюля на огне —");
            log.AppendLine("  сливочное масло (своё, из сливок вибратором) 2 мастерка + шалфей 2 дозы (пучок — рвать над ковшом) → растопить 8 с (от 40 °C).");
            log.AppendLine("  Допуск ±50%: масла 1–3 мастерка, шалфея 1–3 дозы. Держать выше 200 °C 6 с — «Масло горелое».");
            log.AppendLine("Дальше: 10б/10г/10д — долька и головка сыра, 10в — выдели модель пучка шалфея, 11 — выдели свою тарелку (Аляски).");
            DS.Done("Ньокки — соус и сыр", log);
        }

        // ── СЫР: головка пилится пилой/бензопилой на дольки ──
        private static float CheeseR => 0.5f * Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
        private static float CheeseH => 0.32f * Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
        private const int CheeseSectors = 6;

        private static void CheeseMats(out Material cheese, out Material rind, StringBuilder log)
        {
            DS.EnsureDir(GDir);
            cheese = LitAsset(GDir + "/Mat_Cheese.mat", new Color(0.97f, 0.86f, 0.52f), 0.3f, log);
            rind = LitAsset(GDir + "/Mat_CheeseRind.mat", new Color(0.80f, 0.58f, 0.26f), 0.12f, log);
            // зерно сыра и корка — текстурами (ассеты, один раз)
            TexOn(cheese, GDir + "/Tex_Cheese.asset", () => GnocchiLooks.Noise("Tex_Cheese", 256, 23, 0.06f, 0.07f, 0.84f, 1.14f), 3f, log);
            TexOn(rind, GDir + "/Tex_CheeseRind.asset", () => GnocchiLooks.Noise("Tex_CheeseRind", 256, 37, 0.16f, 0.04f, 0.68f, 1.06f), 2f, log);
        }

        private static void TexOn(Material m, string path, System.Func<Texture2D> make, float tiling, StringBuilder log)
        {
            if (m == null) return;
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                tex = make();
                tex.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(tex, path);
                log.AppendLine("  ＋ текстура " + path);
            }
            string prop = m.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            if (m.GetTexture(prop) == tex) return;
            m.SetTexture(prop, tex);
            m.SetTextureScale(prop, Vector2.one * tiling);
            EditorUtility.SetDirty(m);
            log.AppendLine($"  ~ {m.name}: текстура {System.IO.Path.GetFileNameWithoutExtension(path)}");
        }

        [MenuItem(Root + "10е. Сыр — обновить вид (текстуры, меш дольки)", priority = 195)]
        public static void CheeseRefreshLook()
        {
            var log = new StringBuilder();
            CheeseMats(out Material cm, out Material rm, log);
            float step = 360f / CheeseSectors;
            SectorMeshAsset(GDir + "/CheeseWedge_Mesh.asset", -step * 0.5f, step * 0.5f, true, log);
            SectorMeshAsset(GDir + "/CheeseWheel_Mesh.asset", 0f, 360f, false, log);
            AssetDatabase.SaveAssets();
            log.AppendLine("\nГотово: головки и дольки (и префаб дольки) используют эти материалы — обновились сами.");
            DS.Done("Ньокки — вид сыра", log);
        }

        private static Material LitAsset(string path, Color c, float smooth, StringBuilder log)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine("  ＋ материал " + path);
            return m;
        }

        /// <summary>Меш-ассет: если уже есть — перезаполняется на месте (ссылки префабов не теряются).</summary>
        private static Mesh SectorMeshAsset(string path, float a0, float a1, bool centered, StringBuilder log)
        {
            Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = m == null;
            if (fresh) m = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            CheeseWheel.Fill(m, CheeseR, CheeseH, a0, a1, centered);
            if (fresh) { AssetDatabase.CreateAsset(m, path); log.AppendLine("  ＋ меш " + path); }
            else { EditorUtility.SetDirty(m); log.AppendLine("  ~ меш " + path + " обновлён (мякиш + корка)"); }
            return m;
        }

        [MenuItem(Root + "10б. Долька сыра — заготовка для префаба", priority = 191)]
        public static void CheeseWedgeTemplate()
        {
            var log = new StringBuilder();
            ProductDef cheese = DS.Find(CookRecipeBook.Gnocchi.Cheese);
            if (cheese == null) { EditorUtility.DisplayDialog("Ньокки", "Нет продукта «Сыр твёрдый» (Еда → Создать продукты).", "Ок"); return; }
            CheeseMats(out Material cm, out Material rm, log);
            float step = 360f / CheeseSectors;
            Mesh mesh = SectorMeshAsset(GDir + "/CheeseWedge_Mesh.asset", -step * 0.5f, step * 0.5f, true, log);
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            var go = new GameObject("CheeseWedge");
            Undo.RegisterCreatedObjectUndo(go, "CheeseWedge");
            go.transform.position = at + Vector3.up * (CheeseH * 0.5f + 0.02f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { cm, rm };
            MeshCollider mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true;
            Rigidbody rb = go.AddComponent<Rigidbody>(); rb.mass = 1.5f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            ClientAuthNT(go, log);
            FoodPiece fp = go.AddComponent<FoodPiece>();
            var so = new SerializedObject(fp);
            DS.SetObj(so, "_product", cheese);
            DS.SetInt(so, "_startState", (int)FoodState.Raw);
            DS.SetInt(so, "_count", 3);
            so.ApplyModifiedProperties();
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"＋ «CheeseWedge»: долька {step:0}°, R {CheeseR:0.00} м — Food Piece «Сыр твёрдый», сырое, 3 порции");
            log.AppendLine("\nДАЛЬШЕ РУКАМИ: перетащи CheeseWedge в Project → префаб → Network Manager → Spawnable Prefabs. Из сцены удали.");
            log.AppendLine("Потом: 10г — головка в сцену, 10д — выдели префаб дольки → назначить всем головкам.");
            DS.Done("Ньокки — долька сыра", log);
        }

        [MenuItem(Root + "10г. Головка сыра — в сцену (пилить пилой / бензопилой)", priority = 193)]
        public static void CheeseWheelMake()
        {
            var log = new StringBuilder();
            ProductDef cheese = DS.Find(CookRecipeBook.Gnocchi.Cheese);
            if (cheese == null) { EditorUtility.DisplayDialog("Ньокки", "Нет продукта «Сыр твёрдый» (Еда → Создать продукты).", "Ок"); return; }
            GameObject wedge = PickWedgePrefab();
            CheeseMats(out Material cm, out Material rm, log);
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            var go = new GameObject("CheeseWheel");
            Undo.RegisterCreatedObjectUndo(go, "CheeseWheel");
            go.transform.position = at + Vector3.up * 0.02f;
            Mesh preview = SectorMeshAsset(GDir + "/CheeseWheel_Mesh.asset", 0f, 360f, false, log);   // вид в редакторе; в игре — свой меш с вырезом
            go.AddComponent<MeshFilter>().sharedMesh = preview;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { cm, rm };
            MeshCollider mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = preview; mc.convex = true;
            Rigidbody rb = go.AddComponent<Rigidbody>(); rb.mass = 9f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            CheeseWheel w = go.AddComponent<CheeseWheel>();
            w.EditorSetup(wedge, cheese, CheeseR, CheeseH, cm, rm);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"＋ «CheeseWheel»: головка Ø{CheeseR * 2f:0.00} м × {CheeseH:0.00} м, {CheeseSectors} долек по 3 порции");
            log.AppendLine(wedge != null ? $"✔ префаб дольки: «{wedge.name}»" : "⚠ префаб дольки не назначен — 10б → префаб → выдели его → 10д (без него пила не режет)");
            log.AppendLine("\nВ ИГРЕ: пила или бензопила по головке — отваливается долька (по кругу). Последний распил — обе оставшиеся.");
            log.AppendLine("Долька — F в руку → в измельчитель → тёртый сыр. Головка — объект сцены (или сохрани префабом в Spawnable Prefabs).");
            DS.Done("Ньокки — головка сыра", log);
        }

        [MenuItem(Root + "10д. Назначить выделенный префаб дольки всем головкам сыра", priority = 194)]
        public static void CheeseAssignWedge()
        {
            var log = new StringBuilder();
            GameObject wedge = PickWedgePrefab();
            if (wedge == null) { EditorUtility.DisplayDialog("Ньокки", "Выдели в Project префаб дольки сыра (из 10б: Food Piece + меш-сектор).", "Ок"); return; }
            int n = 0;
            foreach (CheeseWheel w in Object.FindObjectsByType<CheeseWheel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                w.EditorSetup(wedge, null, 0f, 0f, null, null);
                EditorSceneManager.MarkSceneDirty(w.gameObject.scene);
                n++;
            }
            log.AppendLine(n > 0 ? $"✔ префаб «{wedge.name}» назначен головкам: {n}" : "⚠ головок сыра в сцене нет — 10г");
            if (wedge.GetComponent<FishNet.Object.NetworkObject>() == null) log.AppendLine("✖ у префаба нет Network Object — сделай заново из 10б");
            log.AppendLine("Не забудь префаб дольки в Network Manager → Spawnable Prefabs.");
            DS.Done("Ньокки — долька сыра", log);
        }

        /// <summary>Префаб дольки: выделенный в Project (Food Piece «Сыр твёрдый», не головка).</summary>
        private static GameObject PickWedgePrefab()
        {
            GameObject g = Selection.activeObject as GameObject;
            if (g == null || !EditorUtility.IsPersistent(g)) return null;
            FoodPiece fp = g.GetComponent<FoodPiece>();
            if (fp == null || g.GetComponent<CheeseWheel>() != null) return null;
            return g;
        }

        [MenuItem(Root + "10в. Пучок шалфея — настроить (выдели модель пучка)", priority = 192)]
        public static void SetupHerbBunch()
        {
            var log = new StringBuilder();
            ProductDef sage = DS.Find(CookRecipeBook.Gnocchi.Sage);
            GameObject go = Selection.activeGameObject;
            if (go == null || sage == null) { EditorUtility.DisplayDialog("Ньокки", "Выдели корень модели пучка шалфея (в сцене или префаб в Project). Продукт «Шалфей» должен быть (Еда → Создать продукты).", "Ок"); return; }
            // чужая логика предмета на пучке — мешает (две ручки на одном объекте)
            foreach (PickupItem other in go.GetComponents<PickupItem>())
            {
                if (other is CatCooks.Food.Tools.HerbBunchItem) continue;
                if (other.GetType() == typeof(PickupItem)) { log.AppendLine("－ простой Pickup Item убран — его заменяет Herb Bunch Item (он тоже берётся F)"); Undo.DestroyObjectImmediate(other); }
                else log.AppendLine($"⚠ на пучке уже «{other.GetType().Name}» — убери его руками, иначе два предмета на одном объекте");
            }
            Bounds lb = CatCooksDessertSetup.LocalBounds(go.transform);
            if (go.GetComponentInChildren<Collider>() == null)
            {
                BoxCollider bc = Undo.AddComponent<BoxCollider>(go);
                bc.center = lb.center; bc.size = Vector3.Max(lb.size, Vector3.one * 0.02f);
                log.AppendLine("  ＋ Box Collider по модели");
            }
            Rigidbody rb = DS.Need<Rigidbody>(go);
            rb.mass = Mathf.Clamp(rb.mass, 0.2f, 1f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            ClientAuthNT(go, log);
            // верх пучка — откуда рвутся листья (самая высокая точка модели по миру)
            Transform top = go.transform.Find("Top");
            if (top == null)
            {
                top = DS.FindOrCreate(go.transform, "Top");
                Vector3 c = go.transform.TransformPoint(lb.center);
                float maxY = c.y;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 lc = new Vector3((i & 1) == 0 ? lb.min.x : lb.max.x, (i & 2) == 0 ? lb.min.y : lb.max.y, (i & 4) == 0 ? lb.min.z : lb.max.z);
                    maxY = Mathf.Max(maxY, go.transform.TransformPoint(lc).y);
                }
                top.position = new Vector3(c.x, Mathf.Lerp(c.y, maxY, 0.8f), c.z);
                log.AppendLine("  ＋ Top — верх пучка (подвинь к листьям, если нужно)");
            }
            var hb = DS.Need<CatCooks.Food.Tools.HerbBunchItem>(go);
            hb.EditorSetup(sage, top);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"＋ «{go.name}»: Herb Bunch Item — «Шалфей», 1 доза = 1 порция, бесконечный");
            log.AppendLine("\nВ ИГРЕ: взять пучок (F) → смотришь на ковш → ПКМ: кот подносит пучок и дёргает — листья падают в ковш (+1 доза).");
            log.AppendLine("Для соуса — 2 дозы (1–3 тоже засчитаются). Префаб пучка — в Spawnable Prefabs, если спавнишь его по сети.");
            if (PrefabUtility.IsPartOfPrefabInstance(go)) log.AppendLine("Это экземпляр префаба — Overrides → Apply All.");
            DS.Done("Ньокки — пучок шалфея", log);
        }

        [MenuItem(Root + "11. Тарелка — научить ньокки (выдели свою тарелку, где делали Аляску)", priority = 196)]
        public static void TeachPlate()
        {
            var log = new StringBuilder();
            GameObject sel = Selection.activeGameObject;
            Cookware cw = sel != null ? sel.GetComponentInParent<Cookware>() : null;
            if (cw == null) { EditorUtility.DisplayDialog("Ньокки", "Выдели в сцене (или префаб в Project) свою тарелку — ту, где собирали Аляску (на ней Cookware и Dessert Plate). Тарелку я не создаю, только добавлю ей Gnocchi Plate.", "Ок"); return; }
            GameObject go = cw.gameObject;
            bool alaska = go.GetComponent<DessertPlate>() != null;
            // якорь: общий с Аляской (DessertAnchor — центр на дне), иначе FoodAnchor
            Transform anchor = go.transform.Find("DessertAnchor");
            if (anchor == null) anchor = go.transform.Find("FoodAnchor");
            if (anchor == null)
            {
                anchor = DS.FindOrCreate(go.transform, "FoodAnchor");
                Bounds wb0 = PlateBounds(go);
                anchor.position = new Vector3(wb0.center.x, wb0.min.y + wb0.size.y * 0.3f, wb0.center.z);
                log.AppendLine("  ＋ FoodAnchor — центр на дне (подвинь, если ньокки висят или утонули)");
            }
            Bounds wb = PlateBounds(go);
            float R = Mathf.Max(0.2f, Mathf.Max(wb.extents.x, wb.extents.z));
            // вместимость: ньокки 8 + соус 2 + сыр 2 — тарелке Аляски (10) мало
            if (cw.Def != null && cw.Def.Capacity < 16)
            {
                Undo.RecordObject(cw.Def, "Capacity");
                log.AppendLine($"  ~ тара «{cw.Def.Title}»: вместимость {cw.Def.Capacity} → 16 (ньокки 8 + соус + сыр; Аляске не мешает)");
                cw.Def.Capacity = 16; EditorUtility.SetDirty(cw.Def);
            }
            if (cw.Def == null) log.AppendLine("⚠ у Cookware нет Def — выдели тарелку и сделай Десерты → 7б (Аляска), потом снова 11");
            if (cw.InsideVolume == null) log.AppendLine("⚠ у Cookware нет Inside — Десерты → 7б, потом снова 11");
            bool had = go.GetComponent<GnocchiPlate>() != null;
            GnocchiPlate gp = DS.Need<GnocchiPlate>(go);
            gp.EditorSetup(anchor, R);
            AssetDatabase.SaveAssets();
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"{(had ? "~" : "＋")} Gnocchi Plate на «{go.name}»: якорь «{anchor.name}», Ø{R * 2f:0.00} м{(alaska ? " — рядом с Dessert Plate, друг другу не мешают" : "")}");
            log.AppendLine("  Подсказка ньокков появится, только когда в тарелке ньокки/соус/сыр; Аляски — когда бисквит/мороженое/меренга.");
            log.AppendLine("\nПОДАЧА: лопатой в тарелку (ПКМ) — ньокки из кастрюли, масло с шалфеем из ковша, тёртый сыр. Всё есть — звёзды ★ (1–5).");
            if (PrefabUtility.IsPartOfPrefabInstance(go)) log.AppendLine("Это экземпляр префаба — Overrides → Apply All, чтобы было на всех тарелках.");
            DS.Done("Ньокки — тарелка", log);
        }

        /// <summary>Границы модели тарелки в мире (без авто-детей: десерта, ньокков).</summary>
        private static Bounds PlateBounds(GameObject go)
        {
            bool any = false; Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("(auto)") || r.transform.parent != null && r.transform.parent.name.Contains("(auto)")) continue;
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        [MenuItem(Root + "3. Проверить каток и рулон", priority = 182)]
        public static void Check()
        {
            var sb = new StringBuilder();
            int err = 0;
            var rollers = Object.FindObjectsByType<RoadRollerVehicle>(FindObjectsSortMode.None);
            if (rollers.Length == 0) { sb.AppendLine("✖ катка в сцене нет — меню 1"); err++; }
            foreach (RoadRollerVehicle v in rollers)
            {
                var so = new SerializedObject(v);
                bool ok = so.FindProperty("_frontDrum").objectReferenceValue != null && so.FindProperty("_rearDrum").objectReferenceValue != null
                       && so.FindProperty("_seatPoint").objectReferenceValue != null && so.FindProperty("wheels").arraySize >= 2;
                bool net = HasType(v.gameObject, "NetworkObject") && HasType(v.gameObject, "NetworkTransform");
                sb.AppendLine($"{(ok && net ? "✔" : "✖")} каток «{v.name}»: вальцы, сиденье, подвеска {(ok ? "есть" : "НЕ ВСЁ — меню 1")}, сеть {(net ? "есть" : "НЕТ")}");
                if (!ok || !net) err++;
            }
            var rolls = Object.FindObjectsByType<WaterproofRollItem>(FindObjectsSortMode.None);
            if (rolls.Length == 0) { sb.AppendLine("✖ рулона в сцене нет — меню 2"); err++; }
            foreach (WaterproofRollItem r in rolls)
            {
                bool net = HasType(r.gameObject, "NetworkObject") && HasType(r.gameObject, "NetworkTransform");
                bool roll = new SerializedObject(r).FindProperty("_roll").objectReferenceValue != null;
                sb.AppendLine($"{(net && roll ? "✔" : "✖")} рулон «{r.name}»: меш Roll {(roll ? "есть" : "НЕТ")}, сеть {(net ? "есть" : "НЕТ")}");
                if (!net || !roll) err++;
            }
            foreach (DoughSheet d in Object.FindObjectsByType<DoughSheet>(FindObjectsSortMode.None))
            {
                if (d.IsPieceTemplate) continue;
                GameObject pp = d.PiecePrefab;
                DoughSheet pd = pp != null ? pp.GetComponent<DoughSheet>() : null;
                bool ok = pd != null && pd.IsPieceTemplate && EditorUtility.IsPersistent(pp);
                sb.AppendLine($"{(ok ? "✔" : "✖")} тесто «{d.name}» ({DoughSheet.KindTitle(d.Kind)}): префаб куска {(ok ? $"«{pp.name}»" : "НЕ ЗАДАН — меню 6, потом 7 (старый префаб-брусок v3.8 не подходит — меню 6 заново; без префаба куски не отделяются)")}");
                if (!ok) err++;
            }
            sb.AppendLine(DS.Find(GnocchiProduct.Title) != null ? "✔ продукт «Ньокки» есть (варка)" : "✖ нет продукта «Ньокки» — меню 8 (без него лопата не берёт ньокки)");
            if (DS.Find(GnocchiProduct.Title) == null) err++;
            // этап 5: соус, сыр, подача
            foreach (string t in new[] { CookRecipeBook.Gnocchi.SageButter, CookRecipeBook.Gnocchi.BurntButter })
                if (DS.Find(t) == null) { sb.AppendLine($"⚠ нет продукта «{t}» — меню 10 (соус)"); }
            foreach (CheeseWheel w in Object.FindObjectsByType<CheeseWheel>(FindObjectsSortMode.None))
                sb.AppendLine(w.WedgePrefab != null ? $"✔ головка сыра «{w.name}»: долька «{w.WedgePrefab.name}»" : $"✖ головка сыра «{w.name}»: нет префаба дольки — 10б → префаб → 10д");
            if (Object.FindObjectsByType<CheeseWheel>(FindObjectsSortMode.None).Length == 0) sb.AppendLine("⚠ головки сыра в сцене нет — 10б (долька-префаб), 10г (головка)");
            sb.AppendLine(Object.FindObjectsByType<CatCooks.Food.Tools.HerbBunchItem>(FindObjectsSortMode.None).Length > 0 ? "✔ пучок шалфея есть" : "⚠ пучка шалфея в сцене нет — выдели модель пучка → меню 10в");
            sb.AppendLine(Object.FindObjectsByType<GnocchiPlate>(FindObjectsSortMode.None).Length > 0 ? "✔ тарелка знает ньокки" : "⚠ ни одна тарелка в сцене не знает ньокки — выдели тарелку Аляски → меню 11");
            var coats = Object.FindObjectsByType<CatCooks.Food.Tools.ToolFlourCoat>(FindObjectsSortMode.None);
            sb.AppendLine(coats.Length > 0 ? $"✔ мука на инструментах: {coats.Length} шт. (кувалда)" : "⚠ ни на одной кувалде нет Tool Flour Coat — меню 5 (иначе кувалда не липнет)");
            sb.AppendLine("\nНе забудь: оба — префабами в Network Manager → Spawnable Prefabs.");
            sb.Insert(0, err == 0 ? "ВСЁ ГОТОВО ✔\n\n" : $"Ошибок: {err}\n\n");
            DS.Done("Ньокки — проверка", sb);
        }

        // ───────────────────────── помощники ─────────────────────────

        /// <summary>Ось руля = нормаль обода. Обод — вершины на дальнем конце руля (колонка — к пивоту);
        /// если руль просто «блин» — нормаль самого плоского направления. pivot — центр обода (оси меша руля).</summary>
        private static bool SteeringAxis(Transform t, out Vector3 axis, out Vector3 pivot)
        {
            axis = Vector3.forward; pivot = Vector3.zero;
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            Vector3[] v = mf.sharedMesh.vertices;
            if (v.Length < 8) return false;
            Bounds b = mf.sharedMesh.bounds;
            Vector3 sz = b.size;
            int ax = sz.x >= sz.y && sz.x >= sz.z ? 0 : (sz.y >= sz.z ? 1 : 2);
            float[] e = { sz.x, sz.y, sz.z };
            System.Array.Sort(e);
            var pts = new List<Vector3>();
            if (e[2] > e[1] * 1.3f)
            {
                // колонка + обод: обод — на конце, дальнем от пивота
                float lo = b.min[ax], hi = b.max[ax];
                bool far = Mathf.Abs(hi) >= Mathf.Abs(lo);
                float cut = far ? hi - (hi - lo) * 0.18f : lo + (hi - lo) * 0.18f;
                foreach (Vector3 p in v) if (far ? p[ax] >= cut : p[ax] <= cut) pts.Add(p);
            }
            else pts.AddRange(v);
            if (pts.Count < 6) return false;
            Vector3 c = Vector3.zero; foreach (Vector3 p in pts) c += p; c /= pts.Count;
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (Vector3 p in pts) { Vector3 d = p - c; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
            float tr = xx + yy + zz;
            // самое «плоское» направление: собственный вектор с наименьшим числом (итерация по tr·I − C)
            Vector3 n = new Vector3(0.3f, 0.5f, 0.8f);
            for (int i = 0; i < 64; i++)
            {
                Vector3 m = new Vector3(tr * n.x - (xx * n.x + xy * n.y + xz * n.z),
                                        tr * n.y - (xy * n.x + yy * n.y + yz * n.z),
                                        tr * n.z - (xz * n.x + yz * n.y + zz * n.z));
                if (m.sqrMagnitude < 1e-12f) break;
                n = m.normalized;
            }
            if (Vector3.Dot(n, c) < 0f) n = -n;   // от пивота к ободу
            axis = n; pivot = c;
            return true;
        }

        private static void ClientAuthNT(GameObject go, StringBuilder log)
        {
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null || c.GetType().Name != "NetworkTransform") continue;
                var so = new SerializedObject(c);
                SerializedProperty a = so.FindProperty("_clientAuthoritative"); if (a != null) a.boolValue = true;
                SerializedProperty s = so.FindProperty("_sendToOwner"); if (s != null) s.boolValue = true;
                SerializedProperty cfg = so.FindProperty("_componentConfiguration");
                if (cfg != null && cfg.propertyType == SerializedPropertyType.Enum)
                {
                    int dis = System.Array.IndexOf(cfg.enumNames, "Disabled");
                    if (dis >= 0) cfg.enumValueIndex = dis;
                }
                so.ApplyModifiedProperties();
                log.AppendLine("  ✔ Network Transform: Client Authoritative, Component Configuration = Disabled");
            }
        }

        private static bool HasType(GameObject go, string n)
        {
            foreach (Component c in go.GetComponents<Component>()) if (c != null && c.GetType().Name == n) return true;
            return false;
        }

        private static void Record(Component c)
        {
            EditorUtility.SetDirty(c);
            if (PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }

        private static Transform FindExact(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        private static Transform FindContains(Transform root, string part)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name.ToLowerInvariant().Contains(part)) return t;
            return null;
        }

        private static GameObject FindRootByName(string name)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.parent == null && t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
            return null;
        }

        private static Vector3 Center(Transform t)
        {
            MeshFilter mf = t.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? t.TransformPoint(mf.sharedMesh.bounds.center) : t.position;
        }

        /// <summary>Габарит меша объекта t (только его MeshFilter) в осях root.</summary>
        private static Bounds BoundsIn(Transform root, Transform t)
        {
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return new Bounds(root.InverseTransformPoint(t.position), Vector3.zero);
            Bounds mb = mf.sharedMesh.bounds;
            Bounds res = new Bounds(root.InverseTransformPoint(t.TransformPoint(mb.center)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                res.Encapsulate(root.InverseTransformPoint(t.TransformPoint(c)));
            }
            return res;
        }
    }
}
#endif
