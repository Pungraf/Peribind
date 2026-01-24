using Peribind.Domain.Board;
using UnityEngine;

namespace Peribind.Unity.Board
{
    public class GridMapper : MonoBehaviour
    {
        [SerializeField] private Transform origin;
        [SerializeField] private float cellSize = 1f;
        [SerializeField] private int width = 10;
        [SerializeField] private int height = 10;

        public int Width => width;
        public int Height => height;
        public float CellSize => cellSize;
        public Transform Origin => origin;

        public bool TryWorldToCell(Vector3 worldPosition, out Cell cell)
        {
            var local = origin != null ? origin.InverseTransformPoint(worldPosition) : worldPosition;
            var x = Mathf.FloorToInt(local.x / cellSize);
            var y = Mathf.FloorToInt(local.z / cellSize);
            cell = new Cell(x, y);
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        public Vector3 CellToWorldCenter(Cell cell, float yOffset = 0f)
        {
            var x = (cell.X + 0.5f) * cellSize;
            var z = (cell.Y + 0.5f) * cellSize;
            var local = new Vector3(x, yOffset, z);
            return origin != null ? origin.TransformPoint(local) : local;
        }

        public Vector3 CellToWorldMinCorner(Cell cell, float yOffset = 0f)
        {
            var x = cell.X * cellSize;
            var z = cell.Y * cellSize;
            var local = new Vector3(x, yOffset, z);
            return origin != null ? origin.TransformPoint(local) : local;
        }
    }
}
