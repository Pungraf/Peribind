using UnityEngine;

namespace Peribind.Unity.ScriptableObjects
{
    [CreateAssetMenu(menuName = "Peribind/Game Config", fileName = "GameConfig")]
    public class GameConfigSO : ScriptableObject
    {
        [SerializeField] private PieceSetSO playerOnePieceSet;
        [SerializeField] private PieceSetSO playerTwoPieceSet;
        [SerializeField] private PieceDefinitionSO cathedralPiece;
        [SerializeField] private Color playerOneColor = new Color(0.2f, 0.4f, 0.9f, 1f);
        [SerializeField] private Color playerTwoColor = new Color(0.9f, 0.4f, 0.2f, 1f);
        [SerializeField] private Color cathedralColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        public PieceSetSO PlayerOnePieceSet => playerOnePieceSet;
        public PieceSetSO PlayerTwoPieceSet => playerTwoPieceSet;
        public PieceDefinitionSO CathedralPiece => cathedralPiece;
        public Color PlayerOneColor => playerOneColor;
        public Color PlayerTwoColor => playerTwoColor;
        public Color CathedralColor => cathedralColor;
    }
}
