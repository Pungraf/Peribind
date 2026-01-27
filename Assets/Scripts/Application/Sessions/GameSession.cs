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
        private readonly Dictionary<string, int> _pieceSizes;
        private readonly Dictionary<string, int>[] _initialCounts;
        private readonly Dictionary<int, PlacedPiece> _placedPieces = new Dictionary<int, PlacedPiece>();
        private readonly HashSet<Cell>[] _claimedTerritories;
        private int _nextPieceInstanceId;
        private readonly bool[] _finishedThisRound;

        public BoardState Board { get; }
        public int CurrentPlayerId { get; private set; }
        public GamePhase Phase { get; private set; }
        public IReadOnlyCollection<PlacedPiece> PlacedPieces => _placedPieces.Values;
        public int CurrentRound { get; private set; }
        public int[] TotalScores { get; }
        public bool IsGameOver { get; private set; }
        public int RoundRevision { get; private set; }
        public bool[] FinishedThisRound => _finishedThisRound;

        public GameSession(
            BoardSize size,
            string cathedralPieceId,
            PlayerInventory[] inventories,
            Dictionary<string, int> pieceSizes,
            int startingPlayerId = 0)
        {
            Board = new BoardState(size);
            _cathedralPieceId = cathedralPieceId;
            _inventories = inventories;
            _pieceSizes = pieceSizes ?? new Dictionary<string, int>();
            CurrentPlayerId = startingPlayerId;
            Phase = GamePhase.CathedralPlacement;
            _claimedTerritories = new[]
            {
                new HashSet<Cell>(),
                new HashSet<Cell>()
            };
            _finishedThisRound = new bool[_inventories.Length];
            TotalScores = new int[_inventories.Length];
            CurrentRound = 1;
            RoundRevision = 0;

            _initialCounts = new Dictionary<string, int>[_inventories.Length];
            for (var i = 0; i < _inventories.Length; i++)
            {
                _initialCounts[i] = CopyCounts(_inventories[i]);
            }
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
                AdvanceTurn();
            }
            else
            {
                _inventories[CurrentPlayerId].TryConsume(pieceId);
                RecomputeTerritories(placedPlayerId);
                if (!_inventories[CurrentPlayerId].HasAnyPieces())
                {
                    _finishedThisRound[CurrentPlayerId] = true;
                }
                AdvanceTurn();
            }

            failureReason = PlacementFailureReason.None;
            return true;
        }

        private void AdvanceTurn()
        {
            if (Phase != GamePhase.PlayerTurn)
            {
                return;
            }

            if (AllPlayersFinished())
            {
                EndRound();
                return;
            }

            var nextPlayer = 1 - CurrentPlayerId;
            if (_finishedThisRound[nextPlayer])
            {
                CurrentPlayerId = CurrentPlayerId;
                return;
            }

            CurrentPlayerId = nextPlayer;
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
                if (_finishedThisRound[CurrentPlayerId])
                {
                    return new PlacementResult(false, PlacementFailureReason.FinishedRound, BuildAbsoluteCells(piece, origin, rotation));
                }

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

        public void FinishRoundForCurrentPlayer()
        {
            if (Phase != GamePhase.PlayerTurn || IsGameOver)
            {
                return;
            }

            _finishedThisRound[CurrentPlayerId] = true;
            AdvanceTurn();
        }

        private bool AllPlayersFinished()
        {
            for (var i = 0; i < _finishedThisRound.Length; i++)
            {
                if (!_finishedThisRound[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void EndRound()
        {
            ScoreRound();

            if (CurrentRound >= 2)
            {
                IsGameOver = true;
                return;
            }

            CurrentRound += 1;
            StartRound(CurrentRound == 2 ? 1 : 0);
        }

        private void StartRound(int startingPlayerId)
        {
            Board.ClearAll();
            _placedPieces.Clear();
            _nextPieceInstanceId = 0;

            for (var i = 0; i < _claimedTerritories.Length; i++)
            {
                _claimedTerritories[i].Clear();
            }

            for (var i = 0; i < _finishedThisRound.Length; i++)
            {
                _finishedThisRound[i] = false;
            }

            for (var i = 0; i < _inventories.Length; i++)
            {
                _inventories[i] = new PlayerInventory(new Dictionary<string, int>(_initialCounts[i]));
            }

            CurrentPlayerId = startingPlayerId;
            Phase = GamePhase.CathedralPlacement;
            RoundRevision++;
        }

        private void ScoreRound()
        {
            for (var playerId = 0; playerId < _inventories.Length; playerId++)
            {
                var score = 0;
                foreach (var pair in _inventories[playerId].GetCounts())
                {
                    var size = _pieceSizes.TryGetValue(pair.Key, out var value) ? value : 0;
                    score += size * pair.Value;
                }

                TotalScores[playerId] += score;
            }
        }

        public IReadOnlyCollection<Cell> GetClaimedCells(int playerId)
        {
            if (playerId < 0 || playerId >= _claimedTerritories.Length)
            {
                return new List<Cell>();
            }

            return _claimedTerritories[playerId];
        }

        public GameSessionSnapshot BuildSnapshot()
        {
            var placed = new List<PlacedPieceSnapshot>();
            foreach (var piece in _placedPieces.Values)
            {
                placed.Add(new PlacedPieceSnapshot
                {
                    InstanceId = piece.InstanceId,
                    PlayerId = piece.PlayerId,
                    PieceId = piece.PieceId,
                    IsCathedral = piece.IsCathedral,
                    Cells = new List<Cell>(piece.Cells)
                });
            }

            var inventories = new Dictionary<string, int>[_inventories.Length];
            for (var i = 0; i < _inventories.Length; i++)
            {
                inventories[i] = new Dictionary<string, int>(_inventories[i].GetCounts());
            }

            var territories = new List<Cell>[_claimedTerritories.Length];
            for (var i = 0; i < _claimedTerritories.Length; i++)
            {
                territories[i] = new List<Cell>(_claimedTerritories[i]);
            }

            return new GameSessionSnapshot
            {
                CurrentPlayerId = CurrentPlayerId,
                Phase = Phase,
                CurrentRound = CurrentRound,
                RoundRevision = RoundRevision,
                IsGameOver = IsGameOver,
                TotalScores = (int[])TotalScores.Clone(),
                FinishedThisRound = (bool[])_finishedThisRound.Clone(),
                Inventories = inventories,
                PlacedPieces = placed,
                ClaimedTerritories = territories
            };
        }

        public void LoadSnapshot(GameSessionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            Board.ClearAll();
            _placedPieces.Clear();
            _nextPieceInstanceId = 0;

            for (var i = 0; i < _claimedTerritories.Length; i++)
            {
                _claimedTerritories[i].Clear();
            }

            if (snapshot.Inventories != null)
            {
                for (var i = 0; i < _inventories.Length && i < snapshot.Inventories.Length; i++)
                {
                    _inventories[i] = new PlayerInventory(new Dictionary<string, int>(snapshot.Inventories[i]));
                }
            }

            CurrentPlayerId = snapshot.CurrentPlayerId;
            Phase = snapshot.Phase;
            CurrentRound = snapshot.CurrentRound;
            RoundRevision = snapshot.RoundRevision;
            IsGameOver = snapshot.IsGameOver;

            if (TotalScores != null && snapshot.TotalScores != null)
            {
                for (var i = 0; i < TotalScores.Length && i < snapshot.TotalScores.Length; i++)
                {
                    TotalScores[i] = snapshot.TotalScores[i];
                }
            }

            if (snapshot.FinishedThisRound != null)
            {
                for (var i = 0; i < _finishedThisRound.Length && i < snapshot.FinishedThisRound.Length; i++)
                {
                    _finishedThisRound[i] = snapshot.FinishedThisRound[i];
                }
            }

            if (snapshot.ClaimedTerritories != null)
            {
                for (var i = 0; i < _claimedTerritories.Length && i < snapshot.ClaimedTerritories.Length; i++)
                {
                    _claimedTerritories[i].UnionWith(snapshot.ClaimedTerritories[i]);
                }
            }

            if (snapshot.PlacedPieces == null)
            {
                return;
            }

            foreach (var piece in snapshot.PlacedPieces)
            {
                if (piece == null || piece.Cells == null)
                {
                    continue;
                }

                var occupant = new CellOccupant(piece.PlayerId, piece.InstanceId, piece.PieceId);
                foreach (var cell in piece.Cells)
                {
                    Board.SetOccupant(cell, occupant);
                }

                _placedPieces[piece.InstanceId] = new PlacedPiece(piece.InstanceId, piece.PlayerId, piece.PieceId, piece.Cells, piece.IsCathedral);
                if (piece.InstanceId > _nextPieceInstanceId)
                {
                    _nextPieceInstanceId = piece.InstanceId;
                }
            }
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

        private static Dictionary<string, int> CopyCounts(PlayerInventory inventory)
        {
            var copy = new Dictionary<string, int>();
            foreach (var pair in inventory.GetCounts())
            {
                copy[pair.Key] = pair.Value;
            }

            return copy;
        }
    }
}
