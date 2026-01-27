using System.Collections.Generic;
using Peribind.Domain.Board;

namespace Peribind.Application.Sessions
{
    public class GameSessionSnapshot
    {
        public int CurrentPlayerId { get; set; }
        public GamePhase Phase { get; set; }
        public int CurrentRound { get; set; }
        public int RoundRevision { get; set; }
        public bool IsGameOver { get; set; }
        public int[] TotalScores { get; set; }
        public bool[] FinishedThisRound { get; set; }
        public Dictionary<string, int>[] Inventories { get; set; }
        public List<PlacedPieceSnapshot> PlacedPieces { get; set; }
        public List<Cell>[] ClaimedTerritories { get; set; }
    }

    public class PlacedPieceSnapshot
    {
        public int InstanceId { get; set; }
        public int PlayerId { get; set; }
        public string PieceId { get; set; }
        public bool IsCathedral { get; set; }
        public List<Cell> Cells { get; set; }
    }
}
