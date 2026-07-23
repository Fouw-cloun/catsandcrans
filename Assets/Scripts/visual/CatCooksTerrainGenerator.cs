using System.Collections.Generic;
using UnityEngine;

// ============================================================================
//  CatCooks Terrain Generator
//  Процедурный генератор квадратной территории под Unity Terrain.
//
//  Планировка (вид сверху, север = +Z):
//   ┌─────────────────────────────┐
//   │ ГОРНАЯ ПОЛКА (север/зад)    │   ← «десна» под модели гор
//   │▓┌─────────────────────────┐ │
//   │▓│  ЛЕС И ХОЛМЫ            │ │
//   │▓║ ДОРОГА (вдоль левых гор)│ │
//   │▓║                         │ │
//   │▓│  ~~ РЕКА И ОЗЁРА ~~     │ │   ← юг
//   │▓└─────────────────────────┘ │
//   └─────────────────────────────┘
//   ▓ = горная полка слева (запад)
//
//  Требует компонент Terrain. Все высоты — в метрах.
//  Редактор с живым предпросмотром: CatCooksTerrainGeneratorEditor.cs
// ============================================================================
[RequireComponent(typeof(Terrain))]
public class CatCooksTerrainGenerator : MonoBehaviour
{
    // ------------------------------------------------------------------ общие
    [Header("Общее")]
    public int seed = 1234;
    [Tooltip("Перестраивать террейн при изменении любого ползунка")]
    public bool autoUpdate = true;
    [Tooltip("Базовая высота равнины над нулём террейна, м")]
    [Range(0f, 30f)] public float baseHeight = 8f;

    // ------------------------------------------------------------------ рельеф
    [Header("Рельеф и шум")]
    [Tooltip("Масштаб шума: меньше = крупные формы, больше = мелкая рябь")]
    [Range(0.5f, 8f)] public float noiseScale = 2.2f;
    [Range(1, 8)] public int octaves = 5;
    [Tooltip("Насколько каждая следующая октава слабее")]
    [Range(0.2f, 0.7f)] public float persistence = 0.45f;
    [Tooltip("Насколько каждая следующая октава мельче")]
    [Range(1.5f, 3.5f)] public float lacunarity = 2.1f;
    [Tooltip("Высота холмов, м")]
    [Range(0f, 40f)] public float hillAmplitude = 9f;
    [Tooltip("Доменное искажение — «закручивает» шум в естественные хребты и лощины. 0 = выкл")]
    [Range(0f, 1.5f)] public float domainWarp = 0.6f;
    [Tooltip("Усиление холмов в дальней от дороги (восточной) части")]
    [Range(0f, 2f)] public float hillZoneBoost = 0.8f;

    // ------------------------------------------------------------------ полка
    [Header("Горная полка — «десна» под модели гор (запад + север)")]
    [Tooltip("Ширина полки слева, доля карты")]
    [Range(0f, 0.35f)] public float shelfWestWidth = 0.14f;
    [Tooltip("Ширина полки сзади (север), доля карты")]
    [Range(0f, 0.35f)] public float shelfNorthWidth = 0.14f;
    [Tooltip("Высота полки, м — на неё ставишь модели гор")]
    [Range(0f, 60f)] public float shelfHeight = 22f;
    [Tooltip("Неровность внутреннего края полки (чтобы граница не была линейкой)")]
    [Range(0f, 0.15f)] public float shelfEdgeNoise = 0.05f;
    [Tooltip("Мягкость склона от полки к равнине")]
    [Range(0.01f, 0.25f)] public float shelfSlopeSoftness = 0.09f;
    [Tooltip("Бугристость поверхности самой полки, м")]
    [Range(0f, 10f)] public float shelfRoughness = 3f;

