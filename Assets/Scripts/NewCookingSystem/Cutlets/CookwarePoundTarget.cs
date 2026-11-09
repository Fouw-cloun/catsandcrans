using CatCooks.Food.Tools;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТАРУ МОЖНО БИТЬ КУВАЛДОЙ — «отбить фарш о миску». Вешается на ведро/банку (меню Котлеты → 2).
    /// Удар кувалдой по таре: если в таре рецепт с шагом «Отбить» (фарш для котлет) — шаг идёт (1 удар = Work Per Hit секунд),
    /// содержимое вздрагивает. Нет такого рецепта — просто вздрагивает. Сервер (кувалда зовёт на сервере).
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class CookwarePoundTarget : MonoBehaviour, IHammerTarget
    {
        [Tooltip("Секунд шага «Отбить» за один удар (у фарша 8 с — 8 ударов)")]
        [Min(0.1f)][SerializeField] private float _workPerHit = 1f;
        [SerializeField] private bool _log = true;

        private Cookware _cw;
        private void Awake() => _cw = GetComponent<Cookware>();

        public void ServerHammerHit(Vector3 point, Vector3 dir)
        {
            if (_cw == null) return;
            _cw.ServerAgitate(0.8f);
            if (!CookRecipeBook.Wants(_cw, CookStepKind.Pound))
            {
                if (_log) Debug.Log($"[Кувалда] по «{name}» — нечего отбивать (нужен «Фарш для котлет»)", this);
                return;
            }
            bool step = CookRecipeBook.ServerProcess(_cw, CookStepKind.Pound, _workPerHit);
            if (_log && !step) Debug.Log($"[Кувалда] по «{name}»: не по рецепту — смотри F4", this);
        }
    }
}
