using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

public class WorldManager : MonoBehaviour
{
    public const int NavRoadAreaIndex = 3;

    public static WorldManager Instance;
    public static GameInfo ExitedGameInfo = null;

    public static Queue<CompletableAction> QuedActions = new();
    public static CompletableAction CurrentAction = null;

    public static event Action<WorldManager> OnGameOver;

    public LayerMask TouchWorldMask;
    public LayerMask TerrainMask;
    [Space]
    [Header("Moving")]
    public float CameraLerpMoveSpeed = 1;
    public float CameraMoveSens = 10f;
    public float MaxCameraHeight = 20f;
    [Tooltip("At this height, the CameraMoveSenseByHeight curve will be at time = 1. (The height you want the camera sens to be equal to CameraMoveSens.)")]
    public float MaxSensCameraHeight = 10f;
    [Tooltip("A curve where time = targetCameraHeight / MaxSensCameraHeight, and is multiplied by the CameraMoveSens, to get the camera's move sens at that height.")]
    public AnimationCurve CameraMoveSenseByHeight = AnimationCurve.Linear(0, 0, 1, 1);
    [Space]
    [Header("Turning")]
    public float CameraLerpTurnSpeed = 1;
    public float XCameraRotMax = 90;
    public float MaxXRotCameraHeight = 100f;
    public AnimationCurve XCameraRotByHeight = AnimationCurve.Linear(0, 0, 1, 1);
    [Space]
    [Header("Following")]
    public float CamFollowTurnSpeed = 1f;
    public Delayer CamFollowTurnDelay = new(2f);
    public float BreakFollowDragDist = 5f;
    [Space]
    public Transform WorldCamParent;
    public Camera WorldCamera;
    public List<VisualBounds> CameraBounds;
    [Space]
    public float DeathCameraSpeed = 1f;
    public AnimationCurve DeathCameraSpeedCurve;
    [Space]
    public Transform PlayerSpawnPos;
    public WorldLocation PlayerHouse;
    public Transform VisualPathParent;
    public WorldPlayer WorldPlayer;
    public WorldUI WorldUI;
    [Space]
    public CloudManager CloudManager;
    public Transform EnvironmentParent;
    [Space]
    public AudioClip WorldAmbience;
    [Space]
    [HideInEditorMode, ReadOnly] public List<WorldLocation> Locations = new();
    [HideInEditorMode, ReadOnly] public List<Terrain> Terrains = new();
    [HideInEditorMode, ReadOnly] public List<CloudBounds> CloudBounds = new();

    [HideInEditorMode, ReadOnly] public Transform CameraFollowObj;
    [HideInEditorMode, ReadOnly] public float FollowCameraDist = 0f;
    [HideInEditorMode, ReadOnly] public Vector3 TargetCameraRot;
    [HideInEditorMode, ReadOnly] public bool CanMoveCamera = true;

    /// <summary>How far off the terrain's y level is the camera.</summary>
    public float TargetCameraHeight => cameraFloorOffset.y;
    public Vector3 TargetCameraPos 
    { 
        get 
        {
            var pos = cameraFloorOffset;
            if (CameraFollowObj)
                pos.y += CameraFollowObj.position.y;
            else
                pos.y += terrainY;
            return pos;
        } 
    }

    public WorldSaved WorldSaved => MainPlayerData.Instance.WorldSaved;
    public bool IsGameOver => MainPlayerData.Instance.IsGameOver;

    const float minCameraHeight = 1f;
    Vector3 cameraFloorOffset;
    float terrainY;
    Terrain terrainUnderCamera;
    Vector3 floorHitPoint;

    RaycastHit[] camFloorRaycasts = new RaycastHit[256];

    ManagedCoroutine enterCor = null;

    Vector3 camForward;
    Vector3 toFollow;
    Vector3 onForward;
    Vector3 posChange;

