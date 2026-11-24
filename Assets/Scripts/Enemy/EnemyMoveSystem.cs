using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Waypoint movement for the IG-88 droids. NO NavMesh and no NavMeshAgent: patrol droids walk their assigned two-point segment;
/// stationary guards stay on their NEW1-NEW8 post.
///
/// This file holds two things:
///
///  1) ALL WAYPOINTS (static, one graph shared by every droid): G1-G6 / R1-R5 and NEW1-NEW8, the six patrol segments, shortest paths,
///     and the closest point on a patrol track. The GameManager hands in all 19 waypoint objects via SetWaypoints().
///
///  2) THE MOVEMENT + DECISIONS OF ONE DROID (one instance per droid, created by the IG88Controller). Every frame IG88Controller calls Tick():
///      - PATROL: walks back and forth along its assigned pair of waypoints.
///      - GUARD: waits at NEW1-NEW8 without patrolling, senses through walls inside the guard radius, then chases on the network and returns to its post.
///      - DETECTION: patrol droids first detect by sight; guards also detect within their short guard radius.
///      - CHASE: every droid can move anywhere along the connected track to the closest track point; guards return to their post after giving up.
///      - GIVE UP: not detected for 'Lose Sight Time' seconds -> patrol droids return to their assigned segment; guards stay at their post.
///      - Never touches another droid: it waits until the other droid moves away.
///    The droid body (animation, shooting, health, glow) stays in the IG88Controller; this class only decides WHERE the droid walks.
/// </summary>
internal sealed class EnemyMoveSystem
{
    // =====================================================================
    //  1) WAYPOINTS (shared by all droids)
    // =====================================================================

    public const int G1 = 0, G2 = 1, G3 = 2, G4 = 3, G5 = 4, G6 = 5, R1 = 6, R2 = 7, R3 = 8, R4 = 9, R5 = 10;
    public const int NEW1 = 11, NEW2 = 12, NEW3 = 13, NEW4 = 14, NEW5 = 15, NEW6 = 16, NEW7 = 17, NEW8 = 18;
    public static readonly string[] NodeNames = { "G1", "G2", "G3", "G4", "G5", "G6", "R1", "R2", "R3", "R4", "R5", "NEW1", "NEW2", "NEW3", "NEW4", "NEW5", "NEW6", "NEW7", "NEW8" };

    public struct Edge
    {
        public int a, b;
        public IG88PatrolRoute route;
        public IG88EdgeKind kind;
        public Edge(int a, int b, IG88PatrolRoute route, IG88EdgeKind kind) { this.a = a; this.b = b; this.route = route; this.kind = kind; }
    }

    // The first six are the only patrol routes. Extra links retain the full chase/return track but are chase-only.
    public static readonly Edge[] Edges =
    {
        new Edge(G6, G5, IG88PatrolRoute.G6ToG5, IG88EdgeKind.Green),
        new Edge(G5, G2, IG88PatrolRoute.G5ToG2, IG88EdgeKind.Green),
        new Edge(G2, G1, IG88PatrolRoute.G2ToG1, IG88EdgeKind.Green),
        new Edge(R5, R4, IG88PatrolRoute.R5ToR4, IG88EdgeKind.Red),
        new Edge(R4, R2, IG88PatrolRoute.R4ToR2, IG88EdgeKind.Red),
        new Edge(R2, R1, IG88PatrolRoute.R2ToR1, IG88EdgeKind.Red),
        new Edge(G5, G4, IG88PatrolRoute.None, IG88EdgeKind.Green),
        new Edge(G4, G3, IG88PatrolRoute.None, IG88EdgeKind.Green),
        new Edge(G3, G2, IG88PatrolRoute.None, IG88EdgeKind.Green),
        new Edge(R2, R3, IG88PatrolRoute.None, IG88EdgeKind.Red),
        new Edge(R3, G3, IG88PatrolRoute.None, IG88EdgeKind.Red),
        new Edge(G3, R4, IG88PatrolRoute.None, IG88EdgeKind.Red),
        new Edge(R5, G6, IG88PatrolRoute.None, IG88EdgeKind.Yellow),
        new Edge(R1, G1, IG88PatrolRoute.None, IG88EdgeKind.Yellow),
    };

