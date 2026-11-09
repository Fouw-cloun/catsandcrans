using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ЭТО МОКНЕТ ПОД ДОЖДЁМ — метка для модулей этажа (пол, крыши). Мокрость, тёмные капли и лужи рисуются
    /// только на отмеченных объектах (если на City Rain включено Wet Only Marked). Остальное — сухое, пока не решим иначе.
    /// Как работает: ставит рендерерам (свой + дочерние) бит слоя рендера №12, проход мокрости рисует маску по этому биту.
    /// Exclude — дочерние рендереры, которым мокнуть не надо (пропы, мебель).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CityWetSurface : MonoBehaviour
    {
        /// <summary>Номер бита Rendering Layer «мокнет».</summary>
        public const int Bit = 12;
        public static uint LayerMask { get { return 1u << Bit; } }
        public static readonly List<CityWetSurface> All = new List<CityWetSurface>();

        [Tooltip("Мокнут и дочерние объекты (весь модуль)")]
        public bool IncludeChildren = true;
        [Tooltip("Эти рендереры не мокнут (пропы, мебель внутри модуля)")]
        public List<Renderer> Exclude = new List<Renderer>();

        private static readonly List<Renderer> _tmp = new List<Renderer>();

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            Apply(true);
        }

        private void OnDisable()
        {
            All.Remove(this);
            if (!enabled) Apply(false);          // выключили компонент руками — снять метку; перезагрузка скриптов — не трогать
        }

        private void OnDestroy() { Apply(false); }

        private void OnValidate() { if (isActiveAndEnabled) Apply(true); }

        /// <summary>Заново пометить (если в модуль добавились объекты).</summary>
        [ContextMenu("Обновить метку")]
        public void Refresh()
        {
            Apply(false);
            Apply(true);
            Debug.Log("[Дождь] «" + name + "»: мокнут рендереров — " + CountMarked(), this);
        }

        public int CountMarked()
        {
            Collect();
            int n = 0;
            for (int i = 0; i < _tmp.Count; i++) if (_tmp[i] != null && (_tmp[i].renderingLayerMask & LayerMask) != 0) n++;
            return n;
        }

        private void Collect()
        {
            _tmp.Clear();
            if (IncludeChildren) GetComponentsInChildren<Renderer>(true, _tmp);
            else GetComponents<Renderer>(_tmp);
        }

        private void Apply(bool on)
        {
            Collect();
            uint bit = LayerMask;
            for (int i = 0; i < _tmp.Count; i++)
            {
                Renderer r = _tmp[i];
                if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                bool want = on && (Exclude == null || !Exclude.Contains(r));
                uint m = r.renderingLayerMask;
                uint nm = want ? (m | bit) : (m & ~bit);
                if (nm != m) r.renderingLayerMask = nm;
            }
            _tmp.Clear();
        }
    }
}
