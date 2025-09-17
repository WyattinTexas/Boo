using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PredictedDice.Editor
{
    [CustomEditor(typeof(Dice))]
    public class DiceEditor : UnityEditor.Editor
    {
        enum SelectionType
        {
            Face,
            Vertex,
            Edge,
            FreeForm,
            Collider
        }

        private Dice _dice;
        private Mesh _mesh;
        private Collider _collider;
        private bool _isEditingFaces;
        private SelectionType _selectionType;
        Dictionary<Vector3, Vector3> normalToCenter = new Dictionary<Vector3, Vector3>();
        Dictionary<Vector3, int> normalToCount = new Dictionary<Vector3, int>();
        private bool _useCachedData = true;
        private bool _isMeshDirty = true;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                GUIStyle editButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    richText = true
                };
                if (GUILayout.Button(_isEditingFaces ? "Save" : "Edit Faces", editButtonStyle))
                {
                    _isEditingFaces = !_isEditingFaces;
                    if (_isEditingFaces && Selection.activeGameObject &&
                        Selection.activeGameObject.TryGetComponent<MeshFilter>(out var meshFilter))
                    {
                        _mesh = meshFilter.sharedMesh;
                    }

                    if (!_isEditingFaces)
                    {
                        EditorUtility.SetDirty(_dice);
                        AssetDatabase.SaveAssets();
                    }

                    SceneView.RepaintAll();
                }

                if (_isEditingFaces)
                {
                    _selectionType = (SelectionType)EditorGUILayout.EnumPopup("Selection Type", _selectionType);

                    EditorGUI.BeginChangeCheck();
                    _useCachedData = EditorGUILayout.Toggle("Use Cached Data", _useCachedData);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _isMeshDirty = true;
                        SceneView.RepaintAll();
                    }

                    if (GUILayout.Button("Clear Cache"))
                    {
                        _isMeshDirty = true;
                        normalToCenter.Clear();
                        normalToCount.Clear();
                        SceneView.RepaintAll();
                    }
                }
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying) return;
            _dice = (Dice)target;
            if (_dice.TryGetComponent<MeshFilter>(out var meshFilter))
                _mesh = meshFilter.sharedMesh;
            else
                Debug.LogWarning("MeshFilter not found in the Dice");

            _collider = _dice.GetComponent<Collider>();
            if (_collider == null)
                Debug.LogWarning("Collider not found in the Dice");

            _isMeshDirty = true;
        }

        private void OnSceneGUI()
        {
            // No longer require mesh for all edit modes
            DiceFaceSelectionEditor();
        }

        private void DiceFaceSelectionEditor()
        {
            if (!_isEditingFaces) return;

            // Only require mesh for mesh-based editing modes
            bool needsMesh = _selectionType == SelectionType.Vertex || 
                            _selectionType == SelectionType.Edge || 
                            _selectionType == SelectionType.Face;

            if (needsMesh && !_mesh) return;

            // Handle scene view events for better interaction
            HandleSceneViewEvents();

            if (needsMesh)
            {
                Graphics.DrawMeshNow(_mesh, _dice.transform.position, _dice.transform.rotation);
            }

            var previousMatrix = Handles.matrix;
            Handles.matrix = _dice.transform.localToWorldMatrix;

            if (needsMesh)
            {
                DrawEdges();
            }

            switch (_selectionType)
            {
                case SelectionType.Vertex:
                    EditVertices();
                    break;
                case SelectionType.Edge:
                    EditEdges();
                    break;
                case SelectionType.Face:
                    EditFaces();
                    break;
                case SelectionType.FreeForm:
                    EditFreeForm();
                    break;
                case SelectionType.Collider:
                    EditCollider();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Handles.matrix = previousMatrix;
        }

        private void HandleSceneViewEvents()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return;

            // Force scene view to repaint on mouse move for smoother interaction
            Event e = Event.current;
            if (e.type == EventType.MouseMove)
            {
                sceneView.Repaint();
            }

            // Debug mode to visualize the dice faces and positions
            if (_isEditingFaces && _selectionType == SelectionType.Collider)
            {
                Handles.color = Color.yellow;

                // Draw lines from center to each face to help visualize positions
                if (_dice.faceMap != null && _dice.faceMap.Faces != null)
                {
                    Vector3 center = _dice.transform.TransformPoint(Vector3.zero);
                    foreach (var face in _dice.faceMap.Faces)
                    {
                        Vector3 worldPos = _dice.transform.TransformPoint(face.faceDirection);
                        Handles.DrawLine(center, worldPos);

                        // Draw a small sphere at the exact face position
                        Handles.color = Color.red;
                        float size = HandleUtility.GetHandleSize(worldPos) * 0.1f;
                        Handles.SphereHandleCap(0, worldPos, Quaternion.identity, size, EventType.Repaint);
                        Handles.color = Color.yellow;
                    }
                }
            }
        }

        private void EditFreeForm()
        {
            //Ray from SceneView camera to mouse position
            Ray camRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

            int layerMask = 1 << _dice.gameObject.layer;
            if (Physics.Raycast(camRay, out var hitDebug, float.MaxValue, layerMask, QueryTriggerInteraction.Ignore))
            {
                if (hitDebug.transform == _dice.transform || hitDebug.transform.IsChildOf(_dice.transform))
                {
                    Handles.matrix = Matrix4x4.identity;

                    // Round normal to the nearest axis for standard dice faces
                    Vector3 normal = hitDebug.normal;
                    Vector3 roundedNormal = new Vector3(
                        Mathf.Round(normal.x),
                        Mathf.Round(normal.y),
                        Mathf.Round(normal.z));

                    if (roundedNormal.magnitude > 0)
                    {
                        roundedNormal.Normalize();
                    }
                    else
                    {
                        roundedNormal = normal;
                    }

                    // Convert to local space
                    Vector3 localPoint = _dice.transform.InverseTransformPoint(hitDebug.point);
                    Vector3 localNormal = _dice.transform.InverseTransformDirection(hitDebug.normal); // Use exact normal

                    if (DrawButton(hitDebug.point, "Free Form: " + localNormal,
                            _dice.faceMap.Faces.Any(x => Vector3.Distance(x.faceDirection, localNormal) < 0.1f)))
                    {
                        OpenWindowForFaceIndexSet((faceIndex) =>
                        {
                            // Use exact local point as face direction
                            _dice.faceMap.AddFace(new Face(faceIndex, localPoint));
                        });
                    }

                    Handles.matrix = _dice.transform.localToWorldMatrix;
                    SceneView.RepaintAll();
                }
            }
        }


        private void EditVertices()
        {
            // For better performance, only show vertices for standard dice directions (6 main axes)
            Vector3[] directions = new Vector3[] 
            { 
                Vector3.up, Vector3.down, 
                Vector3.left, Vector3.right,
                Vector3.forward, Vector3.back 
            };

            foreach (var direction in directions)
            {
                // Find the closest vertex to this direction
                Vector3 closestVertex = Vector3.zero;
                float closestDistance = float.MaxValue;

                foreach (var vertex in _mesh.vertices)
                {
                    float distance = Vector3.Distance(vertex.normalized, direction);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestVertex = vertex;
                    }
                }

                if (DrawButton(closestVertex, "Vertex: " + direction, 
                        _dice.faceMap.Faces.Any(x => Vector3.Distance(x.faceDirection.normalized, direction) < 0.1f)))
                {
                    OpenWindowForFaceIndexSet((faceIndex) => { _dice.faceMap.AddFace(new Face(faceIndex, direction)); });
                }
            }
        }

        private void EditEdges()
        {
            // Only process a subset of triangles for better performance
            int maxTriangles = 20; // Limit the number of triangles for performance
            int step = Mathf.Max(1, _mesh.triangles.Length / 3 / maxTriangles);

            for (int i = 0; i < _mesh.triangles.Length; i += 3 * step)
            {
                int index1 = _mesh.triangles[i];
                int index2 = _mesh.triangles[i + 1];
                int index3 = _mesh.triangles[i + 2];

                Vector3 vertex1 = _mesh.vertices[index1];
                Vector3 vertex2 = _mesh.vertices[index2];
                Vector3 vertex3 = _mesh.vertices[index3];

                Vector3 center = (vertex1 + vertex2 + vertex3) / 3;
                Vector3 normal = Vector3.Cross(vertex2 - vertex1, vertex3 - vertex1).normalized;

                // Round normal to the nearest axis for standard dice faces
                Vector3 roundedNormal = new Vector3(
                    Mathf.Round(normal.x),
                    Mathf.Round(normal.y),
                    Mathf.Round(normal.z));

                if (roundedNormal.magnitude > 0)
                {
                    roundedNormal.Normalize();
                    normal = roundedNormal;
                }

                if (DrawButton(center, "Edge: " + normal, 
                        _dice.faceMap.Faces.Any(x => Vector3.Distance(x.faceDirection, normal) < 0.1f)))
                {
                    OpenWindowForFaceIndexSet((faceIndex) => { _dice.faceMap.AddFace(new Face(faceIndex, normal)); });
                }
            }
        }

        private void EditFaces()
        {
            if (_isMeshDirty || normalToCenter.Count == 0)
            {
                normalToCenter.Clear();
                normalToCount.Clear();
                GetFaceDictionaries();
                _isMeshDirty = false;
            }

            foreach (var kvp in normalToCenter)
            {
                var combinedCenter = kvp.Value / normalToCount[kvp.Key];
                if (DrawButton(combinedCenter, "Face " + kvp.Key,
                        _dice.faceMap.Faces.Any(x => x.faceDirection == kvp.Key)))
                {
                    OpenWindowForFaceIndexSet((faceIndex) => { _dice.faceMap.AddFace(new Face(faceIndex, kvp.Key)); });
                }
            }
        }

        private void EditCollider()
        {
            if (_collider == null) return;

            // Standard dice directions
            Vector3[] standardDirections = new Vector3[] 
            { 
                Vector3.up, Vector3.down, 
                Vector3.left, Vector3.right,
                Vector3.forward, Vector3.back 
            };

            // Define probe points for each direction and draw handles at those points
            List<Vector3> handlePositions = new List<Vector3>();
            Dictionary<Vector3, Vector3> handleToDirection = new Dictionary<Vector3, Vector3>();

            // For BoxCollider - handles at the center of each face
            if (_collider is BoxCollider boxCollider)
            {
                foreach (var direction in standardDirections)
                {
                    // Calculate position on box face
                    Vector3 size = boxCollider.size * 0.5f;
                    Vector3 directionAbs = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
                    Vector3 faceCenter = boxCollider.center + Vector3.Scale(size, direction);

                    // Store actual position without normalization
                    handlePositions.Add(faceCenter);
                    handleToDirection[faceCenter] = direction;
                }
            }
            // For SphereCollider - handles at equidistant points on sphere surface
            else if (_collider is SphereCollider sphereCollider)
            {
                foreach (var direction in standardDirections)
                {
                    Vector3 pointOnSphere = sphereCollider.center + direction * sphereCollider.radius;
                    handlePositions.Add(pointOnSphere);
                    handleToDirection[pointOnSphere] = direction;
                }
            }
            // For CapsuleCollider - handles at key points
            else if (_collider is CapsuleCollider capsuleCollider)
            {
                // Direction of capsule axis
                Vector3 capsuleDir = Vector3.zero;
                switch (capsuleCollider.direction)
                {
                    case 0: capsuleDir = Vector3.right; break; // X
                    case 1: capsuleDir = Vector3.up; break;    // Y
                    case 2: capsuleDir = Vector3.forward; break; // Z
                }

                // Height of capsule without the end caps
                float capsuleHeight = capsuleCollider.height - capsuleCollider.radius * 2;
                if (capsuleHeight < 0) capsuleHeight = 0;

                // Add points along the capsule's main axis
                for (int i = -1; i <= 1; i += 2) // -1 and 1 (ends)
                {
                    Vector3 endPoint = capsuleCollider.center + capsuleDir * (capsuleHeight * 0.5f * i);

                    foreach (var direction in standardDirections)
                    {
                        // Skip directions that are aligned with the capsule axis
                        if (Mathf.Abs(Vector3.Dot(direction, capsuleDir)) > 0.9f) continue;

                        Vector3 rayDir = direction.normalized;
                        Vector3 pointOnCapsule = endPoint + rayDir * capsuleCollider.radius;

                        handlePositions.Add(pointOnCapsule);
                        handleToDirection[pointOnCapsule] = direction;
                    }
                }

                // Add points on the middle of the capsule
                Vector3 middle = capsuleCollider.center;
                foreach (var direction in standardDirections)
                {
                    // Skip directions that are aligned with the capsule axis
                    if (Mathf.Abs(Vector3.Dot(direction, capsuleDir)) > 0.9f) continue;

                    Vector3 pointOnCapsule = middle + direction * capsuleCollider.radius;
                    handlePositions.Add(pointOnCapsule);
                    handleToDirection[pointOnCapsule] = direction;
                }

                // Add end cap points in the direction of the capsule axis
                Vector3 endPoint1 = capsuleCollider.center + capsuleDir * (capsuleHeight * 0.5f);
                Vector3 endPoint2 = capsuleCollider.center - capsuleDir * (capsuleHeight * 0.5f);

                handlePositions.Add(endPoint1 + capsuleDir * capsuleCollider.radius);
                handleToDirection[endPoint1 + capsuleDir * capsuleCollider.radius] = capsuleDir;

                handlePositions.Add(endPoint2 - capsuleDir * capsuleCollider.radius);
                handleToDirection[endPoint2 - capsuleDir * capsuleCollider.radius] = -capsuleDir;
            }
            // For MeshCollider - cast rays to find points on the surface
            else if (_collider is MeshCollider)
            {
                // Generate multiple directions to cast rays
                Vector3 center = _collider.bounds.center;
                List<Vector3> probeDirections = new List<Vector3>();

                // Add standard dice directions
                probeDirections.AddRange(standardDirections);

                // Add more directions for better coverage
                for (int x = -1; x <= 1; x += 1)
                {
                    for (int y = -1; y <= 1; y += 1)
                    {
                        for (int z = -1; z <= 1; z += 1)
                        {
                            if (x == 0 && y == 0 && z == 0) continue; // Skip center
                            if (Mathf.Abs(x) + Mathf.Abs(y) + Mathf.Abs(z) == 1) continue; // Skip standard directions (already added)

                            probeDirections.Add(new Vector3(x, y, z).normalized);
                        }
                    }
                }

                // Cast rays in each direction
                foreach (var direction in probeDirections)
                {
                    Ray ray = new Ray(_dice.transform.TransformPoint(center), _dice.transform.TransformDirection(direction));
                    if (Physics.Raycast(ray, out RaycastHit hit, 10f))
                    {
                        if (hit.collider == _collider)
                        {
                            // Convert hit point to local space
                            Vector3 localHitPoint = _dice.transform.InverseTransformPoint(hit.point);
                            Vector3 localNormal = _dice.transform.InverseTransformDirection(hit.normal);

                            // Find the closest standard direction to the hit normal
                            Vector3 closestDirection = Vector3.zero;
                            float closestAngle = float.MaxValue;

                            foreach (var stdDir in standardDirections)
                            {
                                float angle = Vector3.Angle(localNormal, stdDir);
                                if (angle < closestAngle)
                                {
                                    closestAngle = angle;
                                    closestDirection = stdDir;
                                }
                            }

                            // Store exact hit point in local space, not normalized
                            handlePositions.Add(localHitPoint);
                            handleToDirection[localHitPoint] = localNormal; // Use actual normal, not standardized
                        }
                    }
                }

                // If no hits were found, fall back to standard directions around the center
                if (handlePositions.Count == 0)
                {
                    foreach (var direction in standardDirections)
                    {
                        // Calculate point using exact bounds values, no rounding or normalization
                        float distance = _collider.bounds.extents.magnitude * 0.5f;
                        Vector3 point = center + direction * distance;
                        handlePositions.Add(point);
                        handleToDirection[point] = point; // Store exact point as direction
                    }
                }
            }
            // For any other collider type
            else
            {
                // Use standard directions and raycast to find collider surface
                Vector3 center = _collider.bounds.center;
                foreach (var direction in standardDirections)
                {
                    Ray ray = new Ray(_dice.transform.TransformPoint(center), _dice.transform.TransformDirection(direction));
                    if (Physics.Raycast(ray, out RaycastHit hit, 10f))
                    {
                        if (hit.collider == _collider)
                        {
                            Vector3 localHitPoint = _dice.transform.InverseTransformPoint(hit.point);
                            handlePositions.Add(localHitPoint);
                            handleToDirection[localHitPoint] = direction;
                        }
                    }
                    else
                    {
                        // If no hit, use the bounds to estimate a point
                        Vector3 point = center + direction * _collider.bounds.extents.magnitude * 0.5f;
                        handlePositions.Add(point);
                        handleToDirection[point] = direction;
                    }
                }
            }

            // Draw handles for all collected points
            foreach (var position in handlePositions)
            {
                Vector3 direction = handleToDirection[position];

                // Check if this direction already exists in faces
                bool directionExists = _dice.faceMap.Faces.Any(x => Vector3.Distance(x.faceDirection.normalized, direction.normalized) < 0.1f);

                if (DrawButton(position, "Face: " + position, directionExists))
                {
                    // Log position data for debugging
                    Debug.Log($"Adding face at local position: {position} (World: {_dice.transform.TransformPoint(position)})");

                    OpenWindowForFaceIndexSet((faceIndex) => 
                    { 
                        // Store the position exactly as is, not normalized or modified
                        _dice.faceMap.AddFace(new Face(faceIndex, position)); 

                        // Log confirmation after adding
                        Debug.Log($"Added face with index {faceIndex} at position {position}");
                    });
                }
            }
        }

        private void GetFaceDictionaries()
        {
            if (!_useCachedData || normalToCenter.Count == 0)
            {
                // Use a simplified approach to reduce the number of triangles processed
                int maxTriangles = 500; // Limit the number of triangles to process for performance
                int step = Mathf.Max(1, _mesh.triangles.Length / 3 / maxTriangles);

                for (int i = 0; i < _mesh.triangles.Length / 3; i += step)
                {
                    var meshTriangle = i * 3;
                    var triangle = new Vector3[3];
                    for (int y = 0; y < 3; y++)
                    {
                        triangle[y] = _mesh.vertices[_mesh.triangles[meshTriangle + y]];
                    }

                    var center = (triangle[0] + triangle[1] + triangle[2]) / 3;
                    var normal = Vector3.Cross(triangle[1] - triangle[0], triangle[2] - triangle[0]).normalized;

                    // Round normals to standard directions for dice
                    Vector3 roundedNormal = new Vector3(
                        Mathf.Round(normal.x),
                        Mathf.Round(normal.y),
                        Mathf.Round(normal.z));

                    if (roundedNormal.magnitude > 0)
                    {
                        roundedNormal.Normalize();
                        normal = roundedNormal;
                    }

                    if (!normalToCenter.ContainsKey(normal))
                    {
                        normalToCenter[normal] = Vector3.zero;
                        normalToCount[normal] = 0;
                    }

                    normalToCenter[normal] += center;
                    normalToCount[normal]++;
                }
            }
        }

        private void DrawEdges()
        {
            Handles.color = Color.black;

            // Only draw a subset of edges for better performance
            int maxTriangles = 200; // Limit the number of triangles to process for performance
            int step = Mathf.Max(1, _mesh.triangles.Length / 3 / maxTriangles);

            for (int i = 0; i < _mesh.triangles.Length; i += 3 * step)
            {
                int index1 = _mesh.triangles[i];
                int index2 = _mesh.triangles[i + 1];
                int index3 = _mesh.triangles[i + 2];

                Vector3 vertex1 = _mesh.vertices[index1];
                Vector3 vertex2 = _mesh.vertices[index2];
                Vector3 vertex3 = _mesh.vertices[index3];

                Handles.DrawLine(vertex1, vertex2);
                Handles.DrawLine(vertex2, vertex3);
                Handles.DrawLine(vertex3, vertex1);
            }

            Handles.color = Color.white;
        }

        private void OpenWindowForFaceIndexSet(Action<int> onComplete)
        {
            var window = EditorWindow.GetWindow<FaceIndexSetWindow>("Face Data");
            window.minSize = new Vector2(250, 100);
            window.maxSize = new Vector2(250, 100);
            window.OnClosed = (faceIndex) => { onComplete?.Invoke(faceIndex); };
        }

        private bool DrawButton(Vector3 position, string text, bool isFaceExist)
        {
            if (isFaceExist)
            {
                Handles.color = Color.green;
            }
            else
            {
                Handles.color = Color.white;
            }

            // Get the scene view camera
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return false;

            // Calculate distance-based size for handle
            float handleSize = HandleUtility.GetHandleSize(position) * 0.15f;

            return Handles.Button(position, Quaternion.identity, handleSize, handleSize, Handles.SphereHandleCap) &&
                   EditorUtility.DisplayDialog("Face Index", text, "Set", "Cancel");
        }

        [DrawGizmo(GizmoType.Selected, typeof(Dice))]
        private static void DrawGizmo(Dice target, GizmoType gizmoType)
        {
            var targetTransform = target.transform;
            var diceGraphic = target.DiceGraphic;
            var simulation = target.Simulation;
            if (target.faceMap == null) return;
            DrawGizmos(target.faceMap.Faces,
                diceGraphic == null ? targetTransform.localToWorldMatrix : diceGraphic.transform.localToWorldMatrix,
                simulation == null ? -1 : simulation.Outcome(), target.RollData);
        }

        private static void DrawGizmos(List<Face> faces, Matrix4x4 matrix4X4, int outCome, RollData data)
        {
            for (int i = 0; i < faces.Count; i++)
            {
                Handles.matrix = matrix4X4;
                Color textColor = faces[i].faceValue == outCome ? Color.red : Color.black;
                if (!data.Equals(RollData.Default))
                {
                    textColor = faces[i].faceValue == data.faceValue ? Color.green : textColor;
                }

                GUIStyle style = new GUIStyle
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    richText = true,
                    normal =
                    {
                        textColor = textColor
                    }
                };
                Handles.Label(faces[i].faceDirection, faces[i].faceValue.ToString(), style);
            }
        }
    }

    internal class FaceIndexSetWindow : EditorWindow
    {
        private bool changed;
        private int faceIndex = 0;
        public Action<int> OnClosed;

        public void OnGUI()
        {
            GUILayout.Label("Face Index Set");
            EditorGUI.BeginChangeCheck();
            faceIndex = EditorGUILayout.IntField("Face Index", faceIndex);
            if (EditorGUI.EndChangeCheck())
            {
                changed = true;
            }

            using (new EditorGUI.DisabledScope(!changed))
            {
                if (GUILayout.Button("Set"))
                {
                    OnClosed?.Invoke(faceIndex);
                    Close();
                }
            }
        }
    }
}