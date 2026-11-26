using UnityEngine;

public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    public enum MissionState
    {
        WaitingToStart,
        ReachTerminal,
        Uploading,
        ReachExtraction,
        Completed
    }

    [Header("Mission Status")]
    [SerializeField] private MissionState state = MissionState.WaitingToStart;
    [SerializeField] private float elapsedTime;

    public MissionState State => state;
    public float ElapsedTime => elapsedTime;

    public bool UploadCompleted =>
        state == MissionState.ReachExtraction ||
        state == MissionState.Completed;

    public bool MissionActive =>
        state != MissionState.WaitingToStart &&
        state != MissionState.Completed;

    /// <summary>
    /// True from the moment the virus upload is complete (and for the rest of the mission).
    /// While it is true, every IG-88 droid and every Flathead malfunctions: they no longer hunt the player, but shoot wildly (stray bolts still hurt).
    /// Safe to read from anywhere: false when there is no MissionManager in the scene.
    /// </summary>
    public static bool EnemiesMalfunctioning =>
        Instance != null && Instance.UploadCompleted;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        state = MissionState.WaitingToStart;
        elapsedTime = 0f;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (MissionActive)
        {
            elapsedTime += Time.deltaTime;
        }
    }

    public void StartMission()
    {
        if (state != MissionState.WaitingToStart)
            return;

        elapsedTime = 0f;
        state = MissionState.ReachTerminal;

        Debug.Log("[Mission] Mission started.");
    }

    public void StartUpload()
    {
        if (state != MissionState.ReachTerminal)
            return;

        state = MissionState.Uploading;

        Debug.Log("[Mission] Virus upload started.");
    }

    public void CompleteUpload()
    {
        if (state != MissionState.Uploading)
            return;

        state = MissionState.ReachExtraction;

        Debug.Log("[Mission] Upload complete. Exit unlocked.");
    }

    public void CompleteMission()
    {
        if (state != MissionState.ReachExtraction)
            return;

        state = MissionState.Completed;

        Debug.Log("[Mission] Completed in " + GetFormattedTime());
    }

    public string GetFormattedTime()
    {
        int minutes = Mathf.FloorToInt(elapsedTime / 60f);
        int seconds = Mathf.FloorToInt(elapsedTime % 60f);
        int milliseconds = Mathf.FloorToInt(
            (elapsedTime % 1f) * 100f
        );

        return $"{minutes:00}:{seconds:00}.{milliseconds:00}";
    }
}