using System.Collections.Generic;
using Peribind.Application.Sessions;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;
using Peribind.Unity.Input;
using Peribind.Unity.Networking;
using Peribind.Unity.ScriptableObjects;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Peribind.Unity.Board
{
    public class BoardPresenter : MonoBehaviour
    {
        [SerializeField] private GridMapper gridMapper;
        [SerializeField] private BoardRaycaster boardRaycaster;
        [SerializeField] private PlacementPreview placementPreview;
        [SerializeField] private BoardPlacementView placementView;
        [SerializeField] private TerritoryOverlayView territoryOverlayView;
        [SerializeField] private PieceSelection pieceSelection;
        [SerializeField] private GameConfigSO gameConfig;
        [SerializeField] private InputReader inputReader;
        [SerializeField] private NetworkGameController networkController;

        private GameSession _session;
        private Rotation _rotation = Rotation.Deg0;
        private PieceDefinition _currentPieceDefinition;
        private PieceDefinitionSO _currentPieceAsset;
        private bool _placementPaused;
        private int _placementRevision;
        private GamePhase _lastPhase;
        private int _lastRoundRevision;

        public int PlacementRevision => _placementRevision;
        public int CurrentPlayerId => _session != null ? _session.CurrentPlayerId : 0;
        public bool IsPlayerTurn => _session != null && _session.Phase == GamePhase.PlayerTurn;
        public Color CurrentPlayerColor => ResolvePreviewColor();
        private void Awake()
        {
            if (gridMapper == null)
            {
                return;
            }

            if (networkController == null)
            {
                networkController = FindObjectOfType<NetworkGameController>();
            }

            if (networkController != null)
            {
                return;
            }

            if (gameConfig == null || gameConfig.PlayerOnePieceSet == null || gameConfig.PlayerTwoPieceSet == null || gameConfig.CathedralPiece == null)
            {
                return;
            }

            var inventories = BuildInventories(gameConfig.PlayerOnePieceSet, gameConfig.PlayerTwoPieceSet);
            var pieceSizes = BuildPieceSizes(gameConfig.PlayerOnePieceSet, gameConfig.PlayerTwoPieceSet, gameConfig.CathedralPiece);
            _session = new GameSession(new BoardSize(gridMapper.Width, gridMapper.Height), gameConfig.CathedralPiece.Id, inventories, pieceSizes);
        }

        private void OnEnable()
        {
            if (networkController != null)
            {
                networkController.SessionUpdated += OnSessionUpdated;
            }
        }

        private void OnDisable()
        {
            if (networkController != null)
            {
                networkController.SessionUpdated -= OnSessionUpdated;
            }
        }

        private void Update()
        {
            if (_session == null && networkController != null && networkController.Session != null)
            {
                _session = networkController.Session;
                _lastRoundRevision = -1;
                _lastPhase = (GamePhase)(-1);
                _placementRevision++;
                RebuildPlacements();
                UpdateTerritories();
            }

            if (_session == null || inputReader == null || pieceSelection == null)
            {
                return;
            }

            if (_lastRoundRevision != _session.RoundRevision)
            {
                _lastRoundRevision = _session.RoundRevision;
                _rotation = Rotation.Deg0;
                pieceSelection.ClearSelection();
                if (placementPreview != null)
                {
                    placementPreview.Hide();
                }
                if (placementView != null)
                {
                    placementView.ClearAll();
                }
                if (territoryOverlayView != null)
                {
                    territoryOverlayView.ClearAll();
                }
                _placementRevision++;
            }

            if (_lastPhase != _session.Phase)
            {
                _lastPhase = _session.Phase;
                if (_session.Phase == GamePhase.PlayerTurn)
                {
                    pieceSelection.ClearSelection();
                    if (placementPreview != null)
                    {
                        placementPreview.Hide();
                    }
                }
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                if (placementPreview != null)
                {
                    placementPreview.Hide();
                }
                inputReader.ClearPlacePressed();
                return;
            }

            HandleSelectionInput();
            HandleRotationInput();

            if (inputReader.ConsumeCancelPressed())
            {
                _placementPaused = true;
                if (networkController != null)
                {
                    networkController.NotifyLocalInput();
                }
            }

            var pieceAsset = GetActivePiece();
            if (pieceAsset == null)
            {
                if (placementPreview != null)
                {
                    placementPreview.Hide();
                }
                return;
            }

            if (_placementPaused)
            {
                if (placementPreview != null)
                {
                    placementPreview.Hide();
                }
                return;
            }

            if (_currentPieceAsset != pieceAsset)
            {
                _currentPieceAsset = pieceAsset;
                _currentPieceDefinition = pieceAsset.ToDomainDefinition();
            }

            if (!boardRaycaster.TryGetCellAtScreenPoint(inputReader.PointerPosition, out var cell))
            {
                if (placementPreview != null)
                {
                    placementPreview.Hide();
                }
                return;
            }

            var result = _session.ValidatePlacement(_currentPieceDefinition, pieceAsset.Id, cell, _rotation, out _, out _);
            if (placementPreview != null)
            {
                var previewColor = ResolvePreviewColor();
                placementPreview.Show(gridMapper, _currentPieceDefinition, cell, _rotation, result.IsValid, previewColor);
            }

            if (inputReader.ConsumePlacePressed() && result.IsValid)
            {
                if (networkController != null)
                {
                    networkController.NotifyLocalInput();
                }
                if (networkController != null)
                {
                    networkController.RequestPlacePiece(pieceAsset.Id, cell, _rotation);
                }
                else
                {
                    if (_session.TryPlacePiece(
                        _currentPieceDefinition,
                        pieceAsset.Id,
                        cell,
                        _rotation,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _))
                    {
                        if (placementView != null)
                        {
                            RebuildPlacements();
                        }

                        _placementRevision++;
                        if (_session.Phase == GamePhase.PlayerTurn)
                        {
                            pieceSelection.ClearSelection();
                        }

                        UpdateTerritories();
                    }
                }
            }
        }

        private void HandleRotationInput()
        {
            var step = inputReader.ConsumeRotateStep();
            if (step == 0)
            {
                return;
            }

            if (networkController != null)
            {
                networkController.NotifyLocalInput();
            }

            var current = (int)_rotation;
            current = (current + step) % 4;
            if (current < 0)
            {
                current += 4;
            }

            _rotation = (Rotation)current;
        }

        private void HandleSelectionInput()
        {
            if (_session == null || _session.Phase != GamePhase.PlayerTurn)
            {
                return;
            }

            if (networkController != null && !networkController.IsLocalPlayerTurn())
            {
                return;
            }

            var step = inputReader.ConsumeSelectStep();
            if (step == 0)
            {
                return;
            }

            if (networkController != null)
            {
                networkController.NotifyLocalInput();
            }

            SelectAvailablePiece(step > 0 ? 1 : -1);

            _placementPaused = false;
        }

        private PieceDefinitionSO GetActivePiece()
        {
            if (_session == null)
            {
                return null;
            }

            if (_session.Phase == GamePhase.CathedralPlacement)
            {
                if (networkController != null && !networkController.IsLocalPlayerTurn())
                {
                    return null;
                }
                return gameConfig != null ? gameConfig.CathedralPiece : null;
            }

            if (pieceSelection == null)
            {
                return null;
            }

            var piece = pieceSelection.Current;
            if (piece == null)
            {
                return null;
            }

            if (_session.HasPieceForCurrentPlayer(piece.Id))
            {
                if (networkController != null && !networkController.IsLocalPlayerTurn())
                {
                    return null;
                }
                return piece;
            }

            SelectAvailablePiece(1);
            piece = pieceSelection.Current;
            return piece != null && _session.HasPieceForCurrentPlayer(piece.Id) ? piece : null;
        }

        private void SelectAvailablePiece(int direction)
        {
            if (pieceSelection == null)
            {
                return;
            }

            var attempts = 0;
            var maxAttempts = Mathf.Max(1, pieceSelection.Count);
            while (attempts < maxAttempts)
            {
                if (direction > 0)
                {
                    pieceSelection.SelectNext();
                }
                else
                {
                    pieceSelection.SelectPrevious();
                }

                var piece = pieceSelection.Current;
                if (piece != null && _session.HasPieceForCurrentPlayer(piece.Id))
                {
                    return;
                }

                attempts++;
            }
        }

        private static PlayerInventory[] BuildInventories(PieceSetSO playerOneSet, PieceSetSO playerTwoSet)
        {
            var playerOne = new PlayerInventory(BuildCounts(playerOneSet));
            var playerTwo = new PlayerInventory(BuildCounts(playerTwoSet));
            return new[] { playerOne, playerTwo };
        }

        private static Dictionary<string, int> BuildCounts(PieceSetSO set)
        {
            var counts = new Dictionary<string, int>();
            foreach (var entry in set.Entries)
            {
                if (entry.piece == null)
                {
                    continue;
                }

                var count = Mathf.Max(0, entry.count);
                if (counts.ContainsKey(entry.piece.Id))
                {
                    counts[entry.piece.Id] += count;
                }
                else
                {
                    counts.Add(entry.piece.Id, count);
                }
            }

            return counts;
        }

        private static Dictionary<string, int> BuildPieceSizes(PieceSetSO playerOneSet, PieceSetSO playerTwoSet, PieceDefinitionSO cathedral)
        {
            var sizes = new Dictionary<string, int>();
            AddPieceSizes(sizes, playerOneSet);
            AddPieceSizes(sizes, playerTwoSet);
            if (cathedral != null && !sizes.ContainsKey(cathedral.Id))
            {
                sizes[cathedral.Id] = cathedral.Cells.Count;
            }
            return sizes;
        }

        private static void AddPieceSizes(Dictionary<string, int> sizes, PieceSetSO set)
        {
            foreach (var entry in set.Entries)
            {
                if (entry.piece == null)
                {
                    continue;
                }

                if (!sizes.ContainsKey(entry.piece.Id))
                {
                    sizes[entry.piece.Id] = entry.piece.Cells.Count;
                }
            }
        }

        private Color ResolvePlacementColor(int placedPlayerId, bool isCathedral)
        {
            if (gameConfig == null)
            {
                return Color.white;
            }

            if (isCathedral || placedPlayerId == GameSession.NeutralPlayerId)
            {
                return gameConfig.CathedralColor;
            }

            return placedPlayerId == 0 ? gameConfig.PlayerOneColor : gameConfig.PlayerTwoColor;
        }

        private Color ResolvePreviewColor()
        {
            if (gameConfig == null || _session == null)
            {
                return Color.white;
            }

            if (_session.Phase == GamePhase.CathedralPlacement)
            {
                return gameConfig.CathedralColor;
            }

            return _session.CurrentPlayerId == 0 ? gameConfig.PlayerOneColor : gameConfig.PlayerTwoColor;
        }

        private void UpdateTerritories()
        {
            if (territoryOverlayView == null || _session == null || gridMapper == null || gameConfig == null)
            {
                return;
            }

            var territories = _session.GetClaimedTerritories();
            var colors = new Dictionary<int, Color>
            {
                { 0, new Color(gameConfig.PlayerOneColor.r, gameConfig.PlayerOneColor.g, gameConfig.PlayerOneColor.b, 0.2f) },
                { 1, new Color(gameConfig.PlayerTwoColor.r, gameConfig.PlayerTwoColor.g, gameConfig.PlayerTwoColor.b, 0.2f) }
            };

            territoryOverlayView.SetTerritories(gridMapper, territories, colors);
        }

        private void RebuildPlacements()
        {
            if (placementView == null || _session == null || gridMapper == null)
            {
                return;
            }

            placementView.ClearAll();
            foreach (var placedPiece in _session.PlacedPieces)
            {
                var color = ResolvePlacementColor(placedPiece.PlayerId, placedPiece.IsCathedral);
                placementView.AddPlacement(gridMapper, placedPiece.Cells, color);
            }
        }

        public bool HasPieceForCurrentPlayer(string pieceId)
        {
            if (_session == null)
            {
                return false;
            }

            return _session.HasPieceForCurrentPlayer(pieceId);
        }

        public int GetRemainingCount(string pieceId)
        {
            if (_session == null)
            {
                return 0;
            }

            return _session.GetRemainingCount(_session.CurrentPlayerId, pieceId);
        }

        public int CurrentRound => _session != null ? _session.CurrentRound : 1;
        public bool IsGameOver => _session != null && _session.IsGameOver;

        public int GetTotalScore(int playerId)
        {
            if (_session == null || _session.TotalScores == null)
            {
                return 0;
            }

            if (playerId < 0 || playerId >= _session.TotalScores.Length)
            {
                return 0;
            }

            return _session.TotalScores[playerId];
        }

        public bool HasFinishedRound(int playerId)
        {
            if (_session == null || _session.FinishedThisRound == null)
            {
                return false;
            }

            if (playerId < 0 || playerId >= _session.FinishedThisRound.Length)
            {
                return false;
            }

            return _session.FinishedThisRound[playerId];
        }

        public void FinishRoundForCurrentPlayer()
        {
            if (_session == null)
            {
                return;
            }

            if (networkController != null)
            {
                networkController.RequestFinishRound();
            }
            else
            {
                _session.FinishRoundForCurrentPlayer();
                _placementRevision++;
                UpdateTerritories();
            }
        }

        private void OnSessionUpdated()
        {
            _placementRevision++;
            if (_session != null && _session.Phase == GamePhase.PlayerTurn && pieceSelection != null)
            {
                pieceSelection.ClearSelection();
            }

            RebuildPlacements();
            UpdateTerritories();
        }
    }
}
