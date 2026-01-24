using System.Collections.Generic;
using UnityEngine;

namespace Peribind.Unity.ScriptableObjects
{
    [CreateAssetMenu(menuName = "Peribind/Piece Catalog", fileName = "PieceCatalog")]
    public class PieceCatalogSO : ScriptableObject
    {
        [SerializeField] private List<PieceDefinitionSO> pieces = new List<PieceDefinitionSO>();

        public IReadOnlyList<PieceDefinitionSO> Pieces => pieces;
    }
}
