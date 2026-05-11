using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The bridge between the existing Unity game and all new Overworld Online systems.
/// Hooks into GameManager events, WorldManager, and builds runtime UI overlay.
/// This is the single integration point — attach to a GameObject in every scene.
/// </summary>
public class OverworldIntegration : MonoBehaviour
{
    public static OverworldIntegration Instance { get; private set; }

    /// <summary>Fired when a trainer battle ends. Args: trainerName, playerWon.</summary>
    public static event Action<string, bool> OnTrainerDefeated;

    [Header("Runtime UI")]
    Canvas _overlayCanvas;
    GameObject _hudPanel;
    TextMeshProUGUI _goldText;
    TextMeshProUGUI _xpText; // kept for fallback

    // Lerp display — XP numbers smoothly count up
    float _displayCombat, _displayCraft, _displayExplore;
    const float XP_LERP_SPEED = 3f;

    // SWG-style skill bars
    GameObject _skillBarPanel;
    Image _combatFill, _craftFill, _exploreFill;
    TextMeshProUGUI _combatLabel, _craftLabel, _exploreLabel;
    // Milestone thresholds — segments between each
    static readonly int[] SKILL_MILESTONES = { 0, 100, 250, 500, 1000, 2000, 3500, 5000 };
    TextMeshProUGUI _notificationText;
    GameObject _chatPanel;
    TMP_InputField _chatInput;
    Transform _chatMessageParent;
    ScrollRect _chatScroll;
    GameObject _menuPanel;
    GameObject _craftingPanel;
    GameObject _professionPanel;
    GameObject _marketPanel;
    GameObject _partyPanel;
    GameObject _guildPanel;

    // Market panel internals
    Transform _marketForSaleContent;
    Transform _marketMyListingsContent;
    ScrollRect _marketForSaleScroll;
    ScrollRect _marketMyListingsScroll;

    // Party panel internals
    Transform _partyMembersContent;
    Transform _partyNearbyContent;
    ScrollRect _partyMembersScroll;
    ScrollRect _partyNearbyScroll;

    // Guild panel internals
    Transform _guildContent;
    ScrollRect _guildScroll;

    // Profession panel internals
    Transform _professionScrollContent;
    ScrollRect _professionScroll;

    // Crafting panel internals
    Transform _craftEssenceContent;
    Transform _craftSchematicContent;
    Transform _craftInventoryContent;
    Transform _craftResultArea;
    ScrollRect _craftEssenceScroll;
    ScrollRect _craftSchematicScroll;
    ScrollRect _craftInventoryScroll;
    SchematicData[] _allSchematics;
    List<EssenceItem> _selectedEssences = new();
    SchematicData _selectedSchematic;

    // Spiritkin team HP bars
    GameObject _teamHPPanel;
    List<(TextMeshProUGUI nameLabel, Image hpFill, TextMeshProUGUI hpText)> _teamBars = new();

    // Progress tracker
    GameObject _progressPanel;
    TextMeshProUGUI _progressRegionText;
    TextMeshProUGUI _progressQuestText;
    TextMeshProUGUI _progressStatsText;

    // Notification queue
    readonly Queue<string> _notifications = new();
    float _notifTimer;
    const float NotifDuration = 3f;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Subscribe to game events
        GameManager.OnGameStarted += OnBattleStarted;
        GameManager.OnWinResult += OnRollWin;
        SceneSwitcher.OnSceneLoaded += OnSceneChanged;

        // Subscribe to MMO events
        ProfessionManager.OnXPGained += OnXPGained;
        ProfessionManager.OnSkillUnlocked += OnSkillUnlocked;
        ProfessionManager.OnMilestoneReached += ShowNotification;
        CraftingManager.OnItemCrafted += OnItemCrafted;
        CraftingManager.OnCraftingNotification += ShowNotification;
        ChatSystem.OnMessageReceived += OnChatMessage;
        QuestManager.OnQuestComplete += OnQuestCompleted;
        QuestManager.OnQuestProgress += ShowNotification;
        RegionManager.OnRegionChanged += OnRegionChanged;
        MarketManager.OnMarketNotification += ShowNotification;
        MarketManager.OnItemPurchased += OnMarketItemPurchased;
        MarketManager.OnListingAdded += OnMarketListingAdded;
        PartySystem.OnPlayerJoinedParty += OnPartyPlayerJoined;
        PartySystem.OnPlayerLeftParty += OnPartyPlayerLeft;
        PartySystem.OnPartyDisbanded += OnPartyDisbanded;
        GuildSystem.OnGuildJoined += OnGuildJoined;
        GuildSystem.OnGuildLeft += OnGuildLeft;

        // Build the HUD
        BuildOverlayUI();

        // Create SpiritComms (Star Fox dialogue system)
        if (SpiritComms.Instance == null)
        {
            var commsGO = new GameObject("SpiritComms");
            commsGO.AddComponent<SpiritComms>();
            DontDestroyOnLoad(commsGO);
        }

        // Hook into Firebase auth — initialization is handled by OverworldBootstrap
        FirebaseService.OnAuthenticated += OnFirebaseReady;
        // If already authenticated (Bootstrap ran first), fire immediately
        if (FirebaseService.IsAuthenticated)
            OnFirebaseReady(FirebaseService.UID);

