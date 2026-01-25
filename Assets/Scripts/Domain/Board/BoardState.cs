using System;

namespace Peribind.Domain.Board
{
    public class BoardState
    {
        private readonly CellOccupant?[,] _occupancy;

        public BoardSize Size { get; }

        public BoardState(BoardSize size)
        {
            Size = size;
            _occupancy = new CellOccupant?[size.Width, size.Height];
        }

        public bool IsOccupied(Cell cell)
        {
            return _occupancy[cell.X, cell.Y].HasValue;
        }

        public CellOccupant? GetOccupant(Cell cell)
        {
            return _occupancy[cell.X, cell.Y];
        }

        public int? GetOccupantPlayerId(Cell cell)
        {
            return _occupancy[cell.X, cell.Y]?.PlayerId;
        }

        public void SetOccupant(Cell cell, CellOccupant occupant)
        {
            _occupancy[cell.X, cell.Y] = occupant;
        }

        public void ClearOccupant(Cell cell)
        {
            _occupancy[cell.X, cell.Y] = null;
        }

        public void ClearAll()
        {
            Array.Clear(_occupancy, 0, _occupancy.Length);
        }
    }
}
