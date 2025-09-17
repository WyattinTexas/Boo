using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PredictedDice.Demo
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class SetTextWithSlider : MonoBehaviour
    {
        private TextMeshProUGUI _text;
        private Slider _slider;

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
            _slider = GetComponentInParent<Slider>();
            _slider.onValueChanged.AddListener(UpdateText);
        }

        private void UpdateText(float value)
        {
            _text.text = value.ToString();
        }
    }
}
