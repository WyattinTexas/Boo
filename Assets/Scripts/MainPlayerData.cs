using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class MainPlayerData
{
    public static MainPlayerData Instance;
    public static string SavePath => $"{Application.persistentDataPath}/MainPlayerData.json";

    public GameInfo InGameInfo = null;
    public WorldSaved WorldSaved = new();

    public Dictionary<string, CardSaved> SavedCards = new();
    public Dictionary<string, NotificationInfo> NotificationInfos = new();
    public List<string> SlottedCardIds = new();

    public List<GraveyardChoiceArgs> GraveyardChoiceArgs = new();

    public int DefeatedGhostCount = 0;
    public float Lifetime = 0;
    public bool IsGameOver = false;
    public bool DefeatedGame = false;

    // Team progression — sideline unlocks after 5 battle wins
    public bool SidelineUnlocked => DefeatedGhostCount >= 5;
    public int EffectiveTeamSize => SidelineUnlocked ? 3 : 1;
    public bool SidelineUnlockNotified = false;

    // ═══════ OVERWORLD ONLINE — MMO FIELDS ═══════

    // Character
    public string PlayerName = "";
    public int SpriteIndex = 0;
    public bool CharacterCreated = false;

    // Economy
    public int Gold = 100;
    public List<InventoryItem> Inventory = new();
    public List<EssenceItem> Essences = new();
    public List<CraftedItem> CraftedItems = new();
    public Dictionary<string, int> Materials = new(); // material_id → count

    // Equipment
    public string EquippedWeapon = "";
    public string EquippedArmor = "";
    public string EquippedAccessory = "";

    // Professions
    public Dictionary<string, float> ProfessionXP = new()
    {
        ["combat"] = 0, ["exploration"] = 0, ["crafting"] = 0, ["trade"] = 0, ["charisma"] = 0
    };
    public HashSet<string> UnlockedSkills = new();
    public int SkillPointsUsed = 0;
    public const int SkillPointsCap = 80;

    // Crafting Mastery
    public Dictionary<string, MasteryData> Mastery = new();

    // Quests
    public QuestState ActiveQuest = null;
    public List<string> CompletedQuests = new();
    public Dictionary<string, int> WeeklyChallenges = new(); // challenge_id → progress
    public HashSet<string> DiscoveredLore = new();
    public HashSet<string> FoundCrystals = new();
    public HashSet<string> FoundViewpoints = new();

    // Social
    public string GuildId = "";
    public string PartyId = "";
    public string Title = "";
    public List<string> Titles = new();

    // Housing
    public bool OwnsHouse = false;
    public List<string> HouseFurniture = new();

    // Exploration
    public HashSet<string> VisitedTiles = new();
    public HashSet<string> VisitedZones = new();
    public HashSet<string> VisitedRegions = new();

    // Achievements
    public HashSet<string> UnlockedAchievements = new();

    // Arena PvP
    public int ArenaRating = 1000;
    public int ArenaWins = 0;
    public int ArenaLosses = 0;

    // Market
    public int MarketListingSlots = 3;

    //Settings
    public Dictionary<VolumeType, float> VolumeValues = new();

    public MainPlayerData() { }
    public MainPlayerData(MainPlayerData other)
    {
        if (other == null)
            return;

        //Copy settings.
        VolumeValues = other.VolumeValues;
    }

    public static void Load()
    {
        Instance = SaveToJson.LoadFile<MainPlayerData>(SavePath);
        if (Instance == null || Instance.IsGameOver)
        {
            var newData = new MainPlayerData();
            newData.OnCreated();
            Instance = newData;
        }

        Instance.Validate();
    }
    public static void Save(bool reset = false)
    {
        var toSave = Instance;
        if (reset)
        {
            Instance.OnDeleted();
            toSave = new(Instance);
            toSave.OnCreated();
        }
        SaveToJson.SaveFile(toSave, SavePath);
    }

    public void OnCreated()
    {
        //Set default volumes.
        VolumeValues[VolumeType.Master] = 0.7f;
        VolumeValues[VolumeType.Effects] = 0.7f;
        VolumeValues[VolumeType.Ambient] = 0.7f;
        VolumeValues[VolumeType.Music] = 0.7f;
    }
    public void OnDeleted()
    {
        //Clear graveyard notifs.
        NotificationManager.ClearRespawnNotifications();
    }

    void Validate()
    {
        //Add all card saveds.
        int ownedCardCount = 0;
        foreach (var pair in AssetManager.Cards)
        {
            var saved = GetSavedCard(pair.Key);
            if (saved.IsOwned)
                ownedCardCount++;
        }

        //No cards, roll starting cards.
        int startCount = CardManager.Instance.StartingCardCount;
        if (ownedCardCount < startCount)
        {
            var startingCards = CardManager.Instance.RollStartingCards(startCount - ownedCardCount);
            startingCards.ForEach(x => CardManager.RewardCard(x));
        }
    }

    /// <returns>If the owned status changed.</returns>
    public bool SetOwned(string cardId, bool owned)
    {
        var saved = GetSavedCard(cardId);
        bool hasAlready = saved.IsOwned;
        //Set owned and already owned, false.
        //Set unowned and already unowned, false.
        if (owned == hasAlready)
            return false;

        saved.IsOwned = owned;
        return true;
    }
    public bool IsOwned(string cardId)
    {
        var saved = GetSavedCard(cardId);
        if (saved.IsOwned)
            return true;
        if (DevTools.Instance.OwnAllCards)
            return true;

        return false;
    }

    public CardSaved GetSavedCard(string cardId)
    {
        if (!SavedCards.TryGetValue(cardId, out CardSaved saved))
        {
            saved = new CardSaved()
            {
                CardId = cardId,
            };

            SavedCards[cardId] = saved;
        }
        return saved;
    }

    public bool TryGetNotificationInfo(string id, out NotificationInfo info)
    {
        return NotificationInfos.TryGetValue(id, out info);
    }

    public void SetGameOver(bool gameOver, bool defeatedGame)
    {
        IsGameOver = gameOver;
        DefeatedGame = defeatedGame;
    }

    // ═══════ FIREBASE CLOUD SAVE ═══════

    /// <summary>Save player data to Firebase (WebGL) in addition to local.</summary>
    public static void SaveToCloud()
    {
        Save();
        if (FirebaseService.IsAuthenticated)
        {
            string path = $"overworld/saves/{FirebaseService.UID}";
            FirebaseService.Instance.Set(path, Instance);
        }
    }

    /// <summary>Load from Firebase if available, else local.</summary>
    public static void LoadFromCloud(Action onComplete = null)
    {
        if (!FirebaseService.IsAuthenticated)
        {
            Load();
            onComplete?.Invoke();
            return;
        }

        string path = $"overworld/saves/{FirebaseService.UID}";
        FirebaseService.Instance.Get(path, (data) =>
        {
            if (data != null)
            {
                try
                {
                    var cloudSave = Newtonsoft.Json.JsonConvert.DeserializeObject<MainPlayerData>(data.ToString(), SaveToJson._settings);
                    if (cloudSave != null && cloudSave.CharacterCreated)
                    {
                        Instance = cloudSave;
                        Instance.Validate();
                        Debug.Log("[Save] Loaded from cloud");
                        onComplete?.Invoke();
                        return;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Save] Cloud parse error: {e.Message}");
                }
            }

            // Fallback to local
            Load();
            onComplete?.Invoke();
        });
    }

    // ═══════ PROFESSION HELPERS ═══════

    public bool HasSkill(string skillId) => UnlockedSkills.Contains(skillId);

    public float GetProfessionXP(string type)
    {
        ProfessionXP.TryGetValue(type, out float xp);
        return xp;
    }

    // ═══════ INVENTORY HELPERS ═══════

    public int GetMaterialCount(string materialId)
    {
        Materials.TryGetValue(materialId, out int count);
        return count;
    }

    public bool SpendMaterial(string materialId, int amount)
    {
        if (GetMaterialCount(materialId) < amount) return false;
        Materials[materialId] -= amount;
        if (Materials[materialId] <= 0) Materials.Remove(materialId);
        return true;
    }

    public void AddMaterial(string materialId, int amount)
    {
        if (!Materials.ContainsKey(materialId)) Materials[materialId] = 0;
        Materials[materialId] += amount;
    }

    public void AddGold(int amount)
    {
        Gold += amount;
    }

    public bool SpendGold(int amount)
    {
        if (Gold < amount) return false;
        Gold -= amount;
        return true;
    }
}

