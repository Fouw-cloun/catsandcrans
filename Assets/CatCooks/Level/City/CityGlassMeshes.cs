using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// Размеченные стёкла: исходный меш стекла из FBX → его копия, где у каждого окна (куска стекла) свои координаты
    /// 0..1 (UV3) и случайное число (UV4). Заполняет кнопка «Атласы» (CityGlassBaker), читает генератор при расстановке.
    /// Ассет: Assets/Generated/City/CityGlassMeshes.asset.
    /// </summary>
    public sealed class CityGlassMeshes : ScriptableObject
    {
        public List<Mesh> Sources = new List<Mesh>();
        public List<Mesh> Baked = new List<Mesh>();

        [System.NonSerialized] private Dictionary<Mesh, Mesh> _map;

        public Mesh Get(Mesh src)
        {
            if (src == null) return null;
            if (_map == null || _map.Count != Mathf.Min(Sources.Count, Baked.Count))
            {
                _map = new Dictionary<Mesh, Mesh>();
                for (int i = 0; i < Sources.Count && i < Baked.Count; i++)
                    if (Sources[i] != null && Baked[i] != null) _map[Sources[i]] = Baked[i];
            }
            Mesh m;
            return _map.TryGetValue(src, out m) ? m : null;
        }

        public bool IsBaked(Mesh m) { return m != null && Baked.Contains(m); }

        public void Set(Mesh src, Mesh baked)
        {
            int i = Sources.IndexOf(src);
            if (i >= 0 && i < Baked.Count) Baked[i] = baked;
            else { Sources.Add(src); Baked.Add(baked); }
            _map = null;
        }
    }
}
