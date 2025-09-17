using UnityEngine;

namespace PredictedDice
{
    public static class DiceUtility
    {
        public const string CComponentMenuName = "Predicted Dice Roller/";
        private static Color[] FaceColors = new Color[]
        {
            Color.red,
            Color.green,
            Color.blue,
            Color.yellow,
            Color.cyan,
            Color.magenta,
        };
        public static Color GetColor(int faceValue)
        {
            return FaceColors[faceValue % FaceColors.Length];
        }
        public static void DestroyNonRenderComponents(this GameObject self)
        {
            foreach (Component component in self.GetComponents(typeof(Component)))
            {
                if (component is Transform) continue;
                if (!(component is MeshRenderer || component is MeshFilter))
                    Object.DestroyImmediate(component);
            }
        }
        public static void DestroyRenderComponents(this GameObject self)
        {
            foreach (Component component in self.GetComponents(typeof(Component)))
            {
                if (component is Transform) continue;
                if (component is MeshRenderer || component is MeshFilter)
                    Object.DestroyImmediate(component);
            }
        }
    }
}