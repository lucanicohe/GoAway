using System.Collections.Generic;
using UnityEngine;
using FlatheadRobotKit;

public enum FlatheadEdgeKind { Walk, Jump }
public enum FlatheadSide { Top, Right, Bottom, Left }
public struct FlatheadLink { public int node; public FlatheadEdgeKind kind; }

/// <summary>
/// Waypoint movement for the Flathead robots. Same idea as the EnemyMoveSystem of the IG-88 droids: NO NavMesh, a robot only ever
/// moves along the drawn waypoint connections.
///
///  1) THE WAYPOINTS (static, one graph shared by all Flatheads): 20 waypoints, W1-W13 (walls) and T1-T7 (ceiling), joined in one loop.
///       WALK connection (red in your drawing)  = the robot walks along it, feet planted on the surface.
///       JUMP connection (blue in your drawing) = the robot crouches, jumps and lands on the next waypoint (the surface can change here:
///                                                wall -> ceiling, wall -> other wall ...). The body turns in the air to the pose of the landing waypoint.
///     Every waypoint has a fixed pose (see ComputeFrame): which wall it is on (arrows of your drawing) and wall or ceiling.
///       WALL waypoint    = feet on the wall the arrow points to, body sticks out into the room, robot faces UP the wall.
///       CEILING waypoint = feet UP on the ceiling, robot faces away from the wall of its arrow (toward the middle of the room).
///
///  2) THE MOVEMENT + DECISIONS OF ONE ROBOT (one instance per robot, created by the FlatheadEnemy). Every frame FlatheadEnemy calls Tick().
///     The Flathead is a SNEAKY droid (think mosquito). It never opens fire just because it sees the player:
///      - PATROL (lurk): random walk over the connections inside its patrol zone (jumps included). Never shoots.
///      - DETECTION: a Flathead that SEES the player, or is within the Flathead Sense Radius (through walls, by distance alone), alerts ALL
///        the Flatheads for 'Lose Sight Time' seconds. A Flathead that never saw him is alerted too (the alert is shared).
///      - ONE AT A TIME: while the alert lasts, the GameManager sends ONE Flathead (the closest that isn't hiding after a scare) after the
///        player, whether or not that one has a line of sight. All the others keep lurking until it is their turn.
///      - STALK: the chosen one creeps along the track (walking and jumping, slowly) toward the closest STANDABLE point of the track
///        to the player and keeps following him as he moves.
///      - STRIKE: close enough (or as close as the track lets it get) and with a clear shot: it stops, aims and empties an automatic burst.
///      - DODGE: a real but brief look makes it move a little way off, then it resumes its chase.
///      - STRIKE: a continuous stare or a second look in quick succession makes it attack immediately.
///      - FLEE: after its burst it scurries away to a far waypoint. It stays away for 'Flathead Retreat Time', and the GameManager
///        sends the next Flathead to try its luck.
///      - Never touches another Flathead: it waits until the other one moves away.
/// </summary>
internal sealed class FlatheadMoveSystem
{
    // =====================================================================
    //  1) WAYPOINTS (shared by all Flatheads)
    // =====================================================================

    public const int W1 = 0, W2 = 1, W3 = 2, W4 = 3, W5 = 4, W6 = 5, W7 = 6, W8 = 7, W9 = 8, W10 = 9, W11 = 10, W12 = 11, W13 = 12,
                     T1 = 13, T2 = 14, T3 = 15, T4 = 16, T5 = 17, T6 = 18, T7 = 19;
    public const int NodeTotal = 20;

    public static readonly string[] NodeNames =
    {
        "W1", "W2", "W3", "W4", "W5", "W6", "W7", "W8", "W9", "W10", "W11", "W12", "W13",
        "T1", "T2", "T3", "T4", "T5", "T6", "T7"
    };

    // which wall each waypoint is on = where the arrow of your drawing points (T = ceiling, its arrow only says which side of the room)
    static readonly FlatheadSide[] Sides =
    {
        FlatheadSide.Right,    // W1
        FlatheadSide.Top,      // W2
        FlatheadSide.Top,      // W3
        FlatheadSide.Top,      // W4
        FlatheadSide.Top,      // W5
        FlatheadSide.Left,     // W6
        FlatheadSide.Left,     // W7
        FlatheadSide.Left,     // W8
        FlatheadSide.Bottom,   // W9
        FlatheadSide.Bottom,   // W10
        FlatheadSide.Bottom,   // W11
        FlatheadSide.Bottom,   // W12
        FlatheadSide.Right,    // W13
        FlatheadSide.Top,      // T1
        FlatheadSide.Top,      // T2
        FlatheadSide.Left,     // T3
        FlatheadSide.Bottom,   // T4
        FlatheadSide.Right,    // T5
        FlatheadSide.Right,    // T6
        FlatheadSide.Right,    // T7
    };

    public struct Edge
    {
        public int a, b;
        public FlatheadEdgeKind kind;
        public Edge(int a, int b, FlatheadEdgeKind kind) { this.a = a; this.b = b; this.kind = kind; }
    }

