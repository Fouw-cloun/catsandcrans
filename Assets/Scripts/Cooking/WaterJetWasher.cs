using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// МОЙКА ПРОДУКТОВ СТРУЁЙ.
///
/// ГДЕ ВЕШАТЬ: на брандспойт ИЛИ на дочерний JetOrigin — компонент сам найдёт
/// WaterJet: сперва на себе, потом в детях, потом в родителях. RequireComponent
/// тут был бы вреден: у NozzleGunItem струя живёт на ДОЧЕРНЕМ объекте, и мойка,
/// повешенная на ствол, молча не находила бы её и ничего не делала.
///
/// МОЙКА ИДЁТ ПО ВРЕМЕНИ, а не по ведению: держишь струю на продукте
/// секунду-другую, и он отмывается. Слабый напор моет медленнее.
///
/// СЕТЬ:
///   сервер  — копит прогресс мойки (единственный источник правды);
///   клиенты — сами ищут продукты под струёй и рисуют капли.
///
/// Ни одного RPC: путь струи каждый клиент строит сам в WaterJet.Update из уже
/// синхронизированного состояния гидранта, а чистота приезжает в Progress01.
/// </summary>
public class WaterJetWasher : NetworkBehaviour
{
    [Header("Что делает струя")]
    [Tooltip("Обработка, которую применяет вода. Обычно — мытьё.")]
    [SerializeField] private ProcessDef _washProcess;

    [Tooltip("Секунд под ПОЛНЫМ напором, чтобы отмыть продукт целиком")]
    [Min(0.2f)][SerializeField] private float _secondsToWash = 1.5f;

    [Tooltip("Ниже этого напора струя только мочит, но не отмывает")]
    [Range(0f, 1f)][SerializeField] private float _minFlow = 0.2f;

    [Header("Струя")]
    [Tooltip("Оставь пустым — найдётся сам: на себе, в детях, в родителях.")]
    [SerializeField] private WaterJet _jet;

    [Tooltip("Радиус захвата вокруг линии струи")]
    [SerializeField] private float _hitRadius = 0.35f;

    [Tooltip("Слой еды. Убери отсюда всё остальное — струя длинная, и лишние " +
             "коллайдеры на её пути стоят дорого.")]
    [SerializeField] private LayerMask _foodMask = ~0;

    [Tooltip("Как часто пересканировать струю, сек")]
    [SerializeField] private float _scanInterval = 0.08f;

    [Header("Капли с продукта")]
    [Tooltip("Цвет капель с грязного продукта")]
    [SerializeField] private Color _dirtyColor = new Color(0.34f, 0.24f, 0.13f);

    [Tooltip("Цвет капель с чистого продукта")]
    [SerializeField] private Color _cleanColor = new Color(0.55f, 0.8f, 0.95f);

    [Tooltip("Капель в секунду с одного продукта. Это КАПАНЬЕ, а не брызги — " +
             "держи небольшим.")]
    [SerializeField] private float _dripsPerSecond = 18f;

    [Tooltip("Размер капли, метры")]
    [SerializeField] private float _dripSize = 0.025f;

    [Tooltip("Насколько быстро капли падают. 0.6 = вяло стекают, 1 = как камень.")]
    [Range(0.1f, 2f)][SerializeField] private float _dripGravity = 0.6f;

    [Tooltip("Сколько живёт капля, секунд")]
    [SerializeField] private Vector2 _dripLifetime = new Vector2(1.2f, 2.2f);

    [Tooltip("Капли отскакивают от пола. ВЫКЛ по умолчанию: продукт часто лежит " +
             "на столе, и капля, родившаяся у самой поверхности, засчитывалась " +
             "бы как столкнувшаяся и гасла в тот же кадр.")]
    [SerializeField] private bool _dripsCollide = false;

    [Header("ОТЛАДКА (выключить в релизе)")]
    [Tooltip("Текст в углу экрана: что именно мешает мойке")]
    [SerializeField] private bool _debugHud = true;

    private ParticleSystem _drips;

    /// <summary>Продукты под струёй. Обновляется по таймеру, а капли льются
    /// каждый кадр — сканировать параболу 60 раз в секунду незачем.</summary>
    private readonly List<FoodItem> _wet = new List<FoodItem>();

    private readonly Collider[] _hits = new Collider[24];
    private readonly HashSet<int> _seen = new HashSet<int>();

    private float _nextScan;
    private float _lastScan;
    private float _dripCarry;
    private bool _logFirstDrip = true;

