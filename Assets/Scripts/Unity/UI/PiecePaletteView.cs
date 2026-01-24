using System.Collections.Generic;
using Peribind.Unity.Board;
using Peribind.Unity.ScriptableObjects;
using UnityEngine;
using UnityEngine.UI;

namespace Peribind.Unity.UI
{
    public class PiecePaletteView : MonoBehaviour
    {
        [SerializeField] private PieceCatalogSO catalog;
        [SerializeField] private BoardPresenter boardPresenter;
        [SerializeField] private PieceSelection pieceSelection;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private PiecePaletteButton buttonPrefab;
        [SerializeField] private Color disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        private readonly List<PiecePaletteButton> _buttons = new List<PiecePaletteButton>();
        private int _lastRevision = -1;
        private int _lastPlayerId = -1;

        private void Start()
        {
            BuildButtons();
            Refresh();
        }

        private void Update()
        {
            if (boardPresenter == null)
            {
                return;
            }

            if (_lastRevision != boardPresenter.PlacementRevision || _lastPlayerId != boardPresenter.CurrentPlayerId)
            {
                Refresh();
            }
        }

        private void BuildButtons()
        {
            if (catalog == null || contentRoot == null || buttonPrefab == null)
            {
                return;
            }

            foreach (Transform child in contentRoot)
            {
                Destroy(child.gameObject);
            }

            _buttons.Clear();

            foreach (var piece in catalog.Pieces)
            {
                if (piece == null)
                {
                    continue;
                }

                var instance = Instantiate(buttonPrefab, contentRoot);
                instance.gameObject.name = $"PieceButton_{piece.Id}";
                var capturedPiece = piece;
                instance.Button.onClick.AddListener(() => OnPieceClicked(capturedPiece));
                _buttons.Add(instance);
            }
        }

        private void Refresh()
        {
            if (catalog == null || boardPresenter == null || pieceSelection == null)
            {
                return;
            }

            _lastRevision = boardPresenter.PlacementRevision;
            _lastPlayerId = boardPresenter.CurrentPlayerId;

            var canSelect = boardPresenter.IsPlayerTurn;
            var playerColor = boardPresenter.CurrentPlayerColor;

            for (var i = 0; i < catalog.Pieces.Count && i < _buttons.Count; i++)
            {
                var piece = catalog.Pieces[i];
                var button = _buttons[i];
                if (piece == null || button == null)
                {
                    continue;
                }

                var remaining = boardPresenter.GetRemainingCount(piece.Id);
                var hasPiece = remaining > 0;

                if (button.gameObject.activeSelf != hasPiece)
                {
                    button.gameObject.SetActive(hasPiece);
                }

                if (!hasPiece)
                {
                    continue;
                }

                if (button.Label != null)
                {
                    button.Label.text = $"{piece.DisplayName} ({remaining})";
                }

                if (button.ColorSwatch != null)
                {
                    button.ColorSwatch.color = hasPiece ? playerColor : disabledColor;
                }

                if (button.Button != null)
                {
                    button.Button.interactable = canSelect && hasPiece;
                }
            }
        }

        private void OnPieceClicked(PieceDefinitionSO piece)
        {
            if (boardPresenter == null || pieceSelection == null)
            {
                return;
            }

            if (!boardPresenter.IsPlayerTurn || !boardPresenter.HasPieceForCurrentPlayer(piece.Id))
            {
                return;
            }

            pieceSelection.SelectPiece(piece);
            Refresh();
        }
    }
}
