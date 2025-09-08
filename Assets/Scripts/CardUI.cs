// 1. SIMPLE CARD UI COMPONENT
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class CardUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Card Data")]
    public int cardID;
    
    [Header("Display")]
    public Image cardImage;
    public TextMeshProUGUI cardIDText; // Just shows the ID for now
    
    [Header("Drag Settings")]
    public float dragScale = 1.1f;
    
    private Vector3 originalPosition;
    private Transform originalParent;
    private CanvasGroup canvasGroup;
    private CardLineupManager lineupManager;
    
    public int GetCardID() => cardID;
    
    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }
    
    private void Start()
    {
        lineupManager = FindObjectOfType<CardLineupManager>();
        UpdateDisplay();
    }
    
    public void SetCardID(int id)
    {
        cardID = id;
        UpdateDisplay();
    }
    
    private void UpdateDisplay()
    {
        if (cardIDText != null)
            cardIDText.text = cardID.ToString();
    }
    
    public void OnBeginDrag(PointerEventData eventData)
    {
        originalPosition = transform.position;
        originalParent = transform.parent;
        
        canvasGroup.alpha = 0.8f;
        canvasGroup.blocksRaycasts = false;
        transform.SetAsLastSibling();
        transform.localScale = Vector3.one * dragScale;
    }
    
    public void OnDrag(PointerEventData eventData)
    {
        transform.position = eventData.position;
    }
    
    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        transform.localScale = Vector3.one;
        
        GameObject dropTarget = eventData.pointerCurrentRaycast.gameObject;
        CardSlot targetSlot = null;
        
        if (dropTarget != null)
        {
            targetSlot = dropTarget.GetComponent<CardSlot>();
            if (targetSlot == null)
                targetSlot = dropTarget.GetComponentInParent<CardSlot>();
        }
        
        if (targetSlot != null && lineupManager != null)
        {
            lineupManager.SwapCards(this, targetSlot);
        }
        else
        {
            transform.position = originalPosition;
            transform.SetParent(originalParent);
        }
    }
}
