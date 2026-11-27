using UnityEngine;
using UnityEngine.InputSystem;

public class VirusTerminal : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [Tooltip("The camera you look through. Leave empty to use the Main Camera.")]
    [SerializeField] private UnityEngine.Camera playerCamera;

    [Header("Upload Settings")]
    [Tooltip("How close you must be to the terminal, while looking at it, to press E (measured from the camera).")]
    [SerializeField] private float interactionRadius = 2f;
    [SerializeField] private float uploadRadius = 5f;
    [SerializeField] private float uploadDuration = 30f;
    [SerializeField] private Key interactionKey = Key.E;

    [Header("Runtime Status")]
    [SerializeField] private bool uploadStarted;
    [SerializeField] private bool uploadCompleted;
    [SerializeField] private bool playerInRange;
    [SerializeField] private float uploadProgress;

    public bool UploadStarted => uploadStarted;
    public bool UploadCompleted => uploadCompleted;
    public bool PlayerInRange => playerInRange;
    public float UploadProgress => uploadProgress;

    // true while you look at the terminal and can press E (worked out once per frame in Update)
    public bool CanInteract => canInteract;

    private bool canInteract;
    private static readonly RaycastHit[] lookHits = new RaycastHit[32];

    public bool IsUploading =>
        uploadStarted &&
        !uploadCompleted &&
        playerInRange;

    public bool IsPaused =>
        uploadStarted &&
        !uploadCompleted &&
        !playerInRange;

    void Update()
    {
        if (player == null || MissionManager.Instance == null)
            return;

        float distance = Vector3.Distance(
            player.position,
            transform.position
        );

        playerInRange = distance <= uploadRadius;

        // only look for the terminal while it can be used, so there is no raycast the rest of the game
        canInteract =
            MissionManager.Instance.State ==
                MissionManager.MissionState.ReachTerminal &&
            !uploadStarted &&
            LookingAtTerminal();

        if (canInteract && Keyboard.current != null && Keyboard.current[interactionKey].wasPressedThisFrame)
        { 
            BeginUpload();
        }

        if (IsUploading &&
            MissionManager.Instance.State ==
                MissionManager.MissionState.Uploading)
        {
            uploadProgress += Time.deltaTime /
                Mathf.Max(uploadDuration, 0.01f);

            uploadProgress = Mathf.Clamp01(uploadProgress);

            if (uploadProgress >= 1f)
            {
                FinishUpload();
            }
        }
    }

    void Start()
    {
        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning("[Terminal] " + name + " has no Collider (also not on its children), so it can not be looked at.", this);
    }

    // true when the centre of the screen (the crosshair) points at this terminal, within interactionRadius
    bool LookingAtTerminal()
    {
        if (playerCamera == null)
            playerCamera = UnityEngine.Camera.main;

        if (playerCamera == null)
            return false;

        Ray ray = playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        int count = Physics.RaycastNonAlloc(
            ray, lookHits, interactionRadius,
            ~0, QueryTriggerInteraction.Collide
        );

        float nearest = float.MaxValue;
        bool nearestIsTerminal = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = lookHits[i];
            Transform t = hit.collider.transform;
            bool isTerminal = t.IsChildOf(transform);

            // other triggers and the player's own colliders never block the view
            if (!isTerminal &&
                (hit.collider.isTrigger || t.IsChildOf(player)))
                continue;

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                nearestIsTerminal = isTerminal;
            }
        }

        return nearestIsTerminal;
    }

    void BeginUpload()
    {
        uploadStarted = true;
        canInteract = false;

        MissionManager.Instance.StartUpload();

        Debug.Log("[Terminal] Virus upload initiated.");
    }

    void FinishUpload()
    {
        uploadProgress = 1f;
        uploadCompleted = true;

        MissionManager.Instance.CompleteUpload();

        Debug.Log("[Terminal] Upload complete.");
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            transform.position,
            interactionRadius
        );

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(
            transform.position,
            uploadRadius
        );
    }
}