    public static readonly IG88PatrolRoute[] PatrolRoutes =
    {
        IG88PatrolRoute.G6ToG5, IG88PatrolRoute.G5ToG2, IG88PatrolRoute.G2ToG1,
        IG88PatrolRoute.R5ToR4, IG88PatrolRoute.R4ToR2, IG88PatrolRoute.R2ToR1
    };

    // The scene image shows where every stationary post sits on the old movement network.
    // N1=R4-G3, N2=G3-G2, N3=G4-G5, N4=R2-R3, N5/N6=R1-G1, N7/N8=R5-G6.
    static readonly int[,] PostTrackEndpoints =
    {
        { R4, G3 }, { G3, G2 }, { G4, G5 }, { R2, R3 },
        { R1, G1 }, { R1, G1 }, { R5, G6 }, { R5, G6 }
    };
    static readonly bool[] postPlacementWarningLogged = new bool[8];

    // Former middle-room positions remain in the scene graph but are not part of the new patrol network.
    static readonly int[] NoPatrolNodes = { G3, G4, R3 };

    static Transform[] nodes;
    static List<Edge> graphEdges;
    static List<IG88Link>[] links;
    static float[,] dist;
    static int[,] next;
    static bool graphReady;

    /// <summary>True when all 19 waypoints are assigned and the graph is built.</summary>
    public static bool Ready { get { return graphReady; } }
    public static int NodeCount { get { return graphReady ? nodes.Length : 0; } }
    public static IEnumerable<Edge> Connections
    {
        get
        {
            if (graphEdges != null) return graphEdges;
            return Edges;
        }
    }

    /// <summary>
    /// Hands in all waypoint objects in this order: G1-G6, R1-R5, NEW1-NEW8.
    /// Returns false when one is missing ('missing' then lists the names). Builds the connections and the shortest paths.
    /// </summary>
    public static bool SetWaypoints(Transform[] waypoints, List<string> missing)
    {
        missing.Clear();
        for (int i = 0; i < NodeNames.Length; i++)
            if (waypoints == null || i >= waypoints.Length || waypoints[i] == null) missing.Add(NodeNames[i]);

        graphReady = missing.Count == 0;
        if (!graphReady) return false;

        nodes = waypoints;
        for (int i = 0; i < postPlacementWarningLogged.Length; i++) postPlacementWarningLogged[i] = false;
        dist = new float[nodes.Length, nodes.Length];
        next = new int[nodes.Length, nodes.Length];
        RebuildDistances();
        return true;
    }

    // Rebuild the chase graph (including the explicit NEW post segments), then its shortest paths.
    public static void RebuildDistances()
    {
        if (!graphReady) return;
        RebuildGraphEdges();
        links = new List<IG88Link>[nodes.Length];
        for (int i = 0; i < links.Length; i++) links[i] = new List<IG88Link>();
        for (int i = 0; i < graphEdges.Count; i++)
        {
            Edge e = graphEdges[i];
            links[e.a].Add(new IG88Link { node = e.b, route = e.route, kind = e.kind });
            links[e.b].Add(new IG88Link { node = e.a, route = e.route, kind = e.kind });
        }

        int n = nodes.Length;
        const float INF = 1e9f;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                dist[i, j] = i == j ? 0f : INF;
                next[i, j] = i == j ? i : -1;
            }

        for (int i = 0; i < graphEdges.Count; i++)
        {
            Edge e = graphEdges[i];
            float w = Vector3.Distance(nodes[e.a].position, nodes[e.b].position);
            dist[e.a, e.b] = dist[e.b, e.a] = w;
            next[e.a, e.b] = e.b;
            next[e.b, e.a] = e.a;
        }

