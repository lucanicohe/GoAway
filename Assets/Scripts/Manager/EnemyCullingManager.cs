using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy Culling & LOD Manager using Unity's built-in CullingGroup API.
///
/// Features:
/// 1. Frustum Culling: Tracks whether each enemy is within the camera's view frustum.
///    When offscreen, heavy procedural visual calculations (skeletal bone IK, material glow updates, lights)
///    are suspended. Essential AI logic, aiming and shooting never depend on it, so enemies can still attack from behind.
/// 2. Distance LOD: Categorizes enemies into distance bands relative to the player:
///    - Band 0 (0-20 m):   Full simulation (every frame).
///    - Band 1 (20-50 m):  Mid-range (AI checks ~10 times/sec).
///    - Band 2 (50-100 m): Far range (AI checks ~2 times/sec, audio raycasts suspended).
///    - Band 3 (100+ m):   Very far (AI checks ~1 time/sec, non-essential processing suspended).
///
/// Slots: every enemy owns one bounding sphere slot. A removed enemy's slot is parked far away and reused by the next enemy,
/// so nothing is ever swapped inside the CullingGroup (swapping spheres leaves the group's cached visibility / band of the
/// removed enemy on the one that took its place).
/// </summary>
[DisallowMultipleComponent]
public class EnemyCullingManager : MonoBehaviour
{
    public interface ICullableTarget
    {
        Transform Transform { get; }
        bool IsDead { get; }
        void OnCullingStateChanged(bool isVisible, int distanceBand);
    }

    [Header("Distance Bands (metres)")]
    [Tooltip("Band 0 (Full simulation) is 0 to this distance.")]
    [SerializeField] private float closeDistance = 20f;
    [Tooltip("Band 1 (10 Hz simulation) is closeDistance to this distance.")]
    [SerializeField] private float midDistance = 50f;
    [Tooltip("Band 2 (2 Hz simulation) is midDistance to this distance. Band 3 (suspended) is beyond this.")]
    [SerializeField] private float farDistance = 100f;

    [Header("Bounding Sphere")]
    [SerializeField] private float sphereRadius = 1.5f;
    [Tooltip("The sphere sits this far above the enemy's pivot.")]
    [SerializeField] private float sphereHeight = 0.9f;

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = false;

    private CullingGroup cullingGroup;
    private BoundingSphere[] spheres = new BoundingSphere[32];
    private ICullableTarget[] targets = new ICullableTarget[32];
    private int slotCount;                                       // slots in use (free ones are parked far away)
    private readonly Stack<int> freeSlots = new Stack<int>();
    private readonly Dictionary<ICullableTarget, int> slotOf = new Dictionary<ICullableTarget, int>();
    private UnityEngine.Camera targetCamera;
    private Transform distanceReference;
    private static readonly Vector3 ParkedPosition = new Vector3(0f, -100000f, 0f);

    public int TrackedCount => slotOf.Count;

    private void Awake()
    {
        EnsureInitialized();
    }

    /// <summary>The distance bands are measured from this object (the player). Safe to call every frame.</summary>
    public void SetReferencePoint(Transform reference)
    {
        if (reference == null || reference == distanceReference) return;
        distanceReference = reference;
        if (cullingGroup != null)
            cullingGroup.SetDistanceReferencePoint(distanceReference);
    }

    public void SetTargetCamera(UnityEngine.Camera cam)
    {
        if (cam == null || cam == targetCamera) return;
        targetCamera = cam;
        if (cullingGroup != null)
            cullingGroup.targetCamera = targetCamera;
    }

    private void EnsureInitialized()
    {
        if (cullingGroup != null) return;

        cullingGroup = new CullingGroup();

        if (targetCamera == null)
            targetCamera = UnityEngine.Camera.main;
        if (targetCamera == null)
            targetCamera = FindFirstObjectByType<UnityEngine.Camera>();

        if (targetCamera != null)
            cullingGroup.targetCamera = targetCamera;

        if (distanceReference != null)
            cullingGroup.SetDistanceReferencePoint(distanceReference);
        else if (targetCamera != null)
            cullingGroup.SetDistanceReferencePoint(targetCamera.transform);   // until the player is known the camera is the centre

        // Distance bands: 0-20m (band 0), 20-50m (band 1), 50-100m (band 2), 100m+ (band 3)
        cullingGroup.SetBoundingDistances(new float[] { closeDistance, midDistance, farDistance });
        cullingGroup.SetBoundingSpheres(spheres);
        cullingGroup.SetBoundingSphereCount(slotCount);

        cullingGroup.onStateChanged = HandleStateChanged;
    }

