using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ВИТРИНА СТРУЙ — поставь пустой объект на высоте ~1.2 м над полом, повесь этот
    /// компонент и нажми Play: появится ряд струй разных жидкостей, ширины и углов
    /// (бутылка, кувшин, ведро краем, противень, мёд, битум, качающаяся струя...).
    /// Для настройки вида: крути LiquidPourProfile и смотри сразу на все.
    /// </summary>
    public sealed class PourShowcase : MonoBehaviour
    {
        [SerializeField] private float _spacing = 0.9f;
        [Tooltip("Качать источники (видно инерцию струи)")]
        [SerializeField] private bool _sway = true;

        private struct Item { public Transform T; public Quaternion Base; public float Phase, Amp; }
        private readonly List<Item> _items = new List<Item>();

        private struct Def
        {
            public string Name; public LiquidPourSource.Shape Shape; public float Width, Lip, Speed, Pitch; public Color Color;
            public LiquidPourProfile Profile; public float Sway;
        }

        private void Start()
        {
            var defs = new List<Def>
            {
                new Def { Name = "Вода — бутылка", Shape = LiquidPourSource.Shape.Spout, Width = 0.018f, Speed = 1.0f, Pitch = 25f, Color = new Color(0.4f, 0.8f, 0.9f), Profile = LiquidPourProfile.Water(), Sway = 4f },
                new Def { Name = "Вода — ведро краем", Shape = LiquidPourSource.Shape.Lip, Width = 0.05f, Lip = 0.26f, Speed = 0.9f, Pitch = 10f, Color = new Color(0.4f, 0.8f, 0.9f), Profile = LiquidPourProfile.Water(), Sway = 3f },
                new Def { Name = "Молоко — кувшин", Shape = LiquidPourSource.Shape.Spout, Width = 0.032f, Speed = 0.8f, Pitch = 20f, Color = new Color(0.97f, 0.96f, 0.92f), Profile = LiquidPourProfile.Milk(), Sway = 5f },
                new Def { Name = "Молоко — противень", Shape = LiquidPourSource.Shape.Lip, Width = 0.03f, Lip = 0.34f, Speed = 0.6f, Pitch = 5f, Color = new Color(0.97f, 0.96f, 0.92f), Profile = LiquidPourProfile.Milk(), Sway = 2f },
                new Def { Name = "Масло — бутылка", Shape = LiquidPourSource.Shape.Spout, Width = 0.014f, Speed = 0.7f, Pitch = 30f, Color = new Color(0.95f, 0.78f, 0.25f), Profile = LiquidPourProfile.Oil(), Sway = 3f },
                new Def { Name = "Мёд — банка", Shape = LiquidPourSource.Shape.Spout, Width = 0.02f, Speed = 0.35f, Pitch = 40f, Color = new Color(0.9f, 0.55f, 0.1f), Profile = LiquidPourProfile.Honey(), Sway = 1.5f },
                new Def { Name = "Битум — банка краем", Shape = LiquidPourSource.Shape.Lip, Width = 0.04f, Lip = 0.16f, Speed = 0.4f, Pitch = 15f, Color = new Color(0.12f, 0.1f, 0.11f), Profile = LiquidPourProfile.Bitumen(), Sway = 2f },
                new Def { Name = "Сок — качаем", Shape = LiquidPourSource.Shape.Spout, Width = 0.025f, Speed = 1.1f, Pitch = 20f, Color = new Color(0.95f, 0.45f, 0.1f), Profile = LiquidPourProfile.Juice(), Sway = 18f },
                new Def { Name = "Сливки — миска", Shape = LiquidPourSource.Shape.Lip, Width = 0.03f, Lip = 0.12f, Speed = 0.5f, Pitch = 15f, Color = new Color(1f, 0.93f, 0.75f), Profile = LiquidPourProfile.Cream(), Sway = 3f },
                new Def { Name = "Бульон — большое ведро", Shape = LiquidPourSource.Shape.Lip, Width = 0.07f, Lip = 0.42f, Speed = 1.3f, Pitch = 0f, Color = new Color(0.85f, 0.62f, 0.25f), Profile = LiquidPourProfile.Broth(), Sway = 4f },
            };
            for (int i = 0; i < defs.Count; i++)
            {
                Def d = defs[i];
                var go = new GameObject(d.Name);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3((i - (defs.Count - 1) * 0.5f) * _spacing, 0f, 0f);
                // носик смотрит вперёд (+Z) и вниз на Pitch
                go.transform.localRotation = Quaternion.Euler(d.Pitch, 0f, 0f);
                var mouth = new GameObject("Mouth").transform;
                mouth.SetParent(go.transform, false);
                var src = go.AddComponent<LiquidPourSource>();
                src.Setup(mouth, d.Shape, d.Width, d.Lip, d.Speed, d.Color, d.Profile, true);
                _items.Add(new Item { T = go.transform, Base = go.transform.localRotation, Phase = i * 1.3f, Amp = d.Sway });
            }
        }

        private void Update()
        {
            if (!_sway) return;
            float t = Time.time;
            foreach (Item it in _items)
            {
                float yaw = Mathf.Sin(t * 0.9f + it.Phase) * it.Amp;
                float pitch = Mathf.Sin(t * 0.6f + it.Phase * 0.7f) * it.Amp * 0.4f;
                it.T.localRotation = it.Base * Quaternion.Euler(pitch, yaw, 0f);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(transform.position, new Vector3(_spacing * 10f, 0.05f, 0.05f));
        }
    }
}
