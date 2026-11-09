using System.Collections.Generic;
using UnityEngine;

// ============================================================================
//  CatCooksLightningBolt — разряд, который реально бьёт в землю.
//
//  ЧТО ЭТО ДОБАВЛЯЕТ К CatCooksLightning
//   Тот работает со СВЕТОМ: вспышка, экспозиция, гром. Этого достаточно для
//   разрядов внутри облаков — их и в жизни видно как «небо мигнуло».
//   Но удар в землю читается только если виден сам канал: ломаная от
//   основания облака до точки на рельефе.
//
//  ПОЧЕМУ ГЕОМЕТРИЯ, А НЕ ПАРТИКЛЫ
//   Канал молнии — это ОДНА непрерывная ломаная с ветвями. Партиклами
//   такое собирают из отрезков, и на стыках всегда видны разрывы или
//   нахлёсты. Здесь один меш на весь разряд: ветви лежат в тех же
//   треугольниках, порядок отрисовки задан и не переставляется.
//
//  ФОРМА. Срединное смещение (midpoint displacement): берём отрезок
//  облако→земля и рекурсивно ломаем каждый кусок пополам, сдвигая точку
//  вбок на величину, которая падает с каждым уровнем. Это даёт излом
//  на всех масштабах сразу — то, чем молния отличается от зигзага.
//
//  ВЕТВИ. Настоящий разряд ветвится ВНИЗ и ВНАРУЖУ, и почти ни одна
//  ветвь не доходит до земли: до неё добирается только главный канал.
//  Ветви, дотянутые до земли, — первое, что выдаёт подделку.
//
//  ЛЕНТА К КАМЕРЕ. Канал разворачивается в вершинном шейдере вокруг
//  своего направления, поэтому выглядит одинаково с любой стороны и
//  не требует ни одного пересчёта на процессоре после сборки.
//
//  Сеть: косметика. Но точка удара и форма полностью определяются seed,
//  поэтому для синхронной грозы достаточно разослать ОДНО целое число —
//  см. Fire(seed). Позиции игроков при этом не важны: точка считается
//  от переданного центра.
// ============================================================================
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Lightning Bolt")]
public class CatCooksLightningBolt : MonoBehaviour
{
    [Header("Материал")]
    [Tooltip("Материал с шейдером CatCooks/Lightning Bolt.")]
    public Material material;

    [Header("Где бьёт")]
    [Tooltip("Вокруг чего выбирается точка удара. Пусто — Camera.main.")]
    public Transform around;

    [Tooltip("Ближе этого не бьёт. Слишком близкий удар выглядит как " +
             "столб света в двух метрах от лица и ломает масштаб.")]
    public float minDistance = 60f;

    [Tooltip("Дальше этого не бьёт: канал становится тоньше пикселя.")]
    public float maxDistance = 400f;

    [Tooltip("Откуда начинается канал, м над точкой удара. Должно совпадать " +
             "с высотой облачного слоя, иначе разряд начинается в пустоте.")]
    public float cloudHeight = 260f;

    [Tooltip("По чему искать землю. Оставь Everything, если не уверен.")]
    public LayerMask groundMask = ~0;

    [Tooltip("Если луч не нашёл ничего — бьём на эту высоту.")]
    public float fallbackGroundY = 0f;

    [Header("Форма")]
    [Tooltip("Уровней дробления. 5 — это 32 отрезка, достаточно для " +
             "любого расстояния. Больше только жрёт вершины.")]
    [Range(3, 7)] public int subdivisions = 5;

    [Tooltip("Разброс канала вбок на первом уровне, доля от длины. " +
             "0.12–0.2 — узнаваемая молния, выше — клубок.")]
    [Range(0.02f, 0.4f)] public float jitter = 0.15f;

    [Tooltip("Во сколько раз падает разброс на каждом следующем уровне. " +
             "0.5 — классический фрактал, ниже — глаже.")]
    [Range(0.3f, 0.8f)] public float jitterFalloff = 0.52f;

    [Tooltip("Толщина главного канала, м. Это ЭКРАННАЯ величина только " +
             "по ощущению: на 400 м канал в 1.2 м уже тонкая нить.")]
    public float width = 1.2f;

    [Header("Ветви")]
    [Range(0, 12)] public int branches = 5;

    [Tooltip("Длина ветви как доля остатка пути до земли. Держи меньше 1: " +
             "ветвь, дотянувшаяся до земли, сразу выдаёт подделку.")]
    [Range(0.1f, 0.9f)] public float branchLength = 0.45f;

