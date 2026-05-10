using System;
using UnityEngine;

/// <summary>
/// Static harvestable resource node. Press E to channel-harvest.
/// Depletes after harvest, respawns on timer.
/// GatheringManager detects E key press and calls StartHarvest.
/// </summary>
public class ResourceNode : MonoBehaviour
{
    public string NodeId;
    public string MaterialId;
    public string NodeName;

    [Header("State")]
    public bool IsDepleted;

    float _harvestProgress;
    float _harvestDuration;
    Action _onHarvestComplete;
    bool _isHarvesting;

    float _respawnTimer;
    Color _originalColor;
    bool _colorSaved;

    void Start()
    {
        // Build procedural model if no existing mesh
        var existingMesh = GetComponentInChildren<MeshFilter>();
        if (existingMesh == null && !string.IsNullOrEmpty(MaterialId))
            CollectibleModelBuilder.BuildForMaterial(MaterialId, transform);

        // Save original color for respawn restore
        var renderer = GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            _originalColor = renderer.material.color;
            _colorSaved = true;
        }
    }

    /// <summary>Begin the harvest channel.</summary>
    public void StartHarvest(float duration, Action onComplete)
    {
        if (IsDepleted || _isHarvesting) return;

        _isHarvesting = true;
        _harvestDuration = duration;
        _harvestProgress = 0;
        _onHarvestComplete = onComplete;
    }

    /// <summary>Cancel an in-progress harvest (e.g., moved away or enemy interruption).</summary>
    public void CancelHarvest()
    {
        _isHarvesting = false;
        _harvestProgress = 0;
        _onHarvestComplete = null;
    }

    /// <summary>Mark as depleted and start respawn countdown.</summary>
    public void Deplete(float respawnTime)
    {
        IsDepleted = true;
        _isHarvesting = false;
        _respawnTimer = respawnTime;

        // Visual: dim the node
        var renderer = GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            if (!_colorSaved)
            {
                _originalColor = renderer.material.color;
                _colorSaved = true;
            }
            renderer.material.color = _originalColor * 0.3f;
        }

        // Shrink slightly
        transform.localScale *= 0.6f;
    }

    void Update()
    {
        // Harvest progress
        if (_isHarvesting)
        {
            _harvestProgress += Time.deltaTime;
            if (_harvestProgress >= _harvestDuration)
            {
                _isHarvesting = false;
                _onHarvestComplete?.Invoke();
                _onHarvestComplete = null;
            }
        }

        // Respawn timer
        if (IsDepleted)
        {
            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer <= 0)
            {
                IsDepleted = false;

                // Restore visuals
                var renderer = GetComponentInChildren<Renderer>();
                if (renderer != null && _colorSaved)
                {
                    renderer.material.color = _originalColor;
                }

                // Restore scale
                transform.localScale /= 0.6f;
            }
        }
    }

    /// <summary>Current harvest progress (0-1).</summary>
    public float HarvestPercent => _isHarvesting ? _harvestProgress / _harvestDuration : 0;
    public bool IsHarvesting => _isHarvesting;
}
