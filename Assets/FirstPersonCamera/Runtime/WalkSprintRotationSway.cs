using UnityEngine;

/// <summary>
/// Adds a rotation sway (degrees on X / Y / Z) to any objects you drag in, while the player walks or sprints.
///   WALK   : each axis moves between Walk Min and Walk Max   (default 0 -> 0.5)
///   SPRINT : each axis moves between Sprint Min and Sprint Max (default 0 -> 1)
///   IDLE   : eases back to 0
/// Values are never negative (the Inspector clamps them to 0 or more), so the rotation only ever goes + from the start pose.
/// Put this script on the Player (or anything that moves), then drag the objects to rotate into "Targets".
/// No other scripts needed: walking / sprinting is detected from how fast "Speed Source" moves.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Player/Walk Sprint Rotation Sway")]
public class WalkSprintRotationSway : MonoBehaviour
{
    public enum Mode { Auto, ForceWalk, ForceSprint, ForceIdle }

    [Header("Objects to rotate (drag them in)")]
    [Tooltip("Use child objects (Head, arms/weapon holder, body mesh...). Don't use a Rigidbody root, and don't use an object another script also rotates.")]
    public Transform[] targets;

    [Header("Walk / sprint detection")]
    [Tooltip("The object whose movement is measured. Empty = the object this script is on.")]
    public Transform speedSource;
    [Tooltip("Horizontal speed (m/s) above which it counts as WALKING.")]
    [Min(0f)] public float walkStartSpeed = 0.5f;
    [Tooltip("Horizontal speed (m/s) above which it counts as SPRINTING. (Walk 4.5 / sprint 7.5 -> 6 works.)")]
    [Min(0f)] public float sprintStartSpeed = 6f;
    [Tooltip("Auto = detect from speed. Force modes are handy for testing the values in Play mode.")]
    public Mode mode = Mode.Auto;

    [Header("WALK rotation (degrees, only + values)")]
    public Vector3 walkMin = Vector3.zero;
    public Vector3 walkMax = new Vector3(0.5f, 0.5f, 0.5f);
    [Tooltip("Cycles per second.")]
    [Min(0f)] public float walkFrequency = 1.8f;

    [Header("SPRINT rotation (degrees, only + values)")]
    public Vector3 sprintMin = Vector3.zero;
    public Vector3 sprintMax = new Vector3(1f, 1f, 1f);
    [Tooltip("Cycles per second.")]
    [Min(0f)] public float sprintFrequency = 2.6f;

    [Header("Feel")]
    [Tooltip("Speed multiplier per axis so X, Y and Z don't all move in lock-step. Set all to 1 for identical timing.")]
    public Vector3 axisSpeedMultiplier = new Vector3(1f, 0.5f, 0.75f);
    [Tooltip("Start offset per axis, in cycles (0 - 1).")]
    public Vector3 axisPhaseOffset = new Vector3(0f, 0.25f, 0.5f);
    [Tooltip("How quickly it blends between idle / walk / sprint. Higher = snappier.")]
    [Min(0.1f)] public float blendSpeed = 8f;

    /// <summary>The rotation offset (degrees) currently being applied.</summary>
    public Vector3 CurrentOffset { get; private set; }

    Quaternion[] baseRot;
    Vector3 lastPos;
    float speed, phase, curFreq;
    Vector3 curMin, curMax;

    void OnEnable()
    {
        Capture();
        lastPos = (speedSource != null ? speedSource : transform).position;
    }

    void OnDisable()
    {
        if (targets == null || baseRot == null) return;
        for (int i = 0; i < targets.Length && i < baseRot.Length; i++)
            if (targets[i] != null) targets[i].localRotation = baseRot[i];
        CurrentOffset = Vector3.zero;
    }

    void OnValidate()
    {
        walkMin = Max0(walkMin);
        walkMax = Vector3.Max(Max0(walkMax), walkMin);
        sprintMin = Max0(sprintMin);
        sprintMax = Vector3.Max(Max0(sprintMax), sprintMin);
        axisSpeedMultiplier = Max0(axisSpeedMultiplier);
        if (sprintStartSpeed < walkStartSpeed) sprintStartSpeed = walkStartSpeed;
    }

    static Vector3 Max0(Vector3 v) { return new Vector3(Mathf.Max(0f, v.x), Mathf.Max(0f, v.y), Mathf.Max(0f, v.z)); }

    // remember each target's starting rotation so the sway is added on top of it
    void Capture()
    {
        int n = targets != null ? targets.Length : 0;
        baseRot = new Quaternion[n];
        for (int i = 0; i < n; i++)
            baseRot[i] = targets[i] != null ? targets[i].localRotation : Quaternion.identity;
    }

    float Wave(float axisMul, float axisPhase)
    {
        return 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * (phase * axisMul + axisPhase));   // always 0..1
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (targets == null) return;
        if (baseRot == null || baseRot.Length != targets.Length) Capture();

        // --- measure horizontal speed
        Transform src = speedSource != null ? speedSource : transform;
        Vector3 p = src.position;
        Vector3 d = p - lastPos;
        d.y = 0f;
        lastPos = p;
        speed = Mathf.Lerp(speed, d.magnitude / dt, 1f - Mathf.Exp(-12f * dt));

        // --- idle / walk / sprint
        bool sprint, walk;
        switch (mode)
        {
            case Mode.ForceSprint: sprint = true; walk = false; break;
            case Mode.ForceWalk: sprint = false; walk = true; break;
            case Mode.ForceIdle: sprint = false; walk = false; break;
            default:
                sprint = speed >= sprintStartSpeed;
                walk = !sprint && speed >= walkStartSpeed;
                break;
        }

        Vector3 tMin = Vector3.zero, tMax = Vector3.zero;
        float tFreq = curFreq;
        if (sprint) { tMin = sprintMin; tMax = sprintMax; tFreq = sprintFrequency; }
        else if (walk) { tMin = walkMin; tMax = walkMax; tFreq = walkFrequency; }

        float k = 1f - Mathf.Exp(-blendSpeed * dt);
        curMin = Vector3.Lerp(curMin, tMin, k);
        curMax = Vector3.Lerp(curMax, tMax, k);
        if (sprint || walk) curFreq = Mathf.Lerp(curFreq, tFreq, k);
        phase = Mathf.Repeat(phase + curFreq * dt, 1000f);

        // --- offset: each axis between its min and max, never below 0
        float wx = Wave(axisSpeedMultiplier.x, axisPhaseOffset.x);
        float wy = Wave(axisSpeedMultiplier.y, axisPhaseOffset.y);
        float wz = Wave(axisSpeedMultiplier.z, axisPhaseOffset.z);
        Vector3 offset = new Vector3(
            Mathf.Max(0f, Mathf.Lerp(curMin.x, curMax.x, wx)),
            Mathf.Max(0f, Mathf.Lerp(curMin.y, curMax.y, wy)),
            Mathf.Max(0f, Mathf.Lerp(curMin.z, curMax.z, wz)));
        CurrentOffset = offset;

        Quaternion q = Quaternion.Euler(offset);
        for (int i = 0; i < targets.Length; i++)
            if (targets[i] != null) targets[i].localRotation = baseRot[i] * q;
    }
}
