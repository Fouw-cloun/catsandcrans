using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Цель, у которой режут КОНКРЕТНУЮ часть — ту, по шву которой ведут пилу (туша).
/// Бензопила, если цель это умеет, передаёт, какую часть и в какой точке пилят.
/// </summary>
public interface IPartCuttable : ICuttable
{
    /// <summary>Какая часть под точкой касания (по коллайдеру). −1 — ни одна.</summary>
    int PartAt(Collider hit, Vector3 point);
    /// <summary>Можно ли сейчас отпилить эту часть, касаясь в этой точке. why — подсказка, если нет.</summary>
    bool CanCutPart(int part, Vector3 point, out string why);
    /// <summary>Отпилить часть. Только сервер.</summary>
    void ServerCutPart(int part, Vector3 point, ProcessDef cutProcess, float quality01);
    /// <summary>Где сейчас пилят (для крошек и подачи пилы у всех).</summary>
    void ServerSetFocus(int part);
}

namespace CatCooks.Food
{
    /// <summary>
    /// ТУША (говяжья, свиная) — собрана из частей: лопатки, рёбра, окорока.
    ///
    /// ═══ КАК РЕЖЕТСЯ ═══
    /// Бензопилой: заводишь, ведёшь пилой ПО ШВУ — по краю куска, там, где он
    /// примыкает к соседнему (Edge Width). Подержал Seconds Per Cut — кусок
    /// отваливается и падает отдельным предметом (FoodPiece «Разделано»).
    /// Посреди куска пила не берёт — подсказка «веди по шву». Последний кусок
    /// режется где угодно. Когда отваливается последний — из туши выпадают КОСТИ
    /// (отдельные предметы, продукт «Кости для бульона»), сама туша исчезает.
    ///
    /// Кусок туши дальше: в измельчитель → фарш (говядина — «Говяжий фарш» по
    /// рецепту, свинина — свинина-фарш), или резать, мариновать, жарить.
    ///
    /// ═══ КАК НАСТРОИТЬ ═══
    /// Меню CatCooks → Мясо → «Настроить выделенную тушу» делает всё само:
    /// FoodPiece (Говядина/Свинина, «Туша целиком»), этот компонент, префаб
    /// куска с мешами частей, префаб костей. Руками:
    ///   Parts        — пусто: соберёт дочерние меши (у каждой части свой BoxCollider)
    ///   Piece Prefab — FoodPiece + SlicePiece (меши частей в том же порядке, что Parts)
    ///   Bone Prefab  — FoodPiece с продуктом «Кости для бульона»
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class FoodCarcass : NetworkBehaviour, IPartCuttable
    {
        [Header("Части")]
        [Tooltip("Части туши. Пусто — все дочерние меши")]
        [SerializeField] private Transform[] _parts;
        [Tooltip("Префаб отпиленного куска: FoodPiece + SlicePiece (меши в порядке Parts)")]
        [SerializeField] private GameObject _piecePrefab;

        [Header("Новая туша (Новые туши → 2/3)")]
        [Tooltip("Общий префаб куска (Meat Cut). Пусто — из MeatCutSettings. Части с Meat Part Tag выпадают им: " +
                 "своим продуктом (корейка, вырезка, рулька…), той же формы, что были в туше")]
        [SerializeField] private GameObject _cutPrefab;

        [Header("Брискет (говяжья туша)")]
        [Tooltip("Префаб куска брискета (Ribs Rack с галкой Brisket, меню Брискет → 2). Задан — низ говяжьей туши выпадает им")]
        [SerializeField] private GameObject _brisketPrefab;

        [Header("Рёбра (барбекю)")]
        [Tooltip("Префаб пласта рёбер (Ribs Rack, меню Барбекю → 2). Задан — вместо куска-рёбер туши выпадает он")]
        [SerializeField] private GameObject _ribsPrefab;
        [Tooltip("Какая часть — рёбра (номер в Parts). −1 — по имени части: rib / ребр")]
        [SerializeField] private int _ribsPart = -1;

        [Header("Кости")]
        [Tooltip("Набор префабов костей (длинная, короткая, рёбра, позвонок, мозговая) — FoodPiece «Кости для бульона». " +
                 "Делаются из набора кнопкой CatCooks → Мясо → «Префабы костей из выделенного набора»")]
        [SerializeField] private GameObject[] _bonePrefabs;
        [Tooltip("Сколько костей выпадает при отпиле КАЖДОГО куска (случайно от X до Y)")]
        [SerializeField] private Vector2Int _bonesPerCut = new Vector2Int(1, 2);
        [Tooltip("Сколько ещё выпадает, когда отпилен последний кусок")]
        [Range(0, 8)][SerializeField] private int _boneCount = 1;
        [Tooltip("Новая туша (Meat Part Tag): кости выпадают только из ног (рулька/голяшка) и в конце. Выкл — из каждого куска (Bones Per Cut)")]
        [SerializeField] private bool _bonesOnlyFromLegs = true;
        [Tooltip("Старое поле: одна кость (если набор пуст)")]
        [SerializeField] private GameObject _bonePrefab;

        [Header("Распил")]
        [Tooltip("Каким процессом режется туша. «Разделывать»: Туша → Разделано")]
        [SerializeField] private FoodProcess _process = FoodProcess.Butcher;
        [Tooltip("Секунд держать пилу на шве до отпадания куска")]
        [Min(0.1f)][SerializeField] private float _secondsPerCut = 1.6f;
        [Tooltip("Резать только по шву (край куска у соседнего)")]
        [SerializeField] private bool _requireSeam = true;
        [Tooltip("Ширина шва, м — насколько близко к соседнему куску надо вести пилу")]
        [SerializeField] private float _edgeWidth = 0.1f;
        [Tooltip("Зажимать тушу, пока пилят (тяжёлая — не уезжает)")]
        [SerializeField] private bool _clampWhileCutting = true;

        [Header("Отделение")]
        [SerializeField] private float _pieceImpulse = 2f;
        [SerializeField] private float _pieceSpread = 0.3f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ulong> _removed = new SyncVar<ulong>(0);
        private readonly SyncVar<sbyte> _focus = new SyncVar<sbyte>(-1);
        private readonly SyncVar<bool> _clamped = new SyncVar<bool>(false);

        private FoodPiece _food;
        private Rigidbody _rb;
        private bool _wasKinematic;
        private Collider[][] _partColliders;

        public int PartCount => _parts != null ? _parts.Length : 0;
        public int PartsLeft
        {
            get
            {
                int n = 0;
                for (int i = 0; i < PartCount; i++) if (!IsRemoved(i)) n++;
                return n;
            }
        }
        private bool IsRemoved(int i) => (_removed.Value & (1UL << i)) != 0;

        // ───────────────────────── ICuttable ─────────────────────────

        public bool CanCut => PartsLeft > 0 && _food != null && !_food.IsConsumed && RuleAllows();
        public float SecondsPerCut => _secondsPerCut;
        public bool ClampWhileCutting => _clampWhileCutting;
        public string CutLabel => $"кусков осталось {PartsLeft}";
        public Color CrumbColor => _food != null && _food.Def != null ? _food.Def.BaseColor : new Color(0.7f, 0.25f, 0.22f);

        public Vector3 CutPoint()
        {
            int f = _focus.Value;
            if (f >= 0 && f < PartCount && !IsRemoved(f)) return PartCenter(f);
            for (int i = 0; i < PartCount; i++) if (!IsRemoved(i)) return PartCenter(i);
            return transform.position;
        }

        /// <summary>Инструмент без выбора части (FoodTool, старый вызов): режем первую оставшуюся.</summary>
        public void ServerCutOne(ProcessDef cutProcess, float quality01)
        {
            for (int i = 0; i < PartCount; i++)
                if (!IsRemoved(i)) { ServerCutPart(i, PartCenter(i), cutProcess, quality01); return; }
        }

        [Server]
        public void ServerSetClamped(bool value)
        {
            if (_clamped.Value != value) _clamped.Value = value;
        }

        // ───────────────────────── IPartCuttable ─────────────────────────

        public int PartAt(Collider hit, Vector3 point)
        {
            if (hit != null)
                for (int i = 0; i < PartCount; i++)
                {
                    if (IsRemoved(i) || _parts[i] == null) continue;
                    if (hit.transform == _parts[i] || hit.transform.IsChildOf(_parts[i])) return i;
                }
            // попали в общий коллайдер — ближайшая оставшаяся часть
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < PartCount; i++)
            {
                if (IsRemoved(i)) continue;
                float d = DistanceToPart(i, point);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public bool CanCutPart(int part, Vector3 point, out string why)
        {
            why = null;
            if (part < 0 || part >= PartCount || IsRemoved(part)) { why = "этот кусок уже отпилен"; return false; }
            if (!CanCut) { why = "туша не режется (нет правила «Туша + Разделывать»)"; return false; }
            if (!_requireSeam || PartsLeft <= 1) return true;
            // Шов: точка касания близко к другому оставшемуся куску.
            for (int i = 0; i < PartCount; i++)
            {
                if (i == part || IsRemoved(i)) continue;
                if (DistanceToPart(i, point) <= _edgeWidth) return true;
            }
            why = "веди пилу по шву — по краю куска, где он примыкает к соседнему";
            return false;
        }

        [Server]
        public void ServerSetFocus(int part)
        {
            sbyte f = (sbyte)Mathf.Clamp(part, -1, 63);
            if (_focus.Value != f) _focus.Value = f;
        }

        [Server]
        public void ServerCutPart(int part, Vector3 point, ProcessDef cutProcess, float quality01)
        {
            if (part < 0 || part >= PartCount || IsRemoved(part) || !CanCut) return;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || !db.Table.TryGet(_food.ProductId, _food.State, _process, out FoodTransition t)) return;

            MeatPartTag tag = _parts[part] != null ? _parts[part].GetComponent<MeatPartTag>() : null;
            bool ribs = IsRibsPart(part);
            if (ribs && _ribsPrefab == null && (tag == null || tag.Species == MeatSpecies.Pork))
                Debug.LogWarning($"[Туша] «{name}»: рёбра «{_parts[part].name}» выпали ОБЫЧНЫМ куском — у Food Carcass пуст Ribs Prefab. " +
                                 "Меню CatCooks → Мясо → Новые туши → 6 (нужен префаб RibsRack из Барбекю → 2)", this);
            bool beefBrisket = tag != null && tag.Species == MeatSpecies.Beef && tag.Part == MeatPart.Brisket;
            if (beefBrisket && _brisketPrefab == null)
                Debug.LogWarning($"[Туша] «{name}»: брискет выпал ОБЫЧНЫМ куском — у Food Carcass пуст Brisket Prefab. Меню CatCooks → Брискет → 5", this);
            // Борщ: свиная грудинка — пластами сала, говяжья нога — шайбами голяшки (префабы в MeatCutSettings, Борщ → 9)
            MeatCutSettings mst = MeatCutSettings.Instance;
            bool lard = tag != null && mst != null && mst.LardPrefab != null && tag.Species == MeatSpecies.Pork && tag.Part == MeatPart.Brisket;
            bool shank = tag != null && mst != null && mst.ShankPrefab != null && tag.Species == MeatSpecies.Beef && CarcassModel.Kind(tag.Part) == 5;
            // Телятина по-орловски: говяжий толстый край — целым куском «Телячья корейка» (MeatCutSettings, Орлов → 5)
            bool veal = tag != null && mst != null && mst.VealLoinPrefab != null && tag.Species == MeatSpecies.Beef && CarcassModel.Kind(tag.Part) == 2
                        && (!mst.VealLoinLeftOnly || tag.Part == MeatPart.LoinL);
            bool ok;
            if (veal) ok = SpawnMany(part, mst.VealLoinPrefab, 1, "телячья корейка целым куском");
            else if (_ribsPrefab != null && ribs) ok = SpawnWhole(part, _ribsPrefab, "рёбра");
            else if (_brisketPrefab != null && beefBrisket) ok = SpawnWhole(part, _brisketPrefab, "брискет");
            else if (lard) ok = SpawnMany(part, mst.LardPrefab, Mathf.Max(1, mst.LardPerBrisket), "сало пластами");
            else if (shank) ok = SpawnMany(part, mst.ShankPrefab, Mathf.Max(1, mst.ShankPerLeg), "голяшка шайбами");
            else if (tag != null) ok = SpawnCut(part, tag, t.To, quality01);
            else ok = SpawnPiece(part, t.To, quality01);
            if (!ok)
            {
                // кусок не появился — часть НЕ убираем с туши (иначе мясо просто исчезает)
                if (_log) Debug.LogWarning($"[Туша] «{name}»: «{_parts[part].name}» не отпилен — кусок не создался (ошибка выше)", this);
                return;
            }
            _removed.Value |= 1UL << part;
            bool leg = tag != null && CarcassModel.Kind(tag.Part) == 5;
            int bones = tag != null && _bonesOnlyFromLegs
                ? 0
                : Random.Range(_bonesPerCut.x, _bonesPerCut.y + 1);
            if (leg) SpawnLegBone(part);                 // нога — СВОЯ кость (мозговая/трубчатая) вместе с кусками, а не случайная
            SpawnBones(bones, PartCenter(part));

            int left = PartsLeft;
            if (left > 0)
            {
                _food.ServerSetCount(left);
                if (_log) Debug.Log($"[Туша] «{name}»: отпилен «{_parts[part].name}», {CutLabel}", this);
                return;
            }

            // Последний кусок — ещё кости, туша исчезает.
            SpawnBones(_boneCount, transform.position);
            if (_log) Debug.Log($"[Туша] «{name}» разделана полностью, костей: {_boneCount}", this);
            _food.ServerTryConsume();
            base.NetworkObject.RemoveOwnership();
            base.Despawn(base.NetworkObject);
        }

        // ───────────────────────── ЖИЗНЬ ─────────────────────────

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _rb = GetComponent<Rigidbody>();
            CheckRibsPrefab();
            if (_parts == null || _parts.Length == 0) _parts = CollectParts().ToArray();
            if (_parts.Length > 64) System.Array.Resize(ref _parts, 64);
            _partColliders = new Collider[_parts.Length][];
            for (int i = 0; i < _parts.Length; i++)
                _partColliders[i] = _parts[i] != null ? _parts[i].GetComponentsInChildren<Collider>(true) : new Collider[0];
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (PartCount > 0) _food.ServerSetCount(PartsLeft);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _removed.OnChange += OnRemovedChanged;
            _clamped.OnChange += OnClampChanged;
            RefreshParts();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _removed.OnChange -= OnRemovedChanged;
            _clamped.OnChange -= OnClampChanged;
        }

