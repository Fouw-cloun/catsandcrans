using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food.Supply
{
    /// <summary>
    /// v24.4: «УСПОКОИТЬ» ТОЛЬКО ЧТО ПОЯВИВШИЙСЯ ПРЕДМЕТ ПОСТАВКИ. Предметы появляются рядом друг с другом и со
    /// стенками ящика; если коллайдеры чуть пересеклись, физика выталкивает их с огромной скоростью — «взрыв».
    /// Здесь на первые секунды скорость выталкивания ограничена (Max Depenetration Velocity), скорость обнулена:
    /// предметы мягко расходятся и ложатся. Потом всё как было. Сервер (там считается физика); компонент удаляет себя сам.
    /// </summary>
    public sealed class SupplySettle : MonoBehaviour
    {
        const float CalmSeconds = 2.5f, CalmDepenetration = 0.4f;

        private readonly List<(Rigidbody rb, float was)> _bodies = new List<(Rigidbody, float)>();
        private float _until;

        public static void Calm(GameObject go)
        {
            if (go == null) return;
            SupplySettle s = go.GetComponent<SupplySettle>();
            if (s == null) s = go.AddComponent<SupplySettle>();
            s._until = Time.time + CalmSeconds;
            if (s._bodies.Count > 0) return;
            foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))
            {
                s._bodies.Add((rb, rb.maxDepenetrationVelocity));
                rb.maxDepenetrationVelocity = CalmDepenetration;
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
        }

        private void FixedUpdate()
        {
            // в первые кадры гасим и то, что всё-таки набрало скорость
            foreach (var (rb, _) in _bodies)
            {
                if (rb == null || rb.isKinematic) continue;
                if (rb.linearVelocity.sqrMagnitude > 9f) rb.linearVelocity = rb.linearVelocity.normalized * 3f;
                if (rb.angularVelocity.sqrMagnitude > 100f) rb.angularVelocity = rb.angularVelocity.normalized * 10f;
            }
            if (Time.time < _until) return;
            foreach (var (rb, was) in _bodies) if (rb != null) rb.maxDepenetrationVelocity = was;
            Destroy(this);
        }
    }
}
