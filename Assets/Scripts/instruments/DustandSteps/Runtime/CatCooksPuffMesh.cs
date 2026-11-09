using System.Collections.Generic;
using UnityEngine;

// A single closed surface with broad lobes, rather than intersecting transparent spheres.
public static class CatCooksPuffMesh
{
    public sealed class Template
    {
        public Vector3[] vertices, normals;
        public Vector2[] uv;
        public int[] triangles;
    }
    public static Template Build(int variant = 0, int rings = 10, int sides = 16)
    {
        rings = Mathf.Clamp(rings, 4, 24); sides = Mathf.Clamp(sides, 6, 32);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
        float phase = variant * 1.71f;
        Vector3[] centres = { new Vector3(-.19f,-.08f,0), new Vector3(.18f,-.04f,.03f),
            new Vector3(-.01f,.19f,-.03f), new Vector3(.015f,-.005f,-.17f), new Vector3(0,-.05f,.17f) };
        float[] radii = { .30f,.31f,.29f,.29f,.27f };
        for(int i=0;i<centres.Length;i++)
        {
            centres[i] += new Vector3(Mathf.Sin(phase+i)*.018f,Mathf.Cos(phase+i*2)*.018f,Mathf.Sin(phase-i)*.015f);
            radii[i] += Mathf.Cos(phase+i)*.015f;
        }
        for (int y = 0; y <= rings; y++)
        {
            float t = y / (float)rings, theta = t * Mathf.PI;
            for (int j = 0; j <= sides; j++)
            {
                float a = j / (float)sides * Mathf.PI * 2;
                float sin = y == 0 || y == rings ? 0 : Mathf.Sin(theta);
                Vector3 direction = new Vector3(Mathf.Cos(a) * sin, -Mathf.Cos(theta), Mathf.Sin(a) * sin);
                // All balls contain the origin, so their union is star-shaped:
                // each ray has one outer surface. Smooth the joins into rounded lobes.
                float radius=0;
                for(int k=0;k<centres.Length;k++)
                {
                    float along=Vector3.Dot(direction,centres[k]);
                    float end=along+Mathf.Sqrt(Mathf.Max(0,radii[k]*radii[k]-centres[k].sqrMagnitude+along*along));
                    float blend=Mathf.Clamp01(.5f+.5f*(radius-end)/.075f);
                    radius=Mathf.Lerp(end,radius,blend)+.075f*blend*(1-blend);
                }
                v.Add(direction * radius); uv.Add(new Vector2(j / (float)sides, t));
            }
        }
        for (int y = 0; y < rings; y++) for (int j = 0; j < sides; j++)
        {
            int a = y * (sides + 1) + j, b = a + sides + 1;
            if (y > 0) { indices.Add(a); indices.Add(b); indices.Add(a + 1); }
            if (y < rings - 1) { indices.Add(a + 1); indices.Add(b); indices.Add(b + 1); }
        }
        var normals = new Vector3[v.Count];
        for (int i = 0; i < indices.Count; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            normals[a] += n; normals[b] += n; normals[c] += n;
        }
        for (int y = 0; y <= rings; y++)
        {
            int a = y * (sides + 1), b = a + sides;
            if (y == 0 || y == rings)
            {
                Vector3 pole=Vector3.zero;
                for(int j=0;j<=sides;j++) pole+=normals[a+j];
                for(int j=0;j<=sides;j++) normals[a+j]=pole;
            }
            else
            { Vector3 seam = normals[a] + normals[b]; normals[a] = normals[b] = seam; }
        }
        for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;
        return new Template { vertices = v.ToArray(), normals = normals, uv = uv.ToArray(), triangles = indices.ToArray() };
    }
}
