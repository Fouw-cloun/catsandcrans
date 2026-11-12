using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// СТРУЯ ЖИДКОСТИ — гладкая трубка. Только визуал, у каждого игрока свой.
    ///
    /// ═══ v3: СТРУЯ С ИНЕРЦИЕЙ ═══
    /// Раньше вся дуга пересчитывалась каждый кадр от носика — повёл ведром, и
    /// струя дёргалась целиком, как палка. Теперь струя — цепочка «капель-узлов»:
    /// носик выпускает их 60 раз в секунду, каждая летит сама по баллистике.
    /// Поэтому:
    ///   • качнул ведро — по струе бежит плавная волна (изгиб догоняет);
    ///   • идёшь и льёшь — струя отстаёт и красиво изгибается;
    ///   • перестал лить — струя отрывается от носика и падает целиком;
    ///   • внизу струя сужается (разгоняется), по ней бегут прожилки;
    ///   • падает в тару — ныряет до поверхности молока (а не в воздух над ней);
    ///   • где упала — вспышка, корона капель, круги; от струи отрываются капли.
    ///
    /// ═══ v4: МУЛЬТЯШНАЯ СТРУЯ ═══
    ///   • свой шейдер CatCooks/Liquid Stream: цвет ступеньками, прожилки бегут вниз,
    ///     пена у кромки и внизу, рваный хвост, блик-полоса;
    ///   • сечение — эллипс: из носика — круглая струя, с широкого края — «простыня»,
    ///     которая по пути сворачивается в струю (SetLip);
    ///   • характер жидкости — LiquidPourProfile (вода, молоко, масло, мёд, битум...):
    ///     вязкая медленнее, толще, почти без брызг;
    ///   • удар — корона-всплеск и пенное пятно (LiquidImpactFx) + капли.
    ///
    /// API прежний (всё новое — по желанию):
    ///   var s = LiquidStreamRibbon.Create(transform);
    ///   s.Profile = LiquidPourProfile.Honey();          // необязательно: иначе по цвету
    ///   s.SetLip(lipTangent, lipWidth);                 // необязательно: слив широким краем
    ///   s.Tick(on, start, velocity, width, color, ignoreRoot, splashPerSecond);
    /// </summary>
    public sealed class LiquidStreamRibbon : MonoBehaviour
    {
        private const int Sides = 12;
        private const int MaxNodes = 90;
        private const float EmitDt = 1f / 60f;
        private const float MaxLife = 1.6f;

        private struct Node
        {
            public Vector3 P, V;
            public float R0, Speed0, Arc, Age, HitAge;
            public float W0;                         // ширина поперёк у кромки (простыня), м
            public Vector3 Across;                   // направление кромки
            public bool Hit;
            public CookwareLiquidVisual Dive;       // летит внутрь тары — ищем поверхность жидкости
            public Vector3 Checked;                  // v10.4: откуда считать следующий луч (лучи через кадр)
            public bool HasChecked;
        }

        private Mesh _mesh;
        private MeshRenderer _mr;
        private MaterialPropertyBlock _mpb;
        private readonly List<Node> _nodes = new List<Node>(MaxNodes);   // 0 — самый новый (у носика)
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Color32> _c = new List<Color32>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Vector2> _uv1 = new List<Vector2>();
        private readonly List<Vector2> _uv2 = new List<Vector2>();
        private readonly List<float> _pw = new List<float>();
        private readonly List<float> _pg = new List<float>();
        private readonly List<Vector3> _px = new List<Vector3>();
        private readonly List<int> _t = new List<int>();
        private readonly List<Vector3> _pp = new List<Vector3>();
        private readonly List<float> _pr = new List<float>();
        private readonly List<float> _pa = new List<float>();
        private readonly RaycastHit[] _hits = new RaycastHit[12];
        private ParticleSystem _splash;
        private float _amount, _emitAcc, _arc, _impactT, _dropT, _landT = 99f;
        private Vector3 _lastStart, _srcVel, _velS;
        private bool _hasLast;
        private Color _color = Color.white;
        private static Material _mat;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int DeepId = Shader.PropertyToID("_Deep");
        private static readonly int MidId = Shader.PropertyToID("_Mid");
        private static readonly int LightId = Shader.PropertyToID("_Light");
        private static readonly int FoamColId = Shader.PropertyToID("_FoamColor");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int FlowId = Shader.PropertyToID("_FlowSpeed");
        private static readonly int StreakScaleId = Shader.PropertyToID("_StreakScale");
        private static readonly int StreakAmtId = Shader.PropertyToID("_StreakAmount");
        private static readonly int LipFoamId = Shader.PropertyToID("_LipFoam");
        private static readonly int BottomFoamId = Shader.PropertyToID("_BottomFoam");
        private static readonly int BreakUpId = Shader.PropertyToID("_BreakUp");
        private static readonly int WobbleId = Shader.PropertyToID("_Wobble");
        private static readonly int GlossId = Shader.PropertyToID("_Gloss");

        /// <summary>Характер жидкости. null — подбирается по цвету.</summary>
        public LiquidPourProfile Profile;
        /// <summary>
        /// v7: «шланг» — струя к концу не сужается, а расширяется конусом:
        /// 0 — как раньше, 1 — к концу вдвое толще, 2 — втрое.
        /// </summary>
        public float Flare;
        /// <summary>v7: брызг-капель в секунду, слетающих с толстой части струи (как у шланга). 0 — нет.</summary>
        public float SprayPerSecond;
        private float _sprayAcc;
        /// <summary>
        /// v9: «вес» струи — множитель тяжести для узлов (1 — как раньше). Тяжелее — струя
        /// падает круче, ровнее, не болтается. Хозяин должен считать падение с тем же множителем.
        /// </summary>
        public float GravityScale = 1f;
        /// <summary>v9: цельность 0..1 — меньше дрожи, разрывов хвоста и «ломкости» (1 — ровный жгут).</summary>
        public float Steady;
        /// <summary>v10: полосы пены по струе (как у водопада): &lt;0 — сами (молоко — да), 0 — нет, 0..1 — сила.</summary>
        public float Bands = -1f;
        /// <summary>v10: пенные комочки в месте падения, в секунду (0 — нет).</summary>
        public float LandingFoam = 14f;
        private float _foamAcc;
        private static readonly int BandsId = Shader.PropertyToID("_Bands");
        private Vector3 _lipTangent;
        private float _lipWidth;
        private LiquidImpactFx.FoamPatch _patch;
        private float _crownT;
        private Vector3 _landNormal = Vector3.up;

        /// <summary>Куда струя упала (если Landed).</summary>
        public Vector3 LandingPoint { get; private set; }
        /// <summary>Струя сейчас касается поверхности/пола.</summary>
        public bool Landed => _landT < 0.15f;

        /// <summary>
        /// Сила удара 0..1 — по скорости падения в точке касания. Льёшь с высоты —
        /// корона выше и шире, брызг больше; льёшь низко над тарой — тихо.
        /// </summary>
        public float ImpactStrength => _impactK;
        private float _impactK = 0.5f, _impactSpeed;

        public static LiquidStreamRibbon Create(Transform owner)
        {
            GameObject go = new GameObject("LiquidStream (auto)") { hideFlags = HideFlags.DontSave, layer = owner.gameObject.layer };
            LiquidStreamRibbon s = go.AddComponent<LiquidStreamRibbon>();
            s._owner = owner;
            s._hasOwner = owner != null;
            s.Init();
            return s;
        }

        private void Init()
        {
            _mesh = new Mesh { name = "LiquidStream", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = gameObject.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = Mat;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mpb = new MaterialPropertyBlock();
            _splash = LiquidFx.CreateStream(transform, 0.022f, 1.2f);
            _patch = new LiquidImpactFx.FoamPatch(transform);
            // v9.2: точка падения — за ней едут корона и «след рикошета»
            _impactAnchor = new GameObject("ImpactAnchor") { hideFlags = HideFlags.DontSave }.transform;
            _impactAnchor.SetParent(transform, false);
        }

        private Transform _impactAnchor;

        /// <summary>
        /// Слив широким краем (ведро, кастрюля, противень): tangent — направление кромки,
        /// lipWidth — ширина «простыни» у края, м. Действует на следующий Tick.
        /// </summary>
        public void SetLip(Vector3 tangent, float lipWidth)
        {
            _lipTangent = tangent;
            _lipWidth = Mathf.Max(0f, lipWidth);
        }

        /// <summary>Для прогрева шейдеров (LiquidFx.Warmup).</summary>
        public static Material WarmupMaterial => Mat;

        private static Material Mat
        {
            get
            {
                if (_mat != null) return _mat;
                Shader sh = Shader.Find("CatCooks/Liquid Stream");
                if (sh == null) sh = Shader.Find("CatCooks/Liquid Surface");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                _mat = new Material(sh) { name = "CC Liquid Stream (auto)", hideFlags = HideFlags.DontSave };
                if (_mat.HasProperty("_RippleStrength")) _mat.SetFloat("_RippleStrength", 0.25f);
                if (_mat.HasProperty("_EdgeSoftness")) _mat.SetFloat("_EdgeSoftness", 0.05f);
                if (_mat.HasProperty("_Opacity")) _mat.SetFloat("_Opacity", 0.97f);
                if (_mat.HasProperty("_FlowStreaks")) _mat.SetFloat("_FlowStreaks", 1f);
                if (_mat.HasProperty("_FresnelStrength")) _mat.SetFloat("_FresnelStrength", 0.5f);
                if (_mat.HasProperty("_SheenStrength")) _mat.SetFloat("_SheenStrength", 0.4f);
                return _mat;
            }
        }

        /// <summary>
        /// Каждый кадр. on — льётся ли; start — откуда (кромка/носик); velocity — скорость вылета;
        /// width — толщина у носика, м; ignore — что пропускать (сам льющий).
        /// </summary>
        public void Tick(bool on, Vector3 start, Vector3 velocity, float width, Color color, Transform ignore,
                         float splashPerSecond = 60f)
        {
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 0.05f);
            if (!_selfTick) { _lastTickFrame = Time.frameCount; _ignore = ignore; }
            _color = color;
            LiquidPourProfile prof = LiquidPourProfile.Pick(Profile, color);
            float visc = prof.Viscosity;
            // вязкая — медленнее вытекает и толще (та же формула у сервера — SpeedScale)
            velocity *= prof.SpeedScale;
            width *= Mathf.Lerp(1f, 1.25f, visc);
            float lipW = _lipWidth;
            Vector3 lipT = _lipTangent;
            _lipWidth = 0f;
            _amount = Mathf.MoveTowards(_amount, on ? 1f : 0f, dt * (on ? 5f : 8f));

            // Скорость самого носика (идёшь с ведром) — струя её наследует.
            if (_hasLast)
            {
                Vector3 sv = (start - _lastStart) / dt;
                if (sv.sqrMagnitude > 100f) sv = _srcVel;                   // телепорт
                _srcVel = Vector3.Lerp(_srcVel, sv, 1f - Mathf.Exp(-dt / 0.05f));
            }
            _lastStart = start; _hasLast = true;
            // Скорость вылета сглажена: рывки кромки не ломают струю.
            _velS = _nodes.Count == 0 || !on ? velocity : Vector3.Lerp(_velS, velocity, 1f - Mathf.Exp(-dt / Mathf.Lerp(0.06f, 0.18f, Steady)));

            float r0 = width * 0.5f * Mathf.SmoothStep(0f, 1f, _amount);

            // ── выпуск узлов ──
            if (on)
            {
                _emitAcc += dt;
                int guard = 0;
                while (_emitAcc >= EmitDt && guard++ < 6)
                {
                    _emitAcc -= EmitDt;
                    Vector3 v0 = _velS + _srcVel * Mathf.Lerp(0.85f, 0.45f, Steady);   // v9: тяжёлая меньше «хлещет» от рывков
                    float pre = _emitAcc;                                   // догнать остаток кадра
                    Vector3 across = lipT.sqrMagnitude > 1e-6f ? lipT.normalized : Vector3.Cross(v0.sqrMagnitude > 1e-6f ? v0.normalized : Vector3.down, Vector3.up);
                    if (across.sqrMagnitude < 1e-6f) across = Vector3.right;
                    var n = new Node
                    {
                        P = start + v0 * pre + 0.5f * Physics.gravity * GravityScale * pre * pre,
                        V = v0 + Physics.gravity * GravityScale * pre,
                        R0 = r0, Speed0 = Mathf.Max(0.2f, v0.magnitude), Arc = _arc, Age = pre,
                        W0 = Mathf.Max(r0 * 2f, lipW * Mathf.SmoothStep(0f, 1f, _amount)),
                        Across = across.normalized,
                    };
                    _arc += v0.magnitude * EmitDt;
                    _nodes.Insert(0, n);
                }
                if (_nodes.Count > MaxNodes) _nodes.RemoveRange(MaxNodes, _nodes.Count - MaxNodes);
            }
            else _emitAcc = 0f;

            // ── полёт ──
            _landT += dt;
            _impactT -= dt;
            int firstHit = -1;
            for (int i = 0; i < _nodes.Count; i++)
            {
                Node n = _nodes[i];
                n.Age += dt;
                if (n.Hit) { n.HitAge += dt; _nodes[i] = n; if (firstHit < 0) firstHit = i; continue; }
                Vector3 prev = n.P;
                n.V += Physics.gravity * GravityScale * dt;
                n.V *= 1f - 0.08f * dt * (1f - Steady * 0.8f);              // лёгкое сопротивление воздуха (тяжёлой — меньше)
                n.P += n.V * dt;

                if (n.Dive != null)
                {
                    if (n.Dive.TryGetSurfaceY(n.P, out float sy) && n.P.y <= sy)
                    {
                        n.P.y = sy;
                        Land(ref n, Vector3.up, true);
                    }
                }
                else if (((Time.frameCount + i) & 1) == 0 || i >= _nodes.Count - 3)
                {
                    // v10.4: луч у каждого узла — через кадр (от прошлой проверки, поэтому без пропусков);
                    // последние узлы хвоста — каждый кадр (они и падают)
                    Vector3 from = n.HasChecked ? n.Checked : prev;
                    n.Checked = n.P; n.HasChecked = true;
                    Vector3 seg = n.P - from;
                    float len = seg.magnitude;
                    if (len > 1e-6f && Hit(from, seg / len, len, ignore, out RaycastHit h, out CookwareLiquidVisual dive))
                    {
                        if (dive != null && dive.TryGetSurfaceY(h.point, out float sy) && sy < h.point.y - 0.005f)
                        {
                            n.Dive = dive;                                  // влетела в тару — ныряем до молока
                            if (n.P.y <= sy) { n.P.y = sy; Land(ref n, Vector3.up, true); }
                        }
                        else
                        {
                            n.P = h.point;
                            Land(ref n, h.normal, dive != null);
                        }
                    }
                }
                _nodes[i] = n;
                if (n.Hit && firstHit < 0) firstHit = i;
            }
            // Всё, что старше упавшего узла, — уже «в луже».
            if (firstHit >= 0 && firstHit + 1 < _nodes.Count) _nodes.RemoveRange(firstHit + 1, _nodes.Count - firstHit - 1);
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                Node n = _nodes[i];
                if (n.Age > MaxLife || (n.Hit && n.HitAge > 0.08f && (i > 0 || !on))) _nodes.RemoveAt(i);
                else break;
            }

            // ── капли, отрывающиеся от струи ──
            _dropT -= dt;
            if (_nodes.Count > 6 && _dropT <= 0f && prof.DropsPerSecond > 0.01f)
            {
                _dropT = Random.Range(0.6f, 1.4f) / Mathf.Max(0.1f, prof.DropsPerSecond * (1f - 0.7f * Steady));
                Node n = _nodes[Random.Range(_nodes.Count / 3, _nodes.Count)];
                if (!n.Hit)
                    LiquidFx.Drop(n.P + Random.insideUnitSphere * n.R0, n.V + Random.insideUnitSphere * 0.25f,
                                  Mathf.Max(0.004f, n.R0 * Random.Range(0.4f, 0.8f)), color, 0.6f);
            }

            // ── v7: брызги со струи — чем дальше от носика, тем шире разлёт ──
            if (on && SprayPerSecond > 0f && _nodes.Count > 4)
            {
                _sprayAcc += dt * SprayPerSecond * _amount * (1f - visc * 0.6f);
                int ns = Mathf.Min(8, (int)_sprayAcc);
                _sprayAcc -= ns;
                for (int k = 0; k < ns; k++)
                {
                    float u = Mathf.Sqrt(Random.value);                          // чаще к концу
                    int idx = Mathf.Clamp((int)(u * (_nodes.Count - 1)), 0, _nodes.Count - 1);
                    Node sn = _nodes[idx];
                    if (sn.Hit) continue;
                    Vector3 fwd = sn.V.sqrMagnitude > 1e-6f ? sn.V.normalized : Vector3.down;
                    Vector3 side = Vector3.ProjectOnPlane(Random.onUnitSphere, fwd).normalized;
                    float rr = sn.R0 * FlareK(sn.Age);
                    LiquidFx.Drop(sn.P + side * rr * 1.05f, sn.V * Random.Range(0.85f, 1.05f) + side * Random.Range(0.25f, 0.8f) * (0.4f + u),
                                  Mathf.Max(0.005f, sn.R0 * Random.Range(0.35f, 0.7f)), color, Random.Range(0.35f, 0.6f));
                }
            }
            else _sprayAcc = 0f;

            bool visible = _nodes.Count >= 1 && (on || _nodes.Count >= 2);
            _mr.enabled = visible;
            if (visible)
            {
                BuildTube(on ? start : (Vector3?)null, r0, lipW > r0 * 2f ? Mathf.Max(r0 * 2f, lipW * _amount) : r0 * 2f,
                          lipT, prof.Necking);
                ApplyLook(prof, color);
            }
            else _mesh.Clear();

            if (Landed) _impactK = Mathf.Lerp(_impactK, Mathf.InverseLerp(0.8f, 5f, _impactSpeed), 1f - Mathf.Exp(-dt / 0.15f));
            float impactScale = Mathf.Lerp(0.6f, 1.35f, _impactK);

            CCRuntimeParticles.SetStream(_splash, Landed && on, LandingPoint + Vector3.up * 0.01f, Vector3.up,
                                         color, Color.Lerp(color, Color.white, 0.5f),
                                         splashPerSecond * 0.45f * _amount * (1f - visc * 0.85f) * impactScale);

            // ── удар: корона-всплеск пульсами + пенное пятно (размер — от силы удара) ──
            float hitR = Mathf.Max(0.006f, r0 * 0.6f) * impactScale;
            if (_impactAnchor != null && Landed) _impactAnchor.position = LandingPoint;
            if (Landed && on)
            {
                _crownT -= dt;
                if (_crownT <= 0f)
                {
                    _crownT = Mathf.Lerp(0.09f, 0.3f, visc) * Random.Range(0.8f, 1.25f);
                    LiquidImpactFx.Crown(LandingPoint, _landNormal, hitR, prof, color, _impactAnchor);
                }
            }
            _patch.Tick(Landed && on, LandingPoint, _landNormal, hitR, prof, color, dt);

            // v10: пена в месте падения — объёмные комочки кольцом (как пена под водопадом)
            if (Landed && on && LandingFoam > 0f)
            {
                _foamAcc += dt * LandingFoam * _amount * (1f - visc * 0.5f);
                int nf = Mathf.Min(6, (int)_foamAcc);
                _foamAcc -= nf;
                Vector3 nrm = _landNormal;
                Color fc = Color.Lerp(color, Color.white, 0.6f);
                for (int k = 0; k < nf; k++)
                {
                    Vector3 hd = Vector3.ProjectOnPlane(Random.onUnitSphere, nrm);
                    if (hd.sqrMagnitude < 1e-6f) continue;
                    hd.Normalize();
                    float sz = hitR * Random.Range(0.7f, 1.6f);
                    LiquidFx.Poof(LandingPoint + hd * hitR * Random.Range(0.8f, 1.6f) + nrm * sz * 0.2f,
                                  hd * Random.Range(0.25f, 0.6f) + nrm * Random.Range(0.1f, 0.35f), sz, fc, Random.Range(0.35f, 0.6f));
                }
                // веер быстрых вытянутых брызг (как в splash-туториале)
                if (Random.value < dt * 10f)
                {
                    int ns = Random.Range(2, 5);
                    for (int k = 0; k < ns; k++)
                    {
                        Vector3 hd = Vector3.ProjectOnPlane(Random.onUnitSphere, nrm).normalized;
                        LiquidFx.Jet(LandingPoint + nrm * 0.01f, (hd * Random.Range(0.6f, 1.2f) + nrm * Random.Range(1.2f, 2.2f)) * (0.7f + _impactK * 0.6f),
                                     Mathf.Max(0.006f, hitR * Random.Range(0.25f, 0.45f)), color, Random.Range(0.3f, 0.5f));
                    }
                }
            }
            else _foamAcc = 0f;
        }

        /// <summary>v7: во сколько раз толще струя у узла этого возраста (конус «шланга»).</summary>
        private float FlareK(float age)
        {
            if (Flare <= 0f) return 1f;
            return 1f + Flare * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.45f));
        }

        private void ApplyLook(LiquidPourProfile prof, Color liquid)
        {
            prof.Resolve(liquid, out Color deep, out Color mid, out Color light, out Color foam);
            _mpb.SetColor(BaseColorId, liquid);
            _mpb.SetColor(DeepId, deep);
            _mpb.SetColor(MidId, mid);
            _mpb.SetColor(LightId, light);
            _mpb.SetColor(FoamColId, foam);
            _mpb.SetFloat(OpacityId, prof.Opacity);
            _mpb.SetFloat(FlowId, prof.FlowSpeed);
            _mpb.SetVector(StreakScaleId, new Vector4(prof.StreakScale.x, prof.StreakScale.y, 0f, 0f));
            _mpb.SetFloat(StreakAmtId, prof.StreakAmount);
            _mpb.SetFloat(LipFoamId, prof.LipFoam);
            _mpb.SetFloat(BottomFoamId, prof.BottomFoam);
            _mpb.SetFloat(BreakUpId, prof.BreakUp * (1f - Steady));
            _mpb.SetFloat(WobbleId, prof.Wobble * (1f - 0.8f * Steady));
            _mpb.SetFloat(GlossId, prof.Gloss);
            float bands = Bands;
            if (bands < 0f)
            {
                Color.RGBToHSV(liquid, out float hh, out float ss, out float vv);
                bands = vv > 0.78f && ss < 0.3f ? 0.55f : 0f;                     // молоко/сливки — полосы
            }
            _mpb.SetFloat(BandsId, bands);
            _mr.SetPropertyBlock(_mpb);
        }

        private void Land(ref Node n, Vector3 normal, bool intoLiquid)
        {
            // Скорость падения — до остановки узла: от неё сила всплеска.
            _impactSpeed = Vector3.Dot(n.V, -(normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up));
            n.Hit = true;
            n.HitAge = 0f;
            n.V = Vector3.zero;
            LandingPoint = n.P;
            _landNormal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            _landT = 0f;
            if (_impactT > 0f) return;
            _impactT = intoLiquid ? 0.06f : 0.09f;
            float sp01 = Mathf.InverseLerp(0.8f, 5f, _impactSpeed);           // 0.8 м/с — тихо, 5 м/с (≈1.3 м падения) — сильно
            float power = (Mathf.Clamp01(n.R0 / 0.03f) * 0.8f + 0.2f) * Mathf.Lerp(0.45f, 1.25f, sp01);
            LiquidFx.ImpactSplash(n.P + normal * 0.003f, normal, _color, power, intoLiquid ? 0.8f : 1f);
        }

        private bool Hit(Vector3 from, Vector3 dir, float len, Transform ignore, out RaycastHit best, out CookwareLiquidVisual dive)
        {
            int n = Physics.RaycastNonAlloc(from, dir, _hits, len, ~0, QueryTriggerInteraction.Collide);
            best = default(RaycastHit);
            dive = null;
            float bd = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (ignore != null && h.transform.IsChildOf(ignore)) continue;
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                if (h.collider.isTrigger && h.collider.GetComponentInParent<Cookware>() == null) continue;
                if (h.distance >= bd) continue;
                bd = h.distance; best = h; found = true;
            }
            if (found)
            {
                Cookware cw = best.collider.GetComponentInParent<Cookware>();
                if (cw != null) dive = cw.GetComponentInChildren<CookwareLiquidVisual>();
            }
            return found;
        }

        private void BuildTube(Vector3? head, float headR, float headW, Vector3 headAcross, float necking)
        {
            _pp.Clear(); _pr.Clear(); _pa.Clear(); _pw.Clear(); _pg.Clear(); _px.Clear();
            if (head.HasValue)
            {
                _pp.Add(head.Value); _pr.Add(headR); _pa.Add(_arc); _pw.Add(headW); _pg.Add(0f);
                _px.Add(headAcross.sqrMagnitude > 1e-6f ? headAcross.normalized : (_nodes.Count > 0 ? _nodes[0].Across : Vector3.right));
            }
            int count = _nodes.Count;
            bool tailHit = false;
            for (int i = 0; i < count; i++)
            {
                Node n = _nodes[i];
                // Разгоняется — тоньше (сохранение расхода), не тоньше трети.
                float thin = Mathf.Clamp(Mathf.Sqrt(n.Speed0 / Mathf.Max(0.2f, n.V.magnitude)), Mathf.Lerp(0.33f, 0.7f, Steady), 1f);
                // v7: «шланг» — вместо сужения конус к концу
                if (Flare > 0f) thin = Mathf.Lerp(thin, FlareK(n.Age), Mathf.Clamp01(Flare));
                if (n.Hit) { thin = Flare > 0f ? Mathf.Max(0.45f, FlareK(n.Age) * 0.8f) : 0.45f; tailHit = true; }
                // «Простыня» с края по пути сворачивается в струю (поверхностное натяжение)
                float k = Mathf.Exp(-n.Age / Mathf.Max(0.05f, necking));
                float w = Mathf.Lerp(n.R0 * 2f, n.W0, k) * Mathf.Lerp(thin, 1f, 0.35f);
                _pp.Add(n.P); _pr.Add(n.R0 * thin); _pa.Add(n.Arc); _pw.Add(w); _pg.Add(n.Age); _px.Add(n.Across);
            }
            int pc = _pp.Count;
            if (pc < 2) { _mesh.Clear(); return; }
            // Кончики скруглены: оторвавшаяся голова и хвост сходят на нет.
            for (int i = 0; i < pc; i++)
            {
                float fromHead = head.HasValue ? 1f : Mathf.Clamp01(i / 2.5f);
                float fromTail = Mathf.Clamp01((pc - 1 - i) / 1.5f + 0.35f);
                float m = Mathf.Min(fromHead, fromTail);
                _pr[i] *= m;
                _pw[i] *= m;
            }

            _v.Clear(); _n.Clear(); _c.Clear(); _uv.Clear(); _uv1.Clear(); _uv2.Clear(); _t.Clear();
            Color32 white = new Color32(255, 255, 255, 255);
            Vector3 prevA = Vector3.zero;
            for (int i = 0; i < pc; i++)
            {
                Vector3 a = _pp[Mathf.Max(0, i - 1)], b = _pp[Mathf.Min(pc - 1, i + 1)];
                Vector3 fwd = (b - a).sqrMagnitude > 1e-10f ? (b - a).normalized : Vector3.down;
                // ось «поперёк» — кромка, перпендикулярно полёту
                Vector3 A = Vector3.ProjectOnPlane(_px[i], fwd);
                if (A.sqrMagnitude < 1e-6f) A = Vector3.Cross(fwd, Vector3.up);
                if (A.sqrMagnitude < 1e-6f) A = prevA.sqrMagnitude > 0f ? prevA : Vector3.Cross(fwd, Vector3.right);
                A.Normalize();
                if (i > 0 && Vector3.Dot(A, prevA) < 0f) A = -A;                  // без перекрутов
                prevA = A;
                Vector3 B = Vector3.Cross(fwd, A);
                float th = Mathf.Max(0.0005f, _pr[i]);                            // полутолщина
                float hw = Mathf.Max(th, _pw[i] * 0.5f);                          // полуширина
                float sheet = Mathf.Clamp01((hw / th - 1f) / 2.5f);
                float frac = i / (float)(pc - 1);
                float nearHit = tailHit && i >= pc - 3 ? 1f : 0f;
                for (int s = 0; s <= Sides; s++)                                   // +1 столбец — шов без скачка UV
                {
                    float ang = s / (float)Sides * Mathf.PI * 2f;
                    float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                    // эллипс: у простыни края тоньше и немного загнуты (как язык воды)
                    float bend = sheet * cs * cs * th * 0.6f;
                    Vector3 dir = A * (cs * hw) + B * (sn * th + bend);
                    _v.Add(_pp[i] + dir);
                    _n.Add((A * (cs / hw) + B * (sn / th)).normalized);
                    _c.Add(white);
                    _uv.Add(new Vector2(s / (float)Sides, _pa[i]));
                    _uv1.Add(new Vector2(frac, sheet));
                    _uv2.Add(new Vector2(th, nearHit));
                }
            }
            const int Ring = Sides + 1;
            for (int i = 0; i < pc - 1; i++)
            for (int s = 0; s < Sides; s++)
            {
                int a0 = i * Ring + s, a1 = a0 + 1;
                int b0 = a0 + Ring, b1 = a1 + Ring;
                _t.Add(a0); _t.Add(b0); _t.Add(a1);
                _t.Add(a1); _t.Add(b0); _t.Add(b1);
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetNormals(_n);
            _mesh.SetColors(_c);
            _mesh.SetUVs(0, _uv);
            _mesh.SetUVs(1, _uv1);
            _mesh.SetUVs(2, _uv2);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
        }

        // ── Хозяин перестал звать Tick (предмет выключили, спрятали в руке, удалили) ──
        // Раньше струя так и застывала в воздухе. Теперь она сама доигрывает: новые
        // узлы не выпускаются, летящие долетают и гаснут. Хозяин удалён — убираемся.
        private Transform _owner, _ignore;
        private bool _hasOwner, _selfTick;
        private int _lastTickFrame = -10;

        private void LateUpdate()
        {
            if (Time.frameCount - _lastTickFrame <= 1) return;          // хозяин тикает сам
            if (_nodes.Count > 0 || _amount > 0.001f || (_mr != null && _mr.enabled))
            {
                _selfTick = true;
                try { Tick(false, _lastStart, Vector3.zero, 0f, _color, _ignore, 0f); }
                finally { _selfTick = false; }
                return;
            }
            if (_hasOwner && _owner == null) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_mesh != null) SafeDestroy.Kill(_mesh);
            _patch?.Destroy();
        }
    }
}
