using DG.Tweening;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class WorldPlayer : Interactable
{
    public float MaxMoveSpeed = 14f;
    public float DistForMaxMoveSpeed = 100f;
    public AnimationCurve MoveSpeedCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Space]
    public NavMeshAgent NavAgent;
    [Space]
    [Header("=== OVERWORLD: Free-Roam Movement ===")]
    public float WalkSpeed = 5f;
    public float SprintSpeed = 8f;
    public float RotationSpeed = 10f;
    public float Gravity = -20f;
    public float ClickMoveStopDist = 0.5f;
    CharacterController _charController;
    Vector3 _clickMoveTarget;
    bool _isClickMoving;
    float _verticalVelocity;
    public bool IsSprinting { get; private set; }
    public float CurrentSpeed => _charController != null && _charController.velocity.magnitude > 0.1f
        ? _charController.velocity.magnitude : 0f;
    [Header("=== JUMP ===")]
    public float JumpForce = 8f;

    [Header("=== INVINCIBILITY ===")]
    float _invincibilityTimer;
    public bool IsInvincible => _invincibilityTimer > 0;

    // Distance tracking for quests
    Vector3 _lastPosition;
    float _distanceAccumulator;

    [Space]
    public float AnimMaxMoveSpeedMultiplier = 1.25f;
    public string AnimMoveSpeedName = "MoveSpeed";
    public string GameOverAnimBool = "IsCrying";
    [Tooltip("The position in front of the player that the camera will pan to when the player dies.")]
    public Transform GameOverCameraPos;
    public float GameOverAnimPlayCamDist = 3f;
    public float GameOverCamFollowHeight = 5f;
    public Animator Animator;
    [Space]
    public float ModelMaxTiltAngle = 30f;
    public float ModelUprightLerpSpeed = 1;
    public float ModelTurnLerpSpeed = 1;
    public Transform ModelParent;
    [Space]
    public float EndArrowHoverHeight = 3f;
    public float EndArrowBobDist = 1f;
    public float EndArrowBobSpeed = 1f;
    public Transform PathEndArrow;
    public float EndRingRadius = 0.5f;
    public Transform PathEndRing;
    public Transform PathEndObjParent;
    [Space]
    public Transform PathArrowPrefab;
    //public float DottedPathObjSpacing = 2f;
    //public float DottedPathObjLength = 1f;
    //public Transform DottedPathObjPrefab;
    [Space]
    public VisualBounds ExploreBounds;

    [HideInEditorMode, ReadOnly] public List<Transform> SpawnedArrows = new();
    [HideInEditorMode, ReadOnly] public List<List<Transform>> SpawnedArrowGroups = new();

    ManagedCoroutine moveCor = null;
    ManagedCoroutine gameOverCor = null;

    Vector3 lastNavDest;
    float originSpeed;
    float originAccel;

    void Awake()
    {
        gameObject.tag = "Player"; // NPCController uses FindGameObjectWithTag("Player")

        moveCor = new(this);
        gameOverCor = new(this);

        if (NavAgent != null)
        {
            originSpeed = NavAgent.speed;
            originAccel = NavAgent.acceleration;
        }
        else
        {
            originSpeed = WalkSpeed;
            originAccel = WalkSpeed * 2f;
        }

        // Setup CharacterController for free-roam WASD movement
        _charController = GetComponent<CharacterController>();
        if (_charController == null)
        {
            _charController = gameObject.AddComponent<CharacterController>();
        }
        // Always enforce sane defaults (component may exist with zeroed values)
        if (_charController.height < 0.1f) _charController.height = 1.8f;
        if (_charController.radius < 0.01f) _charController.radius = 0.4f;
        if (_charController.center == Vector3.zero) _charController.center = new Vector3(0, _charController.height * 0.5f, 0);
        if (_charController.slopeLimit < 1f) _charController.slopeLimit = 45f;
        if (_charController.stepOffset < 0.01f) _charController.stepOffset = 0.4f;
        _charController.minMoveDistance = 0.001f;
    }

    public void Load(WorldSaved saved, WorldManager manager)
    {
        if (PathEndObjParent != null && WorldManager.Instance != null)
            PathEndObjParent.SetParent(WorldManager.Instance.VisualPathParent);

        // Ensure CharacterController exists
        if (_charController == null)
        {
            _charController = GetComponent<CharacterController>();
            if (_charController == null)
                _charController = gameObject.AddComponent<CharacterController>();
        }

        Vector3 spawnPos;
        if (saved != null && saved.WorldPlayerPos != Vector3.zero)
            spawnPos = saved.WorldPlayerPos;
        else if (manager != null && manager.PlayerSpawnPos != null)
            spawnPos = manager.PlayerSpawnPos.position;
        else
            spawnPos = transform.position;

        // Raycast down to snap to terrain surface
        if (Physics.Raycast(spawnPos + Vector3.up * 100f, Vector3.down, out var hit, 500f))
        {
            spawnPos = hit.point + Vector3.up * 0.1f; // Slightly above terrain
            Debug.Log($"Snapped spawn to terrain: {spawnPos} (hit: {hit.collider.name})");
        }
        else
        {
            Debug.LogWarning($"No terrain found below spawn {spawnPos}, using as-is");
        }

        // Disable NavAgent completely — CharacterController is primary
        if (NavAgent != null)
        {
            NavAgent.enabled = false;
        }

        // Warp CharacterController to terrain-snapped position
        _charController.enabled = false;
        transform.position = spawnPos;
        _charController.enabled = true;
        _verticalVelocity = 0f;

        if (saved != null && saved.WorldPlayerRot != Quaternion.identity)
            transform.rotation = saved.WorldPlayerRot;

        Debug.Log($"Player loaded at: {transform.position}");
    }

    // Combined movement vector — built each frame, applied once
    Vector3 _frameMovement;

    public void SetInvincible(float duration)
    {
        _invincibilityTimer = duration;
        Debug.Log($"[WorldPlayer] Invincible for {duration}s");
    }

    public void TeleportTo(Vector3 worldPos)
    {
        // Snap to terrain
        if (Physics.Raycast(worldPos + Vector3.up * 100f, Vector3.down, out var hit, 500f))
            worldPos = hit.point + Vector3.up * 0.1f;

        _charController.enabled = false;
        transform.position = worldPos;
        _charController.enabled = true;
        _verticalVelocity = 0f;
        Debug.Log($"[WorldPlayer] Teleported to {worldPos}");
    }

    void Update()
    {
        if (_charController == null || !_charController.enabled) return;

        // Tick invincibility
        if (_invincibilityTimer > 0)
        {
            _invincibilityTimer -= Time.deltaTime;
            // Flash the model while invincible
            if (ModelParent != null)
            {
                bool visible = Mathf.Sin(Time.time * 10f) > 0;
                foreach (var r in ModelParent.GetComponentsInChildren<Renderer>())
                    r.enabled = visible;
            }
            // Restore visibility when invincibility ends
            if (_invincibilityTimer <= 0 && ModelParent != null)
            {
                foreach (var r in ModelParent.GetComponentsInChildren<Renderer>())
                    r.enabled = true;
            }
        }

        _frameMovement = Vector3.zero;

        // Freeze movement while NPC dialogue is active
        if (SpiritComms.Instance != null && SpiritComms.Instance.IsActive)
        {
            // Still apply gravity so we don't float
            if (!_charController.isGrounded)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
                if (_verticalVelocity < -50f) _verticalVelocity = -50f;
            }
            else
            {
                _verticalVelocity = -2f;
            }
            _frameMovement.y = _verticalVelocity * Time.deltaTime;
            _charController.Move(_frameMovement);
            _isClickMoving = false;
            UpdateAnimations();
            return;
        }

        // Gravity + jump (check grounded BEFORE any Move call)
        if (_charController.isGrounded)
        {
            _verticalVelocity = -2f;
            if (Input.GetKeyDown(KeyCode.Space))
                _verticalVelocity = JumpForce;
        }
        else
        {
            _verticalVelocity += Gravity * Time.deltaTime;
            if (_verticalVelocity < -50f) _verticalVelocity = -50f;
        }
        _frameMovement.y = _verticalVelocity * Time.deltaTime;

        HandleWASDMovement();
        HandleClickMovement();

        // Single Move call with combined horizontal + vertical
        _charController.Move(_frameMovement);

        UpdateAnimations();
        AngleModelWithFloor();
        SavePosition();
    }

    // =========================================================================
    // WASD FREE-ROAM MOVEMENT
    // =========================================================================

    void HandleWASDMovement()
    {
        if (_charController == null || !_charController.enabled) return;

        float h = 0, v = 0;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1;

        IsSprinting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float speed = IsSprinting ? SprintSpeed : WalkSpeed;
        if (ProfessionManager.Instance != null)
            speed *= ProfessionManager.Instance.GetSpeedMultiplier();

        if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
        {
            _isClickMoving = false;

            var cam = WorldManager.Instance?.WorldCamera;
            if (cam == null) cam = Camera.main;

            Vector3 forward = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            forward.y = 0; forward.Normalize();
            right.y = 0; right.Normalize();

            Vector3 moveDir = (forward * v + right * h).normalized;

            if (moveDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir);
                ModelParent.rotation = Quaternion.Slerp(ModelParent.rotation, targetRot, RotationSpeed * Time.deltaTime);
            }

            // Add horizontal movement to frame total (vertical added in Update)
            _frameMovement += moveDir * speed * Time.deltaTime;
        }
    }

    void HandleClickMovement()
    {
        if (!_isClickMoving || _charController == null || !_charController.enabled) return;

        Vector3 toTarget = _clickMoveTarget - transform.position;
        toTarget.y = 0;

        if (toTarget.magnitude < ClickMoveStopDist)
        {
            _isClickMoving = false;
            DisablePathArrows();
            return;
        }

        Vector3 moveDir = toTarget.normalized;
        float speed = WalkSpeed;

        Quaternion targetRot = Quaternion.LookRotation(moveDir);
        ModelParent.rotation = Quaternion.Slerp(ModelParent.rotation, targetRot, RotationSpeed * Time.deltaTime);

        // Add horizontal movement to frame total
        _frameMovement += moveDir * speed * Time.deltaTime;
    }

    void UpdateAnimations()
    {
        if (_charController == null || Animator == null) return;
        float denom = SprintSpeed * AnimMaxMoveSpeedMultiplier;
        float moveSpeed = denom > 0.001f ? _charController.velocity.magnitude / denom : 0f;
        Animator.SetFloat(AnimMoveSpeedName, moveSpeed);
    }

    void SavePosition()
    {
        if (WorldManager.Instance != null)
        {
            WorldManager.Instance.WorldSaved.WorldPlayerPos = transform.position;
            WorldManager.Instance.WorldSaved.WorldPlayerRot = transform.rotation;
        }

        // Track distance walked for quests
        if (_lastPosition != Vector3.zero)
        {
            float moved = Vector3.Distance(transform.position, _lastPosition);
            if (moved < 5f) // ignore teleports
            {
                _distanceAccumulator += moved;
                if (_distanceAccumulator >= 10f)
                {
                    QuestManager.Instance?.ReportDistanceWalked(_distanceAccumulator);
                    _distanceAccumulator = 0f;
                }
            }
        }
        _lastPosition = transform.position;
    }
    void AngleModelWithFloor()
    {
        if (ModelParent == null || WorldManager.Instance == null) return;
        //Raycast to floor.
        if (Physics.Raycast(ModelParent.position + Vector3.up, Vector3.down, out RaycastHit hit, 10f, WorldManager.Instance.TerrainMask))
        {
            var uprightRot = ModelParent.rotation;
            uprightRot = Quaternion.FromToRotation(ModelParent.up, hit.normal) * uprightRot;
            //Debug.Log($"Hit: {hit.collider}", hit.collider);
            //ModelParent.rotation = Quaternion.Slerp(ModelParent.rotation, uprightRot, ModelUprightLerpSpeed * Time.deltaTime);

            float speed = ModelUprightLerpSpeed * Time.deltaTime;
            //Only on x and z.
            var desiredRot = ModelParent.rotation.LerpToLocked(uprightRot, speed, 0, speed);

            Vector3 desiredUp = desiredRot * Vector3.up;
            float angle = Vector3.Angle(Vector3.up, desiredUp);
            if (angle > ModelMaxTiltAngle)
            {
                float t = ModelMaxTiltAngle / angle;
                var clampedUp = Vector3.Slerp(Vector3.up, desiredUp, t);

                float yRot = desiredRot.eulerAngles.y;
                desiredRot = Quaternion.FromToRotation(Vector3.up, clampedUp) * Quaternion.Euler(0, yRot, 0);
            }

            ModelParent.rotation = desiredRot;
        }
    }

    void OnDisable()
    {
        ClearPathArrows();
    }

    /// <summary>Click-to-move: set a target and walk toward it using CharacterController.</summary>
    public void MoveTo(Vector3 pos)
    {
        _clickMoveTarget = pos;
        _isClickMoving = true;
        lastNavDest = pos; // Track for GameOver re-call check
        DisablePathArrows(); // Clear old path visuals
    }

    // Legacy NavAgent methods (kept for compatibility but CharacterController is primary)
    void SetNavDest(Vector3 dest)
    {
        if (NavAgent != null && NavAgent.enabled)
        {
            if (lastNavDest != dest)
            {
                lastNavDest = dest;
                NavAgent.destination = dest;
            }
        }
    }
    void UpdateVisualPath()
    {
        if (NavAgent == null || !NavAgent.enabled || !NavAgent.hasPath)
            return;
        var corners = NavAgent.path.corners;
        int lastIndex = corners.Length - 1;
        bool raiseEnd = true;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 corner = corners[i];
            int nextCornerIndex = i + 1;
            if (corners.TryGetIndex(nextCornerIndex, out Vector3 nextCorner))
            {
                //bool raiseEnd = nextCornerIndex == lastIndex;
                //If our last end was raised, that is our current corner now, so it must be raised as well.
                bool raiseStart = raiseEnd;
                //If the next corner isn't close to the final corner, then we'll raise our end point.
                raiseEnd = !nextCorner.CloseTo(corners[lastIndex], EndRingRadius * 2);

                //Raise the start and end positions of the arrow off the ground.
                var raise = Vector3.up * 1f;
                var start = corner + (raiseStart ? raise : Vector3.zero);
                //Don't raise the end pos of the last arrow.
                var end = nextCorner + (raiseEnd ? raise : Vector3.zero);
                //Shift the end point a little backwards so it doesn't poke through the ring.
                if (nextCornerIndex == lastIndex)
                    end -= (end - start).normalized * EndRingRadius;
                var toNext = end - start;
                var middlePos = Vector3.Lerp(start, end, 0.5f);

                //Arrow pooling.
                if (!SpawnedArrows.HasIndex(i))
                    SpawnedArrows.Add(Instantiate(PathArrowPrefab, WorldManager.Instance.VisualPathParent));
                Transform arrow = SpawnedArrows[i];
                //Debug.Log($"Enabling: {i}");
                arrow.gameObject.SetActiveCheck(true);
                arrow.localScale = arrow.localScale.SetZ(toNext.magnitude);
                arrow.SetPositionAndRotation(middlePos, Quaternion.LookRotation(toNext));
            }

            //Final corner, put ending ring and arrow.
            if (i == lastIndex)
            {
                PathEndObjParent.gameObject.SetActiveCheck(true);

                //Ring
                Vector3 ringPos = corner;
                Quaternion ringRot = PathEndRing.rotation;
                if (Physics.Raycast(corner, Vector3.down, out RaycastHit hit, 100f, WorldManager.Instance.TerrainMask))
                {
                    ringPos = hit.point;
                    //Rotate to position upwards.
                    ringRot = Quaternion.FromToRotation(PathEndRing.up, hit.normal) * ringRot;
                }
                PathEndRing.SetPositionAndRotation(ringPos, ringRot);

                //Arrow
                //Position arrow above ring, bobbing up and down.
                var arrowPos = ringPos + (Vector3.up * EndArrowHoverHeight);
                float bobValue = Mathf.Sin(Time.time * EndArrowBobSpeed) * EndArrowBobDist;
                arrowPos += Vector3.up * bobValue;
                //Look towards the player, ignoring verticality.
                var toPlayer = transform.position.SetY(0) - PathEndArrow.position.SetY(0);
                var arrowRot = Quaternion.LookRotation(toPlayer);
                PathEndArrow.SetPositionAndRotation(arrowPos, arrowRot);
            }
        }
        //Disable the rest. Subtract 1 because we spawn 1 less arrow (2 corners, 1 arrow), always.
        for (int i = lastIndex; i < SpawnedArrows.Count; i++)
        {
            //Debug.Log($"Disabling: {i}");
            SpawnedArrows[i].gameObject.SetActiveCheck(false);
        }
        //Debug.Log($"Corner count: {corners.Length}");
    }
    //void UpdateVisualPathDotted()
    //{
    //    if (!NavAgent.hasPath)
    //        return;
    //    var corners = NavAgent.path.corners;
    //    int lastIndex = corners.Length - 1;
    //    for (int i = 0; i < corners.Length; i++)
    //    {
    //        Vector3 corner = corners[i];
    //        int nextCornerIndex = i + 1;
    //        if (corners.TryGetIndex(nextCornerIndex, out Vector3 nextCorner))
    //        {
    //            //Raise the start and end positions of the arrow off the ground.
    //            var raise = Vector3.up * 1f;
    //            var start = corner + raise;
    //            //Don't raise the end pos of the last arrow.
    //            var end = nextCorner + (nextCornerIndex == lastIndex ? Vector3.zero : raise);
    //            //Shift the end point a little backwards so it doesn't poke through the ring.
    //            if (nextCornerIndex == lastIndex)
    //                end -= (end - start).normalized * EndRingRadius;
    //            var toNext = end - start;

    //            if (!SpawnedArrowGroups.HasIndex(i))
    //                SpawnedArrowGroups.Add(new());

    //            float maxDist = toNext.magnitude;
    //            float dottedArrowCount = maxDist / DottedPathObjSpacing;
    //            Debug.Log(dottedArrowCount);
    //            for (int j = 0; j < dottedArrowCount; j++)
    //            {
    //                var pos = end - (toNext.normalized * (j * DottedPathObjSpacing));

    //                //Arrow pooling.
    //                if (!SpawnedArrowGroups[i].HasIndex(j))
    //                    SpawnedArrowGroups[i].Add(Instantiate(DottedPathObjPrefab, WorldManager.Instance.VisualPathParent));
    //                Transform arrow = SpawnedArrowGroups[i][j];
    //                arrow.gameObject.SetActiveCheck(true);
    //                arrow.localScale = arrow.localScale.SetZ(DottedPathObjLength);
    //                arrow.SetPositionAndRotation(pos, Quaternion.LookRotation(toNext));
    //            }
    //            //Disable the rest. Subtract 1 because we spawn 1 less arrow (2 corners, 1 arrow), always.
    //            for (int x = (int)dottedArrowCount; x < SpawnedArrowGroups[i].Count; x++)
    //            {
    //                Debug.Log($"Disabling: {x}");
    //                SpawnedArrowGroups[i][x].gameObject.SetActiveCheck(false);
    //            }

    //            //var middlePos = Vector3.Lerp(start, end, 0.5f);

    //            ////Arrow pooling.
    //            //if (!SpawnedArrows.HasIndex(i))
    //            //    SpawnedArrows.Add(Instantiate(PathArrowPrefab, WorldManager.Instance.VisualPathParent));
    //            //Transform arrow = SpawnedArrows[i];
    //            ////Debug.Log($"Enabling: {i}");
    //            //arrow.gameObject.SetActiveCheck(true);
    //            //arrow.localScale = arrow.localScale.SetZ(toNext.magnitude);
    //            //arrow.SetPositionAndRotation(middlePos, Quaternion.LookRotation(toNext));
    //        }

    //        //Final corner, put ending arrow.
    //        if (i == lastIndex)
    //        {
    //            PathEndObjParent.gameObject.SetActiveCheck(true);

    //            //Ring
    //            Vector3 ringPos = corner;
    //            Quaternion ringRot = PathEndRing.rotation;
    //            if (Physics.Raycast(corner, Vector3.down, out RaycastHit hit, 100f, WorldManager.Instance.TouchWorldMask))
    //            {
    //                ringPos = hit.point;
    //                //Rotate to position upwards.
    //                ringRot = Quaternion.FromToRotation(PathEndRing.up, hit.normal) * ringRot;
    //            }
    //            PathEndRing.SetPositionAndRotation(ringPos, ringRot);

    //            //Arrow
    //            //Position arrow above ring, bobbing up and down.
    //            var arrowPos = ringPos + (PathEndRing.up * EndArrowHoverHeight);
    //            float bobValue = Mathf.Sin(Time.time * EndArrowBobSpeed) * EndArrowBobDist;
    //            arrowPos += Vector3.up * bobValue;
    //            //Look towards the player, ignoring verticality.
    //            var toPlayer = transform.position.SetY(0) - PathEndArrow.position.SetY(0);
    //            var arrowRot = Quaternion.LookRotation(toPlayer);
    //            PathEndArrow.SetPositionAndRotation(arrowPos, arrowRot);
    //        }
    //    }
    //    //Debug.Log($"Corner count: {corners.Length}");
    //}
    void DisablePathArrows()
    {
        SpawnedArrowGroups.ForEach(x => x.ForEach(x => x.gameObject.SetActiveCheck(false)));
        SpawnedArrows.ForEach(x => x.gameObject.SetActiveCheck(false));
        if (PathEndObjParent != null)
            PathEndObjParent.gameObject.SetActiveCheck(false);
    }
    void ClearPathArrows()
    {
        foreach (var arrow in SpawnedArrows)
        {
            if (arrow)
                Destroy(arrow.gameObject);
        }
        SpawnedArrows.Clear();
    }

    public bool IsNavAgentDone()
    {
        // With CharacterController, check click-move state
        if (!_isClickMoving) return true;

        // Legacy NavAgent check (if still enabled)
        if (NavAgent != null && NavAgent.enabled && NavAgent.isOnNavMesh)
        {
            return !NavAgent.pathPending
                && NavAgent.remainingDistance <= NavAgent.stoppingDistance
                && (!NavAgent.hasPath || NavAgent.velocity.sqrMagnitude < 0.01f);
        }

        return !_isClickMoving;
    }

    public override void OnInteract(InteractCtx ctx)
    {
        // Camera always follows player — no toggle
        base.OnInteract(ctx);
    }

    public Coroutine GameOver()
    {
        gameOverCor.Start(GameOverCor);

        return gameOverCor.RunningCor;
    }
    IEnumerator GameOverCor(object[] args)
    {
        //Start crying.
        Animator.SetBool(GameOverAnimBool, true);
        var playerHouse = WorldManager.Instance.PlayerHouse;

        Delayer maxDelay = new(30f);
        //Wait till we get home, or delay.
        while (playerHouse.PlayerInside != this && !maxDelay.IsReady())
        {
            maxDelay.Update(Time.deltaTime);
            //Run home.
            if (lastNavDest != playerHouse.EnterPos.position)
                MoveTo(playerHouse.EnterPos.position);

            yield return null;
        }

        gameOverCor.OnCorEnd();
    }
}