    private void HandleStateChanged(CullingGroupEvent evt)
    {
        int index = evt.index;
        if (index < 0 || index >= slotCount) return;

        ICullableTarget target = targets[index];
        if (target == null || !IsAlive(target) || target.IsDead) return;

        target.OnCullingStateChanged(evt.isVisible, evt.currentDistance);

        if (logStateChanges)
        {
            Debug.Log($"[CullingGroup] Target #{index} visible={evt.isVisible}, band={evt.currentDistance}");
        }
    }

    public void RegisterEnemy(ICullableTarget enemy)
    {
        if (enemy == null || slotOf.ContainsKey(enemy)) return;

        EnsureInitialized();

        int slot;
        if (freeSlots.Count > 0)
        {
            slot = freeSlots.Pop();
        }
        else
        {
            if (slotCount == spheres.Length) Grow();
            slot = slotCount++;
            cullingGroup.SetBoundingSphereCount(slotCount);
        }

        targets[slot] = enemy;
        slotOf[enemy] = slot;
        spheres[slot] = new BoundingSphere(CenterOf(enemy), sphereRadius);

        // Until Unity has measured the new sphere (next frame) the enemy counts as visible and near = full detail.
        // (IsVisible / GetDistance of a sphere that was just added are not measured yet and would say "invisible".)
        enemy.OnCullingStateChanged(true, 0);
    }

    public void UnregisterEnemy(ICullableTarget enemy)
    {
        if (enemy == null) return;
        int slot;
        if (!slotOf.TryGetValue(enemy, out slot)) return;
        slotOf.Remove(enemy);
        FreeSlot(slot);
    }

    public void ClearAll()
    {
        for (int i = 0; i < slotCount; i++)
        {
            if (targets[i] != null) FreeSlot(i);
        }
        slotOf.Clear();
    }

    private void LateUpdate()
    {
        if (cullingGroup == null || slotOf.Count == 0) return;

        // Auto-refresh camera or reference point if they weren't ready at Awake
        if (targetCamera == null)
        {
            targetCamera = UnityEngine.Camera.main;
            if (targetCamera != null) cullingGroup.targetCamera = targetCamera;
        }

        if (distanceReference == null)
        {
            GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null)
            {
                distanceReference = playerGo.transform;
                cullingGroup.SetDistanceReferencePoint(distanceReference);
            }
            else if (targetCamera != null)
            {
                // no player yet: measure from the camera (and do not search for the player every frame; the GameManager sets it)
                distanceReference = targetCamera.transform;
                cullingGroup.SetDistanceReferencePoint(distanceReference);
            }
        }

        // Synchronize bounding spheres with moving enemy positions
        for (int i = 0; i < slotCount; i++)
        {
            ICullableTarget t = targets[i];
            if (t == null) continue;
            if (!IsAlive(t))
            {
                slotOf.Remove(t);
                FreeSlot(i);
                continue;
            }
            spheres[i].position = CenterOf(t);
        }
    }

    private void OnDestroy()
    {
        if (cullingGroup != null)
        {
            cullingGroup.onStateChanged = null;
            cullingGroup.Dispose();
            cullingGroup = null;
        }
        slotOf.Clear();
        freeSlots.Clear();
    }

    // ------------------------------------------------------------------ helpers

    private void Grow()
    {
        int size = spheres.Length * 2;
        Array.Resize(ref spheres, size);
        Array.Resize(ref targets, size);
        cullingGroup.SetBoundingSpheres(spheres);   // the group must be given the bigger array
    }

    private void FreeSlot(int slot)
    {
        targets[slot] = null;
        spheres[slot] = new BoundingSphere(ParkedPosition, 0.01f);
        freeSlots.Push(slot);
    }

    private Vector3 CenterOf(ICullableTarget t)
    {
        Transform tr = t.Transform;
        return tr != null ? tr.position + Vector3.up * sphereHeight : Vector3.zero;
    }

    // a destroyed enemy is "null" for Unity but not for a variable of the interface type
    private static bool IsAlive(ICullableTarget t)
    {
        UnityEngine.Object o = t as UnityEngine.Object;
        return o != null;
    }
}