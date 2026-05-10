using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Complete NPC system with alive behaviors: facing, idle animation,
/// wandering, ambient dialogue, interaction polish, trainer indicators.
/// </summary>
public class NPCController : Interactable
{
    // =========================================================================
    // STATIC DATA — All 15 NPCs
    // =========================================================================

    public static readonly List<NPCData> NPC_DATA = new()
    {
        // ── Friendly: Frost Valley ──
        new NPCData
        {
            Id = "elder_frost", Name = "Elder Frost", Region = "frost_valley",
            Type = NPCType.Friendly, Role = NPCRole.Wisdom,
            Dialogue = new List<string>
            {
                "The spirits remember what men forget. Listen closely.",
                "Frost Valley was the first land the Spiritkin claimed. We are guests here.",
                "A wise trainer crafts before they fight. Visit Smith Ember when you can.",
                "The frozen crystals hum at night. Can you hear them?",
                "Every defeat teaches more than victory. Remember that, young one."
            }
        },
        new NPCData
        {
            Id = "smith_ember", Name = "Smith Ember", Region = "frost_valley",
            Type = NPCType.Friendly, Role = NPCRole.Crafting,
            Dialogue = new List<string>
            {
                "Iron sings when you heat it right. Most folk never learn that.",
                "Bring me ore and I will make you something worth carrying.",
                "The best weapons are forged with patience, not fire alone.",
                "Superior grade? That takes three perfect essences and steady hands.",
                "Kira down in the Volcanic Isles thinks she is the best smith. She is wrong."
            }
        },
        new NPCData
        {
            Id = "keeper_zara", Name = "Keeper Zara", Region = "frost_valley",
            Type = NPCType.Friendly, Role = NPCRole.Knowledge,
            Dialogue = new List<string>
            {
                "Every Spiritkin has a story. I have catalogued four hundred and twelve of them.",
                "Rare spirits hide in the tall grass. Walk slowly and look carefully.",
                "A full team of three is stronger than one legendary alone.",
                "The battle is won before the dice are rolled. Preparation is everything.",
                "If you find a spirit you have never seen, bring word to me. I pay well for knowledge."
            }
        },

        // ── Friendly: Rolling Hills ──
        new NPCData
        {
            Id = "farmer_bea", Name = "Farmer Bea", Region = "rolling_hills",
            Type = NPCType.Friendly, Role = NPCRole.Gathering,
            Dialogue = new List<string>
            {
                "The hills give everything you need if you know where to look.",
                "Healing seeds grow near water. Always check the riverbanks.",
                "I feed the wild spirits sometimes. They remember kindness.",
                "My harvest this season was the best in years. The soil is happy.",
                "Watch your step in the tall grass. Not everything hiding there is friendly."
            }
        },
        new NPCData
        {
            Id = "herbalist_sage", Name = "Herbalist Sage", Region = "rolling_hills",
            Type = NPCType.Friendly, Role = NPCRole.Healing,
            Dialogue = new List<string>
            {
                "Nature provides all the medicine you will ever need.",
                "The rarest essences have a faint glow. Train your eyes to see it.",
                "Walk more. The body knows things the mind ignores.",
                "A good essence has potency above five hundred. Below that, it is compost.",
                "Bring me three essences and I will teach you something useful."
            }
        },
        new NPCData
        {
            Id = "captain_rex", Name = "Captain Rex", Region = "rolling_hills",
            Type = NPCType.Friendly, Role = NPCRole.Exploration,
            Dialogue = new List<string>
            {
                "There are places on this map nobody has walked in a hundred years.",
                "Three zones in one day and you will find something worth keeping. Guaranteed.",
                "The bravest trainers fight without getting touched. Try it sometime.",
                "Treasure chests do not open themselves. Get out there and explore!",
                "I have walked every inch of Rolling Hills. The Volcanic Isles, though... not yet."
            }
        },

        // ── Friendly: Volcanic Isles ──
        new NPCData
        {
            Id = "forge_master_kira", Name = "Forge Master Kira", Region = "volcanic_isles",
            Type = NPCType.Friendly, Role = NPCRole.FireCrafting,
            Dialogue = new List<string>
            {
                "Fire essence makes everything stronger. That is not opinion, it is physics.",
                "Obsidian glass only forms where the lava meets the sea. Dangerous to collect.",
                "Smith Ember thinks cold forging is the future. He is a fool.",
                "Win three battles on these isles and the mountain spirits will respect you.",
                "Bring me volcanic glass and fire essence. I will make you a legend."
            }
        },
        new NPCData
        {
            Id = "merchant_dax", Name = "Merchant Dax", Region = "volcanic_isles",
            Type = NPCType.Friendly, Role = NPCRole.Trading,
            Dialogue = new List<string>
            {
                "Everything has a price. The trick is knowing what others will pay.",
                "List your crafted items on the market. Gold does not earn itself.",
                "Travel between regions and you will learn what sells where.",
                "Fifty gold in a day is decent. A hundred means you are paying attention.",
                "The best traders never fight. But they always fund the fighters."
            }
        },

        // ── Friendly: Dark Castle ──
        new NPCData
        {
            Id = "shadow_warden_vex", Name = "Shadow Warden Vex", Region = "dark_castle",
            Type = NPCType.Friendly, Role = NPCRole.DarkLore,
            Dialogue = new List<string>
            {
                "You made it this far. Most do not. The shadows are watching.",
                "The elite spirits here will test everything you have learned.",
                "Darkness is not evil. It is merely the absence of what you expected.",
                "When night falls, the castle changes. Pay attention to what moves.",
                "The endgame begins when you stop fearing what you cannot see."
            }
        },

        // ── Hostile Trainers ──
        new NPCData
        {
            Id = "brawler_jax", Name = "Brawler Jax", Region = "frost_valley",
            Type = NPCType.Trainer, Role = NPCRole.Trainer,
            Team = new[] { 34, 5, 48 }, // Grawr, Puff, Opa
            GoldReward = 30, XPReward = 20,
            Dialogue = new List<string>
            {
                "You look soft. Let me fix that.",
                "Grawr has not lost in weeks. Want to be next?",
                "No running. We settle this with dice.",
                "Puff may look cute but she hits like a boulder.",
                "Come back tomorrow if you want another beating."
            }
        },
        new NPCData
        {
            Id = "ice_queen_vera", Name = "Ice Queen Vera", Region = "frost_valley",
            Type = NPCType.Trainer, Role = NPCRole.Trainer,
            Team = new[] { 81, 29, 86 }, // Spockles, Sad Sal, Pelter
            GoldReward = 40, XPReward = 30,
            Dialogue = new List<string>
            {
                "The cold does not bother me. But it will bother you.",
                "Spockles freezes opponents solid. Literally.",
                "Sad Sal has never smiled. Neither will you after this fight.",
                "Pelter rains ice from above. Dodge if you can.",
                "Defeated? The frost claims another victim."
            }
        },
        new NPCData
        {
            Id = "bandit_marcus", Name = "Bandit Marcus", Region = "rolling_hills",
            Type = NPCType.Trainer, Role = NPCRole.Trainer,
            Team = new[] { 43, 60, 93 }, // Outlaw, Dallas, Bandit Pete
            GoldReward = 45, XPReward = 35,
            Dialogue = new List<string>
            {
                "Hand over your gold or hand over your dignity. Your choice.",
                "Dallas steals dice right off the board. Fair? Never said I was.",
                "Outlaw plays by his own rules. Spoiler: he has no rules.",
                "Bandit Pete is new to the crew but he is hungry to prove himself.",
                "Nothing personal. Just business."
            }
        },
        new NPCData
        {
            Id = "lava_raider_kira", Name = "Lava Raider Kira", Region = "volcanic_isles",
            Type = NPCType.Trainer, Role = NPCRole.Trainer,
            Team = new[] { 304, 336, 209 }, // Ember Force, Humar, Dart
            GoldReward = 60, XPReward = 50,
            Dialogue = new List<string>
            {
                "The isles burn. So will you.",
                "Ember Force does not just attack. He incinerates.",
                "Humar learned to fight in the caldera. You learned in a field.",
                "Dart never misses. I trained him myself.",
                "Lava does not care about your strategy."
            }
        },
        new NPCData
        {
            Id = "shadow_knight_vex", Name = "Shadow Knight Vex", Region = "dark_castle",
            Type = NPCType.Trainer, Role = NPCRole.Trainer,
            Team = new[] { 202, 78, 108 }, // Dark Fang, Haywire, Lucy
            GoldReward = 75, XPReward = 60,
            Dialogue = new List<string>
            {
                "You entered the dark willingly. That was your first mistake.",
                "Dark Fang feeds on fear. Yours smells delicious.",
                "Haywire is unpredictable. Even I do not know what she will do.",
                "Lucy looks innocent. That is how she gets you.",
                "The shadows remember every challenger. None have returned unbroken."
            }
        },
        new NPCData
        {
            Id = "the_exile", Name = "The Exile", Region = "dark_castle",
            Type = NPCType.Trainer, Role = NPCRole.Trainer,
            Team = new[] { 97, 113, 110 }, // Toby, Prince Balatron, Mountain King
            GoldReward = 100, XPReward = 80,
            Dialogue = new List<string>
            {
                "I was the greatest trainer this world has ever seen. Then they cast me out.",
                "Toby alone would destroy your entire team. I brought three.",
                "Prince Balatron bows to no one. Especially not to you.",
                "Mountain King shakes the ground when he walks. Feel that tremor?",
                "Defeat me and you earn a title few have ever claimed."
            }
        },
    };

