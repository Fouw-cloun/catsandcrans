using System.Collections.Generic;
using FishNet;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ГОРЕЛКА ОПАЛЯЕТ ЕДУ НОВОЙ СИСТЕМЫ (FoodPiece): кости, голяшку, лук, морковь для бульона борща.
    ///
    /// Ставить в сцену НЕ НАДО — создаётся сам при запуске игры (объект «FoodTorchHeat (auto)»).
    /// Каждый кадр смотрит на горящие струи (HeatJets: ручная горелка, кровельная) и ищет
    /// продукты в пламени. У продукта есть правило «Опалять горелкой» (меню Борщ → 2) —
    /// на сервере идёт работа FoodProcess.Torch: держишь пламя ~3–5 с — «Опалено», передержал — «Сгорело».
    /// Пламя ушло — прогресс НЕ сбрасывается: водить горелкой можно с перерывами.
    ///
    /// У всех игроков: продукт в пламени темнеет по прогрессу (FoodStateVisual) и над ним подсказка
    /// «опаляется 45%» / «опалено ✔» / «сгорело». По сети ничего своего не шлёт — состояние и прогресс уже
    /// синхронизирует FoodPiece.
    ///
    /// Не трогает то, что опаляется само: какао-зёрна (FoodRoastable), меренга (MeringueCap).
    /// Костям с туши настраивать ничего не надо: вид по состоянию (FoodStateVisual) добавится сам при первом пламени.
    /// </summary>
    public sealed class FoodTorchHeat : MonoBehaviour
    {
        /// <summary>Скорость опаления (1 — как в правилах при мощности горелки 1).</summary>
        public static float Speed = 1f;
        /// <summary>Писать в консоль смену состояния.</summary>
        public static bool Log = true;
        /// <summary>Подсказка над продуктом в пламени.</summary>
        public static bool Hud = true;

        private static FoodTorchHeat _inst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_inst != null) return;
            var go = new GameObject("FoodTorchHeat (auto)");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<FoodTorchHeat>();
        }

        /// <summary>Этот продукт сейчас в пламени (у этого игрока). Для подкраски FoodStateVisual.</summary>
        public static bool IsHeating(FoodPiece p) => _inst != null && p != null && _inst._now.Contains(p);

        private struct Hit { public FoodPiece P; public float K; }
        private struct Block { public FoodPiece P; public string Why; }
        private readonly List<Block> _blocked = new List<Block>();
        private readonly HashSet<FoodPiece> _blockedSet = new HashSet<FoodPiece>();

        /// <summary>
        /// Продукт вообще опаляется (есть правило горелки), но не из этого состояния — подсказка «почисти».
        /// Нет правила — молчим (не всё на свете опаляется).
        /// </summary>
        private static string WhyNot(FoodPiece p)
        {
            ProductDef d = p.Def;
            if (d == null || d.Overrides == null) return null;
            bool torchable = false;
            FoodState need = FoodState.None;
            for (int i = 0; i < d.Overrides.Count; i++)
                if (d.Overrides[i].Process == FoodProcess.Torch && d.Overrides[i].To == FoodState.Torched) { torchable = true; need = d.Overrides[i].From; break; }
            if (!torchable) return null;
            switch (p.State)
            {
                case FoodState.Dirty: return need == FoodState.Peeled ? "грязный — помой и почисть, потом опаляй" : "грязный — сначала помой";
                case FoodState.Washed: return need == FoodState.Peeled ? "в кожуре — сначала почисти" : null;
                case FoodState.Burnt: return "сгорело";
                default: return $"так не опаляется (сейчас: {FoodStateInfo.NameOf(p.State)})";
            }
        }

        private readonly Collider[] _hits = new Collider[96];
        private readonly HashSet<FoodPiece> _now = new HashSet<FoodPiece>();
        private readonly List<Hit> _shown = new List<Hit>();
        private float _pruneAt;

        private void Update()
        {
            _now.Clear();
            _shown.Clear();
            _blocked.Clear(); _blockedSet.Clear();
            if (Time.time >= _pruneAt) { _pruneAt = Time.time + 2f; HeatJets.Prune(); }
            IReadOnlyList<IHeatJet> jets = HeatJets.All;
            if (jets.Count == 0) return;
            bool server = InstanceFinder.IsServerStarted;
            float dt = Time.deltaTime;

            for (int i = 0; i < jets.Count; i++)
            {
                IHeatJet j = jets[i];
                if (j == null || (j is Object o && o == null) || !j.JetActive) continue;
                Vector3 d = j.JetDirection;
                if (d.sqrMagnitude < 1e-6f) continue;
                d.Normalize();
                float len = Mathf.Max(0.05f, j.JetLength) * 1.1f;
                float r = Mathf.Max(0.04f, j.JetRadius) * 1.5f;
                Vector3 a = j.JetOrigin, b = a + d * len;
                int n = Physics.OverlapCapsuleNonAlloc(a, b, r, _hits, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    Collider c = _hits[k];
                    if (c == null) continue;
                    if (j.JetRoot != null && c.transform.IsChildOf(j.JetRoot)) continue;
                    FoodPiece p = c.attachedRigidbody != null ? c.attachedRigidbody.GetComponent<FoodPiece>() : null;
                    if (p == null) p = c.GetComponentInParent<FoodPiece>();
                    if (p == null || p.IsConsumed || _now.Contains(p)) continue;
                    if (OwnTorch(p)) continue;
                    if (!p.CanApplyNow(FoodProcess.Torch))
                    {
                        string why = WhyNot(p);
                        if (why != null && !_blockedSet.Contains(p)) { _blockedSet.Add(p); _blocked.Add(new Block { P = p, Why = why }); }
                        continue;
                    }
                    EnsureLook(p);
                    float along = Mathf.Clamp01(Vector3.Dot(c.bounds.center - a, d) / len);
                    float heat = Mathf.Max(0.3f, j.JetPower) * Mathf.Lerp(1f, 0.5f, along);
                    _now.Add(p);
                    _shown.Add(new Hit { P = p, K = heat });
                    if (!server) continue;
                    FoodState was = p.State;
                    if (p.ServerAddWork(FoodProcess.Torch, dt * heat * Speed) && Log)
                        Debug.Log($"[Горелка] «{(p.Def != null ? p.Def.Title : p.name)}»: {FoodStateInfo.NameOf(was)} → {FoodStateInfo.NameOf(p.State)}" +
                                  (p.State == FoodState.Torched ? " — хватит, дальше сгорит" : p.State == FoodState.Burnt ? " — передержал" : ""), p);
                }
            }
        }

        /// <summary>
        /// Кости с туши (и любой продукт без вида по состоянию) — вид вешается сам при первом пламени,
        /// у каждого игрока (чисто визуальный компонент): темнеет в пламени, «Опалено» — коричневое, «Сгорело» — уголь.
        /// Мясо туши (MeatCut) красится своим кодом — его не трогаем.
        /// </summary>
        private static void EnsureLook(FoodPiece p)
        {
            if (p is MeatCut || p.GetComponent<FoodStateVisual>() != null) return;
            p.gameObject.AddComponent<FoodStateVisual>();
        }

        /// <summary>Опаляется своим кодом — не мешаем.</summary>
        private static bool OwnTorch(FoodPiece p)
        {
            if (p.GetComponent<FoodRoastable>() != null) return true;
            if (p.GetComponentInParent<MeringueCap>() != null) return true;
            return false;
        }

        // ───────────────────────── подсказка ─────────────────────────

        private GUIStyle _style;

        private void OnGUI()
        {
            if (!Hud || (_shown.Count == 0 && _blocked.Count == 0)) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = false };
            for (int i = 0; i < _blocked.Count; i++)
            {
                FoodPiece p = _blocked[i].P;
                if (p == null) continue;
                Vector3 s = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 0.25f);
                if (s.z <= 0f || s.z > 20f) continue;
                string text = (p.Def != null ? p.Def.Title : p.name) + ": " + _blocked[i].Why;
                Vector2 size = _style.CalcSize(new GUIContent(text));
                GUI.Box(new Rect(s.x - size.x * 0.5f, Screen.height - s.y - size.y, size.x + 8f, size.y + 4f), text, _style);
            }
            for (int i = 0; i < _shown.Count; i++)
            {
                FoodPiece p = _shown[i].P;
                if (p == null) continue;
                Vector3 w = p.transform.position + Vector3.up * 0.25f;
                if ((w - cam.transform.position).sqrMagnitude > 20f * 20f) continue;
                Vector3 s = cam.WorldToScreenPoint(w);
                if (s.z <= 0f) continue;
                string t;
                FoodState st = p.State;
                if (st == FoodState.Burnt) t = "сгорело!";
                else if (st == FoodState.Torched) t = "опалено ✔ — убери пламя";
                else t = $"опаляется {Mathf.RoundToInt(p.Progress01 * 100f)}%";
                string title = p.Def != null ? p.Def.Title : p.name;
                string text = title + ": " + t;
                Vector2 size = _style.CalcSize(new GUIContent(text));
                GUI.Box(new Rect(s.x - size.x * 0.5f, Screen.height - s.y - size.y, size.x + 8f, size.y + 4f), text, _style);
            }
        }
    }
}
