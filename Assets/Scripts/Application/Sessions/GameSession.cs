using System.Collections.Generic;
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
        private readonly Dictionary<int, PlacedPiece> _placedPieces = new Dictionary<int, PlacedPiece>();
        private readonly HashSet<Cell>[] _claimedTerritories;
        private int _nextPieceInstanceId;

        public BoardState Board { get; }
        public int CurrentPlayerId { get; private set; }
        public GamePhase Phase { get; private set; }
        public IReadOnlyCollection<PlacedPiece> PlacedPieces => _placedPieces.Values;

        public GameSession(BoardSize size, string cathedralPieceId, PlayerInventory[] inventories, int startingPlayerId = 0)
        {
            Board = new BoardState(size);
            _cathedralPieceId = cathedralPieceId;
            _inventories = inventories;
            CurrentPlayerId = startingPlayerId;
            Phase = GamePhase.CathedralPlacement;
            _claimedTerritories = new[]
            {
                new HashSet<Cell>(),
                new HashSet<Cell>()
            };
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
            placedPlayerId = NeutralPlayerId;
            isCathedral = false;
            placementResult = ValidatePlacement(piece, pieceId, origin, rotation, out placedPlayerId, out isCathedral);
            if (!placementResult.IsValid)
            {
                failureReason = placementResult.FailureReason;
                return false;
            }

            var instanceId = ++_nextPieceInstanceId;
            var occupant = new CellOccupant(placedPlayerId, instanceId, pieceId);
            command = new PlacePieceCommand(placementResult.AbsoluteCells, occupant);
            command.Apply(Board);
            _placedPieces[instanceId] = new PlacedPiece(instanceId, placedPlayerId, pieceId, placementResult.AbsoluteCells, isCathedral);

            if (isCathedral)
            {
                Phase = GamePhase.PlayerTurn;
            }
            else
            {
                _inventories[CurrentPlayerId].TryConsume(pieceId);
                RecomputeTerritories(placedPlayerId);
                EndTurn();
            }

            failureReason = PlacementFailureReason.None;
            return true;
        }

        private void EndTurn()
        {
            CurrentPlayerId = 1 - CurrentPlayerId;
        }

        public PlacementResult ValidatePlacement(
            PieceDefinition piece,
            string pieceId,
            Cell origin,
            Rotation rotation,
            out int placedPlayerId,
            out bool isCathedral)
        {
            placedPlayerId = NeutralPlayerId;
            isCathedral = false;

            if (Phase == GamePhase.CathedralPlacement)
            {
                if (pieceId != _cathedralPieceId)
                {
                    return new PlacementResult(false, PlacementFailureReason.InvalidPieceForPhase, BuildAbsoluteCells(piece, origin, rotation));
                }

                placedPlayerId = NeutralPlayerId;
                isCathedral = true;
            }
            else
            {
                if (!_inventories[CurrentPlayerId].HasPiece(pieceId))
                {
                    return new PlacementResult(false, PlacementFailureReason.NoRemainingPieces, BuildAbsoluteCells(piece, origin, rotation));
                }

                placedPlayerId = CurrentPlayerId;
            }

            var placement = new Placement(piece, origin, rotation, placedPlayerId);
            var placementResult = PlacementValidator.Validate(Board, placement);
            if (!placementResult.IsValid)
            {
                return placementResult;
            }

            if (Phase == GamePhase.PlayerTurn)
            {
                var opponentId = 1 - CurrentPlayerId;
                foreach (var cell in placementResult.AbsoluteCells)
                {
                    if (_claimedTerritories[opponentId].Contains(cell))
                    {
                        return new PlacementResult(false, PlacementFailureReason.InOpponentTerritory, placementResult.AbsoluteCells);
                    }
                }
            }

            return placementResult;
        }

        public IReadOnlyCollection<Cell> GetClaimedCells(int playerId)
        {
            if (playerId < 0 || playerId >= _claimedTerritories.Length)
            {
                return new List<Cell>();
            }

            return _claimedTerritories[playerId];
        }

        public Dictionary<int, List<Cell>> GetClaimedTerritories()
        {
            var result = new Dictionary<int, List<Cell>>();
            for (var i = 0; i < _claimedTerritories.Length; i++)
            {
                result[i] = new List<Cell>(_claimedTerritories[i]);
            }
            return result;
        }

        private void RecomputeTerritories(int lastMoverId)
        {
            var previousClaims = new HashSet<Cell>[_claimedTerritories.Length];
            for (var i = 0; i < _claimedTerritories.Length; i++)
            {
                previousClaims[i] = new HashSet<Cell>(_claimedTerritories[i]);
            }

            for (var i = 0; i < _claimedTerritories.Length; i++)
            {
                _claimedTerritories[i].Clear();
            }

            var cathedralRemoved = false;
            for (var ownerId = 0; ownerId < _claimedTerritories.Length; ownerId++)
            {
                if (ResolveTerritoryForPlayer(ownerId))
                {
                    cathedralRemoved = true;
                }
            }

            for (var ownerId = 0; ownerId < _claimedTerritories.Length; ownerId++)
            {
                _claimedTerritories[ownerId].UnionWith(previousClaims[ownerId]);
            }

            if (_claimedTerritories.Length < 2)
            {
                return;
            }

            var overlap = new List<Cell>();
            foreach (var cell in _claimedTerritories[0])
            {
                if (_claimedTerritories[1].Contains(cell))
                {
                    overlap.Add(cell);
                }
            }

            if (overlap.Count == 0)
            {
                if (cathedralRemoved)
                {
                    RecomputeTerritories(lastMoverId);
                }
                return;
            }

            if (lastMoverId == 0)
            {
                foreach (var cell in overlap)
                {
                    _claimedTerritories[1].Remove(cell);
                }
            }
            else if (lastMoverId == 1)
            {
                foreach (var cell in overlap)
                {
                    _claimedTerritories[0].Remove(cell);
                }
            }
            else
            {
                foreach (var cell in overlap)
                {
                    _claimedTerritories[0].Remove(cell);
                    _claimedTerritories[1].Remove(cell);
                }
            }

            if (cathedralRemoved)
            {
                RecomputeTerritories(lastMoverId);
            }
        }

        private bool ResolveTerritoryForPlayer(int ownerId)
        {
            if (ownerId < 0 || ownerId >= _claimedTerritories.Length)
            {
                return false;
            }

            var regions = TerritoryResolver.GetRegions(Board, ownerId, NeutralPlayerId);
            var cathedralRemoved = false;
            foreach (var region in regions)
            {
                if (!region.HasOwnerBorder || !region.IsBoundaryClosed)
                {
                    continue;
                }

                if (region.ContainsNeutral)
                {
                    if (region.OpponentPieceInstanceIds.Count == 0 && !region.TouchesBoardEdge)
                    {
                        if (RemoveNeutralPieces(region.Cells))
                        {
                            cathedralRemoved = true;
                        }
                    }
                    continue;
                }

                var opponentCount = region.OpponentPieceInstanceIds.Count;
                if (opponentCount >= 2)
                {
                    continue;
                }

                if (opponentCount == 1)
                {
                    foreach (var instanceId in region.OpponentPieceInstanceIds)
                    {
                        CapturePiece(instanceId);
                    }
                }

                foreach (var cell in region.Cells)
                {
                    _claimedTerritories[ownerId].Add(cell);
                }
            }

            return cathedralRemoved;
        }

        private bool RemoveNeutralPieces(IReadOnlyList<Cell> regionCells)
        {
            var regionSet = new HashSet<Cell>(regionCells);
            var toRemove = new List<int>();
            foreach (var pair in _placedPieces)
            {
                var piece = pair.Value;
                if (piece.PlayerId != NeutralPlayerId || !piece.IsCathedral)
                {
                    continue;
                }

                foreach (var cell in piece.Cells)
                {
                    if (regionSet.Contains(cell))
                    {
                        toRemove.Add(pair.Key);
                        break;
                    }
                }
            }

            if (toRemove.Count == 0)
            {
                return false;
            }

            foreach (var instanceId in toRemove)
            {
                if (!_placedPieces.TryGetValue(instanceId, out var piece))
                {
                    continue;
                }

                foreach (var cell in piece.Cells)
                {
                    Board.ClearOccupant(cell);
                }

                _placedPieces.Remove(instanceId);
            }

            return true;
        }

        private void CapturePiece(int pieceInstanceId)
        {
            if (!_placedPieces.TryGetValue(pieceInstanceId, out var piece))
            {
                return;
            }

            if (piece.PlayerId == NeutralPlayerId)
            {
                return;
            }

            foreach (var cell in piece.Cells)
            {
                Board.ClearOccupant(cell);
            }

            if (piece.PlayerId >= 0 && piece.PlayerId < _inventories.Length)
            {
                _inventories[piece.PlayerId].ReturnPiece(piece.PieceId);
            }

            _placedPieces.Remove(pieceInstanceId);
        }

        private static List<Cell> BuildAbsoluteCells(PieceDefinition piece, Cell origin, Rotation rotation)
        {
            var cells = new List<Cell>();
            foreach (var cell in piece.GetCells(rotation))
            {
                cells.Add(cell + origin);
            }

            return cells;
        }
    }
}
