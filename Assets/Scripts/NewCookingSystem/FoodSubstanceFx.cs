using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v18: ЭФФЕКТЫ ВЕЩЕСТВ — по профилю <see cref="FoodSubstance"/>: пыль при зачерпывании,
    /// капли/крошки с края полотна, лёгкий пар, звуки. Только вид и звук: вес, количество
    /// и сам вид кучки не меняются. По сети не ходит — каждый игрок рисует у себя.
    /// </summary>
    public static class FoodSubstanceFx
    {
        /// <summary>Зачерпнули (из кучки или на полотно).</summary>
        public static void Scoop(FoodSubstance s, Vector3 at, Color c, float size)
        {
            if (s == null || !Application.isPlaying) return;
            if (s.DustOnScoop) CCPowderFx.Puff(at, Vector3.up, c, size, 0.7f * s.DustAmount);
            s.Play(s.ScoopSounds, at);
        }

        /// <summary>Досыпали / шлёпнули сверху (кучка выросла).</summary>
        public static void Drop(FoodSubstance s, Vector3 at, Color c, float size)
        {
            if (s == null || !Application.isPlaying) return;
            if (s.DustOnScoop) CCPowderFx.Puff(at, Vector3.up, c, size, 0.5f * s.DustAmount);
            s.Play(s.DropSounds, at);
        }

        /// <summary>Капля (масло) или крошка (порошок) с края полотна.</summary>
        public static void Drip(FoodSubstance s, Vector3 at, Vector3 vel, Color c, float size)
        {
            if (s == null || !Application.isPlaying) return;
            bool powder = s.Look == SubstanceLook.Powder;
            Color cc = c * Random.Range(0.85f, 1.05f);
            CCPowderFx.Crumb(at, vel, cc, size * Random.Range(0.7f, 1.2f), !powder || Random.value < 0.5f);
            if (powder && Random.value < 0.5f) CCPowderFx.Puff(at, Vector3.up, c, size * 2f, 0.4f * s.DustAmount);
        }

        /// <summary>Лёгкий пар над горячим.</summary>
        public static void Steam(Vector3 at, float size)
        {
            if (!Application.isPlaying) return;
            CCHeatFx.Wisp(at, Vector3.up * Random.Range(0.12f, 0.22f) + Random.insideUnitSphere * 0.03f,
                          Mathf.Max(0.04f, size) * Random.Range(0.5f, 0.8f), new Color(1f, 1f, 1f, 0.22f), Random.Range(1.2f, 1.8f));
        }
    }
}
