# Источники и решения

## Предоставленные материалы

Из `dust particles in unity.unitypackage` извлечены и изучены StepDust.prefab, CloudMaterial.mat, ToonShader.shadergraph и метаданные cloud.fbx. Пакет использован как технический/визуальный ориентир и источник структуры сериализации Particle System. Исходные FBX, Shader Graph и материал не входят в новую поставку. Новые меши, карты данных и HLSL подготовлены для этой системы.

Видео проверены выборочно по кадрам, а не полностью просмотрены со звуком. Всего 48 опорных кадров из четырёх файлов:

- VFX Cartoon Dust Unity 3d — 4.04 с: сопоставлены начало, расширение, отделение мелких клубов и исчезновение.
- Animated Dust Particles, Part 1 — 23:48.9: общий результат и построение маски в разные моменты урока.
- Part 2 — 28:03.1: промежуточные графы, вершинные данные и вариативность формы.
- Part 3 — 12:13.1: вид результата и этапы Shader Graph.

Разрешение видео 640×360 не позволяет надёжно восстановить все мелкие значения узлов. В новой версии используется собственная реализация; точное воспроизведение графов из видео не заявляется.

## Веб-первоисточники

- [Sunny Valley Studio — Mario-Like Footstep Dust in Unity (URP)](https://www.patreon.com/sunnyvalleystudio/posts/unitytip-mario-141197017): ориентир на округлую mesh-пыль, мировое пространство и эмиссию по пройденному расстоянию. Для лап в нашей системе выбран отдельный путь с событиями анимации, чтобы привязать выброс к контакту.
- [Unity 6 — Particle System Renderer](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysRendererModule.html): режимы мешей/карт, сортировка и условия instancing.
- [Unity — SetActiveVertexStreams](https://docs.unity3d.com/jp/current/ScriptReference/ParticleSystemRenderer.SetActiveVertexStreams.html): контракт потока вершин. Наш порядок: Position, Normal, Color, UV, AgePercent, StableRandomX; UV/age/random занимают TEXCOORD0.xy/z/w.
- [UnityCsReference — ParticleSystemEnums](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/ParticleSystem/Managed/ParticleSystemEnums.cs) и [bindings](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/ParticleSystem/ScriptBindings/ParticleSystemModules.bindings.cs): сверка имён API и перечислений. Ветка master не заменяет компиляцию против конкретной версии 6000.x.
- [Unity — Limit Velocity over Lifetime](https://docs.unity3d.com/jp/current/ScriptReference/ParticleSystem.LimitVelocityOverLifetimeModule.html): сопротивление для затухания стартового импульса.
- [Jason Keyser — Artistic Principles of VFX](https://realtimevfx.com/t/artistic-principles-of-vfx/4081): авторская отправная точка по художественным принципам VFX.
- [GDC — Artistic Principles of VFX](https://www.gdcvault.com/play/1023943/Visual-Effects-Bootcamp-Artistic-Principles): описание доклада; полная лекция в этом проходе не просмотрена.

Короткий импульс, разделение крупных/мелких форм, задержки вторичного движения и различия веса в этом пакете — наши авторские настройки под описанную игру. Они не выдаются за параметры из лекций или проверенный визуальный стиль готовой игры.
