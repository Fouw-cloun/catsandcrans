using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТАРЕЛКА БЕФСТРОГАНОВА — подача и звёзды. Вешается на тарелку (Cookware), меню CatCooks → Бефстроганов → 3.
    /// В тарелку мастерком: бефстроганов из сковороды, рядом гарнир — картофельное пюре, сверху зелень (укроп/петрушка).
    /// Звёзды (1–5):
    ///   1 — бефстроганов на тарелке (от 2 порций, т.е. от 1 мастерка);
    ///   2 — полная порция (от 4 порций = 2 мастерка);
    ///   3 — гарнир: картофельное пюре (от 2 порций);
    ///   4 — соус не свернулся (нет «сметана свернулась»);
    ///   5 — зелень сверху.
    /// Над тарелкой — подсказка (видно с 6 м). Всё считается у каждого по содержимому тары (оно синхронизировано).
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class StroganoffPlate : MonoBehaviour
    {
        [Range(1, 20)][SerializeField] private int _minPortions = 2;
        [Range(1, 30)][SerializeField] private int _fullPortions = 4;
        [Range(1, 20)][SerializeField] private int _garnish = 2;
        [SerializeField] private bool _hud = true;
        [SerializeField] private bool _log = true;

        private Cookware _cw;
        private int _dish, _curd, _puree, _herb;
        private float _at;
        private int _lastStars = -1;

        public int Stars { get; private set; }

        private void Awake() => _cw = GetComponent<Cookware>();

        private void Update()
        {
            if (_cw == null || Time.time < _at) return;
            _at = Time.time + 0.25f;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            _dish = 0; _curd = 0; _puree = 0; _herb = 0;
            for (int i = 0; i < _cw.ViewCount; i++)
            {
                BagView v = _cw.GetView(i);
                if (v.Count <= 0) continue;
                ProductDef d = db.Get(v.Product);
                if (d == null || string.IsNullOrEmpty(d.Title)) continue;
                string t = d.Title.Trim().ToLowerInvariant();
                if (t == "бефстроганов") _dish += v.Count;
                else if (t.StartsWith("бефстроганов (")) _curd += v.Count;
                else if (t.Contains("пюре") || (t.StartsWith("картоф") && v.State == FoodState.Puree)) _puree += v.Count;
                else if (t == "укроп" || t == "петрушка" || t.Contains("зелен")) _herb += v.Count;
            }
            int all = _dish + _curd;
            int s = 0;
            if (all >= _minPortions)
            {
                s = 1;
                if (all >= _fullPortions) s++;
                if (_puree >= _garnish) s++;
                if (_curd == 0) s++;
                if (_herb > 0) s++;
            }
            Stars = s;
            if (s != _lastStars)
            {
                if (_log && s > _lastStars && s > 0 && FishNet.InstanceFinder.IsServerStarted)
                    Debug.Log($"[Бефстроганов] «{name}»: {s}★ — порций {all}, пюре {_puree}, зелень {_herb}" + (_curd > 0 ? ", сметана свернулась" : ""), this);
                _lastStars = s;
            }
        }

        private static GUIStyle _style;

        private void OnGUI()
        {
            if (!_hud || Event.current.type != EventType.Repaint || _cw == null) return;
            if (_dish + _curd + _puree + _herb == 0) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 w = transform.position + Vector3.up * 0.35f;
            if ((w - cam.transform.position).sqrMagnitude > 36f) return;
            Vector3 sp = cam.WorldToScreenPoint(w);
            if (sp.z <= 0f) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = false, richText = true };
            int all = _dish + _curd;
            string text = $"Бефстроганов {Mark(all >= _fullPortions)} {all}/{_fullPortions} · пюре {Mark(_puree >= _garnish)} · зелень {Mark(_herb > 0)}" +
                          (_curd > 0 ? " · <color=#FF8A6A>сметана свернулась</color>" : "") +
                          (Stars > 0 ? "   " + new string('★', Stars) + new string('☆', 5 - Stars) : "");
            Vector2 size = _style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(sp.x - size.x * 0.5f - 4f, Screen.height - sp.y - size.y, size.x + 8f, size.y + 4f), text, _style);
        }

        private static string Mark(bool ok) => ok ? "<color=#7CFC6A>✔</color>" : "—";
    }
}
