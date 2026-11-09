using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// СОСТОЯНИЕ ПРОДУКТА. Словарь ОБЩИЙ на всю игру.
    ///
    /// ═══ ПОЧЕМУ ИМЕНА ЛАТИНИЦЕЙ, А ПОДПИСИ РУССКИЕ ═══
    ///
    /// Само имя в коде остаётся английским: кириллица в идентификаторах ломает
    /// автодополнение, поиск по проекту и любые внешние инструменты. А вот в
    /// выпадающих списках редактора видно русское — за это отвечает атрибут
    /// InspectorName. Пишешь код латиницей, настраиваешь мышкой по-русски.
    ///
    /// ═══ ПРАВИЛА ПРАВКИ ═══
    ///
    /// Числа заданы явно и НЕ МЕНЯЮТСЯ: сохранёнки и сетевые пакеты хранят
    /// именно число. Состояние больше не нужно — оставь дырку, не сдвигай
    /// соседей. Новое — только в конец своего диапазона.
    ///
    ///   1-19     подготовка        60-79    сыпучее
    ///   20-39    форма             80-99    жидкое, молочное, яйцо
    ///   40-59    нагрев            100-119  мясо      120-139  тесто
    /// </summary>
    public enum FoodState : byte
    {
        [InspectorName("— нет —")]                None = 0,

        // ── подготовка ──
        [InspectorName("Подготовка/Сырое")]       Raw = 1,
        [InspectorName("Подготовка/Грязное")]     Dirty = 2,
        [InspectorName("Подготовка/Мытое")]       Washed = 3,
        [InspectorName("Подготовка/Чищеное")]     Peeled = 4,
        [InspectorName("Подготовка/Маринованное")] Marinated = 5,
        [InspectorName("Подготовка/В панировке")] Breaded = 6,
        [InspectorName("Подготовка/Отбито")]      Pounded = 7,

        // ── форма ──
        [InspectorName("Форма/Кусочки")]          Pieces = 20,
        [InspectorName("Форма/Тёртое")]           Grated = 21,
        [InspectorName("Форма/Пюре")]             Puree = 22,
        [InspectorName("Форма/Фарш")]             Minced = 23,
        [InspectorName("Форма/Раскатано в пласт")] Rolled = 24,
        [InspectorName("Форма/Сформовано")]       Shaped = 25,
        [InspectorName("Форма/Отсажено")]         Piped = 26,

        // ── нагрев ──
        [InspectorName("Нагрев/Варёное")]         Boiled = 40,
        [InspectorName("Нагрев/Жареное")]         Fried = 41,
        [InspectorName("Нагрев/Запечённое")]      Baked = 42,
        [InspectorName("Нагрев/Тушёное")]         Stewed = 43,
        [InspectorName("Нагрев/С вертела")]       Spitted = 44,
        [InspectorName("Нагрев/Копчёное")]        Smoked = 45,
        [InspectorName("Нагрев/НЕДОготовлено")]   Undercooked = 46,
        [InspectorName("Нагрев/ПЕРЕготовлено")]   Overcooked = 47,
        [InspectorName("Нагрев/Сгорело")]         Burnt = 48,
        [InspectorName("Нагрев/Опалено")]         Torched = 49,

        // ── сыпучее ──
        [InspectorName("Сыпучее/В мешке")]        InSack = 60,
        [InspectorName("Сыпучее/Зачерпнуто")]     Scooped = 61,
        [InspectorName("Сыпучее/Пыль")]           Dust = 62,
        [InspectorName("Сыпучее/Кучка сухая")]    PileDry = 63,
        [InspectorName("Сыпучее/Кучка набухшая")] PileSwollen = 64,
        [InspectorName("Сыпучее/Растворилось")]   Dissolved = 65,

        // ── жидкое, молочное, яйцо ──
        [InspectorName("Жидкое/В таре")]          InContainer = 80,
        [InspectorName("Жидкое/Налито")]          Poured = 81,
        [InspectorName("Жидкое/Растоплено")]      Melted = 82,
        [InspectorName("Жидкое/Взбито")]          Whipped = 83,
        [InspectorName("Яйцо/В скорлупе")]        InShell = 84,
        [InspectorName("Яйцо/Разбито")]           Cracked = 85,
        [InspectorName("Яйцо/Желток")]            Yolk = 86,
        [InspectorName("Яйцо/Белок")]             White = 87,

        // ── мясо ──
        [InspectorName("Мясо/Туша целиком")]      Carcass = 100,
        [InspectorName("Мясо/Разделано")]         Butchered = 101,

        // ── тесто ──
        [InspectorName("Тесто/Замешано")]         Kneaded = 120,
        [InspectorName("Тесто/Подошло")]          Proofed = 121,
        [InspectorName("Тесто/Готовый продукт")]  Ready = 122,

        // ── холод и выдержка (140-159) ──
        [InspectorName("Холод/Охлаждено")]        Chilled = 140,
        [InspectorName("Холод/Заморожено")]       Frozen = 141,
        [InspectorName("Время/Подсохло")]         Dried = 142,

        // ── порча ──
        [InspectorName("Порча/Испортилось")]      Spoiled = 200,
    }

    /// <summary>
    /// ПРОЦЕСС — то, что делает инструмент. Не сам инструмент: болгарка и тёрка
    /// делают один процесс, и в таблице им соответствует одна запись.
    ///
    /// Инструмент знает свой процесс и больше ничего. Поэтому новый инструмент —
    /// это одна строчка в инспекторе, а не новый скрипт.
    /// </summary>
    public enum FoodProcess : byte
    {
        [InspectorName("— нет —")]                       None = 0,

        // подготовка
        [InspectorName("Подготовка/Мыть")]               Wash = 1,
        [InspectorName("Подготовка/Чистить")]            Peel = 2,
        [InspectorName("Подготовка/Разделывать")]        Butcher = 3,
        [InspectorName("Подготовка/Мариновать")]         Marinate = 4,
        [InspectorName("Подготовка/Панировать")]         Bread = 5,
        [InspectorName("Подготовка/Отбивать")]           Pound = 6,

        // разделение
        [InspectorName("Разделение/Резать")]             Cut = 20,
        [InspectorName("Разделение/Тереть")]             Grate = 21,
        [InspectorName("Разделение/Толочь")]             Mash = 22,
        [InspectorName("Разделение/Молоть в фарш")]      Mince = 23,
        [InspectorName("Разделение/Раскатывать")]        Roll = 24,
        [InspectorName("Разделение/Формовать")]          Shape = 25,
        [InspectorName("Разделение/Отсаживать шприцем")] Pipe = 26,

        // нагрев
        [InspectorName("Нагрев/Варить")]                 Boil = 40,
        [InspectorName("Нагрев/Жарить")]                 Fry = 41,
        [InspectorName("Нагрев/Запекать")]               Bake = 42,
        [InspectorName("Нагрев/Тушить")]                 Stew = 43,
        [InspectorName("Нагрев/Жарить на вертеле")]      Spit = 44,
        [InspectorName("Нагрев/Коптить")]                Smoke = 45,
        [InspectorName("Нагрев/Опалять горелкой")]       Torch = 46,

        // сыпучее и жидкое
        [InspectorName("Сыпучее/Черпать")]               Scoop = 60,
        [InspectorName("Сыпучее/Просеивать")]            Sift = 61,
        [InspectorName("Жидкое/Лить")]                   Pour = 62,
        [InspectorName("Жидкое/Мешать")]                 Mix = 63,
        [InspectorName("Жидкое/Взбивать")]               Whip = 64,
        [InspectorName("Яйцо/Разбивать")]                Crack = 65,
        [InspectorName("Яйцо/Отделять белок")]           Separate = 66,

        // холод и время
        [InspectorName("Холод/Охлаждать")]               Chill = 80,
        [InspectorName("Холод/Замораживать")]            Freeze = 81,
        [InspectorName("Время/Дать подойти")]            Rest = 82,
    }

    /// <summary>
    /// КЛАСС ОТРИСОВКИ — как продукт выглядит. Шесть на всю игру, поэтому
    /// «тёртая морковь», «тёртый сыр» и «молотый перец» рисуются одним кодом.
    /// </summary>
    public enum FoodVisual : byte
    {
        [InspectorName("— нет —")]                  None = 0,
        [InspectorName("Целое (меш)")]              Solid = 1,
        [InspectorName("Куски (инстансы)")]         Pieces = 2,
        [InspectorName("Кучка (горка)")]            Pile = 3,
        [InspectorName("Масса (пюре, фарш)")]       Mass = 4,
        [InspectorName("Жидкость (уровень)")]       Liquid = 5,
        [InspectorName("Пласт (сетка)")]            Sheet = 6,
        [InspectorName("Не рисуется")]              Hidden = 7,
    }

    /// <summary>
    /// ЧЕМ МОЖНО ВЗЯТЬ. Не задаётся руками — вычисляется из класса отрисовки
    /// и температуры. Нужен для проверки: горячее рукой не поднять, муку
    /// в кармане не унести.
    /// </summary>
    public enum FoodHandling : byte
    {
        [InspectorName("Руками")]                   Hands = 0,
        [InspectorName("Щипцами (горячее)")]        Tongs = 1,
        [InspectorName("Лопатой (сыпучее)")]        Shovel = 2,
        [InspectorName("Половником (жидкое)")]      Ladle = 3,
        [InspectorName("Краном (тяжёлое)")]         Crane = 4,
    }

    /// <summary>Температура предмета. Три ступени — больше не нужно.</summary>
    public enum FoodTemp : byte
    {
        [InspectorName("Холодное")]                 Cold = 0,
        [InspectorName("Тёплое")]                   Warm = 1,
        [InspectorName("Горячее")]                  Hot = 2,
    }

    /// <summary>
    /// НАРУШЕНИЕ ПРАВИЛ ГОТОВКИ. Копится по ходу и снижает итоговую оценку.
    ///
    /// Это ответ на «пюре тоже должно как-то оцениваться»: даже у простейшего
    /// продукта есть что нарушить, и штрафы накапливаются ещё до тарелки.
    /// </summary>
    public enum FoodFault : byte
    {
        [InspectorName("Уронил на пол")]            Dropped = 0,
        [InspectorName("Переготовлено")]            Overcooked = 1,
        [InspectorName("Недоготовлено")]            Undercooked = 2,
        [InspectorName("Грязный продукт")]          Dirty = 3,
        [InspectorName("Не тот инструмент")]        WrongTool = 4,
        [InspectorName("Испортилось от времени")]   Spoiled = 5,
        [InspectorName("Готовили не в таре")]       NoVessel = 6,
    }

    /// <summary>
    /// Справочники. Свойства состояния одинаковы для всех продуктов, поэтому
    /// живут в коде, а не в ассетах: у продукта настраивается меш и цвет,
    /// а не способ рисования.
    /// </summary>
    public static class FoodStateInfo
    {
        public static FoodVisual VisualOf(FoodState s)
        {
            switch (s)
            {
                case FoodState.Raw: case FoodState.Dirty: case FoodState.Washed:
                case FoodState.Peeled: case FoodState.Marinated: case FoodState.Breaded:
                case FoodState.Carcass: case FoodState.InShell: case FoodState.Shaped:
                case FoodState.Ready: case FoodState.Pounded: case FoodState.Piped:
                case FoodState.Chilled: case FoodState.Frozen: case FoodState.Dried:
                    return FoodVisual.Solid;

                case FoodState.Pieces: case FoodState.Butchered:
                    return FoodVisual.Pieces;

                case FoodState.Grated: case FoodState.Dust: case FoodState.PileDry:
                case FoodState.PileSwollen: case FoodState.Scooped: case FoodState.InSack:
                    return FoodVisual.Pile;

                case FoodState.Puree: case FoodState.Minced: case FoodState.Whipped:
                case FoodState.Kneaded: case FoodState.Proofed:
                    return FoodVisual.Mass;

                case FoodState.InContainer: case FoodState.Poured: case FoodState.Melted:
                case FoodState.Cracked: case FoodState.Yolk: case FoodState.White:
                    return FoodVisual.Liquid;

                case FoodState.Rolled:
                    return FoodVisual.Sheet;

                case FoodState.Dissolved:
                    return FoodVisual.Hidden;

                // Тепловые геометрию не меняют — только материал. Класс берётся
                // от того, чем продукт был до нагрева.
                default:
                    return FoodVisual.None;
            }
        }

        /// <summary>Тепловое ли состояние: не меняет геометрию и необратимо.</summary>
        public static bool IsThermal(FoodState s)
        {
            byte v = (byte)s;
            return v >= 40 && v <= 48;
        }

        /// <summary>Состояние с плохой оценкой. Не сбой системы, а узел графа.</summary>
        public static bool IsSpoiled(FoodState s)
            => s == FoodState.Undercooked || s == FoodState.Overcooked
            || s == FoodState.Burnt || s == FoodState.Spoiled;

        /// <summary>
        /// ТЕКУЧЕЕ ЛИ. Такое не лежит на полу предметом: либо собирается в
        /// таре, либо растекается лужей. Ключевая проверка для «раздробил
        /// картошку на полу — осталось пятно».
        /// </summary>
        public static bool IsFlowing(FoodState s)
        {
            FoodVisual v = VisualOf(s);
            return v == FoodVisual.Mass || v == FoodVisual.Liquid || v == FoodVisual.Pile;
        }

        /// <summary>
        /// Чем можно взять. Считается, а не задаётся: кучка всегда лопатой,
        /// жидкость половником, горячее щипцами.
        /// </summary>
        public static FoodHandling HandlingOf(FoodState s, FoodTemp temp, float sizeMeters)
        {
            if (temp == FoodTemp.Hot) return FoodHandling.Tongs;
            if (sizeMeters > 0.6f) return FoodHandling.Crane;

            switch (VisualOf(s))
            {
                case FoodVisual.Liquid: return FoodHandling.Ladle;
                case FoodVisual.Pile:   return FoodHandling.Shovel;
                case FoodVisual.Mass:   return FoodHandling.Shovel;
                default:                return FoodHandling.Hands;
            }
        }

        public static string NameOf(FoodState s)
        {
            switch (s)
            {
                case FoodState.Raw: return "сырое";
                case FoodState.Dirty: return "грязное";
                case FoodState.Washed: return "мытое";
                case FoodState.Peeled: return "чищеное";
                case FoodState.Marinated: return "маринованное";
                case FoodState.Breaded: return "в панировке";
                case FoodState.Pounded: return "отбито";
                case FoodState.Piped: return "отсажено";
                case FoodState.Torched: return "опалено";
                case FoodState.Chilled: return "охлаждено";
                case FoodState.Frozen: return "заморожено";
                case FoodState.Dried: return "подсохло";
                case FoodState.Pieces: return "кусочки";
                case FoodState.Grated: return "тёртое";
                case FoodState.Puree: return "пюре";
                case FoodState.Minced: return "фарш";
                case FoodState.Rolled: return "раскатано";
                case FoodState.Shaped: return "сформовано";
                case FoodState.Boiled: return "варёное";
                case FoodState.Fried: return "жареное";
                case FoodState.Baked: return "запечённое";
                case FoodState.Stewed: return "тушёное";
                case FoodState.Spitted: return "с вертела";
                case FoodState.Smoked: return "копчёное";
                case FoodState.Undercooked: return "НЕДОготовлено";
                case FoodState.Overcooked: return "ПЕРЕготовлено";
                case FoodState.Burnt: return "сгорело";
                case FoodState.InSack: return "в мешке";
                case FoodState.Scooped: return "зачерпнуто";
                case FoodState.Dust: return "пыль";
                case FoodState.PileDry: return "кучка сухая";
                case FoodState.PileSwollen: return "кучка набухшая";
                case FoodState.Dissolved: return "растворилось";
                case FoodState.InContainer: return "в таре";
                case FoodState.Poured: return "налито";
                case FoodState.Melted: return "растоплено";
                case FoodState.Whipped: return "взбито";
                case FoodState.InShell: return "в скорлупе";
                case FoodState.Cracked: return "разбито";
                case FoodState.Yolk: return "желток";
                case FoodState.White: return "белок";
                case FoodState.Carcass: return "туша";
                case FoodState.Butchered: return "разделано";
                case FoodState.Kneaded: return "замешано";
                case FoodState.Proofed: return "подошло";
                case FoodState.Ready: return "готовый";
                case FoodState.Spoiled: return "испортилось";
                default: return s.ToString();
            }
        }

        public static string NameOf(FoodProcess p)
        {
            switch (p)
            {
                case FoodProcess.Wash: return "мыть";
                case FoodProcess.Peel: return "чистить";
                case FoodProcess.Butcher: return "разделывать";
                case FoodProcess.Marinate: return "мариновать";
                case FoodProcess.Bread: return "панировать";
                case FoodProcess.Cut: return "резать";
                case FoodProcess.Grate: return "тереть";
                case FoodProcess.Mash: return "толочь";
                case FoodProcess.Mince: return "молоть в фарш";
                case FoodProcess.Roll: return "раскатывать";
                case FoodProcess.Shape: return "формовать";
                case FoodProcess.Pound: return "отбивать";
                case FoodProcess.Pipe: return "отсаживать";
                case FoodProcess.Boil: return "варить";
                case FoodProcess.Fry: return "жарить";
                case FoodProcess.Bake: return "запекать";
                case FoodProcess.Stew: return "тушить";
                case FoodProcess.Spit: return "жарить на вертеле";
                case FoodProcess.Smoke: return "коптить";
                case FoodProcess.Torch: return "опалять";
                case FoodProcess.Scoop: return "черпать";
                case FoodProcess.Sift: return "просеивать";
                case FoodProcess.Pour: return "лить";
                case FoodProcess.Mix: return "мешать";
                case FoodProcess.Whip: return "взбивать";
                case FoodProcess.Crack: return "разбивать";
                case FoodProcess.Separate: return "отделять белок";
                case FoodProcess.Chill: return "охлаждать";
                case FoodProcess.Freeze: return "замораживать";
                case FoodProcess.Rest: return "дать подойти";
                default: return p.ToString();
            }
        }

        public static string NameOf(FoodFault f)
        {
            switch (f)
            {
                case FoodFault.Dropped: return "уронил на пол";
                case FoodFault.Overcooked: return "переготовлено";
                case FoodFault.Undercooked: return "недоготовлено";
                case FoodFault.Dirty: return "грязный продукт";
                case FoodFault.WrongTool: return "не тот инструмент";
                case FoodFault.Spoiled: return "испортилось";
                case FoodFault.NoVessel: return "готовили не в таре";
                default: return f.ToString();
            }
        }

        public static string NameOf(FoodHandling h)
        {
            switch (h)
            {
                case FoodHandling.Hands: return "руками";
                case FoodHandling.Tongs: return "щипцами";
                case FoodHandling.Shovel: return "лопатой";
                case FoodHandling.Ladle: return "половником";
                case FoodHandling.Crane: return "краном";
                default: return h.ToString();
            }
        }

        /// <summary>Штраф за нарушение в очках итоговой оценки.</summary>
        public static int PenaltyOf(FoodFault f)
        {
            switch (f)
            {
                case FoodFault.Dropped: return 20;
                case FoodFault.Overcooked: return 35;
                case FoodFault.Undercooked: return 30;
                case FoodFault.Dirty: return 25;
                case FoodFault.WrongTool: return 10;
                case FoodFault.Spoiled: return 60;
                case FoodFault.NoVessel: return 40;
                default: return 10;
            }
        }
    }
}
