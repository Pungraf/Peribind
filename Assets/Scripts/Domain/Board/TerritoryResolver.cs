using System.Collections.Generic;

namespace Peribind.Domain.Board
{
    public readonly struct TerritoryRegion
    {
        public List<Cell> Cells { get; }
        public HashSet<int> OpponentPieceInstanceIds { get; }
        public bool ContainsNeutral { get; }
        public bool HasOwnerBorder { get; }
        public bool IsBoundaryClosed { get; }
        public bool TouchesBoardEdge { get; }

        public TerritoryRegion(
            List<Cell> cells,
            HashSet<int> opponentPieceInstanceIds,
            bool containsNeutral,
            bool hasOwnerBorder,
            bool isBoundaryClosed,
            bool touchesBoardEdge)
        {
            Cells = cells;
            OpponentPieceInstanceIds = opponentPieceInstanceIds;
            ContainsNeutral = containsNeutral;
            HasOwnerBorder = hasOwnerBorder;
            IsBoundaryClosed = isBoundaryClosed;
            TouchesBoardEdge = touchesBoardEdge;
        }
    }

    public static class TerritoryResolver
    {
        public static List<TerritoryRegion> GetRegions(BoardState board, int ownerId, int neutralPlayerId)
        {
            var regions = new List<TerritoryRegion>();
            var size = board.Size;
            var visited = new bool[size.Width, size.Height];

            for (var x = 0; x < size.Width; x++)
            {
                for (var y = 0; y < size.Height; y++)
                {
                    var cell = new Cell(x, y);
                    if (visited[x, y])
                    {
                        continue;
                    }

                    var occupant = board.GetOccupant(cell);
                    if (occupant.HasValue && occupant.Value.PlayerId == ownerId)
                    {
                        continue;
                    }

                    var regionCells = new List<Cell>();
                    var opponentPieces = new HashSet<int>();
                    var containsNeutral = false;
                    FloodFill(board, cell, ownerId, neutralPlayerId, visited, regionCells, opponentPieces, ref containsNeutral);
                    var boundaryInfo = GetBoundaryInfo(board, regionCells, ownerId);

                    regions.Add(new TerritoryRegion(
                        regionCells,
                        opponentPieces,
                        containsNeutral,
                        boundaryInfo.hasOwnerBorder,
                        boundaryInfo.isBoundaryClosed,
                        boundaryInfo.touchesBoardEdge));
                }
            }

            return regions;
        }

        private static void FloodFill(
            BoardState board,
            Cell start,
            int ownerId,
            int neutralPlayerId,
            bool[,] visited,
            List<Cell> region,
            HashSet<int> opponentPieceInstanceIds,
            ref bool containsNeutral)
        {
            var size = board.Size;
            var queue = new Queue<Cell>();
            queue.Enqueue(start);
            visited[start.X, start.Y] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                region.Add(current);

                RecordOccupant(board, current, ownerId, neutralPlayerId, opponentPieceInstanceIds, ref containsNeutral);

                VisitNeighbor(board, new Cell(current.X + 1, current.Y), ownerId, visited, queue);
                VisitNeighbor(board, new Cell(current.X - 1, current.Y), ownerId, visited, queue);
                VisitNeighbor(board, new Cell(current.X, current.Y + 1), ownerId, visited, queue);
                VisitNeighbor(board, new Cell(current.X, current.Y - 1), ownerId, visited, queue);
                VisitNeighbor(board, new Cell(current.X + 1, current.Y + 1), ownerId, visited, queue);
                VisitNeighbor(board, new Cell(current.X - 1, current.Y + 1), ownerId, visited, queue);
                VisitNeighbor(board, new Cell(current.X + 1, current.Y - 1), ownerId, visited, queue);
                VisitNeighbor(board, new Cell(current.X - 1, current.Y - 1), ownerId, visited, queue);
            }
        }

        private static void VisitNeighbor(
            BoardState board,
            Cell neighbor,
            int ownerId,
            bool[,] visited,
            Queue<Cell> queue)
        {
            var size = board.Size;
            if (neighbor.X < 0 || neighbor.Y < 0 || neighbor.X >= size.Width || neighbor.Y >= size.Height)
            {
                return;
            }

            if (visited[neighbor.X, neighbor.Y])
            {
                return;
            }

            var occupant = board.GetOccupant(neighbor);
            if (occupant.HasValue && occupant.Value.PlayerId == ownerId)
            {
                return;
            }

            visited[neighbor.X, neighbor.Y] = true;
            queue.Enqueue(neighbor);
        }

