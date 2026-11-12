using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ПОСКОЛЬЗНУТЬСЯ НА ЛУЖЕ — у своего (локального) игрока.
    ///
    ///   • идёшь по луже (не стоишь!) — раз в Slip Check Every секунд бросается шанс Slip Chance
    ///     (настройки — на префабе лужи, блок «v11: поскользнуться на луже»);
    ///   • выпало — ноги уезжают вперёд, игрок падает на спину и лежит;
    ///   • ПРОБЕЛ — встать (с анимацией подъёма);
    ///   • после подъёма Slip Cooldown секунд (10) не падаешь снова;
    ///   • стоишь в луже, крадёшься медленнее Slip Min Speed, висишь на лестнице,
    ///     сидишь в технике или открыто меню — не падаешь.
    ///
    /// Ничего вешать не надо: объект создаётся сам при запуске. Тело наклоняется целиком —
    /// если NetworkTransform игрока передаёт поворот, лежащего видят и остальные.
    /// Пока лежишь — ходить и крутить головой нельзя (обзор зафиксирован, камера смотрит вверх).
    /// </summary>
    public sealed class PuddleSlip : MonoBehaviour
    {
        private enum State { Walk, Falling, Lying, Rising }

        private const float FallTime = 0.35f;
        private const float RiseTime = 0.45f;
        private const float LieAngle = 80f;                 // на спину, градусы

        private static PuddleSlip _inst;

        /// <summary>Отладка: раз в секунду пишет в консоль, видит ли лужу под ногами, скорость, шанс.</summary>
        public static bool DebugLog = true;
        private float _dbgT;

        private FirstPersonController _fpc;
        private CharacterController _cc;
        private float _findT;
        private State _state = State.Walk;
        private float _t, _checkT, _coolUntil;
        private Vector3 _lastPos, _slide;
        private bool _hasLast;
        private float _yaw;
        private Quaternion _fromRot;
        private GUIStyle _style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_inst != null) return;
            var go = new GameObject("CC Puddle Slip (auto)") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<PuddleSlip>();
        }

        /// <summary>Лежит ли сейчас свой игрок (для других систем: не брать предметы и т.п.).</summary>
        public static bool LocalDown => _inst != null && _inst._state != State.Walk;

        private void Update()
        {
            if (_fpc == null || !_fpc.IsOwner)
            {
                _fpc = null;
                _findT -= Time.deltaTime;
                if (_findT > 0f) return;
                _findT = 1f;
                foreach (FirstPersonController f in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
                    if (f != null && f.IsOwner) { _fpc = f; _cc = f.GetComponent<CharacterController>(); _hasLast = false; break; }
                if (_fpc == null) return;
            }

            float dt = Time.deltaTime;
            switch (_state)
            {
                case State.Walk: TickWalk(dt); break;
                case State.Falling: TickFall(dt); break;
                case State.Lying: TickLie(dt); break;
                case State.Rising: TickRise(dt); break;
            }
        }

        // ───────────────────────── идёт ─────────────────────────

        private void TickWalk(float dt)
        {
            Vector3 p = _fpc.transform.position;
            Vector3 vel = _hasLast ? (p - _lastPos) / Mathf.Max(1e-4f, dt) : Vector3.zero;
            _lastPos = p; _hasLast = true;
            vel.y = 0f;

            // v11.1: ноги — низ капсулы (у игрока точка объекта может быть не у ног)
            Vector3 feet = _cc != null && _cc.enabled ? new Vector3(p.x, _cc.bounds.min.y, p.z) : p;
            float edge;
            LiquidPuddle pd = LiquidPuddle.FindUnderFoot(feet, 0.05f, out edge);

            _dbgT -= dt;
            bool dbg = DebugLog && _dbgT <= 0f;
            if (dbg)
            {
                _dbgT = 1f;
                string why = Time.time < _coolUntil ? "перерыв " + (_coolUntil - Time.time).ToString("0") + " с"
                           : _fpc.CraneSuspended ? "в технике/подвес" : _fpc.IsClimbing ? "на лестнице"
                           : UiModal.AnyOpen ? "открыто меню" : (_cc != null && !_cc.isGrounded) ? "в воздухе"
                           : pd == null ? "не в луже (до края " + (edge < 1e5f ? edge.ToString("0.00") + " м" : "луж нет") + ")"
                           : vel.magnitude < pd.SlipMinSpeed ? "в луже, но стоишь (" + vel.magnitude.ToString("0.0") + " м/с)"
                           : "в луже, бегу " + vel.magnitude.ToString("0.0") + " м/с — шанс " + (pd.SlipChance * 100f).ToString("0") + "%";
                Debug.Log("[Скольжение] " + why);
            }

            if (Time.time < _coolUntil) return;
            if (_fpc.CraneSuspended || _fpc.IsClimbing || UiModal.AnyOpen) return;
            if (_cc != null && !_cc.isGrounded) return;

            if (pd == null) { _checkT = 0f; return; }
            if (vel.magnitude < pd.SlipMinSpeed) return;          // стоишь или крадёшься — не падаешь

            _checkT += dt;
            if (_checkT < pd.SlipCheckEvery) return;
            _checkT = 0f;
            if (Random.value >= pd.SlipChance) return;

            BeginFall(vel, pd);
        }

        private void BeginFall(Vector3 vel, LiquidPuddle pd)
        {
            _state = State.Falling;
            _t = 0f;
            _yaw = _fpc.transform.eulerAngles.y;
            _fromRot = Quaternion.Euler(0f, _yaw, 0f);
            // ноги уезжают вперёд по ходу движения
            _slide = vel.normalized * Mathf.Clamp(vel.magnitude * 0.9f, 1.2f, 3f);
            _fpc.SetCraneSuspended(true, Vector3.zero);           // ходьба и прыжок выключены
            // CharacterController на время падения выключаем: при наклоне тела его центр уходит
            // в пол, и контроллер «выстрелил» бы игрока вверх
            if (_cc != null) _cc.enabled = false;
            _cooldownFrom = pd != null ? pd.SlipCooldown : 10f;
            Debug.Log("[Лужа] поскользнулся! Пробел — встать");
        }

        private float _cooldownFrom = 10f;

        // ───────────────────────── падает ─────────────────────────

        private void TickFall(float dt)
        {
            _t += dt;
            float k = Mathf.Clamp01(_t / FallTime);
            float e = 1f - (1f - k) * (1f - k) * (1f - k);           // быстро вначале
            // проскальзывание вперёд (затухает)
            Vector3 step = _slide * (1f - k) * dt;
            if (step.sqrMagnitude > 1e-8f)
            {
                Vector3 from = _fpc.transform.position + Vector3.up * 0.4f;
                bool wall = Physics.Raycast(from, step.normalized, step.magnitude + 0.35f, ~0, QueryTriggerInteraction.Ignore);
                if (!wall) _fpc.transform.position += step;               // ноги уезжают, у стены — стоп
            }
            _fpc.HoldLook();
            _fpc.transform.rotation = Quaternion.Euler(0f, _yaw, 0f) * Quaternion.Euler(-LieAngle * e, 0f, 0f);
            if (k >= 1f)
            {
                _state = State.Lying;
                _t = 0f;
            }
        }

        // ───────────────────────── лежит ─────────────────────────

        private void TickLie(float dt)
        {
            _t += dt;
            _fpc.HoldLook();                                          // обзор зафиксирован
            // лёгкое «покачивание» — отлежался
            float wob = Mathf.Sin(_t * 2.1f) * 1.2f * Mathf.Exp(-_t * 0.8f);
            _fpc.transform.rotation = Quaternion.Euler(0f, _yaw, 0f) * Quaternion.Euler(-LieAngle + wob, 0f, wob * 0.6f);
            if (!UiModal.AnyOpen && Input.GetKeyDown(KeyCode.Space))
            {
                _state = State.Rising;
                _t = 0f;
            }
        }

        // ───────────────────────── встаёт ─────────────────────────

        private void TickRise(float dt)
        {
            _t += dt;
            float k = Mathf.Clamp01(_t / RiseTime);
            float e = k * k * (3f - 2f * k);
            _fpc.HoldLook();
            // чуть «перекачивается» вперёд и выпрямляется
            float over = Mathf.Sin(k * Mathf.PI) * 8f;
            _fpc.transform.rotation = Quaternion.Euler(0f, _yaw, 0f) * Quaternion.Euler(-LieAngle * (1f - e) + over, 0f, 0f);
            if (k >= 1f)
            {
                _fpc.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                if (_cc != null) _cc.enabled = true;
                _fpc.SetCraneSuspended(false, Vector3.zero);
                _state = State.Walk;
                _coolUntil = Time.time + _cooldownFrom;
                _checkT = 0f;
                _hasLast = false;
            }
        }

        // ───────────────────────── подсказка ─────────────────────────

        private void OnGUI()
        {
            if (_fpc == null || _state == State.Walk) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                _style.normal.textColor = Color.white;
            }
            string t = _state == State.Lying ? "Поскользнулся на луже!   ПРОБЕЛ — встать" : _state == State.Falling ? "Ой!" : "Встаёшь…";
            float w = 520f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.38f, w, 48f), t, _style);
        }

        private void OnDestroy()
        {
            if (_inst == this) _inst = null;
            // не оставить игрока «подвешенным», если объект убрали посреди падения
            if (_fpc != null && _state != State.Walk)
            {
                _fpc.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                if (_cc != null) _cc.enabled = true;
                _fpc.SetCraneSuspended(false, Vector3.zero);
            }
        }
    }
}
