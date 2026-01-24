using Peribind.Domain.Pieces;

namespace Peribind.Domain.Board
{
    public readonly struct Placement
    {
        public PieceDefinition Piece { get; }
        public Cell Origin { get; }
        public Rotation Rotation { get; }
        public int PlayerId { get; }

        public Placement(PieceDefinition piece, Cell origin, Rotation rotation, int playerId)
        {
            Piece = piece;
            Origin = origin;
            Rotation = rotation;
            PlayerId = playerId;
        }
    }
}
