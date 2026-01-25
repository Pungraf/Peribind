using System.Collections.Generic;
using Peribind.Domain.Board;

namespace Peribind.Application.Sessions
{
    public sealed class PlacedPiece
    {
        public int InstanceId { get; }
        public int PlayerId { get; }
        public string PieceId { get; }
        public IReadOnlyList<Cell> Cells { get; }
        public bool IsCathedral { get; }

        public PlacedPiece(int instanceId, int playerId, string pieceId, IReadOnlyList<Cell> cells, bool isCathedral)
        {
            InstanceId = instanceId;
            PlayerId = playerId;
            PieceId = pieceId;
            Cells = cells;
            IsCathedral = isCathedral;
        }
    }
}