    [Tooltip("Толщина ветви относительно главного канала.")]
    [Range(0.1f, 1f)] public float branchWidth = 0.45f;

    [Tooltip("Насколько ветви расходятся вбок.")]
    [Range(0f, 2f)] public float branchSpread = 0.8f;

    [Header("Время")]
    [Tooltip("Сколько живёт разряд, секунд. Реальный виден около 0.2 с.")]
    public float duration = 0.28f;

    [Tooltip("Мерцаний за время жизни. Канал не гаснет ровно — он " +
             "пульсирует, пока идут обратные удары.")]
    public Vector2Int flickers = new Vector2Int(3, 6);

    [Header("Свет в точке удара")]
    public bool impactLight = true;
    public Color impactColor = new Color(0.78f, 0.86f, 1f);
    public float impactIntensity = 900f;
    public float impactRange = 120f;

    [Tooltip("Что заспавнить в точке удара: искры, дым, выжженное пятно. " +
             "Уничтожается само через impactLifetime.")]
    public GameObject impactPrefab;
    public float impactLifetime = 4f;

    // ───────── внутреннее ─────────
    private GameObject _go;
    private MeshFilter _mf;
    private MeshRenderer _mr;
    private Mesh _mesh;
    private Light _light;
    private MaterialPropertyBlock _mpb;

    private float _age = -1f;
    private int _flickerCount = 4;
    private float _power = 1f;

    static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    /// <summary>Расстояние от камеры до последней точки удара, м.
    /// Из него CatCooksLightning считает задержку грома.</summary>
    public float LastDistance { get; private set; }

    /// <summary>Точка последнего удара в мире.</summary>
    public Vector3 LastPoint { get; private set; }

    void OnDisable() { Cleanup(); }
    void OnDestroy() { Cleanup(); }

    void Cleanup()
    {
        if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
        _mesh = null;
        if (_go != null) { if (Application.isPlaying) Destroy(_go); else DestroyImmediate(_go); }
        _go = null;
        _age = -1f;
    }

