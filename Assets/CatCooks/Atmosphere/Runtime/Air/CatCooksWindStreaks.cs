using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// ============================================================================
//  CatCooksWindStreaks — видимые порывы ветра.
//
//  Раз в несколько секунд мимо игрока пролетает росчерк: тонкая линия,
//  которая идёт по волне и сворачивается в завиток. Вычерчивается от
//  головы к хвосту и так же стирается.
//
//  ПОЧЕМУ ИМЕННО ТАК
//
//   • ГЕОМЕТРИЯ, А НЕ ЧАСТИЦЫ. Завиток — это кривая с петлёй. Партиклами
//     петлю не сделать: они летят по траектории, но каждый остаётся
//     точкой. Здесь строится лента вдоль всей кривой сразу, и петля
//     видна как петля.
//
//   • ПРОРИСОВКА. Штрих не выезжает готовым — он ВЫЧЕРЧИВАЕТСЯ. Это
//     единственное, что отличает рисованную анимацию от «пролетела
//     текстурка». Шейдер получает положение пера в _Head.
//
//   • ЛЕНТА СМОТРИТ В КАМЕРУ. Каждый сегмент разворачивается к зрителю
//     при построении, иначе с ребра росчерк исчезает.
//
//   • Порывы согласованы с ветром: направление и частота берутся из
//     глобалей CatCooksWindController. В штиль их почти нет, в шторм
//     они идут один за другим.
//
//  Сеть: чистая косметика, НЕ NetworkObject.
// ============================================================================
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Atmosphere/Wind Streaks")]
public class CatCooksWindStreaks : MonoBehaviour
{
    [Header("Материал")]
    [Tooltip("Материал с шейдером CatCooks/Wind Streak")]
    public Material streakMaterial;

    [Header("Количество")]
    [Tooltip("Сколько росчерков может лететь одновременно.")]
    [Range(1, 24)] public int maxStreaks = 10;

    [Tooltip("Пауза между появлениями в штиль и в шторм, секунд.")]
    public Vector2 intervalCalmStorm = new Vector2(3.2f, 0.35f);

    [Header("Зона")]
    [Tooltip("Камера, вокруг которой всё происходит. Пусто — Camera.main.")]
    public Camera targetCamera;

    [Tooltip("Радиус появления вокруг игрока, м.")]
    public float radius = 34f;

    [Tooltip("Высота появления относительно камеры, м.")]
    public Vector2 heightRange = new Vector2(-1.5f, 5f);

    [Header("Форма росчерка")]
    [Tooltip("Длина, м. Короткие штрихи выглядят как мусор в кадре, " +
             "длинные — как порыв. Ниже 8 м смысла нет.")]
    public Vector2 lengthRange = new Vector2(12f, 30f);

    [Tooltip("Толщина линии, м. Тонкие росчерки (меньше 0.1) на " +
             "фоне неба почти не читаются — линия шириной в пиксель " +
             "теряется. Для заметности нужно 0.15–0.5.")]
    public Vector2 widthRange = new Vector2(0.16f, 0.42f);

    [Tooltip("Амплитуда волны на прямом участке, доля от длины.")]
    [Range(0f, 0.4f)] public float waveAmount = 0.16f;

    [Tooltip("Сколько волн укладывается на прямом участке.")]
    [Range(0.5f, 4f)] public float waveCount = 1.6f;

    [Tooltip("Доля длины, уходящая в завиток. 0 = просто линия.")]
    [Range(0f, 0.6f)] public float curlPart = 0.34f;

    [Tooltip("Витков в завитке.")]
    [Range(0.5f, 3f)] public float curlTurns = 1.6f;

    [Tooltip("Радиус завитка, доля от длины.")]
    [Range(0.02f, 0.3f)] public float curlRadius = 0.14f;

    [Header("Движение")]
    [Tooltip("Скорость полёта росчерка, м/с. Складывается со скоростью пера.")]
    public Vector2 speedRange = new Vector2(6f, 16f);

    [Tooltip("За сколько секунд перо проходит штрих от начала до конца.")]
    public Vector2 drawTimeRange = new Vector2(0.7f, 1.4f);

    [Header("Погода")]
    [Tooltip("Брать направление и частоту у CatCooksWindController.")]
    public bool useGlobalWind = true;

    [Tooltip("0 = штиль, 1 = шторм. Ведёт погодный пресет.")]
    [Range(0f, 1f)] public float weather = 0.3f;

    [Tooltip("Ниже этой силы порывов не видно совсем.")]
    [Range(0f, 1f)] public float minWeather = 0.06f;

    public int seed = 7717;

    // ───────── внутреннее ─────────
    private class Streak
    {
        public GameObject go;
        public Transform t;
        public MeshRenderer mr;
        public Mesh mesh;
        public bool alive;
        public float head;       // положение пера, −trail … 1+trail
        public float drawSpeed;
        public float flySpeed;
        public Vector3 dir;
        public float fade;
    }