    // Quick lookup by Id
    static Dictionary<string, NPCData> _npcLookup;
    public static Dictionary<string, NPCData> NPCLookup
    {
        get
        {
            if (_npcLookup == null)
            {
                _npcLookup = new Dictionary<string, NPCData>();
                foreach (var npc in NPC_DATA)
                    _npcLookup[npc.Id] = npc;
            }
            return _npcLookup;
        }
    }

    // =========================================================================
    // INSTANCE FIELDS
    // =========================================================================

    [Header("Identity")]
    public string NPCId;
    public string NPCName;
    public string Region;
    public NPCType Type = NPCType.Friendly;
    public NPCRole Role = NPCRole.Wisdom;
    public float NPCHeight = 1.8f;

    [Header("Dialogue")]
    public List<string> DialogueLines = new();
    public float AutoDismissTime = 6f;

    [Header("Trainer")]
    public int[] TrainerTeam;
    public int GoldReward;
    public int XPReward;

    [Header("UI")]
    public Transform SpeechBubbleAnchor;
    public GameObject SpeechBubblePrefab;

    [Header("Proximity")]
    public float InteractionRange = 3f;
    public float TrainerChallengeRange = 1.5f;

    // Core state
    bool _isInRange;
    bool _isShowingDialogue;
    int _currentLine;
    float _dismissTimer;
    GameObject _activeBubble;
    bool _challengePromptShown;
    Transform _playerTransform;

