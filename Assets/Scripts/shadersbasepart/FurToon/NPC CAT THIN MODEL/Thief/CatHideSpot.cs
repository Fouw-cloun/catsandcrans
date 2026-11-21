using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Thief
{
    /// <summary>
    /// Укрытие кота-воришки: сюда он убегает с мясом и там его съедает (если за ним не гонятся).
    /// Ставь в углы, под столы, за ящики — меню CatCooks → Кот-воришка → 3. Кот выбирает то,
    /// что дальше от игроков и не слишком далеко от него. Нет ни одного — бежит просто подальше от игроков.
    /// </summary>
    public sealed class CatHideSpot : MonoBehaviour
    {
        private static readonly List<CatHideSpot> _all = new List<CatHideSpot>();
        public static IReadOnlyList<CatHideSpot> All => _all;

        private void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
        private void OnDisable() => _all.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.3f, 0.6f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.2f);
        }
    }
}
