// ============================================================================
//  CatFurController v2 — оболочки меха + продвинутая "физика жизни":
//   * инерция от перемещения (spring-damper)
//   * реакция на повороты персонажа (мех заносит при развороте)
//   * API импульсов: прыжок / приземление / удар => мех вздрагивает
//   * микро-дрожь на шуме Перлина
//   * авто-пересборка при изменении числа оболочек в Play Mode
//
//  Повесить на объект со SkinnedMeshRenderer (или MeshRenderer) кота,
//  у которого стоит материал с шейдером "Toon/FurToonShell".
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class CatFurController : MonoBehaviour
{
    [Header("Оболочки меха")]
    [Range(2, 64)]
    [Tooltip("Сколько слоёв меха. 16 — старт, 24-32 — максимум красоты на ПК.")]
    public int shellCount = 20;

    [Tooltip("Отбрасывают ли оболочки тени (дорого, обычно не нужно).")]
    public bool shellsCastShadows = false;

    [Header("Источник движения")]
    [Tooltip("Кость, по которой считается движение (например, грудь/корень). " +
             "Пусто = сам transform. С костью мех реагирует и на анимацию тела.")]
    public Transform motionAnchor;

    [Header("Инерция от перемещения")]
    [Tooltip("Насколько мех отстаёт от движения персонажа.")]
    public float inertia = 0.7f;
    [Tooltip("Жёсткость пружины: выше = мех быстрее возвращается.")]
    public float stiffness = 55f;
    [Tooltip("Затухание: выше = меньше колебаний после остановки.")]
    public float damping = 7f;
    public float maxOffset = 1.5f;

    [Header("Инерция от поворотов")]
    [Tooltip("Насколько мех заносит при развороте персонажа.")]
    public float angularInertia = 0.35f;
    [Tooltip("Условный радиус тела для пересчёта вращения в занос (м).")]
    public float angularRadius = 0.35f;

    [Header("Микро-дрожь")]
    [Tooltip("Лёгкое живое шевеление кончиков даже в покое.")]
    public float jitterAmount = 0.06f;
    public float jitterSpeed = 8f;

    const string FurShaderName = "Toon/FurToonShell";

    static readonly int ShellIndexId = Shader.PropertyToID("_ShellIndex");
    static readonly int ShellCountId = Shader.PropertyToID("_ShellCount");
    static readonly int PhysOffsetId = Shader.PropertyToID("_PhysOffset");
    static readonly int FurMaskId    = Shader.PropertyToID("_FurMask");

    readonly List<Renderer> _shells = new List<Renderer>();
    readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();

    SkinnedMeshRenderer _srcSmr;
    MeshRenderer _srcMr;
    Material _invisibleMat;
    int _builtShellCount = -1;

    Vector3 _prevPos;
    Quaternion _prevRot;
    Vector3 _offset;
    Vector3 _offsetVel;

    Transform Anchor => motionAnchor != null ? motionAnchor : transform;

    void OnEnable()
    {
        _srcSmr = GetComponent<SkinnedMeshRenderer>();
        _srcMr  = GetComponent<MeshRenderer>();
        if (_srcSmr == null && _srcMr == null)
        {
            Debug.LogError("[CatFurController] Нужен SkinnedMeshRenderer или MeshRenderer на этом объекте.", this);
            enabled = false;
            return;
        }
        BuildShells();
        _prevPos = Anchor.position;
        _prevRot = Anchor.rotation;
        _offset = _offsetVel = Vector3.zero;
    }

    void OnDisable() => ClearShells();

    // ------------------------------------------------------------------
    //  Публичный API для геймплея:
    //  вызывай при прыжке, приземлении, ударе, рывке и т.п.
    //  Пример: fur.AddImpulse(Vector3.down * 3f)  — мех "подпрыгнул" вверх
    //          fur.AddImpulse(Vector3.up * 2.5f)  — мех прижало при приземлении
    // ------------------------------------------------------------------
    public void AddImpulse(Vector3 worldImpulse)
    {
        _offsetVel += worldImpulse;
    }

    [ContextMenu("Rebuild Shells")]
    public void BuildShells()
    {
        ClearShells();

        Renderer src = _srcSmr != null ? (Renderer)_srcSmr : _srcMr;
        Material[] shellMats = MakeShellMaterials(src.sharedMaterials);

        for (int i = 1; i < shellCount; i++)
        {
            var go = new GameObject("FurShell_" + i);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);

            Renderer r;
            if (_srcSmr != null)
            {
                var s = go.AddComponent<SkinnedMeshRenderer>();
                s.sharedMesh          = _srcSmr.sharedMesh;
                s.bones               = _srcSmr.bones;
                s.rootBone            = _srcSmr.rootBone;
                s.localBounds         = _srcSmr.localBounds;
                s.updateWhenOffscreen = _srcSmr.updateWhenOffscreen;
                s.quality             = _srcSmr.quality;
                s.sharedMaterials     = shellMats;
                r = s;
            }
            else
            {
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = GetComponent<MeshFilter>().sharedMesh;
                var m = go.AddComponent<MeshRenderer>();
                m.sharedMaterials = shellMats;
                r = m;
            }

            r.shadowCastingMode = shellsCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;

            var b = new MaterialPropertyBlock();
            b.SetFloat(ShellIndexId, i);
            b.SetFloat(ShellCountId, shellCount);
            r.SetPropertyBlock(b);

            _shells.Add(r);
            _blocks.Add(b);
        }

        var baseBlock = new MaterialPropertyBlock();
        src.GetPropertyBlock(baseBlock);
        baseBlock.SetFloat(ShellIndexId, 0);
        baseBlock.SetFloat(ShellCountId, shellCount);
        src.SetPropertyBlock(baseBlock);

        _builtShellCount = shellCount;
    }

    // На оболочках все НЕ-меховые слоты материалов (жилетка/каска отдельным
    // слотом) заменяются невидимым материалом, чтобы одежда не рисовалась N раз.
    Material[] MakeShellMaterials(Material[] source)
    {
        Material fur = null;
        foreach (var m in source)
            if (m != null && m.shader != null && m.shader.name == FurShaderName) { fur = m; break; }

        if (fur == null)
        {
            Debug.LogWarning("[CatFurController] Не найден материал с шейдером " + FurShaderName +
                             " — оболочки будут использовать материалы как есть.", this);
            return source;
        }

        var result = new Material[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null && source[i].shader != null &&
                source[i].shader.name == FurShaderName)
            {
                result[i] = source[i];
            }
            else
            {
                if (_invisibleMat == null)
                {
                    _invisibleMat = new Material(fur.shader) { name = "FurShell_Invisible" };
                    _invisibleMat.SetTexture(FurMaskId, Texture2D.blackTexture);
                    _invisibleMat.hideFlags = HideFlags.DontSave;
                }
                result[i] = _invisibleMat;
            }
        }
        return result;
    }

    void ClearShells()
    {
        foreach (var r in _shells)
            if (r != null) Destroy(r.gameObject);
        _shells.Clear();
        _blocks.Clear();
        _builtShellCount = -1;
    }

    void LateUpdate()
    {
        // Пересобрать, если число оболочек поменяли в инспекторе на лету
        if (_builtShellCount != shellCount) BuildShells();

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Transform a = Anchor;

        // --- Линейная скорость ---
        Vector3 vel = (a.position - _prevPos) / dt;
        _prevPos = a.position;

        // --- Угловая скорость => занос меха при развороте ---
        Quaternion dq = a.rotation * Quaternion.Inverse(_prevRot);
        _prevRot = a.rotation;
        dq.ToAngleAxis(out float angDeg, out Vector3 axis);
        if (angDeg > 180f) angDeg -= 360f;
        Vector3 angVel = axis * (angDeg * Mathf.Deg2Rad / dt);
        // Тангенциальная скорость точки на "поверхности тела" (приближение)
        Vector3 tangentialVel = Vector3.Cross(angVel, a.forward * angularRadius);

        Vector3 target = Vector3.ClampMagnitude(
            -vel * inertia - tangentialVel * angularInertia, maxOffset);

        // --- Пружина с затуханием ---
        _offsetVel += (target - _offset) * (stiffness * dt);
        _offsetVel *= Mathf.Exp(-damping * dt);
        _offset += _offsetVel * dt;
        _offset = Vector3.ClampMagnitude(_offset, maxOffset);

        // --- Микро-дрожь: чуть-чуть всегда, сильнее при движении ---
        float t = Time.time * jitterSpeed;
        float energy = Mathf.Clamp01(vel.magnitude * 0.5f + 0.25f);
        Vector3 jitter = new Vector3(
            Mathf.PerlinNoise(t, 0.37f) - 0.5f,
            Mathf.PerlinNoise(0.83f, t) - 0.5f,
            Mathf.PerlinNoise(t * 0.7f, t * 0.31f) - 0.5f
        ) * (2f * jitterAmount * energy);

        Vector4 phys = _offset + jitter;

        for (int i = 0; i < _shells.Count; i++)
        {
            if (_shells[i] == null) continue;
            _blocks[i].SetVector(PhysOffsetId, phys);
            _shells[i].SetPropertyBlock(_blocks[i]);
        }
    }
}
