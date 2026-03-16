using System.Collections.Generic;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;
using Peribind.Unity.ScriptableObjects;
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
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        public void AddPlacement(GridMapper gridMapper, PieceDefinitionSO pieceAsset, IReadOnlyList<Cell> cells, Rotation rotation, int playerId, Color color)
        {
            if (gridMapper == null || cells == null || cells.Count == 0)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            var instance = new GameObject("PlacedPiece");
            instance.transform.SetParent(transform, false);
            _spawned.Add(instance);
            var buildingPrefab = pieceAsset != null ? pieceAsset.GetBuildingPrefabForPlayer(playerId) : null;

            if (pieceAsset != null && buildingPrefab != null)
            {
                AddBuildingPlacement(instance.transform, gridMapper, pieceAsset, buildingPrefab, cells, rotation);
            }
            else
            {
                AddProceduralPlacement(instance.transform, gridMapper, cells, color);
                AddOutline(instance.transform, gridMapper, cells);
            }
        }

        public void ClearAll()
        {
            foreach (var instance in _spawned)
            {
                if (instance != null)
                {
                    Destroy(instance);
                }
            }

            _spawned.Clear();
        }

        private void AddProceduralPlacement(Transform parent, GridMapper gridMapper, IReadOnlyList<Cell> cells, Color color)
        {
            if (pieceMaterial == null)
            {
                Debug.LogWarning($"[BoardPlacementView] Missing pieceMaterial on '{name}'. Placement skipped.");
                return;
            }

            var mesh = PieceMeshBuilder.BuildMesh(cells, gridMapper.CellSize, pieceHeight, out var minCell);
            var pieceObject = new GameObject("PieceMesh");
            pieceObject.transform.SetParent(parent, false);
            pieceObject.transform.position = gridMapper.CellToWorldMinCorner(minCell, yOffset);
            pieceObject.transform.rotation = Quaternion.identity;

            var meshFilter = pieceObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = pieceObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = pieceMaterial;

            _propertyBlock.Clear();
            var propertyId = GetColorPropertyId(meshRenderer);
            _propertyBlock.SetColor(propertyId, color);
            meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void AddBuildingPlacement(Transform parent, GridMapper gridMapper, PieceDefinitionSO pieceAsset, GameObject buildingPrefab, IReadOnlyList<Cell> cells, Rotation rotation)
        {
            var visualRoot = new GameObject("PieceVisual");
            visualRoot.transform.SetParent(parent, false);
            visualRoot.transform.position = PieceVisualUtility.GetFootprintWorldCenter(gridMapper, cells, yOffset);
            visualRoot.transform.rotation = PieceVisualUtility.GetWorldRotation(rotation);

            var buildingInstance = Instantiate(buildingPrefab, visualRoot.transform);
            buildingInstance.transform.localPosition = pieceAsset.BuildingLocalOffset;
            buildingInstance.transform.localRotation = Quaternion.Euler(pieceAsset.BuildingLocalRotation);
            buildingInstance.transform.localScale = pieceAsset.BuildingLocalScale;
        }

        private void AddOutline(Transform parent, GridMapper gridMapper, IReadOnlyList<Cell> cells)
        {
            if (outlineMaterial == null)
            {
                return;
            }

            var outlineMesh = new Mesh { name = "PlacedOutlineMesh" };
            PieceOutlineBuilder.BuildOutlineMesh(cells, gridMapper.CellSize, pieceHeight, outlineWidth, outlineYOffset, outlineMesh, out _);

            var outlineObject = new GameObject("Outline");
            outlineObject.transform.SetParent(parent, false);
            outlineObject.transform.position = gridMapper.CellToWorldMinCorner(PieceVisualUtility.GetMinCell(cells), 0f);
            outlineObject.transform.rotation = Quaternion.identity;

            var outlineFilter = outlineObject.AddComponent<MeshFilter>();
            outlineFilter.sharedMesh = outlineMesh;

            var outlineRenderer = outlineObject.AddComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = outlineMaterial;
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
