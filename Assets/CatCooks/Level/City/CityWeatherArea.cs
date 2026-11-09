using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ЗОНЫ ПОГОДЫ — общий расчёт. Красные зоны (CityRainZone, вид «Полная») — вся погода. Жёлтые (вид «Лёгкая») — слабее и дешевле.
    /// Вне всех зон осадков, мокрого, льда, града и жары нет (туман и облака остаются). Зон нет — погода везде, как раньше.
    /// Раз в кадр пишет в шейдеры коробки зон (_CC_WxBox, _CC_WxInfo, _CC_WxN) и силу погоды у камеры (_CC_WxCam).
    /// Дождь, снег, град и ветер берут отсюда CamK (сила у камеры, 0..1) и CamFull (доля полной, 0..1).
    /// </summary>
    public static class CityWeatherArea
    {
        public const int Max = 8;

        /// <summary>Сила погоды у камеры: 0 — вне зон, жёлтая — её сила, красная — 1.</summary>
        public static float CamK { get; private set; }
        /// <summary>Доля полной (красной) погоды у камеры: 0 — жёлтая или вне зон, 1 — внутри красной.</summary>
        public static float CamFull { get; private set; }
        /// <summary>Есть ли зоны (нет — погода везде).</summary>
        public static bool HasZones { get; private set; }

        private static int _frame = -1;
        private static readonly Vector4[] Box = new Vector4[Max];
        private static readonly Vector4[] Info = new Vector4[Max];
        private static readonly float[] Margin = new float[Max];
        private static int _n;
        private static bool _lite;

        static readonly int IdBox = Shader.PropertyToID("_CC_WxBox");
        static readonly int IdInfo = Shader.PropertyToID("_CC_WxInfo");
        static readonly int IdN = Shader.PropertyToID("_CC_WxN");
        static readonly int IdCam = Shader.PropertyToID("_CC_WxCam");
        static readonly int IdLite = Shader.PropertyToID("_CC_WxLite");

        static CityWeatherArea() { CamK = 1f; CamFull = 1f; }

        /// <summary>Обновить (раз в кадр; повторные вызовы в том же кадре ничего не делают).</summary>
        public static void Refresh()
        {
            if (Application.isPlaying && Time.frameCount == _frame) return;
            _frame = Time.frameCount;

            _n = 0; _lite = false;
            for (int i = 0; i < Max; i++) { Box[i] = Vector4.zero; Info[i] = Vector4.zero; Margin[i] = 0f; }
            foreach (CityRainZone z in CityRainZone.All)
            {
                if (z == null || !z.isActiveAndEnabled || _n >= Max) continue;
                Bounds b = z.WorldBounds;
                bool light = z.Kind == CityRainZone.ZoneKind.Light;
                Box[_n] = new Vector4(b.center.x, b.center.z, b.extents.x, b.extents.z);
                Info[_n] = new Vector4(light ? 2f : 1f, light ? z.LightStrength : 1f, Mathf.Max(1f, z.Fade), 0f);
                Margin[_n] = Mathf.Max(0f, z.EnterMargin);
                if (light && z.LiteScreenPass) _lite = true;
                _n++;
            }
            HasZones = _n > 0;

            Camera cam = Camera.main;
            if (!HasZones || cam == null) { CamK = 1f; CamFull = 1f; }
            else
            {
                Vector3 p = cam.transform.position;
                float k, f;
                Eval(p.x, p.z, true, out k, out f);
                CamK = k; CamFull = f;
            }

            Shader.SetGlobalVectorArray(IdBox, Box);
            Shader.SetGlobalVectorArray(IdInfo, Info);
            Shader.SetGlobalFloat(IdN, _n);
            Shader.SetGlobalVector(IdCam, new Vector4(CamK, CamFull, 0f, 0f));
            Shader.SetGlobalFloat(IdLite, _lite ? 1f : 0f);
        }

        /// <summary>Сила погоды и доля полной в точке (так же считает шейдер). withMargin — для камеры: коробка чуть шире (запас входа).</summary>
        public static void Eval(float x, float z, bool withMargin, out float k, out float full)
        {
            k = 0f; full = 0f;
            for (int i = 0; i < _n; i++)
            {
                float dx = Mathf.Abs(x - Box[i].x) - Box[i].z - (withMargin ? Margin[i] : 0f);
                float dz = Mathf.Abs(z - Box[i].y) - Box[i].w - (withMargin ? Margin[i] : 0f);
                float o = Mathf.Max(dx, dz);
                float t = Mathf.Clamp01(o / Mathf.Max(1e-3f, Info[i].z));
                float kk = 1f - t * t * (3f - 2f * t);
                if (Info[i].x < 1.5f) { k = Mathf.Max(k, kk); full = Mathf.Max(full, kk); }
                else k = Mathf.Max(k, kk * Info[i].y);
            }
        }

        /// <summary>Сила погоды в мировой точке (0..1).</summary>
        public static float At(Vector3 p)
        {
            if (!HasZones) return 1f;
            float k, f;
            Eval(p.x, p.z, false, out k, out f);
            return k;
        }
    }
}
