using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PredictedDice
{
    [AddComponentMenu("")]
    public class DiceLocomotion : MonoBehaviour
    {
        private Rigidbody _rb;
        public Rigidbody RB => _rb;

        private Collider _collider;

        public Collider Collider
        {
            get
            {
                if (_collider == null)
                {
                    _collider = GetComponent<Collider>();
                }

                return _collider;
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
        }

        public IEnumerator Play(List<Pose> projectory, UnityAction onComplete)
        {
            Pose[] poses = new Pose[projectory.Count];
            projectory.CopyTo(poses);
            var wait = new WaitForFixedUpdate();
            foreach (var pose in poses)
            {
                _rb.MovePosition(pose.position);
                _rb.MoveRotation(pose.rotation);
                yield return wait;
            }

            onComplete?.Invoke();
        }

        public IEnumerator PlayInTime(List<Pose> projectory, UnityAction onComplete, float completeTime = 1f)
        {
            Pose[] poses = new Pose[projectory.Count];
            projectory.CopyTo(poses);
            int currentPoint = 0;
            int targetPoint = poses.Length - 1;

            float elapsedTime = 0f;
            while (elapsedTime < completeTime)
            {
                float t = elapsedTime / completeTime;
                currentPoint = Mathf.Min((int)(t * targetPoint), targetPoint);
                var pose = poses[currentPoint];
                _rb.MovePosition(pose.position);
                _rb.MoveRotation(pose.rotation);
                elapsedTime += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
            onComplete?.Invoke();
        }

        public void ResetDice(Pose pose)
        {
            _rb.isKinematic = true;
            _rb.position = pose.position;
            _rb.rotation = pose.rotation;
        }

        public void Roll(Vector3 force, Vector3 torque)
        {
            _rb.isKinematic = false;
            _rb.AddForce(force, ForceMode.Impulse);
            _rb.AddTorque(torque, ForceMode.Impulse);
        }

        public Pose GetPose() => new(_rb.position, _rb.rotation);
    }
}