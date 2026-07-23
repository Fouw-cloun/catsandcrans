using UnityEngine;

/// <summary>
/// Генерирует меш «короны» — открытую юбку-кольцо (расширяющуюся кверху трубу
/// без крышек) для шейдера Custom/WaterSplashCrownURP. Это «поднимающаяся вода»
/// вокруг точки удара струи.
///
/// UV.y: 0 низ (прибит к земле) → 1 верх (дышит и рвётся в шейдере).
///
/// ИСПОЛЬЗОВАНИЕ: пустой GameObject → этот компонент → материал
/// WaterSplashCrownURP → сохранить префабом → в поле Splash Effect у WaterJet
/// (WaterJet сам ставит его в точку удара и масштабирует от напора).
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SplashCrownMesh : MonoBehaviour
{
    [Tooltip("Радиус у земли")]
    [SerializeField] private float _bottomRadius = 0.35f;
    [Tooltip("Радиус наверху (шире = брызги разлетаются)")]
    [SerializeField] private float _topRadius = 0.7f;
    [Tooltip("Высота короны")]
    [SerializeField] private float _height = 0.5f;
    [Range(8, 48)]
    [SerializeField] private int _radialSegments = 24;
    [Range(1, 8)]
    [SerializeField] private int _heightSegments = 3;

    private void Awake() => Build();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) Build();
    }
#endif

    private void Build()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh mesh = new Mesh { name = "SplashCrown" };

        int vpr = _radialSegments + 1;
        int rows = _heightSegments + 1;
        Vector3[] verts = new Vector3[vpr * rows];
        Vector2[] uvs = new Vector2[verts.Length];
        int[] tris = new int[_radialSegments * _heightSegments * 6];

        for (int r = 0; r < rows; r++)
        {
            float v = (float)r / _heightSegments;
            float radius = Mathf.Lerp(_bottomRadius, _topRadius, v);
            float y = v * _height;
            for (int s = 0; s < vpr; s++)
            {
                float ang = (float)s / _radialSegments * Mathf.PI * 2f;
                int i = r * vpr + s;
                verts[i] = new Vector3(Mathf.Cos(ang) * radius, y, Mathf.Sin(ang) * radius);
                uvs[i] = new Vector2((float)s / _radialSegments, v);
            }
        }

        int ti = 0;
        for (int r = 0; r < _heightSegments; r++)
            for (int s = 0; s < _radialSegments; s++)
            {
                int a = r * vpr + s;
                int b = a + vpr;
                tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
            }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
