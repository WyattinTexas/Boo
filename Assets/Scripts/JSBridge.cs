using UnityEngine;

/// <summary>
/// Bridge for JavaScript → Unity communication.
/// Creates a GameObject named "JSBridge" that JS can target with SendMessage.
/// </summary>
public class JSBridge : MonoBehaviour
{
    static JSBridge _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (_instance != null) return;
        var go = new GameObject("JSBridge");
        _instance = go.AddComponent<JSBridge>();
        DontDestroyOnLoad(go);
        Debug.Log("[JSBridge] Ready");
    }

    /// <summary>JS: unityInstance.SendMessage('JSBridge', 'SetTerrainOffset', '0.5')</summary>
    public void SetTerrainOffset(string value)
    {
        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float offset))
        {
            TerrainMeshColliderGenerator.HeightOffset = offset;

            // Destroy old generated colliders
            if (WorldManager.Instance != null)
            {
                foreach (var t in WorldManager.Instance.Terrains)
                {
                    foreach (var mc in t.GetComponentsInChildren<MeshCollider>())
                    {
                        if (mc.gameObject.name == "_WebGLTerrainCollider")
                            Destroy(mc.gameObject);
                    }
                }
                // Regenerate
                TerrainMeshColliderGenerator.GenerateForTerrains(WorldManager.Instance.Terrains);
            }
            Debug.Log($"[JSBridge] Terrain offset = {offset}");
        }
    }

    /// <summary>JS: unityInstance.SendMessage('JSBridge', 'DebugOverhead', '')</summary>
    public void DebugOverhead(string _)
    {
        if (WorldManager.Instance != null)
            WorldManager.Instance.DebugSetCameraOverhead(_);
    }

    // =========================================================================
    // PLAYTEST AGENTS — call from browser console:
    //   unityInstance.SendMessage('JSBridge', 'RunCollector', '')
    //   unityInstance.SendMessage('JSBridge', 'RunExplorer', '')
    //   unityInstance.SendMessage('JSBridge', 'RunCrafter', '')
    //   unityInstance.SendMessage('JSBridge', 'StopPlaytest', '')
    // =========================================================================

    public void RunCollector(string _) => PlaytestAgent.StartTest(PlaytestAgent.AgentType.Collector);
    public void RunExplorer(string _) => PlaytestAgent.StartTest(PlaytestAgent.AgentType.Explorer);
    public void RunCrafter(string _) => PlaytestAgent.StartTest(PlaytestAgent.AgentType.Crafter);
    public void StopPlaytest(string _) => PlaytestAgent.StopTest();
}
