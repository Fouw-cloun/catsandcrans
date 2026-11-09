using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// МИКСЕР И ШОКОЛАД (зовёт MixerItem, когда шаг рецепта не идёт).
    ///
    /// Сервер (ServerMix):
    ///   • жидкая паста в НЕгорячей таре — «темперирование»: мешаешь — застывает в Temper Speed раз быстрее,
    ///     в логе сколько осталось;
    ///   • паста в горячей таре — мешается, но не застынет, пока тара не остынет (лог подсказывает);
    ///   • застывший шоколад — венчик скребёт, ничего не происходит (лог: растопи горелкой).
    /// Клиенты (ClientStir): брызги пасты от венчика (жидкая), крошки и стружка (застывший);
    /// форма для шоколада — рябь, закрутка и покачивание пасты (ChocolateMold.ClientStir).
    /// </summary>
    public static class ChocolateWork
    {
        /// <summary>Во сколько раз быстрее застывает паста, пока её мешают (1 — не ускорять).</summary>
        public static float TemperSpeed = 3f;
        public static bool Log = true;

        private static readonly Dictionary<Cookware, float> _logAt = new Dictionary<Cookware, float>();
        private static readonly Dictionary<Cookware, float> _fxAcc = new Dictionary<Cookware, float>();

        private static bool Say(Cookware cw, float every)
        {
            if (!Log || cw == null) return false;
            if (_logAt.TryGetValue(cw, out float t) && Time.time < t) return false;
            _logAt[cw] = Time.time + every;
            return true;
        }

        /// <summary>Что из шоколада в таре: жидкая паста (застывает) и твёрдое (тает).</summary>
        public static void Count(Cookware cw, out ProductDef paste, out int pasteN, out ProductDef solid, out int solidN)
        {
            paste = null; solid = null; pasteN = 0; solidN = 0;
            FoodDatabase db = FoodDatabase.Instance;
            if (cw == null || db == null) return;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count <= 0) continue;
                ProductDef p = db.Get(v.Product);
                if (p == null) continue;
                if (p.HardensInto != null) { paste = p; pasteN += v.Count; }
                else if (FoodHardening.MeltsInto(v.Product) != null) { solid = p; solidN += v.Count; }
            }
        }

        private static bool HasGroundCocoa(Cookware cw, out int n)
        {
            n = 0;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return false;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                ProductDef p = v.Count > 0 ? db.Get(v.Product) : null;
                if (p == null || string.IsNullOrEmpty(p.Title)) continue;
                string t = p.Title.ToLowerInvariant().Replace('ё', 'е');
                if (t.Contains("тертое какао")) n += v.Count;
            }
            return n > 0;
        }

        /// <summary>Миксер крутится в таре dt секунд (сервер).</summary>
        public static void ServerMix(Cookware cw, float dt)
        {
            if (cw == null || dt <= 0f) return;
            Count(cw, out ProductDef paste, out int pn, out ProductDef solid, out int sn);
            if (pn <= 0 && sn <= 0)
            {
                // Миксер крутит какао с сахаром и маслом, а паста не идёт — сказать почему (что не сходится).
                if (HasGroundCocoa(cw, out int cocoa) && Say(cw, 3f))
                {
                    CookwareJournal j = CookwareJournal.Peek(cw);
                    string why = j != null && !string.IsNullOrEmpty(j.Hint) ? j.Hint : "рецепт «Шоколадная паста» не найден — CatCooks → Шоколад → 1";
                    Debug.Log($"[Шоколад] «{cw.name}»: миксер крутит, но паста не получается — {why}. " +
                              $"Нужно на каждые 12 какао (один боб): Сахар 1 мастерок + Сливочное масло 1 мастерок (по 2 порции), больше ничего (F4 — что внутри)", cw);
                }
                return;
            }

            if (pn > 0)
            {
                if (FoodHardening.IsHot(cw, out string why))
                {
                    if (Say(cw, 4f))
                        Debug.Log($"[Шоколад] «{cw.name}»: мешаешь горячую пасту ({why}) — гладкая и жидкая; " +
                                  $"застывать начнёт ниже {FoodHardening.MeltFromC:0} °C. Мешай, пока остывает, — схватится быстрее", cw);
                    return;
                }
                bool boosted = FoodHardening.ServerBoost(cw, paste.Id, dt * Mathf.Max(0f, TemperSpeed - 1f));
                if (boosted && Say(cw, 2f))
                {
                    float left = FoodHardening.LeftOf(cw, paste);
                    Debug.Log($"[Шоколад] «{cw.name}»: темперируешь — паста ×{pn} густеет в {TemperSpeed:0.#} раза быстрее" +
                              (left >= 0f ? $", застынет через {left:0} с" : ""), cw);
                }
                return;
            }
            if (Say(cw, 4f))
                Debug.Log($"[Шоколад] «{cw.name}»: {solid.Title} ×{sn} застыл — венчик только скребёт. " +
                          $"Растопи горелкой или на огне (≥ {FoodHardening.MeltFromC:0} °C)", cw);
        }

        /// <summary>Венчик в таре (у всех): брызги / крошки, форме — рябь.</summary>
        public static void ClientStir(Cookware cw, Vector3 whisk, float dt)
        {
            if (cw == null || dt <= 0f) return;
            Count(cw, out ProductDef paste, out int pn, out ProductDef solid, out int sn);
            if (pn <= 0 && sn <= 0) return;

            ChocolateMold mold = cw.GetComponent<ChocolateMold>();
            if (mold != null) mold.ClientStir(whisk, sn > pn);

            _fxAcc.TryGetValue(cw, out float acc);
            acc += dt * (sn > pn ? 5f : 7f);
            if (acc < 1f) { _fxAcc[cw] = acc; return; }
            acc -= 1f;
            _fxAcc[cw] = acc;

            Vector3 at = whisk;
            CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>();
            if (lv != null && lv.TryGetSurfaceY(whisk, out float y)) at.y = y + 0.01f;
            Vector2 r = Random.insideUnitCircle.normalized;
            Vector3 dir = new Vector3(r.x, 1.3f, r.y).normalized;
            if (sn > pn)
            {
                // застывший: крошки и стружка, сухо
                Color c = solid != null ? solid.BaseColor : new Color(0.25f, 0.13f, 0.07f);
                CCRuntimeParticles.Burst(at, dir, c, Color.Lerp(c, Color.black, 0.25f), 2, CCRuntimeParticles.Look.Chips.Scaled(0.35f));
            }
            else
            {
                // жидкая паста: густые брызги-капли
                Color c = paste != null ? paste.BaseColor : new Color(0.43f, 0.24f, 0.13f);
                CCRuntimeParticles.Burst(at, dir, c, Color.Lerp(c, new Color(0.2f, 0.1f, 0.05f), 0.4f), 3, CCRuntimeParticles.Look.Gunk.Scaled(0.3f));
            }
        }
    }
}
