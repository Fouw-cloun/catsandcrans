using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ДИОРАМА ВОКРУГ ГОРОДА.
///
/// Собирает кольца-цилиндры и стаи. Строит меши один раз, дальше не работает
/// вовсе: всё движение живёт в вершинных шейдерах.
///
/// ═══ КАК ЭТО ЧИТАЕТСЯ ИГРОКОМ ═══
///
///     ближнее кольцо  ~250 м   средний город, крупный силуэт
///     среднее кольцо  ~450 м   дальние кварталы, мельче и бледнее
///     дальнее кольцо  ~800 м   горизонт, почти растворён
///     облачное кольцо ~600 м   выше остальных, медленно ползёт
///     стаи            200-500 м
///
/// Параллакс между ними настоящий: ближнее смещается относительно дальнего
/// ровно потому, что оно ближе. Ничего подделывать не надо.
///
/// ═══ ПОЧЕМУ КОЛЬЦА НЕ СЛЕДУЮТ ЗА КАМЕРОЙ ═══
///
/// Обычно диораму привязывают к камере. Для открытого мира иначе нельзя, но
/// здесь игровая зона — три башни, и игрок смещается по горизонтали от силы
/// на сотню метров. Кольцо на шестистах метрах при этом почти не меняет вид.
///
/// А привязка как раз и выдаёт приём: при быстром повороте слои заметно
/// «едут», и горизонт перестаёт быть местом, превращаясь в обои. Поэтому
/// следование здесь есть, но выключено — включать только если игрок начнёт
/// уходить на сотни метров.
///
/// Меню компонента: правой кнопкой → СОБРАТЬ.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("CatCooks/Окружение/Diorama Rig")]
public class DioramaRig : MonoBehaviour
{
    [System.Serializable]
    public class Ring
    {
        public string name = "Кольцо";
        public Material material;

        [Tooltip("Радиус, метры. Держи не меньше трёх размеров игровой зоны — " +
                 "иначе при ходьбе по крышам станет виден параллакс самого " +
                 "цилиндра, и он прочитается как цилиндр.")]
        [Min(20f)] public float radius = 400f;

        [Tooltip("Низ кольца по высоте мира")]
        public float bottom = -40f;

        [Tooltip("Верх кольца по высоте мира")]
        public float top = 160f;

        [Tooltip("Граней по кругу. Больше 64 смысла нет: на таком радиусе " +
                 "грань всё равно шире экрана.")]
        [Range(12, 128)] public int segments = 48;

        [Tooltip("Порядок отрисовки. У ДАЛЬНЕГО кольца число должно быть " +
                 "МЕНЬШЕ — оно рисуется первым, ближние ложатся поверх.\n\n" +
                 "Перепутаешь — дальний горизонт окажется перед средним " +
                 "городом, и это будет выглядеть необъяснимо.")]
        [Range(-100, -1)] public int queueOffset = -50;
    }

    [System.Serializable]
    public class Flock
    {
        public string name = "Птицы";
        public Material material;

        [Range(1, 400)] public int count = 60;

        [Tooltip("Разброс радиусов орбит, метры")]
        public Vector2 radius = new Vector2(180f, 420f);

        [Tooltip("Разброс высот, метры")]
        public Vector2 height = new Vector2(40f, 140f);

        public int seed = 1;
    }

    [Header("━━━ Кольца ━━━")]
    [SerializeField] private Ring[] _rings = new Ring[0];

    [Header("━━━ Стаи ━━━")]
    [SerializeField] private Flock[] _flocks = new Flock[0];

    [Header("━━━ Следование ━━━")]
    [Tooltip("СЛЕДОВАТЬ ЗА КАМЕРОЙ ПО ГОРИЗОНТАЛИ.\n\n" +
             "Для трёх башен НЕ НУЖНО и даже вредно: игрок почти не смещается, " +
             "а слои начнут заметно ехать при повороте и выдадут приём.\n\n" +
             "Включай, только если игровая зона разрастётся на сотни метров.")]
    [SerializeField] private bool _followCamera = false;

    [Tooltip("ПО ВЫСОТЕ НЕ СЛЕДУЕМ НИКОГДА, и это принципиально.\n\n" +
             "Подъём на двадцатый этаж ДОЛЖЕН менять вид на горизонт — иначе " +
             "пропадёт ощущение высоты, ради которого всё и строится. " +
             "Привязанная по вертикали диорама сделает вид с крыши " +
             "неотличимым от вида с земли.")]
    [SerializeField] private bool _lockVertical = true;

    private Transform _built;

    private void LateUpdate()
    {
        if (!_followCamera || _built == null) return;

        Camera cam = Camera.main;

#if UNITY_EDITOR
        if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
            cam = SceneView.lastActiveSceneView.camera;
#endif
        if (cam == null) return;

        Vector3 p = cam.transform.position;

        _built.position = _lockVertical
            ? new Vector3(p.x, transform.position.y, p.z)
            : p;
    }