// ═══════ DATA CLASSES ═══════

[Serializable]
public class InventoryItem
{
    public string ItemId;
    public string ItemName;
    public string ItemType; // weapon, armor, accessory, consumable, component
    public int Quantity = 1;
}

[Serializable]
public class EssenceItem
{
    public string Id;
    public string Name;
    public string Subtype;
    public string FromCard;
    public string FromName;
    public string Region;
    public string Rarity;
    public int Potency;
    public int Stability;
    public int Resonance;
    public int Purity;
    public long Timestamp;

    public int TotalStats => Potency + Stability + Resonance + Purity;
}

[Serializable]
public class CraftedItem
{
    public string Id;
    public string SchematicId;
    public string ItemName;
    public string CrafterName;
    public string Slot; // weapon, head, accessory
    public string Description;
    public int BasePower;
    public string QualityGrade; // Basic, Standard, Quality, Superior, Mastercraft
    public float QualityScore;
    public Dictionary<string, float> Stats = new(); // potency, stability, resonance, purity
    public long CraftedAt;
    public string Serial; // unique identifier
}

[Serializable]
public class MasteryData
{
    public int Xp;
    public int Level;
    public int ItemsCrafted;
}

[Serializable]
public class QuestState
{
    public string QuestId;
    public int Phase;
    public Dictionary<string, object> Progress = new();
    public long StartedAt;
}

[Serializable]
public enum VolumeType
{
    //Max 3
    Master      = 0,
    Effects     = 1,
    Ambient     = 2,
    Music       = 3,
}