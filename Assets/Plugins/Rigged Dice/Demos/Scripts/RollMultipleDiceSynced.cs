using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PredictedDice.Demo
{
    public class RollMultipleDiceSynced : MonoBehaviour
    {
        [Serializable]
        private struct DiceAndOutcome
        {
            public Dice dice;
            public bool random;
            [Range(1, 6)] public int outcome;
        }

        [SerializeField] private DiceAndOutcome[] diceAndOutcomeArray;

        public void SetWhiteDiceOutcome(float outcome)
        {
            diceAndOutcomeArray[0].outcome = (int)outcome;
        }

        public void SetBlueDiceOutcome(float outcome)
        {
            diceAndOutcomeArray[1].outcome = (int)outcome;
        }

        public void SetRedDiceOutcome(float outcome)
        {
            diceAndOutcomeArray[2].outcome = (int)outcome;
        }

        public void RollAll()
        {
            foreach (var diceAndOutcome in diceAndOutcomeArray)
            {
                diceAndOutcome.dice.RollDiceWithOutCome(
                    GetRandomForcedRollData(diceAndOutcome.random ? RollData.RandomFace : diceAndOutcome.outcome));
            }

            ProjectionSceneManager.Instance.Simulate();
            foreach (DiceAndOutcome diceAndOutcome in diceAndOutcomeArray)
            {
                diceAndOutcome.dice.PlaySimulation();
            }
        }

        private RollData GetRandomForcedRollData(int outcome = RollData.RandomFace)
        {
            return new RollData
            {
                faceValue = outcome,
                force = GetRandomForce(),
                torque = GetRandomForce()
            };
        }
        private Vector3 GetRandomForce()
        {
            return new Vector3(Random.Range(1, 10), Random.Range(1, 10), Random.Range(1, 10));
        }
    }
}