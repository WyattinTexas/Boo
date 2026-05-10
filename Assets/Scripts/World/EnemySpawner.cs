using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns and manages roaming wild Spiritkin enemies in the world.
/// Enemies patrol, detect the player, chase, and trigger battles on contact.
/// Port of gathering.js roaming enemy system.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    public static EnemySpawner Instance { get; private set; }

    [Header("Config")]
    public int MaxEnemies = 25;
    public float SpawnCheckInterval = 5f;
    public int SpawnPerCheck = 2;
    public float MinSpawnDistFromPlayer = 15f;
    public float MaxSpawnDistFromPlayer = 50f;
    public float MinSpawnDistFromHub = 8f;
    public float DespawnDistance = 80f;

    [Header("Prefab")]
    public GameObject EnemyPrefab;

    [Header("Hub Positions (enemies don't spawn near these)")]
    public List<Vector3> HubPositions = new()
    {
        new(0, 0, 0),       // Frost Valley hub (default — update from scene)
    };

    readonly List<RoamingEnemy> _activeEnemies = new();
    public IReadOnlyList<RoamingEnemy> ActiveEnemies => _activeEnemies;

    float _spawnTimer;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (WorldManager.Instance?.WorldPlayer == null) return;

        _spawnTimer += Time.deltaTime;
        if (_spawnTimer >= SpawnCheckInterval)
        {
            _spawnTimer = 0;
            SpawnCheck();
            CleanupDistant();
        }

        // Update all enemies
        var playerPos = WorldManager.Instance.WorldPlayer.transform.position;
        for (int i = _activeEnemies.Count - 1; i >= 0; i--)
        {
            var enemy = _activeEnemies[i];
            if (enemy == null)
            {
                _activeEnemies.RemoveAt(i);
                continue;
            }
            enemy.UpdateAI(playerPos);
        }
    }

    // =========================================================================
    // SPAWNING
    // =========================================================================

    void SpawnCheck()
    {
        if (_activeEnemies.Count >= MaxEnemies) return;

        var playerPos = WorldManager.Instance.WorldPlayer.transform.position;
        int toSpawn = Mathf.Min(SpawnPerCheck, MaxEnemies - _activeEnemies.Count);

        for (int i = 0; i < toSpawn; i++)
        {
            Vector3? spawnPos = FindSpawnPosition(playerPos);
            if (spawnPos.HasValue)
                SpawnEnemy(spawnPos.Value);
        }
    }

    Vector3? FindSpawnPosition(Vector3 playerPos)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            // Random position around player
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(MinSpawnDistFromPlayer, MaxSpawnDistFromPlayer);
            Vector3 candidate = playerPos + new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);

            // Check not too close to hubs
            bool nearHub = false;
            foreach (var hub in HubPositions)
            {
                if (Vector3.Distance(candidate, hub) < MinSpawnDistFromHub)
                {
                    nearHub = true;
                    break;
                }
            }
            if (nearHub) continue;

            // Snap to terrain
            if (Physics.Raycast(candidate + Vector3.up * 100, Vector3.down, out var hit, 200f))
            {
                candidate.y = hit.point.y;
                return candidate;
            }
        }
        return null;
    }

    void SpawnEnemy(Vector3 position)
    {
        // Pick a random card weighted by rarity
        var card = PickRandomCard();
        if (card == null) return;

        // Determine if elite (far from all hubs)
        bool isElite = true;
        foreach (var hub in HubPositions)
        {
            if (Vector3.Distance(position, hub) < 30f)
            {
                isElite = false;
                break;
            }
        }

        GameObject go;
        if (EnemyPrefab != null)
        {
            go = Instantiate(EnemyPrefab, position, Quaternion.identity, transform);
        }
        else
        {
            // Build a multi-primitive humanoid enemy figure
            go = EnemyModelBuilder.Build(card.Value.Rarity, isElite);
            go.transform.position = position;
            go.transform.SetParent(transform);
            EnemyModelBuilder.SetNameLabel(go, card.Value.Name);
        }

        var enemy = go.GetComponent<RoamingEnemy>();
        if (enemy == null) enemy = go.AddComponent<RoamingEnemy>();

        enemy.Initialize(card.Value, isElite);
        _activeEnemies.Add(enemy);
    }

    void CleanupDistant()
    {
        var playerPos = WorldManager.Instance.WorldPlayer.transform.position;
        for (int i = _activeEnemies.Count - 1; i >= 0; i--)
        {
            var enemy = _activeEnemies[i];
            if (enemy == null || Vector3.Distance(enemy.transform.position, playerPos) > DespawnDistance)
            {
                if (enemy != null) Destroy(enemy.gameObject);
                _activeEnemies.RemoveAt(i);
            }
        }
    }

    /// <summary>Remove a specific enemy (called after battle).</summary>
    public void RemoveEnemy(RoamingEnemy enemy)
    {
        _activeEnemies.Remove(enemy);
        if (enemy != null) Destroy(enemy.gameObject);
    }

    // =========================================================================
    // CARD SELECTION
    // =========================================================================

    AllCardsData.CardEntry? PickRandomCard()
    {
        // Get region-filtered cards (includes Set 1 universals)
        string region = RegionManager.Instance != null
            ? RegionManager.Instance.CurrentRegion
            : RegionManager.FrostValley;

        var regionCards = RegionManager.GetRegionCards(region);

        // Weight by rarity: common 50, uncommon 25, rare 10, ghost-rare 3
        var pool = new List<(AllCardsData.CardEntry card, int weight)>();
        foreach (var card in regionCards)
        {
            int weight = card.Rarity switch
            {
                "common" => 50,
                "uncommon" => 25,
                "rare" => 10,
                "ghost-rare" => 3,
                _ => 0 // No legendaries in wild
            };
            if (weight > 0) pool.Add((card, weight));
        }

        if (pool.Count == 0) return null;

        int totalWeight = 0;
        foreach (var (_, w) in pool) totalWeight += w;

        int roll = Random.Range(0, totalWeight);
        int cumulative = 0;
        foreach (var (card, weight) in pool)
        {
            cumulative += weight;
            if (roll < cumulative) return card;
        }

        return pool[0].card;
    }
}
