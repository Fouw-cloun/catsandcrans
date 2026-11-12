using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v14: ОБЩИЕ ЭФФЕКТЫ ЖАРА — марево и лёгкий дымок/пар.
    ///
    ///   • CCHeatFx.Haze(низ, ширина, высота, сила) — «марево» над горячим: каждый кадр, пока горячо.
    ///     Рисуется прямоугольником, повёрнутым к камере (вокруг вертикали), шейдер CatCooks/Heat Haze
    ///     колышет картинку сцены за ним. Нужен Opaque Texture в URP (если выключен — включается сам
    ///     при запуске, в консоли строка «[Жар] Opaque Texture…»; насовсем — меню CatCooks → Огонь и жар).
    ///   • CCHeatFx.Wisp(где, скорость, размер, цвет, жизнь) — мягкий клубок дымка/пара: круглый,
    ///     с неровным краем, растёт, закручивается и тает. Без квадратиков.
    ///
    /// Объекты создаются сами (DontSave), по сети ничего не идёт — каждый игрок рисует по своим данным.
    /// </summary>
    public sealed class CCHeatFx : MonoBehaviour
    {
        /// <summary>Выключатель марева (например, в настройках графики).</summary>
        public static bool HazeEnabled = true;
        /// <summary>Сила марева на всё (1 — как задумано).</summary>
        public static float HazeGlobal = 1f;

        private static CCHeatFx _inst;
        private static bool _checked, _hazeOk = true;

        private Material _hazeMat;
        private Mesh _quad;
        private readonly List<MeshRenderer> _pool = new List<MeshRenderer>();
        private readonly List<MaterialPropertyBlock> _mpb = new List<MaterialPropertyBlock>();
        private int _used;

        private ParticleSystem _wisp;
        private Material _wispMat;
        private static Texture2D _wispTex;

        private struct Req { public Vector3 Bottom; public float W, H, S; }
        private readonly List<Req> _reqs = new List<Req>();

        private static CCHeatFx Inst
        {
            get
            {
                if (_inst != null) return _inst;
                var go = new GameObject("CC Heat Fx (auto)") { hideFlags = HideFlags.DontSave };
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<CCHeatFx>();
                return _inst;
            }
        }

        // ───────────────────────── МАРЕВО ─────────────────────────

        /// <summary>Марево на этот кадр. bottom — низ (у горячего), w×h — размер в метрах, strength 0..1.</summary>
        public static void Haze(Vector3 bottom, float width, float height, float strength)
        {
            if (!HazeEnabled || strength <= 0.01f || width <= 0f || height <= 0f) return;
            if (!Application.isPlaying) return;
            if (!CheckOpaque()) return;
            Inst._reqs.Add(new Req { Bottom = bottom, W = width, H = height, S = Mathf.Clamp01(strength) * HazeGlobal });
        }

        private static bool CheckOpaque()
        {
            if (_checked) return _hazeOk;
            _checked = true;
            Shader sh = Shader.Find("CatCooks/Heat Haze");
            if (sh == null)
            {
                Debug.LogWarning("[Жар] нет шейдера «CatCooks/Heat Haze» — марева не будет. Меню CatCooks → Огонь и жар → Добавить шейдеры в сборку");
                _hazeOk = false;
                return false;
            }
            Object rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (rp == null) return _hazeOk = true;
            System.Reflection.PropertyInfo pi = rp.GetType().GetProperty("supportsCameraOpaqueTexture");
            if (pi == null) return _hazeOk = true;
            bool on = (bool)pi.GetValue(rp, null);
            if (!on)
            {
                if (pi.CanWrite)
                {
                    pi.SetValue(rp, true, null);
                    Debug.Log("[Жар] Opaque Texture в URP был выключен — включил для марева (насовсем: CatCooks → Огонь и жар → Включить Opaque Texture). " +
                              "Если марево серое — у камеры игрока Rendering → Opaque Texture = Use Pipeline Settings");
                }
                else
                {
                    Debug.LogWarning("[Жар] Opaque Texture в URP выключен — марева не будет (URP Asset → Rendering → Opaque Texture)");
                    return _hazeOk = false;
                }
            }
            return _hazeOk = true;
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            _used = 0;
            if (cam != null && _reqs.Count > 0)
            {
                if (_hazeMat == null) BuildHaze();
                Vector3 cp = cam.transform.position;
                for (int i = 0; i < _reqs.Count && _used < 24; i++)
                {
                    Req r = _reqs[i];
                    Vector3 mid = r.Bottom + Vector3.up * r.H * 0.5f;
                    if ((mid - cp).sqrMagnitude > 900f) continue;             // дальше 30 м — не видно
                    MeshRenderer mr = Get(_used);
                    Vector3 to = cp - r.Bottom; to.y = 0f;
                    if (to.sqrMagnitude < 1e-6f) to = -cam.transform.forward;
                    Transform t = mr.transform;
                    // чуть ближе к камере, чтобы не резалось горячим предметом
                    t.position = r.Bottom + to.normalized * Mathf.Min(r.W * 0.35f, 0.4f);
                    t.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up);
                    t.localScale = new Vector3(r.W, r.H, 1f);
                    MaterialPropertyBlock b = _mpb[_used];
                    b.SetFloat("_Strength", 0.022f * r.S);
                    b.SetFloat("_Freq", Mathf.Clamp(6f / Mathf.Max(0.1f, r.H), 4f, 22f) * 1.3f);
                    mr.SetPropertyBlock(b);
                    if (!mr.enabled) mr.enabled = true;
                    _used++;
                }
            }
            for (int i = _used; i < _pool.Count; i++) if (_pool[i].enabled) _pool[i].enabled = false;
            _reqs.Clear();
        }

        private MeshRenderer Get(int i)
        {
            while (_pool.Count <= i)
            {
                var go = new GameObject("Heat Haze " + _pool.Count) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _quad;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _hazeMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = false;
                _pool.Add(mr);
                _mpb.Add(new MaterialPropertyBlock());
            }
            return _pool[i];
        }

        private void BuildHaze()
        {
            _hazeMat = new Material(Shader.Find("CatCooks/Heat Haze")) { name = "Heat Haze (auto)", hideFlags = HideFlags.DontSave };
            _quad = new Mesh { name = "HazeQuad", hideFlags = HideFlags.DontSave };
            _quad.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            _quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            _quad.RecalculateBounds();
            _quad.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 1f));
        }

        // ───────────────────────── ДЫМОК / ПАР ─────────────────────────

        /// <summary>Мягкий клубок: где, начальная скорость, размер (м), цвет (альфа — плотность), жизнь (с).</summary>
        public static void Wisp(Vector3 at, Vector3 velocity, float size, Color color, float life)
        {
            if (!Application.isPlaying) return;
            CCHeatFx fx = Inst;
            if (fx._wisp == null) fx.BuildWisp();
            var ep = new ParticleSystem.EmitParams();
            ep.position = at;
            ep.velocity = velocity;
            ep.startSize = size;
            ep.startColor = color;
            ep.startLifetime = life;
            ep.rotation = Random.Range(0f, 360f);
            fx._wisp.Emit(ep, 1);
        }

        private void BuildWisp()
        {
            var go = new GameObject("CC Wisps") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule m = ps.main;
            m.loop = true; m.playOnAwake = true; m.duration = 1f;
            m.startSpeed = 0f;
            m.gravityModifier = -0.02f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Shape;
            m.maxParticles = 500;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule e = ps.emission; e.enabled = true; e.rateOverTime = 0f;
            ParticleSystem.ShapeModule sh = ps.shape; sh.enabled = false;
            ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.9f)));
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f), new GradientAlphaKey(0.55f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ParticleSystem.NoiseModule nz = ps.noise;                   // закручивается
            nz.enabled = true;
            nz.strength = 0.25f;
            nz.frequency = 1.2f;
            nz.scrollSpeed = 0.4f;
            nz.quality = ParticleSystemNoiseQuality.Medium;
            ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
            lv.enabled = true; lv.drag = 1.2f; lv.limit = 100f;
            ParticleSystem.RotationOverLifetimeModule ro = ps.rotationOverLifetime;
            ro.enabled = true; ro.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.sortingFudge = 2f;
            _wispMat = new Material(CCRuntimeParticles.SharedMaterial) { name = "CC Wisp (auto)", hideFlags = HideFlags.DontSave };
            _wispMat.mainTexture = WispTexture();
            pr.sharedMaterial = _wispMat;
            go.SetActive(true);
            ps.Play();
            _wisp = ps;
        }

        /// <summary>Клубок: неровный край (три «горба»), мягкий спад, внутри — светлее (мультяшно, 2 тона).</summary>
        public static Texture2D WispTexture()
        {
            if (_wispTex != null) return _wispTex;
            const int S = 64;
            _wispTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "CC Wisp", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float ang = Mathf.Atan2(dy, dx);
                    float edge = 0.8f + 0.09f * Mathf.Sin(ang * 3f + 0.4f) + 0.05f * Mathf.Sin(ang * 5f + 1.9f) + 0.03f * Mathf.Sin(ang * 9f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / edge;
                    float a = Mathf.Clamp01((1f - d) / 0.35f);
                    a = a * a * (3f - 2f * a);
                    // мультяшный объём: светлое пятно сверху-слева
                    float hl = Mathf.Clamp01(1f - Mathf.Sqrt((dx + 0.25f) * (dx + 0.25f) + (dy - 0.3f) * (dy - 0.3f)) / 0.55f);
                    byte c = (byte)(Mathf.Lerp(215f, 255f, Mathf.SmoothStep(0f, 1f, hl)));
                    px[y * S + x] = new Color32(c, c, c, (byte)(a * 255f));
                }
            _wispTex.SetPixels32(px);
            _wispTex.Apply();
            return _wispTex;
        }

        private void OnDestroy()
        {
            if (_inst == this) _inst = null;
            Kill(_hazeMat);
            Kill(_wispMat);
            Kill(_quad);
        }

        /// <summary>v17: удалить и в игре, и в редакторе (при выходе из Play Destroy нельзя — только DestroyImmediate).</summary>
        public static void Kill(Object o, float delay = 0f)
        {
            if (o == null) return;
            if (Application.isPlaying) { if (delay > 0f) Destroy(o, delay); else Destroy(o); }
            else DestroyImmediate(o);
        }
    }
}
