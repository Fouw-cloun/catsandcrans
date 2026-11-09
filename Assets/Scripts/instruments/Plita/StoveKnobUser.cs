using FishNet.Object;
using UnityEngine;

/// <summary>
/// КРУТИТЬ РУЧКИ ПЛИТЫ. Вешается на префаб ИГРОКА, рядом с PlayerInteractor.
///
/// КАК ЭТО РАБОТАЕТ ТЕПЕРЬ:
///   навёлся на ручку        → на экране полукруглая шкала градусов;
///   зажал ЛКМ и ведёшь мышь → крутишь температуру, ручка едет следом;
///   отпустил                → конфорка встала на эту температуру и держит её;
///   настройку сбило         → та же ЛКМ по ручке открывает мини-игру.
///
/// ОДНА КЛАВИША НА ДВА ДЕЙСТВИЯ — это намеренно. Игроку не надо помнить, что
/// «E настраивает, а ЛКМ крутит»: он всегда жмёт ЛКМ на ручке, а что именно
/// произойдёт, решает состояние конфорки. Подсказка на экране всегда говорит,
/// что будет.
///
/// ВЗГЛЯД ЗАМОРАЖИВАЕТСЯ на время протяжки — тем же приёмом, что в HandsawItem:
/// иначе движение мыши одновременно крутило бы ручку и разворачивало игрока,
/// и попасть в нужный градус было бы невозможно.
///
/// Прицел считает КЛИЕНТ — сервер не знает наклон камеры.
/// </summary>
public class StoveKnobUser : NetworkBehaviour
{
    [Header("Прицел")]
    [Tooltip("Дальность, метры")]
    [SerializeField] private float _reach = 2.5f;

    [Tooltip("Слои плиты. Можно оставить всё: компонент всё равно проверяет " +
             "наличие StoveBurner.")]
    [SerializeField] private LayerMask _mask = ~0;

    [Header("Управление")]
    [Tooltip("Зажать на ручке: крутить температуру или начать настройку")]
    [SerializeField] private KeyCode _useKey = KeyCode.Mouse0;

    [Tooltip("Сколько градусов даёт полное движение мыши вбок. Больше — " +
             "резче, но труднее попасть в конкретное значение.")]
    [SerializeField] private float _degreesPerMouse = 34f;

    [Tooltip("Округлять до шага, градусы. 5 = ручка щёлкает по пятёркам, и " +
             "попасть в круглое число легко. 0 = плавно.")]
    [SerializeField] private float _snapStep = 5f;

    [Tooltip("Сколько раз в секунду слать серверу новое значение, пока крутишь")]
    [Min(5f)][SerializeField] private float _sendRate = 20f;

    [Header("Шкала на экране")]
    [Tooltip("Радиус полукруга, пикселей")]
    [SerializeField] private float _dialRadius = 110f;

    [Tooltip("Сколько делений рисовать по дуге")]
    [Min(8)][SerializeField] private int _dialTicks = 33;

    [Tooltip("Подписывать каждое N-е деление градусами")]
    [Min(2)][SerializeField] private int _labelEvery = 8;

    [Tooltip("На сколько опустить шкалу от центра экрана, пикселей")]
    [SerializeField] private float _dialOffsetY = 130f;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud = true;

    private Camera _cam;
    private StoveBurner _hover;

    // протяжка
    private StoveBurner _dragging;
    private float _dragC;
    private float _nextSend;

    // заморозка взгляда
    private FirstPersonController _fpc;
    private Transform _camT;
    private Quaternion _frozenLook;
    private bool _lookFrozen;

    private void Awake()
    {
        _fpc = GetComponent<FirstPersonController>();

        if (GetComponentInParent<NetworkObject>() == null)
            Debug.LogError($"[Плита] «{name}»: ни на этом объекте, ни выше нет " +
                           "NetworkObject. Компонент должен висеть на КОРНЕ префаба " +
                           "игрока, там же, где PlayerInteractor.", this);
    }

