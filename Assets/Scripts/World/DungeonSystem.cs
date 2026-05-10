using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SWG-style theme park dungeon system. Reusable framework for puzzle rooms,
/// guardian battles, and loot rewards. Content (level design) added separately.
///
/// Flow: Enter dungeon → intro dialogue → puzzle rooms → guardian battle → loot → exit
/// </summary>
public class DungeonSystem : MonoBehaviour
{
    public static DungeonSystem Instance { get; private set; }

    // =========================================================================
    // DUNGEON DEFINITIONS (data only — level design is separate)
    // =========================================================================

    public static readonly List<DungeonDefinition> Dungeons = new()
    {
        new DungeonDefinition
        {
            Id = "frost_cavern",
            Name = "Frost Cavern",
            Region = "frost_valley",
            IntroDialogue = "Ancient ice walls glimmer with a faint blue light. The cavern hums with energy.",
            Rooms = new[]
            {
                new DungeonRoom { Type = PuzzleType.SequenceChest, SequenceLength = 3,
                    Description = "Three frozen chests sit in a circle. A pattern of lights flashes across them." },
                new DungeonRoom { Type = PuzzleType.Battle, GuardianCardIds = new[] { 5 }, // Puff
                    Description = "A guardian spirit blocks the way forward!" }
            },
            Rewards = new DungeonRewards { Gold = 50, MaterialId = "healing_seed", MaterialAmount = 3 }
        },
        new DungeonDefinition
        {
            Id = "lava_forge_trial",
            Name = "Lava Forge Trial",
            Region = "volcanic_isles",
            IntroDialogue = "Heat washes over you as you enter the forge. The walls glow orange with molten rock.",
            Rooms = new[]
            {
                new DungeonRoom { Type = PuzzleType.SurvivalWaves, WaveCount = 3,
                    Description = "Waves of wild Spiritkin emerge from the lava. Survive all three!" },
            },
            Rewards = new DungeonRewards { Gold = 80, MaterialId = "fire_essence", MaterialAmount = 2 }
        },
        new DungeonDefinition
        {
            Id = "shadow_vault",
            Name = "Shadow Vault",
            Region = "dark_castle",
            IntroDialogue = "The vault door groans open. Darkness swallows your torchlight within three steps.",
            Rooms = new[]
            {
                new DungeonRoom { Type = PuzzleType.RiddleGate,
                    RiddleText = "I have cities, but no houses. I have mountains, but no trees. I have water, but no fish. What am I?",
                    RiddleAnswers = new[] { "A map", "A painting", "A dream" },
                    CorrectAnswer = 0,
                    Description = "An ancient gate blocks the path. Glowing runes spell out a riddle." },
                new DungeonRoom { Type = PuzzleType.Battle, GuardianCardIds = new[] { 202 }, // Dark Fang
                    Description = "The vault guardian emerges from the shadows!" }
            },
            Rewards = new DungeonRewards { Gold = 100, MaterialId = "shadow_crystal", MaterialAmount = 1, CardChanceId = 108 }
        }
    };

    static Dictionary<string, DungeonDefinition> _lookup;
    public static DungeonDefinition? GetDungeon(string id)
    {
        if (_lookup == null)
        {
            _lookup = new Dictionary<string, DungeonDefinition>();
            foreach (var d in Dungeons) _lookup[d.Id] = d;
        }
        return _lookup.TryGetValue(id, out var def) ? def : null;
    }

    // =========================================================================
    // STATE
    // =========================================================================

    DungeonDefinition _activeDungeon;
    int _currentRoom;
    bool _isRunning;
    Vector3 _returnPosition;
    Coroutine _activeRoomCoroutine;

