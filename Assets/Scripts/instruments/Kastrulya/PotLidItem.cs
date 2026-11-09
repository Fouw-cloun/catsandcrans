using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// КРЫШКА КАК ПРЕДМЕТ. Один и тот же объект и на кастрюле, и на полу —
/// он не пересоздаётся, а только прицепляется и отцепляется.
///
///   F   — взять / положить (база)
///   ЛКМ — накрыть кастрюлю, на которую смотришь
///
/// ПОКА ПРИЦЕПЛЕНА: физика выключена, позиция ведётся к гнезду ЛОКАЛЬНО у
/// каждого клиента. По сети идёт только ссылка на гнездо — позиция крышки
/// не гоняется вовсе, и при этом она всегда точно на месте, даже если
/// кастрюлю несут четверо и она качается.
/// </summary>
public class PotLidItem : InteractableItem
{
    [Header("Возврат на посуду")]
    [Tooltip("Дальность, с которой можно накрыть кастрюлю")]
    [SerializeField] private float _reach = 2.5f;

    [SerializeField] private LayerMask _potMask = ~0;

    [Header("Посадка на кастрюлю")]
    [Tooltip("Сам найти, какая ось модели — «верх» крышки (самая тонкая ось, обод снизу), " +
             "и положить крышку плашмя ручкой вверх. Модели из Blender часто лежат «на боку» (Z-up).")]
    [SerializeField] private bool _autoUpright = true;

    [Tooltip("Дополнительный поворот на кастрюле, градусы. Крышка вверх ногами — X = 180. " +
             "Повёрнута ручкой не туда — крути Y.")]
    [SerializeField] private Vector3 _seatEuler = Vector3.zero;

    [Tooltip("Сам сдвинуть крышку так, чтобы её ОБОД лёг ровно в точку гнезда (центр по горизонтали тоже).")]
    [SerializeField] private bool _autoSeat = true;

    [Tooltip("Приподнять крышку над точкой гнезда, метры (минус — опустить)")]
    [SerializeField] private float _seatLift = 0f;

    [SerializeField] private bool _logSeat = true;

    /// <summary>Гнездо, к которому прицеплены. Null — лежим сами по себе.</summary>
    private readonly SyncVar<NetworkObject> _socketOwner = new SyncVar<NetworkObject>();

    private PotLidSocket _socket;
    private bool _wasKinematic;
    private bool _physicsParked;

    /// <summary>С кем сейчас отключены столкновения — чтобы потом вернуть.</summary>
    private readonly List<Collider> _ignored = new List<Collider>();
    private Collider[] _own;

