
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(VisorHud))]
public class MissionUI : MonoBehaviour
{
    [Header("References")]
    public VirusTerminal terminal;
    public Font font;

    [Header("Positions")]
    public Vector2 timerPosition = new Vector2(0, -150);
    public Vector2 objectivePosition = new Vector2(100, -180);
    public Vector2 promptPosition = new Vector2(0, -100);
    public Vector2 uploadPosition = new Vector2(0, -200);

    [Header("Appearance")]
    public Color accentColor = new Color(0.45f, 0.85f, 0.95f);
    public Color warningColor = new Color(1f, 0.7f, 0.2f);
    public Color textColor = new Color(0.84f, 0.93f, 0.96f);

    [Range(0f, 1f)]
    public float opacity = 1f;

    public int timerFontSize = 32;
    public int objectiveFontSize = 24;
    public int promptFontSize = 24;
    public int uploadFontSize = 22;

    [Header("Upload Bar")]
    public Vector2 barSize = new Vector2(420, 18);
    public int stripeCount = 32;
    public float stripeAngle = 20f;

    [Header("Mission Completion")]
    public Behaviour[] disableOnCompletion;
    public bool pauseOnCompletion = true;

    private VisorHud visor;
    private RectTransform root;
    private CanvasGroup missionGroup;

    private Text timerText;
    private Text objectiveText;
    private Text promptText;

    private RectTransform uploadPanel;
    private Text uploadTitle;
    private Text uploadDescription;
    private Text uploadPercent;

    private Image uploadFill;
    private VisorHudBarWipe uploadWipe;

    private RectTransform endPanel;
    private Text finalTimeText;
    private Text bestTimeText;

    private bool resultsShown;
    private float previousTimeScale = 1f;

    private Texture2D stripeTexture;
    private Sprite stripeSprite;

    private string BestTimeKey =>
        "MissionBest_" + SceneManager.GetActiveScene().name;

    void Start()
    {
        visor = GetComponent<VisorHud>();

        if (font == null)
        {
            if (visor.healthLabel != null)
                font = visor.healthLabel.font;
            else if (visor.ammoLabel != null)
                font = visor.ammoLabel.font;
            else
                font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf"
                );
        }

        textColor = visor.labelColor;

