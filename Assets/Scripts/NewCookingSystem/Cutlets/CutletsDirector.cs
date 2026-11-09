using System.Collections.Generic;
using FishNet;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КОТЛЕТЫ — общие мелочи, ставить в сцену не нужно (создаётся сам):
    ///   • Хлеб (Буханка / Багет / Хлеб), очищенный ШЛИФМАШИНКОЙ («Чистить» → «Чищеное»), становится продуктом «Мякиш» (сервер);
    ///     у всех — светлый, без корки (подкраска батона цветом мякиша).
    ///   • Кучка «Фарш отбитый» на столе/полу (бросок мастерком) становится КОМКОМ — котлетой для формовки (префаб — CutletSettings).
    ///   • Рецепты, которые начинаются с «Выдержать» (хлебная масса: мякиш размокает в молоке), идут сами в любой таре,
    ///     даже без нагрева (ведро). Где есть Cookware Heat и рецепт уже идёт — его ведёт нагрев.
    /// </summary>
    public sealed class CutletsDirector : MonoBehaviour
    {
        private static CutletsDirector _inst;
        private float _scanAt, _restAt, _heapAt, _warnAt;
        private ProductDef _patty;
        private ProductDef _crumb;
        private readonly List<FoodPiece> _pieces = new List<FoodPiece>();
        private MaterialPropertyBlock _mpb;
        private static readonly Color CrumbColor = new Color(0.97f, 0.93f, 0.82f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_inst != null) return;
            var go = new GameObject("CutletsDirector (auto)") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<CutletsDirector>();
        }

        private static bool IsBread(ProductDef d)
        {
            if (d == null || string.IsNullOrEmpty(d.Title)) return false;
            string t = d.Title.Trim().ToLowerInvariant();
            return t == "буханка" || t == "багет" || t == "хлеб" || t == "батон";
        }

        private ProductDef Crumb()
        {
            if (_crumb != null) return _crumb;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return null;
            for (int id = 1; id < 4096; id++)
            {
                ProductDef p = db.Get((ushort)id);
                if (p != null && p.Title == CookRecipeBook.Cutlets.Crumb) { _crumb = p; return p; }
            }
            return null;
        }

        private void Update()
        {
            if (Time.time >= _scanAt)
            {
                _scanAt = Time.time + 0.5f;
                _pieces.Clear();
                _pieces.AddRange(Object.FindObjectsByType<FoodPiece>(FindObjectsSortMode.None));
                bool server = InstanceFinder.IsServerStarted;
                ProductDef crumb = Crumb();
                foreach (FoodPiece p in _pieces)
                {
                    if (p == null || p.IsConsumed) continue;
                    ProductDef d = p.Def;
                    if (server && crumb != null && IsBread(d) && p.State == FoodState.Peeled)
                    {
                        p.ServerSetProductState(crumb, FoodState.Peeled);
                        Debug.Log($"[Котлеты] «{p.name}»: корку сняли — «Мякиш». Целиком в ведро с молоком", p);
                    }
                    if (d != null && d.Title == CookRecipeBook.Cutlets.Crumb) Tint(p);
                }
            }
            if (InstanceFinder.IsServerStarted && Time.time >= _heapAt) { _heapAt = Time.time + 0.3f; ServerHeapsToPatties(); }
            if (InstanceFinder.IsServerStarted && Time.time >= _restAt)
            {
                float dt = 0.2f;
                _restAt = Time.time + dt;
                foreach (Cookware cw in Cookware.All)
                {
                    if (cw == null || !cw.IsServerInitialized) continue;
                    if (!CookRecipeBook.Wants(cw, CookStepKind.Rest)) continue;
                    CookwareJournal j = CookwareJournal.Peek(cw);
                    bool running = j != null && j.Recipe != null;
                    if (running && cw.GetComponent<CookwareHeat>() != null) continue;      // идёт — ведёт нагрев
                    CookRecipeBook.ServerProcess(cw, CookStepKind.Rest, dt);
                }
            }
        }

        /// <summary>Кучка отбитого фарша (легла на стол/пол) → комок-котлета. Рядом уже есть комок, который ещё не формуют, — досыпать в него.</summary>
        private void ServerHeapsToPatties()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            FoodHeap[] heaps = Object.FindObjectsByType<FoodHeap>(FindObjectsSortMode.None);
            if (heaps.Length == 0) return;
            CutletSettings st = CutletSettings.Instance;
            foreach (FoodHeap h in heaps)
            {
                if (h == null || !h.IsSettled || h.Count <= 0 || !h.IsServerInitialized) continue;
                ProductDef d = h.Def;
                if (d == null || d.Title != CookRecipeBook.Cutlets.Pounded) continue;
                // досыпать в комок рядом
                CutletPatty near = null; float bd = 1.2f;
                foreach (CutletPatty p in Object.FindObjectsByType<CutletPatty>(FindObjectsSortMode.None))
                {
                    if (p == null || p.Shape01 > 0.15f) continue;
                    float dd = Vector3.Distance(p.transform.position, h.transform.position);
                    if (dd < bd) { bd = dd; near = p; }
                }
                if (near != null)
                {
                    int got = near.ServerReceiveScoop(h.ProductId, h.State, h.Count, h.transform.position, Vector3.up, out string why);
                    if (got > 0) h.ServerTake(got);
                    continue;
                }
                if (st == null || st.PattyPrefab == null)
                {
                    if (Time.time >= _warnAt) { _warnAt = Time.time + 8f; Debug.LogWarning("[Котлеты] кучка отбитого фарша не стала комком: нет CutletSettings с префабом котлеты — меню Котлеты → 4, сохранить префаб, Котлеты → 5", h); }
                    continue;
                }
                if (h.Count < st.MinHeapPortions) continue;
                ProductDef patty = Patty(db);
                int n = Mathf.Min(h.Count, st.MaxPortions);
                GameObject go = Object.Instantiate(st.PattyPrefab, h.transform.position + Vector3.up * 0.02f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                FoodPiece fp = go.GetComponent<FoodPiece>();
                if (fp == null) { Debug.LogError("[Котлеты] на префабе котлеты нет FoodPiece", st.PattyPrefab); Object.Destroy(go); return; }
                fp.ServerPreset(patty, FoodState.Raw, h.Clean, 0, 100);
                InstanceFinder.ServerManager.Spawn(go);
                fp.ServerSetCount(n);
                h.ServerTake(n);
                Debug.Log($"[Котлеты] кучка отбитого фарша → комок ({n} порц.). Ещё мастерок на комок — больше; мастерком ЛКМ зажать и водить по кругу — формовать", go);
            }
        }

        private ProductDef Patty(FoodDatabase db)
        {
            if (_patty != null) return _patty;
            for (int id = 1; id < 4096; id++)
            {
                ProductDef p = db.Get((ushort)id);
                if (p != null && p.Title == CookRecipeBook.Cutlets.Patty) { _patty = p; return p; }
            }
            return null;
        }

        /// <summary>Батон без корки: светлый (подкраска всех мешей куска).</summary>
        private void Tint(FoodPiece p)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", CrumbColor);
                _mpb.SetColor("_Color", CrumbColor);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
