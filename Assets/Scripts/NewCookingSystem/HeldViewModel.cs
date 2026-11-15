using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CatCooks.Food
{
    /// <summary>
    /// v19.3: ПРЕДМЕТ В РУКЕ «КАК ОРУЖИЕ В ШУТЕРЕ» (viewmodel / weapon camera).
    ///
    /// Как в играх от первого лица: предмет в руке рисуется ОТДЕЛЬНОЙ камерой поверх мира
    /// (свой слой HeldOverlay, своя глубина). Поэтому он:
    ///   • никогда не проходит сквозь стену/стол на экране — мир просто не может его перекрыть;
    ///   • не отъезжает к лицу и не дёргается — стоит в руке ровно, где задано;
    ///   • физически ни во что не упирается (коллайдеры в руке и так выключены).
    ///
    /// Но готовке нужно, чтобы лопата честно заходила В ведро, венчик — в тару, ложка — в кучку.
    /// Поэтому «поверх мира» включается, только когда рядом НЕТ рабочей цели:
    ///   смотришь на тару / продукт / кучку / лужу ближе Work Reach, или предмет уже у тары —
    ///   предмет снова рисуется в мире (как раньше: упирается в большое, проходит в малую тару).
    ///
    /// Работает только у своего игрока (у остальных предмет виден обычно).
    /// Нужен слой «HeldOverlay» — создаётся сам (CatCooks → Рука → Слой HeldOverlay). Нет слоя — всё по-старому.
    /// Тень от предмета в режиме «поверх мира» не рисуется (как у оружия в большинстве шутеров).
    /// </summary>
    public sealed class HeldViewModel
    {
        public const string LayerName = "HeldOverlay";

        /// <summary>Общий выключатель (для проверки: false — как было в v19.2).</summary>
        public static bool Enabled = false;   // v19.5: выключено — полоса у горизонта
        /// <summary>Дальность «рабочей цели» от камеры, м.</summary>
        public static float WorkReach = 3.2f;
        /// <summary>Предмет ближе этого к таре — рабочий режим, м.</summary>
        public static float NearCookware = 0.45f;
        /// <summary>Сколько держать «мировой» режим после того, как цель пропала, с (без мигания).</summary>
        public static float WorldHold = 0.35f;

        private static int _layer = -2;
        private static bool _warned;

        public static int Layer
        {
            get
            {
                if (_layer == -2) _layer = LayerMask.NameToLayer(LayerName);
                return _layer;
            }
        }

        private readonly Camera _main;
        private Camera _overlay;
        private bool _stackOk, _stackTried;
        private InteractableItem _item;
        private readonly List<GameObject> _objs = new List<GameObject>();
        private readonly List<int> _origLayers = new List<int>();
        private bool _on;
        private float _rescanT, _worldUntil;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly Collider[] _near = new Collider[16];

        public HeldViewModel(Camera main) { _main = main; }

        public bool Available
        {
            get
            {
                if (!Enabled || _main == null) return false;
                if (Layer < 0)
                {
                    if (!_warned)
                    {
                        _warned = true;
                        Debug.LogWarning($"[В руке] нет слоя «{LayerName}» — предмет в руке рисуется по-старому. " +
                                         "Меню CatCooks → Рука → Слой HeldOverlay (или добавь слой в Tags and Layers).");
                    }
                    return false;
                }
                return EnsureOverlay();
            }
        }

        /// <summary>Сейчас предмет рисуется поверх мира.</summary>
        public bool Active => _on;

        /// <summary>Рабочая цель рядом: смотрим на тару/продукт/кучку/лужу или предмет у тары.</summary>
        public bool NearWork(Transform item)
        {
            if (_main == null || item == null) return false;
            bool work = false;
            Transform cam = _main.transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, WorkReach, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i].collider;
                if (c == null || c.transform.IsChildOf(item)) continue;
                if (c.GetComponentInParent<FirstPersonController>() != null) continue;
                if (_hits[i].distance >= best) continue;
                bool w = IsWork(c);
                // ближайшее твёрдое (стена) закрывает то, что за ним; триггеры тары считаем целью
                if (!c.isTrigger || w) { best = _hits[i].distance; work = w; }
            }
            if (!work)
            {
                int m = Physics.OverlapSphereNonAlloc(item.position, NearCookware, _near, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < m && !work; i++)
                {
                    Collider c = _near[i];
                    if (c == null || c.transform.IsChildOf(item)) continue;
                    if (c.GetComponentInParent<Cookware>() != null) work = true;
                }
            }
            if (work) _worldUntil = Time.time + WorldHold;
            return work || Time.time < _worldUntil;
        }

        private static bool IsWork(Collider c)
        {
            return c.GetComponentInParent<Cookware>() != null
                || c.GetComponentInParent<FoodPiece>() != null
                || c.GetComponentInParent<FoodHeap>() != null
                || c.GetComponentInParent<LiquidPuddle>() != null;
        }

        /// <summary>Каждый кадр (LateUpdate руки). item = null — ничего в руке.</summary>
        public void Tick(InteractableItem item, bool overlay)
        {
            if (item != _item) { Restore(); _item = item; }
            if (item == null || !overlay || !Available)
            {
                if (_on) Restore();
                if (_overlay != null) _overlay.enabled = false;
                return;
            }
            if (!_on || Time.time >= _rescanT) Apply(item.transform);
            _overlay.enabled = true;
            _overlay.fieldOfView = _main.fieldOfView;
            _overlay.nearClipPlane = Mathf.Min(0.01f, _main.nearClipPlane);
            _overlay.farClipPlane = 30f;
        }

        public void Dispose()
        {
            Restore();
            if (_overlay != null) Object.Destroy(_overlay.gameObject);
        }

        // ───────── слои ─────────

        private void Apply(Transform root)
        {
            _rescanT = Time.time + 0.5f;            // подхватить то, что появилось под предметом (кучка на лопате)
            int L = Layer;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                GameObject g = r.gameObject;
                if (g.layer == L || HasTrigger(g)) continue;   // триггеры (лезвие, щуп) не трогаем — по ним работает логика
                _objs.Add(g);
                _origLayers.Add(g.layer);
                g.layer = L;
            }
            _on = true;
        }

        private static bool HasTrigger(GameObject g)
        {
            Collider[] cs = g.GetComponents<Collider>();
            for (int i = 0; i < cs.Length; i++)
                if (cs[i] != null && cs[i].enabled && cs[i].isTrigger) return true;
            return false;
        }

        private void Restore()
        {
            for (int i = 0; i < _objs.Count; i++)
                if (_objs[i] != null) _objs[i].layer = _origLayers[i];
            _objs.Clear();
            _origLayers.Clear();
            _on = false;
        }

        // ───────── камера поверх мира ─────────

        private bool EnsureOverlay()
        {
            if (_overlay != null) return _stackOk;
            if (_stackTried) return false;
            _stackTried = true;
            UniversalAdditionalCameraData md = _main.GetUniversalAdditionalCameraData();
            if (md == null || md.renderType != CameraRenderType.Base)
            {
                Debug.LogWarning("[В руке] камера игрока не Base (URP) — режим «поверх мира» выключен", _main);
                return false;
            }
            var g = new GameObject("HeldOverlayCam (auto)");
            g.transform.SetParent(_main.transform, false);
            _overlay = g.AddComponent<Camera>();
            _overlay.clearFlags = CameraClearFlags.Depth;
            _overlay.cullingMask = 1 << Layer;
            _overlay.depth = _main.depth + 1f;
            _overlay.allowHDR = _main.allowHDR;
            _overlay.allowMSAA = _main.allowMSAA;
            UniversalAdditionalCameraData od = _overlay.GetUniversalAdditionalCameraData();
            od.renderType = CameraRenderType.Overlay;
            od.renderShadows = false;
            // v19.4: пост-эффекты у оверлея ВЫКЛ. С ними URP откладывал пост-обработку мира до этой камеры,
            // а у неё глубина очищена — размытая полоса у горизонта и «синие» артефакты.
            od.renderPostProcessing = false;
            if (!md.cameraStack.Contains(_overlay)) md.cameraStack.Add(_overlay);
            _main.cullingMask &= ~(1 << Layer);                 // основная камера этот слой не рисует
            _overlay.enabled = false;
            _stackOk = true;
            Debug.Log("[В руке] предмет в руке рисуется поверх мира, когда рядом нет тары/продуктов (как оружие в шутерах)", _main);
            return true;
        }
    }
}
