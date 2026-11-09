using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ОБЩИЙ РОСТ ЭТАЖЕЙ. Один компонент на все этажи и дома стройки: «Поднять» двигает каждый объект на высоту
    /// его этажа вверх и добавляет столько же фейк-этажей снизу — дом остаётся стоять на земле, все этажи едут вместе.
    /// Всё, что лежит ВНУТРИ этажа (дети объекта: пропсы, привязанные кнопкой «Привязать пропсы»), едет само.
    /// Физические тела (Rigidbody, CharacterController) над этажом при подъёме переносятся вместе с ним.
    /// Из кода: GetComponent&lt;CityLevelStack&gt;().Raise(1).
    /// </summary>
    public class CityLevelStack : MonoBehaviour
    {
        [Tooltip("Генераторы этажей (CityFloorGenerator) и дома из модулей (CityModuleHouse)")]
        public List<Component> Targets = new List<Component>();
        [Tooltip("Насколько этажей всё поднято от уровня «на земле»")]
        public int Level = 0;
        [Min(1)] public int MaxLevel = 30;
        [Tooltip("Переносить Rigidbody и CharacterController, стоящие над этажом")]
        public bool CarryBodies = true;
        [Tooltip("Y земли — для кнопки «На землю»")]
        public float GroundY = 0f;
        public bool Log = true;

        private readonly HashSet<Transform> _carried = new HashSet<Transform>();
        public HashSet<Transform> Carried { get { return _carried; } }

        public static float HeightOf(Component c)
        {
            CityFloorGenerator g = c as CityFloorGenerator;
            if (g != null) return g.Height;
            CityModuleHouse h = c as CityModuleHouse;
            return h != null ? h.Height : 0f;
        }

        public static int FakesOf(Component c)
        {
            CityFloorGenerator g = c as CityFloorGenerator;
            if (g != null) return g.FakeFloors;
            CityModuleHouse h = c as CityModuleHouse;
            return h != null ? h.FakeFloors : 0;
        }

        public static void SetFakes(Component c, int n)
        {
            n = Mathf.Clamp(n, 0, 60);
            CityFloorGenerator g = c as CityFloorGenerator;
            CityModuleHouse h = c as CityModuleHouse;
            if (g != null) g.FakeFloors = n;
            if (h != null) h.FakeFloors = n;
            Transform ft = c.transform.Find("FakeFloors");
            CityFakeFloors ff = ft != null ? ft.GetComponent<CityFakeFloors>() : null;
            if (ff != null) { ff.SetFloors(n); ff.Rebuild(); }
        }

        /// <summary>Габарит собранной геометрии этажа (без фейк-этажей).</summary>
        public static bool Footprint(Component c, out Bounds b)
        {
            b = new Bounds(c.transform.position, Vector3.zero);
            Transform root = c is CityFloorGenerator ? c.transform.Find(CityFloorGenerator.RootName) : c.transform.Find(CityModuleHouse.BuiltName);
            if (root == null) return false;
            Renderer[] rs = root.GetComponentsInChildren<Renderer>();
            bool any = false;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] is ParticleSystemRenderer) continue;
                if (!any) { b = rs[i].bounds; any = true; } else b.Encapsulate(rs[i].bounds);
            }
            return any;
        }

        public void Collect()
        {
            Targets.Clear();
            CityFloorGenerator[] gs = FindObjectsByType<CityFloorGenerator>(FindObjectsSortMode.None);
            CityModuleHouse[] hs = FindObjectsByType<CityModuleHouse>(FindObjectsSortMode.None);
            for (int i = 0; i < gs.Length; i++) Targets.Add(gs[i]);
            for (int i = 0; i < hs.Length; i++) Targets.Add(hs[i]);
            Targets.Sort(delegate (Component a, Component b) { return string.CompareOrdinal(a.name, b.name); });
        }

        /// <summary>Сколько этажей реально можно сдвинуть: вниз не ниже земли (фейк-этажей не меньше нуля), вверх не выше MaxLevel.</summary>
        public int Allowed(int n)
        {
            int a = n;
            if (n > 0) a = Mathf.Min(n, MaxLevel - Level);
            else
            {
                a = Mathf.Max(n, -Level);
                for (int i = 0; i < Targets.Count; i++) if (Targets[i] != null) a = Mathf.Max(a, -FakesOf(Targets[i]));
            }
            return a;
        }

        /// <summary>Поднять (n&gt;0) или опустить (n&lt;0) ВСЕ этажи вместе. Возвращает, на сколько этажей сдвинулось.</summary>
        public int Raise(int n)
        {
            int a = Allowed(n);
            if (a == 0) { if (Log) Debug.LogWarning("[Рост этажей] дальше некуда: уровень " + Level + " из " + MaxLevel + ".", this); return 0; }
            _carried.Clear();
            for (int i = 0; i < Targets.Count; i++)
            {
                Component c = Targets[i];
                if (c == null) continue;
                float dy = a * HeightOf(c);
                if (CarryBodies) CarryBodiesOf(c, dy);
                c.transform.position += new Vector3(0f, dy, 0f);
                SetFakes(c, FakesOf(c) + a);
            }
            Level += a;
            Physics.SyncTransforms();
            if (Log) Debug.Log("[Рост этажей] все этажи " + (a > 0 ? "подняты" : "опущены") + " на " + Mathf.Abs(a) + ", уровень " + Level + ".", this);
            return a;
        }

        private void CarryBodiesOf(Component c, float dy)
        {
            Bounds b;
            if (!Footprint(c, out b)) return;
            Vector3 ext = b.extents; ext.y += 1.5f;
            Vector3 ctr = new Vector3(b.center.x, b.max.y + 1.5f - ext.y, b.center.z);
            Collider[] cols = Physics.OverlapBox(ctr, ext, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cols.Length; i++)
            {
                Rigidbody rb = cols[i].attachedRigidbody;
                Transform t = rb != null ? rb.transform : null;
                CharacterController cc = cols[i] as CharacterController;
                if (cc != null) t = cc.transform;
                if (t == null || t.IsChildOf(c.transform) || _carried.Contains(t)) continue;
                _carried.Add(t);
                if (cc != null) { cc.enabled = false; t.position += new Vector3(0f, dy, 0f); cc.enabled = true; }
                else { rb.position += new Vector3(0f, dy, 0f); t.position += new Vector3(0f, dy, 0f); }
            }
        }
    }
}