    void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(floorHitPoint, 0.2f);

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(TargetCameraPos, camForward);
        //Gizmos.color = Color.red;
        //Gizmos.DrawLine(TargetCameraPos, TargetCameraPos + toFollow);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(TargetCameraPos, TargetCameraPos + onForward);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(TargetCameraPos, TargetCameraPos + posChange);
    }

    void Awake()
    {
        Instance = this;

        enterCor = new(this);

        Terrains = new(EnvironmentParent.GetComponentsInChildren<Terrain>());

        // WebGL: TerrainCollider is not supported, generate mesh colliders from heightmaps
        TerrainMeshColliderGenerator.GenerateForTerrains(Terrains);

        Locations = new(EnvironmentParent.GetComponentsInChildren<WorldLocation>());
        Locations.ForEach(x => AddLocation(x, true));

        // Cloud cover disabled — whole map explorable. Re-enable later for area gating.
        //SpawnCloudBounds();
        if (CloudManager != null)
            CloudManager.gameObject.SetActive(false);

        // TownBuilder disabled — town layout now hand-placed in World.unity scene editor.
        // To re-enable procedural town: uncomment the line below.
        //if (gameObject.GetComponent<TownBuilder>() == null)
        //    gameObject.AddComponent<TownBuilder>();

        // Spawn NPCs in the world
        if (gameObject.GetComponent<NPCSpawner>() == null)
            gameObject.AddComponent<NPCSpawner>();

        // SWG-style surveying and resource gathering
        if (gameObject.GetComponent<SurveySystem>() == null)
            gameObject.AddComponent<SurveySystem>();

        LoadWorldData();

        bool justLeftGame = ExitedGameInfo != null;
        if (ExitedGameInfo != null)
        {
            OnGameExited(ExitedGameInfo);

            // Teleport home on loss + grant invincibility
            if (ExitedGameInfo.ClientWon.HasValue && !ExitedGameInfo.ClientWon.Value)
            {
                // Teleport player to town center (spawn point)
                if (WorldPlayer != null && PlayerSpawnPos != null)
                {
                    WorldPlayer.TeleportTo(PlayerSpawnPos.position);
                    WorldPlayer.SetInvincible(5f);
                    Debug.Log("[WorldManager] Lost battle — teleported home with 5s invincibility");
                }
            }
        }
        //If we didn't exit a game and some how we have game over player data, then reset and reload scene.
        else if (IsGameOver)
            LoadManager.ResetPlayerData(true);

        //Has graveyard choices.
        if (!IsGameOver && GraveyardManager.ChoiceArgs.Count > 0)
        {
            AddQuedAction(new(
                GraveyardManager.Instance,
                () => GraveyardManager.ShowGraveyardChoices(),
                () => !GraveyardManager.Instance.GraveyardPanel.IsOpen));
        }
        //Animate health change.
        if (WorldSaved.LastShownBattleHealth != WorldSaved.BattleHealth)
        {
            AddQuedAction(new(
                WorldUI,
                () => WorldUI.ShowHealthBarChange(WorldSaved.LastShownBattleHealth, WorldSaved.BattleHealth),
                () => !WorldUI.ShowingHealthBarChange));
        }
        //Show game over panel after health change.
        if (IsGameOver)
        {
            AddQuedAction(new(
                WorldUI.GameOverPanel,
                () => WorldUI.ShowGameOverPanel(),
                () => !WorldUI.GameOverPanel.IsOpen)
                );
        }

        AudioManager.PlayAmbience(WorldAmbience);
    }

    void Start()
    {
        CameraInputUpdate();
        CameraUpdate(true);
    }

    bool _overheadMode = false;

    void Update()
    {
        // F9 = toggle overhead map view for screenshots
        if (Input.GetKeyDown(KeyCode.F9))
        {
            _overheadMode = !_overheadMode;
            if (_overheadMode)
            {
                CameraFollowObj = null;
                // Center of all terrain tiles
                float cx = -23f, cz = -5f;
                cameraFloorOffset = new Vector3(cx, 250, cz);
                TargetCameraRot = new Vector3(90, 0, 0);
                SetCameraPos(TargetCameraPos, true);
                SetCameraRot(Quaternion.Euler(TargetCameraRot), true);
                // Hide HUD
                if (WorldUI != null) WorldUI.gameObject.SetActive(false);
                Debug.Log("[Debug] Overhead mode ON — F9 to return");
            }
            else
            {
                SetCameraFollow(WorldPlayer.transform);
                cameraFloorOffset = new Vector3(0, 8, 0);
                TargetCameraRot = new Vector3(45, 0, 0);
                if (WorldUI != null) WorldUI.gameObject.SetActive(true);
                Debug.Log("[Debug] Overhead mode OFF");
            }
        }
        if (_overheadMode)
        {
            // Allow zoom in overhead mode with scroll
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0) cameraFloorOffset.y = Mathf.Clamp(cameraFloorOffset.y + scroll * -50f, 50, 500);
            // Allow pan with WASD
            float h = 0, v = 0;
            if (Input.GetKey(KeyCode.W)) v += 1;
            if (Input.GetKey(KeyCode.S)) v -= 1;
            if (Input.GetKey(KeyCode.A)) h -= 1;
            if (Input.GetKey(KeyCode.D)) h += 1;
            if (h != 0 || v != 0) cameraFloorOffset += new Vector3(h, 0, v) * 80f * Time.deltaTime;
            SetCameraPos(TargetCameraPos, false);
            return; // Skip normal update in overhead mode
        }

        //Not touching any UI.
        if (!MasterUI.IsTouchingUI && !MasterUI.IsPanelOpen)
        {
            if (InputManager.QuickTouch && WorldRaycast(InputManager.TouchPosition, out RaycastHit hit))
            {
                var hitPos = hit.point;
                //Hit interactable, try interact first.
                if (hit.collider.TryGetParent(out Interactable inter))
                {
                    inter.TryInteract(new()
                    {
                        InteractPos = hit.point
                    });
                }
                //Hit terrain or any ground — click-to-move (free roam, not path-only)
                else
                {
                    WorldPlayer.MoveTo(hitPos);
                }
            }

            // E key: interact with nearest NPC
            if (Input.GetKeyDown(KeyCode.E) && WorldPlayer != null)
            {
                var npcs = FindObjectsByType<NPCController>(FindObjectsSortMode.None);
                NPCController nearest = null;
                float nearestDist = float.MaxValue;
                Vector3 playerPos = WorldPlayer.transform.position;
                foreach (var npc in npcs)
                {
                    float dist = Vector3.Distance(playerPos, npc.transform.position);
                    if (dist <= npc.InteractionRange && dist < nearestDist)
                    {
                        nearest = npc;
                        nearestDist = dist;
                    }
                }
                if (nearest != null)
                {
                    nearest.TryInteract(new InteractCtx { InteractPos = playerPos });
                }
            }

            if (CanMoveCamera)
                CameraInputUpdate();
        }

        if (!CamFollowTurnDelay.IsReady(false))
            CamFollowTurnDelay.Update(Time.deltaTime);

        //Debug.Log(TargetCameraHeight);

        if ((CurrentAction == null && QuedActions.Count > 0) || (CurrentAction != null && CurrentAction.IsComplete()))
            DoNextQuedAction();
    }

    void LateUpdate()
    {
        CameraUpdate();
    }

    void OnEnable()
    {
        //InputManager.OnFingerAction += InputManager_OnFingerAction;
    }
    void OnDisable()
    {
        //InputManager.OnFingerAction -= InputManager_OnFingerAction;
        AudioManager.StopAmbience();

        SaveWorldData();
    }
    void InputManager_OnFingerAction(UnityEngine.InputSystem.EnhancedTouch.Finger finger, FingerAction action)
    {
        if (finger.index != 0)
            return;

        if (action == FingerAction.OnFingerDown)
        {

        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GUILD WARS-STYLE ORBIT CAMERA
    // - Always orbits around the player
    // - Right-click drag to rotate (yaw + pitch)
    // - Scroll to zoom in/out
    // - Smooth follow, no break-away
    // - Camera collides with terrain (won't clip through ground)
    // ═══════════════════════════════════════════════════════════════

    #region World Camera

    // Orbit state
    float _orbitYaw = 0f;       // Horizontal rotation around player (degrees)
    float _orbitPitch = 35f;    // Vertical angle (degrees, 10=low, 80=top-down)
    float _orbitDist = 15f;     // Distance from player
    bool _rightMouseHeld = false;
    Vector2 _lastMousePos;

    // Orbit tuning
    const float ORBIT_MIN_PITCH = 10f;
    const float ORBIT_MAX_PITCH = 75f;
    const float ORBIT_MIN_DIST = 4f;
    const float ORBIT_MAX_DIST = 200f;
    const float ORBIT_MOUSE_SENS = 0.3f;
    const float ORBIT_ZOOM_SENS = 3f;
    const float ORBIT_SMOOTH_SPEED = 8f;       // How fast camera lerps to target pos
    const float ORBIT_ROT_SMOOTH = 10f;        // How fast camera lerps to target rot
    const float ORBIT_LOOK_OFFSET_Y = 1.5f;    // Look at player's chest, not feet
    const float ORBIT_TERRAIN_OFFSET = 0.5f;   // Min distance above terrain

    void CameraInputUpdate()
    {
        // Right-click drag = orbit rotation
        if (Input.GetMouseButtonDown(1))
        {
            _rightMouseHeld = true;
            _lastMousePos = Input.mousePosition;
        }
        if (Input.GetMouseButtonUp(1))
            _rightMouseHeld = false;

        if (_rightMouseHeld)
        {
            Vector2 delta = (Vector2)Input.mousePosition - _lastMousePos;
            _lastMousePos = Input.mousePosition;

            _orbitYaw += delta.x * ORBIT_MOUSE_SENS;
            _orbitPitch -= delta.y * ORBIT_MOUSE_SENS;
            _orbitPitch = Mathf.Clamp(_orbitPitch, ORBIT_MIN_PITCH, ORBIT_MAX_PITCH);
        }

        // Scroll = zoom (scales with current distance for smooth feel at any range)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
        {
            _orbitDist *= 1f - scroll * 0.15f;
            _orbitDist = Mathf.Clamp(_orbitDist, ORBIT_MIN_DIST, ORBIT_MAX_DIST);
        }
    }

    void CameraUpdate(bool set = false)
    {
        Transform target = WorldPlayer != null ? WorldPlayer.transform : CameraFollowObj;
        if (target == null) return;

        // Look-at point: player position + slight Y offset
        Vector3 lookAt = target.position + Vector3.up * ORBIT_LOOK_OFFSET_Y;

        // Calculate orbit position from yaw, pitch, distance
        float yawRad = _orbitYaw * Mathf.Deg2Rad;
        float pitchRad = _orbitPitch * Mathf.Deg2Rad;

        Vector3 orbitOffset = new Vector3(
            Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
            Mathf.Sin(pitchRad),
            -Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
        ) * _orbitDist;

        Vector3 desiredPos = lookAt + orbitOffset;

        // Terrain collision: don't let camera go below terrain
        if (Physics.Raycast(desiredPos + Vector3.up * 50f, Vector3.down, out var terrainHit, 100f))
        {
            float minY = terrainHit.point.y + ORBIT_TERRAIN_OFFSET;
            if (desiredPos.y < minY)
                desiredPos.y = minY;
        }

        // Obstacle collision: raycast from lookAt to desired, pull camera forward if blocked
        Vector3 dir = desiredPos - lookAt;
        if (Physics.Raycast(lookAt, dir.normalized, out var obstacleHit, dir.magnitude))
        {
            desiredPos = obstacleHit.point - dir.normalized * 0.3f;
        }

        // Smooth position
        var camParent = WorldCamParent.transform;
        float smoothSpeed = set ? 1f : ORBIT_SMOOTH_SPEED * Time.deltaTime;
        camParent.position = set ? desiredPos : Vector3.Lerp(camParent.position, desiredPos, smoothSpeed);

        // Smooth rotation — always look at the player
        Quaternion desiredRot = Quaternion.LookRotation(lookAt - camParent.position);
        float rotSpeed = set ? 1f : ORBIT_ROT_SMOOTH * Time.deltaTime;
        camParent.rotation = set ? desiredRot : Quaternion.Slerp(camParent.rotation, desiredRot, rotSpeed);

        // Save state
        TargetCameraRot = camParent.rotation.eulerAngles;
        cameraFloorOffset = camParent.position - target.position;
    }

    public void MoveCamera(Vector3 move, bool set = false)
    {
        // Legacy — not used by orbit camera but kept for compatibility
    }
    public void ResetCameraPos(bool snap = false)
    {
        _orbitYaw = 0;
        _orbitPitch = 35f;
        _orbitDist = 15f;
    }

    void SetCameraPos(Vector3 pos, bool snap = false)
    {
        // Used by overhead mode only now
        if (!pos.IsFinite()) return;
        var camParent = WorldCamParent.transform;
        if (snap)
            camParent.position = pos;
        else
            camParent.position = Vector3.Lerp(camParent.position, pos, CameraLerpMoveSpeed * Time.deltaTime);
    }
    void ClampTargetCameraPos()
    {
        if (!cameraFloorOffset.IsFinite())
            cameraFloorOffset = Vector3.zero;

        cameraFloorOffset.y = Mathf.Max(cameraFloorOffset.y, minCameraHeight);
        //Stop camera from going lower then minY.
        float minY = terrainY + minCameraHeight;
        if (TargetCameraPos.y < minY)
        {
            float diff = minY - TargetCameraPos.y;
            cameraFloorOffset.y += diff;
        }
        cameraFloorOffset.y = Mathf.Min(cameraFloorOffset.y, MaxCameraHeight);

        var ray = new Ray(TargetCameraPos, Vector3.down);
        //Need to raycast all because we need to find the terrain directly under us.
        int hitCount = Physics.RaycastNonAlloc(ray, camFloorRaycasts, 100f, TouchWorldMask);
        for (int i = 0; i < hitCount; i++)
        {
            var hit = camFloorRaycasts[i];
            //Debug.Log(hit.transform, hit.transform);
            floorHitPoint = hit.point;

            //If the hit col is part of a terrain, and we are above that terrain.
            if (hit.collider.TryGetParent(out Terrain terrain) && terrain.IsOnTerrain(TargetCameraPos))
                terrainUnderCamera = terrain;
        }

        //Clamp camera to the terrain under us.
        if (terrainUnderCamera && !terrainUnderCamera.IsOnTerrain(TargetCameraPos))
        {
            //Keep the x and z on the terrain.
            //Debug.Log($"Clamping to terrain: {terrainUnderCamera}");
            cameraFloorOffset = terrainUnderCamera.ClampToTerrain(cameraFloorOffset);
        }

        // Skip cloud boundary checks if clouds are disabled
        if (CloudManager == null || !CloudManager.gameObject.activeInHierarchy)
            return;

        List<CloudBounds> explorable = new();
        List<CloudBounds> unexplorable = new();
        foreach (var bounds in CloudBounds)
        {
            if (bounds.CanBeExplored)
                explorable.Add(bounds);
            else
                unexplorable.Add(bounds);
        }
        //if (explorable.Count > 0 
        //    && !explorable.Exists(x => x.Bounds.Bounds.Contains(TargetCameraPos.SetY(x.transform.position.y))))
        //If an unexplorable bounds has our camera in it, move us into the closest explorable bounds.
        if (unexplorable.Exists(x => x.Bounds.Bounds.Contains(TargetCameraPos.SetY(x.transform.position.y))))
        {
            VisualBounds closest = null;
            float closestDist = 0;
            foreach (var cloudBounds in explorable)
            {
                //Using same y so it doesn't shift our camera's y.
                var xzCamPos = TargetCameraPos.SetY(cloudBounds.transform.position.y);
                float dist = (cloudBounds.Bounds.Bounds.ClosestPoint(xzCamPos) - xzCamPos).sqrMagnitude;
                if (closest == null || dist < closestDist)
                {
                    closest = cloudBounds.Bounds;
                    closestDist = dist;
                }
            }

            //Move camera inside closest, explorable bounds (don't affect the y).
            //Debug.Log($"Closest: {closest}", closest);
            var sameYCamPos = cameraFloorOffset.SetY(closest.transform.position.y);
            cameraFloorOffset += closest.MoveInsideBounds(sameYCamPos) - sameYCamPos;
        }

        return;

        //No camera bounds contains target camera pos, find closest bounds, and clamp.
        if (!CameraBounds.Exists(x => x.Bounds.Contains(TargetCameraPos)))
        {
            VisualBounds closest = null;
            float closestDist = 0;
            foreach (var bounds in CameraBounds)
            {
                float dist = (bounds.Bounds.ClosestPoint(TargetCameraPos) - TargetCameraPos).sqrMagnitude;
                if (closest == null || dist < closestDist)
                {
                    closest = bounds;
                    closestDist = dist;
                }
            }

            Debug.Log($"Found closest bounds: {closest}", closest);
            var inBounds = closest.MoveInsideBounds(TargetCameraPos);
            var diff = inBounds - TargetCameraPos;
            //Move offset.
            MoveCamera(diff);
        }
    }

    void SetCameraRot(Quaternion rot, bool snap = false)
    {
        var cam = WorldCamera.transform;
        var camParent = WorldCamParent;

        //Split it, cam takes x and z rots, parent takes y.
        var camRot = Quaternion.Euler(rot.eulerAngles.SetY(0));
        var camParentRot = Quaternion.Euler(rot.eulerAngles.SetX(0).SetZ(0));

        if (snap)
        {
            cam.localRotation = camRot;
            camParent.rotation = camParentRot;
        }
        else
        {
            //Makes it smoother.
            float t = 1f - Mathf.Exp(-CameraLerpTurnSpeed * Time.deltaTime);

            cam.localRotation = Quaternion.Slerp(cam.localRotation, camRot, t);
            camParent.rotation = Quaternion.Slerp(camParent.rotation, camParentRot, t);
        }

        WorldSaved.WorldCameraRotation = rot;
    }

    public void SetCameraFollow(Transform obj)
    {
        CameraFollowObj = obj;

        WorldSaved.IsCameraFollowingPlayer = obj != null && obj.GetComponent<WorldPlayer>();
        //lastFollowObjRot = obj.rotation;
    }

    /// <summary>Called from JS: adjust terrain collider height offset and regenerate.</summary>
    public void SetTerrainOffset(string value)
    {
        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float offset))
        {
            TerrainMeshColliderGenerator.HeightOffset = offset;
            // Destroy old colliders
            foreach (var t in Terrains)
            {
                var old = t.GetComponentInChildren<MeshCollider>();
                if (old != null && old.gameObject.name == "_WebGLTerrainCollider")
                    Destroy(old.gameObject);
            }
            // Regenerate
            TerrainMeshColliderGenerator.GenerateForTerrains(Terrains);
            Debug.Log($"[Debug] Terrain offset set to {offset}");
        }
    }

    /// <summary>Called from JS: move camera to overhead map view for screenshots.</summary>
    public void DebugSetCameraOverhead(string _)
    {
        CameraFollowObj = null;
        // Center of the world, looking straight down
        cameraFloorOffset = new Vector3(0, 200, 0);
        TargetCameraRot = new Vector3(90, 0, 0); // Look straight down
        SetCameraPos(TargetCameraPos, true);
        SetCameraRot(Quaternion.Euler(TargetCameraRot), true);
        Debug.Log("[Debug] Camera set to overhead view");
    }
    #endregion

    public bool WorldRaycast(Vector2 screenPos, out RaycastHit hit)
    {
        var ray = WorldCamera.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out hit, 300f, TouchWorldMask);
    }

    void SpawnCloudBounds()
    {
        //Make sure there is a cloud bounds per terrain.
        foreach (var terrain in Terrains)
        {
            Vector3 tCenter = terrain.transform.position + (terrain.terrainData.size.SetY(0) / 2);

            var cloudBounds = terrain.GetComponentInChildren<CloudBounds>();
            //Spawn cloud bounds.
            if (cloudBounds == null)
            {
                cloudBounds = Instantiate(CloudManager.CloudBoundsPrefab, terrain.transform);
                cloudBounds.Terrain = terrain;
                //Fit to terrain.
                cloudBounds.FitToTerrain();
            }

            //Terrain ref check.
            cloudBounds.Terrain = terrain;
            cloudBounds.SpawnClouds();

            var onTerrain = Locations.FindAll(x => x.OnTerrain == terrain);
            onTerrain.ForEach(x => x.ExploreCloudBounds.Add(cloudBounds));

            //None on terrain, choose closest location as holder of bounds.
            if (onTerrain.Count == 0)
            {
                var locationsByDist = Locations.OrderByDescending(x => (tCenter - x.transform.position).sqrMagnitude).ToList();
                //Last location is closest.
                locationsByDist[^1].ExploreCloudBounds.Add(cloudBounds);
            }

            CloudBounds.Add(cloudBounds);
        }
    }
    void LoadWorldData()
    {
        Locations.ForEach(x => x.LoadSaved(this));
        Locations.ForEach(x => x.OnLocationsSet(this));

        CloudManager.LoadExplored(this);

        //Saved player pos. Needs to happen after locations are explored, and nav boundaries are disabled.
        WorldPlayer.Load(WorldSaved, this);

        //Saved world cam pos.
        if (WorldSaved.WorldCameraOffset != Vector3.zero)
        {
            cameraFloorOffset = WorldSaved.WorldCameraOffset;
            SetCameraPos(TargetCameraPos, true);
        }
        else
        {
            //Set camera pos to where it is in scene start.
            cameraFloorOffset = WorldCamParent.position;
        }
        //Saved cam rot.
        if (WorldSaved.WorldCameraRotation != Quaternion.identity)
        {
            TargetCameraRot = WorldSaved.WorldCameraRotation.eulerAngles;
            SetCameraRot(WorldSaved.WorldCameraRotation, true);
        }

        //Always follow the player camera
        SetCameraFollow(WorldPlayer.transform);

        //Saved health.
        WorldUI.PlayerHealth.SetHealth(WorldSaved.BattleHealth, false);

        //Was in a game and didn't finish it, fail it.
        var inGameInfo = MainPlayerData.Instance.InGameInfo;
        if (inGameInfo != null)
        {
            Debug.LogError("Player exited game without finishing, failing the game...");
            MainPlayerData.Instance.InGameInfo = null;
            inGameInfo.ClientWon = false;
            GameManager.OnGameEnded(inGameInfo);
            //Do on game exited after this.
            ExitedGameInfo = inGameInfo;
        }
    }
    public void SaveWorldData()
    {
        //WorldSaved.WorldPlayerPos = WorldPlayer.transform.position;
        //WorldSaved.WorldCameraOffset = cameraFloorOffset;

        //WorldSaved.BattleHealth = WorldUI.PlayerHealth.ShownHealth;
    }

    public static void AddLocation(WorldLocation location, bool add)
    {
        //Debug.Log($"Add location: {location}, add: {add}");
        if (add && !Instance.Locations.Contains(location))
        {
            Debug.Log($"Add location: {location}, add: {add}");

            Instance.Locations.Add(location);
            location.Set(Instance);
        }
        else if (!add)
            Instance.Locations.Remove(location);
    }

    public void EnterLocation(WorldLocation location, Encounter encounter)
    {
        enterCor.Start(EnterCor, location, encounter);
    }
    IEnumerator EnterCor(object[] args)
    {
        var location = (WorldLocation)args[0];
        var encounter = (Encounter)args[1];

        //Fade to background color.
        yield return WorldUI.FadeScreen(1, encounter.BackgroundColor);

        //If it's a repeatable location, randomize the lineup again, regardless of if we won or lost last time.
        //if (location.IsRepeatable)
        //    location.Saved.LineupSeed = StaticClass.RandomSeed;

        //Spawn in and start game.
        var manager = GameLoader.StartGame(new()
        {
            LocationId = location.LocationId,
            EncounterId = encounter.AssetId,
        });

        enterCor.OnCorEnd();
    }

    public void OnGameExited(GameInfo info)
    {
        var loc = Locations.Find(x => x.LocationId == info.LocationId);
        if (loc == null)
        {
            // Trainer/wild/dungeon battles won't have a WorldLocation.
            // Skip reward panel — these battles handle rewards via their own systems
            // (NPCController.OnTrainerDefeated, OverworldIntegration notifications, DungeonSystem).
            Debug.Log($"{nameof(OnGameExited)}: No WorldLocation for '{info.LocationId}' (dynamic battle). Client Won: {info.ClientWon}");

            if (info.ClientWon.HasValue)
            {
                // Don't show RewardManager for dynamic battles — it blocks camera/input
                // and these battles don't populate NextRewardInfos anyway.

                if (WorldSaved.BattleHealth <= 0)
                    GameOver(false);
            }

            ExitedGameInfo = null;
            return;
        }

        Debug.Log($"{nameof(OnGameExited)}, Client Won: {info.ClientWon}");

        loc.UpdateDefeatStatus();
        loc.ConnectedLocations.ForEach(x => x.UpdateExplored());

        if (info.ClientWon.HasValue)
        {
            if (info.ClientWon.Value)
            {
                //Show any rewards.
                RewardManager.Show();
            }
            else
            {
            }

            if (WorldSaved.BattleHealth <= 0)
                GameOver(false);
        }

        ExitedGameInfo = null;
    }

    public void AddQuedAction(CompletableAction completable)
    {
        QuedActions.Enqueue(completable);
    }
    public void DoNextQuedAction()
    {
        if (QuedActions.Count == 0)
        {
            CurrentAction = null;
            return;
        }
        CurrentAction = QuedActions.Dequeue();
        Debug.Log($"Doing next qued action on obj: '{CurrentAction.Source}'", CurrentAction.Source);
        CurrentAction.Action.InvokeSafe($"{nameof(CurrentAction)}.{nameof(Action)}");
    }

    public void GameOver(bool defeatedGame)
    {
        if (IsGameOver)
            return;
        Debug.Log("GAME OVER!");
        //Don't allow any other panel from being opened.
        MasterUI.Instance.OnlyOpenablePanels.Add(WorldUI.GameOverPanel);
        //Mark data
        MainPlayerData.Instance.SetGameOver(true, defeatedGame);

        //Follow
        SetCameraFollow(WorldPlayer.transform);
        //Run home
        WorldPlayer.GameOver();

        OnGameOver.InvokeSafe(nameof(OnGameOver), this);
        //StartCoroutine(GameOverCor());
    }
    IEnumerator GameOverCor()
    {
        //var pivotTrans = WorldPlayer.transform;
        //var pivotPos = pivotTrans.position;
        //var moveTrans = WorldCamParent.transform;

        //To cam.
        //var startOffset = moveTrans.position - pivotPos;
        //To death cam pos.
        //var targetOffset = WorldPlayer.GameOverCameraPos.position - pivotTrans.position;

        //Don't allow camera input.
        //CanMoveCamera = false;
        SetCameraFollow(WorldPlayer.transform);
        //WorldUI.ShowGameOverPanel();

        //Start running home.
        var runningHomeCor = WorldPlayer.GameOver();
        //Wait a frame to trigger health change showing.
        yield return null;
        //Wait till heart change finishes.
        yield return new WaitUntil(() => !WorldUI.ShowingHealthBarChange);
        //Wait till he finishes running home.
        yield return runningHomeCor;

        //Show end game UI.
        WorldUI.ShowGameOverPanel();

        //bool activatedPlayer = false;
        //while (!moveTrans.position.CloseTo(pivotPos, 0.05f))
        //{
        //    var arcOffset = Vector3.Slerp(startOffset, targetOffset, DeathCameraSpeed * Time.deltaTime);
        //    var arcedPos = pivotTrans.position + arcOffset;
        //    MoveCamera(arcedPos, true);
        //    //moveTrans.position = pivotPos + arcOffset;

        //    //If the projected camera pos is close enough to the player, begin his deah animation.
        //    if (!activatedPlayer && arcedPos.CloseTo(WorldPlayer.transform.position, WorldPlayer.GameOverAnimPlayCamDist))
        //    {
        //        activatedPlayer = true;
        //        WorldPlayer.GameOver();
        //    }

        //    yield return null;
        //}
    }
}

