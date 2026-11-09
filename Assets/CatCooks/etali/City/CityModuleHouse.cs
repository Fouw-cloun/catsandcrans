using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ДОМ ИЗ МОДУЛЕЙ «КУБИКАМИ» (столовая, бытовки — дом котов). Сетка клеток X×этаж×Z, каждая клетка — свой тип
    /// (комната, каркас с плитой, каркас, колонны, плита) и свои стены по 4 сторонам. Рисуется мышкой в сцене
    /// (инспектор → «Рисовать»), собирается из тех же модулей, что и генератор этажа: балочный + стекло / кирпич.
    /// Соседи делят колонны и балки без дублей, стены по умолчанию — только снаружи.
    /// Сборка — только в редакторе (CityModuleHouseEditor). В игре компонент ничего не делает.
    /// </summary>
    public class CityModuleHouse : MonoBehaviour
    {
        public enum CellKind { Room = 0, Deck = 1, Frame = 2, Columns = 3, Slab = 4 }
        public enum WallKind { Auto = 0, None = 1, Glass = 2, Brick = 3, BrickBlank = 4 }
        public enum GlassMode { Clear = 0, CityWindows = 1 }

        [Serializable]
        public class Cell
        {
            public int X, Y, Z;
            public CellKind Kind = CellKind.Room;
            public WallKind PX, NX, PZ, NZ;            // стены по сторонам: +X, −X, +Z, −Z
            [Tooltip("Ручная клетка: пересборка её не трогает (можно править руками)")]
            public bool Locked;
            [Tooltip("Убранные балки: бит 0 +X, 1 −X, 2 +Z, 3 −Z, 4 — средняя; колонны по углам: 5 +X+Z, 6 −X+Z, 7 +X−Z, 8 −X−Z")]
            public int BeamOff;

            public bool BeamRemoved(int s) { return (BeamOff & (1 << s)) != 0; }
            public void SetBeam(int s, bool removed) { if (removed) BeamOff |= 1 << s; else BeamOff &= ~(1 << s); }
            /// <summary>Бит угловой колонны по знакам угла (sx, sz = ±1).</summary>
            public static int CornerBit(int sx, int sz) { return 5 + (sx > 0 ? 0 : 1) + (sz > 0 ? 0 : 2); }

            public WallKind Wall(int s) { return s == 0 ? PX : s == 1 ? NX : s == 2 ? PZ : NZ; }
            public void SetWall(int s, WallKind w) { if (s == 0) PX = w; else if (s == 1) NX = w; else if (s == 2) PZ = w; else NZ = w; }
            public Cell Copy() { return (Cell)MemberwiseClone(); }
        }

        [Header("Модули (те же, что у генератора этажа)")]
        public GameObject BulkModule;
        public GameObject GlassModule;
        public GameObject BrickModule;

        [Header("Материалы (пусто — возьмёт/создаст материалы генератора этажа)")]
        public bool ApplyMaterials = true;
        [Tooltip("Стёкла: прозрачные (видно котов внутри) или городские окна с фейковой комнатой (как у фоновых домов)")]
        public GlassMode Windows = GlassMode.Clear;
        [Tooltip("Прозрачное стекло (пусто — создаст CC_House_GlassClear)")]
        public Material ClearGlassMaterial;
        public Material BulkMaterial;
        public Material BrickMaterial;
        public Material GlassMaterial;
        public Material WindowMaterial;

        [Header("Размер")]
        [Tooltip("Масштаб модулей (2.6 = на 30% больше, чем ×2)")]
        public float ModuleScale = 2.6f;
        [Tooltip("Шаг сетки до масштаба (0 — сам по колоннам модуля)")]
        public float CellSize = 0f;
        [Tooltip("Высота этажа до масштаба (0 — сама по колоннам)")]
        public float FloorHeight = 0f;

        [Header("Стены")]
        [Tooltip("Какая стена ставится снаружи у «комнат», если у стороны стоит «Авто»")]
        public WallKind DefaultWall = WallKind.Glass;
        [Tooltip("Пол под комнатой и каркасом с плитой, если снизу нет плиты (нижний этаж, нависание)")]
        public bool FloorUnder = true;
        [Tooltip("Убрать пластины-коннекторы сверху колонн, если над клеткой ничего нет (крыша без «крестиков»)")]
        public bool HideRoofPlates = true;

        [Header("Фейк-этажи под домом (снимок нижнего этажа)")]
        [Range(0, 60)] public int FakeFloors = 0;
        [Range(8, 64)] public int FakePixelsPerUnit = 24;
        public Color FakeBackground = new Color(0.12f, 0.13f, 0.16f, 1f);
        [Range(0f, 1f)] public float FakeUnlit = 0.6f;

        [Header("Прочее")]
        public bool AddColliders = true;
        public bool MarkWet = true;
        public bool Log = true;

        [HideInInspector] public List<Cell> Cells = new List<Cell>();

        // состояние кисти (сохраняется со сценой, чтобы не сбрасывалось)
        [HideInInspector] public bool Painting;
        [HideInInspector] public int ActiveFloor;
        [HideInInspector] public int Tool;                 // 0 кисть, 1 ластик, 2 стена, 3 замок
        [HideInInspector] public CellKind Brush = CellKind.Room;
        [HideInInspector] public WallKind WallBrush = WallKind.Brick;
        [HideInInspector] public bool ShowOtherFloors = true;
        [HideInInspector] public int MapW = 10, MapD = 10;
        [HideInInspector] public int EdgeMode;               // карта: полоски = 0 стены, 1 балки   // размер карты в инспекторе (растёт сам под клетки)

        [SerializeField, HideInInspector] private float _autoCell = 4f, _autoHeight = 4f, _autoSlabTop = 4f;

        public const string BuiltName = "ModuleHouse_Built";
        public const string FakeName = "FakeFloors";

        public float Pitch { get { return (CellSize > 0f ? CellSize : _autoCell) * ModuleScale; } }
        public float Height { get { return (FloorHeight > 0f ? FloorHeight : _autoHeight) * ModuleScale; } }
        public float SlabTop { get { return _autoSlabTop * ModuleScale; } }

        // ── поиск клеток ──
        private Dictionary<long, Cell> _map;

        public static long Key(int x, int y, int z) { return ((long)(x & 0xFFFFF) << 40) | ((long)(y & 0xFFFFF) << 20) | (long)(z & 0xFFFFF); }

        /// <summary>Пересобрать индекс клеток (после правок списка / отмены).</summary>
        public void Reindex()
        {
            if (_map == null) _map = new Dictionary<long, Cell>(); else _map.Clear();
            if (Cells == null) Cells = new List<Cell>();
            for (int i = Cells.Count - 1; i >= 0; i--)
            {
                Cell c = Cells[i];
                if (c == null) { Cells.RemoveAt(i); continue; }
                long k = Key(c.X, c.Y, c.Z);
                if (_map.ContainsKey(k)) { Cells.RemoveAt(i); continue; }          // дубль — убрать
                _map[k] = c;
            }
        }

        public Cell Find(int x, int y, int z)
        {
            if (_map == null) Reindex();
            Cell c;
            return _map.TryGetValue(Key(x, y, z), out c) ? c : null;
        }

        public Cell Add(int x, int y, int z, CellKind kind)
        {
            Cell c = Find(x, y, z);
            if (c != null) { c.Kind = kind; return c; }
            c = new Cell { X = x, Y = y, Z = z, Kind = kind };
            Cells.Add(c);
            _map[Key(x, y, z)] = c;
            return c;
        }

        public bool Remove(int x, int y, int z)
        {
            Cell c = Find(x, y, z);
            if (c == null) return false;
            Cells.Remove(c);
            _map.Remove(Key(x, y, z));
            return true;
        }

        public static bool HasColumns(CellKind k) { return k != CellKind.Slab; }
        public static bool HasBeams(CellKind k) { return k == CellKind.Room || k == CellKind.Deck || k == CellKind.Frame; }
        public static bool HasSlab(CellKind k) { return k == CellKind.Room || k == CellKind.Deck || k == CellKind.Slab; }
        public static bool WantsFloor(CellKind k) { return k == CellKind.Room || k == CellKind.Deck; }

        /// <summary>Какая стена реально стоит у клетки на стороне s (Авто → по соседям).</summary>
        public WallKind ResolveWall(Cell c, int s)
        {
            WallKind w = c.Wall(s);
            if (w != WallKind.Auto) return w;
            if (c.Kind != CellKind.Room) return WallKind.None;
            int dx = s == 0 ? 1 : s == 1 ? -1 : 0, dz = s == 2 ? 1 : s == 3 ? -1 : 0;
            Cell n = Find(c.X + dx, c.Y, c.Z + dz);
            if (n != null && n.Kind == CellKind.Room) return WallKind.None;
            if (n != null && n.Wall(Opp(s)) != WallKind.Auto && n.Wall(Opp(s)) != WallKind.None) return WallKind.None;   // стену уже ставит сосед
            return DefaultWall == WallKind.Auto ? WallKind.Glass : DefaultWall;
        }

        public static int Opp(int s) { return s == 0 ? 1 : s == 1 ? 0 : s == 2 ? 3 : 2; }

        /// <summary>Центр низа клетки в локальных координатах дома.</summary>
        public Vector3 LocalPos(int x, int y, int z) { return new Vector3(x * Pitch, y * Height, z * Pitch); }

        public void FloorRange(out int lo, out int hi)
        {
            lo = int.MaxValue; hi = int.MinValue;
            foreach (Cell c in Cells) { if (c == null) continue; lo = Mathf.Min(lo, c.Y); hi = Mathf.Max(hi, c.Y); }
            if (lo > hi) { lo = 0; hi = 0; }
        }

        /// <summary>Замерить балочный модуль: шаг — расстояние между колоннами, высота — по колоннам, верх плиты.</summary>
        public void Measure()
        {
            if (BulkModule == null) return;
            GameObject tmp = Instantiate(BulkModule);
            tmp.hideFlags = HideFlags.HideAndDontSave;
            tmp.transform.position = Vector3.zero; tmp.transform.rotation = Quaternion.identity; tmp.transform.localScale = Vector3.one;
            float slabTop = 0f, colTop = 0f, slabW = 0f;
            float cx0 = float.MaxValue, cx1 = float.MinValue, cz0 = float.MaxValue, cz1 = float.MinValue;
            Bounds slabB = new Bounds(); bool anySlab = false;
            foreach (Renderer r in tmp.GetComponentsInChildren<Renderer>())
            {
                string n = r.gameObject.name.ToLowerInvariant();
                Bounds b = r.bounds;
                if (n.Contains("beton"))
                {
                    if (!anySlab) { slabB = b; anySlab = true; } else slabB.Encapsulate(b);
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
            if (colSpan > 0.5f) _autoCell = Mathf.Round(colSpan * 100f) / 100f;
            else if (slabW > 0f) _autoCell = Mathf.Max(0.5f, Mathf.Round(slabW * 2f) * 0.5f);
            if (colTop > 0f) _autoHeight = colTop;
            if (slabTop > 0f) _autoSlabTop = slabTop;
            if (Log) Debug.Log("[Дом-модули] модуль: шаг " + _autoCell.ToString("0.##") + ", высота " + _autoHeight.ToString("0.##") + " (×" + ModuleScale + " = клетка " + Pitch.ToString("0.##") + " м, этаж " + Height.ToString("0.##") + " м)", this);
        }

        private void OnDrawGizmosSelected()
        {
            if (Cells == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            float P = Pitch, H = Height;
            foreach (Cell c in Cells)
            {
                if (c == null) continue;
                bool act = c.Y == ActiveFloor;
                if (!act && Painting && !ShowOtherFloors) continue;
                Color col = KindColor(c.Kind);
                col.a = act ? 0.9f : 0.25f;
                Gizmos.color = col;
                Gizmos.DrawWireCube(LocalPos(c.X, c.Y, c.Z) + Vector3.up * H * 0.5f, new Vector3(P, H, P) * 0.98f);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }

        public static Color KindColor(CellKind k)
        {
            switch (k)
            {
                case CellKind.Room: return new Color(0.35f, 0.8f, 1f);
                case CellKind.Deck: return new Color(0.4f, 1f, 0.5f);
                case CellKind.Frame: return new Color(1f, 0.75f, 0.25f);
                case CellKind.Columns: return new Color(1f, 0.45f, 0.3f);
                default: return new Color(0.8f, 0.8f, 0.8f);
            }
        }

        public static string KindName(CellKind k)
        {
            switch (k)
            {
                case CellKind.Room: return "Комната";
                case CellKind.Deck: return "Каркас+плита";
                case CellKind.Frame: return "Каркас";
                case CellKind.Columns: return "Колонны";
                default: return "Плита";
            }
        }

        public static string WallName(WallKind w)
        {
            switch (w)
            {
                case WallKind.Auto: return "Авто";
                case WallKind.None: return "Нет";
                case WallKind.Glass: return "Стекло";
                case WallKind.Brick: return "Кирпич";
                default: return "Глухая";
            }
        }
    }
}
