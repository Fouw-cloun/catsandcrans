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

        [Header("v25.5: распил ПО ШВУ (как какао-боб)")]
        [Tooltip("Вкл — кусок отпадает, когда шов вокруг него пропилен на Seam Need: веди пилой ВДОЛЬ шва, за ней остаётся пропил. " +
                 "Бензопила — быстро ведёшь, ножовка — по штриху (вперёд-назад) в точке прицела, экскаватор (траншеекопатель) — грубо и широко. " +
                 "Выкл — как раньше: держи пилу на шве Seconds Per Cut")]
        [SerializeField] private bool _traceSeam = true;
        [Tooltip("Какую долю шва вокруг куска надо пропилить (0.6 — 60%)")]
        [Range(0.2f, 1f)][SerializeField] private float _seamNeed = 0.5f;
        [Tooltip("v28.4 (новые туши): какую долю шва пропилить, чтобы кусок отпал. Раньше хватало одной полоски")]
        [Range(0.2f, 1f)][SerializeField] private float _modelNeed = 0.9f;
        [Tooltip("v28.4 (новые туши): сколько линии пропиливает одно касание, × ширины шва")]
        [Range(0.2f, 4f)][SerializeField] private float _modelMark = 0.8f;
        [Tooltip("v28.6: пила «прилипает» к ближайшей линии разделки в этом радиусе (× ширины шва). Больше — легче попасть")]
        [Range(0.5f, 6f)][SerializeField] private float _snapRadius = 2.4f;
        [Tooltip("v28.6: коллайдеры кусков — по форме мяса (выпуклые), а не коробки. Пила попадает в поверхность, а не в воздух рядом")]
        [SerializeField] private bool _convexParts = true;
        private float Need => _model ? _modelNeed : _seamNeed;
        private float MarkR => _model ? _modelMark : _markRadius;
        [Tooltip("Ширина шва — доля размера туши (туша 7 м × 0.03 = 21 см). Не меньше Edge Width")]
        [Range(0.005f, 0.1f)][SerializeField] private float _seamBandFrac = 0.03f;
        [Tooltip("Радиус, который пропиливает пила за одно касание, × ширины шва")]
        [Range(0.3f, 4f)][SerializeField] private float _markRadius = 1.3f;
        [SerializeField] private Color _kerfFresh = new Color(0.86f, 0.42f, 0.42f);
        [SerializeField] private Color _kerfDark = new Color(0.25f, 0.03f, 0.04f);

        [Header("v28: разметка и пропил (новые туши)")]
        [Tooltip("Новая туша (части с Meat Part Tag): туша цельная, без видимых швов. Где пилить — пунктир «чернилами» по линиям разделки; " +
                 "пропиленное — ровная бороздка точно по линии. Выкл — пунктира нет (пропил всё равно ровный)")]
        [SerializeField] private bool _showGuide = true;
        [SerializeField] private Color _guideColor = new Color(0.34f, 0.15f, 0.42f);
        [Tooltip("Толщина бороздки пропила, × ширины шва")]
        [Range(0.05f, 0.6f)][SerializeField] private float _kerfWidth = 0.16f;

        [Header("v25.6: пила ЗАСТРЕВАЕТ")]
        [Tooltip("Бензопила: шанс застрять за секунду пиления")]
        [Range(0f, 0.5f)][SerializeField] private float _jamChainsaw = 0.05f;
        [Tooltip("Ножовка: шанс застрять за один штрих")]
        [Range(0f, 0.5f)][SerializeField] private float _jamHandsaw = 0.07f;
        [Tooltip("Траншеекопатель: шанс застрять за секунду")]
        [Range(0f, 0.5f)][SerializeField] private float _jamTrencher = 0.08f;
        [Tooltip("Такт рывков, с: все жмут Pull Key в такт")]
        [Range(0.6f, 2f)][SerializeField] private float _tugBeat = 1.1f;
        [Tooltip("Сколько рывков в такт нужно ОДНОМУ. Вдвоём сила рывка ×2.8, втроём ×5.2 — команда справляется в 2–3 такта")]
        [Range(1f, 20f)][SerializeField] private float _tugSoloPulls = 6f;
        [Tooltip("Траншеекопатель застрял: сколько касаний пилой (ножовка, бензопила) у цепи, чтобы его освободить")]
        [Range(1, 10)][SerializeField] private int _trencherFreeMarks = 3;
        [SerializeField] private KeyCode _pullKey = KeyCode.Q;

        [Header("Отделение")]
        [SerializeField] private float _pieceImpulse = 2f;
        [SerializeField] private float _pieceSpread = 0.3f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ulong> _removed = new SyncVar<ulong>(0);
        private readonly SyncVar<sbyte> _focus = new SyncVar<sbyte>(-1);
        private readonly SyncVar<bool> _clamped = new SyncVar<bool>(false);
        private readonly SyncList<Vector4> _kerf = new SyncList<Vector4>();   // v25.5: путь пилы (в осях туши), w=1 — начало штриха
        private readonly SyncVar<byte> _seamPct = new SyncVar<byte>(0);
        // v25.6: застревание
        public enum JamKind : byte { None = 0, Handsaw = 1, Chainsaw = 2, Trencher = 3 }
        private readonly SyncVar<byte> _jamKind = new SyncVar<byte>(0);
        private readonly SyncVar<NetworkObject> _jamTool = new SyncVar<NetworkObject>();
        private readonly SyncVar<Vector3> _jamLocal = new SyncVar<Vector3>();
        private readonly SyncVar<byte> _tug = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _freeNet = new SyncVar<byte>(0);
        private readonly SyncList<NetworkObject> _pullers = new SyncList<NetworkObject>();

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
        public string CutLabel => _traceSeam
            ? $"кусков осталось {PartsLeft} · шов {_seamPct.Value}% из {Need * 100f:0}% — веди пилу вдоль шва"
            : $"кусков осталось {PartsLeft}";
        public bool TraceSeam => _traceSeam;
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
                if (!IsRemoved(i)) { ServerCutPartNow(i, quality01); return; }   // старые инструменты без точки — сразу
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
            if (Jammed)
            {
                if (_jamKind.Value == (byte)JamKind.Trencher && NearJam(point))
                {
                    if (_traceSeam && base.IsClientInitialized) RecordLocal(point);
                    return true;
                }
                why = _jamKind.Value == (byte)JamKind.Trencher
                    ? "в туше застрял траншеекопатель — отпили его пилой у цепи"
                    : $"в туше застряла пила — тяните вместе: {_pullKey} в такт";
                return false;
            }
            if (!_requireSeam || PartsLeft <= 1) return true;
            if (_model)
            {
                EnsureSeams();
                if (NearModelSeam(point, _band * _snapRadius))
                {
                    if (_traceSeam && base.IsClientInitialized) RecordLocal(point);
                    return true;
                }
                why = "веди пилу по линии разделки (пунктир)";
                return false;
            }
            // Шов: точка касания близко к другому оставшемуся куску.
            float edge = _traceSeam ? Mathf.Max(_edgeWidth, Band()) : _edgeWidth;
            for (int i = 0; i < PartCount; i++)
            {
                if (i == part || IsRemoved(i)) continue;
                if (DistanceToPart(i, point) <= edge)
                {
                    // v25.5: пила спрашивает это у владельца каждый кадр — пишем путь пилы и пачками шлём серверу
                    if (_traceSeam && base.IsClientInitialized) RecordLocal(point);
                    return true;
                }
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
            if (_traceSeam)
            {
                EnsureSeams();
                if (SeamProgress(part) >= 0f) { ServerSawAt(point, 1f, false); return; }   // есть шов — пилить вдоль него
                // шва нет (последний кусок, часть без коробки) — режется сразу, как раньше
            }
            ServerCutPartNow(part, quality01);
        }

        [Server]
        private void ServerCutPartNow(int part, float quality01)
        {
            if (part < 0 || part >= PartCount || IsRemoved(part) || !CanCut) return;
            _seamPct.Value = 0;
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

        // ───────────────────────── v25.5: ШОВ И ПРОПИЛ ─────────────────────────
        //
        // Шов куска = точки на его коробках-коллайдерах, которые ближе ширины шва к коробке соседнего куска.
        // Пила (любая) отмечает точки шва рядом с собой; пропилено Seam Need шва вокруг куска — кусок отпадает.
        // Швы к уже отпиленным соседям не считаются (там уже пусто). Путь пилы — SyncList, пропил рисует каждый у себя.

        private struct PartBox { public Transform t; public Vector3 c, h; public int part; }
        private List<PartBox> _boxes;
        private List<Vector3>[] _seamPts;   // в осях туши
        private List<int>[] _seamNb;
        private List<bool>[] _seamCut;      // только сервер
        private float _band = -1f;

        private float Band() { EnsureSeams(); return _band; }

        private void EnsureSeams()
        {
            if (_model) { EnsureModelSeams(); return; }
            if (_seamPts != null) return;
            int n = PartCount;
            _boxes = new List<PartBox>();
            Bounds all = default; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (_parts[i] == null) continue;
                foreach (BoxCollider bc in _parts[i].GetComponentsInChildren<BoxCollider>(true))
                    _boxes.Add(new PartBox { t = bc.transform, c = bc.center, h = bc.size * 0.5f, part = i });
                foreach (Renderer r in _parts[i].GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { all = r.bounds; any = true; } else all.Encapsulate(r.bounds);
                }
            }
            float size = any ? Mathf.Max(all.size.x, Mathf.Max(all.size.y, all.size.z)) : 1f;
            _band = Mathf.Max(_edgeWidth, size * _seamBandFrac);
            _size = size;
            _seamPts = new List<Vector3>[n]; _seamNb = new List<int>[n]; _seamCut = new List<bool>[n];
            for (int i = 0; i < n; i++) { _seamPts[i] = new List<Vector3>(); _seamNb[i] = new List<int>(); _seamCut[i] = new List<bool>(); }
            const int G = 9;
            Physics.SyncTransforms();
            var probeHits = new Collider[16];
            int hidden = 0;
            foreach (PartBox b in _boxes)
                for (int f = 0; f < 6; f++)
                {
                    int ax = f / 2, u = (ax + 1) % 3, v = (ax + 2) % 3;
                    float sign = (f & 1) == 0 ? 1f : -1f;
                    for (int a = 0; a < G; a++)
                        for (int c = 0; c < G; c++)
                        {
                            Vector3 l = b.c;
                            l[ax] += sign * b.h[ax];
                            l[u] += ((a + 0.5f) / G * 2f - 1f) * b.h[u];
                            l[v] += ((c + 0.5f) / G * 2f - 1f) * b.h[v];
                            Vector3 w = b.t.TransformPoint(l);
                            float best = float.MaxValue; int nb = -1;
                            foreach (PartBox o in _boxes)
                            {
                                if (o.part == b.part) continue;
                                float d = BoxDist(o, w);
                                if (d < best) { best = d; nb = o.part; }
                            }
                            if (best > _band) continue;
                            // v25.7: только ДОСТУПНЫЕ пиле точки — грань снаружи, а не внутри стыка с соседом
                            // (раньше считались и внутренние грани — до них не добраться, шов никогда не доходил до 60%)
                            Vector3 nrm = b.t.TransformDirection(ax == 0 ? Vector3.right * sign : ax == 1 ? Vector3.up * sign : Vector3.forward * sign).normalized;
                            Vector3 probe = w + nrm * _band * 0.5f;
                            bool covered = false;
                            foreach (PartBox o in _boxes)
                            {
                                if (o.t == b.t && o.c == b.c) continue;
                                if (BoxDist(o, probe) < _band * 0.2f) { covered = true; break; }
                            }
                            if (!covered)
                            {
                                // и не упёрта в пол / стену (снизу туши пилой не подлезть)
                                int hc = Physics.OverlapSphereNonAlloc(probe, _band * 0.3f, probeHits, ~0, QueryTriggerInteraction.Ignore);
                                for (int q = 0; q < hc && !covered; q++)
                                    if (!probeHits[q].transform.IsChildOf(transform)) covered = true;
                            }
                            if (covered) { hidden++; continue; }
                            _seamPts[b.part].Add(transform.InverseTransformPoint(w));
                            _seamNb[b.part].Add(nb);
                            _seamCut[b.part].Add(false);
                        }
                }
            if (_log)
            {
                int tot = 0; for (int i = 0; i < n; i++) tot += _seamPts[i].Count;
                Debug.Log($"[Туша] «{name}»: шов {tot} точек снаружи (внутренних/у пола отброшено {hidden}), ширина шва {_band:0.00} м (коробок {_boxes.Count})", this);
            }
        }


        // ───────────────────────── v28: ШВЫ ПО МОДЕЛИ ТУШИ ─────────────────────────
        //
        // Новая туша (у частей Meat Part Tag): линии разделки берутся из CarcassModel.Seams — точно там, где
        // плоскость стыка двух оставшихся частей выходит на поверхность (шкуру или срез отпиленного соседа).
        // Пила пропиливает точки линии рядом с собой; у каждого клиента — по пути пилы (_kerf), у сервера — сразу.
        // Отпилили кусок — линии пересчитываются (открылись новые стыки), пропиленное сохраняется.

        private bool _model;
        private MeatSpecies _sp;
        private int[] _idxOfMeat;            // MeatPart → номер в Parts
        private MeatPart[] _meatOf;          // номер в Parts → MeatPart
        private List<CarcassModel.SeamPt> _ms;
        private bool[] _msCut, _msHidden;
        private int[] _msA, _msB;
        private ulong _msRemoved = ulong.MaxValue;
        private int _msVersion, _kerfMarked, _msStruct;
        private readonly List<int> _kerfNear = new List<int>();   // ближайшая точка линии к каждой точке пути пилы (кэш)
        private int _kerfNearStruct = -1;
        private float _msStep = 0.01f;

        private void DetectModel()
        {
            _model = false;
            if (PartCount == 0) return;
            _idxOfMeat = new int[CarcassModel.PartCount];
            for (int i = 0; i < _idxOfMeat.Length; i++) _idxOfMeat[i] = -1;
            _meatOf = new MeatPart[PartCount];
            for (int i = 0; i < PartCount; i++)
            {
                MeatPartTag tag = _parts[i] != null ? _parts[i].GetComponent<MeatPartTag>() : null;
                if (tag == null) return;                                       // старая туша — старые швы по коробкам
                if (i == 0) _sp = tag.Species; else if (tag.Species != _sp) return;
                _meatOf[i] = tag.Part;
                if ((int)tag.Part < _idxOfMeat.Length) _idxOfMeat[(int)tag.Part] = i;
            }
            _model = true;
            _msStep = CarcassModel.SeamStep(_sp);
            if (_convexParts) MakeConvexParts();
            // v28: части стоят не там, где их ждёт модель — значит меши туши старые (с волнистыми швами)
            for (int i = 0; i < PartCount; i++)
                if (_parts[i] != null && (_parts[i].localPosition - CarcassModel.BoxCenter(_sp, _meatOf[i])).sqrMagnitude > 1e-5f)
                {
                    Debug.LogWarning($"[Туша] «{name}»: СТАРЫЕ меши частей («{_parts[i].name}» не на месте) — швы будут видны, разметка не совпадёт. " +
                                     "Меню CatCooks → Мясо → Новые туши → 8. Обновить ВСЕ туши", this);
                    break;
                }
        }

        /// <summary>v28.6: коробки кусков → выпуклые меш-коллайдеры по форме мяса (у всех одинаково).</summary>
        private void MakeConvexParts()
        {
            int n = 0;
            for (int i = 0; i < PartCount; i++)
            {
                Transform t = _parts[i];
                if (t == null) continue;
                BoxCollider box = t.GetComponent<BoxCollider>();
                MeshFilter mf = t.GetComponent<MeshFilter>();
                if (box == null || mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                MeshCollider mc = t.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = true;
                mc.isTrigger = box.isTrigger;
                mc.sharedMaterial = box.sharedMaterial;
                mc.enabled = box.enabled;
                Destroy(box);
                n++;
            }
            if (_log && n > 0) Debug.Log($"[Туша] «{name}»: {n} кусков — коллайдеры по форме мяса", this);
        }

        /// <summary>v28.6: ближайшая точка линии разделки (лучше ещё не пропиленная) в радиусе — туда и пилим.</summary>
        private bool SnapModel(Vector3 world, float radius, out Vector3 snapped)
        {
            snapped = world;
            if (_ms == null) return false;
            Vector3 l = transform.InverseTransformPoint(world);
            float r = radius / Scale, r2 = r * r;
            int bu = -1, ba = -1; float du = r2, da = r2;
            for (int k = 0; k < _ms.Count; k++)
            {
                if (_msHidden[k]) continue;
                float d2 = (_ms[k].P - l).sqrMagnitude;
                if (d2 < da) { da = d2; ba = k; }
                if (!_msCut[k] && d2 < du) { du = d2; bu = k; }
            }
            int b = bu >= 0 ? bu : ba;
            if (b < 0) return false;
            snapped = transform.TransformPoint(_ms[b].P);
            return true;
        }

        private float _hiddenAt;

        /// <summary>Недоступные пиле точки: прижаты к столу / полу / стене (неподвижное), смотрят не вверх. Пересчёт раз в 1.5 с.</summary>
        private void RefreshHidden(bool force)
        {
            if (_ms == null || (!force && Time.time < _hiddenAt)) return;
            _hiddenAt = Time.time + 1.5f;
            var hits = new Collider[16];
            bool changed = false;
            for (int k = 0; k < _ms.Count; k++)
            {
                Vector3 n = transform.TransformDirection(_ms[k].N).normalized;
                bool h = false;
                if (n.y < 0.35f)
                {
                    Vector3 w = transform.TransformPoint(_ms[k].P);
                    int hc = Physics.OverlapSphereNonAlloc(w + n * _band * 0.5f, _band * 0.3f, hits, ~0, QueryTriggerInteraction.Ignore);
                    for (int q = 0; q < hc && !h; q++)
                    {
                        Collider c = hits[q];
                        if (c.transform.IsChildOf(transform)) continue;
                        Rigidbody rb = c.attachedRigidbody;
                        if (rb != null && !rb.isKinematic) continue;                  // предметы, инструменты — не мешают
                        if (c is CharacterController) continue;                       // игроки — не мешают
                        h = true;
                    }
                }
                if (h != _msHidden[k]) { _msHidden[k] = h; changed = true; }
            }
            if (changed) { _msVersion++; _msStruct++; }
        }

        private ulong RemovedMeat()
        {
            ulong m = 0;
            for (int i = 0; i < PartCount; i++) if (IsRemoved(i)) m |= 1UL << (int)_meatOf[i];
            // части вида, которых нет в туше (удалили руками) — как отпиленные
            for (int p = 0; p < _idxOfMeat.Length; p++) if (_idxOfMeat[p] < 0) m |= 1UL << p;
            return m;
        }

        private float Scale => Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));

        private long MsKey(Vector3 p)
        {
            float q = _msStep * 0.5f;
            long x = Mathf.RoundToInt(p.x / q), y = Mathf.RoundToInt(p.y / q), z = Mathf.RoundToInt(p.z / q);
            return ((x & 0x1FFFFF) << 42) | ((y & 0x1FFFFF) << 21) | (z & 0x1FFFFF);
        }

        private void EnsureModelSeams()
        {
            if (_band < 0f)
            {
                Bounds bb = default; bool any = false;
                for (int i = 0; i < PartCount; i++)
                    if (_parts[i] != null)
                        foreach (Renderer r in _parts[i].GetComponentsInChildren<Renderer>(true))
                        { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
                _size = any ? Mathf.Max(bb.size.x, Mathf.Max(bb.size.y, bb.size.z)) : 1f;
                _band = Mathf.Max(_edgeWidth, _size * _seamBandFrac);
            }
            ulong rem = RemovedMeat();
            if (_ms != null && rem == _msRemoved) { RefreshHidden(false); return; }
            var keep = new HashSet<long>();
            if (_ms != null) for (int k = 0; k < _ms.Count; k++) if (_msCut[k]) keep.Add(MsKey(_ms[k].P));
            _msRemoved = rem;
            List<CarcassModel.SeamPt> all = CarcassModel.Seams(_sp, rem);
            _ms = new List<CarcassModel.SeamPt>(all.Count);
            var A = new List<int>(); var B = new List<int>(); var cut = new List<bool>(); var hid = new List<bool>();
            foreach (CarcassModel.SeamPt sp in all)
            {
                int a = sp.A < _idxOfMeat.Length ? _idxOfMeat[sp.A] : -1, b = sp.B < _idxOfMeat.Length ? _idxOfMeat[sp.B] : -1;
                if (a < 0 || b < 0) continue;
                _ms.Add(sp); A.Add(a); B.Add(b); hid.Add(false); cut.Add(keep.Contains(MsKey(sp.P)));
            }
            _msA = A.ToArray(); _msB = B.ToArray(); _msCut = cut.ToArray(); _msHidden = hid.ToArray();
            _msVersion++; _msStruct++;
            Physics.SyncTransforms();
            RefreshHidden(true);
            if (_log) { int hidden = 0; foreach (bool h in _msHidden) if (h) hidden++; Debug.Log($"[Туша] «{name}»: линии разделки {_ms.Count} точек (прижато к столу/полу: {hidden}), ширина шва {_band:0.00} м", this); }
        }

        /// <summary>Пометить пропиленными точки линий в радиусе r (мир). Возвращает кусок, у которого шов пропилен больше всех (−1 — не задели).</summary>
        private int MarkModel(Vector3 world, float r, out float bestP)
        {
            bestP = -1f;
            Vector3 l = transform.InverseTransformPoint(world);
            float rl = r / Scale, r2 = rl * rl;
            ulong touched = 0;
            for (int k = 0; k < _ms.Count; k++)
            {
                if (_msCut[k] || _msHidden[k]) continue;
                if ((_ms[k].P - l).sqrMagnitude > r2) continue;
                _msCut[k] = true;
                touched |= (1UL << _msA[k]) | (1UL << _msB[k]);
            }
            if (touched == 0) return -1;
            _msVersion++;
            int best = -1;
            for (int i = 0; i < PartCount; i++)
            {
                if ((touched & (1UL << i)) == 0 || IsRemoved(i)) continue;
                float p = SeamProgress(i);
                if (p > bestP) { bestP = p; best = i; }
            }
            return best;
        }

        /// <summary>Рядом с какой-нибудь линией разделки (мир)?</summary>
        private bool NearModelSeam(Vector3 world, float r)
        {
            Vector3 l = transform.InverseTransformPoint(world);
            float rl = r / Scale, r2 = rl * rl;
            for (int k = 0; k < _ms.Count; k++)
                if (!_msHidden[k] && (_ms[k].P - l).sqrMagnitude <= r2) return true;
            return false;
        }

        [Tooltip("Насколько пропил плавно гуляет поперёк линии, × ширины шва (0 — ровно по линии)")]
        [Range(0f, 0.5f)][SerializeField] private float _kerfJitter = 0.1f;

        [Header("v28.4: вид пропила и эффекты")]
        [Tooltip("Цвет пропила: свежее мясо по краям")]
        [SerializeField] private Color _cutLip = new Color(0.78f, 0.33f, 0.31f);
        [Tooltip("Цвет пропила: глубина бороздки")]
        [SerializeField] private Color _cutDeep = new Color(0.42f, 0.09f, 0.09f);
        [Tooltip("Цвет крошки кости / жира в пропиле")]
        [SerializeField] private Color _cutSpeck = new Color(0.96f, 0.9f, 0.8f);
        [Tooltip("Брызги и крошка из-под пилы")]
        [SerializeField] private bool _sawFx = true;
        [Range(0, 30)][SerializeField] private int _sawFxPerPoint = 9;
        [Range(0.2f, 3f)][SerializeField] private float _sawFxSize = 1f;

        // меш пропила: позиции, нормали, цвета (освещается тем же материалом, что и туша — не «наклейка»)
        private readonly List<Vector3> _kv = new List<Vector3>();
        private readonly List<Vector3> _kn = new List<Vector3>();
        private readonly List<Color> _kc = new List<Color>();
        private readonly List<int> _kt = new List<int>();

        /// <summary>Разметка — тонкий пунктир по линиям (цвет не меняется). Пропил — своя линия по пути пилы:
        /// рваная кромка свежего мяса, тёмная бороздка внутри, редкие белые крошки кости/жира; ширина и цвет гуляют.</summary>
        private void BuildModelKerf()
        {
            if (_kerfGo == null) MakeKerfObject();
            _kv.Clear(); _kn.Clear(); _kc.Clear(); _kt.Clear();
            float bandL = _band / Scale;
            float lift = Mathf.Max(bandL * 0.04f, CarcassModel.Voxel(_sp) * 0.35f);
            float wf = bandL * _kerfWidth * 0.5f, wg = bandL * 0.02f;
            float L = _msStep * 0.62f;
            Color ink = new Color(_guideColor.r, _guideColor.g, _guideColor.b, 0.2f);
            if (_showGuide)
                for (int k = 0; k < _ms.Count; k++)
                {
                    if (_msHidden[k]) continue;
                    CarcassModel.SeamPt sp = _ms[k];
                    Quad(sp.P + sp.N * lift * 0.6f - sp.T * L * 0.4f, sp.P + sp.N * lift * 0.6f + sp.T * L * 0.4f, sp.N, wg, wg, ink, ink);
                }

            // v28.6: пропил рисуется по ПРОПИЛЕННЫМ точкам линии — ровно вдоль разметки, но живой:
            // плавно гуляет поперёк (шум по месту, не по кадру), ширина и цвет неровные, белые крошки
            float Lc = _msStep * 0.8f;
            for (int k = 0; k < _ms.Count; k++)
            {
                if (_msHidden[k] || !_msCut[k]) continue;
                CarcassModel.SeamPt sp = _ms[k];
                Vector3 side = Vector3.Cross(sp.N, sp.T).normalized;
                float f = 9f / Mathf.Max(1e-3f, bandL);
                float wob = Mathf.PerlinNoise(sp.P.x * f + sp.P.z * f * 0.7f + 3.1f, sp.P.y * f + 7.7f) * 2f - 1f;
                Vector3 p = sp.P + side * wob * _kerfJitter * bandL * 1.5f + sp.N * lift;
                float h2 = Mathf.PerlinNoise(sp.P.x * f * 0.6f + 11f, sp.P.z * f * 0.6f + sp.P.y * f * 0.6f);
                float w = wf * Mathf.Lerp(0.75f, 1.25f, h2);
                Color lip = Color.Lerp(_cutLip, _cutLip * 0.82f, h2); lip.a = 0.55f;
                Color deep = Color.Lerp(_cutDeep, _cutDeep * 1.25f, 1f - h2); deep.a = 0.15f;
                Vector3 a0 = p - sp.T * Lc, b0 = p + sp.T * Lc;
                Quad(a0, b0, sp.N, w * 1.25f, w * 1.25f, Color.Lerp(lip, _cutLip * 1.08f, 0.4f), Color.Lerp(lip, _cutLip * 1.08f, 0.4f));
                Quad(a0 + sp.N * lift * 0.3f, b0 + sp.N * lift * 0.3f, sp.N, w * 0.8f, w * 0.8f, lip, lip);
                Quad(a0 + sp.N * lift * 0.6f, b0 + sp.N * lift * 0.6f, sp.N, w * 0.32f, w * 0.32f, deep, deep);
                if (Hash01(k * 53 + 1) < 0.2f)
                {
                    Vector3 c = p + side * (Hash01(k * 67 + 2) - 0.5f) * w * 1.6f + sp.N * lift * 0.9f;
                    float r = w * Mathf.Lerp(0.15f, 0.3f, Hash01(k * 71 + 4));
                    Color sc = _cutSpeck; sc.a = 0.3f;
                    Quad(c - sp.T * r, c + sp.T * r, sp.N, r, r * 0.7f, sc, sc);
                }
            }
            if (_kerfNearStruct != _msStruct || _kerfNear.Count > _kerf.Count) { _kerfNear.Clear(); _kerfNearStruct = _msStruct; }
            float reach = _band * MarkR * 1.6f / Scale, reach2 = reach * reach;
            for (int i = _kerfNear.Count; i < _kerf.Count; i++)                  // для брызг: нормаль у точки пути
            {
                Vector4 kk = _kerf[i];
                Vector3 l = new Vector3(kk.x, kk.y, kk.z);
                int bk = -1; float bd = reach2;
                for (int k = 0; k < _ms.Count; k++)
                {
                    float d2 = (_ms[k].P - l).sqrMagnitude;
                    if (d2 < bd) { bd = d2; bk = k; }
                }
                _kerfNear.Add(bk);
            }
            _kerfMesh.Clear();
            _kerfMesh.indexFormat = _kv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _kerfMesh.SetVertices(_kv);
            _kerfMesh.SetNormals(_kn);
            _kerfMesh.SetColors(_kc);
            _kerfMesh.subMeshCount = 1;
            _kerfMesh.SetTriangles(_kt, 0);
            _kerfMesh.RecalculateBounds();
        }

        /// <summary>Лента a→b шириной wa→wb лицом по нормали n.</summary>
        private void Quad(Vector3 a, Vector3 b, Vector3 n, float wa, float wb, Color ca, Color cb)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-10f) return;
            Vector3 side = Vector3.Cross(n, d).normalized;
            int i0 = _kv.Count;
            _kv.Add(a - side * wa); _kv.Add(a + side * wa); _kv.Add(b - side * wb); _kv.Add(b + side * wb);
            for (int q = 0; q < 4; q++) _kn.Add(n);
            _kc.Add(ca); _kc.Add(ca); _kc.Add(cb); _kc.Add(cb);
            // лицом наружу (по n)
            if (Vector3.Dot(Vector3.Cross(_kv[i0 + 2] - _kv[i0], _kv[i0 + 1] - _kv[i0]), n) > 0f)
            { _kt.Add(i0); _kt.Add(i0 + 2); _kt.Add(i0 + 1); _kt.Add(i0 + 1); _kt.Add(i0 + 2); _kt.Add(i0 + 3); }
            else
            { _kt.Add(i0); _kt.Add(i0 + 1); _kt.Add(i0 + 2); _kt.Add(i0 + 1); _kt.Add(i0 + 3); _kt.Add(i0 + 2); }
        }

        private static float Hash01(int n)
        {
            unchecked { uint h = (uint)n * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (h & 0xFFFF) / 65535f; }
        }

        private void MakeKerfObject()
        {
            _kerfGo = new GameObject("SawKerf (auto)") { layer = gameObject.layer, hideFlags = HideFlags.DontSave };
            _kerfGo.transform.SetParent(transform, false);
            _kerfMesh = new Mesh { name = "CarcassSawKerf" };
            _kerfGo.AddComponent<MeshFilter>().sharedMesh = _kerfMesh;
            MeshRenderer mr = _kerfGo.AddComponent<MeshRenderer>();
            // материал туши (тот же тун-свет, цвет из вершин), но без контура — иначе каждая полоска в чёрной обводке
            Material src = null;
            for (int i = 0; i < PartCount && src == null; i++)
                if (_parts[i] != null) { var r = _parts[i].GetComponentInChildren<Renderer>(true); if (r != null) src = r.sharedMaterial; }
            Material m;
            if (src != null)
            {
                m = new Material(src) { name = "CarcassKerf", hideFlags = HideFlags.DontSave };
                if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0f);
            }
            else
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                m = new Material(sh != null ? sh : Shader.Find("Unlit/Color")) { hideFlags = HideFlags.DontSave };
                m.color = _cutLip;
            }
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ── брызги и крошка из-под пилы (у каждого, по новым точкам пути пилы) ──
        private ParticleSystem _fx;
        private int _fxDone = -1;

        private void SawFx()
        {
            if (!_sawFx || Application.isBatchMode) return;
            int n = _kerf.Count;
            if (_fxDone < 0 || n < _fxDone || n - _fxDone > 12) { _fxDone = n; return; }   // при входе в игру старое не брызгает
            if (n == _fxDone) return;
            if (_fx == null) MakeFx();
            float sz = _band * 0.09f * _sawFxSize;
            for (; _fxDone < n; _fxDone++)
            {
                Vector4 kk = _kerf[_fxDone];
                Vector3 w = transform.TransformPoint(new Vector3(kk.x, kk.y, kk.z));
                Vector3 nrm = Vector3.up;
                int best = _fxDone < _kerfNear.Count ? _kerfNear[_fxDone] : -1;
                if (best >= 0 && best < _ms.Count) nrm = transform.TransformDirection(_ms[best].N).normalized;
                for (int q = 0; q < _sawFxPerPoint; q++)
                {
                    float r = Random.value;
                    Color c = r < 0.55f ? Color.Lerp(_cutDeep, _cutLip, Random.value)            // мясо / кровь
                            : r < 0.85f ? _cutSpeck                                                // костная пыль
                            : new Color(1f, 0.93f, 0.7f);                                          // жир
                    var ep = new ParticleSystem.EmitParams
                    {
                        position = w + nrm * _band * 0.05f,
                        velocity = (nrm * Random.Range(0.8f, 2.2f) + Random.insideUnitSphere * 1.2f + Vector3.up * 0.6f) * Mathf.Clamp(_band * 6f, 0.4f, 2f),
                        startSize = sz * (r < 0.55f ? Random.Range(0.6f, 1.4f) : Random.Range(0.25f, 0.6f)),
                        startLifetime = Random.Range(0.35f, 0.8f),
                        startColor = c,
                        rotation = Random.Range(0f, 360f),
                    };
                    _fx.Emit(ep, 1);
                }
            }
        }

        private void MakeFx()
        {
            var go = new GameObject("SawFx (auto)") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            _fx = go.AddComponent<ParticleSystem>();
            _fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _fx.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 2f;
            main.maxParticles = 600;
            var em = _fx.emission; em.enabled = false;
            var shp = _fx.shape; shp.enabled = false;
            var sol = _fx.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var col = _fx.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World;
            col.dampen = 0.7f; col.bounce = 0.1f; col.lifetimeLoss = 0.3f; col.quality = ParticleSystemCollisionQuality.Low;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh != null) pr.sharedMaterial = new Material(sh) { hideFlags = HideFlags.DontSave };
            else if (_kerfGo != null) pr.sharedMaterial = _kerfGo.GetComponent<MeshRenderer>().sharedMaterial;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fx.Play();
        }

        private static float BoxDist(PartBox b, Vector3 w)
        {
            Vector3 l = b.t.InverseTransformPoint(w) - b.c;
            Vector3 q = new Vector3(Mathf.Clamp(l.x, -b.h.x, b.h.x), Mathf.Clamp(l.y, -b.h.y, b.h.y), Mathf.Clamp(l.z, -b.h.z, b.h.z));
            return Vector3.Distance(b.t.TransformPoint(q + b.c), w);
        }

        /// <summary>Доля пропиленного шва вокруг куска (−1 — шва нет: последний кусок или нет коробок).</summary>
        private float SeamProgress(int part)
        {
            if (_model)
            {
                if (_ms == null || part < 0 || part >= PartCount) return -1f;
                int mt = 0, mc = 0;
                for (int k = 0; k < _ms.Count; k++)
                {
                    if (_msHidden[k] || (_msA[k] != part && _msB[k] != part)) continue;
                    mt++; if (_msCut[k]) mc++;
                }
                return mt == 0 ? -1f : mc / (float)mt;
            }
            if (_seamPts == null || part < 0 || part >= PartCount) return -1f;
            int tot = 0, cut = 0;
            for (int k = 0; k < _seamPts[part].Count; k++)
            {
                int nb = _seamNb[part][k];
                if (nb >= 0 && IsRemoved(nb)) continue;
                tot++;
                if (_seamCut[part][k]) cut++;
            }
            return tot == 0 ? -1f : cut / (float)tot;
        }

        /// <summary>
        /// Сервер: пила (любая) в точке world. radiusMul — насколько широко берёт инструмент
        /// (бензопила 1, ножовка за штрих ~2, траншеекопатель ~1.6). Отпадает кусок, у которого шов пропилен на Seam Need.
        /// </summary>
        [Server]
        public void ServerSawAt(Vector3 world, float radiusMul, bool newStroke, NetworkObject tool = null, JamKind kind = JamKind.None, float snapMul = 1f)
        {
            if (!_traceSeam || !CanCut) return;
            EnsureSeams();
            if (Jammed)
            {
                // застрял траншеекопатель — его освобождают пилой у цепи
                if (_jamKind.Value == (byte)JamKind.Trencher && (kind == JamKind.Handsaw || kind == JamKind.Chainsaw) && NearJam(world))
                {
                    if (Time.time - _lastFreeMark < 0.3f) return;
                    _lastFreeMark = Time.time;
                    AddKerf(world, newStroke, 0.05f);
                    _freeNet.Value = (byte)Mathf.Min(255, _freeNet.Value + 1);
                    if (_log) Debug.Log($"[Туша] «{name}»: отпиливают траншеекопатель {_freeNet.Value}/{_trencherFreeMarks}", this);
                    if (_freeNet.Value >= _trencherFreeMarks) ServerUnjam("траншеекопатель отпилили");
                }
                return;
            }
            float r = _band * MarkR * Mathf.Max(0.2f, radiusMul), r2 = r * r;
            int bestPart = -1; float bestP = -1f;
            // v28.6: пила прилипает к линии — пропил всегда ровно по шву, скорость одинаковая везде
            if (_model && !SnapModel(world, _band * _snapRadius * Mathf.Max(0.2f, snapMul), out world)) return;
            if (_model) bestPart = MarkModel(world, r, out bestP);
            else for (int i = 0; i < PartCount; i++)
            {
                if (IsRemoved(i)) continue;
                bool hit = false;
                for (int k = 0; k < _seamPts[i].Count; k++)
                {
                    if (_seamCut[i][k]) continue;
                    if ((transform.TransformPoint(_seamPts[i][k]) - world).sqrMagnitude <= r2) { _seamCut[i][k] = true; hit = true; }
                }
                if (!hit) continue;
                float p = SeamProgress(i);
                if (p > bestP) { bestP = p; bestPart = i; }
            }
            AddKerf(world, newStroke, radiusMul);
            if (bestPart < 0) return;
            byte pct = (byte)Mathf.Clamp(Mathf.RoundToInt(bestP * 100f), 0, 100);
            if (pct != _seamPct.Value)
            {
                if (_log && pct / 20 != _seamPct.Value / 20) Debug.Log($"[Туша] «{name}»: шов у «{_parts[bestPart].name}» {pct}% (нужно {Need * 100f:0}%)", this);
                _seamPct.Value = pct;
            }
            ServerSetFocus(bestPart);
            if (bestP >= Need - 1e-4f) { ServerCutPartNow(bestPart, 1f); return; }
            if (kind != JamKind.None && tool != null && RollJam(kind)) ServerJam(tool, kind, world);
        }

        [Server]
        private void AddKerf(Vector3 world, bool newStroke, float radiusMul = 1f)
        {
            Vector3 l = transform.InverseTransformPoint(world);
            bool add = newStroke || _kerf.Count == 0;
            if (!add)
            {
                Vector4 last = _kerf[_kerf.Count - 1];
                add = (transform.TransformPoint(new Vector3(last.x, last.y, last.z)) - world).sqrMagnitude >= (_band * 0.35f) * (_band * 0.35f);
            }
            // w: ширина инструмента (radiusMul); +100 — начало штриха. Клиенты по ним сами помечают линии разделки
            if (add && _kerf.Count < 1500) _kerf.Add(new Vector4(l.x, l.y, l.z, Mathf.Clamp(radiusMul, 0.01f, 20f) + (newStroke ? 100f : 0f)));
        }

        // ───────────────────────── v25.6: ЗАСТРЕВАНИЕ И «ТЯНИ ВМЕСТЕ» ─────────────────────────
        //
        // Ножовка / бензопила застряла: пила вылетает из рук и торчит в туше (её не взять), пилить тушу нельзя.
        // Вытаскивают рывками: сервер отбивает такт (Tug Beat), все рядом жмут Pull Key в такт.
        // Сила такта = (сколько попало в такт)^1.5: один — 6 тактов, двое — 3, трое — 2. Вразнобой — минус.
        // Кто тянет — видно «цепочкой» от пилы к котам (друг за другом).
        // Траншеекопатель застрял: экскаватор не едет и цепь стоит; освобождают ПИЛОЙ у цепи (Trencher Free Marks касаний).

        public bool Jammed => _jamKind.Value != 0;
        private float _lastJamRoll = -10f, _lastFreeMark = -10f, _nextBeat;
        private Vector3 _pinLocalPos; private Quaternion _pinLocalRot;
        // v28.3: поза застрявшей пилы — у всех (клиенты тоже держат её неподвижно, без физики)
        private readonly SyncVar<Vector3> _pinPosNet = new SyncVar<Vector3>();
        private readonly SyncVar<Quaternion> _pinRotNet = new SyncVar<Quaternion>(Quaternion.identity);
        private NetworkObject _pinnedTool;          // что держим у себя
        private bool _pinnedWasKinematic;
        private Collider[] _pinIgnored;             // столкновения пила ↔ туша выключены
        private float _unignoreAt = -1f;
        private readonly HashSet<int> _goodPulls = new HashSet<int>();
        private readonly List<NetworkObject> _goodObjs = new List<NetworkObject>();
        private int _badPulls;

        private Vector3 JamWorld => transform.TransformPoint(_jamLocal.Value);
        private bool NearJam(Vector3 world) => (world - JamWorld).sqrMagnitude <= (Band() * 3f) * (Band() * 3f);
        private float TugReach => Mathf.Max(4f, _size * 0.7f);
        private float _size = 1f;

        private bool RollJam(JamKind kind)
        {
            if (kind == JamKind.Handsaw) return Random.value < _jamHandsaw;
            if (Time.time - _lastJamRoll < 0.5f) return false;
            _lastJamRoll = Time.time;
            float rate = kind == JamKind.Chainsaw ? _jamChainsaw : _jamTrencher;
            return Random.value < rate * 0.5f;
        }

        [Server]
        private void ServerJam(NetworkObject tool, JamKind kind, Vector3 world)
        {
            _jamLocal.Value = transform.InverseTransformPoint(world);
            _jamTool.Value = tool;
            _tug.Value = 0; _freeNet.Value = 0;
            _pullers.Clear(); _goodPulls.Clear(); _goodObjs.Clear(); _badPulls = 0;
            _nextBeat = Time.time + _tugBeat;
            if (kind == JamKind.Trencher)
            {
                var veh = tool.GetComponent<CCMiniExcavatorVehicle>();
                if (veh != null) veh.ServerSetStuck(true);
            }
            else
            {
                // пила вылетает из рук и торчит в туше полотном в точке пропила
                PlayerHand holder = null;
                foreach (PlayerHand h in Object.FindObjectsByType<PlayerHand>(FindObjectsSortMode.None))
                    if (h != null && h.HeldItem != null && h.HeldItem.NetworkObject == tool) { holder = h; break; }
                if (holder != null) holder.ServerDrop(Vector3.zero, 0f);
                InteractableItem item = tool.GetComponent<InteractableItem>();
                if (item != null) item.PickupLocked = true;
                Rigidbody rb = tool.GetComponent<Rigidbody>();
                if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
                ISawTool saw = tool.GetComponent<ISawTool>();
                Vector3 off = saw != null && saw.Blade != null ? tool.transform.position - saw.Blade.position : Vector3.zero;
                tool.transform.position = world + off;
                _pinLocalPos = transform.InverseTransformPoint(tool.transform.position);
                _pinLocalRot = Quaternion.Inverse(transform.rotation) * tool.transform.rotation;
                _pinPosNet.Value = _pinLocalPos; _pinRotNet.Value = _pinLocalRot;
            }
            _jamKind.Value = (byte)kind;
            if (_log) Debug.Log($"[Туша] «{name}»: ЗАСТРЯЛА {(kind == JamKind.Trencher ? "цепь траншеекопателя — отпилите пилой" : $"пила — тяните вместе ({_pullKey} в такт)")}", this);
        }

        [Server]
        private void ServerUnjam(string why)
        {
            NetworkObject tool = _jamTool.Value;
            JamKind kind = (JamKind)_jamKind.Value;
            _jamKind.Value = 0;
            _jamTool.Value = null;
            _tug.Value = 0; _freeNet.Value = 0;
            _pullers.Clear();
            if (tool != null)
            {
                if (kind == JamKind.Trencher)
                {
                    var veh = tool.GetComponent<CCMiniExcavatorVehicle>();
                    if (veh != null) veh.ServerSetStuck(false);
                }
                else
                {
                    InteractableItem item = tool.GetComponent<InteractableItem>();
                    if (item != null) item.PickupLocked = false;
                    Rigidbody rb = tool.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.isKinematic = false; rb.WakeUp();
                        Vector3 outDir = (tool.transform.position - transform.position).normalized + Vector3.up;
                        rb.AddForce(outDir.normalized * 3f, ForceMode.VelocityChange);
                    }
                }
            }
            if (_log) Debug.Log($"[Туша] «{name}»: освободили — {why}", this);
        }

        /// <summary>Сервер: держать застрявшую пилу в туше, отбивать такт, считать рывки.</summary>
        private void ServerJamTick()
        {
            if (!Jammed) return;
            NetworkObject tool = _jamTool.Value;
            if (tool == null || !tool.IsSpawned) { ServerUnjam("инструмент пропал"); return; }
            if (_jamKind.Value == (byte)JamKind.Trencher) return;
            tool.transform.SetPositionAndRotation(transform.TransformPoint(_pinLocalPos), transform.rotation * _pinLocalRot);
            if (Time.time < _nextBeat) return;
            _nextBeat += _tugBeat;
            if (_nextBeat < Time.time) _nextBeat = Time.time + _tugBeat;
            int good = _goodPulls.Count;
            float gain = Mathf.Pow(good, 1.5f) / Mathf.Max(1f, _tugSoloPulls) * 100f - _badPulls * 4f;
            int tug = Mathf.Clamp(Mathf.RoundToInt(_tug.Value + gain), 0, 100);
            _tug.Value = (byte)tug;
            _pullers.Clear();
            foreach (NetworkObject o in _goodObjs) if (o != null) _pullers.Add(o);
            if (_log && (good > 0 || _badPulls > 0)) Debug.Log($"[Туша] рывок: в такт {good}, вразнобой {_badPulls} → {tug}%", this);
            _goodPulls.Clear(); _goodObjs.Clear(); _badPulls = 0;
            RpcBeat();
            if (tug >= 100) ServerUnjam(good > 1 ? $"вытащили вместе ({good})" : "вытащил один");
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdPull(bool onBeat, FishNet.Connection.NetworkConnection conn = null)
        {
            if (!Jammed || _jamKind.Value == (byte)JamKind.Trencher || conn == null) return;
            NetworkObject pl = conn.FirstObject;
            if (pl == null || (pl.transform.position - JamWorld).sqrMagnitude > TugReach * TugReach) return;
            if (!onBeat) { _badPulls++; return; }
            if (_goodPulls.Add(conn.ClientId)) _goodObjs.Add(pl);
        }

        // ── у каждого: такт, Q, цепочка ──
        private float _beatAt = -10f;
        private LineRenderer _chain;

        [ObserversRpc]
        private void RpcBeat() => _beatAt = Time.time;

        private void Update()
        {
            if (base.IsServerInitialized) ServerJamTick();
            PinTick();
            bool tugging = Jammed && _jamKind.Value != (byte)JamKind.Trencher;
            if (!base.IsClientInitialized || Application.isBatchMode) return;
            Camera cam = Camera.main;
            bool near = cam != null && (cam.transform.position - JamWorld).sqrMagnitude <= TugReach * TugReach;
            if (tugging && near && Input.GetKeyDown(_pullKey) && !UiModal.AnyOpen)
            {
                float since = Time.time - _beatAt, until = _beatAt + _tugBeat - Time.time;
                CmdPull(since <= 0.4f || (until >= 0f && until <= 0.15f));
            }
            UpdateChain(tugging);
        }

        // ── v28.3: застрявшая пила — неподвижна у ВСЕХ, без столкновений с тушей (раньше физика выталкивала её → взрыв и вечное вращение) ──
        private void FixedUpdate() => PinTick();

        private void PinTick()
        {
            NetworkObject want = Jammed && _jamKind.Value != (byte)JamKind.Trencher ? _jamTool.Value : null;
            if (want != _pinnedTool)
            {
                if (_pinnedTool != null) ReleasePin(_pinnedTool);
                _pinnedTool = want;
                if (want != null)
                {
                    Rigidbody r0 = want.GetComponent<Rigidbody>();
                    _pinnedWasKinematic = r0 != null && r0.isKinematic;
                    SetIgnore(want, true);
                    _unignoreAt = -1f;
                }
            }
            if (_pinIgnored != null && _pinnedTool == null && _unignoreAt > 0f && Time.time >= _unignoreAt) { SetIgnore(null, false); _unignoreAt = -1f; }
            if (_pinnedTool == null) return;
            Rigidbody rb = _pinnedTool.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            _pinnedTool.transform.SetPositionAndRotation(transform.TransformPoint(_pinPosNet.Value), transform.rotation * _pinRotNet.Value);
        }

        private void ReleasePin(NetworkObject tool)
        {
            if (tool == null) return;
            if (!base.IsServerInitialized)                    // сервер сам снимает кинематику в ServerUnjam
            {
                Rigidbody rb = tool.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = _pinnedWasKinematic;
            }
            _unignoreAt = Time.time + 1.2f;                    // пусть сначала вылетит из туши
        }

        private void SetIgnore(NetworkObject tool, bool on)
        {
            if (on)
            {
                if (_pinIgnored != null) SetIgnore(null, false);
                _pinIgnored = tool.GetComponentsInChildren<Collider>(true);
            }
            if (_pinIgnored == null) return;
            Collider[] mine = GetComponentsInChildren<Collider>(true);
            foreach (Collider a in _pinIgnored)
                if (a != null)
                    foreach (Collider b in mine)
                        if (b != null) Physics.IgnoreCollision(a, b, on);
            if (!on) _pinIgnored = null;
        }

        private void UpdateChain(bool on)
        {
            if (!on || _pullers.Count == 0) { if (_chain != null) _chain.enabled = false; return; }
            if (_chain == null)
            {
                var go = new GameObject("TugChain (auto)") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                _chain = go.AddComponent<LineRenderer>();
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh != null) { var m = new Material(sh) { hideFlags = HideFlags.DontSave }; m.SetColor("_BaseColor", new Color(1f, 0.85f, 0.2f)); _chain.sharedMaterial = m; }
                _chain.useWorldSpace = true;
                _chain.numCapVertices = 4;
            }
            // от пилы — к ближнему коту, от него — к следующему: «друг за другом»
            var pts = new List<Vector3> { JamWorld };
            var left = new List<NetworkObject>();
            foreach (NetworkObject o in _pullers) if (o != null) left.Add(o);
            Vector3 cur = JamWorld;
            while (left.Count > 0)
            {
                int bi = 0; float bd = float.MaxValue;
                for (int i = 0; i < left.Count; i++) { float d = (left[i].transform.position - cur).sqrMagnitude; if (d < bd) { bd = d; bi = i; } }
                cur = left[bi].transform.position + Vector3.up * 1f;
                pts.Add(cur);
                left.RemoveAt(bi);
            }
            _chain.enabled = true;
            _chain.widthMultiplier = Mathf.Max(0.05f, Band() * 0.25f);
            _chain.positionCount = pts.Count;
            _chain.SetPositions(pts.ToArray());
        }

        private static GUIStyle _hint;

        private void OnGUI()
        {
            if (!Jammed || Event.current.type != EventType.Repaint) return;
            Camera cam = Camera.main;
            if (cam == null || (cam.transform.position - JamWorld).sqrMagnitude > TugReach * TugReach * 2.25f) return;
            string t;
            bool flash = Time.time - _beatAt < 0.3f;
            if (_jamKind.Value == (byte)JamKind.Trencher)
                t = $"Траншеекопатель ЗАСТРЯЛ в туше — отпилите его пилой у цепи ({_freeNet.Value}/{_trencherFreeMarks})";
            else
                t = (flash ? ">>> ТЯНИ! <<<   " : "") + $"Пила застряла в туше — тяните вместе: {_pullKey} в такт   ·   рывок {_tug.Value}%   ·   тянут: {_pullers.Count}";
            if (_hint == null) { _hint = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true }; _hint.normal.textColor = Color.white; }
            float w = Mathf.Min(820f, Screen.width - 40f);
            Color was = GUI.color;
            if (flash) GUI.color = new Color(1f, 0.85f, 0.3f);
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.22f, w, 46f), t, _hint);
            GUI.color = was;
        }

        // ── путь пилы у владельца (бензопила) ──
        private readonly List<Vector4> _pend = new List<Vector4>();
        private Vector3 _recLast;
        private float _recLastAt = -10f, _recFlushAt;

        private void RecordLocal(Vector3 world)
        {
            Vector3 l = transform.InverseTransformPoint(world);
            bool newStroke = Time.time - _recLastAt > 0.35f;
            float step = Band() * 0.25f;
            if (!newStroke && (transform.TransformPoint(_recLast) - world).sqrMagnitude < step * step) { _recLastAt = Time.time; return; }
            _pend.Add(new Vector4(l.x, l.y, l.z, newStroke ? 1f : 0f));
            _recLast = l;
            _recLastAt = Time.time;
            if (_pend.Count >= 16 || Time.time - _recFlushAt >= 0.1f) FlushPath();
        }

        private void FlushPath()
        {
            _recFlushAt = Time.time;
            if (_pend.Count == 0) return;
            Vector4[] arr = _pend.ToArray();
            _pend.Clear();
            CmdSawPath(arr);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdSawPath(Vector4[] pts, FishNet.Connection.NetworkConnection conn = null)
        {
            if (!_traceSeam || !CanCut || pts == null) return;
            // чем пилят: что в руке у приславшего
            NetworkObject tool = null; JamKind kind = JamKind.None;
            PlayerHand hand = conn != null && conn.FirstObject != null ? conn.FirstObject.GetComponentInChildren<PlayerHand>() : null;
            if (hand != null && hand.HeldItem != null)
            {
                tool = hand.HeldItem.NetworkObject;
                if (hand.HeldItem is ChainsawItem) kind = JamKind.Chainsaw;
                else if (hand.HeldItem is HandsawItem) kind = JamKind.Handsaw;
            }
            int n = Mathf.Min(pts.Length, 32);
            for (int i = 0; i < n && CanCut; i++)
                ServerSawAt(transform.TransformPoint(new Vector3(pts[i].x, pts[i].y, pts[i].z)), 1f, pts[i].w > 0.5f, tool, kind);
        }

        // ── вид пропила (у каждого) ──
        private GameObject _kerfGo;
        private Mesh _kerfMesh;
        private int _kerfBuilt = -1;
        private ulong _kerfRemoved = ulong.MaxValue;

        private void LateUpdate()
        {
            if (!_traceSeam || Application.isBatchMode) return;
            if (_model)
            {
                EnsureSeams();
                // путь пилы от сервера → пометить линии у себя (сервер уже пометил — повтор ничего не меняет)
                int kc = _kerf.Count;
                if (_kerfMarked > kc) _kerfMarked = 0;
                for (; _kerfMarked < kc; _kerfMarked++)
                {
                    Vector4 kk = _kerf[_kerfMarked];
                    float rm = kk.w >= 50f ? kk.w - 100f : kk.w;
                    if (rm <= 0.001f) rm = 1f;
                    MarkModel(transform.TransformPoint(new Vector3(kk.x, kk.y, kk.z)), _band * MarkR * Mathf.Max(0.2f, rm), out _);
                }
                int stamp = _msVersion * 4099 + _kerf.Count;
                if (stamp != _kerfBuilt) { _kerfBuilt = stamp; BuildModelKerf(); }
                SawFx();
                return;
            }
            int count = _kerf.Count;
            if (count == _kerfBuilt && _removed.Value == _kerfRemoved) return;
            _kerfBuilt = count; _kerfRemoved = _removed.Value;
            if (count < 2) { if (_kerfMesh != null) _kerfMesh.Clear(); return; }
            EnsureSeams();
            if (_kerfGo == null)
            {
                _kerfGo = new GameObject("SawKerf (auto)") { layer = gameObject.layer, hideFlags = HideFlags.DontSave };
                _kerfGo.transform.SetParent(transform, false);
                _kerfMesh = new Mesh { name = "CarcassSawKerf" };
                _kerfGo.AddComponent<MeshFilter>().sharedMesh = _kerfMesh;
                MeshRenderer mr = _kerfGo.AddComponent<MeshRenderer>();
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                Material fresh = new Material(sh) { hideFlags = HideFlags.DontSave }, dark = new Material(sh) { hideFlags = HideFlags.DontSave };
                fresh.SetColor("_BaseColor", _kerfFresh); fresh.color = _kerfFresh;
                dark.SetColor("_BaseColor", _kerfDark); dark.color = _kerfDark;
                mr.sharedMaterials = new[] { fresh, dark };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var V = new List<Vector3>(); var Tf = new List<int>(); var Td = new List<int>();
            float wf = _band * 0.22f, wd = _band * 0.08f, lift = _band * 0.04f;   // v25.7: тоньше — ровный пропил, не лоскуты
            bool hasPrev = false; Vector3 pw = default, pn = default;
            for (int i = 0; i < count; i++)
            {
                Vector4 k = _kerf[i];
                Vector3 w = transform.TransformPoint(new Vector3(k.x, k.y, k.z));
                if (!SurfaceNormal(w, out Vector3 n)) { hasPrev = false; continue; }   // кусок отпилен — там пусто
                if (hasPrev && k.w < 50f && (w - pw).sqrMagnitude < (_band * 1.4f) * (_band * 1.4f) && Vector3.Dot(n, pn) > 0.6f)
                {
                    Vector3 nn = (n + pn).normalized;
                    Vector3 dir = w - pw;
                    Vector3 side = Vector3.Cross(nn, dir).normalized;
                    if (side.sqrMagnitude > 0.5f)
                    {
                        Quad(V, Tf, pw + pn * lift, w + n * lift, side, wf);
                        Quad(V, Td, pw + pn * lift * 1.5f, w + n * lift * 1.5f, side, wd);
                    }
                }
                pw = w; pn = n; hasPrev = true;
            }
            _kerfMesh.Clear();
            _kerfMesh.SetVertices(V);
            _kerfMesh.subMeshCount = 2;
            _kerfMesh.SetTriangles(Tf, 0);
            _kerfMesh.SetTriangles(Td, 1);
            _kerfMesh.RecalculateNormals();
            _kerfMesh.RecalculateBounds();
        }

        private void Quad(List<Vector3> V, List<int> T, Vector3 a, Vector3 b, Vector3 side, float w)
        {
            int i0 = V.Count;
            V.Add(transform.InverseTransformPoint(a - side * w)); V.Add(transform.InverseTransformPoint(a + side * w));
            V.Add(transform.InverseTransformPoint(b - side * w)); V.Add(transform.InverseTransformPoint(b + side * w));
            T.Add(i0); T.Add(i0 + 2); T.Add(i0 + 1);
            T.Add(i0 + 1); T.Add(i0 + 2); T.Add(i0 + 3);
            // и обратная сторона — чтобы было видно при любом порядке обхода
            T.Add(i0); T.Add(i0 + 1); T.Add(i0 + 2);
            T.Add(i0 + 1); T.Add(i0 + 3); T.Add(i0 + 2);
        }

        /// <summary>Нормаль поверхности оставшегося куска у точки (по ближайшей коробке). false — рядом всё отпилено.</summary>
        private bool SurfaceNormal(Vector3 w, out Vector3 n)
        {
            n = Vector3.up;
            float best = float.MaxValue; PartBox bb = default; bool any = false;
            foreach (PartBox b in _boxes)
            {
                if (IsRemoved(b.part)) continue;
                float d = BoxDist(b, w);
                if (d < best) { best = d; bb = b; any = true; }
            }
            if (!any || best > _band * 1.5f) return false;
            Vector3 l = bb.t.InverseTransformPoint(w) - bb.c;
            int ax = 0; float m = -1f;
            for (int a = 0; a < 3; a++)
            {
                float f = Mathf.Abs(l[a]) / Mathf.Max(1e-5f, bb.h[a]);
                if (f > m) { m = f; ax = a; }
            }
            Vector3 dl = Vector3.zero; dl[ax] = Mathf.Sign(l[ax]);
            n = bb.t.TransformDirection(dl).normalized;
            return true;
        }

        private void OnDestroy()
        {
            if (_kerfGo != null) Destroy(_kerfGo);
            if (_chain != null) Destroy(_chain.gameObject);
        }

        // ───────────────────────── ЖИЗНЬ ─────────────────────────

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _rb = GetComponent<Rigidbody>();
            CheckRibsPrefab();
            if (_parts == null || _parts.Length == 0) _parts = CollectParts().ToArray();
            if (_parts.Length > 64) System.Array.Resize(ref _parts, 64);
            DetectModel();
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
            // v25.6: куски больше не разлетаются — тихо отваливаются (вместо этого пила иногда застревает)
            CatCooks.Food.Supply.SupplySettle.Calm(go);
            rb.linearVelocity = Vector3.zero;
            if (_traceSeam) return;
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
