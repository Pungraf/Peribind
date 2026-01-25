namespace Peribind.Domain.Board
{
    public readonly struct CellOccupant
    {
        public int PlayerId { get; }
        public int PieceInstanceId { get; }
        public string PieceId { get; }

        public CellOccupant(int playerId, int pieceInstanceId, string pieceId)
        {
            PlayerId = playerId;
            PieceInstanceId = pieceInstanceId;
            PieceId = pieceId;
        }
    }
}
