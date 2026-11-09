using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>Продукт «Ньокки» (меню Ньокки → 8). Состояния: Сформовано — сырые, Варёное — всплыли, ПЕРЕготовлено — разварились.</summary>
    public static class GnocchiProduct
    {
        public const string Title = "Ньокки";
        public const FoodState Raw = FoodState.Shaped, Done = FoodState.Boiled, Over = FoodState.Overcooked;
        private static ProductDef _def; private static FoodDatabase _forDb;

        public static ProductDef Def
        {
            get
            {
                FoodDatabase db = FoodDatabase.Instance;
                if (db == null) return null;
                if (_def != null && _forDb == db) return _def;
                _forDb = db; _def = null;
                for (int id = 1; id < 4096 && _def == null; id++)
                {
                    ProductDef p = db.Get((ushort)id);
                    if (p != null && p.Title == Title) _def = p;
                }
                return _def;
            }
        }
        public static ushort Id => Def != null ? Def.Id : (ushort)0;
        public static bool Is(ushort product) => product != 0 && product == Id;
    }

    /// <summary>
    /// ВАРКА НЬОККОВ. Сам ставит себя в сцену (один на всех), кастрюлю настраивать не надо.
    ///   Сервер: пока в кастрюле КИПИТ вода — сырые ньокки варятся; через Float At секунд всплывают (Варёное, точность 100),
    ///           дальше точность падает, через Over At — разварились (ПЕРЕготовлено). Всплывшие — сверху: лопата берёт их первыми.
    ///   У всех: в кастрюле — ровные ньокки-«ригате» (картинка, без физики): сырые лежат на дне, всплывшие качаются на воде.
    /// </summary>
    public sealed class GnocchiBoiler : MonoBehaviour
    {
        public static float FloatAt = 20f;        // сек кипения до всплытия
        public static float PerfectFor = 6f;      // после всплытия — ещё столько «идеально»
        public static float OverAt = 30f;         // после всплытия — разварились
        public static bool Log = true;

        private sealed class Pot { public float RawT, DoneT; public int Raw, Done; public bool Warned; }
        private readonly Dictionary<Cookware, Pot> _pots = new Dictionary<Cookware, Pot>();
        private float _tickAt;
        private static GnocchiBoiler _inst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_inst != null) return;
            var go = new GameObject("GnocchiBoiler (auto)");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<GnocchiBoiler>();
        }

        private void Update()
        {
            if (Time.time < _tickAt) return;
            float dt = 0.2f; _tickAt = Time.time + dt;
            ushort id = GnocchiProduct.Id;
            ResolveSauce();
            if (id == 0 && _sauceA == 0) return;
            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null) continue;
                // соус (масло с шалфеем) в таре — свой вид (у всех); тарелка рисует соус сама
                if (_sauceA != 0 && cw.GetComponent<GnocchiSauceVisual>() == null && cw.GetComponent<GnocchiPlate>() == null && HasAny(cw, _sauceA, _sauceB))
                    cw.gameObject.AddComponent<GnocchiSauceVisual>();
                if (id == 0) continue;
                // вид — у всех
                int raw = 0, done = 0, over = 0;
                for (int i = 0; i < cw.ViewCount; i++)
                {
                    BagView v = cw.GetView(i);
                    if (v.Product != id) continue;
                    if (v.State == GnocchiProduct.Raw) raw += v.Count; else if (v.State == GnocchiProduct.Done) done += v.Count; else over += v.Count;
                }
                if (raw + done + over > 0 && cw.GetComponent<GnocchiPotVisual>() == null) cw.gameObject.AddComponent<GnocchiPotVisual>();
                if (cw.IsServerInitialized) ServerPot(cw, id, dt);
            }
        }

        private ushort _sauceA, _sauceB;
        private float _sauceRetry;

        private void ResolveSauce()
        {
            if (_sauceA != 0 || Time.time < _sauceRetry) return;
            _sauceRetry = Time.time + 3f;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || db.Table == null) return;
            for (int i = 1; i < 2048; i++)
            {
                ProductDef p = db.Table.GetProduct((ushort)i);
                if (p == null) continue;
                if (p.Title == CookRecipeBook.Gnocchi.SageButter) _sauceA = p.Id;
                else if (p.Title == CookRecipeBook.Gnocchi.BurntButter) _sauceB = p.Id;
            }
        }

        private static bool HasAny(Cookware cw, ushort a, ushort b)
        {
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count > 0 && (v.Product == a || (b != 0 && v.Product == b))) return true;
            }
            return false;
        }

        /// <summary>Сервер: таймер кастрюли (средний по ньоккам: докинул новых — время «разбавилось»).</summary>
        private void ServerPot(Cookware cw, ushort id, float dt)
        {
            ContentBag bag = cw.Bag;
            int raw = 0, done = 0;
            foreach (BagEntry e in bag.Entries)
                if (e.Product == id) { if (e.State == GnocchiProduct.Raw) raw += e.Count; else if (e.State == GnocchiProduct.Done) done += e.Count; }
            if (!_pots.TryGetValue(cw, out Pot p))
            {
                if (raw + done == 0) return;
                p = new Pot(); _pots[cw] = p;
            }
            if (raw > p.Raw) p.RawT = p.RawT * p.Raw / Mathf.Max(1, raw);
            if (done > p.Done) p.DoneT = p.DoneT * p.Done / Mathf.Max(1, done);
            p.Raw = raw; p.Done = done;
            if (raw + done == 0) { _pots.Remove(cw); return; }

            CookwareHeat heat = cw.GetComponent<CookwareHeat>();
            bool boiling = heat != null && heat.ServerBoiling;
            if (!boiling)
            {
                if (raw > 0 && !p.Warned && Log) { p.Warned = true; Debug.Log($"[Ньокки] «{cw.name}»: ньокки в кастрюле, но вода НЕ кипит — ждут (кидать надо в кипящую воду)", cw); }
                return;
            }
            p.Warned = false;
            if (raw > 0) p.RawT += dt;
            if (done > 0) p.DoneT += dt;

            bool changed = false;
            if (raw > 0 && p.RawT >= FloatAt)
            {
                Move(bag, id, GnocchiProduct.Raw, raw);
                cw.ServerAddContent(id, GnocchiProduct.Done, raw, 100, false);
                p.DoneT = p.DoneT * done / Mathf.Max(1, done + raw);
                p.Raw = 0; p.RawT = 0f; p.Done = done + raw; done += raw; raw = 0;
                changed = true;
                if (Log) Debug.Log($"[Ньокки] «{cw.name}»: ВСПЛЫЛИ {p.Done} шт — вылавливай лопатой! (идеально ещё ~{PerfectFor:0} с, разварятся через {OverAt:0} с)", cw);
            }
            if (done > 0)
            {
                float acc = p.DoneT <= PerfectFor ? 1f : Mathf.Clamp01(1f - (p.DoneT - PerfectFor) / Mathf.Max(1f, OverAt - PerfectFor));
                byte cook = (byte)Mathf.RoundToInt(acc * 100f);
                for (int i = 0; i < bag.Entries.Count; i++)
                {
                    BagEntry e = bag.Entries[i];
                    if (e.Product == id && e.State == GnocchiProduct.Done && e.Cook != cook) { e.Cook = cook; bag.Entries[i] = e; }
                }
                if (p.DoneT >= OverAt)
                {
                    Move(bag, id, GnocchiProduct.Done, done);
                    cw.ServerAddContent(id, GnocchiProduct.Over, done, 0, false);
                    p.Done = 0; p.DoneT = 0f;
                    changed = true;
                    if (Log) Debug.LogWarning($"[Ньокки] «{cw.name}»: передержал — {done} шт РАЗВАРИЛИСЬ (качество 0)", cw);
                }
            }
            // всплывшие — наверх (лопата берёт последнюю позицию), сырые — под ними
            if (changed || Time.frameCount % 5 == 0) SortTop(bag, id);
        }

        /// <summary>Убрать count ньокков в состоянии st из содержимого (без публикации — её сделает следующий AddContent).</summary>
        private static void Move(ContentBag bag, ushort id, FoodState st, int count)
        {
            for (int i = bag.Entries.Count - 1; i >= 0 && count > 0; i--)
            {
                BagEntry e = bag.Entries[i];
                if (e.Product != id || e.State != st) continue;
                int t = Mathf.Min(count, e.Count); e.Count -= t; count -= t;
                if (e.Count <= 0) bag.Entries.RemoveAt(i); else bag.Entries[i] = e;
            }
        }

        private static readonly List<BagEntry> _tmp = new List<BagEntry>();
        private static void SortTop(ContentBag bag, ushort id)
        {
            _tmp.Clear();
            for (int r = 0; r <= 3; r++) foreach (BagEntry e in bag.Entries) if (Rank(e, id) == r) _tmp.Add(e);
            bag.Entries.Clear(); bag.Entries.AddRange(_tmp);
        }

        private static int Rank(BagEntry e, ushort id) => e.Product != id ? 0 : e.State == GnocchiProduct.Raw ? 1 : e.State == GnocchiProduct.Over ? 2 : 3;

        /// <summary>Сколько секунд кипения осталось до всплытия (сервер; клиент — −1).</summary>
        public static float SecondsToFloat(Cookware cw)
        {
            if (_inst == null || cw == null || !_inst._pots.TryGetValue(cw, out Pot p) || p.Raw == 0) return -1f;
            return Mathf.Max(0f, FloatAt - p.RawT);
        }
        public static float SecondsAfloat(Cookware cw)
        {
            if (_inst == null || cw == null || !_inst._pots.TryGetValue(cw, out Pot p) || p.Done == 0) return -1f;
            return p.DoneT;
        }
    }

    /// <summary>
    /// ВИД ньокков в кастрюле (вешается сам, только картинка): до 24 штук ровной формы «ригате» —
    /// сырые на дне, всплывшие поднимаются и качаются на кипящей воде, разварившиеся — бледные и расплывшиеся.
    /// Кучку этого продукта в кастрюле не рисуем (вместо неё — эти ньокки).
    /// </summary>
    public sealed class GnocchiPotVisual : MonoBehaviour
    {
        private const int Max = 24;
        private Cookware _cw;
        private CookwareLiquidVisual _lv;
        private CookwareHeat _heat;
        private readonly List<Transform> _items = new List<Transform>();
        private readonly List<float> _rise = new List<float>();     // 0 — на дне, 1 — на воде
        private readonly List<int> _kind = new List<int>();         // 0 сырой, 1 всплыл, 2 разварился
        private int _raw, _done, _over;
        private float _lookAt; private bool _looking;
        private static Material _mRaw, _mDone, _mOver;
        private static GUIStyle _st;

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _lv = GetComponentInChildren<CookwareLiquidVisual>(true);
            _heat = GetComponent<CookwareHeat>();
            HideHeap();
            if (_cw != null) _cw.ContentsChanged += Recount;
            Recount();
        }

        private void OnDestroy()
        {
            if (_cw != null) _cw.ContentsChanged -= Recount;
            foreach (Transform t in _items) if (t != null) Destroy(t.gameObject);
        }

        /// <summary>Кучку ньокков в кастрюле не рисовать — добавить продукт в Ignore у Cookware Heap Visual.</summary>
        private void HideHeap()
        {
            ProductDef d = GnocchiProduct.Def;
            if (d == null) return;
            foreach (CookwareHeapVisual hv in GetComponentsInChildren<CookwareHeapVisual>(true))
            {
                FieldInfo f = typeof(CookwareHeapVisual).GetField("_ignore", BindingFlags.Instance | BindingFlags.NonPublic);
                if (f == null) continue;
                var arr = f.GetValue(hv) as ProductDef[];
                if (arr != null && System.Array.IndexOf(arr, d) >= 0) continue;
                var list = new List<ProductDef>(arr ?? new ProductDef[0]) { d };
                f.SetValue(hv, list.ToArray());
                MethodInfo m = typeof(CookwareHeapVisual).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (m != null && m.GetParameters().Length == 0) m.Invoke(hv, null);
            }
        }

        private void Recount()
        {
            ushort id = GnocchiProduct.Id;
            _raw = _done = _over = 0;
            if (_cw == null || id == 0) return;
            for (int i = 0; i < _cw.ViewCount; i++)
            {
                BagView v = _cw.GetView(i);
                if (v.Product != id) continue;
                if (v.State == GnocchiProduct.Raw) _raw += v.Count; else if (v.State == GnocchiProduct.Done) _done += v.Count; else _over += v.Count;
            }
            int total = Mathf.Min(Max, _raw + _done + _over);
            while (_items.Count < total) AddItem();
            while (_items.Count > total) { int k = _items.Count - 1; if (_items[k] != null) Destroy(_items[k].gameObject); _items.RemoveAt(k); _rise.RemoveAt(k); _kind.RemoveAt(k); }
            // распределить виды пропорционально (сначала всплывшие — они сверху)
            int all = Mathf.Max(1, _raw + _done + _over);
            int nd = Mathf.RoundToInt(total * _done / (float)all), no = Mathf.RoundToInt(total * _over / (float)all);
            for (int i = 0; i < _items.Count; i++)
            {
                int k = i < nd ? 1 : i < nd + no ? 2 : 0;
                if (_kind[i] != k)
                {
                    _kind[i] = k;
                    MeshRenderer r = _items[i].GetComponent<MeshRenderer>();
                    r.sharedMaterial = k == 0 ? MatRaw : k == 1 ? MatDone : MatOver;
                }
            }
            HideHeap();
        }

        private void AddItem()
        {
            var go = new GameObject("Gnocco");
            go.AddComponent<MeshFilter>().sharedMesh = GnocchiMesh.Get();
            go.AddComponent<MeshRenderer>().sharedMaterial = MatRaw;
            go.transform.SetParent(transform, true);
            _items.Add(go.transform); _rise.Add(0f); _kind.Add(0);
        }

        private void LateUpdate()
        {
            if (_cw == null || _items.Count == 0) return;
            Vector3 c = _lv != null ? _lv.transform.position : _cw.MouthPoint;
            float rad = _lv != null ? _lv.RadiusAtRimWorld * 0.62f : 0.12f;
            float surf = c.y + 0.1f;
            if (_lv != null && _lv.TryGetSurfaceY(new Vector3(c.x, c.y, c.z), out float sy)) surf = sy;
            float bottom = _lv != null ? surf - Mathf.Max(0.02f, _lv.Level) : c.y;
            bool boiling = _heat != null && _heat.IsBoiling;
            Vector3 dims = GnocchiMesh.Dims;
            if (dims.z > rad * 1.1f) dims *= rad * 1.1f / dims.z;                       // кастрюля маленькая — ньокки поменьше
            float size = dims.z;
            int fit = Mathf.Clamp(Mathf.FloorToInt(Mathf.PI * rad * rad / (dims.x * dims.z * 1.3f)) * 2, 3, Max);   // два слоя
            float t = Time.time;
            for (int i = 0; i < _items.Count; i++)
            {
                Transform it = _items[i]; if (it == null) continue;
                int k = _kind[i];
                float target = k == 0 ? 0f : 1f;
                _rise[i] = Mathf.MoveTowards(_rise[i], target, Time.deltaTime / 1.4f);   // всплывают плавно
                float seed = i * 2.399f;                                                  // «золотой угол» — ровно по кругу
                float ring = Mathf.Sqrt((i + 0.5f) / Max);
                float drift = boiling ? t * (0.35f + 0.1f * (i % 3)) : 0f;
                float ang = seed + drift * (k == 0 ? 0.2f : 1f);
                Vector3 p = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (rad * ring);
                float bob = boiling ? Mathf.Sin(t * 3.1f + seed) * size * 0.12f : 0f;
                float layer = (i % 2) * dims.y * 0.8f;
                float y = Mathf.Lerp(bottom + dims.y * 0.5f + layer, surf - dims.y * 0.15f + bob * _rise[i], Mathf.SmoothStep(0f, 1f, _rise[i]));
                p.y = y;
                it.gameObject.SetActive(i < fit);
                Vector3 sc = dims;
                if (k == 2) sc = new Vector3(dims.x * 1.3f, dims.y * 0.6f, dims.z * 1.1f);   // расплылись
                it.position = p;
                it.rotation = Quaternion.Euler(boiling && k > 0 ? Mathf.Sin(t * 2.3f + seed) * 12f : 0f, seed * Mathf.Rad2Deg + drift * 30f, 0f);
                it.localScale = new Vector3(sc.x / Mathf.Max(1e-4f, transform.lossyScale.x), sc.y / Mathf.Max(1e-4f, transform.lossyScale.y), sc.z / Mathf.Max(1e-4f, transform.lossyScale.z));
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _raw + _done + _over == 0) return;
            if (Time.time >= _lookAt)
            {
                _lookAt = Time.time + 0.2f;
                Camera cam = Camera.main;
                _looking = cam != null && Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit h, 6f, ~0, QueryTriggerInteraction.Collide)
                           && h.collider.GetComponentInParent<Cookware>() == _cw;
            }
            if (!_looking) return;
            bool boiling = _heat != null && _heat.IsBoiling;
            float toFloat = GnocchiBoiler.SecondsToFloat(_cw), afloat = GnocchiBoiler.SecondsAfloat(_cw);
            string s = "<b>Ньокки</b>";
            if (_raw > 0) s += boiling ? $" · на дне {_raw}" + (toFloat >= 0f ? $" (всплывут через ~{toFloat:0} с)" : " (варятся)") : $" · на дне {_raw} — <color=#FF8A5A>вода не кипит</color>";
            if (_done > 0) s += $" · <color=#9BE37A>всплыли {_done} — лови лопатой!</color>" + (afloat >= 0f ? (afloat <= GnocchiBoiler.PerfectFor ? " (идеально)" : $" (разварятся через ~{Mathf.Max(0f, GnocchiBoiler.OverAt - afloat):0} с)") : "");
            if (_over > 0) s += $" · <color=#FF6A4A>разварились {_over}</color>";
            if (_st == null) _st = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            Rect r = new Rect(Screen.width * 0.5f - 320f, Screen.height * 0.55f, 640f, 30f);
            Color old = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(r, s, _st);
        }

        private static Material MatRaw => _mRaw != null ? _mRaw : (_mRaw = GnocchiLooks.Mat("Gnocchi raw", new Color(0.95f, 0.82f, 0.50f), 0.25f, GnocchiLooks.DoughTex, 2f));
        private static Material MatDone => _mDone != null ? _mDone : (_mDone = GnocchiLooks.Mat("Gnocchi boiled", new Color(1f, 0.91f, 0.63f), 0.55f, GnocchiLooks.DoughTex, 2f));
        private static Material MatOver => _mOver != null ? _mOver : (_mOver = GnocchiLooks.Mat("Gnocchi overcooked", new Color(0.97f, 0.95f, 0.86f), 0.7f, GnocchiLooks.DoughTex, 2f));

        private static Material Lit(string name, Color c, float smooth)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            var m = new Material(s) { name = name + " (runtime)" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }
    }

    /// <summary>
    /// ФОРМА НЬОККА «ригате» — как скатанный по доске ньокк: пухлая подушечка, чуть изогнутая «ракушкой»
    /// (концы приподняты), концы сужены, СВЕРХУ И ПО БОКАМ — бороздки поперёк (рисунок доски), низ гладкий и площе.
    /// Меш в единичном кубе: длина — Z, ширина — X, высота — Y. Бороздки — геометрией (видны светом при любом материале).
    /// Размер в мире — Length (≈ 30 см при масштабе мира 4 — крупные, «гигантские» под котов).
    /// </summary>
    public static class GnocchiMesh
    {
        public static float Length => 0.3f * Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
        public static Vector3 Dims => new Vector3(0.64f, 0.48f, 1f) * Length;          // ширина, высота, длина

        public const int Ridges = 8;            // бороздок вдоль длины
        private static readonly Dictionary<int, Mesh> _cache = new Dictionary<int, Mesh>();
        public static Mesh Get() => Get(1f);

        public static Mesh Get(float shade)
        {
            int key = Mathf.RoundToInt(shade * 100f);
            if (_cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            const int L = 64, A = 32;                         // L — вдоль длины, A — вокруг сечения
            var v = new List<Vector3>(); var c = new List<Color32>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= L; i++)
            {
                float s = i / (float)L;                       // 0..1 вдоль
                float z = s - 0.5f;
                float e = Mathf.Max(0f, 1f - 4f * z * z);     // 1 в середине, 0 на концах
                float hw = 0.5f * Mathf.Pow(e, 0.42f) * (1f - 0.1f * 4f * z * z);    // полуширина: концы сужены, но круглые
                float hh = 0.5f * Mathf.Pow(e, 0.5f);         // полувысота
                float lift = 0.16f * 4f * z * z;              // «ракушка»: концы чуть приподняты
                // бороздка: узкие желобки поперёк длины
                float ph = s * Ridges * Mathf.PI * 2f;
                float groove = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ph), 9f);
                for (int j = 0; j <= A; j++)
                {
                    float th = j / (float)A * Mathf.PI * 2f;  // 0 — бок (+X), π/2 — верх
                    float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
                    float x = cs * hw;
                    float y = sn * hh;
                    if (y < 0f) y *= 0.55f;                   // низ площе — лежит
                    float top = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.25f, 0.45f, sn));   // где бороздки: верх и бока
                    float d = 1f - 0.065f * groove * top * Mathf.Clamp01(e * 3f);
                    x *= d; y = y > 0f ? y * d : y;
                    y += 0.012f * Mathf.Sin(th * 3f + s * 7f) * e;                     // лёгкая ручная неровность
                    v.Add(new Vector3(x, y + lift, z));
                    float sh = shade * (sn < -0.4f ? 0.92f : 1f) * (1f - 0.12f * groove * top);
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(255f * Mathf.Clamp01(sh)), 0, 255);
                    c.Add(new Color32(b, b, b, 255));
                    uv.Add(new Vector2(j / (float)A * 1.5f, s * 2.2f));
                }
            }
            int row = A + 1;
            for (int i = 0; i < L; i++)
                for (int j = 0; j < A; j++)
                {
                    int i0 = i * row + j, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                    t.Add(i0); t.Add(i1); t.Add(i2);
                    t.Add(i1); t.Add(i3); t.Add(i2);
                }
            Vector3 mn = v[0], mx = v[0];
            foreach (Vector3 p in v) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            Vector3 cen = (mn + mx) * 0.5f, ext = mx - mn;
            for (int i = 0; i < v.Count; i++) { Vector3 p = v[i] - cen; v[i] = new Vector3(p.x / ext.x, p.y / ext.y, p.z / ext.z); }
            int bad = 0; foreach (Vector3 p in v) if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) bad++;
            if (bad > 0) Debug.LogError($"[Ньокки] форма ньокка: {bad} вершин NaN — меш будет невидим (сообщи)");
            var m = new Mesh { name = "Gnocco", hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            Vector3[] nn = m.normals;
            int probe = (L / 2) * row + A / 4;                                           // середина, верх — нормаль вверх
            if (nn[probe].y < 0f)
            {
                for (int i = 0; i < t.Count; i += 3) { int q = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = q; }
                m.SetTriangles(t, 0); m.RecalculateNormals();
            }
            m.RecalculateTangents(); m.RecalculateBounds();
            _cache[key] = m;
            return m;
        }
    }
}
