using System.Collections.Generic;

namespace Peribind.Application.Sessions
{
    public class PlayerInventory
    {
        private readonly Dictionary<string, int> _counts;

        public PlayerInventory(Dictionary<string, int> counts)
        {
            _counts = counts;
        }

        public bool HasPiece(string pieceId)
        {
            return _counts.TryGetValue(pieceId, out var count) && count > 0;
        }

        public bool TryConsume(string pieceId)
        {
            if (!HasPiece(pieceId))
            {
                return false;
            }

            _counts[pieceId] -= 1;
            return true;
        }

        public int GetRemainingCount(string pieceId)
        {
            return _counts.TryGetValue(pieceId, out var count) ? count : 0;
        }

        public bool HasAnyPieces()
        {
            foreach (var pair in _counts)
            {
                if (pair.Value > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public IEnumerable<KeyValuePair<string, int>> GetCounts()
        {
            return _counts;
        }

        public void ReturnPiece(string pieceId)
        {
            if (_counts.ContainsKey(pieceId))
            {
                _counts[pieceId] += 1;
                return;
            }

            _counts[pieceId] = 1;
        }
    }
}
