using UnityEngine;

/// <summary>
/// Многосекционный ("телескопический") гидроцилиндр — N секций вложены друг в
/// друга и по мере роста расстояния между двумя фиксированными точками сами
/// равномерно "расползаются", выдвигаясь одна из другой — как настоящий
/// составной гидроцилиндр самосвала.
///
/// Чисто визуальный скрипт (не NetworkBehaviour), своей сети не требует:
/// работает от уже синхронизированных по сети позиций первой и последней
/// секции (например кузов самосвала уже крутится по сети — этого достаточно,
/// все клиенты посчитают одинаковую картинку из одних и тех же данных).
///
/// НАСТРОЙКА (под твои 6 секций):
///   Sections[0]   — секция, физически прикреплённая к РАМЕ (обычный дочерний
///                   объект шасси в иерархии). Скрипт её НЕ двигает и НЕ
///                   поворачивает — только читает её текущее положение как
///                   "нижнюю точку" цилиндра.
///   Sections[5]   — секция, физически прикреплённая к КУЗОВУ (дочерний
///                   объект кузова — уже сама едет вместе с ним по сети).
///                   Тоже только читается, не двигается скриптом.
///   Sections[1..4]— 4 средние секции. Их МОЖНО оставить где угодно в
///                   иерархии (даже прямо под корнем машины) — скрипт сам
///                   выставляет их мировую позицию и поворот каждый кадр.
///
/// Секции НЕ обязательно вкладывать в иерархию Unity друг в друга — скрипт
/// работает в мировых координатах, поэтому родитель каждой секции не важен.
/// </summary>
public class TelescopingCylinder : MonoBehaviour
{
    [Tooltip("Секции по порядку СНИЗУ ВВЕРХ. Первая и последняя — фиксированные " +
             "концы (рама/кузов), скрипт их не трогает. Всё между ними — " +
             "скользящие сегменты, минимум 1 средняя (итого минимум 3 элемента).")]
    public Transform[] sections;

    [Header("Ориентация сегментов")]
    [Tooltip("Доп. поворот по X, если меши сегментов импортированы не тем концом вперёд")]
    public float rotationOffsetX = 0f;
    public bool invertDirection = false;

    [Header("Сглаживание (опционально — обычно не нужно, движение и так " +
            "плавное, т.к. следует за уже сглаженным углом кузова)")]
    public bool smooth = false;
    public float smoothSpeed = 15f;

    private void LateUpdate()
    {
        if (sections == null || sections.Length < 3) return; // мин. низ + 1 средняя + верх

        Transform baseAnchor = sections[0];
        Transform rodAnchor = sections[sections.Length - 1];
        if (baseAnchor == null || rodAnchor == null) return;

        Vector3 basePos = baseAnchor.position;
        Vector3 rodPos = rodAnchor.position;
        Vector3 dir = rodPos - basePos;
        if (invertDirection) dir = -dir;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion lookRot = Quaternion.LookRotation(dir.normalized, Vector3.up)
            * Quaternion.Euler(rotationOffsetX, 0f, 0f);

        int middleCount = sections.Length - 2;
        for (int i = 1; i <= middleCount; i++)
        {
            Transform seg = sections[i];
            if (seg == null) continue;

            // Равномерно распределяем между концами (i-й из middleCount).
            // По мере роста расстояния base↔rod сегменты сами "разъезжаются" —
            // это и даёт эффект выдвижения секций друг из друга.
            float t = (float)i / (middleCount + 1);
            Vector3 targetPos = Vector3.Lerp(basePos, rodPos, t);

            if (smooth)
            {
                seg.position = Vector3.Lerp(seg.position, targetPos, Time.deltaTime * smoothSpeed);
                seg.rotation = Quaternion.Slerp(seg.rotation, lookRot, Time.deltaTime * smoothSpeed);
            }
            else
            {
                seg.position = targetPos;
                seg.rotation = lookRot;
            }
        }
    }
}
