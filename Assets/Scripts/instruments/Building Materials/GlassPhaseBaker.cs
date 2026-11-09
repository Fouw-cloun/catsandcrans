using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ЗАПЕКАНИЕ РАЗБРОСА ОКОН В ЦВЕТА ВЕРШИН.
///
/// Вешается на объект с мешем фасада. При старте находит отдельные панели
/// внутри меша и раздаёт каждой свою фазу блика через красный канал цвета
/// вершин.
///
/// ═══ ПОЧЕМУ НЕ MaterialPropertyBlock ═══
///
/// Блок свойств — привычный способ раздать объектам разные параметры, и для
/// GPU Instancing он работает. НО В URP основной механизм объединения — SRP
/// Batcher, и рендерер с назначенным блоком из него ВЫПАДАЕТ.
///
/// На одном окне это незаметно. На небоскрёбе из ста этажей это означает сотни
/// объектов, каждый со своим вызовом отрисовки, — то есть ровно та просадка,
/// которой мы избегали.
///
/// Цвет вершин никакого состояния на рендерере не создаёт: он часть меша.
/// SRP Batcher продолжает работать, окна остаются разными.
///
/// ═══ ПОЧЕМУ КЭШ ПО ИСХОДНОМУ МЕШУ ═══
///
/// Сто этажей обычно используют ОДИН И ТОТ ЖЕ меш фасада. Запекать его сто раз
/// значило бы держать в памяти сто почти одинаковых копий и потерять
/// объединение — у каждого этажа стал бы свой меш.
///
/// Поэтому результат кэшируется по исходному мешу: сколько бы этажей его ни
/// использовало, копия создаётся ОДНА и общая.
///
/// А чтобы этажи при этом не выглядели одинаково, шейдер СКЛАДЫВАЕТ две фазы:
/// из цвета вершин (своя у каждой панели) и из позиции объекта (своя у каждого
/// этажа). Панель на 3-м и та же панель на 40-м получают разный блик, при том
/// что меш у них общий.
///
/// ═══ КАК НАХОДЯТСЯ ПАНЕЛИ ═══
///
/// Обходом связности: вершины, соединённые треугольниками, считаются одной
/// панелью. Для фасада из отдельных четырёхугольников это работает точно —
/// панели геометрически не связаны друг с другом.
///
/// Если фасад сварен в единую сетку без разрывов, разбиения не получится, и
/// скрипт честно об этом скажет: тогда фазу надо проставлять при генерации.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[DisallowMultipleComponent]
public class GlassPhaseBaker : MonoBehaviour
{
    [Header("Разброс")]
    [Tooltip("Зерно. У всех фасадов, использующих ОДИН меш, разброс панелей " +
             "получится одинаковым — этажи разводит шейдер по позиции объекта.")]
    [SerializeField] private int _seed = 7788;

    [Tooltip("Разброс яркости между панелями, доля. Пишется в зелёный канал.")]
    [Range(0f, 0.4f)][SerializeField] private float _brightnessJitter = 0.12f;

    [Tooltip("Доля панелей со светом внутри. Пишется в синий канал — шейдер " +
             "может подмешать тёплый цвет.")]
    [Range(0f, 1f)][SerializeField] private float _litChance = 0.08f;

    [Header("Отладка")]
    [SerializeField] private bool _log = true;

    // Кэш на весь процесс: ключ — исходный меш, значение — запечённая копия.
    // Статический, потому что сто этажей делят один меш, и копия им нужна одна.
    private static readonly Dictionary<Mesh, Mesh> Baked = new Dictionary<Mesh, Mesh>();

    private void Awake() => Bake();

    public void Bake()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh src = mf.sharedMesh;

        if (src == null)
        {
            Debug.LogWarning($"[Окна] «{name}»: меша нет", this);
            return;
        }

        // Уже запекали этот меш — берём готовый. Ничего не считаем и не
        // выделяем.
        if (Baked.TryGetValue(src, out Mesh cached) && cached != null)
        {
            mf.sharedMesh = cached;
            return;
        }

        Mesh copy = BuildBaked(src, out int panes);
        if (copy == null) return;

        Baked[src] = copy;
        mf.sharedMesh = copy;

        if (_log)
            Debug.Log($"[Окна] «{name}»: запечён меш «{src.name}», панелей {panes}. " +
                      "Копия общая для всех объектов с этим мешем.", this);
    }

    private Mesh BuildBaked(Mesh src, out int paneCount)
    {
        paneCount = 0;

        Vector3[] verts = src.vertices;
        if (verts.Length == 0) return null;

        // ── связность ──
        // Простое объединение множеств: вершины одного треугольника попадают в
        // одну группу. Панели, не соединённые геометрией, останутся разными.
        int[] parent = new int[verts.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        for (int sub = 0; sub < src.subMeshCount; sub++)
        {
            int[] tris = src.GetTriangles(sub);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Union(parent, tris[t], tris[t + 1]);
                Union(parent, tris[t + 1], tris[t + 2]);
            }
        }

        // ── раздача фазы по группам ──
        Random.State prev = Random.state;
        Random.InitState(_seed);

        Dictionary<int, Color> byRoot = new Dictionary<int, Color>();
        Color[] colors = new Color[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            int root = Find(parent, i);

            if (!byRoot.TryGetValue(root, out Color c))
            {
                float phase = Random.value;
                float bright = 1f + Random.Range(-_brightnessJitter, _brightnessJitter);
                float lit = Random.value < _litChance ? 1f : 0f;

                // R — фаза блика, G — яркость, B — «свет внутри».
                // Alpha не трогаем: он может нести что-то от исходного меша.
                c = new Color(phase, Mathf.Clamp01(bright * 0.5f), lit, 1f);
                byRoot[root] = c;
            }

            colors[i] = c;
        }

        Random.state = prev;
        paneCount = byRoot.Count;

        if (paneCount <= 1)
        {
            Debug.LogWarning($"[Окна] «{name}»: меш «{src.name}» — одна сплошная " +
                             "сетка, панели не разделяются. Разброса не будет. " +
                             "Либо разрежь панели в модели, либо проставляй " +
                             "фазу при генерации фасада.", this);
        }

        // Копию делаем ОБЯЗАТЕЛЬНО: правка sharedMesh изменила бы сам ассет
        // модели на диске, и это разошлось бы по всему проекту.
        Mesh copy = Instantiate(src);
        copy.name = src.name + "_glassPhase";
        copy.colors = colors;

        // В сцене не сохраняем: меш пересоздаётся при запуске, и копить его в
        // сцене незачем.
        copy.hideFlags = HideFlags.DontSave;

        return copy;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];   // сжатие пути
            i = parent[i];
        }
        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a), rb = Find(parent, b);
        if (ra != rb) parent[rb] = ra;
    }

#if UNITY_EDITOR
    [ContextMenu("Запечь заново")]
    private void EditorBake()
    {
        // Сбрасываем кэш: иначе после правки зерна вернётся старая копия.
        Baked.Clear();
        Bake();
    }
#endif
}