    private string _status = "—";
    private Color _statusColor = Color.gray;

    private void Awake()
    {
        ResolveJet();
        CreateDrips();
    }

    private void OnDestroy()
    {
        // Система капель не дочерняя — сама она не удалится.
        if (_drips != null) Destroy(_drips.gameObject);
    }

    /// <summary>
    /// Струя может быть где угодно относительно этого компонента: на самом
    /// объекте (выход гидранта) или на дочернем JetOrigin (брандспойт).
    /// </summary>
    private void ResolveJet()
    {
        if (_jet != null) return;
        _jet = GetComponent<WaterJet>();
        if (_jet == null) _jet = GetComponentInChildren<WaterJet>(true);
        if (_jet == null) _jet = GetComponentInParent<WaterJet>();

        if (_jet == null)
            Debug.LogError($"[WaterJetWasher] На {name} и вокруг не найден WaterJet. " +
                           "Назначь его в поле Jet вручную.", this);
    }

    // ───────────────────────── ОСНОВНОЙ ЦИКЛ ─────────────────────────

    private void Update()
    {
        if (_jet == null) { SetStatus("не найден WaterJet", Color.red); return; }
        if (_washProcess == null) { SetStatus("не назначен Wash Process", Color.red); return; }

        float flow = _jet.Flow01;

        if (!_jet.IsFlowing)
        {
            SetStatus("струя выключена", Color.gray);
            _wet.Clear();
            _lastScan = 0f;
            return;
        }

        if (flow < _minFlow)
        {
            SetStatus($"напор мал: {flow:0.00} < {_minFlow:0.00} — открой гайку сильнее",
                      new Color(1f, 0.6f, 0f));
            _wet.Clear();
            _lastScan = 0f;
            return;
        }

        if (Time.time >= _nextScan)
        {
            float dt = _lastScan > 0f
                ? Mathf.Min(Time.time - _lastScan, 0.25f)
                : _scanInterval;
            _lastScan = Time.time;
            _nextScan = Time.time + _scanInterval;

            Scan();

            // Прогресс — только сервер. У остальных это просто картинка.
            if (base.IsServerInitialized) ApplyWash(dt);
            else UpdateStatusForClient();
        }

        EmitDrips(Mathf.Min(Time.deltaTime, 0.05f));
    }

    /// <summary>Идём по точкам параболы и собираем всё съедобное вокруг.</summary>
    private void Scan()
    {
        _wet.Clear();
        _seen.Clear();

        Vector3[] pts = _jet.PathPointsWorld;
        int count = _jet.PathCount;
        if (pts == null || count < 2) return;

        for (int i = 1; i < count; i++)
        {
            int n = Physics.OverlapCapsuleNonAlloc(pts[i - 1], pts[i], _hitRadius,
                                                   _hits, _foodMask,
                                                   QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                FoodItem f = _hits[k].GetComponentInParent<FoodItem>();
                if (f == null || !f.IsSpawned || f.Consumed) continue;

                // Один продукт легко попадает под несколько сегментов струи —
                // без этой проверки он получал бы двойную и тройную порцию.
                if (!_seen.Add(f.NetworkObject.ObjectId)) continue;

                _wet.Add(f);
            }
        }
    }

    [Server]
    private void ApplyWash(float dt)
    {
        if (_wet.Count == 0)
        {
            SetStatus("льёт, но еды под струёй нет — проверь Food Mask и радиус",
                      new Color(1f, 0.6f, 0f));
            return;
        }

        float flow = _jet.Flow01;
        float delta = dt / Mathf.Max(0.2f, _secondsToWash) * flow;

        int washing = 0;
        float shown = 0f;
        string blocked = null;

        for (int i = 0; i < _wet.Count; i++)
        {
            FoodItem f = _wet[i];
            if (f == null || !f.IsSpawned) continue;

            if (!f.CanApplyNow(_washProcess))
            {
                if (blocked == null && f.Form != null)
                    blocked = f.Form.GetLabel(f.StateIndex);
                continue;
            }

            // Качество берём от напора: жидкая струйка отмоет, но так себе.
            f.ServerAddProgress(_washProcess, delta, flow);
            washing++;
            shown = f.Progress01;
        }

        if (washing > 0)
            SetStatus($"МОЕТ: {washing} шт, прогресс {shown * 100f:0}%", Color.green);
        else if (blocked != null)
            SetStatus($"еда есть, но из состояния «{blocked}» нет хода по " +
                      $"«{_washProcess.DisplayName}» — дорисуй ребро в графе формы",
                      new Color(0.4f, 0.7f, 1f));
        else
            SetStatus("еда под струёй есть, но обработать нечего", Color.yellow);
    }

