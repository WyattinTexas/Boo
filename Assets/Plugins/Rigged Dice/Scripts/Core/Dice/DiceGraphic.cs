using UnityEngine;

namespace PredictedDice
{
    [AddComponentMenu("")]
    public class DiceGraphic : MonoBehaviour
    {
        private Transform _transform;

        private void Awake()
        {
            _transform = transform;
        }

        public void ResetRotation()
        {
            _transform.localRotation = Quaternion.identity;
        }

        public void ChangeRotation(Quaternion rotation)
        {
            _transform.localRotation = rotation;
        }
    }
}