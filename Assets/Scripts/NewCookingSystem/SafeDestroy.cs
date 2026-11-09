using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Удалить объект правильно в любом режиме: в игре — Destroy, в редакторе — DestroyImmediate.
    /// Нужно в OnDestroy у авто-объектов (струи, пена, пар): их добивает уборщик после выхода из Play
    /// (CatCooksPlayModeCleanup) уже в режиме редактора, а там обычный Destroy запрещён —
    /// Unity пишет «Destroy may not be called from edit mode!».
    /// </summary>
    public static class SafeDestroy
    {
        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
