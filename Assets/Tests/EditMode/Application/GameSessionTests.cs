using System.Collections.Generic;
using NUnit.Framework;
using Peribind.Application.Sessions;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;

namespace Peribind.Tests.EditMode.Application
{
    public class GameSessionTests
    {
        [Test]
        public void CathedralMustBePlacedFirst()
        {
            var size = new BoardSize(10, 10);
            var cathedral = new PieceDefinition("Cathedral", new[] { new Cell(0, 0) });
            var piece = new PieceDefinition("P1", new[] { new Cell(0, 0) });
            var inventories = new[]
            {
                new PlayerInventory(new Dictionary<string, int> { { "P1", 1 } }),
                new PlayerInventory(new Dictionary<string, int> { { "P1", 1 } })
            };

            var pieceSizes = new Dictionary<string, int>
            {
                { cathedral.Id, 1 },
                { piece.Id, 1 }
            };
            var session = new GameSession(size, cathedral.Id, inventories, pieceSizes);

            var success = session.TryPlacePiece(
                piece,
                piece.Id,
                new Cell(0, 0),
                Rotation.Deg0,
                out _,
                out _,
                out var reason,
                out _,
                out _);

            Assert.IsFalse(success);
            Assert.AreEqual(PlacementFailureReason.InvalidPieceForPhase, reason);
        }

        [Test]
        public void PlayerTurnConsumesInventoryAndAdvances()
        {
            var size = new BoardSize(10, 10);
            var cathedral = new PieceDefinition("Cathedral", new[] { new Cell(0, 0) });
            var piece = new PieceDefinition("P1", new[] { new Cell(0, 0) });
            var inventories = new[]
            {
                new PlayerInventory(new Dictionary<string, int> { { "P1", 1 } }),
                new PlayerInventory(new Dictionary<string, int> { { "P1", 1 } })
            };

            var pieceSizes = new Dictionary<string, int>
            {
                { cathedral.Id, 1 },
                { piece.Id, 1 }
            };
            var session = new GameSession(size, cathedral.Id, inventories, pieceSizes);

            session.TryPlacePiece(
                cathedral,
                cathedral.Id,
                new Cell(0, 0),
                Rotation.Deg0,
                out _,
                out _,
                out _,
                out _,
                out _);

            var success = session.TryPlacePiece(
                piece,
                piece.Id,
                new Cell(1, 0),
                Rotation.Deg0,
                out _,
                out _,
                out _,
                out var placedPlayerId,
                out var isCathedral);

            Assert.IsTrue(success);
            Assert.AreEqual(0, placedPlayerId);
            Assert.IsFalse(isCathedral);
            Assert.AreEqual(1, session.CurrentPlayerId);
        }
    }
}
