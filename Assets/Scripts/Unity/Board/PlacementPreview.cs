using System.Collections.Generic;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;
using Peribind.Unity.ScriptableObjects;
using UnityEngine;

namespace Peribind.Unity.Board
{
    public class PlacementPreview : MonoBehaviour
    {
        [SerializeField] private MeshFilter previewMeshFilter;
        [SerializeField] private MeshRenderer previewMeshRenderer;
        [SerializeField] private MeshFilter outlineMeshFilter;
        [SerializeField] private MeshRenderer outlineMeshRenderer;
        [SerializeField] private Material previewMaterial;
        [SerializeField] private Material outlineMaterial;
        [SerializeField] private float previewAlpha = 0.5f;
        [SerializeField] private Color invalidColor = new Color(0.9f, 0.2f, 0.2f, 0.6f);
        [SerializeField] private float pieceHeight = 0.2f;
        [SerializeField] private float outlineWidth = 0.08f;
        [SerializeField] private float outlineYOffset = 0.01f;
        [SerializeField] private float yOffset = 0.02f;

        private MaterialPropertyBlock _propertyBlock;
        private Mesh _previewMesh;
        private Mesh _outlineMesh;
        private Transform _prefabPreviewRoot;
        private GameObject _prefabPreviewInstance;
        private PieceDefinitionSO _prefabPreviewSource;
        private GameObject _prefabPreviewPrefab;

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            if (previewMeshFilter != null)
            {
                _previewMesh = new Mesh { name = "PreviewPieceMesh" };
                previewMeshFilter.sharedMesh = _previewMesh;
            }

            if (outlineMeshFilter != null)
            {
                _outlineMesh = new Mesh { name = "PreviewOutlineMesh" };
                outlineMeshFilter.sharedMesh = _outlineMesh;
            }

            var prefabRoot = new GameObject("PreviewPrefabRoot");
            prefabRoot.transform.SetParent(transform, false);
            prefabRoot.SetActive(false);
            _prefabPreviewRoot = prefabRoot.transform;
        }

        public void Show(GridMapper gridMapper, PieceDefinitionSO pieceAsset, PieceDefinition piece, Cell origin, Rotation rotation, int playerId, bool isValid, Color baseColor)
        {
            if (gridMapper == null || piece == null)
            {
                return;
            }

            var color = isValid
                ? new Color(baseColor.r, baseColor.g, baseColor.b, previewAlpha)
                : invalidColor;
            var rotatedCells = new List<Cell>();
            foreach (var offset in piece.GetCells(rotation))
            {
                rotatedCells.Add(offset);
            }

            var minCell = PieceVisualUtility.GetMinCell(rotatedCells);
            var outlineWorldPosition = gridMapper.CellToWorldMinCorner(new Cell(origin.X + minCell.X, origin.Y + minCell.Y), yOffset);
            var previewPrefab = pieceAsset != null ? pieceAsset.GetBuildingPrefabForPlayer(playerId) : null;

            if (pieceAsset != null && previewPrefab != null)
            {
                ShowPrefabPreview(gridMapper, pieceAsset, previewPrefab, origin, rotation, rotatedCells);
                HideProceduralPreview();
                if (outlineMeshRenderer != null)
                {
                    outlineMeshRenderer.enabled = false;
                }
            }
            else
            {
                HidePrefabPreview();
                ShowProceduralPreview(gridMapper, rotatedCells, origin, color);
                UpdateOutline(rotatedCells, gridMapper, outlineWorldPosition);
            }
        }

        public void Hide()
        {
            HideProceduralPreview();
            HidePrefabPreview();

            if (outlineMeshRenderer != null)
            {
                outlineMeshRenderer.enabled = false;
            }
        }

