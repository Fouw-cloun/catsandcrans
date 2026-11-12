using System.Reflection;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v18.5: ВИД ВЕЩЕСТВА НА ПОЛОТНЕ ЛОПАТЫ / МАСТЕРКА — отдельный компонент, ScoopItem не меняется.
    ///
    /// Вешается САМ на каждую лопату и мастерок при запуске (ищет ScoopItem в сцене, в том числе
    /// появившиеся позже). Читает у лопаты, что лежит на полотне (продукт, состояние, количество),
    /// и если у продукта есть профиль вещества (масло, тёртое какао) — рисует свою горку:
    ///   • форма по профилю: масло — сбитый ком с мазками и завитком, порошок — комковатая горка;
    ///   • крупная, по форме полотна (не меньше Blade Cover), лежит НА полотне и поворачивается с ним;
    ///   • дрожит (джигл), капли/крошки с края при рывке, пыль и звук при зачерпывании;
    ///   • старую горку и кубик («BladePiece (auto)») прячет.
    /// Нет профиля — показывает старую горку как было, только кладёт её на полотно (не крутится).
    /// По сети не ходит: у каждого игрока своя картинка из синхронных полей лопаты.
    ///
    /// НАГРУЗКА (v18.6): лопаты ищутся только при загрузке сцены и ещё 2 раза в первые 3 секунды —
    /// потом поиска нет. Поля лопаты читаются только когда лопата сменила груз (сигнал от горки
    /// на полотне), а не каждый кадр. Каждый кадр — только поза горки (несколько умножений),
    /// и то лишь пока на полотне масло/порошок. Можно повесить компонент руками и выключить
    /// автоподключение в таблице веществ (Auto Attach To Scoops).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScoopSubstanceView : MonoBehaviour
    {
        // ───────────────────────── АВТОПОДКЛЮЧЕНИЕ ─────────────────────────

        private static System.Type _scoopType;
        private static bool _typeSearched;
        private bool _booter;
        private float _bootT;
        private int _scans;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            FoodSubstanceTable tab = FoodSubstanceTable.Instance;
            if (tab != null && !tab.AutoAttachToScoops) return;
            var go = new GameObject("CC Scoop Substance (auto)") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<ScoopSubstanceView>()._booter = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static ScoopSubstanceView _boot;

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            if (_boot != null) { _boot._scans = 0; _boot._bootT = 0f; _boot.enabled = true; }
        }

        private static System.Type ScoopType
        {
            get
            {
                if (_typeSearched) return _scoopType;
                _typeSearched = true;
                _scoopType = System.Type.GetType("CatCooks.Food.Tools.ScoopItem, Assembly-CSharp");
                if (_scoopType == null)
                    foreach (Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        foreach (System.Type t in SafeTypes(a))
                            if (t.Name == "ScoopItem" && typeof(MonoBehaviour).IsAssignableFrom(t)) { _scoopType = t; break; }
                        if (_scoopType != null) break;
                    }
                if (_scoopType == null) Debug.LogWarning("[Вещества] не нашёл класс ScoopItem — вид веществ на лопатах не подключится");
                return _scoopType;
            }
        }

        private static System.Type[] SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); } catch { return new System.Type[0]; }
        }

        private void ScanScoops()
        {
            System.Type t = ScoopType;
            if (t == null) return;
            Object[] all = Object.FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Object o in all)
            {
                MonoBehaviour mb = o as MonoBehaviour;
                if (mb == null || mb.GetComponent<ScoopSubstanceView>() != null) continue;
                mb.gameObject.AddComponent<ScoopSubstanceView>();
            }
        }

        // ───────────────────────── ЛОПАТА ─────────────────────────

        public enum BladeUp : byte { Auto = 0, PlusY = 1, MinusY = 2, PlusZ = 3, MinusZ = 4, PlusX = 5, MinusX = 6 }

        [Tooltip("Какая ось объекта Blade — «верх полотна» (от неё растёт горка). Auto — та, что смотрит вверх, " +
                 "когда инструмент лежит при запуске игры")]
        [SerializeField] private BladeUp _bladeUp = BladeUp.Auto;
        [Tooltip("Приподнять горку над точкой Blade, м (если горка «проваливается» в полотно)")]
        [Range(0f, 0.05f)][SerializeField] private float _lift = 0.006f;
        [Tooltip("v18.11: размер полотна под горку. ScoopItem считает полотно маленьким (0.12×0.09 / 0.22×0.18 м), " +
                 "а у твоих моделей оно крупнее — горка во столько раз больше")]
        [Range(0.5f, 8f)][SerializeField] private float _bladeScale = 3f;
        [Tooltip("v18.11: дрожь — насколько качается макушка (доля высоты горки). Основание не двигается")]
        [Range(0f, 0.5f)][SerializeField] private float _wobbleTop = 0.16f;

        private MonoBehaviour _scoop;
        private FieldInfo _fProd, _fState, _fCount, _fBlade, _fCap;
        private Transform _blade;
        private FoodHeapRenderer _oldHeap;
        private int _prod = -1, _state = -1, _count = -1;
        private FoodSubstance _sub;
        private Color _col;

        private GameObject _view;
        private MeshFilter _mf;
        private MeshRenderer _mr;
        private Mesh _mesh;
        private FoodSubstanceMesh.Dims _dims;
        private Vector3 _axis = Vector3.up;
        private bool _axisSet;

        private Vector3 _prevPos, _prevVel, _acc;
        private bool _tracked;
        private Vector2 _tilt, _tiltVel;
        private float _sq, _sqVel, _dripCd, _steamCd;

        private void Start()
        {
            if (_booter) { _boot = this; return; }
            System.Type t = ScoopType;
            _scoop = t != null ? GetComponent(t) as MonoBehaviour : null;
            if (_scoop == null) { enabled = false; return; }
            var fl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            System.Type ty = _scoop.GetType();
            _fProd = ty.GetField("_loadProduct", fl);
            _fState = ty.GetField("_loadState", fl);
            _fCount = ty.GetField("_loadCount", fl);
            _fBlade = ty.GetField("_blade", fl);
            _fCap = ty.GetField("_capacity", fl);
            bool ok = _fProd != null && _fCount != null;
            Debug.Log(ok ? $"[Вещества] «{name}»: вид веществ на полотне подключён (v18.5) — масло и порошок по профилю, горка лежит на полотне"
                         : $"[Вещества] «{name}»: у лопаты нет полей _loadProduct/_loadCount — вид веществ на полотне не включится", this);
            if (!ok) enabled = false;
        }

        private void Update()
        {
            if (!_booter) return;
            // поиск лопат: сразу, через 1 с и через 3 с после загрузки сцены — и всё
            _bootT += Time.unscaledDeltaTime;
            float[] at = { 0f, 1f, 3f };
            if (_scans < at.Length && _bootT >= at[_scans]) { _scans++; ScanScoops(); }
            if (_scans >= at.Length) enabled = false;
        }

        private void LateUpdate()
        {
            if (_booter || _scoop == null) return;
            if (_blade == null)
            {
                _blade = _fBlade != null ? _fBlade.GetValue(_scoop) as Transform : null;
                if (_blade == null) return;
                PickAxis();                                                        // инструмент лежит — его «верх» и есть верх полотна
            }
            if (_oldHeap == null)
            {
                _oldHeap = _blade.GetComponent<FoodHeapRenderer>();
                if (_oldHeap != null) _oldHeap.Shown += MarkDirty;              // лопата сменила груз → перечитать
                _dirty = true;
            }

            if (_dirty)
            {
                _dirty = false;
                int prod = SyncInt(_fProd), st = Mathf.Max(0, SyncInt(_fState)), cnt = Mathf.Max(0, SyncInt(_fCount));
                if (prod != _prod || st != _state || cnt != _count) Changed(prod, st, cnt);
            }

            if (_sub != null && _count > 0)
            {
                HideOld();
                Pose();
            }
            else GlueOld();
        }

        private bool _dirty = true;
        private int _noProfileLogged = -1;
        private void MarkDirty() { _dirty = true; }

        private void Changed(int prod, int st, int cnt)
        {
            int before = _count;
            _prod = prod; _state = st; _count = cnt;
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef def = db != null && prod > 0 ? db.Get((ushort)prod) : null;
            FoodSubstance sub = def != null ? FoodSubstance.Of(def, (FoodState)st) : null;
            if (sub != null && !sub.OwnLook) sub = null;
            // v18.9: на полотне что-то есть, а профиля нет — сказать, что именно (по разу на продукт)
            if (cnt > 0 && def != null && sub == null && _noProfileLogged != prod)
            {
                _noProfileLogged = prod;
                GameObject piece;
                bool asPiece = SpongePiece.TryGetPrefab((ushort)prod, out piece);
                Debug.Log($"[Вещества] «{name}»: на полотне «{def.Title}» (id {prod}, {FoodStateInfo.NameOf((FoodState)st)}) ×{cnt} — профиля вещества НЕТ, " +
                          $"рисуется по-старому{(asPiece ? " (кубиком: у продукта есть префаб кусочка)" : "")}. " +
                          "Добавь это название в таблицу FoodSubstances (Names → Words) или строку в Products", this);
            }
            if (sub == null || cnt <= 0)
            {
                if (_sub != null) ShowOld();
                _sub = null;
                if (_view != null) _view.SetActive(false);
                return;
            }
            if (sub != _sub) Debug.Log($"[Вещества] «{name}»: на полотне «{def.Title}» ×{cnt} → профиль «{sub.Title}»", this);
            _sub = sub;
            _col = sub.ColorFor(FoodColors.Of(def, (FoodState)st));
            if (!_axisSet) PickAxis();
            Build(cnt);
            if (before >= 0 && cnt > before)
            {
                Kick(0.8f);
                FoodSubstanceFx.Scoop(sub, _blade.position + Vector3.up * _dims.H * 0.5f, _col, Mathf.Max(_dims.Rx, _dims.Rz) * 2f);
            }
            else if (cnt < before) Kick(-0.6f);
        }

        // ───────────────────────── ФОРМА ─────────────────────────

        private void Build(int portions)
        {
            if (_view == null)
            {
                _view = new GameObject("SubstanceHeap (auto)") { hideFlags = HideFlags.DontSave };
                _view.layer = _blade.gameObject.layer;
                _view.transform.SetParent(_blade, false);
                _mf = _view.AddComponent<MeshFilter>();
                _mr = _view.AddComponent<MeshRenderer>();
                _mesh = new Mesh { name = "Substance Blade", hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
                _mf.sharedMesh = _mesh;
            }
            _view.SetActive(true);
            int cap = _fCap != null ? (int)_fCap.GetValue(_scoop) : 6;
            Vector2 fp = (_oldHeap != null ? _oldHeap.Footprint : (cap <= 2 ? new Vector2(0.06f, 0.045f) : new Vector2(0.11f, 0.09f))) * _bladeScale;
            float pv = (_oldHeap != null ? _oldHeap.PortionVolume : 0.004f) * _bladeScale * _bladeScale * _bladeScale;
            _dims = Dims(_sub, fp, portions * pv);
            FishNet.Object.NetworkBehaviour nb = _scoop as FishNet.Object.NetworkBehaviour;
            int seed = nb != null && nb.IsSpawned ? nb.ObjectId * 31 + 5 : 3;
            FoodSubstanceMesh.SpoilK = FoodStateInfo.IsSpoiled((FoodState)_state) ? 1f : 0f;   // v19: испорченное — видно
            if (_sub.Look == SubstanceLook.Powder) FoodSubstanceMesh.Powder(_mesh, _dims, _sub.Wonky, _col, seed);
            else if (_sub.Look == SubstanceLook.Crystals) FoodSubstanceMesh.Crystals(_mesh, _dims, _sub.CrystalSize, _sub.CrystalDensity, _col, seed);
            else if (_sub.Look == SubstanceLook.Spread) FoodSubstanceMesh.Spread(_mesh, _dims, _sub.Curl * 0.8f, _col, seed);
            else FoodSubstanceMesh.ButterSmear(_mesh, _dims, _sub.Curl, _col, seed);     // v18.7: мазок ножом, а не «кубик»
            FoodSubstanceMesh.SpoilK = 0f;
            Material m = _sub.GetMaterial();
            if (m != null) _mr.sharedMaterial = m;
            _mr.enabled = true;
            _baseV = _mesh.vertices;                                            // для дрожи: исходные вершины
            _workV = (Vector3[])_baseV.Clone();
            _deformed = false;
        }

        private Vector3[] _baseV, _workV;
        private bool _deformed;

        /// <summary>
        /// v18.11: ДРОЖЬ КАК У ЖЕЛЕ — двигаются вершины, а не вся модель: основание стоит на полотне,
        /// макушка чуть отстаёт от рывка (сдвиг ∝ высоте²), по поверхности бежит лёгкая рябь.
        /// Края не «скачут».
        /// </summary>
        private void Jelly(Transform t)
        {
            if (_baseV == null || _mesh == null) return;
            float energy = _tilt.magnitude + _tiltVel.magnitude * 0.04f + Mathf.Abs(_sq);
            if (energy < 0.002f)
            {
                if (_deformed) { _mesh.vertices = _baseV; _deformed = false; }
                return;
            }
            float H = Mathf.Max(1e-4f, _dims.H);
            // сдвиг макушки в мире (м) → в оси меша (с учётом масштаба)
            Vector3 worldShift = new Vector3(_tilt.x, 0f, _tilt.y) * (H * _wobbleTop / 0.25f);
            Vector3 d = t.InverseTransformVector(worldShift);
            d.y = 0f;
            float sq = _sq * 0.5f;
            float time = Time.time * 16f;
            float rip = Mathf.Clamp01(energy * 3f) * 0.012f * H;
            for (int i = 0; i < _baseV.Length; i++)
            {
                Vector3 b = _baseV[i];
                float k = Mathf.Clamp01(b.y / H);
                float k2 = k * k;
                Vector3 w = b + d * k2;
                w.y = b.y * (1f + sq * k) + Mathf.Sin(time + b.x * 40f + b.z * 33f) * rip * k;
                _workV[i] = w;
            }
            _mesh.vertices = _workV;
            _deformed = true;
        }

        /// <summary>Горка по форме полотна: не меньше Blade Cover, до краёв, высота разумная.</summary>
        public static FoodSubstanceMesh.Dims Dims(FoodSubstance sub, Vector2 footprint, float volume)
        {
            var d = new FoodSubstanceMesh.Dims();
            float slope = Mathf.Max(0.1f, sub.FloorSlope * sub.BladeHeight);
            float fx = Mathf.Max(0.005f, footprint.x * 0.94f), fz = Mathf.Max(0.005f, footprint.y * 0.94f);
            float m = Mathf.Min(fx, fz), big = Mathf.Max(fx, fz);
            float vol = volume * sub.BladeVolume;
            float rv = Mathf.Pow(2f * vol / (Mathf.PI * slope), 1f / 3f);
            float grow = Mathf.Clamp01(rv / big);
            float cover = Mathf.Lerp(sub.BladeCover, 1f, grow);
            d.Rx = fx * cover; d.Rz = fz * cover;
            d.H = Mathf.Clamp(2f * vol / (Mathf.PI * d.Rx * d.Rz), m * 0.35f, Mathf.Max(slope * m * 1.15f, m * 0.4f));
            d.Edge = 0f;
            d.RectN = Mathf.Lerp(2.6f, 4f, grow);
            return d;
        }

        // ───────────────────────── ПОЛОЖЕНИЕ, ДРОЖЬ, КАПЛИ ─────────────────────────

        private void PickAxis()
        {
            Vector3[] axes = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            if (_bladeUp != BladeUp.Auto)
            {
                Vector3[] man = { Vector3.up, Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
                _axis = man[(int)_bladeUp];
                _axisSet = true;
                return;
            }
            _axis = Vector3.up;
            float best = Vector3.Dot(_blade.up, Vector3.up);
            if (best < 0.5f)
                for (int i = 1; i < axes.Length; i++)
                {
                    float d = Vector3.Dot(_blade.TransformDirection(axes[i]), Vector3.up);
                    if (d > best) { best = d; _axis = axes[i]; }
                }
            _axisSet = true;
            Debug.Log($"[Вещества] «{name}»: верх полотна — ось {_axis} объекта «{_blade.name}» (если горка легла не так — поле Blade Up на Scoop Substance View)", this);
        }

        private static float AvgInv(Vector3 ls)
        {
            float a = (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f;
            return a < 1e-6f ? 1f : 1f / a;
        }

        private void Pose()
        {
            if (_view == null) return;
            if (!_axisSet) PickAxis();
            float dt = Time.deltaTime;
            Vector3 pos = _blade.position;
            if (dt > 1e-5f)
            {
                if (!_tracked) { _tracked = true; _prevPos = pos; _prevVel = Vector3.zero; }
                Vector3 vel = (pos - _prevPos) / dt;
                Vector3 acc = (vel - _prevVel) / dt;
                _prevPos = pos; _prevVel = vel;
                if (vel.sqrMagnitude > 900f) acc = Vector3.zero;
                _acc = Vector3.Lerp(_acc, acc, 1f - Mathf.Exp(-dt * 20f));
                float jig = _sub.Jiggle;
                Vector2 target = Vector2.ClampMagnitude(new Vector2(-_acc.x, -_acc.z) * 0.006f * jig, 0.25f);
                float sqT = Mathf.Clamp(-_acc.y * 0.002f * jig, -0.12f, 0.12f);
                float k = Mathf.Lerp(320f, 90f, jig), damp = Mathf.Lerp(20f, 6f, jig);
                int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
                float h = dt / n;
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = (target - _tilt) * k - _tiltVel * damp;
                    _tiltVel += a * h; _tilt += _tiltVel * h;
                    float sa = (sqT - _sq) * k - _sqVel * damp;
                    _sqVel += sa * h; _sq += _sqVel * h;
                }
                _tilt = Vector2.ClampMagnitude(_tilt, 0.4f);
                _sq = Mathf.Clamp(_sq, -0.3f, 0.3f);

                // капли/крошки с края, когда дёргаешь (только вид)
                _dripCd -= dt;
                if (_sub.EdgeDrips && _dripCd <= 0f && _acc.magnitude > _sub.DripShake)
                {
                    _dripCd = Random.Range(0.12f, 0.3f);
                    Vector3 side = Vector3.ProjectOnPlane(-_acc, Vector3.up);
                    side = side.sqrMagnitude > 1e-6f ? side.normalized : Random.onUnitSphere;
                    float R = Mathf.Max(_dims.Rx, _dims.Rz);
                    float ds = Mathf.Max(0.006f, R * 2f * _sub.DripSize);
                    FoodSubstanceFx.Drip(_sub, pos + side * R * 0.9f + Vector3.up * _dims.H * 0.3f, vel + side * 0.3f, _col, ds);
                }
                _steamCd -= dt;
                if (_sub.Steam && _steamCd <= 0f)
                {
                    _steamCd = Random.Range(0.5f, 1f);
                    FoodSubstanceFx.Steam(pos + Vector3.up * _dims.H, Mathf.Max(_dims.Rx, _dims.Rz));
                }
            }
            Transform t = _view.transform;
            t.localPosition = _axis * (_lift * AvgInv(_blade.lossyScale));
            t.localRotation = Quaternion.FromToRotation(Vector3.up, _axis);
            t.localScale = Vector3.one * AvgInv(_blade.lossyScale);
            Jelly(t);                                                            // v18.11: качается макушка, а не вся модель
        }

        private void Kick(float s)
        {
            if (_sub == null) return;
            float j = Mathf.Max(0.05f, _sub.Jiggle);
            _sqVel += s * 2.2f * j;
            _tiltVel += Random.insideUnitCircle * Mathf.Abs(s) * 0.8f * j;
        }

        // ───────────────────────── СТАРАЯ ГОРКА ─────────────────────────

        private void HideOld()
        {
            if (_oldHeap != null && _oldHeap.enabled) _oldHeap.enabled = false;     // без Update — без частиц
            SetChildRenderer("HeapMesh (auto)", false);
            Transform bp = _blade.Find("BladePiece (auto)");
            if (bp != null && bp.gameObject.activeSelf) bp.gameObject.SetActive(false);
        }

        private void ShowOld()
        {
            if (_oldHeap != null && !_oldHeap.enabled) _oldHeap.enabled = true;
            SetChildRenderer("HeapMesh (auto)", true);
        }

        /// <summary>Горка без профиля — лежит на полотне (не «вверх по миру»).</summary>
        private void GlueOld()
        {
            if (_oldHeap != null && !_oldHeap.enabled) _oldHeap.enabled = true;
            Transform hm = _blade.Find("HeapMesh (auto)");
            if (hm == null || _count <= 0) return;
            if (!_axisSet) PickAxis();
            hm.localRotation = Quaternion.FromToRotation(Vector3.up, _axis);
        }

        private void SetChildRenderer(string child, bool on)
        {
            Transform c = _blade.Find(child);
            if (c == null) return;
            Renderer r = c.GetComponent<Renderer>();
            if (r != null && r.enabled != on) r.enabled = on;
        }

        private int SyncInt(FieldInfo f)
        {
            if (f == null || _scoop == null) return -1;
            object sv = f.GetValue(_scoop);
            if (sv == null) return -1;
            PropertyInfo pv = sv.GetType().GetProperty("Value");
            object v = pv != null ? pv.GetValue(sv, null) : sv;
            try { return System.Convert.ToInt32(v); } catch { return -1; }
        }

        private void OnDestroy()
        {
            if (_oldHeap != null) _oldHeap.Shown -= MarkDirty;
            if (_mesh != null) CCHeatFx.Kill(_mesh);
            if (_view != null) CCHeatFx.Kill(_view);
        }
    }
}
