using System.Collections.Generic;
using System.Text;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// СВОДНЫЙ ОТЛАДОЧНЫЙ ОВЕРЛЕЙ. Один компонент на всю систему: костёр, подставка,
/// котёл, меха, тачка, игрок.
///
/// Вешается на ЛЮБОЙ объект в сцене — например на пустышку «__Debug». Ничего не
/// требует в полях: всё находит само.
///
/// ═══ ПОЧЕМУ ОДИН, А НЕ ПО HUD НА КАЖДЫЙ СКРИПТ ═══
///
/// У Firebox уже был свой Debug Hud, и по мере роста системы их набралось бы
/// с десяток — все рисуют в разных углах, перекрывают друг друга, каждый
/// включается своей галкой в своём инспекторе. Найти нужный при отладке
/// становится отдельной задачей.
///
/// Здесь всё в одном месте, с общим форматом, разделами по F-клавишам и
/// возможностью выключить целиком одной кнопкой.
///
/// ═══ ЧТО ЭТО НЕ ДЕЛАЕТ ═══
///
/// Не правит состояние само. Читы вызывают ПУБЛИЧНЫЕ серверные методы систем
/// (DebugFill, DebugIgnite, ServerStumble) — то есть ровно то же, что вызвал бы
/// игрок своими действиями. Если чит сработал, а игра нет — значит проблема не
/// в отладке.
///
/// ═══ ГОРЯЧИЕ КЛАВИШИ ═══
///
///   F1  — показать / скрыть оверлей
///   F2  — переключить раздел
///
///   ЧИТЫ (только на сервере/хосте, и только по ближайшему объекту):
///   F5  — насыпать полную закладку в ближайший костёр
///   F6  — поджечь ближайший костёр
///   F7  — потушить и вычистить ближайший костёр
///   F8  — поддать воздуха (как один сильный прыжок)
///   F9  — тряхнуть тачку (проверка «споткнулся»)
/// </summary>
public class CatCooksDebugHUD : MonoBehaviour
{
    private enum Section
    {
        All = 0,
        Fire = 1,
        Cart = 2,
        Player = 3
    }

    [Header("Показ")]
    [SerializeField] private bool _visible = true;
    [SerializeField] private Section _section = Section.All;

    [Tooltip("Клавиша показать/скрыть")]
    [SerializeField] private KeyCode _toggleKey = KeyCode.F1;

    [Tooltip("Клавиша переключения раздела")]
    [SerializeField] private KeyCode _sectionKey = KeyCode.F2;

    [Header("Читы")]
    [Tooltip("Разрешить горячие клавиши F5–F9. В сборке для игроков выключи.")]
    [SerializeField] private bool _cheatsEnabled = true;

    [Tooltip("В каком радиусе от игрока искать объект для чита, метры")]
    [SerializeField] private float _cheatRadius = 25f;

    [Header("Вид")]
    [SerializeField] private int _fontSize = 13;
    [SerializeField] private Vector2 _origin = new Vector2(12f, 12f);
    [SerializeField] private float _width = 560f;

    // Пересобирать списки каждый кадр — дорого и незачем: объекты в сцене
    // появляются редко. Обновляем раз в полсекунды.
    private const float RescanInterval = 0.5f;

    private readonly List<Firebox> _fires = new List<Firebox>();
    private readonly List<FireCauldronStand> _stands = new List<FireCauldronStand>();
    private readonly List<SimpleTireBounce> _bellows = new List<SimpleTireBounce>();
    private readonly List<PushCart> _carts = new List<PushCart>();

    private FirstPersonController _localPlayer;
    private float _nextRescan;
    private GUIStyle _style;
    private readonly StringBuilder _sb = new StringBuilder(2048);

    private void Update()
    {
        if (Input.GetKeyDown(_toggleKey)) _visible = !_visible;

        if (Input.GetKeyDown(_sectionKey))
            _section = (Section)(((int)_section + 1) % 4);

        if (Time.time >= _nextRescan)
        {
            _nextRescan = Time.time + RescanInterval;
            Rescan();
        }

        if (_cheatsEnabled) HandleCheats();
    }

