// =============================================================
//  FogCloudLayer.cs
//  Слой огромных туманных облаков (стиль PEAK) из fog cards.
//  Живой: генерит в редакторе при изменении полей, облака дрейфуют.
//  Density управляет и количеством видимых облаков, и их плотностью —
//  от редких гигантских клубов до плотно затянутого неба с просветами.
//
//  Установка:
//   1. Пустой GameObject -> компонент FogCloudLayer.
//   2. Cloud Material = материал со шейдером Custom/FogCloud.
//   3. В URP Asset ВКЛЮЧИ Depth Texture (для мягких пересечений с горами).
//
//  URP, Unity 2022.3+ / Unity 6.
// =============================================================
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class FogCloudLayer : MonoBehaviour
{
    [Header("Material (Custom/FogCloud)")]
    public Material cloudMaterial;

    [Header("Layer")]
    [Range(1, 60)] public int cloudCount = 16;
    public Vector2 area = new Vector2(400f, 400f);
    public float height = 110f;
    public float heightJitter = 30f;

    [Header("Card Size (огромные)")]
    public float minSize = 80f;
    public float maxSize = 180f;
    [Range(0.3f, 1f)] public float aspectY = 0.55f; // высота карты отн. ширины (плоские)

    [Header("Density (редкие клубы <-> затянутое небо)")]
    [Range(0, 1)] public float density = 0.5f;
    [Tooltip("Density также поднимает Coverage/Opacity в материале")]
    public bool driveMaterial = true;

    [Header("Drift (медленный = красиво)")]
    public Vector2 drift = new Vector2(1.2f, 0.4f);
    public bool wrapAround = true;

    [Header("Weather")]
    [Range(0, 1)] public float darkness = 0f;
    public Color cloudColor  = Color.white;
    public Color shadowTint  = new Color(0.75f, 0.80f, 0.90f);

    public int seed = 0;

    private readonly List<Transform> _cards = new List<Transform>();
    private readonly List<Vector3>   _basePos = new List<Vector3>();
    private Vector2 _driftOffset;
    private int _lastHash;
    private double _lastEditorTime;

    void OnEnable()
    {
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
        PushMaterial();
        int h = ShapeHash();
        if (h != _lastHash)
        {
#if UNITY_EDITOR
            EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
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

    void Tick(float dt)
    {
        if (_cards.Count == 0) return;
        dt = Mathf.Min(dt, 0.05f); // ограничиваем шаг -> нет рывков при неровном обновлении
        _driftOffset += drift * dt;
        for (int i = 0; i < _cards.Count; i++)
        {
            if (_cards[i] == null) continue;
            Vector3 p = _basePos[i];
            p.x += _driftOffset.x; p.z += _driftOffset.y;
            if (wrapAround)
            {
                p.x = Mathf.Repeat(p.x + area.x * 0.5f, area.x) - area.x * 0.5f;
                p.z = Mathf.Repeat(p.z + area.y * 0.5f, area.y) - area.y * 0.5f;
            }
            _cards[i].localPosition = p;
        }
    }

    void PushMaterial()
    {
        if (cloudMaterial == null) return;
        cloudMaterial.SetColor("_Color", cloudColor);
        cloudMaterial.SetColor("_ShadowColor", shadowTint);
        cloudMaterial.SetFloat("_CloudDarkness", darkness);
        if (driveMaterial)
        {
            // density -> плотнее узор и менее прозрачные облака
            cloudMaterial.SetFloat("_Coverage", Mathf.Lerp(0.35f, 0.85f, density));
            cloudMaterial.SetFloat("_Opacity",  Mathf.Lerp(0.25f, 0.65f, density));
        }
    }

    int ShapeHash()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + cloudCount + seed;
            h = h * 31 + Mathf.RoundToInt(area.x) + Mathf.RoundToInt(area.y);
            h = h * 31 + Mathf.RoundToInt(height) + Mathf.RoundToInt(heightJitter);
            h = h * 31 + Mathf.RoundToInt(minSize) + Mathf.RoundToInt(maxSize);
            h = h * 31 + Mathf.RoundToInt(aspectY * 100);
            h = h * 31 + Mathf.RoundToInt(density * 100);
            return h;
        }
    }

    void Rebuild()
    {
        Clear();
        _cards.Clear(); _basePos.Clear();
        _lastHash = ShapeHash();
        PushMaterial();

        Mesh quad = GetQuadMesh();
        Random.InitState(seed);

        // density управляет и числом видимых карт
        int visible = Mathf.Max(1, Mathf.RoundToInt(cloudCount * Mathf.Lerp(0.4f, 1f, density)));

        for (int i = 0; i < visible; i++)
        {
            var go = new GameObject($"FogCloud_{i}");
            go.transform.SetParent(transform, false);

            float x = Random.Range(-area.x * 0.5f, area.x * 0.5f);
            float z = Random.Range(-area.y * 0.5f, area.y * 0.5f);
            float y = height + Random.Range(-heightJitter, heightJitter);
            Vector3 pos = new Vector3(x, y, z);
            go.transform.localPosition = pos;

            float s = Random.Range(minSize, maxSize);
            float cardAspect = aspectY * Random.Range(0.8f, 1.2f);
            go.transform.localScale = new Vector3(s, s * cardAspect, 1f);

            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>();
            if (cloudMaterial) mr.sharedMaterial = cloudMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // per-card форма: свой узор, поворот, растяжка -> все разные
            var mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb);
            mpb.SetFloat("_CardSeed", Random.Range(0f, 100f));
            mpb.SetFloat("_CardRot", Random.Range(0f, 6.2831853f));
            mpb.SetVector("_CardStretch", new Vector4(
                Random.Range(0.75f, 1.4f), Random.Range(0.7f, 1.25f), 0, 0));
            mr.SetPropertyBlock(mpb);

            _cards.Add(go.transform);
            _basePos.Add(pos);
        }
        _driftOffset = Vector2.zero;
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
            if (t.name.StartsWith("FogCloud_")) kill.Add(t.gameObject);
        foreach (var g in kill) { if (Application.isPlaying) Destroy(g); else DestroyImmediate(g); }
    }
}