        private static void RecordOccupant(
            BoardState board,
            Cell cell,
            int ownerId,
            int neutralPlayerId,
            HashSet<int> opponentPieceInstanceIds,
            ref bool containsNeutral)
        {
            var occupant = board.GetOccupant(cell);
            if (!occupant.HasValue)
            {
                return;
            }

            var data = occupant.Value;
            if (data.PlayerId == neutralPlayerId)
            {
                containsNeutral = true;
                return;
            }

            if (data.PlayerId != ownerId)
            {
                opponentPieceInstanceIds.Add(data.PieceInstanceId);
            }
        }

        private static (bool hasOwnerBorder, bool isBoundaryClosed, bool touchesBoardEdge) GetBoundaryInfo(
            BoardState board,
            List<Cell> regionCells,
            int ownerId)
        {
            var degrees = new Dictionary<Cell, int>();
            var size = board.Size;
            var hasOwnerBorder = false;
            var touchesBoardEdge = false;

            foreach (var cell in regionCells)
            {
                AddEdgeIfBoundary(board, cell, new Cell(cell.X + 1, cell.Y), ownerId, size, degrees, ref hasOwnerBorder, ref touchesBoardEdge);
                AddEdgeIfBoundary(board, cell, new Cell(cell.X - 1, cell.Y), ownerId, size, degrees, ref hasOwnerBorder, ref touchesBoardEdge);
                AddEdgeIfBoundary(board, cell, new Cell(cell.X, cell.Y + 1), ownerId, size, degrees, ref hasOwnerBorder, ref touchesBoardEdge);
                AddEdgeIfBoundary(board, cell, new Cell(cell.X, cell.Y - 1), ownerId, size, degrees, ref hasOwnerBorder, ref touchesBoardEdge);
            }

            if (!hasOwnerBorder || degrees.Count == 0)
            {
                return (hasOwnerBorder, false, touchesBoardEdge);
            }

            foreach (var degree in degrees.Values)
            {
                if (degree != 2)
                {
                    return (hasOwnerBorder, false, touchesBoardEdge);
                }
            }

            return (hasOwnerBorder, true, touchesBoardEdge);
        }

        private static void AddEdgeIfBoundary(
            BoardState board,
            Cell from,
            Cell neighbor,
            int ownerId,
            BoardSize size,
            Dictionary<Cell, int> degrees,
            ref bool hasOwnerBorder,
            ref bool touchesBoardEdge)
        {
            var isOutOfBounds = neighbor.X < 0 || neighbor.Y < 0 || neighbor.X >= size.Width || neighbor.Y >= size.Height;
            var isOwner = false;

            if (!isOutOfBounds)
            {
                var occupant = board.GetOccupant(neighbor);
                isOwner = occupant.HasValue && occupant.Value.PlayerId == ownerId;
            }

            if (!isOutOfBounds && !isOwner)
            {
                return;
            }

            if (isOwner)
            {
                hasOwnerBorder = true;
            }
            else if (isOutOfBounds)
            {
                touchesBoardEdge = true;
            }

            var edge = GetEdgeVertices(from, neighbor);
            IncrementDegree(degrees, edge.a);
            IncrementDegree(degrees, edge.b);
        }

        private static (Cell a, Cell b) GetEdgeVertices(Cell from, Cell neighbor)
        {
            if (neighbor.X == from.X + 1 && neighbor.Y == from.Y)
            {
                return (new Cell(from.X + 1, from.Y), new Cell(from.X + 1, from.Y + 1));
            }

            if (neighbor.X == from.X - 1 && neighbor.Y == from.Y)
            {
                return (new Cell(from.X, from.Y), new Cell(from.X, from.Y + 1));
            }

            if (neighbor.X == from.X && neighbor.Y == from.Y + 1)
            {
                return (new Cell(from.X, from.Y + 1), new Cell(from.X + 1, from.Y + 1));
            }

            return (new Cell(from.X, from.Y), new Cell(from.X + 1, from.Y));
        }

        private static void IncrementDegree(Dictionary<Cell, int> degrees, Cell vertex)
        {
            if (degrees.TryGetValue(vertex, out var value))
            {
                degrees[vertex] = value + 1;
                return;
            }

            degrees[vertex] = 1;
        }
    }
}
