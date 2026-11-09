#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// ОСНОВА ЦВЕТА В VOLUME.
///
/// ═══ ЗАЧЕМ ОТДЕЛЬНЫЙ СЛОЙ ═══
///
/// Пульт погоды (CatCooksMoodDirector) при каждом применении стиля
/// перезаписывает в профиле ровно шесть значений:
///
///     ColorAdjustments → postExposure, saturation, contrast
///     WhiteBalance     → temperature
///     Bloom            → intensity
///     Vignette         → intensity
///
/// И БОЛЬШЕ НИЧЕГО. Всё остальное в профиле он не трогает вовсе.
///
/// Отсюда разделение, которое само собой напрашивается:
///
///   ПУЛЬТ отвечает за то, что МЕНЯЕТСЯ с погодой — яркость, насыщенность,
///   контраст, температура, сила свечения и виньетки.
///
///   ЭТА ОСНОВА отвечает за то, что НЕ ДОЛЖНО меняться никогда — характер
///   картинки. Тоновое отображение, тёплые тени, оттенок свечения, зерно.
///
/// Правки руками в профиле жили ровно до следующей смены погоды, и это сбивало
/// с толку: выставил, полюбовался, сменил погоду — всё вернулось. Причина не в
/// том, что настройки не сохранились, а в том, что их кто-то перезаписал.
///
/// Здесь такого не случится: ни одно поле из списка выше тут не трогается.
///
/// Меню: CatCooks ▸ Атмосфера ▸ Основа цвета
/// </summary>
public static class CatCooksBaseLook
{
    private enum Mood { Warm, Neutral }

    [MenuItem("CatCooks/Атмосфера/Основа цвета ▸ Тёплая", false, 8)]
    private static void ApplyWarm() => Apply(Mood.Warm);

    [MenuItem("CatCooks/Атмосфера/Основа цвета ▸ Нейтральная", false, 9)]
    private static void ApplyNeutral() => Apply(Mood.Neutral);

