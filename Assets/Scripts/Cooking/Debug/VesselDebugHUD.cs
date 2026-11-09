using UnityEngine;

/// <summary>
/// ВРЕМЕННЫЙ отладочный экран. Вешается на ту же ёмкость, что и CookingVessel.
/// Показывает то, что видит КЛИЕНТ (SyncList слепков) — то есть проверяет
/// заодно и сетевую часть: если у клиента пусто, а у хоста нет, значит
/// синхронизация не доехала.
///
/// В релиз это не идёт. Заменится весами на модели ёмкости.
/// </summary>
[RequireComponent(typeof(CookingVessel))]
public class VesselDebugHUD : MonoBehaviour
{
    [SerializeField] private bool _show = true;
    [SerializeField] private float _maxDistance = 8f;
    [SerializeField] private Vector2 _screenOffset = new Vector2(0f, -40f);

    private CookingVessel _vessel;
    private GUIStyle _style;

    private void Awake() => _vessel = GetComponent<CookingVessel>();

    private void OnGUI()
    {
        if (!_show || _vessel == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 world = transform.position + Vector3.up * 0.5f;
        if (Vector3.Distance(cam.transform.position, world) > _maxDistance) return;

        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                richText = true
            };
        }

        string txt = $"<b>{(_vessel.Def != null ? _vessel.Def.DisplayName : "?")}</b>\n" +
                     $"Весы: <b>{_vessel.MassKg:0.0} кг</b>\n" +
                     $"Фракций: {_vessel.View.Count}" +
                     (_vessel.IsSealed ? "  [закрыта]" : "") + "\n";

        foreach (FoodPortionView v in _vessel.View)
        {
            txt += $"• {FoodRegistry.NameOfIngredient(v.IngredientId)} — " +
                   $"{v.MassG / 1000f:0.00} кг, " +
                   $"{FoodRegistry.NameOfProcess(v.LastProcessId)}" +
                   (v.ProcessCount > 1 ? $" (+{v.ProcessCount - 1})" : "") +
                   $", кач. {v.Quality01 * 100f:0}%\n";
        }

        Vector2 pos = new Vector2(sp.x + _screenOffset.x, Screen.height - sp.y + _screenOffset.y);
        Vector2 size = _style.CalcSize(new GUIContent(txt));
        GUI.Box(new Rect(pos.x, pos.y, size.x + 12f, size.y + 8f), txt, _style);
    }
}