    // all connections, copied from the drawing (red = Walk, blue = Jump), going round the room
    public static readonly Edge[] Edges =
    {
        new Edge(T1,  W3,  FlatheadEdgeKind.Jump),
        new Edge(W3,  W2,  FlatheadEdgeKind.Walk),
        new Edge(W2,  W1,  FlatheadEdgeKind.Jump),
        new Edge(W1,  T7,  FlatheadEdgeKind.Jump),
        new Edge(T7,  T6,  FlatheadEdgeKind.Jump),
        new Edge(T6,  T5,  FlatheadEdgeKind.Walk),
        new Edge(T5,  W13, FlatheadEdgeKind.Jump),
        new Edge(W13, W12, FlatheadEdgeKind.Jump),
        new Edge(W12, W11, FlatheadEdgeKind.Walk),
        new Edge(W11, T4,  FlatheadEdgeKind.Jump),
        new Edge(T4,  W10, FlatheadEdgeKind.Jump),
        new Edge(W10, W9,  FlatheadEdgeKind.Walk),
        new Edge(W9,  W8,  FlatheadEdgeKind.Jump),
        new Edge(W8,  T3,  FlatheadEdgeKind.Jump),
        new Edge(T3,  W7,  FlatheadEdgeKind.Jump),
        new Edge(W7,  W6,  FlatheadEdgeKind.Walk),
        new Edge(W6,  W5,  FlatheadEdgeKind.Jump),
        new Edge(W5,  W4,  FlatheadEdgeKind.Walk),
        new Edge(W4,  T2,  FlatheadEdgeKind.Jump),
        new Edge(T2,  T1,  FlatheadEdgeKind.Walk),
    };

    static Transform[] nodes;
    static List<FlatheadLink>[] links;
    static float[,] dist;
    static int[,] next;
    static bool graphReady;
    static float surfaceOffset;
    static readonly Vector3[] nodeUp = new Vector3[NodeTotal];    // robot 'up' at every waypoint (away from the surface it stands on)
    static readonly Vector3[] nodeFwd = new Vector3[NodeTotal];   // direction the robot faces at every waypoint

    /// <summary>True when all 20 waypoints are assigned and the graph is built.</summary>
    public static bool Ready { get { return graphReady; } }

    /// <summary>
    /// Hands in the 20 waypoint objects in this order: W1..W13, T1..T7. 'pictureUp' = world direction of the TOP of your drawing
    /// (the arrows pointing up in the drawing point that way), 'offset' = metres the robot is lifted off the surface.
    /// Returns false when one is missing ('missing' then lists the names).
    /// </summary>
    public static bool SetWaypoints(Transform[] waypoints, List<string> missing, Vector3 pictureUp, float offset)
    {
        missing.Clear();
        for (int i = 0; i < NodeTotal; i++)
            if (waypoints == null || i >= waypoints.Length || waypoints[i] == null) missing.Add(NodeNames[i]);

        graphReady = missing.Count == 0;
        if (!graphReady) return false;

        nodes = waypoints;
        links = new List<FlatheadLink>[NodeTotal];
        for (int i = 0; i < NodeTotal; i++) links[i] = new List<FlatheadLink>();
        foreach (Edge e in Edges)
        {
            links[e.a].Add(new FlatheadLink { node = e.b, kind = e.kind });
            links[e.b].Add(new FlatheadLink { node = e.a, kind = e.kind });
        }

        dist = new float[NodeTotal, NodeTotal];
        next = new int[NodeTotal, NodeTotal];
        Configure(pictureUp, offset);
        return true;
    }

    /// <summary>Recomputes the pose of every waypoint and the shortest paths (the GameManager calls it once a second, so moving waypoints while playing works).</summary>
    public static void Configure(Vector3 pictureUp, float offset)
    {
        if (!graphReady) return;
        surfaceOffset = offset;
        for (int i = 0; i < NodeTotal; i++) ComputeFrame(i, pictureUp, out nodeUp[i], out nodeFwd[i]);
        RebuildDistances();
    }

    // The pose of a waypoint. 'pictureUp' = world direction of the top of the drawing; right of the drawing = Cross(up, pictureUp).
    //  wall    : robot 'up' = away from the wall (into the room), robot faces up the wall (world up)
    //  ceiling : robot 'up' = world down (feet on the ceiling), robot faces toward the middle of the room (away from its wall).
    //            (A robot that climbs the wall and goes on over the edge onto the ceiling faces this way, so wall <-> ceiling jumps are a clean 90 degree turn.)
    static void ComputeFrame(int node, Vector3 pictureUp, out Vector3 up, out Vector3 fwd)
    {
        Vector3 pu = Vector3.ProjectOnPlane(pictureUp, Vector3.up);
        if (pu.sqrMagnitude < 1e-6f) pu = Vector3.forward;
        pu.Normalize();
        Vector3 pr = Vector3.Cross(Vector3.up, pu);

        Vector3 toWall;
        switch (Sides[node])
        {
            case FlatheadSide.Top: toWall = pu; break;
            case FlatheadSide.Bottom: toWall = -pu; break;
            case FlatheadSide.Right: toWall = pr; break;
            default: toWall = -pr; break;
        }

        if (node >= T1) { up = Vector3.down; fwd = -toWall; }
        else { up = -toWall; fwd = Vector3.up; }
    }

    /// <summary>The pose of a waypoint without the graph being built (used by the Scene view gizmos).</summary>
    public static void PreviewFrame(int node, Vector3 pictureUp, out Vector3 up, out Vector3 fwd) { ComputeFrame(node, pictureUp, out up, out fwd); }

    // Floyd-Warshall over all connections (walk + jump)
    public static void RebuildDistances()
    {
        if (!graphReady) return;
        const float INF = 1e9f;
        for (int i = 0; i < NodeTotal; i++)
            for (int j = 0; j < NodeTotal; j++)
            {
                dist[i, j] = i == j ? 0f : INF;
                next[i, j] = i == j ? i : -1;
            }

        foreach (Edge e in Edges)
        {
            float w = Vector3.Distance(NodePos(e.a), NodePos(e.b));
            dist[e.a, e.b] = dist[e.b, e.a] = w;
            next[e.a, e.b] = e.b;
            next[e.b, e.a] = e.a;
        }

        for (int k = 0; k < NodeTotal; k++)
            for (int i = 0; i < NodeTotal; i++)
                for (int j = 0; j < NodeTotal; j++)
                    if (dist[i, k] + dist[k, j] < dist[i, j])
                    {
                        dist[i, j] = dist[i, k] + dist[k, j];
                        next[i, j] = next[i, k];
                    }
    }

