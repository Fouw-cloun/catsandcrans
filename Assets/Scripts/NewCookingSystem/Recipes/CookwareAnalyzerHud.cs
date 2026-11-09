using System.Text;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// АНАЛИТИК ТАРЫ — подсказка на экране: смотришь на тару — видно, что в ней
    /// (в вёдрах, мастерках, штуках), в каком порядке клали, какие были обработки,
    /// какой рецепт идёт, на каком шаге, оценка и что не так.
    /// F4 — показать/скрыть. Полный журнал видит хост (журнал ведёт сервер),
    /// у второго игрока — только содержимое.
    /// </summary>
    public sealed class CookwareAnalyzerHud : MonoBehaviour
    {
        public static bool Visible = true;
        public static KeyCode Toggle = KeyCode.F4;
        /// <summary>Отладка (только хост), смотришь на тару: J — +1 ведро воды, Shift+J — до полной, K — опустошить.</summary>
        public static KeyCode FillKey = KeyCode.J;
        public static KeyCode EmptyKey = KeyCode.K;
        /// <summary>Отладка (хост): N — положить в тару под прицелом всё для выбранного рецепта (одна порция рецепта), Shift+N — следующий рецепт.</summary>
        public static KeyCode RecipeFillKey = KeyCode.N;
        /// <summary>Отладка (хост): X — положить в тару под прицелом сразу ГОТОВЫЙ продукт выбранного рецепта
        /// (тесто, ганаш, карамель…), столько, сколько дал бы рецепт. Ctrl+N — предыдущий рецепт.</summary>
        public static KeyCode ReadyFillKey = KeyCode.X;
        private static int _testRecipe;
        private ProductDef _water;
        private string _flash;
        private float _flashT;

        private Cookware _target;
        private string _text;
        private float _t;
        private GUIStyle _style;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<CookwareAnalyzerHud>() != null) return;
            var go = new GameObject("CookwareAnalyzerHud (auto)");
            DontDestroyOnLoad(go);
            go.AddComponent<CookwareAnalyzerHud>();
        }

        private float _journalT;

        private void Update()
        {
            // Сервер: журнал на каждой таре с самого начала (порядок добавления пишется сразу).
            _journalT -= Time.deltaTime;
            if (_journalT <= 0f)
            {
                _journalT = 1f;
                foreach (Cookware c in Cookware.All)
                    if (c != null && c.IsServerInitialized) CookwareJournal.Of(c);
            }
            if (Input.GetKeyDown(Toggle)) Visible = !Visible;
            if (Input.GetKeyDown(FillKey)) DebugFillWater(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (Input.GetKeyDown(EmptyKey)) DebugEmpty();
            if (Input.GetKeyDown(RecipeFillKey))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) NextTestRecipe(1);
                else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) NextTestRecipe(-1);
                else DebugFillRecipe();
            }
            if (Input.GetKeyDown(ReadyFillKey)) DebugFillReady();
            if (!Visible) return;
            _t -= Time.deltaTime;
            if (_t > 0f) return;
            _t = 0.2f;
            _target = Aim();
            _text = _target != null ? Build(_target) : null;
        }

        private Cookware Aim()
        {
            Camera cam = Camera.main;
            if (cam == null) return null;
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _hits, 3f, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Cookware res = null;
            for (int i = 0; i < n; i++)
            {
                Cookware c = _hits[i].collider.GetComponentInParent<Cookware>();
                if (c == null || _hits[i].distance >= best) continue;
                best = _hits[i].distance; res = c;
            }
            return res;
        }

        /// <summary>J: долить воды до полной в тару под прицелом. Хост (сервер) — сразу; у клиента — подсказка.</summary>
        /// <summary>K: вылить из тары под прицелом всё (отладка, хост).</summary>
        private void DebugEmpty()
        {
            Cookware cw = Aim();
            if (cw == null) { Flash("K: наведись на тару"); return; }
            if (!cw.IsServerInitialized) { Flash("K (опустошить) работает у хоста"); return; }
            int was = 0;
            for (int i = 0; i < cw.ViewCount; i++) was += cw.GetView(i).Count;
            cw.ServerClear();
            string title = cw.Def != null ? cw.Def.Title : cw.name;
            Flash($"K: «{title}» опустошена ({was} порц.)");
            Debug.Log($"[Отладка K] «{cw.name}» опустошена — было {was} порц.", cw);
            _t = 0f;
        }

        private void DebugFillWater(bool toFull)
        {
            Cookware cw = Aim();
            if (cw == null) { Flash("J: наведись на тару (кастрюлю, ведро)"); return; }
            if (!cw.IsServerInitialized) { Flash("J (налить воды) работает у хоста"); return; }
            if (_water == null)
            {
                FoodDatabase db = FindAnyObjectByType<FoodDatabase>();
                if (db != null && db.Table != null)
                    for (int id = 1; id < 2048 && _water == null; id++)
                    {
                        ProductDef p = db.Table.GetProduct((ushort)id);
                        if (p != null && p.Title == "Вода") _water = p;
                    }
            }
            if (_water == null) { Flash("нет продукта «Вода»"); return; }
            int total = 0;
            for (int i = 0; i < cw.ViewCount; i++) total += cw.GetView(i).Count;
            int full = cw.Def != null ? cw.Def.Capacity : 30;
            // J — ровно 1 ведро (рецепты пишутся на ведро), Shift+J — до полной
            int need = toFull ? full - total : Mathf.Min(FoodUnits.Bucket, full - total);
            if (need <= 0) { Flash($"«{(cw.Def != null ? cw.Def.Title : cw.name)}» уже полная — K, чтобы опустошить"); return; }
            int put = cw.ServerAddContent(_water.Id, _water.StartState, need, 0, false);
            cw.ServerAgitate(0.6f);
            Flash($"J: +{FoodUnits.Describe(put, CookUnit.Bucket)} воды в «{(cw.Def != null ? cw.Def.Title : cw.name)}»");
            Debug.Log($"[Отладка J] +{put} порц. воды в «{cw.name}»", cw);
            _t = 0f;
        }

        // ───────────────────────── N: наполнить по рецепту ─────────────────────────

        private static System.Collections.Generic.List<CookRecipeDef> TestRecipes()
        {
            var list = new System.Collections.Generic.List<CookRecipeDef>();
            foreach (CookRecipeDef r in CookRecipeBook.Recipes)
                if (r != null && r.Driver == CookDriver.Book && r.Result != null && r.Steps.Count > 0 && !r.IsMistake) list.Add(r);   // «пригоревшее» — не для теста
            return list;
        }

        /// <summary>Выбранный для теста рецепт (имя — для подсказки).</summary>
        public static string TestRecipeTitle
        {
            get
            {
                var list = TestRecipes();
                return list.Count > 0 ? list[Mathf.Abs(_testRecipe) % list.Count].Title : "—";
            }
        }

        private void NextTestRecipe(int dir)
        {
            var list = TestRecipes();
            if (list.Count == 0) { Flash("N: в книге нет рецептов"); return; }
            _testRecipe = ((_testRecipe + dir) % list.Count + list.Count) % list.Count;
            CookRecipeDef r = list[_testRecipe];
            Flash($"«{r.Title}» ({_testRecipe + 1}/{list.Count}): N — ингредиенты, X — готовое «{r.Result.Title}» · Shift+N / Ctrl+N — другой");
            _t = 0f;
        }

        /// <summary>
        /// X: в тару под прицелом — сразу ГОТОВЫЙ результат выбранного рецепта, столько порций, сколько дал бы
        /// рецепт (все ингредиенты × объём шагов). Чтобы проверять этапы по отдельности: тесто, начинку, карамель…
        /// </summary>
        private void DebugFillReady()
        {
            Cookware cw = Aim();
            if (cw == null) { Flash("X: наведись на тару"); return; }
            if (!cw.IsServerInitialized) { Flash("X (готовый продукт) работает у хоста"); return; }
            var list = TestRecipes();
            if (list.Count == 0) { Flash("X: в книге нет рецептов"); return; }
            CookRecipeDef r = list[Mathf.Abs(_testRecipe) % list.Count];
            ProductDef into = r.Result;
            if (into == null) { Flash($"X: у рецепта «{r.Title}» нет результата"); return; }
            int full = cw.Def != null ? cw.Def.Capacity : FoodUnits.Bucket;
            int total = 0;
            foreach (CookRecipeDef.Ingredient ing in r.Ingredients)
                if (ing.Product != null) total += Mathf.Max(0, ing.ToPortions(ing.Amount));
            if (r.Base != null)
                total += r.BaseFillsContainer ? Mathf.CeilToInt(full * Mathf.Max(r.BaseMinFill, 0.98f)) : FoodUnits.ToPortions(r.BaseAmount, r.BaseUnit);
            float vk = 1f;
            foreach (CookRecipeDef.Step st in r.Steps) if (st.VolumeK > 0.01f) vk *= st.VolumeK;
            int n = Mathf.Max(1, Mathf.RoundToInt(total * vk));
            int room = full - cw.TotalCount;
            if (room <= 0) { Flash($"X: «{(cw.Def != null ? cw.Def.Title : cw.name)}» полная — K, чтобы опустошить"); return; }
            n = Mathf.Min(n, room);
            cw.ServerAddPinch(into.Id, into.StartState, n, 0, false);
            cw.ServerAgitate(0.4f);
            string where = cw.Def != null ? cw.Def.Title : cw.name;
            Flash($"X: готовое «{into.Title}» {FoodUnits.Describe(n, FoodUnits.NaturalUnit(into, into.StartState))} → «{where}»");
            Debug.Log($"[Отладка X] «{cw.name}»: положено готовое «{into.Title}» ×{n} порц. (как дал бы рецепт «{r.Title}»)", cw);
            _t = 0f;
        }

        /// <summary>
        /// N: в тару под прицелом — основа и ингредиенты выбранного рецепта ровно на одну порцию рецепта
        /// (если основа «заполняет тару» — до полной). Обработку (взбить, растопить…) делаешь сам.
        /// </summary>
        private void DebugFillRecipe()
        {
            Cookware cw = Aim();
            if (cw == null) { Flash("N: наведись на тару"); return; }
            if (!cw.IsServerInitialized) { Flash("N (наполнить по рецепту) работает у хоста"); return; }
            var list = TestRecipes();
            if (list.Count == 0) { Flash("N: в книге нет рецептов"); return; }
            CookRecipeDef r = list[Mathf.Abs(_testRecipe) % list.Count];
            int full = cw.Def != null ? cw.Def.Capacity : FoodUnits.Bucket;
            var sb = new StringBuilder();
            int batches = 1;
            foreach (CookRecipeDef.Ingredient ing in r.Ingredients)
            {
                if (ing.Product == null) continue;
                int n = ing.ToPortions(ing.Amount * batches);
                if (n <= 0) continue;
                FoodState st = TestState(ing.Product, ing.State, ing.CleanOnly);
                cw.ServerAddContent(ing.Product.Id, st, n, 0, false);
                sb.Append($"{ing.Product.Title} {ing.Describe(n)} ({FoodStateInfo.NameOf(st)}); ");
            }
            if (r.Base != null)
            {
                int n;
                if (r.BaseFillsContainer) n = Mathf.Max(0, Mathf.CeilToInt(full * Mathf.Max(r.BaseMinFill, 0.98f)) - (r.PerContainer ? cw.TotalCount : 0));
                else n = FoodUnits.ToPortions(r.BaseAmount, r.BaseUnit);
                if (n > 0)
                {
                    // у рецепта задано состояние основы (тесто ньокки — картофель ПЮРЕ) — кладём сразу в нём, а не сырым
                    FoodState bs = TestState(r.Base, r.BaseState, false);
                    int put = cw.ServerAddContent(r.Base.Id, bs, n, 0, false);
                    sb.Insert(0, $"{r.Base.Title} {FoodUnits.Describe(put, r.BaseUnit)}; ");
                }
            }
            cw.ServerAgitate(0.5f);
            string what = r.Steps.Count > 0 ? CookRecipeBook.StepTool(r.Steps[0].Kind, r.Driver) : "";
            Flash($"N: «{r.Title}» — положено. Теперь: {what}");
            Debug.Log($"[Отладка N] «{cw.name}»: под рецепт «{r.Title}» положено: {sb}→ {what}", cw);
            _t = 0f;
        }

        /// <summary>
        /// В каком состоянии класть ингредиент для теста (N), чтобы рецепт его засчитал:
        /// задано рецептом (лук — тёртый) → оно; «только чистое» → опалённое (если продукт опаляется: бульон) или чищеное;
        /// мясо (стартовое «туша целиком») → разделанное; иначе — стартовое продукта.
        /// </summary>
        private static FoodState TestState(ProductDef p, FoodState want, bool cleanOnly)
        {
            if (want != FoodState.None) return want;
            FoodState s = p.StartState;
            if (s == FoodState.Carcass) s = FoodState.Butchered;
            if (cleanOnly && p.Overrides != null)
                foreach (FoodRule r in p.Overrides)
                    if (r.Process == FoodProcess.Torch && r.To == FoodState.Torched) return FoodState.Torched;
            if (cleanOnly && (s == FoodState.Dirty || s == FoodState.Raw)) return FoodState.Peeled;
            return s;
        }

        /// <summary>Панель теста кухни сейчас на экране.</summary>
        private static bool PanelShown => Time.frameCount - Tools.BonfireTestKeys.ReservedFrame <= 2;

        private void Flash(string text) { _flash = text; _flashT = 2.5f; }

        private static string Build(Cookware cw)
        {
            var sb = new StringBuilder();
            string title = cw.Def != null ? cw.Def.Title : cw.name;
            float buckets = FoodUnits.BucketsIn(cw);
            if (!PanelShown) sb.AppendLine("<size=11>J +1 ведро воды · Shift+J до полной · K опустошить</size>");
            sb.AppendLine($"<size=11>N — положить всё для «{TestRecipeTitle}» · X — сразу готовое · Shift+N / Ctrl+N — другой рецепт</size>");
            sb.AppendLine($"<b>{title}</b>  (вмещает {FoodUnits.Describe(Mathf.RoundToInt(buckets * FoodUnits.Bucket), CookUnit.Bucket)}, налито {FoodUnits.Describe(cw.TotalCount, CookUnit.Bucket)})   [F4]");
            CookwareHeat heat = cw.GetComponent<CookwareHeat>();
            if (heat != null)
            {
                float tc = heat.TemperatureC;
                sb.AppendLine($"Температура: {tc:0} °C" + (heat.IsBoiling ? " — КИПИТ" : tc > 60f ? " — греется" : ""));
                string car = heat.CaramelStatus();
                if (!string.IsNullOrEmpty(car)) sb.AppendLine("<b>" + car + "</b>");
            }
            CookwareJournal j = CookwareJournal.Peek(cw);
            string loose = LooseLine(cw);
            if (j != null && !j.IsEmpty)
            {
                sb.Append(j.Report());
                if (loose != null) sb.AppendLine(loose);
                string sug = CookRecipeBook.Suggest(cw, j);
                if (!string.IsNullOrEmpty(sug)) sb.AppendLine("➜ " + sug);
                return sb.ToString();
            }
            // клиент без журнала — просто содержимое
            FoodDatabase db = FindAnyObjectByType<FoodDatabase>();
            if (cw.ViewCount == 0) { sb.AppendLine("пусто"); if (j != null && !string.IsNullOrEmpty(j.LastResult)) sb.AppendLine("✔ " + j.LastResult); return sb.ToString(); }
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count == 0) continue;
                ProductDef d = db != null ? db.Get(v.Product) : null;
                sb.AppendLine($"• {(d != null ? d.Title : "#" + v.Product)} — {FoodUnits.Describe(v.Count, FoodUnits.NaturalUnit(d, v.State))}");
            }
            return sb.ToString();
        }

        /// <summary>Куски, что плавают/лежат в таре собой (кастрюля-суп, сковорода) — их тоже видно в списке.</summary>
        private static string LooseLine(Cookware cw)
        {
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (FoodPiece p in cw.Loose)
            {
                if (p == null || p.IsConsumed || p.IsHeld) continue;
                string k = (p.Def != null ? p.Def.Title : p.name) + " (" + FoodStateInfo.NameOf(p.State) + ")";
                counts.TryGetValue(k, out int n);
                counts[k] = n + 1;
            }
            if (counts.Count == 0) return null;
            var parts = new System.Collections.Generic.List<string>();
            foreach (var kv in counts) parts.Add($"{kv.Key} ×{kv.Value}");
            return "Куском в таре: " + string.Join(", ", parts);
        }

        private void OnGUI()
        {
            if (_flashT > 0f && !string.IsNullOrEmpty(_flash))
            {
                _flashT -= Time.unscaledDeltaTime;
                var fs = new GUIStyle(GUI.skin.box) { fontSize = 15 };
                Vector2 sz = fs.CalcSize(new GUIContent(_flash));
                GUI.Box(new Rect(Screen.width * 0.5f - sz.x * 0.5f - 6f, Screen.height * 0.5f + 70f, sz.x + 12f, 26f), _flash, fs);
            }
            if (!Visible || string.IsNullOrEmpty(_text)) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft, richText = true, wordWrap = true };
            float w = 420f;
            float h = _style.CalcHeight(new GUIContent(_text), w);
            // над панелью теста кухни (Bonfire Test Keys), а не поверх неё
            float bottom = PanelShown ? Tools.BonfireTestKeys.ReservedBottom : 90f;
            GUI.Box(new Rect(12f, Screen.height - h - bottom - 8f, w, h + 8f), _text, _style);
        }
    }
}
