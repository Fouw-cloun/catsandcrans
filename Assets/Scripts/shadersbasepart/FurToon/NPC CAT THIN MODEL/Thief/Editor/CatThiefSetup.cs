#if UNITY_EDITOR
using System.IO;
using System.Text;
using CatCooks.Thief;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CatCooks.ThiefEditor
{
    /// <summary>
    /// Меню CatCooks → Кот-воришка:
    ///   1. Собрать префаб — из Cat_Caper_Model.fbx + клипов Cat_Caper_Walk/Sneak/Lookout/Pickup/Escape
    ///      в Assets/CatCaper/Thief/CatThief.prefab (NetworkObject, NetworkTransform, NavMeshAgent, коллайдер, мозг).
    ///   2. Поставить кота в сцену — в центр окна Scene (на пол).
    ///   3. Поставить укрытие — точка, где кот прячется и ест.
    /// После 1: Fish-Networking → Refresh Default Prefabs.
    /// </summary>
    public static class CatThiefSetup
    {
        /// <summary>Префаб — рядом со скриптом CatThief.cs (папку можно переносить куда угодно).</summary>
        static string PrefabPath
        {
            get
            {
                foreach (string g in AssetDatabase.FindAssets("CatThief t:MonoScript"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (Path.GetFileName(p) == "CatThief.cs") return Path.GetDirectoryName(p).Replace('\\', '/') + "/CatThief.prefab";
                }
                return "Assets/CatCaper/Thief/CatThief.prefab";
            }
        }

        [MenuItem("CatCooks/Кот-воришка/1. Собрать префаб кота", priority = 1)]
        public static void BuildPrefab()
        {
            var log = new StringBuilder("КОТ-ВОРИШКА → 1. Префаб\n");
            string modelPath = FindModel(log);
            if (modelPath == null) { log.AppendLine("✖ не нашёл модель кота со скелетом gamerig (DEF-кости + меш). Выдели FBX кота в Project и запусти меню ещё раз"); Debug.Log(log); return; }
            log.AppendLine($"  модель: {modelPath}");
            string sneakPath = Find("Cat_Caper_Sneak");
            string folder = Path.GetDirectoryName(sneakPath ?? modelPath).Replace('\\', '/');
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

            bool ca_missing = false;
            var root = new GameObject("CatThief");
            try
            {
                root.AddComponent<FishNet.Object.NetworkObject>();
                root.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.transform.SetParent(root.transform, false);
                inst.name = "Model";
                // v27.1: пересборка не трогает масштаб, который ты выставил руками (Model и корень старого префаба)
                GameObject oldPf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                bool keptScale = false;
                if (oldPf != null)
                {
                    root.transform.localScale = oldPf.transform.localScale;
                    Transform oldModel = oldPf.transform.Find("Model");
                    if (oldModel != null)
                    {
                        inst.transform.localScale = oldModel.localScale;
                        inst.transform.localPosition = oldModel.localPosition;
                        keptScale = true;
                        log.AppendLine($"  масштаб взят из старого префаба: корень {oldPf.transform.localScale}, Model {oldModel.localScale}");
                    }
                }
                Animator an = inst.GetComponent<Animator>();
                if (an == null) an = inst.AddComponent<Animator>();
                an.applyRootMotion = false;
                an.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                CatThiefAnimator ca = inst.AddComponent<CatThiefAnimator>();
                AnimationClip rawWalk = Clip(folder, "Cat_Caper_Walk", log);
                if (rawWalk == null) rawWalk = ClipByName("CatWalk", log);
                ca.sneak = Clip(folder, "Cat_Caper_Sneak", log);
                ca.lookout = Clip(folder, "Cat_Caper_Lookout", log);
                ca.pickup = Clip(folder, "Cat_Caper_Pickup", log);
                ca.escape = Clip(folder, "Cat_Caper_Escape", log);
                ca_missing = ca.sneak == null || ca.lookout == null || ca.pickup == null || ca.escape == null;
                // v26.9: модель — твоя, ходьба — твоя как есть. Клипы нейронки сделаны на ДРУГОЙ позе покоя тех же костей
                // (поэтому «не связываются») — переносим их на твою модель через МИРОВЫЕ повороты костей.
                bool ownModel = !Path.GetFileName(modelPath).StartsWith("Cat_Caper_");
                ca.walk = rawWalk;
                ca.proceduralPoses = ownModel;
                if (ownModel) ca.targetSpan = 0f;   // v27.1: своя модель — рост не подгоняем   // v27: своя модель — всё строим из твоей ходьбы + позы кодом (клипы нейронки на этот риг не садятся)
                if (ownModel) log.AppendLine("Своя модель: Sneak/Lookout/Pickup/Escape — позы кодом поверх твоей ходьбы (CatThiefAnimator → v27 позы).");
                else if (rawWalk != null && ca.sneak != null) ca.walk = BuildSafeWalk(rawWalk, model, ca.sneak, log);

                // v26.7: эталонный рост в позе Sneak — в игре модель подгоняется под него (масштаб «плывёт» от анимации)
                string sneakFbx = Find("Cat_Caper_Sneak");
                GameObject sneakAsset = sneakFbx != null ? AssetDatabase.LoadAssetAtPath<GameObject>(sneakFbx) : null;
                if (sneakAsset != null && ca.sneak != null && !ownModel)
                {
                    GameObject tmp = Object.Instantiate(sneakAsset);
                    try { ca.sneak.SampleAnimation(tmp, 0f); ca.targetSpan = CatThiefAnimator.SpanOf(tmp.transform); }
                    finally { Object.DestroyImmediate(tmp); }
                    log.AppendLine($"  эталонный рост (голова–ступни в Sneak): {ca.targetSpan:0.00} м — в игре модель подгонится под него");
                }

                // v26.5: рост — как у котов из клипов Cat_Caper (модель художника может быть в других единицах — кот-великан)
                float refH = ownModel ? 0f : Height(Find("Cat_Caper_Sneak"));          // своя модель — свой рост
                float myH = HeightOf(inst);
                if (!keptScale && refH > 0.01f && myH > 0.01f)
                {
                    float k = refH / myH;
                    if (k < 0.8f || k > 1.25f)
                    {
                        inst.transform.localScale *= k;
                        log.AppendLine($"  масштаб модели ×{k:0.###} (было {myH:0.00} м, как у Cat_Caper {refH:0.00} м). Свой рост — поле Scale у объекта Model в префабе");
                    }
                }

                // размер кота
                Bounds b = new Bounds(root.transform.position, Vector3.zero); bool any = false;
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                float h = any ? b.size.y : 1f, w = any ? Mathf.Max(b.size.x, b.size.z) : 0.5f;
                // v26.1: лапы — ровно на точку корня (иначе кот висит над полом или в полу)
                if (any)
                {
                    inst.transform.localPosition = new Vector3(-(b.center.x - root.transform.position.x), -(b.min.y - root.transform.position.y), -(b.center.z - root.transform.position.z));
                    log.AppendLine($"  модель сдвинута на {inst.transform.localPosition} — лапы на земле");
                }
                if (an.avatar == null) log.AppendLine("  ⚠ у модели нет Avatar: выдели Cat_Caper_Model.fbx → Rig → Generic, Avatar Definition: Create From This Model → Apply");
                else if (an.avatar.isHuman) log.AppendLine("  ⚠ Avatar гуманоидный — нужен Generic (клипы на костях DEF-)");
                var col = root.AddComponent<CapsuleCollider>();
                col.height = h; col.radius = Mathf.Clamp(w * 0.32f, 0.1f, h * 0.5f); col.center = new Vector3(0f, h * 0.5f, 0f);
                var rb = root.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.useGravity = false;
                var ag = root.AddComponent<NavMeshAgent>();
                ag.radius = col.radius; ag.height = h; ag.baseOffset = 0f; ag.updateRotation = false;
                ag.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

                CatThief brain = root.AddComponent<CatThief>();
                var so = new SerializedObject(brain);
                so.FindProperty("_anim").objectReferenceValue = ca;
                so.ApplyModifiedPropertiesWithoutUndo();

                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok) { log.AppendLine("✖ не сохранился префаб"); Debug.Log(log); return; }
                log.AppendLine($"＋ {PrefabPath}: рост {h:0.00} м, коллайдер r {col.radius:0.00}");
                Selection.activeObject = saved;
            }
            finally { Object.DestroyImmediate(root); }
            // v26.8: без клипов кот — Т-поза
            if (ca_missing) log.AppendLine("\n✖✖ НЕТ КЛИПОВ Cat_Caper_Sneak/Lookout/Pickup/Escape — кот будет в Т-позе. " +
                                           "Положи 4 файла Cat_Caper_*.fbx из первого архива (папка CatCaper/Models) в любую папку проекта и запусти меню ещё раз.");
            // сеть: сразу обновить список префабов FishNet
            if (EditorApplication.ExecuteMenuItem("Tools/Fish-Networking/Utility/Refresh Default Prefabs"))
                log.AppendLine("  ✔ Fish-Networking → Refresh Default Prefabs выполнен сам");
            log.AppendLine("\nДАЛЬШЕ ОБЯЗАТЕЛЬНО: Fish-Networking → Refresh Default Prefabs (после КАЖДОЙ пересборки — иначе «AssetPathHash is not set for CatThief»), " +
                           "потом удали старого кота из сцены и «2. Поставить кота в сцену».");
            Debug.Log(log);
        }

        [MenuItem("CatCooks/Кот-воришка/2. Поставить кота в сцену", priority = 2)]
        public static void PlaceCat()
        {
            GameObject pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (pf == null) { Debug.Log("КОТ-ВОРИШКА: сначала «1. Собрать префаб кота»"); return; }
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            go.transform.position = ScenePoint();
            Undo.RegisterCreatedObjectUndo(go, "Кот-воришка");
            Selection.activeObject = go;
            Debug.Log($"КОТ-ВОРИШКА: поставил «{go.name}» в {go.transform.position}. Он бродит вокруг этой точки (Wander Radius).");
        }

        [MenuItem("CatCooks/Кот-воришка/3. Поставить укрытие (где кот ест)", priority = 3)]
        public static void PlaceHide()
        {
            var go = new GameObject("CatHideSpot");
            go.AddComponent<CatHideSpot>();
            go.transform.position = ScenePoint();
            Undo.RegisterCreatedObjectUndo(go, "Укрытие кота");
            Selection.activeObject = go;
            Debug.Log($"КОТ-ВОРИШКА: укрытие в {go.transform.position} — поставь ещё 2–3 в разных углах.");
        }

        static Vector3 ScenePoint()
        {
            SceneView sv = SceneView.lastActiveSceneView;
            Vector3 p = sv != null ? sv.pivot : Vector3.zero;
            if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out RaycastHit h, 60f)) p = h.point;
            return p;
        }

        /// <summary>
        /// v26.3: «БЕЗОПАСНАЯ» ХОДЬБА. Клип художника мог быть выгружен с другой позой покоя костей, в сантиметрах
        /// и без части костей — отсюда Т-поза, вывернутая голова и «летает». Делаем новый клип:
        ///   • только ПОВОРОТЫ костей (сдвиги и масштаб выкинуты — кот не улетает);
        ///   • поворот пересчитан с позы покоя FBX ходьбы на позу покоя модели (покой_модели × покой_ходьбы⁻¹ × кадр);
        ///   • кости, которых в ходьбе нет (лапы, хвост…), — из первого кадра Sneak (не Т-поза).
        /// Сохраняется рядом с префабом: CatWalk_Safe.anim.
        /// </summary>
        static AnimationClip BuildSafeWalk(AnimationClip walk, GameObject model, AnimationClip sneak, StringBuilder log)
        {
            string walkPath = AssetDatabase.GetAssetPath(walk);
            GameObject walkRig = AssetDatabase.LoadAssetAtPath<GameObject>(walkPath);
            var restWalk = RestOf(walkRig);
            var restModel = RestOf(model);
            var safe = new AnimationClip { name = "CatWalk_Safe", frameRate = 30f };
            float len = Mathf.Max(walk.length, 1f / 30f);
            int frames = Mathf.Max(2, Mathf.RoundToInt(len * 30f) + 1);

            // повороты из ходьбы, сгруппированные по кости
            var rotPaths = new System.Collections.Generic.HashSet<string>();
            int posDropped = 0;
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(walk))
            {
                if (b.type != typeof(Transform)) continue;
                if (b.propertyName.StartsWith("m_LocalRotation") || b.propertyName.StartsWith("localEulerAngles")) rotPaths.Add(b.path);
                else posDropped++;
            }
            int retargeted = 0;
            foreach (string path in rotPaths)
            {
                string bone = path.Substring(path.LastIndexOf('/') + 1);
                AnimationCurve cx = Get(walk, path, "m_LocalRotation.x"), cy = Get(walk, path, "m_LocalRotation.y"),
                               cz = Get(walk, path, "m_LocalRotation.z"), cw = Get(walk, path, "m_LocalRotation.w");
                // клип без «Resample Curves» хранит углы Эйлера
                AnimationCurve ex = Get(walk, path, "localEulerAnglesRaw.x") ?? Get(walk, path, "localEulerAngles.x"),
                               ey = Get(walk, path, "localEulerAnglesRaw.y") ?? Get(walk, path, "localEulerAngles.y"),
                               ez = Get(walk, path, "localEulerAnglesRaw.z") ?? Get(walk, path, "localEulerAngles.z");
                bool quat = cx != null && cy != null && cz != null && cw != null;
                bool euler = !quat && (ex != null || ey != null || ez != null);
                if (!quat && !euler) continue;
                Quaternion fix = Quaternion.identity;
                if (restWalk.TryGetValue(bone, out Quaternion rw) && restModel.TryGetValue(bone, out Quaternion rm))
                {
                    fix = rm * Quaternion.Inverse(rw);
                    if (Quaternion.Angle(rm, rw) > 1f) retargeted++;
                }
                var kx = new Keyframe[frames]; var ky = new Keyframe[frames]; var kz = new Keyframe[frames]; var kw = new Keyframe[frames];
                Quaternion prev = Quaternion.identity;
                for (int f = 0; f < frames; f++)
                {
                    float t = Mathf.Min(len, f / 30f);
                    Quaternion q = quat ? new Quaternion(cx.Evaluate(t), cy.Evaluate(t), cz.Evaluate(t), cw.Evaluate(t))
                                        : Quaternion.Euler(ex != null ? ex.Evaluate(t) : 0f, ey != null ? ey.Evaluate(t) : 0f, ez != null ? ez.Evaluate(t) : 0f);
                    q = Norm(fix * Norm(q));
                    if (f > 0 && Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);   // без рывков через знак
                    prev = q;
                    kx[f] = new Keyframe(t, q.x); ky[f] = new Keyframe(t, q.y); kz[f] = new Keyframe(t, q.z); kw[f] = new Keyframe(t, q.w);
                }
                Set(safe, path, "m_LocalRotation.x", kx); Set(safe, path, "m_LocalRotation.y", ky);
                Set(safe, path, "m_LocalRotation.z", kz); Set(safe, path, "m_LocalRotation.w", kw);
            }
            // остальные кости — из первого кадра Sneak (лапы опущены, не Т-поза)
            int filled = 0;
            var sneakRot = new System.Collections.Generic.HashSet<string>();
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(sneak))
                if (b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalRotation")) sneakRot.Add(b.path);
            foreach (string path in sneakRot)
            {
                if (rotPaths.Contains(path)) continue;
                foreach (string c in new[] { "x", "y", "z", "w" })
                {
                    AnimationCurve sc = Get(sneak, path, "m_LocalRotation." + c);
                    if (sc == null) continue;
                    float v = sc.Evaluate(0f);
                    Set(safe, path, "m_LocalRotation." + c, new[] { new Keyframe(0f, v), new Keyframe(len, v) });
                }
                filled++;
            }
            var st = AnimationUtility.GetAnimationClipSettings(safe);
            st.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(safe, st);
            safe.EnsureQuaternionContinuity();

            string outPath = Path.GetDirectoryName(PrefabPath).Replace('\\', '/') + "/CatWalk_Safe.anim";
            AnimationClip old = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
            if (old != null) { EditorUtility.CopySerialized(safe, old); safe = old; }
            else AssetDatabase.CreateAsset(safe, outPath);
            AssetDatabase.SaveAssets();
            log.AppendLine($"  ✔ ходьба «{walk.name}» → {outPath}: костей {rotPaths.Count} (пересчитано с другой позы покоя: {retargeted}), " +
                           $"выкинуто сдвигов/масштабов {posDropped}, недостающих костей из Sneak {filled}");
            if (rotPaths.Count == 0) log.AppendLine("  ⚠ в ходьбе нет поворотов костей — пути не совпали? (ожидается gamerig/DEF-…)");
            return safe;
        }

        /// <summary>
        /// v26.9: ПЕРЕНОС КЛИПА НА ДРУГУЮ ПОЗУ ПОКОЯ через мировые повороты.
        /// Источник — FBX клипа (Cat_Caper_X.fbx: в нём кости в «его» покое), цель — твоя модель.
        /// Для каждого кадра: поворот кости в мире у источника относительно его покоя (D = W_анимация × W_покой⁻¹)
        /// прикладывается к покою цели (W = D × W_покой_цели). Корень DEF-spine ещё и сдвигается (приседания, прыжки),
        /// сдвиг масштабируется по высоте таза. Остальные сдвиги и масштабы — не переносятся (кот не раздувается).
        /// </summary>
        static AnimationClip RetargetWorld(AnimationClip clip, GameObject targetModel, string name, StringBuilder log)
        {
            GameObject srcAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(clip));
            if (srcAsset == null) return clip;
            GameObject src = Object.Instantiate(srcAsset), dst = Object.Instantiate(targetModel);
            try
            {
                src.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                dst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var sMap = new System.Collections.Generic.Dictionary<string, Transform>();
                foreach (Transform t in src.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("DEF-") && !sMap.ContainsKey(t.name)) sMap[t.name] = t;
                // кости цели — от корня к листьям (родитель раньше ребёнка)
                var dBones = new System.Collections.Generic.List<Transform>();
                foreach (Transform t in dst.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("DEF-")) dBones.Add(t);
                var sRest = new System.Collections.Generic.Dictionary<string, Quaternion>();
                var dRest = new System.Collections.Generic.Dictionary<Transform, Quaternion>();
                var dRestLocal = new System.Collections.Generic.Dictionary<Transform, Quaternion>();
                foreach (var kv in sMap) sRest[kv.Key] = kv.Value.rotation;
                foreach (Transform t in dBones) { dRest[t] = t.rotation; dRestLocal[t] = t.localRotation; }
                Transform sHip = sMap.ContainsKey("DEF-spine") ? sMap["DEF-spine"] : null;
                Transform dHip = dBones.Find(t => t.name == "DEF-spine");
                Vector3 sHipRest = sHip != null ? sHip.position : Vector3.zero, dHipRest = dHip != null ? dHip.position : Vector3.zero;
                float hs = HeightOf(src), hd = HeightOf(dst);
                float hipK = hs > 1e-3f && hd > 1e-3f ? hd / hs : 1f;                // сдвиг таза — в масштабе модели
                Vector3 dHipRestLocal = dHip != null ? dHip.localPosition : Vector3.zero;

                float len = Mathf.Max(clip.length, 1f / 30f);
                int frames = Mathf.Max(2, Mathf.RoundToInt(len * 30f) + 1);
                var keys = new System.Collections.Generic.Dictionary<Transform, Keyframe[][]>();
                foreach (Transform t in dBones) { var k = new Keyframe[4][]; for (int c = 0; c < 4; c++) k[c] = new Keyframe[frames]; keys[t] = k; }
                var hipPos = new Keyframe[3][]; for (int c = 0; c < 3; c++) hipPos[c] = new Keyframe[frames];
                var prev = new System.Collections.Generic.Dictionary<Transform, Quaternion>();
                int mapped = 0;
                for (int f = 0; f < frames; f++)
                {
                    float t = Mathf.Min(len, f / 30f);
                    clip.SampleAnimation(src, t);
                    foreach (Transform b in dBones)
                    {
                        if (sMap.TryGetValue(b.name, out Transform sb))
                        {
                            Quaternion d = sb.rotation * Quaternion.Inverse(sRest[b.name]);
                            b.rotation = d * dRest[b];
                            if (f == 0) mapped++;
                        }
                        else b.localRotation = dRestLocal[b];
                    }
                    if (dHip != null && sHip != null)
                    {
                        Vector3 delta = (sHip.position - sHipRest) * hipK;
                        dHip.position = dHipRest + delta;
                    }
                    foreach (Transform b in dBones)
                    {
                        Quaternion q = b.localRotation;
                        if (prev.TryGetValue(b, out Quaternion pq) && Quaternion.Dot(pq, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        prev[b] = q;
                        var k = keys[b];
                        k[0][f] = new Keyframe(t, q.x); k[1][f] = new Keyframe(t, q.y); k[2][f] = new Keyframe(t, q.z); k[3][f] = new Keyframe(t, q.w);
                    }
                    if (dHip != null)
                    {
                        Vector3 lp = dHip.localPosition;
                        hipPos[0][f] = new Keyframe(t, lp.x); hipPos[1][f] = new Keyframe(t, lp.y); hipPos[2][f] = new Keyframe(t, lp.z);
                    }
                }
                var outClip = new AnimationClip { name = "Cat_" + name + "_OnModel", frameRate = 30f };
                foreach (Transform b in dBones)
                {
                    string path = AnimationUtility.CalculateTransformPath(b, dst.transform);
                    var k = keys[b];
                    Set(outClip, path, "m_LocalRotation.x", k[0]); Set(outClip, path, "m_LocalRotation.y", k[1]);
                    Set(outClip, path, "m_LocalRotation.z", k[2]); Set(outClip, path, "m_LocalRotation.w", k[3]);
                }
                if (dHip != null && sHip != null)
                {
                    string hp = AnimationUtility.CalculateTransformPath(dHip, dst.transform);
                    Set(outClip, hp, "m_LocalPosition.x", hipPos[0]); Set(outClip, hp, "m_LocalPosition.y", hipPos[1]); Set(outClip, hp, "m_LocalPosition.z", hipPos[2]);
                }
                var st = AnimationUtility.GetAnimationClipSettings(outClip);
                st.loopTime = name != "Pickup";
                AnimationUtility.SetAnimationClipSettings(outClip, st);
                outClip.EnsureQuaternionContinuity();
                string outPath = Path.GetDirectoryName(PrefabPath).Replace('\\', '/') + "/" + outClip.name + ".anim";
                AnimationClip old = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
                if (old != null) { EditorUtility.CopySerialized(outClip, old); outClip = old; }
                else AssetDatabase.CreateAsset(outClip, outPath);
                AssetDatabase.SaveAssets();
                log.AppendLine($"  ✔ {name} перенесён на твою модель → {Path.GetFileName(outPath)} (костей {mapped}/{dBones.Count}, таз ×{hipK:0.00})");
                return outClip;
            }
            finally { Object.DestroyImmediate(src); Object.DestroyImmediate(dst); }
        }

        static System.Collections.Generic.Dictionary<string, Quaternion> RestOf(GameObject go)
        {
            var d = new System.Collections.Generic.Dictionary<string, Quaternion>();
            if (go == null) return d;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("DEF-") && !d.ContainsKey(t.name)) d[t.name] = t.localRotation;
            return d;
        }

        static AnimationCurve Get(AnimationClip c, string path, string prop) =>
            AnimationUtility.GetEditorCurve(c, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop));

        static void Set(AnimationClip c, string path, string prop, Keyframe[] keys) =>
            AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop), new AnimationCurve(keys));

        static Quaternion Norm(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 1e-6f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }

        static AnimationClip ClipByName(string clipName, StringBuilder log)
        {
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip " + clipName))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                foreach (Object a in AssetDatabase.LoadAllAssetsAtPath(p))
                    if (a is AnimationClip c && c.name == clipName) { log.AppendLine($"  ✔ ходьба: клип «{c.name}» из {p}"); return c; }
            }
            return null;
        }

        /// <summary>Сравнить позу клипа с эталонным (Sneak) на кистях, плечах, голове и корне: сильно иначе — клип с другого скелета.</summary>
        static bool SameRig(GameObject model, AnimationClip test, AnimationClip reference, StringBuilder log)
        {
            string[] bones = { "DEF-hand.L", "DEF-hand.R", "DEF-upper_arm.L", "DEF-upper_arm.R", "DEF-spine.006", "DEF-spine" };
            var map = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (System.Array.IndexOf(bones, t.name) >= 0 && !map.ContainsKey(t.name)) map[t.name] = t;
            if (map.Count == 0) return true;
            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            var savedP = new Vector3[all.Length]; var savedR = new Quaternion[all.Length]; var savedS = new Vector3[all.Length];
            for (int i = 0; i < all.Length; i++) { savedP[i] = all[i].localPosition; savedR[i] = all[i].localRotation; savedS[i] = all[i].localScale; }
            var refRot = new System.Collections.Generic.Dictionary<string, Quaternion>();
            reference.SampleAnimation(model, Mathf.Min(0.3f, reference.length));
            Vector3 refRoot = map.ContainsKey("DEF-spine") ? map["DEF-spine"].localPosition : Vector3.zero;
            foreach (var kv in map) refRot[kv.Key] = kv.Value.localRotation;
            test.SampleAnimation(model, Mathf.Min(0.3f, test.length));
            float sum = 0f; int n = 0;
            foreach (var kv in map) { sum += Quaternion.Angle(refRot[kv.Key], kv.Value.localRotation); n++; }
            float avg = n > 0 ? sum / n : 0f;
            float shift = map.ContainsKey("DEF-spine") ? (map["DEF-spine"].localPosition - refRoot).magnitude : 0f;
            for (int i = 0; i < all.Length; i++) { all[i].localPosition = savedP[i]; all[i].localRotation = savedR[i]; all[i].localScale = savedS[i]; }
            log.AppendLine($"  проверка Walk против Sneak: поворот костей в среднем {avg:0}°, сдвиг корня {shift:0.00}");
            return avg < 55f && shift < 0.5f;
        }

        /// <summary>
        /// v26.3: модель кота. По порядку: выделенный в Project FBX с костями → Cat_Caper_Model → «кот с гейм ригом…» →
        /// любой FBX со скинованным мешем и костью DEF-hand.L.
        /// </summary>
        static string FindModel(StringBuilder log)
        {
            string sel = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            if (IsCatModel(sel)) return sel;
            // v26.9: ТВОЯ модель — первой (на ней работает твоя ходьба); клипы нейронки переносятся на неё
            foreach (string g in AssetDatabase.FindAssets("t:Model"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if ((n.Contains("гейм риг") || n.Contains("07.11.26") || n.Contains("game rig")) && IsCatModel(path)) return path;
            }
            string p = Find("Cat_Caper_Model");
            if (IsCatModel(p)) return p;
            p = Find("Cat_Caper_Sneak");
            if (IsCatModel(p)) return p;
            string fallback = null;
            foreach (string g in AssetDatabase.FindAssets("t:Model"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!IsCatModel(path)) continue;
                string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (n.Contains("гейм риг") || n.Contains("07.11.26") || n.Contains("game rig")) return path;
                if (fallback == null && !n.StartsWith("cat_caper_")) fallback = path;
            }
            return fallback;
        }

        static bool IsCatModel(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.ToLowerInvariant().EndsWith(".fbx")) return false;
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null || go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) return false;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "DEF-hand.L") return true;
            return false;
        }

        static float Height(string fbxPath)
        {
            if (string.IsNullOrEmpty(fbxPath)) return 0f;
            GameObject a = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (a == null) return 0f;
            GameObject t = Object.Instantiate(a);
            try { return HeightOf(t); } finally { Object.DestroyImmediate(t); }
        }

        static float HeightOf(GameObject go)
        {
            Bounds b = default; bool any = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b.size.y : 0f;
        }

        static string Find(string file)
        {
            foreach (string g in AssetDatabase.FindAssets(file))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == file && p.ToLowerInvariant().EndsWith(".fbx")) return p;
            }
            return null;
        }

        static AnimationClip Clip(string folder, string file, StringBuilder log)
        {
            string path = folder + "/" + file + ".fbx";
            if (!File.Exists(path)) path = Find(file);
            if (path == null)
            {
                AnimationClip byName = ClipByName(file, log);
                if (byName != null) return byName;
                log.AppendLine($"  ⚠ нет {file}.fbx");
                return null;
            }
            foreach (Object a in AssetDatabase.LoadAllAssetsAtPath(path))
                if (a is AnimationClip c && !c.name.StartsWith("__preview"))
                {
                    log.AppendLine($"  ✔ {file}: «{c.name}», {c.length:0.00} с{(c.isLooping ? ", цикл" : "")}");
                    return c;
                }
            log.AppendLine($"  ⚠ в {file}.fbx нет клипа (Reimport?)");
            return null;
        }
    }
}
#endif
