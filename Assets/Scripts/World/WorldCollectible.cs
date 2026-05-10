using UnityEngine;

/// <summary>
/// World collectible: lore tablets, treasure chests, viewpoints, path signs.
/// Press E to interact. Tracked in MainPlayerData.
/// </summary>
public class WorldCollectible : MonoBehaviour
{
    public enum CollectibleType { Lore, TreasureChest, Viewpoint, PathSign }

    [Header("Identity")]
    public string CollectibleId;
    public string DisplayName;
    public CollectibleType Type;

    [Header("Content")]
    [TextArea(2, 6)]
    public string ContentText;

    [Header("Rewards (Chests only)")]
    public int GoldReward;
    public string MaterialReward;
    public int MaterialAmount;

    [Header("State")]
    public bool IsCollected;

    float _interactCooldown;

    void Start()
    {
        // Build procedural model if no existing mesh
        var existingMesh = GetComponentInChildren<MeshFilter>();
        if (existingMesh == null)
            CollectibleModelBuilder.BuildForCollectible(Type, transform);

        // Check if already collected
        var data = MainPlayerData.Instance;
        if (data != null)
        {
            switch (Type)
            {
                case CollectibleType.Lore:
                    IsCollected = data.DiscoveredLore.Contains(CollectibleId);
                    break;
                case CollectibleType.TreasureChest:
                    IsCollected = data.FoundCrystals.Contains(CollectibleId);
                    break;
                case CollectibleType.Viewpoint:
                    IsCollected = data.FoundViewpoints.Contains(CollectibleId);
                    break;
            }

            // Hide collected chests
            if (IsCollected && Type == CollectibleType.TreasureChest)
            {
                var renderer = GetComponentInChildren<Renderer>();
                if (renderer != null) renderer.material.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            }
        }
    }

    void Update()
    {
        if (_interactCooldown > 0) _interactCooldown -= Time.deltaTime;

        // Check for E key interaction
        if (Input.GetKeyDown(KeyCode.E) && _interactCooldown <= 0)
        {
            var player = WorldManager.Instance?.WorldPlayer;
            if (player == null) return;

            float dist = Vector3.Distance(transform.position, player.transform.position);
            if (dist < 2.5f)
            {
                Interact();
                _interactCooldown = 1f;
            }
        }
    }

    void Interact()
    {
        var data = MainPlayerData.Instance;
        if (data == null) return;

        switch (Type)
        {
            case CollectibleType.Lore:
                if (!data.DiscoveredLore.Contains(CollectibleId))
                {
                    data.DiscoveredLore.Add(CollectibleId);
                    ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Exploration, 25);
                    OverworldIntegration.Instance?.ShowNotification($"Lore discovered: {DisplayName}");
                }
                else
                {
                    OverworldIntegration.Instance?.ShowNotification($"[{DisplayName}] {ContentText}");
                }
                IsCollected = true;
                break;

            case CollectibleType.TreasureChest:
                if (!data.FoundCrystals.Contains(CollectibleId))
                {
                    data.FoundCrystals.Add(CollectibleId);
                    data.AddGold(GoldReward);
                    if (!string.IsNullOrEmpty(MaterialReward))
                        data.AddMaterial(MaterialReward, MaterialAmount);
                    ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Exploration, 50);
                    OverworldIntegration.Instance?.ShowNotification($"Treasure! +{GoldReward} Gold{(MaterialAmount > 0 ? $" + {MaterialAmount} {MaterialReward}" : "")}");
                    IsCollected = true;

                    // Dim the chest
                    var renderer = GetComponentInChildren<Renderer>();
                    if (renderer != null) renderer.material.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                }
                else
                {
                    OverworldIntegration.Instance?.ShowNotification("Already opened.");
                }
                break;

            case CollectibleType.Viewpoint:
                if (!data.FoundViewpoints.Contains(CollectibleId))
                {
                    data.FoundViewpoints.Add(CollectibleId);
                    ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Exploration, 30);
                }
                OverworldIntegration.Instance?.ShowNotification($"[Viewpoint] {DisplayName}: {ContentText}");
                IsCollected = true;
                break;

            case CollectibleType.PathSign:
                OverworldIntegration.Instance?.ShowNotification(ContentText);
                break;
        }

        MainPlayerData.SaveToCloud();
    }

    /// <summary>Static factory to create lore items at runtime.</summary>
    public static readonly LoreData[] ALL_LORE = {
        new("lore_01", "The First Spiritkin", "Before the Overworld existed, there was only the Void. The first Spiritkin emerged from pure emotion — joy became Puff, courage became Toby, mischief became Tommy Salami."),
        new("lore_02", "Valkin's Curse", "The Grand Spiritkin Valkin sought to unite all others under his rule. When they refused, he cursed the land, splitting it into four hostile regions."),
        new("lore_03", "The Frost Valley Pact", "Elder Frost gathered the peaceful Spiritkin to Frost Valley, creating a sanctuary where wardens could train without fear of Valkin's shadows."),
        new("lore_04", "Rolling Hills Origins", "The Rolling Hills were once barren rock. Selene, Heart of the Hills, planted the first Healing Seed, and life bloomed across the meadows."),
        new("lore_05", "The Volcanic Forge", "Beneath the Volcanic Isles, ancient fire spirits forge materials of unimaginable power. Only the bravest crafters dare harvest volcanic glass from the obsidian fields."),
        new("lore_06", "Dark Castle's Fall", "The Dark Castle was once a grand academy of Spiritkin mastery. Valkin corrupted it, turning students into shadow soldiers. The halls still echo with their lessons."),
        new("lore_07", "Leon's Journey", "Leon the wanderer traveled all four regions seeking the Mask of Destiny. His journal entries guide those who follow his path today."),
        new("lore_08", "The Dice of Fate", "Spiritkin battles are resolved by the Dice of Fate — ancient artifacts that respond to the bond between warden and spirit."),
        new("lore_09", "Crafting Mastery", "In the old world, the greatest crafters were celebrities. A weapon stamped with a master's name was worth more than gold."),
        new("lore_10", "The Four Elements", "Ice Shards from Frost Valley. Sacred Fire from the Volcanic Isles. Healing Seeds from Rolling Hills. Moonstones from Dark Castle. Together they form the balance."),
        new("lore_11", "Elder Frost's Warning", "Elder Frost speaks rarely, but when he does, all listen. 'The shadows are growing. Valkin stirs. We need wardens — not soldiers, not heroes — wardens.'"),
        new("lore_12", "The Spirit Bond", "A Spiritkin chooses its warden as much as the warden chooses it. Those who force the bond find their spirits weak and disobedient."),
        new("lore_13", "Tommy Salami's Secret", "Nobody knows where Tommy Salami came from. He appeared one day at Elder Frost's door, grinning, and never left. Some say he's the oldest Spiritkin alive."),
        new("lore_14", "The World Boss Cycle", "Every half-hour, a powerful Spiritkin awakens somewhere in the Overworld. Only wardens working together can bring it down — and claim its treasures."),
    };

    public struct LoreData
    {
        public string Id;
        public string Title;
        public string Text;
        public LoreData(string id, string title, string text) { Id = id; Title = title; Text = text; }
    }
}
