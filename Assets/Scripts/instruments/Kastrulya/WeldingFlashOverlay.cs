using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ЗАСВЕТ ОТ СВАРОЧНОЙ ДУГИ. Вешается на префаб ИГРОКА, рядом с WeldingMask.
///
/// Варишь без маски — экран заливает белым тем сильнее, чем ближе дуга и чем
/// прямее ты на неё смотришь. Маска опущена — всё как обычно, только картинка
/// затемнена, как через настоящее стекло.
///
/// Засвет НЕ пропадает мгновенно: глаз так не работает. Он копится, пока
/// смотришь, и медленно спадает — поэтому долго варить без маски невыгодно,
/// даже если отворачиваться.
///
/// ВСЁ ЛОКАЛЬНО. Дуги ищутся через общий список активных горелок, по сети
/// ничего не гоняется: каждый видит свой засвет.
/// </summary>
public class WeldingFlashOverlay : MonoBehaviour
{
    [Header("Засвет")]
    [Tooltip("Как быстро копится ослепление, когда смотришь на дугу в упор")]
    [SerializeField] private float _buildRate = 2.2f;

    [Tooltip("Как быстро спадает, когда отвёл глаза")]
    [SerializeField] private float _fadeRate = 0.55f;

    [Tooltip("Дальность, с которой дуга ещё слепит, метры")]
    [SerializeField] private float _range = 14f;

    [Tooltip("Половина угла, в котором дуга слепит. Шире — слепит даже боковым " +
             "зрением.")]
    [Range(20f, 180f)][SerializeField] private float _angle = 110f;

    [Tooltip("Насколько маска гасит засвет. 1 = полностью защищает.")]
    [Range(0f, 1f)][SerializeField] private float _maskProtection = 0.97f;

    [Header("Вид")]
    [SerializeField] private Color _flashColor = new Color(0.85f, 0.92f, 1f);

    [Tooltip("Цвет затемнения через опущенную маску")]
    [SerializeField] private Color _maskColor = new Color(0.05f, 0.08f, 0.06f);

    /// <summary>Все горящие дуги на сцене. Статический список, потому что
    /// засвет считает каждый игрок сам, и искать горелки поиском по сцене
    /// каждый кадр было бы расточительно.</summary>
    private static readonly List<Transform> Arcs = new List<Transform>();

    public static void RegisterArc(Transform arc)
    {
        if (arc != null && !Arcs.Contains(arc)) Arcs.Add(arc);
    }

    public static void UnregisterArc(Transform arc) => Arcs.Remove(arc);

    private WeldingMask _mask;
    private Camera _cam;
    private float _flash;
    private static Texture2D _white;

    private void Awake() => _mask = GetComponent<WeldingMask>();

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

    private void Update()
    {
        if (_cam == null) _cam = GetComponentInChildren<Camera>(true) ?? Camera.main;
        if (_cam == null) return;

        float exposure = 0f;

        for (int i = Arcs.Count - 1; i >= 0; i--)
        {
            Transform arc = Arcs[i];
            if (arc == null) { Arcs.RemoveAt(i); continue; }

            Vector3 to = arc.position - _cam.transform.position;
            float dist = to.magnitude;
            if (dist > _range) continue;

            // Прямо в глаза — ярче всего, боковым зрением — слабее.
            float ang = Vector3.Angle(_cam.transform.forward, to);
            if (ang > _angle) continue;

            float byAngle = 1f - Mathf.Clamp01(ang / _angle);
            float byDist = 1f - Mathf.Clamp01(dist / _range);

            exposure = Mathf.Max(exposure, byAngle * byDist);
        }

        bool masked = _mask != null && _mask.IsDown;
        if (masked) exposure *= 1f - _maskProtection;

        // Копится быстро, спадает медленно — как настоящее ослепление.
        _flash = exposure > 0f
            ? Mathf.MoveTowards(_flash, 1f, _buildRate * exposure * Time.deltaTime)
            : Mathf.MoveTowards(_flash, 0f, _fadeRate * Time.deltaTime);
    }

    private void OnGUI()
    {
        bool masked = _mask != null && _mask.IsDown;

        // Затемнение от маски — постоянное, пока она опущена.
        if (masked && _mask.Tint > 0.001f)
        {
            Color prev = GUI.color;
            GUI.color = new Color(_maskColor.r, _maskColor.g, _maskColor.b, _mask.Tint);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), White);
            GUI.color = prev;
        }

        if (_flash <= 0.002f) return;

        // Засвет поверх всего. Нелинейно: первые проценты почти незаметны,
        // а на пике экран выбеливается целиком.
        Color c = _flashColor;
        c.a = Mathf.Clamp01(_flash * _flash * 1.15f);

        Color prevColor = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), White);
        GUI.color = prevColor;
    }
}
