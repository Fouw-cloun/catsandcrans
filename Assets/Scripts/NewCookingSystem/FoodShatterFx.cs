using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// РАЗБИТЬ ВДРЕБЕЗГИ — эффект удара кувалдой по продукту. Только визуал.
    ///
    /// Два слоя, как просили:
    ///   1. ОСКОЛКИ — угловатые кусочки с плоскими гранями (как зёрна граната):
    ///      часть цвета кожуры, часть — мякоти. Разлетаются, отскакивают,
    ///      за полсекунды сжимаются в ничто.
    ///   2. БРЫЗГИ МЯКОТИ — мягкие капли «пюрешки»: летят, шлёпаются лепёшкой
    ///      и через пару секунд тают.
    /// Плюс облачко мелкой пыли-сока в точке удара.
    ///
    /// Меши осколков строятся один раз (6 вариантов), материал — CatCooks/Food Heap.
    /// </summary>
    public static class FoodShatterFx
    {
        private static Mesh[] _shards;
        private static Material _mat;

        public static Material SharedMaterial
        {
            get
            {
                if (_mat != null) return _mat;
                Shader sh = Shader.Find("CatCooks/Food Heap");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
                _mat = new Material(sh) { name = "CC Shatter (auto)", enableInstancing = true };
                if (_mat.HasProperty("_TexDetail")) _mat.SetFloat("_TexDetail", 0f);
                if (_mat.HasProperty("_OutlineWidth")) _mat.SetFloat("_OutlineWidth", 0.002f);
                return _mat;
            }
        }

        /// <param name="size">Размер продукта, м (от него — размер и разлёт осколков)</param>
        public static void Play(Vector3 centre, float size, Color skin, Color flesh,
                                int shards, int drops, int layer, Material material = null)
        {
            EnsureShards();
            Material mat = material != null ? material : SharedMaterial;
            MaterialPropertyBlock skinMpb = new MaterialPropertyBlock();
            skinMpb.SetColor("_BaseColor", skin);
            MaterialPropertyBlock fleshMpb = new MaterialPropertyBlock();
            fleshMpb.SetColor("_BaseColor", flesh);

            float speed = Mathf.Lerp(2.5f, 5f, Mathf.Clamp01(size));
            for (int i = 0; i < shards; i++)
            {
                GameObject go = new GameObject("Shard (fx)") { layer = layer };
                go.transform.position = centre + Random.insideUnitSphere * size * 0.3f;
                go.transform.rotation = Random.rotation;
                go.AddComponent<MeshFilter>().sharedMesh = _shards[Random.Range(0, _shards.Length)];
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.SetPropertyBlock(Random.value < 0.45f ? skinMpb : fleshMpb);

                Vector3 dir = (Random.insideUnitSphere + Vector3.up * 0.8f).normalized;
                float s = size * Random.Range(0.12f, 0.28f);
                go.AddComponent<FoodShardFx>().Init(dir * speed * Random.Range(0.6f, 1.2f),
                                                    Random.insideUnitSphere * 900f,
                                                    new Vector3(s, s * Random.Range(0.6f, 1f), s * Random.Range(0.7f, 1.3f)),
                                                    Random.Range(0.45f, 0.8f));
            }

            // Брызги мякоти: мягкие капли, шлёпаются и тают.
            Mesh sphere = FoodHeapRenderer.DropSphereMesh;
            for (int i = 0; i < drops; i++)
            {
                Vector3 dir = (Random.insideUnitSphere + Vector3.up * 0.6f).normalized;
                float r = size * Random.Range(0.06f, 0.14f);
                FoodDropFx.Spawn(centre + Random.insideUnitSphere * size * 0.2f,
                                 dir * speed * Random.Range(0.4f, 0.9f),
                                 sphere, mat, flesh, new Vector3(r, r * 0.85f, r) * 2f, true, layer);
            }

            // Облачко сока/пыли.
            // v17.5: облачка и ошмётки вместо квадратиков
            CCPowderFx.Impact(centre, Vector3.up, flesh, Mathf.Max(0.12f, size), 1.2f);
        }

        /// <summary>6 угловатых осколков: искажённый октаэдр/коробка с плоскими гранями.</summary>
        private static void EnsureShards()
        {
            if (_shards != null) return;
            _shards = new Mesh[6];
            System.Random rnd = new System.Random(4242);
            for (int k = 0; k < _shards.Length; k++) _shards[k] = BuildShard(rnd, k % 2 == 0);
        }

        private static Mesh BuildShard(System.Random rnd, bool octa)
        {
            // Опорные точки: октаэдр (6) или коробка (8) со случайным сдвигом — неровный камешек.
            Vector3[] p = octa
                ? new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back }
                : new[] { new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(-1,1,-1),
                          new Vector3(-1,-1,1), new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1) };
            for (int i = 0; i < p.Length; i++)
            {
                Vector3 j = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f,
                                        (float)rnd.NextDouble() - 0.5f) * 0.6f;
                p[i] = (p[i].normalized + j) * 0.5f;
            }
            int[] tris = octa
                ? new[] { 0,2,4, 4,2,1, 1,2,5, 5,2,0, 0,4,3, 4,1,3, 1,5,3, 5,0,3 }
                : new[] { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7 };

            // Плоские грани: у каждого треугольника свои вершины и нормаль.
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
            Vector3 centre = Vector3.zero;
            foreach (Vector3 q in p) centre += q;
            centre /= p.Length;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = p[tris[i]], b = p[tris[i + 1]], d = p[tris[i + 2]];
                Vector3 fn = Vector3.Cross(b - a, d - a).normalized;
                // Грань обязана смотреть наружу — проверяем по центру.
                if (Vector3.Dot(fn, (a + b + d) / 3f - centre) < 0f) { Vector3 tmp = b; b = d; d = tmp; fn = -fn; }
                int s0 = v.Count;
                v.Add(a); v.Add(b); v.Add(d);
                n.Add(fn); n.Add(fn); n.Add(fn);
                Color32 w = new Color32(255, 255, 255, 0);
                c.Add(w); c.Add(w); c.Add(w);
                t.Add(s0); t.Add(s0 + 1); t.Add(s0 + 2);
            }
            Mesh m = new Mesh { name = "Shard", hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
