using System;
using System.Collections.Generic;
using UnityEngine;

namespace Peribind.Unity.ScriptableObjects
{
    [CreateAssetMenu(menuName = "Peribind/Piece Set", fileName = "PieceSet")]
    public class PieceSetSO : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public PieceDefinitionSO piece;
            public int count;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;
    }
}
