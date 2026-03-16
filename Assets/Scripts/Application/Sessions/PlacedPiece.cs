using System.Collections.Generic;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;

namespace Peribind.Application.Sessions
{
    public sealed class PlacedPiece
    {
        public int InstanceId { get; }
        public int PlayerId { get; }
        public string PieceId { get; }
        public IReadOnlyList<Cell> Cells { get; }
        public Rotation Rotation { get; }
        public bool IsCathedral { get; }

        public PlacedPiece(int instanceId, int playerId, string pieceId, IReadOnlyList<Cell> cells, Rotation rotation, bool isCathedral)
        {
            InstanceId = instanceId;
            PlayerId = playerId;
            PieceId = pieceId;
            Cells = cells;
            Rotation = rotation;
            IsCathedral = isCathedral;
        }
    }
}
