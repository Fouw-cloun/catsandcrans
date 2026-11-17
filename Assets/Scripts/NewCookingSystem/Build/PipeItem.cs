using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// v23.52: ТРУБА ДЛЯ КОЛЕЦ МАКАРОН. Стальная труба с жёлтыми торцами, диаметр — как кольцо противня.
    ///
    ///   • Лежит на полу (или на верстаке). Берётся в руки (F), носится.
    ///   • ПИЛОЙ или БЕНЗОПИЛОЙ целишься в конец трубы — рамка показывает, где отрежется. Пилишь — от конца отваливается
    ///     КОЛЬЦО (отдельный предмет, Ring Prefab), труба становится короче. Кончилась — исчезает.
    ///   • Кольца бросаешь в шаблон «Противень для макарон» (меню построек, C → 2) — встают по одному в гнёзда.
    ///
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · Box Collider · этот компонент.
    /// Модель строится сама (дочерний «Pipe (auto)»). Делает меню «Стройка → 4». Ring Prefab — кольцо (меню «Стройка → 5»,
    /// сохрани префабом, добавь в Spawnable Prefabs FishNet и перетащи сюда).
    /// </summary>
    public sealed class PipeItem : PickupItem, ICuttable, ICuttableAim
    {
        public static readonly Color PipeColor = new Color(0.56f, 0.60f, 0.66f, 0f);
        public static readonly Color CutColor = new Color(0.99f, 0.76f, 0.12f, 0f);

        [Header("Труба")]
        [Tooltip("Внутренний радиус трубы, м (= радиус кольца противня)")]
        [SerializeField] private float _innerRadius = 0.09f;
        [Tooltip("Наружный радиус, м")]
        [SerializeField] private float _outerRadius = 0.1f;
        [Tooltip("Высота одного кольца (длина отрезка), м")]
        [SerializeField] private float _ringHeight = 0.03f;
        [Tooltip("Сколько колец выходит из целой трубы")]
        [Range(1, 60)][SerializeField] private int _rings = 13;
        [Tooltip("Кольцо — отдельный предмет (префаб с PipeRing, в Spawnable Prefabs FishNet)")]
        [SerializeField] private GameObject _ringPrefab;
        [Tooltip("Материал (цвет в вершинах). Пусто — свой")]
        [SerializeField] private Material _material;

        [Header("Пила")]
        [Tooltip("Секунд пилить одно кольцо")]
        [SerializeField] private float _secondsPerCut = 0.9f;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _left = new SyncVar<byte>(255);
        private Transform _view;
        private MeshFilter _mf;
        private BoxCollider _box;
        private int _shownLeft = -1;
        private bool _clamped, _wasKin;

        private float Full => _rings * _ringHeight + _ringHeight * 0.4f;     // целая труба (с запасом на неровный торец)
        private int Left => _left.Value == 255 ? _rings : _left.Value;
        private float Length => Left * _ringHeight + _ringHeight * 0.4f;

        protected override void Awake()
        {
            _box = GetComponent<BoxCollider>();
            if (_box == null) _box = gameObject.AddComponent<BoxCollider>();
            base.Awake();
            _canPickup = true;
            Rebuild();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_left.Value == 255) _left.Value = (byte)Mathf.Clamp(_rings, 1, 250);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _left.OnChange += OnLeft;
            Rebuild();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _left.OnChange -= OnLeft;
        }

        private void OnLeft(byte a, byte b, bool asServer) => Rebuild();

        // ───────────────────────── вид ─────────────────────────

        /// <summary>Труба вдоль +Z; укорачивается с конца +Z (там пилят), конец −Z стоит на месте.</summary>
        private void Rebuild()
        {
            int left = Left;
            if (left == _shownLeft && _view != null) return;
            _shownLeft = left;
            if (_view == null)
            {
                Transform t = transform.Find("Pipe (auto)");
                if (t == null)
                {
                    var go = new GameObject("Pipe (auto)");
                    go.layer = gameObject.layer;
                    go.transform.SetParent(transform, false);
                    t = go.transform;
                    _mf = go.AddComponent<MeshFilter>();
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _material != null ? _material : AutoMat();
                }
                else _mf = t.GetComponent<MeshFilter>();
                _view = t;
            }
            float L = Mathf.Max(_ringHeight * 0.3f, Length);
            if (_mf != null)
            {
                if (_mf.sharedMesh != null && _mf.sharedMesh.name.EndsWith("(auto)")) Destroy(_mf.sharedMesh);
                Mesh m = MacaronMesh.Tube(_innerRadius, _outerRadius, L, PipeColor, CutColor, 32);
                m.name = "Pipe (auto)";
                m.hideFlags = HideFlags.DontSave;
                _mf.sharedMesh = m;
            }
            _view.localRotation = Quaternion.Euler(90f, 0f, 0f);             // ось трубы — вдоль +Z
            _view.localPosition = new Vector3(0f, 0f, -Full * 0.5f);
            if (_box != null)
            {
                _box.center = new Vector3(0f, 0f, -Full * 0.5f + L * 0.5f);
                _box.size = new Vector3(_outerRadius * 2f, _outerRadius * 2f, L);
            }
        }

        private static Material _auto;
        private static Material AutoMat()
        {
            if (_auto != null) return _auto;
            Shader sh = Shader.Find("CatCooks/Food Heap");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            _auto = new Material(sh) { name = "Pipe (auto)", hideFlags = HideFlags.DontSave };
            if (_auto.HasProperty("_BaseColor")) _auto.SetColor("_BaseColor", Color.white);
            if (_auto.HasProperty("_TexDetail")) _auto.SetFloat("_TexDetail", 0f);
            if (_auto.HasProperty("_GlintStrength")) _auto.SetFloat("_GlintStrength", 0.6f);
            return _auto;
        }

        // ───────────────────────── пила ─────────────────────────

        /// <summary>Центр отрезка-кольца у пилимого конца (локально).</summary>
        private Vector3 SliceLocal => new Vector3(0f, 0f, -Full * 0.5f + Length - _ringHeight * 0.7f);

        public bool CanCut => Left > 0 && _ringPrefab != null;
        public float SecondsPerCut => _secondsPerCut;
        public bool ClampWhileCutting => true;
        public string CutLabel => $"труба: колец {Left}";
        public Color CrumbColor => new Color(0.82f, 0.84f, 0.88f, 1f);          // стальная стружка

        public Vector3 CutPoint() => transform.TransformPoint(SliceLocal + Vector3.up * _outerRadius);

        public void ServerAimAt(Vector3 worldPoint) { }

        public bool AimBox(Vector3 worldPoint, Vector3[] corners)
        {
            if (corners == null || corners.Length < 8 || !CanCut) return false;
            Vector3 c = SliceLocal;
            float r = _outerRadius * 1.08f, hz = _ringHeight * 0.55f;
            for (int k = 0; k < 2; k++)
            {
                float y = k == 0 ? -r : r;
                corners[k * 4 + 0] = transform.TransformPoint(c + new Vector3(-r, y, -hz));
                corners[k * 4 + 1] = transform.TransformPoint(c + new Vector3(r, y, -hz));
                corners[k * 4 + 2] = transform.TransformPoint(c + new Vector3(r, y, hz));
                corners[k * 4 + 3] = transform.TransformPoint(c + new Vector3(-r, y, hz));
            }
            return true;
        }

        public void ServerSetClamped(bool value)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null || value == _clamped) return;
            _clamped = value;
            if (value) { _wasKin = rb.isKinematic; rb.isKinematic = true; }
            else { rb.isKinematic = _wasKin; rb.WakeUp(); }
        }

        /// <summary>Пила допилила: от конца отваливается кольцо, труба короче. Последнее кольцо — трубы нет.</summary>
        public void ServerCutOne(ProcessDef cutProcess, float quality01)
        {
            if (!base.IsServerInitialized || !CanCut) return;
            Vector3 at = transform.TransformPoint(SliceLocal);
            Vector3 dir = transform.forward;
            GameObject go = Instantiate(_ringPrefab, at + Vector3.up * (_outerRadius * 0.2f), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            PipeRing ring = go.GetComponent<PipeRing>();
            if (ring != null) ring.Setup(_innerRadius, _outerRadius, _ringHeight, _material);
            base.Spawn(go);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = dir * 0.8f + Vector3.up * 0.6f;               // отваливается с конца
                rb.angularVelocity = transform.right * 3f;
            }
            int left = Left - 1;
            _left.Value = (byte)Mathf.Max(0, left);
            if (_log) Debug.Log(left > 0 ? $"[Труба] отпилено кольцо — осталось на {left}" : "[Труба] последнее кольцо — трубы больше нет", this);
            if (left <= 0) { ServerSetClamped(false); base.Despawn(); }
        }

#if UNITY_EDITOR
        public void EditorSetup(float ri, float ro, float h, int rings, GameObject ringPrefab, Material mat)
        {
            _innerRadius = ri; _outerRadius = ro; _ringHeight = h; _rings = rings; _ringPrefab = ringPrefab; _material = mat;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
