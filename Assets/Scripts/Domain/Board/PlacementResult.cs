using System.Collections.Generic;

namespace Peribind.Domain.Board
{
    public enum PlacementFailureReason
    {
        None = 0,
        OutOfBounds = 1,
        Overlap = 2,
        InvalidPieceForPhase = 3,
        NoRemainingPieces = 4,
        InOpponentTerritory = 5
    }

    public readonly struct PlacementResult
    {
        public bool IsValid { get; }
        public PlacementFailureReason FailureReason { get; }
        public IReadOnlyList<Cell> AbsoluteCells { get; }

        public PlacementResult(bool isValid, PlacementFailureReason failureReason, IReadOnlyList<Cell> absoluteCells)
        {
            IsValid = isValid;
            FailureReason = failureReason;
            AbsoluteCells = absoluteCells;
        }
    }
}
