using System.Collections.Generic;
using Peribind.Domain.Board;
using UnityEngine;

namespace Peribind.Unity.Board
{
    public static class PieceMeshBuilder
    {
        public static Mesh BuildMesh(IReadOnlyList<Cell> cells, float cellSize, float height, out Cell minCell)
        {
            var mesh = new Mesh
            {
                name = "PieceMesh"
            };
            BuildMesh(cells, cellSize, height, mesh, out minCell);
            return mesh;
        }

        public static void BuildMesh(IReadOnlyList<Cell> cells, float cellSize, float height, Mesh mesh, out Cell minCell)
        {
            if (cells == null || cells.Count == 0)
            {
                minCell = new Cell(0, 0);
                mesh.Clear();
                return;
            }

            var occupied = new HashSet<Cell>(cells);
            minCell = GetMinCell(cells);
            var maxCell = GetMaxCell(cells);
            var pieceWidth = (maxCell.X - minCell.X + 1) * cellSize;
            var pieceDepth = (maxCell.Y - minCell.Y + 1) * cellSize;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            foreach (var cell in cells)
            {
                var localCell = new Cell(cell.X - minCell.X, cell.Y - minCell.Y);
                var basePos = new Vector3(localCell.X * cellSize, 0f, localCell.Y * cellSize);
                var topPos = basePos + new Vector3(0f, height, 0f);

                AddFacesForCell(
                    occupied,
                    cell,
                    basePos,
                    topPos,
                    cellSize,
                    height,
                    pieceWidth,
                    pieceDepth,
                    vertices,
                    normals,
                    uvs,
                    triangles);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private static void AddFacesForCell(
            HashSet<Cell> occupied,
            Cell cell,
            Vector3 basePos,
            Vector3 topPos,
            float cellSize,
            float height,
            float pieceWidth,
            float pieceDepth,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles)
        {
            var x = basePos.x;
            var z = basePos.z;
            var y0 = basePos.y;
            var y1 = topPos.y;
            var x1 = x + cellSize;
            var z1 = z + cellSize;

            if (!occupied.Contains(new Cell(cell.X + 1, cell.Y)))
            {
                AddQuad(
                    new Vector3(x1, y0, z),
                    new Vector3(x1, y0, z1),
                    new Vector3(x1, y1, z1),
                    new Vector3(x1, y1, z),
                    Vector3.right,
                    vertices, normals, uvs, triangles,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f));
            }

            if (!occupied.Contains(new Cell(cell.X - 1, cell.Y)))
            {
                AddQuad(
                    new Vector3(x, y0, z1),
                    new Vector3(x, y0, z),
                    new Vector3(x, y1, z),
                    new Vector3(x, y1, z1),
                    Vector3.left,
                    vertices, normals, uvs, triangles,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f));
            }

            if (!occupied.Contains(new Cell(cell.X, cell.Y + 1)))
            {
                AddQuad(
                    new Vector3(x1, y0, z1),
                    new Vector3(x, y0, z1),
                    new Vector3(x, y1, z1),
                    new Vector3(x1, y1, z1),
                    Vector3.forward,
                    vertices, normals, uvs, triangles,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f));
            }

            if (!occupied.Contains(new Cell(cell.X, cell.Y - 1)))
            {
                AddQuad(
                    new Vector3(x, y0, z),
                    new Vector3(x1, y0, z),
                    new Vector3(x1, y1, z),
                    new Vector3(x, y1, z),
                    Vector3.back,
                    vertices, normals, uvs, triangles,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f));
            }

            var leftOpen = !occupied.Contains(new Cell(cell.X - 1, cell.Y));
            var rightOpen = !occupied.Contains(new Cell(cell.X + 1, cell.Y));
            var downOpen = !occupied.Contains(new Cell(cell.X, cell.Y - 1));
            var upOpen = !occupied.Contains(new Cell(cell.X, cell.Y + 1));

            var uLeft = leftOpen ? 0f : 0.5f;
            var uRight = rightOpen ? 1f : 0.5f;
            var vBottom = downOpen ? 0f : 0.5f;
            var vTop = upOpen ? 1f : 0.5f;

            AddQuad(
                new Vector3(x, y1, z),
                new Vector3(x1, y1, z),
                new Vector3(x1, y1, z1),
                new Vector3(x, y1, z1),
                Vector3.up,
                vertices,
                normals,
                uvs,
                triangles,
                new Vector2(uLeft, vBottom),
                new Vector2(uRight, vBottom),
                new Vector2(uRight, vTop),
                new Vector2(uLeft, vTop));

            AddQuad(
                new Vector3(x, y0, z1),
                new Vector3(x1, y0, z1),
                new Vector3(x1, y0, z),
                new Vector3(x, y0, z),
                Vector3.down,
                vertices,
                normals,
                uvs,
                triangles,
                new Vector2(x / pieceWidth, z1 / pieceDepth),
                new Vector2(x1 / pieceWidth, z1 / pieceDepth),
                new Vector2(x1 / pieceWidth, z / pieceDepth),
                new Vector2(x / pieceWidth, z / pieceDepth));
        }

        private static void AddQuad(
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 v3,
            Vector3 normal,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            Vector2? uv0 = null,
            Vector2? uv1 = null,
            Vector2? uv2 = null,
            Vector2? uv3 = null)
        {
            var index = vertices.Count;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);

            uvs.Add(uv0 ?? new Vector2(0f, 0f));
            uvs.Add(uv1 ?? new Vector2(1f, 0f));
            uvs.Add(uv2 ?? new Vector2(1f, 1f));
            uvs.Add(uv3 ?? new Vector2(0f, 1f));

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

        private static Cell GetMaxCell(IReadOnlyList<Cell> cells)
        {
            var maxX = cells[0].X;
            var maxY = cells[0].Y;
            for (var i = 1; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (cell.X > maxX)
                {
                    maxX = cell.X;
                }

                if (cell.Y > maxY)
                {
                    maxY = cell.Y;
                }
            }

            return new Cell(maxX, maxY);
        }
    }
}