    public bool IsAttached => _socketOwner.Value != null;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        _own = GetComponentsInChildren<Collider>(true);
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _socketOwner.OnChange += OnSocketChanged;
        Refresh(_socketOwner.Value);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _socketOwner.OnChange -= OnSocketChanged;
    }

    private void OnSocketChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        // У ХОСТА OnChange приходит дважды: asServer = true, потом false.
        if (asServer && base.IsClientInitialized) return;
        Refresh(next);
    }

    /// <summary>Локальная реакция на прицепку: физику глушим у ВСЕХ, иначе
    /// у одних крышка стоит, а у других продолжает падать.</summary>
    private void Refresh(NetworkObject owner)
    {
        _socket = owner != null ? owner.GetComponent<PotLidSocket>() : null;

        if (_socket != null)
        {
            if (!_physicsParked)
            {
                _physicsParked = true;
                if (_rb != null) { _wasKinematic = _rb.isKinematic; _rb.isKinematic = true; }
            }

            // КРИТИЧНО. Крышка садится ВНУТРЬ коллайдера кастрюли, и физика
            // расталкивает пересекающиеся тела тем сильнее, чем глубже они
            // вошли друг в друга. Кинематическую крышку сдвинуть нельзя,
            // поэтому всю силу получает кастрюля — и улетает по диагонали,
            // разгоняясь без предела.
            //
            // Столкновения между ними отключаем на время прицепки. Коллайдер
            // самой крышки при этом остаётся включённым: её надо видеть лучом,
            // чтобы снять, и она должна мешать струе воды.
            IgnoreWith(_socket.GetComponentsInChildren<Collider>(true), true);
        }
        else if (_physicsParked)
        {
            _physicsParked = false;
            RestoreCollisions();
            if (_rb != null) { _rb.isKinematic = _wasKinematic; _rb.WakeUp(); }
        }
    }

    private void IgnoreWith(Collider[] others, bool ignore)
    {
        if (_own == null || others == null) return;

        foreach (Collider a in _own)
        {
            if (a == null) continue;
            foreach (Collider b in others)
            {
                if (b == null || b == a) continue;
                Physics.IgnoreCollision(a, b, ignore);
                if (ignore && !_ignored.Contains(b)) _ignored.Add(b);
            }
        }
    }

    /// <summary>Вернуть столкновения при отцепке — иначе снятая крышка
    /// проваливалась бы сквозь кастрюлю.</summary>
    private void RestoreCollisions()
    {
        if (_ignored.Count == 0 || _own == null) return;

        foreach (Collider a in _own)
        {
            if (a == null) continue;
            foreach (Collider b in _ignored)
            {
                if (b == null) continue;
                Physics.IgnoreCollision(a, b, false);
            }
        }
        _ignored.Clear();
    }

    private void OnDestroy() => RestoreCollisions();

    /// <summary>
    /// Ведём крышку к гнезду каждый кадр. LateUpdate — чтобы кастрюлю уже
    /// успели передвинуть носильщики, иначе крышка отставала бы на кадр
    /// и заметно дрожала при переносе.
    /// </summary>
    private void LateUpdate()
    {
        if (_socket == null) return;

        _socket.GetLidPose(this, out Vector3 pos, out Quaternion rot);
        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────────────────────── ПОСАДКА (ось модели) ─────────────────────────

    private bool _seatReady;
    private Quaternion _seatAlign = Quaternion.identity;   // «верх» модели → вверх гнезда
    private Vector3 _seatRim;                                // центр обода в локальных осях (с масштабом)

    /// <summary>
    /// Достроить позу гнезда до позы крышки: повернуть модель плашмя и опустить
    /// ободом в точку гнезда. Зовут PotLidSocket и сама крышка.
    /// </summary>
    public void ApplySeat(ref Vector3 pos, ref Quaternion rot)
    {
        if (!_seatReady) ComputeSeat();
        Quaternion full = rot * Quaternion.Euler(_seatEuler) * (_autoUpright ? _seatAlign : Quaternion.identity);
        Vector3 up = rot * Vector3.up;
        if (_autoSeat) pos -= full * _seatRim;
        pos += up * _seatLift;
        rot = full;
    }

    /// <summary>Перечитать форму (после смены модели в Play).</summary>
    [ContextMenu("Пересчитать посадку крышки")]
    public void ResetSeat() { _seatReady = false; ComputeSeat(); }

    private void ComputeSeat() => ComputeSeat(_logSeat);

    private void ComputeSeat(bool log)
    {
        _seatReady = true;
        _seatAlign = Quaternion.identity;
        _seatRim = Vector3.zero;

        Matrix4x4 toRoot = transform.worldToLocalMatrix;
        Vector3 ls = transform.lossyScale;
        List<Vector3> pts = new List<Vector3>(2048);

        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh m = mf.sharedMesh;
            if (m == null) continue;
            Renderer r = mf.GetComponent<Renderer>();
            if (r == null || r is ParticleSystemRenderer) continue;
            Matrix4x4 mtx = toRoot * mf.transform.localToWorldMatrix;
            if (m.isReadable)
            {
                Vector3[] v = m.vertices;
                int step = Mathf.Max(1, v.Length / 4000);
                for (int i = 0; i < v.Length; i += step) pts.Add(Vector3.Scale(ls, mtx.MultiplyPoint3x4(v[i])));
            }
            else
            {
                Bounds b = m.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    pts.Add(Vector3.Scale(ls, mtx.MultiplyPoint3x4(c)));
                }
            }
        }
        foreach (SkinnedMeshRenderer sk in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Bounds b = sk.bounds;   // мировые — переводим в корень
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                pts.Add(Vector3.Scale(ls, toRoot.MultiplyPoint3x4(c)));
            }
        }
        if (pts.Count < 3)
        {
            if (log) Debug.LogWarning($"[Крышка] «{name}»: нет мешей — ось не определить, ставлю как есть", this);
            return;
        }

        Vector3 mn = pts[0], mx = pts[0];
        foreach (Vector3 p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
        Vector3 size = mx - mn, ctr = (mn + mx) * 0.5f;

        // Самая тонкая ось — «толщина» крышки.
        int a = 0;
        if (size.y < size[a]) a = 1;
        if (size.z < size[a]) a = 2;
        int b1 = (a + 1) % 3, b2 = (a + 2) % 3;

        // Обод — самые дальние от центра точки. Где обод, там «низ».
        float maxR = 0f;
        foreach (Vector3 p in pts)
        {
            float r = new Vector2(p[b1] - ctr[b1], p[b2] - ctr[b2]).magnitude;
            if (r > maxR) maxR = r;
        }
        float sumA = 0f; Vector2 sumB = Vector2.zero; int n = 0;
        foreach (Vector3 p in pts)
        {
            float r = new Vector2(p[b1] - ctr[b1], p[b2] - ctr[b2]).magnitude;
            if (r < maxR * 0.85f) continue;
            sumA += p[a]; sumB += new Vector2(p[b1], p[b2]); n++;
        }
        float rimA = n > 0 ? sumA / n : mn[a];
        float sign = rimA <= ctr[a] ? 1f : -1f;   // обод ниже центра по +оси → верх = +ось
        // Совсем плоская крышка — обод посередине, знак не ясен: берём меньший поворот.
        bool flat = Mathf.Abs(rimA - ctr[a]) < size[a] * 0.08f;

        Vector3 upLocal = Vector3.zero; upLocal[a] = 1f;
        if (flat) sign = 1f;
        upLocal *= sign;

        _seatAlign = Quaternion.FromToRotation(upLocal, Vector3.up);

        Vector3 rim = ctr;
        rim[a] = rimA;
        if (n > 0) { rim[b1] = sumB.x / n; rim[b2] = sumB.y / n; }
        _seatRim = rim;

        if (log)
        {
            string axis = (sign > 0 ? "+" : "−") + "XYZ"[a];
            Debug.Log($"[Крышка] «{name}»: верх модели = локальная {axis}{(flat ? " (плоская, знак наугад — X=180 в Seat Euler перевернёт)" : "")}, " +
                      $"размер {size.x:0.##}×{size.y:0.##}×{size.z:0.##} м, обод {rim}", this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) { _seatReady = false; ComputeSeat(false); }
        if (!_seatReady) return;
        Vector3 up = transform.rotation * (Quaternion.Inverse(_seatAlign) * Vector3.up);
        Vector3 rimW = transform.position + transform.rotation * _seatRim;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(rimW, rimW + up * 0.3f);
        Gizmos.DrawWireSphere(rimW, 0.03f);
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    [Server]
    public void ServerAttachTo(PotLidSocket socket)
    {
        if (socket == null) return;
        _socketOwner.Value = socket.NetworkObject;

        // Владение снимаем: прицепленная крышка ничья, её ведёт гнездо.
        NetworkObject.RemoveOwnership();
    }

    [Server]
    public void ServerDetach()
    {
        _socketOwner.Value = null;

        // Физику возвращаем ЗДЕСЬ, а не ждём OnChange.
        //
        // У хоста первый OnChange приходит с asServer = true и в Refresh
        // пропускается, а второй прилетает только в конце сетевого тика.
        // К моменту, когда гнездо применяет импульс, крышка была бы всё ещё
        // кинематической — и просто не улетала бы.
        Refresh(null);
    }

    /// <summary>
    /// ЛКМ — накрыть кастрюлю, на которую смотришь.
    ///
    /// Именно ЛКМ, а не ПКМ: ПКМ у посуды занято другими действиями, и когда
    /// в руке крышка, а под прицелом кастрюля, эти намерения путались.
    /// </summary>
    public override void ServerPrimaryUse(UseContext ctx)
    {
        if (IsAttached) return;

        PotLidSocket socket = FindTargetSocket(ctx);
        if (socket == null)
        {
            Debug.Log("[Крышка] не вижу кастрюлю под прицелом (подойди ближе / Pot Mask)", this);
            return;
        }

        if (socket.HasLid) { Debug.Log("[Крышка] кастрюля уже накрыта", this); return; }

        // Из руки крышка уходит: держать её и одновременно накрывать нельзя.
        if (ctx.Hand != null && ctx.Hand.HeldItem == this)
            ctx.Hand.ServerDrop(Vector3.zero, 0f);

        socket.ServerAttach(this);
    }

    /// <summary>
    /// Кастрюля, которую накрываем: сначала по лучу (сквозь себя и мелочь), потом —
    /// ближайшее гнездо у линии взгляда. Большая кастрюля дальше 2.5 м — тоже достаём.
    /// </summary>
    private PotLidSocket FindTargetSocket(UseContext ctx)
    {
        float reach = Mathf.Max(_reach, 12f);
        RaycastHit[] hits = Physics.RaycastAll(ctx.Origin, ctx.Direction, reach, _potMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in hits)
        {
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            PotLidSocket s = h.collider.GetComponentInParent<PotLidSocket>();
            if (s != null) return s;
        }

        // Луч мимо — ближайшее гнездо у линии взгляда.
        PotLidSocket best = null; float bd = float.MaxValue;
        foreach (PotLidSocket s in FindObjectsByType<PotLidSocket>(FindObjectsSortMode.None))
        {
            Vector3 v = s.Socket.position - ctx.Origin;
            float along = Vector3.Dot(v, ctx.Direction);
            if (along < 0f || along > reach) continue;
            float off = (v - ctx.Direction * along).magnitude;
            if (off > Mathf.Max(1.5f, along * 0.35f)) continue;
            if (off < bd) { bd = off; best = s; }
        }
        return best;
    }

    /// <summary>Подобрали руками — значит отцепилась от кастрюли.</summary>
    public override void ServerOnPickup(PlayerHand hand)
    {
        if (IsAttached)
        {
            PotLidSocket s = _socketOwner.Value != null
                ? _socketOwner.Value.GetComponent<PotLidSocket>() : null;
            if (s != null) s.ServerDetach();
        }
        base.ServerOnPickup(hand);
    }
}
