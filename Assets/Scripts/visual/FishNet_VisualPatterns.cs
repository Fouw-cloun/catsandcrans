using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Collections;
using UnityEngine;

// ============================================================================
//  Паттерн «сеть = состояние, визуал = локальная реакция» для FishNet 4.
//  Два примера: плита с синхронизированным огнём и «плюх» желе при постановке.
//
//  Если у тебя FishNet 3.x — синтаксис SyncVar другой:
//      [SyncVar(OnChange = nameof(OnCookingChanged))] private bool _isCooking;
//  Логика при этом идентична.
// ============================================================================

/// <summary>
/// Сетевая плита. Синхронизируется ТОЛЬКО бул "готовим/не готовим".
/// Огонь, эмиссия, мерцание, пар — каждый клиент включает у себя сам.
/// </summary>
public class NetworkedStove : NetworkBehaviour
{
    [Header("Локальный визуал (НЕ сетевые объекты)")]
    public LivingLight fireLight;          // наш скрипт мерцания
    public ParticleSystem steam;           // пар над конфоркой
    public Renderer burnerRenderer;        // конфорка с эмиссией

    private readonly SyncVar<bool> _isCooking = new SyncVar<bool>();
    private MaterialPropertyBlock _mpb;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        _isCooking.OnChange += OnCookingChanged;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // применяем текущее состояние для поздно подключившихся игроков
        ApplyVisuals(_isCooking.Value);
    }

    /// <summary>Вызывается из кода взаимодействия игрока (локально).</summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestSetCooking(bool on)
    {
        // здесь серверная валидация: есть ли сковорода, есть ли ингредиенты...
        _isCooking.Value = on; // FishNet сам разошлёт всем клиентам
    }

    // OnChange приходит и на сервере, и на клиентах
    private void OnCookingChanged(bool prev, bool next, bool asServer)
    {
        if (asServer && !base.IsClientInitialized) return; // на dedicated-сервере визуал не нужен
        ApplyVisuals(next);
    }

    /// <summary>Чисто локальная косметика — сеть сюда не заходит.</summary>
    private void ApplyVisuals(bool cooking)
    {
        if (fireLight != null) fireLight.gameObject.SetActive(cooking);
        if (steam != null)
        {
            if (cooking) steam.Play();
            else steam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        if (burnerRenderer != null)
        {
            burnerRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionId, cooking
                ? new Color(2.5f, 0.9f, 0.3f)   // HDR-оранжевый, чтобы сработал Bloom
                : Color.black);
            burnerRenderer.SetPropertyBlock(_mpb);
        }
    }
}

/// <summary>
/// Сетевое блюдо-желе. Когда сервер решил, что блюдо поставлено на стол,
/// он рассылает ObserversRpc — и у КАЖДОГО игрока локально проигрывается «плюх»
/// (дрожание _WobbleStrength из шейдера Toon Food).
/// </summary>
public class NetworkedJellyDish : NetworkBehaviour
{
    public Renderer jellyRenderer;
    public float plopPower = 0.06f;
    public float plopDecay = 3f;

    private static readonly int WobbleId = Shader.PropertyToID("_WobbleStrength");
    private Coroutine _plop;

    /// <summary>Вызывай на СЕРВЕРЕ, когда блюдо реально поставили.</summary>
    [Server]
    public void OnPlacedOnTable()
    {
        PlayPlopObserversRpc();
    }

    [ObserversRpc(BufferLast = false, RunLocally = true)] // RunLocally — хост тоже увидит
    private void PlayPlopObserversRpc()
    {
        if (jellyRenderer == null) return;
        if (_plop != null) StopCoroutine(_plop);
        _plop = StartCoroutine(PlopRoutine());
    }

    private IEnumerator PlopRoutine()
    {
        var mpb = new MaterialPropertyBlock();
        float s = plopPower;
        while (s > 0.001f)
        {
            s = Mathf.Lerp(s, 0f, 1f - Mathf.Exp(-plopDecay * Time.deltaTime));
            jellyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(WobbleId, s);
            jellyRenderer.SetPropertyBlock(mpb);
            yield return null;
        }
        jellyRenderer.SetPropertyBlock(null);
        _plop = null;
    }
}

/// <summary>
/// Включение камеры/DOF только у локального игрока.
/// Повесь на префаб игрока рядом с камерой.
/// </summary>
public class OwnerOnlyCamera : NetworkBehaviour
{
    public Camera playerCamera;
    public AutoFocusDOF autoFocus;       // наш скрипт автофокуса
    public AudioListener audioListener;

    public override void OnStartClient()
    {
        base.OnStartClient();
        bool owner = base.IsOwner;
        if (playerCamera != null) playerCamera.gameObject.SetActive(owner);
        if (autoFocus != null) autoFocus.enabled = owner;
        if (audioListener != null) audioListener.enabled = owner;
    }
}
