using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ГДЕ СТРУЯ БЬЁТ — мультяшный всплеск. Только визуал, у каждого свой.
    ///  • Crown(...) — корона: кольцо растёт, поднимается и рассыпается ячейками (аниме-всплеск);
    ///  • FoamPatch — пенное пятно под струёй: кольца пены бегут наружу, в центре белая шапка.
    /// Короны берутся из пула (без мусора в памяти), обновляются одним скрытым объектом.
    /// </summary>
    public static class LiquidImpactFx
    {
        private const int PoolSize = 24;
        private static readonly int DeepId = Shader.PropertyToID("_Deep");
        private static readonly int MidId = Shader.PropertyToID("_Mid");
        private static readonly int LightId = Shader.PropertyToID("_Light");
        private static readonly int FoamId = Shader.PropertyToID("_FoamColor");
        private static readonly int AgeId = Shader.PropertyToID("_Age");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int SpinId = Shader.PropertyToID("_Spin");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        private class CrownFx
        {
            public Transform T; public MeshRenderer R; public float Born, Life, R0, H0; public bool Busy;
        }

        private static Mesh _crownMesh, _discMesh;
        private static Material _mat;
        private static readonly List<CrownFx> _pool = new List<CrownFx>();
        private static MaterialPropertyBlock _mpb;
        private static Runner _runner;

        private sealed class Runner : MonoBehaviour
        {
            private void LateUpdate() => Step(Time.time);
        }

        public static Material Mat
        {
            get
            {
                if (_mat != null) return _mat;
                Shader sh = Shader.Find("CatCooks/Liquid Splash");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                _mat = new Material(sh) { name = "CC Liquid Splash (auto)", hideFlags = HideFlags.DontSave };
                return _mat;
            }
        }

        private static bool Ensure()
        {
            if (!Application.isPlaying) return false;
            if (_runner != null) return true;
            var go = new GameObject("CC Liquid Impact FX (auto)") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
            _mpb = new MaterialPropertyBlock();
            _pool.Clear();
            for (int i = 0; i < PoolSize; i++)
            {
                var c = new GameObject("Crown") { hideFlags = HideFlags.DontSave };
                c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = CrownMesh;
                var r = c.AddComponent<MeshRenderer>();
                r.sharedMaterial = Mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                c.SetActive(false);
                _pool.Add(new CrownFx { T = c.transform, R = r });
            }
            return true;
        }

        /// <summary>Открытое кольцо, расширяющееся кверху (как корона капли). uv: x — обхват, y — высота.</summary>
        public static Mesh CrownMesh
        {
            get
            {
                if (_crownMesh != null) return _crownMesh;
                const int S = 24;
                float[] hs = { 0f, 0.3f, 0.62f, 0.85f, 1f };
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                for (int k = 0; k < hs.Length; k++)
                {
                    float h = hs[k];
                    float r = 1f + h * h * 0.7f;                  // края короны раскрываются наружу
                    for (int i = 0; i <= S; i++)
                    {
                        float a = i / (float)S * Mathf.PI * 2f;
                        Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        v.Add(d * r + Vector3.up * h);
                        n.Add((d - Vector3.up * 0.3f * h).normalized);
                        uv.Add(new Vector2(i / (float)S, h));
                    }
                }
                for (int k = 0; k < hs.Length - 1; k++)
                for (int i = 0; i < S; i++)
                {
                    int a0 = k * (S + 1) + i, a1 = a0 + 1, b0 = a0 + S + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
                }
                _crownMesh = new Mesh { name = "CC Splash Crown", hideFlags = HideFlags.DontSave };
                _crownMesh.SetVertices(v); _crownMesh.SetNormals(n); _crownMesh.SetUVs(0, uv); _crownMesh.SetTriangles(t, 0);
                _crownMesh.RecalculateBounds();
                return _crownMesh;
            }
        }

        /// <summary>Круг-пятно. uv: x — угол 0..1, y — радиус 0..1.</summary>
        public static Mesh DiscMesh
        {
            get
            {
                if (_discMesh != null) return _discMesh;
                const int S = 32, Rn = 6;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                for (int k = 0; k <= Rn; k++)
                {
                    float r = k / (float)Rn;
                    for (int i = 0; i <= S; i++)
                    {
                        float a = i / (float)S * Mathf.PI * 2f;
                        v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                        n.Add(Vector3.up);
                        uv.Add(new Vector2(i / (float)S, r));
                    }
                }
                for (int k = 0; k < Rn; k++)
                for (int i = 0; i < S; i++)
                {
                    int a0 = k * (S + 1) + i, a1 = a0 + 1, b0 = a0 + S + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(a1); t.Add(b0); t.Add(a1); t.Add(b1); t.Add(b0);
                }
                _discMesh = new Mesh { name = "CC Splash Disc", hideFlags = HideFlags.DontSave };
                _discMesh.SetVertices(v); _discMesh.SetNormals(n); _discMesh.SetUVs(0, uv); _discMesh.SetTriangles(t, 0);
                _discMesh.RecalculateBounds();
                return _discMesh;
            }
        }

        /// <summary>
        /// Корона-всплеск: радиус struiRadius — толщина струи, м. Живёт ~0.3–0.5 с.
        /// </summary>
        public static void Crown(Vector3 pos, Vector3 normal, float streamRadius, LiquidPourProfile prof, Color liquid)
        {
            if (!Ensure()) return;
            CrownFx c = null;
            float oldest = float.MaxValue;
            foreach (CrownFx k in _pool)
            {
                if (!k.Busy) { c = k; break; }
                if (k.Born < oldest) { oldest = k.Born; c = k; }
            }
            if (c == null) return;
            if (prof == null) prof = LiquidPourProfile.For(liquid);
            float visc = prof.Viscosity;
            c.Busy = true;
            c.Born = Time.time;
            c.Life = Mathf.Lerp(0.34f, 0.6f, visc) * Random.Range(0.85f, 1.15f);
            c.R0 = Mathf.Max(0.01f, streamRadius) * prof.SplashSize * 0.5f * Random.Range(0.8f, 1.2f);
            c.H0 = c.R0 * Mathf.Lerp(1.3f, 0.5f, visc) * Random.Range(0.8f, 1.2f);
            c.T.position = pos + normal * 0.002f;
            c.T.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.value * 360f, 0f);
            c.T.gameObject.SetActive(true);
            prof.Resolve(liquid, out Color deep, out Color mid, out Color light, out Color foam);
            if (!prof.FromLiquidColor) { deep = prof.Deep; mid = prof.Mid; light = prof.Light; foam = prof.Foam; }
            _mpb.Clear();
            _mpb.SetColor(DeepId, deep); _mpb.SetColor(MidId, mid); _mpb.SetColor(LightId, light); _mpb.SetColor(FoamId, foam);
            _mpb.SetFloat(ModeId, 0f);
            _mpb.SetFloat(SeedId, Random.value * 50f);
            _mpb.SetFloat(AgeId, 0f);
            _mpb.SetFloat(OpacityId, Mathf.Max(0.7f, prof.Opacity));
            c.R.SetPropertyBlock(_mpb);
            Scale(c, 0f);
        }

        private static void Scale(CrownFx c, float age)
        {
            // растёт быстро и плавно тормозит; поднимается и опадает
            float grow = 1f - (1f - age) * (1f - age) * (1f - age);
            float r = c.R0 * Mathf.Lerp(0.55f, 1.5f, grow);
            float h = c.H0 * Mathf.Sin(Mathf.Clamp01(age * 1.15f) * Mathf.PI) * 1.1f + c.H0 * 0.05f;
            c.T.localScale = new Vector3(r, Mathf.Max(0.0005f, h), r);
        }

        private static void Step(float now)
        {
            foreach (CrownFx c in _pool)
            {
                if (!c.Busy) continue;
                float age = (now - c.Born) / Mathf.Max(0.05f, c.Life);
                if (age >= 1f) { c.Busy = false; c.T.gameObject.SetActive(false); continue; }
                Scale(c, age);
                c.R.GetPropertyBlock(_mpb);
                _mpb.SetFloat(AgeId, age);
                c.R.SetPropertyBlock(_mpb);
            }
        }

        // ─────────────────────── пенное пятно ───────────────────────

        /// <summary>Пятно пены под струёй — одно на струю, плавно появляется/исчезает.</summary>
        public sealed class FoamPatch
        {
            private readonly Transform _t;
            private readonly MeshRenderer _r;
            private readonly MaterialPropertyBlock _pb = new MaterialPropertyBlock();
            private float _k, _r0;
            private Vector3 _pos, _nrm = Vector3.up;

            public FoamPatch(Transform parent)
            {
                var go = new GameObject("FoamPatch") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = DiscMesh;
                _r = go.AddComponent<MeshRenderer>();
                _r.sharedMaterial = Mat;
                _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _r.receiveShadows = false;
                _t = go.transform;
                go.SetActive(false);
            }

            public void Tick(bool on, Vector3 pos, Vector3 normal, float streamRadius, LiquidPourProfile prof, Color liquid, float dt)
            {
                _k = Mathf.MoveTowards(_k, on ? 1f : 0f, dt * (on ? 4f : 1.5f));
                if (_k <= 0.001f) { if (_t.gameObject.activeSelf) _t.gameObject.SetActive(false); return; }
                if (!_t.gameObject.activeSelf) { _t.gameObject.SetActive(true); _pos = pos; _nrm = normal; }
                if (on)
                {
                    // точка падения гуляет — пятно догоняет мягко
                    float f = 1f - Mathf.Exp(-dt / 0.08f);
                    _pos = Vector3.Lerp(_pos, pos, f);
                    _nrm = Vector3.Slerp(_nrm, normal, f).normalized;
                    _r0 = Mathf.Lerp(_r0 <= 0f ? streamRadius : _r0, streamRadius, f);
                }
                if (prof == null) prof = LiquidPourProfile.For(liquid);
                float rad = Mathf.Max(0.02f, _r0 * prof.FoamPatch) * Mathf.Lerp(0.6f, 1f, _k);
                _t.SetPositionAndRotation(_pos + _nrm * 0.003f, Quaternion.FromToRotation(Vector3.up, _nrm));
                _t.localScale = new Vector3(rad, 1f, rad);
                prof.Resolve(liquid, out Color deep, out Color mid, out Color light, out Color foam);
                if (!prof.FromLiquidColor) { deep = prof.Deep; mid = prof.Mid; light = prof.Light; foam = prof.Foam; }
                _pb.SetColor(DeepId, deep); _pb.SetColor(MidId, mid); _pb.SetColor(LightId, light); _pb.SetColor(FoamId, foam);
                _pb.SetFloat(ModeId, 1f);
                _pb.SetFloat(AgeId, 1f - _k);
                _pb.SetFloat(SpinId, Mathf.Lerp(1f, 0.25f, prof.Viscosity));
                _pb.SetFloat(OpacityId, 1f);
                _r.SetPropertyBlock(_pb);
            }

            public void Destroy()
            {
                if (_t != null) SafeDestroy.Kill(_t.gameObject);
            }
        }
    }
}
