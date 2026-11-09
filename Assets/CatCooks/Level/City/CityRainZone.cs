using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ПОГОДНАЯ ЗОНА — красный модуль, где игрок проводит время. Коробка вокруг точки: центр = этот объект,
    /// размер = Size (ширина X, высота, глубина Z) в инспекторе. Объект можно сделать дочерним к модулю.
    /// Зона 1: Always Active — работает всегда. Зона 2 (второй дом, позже): Always Active выключить —
    /// оживает, когда камера входит в коробку (+ запас Enter Margin), и засыпает при выходе.
    /// Что делает зона сейчас:
    ///   • КАРТА УКРЫТИЙ: сетка лучей сверху вниз → высота первой поверхности (пол, навес, балка, крыша).
    ///     По ней дождь не идёт под крышей, всплески ложатся точно на поверхность, (дальше) сухо под навесами.
    ///     Нужны коллайдеры у этажа (они есть: по нему ходят). Пересчёт — сам, раз в Refresh Seconds.
    /// Дальше в зону добавятся лужи, ручейки, капли с краёв — тоже только в активных зонах.
    /// </summary>
    [ExecuteAlways]
    public class CityRainZone : MonoBehaviour
    {
        public static readonly List<CityRainZone> All = new List<CityRainZone>();

        public enum ZoneKind { Full = 0, Light = 1 }

        [Header("Вид зоны")]
        [Tooltip("Полная (красная): все эффекты погоды, карта укрытий, лужи. Лёгкая (жёлтая): рядом с этажом, эффекты слабее и дешевле, без карты укрытий. Вне всех зон осадков, мокрого, льда, града нет — только туман и облака")]
        public ZoneKind Kind = ZoneKind.Full;
        [Tooltip("Жёлтая зона: сила погоды внутри (0 — ничего, 1 — как в красной)")]
        [Range(0f, 1f)] public float LightStrength = 0.35f;
        [Tooltip("Жёлтая зона: облегчённый экранный проход — мокрое и снег без луж-отражений, льда, лап и града (сильно дешевле)")]
        public bool LiteScreenPass = true;
        [Tooltip("Плавный край зоны, единицы сцены: за коробкой погода затухает на такую ширину")]
        public float Fade = 40f;

        [Tooltip("Размер коробки вокруг объекта (единицы сцены): ширина X, высота, глубина Z")]
        public Vector3 Size = new Vector3(250f, 300f, 250f);
        [Tooltip("Зона 1 — всегда. Зона 2 — снять: оживает, когда игрок внутри")]
        public bool AlwaysActive = true;
        [Tooltip("Запас вокруг коробки для входа/выхода (единицы сцены)")]
        public float EnterMargin = 10f;

        [Header("Карта укрытий")]
        [Range(16, 256)] public int Resolution = 96;
        [Tooltip("Слои, которые закрывают от дождя (пол, крыши, навесы)")]
        public LayerMask Mask = ~0;
        [Tooltip("Пересчитывать каждые столько секунд (стройка меняет этаж). 0 — один раз")]
        public float RefreshSeconds = 8f;
        [Tooltip("Лучей за кадр (больше — быстрее пересчёт, дороже кадр)")]
        public int RaysPerFrame = 600;

        [Header("Мокрость")]
        [Tooltip("Точность карты мокрости (лужи, сухо под зонтом)")]
        [Range(32, 256)] public int WetResolution = 160;

        public bool IsActive { get; private set; }
        public Texture2D WetMap { get { return _wetTex; } }
        public float WetMax { get; private set; }

        private Texture2D _wetTex;
        private float[] _wet;
        private byte[] _wetBytes;
        private float _wetTimer;
        private readonly List<Vector4> _blk = new List<Vector4>();
        public Texture2D Occlusion { get { return _tex; } }
        public bool HasOcclusion { get { return _ready; } }

        private Texture2D _tex;
        private float[] _h, _work;
        private int _cursor;
        private bool _ready;
        private float _nextRefresh;

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }
        private void OnDestroy()
        {
            if (_tex != null) { if (Application.isPlaying) Destroy(_tex); else DestroyImmediate(_tex); }
            if (_wetTex != null) { if (Application.isPlaying) Destroy(_wetTex); else DestroyImmediate(_wetTex); }
        }

        /// <summary>
        /// Мокрость пола зоны: под открытым небом копится (rainRate в секунду), под зонтом/навесом — нет,
        /// после дождя сохнет (dryRate в секунду). cap — предел мокрости для силы дождя (морось — только тёмные капли). Шесть раз в секунду, на процессоре (дёшево).
        /// </summary>
        public void TickWet(float dt, float rainRate, float dryRate, float cap, List<CityRainBlocker> blockers)
        {
            int n = Mathf.Clamp(WetResolution, 32, 256);
            if (_wet == null || _wet.Length != n * n)
            {
                _wet = new float[n * n]; _wetBytes = new byte[n * n];
                if (_wetTex != null) { if (Application.isPlaying) Destroy(_wetTex); else DestroyImmediate(_wetTex); }
                _wetTex = new Texture2D(n, n, TextureFormat.R8, false, true);
                _wetTex.name = "RainWet_" + name;
                _wetTex.filterMode = FilterMode.Bilinear;
                _wetTex.wrapMode = TextureWrapMode.Clamp;
                _wetTex.hideFlags = HideFlags.HideAndDontSave;
                _wetTimer = 1f;
            }
            _wetTimer += dt;
            if (_wetTimer < 0.16f) return;
            float step = _wetTimer;
            _wetTimer = 0f;

            _blk.Clear();
            if (blockers != null)
                foreach (CityRainBlocker b in blockers)
                    if (b != null && b.Active && b.isActiveAndEnabled) _blk.Add(b.Packed);

            Bounds bb = WorldBounds;
            int on = _h != null ? Mathf.RoundToInt(Mathf.Sqrt(_h.Length)) : 0;
            float max = 0f;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    float w = _wet[i];
                    bool covered = false;
                    if (_blk.Count > 0)
                    {
                        float px = bb.min.x + (x + 0.5f) / n * bb.size.x, pz = bb.min.z + (z + 0.5f) / n * bb.size.z;
                        float h = -1e6f;
                        if (_ready && on > 0)
                        {
                            int ox = Mathf.Clamp((int)((x + 0.5f) / n * on), 0, on - 1), oz = Mathf.Clamp((int)((z + 0.5f) / n * on), 0, on - 1);
                            h = _h[oz * on + ox];
                        }
                        for (int k = 0; k < _blk.Count; k++)
                        {
                            Vector4 b = _blk[k];
                            float dx = px - b.x, dz = pz - b.z;
                            if (dx * dx + dz * dz < b.w * b.w && b.y > h) { covered = true; break; }
                        }
                    }
                    if (rainRate > 0f && !covered)
                    {
                        if (w < cap) w = Mathf.Min(cap, w + rainRate * step);       // копится до предела этого дождя
                        else w = Mathf.Max(cap, w - dryRate * step);                // дождь ослаб — лужи уходят
                    }
                    else w -= dryRate * step * (covered && rainRate > 0f ? 0.3f : 1f);
                    w = Mathf.Clamp01(w);
                    _wet[i] = w;
                    _wetBytes[i] = (byte)(w * 255f);
                    if (w > max) max = w;
                }
            WetMax = max;
            _wetTex.LoadRawTextureData(_wetBytes);
            _wetTex.Apply(false, false);
        }

        /// <summary>Намочить/высушить всю зону сразу (кнопки в меню P).</summary>
        public void SetAllWet(float w)
        {
            if (_wet == null) return;
            for (int i = 0; i < _wet.Length; i++) _wet[i] = Mathf.Clamp01(w);
            _wetTimer = 1f;
        }

        public Bounds WorldBounds { get { return new Bounds(transform.position, Size); } }

        /// <summary>Прямоугольник карты по XZ: (minX, minZ, 1/sizeX, 1/sizeZ).</summary>
        public Vector4 OccRect
        {
            get
            {
                Bounds b = WorldBounds;
                return new Vector4(b.min.x, b.min.z, 1f / Mathf.Max(b.size.x, 1e-3f), 1f / Mathf.Max(b.size.z, 1e-3f));
            }
        }

        public bool Contains(Vector3 p, float margin)
        {
            Bounds b = WorldBounds;
            b.Expand(margin * 2f);
            return b.Contains(p);
        }

        /// <summary>Активность по камере (зона 2 оживает при входе).</summary>
        public void UpdateActive(Vector3 cam)
        {
            IsActive = Kind == ZoneKind.Light || AlwaysActive || Contains(cam, EnterMargin);
        }

        /// <summary>Шаг пересчёта карты укрытий (вызывает CityRain, пока зона в фокусе).</summary>
        public void TickOcclusion()
        {
            int n = Mathf.Clamp(Resolution, 16, 256);
            if (_h == null || _h.Length != n * n)
            {
                _h = new float[n * n]; _work = new float[n * n];
                for (int i = 0; i < _h.Length; i++) _h[i] = -1e6f;
                _cursor = 0; _ready = false;
                if (_tex != null) { if (Application.isPlaying) Destroy(_tex); else DestroyImmediate(_tex); }
                _tex = new Texture2D(n, n, TextureFormat.RFloat, false, true);
                _tex.name = "RainOcclusion_" + name;
                _tex.filterMode = FilterMode.Point;
                _tex.wrapMode = TextureWrapMode.Clamp;
                _tex.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_cursor >= _work.Length)
            {
                if (RefreshSeconds <= 0f && _ready) return;
                if (Time.realtimeSinceStartup < _nextRefresh) return;
                _cursor = 0;
            }
            Bounds b = WorldBounds;
            float top = b.max.y + 1f, len = b.size.y + 2f;
            int end = Mathf.Min(_work.Length, _cursor + Mathf.Max(32, RaysPerFrame));
            for (; _cursor < end; _cursor++)
            {
                int x = _cursor % n, z = _cursor / n;
                Vector3 o = new Vector3(b.min.x + (x + 0.5f) / n * b.size.x, top, b.min.z + (z + 0.5f) / n * b.size.z);
                RaycastHit hit;
                _work[_cursor] = Physics.Raycast(o, Vector3.down, out hit, len, Mask, QueryTriggerInteraction.Ignore) ? hit.point.y : -1e6f;
            }
            if (_cursor >= _work.Length)
            {
                System.Array.Copy(_work, _h, _h.Length);
                Color[] px = new Color[_h.Length];
                for (int i = 0; i < px.Length; i++) px[i] = new Color(_h[i], 0f, 0f, 1f);
                _tex.SetPixels(px);
                _tex.Apply(false, false);
                _ready = true;
                _nextRefresh = Time.realtimeSinceStartup + Mathf.Max(0.5f, RefreshSeconds);
            }
        }

        public Color GizmoColor
        {
            get
            {
                if (Kind == ZoneKind.Light) return new Color(1f, 0.92f, 0.15f, 1f);
                return AlwaysActive ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(1f, 0.65f, 0.15f, 1f);
            }
        }

        // Заливка + толстая рамка: зону видно издалека и по ней можно кликнуть, чтобы выбрать
        private void OnDrawGizmos()
        {
            Color c = GizmoColor;
            Gizmos.color = new Color(c.r, c.g, c.b, 0.06f);
            Gizmos.DrawCube(transform.position, Size);
            Gizmos.color = new Color(c.r, c.g, c.b, 0.9f);
            Gizmos.DrawWireCube(transform.position, Size);
            Gizmos.DrawWireCube(transform.position, Size * 1.002f);
            Bounds b = WorldBounds;   // «пол» зоны — откуда считается дождь вниз
            Gizmos.color = new Color(c.r, c.g, c.b, 0.18f);
            Gizmos.DrawCube(new Vector3(b.center.x, b.min.y, b.center.z), new Vector3(b.size.x, 0.01f, b.size.z));
        }

        private void OnDrawGizmosSelected()
        {
            Color c = GizmoColor;
            Gizmos.color = new Color(c.r, c.g, c.b, 0.12f);
            Gizmos.DrawCube(transform.position, Size);
            if (!AlwaysActive)
            {
                Gizmos.color = new Color(c.r, c.g, c.b, 0.35f);
                Gizmos.DrawWireCube(transform.position, Size + Vector3.one * EnterMargin * 2f);
            }
        }
    }
}
