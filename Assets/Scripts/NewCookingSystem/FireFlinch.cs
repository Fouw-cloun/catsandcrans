using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v17: «ГОРЯЧО!» — свой игрок отпрыгивает от огня, как кошка.
    ///
    ///   • вплотную к РАСКАЛЁННОМУ металлу (ковш с SurfaceHeatGlow, нагрев ≥ Hot From) —
    ///     отскок от него;
    ///   • в упор попал под СТРУЮ горелки (своей или чужой) — отскок вбок/назад от струи.
    ///     Только очень близко: первые Jet Reach метров струи и почти касание тела.
    ///
    /// Отскок — тот же «выброс», что после подвеса крана (FirstPersonController.SetCraneSuspended
    /// с начальной скоростью): игрок подпрыгивает и отлетает, коллизии работают как обычно.
    /// Потом Cooldown секунд не срабатывает. Лежит после лужи, висит на лестнице, в технике,
    /// открыто меню — не срабатывает. Только у своего игрока; другие видят прыжок по сети как обычно.
    ///
    /// Ничего вешать не надо: объект создаётся сам.
    /// </summary>
    public sealed class FireFlinch : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>С какого нагрева металла (0..1) обжигает.</summary>
        public static float HotFrom = 0.55f;
        /// <summary>Насколько близко к металлу, м (от поверхности капсулы).</summary>
        public static float TouchDistance = 0.12f;
        /// <summary>Какая часть струи обжигает (от сопла), м.</summary>
        public static float JetReach = 0.7f;
        public static float HopSpeed = 8f;          // v17.3: дальше
        public static float HopUp = 7f;             // v17.3: заметно выше
        public static float Cooldown = 1.5f;
        /// <summary>v17.2: отладка — раз в секунду пишет в консоль «[Горячо?] …»: что рядом, насколько горячо, почему не сработало.</summary>
        public static bool DebugLog = true;
        private float _dbgT;

        private static FireFlinch _inst;
        private FirstPersonController _fpc;
        private CharacterController _cc;
        private float _findT, _coolUntil, _msgUntil;
        private string _msg;
        private GUIStyle _style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_inst != null) return;
            var go = new GameObject("CC Fire Flinch (auto)") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<FireFlinch>();
        }

        private void Update()
        {
            if (!Enabled) return;
            if (_fpc == null || !_fpc.IsOwner)
            {
                _fpc = null;
                _findT -= Time.deltaTime;
                if (_findT > 0f) return;
                _findT = 1f;
                foreach (FirstPersonController f in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
                    if (f != null && f.IsOwner) { _fpc = f; _cc = f.GetComponent<CharacterController>(); break; }
                if (_fpc == null) return;
            }
            if (_cc == null) return;
            Bounds pb = _cc.bounds;
            float r = _cc.radius * Mathf.Max(Mathf.Abs(_fpc.transform.lossyScale.x), Mathf.Abs(_fpc.transform.lossyScale.z));
            Vector3 a = new Vector3(pb.center.x, pb.min.y + r, pb.center.z);
            Vector3 b = new Vector3(pb.center.x, pb.max.y - r, pb.center.z);

            _dbgT -= Time.deltaTime;
            if (DebugLog && _dbgT <= 0f) { _dbgT = 1f; Debug.Log("[Горячо?] " + Diagnose(pb, r, a, b)); }

            if (!_cc.enabled) return;
            if (Time.time < _coolUntil) return;
            if (PuddleSlip.LocalDown || _fpc.CraneSuspended || _fpc.IsClimbing || UiModal.AnyOpen) return;
            Vector3 away;
            string why;
            if (CheckJets(a, b, r, out away, out why) || CheckHotMetal(pb, r, out away, out why)) Flinch(away, why);
        }

        private bool CheckJets(Vector3 a, Vector3 b, float r, out Vector3 away, out string why)
        {
            away = Vector3.zero; why = null;
            var jets = HeatJets.All;
            for (int i = 0; i < jets.Count; i++)
            {
                IHeatJet j = jets[i];
                if (j == null || (j is Object o && o == null) || !j.JetActive) continue;
                Vector3 o0 = j.JetOrigin, d = j.JetDirection;
                float L = Mathf.Min(JetReach, Mathf.Max(0.05f, j.JetLength));
                Vector3 pj, pp;
                ClosestSegSeg(o0, o0 + d * L, a, b, out pj, out pp);
                float hitR = r + Mathf.Clamp(j.JetRadius * 0.5f, 0.02f, 0.12f);
                if ((pp - pj).sqrMagnitude > hitR * hitR) continue;
                if (Vector3.Dot(pp - o0, d) < 0.02f) continue;            // за соплом — не жжёт
                Vector3 off = Vector3.ProjectOnPlane(pp - pj, Vector3.up);
                Vector3 flat = Vector3.ProjectOnPlane(d, Vector3.up);
                away = (off.sqrMagnitude > 1e-4f ? off.normalized : Vector3.zero) + (flat.sqrMagnitude > 1e-4f ? flat.normalized * 0.8f : Vector3.zero);
                if (away.sqrMagnitude < 1e-4f) away = -_fpc.transform.forward;
                why = "горелка";
                return true;
            }
            return false;
        }

        private bool CheckHotMetal(Bounds pb, float r, out Vector3 away, out string why)
        {
            away = Vector3.zero; why = null;
            foreach (SurfaceHeatGlow g in SurfaceHeatGlow.All)
            {
                if (g == null || g.MaxHeat01 < HotFrom) continue;
                Bounds mb;
                if (!g.TryBounds(out mb)) continue;
                // по высоте должны пересекаться
                if (mb.max.y < pb.min.y + 0.05f || mb.min.y > pb.max.y) continue;
                Vector3 c = new Vector3(pb.center.x, Mathf.Clamp(pb.center.y, mb.min.y, mb.max.y), pb.center.z);
                Vector3 cp = mb.ClosestPoint(c);
                Vector3 flat = c - cp; flat.y = 0f;
                float dist = flat.magnitude - r;
                if (dist > TouchDistance) continue;
                Vector3 fromC = c - mb.center; fromC.y = 0f;
                away = flat.sqrMagnitude > 1e-6f ? flat.normalized : (fromC.sqrMagnitude > 1e-6f ? fromC.normalized : -_fpc.transform.forward);
                why = "«" + g.name + "» раскалён";
                return true;
            }
            return false;
        }

        private void Flinch(Vector3 away, string why)
        {
            away.y = 0f;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : -_fpc.transform.forward;
            _fpc.SetCraneSuspended(false, away * HopSpeed + Vector3.up * HopUp);      // подпрыжка с отлётом
            _coolUntil = Time.time + Cooldown;
            _msg = Random.value < 0.5f ? "ГОРЯЧО! Мяу!" : "Ай! Горячо!";
            _msgUntil = Time.time + 1.1f;
            Vector3 p = _fpc.transform.position;
            for (int i = 0; i < 3; i++)
                CCHeatFx.Wisp(p + Vector3.up * Random.Range(0.4f, 1.2f) - away * 0.3f + Random.insideUnitSphere * 0.15f,
                              Vector3.up * 0.4f - away * 0.3f, Random.Range(0.25f, 0.4f), new Color(0.6f, 0.58f, 0.56f, 0.35f), 0.9f);
            Debug.Log("[Горячо] отпрыгнул: " + why);
        }

        /// <summary>Отладка: что рядом и почему (не) обжигает.</summary>
        private string Diagnose(Bounds pb, float r, Vector3 a, Vector3 b)
        {
            var sb = new System.Text.StringBuilder();
            if (!_cc.enabled) sb.Append("капсула выключена; ");
            if (Time.time < _coolUntil) sb.Append("перерыв " + (_coolUntil - Time.time).ToString("0.0") + " с; ");
            if (PuddleSlip.LocalDown) sb.Append("лежит после лужи; ");
            if (_fpc.CraneSuspended) sb.Append("в технике/подвес; ");
            if (_fpc.IsClimbing) sb.Append("на лестнице; ");
            if (UiModal.AnyOpen) sb.Append("открыто меню; ");

            // металл
            int glows = 0;
            SurfaceHeatGlow bestG = null; float bestD = float.MaxValue; float bestH = 0f; bool bestYOk = false; bool bestHasB = false;
            foreach (SurfaceHeatGlow g in SurfaceHeatGlow.All)
            {
                if (g == null) continue;
                glows++;
                Bounds mb;
                bool hasB = g.TryBounds(out mb);
                float d = float.MaxValue; bool yOk = false;
                if (hasB)
                {
                    yOk = !(mb.max.y < pb.min.y + 0.05f || mb.min.y > pb.max.y);
                    Vector3 c = new Vector3(pb.center.x, Mathf.Clamp(pb.center.y, mb.min.y, mb.max.y), pb.center.z);
                    Vector3 cp = mb.ClosestPoint(c);
                    Vector3 flat = c - cp; flat.y = 0f;
                    d = flat.magnitude - r;
                }
                if (d < bestD) { bestD = d; bestG = g; bestH = g.MaxHeat01; bestYOk = yOk; bestHasB = hasB; }
            }
            if (glows == 0) sb.Append("металла с нагревом (SurfaceHeatGlow) в сцене нет; ");
            else if (bestG != null)
            {
                sb.Append("ближний металл «" + bestG.name + "»: нагрев " + bestH.ToString("0.00") + " (нужно ≥ " + HotFrom.ToString("0.00") + ")");
                if (!bestHasB) sb.Append(", нет поверхностей (Surfaces пусто)");
                else
                {
                    sb.Append(", до металла " + (bestD > 1e5f ? "—" : bestD.ToString("0.00")) + " м (нужно ≤ " + TouchDistance.ToString("0.00") + ")");
                    if (!bestYOk) sb.Append(", по высоте не пересекаемся");
                }
                sb.Append("; ");
            }

            // струи
            var jets = HeatJets.All;
            int active = 0; float bestJ = float.MaxValue;
            for (int i = 0; i < jets.Count; i++)
            {
                IHeatJet j = jets[i];
                if (j == null || (j is Object o && o == null) || !j.JetActive) continue;
                active++;
                float L = Mathf.Min(JetReach, Mathf.Max(0.05f, j.JetLength));
                Vector3 pj, pp;
                ClosestSegSeg(j.JetOrigin, j.JetOrigin + j.JetDirection * L, a, b, out pj, out pp);
                float dd = (pp - pj).magnitude - r - Mathf.Clamp(j.JetRadius * 0.5f, 0.02f, 0.12f);
                if (dd < bestJ) bestJ = dd;
            }
            sb.Append("горящих струй " + active);
            if (active > 0) sb.Append(", ближняя до тела " + Mathf.Max(0f, bestJ).ToString("0.00") + " м (обжигает при 0, первые " + JetReach.ToString("0.0") + " м струи)");
            return sb.ToString();
        }

        /// <summary>Ближайшие точки двух отрезков.</summary>
        private static void ClosestSegSeg(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, rr = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, rr);
            float s, t;
            if (a <= 1e-8f && e <= 1e-8f) { c1 = p1; c2 = p2; return; }
            if (a <= 1e-8f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, rr);
                if (e <= 1e-8f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float bb = Vector3.Dot(d1, d2), den = a * e - bb * bb;
                    s = den > 1e-8f ? Mathf.Clamp01((bb * f - c * e) / den) : 0f;
                    t = (bb * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((bb - c) / a); }
                }
            }
            c1 = p1 + d1 * s; c2 = p2 + d2 * t;
        }

        private void OnGUI()
        {
            if (Time.time >= _msgUntil || string.IsNullOrEmpty(_msg)) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _style.normal.textColor = new Color(1f, 0.6f, 0.2f);
            }
            float w = 360f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.3f, w, 50f), _msg, _style);
        }

        private void OnDestroy() { if (_inst == this) _inst = null; }
    }
}
