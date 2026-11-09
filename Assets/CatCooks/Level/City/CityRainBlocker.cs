using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// УКРЫТИЕ ОТ ДОЖДЯ — круглый купол (зонт, навес-гриб, тент). Под ним капли не идут, всплески бьют в купол,
    /// пол под ним не мокнет (карта мокрости зоны). Включается/выключается мгновенно (зонт раскрылся — сразу сухо).
    /// Центр — точка обода купола (Local Center), радиус — Radius. Купол выше обода на 0.28 радиуса.
    /// Ставится сам на CityUmbrella; можно повесить на любой навес руками.
    /// </summary>
    [ExecuteAlways]
    public class CityRainBlocker : MonoBehaviour
    {
        public static readonly List<CityRainBlocker> All = new List<CityRainBlocker>();

        [Tooltip("Обод купола в осях объекта")]
        public Vector3 LocalCenter = new Vector3(0f, 2f, 0f);
        [Tooltip("Радиус купола (единицы объекта)")]
        public float Radius = 1.5f;
        [Tooltip("Укрывает ли сейчас (зонт закрыт — нет)")]
        public bool Active = true;

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        public Vector3 WorldCenter { get { return transform.TransformPoint(LocalCenter); } }
        public float WorldRadius
        {
            get
            {
                Vector3 s = transform.lossyScale;
                return Radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            }
        }

        /// <summary>Для шейдеров: (x, высота обода, z, радиус).</summary>
        public Vector4 Packed
        {
            get { Vector3 c = WorldCenter; return new Vector4(c.x, c.y, c.z, WorldRadius); }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
            Vector3 c = WorldCenter;
            float r = WorldRadius;
            for (int i = 0; i < 32; i++)
            {
                float a0 = i / 32f * Mathf.PI * 2f, a1 = (i + 1) / 32f * Mathf.PI * 2f;
                Gizmos.DrawLine(c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r, c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r);
            }
        }
    }
}
