using PredictedDice;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public static List<Player> Players = new();

    /// <summary>
    /// When set, overrides the encounter's card lineup for the opponent.
    /// Used by OverworldIntegration to make roaming enemies fight with their actual cards.
    /// Cleared after battle ends.
    /// </summary>
    public static List<Card> OverrideEnemyLineup = null;

    public static event Action<GameManager> OnGameStarted;
    public static event Action<GameEventCtx> OnGameEvent;
    public static event Action<RollWinCtx> OnWinResult;
    public static event Action<RollWinCtx> OnNextWinResult;

    public int DefaultRollCount = 3;
    public float Gravity = -9.81f;
    [Space]
    public Player ClientPlayer;
    public Player EnemyPlayer;
    [Space]
    public TextMeshProUGUI RollDamageText;
    public Button CancelReplaceBtn;
    public Button CancelRerollBtn;
    [Space]
    public EncounterPanel EncounterPanel;
    [Space]
    public Panel EndGamePanel;
    public Button LeaveBtn;
    
    [HideInEditorMode, ReadOnly] public Canvas Canvas;
    [HideInEditorMode, ReadOnly] public GraphicRaycaster Caster;

    // Clean battle UI overlay
    GameObject _battleBg;
    TextMeshProUGUI _battleHeader;
    TextMeshProUGUI _youLabel;
    TextMeshProUGUI _foeLabel;
    Button _fightBtn;
    Button _runBtn;
    [Space]
    [HideInEditorMode, ReadOnly] public bool HasGameStarted = false;
    [HideInEditorMode, ReadOnly] public GameInfo GameInfo = null;
    [HideInEditorMode, ReadOnly] public List<GameAction> GameActions = new();
    [HideInEditorMode, ReadOnly] public GameAction CurrentGameAction = null;
    [Space]
    [HideInEditorMode, ReadOnly] public Player LastReplacedPlayer = null;

    public List<Func<Card, HealthChangeCtx, IEnumerator>> BeforeCardHealthChangeFuncs = new();

    public bool IsCardAttacking => Players.Exists(x => x.ActiveCard != null && x.ActiveCard.IsAttackCorActive);

    [NonSerialized] public RollWinCtx CurrentWinContext = null;

    ManagedCoroutine startCor = null;
    ManagedCoroutine gameActionCor = null;

    bool gameOver = false;

    void Awake()
    {
        Instance = this;

        startCor = new(this);
        gameActionCor = new(this);

        Canvas = GetComponentInParent<Canvas>();
        Caster = GetComponentInParent<GraphicRaycaster>();

        LeaveBtn.onClick.AddListener(LeaveGame);

        CancelReplaceBtn.gameObject.SetActiveCheck(false);
        CancelReplaceBtn.onClick.AddListener(OnCancelReplaceClick);
        CancelRerollBtn.gameObject.SetActiveCheck(false);
        CancelRerollBtn.onClick.AddListener(OnCancelRerollClick);

        Physics.gravity = new(0, Gravity, 0);

        Players.Clear();
        Players.Add(ClientPlayer);
        Players.Add(EnemyPlayer);
        
        int chosenWhite = StaticClass.Random.Next(2);
        for (int i = 0; i < Players.Count; i++)
        {
            var player = Players[i];
            player.Set(i, i == chosenWhite, this);

            player.OnAllowActiveReplace += OnAllowActiveReplace;
        }
    }

    void OnEnable()
    {
        DiceRoller.OnAllDiceRolled += OnAllDiceRolled;
        DiceRoller.OnRollFinished += OnRollFinished;
    }
    void OnDisable()
    {
        DiceRoller.OnAllDiceRolled -= OnAllDiceRolled;
        DiceRoller.OnRollFinished -= OnRollFinished;
    }

    void Start()
    {
    }

    void Update()
    {
        if (MainPlayerData.Instance != null)
            MainPlayerData.Instance.WorldSaved.UpdateTime(Time.deltaTime);

        if (CurrentWinContext != null && !CurrentWinContext.IsTie && CurrentWinContext.LoserHealthChange != null)
            RollDamageText.text = $"{Mathf.Abs(CurrentWinContext.LoserHealthChange.Change.Amount)} Damage";
    }

    void OnDestroy()
    {
        //DiceRoller.Instance.ClearDice();
    }

    /// <summary>Whether the current game is a trainer battle (uses OverrideEnemyLineup, skip encounter dialogs).</summary>
    public static bool IsTrainerBattle = false;
    /// <summary>Whether the current game is an overworld wild battle (skip encounter dialogs on end).</summary>
    public static bool IsWildBattle = false;

    public void StartGame(GameInfo info)
    {
        HasGameStarted = false;
        GameInfo = info;

        var encounter = info.Encounter();
        // For trainer battles or encounters with no Start dialog, skip the encounter panel entirely.
        bool hasStartDialog = encounter != null
            && encounter.DialogSquences != null
            && encounter.DialogSquences.Exists(x => x.Type == DialogType.Start && x.Dialogs != null && x.Dialogs.Count > 0);

        if (IsTrainerBattle || IsWildBattle || !hasStartDialog)
        {
            Debug.Log($"[GameManager] Skipping encounter dialog (trainer={IsTrainerBattle}, wild={IsWildBattle}, hasStartDialog={hasStartDialog})");
            startCor.Start(StartGameCor);
        }
        else
        {
            //Start encounter, and start game after.
            EncounterPanel.ShowEncounter(encounter, DialogType.Start, OnStartEncounterEnd);
        }
    }
    void OnStartEncounterEnd(EncounterPanel panel)
    {
        EncounterPanel.Open(false);
        startCor.Start(StartGameCor);
    }
    IEnumerator StartGameCor(object[] args)
    {
        var info = GameInfo;
        Debug.Log($"Starting game for encounter: {info.Encounter()}!");
        //Now if player leaves, penalty will be applied.
        MainPlayerData.Instance.InGameInfo = info;

        var locationSaved = MainPlayerData.Instance.WorldSaved.GetLocationSaved(info.LocationId);
        if (locationSaved == null)
        {
            Debug.LogError($"No location saved for location id: {info.LocationId}!");
            EndGame(null);
            yield break;
        }
        //If no lineup seed, random it, and save.
        if (locationSaved.LineupSeed == 0)
        {
            locationSaved.LineupSeed = StaticClass.RandomSeed;
            MainPlayerData.Save();
        }

        var encounter = info.Encounter();
        //Start music (null-safe — trainer fallback encounters may lack music).
        var musicInfo = encounter != null ? encounter.GetMusicInfo() : null;
        if (musicInfo != null)
            AudioManager.PlayMusic(musicInfo);

        //Spawn client cards — ensure player has cards to fight with.
        var playerCardIds = MainPlayerData.Instance.SlottedCardIds;
        if (playerCardIds == null || playerCardIds.Count == 0)
        {
            Debug.LogWarning("[GameManager] Player has no SlottedCardIds! Granting starter Spiritkin.");
            playerCardIds = new List<string>();
            // Try Castle Guards (39), Snorton (66), Gary (91) — then fallback to any
            int[] starterIds = { 39, 66, 91 };
            int needed = MainPlayerData.Instance.EffectiveTeamSize;
            foreach (int id in starterIds)
            {
                if (playerCardIds.Count >= needed) break;
                var entry = AllCardsData.FindById(id);
                if (!entry.HasValue) continue;
                foreach (var pair in AssetManager.Cards)
                {
                    if (pair.Value.CardName.Equals(entry.Value.Name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        playerCardIds.Add(pair.Key);
                        MainPlayerData.Instance.SetOwned(pair.Key, true);
                        break;
                    }
                }
            }
            // Fallback if none found
            if (playerCardIds.Count == 0)
            {
                foreach (var pair in AssetManager.Cards)
                {
                    if (playerCardIds.Count >= needed) break;
                    playerCardIds.Add(pair.Key);
                    MainPlayerData.Instance.SetOwned(pair.Key, true);
                }
            }
            MainPlayerData.Instance.SlottedCardIds = playerCardIds;
            MainPlayerData.Save();
        }

        // Cap player team to EffectiveTeamSize (1 before sideline unlock, 3 after)
        int effectiveSize = MainPlayerData.Instance.EffectiveTeamSize;
        var cappedPlayerIds = playerCardIds.Count > effectiveSize
            ? playerCardIds.GetRange(0, effectiveSize) : playerCardIds;
        yield return ClientPlayer.SpawnCards(cappedPlayerIds);

        //Roll opponent cards.
        List<Card> opponentCards;
        if (OverrideEnemyLineup != null && OverrideEnemyLineup.Count >= 1)
        {
            Debug.Log($"[GameManager] Using overridden enemy lineup: [{string.Join(", ", OverrideEnemyLineup.ConvertAll(c => c.CardName))}]");
            opponentCards = OverrideEnemyLineup;
        }
        else
        {
            var opponentLineup = encounter.GetCardLineup(new()
            {
                Rand = new(locationSaved.LineupSeed),
            });
            if (opponentLineup == null || opponentLineup.SlottedCards.Count < CardLineup.LineupSize)
            {
                EndGame(null);
                yield break;
            }
            opponentCards = opponentLineup.SlottedCards;
        }
        //Spawn opponent cards.
        yield return EnemyPlayer.SpawnCards(opponentCards);

        // Build the clean battle UI overlay
        BuildBattleUI();

        //Must be after the cards are slotted.
        HasGameStarted = true;
        //Entered play on start cards.
        //ClientPlayer.ActiveCard.EnteredPlay();
        //EnemyPlayer.ActiveCard.EnteredPlay();

        //Do any entered play actions.
        //DoGameActions(StartTurn);
        OnGameStarted?.InvokeSafe(nameof(OnGameStarted), this);
        StartTurn();

        startCor.OnCorEnd();
        yield return null;
    }

    // =========================================================================
    // CLEAN BATTLE UI — light background, header, labels, FIGHT/RUN
    // =========================================================================

    void BuildBattleUI()
    {
        if (Canvas == null) return;
        var canvasTransform = Canvas.transform;

        // ── Light background — fully opaque to cover camera skybox ──
        _battleBg = new GameObject("BattleBG", typeof(RectTransform), typeof(Image));
        _battleBg.transform.SetParent(canvasTransform, false);
        _battleBg.transform.SetAsFirstSibling(); // Behind everything
        var bgRect = _battleBg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        _battleBg.GetComponent<Image>().color = new Color(0.92f, 0.90f, 0.87f, 1f); // Warm off-white, FULLY OPAQUE
        _battleBg.GetComponent<Image>().raycastTarget = false;

        // ── Reposition card slots: player LEFT, enemy RIGHT ──
        RepositionCardSlots();

        // ── Battle header — "[Trainer] challenges you!" ──
        var headerGO = new GameObject("BattleHeader", typeof(RectTransform), typeof(TextMeshProUGUI));
        headerGO.transform.SetParent(canvasTransform, false);
        _battleHeader = headerGO.GetComponent<TextMeshProUGUI>();
        _battleHeader.fontSize = 28;
        _battleHeader.fontStyle = FontStyles.Bold;
        _battleHeader.color = new Color(0.15f, 0.15f, 0.15f);
        _battleHeader.alignment = TextAlignmentOptions.Center;
        _battleHeader.enableWordWrapping = false;
        var headerRect = headerGO.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.sizeDelta = new Vector2(0, 50);
        headerRect.anchoredPosition = new Vector2(0, -10);

        // Set header text
        string enemyName = EnemyPlayer.ActiveCard != null ? EnemyPlayer.ActiveCard.CardName : "???";
        if (IsTrainerBattle && GameInfo.LocationId.StartsWith("trainer_"))
        {
            string trainerName = GameInfo.LocationId.Substring("trainer_".Length);
            _battleHeader.text = $"{trainerName} challenges you!";
        }
        else
            _battleHeader.text = $"Wild {enemyName} appears!";

        // ── "YOU" label — below player card (left side) ──
        var youGO = new GameObject("YouLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        youGO.transform.SetParent(canvasTransform, false);
        _youLabel = youGO.GetComponent<TextMeshProUGUI>();
        _youLabel.text = "YOU";
        _youLabel.fontSize = 16;
        _youLabel.fontStyle = FontStyles.Bold;
        _youLabel.color = new Color(0.4f, 0.4f, 0.45f);
        _youLabel.alignment = TextAlignmentOptions.Center;
        var youRect = youGO.GetComponent<RectTransform>();
        youRect.anchorMin = new Vector2(0.28f, 0.42f);
        youRect.anchorMax = new Vector2(0.28f, 0.42f);
        youRect.sizeDelta = new Vector2(80, 24);

        // ── "FOE" label — below enemy card (right side) ──
        var foeGO = new GameObject("FoeLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        foeGO.transform.SetParent(canvasTransform, false);
        _foeLabel = foeGO.GetComponent<TextMeshProUGUI>();
        _foeLabel.text = "FOE";
        _foeLabel.fontSize = 16;
        _foeLabel.fontStyle = FontStyles.Bold;
        _foeLabel.color = new Color(0.4f, 0.4f, 0.45f);
        _foeLabel.alignment = TextAlignmentOptions.Center;
        var foeRect = foeGO.GetComponent<RectTransform>();
        foeRect.anchorMin = new Vector2(0.72f, 0.42f);
        foeRect.anchorMax = new Vector2(0.72f, 0.42f);
        foeRect.sizeDelta = new Vector2(80, 24);

        // ── FIGHT button (dark, bottom-right) ──
        var fightGO = new GameObject("FightBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        fightGO.transform.SetParent(canvasTransform, false);
        fightGO.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f);
        _fightBtn = fightGO.GetComponent<Button>();
        var fightRect = fightGO.GetComponent<RectTransform>();
        fightRect.anchorMin = new Vector2(1, 0);
        fightRect.anchorMax = new Vector2(1, 0);
        fightRect.pivot = new Vector2(1, 0);
        fightRect.sizeDelta = new Vector2(130, 48);
        fightRect.anchoredPosition = new Vector2(-20, 20);

        var fightTextGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        fightTextGO.transform.SetParent(fightGO.transform, false);
        var fightTMP = fightTextGO.GetComponent<TextMeshProUGUI>();
        fightTMP.text = "FIGHT";
        fightTMP.fontSize = 22;
        fightTMP.fontStyle = FontStyles.Bold;
        fightTMP.color = Color.white;
        fightTMP.alignment = TextAlignmentOptions.Center;
        var ftRect = fightTextGO.GetComponent<RectTransform>();
        ftRect.anchorMin = Vector2.zero;
        ftRect.anchorMax = Vector2.one;

        // ── RUN button (red, next to FIGHT) ──
        var runGO = new GameObject("RunBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        runGO.transform.SetParent(canvasTransform, false);
        runGO.GetComponent<Image>().color = new Color(0.6f, 0.15f, 0.1f);
        _runBtn = runGO.GetComponent<Button>();
        var runRect = runGO.GetComponent<RectTransform>();
        runRect.anchorMin = new Vector2(1, 0);
        runRect.anchorMax = new Vector2(1, 0);
        runRect.pivot = new Vector2(1, 0);
        runRect.sizeDelta = new Vector2(90, 48);
        runRect.anchoredPosition = new Vector2(-160, 20);

        var runTextGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        runTextGO.transform.SetParent(runGO.transform, false);
        var runTMP = runTextGO.GetComponent<TextMeshProUGUI>();
        runTMP.text = "RUN";
        runTMP.fontSize = 22;
        runTMP.fontStyle = FontStyles.Bold;
        runTMP.color = Color.white;
        runTMP.alignment = TextAlignmentOptions.Center;
        var rtRect = runTextGO.GetComponent<RectTransform>();
        rtRect.anchorMin = Vector2.zero;
        rtRect.anchorMax = Vector2.one;

        // Button actions
        _runBtn.onClick.AddListener(() =>
        {
            Debug.Log("[GameManager] Player chose to RUN!");
            EndGame(EnemyPlayer);
        });

        _fightBtn.onClick.AddListener(() =>
        {
            Debug.Log("[GameManager] FIGHT!");
        });

        // Hide sideline slots (1v1 focus, no clutter)
        foreach (var slot in ClientPlayer.SidelineSlots)
            if (slot != null) slot.gameObject.SetActive(false);
        foreach (var slot in EnemyPlayer.SidelineSlots)
            if (slot != null) slot.gameObject.SetActive(false);

        Debug.Log("[GameManager] Clean battle UI built — light bg, cards left/right, FIGHT/RUN");
    }

    /// <summary>Move player's active card to left side, enemy's to right side.</summary>
    void RepositionCardSlots()
    {
        // Player active card — left-center
        if (ClientPlayer.ActiveSlot != null)
        {
            var rect = ClientPlayer.ActiveSlot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.15f, 0.3f);
            rect.anchorMax = new Vector2(0.4f, 0.85f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Enemy active card — right-center
        if (EnemyPlayer.ActiveSlot != null)
        {
            var rect = EnemyPlayer.ActiveSlot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.6f, 0.3f);
            rect.anchorMax = new Vector2(0.85f, 0.85f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Move player dice poses to below player card (left side)
        if (ClientPlayer.DicePosesParent != null)
        {
            var rect = ClientPlayer.DicePosesParent;
            rect.anchorMin = new Vector2(0.15f, 0.08f);
            rect.anchorMax = new Vector2(0.4f, 0.28f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Move enemy dice poses to below enemy card (right side)
        if (EnemyPlayer.DicePosesParent != null)
        {
            var rect = EnemyPlayer.DicePosesParent;
            rect.anchorMin = new Vector2(0.6f, 0.08f);
            rect.anchorMax = new Vector2(0.85f, 0.28f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    public void StartTurn()
    {
        if (gameOver)
            return;

        GameInfo.TurnCount += 1;
        Debug.Log($"STARTING TURN: {GameInfo.TurnCount}");

        Players.ForEach(x => x.OnTurnStarted(this));

        DoGameEvent(new()
        {
            Event = GameEvent.OnBeforeRoll,
        });
        DoGameActions(DiceRoller.Instance.StartDiceRoll);
    }
    void EndTurn()
    {
        RollDamageText.gameObject.SetActiveCheck(false);

        //Replace any dead active cards.
        AddReplacementActions();
        DoGameActions(StartTurn);
    }

    void OnAllDiceRolled(List<Dice> rolled, DiceRoller roller)
    {
        //Calculate win ctx.
        var rollWinCtx = RollWinCtx.GetWinningResult(ClientPlayer.RollResult, EnemyPlayer.RollResult);
        CurrentWinContext = rollWinCtx;

        //Do game event.
        DoGameEvent(new()
        {
            Event = GameEvent.OnAllDiceRolled,
            Args = new() { rolled, roller },
        });
        DoGameActions(DiceRoller.Instance.CheckForRerolls);
    }
    public void OnCheckForRerolls()
    {
        var activeRerollReq = DiceRoller.Instance.ActiveRerollReq;
        bool canCancel = activeRerollReq != null && activeRerollReq.CanBeCancelled;

        //Show cancel reroll btn.
        if (canCancel)
            CancelRerollBtn.interactable = activeRerollReq.Reroller.IsClient;
        CancelRerollBtn.gameObject.SetActiveCheck(canCancel);
    }
    public void OnCancelRerollClick()
    {
        var activeReq = DiceRoller.Instance.ActiveRerollReq;
        if (!activeReq.CanBeCancelled)
            Debug.LogError($"{nameof(OnCancelRerollClick)} although reroll req {nameof(activeReq.CanBeCancelled)} = false!");

        CancelRerollBtn.gameObject.SetActiveCheck(false);

        //Stop all rerollable dice for us.
        DiceRoller.Instance.ClearRerolls(activeReq.Reroller);
        //DiceRoller.Instance.SpawnedDice.ForEach(x => x.CanBeRerolled = false);
        //Recheck.
        DiceRoller.Instance.CheckForRerolls();
    }

    void OnRollFinished(DiceRoller roller)
    {
        var rollWinCtx = CurrentWinContext;

        OnWinResult.InvokeSafe(nameof(OnWinResult), rollWinCtx);
        OnNextWinResult.InvokeSafe(nameof(OnNextWinResult), rollWinCtx);
        OnNextWinResult = null;

        AddGameAction(new("Move and highlight dice", MoveAndHighlightDice));

        if (!rollWinCtx.IsTie)
        {
            Debug.Log($"Roll winner: {rollWinCtx.WinResult.Owner.PlayerIndex}");

            DoGameEvent(new()
            {
                Event = GameEvent.OnBeforeRollDamage,
            });
            DoGameActions(DoRightBeforeRollDamage);
        }
        else
            DoGameActions(EndTurn);
    }

    IEnumerator MoveAndHighlightDice()
    {
        yield return new WaitForSeconds(0.2f);

        //Shrink the dice.
        DiceRoller.Instance.ShrinkDice(true);

        //Wait till all players move their dices to the dice poses.
        yield return this.WhenAll(Players.ConvertAll(x => x.MoveDiceToRolledPoses()).ToArray());

        List<Dice> winningDice = new();
        if (CurrentWinContext.IsTie)
            Players.ForEach(x => winningDice.AddRange(x.RolledDice));
        else
            winningDice.AddRange(CurrentWinContext.WinResult.Owner.RolledDice);

        List<int> highlightFaces = new();
        //Not a tie, highlight the faces that dealt damage.
        if (!CurrentWinContext.IsTie)
        {
            int damageFace = CurrentWinContext.WinResult.FaceCounts[0].Face;
            highlightFaces.Add(damageFace);
        }
        //Tie, highlight all faces.
        else
            highlightFaces.AddRange(winningDice.ConvertAll(x => x.RolledUpwardsFace.Value));

        //Scale up and down faces.
        foreach (var dice in winningDice)
        {
            if (highlightFaces.Contains(dice.RolledUpwardsFace.Value))
                yield return StartCoroutine(StaticClass.DelayAction(0.12f, () => dice.Scale(0.8f, 5f, true)));
        }

        //Show damage.
        if (!CurrentWinContext.IsTie)
            StartCoroutine(ShowRollDamage());

        DoGameEvent(new()
        {
            Event = GameEvent.OnAfterRoll,
            Args = new()
            {
                CurrentWinContext,
            }
        });
    }
    IEnumerator ShowRollDamage()
    {
        yield return new WaitForSeconds(0.1f);
        RollDamageText.color = CurrentWinContext.WinPlayer.IsClient ? Color.green : Color.red;
        RollDamageText.gameObject.SetActive(true);
    }

    void DoRightBeforeRollDamage()
    {
        DoGameEvent(new()
        {
            Event = GameEvent.OnRightBeforeRollDamage,
        });
        DoGameActions(DoRollDamage);
    }

    void DoRollDamage()
    {
        //Make sure we still have damage, and deal it.
        if (CurrentWinContext.LoserHealthChange.Change.Amount != 0)
            AddGameAction(new("Doing roll damage", RollDamageCor));
        DoGameActions(OnAfterRollDamage);
    }
    IEnumerator RollDamageCor()
    {
        //Card attack.
        var winCtx = CurrentWinContext;
        yield return winCtx.WinCard.Attack(winCtx.LoseCard, winCtx.LoserHealthChange, (x) =>
        {
            //Disable roll damage text half way through anim.
            RollDamageText.gameObject.SetActive(false);
        });
        winCtx.DealtHealthChange = true;
    }

    void OnAfterRollDamage()
    {
        DoGameEvent(new()
        {
            Event = GameEvent.OnAfterRollDamage,
        });
        DoGameActions(EndTurn);
    }

    public void OnCardEnteredPlay(Card card)
    {
        DoGameEvent(new()
        {
            Event = GameEvent.OnCardEnteredPlay,
            Args = new() { card }
        });
    }
    public void OnCardExitedPlay(Card card)
    {
        DoGameEvent(new()
        {
            Event = GameEvent.OnCardExitedPlay,
            Args = new() { card }
        });
    }
    public void OnCardHealthChanged(Card card, HealthChangeCtx ctx)
    {
        if (card.Health != 0)
        {
            DoGameEvent(new()
            {
                Event = GameEvent.OnCardHealthChanged,
                Args = new() { card, ctx }
            });
        }
    }
    public void OnCardDeath(DeathCtx ctx)
    {
        var card = ctx.Dead;
        var player = card.User;

        DoGameEvent(new()
        {
            Event = GameEvent.OnCardKilled,
            Args = new() { ctx }
        });

        //No more alive sideline cards, player lost, end game.
        if (!player.IsSidelineCardAlive)
            EndGame(GetOpponent(player));
        //During card attack, we wait till the turn ends to replace our card instead.
        //Required so replacement doesn't happen right after damage, causing incorrect events procs like Flora.
        else if (!IsCardAttacking)
            AddReplacementActions(player);

        /*
        //User has no active card, make him replace it.
        if (!player.IsActiveCardAlive)
        {
            //Has a sideline card still alive, replace.
            if (player.AllowActiveReplace(true, player))
            {
            }
            //No more ghosts to replace, player lost, end game.
            else
                EndGame(GetOpponent(player));
        }
        */
    }
    public void OnCardRevive(Card card)
    {

    }

    public void AddReplacementActions(Player checkOnly = null)
    {
        //Add game actions for players that need to replace active cards.
        var needsReplacement = Players.FindAll(x => !x.IsActiveCardAlive);
        if (checkOnly)
            needsReplacement = new List<Player>() { checkOnly };

        foreach (var player in needsReplacement)
        {
            AddGameAction(new($"Player: {player.PlayerIndex}, waiting for active replacement",
                () => player.AllowActiveReplaceCor(true, player)));
        }
    }
    public void OnAllowActiveReplace(bool allow, Player replacer, Player player)
    {
        //Enable ability to cancel replacement if the player's active card is alive.
        //if (replacer.IsClient)
        CancelReplaceBtn.interactable = replacer.IsClient;
        CancelReplaceBtn.gameObject.SetActiveCheck(allow && player.IsActiveCardAlive);

        LastReplacedPlayer = player;

        //Active card changed, we replaced, event.
        if (player.LastReplacedCard != player.ActiveCard)
        {
            DoGameEvent(new()
            {
                Event = GameEvent.OnCardReplaced,
                Args = new() { player.LastReplacedCard, player.ActiveCard, replacer, player }
            });
        }
    }
    public void OnCancelReplaceClick()
    {
        AudioManager.PlayClickSound();

        if (Players.TryFind(x => x.ReplacingActive, out Player replacee))
            replacee.AllowActiveReplace(false, ClientPlayer);
    }
    public void OnActiveSlotted(Player player)
    {
    }

    public void OnSpecialActionClick(Card card)
    {
        DoGameEvent(new()
        {
            Event = GameEvent.OnSpecialActionClicked,
            Args = { card },
        });
    }

    public void DoGameEvent(GameEventCtx ctx)
    {
        Debug.Log($"GAME EVENT: {ctx.Event}");

        //Prioritize.
        var cardTriggers = Players.SelectMany(x => x.Slots.Select(x => x.SlottedCard))
            .Where(x => x != null)
            .SelectMany(x => x.EventHandler.Triggers)
            .OrderByDescending(x => x.GetProcPriority(ctx))
            .ToList();
        //Debug.Log($"Sorted proc list: {cardTriggers.ConvertAll(x => new Pair<string, int>($"'{x.Card.CardName}' trig: {x.Handler.Triggers.IndexOf(x)}", x.GetProcPriority(ctx))).ToStringList()}");
        //Que card trigger actions in order.
        foreach (var trigger in cardTriggers)
            trigger.AddProcAction(ctx);

        OnGameEvent.InvokeSafe(nameof(OnGameEvent), ctx);
    }

    public void AddGameAction(GameAction action, bool insert = false)
    {
        if (insert && CurrentGameAction != null)
        {
            int i = GameActions.IndexOf(CurrentGameAction);
            //Put right after current.
            GameActions.Insert(i + 1, action);
        }
        else
            GameActions.Add(action);
    }

    public void DoGameActions(Action onFinish)
    {
        gameActionCor.Start(GameActionCor, onFinish);
    }
    public IEnumerator GameActionCor(object[] args)
    {
        Action onFinish = (Action)args[0];

        //if (!CurrentWinContext.IsTie)
        //{
        //    var winnerActions = GameActions.FindAll(x => x.Player == CurrentWinContext.WinPlayer);
        //    GameActions.RemoveAll(x => winnerActions.Contains(x));
        //    GameActions.InsertRange(0, winnerActions);
        //}

        //int initialActions = GameActions.Count;
        int completeActions = 0;
        //Do each action, and wait for them to complete.
        for (int i = 0; i < GameActions.Count; i++)
        {
            if (gameOver)
                yield break;

            var action = GameActions[i];
            CurrentGameAction = action;

            if (action.PreCondition != null)
            {
                try
                {
                    if (!action.PreCondition())
                    {
                        //Debug.Log($"Precondition for action '{action.Label}' not met, skipping.");
                        continue;
                    }
                }
                catch (Exception ex) 
                { 
                    Debug.LogError($"Exception when checking precondition for action '{action.Label}': {ex}");
                    continue;
                }
            }

            Debug.Log($"GAME ACTION: '{action.Label}'");
            float startTime = Time.realtimeSinceStartup;
            yield return StartCoroutine(action.Func());

            //Actually did something, add a delay after it.
            if (Time.realtimeSinceStartup - startTime > 0.01f)
                yield return new WaitForSeconds(0.2f);
            completeActions += 1;
        }
        //Once done, clear all actions.
        GameActions.Clear();
        CurrentGameAction = null;

        if (gameOver)
            yield break;

        if (completeActions > 0)
            Debug.Log("GAME ACTIONS COMPLETE!");
        gameActionCor.OnCorEnd();
        onFinish?.Invoke();
    }

    public static Player GetOpponent(Player player)
    {
        return Players.Find(x => x != player);
    }

    [Button]
    public void EndGame(Player winner)
    {
        if (gameOver)
            return;
        gameOver = true;
        Debug.Log($"{nameof(EndGame)}, winner: {winner?.PlayerIndex}!");

        MainPlayerData.Instance.InGameInfo = null;
        GameInfo.ClientWon = winner != null ? winner.IsClient : null;

        //Rewards and penalties.
        OnGameEnded(GameInfo);

        //Stop game actions.
        GameActions.Clear();
        gameActionCor.Reset();

        //Clear dice.
        if (DiceRoller.Instance != null)
            DiceRoller.Instance.ClearDice();

        //Stop music.
        AudioManager.StopMusic();

        // Determine dialog type safely — winner can be null (early exit / forfeit).
        DialogType endDialogType = DialogType.Defeat;
        if (winner != null)
            endDialogType = winner.IsClient ? DialogType.Defeat : DialogType.Victory;

        // For arena battles, report result before cleanup.
        if (ArenaSystem.IsArenaBattle && ArenaSystem.Instance != null)
        {
            bool arenaWon = GameInfo.ClientWon == true;
            Debug.Log($"[GameManager] Arena battle ended: {(arenaWon ? "WIN" : "LOSS")}");
            ArenaSystem.Instance.ReportResult(arenaWon);
        }

        // Always clear override lineup after battle ends
        OverrideEnemyLineup = null;

        // For trainer/wild battles, skip the end encounter panel and leave immediately.
        if (IsTrainerBattle || IsWildBattle)
        {
            Debug.Log($"[GameManager] Overworld battle ended (trainer={IsTrainerBattle}, wild={IsWildBattle}), skipping end dialog.");
            IsTrainerBattle = false;
            IsWildBattle = false;
            LeaveGame();
            return;
        }

        var encounter = GameInfo.Encounter();
        // Check if the encounter has the required end dialog before showing the panel.
        bool hasEndDialog = encounter != null
            && encounter.DialogSquences != null
            && encounter.DialogSquences.Exists(x => x.Type == endDialogType && x.Dialogs != null && x.Dialogs.Count > 0);

        if (hasEndDialog)
        {
            EncounterPanel.ShowEncounter(encounter, endDialogType, OnEndEncounterEnd);
        }
        else
        {
            // No dialog configured — go straight to leaving.
            Debug.Log("[GameManager] No end dialog configured, leaving game directly.");
            LeaveGame();
        }
    }
    public static void OnGameEnded(GameInfo info)
    {
        if (info == null)
            return;

        Debug.Log($"{nameof(OnGameEnded)}: Client Won: {info.ClientWon}");

        var clientWon = info.ClientWon;
        var locationId = info.LocationId;
        var encounter = info.Encounter();

        //Win and lose rewards.
        if (clientWon.HasValue)
        {
            var playerData = MainPlayerData.Instance;
            if (playerData == null)
            {
                Debug.LogError("[GameManager] MainPlayerData.Instance is null in OnGameEnded!");
                return;
            }
            bool alreadyDefeated = encounter != null
                && playerData.WorldSaved.HasDefeatedEncounter(encounter.AssetId, locationId);

            if (clientWon.Value)
            {
                //Set location defeated (only if we have a real encounter).
                bool firstDefeat = false;
                if (encounter != null)
                {
                    firstDefeat = playerData.WorldSaved.DefeatEncounter(encounter.AssetId, locationId, true);

                    //Gain health.
                    if (!alreadyDefeated)
                        playerData.WorldSaved.AddBattleHealth(encounter.WinHealAmount);

                    //Card rewards.
                    bool reducedRewards = !firstDefeat;
                    var rewards = encounter.GetCardRewards(new(), reducedRewards);
                    rewards.ForEach(x => CardManager.RewardCard(x));
                }

                playerData.DefeatedGhostCount++;

                // Sideline unlock notification at 5 wins
                if (playerData.DefeatedGhostCount >= 5 && !playerData.SidelineUnlockNotified)
                {
                    playerData.SidelineUnlockNotified = true;
                    OverworldIntegration.Instance?.ShowNotification("Sideline slots unlocked! You can now build a team of 3!");
                }
            }
            else
            {
                if (encounter != null)
                {
                    //Lose health.
                    if (!alreadyDefeated)
                        playerData.WorldSaved.AddBattleHealth(-encounter.LossDamageAmount);

                    if (encounter.LossGraveyardCardCount > 0)
                    {
                        //Add graveyard choice.
                        GraveyardManager.ChoiceArgs.Add(new()
                        {
                            ChoiceCardIds = new(playerData.SlottedCardIds),
                            ChooseCount = encounter.LossGraveyardCardCount,
                        });
                    }
                }
            }
        }

        //Save data.
        MainPlayerData.Save();

        // === OVERWORLD ONLINE HOOK ===
        OverworldIntegration.OnBattleEnded(info);
    }
    void OnEndEncounterEnd(EncounterPanel panel)
    {
        LeaveGame();
    }
    public void LeaveGame()
    {
        GameLoader.DestroyGame(GameInfo);
    }

    // ═══════ TRAINER ENCOUNTERS ═══════

    /// <summary>Start a trainer battle with the given NPC name and card IDs (int IDs from AllCardsData).</summary>
    public void StartTrainerEncounter(string trainerName, List<string> cardIds)
    {
        Debug.Log($"[GameManager] Starting trainer encounter: {trainerName} with cards [{string.Join(", ", cardIds)}]");

        // Build override lineup by finding cards by their int ID → card name → AssetManager
        var lineup = new List<Card>();
        foreach (var idStr in cardIds)
        {
            if (int.TryParse(idStr, out int cardIntId))
            {
                var entry = AllCardsData.FindById(cardIntId);
                if (entry.HasValue)
                {
                    // Find card in AssetManager by name
                    Card found = null;
                    foreach (var pair in AssetManager.Cards)
                    {
                        if (pair.Value.CardName.Equals(entry.Value.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            found = pair.Value;
                            break;
                        }
                    }
                    if (found != null)
                        lineup.Add(found);
                    else
                        Debug.LogWarning($"[GameManager] Card '{entry.Value.Name}' (id {cardIntId}) not in AssetManager");
                }
            }
        }

        if (lineup.Count < 3)
        {
            Debug.LogWarning($"[GameManager] Only found {lineup.Count}/3 cards for trainer {trainerName}, padding with commons");
            // Pad with whatever cards we can find
            foreach (var pair in AssetManager.Cards)
            {
                if (lineup.Count >= 3) break;
                if (!lineup.Contains(pair.Value))
                    lineup.Add(pair.Value);
            }
        }

        OverrideEnemyLineup = lineup;

        // Find any encounter to use as a template (we override the lineup anyway)
        Encounter fallbackEncounter = null;
        foreach (var pair in AssetManager.IdItems)
        {
            if (pair.Value is Encounter enc)
            {
                fallbackEncounter = enc as Encounter;
                break;
            }
        }

        if (fallbackEncounter == null)
        {
            // Try loading from Resources directly
            var encPrefabs = Resources.LoadAll<GameObject>("Prefabs/Encounters");
            foreach (var p in encPrefabs)
            {
                var enc = p.GetComponent<Encounter>();
                if (enc != null) { fallbackEncounter = enc; break; }
            }
        }

        if (fallbackEncounter == null)
        {
            Debug.LogError("[GameManager] No encounter prefab found anywhere!");
            return;
        }

        string locationId = $"trainer_{trainerName}";

        // Ensure a WorldLocationSaved exists for this trainer
        var worldSaved = MainPlayerData.Instance.WorldSaved;
        if (worldSaved.GetLocationSaved(locationId) == null)
        {
            worldSaved.SavedLocations.Add(new WorldLocationSaved { LocationId = locationId });
        }

        var info = new GameInfo
        {
            LocationId = locationId,
            EncounterId = fallbackEncounter.AssetId,
        };

        Debug.Log($"[GameManager] Trainer battle ready: {lineup.Count} cards, encounter={fallbackEncounter.name}");
        GameLoader.StartGame(info);
    }

    public static Card GetTargetCard(TargetType type, Player player)
    {
        switch (type)
        {
            case TargetType.OurActiveCard:
                return player.ActiveCard;
            case TargetType.EnemyActiveCard:
                return player.Opponent.ActiveCard;
            default:
                return null;
        }
    }
}

[Serializable]
public class GameInfo
{
    //Saved to player data.
    public string LocationId;
    public string EncounterId;
    public int TurnCount = 0;

    public bool? ClientWon = null;

    public Encounter Encounter()
    {
        if (AssetManager.TryGetIdItem(EncounterId, out IdItem item) && item is Encounter encounter)
            return encounter;
        else
        {
            Debug.LogError($"Failed to get encounter for game info! Encounter Id: {EncounterId}");
            return null;
        }
    }
}

public class RollWinCtx
{
    public RollResult WinResult;
    public RollResult LoseResult;

    public FaceCount WinningCount = null;

    public HealthChangeCtx LoserHealthChange = null;
    public bool DealtHealthChange = false;

    public bool IsTie { get { return WinResult == null && LoseResult == null; } }
    public Player WinPlayer => WinResult?.Owner;
    public Player LosePlayer => LoseResult?.Owner;
    public Card WinCard => WinPlayer?.ActiveCard;
    public Card LoseCard => LosePlayer?.ActiveCard;

    public RollWinCtx(RollResult win, RollResult loser, FaceCount winningCount)
    {
        WinResult = win;
        LoseResult = loser;
        WinningCount = winningCount;

        CalculateWinHealthChange();
    }
    void CalculateWinHealthChange()
    {
        if (WinResult == null)
            return;

        int amount = 0;
        //Damage equal to highest count.
        amount -= WinResult.FaceCounts[0].Count;

        var change = new HealthChange()
        {
            Amount = amount,
        };

        LoserHealthChange = new()
        {
            Change = change,
            Applier = WinResult.Owner.ActiveCard,
        };
    }

    public static RollWinCtx GetWinningResult(RollResult r1, RollResult r2)
    {
        //Compare roll results.
        int result = r1.Compare(r2, out FaceCount winningCount);

        if (result > 0)
            return new(r1, r2, winningCount);
        else if (result < 0)
            return new(r2, r1, winningCount);
        //Tie
        else
            return new(null, null, null);
    }

    public void ChangeLoserHealthChange(float amount, bool isMultiplier, CardEventProcCtx eventCtx)
    {
        if (LoserHealthChange == null) 
            return;

        if (isMultiplier)
            LoserHealthChange.Change.ApplyMultiplier(amount);
        else
            LoserHealthChange.Change.Amount += (int)amount;

        //Loser health change can't heal.
        if (LoserHealthChange.Change.Amount > 0)
            LoserHealthChange.Change.Amount = 0;
    }
}

[Serializable]
public class RollResult
{
    /// <summary>In descending order.</summary>
    public List<int> RollFaces = new();
    [Space]
    public List<FaceCount> FaceCounts = new();
    public Dictionary<int, int> DictFaceCounts = new();
    [Space]
    public Player Owner = null;

    public RollResult() { }
    public RollResult(List<Dice> dices, Player owner)
    {
        RollFaces.Clear();
        foreach (var dice in dices)
        {
            //Already set from the roll, use it.
            if (dice.RolledUpwardsFace != null)
                RollFaces.Add(dice.RolledUpwardsFace.Value);
            else
            {
                //Not set, must be a prediction, check the upwards face.
                var upwardsFace = dice.GetUpwardsFace();
                RollFaces.Add(upwardsFace.Value);
            }
        }
        //RollFaces = dices.ConvertAll(x => x.RolledUpwardsFace.Value);
        Owner = owner;
    }

    public void Calculate()
    {
        FaceCounts.Clear();
        DictFaceCounts.Clear();

        //Finding the counts to each face value.
        for (int i = 0; i < RollFaces.Count; i++)
        {
            int face = RollFaces[i];

            //Increment the count of each face.
            DictFaceCounts[face] = DictFaceCounts.TryGetValue(face, out int count) ? count + 1 : 1;
        }

        //Sort by count, then by face.
        var rollCounts = DictFaceCounts
            .Select(x => new FaceCount(x.Key, x.Value))
            .OrderByDescending(x => x.Count)    //Triples > doubles > singles.
            .ThenByDescending(x => x.Face)       //Same count, higher value first.
            .ToList();

        FaceCounts = rollCounts;
        Debug.Log($"Set face counts to: {FaceCounts.ToStringList()}");
    }

    public FaceCount GetCount(int index)
    {
        if (FaceCounts.TryGetIndex(index, out FaceCount faceCount))
            return faceCount;
        else
            return new(0, 0);
    }

    /// <returns>
    /// > 0 win, < 0 lost, == 0 tie.
    /// </returns>
    public int Compare(RollResult result, out FaceCount winningCount)
    {
        winningCount = null;

        int numOfFaceCounts = Mathf.Max(FaceCounts.Count, result.FaceCounts.Count);
        for (int i = 0; i < numOfFaceCounts; i++)
        {
            var f1 = GetCount(i);
            var f2 = result.GetCount(i);

            //Diff counts, then highest count wins.
            int compare = f1.Count.CompareTo(f2.Count);
            if (compare != 0)
            {
                winningCount = (compare > 0) ? f1 : f2;
                return compare;
            }

            //Diff face values, then highest face value wins.
            compare = f1.Face.CompareTo(f2.Face);
            if (compare != 0)
            {
                winningCount = (compare > 0) ? f1 : f2;
                return compare;
            }
        }

        //Tie.
        return 0;
    }
}

[Serializable]
public class FaceCount
{
    public int Face;
    public int Count;

    public FaceCount(int face, int count)
    {
        this.Face = face;
        this.Count = count;
    }

    public override string ToString()
    {
        return $"[F: {Face}, C: {Count}]";
    }
}

[Serializable]
public class GameAction
{
    public string Label;
    public Func<IEnumerator> Func;
    public Func<bool> PreCondition;
    public Player Player;

    public GameAction(string label, Func<IEnumerator> func, Player player = null, Func<bool> preCond = null)
    {
        Label = label;
        Func = func;
        Player = player;
        PreCondition = preCond;
    }
}

public class GameEventCtx
{
    public GameEvent Event;
    public List<object> Args = new();

    public T GetArg<T>(int index)
    {
        if (Args.TryGetIndex(index, out object obj))
            return (T)obj;
        else
            return default;
    }
}

public enum GameEvent
{
    //Max 12
    OnBeforeRoll                = 0,
    OnAllDiceRolled             = 11,
    OnAfterRoll                 = 1,
    OnBeforeRollDamage          = 4,
    OnRightBeforeRollDamage     = 10,
    OnAfterRollDamage           = 5,

    OnCardEnteredPlay           = 2,
    OnCardExitedPlay            = 3,
    OnCardReplaced              = 8,
    OnBeforeCardHealthChanged   = 12,
    OnCardHealthChanged         = 7,
    OnCardKilled                = 6,

    OnSpecialActionClicked      = 9,
}