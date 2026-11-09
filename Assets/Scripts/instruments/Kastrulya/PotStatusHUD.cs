using UnityEngine;

/// <summary>
/// ТАБЛИЧКА НАД ПОСУДОЙ: сколько налито, насколько горячо, что происходит.
/// Вешается на кастрюлю рядом с CookingVessel.
///
/// Рисуется через OnGUI в экранных координатах, а НЕ через мировой Canvas.
/// Причина простая: Canvas в мировом пространстве требует согласованных
/// настроек камеры, слоёв и масштаба, и когда что-то из этого не сходится, он
/// молча не показывается — искать причину тяжело. OnGUI рисуется всегда.
///
/// Чисто локальная штука: всё берётся из уже синхронизированных полей,
/// по сети не гоняется ничего.
/// </summary>
[RequireComponent(typeof(CookingVessel))]
public class PotStatusHUD : MonoBehaviour
{
    [Header("Размещение")]
    [Tooltip("На сколько поднять над центром посуды, метры")]
    [SerializeField] private float _height = 0.45f;

    [Tooltip("Дальше этого не показывать, метры")]
    [SerializeField] private float _maxDistance = 10f;

    [Tooltip("Ширина таблички, пиксели")]
    [SerializeField] private float _width = 190f;

    [Header("Что показывать")]
    [Tooltip("Показывать даже у пустой холодной посуды")]
    [SerializeField] private bool _alwaysShow;

    [Header("Цвета")]
    [SerializeField] private Color _waterColor = new Color(0.35f, 0.7f, 1f);
    [SerializeField] private Color _coldColor = new Color(0.45f, 0.75f, 1f);
    [SerializeField] private Color _hotColor = new Color(1f, 0.6f, 0.15f);
    [SerializeField] private Color _dangerColor = new Color(1f, 0.25f, 0.15f);

    private CookingVessel _vessel;
    private PotThermal _thermal;
    private CooperativeCarry _carry;

    private static Texture2D _white;
    private GUIStyle _box, _small;

    private void Awake()
    {
        _vessel = GetComponent<CookingVessel>();
        _thermal = GetComponent<PotThermal>();
        _carry = GetComponent<CooperativeCarry>();
    }

    /// <summary>Доля заполнения посуды, 0..1.</summary>
    public float Fill01
    {
        get
        {
            if (_vessel == null || _vessel.Def == null || _vessel.Def.CapacityG <= 0) return 0f;
            return Mathf.Clamp01(
                (float)(_vessel.Def.CapacityG - _vessel.FreeSpaceG) / _vessel.Def.CapacityG);
        }
    }

    private static Texture2D White
    {
        get
        {
            if (_white != null) return _white;
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            return _white;
        }
    }

    private void OnGUI()
    {
        if (_vessel == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        bool worth = _alwaysShow || !_vessel.IsEmpty ||
                     (_thermal != null && (_thermal.Temp01 > 0.02f || _thermal.IsRuptured));
        if (!worth) return;

        Vector3 world = transform.position + Vector3.up * _height;
        float dist = Vector3.Distance(cam.transform.position, world);
        if (dist > _maxDistance) return;

        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;   // за спиной

        EnsureStyles();

        // Ближе — крупнее, но в разумных пределах.
        float scale = Mathf.Clamp(3f / Mathf.Max(0.5f, dist), 0.6f, 1.4f);
        float w = _width * scale;
        float h = (_thermal != null ? 78f : 52f) * scale;

        float x = sp.x - w * 0.5f;
        float y = Screen.height - sp.y - h;

        GUI.Box(new Rect(x, y, w, h), GUIContent.none, _box);

        float pad = 8f * scale;
        float lineH = 16f * scale;
        float barH = 9f * scale;
        float cy = y + pad;

        // ── вода ──
        float fill = Fill01;
        Label(new Rect(x + pad, cy, w - pad * 2f, lineH),
              $"вода   {_vessel.MassKg:0.0} / {(_vessel.Def != null ? _vessel.Def.CapacityG / 1000f : 0f):0.0} кг",
              Color.white, scale);
        cy += lineH + 2f * scale;

        Bar(new Rect(x + pad, cy, w - pad * 2f, barH), fill,
            Color.Lerp(_waterColor * 0.6f, _waterColor, fill));
        cy += barH + 6f * scale;

        if (_thermal == null) return;

        // ── температура ──
        bool danger = _thermal.State == PotState.Overheat;
        bool broken = _thermal.IsRuptured;

        string right = broken ? "ПРОБИТА"
                     : danger ? "ПЕРЕГРЕВ!"
                     : _thermal.State == PotState.Boiling ? "кипит"
                     : "";

        Color textColor = broken ? Color.gray
                        : danger ? Blink(_dangerColor, Color.white)
                        : Color.white;

        Label(new Rect(x + pad, cy, w - pad * 2f, lineH),
              $"{_thermal.Temperature:0}°   {right}", textColor, scale);
        cy += lineH + 2f * scale;

        float t = Mathf.Clamp01(_thermal.Temp01);
        Color tc = Color.Lerp(_coldColor, _hotColor, t);
        if (danger) tc = Blink(tc, _dangerColor);
        if (broken) tc = Color.Lerp(tc, Color.gray, 0.6f);

        Bar(new Rect(x + pad, cy, w - pad * 2f, barH), t, tc);

        // ── подсказка о переносе ──
        if (_carry == null || !_carry.NeedsMore) return;

        Label(new Rect(x, y + h + 2f, w, lineH),
              $"нужно ещё {_carry.Required - _carry.Count}",
              new Color(1f, 0.85f, 0.3f), scale);
    }

    /// <summary>Мигание — так тревога заметна боковым зрением.</summary>
    private static Color Blink(Color a, Color b)
        => Color.Lerp(a, b, 0.5f + 0.5f * Mathf.Sin(Time.time * 12f));

    private void Bar(Rect r, float fill01, Color color)
    {
        Color prev = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(r, White);

        if (fill01 > 0.001f)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill01), r.height), White);
        }

        GUI.color = prev;
    }

    private void Label(Rect r, string text, Color color, float scale)
    {
        _small.fontSize = Mathf.RoundToInt(13f * scale);
        _small.normal.textColor = color;
        GUI.Label(r, text, _small);
    }

    private void EnsureStyles()
    {
        if (_box != null) return;

        _box = new GUIStyle(GUI.skin.box);
        _small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
    }
}