        private void ShowProceduralPreview(GridMapper gridMapper, IReadOnlyList<Cell> rotatedCells, Cell origin, Color color)
        {
            if (previewMeshFilter == null || previewMeshRenderer == null)
            {
                return;
            }

            if (_previewMesh == null)
            {
                _previewMesh = new Mesh { name = "PreviewPieceMesh" };
                previewMeshFilter.sharedMesh = _previewMesh;
            }

            PieceMeshBuilder.BuildMesh(rotatedCells, gridMapper.CellSize, pieceHeight, _previewMesh, out var minCell);

            var worldPosition = gridMapper.CellToWorldMinCorner(new Cell(origin.X + minCell.X, origin.Y + minCell.Y), yOffset);
            previewMeshFilter.transform.position = worldPosition;
            previewMeshFilter.transform.rotation = Quaternion.identity;

            if (previewMaterial != null)
            {
                previewMeshRenderer.sharedMaterial = previewMaterial;
            }

            var propertyId = GetColorPropertyId(previewMeshRenderer);
            _propertyBlock.SetColor(propertyId, color);
            previewMeshRenderer.SetPropertyBlock(_propertyBlock);
            previewMeshRenderer.enabled = true;
        }

        private void ShowPrefabPreview(GridMapper gridMapper, PieceDefinitionSO pieceAsset, GameObject buildingPrefab, Cell origin, Rotation rotation, IReadOnlyList<Cell> rotatedCells)
        {
            if (_prefabPreviewRoot == null)
            {
                return;
            }

            EnsurePrefabPreviewInstance(pieceAsset, buildingPrefab);
            if (_prefabPreviewInstance == null)
            {
                return;
            }

            var absoluteCells = new List<Cell>(rotatedCells.Count);
            for (var i = 0; i < rotatedCells.Count; i++)
            {
                absoluteCells.Add(rotatedCells[i] + origin);
            }

            _prefabPreviewRoot.position = PieceVisualUtility.GetFootprintWorldCenter(gridMapper, absoluteCells, yOffset);
            _prefabPreviewRoot.rotation = PieceVisualUtility.GetWorldRotation(rotation);
            _prefabPreviewRoot.gameObject.SetActive(true);
        }

        private void HideProceduralPreview()
        {
            if (previewMeshRenderer != null)
            {
                previewMeshRenderer.enabled = false;
            }
        }

        private void HidePrefabPreview()
        {
            if (_prefabPreviewRoot != null)
            {
                _prefabPreviewRoot.gameObject.SetActive(false);
            }
        }

        private void EnsurePrefabPreviewInstance(PieceDefinitionSO pieceAsset, GameObject buildingPrefab)
        {
            if (_prefabPreviewSource == pieceAsset && _prefabPreviewPrefab == buildingPrefab && _prefabPreviewInstance != null)
            {
                return;
            }

            if (_prefabPreviewInstance != null)
            {
                Destroy(_prefabPreviewInstance);
                _prefabPreviewInstance = null;
            }

            _prefabPreviewSource = pieceAsset;
            _prefabPreviewPrefab = buildingPrefab;
            if (pieceAsset == null || buildingPrefab == null || _prefabPreviewRoot == null)
            {
                return;
            }

            _prefabPreviewInstance = Instantiate(buildingPrefab, _prefabPreviewRoot);
            _prefabPreviewInstance.transform.localPosition = pieceAsset.BuildingLocalOffset;
            _prefabPreviewInstance.transform.localRotation = Quaternion.Euler(pieceAsset.BuildingLocalRotation);
            _prefabPreviewInstance.transform.localScale = pieceAsset.BuildingLocalScale;
        }

        private void UpdateOutline(IReadOnlyList<Cell> rotatedCells, GridMapper gridMapper, Vector3 worldPosition)
        {
            if (outlineMeshFilter == null || outlineMeshRenderer == null)
            {
                return;
            }

            if (_outlineMesh == null)
            {
                _outlineMesh = new Mesh { name = "PreviewOutlineMesh" };
                outlineMeshFilter.sharedMesh = _outlineMesh;
            }

            PieceOutlineBuilder.BuildOutlineMesh(rotatedCells, gridMapper.CellSize, pieceHeight, outlineWidth, outlineYOffset, _outlineMesh, out _);
            outlineMeshFilter.transform.position = worldPosition;
            outlineMeshFilter.transform.rotation = Quaternion.identity;

            if (outlineMaterial != null)
            {
                outlineMeshRenderer.sharedMaterial = outlineMaterial;
            }

            outlineMeshRenderer.enabled = true;
        }

        private static int GetColorPropertyId(Renderer renderer)
        {
            var material = renderer.sharedMaterial;
            if (material != null && material.HasProperty("_BaseColor"))
            {
                return Shader.PropertyToID("_BaseColor");
            }

            return Shader.PropertyToID("_Color");
        }
    }
}
