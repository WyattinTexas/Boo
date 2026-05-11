using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Clean text-based HP display: "HP 6/6"
/// Replaces the old heart/pip grid system.
/// Flashes red on damage, green on heal.
/// </summary>
public class HealthBar : MonoBehaviour
{
    public const float DestroyShakeDuration = 0.5f;
    public const float ShakeStrength = 8f;

    public Image PartPrefab; // Kept for backwards compatibility — not used in text mode

    [HideInEditorMode, ReadOnly] public List<Image> ActiveParts = new();
    [HideInEditorMode, ReadOnly] public List<Image> DestroyingParts = new();

    public event Action<HealthBar> OnHealthChanged;

    public int ShownHealth { get; private set; }
    public int MaxHP { get; set; }

    // Text-based HP display
    TextMeshProUGUI _hpText;
    string _cardName;
    Color _defaultColor = Color.white;
    Coroutine _flashCor;

    void Awake()
    {
        BuildTextDisplay();
    }

    void BuildTextDisplay()
    {
        // Hide any existing GridLayoutGroup / heart children
        var grid = GetComponent<GridLayoutGroup>();
        if (grid != null) grid.enabled = false;

        // Clear old heart children
        foreach (Transform child in transform)
            Destroy(child.gameObject);
        ActiveParts.Clear();

        // Create the text display
        var textGO = new GameObject("HPText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(transform, false);
        _hpText = textGO.GetComponent<TextMeshProUGUI>();
        _hpText.fontSize = 18;
        _hpText.fontStyle = FontStyles.Bold;
        _hpText.color = Color.white;
        _hpText.alignment = TextAlignmentOptions.MidlineRight;
        _hpText.enableWordWrapping = false;
        _hpText.text = "";

        var rect = textGO.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        _defaultColor = new Color(0.15f, 0.15f, 0.15f); // Dark text on light bg
        _hpText.color = _defaultColor;
    }

    /// <summary>Set the card name shown before HP.</summary>
    public void SetCardName(string name)
    {
        _cardName = name;
        UpdateText();
    }

    void OnDisable()
    {
        for (int i = 0; i < DestroyingParts.Count; i++)
        {
            var part = DestroyingParts[i];
            DestroyPart(part);
            i--;
        }
    }

    public void AddHealth(int amount, bool animate = true) => SetHealth(ShownHealth + amount, animate);
    public void SetHealth(int health, bool animate = true)
    {
        int oldHealth = ShownHealth;
        health = Mathf.Max(health, 0);
        ShownHealth = health;

        if (MaxHP <= 0) MaxHP = health; // Auto-set max on first call

        UpdateText();

        // Flash animation
        if (animate && _hpText != null && oldHealth != health)
        {
            if (_flashCor != null) StopCoroutine(_flashCor);
            Color flashColor = health < oldHealth ? new Color(0.9f, 0.15f, 0.1f) : new Color(0.1f, 0.8f, 0.2f);
            _flashCor = StartCoroutine(FlashHP(flashColor));
        }

        OnHealthChanged?.InvokeSafe(nameof(OnHealthChanged), this);
    }

    void UpdateText()
    {
        if (_hpText == null) return;

        string name = !string.IsNullOrEmpty(_cardName) ? _cardName + "  " : "";
        _hpText.text = $"{name}<b>HP {ShownHealth}/{MaxHP}</b>";

        // Color based on health percentage
        float pct = MaxHP > 0 ? (float)ShownHealth / MaxHP : 1f;
        if (pct <= 0)
            _defaultColor = new Color(0.5f, 0.15f, 0.1f); // Dead — dark red
        else if (pct <= 0.33f)
            _defaultColor = new Color(0.8f, 0.2f, 0.1f); // Low — red
        else if (pct <= 0.66f)
            _defaultColor = new Color(0.7f, 0.5f, 0.1f); // Mid — orange
        else
            _defaultColor = new Color(0.15f, 0.15f, 0.15f); // Healthy — dark

        if (_flashCor == null)
            _hpText.color = _defaultColor;
    }

    IEnumerator FlashHP(Color flashColor)
    {
        if (_hpText == null) yield break;

        _hpText.color = flashColor;
        _hpText.fontSize = 22; // Pop bigger

        yield return new WaitForSeconds(0.15f);

        // Lerp back
        float t = 0;
        while (t < 0.4f)
        {
            t += Time.deltaTime;
            _hpText.color = Color.Lerp(flashColor, _defaultColor, t / 0.4f);
            _hpText.fontSize = Mathf.Lerp(22, 18, t / 0.4f);
            yield return null;
        }

        _hpText.color = _defaultColor;
        _hpText.fontSize = 18;
        _flashCor = null;
    }

    // Legacy compatibility — these methods are called by existing code
    Image SpawnPart()
    {
        ShownHealth++;
        UpdateText();
        return null;
    }

    void DestroyPart(Image part)
    {
        DestroyingParts.Remove(part);
        if (part != null) Destroy(part.gameObject);
    }
}
