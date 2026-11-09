#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// ДИАГНОСТИКА КАСТРЮЛИ. Меню CatCooks → Диагностика кастрюли.
///
/// Сканирует проект и сцену и пишет, что не так:
///   • скрипты: ошибки компиляции, ДУБЛИКАТЫ файлов, СТАРЫЕ версии (новых полей нет);
///   • каждая кастрюля (Cookware Heat): вода в Products, LidSocket на кромке, градусник
///     (модель, размер в мире), деформация (Read/Write), дыры (шейдер), эффекты;
///   • крышки в сцене: сеть, физика, размер, слой;
///   • костры: точка стыковки, список Accept;
///   • В PLAY — живые значения: температура, огонь под кастрюлей, вода, кипение, есть ли
///     эффекты (сколько частиц), где крышка относительно гнезда, почему скрыт градусник.
///
/// Всё через отражение — окно компилируется, даже если остальные скрипты старых версий.
/// Кнопка «Копировать отчёт» — вставь текст в чат.
/// </summary>
public sealed class CatCooksPotDoctor : EditorWindow
{
    private enum Lv { Ok, Info, Warn, Err }
    private struct Line { public Lv L; public string T; }

    private readonly List<Line> _lines = new List<Line>();
    private Vector2 _scroll;
    private bool _auto = true;
    private double _next;

    [MenuItem("CatCooks/Диагностика кастрюли", priority = 0)]
    public static void Open()
    {
        var w = GetWindow<CatCooksPotDoctor>("Диагностика кастрюли");
        w.minSize = new Vector2(520, 400);
        w.Scan();
    }

