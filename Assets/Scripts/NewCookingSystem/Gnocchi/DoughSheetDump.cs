using FishNet;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЗАМЕС → КОМ БОЛЬШОГО ТЕСТА. Пельменное (и ньокки) тесто, замешанное в таре, вываливается не кучкой,
    /// а КОМОМ — тем же тестом, что плющат кувалдой и катают катком (DoughSheet).
    ///
    ///   • Бетономешалка (MixerDough): барабан стоит, бочку повернули ручкой — ком вываливается из горловины.
    ///   • Ведро, миска, тазик: наклонил сильно (опустошить до дна) — всё тесто выходит одним комом
    ///     в ту сторону, куда наклонил горловину.
    ///   • Одна партия рецепта (Portions Per Ball порций) — ком как у меню 4б. Больше — ком больше
    ///     (до полутора партий одним комом), ещё больше — несколько комов.
    ///   • Если задан Carry Prefab (шар хлебного теста — FoodPiece + DoughBlob, тот же, что у бетономешалки) — тесто
    ///     вываливается ШАРОМ-ПРЕДМЕТОМ: берёшь в руки (F), бросаешь (ЛКМ) куда надо — там становится большим тестом
    ///     (DoughSheetCarry). Нет — сразу комом большого теста на месте.
    ///   • Подставь РУЛОН (подстилку) — на голый пол тесто ляжет грязным.
    ///
    /// Ком — это префаб куска теста (меню Ньокки → 6, уже в Spawnable Prefabs). Он берётся отсюда
    /// (поле Ball Prefab), а если тут пусто — у любого теста в сцене (его Piece Prefab).
    /// Ставит меню CatCooks → Пельмени → 2 (на объект FoodDatabase). Один на сцену.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DoughSheetDump : MonoBehaviour
    {
        [Tooltip("Префаб кома: заготовка куска теста (Dough Sheet с галкой Start Empty) — меню Ньокки → 6. Должен быть в Spawnable Prefabs")]
        [SerializeField] private GameObject _ballPrefab;
        [Tooltip("Порций замеса на обычный ком (как меню 4б). Рецепт «Тесто для пельменей» на 1 ведро воды даёт 32")]
        [Min(4)][SerializeField] private int _portionsPerBall = 32;
        [Tooltip("Шар-предмет, который берут в руки: FoodPiece + DoughBlob (шар хлебного теста — Ball Prefab у бетономешалки). " +
                 "Пусто — возьмёт у бетономешалки в сцене; нет и там — тесто сразу ляжет комом большого теста")]
        [SerializeField] private GameObject _carryPrefab;
        [SerializeField] private bool _log = true;

        private static DoughSheetDump _inst;
        private static bool _warnNoPrefab;

        private void OnEnable() { _inst = this; }
        private void OnDisable() { if (_inst == this) _inst = null; }

        public static int PortionsPerBall => _inst != null ? Mathf.Max(4, _inst._portionsPerBall) : 32;
        private static bool Log => _inst == null || _inst._log;

        /// <summary>Префаб кома: свой (поле) или Piece Prefab любого теста в сцене.</summary>
        public static GameObject BallPrefab
        {
            get
            {
                if (_inst != null && _inst._ballPrefab != null) return _inst._ballPrefab;
                foreach (DoughSheet d in DoughSheet.All)
                    if (d != null && d.PiecePrefab != null) return d.PiecePrefab;
                return null;
            }
        }

        /// <summary>Шар-предмет для рук: свой (поле) или Ball Prefab бетономешалки в сцене. null — без шара.</summary>
        public static GameObject CarryPrefab
        {
            get
            {
                if (_inst != null && _inst._carryPrefab != null) return _inst._carryPrefab;
                foreach (MixerDough m in FindObjectsByType<MixerDough>(FindObjectsSortMode.None))
                    if (m != null && m.BallPrefab != null && m.BallPrefab.GetComponent<FoodPiece>() != null) return m.BallPrefab;
                return null;
            }
        }

        // ───────────────────────── что за тесто ─────────────────────────

        /// <summary>Продукт — большое тесто (раскатывают катком)? kind — какой пресет.</summary>
        public static bool IsSheetDough(ProductDef p, out DoughKind kind)
        {
            kind = DoughKind.Pelmeni;
            if (p == null || string.IsNullOrEmpty(p.Title)) return false;
            string t = p.Title.Trim().ToLowerInvariant();
            if (t == "тесто для пельменей") { kind = DoughKind.Pelmeni; return true; }
            if (t == "тесто для ньокки") { kind = DoughKind.Gnocchi; return true; }
            return false;
        }

        /// <summary>Порция в таре — замешанное большое тесто (Замешано / Подошло)?</summary>
        public static bool IsSheetDough(ushort product, FoodState state, out DoughKind kind)
        {
            kind = DoughKind.Pelmeni;
            if (product == 0 || (state != FoodState.Kneaded && state != FoodState.Proofed)) return false;
            FoodDatabase db = FoodDatabase.Instance;
            return db != null && IsSheetDough(db.Get(product), out kind);
        }

        /// <summary>Есть ли в таре замешанное большое тесто (первое найденное).</summary>
        public static bool FindIn(Cookware cw, out ushort product, out FoodState state, out int count)
        {
            product = 0; state = FoodState.None; count = 0;
            if (cw == null) return false;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count <= 0 || !IsSheetDough(v.Product, v.State, out _)) continue;
                product = v.Product; state = v.State;
                count = cw.CountOf(v.Product);
                return count > 0;
            }
            return false;
        }

        // ───────────────────────── вывалить ─────────────────────────

        /// <summary>
        /// Тара высыпает через край (Cookware.ServerSpillOver). Это большое тесто — вываливаем ВСЁ это тесто комом
        /// (taken порций уже вынуто), true. Не тесто или нет префаба — false (обычная кучка). Сервер.
        /// </summary>
        public static bool ServerTryDump(Cookware cw, ushort product, FoodState state, int taken, Vector3 rim, Vector3 dir)
        {
            if (cw == null || taken <= 0 || !IsSheetDough(product, state, out DoughKind kind)) return false;
            // бетономешалка вываливает тесто сама — из горловины при повороте бочки (MixerDough). Через «край» — не надо:
            // центр бочки внутри корпуса, шар там проваливался. Порцию — обратно.
            if (cw.GetComponent<MixerDough>() != null || cw.GetComponentInParent<MixerDough>() != null)
            {
                cw.ServerAddPinch(product, state, taken, 0, false);
                return true;
            }
            if (BallPrefab == null) { WarnNoPrefab(cw); return false; }
            // горловиной куда наклонили — туда и падает
            Vector3 tip = Vector3.ProjectOnPlane(cw.transform.up, Vector3.up);
            Vector3 side = tip.sqrMagnitude > 0.04f ? tip.normalized : (dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward);
            int total = taken + cw.ServerTake(product, cw.CountOf(product));
            string who = cw.Def != null ? cw.Def.Title : cw.name;
            int left = ServerSpawnBalls(kind, product, total, rim, side, who);
            if (left > 0)
            {
                cw.ServerAddPinch(product, state, left, 0, false);
                Debug.LogError($"[Тесто] «{who}»: не смог вывалить {left} порц. теста (нет префаба шара/кома) — остались в таре. CatCooks → Пельмени → 2", cw);
            }
            return true;
        }

        /// <summary>
        /// Бетономешалка: взять из тары до партии теста (остаток меньше полутора — весь) и вывалить комом. Сервер.
        /// Возвращает, сколько порций ушло (0 — нет теста или префаба).
        /// </summary>
        public static int ServerPourBall(Cookware cw, ushort product, Vector3 mouth, Vector3 side, string who)
        {
            if (cw == null || !IsSheetDough(product, FoodState.Kneaded, out DoughKind kind)) return 0;
            if (BallPrefab == null) { WarnNoPrefab(cw); return 0; }
            int have = cw.CountOf(product), per = PortionsPerBall;
            int n = have < per * 3 / 2 ? have : per;
            int took = cw.ServerTake(product, n);
            if (took <= 0) return 0;
            if (ServerSpawnCarry(kind, product, took, mouth, side, who)) return took;
            if (ServerSpawnBall(kind, took, mouth, side, who) != null) return took;
            cw.ServerAddPinch(product, FoodState.Kneaded, took, 0, false);                 // не вышло ни шара, ни кома — тесто обратно, не пропадает
            Debug.LogError($"[Тесто] «{who}»: не смог вывалить тесто (нет префаба шара/кома или он без Food Piece / Dough Sheet) — тесто осталось в таре. CatCooks → Пельмени → 2, потом 3", cw);
            return 0;
        }

        /// <summary>Порции → комы по партии (остаток меньше полутора партий — в последний ком), рядком в сторону side.</summary>
        /// <returns>Сколько порций НЕ вывалилось (нет префабов) — вернуть в тару.</returns>
        private static int ServerSpawnBalls(DoughKind kind, ushort product, int total, Vector3 from, Vector3 side, string who)
        {
            int per = PortionsPerBall;
            Vector3 at = from;
            int guard = 0;
            while (total > 0 && guard++ < 8)
            {
                int n = total < per * 3 / 2 ? total : per;
                if (ServerSpawnCarry(kind, product, n, at, side, who)) { total -= n; at += side * 0.6f; continue; }
                DoughSheet ds = ServerSpawnBall(kind, n, at, side, who);
                if (ds == null) break;
                total -= n;
                at = ds.transform.position + side * (ds.FootRadius * 1.1f);
            }
            return total;
        }

        /// <summary>Один ком: префаб куска → пресет шара по порциям → Spawn. Ставится краем к точке from, в сторону side. Сервер.</summary>
        public static DoughSheet ServerSpawnBall(DoughKind kind, int portions, Vector3 from, Vector3 side, string who)
        {
            GameObject pf = BallPrefab;
            if (pf == null || portions <= 0) return null;
            if (InstanceFinder.ServerManager == null) return null;
            GameObject go = Instantiate(pf, from, Quaternion.identity);
            DoughSheet ds = go.GetComponent<DoughSheet>();
            if (ds == null)
            {
                Destroy(go);
                Debug.LogError($"[Тесто] префаб кома «{pf.name}» без Dough Sheet — нужен префаб куска теста (меню Ньокки → 6), потом Пельмени → 2");
                return null;
            }
            ds.ServerPresetBall(kind, portions, PortionsPerBall, pf);
            Vector3 flat = Vector3.ProjectOnPlane(side, Vector3.up);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            go.transform.position = from + flat * (ds.FootRadius + 0.1f) + Vector3.up * 0.05f;   // дальше упадёт само и сядет на пол / подстилку
            InstanceFinder.ServerManager.Spawn(go);
            if (Log)
                Debug.Log($"[Тесто] «{who}»: вывалился ком теста «{DoughSheet.KindTitle(kind)}» — {portions} порц. Ловите на подстилку (рулон), " +
                          "дальше кувалда и каток с мукой — как у ньокков", ds);
            return ds;
        }

        /// <summary>Шар-предмет (FoodPiece «тесто», Замешано, N порций) — в руки и бросать. false — шара нет (нет Carry Prefab). Сервер.</summary>
        public static bool ServerSpawnCarry(DoughKind kind, ushort product, int portions, Vector3 from, Vector3 side, string who)
        {
            GameObject pf = CarryPrefab;
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef def = db != null ? db.Get(product) : null;
            if (pf == null || def == null || portions <= 0 || InstanceFinder.ServerManager == null) return false;
            Vector3 flat = Vector3.ProjectOnPlane(side, Vector3.up);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            DoughBlob proto = pf.GetComponent<DoughBlob>();
            float r = proto != null ? proto.RadiusFor(portions) : 0.4f;
            GameObject go = Instantiate(pf, from + flat * (0.2f + r) + Vector3.up * (r * 0.3f), Quaternion.identity);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp == null) { Destroy(go); Debug.LogError($"[Тесто] Carry Prefab «{pf.name}» без Food Piece — нужен шар хлебного теста (Хлеб → бетономешалка)"); return false; }
            fp.ServerPreset(def, FoodState.Kneaded, 100, 0, 100);
            InstanceFinder.ServerManager.Spawn(go);
            fp.ServerSetCount(Mathf.Clamp(portions, 1, 255));
            DoughSheetCarry carry = go.AddComponent<DoughSheetCarry>();           // только на сервере: «положили — стало большим тестом»
            carry.Kind = kind; carry.Log = Log;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.AddForce((flat + Vector3.up * 0.2f).normalized * 1.5f, ForceMode.VelocityChange);
            if (Log) Debug.Log($"[Тесто] «{who}»: вывалился шар теста «{DoughSheet.KindTitle(kind)}» ({portions} порц.) — бери в руки (F) и брось (ЛКМ) на подстилку", go);
            return true;
        }

        /// <summary>Ком большого теста ровно в точке (шар положили / бросили сюда). Сервер.</summary>
        public static DoughSheet ServerSpawnBallAt(DoughKind kind, int portions, Vector3 at)
        {
            GameObject pf = BallPrefab;
            if (pf == null || portions <= 0 || InstanceFinder.ServerManager == null) return null;
            GameObject go = Instantiate(pf, at, Quaternion.identity);
            DoughSheet ds = go.GetComponent<DoughSheet>();
            if (ds == null) { Destroy(go); return null; }
            ds.ServerPresetBall(kind, portions, PortionsPerBall, pf);
            go.transform.position = at + Vector3.up * 0.1f;
            InstanceFinder.ServerManager.Spawn(go);
            return ds;
        }

        private static void WarnNoPrefab(Object ctx)
        {
            if (_warnNoPrefab) return;
            _warnNoPrefab = true;
            Debug.LogError("[Тесто] замес надо вывалить комом, но нет префаба кома — CatCooks → Пельмени → 2 " +
                           "(возьмёт префаб куска теста из меню Ньокки → 6). Пока высыпается кучкой", ctx);
        }

#if UNITY_EDITOR
        public GameObject EditorBallPrefab => _ballPrefab;
        public GameObject EditorCarryPrefab => _carryPrefab;
        public void EditorSetCarry(GameObject prefab)
        {
            UnityEditor.Undo.RecordObject(this, "Шар теста");
            _carryPrefab = prefab;
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
        public int EditorPortionsPerBall => _portionsPerBall;

        public void EditorSetup(GameObject prefab)
        {
            UnityEditor.Undo.RecordObject(this, "Ком теста из тары");
            _ballPrefab = prefab;
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
#endif
    }
}
