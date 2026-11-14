using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// IG-88 droid controller. Put this on the droid's root object (the prefab already has it).
/// - "Walking" checkbox      : plays a procedural walk cycle (legs, arms, hips, torso).
/// - "Aim At Target" checkbox: waist/chest/head turn toward "Target" and the gun arm points the gun at it.
/// Works with the bones that come inside IG88.fbx. No Animator or animation clip is needed.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("IG88/IG88 Controller")]
public class IG88Controller : MonoBehaviour
{
    [Header("TICK THESE")]
    [Tooltip("Tick to play the walking animation.")]
    public bool walking = false;

    [Tooltip("Tick to make the upper body, head and gun arm aim at the Target. Untick to return to the rest pose.")]
    public bool aimAtTarget = true;

    [Tooltip("The object the droid aims at. Move this object around (in the Scene view) and the droid follows it.")]
    public Transform target;

    [Header("Walk settings")]
    [Range(0.2f, 3f)] public float stepsPerSecond = 1.1f;
    [Range(0f, 50f)] public float thighSwing = 26f;
    [Range(0f, 80f)] public float kneeBend = 38f;
    [Range(0f, 40f)] public float armSwing = 14f;
    [Range(0f, 0.1f)] public float bodyBob = 0.025f;
    [Range(0f, 15f)] public float torsoTwist = 4f;
    [Tooltip("Also move the droid forward while walking (off = walks in place).")]
    public bool moveWhileWalking = false;
    public float moveSpeed = 0.9f;

    [Header("Aim settings")]
    [Tooltip("Max degrees the waist+chest may turn away from the front.")]
    [Range(0f, 120f)] public float maxBodyTurn = 85f;
    [Tooltip("How much of the body turn is done by the waist (the rest is done by the chest).")]
    [Range(0f, 1f)] public float waistShare = 0.4f;
    [Range(0f, 1f)] public float headFollow = 0.7f;
    [Range(0f, 120f)] public float maxHeadTurn = 60f;
    [Tooltip("Max degrees the gun arm may rotate away from its rest pose.")]
    [Range(0f, 180f)] public float maxArmTurn = 110f;
    [Range(0.02f, 1f)] public float aimSmoothTime = 0.15f;
    public bool drawGizmos = true;
    
    [Header("Target")]
    [SerializeField] private LayerMask playerLayer;

    [Header("AgentSettings")]
    [SerializeField] private float stopDistance = 2f;
    [SerializeField] private float detectionDistance = 20f;
    [SerializeField] private float patrolDistance = 10f;
    [SerializeField] private float detectionInterval = 2f;
    [SerializeField] private Transform detectionCenter;
    
    private NavMeshAgent agent;

    private Transform detectedPlayer;
    private Vector3 patrolOrigin;
    private NavMeshPath patrolPath;
    private float agentTimer;
    
    class RigBone
    {
        public Transform t;
        public Vector3 restPos;
        public Quaternion restRot;
        public Vector3 swingAxis; // bone-local axis: positive angle swings the front of a limb up
        public Vector3 upAxis;    // bone-local up axis (for twisting)
    }

    readonly List<RigBone> all = new List<RigBone>();
    RigBone pelvis, waist, chest, head, armL, armR, thighL, thighR, shinL, shinR, footL, footR, gun, gunTip;
    Vector3 restForwardLocal = Vector3.forward;
    Vector3 gunFwdLocal = Vector3.forward;
    Vector3 headFwdLocal = Vector3.forward;
    bool rigReady;
    float phase, walkWeight, aimWeight, yaw, yawVel;

    /// <summary>Direction the droid faces (world space, flattened).</summary>
    public Vector3 RestForward
    {
        get { return Vector3.ProjectOnPlane(transform.TransformDirection(restForwardLocal), transform.up).normalized; }
    }

    void Awake()
    {
        DetectRig();
        agent = GetComponent<NavMeshAgent>();
        patrolOrigin = transform.position;
        patrolPath = new NavMeshPath();
    }
    
    private bool CanNavigate => agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;

    private void OnDisable()
    {
        if (CanNavigate)
            agent.isStopped = true;
    }

    private void Update()
    {
        if (!CanNavigate)
        {
            detectedPlayer = null;
            return;
        }

        bool canMove = walking && moveWhileWalking;
        bool resumeMovement = canMove && agent.isStopped;
        if (!canMove && !agent.isStopped)
            agent.isStopped = true;

        agentTimer -= Time.deltaTime;
        if (agentTimer > 0f && !resumeMovement)
            return;
        agentTimer = detectionInterval;

        bool wasChasing = !ReferenceEquals(detectedPlayer, null);
        detectedPlayer = FindPlayer();
        if (!canMove)
        {
            return;
        }
        agent.speed = moveSpeed;
        agent.isStopped = false;
        if (detectedPlayer != null)
        {
            agent.stoppingDistance = stopDistance;
            if (!agent.SetDestination(detectedPlayer.position))
                agent.ResetPath();
            return;
        }
        agent.stoppingDistance = 0.1f;
        if (wasChasing || resumeMovement)
        {
            agent.ResetPath();
        }
        if (agent.pathPending || 
            (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete
                           && agent.remainingDistance > agent.stoppingDistance + 0.05f))
            return;

        TryStartPatrol();
    }
    
