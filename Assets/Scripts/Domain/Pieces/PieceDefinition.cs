using System.Collections.Generic;
using Peribind.Domain.Board;

namespace Peribind.Domain.Pieces
{
    public class PieceDefinition
    {
        public string Id { get; }
        public IReadOnlyList<Cell> Cells { get; }

        public PieceDefinition(string id, IReadOnlyList<Cell> cells)
        {
            Id = id;
            Cells = cells;
        }

        public IEnumerable<Cell> GetCells(Rotation rotation)
        {
            foreach (var cell in Cells)
            {
                yield return Rotate(cell, rotation);
            }
        }

        private static Cell Rotate(Cell cell, Rotation rotation)
        {
            return rotation switch
            {
                Rotation.Deg0 => cell,
                Rotation.Deg90 => new Cell(cell.Y, -cell.X),
                Rotation.Deg180 => new Cell(-cell.X, -cell.Y),
                Rotation.Deg270 => new Cell(-cell.Y, cell.X),
                _ => cell
            };
        }
    }
}
