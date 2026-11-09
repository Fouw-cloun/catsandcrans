using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТАРЕЛКА КОТЛЕТ — подача и звёзды. Вешается на тарелку (Cookware), меню Котлеты → 7.
    /// На тарелку: жареную котлету (положить/бросить — лежит собой), мастерком — картофельное пюре.
    /// Звёзды (лучшая котлета на тарелке):
    ///   1 — жареная котлета на тарелке;   2 — обе стороны в зелёной зоне (не недожарена и не пережарена);
    ///   3 — в панировке;   4 — жарилась на сливочном И подсолнечном масле;   5 — пюре рядом (от 2 порций).
    /// Сгорела — максимум 1. Подсказка над тарелкой (видно с 8 м).
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class CutletPlate : MonoBehaviour
    {
        [Range(1, 20)][SerializeField] private int _puree = 2;
        [Tooltip("Радиус тарелки для поиска котлет (0 — по габариту модели)")]
        [SerializeField] private float _radius = 0f;
        [SerializeField] private bool _hud = true;
        [SerializeField] private bool _log = true;

        private Cookware _cw;
        private float _at;
        private int _lastStars = -1, _pureeHave;
        private CutletPatty _best;
        public int Stars { get; private set; }

        private void Awake() => _cw = GetComponent<Cookware>();

        private Bounds PlateBounds()
        {
            Bounds b = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (r == null || r is ParticleSystemRenderer || (r.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private void Update()
        {
            if (_cw == null || Time.time < _at) return;
            _at = Time.time + 0.3f;
            Bounds pb = PlateBounds();
            float rad = _radius > 0f ? _radius : Mathf.Max(pb.extents.x, pb.extents.z) * 1.05f;
            _best = null; int bestS = -1;
            foreach (CutletPatty p in Object.FindObjectsByType<CutletPatty>(FindObjectsSortMode.None))
            {
                if (p == null) continue;
                FoodPiece fp = p.GetComponent<FoodPiece>();
                if (fp == null || fp.IsHeld || fp.IsConsumed) continue;
                Vector3 d = p.transform.position - pb.center;
                if (new Vector2(d.x, d.z).magnitude > rad || p.transform.position.y < pb.min.y - 0.1f || p.transform.position.y > pb.max.y + 1f) continue;
                int s = Score(p, 0);
                if (s > bestS) { bestS = s; _best = p; }
            }
            _pureeHave = 0;
            FoodDatabase db = FoodDatabase.Instance;
            if (db != null)
                for (int i = 0; i < _cw.ViewCount; i++)
                {
                    BagView v = _cw.GetView(i);
                    if (v.Count <= 0) continue;
                    ProductDef d = db.Get(v.Product);
                    if (d == null || string.IsNullOrEmpty(d.Title)) continue;
                    string t = d.Title.Trim().ToLowerInvariant();
                    if (t.Contains("пюре") || (t.StartsWith("картоф") && v.State == FoodState.Puree)) _pureeHave += v.Count;
                }
            Stars = _best != null ? Score(_best, _pureeHave) : 0;
            if (Stars != _lastStars)
            {
                if (_log && Stars > 0 && FishNet.InstanceFinder.IsServerStarted)
                    Debug.Log($"[Котлеты] «{name}»: {Stars}★ — {(_best.Burnt ? "сгорела" : _best.InGoldBoth ? "золотая с двух сторон" : "прожарка не в зоне")}, " +
                              $"{(_best.Breaded ? "в панировке" : "без панировки")}, {(_best.BothFats ? "на двух маслах" : "на одном масле")}, пюре {_pureeHave}", this);
                _lastStars = Stars;
            }
        }

        private int Score(CutletPatty p, int puree)
        {
            if (!p.FriedBoth) return 0;
            if (p.Burnt) return 1;
            int s = 1;
            if (p.InGoldBoth) s++;
            if (p.Breaded) s++;
            if (p.BothFats) s++;
            if (puree >= _puree) s++;
            return s;
        }

        private static GUIStyle _style;

        private void OnGUI()
        {
            if (!_hud || Event.current.type != EventType.Repaint || _cw == null) return;
            if (_best == null && _pureeHave == 0) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Bounds pb = PlateBounds();
            Vector3 w = new Vector3(pb.center.x, pb.max.y + 0.6f, pb.center.z);
            if ((w - cam.transform.position).sqrMagnitude > 64f) return;
            Vector3 sp = cam.WorldToScreenPoint(w);
            if (sp.z <= 0f) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = false, richText = true };
            string text;
            if (_best == null) text = $"Пюре {Mark(_pureeHave >= _puree)} — положи жареную котлету";
            else if (!_best.FriedBoth) text = "Котлета не дожарена — обратно на сковороду";
            else text = $"Котлета {(_best.Burnt ? "<color=#FF8A6A>сгорела</color>" : Mark(_best.InGoldBoth) + " золотая")} · панировка {Mark(_best.Breaded)} · два масла {Mark(_best.BothFats)} · пюре {Mark(_pureeHave >= _puree)}" +
                        (Stars > 0 ? "   " + new string('★', Stars) + new string('☆', 5 - Stars) : "");
            Vector2 size = _style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(sp.x - size.x * 0.5f - 4f, Screen.height - sp.y - size.y, size.x + 8f, size.y + 4f), text, _style);
        }

        private static string Mark(bool ok) => ok ? "<color=#7CFC6A>✔</color>" : "—";
    }
}
