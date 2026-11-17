using System.Collections.Generic;
using UnityEngine;

public enum IG88Route { Green, Red }
public enum IG88EdgeKind { Green, Red, Yellow }
public struct IG88Link { public int node; public IG88EdgeKind kind; }

/// <summary>
/// IG-88 droid controller. Put this on the droid's root object (the prefab already has it).
/// The GameManager spawns the droid and calls Init(). From then on the droid:
///  - PATROLS its route (green or red) by walking from waypoint to waypoint along the drawn connections only.
///  - CHASES the object tagged "Player" when closer than the GameManager chase radius
///    (uses ALL connections incl. yellow, runs, aims at the player, turns the chase light on).
///  - Goes back to its patrol when the player is farther away than the chase radius.
///  - Never touches another droid: it waits until the other droid moves away.
/// Procedural walk animation, no Animator needed.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("IG88/IG88 Controller")]
public class IG88Controller : MonoBehaviour
{
    [Header("Driven automatically (you don't need to touch these)")]
    public bool walking = false;
    public bool aimAtTarget = false;
    public Transform target;

    [Header("Health (set by GameManager when the droid spawns)")]
    public float health = 1f;

    [Header("Death: break apart")]
    [Tooltip("How hard the pieces are thrown away when the droid dies.")]
    public float scatterForce = 3f;
    [Tooltip("How fast the pieces tumble.")]
    public float spinForce = 4f;
    [Tooltip("Size of the collider of pieces that have no child bone (head, feet, gun), in metres.")]
    [Range(0.05f, 0.4f)] public float leafPieceSize = 0.12f;
    [Tooltip("Seconds until the pieces disappear. 0 = they stay on the ground forever.")]
    public float debrisLifetime = 0f;

    [Header("Chase light (drag the Point Light inside the droid here)")]
    public Light chaseLight;
    [Tooltip("Seconds the light needs to fade between 0 and the GameManager intensity.")]
    public float lightFadeTime = 0.2f;

    [Header("Walk pose")]
    [Range(0f, 50f)] public float thighSwing = 26f;
    [Range(0f, 80f)] public float kneeBend = 38f;
    [Range(0f, 40f)] public float armSwing = 14f;
    [Range(0f, 0.1f)] public float bodyBob = 0.025f;
    [Range(0f, 15f)] public float torsoTwist = 4f;
    [Tooltip("Walk pose is multiplied by this while chasing (bigger strides when running).")]
    [Range(1f, 2.5f)] public float runPoseMultiplier = 1.35f;

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

    // ------------------------------------------------------------------ rig

    class RigBone
    {
        public Transform t;
        public Vector3 restPos;
        public Quaternion restRot;
        public Vector3 swingAxis;
        public Vector3 upAxis;
    }

    readonly List<RigBone> all = new List<RigBone>();
    RigBone pelvis, waist, chest, head, armL, armR, thighL, thighR, shinL, shinR, footL, footR, gun, gunTip;
    Vector3 restForwardLocal = Vector3.forward;
    Vector3 gunFwdLocal = Vector3.forward;
    Vector3 headFwdLocal = Vector3.forward;
    bool rigReady;
    float phase, walkWeight, aimWeight, yaw, yawVel;
    float stepsPerSecond = 1.1f;
    float poseScale = 1f;

    // ------------------------------------------------------------------ AI state

    enum Mode { Patrol, Chase }

    GameManager gm;
    IG88Route route;
    Mode mode = Mode.Patrol;
    bool ready;

    int fromNode = -1;      // node we are on / just left
    int toNode = -1;        // node we are walking to (-1 = standing on fromNode, need a new target)
    int prevNode = -1;      // node we came from (patrol: don't turn back unless dead end)
    int goalNode = -1;      // chase goal or "return to patrol" goal
    bool returning;         // walking back to the patrol network after a chase

    float blockedTime;
    float blockLimit = 3f;
    float backoffTimer;

    const float ReverseMargin = 0.05f;
    const float GoalHysteresis = 0.75f;
    static readonly List<int> tmp = new List<int>();