    /// <summary>У клиента прогресса нет, но показать что-то полезное можно.</summary>
    private void UpdateStatusForClient()
    {
        if (_wet.Count == 0) SetStatus("льёт, еды под струёй нет", new Color(1f, 0.6f, 0f));
        else SetStatus($"под струёй: {_wet.Count} шт", Color.green);
    }

    // ───────────────────────── КАПЛИ ─────────────────────────

    /// <summary>
    /// Капли считаются локально у каждого клиента. Цвет берётся из чистоты
    /// продукта: грязный даёт бурую воду, отмытый — голубую. Чистота уже
    /// синхронизирована в Progress01, гнать её отдельно не нужно.
    /// </summary>
    private void EmitDrips(float dt)
    {
        if (_drips == null || _wet.Count == 0) return;

        // Тащим систему за собой. На живые частицы это не влияет — они в
        // мировом пространстве и остаются на месте, — зато границы отрисовки
        // считаются вокруг реального места действия, а не вокруг нуля мира.
        _drips.transform.position = transform.position;

        _dripCarry += _dripsPerSecond * _jet.Flow01 * _wet.Count * dt;
        int total = Mathf.FloorToInt(_dripCarry);
        if (total <= 0) return;
        _dripCarry -= total;

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < total; i++)
        {
            FoodItem f = _wet[Random.Range(0, _wet.Count)];
            if (f == null || !f.IsSpawned) continue;

            Collider col = f.GetComponentInChildren<Collider>();
            if (col == null) continue;

            Bounds b = col.bounds;

            // Рождаем на НИЖНЕЙ ГРАНИ продукта, ближе к его оси.
            // Не в объёме — частица внутри коллайдера при включённых
            // столкновениях гаснет в тот же кадр. И не ниже — под картошкой
            // обычно стол, и капля родилась бы внутри столешницы.
            Vector3 at = new Vector3(
                Mathf.Lerp(b.center.x, Random.Range(b.min.x, b.max.x), 0.7f),
                b.min.y,
                Mathf.Lerp(b.center.z, Random.Range(b.min.z, b.max.z), 0.7f));

            ep.position = at;
            ep.startColor = DripColor(f);
            // Нижняя отсечка: со старым значением из инспектора капля выходила
            // в 1 см и терялась на фоне. Пусть мельче 1.5 см не бывает.
            ep.startSize = Mathf.Max(0.015f, _dripSize) * Random.Range(0.6f, 1.5f);
            ep.startLifetime = Random.Range(_dripLifetime.x, _dripLifetime.y);
            // Почти без начальной скорости: капля должна СТЕКАТЬ, а не
            // выстреливать. Разгон дальше даёт гравитация.
            ep.velocity = new Vector3(Random.Range(-0.05f, 0.05f),
                                      Random.Range(-0.12f, -0.02f),
                                      Random.Range(-0.05f, 0.05f));

            _drips.Emit(ep, 1);

            // Разовый лог: однозначно показывает, ОТКУДА летят капли.
            // Если координаты совпадают с картошкой — значит дело в отрисовке,
            // а не в позиции.
            if (_logFirstDrip)
            {
                _logFirstDrip = false;
                Debug.Log($"[WaterJetWasher] Первая капля: {at} у «{f.name}», " +
                          $"цвет {ep.startColor}, размер {ep.startSize:0.###}, " +
                          $"материал {(_drips.GetComponent<ParticleSystemRenderer>().sharedMaterial != null ? "есть" : "НЕТ")}");
            }
        }
    }

    /// <summary>
    /// Чем чище продукт, тем голубее вода.
    ///
    /// Если продукт уже отмыт, Progress01 сброшен в ноль — по нему одному
    /// судить нельзя, иначе чистая картошка снова закапала бы бурым. Поэтому
    /// сперва спрашиваем, есть ли вообще куда мыть: некуда — значит чистый.
    /// </summary>
    private Color DripColor(FoodItem f)
    {
        float clean = f.CanApplyNow(_washProcess) ? f.Progress01 : 1f;
        return Color.Lerp(_dirtyColor, _cleanColor, clean);
    }

    /// <summary>
    /// Система капель собирается из кода — настраивать в инспекторе нечего.
    /// Создаётся в рантайме, а не берётся из сцены: префаб не может хранить
    /// ссылку на сценный объект, и при спавне брандспойта она стала бы null.
    /// </summary>
    private void CreateDrips()
    {
        GameObject go = new GameObject("WaterDrips (auto)");

        // ВЫКЛЮЧЕННЫМ. Система начинает играть сразу при AddComponent, и часть
        // свойств на играющей менять нельзя — Unity сыплет ошибки в консоль.
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 2000;

        // КРИТИЧНО. При Local капли поехали бы за брандспойтом по всей комнате.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;          // задаём поштучно
        main.startSize = _dripSize;    // тоже поштучно
        main.startLifetime = 1f;
        main.gravityModifier = _dripGravity;

        // КРИТИЧНО при сцене далеко от нуля координат.
        // По умолчанию режим отсечения Automatic: система, чей ТРАНСФОРМ вне
        // экрана, перестаёт симулироваться и рисоваться. Мы создаём систему в
        // нуле мира, а сцена может стоять за километры — и Unity молча гасит
        // её целиком, хотя сами частицы рождаются рядом с игроком.
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;            // эмиссия только ручная

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;         // позицию задаём вручную

        // Тают в самом конце, а не сразу: капля должна успеть долететь.
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.85f, 0.7f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = g;

        if (_dripsCollide)
        {
            ParticleSystem.CollisionModule cm = ps.collision;
            cm.enabled = true;
            cm.type = ParticleSystemCollisionType.World;
            cm.mode = ParticleSystemCollisionMode.Collision3D;
            cm.quality = ParticleSystemCollisionQuality.Low;
            cm.dampen = 0.8f;
            cm.bounce = 0.05f;
            cm.lifetimeLoss = 0.5f;    // разбивается о пол
        }

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();

        // МЕШ, а не билборд. Билборд рисует текстурированный квад, и без
        // текстуры в материале его запросто не видно. Готовая геометрия
        // рисуется всегда — тот же приём, что у ошмётков болгарки.
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = BuildDropMesh();
        r.alignment = ParticleSystemRenderSpace.World;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.material = BuildDripMaterial();

        go.SetActive(true);
        ps.Play();
        _drips = ps;
    }

    /// <summary>
    /// Капля — вытянутый вниз восьмигранник. Объёмная, а не плоская: капли
    /// кувыркаются и падают, плоская пропадала бы, повернувшись ребром.
    /// </summary>
    private static Mesh BuildDropMesh()
    {
        Mesh m = new Mesh { name = "WaterDrop" };

        const float r = 0.5f, top = 0.7f, bottom = -1.1f;
        Vector3[] ring = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f;
            ring[i] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        Vector3 up = new Vector3(0f, top, 0f);
        Vector3 dn = new Vector3(0f, bottom, 0f);

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        for (int i = 0; i < 4; i++)
        {
            Vector3 a = ring[i];
            Vector3 b = ring[(i + 1) % 4];

            int baseIdx = verts.Count;
            verts.Add(up); verts.Add(a); verts.Add(b);
            tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);

            baseIdx = verts.Count;
            verts.Add(dn); verts.Add(b); verts.Add(a);
            tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
        }

        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Материал должен умножать на цвет вершин — через него
    /// подставляется цвет грязной или чистой воды.</summary>
    private static Material BuildDripMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");

        if (sh == null)
        {
            Debug.LogWarning("[WaterJetWasher] Не найден шейдер для частиц — " +
                             "капли будут белыми.");
            return null;
        }

        Material m = new Material(sh) { name = "WaterDrips (auto)" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);

        // Шейдер для ЧАСТИЦ умножает на цвет вершин — через него подставляется
        // цвет каждой капли. Обычный Unlit цвет вершин игнорирует, и тогда все
        // капли будут одного цвета. Пишем, что нашлось, чтобы это не гадать.
        Debug.Log($"[WaterJetWasher] Шейдер капель: {sh.name}");
        return m;
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void SetStatus(string text, Color color)
    {
        _status = text;
        _statusColor = color;
    }

    private void OnGUI()
    {
        if (!_debugHud) return;
        if (_jet == null || !_jet.IsFlowing) return;

        GUIStyle st = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14
        };
        st.normal.textColor = _statusColor;

        int alive = _drips != null ? _drips.particleCount : -1;

        GUI.Box(new Rect(12f, 66f, 640f, 44f),
                $"Мойка → {_status}\n" +
                $"напор {_jet.Flow01:0.00}   под струёй {_wet.Count} шт   " +
                $"капель в воздухе {alive}", st);
    }
}