        private void OnRemovedChanged(ulong prev, ulong next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            RefreshParts();
        }

        private void RefreshParts()
        {
            for (int i = 0; i < PartCount; i++)
                if (_parts[i] != null) _parts[i].gameObject.SetActive(!IsRemoved(i));
        }

        private void OnClampChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            if (_rb == null) return;
            if (next) { _wasKinematic = _rb.isKinematic; _rb.isKinematic = true; }
            else { _rb.isKinematic = _wasKinematic; _rb.WakeUp(); }
        }

        /// <summary>Ribs Prefab должен быть пластом рёбер (Ribs Rack без галки Brisket). Иначе — ошибка и не используем.</summary>
        private void CheckRibsPrefab()
        {
            if (_ribsPrefab == null) return;
            RibsRack rr = _ribsPrefab.GetComponent<RibsRack>();
            if (rr == null)
            {
                Debug.LogError($"[Туша] «{name}»: Ribs Prefab «{_ribsPrefab.name}» — без Ribs Rack. Рёбра выпадут обычным куском. Новые туши → 6", this);
                _ribsPrefab = null;
            }
            else if (rr.IsBrisket)
            {
                Debug.LogError($"[Туша] «{name}»: Ribs Prefab = «{_ribsPrefab.name}» — это БРИСКЕТ, а не рёбра. " +
                               "Рёбра выпадут обычным куском. Исправь: CatCooks → Мясо → Новые туши → 6 (v2.6.4 больше не путает)", this);
                _ribsPrefab = null;
            }
            else if (_log) Debug.Log($"[Туша] «{name}»: рёбра выпадут пластом «{_ribsPrefab.name}»", this);
        }

        // ───────────────────────── спавн ─────────────────────────

        [Server]
        private bool SpawnPiece(int part, FoodState pieceState, float quality01)
        {
            if (_piecePrefab == null) { Debug.LogError($"[Туша] «{name}»: не задан Piece Prefab (старая туша)", this); return false; }
            Transform tr = _parts[part];
            GameObject go = Instantiate(_piecePrefab, tr.position, tr.rotation);
            go.transform.localScale = tr.lossyScale;
            FoodPiece piece = go.GetComponent<FoodPiece>();
            if (piece == null) { Debug.LogError($"[Туша] на «{_piecePrefab.name}» нет FoodPiece", _piecePrefab); Destroy(go); return false; }
            byte form = (byte)Mathf.RoundToInt(_food.FormScore * Mathf.Clamp01(quality01 <= 0f ? 1f : quality01));
            piece.ServerPreset(_food.Def, pieceState, _food.Clean, _food.Cook, form);
            SlicePiece sp = go.GetComponent<SlicePiece>();
            if (sp != null) sp.ServerSetIndex((byte)part);
            base.Spawn(go);
            Push(go, tr.position);
            return true;
        }

        private GameObject CutPrefab
        {
            get
            {
                if (_cutPrefab != null) return _cutPrefab;
                MeatCutSettings st = MeatCutSettings.Instance;
                return st != null ? st.CutPrefab : null;
            }
        }

        /// <summary>Кусок новой туши — общим префабом Meat Cut: своя часть, свой продукт, на том же месте.</summary>
        [Server]
        private bool SpawnCut(int part, MeatPartTag tag, FoodState pieceState, float quality01)
        {
            Transform tr = _parts[part];
            GameObject prefab = CutPrefab;
            if (prefab == null)
            {
                Debug.LogError($"[Туша] «{name}»: нет префаба куска MeatCut — у Food Carcass пуст Cut Prefab и в Resources/MeatCutSettings тоже. " +
                               "Меню CatCooks → Мясо → Новые туши → 6 (префаб MeatCut — меню 4), туша-префаб → Overrides → Apply All", this);
                return false;
            }
            GameObject go = Instantiate(prefab, tr.position, tr.rotation);
            MeatCut cut = go.GetComponent<MeatCut>();
            if (cut == null) { Debug.LogError($"[Туша] на «{prefab.name}» нет Meat Cut", prefab); Destroy(go); return false; }
            cut.PresetCut(tag.Species, tag.Part, CarcassModel.NoSlab, CarcassModel.NoSlab, null, Mathf.Abs(tr.lossyScale.x));
            MeatCutSettings st = MeatCutSettings.Instance;
            ProductDef p = st != null ? st.Product(tag.Species, tag.Part) : null;
            if (p == null) Debug.LogWarning($"[Туша] нет продукта для «{CarcassModel.Nice(tag.Species, tag.Part)}» — Новые туши → 1; кусок будет «{_food.Def?.Title}»", this);
            byte form = (byte)Mathf.RoundToInt(_food.FormScore * Mathf.Clamp01(quality01 <= 0f ? 1f : quality01));
            cut.ServerPreset(p != null ? p : _food.Def, pieceState, _food.Clean, _food.Cook, form);
            base.Spawn(go);
            Push(go, tr.position);
            if (_log) Debug.Log($"[Туша] «{name}»: выпал кусок «{CarcassModel.Nice(tag.Species, tag.Part)}» — продукт «{(p != null ? p.Title : _food.Def?.Title)}»", this);
            return true;
        }

        /// <summary>Эта часть туши — рёбра (по номеру или по имени).</summary>
        public bool IsRibsPart(int part)
        {
            if (part < 0 || part >= PartCount) return false;
            MeatPartTag tag = _parts[part] != null ? _parts[part].GetComponent<MeatPartTag>() : null;
            if (tag != null) return CarcassModel.IsRibs(tag.Part);          // новая туша: RibsL / RibsR (не «RibLoin»)
            if (_ribsPart >= 0) return part == _ribsPart;
            string n = _parts[part] != null ? _parts[part].name.ToLowerInvariant() : "";
            return n.Contains("rib") || n.Contains("ребр") || n.Contains("рёбр");
        }

        /// <summary>Рёбра — своим красивым пластом (Ribs Rack), лёжа мясом вверх, там же, где были.</summary>
        /// <summary>Особый кусок (рёбра, брискет) — своим префабом, лёжа ровно, там же, где был.</summary>
        [Server]
        private bool SpawnWhole(int part, GameObject prefabIn, string what)
        {
            Transform tr = _parts[part];
            Vector3 at = PartCenter(part) + Vector3.up * 0.05f;
            Vector3 fwd = Vector3.ProjectOnPlane(tr.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            GameObject go = Instantiate(prefabIn, at, Quaternion.LookRotation(fwd.normalized, Vector3.up));
            FoodPiece piece = go.GetComponent<FoodPiece>();
            if (piece == null) { Debug.LogError($"[Туша] на «{prefabIn.name}» нет Ribs Rack / Food Piece", prefabIn); Destroy(go); return false; }
            base.Spawn(go);
            Push(go, at);
            if (_log) Debug.Log($"[Туша] «{name}»: {what} — отдельным куском «{prefabIn.name}»", this);
            return true;
        }

        /// <summary>Несколько одинаковых кусков своим префабом (сало пластами, голяшка шайбами) — кучкой там, где была часть.</summary>
        [Server]
        private bool SpawnMany(int part, GameObject prefab, int count, string what)
        {
            Vector3 c = PartCenter(part);
            Transform tr = _parts[part];
            Vector3 fwd = Vector3.ProjectOnPlane(tr.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd.normalized);
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 at = c + Vector3.up * (0.05f + i * 0.08f) + side * ((i - (count - 1) * 0.5f) * 0.15f);
                GameObject go = Instantiate(prefab, at, Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(0f, i * 37f, 0f));
                if (go.GetComponent<FoodPiece>() == null) { Debug.LogError($"[Туша] на «{prefab.name}» нет Food Piece", prefab); Destroy(go); return made > 0; }
                base.Spawn(go);
                Push(go, at);
                made++;
            }
            if (_log) Debug.Log($"[Туша] «{name}»: {what} — {made} шт. «{prefab.name}»", this);
            return made > 0;
        }

        /// <summary>Кость ноги из набора: по имени (мозговая, трубчатая, длинная, нога) или самая крупная.</summary>
        private GameObject LegBonePrefab()
        {
            if (_bonePrefabs == null || _bonePrefabs.Length == 0) return _bonePrefab;
            string[] keys = { "marrow", "мозг", "long", "длин", "femur", "leg", "ног", "tube", "труб", "shank", "голяш" };
            foreach (string k in keys)
                foreach (GameObject g in _bonePrefabs)
                    if (g != null && g.name.ToLowerInvariant().Contains(k)) return g;
            GameObject best = null; float bs = -1f;
            foreach (GameObject g in _bonePrefabs)
            {
                if (g == null) continue;
                MeshFilter mf = g.GetComponentInChildren<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                float s = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale).magnitude;
                if (s > bs) { bs = s; best = g; }
            }
            return best != null ? best : _bonePrefabs[0];
        }

        /// <summary>Кость ноги — рядом с кусками (шайбами голяшки), там же, где была нога.</summary>
        [Server]
        private void SpawnLegBone(int part)
        {
            GameObject prefab = LegBonePrefab();
            if (prefab == null) { if (_log) Debug.LogWarning($"[Туша] «{name}»: набора костей нет — кость ноги не выпала", this); return; }
            Transform tr = _parts[part];
            Vector3 fwd = Vector3.ProjectOnPlane(tr.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            Vector3 at = PartCenter(part) + Vector3.up * 0.1f - Vector3.Cross(Vector3.up, fwd.normalized) * 0.25f;
            GameObject go = Instantiate(prefab, at, Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, 90f));
            base.Spawn(go);
            Push(go, at);
            if (_log) Debug.Log($"[Туша] «{name}»: кость ноги «{prefab.name}» — вместе с кусками", this);
        }

        [Server]
        private void SpawnBones(int count, Vector3 c)
        {
            if (count <= 0) return;
            int kinds = _bonePrefabs != null ? _bonePrefabs.Length : 0;
            if (kinds == 0 && _bonePrefab == null)
            {
                Debug.LogWarning($"[Туша] «{name}»: не задан набор костей — кости не выпали", this);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                GameObject prefab = kinds > 0 ? _bonePrefabs[Random.Range(0, kinds)] : _bonePrefab;
                if (prefab == null) continue;
                Vector3 at = c + Random.insideUnitSphere * 0.12f + Vector3.up * 0.08f;
                GameObject go = Instantiate(prefab, at, Random.rotation);
                base.Spawn(go);
                Push(go, at);
                if (_log) Debug.Log($"[Туша] выпала кость «{prefab.name}»", this);
            }
        }

        private void Push(GameObject go, Vector3 from)
        {
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null) return;
            Vector3 away = from - transform.position;
            if (away.sqrMagnitude < 1e-4f) away = transform.up;
            Vector3 dir = (away.normalized + Random.insideUnitSphere * _pieceSpread + Vector3.up * 0.35f).normalized;
            rb.AddForce(dir * _pieceImpulse * Mathf.Max(1f, rb.mass * 0.2f), ForceMode.Impulse);
        }

        // ───────────────────────── геометрия ─────────────────────────

        private Vector3 PartCenter(int i)
        {
            Transform t = _parts[i];
            if (t == null) return transform.position;
            MeshFilter mf = t.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? t.TransformPoint(mf.sharedMesh.bounds.center) : t.position;
        }

        private float DistanceToPart(int i, Vector3 p)
        {
            float best = float.MaxValue;
            Collider[] cols = _partColliders != null && i < _partColliders.Length ? _partColliders[i] : null;
            if (cols != null)
                foreach (Collider c in cols)
                {
                    if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                    best = Mathf.Min(best, Vector3.Distance(c.ClosestPoint(p), p));
                }
            if (best < float.MaxValue) return best;
            Renderer r = _parts[i] != null ? _parts[i].GetComponent<Renderer>() : null;
            return r != null ? Mathf.Sqrt(r.bounds.SqrDistance(p)) : Vector3.Distance(PartCenter(i), p);
        }

        private bool RuleAllows()
        {
            FoodDatabase db = FoodDatabase.Instance;
            return db != null && db.CanApply(_food.ProductId, _food.State, _process);
        }

        private List<Transform> CollectParts()
        {
            var found = new List<Transform>();
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
                if (r.transform != transform) found.Add(r.transform);
            return found;
        }

