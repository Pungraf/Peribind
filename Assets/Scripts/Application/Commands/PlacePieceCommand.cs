using System.Collections.Generic;
using Peribind.Domain.Board;

namespace Peribind.Application.Commands
{
    public class PlacePieceCommand : IGameCommand
    {
        private readonly IReadOnlyList<Cell> _cells;
        private readonly CellOccupant _occupant;

        public PlacePieceCommand(IReadOnlyList<Cell> cells, CellOccupant occupant)
        {
            _cells = cells;
            _occupant = occupant;
        }

        public void Apply(BoardState board)
        {
            foreach (var cell in _cells)
            {
                board.SetOccupant(cell, _occupant);
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