    /// <summary>Direction the droid faces (world space, flattened).</summary>
    public Vector3 RestForward
    {
        get { return Vector3.ProjectOnPlane(transform.TransformDirection(restForwardLocal), transform.up).normalized; }
    }

    void Awake() { DetectRig(); EnsureHitCollider(); }

    void OnDestroy()
    {
        if (gm != null) gm.Unregister(this);
    }

    /// <summary>Called by the GameManager right after spawning.</summary>
    public void Init(GameManager manager, IG88Route patrolRoute, int startNode)
    {
        gm = manager;
        route = patrolRoute;
        mode = Mode.Patrol;
        returning = false;
        fromNode = startNode;
        toNode = -1;
        prevNode = -1;
        goalNode = -1;
        blockedTime = 0f;
        blockLimit = Random.Range(2f, 4f);
        backoffTimer = 0f;
        aimAtTarget = false;
        walking = false;
        IsDead = false;
        health = gm.DroidHealth;
        transform.position = gm.NodePos(startNode);
        if (chaseLight != null) { chaseLight.enabled = true; chaseLight.intensity = 0f; }
        ready = true;
    }

    // ------------------------------------------------------------------ rig detection (unchanged logic)

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

    // ------------------------------------------------------------------ health / death

    public bool IsDead { get; private set; }

