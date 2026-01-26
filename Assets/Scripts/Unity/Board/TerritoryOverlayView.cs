using System.Collections.Generic;
using Peribind.Domain.Board;
using UnityEngine;

namespace Peribind.Unity.Board
{
    public class TerritoryOverlayView : MonoBehaviour
    {
        [SerializeField] private GameObject cellOverlayPrefab;
        [SerializeField] private float yOffset = 0.005f;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        public void SetTerritories(GridMapper gridMapper, Dictionary<int, List<Cell>> territories, Dictionary<int, Color> colors)
        {
            EnsurePoolSize(CountCells(territories));
            var index = 0;

            foreach (var pair in territories)
            {
                var ownerId = pair.Key;
                var color = colors.TryGetValue(ownerId, out var c) ? c : Color.white;

                foreach (var cell in pair.Value)
                {
                    var instance = _spawned[index];
                    instance.transform.position = gridMapper.CellToWorldCenter(cell, yOffset);
                    instance.transform.rotation = Quaternion.identity;
                    instance.SetActive(true);

                var renderer = instance.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    var propertyId = GetColorPropertyId(renderer);
                    _propertyBlock.SetColor(propertyId, color);
                    if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(EmissionColorId))
                    {
                        _propertyBlock.SetColor(EmissionColorId, new Color(color.r, color.g, color.b, 1f));
                    }
                    renderer.SetPropertyBlock(_propertyBlock);
                }

                    index++;
                }
            }

            for (var i = index; i < _spawned.Count; i++)
            {
                _spawned[i].SetActive(false);
            }
        }

        public void ClearAll()
        {
            for (var i = 0; i < _spawned.Count; i++)
            {
                _spawned[i].SetActive(false);
            }
        }

        private static int CountCells(Dictionary<int, List<Cell>> territories)
        {
            var count = 0;
            foreach (var pair in territories)
            {
                count += pair.Value.Count;
            }
            return count;
        }

        private void EnsurePoolSize(int count)
        {
            if (cellOverlayPrefab == null)
            {
                return;
            }

            while (_spawned.Count < count)
            {
                var instance = Instantiate(cellOverlayPrefab, transform);
                instance.SetActive(false);
                _spawned.Add(instance);
            }
        }

        private static int GetColorPropertyId(Renderer renderer)
        {
            var material = renderer.sharedMaterial;
            if (material != null && material.HasProperty(BaseColorId))
            {
                return BaseColorId;
            }

            return ColorId;
        }
    }
}
