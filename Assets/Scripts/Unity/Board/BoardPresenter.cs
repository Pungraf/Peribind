using System.Collections.Generic;
using Peribind.Application.Sessions;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;
using Peribind.Unity.Input;
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
        [SerializeField] private PieceSelection pieceSelection;
        [SerializeField] private GameConfigSO gameConfig;
        [SerializeField] private InputReader inputReader;

        private GameSession _session;
        private Rotation _rotation = Rotation.Deg0;
        private PieceDefinition _currentPieceDefinition;
        private PieceDefinitionSO _currentPieceAsset;
        private bool _placementPaused;
        private int _placementRevision;
        private GamePhase _lastPhase;

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

            if (gameConfig == null || gameConfig.PlayerOnePieceSet == null || gameConfig.PlayerTwoPieceSet == null || gameConfig.CathedralPiece == null)
            {
                return;
            }

            var inventories = BuildInventories(gameConfig.PlayerOnePieceSet, gameConfig.PlayerTwoPieceSet);
            _session = new GameSession(new BoardSize(gridMapper.Width, gridMapper.Height), gameConfig.CathedralPiece.Id, inventories);
        }

        private void Update()
        {
            if (_session == null || inputReader == null || pieceSelection == null)
            {
                return;
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

            var placement = new Placement(_currentPieceDefinition, cell, _rotation, _session.Phase == GamePhase.CathedralPlacement ? GameSession.NeutralPlayerId : _session.CurrentPlayerId);
            var result = PlacementValidator.Validate(_session.Board, placement);
            if (placementPreview != null)
            {
                var previewColor = ResolvePreviewColor();
                placementPreview.Show(gridMapper, _currentPieceDefinition, cell, _rotation, result.IsValid, previewColor);
            }

            if (inputReader.ConsumePlacePressed() && result.IsValid)
            {
                if (_session.TryPlacePiece(
                    _currentPieceDefinition,
                    pieceAsset.Id,
                    cell,
                    _rotation,
                    out var command,
                    out var placementResult,
                    out _,
                    out var placedPlayerId,
                    out var isCathedral))
                {
                    if (placementView != null)
                    {
                        var color = ResolvePlacementColor(placedPlayerId, isCathedral);
                        placementView.AddPlacement(gridMapper, placementResult.AbsoluteCells, color);
                    }

                    _placementRevision++;
                    if (_session.Phase == GamePhase.PlayerTurn)
                    {
                        pieceSelection.ClearSelection();
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

            var step = inputReader.ConsumeSelectStep();
            if (step == 0)
            {
                return;
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
    }
}
