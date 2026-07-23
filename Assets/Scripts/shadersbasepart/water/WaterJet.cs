using UnityEngine;

/// <summary>
/// СТРУЯ ВОДЫ из бокового выхода гидранта. Вешается на пустышку у выхода
/// (forward = направление вылета).
///
/// ЧТО ДЕЛАЕТ:
///   - ГЕНЕРИРУЕТ МЕШ-ТРУБУ по параболе: вылетает горизонтально, гравитация
///     загибает вниз. Форма зависит от НАПОРА (Open01 верхнего крана):
///       напор 1.0 → мощная дальняя дуга, почти прямая у выхода
///       напор 0.3 → вялая струя, быстро падает вниз
///   - РЕЙКАСТИТ вдоль параболы: где струя встречает поверхность — там конец
///     меша и точка удара (для всплеска и луж).
///   - ФИЗИКА: на высоком напоре ВЫШИБАЕТ предметы (Rigidbody) вдоль струи.
///   - ЛУЖИ: в точке удара спавнит/растит лужу (WaterPuddle).
///
/// СЕТЬ: чисто ЛОКАЛЬНЫЙ ВИЗУАЛ + серверная физика. Напор (Open01) и
/// открытость (IsOutletOpen) уже синхронизированы SyncVar'ами гидранта, поэтому
/// струя у всех клиентов одинаковая сама по себе. Выталкивание предметов — только
/// на сервере (authority физики ничьих объектов).
///
/// НАСТРОЙКА:
///   1. Пустышка "JetOrigin" у бокового выхода, forward — куда бьёт вода.
///   2. Этот скрипт на неё + MeshFilter + MeshRenderer (добавятся сами).
///   3. Материал Custom/WaterJetURP → в Jet Material.
///   4. Ссылки: Top Valve (верхняя гайка) и My Outlet (своя боковая гайка).
///   5. (Опц.) Puddle Prefab, Splash VFX в поля.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class WaterJet : MonoBehaviour
{
    [Header("Источник напора (гидрант)")]
    [Tooltip("ВЕРХНЯЯ гайка (TopValve) — её Open01 задаёт напор")]
    [SerializeField] private HydrantNut _topValve;
    [Tooltip("СВОЯ боковая гайка (SideOutlet) — вода идёт, когда крышка отпала")]
    [SerializeField] private HydrantNut _myOutlet;

    [Header("Форма струи")]
    [Tooltip("Скорость вылета на ПОЛНОМ напоре, м/с")]
    [SerializeField] private float _maxLaunchSpeed = 14f;
    [Tooltip("Скорость вылета на минимальном напоре")]
    [SerializeField] private float _minLaunchSpeed = 3f;
    [Tooltip("Гравитация струи (загиб параболы)")]
    [SerializeField] private float _gravity = 14f;
    [Tooltip("Радиус трубы на ПОЛНОМ напоре")]
    [SerializeField] private float _maxRadius = 0.22f;
    [Tooltip("Радиус на минимальном напоре")]
    [SerializeField] private float _minRadius = 0.08f;
    [Tooltip("ПРОФИЛЬ ФОРМЫ: множитель радиуса вдоль струи (X: 0=выход, 1=конец; " +
             "Y: множитель радиуса). Восходящая кривая = конус, расширяющийся к " +
             "концу (узкое начало → широкий конец). Правь кривую прямо в инспекторе.")]
    [SerializeField] private AnimationCurve _radiusProfile =
        new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.7f, 1.1f), new Keyframe(1f, 1.5f));
    [Tooltip("Сегментов вдоль струи (больше = плавнее дуга)")]
    [Range(6, 48)]
    [SerializeField] private int _lengthSegments = 22;
    [Tooltip("Сегментов по окружности")]
    [Range(4, 16)]
    [SerializeField] private int _radialSegments = 8;
    [Tooltip("Максимальное время полёта (ограничивает длину), сек")]
    [SerializeField] private float _maxFlightTime = 2.5f;

    [Header("Столкновение с миром")]
    [Tooltip("По каким слоям струя упирается (земля/стены)")]
    [SerializeField] private LayerMask _hitMask = ~0;

    [Header("Материал")]
    [Tooltip("Материал Custom/WaterJetURP")]
    [SerializeField] private Material _jetMaterial;

    [Header("Выталкивание предметов (сервер)")]
    [Tooltip("ВКЛ — струя толкает Rigidbody на пути")]
    [SerializeField] private bool _pushObjects = true;
    [Tooltip("Сила толчка на полном напоре")]
    [SerializeField] private float _maxPushForce = 18f;
    [Tooltip("Напор, ниже которого вода не толкает (просто льётся)")]
    [Range(0f, 1f)]
    [SerializeField] private float _pushThreshold = 0.6f;
    [Tooltip("Радиус захвата предметов вокруг оси струи")]
    [SerializeField] private float _pushRadius = 0.6f;

    [Header("Лужи (стабильные якоря вдоль дуги)")]
    [Tooltip("Префаб лужи (WaterPuddle). Пусто = без луж.")]
    [SerializeField] private GameObject _puddlePrefab;
    [Tooltip("Шаг между лужами вдоль дуги, м (реже = меньше и крупнее)")]
    [SerializeField] private float _puddleSpacing = 1.4f;
    [Tooltip("Макс. дальность, на которую раскладываются якоря луж, м")]
    [SerializeField] private float _maxReach = 9f;
    [Tooltip("Обычный целевой радиус лужи")]
    [SerializeField] private float _smallRadius = 0.5f;
    [Tooltip("Крупный целевой радиус лужи")]
    [SerializeField] private float _largeRadius = 1.1f;
    [Tooltip("Каждая N-я лужа — крупная (2 = через одну, 3 = каждая третья)")]
    [SerializeField] private int _largeEvery = 3;
    [Tooltip("Случайный разброс якоря вбок от линии дуги, м")]
    [SerializeField] private float _puddleScatter = 0.35f;
    [Tooltip("Не класть лужи на склонах круче этого угла, град")]
    [SerializeField] private float _maxSlopeAngle = 40f;
    [Tooltip("ПОДВИЖНЫЙ ИСТОЧНИК (брандспойт): на сколько метров сдвинуться, " +
             "чтобы лужи переложились под новое место")]
    [SerializeField] private float _anchorRebuildDist = 1.5f;
    [Tooltip("На сколько градусов повернуться, чтобы лужи переложились")]
    [SerializeField] private float _anchorRebuildAngle = 25f;

    [Header("Всплеск / блинчик в точке удара")]
    [Tooltip("Объект-эффект (корона/блин), ставится в точку удара")]
    [SerializeField] private GameObject _splashEffect;
    [Tooltip("Доворот блина, если он лёг не плоско. Плоский диск-квад обычно (90,0,0). " +
             "Вертикальная корона-юбка = (0,0,0).")]
    [SerializeField] private Vector3 _splashEulerOffset = Vector3.zero;

    // ── runtime ──
    private MeshFilter _mf;
    private MeshRenderer _mr;
    private Mesh _mesh;
    private Vector3[] _verts;
    private Vector2[] _uvs;
    private int[] _tris;
    private Vector3[] _pathPoints;   // точки параболы (мировые)
    private int _pathCount;
    private Vector3 _hitPoint;
    private Vector3 _hitNormal = Vector3.up;
    private bool _hasHit;
    // Якоря луж — фиксированные позиции вдоль дуги (не прыгают).
    private struct PuddleAnchor { public WaterPuddle puddle; public float dist; public float maxR; }
    private PuddleAnchor[] _anchors;
    private bool _anchorsBuilt;
    private Vector3 _anchorOrigin;   // где раскладывали якоря
    private Vector3 _anchorForward;
    private MaterialPropertyBlock _mpb;

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    /// <summary>Текущий напор 0..1 (для внешних систем/звука).</summary>
    public float Flow01 { get; private set; }

    /// <summary>Точки параболы в МИРОВЫХ координатах (для слоёв-юбок).</summary>
    public Vector3[] PathPointsWorld => _pathPoints;

    /// <summary>Сколько точек пути валидно (первые PathCount элементов).</summary>
    public int PathCount => _pathCount;

    /// <summary>Идёт ли вода прямо сейчас.</summary>
    public bool IsFlowing => Flow01 > 0.02f;

    // Внешний источник напора (брандспойт): переопределяет гайки.
    private bool _useExternal;
    private bool _extOpen;
    private float _extFlow;

    /// <summary>Задать поток ИЗВНЕ (брандспойт вместо гидранта). open — льёт ли,
    /// flow — напор 0..1. Пока вызывается каждый кадр — источник внешний.</summary>
    public void SetExternalFlow(bool open, float flow)
    {
        _useExternal = true;
        _extOpen = open;
        _extFlow = Mathf.Clamp01(flow);
    }

    private void Awake()
    {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        _mesh = new Mesh { name = "WaterJetMesh" };
        _mesh.MarkDynamic();
        _mf.sharedMesh = _mesh;
        if (_jetMaterial != null) _mr.sharedMaterial = _jetMaterial;
        _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        _pathPoints = new Vector3[_lengthSegments + 1];
        _mpb = new MaterialPropertyBlock();
    }

    private void Update()
    {
        // Напор: открыт ли МОЙ выход × насколько открыт ВЕРХНИЙ кран.
        bool open;
        float valve;
        if (_useExternal)
        {
            open = _extOpen;
            valve = _extFlow;
        }
        else
        {
            open = _myOutlet != null && _myOutlet.IsOutletOpen;
            valve = _topValve != null ? _topValve.Open01 : 1f;
        }
        Flow01 = open ? valve : 0f;

        // Шейдеру — напор (гасит яркость/скорость сам).
        _mr.GetPropertyBlock(_mpb);
        _mpb.SetFloat(IntensityId, Flow01);
        _mr.SetPropertyBlock(_mpb);

        if (Flow01 <= 0.02f)
        {
            if (_mr.enabled) _mr.enabled = false;
            if (_splashEffect != null && _splashEffect.activeSelf) _splashEffect.SetActive(false);
            UpdatePuddleField(); // ВАЖНО: продолжаем сушить лужи даже когда воды нет
            return;
        }
        if (!_mr.enabled) _mr.enabled = true;

        BuildPath();
        BuildTubeMesh();
        UpdateSplash();
        UpdatePuddleField();
    }

    private void FixedUpdate()
    {
        // Физику толкаем только там, где считается физика "ничьих" объектов — на
        // сервере/хосте. На чистом клиенте кинематичные RB всё равно не сдвинуть.
        if (_pushObjects && Flow01 >= _pushThreshold)
            PushObjectsAlongJet();
    }

    // ───────── ПАРАБОЛА ─────────

    /// <summary>Строим точки полёта: старт из origin, скорость по forward,
    /// гравитация гнёт вниз. Первый рейкаст-хит обрезает путь.</summary>
    private void BuildPath()
    {
        // Защита: если Length Segments поменяли в инспекторе в рантайме —
        // пересоздаём массив (иначе IndexOutOfRange).
        if (_pathPoints == null || _pathPoints.Length != _lengthSegments + 1)
            _pathPoints = new Vector3[_lengthSegments + 1];

        float speed = Mathf.Lerp(_minLaunchSpeed, _maxLaunchSpeed, Flow01);
        Vector3 v0 = transform.forward * speed;
        Vector3 p = transform.position;

        float dtTotal = _maxFlightTime;
        float step = dtTotal / _lengthSegments;

        _pathPoints[0] = p;
        _pathCount = 1;
        _hasHit = false;

        Vector3 prev = p;
        for (int i = 1; i <= _lengthSegments; i++)
        {
            float t = step * i;
            Vector3 pos = transform.position + v0 * t + 0.5f * Vector3.down * _gravity * t * t;

            // Рейкаст сегмента: упёрлись — обрезаем струю здесь.
            Vector3 seg = pos - prev;
            float len = seg.magnitude;
            if (len > 0.0001f &&
                Physics.Raycast(prev, seg / len, out RaycastHit hit, len, _hitMask,
                                QueryTriggerInteraction.Ignore))
            {
                _pathPoints[_pathCount++] = hit.point;
                _hitPoint = hit.point;
                _hitNormal = hit.normal;
                _hasHit = true;
                return;
            }

            _pathPoints[_pathCount++] = pos;
            prev = pos;
        }

        // Не упёрлись — конец в воздухе.
        _hitPoint = _pathPoints[_pathCount - 1];
        _hitNormal = Vector3.up;
    }

    // ───────── МЕШ-ТРУБА ─────────

    /// <summary>Труба вокруг параболы (parallel transport, чтобы не крутило).
    /// UV: x — по окружности, y — вдоль (0 у выхода → 1 в конце) для шейдера.</summary>
    private void BuildTubeMesh()
    {
        int rings = _pathCount;
        int vpr = _radialSegments + 1;
        int vCount = rings * vpr;

        if (_verts == null || _verts.Length != vCount)
        {
            _verts = new Vector3[vCount];
            _uvs = new Vector2[vCount];
            _tris = new int[(rings - 1) * _radialSegments * 6];
            int ti = 0;
            for (int i = 0; i < rings - 1; i++)
                for (int s = 0; s < _radialSegments; s++)
                {
                    int a = i * vpr + s;
                    int b = a + vpr;
                    _tris[ti++] = a; _tris[ti++] = b; _tris[ti++] = a + 1;
                    _tris[ti++] = a + 1; _tris[ti++] = b; _tris[ti++] = b + 1;
                }
        }

        float radius = Mathf.Lerp(_minRadius, _maxRadius, Flow01);

        // Стартовый фрейм.
        Vector3 tangent = (_pathPoints[1] - _pathPoints[0]).normalized;
        Vector3 normal = Vector3.Cross(tangent, Vector3.up);
        if (normal.sqrMagnitude < 1e-4f) normal = Vector3.Cross(tangent, Vector3.right);
        normal.Normalize();
        Vector3 prevT = tangent;

        for (int i = 0; i < rings; i++)
        {
            Vector3 t = i < rings - 1
                ? (_pathPoints[i + 1] - _pathPoints[i]).normalized
                : prevT;
            if (t.sqrMagnitude < 1e-6f) t = prevT;

            // Parallel transport — минимальный доворот нормали.
            Quaternion rot = Quaternion.FromToRotation(prevT, t);
            normal = (rot * normal).normalized;
            normal = (normal - Vector3.Dot(normal, t) * t).normalized;
            Vector3 binormal = Vector3.Cross(t, normal);
            prevT = t;

            float vAlong = (float)i / (rings - 1);
            // Радиус по ПРОФИЛЮ из инспектора: конус, бочка, капля — любая форма.
            float r = radius * Mathf.Max(0.01f, _radiusProfile.Evaluate(vAlong));

            Vector3 local = transform.InverseTransformPoint(_pathPoints[i]);
            for (int s = 0; s < vpr; s++)
            {
                float ang = (float)s / _radialSegments * Mathf.PI * 2f;
                Vector3 dir = Mathf.Cos(ang) * normal + Mathf.Sin(ang) * binormal;
                int idx = i * vpr + s;
                _verts[idx] = local + transform.InverseTransformDirection(dir) * r;
                _uvs[idx] = new Vector2((float)s / _radialSegments, vAlong);
            }
        }

        _mesh.Clear();
        _mesh.vertices = _verts;
        _mesh.uv = _uvs;
        _mesh.triangles = _tris;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    // ───────── ВСПЛЕСК ─────────

    private void UpdateSplash()
    {
        if (_splashEffect == null) return;

        if (_hasHit)
        {
            if (!_splashEffect.activeSelf) _splashEffect.SetActive(true);
            // Корона/блинчик ЛЕЖИТ НА ЗЕМЛЕ ПЛОСКО, как прицел. Кладём строго по
            // нормали ЗЕМЛИ (up на ровном полу, наклон на склоне), а НЕ по нормали
            // струи — иначе торчит боком.
            _splashEffect.transform.SetPositionAndRotation(
                _hitPoint + _hitNormal * 0.02f,
                Quaternion.FromToRotation(Vector3.up, _hitNormal) * Quaternion.Euler(_splashEulerOffset));
            float s = Mathf.Lerp(0.5f, 1.3f, Flow01);
            _splashEffect.transform.localScale = Vector3.one * s;

            // Прокидываем напор во все материалы эффекта (корона дышит от него).
            if (_splashRenderers == null)
                _splashRenderers = _splashEffect.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in _splashRenderers)
            {
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(IntensityId, Flow01);
                r.SetPropertyBlock(_mpb);
            }
        }
        else if (_splashEffect.activeSelf)
        {
            _splashEffect.SetActive(false);
        }
    }

    private Renderer[] _splashRenderers;

    // ───────── ЛУЖИ (стабильные, плавные) ─────────

    /// <summary>Один раз раскладываем ЯКОРЯ вдоль горизонтальной проекции дуги:
    /// фиксированные позиции на земле от гидранта вперёд. Позиции НЕ меняются —
    /// поэтому лужи не прыгают. Часть якорей — крупные.</summary>
    /// <summary>Снести старые якоря и разложить заново (для подвижного ствола).</summary>
    private void RebuildAnchors()
    {
        if (_anchors != null)
            foreach (PuddleAnchor a in _anchors)
                if (a.puddle != null) Destroy(a.puddle.gameObject);
        _anchors = null;
        _anchorsBuilt = false;
    }

    private void BuildAnchors()
    {
        _anchorOrigin = transform.position;
        _anchorForward = transform.forward;

        Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;
        flat.Normalize();

        int count = Mathf.Max(1, Mathf.FloorToInt(_maxReach / Mathf.Max(0.2f, _puddleSpacing)));
        var list = new System.Collections.Generic.List<PuddleAnchor>();

        for (int k = 0; k < count; k++)
        {
            float dist = (k + 1) * _puddleSpacing;
            // фиксированный (не случайный каждый кадр) боковой сдвиг по индексу
            float side = (Mathf.PerlinNoise(k * 0.7f, 0.3f) - 0.5f) * 2f * _puddleScatter;
            Vector3 lateral = Vector3.Cross(Vector3.up, flat) * side;
            Vector3 probe = transform.position + flat * dist + lateral + Vector3.up * 1.5f;

            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit h, 12f,
                                 _hitMask, QueryTriggerInteraction.Ignore))
                continue;
            if (Vector3.Angle(h.normal, Vector3.up) > _maxSlopeAngle)
                continue;

            bool large = (_largeEvery > 0) && (k % _largeEvery == 0);
            float maxR = large ? _largeRadius : _smallRadius;
            maxR *= Random.Range(0.85f, 1.15f); // лёгкая разница размеров (фикс. при билде)

            GameObject go = Instantiate(_puddlePrefab);
            WaterPuddle pud = go.GetComponent<WaterPuddle>();
            if (pud == null) pud = go.AddComponent<WaterPuddle>();
            pud.PlaceAt(h.point, h.normal, 1f);
            pud.SetTarget(0f); // стартуем сухими, растим по мере досягаемости

            list.Add(new PuddleAnchor { puddle = pud, dist = dist, maxR = maxR });
        }

        _anchors = list.ToArray();
        _anchorsBuilt = true;
    }

    /// <summary>Каждый кадр: лужи в пределах текущей ДАЛЬНОСТИ струи растут к
    /// своему размеру, дальше — сохнут. Всё плавно (сглаживание в WaterPuddle).
    /// Когда напор падает, дальность меньше — дальние лужи сохнут ПЕРВЫМИ.</summary>
    private void UpdatePuddleField()
    {
        if (_puddlePrefab == null) return;

        // ПОДВИЖНЫЙ ИСТОЧНИК (брандспойт в руках): если ствол сдвинулся или
        // повернулся — перекладываем якоря под НОВОЕ место. Иначе лужи
        // спавнились бы там, где струя была раньше (у гидранта).
        if (_anchorsBuilt &&
            (Vector3.Distance(transform.position, _anchorOrigin) > _anchorRebuildDist ||
             Vector3.Angle(transform.forward, _anchorForward) > _anchorRebuildAngle))
        {
            RebuildAnchors();
        }

        if (!_anchorsBuilt) BuildAnchors();
        if (_anchors == null || _anchors.Length == 0) return;

        bool flowing = IsFlowing;

        // Струя не течёт → сушим ВСЕ лужи и выходим (путь может быть пуст).
        if (!flowing || _pathCount < 1)
        {
            foreach (PuddleAnchor a in _anchors)
                if (a.puddle != null) a.puddle.SetTarget(0f);
            return;
        }

        // Горизонтальная дальность струи до точки падения.
        Vector3 end = _pathPoints[_pathCount - 1];
        Vector3 flatDelta = end - transform.position; flatDelta.y = 0f;
        float reach = flatDelta.magnitude;

        foreach (PuddleAnchor a in _anchors)
        {
            if (a.puddle == null) continue;
            // Если струя не течёт — цель 0 для ВСЕХ (иначе крайняя лужа зависала).
            bool fed = flowing && a.dist <= reach + _puddleSpacing * 0.5f;
            a.puddle.SetTarget(fed ? a.maxR * Mathf.Lerp(0.6f, 1f, Flow01) : 0f);
        }
    }

    // ───────── ВЫТАЛКИВАНИЕ ─────────

    /// <summary>Толкаем Rigidbody вдоль пути струи (капсулы между точками).</summary>
    private void PushObjectsAlongJet()
    {
        if (_pathPoints == null || _pathCount < 2) return;
        int safeCount = Mathf.Min(_pathCount, _pathPoints.Length);

        float force = _maxPushForce * Flow01;

        for (int i = 0; i < safeCount - 1; i += 2) // каждый 2-й сегмент — дешевле
        {
            Vector3 a = _pathPoints[i];
            Vector3 b = _pathPoints[Mathf.Min(i + 2, safeCount - 1)];
            Vector3 dir = (b - a).normalized;

            Collider[] hits = Physics.OverlapCapsule(a, b, _pushRadius, ~0,
                                                     QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits)
            {
                Rigidbody rb = c.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                // ВАЖНО: спящее тело не реагирует на силу — будим, иначе предметы
                // сдувало только в момент запуска струи, а дальше они «залипали».
                if (rb.IsSleeping()) rb.WakeUp();
                // Толчок по направлению струи + чуть вверх (подхватывает).
                rb.AddForce((dir + Vector3.up * 0.25f).normalized * force,
                            ForceMode.Acceleration);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.forward * 2f);
    }
}
