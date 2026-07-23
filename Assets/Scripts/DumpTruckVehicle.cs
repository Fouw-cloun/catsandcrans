using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Самосвал. Колёсная физика (WheeledVehicle): 6 колёс, 2 передних поворотных.
///   WASD — езда, Q/E — поднять/опустить кузов (плавно, пока клавиша зажата,
///   останавливается сразу при отпускании), F — выйти.
///
/// АРХИТЕКТУРА КУЗОВА (важно, чтобы не наступить на старые грабли):
///   Целевой угол считается на клиенте-ВОДИТЕЛЕ каждый Update по Time.deltaTime
///   (НЕ по сетевым тикам — тики могут "досрочно" сработать несколько раз за
///   кадр при лагах, и если копить через тик, короткое нажатие может дать
///   скачок на много тиков вперёд). На сервер каждый тик уходит АБСОЛЮТНОЕ
///   текущее значение (не дельта) — повторная отправка того же числа безвредна.
///   Водитель видит своё значение мгновенно (локально), остальные — сглаженно
///   по SyncVar.
/// </summary>
public class DumpTruckVehicle : WheeledVehicle
{
    [Header("Кузов")]
    [SerializeField] private Transform _bed;
    [Tooltip("Скорость подъёма, градусы/сек")]
    [SerializeField] private float _tiltSpeed = 25f;
    [Tooltip("Скорость визуального сглаживания для НЕ-водителя (градусы/сек)")]
    [SerializeField] private float _visualSmoothSpeed = 90f;
    [Tooltip("Добавка к базовому повороту: 0 = закрыт (база, у тебя X≈-90), " +
             "70 = поднят (база + 70 = X≈-20).")]
    [SerializeField] private Vector2 _bedLimit = new Vector2(0f, 70f);

    // Целевой угол, посчитанный ЛОКАЛЬНО у водителя каждый кадр (точный,
    // не зависит от сетевых тиков).
    private float _localBedAngle;

    // Авторитетное значение сервера — синхронизируется всем клиентам.
    private readonly SyncVar<float> _bedAngle = new(0f);

    // Визуальный угол, который реально крутит меш — у водителя равен
    // _localBedAngle напрямую (без задержки), у остальных плавно догоняет
    // _bedAngle.Value.
    private float _bedVisualAngle;

    // Исходный поворот кузова "как есть в сцене" (модель импортирована со своим
    // оффсетом, у тебя это ~-90 по X). Запоминаем один раз и дальше крутим
    // ОТНОСИТЕЛЬНО него.
    private Quaternion _bedBaseLocalRotation;
    private bool _bedBaseCaptured;

    protected override void DriverUpdate()
    {
        float dir = (Input.GetKey(KeyCode.Q) ? 1f : 0f) - (Input.GetKey(KeyCode.E) ? 1f : 0f);
        if (dir != 0f)
        {
            // Защита от скачка кадра (лаг/GC-пауза/фриз редактора): даже если
            // реальный Time.deltaTime в этот кадр аномально большой, движение
            // кузова за ОДИН кадр не может превысить этот потолок. Без этой
            // защиты скачок FPS даёт пропорционально большой прыжок угла.
            float dt = Mathf.Min(Time.deltaTime, 0.05f); // потолок ~20 FPS на шаг

            float prevAngle = _localBedAngle;
            _localBedAngle = Mathf.Clamp(
                _localBedAngle + dir * _tiltSpeed * dt,
                _bedLimit.x, _bedLimit.y);

            // Диагностика: если шаг всё равно оказался подозрительно большим
            // (например Time.deltaTime был честно большим ДО клампа — сам факт
            // клампа уже спас от прыжка, но полезно видеть, что он сработал),
            // залогируем — при следующем "дёрганье" в консоли будут точные цифры.
            float step = Mathf.Abs(_localBedAngle - prevAngle);
            float maxExpectedStep = _tiltSpeed * dt * 1.5f; // с запасом
            if (step > maxExpectedStep + 0.01f)
            {
                Debug.LogWarning($"[DumpTruck] Подозрительный шаг кузова: " +
                    $"{prevAngle:F2} -> {_localBedAngle:F2} (шаг {step:F2}°), " +
                    $"raw deltaTime={Time.deltaTime:F4}, dir={dir}");
            }
            _localBedAngle = Mathf.Clamp(
                _localBedAngle + dir * _tiltSpeed * Time.deltaTime,
                _bedLimit.x, _bedLimit.y);
        }
    }

    protected override void DriverTick()
    {
        // Отправляем ТЕКУЩЕЕ (абсолютное) значение каждый тик — идемпотентно,
        // несколько подряд идущих тиков с одним и тем же числом безвредны.
        CmdSetBedAngle(_localBedAngle);
    }

    [ServerRpc]
    private void CmdSetBedAngle(float angle)
    {
        _bedAngle.Value = Mathf.Clamp(angle, _bedLimit.x, _bedLimit.y);
    }

    protected override void ApplyVisuals()
    {
        base.ApplyVisuals(); // визуал колёс

        if (_bed == null) return;

        if (!_bedBaseCaptured)
        {
            _bedBaseLocalRotation = _bed.localRotation;
            _bedVisualAngle = _bedAngle.Value;
            _localBedAngle = _bedAngle.Value; // не дёргаться из 0 при спавне
            _bedBaseCaptured = true;
        }

        if (_isLocalDriver)
        {
            // Водитель — без задержки, своё же посчитанное значение.
            _bedVisualAngle = _localBedAngle;
        }
        else
        {
            // Остальные — плавно догоняют то, что прислал сервер.
            _bedVisualAngle = Mathf.MoveTowards(_bedVisualAngle, _bedAngle.Value,
                _visualSmoothSpeed * Time.deltaTime);
        }

        // Крутим ДОБАВОЧНО к базовому повороту: база -90 (закрыт) + угол (0..70)
        // = -20 при полном подъёме. Плюс, не минус — иначе крутит в другую сторону.
        _bed.localRotation = _bedBaseLocalRotation * Quaternion.Euler(_bedVisualAngle, 0f, 0f);
    }
}
