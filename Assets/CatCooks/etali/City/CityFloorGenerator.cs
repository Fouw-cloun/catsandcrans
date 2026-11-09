using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// РАБОЧИЙ ЭТАЖ — генератор из твоих модулей (балочный, стеклянный, кирпичный).
    ///   • Этаж 1 («подвал»): по сетке клеток — балочный модуль в каждой клетке (колонны, балки, плита сверху —
    ///     на ней стоит игрок), стены (стекло/кирпич) только по ВНЕШНЕМУ краю; внутренние стены и углы вырезаются —
    ///     внутри остаётся балочная система.
    ///   • Этаж 2 (уровень игрока): балочный каркас «в процессе стройки» — колонны везде, балки местами,
    ///     плита-перекрытие кусками, редкие куски готовых стен по краю.
    ///   • Этажи 3+ (по желанию): редкие колонны и балки.
    /// Позиция объекта-генератора = уровень пола игрока (верх плиты этажа 1). Размер — случайный в пределах.
    /// Модули подставляются сами (по GUID твоих FBX), можно указать руками.
    /// </summary>
    public class CityFloorGenerator : MonoBehaviour
    {
        public enum FootShape { Rect, LShape, Random }
        public enum WallStyle { BrickAndBeams, GlassAndBeams, Mixed, RandomBySeed }

        [Header("Модули (целые, со всех сторон)")]
        [Tooltip("full bulk module — колонны, балки, плита сверху")]
        public GameObject BulkModule;
        [Tooltip("glass full module — стеклянные стены с 4 сторон")]
        public GameObject GlassModule;
        [Tooltip("brick full module — кирпичные стены с 4 сторон")]
        public GameObject BrickModule;

        [Header("Из чего дом")]
        [Tooltip("Кирпич + балки, стекло + балки, вперемешку или по сиду")]
        public WallStyle Walls = WallStyle.RandomBySeed;

        [Header("Материалы (создаются сами из атласов модулей; окна — шейдер окон города)")]
        public bool ApplyMaterials = true;
        public Material BulkMaterial;
        public Material BrickMaterial;
        public Material GlassMaterial;
        public Material WindowMaterial;

        [Header("Размер")]
        [Tooltip("Масштаб модулей (×2 — как у тебя на уровне)")]
        public float ModuleScale = 2f;
        [Tooltip("Шаг сетки модуля до масштаба, ед. (0 — сам по плите модуля)")]
        public float CellSize = 0f;
        [Tooltip("Высота этажа до масштаба, ед. (0 — сама по колоннам)")]
        public float FloorHeight = 0f;
        [Tooltip("Клеток по ширине: от и до")]
        public Vector2Int Width = new Vector2Int(3, 6);
        [Tooltip("Клеток в глубину: от и до")]
        public Vector2Int Depth = new Vector2Int(3, 5);
        public FootShape Shape = FootShape.Random;
        public int Seed = 1;

        [Header("Этаж 1 (подвал): стены по краю")]
        [Tooltip("Доля стекла при «вперемешку» (остальное — кирпич)")]
        [Range(0f, 1f)] public float GlassShare = 0.6f;
        [Tooltip("Сколько клеток подряд одна и та же стена (от и до)")]
        public Vector2Int WallRun = new Vector2Int(1, 3);

        [Header("Этаж 2 (уровень игрока): каркас в процессе")]
        [Range(0f, 1f)] public float BeamShare = 0.7f;
        [Tooltip("Плита-перекрытие сверху (кусками)")]
        [Range(0f, 1f)] public float CeilingShare = 0.25f;
        [Tooltip("Куски готовых стен по краю")]
        [Range(0f, 1f)] public float WallPieceShare = 0.3f;

        [Header("Этажи 3+")]
        [Range(0, 3)] public int UpperFloors = 1;
        [Range(0f, 1f)] public float UpperShare = 0.4f;

        [Header("Фейк-этажи под домом (картинки фасадов подвала)")]
        [Tooltip("Сколько фейковых этажей вырастить под подвалом")]
        [Range(0, 60)] public int FakeFloors = 6;
        [Tooltip("Пикселей на единицу при съёмке фасадов (больше — чётче, тяжелее текстура)")]
        [Range(8, 64)] public int FakePixelsPerUnit = 24;
        [Tooltip("Цвет пустоты при съёмке (видно сквозь окна, если внутри пусто)")]
        public Color FakeBackground = new Color(0.12f, 0.13f, 0.16f, 1f);
        [Tooltip("Насколько фейк-этажи плоские по свету (0 — освещаются сценой, 1 — как на фото)")]
        [Range(0f, 1f)] public float FakeUnlit = 0.6f;

        [Header("Прочее")]
        [Tooltip("Коллайдеры: плиты и стены (по габариту)")]
        public bool AddColliders = true;
        [Tooltip("Плиты пола мокнут/засыпает снегом (City Wet Surface)")]
        public bool MarkWet = true;
        public bool Log = true;

        // GUID моделей из твоего пакета «etali» — модули находятся сами
        public const string GuidBulk = "36af0717566fd994ea017fafd4609ff6";
        public const string GuidGlass = "25dae03d0987db94980f1cb7e4e80a89";
        public const string GuidBrick = "92c8d727d5aed914184cb13203fb75ab";
        // атласы текстур модулей
        public const string GuidTexBulk = "98cb33984dcaba74f93c55e17d5f55de";
        public const string GuidTexBrick = "011ec139492f8a543a0c8ed90dc8da6d";
        public const string GuidTexGlass = "1124aa4356c012b459f8e23e3aea3f90";

        /// <summary>Итоговый стиль дома (RandomBySeed — по сиду: кирпич, стекло или вперемешку).</summary>
        public WallStyle ResolvedStyle()
        {
            if (Walls != WallStyle.RandomBySeed) return Walls;
            int k = new System.Random(Seed * 31 + 7).Next(3);
            return k == 0 ? WallStyle.BrickAndBeams : (k == 1 ? WallStyle.GlassAndBeams : WallStyle.Mixed);
        }

        /// <summary>Доля стекла для текущего стиля.</summary>
        public float GlassShareNow()
        {
            WallStyle st = ResolvedStyle();
            return st == WallStyle.GlassAndBeams ? 1f : (st == WallStyle.BrickAndBeams ? 0f : GlassShare);
        }

        public const string RootName = "GeneratedFloors";

        // ── посчитанный план (для гизмо и генерации) ──
        [NonSerialized] public int PlanW, PlanD;
        [NonSerialized] public bool[,] Cells;

        public float Pitch { get { return (CellSize > 0f ? CellSize : _autoCell) * ModuleScale; } }
        public float Height { get { return (FloorHeight > 0f ? FloorHeight : _autoHeight) * ModuleScale; } }
        public float SlabTop { get { return _autoSlabTop * ModuleScale; } }

        [SerializeField, HideInInspector] private float _autoCell = 4f, _autoHeight = 4f, _autoSlabTop = 3.9f;

        /// <summary>Замерить модуль: шаг — по плите (округлено до 0.5), высота — по колоннам, верх плиты — пол игрока.</summary>
        public void Measure()
        {
            if (BulkModule == null) return;
            GameObject tmp = Instantiate(BulkModule);
            tmp.hideFlags = HideFlags.HideAndDontSave;
            tmp.transform.position = Vector3.zero; tmp.transform.rotation = Quaternion.identity; tmp.transform.localScale = Vector3.one;
            float slabW = 0f, slabTop = 0f, colTop = 0f;
            float cx0 = float.MaxValue, cx1 = float.MinValue, cz0 = float.MaxValue, cz1 = float.MinValue;
            Bounds slabB = new Bounds(); bool anySlab = false;
            foreach (Renderer r in tmp.GetComponentsInChildren<Renderer>())
            {
                string n = r.gameObject.name.ToLowerInvariant();
                Bounds b = r.bounds;
                if (n.Contains("beton"))
                {
                    if (!anySlab) { slabB = b; anySlab = true; } else slabB.Encapsulate(b);   // плита может быть из нескольких кусков
                    slabTop = Mathf.Max(slabTop, b.max.y);
                }
                if (n.Contains("vertical"))
                {
                    colTop = Mathf.Max(colTop, b.max.y);
                    cx0 = Mathf.Min(cx0, b.center.x); cx1 = Mathf.Max(cx1, b.center.x);
                    cz0 = Mathf.Min(cz0, b.center.z); cz1 = Mathf.Max(cz1, b.center.z);
                }
            }
            DestroyImmediate(tmp);
            if (anySlab) slabW = Mathf.Max(slabB.size.x, slabB.size.z);
            float colSpan = Mathf.Max(cx1 - cx0, cz1 - cz0);
            if (colSpan > 0.5f) _autoCell = Mathf.Round(colSpan * 100f) / 100f;            // шаг = расстояние между колоннами (соседи делят колонны)
            else if (slabW > 0f) _autoCell = Mathf.Max(0.5f, Mathf.Round(slabW * 2f) * 0.5f);
            if (colTop > 0f) _autoHeight = colTop;
            if (slabTop > 0f) _autoSlabTop = slabTop;
            if (Log) Debug.Log("[Этаж] модуль: шаг " + _autoCell.ToString("0.##") + ", высота " + _autoHeight.ToString("0.##") + ", верх плиты " + _autoSlabTop.ToString("0.##") + " (до масштаба ×" + ModuleScale + ")", this);
        }

        /// <summary>План клеток этажа 1 (прямоугольник или буква Г) по сиду.</summary>
        public void MakePlan()
        {
            System.Random rnd = new System.Random(Seed);
            PlanW = rnd.Next(Mathf.Max(1, Width.x), Mathf.Max(Width.x, Width.y) + 1);
            PlanD = rnd.Next(Mathf.Max(1, Depth.x), Mathf.Max(Depth.x, Depth.y) + 1);
            Cells = new bool[PlanW, PlanD];
            for (int x = 0; x < PlanW; x++) for (int z = 0; z < PlanD; z++) Cells[x, z] = true;
            bool l = Shape == FootShape.LShape || (Shape == FootShape.Random && rnd.NextDouble() < 0.5);
            if (l && PlanW >= 3 && PlanD >= 3)
            {
                int nw = rnd.Next(1, PlanW / 2 + 1), nd = rnd.Next(1, PlanD / 2 + 1);
                bool right = rnd.NextDouble() < 0.5, far = rnd.NextDouble() < 0.5;
                for (int x = 0; x < nw; x++) for (int z = 0; z < nd; z++)
                    Cells[right ? PlanW - 1 - x : x, far ? PlanD - 1 - z : z] = false;
            }
        }

        public bool Has(int x, int z) { return Cells != null && x >= 0 && z >= 0 && x < PlanW && z < PlanD && Cells[x, z]; }

        /// <summary>Центр клетки в мире (низ этажа floor: 0 — подвал).</summary>
        public Vector3 CellPos(int x, int z, int floor)
        {
            float P = Pitch;
            Vector3 o = transform.position - new Vector3((PlanW - 1) * P * 0.5f, 0f, (PlanD - 1) * P * 0.5f);
            float baseY = -SlabTop + floor * Height;                  // верх плиты подвала = уровень генератора
            return o + new Vector3(x * P, baseY, z * P);
        }

        private void OnDrawGizmosSelected()
        {
            if (Cells == null) MakePlan();
            float P = Pitch;
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
            for (int x = 0; x < PlanW; x++)
                for (int z = 0; z < PlanD; z++)
                {
                    if (!Has(x, z)) continue;
                    Vector3 c = CellPos(x, z, 0) + Vector3.up * SlabTop;
                    Gizmos.DrawWireCube(c, new Vector3(P, 0.02f, P));
                }
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * Height * 0.5f, new Vector3(PlanW * P, Height, PlanD * P));
        }
    }
}
