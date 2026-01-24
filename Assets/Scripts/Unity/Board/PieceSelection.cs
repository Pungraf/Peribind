using Peribind.Unity.ScriptableObjects;
using UnityEngine;

namespace Peribind.Unity.Board
{
    public class PieceSelection : MonoBehaviour
    {
        [SerializeField] private PieceCatalogSO catalog;
        [SerializeField] private int selectedIndex;

        public PieceDefinitionSO Current
        {
            get
            {
                if (catalog == null || catalog.Pieces.Count == 0)
                {
                    return null;
                }

                if (selectedIndex < 0 || selectedIndex >= catalog.Pieces.Count)
                {
                    return null;
                }

                return catalog.Pieces[selectedIndex];
            }
        }

        public void SelectNext()
        {
            if (catalog == null || catalog.Pieces.Count == 0)
            {
                return;
            }

            if (selectedIndex < 0)
            {
                selectedIndex = 0;
                return;
            }

            selectedIndex = (selectedIndex + 1) % catalog.Pieces.Count;
        }

        public void SelectPrevious()
        {
            if (catalog == null || catalog.Pieces.Count == 0)
            {
                return;
            }

            if (selectedIndex < 0)
            {
                selectedIndex = catalog.Pieces.Count - 1;
                return;
            }

            selectedIndex = (selectedIndex - 1 + catalog.Pieces.Count) % catalog.Pieces.Count;
        }

        public int Count => catalog != null ? catalog.Pieces.Count : 0;

        public void SelectPiece(PieceDefinitionSO piece)
        {
            if (catalog == null || piece == null)
            {
                return;
            }

            for (var i = 0; i < catalog.Pieces.Count; i++)
            {
                if (catalog.Pieces[i] == piece)
                {
                    selectedIndex = i;
                    return;
                }
            }
        }

        public void ClearSelection()
        {
            selectedIndex = -1;
        }
    }
}
