using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// МЕШ ПЛАМЕНИ ИЗ ЛЕПЕСТКОВ. Строится кодом, руками моделировать нечего.
///
/// Пламя собирается из карточек-лепестков, расставленных КОЛЬЦАМИ вокруг центра
/// и наклонённых наружу — как лепестки пиона. Каждый лепесток колышется сам по
/// себе, поэтому даже при полной неподвижности геометрии огонь выглядит живым.
///
/// ═══ ПОЧЕМУ ЛЕПЕСТКИ, А НЕ ЦИЛИНДР ═══
///
/// Цилиндрическая оболочка даёт ровно ту беду, которая видна на скринах: сбоку
/// это стенка, сверху — кольцо, и ни с одного ракурса нет ОБЪЁМА. Шум по стенке
/// рисует пятна, но силуэт остаётся цилиндром.
///
/// У лепестков силуэт создаёт сама геометрия: с любого ракурса видно несколько
/// карточек внахлёст, и глаз читает это как объёмное пламя. Тот же приём, что в
/// стилизованных играх с рисованным огнём, — он старше и надёжнее любых шейдерных
/// трюков.
///
/// ═══ КОЛЬЦА = ТЕМПЕРАТУРА ═══
///
/// Кольца отличаются не только радиусом:
///
///   ВНУТРЕННЕЕ — высокое, узкое, почти вертикальное. Самое горячее: бело-жёлтое.
///   СРЕДНЕЕ    — пониже, наклонено сильнее. Оранжевое.
///   ВНЕШНЕЕ    — низкое, широко разваленное. Холодное: красное, дымное.
///
/// Это не украшение, а физика: у настоящего пламени сердцевина горячее и выше,
/// а края успевают остыть и заваливаются. Номер кольца уходит в шейдер вторым
/// UV-каналом, и цвет с высотой выводятся из него — а не назначаются вручную
/// каждому лепестку.
///
/// ═══ ОПТИМИЗАЦИЯ ═══
///
/// Три кольца по восемь лепестков — это 24 карточки, 96 вершин, ОДИН меш и ОДИН
/// вызов отрисовки. CPU не делает ничего: колыхание, высота и раздув считаются
/// в вершинном шейдере из времени и параметров материала.
///
/// Для сравнения: тот же эффект на частицах — это сотни билбордов, CPU-симуляция
/// каждый кадр и овердро в десятки слоёв.
///
/// ═══ СБОРКА ═══
///
/// Повесь на пустышку в центре кострища. MeshFilter и MeshRenderer добавятся
/// сами. В MeshRenderer положи материал на шейдере CatCooks/Flame Petals, а сам
/// этот объект укажи в FireVisualController как Fire Renderer.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class FlamePetalMesh : MonoBehaviour
{
    [Header("Кольца")]
    [Tooltip("Сколько колец лепестков. 3 — золотая середина: видно " +
             "температурное расслоение, но геометрия остаётся дешёвой.")]
    [Range(1, 5)][SerializeField] private int _rings = 3;

    [Tooltip("Лепестков во ВНУТРЕННЕМ кольце")]
    [Range(3, 24)][SerializeField] private int _petalsInner = 7;

    [Tooltip("Лепестков во ВНЕШНЕМ кольце. Больше внутреннего: длина окружности " +
             "там больше, и при равном числе внешние лепестки стояли бы с " +
             "прорехами.")]
    [Range(3, 32)][SerializeField] private int _petalsOuter = 11;

    [Header("Размеры (в долях, масштаб задаёт скрипт визуала)")]
    [Tooltip("Радиус внутреннего кольца")]
    [Range(0f, 1f)][SerializeField] private float _innerRadius = 0.12f;

    [Tooltip("Радиус внешнего кольца")]
    [Range(0.1f, 1.5f)][SerializeField] private float _outerRadius = 0.62f;

    [Tooltip("Высота лепестков ВНУТРЕННЕГО кольца. Сердцевина у пламени всегда " +
             "выше краёв.")]
    [Range(0.2f, 2f)][SerializeField] private float _innerHeight = 1f;

    [Tooltip("Высота лепестков ВНЕШНЕГО кольца")]
    [Range(0.1f, 1.5f)][SerializeField] private float _outerHeight = 0.5f;

    [Tooltip("Ширина лепестка у основания, в долях радиуса кольца")]
    [Range(0.1f, 3f)][SerializeField] private float _petalWidth = 0.85f;

    [Header("Наклон")]
    [Tooltip("Наклон наружу у ВНУТРЕННЕГО кольца, градусы. Почти вертикально.")]
    [Range(0f, 60f)][SerializeField] private float _innerTilt = 4f;

    [Tooltip("Наклон наружу у ВНЕШНЕГО кольца, градусы. Края пламени " +
             "заваливаются, остывая — отсюда и форма цветка.")]
    [Range(0f, 80f)][SerializeField] private float _outerTilt = 38f;

    [Header("Разброс")]
    [Tooltip("Случайный разброс высоты лепестков, доля. Ноль — все одинаковые, " +
             "и пламя выглядит штампованным.")]
    [Range(0f, 0.6f)][SerializeField] private float _heightJitter = 0.3f;

    [Tooltip("Зерно случайности. Меняй, если конкретная раскладка не нравится.")]
    [SerializeField] private int _seed = 12345;

    [Header("Отладка")]
    [SerializeField] private bool _logOnBuild = false;

    private Mesh _mesh;

    private void Awake() => Build();

    /// <summary>Собрать меш. Публичный, чтобы можно было пересобрать после
    /// правки полей — из кнопки в инспекторе.</summary>
    public void Build()
    {
        MeshFilter mf = GetComponent<MeshFilter>();

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "FlamePetals" };
            // Меш живёт только в рантайме и в сцене не сохраняется: иначе он
            // копился бы в сцене при каждой пересборке.
            _mesh.hideFlags = HideFlags.DontSave;
        }
        _mesh.Clear();

        Random.State prev = Random.state;
        Random.InitState(_seed);

        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Vector4> data = new List<Vector4>();   // фаза, кольцо 0..1, высота
        List<int> tris = new List<int>();

        for (int ring = 0; ring < _rings; ring++)
        {
            // ringT: 0 — сердцевина, 1 — край. Из него выводится ВСЁ: радиус,
            // высота, наклон, температура в шейдере. Одно число вместо трёх
            // независимых наборов полей на кольцо.
            float ringT = _rings == 1 ? 0f : ring / (float)(_rings - 1);

            float radius = Mathf.Lerp(_innerRadius, _outerRadius, ringT);
            float height = Mathf.Lerp(_innerHeight, _outerHeight, ringT);
            float tilt = Mathf.Lerp(_innerTilt, _outerTilt, ringT);
            int count = Mathf.RoundToInt(Mathf.Lerp(_petalsInner, _petalsOuter, ringT));

            // Сдвиг колец по углу: без него лепестки встают колоннами друг за
            // другом и сквозь пламя видны просветы.
            float angleOffset = ring * (Mathf.PI * 2f / Mathf.Max(1, count)) * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float a = angleOffset + i / (float)count * Mathf.PI * 2f;
                float h = height * (1f + Random.Range(-_heightJitter, _heightJitter));

                AddPetal(verts, uvs, data, tris,
                         a, radius, h, tilt, ringT,
                         Random.Range(0f, 100f));
            }
        }

        Random.state = prev;

        _mesh.SetVertices(verts);
        _mesh.SetUVs(0, uvs);
        _mesh.SetUVs(1, data);
        _mesh.SetTriangles(tris, 0);

        // Нормали не считаем: шейдер unlit, они ему не нужны, а расчёт стоит
        // времени и места в вершинном буфере.
        _mesh.RecalculateBounds();

        mf.sharedMesh = _mesh;

        if (_logOnBuild)
            Debug.Log($"[Пламя] «{name}»: лепестков {tris.Count / 6}, " +
                      $"вершин {verts.Count}", this);
    }

    /// <summary>
    /// Один лепесток — карточка, сужающаяся кверху, стоящая на радиусе и
    /// наклонённая наружу.
    ///
    /// Сужение задано ГЕОМЕТРИЕЙ, а не альфой: так силуэт остаётся чётким при
    /// любом качестве шейдера и не зависит от разрешения текстуры.
    /// </summary>
    private void AddPetal(List<Vector3> verts, List<Vector2> uvs, List<Vector4> data,
                          List<int> tris,
                          float angle, float radius, float height, float tiltDeg,
                          float ringT, float phase)
    {
        int b = verts.Count;

        Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 side = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));

        // Наклон наружу: верх лепестка уезжает от центра.
        float tilt = tiltDeg * Mathf.Deg2Rad;
        Vector3 up = Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt);

        float wBase = radius * _petalWidth;
        float wTip = wBase * 0.18f;

        Vector3 root = outward * radius;
        Vector3 tip = root + up * height;

        verts.Add(root - side * wBase);
        verts.Add(root + side * wBase);
        verts.Add(tip + side * wTip);
        verts.Add(tip - side * wTip);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(0f, 1f));

        // Одинаковые данные на все четыре вершины: шейдеру нужен признак
        // лепестка целиком, а не отдельной вершины.
        Vector4 d = new Vector4(phase, ringT, height, 0f);
        data.Add(d); data.Add(d); data.Add(d); data.Add(d);

        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
        tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);

        // Обратная сторона: пламя должно быть видно и изнутри, и снаружи.
        // Дублируем треугольники, а не ставим Cull Off в шейдере — так у нас
        // остаётся возможность рисовать стороны по-разному, если понадобится.
        tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
        tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
    }

#if UNITY_EDITOR
    [ContextMenu("Пересобрать пламя")]
    private void EditorRebuild()
    {
        Build();
        Debug.Log($"[Пламя] «{name}» пересобрано", this);
    }
#endif

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.DrawWireSphere(Vector3.zero, _innerRadius);
        Gizmos.DrawWireSphere(Vector3.zero, _outerRadius);
        Gizmos.DrawLine(Vector3.zero, Vector3.up * _innerHeight);
    }
}
