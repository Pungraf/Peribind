using System.Collections.Generic;
using Peribind.Domain.Board;
using UnityEngine;

namespace Peribind.Unity.Board
{
    public static class PieceOutlineBuilder
    {
        public static void BuildOutlineMesh(IReadOnlyList<Cell> cells, float cellSize, float height, float width, float yOffset, Mesh mesh, out Cell minCell)
        {
            if (cells == null || cells.Count == 0)
            {
                minCell = new Cell(0, 0);
                mesh.Clear();
                return;
            }

            var occupied = new HashSet<Cell>(cells);
            minCell = GetMinCell(cells);
            var stripeWidth = Mathf.Clamp(width, 0.001f, cellSize * 0.5f);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            foreach (var cell in cells)
            {
                var localCell = new Cell(cell.X - minCell.X, cell.Y - minCell.Y);
                var x0 = localCell.X * cellSize;
                var z0 = localCell.Y * cellSize;
                var x1 = x0 + cellSize;
                var z1 = z0 + cellSize;
                var y = height + yOffset;
                var xInset0 = x0 + stripeWidth;
                var xInset1 = x1 - stripeWidth;
                var zInset0 = z0 + stripeWidth;
                var zInset1 = z1 - stripeWidth;

                if (!occupied.Contains(new Cell(cell.X - 1, cell.Y)))
                {
                    AddTopQuad(x0, z0, xInset0, z1, y, vertices, normals, uvs, triangles);
                }

                if (!occupied.Contains(new Cell(cell.X + 1, cell.Y)))
                {
                    AddTopQuad(xInset1, z0, x1, z1, y, vertices, normals, uvs, triangles);
                }

                if (!occupied.Contains(new Cell(cell.X, cell.Y - 1)))
                {
                    AddTopQuad(x0, z0, x1, zInset0, y, vertices, normals, uvs, triangles);
                }

                if (!occupied.Contains(new Cell(cell.X, cell.Y + 1)))
                {
                    AddTopQuad(x0, zInset1, x1, z1, y, vertices, normals, uvs, triangles);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private static void AddTopQuad(
            float x0,
            float z0,
            float x1,
            float z1,
            float y,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles)
        {
            var index = vertices.Count;
            vertices.Add(new Vector3(x0, y, z0));
            vertices.Add(new Vector3(x1, y, z0));
            vertices.Add(new Vector3(x1, y, z1));
            vertices.Add(new Vector3(x0, y, z1));

            normals.Add(Vector3.up);
            normals.Add(Vector3.up);
            normals.Add(Vector3.up);
            normals.Add(Vector3.up);

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));

            triangles.Add(index + 0);
            triangles.Add(index + 2);
            triangles.Add(index + 1);
            triangles.Add(index + 0);
            triangles.Add(index + 3);
            triangles.Add(index + 2);
        }

        private static Cell GetMinCell(IReadOnlyList<Cell> cells)
        {
            var minX = cells[0].X;
            var minY = cells[0].Y;
            for (var i = 1; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (cell.X < minX)
                {
                    minX = cell.X;
                }

                if (cell.Y < minY)
                {
                    minY = cell.Y;
                }
            }

            return new Cell(minX, minY);
        }
    }
}
