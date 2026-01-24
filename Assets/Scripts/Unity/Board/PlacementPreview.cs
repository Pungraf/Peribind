using System.Collections.Generic;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;
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
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

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
        }

        public void Show(GridMapper gridMapper, PieceDefinition piece, Cell origin, Rotation rotation, bool isValid, Color baseColor)
        {
            if (previewMeshFilter == null || previewMeshRenderer == null)
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

            UpdateOutline(rotatedCells, minCell, gridMapper, worldPosition);
        }

        public void Hide()
        {
            if (previewMeshRenderer != null)
            {
                previewMeshRenderer.enabled = false;
            }

            if (outlineMeshRenderer != null)
            {
                outlineMeshRenderer.enabled = false;
            }
        }

        private void UpdateOutline(IReadOnlyList<Cell> rotatedCells, Cell minCell, GridMapper gridMapper, Vector3 worldPosition)
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
            if (material != null && material.HasProperty(BaseColorId))
            {
                return BaseColorId;
            }

            return ColorId;
        }
    }
}
