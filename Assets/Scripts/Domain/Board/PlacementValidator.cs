using System.Collections.Generic;
using Peribind.Domain.Pieces;

namespace Peribind.Domain.Board
{
    public static class PlacementValidator
    {
        public static PlacementResult Validate(BoardState board, Placement placement)
        {
            var absoluteCells = new List<Cell>();

            foreach (var offset in placement.Piece.GetCells(placement.Rotation))
            {
                var cell = placement.Origin + offset;
                if (!board.Size.IsInBounds(cell))
                {
                    return new PlacementResult(false, PlacementFailureReason.OutOfBounds, absoluteCells);
                }

                if (board.IsOccupied(cell))
                {
                    return new PlacementResult(false, PlacementFailureReason.Overlap, absoluteCells);
                }

                absoluteCells.Add(cell);
            }

            return new PlacementResult(true, PlacementFailureReason.None, absoluteCells);
        }
    }
}
