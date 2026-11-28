using UnityEngine;

/// <summary>
/// The ending. ExtractionZone calls Begin() the moment the player touches it, and then:
///   - the player's controls are switched off
///   - the view switches to the outside camera
///   - the escape pod slides from "From Value" to "To Value" (fast at first, then slower and slower)
///   - the HUD parent you dragged in is switched off (MissionUI still shows the timer + return-to-title button)
///   - ALL sound fades to 0 over "Sound Fade Time" seconds
/// Time is NOT stopped, the game keeps running the whole time.
///
/// Put this on an empty GameObject in the scene (NOT inside the HUD parent).
/// </summary>
[DisallowMultipleComponent]
public class ExtractionSequence : MonoBehaviour
{
    public static ExtractionSequence Instance { get; private set; }

    public enum PodAxis { X, Y, Z }

    [Header("Outside camera")]
    [Tooltip("The camera that looks at the pod from outside. Keep it switched OFF in the scene. " +
             "A normal Camera works as is. A Cinemachine camera works too: give it a higher Priority than the player's camera.")]
    public GameObject outsideCamera;

    [Header("Escape pod")]
    [Tooltip("The escape pod object that moves.")]
    public Transform escapePod;
    [Tooltip("Which LOCAL position axis the pod moves on. Change this if it moves in the wrong direction.")]
    public PodAxis axis = PodAxis.Y;
    public float fromValue = -0.252f;
    public float toValue = -1.822f;
    [Tooltip("Seconds the whole move takes.")]
    [Min(0.1f)] public float moveDuration = 9f;
    [Tooltip("How strongly it slows down. Higher = faster start and a longer, softer crawl at the end.")]
    [Range(1f, 10f)] public float slowdown = 4f;

    [Header("HUD")]
    [Tooltip("The parent of all the HUD elements. It is switched off when the sequence starts. " +
             "The timer text and the return-to-title button must NOT be inside it.")]
    public GameObject hudParent;

    [Header("Sound")]
    [Tooltip("Seconds all sound takes to fade down to 0.")]
    [Min(0.01f)] public float soundFadeTime = 3f;

