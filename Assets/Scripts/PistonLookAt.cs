using UnityEngine;

/// <summary>
/// Простой визуальный поршень из ДВУХ частей, которые постоянно смотрят друг
/// на друга (цилиндр наводится на конец штока, шток — на конец цилиндра).
/// Чисто косметика: не NetworkBehaviour, своей синхронизации не требует —
/// работает от положения уже синхронной по сети иерархии руки экскаватора,
/// поэтому у всех клиентов выглядит одинаково автоматически.
///
/// Вешается на любой объект (например на сам поршень). На каждый поршень руки
/// — свой экземпляр этого компонента.
///
/// НАСТРОЙКА:
///   BasePart / BaseEnd — меш нижней части и точка-цель, КУДА она смотрит
///                        (обычно точка крепления на дальней детали).
///   RodPart  / RodEnd  — меш штока и его точка-цель.
/// Точки End — это пустые объекты в местах шарнирного крепления поршня.
/// </summary>
public class PistonLookAt : MonoBehaviour
{
    [Header("Нижняя часть (цилиндр)")]
    public Transform basePart;   // меш
    public Transform baseTarget; // куда смотрит basePart (обычно = rodEnd)

    [Header("Шток")]
    public Transform rodPart;    // меш
    public Transform rodTarget;  // куда смотрит rodPart (обычно = baseEnd)

    [Header("Подгонка ориентации")]
    [Tooltip("Доп. поворот по X для basePart, если меш 'смотрит' не тем концом")]
    public float baseOffsetX = 0f;
    [Tooltip("Доп. поворот по X для rodPart")]
    public float rodOffsetX = 0f;
    public bool invertBase = false;
    public bool invertRod = false;

    [Header("Сглаживание (обычно не нужно)")]
    public bool smooth = false;
    public float smoothSpeed = 12f;

    [Header("Debug")]
    public bool showDebugLines = false;

    private void LateUpdate()
    {
        float dt = Time.deltaTime;

        // Нижняя часть смотрит на свою цель.
        if (basePart != null && baseTarget != null)
        {
            Vector3 dir = baseTarget.position - basePart.position;
            if (invertBase) dir = -dir;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up)
                    * Quaternion.Euler(baseOffsetX, 0f, 0f);
                basePart.rotation = smooth
                    ? Quaternion.Slerp(basePart.rotation, target, dt * smoothSpeed)
                    : target;
            }
        }

        // Шток смотрит на свою цель.
        if (rodPart != null && rodTarget != null)
        {
            Vector3 dir = rodTarget.position - rodPart.position;
            if (invertRod) dir = -dir;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up)
                    * Quaternion.Euler(rodOffsetX, 0f, 0f);
                rodPart.rotation = smooth
                    ? Quaternion.Slerp(rodPart.rotation, target, dt * smoothSpeed)
                    : target;
            }
        }

        if (showDebugLines && basePart != null && rodPart != null)
        {
            if (baseTarget != null) Debug.DrawLine(basePart.position, baseTarget.position, Color.green);
            if (rodTarget != null) Debug.DrawLine(rodPart.position, rodTarget.position, Color.cyan);
        }
    }
}
