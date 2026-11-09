using System.Collections.Generic;
using UnityEngine;

/// <summary>Один факт обработки: что сделали и насколько ровно вышло.</summary>
public struct ProcessRecord
{
    public ushort ProcessId;
    public float Quality01;   // результат мини-игры, 0..1
}

/// <summary>
/// АТОМ ЕДЫ. Фракция внутри ёмкости или содержимое предмета-продукта.
///
/// Это СЕРВЕРНЫЙ класс (не struct и не NetworkBehaviour) — чистые данные.
/// Клиентам он никогда не уходит целиком: по сети летит FoodPortionView.
/// Причина: FishNet сравнивает элементы SyncList по значению, а структура со
/// списком внутри сравнивается по ССЫЛКЕ на список. Дельта либо не отправится
/// вовсе, либо отправится вхолостую. Поэтому — сервер владеет, клиент видит слепок.
///
/// Ключевое поле — Log. Это УПОРЯДОЧЕННАЯ цепочка обработок. Именно она
/// отличает картошку борща (чистится/кубиком/варится) от картошки пастушьего
/// пирога (чистится/крупно/варится/мнётся) при одном и том же ингредиенте.
/// </summary>
public class FoodPortion
{
    public ushort IngredientId;

    /// <summary>Масса в ГРАММАХ, целое. Всё, что связано с вместимостью и
    /// фасовкой, считается целыми — иначе после двадцатой миски вылезет
    /// отрицательный остаток из-за накопленной ошибки float.</summary>
    public int MassG;

    public TasteVector Taste;
    public float Temperature = 20f;

    public readonly List<ProcessRecord> Log = new List<ProcessRecord>();

    // ───────────────────────── СОЗДАНИЕ ─────────────────────────

    public static FoodPortion FromIngredient(IngredientDef def, int massG)
    {
        if (def == null || massG <= 0) return null;
        return new FoodPortion
        {
            IngredientId = def.Id,
            MassG        = massG,
            Taste        = def.TastePerKg * (massG / 1000f),
            Temperature  = 20f
        };
    }

    public FoodPortion Clone()
    {
        FoodPortion c = new FoodPortion
        {
            IngredientId = IngredientId,
            MassG        = MassG,
            Taste        = Taste,
            Temperature  = Temperature
        };
        c.Log.AddRange(Log);
        return c;
    }

    // ───────────────────────── ОБРАБОТКА ─────────────────────────

    /// <summary>
    /// Можно ли применить этот процесс к ЭТОМУ конкретному продукту.
    ///
    /// Проверяет IngredientDef.AllowedProcesses — список, который в проекте
    /// давно существовал, но нигде не читался. Именно поэтому ёмкость до сих
    /// пор варила бы соль наравне со всем остальным: она смотрела только на
    /// свой VesselDef.SupportedProcesses (может ли ЧАН физически кипятить), но
    /// не на то, что за продукт внутри. Два независимых фильтра — тара
    /// проверяет СЕБЯ, продукт проверяет СЕБЯ, ни один не знает о другом.
    ///
    /// Сравнение по Id, а не по ссылке: ассеты те же самые, но Id переживает
    /// пересборку и однозначно сериализуется, а ссылка — нет.
    /// </summary>
    public bool CanApply(ProcessDef process)
    {
        if (process == null) return false;

        IngredientDef def = FoodRegistry.Instance != null
            ? FoodRegistry.Instance.GetIngredient(IngredientId)
            : null;

        // Продукт не нашёлся в реестре — не блокируем работу молча. Лучше
        // разрешить и обработку записать, чем без объяснимой причины сделать
        // предмет неприкасаемым из-за дыры в данных.
        if (def == null || def.AllowedProcesses == null || def.AllowedProcesses.Length == 0)
            return true;

        for (int i = 0; i < def.AllowedProcesses.Length; i++)
            if (def.AllowedProcesses[i] != null && def.AllowedProcesses[i].Id == process.Id)
                return true;

        return false;
    }

