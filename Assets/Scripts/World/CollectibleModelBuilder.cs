using UnityEngine;

/// <summary>
/// Builds procedural 3D models for resource nodes and world collectibles
/// from Unity primitives. Recognizable shapes instead of generic spheres.
/// </summary>
public static class CollectibleModelBuilder
{
    /// <summary>Build a model for a resource node based on its material type.</summary>
    public static GameObject BuildForMaterial(string materialId, Transform parent)
    {
        string type = materialId?.ToLower() ?? "";
        GameObject model;

        if (type.Contains("ore") || type.Contains("iron") || type.Contains("metal") || type.Contains("obsidian"))
            model = BuildOreNode();
        else if (type.Contains("herb") || type.Contains("seed") || type.Contains("plant") || type.Contains("heal"))
            model = BuildHerbNode();
        else if (type.Contains("wood") || type.Contains("timber") || type.Contains("branch"))
            model = BuildWoodNode();
        else if (type.Contains("crystal") || type.Contains("gem") || type.Contains("glass"))
            model = BuildCrystal();
        else if (type.Contains("essence") || type.Contains("fire") || type.Contains("spirit"))
            model = BuildEssenceOrb();
        else
            model = BuildGenericNode();

        model.transform.SetParent(parent, false);
        model.transform.localPosition = Vector3.zero;
        return model;
    }

    /// <summary>Build a model for a world collectible by type.</summary>
    public static GameObject BuildForCollectible(WorldCollectible.CollectibleType type, Transform parent)
    {
        GameObject model = type switch
        {
            WorldCollectible.CollectibleType.TreasureChest => BuildTreasureChest(),
            WorldCollectible.CollectibleType.Lore => BuildLoreTablet(),
            WorldCollectible.CollectibleType.Viewpoint => BuildViewpoint(),
            WorldCollectible.CollectibleType.PathSign => BuildPathSign(),
            _ => BuildGenericNode()
        };

        model.transform.SetParent(parent, false);
        model.transform.localPosition = Vector3.zero;
        return model;
    }

    // =========================================================================
    // RESOURCE NODE MODELS
    // =========================================================================

    /// <summary>Crystalline rock cluster — 3 cubes at random rotations.</summary>
    static GameObject BuildOreNode()
    {
        var root = new GameObject("OreModel");
        Color oreColor = new(0.4f, 0.4f, 0.45f);

        for (int i = 0; i < 3; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Rock_{i}";
            cube.transform.SetParent(root.transform, false);
            float s = Random.Range(0.3f, 0.5f);
            cube.transform.localScale = new Vector3(s, s * Random.Range(0.8f, 1.5f), s);
            cube.transform.localPosition = new Vector3(
                Random.Range(-0.2f, 0.2f), s * 0.3f, Random.Range(-0.2f, 0.2f));
            cube.transform.localRotation = Quaternion.Euler(
                Random.Range(-15f, 15f), Random.Range(0f, 45f), Random.Range(-15f, 15f));
            SetColor(cube, oreColor * Random.Range(0.8f, 1.2f));
            Object.Destroy(cube.GetComponent<Collider>());
        }

        // Sparkle — tiny emissive cube
        var sparkle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sparkle.name = "Sparkle";
        sparkle.transform.SetParent(root.transform, false);
        sparkle.transform.localScale = Vector3.one * 0.08f;
        sparkle.transform.localPosition = new Vector3(0, 0.4f, 0);
        var mat = sparkle.GetComponent<Renderer>().material;
        mat.color = new Color(0.8f, 0.8f, 1f);
        mat.SetColor("_EmissionColor", new Color(0.8f, 0.8f, 1f) * 2f);
        mat.EnableKeyword("_EMISSION");
        Object.Destroy(sparkle.GetComponent<Collider>());

        return root;
    }

