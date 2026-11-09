using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Настройки кусков мяса — один ассет Resources/MeatCutSettings (делает меню «Новые туши → 1»).
    ///   Cut Prefab — ОДИН общий префаб куска (Meat Cut). Любая часть и любой стейк — он же:
    ///                форму и продукт кусок получает при отпиле (сам строит меш).
    ///   Продукты частей и ломтей (стейков) — по видам, в порядке CarcassModel.Kind.
    ///   Меши частей целиком — чтобы отпиленный кусок появлялся сразу, без построения.
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Meat Cut Settings", fileName = "MeatCutSettings")]
    public sealed class MeatCutSettings : ScriptableObject
    {
        [Tooltip("Общий префаб куска: Meat Cut + Mesh Filter/Renderer + Box Collider + Rigidbody + Network Object/Transform")]
        public GameObject CutPrefab;
        public Material Material;
        [Tooltip("Масштаб мяса под мир игры: туши (меню 2/3), полки, рёбра (Барбекю → 2) и брискет (Брискет → 2). " +
                 "1 — настоящий размер (свинья ~1.1 м), 4 — в 4 раза больше. После смены — заново меню 2/3, Барбекю → 2, Брискет → 2")]
        [Min(0.25f)] public float Scale = 4f;
        public ProductDef[] PorkKinds = new ProductDef[CarcassModel.KindCount];
        public ProductDef[] PorkSlices = new ProductDef[CarcassModel.KindCount];
        public ProductDef[] BeefKinds = new ProductDef[CarcassModel.KindCount];
        public ProductDef[] BeefSlices = new ProductDef[CarcassModel.KindCount];
        public Mesh[] PorkMeshes = new Mesh[CarcassModel.PartCount];
        public Mesh[] BeefMeshes = new Mesh[CarcassModel.PartCount];

        [Header("Борщ: свои куски вместо обычных")]
        [Tooltip("Свиная грудинка выпадает этим префабом — пласт сала со шкуркой (FoodPiece «Сало»). Пусто — обычным куском. Ставит меню Борщ → 9")]
        public GameObject LardPrefab;
        [Tooltip("Сколько пластов сала с одной грудинки")]
        [Range(1, 4)] public int LardPerBrisket = 2;
        [Tooltip("Говяжья нога выпадает шайбами голяшки (мясо кольцом вокруг мозговой кости, FoodPiece «Говяжья голяшка»). Пусто — обычным куском (пилить пилой). Борщ → 9")]
        public GameObject ShankPrefab;
        [Tooltip("Сколько шайб голяшки с одной говяжьей ноги")]
        [Range(1, 6)] public int ShankPerLeg = 3;

        [Header("Телятина по-орловски")]
        [Tooltip("Говяжья корейка (толстый край) выпадает этим префабом — целым куском «Телячья корейка». Пусто — обычным куском. Ставит меню Орлов → 5")]
        public GameObject VealLoinPrefab;
        [Tooltip("Только ЛЕВЫЙ толстый край — корейкой; правый — как раньше (стейки рибай). Выкл — оба корейкой")]
        public bool VealLoinLeftOnly = true;

        private static MeatCutSettings _inst;
        private static bool _tried, _warned;

        public static MeatCutSettings Instance
        {
            get
            {
#if UNITY_EDITOR
                if (_inst == null) _tried = false;   // в редакторе ассет могли создать позже — искать заново
#endif
                if (_inst == null && !_tried)
                {
                    _tried = true;
                    _inst = Resources.Load<MeatCutSettings>("MeatCutSettings");
                    if (_inst == null && !_warned) { _warned = true; Debug.LogWarning("[Мясо] нет Resources/MeatCutSettings — CatCooks → Мясо → Новые туши → 1"); }
                }
                return _inst;
            }
        }

        /// <summary>Масштаб мяса (нет настроек — 4).</summary>
        public static float WorldScale => Instance != null ? Instance.Scale : 4f;

        public ProductDef Product(MeatSpecies sp, MeatPart p)
        {
            ProductDef[] a = sp == MeatSpecies.Beef ? BeefKinds : PorkKinds;
            int k = CarcassModel.Kind(p);
            return a != null && k < a.Length ? a[k] : null;
        }

        /// <summary>Продукт ломтя (стейк, медальон). Нет своего — тот же, что у части.</summary>
        public ProductDef SliceProduct(MeatSpecies sp, MeatPart p)
        {
            ProductDef[] a = sp == MeatSpecies.Beef ? BeefSlices : PorkSlices;
            int k = CarcassModel.Kind(p);
            ProductDef s = a != null && k < a.Length ? a[k] : null;
            return s != null ? s : Product(sp, p);
        }

        public Mesh BaseMesh(MeatSpecies sp, MeatPart p)
        {
            Mesh[] a = sp == MeatSpecies.Beef ? BeefMeshes : PorkMeshes;
            int i = (int)p;
            return a != null && i < a.Length ? a[i] : null;
        }
    }
}
