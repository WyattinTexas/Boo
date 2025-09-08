

// 2. CASTLE COMPONENT
using UnityEngine;

public class OverworldCastle : MonoBehaviour
{
    [Header("Castle Settings")]
    public string castleName;
    public string levelID;
    public OverworldNode nearestNode; // The node this castle connects to
    
    [Header("Visual")]
    public Color highlightColor = Color.cyan;
    private bool isHighlighted = false;
    private Renderer castleRenderer;
    
    private void Start()
    {
        castleRenderer = GetComponent<Renderer>();
        
        // Auto-find nearest node if not assigned
        if (nearestNode == null)
        {
            FindNearestNode();
        }
    }
    
    private void FindNearestNode()
    {
        OverworldNode[] allNodes = FindObjectsOfType<OverworldNode>();
        float closestDistance = Mathf.Infinity;
        
        foreach (var node in allNodes)
        {
            float distance = Vector3.Distance(transform.position, node.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                nearestNode = node;
            }
        }
    }
    
    private void OnMouseEnter()
    {
        SetHighlight(true);
    }
    
    private void OnMouseExit()
    {
        SetHighlight(false);
    }
    
    private void OnMouseDown()
    {
        OverworldManager.Instance?.NavigateToCastle(this);
    }
    
    private void SetHighlight(bool highlight)
    {
        isHighlighted = highlight;
        if (castleRenderer != null)
        {
            castleRenderer.material.color = highlight ? highlightColor : Color.white;
        }
    }
    
    private void OnDrawGizmos()
    {
        // Draw connection to nearest node
        if (nearestNode != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, nearestNode.transform.position);
        }
    }
}