    /// <summary>Plant-like node — sphere base with stems and berries.</summary>
    static GameObject BuildHerbNode()
    {
        var root = new GameObject("HerbModel");
        Color green = new(0.3f, 0.65f, 0.3f);

        // Base mound
        var baseObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        baseObj.name = "Base";
        baseObj.transform.SetParent(root.transform, false);
        baseObj.transform.localScale = new Vector3(0.4f, 0.2f, 0.4f);
        baseObj.transform.localPosition = new Vector3(0, 0.1f, 0);
        SetColor(baseObj, new Color(0.35f, 0.25f, 0.15f));
        Object.Destroy(baseObj.GetComponent<Collider>());

        // 3 stems with berries
        for (int i = 0; i < 3; i++)
        {
            float angle = i * 120f * Mathf.Deg2Rad;
            float r = 0.1f;

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = $"Stem_{i}";
            stem.transform.SetParent(root.transform, false);
            stem.transform.localScale = new Vector3(0.03f, 0.2f, 0.03f);
            stem.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * r, 0.3f, Mathf.Sin(angle) * r);
            SetColor(stem, green);
            Object.Destroy(stem.GetComponent<Collider>());

            var berry = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            berry.name = $"Berry_{i}";
            berry.transform.SetParent(root.transform, false);
            berry.transform.localScale = Vector3.one * 0.1f;
            berry.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * r, 0.52f, Mathf.Sin(angle) * r);
            SetColor(berry, new Color(0.2f, 0.8f, 0.3f));
            Object.Destroy(berry.GetComponent<Collider>());
        }

        return root;
    }

    /// <summary>Tree stump — cylinder trunk with flattened sphere canopy.</summary>
    static GameObject BuildWoodNode()
    {
        var root = new GameObject("WoodModel");

        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Trunk";
        trunk.transform.SetParent(root.transform, false);
        trunk.transform.localScale = new Vector3(0.15f, 0.3f, 0.15f);
        trunk.transform.localPosition = new Vector3(0, 0.3f, 0);
        SetColor(trunk, new Color(0.45f, 0.3f, 0.15f));
        Object.Destroy(trunk.GetComponent<Collider>());

        var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        canopy.name = "Canopy";
        canopy.transform.SetParent(root.transform, false);
        canopy.transform.localScale = new Vector3(0.5f, 0.3f, 0.5f);
        canopy.transform.localPosition = new Vector3(0, 0.65f, 0);
        SetColor(canopy, new Color(0.25f, 0.55f, 0.2f));
        Object.Destroy(canopy.GetComponent<Collider>());

        return root;
    }

    /// <summary>Tall diamond-shaped crystal with emissive glow and gentle spin.</summary>
    static GameObject BuildCrystal()
    {
        var root = new GameObject("CrystalModel");

        var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crystal.name = "Crystal";
        crystal.transform.SetParent(root.transform, false);
        crystal.transform.localScale = new Vector3(0.15f, 0.5f, 0.15f);
        crystal.transform.localPosition = new Vector3(0, 0.35f, 0);
        crystal.transform.localRotation = Quaternion.Euler(0, 45f, 0);
        var mat = crystal.GetComponent<Renderer>().material;
        Color crystalColor = new(0.4f, 0.7f, 1f);
        mat.color = crystalColor;
        mat.SetColor("_EmissionColor", crystalColor * 1.5f);
        mat.EnableKeyword("_EMISSION");
        Object.Destroy(crystal.GetComponent<Collider>());

        // Smaller shard
        var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shard.name = "Shard";
        shard.transform.SetParent(root.transform, false);
        shard.transform.localScale = new Vector3(0.08f, 0.25f, 0.08f);
        shard.transform.localPosition = new Vector3(0.12f, 0.2f, 0.05f);
        shard.transform.localRotation = Quaternion.Euler(0, 30f, 15f);
        var shardMat = shard.GetComponent<Renderer>().material;
        shardMat.color = crystalColor * 0.8f;
        shardMat.SetColor("_EmissionColor", crystalColor);
        shardMat.EnableKeyword("_EMISSION");
        Object.Destroy(shard.GetComponent<Collider>());

        root.AddComponent<SpinY>().Speed = 20f;
        return root;
    }

    /// <summary>Glowing orb with orbiting electron sphere.</summary>
    static GameObject BuildEssenceOrb()
    {
        var root = new GameObject("EssenceModel");

        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "Core";
        core.transform.SetParent(root.transform, false);
        core.transform.localScale = Vector3.one * 0.3f;
        core.transform.localPosition = new Vector3(0, 0.4f, 0);
        var coreMat = core.GetComponent<Renderer>().material;
        Color essenceColor = new(1f, 0.6f, 0.2f);
        coreMat.color = essenceColor;
        coreMat.SetColor("_EmissionColor", essenceColor * 2f);
        coreMat.EnableKeyword("_EMISSION");
        Object.Destroy(core.GetComponent<Collider>());

        // Orbiting electron
        var orbit = new GameObject("Orbit");
        orbit.transform.SetParent(root.transform, false);
        orbit.transform.localPosition = new Vector3(0, 0.4f, 0);
        orbit.AddComponent<SpinY>().Speed = 90f;

        var electron = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        electron.name = "Electron";
        electron.transform.SetParent(orbit.transform, false);
        electron.transform.localScale = Vector3.one * 0.1f;
        electron.transform.localPosition = new Vector3(0.3f, 0, 0);
        var eMat = electron.GetComponent<Renderer>().material;
        eMat.color = essenceColor * 0.7f;
        eMat.SetColor("_EmissionColor", essenceColor);
        eMat.EnableKeyword("_EMISSION");
        Object.Destroy(electron.GetComponent<Collider>());

        return root;
    }

    /// <summary>Fallback generic collectible — small glowing sphere.</summary>
    static GameObject BuildGenericNode()
    {
        var root = new GameObject("GenericModel");

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Orb";
        sphere.transform.SetParent(root.transform, false);
        sphere.transform.localScale = Vector3.one * 0.25f;
        sphere.transform.localPosition = new Vector3(0, 0.3f, 0);
        var mat = sphere.GetComponent<Renderer>().material;
        mat.color = new Color(0.7f, 0.7f, 0.8f);
        mat.SetColor("_EmissionColor", new Color(0.5f, 0.5f, 0.6f));
        mat.EnableKeyword("_EMISSION");
        Object.Destroy(sphere.GetComponent<Collider>());

        return root;
    }

    // =========================================================================
    // COLLECTIBLE MODELS
    // =========================================================================

    /// <summary>Treasure chest — cube body with angled lid.</summary>
    static GameObject BuildTreasureChest()
    {
        var root = new GameObject("ChestModel");

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(0.4f, 0.25f, 0.3f);
        body.transform.localPosition = new Vector3(0, 0.125f, 0);
        SetColor(body, new Color(0.6f, 0.45f, 0.2f));
        Object.Destroy(body.GetComponent<Collider>());

        var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lid.name = "Lid";
        lid.transform.SetParent(root.transform, false);
        lid.transform.localScale = new Vector3(0.42f, 0.06f, 0.32f);
        lid.transform.localPosition = new Vector3(0, 0.28f, -0.05f);
        lid.transform.localRotation = Quaternion.Euler(-15f, 0, 0);
        SetColor(lid, new Color(0.65f, 0.5f, 0.25f));
        Object.Destroy(lid.GetComponent<Collider>());

        // Gold clasp
        var clasp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        clasp.name = "Clasp";
        clasp.transform.SetParent(root.transform, false);
        clasp.transform.localScale = new Vector3(0.08f, 0.08f, 0.02f);
        clasp.transform.localPosition = new Vector3(0, 0.2f, 0.16f);
        var claspMat = clasp.GetComponent<Renderer>().material;
        claspMat.color = new Color(1f, 0.85f, 0.2f);
        claspMat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.2f) * 0.5f);
        claspMat.EnableKeyword("_EMISSION");
        Object.Destroy(clasp.GetComponent<Collider>());

        return root;
    }

    /// <summary>Standing stone tablet with ancient look.</summary>
    static GameObject BuildLoreTablet()
    {
        var root = new GameObject("LoreModel");

        var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stone.name = "Tablet";
        stone.transform.SetParent(root.transform, false);
        stone.transform.localScale = new Vector3(0.3f, 0.5f, 0.06f);
        stone.transform.localPosition = new Vector3(0, 0.25f, 0);
        stone.transform.localRotation = Quaternion.Euler(0, Random.Range(-10f, 10f), 0);
        SetColor(stone, new Color(0.5f, 0.5f, 0.45f));
        Object.Destroy(stone.GetComponent<Collider>());

        // Rune glow
        var rune = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rune.name = "Rune";
        rune.transform.SetParent(stone.transform, false);
        rune.transform.localScale = new Vector3(0.5f, 0.3f, 1.05f);
        rune.transform.localPosition = new Vector3(0, 0.1f, 0);
        var runeMat = rune.GetComponent<Renderer>().material;
        runeMat.color = new Color(0.3f, 0.6f, 0.8f, 0.5f);
        runeMat.SetColor("_EmissionColor", new Color(0.3f, 0.6f, 0.8f) * 0.8f);
        runeMat.EnableKeyword("_EMISSION");
        Object.Destroy(rune.GetComponent<Collider>());

        return root;
    }

    /// <summary>Pillar with glowing orb on top.</summary>
    static GameObject BuildViewpoint()
    {
        var root = new GameObject("ViewpointModel");

        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "Pillar";
        pillar.transform.SetParent(root.transform, false);
        pillar.transform.localScale = new Vector3(0.12f, 0.4f, 0.12f);
        pillar.transform.localPosition = new Vector3(0, 0.4f, 0);
        SetColor(pillar, new Color(0.55f, 0.55f, 0.5f));
        Object.Destroy(pillar.GetComponent<Collider>());

        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "Orb";
        orb.transform.SetParent(root.transform, false);
        orb.transform.localScale = Vector3.one * 0.2f;
        orb.transform.localPosition = new Vector3(0, 0.85f, 0);
        var orbMat = orb.GetComponent<Renderer>().material;
        Color orbColor = new(0.9f, 0.8f, 0.3f);
        orbMat.color = orbColor;
        orbMat.SetColor("_EmissionColor", orbColor * 2f);
        orbMat.EnableKeyword("_EMISSION");
        Object.Destroy(orb.GetComponent<Collider>());

        return root;
    }

    /// <summary>Signpost — thin post with flat sign.</summary>
    static GameObject BuildPathSign()
    {
        var root = new GameObject("SignModel");

        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "Post";
        post.transform.SetParent(root.transform, false);
        post.transform.localScale = new Vector3(0.04f, 0.4f, 0.04f);
        post.transform.localPosition = new Vector3(0, 0.4f, 0);
        SetColor(post, new Color(0.45f, 0.3f, 0.15f));
        Object.Destroy(post.GetComponent<Collider>());

        var sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sign.name = "Sign";
        sign.transform.SetParent(root.transform, false);
        sign.transform.localScale = new Vector3(0.35f, 0.15f, 0.03f);
        sign.transform.localPosition = new Vector3(0.05f, 0.75f, 0);
        SetColor(sign, new Color(0.5f, 0.35f, 0.18f));
        Object.Destroy(sign.GetComponent<Collider>());

        return root;
    }

    static void SetColor(GameObject go, Color color)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = color;
    }
}
