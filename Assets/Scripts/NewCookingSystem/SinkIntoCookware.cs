using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v23.33: ПРОДУКТ «ПОГРУЖАЕТСЯ» В ЛЮБУЮ ТАРУ — стручок ванили и подобные добавки.
    ///
    /// Обычный кусок (Food Piece) в таре, которая «не глотает» (ведро, банка для бетона, сковорода), лежит
    /// предметом сам по себе. Добавка же должна уйти В СОСТАВ: кинул стручок в ведро со сливками — он
    /// исчезает как предмет и становится 1 порцией продукта в таре (рецепт его видит, миксер мешает со всем).
    ///
    /// Срабатывает:
    ///   • при попадании в полость тары (Cookware зовёт ServerPut → этот обработчик);
    ///   • и сам, раз в Check Every секунд: лежит внутри любой тары (бросили, уже находясь внутри полости) — погружается.
    /// Места в таре не занимает (как щепотка: сыпется и в полное ведро). Только сервер; клиенты видят деспавн.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class SinkIntoCookware : MonoBehaviour, IFoodPutHandler
    {
        [Tooltip("Как часто (с) проверять, не лежит ли предмет уже внутри тары")]
        [SerializeField] private float _checkEvery = 0.15f;
        [Tooltip("Писать в консоль, куда погрузился")]
        [SerializeField] private bool _log = true;

        private FoodPiece _fp;
        private float _next;

        private void Awake() { _fp = GetComponent<FoodPiece>(); }

        public bool ServerHandlePut(Cookware cw, out bool put)
        {
            put = Sink(cw);
            return true;
        }

        private void Update()
        {
            if (_fp == null || !_fp.IsServerInitialized || _fp.IsHeld || _fp.IsConsumed) return;
            if (Time.time < _next) return;
            _next = Time.time + Mathf.Max(0.05f, _checkEvery);
            Collider c = GetComponent<Collider>();
            Vector3 p = c != null ? c.bounds.center : transform.position;
            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null || cw.Def == null || !cw.IsServerInitialized) continue;
                if (cw.IsInside(p)) { Sink(cw); return; }
            }
        }

        private bool Sink(Cookware cw)
        {
            if (cw == null || cw.Def == null || _fp == null || _fp.IsHeld || _fp.IsConsumed) return false;
            ushort id = _fp.ProductId;
            FoodState st = _fp.State;
            int n = Mathf.Max(1, _fp.Count);
            byte ck = _fp.Cook;
            if (!_fp.ServerTryConsume()) return false;
            _fp.ServerStopWork();
            cw.ServerAddPinch(id, st, n, ck, false);
            cw.ServerAgitate(0.4f);
            if (_log) Debug.Log($"[Добавка] «{name}» погрузился в «{cw.name}» (+{n} порц.)", cw);
            if (_fp.NetworkObject != null && _fp.NetworkObject.IsSpawned) _fp.NetworkObject.Despawn();
            else Destroy(gameObject);
            return true;
        }
    }
}
