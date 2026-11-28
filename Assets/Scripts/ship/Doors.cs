using System.Collections;
using UnityEngine;

public class Doors : MonoBehaviour, IBlasterDamageable
{
    public enum DoorType
    {
        Normal,
        MissionStart,
        Extraction
    }

    [Header("Mission Settings")]
    public DoorType doorType = DoorType.Normal;

    [Header("Door References")]
    public Transform leftDoor;
    public Transform rightDoor;
    public float duration = 1f;

    [Header("Door Sounds (drag in AudioSources that already have their clip)")]
    [Tooltip("Plays once when the left door starts opening. Put the AudioSource on the left door so the sound comes from the door itself. 'Play On Awake' should be off.")]
    public AudioSource leftDoorSound;
    [Tooltip("Plays once when the right door starts opening. Put the AudioSource on the right door so the sound comes from the door itself. 'Play On Awake' should be off.")]
    public AudioSource rightDoorSound;

    [Header("Local Y positions")]
    public float leftClosedY = 0.229f;
    public float leftOpenY = 0.24009f;
    public float rightClosedY = 0.21098f;
    public float rightOpenY = 0.19974f;

    bool opened;

    void Awake()
    {
        opened = false;

        if (leftDoor == null || rightDoor == null)
            Debug.LogWarning("[Doors] Missing door references on " + name, this);
        else
            Debug.Log("[Doors] Ready on " + name, this);

        if (leftDoor != null)
        {
            Vector3 p = leftDoor.localPosition;
            p.y = leftClosedY;
            leftDoor.localPosition = p;
        }

        if (rightDoor != null)
        {
            Vector3 p = rightDoor.localPosition;
            p.y = rightClosedY;
            rightDoor.localPosition = p;
        }
    }

    public void TakeBlasterDamage(BlasterHitInfo hit)
    {
        if (opened) return;

        Debug.Log("[Doors] " + name + " was shot.", this);

        Open();
    }

    [ContextMenu("Test: Open now")]
    void TestOpen()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Doors] Press Play first.", this);
            return;
        }

        Open();
    }

    void Open()
    {
        if (opened) return;

        if (leftDoor == null || rightDoor == null)
        {
            Debug.LogWarning("[Doors] Missing door references.", this);
            return;
        }

        // Mission-related doors require a MissionManager.
        if (doorType != DoorType.Normal &&
            MissionManager.Instance == null)
        {
            Debug.LogWarning("[Doors] MissionManager not found.", this);
            return;
        }

        // Prevent extraction before the upload is complete.
        if (doorType == DoorType.Extraction)
        {
            if (!MissionManager.Instance.UploadCompleted)
            {
                Debug.Log("[Doors] Exit locked. Complete virus upload first.", this);
                return;
            }
        }

        opened = true;

        // Start the timer as the first door begins opening.
        if (doorType == DoorType.MissionStart)
        {
            MissionManager.Instance.StartMission();
        }

        PlayDoorSounds();

        Report(leftDoor);
        Report(rightDoor);

        StartCoroutine(Move());
    }

    // Each of the two doors plays its own sound once.
    // If both slots hold the same AudioSource it still only plays once.
    void PlayDoorSounds()
    {
        PlayOnce(leftDoorSound);

        if (rightDoorSound != leftDoorSound)
            PlayOnce(rightDoorSound);
    }

    static void PlayOnce(AudioSource source)
    {
        if (source == null || source.clip == null)
            return;

        source.loop = false;
        source.Play();
    }

    void Report(Transform leaf)
    {
        Animator anim = leaf.GetComponentInParent<Animator>();
        Renderer r = leaf.GetComponentInChildren<Renderer>();

        Debug.Log("[Doors] " + leaf.name
            + ": renderer=" + (r != null ? r.name : "NONE")
            + ", partOfStaticBatch=" + (r != null && r.isPartOfStaticBatch)
            + ", animator=" + (anim != null ? anim.gameObject.name : "none")
            + ", localPos=" + leaf.localPosition.ToString("F3"), leaf);
    }

    IEnumerator Move()
    {
        Vector3 leftStart = leftDoor.localPosition;
        Vector3 rightStart = rightDoor.localPosition;

        Vector3 leftEnd = leftStart;
        leftEnd.y = leftOpenY;

        Vector3 rightEnd = rightStart;
        rightEnd.y = rightOpenY;

        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(duration, 0.001f);

            leftDoor.localPosition =
                Vector3.Lerp(leftStart, leftEnd, t);

            rightDoor.localPosition =
                Vector3.Lerp(rightStart, rightEnd, t);

            yield return null;
        }

        yield return null;

        Debug.Log("[Doors] after opening: left local y = "
            + leftDoor.localPosition.y.ToString("F4")
            + " (wanted " + leftOpenY + "), right local y = "
            + rightDoor.localPosition.y.ToString("F4")
            + " (wanted " + rightOpenY + ")", this);
    }
}