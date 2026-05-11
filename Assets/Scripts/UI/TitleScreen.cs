using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Clean cinematic title screen. Hides ALL HUD, zooms camera in on the player,
/// shows just "BATTLE OF ORIGINS" + START button. Press START → fade out → game begins.
/// </summary>
public class TitleScreen : MonoBehaviour
{
    public static TitleScreen Instance { get; private set; }
    public static bool HasStarted { get; private set; }

    Canvas _canvas;
    CanvasGroup _overlayGroup;
    GameObject _overlay;
    bool _isActive;

    // Saved state to restore after START
    Vector3 _savedCamPos;
    Quaternion _savedCamRot;
    float _savedCamFov;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Show()
    {
        if (HasStarted) return;

        _isActive = true;

        // Hide ALL HUD elements
        HideHUD();

        // Freeze the player
        var player = WorldManager.Instance?.WorldPlayer;
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            // Rotate player to face the camera
            player.transform.rotation = Quaternion.Euler(0, 180f, 0);
        }

        // Zoom camera in for cinematic framing
        SetupCamera();

        // Build minimal UI
        BuildUI();

        Debug.Log("[TitleScreen] Showing — HUD hidden, camera zoomed, waiting for START");
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;

        // Save current state to restore later
        _savedCamPos = cam.transform.position;
        _savedCamRot = cam.transform.rotation;
        _savedCamFov = cam.fieldOfView;

        // Position camera closer to the player, slightly above, looking down
        var player = WorldManager.Instance?.WorldPlayer;
        if (player != null)
        {
            Vector3 playerPos = player.transform.position;
            cam.transform.position = playerPos + new Vector3(0, 3f, -5f);
            cam.transform.LookAt(playerPos + Vector3.up * 1.5f);
        }

        // Tighter FOV for portrait feel
        cam.fieldOfView = 40f;
    }

    void HideHUD()
    {
        // Hide the entire overlay canvas (HUD, chat, skill bars, everything)
        if (OverworldIntegration.Instance?._overlayCanvas != null)
        {
            var canvasGO = OverworldIntegration.Instance._overlayCanvas.gameObject;
            // Don't disable the canvas — we need it for our title UI
            // Instead, hide all existing children
            for (int i = 0; i < canvasGO.transform.childCount; i++)
            {
                var child = canvasGO.transform.GetChild(i);
                child.gameObject.SetActive(false);
            }
        }
    }

    void ShowHUD()
    {
        if (OverworldIntegration.Instance?._overlayCanvas != null)
        {
            var canvasGO = OverworldIntegration.Instance._overlayCanvas.gameObject;
            for (int i = 0; i < canvasGO.transform.childCount; i++)
            {
                var child = canvasGO.transform.GetChild(i);
                // Don't re-show our overlay (it's being destroyed anyway)
                if (child.name == "TitleScreenOverlay") continue;
                child.gameObject.SetActive(true);
            }
        }
    }

    void BuildUI()
    {
        _canvas = OverworldIntegration.Instance?._overlayCanvas;
        if (_canvas == null)
        {
            var existing = FindAnyObjectByType<Canvas>();
            if (existing != null) _canvas = existing;
            else return;
        }

        // ── Overlay container ──
        _overlay = new GameObject("TitleScreenOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        _overlay.transform.SetAsLastSibling();

        _overlayGroup = _overlay.AddComponent<CanvasGroup>();
        _overlayGroup.alpha = 1f;
        _overlayGroup.blocksRaycasts = true;

        var overlayRect = _overlay.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        // ── Subtle top vignette for title readability ──
        var topGrad = new GameObject("TopGrad", typeof(RectTransform), typeof(Image));
        topGrad.transform.SetParent(_overlay.transform, false);
        topGrad.GetComponent<Image>().color = new Color(0, 0, 0, 0.35f);
        topGrad.GetComponent<Image>().raycastTarget = false;
        var topRect = topGrad.GetComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0, 0.7f);
        topRect.anchorMax = Vector2.one;
        topRect.offsetMin = Vector2.zero;
        topRect.offsetMax = Vector2.zero;

        // ── Title — "BATTLE OF ORIGINS" ──
        var titleGO = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleGO.transform.SetParent(_overlay.transform, false);
        var titleTMP = titleGO.GetComponent<TextMeshProUGUI>();
        titleTMP.text = "BATTLE OF ORIGINS";
        titleTMP.fontSize = 56;
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
        subTMP.color = new Color(1, 1, 1, 0.65f);
        subTMP.alignment = TextAlignmentOptions.Center;
        var subRect = subGO.GetComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0, 0.76f);
        subRect.anchorMax = new Vector2(1, 0.82f);
        subRect.offsetMin = Vector2.zero;
        subRect.offsetMax = Vector2.zero;

        // ── START button — centered, below the character ──
        var btnGO = new GameObject("StartBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(_overlay.transform, false);
        btnGO.GetComponent<Image>().color = new Color(0.95f, 0.95f, 0.92f, 0.9f);
        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.35f, 0.12f);
        btnRect.anchorMax = new Vector2(0.65f, 0.22f);
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;

        var outline = btnGO.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(0.2f, 0.2f, 0.2f, 0.7f);
        outline.effectDistance = new Vector2(2, -2);

        var btnTextGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnTMP = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnTMP.text = "START";
        btnTMP.fontSize = 36;
        btnTMP.fontStyle = FontStyles.Bold;
        btnTMP.color = new Color(0.15f, 0.15f, 0.15f);
        btnTMP.alignment = TextAlignmentOptions.Center;
        var btRect = btnTextGO.GetComponent<RectTransform>();
        btRect.anchorMin = Vector2.zero;
        btRect.anchorMax = Vector2.one;
        btRect.offsetMin = Vector2.zero;
        btRect.offsetMax = Vector2.zero;

        btnGO.GetComponent<Button>().onClick.AddListener(OnStartPressed);
    }

    void OnStartPressed()
    {
        if (!_isActive) return;
        _isActive = false;
        HasStarted = true;

        Debug.Log("[TitleScreen] START pressed!");
        StartCoroutine(FadeOutAndBegin());
    }

    IEnumerator FadeOutAndBegin()
    {
        float duration = 1.5f;
        float elapsed = 0;

        // Lerp camera back to saved position while fading
        var cam = Camera.main;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float smooth = t * t * (3f - 2f * t); // smoothstep

            _overlayGroup.alpha = 1f - smooth;

            // Smooth camera back to gameplay position
            if (cam != null)
            {
                cam.fieldOfView = Mathf.Lerp(40f, _savedCamFov, smooth);
                // Let the camera controller take over — just restore FOV
            }

            yield return null;
        }

        _overlayGroup.alpha = 0;
        _overlayGroup.blocksRaycasts = false;

        // Restore camera
        if (cam != null)
            cam.fieldOfView = _savedCamFov;

        // Unfreeze player and rotate back to normal
        var player = WorldManager.Instance?.WorldPlayer;
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            player.transform.rotation = Quaternion.identity;
        }

        // Show HUD
        ShowHUD();

        yield return new WaitForSeconds(0.3f);
        Destroy(_overlay);

        Debug.Log("[TitleScreen] Game started — HUD restored, player free");
    }
}
