using UnityEngine;

/// <summary>
/// Ассет-«материал» верёвки: физика + вид. Create → Rope → Rope Material.
/// Кнопка "Fill From Type" в контекстном меню ассета заполнит дефолты под тип.
/// </summary>
[CreateAssetMenu(fileName = "RopeMaterial", menuName = "Rope/Rope Material")]
public class RopeMaterial : ScriptableObject
{
    public VerletRope.RopeType type = VerletRope.RopeType.Wire;

    [Header("Геометрия / толщина")]
    public float radius = 0.03f;
    [Range(3, 16)] public int radialSegments = 6;
    [Tooltip("Профиль толщины вдоль верёвки (0..1 длина → множитель).")]
    public AnimationCurve radiusProfile = AnimationCurve.Constant(0f, 1f, 1f);

    [Header("Физика")]
    [Range(0f, 4f)] public float gravityScale = 1f;
    [Range(1, 80)] public int stiffness = 30;
    [Range(0f, 1f)] public float bendStiffness = 0.25f;
    [Range(0f, 1f)] public float elasticity = 0.1f;
    [Range(1f, 3f)] public float stretchLimit = 1.5f;
    [Range(0.85f, 1f)] public float damping = 0.99f;
    [Range(0f, 1f)] public float surfaceFriction = 0.5f;
    [Range(0f, 1f)] public float bounciness = 0f;
    public bool collide = true;
    public bool preventTunneling = true;

    [Header("Цвет")]
    public Color baseColor = new Color(0.15f, 0.15f, 0.16f);
    public Color tipColor  = new Color(0.1f, 0.1f, 0.11f);
    [Range(0f, 1f)] public float gradient = 0f;

    [Header("Поверхность")]
    [Range(0f, 1f)] public float smoothness = 0.3f;
    [Range(0f, 1f)] public float lightWrap = 0.5f;

    [Header("Волокна")]
    [Range(0f, 1f)] public float fiberDetail = 0.4f;
    [Range(0f, 40f)] public float fiberTiling = 12f;
    [Range(0f, 20f)] public float fiberTwist = 4f;

    [Header("Ворс")]
    [Range(0f, 1f)] public float fuzz = 0.2f;
    public Color fuzzColor = new Color(0.6f, 0.6f, 0.6f);
    [Range(1f, 8f)] public float fuzzPower = 3f;

    [Header("Износ / грязь")]
    [Range(0f, 1f)] public float wear = 0f;
    public Color wearColor = new Color(0.05f, 0.04f, 0.03f);
    [Range(1f, 60f)] public float wearScale = 20f;

    [Header("Аутлайн")]
    public Color outlineColor = Color.black;
    [Range(0f, 0.05f)] public float outlineWidth = 0.008f;

    [ContextMenu("Fill From Type")]
    public void FillFromType()
    {
        radiusProfile = AnimationCurve.Constant(0f, 1f, 1f);
        gradient = 0f; wear = 0.1f; fiberTwist = 4f; fuzzPower = 3f; lightWrap = 0.5f;

        switch (type)
        {
            case VerletRope.RopeType.Wire:
                radius = 0.02f; radialSegments = 5;
                gravityScale = 0.7f; stiffness = 40; bendStiffness = 0.15f; elasticity = 0.08f;
                damping = 0.98f; surfaceFriction = 0.4f; bounciness = 0f;
                baseColor = new Color(0.1f,0.1f,0.11f); tipColor = new Color(0.2f,0.2f,0.2f);
                smoothness = 0.5f; fuzz = 0.05f; fiberDetail = 0.1f; fiberTiling = 6f; wear = 0.05f;
                break;
            case VerletRope.RopeType.Hose:
                radius = 0.05f; radialSegments = 8;
                gravityScale = 1.1f; stiffness = 35; bendStiffness = 0.45f; elasticity = 0.2f;
                damping = 0.98f; surfaceFriction = 0.6f; bounciness = 0.05f;
                baseColor = new Color(0.12f,0.13f,0.14f); tipColor = new Color(0.1f,0.1f,0.1f);
                smoothness = 0.55f; fuzz = 0.03f; fiberDetail = 0.05f; fiberTiling = 3f; wear = 0.08f;
                break;
            case VerletRope.RopeType.SteelCable:
                radius = 0.03f; radialSegments = 7;
                gravityScale = 1.4f; stiffness = 55; bendStiffness = 0.6f; elasticity = 0.02f;
                damping = 0.985f; surfaceFriction = 0.35f; bounciness = 0.02f;
                baseColor = new Color(0.4f,0.42f,0.45f); tipColor = new Color(0.5f,0.52f,0.55f);
                smoothness = 0.7f; fuzz = 0.02f; fiberDetail = 0.6f; fiberTiling = 20f; fiberTwist = 10f; wear = 0.15f;
                break;
            case VerletRope.RopeType.Chain:
                radius = 0.04f; radialSegments = 6;
                gravityScale = 1.7f; stiffness = 65; bendStiffness = 0.1f; elasticity = 0f;
                damping = 0.97f; surfaceFriction = 0.5f; bounciness = 0.08f;
                baseColor = new Color(0.3f,0.3f,0.32f); tipColor = new Color(0.35f,0.35f,0.37f);
                smoothness = 0.6f; fuzz = 0f; fiberDetail = 0.2f; fiberTiling = 8f; wear = 0.2f;
                break;
            case VerletRope.RopeType.ClimbingRope:
                radius = 0.045f; radialSegments = 8;
                gravityScale = 1f; stiffness = 30; bendStiffness = 0.2f; elasticity = 0.15f;
                damping = 0.99f; surfaceFriction = 0.7f; bounciness = 0f;
                baseColor = new Color(0.55f,0.45f,0.25f); tipColor = new Color(0.45f,0.35f,0.18f);
                smoothness = 0.15f; fuzz = 0.5f; fuzzColor = new Color(0.7f,0.6f,0.4f);
                fiberDetail = 0.7f; fiberTiling = 14f; fiberTwist = 8f; wear = 0.25f;
                break;
        }
    }
}