    public bool IsInDungeon => _isRunning;

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>Enter a dungeon by ID. Checks cooldown and prerequisites.</summary>
    public bool EnterDungeon(string dungeonId)
    {
        if (_isRunning) return false;

        var def = GetDungeon(dungeonId);
        if (def == null)
        {
            Debug.LogWarning($"[Dungeon] Unknown dungeon: {dungeonId}");
            return false;
        }

        // Check daily cooldown
        var data = MainPlayerData.Instance;
        string todayKey = $"dungeon_{dungeonId}_{DateTime.UtcNow:yyyyMMdd}";
        if (data.CompletedQuests.Contains(todayKey))
        {
            OverworldIntegration.Instance?.ShowNotification("You've already cleared this dungeon today. Come back tomorrow.");
            return false;
        }

        _activeDungeon = def.Value;
        _currentRoom = 0;
        _isRunning = true;

        // Save return position
        var player = WorldManager.Instance?.WorldPlayer;
        if (player != null) _returnPosition = player.transform.position;

        Debug.Log($"[Dungeon] Entering: {_activeDungeon.Name}");

        // Show intro dialogue, then start first room
        if (SpiritComms.Instance != null)
        {
            var lines = new List<(string, string, Sprite, Color)>
            {
                (_activeDungeon.Name.ToUpper(), _activeDungeon.IntroDialogue, null, new Color(0.6f, 0.4f, 0.9f))
            };
            SpiritComms.Instance.ShowCommSequence(lines, () => _activeRoomCoroutine = StartCoroutine(RunRoom()));
        }
        else
        {
            _activeRoomCoroutine = StartCoroutine(RunRoom());
        }

        return true;
    }

    /// <summary>Flee from dungeon — return to entrance with no rewards.</summary>
    public void FleeDungeon()
    {
        if (!_isRunning) return;
        if (_activeRoomCoroutine != null) { StopCoroutine(_activeRoomCoroutine); _activeRoomCoroutine = null; }
        Debug.Log($"[Dungeon] Player fled from {_activeDungeon.Name}");
        EndDungeon(false);
    }

    // =========================================================================
    // ROOM EXECUTION
    // =========================================================================

    IEnumerator RunRoom()
    {
        if (_currentRoom >= _activeDungeon.Rooms.Length)
        {
            // All rooms complete — victory!
            EndDungeon(true);
            yield break;
        }

        var room = _activeDungeon.Rooms[_currentRoom];

        // Show room description
        if (SpiritComms.Instance != null)
        {
            var lines = new List<(string, string, Sprite, Color)>
            {
                ("DUNGEON", room.Description, null, new Color(0.8f, 0.7f, 0.4f))
            };
            bool dialogueDone = false;
            SpiritComms.Instance.ShowCommSequence(lines, () => dialogueDone = true);
            while (!dialogueDone) yield return null;
        }

        yield return new WaitForSeconds(0.5f);

        // Execute puzzle
        bool passed = false;
        switch (room.Type)
        {
            case PuzzleType.SequenceChest:
                yield return RunSequenceChest(room, result => passed = result);
                break;
            case PuzzleType.SurvivalWaves:
                yield return RunSurvivalWaves(room, result => passed = result);
                break;
            case PuzzleType.RiddleGate:
                yield return RunRiddleGate(room, result => passed = result);
                break;
            case PuzzleType.Battle:
                yield return RunGuardianBattle(room, result => passed = result);
                break;
        }

        if (passed)
        {
            Debug.Log($"[Dungeon] Room {_currentRoom + 1} passed!");
            _currentRoom++;
            yield return new WaitForSeconds(0.5f);
            _activeRoomCoroutine = StartCoroutine(RunRoom()); // Next room
        }
        else
        {
            Debug.Log($"[Dungeon] Room {_currentRoom + 1} failed!");
            if (SpiritComms.Instance != null)
            {
                var lines = new List<(string, string, Sprite, Color)>
                {
                    ("DUNGEON", "You have been defeated. The dungeon expels you.", null, new Color(0.9f, 0.3f, 0.3f))
                };
                SpiritComms.Instance.ShowCommSequence(lines, null);
            }
            yield return new WaitForSeconds(2f);
            EndDungeon(false);
        }
    }

    // =========================================================================
    // PUZZLE: SEQUENCE CHEST
    // =========================================================================

