using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Star Fox 64-style dialogue system for NPC interactions.
/// Typewriter text reveal with square wave blip sounds,
/// character portraits, and a message queue.
/// </summary>
public class SpiritComms : MonoBehaviour
{
    public static SpiritComms Instance { get; private set; }

    // =========================================================================
    // CONFIG
    // =========================================================================

    [Header("Timing")]
    public float CharDelay = 0.035f;    // 35ms per character
    public float PostTypePause = 0.5f;  // pause after typing finishes
    public float DismissHoldTime = 0f;  // 0 = click to dismiss immediately

    [Header("Audio")]
    public bool SfxEnabled = true;
    [Range(0f, 0.1f)]
    public float BlipVolume = 0.03f;
    public float BlipMinFreq = 220f;
    public float BlipMaxFreq = 300f;
    public float BlipDuration = 0.04f;
    public float CommOpenFreq = 800f;

    // =========================================================================
    // UI REFERENCES (built at runtime)
    // =========================================================================

    Canvas _canvas;
    GameObject _commPanel;          // compact bottom-center comm box (no overlay)
    GameObject _commBox;            // inner dark background
    RawImage _portraitImage;        // character portrait
    TextMeshProUGUI _nameText;      // character name (bold, colored)
    TextMeshProUGUI _dialogueText;  // typewriter text (white)
    TextMeshProUGUI _promptText;    // "[ E ]"
    TextMeshProUGUI _portraitLetter;// placeholder initial when no portrait
    Image _borderImage;             // outer border (colored per speaker)
    Image _portraitFrame;           // portrait background
    Image _colorBarImage;           // accent strip on portrait left edge
    GameObject _scanlineOverlay;

    // =========================================================================
    // STATE
    // =========================================================================

    readonly Queue<CommMessage> _queue = new();
    bool _isTyping;
    bool _waitingForDismiss;
    Coroutine _typewriterCor;
    Action _onSequenceComplete;

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>Show a single comm message. Queues if one is already showing.</summary>
    public void ShowComm(string speakerName, string text, Sprite portrait = null, Color? nameColor = null)
    {
        _queue.Enqueue(new CommMessage
        {
            Name = speakerName,
            Text = text,
            Portrait = portrait,
            NameColor = nameColor ?? Color.white
        });

        if (!_isTyping && !_waitingForDismiss)
            StartCoroutine(ProcessQueue());
    }

    /// <summary>Show a sequence of comm messages, with optional callback when done.</summary>
    public void ShowCommSequence(List<(string name, string text, Sprite portrait, Color color)> lines, Action onComplete = null)
    {
        foreach (var line in lines)
            _queue.Enqueue(new CommMessage
            {
                Name = line.name,
                Text = line.text,
                Portrait = line.portrait,
                NameColor = line.color
            });

        _onSequenceComplete = onComplete;

        if (!_isTyping && !_waitingForDismiss)
            StartCoroutine(ProcessQueue());
    }

    /// <summary>Show NPC dialogue using NPC name and dialogue text. Looks up portrait from card art if available.</summary>
    public void ShowNPCDialogue(string npcName, string text, Color? nameColor = null)
    {
        ShowComm(npcName, text, null, nameColor ?? new Color(0.5f, 0.8f, 1f));
    }

    /// <summary>Check if the comms system is currently active (showing or queued messages).</summary>
    public bool IsActive => _isTyping || _waitingForDismiss || _queue.Count > 0;