    // ═════════════════════════ СБОРКА ═════════════════════════

    [ContextMenu("★ СОБРАТЬ")]
    public void Build()
    {
        Clear();

        GameObject root = new GameObject("Diorama (generated)");
        root.transform.SetParent(transform, false);
        _built = root.transform;

        // ЗДЕСЬ РАНЬШЕ СТОЯЛ HideFlags.DontSave, И ЭТО БЫЛА ОШИБКА.
        //
        // Замысел был разумный: не сохранять в сцену то, что собирается
        // кнопкой. Но при входе в Play сцена перезагружается, и всё
        // помеченное DontSave уничтожается. Снаружи это выглядит совершенно
        // необъяснимо: в редакторе диорама есть, нажал Play — объект пуст.
        //
        // Обычный дочерний объект в сцене надёжнее. Он занимает несколько
        // килобайт, зато переживает Play, сборку и открытие сцены заново.

        int made = 0;

        foreach (Ring r in _rings)
        {
            if (r == null || r.material == null) continue;

            GameObject go = new GameObject(r.name);
            go.transform.SetParent(root.transform, false);

            Mesh m = BuildRing(r);
            go.AddComponent<MeshFilter>().sharedMesh = m;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = r.material;

            // ПОРЯДОК СЛОЁВ ЖИВЁТ В САМОМ МАТЕРИАЛЕ.
            //
            // Здесь раньше создавалась КОПИЯ материала, чтобы не трогать
            // ассет. Копия помечалась DontSave — и это ломало всё ровно так
            // же, как раньше ломался сам объект: копия не сохраняется, при
            // входе в Play сцена перезагружается, ссылка указывает в пустоту,
            // и Unity рисует розовую заглушку.
            //
            // Правка ассета надёжнее. Номер очереди — это настройка слоя, а
            // не временное состояние: он обязан пережить перезапуск, и место
            // ему именно в материале.
            //
            // Разные кольца всё равно берут разные текстуры, то есть разные
            // материалы. Общий материал на два кольца — ошибка сборки, и о
            // ней предупреждает диагностика.
            int want = 3000 + Mathf.Clamp(r.queueOffset, -400, 0);

            if (r.material.renderQueue != want)
            {
                r.material.renderQueue = want;
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(r.material);
#endif
            }

            // Диорама не участвует в свете вообще: она уже нарисована, а
            // тени на горизонте в трёхстах метрах не видит никто.
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            made++;
        }

        foreach (Flock f in _flocks)
        {
            if (f == null || f.material == null || f.count < 1) continue;

            GameObject go = new GameObject(f.name);
            go.transform.SetParent(root.transform, false);

            Mesh m = BuildFlock(f);
            go.AddComponent<MeshFilter>().sharedMesh = m;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = f.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            made++;
        }

        Debug.Log($"[Диорама] «{name}»: собрано {made} слоёв. " +
                  "Это столько же отрисовок — движение живёт в шейдерах, " +
                  "процессор после сборки не участвует.", this);
    }

    [ContextMenu("★ УБРАТЬ")]
    public void Clear()
    {
        List<Transform> kill = new List<Transform>();

        foreach (Transform c in transform)
            if (c != null && c.name.StartsWith("Diorama (")) kill.Add(c);

        foreach (Transform c in kill)
        {
            if (Application.isPlaying) Destroy(c.gameObject);
            else DestroyImmediate(c.gameObject);
        }

        _built = null;
    }

    /// <summary>
    /// КОЛЬЦО. Полоса из четырёхугольников по кругу, нормалями наружу —
    /// шейдер отсекает передние грани, поэтому изнутри видна изнанка.
    ///
    /// Развёртка идёт от нуля до единицы по кругу и по высоте. Повторы задаёт
    /// шейдер, а не меш: так одно и то же кольцо годится и под редкий силуэт
    /// города, и под частые облака.
    /// </summary>
    private Mesh BuildRing(Ring r)
    {
        int seg = Mathf.Clamp(r.segments, 12, 128);
        float top = Mathf.Max(r.bottom + 1f, r.top);

        Vector3[] v = new Vector3[(seg + 1) * 2];
        Vector2[] uv = new Vector2[v.Length];
        int[] tri = new int[seg * 6];

        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg;
            float a = t * Mathf.PI * 2f;

            float x = Mathf.Cos(a) * r.radius;
            float z = Mathf.Sin(a) * r.radius;

            v[i * 2] = new Vector3(x, r.bottom, z);
            v[i * 2 + 1] = new Vector3(x, top, z);

            uv[i * 2] = new Vector2(t, 0f);
            uv[i * 2 + 1] = new Vector2(t, 1f);
        }

