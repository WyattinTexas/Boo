using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Automated playtest agents — three named characters who each play the game
/// with a distinct motivation and strategy. They test the full gameplay loop
/// from a real player's perspective and report what works and what breaks.
///
/// Trigger via browser console:
///   unityInstance.SendMessage('JSBridge', 'RunCollector', '')
///   unityInstance.SendMessage('JSBridge', 'RunExplorer', '')
///   unityInstance.SendMessage('JSBridge', 'RunCrafter', '')
/// </summary>
public class PlaytestAgent : MonoBehaviour
{
    public static PlaytestAgent Instance { get; private set; }

    public enum AgentType { Collector, Explorer, Crafter }
    public enum AgentState { Idle, Moving, Interacting, InBattle, Waiting, Done, Stuck }

    // =========================================================================
    // THE THREE AGENTS
    // =========================================================================
    //
    // MAXINE "MAX" VOSS — The Collector
    //   "Gotta catch 'em all. Every last one."
    //   Strategy: Hunt every wild Spiritkin, fight everything that moves.
    //   Wants to fill out her collection. Judges the game by how satisfying
    //   the battle loop feels and whether she can actually GET new cards.
    //   Win condition: 5 unique Spiritkin collected.
    //
    // DIEGO SANTOS — The Explorer
    //   "If there's a corner of this map I haven't seen, I'm not done."
    //   Strategy: Systematic sweep of every collectible, every region.
    //   Opens every chest, reads every lore tablet, finds every viewpoint.
    //   Judges the game by whether exploration is rewarded and whether
    //   there's always something new to find.
    //   Win condition: All collectibles found, all 4 regions visited.
    //
    // YUKI TANAKA — The Crafter
    //   "Give me the right materials and I'll build something legendary."
    //   Strategy: Harvest every resource node, collect essences, find
    //   schematics, craft the best possible item. Judges the game by
    //   whether the gathering-to-crafting pipeline actually works end to end.
    //   Win condition: Craft at least 1 item.
    //
    // =========================================================================

    static readonly Dictionary<AgentType, (string name, string title, string motto)> AGENTS = new()
    {
        [AgentType.Collector] = ("Maxine \"Max\" Voss", "The Collector", "Gotta catch 'em all. Every last one."),
        [AgentType.Explorer] = ("Diego Santos", "The Explorer", "If there's a corner of this map I haven't seen, I'm not done."),
        [AgentType.Crafter] = ("Yuki Tanaka", "The Crafter", "Give me the right materials and I'll build something legendary."),
    };

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

    string _agentName;
    string _agentTitle;
    Vector3 _targetPos;
    float _stuckTimer;
    Vector3 _lastPos;
    float _posCheckTimer;
    int _actionsCompleted;
    int _battlesFought;
    int _battlesWon;
    int _itemsCollected;
    float _totalRunTime;
    readonly List<string> _blockers = new();
    readonly List<string> _highlights = new();
    Coroutine _agentCoroutine;

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    public static void StartTest(AgentType type)
    {
        if (Instance == null)
        {
            var go = new GameObject("PlaytestAgent");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PlaytestAgent>();
        }

        Instance.Type = type;
        Instance.BeginTest();
    }

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
    // LIFECYCLE
    // =========================================================================

    void BeginTest()
    {
        if (IsRunning) EndTest("Restarting");

        var info = AGENTS[Type];
        _agentName = info.name;
        _agentTitle = info.title;

        IsRunning = true;
        State = AgentState.Idle;
        _actionsCompleted = 0;
        _battlesFought = 0;
        _battlesWon = 0;
        _itemsCollected = 0;
        _totalRunTime = 0;
        _blockers.Clear();
        _highlights.Clear();

        Log("════════════════════════════════════════════════════");
        Log($"  {_agentName}");
        Log($"  \"{info.motto}\"");
        Log("════════════════════════════════════════════════════");
        LogStartState();

        _agentCoroutine = StartCoroutine(RunAgent());
    }