    // ------------------------------------------------------------------ дорога
    [Header("Дорога (идёт с юга на север вдоль левых гор)")]
    public bool roadEnabled = true;
    [Tooltip("Отступ оси дороги от левого края, доля карты")]
    [Range(0.05f, 0.6f)] public float roadPosition = 0.24f;
    [Tooltip("Ширина полотна, доля карты")]
    [Range(0.005f, 0.08f)] public float roadWidth = 0.03f;
    [Tooltip("Ширина обочины-склона")]
    [Range(0.005f, 0.12f)] public float roadShoulder = 0.05f;
    [Tooltip("Извилистость дороги")]
    [Range(0f, 0.15f)] public float roadCurviness = 0.05f;
    [Tooltip("Масштаб изгибов (меньше = длинные плавные повороты)")]
    [Range(0.5f, 6f)] public float roadCurveScale = 1.6f;

    // ------------------------------------------------------------------ река
    [Header("Река и озёра (южная часть)")]
    public bool riverEnabled = true;
    [Tooltip("Положение русла от южного края, доля карты")]
    [Range(0.02f, 0.5f)] public float riverPosition = 0.13f;
    [Tooltip("Ширина русла, доля карты")]
    [Range(0.005f, 0.1f)] public float riverWidth = 0.035f;
    [Tooltip("Глубина русла ниже равнины, м")]
    [Range(0.5f, 15f)] public float riverDepth = 4.5f;
    [Tooltip("Мягкость берегов")]
    [Range(0.005f, 0.15f)] public float bankSoftness = 0.05f;
    [Tooltip("Меандры: амплитуда изгибов русла")]
    [Range(0f, 0.15f)] public float meanderAmount = 0.06f;
    [Range(0.5f, 6f)] public float meanderScale = 2.2f;
    [Tooltip("Количество озёр, нанизанных на реку")]
    [Range(0, 6)] public int lakeCount = 2;
    [Tooltip("Радиус озёр, доля карты (min..max)")]
    public Vector2 lakeRadius = new Vector2(0.05f, 0.09f);
    [Tooltip("Глубина озёр ниже равнины, м")]
    [Range(1f, 20f)] public float lakeDepth = 6f;
    [Tooltip("Уровень воды, м (для покраски песка и водной плоскости)")]
    [Range(0f, 30f)] public float waterLevel = 5.5f;
    [Tooltip("Создавать/обновлять плоскость воды")]
    public bool createWaterPlane = true;
    public Material waterMaterial;

    // ------------------------------------------------------------------ выбоины
    [Header("Выбоины и ямки (стройка же!)")]
    [Range(0, 60)] public int pitCount = 14;
    [Tooltip("Радиус выбоин, доля карты (min..max)")]
    public Vector2 pitRadius = new Vector2(0.008f, 0.025f);
    [Tooltip("Глубина выбоин, м")]
    [Range(0f, 4f)] public float pitDepth = 1.2f;

    // ------------------------------------------------------------------ финиш
    [Header("Финишная обработка")]
    [Tooltip("Проходы сглаживания (убирают «шершавость» пикселей шума)")]
    [Range(0, 4)] public int smoothPasses = 1;

    [Header("Автопокраска (Terrain Layers по порядку: 0 трава, 1 земля/дорога, 2 камень, 3 песок)")]
    public bool paintTextures = true;
    [Tooltip("Крутизна склона, с которой начинается камень, °")]
    [Range(10f, 70f)] public float rockAngle = 38f;
    [Tooltip("Высота песчаной полосы над уровнем воды, м")]
    [Range(0.2f, 5f)] public float sandBand = 1.4f;

    [Header("Лес (зона между дорогой и холмами)")]
    public bool scatterTrees = false;
    [Tooltip("Префабы деревьев (обычные меши, станут Tree-прототипами террейна)")]
    public GameObject[] treePrefabs;
    [Range(0, 3000)] public int treeCount = 600;
    [Tooltip("Восточная граница леса, доля карты")]
    [Range(0.3f, 1f)] public float forestEastEdge = 0.75f;
    public Vector2 treeScale = new Vector2(0.8f, 1.35f);

    // ------------------------------------------------------------------ private
    private Terrain terrain;
    private float ox1, oy1, ox2, oy2, oxW, oyW; // сид-смещения шума
    private List<Vector2> lakeCenters = new List<Vector2>();
    private List<float> lakeRadii = new List<float>();
    private List<Vector3> pits = new List<Vector3>(); // xy = uv, z = радиус