    private readonly List<Streak> _pool = new List<Streak>();
    private MaterialPropertyBlock _mpb;
    private System.Random _rng;
    private float _nextSpawn;
    private double _lastEditorTime;

    static readonly int HeadId = Shader.PropertyToID("_Head");
    static readonly int FadeId = Shader.PropertyToID("_Fade");
    static readonly int WindDirId = Shader.PropertyToID("_CatWindDir");
    static readonly int WindStrId = Shader.PropertyToID("_CatWindStrength");

    // ========================================================== жизненный цикл
    void OnEnable()
    {
        _mpb = new MaterialPropertyBlock();
        _rng = new System.Random(seed);
        BuildPool();
#if UNITY_EDITOR
        _lastEditorTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
        ClearPool();
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall += () =>
        {
            if (this == null || !isActiveAndEnabled) return;
            BuildPool();
        };
#endif
    }

    void Update()
    {
        if (Application.isPlaying) Tick(Time.deltaTime);
    }

#if UNITY_EDITOR
    void EditorTick()
    {
        if (Application.isPlaying || this == null || !isActiveAndEnabled) return;
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastEditorTime);
        _lastEditorTime = now;
        if (dt <= 0f || dt > 0.5f) return;
        Tick(dt);
        SceneView.RepaintAll();
    }
