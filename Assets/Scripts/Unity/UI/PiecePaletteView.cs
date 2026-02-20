using System;
using System.Collections.Generic;
using Peribind.Unity.Board;
using Peribind.Unity.ScriptableObjects;
using UnityEngine;
using UIDocument = UnityEngine.UIElements.UIDocument;
using UiButton = UnityEngine.UIElements.Button;
using UiLabel = UnityEngine.UIElements.Label;
using UiScrollView = UnityEngine.UIElements.ScrollView;
using UiVisualElement = UnityEngine.UIElements.VisualElement;
using UiDisplayStyle = UnityEngine.UIElements.DisplayStyle;
using UiStyleBackground = UnityEngine.UIElements.StyleBackground;
using VisualTreeAsset = UnityEngine.UIElements.VisualTreeAsset;
using StyleSheet = UnityEngine.UIElements.StyleSheet;

namespace Peribind.Unity.UI
{
    public class PiecePaletteView : MonoBehaviour
    {
        [SerializeField] private PieceCatalogSO catalog;
        [SerializeField] private BoardPresenter boardPresenter;
        [SerializeField] private PieceSelection pieceSelection;
        [SerializeField] private Color disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Header("UI Toolkit")]
        [SerializeField] private bool enableUiToolkit = true;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private bool autoAssignUiDocument = true;
        [SerializeField] private bool autoAssignVisualTreeFromResources = true;
        [SerializeField] private bool autoAssignStylesFromResources = true;

        private const string GameUxmlResourcePath = "UI/Toolkit/Game/GameHud";
        private const string CommonStyleResourcePath = "UI/Toolkit/Common/PeribindTheme";
        private const string GameHudStyleResourcePath = "UI/Toolkit/Game/GameHud";
        private const string PaletteScrollName = "piece-palette-scroll";

        private readonly List<ToolkitPieceRow> _uiRows = new List<ToolkitPieceRow>();
        private int _lastRevision = -1;
        private int _lastCurrentPlayerId = -1;
        private int _lastBenchPlayerId = -1;
        private bool _lastLocalTurn;

        private UiVisualElement _uiRoot;
        private UiScrollView _uiPaletteScroll;

        private sealed class ToolkitPieceRow
        {
            public PieceDefinitionSO Piece;
            public UiButton Button;
            public UiLabel Label;
            public UiVisualElement ColorSwatch;
            public UiVisualElement Icon;
            public UiVisualElement SelectionOutline;
            public Action ClickHandler;
        }

        private void Start()
        {
            TryBindUiToolkit();
            BuildUiToolkitButtons();
            Refresh();
        }

        private void OnEnable()
        {
            if (enableUiToolkit && _uiRoot == null)
            {
                TryBindUiToolkit();
                BuildUiToolkitButtons();
                Refresh();
            }
        }

        private void OnDestroy()
        {
            ClearUiToolkitRows();
        }

        private void Update()
        {
            if (enableUiToolkit && _uiRoot == null)
            {
                TryBindUiToolkit();
                BuildUiToolkitButtons();
                Refresh();
            }

            if (enableUiToolkit && _uiPaletteScroll == null && _uiRoot != null)
            {
                _uiPaletteScroll = UnityEngine.UIElements.UQueryExtensions.Q<UiScrollView>(_uiRoot, PaletteScrollName);
                if (_uiPaletteScroll != null)
                {
                    BuildUiToolkitButtons();
                    Refresh();
                }
            }

            if (enableUiToolkit && _uiPaletteScroll != null && _uiRows.Count == 0 && catalog != null)
            {
                BuildUiToolkitButtons();
                Refresh();
            }

            if (boardPresenter == null)
            {
                return;
            }

            var currentPlayerId = boardPresenter.CurrentPlayerId;
            var benchPlayerId = boardPresenter.LocalPlayerId;
            var isLocalTurn = boardPresenter.IsLocalPlayerTurn();
            if (_lastRevision != boardPresenter.PlacementRevision ||
                _lastCurrentPlayerId != currentPlayerId ||
                _lastBenchPlayerId != benchPlayerId ||
                _lastLocalTurn != isLocalTurn)
            {
                Refresh();
            }
        }

        private void BuildUiToolkitButtons()
        {
            if (_uiPaletteScroll == null)
            {
                return;
            }

            ClearUiToolkitRows();
            if (catalog == null || catalog.Pieces == null)
            {
                return;
            }

            foreach (var piece in catalog.Pieces)
            {
                if (piece == null)
                {
                    continue;
                }

                var rowButton = new UiButton();
                rowButton.AddToClassList("palette-piece-button");

                var row = new UiVisualElement();
                row.AddToClassList("palette-piece-row");

                var swatch = new UiVisualElement();
                swatch.AddToClassList("palette-piece-swatch");

                var icon = new UiVisualElement();
                icon.AddToClassList("palette-piece-icon");

                var label = new UiLabel();
                label.AddToClassList("palette-piece-label");

                var selectionOutline = new UiVisualElement();
                selectionOutline.AddToClassList("palette-piece-selection");
                selectionOutline.style.display = UiDisplayStyle.None;

                row.Add(swatch);
                row.Add(icon);
                row.Add(label);
                row.Add(selectionOutline);
                rowButton.Add(row);

                var capturedPiece = piece;
                Action clickHandler = () => OnPieceClicked(capturedPiece);
                rowButton.clicked += clickHandler;

                _uiPaletteScroll.Add(rowButton);
                _uiRows.Add(new ToolkitPieceRow
                {
                    Piece = capturedPiece,
                    Button = rowButton,
                    Label = label,
                    ColorSwatch = swatch,
                    Icon = icon,
                    SelectionOutline = selectionOutline,
                    ClickHandler = clickHandler
                });
            }
        }

