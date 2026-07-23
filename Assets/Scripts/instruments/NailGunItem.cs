using FishNet.Object;
using UnityEngine;

/// <summary>
/// Гвоздепистолет. Инструмент в руке.
///
///   ЛКМ — выстрел гвоздём (сервер спавнит гвоздь-префаб и запускает вперёд).
///   G   — бросить (база).
///
/// ДЖЕМ (кривой выстрел):
///   С шансом _jamChance вместо нормального выстрела вылетает «погнутый» гвоздь —
///   максимально криво (большой разброс) и слабо (малая сила). Можно задать
///   отдельный префаб погнутого гвоздя; если не задан — берётся обычный.
///
/// Гвозди живут _nailLifetime секунд (по умолчанию 15) и деспавнятся.
///
/// СЕТЬ: стреляет и решает джем СЕРВЕР (авторитетный рандом). Владелец шлёт
/// CmdFire с позицией/направлением дула; сервер валидирует темп и спавнит.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody. Плюс гвоздь-префаб с NailProjectile в Spawnable Prefabs.
/// </summary>
public class NailGunItem : InteractableItem
{
    [Header("Ствол и гвозди")]
    [Tooltip("Точка вылета гвоздя (пустышка на дуле, forward = направление)")]
    [SerializeField] private Transform _muzzle;
    [Tooltip("Префаб гвоздя (NetworkObject + Rigidbody + NailProjectile)")]
    [SerializeField] private GameObject _nailPrefab;
    [Tooltip("Префаб ПОГНУТОГО гвоздя для джема (необязательно; пусто = обычный)")]
    [SerializeField] private GameObject _bentNailPrefab;
    [Tooltip("Сколько секунд живёт гвоздь до исчезновения")]
    [SerializeField] private float _nailLifetime = 15f;
    [Tooltip("Доворот МОДЕЛИ гвоздя при спавне, если он летит не той стороной")]
    [SerializeField] private Vector3 _nailSpawnEuler = Vector3.zero;
    [Tooltip("Сдвиг точки спавна вперёд от дула (чтобы гвоздь не застрял в стволе/игроке)")]
    [SerializeField] private float _spawnForwardOffset = 0.15f;

    [Header("Темп стрельбы")]
    [Tooltip("ВКЛ — авто-огонь, пока ЛКМ зажата. ВЫКЛ — одиночный на клик.")]
    [SerializeField] private bool _autoFire = true;
    [Tooltip("Выстрелов в секунду")]
    [SerializeField] private float _fireRate = 4f;

    [Header("Нормальный выстрел")]
    [Tooltip("Начальная скорость гвоздя")]
    [SerializeField] private float _nailSpeed = 25f;
    [Tooltip("Случайный разброс скорости (±)")]
    [SerializeField] private float _nailSpeedRandom = 2f;

    [Header("Джем (кривой выстрел)")]
    [Tooltip("Шанс кривого выстрела 0..1")]
    [Range(0f, 1f)]
    [SerializeField] private float _jamChance = 0.15f;
    [Tooltip("Скорость погнутого гвоздя (слабый вылет)")]
    [SerializeField] private float _jamSpeed = 3f;
    [Tooltip("Случайное кувыркание погнутого гвоздя")]
    [SerializeField] private float _jamTumble = 15f;

    [Header("Положение в руке (калибровка под модель)")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    // ───────── ЛОКАЛЬНОЕ / СЕРВЕР ─────────
    private float _lastFireTime;    // владелец: темп
    private float _serverLastFire;  // сервер: анти-спам

    private float FireInterval => _fireRate > 0.01f ? 1f / _fireRate : 0.25f;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
    }

    // ───────── ВВОД (владелец) ─────────

    private void Update()
    {
        if (!base.IsOwner) return;

        bool wantFire = _autoFire ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        if (!wantFire) return;
        if (Time.time - _lastFireTime < FireInterval) return;

        _lastFireTime = Time.time;
        Transform m = _muzzle != null ? _muzzle : transform;
        CmdFire(m.position, m.forward);
    }

    // ───────── СЕРВЕР: ВЫСТРЕЛ ─────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdFire(Vector3 origin, Vector3 forward)
    {
        // Анти-спам на сервере (с запасом на джиттер темпа).
        if (Time.time - _serverLastFire < FireInterval * 0.6f) return;
        _serverLastFire = Time.time;

        if (_nailPrefab == null) return;

        bool jam = Random.value < _jamChance;
        GameObject prefab = (jam && _bentNailPrefab != null) ? _bentNailPrefab : _nailPrefab;

        Vector3 dir = forward.normalized;
        float speed;
        Vector3 torque = Vector3.zero;

        if (jam)
        {
            // «Сломался»: вылетает В БОК от ствола и слабо, кувыркаясь. Дальше
            // летит по своей траектории (гравитация уводит вниз), просто вяло.
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            float sign = Random.value < 0.5f ? -1f : 1f;
            dir = (side * (sign * Random.Range(0.6f, 1f))   // в основном вбок
                 + dir * Random.Range(0f, 0.35f)             // чуть вперёд
                 + Vector3.up * Random.Range(-0.15f, 0.3f)   // немного вверх/вниз
                 ).normalized;
            speed = _jamSpeed;                               // слабая сила
            torque = Random.insideUnitSphere * _jamTumble;   // кувыркается
        }
        else
        {
            speed = _nailSpeed + Random.Range(-_nailSpeedRandom, _nailSpeedRandom);
        }

        Quaternion look = dir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(dir, Vector3.up)
            : Quaternion.identity;
        // Доворот под модель гвоздя (если «остриё» смотрит не по forward).
        Quaternion rot = look * Quaternion.Euler(_nailSpawnEuler);
        // Спавним чуть впереди дула, чтобы гвоздь не застрял в стволе/игроке.
        Vector3 spawnPos = origin + dir * _spawnForwardOffset;

        GameObject go = Instantiate(prefab, spawnPos, rot);
        // ВАЖНО: префаб гвоздя должен быть в Spawnable Prefabs у NetworkManager.
        base.ServerManager.Spawn(go, null);

        NailProjectile proj = go.GetComponent<NailProjectile>();
        if (proj != null)
        {
            proj.ServerLaunch(dir * speed, torque, _nailLifetime);
        }
        else
        {
            // Фолбэк без NailProjectile: всё равно запускаем — принудительно
            // делаем динамическим и задаём скорость. Но NailProjectile нужен для
            // деспавна и корректной сети — добавь его на префаб гвоздя.
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.linearVelocity = dir * speed;
                rb.angularVelocity = torque;
            }
        }
    }

    // ───────── ПОЗА В РУКЕ ─────────

    public override void UpdateHeldPose(Transform handAnchor)
    {
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }
}
