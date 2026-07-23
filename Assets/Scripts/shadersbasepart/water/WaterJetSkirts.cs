using UnityEngine;

/// <summary>
/// МНОГОСЛОЙНЫЕ КОНУСЫ-ЮБКИ вокруг основной струи. Даёт объём «платья»:
/// основной поток внутри (WaterJet), а вокруг несколько расширяющихся книзу
/// конусов, каждый со своим радиусом, прозрачностью и ВРАЩЕНИЕМ по оси струи —
/// слои разлетаются и крутятся, вода выглядит объёмной и живой.
///
/// Вешается на ТОТ ЖЕ объект, что WaterJet (или ребёнком). Каждый слой — свой
/// сгенерированный меш-конус вдоль той же параболы, что и основная струя, но с
/// более широким концом (Flare) и закрученный вокруг оси на свой угол.
///
/// Слои — чистый ЛОКАЛЬНЫЙ визуал, форму берут из того же напора (WaterJet.Flow01)
/// и того же пути параболы, поэтому у всех клиентов совпадают.
///
/// НАСТРОЙКА:
///   1. Компонент на объект с WaterJet (Jet подтянется автоматически).
///   2. Материал слоёв (тот же WaterJetURP или отдельный, попрозрачнее).
///   3. Настрой Layers (кол-во, разброс радиуса, скорость вращения).
/// </summary>
[RequireComponent(typeof(WaterJet))]
public class WaterJetSkirts : MonoBehaviour
{
    [System.Serializable]
    public class Layer
    {
        [Tooltip("Множитель радиуса конца (широкая юбка = 2..4)")]
        public float endFlare = 2.5f;
        [Tooltip("Множитель радиуса начала (узкое горлышко = 0.3..0.8)")]
        public float startScale = 0.6f;
        [Tooltip("Скорость вращения этого слоя вокруг оси струи, град/сек (знак = сторона)")]
        public float spinSpeed = 40f;
        [Tooltip("Прозрачность слоя (внешние делай прозрачнее)")]
        [Range(0f, 1f)] public float opacity = 0.4f;
        [Tooltip("Смещение начала слоя вниз по струе (0..1) — где юбка стартует")]
        [Range(0f, 0.9f)] public float startAlong = 0.1f;
        [Tooltip("Волнистость края (кол-во фестонов по кругу)")]
        public int scallops = 6;
        [Tooltip("Глубина фестонов")]
        [Range(0f, 0.5f)] public float scallopDepth = 0.15f;

        [HideInInspector] public GameObject go;
        [HideInInspector] public MeshFilter mf;
        [HideInInspector] public MeshRenderer mr;
        [HideInInspector] public Mesh mesh;
        [HideInInspector] public float spinAngle;
        [HideInInspector] public Vector3[] verts;
        [HideInInspector] public Vector2[] uvs;
        [HideInInspector] public int[] tris;
    }

    [Header("Слои-юбки (внешние делай шире и прозрачнее)")]
    [SerializeField] private Layer[] _layers = new Layer[]
    {
        new Layer { endFlare = 1.8f, startScale = 0.7f, spinSpeed =  35f, opacity = 0.55f, startAlong = 0.05f, scallops = 5, scallopDepth = 0.10f },
        new Layer { endFlare = 2.6f, startScale = 0.5f, spinSpeed = -50f, opacity = 0.40f, startAlong = 0.15f, scallops = 7, scallopDepth = 0.18f },
        new Layer { endFlare = 3.4f, startScale = 0.4f, spinSpeed =  28f, opacity = 0.25f, startAlong = 0.30f, scallops = 9, scallopDepth = 0.22f },
    };

    [Header("Материал слоёв")]
    [Tooltip("Материал юбок (тот же WaterJetURP или отдельный попрозрачнее)")]
    [SerializeField] private Material _skirtMaterial;

    [Header("Геометрия")]
    [Range(6, 40)] [SerializeField] private int _lengthSegments = 18;
    [Range(6, 24)] [SerializeField] private int _radialSegments = 14;
    [Tooltip("Базовый радиус юбок (× на flare каждого слоя)")]
    [SerializeField] private float _baseRadius = 0.25f;

    private WaterJet _jet;
    private MaterialPropertyBlock _mpb;
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int OpacityId = Shader.PropertyToID("_BodyOpacity");

    private void Awake()
    {
        _jet = GetComponent<WaterJet>();
        _mpb = new MaterialPropertyBlock();
        BuildLayerObjects();
    }

    private void BuildLayerObjects()
    {
        foreach (Layer L in _layers)
        {
            L.go = new GameObject("WaterSkirt");
            L.go.transform.SetParent(transform, false);
            L.mf = L.go.AddComponent<MeshFilter>();
            L.mr = L.go.AddComponent<MeshRenderer>();
            L.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (_skirtMaterial != null) L.mr.sharedMaterial = _skirtMaterial;
            L.mesh = new Mesh { name = "SkirtMesh" };
            L.mesh.MarkDynamic();
            L.mf.sharedMesh = L.mesh;
        }
    }

