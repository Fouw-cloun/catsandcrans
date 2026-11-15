using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v19.4: МОРОЖЕНОЕ ТАЕТ.
    ///   • Замёрзшее мороженое в таре держится: не высыпается при наклоне, его черпают лопатой/мастерком.
    ///   • Без холода оно ТАЕТ: через Melt Seconds (на огне — быстрее в Hot K раз) становится
    ///     «Мороженое / Растоплено» — жидкое, льётся, плещется.
    ///   • Полил азотом — таяние сбрасывается, растаявшее снова замерзает (Refreeze Seconds струи).
    /// Сам создаётся в сцене; считает только сервер. Продукт — «Мороженое» по названию.
    /// </summary>
    public sealed class IceCreamMelt : MonoBehaviour
    {
        [Tooltip("За сколько секунд мороженое в таре тает без холода")]
        [Min(5f)] public float MeltSeconds = 120f;
        [Tooltip("На огне (тара греется) тает во столько раз быстрее")]
        [Min(1f)] public float HotK = 6f;
        [Tooltip("Сколько секунд струи азота, чтобы растаявшее снова замёрзло")]
        [Min(0.2f)] public float RefreezeSeconds = 1.5f;
        public bool Log = true;

        private static IceCreamMelt _inst;
        private ProductDef _ice;
        private bool _resolved;
        private float _tick;
        private readonly Dictionary<Cookware, float> _warm = new Dictionary<Cookware, float>();
        private readonly Dictionary<Cookware, float> _cold = new Dictionary<Cookware, float>();
        private readonly Dictionary<Cookware, int> _shownPct = new Dictionary<Cookware, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_inst != null) return;
            var go = new GameObject("IceCreamMelt (auto)");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<IceCreamMelt>();
        }

        private void OnEnable() { FoodFreeze.Chilled += OnChilled; }
        private void OnDisable() { FoodFreeze.Chilled -= OnChilled; }

        private bool Resolve()
        {
            if (_resolved) return _ice != null;
            FoodDatabase db = FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return false;
            _resolved = true;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Table.GetProduct((ushort)id);
                if (p == null || string.IsNullOrEmpty(p.Title)) continue;
                string t = p.Title.Trim().ToLowerInvariant();
                if (t == "мороженое") { _ice = p; break; }
                if (_ice == null && t.Contains("морожен") && !t.Contains("смесь")) _ice = p;
            }
            if (Log) Debug.Log($"[Мороженое] таяние: продукт {(_ice != null ? "«" + _ice.Title + "»" : "НЕ НАЙДЕН (нужен «Мороженое»)")}");
            return _ice != null;
        }

        private void OnChilled(Cookware cw, float seconds)
        {
            if (cw == null || !Resolve()) return;
            _warm[cw] = 0f;
            int melted = cw.Bag.CountOf(_ice.Id, FoodState.Melted);
            if (melted <= 0) { _cold.Remove(cw); return; }
            float c;
            _cold.TryGetValue(cw, out c);
            c += seconds;
            if (c >= RefreezeSeconds)
            {
                c = 0f;
                Convert(cw, _ice.StartState == FoodState.Melted ? FoodState.Raw : _ice.StartState);
                if (Log) Debug.Log($"[Мороженое] «{cw.name}»: азот — растаявшее снова замёрзло", cw);
            }
            _cold[cw] = c;
        }

        private void Update()
        {
            _tick -= Time.deltaTime;
            if (_tick > 0f) return;
            const float step = 0.5f;
            _tick = step;
            if (!Resolve()) return;
            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null || !cw.IsServerInitialized) continue;
                int all = cw.CountOf(_ice.Id);
                int melted = cw.Bag.CountOf(_ice.Id, FoodState.Melted);
                if (all - melted <= 0) { _warm.Remove(cw); _shownPct.Remove(cw); continue; }
                float w;
                _warm.TryGetValue(cw, out w);
                w += step * (cw.IsHeating ? HotK : 1f);
                int pct = Mathf.Clamp(Mathf.FloorToInt(w / MeltSeconds * 4f) * 25, 0, 100);
                int shown;
                _shownPct.TryGetValue(cw, out shown);
                if (Log && pct > shown && pct < 100) Debug.Log($"[Мороженое] «{cw.name}»: тает {pct}%", cw);
                _shownPct[cw] = pct;
                if (w >= MeltSeconds)
                {
                    w = 0f;
                    Convert(cw, FoodState.Melted);
                    _shownPct.Remove(cw);
                    if (Log) Debug.Log($"[Мороженое] «{cw.name}»: растаяло — теперь льётся. Полей азотом, чтобы снова замёрзло", cw);
                }
                _warm[cw] = w;
            }
        }

        /// <summary>Всё мороженое в таре — в одно состояние (порций столько же).</summary>
        private void Convert(Cookware cw, FoodState to)
        {
            int n = cw.CountOf(_ice.Id);
            if (n <= 0) return;
            int took = cw.ServerTake(_ice.Id, n);
            if (took > 0) cw.ServerAddPinch(_ice.Id, to, took, 0, false);
        }
    }
}