    /// <summary>
    /// FindObjectsByType с SortMode.None — сортировка нам не нужна, а она
    /// заметно дороже самого поиска. И только раз в полсекунды: сцена не
    /// меняется каждый кадр.
    /// </summary>
    private void Rescan()
    {
        _fires.Clear();
        _fires.AddRange(Object.FindObjectsByType<Firebox>(FindObjectsSortMode.None));

        _stands.Clear();
        _stands.AddRange(Object.FindObjectsByType<FireCauldronStand>(FindObjectsSortMode.None));

        _bellows.Clear();
        _bellows.AddRange(Object.FindObjectsByType<SimpleTireBounce>(FindObjectsSortMode.None));

        _carts.Clear();
        _carts.AddRange(Object.FindObjectsByType<PushCart>(FindObjectsSortMode.None));

        // Свой игрок — тот, у кого IsOwner. Ищем каждый раз, потому что при
        // респавне или смене сцены объект будет уже другой.
        if (_localPlayer == null || !_localPlayer.IsOwner)
        {
            _localPlayer = null;
            foreach (FirstPersonController p in
                     Object.FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
            {
                if (!p.IsOwner) continue;
                _localPlayer = p;
                break;
            }
        }
    }

    // ───────────────────────── ЧИТЫ ─────────────────────────

    private void HandleCheats()
    {
        bool fill = Input.GetKeyDown(KeyCode.F5);
        bool ignite = Input.GetKeyDown(KeyCode.F6);
        bool douse = Input.GetKeyDown(KeyCode.F7);
        bool air = Input.GetKeyDown(KeyCode.F8);
        bool stumble = Input.GetKeyDown(KeyCode.F9);

        if (!fill && !ignite && !douse && !air && !stumble) return;

        if (fill || ignite || douse || air)
        {
            Firebox f = Nearest(_fires);
            if (f == null)
            {
                Debug.LogWarning("[Отладка] костра поблизости нет");
                return;
            }

            // Состояние костра живёт на сервере. С чистого клиента менять его
            // бессмысленно — синхронизация тут же вернёт настоящее значение,
            // и выглядело бы это как «чит не работает».
            if (!f.IsServerInitialized)
            {
                Debug.LogWarning("[Отладка] читы костра работают только на хосте/сервере");
                return;
            }

            if (fill) f.DebugFill(4f, 12f, 40f);
            if (ignite) f.DebugIgnite();
            if (douse) f.DebugExtinguish();
            if (air) f.ServerAddAir(1f);
        }

        if (stumble)
        {
            PushCart c = Nearest(_carts);
            if (c == null) { Debug.LogWarning("[Отладка] тачки поблизости нет"); return; }
            if (!c.IsServerInitialized)
            {
                Debug.LogWarning("[Отладка] тряска тачки работает только на хосте/сервере");
                return;
            }
            c.ServerStumble(1f);
        }
    }

    /// <summary>Ближайший к игроку объект в радиусе. Читы всегда бьют по одному
    /// объекту — иначе на площадке с тремя кострами непонятно, какой именно
    /// подожгли.</summary>
    private T Nearest<T>(List<T> list) where T : Component
    {
        if (_localPlayer == null || list.Count == 0) return null;

        Vector3 from = _localPlayer.transform.position;
        T best = null;
        float bestDist = _cheatRadius * _cheatRadius;

        foreach (T item in list)
        {
            if (item == null) continue;
            float d = (item.transform.position - from).sqrMagnitude;
            if (d > bestDist) continue;
            bestDist = d;
            best = item;
        }
        return best;
    }

    // ───────────────────────── ОТРИСОВКА ─────────────────────────

    private void OnGUI()
    {
        if (!_visible) return;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                richText = true,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false
            };
        }

        _sb.Length = 0;

        _sb.Append($"<b>CAT COOKS — ОТЛАДКА</b>   [{_toggleKey}] скрыть   " +
                   $"[{_sectionKey}] раздел: <b>{_section}</b>\n");

        if (_cheatsEnabled)
            _sb.Append("<color=#888888>F5 насыпать · F6 поджечь · F7 потушить · " +
                       "F8 воздух · F9 тряхнуть тачку</color>\n");

        _sb.Append('\n');

        if (_section == Section.All || _section == Section.Fire) AppendFire();
        if (_section == Section.All || _section == Section.Cart) AppendCart();
        if (_section == Section.All || _section == Section.Player) AppendPlayer();

