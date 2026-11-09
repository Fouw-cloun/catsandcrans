using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// КОРЁЖИТ И ВЫПРАВЛЯЕТ МЕШИ. Вешается на объект, который может разорвать —
/// кастрюлю, бак, что угодно.
///
/// ПОЧЕМУ БЕЗ ОТДЕЛЬНОЙ МОДЕЛИ ПОВРЕЖДЁННОЙ ПОСУДЫ:
///   вмятины считаются прямо по вершинам исходного меша — шум плюс выброс
///   наружу от оси. Моделировать «рваную кастрюлю» не надо, и это работает
///   с любой посудой, которую ты добавишь потом.
///
///   А главное — сварка получается бесплатно: она просто ведёт вершины обратно
///   к исходным. Никакого второго набора моделей и никакой возни со стыковкой
///   «сломанной» и «целой» версии.
///
/// СЕТЬ: наружу уходят зерно случайности и степень повреждения. Сами вершины
/// каждый клиент считает сам и получает ровно те же вмятины — генератор
/// детерминированный.
/// </summary>
public class DeformableMesh : NetworkBehaviour
{
    [Header("Что корёжить")]
    [Tooltip("Меши корпуса. ПУСТО = собрать все из детей.")]
    [SerializeField] private MeshFilter[] _targets;

    [Header("Сила повреждения")]
    [Tooltip("На сколько выпирает наружу, в долях радиуса объекта")]
    [Range(0f, 0.4f)][SerializeField] private float _bulge = 0.09f;

    [Tooltip("Крупные вмятины и выпуклости")]
    [Range(0f, 0.4f)][SerializeField] private float _dents = 0.07f;

    [Tooltip("Мелкая рваность. Считается от ПОЗИЦИИ вершины, а не случайно, " +
             "иначе рвутся швы меша.")]
    [Range(0f, 0.15f)][SerializeField] private float _jagged = 0.02f;

    [Tooltip("Крупность вмятин: меньше — крупнее пятна")]
    [SerializeField] private float _noiseScale = 5f;

    [Tooltip("Насколько сильнее рвёт вверху. Разрывает по слабому месту — " +
             "у горловины, а не по дну.")]
    [Range(0f, 1f)][SerializeField] private float _topBias = 0.65f;

    [Tooltip("Дно (доля высоты снизу) не деформируется ВООБЩЕ — жёсткая " +
             "граница, а не подбираемое число. Раньше даже с полным _topBias " +
             "дно получало 35% деформации от общей силы, кастрюлю могло " +
             "оторвать от собственного основания. Ниже этой границы смещение " +
             "плавно сходит к нулю.")]
    [Range(0f, 0.3f)][SerializeField] private float _bottomProtect = 0.12f;

    [Header("Сила (новая кастрюля)")]
    [Tooltip("Общий множитель силы. 1 — как было; 1.8 — заметно сильнее корёжит.")]
    [Range(0.2f, 4f)][SerializeField] private float _strength = 1.8f;
    [Tooltip("Сколько крупных вмятин-«ударов» по бокам (как от взрыва изнутри — рваные провалы)")]
    [Range(0, 8)][SerializeField] private int _bigDents = 4;
    [Tooltip("Глубина крупной вмятины, доля радиуса")]
    [Range(0f, 0.5f)][SerializeField] private float _bigDentDepth = 0.2f;
    [Tooltip("Низкополигональную модель дробить, пока вершин меньше этого — иначе мять нечего " +
             "(у восьмигранной кастрюли пара сотен вершин). 0 — не дробить.")]
    [SerializeField] private int _subdivideBelow = 3000;
    [Range(0, 3)][SerializeField] private int _maxSubdivisions = 2;

    [Header("Сварка")]
    [Tooltip("Секунд непрерывной сварки, чтобы выправить полностью")]
    [Min(0.5f)][SerializeField] private float _secondsToWeld = 6f;

    [Header("Молоток")]
    [Tooltip("На сколько выправляет форму один удар молотком, доля от " +
             "полного восстановления. При 0.12 — примерно 8-9 ударов на " +
             "полностью раскорёженную кастрюлю.")]
    [Range(0.01f, 0.5f)][SerializeField] private float _hammerStepPerHit = 0.12f;