    bool started;
    float clock;
    float startVolume = 1f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        // the outside camera must not be live before the ending
        if (outsideCamera != null)
            outsideCamera.SetActive(false);
    }

    void Start()
    {
        WarnAboutSetup();
    }

    void OnDestroy()
    {
        if (Instance != this)
            return;

        Instance = null;

        // AudioListener.volume survives a scene load, so give the sound back (the title screen must not be silent)
        if (started)
            AudioListener.volume = startVolume;
    }

    /// <summary>Called by ExtractionZone. Does nothing unless the mission has just been completed.</summary>
    public void Begin()
    {
        if (started)
            return;

        MissionManager mission = MissionManager.Instance;

        if (mission == null || mission.State != MissionManager.MissionState.Completed)
            return;

        started = true;
        clock = 0f;
        startVolume = AudioListener.volume;

        DisablePlayerControls();
        SwitchCamera();

        if (hudParent != null)
            hudParent.SetActive(false);

        SetPodValue(fromValue);

        Debug.Log("[Extraction] Ending sequence started.", this);
    }

    void Update()
    {
        if (!started)
            return;

        clock += Time.deltaTime;

        // all sound: straight fade to 0
        float fade = Mathf.Clamp01(clock / Mathf.Max(0.01f, soundFadeTime));
        AudioListener.volume = Mathf.Lerp(startVolume, 0f, fade);

        // pod: exponential ease-out, scaled so it lands exactly on the end value at the end of the duration
        float t = Mathf.Clamp01(clock / Mathf.Max(0.1f, moveDuration));
        float eased = (1f - Mathf.Exp(-slowdown * t)) / (1f - Mathf.Exp(-slowdown));
        SetPodValue(Mathf.Lerp(fromValue, toValue, eased));

        // everything finished: stop updating (the volume stays at 0 until the scene changes)
        if (fade >= 1f && t >= 1f)
            enabled = false;
    }

    // ---------------------------------------------------------------

    void SetPodValue(float value)
    {
        if (escapePod == null)
            return;

        Vector3 p = escapePod.localPosition;

        switch (axis)
        {
            case PodAxis.X: p.x = value; break;
            case PodAxis.Y: p.y = value; break;
            default: p.z = value; break;
        }

        escapePod.localPosition = p;
    }

    // MissionUI switches off the scripts in its "Disable On Completion" list.
    // This is the safety net: the player's motor is always stopped, even if it is missing from that list.
    void DisablePlayerControls()
    {
        FPCam.FirstPersonMotor motor = FindFirstObjectByType<FPCam.FirstPersonMotor>();

        if (motor == null)
            return;

        motor.moveInput = Vector2.zero;
        motor.sprintHeld = false;
        motor.jumpRequested = false;
        motor.enabled = false;

        // a disabled motor no longer brakes the body, so stop the sideways movement here
        Rigidbody rb = motor.GetComponent<Rigidbody>();

        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(0f, v.y, 0f);
#else
            Vector3 v = rb.velocity;
            rb.velocity = new Vector3(0f, v.y, 0f);
#endif
        }
    }

    // "UnityEngine.Camera" in full on purpose: the project has its own FPCam scripts and a script called "Camera" would hide Unity's.
    void SwitchCamera()
    {
        if (outsideCamera == null)
        {
            Debug.LogWarning("[Extraction] No outside camera assigned, the view stays inside.", this);
            return;
        }

        UnityEngine.Camera playerCam = UnityEngine.Camera.main;     // read before anything changes
        UnityEngine.Camera outCam = outsideCamera.GetComponent<UnityEngine.Camera>();

        // A normal Camera: turn the player's camera off, this one on.
        if (outCam != null && playerCam != null && playerCam != outCam)
        {
            // only one AudioListener may be active
            AudioListener oldListener = playerCam.GetComponent<AudioListener>();
            AudioListener newListener = outCam.GetComponent<AudioListener>();

            if (oldListener != null && newListener != null)
                oldListener.enabled = false;

            // a "Screen Space - Camera" canvas would vanish with the old camera, so hand it over
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].renderMode == RenderMode.ScreenSpaceCamera && canvases[i].worldCamera == playerCam)
                    canvases[i].worldCamera = outCam;
            }

            playerCam.enabled = false;
        }

        outsideCamera.SetActive(true);

        if (outCam != null)
            outCam.enabled = true;

        // A Cinemachine camera has no Camera component: switching it on (with the higher Priority) is the whole switch.
    }

    void WarnAboutSetup()
    {
        if (outsideCamera == null)
            Debug.LogWarning("[Extraction] ExtractionSequence: Outside Camera is not assigned.", this);

        if (escapePod == null)
            Debug.LogWarning("[Extraction] ExtractionSequence: Escape Pod is not assigned.", this);

        if (hudParent == null)
            return;

        if (transform.IsChildOf(hudParent.transform))
            Debug.LogWarning("[Extraction] ExtractionSequence is inside the HUD parent. Move it to an empty object outside it.", this);

        MissionUI ui = FindFirstObjectByType<MissionUI>();

        if (ui == null)
            return;

        if (ui.transform.IsChildOf(hudParent.transform))
            Debug.LogWarning("[Extraction] The HUD parent contains MissionUI itself. Pick a different parent.", this);

        if (ui.timerText != null && ui.timerText.transform.IsChildOf(hudParent.transform))
            Debug.LogWarning("[Extraction] The timer text is inside the HUD parent and would be hidden. Move it out.", this);

        if (ui.returnToTitleButton != null && ui.returnToTitleButton.transform.IsChildOf(hudParent.transform))
            Debug.LogWarning("[Extraction] The return-to-title button is inside the HUD parent and would be hidden. Move it out.", this);
    }
}
