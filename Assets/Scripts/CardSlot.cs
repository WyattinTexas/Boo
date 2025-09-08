
//=================================================================
// 2. SIMPLE CARD SLOT
using UnityEngine;
using UnityEngine.UI;

public class CardSlot : MonoBehaviour
{
    [Header("Slot Settings")]
    public string slotType; // "InPlay", "Sideline", or "Collection"
    
    [Header("Visual")]
    public Image slotBackground;
    public Color emptyColor = Color.gray;
    public Color filledColor = Color.white;
    
    private CardUI currentCard;
    
    public CardUI GetCard() => currentCard;
    public bool IsEmpty() => currentCard == null;
    public int GetCardID() => currentCard != null ? currentCard.GetCardID() : -1;
    
    public void SetCard(CardUI card)
    {
        currentCard = card;
        
        if (card != null)
        {
            card.transform.SetParent(transform);
            card.transform.localPosition = Vector3.zero;
            card.transform.localScale = Vector3.one;
        }
        
        UpdateVisuals();
    }
    
    public CardUI RemoveCard()
    {
        CardUI removedCard = currentCard;
        currentCard = null;
        UpdateVisuals();
        return removedCard;
    }
    
    private void UpdateVisuals()
    {
        if (slotBackground != null)
        {
            slotBackground.color = IsEmpty() ? emptyColor : filledColor;
        }
    }
}
