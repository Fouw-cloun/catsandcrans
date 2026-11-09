using System.Collections.Generic;
using FishNet;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// То, что можно отмыть струёй, но что не еда: забитая шлифмашинка,
    /// грязная тара, игрок в соусе. FoodJetWasher зовёт это на сервере
    /// каждый кадр, пока струя попадает.
    /// </summary>
    public interface IWashable
    {
        /// <summary>amount01 — секунды под полным напором за этот кадр (dt × напор).</summary>
        void ServerWashTick(float amount01);
    }

    /// <summary>
    /// МОЙКА СТРУЁЙ ДЛЯ НОВОЙ СИСТЕМЫ.
    ///
    /// Струю по-прежнему считает старый WaterJet: напор от гидранта, дуга с
    /// гравитацией, точки пути. Этот компонент только смотрит вдоль этих точек
    /// и моет каждый FoodPiece, в который попадает вода:
    ///   • процесс «Мыть» (Грязное → Мытое по таблице группы);
    ///   • шкала чистоты растёт обратно к 100 (отмываем упавшее на пол);
    ///   • продукт слегка сносит напором — видно, что по нему бьёт вода.
    ///
    /// Старый WaterJetWasher рядом может остаться: он моет старые FoodItem,
    /// этот — новые FoodPiece. Друг другу не мешают.
    ///
    /// Работает ТОЛЬКО на сервере. Вид струи — отдельно (CCLiquidStreamFromWaterJet),
    /// он крутится у каждого игрока сам.
    ///
    /// ═══ КУДА ВЕШАТЬ ═══
    /// На тот же объект JetOrigin, где стоит WaterJet (или на пистолет — Jet
    /// найдётся в детях).
    /// </summary>
    public sealed class FoodJetWasher : MonoBehaviour
    {
        [Tooltip("Струя, вдоль которой моем. Пусто — найдётся на этом объекте или в детях.")]
        [SerializeField] private WaterJet _jet;

        [Header("Мытьё")]
        [Tooltip("Скорость мытья: 1 — секунды правила как есть")]
        [Range(0.1f, 5f)][SerializeField] private float _washSpeed = 1f;
        [Tooltip("Слабее этого напора вода не моет, только капает")]
        [Range(0f, 1f)][SerializeField] private float _minFlow = 0.2f;
        [Tooltip("Сколько секунд под полным напором отмывает от 0 до 100 чистоты")]
        [Min(0.1f)][SerializeField] private float _secondsToClean = 2f;

        [Header("Попадание")]
        [Tooltip("Толщина струи для проверки попадания, м")]
        [Min(0.02f)][SerializeField] private float _hitRadius = 0.3f;
        [Tooltip("Слой еды")]
        [SerializeField] private LayerMask _foodMask = ~0;
        [Tooltip("Слои НЕ-еды, которую тоже можно отмыть: инструменты (шлифмашинка), тара. " +
                 "Обычно Interactable.")]
        [SerializeField] private LayerMask _washableMask = 0;
        [Tooltip("Как часто проверять попадание, с")]
        [Min(0.02f)][SerializeField] private float _scanInterval = 0.08f;

        [Header("Напор толкает продукт")]
        [Min(0f)][SerializeField] private float _pushForce = 2.5f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly Collider[] _hits = new Collider[24];
        private readonly HashSet<FoodPiece> _wet = new HashSet<FoodPiece>();
        private readonly HashSet<FoodPiece> _seen = new HashSet<FoodPiece>();
        private readonly List<FoodPiece> _tmp = new List<FoodPiece>();
        private readonly Dictionary<FoodPiece, Vector3> _pushDir = new Dictionary<FoodPiece, Vector3>();
        private readonly HashSet<IWashable> _washables = new HashSet<IWashable>();
        private readonly List<IWashable> _washTmp = new List<IWashable>();
        private float _scanTimer;

        /// <summary>Сколько продуктов под струёй прямо сейчас (для HUD).</summary>
        public int WetCount => _wet.Count;

        private void Awake()
        {
            if (_jet == null) _jet = GetComponentInChildren<WaterJet>(true);
            if (_jet == null) Debug.LogError($"[Мойка] «{name}»: не найден WaterJet", this);
        }

        private void Update()
        {
            if (_jet == null || !InstanceFinder.IsServerStarted) return;

            float flow = _jet.Flow01;
            if (!_jet.IsFlowing || flow < _minFlow)
            {
                if (_wet.Count > 0) StopAll();
                _washables.Clear();
                return;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = _scanInterval;
                Scan();
            }

            float dt = Time.deltaTime;

            _washTmp.Clear();
            _washTmp.AddRange(_washables);
            foreach (IWashable w in _washTmp)
            {
                if (w == null || (w is Object o && o == null)) { _washables.Remove(w); continue; }
                w.ServerWashTick(dt * flow);
            }

            _tmp.Clear();
            _tmp.AddRange(_wet);
            foreach (FoodPiece p in _tmp)
            {
                if (p == null || p.IsConsumed) { _wet.Remove(p); continue; }

                if (p.CanApplyNow(FoodProcess.Wash) &&
                    p.ServerAddWork(FoodProcess.Wash, dt * _washSpeed * flow, flow) && _log)
                    Debug.Log($"[Мойка] {p}", p);

                p.ServerAddClean(dt * flow / _secondsToClean);

                if (_pushForce > 0f && !p.IsHeld && _pushDir.TryGetValue(p, out Vector3 dir))
                {
                    Rigidbody rb = p.GetComponent<Rigidbody>();
                    if (rb != null && !rb.isKinematic) rb.AddForce(dir * (_pushForce * flow), ForceMode.Force);
                }
            }
        }

        /// <summary>Пройти вдоль точек пути струи сферами и собрать продукты.</summary>
        private void Scan()
        {
            Vector3[] pts = _jet.PathPointsWorld;
            int count = _jet.PathCount;
            _seen.Clear();
            _pushDir.Clear();
            _washables.Clear();
            int mask = _foodMask | _washableMask;

            if (pts != null)
            {
                for (int i = 1; i < count && i < pts.Length; i++)
                {
                    Vector3 a = pts[i - 1], b = pts[i];
                    Vector3 seg = b - a;
                    float len = seg.magnitude;
                    if (len < 0.0001f) continue;

                    int n = Physics.OverlapCapsuleNonAlloc(a, b, _hitRadius, _hits, mask,
                                                           QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < n; k++)
                    {
                        IWashable w = _hits[k].GetComponentInParent<IWashable>();
                        if (w != null) _washables.Add(w);

                        FoodPiece p = _hits[k].GetComponentInParent<FoodPiece>();
                        if (p == null || _seen.Contains(p)) continue;
                        _seen.Add(p);
                        _pushDir[p] = seg / len;
                    }
                }
            }

            // Кто вышел из-под струи — фиксируем результат.
            _tmp.Clear();
            foreach (FoodPiece p in _wet) if (p == null || !_seen.Contains(p)) _tmp.Add(p);
            foreach (FoodPiece p in _tmp)
            {
                _wet.Remove(p);
                if (p != null) p.ServerStopWork();
            }
            foreach (FoodPiece p in _seen) _wet.Add(p);
        }

        private void StopAll()
        {
            foreach (FoodPiece p in _wet) if (p != null) p.ServerStopWork();
            _wet.Clear();
            _pushDir.Clear();
        }

        private void OnDisable() => StopAll();

        private void OnDrawGizmosSelected()
        {
            if (_jet == null || _jet.PathPointsWorld == null) return;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Vector3[] pts = _jet.PathPointsWorld;
            for (int i = 0; i < _jet.PathCount && i < pts.Length; i++)
                Gizmos.DrawWireSphere(pts[i], _hitRadius);
        }
    }
}
