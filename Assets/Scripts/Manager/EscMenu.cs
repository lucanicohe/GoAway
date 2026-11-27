using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// ESC menu. Put this on an object that is ACTIVE from the start (for example the GameManager object).
///
/// ESC          : pauses the game, shows the End button, switches the Depth Of Field on, frees the mouse cursor.
/// Hover button : the End button gets slightly bigger.
/// Click button : back to the intro scene.
/// ESC again    : everything goes back to how it was and the game continues.
/// </summary>
public class EscMenu : MonoBehaviour
{
    [Header("Menu")]
    [Tooltip("Your End button object (a UI object with an Image). It is switched off at the start and set active while the game is paused.")]
    [SerializeField] private GameObject endButton;
    [Tooltip("How much bigger the End button gets while the mouse is over it. 1.1 = 10% bigger.")]
    [Range(1f, 1.5f)]
    [SerializeField] private float hoverScale = 1.1f;
    [Tooltip("The scene that is loaded when the End button is clicked. It must be in File > Build Settings > Scenes In Build.")]
    [SerializeField] private string introSceneName = "intro";

    [Header("Post Process")]
    [Tooltip("Drag the Volume object that holds your post processing here. Its Depth Of Field override is switched on while paused " +
             "and goes back to what it was when the game continues.")]
    [SerializeField] private UnityEngine.Rendering.Volume postProcessVolume;

    [Header("Player scripts switched off while paused")]
    [Tooltip("Drag the player's look, movement and weapon scripts here, so the camera does not turn and the blaster does not fire while you use the mouse in the menu. " +
             "If this stays empty, the 'Disable On Completion' list of MissionUI is used instead.")]
    [SerializeField] private Behaviour[] disableWhilePaused;

    private bool paused;
    private float previousTimeScale = 1f;

    private CursorLockMode previousLockState;
    private bool previousCursorVisible;

    private RectTransform buttonRect;
    private Vector3 buttonNormalScale = Vector3.one;
    private UnityEngine.Camera uiCamera;
    private bool hovering;

    private UnityEngine.Rendering.VolumeComponent depthOfField;
    private bool depthOfFieldWasActive;

    private Behaviour[] switchOff;
    private bool[] wasEnabled;

    private GameManager gameManager;

    void Start()
    {
        gameManager = FindFirstObjectByType<GameManager>();

        if (endButton != null)
        {
            buttonRect = endButton.GetComponent<RectTransform>();
            buttonNormalScale = endButton.transform.localScale;
            endButton.SetActive(false);
        }
        else
        {
            Debug.LogWarning("[EscMenu] No End button dragged in.", this);
        }

        FindDepthOfField();
        FindScriptsToSwitchOff();
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (paused)
                Resume();
            else if (CanPause())
                Pause();
        }

        if (paused)
            UpdateButton();
    }

    // not while the player is dead or the last room is reached: the game is already stopped there
    bool CanPause()
    {
        if (Time.timeScale <= 0f)
            return false;

        if (gameManager != null && !gameManager.PlayerAlive)
            return false;

        if (MissionManager.Instance != null &&
            MissionManager.Instance.State == MissionManager.MissionState.Completed)
            return false;

        return true;
    }

    void Pause()
    {
        paused = true;

        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        // all audio pauses too
        AudioListener.pause = true;

        // the mouse is needed for the button
        previousLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // look, movement and weapon off (each one remembers if it was on)
        for (int i = 0; i < switchOff.Length; i++)
        {
            Behaviour script = switchOff[i];

            wasEnabled[i] = script != null && script != this && script.enabled;

            if (wasEnabled[i])
                script.enabled = false;
        }

        // Depth Of Field on
        if (depthOfField != null)
        {
            depthOfFieldWasActive = depthOfField.active;
            depthOfField.active = true;
        }

        if (endButton != null)
        {
            endButton.SetActive(true);
            endButton.transform.localScale = buttonNormalScale;
            hovering = false;

            UnityEngine.Canvas canvas = endButton.GetComponentInParent<UnityEngine.Canvas>();

            if (canvas != null)
            {
                canvas = canvas.rootCanvas;

                uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : canvas.worldCamera;
            }
        }
    }

    void Resume()
    {
        paused = false;

        if (endButton != null)
        {
            endButton.transform.localScale = buttonNormalScale;
            endButton.SetActive(false);
        }

        hovering = false;

        if (depthOfField != null)
            depthOfField.active = depthOfFieldWasActive;

        for (int i = 0; i < switchOff.Length; i++)
        {
            if (wasEnabled[i] && switchOff[i] != null)
                switchOff[i].enabled = true;
        }

        Cursor.lockState = previousLockState;
        Cursor.visible = previousCursorVisible;

        AudioListener.pause = false;
        Time.timeScale = previousTimeScale;
    }

    // hover = slightly bigger, click = back to the intro scene
    void UpdateButton()
    {
        Mouse mouse = Mouse.current;

        if (buttonRect == null || mouse == null)
            return;

        bool over = RectTransformUtility.RectangleContainsScreenPoint(
            buttonRect,
            mouse.position.ReadValue(),
            uiCamera
        );

        if (over != hovering)
        {
            hovering = over;

            endButton.transform.localScale = over
                ? buttonNormalScale * hoverScale
                : buttonNormalScale;
        }

        if (over && mouse.leftButton.wasPressedThisFrame)
            GoToIntro();
    }

    void GoToIntro()
    {
        if (!Application.CanStreamedLevelBeLoaded(introSceneName))
        {
            Debug.LogError("[EscMenu] Scene '" + introSceneName + "' is not in the Build Settings (File > Build Settings > Scenes In Build).", this);
            return;
        }

        paused = false;
        AudioListener.pause = false;
        Time.timeScale = previousTimeScale;

        SceneManager.LoadScene(introSceneName);
    }

    // finds the Depth Of Field override inside the Volume's profile (works for URP and HDRP)
    void FindDepthOfField()
    {
        if (postProcessVolume == null)
        {
            Debug.LogWarning("[EscMenu] No post process Volume dragged in, so no Depth Of Field.", this);
            return;
        }

        UnityEngine.Rendering.VolumeProfile profile = postProcessVolume.profile;

        if (profile != null)
        {
            foreach (UnityEngine.Rendering.VolumeComponent component in profile.components)
            {
                if (component != null && component.GetType().Name == "DepthOfField")
                {
                    depthOfField = component;
                    break;
                }
            }
        }

        if (depthOfField == null)
            Debug.LogWarning("[EscMenu] The Volume's profile has no Depth Of Field override. Add it with Add Override > Post-processing > Depth Of Field.", this);
    }

    void FindScriptsToSwitchOff()
    {
        if (disableWhilePaused != null && disableWhilePaused.Length > 0)
        {
            switchOff = disableWhilePaused;
        }
        else
        {
            MissionUI missionUI = FindFirstObjectByType<MissionUI>();

            switchOff = missionUI != null && missionUI.disableOnCompletion != null
                ? missionUI.disableOnCompletion
                : new Behaviour[0];
        }

        wasEnabled = new bool[switchOff.Length];
    }

    void OnDestroy()
    {
        if (paused)
        {
            AudioListener.pause = false;
            Time.timeScale = previousTimeScale;
        }
    }
}