    private void OnInspectorUpdate()
    {
        if (_auto && Application.isPlaying && EditorApplication.timeSinceStartup >= _next)
        {
            _next = EditorApplication.timeSinceStartup + 1.0;
            Scan();
            Repaint();
        }
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Сканировать", GUILayout.Height(26))) Scan();
            if (GUILayout.Button("Копировать отчёт", GUILayout.Height(26)))
            {
                EditorGUIUtility.systemCopyBuffer = Report();
                ShowNotification(new GUIContent("Отчёт скопирован — вставь в чат"));
            }
            _auto = GUILayout.Toggle(_auto, "Обновлять в Play", GUILayout.Width(140));
        }
        int err = _lines.Count(l => l.L == Lv.Err), warn = _lines.Count(l => l.L == Lv.Warn);
        EditorGUILayout.HelpBox(err == 0 && warn == 0 ? "Проблем не найдено" : $"Ошибок: {err}   Предупреждений: {warn}",
                                err > 0 ? MessageType.Error : warn > 0 ? MessageType.Warning : MessageType.Info);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        var st = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
        foreach (Line l in _lines)
        {
            string c = l.L == Lv.Err ? "#ff5a4a" : l.L == Lv.Warn ? "#ffb02e" : l.L == Lv.Ok ? "#5ad16a" : "#c8c8c8";
            string p = l.L == Lv.Err ? "✖ " : l.L == Lv.Warn ? "▲ " : l.L == Lv.Ok ? "✔ " : "";
            if (l.T.StartsWith("##")) { GUILayout.Space(6); GUILayout.Label($"<b>{l.T.Substring(2).Trim()}</b>", st); continue; }
            GUILayout.Label($"<color={c}>{p}{l.T}</color>", st);
        }
        EditorGUILayout.EndScrollView();
    }

    private string Report()
    {
        var sb = new StringBuilder("ДИАГНОСТИКА КАСТРЮЛИ " + DateTime.Now.ToString("HH:mm:ss") + (Application.isPlaying ? " (PLAY)" : "") + "\n");
        foreach (Line l in _lines)
            sb.AppendLine((l.L == Lv.Err ? "[ОШИБКА] " : l.L == Lv.Warn ? "[!] " : l.L == Lv.Ok ? "[ok] " : "") + l.T);
        return sb.ToString();
    }

    private void H(string t) => _lines.Add(new Line { L = Lv.Info, T = "## " + t });
    private void Ok(string t) => _lines.Add(new Line { L = Lv.Ok, T = t });
    private void Inf(string t) => _lines.Add(new Line { L = Lv.Info, T = t });
    private void W(string t) => _lines.Add(new Line { L = Lv.Warn, T = t });
    private void E(string t) => _lines.Add(new Line { L = Lv.Err, T = t });

    // ───────────────────────── ОТРАЖЕНИЕ ─────────────────────────

    private static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type T(string name)
    {
        if (_types.TryGetValue(name, out Type t)) return t;
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] ts;
            try { ts = a.GetTypes(); } catch { continue; }
            foreach (Type x in ts)
                if (x.Name == name && typeof(Component).IsAssignableFrom(x)) { t = x; break; }
            if (t != null) break;
        }
        if (t == null)
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] ts;
                try { ts = a.GetTypes(); } catch { continue; }
                t = ts.FirstOrDefault(x => x.Name == name);
                if (t != null) break;
            }
        _types[name] = t;
        return t;
    }

    private static FieldInfo Fi(Type t, string f)
    {
        for (; t != null; t = t.BaseType)
        {
            FieldInfo fi = t.GetField(f, All | BindingFlags.DeclaredOnly);
            if (fi != null) return fi;
        }
        return null;
    }

    private static object F(object o, string f)
    {
        if (o == null) return null;
        FieldInfo fi = Fi(o.GetType(), f);
        if (fi == null) return null;
        object v = fi.GetValue(o);
        // SyncVar<T> → Value
        if (v != null && v.GetType().Name.StartsWith("SyncVar"))
        {
            PropertyInfo pv = v.GetType().GetProperty("Value", All);
            if (pv != null) { try { return pv.GetValue(v); } catch { } }
        }
        return v;
    }

    private static object P(object o, string p)
    {
        if (o == null) return null;
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            PropertyInfo pi = t.GetProperty(p, All | BindingFlags.DeclaredOnly);
            if (pi != null) { try { return pi.GetValue(o); } catch { return null; } }
        }
        return null;
    }

    private static bool Has(Type t, string member) =>
        t != null && (Fi(t, member) != null || t.GetMethod(member, All | BindingFlags.Static) != null ||
                      t.GetProperty(member, All | BindingFlags.Static) != null);

    private static Component C(GameObject go, string type)
    {
        Type t = T(type);
        return t == null || go == null ? null : go.GetComponent(t);
    }

    private static Component CC(GameObject go, string type)
    {
        Type t = T(type);
        return t == null || go == null ? null : go.GetComponentInChildren(t, true);
    }

    private static string N(object o) => o is Object uo && uo != null ? uo.name : "—";

    private static string Path(Transform t)
    {
        if (t == null) return "—";
        string s = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    private static Component[] Find(string type)
    {
        Type t = T(type);
        if (t == null) return new Component[0];
        return Object.FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None).OfType<Component>()
                     .Where(c => c != null && c.gameObject.scene.IsValid()).ToArray();
    }

    private static bool WorldBounds(Component root, out Bounds b, Func<Renderer, bool> filter = null)
    {
        b = default(Bounds); bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (filter != null && !filter(r)) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }

    private static string Cm(float m) => m < 1f ? $"{m * 100f:0.#} см" : $"{m:0.##} м";

    // ───────────────────────── СКАН ─────────────────────────

    private void Scan()
    {
        _lines.Clear();
        try
        {
            ScanScripts();
            ScanPots();
            ScanLids();
            ScanFires();
        }
        catch (Exception ex)
        {
            E("Сам сканер упал: " + ex.GetType().Name + ": " + ex.Message);
        }
        if (!Application.isPlaying) Debug.Log("[Диагностика кастрюли]\n" + Report());
    }

    // ── 1. Скрипты ──
    private void ScanScripts()
    {
        H("1. СКРИПТЫ");
        if (EditorUtility.scriptCompilationFailed)
            E("В проекте ОШИБКИ КОМПИЛЯЦИИ — Unity работает на старой сборке, новые правки не действуют. Открой Console, исправь первую красную ошибку.");
        else Ok("компиляция без ошибок");

        string[] names = { "Cookware", "CookwareHeat", "BoilFx", "PotBodyFilter", "CookwareLiquidVisual", "CookwareDock",
                           "CookwarePressure", "PotLidSocket", "PotLidItem", "PotDamage", "DeformableMesh", "PotEffects",
                           "PotThermometer3D", "BonfirePit", "BonfireTestKeys" };
        foreach (string n in names)
        {
            var paths = AssetDatabase.FindAssets(n + " t:MonoScript")
                                     .Select(AssetDatabase.GUIDToAssetPath)
                                     .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == n).ToList();
            if (paths.Count == 0) E($"{n}.cs — НЕ НАЙДЕН в проекте");
            else if (paths.Count > 1) E($"{n}.cs — ДУБЛИКАТЫ ({paths.Count}): " + string.Join("  |  ", paths) + " — оставь один (новый), остальные удали");
        }

        // Метки новых версий: если поля нет — в проекте старый файл.
        (string type, string member, string archive)[] marks =
        {
            ("CookwareHeat", "_heatBoost", "Pot_v2.2"), ("CookwareHeat", "_prettyFx", "Boil_Pretty_v1"),
            ("BoilFx", "Surface", "Pot_v2.2"), ("PotBodyFilter", "IsBody", "Pot_v2.1"),
            ("PotDamage", "_autoDamageMaterial", "Pot_v2"), ("DeformableMesh", "_strength", "Pot_v2"),
            ("PotThermometer3D", "_autoCalibrate", "Pot_v2"), ("PotThermometer3D", "_keepSceneScale", "Boil_Pretty_v1"),
            ("PotLidSocket", "OnDrawGizmos", "Pot_v2.1"), ("CookwarePressure", "_why", "Pot_v2"),
            ("CookwareDock", "_instantSnap", "Boil_Pretty_v1"), ("BonfirePit", "ServerDebugIgnite", "Ladder_v2_Ignite"),
            ("BonfirePit", "_gapFlamesMax", "Bonfire_v2"), ("BonfireTestKeys", "_lidKey", "Pot_v2"),
        };
        int old = 0;
        foreach (var m in marks)
        {
            Type t = T(m.type);
            if (t == null) continue;
            if (!Has(t, m.member)) { E($"{m.type} — СТАРАЯ версия (нет «{m.member}»). Замени файлом из архива {m.archive} или новее."); old++; }
        }
        if (old == 0) Ok("все файлы кастрюли/костра — новых версий");

        if (Shader.Find("CatCooks/Toon Props Damage") == null)
            W("шейдер «CatCooks/Toon Props Damage» не найден — дыр видно не будет");
    }

    // ── 2. Кастрюли ──
    private void ScanPots()
    {
        Component[] heats = Find("CookwareHeat");
        H($"2. КАСТРЮЛИ (с Cookware Heat): {heats.Length}");
        if (heats.Length == 0) { W("в сцене нет ни одной тары с Cookware Heat"); return; }

        foreach (Component heat in heats)
        {
            GameObject go = heat.gameObject;
            H($"«{go.name}»  ({Path(go.transform)})");
            if (!go.activeInHierarchy) W("объект выключен");
            Inf($"масштаб корня {go.transform.lossyScale.x:0.###}, слой «{LayerMask.LayerToName(go.layer)}»");
            if (C(go, "NetworkObject") == null) E("нет NetworkObject");
            if (go.GetComponent<Rigidbody>() == null) W("нет Rigidbody");

            Component cw = C(go, "Cookware");
            if (cw == null) E("нет Cookware");
            else
            {
                object def = F(cw, "Def");
                object cap = F(def, "Capacity") ?? P(def, "Capacity");
                if (def == null) E("Cookware → Def пустой");
                else Inf($"Def «{N(def)}», вместимость {cap}");
            }

            // корпус
            Type filter = T("PotBodyFilter");
            MethodInfo isBody = filter?.GetMethod("IsBody", BindingFlags.Public | BindingFlags.Static);
            bool BodyF(Renderer r) => isBody == null || (bool)isBody.Invoke(null, new object[] { r, go.transform });
            bool hasBody = WorldBounds(go.transform, out Bounds body, BodyF);
            if (hasBody)
            {
                Inf($"корпус: ширина {Cm(Mathf.Max(body.size.x, body.size.z))}, высота {Cm(body.size.y)}, верх на Y={body.max.y:0.###}");
                if (body.size.y > 2f) W($"кастрюля очень большая ({Cm(body.size.y)}): эффекты кипения масштабируются сами (Cookware Heat v2.3), " +
                                        "но тонкие настройки (Bubble Size, Steam Scale) заданы для кастрюли ~30 см");
            }

            // вода
            Component lv = CC(go, "CookwareLiquidVisual");
            if (lv == null) E("нет Cookware Liquid Visual (LiquidAnchor) — вода не рисуется и не кипит");
            else
            {
                var prods = F(lv, "_products") as IEnumerable;
                bool water = false, broth = false; var list = new List<string>();
                if (prods != null)
                    foreach (object e in prods)
                    {
                        object pd = F(e, "Product"); object role = F(e, "Role");
                        string title = (P(pd, "Title") ?? F(pd, "Title") ?? N(pd))?.ToString() ?? "?";
                        list.Add($"{title}={role}");
                        string tl = title.ToLowerInvariant();
                        if ((tl.Contains("вода") || tl.Contains("water")) && role != null && role.ToString() == "Liquid") water = true;
                        if (tl.Contains("бульон") && role != null && role.ToString() == "Liquid") broth = true;
                    }
                if (!water) E("LiquidAnchor → Products: нет «Вода» с ролью Liquid — вода не считается жидкостью, НЕ КИПИТ. Сейчас: " + string.Join(", ", list));
                else Ok("вода в Products — жидкость");
                if (!broth) W("в Products нет «Бульон» (Liquid) — бульон будет невидимым");
            }

            if (F(heat, "_prettyFx") is bool pf && !pf) W("Cookware Heat → Pretty Fx выключен — красивого кипения не будет");
            Inf($"Cookware Heat: Heat Boost {F(heat, "_heatBoost") ?? "нет поля (старая версия)"}");

            ScanLidSocket(go, hasBody, body);
            ScanThermometer(go, hasBody ? body.size.y : 0.3f);
            ScanDamage(go);

            Component fxs = C(go, "PotEffects");
            if (fxs != null && F(fxs, "_shakeTarget") == null) W("Pot Effects → Shake Target пуст (тряски не будет)");
            Component pr = C(go, "CookwarePressure");
            if (pr == null) E("нет Cookware Pressure — это «тело» кастрюли: без него градусник не знает температуру, " +
                              "нет давления/взрыва, дыр и ремонта. Add Component → Cookware Pressure");
            else Inf($"взрыв с клавиши: Shift+{F(pr, "_debugRuptureKey")}");

            if (Application.isPlaying) Live(go, heat, lv);
        }
    }

    private void ScanLidSocket(GameObject go, bool hasBody, Bounds body)
    {
        Component sock = C(go, "PotLidSocket");
        if (sock == null) { E("нет Pot Lid Socket — крышку не поставить"); return; }
        var s = F(sock, "_socket") as Transform;
        if (s == null) { E("Pot Lid Socket → Socket пуст — крышка встанет в центр корня кастрюли"); s = go.transform; }
        else Inf($"LidSocket: «{Path(s)}», ось вверх у него {(Vector3.Dot(s.up, Vector3.up) > 0.9f ? "вверх ✓" : "НЕ вверх (" + s.up.ToString("F2") + ")")}");
        if (hasBody)
        {
            float dy = s.position.y - body.max.y;
            float h = body.size.y;
            if (dy < -0.15f * h) E($"LidSocket на {Cm(-dy)} НИЖЕ верхней кромки — крышка окажется внутри кастрюли (не видно). Подними на кромку.");
            else if (dy > 0.25f * h) W($"LidSocket на {Cm(dy)} выше кромки — крышка будет висеть в воздухе");
            else Ok($"LidSocket на кромке (разница {Cm(Mathf.Abs(dy))})");
            Vector2 off = new Vector2(s.position.x - body.center.x, s.position.z - body.center.z);
            if (off.magnitude > Mathf.Max(body.extents.x, body.extents.z) * 0.3f)
                W($"LidSocket смещён от центра горловины на {Cm(off.magnitude)}");
        }
        object al = F(sock, "_assignedLid");
        if (al is Component alc && alc != null) Ok($"своя крышка: «{alc.name}»");
        else if (F(sock, "_startingLidPrefab") is GameObject sp && sp != null) Inf($"своей крышки нет — при старте создастся из «{sp.name}» рядом с кастрюлей");
        else W("своя крышка не назначена (Pot Lid Socket → Assigned Lid) — U возьмёт ближайшую свободную");
        Inf($"накрыть при старте: {F(sock, "_attachOnStart") ?? "нет поля (старая версия)"}");
        if (Application.isPlaying)
        {
            object lid = P(sock, "HasLid");
            Inf($"PLAY: крышка на кастрюле: {lid}");
        }
    }

    private void ScanThermometer(GameObject go, float potH)
    {
        Component th = C(go, "PotThermometer3D") ?? CC(go, "PotThermometer3D");
        if (th == null) { Inf("Pot Thermometer 3D нет (градусника не будет)"); return; }
        var model = F(th, "_model") as Transform;
        if (model == null)
        {
            string needle = (F(th, "_findByName") as string ?? "termometer").ToLowerInvariant();
            model = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != go.transform && t.name.ToLowerInvariant().Contains(needle));
            if (model == null) { E($"ГРАДУСНИК: модели нет (поле Model пустое и нет ребёнка с «{needle}» в имени) — нечего показывать"); return; }
            W($"градусник: Model пустое, найдётся по имени «{model.name}»");
        }
        Inf($"градусник: модель «{Path(model)}», localScale {model.localScale.ToString("F3")}, в мире ×{model.lossyScale.x:0.####}{(model.gameObject.activeInHierarchy ? "" : ", ВЫКЛЮЧЕН")}");
        if (WorldBounds(model, out Bounds b))
        {
            float sz = Mathf.Max(b.size.x, b.size.y, b.size.z);
            float rel = sz / Mathf.Max(0.01f, potH);
            if (rel < 0.1f) E($"градусник МИКРОСКОПИЧЕСКИЙ: {Cm(sz)} при кастрюле {Cm(potH)}. Увеличь Scale у модели (или выключи Keep Scene Scale и задай Model Scale)");
            else if (rel > 3f) W($"градусник огромный: {Cm(sz)} при кастрюле {Cm(potH)}");
            else Ok($"градусник: размер {Cm(sz)} (кастрюля {Cm(potH)})");
        }
        else E("у модели градусника нет ни одного меша (Renderer)");
        if (F(th, "_column") == null) W("градусник: Column пуст — столбик не будет заливаться");
        if (F(th, "_slider") == null) W("градусник: Slider пуст — пуговки не будет");
        if (F(th, "_keepSceneScale") is bool ks) Inf($"Keep Scene Scale: {ks}, Model Scale: {F(th, "_modelScale")}");
        if (Application.isPlaying)
        {
            Inf($"PLAY: виден={F(th, "_visible")}, раскрыт={F(th, "_fold")}, почему скрыт: «{F(th, "_whyHidden")}», температура на шкале {F(th, "_shown")}");
            if (F(th, "_fold") is float fold && fold < 0.5f)
                W("градусник свёрнут (маленький), потому что не должен показываться: включи Always Show для проверки");
        }
    }

    private void ScanDamage(GameObject go)
    {
        Component dm = C(go, "DeformableMesh");
        if (dm == null) W("нет Deformable Mesh — вмятин не будет");
        else
        {
            var tg = F(dm, "_targets") as MeshFilter[];
            if (tg == null || tg.Length == 0) Inf("Deformable Mesh: Targets пусто — возьмёт корпус сам");
            else foreach (MeshFilter mf in tg)
                {
                    if (mf == null) { W("Deformable Mesh: пустой элемент в Targets"); continue; }
                    Mesh m = mf.sharedMesh;
                    if (m == null) W($"«{mf.name}»: нет меша");
                    else if (!m.isReadable) E($"«{mf.name}»: у модели выключен Read/Write — вмятин не будет (Import Settings → Read/Write → Apply)");
                    else Ok($"вмятины: «{mf.name}», вершин {m.vertexCount}");
                }
        }
        Component pd = C(go, "PotDamage");
        if (pd == null) { W("нет Pot Damage — дыр не будет"); return; }
        var shell = F(pd, "_shell") as Renderer[];
        if (shell == null || shell.Length == 0) Inf("Pot Damage: Shell пусто — возьмёт корпус сам (в Play)");
        else foreach (Renderer r in shell)
            {
                if (r == null) continue;
                string sh = string.Join(", ", r.sharedMaterials.Where(m => m != null).Select(m => m.shader.name));
                bool ok = r.sharedMaterials.Any(m => m != null && m.HasProperty("_DamageMask"));
                if (ok) Ok($"дыры: «{r.name}» шейдер {sh}");
                else if (F(pd, "_autoDamageMaterial") is bool a && a) Inf($"дыры: «{r.name}» ({sh}) — в Play переведётся на шейдер с дырами");
                else W($"дыры: «{r.name}» шейдер {sh} — дыр НЕ видно (нужен CatCooks/Toon Props Damage)");
            }
    }

    // ── живые значения ──
    private void Live(GameObject go, Component heat, Component lv)
    {
        Component cw = C(go, "Cookware");
        object temp = P(heat, "TemperatureC"), fire = P(heat, "FireC"), liq = P(heat, "LiquidPortions"), boil = P(heat, "ServerBoiling");
        Inf($"PLAY: вода {temp:0} °C · огонь под ней {fire:0} °C · жидкость {liq} порц. · кипит: {boil} · внешняя t {P(cw, "ExternalTemperatureC"):0}");
        if (fire is float f && f <= 0.1f) E("PLAY: огонь под кастрюлей 0 — костёр её НЕ ГРЕЕТ (не в точке Pot Dock / список Accept)");
        if (liq is int li && li == 0) W("PLAY: жидкости 0 — налей воду (или вода не Liquid в Products)");
        if (lv != null)
        {
            object has = P(lv, "HasLiquid"), pour = P(lv, "CanPour");
            if (!(has is bool h && h) || !(pour is bool p && p)) W($"PLAY: визуал воды: HasLiquid={has}, CanPour={pour} — эффекты кипения рисуются только при обоих true");
        }

        Component fx = CC(go, "BoilFx");
        if (fx == null) E("PLAY: эффектов кипения (BoilFx) НЕТ — Cookware Heat их не создал (старая версия файла или Pretty Fx выкл)");
        else
        {
            var parts = fx.GetComponentsInChildren<ParticleSystem>(true).Select(ps => $"{ps.name}:{ps.particleCount}");
            var surf = fx.transform.Find("BoilSurface");
            Ok($"PLAY: BoilFx есть · частиц {string.Join(", ", parts)} · поверхность: {(surf != null && surf.GetComponent<MeshRenderer>().enabled ? "видна" : "скрыта (до 75 °C)")}");
            foreach (ParticleSystemRenderer r in fx.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (r.sharedMaterial == null || r.sharedMaterial.shader == null || !r.sharedMaterial.shader.isSupported)
                    E($"PLAY: у частиц «{r.name}» нет рабочего материала/шейдера");
        }

        Component sock = C(go, "PotLidSocket");
        if (sock != null && P(sock, "HasLid") is bool hl && hl)
        {
            object nob = F(sock, "_lid");
            var lidT = (nob as Component)?.transform;
            var s = (P(sock, "Socket") as Transform) ?? go.transform;
            if (lidT != null)
            {
                float d = Vector3.Distance(lidT.position, s.position);
                if (d > 0.05f) E($"PLAY: крышка «{lidT.name}» в {Cm(d)} от гнезда — не на кастрюле");
                else Ok($"PLAY: крышка «{lidT.name}» на гнезде");
                if (WorldBounds(lidT, out Bounds lb)) Inf($"PLAY: размер крышки {Cm(Mathf.Max(lb.size.x, lb.size.z))}, видимых мешей {lidT.GetComponentsInChildren<Renderer>().Count(r => r.enabled)}");
                else E("PLAY: у крышки НЕТ мешей — её не видно");
            }
        }
    }

    // ── 3. Крышки ──
    private void ScanLids()
    {
        Component[] lids = Find("PotLidItem");
        H($"3. КРЫШКИ В СЦЕНЕ: {lids.Length}");
        foreach (Component l in lids)
        {
            string tag = $"«{l.name}»";
            var issues = new List<string>();
            if (!l.gameObject.activeInHierarchy) issues.Add("выключена");
            if (C(l.gameObject, "NetworkObject") == null) issues.Add("нет NetworkObject");
            if (l.GetComponent<Rigidbody>() == null) issues.Add("нет Rigidbody");
            if (l.GetComponentsInChildren<Collider>(true).Length == 0) issues.Add("нет коллайдера");
            if (!WorldBounds(l, out Bounds b)) issues.Add("НЕТ МЕШЕЙ — невидимая");
            object mask = F(l, "_potMask");
            if (mask is LayerMask lm)
            {
                int cookL = LayerMask.NameToLayer("Cookware");
                if (cookL >= 0 && (lm.value & (1 << cookL)) == 0) issues.Add("Pot Mask без слоя Cookware (ЛКМ не накроет)");
            }
            if (Application.isPlaying)
            {
                object att = P(l, "IsAttached");
                Inf($"PLAY: {tag} прицеплена: {att}, позиция {l.transform.position.ToString("F2")}");
            }
            if (issues.Count == 0) Ok($"{tag}: ок, размер {(WorldBounds(l, out Bounds bb) ? Cm(Mathf.Max(bb.size.x, bb.size.z)) : "?")}");
            else E($"{tag}: " + string.Join("; ", issues));
        }
        foreach (Component k in Find("BonfireTestKeys"))
        {
            var pf = F(k, "_lidPrefab") as GameObject;
            if (pf == null) Inf("Bonfire Test Keys → Lid Prefab пуст (U берёт крышку из сцены)");
            else
            {
                Type lit = T("PotLidItem");
                if (lit != null && pf.GetComponent(lit) == null) E($"Lid Prefab «{pf.name}» без Pot Lid Item");
                else Ok($"Lid Prefab «{pf.name}»");
            }
        }
    }

    // ── 4. Костры ──
    private void ScanFires()
    {
        Component[] pits = Find("BonfirePit");
        H($"4. КОСТРЫ: {pits.Length}");
        foreach (Component p in pits)
        {
            var dock = F(p, "_potDock") as Component;
            if (dock == null) W($"«{p.name}»: Pot Dock пуст (найдётся сам в детях / рядом)");
            else
            {
                var acc = F(dock, "_accept") as Array;
                if (acc != null && acc.Length > 0)
                    W($"«{p.name}»: точка «{dock.name}» принимает только: {string.Join(", ", acc.Cast<object>().Select(N))} — очисти Accept, если новая кастрюля не там");
                else Ok($"«{p.name}»: точка «{dock.name}», принимает любую тару");
            }
            if (Application.isPlaying)
                Inf($"PLAY: «{p.name}» {P(p, "FireState")}, досок {P(p, "Planks")}, сила {P(p, "Intensity01"):0.00}, огонь {P(p, "PotTemperatureC")} °C");
        }
    }
}
#endif
