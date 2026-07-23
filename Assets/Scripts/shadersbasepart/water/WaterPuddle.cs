using UnityEngine;

/// <summary>
/// ЛУЖА на земле. Раскладываются WaterJet'ом как СТАБИЛЬНЫЕ ЯКОРЯ вдоль дуги.
///
/// ПЛАВНОСТЬ: у лужи есть ЦЕЛЕВОЙ радиус (SetTarget). Текущий радиус плавно
/// ползёт к нему (MoveTowards) — БЕЗ рывков. Струя достаёт → цель = размер лужи,
/// растёт; струя короче/выключена → цель = 0, плавно сохнет. Позиция фиксирована
/// (лужи не прыгают).
///
/// Задел под скользкость: мокрая зона (триггер) + IsSlippery/SlipRadius.
///
/// ПРЕФАБ: плоский Quad (доворот Spawn Euler), материал WaterPuddleURP, БЕЗ
/// обычного коллайдера. Триггер добавляется сам.
/// </summary>
public class WaterPuddle : MonoBehaviour
{
    [Header("Ориентация")]
    [Tooltip("Доворот при укладке. Плоский Quad обычно (90,0,0).")]
    [SerializeField] private Vector3 _spawnEulerOffset = new Vector3(90f, 0f, 0f);
    [Tooltip("Приподнять над поверхностью, чтобы не проваливалась")]
    [SerializeField] private float _surfaceLift = 0.03f;

    [Header("Плавный рост/высыхание")]
    [Tooltip("Скорость роста к целевому радиусу, м/сек")]
    [SerializeField] private float _growSpeed = 0.5f;
    [Tooltip("Скорость высыхания к нулю, м/сек (обычно медленнее роста)")]
    [SerializeField] private float _drySpeed = 0.25f;

    [Header("Скользкость (задел на будущее)")]
    [Tooltip("Во сколько раз мокрая зона больше видимой лужи")]
    [SerializeField] private float _wetMultiplier = 1.6f;
    [Range(0f, 1f)] [SerializeField] private float _slipperiness = 0.8f;

    private float _radius;        // текущий (плавно движется)
    private float _targetRadius;  // цель
    private SphereCollider _wetTrigger;
    private MeshRenderer _mr;
    private MaterialPropertyBlock _mpb;
    private static readonly int ShapeSeedId = Shader.PropertyToID("_ShapeSeed");

    public float CurrentRadius => _radius;
    public float SlipRadius => _radius * _wetMultiplier;
    public bool IsSlippery => gameObject.activeSelf && _radius > 0.1f;
    public float Slipperiness => _slipperiness;

    private void Awake()
    {
        _wetTrigger = GetComponent<SphereCollider>();
        if (_wetTrigger == null) _wetTrigger = gameObject.AddComponent<SphereCollider>();
        _wetTrigger.isTrigger = true;

        _mr = GetComponent<MeshRenderer>();
        _mpb = new MaterialPropertyBlock();
    }

    /// <summary>Поставить лужу в точку (один раз). Позиция дальше не меняется.</summary>
    public void PlaceAt(Vector3 position, Vector3 surfaceNormal, float sizeMul = 1f)
    {
        transform.position = position + surfaceNormal * _surfaceLift;
        transform.rotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal)
                             * Quaternion.Euler(_spawnEulerOffset);
        _radius = 0.01f;
        _targetRadius = 0f;
        gameObject.SetActive(true);

        // Уникальная форма края.
        if (_mr != null)
        {
            _mr.GetPropertyBlock(_mpb);
            _mpb.SetVector(ShapeSeedId, new Vector4(Random.value * 10f, Random.value * 10f, 0, 0));
            _mr.SetPropertyBlock(_mpb);
        }
        Apply();
    }

    /// <summary>Задать ЦЕЛЕВОЙ радиус. Лужа плавно поползёт к нему.</summary>
    public void SetTarget(float targetRadius)
    {
        _targetRadius = Mathf.Max(0f, targetRadius);
        if (_targetRadius > 0.001f && !gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    private void Update()
    {
        // Плавное движение к цели: рост и высыхание с разной скоростью.
        float speed = _targetRadius > _radius ? _growSpeed : _drySpeed;
        _radius = Mathf.MoveTowards(_radius, _targetRadius, speed * Time.deltaTime);

        if (_radius <= 0.02f && _targetRadius <= 0.001f)
        {
            gameObject.SetActive(false);
            return;
        }
        Apply();
    }

    private void Apply()
    {
        transform.localScale = new Vector3(_radius * 2f, _radius * 2f, 1f);

        if (_wetTrigger != null)
        {
            float local = _radius > 0.001f ? SlipRadius / (_radius * 2f) : 0.5f;
            _wetTrigger.radius = local;
            _wetTrigger.center = Vector3.zero;
        }
    }
}