#endif

    // ================================================================== кадр
    void Tick(float dt)
    {
        if (streakMaterial == null || _pool.Count == 0) return;

        float w = Mathf.Clamp01(weather);
        if (useGlobalWind)
        {
            // 0.12 = Calm, 1.9 = Storm в пресетах CatCooksWindController.
            float g = Mathf.InverseLerp(0.12f, 1.9f, Shader.GetGlobalFloat(WindStrId));
            w = Mathf.Max(w, Mathf.Clamp01(g));
        }

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
#if UNITY_EDITOR
        if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
            cam = SceneView.lastActiveSceneView.camera;
#endif
        if (cam == null) return;

        // Двигаем живые.
        int alive = 0;
        foreach (var s in _pool)
        {
            if (!s.alive) continue;
            alive++;

            s.head += s.drawSpeed * dt;
            s.t.position += s.dir * (s.flySpeed * dt);

            // Штрих отжил, когда перо ушло далеко за хвост.
            if (s.head > 1.9f) { Kill(s); continue; }

            s.mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(HeadId, s.head);
            _mpb.SetFloat(FadeId, s.fade);
            s.mr.SetPropertyBlock(_mpb);
        }

        if (w < minWeather) return;

        _nextSpawn -= dt;
        if (_nextSpawn > 0f || alive >= maxStreaks) return;

        // Чем сильнее ветер, тем чаще порывы.
        float interval = Mathf.Lerp(intervalCalmStorm.x, intervalCalmStorm.y, w);
        _nextSpawn = interval * Mathf.Lerp(0.6f, 1.4f, (float)_rng.NextDouble());

        Spawn(cam, w);
    }

    void Spawn(Camera cam, float w)
    {
        Streak s = null;
        foreach (var p in _pool) if (!p.alive) { s = p; break; }
        if (s == null) return;

        // Направление ветра.
        Vector3 dir;
        if (useGlobalWind)
        {
            Vector4 g = Shader.GetGlobalVector(WindDirId);
            dir = new Vector3(g.x, 0f, g.z);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        }
        else dir = Vector3.forward;
        dir.Normalize();

        // Появляемся с наветренной стороны, сбоку от взгляда, чтобы
        // росчерк входил в кадр, а не возникал в центре экрана.
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        Vector3 origin = cam.transform.position
                       - dir * radius
                       + side * ((float)_rng.NextDouble() - 0.5f) * radius * 1.6f
                       + Vector3.up * Mathf.Lerp(heightRange.x, heightRange.y,
                                                 (float)_rng.NextDouble());

        float length = Mathf.Lerp(lengthRange.x, lengthRange.y, (float)_rng.NextDouble());
        float width = Mathf.Lerp(widthRange.x, widthRange.y, (float)_rng.NextDouble());

        BuildStreakMesh(s.mesh, cam, dir, length, width);

        s.t.position = origin;
        s.t.rotation = Quaternion.identity;   // меш уже построен в мире
        s.alive = true;
        s.dir = dir;
        s.head = -0.15f;

        float drawTime = Mathf.Lerp(drawTimeRange.y, drawTimeRange.x, w);
        s.drawSpeed = 1.6f / Mathf.Max(drawTime, 0.05f);
        s.flySpeed = Mathf.Lerp(speedRange.x, speedRange.y, w)
                   * Mathf.Lerp(0.8f, 1.2f, (float)_rng.NextDouble());
        s.fade = Mathf.Lerp(0.55f, 1f, w);

        s.go.SetActive(true);
    }

    void Kill(Streak s)
    {
        s.alive = false;
        if (s.go != null) s.go.SetActive(false);
    }

    // =========================================== построение ленты по кривой
    /// <summary>
    /// Кривая: волнистый прямой участок, переходящий в спираль.
    /// Лента разворачивается к камере — плоский росчерк с ребра исчез бы.
    /// </summary>
    void BuildStreakMesh(Mesh mesh, Camera cam, Vector3 dir, float length, float width)
    {
        const int Segments = 48;

        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        Vector3 toCam = (cam.transform.position - transform.position).normalized;

        // Плоскость росчерка: вдоль ветра и «вверх экрана».
        Vector3 planeUp = Vector3.Cross(dir, toCam).normalized;
        if (planeUp.sqrMagnitude < 0.001f) planeUp = Vector3.up;

        float straightLen = length * (1f - curlPart);
        float curlLen = length * curlPart;
        float curlR = length * curlRadius;

        var verts = new Vector3[(Segments + 1) * 2];
        var uvs = new Vector2[(Segments + 1) * 2];
        var tris = new int[Segments * 6];

        Vector3 curlCenter = Vector3.zero;
        Vector3 prev = Vector3.zero;

        for (int i = 0; i <= Segments; i++)
        {
            float u = i / (float)Segments;
            Vector3 p;

            float tStraight = straightLen / Mathf.Max(length, 0.001f);

            if (u <= tStraight)
            {
                // Прямой участок с волной.
                float k = u / Mathf.Max(tStraight, 0.001f);
                float wave = Mathf.Sin(k * Mathf.PI * 2f * waveCount)
                           * waveAmount * length * Mathf.Sin(k * Mathf.PI);
                p = dir * (u * length) + planeUp * wave;

                // Запоминаем, где начнётся завиток и куда смотрит кривая.
                if (i == Segments || u + 1f / Segments > tStraight)
                    curlCenter = p + planeUp * curlR;
            }
            else
            {
                // ЗАВИТОК. Спираль в плоскости росчерка, радиус сходит
                // к нулю — линия закручивается внутрь и гаснет.
                float k = (u - tStraight) / Mathf.Max(1f - tStraight, 0.001f);
                float ang = k * curlTurns * Mathf.PI * 2f - Mathf.PI * 0.5f;
                float r = curlR * (1f - k * 0.75f);

                p = curlCenter
                  + dir * (Mathf.Cos(ang) * r)
                  + planeUp * (Mathf.Sin(ang) * r)
                  + dir * (curlLen * 0.12f * k);
            }

            // Толщина вдоль штриха. Раньше синус сводил ленту почти
            // в точку на обоих концах, и от росчерка оставалась видимой
            // только середина. Теперь минимум держим выше, а к голове
            // лента, наоборот, чуть расширяется — как нажим пера.
            float taper = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI);
            float swell = Mathf.Lerp(1f, 1.35f, u);
            float hw = width * Mathf.Lerp(0.55f, 1f, taper) * swell * 0.5f;

            // Поперечное направление — перпендикуляр к ходу кривой
            // в плоскости росчерка.
            Vector3 tangent = i == 0 ? dir : (p - prev).normalized;
            Vector3 across = Vector3.Cross(tangent, toCam).normalized;
            if (across.sqrMagnitude < 0.001f) across = side;
            prev = p;

            verts[i * 2 + 0] = p - across * hw;
            verts[i * 2 + 1] = p + across * hw;
            uvs[i * 2 + 0] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);

            if (i < Segments)
            {
                int b = i * 6, v = i * 2;
                tris[b + 0] = v; tris[b + 1] = v + 2; tris[b + 2] = v + 1;
                tris[b + 3] = v + 1; tris[b + 4] = v + 2; tris[b + 5] = v + 3;
            }
        }

        mesh.Clear();
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
    }

    // ================================================================ пул
    void BuildPool()
    {
        ClearPool();
        for (int i = 0; i < maxStreaks; i++)
        {
            var go = new GameObject($"Streak_{i:00}");
            go.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var mesh = new Mesh { name = "WindStreak", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = streakMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            _pool.Add(new Streak { go = go, t = go.transform, mr = mr, mesh = mesh });
        }
        _nextSpawn = 0.2f;
    }

    void ClearPool()
    {
        foreach (var s in _pool)
        {
            if (s.mesh != null) DestroySafe(s.mesh);
            if (s.go != null) DestroySafe(s.go);
        }
        _pool.Clear();

        var kill = new List<GameObject>();
        foreach (Transform t in transform) kill.Add(t.gameObject);
        foreach (var g in kill) DestroySafe(g);
    }

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    /// <summary>Дёргать из погодных пресетов: 0 = штиль, 1 = шторм.</summary>
    public void SetWeather(float t) => weather = Mathf.Clamp01(t);
}
