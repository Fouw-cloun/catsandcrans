using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Floors.Editor
{
    // Geometry authoring only; no runtime mesh-building component.
    public sealed class CCSiteMesh
    {
        readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> triangles = new List<int>();
        public int TriangleCount => triangles.Count / 3;
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect rect)
        {
            int start = vertices.Count;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            uv.Add(new Vector2(rect.xMin, rect.yMin)); uv.Add(new Vector2(rect.xMax, rect.yMin));
            uv.Add(new Vector2(rect.xMax, rect.yMax)); uv.Add(new Vector2(rect.xMin, rect.yMax));
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }
        public void Box(Vector3 center, Vector3 size, Rect rect)
        {
            Vector3 a = center - size * .5f, b = center + size * .5f;
            Quad(new Vector3(b.x,a.y,a.z), new Vector3(a.x,a.y,a.z), new Vector3(a.x,b.y,a.z), new Vector3(b.x,b.y,a.z), rect);
            Quad(new Vector3(a.x,a.y,b.z), new Vector3(b.x,a.y,b.z), new Vector3(b.x,b.y,b.z), new Vector3(a.x,b.y,b.z), rect);
            Quad(new Vector3(a.x,a.y,a.z), new Vector3(a.x,a.y,b.z), new Vector3(a.x,b.y,b.z), new Vector3(a.x,b.y,a.z), rect);
            Quad(new Vector3(b.x,a.y,b.z), new Vector3(b.x,a.y,a.z), new Vector3(b.x,b.y,a.z), new Vector3(b.x,b.y,b.z), rect);
            Quad(new Vector3(a.x,a.y,a.z), new Vector3(b.x,a.y,a.z), new Vector3(b.x,a.y,b.z), new Vector3(a.x,a.y,b.z), rect);
            Quad(new Vector3(a.x,b.y,b.z), new Vector3(b.x,b.y,b.z), new Vector3(b.x,b.y,a.z), new Vector3(a.x,b.y,a.z), rect);
        }
        public MeshRenderer Save(string name, Transform parent, Material material, CCFloorBake bake, int layer)
        {
            if (vertices.Count == 0) return null;
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            AssetDatabase.AddObjectToAsset(mesh, bake);
            var go = new GameObject(name); go.layer = layer; go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }
    }
}
