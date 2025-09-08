

// 4. PATHFINDING SYSTEM
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class OverworldPathfinding
{
    public static List<OverworldNode> FindPath(OverworldNode startNode, OverworldNode targetNode)
    {
        if (startNode == null || targetNode == null)
        {
            Debug.LogError("Start or target node is null!");
            return new List<OverworldNode>();
        }
        
        if (startNode == targetNode)
        {
            return new List<OverworldNode> { targetNode };
        }
        
        List<OverworldNode> openSet = new List<OverworldNode>();
        HashSet<OverworldNode> closedSet = new HashSet<OverworldNode>();
        
        openSet.Add(startNode);
        
        while (openSet.Count > 0)
        {
            OverworldNode currentNode = openSet[0];
            for (int i = 1; i < openSet.Count; i++)
            {
                if (openSet[i].fCost < currentNode.fCost || 
                    (openSet[i].fCost == currentNode.fCost && openSet[i].hCost < currentNode.hCost))
                {
                    currentNode = openSet[i];
                }
            }
            
            openSet.Remove(currentNode);
            closedSet.Add(currentNode);
            
            if (currentNode == targetNode)
            {
                return RetracePath(startNode, targetNode);
            }
            
            foreach (OverworldNode neighbor in currentNode.connectedNodes)
            {
                if (neighbor == null || closedSet.Contains(neighbor))
                    continue;
                
                float newCostToNeighbor = currentNode.gCost + currentNode.GetDistanceTo(neighbor);
                
                if (newCostToNeighbor < neighbor.gCost || !openSet.Contains(neighbor))
                {
                    neighbor.gCost = newCostToNeighbor;
                    neighbor.hCost = neighbor.GetDistanceTo(targetNode);
                    neighbor.parent = currentNode;
                    
                    if (!openSet.Contains(neighbor))
                        openSet.Add(neighbor);
                }
            }
        }
        
        Debug.LogWarning("No path found between nodes!");
        return new List<OverworldNode>();
    }
    
    private static List<OverworldNode> RetracePath(OverworldNode startNode, OverworldNode endNode)
    {
        List<OverworldNode> path = new List<OverworldNode>();
        OverworldNode currentNode = endNode;
        
        while (currentNode != startNode)
        {
            path.Add(currentNode);
            currentNode = currentNode.parent;
        }
        
        path.Reverse();
        return path;
    }
}
