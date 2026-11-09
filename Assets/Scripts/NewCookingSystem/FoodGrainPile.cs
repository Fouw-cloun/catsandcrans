using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЖИВАЯ КУЧКА — физика зёрен, только визуал, у каждого игрока своя.
    ///
    /// Каждая стружка (кусочек) — частица-шарик. Шаг симуляции (PBD):
    ///   1. движение по инерции + гравитация;
    ///   2. расталкивание соседей (быстрый поиск через сетку-хэш);
    ///   3. пол и стенки тары;
    ///   4. трение: кто касается пола или соседей — тормозит, медленные
    ///      замирают совсем (статическое трение) — так получается горка
    ///      с настоящим углом откоса, а не лужа шариков.
    /// Когда всё улеглось — куча ЗАСЫПАЕТ и больше не считается, только рисуется.
    /// Будят её: новые стружки, зачерпывание, удар при падении, резкий рывок тары.
    ///
    /// Всё в локальных координатах «рамки» кучки. Гравитация берётся из мира:
    /// наклонил корыто — стружка съезжает к нижнему краю. Дёрнул корыто —
    /// содержимое по инерции плещется.
    ///
    /// Без аллокаций в шаге: массивы выделяются один раз по Max Grains.
    /// </summary>
    public sealed class FoodGrainPile
    {
        public struct Settings
        {
            public float Radius;          // радиус столкновения зерна, м
            public float Friction;        // 0.3..1.5 — кинетическое трение в паре зёрен
            public float StaticFriction;  // 0.5..3 — статическое трение: чем больше, тем круче горка
            public float FreezeSpeed;     // м/с — медленнее этого и с опорой = зерно «замерзает»
            public float GravityScale;
            public float Damping;         // 0..1 — сопротивление воздуха за шаг
            public bool Box;              // стенки: да — тара, нет — пол без границ
            public Vector2 HalfSize;      // полуразмеры дна тары, м
            public float Inertia;         // 0..1 — насколько содержимое чувствует рывки тары
            public int Iterations;        // проходов расталкивания за шаг
            public float SleepSpeed;      // м/с — всё медленнее = засыпаем
        }

        public Settings S;

        private readonly int _max;
        private readonly Vector3[] _pos, _prev;
        private readonly Quaternion[] _rot;
        private readonly Vector3[] _size;
        private readonly byte[] _shade;
        private readonly byte[] _contact;
        private readonly byte[] _support;
        private readonly bool[] _frozen;
        private readonly float[] _still;
        private readonly Vector3[] _spin;     // ось × скорость вращения, град/с — кувыркание в полёте
        private int _count;

        // сетка-хэш
        private readonly int[] _cellOf, _cellStart, _cellCount, _sorted;
        private readonly int _tableMask;

        private System.Random _rnd;
        private bool _asleep = true;
        private float _calm;

        public int Count => _count;
        public bool Asleep => _asleep;
        public Vector3 Position(int i) => _pos[i];
        public Quaternion Rotation(int i) => _rot[i];
        public Vector3 Size(int i) => _size[i];
        public int Shade(int i) => _shade[i];

        /// <summary>Горизонтальный радиус и высота текущей горки (для коллайдера).</summary>
        public float ExtentRadius { get; private set; }
        public float ExtentHeight { get; private set; }

        public FoodGrainPile(int maxGrains, int seed)
        {
            _max = Mathf.Max(8, maxGrains);
            _pos = new Vector3[_max];
            _prev = new Vector3[_max];
            _rot = new Quaternion[_max];
            _size = new Vector3[_max];
            _shade = new byte[_max];
            _contact = new byte[_max];
            _support = new byte[_max];
            _frozen = new bool[_max];
            _still = new float[_max];
            _spin = new Vector3[_max];
            _cellOf = new int[_max];
            _sorted = new int[_max];
            int table = Mathf.NextPowerOfTwo(_max * 2);
            _tableMask = table - 1;
            _cellStart = new int[table];
            _cellCount = new int[table];
            _rnd = new System.Random(seed);
        }

        public void Reseed(int seed) => _rnd = new System.Random(seed);

        // ───────────────────────── ДОБАВИТЬ / УБРАТЬ ─────────────────────────

        /// <summary>Досыпать зёрна: падают из точки spawnLocal с небольшим разбросом.</summary>
        public void Add(int n, Vector3 spawnLocal, float spread, System.Func<System.Random, Vector3> sizeFn,
                        System.Func<System.Random, Quaternion> rotFn)
        {
            for (int k = 0; k < n && _count < _max; k++)
            {
                int i = _count++;
                Vector2 d = RandomDisk() * spread;
                Vector3 p = spawnLocal + new Vector3(d.x, (float)_rnd.NextDouble() * S.Radius * 4f, d.y);
                _pos[i] = p;
                // Лёгкая начальная скорость вниз и вбок — струя, а не ровный столб.
                _prev[i] = p - new Vector3(d.x * 0.02f, -S.Radius * 0.3f, d.y * 0.02f);
                _size[i] = sizeFn(_rnd);
                _rot[i] = rotFn(_rnd);
                _shade[i] = (byte)_rnd.Next(0, 3);
                _contact[i] = 0;
                _frozen[i] = false;
                _still[i] = 0f;
                _spin[i] = RandomSpin(360f);
            }
            _asleep = false;
            _calm = 0f;
        }

        /// <summary>Сразу уложенная горка — для позднего входа, превью и первого показа.</summary>
        public void Layout(int n, float slope, System.Func<System.Random, Vector3> sizeFn,
                           System.Func<System.Random, Quaternion> rotFn)
        {
            _count = 0;
            n = Mathf.Min(n, _max);
            if (n <= 0) { UpdateExtents(); return; }

            // Объём зерна ~ (2r)³ с неплотной упаковкой; горка-параболоид.
            float grainVol = Mathf.Pow(S.Radius * 2f, 3f) * 1.35f;
            float volume = n * grainVol;
            float rx, rz, h;
            if (S.Box)
            {
                rx = Mathf.Max(S.HalfSize.x - S.Radius, S.Radius);
                rz = Mathf.Max(S.HalfSize.y - S.Radius, S.Radius);
                h = Mathf.Max(S.Radius * 2f, volume / (Mathf.PI * rx * rz * 0.75f));
            }
            else
            {
                float r = Mathf.Pow(2f * volume / (Mathf.PI * slope), 1f / 3f);
                rx = rz = r;
                h = slope * r;
            }

            for (int k = 0; k < n; k++)
            {
                int i = _count++;
                // Точка внутри горки: по радиусу — с плотностью к центру, по высоте — под профилем.
                Vector2 d = RandomDisk();
                float r2 = d.sqrMagnitude;
                float profile = S.Box ? (1f - 0.5f * r2) : (1f - r2);
                float y = S.Radius + (float)_rnd.NextDouble() * Mathf.Max(0f, h * profile - S.Radius);
                Vector3 p = new Vector3(d.x * rx, y, d.y * rz);
                _pos[i] = p;
                _prev[i] = p;
                _size[i] = sizeFn(_rnd);
                _rot[i] = rotFn(_rnd);
                _shade[i] = (byte)_rnd.Next(0, 3);
                _frozen[i] = true;
                _still[i] = 0f;
            }
            _asleep = true;
            UpdateExtents();
        }

        /// <summary>
        /// Уложенная горка ПРОИЗВОЛЬНОЙ формы: высота в точке берётся из heightAt
        /// (бугры, наплывы), часть зёрен — в мелких кучках вокруг (clusters: x, z, радиус).
        /// Для хаоса: неровная кучка, рядом крошки и комочки.
        /// </summary>
        public void LayoutShaped(int n, float extentX, float extentZ, System.Func<float, float, float> heightAt,
                                 Vector3[] clusters, float clusterFrac,
                                 System.Func<System.Random, Vector3> sizeFn, System.Func<System.Random, Quaternion> rotFn)
        {
            _count = 0;
            n = Mathf.Min(n, _max);
            int inClusters = clusters != null && clusters.Length > 0 ? Mathf.RoundToInt(n * clusterFrac) : 0;

            for (int k = 0; k < n; k++)
            {
                Vector3 p;
                if (k < inClusters)
                {
                    Vector3 cl = clusters[_rnd.Next(0, clusters.Length)];
                    Vector2 d = RandomDisk() * cl.z;
                    p = new Vector3(cl.x + d.x, S.Radius + (float)_rnd.NextDouble() * cl.z * 0.6f, cl.y + d.y);
                }
                else
                {
                    p = Vector3.zero;
                    bool ok = false;
                    for (int tries = 0; tries < 30 && !ok; tries++)
                    {
                        float x = ((float)_rnd.NextDouble() * 2f - 1f) * extentX;
                        float z = ((float)_rnd.NextDouble() * 2f - 1f) * extentZ;
                        float h = heightAt(x, z);
                        if (h <= S.Radius) continue;
                        // Ближе к поверхности гуще: снаружи горки видно больше стружек.
                        float t = 1f - Mathf.Pow((float)_rnd.NextDouble(), 1.6f);
                        p = new Vector3(x, S.Radius + t * (h - S.Radius), z);
                        ok = true;
                    }
                    if (!ok) p = new Vector3(0f, S.Radius, 0f);
                }
                int i = _count++;
                _pos[i] = p;
                _prev[i] = p;
                _size[i] = sizeFn(_rnd);
                _rot[i] = rotFn(_rnd);
                _shade[i] = (byte)_rnd.Next(0, 3);
                _frozen[i] = true;
                _still[i] = 0f;
            }
            _asleep = true;
            UpdateExtents();
        }

        /// <summary>Убрать n верхних зёрен (зачерпнули, взяли сверху).</summary>
        public void RemoveTop(int n)
        {
            n = Mathf.Min(n, _count);
            for (int k = 0; k < n; k++)
            {
                int top = 0;
                float best = float.MinValue;
                for (int i = 0; i < _count; i++)
                    if (_pos[i].y > best) { best = _pos[i].y; top = i; }
                RemoveAt(top);
            }
            Wake();
        }

        /// <summary>Убрать n зёрен ближе всего к точке (лопата черпает здесь).</summary>
        public void RemoveNear(Vector3 local, int n)
        {
            n = Mathf.Min(n, _count);
            for (int k = 0; k < n; k++)
            {
                int best = 0;
                float bd = float.MaxValue;
                for (int i = 0; i < _count; i++)
                {
                    float d = (_pos[i] - local).sqrMagnitude;
                    if (d < bd) { bd = d; best = i; }
                }
                RemoveAt(best);
            }
            Wake();
        }

        private void RemoveAt(int i)
        {
            int last = --_count;
            if (i == last) return;
            _pos[i] = _pos[last]; _prev[i] = _prev[last]; _rot[i] = _rot[last];
            _size[i] = _size[last]; _shade[i] = _shade[last]; _contact[i] = _contact[last];
            _frozen[i] = _frozen[last]; _still[i] = _still[last]; _spin[i] = _spin[last];
        }

        public void Clear() { _count = 0; UpdateExtents(); }

        // ───────────────────────── РАЗБУДИТЬ ─────────────────────────

        /// <summary>Разбудить всю кучу: все зёрна снова подвижны и заново ищут опору.</summary>
        public void Wake()
        {
            _asleep = false;
            _calm = 0f;
            for (int i = 0; i < _count; i++) { _frozen[i] = false; _still[i] = 0f; }
        }

        /// <summary>Удар при падении: всё немного разлетается и оседает заново.</summary>
        public void Splash(float strength)
        {
            for (int i = 0; i < _count; i++)
            {
                Vector2 d = RandomDisk() * strength * S.Radius;
                _prev[i] = _pos[i] - new Vector3(d.x, -strength * S.Radius * 0.5f, d.y);
                _spin[i] = RandomSpin(240f * strength);
            }
            Wake();
        }

        /// <summary>Рывок тары (ускорение в локальных осях). Сильный — будит кучу.</summary>
        public void Shove(Vector3 localAccel, float dt)
        {
            if (S.Inertia <= 0f || _count == 0) return;
            Vector3 dv = -localAccel * S.Inertia * dt * dt;
            if (_asleep && localAccel.magnitude < 6f) return;   // лёгкое покачивание не будит
            for (int i = 0; i < _count; i++) _pos[i] += dv;
            Wake();
        }

        // ───────────────────────── ШАГ ─────────────────────────

        /// <summary>
        /// Один шаг физики. gravityLocal — гравитация в осях рамки.
        ///
        /// Проверено на прототипе: 360 стружек, падающих струёй, ложатся горкой
        /// с углом откоса ~40° (как тёртая картошка) и засыпают. Без трения в
        /// парах и без «замерзания» с опорой получалась лужа в один слой, а с
        /// замерзанием без проверки опоры — столбы.
        /// </summary>
        public void Step(float dt, Vector3 gravityLocal)
        {
            if (_asleep || _count == 0) return;

            float r = S.Radius;
            Vector3 g = gravityLocal * S.GravityScale * dt * dt;
            float keep = 1f - S.Damping;

            // 1. Инерция + гравитация (замёрзшие стоят).
            for (int i = 0; i < _count; i++)
            {
                _contact[i] = 0;
                _support[i] = 0;
                if (_frozen[i]) { _prev[i] = _pos[i]; continue; }
                Vector3 v = (_pos[i] - _prev[i]) * keep;
                _prev[i] = _pos[i];
                _pos[i] += v + g;
            }

            // 2–3. Расталкивание с трением + пол + стенки.
            float minD = r * 2f;
            float minD2 = minD * minD;
            float cell = minD;
            float mus = S.StaticFriction, muk = S.Friction;
            int iters = Mathf.Max(1, S.Iterations);
            Vector3 up = gravityLocal.sqrMagnitude > 1e-6f ? -gravityLocal.normalized : Vector3.up;

            for (int it = 0; it < iters; it++)
            {
                BuildGrid(cell);

                for (int i = 0; i < _count; i++)
                {
                    Vector3 pi = _pos[i];
                    int cx = Mathf.FloorToInt(pi.x / cell), cy = Mathf.FloorToInt(pi.y / cell), cz = Mathf.FloorToInt(pi.z / cell);
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int h = Hash(cx + dx, cy + dy, cz + dz);
                        int start = _cellStart[h], end = start + _cellCount[h];
                        for (int s = start; s < end; s++)
                        {
                            int j = _sorted[s];
                            if (j <= i) continue;
                            bool fi = _frozen[i], fj = _frozen[j];
                            if (fi && fj) continue;

                            Vector3 d = _pos[j] - _pos[i];
                            float d2 = d.sqrMagnitude;
                            if (d2 >= minD2 || d2 < 1e-12f) continue;
                            float dist = Mathf.Sqrt(d2);
                            Vector3 n = d / dist;
                            float pen = minD - dist;

                            float wi = fi ? 0f : 1f, wj = fj ? 0f : 1f;
                            float ws = wi + wj;
                            _pos[i] -= n * (pen * wi / ws);
                            _pos[j] += n * (pen * wj / ws);
                            if (_contact[i] < 255) _contact[i]++;
                            if (_contact[j] < 255) _contact[j]++;

                            // Опора: кто лежит на ком (по направлению «вверх»).
                            float ny = Vector3.Dot(n, up);
                            if (ny < -0.5f && _support[i] < 255) _support[i]++;
                            else if (ny > 0.5f && _support[j] < 255) _support[j]++;

                            // Трение (PBD, Macklin): гасим относительное скольжение вдоль контакта.
                            Vector3 rel = (_pos[j] - _prev[j]) - (_pos[i] - _prev[i]);
                            Vector3 tan = rel - n * Vector3.Dot(rel, n);
                            float tl = tan.magnitude;
                            if (tl < 1e-12f) continue;
                            Vector3 fix = tl < mus * pen ? tan : tan * Mathf.Min(muk * pen / tl, 1f);
                            _pos[i] += fix * (wi / ws);
                            _pos[j] -= fix * (wj / ws);
                        }
                    }
                }

                // Пол (y = 0 рамки) и стенки тары.
                for (int i = 0; i < _count; i++)
                {
                    if (_frozen[i]) continue;
                    Vector3 p = _pos[i];
                    if (p.y < r)
                    {
                        float pen = r - p.y;
                        p.y = r;
                        // Трение о пол: медленное — стоп, быстрое — вполовину.
                        Vector3 hv = p - _prev[i]; hv.y = 0f;
                        if (hv.magnitude < mus * pen * 4f) { p.x = _prev[i].x; p.z = _prev[i].z; }
                        else { p.x -= hv.x * 0.5f; p.z -= hv.z * 0.5f; }
                        _contact[i] = (byte)Mathf.Min(255, _contact[i] + 2);
                        _support[i] = 255;
                    }
                    if (S.Box)
                    {
                        float lx = S.HalfSize.x - r, lz = S.HalfSize.y - r;
                        if (p.x < -lx) { p.x = -lx; _contact[i]++; }
                        else if (p.x > lx) { p.x = lx; _contact[i]++; }
                        if (p.z < -lz) { p.z = -lz; _contact[i]++; }
                        else if (p.z > lz) { p.z = lz; _contact[i]++; }
                    }
                    _pos[i] = p;
                }
            }

            // 3½. Кувыркание: летящие вертятся, в контакте вращение быстро гаснет.
            for (int i = 0; i < _count; i++)
            {
                if (_frozen[i]) continue;
                Vector3 w = _spin[i];
                float sp = w.magnitude;
                if (sp < 1f) continue;
                _rot[i] = Quaternion.AngleAxis(sp * dt, w / sp) * _rot[i];
                if (_contact[i] > 0) _spin[i] = w * 0.8f;
            }

            // 4. Замерзание устойчивых зёрен + проверка на сон всей кучи.
            float freezeD = S.FreezeSpeed * dt;
            int needSupport = 3 * iters;
            float maxMove = 0f;
            for (int i = 0; i < _count; i++)
            {
                if (_frozen[i]) continue;
                float spd = (_pos[i] - _prev[i]).magnitude;
                if (spd < freezeD && _support[i] >= needSupport) _still[i] += dt;
                else _still[i] = 0f;
                if (_still[i] > 0.15f) { _frozen[i] = true; _prev[i] = _pos[i]; continue; }
                if (spd > maxMove) maxMove = spd;
            }

            if (maxMove / dt < S.SleepSpeed)
            {
                _calm += dt;
                if (_calm > 0.35f)
                {
                    _asleep = true;
                    for (int i = 0; i < _count; i++) { _prev[i] = _pos[i]; _frozen[i] = true; }
                    UpdateExtents();
                }
            }
            else _calm = 0f;
        }

        private void BuildGrid(float cell)
        {
            System.Array.Clear(_cellCount, 0, _cellCount.Length);
            for (int i = 0; i < _count; i++)
            {
                Vector3 p = _pos[i];
                int h = Hash(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
                _cellOf[i] = h;
                _cellCount[h]++;
            }
            int run = 0;
            for (int h = 0; h < _cellStart.Length; h++) { _cellStart[h] = run; run += _cellCount[h]; }
            // Раскладка по ячейкам (сортировка подсчётом), счётчики — как курсоры.
            System.Array.Clear(_cellCount, 0, _cellCount.Length);
            for (int i = 0; i < _count; i++)
            {
                int h = _cellOf[i];
                _sorted[_cellStart[h] + _cellCount[h]++] = i;
            }
        }

        private Vector3 RandomSpin(float speed)
        {
            Vector3 axis = new Vector3((float)_rnd.NextDouble() - 0.5f, (float)_rnd.NextDouble() - 0.5f,
                                       (float)_rnd.NextDouble() - 0.5f);
            if (axis.sqrMagnitude < 1e-4f) axis = Vector3.up;
            return axis.normalized * speed * (0.4f + 0.6f * (float)_rnd.NextDouble());
        }

        private int Hash(int x, int y, int z) => ((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) & _tableMask;

        private Vector2 RandomDisk()
        {
            float a = (float)_rnd.NextDouble() * Mathf.PI * 2f;
            float rr = Mathf.Sqrt((float)_rnd.NextDouble());
            return new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr);
        }

        private void UpdateExtents()
        {
            float r = 0f, h = 0f;
            for (int i = 0; i < _count; i++)
            {
                Vector3 p = _pos[i];
                float hr = p.x * p.x + p.z * p.z;
                if (hr > r) r = hr;
                if (p.y > h) h = p.y;
            }
            ExtentRadius = Mathf.Sqrt(r) + S.Radius;
            ExtentHeight = h + S.Radius;
        }

        /// <summary>Самая высокая точка горки около (x,z) — куда сыпать сверху.</summary>
        public float TopAt(float x, float z, float within)
        {
            float top = 0f, w2 = within * within;
            for (int i = 0; i < _count; i++)
            {
                float dx = _pos[i].x - x, dz = _pos[i].z - z;
                if (dx * dx + dz * dz < w2 && _pos[i].y > top) top = _pos[i].y;
            }
            return top;
        }
    }
}