public class CompletableAction
{
    public MonoBehaviour Source;
    public Action Action;
    public Func<bool> IsComplete;

    public CompletableAction(MonoBehaviour source, Action action, Func<bool> completeCheck)
    {
        Source = source;
        Action = action;
        IsComplete = completeCheck;
    }
}

[Serializable]
public class WorldSaved
{
    public const float MaxBattleHealth = 6;

    //Start mid day.
    public float Time = 300;

    public int LastShownBattleHealth = 6;
    public int BattleHealth = 6;
    
    public Vector3 WorldPlayerPos;
    public Quaternion WorldPlayerRot;

    public bool IsCameraFollowingPlayer = true;
    public Vector3 WorldCameraOffset;
    public Quaternion WorldCameraRotation;

    public List<WorldLocationSaved> SavedLocations = new();

    public void UpdateTime(float delta)
    {
        Time += delta;
    }

    public WorldLocationSaved GetLocationSaved(string locationId)
    {
        return SavedLocations.Find(x => x.LocationId == locationId);
    }

    /// <returns>If the location defeat status was changed.</returns>
    public bool DefeatEncounter(string encounterId, string locationId, bool isDefeated)
    {
        var saved = GetLocationSaved(locationId);
        if (saved != null)
        {
            Debug.Log($"Defeating encounter at location: {isDefeated}, encounter Id: {encounterId}, location Id: {locationId}");

            bool changed = false;
            if (isDefeated && !saved.DefeatedEncounterIds.Contains(encounterId))
            {
                saved.DefeatedEncounterIds.Add(encounterId);
                changed = true;
            }
            else if (!isDefeated)
            {
                saved.DefeatedEncounterIds.Remove(encounterId);
                changed = true;
            }

            saved.LineupSeed = 0;
            return changed;
        }
        else
        {
            Debug.LogError($"Trying to defeat location but failed to find {nameof(WorldLocationSaved)} for id: {locationId}!");
            return false;
        }
    }
    public bool HasDefeatedEncounter(string encounterId, string locationId)
    {
        var saved = GetLocationSaved(locationId);
        return saved != null && saved.DefeatedEncounterIds.Contains(encounterId);
    }

    public void AddBattleHealth(int change) => SetBattleHealth(BattleHealth + change);
    public void SetBattleHealth(int health)
    {
        BattleHealth = (int)Mathf.Clamp(health, 0, MaxBattleHealth);

        Debug.Log($"Set battle health to {BattleHealth}!");
    }
}
[Serializable]
public class WorldLocationSaved
{
    public string LocationId;
    //public bool IsDefeated = false;
    public List<string> DefeatedEncounterIds = new();
    public bool IsFullyExplored = false;
    public int LineupSeed = 0;
}