using Peribind.Domain.Board;
using UnityEngine;

namespace Peribind.Unity.Board
{
    public class BoardRaycaster : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private LayerMask boardLayerMask = ~0;
        [SerializeField] private GridMapper gridMapper;
        [SerializeField] private float maxDistance = 200f;

        public bool TryGetCellAtScreenPoint(Vector2 screenPoint, out Cell cell)
        {
            cell = default;
            if (targetCamera == null || gridMapper == null)
            {
                return false;
            }

            var ray = targetCamera.ScreenPointToRay(screenPoint);
            if (!Physics.Raycast(ray, out var hitInfo, maxDistance, boardLayerMask))
            {
                return false;
            }

            return gridMapper.TryWorldToCell(hitInfo.point, out cell);
        }
    }
}
