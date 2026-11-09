using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЗАСТЫВАНИЕ И ПЛАВЛЕНИЕ В ТАРЕ: шоколадная паста со временем становится шоколадом —
    /// а шоколад от жара (горелка, огонь) снова тает в пасту.
    ///
    /// Правило — в самом продукте: ProductDef → Hardens Into (во что) и Harden Seconds (через сколько).
    ///
    /// ЗАСТЫВАНИЕ. Отсчёт идёт с момента, как продукт появился в этой таре. Переложил пасту мастерком
    /// в другое ведро — там отсчёт начинается заново (успел — молодец). Пока тара ГОРЯЧАЯ — не застывает
    /// (отсчёт стоит на нуле). Застыло — порции становятся «твёрдым» продуктом (Solid ✔ — мастерком не взять).
    ///
    /// ПЛАВЛЕНИЕ (v1.8.2). Твёрдое — то, во что что-то застывает (Шоколад) — в ГОРЯЧЕЙ таре через
    /// MeltSeconds снова становится тем, из чего застыло (Шоколадная паста). Горячая тара:
    ///   • её Cookware Heat не ниже MeltFromC (ковш/кастрюля на костре, ковш под горелкой);
    ///   • или в неё прямо бьёт пламя горелки (форма для шоколада, ведро — без Cookware Heat).
    /// Если в таре собран рецепт «растопить» (шоколад + масло → шоколадная масса) — плавит рецепт, не мы.
    ///
    /// Вешать никуда не нужно: создаётся сам при запуске игры и работает только на сервере.
    /// </summary>
    public sealed class FoodHardening : MonoBehaviour
    {
        private static FoodHardening _inst;
        private readonly Dictionary<(Cookware, ushort), float> _since = new Dictionary<(Cookware, ushort), float>();
        private readonly Dictionary<(Cookware, ushort), float> _melt = new Dictionary<(Cookware, ushort), float>();
        private readonly List<(Cookware, ushort)> _gone = new List<(Cookware, ushort)>();
        private readonly Dictionary<ushort, ProductDef> _meltsInto = new Dictionary<ushort, ProductDef>();
        private readonly Dictionary<(Cookware, ushort), int> _lastPct = new Dictionary<(Cookware, ushort), int>();
        private readonly Dictionary<(Cookware, ushort), int> _lastMeltPct = new Dictionary<(Cookware, ushort), int>();
        private readonly HashSet<(Cookware, ushort)> _seen = new HashSet<(Cookware, ushort)>();
        private readonly HashSet<(Cookware, ushort)> _seenMelt = new HashSet<(Cookware, ushort)>();
        private readonly HashSet<Cookware> _hotLogged = new HashSet<Cookware>();
        private readonly Dictionary<(Cookware, ushort), float> _hotSaid = new Dictionary<(Cookware, ushort), float>();
        private float _next, _mapAt;

        public static bool Log = true;
        /// <summary>С какой температуры тары твёрдое тает (и паста перестаёт застывать), °C.</summary>
        public static float MeltFromC = 45f;
        /// <summary>Сколько секунд жара нужно, чтобы твёрдое полностью растаяло.</summary>
        public static float MeltSeconds = 5f;
        /// <summary>Паста меньше этой доли содержимого — она в смеси (тесто), не застывает.</summary>
        public static float MixedShare = 0.85f;
        private const float Tick = 0.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_inst != null) return;
            var go = new GameObject("FoodHardening (auto)") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<FoodHardening>();
        }

        /// <summary>Сколько секунд этот продукт уже лежит в таре (−1 — не отслеживается). Для HUD.</summary>
        public static float AgeOf(Cookware cw, ushort product)
            => _inst != null && _inst._since.TryGetValue((cw, product), out float t) ? Time.time - t : -1f;

        /// <summary>Сколько секунд твёрдое уже тает в таре (−1 — не тает). Для HUD.</summary>
        public static float MeltAgeOf(Cookware cw, ushort product)
            => _inst != null && _inst._melt.TryGetValue((cw, product), out float t) ? t : -1f;

        /// <summary>
        /// Ускорить застывание (миксер мешает остывающую пасту — «темперирование»): отсчёт продвигается
        /// на seconds вперёд. Горячую не ускоряет. Сервер. true — паста в таре и отсчёт идёт.
        /// </summary>
        public static bool ServerBoost(Cookware cw, ushort product, float seconds)
        {
            if (_inst == null || cw == null || seconds <= 0f) return false;
            var key = (cw, product);
            if (!_inst._since.TryGetValue(key, out float t0)) return false;
            _inst._since[key] = t0 - seconds;
            return true;
        }

        /// <summary>Сколько секунд пасте осталось до застывания (−1 — не отслеживается). Сервер.</summary>
        public static float LeftOf(Cookware cw, ProductDef paste)
        {
            if (paste == null || paste.HardenSeconds <= 0f) return -1f;
            float age = AgeOf(cw, paste.Id);
            return age < 0f ? -1f : Mathf.Max(0f, paste.HardenSeconds - age);
        }

        /// <summary>Во что тает этот продукт (null — не тает). Сервер/клиент — по базе продуктов.</summary>
        public static ProductDef MeltsInto(ushort solid)
        {
            if (_inst == null) return null;
            if (_inst._meltsInto.Count == 0 && FoodDatabase.Instance != null) _inst.RebuildMap(FoodDatabase.Instance);
            return _inst._meltsInto.TryGetValue(solid, out ProductDef p) ? p : null;
        }

        /// <summary>Горячая ли тара: её нагрев не ниже MeltFromC или в неё бьёт горелка. why — откуда жар.</summary>
        public static bool IsHot(Cookware cw, out string why)
        {
            why = null;
            if (cw == null) return false;
            CookwareHeat h = cw.GetComponent<CookwareHeat>();
            if (h != null && h.TemperatureC >= MeltFromC) { why = $"{h.TemperatureC:0} °C"; return true; }
            return JetHits(cw, out why);
        }

        /// <summary>Бьёт ли в тару пламя горелки (сопло → кончик пламени пересекает тару).</summary>
        public static bool JetHits(Cookware cw, out string why)
        {
            why = null;
            IReadOnlyList<IHeatJet> jets = HeatJets.All;
            if (cw == null || jets.Count == 0) return false;
            Bounds b;
            if (cw.InsideVolume != null) b = cw.InsideVolume.bounds;
            else
            {
                Renderer r = cw.GetComponentInChildren<Renderer>();
                if (r == null) return false;
                b = r.bounds;
            }
            b.Expand(0.12f);
            for (int i = 0; i < jets.Count; i++)
            {
                IHeatJet j = jets[i];
                if (j == null || (j is Object o && o == null) || !j.JetActive) continue;
                if (j.JetRoot != null && cw.transform.IsChildOf(j.JetRoot)) continue;
                Vector3 dir = j.JetDirection;
                if (dir.sqrMagnitude < 1e-6f) continue;
                float len = j.JetLength * 1.15f + j.JetRadius;
                Vector3 tip = j.JetOrigin + dir.normalized * len;
                if (b.Contains(j.JetOrigin) || b.Contains(tip) ||
                    (b.IntersectRay(new Ray(j.JetOrigin, dir), out float d) && d <= len))
                { why = "горелка"; return true; }
            }
            return false;
        }

        private void RebuildMap(FoodDatabase db)
        {
            _meltsInto.Clear();
            if (db == null || db.Table == null) return;
            for (int id = 1; id < 4096; id++)
            {
                ProductDef p = db.Get((ushort)id);
                if (p != null && p.HardensInto != null && !_meltsInto.ContainsKey(p.HardensInto.Id))
                    _meltsInto[p.HardensInto.Id] = p;
            }
        }

        /// <summary>
        /// В таре собран рецепт «растопить», который начинается с этого твёрдого (шоколад + масло) —
        /// тогда плавит рецепт (получится шоколадная масса), а не мы.
        /// </summary>
        private static bool MeltRecipeBusy(Cookware cw, ushort solid)
        {
            CookwareJournal j = CookwareJournal.Peek(cw);
            if (j != null && j.Recipe != null && j.StepIndex < j.Recipe.Steps.Count &&
                j.Recipe.Steps[j.StepIndex].Kind == CookStepKind.Melt) return true;
            IReadOnlyList<CookRecipeDef> rs = CookRecipeBook.Recipes;
            if (rs == null) return false;
            for (int i = 0; i < rs.Count; i++)
            {
                CookRecipeDef r = rs[i];
                if (r == null || r.Base == null || r.Base.Id != solid || r.Steps.Count == 0 || r.Steps[0].Kind != CookStepKind.Melt) continue;
                foreach (CookRecipeDef.Ingredient ing in r.Ingredients)
                {
                    if (ing.Product == null || ing.Product.Id == solid) continue;
                    if (cw.CountOf(ing.Product.Id) > 0 || (ing.Alt != null && cw.CountOf(ing.Alt.Id) > 0)) return true;
                }
            }
            return false;
        }

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + Tick;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            if (Time.time >= _mapAt || _meltsInto.Count == 0)
            {
                _mapAt = Time.time + 10f;
                RebuildMap(db);
            }

            _seen.Clear();
            _seenMelt.Clear();
            List<Cookware> all = Cookware.All;
            for (int c = 0; c < all.Count; c++)
            {
                Cookware cw = all[c];
                if (cw == null || !cw.IsServerInitialized || cw.ViewCount == 0) continue;
                bool hot = IsHot(cw, out string why);
                for (int i = 0; i < cw.ViewCount; i++)
                {
                    BagView v = cw.GetView(i);
                    if (v.Count <= 0) continue;
                    ProductDef p = db.Get(v.Product);
                    if (p == null) continue;
                    var key = (cw, v.Product);

                    // ── плавление: твёрдое в горячей таре ──
                    if (_meltsInto.TryGetValue(v.Product, out ProductDef liquid))
                    {
                        if (!hot)
                        {
                            // Отвёл горелку — таяние не обнуляется сразу, а медленно «откатывается».
                            if (_melt.TryGetValue(key, out float cool))
                            {
                                cool = Mathf.Max(0f, cool - Tick * 0.5f);
                                _melt[key] = cool;
                                if (cool > 0f) _seenMelt.Add(key);
                            }
                            continue;
                        }
                        if (MeltRecipeBusy(cw, v.Product))
                        {
                            if (Log && _hotLogged.Add(cw))
                                Debug.Log($"[Плавление] «{cw.name}»: {p.Title} не тает сам — в таре рецепт «растопить» (с маслом), им и займётся нагрев", cw);
                            continue;
                        }
                        _seenMelt.Add(key);
                        _melt.TryGetValue(key, out float m);
                        m += Tick;
                        _melt[key] = m;
                        int mp = Mathf.Clamp(Mathf.FloorToInt(m / MeltSeconds * 4f) * 25, 0, 100);
                        if (Log && (!_lastMeltPct.TryGetValue(key, out int lm) || lm != mp) && mp < 100)
                            Debug.Log($"[Плавление] «{cw.name}»: {p.Title} ×{v.Count} тает {mp}% (жар: {why}, ещё {Mathf.Max(0f, MeltSeconds - m):0.0} с)", cw);
                        _lastMeltPct[key] = mp;
                        if (m < MeltSeconds) continue;

                        int n = cw.ServerTake(v.Product, v.Count);
                        if (n > 0) cw.ServerAddPinch(liquid.Id, liquid.StartState, n, 0, false);
                        _melt.Remove(key);
                        _lastMeltPct.Remove(key);
                        if (Log) Debug.Log($"[Плавление] «{cw.name}»: {p.Title} ×{n} растаял → {liquid.Title} " +
                                           $"(берётся мастерком; остынет — снова застынет через {liquid.HardenSeconds:0} с)", cw);
                        break;                                   // вид тары поменялся — остальное в следующий раз
                    }

                    // ── застывание ──
                    if (p.HardensInto == null) continue;
                    _seen.Add(key);
                    // В смеси (паста с яичной массой, мукой — это уже тесто) и в бочке бетономешалки — не застывает.
                    if (cw.GetComponent<MixerDough>() != null || v.Count < cw.TotalCount * MixedShare)
                    {
                        if (Log && (!_hotSaid.TryGetValue(key, out float sm) || Time.time >= sm))
                        {
                            _hotSaid[key] = Time.time + 15f;
                            Debug.Log($"[Застывание] «{cw.name}»: {p.Title} ×{v.Count} " +
                                      (cw.GetComponent<MixerDough>() != null ? "в бетономешалке" : "в смеси с другим") + " — не застывает", cw);
                        }
                        _since[key] = Time.time;
                        _lastPct[key] = 0;
                        continue;
                    }
                    if (hot)
                    {
                        if (Log && _since.ContainsKey(key) && _lastPct.TryGetValue(key, out int was) && was > 0)
                            Debug.Log($"[Застывание] «{cw.name}»: {p.Title} снова горячая ({why}) — застывание сброшено", cw);
                        else if (Log && (!_hotSaid.TryGetValue(key, out float said) || Time.time >= said))
                        {
                            _hotSaid[key] = Time.time + 8f;
                            Debug.Log($"[Застывание] «{cw.name}»: {p.Title} ×{v.Count} горячая ({why}) — жидкая, бери мастерком; " +
                                      $"застынет, когда тара остынет ниже {MeltFromC:0} °C (+{p.HardenSeconds:0} с). Быстрее — переложи в форму", cw);
                        }
                        _since[key] = Time.time;                 // горячее не застывает
                        _lastPct[key] = 0;
                        continue;
                    }
                    if (!_since.TryGetValue(key, out float t0)) { _since[key] = Time.time; continue; }
                    float age = Time.time - t0;
                    int pct = Mathf.Clamp(Mathf.FloorToInt(age / p.HardenSeconds * 4f) * 25, 0, 100);
                    if (Log && (!_lastPct.TryGetValue(key, out int lp) || lp != pct) && pct > 0 && pct < 100)
                        Debug.Log($"[Застывание] «{cw.name}»: {p.Title} застывает {pct}% (ещё {p.HardenSeconds - age:0} с — успей переложить мастерком)", cw);
                    _lastPct[key] = pct;
                    if (age < p.HardenSeconds) continue;

                    int k = cw.ServerTake(v.Product, v.Count);
                    if (k > 0) cw.ServerAddPinch(p.HardensInto.Id, p.HardensInto.StartState, k, 0, false);
                    _since.Remove(key);
                    _lastPct.Remove(key);
                    if (Log) Debug.Log($"[Застывание] «{cw.name}»: {p.Title} ×{k} застыло → {p.HardensInto.Title}", cw);
                    break;                                       // вид тары поменялся — остальное в следующий раз
                }
                if (!hot) _hotLogged.Remove(cw);
            }

            // Продукт ушёл из тары (вычерпали) — отсчёт застывания забываем; остыло совсем — таяние забываем.
            _gone.Clear();
            foreach (var key in _since.Keys) if (!_seen.Contains(key)) _gone.Add(key);
            foreach (var key in _gone) { _since.Remove(key); _lastPct.Remove(key); }
            _gone.Clear();
            foreach (var key in _melt.Keys) if (!_seenMelt.Contains(key)) _gone.Add(key);
            foreach (var key in _gone)
            {
                if (Log && _melt.TryGetValue(key, out float m) && m > 0f && key.Item1 != null)
                    Debug.Log($"[Плавление] «{key.Item1.name}»: твёрдое остыло или вычерпано — таяние сброшено", key.Item1);
                _melt.Remove(key);
                _lastMeltPct.Remove(key);
            }
        }
    }
}
