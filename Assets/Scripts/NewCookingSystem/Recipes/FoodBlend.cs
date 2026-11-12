using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v19: СМЕСЬ ПО ПРОПОРЦИЯМ — для рецептов «перемешать», у результата которых в профиле вещества
    /// стоит галка Blend (Шоколадная паста).
    ///
    /// Вместо «сошлось / не сошлось» считаются ДОЛИ компонентов (основа + ингредиенты рецепта):
    ///   • отклонение в пределах допуска рецепта (Tolerance, у пасты 25%) — нормальная паста,
    ///     хуже только оценка (в консоли), вид тот же;
    ///   • сильнее — паста получается ИСПОРЧЕННОЙ (состояние «Испорчено»): видно пятна, её так же
    ///     черпают мастерком и лопатой, она липнет к таре;
    ///   • ИСПРАВИТЬ: досыпать недостающее (в консоли — сколько) и перемешать снова — доли пересчитаются
    ///     вместе с уже готовой массой, и если вошли в допуск — масса станет нормальной.
    ///
    /// Масса помнит свой состав в байте Cook: старшие 4 бита — какой компонент в переборе (номер+1),
    /// младшие — насколько (0..15 от Blend Max Excess). При досыпании той же массы байт усредняется —
    /// для одного и того же перебора это верно.
    /// </summary>
    public static class FoodBlend
    {
        public static bool Log = true;

        private struct Part { public ushort Id; public string Title; public float Need; }

        private static readonly Dictionary<Cookware, float> _progress = new Dictionary<Cookware, float>();
        private static readonly Dictionary<Cookware, int> _sig = new Dictionary<Cookware, int>();
        private static readonly Dictionary<Cookware, int> _pct = new Dictionary<Cookware, int>();

        /// <summary>Есть ли в таре смесь, которую надо считать по пропорциям (без продвижения). Сервер.</summary>
        public static bool Applies(Cookware cw)
        {
            CookRecipeDef r; Part[] parts; float[] have; int sig; bool fromMass;
            return Find(cw, out r, out parts, out have, out sig, out fromMass);
        }

        /// <summary>v19.1: для вида у всех игроков — идёт ли смесь по пропорциям, сколько секунд мешать, цвет результата.</summary>
        public static bool ClientInfo(Cookware cw, out float seconds, out Color resultColor)
        {
            seconds = 6f; resultColor = Color.white;
            CookRecipeDef r; Part[] parts; float[] have; int sig; bool fromMass;
            if (!Find(cw, out r, out parts, out have, out sig, out fromMass)) return false;
            seconds = r.Steps.Count > 0 ? Mathf.Max(0.5f, r.Steps[0].Seconds) : 6f;
            resultColor = FoodColors.Of(r.Result, r.Result.StartState);
            return true;
        }

        /// <summary>Миксер/отбойник мешает тару dt секунд. true — это наша смесь (шаг сделан тут). Сервер.</summary>
        public static bool ServerMix(Cookware cw, float dt)
        {
            if (cw == null || !cw.IsServerInitialized) return false;
            CookRecipeDef r; Part[] parts; float[] have; int sig; bool fromMass;
            if (!Find(cw, out r, out parts, out have, out sig, out fromMass)) return false;

            int old;
            if (!_sig.TryGetValue(cw, out old) || old != sig) { _sig[cw] = sig; _progress[cw] = 0f; _pct[cw] = -1; }
            float sec = r.Steps.Count > 0 ? Mathf.Max(0.5f, r.Steps[0].Seconds) : 6f;
            float p = _progress[cw] + Mathf.Max(0f, dt);
            _progress[cw] = p;
            int pct = Mathf.Clamp(Mathf.FloorToInt(p / sec * 4f) * 25, 0, 100);
            if (pct != _pct[cw])
            {
                _pct[cw] = pct;
                if (Log && pct > 0 && pct < 100) Debug.Log($"[Пропорции] «{Nm(cw)}»: {r.Title} — перемешиваю {pct}%", cw);
            }
            if (p < sec) return true;
            Finish(cw, r, parts, have);
            _progress[cw] = 0f;
            _sig[cw] = -1;
            return true;
        }

        private static string Nm(Cookware cw) => cw.Def != null ? cw.Def.Title : cw.name;

        /// <summary>Подходит ли содержимое тары под какой-нибудь рецепт с пропорциями.</summary>
        private static bool Find(Cookware cw, out CookRecipeDef rec, out Part[] parts, out float[] have, out int sig, out bool fromMass)
        {
            rec = null; parts = null; have = null; sig = 0; fromMass = false;
            if (cw == null || cw.IsEmpty) return false;
            FoodDatabase db = FoodDatabase.Instance;
            foreach (CookRecipeDef r in CookRecipeBook.Recipes)
            {
                if (r == null || r.Driver != CookDriver.Book || r.Result == null || r.Base == null) continue;
                if (r.Steps.Count != 1 || r.Steps[0].Kind != CookStepKind.Mix) continue;
                FoodSubstance sub = FoodSubstance.Of(r.Result, FoodState.None);
                if (sub == null || !sub.Blend) continue;

                var list = new List<Part>();
                list.Add(new Part { Id = r.Base.Id, Title = r.Base.Title, Need = Mathf.Max(1, FoodUnits.ToPortions(r.BaseAmount, r.BaseUnit)) });
                foreach (CookRecipeDef.Ingredient ing in r.Ingredients)
                    if (ing.Product != null) list.Add(new Part { Id = ing.Product.Id, Title = ing.Product.Title, Need = Mathf.Max(1, ing.ToPortions(ing.Amount)) });
                Part[] ps = list.ToArray();
                float total = 0f;
                foreach (Part q in ps) total += q.Need;
                float[] F = new float[ps.Length];
                for (int i = 0; i < ps.Length; i++) F[i] = ps[i].Need / total;

                float[] h = new float[ps.Length];
                bool foreign = false, mass = false;
                int raw = 0, s = 17;
                bool[] rawSeen = new bool[ps.Length];
                for (int v = 0; v < cw.ViewCount; v++)
                {
                    BagView bv = cw.GetView(v);
                    if (bv.Count == 0) continue;
                    s = s * 31 + bv.Product * 7 + bv.Count * 13 + (int)bv.State;
                    int idx = -1;
                    for (int i = 0; i < ps.Length; i++) if (ps[i].Id == bv.Product) { idx = i; break; }
                    if (idx >= 0) { h[idx] += bv.Count; if (!rawSeen[idx]) { rawSeen[idx] = true; raw++; } continue; }
                    if (bv.Product == r.Result.Id)
                    {
                        mass = true;
                        float[] f = Decode(bv.Cook, F, sub.BlendMaxExcess);
                        for (int i = 0; i < ps.Length; i++) h[i] += bv.Count * f[i];
                        continue;
                    }
                    foreign = true; break;
                }
                if (foreign) continue;
                // мешать есть что: два компонента, или готовая масса + досыпали компонент
                if (raw >= 2 || (mass && raw >= 1))
                {
                    rec = r; parts = ps; have = h; sig = s; fromMass = mass;
                    return true;
                }
            }
            return false;
        }

        private static float[] Decode(byte cook, float[] F, float maxE)
        {
            float[] f = (float[])F.Clone();
            int type = cook >> 4, mag = cook & 15;
            if (type <= 0 || type > F.Length) return f;
            int k = type - 1;
            float e = mag / 15f * maxE;
            float fk = F[k] + e * (1f - F[k]);
            float scale = (1f - fk) / Mathf.Max(1e-4f, 1f - F[k]);
            for (int i = 0; i < f.Length; i++) f[i] = i == k ? fk : F[i] * scale;
            return f;
        }

        private static void Finish(Cookware cw, CookRecipeDef r, Part[] parts, float[] have)
        {
            FoodSubstance sub = FoodSubstance.Of(r.Result, FoodState.None);
            float maxE = sub != null ? sub.BlendMaxExcess : 0.6f;
            float T = 0f, needT = 0f;
            for (int i = 0; i < have.Length; i++) { T += have[i]; needT += parts[i].Need; }
            if (T < 0.5f) return;
            float worst = 0f, devSum = 0f;
            int kEx = -1; float eBest = 0f;
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                float F = parts[i].Need / needT, f = have[i] / T;
                float dev = (f - F) / F;
                worst = Mathf.Max(worst, Mathf.Abs(dev));
                devSum += Mathf.Abs(dev);
                float e = (f - F) / Mathf.Max(1e-4f, 1f - F);
                if (e > eBest) { eBest = e; kEx = i; }
                sb.Append($"{parts[i].Title} {f * 100f:0}% (норма {F * 100f:0}%); ");
            }
            float score = Mathf.Clamp(100f * (1f - devSum / parts.Length), 0f, 100f);
            bool spoiled = worst > Mathf.Max(0.05f, r.Tolerance);
            int count = Mathf.Max(1, Mathf.RoundToInt(T));
            byte cook = 0;
            if (kEx >= 0 && eBest > 0.005f)
                cook = (byte)((kEx + 1) * 16 + Mathf.Clamp(Mathf.RoundToInt(eBest / maxE * 15f), 1, 15));
            FoodState state = spoiled ? FoodState.Spoiled : r.Result.StartState;

            cw.ServerClear();
            cw.ServerAddContent(r.Result.Id, state, count, cook, false);

            if (!Log) return;
            if (!spoiled)
                Debug.Log($"[Пропорции] «{Nm(cw)}»: {r.Title} ×{count} готово — {sb}отклонение {worst * 100f:0}% (допуск {r.Tolerance * 100f:0}%), оценка {score:0}", cw);
            else
            {
                // что досыпать, чтобы войти в норму: перебор держим, остальное догоняем
                var fix = new StringBuilder();
                if (kEx >= 0)
                {
                    float Fk = parts[kEx].Need / needT;
                    float T2 = have[kEx] / Mathf.Max(1e-4f, Fk);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (i == kEx) continue;
                        float add = parts[i].Need / needT * T2 - have[i];
                        if (add >= 0.5f) fix.Append($"{parts[i].Title} +{Mathf.CeilToInt(add)} порц. ({Mathf.CeilToInt(add / FoodUnits.Trowel)} мастерк.); ");
                    }
                }
                Debug.LogWarning($"[Пропорции] «{Nm(cw)}»: {r.Title} ×{count} — ИСПОРЧЕНО: {sb}отклонение {worst * 100f:0}% (допуск {r.Tolerance * 100f:0}%)" +
                                 (kEx >= 0 ? $", перебор: {parts[kEx].Title}" : "") +
                                 (fix.Length > 0 ? $". Исправить: досыпь {fix}и перемешай снова" : ""), cw);
            }
        }
    }
}
