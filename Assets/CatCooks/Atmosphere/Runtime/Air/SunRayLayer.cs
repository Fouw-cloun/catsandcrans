// =============================================================
//  SunRayLayer.cs
//  Слой солнечных лучей (beam cards, шейдер CatCooks/God Ray).
//  Лучи автоматически выровнены вдоль направления солнца и живут:
//  медленно дрейфуют и меняют яркость (как просветы в облаках).
//
//  Устроен в том же стиле, что FogCloudLayer: живой ребилд в
//  редакторе, per-card вариации через MaterialPropertyBlock.
//
//  Установка:
//   1. Пустой GameObject -> компонент SunRayLayer.
//   2. Ray Material = материал со шейдером CatCooks/God Ray.
//   3. Sun = Directional Light (тот же, которым рулит SkyController).
//   4. Управление яркостью: WorldDirector пишет intensityMultiplier
//      и tint сам, если указать ссылку. Вручную тоже можно.
//
//  Сеть: НЕ NetworkObject. Чистая локальная косметика.
//  URP, Unity 6.
// =============================================================
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class SunRayLayer : MonoBehaviour
{
    [Header("Material (CatCooks/God Ray)")]
    public Material rayMaterial;

    [Header("Sun")]
    public Light sun;

    [Header("Layout")]
    [Range(1, 40)] public int rayCount = 10;
    [Tooltip("Зона, над которой висят лучи (обычно = игровая зона)")]
    public Vector2 area = new Vector2(220f, 220f);
    [Tooltip("Высота верхушек лучей (чуть НИЖЕ слоя облаков!)")]
    public float topHeight = 95f;
    public float topHeightJitter = 15f;

    [Header("Ray Size")]
    public Vector2 widthRange = new Vector2(6f, 18f);
    public Vector2 lengthRange = new Vector2(70f, 130f);

    [Header("Live")]
    [Tooltip("Медленный дрейф лучей (просветы в облаках плывут)")]
    public Vector2 drift = new Vector2(0.8f, 0.3f);
    public bool wrapAround = true;
    [Tooltip("Скорость «дыхания» яркости каждого луча")]
    public float breatheSpeed = 0.25f;
    [Tooltip("Глубина дыхания: 0 = ровные, 1 = лучи появляются и исчезают")]
    [Range(0f, 1f)] public float breatheAmount = 0.7f;

    [Header("Driven by WorldDirector (или вручную)")]
    public float intensityMultiplier = 1f;
    public Color tint = Color.white;

    public int seed = 0;

    private readonly List<MeshRenderer> _cards = new List<MeshRenderer>();
    private readonly List<Vector3> _basePos = new List<Vector3>();
    private readonly List<float> _seeds = new List<float>();
    private Vector2 _driftOffset;
    private int _lastHash;
    private double _lastEditorTime;
    private MaterialPropertyBlock _mpb;

    // ---------------------------------------------------------------- lifecycle
    void OnEnable()
    {
        _mpb = new MaterialPropertyBlock();
        Rebuild();
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
    }

    void OnValidate()
    {
        // LightCraft uses a disabled SunRayLayer as weather data only.
        // OnValidate can run for disabled behaviours during domain reload.
        if (!isActiveAndEnabled) return;
        int h = ShapeHash();
        if (h != _lastHash)
        {
#if UNITY_EDITOR
            EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) Rebuild(); };
#else
            Rebuild();
#endif
        }
    }

    void Update() { if (Application.isPlaying) Tick(Time.deltaTime); }

#if UNITY_EDITOR
    void EditorTick()
    {
        if (Application.isPlaying || this == null) return;
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastEditorTime);
        _lastEditorTime = now;
        if (dt > 0f && dt < 0.5f) { Tick(dt); SceneView.RepaintAll(); }
    }
