# Обновление 0.2 и аудит

Прочитаны страницы проектов: [BotW Toon Shader](https://github.com/ciro-unity/BotW-ToonShader) — ориентир освещения поверхностей, не готовая система дыма; [URP Smoke Lighting](https://github.com/peeweek/Unity-URP-SmokeLighting) — normal/6-way/ASTA подходы, исходный проект заявлен под Unity 2021.2; [URP Spritesheets](https://github.com/Gaxil/Unity-URP-Spritesheets) — подходы с flipbook и motion vectors. Полный исходный код этих репозиториев в этом проходе не разбирался и в пакет не копировался. Тяжёлое 6-way освещение и motion-vector interpolation сейчас не добавлялись.

[Авторский stylized smoke tutorial](https://www.youtube.com/watch?v=dPJQuD93-Ks) найден как альтернативный mesh+erosion подход. Из предоставленного 32.58-секундного ролика изучены 9 опорных кадров: результат, спиральная текстура и схема smoke+dissolve. Это выборочное визуальное изучение, без расшифровки звука.

Сохранена гибридная реализация Shuriken: объёмные клубы плюс несколько карт для рассеивания. Вихревая маска в B-канале атласа создана заново; это не полноценный анимированный 4x4 flipbook из референса. Количество выборок атласа — одна на пиксель, добавлены поворот UV и изменение порога по возрасту.

Ниже проверка данных каждого слоя: положительный размер/lifetime/alpha, Two Colors, включённая ненулевая эмиссия. OK не означает проверенную отрисовку в Unity. Дополнительно общий валидатор проверяет материалы, ссылки, меши, записи bursts и синтаксис C#.

| Префаб / слой | Max particles | Статические данные |
|---|---:|---|
| Cooking_Steam / Core | 64 | OK |
| Cooking_Steam / Wisps | 64 | OK |
| Drop_Heavy / Contact | 21 | OK |
| Drop_Heavy / Core | 27 | OK |
| Drop_Heavy / Wisps | 15 | OK |
| Drop_Heavy / Chips | 12 | OK |
| Drop_Small / Core | 12 | OK |
| Drop_Small / Wisps | 8 | OK |
| Exhaust_Idle / Core | 64 | OK |
| Exhaust_Idle / Wisps | 64 | OK |
| Exhaust_Load / Core | 64 | OK |
| Exhaust_Load / Wisps | 64 | OK |
| Flour_Puff / Core | 18 | OK |
| Flour_Puff / Wisps | 15 | OK |
| Footstep_Run / Core | 9 | OK |
| Footstep_Run / Contact | 8 | OK |
| Footstep_Run / Wisps | 8 | OK |
| Footstep_Soft / Core | 8 | OK |
| Footstep_Soft / Wisps | 8 | OK |
| Jump_Takeoff / Contact | 12 | OK |
| Jump_Takeoff / Core | 12 | OK |
| Jump_Takeoff / Wisps | 8 | OK |
| Land_Heavy / Contact | 24 | OK |
| Land_Heavy / Core | 24 | OK |
| Land_Heavy / Wisps | 15 | OK |
| Land_Heavy / Chips | 12 | OK |
| Land_Light / Contact | 15 | OK |
| Land_Light / Core | 15 | OK |
| Land_Light / Wisps | 9 | OK |
| Tool_Hit / Core | 9 | OK |
| Tool_Hit / Chips | 9 | OK |
| Tool_Hit / Wisps | 8 | OK |
| Wheel_Rolling / Core | 64 | OK |
| Wheel_Rolling / Wisps | 64 | OK |
| Wheel_Skid / Contact | 15 | OK |
| Wheel_Skid / Core | 18 | OK |
| Wheel_Skid / Wisps | 12 | OK |
