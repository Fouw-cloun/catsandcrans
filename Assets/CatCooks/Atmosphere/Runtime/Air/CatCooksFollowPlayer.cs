using UnityEngine;

/// <summary>
/// Держит объект над игроком (главной камерой): зона листопада, пыли, дождя
/// всегда вокруг того, кто смотрит. Частицы в мировом пространстве, поэтому
/// уже летящие не дёргаются вместе с игроком.
/// </summary>
[AddComponentMenu("CatCooks/Атмосфера/Follow Player")]
public class CatCooksFollowPlayer : MonoBehaviour
{
    public Vector3 offset = new Vector3(0f, 4f, 0f);
    [Tooltip("Повторять только горизонталь (высота — своя)")]
    public bool horizontalOnly = false;

    void LateUpdate()
    {
        if (!CatCooksWeatherState.TryGetPlayer(out Vector3 p)) return;
        Vector3 t = p + offset;
        if (horizontalOnly) t.y = transform.position.y;
        transform.position = t;
    }
}
