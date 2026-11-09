using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ТОЧКА ПОДКЛЮЧЕНИЯ ГАЗА. Вешается на плиту, горелку, любую технику,
/// которой нужен газ.
///
/// ПОРТЫ РЕГИСТРИРУЮТСЯ САМИ. Шлангу не нужно ни искать их лучом, ни знать
/// про слои и маски: он просто спрашивает ближайший из списка. Настраивать
/// нечего, конфликтовать нечему, и новый порт начинает работать сразу, как
/// только появился в сцене.
///
/// Компонент намеренно тупой: он не знает ни про баллон, ни про давление.
/// Его дело — сказать, где крепиться и занято ли место.
/// </summary>
public class GasPort : MonoBehaviour
{
    [Tooltip("Куда садится конец шланга. Пусто = сам объект.")]
    [SerializeField] private Transform _socket;

    [Tooltip("С какого расстояния конец сам прицепляется, метры. Подходишь " +
             "ближе — щёлкает без всякой наводки.")]
    [SerializeField] private float _snapRange = 0.7f;

    [Tooltip("Подпись для подсказки на экране. Пусто = имя объекта.")]
    [SerializeField] private string _label = "";

    /// <summary>Все порты на сцене. Заполняется сам при включении объекта.</summary>
    private static readonly List<GasPort> All = new List<GasPort>();

    /// <summary>Кто подключён. Null — порт свободен.</summary>
    public GasHoseEnd Connected { get; private set; }

    public Transform Socket => _socket != null ? _socket : transform;
    public float SnapRange => _snapRange;
    public bool IsFree => Connected == null;
    public string Label => string.IsNullOrEmpty(_label) ? name : _label;

    /// <summary>Идёт ли сейчас газ. Техника спрашивает это у своего порта.</summary>
    public bool HasGas => Connected != null && Connected.IsSupplying;

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);

        // Порт всегда просыпается СВОБОДНЫМ. Кто в нём сидит, скажет шланг,
        // когда синхронизируется: связь живёт в SyncVar у GasHoseEnd, а тут
        // только её отражение.
        Connected = null;
    }

    private void OnDisable()
    {
        All.Remove(this);
        Connected = null;
    }

    /// <summary>
    /// Статику надо чистить руками.
    ///
    /// Список All — static, а Unity при выключенном Reload Domain (то самое
    /// «Enter Play Mode Options», которое включают ради быстрого старта) НЕ
    /// сбрасывает статические поля между запусками. Список приезжает в новую
    /// сессию с уничтоженными портами прошлой, и шланг цепляется к трупу:
    /// внешне всё подключено, а газа нет. Ровно тот баг, где плита просит
    /// подключить газ, который «включен с прошлого раза».
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => All.Clear();

    public void SetConnected(GasHoseEnd end) => Connected = end;

    /// <summary>
    /// Ближайший свободный порт к точке, до которого реально дотянуться.
    ///
    /// Именно ближайший, а не первый попавшийся: у плиты может быть несколько
    /// точек рядом, и цепляться должна та, к которой поднесли.
    /// </summary>
    public static GasPort FindNearest(Vector3 point, GasPort[] allowed = null)
    {
        GasPort best = null;
        float bestSqr = float.MaxValue;

        for (int i = All.Count - 1; i >= 0; i--)
        {
            GasPort p = All[i];
            if (p == null) { All.RemoveAt(i); continue; }
            if (!p.IsFree) continue;

            // Если задан свой список, чужие порты игнорируем.
            if (allowed != null && allowed.Length > 0 && System.Array.IndexOf(allowed, p) < 0)
                continue;

            float sqr = (p.Socket.position - point).sqrMagnitude;
            if (sqr > p._snapRange * p._snapRange) continue;
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            best = p;
        }
        return best;
    }

    /// <summary>Ближайший порт БЕЗ учёта радиуса — для подсказки игроку,
    /// насколько ещё надо подойти.</summary>
    public static GasPort Nearest(Vector3 point, out float distance)
    {
        GasPort best = null;
        float bestSqr = float.MaxValue;

        for (int i = All.Count - 1; i >= 0; i--)
        {
            GasPort p = All[i];
            if (p == null) { All.RemoveAt(i); continue; }

            float sqr = (p.Socket.position - point).sqrMagnitude;
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            best = p;
        }

        distance = best != null ? Mathf.Sqrt(bestSqr) : 0f;
        return best;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = IsFree ? new Color(0.4f, 1f, 0.5f, 0.35f)
                              : new Color(1f, 0.6f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(Socket.position, _snapRange);
    }
}
