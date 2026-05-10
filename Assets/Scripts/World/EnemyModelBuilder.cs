using UnityEngine;

/// <summary>
/// Builds procedural multi-primitive humanoid enemy models from Unity primitives.
/// Replaces the old single-capsule fallback with recognizable figures.
/// </summary>
public static class EnemyModelBuilder
{
    /// <summary>Build a humanoid enemy figure from primitives, colored by rarity.</summary>
    public static GameObject Build(string rarity, bool isElite)
    {
        var root = new GameObject("EnemyModel");

        Color baseColor = GetRarityColor(rarity);
        float scale = isElite ? 1.4f : 1f;

        // ── Body (capsule torso) ──
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(0.6f, 0.9f, 0.4f) * scale;
        body.transform.localPosition = new Vector3(0, 0.9f * scale, 0);
        SetColor(body, baseColor);
        Object.Destroy(body.GetComponent<Collider>());

        // ── Head (sphere) ──
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(root.transform, false);
        head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f) * scale;
        head.transform.localPosition = new Vector3(0, 1.85f * scale, 0);
        SetColor(head, baseColor * 1.1f);
        Object.Destroy(head.GetComponent<Collider>());

        // ── Eyes (2 tiny emissive spheres) ──
        for (int i = 0; i < 2; i++)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = $"Eye_{i}";
            eye.transform.SetParent(head.transform, false);
            eye.transform.localScale = new Vector3(0.2f, 0.2f, 0.15f);
            float xOff = (i == 0) ? -0.25f : 0.25f;
            eye.transform.localPosition = new Vector3(xOff, 0.1f, 0.4f);
            var eyeMat = eye.GetComponent<Renderer>().material;
            eyeMat.color = Color.white;
            eyeMat.SetColor("_EmissionColor", Color.white * 2f);
            eyeMat.EnableKeyword("_EMISSION");
            Object.Destroy(eye.GetComponent<Collider>());
        }

        // ── Arms (2 thin cylinders) ──
        for (int i = 0; i < 2; i++)
        {
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arm.name = $"Arm_{i}";
            arm.transform.SetParent(root.transform, false);
            arm.transform.localScale = new Vector3(0.12f, 0.35f, 0.12f) * scale;
            float side = (i == 0) ? -1f : 1f;
            arm.transform.localPosition = new Vector3(side * 0.4f * scale, 1.1f * scale, 0);
            arm.transform.localRotation = Quaternion.Euler(0, 0, side * -25f);
            SetColor(arm, baseColor * 0.85f);
            Object.Destroy(arm.GetComponent<Collider>());
        }

        // ── Legs (2 thin cylinders) ──
        for (int i = 0; i < 2; i++)
        {
            var leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leg.name = $"Leg_{i}";
            leg.transform.SetParent(root.transform, false);
            leg.transform.localScale = new Vector3(0.14f, 0.35f, 0.14f) * scale;
            float side = (i == 0) ? -0.15f : 0.15f;
            leg.transform.localPosition = new Vector3(side * scale, 0.15f * scale, 0);
            SetColor(leg, baseColor * 0.75f);
            Object.Destroy(leg.GetComponent<Collider>());
        }

        // ── Elite crown (small cone on head) ──
        if (isElite)
        {
            var crown = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            crown.name = "Crown";
            crown.transform.SetParent(head.transform, false);
            crown.transform.localScale = new Vector3(0.6f, 0.2f, 0.6f);
            crown.transform.localPosition = new Vector3(0, 0.55f, 0);
            var crownMat = crown.GetComponent<Renderer>().material;
            crownMat.color = new Color(1f, 0.85f, 0.2f);
            crownMat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.2f) * 1.5f);
            crownMat.EnableKeyword("_EMISSION");
            Object.Destroy(crown.GetComponent<Collider>());
        }

        // ── Floating name label ──
        var labelGO = new GameObject("NameLabel");
        labelGO.transform.SetParent(root.transform, false);
        labelGO.transform.localPosition = new Vector3(0, 2.4f * scale, 0);
        var tm = labelGO.AddComponent<TextMesh>();
        tm.text = "";
        tm.fontSize = 48;
        tm.characterSize = 0.04f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = baseColor;
        tm.fontStyle = FontStyle.Bold;
        // Billboard component added after spawn
        labelGO.AddComponent<BillboardLabel>();

        // ── Slow spin component ──
        root.AddComponent<SpinY>();

        // Add a single capsule collider on root for detection
        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0, 1f * scale, 0);
        col.radius = 0.4f * scale;
        col.height = 2f * scale;

        return root;
    }

    /// <summary>Set the name label text on an enemy built by this builder.</summary>
    public static void SetNameLabel(GameObject enemyModel, string name)
    {
        var label = enemyModel.GetComponentInChildren<TextMesh>();
        if (label != null) label.text = name;
    }

    static Color GetRarityColor(string rarity)
    {
        return rarity?.ToLower() switch
        {
            "common" => new Color(0.5f, 0.7f, 0.5f),
            "uncommon" => new Color(0.4f, 0.6f, 0.9f),
            "rare" => new Color(0.8f, 0.5f, 0.9f),
            "ghost-rare" => new Color(0.9f, 0.3f, 0.3f),
            "legendary" => new Color(1f, 0.85f, 0.2f),
            _ => Color.white
        };
    }

    static void SetColor(GameObject go, Color color)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = color;
    }
}

/// <summary>Simple Y-axis spin for idle enemy animation.</summary>
public class SpinY : MonoBehaviour
{
    public float Speed = 30f;
    void Update() => transform.Rotate(0, Speed * Time.deltaTime, 0);
}
