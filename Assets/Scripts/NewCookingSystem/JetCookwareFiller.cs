using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// НАЛИВ ВОДЫ СТРУЁЙ В ТАРУ НОВОЙ СИСТЕМЫ (кастрюля, ведро, банка, казан).
    ///
    /// Вешается на брандспойт (или выход гидранта) рядом с WaterJet. Струя
    /// (дуга WaterJet) проходит через горловину тары или падает внутрь — в таре
    /// прибавляется «Вода»: Portions Per Second при полном напоре. Полная — вода
    /// дальше не льётся (в консоли один раз «полная»), накрытая крышкой и
    /// пробитая — не принимают.
    ///
    /// Вид: в месте падения на воде в таре — круги и закрутка (у всех игроков).
    /// Красивая мультяшная струя — WaterJetLiquidStreamView на том же объекте:
    /// она сама ныряет в жидкость тары.
    ///
    /// Старый WaterJetFiller (CookingVessel, граммы) этому не мешает — он для старых кастрюль.
    /// Сеть: наливает сервер, содержимое тары синхронизировано и так.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JetCookwareFiller : NetworkBehaviour
    {
        [Header("Что наливаем")]
        [Tooltip("Продукт воды. Пусто — «Вода» по названию")]
        [SerializeField] private ProductDef _water;
        [Tooltip("Порций в секунду при полном напоре (ведро = 30 порций)")]
        [SerializeField] private float _portionsPerSecond = 8f;
        [Tooltip("Ниже этого напора не наливается")]
        [Range(0f, 1f)][SerializeField] private float _minFlow = 0.12f;

        [Header("Струя")]
        [Tooltip("Пусто — WaterJet на себе, в детях или в родителях")]
        [SerializeField] private WaterJet _jet;
        [Tooltip("Насколько струя может пройти мимо центра горловины и всё равно попасть, м")]
        [SerializeField] private float _mouthRadius = 0.28f;
        [SerializeField] private float _scanInterval = 0.1f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private float _next, _acc, _rippleT;
        private Cookware _lastFull;

        private void Awake()
        {
            if (_jet == null) _jet = GetComponent<WaterJet>();
            if (_jet == null) _jet = GetComponentInChildren<WaterJet>(true);
            if (_jet == null) _jet = GetComponentInParent<WaterJet>();
            if (_jet == null) Debug.LogError($"[Налив] «{name}»: рядом нет WaterJet", this);
        }

        private void Update()
        {
            if (_jet == null || !_jet.IsFlowing || _jet.Flow01 < _minFlow) { _acc = 0f; return; }

            // Вид у всех: круги на воде в таре, куда бьёт струя.
            _rippleT -= Time.deltaTime;
            if (_rippleT <= 0f)
            {
                _rippleT = 0.12f;
                Cookware vis = FindTarget(out Vector3 at);
                CookwareLiquidVisual lv = vis != null ? vis.GetComponentInChildren<CookwareLiquidVisual>() : null;
                if (lv != null && lv.HasLiquid)
                {
                    if (lv.TryGetSurfaceY(at, out float y)) at.y = y;
                    lv.Stir(at, 0.6f * _jet.Flow01);
                    lv.Ripple(at);
                }
            }

            if (!base.IsServerInitialized || Time.time < _next) return;
            float dt = _scanInterval;
            _next = Time.time + _scanInterval;

            Cookware cw = FindTarget(out _);
            if (cw == null) return;
            if (!Accepts(cw, out string why))
            {
                if (_log && _lastFull != cw) { _lastFull = cw; Debug.Log($"[Налив] «{Title(cw)}»: {why}", cw); }
                return;
            }
            ProductDef water = Water();
            if (water == null) return;
            _acc += dt * _portionsPerSecond * _jet.Flow01;
            int n = Mathf.FloorToInt(_acc);
            if (n <= 0) return;
            _acc -= n;
            int put = cw.ServerAddContent(water.Id, water.StartState, n, 0, false);
            if (put <= 0)
            {
                if (_log && _lastFull != cw) { _lastFull = cw; Debug.Log($"[Налив] «{Title(cw)}» полная", cw); }
                return;
            }
            _lastFull = null;
            cw.ServerAgitate(0.4f * _jet.Flow01);
        }

        /// <summary>Тара, в которую сейчас льётся струя: дуга проходит у горловины или конец струи внутри.</summary>
        private Cookware FindTarget(out Vector3 at)
        {
            at = default;
            Vector3[] pts = _jet.PathPointsWorld;
            int count = _jet.PathCount;
            if (pts == null || count < 2) return null;
            Vector3 end = pts[count - 1];
            Cookware best = null;
            float bd = float.MaxValue;
            foreach (Cookware c in Cookware.All)
            {
                if (c == null) continue;
                if (c.IsInside(end)) { at = end; return c; }      // упёрлась в дно/воду внутри
                Vector3 m = c.MouthPoint;
                // упёрлась в кромку/край горловины изнутри — тоже в тару
                float de = new Vector2(end.x - m.x, end.z - m.z).magnitude;
                if (de < _mouthRadius && Mathf.Abs(end.y - m.y) < 0.15f && de < bd) { bd = de; best = c; at = end; }
                // ближайшая к горловине точка дуги, которая идёт СВЕРХУ ВНИЗ через неё
                for (int i = 1; i < count; i++)
                {
                    Vector3 a = pts[i - 1], b = pts[i];
                    if (a.y < m.y || b.y > m.y + 0.05f) continue;    // этот отрезок не пересекает уровень горловины
                    float t = Mathf.Approximately(a.y, b.y) ? 0f : Mathf.Clamp01((a.y - m.y) / (a.y - b.y));
                    Vector3 cross = Vector3.Lerp(a, b, t);
                    float d = new Vector2(cross.x - m.x, cross.z - m.z).magnitude;
                    if (d < _mouthRadius && d < bd) { bd = d; best = c; at = cross; }
                }
            }
            return best;
        }

        private static bool Accepts(Cookware cw, out string why)
        {
            why = null;
            PotLidSocket lid = cw.GetComponent<PotLidSocket>();
            if (lid != null && lid.HasLid) { why = "накрыта крышкой"; return false; }
            PotThermal pot = cw.GetComponent<PotThermal>();
            if (pot != null && pot.IsRuptured) { why = "пробита — сначала заварить"; return false; }
            return true;
        }

        private ProductDef Water()
        {
            if (_water != null) return _water;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || db.Table == null) return null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Table.GetProduct((ushort)id);
                if (p != null && p.Title == "Вода") { _water = p; break; }
            }
            if (_water == null && _log) Debug.LogWarning("[Налив] нет продукта «Вода» — задай Water", this);
            return _water;
        }

        private static string Title(Cookware cw) => cw.Def != null ? cw.Def.Title : cw.name;
    }
}
