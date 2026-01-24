namespace Peribind.Domain.Board
{
    public readonly struct BoardSize
    {
        public int Width { get; }
        public int Height { get; }

        public BoardSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public bool IsInBounds(Cell cell)
        {
            return cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;
        }
    }
}
