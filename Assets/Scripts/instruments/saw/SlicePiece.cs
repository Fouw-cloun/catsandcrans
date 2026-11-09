using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ОТРЕЗАННЫЙ КУСОК. Вешается на префаб кусочка рядом с FoodItem.
///
/// Куски у продукта разной формы, но заводить четыре отдельных префаба не
/// нужно: префаб один, а нужный меш выбирается по индексу. Индекс ставит
/// SliceableFood перед спавном, дальше он синхронизируется одним байтом.
///
/// Если массив мешей пуст, компонент ничего не делает — останется тот меш,
/// что лежит в префабе.
/// </summary>
public class SlicePiece : NetworkBehaviour
{
    [Tooltip("Куда ставить меш. ПУСТО = найдётся сам.")]
    [SerializeField] private MeshFilter _filter;

    [Tooltip("Меши кусков В ТОМ ЖЕ ПОРЯДКЕ, что и на целом продукте: " +
             "нулевой — с того конца, с которого начинается нарезка.")]
    [SerializeField] private Mesh[] _meshes;

    [Tooltip("Подгонять размер коллайдера под меш. Работает только для " +
             "BoxCollider — у кусков он и должен быть простым, а не MeshCollider.")]
    [SerializeField] private BoxCollider _box;

    private readonly SyncVar<byte> _index = new SyncVar<byte>(0);

#if UNITY_EDITOR
    /// <summary>Только для кнопки на SliceableFood — она заполняет меши
    /// в правильном порядке, чтобы его не приходилось выставлять руками.</summary>
    public void EditorSetMeshes(Mesh[] meshes)
    {
        _meshes = meshes;
        if (_filter == null) _filter = GetComponentInChildren<MeshFilter>();
        if (_box == null) _box = GetComponentInChildren<BoxCollider>();
    }
#endif

    private void Awake()
    {
        if (_filter == null) _filter = GetComponentInChildren<MeshFilter>();
        if (_box == null) _box = GetComponentInChildren<BoxCollider>();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _index.OnChange += OnIndexChanged;
        Apply(_index.Value);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _index.OnChange -= OnIndexChanged;
    }

    /// <summary>Ставится ДО Spawn — тогда все клиенты получат готовое значение
    /// вместе с самим объектом.</summary>
    [Server]
    public void ServerSetIndex(byte index) => _index.Value = index;

    private void OnIndexChanged(byte prev, byte next, bool asServer)
    {
        // У ХОСТА OnChange приходит дважды: asServer = true, потом false.
        if (asServer && base.IsClientInitialized) return;
        Apply(next);
    }

    private void Apply(byte index)
    {
        if (_meshes == null || _meshes.Length == 0 || _filter == null) return;

        Mesh m = _meshes[Mathf.Clamp(index, 0, _meshes.Length - 1)];
        if (m == null) return;

        _filter.sharedMesh = m;

        // Коллайдер под размер меша: у кусков он простой, и без подгонки
        // маленький кусочек имел бы коробку от самого крупного.
        if (_box != null)
        {
            _box.center = m.bounds.center;
            _box.size = m.bounds.size;
        }
    }
}
