using System;
using UnityEngine;

namespace Rendering
{
    public static class MeshGenerator
    {
        public static Mesh Sphere(float radius, int resolution)
        {
            Vector3[] verticies = new Vector3[4 * resolution + 1];
            int[] triangles = new int[resolution * 12];

            verticies[0] = new(0, 0);

            for (int i = 1; i < verticies.Length; i++)
            {
                verticies[i] = new((float)(radius * Math.Cos(Math.PI * (i - 1) / resolution / 2)),
                                   (float)(radius * Math.Sin(Math.PI * (i - 1) / resolution / 2)),
                                   0);
            }

            int v = 1;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                triangles[i] = 0;
                triangles[i + 1] = v + 1;
                triangles[i + 2] = v;
                v += 1;
            }

            triangles[^2] = 1;

            Mesh mesh = new()
            {
                vertices = verticies,
                triangles = triangles
            };

            return mesh;
        }
    }
}