using UnityEngine;
using System.Collections.Generic;

public class OverworldNode : MonoBehaviour
{
    [Header("Node Settings")]
    public List<OverworldNode> connectedNodes = new List<OverworldNode>();

    [Header("Visual Settings")]
    public bool showConnections = true;
    public Color nodeColor = Color.yellow;
    public Color connectionColor = Color.white;
    public bool useSprite = true;

    private SpriteRenderer spriteRenderer;

    // Pathfinding properties
    [HideInInspector] public float gCost; // Distance from start
    [HideInInspector] public float hCost; // Distance to target
    [HideInInspector] public float fCost { get { return gCost + hCost; } }
    [HideInInspector] public OverworldNode parent;

    private void Start()
    {
        SetupSpriteRenderer();
    }

    private void SetupSpriteRenderer()
    {
        if (useSprite)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
            {
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }

            // Create a simple circle sprite if none assigned
            if (spriteRenderer.sprite == null)
            {
                spriteRenderer.sprite = CreateCircleSprite();
            }

            spriteRenderer.color = nodeColor;
            spriteRenderer.sortingOrder = 10; // Ensure nodes appear above map
        }
    }

    private Sprite CreateCircleSprite()
    {
        // Create a simple white circle texture
        int size = 32;
        Texture2D texture = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.4f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                if (distance <= radius)
                {
                    pixels[y * size + x] = Color.white;
                }
                else
                {
                    pixels[y * size + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private void OnDrawGizmos()
    {
        if (!showConnections) return;

        // Only draw gizmos if not using sprites, or in addition to sprites
        if (!useSprite)
        {
            // Draw node
            Gizmos.color = nodeColor;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }

        // Draw connections
        Gizmos.color = connectionColor;
        foreach (var node in connectedNodes)
        {
            if (node != null)
            {
                Gizmos.DrawLine(transform.position, node.transform.position);
            }
        }
    }

    public void AddConnection(OverworldNode node)
    {
        if (!connectedNodes.Contains(node))
        {
            connectedNodes.Add(node);
        }
    }

    public void RemoveConnection(OverworldNode node)
    {
        connectedNodes.Remove(node);
    }

    public float GetDistanceTo(OverworldNode other)
    {
        return Vector3.Distance(transform.position, other.transform.position);
    }

    // Change node color at runtime
    public void SetNodeColor(Color color)
    {
        nodeColor = color;
        if (spriteRenderer != null)
        {
            spriteRenderer.color = color;
        }
    }
    private void OnMouseDown()
{
    if (OverworldManager.Instance != null)
    {
        OverworldManager.Instance.NavigateToNode(this);
    }
}
}