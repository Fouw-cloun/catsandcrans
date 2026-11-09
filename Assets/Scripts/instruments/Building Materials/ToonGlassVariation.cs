using UnityEngine;

/// <summary>
/// РАЗБРОС ОКОН. Вешается на объект с окнами — на одно окно или на корень
/// фасада, тогда обработает всех потомков.
///
/// ═══ ЗАЧЕМ ═══
///
/// Процедурный блик в шейдере считается от UV, а UV у всех окон одинаковые.
/// Без сдвига фазы фасад из сотни окон выглядит копипастой: одна и та же
/// полоса в одном и том же месте сто раз подряд. Глаз ловит это мгновенно,
/// даже не понимая, что именно не так.
///
/// Скрипт раздаёт каждому окну свою фазу и лёгкий сдвиг оттенка.
///
/// ═══ MaterialPropertyBlock, А НЕ material ═══
///
/// Обращение к renderer.material создаёт КОПИЮ материала на каждое окно. Сто
/// окон — сто материалов, ни одного объединённого вызова отрисовки, и все сто
/// копий висят в памяти до конца сцены. На фасаде небоскрёба это разница между
/// одним DrawCall и сотней.
///
/// Блок подменяет значения без копирования: материал остаётся один, GPU
/// Instancing собирает окна в один вызов, а рисунок у каждого свой.
///
/// ═══ РАБОТАЕТ ОДИН РАЗ ═══
///
/// Разброс не меняется во времени, поэтому считается в Start и больше никогда.
/// Никакого Update — на фасаде это было бы сто пустых вызовов каждый кадр.
/// </summary>
[DisallowMultipleComponent]
public class ToonGlassVariation : MonoBehaviour
{
    [Header("Что обрабатывать")]
    [Tooltip("Обработать всех потомков, а не только этот объект. Включай, " +
             "если вешаешь на корень фасада.")]
    [SerializeField] private bool _includeChildren = true;

    [Header("Разброс")]
    [Tooltip("Разброс оттенка, доля. 0.06 — окна чуть отличаются, но фасад " +
             "остаётся единым. Выше 0.15 начинает выглядеть пёстрой мозаикой.")]
    [Range(0f, 0.3f)][SerializeField] private float _tintJitter = 0.06f;

    [Tooltip("Насколько окна отличаются яркостью. Имитирует разное освещение " +
             "внутри помещений.")]
    [Range(0f, 0.4f)][SerializeField] private float _brightnessJitter = 0.12f;

    [Tooltip("Зерно. Меняй, если конкретная раскладка не нравится.")]
    [SerializeField] private int _seed = 2024;

    [Header("Тёплые окна")]
    [Tooltip("Доля окон со светом внутри. Пара тёплых окон на холодном фасаде " +
             "оживляет его сильнее любого разброса оттенка.")]
    [Range(0f, 1f)][SerializeField] private float _litChance = 0.08f;

    [Tooltip("Цвет освещённого окна")]
    [SerializeField] private Color _litColor = new Color(1f, 0.86f, 0.6f);

    private static readonly int IdPhase = Shader.PropertyToID("_InstPhase");
    private static readonly int IdTint = Shader.PropertyToID("_InstTint");

    private void Start() => Apply();

    /// <summary>Разложить разброс. Публичный, чтобы можно было перезапустить
    /// из кнопки после правки полей.</summary>
    public void Apply()
    {
        Renderer[] targets = _includeChildren
            ? GetComponentsInChildren<Renderer>(true)
            : GetComponents<Renderer>();

        if (targets.Length == 0)
        {
            Debug.LogWarning($"[Окна] «{name}»: рендереров не найдено", this);
            return;
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        Random.State prev = Random.state;
        Random.InitState(_seed);

        int lit = 0;

        foreach (Renderer r in targets)
        {
            if (r == null) continue;

            // Читаем текущий блок, а не создаём чистый: на окне могли уже
            // что-то выставить, и затирать это молча нельзя.
            r.GetPropertyBlock(block);

            block.SetFloat(IdPhase, Random.Range(0f, 100f));

            Color tint = Color.white;

            if (Random.value < _litChance)
            {
                tint = _litColor;
                lit++;
            }
            else
            {
                // Сдвиг по всем каналам разный — иначе окна отличаются только
                // яркостью, а это читается как ошибка освещения, а не как
                // разные стёкла.
                tint = new Color(
                    1f + Random.Range(-_tintJitter, _tintJitter),
                    1f + Random.Range(-_tintJitter, _tintJitter) * 0.6f,
                    1f + Random.Range(-_tintJitter, _tintJitter),
                    1f);
            }

            float b = 1f + Random.Range(-_brightnessJitter, _brightnessJitter);
            tint.r *= b; tint.g *= b; tint.b *= b;

            block.SetColor(IdTint, tint);
            r.SetPropertyBlock(block);
        }

        Random.state = prev;

        Debug.Log($"[Окна] «{name}»: разбросано {targets.Length} окон, " +
                  $"из них светящихся {lit}", this);
    }

#if UNITY_EDITOR
    [ContextMenu("Разбросать заново")]
    private void EditorApply() => Apply();
#endif
}
