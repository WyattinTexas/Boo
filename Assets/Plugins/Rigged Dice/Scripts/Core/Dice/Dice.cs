using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace PredictedDice
{
    [RequireComponent(typeof(Rigidbody)), AddComponentMenu(DiceUtility.CComponentMenuName + "Dice")]
    public class Dice : MonoBehaviour
    {
        [SerializeField] private bool _usePlaybackTime = false;
        [field: SerializeField, Min(0.001f)] public float PlaybackTime { get; private set; } = 1;
        [SerializeField] private bool hideGraphicObject = true;

        public FaceMap faceMap;
        private IEnumerator _play;

        private bool _isClone;

        public DiceGraphic DiceGraphic { get; private set; }
        private DiceLocomotion _locomotion;
        public DiceSimulation Simulation { get; private set; }
        private ProjectionSceneManager _projectionSceneManager;

        public Pose GetPose() => _locomotion.GetPose();
        private RollData _rollData = RollData.Default;
        public RollData RollData => _rollData;

        public UnityEvent OnRollStart = new();
        public UnityEvent<int> OnRollEnd = new();

        private void SetClone(out GameObject obj)
        {
            enabled = false;
            _isClone = true;
            obj = gameObject;
            foreach (Transform childs in transform)
            {
                if (childs.gameObject != gameObject)
                {
                    DestroyImmediate(childs.gameObject);
                }
            }

            DestroyImmediate(this);
        }

        private void Start()
        {
            if (_isClone) return;
            GetProjectionSceneManager();
            CreateGfx();
            gameObject.DestroyRenderComponents();
            SetupLocomotion();
            SetupProjection();
        }

        private void GetProjectionSceneManager()
        {
            bool isProjectionSceneManagerExist = ProjectionSceneManager.Instance;
            _projectionSceneManager = isProjectionSceneManagerExist
                ? ProjectionSceneManager.Instance
                : new GameObject("ProjectionSceneManager").AddComponent<ProjectionSceneManager>();
            if (!isProjectionSceneManagerExist)
            {
                Debug.LogWarning("ProjectionSceneManager not found, creating a new one");
            }
        }


        private void CreateGfx()
        {
            Instantiate(this, transform, true).SetClone(out var createdGfx);

            if (hideGraphicObject)
                createdGfx.hideFlags = HideFlags.HideInHierarchy;
            else
                createdGfx.hideFlags = HideFlags.NotEditable;

            createdGfx.gameObject.DestroyNonRenderComponents();
            DiceGraphic = createdGfx.AddComponent<DiceGraphic>();
        }

        private void SetupLocomotion()
        {
            _locomotion = gameObject.AddComponent<DiceLocomotion>();
        }

        private void SetupProjection()
        {
            var transformCache = transform;
            Instantiate(this, transformCache.position, transformCache.rotation).SetClone(out var createdSimulation);
            Simulation = createdSimulation.AddComponent<DiceSimulation>();
            Simulation.Setup(faceMap);
            Physics.IgnoreCollision(_locomotion.Collider, Simulation.Collider);
            _projectionSceneManager.AddDice(this);
        }

        private void OnDestroy()
        {
            if (_isClone) return;
            if (_projectionSceneManager)
                _projectionSceneManager.RemoveDice(this);
            if (Simulation)
                Simulation.Destroy();
        }

        public void RollDiceWithOutCome(RollData data)
        {
            if (!enabled || !gameObject.activeInHierarchy) return;
            if (_play != null)
                StopCoroutine(_play);
            Simulation.ResetSimulationDice(this);
            Simulation.Roll(data.force, data.torque);
            _rollData = data;
        }

        public void PlaySimulation()
        {
            if (!_locomotion || !enabled) return;
            if (_rollData.faceValue != RollData.RandomFace)
            {
                ChangeOutcome(_rollData.faceValue);
            }

            OnRollStart?.Invoke();

            if (_usePlaybackTime)
            {
                _play = _locomotion.PlayInTime(Simulation.Trajectory,
                    () => OnRollEnd?.Invoke(_rollData.faceValue), PlaybackTime);
            }
            else
            {
                _play = _locomotion.Play(Simulation.Trajectory, () => OnRollEnd?.Invoke(_rollData.faceValue));
            }

            StartCoroutine(_play);
        }

        private void ChangeOutcome(int faceValue)
        {
            DiceGraphic.ResetRotation();
            Face outcome = faceMap.GetFace(Simulation.Outcome());
            if (outcome.faceValue == faceValue) return;

            var availableFaces = faceMap.GetFaces(faceValue);
            Face targetFace = availableFaces[Random.Range(0, availableFaces.Length)];
            Vector3 changeVec = outcome.faceDirection;
            Vector3 targetDir = targetFace.faceDirection;

            Vector3 rotationAxis = CalculateRotationAxis(changeVec, targetDir);
            float rotationAngle = CalculateRotationAngle(changeVec, targetDir, rotationAxis);

            Quaternion targetRotation = Quaternion.AngleAxis(rotationAngle, -rotationAxis);
            DiceGraphic.ChangeRotation(targetRotation);
        }

        public void SetOutCome(int faceValue)
        {
            DiceGraphic.ResetRotation();
            Face outCome = faceMap.GetFace(Simulation.Outcome());
            if (outCome.faceValue == faceValue) return;

            Face targetFace = faceMap.GetFace(faceValue);
            Vector3 changeVec = outCome.faceDirection;
            Vector3 targetDir = targetFace.faceDirection;

            Vector3 rotationAxis = Vector3.Cross(changeVec, targetDir).normalized;

            float rotationAngle = Mathf.Acos(Vector3.Dot(changeVec.normalized, targetDir.normalized)) * Mathf.Rad2Deg;

            Quaternion targetRotation = Quaternion.AngleAxis(rotationAngle, rotationAxis);
            DiceGraphic.ChangeRotation(targetRotation);
        }

        private Vector3 CalculateRotationAxis(Vector3 changeVec, Vector3 targetDir)
        {
            Vector3 rotationAxis = Vector3.Cross(changeVec, targetDir);
            if (rotationAxis == Vector3.zero)
            {
                rotationAxis = (changeVec == Vector3.up || changeVec == Vector3.down)
                    ? Vector3.Cross(Vector3.right, changeVec)
                    : Vector3.Cross(Vector3.up, changeVec);
            }

            return rotationAxis;
        }

        private float CalculateRotationAngle(Vector3 changeVec, Vector3 targetDir, Vector3 rotationAxis)
        {
            float rotationAngle = Vector3.Angle(changeVec, targetDir);
            if (rotationAxis == Vector3.zero)
            {
                rotationAngle = 180;
            }

            return rotationAngle;
        }

        private int FaceLookingUp() => faceMap.FaceLookingUp(DiceGraphic.transform.localToWorldMatrix);
    }
}