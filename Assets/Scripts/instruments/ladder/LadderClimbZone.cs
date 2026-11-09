using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// ЗОНА ЛАЗАНИЯ. Вешается на ДОЧЕРНИЙ объект лестницы с BoxCollider (Is Trigger).
/// Размер коллайдера задаёт сам LadderItem — руками его трогать не надо.
///
/// ═══ ПОЧЕМУ СПИСОК, А НЕ ПРОСТО Enter/Exit ═══
///
/// Условия допуска МЕНЯЮТСЯ, пока игрок уже внутри: лестницу подняли с земли,
/// сложили, наклонили, отдали другому. Если решать один раз на входе, игрок
/// останется висеть на лестнице, которую напарник уже унёс. Поэтому триггер
/// только ведёт список тех, кто внутри, а решение пересчитывается каждый кадр.
///
/// Разбор дешёвый: внутри зоны почти всегда ноль или один игрок.
///
/// СЕТИ ЗДЕСЬ НЕТ ВООБЩЕ. Лазание — это движение собственного персонажа, а его
/// в проекте считает клиент-владелец (FirstPersonController owner-authoritative,
/// позиция расходится через NetworkTransform). Значит зона трогает ТОЛЬКО
/// своего игрока и молчит про чужих: у каждого клиента свой экземпляр этой же
/// зоны разберётся со своим.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class LadderClimbZone : MonoBehaviour
{
    [Tooltip("Лестница. Пусто = ищется в родителях (стремянка LadderItem или раскладная FoldingLadderItem).")]
    [FormerlySerializedAs("_ladder")]
    [SerializeField] private LadderItem _ladderRef;

    /// <summary>По чему лезем. Любая лестница через IClimbable.</summary>
    private IClimbable _ladder;

    [Tooltip("Отладочный лог: почему зацепился или не зацепился")]
    [SerializeField] private bool _debugLog;

    private readonly List<FirstPersonController> _inside = new List<FirstPersonController>();

    /// <summary>Свой игрок, который сейчас висит на этой лестнице. Хранится,
    /// чтобы отцепить именно его и не дёргать чужие контроллеры.</summary>
    private FirstPersonController _attached;

    private void Awake()
    {
        _ladder = _ladderRef != null ? _ladderRef : GetComponentInParent<IClimbable>();

        if (_ladder == null)
            Debug.LogError($"[Зона лазания] «{name}»: в родителях нет лестницы (LadderItem / FoldingLadderItem). " +
                           "Объект должен быть дочерним к лестнице.", this);

        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnDisable() => Detach();

    // ───────────────────────── СПИСОК ─────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        FirstPersonController fpc = other.GetComponentInParent<FirstPersonController>();

        // Чужими персонажами не управляем: их движение считает их же клиент.
        if (fpc == null || !fpc.IsOwner) return;
        if (_inside.Contains(fpc)) return;

        _inside.Add(fpc);
    }

    private void OnTriggerExit(Collider other)
    {
        FirstPersonController fpc = other.GetComponentInParent<FirstPersonController>();
        if (fpc == null) return;

        _inside.Remove(fpc);
        if (_attached == fpc) Detach();
    }

    // ───────────────────────── РЕШЕНИЕ ─────────────────────────

    private void Update()
    {
        if (_ladder == null) return;

        // Подчищаем мёртвых: игрок мог отключиться прямо в зоне, и тогда
        // OnTriggerExit уже не придёт.
        for (int i = _inside.Count - 1; i >= 0; i--)
            if (_inside[i] == null) _inside.RemoveAt(i);

        FirstPersonController candidate = null;
        for (int i = 0; i < _inside.Count; i++)
        {
            if (!_ladder.CanClimb(_inside[i].NetworkObject)) continue;
            candidate = _inside[i];
            break;
        }

        if (candidate == null) { Detach(); return; }

        // Уже висит — только обновляем ось: лестницу могли наклонить или
        // понести, и держаться игрок должен за её текущее положение.
        if (_attached == candidate)
        {
            candidate.UpdateClimb(_ladder.ClimbBase, _ladder.ClimbAxis, _ladder.CurrentLength);
            return;
        }

        Detach();
        _attached = candidate;
        _attached.BeginClimb(_ladder.ClimbBase, _ladder.ClimbAxis, _ladder.CurrentLength);
        _ladder.AnyoneClimbing = true;

        if (_debugLog)
            Debug.Log($"[Зона лазания] зацепился за «{((Component)_ladder).name}», " +
                      $"наклон {_ladder.AngleFromGround:0}°", this);
    }

    private void Detach()
    {
        if (_attached != null) _attached.EndClimb();
        _attached = null;

        if (_ladder != null) _ladder.AnyoneClimbing = false;
    }
}
