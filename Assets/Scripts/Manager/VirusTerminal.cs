
using UnityEngine;
using UnityEngine.InputSystem;

public class VirusTerminal : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;

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

    public bool UploadStarted => uploadStarted;
    public bool UploadCompleted => uploadCompleted;
    public bool PlayerInRange => playerInRange;
    public float UploadProgress => uploadProgress;

    public bool CanInteract =>
        player != null &&
        MissionManager.Instance != null &&
        MissionManager.Instance.State ==
            MissionManager.MissionState.ReachTerminal &&
        !uploadStarted &&
        Vector3.Distance(player.position, transform.position)
            <= interactionRadius;

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

        if (CanInteract && Keyboard.current != null && Keyboard.current[interactionKey].wasPressedThisFrame)
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

    void BeginUpload()
    {
        uploadStarted = true;

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