    // ============================================================== запуск
    /// <summary>
    /// Ударить. seed определяет и точку, и форму целиком: одинаковый seed
    /// у всех клиентов даёт одинаковую молнию без синхронизации позиций.
    /// power 0…1 — сила грозы, влияет на толщину, яркость и число ветвей.
    /// Возвращает false, если бить некуда (нет камеры или материала).
    /// </summary>
    public bool Fire(int seed, float power = 1f)
    {
        Transform anchor = around != null ? around
                         : (Camera.main != null ? Camera.main.transform : null);
        if (anchor == null || material == null) return false;

        var rng = new System.Random(seed);
        _power = Mathf.Clamp01(power);

        // Точка удара: кольцо вокруг игрока. Равномерно по ПЛОЩАДИ, а не по
        // радиусу — иначе удары кучкуются у ближней границы кольца.
        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
        float t = (float)rng.NextDouble();
        float dist = Mathf.Sqrt(Mathf.Lerp(minDistance * minDistance,
                                           maxDistance * maxDistance, t));

        Vector3 flat = anchor.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;

        // Ищем землю сверху вниз: луч сам найдёт крышу, дерево или скалу,
        // если они выше рельефа. Молния бьёт в самое высокое — это и надо.
        Vector3 from = new Vector3(flat.x, flat.y + cloudHeight, flat.z);
        Vector3 ground = new Vector3(flat.x, fallbackGroundY, flat.z);
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit,
                            cloudHeight * 3f, groundMask, QueryTriggerInteraction.Ignore))
            ground = hit.point;

        return FireAt(ground, seed, power);
    }

    /// <summary>Ударить в конкретную точку. Форма всё так же от seed.</summary>
    public bool FireAt(Vector3 groundPoint, int seed, float power = 1f)
    {
        if (material == null) return false;

        var rng = new System.Random(seed);
        _power = Mathf.Clamp01(power);
        LastPoint = groundPoint;

        Camera cam = Camera.main;
        LastDistance = cam != null
            ? Vector3.Distance(cam.transform.position, groundPoint)
            : Mathf.Lerp(minDistance, maxDistance, 0.5f);

        EnsureObject();
        _go.transform.position = groundPoint;

        // Верх канала слегка уводим вбок: строго вертикальный столб
        // читается как луч прожектора, а не как разряд.
        Vector3 top = new Vector3(
            ((float)rng.NextDouble() - 0.5f) * cloudHeight * 0.35f,
            cloudHeight,
            ((float)rng.NextDouble() - 0.5f) * cloudHeight * 0.35f);

        BuildMesh(top, Vector3.zero, rng);

        if (impactLight) SetupLight(groundPoint);
        if (impactPrefab != null)
        {
            var fx = Instantiate(impactPrefab, groundPoint, Quaternion.identity);
            if (impactLifetime > 0f) Destroy(fx, impactLifetime);
        }

        _flickerCount = rng.Next(flickers.x, Mathf.Max(flickers.x, flickers.y) + 1);
        _age = 0f;
        _go.SetActive(true);
        return true;
    }

    void EnsureObject()
    {
        if (_go != null) return;

        _go = new GameObject("LightningBolt");
        _go.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
        _go.transform.SetParent(null, true);   // мировые координаты, без наследования

        _mf = _go.AddComponent<MeshFilter>();
        _mr = _go.AddComponent<MeshRenderer>();
        _mr.sharedMaterial = material;
        _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _mr.receiveShadows = false;
        _mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        _mpb = new MaterialPropertyBlock();
        _go.SetActive(false);
    }

    void SetupLight(Vector3 at)
    {
        if (_light == null)
        {
            var lgo = new GameObject("LightningImpactLight");
            lgo.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;   // тени от молнии не окупаются
        }
        // Свет чуть ВЫШЕ точки удара: ровно на земле он светит в полусферу
        // и подсвечивает только пятно под собой.
        _light.transform.position = at + Vector3.up * 8f;
        _light.color = impactColor;
        _light.range = impactRange;
        _light.enabled = true;
    }

    // ================================================================ форма
    void BuildMesh(Vector3 top, Vector3 bottom, System.Random rng)
    {
        var verts = new List<Vector3>(256);
        var uvs = new List<Vector2>(256);
        var dirs = new List<Vector3>(256);
        var widths = new List<Vector2>(256);
        var tris = new List<int>(512);

        float len = Vector3.Distance(top, bottom);
        float w = width * Mathf.Lerp(0.7f, 1.25f, _power);

        // Главный канал.
        var main = MakePath(top, bottom, len * jitter, subdivisions, rng);
        AddRibbon(verts, uvs, dirs, widths, tris, main, w, 1f, true);

        // Ветви. Точка отрыва берётся из верхних двух третей: у самой
        // земли канал уже не ветвится, там всё сходится в один удар.
        int count = Mathf.RoundToInt(branches * Mathf.Lerp(0.4f, 1f, _power));
        for (int b = 0; b < count; b++)
        {
            int i = rng.Next(1, Mathf.Max(2, (int)(main.Count * 0.66f)));
            Vector3 start = main[i];

            // Направление: вниз-вбок от хода канала.
            Vector3 down = (main[Mathf.Min(i + 1, main.Count - 1)] - start).normalized;
            Vector3 side = new Vector3((float)rng.NextDouble() - 0.5f, 0f,
                                       (float)rng.NextDouble() - 0.5f).normalized;
            float remain = start.y - bottom.y;
            float bl = remain * branchLength * Mathf.Lerp(0.5f, 1f, (float)rng.NextDouble());

            Vector3 end = start + (down + side * branchSpread).normalized * bl;
            // Обрубаем, если ветвь всё же дотянулась до земли.
            if (end.y < bottom.y + remain * 0.15f) end.y = bottom.y + remain * 0.15f;

            var path = MakePath(start, end, bl * jitter * 1.4f, subdivisions - 2, rng);
            AddRibbon(verts, uvs, dirs, widths, tris, path,
                      w * branchWidth, 0.55f, false);
        }

        if (_mesh == null)
            _mesh = new Mesh { name = "CatCooksLightning", hideFlags = HideFlags.HideAndDontSave };
        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetUVs(0, uvs);
        _mesh.SetUVs(1, dirs);
        _mesh.SetUVs(2, widths);
        _mesh.SetTriangles(tris, 0);

        // Границы вручную и с запасом: ленту разворачивает вершинный
        // шейдер, посчитанные по осевой линии границы будут малы.
        _mesh.bounds = new Bounds(new Vector3(0f, len * 0.5f, 0f),
                                  new Vector3(len, len * 1.2f, len));
        _mf.sharedMesh = _mesh;
    }

    /// <summary>
    /// Срединное смещение. Ломаем отрезок пополам, сдвигаем середину вбок,
    /// повторяем для половин с меньшим разбросом. Излом получается на всех
    /// масштабах сразу — в этом всё отличие от обычного зигзага.
    /// </summary>
    List<Vector3> MakePath(Vector3 a, Vector3 b, float amp, int levels, System.Random rng)
    {
        var pts = new List<Vector3> { a, b };
        levels = Mathf.Clamp(levels, 1, 7);

        for (int lv = 0; lv < levels; lv++)
        {
            var next = new List<Vector3>(pts.Count * 2);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 p0 = pts[i], p1 = pts[i + 1];
                Vector3 mid = (p0 + p1) * 0.5f;

                // Смещаем ПОПЕРЁК отрезка, а не по случайным осям: иначе
                // канал то растягивается, то сжимается по длине и теряет
                // ощущение единого разряда.
                Vector3 axis = (p1 - p0).normalized;
                Vector3 any = Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up;
                Vector3 n1 = Vector3.Cross(axis, any).normalized;
                Vector3 n2 = Vector3.Cross(axis, n1).normalized;

                mid += (n1 * ((float)rng.NextDouble() - 0.5f)
                      + n2 * ((float)rng.NextDouble() - 0.5f)) * amp * 2f;

                next.Add(p0);
                next.Add(mid);
            }
            next.Add(pts[pts.Count - 1]);
            pts = next;
            amp *= jitterFalloff;
        }
        return pts;
    }

    /// <summary>
    /// Лента вдоль ломаной. Ширину и разворот к камере делает шейдер:
    /// в вершину пишется направление отрезка, а сторона — в uv.x.
    /// </summary>
    static void AddRibbon(List<Vector3> verts, List<Vector2> uvs, List<Vector3> dirs,
                          List<Vector2> widths, List<int> tris,
                          List<Vector3> path, float halfWidth, float brightness,
                          bool fadeTop)
    {
        if (path.Count < 2) return;
        int start = verts.Count;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 dir = i == 0 ? (path[1] - path[0])
                        : i == path.Count - 1 ? (path[i] - path[i - 1])
                        : (path[i + 1] - path[i - 1]);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;

            float along = i / (float)(path.Count - 1);

            // Канал ТОЛЩЕ У ЗЕМЛИ: путь строится от облака (along 0)
            // к земле (along 1), а самый яркий и широкий — обратный удар
            // у поверхности. В облаке канал должен истончаться и тонуть.
            float hw = halfWidth * Mathf.Lerp(0.55f, 1f, along);

            // У ветвей uv.y = 1 постоянно: иначе шейдер погасит их корень
            // тем же затуханием, что и верх главного канала, и ветвь
            // будет отрываться от него на пустом месте.
            float v = fadeTop ? along : 1f;

            verts.Add(path[i]); uvs.Add(new Vector2(0f, v));
            dirs.Add(dir); widths.Add(new Vector2(hw, brightness));

            verts.Add(path[i]); uvs.Add(new Vector2(1f, v));
            dirs.Add(dir); widths.Add(new Vector2(hw, brightness));
        }

        for (int i = 0; i < path.Count - 1; i++)
        {
            int a = start + i * 2;
            tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
            tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
        }
    }

    // ================================================================= кадр
    void Update()
    {
        if (_age < 0f) return;

        _age += Time.deltaTime;
        float t = _age / Mathf.Max(duration, 0.02f);

        if (t >= 1f)
        {
            _age = -1f;
            if (_go != null) _go.SetActive(false);
            if (_light != null) _light.enabled = false;
            return;
        }

        // МЕРЦАНИЕ. Пила по числу обратных ударов на общем спаде — та же
        // форма, что у вспышки в CatCooksLightning, чтобы канал и засветка
        // кадра гасли согласованно, а не жили каждый своей жизнью.
        float saw = 1f - Mathf.Repeat(t * _flickerCount, 1f);
        float envelope = 1f - t;
        float f = Mathf.Pow(saw, 1.6f) * Mathf.Pow(envelope, 0.6f);
        // Первый импульс всегда самый сильный.
        if (t < 1f / _flickerCount)
            f = Mathf.Max(f, Mathf.Pow(1f - t * _flickerCount, 1.2f));

        float bright = Mathf.Lerp(0.55f, 1f, _power);

        if (_mr != null)
        {
            _mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(IntensityId, f * bright);
            _mr.SetPropertyBlock(_mpb);
        }
        if (_light != null && _light.enabled)
            _light.intensity = impactIntensity * f * bright;
    }

#if UNITY_EDITOR
    [ContextMenu("Ударить рядом")]
    void TestStrike()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[LightningBolt] Разряд живёт доли секунды и " +
                             "анимируется в Update — проверяй в Play Mode.", this);
            return;
        }
        Fire(Random.Range(0, 99999), 1f);
    }
#endif
}
