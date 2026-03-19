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
                new int[] { Top, TL, Left },
                // Top-right corner
                new int[] { TR, Top, Right },
                // Bottom-right corner
                new int[] { Bottom, BR, Right },
                // Bottom-left corner
                new int[] { BL, Bottom, Left },

                // Saddle (TL + BR)
                new int[] { Top, TL, Left,  Bottom, BR, Right },
                // Saddle (TR + BL)
                new int[] { TR, Top, Right,  BL, Bottom, Left },

                // Top edge (TL + TR)
                new int[] { TL, Right, TR,  TL, Left, Right },
                // Right edge (TR + BR)
                new int[] { TR, Bottom, BR,  TR, Top, Bottom },
                // Left edge (TL + BL)
                new int[] { TL, Bottom, Top,  TL, BL, Bottom },
                // Bottom edge (BL + BR)
                new int[] { BL, Bottom, Right,  BL, BR, Right },

                // Filled except BL
                new int[] { TL, BR, TR,  TL, Bottom, BR,  TL, Left, Bottom },
                // Filled except BR
                new int[] { TL, Right, TR,  TL, Bottom, Right,  TL, BL, Bottom },
                // Filled except TR
                new int[] { TL, Right, Top,  TL, BR, Right,  TL, BL, BR },
                // Filled except TL
                new int[] { TR, BL, BR,  TR, Left, BL,  TR, Top, Left },

                // Full
                new int[] { TL, BR, TR,  TL, BL, BR },
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