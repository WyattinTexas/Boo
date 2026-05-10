using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Quest system: daily NPC quests (9 NPCs x 3 rotating options), Mask of Destiny,
/// weekly challenges, lore, and crystal collectibles.
/// </summary>
public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance { get; private set; }

    public static event Action<string> OnQuestProgress;
    public static event Action<string, int> OnQuestComplete;
    public static event Action<string> OnLoreDiscovered;
    public static event Action<string> OnCrystalFound;

    [Header("Quest Definitions")]
    public List<QuestDefinition> AllQuests = new();

    [Header("Weekly Challenge Definitions")]
    public List<WeeklyChallenge> WeeklyChallenges = new();

    // ═══════ DAILY QUEST POOL ═══════

    /// <summary>Full pool of daily quests. Each NPC has 3 options, rotated by day seed.</summary>
    static readonly Dictionary<string, List<DailyQuestDef>> DAILY_QUEST_POOL = new()
    {
        ["elder_frost"] = new()
        {
            new DailyQuestDef
            {
                QuestId = "elder_frost_premium_essence", Title = "Premium Essence",
                Description = "Collect an essence with potency greater than 700.",
                Type = DailyQuestType.CollectEssencePotency, Target = 700,
                Reward = new DailyQuestReward { Gold = 80, XP = 40 }
            },
            new DailyQuestDef
            {
                QuestId = "elder_frost_frost_patrol", Title = "Frost Patrol",
                Description = "Win 3 battles in Frost Valley.",
                Type = DailyQuestType.WinBattle, Target = 3, Region = "frost_valley",
                Reward = new DailyQuestReward { Gold = 60, XP = 30 }
            },
            new DailyQuestDef
            {
                QuestId = "elder_frost_crystal_hunt", Title = "Crystal Hunt",
                Description = "Collect 2 frozen crystals.",
                Type = DailyQuestType.CollectCrystal, Target = 2,
                Reward = new DailyQuestReward { Gold = 100, XP = 50 }
            }
        },

        ["smith_ember"] = new()
        {
            new DailyQuestDef
            {
                QuestId = "smith_ember_superior_work", Title = "Superior Work",
                Description = "Craft a Superior or better item.",
                Type = DailyQuestType.CraftSuperior, Target = 1,
                Reward = new DailyQuestReward { Gold = 100, XP = 50 }
            },
            new DailyQuestDef
            {
                QuestId = "smith_ember_iron_supply", Title = "Iron Supply",
                Description = "Collect 3 iron ore.",
                Type = DailyQuestType.CollectMaterial, Target = 3, MaterialId = "iron_ore",
                Reward = new DailyQuestReward { Gold = 50, XP = 25 }
            },
            new DailyQuestDef
            {
                QuestId = "smith_ember_forge_master", Title = "Forge Master",
                Description = "Craft 2 items of any quality.",
                Type = DailyQuestType.CraftAny, Target = 2,
                Reward = new DailyQuestReward { Gold = 60, XP = 30 }
            }
        },

        ["keeper_zara"] = new()
        {
            new DailyQuestDef
            {
                QuestId = "keeper_zara_spirit_patrol", Title = "Spirit Patrol",
                Description = "Win 5 battles anywhere.",
                Type = DailyQuestType.WinBattle, Target = 5,
                Reward = new DailyQuestReward { Gold = 75, XP = 40 }
            },
            new DailyQuestDef
            {
                QuestId = "keeper_zara_rare_hunt", Title = "Rare Hunt",
                Description = "Defeat a rare or higher enemy.",
                Type = DailyQuestType.DefeatRare, Target = 1,
                Reward = new DailyQuestReward { Gold = 90, XP = 45 }
            },
            new DailyQuestDef
            {
                QuestId = "keeper_zara_team_builder", Title = "Team Builder",
                Description = "Recruit 1 new Spiritkin to your roster.",
                Type = DailyQuestType.RecruitSpirit, Target = 1,
                Reward = new DailyQuestReward { Gold = 80, XP = 35 }
            }
        },

        ["farmer_bea"] = new()
        {
            new DailyQuestDef
            {
                QuestId = "farmer_bea_harvest_time", Title = "Harvest Time",
                Description = "Collect 5 materials of any type.",
                Type = DailyQuestType.CollectMaterial, Target = 5,
                Reward = new DailyQuestReward { Gold = 40, XP = 20 }
            },
            new DailyQuestDef
            {
                QuestId = "farmer_bea_seed_finder", Title = "Seed Finder",
                Description = "Collect 3 healing seeds.",
                Type = DailyQuestType.CollectMaterial, Target = 3, MaterialId = "healing_seed",
                Reward = new DailyQuestReward { Gold = 50, XP = 25 }
            },
            new DailyQuestDef
            {
                QuestId = "farmer_bea_green_thumb", Title = "Green Thumb",
                Description = "Feed 2 friendly spirits in the wild.",
                Type = DailyQuestType.FeedSpirit, Target = 2,
                Reward = new DailyQuestReward { Gold = 45, XP = 20 }
            }
        },

        ["herbalist_sage"] = new()
        {
            new DailyQuestDef
            {
                QuestId = "herbalist_sage_essence_harvest", Title = "Essence Harvest",
                Description = "Collect 3 essences of any type.",
                Type = DailyQuestType.CollectEssence, Target = 3,
                Reward = new DailyQuestReward { Gold = 55, XP = 25 }
            },
            new DailyQuestDef
            {
                QuestId = "herbalist_sage_quality_check", Title = "Quality Check",
                Description = "Collect an essence with 500+ total stats.",
                Type = DailyQuestType.CollectEssenceTotalStats, Target = 500,
                Reward = new DailyQuestReward { Gold = 70, XP = 35 }
            },
            new DailyQuestDef
            {
                QuestId = "herbalist_sage_nature_walk", Title = "Nature Walk",
                Description = "Walk 200 units through the world.",
                Type = DailyQuestType.WalkDistance, Target = 200,
                Reward = new DailyQuestReward { Gold = 35, XP = 15 }
            }
        },

        ["captain_rex"] = new()
        {
            new DailyQuestDef
            {
                QuestId = "captain_rex_explorer", Title = "Explorer",
                Description = "Visit 3 different zones.",
                Type = DailyQuestType.VisitZones, Target = 3,
                Reward = new DailyQuestReward { Gold = 60, XP = 30 }
            },
            new DailyQuestDef
            {
                QuestId = "captain_rex_brave_heart", Title = "Brave Heart",
                Description = "Win a battle without taking any damage.",
                Type = DailyQuestType.WinNoDamage, Target = 1,
                Reward = new DailyQuestReward { Gold = 100, XP = 50 }
            },
            new DailyQuestDef
            {
                QuestId = "captain_rex_treasure_seeker", Title = "Treasure Seeker",
                Description = "Open 1 treasure chest.",
                Type = DailyQuestType.OpenChest, Target = 1,
                Reward = new DailyQuestReward { Gold = 50, XP = 25 }
            }
        },

        ["forge_master_kira"] = new()
        {
            new DailyQuestDef
            {
                QuestId = "forge_master_kira_flame_forge", Title = "Flame Forge",
                Description = "Craft an item using a fire essence.",
                Type = DailyQuestType.CraftWithFireEssence, Target = 1,
                Reward = new DailyQuestReward { Gold = 80, XP = 40 }
            },
            new DailyQuestDef
            {
                QuestId = "forge_master_kira_obsidian_hunter", Title = "Obsidian Hunter",
                Description = "Collect 2 volcanic glass.",
                Type = DailyQuestType.CollectMaterial, Target = 2, MaterialId = "volcanic_glass",
                Reward = new DailyQuestReward { Gold = 70, XP = 35 }
            },
            new DailyQuestDef
            {
                QuestId = "forge_master_kira_heat_wave", Title = "Heat Wave",
                Description = "Win 3 battles in the Volcanic Isles.",
                Type = DailyQuestType.WinBattle, Target = 3, Region = "volcanic_isles",
                Reward = new DailyQuestReward { Gold = 65, XP = 35 }
            }
        },

        ["merchant_dax"] = new()
        {
            new DailyQuestDef
            {
                QuestId = "merchant_dax_market_day", Title = "Market Day",
                Description = "List 1 item on the market.",
                Type = DailyQuestType.ListOnMarket, Target = 1,
                Reward = new DailyQuestReward { Gold = 40, XP = 20 }
            },
            new DailyQuestDef
            {
                QuestId = "merchant_dax_gold_rush", Title = "Gold Rush",
                Description = "Earn 50 gold from any source.",
                Type = DailyQuestType.EarnGold, Target = 50,
                Reward = new DailyQuestReward { Gold = 30, XP = 15 }
            },
            new DailyQuestDef
            {
                QuestId = "merchant_dax_trade_route", Title = "Trade Route",
                Description = "Visit 2 different regions.",
                Type = DailyQuestType.VisitRegions, Target = 2,
                Reward = new DailyQuestReward { Gold = 50, XP = 25 }
            }
        },

        ["shadow_warden_vex"] = new()
        {
            new DailyQuestDef
            {
                QuestId = "shadow_warden_vex_dark_patrol", Title = "Dark Patrol",
                Description = "Win 3 battles in the Dark Castle.",
                Type = DailyQuestType.WinBattle, Target = 3, Region = "dark_castle",
                Reward = new DailyQuestReward { Gold = 90, XP = 50 }
            },
            new DailyQuestDef
            {
                QuestId = "shadow_warden_vex_shadow_strike", Title = "Shadow Strike",
                Description = "Defeat an elite enemy.",
                Type = DailyQuestType.DefeatElite, Target = 1,
                Reward = new DailyQuestReward { Gold = 120, XP = 60, Title = "Shadow Striker" }
            },
            new DailyQuestDef
            {
                QuestId = "shadow_warden_vex_night_walker", Title = "Night Walker",
                Description = "Play during the night phase (8PM-6AM).",
                Type = DailyQuestType.PlayDuringNight, Target = 1,
                Reward = new DailyQuestReward { Gold = 50, XP = 25, Title = "Night Walker" }
            }
        }
    };

    // ═══════ ACTIVE DAILY QUESTS (runtime state) ═══════

    /// <summary>Active daily quests keyed by npcId. Regenerated each day.</summary>
    Dictionary<string, DailyQuestState> _activeDailyQuests = new();
    string _lastDaySeed = "";

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        RefreshDailyQuests();
    }

    void Update()
    {
        // Check if day rolled over
        string today = GetTodayId();
        if (today != _lastDaySeed)
            RefreshDailyQuests();
    }

    // =========================================================================
    // DAILY QUEST SYSTEM
    // =========================================================================

    /// <summary>Regenerate daily quests based on today's date seed.</summary>
    void RefreshDailyQuests()
    {
        _lastDaySeed = GetTodayId();
        int daySeed = int.Parse(_lastDaySeed); // yyyyMMdd as int

        _activeDailyQuests.Clear();

        foreach (var kvp in DAILY_QUEST_POOL)
        {
            string npcId = kvp.Key;
            var pool = kvp.Value;
            if (pool.Count == 0) continue;

            // Deterministic selection: hash npcId + day
            int hash = (npcId.GetHashCode() ^ daySeed) & 0x7FFFFFFF;
            int index = hash % pool.Count;
            var def = pool[index];

            // Check if already completed today
            var data = MainPlayerData.Instance;
            string completedKey = $"daily_{def.QuestId}_{_lastDaySeed}";
            bool alreadyComplete = data != null && data.CompletedQuests.Contains(completedKey);

            _activeDailyQuests[npcId] = new DailyQuestState
            {
                Definition = def,
                NPCId = npcId,
                Progress = 0,
                Accepted = alreadyComplete, // if completed, was accepted
                Completed = alreadyComplete,
                Target = def.Target
            };
        }

        Debug.Log($"[Quest] Daily quests refreshed for {_lastDaySeed}: {_activeDailyQuests.Count} quests");
    }

    /// <summary>Get today's daily quest for an NPC. Returns null if NPC has no quest pool.</summary>
    public DailyQuestState GetDailyQuestForNPC(string npcId)
    {
        _activeDailyQuests.TryGetValue(npcId, out DailyQuestState state);
        return state;
    }

    /// <summary>Mark a daily quest as accepted.</summary>
    public void AcceptDailyQuest(string npcId)
    {
        if (_activeDailyQuests.TryGetValue(npcId, out DailyQuestState state))
        {
            state.Accepted = true;
            OnQuestProgress?.Invoke($"Quest accepted: {state.Definition.Title}");
            Debug.Log($"[Quest] Accepted: {state.Definition.Title} from {npcId}");
        }
    }

    /// <summary>Complete a daily quest and grant rewards.</summary>
    public void CompleteDailyQuest(string npcId)
    {
        if (!_activeDailyQuests.TryGetValue(npcId, out DailyQuestState state)) return;
        if (state.Completed) return;
        if (state.Progress < state.Target) return;

        state.Completed = true;

        var data = MainPlayerData.Instance;
        if (data == null) return;

        // Mark completed for today
        string completedKey = $"daily_{state.Definition.QuestId}_{_lastDaySeed}";
        if (!data.CompletedQuests.Contains(completedKey))
            data.CompletedQuests.Add(completedKey);

        // Grant rewards
        var reward = state.Definition.Reward;
        data.AddGold(reward.Gold);
        ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Exploration, reward.XP);

        if (!string.IsNullOrEmpty(reward.Title) && !data.Titles.Contains(reward.Title))
            data.Titles.Add(reward.Title);

        OnQuestComplete?.Invoke(state.Definition.Title, reward.Gold);
        Debug.Log($"[Quest] Completed: {state.Definition.Title} — +{reward.Gold}g +{reward.XP}xp");

        MainPlayerData.SaveToCloud();
    }

    /// <summary>Get all active (accepted, not completed) daily quests.</summary>
    public List<DailyQuestState> GetActiveDailyQuests()
    {
        return _activeDailyQuests.Values
            .Where(q => q.Accepted && !q.Completed)
            .ToList();
    }

    /// <summary>Get all available (not yet accepted) daily quests.</summary>
    public List<DailyQuestState> GetAvailableDailyQuests()
    {
        return _activeDailyQuests.Values
            .Where(q => !q.Accepted && !q.Completed)
            .ToList();
    }

    // =========================================================================
    // PROGRESS REPORTING — Called by game systems
    // =========================================================================

    /// <summary>Report progress toward daily quests of a given type.</summary>
    public void ReportDailyQuestProgress(DailyQuestType type, int amount, string region = null)
    {
        foreach (var kvp in _activeDailyQuests)
        {
            var state = kvp.Value;
            if (!state.Accepted || state.Completed) continue;
            if (state.Definition.Type != type) continue;

            // Region-specific quests: only count if region matches
            if (!string.IsNullOrEmpty(state.Definition.Region) &&
                !string.IsNullOrEmpty(region) &&
                state.Definition.Region != region)
                continue;

            state.Progress += amount;

            if (state.Progress >= state.Target)
            {
                state.Progress = state.Target;
                OnQuestProgress?.Invoke($"{state.Definition.Title}: Complete! Return to {state.NPCId}.");
            }
            else
            {
                OnQuestProgress?.Invoke($"{state.Definition.Title}: {state.Progress}/{state.Target}");
            }
        }
    }

    /// <summary>Report material collection for material-specific quests.</summary>
    public void ReportMaterialCollected(string materialId, int amount)
    {
        foreach (var kvp in _activeDailyQuests)
        {
            var state = kvp.Value;
            if (!state.Accepted || state.Completed) continue;
            if (state.Definition.Type != DailyQuestType.CollectMaterial) continue;

            // If quest specifies a material, must match. If empty, any material counts.
            if (!string.IsNullOrEmpty(state.Definition.MaterialId) &&
                state.Definition.MaterialId != materialId)
                continue;

            state.Progress += amount;
            if (state.Progress >= state.Target)
            {
                state.Progress = state.Target;
                OnQuestProgress?.Invoke($"{state.Definition.Title}: Complete! Return to NPC.");
            }
            else
            {
                OnQuestProgress?.Invoke($"{state.Definition.Title}: {state.Progress}/{state.Target}");
            }
        }
    }

    /// <summary>Report essence collected — checks potency and total stats thresholds.</summary>
    public void ReportEssenceCollected(EssenceItem essence)
    {
        foreach (var kvp in _activeDailyQuests)
        {
            var state = kvp.Value;
            if (!state.Accepted || state.Completed) continue;

            switch (state.Definition.Type)
            {
                case DailyQuestType.CollectEssence:
                    state.Progress++;
                    break;

                case DailyQuestType.CollectEssencePotency:
                    if (essence.Potency > state.Target)
                        state.Progress = state.Target; // binary: met or not
                    break;

                case DailyQuestType.CollectEssenceTotalStats:
                    if (essence.TotalStats >= state.Target)
                        state.Progress = state.Target;
                    break;

                default:
                    continue;
            }

            if (state.Progress >= state.Target)
                OnQuestProgress?.Invoke($"{state.Definition.Title}: Complete! Return to NPC.");
            else
                OnQuestProgress?.Invoke($"{state.Definition.Title}: {state.Progress}/{state.Target}");
        }
    }

    /// <summary>Report crafting — quality grade for Superior+ check.</summary>
    public void ReportItemCrafted(string qualityGrade, bool usedFireEssence)
    {
        foreach (var kvp in _activeDailyQuests)
        {
            var state = kvp.Value;
            if (!state.Accepted || state.Completed) continue;

            switch (state.Definition.Type)
            {
                case DailyQuestType.CraftAny:
                    state.Progress++;
                    break;

                case DailyQuestType.CraftSuperior:
                    if (qualityGrade == "Superior" || qualityGrade == "Mastercraft")
                        state.Progress++;
                    break;

                case DailyQuestType.CraftWithFireEssence:
                    if (usedFireEssence) state.Progress++;
                    break;

                default:
                    continue;
            }

            if (state.Progress >= state.Target)
                OnQuestProgress?.Invoke($"{state.Definition.Title}: Complete! Return to NPC.");
            else
                OnQuestProgress?.Invoke($"{state.Definition.Title}: {state.Progress}/{state.Target}");
        }
    }

    /// <summary>Report walking distance.</summary>
    public void ReportDistanceWalked(float distance)
    {
        ReportDailyQuestProgress(DailyQuestType.WalkDistance, Mathf.RoundToInt(distance));
    }

    /// <summary>Report zone visit.</summary>
    public void ReportZoneVisited()
    {
        ReportDailyQuestProgress(DailyQuestType.VisitZones, 1);
    }

    /// <summary>Report region visit.</summary>
    public void ReportRegionVisited()
    {
        ReportDailyQuestProgress(DailyQuestType.VisitRegions, 1);
    }

    /// <summary>Report chest opened.</summary>
    public void ReportChestOpened()
    {
        ReportDailyQuestProgress(DailyQuestType.OpenChest, 1);
    }

    /// <summary>Report market listing.</summary>
    public void ReportMarketListing()
    {
        ReportDailyQuestProgress(DailyQuestType.ListOnMarket, 1);
    }

    /// <summary>Report gold earned (non-quest sources).</summary>
    public void ReportGoldEarned(int amount)
    {
        ReportDailyQuestProgress(DailyQuestType.EarnGold, amount);
    }

    /// <summary>Report spirit feeding.</summary>
    public void ReportSpiritFed()
    {
        ReportDailyQuestProgress(DailyQuestType.FeedSpirit, 1);
    }

    /// <summary>Report spirit recruitment.</summary>
    public void ReportSpiritRecruited()
    {
        ReportDailyQuestProgress(DailyQuestType.RecruitSpirit, 1);
    }

    /// <summary>Report winning without taking damage.</summary>
    public void ReportFlawlessVictory()
    {
        ReportDailyQuestProgress(DailyQuestType.WinNoDamage, 1);
    }

    /// <summary>Report defeating a rare enemy.</summary>
    public void ReportRareDefeated()
    {
        ReportDailyQuestProgress(DailyQuestType.DefeatRare, 1);
    }

    /// <summary>Report defeating an elite enemy.</summary>
    public void ReportEliteDefeated()
    {
        ReportDailyQuestProgress(DailyQuestType.DefeatElite, 1);
    }

    /// <summary>Report a dungeon completion for quest progress.</summary>
    public void ReportDungeonCompleted(string dungeonId)
    {
        ReportDailyQuestProgress(DailyQuestType.CompleteDungeon, 1);
    }

    /// <summary>Report a puzzle solved for quest progress.</summary>
    public void ReportPuzzleSolved()
    {
        ReportDailyQuestProgress(DailyQuestType.CompletePuzzle, 1);
    }

    /// <summary>Check if playing during night phase and report.</summary>
    public void CheckNightPhase()
    {
        int hour = DateTime.Now.Hour;
        bool isNight = hour >= 20 || hour < 6;
        if (isNight)
            ReportDailyQuestProgress(DailyQuestType.PlayDuringNight, 1);
    }

    // =========================================================================
    // DAILY QUEST: MASK OF DESTINY (kept from original)
    // =========================================================================

    /// <summary>Check if Mask of Destiny is available (resets daily at midnight).</summary>
    public bool IsMaskQuestAvailable()
    {
        var data = MainPlayerData.Instance;
        if (data.CompletedQuests.Contains($"mask_{GetTodayId()}")) return false;
        return true;
    }

    /// <summary>Start or continue the Mask of Destiny quest.</summary>
    public void StartMaskQuest()
    {
        var data = MainPlayerData.Instance;
        string todayId = GetTodayId();

        if (data.ActiveQuest == null || data.ActiveQuest.QuestId != $"mask_{todayId}")
        {
            data.ActiveQuest = new QuestState
            {
                QuestId = $"mask_{todayId}",
                Phase = 0,
                Progress = new Dictionary<string, object>(),
                StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        OnQuestProgress?.Invoke("Mask of Destiny quest started! Visit all 4 regions.");
    }

    /// <summary>Report a region visit for the Mask quest.</summary>
    public void ReportMaskRegionVisit(string region)
    {
        var data = MainPlayerData.Instance;
        if (data.ActiveQuest == null || !data.ActiveQuest.QuestId.StartsWith("mask_")) return;

        string key = $"visited_{region}";
        data.ActiveQuest.Progress[key] = true;

        string[] regions = { "frost_valley", "rolling_hills", "volcanic_isles", "dark_castle" };
        int visited = 0;
        foreach (string r in regions)
            if (data.ActiveQuest.Progress.ContainsKey($"visited_{r}"))
                visited++;

        if (visited >= 4)
            CompleteMaskQuest();
        else
            OnQuestProgress?.Invoke($"Mask of Destiny: {visited}/4 regions visited");
    }

    void CompleteMaskQuest()
    {
        var data = MainPlayerData.Instance;
        data.CompletedQuests.Add(data.ActiveQuest.QuestId);
        data.ActiveQuest = null;

        data.AddGold(200);
        ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Exploration, 100);

        OnQuestComplete?.Invoke("Mask of Destiny", 200);
        MainPlayerData.SaveToCloud();
    }

    // =========================================================================
    // WEEKLY CHALLENGES (kept from original)
    // =========================================================================

    public void ReportChallengeProgress(string challengeId, int amount = 1)
    {
        var data = MainPlayerData.Instance;
        if (!data.WeeklyChallenges.ContainsKey(challengeId))
            data.WeeklyChallenges[challengeId] = 0;

        data.WeeklyChallenges[challengeId] += amount;

        var challenge = WeeklyChallenges.Find(c => c.ChallengeId == challengeId);
        if (challenge != null && data.WeeklyChallenges[challengeId] >= challenge.Target)
        {
            data.AddGold(challenge.GoldReward);
            OnQuestComplete?.Invoke(challenge.ChallengeName, challenge.GoldReward);
        }
    }

    // =========================================================================
    // LORE & COLLECTIBLES (kept from original)
    // =========================================================================

    public void DiscoverLore(string loreId)
    {
        var data = MainPlayerData.Instance;
        if (data.DiscoveredLore.Contains(loreId)) return;

        data.DiscoveredLore.Add(loreId);
        ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Exploration, 25);
        OnLoreDiscovered?.Invoke(loreId);
        MainPlayerData.SaveToCloud();
    }

    public void FindCrystal(string crystalId)
    {
        var data = MainPlayerData.Instance;
        if (data.FoundCrystals.Contains(crystalId)) return;

        data.FoundCrystals.Add(crystalId);
        ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Exploration, 50);
        data.AddGold(50);
        OnCrystalFound?.Invoke(crystalId);

        // Crystal quests
        ReportDailyQuestProgress(DailyQuestType.CollectCrystal, 1);

        MainPlayerData.SaveToCloud();
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    string GetTodayId() => DateTime.UtcNow.ToString("yyyyMMdd");
}