    private static void Apply(Mood mood)
    {
        Volume vol = FindGlobal();

        if (vol == null)
        {
            Debug.LogError("[Основа] Глобальный Volume не найден. Сначала " +
                           "CatCooks ▸ Атмосфера ▸ Собрать сцену.");
            return;
        }

        VolumeProfile prof = vol.profile;

        if (prof == null)
        {
            Debug.LogError($"[Основа] У «{vol.name}» нет профиля.", vol);
            return;
        }

        bool warm = mood == Mood.Warm;

        // ── ТОНОВОЕ ОТОБРАЖЕНИЕ ──
        //
        // Обязательно и первым. Без него тёплый сдвиг выбивает света в чистый
        // белый: всё ярче единицы обрезается, и на небе появляются плоские
        // выжженные пятна там, где должно быть солнце.
        //
        // Neutral, а не ACES: ACES заметно душит насыщенность и уводит
        // картинку в кинематографичность, мультяшному миру ненужную.
        Tonemapping tone = Get<Tonemapping>(prof);
        Set(tone.mode, TonemappingMode.Neutral);

        // ── ЦВЕТОВОЙ ФИЛЬТР И ОТТЕНОК ──
        //
        // Экспозицию, насыщенность и контраст НЕ ТРОГАЕМ — их ведёт пульт.
        ColorAdjustments ca = Get<ColorAdjustments>(prof);
        Set(ca.colorFilter, warm ? H("FFF8EE") : Color.white);
        Set(ca.hueShift, 0f);

        // Температуру ведёт пульт, оттенок — нет.
        WhiteBalance wb = Get<WhiteBalance>(prof);
        Set(wb.tint, warm ? 4f : 0f);

        // ── ТЁПЛЫЕ ТЕНИ ──
        //
        // Самое важное здесь, и пульт этого не касается вовсе.
        //
        // Разбор кадров референса: освещённая сторона балки #D08A5E, теневая
        // #9C6A50 — коричневый. Обычно тень уводят в синеву, «так физичнее»,
        // и картинка немедленно становится холодной.
        //
        // Одна эта правка меняет ощущение всей сцены, не трогая ни одного
        // материала.
        ShadowsMidtonesHighlights smh = Get<ShadowsMidtonesHighlights>(prof);

        Set(smh.shadows, warm ? Tint("B08A70", 0.55f) : Vector4Neutral());
        Set(smh.midtones, warm ? Tint("FFF4E4", 0.30f) : Vector4Neutral());
        Set(smh.highlights, warm ? Tint("FFF0D8", 0.40f) : Vector4Neutral());

        Set(smh.shadowsStart, 0f);
        Set(smh.shadowsEnd, 0.35f);
        Set(smh.highlightsStart, 0.55f);
        Set(smh.highlightsEnd, 1.1f);

        // ── СВЕЧЕНИЕ: ХАРАКТЕР, НЕ СИЛА ──
        //
        // Силу ведёт пульт, поэтому задаём только порог, рассеяние и оттенок.
        //
        // Порог выше единицы принципиален: светиться должны солнце, блики и
        // вывески. Порог ниже размазывает свечение по всей светлой геометрии,
        // и картинка становится мутной, а не тёплой.
        Bloom bloom = Get<Bloom>(prof);
        Set(bloom.threshold, 1.05f);
        Set(bloom.scatter, 0.68f);
        Set(bloom.tint, warm ? H("FFE8C8") : Color.white);
        Set(bloom.highQualityFiltering, false);

        // ── ВИНЬЕТКА: ЦВЕТ, НЕ СИЛА ──
        //
        // Чёрная виньетка читается как прицел шутера. Коричневая — как мягкий
        // свет, падающий из центра кадра.
        Vignette vig = Get<Vignette>(prof);
        Set(vig.color, warm ? H("4A3A2E") : H("1A1A1E"));
        Set(vig.smoothness, 0.55f);
        Set(vig.rounded, false);

        // ── ЗЕРНО ──
        //
        // Оно не «состаривает», а СКЛЕИВАЕТ. Мультяшная картинка состоит из
        // больших плоских заливок, и на них видны ступеньки градиента —
        // особенно на небе, где переход идёт через полэкрана. Зерно их прячет.
        FilmGrain grain = Get<FilmGrain>(prof);
        Set(grain.type, FilmGrainLookup.Thin1);
        Set(grain.intensity, 0.12f);
        Set(grain.response, 0.8f);

        EditorUtility.SetDirty(prof);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Основа] «{(warm ? "Тёплая" : "Нейтральная")}» применена к " +
                  $"профилю «{prof.name}».\n" +
                  "Экспозиция, насыщенность, контраст, температура, сила свечения " +
                  "и виньетки НЕ ТРОНУТЫ — их ведёт пульт погоды, и любая правка " +
                  "здесь была бы стёрта при следующей смене стиля.", prof);
    }

    // ═════════════════════════ ВСПОМОГАТЕЛЬНОЕ ═════════════════════════

    private static Volume FindGlobal()
    {
        Volume best = null;

        foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (v == null || !v.isGlobal || v.profile == null) continue;

            // При нескольких глобальных побеждает тот, у кого выше приоритет —
            // ему и правим основу, иначе правка уйдёт в проигравший профиль.
            if (best == null || v.priority > best.priority) best = v;
        }

        return best;
    }

    /// <summary>Достать компонент из профиля или добавить, если его нет.</summary>
    private static T Get<T>(VolumeProfile prof) where T : VolumeComponent
    {
        if (prof.TryGet(out T existing)) return existing;

        T c = prof.Add<T>(true);
        c.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(c, prof);
        return c;
    }

    /// <summary>
    /// Включить переопределение и задать значение.
    ///
    /// Без overrideState настройка лежит в профиле, но НЕ ПРИМЕНЯЕТСЯ. Снаружи
    /// это выглядит как «всё выставил, ничего не изменилось» — галочка слева
    /// от поля просто снята.
    /// </summary>
    private static void Set<T>(VolumeParameter<T> p, T value)
    {
        p.overrideState = true;
        p.value = value;
    }

    private static Color H(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    private static Vector4 Vector4Neutral() => new Vector4(1f, 1f, 1f, 0f);

    /// <summary>
    /// Цвет → вектор для ShadowsMidtonesHighlights.
    ///
    /// Значение там — это МНОЖИТЕЛИ по каналам около единицы, а не цвет.
    /// Важна не тёмность оттенка, а его перекос: нормируем по наибольшему
    /// каналу и подмешиваем к нейтральному по заданной силе.
    /// </summary>
    private static Vector4 Tint(string hex, float strength)
    {
        Color c = H(hex);
        float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));

        if (m < 0.001f) return Vector4Neutral();

        Vector3 n = new Vector3(c.r / m, c.g / m, c.b / m);
        Vector3 mixed = Vector3.Lerp(Vector3.one, n, Mathf.Clamp01(strength));

        return new Vector4(mixed.x, mixed.y, mixed.z, 0f);
    }
}
#endif
