using UnityEngine;

/// <summary>
/// Wild Spiritkin enemy that patrols, detects, chases, and triggers battles.
/// Full state machine: Patrol → Alert → Chase → Battle / Returning.
/// Does NOT use NavMeshAgent — uses CharacterController-style direct movement.
/// </summary>
public class RoamingEnemy : MonoBehaviour
{
    public enum EnemyState { Patrol, Alert, Chase, Returning, Dead }

    [Header("Identity")]
    public string EnemyName;
    public int CardId;
    public string CardRarity;
    public int CardMaxHp;
    public bool IsElite;

    [Header("Config")]
    public float PatrolSpeed = 2f;
    public float ChaseSpeed = 4f;
    public float DetectRange = 6f;
    public float BattleTriggerRange = 1.2f;
    public float AlertDuration = 0.6f;
    public float GiveUpRange = 25f; // Distance from spawn before giving up chase
    public float PatrolRadius = 10f;

    [Header("State")]
    public EnemyState State = EnemyState.Patrol;

    Vector3 _spawnPosition;
    Vector3 _patrolTarget;
    float _patrolTimer;
    float _alertTimer;

    // Card data reference
    AllCardsData.CardEntry? _cardData;

    public void Initialize(AllCardsData.CardEntry card, bool isElite)
    {
        _cardData = card;
        CardId = card.Id;
        EnemyName = card.Name;
        CardRarity = card.Rarity;
        CardMaxHp = card.MaxHp;
        IsElite = isElite;
        _spawnPosition = transform.position;

        if (isElite)
        {
            // Elite: more HP (visuals handled by EnemyModelBuilder)
            CardMaxHp = Mathf.CeilToInt(CardMaxHp * 1.5f);
        }

        gameObject.name = $"Enemy_{card.Name}_{(isElite ? "ELITE" : "normal")}";
        PickPatrolTarget();
    }

    /// <summary>Called by EnemySpawner.Update() — not MonoBehaviour Update.</summary>
    public void UpdateAI(Vector3 playerPos)
    {
        switch (State)
        {
            case EnemyState.Patrol:
                UpdatePatrol(playerPos);
                break;
            case EnemyState.Alert:
                UpdateAlert(playerPos);
                break;
            case EnemyState.Chase:
                UpdateChase(playerPos);
                break;
            case EnemyState.Returning:
                UpdateReturning();
                break;
        }

        // Gravity — keep on terrain
        SnapToTerrain();
    }

    // =========================================================================
    // PATROL
    // =========================================================================

    void UpdatePatrol(Vector3 playerPos)
    {
        // Check for player detection
        float distToPlayer = Vector3.Distance(transform.position, playerPos);
        if (distToPlayer < DetectRange)
        {
            State = EnemyState.Alert;
            _alertTimer = AlertDuration;
            // TODO: Show "!" alert icon
            return;
        }

        // Move toward patrol target
        Vector3 toTarget = _patrolTarget - transform.position;
        toTarget.y = 0;

        if (toTarget.magnitude < 1f)
        {
            PickPatrolTarget();
            return;
        }

        _patrolTimer -= Time.deltaTime;
        if (_patrolTimer <= 0)
        {
            PickPatrolTarget();
            return;
        }

        MoveToward(_patrolTarget, PatrolSpeed);
        FaceDirection(toTarget);
    }

    void PickPatrolTarget()
    {
        var offset = Random.insideUnitCircle * PatrolRadius;
        _patrolTarget = _spawnPosition + new Vector3(offset.x, 0, offset.y);
        _patrolTimer = Random.Range(5f, 15f);
    }

    // =========================================================================
    // ALERT (brief pause before chase)
    // =========================================================================

    void UpdateAlert(Vector3 playerPos)
    {
        _alertTimer -= Time.deltaTime;
        if (_alertTimer <= 0)
        {
            State = EnemyState.Chase;
        }

        // Face the player during alert
        FaceDirection(playerPos - transform.position);
    }

    // =========================================================================
    // CHASE
    // =========================================================================

    void UpdateChase(Vector3 playerPos)
    {
        float distToPlayer = Vector3.Distance(transform.position, playerPos);
        float distFromSpawn = Vector3.Distance(transform.position, _spawnPosition);

        // Battle trigger!
        if (distToPlayer < BattleTriggerRange)
        {
            TriggerBattle();
            return;
        }

        // Give up if too far from spawn or player escaped detection range * 1.5
        if (distFromSpawn > GiveUpRange || distToPlayer > DetectRange * 2.5f)
        {
            State = EnemyState.Returning;
            return;
        }

        // Chase the player
        MoveToward(playerPos, ChaseSpeed);
        FaceDirection(playerPos - transform.position);
    }

    // =========================================================================
    // RETURNING (walk back to spawn after giving up)
    // =========================================================================

    void UpdateReturning()
    {
        float distFromSpawn = Vector3.Distance(transform.position, _spawnPosition);
        if (distFromSpawn < 2f)
        {
            State = EnemyState.Patrol;
            PickPatrolTarget();
            return;
        }

        MoveToward(_spawnPosition, PatrolSpeed);
        FaceDirection(_spawnPosition - transform.position);
    }

    // =========================================================================
    // BATTLE TRIGGER
    // =========================================================================

    void TriggerBattle()
    {
        State = EnemyState.Dead;
        Debug.Log($"[Enemy] Battle triggered: {EnemyName} (HP:{CardMaxHp}, Elite:{IsElite})");

        // Notify OverworldIntegration to start a battle
        OverworldIntegration.Instance?.TriggerEnemyBattle(this);
    }

    // =========================================================================
    // MOVEMENT HELPERS
    // =========================================================================

    void MoveToward(Vector3 target, float speed)
    {
        Vector3 dir = (target - transform.position);
        dir.y = 0;
        dir.Normalize();

        transform.position += dir * speed * Time.deltaTime;
    }

    void FaceDirection(Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 8f * Time.deltaTime);
        }
    }

    void SnapToTerrain()
    {
        if (Physics.Raycast(transform.position + Vector3.up * 50, Vector3.down, out var hit, 100f))
        {
            var pos = transform.position;
            pos.y = hit.point.y;
            transform.position = pos;
        }
    }
}