    private void LateUpdate()
    {
        // Читаем путь и напор из основной струи (публичные геттеры добавлены в WaterJet).
        Vector3[] path = _jet.PathPointsWorld;
        int count = _jet.PathCount;
        float flow = _jet.Flow01;

        bool show = _jet.IsFlowing && count >= 2;

        foreach (Layer L in _layers)
        {
            if (!show)
            {
                if (L.mr.enabled) L.mr.enabled = false;
                continue;
            }
            if (!L.mr.enabled) L.mr.enabled = true;

            L.spinAngle += L.spinSpeed * Time.deltaTime;
            BuildSkirt(L, path, count, flow);

            // Прозрачность + напор в шейдер (юбки прозрачнее ядра).
            L.mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(IntensityId, flow);
            _mpb.SetFloat(OpacityId, L.opacity);
            L.mr.SetPropertyBlock(_mpb);
        }
    }

    /// <summary>Строим один конус-юбку вдоль пути струи: радиус растёт к концу
    /// (flare), край волнистый (фестоны), весь слой закручен на spinAngle.</summary>
    private void BuildSkirt(Layer L, Vector3[] path, int count, float flow)
    {
        // Слой стартует не от самого выхода, а с startAlong (юбка ниже горлышка).
        int startIdx = Mathf.Clamp(Mathf.RoundToInt(L.startAlong * (count - 1)), 0, count - 2);
        int rings = count - startIdx;
        if (rings < 2) { L.mr.enabled = false; return; }

        int vpr = _radialSegments + 1;
        int vCount = rings * vpr;

        if (L.verts == null || L.verts.Length != vCount)
        {
            L.verts = new Vector3[vCount];
            L.uvs = new Vector2[vCount];
            L.tris = new int[(rings - 1) * _radialSegments * 6];
            int ti = 0;
            for (int i = 0; i < rings - 1; i++)
                for (int s = 0; s < _radialSegments; s++)
                {
                    int a = i * vpr + s, b = a + vpr;
                    L.tris[ti++] = a; L.tris[ti++] = b; L.tris[ti++] = a + 1;
                    L.tris[ti++] = a + 1; L.tris[ti++] = b; L.tris[ti++] = b + 1;
                }
        }

        float baseR = _baseRadius * Mathf.Lerp(0.6f, 1f, flow);
        float spinRad = L.spinAngle * Mathf.Deg2Rad;

        // Стартовый фрейм для parallel transport.
        Vector3 prevT = (path[startIdx + 1] - path[startIdx]).normalized;
        Vector3 normal = Vector3.Cross(prevT, Vector3.up);
        if (normal.sqrMagnitude < 1e-4f) normal = Vector3.Cross(prevT, Vector3.right);
        normal.Normalize();

        for (int i = 0; i < rings; i++)
        {
            int p = startIdx + i;
            Vector3 t = p < count - 1 ? (path[p + 1] - path[p]).normalized : prevT;
            if (t.sqrMagnitude < 1e-6f) t = prevT;

            Quaternion rot = Quaternion.FromToRotation(prevT, t);
            normal = (rot * normal).normalized;
            normal = (normal - Vector3.Dot(normal, t) * t).normalized;
            Vector3 binormal = Vector3.Cross(t, normal);
            prevT = t;

            float vAlong = (float)i / (rings - 1);
            // РАСШИРЕНИЕ к концу (flare), узкое начало (startScale).
            float flare = Mathf.Lerp(L.startScale, L.endFlare, vAlong * vAlong);
            float ringR = baseR * flare;

            Vector3 localCenter = transform.InverseTransformPoint(path[p]);

            for (int s = 0; s < vpr; s++)
            {
                float ang = (float)s / _radialSegments * Mathf.PI * 2f + spinRad;
                // Фестоны — волнистый край, сильнее к концу юбки.
                float scallop = 1f + Mathf.Sin(ang * L.scallops) * L.scallopDepth * vAlong;
                Vector3 dir = Mathf.Cos(ang) * normal + Mathf.Sin(ang) * binormal;
                int idx = i * vpr + s;
                L.verts[idx] = localCenter +
                    transform.InverseTransformDirection(dir) * (ringR * scallop);
                L.uvs[idx] = new Vector2((float)s / _radialSegments, vAlong);
            }
        }

        L.mesh.Clear();
        L.mesh.vertices = L.verts;
        L.mesh.uv = L.uvs;
        L.mesh.triangles = L.tris;
        L.mesh.RecalculateNormals();
        L.mesh.RecalculateBounds();
    }
}
