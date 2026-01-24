using System.Collections.Generic;
using Peribind.Domain.Board;
using UnityEngine;

namespace Peribind.Unity.Board
{
    public class BoardPlacementView : MonoBehaviour
    {
        [SerializeField] private Material pieceMaterial;
        [SerializeField] private Material outlineMaterial;
        [SerializeField] private float yOffset = 0.01f;
        [SerializeField] private float pieceHeight = 0.2f;
        [SerializeField] private float outlineWidth = 0.08f;
        [SerializeField] private float outlineYOffset = 0.01f;

        private MaterialPropertyBlock _propertyBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        public void AddPlacement(GridMapper gridMapper, IReadOnlyList<Cell> cells, Color color)
        {
            if (pieceMaterial == null || cells == null || cells.Count == 0)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            var mesh = PieceMeshBuilder.BuildMesh(cells, gridMapper.CellSize, pieceHeight, out var minCell);
            var instance = new GameObject("PlacedPiece");
            instance.transform.SetParent(transform, false);
            instance.transform.position = gridMapper.CellToWorldMinCorner(minCell, yOffset);
            instance.transform.rotation = Quaternion.identity;
            _spawned.Add(instance);

            var meshFilter = instance.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = instance.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = pieceMaterial;

            var propertyId = GetColorPropertyId(meshRenderer);
            _propertyBlock.SetColor(propertyId, color);
            meshRenderer.SetPropertyBlock(_propertyBlock);

            if (outlineMaterial != null)
            {
                var outlineMesh = new Mesh { name = "PlacedOutlineMesh" };
                PieceOutlineBuilder.BuildOutlineMesh(cells, gridMapper.CellSize, pieceHeight, outlineWidth, outlineYOffset, outlineMesh, out _);

                var outlineObject = new GameObject("Outline");
                outlineObject.transform.SetParent(instance.transform, false);
                outlineObject.transform.localPosition = Vector3.zero;
                outlineObject.transform.localRotation = Quaternion.identity;

                var outlineFilter = outlineObject.AddComponent<MeshFilter>();
                outlineFilter.sharedMesh = outlineMesh;

                var outlineRenderer = outlineObject.AddComponent<MeshRenderer>();
                outlineRenderer.sharedMaterial = outlineMaterial;
            }
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
