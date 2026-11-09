using FishNet;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТЕЛЯЧЬЯ КОРЕЙКА (телятина по-орловски, шаг 3) — ведёт кусок по шагам и подсказывает над ним:
    ///   1. ОБЖАРИТЬ в сковороде (правило «Разделано → Жарить → Жареное», ~20 с) — корочка.
    ///   2. ТУШИТЬ в кастрюле: в бульоне (или воде), ПОД КРЫШКОЙ (правило «Жареное → Варить → Тушёное», ~60 с).
    ///      Без крышки или без жидкости — не тушится (подсказка). Сырая, не обжаренная — «сначала обжарь».
    ///   3. ДАТЬ ОТДОХНУТЬ — вынуть и положить (не в тару, не в руки) на Rest Seconds → продукт «Телятина тушёная»
    ///      (готова к нарезке). Режешь, не дав отдохнуть, — сок вытечет (оценка ниже, шаг 4).
    /// Вешается на префаб корейки (меню Орлов → 8). Сам кусок — обычный FoodPiece; по сети идут его продукт и состояние,
    /// отдых считается на сервере, подсказка — у каждого игрока своя (тот же таймер).
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class VealLoinCook : MonoBehaviour, IFoodWorkHandler
    {
        [Tooltip("Продукт после отдыха. Пусто — «Телятина тушёная» по названию")]
        [SerializeField] private ProductDef _restedProduct;
        [Tooltip("Сколько секунд отдыхать вне тары после тушения")]
        [Min(1f)][SerializeField] private float _restSeconds = 10f;
        [Tooltip("Тушить только под крышкой (PotLidSocket на кастрюле)")]
        [SerializeField] private bool _needLid = true;
        [Tooltip("Подсказка над куском (видно с 10 м)")]
        [SerializeField] private bool _hud = true;
        [SerializeField] private bool _log = true;

        private FoodPiece _food;
        private float _rest, _hintAt, _checkAt;
        private string _why;
        private bool _inPot;

        public float Rest01 => Mathf.Clamp01(_rest / _restSeconds);

        private void Awake() => _food = GetComponent<FoodPiece>();

        private bool IsLoin => _food != null && _food.Def != null && _food.Def.Title == CookRecipeBook.Orloff.VealLoin;

        // ───────────────────────── обработка (сервер) ─────────────────────────

        public bool ServerHandleWork(FoodPiece piece, FoodProcess process, float seconds, float quality01)
        {
            if (!IsLoin || process != FoodProcess.Boil) return false;
            FoodState st = piece.State;
            if (st == FoodState.Butchered || st == FoodState.Raw)
            {
                Hint("сначала обжарь в сковороде до корочки — потом тушить");
                return true;                       // сырая в кастрюле не варится
            }
            if (st == FoodState.Stewed || st == FoodState.Overcooked) return false;
            Cookware cw = Container();
            if (cw == null) return false;
            if (_needLid)
            {
                PotLidSocket lid = cw.GetComponentInParent<PotLidSocket>();
                if (lid == null) lid = cw.GetComponentInChildren<PotLidSocket>();
                if (lid != null && !lid.HasLid) { Hint("накрой крышкой — без неё не тушится, сохнет"); return true; }
            }
            if (!HasBroth(cw)) { Hint("налей бульон (или воду) — тушится в жидкости"); return true; }
            _why = null;
            return false;                          // дальше — правило продукта «Жареное → Варить → Тушёное»
        }

        public void ServerStopWork(FoodPiece piece) { }

        private void Hint(string why)
        {
            _why = why;
            if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 4f; Debug.Log($"[Телятина] «{name}»: {why}", this); }
        }

        private Cookware Container()
        {
            Vector3 p = transform.position;
            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null) continue;
                Collider iv = cw.InsideVolume;
                if (cw.IsInside(p) || (iv != null && iv.bounds.Contains(p))) return cw;
            }
            return null;
        }

        private static bool HasBroth(Cookware cw)
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return true;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count <= 0) continue;
                ProductDef d = db.Get(v.Product);
                if (d == null || string.IsNullOrEmpty(d.Title)) continue;
                string t = d.Title.ToLowerInvariant();
                if (t.Contains("бульон") || t == "вода") return true;
            }
            return false;
        }

        // ───────────────────────── отдых ─────────────────────────

        private void Update()
        {
            if (_food == null || _food.IsConsumed) return;
            if (Time.time >= _checkAt) { _checkAt = Time.time + 0.25f; _inPot = Container() != null; }
            bool resting = IsLoin && _food.State == FoodState.Stewed && !_food.IsHeld && !_inPot;
            if (!resting) { if (!IsLoin || _food.State != FoodState.Stewed) _rest = 0f; return; }
            _rest += Time.deltaTime;
            if (_rest < _restSeconds || !InstanceFinder.IsServerStarted) return;
            ProductDef done = Rested();
            if (done == null) { if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 6f; Debug.LogWarning("[Телятина] нет продукта «Телятина тушёная» — меню Орлов → 8", this); } return; }
            _food.ServerSetProductState(done, FoodState.Stewed);
            if (_log) Debug.Log($"[Телятина] «{name}»: отдохнула {_restSeconds:0} с — «{done.Title}», можно резать", this);
        }

        private ProductDef Rested()
        {
            if (_restedProduct != null) return _restedProduct;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Get((ushort)id);
                if (p != null && p.Title == CookRecipeBook.Orloff.VealStewed) { _restedProduct = p; return p; }
            }
            return null;
        }

        // ───────────────────────── подсказка ─────────────────────────

        private static GUIStyle _style;
        private FoodState _winState;
        private float _winStart;

        private void OnGUI()
        {
            if (!_hud || Event.current.type != EventType.Repaint || _food == null || _food.IsConsumed || _food.Def == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Renderer rr = GetComponent<Renderer>();
            Vector3 w = rr != null ? new Vector3(rr.bounds.center.x, rr.bounds.max.y + 0.3f, rr.bounds.center.z) : transform.position + Vector3.up * 0.4f;
            if ((w - cam.transform.position).sqrMagnitude > 144f) return;
            Vector3 s = cam.WorldToScreenPoint(w);
            if (s.z <= 0f) return;
            string text = Status(out float bar, out Color barCol, out float zoneFrom);
            if (string.IsNullOrEmpty(text)) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = false, richText = true };
            Vector2 size = _style.CalcSize(new GUIContent(text));
            float bw = Mathf.Max(size.x + 12f, 180f), bh = size.y + (bar >= 0f ? 14f : 4f);
            Rect box = new Rect(s.x - bw * 0.5f, Screen.height - s.y - bh, bw, bh);
            GUI.Box(box, text, _style);
            if (bar < 0f) return;
            Color was = GUI.color;
            Rect r = new Rect(box.x + 6f, box.yMax - 9f, box.width - 12f, 6f);
            GUI.color = new Color(0f, 0f, 0f, 0.65f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            if (zoneFrom >= 0f)
            {
                // зелёная зона «готово» — видно заранее, куда идёт полоска
                GUI.color = new Color(0.30f, 0.75f, 0.30f, 0.45f);
                GUI.DrawTexture(new Rect(r.x + r.width * zoneFrom, r.y, r.width * (1f - zoneFrom), r.height), Texture2D.whiteTexture);
            }
            GUI.color = barCol; GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(bar), r.height), Texture2D.whiteTexture);
            GUI.color = was;
        }

        /// <summary>
        /// Текст и полоска. Полоска — вся длина процесса до «пережарено/переготовлено»: оранжевое — готовится,
        /// зелёная зона — окно готовности (вынимать тут), за ней — передержал. Секунды — по правилам (Сек × (1 ± окно)).
        /// </summary>
        private string Status(out float bar, out Color barCol, out float zoneFrom)
        {
            bar = -1f; zoneFrom = -1f; barCol = new Color(0.95f, 0.6f, 0.2f);
            string title = _food.Def.Title;
            if (title == CookRecipeBook.Orloff.VealStewed) return GetComponent<OrloffLoaf>() != null ? null : "Телятина: <color=#7CFC6A>готова к нарезке ✔</color>";
            if (title != CookRecipeBook.Orloff.VealLoin) return null;
            float p = _food.Progress01;
            FoodState st = _food.State;
            // окно: состояние уже сменилось, а процесс идёт (Progress01 = 1)
            bool inWin = p >= 0.999f && (st == FoodState.Fried || st == FoodState.Stewed);
            if (inWin && _winState != st) { _winState = st; _winStart = Time.time; }
            if (!inWin) _winState = FoodState.None;

            float sear = CookRecipeBook.Orloff.SearSeconds, braise = CookRecipeBook.Orloff.BraiseSeconds;
            switch (st)
            {
                case FoodState.Butchered:
                case FoodState.Raw:
                    {
                        float ready = sear * 0.6f, total = sear * 1.4f;
                        zoneFrom = ready / total;
                        if (p <= 0f) { bar = 0f; return $"Корейка: обжарить в сковороде ~{ready:0}–{total:0} с"; }
                        bar = p * zoneFrom;
                        return $"Корейка: обжаривается {Mathf.RoundToInt(p * 100f)}% · ещё ~{Mathf.CeilToInt((1f - p) * ready)} с";
                    }
                case FoodState.Fried:
                    {
                        if (inWin)
                        {
                            float ready = sear * 0.6f, total = sear * 1.4f, t = ready + (Time.time - _winStart);
                            zoneFrom = ready / total; bar = t / total; barCol = new Color(0.35f, 0.85f, 0.3f);
                            return $"Корейка: обжарена ✔ — <color=#FFD36A>снимай!</color> до пережарки ~{Mathf.Max(0, Mathf.CeilToInt(total - t))} с";
                        }
                        float bReady = braise * 0.5f, bTotal = braise * 1.5f;
                        zoneFrom = bReady / bTotal;
                        if (!string.IsNullOrEmpty(_why) && FishNet.InstanceFinder.IsServerStarted) { bar = p * zoneFrom; return "Корейка: " + _why; }
                        if (p <= 0f) { bar = 0f; return $"Корейка: обжарена ✔ — в кастрюлю с бульоном, под крышку (U), тушить ~{bReady:0}–{bTotal:0} с"; }
                        bar = p * zoneFrom;
                        return $"Корейка: тушится {Mathf.RoundToInt(p * 100f)}% · ещё ~{Mathf.CeilToInt((1f - p) * bReady)} с";
                    }
                case FoodState.Undercooked:
                    if (p > 0f) { bar = p; return $"Корейка: доводится {Mathf.RoundToInt(p * 100f)}%"; }
                    return "Корейка: недоготовлена — дотушить";
                case FoodState.Stewed:
                    if (inWin || _inPot)
                    {
                        float bReady = braise * 0.5f, bTotal = braise * 1.5f, t = bReady + (inWin ? Time.time - _winStart : 0f);
                        zoneFrom = bReady / bTotal; bar = t / bTotal; barCol = new Color(0.35f, 0.85f, 0.3f);
                        return $"Корейка: протушилась ✔ — <color=#FFD36A>вынь</color> и дай отдохнуть" + (inWin ? $" (до переготовки ~{Mathf.Max(0, Mathf.CeilToInt(bTotal - t))} с)" : "");
                    }
                    bar = Rest01; barCol = new Color(0.55f, 0.75f, 0.95f);
                    return $"Корейка: отдыхает {Mathf.Min(_rest, _restSeconds):0}/{_restSeconds:0} с (не в таре, не в руках)";
                case FoodState.Overcooked: return "Корейка: <color=#FF8A6A>переготовлена</color>";
                case FoodState.Burnt: return "Корейка: <color=#FF8A6A>сгорела</color>";
            }
            return null;
        }
    }
}
