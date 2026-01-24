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
        [SerializeField] private List<Vector2Int> cells = new List<Vector2Int>();

        public string Id => string.IsNullOrWhiteSpace(id) ? name : id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public Color Color => color;
        public IReadOnlyList<Vector2Int> Cells => cells;

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
        }
    }
}