    // ==================================================================
    //  ГЛАВНЫЙ МЕТОД
    // ==================================================================
    public void Generate()
    {
        terrain = GetComponent<Terrain>();
        TerrainData td = terrain.terrainData;
        int res = td.heightmapResolution;
        float terrainMaxY = td.size.y;

        InitSeed();
        BuildFeaturePoints();

        float[,] heights = new float[res, res];

        for (int y = 0; y < res; y++)
        {
            float uz = (float)y / (res - 1); // 0 = юг, 1 = север
            for (int x = 0; x < res; x++)
            {
                float ux = (float)x / (res - 1); // 0 = запад, 1 = восток
                float h = ComputeHeight(ux, uz);
                heights[y, x] = Mathf.Clamp01(h / terrainMaxY);
            }
        }

        for (int i = 0; i < smoothPasses; i++)
            Smooth(heights, res);

        td.SetHeights(0, 0, heights);

        if (paintTextures) PaintSplatmap(td);
        if (scatterTrees) ScatterTrees(td);
        else td.treeInstances = new TreeInstance[0];

        if (createWaterPlane) UpdateWaterPlane(td);

        terrain.Flush();
    }

    // ==================================================================
    //  ВЫСОТА В ТОЧКЕ (вся логика зон)
    // ==================================================================
    float ComputeHeight(float ux, float uz)
    {
        // --- базовый рельеф: fBm с доменным искажением ---
        float wx = (Mathf.PerlinNoise(ux * 2.3f + oxW, uz * 2.3f + oyW) - 0.5f) * domainWarp;
        float wz = (Mathf.PerlinNoise(ux * 2.3f + oxW + 37.7f, uz * 2.3f + oyW + 11.3f) - 0.5f) * domainWarp;
        float n = Fbm(ux + wx, uz + wz); // ~0..1

        // холмы сильнее к востоку (за дорогой)
        float hillMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(roadPosition, 0.9f, ux));
        float amp = hillAmplitude * (1f + hillZoneBoost * hillMask);
        float h = baseHeight + (n - 0.35f) * amp;

        // --- горная полка (запад + север) с рваным краем ---
        float edgeN = (Mathf.PerlinNoise(ux * 7f + ox2, uz * 7f + oy2) - 0.5f) * 2f * shelfEdgeNoise;
        float westEdge = shelfWestWidth + edgeN;
        float northEdge = 1f - shelfNorthWidth + edgeN;
        float mWest = 1f - Mathf.SmoothStep(westEdge, westEdge + shelfSlopeSoftness, ux);
        float mNorth = Mathf.SmoothStep(northEdge - shelfSlopeSoftness, northEdge, uz);
        float shelfMask = Mathf.Max(mWest, mNorth);
        if (shelfMask > 0f)
        {
            float shelfSurf = shelfHeight + (Mathf.PerlinNoise(ux * 9f + ox1, uz * 9f + oy1) - 0.5f) * 2f * shelfRoughness;
            h = Mathf.Lerp(h, shelfSurf, shelfMask);
        }

        float protect = 1f - shelfMask; // дорога/река/ямы не лезут на полку

        // --- дорога ---
        float roadMask = 0f;
        if (roadEnabled)
        {
            roadMask = RoadMask(ux, uz) * protect;
            if (roadMask > 0f)
            {
                // дорога следует крупному рельефу с малой амплитудой — плавный профиль
                float lf = Mathf.PerlinNoise(uz * 1.1f + ox1, oy1);
                float roadH = baseHeight + (lf - 0.35f) * hillAmplitude * 0.25f;
                h = Mathf.Lerp(h, roadH, roadMask);
            }
        }