    private void Update()
    {
        if (!base.IsOwner) return;

        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        if (_camT == null) _camT = _cam.transform;

        // Пока крутим — прицел не пересчитываем: ручка не должна теряться
        // от того, что курсор на пиксель уехал с коллайдера.
        if (_dragging != null) { UpdateDrag(); return; }

        _hover = FindBurner();
        if (_hover == null) return;

        if (Input.GetKeyDown(_useKey)) BeginUse(_hover);

        // Колесо оставлено как грубая подстройка: иногда надо просто чуть
        // убавить, не заходя в протяжку.
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        int steps = wheel > 0.01f ? 1 : wheel < -0.01f ? -1 : 0;
        if (steps != 0) CmdTurn(_hover, steps);
    }

    /// <summary>
    /// Что произойдёт по ЛКМ, решает СОСТОЯНИЕ конфорки, а не отдельная
    /// клавиша. Сбило настройку — чиним, всё в порядке — крутим.
    /// </summary>
    private void BeginUse(StoveBurner burner)
    {
        if (burner.IsDead || burner.IsBroken) return;

        if (burner.NeedsTuning)
        {
            BurnerTuning tuning = burner.GetComponent<BurnerTuning>();
            if (tuning != null) { tuning.BeginTuning(); return; }
        }

        _dragging = burner;
        _dragC = burner.KnobC;
        _nextSend = 0f;
        FreezeLook(true);
    }

    private void UpdateDrag()
    {
        if (_dragging == null) { EndDrag(); return; }

        if (!Input.GetKey(_useKey)) { EndDrag(); return; }

        // Ушёл слишком далеко — отпускаем. Иначе можно было бы крутить плиту
        // из соседней комнаты, зажав кнопку.
        if (Vector3.Distance(_camT.position, _dragging.transform.position) > _reach * 1.6f)
        {
            EndDrag();
            return;
        }

        // Вправо — горячее. GetAxisRaw, а не GetAxis: сглаживание Unity даёт
        // «доезд» ручки после того, как мышь уже стоит.
        _dragC += Input.GetAxisRaw("Mouse X") * _degreesPerMouse;
        _dragC = Mathf.Clamp(_dragC, _dragging.MinC, _dragging.MaxC);

        if (Time.time >= _nextSend)
        {
            _nextSend = Time.time + 1f / Mathf.Max(5f, _sendRate);
            CmdSetTemp(_dragging, Snap(_dragC));
        }
    }

    private void EndDrag()
    {
        if (_dragging != null)
            CmdSetTemp(_dragging, Snap(_dragC));

        _dragging = null;
        FreezeLook(false);
    }

    private float Snap(float c)
        => _snapStep > 0.01f ? Mathf.Round(c / _snapStep) * _snapStep : c;

    /// <summary>
    /// Заморозка взгляда. Контроллер всё равно накручивает поворот у себя,
    /// поэтому мы просто возвращаем камеру на место каждый кадр, а отпуская —
    /// зовём ResetLookToCurrent, чтобы контроллер согласился с тем, что видит
    /// игрок. Ровно то же самое делает ножовка.
    /// </summary>
    private void FreezeLook(bool on)
    {
        if (on)
        {
            if (_lookFrozen || _camT == null) return;
            _lookFrozen = true;
            _frozenLook = _camT.rotation;
            if (_fpc != null) _fpc.SpeedMultiplier = 0f;
        }
        else
        {
            if (!_lookFrozen) return;
            _lookFrozen = false;
            if (_fpc != null)
            {
                _fpc.SpeedMultiplier = 1f;
                _fpc.ResetLookToCurrent();
            }
        }
    }

    private void LateUpdate()
    {
        if (!base.IsOwner || !_lookFrozen || _camT == null) return;
        _camT.rotation = _frozenLook;
    }