    IEnumerator RunSequenceChest(DungeonRoom room, Action<bool> onResult)
    {
        int length = room.SequenceLength > 0 ? room.SequenceLength : 3;

        // Generate random sequence
        int[] sequence = new int[length];
        for (int i = 0; i < length; i++)
            sequence[i] = UnityEngine.Random.Range(0, length);

        // Show sequence to player via dialogue
        string seqDisplay = "";
        for (int i = 0; i < length; i++)
            seqDisplay += $"Chest {sequence[i] + 1} → ";
        seqDisplay = seqDisplay.TrimEnd(' ', '→');

        if (SpiritComms.Instance != null)
        {
            var lines = new List<(string, string, Sprite, Color)>
            {
                ("PUZZLE", $"Watch the sequence carefully...\n{seqDisplay}", null, new Color(0.4f, 0.8f, 1f)),
                ("PUZZLE", "Now repeat the sequence! (Click the chests in order)", null, new Color(0.4f, 0.8f, 1f))
            };
            bool shown = false;
            SpiritComms.Instance.ShowCommSequence(lines, () => shown = true);
            while (!shown) yield return null;
        }

        // For now, auto-pass with a simulated delay (UI interaction requires scene objects)
        // TODO: When dungeon areas are designed, spawn clickable chest objects
        yield return new WaitForSeconds(1.5f);

        // Simulate: 70% pass rate for now (player skill will determine this when UI is built)
        bool passed = UnityEngine.Random.value < 0.7f;

        if (passed)
        {
            OverworldIntegration.Instance?.ShowNotification("Sequence correct! The path opens.");
        }
        else
        {
            OverworldIntegration.Instance?.ShowNotification("Wrong sequence! The chests reset.");
        }

        onResult(passed);
    }

    // =========================================================================
    // PUZZLE: SURVIVAL WAVES
    // =========================================================================

    IEnumerator RunSurvivalWaves(DungeonRoom room, Action<bool> onResult)
    {
        int waves = room.WaveCount > 0 ? room.WaveCount : 3;

        for (int w = 0; w < waves; w++)
        {
            if (SpiritComms.Instance != null)
            {
                var lines = new List<(string, string, Sprite, Color)>
                {
                    ("WAVE", $"Wave {w + 1} of {waves} — prepare yourself!", null, new Color(0.9f, 0.4f, 0.3f))
                };
                bool shown = false;
                SpiritComms.Instance.ShowCommSequence(lines, () => shown = true);
                while (!shown) yield return null;
            }

            // Pick a random common card for the wave enemy
            Card waveEnemy = null;
            foreach (var pair in AssetManager.Cards)
            {
                if (pair.Value.Rarity == Rarity.Common)
                {
                    waveEnemy = pair.Value;
                    break;
                }
            }

            if (waveEnemy != null)
            {
                GameManager.OverrideEnemyLineup = new List<Card> { waveEnemy };
                GameManager.IsTrainerBattle = false;

                // Find a template encounter
                Encounter templateEnc = null;
                foreach (var pair in AssetManager.IdItems)
                {
                    if (pair.Value is Encounter e) { templateEnc = e; break; }
                }

                if (templateEnc != null)
                {
                    string waveLocId = $"dungeon_wave_{_activeDungeon.Id}_{w}";

                    // Ensure WorldLocationSaved exists or battle will abort
                    var worldSaved = MainPlayerData.Instance.WorldSaved;
                    if (worldSaved.GetLocationSaved(waveLocId) == null)
                        worldSaved.SavedLocations.Add(new WorldLocationSaved { LocationId = waveLocId });

                    int ghostCountBefore = MainPlayerData.Instance?.DefeatedGhostCount ?? 0;

                    var info = new GameInfo
                    {
                        LocationId = waveLocId,
                        EncounterId = templateEnc.AssetId,
                    };
                    GameLoader.StartGame(info);

                    // Wait for battle to end
                    while (GameLoader.CurrentManager != null)
                        yield return null;

                    // Check if player won this wave
                    int ghostCountAfter = MainPlayerData.Instance?.DefeatedGhostCount ?? 0;
                    if (ghostCountAfter <= ghostCountBefore)
                    {
                        // Player lost this wave — dungeon fails
                        onResult(false);
                        yield break;
                    }
                    // If player lost, they'll be back in overworld with less HP
                    yield return new WaitForSeconds(0.5f);
                }
            }
            else
            {
                // No cards found — skip wave
                yield return new WaitForSeconds(1f);
            }

            // Heal half HP between waves (if not last wave)
            if (w < waves - 1)
            {
                var data = MainPlayerData.Instance;
                if (data != null)
                {
                    data.WorldSaved.AddBattleHealth(1); // Partial heal
                    OverworldIntegration.Instance?.ShowNotification("A brief rest between waves. +1 HP restored.");
                }
                yield return new WaitForSeconds(1f);
            }
        }

        OverworldIntegration.Instance?.ShowNotification($"All {waves} waves survived!");
        onResult(true);
    }