#if UNITY_EDITOR
        /// <summary>Туша в сцене — экземпляр префаба: без этого Unity откатит поля к префабу в Play.</summary>
        private void EditorRecord()
        {
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        public void EditorSetup(Transform[] parts, GameObject piecePrefab, GameObject bonePrefab, GameObject[] bones)
        {
            UnityEditor.Undo.RecordObject(this, "Туша");
            _parts = parts;
            if (piecePrefab != null) _piecePrefab = piecePrefab;
            if (bonePrefab != null) _bonePrefab = bonePrefab;
            if (bones != null && bones.Length > 0) _bonePrefabs = bones;
            EditorRecord();
        }

        public Transform[] EditorCollectParts() => CollectParts().ToArray();

        public void EditorSetBrisket(GameObject prefab)
        {
            UnityEditor.Undo.RecordObject(this, "Туша");
            _brisketPrefab = prefab;
            EditorRecord();
        }

        public void EditorSetCut(GameObject prefab)
        {
            UnityEditor.Undo.RecordObject(this, "Туша");
            _cutPrefab = prefab;
            EditorRecord();
        }

        public void EditorSetRibs(GameObject prefab, int part)
        {
            UnityEditor.Undo.RecordObject(this, "Туша");
            _ribsPrefab = prefab; _ribsPart = part;
            EditorRecord();
        }

        /// <summary>Номер части-рёбер по имени (−1 — нет такой).</summary>
        public int EditorFindRibsPart()
        {
            Transform[] parts = _parts != null && _parts.Length > 0 ? _parts : CollectParts().ToArray();
            for (int i = 0; i < parts.Length; i++)
            {
                MeatPartTag tag = parts[i] != null ? parts[i].GetComponent<MeatPartTag>() : null;
                if (tag != null) { if (CarcassModel.IsRibs(tag.Part)) return i; continue; }
                string n = parts[i] != null ? parts[i].name.ToLowerInvariant() : "";
                if (n.Contains("rib") || n.Contains("ребр") || n.Contains("рёбр")) return i;
            }
            return -1;
        }

        public Transform EditorPart(int i)
        {
            Transform[] parts = _parts != null && _parts.Length > 0 ? _parts : CollectParts().ToArray();
            return i >= 0 && i < parts.Length ? parts[i] : null;
        }
#endif
    }
}
