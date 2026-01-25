using NUnit.Framework;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;

namespace Peribind.Tests.EditMode.Domain
{
    public class PlacementValidatorTests
    {
        [Test]
        public void Validate_ReturnsOutOfBounds_WhenAnyCellOutsideBoard()
        {
            var board = new BoardState(new BoardSize(10, 10));
            var piece = new PieceDefinition("Test", new[] { new Cell(0, 0), new Cell(1, 0) });
            var placement = new Placement(piece, new Cell(9, 9), Rotation.Deg0, 0);

            var result = PlacementValidator.Validate(board, placement);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(PlacementFailureReason.OutOfBounds, result.FailureReason);
        }

        [Test]
        public void Validate_ReturnsOverlap_WhenAnyCellOccupied()
        {
            var board = new BoardState(new BoardSize(10, 10));
            board.SetOccupant(new Cell(4, 4), new CellOccupant(0, 1, "Test"));
            var piece = new PieceDefinition("Test", new[] { new Cell(0, 0), new Cell(1, 0) });
            var placement = new Placement(piece, new Cell(4, 4), Rotation.Deg0, 0);

            var result = PlacementValidator.Validate(board, placement);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(PlacementFailureReason.Overlap, result.FailureReason);
        }

        [Test]
        public void Validate_ReturnsValid_WhenAllCellsFreeAndInBounds()
        {
            var board = new BoardState(new BoardSize(10, 10));
            var piece = new PieceDefinition("Test", new[] { new Cell(0, 0), new Cell(1, 0) });
            var placement = new Placement(piece, new Cell(3, 3), Rotation.Deg90, 0);

            var result = PlacementValidator.Validate(board, placement);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(PlacementFailureReason.None, result.FailureReason);
            Assert.AreEqual(2, result.AbsoluteCells.Count);
        }
    }
}
