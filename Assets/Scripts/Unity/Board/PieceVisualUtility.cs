using System.Collections.Generic;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;
using Peribind.Unity.ScriptableObjects;
using UnityEngine;

namespace Peribind.Unity.Board
{
    public static class PieceVisualUtility
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static Quaternion GetWorldRotation(Rotation rotation)
        {
            return Quaternion.Euler(0f, -90f * (int)rotation, 0f);
        }

        public static Vector3 GetFootprintWorldCenter(GridMapper gridMapper, IReadOnlyList<Cell> cells, float yOffset = 0f)
        {
            var minCell = GetMinCell(cells);
            var maxCell = GetMaxCell(cells);
            return GetFootprintWorldCenter(gridMapper, minCell, maxCell, yOffset);
        }

        public static Vector3 GetFootprintWorldCenter(GridMapper gridMapper, Cell minCell, Cell maxCell, float yOffset = 0f)
        {
            if (gridMapper == null)
            {
                return Vector3.zero;
            }

            var localX = (minCell.X + maxCell.X + 1) * 0.5f * gridMapper.CellSize;
            var localZ = (minCell.Y + maxCell.Y + 1) * 0.5f * gridMapper.CellSize;
            var localPosition = new Vector3(localX, yOffset, localZ);
            return gridMapper.Origin != null ? gridMapper.Origin.TransformPoint(localPosition) : localPosition;
        }

        public static Cell GetMinCell(IReadOnlyList<Cell> cells)
        {
            if (cells == null || cells.Count == 0)
            {
                return new Cell(0, 0);
            }

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

        public static Cell GetMaxCell(IReadOnlyList<Cell> cells)
        {
            if (cells == null || cells.Count == 0)
            {
                return new Cell(0, 0);
            }

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

        public static bool TryResolvePlacement(PieceDefinitionSO pieceAsset, IReadOnlyList<Cell> absoluteCells, out Rotation rotation, out Cell origin)
        {
            rotation = Rotation.Deg0;
            origin = new Cell(0, 0);

            if (pieceAsset == null || absoluteCells == null || absoluteCells.Count == 0)
            {
                return false;
            }

            var absoluteMin = GetMinCell(absoluteCells);
            var normalizedAbsoluteCells = Normalize(absoluteCells, absoluteMin);
            var piece = pieceAsset.ToDomainDefinition();

            foreach (Rotation candidate in System.Enum.GetValues(typeof(Rotation)))
            {
                var rotatedCells = new List<Cell>();
                foreach (var cell in piece.GetCells(candidate))
                {
                    rotatedCells.Add(cell);
                }

                var rotatedMin = GetMinCell(rotatedCells);
                if (!MatchNormalized(normalizedAbsoluteCells, Normalize(rotatedCells, rotatedMin)))
                {
                    continue;
                }

                rotation = candidate;
                origin = new Cell(absoluteMin.X - rotatedMin.X, absoluteMin.Y - rotatedMin.Y);
                return true;
            }

            return false;
        }

        public static void ApplyColorToHierarchy(GameObject root, MaterialPropertyBlock propertyBlock, Color color)
        {
            if (root == null || propertyBlock == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var propertyId = GetColorPropertyId(renderer);
                propertyBlock.Clear();
                propertyBlock.SetColor(propertyId, color);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private static List<Cell> Normalize(IReadOnlyList<Cell> cells, Cell minCell)
        {
            var normalized = new List<Cell>(cells.Count);
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                normalized.Add(new Cell(cell.X - minCell.X, cell.Y - minCell.Y));
            }

            return normalized;
        }

        private static bool MatchNormalized(IReadOnlyList<Cell> a, IReadOnlyList<Cell> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            var cells = new HashSet<Cell>(a);
            for (var i = 0; i < b.Count; i++)
            {
                if (!cells.Contains(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int GetColorPropertyId(Renderer renderer)
        {
            var material = renderer.sharedMaterial;
            if (material != null && material.HasProperty(BaseColorId))
            {
                return BaseColorId;
            }

            return ColorId;
        }
    }
}