    /// <summary>Конфорка под прицелом. Через GetComponentInParent, поэтому
    /// годится попадание и в ручку, и в саму конфорку.</summary>
    private StoveBurner FindBurner()
    {
        if (!Physics.Raycast(_camT.position, _camT.forward,
                             out RaycastHit hit, _reach, _mask,
                             QueryTriggerInteraction.Ignore))
            return null;

        return hit.collider.GetComponentInParent<StoveBurner>();
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    /// <summary>
    /// ССЫЛКА ИДЁТ НА САМУ КОНФОРКУ, А НЕ НА ЕЁ NetworkObject.
    ///
    /// ПОЧЕМУ ЭТО ВАЖНО — здесь и был баг «выставил температуру, а она
    /// сбросилась в ноль». У плиты ОДИН NetworkObject на корне и ЧЕТЫРЕ
    /// StoveBurner на дочерних объектах. Прежний код слал NetworkObject и на
    /// сервере делал GetComponent&lt;StoveBurner&gt;() — а тот возвращает
    /// компонент С КОРНЯ, где его нет вовсе. Значит либо null и команда молча
    /// пропадала, либо (если конфорка всё же на корне) всегда одна и та же,
    /// какую ручку ни крути. Уставка не менялась, ручка возвращалась к
    /// нарисованному минимуму, и выглядело это как сброс в ноль.
    ///
    /// FishNet умеет сериализовать NetworkBehaviour: летит тот же id объекта
    /// плюс индекс компонента, то есть на пару байт больше, а адресат теперь
    /// ровно тот, на который смотрел игрок.
    ///
    /// Заодно и дистанцию меряем до САМОЙ конфорки. До корня плиты могло быть
    /// заметно дальше, чем до её ручки, и проверка резала честные команды.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdSetTemp(StoveBurner burner, float celsius)
    {
        if (burner == null) return;

        // Проверка на вменяемость присланного: клиент считает прицел сам.
        if (Vector3.Distance(burner.transform.position, transform.position) > _reach + 2f)
        {
            Debug.LogWarning($"[Плита] команда отброшена: до конфорки " +
                             $"«{burner.name}» " +
                             $"{Vector3.Distance(burner.transform.position, transform.position):0.0} м, " +
                             $"предел {_reach + 2f:0.0} м", this);
            return;
        }

        burner.ServerSetTemperature(celsius);
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdTurn(StoveBurner burner, int steps)
    {
        if (burner == null) return;
        if (Vector3.Distance(burner.transform.position, transform.position) > _reach + 2f)
            return;

        burner.ServerTurnKnob(steps);
    }

    // ───────────────────────── ШКАЛА НА ЭКРАНЕ ─────────────────────────

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner) return;

        StoveBurner b = _dragging != null ? _dragging : _hover;
        if (b == null) return;

        DrawDial(b);
        DrawHint(b);
    }

