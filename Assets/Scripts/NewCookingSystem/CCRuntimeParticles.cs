using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЧАСТИЦЫ, СОБРАННЫЕ ИЗ КОДА. Без префабов и без настройки в редакторе.
    ///
    /// Два вида:
    ///   • Burst  — разовый выброс (плевок кожурой, шлепок): создаётся, выстреливает,
    ///              удаляет себя сам.
    ///   • Stream — поток, пока идёт работа (стружка из-под диска): живёт на
    ///              инструменте, его включают и выключают, цвет и направление
    ///              меняются на ходу.
    ///
    /// Частицы — маленькие вращающиеся квадратики с гравитацией и отскоком от
    /// пола. Для стружки, крошки и комков кожуры это выглядит как надо.
    ///
    /// Материал общий на всех: Sprites/Default есть в любой сборке и красит
    /// частицы их собственным цветом.
    /// </summary>
    public static class CCRuntimeParticles
    {
        private static Material _mat;

        public static Material SharedMaterial
        {
            get
            {
                if (_mat != null) return _mat;
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                _mat = new Material(sh) { name = "CC Runtime Particles" };
                return _mat;
            }
        }

        /// <summary>Настройки одного вида частиц.</summary>
        public struct Look
        {
            public Vector2 Lifetime;     // сек, от..до
            public Vector2 Speed;        // м/с
            public Vector2 Size;         // м
            public float Gravity;        // множитель гравитации
            public float ConeAngle;      // разброс, градусы
            public bool Collide;         // отскакивать от мира
            public float Tumble;         // вращение, град/с
            public float Stretch;        // >0 — вытянуты по скорости (стружка, струйки)
            public float Drag;           // сопротивление воздуха

            public static Look Chips => new Look
            {
                Lifetime = new Vector2(0.4f, 0.9f), Speed = new Vector2(1.5f, 3.5f),
                Size = new Vector2(0.012f, 0.03f), Gravity = 1.1f, ConeAngle = 35f,
                Collide = true, Tumble = 540f,
            };

            /// <summary>Стружка: вытянутые по полёту палочки — струя из измельчителя.</summary>
            public static Look Strands => new Look
            {
                Lifetime = new Vector2(0.5f, 1.0f), Speed = new Vector2(1.2f, 2.8f),
                Size = new Vector2(0.01f, 0.022f), Gravity = 1.0f, ConeAngle = 14f,
                Collide = true, Tumble = 0f, Stretch = 2.2f, Drag = 0f,
            };

            /// <summary>Пыль/сок: мелкая взвесь, медленно оседает.</summary>
            public static Look Dust => new Look
            {
                Lifetime = new Vector2(0.4f, 0.9f), Speed = new Vector2(0.3f, 1.0f),
                Size = new Vector2(0.006f, 0.014f), Gravity = 0.25f, ConeAngle = 50f,
                Collide = false, Tumble = 90f, Drag = 1.5f,
            };

            /// <summary>Тот же вид, но крупнее — для мира со Scale 5.</summary>
            public Look Scaled(float k)
            {
                Look l = this;
                l.Size *= k;
                l.Speed *= Mathf.Sqrt(k);
                return l;
            }

            public static Look Gunk => new Look
            {
                Lifetime = new Vector2(0.6f, 1.2f), Speed = new Vector2(2.5f, 5.5f),
                Size = new Vector2(0.03f, 0.08f), Gravity = 1.3f, ConeAngle = 30f,
                Collide = true, Tumble = 200f,
            };
        }

        /// <summary>Разовый выброс. Удалит себя сам.</summary>
        public static void Burst(Vector3 at, Vector3 dir, Color a, Color b, int count, Look look, Material material = null)
        {
            ParticleSystem ps = Create("CC Burst", look, false, material);
            ps.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Safe(dir)));
            ParticleSystem.MainModule m = ps.main;
            m.startColor = new ParticleSystem.MinMaxGradient(a, b);
            ps.gameObject.SetActive(true);
            ps.Emit(Mathf.Max(1, count));
            Object.Destroy(ps.gameObject, look.Lifetime.y + 0.5f);
        }

        /// <summary>Поток. Вешается дочерним на инструмент. Включать — SetStream.</summary>
        public static ParticleSystem CreateStream(Transform parent, Look look, Material material = null)
        {
            ParticleSystem ps = Create("CC Stream", look, true, material);
            ps.transform.SetParent(parent, false);
            ps.gameObject.SetActive(true);
            return ps;
        }

        /// <summary>Обновить поток: где, куда, каким цветом, сколько в секунду.</summary>
        public static void SetStream(ParticleSystem ps, bool on, Vector3 at, Vector3 dir,
                                     Color a, Color b, float perSecond, float speed = -1f)
        {
            if (ps == null) return;
            ParticleSystem.EmissionModule e = ps.emission;
            e.rateOverTime = on ? perSecond : 0f;
            if (!on) return;
            ps.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Safe(dir)));
            ParticleSystem.MainModule m = ps.main;
            m.startColor = new ParticleSystem.MinMaxGradient(a, b);
            // Скорость можно задать снаружи: струя измельчителя должна лететь
            // по той же дуге, по которой станок считает место падения.
            if (speed > 0f) m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.85f, speed * 1.15f);
        }

        private static ParticleSystem Create(string name, Look look, bool looping, Material material = null)
        {
            GameObject go = new GameObject(name);
            // Выключенным: система частиц начинает играть при AddComponent,
            // а часть настроек нельзя менять у играющей.
            go.SetActive(false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule m = ps.main;
            m.playOnAwake = looping;
            m.loop = looping;
            m.duration = looping ? 1f : 0.1f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(look.Lifetime.x, look.Lifetime.y);
            m.startSpeed = new ParticleSystem.MinMaxCurve(look.Speed.x, look.Speed.y);
            m.startSize = new ParticleSystem.MinMaxCurve(look.Size.x, look.Size.y);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.gravityModifier = look.Gravity;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = 400;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = true;
            e.rateOverTime = 0f;

            ParticleSystem.ShapeModule s = ps.shape;
            s.shapeType = ParticleSystemShapeType.Cone;
            s.angle = look.ConeAngle;
            s.radius = 0.01f;

            if (look.Tumble > 0f)
            {
                ParticleSystem.RotationOverLifetimeModule r = ps.rotationOverLifetime;
                r.enabled = true;
                float rad = look.Tumble * Mathf.Deg2Rad;
                r.z = new ParticleSystem.MinMaxCurve(-rad, rad);
            }

            // Уменьшаются к концу жизни — не пропадают рывком.
            ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.75f, 0.9f), new Keyframe(1f, 0f)));

            if (look.Collide)
            {
                ParticleSystem.CollisionModule c = ps.collision;
                c.enabled = true;
                c.type = ParticleSystemCollisionType.World;
                c.mode = ParticleSystemCollisionMode.Collision3D;
                c.quality = ParticleSystemCollisionQuality.Low;
                c.dampen = 0.7f;
                c.bounce = 0.15f;
                c.lifetimeLoss = 0.15f;
            }

            if (look.Drag > 0f)
            {
                ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
                lv.enabled = true;
                lv.drag = look.Drag;
                lv.limit = 100f;
            }

            ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = material != null ? material : SharedMaterial;
            if (look.Stretch > 0f)
            {
                pr.renderMode = ParticleSystemRenderMode.Stretch;
                pr.lengthScale = look.Stretch;
                pr.velocityScale = 0.02f;
            }
            else pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;

            if (looping) ps.Play();
            return ps;
        }

        private static Vector3 Safe(Vector3 d) => d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.up;
    }
}