        BuildUI();
    }

    void LateUpdate()
    {
        MissionManager mission = MissionManager.Instance;

        if (mission == null || root == null)
            return;

        ApplyLayout();

        timerText.text = mission.GetFormattedTime();

        UpdateObjective(mission.State);
        UpdateTerminal();

        if (mission.State ==
            MissionManager.MissionState.Completed)
        {
            if (!resultsShown)
                ShowResults();

            if (Keyboard.current != null &&
                Keyboard.current.rKey.wasPressedThisFrame)
            {
                RetryMission();
            }
        }
    }

    void UpdateObjective(MissionManager.MissionState state)
    {
        switch (state)
        {
            case MissionManager.MissionState.WaitingToStart:
                objectiveText.text =
                    "OPEN THE STARTING DOOR";
                break;

            case MissionManager.MissionState.ReachTerminal:
                objectiveText.text =
                    "LOCATE THE TERMINAL";
                break;

            case MissionManager.MissionState.Uploading:
                objectiveText.text =
                    "UPLOAD VIRUS TO TERMINAL";
                break;

            case MissionManager.MissionState.ReachExtraction:
                objectiveText.text =
                    "EXIT UNLOCKED\nPROCEED TO EXTRACTION";
                break;

            case MissionManager.MissionState.Completed:
                objectiveText.text =
                    "MISSION COMPLETE";
                break;
        }
    }

    void UpdateTerminal()
    {
        if (terminal == null || resultsShown)
        {
            promptText.gameObject.SetActive(false);
            uploadPanel.gameObject.SetActive(false);
            return;
        }

        promptText.gameObject.SetActive(
            terminal.CanInteract
        );

        bool showUpload =
            terminal.UploadStarted &&
            !terminal.UploadCompleted;

        uploadPanel.gameObject.SetActive(showUpload);

        if (!showUpload)
            return;

        float progress = terminal.UploadProgress;

        uploadPercent.text =
            Mathf.FloorToInt(progress * 100f) + "%";

        uploadWipe.Set(progress, Vector2.left);

        Color currentColor = terminal.IsPaused
            ? warningColor
            : accentColor;

        uploadFill.color = currentColor;
        uploadTitle.color = currentColor;
        uploadPercent.color = currentColor;

        if (terminal.IsPaused)
        {
            uploadTitle.text = "UPLOAD PAUSED";
            uploadDescription.text =
                "RETURN TO TRANSMISSION RANGE";
        }
        else
        {
            uploadTitle.text = "UPLOADING VIRUS";
            uploadDescription.text =
                "MAINTAIN PROXIMITY TO TERMINAL";
        }
    }

    // =========================================
    // UI GENERATION
    // =========================================

    void BuildUI()
    {
        root = CreateRect(
            "Mission HUD",
            transform,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero
        );

        Stretch(root);

        missionGroup =
            root.gameObject.AddComponent<CanvasGroup>();

        missionGroup.blocksRaycasts = false;
        missionGroup.interactable = false;

        timerText = CreateText(
            "Mission Timer",
            root,
            "00:00.00",
            timerFontSize,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 1f),
            timerPosition,
            new Vector2(400, 60),
            textColor
        );

        objectiveText = CreateText(
            "Mission Objective",
            root,
            "OPEN THE STARTING DOOR",
            objectiveFontSize,
            TextAnchor.UpperLeft,
            new Vector2(0f, 1f),
            objectivePosition,
            new Vector2(600, 100),
            textColor
        );

        objectiveText.rectTransform.pivot =
            new Vector2(0f, 1f);

        promptText = CreateText(
            "Interaction Prompt",
            root,
            "[ E ]  INITIATE VIRUS UPLOAD",
            promptFontSize,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f),
            promptPosition,
            new Vector2(700, 60),
            accentColor
        );

        BuildUploadPanel();
        BuildEndScreen();

        promptText.gameObject.SetActive(false);
        uploadPanel.gameObject.SetActive(false);
        endPanel.gameObject.SetActive(false);
    }

    void BuildUploadPanel()
    {
        uploadPanel = CreateRect(
            "Virus Upload",
            root,
            new Vector2(0.5f, 0.5f),
            uploadPosition,
            new Vector2(600, 130)
        );

        uploadTitle = CreateText(
            "Upload Title",
            uploadPanel,
            "UPLOADING VIRUS",
            uploadFontSize,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f),
            new Vector2(0, 45),
            new Vector2(600, 35),
            accentColor
        );

        RectTransform barBack = CreateRect(
            "Upload Bar Background",
            uploadPanel,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            barSize
        );

        CreateStripeSprite();

        Image background =
            barBack.gameObject.AddComponent<Image>();

        background.sprite = stripeSprite;
        background.color =
            new Color(0.15f, 0.3f, 0.35f, 0.45f);

        background.raycastTarget = false;

        RectTransform fillRect = CreateRect(
            "Upload Bar Fill",
            barBack,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            barSize
        );

        Stretch(fillRect);

        uploadFill =
            fillRect.gameObject.AddComponent<Image>();

        uploadFill.sprite = stripeSprite;
        uploadFill.color = accentColor;
        uploadFill.type = Image.Type.Simple;
        uploadFill.raycastTarget = false;

        uploadWipe =
            fillRect.gameObject.AddComponent<VisorHudBarWipe>();

        uploadWipe.Set(0f, Vector2.left);

        uploadPercent = CreateText(
            "Upload Percentage",
            uploadPanel,
            "0%",
            uploadFontSize,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f),
            new Vector2(0, -27),
            new Vector2(300, 35),
            accentColor
        );

        uploadDescription = CreateText(
            "Upload Description",
            uploadPanel,
            "MAINTAIN PROXIMITY TO TERMINAL",
            18,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f),
            new Vector2(0, -55),
            new Vector2(700, 35),
            textColor
        );
    }

    void CreateStripeSprite()
    {
        int width = 1024;
        int height = 64;

        stripeTexture = new Texture2D(
            width, height,
            TextureFormat.RGBA32,
            false
        );

        stripeTexture.filterMode = FilterMode.Bilinear;
        stripeTexture.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[width * height];

        float period = width /
            (float)Mathf.Max(stripeCount, 1);

        float slope = Mathf.Tan(
            stripeAngle * Mathf.Deg2Rad
        );

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float stripe = Mathf.Repeat(
                    x + y * slope, period
                );

                byte alpha = stripe < period * 0.72f
                    ? (byte)255
                    : (byte)0;

                pixels[y * width + x] =
                    new Color32(255, 255, 255, alpha);
            }
        }

        stripeTexture.SetPixels32(pixels);
        stripeTexture.Apply();

        stripeSprite = Sprite.Create(
            stripeTexture,
            new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f)
        );
    }

    // =========================================
    // RESULTS SCREEN
    // =========================================

    void BuildEndScreen()
    {
        endPanel = CreateRect(
            "Mission Results",
            root,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero
        );

        Stretch(endPanel);

        Image background =
            endPanel.gameObject.AddComponent<Image>();

        background.color =
            new Color(0.015f, 0.025f, 0.035f, 0.96f);

        background.raycastTarget = true;

        CreateText(
            "Results Heading",
            endPanel,
            "MISSION COMPLETE",
            56,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f),
            new Vector2(0, 200),
            new Vector2(1000, 90),
            accentColor
        );

        CreateText(
            "Results Subtitle",
            endPanel,
            "EXTRACTION SUCCESSFUL",
            24,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f),
            new Vector2(0, 130),
            new Vector2(800, 50),
            textColor
        );

        CreateText(
            "Completion Time Label",
            endPanel,
            "COMPLETION TIME",
            22,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f),
            new Vector2(0, 40),
            new Vector2(600, 40),
            textColor
        );

        finalTimeText = CreateText(
            "Final Time",
            endPanel,
            "00:00.00",
            80,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f),
            new Vector2(0, -30),
            new Vector2(800, 110),
            Color.white
        );

        bestTimeText = CreateText(
            "Personal Best",
            endPanel,
            "",
            26,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f),
            new Vector2(0, -115),
            new Vector2(800, 50),
            accentColor
        );

        RectTransform retryRect = CreateRect(
            "Retry Button",
            endPanel,
            new Vector2(0.5f, 0.5f),
            new Vector2(0, -220),
            new Vector2(320, 64)
        );

        Image retryImage =
            retryRect.gameObject.AddComponent<Image>();

        retryImage.color =
            new Color(0.1f, 0.25f, 0.3f, 1f);

        Button retryButton =
            retryRect.gameObject.AddComponent<Button>();

        retryButton.targetGraphic = retryImage;
        retryButton.onClick.AddListener(RetryMission);

        CreateText(
            "Retry Label",
            retryRect,
            "RETRY MISSION  [ R ]",
            24,
            TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(320, 64),
            textColor
        );

        CanvasGroup resultsGroup =
            endPanel.gameObject.AddComponent<CanvasGroup>();

        resultsGroup.ignoreParentGroups = true;
        resultsGroup.interactable = true;
        resultsGroup.blocksRaycasts = true;
    }

    void ShowResults()
    {
        resultsShown = true;

        float finalTime =
            MissionManager.Instance.ElapsedTime;

        float bestTime = PlayerPrefs.GetFloat(
            BestTimeKey, float.MaxValue
        );

        bool newRecord = finalTime < bestTime;

        if (newRecord)
        {
            bestTime = finalTime;

            PlayerPrefs.SetFloat(BestTimeKey, bestTime);
            PlayerPrefs.Save();
        }

        finalTimeText.text = FormatTime(finalTime);

        bestTimeText.text = newRecord
            ? "NEW PERSONAL BEST"
            : "PERSONAL BEST  " + FormatTime(bestTime);

        timerText.gameObject.SetActive(false);
        objectiveText.gameObject.SetActive(false);
        promptText.gameObject.SetActive(false);
        uploadPanel.gameObject.SetActive(false);

        endPanel.gameObject.SetActive(true);
        endPanel.SetAsLastSibling();

        foreach (Behaviour control in disableOnCompletion)
        {
            if (control != null && control != this)
                control.enabled = false;
        }

        previousTimeScale = Time.timeScale;

        if (pauseOnCompletion)
            Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnsureEventSystem();
    }

    public void RetryMission()
    {
        Time.timeScale = previousTimeScale;

        SceneManager.LoadScene(
            SceneManager.GetActiveScene().name
        );
    }

    void EnsureEventSystem()
    {
        Canvas canvas = GetComponent<Canvas>();

        if (canvas != null &&
            canvas.GetComponent<GraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        if (EventSystem.current == null)
        {
            GameObject eventObject =
                new GameObject("EventSystem");

            eventObject.AddComponent<EventSystem>();
            eventObject.AddComponent<InputSystemUIInputModule>();
        }
    }

    // =========================================
    // HELPERS
    // =========================================

    void ApplyLayout()
    {
        missionGroup.alpha = opacity;

        timerText.rectTransform.anchoredPosition =
            timerPosition;

        objectiveText.rectTransform.anchoredPosition =
            objectivePosition;

        promptText.rectTransform.anchoredPosition =
            promptPosition;

        uploadPanel.anchoredPosition =
            uploadPosition;

        timerText.fontSize = timerFontSize;
        objectiveText.fontSize = objectiveFontSize;
        promptText.fontSize = promptFontSize;
        uploadTitle.fontSize = uploadFontSize;
        uploadPercent.fontSize = uploadFontSize;

        RectTransform barBack =
            uploadFill.rectTransform.parent as RectTransform;

        barBack.sizeDelta = barSize;
    }

    RectTransform CreateRect(
        string objectName,
        Transform parent,
        Vector2 anchor,
        Vector2 position,
        Vector2 size)
    {
        GameObject go = new GameObject(
            objectName,
            typeof(RectTransform)
        );

        RectTransform rt =
            go.GetComponent<RectTransform>();

        rt.SetParent(parent, false);

        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        return rt;
    }

    void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    Text CreateText(
        string objectName,
        Transform parent,
        string content,
        int size,
        TextAnchor alignment,
        Vector2 anchor,
        Vector2 position,
        Vector2 dimensions,
        Color color)
    {
        RectTransform rt = CreateRect(
            objectName,
            parent,
            anchor,
            position,
            dimensions
        );

        Text text = rt.gameObject.AddComponent<Text>();

        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;

        text.horizontalOverflow =
            HorizontalWrapMode.Overflow;

        text.verticalOverflow =
            VerticalWrapMode.Overflow;

        Shadow shadow =
            rt.gameObject.AddComponent<Shadow>();

        shadow.effectColor =
            new Color(0f, 0f, 0f, 0.65f);

        shadow.effectDistance =
            new Vector2(1f, -1f);

        return text;
    }

    string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        int hundredths =
            Mathf.FloorToInt((time % 1f) * 100f);

        return $"{minutes:00}:{seconds:00}.{hundredths:00}";
    }

    void OnDestroy()
    {
        if (resultsShown && pauseOnCompletion)
            Time.timeScale = previousTimeScale;

        if (root != null)
            Destroy(root.gameObject);

        if (stripeSprite != null)
            Destroy(stripeSprite);

        if (stripeTexture != null)
            Destroy(stripeTexture);
    }
}
