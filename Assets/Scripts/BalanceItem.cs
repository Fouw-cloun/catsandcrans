using FishNet.Object;
using UnityEngine;

/// <summary>
/// Балансный предмет (кастрюля с супом). Несётся двумя «руками» — пока держишь,
/// предмет качается от твоего движения и поворотов, и его нужно выравнивать
/// мышью (ЛКМ/ПКМ или движение мыши). Если наклон превысит предел — проливается.
///
/// Особенности:
///   - FollowHand = false: позу в руке считаем сами (с наклоном).
///   - CarrySpeedMultiplier < 1: с кастрюлей игрок идёт медленнее.
///   - Наклон копится на клиенте-владельце (он двигает предмет), перебор
///     → ServerRpc на пролитие.
///
/// Значения наклона/восстановления — под твою настройку, помечены TUNE.
/// </summary>
public class BalanceItem : InteractableItem
{
    [Header("Баланс (TUNE)")]
    [SerializeField] private float _maxTilt = 35f;            // предел наклона (градусы)
    [SerializeField] private float _swayFromMovement = 6f;    // как сильно качает от скорости
    [SerializeField] private float _recoverSpeed = 20f;       // авто-восстановление к ровному
    [SerializeField] private float _counterSpeed = 60f;       // насколько мышь выравнивает
    [SerializeField] private float _carrySpeed = 0.5f;        // штраф скорости игрока

    public override bool FollowHand => false;
    public override float CarrySpeedMultiplier => _carrySpeed;

    // Текущий наклон по двум осям (локально у держателя).
    private Vector2 _tilt;
    private Vector3 _lastAnchorPos;
    private bool _hasLast;
    private bool _spilled;

    // ───────── ПОЗА В РУКЕ (клиент-владелец) ─────────

    public override void UpdateHeldPose(Transform handAnchor)
    {
        // Скорость руки (= движение игрока) раскачивает кастрюлю.
        if (_hasLast)
        {
            Vector3 delta = handAnchor.position - _lastAnchorPos;
            _tilt.x += delta.z * _swayFromMovement; // вперёд/назад
            _tilt.y += delta.x * _swayFromMovement; // влево/вправо
        }
        _lastAnchorPos = handAnchor.position;
        _hasLast = true;

        // Игрок выравнивает мышью (движение мыши гасит наклон).
        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y");
        _tilt.x = Mathf.MoveTowards(_tilt.x, 0f, (_recoverSpeed + Mathf.Abs(my) * _counterSpeed) * Time.deltaTime);
        _tilt.y = Mathf.MoveTowards(_tilt.y, 0f, (_recoverSpeed + Mathf.Abs(mx) * _counterSpeed) * Time.deltaTime);

        // Применяем позу: позиция кисти + наклон.
        Quaternion tiltRot = Quaternion.Euler(_tilt.x, 0f, -_tilt.y);
        transform.SetPositionAndRotation(handAnchor.position, handAnchor.rotation * tiltRot);

        // Перебор наклона → пролитие (один раз).
        float magnitude = _tilt.magnitude;
        if (!_spilled && magnitude > _maxTilt)
        {
            _spilled = true;
            CmdSpill();
        }
    }

    // ───────── ПРОЛИТИЕ ─────────

    [ServerRpc]
    private void CmdSpill()
    {
        // Сервер сообщает всем об эффекте пролития.
        RpcSpillEffect();
        // Здесь позже: обнулить содержимое/рецепт, начислить штраф и т.п.
    }

    [ObserversRpc]
    private void RpcSpillEffect()
    {
        Debug.Log("[BalanceItem] Пролилось! (здесь particle/звук/потеря содержимого)");
        // TODO: проиграть particle-эффект разлива, звук.
    }

    // Сброс состояния при взятии/броске.
    public override void ServerOnPickup(PlayerHand hand)
    {
        base.ServerOnPickup(hand);
        ResetBalance();
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        base.ServerOnDrop(hand, tossDirection, force);
        ResetBalance();
    }

    private void ResetBalance()
    {
        _tilt = Vector2.zero;
        _hasLast = false;
        _spilled = false;
    }
}