        // Фон под текстом — без него на светлой траве не читается вообще.
        float h = _style.CalcHeight(new GUIContent(_sb.ToString()), _width) + 12f;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(_origin.x - 6f, _origin.y - 6f, _width + 12f, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(_origin.x, _origin.y, _width, h), _sb.ToString(), _style);
    }

    private void AppendFire()
    {
        _sb.Append("<b>── КОСТЁР ──</b>\n");

        if (_fires.Count == 0) { _sb.Append("  нет в сцене\n\n"); return; }

        foreach (Firebox f in _fires)
        {
            if (f == null) continue;

            string state = f.State switch
            {
                FireState.Cold => "<color=#8899AA>ХОЛОДНЫЙ</color>",
                FireState.Catching => "<color=#FFCC44>ЗАНИМАЕТСЯ</color>",
                FireState.Burning => "<color=#FF7722>ГОРИТ</color>",
                _ => "<color=#FF4422>УГЛИ</color>"
            };

            _sb.Append($"  <b>{f.name}</b>  {state}   мощность {f.Intensity01 * 100f:0}%   " +
                       $"{f.DegreesPerSecond:0.0} °C/сек\n");

            // Требования подсвечиваем: сразу видно, чего не хватает, без чтения
            // строки статуса целиком.
            _sb.Append($"    растопка {Req(f.TinderKg, f.HasTinder)}   " +
                       $"хворост {Req(f.KindlingKg, f.HasKindling)}   " +
                       $"дрова {Req(f.WoodKg, f.HasWood)}   " +
                       $"уголь {f.CoalKg:0.0}\n");

            _sb.Append($"    тяга {f.Draft01 * 100f:0}%   " +
                       $"разгораемость {f.Ignitability01:0.00}   " +
                       $"закладка {f.Layup01 * 100f:0}%\n");

            if (!f.LayupReady)
                _sb.Append($"    <color=#FFAA44>{f.LayupStatus}</color>\n");
        }

        _sb.Append('\n');

        // ── подставка и котёл ──
        _sb.Append("<b>── ПОДСТАВКА ──</b>\n");
        if (_stands.Count == 0) _sb.Append("  нет в сцене\n");

        foreach (FireCauldronStand s in _stands)
        {
            if (s == null) continue;

            if (s.Vessel == null)
            {
                _sb.Append($"  <b>{s.name}</b>  <color=#8899AA>котла нет</color>\n");
                continue;
            }

            PotThermal t = s.Vessel.GetComponent<PotThermal>();
            if (t == null)
            {
                _sb.Append($"  <b>{s.name}</b>  котёл «{s.Vessel.name}», " +
                           "<color=#FF6666>нет PotThermal</color>\n");
                continue;
            }

            string potState = t.IsRuptured
                ? "<color=#FF4444>РАЗОРВАН</color>"
                : t.State.ToString();

            _sb.Append($"  <b>{s.name}</b>  котёл «{s.Vessel.name}»  {potState}   " +
                       $"<color=#FFDD66>{t.Temperature:0.0} °C</color>   " +
                       $"давление {t.Pressure01 * 100f:0}%\n");
        }

        _sb.Append('\n');

        // ── колёса-меха ──
        _sb.Append("<b>── КОЛЕСО ──</b>\n");
        if (_bellows.Count == 0) _sb.Append("  нет в сцене\n");

        foreach (SimpleTireBounce b in _bellows)
        {
            if (b == null) continue;

            // Нулевое число входов при попытках прыгать — самый частый случай:
            // зона не ловит игрока. Подсвечиваем, чтобы это бросалось в глаза,
            // а не терялось среди прочих цифр.
            string enters = b.EnterCount == 0
                ? "<color=#FF8866>0</color>"
                : b.EnterCount.ToString();

            _sb.Append($"  <b>{b.name}</b>  сжатие {b.Squash01 * 100f:0}%   " +
                       $"в зоне {b.InsideCount}\n");

            _sb.Append($"    входов {enters}   подбросов {b.BounceCount}   " +
                       $"залпов {b.WindBlasts}   сдуто {b.WindItems}   " +
                       $"раздувов {b.WindFires}\n");

            _sb.Append($"    <color=#AAAAAA>{b.LastReason}</color>\n");
        }

        _sb.Append('\n');
    }

    private void AppendCart()
    {
        _sb.Append("<b>── ТАЧКА ──</b>\n");

        if (_carts.Count == 0) { _sb.Append("  нет в сцене\n\n"); return; }

        foreach (PushCart c in _carts)
        {
            if (c == null) continue;

            string grip = c.IsGripped
                ? "<color=#66DD66>ВЕДУТ</color>"
                : "<color=#8899AA>стоит</color>";
            string tip = c.IsTipped ? "  <color=#FFAA44>ОПРОКИНУТА</color>" : "";

            _sb.Append($"  <b>{c.name}</b>  {grip}{tip}\n");

            CartTrough t = c.Trough;
            if (t == null)
            {
                _sb.Append("    <color=#FF6666>нет CartTrough</color>\n");
                continue;
            }

            Vector3 off = t.LoadOffset;
            _sb.Append($"    груз {t.Count} шт   {t.MassKg:0.0} кг" +
                       (t.IsFull ? "   <color=#FFAA44>ПОЛНА</color>" : "") + "\n");
            _sb.Append($"    центр тяжести  вперёд {off.z:+0.00;-0.00}   " +
                       $"вбок {off.x:+0.00;-0.00}\n");
        }

        _sb.Append('\n');
    }

    private void AppendPlayer()
    {
        _sb.Append("<b>── ИГРОК ──</b>\n");

        if (_localPlayer == null) { _sb.Append("  свой игрок не найден\n\n"); return; }

        FirstPersonController p = _localPlayer;

        string mode = p.IsClimbing
            ? "<color=#66CCFF>ЛЕЗЕТ</color>"
            : p.CartSteering ? "<color=#66DD66>ВЕДЁТ ТАЧКУ</color>" : "идёт";

        _sb.Append($"  режим: {mode}   скорость ×{p.SpeedMultiplier:0.00}\n");
        _sb.Append($"  вертикальная скорость: {p.VerticalVelocity:+0.0;-0.0} м/с");

        // Подсказка про удар: именно это число меха превращают в силу залпа,
        // и при настройке полезно видеть его вживую.
        if (p.VerticalVelocity < -1f)
            _sb.Append($"   <color=#FFAA44>(падает — удар будет " +
                       $"{-p.VerticalVelocity:0.0})</color>");

        _sb.Append("\n  позиция: " + p.transform.position.ToString("0.0") + "\n\n");
    }

    /// <summary>Число с подсветкой: зелёное если требование выполнено.</summary>
    private static string Req(float value, bool ok)
        => ok ? $"<color=#66DD66>{value:0.0}</color>"
              : $"<color=#FF8866>{value:0.0}</color>";
}
