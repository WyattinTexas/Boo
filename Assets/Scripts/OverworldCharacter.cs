using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class OverworldCharacter : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 3f;
    public bool isMoving { get; private set; }
    
    [Header("Current State")]
    public OverworldNode currentNode;
    
    private Queue<OverworldNode> pathQueue = new Queue<OverworldNode>();
    private OverworldCastle targetCastle;
    
    private void Start()
    {
        // Find starting node if not assigned
        if (currentNode == null)
        {
            FindNearestNode();
        }
        
        // Position character at starting node
        if (currentNode != null)
        {
            transform.position = currentNode.transform.position;
            // Update location text on start
            OverworldManager.Instance?.UpdateLocationText(currentNode);
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
                currentNode = node;
            }
        }
    }
    
    public void SetPath(List<OverworldNode> path, OverworldCastle destination)
    {
        if (isMoving)
        {
            StopAllCoroutines();
        }
        
        pathQueue.Clear();
        foreach (var node in path)
        {
            pathQueue.Enqueue(node);
        }
        
        targetCastle = destination;
        StartCoroutine(FollowPath());
    }
    
    private IEnumerator FollowPath()
    {
        isMoving = true;
        
        while (pathQueue.Count > 0)
        {
            OverworldNode nextNode = pathQueue.Dequeue();
            yield return StartCoroutine(MoveToNode(nextNode));
            currentNode = nextNode;
        }
        
        // Move to castle if we have a target
        if (targetCastle != null)
        {
            yield return StartCoroutine(MoveToCastle(targetCastle));
        }
        
        isMoving = false;
    }
    
    private IEnumerator MoveToNode(OverworldNode targetNode)
    {
        Vector3 startPos = transform.position;
        Vector3 endPos = targetNode.transform.position;
        float journeyLength = Vector3.Distance(startPos, endPos);
        float journeyTime = journeyLength / moveSpeed;
        float elapsedTime = 0;
        
        while (elapsedTime < journeyTime)
        {
            elapsedTime += Time.deltaTime;
            float fractionOfJourney = elapsedTime / journeyTime;
            transform.position = Vector3.Lerp(startPos, endPos, fractionOfJourney);
            yield return null;
        }
        
        transform.position = endPos;
        
        // Update location text when reaching a node
        OverworldManager.Instance?.UpdateLocationText(targetNode);
    }
    
    private IEnumerator MoveToCastle(OverworldCastle castle)
    {
        Vector3 startPos = transform.position;
        Vector3 endPos = castle.transform.position;
        float journeyLength = Vector3.Distance(startPos, endPos);
        float journeyTime = journeyLength / moveSpeed;
        float elapsedTime = 0;
        
        while (elapsedTime < journeyTime)
        {
            elapsedTime += Time.deltaTime;
            float fractionOfJourney = elapsedTime / journeyTime;
            transform.position = Vector3.Lerp(startPos, endPos, fractionOfJourney);
            yield return null;
        }
        
        transform.position = endPos;
        
        // Character has reached the castle - ready for level loading
        Debug.Log($"Reached {castle.castleName}! Ready to load level: {castle.levelID}");
    }
}