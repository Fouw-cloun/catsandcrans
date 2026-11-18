using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v23.5: СТЕНД СТИЛЯ — чтобы сразу видеть, как выглядят вещества, без готовки.
    ///  • Вёдра (настоящие тары сцены, их ставит меню CatCooks → Тест → Стенд стиля): на старте сервер
    ///    наливает в каждое своё вещество (молоко, сливки, шоколадная масса, белок, желтки, тесто…).
    ///  • Ряд «мастерков» (только картинка, без сети): плоское полотно и на нём горка вещества —
    ///    ровно так, как рисуется на настоящем мастерке (какао, сахар, мука, масло, паста…). Над каждым — подпись.
    /// </summary>
    public sealed class CCStyleStand : MonoBehaviour
    {
        [Tooltip("Названия твёрдых/сыпучих продуктов для «мастерков». Пусто — все, у кого есть профиль вещества")]
        public List<string> TrowelProducts = new List<string>();
        [Tooltip("Размер полотна «мастерка», м (как у настоящего)")]
        public Vector2 PlateSize = new Vector2(0.44f, 0.36f);
        [Tooltip("Порций на «мастерке»")]
        [Range(1, 12)] public int PlatePortions = 6;
        [Tooltip("Расстояние между «мастерками», м")]
        public float PlateStep = 0.75f;
        [Tooltip("Ряд «мастерков» — смещение вперёд/назад от ряда вёдер (локальная Z стенда), м")]
        public float PlateRowZ = -1.2f;
        [Tooltip("Шаг между вёдрами, м (для «Выстроить в ряд»)")]
        public float BucketStep = 1.5f;
        public bool Labels = true;

        [Header("Вёдра — только вид (v23.13)")]
        [Tooltip("Ведро-образец (модель копируется без скриптов и сети). Ставит меню стенда")]
        public GameObject BucketModel;
        [Tooltip("Жидкости по вёдрам. «А+Б» — второе поверх (желтки на белке)")]
        public List<string> Liquids = new List<string>
        {
            "Яичный белок+Яичный желток", "Яичный желток", "Яичный белок", "Яичная масса", "Шоколад", "Шоколадная масса",
            "Бисквитное тесто", "Молоко", "Сливки", "Смесь для мороженого", "Мороженое", "Меренга", "Меренга перебитая",
            "Вода", "Сметана", "Мёд", "Карамель",
        };
        [Range(0.15f, 0.95f)] public float Fill = 0.6f;

        private bool _built;

        /// <summary>v23.5b: вёдра стенда — дочерние объекты; выстроить их в ряд вдоль локальной X стенда.</summary>
        [ContextMenu("Выстроить вёдра в ряд")]
        public void LayoutBuckets()
        {
            int i = 0;
            foreach (CCStyleStandSlot sl in GetComponentsInChildren<CCStyleStandSlot>(true))
            {
                sl.transform.localPosition = new Vector3(i * BucketStep, 0f, 0f);
                sl.transform.localRotation = Quaternion.identity;
                i++;
            }
        }

        private void Start()
        {
            PlaceOnFloor();                                   // v23.6: вёдра и мастерки — на полу, рядом друг с другом
            Invoke(nameof(Build), 1.5f);                      // база продуктов успевает собраться
            Invoke(nameof(PlaceOnFloor), 0.5f);               // ещё раз, когда сеть расставила объекты
        }

        /// <summary>v23.6: стенд на пол под ним, вёдра — ряд вдоль стенда, каждое дном на пол.</summary>
        [ContextMenu("Поставить всё на пол")]
        public void PlaceOnFloor()
        {
            float y;
            // v23.12: только небольшая поправка — иначе луч находил «землю» под полом сцены и стенд уезжал вниз
            if (FloorY(transform.position, transform, out y) && Mathf.Abs(y - transform.position.y) < MaxSnap)
                transform.position = new Vector3(transform.position.x, y, transform.position.z);
            LayoutBuckets();
            foreach (CCStyleStandSlot sl in GetComponentsInChildren<CCStyleStandSlot>(true)) SnapToGround(sl.transform, transform);
        }

        /// <summary>v23.12: на сколько можно подвинуть к полу (м). Больше — значит, луч попал не в пол, не трогаем.</summary>
        public const float MaxSnap = 0.6f;

        /// <summary>Высота пола под точкой (свои объекты стенда и триггеры не считаются).</summary>
        public static bool FloorY(Vector3 at, Transform ignore, out float y)
        {
            y = at.y;
            RaycastHit[] hits = Physics.RaycastAll(at + Vector3.up * 4f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool ok = false;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null) continue;
                if (ignore != null && hits[i].collider.transform.IsChildOf(ignore)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; y = hits[i].point.y; ok = true; }
            }
            return ok;
        }

        /// <summary>Поставить объект дном (по видимой модели) на пол под ним.</summary>
        public static void SnapToGround(Transform t, Transform ignore)
        {
            if (t == null) return;
            bool any = false;
            Bounds b = new Bounds(t.position, Vector3.zero);
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || r.GetComponent<TextMesh>() != null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float y;
            if (!FloorY(new Vector3(b.center.x, Mathf.Max(b.max.y, t.position.y), b.center.z), ignore, out y)) return;
            float bottom = any ? b.min.y : t.position.y;
            float dy = y - bottom + 0.003f;
            if (Mathf.Abs(dy) > MaxSnap) return;                                  // v23.12: не уводим под пол
            t.position += Vector3.up * dy;
        }

        private void Build()
        {
            if (_built || Application.isBatchMode) return;
            _built = true;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) { Debug.LogWarning("[Стенд] нет базы продуктов", this); return; }
            var list = new List<ProductDef>();
            if (TrowelProducts.Count > 0)
            {
                foreach (string t in TrowelProducts)
                {
                    ProductDef d = CCStyleStandSlot.FindByTitle(db, t);
                    if (d != null) list.Add(d); else Debug.LogWarning($"[Стенд] нет продукта «{t}»", this);
                }
            }
            else
                for (int id = 1; id < 2048 && list.Count < 16; id++)
                {
                    ProductDef d = db.Get((ushort)id);
                    if (d == null) continue;
                    FoodSubstance s = FoodSubstance.Of(d, FoodState.None);
                    if (s != null && s.OwnLook && !FoodHeapLook.IsLiquid(d, FoodState.None)) list.Add(d);
                }
            Shader sh = Shader.Find("CatCooks/Toon Props");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            var steel = new Material(sh) { name = "StandSteel (auto)", hideFlags = HideFlags.DontSave };
            Color sc = new Color(0.72f, 0.74f, 0.78f);
            if (steel.HasProperty("_BaseColor")) steel.SetColor("_BaseColor", sc); else steel.color = sc;
            // v23.41: СНАЧАЛА вёдра, и каждый шаг отдельно — ошибка в одном мастерке больше не оставляет стенд без вёдер
            try { BuildBuckets(db, steel.shader); }
            catch (System.Exception e) { Debug.LogError("[Стенд] вёдра не построились: " + e, this); }
            // v23.40: мороженое шариком — всегда в ряду мастерков (его профиль создаётся кодом)
            ProductDef iceP = CCStyleStandSlot.FindByTitle(db, "Мороженое");
            if (iceP != null && !list.Contains(iceP)) list.Add(iceP);
            // v23.56: превью макарон — свои мастерки (не профилем вещества), из общего ряда убираем
            list.RemoveAll(x => x == null || x.Title == null || IsPreviewAlmond(x.Title.Trim().ToLowerInvariant()) || IsPreviewBatter(x.Title.Trim().ToLowerInvariant()));
            for (int i = 0; i < list.Count; i++)
            {
                try { Plate(list[i], i, steel); }
                catch (System.Exception e) { Debug.LogError($"[Стенд] мастерок «{list[i].Title}» не построился: {e}", this); }
            }
            try { PlateSponge(list.Count, steel); }                           // v23.40: кусок бисквита на мастерке
            catch (System.Exception e) { Debug.LogError("[Стенд] мастерок с бисквитом не построился: " + e, this); }
            try { PlatePreviewAlmond(db, list.Count + 1, steel); }            // v23.56: превью — миндальная смесь
            catch (System.Exception e) { Debug.LogError("[Стенд] превью «Миндальная смесь» не построилось: " + e, this); }
            try { PlatePreviewBatter(list.Count + 2, steel); }                 // v23.58: превью — тесто для макарон, мазками
            catch (System.Exception e) { Debug.LogError("[Стенд] превью «Тесто для макарон» не построилось: " + e, this); }
            Debug.Log($"[Стенд] «мастерков»: {list.Count + 3} (из них 2 превью макарон)", this);
        }

        // ───────────────────────── ВЁДРА ТОЛЬКО ДЛЯ ВИДА ─────────────────────────

        private readonly Dictionary<int, Mesh> _discs = new Dictionary<int, Mesh>();

        /// <summary>
        /// v23.13: ряд вёдер без сети и рецептов: копия модели ведра-образца (только сетки и материалы) и поверхность жидкости
        /// тем же шейдером CatCooks/Liquid Surface и теми же аниме-слоями, что в настоящей таре. Работает всегда.
        /// </summary>
        private void BuildBuckets(FoodDatabase db, Shader propShader)
        {
            if (BucketModel == null) BucketModel = FindAnyBucket();                 // v23.41: образец потерялся — берём любое ведро сцены
            if (BucketModel == null) { Debug.LogWarning("[Стенд] не задано ведро-образец (Bucket Model) и в сцене нет ведра — вёдер не будет. Пересоздай стенд меню, выделив ведро", this); return; }
            if (Liquids == null || Liquids.Count == 0)                               // v23.41: список в сцене пустой — берём стандартный
                Liquids = new List<string> { "Яичный белок+Яичный желток", "Яичный желток", "Яичный белок", "Яичная масса", "Шоколадная масса",
                                             "Бисквитное тесто", "Молоко", "Сливки", "Мороженое", "Меренга", "Вода", "Сметана", "Мёд", "Карамель" };
            EnsurePreviewLiquids();                                                 // v23.56: превью макарон — всегда в конце ряда
            Debug.Log($"[Стенд] вёдра: образец «{BucketModel.name}», жидкостей {Liquids.Count}", this);
            Shader liq = Shader.Find("CatCooks/Liquid Surface");
            if (liq == null) { Debug.LogWarning("[Стенд] нет шейдера CatCooks/Liquid Surface", this); return; }
            Transform mt = BucketModel.transform;
            Quaternion inv = Quaternion.Inverse(mt.rotation);
            // внутренность ведра
            Cookware cw = BucketModel.GetComponentInChildren<Cookware>(true);
            Collider iv = cw != null ? cw.InsideVolume : null;
            Bounds mb = new Bounds(mt.position, Vector3.zero); bool any = false;
            var rends = new List<MeshRenderer>();
            foreach (MeshRenderer mr in BucketModel.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr.enabled || mr.GetComponent<TextMesh>() != null || mr.GetComponent<CookwareLiquidVisual>() != null) continue;
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || (mr.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                Material m0 = mr.sharedMaterial;
                string shn = m0 != null && m0.shader != null ? m0.shader.name : "";
                if (shn.Contains("Liquid") || shn.Contains("Heap") || shn.Contains("Powder") || shn.Contains("Toon Food") || shn.Contains("Sugar")) continue;
                rends.Add(mr);
                if (!any) { mb = mr.bounds; any = true; } else mb.Encapsulate(mr.bounds);
            }
            Vector3 ic; float ir, bottom, top;
            if (iv != null) { Bounds ib = iv.bounds; ic = ib.center; ir = Mathf.Min(ib.extents.x, ib.extents.z) * 0.9f; bottom = ib.min.y; top = ib.max.y; }
            else { ic = mb.center; ir = Mathf.Max(mb.extents.x, mb.extents.z) * 0.78f; bottom = mb.min.y + mb.size.y * 0.06f; top = mb.max.y - mb.size.y * 0.05f; }
            float surfYW = Mathf.Lerp(bottom, top, Fill);
            float surfY = surfYW - mt.position.y;
            // v23.15: радиус жидкости — по самой модели ведра на высоте жидкости (внутренняя стенка), а не по коробке «внутренность»
            float wallR = InnerRadiusAt(rends, ic, surfYW, mb.min.y, mb.max.y);
            if (wallR > 0f) ir = wallR * 0.985f;
            else if (iv != null) ir = Mathf.Min(ir * 1.3f, Mathf.Min(mb.extents.x, mb.extents.z) * 0.92f);
            Vector3 cOff = inv * (ic - mt.position);

            for (int i = 0; i < Liquids.Count; i++)
            {
                string entry = Liquids[i];
                if (string.IsNullOrEmpty(entry)) continue;
                string[] parts = entry.Split('+');
                string title = parts[0].Trim();
                Transform root = new GameObject("Bucket_" + title).transform;
                root.SetParent(transform, false);
                root.localPosition = new Vector3(i * BucketStep, 0f, 0f);
                root.localRotation = Quaternion.identity;
                foreach (MeshRenderer mr in rends)                                     // модель ведра
                {
                    var g = new GameObject(mr.name);
                    g.transform.SetParent(root, false);
                    g.transform.localPosition = inv * (mr.transform.position - mt.position);
                    g.transform.localRotation = inv * mr.transform.rotation;
                    g.transform.localScale = mr.transform.lossyScale;
                    g.AddComponent<MeshFilter>().sharedMesh = mr.GetComponent<MeshFilter>().sharedMesh;
                    MeshRenderer nr = g.AddComponent<MeshRenderer>();
                    nr.sharedMaterials = mr.sharedMaterials;
                }
                // жидкость
                ProductDef d = CCStyleStandSlot.FindByTitle(db, title);
                string t = title.ToLowerInvariant();
                int kind = KindOf(t);
                bool yolkOnly = t.Contains("яичный желток") && parts.Length == 1;
                bool eggClear = t.Contains("яичный белок") || yolkOnly;                  // v23.20: сырой белок прозрачный
                bool water = kind == 6;
                bool macBatter = IsPreviewBatter(t);                                     // v23.56: превью — тесто для макарон
                if (macBatter) kind = 0;                                                 // v23.58: гладкое глянцевое, без пузырей-пор шейдера
                if (IsPreviewAlmond(t))                                                  // v23.56: превью — миндальная смесь (сыпучая горка, не жидкость)
                {
                    var ad = new FoodSubstanceMesh.Dims();
                    ad.Rx = ad.Rz = ir * 0.97f; ad.H = ir * 0.32f; ad.Edge = 0.55f; ad.RectN = 2f;
                    AlmondHeap(root, new Vector3(cOff.x, surfY - ad.H * ad.Edge, cOff.z), db, ad, i * 53 + 7, 160);
                    if (Labels) BucketLabel(root, entry + " (превью)", new Vector3(cOff.x, (any ? mb.max.y - mt.position.y : surfY) + ir * 0.6f, cOff.z), ir, true);
                    continue;
                }
                Color col = d != null ? FoodColors.Of(d, FoodState.None) : new Color(0.95f, 0.93f, 0.88f);
                if (macBatter) col = BatterCol;
                if (eggClear) col = new Color(0.96f, 0.95f, 0.8f);
                if (t.Contains("бисквит")) col = Color.Lerp(col, new Color(0.6f, 0.4f, 0.26f), 0.85f);
                if (t.Contains("шоколадная масса")) col = Color.Lerp(col, new Color(0.5f, 0.31f, 0.19f), 0.8f);   // v23.43: молочный шоколад   // v23.40: как в таре
                if (kind == 11) col = CookwareLiquidVisual.GanacheColor;                 // v23.64: ганаш — сливочная карамель, как в таре
                else if (kind == 10) col = CookwareLiquidVisual.BurntCaramelColor;       // v23.64: горелая — тёмная
                else if (t.Contains("мёд") || t.Contains("мед") || t.Contains("карамел")) col = Color.Lerp(col, new Color(1f, 0.85f, 0.45f), 0.25f);
                if (kind == 5) col = Color.Lerp(col, new Color(0.99f, 0.97f, 0.92f), 0.45f);       // мороженое — белое (крапинки ванили рисует шейдер)
                col.a = 1f;
                var lg = new GameObject("Liquid");
                lg.transform.SetParent(root, false);
                float sy0 = yolkOnly ? Mathf.Lerp(bottom, top, Fill * 0.4f) - mt.position.y : surfY;      // под кучей желтков — тонкая плёнка белка
                lg.transform.localPosition = new Vector3(cOff.x, sy0, cOff.z);
                bool thick = kind == 1 || kind == 2;
                Mesh dm;
                if (macBatter) { if (_batterDisc == null) _batterDisc = BatterDisc(ir); dm = _batterDisc; }           // v23.56: пышные складки
                else if (!_discs.TryGetValue(kind, out dm)) { dm = Disc(ir, thick ? 1f : 0f, kind == 5 ? 1f : 0f, kind); _discs[kind] = dm; }   // v23.14: с рельефом вещества
                lg.AddComponent<MeshFilter>().sharedMesh = dm;
                MeshRenderer lr = lg.AddComponent<MeshRenderer>();
                var lm = new Material(liq) { name = "StandLiquid " + title, hideFlags = HideFlags.DontSave };
                if (water || eggClear) { lm.SetFloat("_SrcBlend", 5f); lm.SetFloat("_DstBlend", 10f); lm.SetFloat("_ZWrite", 0f); lm.renderQueue = 2990; lm.SetFloat("_Opacity", eggClear ? 0.42f : 0.6f); }
                else { lm.SetFloat("_SrcBlend", 1f); lm.SetFloat("_DstBlend", 0f); lm.SetFloat("_ZWrite", 1f); lm.renderQueue = 2450; lm.SetFloat("_Opacity", 1f); }
                lm.SetColor("_BaseColor", col);
                lm.SetFloat("_Density", water || eggClear ? 0f : 1f);
                lm.SetVector("_AnimeCenter", lg.transform.position);
                lm.SetFloat("_AnimeRadius", ir);                                         // v23.24: узор капель по размеру, как в таре
                lm.SetFloat("_AnimeWallShadow", 0.5f);
                lm.SetFloat("_AnimeLayers", 1f);
                lm.SetFloat("_AnimeKind", kind);
                lm.SetFloat("_AnimeInk", 0.75f);
                lm.SetFloat("_AnimeDepth", 0.18f);
                lm.SetFloat("_AnimeFlecks", 0f);
                lm.SetFloat("_AnimeCaustic", water ? 1f : 0f);
                bool syrup = (t.Contains("мёд") || t.Contains("мед") || t.Contains("карамел") || t.Contains("сироп")) && kind != 10 && kind != 11;
                lm.SetFloat("_AnimeGlow", syrup ? 0.4f : 0f);
                lm.SetFloat("_AnimeBubbles", 0f);
                lm.SetFloat("_AnimeShine", kind == 4 || kind == 7 || kind == 9 ? 0.85f : kind == 0 ? 0.4f : (kind == 3 || kind == 5) ? 0f : 0.45f);
                lm.SetFloat("_AnimePores", 0f);
                // v23.15: блеска меньше, без «искр»: матовым — почти нет, глянцевым — умеренно
                if (kind == 3 || kind == 5) { lm.SetFloat("_GlintStrength", 0.2f); lm.SetFloat("_SheenStrength", 0.04f); lm.SetFloat("_FresnelStrength", 0.08f); lm.SetFloat("_AnimeRim", 0.25f); }
                else if (kind == 4 || kind == 7 || kind == 9) { lm.SetFloat("_GlintStrength", 0.9f); lm.SetFloat("_GlintSize", 0.982f); lm.SetFloat("_SheenStrength", 0.3f); }
                else { lm.SetFloat("_GlintStrength", 0.5f); lm.SetFloat("_SheenStrength", 0.12f); }
                lm.SetFloat("_AnimeBreath", CookwareLiquidVisual.BreathFor(kind) * ir);
                if (kind == 9 && !t.Contains("солён") && !t.Contains("солен"))
                {
                    // v23.63: карамель — чуть прозрачная (как в игре), сквозь неё видно дно ведра
                    lm.SetFloat("_SrcBlend", 5f); lm.SetFloat("_DstBlend", 10f); lm.SetFloat("_ZWrite", 0f); lm.renderQueue = 2990;
                    lm.SetOverrideTag("RenderType", "Transparent");
                    lm.SetFloat("_Opacity", 0.78f);
                    lm.SetFloat("_Density", 0f);
                }
                if (macBatter)
                {
                    // v23.58: мазки лопаткой, глянец как у взбитого теста, без пор; микрокрупинки — тем же материалом (тот же цвет)
                    lm.SetFloat("_AnimePores", 0f);
                    lm.SetFloat("_AnimeShine", 0.45f);
                    lm.SetFloat("_AnimeInk", 0.5f);
                    lm.SetFloat("_GlintStrength", 0.6f); lm.SetFloat("_GlintSize", 0.97f);
                    lm.SetFloat("_SheenStrength", 0.18f); lm.SetFloat("_FresnelStrength", 0.12f);
                    var dg = new GameObject("Dots (preview)") { hideFlags = HideFlags.DontSave };
                    dg.transform.SetParent(lg.transform, false);
                    dg.AddComponent<MeshFilter>().sharedMesh = BatterDots(ir, 500, i * 31 + 3);
                    MeshRenderer dr = dg.AddComponent<MeshRenderer>();
                    dr.sharedMaterial = lm;
                    dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                lr.sharedMaterial = lm;
                _standLiquids.Add(lg.transform); _standMats.Add(lm);
                if (parts.Length > 1 && parts[1].ToLowerInvariant().Contains("желток")) Yolks(lg.transform, ir, propShader, 2);
                if (yolkOnly) Yolks(lg.transform, ir, propShader, 13);                  // v23.20: куча желтков друг на друге
                if (kind == 8)                                                           // v23.22: настоящие пузырьки пены
                {
                    CCFoamField ff = lg.gameObject.AddComponent<CCFoamField>();
                    ff.Center = lg.transform.position; ff.SurfaceY = lg.transform.position.y; ff.Radius = ir * 0.9f;
                    ff.Color = Color.Lerp(col, Color.white, 0.4f); ff.Count = 36; ff.Active = true;
                }
                if (Labels)
                {
                    var tg = new GameObject("Label");
                    tg.transform.SetParent(root, false);
                    tg.transform.localPosition = new Vector3(cOff.x, (any ? mb.max.y - mt.position.y : surfY) + ir * 0.6f, cOff.z);
                    TextMesh tm = tg.AddComponent<TextMesh>();
                    tm.text = entry.Replace("+", " + ") + (macBatter ? " (превью)" : "");
                    tm.anchor = TextAnchor.MiddleCenter;
                    tm.characterSize = ir * 0.05f;
                    tm.fontSize = 48;
                    tm.color = d != null || water ? Color.white : new Color(1f, 0.6f, 0.5f);
                    CCStyleStandBillboard.SetFont(tm);
                    tg.AddComponent<CCStyleStandBillboard>();
                }
                if (d == null) Debug.Log($"[Стенд] «{title}» нет в базе — цвет по умолчанию", this);
            }
            Debug.Log($"[Стенд] вёдер (только вид): {Liquids.Count}", this);
        }

        private readonly List<Transform> _standLiquids = new List<Transform>();
        private readonly List<Material> _standMats = new List<Material>();

        private void LateUpdate()
        {
            // стенд передвинули — центр поверхности для слоёв шейдера тоже
            for (int i = 0; i < _standLiquids.Count; i++)
                if (_standLiquids[i] != null && _standLiquids[i].hasChanged) { _standMats[i].SetVector("_AnimeCenter", _standLiquids[i].position); _standLiquids[i].hasChanged = false; }
        }

        /// <summary>
        /// v23.15: радиус внутренней стенки ведра на высоте yW. Стенка ведра прямая (конус), поэтому берём радиус
        /// у верха и у дна (нижние 15% расстояний от оси — внутренняя стенка ближе наружной и ручки) и смешиваем по высоте.
        /// 0 — не удалось (сетка не читается).
        /// </summary>
        private static float InnerRadiusAt(List<MeshRenderer> rends, Vector3 axisW, float yW, float yMin, float yMax)
        {
            var top = new List<float>(256); var bot = new List<float>(256);
            float maxD = 0f, H = Mathf.Max(1e-4f, yMax - yMin);
            foreach (MeshRenderer mr in rends)
            {
                Mesh m = mr.GetComponent<MeshFilter>().sharedMesh;
                if (m == null || !m.isReadable) continue;
                Vector3[] vs = m.vertices;
                Matrix4x4 l2w = mr.transform.localToWorldMatrix;
                for (int i = 0; i < vs.Length; i++)
                {
                    Vector3 w = l2w.MultiplyPoint3x4(vs[i]);
                    float dx = w.x - axisW.x, dz = w.z - axisW.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > maxD) maxD = d;
                    float f = (w.y - yMin) / H;
                    if (f > 0.75f) top.Add(d); else if (f < 0.25f) bot.Add(d);
                }
            }
            float rt = Pct(top, maxD), rb = Pct(bot, maxD);
            if (rt <= 0f && rb <= 0f) return 0f;
            if (rt <= 0f) rt = rb; if (rb <= 0f) rb = rt;
            return Mathf.Lerp(rb, rt, Mathf.Clamp01((yW - yMin) / H));
        }

        private static float Pct(List<float> ds, float maxD)
        {
            ds.RemoveAll(x => x < maxD * 0.3f);                                         // центр дна, заклёпки
            if (ds.Count < 4) return 0f;
            ds.Sort();
            return ds[Mathf.Clamp(Mathf.RoundToInt(ds.Count * 0.15f), 0, ds.Count - 1)];
        }

        private static int KindOf(string t)
        {
            if (t.Contains("вода")) return 6;
            if (t.Contains("морож") && !t.Contains("смесь")) return 5;
            if (t.Contains("меренг")) return 2;
            if (t.Contains("яичная масса")) return 8;                                         // v23.20: своя (разводы + пена)
            if (t.Contains("тесто") || t.Contains("смесь для морож")) return 3;
            if (t.Contains("ганаш")) return 11;                                                                          // v23.64: ганаш — сложенные ленты (до «карамел»: «Карамельный ганаш»)
            if (t.Contains("карамел") && (t.Contains("горел") || t.Contains("пригор"))) return 10;                       // v23.64: горелая — чёрные куски
            if (t.Contains("карамел")) return 9;                                                                        // v23.64: карамель — кучки и пузырьки
            if (t.Contains("мёд") || t.Contains("мед") || t.Contains("сироп")) return 7;     // v23.16: свои ленты
            if (t.Contains("шоколад") || t.Contains("ганаш") || t.Contains("глазур")) return 4;
            if (t.Contains("сметан") || t.Contains("крем")) return 1;
            return 0;
        }

        /// <summary>Круг поверхности: uv0.x — доля до стенки, uv1 — метры, uv2.y — «сметана», uv3.x — «мороженое».</summary>
        private static Mesh Disc(float r, float thick, float ice, int kind)
        {
            const int RG = 40, SG = 96;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>();
            var u0 = new List<Vector2>(); var u1 = new List<Vector2>(); var u2 = new List<Vector2>(); var u3 = new List<Vector2>();
            var tr = new List<int>();
            v.Add(Vector3.zero); n.Add(Vector3.up); c.Add(Color.white); u0.Add(Vector2.zero); u1.Add(Vector2.zero); u2.Add(new Vector2(0f, thick)); u3.Add(new Vector2(ice, 0f));
            for (int i = 1; i <= RG; i++)
                for (int j = 0; j < SG; j++)
                {
                    float f = i / (float)RG, a = j / (float)SG * Mathf.PI * 2f;
                    Vector3 p = new Vector3(Mathf.Cos(a) * r * f, 0f, Mathf.Sin(a) * r * f);
                    p.y = CookwareLiquidVisual.KindRelief(kind, p.x, p.z, f, r);
                    v.Add(p); n.Add(Vector3.up); c.Add(Color.white);
                    u0.Add(new Vector2(f, 0f)); u1.Add(new Vector2(p.x, p.z)); u2.Add(new Vector2(0f, thick)); u3.Add(new Vector2(ice, 0f));
                }
            for (int j = 0; j < SG; j++) { tr.Add(0); tr.Add(1 + (j + 1) % SG); tr.Add(1 + j); }
            for (int i = 1; i < RG; i++)
                for (int j = 0; j < SG; j++)
                {
                    int a = 1 + (i - 1) * SG + j, b = 1 + (i - 1) * SG + (j + 1) % SG, a2 = a + SG, b2 = b + SG;
                    tr.Add(a); tr.Add(b); tr.Add(a2);
                    tr.Add(b); tr.Add(b2); tr.Add(a2);
                }
            var m = new Mesh { name = "StandLiquidDisc", hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c);
            m.SetUVs(0, u0); m.SetUVs(1, u1); m.SetUVs(2, u2); m.SetUVs(3, u3);
            m.SetTriangles(tr, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private Material _yolkMat;

        private void Yolks(Transform liquid, float ir, Shader sh, int count)
        {
            // v23.16: тот же желток, что в настоящей таре (аниме-шейдер с обводкой)
            Material ym = CookwareLiquidVisual.YolkMaterial;
            if (ym == null && sh != null)
            {
                if (_yolkMat == null)
                {
                    _yolkMat = new Material(sh) { name = "StandYolk (auto)", hideFlags = HideFlags.DontSave };
                    Color c = new Color(1f, 0.62f, 0.1f);
                    if (_yolkMat.HasProperty("_BaseColor")) _yolkMat.SetColor("_BaseColor", c); else _yolkMat.color = c;
                }
                ym = _yolkMat;
            }
            if (ym == null) return;
            for (int k = 0; k < count; k++)
            {
                GameObject y = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                y.name = "Yolk";
                Object.Destroy(y.GetComponent<Collider>());
                y.transform.SetParent(liquid, false);
                float s0 = ir * (count > 2 ? 0.6f : 0.85f);                          // v23.22: желтки крупнее (яйца большие)
                y.transform.localScale = new Vector3(s0, s0 * (count > 2 ? 0.6f : 0.45f), s0);
                if (count <= 2)
                    y.transform.localPosition = new Vector3((k == 0 ? -0.42f : 0.38f) * ir, s0 * 0.08f, (k == 0 ? 0.15f : -0.25f) * ir);
                else
                {
                    // куча: 7 по кругу внизу, 5 выше, 1 сверху — каждый слой лежит на предыдущем, чуть криво
                    int li = k < 7 ? 0 : k < 12 ? 1 : 2;
                    int st = li == 0 ? 0 : li == 1 ? 7 : 12;
                    int cnt = li == 0 ? 7 : li == 1 ? 5 : 1;
                    float rr = li == 0 ? ir * 0.62f : li == 1 ? ir * 0.34f : 0f;
                    float a = (k - st) / (float)cnt * 6.2832f + li * 0.6f;
                    float jx = Mathf.Sin(k * 12.9898f) * 0.05f * ir, jz = Mathf.Cos(k * 78.233f) * 0.05f * ir;
                    y.transform.localPosition = new Vector3(Mathf.Cos(a) * rr + jx, s0 * 0.2f + li * s0 * 0.42f, Mathf.Sin(a) * rr + jz);
                    y.transform.localRotation = Quaternion.Euler(Mathf.Sin(k * 3.1f) * 12f, k * 37f, Mathf.Cos(k * 2.3f) * 12f);
                }
                MeshFilter mf = y.GetComponent<MeshFilter>();
                Mesh yme = CookwareLiquidVisual.YolkMesh(mf.sharedMesh);
                if (yme != null && ym != _yolkMat) mf.sharedMesh = yme;
                y.GetComponent<MeshRenderer>().sharedMaterial = ym;
            }
        }

        private Material _nutMat, _nutCut;

        /// <summary>v23.11: два фундука у мастерка с пастой — по ним глаз сразу читает «ореховая паста».</summary>
        private void Nuts(Transform root, Shader sh)
        {
            if (sh == null) return;
            if (_nutMat == null)
            {
                _nutMat = new Material(sh) { name = "StandNut (auto)", hideFlags = HideFlags.DontSave };
                Color c = new Color(0.55f, 0.32f, 0.16f);
                if (_nutMat.HasProperty("_BaseColor")) _nutMat.SetColor("_BaseColor", c); else _nutMat.color = c;
                _nutCut = new Material(sh) { name = "StandNutCut (auto)", hideFlags = HideFlags.DontSave };
                Color c2 = new Color(0.93f, 0.84f, 0.66f);
                if (_nutCut.HasProperty("_BaseColor")) _nutCut.SetColor("_BaseColor", c2); else _nutCut.color = c2;
            }
            float s0 = PlateSize.x * 0.11f;
            for (int k = 0; k < 2; k++)
            {
                GameObject nut = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                nut.name = "Hazelnut";
                Object.Destroy(nut.GetComponent<Collider>());
                nut.transform.SetParent(root, false);
                nut.transform.localScale = new Vector3(s0, s0 * 0.9f, s0 * 1.1f);
                nut.transform.localPosition = new Vector3(PlateSize.x * (0.62f + 0.16f * k), s0 * 0.45f, PlateSize.y * (0.2f - 0.3f * k));
                nut.transform.localRotation = Quaternion.Euler(0f, 35f * k, 12f);
                nut.GetComponent<MeshRenderer>().sharedMaterial = k == 0 ? _nutMat : _nutCut;     // второй — светлая половинка
            }
        }

        /// <summary>v23.40: «мастерок» с куском бисквита — тот же кубик, что падает из пласта под пилой (корочка с двух сторон, купол сверху).</summary>
        private void PlateSponge(int i, Material steel)
        {
            Transform root = new GameObject("Trowel_Бисквит").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(i * PlateStep, 0f, PlateRowZ);
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "Blade";
            p.transform.SetParent(root, false);
            p.transform.localScale = new Vector3(PlateSize.x, 0.012f, PlateSize.y);
            p.transform.localPosition = new Vector3(0f, 0.006f, 0f);
            p.GetComponent<MeshRenderer>().sharedMaterial = steel;
            Object.Destroy(p.GetComponent<Collider>());
            GameObject hd = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hd.name = "Handle";
            hd.transform.SetParent(root, false);
            hd.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hd.transform.localScale = new Vector3(PlateSize.x * 0.12f, PlateSize.y * 0.45f, PlateSize.x * 0.12f);
            hd.transform.localPosition = new Vector3(0f, PlateSize.x * 0.08f, -PlateSize.y * 0.5f - PlateSize.y * 0.4f);
            hd.GetComponent<MeshRenderer>().sharedMaterial = steel;
            Object.Destroy(hd.GetComponent<Collider>());

            // v23.44: ОДИН КУБИК бисквита (один сегмент): корочка сверху и снизу, мякиш с порами посередине
            float side = Mathf.Min(PlateSize.x, PlateSize.y) * 0.55f;
            Vector3 cell = new Vector3(side * 1.25f, side * 0.55f, side * 1.2f);   // v23.45: тоньше, шире и длиннее — как кусок
            var go = new GameObject("SpongeSlab") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 0.012f + cell.y * 0.5f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 18f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = SpongeMesh.BuildPiece(null, cell, SpongeMesh.AllCrust);
            go.AddComponent<MeshRenderer>().sharedMaterial = SpongeMaterial();
            go.AddComponent<SpongeJiggle>().Strength = 0.014f;
            if (Labels)
            {
                var lg = new GameObject("Label");
                lg.transform.SetParent(root, false);
                lg.transform.localPosition = new Vector3(0f, PlateSize.x * (0.8f + 0.25f * (i % 2)), 0f);
                TextMesh tm = lg.AddComponent<TextMesh>();
                tm.text = "Бисквит";
                tm.anchor = TextAnchor.MiddleCenter;
                tm.characterSize = PlateSize.x * 0.035f;
                tm.fontSize = 48;
                tm.color = Color.white;
                CCStyleStandBillboard.SetFont(tm);
                lg.AddComponent<CCStyleStandBillboard>();
            }
        }

        /// <summary>Материал бисквита: как у пласта в сцене (Mat_SpongeChoc), а нет — «CatCooks/Food Heap» (цвет из вершин).</summary>
        private static Material SpongeMaterial()
        {
            foreach (FoodGridSlab sl in Object.FindObjectsByType<FoodGridSlab>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                MeshRenderer r = sl.GetComponent<MeshRenderer>();
                if (IsSpongeShader(r)) return r.sharedMaterial;
            }
            foreach (SpongePiece sp in Object.FindObjectsByType<SpongePiece>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                MeshRenderer r = sp.GetComponent<MeshRenderer>();
                if (IsSpongeShader(r)) return r.sharedMaterial;
            }
            // v23.43: только шейдер с цветом в вершинах и порами — иначе бисквит рисуется плоским цветом без пор
            Shader sh = Shader.Find("CatCooks/Food Heap");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            var m = new Material(sh) { name = "StandSponge (auto)", hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0.003f);
            if (m.HasProperty("_Flat")) m.SetFloat("_Flat", 0.4f);
            if (m.HasProperty("_GlintStrength")) m.SetFloat("_GlintStrength", 0.06f);
            return m;
        }

        private static bool IsSpongeShader(Renderer r)
            => r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == "CatCooks/Food Heap";

        /// <summary>v23.41: любое ведро сцены (тара с жидкостью, в названии «ведр»/«bucket»/«vedro»), не из стенда.</summary>
        private GameObject FindAnyBucket()
        {
            GameObject best = null;
            foreach (CookwareLiquidVisual lv in Object.FindObjectsByType<CookwareLiquidVisual>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (lv == null || lv.transform.IsChildOf(transform)) continue;
                Cookware cw = lv.GetComponentInParent<Cookware>();
                if (cw == null) continue;
                string n = (cw.name + " " + (cw.Def != null ? cw.Def.Title : "")).ToLowerInvariant();
                if (n.Contains("ведр") || n.Contains("bucket") || n.Contains("vedro")) return cw.gameObject;
                if (best == null) best = cw.gameObject;
            }
            return best;
        }

        // ───────────────────────── v23.56: ПРЕВЬЮ МАКАРОН (только стенд) ─────────────────────────
        // «Миндальная смесь» — мука миндальная + сахар: бежевая рыхлая горка с белыми крупинками сахара и редкими искрами.
        // «Тесто для макарон» — шоколадное, воздушное, пористое: в ведре — пышные складки с порами, на мастерке — густая
        // вытянутый мазок (v23.59; v23.58: мазки лопаткой, лёгкий рельеф, микрокрупинки того же цвета, без пор). В игру пока НЕ перенесено.

        public const string PreviewAlmond = "Миндальная смесь";
        public const string PreviewBatter = "Тесто для макарон";
        private static readonly Color BatterCol = new Color(0.62f, 0.45f, 0.31f, 1f);   // v23.57: светлое, бежево-шоколадное

        private static bool IsPreviewAlmond(string lowerTitle) { return lowerTitle == "миндальная смесь"; }
        private static bool IsPreviewBatter(string lowerTitle) { return lowerTitle == "тесто для макарон"; }

        private void EnsurePreviewLiquids()
        {
            if (Liquids == null) return;
            bool a = false, b = false;
            foreach (string s in Liquids)
            {
                if (string.IsNullOrEmpty(s)) continue;
                string l = s.Split('+')[0].Trim().ToLowerInvariant();
                if (IsPreviewAlmond(l)) a = true;
                if (IsPreviewBatter(l)) b = true;
            }
            if (!a) Liquids.Add(PreviewAlmond);
            if (!b) Liquids.Add(PreviewBatter);
        }


        private static Material _previewBatterMat;

        private static Material PreviewBatterMat()
        {
            if (_previewBatterMat != null) return _previewBatterMat;
            Shader sh = Shader.Find("CatCooks/Food Heap");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            _previewBatterMat = new Material(sh) { name = "PreviewMacaronBatter (auto)", hideFlags = HideFlags.DontSave };
            if (_previewBatterMat.HasProperty("_BaseColor")) _previewBatterMat.SetColor("_BaseColor", Color.white);
            if (_previewBatterMat.HasProperty("_TexDetail")) _previewBatterMat.SetFloat("_TexDetail", 0f);
            if (_previewBatterMat.HasProperty("_GlintStrength")) _previewBatterMat.SetFloat("_GlintStrength", 0.75f);   // тесто блестит мягко
            if (_previewBatterMat.HasProperty("_GlintSize")) _previewBatterMat.SetFloat("_GlintSize", 0.955f);
            if (_previewBatterMat.HasProperty("_Flat")) _previewBatterMat.SetFloat("_Flat", 0.35f);
            if (_previewBatterMat.HasProperty("_OutlineWidth")) _previewBatterMat.SetFloat("_OutlineWidth", 0.003f);
            return _previewBatterMat;
        }


        // ── Миндальная смесь ──

        /// <summary>
        /// v23.62: горка миндальной смеси — ТЕМ ЖЕ профилем, что в игре (FoodSubstanceTable.AlmondMixProfile):
        /// порошок миндальной муки + крошечные блестящие кубики сахара. На мастерке — горкой, в ведре — по стенкам.
        /// </summary>
        private void AlmondHeap(Transform parent, Vector3 localPos, FoodDatabase db, FoodSubstanceMesh.Dims dims, int seed, int sparks)
        {
            ProductDef mixP = CCStyleStandSlot.FindByTitle(db, PreviewAlmond);
            FoodSubstance sub = mixP != null ? FoodSubstance.Of(mixP, FoodState.None) : null;
            if (sub == null || sub.Look != SubstanceLook.Powder) sub = FoodSubstanceTable.AlmondMixProfile(mixP);
            Color col = sub.ColorFor(mixP != null ? FoodColors.Of(mixP, FoodState.None) : sub.Color);
            var go = new GameObject("AlmondMix (preview)") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var mesh = new Mesh { name = "Preview AlmondMix", hideFlags = HideFlags.DontSave };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            FoodSubstanceMesh.Powder(mesh, dims, sub.Wonky, col, seed, Mathf.Max(0.5f, sub.Sugar));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            Material m = sub.GetMaterial();
            if (m != null) mr.sharedMaterial = m;
        }



        // ── Тесто для макарон (v23.58): мазки лопаткой, лёгкий рельеф, микрокрупинки того же цвета ──

        private Mesh _batterDisc;

        /// <summary>
        /// Рельеф мазков: тесто размазано лопаткой по спирали — каждый мазок плавно поднимается и кончается мягким
        /// бортиком-«губой», в центре — поднятый лопаткой хохолок. Рельеф лёгкий. rel — сила рельефа, peakK — высота хохолка.
        /// </summary>
        private static float SmearRelief(float x, float z, float R, float dome, float rel, float peakK, float seedK)
        {
            float rho = Mathf.Sqrt(x * x + z * z) / Mathf.Max(1e-5f, R);
            float a = Mathf.Atan2(z, x);
            float body = Mathf.Pow(Mathf.Max(0f, 1f - rho * rho), 0.65f) * dome;
            // спираль мазков с неровным шагом
            float ph = (a + rho * 7.2f + Mathf.Sin(a * 2f + seedK) * 0.55f + Mathf.Sin(rho * 5f + seedK * 1.7f) * 0.35f) / (Mathf.PI * 2f) * 3f;
            float saw = ph - Mathf.Floor(ph);
            float stroke = Mathf.Pow(saw, 1.4f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((saw - 0.82f) / 0.18f)));
            float band = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rho - 0.12f) / 0.2f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rho - 0.82f) / 0.18f)));
            float strokeH = stroke * rel * (0.7f + 0.3f * Mathf.Sin(a * 3f + seedK * 2.3f)) * band;
            // мягкий хохолок в центре (чуть наклонён), как от поднятой лопатки
            float px = x - R * 0.06f, pz = z + R * 0.04f;
            float pr = Mathf.Sqrt(px * px + pz * pz) / (R * 0.22f);
            float peak = Mathf.Exp(-pr * pr * 1.6f) * peakK;
            float puff = (Mathf.PerlinNoise(x / (R * 0.4f) + 7.3f + seedK, z / (R * 0.4f) + 2.1f) - 0.5f) * rel * 0.35f * band;
            return body + strokeH + peak + puff;
        }

        private static float BatterRelief(float x, float z, float R)
        {
            return SmearRelief(x, z, R, R * 0.07f, R * 0.045f, R * 0.03f, 1.3f);       // v23.59: хохолок ниже
        }

        private static Mesh BatterDisc(float r)
        {
            const int RG = 72, SG = 200;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>();
            var u0 = new List<Vector2>(); var u1 = new List<Vector2>(); var u2 = new List<Vector2>(); var u3 = new List<Vector2>();
            var tr = new List<int>();
            v.Add(new Vector3(0f, BatterRelief(0f, 0f, r), 0f)); n.Add(Vector3.up); c.Add(Color.white);
            u0.Add(Vector2.zero); u1.Add(Vector2.zero); u2.Add(new Vector2(0f, 1f)); u3.Add(Vector2.zero);
            for (int i = 1; i <= RG; i++)
                for (int j = 0; j < SG; j++)
                {
                    float f = i / (float)RG, a = j / (float)SG * Mathf.PI * 2f;
                    Vector3 p = new Vector3(Mathf.Cos(a) * r * f, 0f, Mathf.Sin(a) * r * f);
                    p.y = BatterRelief(p.x, p.z, r);
                    v.Add(p); n.Add(Vector3.up); c.Add(Color.white);
                    u0.Add(new Vector2(f, 0f)); u1.Add(new Vector2(p.x, p.z)); u2.Add(new Vector2(0f, 1f)); u3.Add(Vector2.zero);
                }
            for (int j = 0; j < SG; j++) { tr.Add(0); tr.Add(1 + (j + 1) % SG); tr.Add(1 + j); }
            for (int i = 1; i < RG; i++)
                for (int j = 0; j < SG; j++)
                {
                    int a = 1 + (i - 1) * SG + j, b = 1 + (i - 1) * SG + (j + 1) % SG, a2 = a + SG, b2 = b + SG;
                    tr.Add(a); tr.Add(b); tr.Add(a2);
                    tr.Add(b); tr.Add(b2); tr.Add(a2);
                }
            var m = new Mesh { name = "StandBatterDisc (preview)", hideFlags = HideFlags.DontSave };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c);
            m.SetUVs(0, u0); m.SetUVs(1, u1); m.SetUVs(2, u2); m.SetUVs(3, u3);
            m.SetTriangles(tr, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Крупинка теста: приплюснутый бугорок с тёмным ободком-контуром у основания (как обводка у остальной еды).
        /// col — цвет теста, ink — цвет контура.
        /// </summary>
        private static void AddDot(List<Vector3> v, List<Color> c, List<Vector2> u0, List<Vector2> u1, List<int> t,
                                   Vector3 at, float size, Color col, Color ink, float rhoUv, float rot)
        {
            int b = v.Count;
            v.Add(at + Vector3.up * (size * 0.5f)); c.Add(col);
            if (u0 != null) { u0.Add(new Vector2(rhoUv, 0f)); u1.Add(new Vector2(at.x, at.z)); }
            for (int ring = 0; ring < 2; ring++)
                for (int k = 0; k < 7; k++)
                {
                    float a = rot + k / 7f * Mathf.PI * 2f;
                    float wob = 0.85f + 0.3f * ((k * 37) % 5) / 4f;
                    float rr = size * wob * (ring == 0 ? 0.72f : 1f);
                    float yy = ring == 0 ? size * 0.3f : -size * 0.08f;
                    Vector3 p = at + new Vector3(Mathf.Cos(a) * rr, yy, Mathf.Sin(a) * rr);
                    v.Add(p); c.Add(ring == 0 ? col : ink);
                    if (u0 != null) { u0.Add(new Vector2(rhoUv, 0f)); u1.Add(new Vector2(p.x, p.z)); }
                }
            for (int k = 0; k < 7; k++)
            {
                int i0 = b + 1 + k, i1 = b + 1 + (k + 1) % 7, o0 = i0 + 7, o1 = i1 + 7;
                t.Add(b); t.Add(i1); t.Add(i0);
                t.Add(i0); t.Add(i1); t.Add(o0);
                t.Add(i1); t.Add(o1); t.Add(o0);
            }
        }

        /// <summary>Микрокрупинки по тесту в ведре: тот же материал (цвет теста), с тёмным контуром у основания.</summary>
        private static Mesh BatterDots(float r, int count, int seed)
        {
            var rnd = new System.Random(seed);
            var v = new List<Vector3>(); var c = new List<Color>(); var u0 = new List<Vector2>(); var u1 = new List<Vector2>(); var t = new List<int>();
            float sz = r * 0.01f;                                                   // v23.60: мельче
            Color dc = new Color(1.07f, 1.055f, 1.035f, 1f);                       // v23.60: чуть светлее теста, без контура
            for (int i = 0; i < count; i++)
            {
                float rr = Mathf.Sqrt((float)rnd.NextDouble()) * 0.92f, a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * rr * r, z = Mathf.Sin(a) * rr * r;
                Vector3 p = new Vector3(x, BatterRelief(x, z, r), z);
                AddDot(v, c, u0, u1, t, p, sz * (0.6f + (float)rnd.NextDouble() * 0.9f), dc, dc, rr, (float)rnd.NextDouble() * 6.28f);
            }
            var m = new Mesh { name = "StandBatterDots (preview)", hideFlags = HideFlags.DontSave };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var u2 = new List<Vector2>(); var u3 = new List<Vector2>();
            for (int i = 0; i < v.Count; i++) { u2.Add(new Vector2(0f, 1f)); u3.Add(Vector2.zero); }
            m.SetVertices(v); m.SetColors(c); m.SetUVs(0, u0); m.SetUVs(1, u1); m.SetUVs(2, u2); m.SetUVs(3, u3);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// v23.59: тесто на мастерке — ВЫТЯНУТЫЙ МАЗОК (как лопаткой провели по полотну), а не кучка:
        /// толстый скруглённый «носик» спереди, к хвосту тоньше и уже, мазок слегка изогнут; вдоль — бороздки от лопатки,
        /// у носика — мягкий бортик. Поверх — крупинки того же цвета с тёмным контуром.
        /// </summary>
        private static void BatterSmear(Mesh m, float rb, float hgt, Color col, int seed)
        {
            const int RG = 64, SG = 200;
            var rnd = new System.Random(seed);
            float sk = (float)rnd.NextDouble() * 6.28f;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            v.Add(SmearPos(0f, 0f, rb, hgt, sk)); c.Add(col);
            for (int i = 1; i <= RG; i++)
                for (int j = 0; j < SG; j++)
                {
                    float f = i / (float)RG, a = j / (float)SG * Mathf.PI * 2f;
                    v.Add(SmearPos(a, f, rb, hgt, sk));
                    c.Add(col);
                }
            for (int j = 0; j < SG; j++) { t.Add(0); t.Add(1 + j); t.Add(1 + (j + 1) % SG); }   // обход обратный: угол идёт от оси мазка к ширине
            for (int i = 1; i < RG; i++)
                for (int j = 0; j < SG; j++)
                {
                    int a = 1 + (i - 1) * SG + j, b = 1 + (i - 1) * SG + (j + 1) % SG, a2 = a + SG, b2 = b + SG;
                    t.Add(a); t.Add(a2); t.Add(b);
                    t.Add(b); t.Add(a2); t.Add(b2);
                }
            // крупинки того же цвета, с контуром
            float dsz = rb * 0.014f;                                                // v23.60: мельче, без контура
            for (int k = 0; k < 300; k++)
            {
                float rr = Mathf.Sqrt((float)rnd.NextDouble()) * 0.86f, a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                Vector3 p = SmearPos(a, rr, rb, hgt, sk);
                float tone = 1.06f + (float)rnd.NextDouble() * 0.04f;                 // чуть светлее теста — заметно, но в тон
                Color cc = new Color(Mathf.Clamp01(col.r * tone), Mathf.Clamp01(col.g * tone), Mathf.Clamp01(col.b * tone * 0.99f), 1f);
                AddDot(v, c, null, null, t, p, dsz * (0.6f + (float)rnd.NextDouble() * 0.9f), cc, cc, 0f, (float)rnd.NextDouble() * 6.28f);
            }
            m.Clear();
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
        }

        /// <summary>Точка мазка по (угол, доля радиуса) единичного круга: вдоль — ось мазка (повёрнута), поперёк — ширина.</summary>
        private static Vector3 SmearPos(float a, float rho, float rb, float hgt, float sk)
        {
            float u = Mathf.Cos(a) * rho;                    // −1 хвост … +1 носик
            float w = Mathf.Sin(a) * rho;                    // −1 … +1 поперёк
            float L = rb * 1.3f;
            float t01 = (u + 1f) * 0.5f;
            float width = rb * (0.58f + 0.3f * Mathf.Pow(t01, 0.7f));            // к носику шире
            float along = u * L;
            float bend = (u * u - 0.35f) * rb * 0.28f + Mathf.Sin(u * 2.3f + sk) * rb * 0.05f;   // мазок изогнут
            float across = w * width + bend;
            // высота: носик толстый, хвост тонкий; поперёк — скруглено; у самого края — мягко к полотну
            float thick = hgt * (0.45f + 0.55f * Mathf.Pow(t01, 1.1f));            // v23.60: хвост тоже пышный
            float edge = Mathf.Pow(Mathf.Max(0f, 1f - rho * rho), 0.3f);             // v23.60: бока круглее, объёмнее
            float y = thick * edge;
            // бороздки от лопатки вдоль мазка (к носику гаснут — там тесто собралось валиком)
            float groove = Mathf.Sin(w * Mathf.PI * 3.2f + Mathf.Sin(u * 3f + sk) * 0.6f + sk);
            y += groove * hgt * 0.045f * (1f - t01 * 0.6f) * edge;
            // валик-бортик у носика
            float lip = Mathf.Exp(-((rho - 0.78f) / 0.12f) * ((rho - 0.78f) / 0.12f)) * Mathf.Max(0f, Mathf.Cos(a));
            y += lip * hgt * 0.12f;
            y += (Mathf.PerlinNoise(along / (rb * 0.5f) + sk, across / (rb * 0.5f) + 3.7f) - 0.5f) * hgt * 0.08f * edge;
            // v23.60: пушистые воздушные клубы по верху (мягкие бугры)
            float puff = Mathf.PerlinNoise(along / (rb * 0.32f) + 11.3f + sk, across / (rb * 0.32f) + 5.1f);
            y += Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((puff - 0.35f) / 0.4f)) * hgt * 0.16f * edge * (0.6f + 0.4f * t01);
            if (rho >= 0.999f) y = 0.0004f;
            // ось мазка повёрнута на ~25° к ручке — смотрится живее
            const float cs = 0.906f, sn = 0.423f;
            float x = across * cs + along * sn, z = -across * sn + along * cs;
            return new Vector3(x, Mathf.Max(0.0004f, y), z);
        }

        private static void BucketLabel(Transform root, string text, Vector3 localPos, float ir, bool ok)
        {
            var tg = new GameObject("Label");
            tg.transform.SetParent(root, false);
            tg.transform.localPosition = localPos;
            TextMesh tm = tg.AddComponent<TextMesh>();
            tm.text = text.Replace("+", " + ");
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = ir * 0.05f;
            tm.fontSize = 48;
            tm.color = ok ? new Color(1f, 0.92f, 0.6f) : new Color(1f, 0.6f, 0.5f);
            CCStyleStandBillboard.SetFont(tm);
            tg.AddComponent<CCStyleStandBillboard>();
        }

        private Transform PreviewTrowel(string title, int i, Material steel)
        {
            Transform root = new GameObject("Trowel_" + title + " (preview)").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(i * PlateStep, 0f, PlateRowZ);
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "Blade";
            p.transform.SetParent(root, false);
            p.transform.localScale = new Vector3(PlateSize.x, 0.012f, PlateSize.y);
            p.transform.localPosition = new Vector3(0f, 0.006f, 0f);
            p.GetComponent<MeshRenderer>().sharedMaterial = steel;
            Object.Destroy(p.GetComponent<Collider>());
            GameObject hd = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hd.name = "Handle";
            hd.transform.SetParent(root, false);
            hd.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hd.transform.localScale = new Vector3(PlateSize.x * 0.12f, PlateSize.y * 0.45f, PlateSize.x * 0.12f);
            hd.transform.localPosition = new Vector3(0f, PlateSize.x * 0.08f, -PlateSize.y * 0.5f - PlateSize.y * 0.4f);
            hd.GetComponent<MeshRenderer>().sharedMaterial = steel;
            Object.Destroy(hd.GetComponent<Collider>());
            if (Labels)
            {
                var lg = new GameObject("Label");
                lg.transform.SetParent(root, false);
                lg.transform.localPosition = new Vector3(0f, PlateSize.x * (0.8f + 0.25f * (i % 2)), 0f);
                TextMesh tm = lg.AddComponent<TextMesh>();
                tm.text = title + " (превью)";
                tm.anchor = TextAnchor.MiddleCenter;
                tm.characterSize = PlateSize.x * 0.035f;
                tm.fontSize = 48;
                tm.color = new Color(1f, 0.92f, 0.6f);
                CCStyleStandBillboard.SetFont(tm);
                lg.AddComponent<CCStyleStandBillboard>();
            }
            return root;
        }

        private void PlatePreviewAlmond(FoodDatabase db, int i, Material steel)
        {
            Transform root = PreviewTrowel(PreviewAlmond, i, steel);
            float k = PlateSize.x / 0.11f;
            ProductDef flour = CCStyleStandSlot.FindByTitle(db, "Мука миндальная");
            FoodSubstance sub = flour != null ? FoodSubstance.Of(flour, FoodState.None) : null;
            FoodSubstanceMesh.Dims dims;
            if (sub != null) dims = ScoopSubstanceView.Dims(sub, PlateSize * 0.72f, PlatePortions * 0.004f * k * k * k);
            else
            {
                dims = new FoodSubstanceMesh.Dims();
                dims.Rx = PlateSize.x * 0.3f; dims.Rz = PlateSize.y * 0.3f; dims.H = dims.Rx * 0.55f; dims.Edge = 0f; dims.RectN = 2f;
            }
            AlmondHeap(root, new Vector3(0f, 0.012f, 0f), db, dims, i * 37 + 11, 70);
        }

        private void PlatePreviewBatter(int i, Material steel)
        {
            Transform root = PreviewTrowel(PreviewBatter, i, steel);
            float rb = Mathf.Min(PlateSize.x, PlateSize.y) * 0.3f;
            var go = new GameObject("MacaronBatter (preview)") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            var mesh = new Mesh { name = "Preview MacaronBatter", hideFlags = HideFlags.DontSave };
            BatterSmear(mesh, rb, rb * 1.0f, BatterCol, i * 37 + 5);          // v23.60: пышный мазок
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = PreviewBatterMat();
        }

        private void Plate(ProductDef d, int i, Material steel)
        {
            Transform root = new GameObject("Trowel_" + d.Title).transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(i * PlateStep, 0f, PlateRowZ);
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "Blade";
            p.transform.SetParent(root, false);
            p.transform.localScale = new Vector3(PlateSize.x, 0.012f, PlateSize.y);
            p.transform.localPosition = new Vector3(0f, 0.006f, 0f);
            p.GetComponent<MeshRenderer>().sharedMaterial = steel;
            Object.Destroy(p.GetComponent<Collider>());
            // v23.6: ручка — чтобы читалось как мастерок
            GameObject hd = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hd.name = "Handle";
            hd.transform.SetParent(root, false);
            hd.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hd.transform.localScale = new Vector3(PlateSize.x * 0.12f, PlateSize.y * 0.45f, PlateSize.x * 0.12f);
            hd.transform.localPosition = new Vector3(0f, PlateSize.x * 0.08f, -PlateSize.y * 0.5f - PlateSize.y * 0.4f);
            hd.GetComponent<MeshRenderer>().sharedMaterial = steel;
            Object.Destroy(hd.GetComponent<Collider>());

            FoodSubstance sub = FoodSubstance.Of(d, FoodState.None);
            if (sub != null && sub.OwnLook)
            {
                var go = new GameObject("Heap") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(0f, 0.012f, 0f);
                var mesh = new Mesh { name = "Stand " + d.Title, hideFlags = HideFlags.DontSave };
                float k = PlateSize.x / 0.11f;
                // v23.6: горка ЛЕЖИТ на полотне (меньше его), а не закрывает целиком
                FoodSubstanceMesh.Dims dims = ScoopSubstanceView.Dims(sub, PlateSize * 0.72f, PlatePortions * 0.004f * k * k * k);
                Color col = sub.ColorFor(FoodColors.Of(d, FoodState.None));
                int seed = i * 37 + 11;
                if (sub.Look == SubstanceLook.Powder) FoodSubstanceMesh.Powder(mesh, dims, sub.Wonky, col, seed, sub.Sugar);
                else if (sub.Look == SubstanceLook.Crystals) FoodSubstanceMesh.Crystals(mesh, dims, sub.CrystalSize, sub.CrystalDensity, col, seed, sub.Salt);
                else if (sub.Look == SubstanceLook.Spread)
                {
                    float grow = Mathf.Clamp01((dims.RectN - 2.6f) / 1.4f);           // v23.6: с капельками по краю
                    float cover = Mathf.Lerp(sub.BladeCover, 1f, grow);
                    FoodSubstanceMesh.DripEdge = 1f / (0.94f * 0.72f * Mathf.Max(0.2f, cover)) + 0.03f;
                    FoodSubstanceMesh.SpreadDrips = 3;
                    FoodSubstanceMesh.Spread(mesh, dims, sub.Curl * 0.8f, col, seed);
                    FoodSubstanceMesh.SpreadDrips = 0;
                }
                else if (sub.Look == SubstanceLook.IceCream) FoodSubstanceMesh.IceCream(mesh, dims, col, seed);
                else if (sub.Look == SubstanceLook.Meringue) FoodSubstanceMesh.MeringueKiss(mesh, dims, col, seed);
                else FoodSubstanceMesh.ButterSmear(mesh, dims, sub.Curl, col, seed);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                Material m = sub.GetMaterial();
                if (m != null) mr.sharedMaterial = m;

            }
            if (Labels)
            {
                var lg = new GameObject("Label");
                lg.transform.SetParent(root, false);
                lg.transform.localPosition = new Vector3(0f, PlateSize.x * (0.8f + 0.25f * (i % 2)), 0f);   // через одну выше — не налезают
                TextMesh tm = lg.AddComponent<TextMesh>();
                tm.text = d.Title;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.characterSize = PlateSize.x * 0.035f;
                tm.fontSize = 48;
                tm.color = Color.white;
                CCStyleStandBillboard.SetFont(tm);
                lg.AddComponent<CCStyleStandBillboard>();
            }
        }
    }

    /// <summary>v23.5: ведро стенда — сервер на старте наливает в него продукт (по названию).</summary>
    public sealed class CCStyleStandSlot : MonoBehaviour
    {
        public string ProductTitle = "Молоко";
        [Tooltip("Сколько налить, вёдер (доля вместимости ведра)")]
        [Range(0.05f, 1f)] public float Buckets = 0.6f;
        [Tooltip("Второй продукт (например, желтки поверх белка). Пусто — нет")]
        public string SecondTitle = "";
        [Range(0f, 1f)] public float SecondBuckets = 0f;
        public bool Label = true;

        private bool _done;
        private Cookware _cw;

        private void Start()
        {
            _cw = GetComponentInChildren<Cookware>();
            if (Label && !Application.isBatchMode)
            {
                var lg = new GameObject("StandLabel");
                lg.transform.SetParent(transform, false);
                Renderer r = GetComponentInChildren<Renderer>();
                float h = r != null ? r.bounds.size.y : 1f;
                lg.transform.position = transform.position + Vector3.up * (h * 1.25f);
                TextMesh tm = lg.AddComponent<TextMesh>();
                tm.text = ProductTitle + (string.IsNullOrEmpty(SecondTitle) ? "" : " + " + SecondTitle);
                tm.anchor = TextAnchor.MiddleCenter;
                tm.characterSize = h * 0.035f;
                tm.fontSize = 48;
                tm.color = Color.white;
                CCStyleStandBillboard.SetFont(tm);
                lg.AddComponent<CCStyleStandBillboard>();
            }
        }

        private float _holdT;

        private void Update()
        {
            // v23.12: пока ведро стоит на стенде (его родитель — стенд), оно неподвижно — не падает и не проваливается
            _holdT += Time.deltaTime;
            if (_holdT > 0.25f)
            {
                _holdT = 0f;
                Rigidbody rb = GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic && transform.parent != null && transform.parent.GetComponent<CCStyleStand>() != null) rb.isKinematic = true;
            }
            if (_done || _cw == null) return;
            if (!_cw.IsServerInitialized) return;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            _done = true;
            Fill(db, ProductTitle, Buckets);
            if (!string.IsNullOrEmpty(SecondTitle) && SecondBuckets > 0f) Fill(db, SecondTitle, SecondBuckets);
        }

        private void Fill(FoodDatabase db, string title, float buckets)
        {
            ProductDef d = FindByTitle(db, title);
            if (d == null) { Debug.LogWarning($"[Стенд] нет продукта «{title}»", this); return; }
            int n = Mathf.Max(1, Mathf.RoundToInt(FoodUnits.Bucket * buckets));
            int put = _cw.ServerAddContent(d.Id, FoodState.None, n, 0, false);
            Debug.Log($"[Стенд] «{name}»: {d.Title} ×{put}", this);
        }

        public static ProductDef FindByTitle(FoodDatabase db, string title)
        {
            if (db == null || string.IsNullOrEmpty(title)) return null;
            string t = title.Trim().ToLowerInvariant();
            ProductDef part = null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef d = db.Get((ushort)id);
                if (d == null || string.IsNullOrEmpty(d.Title)) continue;
                string dt = d.Title.ToLowerInvariant();
                if (dt == t) return d;
                if (part == null && dt.Contains(t)) part = d;
            }
            return part;
        }
    }

    /// <summary>Подпись всегда повёрнута к камере.</summary>
    public sealed class CCStyleStandBillboard : MonoBehaviour
    {
        private static Font _font;

        /// <summary>Встроенный шрифт (без него TextMesh ничего не рисует).</summary>
        public static void SetFont(TextMesh tm)
        {
            if (_font == null)
            {
                try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { _font = null; }
                if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { _font = null; } }
            }
            if (_font == null) return;
            tm.font = _font;
            MeshRenderer mr = tm.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = _font.material;
        }

        private void LateUpdate()
        {
            Camera c = Camera.main;
            if (c != null) transform.rotation = Quaternion.LookRotation(transform.position - c.transform.position, Vector3.up);
        }
    }

    /// <summary>
    /// v23.22: ПЕНА ЯИЧНОЙ МАССЫ — настоящие объёмные пузырьки-купола (не рисунок): лежат естественными кучками
    /// (5 кучек плавают по поверхности, пузырьки вокруг них не налезают друг на друга), разного размера (мелких больше),
    /// медленно вырастают, живут несколько секунд и лопаются, потом появляются снова. Работает в любой таре и на стенде.
    /// </summary>
    public sealed class CCFoamField : MonoBehaviour
    {
        public CookwareLiquidVisual Lv;
        public Vector3 Center;
        public float Radius = 0.2f;
        public float SurfaceY;
        public Color Color = new Color(1f, 0.95f, 0.8f, 1f);
        public bool Active;
        public int Count = 40;

        private sealed class B { public Transform T; public MeshRenderer Mr; public Vector2 P; public float Size, Life, Dur; public int C; }
        private readonly List<B> _b = new List<B>();
        private readonly Vector2[] _cl = new Vector2[5], _clV = new Vector2[5];
        private bool _clInit;
        private MaterialPropertyBlock _mpb;
        private static readonly int ColId = Shader.PropertyToID("_BaseColor"), PopId = Shader.PropertyToID("_Pop");

        private void OnDisable() { foreach (B b in _b) if (b.T != null) b.T.gameObject.SetActive(false); }

        private void OnDestroy() { foreach (B b in _b) if (b.T != null) Destroy(b.T.gameObject); }

        private void Update()
        {
            Material mat = CookwareLiquidVisual.FoamBubbleMat;
            Mesh mesh = CookwareLiquidVisual.FoamBubbleMesh;
            if (!Active || mat == null || mesh == null || Radius <= 1e-4f) { foreach (B b in _b) if (b.T != null && b.T.gameObject.activeSelf) b.T.gameObject.SetActive(false); return; }
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (!_clInit)
            {
                _clInit = true;
                for (int c = 0; c < _cl.Length; c++) { _cl[c] = Random.insideUnitCircle * 0.65f; _clV[c] = Random.insideUnitCircle.normalized * Random.Range(0.05f, 0.1f); }   // v23.23: плавают заметнее
            }
            for (int c = 0; c < _cl.Length; c++)                                    // кучки медленно плавают
            {
                _cl[c] += _clV[c] * dt;
                if (_cl[c].magnitude > 0.7f) { _cl[c] = _cl[c].normalized * 0.7f; _clV[c] = -_clV[c]; }
            }
            while (_b.Count < Count)
            {
                var go = new GameObject("FoamBubble (auto)") { hideFlags = HideFlags.DontSave };
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, true);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var b = new B { T = go.transform, Mr = go.AddComponent<MeshRenderer>(), Life = 99f, Dur = 1f };
                b.Mr.sharedMaterial = mat;
                b.Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b.Life = 0f; b.Dur = 0f;                                            // сразу появиться
                _b.Add(b);
            }
            foreach (B b in _b)
            {
                b.Life += dt;
                if (b.Life >= b.Dur) Respawn(b);
                float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(b.Life / 0.6f));
                float pop = Mathf.Clamp01((b.Life - (b.Dur - 0.25f)) / 0.25f);
                float wob = b.Size * 0.6f;                                          // v23.23: каждый ещё чуть «гуляет» сам
                b.P += (_clV[b.C] * Radius + new Vector2(Mathf.Sin(Time.time * 0.7f + b.Size * 900f), Mathf.Cos(Time.time * 0.6f + b.Size * 700f)) * wob) * dt;
                if (b.P.magnitude > Radius * 0.88f) b.P = b.P.normalized * Radius * 0.88f;
                Vector3 w = Center + new Vector3(b.P.x, 0f, b.P.y);
                float sy = SurfaceY;
                if (Lv != null && !Lv.TryGetSurfaceY(w, out sy)) sy = SurfaceY;
                float sz = b.Size * grow;
                w.y = sy - sz * 0.12f;
                if (!b.T.gameObject.activeSelf) b.T.gameObject.SetActive(true);
                b.T.position = w;
                b.T.rotation = Quaternion.identity;
                // v23.38: размер — в МЕТРАХ мира. Тара может быть увеличена (корыто, банка — масштаб 200): делим на масштаб родителя,
                // иначе пузырь выходил в сотни раз больше и «взрывался» на всю сцену
                Vector3 ps = transform.lossyScale;
                b.T.localScale = new Vector3(sz / Mathf.Max(1e-5f, Mathf.Abs(ps.x)), sz * 0.8f / Mathf.Max(1e-5f, Mathf.Abs(ps.y)), sz / Mathf.Max(1e-5f, Mathf.Abs(ps.z)));
                _mpb.SetColor(ColId, Color);
                _mpb.SetFloat(PopId, pop);
                b.Mr.SetPropertyBlock(_mpb);
            }
        }

        private void Respawn(B nb)
        {
            for (int tries = 0; tries < 8; tries++)
            {
                int c = Random.Range(0, _cl.Length);
                bool free = Random.value < 0.15f;                                   // немного одиночных по всей поверхности
                Vector2 p = free ? Random.insideUnitCircle * 0.8f : _cl[c] + Random.insideUnitCircle * 0.16f;
                if (p.magnitude > 0.85f) continue;
                float size = Mathf.Min(Radius, 0.45f) * (0.05f + 0.11f * Mathf.Pow(Random.value, 2.2f));   // v23.38: в огромной таре пузыри не больше ~7 см
                Vector2 pw = p * Radius;
                bool ok = true;
                foreach (B o in _b)
                {
                    if (o == nb || o.Life >= o.Dur) continue;
                    if ((o.P - pw).sqrMagnitude < (o.Size + size) * (o.Size + size) * 0.2f) { ok = false; break; }   // не налезают
                }
                if (!ok && tries < 7) continue;
                nb.P = pw; nb.Size = size; nb.C = c; nb.Life = 0f; nb.Dur = Random.Range(15f, 35f);    // v23.23: лопаются редко
                return;
            }
        }
    }
}
