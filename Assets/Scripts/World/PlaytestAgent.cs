using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Automated playtest agent that controls the WorldPlayer to test gameplay loops.
/// Three archetypes: Collector (catch all Spiritkin), Explorer (find all crystals/lore),
/// Crafter (gather materials and craft a target item).
///
/// Activate via console or DevTools: PlaytestAgent.StartTest(AgentType.Collector)
/// Logs progress and blockers to Debug.Log with [PLAYTEST] prefix.
/// </summary>
public class PlaytestAgent : MonoBehaviour
{
    public static PlaytestAgent Instance { get; private set; }

    public enum AgentType { Collector, Explorer, Crafter }
    public enum AgentState { Idle, Moving, Interacting, InBattle, Waiting, Done, Stuck }

    // =========================================================================
    // CONFIG
    // =========================================================================

    [Header("Agent")]
    public AgentType Type = AgentType.Collector;
    public bool IsRunning;
    public AgentState State = AgentState.Idle;

    [Header("Movement")]
    public float MoveSpeed = 5f;
    public float InteractRange = 2.5f;
    public float StuckTimeout = 15f;

    // =========================================================================
    // STATE
    // =========================================================================

    Vector3 _targetPos;
    float _stuckTimer;
    Vector3 _lastPos;
    float _posCheckTimer;
    int _actionsCompleted;
    int _battlesFought;
    int _itemsCollected;
    float _totalRunTime;
    List<string> _blockers = new();
    Coroutine _agentCoroutine;

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>Start a playtest agent of the given type.</summary>
    public static void StartTest(AgentType type)
    {
        if (Instance == null)
        {
            var go = new GameObject("PlaytestAgent");
            Instance = go.AddComponent<PlaytestAgent>();
        }

        Instance.Type = type;
        Instance.BeginTest();
    }

