using Peribind.Application.Commands;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;

namespace Peribind.Application.Sessions
{
    public class GameSession
    {
        public const int NeutralPlayerId = -1;

        private readonly PlayerInventory[] _inventories;
        private readonly string _cathedralPieceId;

        public BoardState Board { get; }
        public int CurrentPlayerId { get; private set; }
        public GamePhase Phase { get; private set; }

        public GameSession(BoardSize size, string cathedralPieceId, PlayerInventory[] inventories, int startingPlayerId = 0)
        {
            Board = new BoardState(size);
            _cathedralPieceId = cathedralPieceId;
            _inventories = inventories;
            CurrentPlayerId = startingPlayerId;
            Phase = GamePhase.CathedralPlacement;
        }

        public bool HasPieceForCurrentPlayer(string pieceId)
        {
            if (Phase != GamePhase.PlayerTurn)
            {
                return false;
            }

            return _inventories[CurrentPlayerId].HasPiece(pieceId);
        }

        public int GetRemainingCount(int playerId, string pieceId)
        {
            if (playerId < 0 || playerId >= _inventories.Length)
            {
                return 0;
            }

            return _inventories[playerId].GetRemainingCount(pieceId);
        }

        public bool TryPlacePiece(
            PieceDefinition piece,
            string pieceId,
            Cell origin,
            Rotation rotation,
            out PlacePieceCommand command,
            out PlacementResult placementResult,
            out PlacementFailureReason failureReason,
            out int placedPlayerId,
            out bool isCathedral)
        {
            command = null;
            placementResult = default;
            placedPlayerId = NeutralPlayerId;
            isCathedral = false;

            if (Phase == GamePhase.CathedralPlacement)
            {
                if (pieceId != _cathedralPieceId)
                {
                    failureReason = PlacementFailureReason.InvalidPieceForPhase;
                    return false;
                }

                placedPlayerId = NeutralPlayerId;
                isCathedral = true;
            }
            else
            {
                if (!_inventories[CurrentPlayerId].HasPiece(pieceId))
                {
                    failureReason = PlacementFailureReason.NoRemainingPieces;
                    return false;
                }

                placedPlayerId = CurrentPlayerId;
            }

            var placement = new Placement(piece, origin, rotation, placedPlayerId);
            placementResult = PlacementValidator.Validate(Board, placement);
            if (!placementResult.IsValid)
            {
                failureReason = placementResult.FailureReason;
                return false;
            }

            command = new PlacePieceCommand(placementResult.AbsoluteCells, placedPlayerId);
            command.Apply(Board);

            if (isCathedral)
            {
                Phase = GamePhase.PlayerTurn;
            }
            else
            {
                _inventories[CurrentPlayerId].TryConsume(pieceId);
                EndTurn();
            }

            failureReason = PlacementFailureReason.None;
            return true;
        }

        private void EndTurn()
        {
            CurrentPlayerId = 1 - CurrentPlayerId;
        }
    }
}
