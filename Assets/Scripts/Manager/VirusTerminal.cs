
using UnityEngine;
using UnityEngine.InputSystem;

public class VirusTerminal : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [Tooltip("The camera you look through. Leave empty to use the Main Camera.")]
    [SerializeField] private UnityEngine.Camera playerCamera;

    [Header("Upload Settings")]
    [SerializeField] private float interactionRadius = 2f;
    [SerializeField] private float uploadRadius = 5f;
    [SerializeField] private float uploadDuration = 30f;
    [SerializeField] private Key interactionKey = Key.E;

    [Header("Runtime Status")]
    [SerializeField] private bool uploadStarted;
    [SerializeField] private bool uploadCompleted;
    [SerializeField] private bool playerInRange;
    [SerializeField] private float uploadProgress;

    [Header("Zone Visual Effect")]
    [SerializeField] private UploadZoneVisual uploadZone;

    public bool UploadStarted => uploadStarted;
    public bool UploadCompleted => uploadCompleted;
    public bool PlayerInRange => playerInRange;
    public float UploadProgress => uploadProgress;
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

    void Start()
    {
        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning("[Terminal] " + name +
                " has no Collider.", this);

        if (uploadZone != null)
        {
            uploadZone.SetRadius(uploadRadius);
            uploadZone.Hide();
        }
    }

    void Update()
    {
        if (player == null || MissionManager.Instance == null)
            return;

        // Horizontal distance, matching the floor circle.
        Vector3 center = transform.position;

        if (uploadZone != null)
        {
            center = uploadZone.zoneCenter != null
                ? uploadZone.zoneCenter.position
                : uploadZone.transform.position;
        }

        Vector3 offset = player.position - center;
        offset.y = 0f;

        playerInRange =
            offset.sqrMagnitude <= uploadRadius * uploadRadius;

        canInteract =
            MissionManager.Instance.State ==
                MissionManager.MissionState.ReachTerminal &&
            !uploadStarted &&
            LookingAtTerminal();

        if (canInteract &&
            Keyboard.current != null &&
            Keyboard.current[interactionKey].wasPressedThisFrame)
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

        UpdateZoneVisual();
    }

    void UpdateZoneVisual()
    {
        if (uploadZone == null)
            return;

        uploadZone.SetRadius(uploadRadius);

        if (!uploadStarted || uploadCompleted)
            return;

        if (IsUploading)
        {
            if (uploadZone.state !=
                UploadZoneVisual.ZoneState.Uploading)
            {
                uploadZone.ShowUploading();
            }
        }
        else if (IsPaused)
        {
            if (uploadZone.state !=
                UploadZoneVisual.ZoneState.Paused)
            {
                uploadZone.ShowPaused();
            }
        }
    }

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

        UpdateZoneVisual();

        Debug.Log("[Terminal] Virus upload initiated.");
    }

    void FinishUpload()
    {
        uploadProgress = 1f;
        uploadCompleted = true;

        if (uploadZone != null)
            uploadZone.ShowComplete();

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

        Vector3 center = transform.position;

        if (uploadZone != null)
        {
            center = uploadZone.zoneCenter != null
                ? uploadZone.zoneCenter.position
                : uploadZone.transform.position;
        }

        Gizmos.color = Color.cyan;

        const int segments = 64;

        Vector3 previous =
            center + Vector3.right * uploadRadius;

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;

            Vector3 next = center + new Vector3(
                Mathf.Cos(angle) * uploadRadius,
                0f,
                Mathf.Sin(angle) * uploadRadius
            );

            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
