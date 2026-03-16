using System.Collections.Generic;
using System.Linq;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;
using UnityEngine;

namespace Peribind.Unity.ScriptableObjects
{
    [CreateAssetMenu(menuName = "Peribind/Piece Definition", fileName = "PieceDefinition")]
    public class PieceDefinitionSO : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private Sprite icon;
        [SerializeField] private GameObject neutralBuildingPrefab;
        [SerializeField] private GameObject playerOneBuildingPrefab;
        [SerializeField] private GameObject playerTwoBuildingPrefab;
        [SerializeField] private Vector3 buildingLocalOffset;
        [SerializeField] private Vector3 buildingLocalRotation;
        [SerializeField] private Vector3 buildingLocalScale = Vector3.one;
        [SerializeField] private List<Vector2Int> cells = new List<Vector2Int>();

        public string Id => string.IsNullOrWhiteSpace(id) ? name : id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public Color Color => color;
        public Sprite Icon => icon;
        public GameObject NeutralBuildingPrefab => neutralBuildingPrefab;
        public GameObject PlayerOneBuildingPrefab => playerOneBuildingPrefab;
        public GameObject PlayerTwoBuildingPrefab => playerTwoBuildingPrefab;
        public Vector3 BuildingLocalOffset => buildingLocalOffset;
        public Vector3 BuildingLocalRotation => buildingLocalRotation;
        public Vector3 BuildingLocalScale => buildingLocalScale;
        public IReadOnlyList<Vector2Int> Cells => cells;

        public bool HasAnyBuildingPrefab =>
            neutralBuildingPrefab != null ||
            playerOneBuildingPrefab != null ||
            playerTwoBuildingPrefab != null;

        public GameObject GetBuildingPrefabForPlayer(int playerId)
        {
            if (playerId == 0 && playerOneBuildingPrefab != null)
            {
                return playerOneBuildingPrefab;
            }

            if (playerId == 1 && playerTwoBuildingPrefab != null)
            {
                return playerTwoBuildingPrefab;
            }

            if (neutralBuildingPrefab != null)
            {
                return neutralBuildingPrefab;
            }

            if (playerOneBuildingPrefab != null)
            {
                return playerOneBuildingPrefab;
            }

            return playerTwoBuildingPrefab;
        }

        public PieceDefinition ToDomainDefinition()
        {
            var domainCells = cells.Select(c => new Cell(c.x, c.y)).ToArray();
            return new PieceDefinition(Id, domainCells);
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                id = name;
            }

            if (buildingLocalScale == Vector3.zero)
            {
                buildingLocalScale = Vector3.one;
            }
        }
    }
}
