using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace PredictedDice
{
    [AddComponentMenu(DiceUtility.CComponentMenuName + "Projection Scene Manager")]
    public class ProjectionSceneManager : MonoBehaviour
    {
        public static ProjectionSceneManager Instance { get; private set; }

        [field: SerializeField, Min(0.001f)] public float SimulationSpeed { get; private set; } = 1;

        [SerializeField] private GameObject[] _collisionObjects;
        private Dictionary<int, GameObject> _addedCollisionObjects = new();

        private const int CMaxIterations = 300;
        private const string CPredictionSceneName = "PredictionScene";

        private Scene _predictionScene;
        private PhysicsScene _predictionPhysicsScene;

        private Dictionary<GameObject, Scene> _previousSceneOfObject = new();
        private List<Dice> _diceList = new();

        private bool _isSimulating;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                LoadScene();
                AddToScene(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void SetCollisionObjects(GameObject[] collisionObjects)
        {
            _collisionObjects = collisionObjects;
        }

        private void LoadScene()
        {
            if (_predictionScene.IsValid())
                return;
            CreatePhysicsScene();
        }

        private void CreatePhysicsScene()
        {
            _predictionScene =
                SceneManager.CreateScene(CPredictionSceneName, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _predictionPhysicsScene = _predictionScene.GetPhysicsScene();
            AddScenePhysicsObjects(_collisionObjects);
        }

        public void AddDice(Dice dice)
        {
            if (_diceList.Contains(dice))
                return;
            _diceList.Add(dice);
            SceneManager.MoveGameObjectToScene(dice.Simulation.gameObject, _predictionScene);
        }

        public void RemoveDice(Dice dice)
        {
            if (!_diceList.Contains(dice))
                return;
            _diceList.Remove(dice);
        }

        public void UnloadScene(UnityAction callback = null)
        {
            PutToOriginalScene(gameObject);
            foreach (var addedCollisionObject in _addedCollisionObjects)
            {
                PutToOriginalScene(addedCollisionObject.Value);
            }

            var task = SceneManager.UnloadSceneAsync(_predictionScene);
            task.completed += operation => callback?.Invoke();
        }

        public void Simulate()
        {
            if (_isSimulating)
                return;

            _isSimulating = true;
            const int maxKinematicSteps = 4;
            Dictionary<Dice, int> kinematicIterations = new();
            for (int i = 0; i < CMaxIterations; i++)
            {
                foreach (var dice in _diceList)
                {
                    if (dice.Simulation.IsStationaryOnStep())
                    {
                        if (kinematicIterations.ContainsKey(dice) && kinematicIterations[dice] > maxKinematicSteps)
                        {
                            continue;
                        }

                        if (!kinematicIterations.TryAdd(dice, 1))
                        {
                            kinematicIterations[dice]++;
                            if (kinematicIterations[dice] > maxKinematicSteps)
                            {
                                dice.Simulation.OnStationary?.Invoke();
                            }
                        }
                    }

                    dice.Simulation.AddPoseToTrajectory();
                }

                _predictionPhysicsScene.Simulate(Time.fixedDeltaTime * SimulationSpeed);
            }

            _isSimulating = false;
        }

        private void AddScenePhysicsObjects(GameObject[] targets)
        {
            if (targets == null)
                return;
            foreach (GameObject target in targets)
            {
                if (target.transform.parent != null)
                {
                    if (target.transform.parent == transform) continue;

                    Debug.LogWarning(
                        "Parented objects are not supported for moving. Put them under Projection Scene Manager Object., skipping...",
                        target);

                    continue;
                }

                AddToScene(target);
            }
        }

        private void AddToScene(GameObject target)
        {
            if (_addedCollisionObjects.ContainsKey(target.GetHashCode()))
                return;
            _previousSceneOfObject.Add(target, target.scene);
            _addedCollisionObjects.Add(target.GetHashCode(), target);
            SceneManager.MoveGameObjectToScene(target, _predictionScene);
        }

        private void PutToOriginalScene(GameObject target)
        {
            if (!_addedCollisionObjects.ContainsKey(target.GetHashCode()))
                return;
            SceneManager.MoveGameObjectToScene(target, _previousSceneOfObject[target]);
            _addedCollisionObjects.Remove(target.GetHashCode());
            _previousSceneOfObject.Remove(target);
        }
    }
}