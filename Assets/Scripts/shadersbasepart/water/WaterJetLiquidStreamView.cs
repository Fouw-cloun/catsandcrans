using CatCooks.Food;
using UnityEngine;

/// <summary>
/// КРАСИВАЯ СТРУЯ ПОВЕРХ ФИЗИКИ ГИДРАНТА — теперь на новой системе струй (Струи v1).
///
/// WaterJet остаётся главным: напор от гидранта, толкание предметов, лужи.
/// Этот компонент рисует поверх него мультяшную струю LiquidStreamRibbon
/// (та же, что у вёдер и бутылок): прожилки, пена у среза, корона и пенное
/// пятно в месте падения. Старый меш WaterJet прячется.
///
/// Старая версия работала на CCLiquidStream (пак CatCooks/LiquidStreams),
/// пак удалён — ссылки на него больше не нужны.
///
/// Сеть: ничего не шлёт. Напор синхронизирует гидрант, струю каждый рисует сам.
///
/// ═══ КАК ВЕШАТЬ ═══
/// На тот же объект, где WaterJet (синяя ось +Z смотрит из ствола). Всё.
/// </summary>
[DefaultExecutionOrder(50)]           // после WaterJet: берём уже посчитанный напор
public sealed class WaterJetLiquidStreamView : MonoBehaviour
{
    [SerializeField] private WaterJet _jet;

    [Header("Струя")]
    [Tooltip("Цвет воды. Характер струи (пена, прожилки, брызги) подбирается по цвету сам.")]
    [SerializeField] private Color _color = new Color(0.35f, 0.75f, 0.85f);
    [Tooltip("Скорость вылета при полном и минимальном напоре (как Max/Min Launch Speed у WaterJet)")]
    [SerializeField] private float _speedAtFull = 14f;
    [SerializeField] private float _speedAtMin = 3f;
    [Tooltip("Гравитация WaterJet (его поле Gravity). Скорость пересчитывается, чтобы дуга совпала по дальности")]
    [SerializeField] private float _jetGravity = 14f;
    [Tooltip("Толщина струи у ствола при полном напоре, м")]
    [SerializeField] private float _widthAtFull = 0.2f;
    [Tooltip("Ниже этого напора струя гаснет")]
    [Range(0f, 0.5f)][SerializeField] private float _cutoff = 0.03f;
    [SerializeField] private float _splashPerSecond = 90f;

    [Header("Старый вид")]
    [Tooltip("Прятать меш старой струи WaterJet")]
    [SerializeField] private bool _hideOldMesh = true;
    [Tooltip("Прятать и «юбки» старой струи (WaterJetSkirts), если есть")]
    [SerializeField] private bool _hideOldSkirts = true;

    private LiquidStreamRibbon _ribbon;
    private MeshRenderer _oldRenderer;

    private void Awake()
    {
        if (_jet == null) _jet = GetComponentInChildren<WaterJet>(true);
        if (_jet == null)
        {
            Debug.LogError($"[Струя] «{name}»: нужен WaterJet на объекте или в детях", this);
            enabled = false;
            return;
        }
        _oldRenderer = _jet.GetComponent<MeshRenderer>();
        if (_hideOldSkirts)
            foreach (WaterJetSkirts s in GetComponentsInChildren<WaterJetSkirts>(true)) s.enabled = false;
    }

    private void Update()
    {
        if (_ribbon == null) _ribbon = LiquidStreamRibbon.Create(transform);

        float flow = _jet.IsFlowing ? _jet.Flow01 : 0f;
        bool on = flow > _cutoff;

        Vector3[] pts = _jet.PathPointsWorld;
        int n = _jet.PathCount;
        Vector3 start = _jet.transform.position;
        Vector3 dir = _jet.transform.forward;
        if (pts != null && n >= 2 && pts.Length >= 2)
        {
            start = pts[0];
            Vector3 d = pts[1] - pts[0];
            if (d.sqrMagnitude > 1e-6f) dir = d.normalized;
        }

        // У WaterJet своя гравитация. Подгоняем скорость, чтобы дальность дуги совпала:
        // дальность ~ v²/g, значит v_новая = v * sqrt(g_мира / g_струи).
        float speed = Mathf.Lerp(_speedAtMin, _speedAtFull, flow);
        float g = Mathf.Abs(Physics.gravity.y);
        if (_jetGravity > 0.01f && g > 0.01f) speed *= Mathf.Sqrt(g / _jetGravity);

        _ribbon.Tick(on, start, dir * speed, _widthAtFull * Mathf.Lerp(0.4f, 1f, flow), _color,
                     _jet.transform, _splashPerSecond * flow);
    }

    // WaterJet сам включает свой MeshRenderer каждый кадр в Update.
    // Гасим ПОСЛЕ него — рисование идёт после LateUpdate.
    private void LateUpdate()
    {
        if (_hideOldMesh && _oldRenderer != null && _oldRenderer.enabled) _oldRenderer.enabled = false;
    }

    private void OnDisable()
    {
        if (_ribbon != null && _jet != null)
            _ribbon.Tick(false, _jet.transform.position, Vector3.zero, 0f, _color, _jet.transform, 0f);
    }
}
