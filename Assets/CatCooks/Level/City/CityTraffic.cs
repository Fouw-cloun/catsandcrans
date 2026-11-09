using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// МАШИНЫ В ДВИЖЕНИИ — примитивно: по кругу вокруг квартала (своя половина дороги, бордюр справа),
    /// со скругленными поворотами на углах. У всех машин круга одна скорость и ровный шаг — не догоняют и не сталкиваются;
    /// круги соседних кварталов идут по своим половинам дороги — встречное движение. Ставит «Пропсы и машины».
    /// Едут только в игре (или в редакторе с галкой «Ехать в редакторе»).
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("CatCooks/Город/Машины по кругу квартала")]
    public sealed class CityTraffic : MonoBehaviour
    {
        [Tooltip("Участок квартала (оси модуля)")]
        public float X0, Z0, X1, Z1;
        [Tooltip("Отступ полосы от бордюра")]
        public float Lane = 10f;
        [Tooltip("Скорость, единиц сцены в секунду")]
        public float Speed = 20f;
        public bool PreviewInEditor = false;
        [Tooltip("Ехать в обратную сторону (овал-разворот вдоль одной улицы — правостороннее движение)")]
        public bool Reverse = false;
        public List<Transform> Cars = new List<Transform>();
        public List<float> Dist = new List<float>();
        public List<float> YawOffset = new List<float>();

        public float Perimeter()
        {
            return 2f * (X1 - X0) + 2f * (Z1 - Z0) + 2f * Mathf.PI * Lane;
        }

        /// <summary>Точка и направление на круге по пройденному пути s (по часовой, если смотреть сверху).</summary>
        public void Eval(float s, out Vector2 pos, out Vector2 dir)
        {
            float W = X1 - X0, D = Z1 - Z0, A = 0.5f * Mathf.PI * Lane, L = Lane;
            float P = Perimeter();
            s = Mathf.Repeat(s, Mathf.Max(P, 1e-3f));
            if (s < W) { pos = new Vector2(X1 - s, Z0 - L); dir = new Vector2(-1, 0); return; }
            s -= W;
            if (s < A) { Arc(X0, Z0, -90f, s, out pos, out dir); return; }
            s -= A;
            if (s < D) { pos = new Vector2(X0 - L, Z0 + s); dir = new Vector2(0, 1); return; }
            s -= D;
            if (s < A) { Arc(X0, Z1, 180f, s, out pos, out dir); return; }
            s -= A;
            if (s < W) { pos = new Vector2(X0 + s, Z1 + L); dir = new Vector2(1, 0); return; }
            s -= W;
            if (s < A) { Arc(X1, Z1, 90f, s, out pos, out dir); return; }
            s -= A;
            if (s < D) { pos = new Vector2(X1 + L, Z1 - s); dir = new Vector2(0, -1); return; }
            s -= D;
            Arc(X1, Z0, 0f, Mathf.Min(s, A), out pos, out dir);
        }

        private void Arc(float cx, float cz, float startDeg, float s, out Vector2 pos, out Vector2 dir)
        {
            float a = (startDeg - s / Mathf.Max(Lane, 1e-3f) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            pos = new Vector2(cx + Mathf.Cos(a) * Lane, cz + Mathf.Sin(a) * Lane);
            dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
        }

        public void Apply()
        {
            for (int i = 0; i < Cars.Count; i++)
            {
                if (Cars[i] == null) continue;
                Vector2 p, d;
                float s = i < Dist.Count ? Dist[i] : 0f;
                Eval(Reverse ? Perimeter() - s : s, out p, out d);
                if (Reverse) d = -d;
                Cars[i].localPosition = new Vector3(p.x, 0f, p.y);
                Cars[i].localRotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + (i < YawOffset.Count ? YawOffset[i] : 0f), 0f);
            }
        }

        private void Update()
        {
            if (!Application.isPlaying && !PreviewInEditor) return;
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            float P = Perimeter();
            for (int i = 0; i < Dist.Count; i++) Dist[i] = Mathf.Repeat(Dist[i] + Speed * dt, Mathf.Max(P, 1e-3f));
            Apply();
        }
    }
}