    /// <summary>Force close the comms overlay.</summary>
    public void ForceClose()
    {
        if (_typewriterCor != null) StopCoroutine(_typewriterCor);
        _isTyping = false;
        _waitingForDismiss = false;
        _queue.Clear();
        _onSequenceComplete = null;
        if (_commPanel != null) _commPanel.SetActive(false);
    }

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        BuildUI();
        _commPanel.SetActive(false);
    }

    void Update()
    {
        // Click or E or Space to dismiss
        if (_waitingForDismiss)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E) ||
                Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                _waitingForDismiss = false;
            }
        }

        // Skip typing with click
        if (_isTyping)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            {
                // Signal to skip (handled in coroutine via _skipRequested)
                _skipRequested = true;
            }
        }
    }

    bool _skipRequested;

    // =========================================================================
    // QUEUE PROCESSING
    // =========================================================================

    IEnumerator ProcessQueue()
    {
        while (_queue.Count > 0)
        {
            var msg = _queue.Dequeue();

            // Show panel — snap on, no fade
            _commPanel.SetActive(true);

            // Set portrait
            if (msg.Portrait != null)
            {
                _portraitImage.texture = msg.Portrait.texture;
                _portraitImage.gameObject.SetActive(true);
                if (_portraitLetter != null) _portraitLetter.gameObject.SetActive(false);
            }
            else
            {
                // Show initial letter as placeholder
                _portraitImage.gameObject.SetActive(false);
                if (_portraitLetter != null)
                {
                    _portraitLetter.gameObject.SetActive(true);
                    _portraitLetter.text = msg.Name.Length > 0 ? msg.Name[..1].ToUpper() : "?";
                    _portraitLetter.color = msg.NameColor;
                }
            }

            // Set name
            _nameText.text = msg.Name.ToUpper();
            _nameText.color = msg.NameColor;

            // Set border + accent bar to speaker's color
            Color borderColor = new Color(msg.NameColor.r, msg.NameColor.g, msg.NameColor.b, 0.8f);
            if (_borderImage != null) _borderImage.color = borderColor;
            if (_colorBarImage != null) _colorBarImage.color = msg.NameColor;
            if (_portraitFrame != null)
                _portraitFrame.color = new Color(msg.NameColor.r * 0.15f, msg.NameColor.g * 0.15f, msg.NameColor.b * 0.15f, 1f);

            // Clear text
            _dialogueText.text = "";
            _promptText.gameObject.SetActive(false);

            // Play comm open sound
            PlayCommOpenSound();

            // Typewriter
            _isTyping = true;
            _skipRequested = false;
            _typewriterCor = StartCoroutine(TypewriterEffect(msg.Text));
            yield return _typewriterCor;
            _isTyping = false;

            // Show dismiss prompt
            _promptText.gameObject.SetActive(true);
            _waitingForDismiss = true;

            // Wait for dismiss
            while (_waitingForDismiss)
                yield return null;

            // Brief pause between messages
            yield return new WaitForSeconds(0.1f);
        }

        // All messages done
        _commPanel.SetActive(false);
        _onSequenceComplete?.Invoke();
        _onSequenceComplete = null;
    }

    // =========================================================================
    // TYPEWRITER EFFECT
    // =========================================================================

    IEnumerator TypewriterEffect(string text)
    {
        _dialogueText.text = "";
        _dialogueText.maxVisibleCharacters = 0;
        _dialogueText.text = text;
        _dialogueText.ForceMeshUpdate();

        int totalChars = _dialogueText.textInfo.characterCount;

        for (int i = 0; i < totalChars; i++)
        {
            if (_skipRequested)
            {
                // Show all remaining text immediately
                _dialogueText.maxVisibleCharacters = totalChars;
                _skipRequested = false;
                break;
            }

            _dialogueText.maxVisibleCharacters = i + 1;

            // Blip on every other visible character
            if (i % 2 == 0)
                PlayTypeBlip();

            yield return new WaitForSeconds(CharDelay);
        }

        // Post-type pause
        yield return new WaitForSeconds(PostTypePause);
    }

    // =========================================================================
    // AUDIO — Square wave blips via AudioSource
    // =========================================================================

    void PlayTypeBlip()
    {
        if (!SfxEnabled) return;

        float freq = UnityEngine.Random.Range(BlipMinFreq, BlipMaxFreq);
        StartCoroutine(PlaySquareWave(freq, BlipVolume, BlipDuration));
    }

    void PlayCommOpenSound()
    {
        if (!SfxEnabled) return;

        // Descending pitch blip (800 -> 400 Hz feel)
        StartCoroutine(PlaySquareWave(CommOpenFreq, 0.06f, 0.08f));
    }

    IEnumerator PlaySquareWave(float frequency, float volume, float duration)
    {
        // Generate a short square wave clip
        int sampleRate = 44100;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        float period = sampleRate / frequency;
        for (int i = 0; i < sampleCount; i++)
        {
            // Square wave: +1 for first half of period, -1 for second half
            float phase = (i % period) / period;
            samples[i] = (phase < 0.5f ? 1f : -1f) * volume;

            // Apply quick fade-out envelope
            float envelope = 1f - ((float)i / sampleCount);
            samples[i] *= envelope;
        }

        var clip = AudioClip.Create("blip", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);

        var tempGO = new GameObject("BlipAudio");
        tempGO.transform.SetParent(transform);
        var source = tempGO.AddComponent<AudioSource>();
        source.clip = clip;
        source.playOnAwake = false;
        source.Play();

        yield return new WaitForSeconds(duration + 0.05f);
        Destroy(tempGO);
    }

    // =========================================================================
    // UI CONSTRUCTION
    // =========================================================================

    void BuildUI()
    {
        // Find or create canvas
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null)
        {
            var existing = FindAnyObjectByType<Canvas>();
            if (existing != null)
                _canvas = existing;
            else
            {
                var canvasGO = new GameObject("SpiritCommsCanvas");
                _canvas = canvasGO.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 100;
                var scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                canvasGO.AddComponent<GraphicRaycaster>();
            }
        }

        // =====================================================================
        // COMM PANEL — compact box at bottom-center, NO full-screen overlay
        // The panel itself acts as the colored border (2px via layout padding)
        // =====================================================================
        _commPanel = new GameObject("SpiritCommsPanel");
        _commPanel.transform.SetParent(_canvas.transform, false);
        var panelRect = _commPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.sizeDelta = new Vector2(720, 0);       // fixed width, auto height
        panelRect.anchoredPosition = new Vector2(0, 28);  // 28px from bottom

        // Border = the panel's own Image, colored per speaker
        _borderImage = _commPanel.AddComponent<Image>();
        _borderImage.color = new Color(0.5f, 0.5f, 0.5f, 0.9f);

        // 2px padding on all sides = the border thickness
        var panelLayout = _commPanel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(2, 2, 2, 2);
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = true;

        // Auto-height: shrink-wrap to content
        var panelFitter = _commPanel.AddComponent<ContentSizeFitter>();
        panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // =====================================================================
        // COMM BOX — dark interior, holds portrait + text
        // =====================================================================
        _commBox = new GameObject("CommBox");
        _commBox.transform.SetParent(_commPanel.transform, false);

        var boxBg = _commBox.AddComponent<Image>();
        boxBg.color = new Color(0.02f, 0.02f, 0.06f, 0.95f); // near-black

        var boxLayout = _commBox.AddComponent<HorizontalLayoutGroup>();
        boxLayout.padding = new RectOffset(14, 18, 14, 14);
        boxLayout.spacing = 14;
        boxLayout.childAlignment = TextAnchor.UpperLeft;
        boxLayout.childForceExpandWidth = false;
        boxLayout.childForceExpandHeight = true;

        var boxLE = _commBox.AddComponent<LayoutElement>();
        boxLE.flexibleWidth = 1;
        boxLE.minHeight = 130; // minimum so it never looks collapsed

        // =====================================================================
        // PORTRAIT FRAME — 96px with colored accent bar on left edge
        // =====================================================================
        var portraitFrame = new GameObject("PortraitFrame");
        portraitFrame.transform.SetParent(_commBox.transform, false);
        _portraitFrame = portraitFrame.AddComponent<Image>();
        _portraitFrame.color = new Color(0.08f, 0.08f, 0.12f, 1f);
        var pfLayout = portraitFrame.AddComponent<LayoutElement>();
        pfLayout.preferredWidth = 110;
        pfLayout.preferredHeight = 110;
        pfLayout.minWidth = 110;
        pfLayout.minHeight = 110;

        // Color bar — 4px accent strip on left edge (Star Fox shield bar)
        var colorBarGO = new GameObject("ColorBar");
        colorBarGO.transform.SetParent(portraitFrame.transform, false);
        _colorBarImage = colorBarGO.AddComponent<Image>();
        _colorBarImage.color = Color.white;
        var cbRect = colorBarGO.GetComponent<RectTransform>();
        cbRect.anchorMin = new Vector2(0, 0);
        cbRect.anchorMax = new Vector2(0, 1);
        cbRect.pivot = new Vector2(0, 0.5f);
        cbRect.sizeDelta = new Vector2(4, 0);
        cbRect.anchoredPosition = Vector2.zero;

        // Portrait image inside frame (inset from edges)
        var portraitGO = new GameObject("Portrait");
        portraitGO.transform.SetParent(portraitFrame.transform, false);
        _portraitImage = portraitGO.AddComponent<RawImage>();
        _portraitImage.color = Color.white;
        var piRect = portraitGO.GetComponent<RectTransform>();
        piRect.anchorMin = new Vector2(0.06f, 0.04f);
        piRect.anchorMax = new Vector2(0.96f, 0.96f);
        piRect.offsetMin = Vector2.zero;
        piRect.offsetMax = Vector2.zero;

        // Portrait placeholder letter (shown when no sprite)
        var letterGO = new GameObject("PortraitLetter");
        letterGO.transform.SetParent(portraitFrame.transform, false);
        _portraitLetter = letterGO.AddComponent<TextMeshProUGUI>();
        _portraitLetter.fontSize = 36;
        _portraitLetter.fontStyle = FontStyles.Bold;
        _portraitLetter.color = Color.white;
        _portraitLetter.alignment = TextAlignmentOptions.Center;
        _portraitLetter.enableWordWrapping = false;
        var plRect = letterGO.GetComponent<RectTransform>();
        plRect.anchorMin = Vector2.zero;
        plRect.anchorMax = Vector2.one;
        plRect.offsetMin = Vector2.zero;
        plRect.offsetMax = Vector2.zero;
        _portraitLetter.gameObject.SetActive(false);

        // Scanline overlay on portrait (CRT comm feel)
        var scanGO = new GameObject("PortraitScanlines");
        scanGO.transform.SetParent(portraitFrame.transform, false);
        var scanImg = scanGO.AddComponent<Image>();
        scanImg.color = new Color(0, 0, 0, 0.12f);
        scanImg.raycastTarget = false;
        var scanRect = scanGO.GetComponent<RectTransform>();
        scanRect.anchorMin = Vector2.zero;
        scanRect.anchorMax = Vector2.one;
        scanRect.offsetMin = Vector2.zero;
        scanRect.offsetMax = Vector2.zero;

        // =====================================================================
        // TEXT COLUMN — name, dialogue, prompt (tight spacing)
        // =====================================================================
        var textColumn = new GameObject("TextColumn");
        textColumn.transform.SetParent(_commBox.transform, false);
        var tcLayout = textColumn.AddComponent<LayoutElement>();
        tcLayout.flexibleWidth = 1;
        var tcVLayout = textColumn.AddComponent<VerticalLayoutGroup>();
        tcVLayout.spacing = 2;
        tcVLayout.childForceExpandWidth = true;
        tcVLayout.childForceExpandHeight = false;

        // Name — bold, colored per speaker type
        var nameGO = new GameObject("Name");
        nameGO.transform.SetParent(textColumn.transform, false);
        _nameText = nameGO.AddComponent<TextMeshProUGUI>();
        _nameText.fontSize = 22;
        _nameText.fontStyle = FontStyles.Bold;
        _nameText.color = Color.white;
        _nameText.enableWordWrapping = false;
        var nameLayout = nameGO.AddComponent<LayoutElement>();
        nameLayout.preferredHeight = 28;

        // Dialogue text — clean white, high contrast
        var textGO = new GameObject("DialogueText");
        textGO.transform.SetParent(textColumn.transform, false);
        _dialogueText = textGO.AddComponent<TextMeshProUGUI>();
        _dialogueText.fontSize = 21;
        _dialogueText.color = Color.white;
        _dialogueText.enableWordWrapping = true;
        _dialogueText.lineSpacing = 4;
        var textLayout = textGO.AddComponent<LayoutElement>();
        textLayout.flexibleHeight = 1;
        textLayout.minHeight = 30;

        // Dismiss prompt — snug bottom-right
        var promptGO = new GameObject("Prompt");
        promptGO.transform.SetParent(textColumn.transform, false);
        _promptText = promptGO.AddComponent<TextMeshProUGUI>();
        _promptText.text = "[ E ]";
        _promptText.fontSize = 14;
        _promptText.color = new Color(0.4f, 0.4f, 0.45f);
        _promptText.fontStyle = FontStyles.Italic;
        _promptText.alignment = TextAlignmentOptions.BottomRight;
        var promptLayout = promptGO.AddComponent<LayoutElement>();
        promptLayout.preferredHeight = 18;
        _promptText.gameObject.SetActive(false);

        // Subtle scanline overlay on entire comm box
        _scanlineOverlay = new GameObject("CommScanlines");
        _scanlineOverlay.transform.SetParent(_commBox.transform, false);
        var commScan = _scanlineOverlay.AddComponent<Image>();
        commScan.color = new Color(0, 0, 0, 0.04f);
        commScan.raycastTarget = false;
        var csr = _scanlineOverlay.GetComponent<RectTransform>();
        csr.anchorMin = Vector2.zero;
        csr.anchorMax = Vector2.one;
        csr.offsetMin = Vector2.zero;
        csr.offsetMax = Vector2.zero;
    }
}

// =========================================================================
// DATA
// =========================================================================

public class CommMessage
{
    public string Name;
    public string Text;
    public Sprite Portrait;
    public Color NameColor;
}