        // --- река ---
        if (riverEnabled)
        {
            float rz = RiverCenter(ux);
            float d = Mathf.Abs(uz - rz);
            float m = 1f - Mathf.SmoothStep(riverWidth, riverWidth + bankSoftness, d);
            m *= protect * (1f - roadMask); // мост не копаем :)
            if (m > 0f)
            {
                float bed = baseHeight - riverDepth
                          + (Mathf.PerlinNoise(ux * 12f + ox2, oy2) - 0.5f) * 0.8f;
                h = Mathf.Lerp(h, bed, m * m * (3f - 2f * m)); // усиленный smooth к центру
            }

            // озёра
            for (int i = 0; i < lakeCenters.Count; i++)
            {
                float dl = Vector2.Distance(new Vector2(ux, uz), lakeCenters[i]);
                float r = lakeRadii[i] * (1f + (Mathf.PerlinNoise(ux * 10f + i * 3.1f, uz * 10f) - 0.5f) * 0.5f);
                float ml = 1f - Mathf.SmoothStep(r * 0.55f, r, dl);
                ml *= protect;
                if (ml > 0f)
                    h = Mathf.Lerp(h, baseHeight - lakeDepth, ml);
            }
        }

        // --- выбоины ---
        foreach (var p in pits)
        {
            float dp = Vector2.Distance(new Vector2(ux, uz), new Vector2(p.x, p.y));
            if (dp < p.z)
            {
                float mp = 1f - Mathf.SmoothStep(0f, p.z, dp);
                h -= pitDepth * mp * mp * protect * (1f - roadMask);
            }
        }

