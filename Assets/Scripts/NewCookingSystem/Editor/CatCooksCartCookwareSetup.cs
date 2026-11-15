using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food.EditorTools
{
    /// <summary>
    /// v19.3: CatCooks → Тачка → Сделать тарой (выдели тачку в сцене).
    /// Корыто тачки становится тарой на 4 ведра (Cookware_Tachka, 80 порций): в него можно лить, сыпать,
    /// черпать лопатой; жижа плещется от разгона, кочек и крена, льётся через край при завале.
    /// Ничего не создаёт в виде префабов — только добавляет компоненты выделенной тачке.
    /// </summary>
    public static class CatCooksCartCookwareSetup
    {
        private const string DefName = "Cookware_Tachka";

        private static readonly string[] OldParts =
        {
            "CartCookwareLink", "CartBalanceUI", "CartDust", "CartCrash", "CartBalance", "CartRig", "CartWheel", "CartTrough", "PushCart"
        };

        private static GameObject FindCartRoot(GameObject sel)
        {
            if (sel == null) return null;
            FoodCart fc = sel.GetComponentInParent<FoodCart>();
            if (fc == null) fc = sel.GetComponentInChildren<FoodCart>();
            if (fc != null) return fc.gameObject;
            PushCart pc = sel.GetComponentInParent<PushCart>();
            if (pc == null) pc = sel.GetComponentInChildren<PushCart>();
            if (pc != null) return pc.gameObject;
            FishNet.Object.NetworkObject nob = sel.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null) return nob.gameObject;
            Rigidbody rb = sel.GetComponentInParent<Rigidbody>();
            return rb != null ? rb.gameObject : sel;
        }

        /// <summary>
        /// v20: тачка ЗАНОВО. Снимает старые компоненты (PushCart, CartRig, CartBalance, CartBalanceUI, CartCrash,
        /// CartDust, CartTrough, CartWheel, CartCookwareLink), ставит один FoodCart и делает корыто тарой.
        /// </summary>
        [MenuItem("CatCooks/Тачка/НОВАЯ тачка-тара (выдели тачку в сцене)")]
        public static void Make()
        {
            var log = new StringBuilder("[Тачка] новая тачка-тара:\n");
            GameObject sel = Selection.activeGameObject;
            if (sel == null) { Debug.LogError("[Тачка] выдели тачку в сцене"); return; }
            GameObject root = FindCartRoot(sel);
            if (root.GetComponent<Rigidbody>() == null) { Undo.AddComponent<Rigidbody>(root); log.AppendLine("  ✔ Rigidbody"); }
            if (root.GetComponent<FishNet.Object.NetworkObject>() == null)
                log.AppendLine("  ✖ на корне нет NetworkObject — добавь NetworkObject и NetworkTransform (Client Authoritative), иначе тачку не взять");

            Undo.RegisterFullObjectHierarchyUndo(root, "Новая тачка-тара");

            // 1) старые компоненты — снять (сами скрипты в проекте остаются)
            int removed = 0;
            for (int pass = 0; pass < 3; pass++)
                foreach (string typeName in OldParts)
                    foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null || mb.GetType().Name != typeName) continue;
                        Undo.DestroyObjectImmediate(mb);
                        removed++;
                    }
            if (removed > 0) log.AppendLine($"  ✔ сняты старые компоненты тачки: {removed} (подвеска, баланс, старое корыто, пыль, удары)");

            // 2) новая езда
            if (root.GetComponent<FoodCart>() == null) { Undo.AddComponent<FoodCart>(root); log.AppendLine("  ✔ FoodCart на корне: езда, завал ПКМ, колёса"); }

            // 3) корыто — тара
            Renderer body = FindTrough(root);
            if (body == null) { Debug.LogError("[Тачка] не нашёл меш корыта (ищу по имени wheelbarrow/корыто/trough/body, иначе самый большой не-колесо)", root); return; }
            GameObject t = body.gameObject;
            log.AppendLine($"  • корыто: «{t.name}» ({AnimationPath(root.transform, t.transform)})");

            CookwareDef def = FindDef();
            if (def == null) log.AppendLine($"  ✖ нет ассета {DefName} (тара «Тачка», 80 порций) — он был в архиве v19.3–v19.5");

            Cookware cw = t.GetComponent<Cookware>();
            if (cw == null) { cw = Undo.AddComponent<Cookware>(t); log.AppendLine("  ✔ Cookware на корыте"); }
            if (def != null && cw.Def != def) { cw.Def = def; log.AppendLine($"  ✔ Def = {def.name} ({def.Capacity} порц. = 4 ведра)"); }

            CookwareCavity cav = t.GetComponent<CookwareCavity>();
            if (cav == null) { cav = Undo.AddComponent<CookwareCavity>(t); log.AppendLine("  ✔ CookwareCavity (форма полости по модели)"); }

            // v20.1: коллайдеры — точно по мешам (корыто, рама, колёса). Тело кинематическое, невыпуклые можно.
            int added = 0;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (!CartBottomFilter.Counts(mf.GetComponent<Renderer>())) continue;
                if (mf.GetComponent<Collider>() != null) continue;
                MeshCollider mcol = Undo.AddComponent<MeshCollider>(mf.gameObject);
                mcol.sharedMesh = mf.sharedMesh;
                added++;
            }
            if (added > 0) log.AppendLine($"  ✔ коллайдеры по мешам: {added} (сквозь тачку больше не пройти, предметы лежат в корыте)");
            bool makeCols = false;
            var so = new SerializedObject(cav);
            SerializedProperty br = so.FindProperty("_bodyRenderers");
            if (br != null) { br.arraySize = 1; br.GetArrayElementAtIndex(0).objectReferenceValue = body; }
            SerializedProperty mc = so.FindProperty("_makeColliders");
            if (mc != null) mc.boolValue = makeCols;
            SerializedProperty sh = so.FindProperty("_shape");
            if (sh != null) sh.enumValueIndex = 0;          // Trough
            so.ApplyModifiedProperties();
            cav.EditorScan();
            cav.EditorBuild();
            log.AppendLine("  ✔ полость собрана: приём, горловина, горка, жидкость, пролив (CookwareSpill)");
            FitLiquidToTrough(t, body, log);

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                log.AppendLine("  ! тачка — копия префаба: Overrides → Apply All (сеть должна знать новые компоненты)");
            log.AppendLine("  Проверка: Play → F по тачке — катишь; ПКМ держать — завал вперёд; F — отпустить. В консоли строка «[Тачка] … колёс N, радиус …».");
            Debug.Log(log.ToString(), root);
        }

        // ───────────────────────── v21: старая тачка (мини-игра) + груз ─────────────────────────

        // настройки старых компонентов тачки из твоей сцены (коммит «al update») — меню v20 их сняло
        private const string V_CartTrough = "_maxItems=12;_maxMassKg=160;_mode=1;_settleSeconds=2;_settleMaxSpeed=0.35;_settleHardCap=6;_thawOnStumble=0;_alignRotation=1;_snapSpeed=22;_forceContinuous=1;_sleepWhenIdle=1;_dumpKick=1.6;_dumpSpread=0.45;_dumpSpin=3;_debugLog=0;_debugGizmo=1";
        private const string V_PushCart = "_canPickup=1;_groundFriction=1;_bounciness=0;_pushDistance=2.16;_speedEmpty=0.85;_speedFull=0.5;_followSharpness=12;_leadCompensation=1;_tipAngle=75;_tipSeconds=0.5;_kickAt=0.5;_autoReturnSeconds=2;_flipSideSpeed=2.2;_flipLift=1.2;_flipSpin=7;_stumbleRoll=35;_stumbleSeconds=0.7;_turnRateDegPerSec=45;_debugLog=0;_groundMask=8";
        private const string V_CartBalanceUI = "_style=0;_layout=1;_size=150;_spread=340;_verticalOffset=-0.22;_colorIdle={r: 0.55, g: 0.58, b: 0.62, a: 1};_colorWarn={r: 0.95, g: 0.72, b: 0.2, a: 1};_colorCall={r: 1, g: 0.55, b: 0.1, a: 1};_colorHit={r: 0.37, g: 0.76, b: 0.42, a: 1};_colorMiss={r: 0.72, g: 0.22, b: 0.18, a: 1};_colorDim={r: 0.3, g: 0.32, b: 0.35, a: 0.55};_idlePulse=1.04;_callPulse=1.14;_idlePeriod=1.6;_callPeriod=0.42;_hitPop=1.3;_missShake=14;_fade=0.15;_ringMax=1.55;_showBrake=1;_brakeSize=118;_brakeOffsetY=-0.4;_colorBrake={r: 0.85, g: 0.28, b: 0.22, a: 1};_colorBraking={r: 0.42, g: 0.72, b: 0.95, a: 1};_showLeanBar=1;_barWidth=420;_barHeight=12;_barOffset=-0.36";
        private const string V_CartBalance = "_valueEmpty=0.12;_valueNormal=1;_valueOverload=1.6;_valueSuper=2.4;_debugLoadKeys=1;_keyEmpty=56;_keyNormal=57;_keyOverload=48;_keySuper=45;_level=1;_useRealMass=0;_fromTurn=1.4;_speedAmplify=0.9;_fromBumps=0.35;_bumpFrequency=1.6;_recovery=0.8;_leanInertia=0.55;_warnAt=0.4;_telegraph=0.35;_windowNormal=1;_windowOverload=0.65;_cooldown=0.9;_pushBack=0.8;_missAdds=0.45;_missStumble=0.55;_tipAt=1;_tipStumble=1;_throwAt=0.55;_throwUp=5.5;_throwForward=6;_throwLockout=2.5;_momentumFrom=1;_momentumGain=0.7;_carryPush=2.6;_brakeRate=1.1;_coastDrain=0.06;_brakePromptAt=0.3;_momentumLean=0.8;_visualRollDeg=22;_visualLift=0.09;_visualSmooth=0.12;_affectPosition=1;_wheelieEnabled=1;_wheelieFrom=0.32;_wheelieFull=0.85;_wheelieExtraDeg=14;_wheelieFromTurn=0.35;_wheelieSmooth=0.14;_flipWheelieSide=0;_leanDirect=2.2;_leanSteerDeg=70;_flipEnabled=1;_flipOnAnyMiss=1;_flipPlayerPush=3.2;_flipPlayerLift=2;_showReadout=0;_debugLog=0";
        private const string V_CartRig = "_travel=0.13;_stiffness=220;_damping=0.6;_heave=0.8;_rollPerMeter=95;_pitchPerMeter=75;_maxAngle=15;_smooth=0.06;_impactAt=1.2;_shakeRoll=0.9;_shakePitch=1.2;_shakeHeave=0.012;_shakeFreq=4.5;_shakeAtRest=0.18;_shakeFullAt=2.5;_hopsEnabled=1;_hopEveryMin=1.8;_hopEveryMax=5;_hopForce=0.8;_hopUneven=0.7;_hopMinSpeed=0.6;_squashFromMotion=0.35;_squashFromImpact=0.5;_squashLimit=0.18;_squashWidth=0.5;_squashSmooth=0.05;_squashSpeedRef=1.2;_squashRolling=0.06;_wheelieEnabled=1;_idleWhenFree=1;_spinDeadzone=0.0015;_drawGizmos=1;_groundMask=8";
        private const string V_CartDust = "_color={r: 0.78, g: 0.72, b: 0.58, a: 0.55};_quality=1;_fullSpeed=2.6;_rollRate=14;_rollSize=0.32;_rollLife=1.1;_rollRise=0.35;_driftAt=55;_driftRate=40;_driftSize=0.55;_driftLife=1.8;_driftRise=2.4;_driftSide=1.3;_dustOnLoad=1;_loadPuff=12;_loadSize=0.4;_loadLife=1.3;_dustOnImpact=1;_impactPuff=8;_drawGizmos=0";

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static T Ensure<T>(GameObject go, string values, StringBuilder log) where T : Component
        {
            T c = go.GetComponent<T>();
            if (c != null) return c;
            c = Undo.AddComponent<T>(go);
            ApplyValues(c, values);
            log.AppendLine($"  ✔ {typeof(T).Name} возвращён (настройки — из твоей сцены)");
            return c;
        }

        private static float F(string s) { return float.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture); }

        private static float Part(string v, string key, float def)
        {
            int i = v.IndexOf(key + ":", System.StringComparison.Ordinal);
            if (i < 0) return def;
            int a = i + key.Length + 1, b = v.IndexOfAny(new[] { ',', '}' }, a);
            return b > a ? F(v.Substring(a, b - a)) : def;
        }

        /// <summary>Записать простые поля «имя=значение;…» в компонент.</summary>
        private static void ApplyValues(Component c, string data)
        {
            if (c == null || string.IsNullOrEmpty(data)) return;
            var so = new SerializedObject(c);
            foreach (string item in data.Split(';'))
            {
                int eq = item.IndexOf('=');
                if (eq <= 0) continue;
                string name = item.Substring(0, eq), v = item.Substring(eq + 1);
                SerializedProperty p = so.FindProperty(name);
                if (p == null) continue;
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Float: p.floatValue = F(v); break;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.Enum: p.intValue = (int)F(v); break;
                    case SerializedPropertyType.Boolean: p.boolValue = v.Trim() != "0"; break;
                    case SerializedPropertyType.Color: p.colorValue = new Color(Part(v, "r", 1f), Part(v, "g", 1f), Part(v, "b", 1f), Part(v, "a", 1f)); break;
                    case SerializedPropertyType.Vector3: p.vector3Value = new Vector3(Part(v, "x", 0f), Part(v, "y", 0f), Part(v, "z", 0f)); break;
                    case SerializedPropertyType.Vector2: p.vector2Value = new Vector2(Part(v, "x", 0f), Part(v, "y", 0f)); break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRef(Component c, string prop, Object value)
        {
            if (c == null) return;
            var so = new SerializedObject(c);
            SerializedProperty p = so.FindProperty(prop);
            if (p == null) return;
            if (p.objectReferenceValue == null && value != null) { p.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        /// <summary>
        /// v21: вернуть тачке физику и мини-игру (PushCart, CartRig, CartBalance, CartBalanceUI, CartDust, CartTrough —
        /// с настройками из твоей сцены), снять FoodCart. Корыто — тара для ГРУЗА: твёрдое и густое лежит неподвижно,
        /// жидкость вытекает. Стояночный тормоз — тачка не катается сама.
        /// </summary>
        [MenuItem("CatCooks/Тачка/Тачка с физикой и мини-игрой + груз (выдели тачку)")]
        public static void MakePhysical()
        {
            var log = new StringBuilder("[Тачка] физика, мини-игра и груз:\n");
            GameObject sel = Selection.activeGameObject;
            if (sel == null) { Debug.LogError("[Тачка] выдели тачку в сцене"); return; }
            GameObject root = FindCartRoot(sel);
            Undo.RegisterFullObjectHierarchyUndo(root, "Тачка: физика и груз");

            // 1) новая «замороженная» тачка — снять
            foreach (FoodCart fc in root.GetComponentsInChildren<FoodCart>(true)) { Undo.DestroyObjectImmediate(fc); log.AppendLine("  ✔ FoodCart снят"); }
            foreach (CartCookwareLink l in root.GetComponentsInChildren<CartCookwareLink>(true)) Undo.DestroyObjectImmediate(l);
            // коллайдеры по мешам (ставило меню v20.1) с динамическим телом несовместимы — убрать
            foreach (MeshCollider mcol in root.GetComponentsInChildren<MeshCollider>(true))
                if (mcol != null && !mcol.convex) { Undo.DestroyObjectImmediate(mcol); log.AppendLine("  ✔ убран невыпуклый MeshCollider (с физикой не работает)"); }

            // 2) тело — снова физическое
            Rigidbody rb = root.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(root);
            rb.isKinematic = false; rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // 3) старые компоненты — вернуть
            Transform tubes = FindDeep(root.transform, "tubes");
            Transform tires = FindDeep(root.transform, "tires"), tires1 = FindDeep(root.transform, "tires.001");
            Transform os1 = FindDeep(root.transform, "os1"), os2 = FindDeep(root.transform, "os2");
            Transform axle = FindDeep(root.transform, "GameObject (2)");

            PushCart pc = Ensure<PushCart>(root, V_PushCart, log);
            SetRef(pc, "_axle", axle);
            CartRig rig = Ensure<CartRig>(root, V_CartRig, log);
            SetRef(rig, "_body", tubes);
            SetRef(rig, "_squashTarget", tubes);
            var rso = new SerializedObject(rig);
            SerializedProperty wheels = rso.FindProperty("_wheels");
            if (wheels != null && wheels.arraySize == 0 && tires != null && tires1 != null)
            {
                wheels.arraySize = 2;
                for (int i = 0; i < 2; i++)
                {
                    SerializedProperty w = wheels.GetArrayElementAtIndex(i);
                    SerializedProperty mesh = w.FindPropertyRelative("mesh"), mount = w.FindPropertyRelative("mount"), radius = w.FindPropertyRelative("radius");
                    if (mesh != null) mesh.objectReferenceValue = i == 0 ? tires : tires1;
                    if (mount != null) mount.objectReferenceValue = i == 0 ? os1 : os2;
                    if (radius != null) radius.floatValue = 0.36038208f;
                }
                rso.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("  ✔ колёса: tires + os1, tires.001 + os2");
            }
            CartBalance bal = Ensure<CartBalance>(root, V_CartBalance, log);
            SetRef(bal, "_rig", rig);
            CartBalanceUI ui = Ensure<CartBalanceUI>(root, V_CartBalanceUI, log);
            SetRef(ui, "_balance", bal);
            CartDust dust = Ensure<CartDust>(root, V_CartDust, log);
            SetRef(dust, "_loadPoint", tubes);

            // корыто для предметов (CartTrough) — на объекте с триггером-объёмом
            BoxCollider vol = null;
            if (tubes != null)
                foreach (BoxCollider b in tubes.GetComponentsInChildren<BoxCollider>(true))
                    if (b.isTrigger && b.transform.parent == tubes && b.GetComponent<CookwareTriggerRelay>() == null && b.name != "Receive") { vol = b; break; }
            if (vol != null)
            {
                CartTrough tr = Ensure<CartTrough>(vol.gameObject, V_CartTrough, log);
                SetRef(tr, "_volume", vol);
            }
            else log.AppendLine("  ! не нашёл триггер-объём корыта (tubes/GameObject с Box Collider IsTrigger) — CartTrough не возвращён");

            if (tubes == null || tires == null || os1 == null || axle == null)
                log.AppendLine("  ! не все части найдены по именам (tubes, tires, tires.001, os1, os2, GameObject (2)) — проверь ссылки в PushCart (Axle) и CartRig (Wheels, Body)");

            // 4) груз
            Cookware cw = root.GetComponentInChildren<Cookware>(true);
            if (cw == null) log.AppendLine("  ! в тачке нет тары — сначала было меню «НОВАЯ тачка-тара»; корыто останется просто корытом");
            if (root.GetComponent<CartCargo>() == null) { Undo.AddComponent<CartCargo>(root); log.AppendLine("  ✔ CartCargo: груз статичен, жидкость вытекает, стояночный тормоз"); }

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                log.AppendLine("  ! тачка — копия префаба: Overrides → Apply All");
            log.AppendLine("  Проверка: Play → F — катишь (занос, баланс, как раньше); отпустил — встала и не катается; масло лопатой в корыто — лежит; молоко — вытекает.");
            Debug.Log(log.ToString(), root);
        }

        /// <summary>v19.4: откат — убрать тару с тачки (сравнить поведение тачки без неё).</summary>
        [MenuItem("CatCooks/Тачка/Убрать тару (выдели тачку)")]
        public static void Remove()
        {
            GameObject sel = Selection.activeGameObject;
            if (sel == null) { Debug.LogError("[Тачка-тара] выдели тачку"); return; }
            GameObject root = FindCartRoot(sel);
            Undo.RegisterFullObjectHierarchyUndo(root, "Тачка — убрать тару");
            int n = 0;
            foreach (CartCookwareLink l in root.GetComponentsInChildren<CartCookwareLink>(true)) { Undo.DestroyObjectImmediate(l); n++; }
            foreach (CookwareSpill c in root.GetComponentsInChildren<CookwareSpill>(true)) { Undo.DestroyObjectImmediate(c); n++; }
            foreach (CookwareCavity c in root.GetComponentsInChildren<CookwareCavity>(true))
            {
                foreach (string child in new[] { "Receive", "Mouth", "HeapAnchor", "LiquidAnchor", "AutoColliders" })
                {
                    Transform t = c.transform.Find(child);
                    if (t != null) { Undo.DestroyObjectImmediate(t.gameObject); n++; }
                }
                Undo.DestroyObjectImmediate(c); n++;
            }
            foreach (Cookware c in root.GetComponentsInChildren<Cookware>(true)) { Undo.DestroyObjectImmediate(c); n++; }
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[Тачка-тара] «{root.name}»: тара убрана ({n} объектов/компонентов). Тачка — как до меню «Сделать тарой»", root);
        }

        /// <summary>
        /// v20.1: ВОДА ПО ФОРМЕ КОРЫТА. Режем меш корыта горизонтальными плоскостями (в осях жидкости) и на каждой
        /// высоте меряем: длину, ширину у заднего и у переднего борта. Корыто тачки — трапеция с наклонным носом;
        /// прямоугольник «по вписанному» не доходил до бортов, и было видно дно.
        /// </summary>
        private static void FitLiquidToTrough(GameObject trough, Renderer body, StringBuilder log)
        {
            CookwareLiquidVisual lv = trough.GetComponentInChildren<CookwareLiquidVisual>(true);
            MeshFilter mf = body.GetComponent<MeshFilter>();
            if (lv == null || mf == null || mf.sharedMesh == null) { log.AppendLine("  ! форма воды: нет жидкости или меша корыта — пропуск"); return; }
            var so = new SerializedObject(lv);
            SerializedProperty bp = so.FindProperty("_boxProfile"), tp = so.FindProperty("_boxTaper"), mh = so.FindProperty("_maxHeight");
            if (bp == null || tp == null || mh == null) { log.AppendLine("  ! форма воды: обнови CookwareLiquidVisual.cs из архива v20.1"); return; }
            float D = mh.floatValue;
            const int N = 12;
            Vector3[] v = mf.sharedMesh.vertices;
            int[] tri = mf.sharedMesh.triangles;
            Matrix4x4 m = Matrix4x4.TRS(lv.transform.position, lv.transform.rotation, Vector3.one).inverse * body.transform.localToWorldMatrix;
            var q = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) q[i] = m.MultiplyPoint3x4(v[i]);

            // два прохода: сужение вдоль Z (ширина X меняется) и вдоль X; берём то, где трапеция сильнее
            var profZ = new Vector4[N]; var taperZ = new float[N]; var okZ = new bool[N];
            var profX = new Vector4[N]; var taperX = new float[N]; var okX = new bool[N];
            var pts = new System.Collections.Generic.List<Vector2>();
            float sumZ = 0f, sumX = 0f;
            for (int k = 0; k < N; k++)
            {
                float y = Mathf.Clamp(D * k / (N - 1), D * 0.04f, D * 0.985f);
                pts.Clear();
                for (int i = 0; i + 2 < tri.Length; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        Vector3 a = q[tri[i + e]], b = q[tri[i + (e + 1) % 3]];
                        if ((a.y - y) * (b.y - y) > 0f || Mathf.Abs(a.y - b.y) < 1e-7f) continue;
                        Vector3 p = Vector3.Lerp(a, b, (y - a.y) / (b.y - a.y));
                        pts.Add(new Vector2(p.x, p.z));
                    }
                if (pts.Count < 4) continue;
                okZ[k] = Slice(pts, false, out profZ[k], out taperZ[k]);
                okX[k] = Slice(pts, true, out profX[k], out taperX[k]);
                sumZ += Mathf.Abs(taperZ[k]); sumX += Mathf.Abs(taperX[k]);
            }
            bool alongX = sumX > sumZ * 1.2f;
            Vector4[] prof = alongX ? profX : profZ;
            float[] taper = alongX ? taperX : taperZ;
            bool[] ok = alongX ? okX : okZ;
            SerializedProperty ax = so.FindProperty("_boxTaperAlongX");
            if (ax != null) ax.boolValue = alongX;
            int first = -1;
            for (int k = 0; k < N; k++) if (ok[k]) { first = k; break; }
            if (first < 0) { log.AppendLine("  ! форма воды: плоскости не пересекли меш корыта — осталась форма от полости"); return; }
            for (int k = 0; k < N; k++)
            {
                if (ok[k]) continue;
                int j = k;
                while (j >= 0 && !ok[j]) j--;
                if (j < 0) { j = k; while (j < N && !ok[j]) j++; }
                prof[k] = prof[j]; taper[k] = taper[j];
            }
            bp.arraySize = N; tp.arraySize = N;
            for (int k = 0; k < N; k++) { bp.GetArrayElementAtIndex(k).vector4Value = prof[k]; tp.GetArrayElementAtIndex(k).floatValue = taper[k]; }
            SerializedProperty form = so.FindProperty("_form");
            if (form != null) form.enumValueIndex = 1;                       // Box
            SerializedProperty hs = so.FindProperty("_halfSize");
            if (hs != null) hs.vector2Value = new Vector2(prof[N - 1].x, prof[N - 1].y);
            so.ApplyModifiedProperties();
            log.AppendLine($"  ✔ форма воды по мешу корыта: у дна {prof[0].x * 2f:0.00}×{prof[0].y * 2f:0.00} м, у края {prof[N - 1].x * 2f:0.00}×{prof[N - 1].y * 2f:0.00} м, " +
                           $"сужение у края {taper[N - 1] * 100f:+0;-0}% (вода доходит до бортов)");
        }

        /// <summary>Сечение на одной высоте: размеры, центр и сужение. swap — меряем вдоль X (иначе вдоль Z).</summary>
        private static bool Slice(System.Collections.Generic.List<Vector2> pts, bool swap, out Vector4 prof, out float taper)
        {
            prof = Vector4.zero; taper = 0f;
            // l — вдоль длины (ось сужения), w — поперёк
            float lMin = float.MaxValue, lMax = float.MinValue, wMin = float.MaxValue, wMax = float.MinValue;
            foreach (Vector2 p in pts)
            {
                float l = swap ? p.x : p.y, w = swap ? p.y : p.x;
                lMin = Mathf.Min(lMin, l); lMax = Mathf.Max(lMax, l); wMin = Mathf.Min(wMin, w); wMax = Mathf.Max(wMax, w);
            }
            float len = lMax - lMin, wAll = wMax - wMin;
            if (len < 1e-3f || wAll < 1e-3f) return false;
            float bMin = float.MaxValue, bMax = float.MinValue, fMin = float.MaxValue, fMax = float.MinValue;
            foreach (Vector2 p in pts)
            {
                float l = swap ? p.x : p.y, w = swap ? p.y : p.x;
                if (l <= lMin + len * 0.12f) { bMin = Mathf.Min(bMin, w); bMax = Mathf.Max(bMax, w); }
                if (l >= lMax - len * 0.12f) { fMin = Mathf.Min(fMin, w); fMax = Mathf.Max(fMax, w); }
            }
            float wB = bMax - bMin, wF = fMax - fMin;
            if (wB < wAll * 0.2f) { wB = wAll; bMin = wMin; bMax = wMax; }
            if (wF < wAll * 0.2f) { wF = wAll; fMin = wMin; fMax = wMax; }
            const float inset = 0.985f;                                       // чуть внутрь — не торчать сквозь лист
            float halfW = (wB + wF) * 0.25f * inset, halfL = len * 0.5f * inset;
            float cW = (bMin + bMax + fMin + fMax) * 0.25f, cL = (lMin + lMax) * 0.5f;
            prof = swap ? new Vector4(halfL, halfW, cL, cW) : new Vector4(halfW, halfL, cW, cL);
            taper = (wF - wB) / Mathf.Max(1e-4f, wF + wB);
            return true;
        }

        private static CookwareDef FindDef()
        {
            foreach (string g in AssetDatabase.FindAssets(DefName + " t:CookwareDef"))
            {
                CookwareDef d = AssetDatabase.LoadAssetAtPath<CookwareDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.name == DefName) return d;
            }
            return null;
        }

        private static Renderer FindTrough(GameObject root)
        {
            Renderer best = null;
            float bestV = 0f;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                if (n.Contains("tire") || n.Contains("wheel ") || n.Contains("колес")) continue;
                if (r.transform == root.transform) continue;
                if (n.Contains("wheelbarrow") || n.Contains("корыт") || n.Contains("trough") || n.Contains("body") || n.Contains("кузов"))
                    return r;
                Vector3 s = r.bounds.size;
                float v = s.x * s.y * s.z;
                if (v > bestV) { bestV = v; best = r; }
            }
            return best;
        }

        private static string AnimationPath(Transform root, Transform t)
        {
            string p = t.name;
            while (t.parent != null && t.parent != root) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
