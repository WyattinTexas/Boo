using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Title screen overlay: player character stands by a tree in the live world.
/// "BATTLE OF ORIGINS" title + START button. Press START → UI fades out → game begins.
/// The world is already loaded and visible behind the overlay — no scene switch needed.
/// </summary>
public class TitleScreen : MonoBehaviour
{
    public static TitleScreen Instance { get; private set; }
    public static bool HasStarted { get; private set; }

    Canvas _canvas;
    CanvasGroup _overlayGroup;
    GameObject _overlay;
    bool _isActive;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Show the title screen. Call after the world scene is loaded.</summary>
    public void Show()
    {
        if (HasStarted) return; // Don't show again after first play

        _isActive = true;
        BuildUI();

        // Freeze the player
        var player = WorldManager.Instance?.WorldPlayer;
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
        }

        Debug.Log("[TitleScreen] Showing — player frozen, waiting for START");
    }

    void BuildUI()
    {
        // Find or create canvas
        _canvas = OverworldIntegration.Instance?._overlayCanvas;
        if (_canvas == null)
        {
            var existing = FindAnyObjectByType<Canvas>();
            if (existing != null) _canvas = existing;
            else return;
        }

        // ── Full-screen overlay with CanvasGroup for fade ──
        _overlay = new GameObject("TitleScreenOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        _overlay.transform.SetAsLastSibling(); // On top of everything

        _overlayGroup = _overlay.AddComponent<CanvasGroup>();
        _overlayGroup.alpha = 1f;
        _overlayGroup.blocksRaycasts = true;

        var overlayRect = _overlay.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        // ── Subtle dark gradient at top and bottom for text readability ──
        // Top gradient
        var topGrad = new GameObject("TopGrad", typeof(RectTransform), typeof(Image));
        topGrad.transform.SetParent(_overlay.transform, false);
        var topImg = topGrad.GetComponent<Image>();
        topImg.color = new Color(0, 0, 0, 0.4f);
        topImg.raycastTarget = false;
        var topRect = topGrad.GetComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0, 0.75f);
        topRect.anchorMax = Vector2.one;
        topRect.offsetMin = Vector2.zero;
        topRect.offsetMax = Vector2.zero;

        // Bottom gradient
        var botGrad = new GameObject("BotGrad", typeof(RectTransform), typeof(Image));
        botGrad.transform.SetParent(_overlay.transform, false);
        var botImg = botGrad.GetComponent<Image>();
        botImg.color = new Color(0, 0, 0, 0.3f);
        botImg.raycastTarget = false;
        var botRect = botGrad.GetComponent<RectTransform>();
        botRect.anchorMin = Vector2.zero;
        botRect.anchorMax = new Vector2(1, 0.15f);
        botRect.offsetMin = Vector2.zero;
        botRect.offsetMax = Vector2.zero;

        // ── Title text — "BATTLE OF ORIGINS" ──
        var titleGO = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleGO.transform.SetParent(_overlay.transform, false);
        var titleTMP = titleGO.GetComponent<TextMeshProUGUI>();
        titleTMP.text = "BATTLE OF ORIGINS";
        titleTMP.fontSize = 52;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.color = Color.white;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.enableWordWrapping = false;
        var titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 0.82f);
        titleRect.anchorMax = new Vector2(1, 0.95f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        // ── Subtitle ──
        var subGO = new GameObject("Subtitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        subGO.transform.SetParent(_overlay.transform, false);
        var subTMP = subGO.GetComponent<TextMeshProUGUI>();
        subTMP.text = "The Spirit World Awaits";
        subTMP.fontSize = 20;
        subTMP.fontStyle = FontStyles.Italic;
        subTMP.color = new Color(1, 1, 1, 0.7f);
        subTMP.alignment = TextAlignmentOptions.Center;
        var subRect = subGO.GetComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0, 0.76f);
        subRect.anchorMax = new Vector2(1, 0.82f);
        subRect.offsetMin = Vector2.zero;
        subRect.offsetMax = Vector2.zero;

        // ── START button — right side, like the sketch ──
        var btnGO = new GameObject("StartBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(_overlay.transform, false);
        var btnImg = btnGO.GetComponent<Image>();
        btnImg.color = new Color(0.95f, 0.95f, 0.92f, 0.9f);
        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.65f, 0.35f);
        btnRect.anchorMax = new Vector2(0.85f, 0.48f);
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;

        // Button outline (dark border)
        var outline = btnGO.AddComponent<Outline>();
        outline.effectColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        outline.effectDistance = new Vector2(2, -2);

        var btnTextGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnTMP = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnTMP.text = "START";
        btnTMP.fontSize = 32;
        btnTMP.fontStyle = FontStyles.Bold;
        btnTMP.color = new Color(0.15f, 0.15f, 0.15f);
        btnTMP.alignment = TextAlignmentOptions.Center;
        var btRect = btnTextGO.GetComponent<RectTransform>();
        btRect.anchorMin = Vector2.zero;
        btRect.anchorMax = Vector2.one;
        btRect.offsetMin = Vector2.zero;
        btRect.offsetMax = Vector2.zero;

        // ── Button click → fade out → game begins ──
        btnGO.GetComponent<Button>().onClick.AddListener(OnStartPressed);
    }

    void OnStartPressed()
    {
        if (!_isActive) return;
        _isActive = false;
        HasStarted = true;

        Debug.Log("[TitleScreen] START pressed — entering the spirit world!");
        StartCoroutine(FadeOutAndBegin());
    }

    IEnumerator FadeOutAndBegin()
    {
        // Fade the overlay out over 1.5 seconds
        float duration = 1.5f;
        float elapsed = 0;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            _overlayGroup.alpha = 1f - t;
            yield return null;
        }

        _overlayGroup.alpha = 0;
        _overlayGroup.blocksRaycasts = false;

        // Unfreeze the player
        var player = WorldManager.Instance?.WorldPlayer;
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
        }

        // Show the HUD elements that were hidden
        OverworldIntegration.Instance?.ShowNotification("Welcome to the Spirit World.");

        // Clean up
        yield return new WaitForSeconds(0.5f);
        Destroy(_overlay);

        Debug.Log("[TitleScreen] Fade complete — player is free!");
    }
}
