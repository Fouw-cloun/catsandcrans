using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Безопасная проверка для тестовых пунктов ПКМ-меню компонентов («Тест: …»).
    /// FishNet падает с NullReferenceException на IsServerInitialized, если объект не в сети:
    /// меню нажато вне Play, на префабе в Project, или хост ещё не запущен. Здесь вместо падения —
    /// понятное предупреждение в консоли, что сделать.
    /// </summary>
    public static class NetTest
    {
        public static bool ServerReady(NetworkBehaviour nb, string tag)
        {
            string why = null;
            if (nb == null) why = "компонент не найден";
            else if (!Application.isPlaying) why = "работает только в Play: нажми Play, запусти хост, потом снова ПКМ → тест";
            else if (!nb.gameObject.scene.IsValid())
                why = "выбран префаб в Project — выдели объект в Hierarchy (тот, что в сцене) и нажми тест на нём";
            else if (nb.GetComponent<NetworkObject>() == null && nb.GetComponentInParent<NetworkObject>() == null)
                why = "у объекта нет Network Object — добавь его на корень";
            else
            {
                bool ok = false;
                try { ok = nb.IsServerInitialized; }
                catch (System.NullReferenceException) { why = "объект ещё не в сети — запусти хост (Host) и подожди, пока заспавнится сцена"; }
                if (ok) return true;
                if (why == null) why = "это окно не сервер — тест работает только на хосте";
            }
            Debug.LogWarning($"[{tag}] тест не выполнен: {why}", nb);
            return false;
        }
    }
}