    /// <summary>Применить обработку. Масса может уменьшиться (шелуха, обрезки).</summary>
    public void ApplyProcess(ProcessDef process, float quality01)
    {
        if (process == null) return;

        MassG = Mathf.Max(1, Mathf.RoundToInt(MassG * process.MassYield));
        Taste += process.TasteDeltaPerKg * (MassG / 1000f);
        Log.Add(new ProcessRecord
        {
            ProcessId = process.Id,
            Quality01 = Mathf.Clamp01(quality01)
        });
    }

    /// <summary>
    /// Изменить массу с сохранением концентрации. Нужно везде, где еда УБЫВАЕТ
    /// не по рецепту: коты своровали кусок, разлил, отсыпал в миску.
    ///
    /// Вкус обязан уехать ВМЕСТЕ с массой. Если этого не сделать, у оставшегося
    /// борща концентрация соли подскочит, и коты будут недовольны непонятно
    /// почему. Баг проявится через час геймплея и найдётся тяжело.
    /// </summary>
    public void ScaleMass(float k)
    {
        k = Mathf.Max(0f, k);
        MassG = Mathf.RoundToInt(MassG * k);
        Taste *= k;
    }

    // ───────────────────────── СЛИЯНИЕ ─────────────────────────

    /// <summary>
    /// Можно ли слить две фракции в одну. Условие жёсткое: тот же ингредиент И
    /// ПОЛНОСТЬЮ совпадающая цепочка обработок. Иначе варёная картошка слилась
    /// бы с сырой, и рецепт перестал бы их различать.
    ///
    /// Без слияния список фракций в чане на 40 котов распухнет до сотни
    /// элементов, и каждое добавление будет гонять по сети весь SyncList.
    /// </summary>
    public static bool CanMerge(FoodPortion a, FoodPortion b)
    {
        if (a == null || b == null) return false;
        if (a.IngredientId != b.IngredientId) return false;
        if (a.Log.Count != b.Log.Count) return false;
        for (int i = 0; i < a.Log.Count; i++)
            if (a.Log[i].ProcessId != b.Log[i].ProcessId) return false;
        return true;
    }

    /// <summary>Влить в себя другую фракцию. Вызывать только после CanMerge.</summary>
    public void MergeIn(FoodPortion other)
    {
        float total = MassG + other.MassG;
        if (total <= 0f) return;

        // Качество усредняем ПО МАССЕ: 100 г идеально нарезанной свёклы не
        // должны вытягивать оценку 3 кг накромсанных бензопилой.
        for (int i = 0; i < Log.Count; i++)
        {
            ProcessRecord r = Log[i];
            r.Quality01 = (r.Quality01 * MassG + other.Log[i].Quality01 * other.MassG) / total;
            Log[i] = r;
        }

        Temperature = (Temperature * MassG + other.Temperature * other.MassG) / total;

        // Вкус абсолютный — просто складывается. Никаких усреднений.
        Taste += other.Taste;
        MassG += other.MassG;
    }

    // ───────────────────────── ПРОИЗВОДНОЕ ─────────────────────────

    public float AverageQuality01()
    {
        if (Log.Count == 0) return 1f;   // сырой продукт не испорчен
        float s = 0f;
        for (int i = 0; i < Log.Count; i++) s += Log[i].Quality01;
        return s / Log.Count;
    }

    public FoodPortionView ToView()
    {
        return new FoodPortionView
        {
            IngredientId  = IngredientId,
            MassG         = MassG,
            LastProcessId = Log.Count > 0 ? Log[Log.Count - 1].ProcessId : (ushort)0,
            ProcessCount  = (byte)Mathf.Min(Log.Count, 255),
            AvgQuality    = (byte)Mathf.RoundToInt(AverageQuality01() * 255f)
        };
    }
}

/// <summary>
/// СЕТЕВОЙ СЛЕПОК фракции. Только то, что нужно клиенту для картинки и UI.
/// Полные цепочки обработок клиенту не нужны никогда — рецепт считает сервер.
///
/// LastProcessId вместо полного лога: внешний вид определяет последняя
/// трансформация. Нарезанное выглядит нарезанным независимо от того, что было
/// до этого.
/// </summary>
public struct FoodPortionView
{
    public ushort IngredientId;
    public int MassG;
    public ushort LastProcessId;
    public byte ProcessCount;
    public byte AvgQuality;       // 0..255

    public float Quality01 => AvgQuality / 255f;
}
