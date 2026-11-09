using UnityEngine;

/// <summary>
/// ЧТО ПЫЛЕСОС МОЖЕТ ЗАСОСАТЬ. Вешается на предметы, которые должны втягиваться.
///
/// РАЗМЕР РЕШАЕТ ВСЁ:
///   Small — мелочь вроде кусочка картошки: втягивается внутрь и лежит в баке;
///   Large — крупное вроде целой картошки или игрока: присасывается к дулу и
///           висит на нём, внутрь не лезет.
///
/// Если компонента нет, предмет не всасывается совсем. Это намеренно: иначе
/// пылесос утягивал бы всю сцену, включая инструменты и посуду.
/// </summary>
public class Suckable : MonoBehaviour
{
    [Tooltip("Мелочь втягивается внутрь, крупное присасывается к дулу")]
    [SerializeField] private SuckableSize _size = SuckableSize.Small;

    [Tooltip("Во сколько раз сильнее или слабее тянется. Тяжёлый ящик — 0.5, " +
             "невесомый листок — 2.")]
    [Range(0.1f, 3f)][SerializeField] private float _dragFactor = 1f;

    [Tooltip("С какой силой вылетает при выстреле")]
    [SerializeField] private float _launchForce = 9f;

    public SuckableSize Size => _size;
    public float DragFactor => _dragFactor;
    public float LaunchForce => _launchForce;
}