        private void ClearUiToolkitRows()
        {
            for (var i = 0; i < _uiRows.Count; i++)
            {
                var row = _uiRows[i];
                if (row == null || row.Button == null || row.ClickHandler == null)
                {
                    continue;
                }

                row.Button.clicked -= row.ClickHandler;
            }

            _uiRows.Clear();
            _uiPaletteScroll?.Clear();
        }

        private void Refresh()
        {
            if (catalog == null || boardPresenter == null || pieceSelection == null)
            {
                return;
            }

            _lastRevision = boardPresenter.PlacementRevision;
            _lastCurrentPlayerId = boardPresenter.CurrentPlayerId;
            _lastBenchPlayerId = boardPresenter.LocalPlayerId;
            _lastLocalTurn = boardPresenter.IsLocalPlayerTurn();

            var canSelect = _lastLocalTurn;
            var playerColor = boardPresenter.GetPlayerColor(_lastBenchPlayerId);
            var selectedPiece = pieceSelection.Current;

            for (var i = 0; i < _uiRows.Count; i++)
            {
                var row = _uiRows[i];
                var piece = row != null ? row.Piece : null;
                if (piece == null || row.Button == null)
                {
                    continue;
                }

                var remaining = boardPresenter.GetRemainingCountForPlayer(_lastBenchPlayerId, piece.Id);
                var hasPiece = remaining > 0;
                row.Button.style.display = hasPiece ? UiDisplayStyle.Flex : UiDisplayStyle.None;
                if (!hasPiece)
                {
                    continue;
                }

                if (row.Label != null)
                {
                    row.Label.text = $"{piece.DisplayName} ({remaining})";
                }

                if (row.Icon != null)
                {
                    row.Icon.style.display = piece.Icon != null ? UiDisplayStyle.Flex : UiDisplayStyle.None;
                    if (piece.Icon != null)
                    {
                        row.Icon.style.backgroundImage = new UiStyleBackground(piece.Icon);
                    }
                }

                if (row.ColorSwatch != null)
                {
                    row.ColorSwatch.style.backgroundColor = hasPiece ? playerColor : disabledColor;
                }

                row.Button.SetEnabled(canSelect && hasPiece);

                var isSelected = piece == selectedPiece;
                row.Button.EnableInClassList("palette-piece-button-selected", isSelected);
                if (row.SelectionOutline != null)
                {
                    row.SelectionOutline.style.display = isSelected ? UiDisplayStyle.Flex : UiDisplayStyle.None;
                }
            }
        }

        private void OnPieceClicked(PieceDefinitionSO piece)
        {
            if (boardPresenter == null || pieceSelection == null)
            {
                return;
            }

            var benchPlayerId = boardPresenter.LocalPlayerId;
            if (!boardPresenter.IsLocalPlayerTurn() || !boardPresenter.HasPieceForPlayer(benchPlayerId, piece.Id))
            {
                return;
            }

            pieceSelection.SelectPiece(piece);
            Refresh();
        }

        private void TryBindUiToolkit()
        {
            if (!enableUiToolkit)
            {
                return;
            }

            if (uiDocument == null && autoAssignUiDocument)
            {
                var documents = FindObjectsOfType<UIDocument>(true);
                UIDocument matched = null;
                foreach (var doc in documents)
                {
                    if (ContainsNamedElement(doc, PaletteScrollName))
                    {
                        matched = doc;
                        break;
                    }
                }

                if (matched == null)
                {
                    foreach (var doc in documents)
                    {
                        if (doc != null && doc.visualTreeAsset != null &&
                            string.Equals(doc.visualTreeAsset.name, "GameHud", StringComparison.Ordinal))
                        {
                            matched = doc;
                            break;
                        }
                    }
                }

                if (matched == null && documents != null && documents.Length > 0)
                {
                    matched = documents[0];
                }

                uiDocument = matched;
            }

            if (uiDocument == null)
            {
                return;
            }

            if (autoAssignVisualTreeFromResources && uiDocument.visualTreeAsset == null)
            {
                var tree = Resources.Load<VisualTreeAsset>(GameUxmlResourcePath);
                if (tree == null)
                {
                    Debug.LogWarning($"[PiecePaletteUITK] Missing UXML at Resources/{GameUxmlResourcePath}.uxml");
                    return;
                }

                uiDocument.visualTreeAsset = tree;
            }

            _uiRoot = uiDocument.rootVisualElement;
            if (_uiRoot == null)
            {
                return;
            }

            if (autoAssignStylesFromResources)
            {
                TryAddStyle(_uiRoot, CommonStyleResourcePath);
                TryAddStyle(_uiRoot, GameHudStyleResourcePath);
            }

            _uiPaletteScroll = UnityEngine.UIElements.UQueryExtensions.Q<UiScrollView>(_uiRoot, PaletteScrollName);
            if (_uiPaletteScroll == null)
            {
                Debug.LogWarning($"[PiecePaletteUITK] Missing '{PaletteScrollName}' in bound UIDocument root.");
            }
        }

        private static void TryAddStyle(UiVisualElement root, string resourcePath)
        {
            if (root == null || string.IsNullOrWhiteSpace(resourcePath))
            {
                return;
            }

            var styleSheet = Resources.Load<StyleSheet>(resourcePath);
            if (styleSheet == null)
            {
                return;
            }

            if (!root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        private static bool ContainsNamedElement(UIDocument document, string elementName)
        {
            if (document == null || string.IsNullOrWhiteSpace(elementName))
            {
                return false;
            }

            var root = document.rootVisualElement;
            if (root == null)
            {
                return false;
            }

            return UnityEngine.UIElements.UQueryExtensions.Q<UiVisualElement>(root, elementName) != null;
        }
    }
}

