using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PredictedDice.Editor
{
    public class SetupUtility : UnityEditor.Editor
    {
        [MenuItem("GameObject/Setup Predicted Dice Manager")]
        public static void SetupDice()
        {
            ProjectionSceneManager projectionSceneManager = FindObjectOfType<ProjectionSceneManager>();

            if (projectionSceneManager == null)
            {
                GameObject projectionSceneManagerObj = new GameObject("Projection Scene Manager");
                projectionSceneManager = projectionSceneManagerObj.AddComponent<ProjectionSceneManager>();
            }
            else
            {
                EditorGUIUtility.PingObject(projectionSceneManager);
            }

            SetCollisionObjectsFromStatics(projectionSceneManager);
        }

        private static void SetCollisionObjectsFromStatics(ProjectionSceneManager projectionSceneManager)
        {
            GameObject[] objects = GameObject.FindObjectsOfType<GameObject>();
            var gameObjects = objects.Where(x => x.transform.parent == null && x.GetComponent(typeof(Collider))).Where(
                x =>
                    GameObjectUtility.AreStaticEditorFlagsSet(x,
                        StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                        StaticEditorFlags.OccluderStatic |
                        StaticEditorFlags.ReflectionProbeStatic));
            projectionSceneManager.SetCollisionObjects(gameObjects.ToArray());
        }
    }
}