using System;

namespace Peribind.Domain.Board
{
    public class BoardState
    {
        private readonly int?[,] _occupancy;

        public BoardSize Size { get; }

        public BoardState(BoardSize size)
        {
            Size = size;
            _occupancy = new int?[size.Width, size.Height];
        }

        public bool IsOccupied(Cell cell)
        {
            return _occupancy[cell.X, cell.Y].HasValue;
        }

        public int? GetOccupant(Cell cell)
        {
            return _occupancy[cell.X, cell.Y];
        }

        public void SetOccupant(Cell cell, int playerId)
        {
            _occupancy[cell.X, cell.Y] = playerId;
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