        for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (dist[i, k] + dist[k, j] < dist[i, j])
                    {
                        dist[i, j] = dist[i, k] + dist[k, j];
                        next[i, j] = next[i, k];
                    }
    }

    static void RebuildGraphEdges()
    {
        graphEdges = new List<Edge>(Edges.Length + 16);
        graphEdges.AddRange(Edges);

        // Split each affected track segment into consecutive links through its posts. The post-to-post links stay on the
        // established corridor rather than cutting directly between endpoints; they are chase-only, not patrol routes.
        int[] splitNodes = new int[10];
        float[] splitT = new float[10];
        for (int edgeIndex = 0; edgeIndex < Edges.Length; edgeIndex++)
        {
            Edge edge = Edges[edgeIndex];
            Vector3 a = nodes[edge.a].position;
            Vector3 ab = nodes[edge.b].position - a;
            float lengthSquared = ab.sqrMagnitude;
            int splitCount = 1;
            splitNodes[0] = edge.a;
            splitT[0] = 0f;

            for (int postIndex = 0; postIndex < 8; postIndex++)
            {
                int endpointA = PostTrackEndpoints[postIndex, 0];
                int endpointB = PostTrackEndpoints[postIndex, 1];
                bool belongsToEdge = (edge.a == endpointA && edge.b == endpointB) ||
                                     (edge.a == endpointB && edge.b == endpointA);
                if (!belongsToEdge) continue;

                int post = NEW1 + postIndex;
                float rawT = lengthSquared > 1e-8f
                    ? Vector3.Dot(nodes[post].position - a, ab) / lengthSquared
                    : 0f;
                float t = Mathf.Clamp01(rawT);
                float offSegment = Vector3.Distance(nodes[post].position, a + ab * t);
                if ((offSegment > 0.5f || rawT < 0f || rawT > 1f) && !postPlacementWarningLogged[postIndex])
                {
                    Debug.LogWarning("IG-88 post " + NodeNames[post] + " is not on its assigned network segment " +
                                     NodeNames[endpointA] + "-" + NodeNames[endpointB] +
                                     ". Move it onto that line so chase and return movement stays on the network.", nodes[post]);
                    postPlacementWarningLogged[postIndex] = true;
                }
                splitNodes[splitCount] = post;
                splitT[splitCount] = t;
                splitCount++;
            }

            if (splitCount == 1) continue;
            splitNodes[splitCount] = edge.b;
            splitT[splitCount] = 1f;
            splitCount++;

            // At most two posts share a segment in this layout; insertion sort keeps their order along that segment.
            for (int i = 1; i < splitCount; i++)
            {
                int node = splitNodes[i];
                float t = splitT[i];
                int j = i - 1;
                while (j >= 0 && splitT[j] > t)
                {
                    splitNodes[j + 1] = splitNodes[j];
                    splitT[j + 1] = splitT[j];
                    j--;
                }
                splitNodes[j + 1] = node;
                splitT[j + 1] = t;
            }

            for (int i = 0; i < splitCount - 1; i++)
                graphEdges.Add(new Edge(splitNodes[i], splitNodes[i + 1], IG88PatrolRoute.None, IG88EdgeKind.Yellow));
        }
    }

    public static Vector3 NodePos(int i) { return nodes[i].position; }
    public static float PathDist(int a, int b) { return dist[a, b]; }
    public static int NextHop(int from, int goal) { return next[from, goal]; }
    public static List<IG88Link> Links(int node) { return links[node]; }
    static readonly int[] G6ToG5Nodes = { G6, G5 };
    static readonly int[] G5ToG2Nodes = { G5, G2 };
    static readonly int[] G2ToG1Nodes = { G2, G1 };
    static readonly int[] R5ToR4Nodes = { R5, R4 };
    static readonly int[] R4ToR2Nodes = { R4, R2 };
    static readonly int[] R2ToR1Nodes = { R2, R1 };
    static readonly int[] NoRouteNodes = new int[0];

    public static int[] RouteNodes(IG88PatrolRoute route)
    {
        switch (route)
        {
            case IG88PatrolRoute.G6ToG5: return G6ToG5Nodes;
            case IG88PatrolRoute.G5ToG2: return G5ToG2Nodes;
            case IG88PatrolRoute.G2ToG1: return G2ToG1Nodes;
            case IG88PatrolRoute.R5ToR4: return R5ToR4Nodes;
            case IG88PatrolRoute.R4ToR2: return R4ToR2Nodes;
            case IG88PatrolRoute.R2ToR1: return R2ToR1Nodes;
            default: return NoRouteNodes;
        }
    }

    /// <summary>True for waypoints where droids may not spawn or patrol (they are chase-only).</summary>
    public static bool IsNoPatrolNode(int node)
    {
        for (int i = 0; i < NoPatrolNodes.Length; i++)
            if (NoPatrolNodes[i] == node) return true;
        return false;
    }

    /// <summary>True if a connection is the droid's assigned patrol segment.</summary>
    public static bool PatrolEdgeMatches(IG88PatrolRoute route, IG88PatrolRoute edgeRoute)
    {
        return route != IG88PatrolRoute.None && route == edgeRoute;
    }

    /// <summary>True if the waypoint belongs to the droid's assigned patrol segment.</summary>
    public static bool NodeOnPatrolNetwork(int node, IG88PatrolRoute route)
    {
        List<IG88Link> ls = links[node];
        for (int i = 0; i < ls.Count; i++)
            if (PatrolEdgeMatches(route, ls[i].route)) return true;
        return false;
    }

    public static IG88PatrolRoute EdgeRouteOf(int a, int b)
    {
        // Use index-based loop to avoid heap-allocating an enumerator over a struct array.
        for (int i = 0; i < graphEdges.Count; i++)
        {
            Edge e = graphEdges[i];
            if ((e.a == a && e.b == b) || (e.a == b && e.b == a)) return e.route;
        }
        return IG88PatrolRoute.None;
    }

    public static int NearestNode(Vector3 p)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < nodes.Length; i++)
        {
            float d = (nodes[i].position - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// The closest point on the chase track to a world position.
    /// The point lies on the connection a-b, t = 0 is at node a, t = 1 is at node b.
    /// </summary>
    public static bool ClosestTrackPoint(Vector3 p, out int a, out int b, out float t, out Vector3 point)
    {
        a = -1; b = -1; t = 0f; point = p;
        if (!graphReady) return false;

        float best = float.MaxValue;
        float bestEdgeLengthSquared = float.MaxValue;
        for (int i = 0; i < graphEdges.Count; i++)
        {
            Edge edge = graphEdges[i];
            Vector3 pa = nodes[edge.a].position;
            Vector3 pb = nodes[edge.b].position;
            Vector3 ab = pb - pa;
            float len2 = ab.sqrMagnitude;
            float tt = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - pa, ab) / len2) : 0f;
            Vector3 q = pa + ab * tt;
            float d = (q - p).sqrMagnitude;
            // When the original full edge and its split post segments overlap, prefer the shorter segment for chase goals.
            // That lets a guard stop/chase along the local piece of track near its post instead of detouring to a far endpoint.
            bool closer = d < best - 0.000001f;
            bool sameDistanceButShorter = Mathf.Abs(d - best) <= 0.000001f && len2 < bestEdgeLengthSquared;
            if (closer || sameDistanceButShorter)
            {
                best = d;
                bestEdgeLengthSquared = len2;
                a = edge.a; b = edge.b; t = tt; point = q;
            }
        }
        return a >= 0;
    }

    // Reusable queue avoids allocating a new one per droid spawn.
    static readonly Queue<int> hopQueue = new Queue<int>();

    /// <summary>Number of connections from 'start' along one patrol segment.</summary>
    public static int[] RouteHops(IG88PatrolRoute route, int start)
    {
        int[] hops = new int[nodes.Length];
        for (int i = 0; i < hops.Length; i++) hops[i] = 999;
        hopQueue.Clear();
        hops[start] = 0;
        hopQueue.Enqueue(start);
        while (hopQueue.Count > 0)
        {
            int n = hopQueue.Dequeue();
            List<IG88Link> ls = links[n];
            for (int i = 0; i < ls.Count; i++)
            {
                bool ok = PatrolEdgeMatches(route, ls[i].route);
                if (!ok || hops[ls[i].node] != 999) continue;
                hops[ls[i].node] = hops[n] + 1;
                hopQueue.Enqueue(ls[i].node);
            }
        }
        return hops;
    }

    // =====================================================================
    //  2) MOVEMENT + DECISIONS OF ONE DROID
    // =====================================================================

    enum Mode { Patrol, Chase }

    readonly IG88Controller droid;      // the body: eye / gun tip positions and the direction it faces
    readonly Transform owner;
    readonly GameManager gm;            // settings, the player, line-of-sight test, droid collision
    readonly IG88PatrolRoute route;
    readonly int[] homeHops;            // connections from my home waypoint, along my assigned patrol segment
    readonly bool standingGuard;
    readonly int homeNode;

    Mode mode = Mode.Patrol;

    int fromNode;           // node we are on / just left
    int toNode = -1;        // node we are walking to (-1 = standing on fromNode, need a new target)
    int prevNode = -1;      // node we came from (patrol: don't turn back unless dead end)
    int goalNode = -1;      // chase goal or "return to patrol" goal
    bool returning;         // walking back to the patrol network after a chase

    // chase stop point: the closest point on the waypoint track (any connection) to the player. A chasing droid walks there and stops.
    bool hasStopPoint;
    int stopA = -1, stopB = -1;   // the connection the stop point lies on
    float stopT;                  // 0 = at stopA, 1 = at stopB
    Vector3 stopPoint;

    float blockedTime;
    float blockLimit;
    float backoffTimer;

    float lastSightTime = -999f;   // last sighting (or close-range guard detection)
    bool hasSeenPlayer;            // normal patrol droids only use Detection Radius after their first sighting
    bool seesPlayer;               // this frame: player within Sight Range and a clear line of sight from the droid's head
    bool clearShot;                // this frame: seesPlayer AND the bolt path from the gun tip to the player is free

    static readonly List<int> tmp = new List<int>();
    static readonly List<int> tmp2 = new List<int>();

    const float ReverseMargin = 0.05f;
    const float GoalHysteresis = 0.75f;
    const float StopPointHysteresis = 0.25f;   // the stop point only jumps to another spot of the track if that is this much closer to the player
    const float ArriveEps = 0.02f;             // metres: this close to the stop point = arrived
    const float CloseDistance = 1f;            // chasing droids never come closer than this to the player

    /// <summary>True while the droid is in chase mode (it has detected the player and has not given up yet).</summary>
    public bool Chasing { get { return mode == Mode.Chase; } }
    /// <summary>This frame: the player is within the Sight Range and nothing blocks the line of sight from the droid's head.</summary>
    public bool SeesPlayer { get { return seesPlayer; } }
    /// <summary>This frame: the droid sees the player AND a bolt from its gun tip would hit him.</summary>
    public bool ClearShot { get { return clearShot; } }

    /// <summary>Puts the droid on its start waypoint. Guards stay there; patrol droids walk only their assigned segment.</summary>
    public EnemyMoveSystem(IG88Controller droid, GameManager gm, IG88PatrolRoute route, int startNode, bool standingGuard)
    {
        this.droid = droid;
        this.gm = gm;
        this.route = route;
        this.standingGuard = standingGuard;
        owner = droid.transform;
        homeNode = startNode;
        fromNode = startNode;
        blockLimit = Random.Range(2f, 4f);
        homeHops = standingGuard ? null : RouteHops(route, startNode);
        owner.position = NodePos(startNode);
    }

    /// <summary>
    /// One frame of detection + movement. Returns true when the droid actually moved (drives the walk animation).
    /// Droid Off / Droid Patrol Off from the GameManager are handled here.
    /// </summary>
    public bool Tick(float dt)
    {
        UpdateAwareness();
        if (gm.DroidPatrolOff) return false;   // Droid Patrol Off = stand still, do nothing
        if (standingGuard && mode == Mode.Patrol && !returning) return false; // sentries stand at their post unless chasing or returning
        return Think(dt);
    }

    // ------------------------------------------------------------------ detection

    void UpdateAwareness()
    {
        Transform p = gm.Player;

        // Droid Off or Droid Patrol Off: never chase / target / shoot the player
        bool canChase = !gm.DroidOff && !gm.DroidPatrolOff && p != null;

        // DETECTION (is the droid aware of the player right now?)
        //  - Patrol droids first detect by sight and only use Detection Radius for a short time after sight.
        //  - A stationary guard also detects the player through walls within its own small guard radius, even before seeing him.
        // 'seesPlayer' / 'clearShot' are worked out once per frame and are ALSO what the stopping and shooting use.
        seesPlayer = false;
        clearShot = false;
        bool aware = false;
        if (canChase)
        {
            float d = Vector3.Distance(owner.position, p.position);
            seesPlayer = d < gm.SightRange && gm.HasClearShot(droid.EyePosition, gm.PlayerAimPoint);
            if (seesPlayer)
            {
                hasSeenPlayer = true;
                lastSightTime = Time.time;
                // clear shot = it sees him AND the bolt would hit him (free path from the gun tip, not only from the head)
                clearShot = droid.HasGun && gm.HasClearShot(droid.GunTipPosition, gm.PlayerAimPoint);
            }
            bool guardRadius = standingGuard && d < gm.StationaryGuardDetectionRadius;
            if (guardRadius)
            {
                hasSeenPlayer = true;
                lastSightTime = Time.time;    // keep the guard aware while the player remains within its short radius
            }
            bool radiusAllowed = hasSeenPlayer && Time.time <= lastSightTime + gm.LoseSightTime;
            aware = seesPlayer || guardRadius || (!standingGuard && radiusAllowed && d < gm.DetectionRadius);
        }
        else
        {
            hasSeenPlayer = false;
        }

        if (mode == Mode.Patrol && aware)
        {
            mode = Mode.Chase;
            returning = false;
            goalNode = -1;
            hasStopPoint = false;
            blockedTime = 0f;
            backoffTimer = 0f;
        }
        else if (mode == Mode.Chase && (!canChase || Time.time > lastSightTime + gm.LoseSightTime))
        {
            // player gone (or droids switched off) / not SEEN for 'Lose Sight Time' seconds: forget him, back to patrol.
            // From now on the Detection Radius is off again until the droid SEES him.
            mode = Mode.Patrol;
            hasSeenPlayer = false;
            hasStopPoint = false;
            if (standingGuard)
            {
                returning = fromNode != homeNode || toNode >= 0;
                goalNode = homeNode;
            }
            else BeginReturnToPatrol();
        }
    }

    // ------------------------------------------------------------------ patrol network / return to patrol

    // my patrol network = the one segment assigned to this droid
    bool Matches(IG88PatrolRoute edgeRoute)
    {
        return PatrolEdgeMatches(route, edgeRoute);
    }

    void BeginReturnToPatrol()
    {
        if (standingGuard)
        {
            returning = fromNode != homeNode || toNode >= 0;
            goalNode = homeNode;
            return;
        }

        if (toNode >= 0) returning = !Matches(EdgeRouteOf(fromNode, toNode));
        else returning = !NodeOnPatrolNetwork(fromNode, route);

        if (returning)
        {
            goalNode = NearestRouteNode();
            if (goalNode < 0) returning = false;
        }
        else if (toNode >= 0 && IsNoPatrolNode(toNode))
        {
            // chase ended while walking along my own route toward a blocked waypoint: turn around instead of walking on to it
            Reverse();
        }
    }

    // path length from where I am (standing on fromNode, or somewhere on the connection fromNode-toNode) to a node
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

    // nearest endpoint of my assigned patrol segment, measured along the connections
    int NearestRouteNode()
    {
        int count = NodeCount;
        int best = -1;
        float bestCost = float.MaxValue;
        for (int n = 0; n < count; n++)
        {
            if (IsNoPatrolNode(n)) continue;                  // never "return to patrol" onto a blocked waypoint
            if (!NodeOnPatrolNetwork(n, route)) continue;
            float cost = CostToNode(n);
            if (cost < bestCost) { bestCost = cost; best = n; }
        }
        return best;
    }

    // ------------------------------------------------------------------ chase target

    // Chase target = the closest point on the waypoint track to the player (it can lie in the middle of a connection).
    // 'goalNode' = the end of that connection that is cheaper to reach first; the last bit is walked along the connection itself.
    void UpdateChaseGoal(Transform player)
    {
        if (player == null) return;

        int a, b; float t; Vector3 pt;
        if (!ClosestTrackPoint(player.position, out a, out b, out t, out pt)) return;

        if (hasStopPoint) stopPoint = Vector3.Lerp(NodePos(stopA), NodePos(stopB), stopT);   // follows waypoints that move while playing
        if (!hasStopPoint ||
            Vector3.Distance(player.position, pt) + StopPointHysteresis < Vector3.Distance(player.position, stopPoint))
        {
            // the player moved: a new spot of the track is clearly closer to him (hysteresis stops flip-flopping between two spots)
            hasStopPoint = true;
            stopA = a; stopB = b; stopT = t; stopPoint = pt;
        }

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
        if (mode != Mode.Chase || !hasStopPoint || toNode < 0) return false;
        return (fromNode == stopA && toNode == stopB) || (fromNode == stopB && toNode == stopA);
    }

    // standing on one end of the stop connection: the node at its other end (walk along it to the stop point), or -1 if the stop point is right here
    int FinalLegTarget()
    {
        if (!hasStopPoint) return -1;
        int other = fromNode == stopA ? stopB : (fromNode == stopB ? stopA : -1);
        if (other < 0) return -1;
        if (Vector3.Distance(NodePos(fromNode), stopPoint) <= ArriveEps) return -1;
        return other;
    }

    // ------------------------------------------------------------------ choosing the next waypoint

    // standing on fromNode: which node do we walk to next? (-1 = stay)
    int ChooseNext()
    {
        if (mode == Mode.Chase)
        {
            if (goalNode < 0) return -1;
            if (goalNode == fromNode) return FinalLegTarget();   // on the end of the stop connection: walk along it to the stop point
            return NextHop(fromNode, goalNode);
        }
        if (returning)
        {
            if (goalNode < 0 || goalNode == fromNode) return -1;
            return NextHop(fromNode, goalNode);
        }

        if (standingGuard) return -1;

        // patrol: walk only the assigned route segment
        tmp.Clear();
        List<IG88Link> ls = Links(fromNode);
        for (int i = 0; i < ls.Count; i++)
            if (Matches(ls[i].route) && !IsNoPatrolNode(ls[i].node)) tmp.Add(ls[i].node);
        if (tmp.Count == 0) return -1;

        // 1) stay in my own patrol zone (a few waypoints around my home). Outside of it (e.g. after a chase) head back toward home.
        tmp2.Clear();
        for (int i = 0; i < tmp.Count; i++)
            if (homeHops[tmp[i]] <= gm.PatrolZoneSize) tmp2.Add(tmp[i]);
        if (tmp2.Count == 0)
        {
            int bestHops = int.MaxValue;
            for (int i = 0; i < tmp.Count; i++) bestHops = Mathf.Min(bestHops, homeHops[tmp[i]]);
            for (int i = 0; i < tmp.Count; i++)
                if (homeHops[tmp[i]] == bestHops) tmp2.Add(tmp[i]);
        }

        // 2) don't walk into a waypoint where another droid stands or waits, if there is another way
        if (tmp2.Count > 1)
        {
            tmp.Clear();
            for (int i = 0; i < tmp2.Count; i++)
                if (!gm.NodeCrowded(tmp2[i], droid)) tmp.Add(tmp2[i]);
            if (tmp.Count > 0) { tmp2.Clear(); tmp2.AddRange(tmp); }
        }

        // 3) no turning back unless it's a dead end
        if (tmp2.Count > 1) tmp2.Remove(prevNode);
        return tmp2[Random.Range(0, tmp2.Count)];
    }

    void Reverse()
    {
        int t = fromNode; fromNode = toNode; toNode = t;
        if (mode == Mode.Chase || returning) backoffTimer = 1.5f; // don't instantly turn around again
    }

    // ------------------------------------------------------------------ walking

    /// <summary>Robotic turn: rotates at a constant speed. Returns true when facing the direction.</summary>
    bool FaceDirection(Vector3 dir, float dt)
    {
        Vector3 up = owner.up;
        Vector3 flat = Vector3.ProjectOnPlane(dir, up);
        if (flat.sqrMagnitude < 1e-6f) return true;
        float ang = Vector3.SignedAngle(droid.RestForward, flat, up);
        float maxStep = gm.TurnSpeed * dt;
        float step = Mathf.Clamp(ang, -maxStep, maxStep);
        owner.rotation = Quaternion.AngleAxis(step, up) * owner.rotation;
        return Mathf.Abs(ang - step) < 4f;
    }

    // not walking: while chasing, keep facing the player
    bool Idle(float dt)
    {
        Transform p = gm.Player;
        if (mode == Mode.Chase && p != null) FaceDirection(p.position - owner.position, dt);
        return false;
    }

    /// <summary>Returns true when the droid actually moved this frame (drives the walk animation).</summary>
    bool Think(float dt)
    {
        if (backoffTimer > 0f) backoffTimer -= dt;
        Transform player = gm.Player;

        if (mode == Mode.Chase) UpdateChaseGoal(player);

        if (mode == Mode.Chase && player != null)
        {
            // 1) clear shot and the player stands still: stop and fire. The droid only walks again when the player moves.
            if (clearShot && !gm.PlayerMoving) return Idle(dt);

            Vector3 toPlayer = player.position - owner.position;

            // 2) 1 m from the player: stop and keep facing him
            if (Vector3.ProjectOnPlane(toPlayer, owner.up).magnitude <= CloseDistance)
            {
                FaceDirection(toPlayer, dt);
                return false;
            }

            // 3) at the closest point of the waypoint track to the player: stop and keep facing him
            if (hasStopPoint && Vector3.Distance(owner.position, stopPoint) <= ArriveEps) return Idle(dt);
        }

        // standing on a node -> decide where to go
        if (toNode < 0)
        {
            if (backoffTimer > 0f) return Idle(dt);
            if (returning && fromNode == goalNode) returning = false;
            toNode = ChooseNext();
            if (toNode < 0) return Idle(dt);
        }

        Vector3 pos = owner.position;

        // chase, on the connection that holds the stop point: walk straight to the stop point, in whichever direction it lies
        bool onStopEdge = IsOnStopEdge();
        if (onStopEdge)
        {
            Vector3 ed = NodePos(toNode) - NodePos(fromNode);
            if (ed.sqrMagnitude > 1e-8f && Vector3.Dot(stopPoint - pos, ed) < 0f)
            {
                int sw = fromNode; fromNode = toNode; toNode = sw;   // the stop point is behind me: turn around (no back-off pause)
            }
        }
        else if ((mode == Mode.Chase || returning) && goalNode >= 0 && backoffTimer <= 0f)
        {
            // chase / returning: if the goal is now behind us, turn around in the middle of the connection
            float fwd = Vector3.Distance(pos, NodePos(toNode)) + PathDist(toNode, goalNode);
            float back = Vector3.Distance(pos, NodePos(fromNode)) + PathDist(fromNode, goalNode);
            if (back + ReverseMargin < fwd) Reverse();
        }

        Vector3 tp = onStopEdge ? stopPoint : NodePos(toNode);
        Vector3 dir = tp - pos;
        float distToTarget = dir.magnitude;

        // robotic: first turn on the spot, then walk in a straight line
        if (distToTarget > 0.0001f && !FaceDirection(dir / distToTarget, dt)) return false;

        float step = gm.WalkSpeed * dt;   // same walk speed for patrol and chase
        bool arrives = distToTarget <= step;
        Vector3 newPos = arrives ? tp : pos + dir / distToTarget * step;

        // never touch another droid: wait until it moves away
        if (gm.WouldCollide(droid, newPos))
        {
            blockedTime += dt;
            if (blockedTime >= blockLimit)
            {
                // head-on / circular deadlock: back off (not while walking to the stop point: it just waits there)
                blockedTime = 0f;
                blockLimit = Random.Range(2f, 4f);
                if (!onStopEdge) Reverse();
            }
            return Idle(dt);
        }
        blockedTime = 0f;

        owner.position = newPos;
        if (arrives && !onStopEdge)
        {
            prevNode = fromNode;
            fromNode = toNode;
            toNode = -1;
        }
        // arrived on the stop point in the middle of its connection: stay there (the stop check at the top keeps the droid standing)
        return true;
    }
}