    // =========================================================================
    // PUZZLE: RIDDLE GATE
    // =========================================================================

    IEnumerator RunRiddleGate(DungeonRoom room, Action<bool> onResult)
    {
        if (string.IsNullOrEmpty(room.RiddleText) || room.RiddleAnswers == null)
        {
            onResult(true);
            yield break;
        }

        // Show riddle via SpiritComms
        if (SpiritComms.Instance != null)
        {
            string answersText = "";
            for (int i = 0; i < room.RiddleAnswers.Length; i++)
                answersText += $"\n{i + 1}. {room.RiddleAnswers[i]}";

            var lines = new List<(string, string, Sprite, Color)>
            {
                ("RIDDLE", room.RiddleText + answersText, null, new Color(0.6f, 0.8f, 1f)),
                ("RIDDLE", "Press 1, 2, or 3 to answer.", null, new Color(0.5f, 0.5f, 0.6f))
            };
            bool shown = false;
            SpiritComms.Instance.ShowCommSequence(lines, () => shown = true);
            while (!shown) yield return null;
        }

        // Wait for player input (1, 2, or 3 key)
        int answer = -1;
        float timeout = 30f;
        while (answer < 0 && timeout > 0)
        {
            timeout -= Time.deltaTime;
            if (Input.GetKeyDown(KeyCode.Alpha1)) answer = 0;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) answer = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) answer = 2;
            yield return null;
        }

        if (answer == room.CorrectAnswer)
        {
            OverworldIntegration.Instance?.ShowNotification("Correct! The gate rumbles open.");
            onResult(true);
        }
        else
        {
            string msg = timeout <= 0 ? "Time's up! The gate remains sealed." : "Wrong answer! The gate shudders and remains sealed.";
            OverworldIntegration.Instance?.ShowNotification(msg);
            onResult(false);
        }
    }

    // =========================================================================
    // PUZZLE: GUARDIAN BATTLE
    // =========================================================================

    IEnumerator RunGuardianBattle(DungeonRoom room, Action<bool> onResult)
    {
        if (room.GuardianCardIds == null || room.GuardianCardIds.Length == 0)
        {
            onResult(true);
            yield break;
        }

        // Build guardian lineup
        var lineup = new List<Card>();
        foreach (int id in room.GuardianCardIds)
        {
            var entry = AllCardsData.FindById(id);
            if (entry.HasValue)
            {
                foreach (var pair in AssetManager.Cards)
                {
                    if (pair.Value.CardName.Equals(entry.Value.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        lineup.Add(pair.Value);
                        break;
                    }
                }
            }
        }

        if (lineup.Count == 0)
        {
            Debug.LogWarning("[Dungeon] No guardian cards found!");
            onResult(true);
            yield break;
        }

        GameManager.OverrideEnemyLineup = lineup;
        GameManager.IsTrainerBattle = false;

        // Find template encounter
        Encounter templateEnc = null;
        foreach (var pair in AssetManager.IdItems)
        {
            if (pair.Value is Encounter e) { templateEnc = e; break; }
        }

        if (templateEnc == null)
        {
            Debug.LogWarning("[Dungeon] No encounter template found!");
            onResult(true);
            yield break;
        }

        int ghostCountBefore = MainPlayerData.Instance?.DefeatedGhostCount ?? 0;

        string guardianLocId = $"dungeon_guardian_{_activeDungeon.Id}";

        // Ensure WorldLocationSaved exists or battle will abort
        var worldSaved = MainPlayerData.Instance.WorldSaved;
        if (worldSaved.GetLocationSaved(guardianLocId) == null)
            worldSaved.SavedLocations.Add(new WorldLocationSaved { LocationId = guardianLocId });

        var info = new GameInfo
        {
            LocationId = guardianLocId,
            EncounterId = templateEnc.AssetId,
        };
        GameLoader.StartGame(info);

        // Wait for battle to end
        while (GameLoader.CurrentManager != null)
            yield return null;

        yield return new WaitForSeconds(0.5f);

        // Check if player won (ghost count increased)
        int ghostCountAfter = MainPlayerData.Instance?.DefeatedGhostCount ?? 0;
        bool won = ghostCountAfter > ghostCountBefore;

        onResult(won);
    }

    // =========================================================================
    // DUNGEON END
    // =========================================================================

    void EndDungeon(bool victory)
    {
        _isRunning = false;

        if (victory)
        {
            // Grant rewards
            var data = MainPlayerData.Instance;
            var rewards = _activeDungeon.Rewards;

            data.AddGold(rewards.Gold);
            OverworldIntegration.Instance?.ShowNotification($"Dungeon cleared! +{rewards.Gold} gold");

            if (!string.IsNullOrEmpty(rewards.MaterialId) && rewards.MaterialAmount > 0)
            {
                if (!data.Materials.ContainsKey(rewards.MaterialId))
                    data.Materials[rewards.MaterialId] = 0;
                data.Materials[rewards.MaterialId] += rewards.MaterialAmount;
            }

            // Card drop chance
            if (rewards.CardChanceId > 0 && UnityEngine.Random.value < 0.25f)
            {
                var cardEntry = AllCardsData.FindById(rewards.CardChanceId);
                if (cardEntry.HasValue)
                    OverworldIntegration.Instance?.ShowNotification($"Rare drop: {cardEntry.Value.Name}!");
            }

            // Mark as completed today
            string todayKey = $"dungeon_{_activeDungeon.Id}_{DateTime.UtcNow:yyyyMMdd}";
            if (!data.CompletedQuests.Contains(todayKey))
                data.CompletedQuests.Add(todayKey);

            // Quest progress
            QuestManager.Instance?.ReportDungeonCompleted(_activeDungeon.Id);

            // XP reward
            ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Exploration, 40);
            ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Combat, 20);

            MainPlayerData.SaveToCloud();

            Debug.Log($"[Dungeon] {_activeDungeon.Name} CLEARED! Rewards granted.");
        }
        else
        {
            Debug.Log($"[Dungeon] {_activeDungeon.Name} failed.");
        }

        // Return player to entrance position
        var player = WorldManager.Instance?.WorldPlayer;
        if (player != null)
            player.transform.position = _returnPosition;
    }
}

// =========================================================================
// DATA STRUCTURES
// =========================================================================

[Serializable]
public struct DungeonDefinition
{
    public string Id;
    public string Name;
    public string Region;
    public string IntroDialogue;
    public DungeonRoom[] Rooms;
    public DungeonRewards Rewards;
}

[Serializable]
public struct DungeonRoom
{
    public PuzzleType Type;
    public string Description;

    // SequenceChest
    public int SequenceLength;

    // SurvivalWaves
    public int WaveCount;

    // RiddleGate
    public string RiddleText;
    public string[] RiddleAnswers;
    public int CorrectAnswer;

    // Battle
    public int[] GuardianCardIds;
}

[Serializable]
public struct DungeonRewards
{
    public int Gold;
    public string MaterialId;
    public int MaterialAmount;
    public int CardChanceId; // 25% chance to drop this card ID
}

public enum PuzzleType
{
    SequenceChest,
    SurvivalWaves,
    RiddleGate,
    Battle
}
