using Unity.Burst;
using UnityEngine;

enum Verticies
{
    TopLeftFront = 0,
    TopRightFront = 1,
    BottomRightFront = 2,
    BottomLeftFront = 3,
    TopLeftBack = 4,
    TopRightBack = 5,
    BottomRightBack = 6,
    BottomLeftBack = 7
}

[BurstCompile]
public static class MeshGenerator
{
    // Returns a cube mesh
    public static Mesh Cube(float size = 1)
    {
        float half = size / 2;
        Vector3[] verticies = new Vector3[8];
        Vector2[] uv = new Vector2[8];

        verticies[(int)Verticies.BottomLeftFront] = new Vector3(-half, -half, -half);
        verticies[(int)Verticies.BottomRightFront] = new Vector3(half, -half, -half);
        verticies[(int)Verticies.TopRightFront] = new Vector3(half, half, -half);
        verticies[(int)Verticies.TopLeftFront] = new Vector3(-half, half, -half);
        verticies[(int)Verticies.BottomLeftBack] = new Vector3(-half, -half, half);
        verticies[(int)Verticies.BottomRightBack] = new Vector3(half, -half, half);
        verticies[(int)Verticies.TopRightBack] = new Vector3(half, half, half);
        verticies[(int)Verticies.TopLeftBack] = new Vector3(-half, half, half);

        uv[(int)Verticies.BottomLeftFront] = new Vector3(-half, -half, -half);
        uv[(int)Verticies.BottomRightFront] = new Vector3(half, -half, -half);
        uv[(int)Verticies.TopRightFront] = new Vector3(half, half, -half);
        uv[(int)Verticies.TopLeftFront] = new Vector3(-half, half, -half);
        uv[(int)Verticies.BottomLeftBack] = new Vector3(-half, -half, half);
        uv[(int)Verticies.BottomRightBack] = new Vector3(half, -half, half);
        uv[(int)Verticies.TopRightBack] = new Vector3(half, half, half);
        uv[(int)Verticies.TopLeftBack] = new Vector3(-half, half, half);

        int[] triangles = new int[]
        {
            // Back
            0, 1, 2,  0, 2, 3,
            // Front
            4, 6, 5,  4, 7, 6,
            // Left
            0, 3, 7,  0, 7, 4,
            // Right
            1, 5, 6,  1, 6, 2,
            // Top
            3, 2, 6,  3, 6, 7,
            // Bottom
            0, 4, 5,  0, 5, 1
        };

        Mesh mesh = new()
        {
            vertices = verticies,
            uv = uv,
            triangles = triangles
        };

        return mesh;
    }

    public static Mesh Sphere(float size, int latitude, int longitude)
    {
        float radius = size / 2;
        Vector3[] vertices = new Vector3[(latitude + 1) * (longitude + 1)];
        int[] triangles = new int[latitude * longitude * 6];

        int v = 0;
        for (int lat = 0; lat <= latitude; lat++)
        {
            float a1 = Mathf.PI * lat / latitude;
            float sin1 = Mathf.Sin(a1);
            float cos1 = Mathf.Cos(a1);

            for (int lon = 0; lon <= longitude; lon++)
            {
                float a2 = 2 * Mathf.PI * lon / longitude;
                float sin2 = Mathf.Sin(a2);
                float cos2 = Mathf.Cos(a2);

                vertices[v++] = new Vector3(
                    sin1 * cos2,
                    cos1,
                    sin1 * sin2
                ) * radius;
            }
        }

        int t = 0;
        for (int lat = 0; lat < latitude; lat++)
        {
            for (int lon = 0; lon < longitude; lon++)
            {
                int current = lat * (longitude + 1) + lon;
                int next = current + longitude + 1;

                triangles[t++] = current;
                triangles[t++] = current + 1;
                triangles[t++] = next;

                triangles[t++] = current + 1;
                triangles[t++] = next + 1;
                triangles[t++] = next;
            }
        }

        Mesh mesh = new()
        {
            vertices = vertices,
            triangles = triangles
        };

        mesh.RecalculateNormals();
        return mesh;
    }
}