using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class OverworldManager : MonoBehaviour
{
    public static OverworldManager Instance { get; private set; }

    [Header("References")]
    public OverworldCharacter character;

    [Header("UI References")]
    public TMPro.TextMeshProUGUI locationText; // Drag your Location_Text here
    public string defaultLocationText = "Traveling...";

    [Header("Level Loading")]
    public bool enableLevelLoading = true;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (character == null)
        {
            character = FindObjectOfType<OverworldCharacter>();
        }
    }

    public void NavigateToCastle(OverworldCastle castle)
    {
        if (character == null || castle.nearestNode == null)
        {
            Debug.LogError("Character or castle's nearest node is null!");
            return;
        }

        // If character is already at the castle, load the level
        if (Vector3.Distance(character.transform.position, castle.transform.position) < 1f)
        {
            LoadLevel(castle.levelID);
            return;
        }

        // Find path from character's current node to castle's node
        List<OverworldNode> path = OverworldPathfinding.FindPath(character.currentNode, castle.nearestNode);

        if (path.Count > 0)
        {
            character.SetPath(path, castle);
        }
        else
        {
            Debug.LogWarning($"No path found to {castle.castleName}");
        }
    }

    public void NavigateToNode(OverworldNode targetNode)
    {
        if (character == null || character.currentNode == null)
        {
            Debug.LogError("Character or current node is null!");
            return;
        }
        
        // If character is already at the target node, do nothing
        if (character.currentNode == targetNode)
        {
            Debug.Log("Already at target node!");
            return;
        }
        
        // Find path from character's current node to target node
        List<OverworldNode> path = OverworldPathfinding.FindPath(character.currentNode, targetNode);
        
        if (path.Count > 0)
        {
            character.SetPath(path, null); // null because we're going to a node, not a castle
        }
        else
        {
            Debug.LogWarning($"No path found to target node");
        }
    }

    public void UpdateLocationText(OverworldNode currentNode)
    {
        if (locationText == null) return;
        
        // Check if current node is nearest to any castle
        OverworldCastle[] allCastles = FindObjectsOfType<OverworldCastle>();
        
        foreach (var castle in allCastles)
        {
            if (castle.nearestNode == currentNode)
            {
                // Character is at a castle's nearest node
                locationText.text = castle.castleName;
                return;
            }
        }
        
        // Not near any castle
        locationText.text = defaultLocationText;
    }

    private void LoadLevel(string levelID)
    {
        if (!enableLevelLoading)
        {
            Debug.Log($"Level loading disabled. Would load: {levelID}");
            return;
        }

        Debug.Log($"Loading level: {levelID}");
        // Replace with your actual level loading logic
        // SceneManager.LoadScene(levelID);
    }

    // Utility method to set up node connections automatically
    [ContextMenu("Auto-Connect Nearby Nodes")]
    public void AutoConnectNodes()
    {
        OverworldNode[] allNodes = FindObjectsOfType<OverworldNode>();
        float maxConnectionDistance = 5f; // Adjust this value as needed

        foreach (var node in allNodes)
        {
            node.connectedNodes.Clear();

            foreach (var otherNode in allNodes)
            {
                if (node != otherNode &&
                    Vector3.Distance(node.transform.position, otherNode.transform.position) <= maxConnectionDistance)
                {
                    node.AddConnection(otherNode);
                }
            }
        }

        Debug.Log($"Auto-connected {allNodes.Length} nodes");
    }
}