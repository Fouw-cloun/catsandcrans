using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// БЕТОНОМЕШАЛКА КАК ТАРА ДЛЯ ТЕСТА.
    ///
    /// Вешается на КОРЕНЬ бетономешалки рядом с ConcreteMixer и Cookware (Cookware Def — «Бетономешалка»,
    /// своя вместимость). Внутрь бочки: мука (мастерок), вода (шланг/ведро), дрожжи (баночка ПКМ), соль (мастерок).
    ///
    ///   • Барабан крутится (зелёная кнопка) — идёт шаг рецепта «Перемешать»: всё смешивается
    ///     в «Тесто дрожжевое» (одна масса). Не по рецепту — в консоли, чего не хватает.
    ///   • Повернул бочку ручкой горловиной вниз — тесто вываливается ШАРАМИ (Ball Prefab):
    ///     одна партия рецепта = один шар.
    ///   • ПЕЛЬМЕННОЕ тесто (вода 1 ведро + мука 12 мастерков + 2 яйца + соль 1 мастерок, барабан 10 с) —
    ///     при повороте вываливается КОМОМ большого теста (DoughSheet: кувалда, каток) — DoughSheetDump.
    ///   • Жидкое (бисквитное тесто, любая жидкость) — при повороте ЛЬЁТСЯ СТРУЁЙ в тару под горловиной
    ///     (подставь опалубку или ведро). Нет тары под струёй — не льётся (в консоли подсказка).
    ///     Можно и черпать из бочки ведром (ПКМ с ведром в руке, глядя в бочку).
    ///
    /// Горловина — Mouth у Cookware (дочерний объект бочки, СИНЯЯ ось смотрит из бочки наружу).
    /// Он должен быть ребёнком Barrel Pivot (поворачивается вместе с бочкой, но не крутится).
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class MixerDough : NetworkBehaviour
    {
        [Header("Тесто")]
        [Tooltip("Продукт теста (Тесто дрожжевое). Пусто — результат рецепта «Тесто»")]
        [SerializeField] private ProductDef _dough;
        [Tooltip("Сетевой префаб шара теста (FoodPiece + DoughBlob)")]
        [SerializeField] private GameObject _ballPrefab;
        [Tooltip("Порций в одном шаре (= одна партия рецепта: 30 вода + 16 мука + дрожжи + соль ≈ 49)")]
        [Range(4, 250)][SerializeField] private int _ballPortions = 49;

        [Header("Высыпание")]
        [Tooltip("Горловина (СИНЯЯ ось наружу). Пусто — Mouth у Cookware")]
        [SerializeField] private Transform _mouth;
        [Tooltip("Насколько горловина должна смотреть вниз, градусы ниже горизонта (запасной способ — без ручки бочки)")]
        [Range(0f, 80f)][SerializeField] private float _pourAngle = 25f;
        [Tooltip("Тесто вываливается, когда бочку повернули ручкой на столько градусов от исходного положения " +
                 "и барабан НЕ крутится. Шар вылетает вбок из горловины.")]
        [Range(20f, 90f)][SerializeField] private float _turnToPour = 60f;
        [Tooltip("Шар раз в столько секунд, пока бочка наклонена")]
        [Min(0.2f)][SerializeField] private float _pourEvery = 1.2f;
        [SerializeField] private float _pourSpeed = 1.5f;

        [Header("Жидкое (бисквитное тесто) — струёй")]
        [Tooltip("Сколько порций в секунду льётся из повёрнутой бочки в тару под горловиной")]
        [Range(1f, 60f)][SerializeField] private float _liquidPerSecond = 12f;
        [Tooltip("Как глубоко под горловиной искать тару, м")]
        [Range(0.5f, 6f)][SerializeField] private float _streamReach = 3f;
        [Tooltip("Точка для тары у бетономешалки (Cookware Dock). Тара стоит в ней — бочка льёт туда, даже если " +
                 "струя не точно над ней. Пусто — дочерний «PourDock». Меню: Хлеб → Бетономешалка: точка для тары")]
        [SerializeField] private CookwareDock _pourDock;

        [Header("Мешать")]
        [Range(0f, 1f)][SerializeField] private float _agitation = 0.8f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        /// <summary>Шар теста (FoodPiece + DoughBlob) — им же вываливается пельменное / ньокки тесто, чтобы брать в руки.</summary>
        public GameObject BallPrefab => _ballPrefab;

        private Cookware _cw;
        private ConcreteMixer _mixer;
        private float _pourAt, _logAt, _hintAt, _liquidAcc, _fxAt, _liquidLogAt;
        private int _liquidPoured;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private float _openSign = 1f;        // +1/−1: какой конец оси барабана — горловина (тот, что смотрел вверх в сцене)

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _mixer = GetComponent<ConcreteMixer>();
            if (_mixer == null) _mixer = GetComponentInParent<ConcreteMixer>();
            if (_pourDock == null)
            {
                Transform d = transform.Find("PourDock");
                if (d != null) _pourDock = d.GetComponent<CookwareDock>();
            }
            if (_mouth == null)
            {
                Transform m = transform.Find("Mouth");
                if (m == null) foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (t.name == "Mouth") { m = t; break; }
                _mouth = m;
            }
        }

        /// <summary>Триггер полости внутри бочки: его синяя ось — ось барабана.</summary>
        private Transform Receive => _cw != null && _cw.InsideVolume != null ? _cw.InsideVolume.transform : null;

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Горловина — конец оси барабана, который в исходной позе смотрит вверх (тесто внутри не выпадает).
            Transform rc = Receive;
            if (rc != null) _openSign = Vector3.Dot(rc.forward, Vector3.up) >= 0f ? 1f : -1f;
            if (!_log) return;
            if (_mixer == null) Debug.LogError($"[Мешалка-тесто] «{name}»: нет ConcreteMixer на корне", this);
            if (_mouth == null) Debug.LogError($"[Мешалка-тесто] «{name}»: нет Mouth (горловины) — тесто не вывалится", this);
            if (_ballPrefab == null) Debug.LogError($"[Мешалка-тесто] «{name}»: не задан Ball Prefab", this);
        }

        private ProductDef Dough
        {
            get
            {
                if (_dough != null) return _dough;
                FoodDatabase db = FoodDatabase.Instance;
                if (db == null || db.Table == null) return null;
                for (int id = 1; id < 2048; id++)
                {
                    ProductDef p = db.Table.GetProduct((ushort)id);
                    if (p != null && p.Title != null && p.Title.Trim().ToLowerInvariant() == "тесто дрожжевое") { _dough = p; break; }
                }
                return _dough;
            }
        }

        private void Update()
        {
            if (!base.IsServerInitialized || _cw == null) return;
            float dt = Time.deltaTime;

            bool spinning = _mixer != null && _mixer.IsSpinning;
            if (spinning && !_cw.IsEmpty)
            {
                _cw.ServerAgitate(_agitation);
                bool step = CookRecipeBook.ServerProcess(_cw, CookStepKind.Mix, dt);
                if (_log && Time.time >= _logAt)
                {
                    _logAt = Time.time + 3f;
                    CookwareJournal j = CookwareJournal.Peek(_cw);
                    string st = step && j != null && j.Recipe != null ? $"месим «{j.Recipe.Title}»"
                              : j != null && !string.IsNullOrEmpty(j.Hint) ? "не по рецепту: " + j.Hint
                              : "крутится, рецепта нет";
                    Debug.Log($"[Мешалка-тесто] «{name}»: {st}", this);
                }
            }

            // высыпание: только когда барабан стоит и бочку повернули ручкой
            ProductDef dough = Dough;
            int have = dough != null ? _cw.CountOf(dough.Id) : 0;
            // пельменное тесто — вываливается КОМОМ большого теста (DoughSheet: кувалда, каток), не шаром и не струёй
            if (have <= 0 && DoughSheetDump.FindIn(_cw, out ushort sheetDough, out _, out int sheetHave))
            {
                ServerPourSheet(sheetDough, sheetHave, spinning);
                return;
            }
            if (have <= 0)
            {
                // нет дрожжевого теста — жидкое (бисквитное тесто) льётся струёй в тару под горловиной
                if (!spinning && !_cw.IsEmpty && IsTurned()) ServerPourLiquid(dt);
                else
                {
                    if (_liquidPoured > 0 && _log) Debug.Log($"[Мешалка-тесто] «{name}»: струя остановилась — вылито {_liquidPoured} порц., в бочке {_cw.TotalCount}", this);
                    _liquidPoured = 0; _liquidAcc = 0f;
                }
                return;
            }
            if (Time.time < _pourAt) return;
            if (spinning)
            {
                if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 6f; Debug.Log($"[Мешалка-тесто] «{name}»: тесто готово — выключи барабан (красная кнопка) и поверни бочку ручкой", this); }
                return;
            }
            bool turned = IsTurned();
            if (!turned)
            {
                if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 8f; Debug.Log($"[Мешалка-тесто] «{name}»: тесто в бочке — поверни бочку ручкой на {_turnToPour:0}°, чтобы вывалить", this); }
                return;
            }
            _pourAt = Time.time + _pourEvery;
            int n = have < _ballPortions * 3 / 2 ? have : _ballPortions;     // остаток меньше полутора шаров — одним
            ServerPourBall(dough, n);
        }

        /// <summary>Пельменное тесто в бочке: барабан стоит, бочка повёрнута — ком из горловины (партия за раз). Сервер.</summary>
        [Server]
        private void ServerPourSheet(ushort product, int have, bool spinning)
        {
            if (Time.time < _pourAt) return;
            if (spinning)
            {
                if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 6f; Debug.Log($"[Мешалка-тесто] «{name}»: пельменное тесто готово ({have} порц.) — выключи барабан и поверни бочку ручкой. Под горловину — РУЛОН (подстилку)", this); }
                return;
            }
            if (!IsTurned())
            {
                if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 8f; Debug.Log($"[Мешалка-тесто] «{name}»: пельменное тесто в бочке — поверни бочку ручкой на {_turnToPour:0}°, вывалится комом. Подстели рулон", this); }
                return;
            }
            _pourAt = Time.time + Mathf.Max(_pourEvery, 2f);
            OpeningPose(out Vector3 mouthP, out Vector3 dir);
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (flat.sqrMagnitude < 0.04f) flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            flat.Normalize();
            int took = DoughSheetDump.ServerPourBall(_cw, product, mouthP + flat * 0.2f, flat, name);
            if (took > 0 && _log) Debug.Log($"[Мешалка-тесто] «{name}»: вывалился ком пельменного теста ({took} порц.), в бочке ещё {_cw.CountOf(product)}", this);
        }

        private bool IsTurned() => _mixer != null ? Mathf.Abs(_mixer.TurnAngle) >= _turnToPour
                                                  : _mouth != null && Vector3.Dot(_mouth.forward, Vector3.down) >= Mathf.Sin(_pourAngle * Mathf.Deg2Rad);

        /// <summary>Тара прямо под горловиной (опалубка, ведро) — первое, во что упирается луч вниз.</summary>
        private Cookware FindBelow(Vector3 from)
        {
            int n = Physics.RaycastNonAlloc(new Ray(from, Vector3.down), _hits, _streamReach, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Cookware res = null;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                Cookware c = h.collider.GetComponentInParent<Cookware>();
                if (c == _cw) continue;
                if (c == null && h.collider.isTrigger) continue;          // чужие зоны-триггеры не мешают
                best = h.distance;
                res = c;                                                   // null — упёрлись в пол/стол
            }
            return res;
        }

        /// <summary>Жидкое из повёрнутой бочки — струёй в тару под горловиной (сервер).</summary>
        [Server]
        private void ServerPourLiquid(float dt)
        {
            OpeningPose(out Vector3 mouthP, out Vector3 dir);
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (flat.sqrMagnitude < 0.04f) flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            flat.Normalize();
            Vector3 lip = mouthP + flat * 0.15f;
            // в точке у бетономешалки стоит тара — льём в неё; иначе — что под горловиной
            Cookware target = _pourDock != null && _pourDock.Current != null && _pourDock.Current != _cw ? _pourDock.Current : FindBelow(lip);
            if (target == null)
            {
                _liquidAcc = 0f;
                if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 4f; Debug.Log($"[Мешалка-тесто] «{name}»: бочка повёрнута — поставь опалубку или ведро " + (_pourDock != null ? "в точку у бетономешалки (PourDock) или " : "") + "ПОД горловину, тогда польётся", this); }
                return;
            }
            _liquidAcc += dt * _liquidPerSecond;
            int n = Mathf.FloorToInt(_liquidAcc);
            if (n <= 0) return;
            _liquidAcc -= n;
            int took = _cw.ServerTakeTop(n, out ushort product, out FoodState state, out byte cook);
            if (took <= 0) return;
            int put = target.ServerAddContent(product, state, took, cook, false);
            if (put < took) _cw.ServerAddPinch(product, state, took - put, cook, false);     // не влезло — обратно в бочку
            if (put <= 0)
            {
                if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 3f; Debug.Log($"[Мешалка-тесто] «{name}»: «{(target.Def != null ? target.Def.Title : target.name)}» полная — подставь другую тару", this); }
                return;
            }
            _liquidPoured += put;
            if (Time.time >= _fxAt)
            {
                _fxAt = Time.time + 0.08f;
                FoodDatabase db = FoodDatabase.Instance;
                ProductDef pd = db != null ? db.Get(product) : null;
                RpcStream(lip, target.MouthPoint, pd != null ? pd.BaseColor : new Color(0.4f, 0.25f, 0.15f));
            }
            if (_log && Time.time >= _liquidLogAt)
            {
                _liquidLogAt = Time.time + 1.5f;
                FoodDatabase db = FoodDatabase.Instance;
                ProductDef pd = db != null ? db.Get(product) : null;
                Debug.Log($"[Мешалка-тесто] «{name}»: льётся «{(pd != null ? pd.Title : "?")}» в «{(target.Def != null ? target.Def.Title : target.name)}» — " +
                          $"вылито {_liquidPoured}, там {target.TotalCount}/{target.Capacity}, в бочке ещё {_cw.TotalCount}", this);
            }
        }

        /// <summary>Струя (у всех): капли от горловины вниз к таре.</summary>
        [ObserversRpc]
        private void RpcStream(Vector3 from, Vector3 to, Color c)
        {
            Vector3 d = to - from;
            Vector3 dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.down;
            Color c2 = Color.Lerp(c, Color.black, 0.2f);
            CCRuntimeParticles.Burst(from, dir, c, c2, 3, CCRuntimeParticles.Look.Gunk.Scaled(0.5f));
            CCRuntimeParticles.Burst(Vector3.Lerp(from, to, 0.5f), Vector3.down, c, c2, 2, CCRuntimeParticles.Look.Gunk.Scaled(0.45f));
        }

        /// <summary>Горловина: конец оси барабана (по триггеру полости), что смотрел вверх в сцене. Нет триггера — Mouth.</summary>
        private void OpeningPose(out Vector3 pos, out Vector3 dir)
        {
            Transform rc = Receive;
            BoxCollider box = _cw != null ? _cw.InsideVolume as BoxCollider : null;
            if (rc != null && box != null)
            {
                dir = rc.forward * _openSign;
                pos = rc.TransformPoint(box.center + Vector3.forward * _openSign * box.size.z * 0.72f);
                return;
            }
            if (_mouth != null) { pos = _mouth.position; dir = _mouth.forward; return; }
            pos = transform.position + Vector3.up; dir = transform.forward;
        }

        [Server]
        private void ServerPourBall(ProductDef dough, int portions)
        {
            if (_ballPrefab == null) return;
            int took = _cw.ServerTake(dough.Id, portions);
            if (took <= 0) return;
            DoughBlob proto = _ballPrefab.GetComponent<DoughBlob>();
            float r = proto != null ? proto.RadiusFor(took) : 0.16f;
            OpeningPose(out Vector3 mouthP, out Vector3 dir);
            // вбок из горловины, на свой радиус наружу — не в стенке и не сквозь бочку
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (flat.sqrMagnitude < 0.04f) flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            flat.Normalize();
            Vector3 at = mouthP + flat * (0.2f + r) + Vector3.up * (r * 0.3f);
            GameObject go = Instantiate(_ballPrefab, at, Quaternion.identity);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp != null) fp.ServerPreset(dough, FoodState.Kneaded, 100, 0, 100);
            base.Spawn(go);
            if (fp != null) fp.ServerSetCount(Mathf.Clamp(took, 1, 255));
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.AddForce((flat + Vector3.up * 0.2f).normalized * _pourSpeed, ForceMode.VelocityChange);
            if (_log) Debug.Log($"[Мешалка-тесто] «{name}»: вывалился шар теста ({took} порц.), в бочке ещё {_cw.CountOf(dough.Id)}", this);
        }
    }
}
