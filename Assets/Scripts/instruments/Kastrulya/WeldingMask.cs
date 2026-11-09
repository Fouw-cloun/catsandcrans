using UnityEngine;

/// <summary>
/// МАСКА СВАРЩИКА. Вешается на префаб ИГРОКА.
///
/// Пока модели маски нет, опускается и поднимается клавишей — временно, но
/// механику это уже даёт: варить без маски можно, только глаза выжжет.
///
/// Когда появится настоящая маска-предмет, ей останется дёргать ServerSetDown,
/// а весь остальной код менять не придётся.
///
/// Чисто локальная штука: ослепляет только того, кто смотрит, и на чужой
/// экран это не влияет.
/// </summary>
public class WeldingMask : MonoBehaviour
{
    [Tooltip("Опустить и поднять маску. Временно, пока нет модели.")]
    [SerializeField] private KeyCode _toggleKey = KeyCode.M;

    [Tooltip("Насколько маска темнит обычную картинку. Настоящая маска почти " +
             "чёрная, и без дуги в ней ничего не видно.")]
    [Range(0f, 0.95f)][SerializeField] private float _tint = 0.75f;

    [Tooltip("Опущена ли маска на старте")]
    [SerializeField] private bool _down;

    public bool IsDown => _down;
    public float Tint => _tint;

    private void Update()
    {
        if (!Input.GetKeyDown(_toggleKey)) return;
        _down = !_down;
    }

    /// <summary>Точка входа для будущей маски-предмета.</summary>
    public void SetDown(bool down) => _down = down;
}