// =========================================================================
// DATA CLASSES
// =========================================================================

[Serializable]
public class QuestDefinition
{
    public string QuestId;
    public string QuestName;
    public string Description;
    public int Phases;
    public int GoldReward;
    public float XPReward;
}

[Serializable]
public class WeeklyChallenge
{
    public string ChallengeId;
    public string ChallengeName;
    public string Description;
    public int Target;
    public int GoldReward;
}

// ═══════ DAILY QUEST TYPES ═══════

public enum DailyQuestType
{
    WinBattle,
    CollectMaterial,
    CollectEssence,
    CollectEssencePotency,
    CollectEssenceTotalStats,
    CollectCrystal,
    CraftAny,
    CraftSuperior,
    CraftWithFireEssence,
    DefeatRare,
    DefeatElite,
    RecruitSpirit,
    FeedSpirit,
    WalkDistance,
    VisitZones,
    VisitRegions,
    OpenChest,
    ListOnMarket,
    EarnGold,
    WinNoDamage,
    PlayDuringNight,
    CompleteDungeon,
    CompletePuzzle
}

[Serializable]
public class DailyQuestDef
{
    public string QuestId;
    public string Title;
    public string Description;
    public DailyQuestType Type;
    public int Target;
    public string Region;       // optional: region lock for battle quests
    public string MaterialId;   // optional: specific material for collect quests
    public DailyQuestReward Reward;
}

[Serializable]
public class DailyQuestReward
{
    public int Gold;
    public int XP;
    public string Title; // optional title unlock
}

[Serializable]
public class DailyQuestState
{
    public DailyQuestDef Definition;
    public string NPCId;
    public int Progress;
    public int Target;
    public bool Accepted;
    public bool Completed;

    // Convenience accessors
    public string Title => Definition?.Title ?? "";
    public string Description => Definition?.Description ?? "";
}