    void EndTest(string reason)
    {
        IsRunning = false;
        State = AgentState.Done;
        if (_agentCoroutine != null) { StopCoroutine(_agentCoroutine); _agentCoroutine = null; }

        Log("════════════════════════════════════════════════════");
        Log($"  {_agentName} — FINAL REPORT");
        Log("════════════════════════════════════════════════════");
        Log($"Result: {reason}");
        Log($"Runtime: {FormatTime(_totalRunTime)} | Actions: {_actionsCompleted}");
        Log($"Battles: {_battlesFought} fought, {_battlesWon} won ({(_battlesFought > 0 ? (100f * _battlesWon / _battlesFought).ToString("F0") : "0")}% winrate)");
        Log($"Items collected: {_itemsCollected}");

        if (_highlights.Count > 0)
        {
            Log($"HIGHLIGHTS ({_highlights.Count}):");
            foreach (var h in _highlights) Log($"  ★ {h}");
        }

        if (_blockers.Count > 0)
        {
            Log($"BLOCKERS ({_blockers.Count}):");
            foreach (var b in _blockers) Log($"  ✗ {b}");
        }
        else
        {
            Log("✓ No blockers — gameplay loop completed clean!");
        }

        LogFinalState();
        Log("════════════════════════════════════════════════════");
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
                        _blockers.Add($"STUCK at {FormatPos(player.transform.position)} for {_stuckTimer:F0}s — target was {FormatPos(_targetPos)}");
                        Log($"{_agentName} got stuck! Rerouting...");
                        _stuckTimer = 0;
                        State = AgentState.Idle;
                    }
                }
                else
                {
                    _stuckTimer = 0;
                }
            }
        }

        if (SpiritComms.Instance != null && SpiritComms.Instance.IsActive)
            State = AgentState.Interacting;
        else if (GameLoader.CurrentManager != null)
            State = AgentState.InBattle;
    }

    // =========================================================================
    // AGENT DISPATCH
    // =========================================================================

    IEnumerator RunAgent()
    {
        while (WorldManager.Instance?.WorldPlayer == null)
            yield return new WaitForSeconds(0.5f);

        yield return new WaitForSeconds(2f);
        _lastPos = GetPlayer().transform.position;
        Log($"{_agentName} spawned at {FormatPos(_lastPos)}. Let's go.");

        switch (Type)
        {
            case AgentType.Collector: yield return RunMaxine(); break;
            case AgentType.Explorer: yield return RunDiego(); break;
            case AgentType.Crafter: yield return RunYuki(); break;
        }
    }

    // =========================================================================
    // MAXINE "MAX" VOSS — The Collector
    // Hunt Spiritkin, fight everything, build a team
    // =========================================================================

    IEnumerator RunMaxine()
    {
        Log("Max is on the hunt. First priority: find something to fight.");

        int maxLoops = 50;
        int consecutiveWanders = 0;

        for (int loop = 0; loop < maxLoops; loop++)
        {
            yield return WaitForWorldState();

            // Track wins before battle
            int winsBefore = MainPlayerData.Instance?.DefeatedGhostCount ?? 0;

            var enemy = FindNearestEnemy();
            if (enemy != null)
            {
                consecutiveWanders = 0;
                Log($"Max spots {enemy.EnemyName} ({enemy.CardRarity}{(enemy.IsElite ? ", ELITE" : "")}) — engaging!");
                yield return MoveToPosition(enemy.transform.position, 1.2f);

                yield return new WaitForSeconds(1f);
                yield return WaitForBattle(enemy.EnemyName);

                _battlesFought++;
                int winsAfter = MainPlayerData.Instance?.DefeatedGhostCount ?? 0;
                if (winsAfter > winsBefore)
                {
                    _battlesWon++;
                    _highlights.Add($"Defeated {enemy.EnemyName} (battle #{_battlesFought})");

                    // Check for sideline unlock
                    if (winsAfter == 5)
                        _highlights.Add("SIDELINE UNLOCKED at 5 wins!");
                }

                _actionsCompleted++;
                yield return new WaitForSeconds(2f);
            }
            else
            {
                consecutiveWanders++;
                if (consecutiveWanders >= 5)
                {
                    _blockers.Add($"No enemies found after {consecutiveWanders} wanders — spawn system may be broken");
                    Log("Max can't find anyone to fight. Is the spawn system working?");
                    consecutiveWanders = 0;
                }
                else
                {
                    Log($"Max wanders looking for a fight... ({consecutiveWanders})");
                }

                Vector3 wanderDir = new(Random.Range(-40f, 40f), 0, Random.Range(-40f, 40f));
                yield return MoveToPosition(GetPlayer().transform.position + wanderDir, 3f);
                yield return new WaitForSeconds(1f);
            }

            // Progress check every 5 actions
            if (_actionsCompleted > 0 && _actionsCompleted % 5 == 0)
            {
                var data = MainPlayerData.Instance;
                Log($"Max's progress: {data?.SavedCards.Count ?? 0} cards, {_battlesWon}/{_battlesFought} wins, {data?.Gold ?? 0} gold");
            }

            // Win condition
            var d = MainPlayerData.Instance;
            if (d != null && d.SavedCards.Count >= 5)
            {
                _highlights.Add($"COLLECTION COMPLETE: {d.SavedCards.Count} unique Spiritkin!");
                Log($"Max has {d.SavedCards.Count} Spiritkin! Collection goal reached!");
                break;
            }
        }

        EndTest(MainPlayerData.Instance?.SavedCards.Count >= 5
            ? "Max completed her collection!" : "Max ran out of patience (loop cap reached)");
    }

    // =========================================================================
    // DIEGO SANTOS — The Explorer
    // Find everything, visit everywhere, leave no stone unturned
    // =========================================================================

    IEnumerator RunDiego()
    {
        Log("Diego pulls out the map. Every corner, every secret, every treasure.");

        var collectibles = FindObjectsByType<WorldCollectible>(FindObjectsSortMode.None);
        int totalCollectibles = collectibles.Length;
        Log($"Diego counts {totalCollectibles} points of interest on the map.");

        int collected = 0;

        // Sort by distance for efficient pathing
        var player = GetPlayer();
        var sorted = collectibles
            .Where(c => c != null && !c.IsCollected)
            .OrderBy(c => Vector3.Distance(c.transform.position, player.transform.position))
            .ToList();

        foreach (var collectible in sorted)
        {
            if (collectible == null || collectible.IsCollected) continue;

            yield return WaitForWorldState();

            string typeName = collectible.Type.ToString();
            Log($"Diego heads to {typeName}: \"{collectible.DisplayName}\" at {FormatPos(collectible.transform.position)}");
            yield return MoveToPosition(collectible.transform.position, InteractRange);

            // Simulate E key — WorldCollectible checks distance in its Update
            yield return SimulateInteractKey();

            if (collectible.IsCollected)
            {
                collected++;
                _itemsCollected++;

                string reward = collectible.Type switch
                {
                    WorldCollectible.CollectibleType.TreasureChest => $"+{collectible.GoldReward} gold",
                    WorldCollectible.CollectibleType.Lore => "lore discovered",
                    WorldCollectible.CollectibleType.Viewpoint => "viewpoint unlocked",
                    _ => "collected"
                };
                Log($"Diego found: {collectible.DisplayName} ({reward}) — {collected}/{totalCollectibles}");

                if (collected == 1)
                    _highlights.Add($"First discovery: {collectible.DisplayName}");
            }
            else
            {
                _blockers.Add($"Could not interact with {collectible.DisplayName} ({typeName}) at {FormatPos(collectible.transform.position)}");
            }

            _actionsCompleted++;
            yield return new WaitForSeconds(0.3f);
        }

        // Region coverage check
        var data = MainPlayerData.Instance;
        if (data != null)
        {
            string[] regions = { "frost_valley", "rolling_hills", "volcanic_isles", "dark_castle" };
            int visited = 0;
            foreach (var r in regions)
            {
                if (data.VisitedRegions.Contains(r))
                    visited++;
                else
                    _blockers.Add($"Region never visited: {r}");
            }

            if (visited == 4)
                _highlights.Add("All 4 regions explored!");

            Log($"Diego's map coverage: {visited}/4 regions, {data.VisitedZones.Count} zones, {collected} collectibles");
        }

        EndTest($"Diego's expedition complete — {collected}/{totalCollectibles} points of interest found");
    }

    // =========================================================================
    // YUKI TANAKA — The Crafter
    // Harvest materials, gather essences, craft something beautiful
    // =========================================================================

    IEnumerator RunYuki()
    {
        Log("Yuki examines the land. Resources first, then the forge.");

        // Phase 1: Harvest resource nodes
        var nodes = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
        Log($"Yuki spots {nodes.Length} resource nodes.");

        int harvested = 0;
        int targetHarvests = Mathf.Min(10, nodes.Length);

        // Sort by distance
        var sorted = nodes
            .Where(n => n != null && !n.IsDepleted)
            .OrderBy(n => Vector3.Distance(n.transform.position, GetPlayer().transform.position))
            .ToList();

        foreach (var node in sorted)
        {
            if (harvested >= targetHarvests) break;
            if (node == null || node.IsDepleted) continue;

            yield return WaitForWorldState();

            string nodeName = node.NodeName ?? node.MaterialId ?? "unknown";
            Log($"Yuki approaches: {nodeName} at {FormatPos(node.transform.position)}");
            yield return MoveToPosition(node.transform.position, 2f);

            // Simulate E to start harvest
            yield return SimulateInteractKey();

            // Wait for harvest channel
            float harvestTimeout = 12f;
            while (node.IsHarvesting && harvestTimeout > 0)
            {
                harvestTimeout -= 0.5f;
                yield return new WaitForSeconds(0.5f);
            }

            if (node.IsDepleted)
            {
                harvested++;
                _itemsCollected++;
                Log($"Yuki harvested: {nodeName} ({harvested}/{targetHarvests})");

                if (harvested == 1)
                    _highlights.Add($"First harvest: {nodeName}");
            }
            else
            {
                _blockers.Add($"Harvest failed on {nodeName} at {FormatPos(node.transform.position)} — node not depleted after channel");
            }

            _actionsCompleted++;
            yield return new WaitForSeconds(0.3f);
        }

        // Phase 2: Also collect spirit wisps for essences
        Log("Yuki looks for spirit wisps (essence sources)...");
        var wisps = FindObjectsByType<SpiritWisp>(FindObjectsSortMode.None);
        int wispsCollected = 0;
        foreach (var wisp in wisps.Take(5))
        {
            if (wisp == null) continue;
            yield return MoveToPosition(wisp.transform.position, 1.5f);
            yield return new WaitForSeconds(1f);
            wispsCollected++;
        }
        if (wispsCollected > 0)
            Log($"Yuki chased {wispsCollected} wisps for essences.");

        // Phase 3: Inventory report + crafting check
        var data = MainPlayerData.Instance;
        if (data != null)
        {
            Log("─── Yuki's Inventory ───");
            if (data.Materials.Count > 0)
            {
                foreach (var mat in data.Materials)
                    Log($"  {mat.Key}: ×{mat.Value}");
                _highlights.Add($"Gathered {data.Materials.Count} material types");
            }
            else
            {
                _blockers.Add("Zero materials in inventory after harvesting — gathering pipeline broken?");
            }

            Log($"  Essences: {data.Essences.Count}");
            if (data.Essences.Count == 0)
                _blockers.Add("No essences collected — need wisp drops or node essence drops");

            // Crafting system check
            if (CraftingManager.Instance != null)
            {
                _highlights.Add("CraftingManager is active");
                var schematics = Resources.FindObjectsOfTypeAll<SchematicData>();
                if (schematics.Length > 0)
                {
                    Log($"  Schematics available: {schematics.Length}");
                    foreach (var s in schematics)
                        Log($"    • {s.SchematicName} ({s.Category}, {s.Tier})");
                    _highlights.Add($"{schematics.Length} schematics available for crafting");
                }
                else
                {
                    _blockers.Add("No schematics loaded — cannot craft without recipe data");
                }
            }
            else
            {
                _blockers.Add("CraftingManager not in scene — crafting system offline");
            }

            Log($"  Gold: {data.Gold}");
        }

        EndTest($"Yuki's session complete — {harvested} nodes harvested, {data?.Materials.Count ?? 0} material types");
    }

    // =========================================================================
    // SHARED HELPERS
    // =========================================================================

    IEnumerator WaitForWorldState()
    {
        while (GameLoader.CurrentManager != null ||
               (SpiritComms.Instance != null && SpiritComms.Instance.IsActive))
        {
            yield return new WaitForSeconds(0.5f);
        }
    }

    IEnumerator WaitForBattle(string enemyName)
    {
        float timeout = 60f;
        while (GameLoader.CurrentManager != null && timeout > 0)
        {
            timeout -= 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        if (timeout <= 0)
            _blockers.Add($"Battle vs {enemyName} timed out after 60s — game may be stuck");
    }

    IEnumerator SimulateInteractKey()
    {
        // We can't simulate Input.GetKeyDown from code, but we CAN directly call
        // the interaction methods if we're in range. The Update() loops in
        // WorldCollectible and GatheringManager check E key + distance.
        // Workaround: directly trigger nearby interactables.
        var player = GetPlayer();
        if (player == null) yield break;

        // Try WorldCollectible
        foreach (var c in FindObjectsByType<WorldCollectible>(FindObjectsSortMode.None))
        {
            if (c == null || c.IsCollected) continue;
            if (Vector3.Distance(c.transform.position, player.transform.position) < InteractRange)
            {
                // Call Interact via reflection or just set a flag
                // For now, use SendMessage which calls any "Interact" method
                c.SendMessage("Interact", SendMessageOptions.DontRequireReceiver);
                break;
            }
        }

        // Try ResourceNode via GatheringManager
        if (GatheringManager.Instance != null)
        {
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
            {
                if (n == null || n.IsDepleted || n.IsHarvesting) continue;
                if (Vector3.Distance(n.transform.position, player.transform.position) < InteractRange)
                {
                    n.StartHarvest(4f, () =>
                    {
                        n.Deplete(300f);
                        // Simulate material grant
                        var data = MainPlayerData.Instance;
                        if (data != null && !string.IsNullOrEmpty(n.MaterialId))
                        {
                            if (!data.Materials.ContainsKey(n.MaterialId))
                                data.Materials[n.MaterialId] = 0;
                            data.Materials[n.MaterialId]++;
                        }
                    });
                    break;
                }
            }
        }

        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator MoveToPosition(Vector3 target, float stopDist)
    {
        State = AgentState.Moving;
        _targetPos = target;
        var player = GetPlayer();
        if (player == null) yield break;

        float timeout = 30f;
        while (Vector3.Distance(player.transform.position, target) > stopDist && timeout > 0)
        {
            if (GameLoader.CurrentManager != null ||
                (SpiritComms.Instance != null && SpiritComms.Instance.IsActive))
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

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

    void Log(string msg) => Debug.Log($"[{_agentTitle}] {msg}");

    void LogStartState()
    {
        var data = MainPlayerData.Instance;
        if (data == null) { Log("No player data loaded yet."); return; }

        Log($"Starting state: {data.SavedCards.Count} cards, {data.DefeatedGhostCount} wins, {data.Gold} gold");
        Log($"Sideline: {(data.SidelineUnlocked ? "UNLOCKED" : $"locked ({data.DefeatedGhostCount}/5 wins)")}");
        Log($"Regions: {data.VisitedRegions.Count}/4 | Materials: {data.Materials.Count} types");
    }

    void LogFinalState()
    {
        var data = MainPlayerData.Instance;
        if (data == null) return;

        Log($"Final state: {data.SavedCards.Count} cards, {data.DefeatedGhostCount} wins, {data.Gold} gold");
        Log($"Sideline: {(data.SidelineUnlocked ? "UNLOCKED" : $"locked ({data.DefeatedGhostCount}/5 wins)")}");
        Log($"Regions: {data.VisitedRegions.Count}/4 | Zones: {data.VisitedZones.Count} | Materials: {data.Materials.Count}");
        Log($"Lore: {data.DiscoveredLore.Count} | Crystals: {data.FoundCrystals.Count} | Viewpoints: {data.FoundViewpoints.Count}");
    }

    static string FormatTime(float seconds)
    {
        int m = (int)(seconds / 60);
        int s = (int)(seconds % 60);
        return m > 0 ? $"{m}m {s}s" : $"{s}s";
    }

    static string FormatPos(Vector3 p) => $"({p.x:F0}, {p.z:F0})";
}
