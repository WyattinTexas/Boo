using UnityEngine;

namespace PredictedDice.Demo
{
    public class RollADice : MonoBehaviour
    {
        [SerializeField,Range(1,6)] private int _rollOutcome;
        private Camera _camera;
        private void Start()
        {
            _camera = Camera.main;
        }

        public void SetRollOutcome(float outcome)
        {
            _rollOutcome = (int)outcome;
        }
        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                var ray = _camera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out var hit, 100f))
                {
                    if (hit.collider.TryGetComponent<Dice>(out var dice))
                    {
                        dice.RollDiceWithOutCome(new RollData
                        {
                            faceValue = _rollOutcome,
                            force = GetRandomForce(),
                            torque = GetRandomForce()
                        });
                        ProjectionSceneManager.Instance.Simulate();
                        dice.PlaySimulation();
                    }
                }
            }
        }

        private Vector3 GetRandomForce()
        {
            return new Vector3(Random.Range(1, 10), Random.Range(1, 10), Random.Range(1, 10));
        }
    }
}