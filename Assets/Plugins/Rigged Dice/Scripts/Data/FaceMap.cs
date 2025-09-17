using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace PredictedDice
{
    [Serializable]
    public class FaceMap
    {
        [SerializeField] private List<Face> faces = new();

        public List<Face> Faces => faces;
        
        public void AddFace(Face face)
        {
            faces.Add(face);
        }

        public Face GetFace(int faceValue)
        {
            return faces.FirstOrDefault(x => x.faceValue == faceValue);
        }
        public Face[] GetFaces(int faceValue)
        {
            return faces.Where(x => x.faceValue == faceValue).ToArray();
        }

        public int FaceLookingUp(Matrix4x4 matrix4X4)
        {
            var maxDot = -1f;
            var faceValue = RollData.RandomFace;
            for (int i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                var directionUp = matrix4X4.MultiplyVector(face.faceDirection);
                var dot = Vector3.Dot(Vector3.up, directionUp);
                if (dot > maxDot)
                {
                    maxDot = dot;
                    faceValue = face.faceValue;
                }
            }

            return faceValue;
        }

        public Vector3 GetFaceDirection(int faceValue)
        {
            var face = faces.FirstOrDefault(x => x.faceValue == faceValue);
            return face.faceDirection;
        }
    }

    [Serializable]
    public struct Face
    {
        public int faceValue;
        public Vector3 faceDirection;
        public Face(int faceValue, Vector3 faceDirection)
        {
            this.faceValue = faceValue;
            this.faceDirection = faceDirection;
        }
    }
}