        for (int i = 0; i < seg; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d = i * 2 + 3;
            int o = i * 6;

            tri[o] = a; tri[o + 1] = b; tri[o + 2] = c;
            tri[o + 3] = c; tri[o + 4] = b; tri[o + 5] = d;
        }

        Mesh m = new Mesh { name = $"Ring_{r.name}" };
        m.vertices = v;
        m.uv = uv;
        m.triangles = tri;

        // Границы задаём руками: меш огромный и неподвижный, считать их по
        // вершинам незачем, а отсекаться кольцо не должно никогда.
        m.bounds = new Bounds(new Vector3(0, (r.bottom + top) * 0.5f, 0),
                              new Vector3(r.radius * 2.2f, (top - r.bottom) * 1.2f,
                                          r.radius * 2.2f));
        return m;
    }

    /// <summary>
    /// СТАЯ. Плоские четырёхугольники, у каждого свой номер в цвете вершин.
    ///
    /// Позиция вершины — это НЕ место птицы, а её орбита: xz задают центр и
    /// радиус круга, y — среднюю высоту. Куда птица попадёт в данный момент,
    /// решает вершинный шейдер по времени.
    ///
    /// Поэтому меш строится один раз и больше никогда не трогается, сколько бы
    /// птица ни летала.
    /// </summary>
    private Mesh BuildFlock(Flock f)
    {
        int n = Mathf.Clamp(f.count, 1, 400);

        Vector3[] v = new Vector3[n * 4];
        Vector2[] uv = new Vector2[n * 4];
        Color[] col = new Color[n * 4];
        int[] tri = new int[n * 6];

        Random.State state = Random.state;
        Random.InitState(f.seed);

        float rMin = Mathf.Min(f.radius.x, f.radius.y);
        float rMax = Mathf.Max(f.radius.x, f.radius.y);
        float hMin = Mathf.Min(f.height.x, f.height.y);
        float hMax = Mathf.Max(f.height.x, f.height.y);

        for (int i = 0; i < n; i++)
        {
            float rad = Random.Range(rMin, rMax);
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float h = Random.Range(hMin, hMax);

            Vector3 orbit = new Vector3(Mathf.Cos(ang) * rad, h, Mathf.Sin(ang) * rad);

            // Один и тот же номер у всех четырёх вершин — иначе углы квадрата
            // разлетятся в разные стороны, каждый по своей орбите.
            Color seed = new Color(Random.value, Random.value, Random.value, 1f);

            int b = i * 4;

            for (int k = 0; k < 4; k++)
            {
                v[b + k] = orbit;
                col[b + k] = seed;
            }

            uv[b] = new Vector2(0, 0);
            uv[b + 1] = new Vector2(1, 0);
            uv[b + 2] = new Vector2(1, 1);
            uv[b + 3] = new Vector2(0, 1);

            int o = i * 6;
            tri[o] = b; tri[o + 1] = b + 2; tri[o + 2] = b + 1;
            tri[o + 3] = b; tri[o + 4] = b + 3; tri[o + 5] = b + 2;
        }

        Random.state = state;

        Mesh m = new Mesh { name = $"Flock_{f.name}" };
        m.vertices = v;
        m.uv = uv;
        m.colors = col;
        m.triangles = tri;

        // Птицы улетают далеко от своих орбит — коробка с запасом, иначе стая
        // начнёт пропадать целиком, когда её центр уйдёт за край кадра.
        m.bounds = new Bounds(new Vector3(0, (hMin + hMax) * 0.5f, 0),
                              new Vector3(rMax * 2.5f, (hMax - hMin) + 120f, rMax * 2.5f));
        return m;
    }

    // ═════════════════════════ ДИАГНОСТИКА ═════════════════════════

    [ContextMenu("★ ПОЧЕМУ НЕ РАБОТАЕТ")]
    public void Diagnose()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int problems = 0;

        sb.AppendLine($"═══ ДИОРАМА: {name} ═══\n");
        sb.AppendLine($"Колец: {_rings.Length}   Стай: {_flocks.Length}");

        int prevQueue = int.MinValue;
        float prevRadius = 0f;

        System.Array.Sort(_rings, (a, b) =>
            b == null ? -1 : a == null ? 1 : b.radius.CompareTo(a.radius));

        sb.AppendLine("\n── КОЛЬЦА, ОТ ДАЛЬНЕГО ──");

        foreach (Ring r in _rings)
        {
            if (r == null) continue;

            sb.AppendLine($"  {r.radius,6:0} м   очередь {r.queueOffset,4}   " +
                          $"{r.name}   {(r.material == null ? "❌ НЕТ МАТЕРИАЛА" : "")}");

            if (r.material == null) problems++;

            if (prevQueue != int.MinValue && r.queueOffset < prevQueue)
            {
                sb.AppendLine($"     ❌ Очередь меньше, чем у более дальнего " +
                              "кольца. Оно нарисуется ПЕРЕД ним, и дальний " +
                              "горизонт окажется впереди среднего города.");
                problems++;
            }

            prevQueue = r.queueOffset;
            prevRadius = r.radius;
        }

        if (_rings.Length > 0 && prevRadius < 150f)
        {
            sb.AppendLine($"\n⚠ Ближнее кольцо всего в {prevRadius:0} м. При " +
                          "ходьбе по крышам станет виден его собственный " +
                          "параллакс, и цилиндр прочитается как цилиндр. " +
                          "Держи не меньше трёх размеров игровой зоны.");
        }

        if (_followCamera && _lockVertical == false)
        {
            sb.AppendLine("\n❌ Следование по вертикали включено. Подъём на " +
                          "верхний этаж перестанет менять вид на горизонт — " +
                          "пропадёт ровно то ощущение высоты, ради которого " +
                          "строится игра про небоскрёбы.");
            problems++;
        }

        // ── ОДИН МАТЕРИАЛ НА ДВА КОЛЬЦА ──
        //
        // Номер очереди хранится в материале. Если два кольца делят один
        // материал, второе перепишет очередь первого, и порядок слоёв станет
        // случайным — а выглядеть это будет как «дальний горизонт иногда
        // впереди ближнего», без всякой закономерности.
        for (int i = 0; i < _rings.Length; i++)
        {
            if (_rings[i] == null || _rings[i].material == null) continue;

            for (int j = i + 1; j < _rings.Length; j++)
            {
                if (_rings[j] == null) continue;
                if (_rings[j].material != _rings[i].material) continue;

                sb.AppendLine($"\n❌ «{_rings[i].name}» и «{_rings[j].name}» " +
                              "делят ОДИН материал. Номер очереди живёт в " +
                              "материале, значит второе кольцо перепишет " +
                              "порядок первого. Сделай копию материала.");
                problems++;
            }
        }

        // ── ДАЛЬНЯЯ ПЛОСКОСТЬ КАМЕРЫ ──
        //
        // Самая обидная причина «ничего не видно»: кольцо построено верно,
        // материалы на месте, а камера просто не достаёт. По умолчанию у
        // камеры Far Clip Plane равен 1000, и кольцо на восьмистах метрах
        // проходит впритык — а стоит игроку отойти от центра, и оно
        // начинает обрезаться краем.
        Camera cam = Camera.main;

        if (cam != null && _rings.Length > 0)
        {
            float need = 0f;
            foreach (Ring r in _rings) if (r != null) need = Mathf.Max(need, r.radius);

            need *= 2.2f;   // запас: игрок стоит не в центре, а кольцо круглое

            sb.AppendLine($"\n── КАМЕРА ──");
            sb.AppendLine($"  Far Clip Plane: {cam.farClipPlane:0}   нужно не меньше {need:0}");

            if (cam.farClipPlane < need)
            {
                sb.AppendLine($"  ❌ КАМЕРА НЕ ДОСТАЁТ ДО ДИОРАМЫ. Кольца есть в " +
                              "сцене, но обрезаются дальней плоскостью — " +
                              "выглядит как «объекты появились, а на экране " +
                              "пусто».");
                sb.AppendLine($"  ЛЕЧЕНИЕ: Far Clip Plane на камере в {need:0} " +
                              "или больше. Это почти ничего не стоит: дальность " +
                              "влияет на точность буфера глубины, а не на " +
                              "количество работы.");
                problems++;
            }
        }

        sb.AppendLine($"\n── ОЧЕРЕДЬ ОТРИСОВКИ ──");
        sb.AppendLine("  Кольца идут в ПРОЗРАЧНОЙ очереди, 3000 + смещение.");
        sb.AppendLine("  Раньше они стояли до непрозрачной геометрии — и их");
        sb.AppendLine("  закрашивал скайбокс, потому что глубину они не пишут.");

        sb.AppendLine($"\n═══ ПРОБЛЕМ: {problems} ═══");

        if (problems == 0)
            sb.AppendLine("Чисто. Если между кольцами видны жёсткие " +
                          "горизонтальные линии — это края цилиндров, " +
                          "увеличивай Fade Top и Fade Bottom в материалах.");

        if (problems > 0) Debug.LogWarning(sb.ToString(), this);
        else Debug.Log(sb.ToString(), this);
    }

    private void OnDrawGizmosSelected()
    {
        foreach (Ring r in _rings)
        {
            if (r == null) continue;

            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);

            Vector3 c = transform.position;
            Vector3 prev = Vector3.zero;

            for (int i = 0; i <= 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Cos(a) * r.radius, r.bottom,
                                            Mathf.Sin(a) * r.radius);

                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