        return h;
    }

    // ------------------------------------------------------------------ формы
    float Fbm(float x, float y)
    {
        float sum = 0f, ampl = 1f, freq = noiseScale, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Mathf.PerlinNoise(x * freq + ox1 + i * 13.7f, y * freq + oy1 + i * 7.3f) * ampl;
            norm += ampl;
            ampl *= persistence;
            freq *= lacunarity;
        }
        return sum / norm;
    }

    float RoadCenter(float uz) =>
        roadPosition + (Mathf.PerlinNoise(uz * roadCurveScale + ox2, oy2 + 5f) - 0.5f) * 2f * roadCurviness;

    float RoadMask(float ux, float uz)
    {
        float d = Mathf.Abs(ux - RoadCenter(uz));
        return 1f - Mathf.SmoothStep(roadWidth, roadWidth + roadShoulder, d);
    }

    float RiverCenter(float ux) =>
        riverPosition + (Mathf.PerlinNoise(ux * meanderScale + ox1 + 91f, oy1 + 17f) - 0.5f) * 2f * meanderAmount;

    // ------------------------------------------------------------------ сид
    void InitSeed()
    {
        var r = new System.Random(seed);
        float R() => (float)r.NextDouble() * 1000f;
        ox1 = R(); oy1 = R(); ox2 = R(); oy2 = R(); oxW = R(); oyW = R();
    }

    void BuildFeaturePoints()
    {
        var r = new System.Random(seed * 7 + 3);
        float Rf(float a, float b) => a + (float)r.NextDouble() * (b - a);

        lakeCenters.Clear(); lakeRadii.Clear();
        for (int i = 0; i < lakeCount; i++)
        {
            float lx = Rf(shelfWestWidth + 0.08f, 0.92f);
            lakeCenters.Add(new Vector2(lx, RiverCenter(lx) + Rf(-0.02f, 0.02f)));
            lakeRadii.Add(Rf(lakeRadius.x, lakeRadius.y));
        }

        pits.Clear();
        for (int i = 0; i < pitCount; i++)
            pits.Add(new Vector3(Rf(shelfWestWidth + 0.05f, 0.95f),
                                 Rf(riverPosition + 0.12f, 1f - shelfNorthWidth - 0.05f),
                                 Rf(pitRadius.x, pitRadius.y)));
    }

    // ------------------------------------------------------------------ сглаживание
    void Smooth(float[,] h, int res)
    {
        for (int y = 1; y < res - 1; y++)
            for (int x = 1; x < res - 1; x++)
                h[y, x] = (h[y, x] * 4f + h[y - 1, x] + h[y + 1, x] + h[y, x - 1] + h[y, x + 1]) / 8f;
    }

    // ------------------------------------------------------------------ покраска
    void PaintSplatmap(TerrainData td)
    {
        int layers = td.alphamapLayers;
        if (layers == 0) return;
        int res = td.alphamapResolution;
        float[,,] map = new float[res, res, layers];

        for (int y = 0; y < res; y++)
        {
            float uz = (float)y / (res - 1);
            for (int x = 0; x < res; x++)
            {
                float ux = (float)x / (res - 1);
                float height = td.GetInterpolatedHeight(ux, uz);
                float steep = td.GetSteepness(ux, uz);

                float sand = layers > 3 ? Mathf.Clamp01(Mathf.InverseLerp(waterLevel + sandBand, waterLevel, height)) : 0f;
                float rock = layers > 2 ? Mathf.Clamp01(Mathf.InverseLerp(rockAngle - 8f, rockAngle + 8f, steep)) : 0f;
                float road = (layers > 1 && roadEnabled) ? RoadMask(ux, uz) : 0f;

                float grass = Mathf.Clamp01(1f - sand - rock - road);
                float sum = grass + road + rock + sand + 1e-5f;

                map[y, x, 0] = grass / sum;
                if (layers > 1) map[y, x, 1] = road / sum;
                if (layers > 2) map[y, x, 2] = rock / sum;
                if (layers > 3) map[y, x, 3] = sand / sum;
            }
        }
        td.SetAlphamaps(0, 0, map);
    }

    // ------------------------------------------------------------------ лес
    void ScatterTrees(TerrainData td)
    {
        if (treePrefabs == null || treePrefabs.Length == 0) { td.treeInstances = new TreeInstance[0]; return; }

        var protos = new List<TreePrototype>();
        foreach (var p in treePrefabs)
            if (p != null) protos.Add(new TreePrototype { prefab = p });
        if (protos.Count == 0) { td.treeInstances = new TreeInstance[0]; return; }
        td.treePrototypes = protos.ToArray();

        var r = new System.Random(seed * 13 + 1);
        float Rf(float a, float b) => a + (float)r.NextDouble() * (b - a);
        var list = new List<TreeInstance>();
        int attempts = treeCount * 4;

        for (int i = 0; i < attempts && list.Count < treeCount; i++)
        {
            float ux = Rf(RoadCenter(0.5f) + roadWidth + roadShoulder + 0.02f, forestEastEdge);
            float uz = Rf(riverPosition + riverWidth + bankSoftness + 0.04f, 1f - shelfNorthWidth - shelfSlopeSoftness - 0.02f);

            if (RoadMask(ux, uz) > 0.05f) continue;
            float h = td.GetInterpolatedHeight(ux, uz);
            if (h < waterLevel + 0.8f) continue;
            if (td.GetSteepness(ux, uz) > 32f) continue;

            float s = Rf(treeScale.x, treeScale.y);
            list.Add(new TreeInstance
            {
                position = new Vector3(ux, h / td.size.y, uz),
                prototypeIndex = r.Next(protos.Count),
                widthScale = s,
                heightScale = s * Rf(0.9f, 1.15f),
                rotation = Rf(0f, Mathf.PI * 2f),
                color = Color.white,
                lightmapColor = Color.white
            });
        }
        td.treeInstances = list.ToArray();
    }

    // ------------------------------------------------------------------ вода
    void UpdateWaterPlane(TerrainData td)
    {
        const string name = "CatCooks_WaterPlane";
        Transform w = transform.Find(name);
        if (w == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            w = go.transform;
        }
        w.localPosition = new Vector3(td.size.x * 0.5f, waterLevel, td.size.z * 0.5f);
        w.localScale = new Vector3(td.size.x / 10f, 1f, td.size.z / 10f);
        var mr = w.GetComponent<MeshRenderer>();
        if (waterMaterial != null) mr.sharedMaterial = waterMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!autoUpdate || !isActiveAndEnabled) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && autoUpdate) Generate();
        };
    }
#endif
}
