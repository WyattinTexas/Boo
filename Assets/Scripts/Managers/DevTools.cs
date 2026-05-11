using Sirenix.OdinInspector;
using UnityEngine;

public class DevTools : MonoBehaviour, IAlignedScript
{
    public static DevTools Instance;

    public bool OwnAllCards = false;
    [Space]
    public bool EnableClearGraveyardBtn = false;
    public bool OverrideGraveyardHours = false;
    public float TestingGraveyardHours = 0.0027f;

    public void AlignedAwake()
    {
        Instance = this;

        DontDestroyOnLoad(gameObject);
    }

#if UNITY_EDITOR
    [HideInEditorMode, Button]
    public void DefeatAllLocations()
    {
        if (WorldManager.Instance == null)
            return;

        foreach (var location in WorldManager.Instance.Locations)
        {
            foreach (var encounter in location.Encounters)
                MainPlayerData.Instance.WorldSaved.DefeatEncounter(encounter.AssetId, location.LocationId, true);
        }

        foreach (var location in WorldManager.Instance.Locations)
        {
            location.UpdateDefeatStatus();
            location.ConnectedLocations.ForEach(x => x.UpdateExplored());
        }
    }
    [HideInEditorMode, Button]
    public void DefeatLocation(WorldLocationType type)
    {
        if (!WorldManager.Instance.Locations.TryFind(x => x.LocationType == type, out WorldLocation location))
            return;

        foreach (var encounter in location.Encounters)
            MainPlayerData.Instance.WorldSaved.DefeatEncounter(encounter.AssetId, location.LocationId, true);
        location.UpdateDefeatStatus();
        location.ConnectedLocations.ForEach(x => x.UpdateExplored());
    }
    [HideInEditorMode, Button]
    public void UnDefeatLocation(WorldLocationType type)
    {
        if (!WorldManager.Instance.Locations.TryFind(x => x.LocationType == type, out WorldLocation location))
            return;

        foreach (var encounter in location.Encounters)
            MainPlayerData.Instance.WorldSaved.DefeatEncounter(encounter.AssetId, location.LocationId, false);
        location.UpdateDefeatStatus();
        location.ConnectedLocations.ForEach(x => x.UpdateExplored());
    }
#endif

    // =========================================================================
    // PLAYTEST AGENTS — trigger via browser console:
    //   FindObjectOfType<DevTools>().RunCollector()
    //   FindObjectOfType<DevTools>().RunExplorer()
    //   FindObjectOfType<DevTools>().RunCrafter()
    //   FindObjectOfType<DevTools>().StopPlaytest()
    // =========================================================================

    public void RunCollector() => PlaytestAgent.StartTest(PlaytestAgent.AgentType.Collector);
    public void RunExplorer() => PlaytestAgent.StartTest(PlaytestAgent.AgentType.Explorer);
    public void RunCrafter() => PlaytestAgent.StartTest(PlaytestAgent.AgentType.Crafter);
    public void StopPlaytest() => PlaytestAgent.StopTest();
}