        Debug.Log("[Overworld] Integration layer active");
    }

    void OnDestroy()
    {
        GameManager.OnGameStarted -= OnBattleStarted;
        GameManager.OnWinResult -= OnRollWin;
        SceneSwitcher.OnSceneLoaded -= OnSceneChanged;
        ProfessionManager.OnXPGained -= OnXPGained;
        ProfessionManager.OnSkillUnlocked -= OnSkillUnlocked;
        ProfessionManager.OnMilestoneReached -= ShowNotification;
        CraftingManager.OnItemCrafted -= OnItemCrafted;
        CraftingManager.OnCraftingNotification -= ShowNotification;
        ChatSystem.OnMessageReceived -= OnChatMessage;
        MarketManager.OnMarketNotification -= ShowNotification;
        MarketManager.OnItemPurchased -= OnMarketItemPurchased;
        MarketManager.OnListingAdded -= OnMarketListingAdded;
        PartySystem.OnPlayerJoinedParty -= OnPartyPlayerJoined;
        PartySystem.OnPlayerLeftParty -= OnPartyPlayerLeft;
        PartySystem.OnPartyDisbanded -= OnPartyDisbanded;
        GuildSystem.OnGuildJoined -= OnGuildJoined;
        GuildSystem.OnGuildLeft -= OnGuildLeft;
        FirebaseService.OnAuthenticated -= OnFirebaseReady;
    }

    void Update()
    {
        UpdateHUD();
        UpdateNotifications();
    }

    // =========================================================================
    // GAME EVENT HOOKS
    // =========================================================================

    void OnBattleStarted(GameManager gm)
    {
        Debug.Log("[Overworld] Battle started — tracking for XP/essences");

        // Apply profession effects to battle
        if (ProfessionManager.Instance != null)
        {
            // Extra dice from Spirit Hunter path
            int bonusDice = ProfessionManager.Instance.GetFirstRollBonusDice();
            if (bonusDice > 0)
                Debug.Log($"[Overworld] Profession bonus: +{bonusDice} first roll dice");

            // Damage multiplier from Hunter/Spirit Hunter path
            float damageMult = ProfessionManager.Instance.GetDamageMultiplier();
            if (damageMult > 1f)
            {
                Debug.Log($"[Overworld] Profession damage multiplier: x{damageMult:F2}");
                gm.BeforeCardHealthChangeFuncs.Add((card, ctx) =>
                {
                    // Only boost damage the player's cards deal to enemies
                    if (ctx.Applier != null && ctx.Applier.User == gm.ClientPlayer
                        && ctx.Change.Amount < 0)
                    {
                        ctx.Change.ApplyMultiplier(damageMult);
                    }
                    return EmptyCoroutine();
                });
            }
        }

        // Apply equipped gear effects
        ApplyEquippedGear();
    }

    static System.Collections.IEnumerator EmptyCoroutine() { yield break; }

    void OnRollWin(RollWinCtx ctx)
    {
        // Small XP per turn
        ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Combat, 1);
    }

    void OnSceneChanged(SceneType sceneType, LoadSceneMode mode)
    {
        Debug.Log($"[Overworld] Scene loaded: {sceneType} — refreshing HUD");

        // Rebuild UI references if needed (Canvas gets destroyed on scene change)
        if (_overlayCanvas == null)
            BuildOverlayUI();

        // If World scene, set up gathering/NPCs
        if (WorldManager.Instance != null)
        {
            StartCoroutine(SetupWorldFeatures());
        }
    }

    /// <summary>Called by GameManager.OnGameEnded — we patch this in via Harmony or manual hook.</summary>
    public static void OnBattleEnded(GameInfo info)
    {
        // Always clear the override lineup when a battle ends
        GameManager.OverrideEnemyLineup = null;

        if (Instance == null) return;
        var data = MainPlayerData.Instance;
        if (data == null || info == null) return;

        bool won = info.ClientWon == true;

        // Combat XP
        float combatXP = won ? 10 : 5;
        ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Combat, combatXP);

        // Build reward data for overlay
        var rewardData = new RewardsOverlay.RewardData
        {
            Won = won,
            CombatXP = combatXP,
        };

        if (won)
        {
            // Generate essence drop
            var encounter = info.Encounter();
            if (encounter != null)
            {
                string cardName = encounter.GetGhostName() ?? encounter.name;
                string rarity = encounter.EncounterCard != null
                    ? encounter.EncounterCard.Rarity.ToString().ToLower()
                    : "common";
                string zone = RegionManager.Instance != null
                    ? RegionManager.Instance.CurrentRegion
                    : RegionManager.FrostValley;

                var essence = CraftingManager.GenerateEssence(cardName, rarity, zone);
                data.Essences.Add(essence);

                rewardData.EssenceName = essence.Name;
                rewardData.EssencePotency = essence.Potency;
                rewardData.EssenceStability = essence.Stability;
                rewardData.EssenceResonance = essence.Resonance;
                rewardData.EssencePurity = essence.Purity;

                Instance.ShowNotification($"Obtained: {essence.Name}!");

                // Gold reward
                int goldReward = UnityEngine.Random.Range(5, 20);
                data.AddGold(goldReward);
                rewardData.GoldEarned = goldReward;
                Instance.ShowNotification($"+{goldReward} Gold");

                // Recruitment: 15% base, 30% for rare+, plus profession bonus
                bool isRareOrBetter = encounter.EncounterCard != null
                    && encounter.EncounterCard.Rarity >= Rarity.Rare;
                float recruitChance = isRareOrBetter ? 0.30f : 0.15f;
                if (ProfessionManager.Instance != null)
                    recruitChance += ProfessionManager.Instance.GetRecruitBonus();

                if (UnityEngine.Random.value < recruitChance)
                {
                    string recruitCardId = encounter.EncounterCard != null
                        ? encounter.EncounterCard.AssetId
                        : null;

                    if (!string.IsNullOrEmpty(recruitCardId)
                        && !data.SlottedCardIds.Contains(recruitCardId))
                    {
                        data.SlottedCardIds.Add(recruitCardId);
                        string recruitName = encounter.GetGhostName() ?? "Unknown Spirit";
                        rewardData.RecruitedCardName = recruitName;
                        Debug.Log($"[Overworld] Recruited {recruitName} (card {recruitCardId})!");
                    }
                }
            }

            // Exploration XP for visiting new locations
            string locationId = info.LocationId;
            if (!string.IsNullOrEmpty(locationId) && !data.VisitedZones.Contains(locationId))
            {
                data.VisitedZones.Add(locationId);
                float exploXP = 20;
                ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Exploration, exploXP);
                rewardData.ExplorationXP = exploXP;
                Instance.ShowNotification("New zone discovered!");
            }
        }
        else
        {
            // Defeat — still show the overlay with combat XP
        }

        // Save
        MainPlayerData.SaveToCloud();

        // Clean up battle resources
        if (BattleResourceManager.Instance != null)
            BattleResourceManager.Instance.OnBattleEnd();

        // Fire trainer event if this was a trainer battle
        if (info.LocationId != null && info.LocationId.StartsWith("trainer_"))
        {
            string trainerName = info.LocationId.Substring("trainer_".Length);
            OnTrainerDefeated?.Invoke(trainerName, won);
        }

        // Report battle win to quest system
        if (won)
        {
            string region = RegionManager.Instance?.CurrentRegion;
            QuestManager.Instance?.ReportDailyQuestProgress(DailyQuestType.WinBattle, 1, region);
            QuestManager.Instance?.ReportEssenceCollected(data.Essences.LastOrDefault());
        }

        // Show rewards overlay
        RewardsOverlay.Show(rewardData, null);
    }

    // =========================================================================
    // ROAMING ENEMY → BATTLE
    // =========================================================================

    RoamingEnemy _currentBattleEnemy;

    /// <summary>Called by RoamingEnemy when it catches the player.</summary>
    public void TriggerEnemyBattle(RoamingEnemy enemy)
    {
        if (enemy == null) return;
        _currentBattleEnemy = enemy;

        ShowNotification($"Wild {enemy.EnemyName} attacks!");

        Debug.Log($"[Overworld] Starting battle with {enemy.EnemyName} (Card:{enemy.CardId}, HP:{enemy.CardMaxHp}, Elite:{enemy.IsElite})");

        // Build the override lineup from the roaming enemy's actual card data
        var enemyLineup = BuildEnemyLineup(enemy);
        if (enemyLineup != null && enemyLineup.Count >= 1)
        {
            GameManager.OverrideEnemyLineup = enemyLineup;
            GameManager.IsWildBattle = true;
            Debug.Log($"[Overworld] Override lineup set: [{string.Join(", ", enemyLineup.ConvertAll(c => c.CardName))}]");
        }
        else
        {
            Debug.LogWarning("[Overworld] Could not build enemy lineup, falling back to encounter default");
            GameManager.OverrideEnemyLineup = null;
        }

        // Still need a template encounter for the battle system (music, dialogs, rewards, etc.)
        if (WorldManager.Instance != null)
        {
            var locations = WorldManager.Instance.Locations;
            Encounter foundEncounter = null;
            string locationId = null;

            foreach (var loc in locations)
            {
                if (loc.Encounters != null && loc.Encounters.Count > 0)
                {
                    foundEncounter = loc.Encounters[0];
                    locationId = loc.LocationId;
                    break;
                }
            }

            if (foundEncounter != null)
            {
                string wildLocationId = $"wild_{enemy.EnemyName}_{enemy.CardId}";

                // Ensure WorldLocationSaved exists or battle will abort
                var worldSaved = MainPlayerData.Instance.WorldSaved;
                if (worldSaved.GetLocationSaved(wildLocationId) == null)
                    worldSaved.SavedLocations.Add(new WorldLocationSaved { LocationId = wildLocationId });

                var info = new GameInfo
                {
                    LocationId = wildLocationId,
                    EncounterId = foundEncounter.AssetId,
                    TurnCount = 0,
                };

                GameLoader.StartGame(info);
                EnemySpawner.Instance?.RemoveEnemy(enemy);
            }
            else
            {
                Debug.LogWarning("[Overworld] No encounters found to use as battle template!");
                GameManager.OverrideEnemyLineup = null;
            }
        }
    }

    /// <summary>
    /// Build a 3-card lineup from the roaming enemy's card data.
    /// The enemy's own card is always included as the active card;
    /// remaining slots filled with random commons from the same set.
    /// </summary>
    static List<Card> BuildEnemyLineup(RoamingEnemy enemy)
    {
        // Wild encounters are always 1v1 — just the enemy's single card
        Card enemyCard = null;
        foreach (var pair in AssetManager.Cards)
        {
            if (pair.Value.CardName == enemy.EnemyName)
            {
                enemyCard = pair.Value;
                break;
            }
        }

        if (enemyCard == null)
        {
            Debug.LogWarning($"[Overworld] Could not find Card prefab for enemy '{enemy.EnemyName}'");
            return null;
        }

        return new List<Card> { enemyCard };
    }

    void ApplyEquippedGear()
    {
        var data = MainPlayerData.Instance;
        if (data == null) return;

        // Apply weapon/armor/accessory effects to battle
        // These are read by the battle system via MainPlayerData
        if (!string.IsNullOrEmpty(data.EquippedAccessory))
        {
            // e.g., Ember Stone gives Sacred Fire at battle start
            Debug.Log($"[Overworld] Equipped accessory: {data.EquippedAccessory}");
        }
    }

    // =========================================================================
    // WORLD FEATURES SETUP
    // =========================================================================

    IEnumerator SetupWorldFeatures()
    {
        yield return null; // Wait one frame for WorldManager to initialize

        // Start gathering system
        if (GatheringManager.Instance != null)
        {
            Debug.Log("[Overworld] Gathering system active in world");
        }

        // Spawn dungeon entrances at predefined positions
        SpawnDungeonEntrances();
    }

    void SpawnDungeonEntrances()
    {
        var entrances = new (string id, Vector3 pos, Color color)[]
        {
            ("frost_cavern", new Vector3(60, 0, -40), new Color(0.4f, 0.7f, 1f)),
            ("lava_forge_trial", new Vector3(-80, 0, 70), new Color(1f, 0.5f, 0.2f)),
            ("shadow_vault", new Vector3(120, 0, 100), new Color(0.6f, 0.3f, 0.9f)),
        };

        foreach (var (id, pos, color) in entrances)
        {
            // Snap to terrain
            Vector3 spawnPos = pos;
            if (Physics.Raycast(pos + Vector3.up * 100, Vector3.down, out var hit, 200f))
                spawnPos = hit.point;

            var go = new GameObject($"DungeonEntrance_{id}");
            go.transform.position = spawnPos;
            var entrance = go.AddComponent<DungeonEntrance>();
            entrance.DungeonId = id;
            entrance.MarkerColor = color;
        }

        Debug.Log($"[Overworld] Spawned {entrances.Length} dungeon entrances");
    }

    // =========================================================================
    // FIREBASE
    // =========================================================================

    void OnFirebaseReady(string uid)
    {
        ShowNotification("Connected to the Battle of Origins!");

        // Load cloud save
        MainPlayerData.LoadFromCloud(() =>
        {
            var data = MainPlayerData.Instance;
            if (data.CharacterCreated)
                ShowNotification($"Welcome back, {data.PlayerName}!");
            else
            {
                // First time — set defaults
                data.PlayerName = "Adventurer";
                data.CharacterCreated = true;
                data.Gold = 100;
                MainPlayerData.SaveToCloud();
                ShowNotification("Welcome to the Battle of Origins!");
            }
        });
    }

    // =========================================================================
    // MMO EVENT CALLBACKS
    // =========================================================================

    void OnXPGained(ProfessionXPType type, float amount)
    {
        UpdateHUD();
        SpawnFloatingXPText(type, amount);
    }

    void OnSkillUnlocked(string professionId, SkillBox box)
    {
        ShowNotification($"Skill unlocked: {box.BoxName}!");
    }

    void OnItemCrafted(CraftedItem item)
    {
        ShowNotification($"Crafted: {item.ItemName} ({item.QualityGrade})!");
        QuestManager.Instance?.ReportItemCrafted(item.QualityGrade, false);
    }

    void OnQuestCompleted(string questTitle, int goldReward)
    {
        ShowNotification($"Quest Complete: {questTitle} — +{goldReward}g!");
    }

    void OnRegionChanged(string oldRegion, string newRegion)
    {
        ShowNotification($"Entered: {newRegion.Replace('_', ' ')}");
        QuestManager.Instance?.ReportRegionVisited();
        QuestManager.Instance?.ReportZoneVisited();
        QuestManager.Instance?.ReportMaskRegionVisit(newRegion);
    }

    void OnChatMessage(ChatMessage msg)
    {
        AddChatMessageUI(msg);
    }

    // =========================================================================
    // RUNTIME UI BUILDER
    // =========================================================================

    void BuildOverlayUI()
    {
        // Create overlay canvas that persists across scenes
        var canvasGO = new GameObject("OverworldHUD");
        canvasGO.transform.SetParent(transform);
        _overlayCanvas = canvasGO.AddComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 100; // On top of everything
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        // === TOP BAR (Gold + XP) ===
        var topBar = CreatePanel(canvasGO.transform, "TopBar", new Color(0, 0, 0, 0.6f));
        var topRect = topBar.GetComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0, 1);
        topRect.anchorMax = new Vector2(1, 1);
        topRect.pivot = new Vector2(0.5f, 1);
        topRect.sizeDelta = new Vector2(0, 76);
        var topLayout = topBar.AddComponent<HorizontalLayoutGroup>();
        topLayout.padding = new RectOffset(15, 15, 5, 5);
        topLayout.spacing = 20;
        topLayout.childAlignment = TextAnchor.MiddleLeft;

        _goldText = CreateText(topBar.transform, "GoldText", "Gold: 100", 18, Color.yellow);

        // === SWG-STYLE SKILL BARS ===
        _skillBarPanel = new GameObject("SkillBars", typeof(RectTransform));
        _skillBarPanel.transform.SetParent(topBar.transform, false);
        var sbLayout = _skillBarPanel.AddComponent<VerticalLayoutGroup>();
        sbLayout.spacing = 1;
        sbLayout.childForceExpandWidth = true;
        sbLayout.childForceExpandHeight = false;
        var sbLE = _skillBarPanel.AddComponent<LayoutElement>();
        sbLE.flexibleWidth = 1;
        sbLE.preferredHeight = 66;

        (_combatFill, _combatLabel) = CreateSkillBar(_skillBarPanel.transform, "Combat", new Color(0.9f, 0.25f, 0.2f));
        (_craftFill, _craftLabel) = CreateSkillBar(_skillBarPanel.transform, "Craft", new Color(0.8f, 0.55f, 0.15f));
        (_exploreFill, _exploreLabel) = CreateSkillBar(_skillBarPanel.transform, "Explore", new Color(0.2f, 0.7f, 0.4f));

        // Spacer
        var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(topBar.transform, false);
        spacer.GetComponent<LayoutElement>().flexibleWidth = 0;

        // Menu button
        var menuBtn = CreateButton(topBar.transform, "MenuBtn", "SKILLS", new Color(0.2f, 0.5f, 0.8f), () => ToggleProfessionPanel());
        var craftBtn = CreateButton(topBar.transform, "CraftBtn", "CRAFT", new Color(0.6f, 0.4f, 0.2f), () => ToggleCraftingPanel());
        var chatBtn = CreateButton(topBar.transform, "ChatBtn", "CHAT", new Color(0.3f, 0.6f, 0.3f), () => ToggleChatPanel());
        var marketBtn = CreateButton(topBar.transform, "MarketBtn", "MARKET", new Color(0.7f, 0.5f, 0.2f), () => ToggleMarketPanel());
        var partyBtn = CreateButton(topBar.transform, "PartyBtn", "PARTY", new Color(0.4f, 0.3f, 0.7f), () => TogglePartyPanel());
        var guildBtn = CreateButton(topBar.transform, "GuildBtn", "GUILD", new Color(0.6f, 0.3f, 0.3f), () => ToggleGuildPanel());

        // === TEAM HP BARS (top left, below top bar) ===
        BuildTeamHPPanel(canvasGO.transform);

        // === PROGRESS TRACKER (below team HP) ===
        BuildProgressPanel(canvasGO.transform);

        // === NOTIFICATION TEXT (center screen) ===
        _notificationText = CreateText(canvasGO.transform, "Notification", "", 24, Color.white);
        var notifRect = _notificationText.GetComponent<RectTransform>();
        notifRect.anchorMin = new Vector2(0.5f, 0.7f);
        notifRect.anchorMax = new Vector2(0.5f, 0.7f);
        notifRect.sizeDelta = new Vector2(700, 50);
        _notificationText.alignment = TextAlignmentOptions.Center;
        _notificationText.enableWordWrapping = true;
        _notificationText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        _notificationText.gameObject.SetActive(false);

        // === CHAT PANEL (bottom left) ===
        BuildChatPanel(canvasGO.transform);

        // === PROFESSION PANEL (hidden) ===
        BuildProfessionPanel(canvasGO.transform);

        // === CRAFTING PANEL (hidden) ===
        BuildCraftingPanel(canvasGO.transform);

        // === MARKET PANEL (hidden) ===
        BuildMarketPanel(canvasGO.transform);

        // === PARTY PANEL (hidden) ===
        BuildPartyPanel(canvasGO.transform);

        // === GUILD PANEL (hidden) ===
        BuildGuildPanel(canvasGO.transform);

        Debug.Log("[Overworld] HUD built: top bar, chat, profession tree, crafting, market, party, guild");
    }

    void BuildChatPanel(Transform parent)
    {
        _chatPanel = CreatePanel(parent, "ChatPanel", new Color(0, 0, 0, 0.7f));
        var rect = _chatPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(0.35f, 0.35f);
        rect.offsetMin = new Vector2(10, 50);
        rect.offsetMax = new Vector2(-5, -5);

        // Scroll area for messages
        var scrollGO = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollGO.transform.SetParent(_chatPanel.transform, false);
        scrollGO.GetComponent<Image>().color = new Color(0, 0, 0, 0.3f);
        var scrollRect = scrollGO.GetComponent<RectTransform>();
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = new Vector2(1, 1);
        scrollRect.offsetMin = new Vector2(5, 35);
        scrollRect.offsetMax = new Vector2(-5, -5);
        _chatScroll = scrollGO.GetComponent<ScrollRect>();

        // Content
        var contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGO.transform.SetParent(scrollGO.transform, false);
        _chatMessageParent = contentGO.transform;
        var contentRect = contentGO.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 0);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0, 0);
        var vlg = contentGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(3, 3, 3, 3);
        vlg.spacing = 2;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var csf = contentGO.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _chatScroll.content = contentRect;
        _chatScroll.verticalScrollbar = null;

        // Input field
        var inputGO = new GameObject("ChatInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputGO.transform.SetParent(_chatPanel.transform, false);
        inputGO.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.2f);
        var inputRect = inputGO.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0, 0);
        inputRect.anchorMax = new Vector2(1, 0);
        inputRect.offsetMin = new Vector2(5, 5);
        inputRect.offsetMax = new Vector2(-5, 30);

        // Input text area
        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(inputGO.transform, false);
        var textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(5, 0);
        textAreaRect.offsetMax = new Vector2(-5, 0);

        var inputTextGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        inputTextGO.transform.SetParent(textArea.transform, false);
        var inputText = inputTextGO.GetComponent<TextMeshProUGUI>();
        inputText.fontSize = 16;
        inputText.color = Color.white;
        var inputTextRect = inputTextGO.GetComponent<RectTransform>();
        inputTextRect.anchorMin = Vector2.zero;
        inputTextRect.anchorMax = Vector2.one;
        inputTextRect.offsetMin = Vector2.zero;
        inputTextRect.offsetMax = Vector2.zero;

        var placeholderGO = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        placeholderGO.transform.SetParent(textArea.transform, false);
        var placeholder = placeholderGO.GetComponent<TextMeshProUGUI>();
        placeholder.text = "Press Enter to chat...";
        placeholder.fontSize = 16;
        placeholder.color = new Color(0.5f, 0.5f, 0.5f);
        var phRect = placeholderGO.GetComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = Vector2.zero;
        phRect.offsetMax = Vector2.zero;

        _chatInput = inputGO.GetComponent<TMP_InputField>();
        _chatInput.textViewport = textAreaRect;
        _chatInput.textComponent = inputText;
        _chatInput.placeholder = placeholder;
        _chatInput.onSubmit.AddListener(OnChatSubmit);

        _chatPanel.SetActive(true); // Chat starts visible
    }

    void BuildProfessionPanel(Transform parent)
    {
        _professionPanel = CreatePanel(parent, "ProfessionPanel", new Color(0.05f, 0.05f, 0.1f, 0.95f));
        var rect = _professionPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.05f, 0.05f);
        rect.anchorMax = new Vector2(0.95f, 0.95f);

        // Title
        var title = CreateText(_professionPanel.transform, "Title", "PROFESSION SKILLS", 28, Color.white);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(0, 50);
        title.alignment = TextAlignmentOptions.Center;

        // Points counter
        var points = CreateText(_professionPanel.transform, "Points", "0 / 80 Skill Points", 18, new Color(1, 0.85f, 0.3f));
        var pointsRect = points.GetComponent<RectTransform>();
        pointsRect.anchorMin = new Vector2(0, 1);
        pointsRect.anchorMax = new Vector2(1, 1);
        pointsRect.pivot = new Vector2(0.5f, 1);
        pointsRect.anchoredPosition = new Vector2(0, -55);
        pointsRect.sizeDelta = new Vector2(0, 30);
        points.alignment = TextAlignmentOptions.Center;

        // XP bars
        var xpArea = CreateText(_professionPanel.transform, "XPBars",
            "Combat: 0 | Exploration: 0 | Crafting: 0 | Trade: 0 | Charisma: 0",
            14, new Color(0.7f, 0.8f, 1f));
        var xpRect = xpArea.GetComponent<RectTransform>();
        xpRect.anchorMin = new Vector2(0, 1);
        xpRect.anchorMax = new Vector2(1, 1);
        xpRect.pivot = new Vector2(0.5f, 1);
        xpRect.anchoredPosition = new Vector2(0, -85);
        xpRect.sizeDelta = new Vector2(0, 25);
        xpArea.alignment = TextAlignmentOptions.Center;

        // Scrollable profession list
        var scrollGO = new GameObject("ProfScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollGO.transform.SetParent(_professionPanel.transform, false);
        scrollGO.GetComponent<Image>().color = new Color(0, 0, 0, 0.3f);
        var scrollRt = scrollGO.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0.02f, 0.02f);
        scrollRt.anchorMax = new Vector2(0.98f, 1f);
        scrollRt.offsetMin = new Vector2(10, 10);
        scrollRt.offsetMax = new Vector2(-10, -115);

        var mask = scrollGO.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        var contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGO.transform.SetParent(scrollGO.transform, false);
        var contentRect = contentGO.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 0);

        var vlg = contentGO.GetComponent<VerticalLayoutGroup>();
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4;
        vlg.padding = new RectOffset(8, 8, 8, 8);

        var csf = contentGO.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _professionScroll = scrollGO.GetComponent<ScrollRect>();
        _professionScroll.content = contentRect;
        _professionScroll.horizontal = false;
        _professionScroll.vertical = true;
        _professionScroll.movementType = ScrollRect.MovementType.Clamped;
        _professionScrollContent = contentGO.transform;

        // Close button
        var closeBtn = CreateButton(_professionPanel.transform, "CloseBtn", "X", Color.red, () => _professionPanel.SetActive(false));
        var closeBtnRect = closeBtn.GetComponent<RectTransform>();
        closeBtnRect.anchorMin = new Vector2(1, 1);
        closeBtnRect.anchorMax = new Vector2(1, 1);
        closeBtnRect.pivot = new Vector2(1, 1);
        closeBtnRect.anchoredPosition = new Vector2(-10, -10);
        closeBtnRect.sizeDelta = new Vector2(40, 40);

        _professionPanel.SetActive(false);
    }

    void BuildCraftingPanel(Transform parent)
    {
        // Load schematics from Resources
        _allSchematics = Resources.LoadAll<SchematicData>("Data/Schematics");

        _craftingPanel = CreatePanel(parent, "CraftingPanel", new Color(0.08f, 0.05f, 0.02f, 0.95f));
        var rect = _craftingPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.05f, 0.05f);
        rect.anchorMax = new Vector2(0.95f, 0.95f);

        // Title
        var title = CreateText(_craftingPanel.transform, "Title", "CRAFTING WORKSHOP", 28, new Color(1, 0.8f, 0.4f));
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(0, 50);
        title.alignment = TextAlignmentOptions.Center;

        // Info bar
        var info = CreateText(_craftingPanel.transform, "Info",
            "Essences: 0 | Crafted Items: 0", 15, Color.white);
        var infoRect = info.GetComponent<RectTransform>();
        infoRect.anchorMin = new Vector2(0, 1);
        infoRect.anchorMax = new Vector2(1, 1);
        infoRect.pivot = new Vector2(0.5f, 1);
        infoRect.anchoredPosition = new Vector2(0, -52);
        infoRect.sizeDelta = new Vector2(0, 22);
        info.alignment = TextAlignmentOptions.Center;

        // --- Left column: Essences (0..0.32) ---
        var essLabel = CreateText(_craftingPanel.transform, "EssLabel", "YOUR ESSENCES", 14, new Color(0.6f, 0.9f, 1f));
        var essLabelRect = essLabel.GetComponent<RectTransform>();
        essLabelRect.anchorMin = new Vector2(0.01f, 1);
        essLabelRect.anchorMax = new Vector2(0.32f, 1);
        essLabelRect.pivot = new Vector2(0, 1);
        essLabelRect.anchoredPosition = new Vector2(8, -78);
        essLabelRect.sizeDelta = new Vector2(0, 20);

        _craftEssenceScroll = BuildScrollColumn(_craftingPanel.transform, "EssScroll",
            new Vector2(0.01f, 0.02f), new Vector2(0.32f, 1f), new Vector2(8, 10), new Vector2(-4, -100),
            out _craftEssenceContent);

        // --- Middle column: Schematics (0.33..0.65) ---
        var schLabel = CreateText(_craftingPanel.transform, "SchLabel", "SCHEMATICS", 14, new Color(1f, 0.85f, 0.5f));
        var schLabelRect = schLabel.GetComponent<RectTransform>();
        schLabelRect.anchorMin = new Vector2(0.33f, 1);
        schLabelRect.anchorMax = new Vector2(0.65f, 1);
        schLabelRect.pivot = new Vector2(0, 1);
        schLabelRect.anchoredPosition = new Vector2(4, -78);
        schLabelRect.sizeDelta = new Vector2(0, 20);

        _craftSchematicScroll = BuildScrollColumn(_craftingPanel.transform, "SchScroll",
            new Vector2(0.33f, 0.02f), new Vector2(0.65f, 1f), new Vector2(4, 10), new Vector2(-4, -100),
            out _craftSchematicContent);

        // --- Right column: Crafted Inventory + Result (0.66..0.99) ---
        var invLabel = CreateText(_craftingPanel.transform, "InvLabel", "CRAFTED ITEMS", 14, new Color(0.5f, 1f, 0.6f));
        var invLabelRect = invLabel.GetComponent<RectTransform>();
        invLabelRect.anchorMin = new Vector2(0.66f, 1);
        invLabelRect.anchorMax = new Vector2(0.99f, 1);
        invLabelRect.pivot = new Vector2(0, 1);
        invLabelRect.anchoredPosition = new Vector2(4, -78);
        invLabelRect.sizeDelta = new Vector2(0, 20);

        _craftInventoryScroll = BuildScrollColumn(_craftingPanel.transform, "InvScroll",
            new Vector2(0.66f, 0.22f), new Vector2(0.99f, 1f), new Vector2(4, 10), new Vector2(-8, -100),
            out _craftInventoryContent);

        // Result area at bottom-right
        var resultGO = CreatePanel(_craftingPanel.transform, "ResultArea", new Color(0.1f, 0.08f, 0.04f, 0.9f));
        var resultRect = resultGO.GetComponent<RectTransform>();
        resultRect.anchorMin = new Vector2(0.66f, 0.02f);
        resultRect.anchorMax = new Vector2(0.99f, 0.21f);
        resultRect.offsetMin = new Vector2(4, 10);
        resultRect.offsetMax = new Vector2(-8, -4);
        _craftResultArea = resultGO.transform;

        var resultText = CreateText(_craftResultArea, "ResultText", "Select a schematic and essences, then craft.", 12, new Color(0.7f, 0.7f, 0.7f));
        var rtRect = resultText.GetComponent<RectTransform>();
        rtRect.anchorMin = Vector2.zero;
        rtRect.anchorMax = Vector2.one;
        rtRect.offsetMin = new Vector2(6, 4);
        rtRect.offsetMax = new Vector2(-6, -4);
        resultText.alignment = TextAlignmentOptions.TopLeft;
        resultText.enableWordWrapping = true;

        // Close button
        var closeBtn = CreateButton(_craftingPanel.transform, "CloseBtn", "X", Color.red, () =>
        {
            CraftingManager.Instance?.CancelCraft();
            _selectedEssences.Clear();
            _selectedSchematic = null;
            _craftingPanel.SetActive(false);
        });
        var closeBtnRect = closeBtn.GetComponent<RectTransform>();
        closeBtnRect.anchorMin = new Vector2(1, 1);
        closeBtnRect.anchorMax = new Vector2(1, 1);
        closeBtnRect.pivot = new Vector2(1, 1);
        closeBtnRect.anchoredPosition = new Vector2(-10, -10);
        closeBtnRect.sizeDelta = new Vector2(40, 40);

        _craftingPanel.SetActive(false);
    }

    /// <summary>Helper: build a scroll column with VerticalLayoutGroup content.</summary>
    ScrollRect BuildScrollColumn(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
        out Transform contentTransform)
    {
        var scrollGO = new GameObject(name, typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollGO.transform.SetParent(parent, false);
        scrollGO.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);
        var srt = scrollGO.GetComponent<RectTransform>();
        srt.anchorMin = anchorMin;
        srt.anchorMax = anchorMax;
        srt.offsetMin = offsetMin;
        srt.offsetMax = offsetMax;

        scrollGO.AddComponent<Mask>().showMaskGraphic = true;

        var contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGO.transform.SetParent(scrollGO.transform, false);
        var crt = contentGO.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1);
        crt.sizeDelta = new Vector2(0, 0);

        var vlg = contentGO.GetComponent<VerticalLayoutGroup>();
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 3;
        vlg.padding = new RectOffset(4, 4, 4, 4);

        contentGO.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var sr = scrollGO.GetComponent<ScrollRect>();
        sr.content = crt;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;

        contentTransform = contentGO.transform;
        return sr;
    }

    // =========================================================================
    // UI INTERACTIONS
    // =========================================================================

    void ToggleProfessionPanel()
    {
        bool show = !_professionPanel.activeSelf;
        _professionPanel.SetActive(show);
        _craftingPanel.SetActive(false);
        _marketPanel.SetActive(false);
        _partyPanel.SetActive(false);
        _guildPanel.SetActive(false);
        if (show) RefreshProfessionPanel();
    }

    void ToggleCraftingPanel()
    {
        bool show = !_craftingPanel.activeSelf;
        _craftingPanel.SetActive(show);
        _professionPanel.SetActive(false);
        _marketPanel.SetActive(false);
        _partyPanel.SetActive(false);
        _guildPanel.SetActive(false);
        if (show)
        {
            _selectedEssences.Clear();
            _selectedSchematic = null;
            CraftingManager.Instance?.CancelCraft();
            RefreshCraftingPanel();
        }
    }

    void ToggleChatPanel()
    {
        _chatPanel.SetActive(!_chatPanel.activeSelf);
    }

    void ToggleMarketPanel()
    {
        bool show = !_marketPanel.activeSelf;
        _marketPanel.SetActive(show);
        _craftingPanel.SetActive(false);
        _professionPanel.SetActive(false);
        _partyPanel.SetActive(false);
        _guildPanel.SetActive(false);
        if (show) RefreshMarketPanel();
    }

    void TogglePartyPanel()
    {
        bool show = !_partyPanel.activeSelf;
        _partyPanel.SetActive(show);
        _craftingPanel.SetActive(false);
        _professionPanel.SetActive(false);
        _marketPanel.SetActive(false);
        _guildPanel.SetActive(false);
        if (show) RefreshPartyPanel();
    }

    void ToggleGuildPanel()
    {
        bool show = !_guildPanel.activeSelf;
        _guildPanel.SetActive(show);
        _craftingPanel.SetActive(false);
        _professionPanel.SetActive(false);
        _marketPanel.SetActive(false);
        _partyPanel.SetActive(false);
        if (show) RefreshGuildPanel();
    }

    // =========================================================================
    // PROFESSION PANEL REFRESH
    // =========================================================================

    void RefreshProfessionPanel()
    {
        var data = MainPlayerData.Instance;
        if (data == null) return;

        // Update header texts
        var pointsText = _professionPanel.transform.Find("Points")?.GetComponent<TextMeshProUGUI>();
        if (pointsText != null)
            pointsText.text = $"{data.SkillPointsUsed} / {MainPlayerData.SkillPointsCap} Skill Points Used";

        var xpText = _professionPanel.transform.Find("XPBars")?.GetComponent<TextMeshProUGUI>();
        if (xpText != null)
            xpText.text = $"Combat: {data.GetProfessionXP("combat"):F0} | Exploration: {data.GetProfessionXP("exploration"):F0} | Crafting: {data.GetProfessionXP("crafting"):F0} | Trade: {data.GetProfessionXP("trade"):F0} | Charisma: {data.GetProfessionXP("charisma"):F0}";

        // Clear and rebuild skill tree list
        if (_professionScrollContent == null) return;
        for (int i = _professionScrollContent.childCount - 1; i >= 0; i--)
            Destroy(_professionScrollContent.GetChild(i).gameObject);

        var profMgr = ProfessionManager.Instance;
        if (profMgr == null || profMgr.AllProfessions.Count == 0) return;

        // Sort by tier then name
        var sorted = profMgr.AllProfessions.OrderBy(p => p.Tier).ThenBy(p => p.ProfessionName).ToList();

        foreach (var prof in sorted)
        {
            // Profession header row
            string xpType = prof.XPType.ToString();
            float currentXP = data.GetProfessionXP(xpType.ToLower());
            string prereqNote = "";
            if (!string.IsNullOrEmpty(prof.RequiresSkillId) && !data.HasSkill(prof.RequiresSkillId))
                prereqNote = "  [LOCKED - prerequisite not met]";

            Color profColor = prof.TreeColor != Color.white ? prof.TreeColor : TierColor(prof.Tier);

            var headerGO = CreatePanel(_professionScrollContent, $"Prof_{prof.ProfessionId}",
                new Color(profColor.r * 0.3f, profColor.g * 0.3f, profColor.b * 0.3f, 0.8f));
            headerGO.AddComponent<LayoutElement>().preferredHeight = 32;
            var headerText = CreateText(headerGO.transform, "Name",
                $"{prof.ProfessionName}  (Tier {prof.Tier} - {xpType} XP: {currentXP:F0}){prereqNote}",
                15, profColor);
            var htRect = headerText.GetComponent<RectTransform>();
            htRect.anchorMin = Vector2.zero;
            htRect.anchorMax = Vector2.one;
            htRect.offsetMin = new Vector2(10, 0);
            htRect.offsetMax = new Vector2(-10, 0);
            headerText.fontStyle = FontStyles.Bold;

            // Skill boxes
            foreach (var box in prof.Boxes)
            {
                bool unlocked = data.HasSkill(box.BoxId);
                bool canUnlock = !unlocked && profMgr.CanUnlock(box.BoxId);

                Color bgColor, textColor;
                string statusTag;
                if (unlocked)
                {
                    bgColor = new Color(0.1f, 0.35f, 0.1f, 0.85f);
                    textColor = new Color(0.5f, 1f, 0.5f);
                    statusTag = "[UNLOCKED]";
                }
                else if (canUnlock)
                {
                    bgColor = new Color(0.35f, 0.3f, 0.05f, 0.85f);
                    textColor = new Color(1f, 0.95f, 0.4f);
                    statusTag = "[AVAILABLE]";
                }
                else
                {
                    bgColor = new Color(0.15f, 0.15f, 0.15f, 0.7f);
                    textColor = new Color(0.5f, 0.5f, 0.5f);
                    statusTag = "[LOCKED]";
                }

                var boxGO = CreatePanel(_professionScrollContent, $"Box_{box.BoxId}", bgColor);
                var boxLE = boxGO.AddComponent<LayoutElement>();
                boxLE.preferredHeight = canUnlock ? 60 : 48;

                // Skill info text
                string desc = string.IsNullOrEmpty(box.Description) ? "" : $"\n  {box.Description}";
                var boxText = CreateText(boxGO.transform, "Info",
                    $"  {box.BoxName}  {statusTag}   Cost: {box.SkillPointCost} pts | XP Req: {box.XPRequired}{desc}",
                    12, textColor);
                var btRect = boxText.GetComponent<RectTransform>();
                btRect.anchorMin = Vector2.zero;
                btRect.anchorMax = new Vector2(canUnlock ? 0.75f : 1f, 1f);
                btRect.offsetMin = new Vector2(20, 2);
                btRect.offsetMax = new Vector2(-4, -2);
                boxText.enableWordWrapping = true;

                // UNLOCK button for available skills
                if (canUnlock)
                {
                    string capturedId = box.BoxId;
                    var unlockBtn = CreateButton(boxGO.transform, "UnlockBtn", "UNLOCK",
                        new Color(0.2f, 0.6f, 0.2f), () =>
                        {
                            if (ProfessionManager.Instance != null && ProfessionManager.Instance.UnlockSkill(capturedId))
                            {
                                ShowNotification($"Skill unlocked: {capturedId}");
                                RefreshProfessionPanel();
                            }
                            else
                            {
                                ShowNotification("Cannot unlock this skill.");
                            }
                        });
                    var ubRect = unlockBtn.GetComponent<RectTransform>();
                    ubRect.anchorMin = new Vector2(0.78f, 0.15f);
                    ubRect.anchorMax = new Vector2(0.98f, 0.85f);
                    ubRect.offsetMin = Vector2.zero;
                    ubRect.offsetMax = Vector2.zero;
                    var ubLE = unlockBtn.GetComponent<LayoutElement>();
                    ubLE.preferredWidth = -1;
                    ubLE.preferredHeight = -1;
                    ubLE.ignoreLayout = true;
                }
            }

            // Spacer between professions
            var spacer = new GameObject("Spacer", typeof(RectTransform));
            spacer.transform.SetParent(_professionScrollContent, false);
            spacer.AddComponent<LayoutElement>().preferredHeight = 6;
        }
    }

    static Color TierColor(int tier)
    {
        return tier switch
        {
            1 => new Color(0.6f, 0.8f, 1f),
            2 => new Color(1f, 0.7f, 0.3f),
            3 => new Color(1f, 0.4f, 0.4f),
            _ => Color.white
        };
    }

    // =========================================================================
    // CRAFTING PANEL REFRESH
    // =========================================================================

    void RefreshCraftingPanel()
    {
        var data = MainPlayerData.Instance;
        if (data == null) return;

        // Update info bar
        var info = _craftingPanel.transform.Find("Info")?.GetComponent<TextMeshProUGUI>();
        if (info != null)
            info.text = $"Essences: {data.Essences.Count} | Crafted Items: {data.CraftedItems.Count}";

        RefreshEssenceList(data);
        RefreshSchematicList(data);
        RefreshCraftedInventory(data);
    }

    void RefreshEssenceList(MainPlayerData data)
    {
        if (_craftEssenceContent == null) return;
        ClearChildren(_craftEssenceContent);

        if (data.Essences.Count == 0)
        {
            var empty = CreateText(_craftEssenceContent, "Empty", "No essences yet.\nDefeat spirits to collect essences.", 12, new Color(0.5f, 0.5f, 0.5f));
            empty.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 40);
            empty.enableWordWrapping = true;
            var le = empty.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 40;
            return;
        }

        // Sort by total stats descending
        var sorted = data.Essences.OrderByDescending(e => e.TotalStats).ToList();
        foreach (var essence in sorted)
        {
            bool isSelected = _selectedEssences.Contains(essence);
            Color bg = isSelected
                ? new Color(0.2f, 0.4f, 0.6f, 0.9f)
                : RarityBgColor(essence.Rarity);

            var row = CreatePanel(_craftEssenceContent, $"Ess_{essence.Id}", bg);
            row.AddComponent<LayoutElement>().preferredHeight = 52;

            Color textCol = isSelected ? Color.white : RarityTextColor(essence.Rarity);
            string selMark = isSelected ? " [SELECTED]" : "";
            var txt = CreateText(row.transform, "Info",
                $"{essence.Name}{selMark}\nP:{essence.Potency} S:{essence.Stability} R:{essence.Resonance} Pu:{essence.Purity}  ({essence.Rarity})",
                11, textCol);
            var tRect = txt.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = new Vector2(0.7f, 1f);
            tRect.offsetMin = new Vector2(6, 2);
            tRect.offsetMax = new Vector2(-2, -2);
            txt.enableWordWrapping = true;

            // Select/Deselect button
            var capturedEssence = essence;
            string btnLabel = isSelected ? "REMOVE" : "SELECT";
            Color btnColor = isSelected ? new Color(0.5f, 0.2f, 0.2f) : new Color(0.2f, 0.4f, 0.6f);
            var btn = CreateButton(row.transform, "SelectBtn", btnLabel, btnColor, () =>
            {
                if (_selectedEssences.Contains(capturedEssence))
                    _selectedEssences.Remove(capturedEssence);
                else
                    _selectedEssences.Add(capturedEssence);
                RefreshCraftingPanel();
            });
            var bRect = btn.GetComponent<RectTransform>();
            bRect.anchorMin = new Vector2(0.72f, 0.15f);
            bRect.anchorMax = new Vector2(0.98f, 0.85f);
            bRect.offsetMin = Vector2.zero;
            bRect.offsetMax = Vector2.zero;
            btn.GetComponent<LayoutElement>().ignoreLayout = true;
        }
    }

    void RefreshSchematicList(MainPlayerData data)
    {
        if (_craftSchematicContent == null) return;
        ClearChildren(_craftSchematicContent);

        var schematics = _allSchematics;
        if (schematics == null || schematics.Length == 0)
        {
            var empty = CreateText(_craftSchematicContent, "Empty", "No schematics loaded.\nPlace SchematicData assets in\nResources/Data/Schematics/", 12, new Color(0.5f, 0.5f, 0.5f));
            empty.enableWordWrapping = true;
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
            return;
        }

        // Sort by tier then name
        var sorted = schematics.OrderBy(s => s.Tier).ThenBy(s => s.SchematicName).ToArray();
        foreach (var sch in sorted)
        {
            bool hasSkill = string.IsNullOrEmpty(sch.RequiredSkillId) || data.HasSkill(sch.RequiredSkillId);
            bool isSelected = _selectedSchematic == sch;
            bool canCraft = hasSkill && _selectedEssences.Count >= sch.RequiredEssences;

            Color bg;
            if (isSelected) bg = new Color(0.4f, 0.3f, 0.1f, 0.9f);
            else if (!hasSkill) bg = new Color(0.15f, 0.1f, 0.1f, 0.6f);
            else bg = new Color(0.12f, 0.1f, 0.06f, 0.7f);

            var row = CreatePanel(_craftSchematicContent, $"Sch_{sch.SchematicId}", bg);
            int rowH = isSelected ? 80 : 46;
            row.AddComponent<LayoutElement>().preferredHeight = rowH;

            Color textCol = hasSkill ? new Color(1f, 0.9f, 0.6f) : new Color(0.4f, 0.35f, 0.3f);
            string lockTag = hasSkill ? "" : $" [Requires: {sch.RequiredSkillId}]";
            string selTag = isSelected ? " >>> " : "";
            string essReq = $"Essences: {_selectedEssences.Count}/{sch.RequiredEssences}";

            string label = $"{selTag}{sch.SchematicName} ({sch.Tier}){lockTag}\n{sch.Category} -> {sch.OutputType}  |  {essReq}";
            if (isSelected && !string.IsNullOrEmpty(sch.OutputDescription))
                label += $"\n{sch.OutputDescription}";

            var txt = CreateText(row.transform, "Info", label, 11, textCol);
            var tRect = txt.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = new Vector2(isSelected && canCraft ? 0.7f : 1f, 1f);
            tRect.offsetMin = new Vector2(6, 2);
            tRect.offsetMax = new Vector2(-4, -2);
            txt.enableWordWrapping = true;

            // Click to select schematic
            var capturedSch = sch;
            var selectBtn = row.AddComponent<Button>();
            selectBtn.onClick.AddListener(() =>
            {
                _selectedSchematic = (_selectedSchematic == capturedSch) ? null : capturedSch;
                RefreshCraftingPanel();
            });

            // CRAFT button when selected and craftable
            if (isSelected && canCraft)
            {
                var craftBtn = CreateButton(row.transform, "CraftBtn", "CRAFT", new Color(0.6f, 0.4f, 0.1f), () =>
                {
                    PerformFullCraft(capturedSch);
                });
                var cbRect = craftBtn.GetComponent<RectTransform>();
                cbRect.anchorMin = new Vector2(0.72f, 0.1f);
                cbRect.anchorMax = new Vector2(0.98f, 0.9f);
                cbRect.offsetMin = Vector2.zero;
                cbRect.offsetMax = Vector2.zero;
                craftBtn.GetComponent<LayoutElement>().ignoreLayout = true;
            }
        }
    }

    void RefreshCraftedInventory(MainPlayerData data)
    {
        if (_craftInventoryContent == null) return;
        ClearChildren(_craftInventoryContent);

        if (data.CraftedItems.Count == 0)
        {
            var empty = CreateText(_craftInventoryContent, "Empty", "No crafted items yet.", 12, new Color(0.5f, 0.5f, 0.5f));
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;
            return;
        }

        // Show newest first
        var sorted = data.CraftedItems.OrderByDescending(c => c.CraftedAt).ToList();
        foreach (var item in sorted)
        {
            Color bg = QualityBgColor(item.QualityGrade);
            var row = CreatePanel(_craftInventoryContent, $"Item_{item.Id}", bg);
            row.AddComponent<LayoutElement>().preferredHeight = 56;

            string statsLine = "";
            if (item.Stats != null && item.Stats.Count > 0)
                statsLine = string.Join(" ", item.Stats.Select(kv => $"{kv.Key[..1].ToUpper()}:{kv.Value:F0}"));

            Color textCol = QualityTextColor(item.QualityGrade);
            var txt = CreateText(row.transform, "Info",
                $"{item.ItemName}\nSlot: {item.Slot} | Power: {item.BasePower} | {statsLine}\nCrafter: {item.CrafterName} | Serial: {item.Serial}",
                10, textCol);
            var tRect = txt.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = new Vector2(6, 2);
            tRect.offsetMax = new Vector2(-6, -2);
            txt.enableWordWrapping = true;
        }
    }

    /// <summary>Run the full craft pipeline: start -> add essences -> assemble -> finish.</summary>
    void PerformFullCraft(SchematicData schematic)
    {
        var cm = CraftingManager.Instance;
        if (cm == null) { ShowNotification("CraftingManager not found."); return; }

        // Start session
        if (!cm.StartCrafting(schematic))
        {
            ShowNotification("Cannot start crafting - check requirements.");
            return;
        }

        // Add selected essences
        int needed = schematic.RequiredEssences;
        int added = 0;
        foreach (var ess in _selectedEssences)
        {
            if (added >= needed) break;
            if (cm.AddEssence(ess)) added++;
        }

        if (added < needed)
        {
            cm.CancelCraft();
            ShowNotification($"Need {needed} essences, only {added} accepted.");
            return;
        }

        // Assembly
        var assemblyResult = cm.PerformAssembly();
        if (assemblyResult == null)
        {
            cm.CancelCraft();
            ShowNotification("Assembly failed.");
            return;
        }

        // Auto-allocate experiment points evenly across stats
        var statNames = assemblyResult.BaseStats.Keys.ToList();
        int pts = assemblyResult.ExperimentPoints;
        int idx = 0;
        while (pts > 0)
        {
            cm.AllocateExperimentPoint(statNames[idx % statNames.Count]);
            pts--;
            idx++;
        }

        // Finish
        var item = cm.FinishCraft();
        if (item == null)
        {
            ShowNotification("Craft finishing failed.");
            return;
        }

        // Show result
        _selectedEssences.Clear();
        _selectedSchematic = null;
        ShowCraftResult(item, assemblyResult);
        RefreshCraftingPanel();
    }

    void ShowCraftResult(CraftedItem item, AssemblyResult assembly)
    {
        if (_craftResultArea == null) return;
        ClearChildren(_craftResultArea);

        string statsLine = item.Stats != null && item.Stats.Count > 0
            ? string.Join("  ", item.Stats.Select(kv => $"{kv.Key}: {kv.Value:F0}"))
            : "";

        Color gradeColor = QualityTextColor(item.QualityGrade);
        var txt = CreateText(_craftResultArea, "Result",
            $"CRAFTED: {item.ItemName}\n" +
            $"Grade: {item.QualityGrade} ({item.QualityScore:F0}) | Power: {item.BasePower}\n" +
            $"Stats: {statsLine}\n" +
            $"Slot: {item.Slot} | Assembly: {assembly.Score:F0}\n" +
            $"Crafter: {item.CrafterName} | {item.Serial}",
            11, gradeColor);
        var tRect = txt.GetComponent<RectTransform>();
        tRect.anchorMin = Vector2.zero;
        tRect.anchorMax = Vector2.one;
        tRect.offsetMin = new Vector2(6, 4);
        tRect.offsetMax = new Vector2(-6, -4);
        txt.enableWordWrapping = true;
        txt.alignment = TextAlignmentOptions.TopLeft;

        ShowNotification($"Crafted: {item.ItemName} ({item.QualityGrade})!");
    }

    // =========================================================================
    // MARKET PANEL BUILD & REFRESH (Session 7)
    // =========================================================================

    void BuildMarketPanel(Transform parent)
    {
        _marketPanel = CreatePanel(parent, "MarketPanel", new Color(0.06f, 0.04f, 0.02f, 0.95f));
        var rect = _marketPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.05f, 0.05f);
        rect.anchorMax = new Vector2(0.95f, 0.95f);

        // Title
        var title = CreateText(_marketPanel.transform, "Title", "PLAYER MARKET", 28, new Color(1f, 0.75f, 0.3f));
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(0, 50);
        title.alignment = TextAlignmentOptions.Center;

        // --- Left column: FOR SALE (0..0.55) ---
        var saleLabel = CreateText(_marketPanel.transform, "SaleLabel", "FOR SALE", 14, new Color(0.6f, 0.9f, 1f));
        var saleLabelRect = saleLabel.GetComponent<RectTransform>();
        saleLabelRect.anchorMin = new Vector2(0.01f, 1);
        saleLabelRect.anchorMax = new Vector2(0.55f, 1);
        saleLabelRect.pivot = new Vector2(0, 1);
        saleLabelRect.anchoredPosition = new Vector2(8, -55);
        saleLabelRect.sizeDelta = new Vector2(0, 20);

        _marketForSaleScroll = BuildScrollColumn(_marketPanel.transform, "ForSaleScroll",
            new Vector2(0.01f, 0.02f), new Vector2(0.55f, 1f), new Vector2(8, 10), new Vector2(-4, -80),
            out _marketForSaleContent);

        // --- Right column: MY LISTINGS (0.56..0.99) ---
        var myLabel = CreateText(_marketPanel.transform, "MyLabel", "MY LISTINGS", 14, new Color(1f, 0.85f, 0.5f));
        var myLabelRect = myLabel.GetComponent<RectTransform>();
        myLabelRect.anchorMin = new Vector2(0.56f, 1);
        myLabelRect.anchorMax = new Vector2(0.99f, 1);
        myLabelRect.pivot = new Vector2(0, 1);
        myLabelRect.anchoredPosition = new Vector2(4, -55);
        myLabelRect.sizeDelta = new Vector2(0, 20);

        _marketMyListingsScroll = BuildScrollColumn(_marketPanel.transform, "MyListingsScroll",
            new Vector2(0.56f, 0.02f), new Vector2(0.99f, 1f), new Vector2(4, 10), new Vector2(-8, -80),
            out _marketMyListingsContent);

        // Close button
        var closeBtn = CreateButton(_marketPanel.transform, "CloseBtn", "X", Color.red, () => _marketPanel.SetActive(false));
        var closeBtnRect = closeBtn.GetComponent<RectTransform>();
        closeBtnRect.anchorMin = new Vector2(1, 1);
        closeBtnRect.anchorMax = new Vector2(1, 1);
        closeBtnRect.pivot = new Vector2(1, 1);
        closeBtnRect.anchoredPosition = new Vector2(-10, -10);
        closeBtnRect.sizeDelta = new Vector2(40, 40);

        _marketPanel.SetActive(false);
    }

    void RefreshMarketPanel()
    {
        if (_marketForSaleContent == null || _marketMyListingsContent == null) return;

        ClearChildren(_marketForSaleContent);
        ClearChildren(_marketMyListingsContent);

        var market = MarketManager.Instance;
        if (market == null)
        {
            var empty = CreateText(_marketForSaleContent, "Empty", "Market not available.", 12, new Color(0.5f, 0.5f, 0.5f));
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
            return;
        }

        string myUID = FirebaseService.IsAuthenticated ? FirebaseService.UID : "";

        // FOR SALE — other players' listings
        int forSaleCount = 0;
        foreach (var listing in market.Listings)
        {
            if (listing.SellerUID == myUID) continue; // skip own listings
            forSaleCount++;

            var row = CreatePanel(_marketForSaleContent, $"Listing_{listing.Key}", new Color(0.12f, 0.1f, 0.06f, 0.8f));
            row.AddComponent<LayoutElement>().preferredHeight = 52;

            var txt = CreateText(row.transform, "Info",
                $"{listing.ItemName}\nSeller: {listing.SellerName} | Price: {listing.Price}g",
                11, new Color(1f, 0.9f, 0.7f));
            var tRect = txt.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = new Vector2(0.7f, 1f);
            tRect.offsetMin = new Vector2(6, 2);
            tRect.offsetMax = new Vector2(-2, -2);
            txt.enableWordWrapping = true;

            var capturedKey = listing.Key;
            var capturedListing = listing;
            var buyBtn = CreateButton(row.transform, "BuyBtn", "BUY", new Color(0.2f, 0.5f, 0.2f), () =>
            {
                if (MarketManager.Instance != null && MarketManager.Instance.BuyItem(capturedKey, capturedListing))
                    RefreshMarketPanel();
            });
            var bRect = buyBtn.GetComponent<RectTransform>();
            bRect.anchorMin = new Vector2(0.72f, 0.15f);
            bRect.anchorMax = new Vector2(0.98f, 0.85f);
            bRect.offsetMin = Vector2.zero;
            bRect.offsetMax = Vector2.zero;
            buyBtn.GetComponent<LayoutElement>().ignoreLayout = true;
        }

        if (forSaleCount == 0)
        {
            var empty = CreateText(_marketForSaleContent, "Empty", "No items for sale.", 12, new Color(0.5f, 0.5f, 0.5f));
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;
        }

        // MY LISTINGS — player's own
        int myCount = 0;
        foreach (var listing in market.Listings)
        {
            if (listing.SellerUID != myUID) continue;
            myCount++;

            var row = CreatePanel(_marketMyListingsContent, $"MyListing_{listing.Key}", new Color(0.1f, 0.12f, 0.08f, 0.8f));
            row.AddComponent<LayoutElement>().preferredHeight = 42;

            var txt = CreateText(row.transform, "Info",
                $"{listing.ItemName} — {listing.Price}g",
                11, new Color(0.8f, 0.9f, 0.6f));
            var tRect = txt.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = new Vector2(6, 2);
            tRect.offsetMax = new Vector2(-6, -2);
            txt.enableWordWrapping = true;
        }

        if (myCount == 0)
        {
            var empty = CreateText(_marketMyListingsContent, "Empty", "You have no active listings.\nList crafted items from your inventory.", 12, new Color(0.5f, 0.5f, 0.5f));
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            empty.enableWordWrapping = true;
        }
    }

    void OnMarketItemPurchased(MarketListing listing)
    {
        if (_marketPanel.activeSelf) RefreshMarketPanel();
    }

    void OnMarketListingAdded(MarketListing listing)
    {
        if (_marketPanel.activeSelf) RefreshMarketPanel();
    }

    // =========================================================================
    // PARTY PANEL BUILD & REFRESH (Session 8)
    // =========================================================================

    void BuildPartyPanel(Transform parent)
    {
        _partyPanel = CreatePanel(parent, "PartyPanel", new Color(0.04f, 0.03f, 0.08f, 0.95f));
        var rect = _partyPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.15f, 0.1f);
        rect.anchorMax = new Vector2(0.85f, 0.9f);

        // Title
        var title = CreateText(_partyPanel.transform, "Title", "PARTY", 28, new Color(0.7f, 0.6f, 1f));
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(0, 50);
        title.alignment = TextAlignmentOptions.Center;

        // --- Top section: PARTY MEMBERS (0..0.55 vertically) ---
        var membersLabel = CreateText(_partyPanel.transform, "MembersLabel", "PARTY MEMBERS", 14, new Color(0.7f, 0.8f, 1f));
        var mlRect = membersLabel.GetComponent<RectTransform>();
        mlRect.anchorMin = new Vector2(0.01f, 1);
        mlRect.anchorMax = new Vector2(0.99f, 1);
        mlRect.pivot = new Vector2(0, 1);
        mlRect.anchoredPosition = new Vector2(8, -55);
        mlRect.sizeDelta = new Vector2(0, 20);

        _partyMembersScroll = BuildScrollColumn(_partyPanel.transform, "MembersScroll",
            new Vector2(0.01f, 0.45f), new Vector2(0.99f, 1f), new Vector2(8, 10), new Vector2(-8, -80),
            out _partyMembersContent);

        // --- Bottom section: NEARBY PLAYERS ---
        var nearbyLabel = CreateText(_partyPanel.transform, "NearbyLabel", "NEARBY PLAYERS", 14, new Color(0.6f, 1f, 0.7f));
        var nlRect = nearbyLabel.GetComponent<RectTransform>();
        nlRect.anchorMin = new Vector2(0.01f, 0.42f);
        nlRect.anchorMax = new Vector2(0.99f, 0.42f);
        nlRect.pivot = new Vector2(0, 0);
        nlRect.anchoredPosition = new Vector2(8, 0);
        nlRect.sizeDelta = new Vector2(0, 20);

        _partyNearbyScroll = BuildScrollColumn(_partyPanel.transform, "NearbyScroll",
            new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.42f), new Vector2(8, 10), new Vector2(-8, -4),
            out _partyNearbyContent);

        // Close button
        var closeBtn = CreateButton(_partyPanel.transform, "CloseBtn", "X", Color.red, () => _partyPanel.SetActive(false));
        var closeBtnRect = closeBtn.GetComponent<RectTransform>();
        closeBtnRect.anchorMin = new Vector2(1, 1);
        closeBtnRect.anchorMax = new Vector2(1, 1);
        closeBtnRect.pivot = new Vector2(1, 1);
        closeBtnRect.anchoredPosition = new Vector2(-10, -10);
        closeBtnRect.sizeDelta = new Vector2(40, 40);

        _partyPanel.SetActive(false);
    }

    void RefreshPartyPanel()
    {
        if (_partyMembersContent == null || _partyNearbyContent == null) return;

        ClearChildren(_partyMembersContent);
        ClearChildren(_partyNearbyContent);

        var party = PartySystem.Instance;
        bool inParty = party != null && party.InParty;

        // === PARTY MEMBERS ===
        if (inParty)
        {
            foreach (var kvp in party.Members)
            {
                bool isSelf = kvp.Key == FirebaseService.UID;
                Color bg = isSelf
                    ? new Color(0.15f, 0.12f, 0.3f, 0.8f)
                    : new Color(0.1f, 0.1f, 0.18f, 0.8f);

                var row = CreatePanel(_partyMembersContent, $"Member_{kvp.Key}", bg);
                row.AddComponent<LayoutElement>().preferredHeight = 36;

                string selfTag = isSelf ? " (You)" : "";
                var txt = CreateText(row.transform, "Name", $"  {kvp.Value}{selfTag}", 13, new Color(0.8f, 0.8f, 1f));
                var tRect = txt.GetComponent<RectTransform>();
                tRect.anchorMin = Vector2.zero;
                tRect.anchorMax = Vector2.one;
                tRect.offsetMin = new Vector2(6, 2);
                tRect.offsetMax = new Vector2(-6, -2);
            }

            // Leave Party button
            var leaveBtn = CreateButton(_partyMembersContent, "LeaveBtn", "LEAVE PARTY", new Color(0.5f, 0.2f, 0.2f), () =>
            {
                PartySystem.Instance?.LeaveParty();
                RefreshPartyPanel();
            });
            leaveBtn.AddComponent<LayoutElement>().preferredHeight = 34;
        }
        else
        {
            var empty = CreateText(_partyMembersContent, "Empty", "Not in a party.", 12, new Color(0.5f, 0.5f, 0.5f));
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;

            var createBtn = CreateButton(_partyMembersContent, "CreateBtn", "CREATE PARTY", new Color(0.3f, 0.25f, 0.6f), () =>
            {
                // Creating a party requires inviting someone — show notification
                ShowNotification("Invite a nearby player to create a party!");
            });
            createBtn.AddComponent<LayoutElement>().preferredHeight = 34;
        }

        // === NEARBY PLAYERS ===
        var presence = PresenceSystem.Instance;
        if (presence != null && presence.RemotePlayers.Count > 0)
        {
            foreach (var kvp in presence.RemotePlayers)
            {
                string uid = kvp.Key;
                var state = kvp.Value;

                // Skip players already in our party
                if (inParty && party.Members.ContainsKey(uid)) continue;

                var row = CreatePanel(_partyNearbyContent, $"Nearby_{uid}", new Color(0.08f, 0.12f, 0.08f, 0.8f));
                row.AddComponent<LayoutElement>().preferredHeight = 42;

                var txt = CreateText(row.transform, "Info",
                    $"{state.Name}\nLv.{state.Level} — {state.Region?.Replace('_', ' ')}",
                    11, new Color(0.7f, 1f, 0.7f));
                var tRect = txt.GetComponent<RectTransform>();
                tRect.anchorMin = Vector2.zero;
                tRect.anchorMax = new Vector2(0.7f, 1f);
                tRect.offsetMin = new Vector2(6, 2);
                tRect.offsetMax = new Vector2(-2, -2);
                txt.enableWordWrapping = true;

                var capturedUID = uid;
                var capturedName = state.Name;
                var inviteBtn = CreateButton(row.transform, "InviteBtn", "INVITE", new Color(0.3f, 0.25f, 0.6f), () =>
                {
                    PartySystem.Instance?.InvitePlayer(capturedUID, capturedName);
                    ShowNotification($"Invited {capturedName} to party!");
                });
                var bRect = inviteBtn.GetComponent<RectTransform>();
                bRect.anchorMin = new Vector2(0.72f, 0.15f);
                bRect.anchorMax = new Vector2(0.98f, 0.85f);
                bRect.offsetMin = Vector2.zero;
                bRect.offsetMax = Vector2.zero;
                inviteBtn.GetComponent<LayoutElement>().ignoreLayout = true;
            }
        }
        else
        {
            var empty = CreateText(_partyNearbyContent, "Empty", "No players nearby.", 12, new Color(0.5f, 0.5f, 0.5f));
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;
        }
    }

    void OnPartyPlayerJoined(string uid, string name)
    {
        ShowNotification($"{name} joined the party!");
        if (_partyPanel.activeSelf) RefreshPartyPanel();
    }

    void OnPartyPlayerLeft(string uid)
    {
        ShowNotification("A player left the party.");
        if (_partyPanel.activeSelf) RefreshPartyPanel();
    }

    void OnPartyDisbanded()
    {
        ShowNotification("Party disbanded.");
        if (_partyPanel.activeSelf) RefreshPartyPanel();
    }

    // =========================================================================
    // GUILD PANEL BUILD & REFRESH (Session 9)
    // =========================================================================

    void BuildGuildPanel(Transform parent)
    {
        _guildPanel = CreatePanel(parent, "GuildPanel", new Color(0.06f, 0.02f, 0.02f, 0.95f));
        var rect = _guildPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.15f, 0.1f);
        rect.anchorMax = new Vector2(0.85f, 0.9f);

        // Title
        var title = CreateText(_guildPanel.transform, "Title", "GUILD", 28, new Color(1f, 0.5f, 0.5f));
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(0, 50);
        title.alignment = TextAlignmentOptions.Center;

        // Scrollable content area
        _guildScroll = BuildScrollColumn(_guildPanel.transform, "GuildScroll",
            new Vector2(0.01f, 0.02f), new Vector2(0.99f, 1f), new Vector2(8, 10), new Vector2(-8, -60),
            out _guildContent);

        // Close button
        var closeBtn = CreateButton(_guildPanel.transform, "CloseBtn", "X", Color.red, () => _guildPanel.SetActive(false));
        var closeBtnRect = closeBtn.GetComponent<RectTransform>();
        closeBtnRect.anchorMin = new Vector2(1, 1);
        closeBtnRect.anchorMax = new Vector2(1, 1);
        closeBtnRect.pivot = new Vector2(1, 1);
        closeBtnRect.anchoredPosition = new Vector2(-10, -10);
        closeBtnRect.sizeDelta = new Vector2(40, 40);

        _guildPanel.SetActive(false);
    }

    void RefreshGuildPanel()
    {
        if (_guildContent == null) return;
        ClearChildren(_guildContent);

        var guild = GuildSystem.Instance;
        var data = MainPlayerData.Instance;

        if (guild == null)
        {
            var empty = CreateText(_guildContent, "Empty", "Guild system not available.", 12, new Color(0.5f, 0.5f, 0.5f));
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
            return;
        }

        if (guild.InGuild)
        {
            // === IN GUILD VIEW ===
            // Guild name header
            var nameRow = CreatePanel(_guildContent, "GuildName", new Color(0.2f, 0.1f, 0.1f, 0.8f));
            nameRow.AddComponent<LayoutElement>().preferredHeight = 40;
            var nameTxt = CreateText(nameRow.transform, "Name",
                $"  {guild.GuildName}  —  {guild.Members.Count} members",
                16, new Color(1f, 0.7f, 0.5f));
            nameTxt.fontStyle = FontStyles.Bold;
            var ntRect = nameTxt.GetComponent<RectTransform>();
            ntRect.anchorMin = Vector2.zero;
            ntRect.anchorMax = Vector2.one;
            ntRect.offsetMin = new Vector2(6, 2);
            ntRect.offsetMax = new Vector2(-6, -2);

            // Member list
            foreach (var kvp in guild.Members)
            {
                bool isSelf = kvp.Key == FirebaseService.UID;
                Color bg = isSelf
                    ? new Color(0.18f, 0.08f, 0.08f, 0.8f)
                    : new Color(0.12f, 0.06f, 0.06f, 0.7f);

                var row = CreatePanel(_guildContent, $"GMember_{kvp.Key}", bg);
                row.AddComponent<LayoutElement>().preferredHeight = 30;

                string selfTag = isSelf ? " (You)" : "";
                var txt = CreateText(row.transform, "Name", $"  {kvp.Value}{selfTag}", 12, new Color(0.9f, 0.8f, 0.7f));
                var tRect = txt.GetComponent<RectTransform>();
                tRect.anchorMin = Vector2.zero;
                tRect.anchorMax = Vector2.one;
                tRect.offsetMin = new Vector2(6, 2);
                tRect.offsetMax = new Vector2(-6, -2);
            }

            // Spacer
            var spacer = new GameObject("Spacer", typeof(RectTransform));
            spacer.transform.SetParent(_guildContent, false);
            spacer.AddComponent<LayoutElement>().preferredHeight = 10;

            // Deposit Gold button
            var depositBtn = CreateButton(_guildContent, "DepositBtn", "DEPOSIT 50 GOLD", new Color(0.5f, 0.4f, 0.1f), () =>
            {
                if (data != null && data.SpendGold(50))
                {
                    ShowNotification("Deposited 50 gold to guild treasury!");
                    ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Charisma, 5);
                    MainPlayerData.SaveToCloud();
                }
                else
                {
                    ShowNotification("Not enough gold!");
                }
            });
            depositBtn.AddComponent<LayoutElement>().preferredHeight = 34;

            // Leave Guild button
            var leaveBtn = CreateButton(_guildContent, "LeaveBtn", "LEAVE GUILD", new Color(0.5f, 0.2f, 0.2f), () =>
            {
                GuildSystem.Instance?.LeaveGuild();
                RefreshGuildPanel();
            });
            leaveBtn.AddComponent<LayoutElement>().preferredHeight = 34;
        }
        else
        {
            // === NOT IN GUILD VIEW ===
            // Create Guild button
            var createBtn = CreateButton(_guildContent, "CreateBtn", $"CREATE GUILD ({GuildSystem.Instance.GuildCreateCost}g)", new Color(0.5f, 0.2f, 0.2f), () =>
            {
                // For now, auto-name the guild. A text input could be added later.
                string guildName = $"{data?.PlayerName ?? "Adventurer"}'s Guild";
                if (GuildSystem.Instance.CreateGuild(guildName))
                    RefreshGuildPanel();
                else
                    ShowNotification("Cannot create guild — check gold!");
            });
            createBtn.AddComponent<LayoutElement>().preferredHeight = 40;

            // Spacer
            var spacer = new GameObject("Spacer", typeof(RectTransform));
            spacer.transform.SetParent(_guildContent, false);
            spacer.AddComponent<LayoutElement>().preferredHeight = 10;

            // Available guilds header
            var headerTxt = CreateText(_guildContent, "Header", "AVAILABLE GUILDS", 14, new Color(0.8f, 0.6f, 0.5f));
            headerTxt.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;

            // List guilds from presence (other players' guilds)
            bool foundGuilds = false;
            if (PresenceSystem.Instance != null)
            {
                var seenGuilds = new HashSet<string>();
                foreach (var kvp in PresenceSystem.Instance.RemotePlayers)
                {
                    // We don't have guild info on remote players directly,
                    // so show a placeholder message
                }
            }

            if (!foundGuilds)
            {
                var empty = CreateText(_guildContent, "Empty", "No guilds found nearby.\nCreate one to get started!", 12, new Color(0.5f, 0.5f, 0.5f));
                empty.enableWordWrapping = true;
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            }
        }
    }

    void OnGuildJoined(string guildName)
    {
        ShowNotification($"Joined guild: {guildName}!");
        if (_guildPanel.activeSelf) RefreshGuildPanel();
    }

    void OnGuildLeft()
    {
        ShowNotification("Left guild.");
        if (_guildPanel.activeSelf) RefreshGuildPanel();
    }

    // =========================================================================
    // CRAFTING / PROFESSION COLOR HELPERS
    // =========================================================================

    static Color RarityBgColor(string rarity)
    {
        return rarity switch
        {
            "legendary" => new Color(0.3f, 0.2f, 0.05f, 0.8f),
            "ghost-rare" => new Color(0.2f, 0.1f, 0.3f, 0.8f),
            "rare" => new Color(0.1f, 0.15f, 0.3f, 0.8f),
            "uncommon" => new Color(0.1f, 0.2f, 0.1f, 0.8f),
            _ => new Color(0.1f, 0.1f, 0.1f, 0.7f)
        };
    }

    static Color RarityTextColor(string rarity)
    {
        return rarity switch
        {
            "legendary" => new Color(1f, 0.85f, 0.3f),
            "ghost-rare" => new Color(0.8f, 0.5f, 1f),
            "rare" => new Color(0.4f, 0.6f, 1f),
            "uncommon" => new Color(0.4f, 0.8f, 0.4f),
            _ => new Color(0.7f, 0.7f, 0.7f)
        };
    }

    static Color QualityBgColor(string grade)
    {
        return grade switch
        {
            "Mastercraft" => new Color(0.35f, 0.25f, 0.05f, 0.85f),
            "Superior" => new Color(0.15f, 0.1f, 0.3f, 0.85f),
            "Quality" => new Color(0.1f, 0.2f, 0.1f, 0.85f),
            "Standard" => new Color(0.12f, 0.12f, 0.12f, 0.8f),
            _ => new Color(0.08f, 0.08f, 0.08f, 0.7f)
        };
    }

    static Color QualityTextColor(string grade)
    {
        return grade switch
        {
            "Mastercraft" => new Color(1f, 0.85f, 0.2f),
            "Superior" => new Color(0.7f, 0.5f, 1f),
            "Quality" => new Color(0.4f, 0.9f, 0.4f),
            "Standard" => Color.white,
            _ => new Color(0.6f, 0.6f, 0.6f)
        };
    }

    static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    void OnChatSubmit(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        ChatSystem.Instance?.SendMessage(text);
        _chatInput.text = "";
        _chatInput.ActivateInputField();
    }

    void AddChatMessageUI(ChatMessage msg)
    {
        if (_chatMessageParent == null) return;

        var msgGO = new GameObject("Msg", typeof(RectTransform), typeof(TextMeshProUGUI));
        msgGO.transform.SetParent(_chatMessageParent, false);
        var text = msgGO.GetComponent<TextMeshProUGUI>();
        text.text = msg.FormattedText;
        text.fontSize = 16;
        text.color = msg.IsEmote ? new Color(1, 0.8f, 0.5f) : Color.white;

        var le = msgGO.AddComponent<LayoutElement>();
        le.preferredHeight = 24;

        // Trim old messages
        while (_chatMessageParent.childCount > 50)
            Destroy(_chatMessageParent.GetChild(0).gameObject);

        // Auto-scroll
        Canvas.ForceUpdateCanvases();
        if (_chatScroll != null) _chatScroll.verticalNormalizedPosition = 0;
    }

    // =========================================================================
    // HUD UPDATE
    // =========================================================================

    void UpdateHUD()
    {
        var data = MainPlayerData.Instance;
        if (data == null) return;

        if (_goldText != null)
            _goldText.text = $"Gold: {data.Gold}";

        // Skill bar updates
        float combat = data.GetProfessionXP("combat");
        float craft = data.GetProfessionXP("crafting");
        float explore = data.GetProfessionXP("exploration");

        float dt = Time.deltaTime * XP_LERP_SPEED;
        _displayCombat = Mathf.Lerp(_displayCombat, combat, dt);
        _displayCraft = Mathf.Lerp(_displayCraft, craft, dt);
        _displayExplore = Mathf.Lerp(_displayExplore, explore, dt);
        if (Mathf.Abs(_displayCombat - combat) < 0.5f) _displayCombat = combat;
        if (Mathf.Abs(_displayCraft - craft) < 0.5f) _displayCraft = craft;
        if (Mathf.Abs(_displayExplore - explore) < 0.5f) _displayExplore = explore;

        UpdateSkillBar(_combatFill, _combatLabel, _displayCombat, combat, new Color(0.9f, 0.25f, 0.2f));
        UpdateSkillBar(_craftFill, _craftLabel, _displayCraft, craft, new Color(0.8f, 0.55f, 0.15f));
        UpdateSkillBar(_exploreFill, _exploreLabel, _displayExplore, explore, new Color(0.2f, 0.7f, 0.4f));

        // Team HP bars
        UpdateTeamHPBars();

        // Progress tracker
        UpdateProgressTracker(data);
    }

    // =========================================================================
    // NOTIFICATIONS
    // =========================================================================

    public void ShowNotification(string text)
    {
        _notifications.Enqueue(text);
    }

    void UpdateNotifications()
    {
        if (_notificationText == null) return;

        if (_notifTimer > 0)
        {
            _notifTimer -= Time.deltaTime;
            if (_notifTimer <= 0)
                _notificationText.gameObject.SetActive(false);
        }

        if (_notifications.Count > 0 && _notifTimer <= 0)
        {
            string msg = _notifications.Dequeue();
            _notificationText.text = msg;
            _notificationText.gameObject.SetActive(true);
            _notifTimer = NotifDuration;
        }
    }

    // =========================================================================
    // UI HELPERS
    // =========================================================================

    /// <summary>Creates a SWG-style segmented skill bar with label and fill.</summary>
    static (Image fill, TextMeshProUGUI label) CreateSkillBar(Transform parent, string skillName, Color barColor)
    {
        // Row container
        var row = new GameObject($"SkillBar_{skillName}", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRect = row.GetComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(0, 20);
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        // Skill name label (left)
        var nameGO = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(row.transform, false);
        var nameTMP = nameGO.GetComponent<TextMeshProUGUI>();
        nameTMP.text = skillName;
        nameTMP.fontSize = 14;
        nameTMP.color = barColor;
        nameTMP.fontStyle = TMPro.FontStyles.Bold;
        var nameLE = nameGO.AddComponent<LayoutElement>();
        nameLE.preferredWidth = 70;
        nameLE.minWidth = 70;

        // Bar background (dark container with segments)
        var barBG = new GameObject("BarBG", typeof(RectTransform), typeof(Image));
        barBG.transform.SetParent(row.transform, false);
        barBG.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 0.9f);
        var barLE = barBG.AddComponent<LayoutElement>();
        barLE.flexibleWidth = 1;
        barLE.preferredHeight = 14;

        // Fill bar (stretches left-to-right based on XP progress)
        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(barBG.transform, false);
        var fillImg = fillGO.GetComponent<Image>();
        fillImg.color = barColor;
        var fillRect = fillGO.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0, 1); // Width controlled by anchorMax.x
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        // Segment lines overlay (thin white dividers at milestone positions)
        for (int i = 1; i < SKILL_MILESTONES.Length; i++)
        {
            float pct = (float)i / (SKILL_MILESTONES.Length - 1);
            var seg = new GameObject($"Seg{i}", typeof(RectTransform), typeof(Image));
            seg.transform.SetParent(barBG.transform, false);
            seg.GetComponent<Image>().color = new Color(1, 1, 1, 0.15f);
            var segRect = seg.GetComponent<RectTransform>();
            segRect.anchorMin = new Vector2(pct, 0);
            segRect.anchorMax = new Vector2(pct, 1);
            segRect.sizeDelta = new Vector2(1, 0);
        }

        // XP value label (right of bar)
        var valGO = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
        valGO.transform.SetParent(row.transform, false);
        var valTMP = valGO.GetComponent<TextMeshProUGUI>();
        valTMP.text = "0";
        valTMP.fontSize = 14;
        valTMP.color = new Color(0.7f, 0.7f, 0.8f);
        valTMP.alignment = TextAlignmentOptions.Right;
        var valLE = valGO.AddComponent<LayoutElement>();
        valLE.preferredWidth = 55;

        return (fillImg, valTMP);
    }

    /// <summary>Update a skill bar fill amount based on XP value.</summary>
    void UpdateSkillBar(Image fill, TextMeshProUGUI label, float displayXP, float actualXP, Color barColor)
    {
        if (fill == null || label == null) return;

        // Calculate fill percentage across milestones (0→5000 mapped to 0→1)
        float maxXP = SKILL_MILESTONES[^1];
        float fillPct = Mathf.Clamp01(displayXP / maxXP);
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMax = new Vector2(fillPct, 1);

        // Glow brighter when actively climbing
        bool climbing = Mathf.Abs(displayXP - actualXP) > 0.5f;
        fill.color = climbing
            ? Color.Lerp(barColor, Color.white, 0.3f + Mathf.Sin(Time.time * 6f) * 0.15f)
            : barColor;

        // Find current milestone segment
        string milestoneText = "";
        for (int i = 0; i < SKILL_MILESTONES.Length - 1; i++)
        {
            if (displayXP < SKILL_MILESTONES[i + 1])
            {
                milestoneText = $"{displayXP:F0}/{SKILL_MILESTONES[i + 1]}";
                break;
            }
        }
        if (string.IsNullOrEmpty(milestoneText))
            milestoneText = $"{displayXP:F0} MAX";

        label.text = milestoneText;
    }

    static GameObject CreatePanel(Transform parent, string name, Color bgColor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bgColor;
        return go;
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        return tmp;
    }

    // =========================================================================
    // TEAM HP BARS
    // =========================================================================

    void BuildTeamHPPanel(Transform parent)
    {
        _teamHPPanel = CreatePanel(parent, "TeamHPPanel", new Color(0, 0, 0, 0.5f));
        var rect = _teamHPPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(270, 120);
        rect.anchoredPosition = new Vector2(10, -70);

        var layout = _teamHPPanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 6, 6);
        layout.spacing = 3;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Title
        var title = CreateText(_teamHPPanel.transform, "TeamTitle", "TEAM", 16, new Color(1f, 0.84f, 0f));
        title.fontStyle = TMPro.FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;

        _teamBars.Clear();
        Color[] barColors = {
            new Color(0.3f, 0.8f, 0.4f),
            new Color(0.3f, 0.5f, 0.9f),
            new Color(0.9f, 0.6f, 0.2f)
        };

        var data = MainPlayerData.Instance;
        int count = data != null ? Mathf.Min(3, data.SlottedCardIds.Count) : 0;

        for (int i = 0; i < 3; i++)
        {
            var row = new GameObject($"HPRow_{i}", typeof(RectTransform));
            row.transform.SetParent(_teamHPPanel.transform, false);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 4;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            row.AddComponent<LayoutElement>().preferredHeight = 24;

            // Name label
            string cardName = "---";
            if (i < count)
            {
                string cardId = data.SlottedCardIds[i];
                if (AssetManager.TryGetIdItem(cardId, out IdItem item) && item is Card card)
                    cardName = card.CardName ?? "???";
            }
            var nameGO = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameGO.transform.SetParent(row.transform, false);
            var nameTMP = nameGO.GetComponent<TextMeshProUGUI>();
            nameTMP.text = cardName;
            nameTMP.fontSize = 15;
            nameTMP.color = Color.white;
            var nameLE = nameGO.AddComponent<LayoutElement>();
            nameLE.preferredWidth = 85;
            nameLE.minWidth = 85;

            // Bar background
            var barBG = new GameObject("BarBG", typeof(RectTransform), typeof(Image));
            barBG.transform.SetParent(row.transform, false);
            barBG.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.2f);
            var barLE = barBG.AddComponent<LayoutElement>();
            barLE.flexibleWidth = 1;
            barLE.preferredHeight = 14;

            // Fill bar
            var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGO.transform.SetParent(barBG.transform, false);
            var fillImg = fillGO.GetComponent<Image>();
            fillImg.color = barColors[i];
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillAmount = 1f;
            var fillRect = fillGO.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            // HP text
            var hpGO = new GameObject("HP", typeof(RectTransform), typeof(TextMeshProUGUI));
            hpGO.transform.SetParent(row.transform, false);
            var hpTMP = hpGO.GetComponent<TextMeshProUGUI>();
            hpTMP.text = "2/2";
            hpTMP.fontSize = 15;
            hpTMP.color = Color.white;
            hpTMP.alignment = TextAlignmentOptions.Right;
            var hpLE = hpGO.AddComponent<LayoutElement>();
            hpLE.preferredWidth = 40;

            _teamBars.Add((nameTMP, fillImg, hpTMP));

            if (i >= count)
                row.SetActive(false);
        }
    }

    void UpdateTeamHPBars()
    {
        if (_teamBars == null || _teamBars.Count == 0) return;

        int battleHealth = WorldManager.Instance?.WorldSaved?.BattleHealth ?? 6;
        int remaining = Mathf.Clamp(battleHealth, 0, 6);

        for (int i = 0; i < _teamBars.Count; i++)
        {
            int cardHP = Mathf.Min(2, remaining);
            remaining -= cardHP;

            var bar = _teamBars[i];
            if (bar.hpFill != null)
                bar.hpFill.fillAmount = cardHP / 2f;
            if (bar.hpText != null)
                bar.hpText.text = $"{cardHP}/2";
        }
    }

    // =========================================================================
    // FLOATING XP TEXT
    // =========================================================================

    void SpawnFloatingXPText(ProfessionXPType type, float amount)
    {
        if (_overlayCanvas == null) return;

        var go = new GameObject("FloatingXP", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(CanvasGroup));
        go.transform.SetParent(_overlayCanvas.transform, false);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = $"+{amount:F0} {type} XP";
        tmp.fontSize = 20;
        tmp.fontStyle = TMPro.FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;

        switch (type)
        {
            case ProfessionXPType.Combat:
                tmp.color = new Color(0.9f, 0.25f, 0.2f);
                break;
            case ProfessionXPType.Crafting:
                tmp.color = new Color(0.8f, 0.55f, 0.15f);
                break;
            case ProfessionXPType.Exploration:
                tmp.color = new Color(0.2f, 0.7f, 0.4f);
                break;
            default:
                tmp.color = Color.white;
                break;
        }

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1);
        rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(350, 36);
        rect.anchoredPosition = new Vector2(0, -55);

        StartCoroutine(FloatAndFade(go, 1.5f, 30f));
    }

    IEnumerator FloatAndFade(GameObject go, float duration, float floatDistance)
    {
        var rect = go.GetComponent<RectTransform>();
        var cg = go.GetComponent<CanvasGroup>();
        Vector2 startPos = rect.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            rect.anchoredPosition = startPos + new Vector2(0, -floatDistance * t);
            cg.alpha = 1f - t;
            yield return null;
        }

        Destroy(go);
    }

    // =========================================================================
    // PROGRESS TRACKER
    // =========================================================================

    void BuildProgressPanel(Transform parent)
    {
        _progressPanel = CreatePanel(parent, "ProgressPanel", new Color(0, 0, 0, 0.5f));
        var rect = _progressPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(270, 110);
        rect.anchoredPosition = new Vector2(10, -200);

        var layout = _progressPanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 6, 6);
        layout.spacing = 4;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Region text
        _progressRegionText = CreateText(_progressPanel.transform, "Region", "Region: ---", 16, Color.white);
        _progressRegionText.fontStyle = TMPro.FontStyles.Bold;
        _progressRegionText.gameObject.AddComponent<LayoutElement>().preferredHeight = 26;

        // Quest text
        _progressQuestText = CreateText(_progressPanel.transform, "Quest", "Quest: ---", 15, new Color(1f, 0.84f, 0f));
        _progressQuestText.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;

        // Stats text
        _progressStatsText = CreateText(_progressPanel.transform, "Stats", "Cards: 0 | Crafted: 0 | Zones: 0", 14, new Color(0.6f, 0.7f, 0.9f));
        _progressStatsText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;
    }

    void UpdateProgressTracker(MainPlayerData data)
    {
        if (_progressRegionText != null)
        {
            string region = RegionManager.Instance?.CurrentRegion;
            _progressRegionText.text = !string.IsNullOrEmpty(region)
                ? $"Region: {region.Replace('_', ' ')}"
                : "Region: ---";
        }

        if (_progressQuestText != null)
        {
            var quests = QuestManager.Instance?.GetActiveDailyQuests();
            if (quests != null && quests.Count > 0)
            {
                var q = quests[0];
                _progressQuestText.text = $"Quest: {q.Title}";
            }
            else
            {
                _progressQuestText.text = "Quest: ---";
            }
        }

        if (_progressStatsText != null && data != null)
        {
            int cards = data.SlottedCardIds?.Count ?? 0;
            int crafted = data.CraftedItems?.Count ?? 0;
            int zones = data.VisitedZones?.Count ?? 0;
            _progressStatsText.text = $"Cards: {cards} | Crafted: {crafted} | Zones: {zones}";
        }
    }

    static GameObject CreateButton(Transform parent, string name, string label, Color bgColor, Action onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bgColor;
        go.GetComponent<Button>().onClick.AddListener(() => onClick());

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 90;
        le.preferredHeight = 40;

        var textGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);
        var tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 18;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;

        return go;
    }
}
