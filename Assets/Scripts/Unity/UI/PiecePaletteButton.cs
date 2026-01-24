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

        public Button Button => button;
        public TextMeshProUGUI Label => label;
        public Image ColorSwatch => colorSwatch;

        private void Reset()
        {
            button = GetComponent<Button>();
            label = GetComponentInChildren<TextMeshProUGUI>();
            colorSwatch = GetComponentInChildren<Image>();
        }
    }
}
