using UnityEngine;

/// <summary>
/// Put this on the trigger collider behind the end door.
/// When the player walks in, the mission is completed: the timer stops and MissionUI shows it.
/// It only counts after the virus upload is done (MissionManager checks that).
/// </summary>
[RequireComponent(typeof(Collider))]
public class ExtractionZone : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player. Leave empty to use anything tagged Player.")]
    [SerializeField] private Transform player;

    // runs once when the script is added: makes the collider a trigger
    void Reset()
    {
        Collider zone = GetComponent<Collider>();

        if (zone != null)
            zone.isTrigger = true;
    }

    void Start()
    {
        Collider zone = GetComponent<Collider>();

        if (zone != null && !zone.isTrigger)
            Debug.LogWarning("[Extraction] The collider on " + name + " needs 'Is Trigger' ticked.", this);
    }

    void OnTriggerEnter(Collider other)
    {
        bool isPlayer = player != null
            ? other.transform.IsChildOf(player)
            : other.CompareTag("Player");

        if (!isPlayer)
            return;

        if (MissionManager.Instance == null)
            return;

        MissionManager.Instance.CompleteMission();

        Debug.Log("[Extraction] Player reached the last room.", this);
    }
}
