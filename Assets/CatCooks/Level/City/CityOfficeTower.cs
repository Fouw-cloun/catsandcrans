using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// Небоскрёб-офис — как горят окна ночью (Mode):
    ///   • Частично (по умолчанию) — часть окон (доля — CityAtmosphere.OfficeLitShare от жилых), включаются и выключаются;
    ///   • Только полоса — горит верхняя полоса (корона), ниже редкие поздние окна (CityAtmosphere.OfficeLateRatio);
    ///   • Верх весь + частично — верхняя полоса горит вся, ниже частично (ориентиры, космическая башня).
    /// Жилые дома без этого компонента горят как обычно.
    /// Ставится сам при генерации на дома типа Tower; на готовый город — кнопка
    /// «Офисы: отметить небоскрёбы» в инспекторе застройки (без перегенерации).
    /// Можно добавить/снять руками на любой дом (на корень B_...).
    /// Данные идут в шейдеры CityBuilding / Window Glass через MaterialPropertyBlock (переставляется в OnEnable).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CityOfficeTower : MonoBehaviour
    {
        public enum NightMode { Partial = 0, TopBand = 1, TopAllPartial = 2, Windows = 3, Floors = 4 }

        [Tooltip("Частично — часть окон горит и переключается; Только полоса — горит верх; Верх весь + частично — ориентиры; " +
                 "Окна — отдельные окна вразнобой (+ верхние этажи); Этажи — целые этажи вокруг здания (+ верхние этажи)")]
        public NightMode Mode = NightMode.Partial;
        [Tooltip("Окна / Этажи: высота этажа, м")]
        [Min(1f)] public float FloorHeight = 3.5f;
        [Tooltip("Окна / Этажи: сколько верхних этажей горит всегда (0 — нет)")]
        [Range(0, 10)] public int TopFloors = 1;
        [Tooltip("Высота светящейся полосы — доля высоты здания")]
        [Range(0.02f, 0.5f)] public float BandShare = 0.1f;
        [Tooltip("Отступ полосы от крыши (доля высоты): 0 — под самой крышей")]
        [Range(0f, 0.3f)] public float TopInset = 0.01f;

        static readonly int IdOffice = Shader.PropertyToID("_CC_Office");
        static readonly int IdTop = Shader.PropertyToID("_CC_TopY");
        static readonly int IdBand = Shader.PropertyToID("_CC_BandH");
        static readonly int IdFloor = Shader.PropertyToID("_CC_FloorH");
        static readonly int IdMeter = Shader.PropertyToID("_CC_Meter");
        static MaterialPropertyBlock _mpb;

        void OnEnable() { Apply(); }
        void OnDisable() { Apply(false); }
        void OnValidate() { if (isActiveAndEnabled) Apply(); }

        /// <summary>Переставить данные (после перемещения/масштаба дома).</summary>
        public void Apply() { Apply(true); }

        void Apply(bool on)
        {
            var rs = GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return;
            Bounds b = new Bounds();
            bool has = false;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] is ParticleSystemRenderer || rs[i] is TrailRenderer || rs[i] is LineRenderer) continue;
                if (!has) { b = rs[i].bounds; has = true; } else b.Encapsulate(rs[i].bounds);
            }
            if (!has) return;
            float h = b.size.y;
            float top = b.max.y - h * TopInset;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (!Uses(r)) continue;
                r.GetPropertyBlock(_mpb);
                if (on)
                {
                    float m = Shader.GetGlobalFloat(IdMeter);
                    if (m <= 0f) m = 3.5f;
                    float fh = FloorHeight * m;
                    bool custom = Mode == NightMode.Windows || Mode == NightMode.Floors;
                    float code = Mode == NightMode.Partial ? 2f : Mode == NightMode.TopBand ? 1f : Mode == NightMode.TopAllPartial ? 3f : Mode == NightMode.Floors ? 4f : 5f;
                    _mpb.SetFloat(IdOffice, code);
                    _mpb.SetFloat(IdTop, custom ? b.max.y - h * TopInset : top);
                    _mpb.SetFloat(IdBand, custom ? TopFloors * fh : h * BandShare);
                    _mpb.SetFloat(IdFloor, fh);
                    r.SetPropertyBlock(_mpb);
                }
                else
                {
                    _mpb.Clear();
                    r.SetPropertyBlock(null);
                }
            }
        }

        static bool Uses(Renderer r)
        {
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
                if (ms[i] != null && ms[i].HasProperty(IdOffice)) return true;
            return false;
        }

        /// <summary>Офис ли это по габариту: высокий и узкий (как AutoKind → Tower).</summary>
        public static bool LooksLikeTower(GameObject building, float moduleSize, float minRatio)
        {
            var rs = building.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return false;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            float w = Mathf.Max(b.size.x, b.size.z);
            return b.size.y > minRatio * w && b.size.y > 0.3f * moduleSize;
        }

        /// <summary>Отметить офисами все небоскрёбы под корнем (дома — объекты B_...). Возвращает число.</summary>
        public static int MarkAll(Transform root, float moduleSize, float minRatio, bool clearOthers)
        {
            int n = 0;
            var list = new List<Transform>();
            Collect(root, list);
            for (int i = 0; i < list.Count; i++)
            {
                GameObject go = list[i].gameObject;
                bool tower = LooksLikeTower(go, moduleSize, minRatio);
                CityOfficeTower o = go.GetComponent<CityOfficeTower>();
                if (tower)
                {
                    if (o == null) o = go.AddComponent<CityOfficeTower>();
                    o.Apply();
                    n++;
                }
                else if (clearOthers && o != null)
                {
                    if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
                }
            }
            Debug.Log("[Офисы] отмечено небоскрёбов: " + n + " (из домов " + list.Count + ")");
            return n;
        }

        static void Collect(Transform t, List<Transform> list)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c.name.StartsWith("B_")) { list.Add(c); continue; }
                Collect(c, list);
            }
        }
    }
}
