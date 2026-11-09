// ============================================================================
//  CatCooksShapeAnchor.cs
//  Вешается на РОДИТЕЛЬСКИЙ объект составной еды (картошка из кусочков,
//  башня из блинов, гора фарша и т.п.).
//
//  Что делает: каждому дочернему рендереру прописывает _ShapeCenter —
//  положение общего центра формы в ЕГО локальных координатах.
//  Шейдер после этого освещает всю сборку как один объём, и границы кусков
//  перестают ловить свет по-разному.
//
//  Кладётся в обычную папку (НЕ Editor), потому что нужен и в рантайме.
// ============================================================================
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Shape Anchor")]
public class CatCooksShapeAnchor : MonoBehaviour
{
    [Tooltip("Точка, от которой считается объём. Пусто — берётся центр общих границ всех кусков.")]
    public Transform CustomCenter;

    [Tooltip("Смещение центра относительно вычисленного, в мировых единицах.")]
    public Vector3 Offset = Vector3.zero;

    [Tooltip("Обновлять каждый кадр. Нужно только если куски двигаются друг относительно друга.")]
    public bool UpdateEveryFrame = false;

    [Tooltip("Показывать центр в сцене.")]
    public bool DrawGizmo = true;

    private static readonly int ShapeCenterId = Shader.PropertyToID("_ShapeCenter");
    private MaterialPropertyBlock _block;
    private Vector3 _centerWS;

    private void OnEnable() { Apply(); }
    private void OnValidate() { Apply(); }
    private void Update() { if (UpdateEveryFrame) Apply(); }

    [ContextMenu("Обновить центр")]
    public void Apply()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        if (CustomCenter != null)
        {
            _centerWS = CustomCenter.position + Offset;
        }
        else
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            _centerWS = b.center + Offset;
        }

        if (_block == null)
            _block = new MaterialPropertyBlock();

        foreach (var r in renderers)
        {
            Vector3 local = r.transform.InverseTransformPoint(_centerWS);

            r.GetPropertyBlock(_block);
            _block.SetVector(ShapeCenterId, new Vector4(local.x, local.y, local.z, 0f));
            r.SetPropertyBlock(_block);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!DrawGizmo)
            return;

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(_centerWS, 0.08f);
        Gizmos.DrawLine(_centerWS + Vector3.up * 0.15f, _centerWS - Vector3.up * 0.15f);
        Gizmos.DrawLine(_centerWS + Vector3.right * 0.15f, _centerWS - Vector3.right * 0.15f);
        Gizmos.DrawLine(_centerWS + Vector3.forward * 0.15f, _centerWS - Vector3.forward * 0.15f);
    }
}
