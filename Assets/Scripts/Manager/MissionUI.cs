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
    public MissionVoice missionVoice;

    [Tooltip("Only used for the results screen.")]
    public Font font;

    [Header("My UI elements (drag them in from the Hierarchy)")]
    public Text stageText;

    [Tooltip("The object that holds the bar's Back and Fill.")]
    public GameObject progressBar;

    [Tooltip("The Fill image of the bar.")]
    public Image progressFill;

    public Text progressText;
    public Text interactText;
    public Text timerText;

    [Header("Texts")]
    public string interactString = "「 E 」ウイルスをアップロードする";
    public string waitingToStartText = "スタートドアを開ける";
    public string reachTerminalText = "ターミナルを探す";
    public string uploadingText = "アップロード中";
    public string uploadPausedText = "一時停止中。ターミナルに戻る";
    public string uploadCompleteText = "アップロード完了";
    public string reachExtractionText = "出口のロック解除。脱出地点へ向かう";

    [Header("Upload paused look (the player left the upload zone)")]
    [Tooltip("Colour of the stage text and of the progress bar fill while the upload is paused.")]
    public Color pausedColor = new Color(1f, 0.5f, 0.5f, 1f);

    [Header("Timing")]
    [Tooltip("Seconds 'upload complete' and the full bar stay on screen before the next stage.")]
    public float uploadCompleteDelay = 3f;

    [Tooltip("Only used when Show Results Screen is ticked.")]
    public float resultsDelay = 3f;

    [Header("Results screen")]
    [Tooltip("Off = after the last room you only see the timer and the visor. On = the results screen covers everything after Results Delay seconds.")]
    public bool showResultsScreen = false;

    public Color accentColor = new Color(0.45f, 0.85f, 0.95f);
    public Color textColor = new Color(0.84f, 0.93f, 0.96f);

    [Header("Return To Title")]
    public Button returnToTitleButton;
    public string titleSceneName = "intro";

    [Header("Mission Completion")]
    [Tooltip("Drag the player's movement, look and weapon scripts here.")]
    public Behaviour[] disableOnCompletion;

    [Tooltip("Freezes the whole game when the last room is reached.")]
    public bool pauseOnCompletion = true;

    private VisorHud visor;
    private RectTransform root;
    private VisorHudBarWipe progressWipe;

    private RectTransform endPanel;
    private Text finalTimeText;
    private Text bestTimeText;

    private MissionManager.MissionState lastState;
    private float uploadCompleteUntil;
    private float completedAt = -1f;
    private int lastPercent = -1;

    private Color stageNormalColor = Color.white;
    private Color fillNormalColor = Color.white;
    private bool pausedStyleOn;

    private bool resultsShown;
    private bool playerDead;
    private bool frozen;
    private float previousTimeScale = 1f;

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

        // Hide the return button until mission completion.
        if (returnToTitleButton != null)
        {
            returnToTitleButton.gameObject.SetActive(false);
            returnToTitleButton.onClick.AddListener(ReturnToTitle);
        }

        // Auto-create stageText if it was not wired in the Inspector
        // (protects against broken prefab references in WebGL builds).
        if (stageText == null)
        {
            GameObject stageGo = new GameObject("Stage Text (auto)");
            stageGo.transform.SetParent(transform, false);
            RectTransform stageRt = stageGo.AddComponent<RectTransform>();
            // Anchor to top-centre, just below the compass bar (~140 canvas units from top).
            stageRt.anchorMin = new Vector2(0.5f, 1f);
            stageRt.anchorMax = new Vector2(0.5f, 1f);
            stageRt.pivot     = new Vector2(0.5f, 1f);
            stageRt.anchoredPosition = new Vector2(0f, -145f);
            stageRt.sizeDelta = new Vector2(900f, 40f);
            stageText = stageGo.AddComponent<Text>();
            stageText.font      = font;
            stageText.fontSize  = 22;
            stageText.fontStyle = FontStyle.Bold;
            stageText.alignment = TextAnchor.UpperCenter;
            stageText.color     = new Color(0.84f, 0.93f, 0.96f, 1f);
            stageText.horizontalOverflow = HorizontalWrapMode.Overflow;
            stageText.verticalOverflow   = VerticalWrapMode.Overflow;
            stageText.raycastTarget = false;
            Shadow sh = stageGo.AddComponent<Shadow>();
            sh.effectColor    = new Color(0f, 0f, 0f, 0.65f);
            sh.effectDistance = new Vector2(1f, -1f);
        }

        // Remember the normal colours.
        if (stageText != null)
            stageNormalColor = stageText.color;

        if (progressFill != null)
            fillNormalColor = progressFill.color;

        // Set up the progress bar wipe.
        if (progressFill != null)
        {
            progressFill.type = Image.Type.Simple;

            progressWipe =
                progressFill.GetComponent<VisorHudBarWipe>();

            if (progressWipe == null)
                progressWipe = progressFill.gameObject
                    .AddComponent<VisorHudBarWipe>();

            progressWipe.Set(0f, Vector2.left);
        }

        SetText(interactText, interactString);

        SetShown(timerText, false);
        SetShown(interactText, false);
        SetShown(progressBar, false);
        SetShown(progressText, false);
    }

    void LateUpdate()
    {
        MissionManager mission = MissionManager.Instance;

        if (mission == null || root == null)
            return;

        // Player is dead.
        if (playerDead)
            return;

        if (resultsShown)
        {
            if (Keyboard.current != null &&
                Keyboard.current.rKey.wasPressedThisFrame)
            {
                RetryMission();
            }

            return;
        }

        MissionManager.MissionState state = mission.State;

        if (state == MissionManager.MissionState.Completed)
        {
            // Only runs once upon mission completion.
            if (completedAt < 0f)
            {
                completedAt = Time.unscaledTime;

                if (missionVoice != null)
                    missionVoice.Play(
                        MissionVoice.Cue.MissionComplete
                    );

                SetText(timerText, mission.GetFormattedTime());
                SetShown(timerText, true);

                visor.SetCrosshairHidden(true);

                SetShown(stageText, false);
                SetShown(interactText, false);
                SetShown(progressBar, false);
                SetShown(progressText, false);

                FreezeGame();

                // Show the existing return-to-title button.
                if (returnToTitleButton != null)
                {
                    returnToTitleButton.gameObject.SetActive(true);
                    returnToTitleButton.transform.SetAsLastSibling();
                }

                // Release the FPS cursor.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                EnsureEventSystem();
            }

            // Optional generated results screen.
            if (showResultsScreen &&
                Time.unscaledTime - completedAt >= resultsDelay)
            {
                ShowResults();
            }

            return;
        }

        // Upload just finished.
        if (lastState == MissionManager.MissionState.Uploading &&
            state == MissionManager.MissionState.ReachExtraction)
        {
            uploadCompleteUntil = Time.time + uploadCompleteDelay;
        }

        lastState = state;

        UpdateStage(state);
        UpdatePausedStyle(state);
        UpdateUpload(state);
        UpdateInteract();
    }

    // =========================================
    // PLAYER DEATH
    // =========================================

    public void HideForDeath()
    {
        playerDead = true;

        if (visor == null)
            visor = GetComponent<VisorHud>();

        SetShown(stageText, false);
        SetShown(interactText, false);
        SetShown(timerText, false);
        SetShown(progressBar, false);
        SetShown(progressText, false);
        SetShown(returnToTitleButton, false);

        if (visor != null)
            visor.SetCrosshairHidden(true);
    }

    bool UploadCompleteVisible(MissionManager.MissionState state)
    {
        return state == MissionManager.MissionState.ReachExtraction &&
               Time.time < uploadCompleteUntil;
    }

    // =========================================
    // MISSION STAGE
    // =========================================

    void UpdateStage(MissionManager.MissionState state)
    {
        string text;
        MissionVoice.Cue cue;

        switch (state)
        {
            case MissionManager.MissionState.WaitingToStart:

                text = waitingToStartText;
                cue = MissionVoice.Cue.WaitingToStart;
                break;

            case MissionManager.MissionState.ReachTerminal:

                text = reachTerminalText;

                cue = terminal != null && terminal.CanInteract
                    ? MissionVoice.Cue.TerminalInRange
                    : MissionVoice.Cue.ReachTerminal;

                break;

            case MissionManager.MissionState.Uploading:

                bool paused = terminal != null && terminal.IsPaused;

                text = paused
                    ? uploadPausedText
                    : uploadingText;

                cue = MissionVoice.Cue.Uploading;
                break;

            default:

                bool complete = UploadCompleteVisible(state);

                text = complete
                    ? uploadCompleteText
                    : reachExtractionText;

                cue = complete
                    ? MissionVoice.Cue.UploadComplete
                    : MissionVoice.Cue.ReachExtraction;

                break;
        }

        SetText(stageText, text);

        if (missionVoice != null)
            missionVoice.Play(cue);
    }

    // =========================================
    // UPLOAD PAUSED STYLE
    // =========================================

    void UpdatePausedStyle(MissionManager.MissionState state)
    {
        bool paused =
            state == MissionManager.MissionState.Uploading &&
            terminal != null &&
            terminal.IsPaused;

        if (paused == pausedStyleOn)
            return;

        pausedStyleOn = paused;

        if (stageText != null)
            stageText.color = paused
                ? WithAlpha(pausedColor, stageNormalColor.a)
                : stageNormalColor;

        if (progressFill != null)
            progressFill.color = paused
                ? WithAlpha(pausedColor, fillNormalColor.a)
                : fillNormalColor;
    }

    static Color WithAlpha(Color c, float alpha)
    {
        c.a = alpha;
        return c;
    }

    // =========================================
    // UPLOAD PROGRESS
    // =========================================

    void UpdateUpload(MissionManager.MissionState state)
    {
        bool show =
            state == MissionManager.MissionState.Uploading ||
            UploadCompleteVisible(state);

        SetShown(progressBar, show);
        SetShown(progressText, show);

        if (!show)
            return;

        float progress = terminal != null
            ? terminal.UploadProgress
            : 0f;

        if (progressWipe != null)
            progressWipe.Set(progress, Vector2.left);

        int percent = Mathf.FloorToInt(progress * 100f);

        if (percent != lastPercent)
        {
            lastPercent = percent;
            SetText(progressText, percent + "%");
        }
    }

    void UpdateInteract()
    {
        bool show = terminal != null && terminal.CanInteract;

        SetShown(interactText, show);

        visor.SetCrosshairHidden(show);
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

        BuildEndScreen();

        endPanel.gameObject.SetActive(false);
    }

    // =========================================
    // OPTIONAL RESULTS SCREEN
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

    // =========================================
    // FREEZE GAME
    // =========================================

    void FreezeGame()
    {
        foreach (Behaviour control in disableOnCompletion)
        {
            if (control != null && control != this)
                control.enabled = false;
        }

        // With an ExtractionSequence in the scene the game keeps running and the sequence fades the sound itself.
        if (pauseOnCompletion && !frozen && ExtractionSequence.Instance == null)
        {
            frozen = true;
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            // Pause all audio except the mission voice.
            if (missionVoice != null)
            {
                foreach (AudioSource voice in
                    missionVoice.GetComponentsInChildren<AudioSource>(true))
                {
                    voice.ignoreListenerPause = true;
                }
            }

            AudioListener.pause = true;
        }
    }

    // =========================================
    // SHOW OPTIONAL RESULTS
    // =========================================

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

        SetShown(timerText, false);
        SetShown(stageText, false);
        SetShown(interactText, false);
        SetShown(progressBar, false);
        SetShown(progressText, false);

        endPanel.gameObject.SetActive(true);
        endPanel.SetAsLastSibling();
    }

    // =========================================
    // SCENE NAVIGATION
    // =========================================

    public void ReturnToTitle()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        frozen = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SceneManager.LoadScene(titleSceneName);
    }

    public void RetryMission()
    {
        Time.timeScale = previousTimeScale;
        AudioListener.pause = false;
        frozen = false;

        SceneManager.LoadScene(
            SceneManager.GetActiveScene().name
        );
    }

    // =========================================
    // EVENT SYSTEM
    // =========================================

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

    static void SetText(Text text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
    }

    static void SetShown(Component element, bool on)
    {
        if (element != null && element.gameObject.activeSelf != on)
            element.gameObject.SetActive(on);
    }

    static void SetShown(GameObject element, bool on)
    {
        if (element != null && element.activeSelf != on)
            element.SetActive(on);
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
        if (returnToTitleButton != null)
            returnToTitleButton.onClick.RemoveListener(ReturnToTitle);

        if (frozen)
        {
            Time.timeScale = previousTimeScale;
            AudioListener.pause = false;
        }

        if (root != null)
            Destroy(root.gameObject);
    }
}