    // ───────── сеть ─────────
    /// <summary>0 = цело, 255 = раскорёжено полностью.</summary>
    private readonly SyncVar<byte> _damage = new SyncVar<byte>(0);

    /// <summary>Зерно вмятин. Одинаковое у всех — значит и вмятины одинаковые.</summary>
    private readonly SyncVar<int> _seed = new SyncVar<int>(0);

    private readonly List<Mesh> _work = new List<Mesh>();       // рабочие копии
    private readonly List<Vector3[]> _clean = new List<Vector3[]>();  // исходные вершины
    private readonly List<Vector3[]> _broken = new List<Vector3[]>(); // раскорёженные
    private readonly List<Matrix4x4> _toRoot = new List<Matrix4x4>();   // меш → оси корня

    private float _weld;             // прогресс сварки на сервере
    private byte _appliedDamage = 255;
    private int _appliedSeed;

    public bool IsBroken => _damage.Value > 0;
    public float Damage01 => _damage.Value / 255f;

    /// <summary>Секунд сварки до полного восстановления — нужно инструменту.</summary>
    public float SecondsToWeld => _secondsToWeld;

    /// <summary>Доля восстановления за один удар молотком — нужно инструменту
    /// для расчёта, сколько ударов ещё осталось.</summary>
    public float HammerStepPerHit => _hammerStepPerHit;

    /// <summary>Форма полностью выправлена. И сварка, и молоток зовут его
    /// одинаково — общий финальный код, без дублей.</summary>
    public event System.Action Repaired;

    private void Awake()
    {
        if (_targets == null || _targets.Length == 0)
        {
            var list = new List<MeshFilter>();
            foreach (MeshFilter f in GetComponentsInChildren<MeshFilter>(true))
                if (PotBodyFilter.IsBody(f, transform)) list.Add(f);
            _targets = list.ToArray();
        }

        // Работаем на КОПИЯХ мешей: исходные — общие ассеты, и правка вершин
        // в них покорёжила бы вообще всю посуду в проекте, включая ту, что
        // лежит на полке в другой сцене.
        foreach (MeshFilter mf in _targets)
        {
            if (mf == null || mf.sharedMesh == null) continue;

            // Unity по умолчанию выгружает геометрию из памяти после загрузки
            // на видеокарту, и вершины становятся недоступны. Без этой проверки
            // в консоль сыплется поток невнятных ошибок про isReadable, из
            // которых непонятно, что чинить.
            if (!mf.sharedMesh.isReadable)
            {
                Debug.LogError(
                    $"[Деформация] у меша «{mf.sharedMesh.name}» выключен Read/Write.\n" +
                    "Выдели модель в Project, поставь галку Read/Write Enabled " +
                    "(раздел Model) и нажми Apply. Без неё корёжить нечего.", this);
                continue;
            }

            Mesh copy = Instantiate(mf.sharedMesh);
            copy.name = mf.sharedMesh.name + " (deformable)";
            for (int k = 0; k < _maxSubdivisions && _subdivideBelow > 0 && copy.vertexCount < _subdivideBelow; k++)
                if (!Subdivide(copy)) break;
            copy.MarkDynamic();
            mf.sharedMesh = copy;

            _work.Add(copy);
            _clean.Add(copy.vertices);
            _broken.Add(null);
            _toRoot.Add(transform.worldToLocalMatrix * mf.transform.localToWorldMatrix);
        }
    }

    private void OnDestroy()
    {
        foreach (Mesh m in _work) if (m != null) Destroy(m);
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _damage.OnChange += OnDamageChanged;
        _seed.OnChange += OnSeedChanged;
        Apply();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _damage.OnChange -= OnDamageChanged;
        _seed.OnChange -= OnSeedChanged;
    }

    private void OnDamageChanged(byte prev, byte next, bool asServer)
    {
        // У ХОСТА OnChange приходит дважды: asServer = true, потом false.
        if (asServer && base.IsClientInitialized) return;
        Apply();
    }

