using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PredictedDice
{
    [AddComponentMenu("")]
    public class DiceSimulation : MonoBehaviour
    {
        private FaceMap _faceMap;
        private DiceLocomotion _locomotion;

        private readonly List<Pose> _trajectory = new List<Pose>();
        public List<Pose> Trajectory => _trajectory;
        public Collider Collider => _locomotion.Collider;
        public DiceLocomotion Locomotion => _locomotion;
        public UnityAction OnStationary = delegate { };

        public void Setup(FaceMap faceMap)
        {
            _faceMap = faceMap;
            _locomotion = GetComponent<DiceLocomotion>();
        }

        public void Roll(Vector3 force, Vector3 torque)
        {
            _locomotion.Roll(force, torque);
        }

        public void AddPoseToTrajectory()
        {
            _trajectory.Add(_locomotion.GetPose());
        }

        public bool IsStationaryOnStep()
        {
            return ApproxEqual(Locomotion.RB.angularVelocity, Vector3.zero) &&
                   ApproxEqual(Locomotion.RB.velocity, Vector3.zero);
        }

        private bool ApproxEqual(Vector3 a, Vector3 b)
        {
            return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y) && Mathf.Approximately(a.z, b.z);
        }

        public void ResetSimulationDice(Dice dice)
        {
            _trajectory.Clear();
            _locomotion.ResetDice(dice.GetPose());
            _faceMap = dice.faceMap;
        }

        public void Destroy()
        {
            if (!gameObject) return;
            Destroy(gameObject);
        }

        public int Outcome()
        {
            var face = _faceMap.FaceLookingUp(transform.localToWorldMatrix);
            return face;
        }
    }
}