using UnityEngine;

/// <summary>
/// Place in the world to mark a dungeon entrance.
/// Shows interaction prompt when player is near, enters dungeon on E key.
/// Also ensures DungeonSystem singleton exists.
/// </summary>
public class DungeonEntrance : MonoBehaviour
{
    [Header("Config")]
    public string DungeonId;
    public float InteractionRange = 3f;

    [Header("Visual")]
    public Color MarkerColor = new(0.6f, 0.4f, 0.9f);

    TextMesh _label;
    bool _playerInRange;

    void Start()
    {
        // Ensure DungeonSystem singleton exists
        if (DungeonSystem.Instance == null)
        {
            var go = new GameObject("DungeonSystem");
            go.AddComponent<DungeonSystem>();
            DontDestroyOnLoad(go);
        }

        // Build visual marker if no existing mesh
        if (GetComponentInChildren<MeshFilter>() == null)
            BuildMarker();
    }

    void Update()
    {
        var player = WorldManager.Instance?.WorldPlayer;
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.transform.position);
        bool inRange = dist < InteractionRange;

        if (inRange && !_playerInRange)
        {
            _playerInRange = true;
            ShowPrompt();
        }
        else if (!inRange && _playerInRange)
        {
            _playerInRange = false;
            HidePrompt();
        }

        // E key to enter
        if (inRange && Input.GetKeyDown(KeyCode.E)
            && (SpiritComms.Instance == null || !SpiritComms.Instance.IsActive)
            && (DungeonSystem.Instance == null || !DungeonSystem.Instance.IsInDungeon))
        {
            DungeonSystem.Instance?.EnterDungeon(DungeonId);
        }
    }

    void BuildMarker()
    {
        // Archway from primitives
        var pillarL = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillarL.name = "PillarL";
        pillarL.transform.SetParent(transform, false);
        pillarL.transform.localScale = new Vector3(0.2f, 1.2f, 0.2f);
        pillarL.transform.localPosition = new Vector3(-0.6f, 1.2f, 0);
        pillarL.GetComponent<Renderer>().material.color = MarkerColor * 0.6f;
        Object.Destroy(pillarL.GetComponent<Collider>());

        var pillarR = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillarR.name = "PillarR";
        pillarR.transform.SetParent(transform, false);
        pillarR.transform.localScale = new Vector3(0.2f, 1.2f, 0.2f);
        pillarR.transform.localPosition = new Vector3(0.6f, 1.2f, 0);
        pillarR.GetComponent<Renderer>().material.color = MarkerColor * 0.6f;
        Object.Destroy(pillarR.GetComponent<Collider>());

        var arch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arch.name = "Arch";
        arch.transform.SetParent(transform, false);
        arch.transform.localScale = new Vector3(1.4f, 0.15f, 0.2f);
        arch.transform.localPosition = new Vector3(0, 2.45f, 0);
        arch.GetComponent<Renderer>().material.color = MarkerColor * 0.7f;
        Object.Destroy(arch.GetComponent<Collider>());

        // Glowing orb on top
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "Orb";
        orb.transform.SetParent(transform, false);
        orb.transform.localScale = Vector3.one * 0.25f;
        orb.transform.localPosition = new Vector3(0, 2.7f, 0);
        var orbMat = orb.GetComponent<Renderer>().material;
        orbMat.color = MarkerColor;
        orbMat.SetColor("_EmissionColor", MarkerColor * 2f);
        orbMat.EnableKeyword("_EMISSION");
        Object.Destroy(orb.GetComponent<Collider>());

        // Name label
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(transform, false);
        labelGO.transform.localPosition = new Vector3(0, 3.1f, 0);
        _label = labelGO.AddComponent<TextMesh>();
        var def = DungeonSystem.GetDungeon(DungeonId);
        _label.text = def?.Name ?? DungeonId;
        _label.fontSize = 48;
        _label.characterSize = 0.04f;
        _label.anchor = TextAnchor.MiddleCenter;
        _label.alignment = TextAlignment.Center;
        _label.color = MarkerColor;
        _label.fontStyle = FontStyle.Bold;
        labelGO.AddComponent<BillboardLabel>();
    }

    void ShowPrompt()
    {
        OverworldIntegration.Instance?.ShowNotification($"[E] Enter Dungeon");
    }

    void HidePrompt()
    {
        // Prompt auto-dismisses via notification timer
    }
}