    /// <summary>Damage from any source. When health reaches 0 the droid breaks apart.</summary>
    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (IsDead || !ready) return;
        health -= amount;
        if (health <= 0f) Die(hitPoint, hitDirection);
    }

    void Die(Vector3 hitPoint, Vector3 hitDirection)
    {
        IsDead = true;
        ready = false;
        walking = false;
        aimAtTarget = false;
        if (chaseLight != null) { chaseLight.intensity = 0f; chaseLight.enabled = false; }
        if (gm != null) gm.Unregister(this); // a dead droid no longer blocks the living ones
        BreakApart(hitPoint, hitDirection);
        enabled = false; // stops Update/LateUpdate, otherwise the pose reset would snap the bones back
    }

    // If the prefab has no solid collider, bolts would fly through the droid. Make one from the renderer bounds.
    void EnsureHitCollider()
    {
        Collider[] existing = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < existing.Length; i++)
            if (existing[i].enabled && !existing[i].isTrigger) return;

        Renderer[] rs = GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

        Vector3 s = transform.lossyScale;
        float sx = Mathf.Max(Mathf.Abs(s.x), 0.0001f), sy = Mathf.Max(Mathf.Abs(s.y), 0.0001f), sz = Mathf.Max(Mathf.Abs(s.z), 0.0001f);
        CapsuleCollider cap = gameObject.AddComponent<CapsuleCollider>();
        cap.direction = 1;
        cap.center = transform.InverseTransformPoint(b.center);
        cap.height = b.size.y / sy;
        cap.radius = Mathf.Clamp((b.size.x / sx + b.size.z / sz) * 0.25f, 0.05f, cap.height * 0.5f);

        // a moving collider should have a kinematic rigidbody
        if (GetComponent<Rigidbody>() == null)
            gameObject.AddComponent<Rigidbody>().isKinematic = true;
    }

    void AddPiece(List<Transform> list, Transform t)
    {
        if (t == null || t == transform || !t.IsChildOf(transform)) return;
        if (!list.Contains(t)) list.Add(t);
    }

    // box that covers the bone and the points of its child bones, in the bone's own space (works for any bone orientation)
    BoxCollider AddPieceCollider(Transform p)
    {
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        float longest = 0f;
        for (int i = 0; i < p.childCount; i++)
        {
            Transform c = p.GetChild(i);
            b.Encapsulate(p.InverseTransformPoint(c.position));
            longest = Mathf.Max(longest, Vector3.Distance(p.position, c.position));
        }
        float thick = longest > 0.001f ? Mathf.Clamp(longest * 0.35f, 0.04f, 0.18f) : leafPieceSize;
        Vector3 s = p.lossyScale;
        Vector3 pad = new Vector3(thick / Mathf.Max(Mathf.Abs(s.x), 0.0001f),
                                  thick / Mathf.Max(Mathf.Abs(s.y), 0.0001f),
                                  thick / Mathf.Max(Mathf.Abs(s.z), 0.0001f));
        BoxCollider box = p.gameObject.AddComponent<BoxCollider>();
        box.center = b.center;
        box.size = b.size + pad;
        return box;
    }

    /// <summary>Every rigged part (head, arms, thighs, shins, feet, gun ...) is cut loose and becomes a falling rigidbody.</summary>
    void BreakApart(Vector3 hitPoint, Vector3 hitDirection)
    {
        // old colliders (the hit capsule) would fight with the pieces
        Collider[] oldCols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < oldCols.Length; i++) oldCols[i].enabled = false;

        // which bones become pieces: everything the skinned mesh is bound to + the known rig bones
        List<Transform> pieces = new List<Transform>();
        SkinnedMeshRenderer[] skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int s = 0; s < skins.Length; s++)
        {
            skins[s].updateWhenOffscreen = true; // bounds follow the falling bones, mesh is not culled
            Transform[] bones = skins[s].bones;
            for (int i = 0; i < bones.Length; i++) AddPiece(pieces, bones[i]);
        }
        for (int i = 0; i < all.Count; i++)
            if (all[i] != gunTip) AddPiece(pieces, all[i].t);
        if (pieces.Count == 0) return;

        // 1) a collider per piece, measured while the skeleton is still connected
        BoxCollider[] boxes = new BoxCollider[pieces.Count];
        for (int i = 0; i < pieces.Count; i++) boxes[i] = AddPieceCollider(pieces[i]);

        // 2) cut every piece loose from its parent (world position stays the same)
        Transform debris = new GameObject(name + " Debris").transform;
        for (int i = 0; i < pieces.Count; i++) pieces[i].SetParent(debris, true);

        // 3) physics + scatter, away from the hit point and along the bolt direction
        Vector3 dir = hitDirection.sqrMagnitude > 1e-6f ? hitDirection.normalized : Vector3.zero;
        for (int i = 0; i < pieces.Count; i++)
        {
            Transform p = pieces[i];
            Rigidbody body = p.GetComponent<Rigidbody>();
            if (body == null) body = p.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.mass = 1f;
            body.maxDepenetrationVelocity = 3f; // pieces start overlapping at the joints, don't let them pop apart violently

            Vector3 away = p.position - hitPoint;
            if (away.sqrMagnitude < 1e-4f) away = Random.onUnitSphere;
            away.Normalize();

            Vector3 v = dir * scatterForce
                      + away * (scatterForce * 0.5f)
                      + Vector3.up * (scatterForce * 0.4f)
                      + Random.insideUnitSphere * (scatterForce * 0.3f);
            body.AddForce(v, ForceMode.VelocityChange);
            body.AddTorque(Random.onUnitSphere * spinForce, ForceMode.VelocityChange);
        }

        // pieces of the same droid never collide with each other
        for (int i = 0; i < boxes.Length; i++)
            for (int j = i + 1; j < boxes.Length; j++)
                Physics.IgnoreCollision(boxes[i], boxes[j]);

        if (debrisLifetime > 0f)
        {
            Destroy(debris.gameObject, debrisLifetime);
            Destroy(gameObject, debrisLifetime);
        }
    }

    // ------------------------------------------------------------------ AI (movement happens in Update)

    void Update()
    {
        if (!ready || gm == null) return;
        float dt = Time.deltaTime;

        UpdateMode();
        walking = gm.DroidPatrolOff ? false : Think(dt); // Droid Patrol Off = stand still, do nothing

        stepsPerSecond = (mode == Mode.Chase) ? gm.RunSteps : gm.PatrolSteps;
        poseScale = Mathf.MoveTowards(poseScale, mode == Mode.Chase ? runPoseMultiplier : 1f, dt * 3f);
        UpdateLight(dt);
    }

    void UpdateMode()
    {
        Transform p = gm.Player;
        // Droid Off or Droid Patrol Off: never chase / target the player
        bool canChase = !gm.DroidOff && !gm.DroidPatrolOff;
        bool inRange = canChase && p != null && Vector3.Distance(transform.position, p.position) < gm.ChaseRadius;

        if (inRange && mode == Mode.Patrol)
        {
            mode = Mode.Chase;
            returning = false;
            goalNode = -1;
            blockedTime = 0f;
            backoffTimer = 0f;
            aimAtTarget = true;
        }
        else if (!inRange && mode == Mode.Chase)
        {
            mode = Mode.Patrol;
            aimAtTarget = false;
            BeginReturnToPatrol();
        }

        if (mode == Mode.Chase && p != null) target = p; // target = object tagged Player, found automatically
    }

    bool Matches(IG88EdgeKind kind)
    {
        return route == IG88Route.Green ? kind == IG88EdgeKind.Green : kind == IG88EdgeKind.Red;
    }

    void BeginReturnToPatrol()
    {
        if (toNode >= 0) returning = !Matches(gm.EdgeKindOf(fromNode, toNode));
        else returning = !gm.NodeInRoute(fromNode, route);

        if (returning)
        {
            goalNode = NearestRouteNode();
            if (goalNode < 0) returning = false;
        }
    }

    // nearest node that belongs to my patrol route, measured along the connections
    int NearestRouteNode()
    {
        int[] nodes = gm.RouteNodes(route);
        Vector3 pos = transform.position;
        int best = -1;
        float bestCost = float.MaxValue;
        for (int i = 0; i < nodes.Length; i++)
        {
            int n = nodes[i];
            float cost;
            if (toNode >= 0)
            {
                float viaTo = Vector3.Distance(pos, gm.NodePos(toNode)) + gm.PathDist(toNode, n);
                float viaFrom = Vector3.Distance(pos, gm.NodePos(fromNode)) + gm.PathDist(fromNode, n);
                cost = Mathf.Min(viaTo, viaFrom);
            }
            else cost = gm.PathDist(fromNode, n);

            if (cost < bestCost) { bestCost = cost; best = n; }
        }
        return best;
    }

    void UpdateChaseGoal(Transform player)
    {
        if (player == null) return;
        int best = gm.NearestNode(player.position);
        if (best < 0) return;
        if (goalNode < 0)
        {
            goalNode = best;
        }
        else if (best != goalNode)
        {
            float dBest = Vector3.Distance(player.position, gm.NodePos(best));
            float dCur = Vector3.Distance(player.position, gm.NodePos(goalNode));
            if (dBest + GoalHysteresis < dCur) goalNode = best; // hysteresis stops flip-flopping between 2 nodes
        }
    }

    // standing on fromNode: which node do we walk to next? (-1 = stay)
    int ChooseNext()
    {
        if (mode == Mode.Chase || returning)
        {
            if (goalNode < 0 || goalNode == fromNode) return -1;
            return gm.NextHop(fromNode, goalNode);
        }

        // patrol: random walk over the connections of MY route only, no turning back unless dead end
        tmp.Clear();
        List<IG88Link> links = gm.Links(fromNode);
        for (int i = 0; i < links.Count; i++)
            if (Matches(links[i].kind)) tmp.Add(links[i].node);
        if (tmp.Count == 0) return -1;
        if (tmp.Count > 1) tmp.Remove(prevNode);
        return tmp[Random.Range(0, tmp.Count)];
    }

    void Reverse()
    {
        int t = fromNode; fromNode = toNode; toNode = t;
        if (mode == Mode.Chase || returning) backoffTimer = 1.5f; // don't instantly turn around again
    }

    /// <summary>Robotic turn: rotates at a constant speed. Returns true when facing the direction.</summary>
    bool FaceDirection(Vector3 dir, float dt)
    {
        Vector3 up = transform.up;
        Vector3 flat = Vector3.ProjectOnPlane(dir, up);
        if (flat.sqrMagnitude < 1e-6f) return true;
        float ang = Vector3.SignedAngle(RestForward, flat, up);
        float maxStep = gm.TurnSpeed * dt;
        float step = Mathf.Clamp(ang, -maxStep, maxStep);
        transform.rotation = Quaternion.AngleAxis(step, up) * transform.rotation;
        return Mathf.Abs(ang - step) < 4f;
    }

    /// <summary>Returns true when the droid actually moved this frame (drives the walk animation).</summary>
    bool Think(float dt)
    {
        if (backoffTimer > 0f) backoffTimer -= dt;
        Transform player = gm.Player;

        if (mode == Mode.Chase) UpdateChaseGoal(player);

        // standing on a node -> decide where to go
        if (toNode < 0)
        {
            if (backoffTimer > 0f) return false;
            if (returning && fromNode == goalNode) returning = false;
            toNode = ChooseNext();
            if (toNode < 0) return false;
        }

        Vector3 pos = transform.position;

        // chase: droids are ranged, not melee. Once inside firing distance, hold position (aim stays on the player).
        // If the player backs away beyond the firing distance, the droid moves toward him again.
        if (mode == Mode.Chase && player != null && gm.FiringDistance > 0f &&
            Vector3.Distance(pos, player.position) <= gm.FiringDistance)
            return false;

        // chase / returning: if the goal is now behind us, turn around in the middle of the connection
        if ((mode == Mode.Chase || returning) && goalNode >= 0 && backoffTimer <= 0f)
        {
            float fwd = Vector3.Distance(pos, gm.NodePos(toNode)) + gm.PathDist(toNode, goalNode);
            float back = Vector3.Distance(pos, gm.NodePos(fromNode)) + gm.PathDist(fromNode, goalNode);
            if (back + ReverseMargin < fwd) Reverse();
        }

        Vector3 tp = gm.NodePos(toNode);
        Vector3 dir = tp - pos;
        float dist = dir.magnitude;

        // robotic: first turn on the spot, then walk in a straight line
        if (dist > 0.0001f && !FaceDirection(dir / dist, dt)) return false;

        float speed = (mode == Mode.Chase) ? gm.RunSpeed : gm.PatrolSpeed;
        float step = speed * dt;
        bool arrives = dist <= step;
        Vector3 newPos = arrives ? tp : pos + dir / dist * step;

        // never touch another droid: wait until it moves away
        if (gm.WouldCollide(this, newPos))
        {
            blockedTime += dt;
            if (blockedTime >= blockLimit)
            {
                // head-on / circular deadlock: back off
                blockedTime = 0f;
                blockLimit = Random.Range(2f, 4f);
                Reverse();
            }
            return false;
        }
        blockedTime = 0f;

        transform.position = newPos;
        if (arrives)
        {
            prevNode = fromNode;
            fromNode = toNode;
            toNode = -1;
        }
        return true;
    }

    void UpdateLight(float dt)
    {
        if (chaseLight == null) return;
        float goal = (mode == Mode.Chase) ? gm.ChaseLightIntensity : 0f;
        float rate = Mathf.Max(gm.ChaseLightIntensity, 0.0001f) / Mathf.Max(lightFadeTime, 0.01f);
        chaseLight.intensity = Mathf.MoveTowards(chaseLight.intensity, goal, rate * dt);
    }

    // ------------------------------------------------------------------ procedural animation

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
        float t = thighSwing * poseScale * s * w;
        float knee = kneeBend * poseScale * Mathf.Max(0f, c) * w;
        Swing(thigh, t);
        Swing(shin, -knee);
        Swing(foot, (knee - t) * 0.8f);
    }

    void ApplyWalk(float w)
    {
        float sL = Mathf.Sin(phase), cL = Mathf.Cos(phase);
        float sR = Mathf.Sin(phase + Mathf.PI), cR = Mathf.Cos(phase + Mathf.PI);
        Leg(thighL, shinL, footL, sL, cL, w);
        Leg(thighR, shinR, footR, sR, cR, w);
        Swing(armL, -armSwing * poseScale * sL * w);
        Swing(armR, armSwing * 0.25f * poseScale * sL * w);
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