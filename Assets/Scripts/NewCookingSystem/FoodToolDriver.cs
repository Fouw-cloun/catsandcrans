using FishNet;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// МОСТ «ГОТОВЫЙ ИНСТРУМЕНТ → FoodTool».
    ///
    /// Бензопила, болгарка, отбойник, горелка, пистолет-струя уже умеют всё
    /// физическое: крутятся, вибрируют, синхронизируют это по сети. Новой
    /// кулинарии от них нужно одно — «работает сейчас или нет» и насколько
    /// хорошо. Этот компонент читает это на сервере и выставляет FoodTool.Active.
    ///
    /// Старые обработчики (GrinderFoodProcessor, ChainsawFoodProcessor…) не
    /// трогаются и продолжают работать со старым FoodItem. Новый FoodPiece
    /// обрабатывает только FoodTool. Одна железка — две системы, без конфликта,
    /// пока старую не уберёте.
    ///
    /// ═══ КАК ВЕШАТЬ ═══
    ///
    ///   ChainsawItem (корень префаба)
    ///   └─ FoodZone           ← пустой дочерний объект у полотна/диска/жала
    ///        SphereCollider или CapsuleCollider, Is Trigger
    ///        FoodTool          Process = Резать / Чистить / Толочь …
    ///        FoodToolDriver    Source = Auto
    ///
    /// Rigidbody на зону не нужен: он есть у продукта.
    ///
    /// ═══ ЧТО ЧИТАЕТ ═══
    ///
    ///   Auto       — найдёт сам в родителях, в порядке ниже
    ///   Spinning   — ISpinningTool: болгарка, шлифмашинка, миксер (см. ниже)
    ///   Saw        — ISawTool: бензопила, ножовка
    ///   Jackhammer — JackhammerItem.IsRunning
    ///   BlowTorch  — BlowTorchItem.IsBurning
    ///   Nozzle     — NozzleGunItem.IsFiring (мойка струёй)
    ///   AlwaysOn   — всегда, пока объект включён (станок, измельчитель)
    ///
    /// МИКСЕР публичного состояния пока не отдаёт. Добавь в MixerItem
    /// реализацию ISpinningTool — три строки, как и задумано в ISpinningTool:
    ///     public class MixerItem : …, ISpinningTool
    ///     public bool IsActive => _spinning.Value &amp;&amp; !_stuck.Value;
    ///     public float Power01 => 1f;
    ///     public Transform WorkPoint => _whisk;   // венчик, как он у тебя назван
    /// </summary>
    [RequireComponent(typeof(FoodTool))]
    public sealed class FoodToolDriver : MonoBehaviour
    {
        public enum Source : byte
        {
            Auto = 0,
            Spinning = 1,
            Saw = 2,
            Jackhammer = 3,
            BlowTorch = 4,
            Nozzle = 5,
            AlwaysOn = 6,
        }

        [SerializeField] private Source _source = Source.Auto;

        [Tooltip("Чей режим читать. Пусто — ищется в родителях.")]
        [SerializeField] private MonoBehaviour _tool;

        [Tooltip("Множитель скорости обработки для этого инструмента")]
        [Range(0.1f, 5f)][SerializeField] private float _speed = 1f;

        [Tooltip("Взять рабочую точку у ISpinningTool, если у FoodTool своя не задана")]
        [SerializeField] private bool _useToolWorkPoint = true;

        private FoodTool _ft;
        private ISpinningTool _spin;
        private ISawTool _saw;
        private JackhammerItem _jack;
        private BlowTorchItem _torch;
        private NozzleGunItem _nozzle;

        private void Awake()
        {
            _ft = GetComponent<FoodTool>();
            _ft.AutoRun = false;
            Resolve();

            if (_useToolWorkPoint && _ft.WorkPoint == null && _spin != null && _spin.WorkPoint != null)
                _ft.WorkPoint = _spin.WorkPoint;
        }

        private void Resolve()
        {
            Component root = _tool != null ? (Component)_tool : this;

            if (_source == Source.Auto || _source == Source.Spinning)
                _spin = root.GetComponentInParent<ISpinningTool>();
            if (_source == Source.Auto || _source == Source.Saw)
                _saw = root.GetComponentInParent<ISawTool>();
            if (_source == Source.Auto || _source == Source.Jackhammer)
                _jack = root.GetComponentInParent<JackhammerItem>();
            if (_source == Source.Auto || _source == Source.BlowTorch)
                _torch = root.GetComponentInParent<BlowTorchItem>();
            if (_source == Source.Auto || _source == Source.Nozzle)
                _nozzle = root.GetComponentInParent<NozzleGunItem>();

            if (_source == Source.Auto)
            {
                if (_spin != null) _source = Source.Spinning;
                else if (_saw != null) _source = Source.Saw;
                else if (_jack != null) _source = Source.Jackhammer;
                else if (_torch != null) _source = Source.BlowTorch;
                else if (_nozzle != null) _source = Source.Nozzle;
                else
                {
                    Debug.LogWarning($"[FoodToolDriver] «{name}»: не нашёл инструмент в родителях. " +
                                     "Укажи Source и Tool вручную или поставь AlwaysOn.", this);
                    enabled = false;
                }
            }
        }

        private void Update()
        {
            if (!InstanceFinder.IsServerStarted) return;

            bool on;
            float quality = 1f;

            switch (_source)
            {
                case Source.Spinning:
                    on = _spin != null && _spin.IsActive;
                    quality = _spin != null ? Mathf.Clamp01(_spin.Power01) : 0f;
                    break;
                case Source.Saw:        on = _saw != null && _saw.IsSawing; break;
                case Source.Jackhammer: on = _jack != null && _jack.IsRunning; break;
                case Source.BlowTorch:  on = _torch != null && _torch.IsBurning; break;
                case Source.Nozzle:     on = _nozzle != null && _nozzle.IsFiring; break;
                case Source.AlwaysOn:   on = true; break;
                default:                on = false; break;
            }

            _ft.Active = on;
            _ft.Quality = quality;
            _ft.Speed = _speed;
        }

        private void OnDisable()
        {
            if (_ft != null) _ft.Active = false;
        }
    }
}
