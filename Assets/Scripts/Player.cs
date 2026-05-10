using DG.Tweening;
using PredictedDice;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class Player : MonoBehaviour
{
    public float CardSpawnY = 15;
    public float CardSpawnLerpSpeed = 5;
    public List<CardSlot> Slots = new();
    [Space]
    public float DiceLerpSpeed = 0.5f;
    public RectTransform DicePosesParent;
    [Space]
    public TextMeshProUGUI EventProcInfoPrefab;
    public RectTransform EventProcInfoParent;
    [Space]
    public RectTransform RectTrans;
    [Space]
    [HideInEditorMode, ReadOnly] public int PlayerIndex = 0;
    [HideInEditorMode, ReadOnly] public DiceColor DiceColor = DiceColor.White;
    [Space]
    [HideInEditorMode, ReadOnly] public List<Dice> RolledDice = new();
    [HideInEditorMode, ReadOnly] public List<RectTransform> RolledDicePoses = new();
    [HideInEditorMode, ReadOnly] public RollResult RollResult = new();
    [Space]
    [HideInEditorMode, ReadOnly] public bool ReplacingActive = false;
    [HideInEditorMode, ReadOnly] public List<CardSlot> CanSwapInSlots = new();
    [HideInEditorMode, ReadOnly] public Player Replacer = null;
    [HideInEditorMode, ReadOnly] public Card LastReplaceCausedBy = null;
    [HideInEditorMode, ReadOnly] public Card LastReplacedCard = null;

    [NonSerialized] public List<CardEventProcCtx> CardEventProcCtxs = new();
    
    public CardSlot ActiveSlot => Slots[0];
    public List<CardSlot> SidelineSlots => Slots.GetRange(1, 2);
    public List<CardSlot> AliveSidelineSlots => Slots.GetRange(1, 2).FindAll(x => x.SlottedCard != null && !x.SlottedCard.IsDead);

    public bool IsActiveCardAlive => ActiveCard != null && !ActiveCard.IsDead;
    public Card ActiveCard => ActiveSlot.SlottedCard;
    public bool IsSidelineCardAlive => SidelineCards.Exists(x => !x.IsDead);
    /// <summary>Empty if no slotted sideline cards.</summary>
    public List<Card> SidelineCards => SidelineSlots.Where(x => x.SlottedCard != null).ToList().ConvertAll(x => x.SlottedCard);

    public bool IsClient => GameManager.Instance.ClientPlayer == this;
    public Player Opponent => GameManager.GetOpponent(this);

    public List<Func<Player, int>> OnGetNextRollCount = new();

    /// <summary>First player is the replacer, second is the person being replaced.</summary>
    public event Action<bool, Player, Player> OnAllowActiveReplace;
    public event Action<Card, CardSlot> OnCardSlotted;

    GameManager manager = null;
    
    public void Set(int playerIndex, bool isWhite, GameManager manager)
    {
        PlayerIndex = playerIndex;
        DiceColor = isWhite ? DiceColor.White : DiceColor.Black;
        this.manager = manager;

        foreach (var slot in Slots)
        {
            slot.OnSlotted += Slot_OnSlotted;
            slot.OnUnSlotted += Slot_OnUnSlotted;
        }

        //Get rolled dice poses.
        for (int i = 0; i < DicePosesParent.childCount; i++)
        {
            var rect = DicePosesParent.GetChild(i) as RectTransform;
            if (rect.TryGetComponent(out Image image) && image.enabled)
                image.enabled = false;
            RolledDicePoses.Add(rect);
        }
    }

    public IEnumerator SpawnCards(List<string> cardIds) => SpawnCards(cardIds.ConvertAll(x => AssetManager.Cards[x]));
    public IEnumerator SpawnCards(List<Card> slotCards)
    {
        if (slotCards.Count > Slots.Count)
            Debug.LogError($"Slotted card count: {slotCards.Count} exceeds available slot count: {Slots.Count}!");

        for (int i = 0; i < Slots.Count; i++)
        {
            var slot = Slots[i];
            Card prefab = (i < slotCards.Count) ? slotCards[i] : null;

            // Skip empty slots (supports 1v1 and 2v1 battles)
            if (prefab == null)
                continue;

            //Spawn.
            var spawnedCard = Instantiate(prefab, slot.transform);
            spawnedCard.SetUser(this, manager);
            spawnedCard.OnDragStart += Card_OnDragStart;
            spawnedCard.OnDragEnd += Card_OnDragEnd;

            //Slot.
            slot.Slot(spawnedCard, false);

            //Slide into slot.
            var cardTrans = spawnedCard.transform;
            cardTrans.position = cardTrans.position.SetY(cardTrans.position.y + CardSpawnY);
            while (!cardTrans.position.CloseTo(slot.transform.position, 0.05f))
            {
                cardTrans.position = Vector3.Lerp(cardTrans.position, slot.transform.position, CardSpawnLerpSpeed * Time.deltaTime);
                yield return null;
            }
            cardTrans.position = slot.transform.position;
        }
    }

    public void OnTurnStarted(GameManager manager)
    {
        //Clear all proc infos.
        try
        {
            for (int i = 0; i < CardEventProcCtxs.Count; i++)
            {
                var ctx = CardEventProcCtxs[i];
                var infoText = ctx.SpawnedProcText;
                if (infoText == null)
                    continue;

                if (ctx.ProcTextTurnDuration <= 0)
                {
                    Destroy(infoText.gameObject);
                    CardEventProcCtxs.RemoveAt(i);
                    i--;
                }
                else
                {
                    ctx.ProcTextTurnDuration -= 1;
                    continue;
                }
            }
            //CardEventProcInfos.ForEach(x => Destroy(x.gameObject));
            //CardEventProcInfos.Clear();
            //CardEventProcCtxs.Clear();
        }
        catch (Exception e) { Debug.LogException(e); }

        //In case the active card hasn't called it's enter yet.
        if (IsActiveCardAlive)
            ActiveCard.EnteredPlay();
    }

    public int GetRollCount(bool peek = false)
    {
        int total = GameManager.Instance.DefaultRollCount;

        for (int i = 0; i < OnGetNextRollCount.Count; i++)
        {
            var func = OnGetNextRollCount[i];

            try { total += func(this); }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        if (!peek)
            OnGetNextRollCount.Clear();

        return Mathf.Max(total, 0);
    }

    public void CalculateRollResult()
    {
        RollResult = GetRollResult(RolledDice);
    }
    public RollResult GetRollResult(List<Dice> dices)
    {
        var result = new RollResult(dices, this);
        result.Calculate();
        return result;
    }

    public void OnCardEventsProcced(List<CardEventProcCtx> ctxs)
    {
        foreach (var ctx in ctxs)
        {
            if (ctx.ShowProcText)
            {
                var info = Instantiate(EventProcInfoPrefab, EventProcInfoParent);
                info.gameObject.SetActive(true);
                //Debug.Log($"Spawning proc text info for trigger: {ctx.Trigger.Handler.Triggers.IndexOf(ctx.Trigger)}");

                //Info text.
                if (ctx.ProcText.IsEmpty())
                    info.text = ctx.Card.EventHandler.AbilityName;
                else
                    info.text = $"{ctx.ProcText} {ctx.Card.EventHandler.AbilityName}";
                //Green if us.
                info.color = IsClient ? Color.green : Color.red;
                ctx.SpawnedProcText = info;
                //CardEventProcInfos.Add(info);
            }
        }

        CardEventProcCtxs.AddRange(ctxs);
    }

    public void AiRerollDice()
    {
        var rolledDice = DiceRoller.Instance.RolledDice;
        var rerollReq = DiceRoller.Instance.ActiveRerollReq;
        List<Dice> rerollable = new(rerollReq.RerollDice);
        if (rerollable.Count == 0)
            return;

        List<Dice> rerollDice = new();

        var encounter = GameManager.Instance.GameInfo.Encounter();
        if (encounter && encounter.Difficulty <= Difficulty.Easy)
        {
            //Easy, random choice.
            if (StaticClass.Random.Next(0, 1f) < 0.5f)
                rerollDice = rerollable;
        }
        else
        {
            //Predict who will win the roll.
            var ourResult = GetRollResult(rolledDice.FindAll(x => x.Owner == this));
            var opponent = GameManager.GetOpponent(this);
            var opponentResult = opponent.GetRollResult(rolledDice.FindAll(x => x.Owner == opponent));
            var winCtx = RollWinCtx.GetWinningResult(ourResult, opponentResult);

            //We lose, reroll all possible dice.
            if (winCtx.WinPlayer != this)
                rerollDice = rerollable;
            Debug.Log($"AI reroll dice, predicted that we will win: {winCtx.WinPlayer == this}, gonna reroll {rerollDice.Count} dice.");
        }

        //If we chose to cancel and we can't, force reroll.
        if (rerollDice.Count == 0 && !rerollReq.CanBeCancelled)
            rerollDice = rerollable;

        Action action = () =>
        {
            //Chose to reroll dice.
            if (rerollDice.Count > 0)
            {
                //Respawn dice.
                rerollDice.ForEach(x => DiceRoller.Instance.RerollDice(x));
                //Roll.
                DiceRoller.Instance.RollOpponentDice(false);
            }
            //Cancel.
            else
                GameManager.Instance.OnCancelRerollClick();
        };
        StartCoroutine(StaticClass.DelayAction(StaticClass.Random.Next(0.5f, 1f), action));
    }

    public IEnumerator MoveDiceToRolledPoses()
    {
        var cam = InputManager.Instance.MainCamera;
        var offGround = Vector3.up * DiceRoller.Instance.DicePrefab.Width / 2;
        //Turn all poses into world poses, and move them a bit higher on the screen.
        var endRolledPositions = RolledDicePoses.ConvertAll(x => x.position + offGround);

        List<Pair<Vector3, Quaternion>> startPoses = new();
        List<Pair<Vector3, Quaternion>> endPoses = new();
        for (int i = 0; i < RolledDice.Count; i++)
        {
            //Need the i from RolledDice for the correct world pos.
            var dice = RolledDice[i];

            //Set the starting poses.
            startPoses.Add(new(dice.transform.position, dice.transform.rotation));

            //Set the ending poses.
            var endPos = endRolledPositions[i];

            //End rotation will be the nearest forward rotation to the dices forward, so it doesnt rotate a bunch.
            var upFaceDir = dice.GetFaceDirection(dice.RolledUpwardsFace).normalized;
            var upFaceWorldDir = StaticClass.GetClosestWorldDir(upFaceDir);
            var toUp = Quaternion.FromToRotation(upFaceDir, upFaceWorldDir);

            //Find the new forward and right after the toUp rotation.
            var forward = toUp * dice.transform.forward;
            var right = toUp * dice.transform.right;
            var useVect = Vector3.zero;
            float forwardAngleDist = Mathf.Abs(90f - Vector3.Angle(upFaceWorldDir, forward));
            float rightAngleDist = Mathf.Abs(90f - Vector3.Angle(upFaceWorldDir, right));
            if (forwardAngleDist < rightAngleDist)
                useVect = forward;
            else
                useVect = right;
            var useVectWorldDir = StaticClass.GetClosestWorldDir(useVect);
            var toDir = Quaternion.FromToRotation(useVect, useVectWorldDir);
            //var closestToUp = StaticClass.GetClosestDir(upFaceWorldDir, forward, right);
            //var closestWorldDir = StaticClass.GetClosestWorldDir(closestToUp);
            //var toDir = Quaternion.FromToRotation(closestToUp, closestWorldDir);

            //var right = dice.transform.right;
            //var closestRight = StaticClass.GetClosestWorldDir(right);
            //var toRight = Quaternion.FromToRotation(right, closestRight);

            var combined = toDir * toUp;
            var endRot = combined * dice.transform.rotation;
            //var endRot = Quaternion.LookRotation(dice.GetClosestForwardWorldDir(), dice.GetClosestUpWorldDir());
            //var endRot = Quaternion.LookRotation(dice.GetClosestForwardWorldDir(), dice.GetUpwardsFaceDir());
            endPoses.Add(new(endPos, endRot));
        }

        List<Dice> movingDice = new(RolledDice);
        float timePassed = 0;
        float maxTime = DiceLerpSpeed;
        while (movingDice.Count > 0)
        {
            timePassed += Time.deltaTime;
            float percent = timePassed / maxTime;

            //Move all dice each frame.
            for (int i = 0; i < RolledDice.Count; i++)
            {
                var dice = RolledDice[i];
                if (!movingDice.Contains(dice))
                    continue;
                var startPos = startPoses[i];
                var endPos = endPoses[i];

                var pos = Vector3.Lerp(startPos.Item1, endPos.Item1, percent);
                var rot = Quaternion.Slerp(startPos.Item2, endPos.Item2, percent);
                dice.transform.SetPositionAndRotation(pos, rot);

                //Dice is there, don't need to move it no more.
                if (dice.transform.position == endPos.Item1)
                    movingDice.Remove(dice);
            }

            yield return null;
        }
    }

    /// <returns>If the allow replace is possible.</returns>
    public bool AllowActiveReplace(bool allow, Player replacer, Card causedBy = null, List<Card> canSwapIn = null)
    {
        //Trying to activate, and no alive sideline card, false.
        if (allow && !IsSidelineCardAlive)
            return false;

        if (allow == ReplacingActive)
            return false;
        ReplacingActive = allow;
        Replacer = allow ? replacer : null;
        if (allow)
        {
            LastReplacedCard = ActiveCard;
            LastReplaceCausedBy = causedBy;
        }

        canSwapIn ??= new();
        CanSwapInSlots.Clear();
        CanSwapInSlots.AddRange(canSwapIn.Select(x => x.InSlot));
        //Specified no swap in slots, use our active alive ones.
        if (CanSwapInSlots.Count == 0)
            CanSwapInSlots.AddRange(AliveSidelineSlots);
        //Make sure we aren't forcing un-swappable cards.
        CanSwapInSlots.RemoveAll(x => x.SlottedCard == null || !x.SlottedCard.CanSwapIn());

        //Highlight sideline slots with cards.
        foreach (var slot in SidelineSlots)
        {
            //bool canSwap = allow && !slot.SlottedCard.IsDead && (canSwapIn.Count == 0 || canSwapIn.Contains(slot.SlottedCard));
            bool canSwap = allow && CanSwapInSlots.Contains(slot);
            slot.SetHighlight(canSwap);
            slot.SlottedCard.SetDraggable(canSwap, replacer);
        }
        
        //Game action wait for replacement.
        //if (allow)
        //    manager.AddGameAction(new($"Player: {PlayerIndex}, waiting for active replacement", WaitForReplace), false);

        //Replacer is AI, do replace automatically after delay.
        if (allow && !replacer.IsClient)
            StartCoroutine(StaticClass.DelayAction(StaticClass.Random.Next(0.4f, 1f), () => DoAiReplaceDecision(CanSwapInSlots)));

        OnAllowActiveReplace.InvokeSafe(nameof(OnAllowActiveReplace), allow, replacer, this);
        return true;
    }
    void DoAiReplaceDecision(List<CardSlot> canSwapInSlots)
    {
        //Choose best card to swap in.
        var possibleCards = new List<Card>(canSwapInSlots.ConvertAll(x => x.SlottedCard));

        //Include active slot, so we can decide to do nothing.
        if (ActiveCard && !ActiveCard.IsDead && !possibleCards.Contains(ActiveCard))
            possibleCards.Add(ActiveCard);

        Debug.Log($"AI decision replace possible swap-in cards: {possibleCards.ToStringList()}");
        //No possible, cancel.
        if (possibleCards.Count == 0)
        {
            GameManager.Instance.OnCancelReplaceClick();
            return;
        }

        var swapToCard = possibleCards.RandomValue();
        var encounter = GameManager.Instance.GameInfo.Encounter();
        //Better then easy.
        if (encounter.Difficulty > Difficulty.Easy)
        {
            int maxRng = 2;
            //Less rng with higher diff.
            maxRng -= (int)encounter.Difficulty;

            //var descendingPower = possible.OrderByDescending(x => x.SlottedCard.GetSwapInPowerLevel(maxRng)).ToList();
            var descendingPowerPairs = possibleCards
                .ConvertAll(x => new Pair<Card, float>(x, x.GetSwapInPowerLevel(maxRng)))
                .OrderByDescending(x => x.Item2).ToList();
            Debug.Log($"AI decision swap, possible swaps with power levels: {descendingPowerPairs.ToStringList()}");
            //If it's us, swap in highest power. If it's the enemy, swap in lowest power.
            if (!IsClient)
                swapToCard = descendingPowerPairs.First().Item1;
            else
                swapToCard = descendingPowerPairs.Last().Item1;
        }

        //If it's the active slot, we decided to cancel.
        if (ActiveSlot.SlottedCard == swapToCard)
            GameManager.Instance.OnCancelReplaceClick();
        //Non-active slot, swap in.
        else
            swapToCard.SlotInto(ActiveSlot);
    }

    public IEnumerator AllowActiveReplaceCor(bool allow, Player replacer, Card causedBy = null, List<Card> canSwapIn = null)
    {
        AllowActiveReplace(allow, replacer, causedBy, canSwapIn);

        yield return WaitForReplace();
    }
    public IEnumerator WaitForReplace()
    {
        yield return new WaitUntil(() => !ReplacingActive);

        yield return new WaitForSeconds(0.1f);
    }

    void Card_OnDragStart(Card card)
    {
        SidelineSlots.ForEach(x => x.SetHighlight(false));

        //Highlight active.
        ActiveSlot.SetHighlight(true);
    }
    void Card_OnDragEnd(Card card)
    {
        //Disable active highlight.
        ActiveSlot.SetHighlight(false);

        foreach (var result in MasterUI.Raycast(card.GetDragPos(), manager.Caster))
        {
            //Dragged over our slot.
            if (result.gameObject.TryGetParent(out CardSlot slot) && Slots.Contains(slot))
            {
                //Can only drag over active slot.
                if (ActiveSlot != slot)
                    continue;

                //Try slot into active slot.
                card.SlotInto(slot);
                break;
            }
        }

        //Replacing still active (didn't slot in active), rehighlight sideline slots.
        if (ReplacingActive)
            CanSwapInSlots.ForEach(x => x.SetHighlight(true));
            //AliveSidelineSlots.ForEach(x => x.SetHighlight(true));
    }

    void Slot_OnSlotted(Card card, CardSlot slot)
    {
        if (slot == ActiveSlot)
        {
            if (ReplacingActive)
            {
                card.CanBeDragged = false;

                //Stop active replace.
                AllowActiveReplace(false, card.CanDragByPlayer);
            }

            //If the game hasn't started (cards can be slotted before the enter animation finishes, then don't call yet).
            if (GameManager.Instance.HasGameStarted)
            {
                //Opponent doesn't have a card in play, wait till round start to call enter play.
                if (Opponent.IsActiveCardAlive)
                    card.EnteredPlay();
            }

            manager.OnActiveSlotted(this);
        }
        else
        {
            //Exited play, entered sideline.
            //card.ExitedPlay();
            card.EnteredSideline();
        }

        OnCardSlotted.InvokeSafe(nameof(OnCardSlotted), card, slot);
    }
    void Slot_OnUnSlotted(Card card, CardSlot slot)
    {
        if (ActiveSlot == slot)
        {
            card.ExitedPlay();
        }
    }
}
