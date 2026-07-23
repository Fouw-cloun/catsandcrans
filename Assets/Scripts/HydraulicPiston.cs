using UnityEngine;

/// <summary>
/// Визуальный гидравлический поршень из ТРЁХ частей:
///   BasePart / RodPart — два конца, которые РАЗВОРАЧИВАЮТСЯ друг на друга
///                        (смотрят один на другой по оси, как шарнир).
///   MiddlePart          — средняя часть (шток), которая ЛОЖИТСЯ на линию
///                        между двумя концами и РАСТЯГИВАЕТСЯ по масштабу,
///                        заполняя расстояние между ними — по мере того как
///                        концы расходятся/сходятся, средняя часть сама
///                        удлиняется/укорачивается.
///
/// Чисто визуальный скрипт (не NetworkBehaviour) — своей синхронизации не
/// требует: работает от уже синхронизированных по сети точек крепления
/// (например кузов самосвала уже крутится по сети — этого достаточно, все
/// клиенты посчитают одинаковую картинку из одних и тех же данных).
///
/// НАСТРОЙКА:
///   BaseEnd / RodEnd — точки шарнирного крепления (на раме и на кузове).
///   BasePart / RodPart — сами меши концов, разворачиваются друг на друга.
///   MiddlePart — меш штока/трубы, который растягивается между BaseEnd и RodEnd.
///   Middle Length Axis Local — какая локальная ось меша "длинная" (обычно
///     Z или Y, смотря как экспортирован меш) — растяжение идёт по ней через Scale.
///   Middle Rest Length — длина меша "как есть" в сцене при масштабе 1.
/// </summary>
public class HydraulicPiston : MonoBehaviour
{
    [Header("Концы (поворачиваются друг на друга)")]
    public Transform basePart;   // нижняя часть (меш)
    public Transform baseEnd;    // точка крепления нижней части (на раме)
    public Transform rodPart;    // верхняя часть (меш)
    public Transform rodEnd;     // точка крепления верхней части (на кузове)

    [Header("Направление")]
    public bool invertBaseDirection = false;
    public bool invertRodDirection = false;
    [Tooltip("Доп. поворот меша по X, если модель импортирована 'смотрящей' не туда")]
    public float baseRotationOffsetX = 0f;
    public float rodRotationOffsetX = 0f;

    [Header("Средняя часть (растягивается между концами)")]
    [Tooltip("Меш штока/трубы, заполняющий расстояние между BaseEnd и RodEnd")]
    public Transform middlePart;
    [Tooltip("Локальная ось меша, вдоль которой он 'длинный' (обычно Z или Y)")]
    public Vector3 middleLengthAxisLocal = Vector3.forward;
    [Tooltip("Длина меша 'как есть' в сцене при масштабе 1 (единицы Unity)")]
    public float middleRestLength = 1f;
    [Tooltip("Небольшой отступ с каждого края — чтобы средняя часть визуально " +
             "пряталась внутри концевых деталей, а не торчала впритык")]
    public float middleInset = 0f;
    [Tooltip("Доп. поворот средней части по X, если меш смотрит не туда")]
    public float middleRotationOffsetX = 0f;

    [Header("Сглаживание поворота (обычно не нужно — движение и так плавное)")]
    public bool smoothRotation = false;
    public float rotationSpeed = 10f;

    [Header("Debug")]
    public bool showDebugLines = false;

    private Vector3 _middleInitialScale;
    private bool _middleScaleCaptured;

    private void LateUpdate()
    {
        if (!basePart || !baseEnd || !rodPart || !rodEnd)
            return;

        float dt = Time.deltaTime;

        // ===== BASE смотрит на rodEnd =====
        Vector3 baseDir = rodEnd.position - basePart.position;
        if (invertBaseDirection) baseDir = -baseDir;
        Quaternion baseTargetRot = basePart.rotation;
        if (baseDir.sqrMagnitude > 0.0001f)
        {
            baseTargetRot = Quaternion.LookRotation(baseDir.normalized, Vector3.up)
                * Quaternion.Euler(baseRotationOffsetX, 0f, 0f);
            basePart.rotation = smoothRotation
                ? Quaternion.Slerp(basePart.rotation, baseTargetRot, dt * rotationSpeed)
                : baseTargetRot;
        }

        // ===== ROD смотрит на baseEnd =====
        Vector3 rodDir = baseEnd.position - rodPart.position;
        if (invertRodDirection) rodDir = -rodDir;
        if (rodDir.sqrMagnitude > 0.0001f)
        {
            Quaternion rodTargetRot = Quaternion.LookRotation(rodDir.normalized, Vector3.up)
                * Quaternion.Euler(rodRotationOffsetX, 0f, 0f);
            rodPart.rotation = smoothRotation
                ? Quaternion.Slerp(rodPart.rotation, rodTargetRot, dt * rotationSpeed)
                : rodTargetRot;
        }

        // ===== MIDDLE: на линию между концами + растяжение по масштабу =====
        if (middlePart != null)
        {
            if (!_middleScaleCaptured)
            {
                _middleInitialScale = middlePart.localScale;
                _middleScaleCaptured = true;
            }

            Vector3 lineDir = rodEnd.position - baseEnd.position;
            float totalDist = lineDir.magnitude;

            if (totalDist > 0.0001f)
            {
                // Позиция — середина между концами.
                Vector3 midPos = Vector3.Lerp(baseEnd.position, rodEnd.position, 0.5f);
                middlePart.position = midPos;

                // Поворот — вдоль той же линии, что и концы.
                Quaternion midRot = Quaternion.LookRotation(lineDir.normalized, Vector3.up)
                    * Quaternion.Euler(middleRotationOffsetX, 0f, 0f);
                middlePart.rotation = smoothRotation
                    ? Quaternion.Slerp(middlePart.rotation, midRot, dt * rotationSpeed)
                    : midRot;

                // Растяжение — заполняем расстояние между концами (минус отступы).
                float effectiveLength = Mathf.Max(0.01f, totalDist - middleInset * 2f);
                float factor = middleRestLength > 0.0001f ? effectiveLength / middleRestLength : 1f;

                Vector3 axis = middleLengthAxisLocal.sqrMagnitude > 0.0001f
                    ? middleLengthAxisLocal.normalized : Vector3.forward;
                Vector3 scaled = _middleInitialScale;
                if (Mathf.Abs(axis.x) > 0.5f) scaled.x = _middleInitialScale.x * factor;
                else if (Mathf.Abs(axis.y) > 0.5f) scaled.y = _middleInitialScale.y * factor;
                else scaled.z = _middleInitialScale.z * factor;

                middlePart.localScale = scaled;
            }
        }

        // ===== DEBUG =====
        if (showDebugLines)
        {
            Debug.DrawLine(baseEnd.position, rodEnd.position, Color.yellow);
            Debug.DrawRay(basePart.position, basePart.forward * 0.3f, Color.green);
            Debug.DrawRay(rodPart.position, rodPart.forward * 0.3f, Color.cyan);
        }
    }
}
