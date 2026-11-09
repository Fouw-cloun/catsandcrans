using UnityEngine;

/// <summary>
/// Применяет RopeMaterial к верёвке: физика → VerletRope, толщина/профиль → RopeMeshRenderer,
/// вид → материал через MaterialPropertyBlock. Работает в редакторе ([ExecuteAlways]) —
/// меняешь ассет и сразу видишь. Кнопка "Apply Material" — в кастомном инспекторе (папка Editor).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(VerletRope))]
public class RopeStyle : MonoBehaviour
{
    public RopeMaterial material;
    [Tooltip("Применять автоматически при изменении в редакторе.")]
    public bool autoApplyInEditor = true;

    static readonly int P_Base=Shader.PropertyToID("_BaseColor"), P_Tip=Shader.PropertyToID("_TipColor"),
        P_Grad=Shader.PropertyToID("_Gradient"), P_Smooth=Shader.PropertyToID("_Smoothness"),
        P_Wrap=Shader.PropertyToID("_Wrap"), P_Fiber=Shader.PropertyToID("_Fiber"),
        P_FTile=Shader.PropertyToID("_FiberTiling"), P_FTwist=Shader.PropertyToID("_FiberTwist"),
        P_Fuzz=Shader.PropertyToID("_Fuzz"), P_FuzzCol=Shader.PropertyToID("_FuzzColor"),
        P_FuzzPow=Shader.PropertyToID("_FuzzPower"), P_Wear=Shader.PropertyToID("_Wear"),
        P_WearCol=Shader.PropertyToID("_WearColor"), P_WearScale=Shader.PropertyToID("_WearScale"),
        P_OutCol=Shader.PropertyToID("_OutlineColor"), P_OutW=Shader.PropertyToID("_OutlineWidth");

    MaterialPropertyBlock _mpb;

    void OnEnable() => Apply();

    [ContextMenu("Apply Material")]
    public void Apply()
    {
        if (material == null) return;
        var rope = GetComponent<VerletRope>();

        // физика
        rope.gravityScale=material.gravityScale; rope.stiffness=material.stiffness;
        rope.bendStiffness=material.bendStiffness; rope.elasticity=material.elasticity;
        rope.stretchLimit=material.stretchLimit; rope.damping=material.damping;
        rope.surfaceFriction=material.surfaceFriction; rope.bounciness=material.bounciness;
        rope.collide=material.collide; rope.preventTunneling=material.preventTunneling;
        rope.ropeRadius=material.radius;

        // меш / толщина
        var mesh = GetComponent<RopeMeshRenderer>();
        if (mesh != null)
        {
            mesh.radius = material.radius;
            mesh.radialSegments = material.radialSegments;
            if (material.radiusProfile != null && material.radiusProfile.length > 0)
                mesh.radiusProfile = material.radiusProfile;
        }

        // вид
        var rend = GetComponent<Renderer>();
        if (rend != null)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(_mpb);
            _mpb.SetColor(P_Base, material.baseColor);
            _mpb.SetColor(P_Tip, material.tipColor);
            _mpb.SetFloat(P_Grad, material.gradient);
            _mpb.SetFloat(P_Smooth, material.smoothness);
            _mpb.SetFloat(P_Wrap, material.lightWrap);
            _mpb.SetFloat(P_Fiber, material.fiberDetail);
            _mpb.SetFloat(P_FTile, material.fiberTiling);
            _mpb.SetFloat(P_FTwist, material.fiberTwist);
            _mpb.SetFloat(P_Fuzz, material.fuzz);
            _mpb.SetColor(P_FuzzCol, material.fuzzColor);
            _mpb.SetFloat(P_FuzzPow, material.fuzzPower);
            _mpb.SetFloat(P_Wear, material.wear);
            _mpb.SetColor(P_WearCol, material.wearColor);
            _mpb.SetFloat(P_WearScale, material.wearScale);
            _mpb.SetColor(P_OutCol, material.outlineColor);
            _mpb.SetFloat(P_OutW, material.outlineWidth);
            rend.SetPropertyBlock(_mpb);
        }

        if (!Application.isPlaying) rope.Build();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (autoApplyInEditorSafe())
            UnityEditor.EditorApplication.delayCall += () => { if (this) Apply(); };
    }
    bool autoApplyInEditorSafe() => autoApplyInEditor && material != null && !Application.isPlaying;
#endif
}
