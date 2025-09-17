namespace PredictedDice
{ 
    [System.Serializable]
    public struct RollData
    {
        public const int RandomFace = -1;
        public static RollData Default => new RollData
        {
            faceValue = RandomFace,
            force = UnityEngine.Vector3.zero,
            torque = UnityEngine.Vector3.zero
        };
        /// <summary>
        /// -1 for random face
        /// </summary>
        public int faceValue;
        public UnityEngine.Vector3 force;
        public UnityEngine.Vector3 torque;
    }
}