    // ── Behavior: Facing ──
    Quaternion _spawnRotation;
    float _facePlayerRange = 8f;
    bool _shouldFacePlayer;

    // ── Behavior: Idle Animation ──
    Transform _bodyTransform;
    Vector3 _bodyBaseLocalPos;
    float _idlePhase;
    float _bobAmplitude = 0.03f;
    float _bobFrequency = 1.2f;
    float _swayAmplitude = 3f;
    float _swayFrequency = 0.7f;

    // ── Behavior: Wandering ──
    Vector3 _spawnPosition;
    Vector3[] _waypoints;
    int _currentWaypoint;
    float _wanderSpeed = 0.8f;
    float _waypointPauseTimer;
    float _wanderStopRange = 6f;
    bool _isWandering;

    // ── Behavior: Ambient Dialogue ──
    float _ambientChatTimer;
    const float AMBIENT_CHAT_RANGE = 20f;
    static float _lastAmbientChatTime;

    // ── Behavior: Interaction Polish ──
    TextMesh _nameLabel;
    Color _nameLabelOriginalColor;
    Coroutine _typingCoroutine;
    Coroutine _bounceCoroutine;
    readonly WaitForSeconds _typingDelay = new(0.025f);

    // ── Behavior: Trainer Indicator ──
    GameObject _challengeIndicator;

    // ── Terrain clamping ──
    float _terrainClampTimer;

    // =========================================================================
    // INITIALIZATION
    // =========================================================================

    /// <summary>Initialize this NPC from static data by Id.</summary>
    public void InitFromData(string npcId)
    {
        if (!NPCLookup.TryGetValue(npcId, out NPCData data))
        {
            Debug.LogWarning($"[NPC] Unknown NPC Id: {npcId}");
            return;
        }

        NPCId = data.Id;
        NPCName = data.Name;
        Region = data.Region;
        Type = data.Type;
        Role = data.Role;
        DialogueLines = new List<string>(data.Dialogue);
        TrainerTeam = data.Team;
        GoldReward = data.GoldReward;
        XPReward = data.XPReward;
    }

    void Start()
    {
        if (!string.IsNullOrEmpty(NPCId) && DialogueLines.Count == 0)
            InitFromData(NPCId);

        // Behavior init
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;
        _idlePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        _ambientChatTimer = UnityEngine.Random.Range(10f, 30f);
        _waypointPauseTimer = UnityEngine.Random.Range(1f, 4f);

        // Find body child
        var body = transform.Find("Body");
        if (body != null)
        {
            _bodyTransform = body;
            _bodyBaseLocalPos = body.localPosition;
        }

        // Find name label
        var label = transform.Find("NameLabel");
        if (label != null)
        {
            _nameLabel = label.GetComponent<TextMesh>();
            if (_nameLabel != null) _nameLabelOriginalColor = _nameLabel.color;
        }

        // Trainer config
        if (Type == NPCType.Trainer)
        {
            _wanderSpeed = 1.4f;
            _bobAmplitude = 0.04f;
            _bobFrequency = 1.8f;
            _facePlayerRange = 12f;
            _wanderStopRange = 10f;
        }

        GenerateWaypoints();
    }

    // =========================================================================
    // INTERACTABLE OVERRIDES
    // =========================================================================

    public override bool CanInteract(InteractCtx ctx)
    {
        if (ctx == null) return false;
        float dist = Vector3.Distance(transform.position, ctx.InteractPos);
        return dist <= InteractionRange;
    }