    /// <summary>Where the robot's feet stand at a waypoint (the waypoint object, lifted by the surface offset).</summary>
    public static Vector3 NodePos(int i) { return nodes[i].position + nodeUp[i] * surfaceOffset; }
    public static void NodeFrame(int i, out Vector3 up, out Vector3 fwd) { up = nodeUp[i]; fwd = nodeFwd[i]; }
    public static bool IsCeiling(int i) { return i >= T1; }
    public static float PathDist(int a, int b) { return dist[a, b]; }
    public static int NextHop(int from, int goal) { return next[from, goal]; }
    public static List<FlatheadLink> Links(int node) { return links[node]; }

    public static FlatheadEdgeKind EdgeKindOf(int a, int b)
    {
        // Use index-based loop to avoid heap-allocating an enumerator over a struct array.
        for (int i = 0; i < Edges.Length; i++)
        {
            Edge e = Edges[i];
            if ((e.a == a && e.b == b) || (e.a == b && e.b == a)) return e.kind;
        }
        return FlatheadEdgeKind.Walk;
    }

    /// <summary>
    /// The closest point of the track to a world position where a robot can STAND: anywhere on a WALK connection, but only the two end waypoints of a JUMP
    /// connection (a robot can't stop in the air). The point lies on the connection a-b, t = 0 is at a, t = 1 is at b.
    /// </summary>
    public static bool ClosestStandablePoint(Vector3 p, out int a, out int b, out float t, out Vector3 point)
    {
        a = -1; b = -1; t = 0f; point = p;
        if (!graphReady) return false;

        float best = float.MaxValue;
        for (int i = 0; i < Edges.Length; i++)
        {
            Edge e = Edges[i];
            Vector3 pa = NodePos(e.a);
            Vector3 pb = NodePos(e.b);
            if (e.kind == FlatheadEdgeKind.Walk)
            {
                Vector3 ab = pb - pa;
                float len2 = ab.sqrMagnitude;
                float tt = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - pa, ab) / len2) : 0f;
                Vector3 q = pa + ab * tt;
                float d = (q - p).sqrMagnitude;
                if (d < best) { best = d; a = e.a; b = e.b; t = tt; point = q; }
            }
            else
            {
                float da = (pa - p).sqrMagnitude, db = (pb - p).sqrMagnitude;
                if (da < best) { best = da; a = e.a; b = e.b; t = 0f; point = pa; }
                if (db < best) { best = db; a = e.a; b = e.b; t = 1f; point = pb; }
            }
        }
        return a >= 0;
    }

    // Reusable queue avoids allocating a new one per robot spawn.
    static readonly Queue<int> hopQueue = new Queue<int>();

    /// <summary>Number of connections from 'start' to every waypoint.</summary>
    static int[] Hops(int start)
    {
        int[] hops = new int[NodeTotal];
        for (int i = 0; i < hops.Length; i++) hops[i] = 999;
        hopQueue.Clear();
        hops[start] = 0;
        hopQueue.Enqueue(start);
        while (hopQueue.Count > 0)
        {
            int n = hopQueue.Dequeue();
            List<FlatheadLink> ls = links[n];
            for (int i = 0; i < ls.Count; i++)
            {
                if (hops[ls[i].node] != 999) continue;
                hops[ls[i].node] = hops[n] + 1;
                hopQueue.Enqueue(ls[i].node);
            }
        }
        return hops;
    }

    // =====================================================================
    //  2) MOVEMENT + DECISIONS OF ONE ROBOT
    // =====================================================================

    enum Mode { Patrol, Stalk, Strike, Dodge, Flee }

    readonly FlatheadEnemy bot;
    readonly Transform owner;
    readonly GameManager gm;
    int[] homeHops;                    // connections from my home waypoint (where I spawned; after a flight, where I ran to)

    Mode mode = Mode.Patrol;

    int fromNode;           // waypoint we are on / just left
    int toNode = -1;        // waypoint we are going to (-1 = standing on fromNode, need a new target)
    int prevNode = -1;      // waypoint we came from (patrol: don't turn back unless it is the only way)
    int goalNode = -1;      // chase goal

    // chase stop point: the closest STANDABLE point of the track to the player
    bool hasStopPoint;
    int stopA = -1, stopB = -1;
    float stopT;
    Vector3 stopPoint;

    float blockedTime, blockLimit, backoffTimer;

    bool seesPlayer, clearShot;
    float blindTime;             // seconds a stalker has stood at its closest point without a clear shot (it gives up after 'Lose Sight Time')

    // sneak attack
    float cooldownUntil;         // after a burst flee, it cannot be picked as the next hunter until this time
    int shotsFired;              // bolts fired in this hunt
    float lastShotTime = -999f;
    float gazeTimer;             // current uninterrupted look duration
    float currentLookStartTime;
    float lastQualifiedLookTime = -999f;
    bool hasQualifiedLook;
    bool currentLookCounted;
    bool currentLookTriggeredAttack;
    bool wasLooking;
    float strikeLostTime;        // seconds the strike has had no clear shot
    float strikeStartDist;       // distance to the player when the strike began
    float fleeStartTime;
    float dodgeStartTime;
    float speedMult = 1f;        // 1 = normal walk, <1 = creeping, >1 = scurrying (smoothed)

    // legs
    float moveW;                 // 0..1 ramp of the walking speed (same 0.25 s ramp as the legs)
    float notWalkingTime = 99f;  // seconds the legs have been at rest
    float walkPause;             // legs go back to rest for a moment when the walk direction changes a lot
    float appliedAngle;          // walk direction the legs are using
    bool angleValid;

    // jump
    bool onJump, jumpStarted;
    float jumpWait;
    Vector3 jumpFrom, jumpTo, jumpBulge;
    Quaternion jumpRotFrom, jumpRotTo;

    static readonly List<int> tmp = new List<int>();
    static readonly List<int> tmp2 = new List<int>();

    const float ReverseMargin = 0.05f;
    const float GoalHysteresis = 0.75f;
    const float StopPointHysteresis = 0.25f;
    const float ArriveEps = 0.02f;
    const float CloseDistance = 1f;       // chasing robots never come closer than this to the player
    const float DirectionPause = 0.3f;    // legs re-plant when the walk direction changes by more than DirectionTurn
    const float DirectionTurn = 25f;
    const float StrikeLeash = 3f;          // a striking robot only follows a player who backs off this far, then it creeps after him again
    const float StrikeLostLimit = 0.6f;    // seconds without a clear shot before a strike turns back into a stalk
    const float ShotsToFleeDelay = 0.4f;   // after the last bolt of its burst it stays this long, then runs
    const float FleeMinRun = 2.5f;         // a waypoint closer than this to a fleeing robot is no escape
    const float FleeTimeout = 12f;         // safety: a flight never lasts longer than this
    const float GlanceIgnoreTime = 0.5f;   // camera flicks shorter than this do not affect the Flathead
    const float DoubleLookWindow = 3f;     // a second real look this soon makes it attack
    const float DodgeMaxPath = 3.5f;       // brief evasions only pick a nearby waypoint
    const float DodgeTimeout = 1.5f;       // return to the chase quickly if the nearby waypoint takes too long

    /// <summary>True while not just lurking (stalking, dodging, striking or fleeing): such a robot is updated every frame.</summary>
    public bool Hunting { get { return mode != Mode.Patrol; } }
    /// <summary>True while it stalks, dodges or strikes: the turret follows the player.</summary>
    public bool Aiming { get { return mode == Mode.Stalk || mode == Mode.Strike || mode == Mode.Dodge; } }
    /// <summary>True while it stands still and is allowed to shoot.</summary>
    public bool Striking { get { return mode == Mode.Strike; } }
    /// <summary>True once it has fired all the bolts of its burst ('Flathead Shots Per Strike'): it fires no more and only waits to run.</summary>
    public bool BurstDone { get { int max = gm.FlatheadShotsPerStrike; return max > 0 && shotsFired >= max; } }
    /// <summary>True while it is the one Flathead going after the player (stalking, dodging or striking).</summary>
    public bool OnTheHunt { get { return mode == Mode.Stalk || mode == Mode.Strike || mode == Mode.Dodge; } }
    /// <summary>Lurking and not hiding from the player after a scare or a failed stalk: the GameManager may send this one while the Flatheads are alerted
    /// (it doesn't need to see the player itself).</summary>
    public bool CanHunt { get { return mode == Mode.Patrol && Time.time >= cooldownUntil; } }
    // stalking, dodging and fleeing walk toward a goal waypoint (patrol picks a random one, a strike stands still)
    bool Directed { get { return mode == Mode.Stalk || mode == Mode.Dodge || mode == Mode.Flee; } }
    public bool SeesPlayer { get { return seesPlayer; } }
    public bool ClearShot { get { return clearShot; } }

    /// <summary>Puts the robot on its start waypoint, in the pose of that waypoint.</summary>
    public FlatheadMoveSystem(FlatheadEnemy bot, GameManager gm, int startNode)
    {
        this.bot = bot;
        this.gm = gm;
        owner = bot.transform;
        fromNode = startNode;
        blockLimit = Random.Range(2f, 4f);
        homeHops = Hops(startNode);

        Vector3 up, fwd;
        NodeFrame(startNode, out up, out fwd);
        owner.SetPositionAndRotation(NodePos(startNode), bot.PoseRotation(fwd, up));
    }

    /// <summary>One frame of detection + movement. Returns true when the robot walked this frame.</summary>
    public bool Tick(float dt)
    {
        UpdateAwareness();
        UpdateMode(dt);
        UpdateSpeed(dt);
        if (onJump) return UpdateJump(dt);            // a jump is always finished, whatever happens
        if (gm.DroidPatrolOff) { LegsOff(dt); return false; }
        return Think(dt);
    }

    // ------------------------------------------------------------------ detection (same rules as the IG-88)

    void UpdateAwareness()
    {
        Transform p = gm.Player;
        bool canChase = !gm.DroidOff && !gm.DroidPatrolOff && p != null;

        seesPlayer = false;
        clearShot = false;
        if (canChase)
        {
            float d = Vector3.Distance(owner.position, p.position);
            seesPlayer = d < gm.SightRange && gm.HasClearShot(bot.EyePosition, gm.PlayerAimPoint);
            if (seesPlayer)
                clearShot = bot.HasGun && gm.HasClearShot(bot.MuzzlePosition, gm.PlayerAimPoint);

            // Seeing him, or being close to him (through walls, by distance alone), alerts ALL the Flatheads, not just this one.
            bool sensed = gm.FlatheadSenseRadius > 0f && d < gm.FlatheadSenseRadius;
            if (seesPlayer || sensed) gm.FlatheadAlert();
        }

        // NOTE: noticing the player no longer starts anything by itself. The GameManager sends ONE Flathead at a time (BeginStalk).
        // The hunter keeps going while the shared alert lasts; once nobody has seen or sensed the player for 'Lose Sight Time', it gives up.
        if ((mode == Mode.Stalk || mode == Mode.Strike || mode == Mode.Dodge) && (!canChase || !gm.FlatheadsAlerted))
        {
            mode = Mode.Patrol;
            hasStopPoint = false;
            ResetGaze();
        }
    }

    // ------------------------------------------------------------------ hunt: stalk -> dodge or strike -> flee after the burst

    /// <summary>The GameManager picked this Flathead to go after the player: it starts creeping toward him.</summary>
    public void BeginStalk()
    {
        mode = Mode.Stalk;
        goalNode = -1;
        hasStopPoint = false;
        blockedTime = 0f;
        backoffTimer = 0f;
        shotsFired = 0;
        ResetGaze();
        strikeLostTime = 0f;
        blindTime = 0f;
    }

    /// <summary>Called by the FlatheadEnemy for every bolt it fires (a strike is a short burst, then the robot runs).</summary>
    public void NoteShot()
    {
        shotsFired++;
        lastShotTime = Time.time;
    }

    void BeginStrike()
    {
        mode = Mode.Strike;
        strikeLostTime = 0f;
        Transform p = gm.Player;
        strikeStartDist = p != null ? Vector3.Distance(owner.position, p.position) : 0f;
    }

    // After it finishes its burst, it runs to a far waypoint and keeps away for 'Flathead Retreat Time'.
    void BeginFlee()
    {
        mode = Mode.Flee;
        ResetGaze();
        hasStopPoint = false;
        blockedTime = 0f;
        backoffTimer = 0f;
        fleeStartTime = Time.time;
        cooldownUntil = Time.time + gm.FlatheadRetreatTime;
        goalNode = PickFleeNode();
    }

    void EndFlee()
    {
        mode = Mode.Patrol;
        hasStopPoint = false;
        ResetGaze();
        homeHops = Hops(fromNode);    // it settles where it ran to: that is its new patrol area
    }

    void ResetGaze()
    {
        gazeTimer = 0f;
        currentLookStartTime = 0f;
        lastQualifiedLookTime = -999f;
        hasQualifiedLook = false;
        currentLookCounted = false;
        currentLookTriggeredAttack = false;
        wasLooking = false;
    }

    // A single real look prompts a small evasive move. A sustained stare or a second real look makes it strike instead.
    void UpdateGaze(float dt)
    {
        bool looked = gm.IsPlayerLookingAt(bot.EyePosition, gm.FlatheadGazeAngle, gm.SightRange);
        float attackLookTime = Mathf.Max(2f, gm.FlatheadGazeTime);

        if (looked)
        {
            if (!wasLooking) currentLookStartTime = Time.time;
            gazeTimer += dt;
            if (gazeTimer >= attackLookTime)
            {
                hasQualifiedLook = false;
                if (!currentLookTriggeredAttack)
                {
                    currentLookTriggeredAttack = true;
                    if (mode != Mode.Strike) BeginStrike();
                }
            }
            else if (!currentLookCounted && gazeTimer >= GlanceIgnoreTime)
            {
                currentLookCounted = true;
                bool secondLook = hasQualifiedLook && currentLookStartTime - lastQualifiedLookTime <= DoubleLookWindow;
                if (secondLook)
                {
                    hasQualifiedLook = false;
                    currentLookTriggeredAttack = true;
                    if (mode != Mode.Strike) BeginStrike();
                }
                else
                {
                    hasQualifiedLook = true;
                    lastQualifiedLookTime = currentLookStartTime;
                }
            }

            wasLooking = true;
            return;
        }

        if (wasLooking && gazeTimer >= GlanceIgnoreTime && !currentLookTriggeredAttack && mode != Mode.Dodge)
            BeginDodge();

        if (hasQualifiedLook && Time.time - lastQualifiedLookTime > DoubleLookWindow)
            hasQualifiedLook = false;

        gazeTimer = 0f;
        currentLookCounted = false;
        currentLookTriggeredAttack = false;
        wasLooking = false;
    }

    void BeginDodge()
    {
        if (mode == Mode.Dodge) return;

        mode = Mode.Dodge;
        hasStopPoint = false;
        blockedTime = 0f;
        backoffTimer = 0f;
        dodgeStartTime = Time.time;
        goalNode = PickDodgeNode();
    }

    void EndDodge()
    {
        mode = Mode.Stalk;
        hasStopPoint = false;
        goalNode = -1;
        blindTime = 0f;
        strikeLostTime = 0f;
    }

    // Pick a nearby track point that increases its distance from the player, keeping the dodge short.
    int PickDodgeNode()
    {
        Transform p = gm.Player;
        if (p == null) return fromNode;

        float currentDistance = Vector3.Distance(owner.position, p.position);
        float bestScore = float.MinValue;
        int best = -1;
        for (int n = 0; n < NodeTotal; n++)
        {
            float pathCost = CostToNode(n);
            if (pathCost < 0.5f || pathCost > DodgeMaxPath) continue;

            float awayGain = Vector3.Distance(NodePos(n), p.position) - currentDistance;
            if (awayGain <= 0.1f) continue;

            float score = awayGain - pathCost * 0.2f;
            if (gm.FlatheadNodeCrowded(n, bot)) score -= 2f;
            if (score > bestScore) { bestScore = score; best = n; }
        }

        return best >= 0 ? best : PickFleeNode();
    }

    // the waypoint to run to: far from the player, not too far to run, not crowded
    int PickFleeNode()
    {
        Transform p = gm.Player;
        Vector3 from = p != null ? p.position : owner.position;
        int best = -1, farthest = fromNode;
        float bestScore = float.MinValue, farthestDist = float.MinValue;
        for (int n = 0; n < NodeTotal; n++)
        {
            Vector3 np = NodePos(n);
            float away = Vector3.Distance(np, from);
            if (away > farthestDist) { farthestDist = away; farthest = n; }
            if (Vector3.Distance(np, owner.position) < FleeMinRun) continue;   // that is not running away

            float score = away - 0.35f * CostToNode(n);
            if (gm.FlatheadNodeCrowded(n, bot)) score -= 4f;
            if (score > bestScore) { bestScore = score; best = n; }
        }
        return best >= 0 ? best : farthest;
    }

    // Runs every tick, after the awareness: the transitions of the sneak attack.
    void UpdateMode(float dt)
    {
        if (mode == Mode.Patrol) return;

        if (mode == Mode.Flee)
        {
            bool arrived = !onJump && toNode < 0 && fromNode == goalNode;
            if (arrived || Time.time > fleeStartTime + FleeTimeout) EndFlee();
            return;
        }

        Transform p = gm.Player;
        if (p == null) return;

        UpdateGaze(dt);

        if (mode == Mode.Dodge)
        {
            bool arrived = !onJump && toNode < 0 && fromNode == goalNode;
            if (arrived || Time.time >= dodgeStartTime + DodgeTimeout) EndDodge();
            return;
        }

        float dist = Vector3.Distance(owner.position, p.position);

        if (mode == Mode.Stalk)
        {
            bool atClosestPoint = hasStopPoint && Vector3.Distance(owner.position, stopPoint) <= ArriveEps;

            // Arrived as close as the track lets it get and STILL no clear shot (an obstacle in the way, or the player hides behind cover): after
            // 'Lose Sight Time' it gives up and lies low for a while, so the next Flathead can try its luck instead of this one blocking the slot.
            // (A long walk toward him doesn't count: only the time spent standing at the closest point.)
            if (!onJump && atClosestPoint && !clearShot) blindTime += dt; else blindTime = 0f;
            if (blindTime > gm.LoseSightTime)
            {
                mode = Mode.Patrol;
                hasStopPoint = false;
                ResetGaze();
                cooldownUntil = Time.time + gm.LoseSightTime;
                return;
            }

            // close enough (or as close as the track lets it get) and a clear shot: strike. Never in mid-air.
            if (onJump || !seesPlayer || !clearShot) return;
            bool closeEnough = dist <= gm.FlatheadStrikeDistance;
            if (closeEnough || atClosestPoint) BeginStrike();
            return;
        }

        // STRIKE: the player slipped out of sight or backed off a good way: creep after him again
        if (clearShot) strikeLostTime = 0f; else strikeLostTime += dt;
        if (strikeLostTime > StrikeLostLimit || dist > strikeStartDist + StrikeLeash) { mode = Mode.Stalk; return; }

        // burst fired: hit and run
        if (BurstDone && Time.time >= lastShotTime + ShotsToFleeDelay) BeginFlee();
    }

    // creeping while stalking, scurrying while dodging or fleeing. The feet are matched to the speed (WalkSpeed follows the steps per second), so they never slide.
    float SpeedTarget()
    {
        if (mode == Mode.Stalk) return gm.FlatheadSneakSpeed;
        if (mode == Mode.Flee || mode == Mode.Dodge) return gm.FlatheadFleeSpeed;
        return 1f;
    }

    void UpdateSpeed(float dt)
    {
        speedMult = Mathf.MoveTowards(speedMult, SpeedTarget(), dt * 4f);
        bot.Ctrl.stepsPerSecond = gm.FlatheadStepsPerSecond * speedMult;
    }

    // ------------------------------------------------------------------ chase target

    Vector3 StopPos() { return Vector3.Lerp(NodePos(stopA), NodePos(stopB), stopT); }
    bool StopIsInterior { get { return hasStopPoint && stopT > 0.0005f && stopT < 0.9995f; } }

    // path length from where I am (standing on fromNode, or somewhere on the connection fromNode-toNode) to a waypoint
    float CostToNode(int n)
    {
        if (toNode >= 0)
        {
            Vector3 pos = owner.position;
            float viaTo = Vector3.Distance(pos, NodePos(toNode)) + PathDist(toNode, n);
            float viaFrom = Vector3.Distance(pos, NodePos(fromNode)) + PathDist(fromNode, n);
            return Mathf.Min(viaTo, viaFrom);
        }
        return PathDist(fromNode, n);
    }

    void UpdateChaseGoal(Transform player)
    {
        if (player == null) return;

        int a, b; float t; Vector3 pt;
        if (!ClosestStandablePoint(player.position, out a, out b, out t, out pt)) return;

        if (hasStopPoint) stopPoint = StopPos();   // follows waypoints that move while playing
        if (!hasStopPoint ||
            Vector3.Distance(player.position, pt) + StopPointHysteresis < Vector3.Distance(player.position, stopPoint))
        {
            hasStopPoint = true;
            stopA = a; stopB = b; stopT = t; stopPoint = pt;
        }

        // stop point exactly on a waypoint: that waypoint is the goal
        if (!StopIsInterior)
        {
            goalNode = stopT <= 0.5f ? stopA : stopB;
            return;
        }

        // stop point in the middle of a walk connection: goal = the end that is cheaper to reach, the last bit is walked along the connection
        float costA = CostToNode(stopA) + Vector3.Distance(NodePos(stopA), stopPoint);
        float costB = CostToNode(stopB) + Vector3.Distance(NodePos(stopB), stopPoint);
        int best = costA <= costB ? stopA : stopB;
        if (goalNode != stopA && goalNode != stopB)
        {
            goalNode = best;
        }
        else if (best != goalNode)
        {
            float cur = goalNode == stopA ? costA : costB;
            float cand = best == stopA ? costA : costB;
            if (cand + GoalHysteresis < cur) goalNode = best;
        }
    }

    // true while I walk on the connection that holds the stop point
    bool IsOnStopEdge()
    {
        if (mode != Mode.Stalk || !StopIsInterior || toNode < 0) return false;
        return (fromNode == stopA && toNode == stopB) || (fromNode == stopB && toNode == stopA);
    }

    // standing on one end of the stop connection: the waypoint at the other end (walk along it to the stop point), or -1 if the stop point is right here
    int FinalLegTarget()
    {
        if (!StopIsInterior) return -1;
        int other = fromNode == stopA ? stopB : (fromNode == stopB ? stopA : -1);
        if (other < 0) return -1;
        if (Vector3.Distance(NodePos(fromNode), stopPoint) <= ArriveEps) return -1;
        return other;
    }

    // ------------------------------------------------------------------ choosing the next waypoint

    // standing on fromNode: which waypoint do we go to next? (-1 = stay)
    int ChooseNext()
    {
        if (Directed)
        {
            if (goalNode < 0) return -1;
            if (goalNode == fromNode) return FinalLegTarget();
            return NextHop(fromNode, goalNode);
        }

        // patrol: random walk over all connections (walking and jumping)
        tmp.Clear();
        List<FlatheadLink> ls = Links(fromNode);
        for (int i = 0; i < ls.Count; i++) tmp.Add(ls[i].node);
        if (tmp.Count == 0) return -1;

        // 1) stay in my patrol zone (a few waypoints around my home); outside of it head back toward home
        tmp2.Clear();
        for (int i = 0; i < tmp.Count; i++)
            if (homeHops[tmp[i]] <= gm.FlatheadPatrolZone) tmp2.Add(tmp[i]);
        if (tmp2.Count == 0)
        {
            int bestHops = int.MaxValue;
            for (int i = 0; i < tmp.Count; i++) bestHops = Mathf.Min(bestHops, homeHops[tmp[i]]);
            for (int i = 0; i < tmp.Count; i++)
                if (homeHops[tmp[i]] == bestHops) tmp2.Add(tmp[i]);
        }

        // 2) don't go to a waypoint where another robot stands, if there is another way
        if (tmp2.Count > 1)
        {
            tmp.Clear();
            for (int i = 0; i < tmp2.Count; i++)
                if (!gm.FlatheadNodeCrowded(tmp2[i], bot)) tmp.Add(tmp2[i]);
            if (tmp.Count > 0) { tmp2.Clear(); tmp2.AddRange(tmp); }
        }

        // 3) no turning back unless it's the only way
        if (tmp2.Count > 1) tmp2.Remove(prevNode);
        return tmp2[Random.Range(0, tmp2.Count)];
    }

    // only for walk connections (a jump can't be turned around in the air)
    void Reverse()
    {
        int t = fromNode; fromNode = toNode; toNode = t;
        if (mode == Mode.Stalk) backoffTimer = 1.5f;
    }

    // ------------------------------------------------------------------ legs

    void LegsOff(float dt)
    {
        bot.Ctrl.walking = false;
        moveW = 0f;
        notWalkingTime += dt;
    }

    bool Standing(float dt) { LegsOff(dt); return false; }

    // Sets the walk direction of the legs (angle in the ROBOT's own frame: 0 = forward, 90 = right ...) from the real direction of travel.
    // Returns false while the legs are going back to rest because the direction changed a lot (the body waits).
    bool PrepareWalk(Vector3 dirWorld, float dt)
    {
        FlatheadController c = bot.Ctrl;
        float want = bot.LocalWalkAngle(dirWorld);

        if (!angleValid || notWalkingTime > 0.3f) { appliedAngle = want; angleValid = true; walkPause = 0f; }
        else if (walkPause <= 0f && Mathf.Abs(Mathf.DeltaAngle(want, appliedAngle)) > DirectionTurn) walkPause = DirectionPause;

        if (walkPause > 0f)
        {
            walkPause -= dt;
            if (walkPause <= 0f) appliedAngle = want;    // legs are at rest again: take the new direction
            LegsOff(dt);
            return false;
        }

        c.walkDirection = Mathf.Repeat(appliedAngle, 360f);
        c.walking = true;
        notWalkingTime = 0f;
        return true;
    }

    // ------------------------------------------------------------------ jumping

    // Called when the next waypoint is on a JUMP connection. Returns false if the landing spot is taken (the robot waits).
    bool TryBeginJump(float dt)
    {
        Vector3 landing = NodePos(toNode);
        if (gm.FlatheadSpotTaken(landing, bot))
        {
            toNode = -1;
            blockedTime += dt;
            if (blockedTime >= blockLimit)
            {
                blockedTime = 0f;
                blockLimit = Random.Range(2f, 4f);
                backoffTimer = Random.Range(1f, 2.5f);
            }
            return false;
        }
        blockedTime = 0f;
        bot.ReserveLanding(landing);   // nobody else may stand on, walk to or land on this spot until this robot has landed

        FlatheadController c = bot.Ctrl;
        Vector3 up, fwd;
        NodeFrame(toNode, out up, out fwd);

        jumpFrom = owner.position;
        jumpTo = landing;
        jumpRotFrom = owner.rotation;
        jumpRotTo = bot.PoseRotation(fwd, up);

        // the jump arcs out into the room: away from both the surface it leaves and the surface it lands on
        Vector3 bulge = owner.up + up;
        if (bulge.sqrMagnitude < 0.01f) bulge = owner.up;
        jumpBulge = bulge.normalized;

        // jump length decides how long it takes (the controller's jump = 22% crouch, 56% in the air, 22% landing)
        float len = Vector3.Distance(jumpFrom, jumpTo);
        float airTime = len / Mathf.Max(0.5f, gm.FlatheadJumpSpeed * SpeedTarget());
        c.jumpDuration = Mathf.Clamp(airTime / 0.56f, 0.6f, 2f);
        c.walking = false;
        c.jump = true;
        moveW = 0f;
        notWalkingTime = 0f;

        onJump = true;
        jumpStarted = false;
        jumpWait = 0f;
        return true;
    }

    // Moves the robot through the air while the controller plays the jump (crouch, launch, tuck, land).
    bool UpdateJump(float dt)
    {
        FlatheadController c = bot.Ctrl;

        if (!jumpStarted)
        {
            if (c.IsJumping) { jumpStarted = true; c.jump = false; }    // started: don't jump again after landing
            else
            {
                jumpWait += dt;
                if (jumpWait > 0.5f) FinishJump();                      // controller never started the jump: don't get stuck, just land
                return false;
            }
        }

        if (!c.IsJumping) { FinishJump(); return false; }

        float u = c.JumpProgress;
        float k = Mathf.Clamp01((u - 0.22f) / 0.56f);                  // 0 while crouching, 0..1 in the air, 1 while landing
        float s = k * k * (3f - 2f * k);
        Vector3 pos = Vector3.Lerp(jumpFrom, jumpTo, s) + jumpBulge * (gm.FlatheadJumpArc * 4f * k * (1f - k));
        owner.SetPositionAndRotation(pos, Quaternion.Slerp(jumpRotFrom, jumpRotTo, s));
        return false;
    }

    void FinishJump()
    {
        bot.Ctrl.jump = false;
        bot.ClearLanding();
        owner.SetPositionAndRotation(jumpTo, jumpRotTo);
        onJump = false;
        prevNode = fromNode;
        fromNode = toNode;
        toNode = -1;
    }

    // ------------------------------------------------------------------ walking

    bool Think(float dt)
    {
        if (backoffTimer > 0f) backoffTimer -= dt;
        Transform player = gm.Player;

        if (mode == Mode.Stalk) UpdateChaseGoal(player);

        // striking: it stands still and fires (the FlatheadEnemy does the shooting) until the player looks at it, backs off or its burst is done
        if (mode == Mode.Strike) return Standing(dt);

        if (mode == Mode.Stalk && player != null)
        {
            // 1) 1 m from the player: stop
            if (Vector3.Distance(owner.position, player.position) <= CloseDistance) return Standing(dt);

            // 2) at the closest standable point of the track to the player: stop (UpdateMode turns this into a strike)
            if (hasStopPoint && Vector3.Distance(owner.position, stopPoint) <= ArriveEps) return Standing(dt);
        }

        // standing on a waypoint -> decide where to go
        if (toNode < 0)
        {
            if (backoffTimer > 0f) return Standing(dt);
            toNode = ChooseNext();
            if (toNode < 0) return Standing(dt);

            if (EdgeKindOf(fromNode, toNode) == FlatheadEdgeKind.Jump)
            {
                if (!TryBeginJump(dt)) return Standing(dt);   // landing spot taken: wait
                return false;                                  // UpdateJump takes over next frame
            }
        }

        Vector3 pos = owner.position;

        // chase, on the connection that holds the stop point: walk straight to the stop point, whichever way it lies
        bool onStopEdge = IsOnStopEdge();
        if (onStopEdge)
        {
            Vector3 ed = NodePos(toNode) - NodePos(fromNode);
            if (ed.sqrMagnitude > 1e-8f && Vector3.Dot(stopPoint - pos, ed) < 0f)
            {
                int sw = fromNode; fromNode = toNode; toNode = sw;   // the stop point is behind me: turn around
            }
        }
        else if (Directed && goalNode >= 0 && backoffTimer <= 0f)
        {
            // stalking / fleeing: if the goal is now behind us, turn around in the middle of the connection
            float fwdCost = Vector3.Distance(pos, NodePos(toNode)) + PathDist(toNode, goalNode);
            float backCost = Vector3.Distance(pos, NodePos(fromNode)) + PathDist(fromNode, goalNode);
            if (backCost + ReverseMargin < fwdCost) Reverse();
        }

        Vector3 tp = onStopEdge ? stopPoint : NodePos(toNode);
        Vector3 dir = tp - pos;
        float distToTarget = dir.magnitude;
        Vector3 dirN = distToTarget > 1e-5f ? dir / distToTarget : Vector3.zero;

        // the robot never turns: the legs just step toward the direction of travel (Walk Direction of the controller)
        if (distToTarget > 1e-5f && !PrepareWalk(dirN, dt)) return false;

        moveW = Mathf.MoveTowards(moveW, 1f, dt / 0.25f);
        float step = bot.WalkSpeed * moveW * dt;       // feet are matched to this speed, they don't slide
        bool arrives = distToTarget <= step;
        Vector3 newPos = arrives ? tp : pos + dirN * step;

        // never touch another robot: wait until it moves away
        if (gm.WouldCollideFlathead(bot, newPos))
        {
            blockedTime += dt;
            if (blockedTime >= blockLimit)
            {
                blockedTime = 0f;
                blockLimit = Random.Range(2f, 4f);
                if (!onStopEdge && mode != Mode.Flee) Reverse();   // head-on deadlock: back off (not while walking to the stop point: it just waits there; a fleeing robot never turns back toward the player)
            }
            return Standing(dt);
        }
        blockedTime = 0f;

        owner.position = newPos;
        if (arrives && !onStopEdge)
        {
            prevNode = fromNode;
            fromNode = toNode;
            toNode = -1;
        }
        // arrived on the stop point in the middle of a connection: it stays there (the stop check at the top keeps it standing)
        return true;
    }
}
