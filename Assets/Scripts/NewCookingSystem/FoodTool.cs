using System.Collections.Generic;
using FishNet;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ИНСТРУМЕНТ-ЗОНА. Процесс + триггер. Не знает про продукты ничего.
    ///
    /// ═══ ГДЕ ПРИМЕНЯТЬ, А ГДЕ НЕТ ═══
    ///
    /// Для ручных инструментов с развитой физикой (бензопила, болгарка,
    /// отбойник, мойка струёй) этот компонент НЕ нужен: у них свои сетевые
    /// обработчики, которые целятся, тянут инструмент, сыплют крошку, и зовут
    /// FoodPiece.ServerAddWork напрямую. Двойная обработка одного куска
    /// двумя компонентами — ошибка.
    ///
    /// FoodTool — для простого и автоматического: измельчитель, вертел,
    /// грохот, миксер в ведре, станок «закинул и отошёл».
    ///
    /// ═══ СЕТЬ ═══
    ///
    /// Работает ТОЛЬКО на сервере. На клиентах компонент ничего не считает —
    /// результат приходит через SyncVar продукта и тары.
    /// Active ставит серверный код предмета (ServerPrimaryUse, рубильник),
    /// а не ввод клиента. Чтобы клиенты видели «станок включён», синхронизируй
    /// это на своём NetworkBehaviour станка, а сюда передавай значение.
    /// </summary>
    public sealed class FoodTool : MonoBehaviour
    {
        public enum Target : byte
        {
            [InspectorName("По куску (картошка на столе)")]          Piece = 0,
            [InspectorName("По содержимому тары (молоко в ведре)")]  Vessel = 1,
            [InspectorName("И по куску, и по таре")]                 Both = 2,
        }

        [Header("Что делает")]
        public FoodProcess Process = FoodProcess.Cut;
        public Target WorksOn = Target.Piece;
        public string Title = "Инструмент";

        [Header("Когда работает")]
        [Tooltip("Включён ли. СТАВИТ СЕРВЕР.")]
        public bool Active;
        [Tooltip("Работает сам, без игрока: измельчитель, вертел")]
        public bool AutoRun;
        [Range(0.1f, 5f)] public float Speed = 1f;
        [Tooltip("Качество работы 0..1 — идёт в шкалу «форма» для ручных операций")]
        [Range(0f, 1f)] public float Quality = 1f;

        [Header("Рабочая точка")]
        public Transform WorkPoint;
        [Min(0.02f)] public float VesselSearchRadius = 0.25f;
        public LayerMask VesselMask = ~0;

        [Header("Ошмётки")]
        [Tooltip("Всплеск в момент смены состояния — прячет подмену меша")]
        public bool BurstOnChange = true;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly List<FoodPiece> _pieces = new List<FoodPiece>();
        private readonly Collider[] _hits = new Collider[16];
        private Cookware _vessel;

        public Vector3 Point => WorkPoint != null ? WorkPoint.position : transform.position;
        public bool Immersed => _vessel != null;
        public bool IsWorking => (Active || AutoRun) && (_pieces.Count > 0 || _vessel != null);

        private static bool IsServer => InstanceFinder.IsServerStarted;

        private void Update()
        {
            if (!IsServer) return;

            if (!Active && !AutoRun)
            {
                // Выключили посреди работы — фиксируем результат у кусков.
                if (_pieces.Count > 0) StopAll(false);
                _vessel = null;
                return;
            }

            float dt = Time.deltaTime * Speed;
            if (WorksOn != Target.Vessel) TickPieces(dt);
            if (WorksOn != Target.Piece) TickVessel(dt);
        }

        private void TickPieces(float dt)
        {
            for (int i = _pieces.Count - 1; i >= 0; i--)
            {
                FoodPiece p = _pieces[i];
                if (p == null || p.IsConsumed) { _pieces.RemoveAt(i); continue; }
                if (!p.CanApplyNow(Process)) continue;

                if (p.ServerAddWork(Process, dt, Quality))
                    OnChanged(p.transform.position, p.ToString());
            }
        }

        private void TickVessel(float dt)
        {
            Cookware found = FindImmersedVessel();
            if (found != _vessel)
            {
                _vessel = found;
                if (_log && found != null)
                    Debug.Log($"[{Title}] опущен в «{found.Def?.Title}»", this);
            }
            if (_vessel == null) return;

            if (_vessel.ServerTick(Process, dt))
                OnChanged(_vessel.transform.position, _vessel.Describe());
        }

        private Cookware FindImmersedVessel()
        {
            Vector3 at = Point;
            int n = Physics.OverlapSphereNonAlloc(at, VesselSearchRadius, _hits,
                                                  VesselMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Cookware c = _hits[i].GetComponentInParent<Cookware>();
                if (c != null && c.IsInside(at)) return c;
            }
            return null;
        }

        private void OnChanged(Vector3 at, string what)
        {
            if (_log) Debug.Log($"[{Title}] {what}", this);
            if (!BurstOnChange) return;
            // Всплеск ошмётков должны увидеть ВСЕ. Сервер здесь только
            // сообщает; рассылку делает сетевой компонент станка/инструмента
            // (ObserversRpc), который принимает это сообщение.
            SendMessage("ServerFoodChanged", at, SendMessageOptions.DontRequireReceiver);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || WorksOn == Target.Vessel) return;
            FoodPiece p = other.GetComponentInParent<FoodPiece>();
            if (p == null || _pieces.Contains(p)) return;
            _pieces.Add(p);

            if (_log && !p.CanApplyNow(Process))
                Debug.Log($"[{Title}] {p.Def?.Title} в состоянии " +
                          $"«{FoodStateInfo.NameOf(p.State)}» этим не обработать", this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;
            FoodPiece p = other.GetComponentInParent<FoodPiece>();
            if (p == null || !_pieces.Remove(p)) return;
            p.ServerStopWork();
        }

        private void OnDisable()
        {
            if (IsServer) StopAll(true);
            _vessel = null;
        }

        private void StopAll(bool clear)
        {
            for (int i = 0; i < _pieces.Count; i++)
                if (_pieces[i] != null) _pieces[i].ServerStopWork();
            if (clear) _pieces.Clear();
        }

        /// <summary>Подсказка «что будет». Работает и на клиенте.</summary>
        public string Preview(FoodPiece p)
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || p == null) return "";
            if (!db.Table.TryGet(p.ProductId, p.State, Process, out FoodTransition t))
                return "не подходит";
            return $"{FoodStateInfo.NameOf(t.From)} → {FoodStateInfo.NameOf(t.To)}" +
                   (t.Seconds > 0.01f ? $"  ({t.Seconds:0.#} сек)" : "");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Immersed ? Color.green : Color.gray;
            Gizmos.DrawWireSphere(Point, VesselSearchRadius);
        }
    }
}
