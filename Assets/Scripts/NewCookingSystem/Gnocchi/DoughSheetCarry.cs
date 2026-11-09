using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КОМ ТЕСТА В РУКАХ → БОЛЬШОЕ ТЕСТО. Только сервер, вешается сам (DoughSheetDump) на шар теста,
    /// вывалившийся из тары (FoodPiece — его берут F и бросают ЛКМ, как хлебное тесто).
    ///   • Шар взяли в руки и отпустили / бросили — где лёг, там становится комом большого теста (DoughSheet):
    ///     плющить кувалдой, раскатывать катком.
    ///   • Упал на такой же пласт — прилипает к нему бугром (тесто добавилось).
    ///   • Пока его ни разу не брали — лежит шаром (можно подойти и взять).
    /// Руками не ставить — компонента нет в префабе, он добавляется в игре.
    /// </summary>
    public sealed class DoughSheetCarry : MonoBehaviour
    {
        public DoughKind Kind = DoughKind.Pelmeni;
        public bool Log = true;

        private FoodPiece _fp;
        private Rigidbody _rb;
        private bool _wasHeld;
        private float _releasedAt = -1f;

        private void Awake()
        {
            _fp = GetComponent<FoodPiece>();
            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (_fp == null || _fp.IsConsumed) { Destroy(this); return; }
            if (!_fp.IsServerInitialized) return;
            if (_fp.IsHeld) { _wasHeld = true; _releasedAt = -1f; return; }
            if (!_wasHeld) return;
            if (_releasedAt < 0f) _releasedAt = Time.time;
            float speed = _rb != null && !_rb.isKinematic ? _rb.linearVelocity.magnitude : 0f;
            float t = Time.time - _releasedAt;
            if ((t > 0.2f && speed < 0.5f) || t > 3f) Convert();
        }

        private void Convert()
        {
            int portions = _fp.Count;
            Vector3 p = transform.position;
            // упал на такое же тесто — прилипает к нему
            foreach (DoughSheet ds in DoughSheet.All)
            {
                if (ds == null || ds.Kind != Kind || !ds.IsServerInitialized) continue;
                if (!ds.HeightAt(p, out _)) continue;
                if (!_fp.ServerTryConsume()) return;
                ds.ServerAddPortions(p, portions);
                if (Log) Debug.Log($"[Тесто] ком ({portions} порц.) бросили на «{ds.name}» — прилип к пласту", ds);
                _fp.NetworkObject.Despawn();
                return;
            }
            if (DoughSheetDump.BallPrefab == null)
            {
                if (Log) Debug.LogWarning("[Тесто] ком положили, но нет префаба большого теста — CatCooks → Пельмени → 2. Пока остаётся шаром", this);
                _wasHeld = false;
                return;
            }
            if (!_fp.ServerTryConsume()) return;
            DoughSheet sheet = DoughSheetDump.ServerSpawnBallAt(Kind, portions, p);
            if (Log) Debug.Log($"[Тесто] ком «{DoughSheet.KindTitle(Kind)}» ({portions} порц.) положили — теперь большое тесто: кувалда, каток" +
                               (sheet == null ? " — НЕ ВЫШЛО (нет префаба кома, Пельмени → 2)" : ""), sheet != null ? (Object)sheet : this);
            _fp.NetworkObject.Despawn();
        }
    }
}
