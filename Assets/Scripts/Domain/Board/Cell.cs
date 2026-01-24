namespace Peribind.Domain.Board
{
    public readonly struct Cell
    {
        public int X { get; }
        public int Y { get; }

        public Cell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static Cell operator +(Cell a, Cell b) => new Cell(a.X + b.X, a.Y + b.Y);

        public override string ToString() => $"({X},{Y})";
    }
}
