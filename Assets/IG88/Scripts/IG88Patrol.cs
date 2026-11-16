using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// IG-88 brain: patrols, spots the player, chases, then stops and engages.
///
/// States
///   Patrol : walks a waypoint route (or wanders randomly around its start point).
///   Chase  : the player was spotted (or just lost) - walks to where they are / were last seen.
///   Engage : close enough (or chase is off) - stands still, turns to face the player and aims.
///
/// REQUIRES A BAKED NAVMESH over the hallways. The NavMesh is what keeps the droid out of walls and pipes:
/// it can only ever walk where the mesh exists. Bake it with an Agent Radius of about half the droid's widest
/// width (arms + gun included) so the mesh stops short of walls and pipes.
///
/// Add this to the IG-88 prefab root (next to IG88Controller). It adds its own NavMeshAgent.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[AddComponentMenu("IG88/IG88 Patrol")]
public class IG88Patrol : MonoBehaviour
{
    public enum State { Patrol, Chase, Engage }

    [Header("Player")]
    [Tooltip("Player or camera. GameManager fills this in. If empty: object tagged 'Player', then the main camera.")]
    public Transform player;
    [Tooltip("Raise the sight target if your player object's pivot is at its feet (try 1.0). Leave 0 for a camera.")]
    public float sightHeightOffset = 0f;

    [Header("Detection")]
    [Tooltip("How far away the droid can spot the player (needs a clear line of sight).")]
    public float detectionRange = 15f;
    [Tooltip("Width of the vision cone in degrees. 360 = sees all around.")]
    [Range(30f, 360f)] public float viewAngle = 150f;
    [Tooltip("Inside this distance the droid notices the player even from behind (line of sight still needed).")]
    public float closeSenseRange = 3f;
    [Tooltip("Ignore the player if they are this much higher/lower than the droid (other deck).")]
    public float maxHeightDifference = 4f;
    [Tooltip("What blocks the droid's view. Everything by default; the droid's own colliders are always ignored.")]
    public LayerMask sightBlockers = ~0;
    [Tooltip("Seconds between sight checks.")]
    public float detectionInterval = 0.2f;
    [Tooltip("Seconds the droid keeps hunting after it loses sight before going back to patrol.")]
    public float loseTargetTime = 3f;

    [Header("Reaction")]
    [Tooltip("On: walks up to Stop Distance, then stops and aims.   Off: stops and aims right where it stands the moment it sees you.")]
    public bool chaseWhenSpotted = true;
    [Tooltip("Distance at which the droid stops and aims (only used when Chase When Spotted is on).")]
    public float stopDistance = 5f;

    [Header("Movement")]
    public float moveSpeed = 2.5f;
    [Tooltip("Speed multiplier while chasing.")]
    public float chaseSpeedMultiplier = 1.25f;
    public float acceleration = 10f;
    [Tooltip("Degrees per second the droid can turn its body.")]
    public float turnSpeed = 150f;

    [Header("Patrol route")]
    [Tooltip("BEST FOR HALLWAYS: put empty GameObjects down the CENTRE of the hallway and drag them here in order. Leave empty to wander randomly instead.")]
    public Transform[] waypoints;
    [Tooltip("Off = loop 1>2>3>1.   On = 1>2>3>2>1.")]
    public bool pingPong = false;
    public float waitAtPoint = 1.5f;
    [Tooltip("Random wander only: how far from its start point the droid may roam.")]
    public float patrolRadius = 15f;
    [Tooltip("Random wander only: minimum distance of the next point.")]
    public float minPatrolStep = 4f;

    [Header("Stay in the middle of hallways")]
    [Tooltip("Gently steers the droid to the middle between the two walls while it walks.")]
    public bool centerInCorridors = true;
    [Tooltip("How far sideways to look for walls. Hallways up to twice this wide get centred.")]
    public float centeringProbe = 3f;
    [Tooltip("Max sideways correction speed (m/s).")]
    public float centeringSpeed = 1.5f;

    public bool drawGizmos = true;

    // ---------- public read-only state (IG88Controller uses these) ----------
    public State CurrentState { get { return state; } }
    public bool CanSeePlayer { get { return canSeePlayer; } }
    public Transform Player { get { return player; } }
    public bool IsMoving { get { return agent != null && agent.isOnNavMesh && agent.velocity.sqrMagnitude > 0.04f; } }
    public float CurrentSpeed
    {
        get
        {
            if (agent == null) return 0f;
            Vector3 v = agent.velocity; v.y = 0f;
            return v.magnitude;
        }
    }

