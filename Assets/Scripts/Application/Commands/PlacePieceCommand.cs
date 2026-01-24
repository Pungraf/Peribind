using System.Collections.Generic;
using Peribind.Domain.Board;

namespace Peribind.Application.Commands
{
    public class PlacePieceCommand : IGameCommand
    {
        private readonly IReadOnlyList<Cell> _cells;
        private readonly int _playerId;

        public PlacePieceCommand(IReadOnlyList<Cell> cells, int playerId)
        {
            _cells = cells;
            _playerId = playerId;
        }

        public void Apply(BoardState board)
        {
            foreach (var cell in _cells)
            {
                board.SetOccupant(cell, _playerId);
            }
        }

        public void Undo(BoardState board)
        {
            foreach (var cell in _cells)
            {
                board.ClearOccupant(cell);
            }
        }
    }
}
