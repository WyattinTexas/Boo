using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class CardLineupManager : MonoBehaviour
{
    [Header("Lineup Slots")]
    public CardSlot inPlaySlot;
    public CardSlot[] sidelineSlots = new CardSlot[2];
    
    [Header("Collection")]
    public Transform collectionParent;
    public GameObject cardPrefab;
    
    [Header("Card IDs")]
    public int[] cardIDs = { 1, 2, 3, 4, 5, 6, 7, 8 }; // Your available card IDs
    
    private void Start()
    {
        SetupCollection();
        SetupInitialLineup();
    }
    
    private void SetupCollection()
    {
        foreach (int cardID in cardIDs)
        {
            GameObject cardObj = Instantiate(cardPrefab, collectionParent);
            CardUI cardUI = cardObj.GetComponent<CardUI>();
            cardUI.SetCardID(cardID);
        }
        
        // Setup grid layout for 4 cards per row
        GridLayoutGroup gridLayout = collectionParent.GetComponent<GridLayoutGroup>();
        if (gridLayout == null)
        {
            gridLayout = collectionParent.gameObject.AddComponent<GridLayoutGroup>();
        }
        
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 4;
        gridLayout.spacing = new Vector2(10, 10);
        gridLayout.cellSize = new Vector2(150, 200);
    }
    
    private void SetupInitialLineup()
    {
        // Start with empty lineup - players can drag cards to set it up
    }
    
    public void SwapCards(CardUI draggedCard, CardSlot targetSlot)
    {
        CardSlot sourceSlot = FindSlotContaining(draggedCard);
        if (sourceSlot == null) return;
        
        CardUI targetCard = targetSlot.GetCard();
        
        sourceSlot.RemoveCard();
        if (targetCard != null)
            targetSlot.RemoveCard();
        
        targetSlot.SetCard(draggedCard);
        if (targetCard != null)
            sourceSlot.SetCard(targetCard);
    }
    
    private CardSlot FindSlotContaining(CardUI card)
    {
        if (inPlaySlot.GetCard() == card) return inPlaySlot;
        
        foreach (CardSlot slot in sidelineSlots)
        {
            if (slot.GetCard() == card) return slot;
        }
        
        if (card.transform.parent == collectionParent)
        {
            GameObject tempSlot = new GameObject("TempCollectionSlot");
            tempSlot.transform.SetParent(collectionParent);
            CardSlot slot = tempSlot.AddComponent<CardSlot>();
            slot.SetCard(card);
            return slot;
        }
        
        return null;
    }
    
    // Get current lineup IDs
    public int GetInPlayCardID()
    {
        return inPlaySlot.GetCardID();
    }
    
    public int[] GetSidelineCardIDs()
    {
        int[] sidelineIDs = new int[sidelineSlots.Length];
        for (int i = 0; i < sidelineSlots.Length; i++)
        {
            sidelineIDs[i] = sidelineSlots[i].GetCardID();
        }
        return sidelineIDs;
    }
    
    // Debug method to print current lineup
    [ContextMenu("Print Current Lineup")]
    public void PrintCurrentLineup()
    {
        Debug.Log($"In Play: {GetInPlayCardID()}");
        int[] sideline = GetSidelineCardIDs();
        Debug.Log($"Sideline: [{sideline[0]}, {sideline[1]}]");
    }
} // This closing brace was missing