    private void OnSeedChanged(int prev, int next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;
        for (int i = 0; i < _broken.Count; i++) _broken[i] = null;   // пересчитать
        Apply();
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    /// <summary>Раскорёжить. Зерно решает, куда лягут вмятины.</summary>
    [Server]
    public void ServerDamage(int seed)
    {
        _seed.Value = seed;
        _damage.Value = 255;
        _weld = 0f;
    }

    /// <summary>
    /// Варить. Возвращает true, когда выправлено до конца.
    /// Инструмент зовёт это каждый кадр, пока держит дугу на объекте.
    /// Внутри — та же общая логика, что и у удара молотком: сварка просто
    /// подаёт очень маленькие шаги прогресса очень часто, вместо одного
    /// крупного шага раз в удар.
    /// </summary>
    [Server]
    public bool ServerWeld(float dt)
    {
        if (_damage.Value == 0) return false;
        return ApplyProgress(dt / Mathf.Max(0.5f, _secondsToWeld));
    }

    /// <summary>
    /// Удар молотком. Возвращает true, когда форма выправлена до конца.
    /// В отличие от сварки, зовётся ДИСКРЕТНО — один раз на удар, а не
    /// каждый кадр: молотком бьют, а не держат прижатым.
    /// </summary>
    [Server]
    public bool ServerHammerHit()
    {
        if (_damage.Value == 0) return false;
        return ApplyProgress(_hammerStepPerHit);
    }

    /// <summary>
    /// Общий шаг прогресса для сварки и молотка. Раньше эта логика жила
    /// только в ServerWeld — вынесена, чтобы оба инструмента чинили форму
    /// ОДИНАКОВО и одинаково избегали классического бага округления.
    /// </summary>
    [Server]
    private bool ApplyProgress(float step)
    {
        _weld = Mathf.Clamp01(_weld + step);

        if (_weld < 1f)
        {
            // НЕ ДАЁМ ДОЙТИ ДО НУЛЯ раньше времени.
            //
            // Прогресс огрубляется до байта, и у самого конца он округлялся
            // в 0 ещё до того, как починка засчитана. IsBroken становился
            // ложным, вызывающий на следующем кадре выходил досрочно — и
            // событие Repaired не срабатывало никогда. Геометрия чинилась,
            // а кастрюля навсегда оставалась «пробитой».
            byte d = (byte)Mathf.Max(1, Mathf.RoundToInt((1f - _weld) * 255f));

            // Не дёргаем SyncVar на каждый кадр — только на заметное изменение.
            if (Mathf.Abs(d - _damage.Value) >= 4) _damage.Value = d;
            return false;
        }

        _damage.Value = 0;
        _weld = 0f;
        Repaired?.Invoke();
        return true;
    }

    // ───────────────────────── ГЕОМЕТРИЯ ─────────────────────────

    /// <summary>
    /// Ставим вершины между целыми и раскорёженными. Сварка просто уменьшает
    /// долю раскорёженных — металл на глазах выправляется обратно.
    /// </summary>
    private void Apply()
    {
        if (_appliedDamage == _damage.Value && _appliedSeed == _seed.Value) return;
        _appliedDamage = _damage.Value;
        _appliedSeed = _seed.Value;

        float t = Damage01;

        for (int i = 0; i < _work.Count; i++)
        {
            Mesh m = _work[i];
            if (m == null) continue;

            if (t <= 0.001f)
            {
                m.vertices = _clean[i];
            }
            else
            {
                if (_broken[i] == null) _broken[i] = BuildBroken(_clean[i], i);

                Vector3[] src = _clean[i];
                Vector3[] dst = _broken[i];
                Vector3[] outv = new Vector3[src.Length];
                for (int v = 0; v < src.Length; v++)
                    outv[v] = Vector3.Lerp(src[v], dst[v], t);

                m.vertices = outv;
            }

            m.RecalculateNormals();
            m.RecalculateBounds();
        }
    }

    /// <summary>
    /// Считаем раскорёженный вариант.
    ///
    /// ГЛАВНОЕ ПРАВИЛО: смещение вершины зависит ТОЛЬКО от её позиции, и ни от
    /// чего больше. В меше вершины дублируются на UV-швах и жёстких рёбрах —
    /// две вершины стоят в одной точке, но имеют разные индексы. Если брать
    /// случайное число по индексу, дубликаты разъезжаются, шов расходится и
    /// полигоны выворачиваются наизнанку. Именно так меш и рвался в клочья.
    ///
    /// Всё детерминировано от зерна, поэтому у всех клиентов вмятины ложатся
    /// одинаково и гонять вершины по сети не нужно.
    /// </summary>
    private Vector3[] BuildBroken(Vector3[] src, int meshIndex)
    {
        System.Random rnd = new System.Random(_seed.Value ^ (meshIndex * 7919));

        float ox = (float)rnd.NextDouble() * 100f;
        float oy = (float)rnd.NextDouble() * 100f;
        float oz = (float)rnd.NextDouble() * 100f;

        // Считаем в ОСЯХ КОРНЯ кастрюли: у модели из Blender узел повёрнут на −90°,
        // и «верх» меша — это его Z. В осях меша вмятины ложились бы поперёк.
        Matrix4x4 toRoot = meshIndex < _toRoot.Count ? _toRoot[meshIndex] : Matrix4x4.identity;
        Matrix4x4 fromRoot = toRoot.inverse;
        Vector3[] P = new Vector3[src.Length];
        for (int i = 0; i < src.Length; i++) P[i] = toRoot.MultiplyPoint3x4(src[i]);

        Bounds b = new Bounds(P[0], Vector3.zero);
        for (int i = 1; i < P.Length; i++) b.Encapsulate(P[i]);

        float radius = Mathf.Max(0.01f, Mathf.Max(b.extents.x, b.extents.z));
        float height = Mathf.Max(0.01f, b.size.y);
        float freq = Mathf.Max(0.1f, _noiseScale * 0.35f);

        // Крупные вмятины: направление и высота — от зерна (у всех одинаковые).
        int nb = _bigDents;
        Vector2[] bigDir = new Vector2[nb];
        float[] bigY = new float[nb], bigW = new float[nb], bigD = new float[nb];
        for (int k = 0; k < nb; k++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
            bigDir[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            bigY[k] = Mathf.Lerp(0.35f, 0.9f, (float)rnd.NextDouble());
            bigW[k] = Mathf.Lerp(0.7f, 1.1f, (float)rnd.NextDouble());           // ширина, радианы (широкие — без изломов)
            bigD[k] = _bigDentDepth * Mathf.Lerp(0.6f, 1.2f, (float)rnd.NextDouble()) * ((rnd.NextDouble() < 0.3) ? -0.6f : 1f);
        }

        Vector3[] outv = new Vector3[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            Vector3 p = P[i];
            Vector3 radial = new Vector3(p.x - b.center.x, 0f, p.z - b.center.z);
            float rlen = radial.magnitude;
            radial = rlen > 0.0001f ? radial / rlen : Vector3.right;

            float up01 = Mathf.InverseLerp(b.min.y, b.max.y, p.y);
            float bias = Mathf.Lerp(1f - _topBias, 1f, up01);
            if (up01 < _bottomProtect)
                bias *= Mathf.SmoothStep(0f, 1f, up01 / Mathf.Max(0.001f, _bottomProtect));

            Vector3 q = (p - b.center) / radius * freq;
            // Плавные складки: без мелкой высокой частоты — на крупных треугольниках она
            // давала зубья и вывернутые грани (тёмные «треугольники»).
            float n1 = Noise3(q + new Vector3(ox, oy, oz)) * 1.8f;
            float n2 = Noise3(q * 1.5f + new Vector3(oz, ox, oy)) * 1.8f;
            float n3 = Noise3(q * 2.6f + new Vector3(oy, oz, ox));

            float bulge = _bulge * _strength * bias * (0.5f + n1);
            float dent = _dents * _strength * bias * n2;

            Vector3 d = radial * (radius * (bulge + dent));
            d.y += height * dent * 0.35f;
            d += radial * (radius * _jagged * bias * n3 * 2f);

            // Провалы стенки — как удары изнутри наоборот: металл смяло.
            float ang = Mathf.Atan2(radial.z, radial.x);
            for (int k = 0; k < nb; k++)
            {
                float da = Mathf.DeltaAngle(ang * Mathf.Rad2Deg, Mathf.Atan2(bigDir[k].y, bigDir[k].x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float dy = (up01 - bigY[k]) / 0.38f;
                float w = Mathf.Exp(-(da * da) / (bigW[k] * bigW[k]) - dy * dy);
                d -= radial * (radius * bigD[k] * Mathf.Min(_strength, 1.5f) * 0.5f * w * bias);
            }

            outv[i] = fromRoot.MultiplyPoint3x4(p + d);
        }
        return outv;
    }

    /// <summary>
    /// Раздробить треугольники 1→4 (середины рёбер). Форма не меняется — просто
    /// больше вершин, чтобы было что мять. Середина ребра общая для соседних
    /// треугольников (по паре индексов), так что швы не расходятся.
    /// </summary>
    private static bool Subdivide(Mesh m)
    {
        for (int s = 0; s < m.subMeshCount; s++)
            if (m.GetTopology(s) != MeshTopology.Triangles) return false;

        Vector3[] v0 = m.vertices;
        int n0 = v0.Length;
        Vector3[] nr0 = m.normals; bool hasN = nr0 != null && nr0.Length == n0;
        Vector4[] tg0 = m.tangents; bool hasT = tg0 != null && tg0.Length == n0;
        Vector2[] uv0 = m.uv; bool hasUv = uv0 != null && uv0.Length == n0;
        Vector2[] uv20 = m.uv2; bool hasUv2 = uv20 != null && uv20.Length == n0;
        Color[] c0 = m.colors; bool hasC = c0 != null && c0.Length == n0;

        var sd = new Subdiv
        {
            V = new List<Vector3>(v0),
            N = hasN ? new List<Vector3>(nr0) : null,
            T = hasT ? new List<Vector4>(tg0) : null,
            U = hasUv ? new List<Vector2>(uv0) : null,
            U2 = hasUv2 ? new List<Vector2>(uv20) : null,
            C = hasC ? new List<Color>(c0) : null,
        };

        int subs = m.subMeshCount;
        var tris = new List<int>[subs];
        for (int s = 0; s < subs; s++)
        {
            int[] t = m.GetTriangles(s);
            var o = new List<int>(t.Length * 4);
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                int ab = sd.Mid(a, b), bc = sd.Mid(b, c), ca = sd.Mid(c, a);
                o.Add(a); o.Add(ab); o.Add(ca);
                o.Add(ab); o.Add(b); o.Add(bc);
                o.Add(ca); o.Add(bc); o.Add(c);
                o.Add(ab); o.Add(bc); o.Add(ca);
            }
            tris[s] = o;
        }

        m.Clear();
        if (sd.V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(sd.V);
        if (sd.N != null) m.SetNormals(sd.N);
        if (sd.T != null) m.SetTangents(sd.T);
        if (sd.U != null) m.SetUVs(0, sd.U);
        if (sd.U2 != null) m.SetUVs(1, sd.U2);
        if (sd.C != null) m.SetColors(sd.C);
        m.subMeshCount = subs;
        for (int s = 0; s < subs; s++) m.SetTriangles(tris[s], s);
        m.RecalculateBounds();
        return true;
    }

    private sealed class Subdiv
    {
        public List<Vector3> V, N;
        public List<Vector4> T;
        public List<Vector2> U, U2;
        public List<Color> C;
        private readonly Dictionary<long, int> _mids = new Dictionary<long, int>();

        public int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            int idx;
            if (_mids.TryGetValue(key, out idx)) return idx;
            idx = V.Count;
            V.Add((V[a] + V[b]) * 0.5f);
            if (N != null) N.Add((N[a] + N[b]).normalized);
            if (T != null) { Vector4 t = (T[a] + T[b]) * 0.5f; t.w = T[a].w; T.Add(t); }
            if (U != null) U.Add((U[a] + U[b]) * 0.5f);
            if (U2 != null) U2.Add((U2[a] + U2[b]) * 0.5f);
            if (C != null) C.Add((C[a] + C[b]) * 0.5f);
            _mids[key] = idx;
            return idx;
        }
    }

    /// <summary>
    /// Трёхмерный шум в диапазоне -0.5..0.5, собранный из трёх плоских.
    ///
    /// Mathf.PerlinNoise двумерный, а нам нужно, чтобы близкие в ПРОСТРАНСТВЕ
    /// точки давали близкий результат по всем трём осям — иначе складки рвутся
    /// там, где меш заворачивается сам на себя.
    /// </summary>
    private static float Noise3(Vector3 p)
    {
        float xy = Mathf.PerlinNoise(p.x, p.y);
        float yz = Mathf.PerlinNoise(p.y, p.z);
        float zx = Mathf.PerlinNoise(p.z, p.x);
        return (xy + yz + zx) / 3f - 0.5f;
    }
}