    // ---------- runtime ----------
    NavMeshAgent agent;
    IG88Controller rig;
    NavMeshPath path;
    readonly RaycastHit[] hitBuf = new RaycastHit[32];

    State state = State.Patrol;
    Vector3 home, lastSeenPos;
    bool canSeePlayer, waiting, warnedNoPlayer;
    float timeSinceSeen = 999f;
    float sightTimer, repathTimer, waitTimer, lateralVel;
    int wpIndex, wpDir = 1;

    const float ArriveDistance = 0.35f;

    Vector3 Facing { get { return rig != null ? rig.RestForward : transform.forward; } }

    // =====================================================================
    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rig = GetComponent<IG88Controller>();
        path = new NavMeshPath();

        agent.updateRotation = false;           // we turn the droid ourselves: its "front" is defined by the gun, not transform.forward
        agent.autoBraking = true;
        agent.stoppingDistance = 0.1f;
        agent.avoidancePriority = Random.Range(40, 61);   // so droids can squeeze past each other
    }

    void Start()
    {
        if (!agent.isOnNavMesh)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 5f, agent.areaMask))
                agent.Warp(hit.position);
            else
            {
                Debug.LogError("IG88Patrol: no NavMesh within 5 m of '" + name + "'. Bake a NavMesh over the hallways first.", this);
                enabled = false;
                return;
            }
        }
        home = transform.position;
        StartPatrolRoute();
    }

    void Update()
    {
        if (!agent.isOnNavMesh) return;
        float dt = Time.deltaTime;

        UpdateSenses(dt);
        UpdateState(dt);

        agent.speed = (state == State.Chase) ? moveSpeed * chaseSpeedMultiplier : moveSpeed;
        agent.acceleration = acceleration;

        UpdateFacing(dt);
        if (centerInCorridors) ApplyCentering(dt);
    }

    // =====================================================================
    //  SENSES
    // =====================================================================
    void UpdateSenses(float dt)
    {
        if (player == null)
        {
            canSeePlayer = false;
            sightTimer -= dt;
            if (sightTimer <= 0f)
            {
                sightTimer = 1f;
                player = ResolvePlayer();
                if (player == null && !warnedNoPlayer)
                {
                    warnedNoPlayer = true;
                    Debug.LogWarning("IG88Patrol: no player found. Assign one on GameManager, tag your player 'Player', or tag your camera 'MainCamera'.", this);
                }
            }
            return;
        }

        sightTimer -= dt;
        if (sightTimer <= 0f)
        {
            sightTimer = detectionInterval;
            canSeePlayer = CheckSight();
        }

        if (canSeePlayer) { lastSeenPos = player.position; timeSinceSeen = 0f; }
        else timeSinceSeen += dt;
    }

    Transform ResolvePlayer()
    {
        GameObject tagged = GameObject.FindWithTag("Player");
        if (tagged != null) return tagged.transform;
        UnityEngine.Camera cam = UnityEngine.Camera.main;
        return cam != null ? cam.transform : null;
    }

    bool CheckSight()
    {
        Vector3 toPlayer = player.position - transform.position;
        Vector3 flat = Vector3.ProjectOnPlane(toPlayer, Vector3.up);
        float dist = flat.magnitude;

        // Once alerted the droid keeps tracking: wider range, no vision cone.
        bool alerted = state != State.Patrol;
        float range = alerted ? detectionRange * 1.25f : detectionRange;

        if (dist > range) return false;
        if (Mathf.Abs(toPlayer.y) > maxHeightDifference) return false;
        if (!alerted && dist > closeSenseRange && Vector3.Angle(Facing, flat) > viewAngle * 0.5f) return false;

        Vector3 eye = rig != null ? rig.EyePosition : transform.position + Vector3.up * 1.6f;
        Vector3 aimPoint = player.position + Vector3.up * sightHeightOffset;
        return HasLineOfSight(eye, aimPoint);
    }

    bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len < 0.01f) return true;

        int n = Physics.RaycastNonAlloc(from, dir / len, hitBuf, len, sightBlockers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        Transform nearestT = null;
        for (int i = 0; i < n; i++)
        {
            Transform t = hitBuf[i].transform;
            if (t.IsChildOf(transform)) continue;                    // our own body
            if (hitBuf[i].distance < nearest) { nearest = hitBuf[i].distance; nearestT = t; }
        }
        if (nearestT == null) return true;                           // nothing in the way
        return nearestT.IsChildOf(player) || player.IsChildOf(nearestT);   // first thing hit is the player
    }

    // =====================================================================
    //  STATE MACHINE
    // =====================================================================
    void UpdateState(float dt)
    {
        float dist = player != null ? FlatDistance(player.position) : float.MaxValue;
        float engageDist = chaseWhenSpotted ? stopDistance : float.MaxValue;

        switch (state)
        {
            case State.Patrol:
                if (canSeePlayer) Enter(dist <= engageDist ? State.Engage : State.Chase);
                else TickPatrol(dt);
                break;

            case State.Chase:
                if (canSeePlayer && dist <= engageDist) Enter(State.Engage);
                else if (!canSeePlayer && timeSinceSeen > loseTargetTime) Enter(State.Patrol);
                else TickChase(dt);
                break;

            case State.Engage:
                // small hysteresis so it doesn't flicker between Engage and Chase at the boundary
                if (!canSeePlayer || dist > engageDist + 1f) Enter(State.Chase);
                break;
        }
    }

    void Enter(State s)
    {
        if (state == s) return;
        state = s;
        switch (s)
        {
            case State.Patrol: StartPatrolRoute(); break;
            case State.Chase:  repathTimer = 0f; break;
            case State.Engage: agent.ResetPath(); break;
        }
    }

    float FlatDistance(Vector3 p)
    {
        Vector3 d = p - transform.position; d.y = 0f;
        return d.magnitude;
    }

    // ---------- chase ----------
    void TickChase(float dt)
    {
        repathTimer -= dt;
        if (repathTimer > 0f) return;
        repathTimer = 0.25f;
        MoveTo(lastSeenPos, false);
    }

    // ---------- patrol ----------
    bool HasWaypoints
    {
        get
        {
            if (waypoints == null) return false;
            for (int i = 0; i < waypoints.Length; i++) if (waypoints[i] != null) return true;
            return false;
        }
    }

    void TickPatrol(float dt)
    {
        if (waiting)
        {
            waitTimer -= dt;
            if (waitTimer <= 0f) { waiting = false; NextPatrolTarget(); }
            return;
        }
        if (agent.pathPending) return;
        if (!agent.hasPath || agent.remainingDistance <= ArriveDistance)
        {
            agent.ResetPath();
            waiting = true;
            waitTimer = Mathf.Max(waitAtPoint, 0.25f);
        }
    }

    void StartPatrolRoute()
    {
        waiting = false;
        if (HasWaypoints)
        {
            wpIndex = NearestWaypoint();                  // after a chase, go to the closest point, not back to the start
            if (MoveTo(waypoints[wpIndex].position, true)) return;
            NextPatrolTarget();
        }
        else PickRandomTarget();
    }

    int NearestWaypoint()
    {
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            float d = (waypoints[i].position - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    void NextPatrolTarget()
    {
        if (!HasWaypoints) { PickRandomTarget(); return; }
        for (int i = 0; i < waypoints.Length; i++)
        {
            AdvanceWaypoint();
            Transform w = waypoints[wpIndex];
            if (w != null && MoveTo(w.position, true)) return;
        }
    }

    void AdvanceWaypoint()
    {
        int n = waypoints.Length;
        if (pingPong)
        {
            if (wpIndex + wpDir < 0 || wpIndex + wpDir >= n) wpDir = -wpDir;
            wpIndex = Mathf.Clamp(wpIndex + wpDir, 0, n - 1);
        }
        else wpIndex = (wpIndex + 1) % n;
    }

    void PickRandomTarget()
    {
        for (int i = 0; i < 12; i++)
        {
            Vector2 c = Random.insideUnitCircle * patrolRadius;
            Vector3 probe = home + new Vector3(c.x, 0f, c.y);
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(probe, out hit, 2f, agent.areaMask)) continue;

            Vector3 p = centerInCorridors ? CenterOnCorridor(hit.position) : hit.position;
            if ((p - transform.position).sqrMagnitude < minPatrolStep * minPatrolStep) continue;
            if (MoveTo(p, true)) return;                  // only accept points we can actually reach
        }
    }

    bool MoveTo(Vector3 worldPos, bool requireCompletePath)
    {
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(worldPos, out hit, 2f, agent.areaMask)) return false;

        if (requireCompletePath)
        {
            if (!NavMesh.CalculatePath(agent.nextPosition, hit.position, agent.areaMask, path)) return false;
            if (path.status != NavMeshPathStatus.PathComplete) return false;
            return agent.SetPath(path);
        }
        return agent.SetDestination(hit.position);
    }

    // =====================================================================
    //  FACING
    // =====================================================================
    void UpdateFacing(float dt)
    {
        Vector3 want;
        if (state == State.Engage && player != null) want = player.position - transform.position;   // turn to face the player
        else want = agent.desiredVelocity;                                                              // face where we're walking
        want.y = 0f;
        if (want.sqrMagnitude < 0.01f) return;

        float angle = Vector3.SignedAngle(Facing, want, Vector3.up);
        float step = Mathf.Clamp(angle, -turnSpeed * dt, turnSpeed * dt);
        transform.Rotate(Vector3.up, step, Space.World);
    }

    // =====================================================================
    //  HALLWAY CENTRING
    //  The NavMesh already keeps the droid out of walls. These two helpers keep it away from the *edges* of the mesh.
    // =====================================================================

    /// <summary>Snaps a NavMesh point to the middle of the hallway it is in (leaves open rooms alone).</summary>
    Vector3 CenterOnCorridor(Vector3 p)
    {
        NavMeshHit near;
        if (!NavMesh.FindClosestEdge(p, out near, agent.areaMask)) return p;

        Vector3 away = Vector3.ProjectOnPlane(p - near.position, Vector3.up);
        if (away.sqrMagnitude < 1e-4f) return p;
        away.Normalize();

        NavMeshHit far;
        if (!NavMesh.Raycast(p, p + away * (centeringProbe * 2f), out far, agent.areaMask)) return p;   // no wall opposite: open room

        if (near.distance + far.distance > centeringProbe * 2f) return p;                              // too wide to be a hallway

        Vector3 mid = (near.position + far.position) * 0.5f;
        NavMeshHit snapped;
        return NavMesh.SamplePosition(mid, out snapped, 1f, agent.areaMask) ? snapped.position : p;
    }

    /// <summary>While walking, slides the droid sideways toward the middle between the walls on its left and right.</summary>
    void ApplyCentering(float dt)
    {
        Vector3 vel = agent.velocity; vel.y = 0f;
        if (vel.sqrMagnitude < 0.09f || !agent.hasPath) { lateralVel = 0f; return; }

        Vector3 right = Vector3.Cross(Vector3.up, vel.normalized);
        Vector3 pos = agent.nextPosition;

        float dR, dL;
        bool wallR = SideDistance(pos, right, out dR);
        bool wallL = SideDistance(pos, -right, out dL);

        float goal = 0f;
        if (wallR && wallL)                                                   // only centre between two real walls (not at junctions)
        {
            float fade = Mathf.Clamp01(agent.remainingDistance / 1.5f);        // don't fight the arrival at the destination
            goal = Mathf.Clamp(dR - dL, -centeringSpeed, centeringSpeed) * fade;   // + = more room on the right
        }

        lateralVel = Mathf.MoveTowards(lateralVel, goal, centeringSpeed * 4f * dt);
        if (Mathf.Abs(lateralVel) > 0.001f)
            agent.Move(right * (lateralVel * dt));                            // Move() is clamped to the NavMesh, so this can't push into a wall
    }

    bool SideDistance(Vector3 from, Vector3 dir, out float dist)
    {
        NavMeshHit hit;
        if (NavMesh.Raycast(from, from + dir * centeringProbe, out hit, agent.areaMask))
        {
            dist = hit.distance;
            return true;
        }
        dist = centeringProbe;
        return false;
    }

    // =====================================================================
    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Vector3 pos = transform.position;
        Vector3 fwd = Application.isPlaying ? Facing : transform.forward;
        fwd.y = 0f; fwd.Normalize();

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, detectionRange);
        Gizmos.DrawRay(pos + Vector3.up * 0.1f, Quaternion.AngleAxis(viewAngle * 0.5f, Vector3.up) * fwd * detectionRange);
        Gizmos.DrawRay(pos + Vector3.up * 0.1f, Quaternion.AngleAxis(-viewAngle * 0.5f, Vector3.up) * fwd * detectionRange);

        if (chaseWhenSpotted)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(pos, stopDistance);
        }

        if (HasWaypoints)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.DrawWireSphere(waypoints[i].position, 0.25f);
                int next = (i + 1) % waypoints.Length;
                if ((pingPong && i == waypoints.Length - 1) || waypoints[next] == null) continue;
                Gizmos.DrawLine(waypoints[i].position, waypoints[next].position);
            }
        }
        else
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(Application.isPlaying ? home : pos, patrolRadius);
        }
    }
}