    /// <summary>
    /// ПОЛУКРУГЛАЯ ШКАЛА ГРАДУСОВ.
    ///
    /// Деления рисуются точками по дуге, а не поворотом матрицы GUI: маленькие
    /// квадраты на своих местах читаются так же, а возни с
    /// GUIUtility.RotateAroundPivot и вложенными матрицами нет вообще. Для
    /// вспомогательного HUD это правильный размен.
    ///
    /// Показываются ДВА указателя: где ручка сейчас и где она была до сбоя.
    /// Игрок сразу видит, насколько её увело и куда возвращать.
    /// </summary>
    private void DrawDial(StoveBurner b)
    {
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f + _dialOffsetY;

        float shown = _dragging != null ? Snap(_dragC) : b.KnobC;
        float t = Mathf.InverseLerp(b.MinC, b.MaxC, shown);

        int ticks = Mathf.Max(8, _dialTicks);

        for (int i = 0; i < ticks; i++)
        {
            float f = i / (float)(ticks - 1);

            // Полукруг слева направо: 180° = холод, 0° = жара.
            float ang = Mathf.PI * (1f - f);
            float x = cx + Mathf.Cos(ang) * _dialRadius;
            float y = cy - Mathf.Sin(ang) * _dialRadius;

            bool filled = f <= t;
            bool off = Mathf.Lerp(b.MinC, b.MaxC, f) < b.OffBelowC;

            Color c = off
                ? new Color(0.45f, 0.5f, 0.55f, filled ? 0.9f : 0.35f)
                : filled
                    ? Color.Lerp(new Color(1f, 0.85f, 0.35f),
                                 new Color(1f, 0.30f, 0.10f), f)
                    : new Color(1f, 1f, 1f, 0.20f);

            float s = filled ? 7f : 4f;
            DrawRect(new Rect(x - s * 0.5f, y - s * 0.5f, s, s), c);

            if (i % Mathf.Max(2, _labelEvery) == 0)
            {
                float deg = Mathf.Lerp(b.MinC, b.MaxC, f);
                GUIStyle ls = new GUIStyle(GUI.skin.label) { fontSize = 11 };
                ls.normal.textColor = new Color(1f, 1f, 1f, 0.6f);

                float lx = cx + Mathf.Cos(ang) * (_dialRadius + 18f);
                float ly = cy - Mathf.Sin(ang) * (_dialRadius + 18f);
                GUI.Label(new Rect(lx - 20f, ly - 9f, 40f, 18f), $"{deg:0}", ls);
            }
        }

        // Куда ручку должно вернуть — призрачная метка на прежней уставке.
        if (b.NeedsTuning)
        {
            float ft = Mathf.InverseLerp(b.MinC, b.MaxC, b.SetpointC);
            float a = Mathf.PI * (1f - ft);
            float x = cx + Mathf.Cos(a) * _dialRadius;
            float y = cy - Mathf.Sin(a) * _dialRadius;
            DrawRect(new Rect(x - 6f, y - 6f, 12f, 12f), new Color(0.4f, 1f, 0.6f, 0.55f));
        }

        // Крупная цифра в центре дуги.
        GUIStyle st = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            alignment = TextAnchor.MiddleCenter
        };
        st.normal.textColor = shown < b.OffBelowC
            ? new Color(0.6f, 0.7f, 0.8f)
            : Color.Lerp(new Color(1f, 0.9f, 0.5f), new Color(1f, 0.45f, 0.2f), t);

        string big = shown < b.OffBelowC ? "выкл" : $"{shown:0}°";
        GUI.Label(new Rect(cx - 80f, cy - 42f, 160f, 34f), big, st);
    }

    private void DrawHint(StoveBurner b)
    {
        GUIStyle st = new GUIStyle(GUI.skin.box) { fontSize = 15 };

        string txt;
        Color col;

        if (b.IsDead)
        {
            txt = "конфорка сгорела — нужна починка";
            col = new Color(1f, 0.4f, 0.35f);
        }
        else if (b.IsBroken)
        {
            txt = "РУЧКУ СОРВАЛО — уходи от плиты";
            col = new Color(1f, 0.35f, 0.2f);
        }
        else if (!b.HasGas)
        {
            txt = "нет газа — подключи шланг к плите";
            col = new Color(1f, 0.6f, 0.2f);
        }
        else if (_dragging != null)
        {
            txt = "веди мышь влево-вправо, отпусти — встанет";
            col = new Color(0.6f, 1f, 0.7f);
        }
        else if (b.NeedsTuning)
        {
            txt = $"температура ушла на {b.KnobC:0}° — зажми ЛКМ, чтобы поправить";
            col = new Color(1f, 0.7f, 0.25f);
        }
        else
        {
            txt = "зажми ЛКМ и веди мышь — температура";
            col = new Color(0.6f, 1f, 0.7f);
        }

        st.normal.textColor = col;

        Vector2 size = st.CalcSize(new GUIContent(txt));
        GUI.Box(new Rect(Screen.width * 0.5f - size.x * 0.5f - 8f,
                         Screen.height * 0.5f + 40f, size.x + 16f, 26f), txt, st);
    }

    private static Texture2D _white;

    private static void DrawRect(Rect r, Color c)
    {
        if (_white == null)
        {
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
        }

        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, _white);
        GUI.color = prev;
    }
}
