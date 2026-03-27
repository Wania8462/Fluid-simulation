using System;
using Unity.Mathematics;
using UnityEngine;

namespace Rendering
{
    public static class MeshGenerator
    {
        public static Mesh Rectangle(float width, float height)
        {
            Vector3[] verticies = new Vector3[]
            {
                // Top left
                new(-0.5f * width, 0.5f * height),
                // Top right
                new(0.5f * width, 0.5f * height),
                // Bottom left
                new(-0.5f * width, -0.5f * height),
                // Bottom right
                new(0.5f * width, -0.5f * height)
            };

            int[] triangles = new int[] { 2, 0, 1, 3, 2, 1 };
            Mesh mesh = new()
            {
                vertices = verticies,
                triangles = triangles
            };

            return mesh;
        }

        public static Mesh Line(float2 start, float2 end, float width)
        {
            float2 perpendicular = end - start;
            float magnitude = math.length(perpendicular);
            if (magnitude < Mathf.Epsilon) return null;
            perpendicular = new(-perpendicular.y, perpendicular.x);
            perpendicular /= Mathf.Sqrt(perpendicular.x * perpendicular.x + perpendicular.y * perpendicular.y);
            perpendicular *= width * 0.5f;

            Vector3[] verticies = new Vector3[]
            {
                new(start.x + perpendicular.x, start.y + perpendicular.y),
                new(start.x - perpendicular.x, start.y - perpendicular.y),
                new(end.x + perpendicular.x, end.y + perpendicular.y),
                new(end.x - perpendicular.x, end.y - perpendicular.y)
            };

            int[] triangles = new int[] { 1, 0, 2, 1, 2, 3 };
            Mesh mesh = new()
            {
                vertices = verticies,
                triangles = triangles
            };

            return mesh;
        }

        public static Mesh Circle(float radius, int resolution)
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

        public static Mesh[] MarchingSquareVariations()
        {
            // Directions
            const float Up = 0.5f;
            const float RightDir = 0.5f;
            const float Down = -0.5f;
            const float LeftDir = -0.5f;
            // Corners
            const int TL = 0;
            const int TR = 1;
            const int BL = 2;
            const int BR = 3;

            // Edge midpoints
            const int Top = 4;
            const int Right = 5;
            const int Bottom = 6;
            const int Left = 7;

            Vector3[] vertices =
            {
                // Vertices
                new(LeftDir, Up),  // top left
                new(RightDir, Up),  // top right
                new(LeftDir, Down),  // bottom left
                new(RightDir, Down),  // bottom right

                // Middle
                new(0, Up),  // top
                new(RightDir, 0),  // right
                new(0, Down),  // bottom
                new(LeftDir, 0)  // left
            };

            int[][] triangles = new int[][]
            {
                // Empty
                new int[] { },
                // Top-left corner
                new int[] { Left, TL, Top },
                // Top-right corner
                new int[] { Top, TR, Right },
                // Top edge (TL + TR)
                new int[] { Left, TL, TR,  Left, TR, Right },
                // Bottom-right corner
                new int[] { Right, BR, Bottom },
                // Saddle (TL + BR)
                new int[] { Left, TL, Top,  Right, BR, Bottom },
                // Right edge (TR + BR)
                new int[] { Top, TR, BR,  Top, BR, Bottom },
                // Filled except BL
                new int[] { Left, TL, TR,  Left, TR, BR,  Left, BR, Bottom },
                // Bottom-left corner
                new int[] { Bottom, BL, Left },
                // Left edge (TL + BL)
                new int[] { Top, Bottom, BL,  Top, BL, TL },
                // Saddle (TR + BL)
                new int[] { Top, TR, Right,  Bottom, BL, Left },
                // Filled except BR
                new int[] { TL, TR, Right,  TL, Right, Bottom,  TL, Bottom, BL },
                // Bottom edge (BL + BR)
                new int[] { Right, BR, BL,  Right, BL, Left },
                // Filled except TR
                new int[] { TL, Top, Right,  TL, Right, BR,  TL, BR, BL },
                // Filled except TL
                new int[] { Top, TR, BR,  Top, BR, BL,  Top, BL, Left },
                // Full
                new int[] { TL, TR, BR,  TL, BR, BL }
            };

            var meshes = new Mesh[16];

            for (int i = 0; i < triangles.Length; i++)
            {
                meshes[i] = new Mesh()
                {
                    vertices = vertices,
                    triangles = triangles[i]
                };
            }

            return meshes;
        }
    }
}