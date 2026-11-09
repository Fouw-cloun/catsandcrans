using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ШПАТЕЛЬ — делит подошедший шар теста на равные куски (по умолчанию 4).
    ///
    /// ПКМ (или Shift+ЛКМ) — маленький шарик-ПАМПУШКА (Bun Prefab, размер — Bun Scale). Возьми в руку и ПКМ по решётке
    /// духовки — ляжет ровно вплотную к соседке. Испекутся, подрастут, слипнутся кучкой, но не влезут друг в друга.
    /// В руке: смотришь на шар теста, ЛКМ — шпатель «режет» вниз, от шара отщипывается кусок
    /// (отлетает в сторону, куда смотришь), шар становится меньше. Последний остаток — сам кусок.
    /// Не подошло тесто — в консоли «ещё не подошло». Кусок дальше кладётся в форму для хлеба.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
    /// Piece Prefab — префаб теста (тот же, что у бетономешалки: FoodPiece + DoughBlob).
    /// </summary>
    public class DoughSpatulaItem : PickupItem
    {
        [Header("Куски")]
        [Tooltip("Префаб куска теста (тот же префаб шара: FoodPiece + DoughBlob)")]
        [SerializeField] private GameObject _piecePrefab;
        [Tooltip("ПКМ — маленький шарик: префаб ПАМПУШКИ (FoodPiece + DoughBlob, выпекается в «Пампушку»). Меню Борщ → 14, 15")]
        [SerializeField] private GameObject _bunPrefab;
        [Tooltip("Сколько порций теста в одной пампушке")]
        [Range(1, 16)][SerializeField] private int _bunPortions = 4;
        [Tooltip("Размер шарика-пампушки: доля «честного по объёму» размера (0.45 — маленькая, 1 — как кусок теста)")]
        [Range(0.1f, 1f)][SerializeField] private float _bunScale = 0.45f;

        [Header("Прицел")]
        [SerializeField] private float _range = 2.4f;
        [SerializeField] private LayerMask _foodMask = ~0;

        [Header("Движение")]
        [Tooltip("Замах, с")]
        [SerializeField] private float _windup = 0.15f;
        [Tooltip("Рез вниз, с")]
        [SerializeField] private float _cut = 0.12f;
        [Tooltip("Возврат, с")]
        [SerializeField] private float _recover = 0.2f;
        [SerializeField] private float _windupAngle = 35f;
        [SerializeField] private float _cutAngle = 40f;
        [SerializeField] private float _lunge = 0.2f;

        [Header("Вид")]
        [SerializeField] private Color _crumbColor = new Color(0.96f, 0.9f, 0.76f);

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private float _swingT = -1f;
        private bool _hitDone, _bun;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private float SwingLength => _windup + _cut + _recover;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
        }

        private void Update()
        {
            if (_swingT >= 0f)
            {
                _swingT += Time.deltaTime;
                if (base.IsOwner && !_hitDone && _swingT >= _windup + _cut)
                {
                    _hitDone = true;
                    Strike();
                }
                if (_swingT >= SwingLength) _swingT = -1f;
            }
            if (!base.IsOwner) return;
            if (_swingT < 0f && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
            {
                _bun = Input.GetMouseButtonDown(1) || Input.GetKey(KeyCode.LeftShift);   // ПКМ или Shift+ЛКМ — маленький шарик-пампушка
                StartSwing();
                CmdSwing();
            }
        }

        private void StartSwing() { _swingT = 0f; _hitDone = false; }

        private void Strike()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            // все слои: валец катка и кувалда могут быть не на слое еды
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue, bestAny = float.MaxValue;
            NetworkObject target = null;
            Vector3 point = Vector3.zero, anyPoint = ray.origin + ray.direction * _range;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform) || h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                if (h.distance < bestAny) { bestAny = h.distance; anyPoint = h.point; }
                if (((1 << h.collider.gameObject.layer) & _foodMask) == 0 || h.distance >= best) continue;
                DoughBlob d = h.collider.GetComponentInParent<DoughBlob>();
                if (d == null) continue;
                best = h.distance; target = d.NetworkObject; point = h.point;
            }
            if (target != null) { if (_bun) CmdCutBun(target, point, ray.direction); else CmdCut(target, point, ray.direction); }
            else CmdSpatulaAt(anyPoint, ray.direction);      // залипший валец, застрявшая кувалда — сервер найдёт, что рядом
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdSwing() => RpcSwing();

        /// <summary>Шпатель в точку: долбим ближайшее залипшее (валец катка, кувалда в тесте). Шпатели сами никогда не липнут.</summary>
        [ServerRpc(RequireOwnership = true)]
        private void CmdSpatulaAt(Vector3 point, Vector3 dir)
        {
            if ((point - transform.position).sqrMagnitude > (_range + 2f) * (_range + 2f)) return;
            ISpatulaTarget t = DoughStick.NearestSpatulable(point, 1.6f);
            if (t == null)
            {
                if (_log) Debug.Log($"[Шпатель] рядом нечего долбить (залипших вальцов/застрявших кувалд в {1.6f:0.0} м от удара нет)", this);
                RpcCrumbs(point, false);
                return;
            }
            bool ok = t.ServerSpatulaHit(point, dir, out string why);
            if (!ok && _log && !string.IsNullOrEmpty(why)) Debug.Log($"[Шпатель] {why}", this);
            RpcCrumbs(point, ok);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void RpcSwing() => StartSwing();

        [ServerRpc(RequireOwnership = true)]
        private void CmdCut(NetworkObject target, Vector3 point, Vector3 dir)
        {
            if (target == null) return;
            if ((point - transform.position).sqrMagnitude > (_range + 2f) * (_range + 2f)) return;
            DoughBlob d = target.GetComponent<DoughBlob>();
            if (d == null) return;
            Vector3 side = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.forward;
            // кусок отлетает вбок от линии взгляда (то вправо, то влево)
            Vector3 right = Vector3.Cross(Vector3.up, side.normalized);
            Vector3 throwDir = (Random.value < 0.5f ? right : -right) + side.normalized * 0.3f;
            bool ok = d.ServerCutPiece(_piecePrefab, throwDir, out string why);
            if (!ok && _log) Debug.Log($"[Шпатель] «{target.name}»: {why}", target);
            RpcCrumbs(point, ok);
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdCutBun(NetworkObject target, Vector3 point, Vector3 dir)
        {
            if (target == null) return;
            if ((point - transform.position).sqrMagnitude > (_range + 2f) * (_range + 2f)) return;
            DoughBlob d = target.GetComponent<DoughBlob>();
            if (d == null) return;
            Vector3 side = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, side.normalized);
            Vector3 throwDir = (Random.value < 0.5f ? right : -right) + side.normalized * 0.3f;
            bool ok = d.ServerCutBun(_bunPrefab, throwDir, _bunPortions, _bunScale, out string why);
            if (!ok && _log) Debug.Log($"[Шпатель] «{target.name}»: {why}", target);
            RpcCrumbs(point, ok);
        }

        [ObserversRpc]
        private void RpcCrumbs(Vector3 at, bool cut)
        {
            CCRuntimeParticles.Burst(at, Vector3.up, _crumbColor, _crumbColor * 0.9f, cut ? 10 : 4,
                                     CCRuntimeParticles.Look.Dust.Scaled(cut ? 1.2f : 0.7f));
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            base.UpdateHeldPose(handAnchor);
            if (_swingT < 0f) return;
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            float pitch, lunge;
            if (_swingT < _windup)
            {
                float k = Mathf.SmoothStep(0f, 1f, _swingT / _windup);
                pitch = -_windupAngle * k; lunge = 0f;
            }
            else if (_swingT < _windup + _cut)
            {
                float k = (_swingT - _windup) / _cut;
                k *= k;
                pitch = Mathf.Lerp(-_windupAngle, _cutAngle, k); lunge = Mathf.Lerp(0f, _lunge, k);
            }
            else
            {
                float k = Mathf.SmoothStep(0f, 1f, (_swingT - _windup - _cut) / _recover);
                pitch = Mathf.Lerp(_cutAngle, 0f, k); lunge = Mathf.Lerp(_lunge, 0f, k);
            }
            rot *= Quaternion.Euler(pitch, 0f, 0f);
            pos += handAnchor.forward * lunge;
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