    /// <summary>Stop the current playtest.</summary>
    public static void StopTest()
    {
        if (Instance != null && Instance.IsRunning)
            Instance.EndTest("Manual stop");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // =========================================================================
    // TEST EXECUTION
    // =========================================================================

    void BeginTest()
    {
        if (IsRunning) EndTest("Restarting");

        IsRunning = true;
        State = AgentState.Idle;
        _actionsCompleted = 0;
        _battlesFought = 0;
        _itemsCollected = 0;
        _totalRunTime = 0;
        _blockers.Clear();

        Log($"=== PLAYTEST START: {Type} ===");
        LogGoals();

        _agentCoroutine = StartCoroutine(RunAgent());
    }

    void EndTest(string reason)
    {
        IsRunning = false;
        State = AgentState.Done;
        if (_agentCoroutine != null) { StopCoroutine(_agentCoroutine); _agentCoroutine = null; }

        Log($"=== PLAYTEST END: {Type} ===");
        Log($"Reason: {reason}");
        Log($"Runtime: {_totalRunTime:F0}s | Actions: {_actionsCompleted} | Battles: {_battlesFought} | Items: {_itemsCollected}");

        if (_blockers.Count > 0)
        {
            Log($"BLOCKERS FOUND ({_blockers.Count}):");
            foreach (var b in _blockers) Log($"  - {b}");
        }
        else
        {
            Log("No blockers found — loop completed successfully!");
        }

        LogProgress();
    }

    void Update()
    {
        if (!IsRunning) return;
        _totalRunTime += Time.deltaTime;

        // Stuck detection
        _posCheckTimer += Time.deltaTime;
        if (_posCheckTimer >= 3f)
        {
            _posCheckTimer = 0;
            var player = GetPlayer();
            if (player != null)
            {
                float moved = Vector3.Distance(player.transform.position, _lastPos);
                _lastPos = player.transform.position;

                if (moved < 0.5f && State == AgentState.Moving)
                {
                    _stuckTimer += 3f;
                    if (_stuckTimer > StuckTimeout)
                    {
                        _blockers.Add($"Stuck at {player.transform.position} after {_stuckTimer:F0}s — couldn't reach target {_targetPos}");
                        Log($"STUCK! Picking new target...");
                        _stuckTimer = 0;
                        State = AgentState.Idle; // Will pick new target next cycle
                    }
                }
                else
                {
                    _stuckTimer = 0;
                }
            }
        }

        // Skip movement while in battle or dialogue
        if (SpiritComms.Instance != null && SpiritComms.Instance.IsActive)
        {
            State = AgentState.Interacting;
            return;
        }
        if (GameLoader.CurrentManager != null)
        {
            State = AgentState.InBattle;
            return;
        }
    }

    // =========================================================================
    // AGENT COROUTINES
    // =========================================================================

    IEnumerator RunAgent()
    {
        // Wait for world to load
        while (WorldManager.Instance?.WorldPlayer == null)
            yield return new WaitForSeconds(0.5f);

        yield return new WaitForSeconds(2f); // Let everything initialize
        _lastPos = GetPlayer().transform.position;

        switch (Type)
        {
            case AgentType.Collector:
                yield return RunCollector();
                break;
            case AgentType.Explorer:
                yield return RunExplorer();
                break;
            case AgentType.Crafter:
                yield return RunCrafter();
                break;
        }
    }

    // =========================================================================
    // COLLECTOR — fight every enemy, collect all Spiritkin
    // =========================================================================

    IEnumerator RunCollector()
    {
        Log("Goal: Fight wild Spiritkin, collect as many unique cards as possible");

        int maxLoops = 50; // Safety cap
        for (int loop = 0; loop < maxLoops; loop++)
        {
            // Wait if in battle or dialogue
            while (GameLoader.CurrentManager != null ||
                   (SpiritComms.Instance != null && SpiritComms.Instance.IsActive))
            {
                yield return new WaitForSeconds(0.5f);
            }

            // Find nearest enemy
            var enemy = FindNearestEnemy();
            if (enemy != null)
            {
                Log($"Targeting enemy: {enemy.EnemyName} ({enemy.CardRarity}) at {enemy.transform.position}");
                yield return MoveToPosition(enemy.transform.position, 1.5f);

                // Wait for battle to start and finish
                yield return new WaitForSeconds(1f);
                float battleTimeout = 60f;
                while (GameLoader.CurrentManager != null && battleTimeout > 0)
                {
                    battleTimeout -= 0.5f;
                    yield return new WaitForSeconds(0.5f);
                }

                if (battleTimeout <= 0)
                    _blockers.Add($"Battle against {enemy.EnemyName} timed out after 60s");

                _battlesFought++;
                _actionsCompleted++;

                // Wait for return to world
                yield return new WaitForSeconds(2f);
            }
            else
            {
                // No enemies nearby — wander to find some
                Log("No enemies found, wandering...");
                Vector3 wanderTarget = GetPlayer().transform.position +
                    new Vector3(Random.Range(-30f, 30f), 0, Random.Range(-30f, 30f));
                yield return MoveToPosition(wanderTarget, 3f);
                yield return new WaitForSeconds(2f);
            }

            // Log progress every 10 actions
            if (_actionsCompleted % 10 == 0 && _actionsCompleted > 0)
                LogProgress();

            // Check win condition: 5+ unique cards
            var data = MainPlayerData.Instance;
            if (data != null && data.SavedCards.Count >= 5)
            {
                Log($"Collector goal reached: {data.SavedCards.Count} unique Spiritkin collected!");
                break;
            }
        }

        EndTest("Collector loop complete");
    }

    // =========================================================================
    // EXPLORER — find all crystals, lore tablets, viewpoints
    // =========================================================================

    IEnumerator RunExplorer()
    {
        Log("Goal: Discover all crystals, lore tablets, and viewpoints");

        // Find all collectibles in the scene
        var collectibles = FindObjectsByType<WorldCollectible>(FindObjectsSortMode.None);
        Log($"Found {collectibles.Length} collectibles in world");

        int collected = 0;
        foreach (var collectible in collectibles)
        {
            if (collectible == null || collectible.IsCollected) continue;

            // Wait if in battle or dialogue
            while (GameLoader.CurrentManager != null ||
                   (SpiritComms.Instance != null && SpiritComms.Instance.IsActive))
            {
                yield return new WaitForSeconds(0.5f);
            }

            Log($"Heading to {collectible.Type}: {collectible.DisplayName} at {collectible.transform.position}");
            yield return MoveToPosition(collectible.transform.position, InteractRange);

            // Simulate E key press — handled by WorldCollectible.Update()
            // We just need to be in range; the collectible checks distance
            yield return new WaitForSeconds(1f);

            if (collectible.IsCollected)
            {
                collected++;
                _itemsCollected++;
                Log($"Collected: {collectible.DisplayName} ({collected}/{collectibles.Length})");
            }
            else
            {
                _blockers.Add($"Could not collect {collectible.DisplayName} at {collectible.transform.position} — in range but interaction failed");
            }

            _actionsCompleted++;
            yield return new WaitForSeconds(0.5f);
        }

        // Also visit all 4 regions
        Log("Checking region visits...");
        var data = MainPlayerData.Instance;
        if (data != null)
        {
            string[] regions = { "frost_valley", "rolling_hills", "volcanic_isles", "dark_castle" };
            foreach (var r in regions)
            {
                if (!data.VisitedRegions.Contains(r))
                    _blockers.Add($"Region not visited: {r}");
            }
            Log($"Regions visited: {data.VisitedRegions.Count}/4");
        }

        EndTest($"Explorer loop complete — {collected} collectibles found");
    }

    // =========================================================================
    // CRAFTER — gather materials, craft a target item
    // =========================================================================

    IEnumerator RunCrafter()
    {
        Log("Goal: Gather materials from resource nodes, craft an item");

        // Phase 1: Harvest resource nodes
        var nodes = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
        Log($"Found {nodes.Length} resource nodes in world");

        int harvested = 0;
        int targetHarvests = Mathf.Min(10, nodes.Length);

        foreach (var node in nodes)
        {
            if (node == null || node.IsDepleted) continue;
            if (harvested >= targetHarvests) break;

            // Wait if in battle or dialogue
            while (GameLoader.CurrentManager != null ||
                   (SpiritComms.Instance != null && SpiritComms.Instance.IsActive))
            {
                yield return new WaitForSeconds(0.5f);
            }

            Log($"Heading to resource: {node.NodeName ?? node.MaterialId} at {node.transform.position}");
            yield return MoveToPosition(node.transform.position, 2f);

            // Simulate harvest (E key near node triggers GatheringManager)
            yield return new WaitForSeconds(0.5f);

            // Wait for harvest to complete (channeled)
            float harvestTimeout = 10f;
            while (node.IsHarvesting && harvestTimeout > 0)
            {
                harvestTimeout -= 0.5f;
                yield return new WaitForSeconds(0.5f);
            }

            if (node.IsDepleted)
            {
                harvested++;
                _itemsCollected++;
                Log($"Harvested: {node.NodeName ?? node.MaterialId} ({harvested}/{targetHarvests})");
            }

            _actionsCompleted++;
            yield return new WaitForSeconds(0.5f);
        }

        // Phase 2: Check if we have enough materials to craft
        var data = MainPlayerData.Instance;
        if (data != null)
        {
            Log($"Materials collected: {data.Materials.Count} types");
            foreach (var mat in data.Materials)
                Log($"  {mat.Key}: {mat.Value}");

            if (data.Essences.Count > 0)
                Log($"Essences: {data.Essences.Count}");
            else
                _blockers.Add("No essences collected — need essences to craft");

            // Try to craft if CraftingManager exists
            if (CraftingManager.Instance != null)
            {
                Log("CraftingManager exists — crafting loop available");
                // Actual crafting requires SchematicData ScriptableObjects loaded in the scene
                // Log what's available
                var schematics = Resources.FindObjectsOfTypeAll<SchematicData>();
                if (schematics.Length > 0)
                {
                    Log($"Available schematics: {schematics.Length}");
                    foreach (var s in schematics)
                        Log($"  {s.SchematicName} ({s.Category}, {s.Tier})");
                }
                else
                {
                    _blockers.Add("No SchematicData assets found — crafting cannot be tested without schematics");
                }
            }
            else
            {
                _blockers.Add("CraftingManager not found in scene — crafting system not initialized");
            }
        }

        EndTest($"Crafter loop complete — {harvested} nodes harvested");
    }

    // =========================================================================
    // MOVEMENT
    // =========================================================================

    IEnumerator MoveToPosition(Vector3 target, float stopDist)
    {
        State = AgentState.Moving;
        _targetPos = target;
        var player = GetPlayer();
        if (player == null) yield break;

        float timeout = 30f;
        while (Vector3.Distance(player.transform.position, target) > stopDist && timeout > 0)
        {
            // Skip if in battle or dialogue
            if (GameLoader.CurrentManager != null ||
                (SpiritComms.Instance != null && SpiritComms.Instance.IsActive))
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            // Direct movement toward target
            Vector3 dir = (target - player.transform.position);
            dir.y = 0;
            if (dir.magnitude > 0.1f)
            {
                dir.Normalize();
                player.transform.position += dir * MoveSpeed * Time.deltaTime;
            }

            timeout -= Time.deltaTime;
            yield return null;
        }

        State = AgentState.Idle;
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    WorldPlayer GetPlayer() => WorldManager.Instance?.WorldPlayer;

    RoamingEnemy FindNearestEnemy()
    {
        if (EnemySpawner.Instance == null) return null;
        var player = GetPlayer();
        if (player == null) return null;

        RoamingEnemy nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var enemy in EnemySpawner.Instance.ActiveEnemies)
        {
            if (enemy == null || enemy.State == RoamingEnemy.EnemyState.Dead) continue;
            float dist = Vector3.Distance(player.transform.position, enemy.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = enemy;
            }
        }

        return nearest;
    }

    // =========================================================================
    // LOGGING
    // =========================================================================

    void Log(string msg) => Debug.Log($"[PLAYTEST:{Type}] {msg}");

    void LogGoals()
    {
        switch (Type)
        {
            case AgentType.Collector:
                Log("Win battles → collect Spiritkin cards → build a team of 3 → defeat trainers");
                var data = MainPlayerData.Instance;
                Log($"Current cards: {data?.SavedCards.Count ?? 0} | Wins: {data?.DefeatedGhostCount ?? 0}");
                break;
            case AgentType.Explorer:
                Log("Visit all 4 regions → find all crystals/lore/viewpoints → complete Mask of Destiny");
                break;
            case AgentType.Crafter:
                Log("Harvest 10 resource nodes → collect essences → open crafting → craft an item");
                break;
        }
    }

    void LogProgress()
    {
        var data = MainPlayerData.Instance;
        if (data == null) return;

        Log($"--- Progress Report ({_totalRunTime:F0}s) ---");
        Log($"Gold: {data.Gold} | Cards: {data.SavedCards.Count} | Wins: {data.DefeatedGhostCount}");
        Log($"Materials: {data.Materials.Count} types | Essences: {data.Essences.Count}");
        Log($"Regions: {data.VisitedRegions.Count}/4 | Zones: {data.VisitedZones.Count}");
        Log($"Lore: {data.DiscoveredLore.Count} | Crystals: {data.FoundCrystals.Count} | Viewpoints: {data.FoundViewpoints.Count}");
        Log($"Blockers so far: {_blockers.Count}");
    }
}