    public override void OnInteract(InteractCtx ctx)
    {
        base.OnInteract(ctx);

        // Don't re-trigger if SpiritComms is already showing dialogue
        if (SpiritComms.Instance != null && SpiritComms.Instance.IsActive)
            return;

        switch (Type)
        {
            case NPCType.Friendly:
                StartDialogue();
                break;

            case NPCType.Trainer:
                if (!IsTrainerOnCooldown())
                    StartTrainerBattle();
                else
                    ShowDialogue("I have nothing more to prove today. Come back tomorrow.");
                break;
        }

        // Greeting bounce
        if (_bounceCoroutine != null) StopCoroutine(_bounceCoroutine);
        _bounceCoroutine = StartCoroutine(GreetingBounce());
    }

    IEnumerator GreetingBounce()
    {
        if (_bodyTransform == null) yield break;
        Vector3 original = _bodyTransform.localScale;
        Vector3 popped = original * 1.15f;
        float t = 0;
        while (t < 0.1f) { t += Time.deltaTime; _bodyTransform.localScale = Vector3.Lerp(original, popped, t / 0.1f); yield return null; }
        t = 0;
        while (t < 0.2f) { t += Time.deltaTime; float ease = 1f - Mathf.Pow(1f - (t / 0.2f), 2f); _bodyTransform.localScale = Vector3.Lerp(popped, original, ease); yield return null; }
        _bodyTransform.localScale = original;
    }

    // =========================================================================
    // UPDATE — Proximity checks + dialogue timer
    // =========================================================================

    void Update()
    {
        // Cache player transform
        if (_playerTransform == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) _playerTransform = player.transform;
        }

        if (_playerTransform != null)
        {
            float dist = Vector3.Distance(transform.position, _playerTransform.position);

            // Interaction range enter/exit
            if (dist <= InteractionRange && !_isInRange)
            {
                _isInRange = true;
                OnPlayerEnterRange();
            }
            else if (dist > InteractionRange && _isInRange)
            {
                _isInRange = false;
                OnPlayerExitRange();
            }

            // Trainer proximity challenge
            if (Type == NPCType.Trainer && dist <= TrainerChallengeRange && !_challengePromptShown)
            {
                _challengePromptShown = true;
                if (!IsTrainerOnCooldown())
                    ShowChallengePrompt();
            }
            else if (dist > TrainerChallengeRange)
            {
                _challengePromptShown = false;
            }
        }

        // ── ALIVE BEHAVIORS ──
        UpdateFacing();
        UpdateIdleAnimation();
        UpdateWandering();
        UpdateAmbientChat();
        UpdateChallengeIndicator();

        // Clamp to terrain — use terrain heightmap directly, not raycast
        _terrainClampTimer -= Time.deltaTime;
        if (_terrainClampTimer <= 0)
        {
            _terrainClampTimer = 0.5f;
            float terrainY = GetTerrainHeight(transform.position);
            if (!float.IsNaN(terrainY))
            {
                var pos = transform.position;
                pos.y = terrainY; // HeightOffset already applied at spawn
                transform.position = pos;
            }
        }

        // Trainer aggro — chase player and trigger battle
        if (Type == NPCType.Trainer) UpdateTrainerAggro();

