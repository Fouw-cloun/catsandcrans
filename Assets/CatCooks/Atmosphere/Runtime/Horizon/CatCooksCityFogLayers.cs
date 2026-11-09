using UnityEngine;

// ============================================================================
//  CatCooksCityFogLayers — слои тумана и облачные кольца ВНУТРИ города,
//  как в Shady Job: дальние кварталы не просто бледнеют, а скрываются в
//  дымке. Каждый слой — живое кольцо: пучится, шевелится, верх рваный.
//  Дёшево: одно кольцо = один меш, без частиц.
//  Цвет — из погоды (дымка у земли → дымка вдали), плотность — от тумана.
// ============================================================================
[AddComponentMenu("CatCooks/Атмосфера/City Fog Layers")]
public class CatCooksCityFogLayers : MonoBehaviour
{
    [System.Serializable]
    public class Layer
    {
        public string name = "Туман";
        public float radius = 500f;
        public float bottom = -20f;
        public float height = 140f;
        [Range(0f, 1f)] public float opacity = 0.6f;
        [Range(0f, 80f)] public float billow = 18f;
        [Range(0f, 1f)] public float puff = 0f;          // 0 — туман, 1 — облачные «кочаны»
        [Range(0f, 1f)] public float ragged = 0.6f;
        [Range(0f, 1f)] public float speed = 0.06f;
        [Tooltip("0 — цвет ближней дымки, 1 — дальней, 2 — облаков")]
        [Range(0f, 2f)] public float colorMode = 1f;
    }

    [Tooltip("Центр колец — по игроку (кольца едут за ним) или по этому объекту")]
    public bool followPlayer = false;
    [Tooltip("Множитель плотности от тумана погоды")]
    [Range(0f, 3f)] public float fogInfluence = 1.5f;
    public Layer[] layers =
    {
        new Layer { name = "Туман 1", radius = 420f,  height = 110f, opacity = 0.45f, billow = 14f, colorMode = 0.3f },
        new Layer { name = "Туман 2", radius = 700f,  height = 170f, opacity = 0.6f,  billow = 22f, colorMode = 0.7f },
        new Layer { name = "Туман 3", radius = 1000f, height = 240f, opacity = 0.75f, billow = 30f, colorMode = 1f },
        new Layer { name = "Облака",  radius = 1400f, bottom = 120f, height = 260f, opacity = 0.9f, billow = 45f, puff = 1f, ragged = 0.9f, colorMode = 2f },
    };

    Transform[] _rings; Material[] _mats;

    void OnEnable() { Rebuild(); }
    void OnDisable() { Clear(); }

    [ContextMenu("Пересобрать")]
    public void Rebuild()
    {
        Clear();
        var sh = Shader.Find("CatCooks/Fog Wall");
        if (sh == null) { Debug.LogWarning("[Туман города] нет шейдера CatCooks/Fog Wall"); return; }
        _rings = new Transform[layers.Length]; _mats = new Material[layers.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            var L = layers[i];
            var m = new Material(sh) { hideFlags = HideFlags.DontSave };
            m.renderQueue = 3000 - 60 - i;        // дальние — раньше
            _mats[i] = m;
            _rings[i] = CatCooksRingMesh.Create(transform, L.name, L.radius, L.height, 360f, 96, 8, m);
            _rings[i].localPosition = Vector3.up * L.bottom;
        }
    }

    void Clear()
    {
        if (_rings != null) foreach (var t in _rings) if (t != null) DestroyImmediate(t.gameObject);
        _rings = null; _mats = null;
    }

    void Update()
    {
        if (_rings == null) return;
        if (followPlayer && CatCooksWeatherState.TryGetPlayer(out Vector3 pl))
            transform.position = new Vector3(pl.x, transform.position.y, pl.z);
        float fog = Mathf.Clamp01(0.35f + CatCooksWeatherState.FogDensity * fogInfluence);
        float dust = CatCooksWeatherState.Dust;
        Color sand = new Color(0.9f, 0.7f, 0.48f);
        for (int i = 0; i < _rings.Length; i++)
        {
            var L = layers[i]; var m = _mats[i];
            if (m == null) continue;
            Color c0, c1;
            if (L.colorMode >= 1.5f) { c0 = CatCooksWeatherState.CloudShadow; c1 = CatCooksWeatherState.CloudLit; }
            else
            {
                c0 = Color.Lerp(CatCooksWeatherState.FogNear, CatCooksWeatherState.FogFar, L.colorMode);
                c1 = Color.Lerp(c0, CatCooksWeatherState.FogFar, 0.6f);
            }
            c0 = Color.Lerp(c0, sand, dust * 0.7f); c1 = Color.Lerp(c1, sand, dust * 0.5f);
            m.SetColor("_ColorBottom", c0); m.SetColor("_ColorTop", c1);
            m.SetFloat("_Opacity", L.opacity * (L.puff > 0.5f ? 1f : fog));
            m.SetFloat("_Billow", L.billow); m.SetFloat("_Puff", L.puff); m.SetFloat("_TopRagged", L.ragged);
            m.SetFloat("_Speed", L.speed * (1f + CatCooksWeatherState.Wind));
            m.SetFloat("_NoiseScale", Mathf.Max(2f, L.radius / 120f));
            m.SetFloat("_SoftFade", Mathf.Clamp(L.radius * 0.08f, 10f, 120f));
        }
    }
}