    private Transform FindPlayer()
    {
        Transform nearestPlayer = null;
        float nearestDistance = float.PositiveInfinity;
        Vector3 origin = transform.position;
        foreach (Collider collider in Physics.OverlapSphere(origin, detectionDistance, playerLayer))
        {
            Transform candidate = collider.transform;
            if (candidate.IsChildOf(transform))
                continue;

            float distance = (candidate.position - origin).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPlayer = candidate;
            }
        }
        return nearestPlayer;
    }

    private void TryStartPatrol()
    {
        var center = detectionCenter != null ? detectionCenter.position : patrolOrigin;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var offset = UnityEngine.Random.insideUnitCircle * patrolDistance;
            var candidate = center + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(candidate, out var hit, 2f, filter)
                && agent.CalculatePath(hit.position, patrolPath)
                && patrolPath.status == NavMeshPathStatus.PathComplete
                && agent.SetPath(patrolPath))
                return;
        }
    }

    RigBone Grab(Dictionary<string, Transform> map, string key, List<string> missing)
    {
        Transform t;
        if (!map.TryGetValue(key, out t)) { missing.Add(key); return null; }
        RigBone b = new RigBone();
        b.t = t; b.restPos = t.localPosition; b.restRot = t.localRotation;
        all.Add(b);
        return b;
    }

    /// <summary>Finds the bones and records the rest pose. Safe to call more than once.</summary>
    public bool DetectRig()
    {
        if (rigReady) return true;
        all.Clear();
        Dictionary<string, Transform> map = new Dictionary<string, Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string k = t.name.ToLowerInvariant().Replace('.', '_');
            if (!map.ContainsKey(k)) map[k] = t;
        }
        List<string> missing = new List<string>();
        pelvis = Grab(map, "pelvis", missing); waist = Grab(map, "waist", missing); chest = Grab(map, "chest", missing);
        head = Grab(map, "head", missing);
        armL = Grab(map, "upper_arm_l", missing); armR = Grab(map, "upper_arm_r", missing);
        thighL = Grab(map, "thigh_l", missing); thighR = Grab(map, "thigh_r", missing);
        shinL = Grab(map, "shin_l", missing); shinR = Grab(map, "shin_r", missing);
        footL = Grab(map, "foot_l", missing); footR = Grab(map, "foot_r", missing);
        gun = Grab(map, "gun", missing); gunTip = Grab(map, "gun_tip", missing);
        if (missing.Count > 0)
        {
            Debug.LogError("IG88Controller: missing bones: " + string.Join(", ", missing.ToArray()) + ". Use the IG88 prefab / IG88.fbx that came with this package.", this);
            return false;
        }

        Vector3 up = transform.up;
        Vector3 f = Vector3.ProjectOnPlane(gunTip.t.position - gun.t.position, up);
        if (f.sqrMagnitude < 1e-6f) f = transform.forward;
        f.Normalize();
        restForwardLocal = transform.InverseTransformDirection(f);

        // Hinge axis for walking: choose the sign so a positive angle lifts the front of a limb.
        Vector3 lateral = Vector3.Cross(up, f).normalized;
        Vector3 test = Quaternion.AngleAxis(10f, lateral) * f;
        if (Vector3.Dot(test, up) < 0f) lateral = -lateral;

        foreach (RigBone b in all)
        {
            Quaternion inv = Quaternion.Inverse(b.t.rotation);
            b.swingAxis = inv * lateral;
            b.upAxis = inv * up;
        }
        gunFwdLocal = gun.t.InverseTransformDirection((gunTip.t.position - gun.t.position).normalized);
        headFwdLocal = head.t.InverseTransformDirection(f);
        rigReady = true;
        return true;
    }

    void Swing(RigBone b, float deg) { b.t.localRotation = b.t.localRotation * Quaternion.AngleAxis(deg, b.swingAxis); }
    void Twist(RigBone b, float deg) { b.t.localRotation = b.t.localRotation * Quaternion.AngleAxis(deg, b.upAxis); }

    void ResetPose()
    {
        for (int i = 0; i < all.Count; i++)
        {
            all[i].t.localPosition = all[i].restPos;
            all[i].t.localRotation = all[i].restRot;
        }
    }

    void LateUpdate()
    {
        if (!rigReady && !DetectRig()) return;
        float dt = Time.deltaTime;

        ResetPose();

        walkWeight = Mathf.MoveTowards(walkWeight, walking ? 1f : 0f, dt / 0.3f);
        if (walkWeight > 0f)
        {
            phase = Mathf.Repeat(phase + stepsPerSecond * 2f * Mathf.PI * dt, 2f * Mathf.PI);
            ApplyWalk(walkWeight);
        }

        float aimGoal = (aimAtTarget && target != null) ? 1f : 0f;
        aimWeight = Mathf.MoveTowards(aimWeight, aimGoal, dt / 0.35f);
        if (aimWeight > 0f && target != null) ApplyAim(aimWeight);
        else { yaw = 0f; yawVel = 0f; }
    }

    void Leg(RigBone thigh, RigBone shin, RigBone foot, float s, float c, float w)
    {
        float t = thighSwing * s * w;                 // + = leg swings forward
        float knee = kneeBend * Mathf.Max(0f, c) * w; // knee bends while the leg moves forward
        Swing(thigh, t);
        Swing(shin, -knee);
        Swing(foot, (knee - t) * 0.8f);               // keep the foot roughly level
    }

    void ApplyWalk(float w)
    {
        float sL = Mathf.Sin(phase), cL = Mathf.Cos(phase);
        float sR = Mathf.Sin(phase + Mathf.PI), cR = Mathf.Cos(phase + Mathf.PI);
        Leg(thighL, shinL, footL, sL, cL, w);
        Leg(thighR, shinR, footR, sR, cR, w);
        Swing(armL, -armSwing * sL * w);
        Swing(armR, armSwing * 0.25f * sL * w);       // gun arm only sways a little
        Twist(waist, torsoTwist * sL * w);
        float bob = bodyBob * Mathf.Cos(2f * phase) * w;
        Transform p = pelvis.t.parent;
        Vector3 d = transform.up * bob;
        pelvis.t.localPosition = pelvis.restPos + (p != null ? p.InverseTransformVector(d) : d);
    }

    void ApplyAim(float w)
    {
        Vector3 up = transform.up;
        Vector3 fwd = Vector3.ProjectOnPlane(transform.TransformDirection(restForwardLocal), up);
        Vector3 tp = target.position;

        // 1) upper body turns (waist + chest) around the vertical axis
        Vector3 flat = Vector3.ProjectOnPlane(tp - chest.t.position, up);
        float desired = 0f;
        if (flat.sqrMagnitude > 0.0004f)
            desired = Mathf.Clamp(Vector3.SignedAngle(fwd, flat, up), -maxBodyTurn, maxBodyTurn);
        yaw = Mathf.SmoothDampAngle(yaw, desired, ref yawVel, aimSmoothTime);
        float a = yaw * w;
        waist.t.rotation = Quaternion.AngleAxis(a * waistShare, up) * waist.t.rotation;
        chest.t.rotation = Quaternion.AngleAxis(a * (1f - waistShare), up) * chest.t.rotation;

        // 2) head looks at the target
        Vector3 hf = head.t.TransformDirection(headFwdLocal);
        Vector3 hd = tp - head.t.position;
        if (hd.sqrMagnitude > 1e-4f)
        {
            Quaternion full = Quaternion.FromToRotation(hf, hd.normalized);
            full = Quaternion.RotateTowards(Quaternion.identity, full, maxHeadTurn);
            head.t.rotation = Quaternion.Slerp(Quaternion.identity, full, headFollow * w) * head.t.rotation;
        }

        // 3) gun arm: rotate the whole arm at the shoulder until the barrel points at the target
        Transform sh = armR.t;
        Quaternion before = sh.rotation;
        for (int i = 0; i < 4; i++)
        {
            Vector3 gd = gun.t.TransformDirection(gunFwdLocal);
            Vector3 want = tp - gunTip.t.position;
            if (want.sqrMagnitude < 0.04f) break;
            sh.rotation = Quaternion.FromToRotation(gd, want.normalized) * sh.rotation;
        }
        Quaternion aimed = sh.rotation;
        if (Quaternion.Angle(before, aimed) > maxArmTurn)
            aimed = Quaternion.RotateTowards(before, aimed, maxArmTurn);
        sh.rotation = Quaternion.Slerp(before, aimed, w);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !rigReady || target == null || gunTip == null) return;
        Gizmos.color = aimAtTarget ? Color.red : Color.gray;
        Gizmos.DrawLine(gunTip.t.position, target.position);
    }

    // Handy for your own scripts / UI buttons:
    public void SetWalking(bool value) { walking = value; }
    public void SetAiming(bool value) { aimAtTarget = value; }
}