        // Auto-dismiss dialogue timer
        if (_isShowingDialogue)
        {
            _dismissTimer -= Time.deltaTime;
            if (_dismissTimer <= 0)
                HideDialogue();
        }
    }

    // =========================================================================
    // BEHAVIOR: Face the Player
    // =========================================================================

    void UpdateFacing()
    {
        if (_playerTransform == null) return;
        float dist = Vector3.Distance(transform.position, _playerTransform.position);
        _shouldFacePlayer = dist <= _facePlayerRange;

        Quaternion targetRot;
        if (_shouldFacePlayer)
        {
            Vector3 dir = _playerTransform.position - transform.position;
            dir.y = 0;
            targetRot = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : transform.rotation;
        }
        else
        {
            targetRot = _spawnRotation;
        }
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 3f);
    }

    // =========================================================================
    // BEHAVIOR: Procedural Idle Animation
    // =========================================================================

    void UpdateIdleAnimation()
    {
        if (_bodyTransform == null) return;
        float t = Time.time + _idlePhase;

        // Breathing bob
        float bob = Mathf.Sin(t * _bobFrequency * Mathf.PI * 2f) * _bobAmplitude;
        _bodyTransform.localPosition = _bodyBaseLocalPos + Vector3.up * bob;

        // Weight-shift sway (only when idle, not facing player)
        if (!_shouldFacePlayer)
        {
            float sway = Mathf.Sin(t * _swayFrequency * Mathf.PI * 2f) * _swayAmplitude;
            _bodyTransform.localRotation = Quaternion.Euler(0, sway, 0);
        }
        else
        {
            _bodyTransform.localRotation = Quaternion.identity;
        }
    }

    // =========================================================================
    // BEHAVIOR: Wandering
    // =========================================================================

    void GenerateWaypoints()
    {
        int count = UnityEngine.Random.Range(3, 6);
        float radius = (Type == NPCType.Trainer) ? 6f : 4f;
        _waypoints = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
            Vector3 candidate = _spawnPosition + new Vector3(offset.x, 0, offset.y);
            if (Physics.Raycast(candidate + Vector3.up * 50f, Vector3.down, out var hit, 100f))
                candidate = hit.point;
            _waypoints[i] = candidate;
        }
    }

    void UpdateWandering()
    {
        // Trainers don't wander — they stand their ground
        if (Type == NPCType.Trainer) return;
        if (_waypoints == null || _waypoints.Length == 0) return;
        if (_isShowingDialogue) return;

        float playerDist = _playerTransform != null
            ? Vector3.Distance(transform.position, _playerTransform.position) : 999f;

        // Stop wandering if player is close
        if (playerDist <= _wanderStopRange)
        {
            _isWandering = false;
            return;
        }

        // Pause between waypoints
        if (!_isWandering)
        {
            _waypointPauseTimer -= Time.deltaTime;
            if (_waypointPauseTimer <= 0)
            {
                _isWandering = true;
                _currentWaypoint = (_currentWaypoint + 1) % _waypoints.Length;
            }
            return;
        }

        // Move toward current waypoint
        Vector3 target = _waypoints[_currentWaypoint];
        Vector3 dir = target - transform.position;
        dir.y = 0;

        if (dir.magnitude < 0.3f)
        {
            _isWandering = false;
            _waypointPauseTimer = UnityEngine.Random.Range(2f, 5f);
            return;
        }

        _spawnRotation = Quaternion.LookRotation(dir);
        Vector3 move = dir.normalized * _wanderSpeed * Time.deltaTime;
        move.y = 0; // Never move vertically — terrain clamp in Update handles Y
        transform.position += move;
    }

    // =========================================================================
    // BEHAVIOR: Ambient Dialogue
    // =========================================================================

    void UpdateAmbientChat()
    {
        if (_isShowingDialogue || DialogueLines.Count == 0 || _playerTransform == null) return;

        float dist = Vector3.Distance(transform.position, _playerTransform.position);
        if (dist > AMBIENT_CHAT_RANGE) return;

        _ambientChatTimer -= Time.deltaTime;
        if (_ambientChatTimer > 0) return;

        // Global cooldown — only one NPC talks at a time
        if (Time.time - _lastAmbientChatTime < 8f)
        {
            _ambientChatTimer = UnityEngine.Random.Range(3f, 8f);
            return;
        }

        _lastAmbientChatTime = Time.time;
        _ambientChatTimer = UnityEngine.Random.Range(15f, 30f);

        string line = DialogueLines[UnityEngine.Random.Range(0, DialogueLines.Count)];
        ShowDialogue(line);
        _dismissTimer = 4f; // shorter than click dialogue
    }

    // =========================================================================
    // BEHAVIOR: Trainer Challenge Indicator
    // =========================================================================

    void UpdateChallengeIndicator()
    {
        if (Type != NPCType.Trainer || _playerTransform == null) return;

        float dist = Vector3.Distance(transform.position, _playerTransform.position);
        bool shouldShow = dist <= TrainerChallengeRange * 3f && !IsTrainerOnCooldown();

        if (shouldShow && _challengeIndicator == null)
        {
            var obj = new GameObject("ChallengeIndicator");
            obj.transform.SetParent(transform);
            obj.transform.localPosition = new Vector3(0, NPCHeight + 1f, 0);
            var tm = obj.AddComponent<TextMesh>();
            tm.text = "!";
            tm.fontSize = 64;
            tm.characterSize = 0.1f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.red;
            tm.fontStyle = FontStyle.Bold;
            obj.AddComponent<BillboardLabel>();
            _challengeIndicator = obj;
        }
        else if (!shouldShow && _challengeIndicator != null)
        {
            Destroy(_challengeIndicator);
            _challengeIndicator = null;
        }

        // Bob the "!"
        if (_challengeIndicator != null)
        {
            float y = Mathf.Sin(Time.time * 3f) * 0.15f;
            _challengeIndicator.transform.localPosition = new Vector3(0, NPCHeight + 1f + y, 0);
        }
    }

    // =========================================================================
    // DIALOGUE SYSTEM
    // =========================================================================

    void OnPlayerEnterRange()
    {
        if (Type == NPCType.Friendly && DialogueLines.Count > 0)
        {
            ShowNameBubble();
        }
    }

    void OnPlayerExitRange()
    {
        HideDialogue();
    }

    void StartDialogue()
    {
        _currentLine = 0;

        // Build dialogue sequence based on NPC state
        if (SpiritComms.Instance != null)
        {
            var lines = new List<(string, string, UnityEngine.Sprite, Color)>();
            Color npcColor = new Color(0.5f, 0.85f, 1f); // friendly blue
            Action onComplete = null;

            // Check for daily quest
            if (Type == NPCType.Friendly && QuestManager.Instance != null)
            {
                var quest = QuestManager.Instance.GetDailyQuestForNPC(NPCId);
                if (quest != null && !quest.Completed)
                {
                    if (quest.Progress >= quest.Target)
                    {
                        // Quest complete — turn in
                        lines.Add((NPCName, DialogueLines.Count > 0 ? DialogueLines[0] : "Ah, there you are!", null, npcColor));
                        lines.Add((NPCName, $"Excellent work on \"{quest.Title}\"! Here is your reward.", null, new Color(1f, 0.85f, 0.3f)));
                        onComplete = () => QuestManager.Instance.CompleteDailyQuest(NPCId);
                    }
                    else if (quest.Progress > 0)
                    {
                        // In progress
                        lines.Add((NPCName, DialogueLines.Count > 0 ? DialogueLines[UnityEngine.Random.Range(0, DialogueLines.Count)] : "Good to see you.", null, npcColor));
                        lines.Add((NPCName, $"{quest.Title}: {quest.Progress}/{quest.Target}. Keep at it!", null, new Color(1f, 0.85f, 0.3f)));
                    }
                    else
                    {
                        // Offer quest
                        lines.Add((NPCName, DialogueLines.Count > 0 ? DialogueLines[0] : "I could use your help.", null, npcColor));
                        lines.Add((NPCName, $"Quest: {quest.Title}\n{quest.Description}", null, new Color(1f, 0.85f, 0.3f)));
                        onComplete = () => QuestManager.Instance.AcceptDailyQuest(NPCId);
                    }

                    SpiritComms.Instance.ShowCommSequence(lines, onComplete);
                    return;
                }
            }

            // No quest — just conversation, cycle through lines
            if (DialogueLines.Count > 0)
            {
                string line = DialogueLines[_currentLine];
                lines.Add((NPCName, line, null, npcColor));
                _currentLine = (_currentLine + 1) % DialogueLines.Count;
                SpiritComms.Instance.ShowCommSequence(lines, null);
            }
            return;
        }

        // Fallback: old system
        ShowDialogue(DialogueLines.Count > 0 ? DialogueLines[0] : "...");
    }

    void AdvanceDialogue()
    {
        // With SpiritComms, just start a new dialogue (it queues)
        _currentLine = (_currentLine + 1) % DialogueLines.Count;
        if (SpiritComms.Instance != null)
        {
            var lines = new List<(string, string, UnityEngine.Sprite, Color)>
            {
                (NPCName, DialogueLines[_currentLine], null, new Color(0.5f, 0.85f, 1f))
            };
            SpiritComms.Instance.ShowCommSequence(lines, null);
        }
        else
        {
            ShowDialogue(DialogueLines[_currentLine]);
        }
    }

    void ShowDialogue(string text)
    {
        _isShowingDialogue = true;
        _dismissTimer = AutoDismissTime;

        // Use SpiritComms Star Fox dialogue if available
        if (SpiritComms.Instance != null)
        {
            Color nameColor = Type == NPCType.Trainer
                ? new Color(1f, 0.4f, 0.3f)   // red-orange for trainers
                : new Color(0.5f, 0.85f, 1f);  // light blue for friendly
            SpiritComms.Instance.ShowNPCDialogue(NPCName, text, nameColor);
            return;
        }

        // Fallback: old bubble system
        EnsureBubble();

        // Typing effect
        var tmp = _activeBubble?.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (tmp != null)
        {
            if (_typingCoroutine != null) StopCoroutine(_typingCoroutine);
            _typingCoroutine = StartCoroutine(TypeText(tmp, $"<b>{NPCName}</b>\n", text));
        }

        // Name label turns gold during conversation
        if (_nameLabel != null) _nameLabel.color = new Color(1f, 0.9f, 0.3f);

        Debug.Log($"[NPC] {NPCName}: {text}");
    }

    IEnumerator TypeText(TMPro.TextMeshProUGUI tmp, string prefix, string text)
    {
        tmp.text = prefix;
        for (int i = 0; i < text.Length; i++)
        {
            tmp.text = prefix + text[..(i + 1)];
            yield return _typingDelay;
        }
    }

    void ShowNameBubble()
    {
        EnsureBubble();
        var tmp = _activeBubble?.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.text = $"<b>{NPCName}</b>\n<size=80%>[Press E to talk]</size>";
        }
    }

    void EnsureBubble()
    {
        if (SpeechBubblePrefab != null && _activeBubble == null)
        {
            Transform anchor = SpeechBubbleAnchor != null ? SpeechBubbleAnchor : transform;
            _activeBubble = Instantiate(SpeechBubblePrefab, anchor);
        }
    }

    void HideDialogue()
    {
        _isShowingDialogue = false;
        if (_typingCoroutine != null) { StopCoroutine(_typingCoroutine); _typingCoroutine = null; }
        if (_activeBubble != null) { Destroy(_activeBubble); _activeBubble = null; }
        if (_nameLabel != null) _nameLabel.color = _nameLabelOriginalColor;
    }

    // =========================================================================
    // TRAINER BATTLES
    // =========================================================================

    void ShowChallengePrompt()
    {
        ShowDialogue(DialogueLines.Count > 0 ? DialogueLines[0] : "You dare challenge me?");
    }

    bool IsTrainerOnCooldown()
    {
        if (Type != NPCType.Trainer) return false;
        var data = MainPlayerData.Instance;
        if (data == null) return false;

        string key = $"trainer_{NPCId}";
        if (data.CompletedQuests.Contains($"{key}_{GetTodayId()}"))
            return true;

        return false;
    }

    void StartTrainerBattle()
    {
        if (TrainerTeam == null || TrainerTeam.Length == 0)
        {
            Debug.LogWarning($"[NPC] Trainer {NPCName} has no team configured.");
            return;
        }

        // Show pre-battle dialogue via SpiritComms, then start battle
        if (SpiritComms.Instance != null)
        {
            string[] challenges = {
                $"You dare challenge me? Let's see what you've got!",
                $"I've been waiting for a worthy opponent. Prepare yourself!",
                $"Think you can handle my team? Let's find out!",
                $"No one passes through here without a fight!",
                $"Your Spiritkin look strong... but mine are stronger!"
            };
            string challenge = challenges[UnityEngine.Random.Range(0, challenges.Length)];
            var lines = new List<(string, string, UnityEngine.Sprite, Color)>
            {
                (NPCName, challenge, null, new Color(1f, 0.4f, 0.3f))
            };
            SpiritComms.Instance.ShowCommSequence(lines, () => ExecuteTrainerBattle());
            return;
        }

        ExecuteTrainerBattle();
    }

    void ExecuteTrainerBattle()
    {
        Debug.Log($"[NPC] Trainer battle: {NPCName} — Team: [{string.Join(", ", TrainerTeam)}]");

        // Mark trainer as fought today
        var data = MainPlayerData.Instance;
        string todayKey = $"trainer_{NPCId}_{GetTodayId()}";
        if (!data.CompletedQuests.Contains(todayKey))
            data.CompletedQuests.Add(todayKey);

        // Build card IDs list
        var cardIds = TrainerTeam.Select(id => id.ToString()).ToList();

        // Build override lineup from int IDs → Card references
        var lineup = new List<Card>();
        foreach (var id in TrainerTeam)
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
        // Scale trainer team size by region (early = smaller teams)
        int trainerTeamSize = Region switch
        {
            "frost_valley" => 1,
            "rolling_hills" => 2,
            "volcanic_isles" => 2,
            "dark_castle" => 3,
            _ => 3
        };
        if (lineup.Count > trainerTeamSize)
            lineup = lineup.GetRange(0, trainerTeamSize);

        GameManager.OverrideEnemyLineup = lineup;
        GameManager.IsTrainerBattle = true;

        // Find a fallback encounter template
        Encounter fallbackEnc = null;
        foreach (var pair in AssetManager.IdItems)
        {
            if (pair.Value is Encounter e) { fallbackEnc = (Encounter)pair.Value; break; }
        }
        if (fallbackEnc == null)
        {
            var encPrefabs = Resources.LoadAll<GameObject>("Prefabs/Encounters");
            foreach (var p in encPrefabs)
            {
                var e = p.GetComponent<Encounter>();
                if (e != null) { fallbackEnc = e; break; }
            }
        }

        if (fallbackEnc == null)
        {
            Debug.LogError("[NPC] No encounter prefab found!");
            return;
        }

        string locationId = $"trainer_{NPCName}";

        // Ensure WorldLocationSaved exists
        var worldSaved = MainPlayerData.Instance.WorldSaved;
        if (worldSaved.GetLocationSaved(locationId) == null)
            worldSaved.SavedLocations.Add(new WorldLocationSaved { LocationId = locationId });

        var info = new GameInfo
        {
            LocationId = locationId,
            EncounterId = fallbackEnc.AssetId,
        };

        Debug.Log($"[NPC] Starting battle via GameLoader: {lineup.Count} enemy cards");

        // Use GameLoader which handles scene switching (GameManager.Instance is null in World scene)
        GameLoader.StartGame(info);

        // Subscribe to battle end for rewards
        OverworldIntegration.OnTrainerDefeated -= OnTrainerDefeated;
        OverworldIntegration.OnTrainerDefeated += OnTrainerDefeated;
    }

    void OnTrainerDefeated(string trainerName, bool won)
    {
        OverworldIntegration.OnTrainerDefeated -= OnTrainerDefeated;

        if (won && trainerName == NPCName)
        {
            var data = MainPlayerData.Instance;
            data.AddGold(GoldReward);
            ProfessionManager.Instance?.AddProfessionXP(ProfessionXPType.Combat, XPReward);

            // First-time victory bonus
            string firstWinKey = $"trainer_first_{NPCId}";
            if (!data.CompletedQuests.Contains(firstWinKey))
            {
                data.CompletedQuests.Add(firstWinKey);
                int firstWinGold = GoldReward; // double gold for first win
                data.AddGold(firstWinGold);
                OverworldIntegration.Instance?.ShowNotification($"First Victory vs {NPCName}! +{firstWinGold} bonus gold!");

                // Title reward for defeating The Exile (toughest trainer)
                if (GoldReward >= 100 && !data.Titles.Contains("Champion"))
                {
                    data.Titles.Add("Champion");
                    OverworldIntegration.Instance?.ShowNotification("Title earned: Champion!");
                }
            }

            Debug.Log($"[NPC] Defeated {NPCName}! +{GoldReward} gold, +{XPReward} XP");
            OverworldIntegration.Instance?.ShowNotification($"Defeated {NPCName}! +{GoldReward} gold");

            // Notify quest manager for battle-related quests
            QuestManager.Instance?.ReportDailyQuestProgress(DailyQuestType.WinBattle, 1, Region);
            QuestManager.Instance?.ReportEliteDefeated();

            MainPlayerData.SaveToCloud();
        }
    }

    // =========================================================================
    // TRAINER AGGRO — chase player and initiate battle
    // =========================================================================

    const float TRAINER_DETECT_RANGE = 15f;
    const float TRAINER_BATTLE_RANGE = 2.5f;
    const float TRAINER_CHASE_SPEED = 3.5f;
    bool _isChasing;

    void UpdateTrainerAggro()
    {
        if (_playerTransform == null || IsTrainerOnCooldown()) return;

        // Don't aggro if player is invincible (just returned from battle)
        var wp = WorldManager.Instance?.WorldPlayer;
        if (wp != null && wp.IsInvincible) { _isChasing = false; return; }

        // Don't aggro if player is in town safe zone
        Vector3 townCenter = new(-15f, 0, 0);
        float townRadius = 35f;
        Vector3 playerPos = _playerTransform.position;
        float playerToTown = Vector2.Distance(
            new Vector2(playerPos.x, playerPos.z),
            new Vector2(townCenter.x, townCenter.z));
        if (playerToTown < townRadius) { _isChasing = false; return; }

        float dist = Vector3.Distance(transform.position, _playerTransform.position);

        // Detect player — start chasing
        if (dist < TRAINER_DETECT_RANGE && !_isChasing)
        {
            _isChasing = true;
        }

        // Lost player — stop chasing
        if (dist > TRAINER_DETECT_RANGE * 2f)
        {
            _isChasing = false;
        }

        if (!_isChasing) return;

        // Close enough — trigger battle
        if (dist < TRAINER_BATTLE_RANGE)
        {
            _isChasing = false;
            StartTrainerBattle();
            return;
        }

        // Chase — move toward player
        Vector3 dir = (_playerTransform.position - transform.position);
        dir.y = 0;
        if (dir.sqrMagnitude > 0.01f)
        {
            Vector3 move = dir.normalized * TRAINER_CHASE_SPEED * Time.deltaTime;
            move.y = 0;
            transform.position += move;
        }
    }

    // =========================================================================
    // TERRAIN HEIGHT — use terrain heightmap directly, no raycast
    // =========================================================================

    static float GetTerrainHeight(Vector3 worldPos)
    {
        try
        {
            if (WorldManager.Instance == null || WorldManager.Instance.Terrains == null) return float.NaN;

            foreach (var terrain in WorldManager.Instance.Terrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                var tPos = terrain.transform.position;
                var tSize = terrain.terrainData.size;
                if (tSize.x <= 0 || tSize.z <= 0) continue;

                if (worldPos.x >= tPos.x && worldPos.x <= tPos.x + tSize.x &&
                    worldPos.z >= tPos.z && worldPos.z <= tPos.z + tSize.z)
                {
                    return terrain.SampleHeight(worldPos) + tPos.y;
                }
            }
        }
        catch (System.Exception) { }
        return float.NaN;
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    static string GetTodayId() => DateTime.UtcNow.ToString("yyyyMMdd");

    /// <summary>Get all friendly NPCs for a given region.</summary>
    public static List<NPCData> GetFriendlyNPCsInRegion(string region)
    {
        return NPC_DATA.Where(n => n.Region == region && n.Type == NPCType.Friendly).ToList();
    }

    /// <summary>Get all trainers for a given region.</summary>
    public static List<NPCData> GetTrainersInRegion(string region)
    {
        return NPC_DATA.Where(n => n.Region == region && n.Type == NPCType.Trainer).ToList();
    }

    void OnDestroy()
    {
        OverworldIntegration.OnTrainerDefeated -= OnTrainerDefeated;
    }
}

// =========================================================================
// ENUMS & DATA CLASSES
// =========================================================================

public enum NPCType
{
    Friendly,
    Trainer
}

public enum NPCRole
{
    Wisdom,
    Crafting,
    Knowledge,
    Gathering,
    Healing,
    Exploration,
    FireCrafting,
    Trading,
    DarkLore,
    Trainer
}

[Serializable]
public class NPCData
{
    public string Id;
    public string Name;
    public string Region;
    public NPCType Type;
    public NPCRole Role;
    public List<string> Dialogue = new();
    public int[] Team;          // trainer only
    public int GoldReward;      // trainer only
    public int XPReward;        // trainer only
}
