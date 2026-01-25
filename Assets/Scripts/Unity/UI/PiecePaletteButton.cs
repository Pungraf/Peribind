using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Peribind.Unity.UI
{
    public class PiecePaletteButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Image colorSwatch;
        [SerializeField] private Image iconImage;
        [SerializeField] private Image selectionOutline;

        public Button Button => button;
        public TextMeshProUGUI Label => label;
        public Image ColorSwatch => colorSwatch;
        public Image IconImage => iconImage;
        public Image SelectionOutline => selectionOutline;

        private void Reset()
        {
            button = GetComponent<Button>();
            label = GetComponentInChildren<TextMeshProUGUI>();
            var images = GetComponentsInChildren<Image>(true);
            foreach (var image in images)
            {
                if (image.name.ToLowerInvariant().Contains("swatch") || image.name.ToLowerInvariant().Contains("color"))
                {
                    colorSwatch = image;
                }
                else if (image.name.ToLowerInvariant().Contains("icon"))
                {
                    iconImage = image;
                }
                else if (image.name.ToLowerInvariant().Contains("outline") || image.name.ToLowerInvariant().Contains("selection"))
                {
                    selectionOutline = image;
                }
            }
        }
    }
}