#endif

    // ---------------------------------------------------------------- tick
    void Tick(float dt)
    {
        dt = Mathf.Min(dt, 0.05f);
        _driftOffset += drift * dt;

        // глобальная яркость и оттенок — для всех лучей разом
        Shader.SetGlobalFloat("_CatRayIntensity", Mathf.Max(intensityMultiplier, 0f));
        Shader.SetGlobalColor("_CatRayTint", tint);

        Quaternion rayRot = sun != null
            ? Quaternion.FromToRotation(Vector3.up, -sun.transform.forward)
            : Quaternion.identity;

        float time = Application.isPlaying ? Time.time : (float)
#if UNITY_EDITOR
            EditorApplication.timeSinceStartup;
#else
            0.0;
#endif

        for (int i = 0; i < _cards.Count; i++)
        {
            if (_cards[i] == null) continue;
            var tr = _cards[i].transform;

            Vector3 p = _basePos[i];
            p.x += _driftOffset.x; p.z += _driftOffset.y;
            if (wrapAround)
            {
                p.x = Mathf.Repeat(p.x + area.x * 0.5f, area.x) - area.x * 0.5f;
                p.z = Mathf.Repeat(p.z + area.y * 0.5f, area.y) - area.y * 0.5f;
            }

            // верхушка луча на topHeight, тело уходит вниз вдоль солнца
            float halfLen = tr.localScale.y * 0.5f;
            Vector3 topPos = new Vector3(p.x, p.y, p.z);
            Vector3 center = topPos + (rayRot * Vector3.down) * halfLen;
            tr.localPosition = center;
            tr.localRotation = rayRot;

            // «дыхание»: каждый луч плавно разгорается и гаснет со своей фазой
            float s = _seeds[i];
            float breathe = Mathf.PerlinNoise(time * breatheSpeed + s, s * 1.7f);
            float vis = Mathf.Lerp(1f, Mathf.SmoothStep(0f, 1f, breathe), breatheAmount);

            _cards[i].GetPropertyBlock(_mpb);
            _mpb.SetFloat("_RaySeed", s);
            _mpb.SetFloat("_Intensity", vis); // перекрывает материал per-card
            _cards[i].SetPropertyBlock(_mpb);
        }
    }

    // ---------------------------------------------------------------- rebuild
    int ShapeHash()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + rayCount + seed;
            h = h * 31 + Mathf.RoundToInt(area.x) + Mathf.RoundToInt(area.y);
            h = h * 31 + Mathf.RoundToInt(topHeight) + Mathf.RoundToInt(topHeightJitter);
            h = h * 31 + Mathf.RoundToInt(widthRange.x + widthRange.y);
            h = h * 31 + Mathf.RoundToInt(lengthRange.x + lengthRange.y);
            return h;
        }
    }

    void Rebuild()
    {
        if (!isActiveAndEnabled) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        Clear();
        _cards.Clear(); _basePos.Clear(); _seeds.Clear();
        _lastHash = ShapeHash();

        Mesh quad = GetQuadMesh();
        Random.InitState(seed + 101);

        for (int i = 0; i < rayCount; i++)
        {
            var go = new GameObject($"SunRay_{i}");
            go.transform.SetParent(transform, false);

            float x = Random.Range(-area.x * 0.5f, area.x * 0.5f);
            float z = Random.Range(-area.y * 0.5f, area.y * 0.5f);
            float y = topHeight + Random.Range(-topHeightJitter, topHeightJitter);

            float w = Random.Range(widthRange.x, widthRange.y);
            float l = Random.Range(lengthRange.x, lengthRange.y);
            go.transform.localScale = new Vector3(w, l, 1f);

            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>();
            if (rayMaterial) mr.sharedMaterial = rayMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _cards.Add(mr);
            _basePos.Add(new Vector3(x, y, z));
            _seeds.Add(Random.Range(0f, 100f));
        }
        _driftOffset = Vector2.zero;
        Tick(0f); // сразу выставить позиции/повороты
    }

    private static Mesh _quadMesh;
    static Mesh GetQuadMesh()
    {
        if (_quadMesh == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quadMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp);
        }
        return _quadMesh;
    }

    void Clear()
    {
        var kill = new List<GameObject>();
        foreach (Transform t in transform)
            if (t.name.StartsWith("SunRay_")) kill.Add(t.gameObject);
        foreach (var g in kill) { if (Application.isPlaying) Destroy(g); else DestroyImmediate(g); }
    }
}
