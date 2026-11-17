using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

[DefaultExecutionOrder(-50)] // runs before most player scripts
public class GameManager : MonoBehaviour
{
    // One droid per waypoint is the physical maximum (11 waypoints, no two droids may share a spot).
    public const int MaxDroids = 11;

    [Header("Ammo")]
    [SerializeField] private int maxAmmo = 30;
    [SerializeField] private int currentAmmo = 30;          // live counter in Inspector
    [SerializeField] private bool infiniteAmmo = false;

    [Header("References (auto-finds if empty)")]
    [SerializeField] private BlasterWeapon blaster;
    [SerializeField] private VisorHud visorHud;
    [SerializeField] private WalkSprintRotationSway sway;
    [SerializeField] private BlasterRecoil recoil;

    [Header("3D Ammo Text")]
    [Tooltip("Drag your 3D TextMeshPro here")]
    [SerializeField] private TextMeshPro ammoText3D;

    [Header("Sway / Recoil Coordination")]
    [Tooltip("If true, GameManager forces a safe execution order so they don't fight over the same transforms.")]
    [SerializeField] private bool autoFixSwayRecoilConflict = true;

    // =====================================================================
    //  IG-88 DROIDS
    // =====================================================================

    [Header("IG-88 Master Switches (work live while playing)")]
    [Tooltip("Droids will NOT chase or target the player. They keep patrolling normally.")]
    [SerializeField] private bool droidOff = false;
    [Tooltip("Droids stand completely still and do nothing (no patrol, no chase, no aiming, light off). Overrides Droid Off.")]
    [SerializeField] private bool droidPatrolOff = false;

    [Header("IG-88 Spawning")]
    [Tooltip("Drag your IG88 prefab here (it must have the IG88Controller on its root).")]
    [SerializeField] private IG88Controller ig88Prefab;
    [Tooltip("How many droids to spawn. Max = 11 (one per waypoint). They alternate: green patrol, red patrol, green, red ...")]
    [Range(0, MaxDroids)]
    [SerializeField] private int droidCount = 4;

    [Header("IG-88 Detection / Chase")]
    [Tooltip("A droid starts chasing when the Player (tag 'Player') is closer than this. It stops when the player is farther away.")]
    [SerializeField] private float chaseRadius = 12f;
    [Tooltip("Firing distance: while chasing, a droid walks toward the player only until it is this close, then holds position and fires. " +
             "Keep it smaller than Chase Radius. If the player moves farther away, the droid moves closer again. 0 = walk all the way to the player.")]
    [Min(0f)]
    [SerializeField] private float firingDistance = 6f;
    [Tooltip("Point light inside each droid goes from 0 to this value while chasing.")]
    [SerializeField] private float chaseLightIntensity = 0.3f;

    [Header("IG-88 Speed")]
    [Tooltip("Metres per second while patrolling.")]
    [SerializeField] private float patrolSpeed = 1.0f;
    [Tooltip("Metres per second while chasing.")]
    [SerializeField] private float runSpeed = 3.5f;
    [Tooltip("Walk animation speed (full step cycles per second) while patrolling.")]
    [SerializeField] private float patrolStepsPerSecond = 1.1f;
    [Tooltip("Walk animation speed (full step cycles per second) while chasing.")]
    [SerializeField] private float runStepsPerSecond = 2.2f;
    [Tooltip("Degrees per second the droid turns on the spot at corners (robotic turning).")]
    [SerializeField] private float turnSpeed = 240f;

    [Header("IG-88 Collision")]
    [Tooltip("Droids never get closer than this to each other. A droid that would break this waits until the other one moves.")]
    [SerializeField] private float minDroidDistance = 1.5f;

    [Header("Waypoints - GREEN patrol (drag empty objects here)")]
    [SerializeField] private Transform g1;
    [SerializeField] private Transform g2;
    [SerializeField] private Transform g3;   // shared with the red patrol
    [SerializeField] private Transform g4;
    [SerializeField] private Transform g5;
    [SerializeField] private Transform g6;

    [Header("Waypoints - RED patrol (drag empty objects here)")]
    [SerializeField] private Transform r1;
    [SerializeField] private Transform r2;
    [SerializeField] private Transform r3;
    [SerializeField] private Transform r4;
    [SerializeField] private Transform r5;

    [Header("IG-88 Debug")]
    [SerializeField] private bool drawWaypointGizmos = true;

    // ----- graph definition (copied from dots.jpg) -----
    const int G1 = 0, G2 = 1, G3 = 2, G4 = 3, G5 = 4, G6 = 5, R1 = 6, R2 = 7, R3 = 8, R4 = 9, R5 = 10;
    static readonly string[] NodeNames = { "G1", "G2", "G3", "G4", "G5", "G6", "R1", "R2", "R3", "R4", "R5" };

    struct Edge
    {
        public int a, b;
        public IG88EdgeKind kind;
        public Edge(int a, int b, IG88EdgeKind kind) { this.a = a; this.b = b; this.kind = kind; }
    }

    static readonly Edge[] Edges =
    {
        // green (patrol 1)
        new Edge(G6, G5, IG88EdgeKind.Green),
        new Edge(G5, G4, IG88EdgeKind.Green),
        new Edge(G4, G3, IG88EdgeKind.Green),
        new Edge(G3, G2, IG88EdgeKind.Green),
        new Edge(G5, G2, IG88EdgeKind.Green),
        new Edge(G2, G1, IG88EdgeKind.Green),
        // red (patrol 2) - G3 is the shared point
        new Edge(R5, R4, IG88EdgeKind.Red),
        new Edge(R4, R2, IG88EdgeKind.Red),
        new Edge(R2, R1, IG88EdgeKind.Red),
        new Edge(R2, R3, IG88EdgeKind.Red),
        new Edge(R3, G3, IG88EdgeKind.Red),
        new Edge(G3, R4, IG88EdgeKind.Red),
        // yellow (chase only)
        new Edge(R5, G6, IG88EdgeKind.Yellow),
        new Edge(R1, G1, IG88EdgeKind.Yellow),
    };

    static readonly int[] GreenNodes = { G1, G2, G3, G4, G5, G6 };
    static readonly int[] RedNodes = { R1, R2, R3, R4, R5, G3 };

    Transform[] nodes;
    List<IG88Link>[] links;
    float[,] dist;
    int[,] next;
    bool graphReady;
    float nextGraphRebuild;

    readonly List<IG88Controller> droids = new List<IG88Controller>();
    Transform player;
    float nextPlayerSearch;

    // ---------- public read-only values used by the droids ----------
    public bool DroidOff => droidOff;
    public bool DroidPatrolOff => droidPatrolOff;
    public float ChaseRadius => chaseRadius;
    public float FiringDistance => firingDistance;
    public float ChaseLightIntensity => chaseLightIntensity;
    public float PatrolSpeed => patrolSpeed;
    public float RunSpeed => runSpeed;
    public float PatrolSteps => patrolStepsPerSecond;
    public float RunSteps => runStepsPerSecond;
    public float TurnSpeed => turnSpeed;
    public float MinDroidDistance => minDroidDistance;

    /// <summary>The object tagged "Player" (found automatically).</summary>
    public Transform Player
    {
        get
        {
            if (player == null && Time.unscaledTime >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.unscaledTime + 0.5f;
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) player = go.transform;
            }
            return player;
        }
    }

    private bool blasterWasEnabled;

    private void Awake()
    {
        // Auto-find missing refs
        if (blaster == null) blaster = FindFirstObjectByType<BlasterWeapon>();
        if (visorHud == null) visorHud = FindFirstObjectByType<VisorHud>();
        if (sway == null) sway = FindFirstObjectByType<WalkSprintRotationSway>();
        if (recoil == null) recoil = FindFirstObjectByType<BlasterRecoil>();

        // Soft fix for the transform conflict
        if (autoFixSwayRecoilConflict)
            FixSwayRecoilOrder();

        BuildGraph();
    }

    private void Start()
    {
        currentAmmo = Mathf.Clamp(currentAmmo, 0, maxAmmo);
        UpdateAmmoUI();

        if (blaster != null)
        {
            blaster.Fired += OnWeaponFired;
            blasterWasEnabled = blaster.enabled;
        }

        SpawnDroids();
    }

    private void OnDestroy()
    {
        if (blaster != null)
            blaster.Fired -= OnWeaponFired;
    }

    private void Update()
    {
        // Optional: also block firing when out of ammo by disabling the component temporarily
        if (blaster != null)
        {
            bool canShoot = infiniteAmmo || currentAmmo > 0;
            if (blaster.enabled != canShoot)
                blaster.enabled = canShoot;
        }

        // keep path lengths correct if you move waypoints while playing
        if (graphReady && Time.time >= nextGraphRebuild)
        {
            nextGraphRebuild = Time.time + 1f;
            RebuildDistances();
        }
    }

    private void OnWeaponFired(BlasterWeapon weapon)
    {
        if (infiniteAmmo) return;

        currentAmmo = Mathf.Max(0, currentAmmo - 1);
        UpdateAmmoUI();

        if (currentAmmo <= 0)
            Debug.Log("Out of ammo!");
    }

    private void UpdateAmmoUI()
    {
        // Visor HUD (0-1)
        if (visorHud != null)
        {
            float normalized = maxAmmo > 0 ? (float)currentAmmo / maxAmmo : 0f;
            visorHud.SetAmmo(normalized);
        }

        // 3D TextMeshPro
        if (ammoText3D != null)
        {
            ammoText3D.text = currentAmmo.ToString();
        }
    }

    // ---------- Public helpers you can call from other scripts ----------

    public void AddAmmo(int amount)
    {
        currentAmmo = Mathf.Clamp(currentAmmo + amount, 0, maxAmmo);
        UpdateAmmoUI();
    }

    public void SetAmmo(int amount)
    {
        currentAmmo = Mathf.Clamp(amount, 0, maxAmmo);
        UpdateAmmoUI();
    }

    public int CurrentAmmo => currentAmmo;
    public int MaxAmmo => maxAmmo;
    public bool HasAmmo => infiniteAmmo || currentAmmo > 0;

    // ---------- Soft conflict fix (no rewrite of the two scripts) ----------

    private void FixSwayRecoilOrder()
    {
        if (sway != null)
        {
            Debug.Log("[GameManager] Sway/Recoil coordination active. " +
                      "Sway should run before Recoil. If they still fight, " +
                      "put WalkSprintRotationSway on the root (PlayerObjects) and " +
                      "keep BlasterRecoil moving only the children — that is the intended setup.");
        }
    }

    // Quick reload for testing (press R)
    private void LateUpdate()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            currentAmmo = maxAmmo;
            UpdateAmmoUI();
            Debug.Log("Reloaded!");
        }
    }

    // =====================================================================
    //  IG-88: waypoint graph
    // =====================================================================

    private void BuildGraph()
    {
        nodes = new Transform[] { g1, g2, g3, g4, g5, g6, r1, r2, r3, r4, r5 };

        List<string> missing = new List<string>();
        for (int i = 0; i < nodes.Length; i++)
            if (nodes[i] == null) missing.Add(NodeNames[i]);

        graphReady = missing.Count == 0;
        if (!graphReady)
        {
            Debug.LogError("[GameManager] Waypoints not assigned: " + string.Join(", ", missing.ToArray()) +
                           ". No IG-88 droids will spawn until all 11 are dragged in.", this);
            return;
        }

        links = new List<IG88Link>[nodes.Length];
        for (int i = 0; i < links.Length; i++) links[i] = new List<IG88Link>();
        foreach (Edge e in Edges)
        {
            links[e.a].Add(new IG88Link { node = e.b, kind = e.kind });
            links[e.b].Add(new IG88Link { node = e.a, kind = e.kind });
        }

        dist = new float[nodes.Length, nodes.Length];
        next = new int[nodes.Length, nodes.Length];
        RebuildDistances();
    }

    // Floyd-Warshall over ALL connections (green + red + yellow) - used for chasing / returning to patrol
    private void RebuildDistances()
    {
        int n = nodes.Length;
        const float INF = 1e9f;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                dist[i, j] = i == j ? 0f : INF;
                next[i, j] = i == j ? i : -1;
            }

        foreach (Edge e in Edges)
        {
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

    public Vector3 NodePos(int i) { return nodes[i].position; }
    public float PathDist(int a, int b) { return dist[a, b]; }
    public int NextHop(int from, int goal) { return next[from, goal]; }
    public List<IG88Link> Links(int node) { return links[node]; }
    public int[] RouteNodes(IG88Route r) { return r == IG88Route.Green ? GreenNodes : RedNodes; }

    public bool NodeInRoute(int node, IG88Route r)
    {
        int[] list = RouteNodes(r);
        for (int i = 0; i < list.Length; i++) if (list[i] == node) return true;
        return false;
    }

    public IG88EdgeKind EdgeKindOf(int a, int b)
    {
        foreach (Edge e in Edges)
            if ((e.a == a && e.b == b) || (e.a == b && e.b == a)) return e.kind;
        return IG88EdgeKind.Yellow;
    }

    public int NearestNode(Vector3 p)
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

    // =====================================================================
    //  IG-88: spawning + collision
    // =====================================================================

    [ContextMenu("Respawn Droids")]
    public void SpawnDroids()
    {
        ClearDroids();
        if (ig88Prefab == null) { Debug.LogWarning("[GameManager] No IG88 prefab assigned - no droids spawned.", this); return; }
        if (!graphReady) return;

        int count = Mathf.Clamp(droidCount, 0, MaxDroids);
        for (int i = 0; i < count; i++)
        {
            IG88Route route = (i % 2 == 0) ? IG88Route.Green : IG88Route.Red;
            int node = PickFreeNode(route);
            if (node < 0)
            {
                route = (route == IG88Route.Green) ? IG88Route.Red : IG88Route.Green;
                node = PickFreeNode(route);
            }
            if (node < 0)
            {
                Debug.LogWarning("[GameManager] No free waypoint left (waypoints closer than 'Min Droid Distance'?). Spawned " + droids.Count + " droids.", this);
                break;
            }

            IG88Controller d = Instantiate(ig88Prefab, NodePos(node), ig88Prefab.transform.rotation);
            d.name = "IG88_" + (i + 1).ToString("00");
            droids.Add(d);
            d.Init(this, route, node);
        }
    }

    private void ClearDroids()
    {
        for (int i = droids.Count - 1; i >= 0; i--)
            if (droids[i] != null) Destroy(droids[i].gameObject);
        droids.Clear();
    }

    private int PickFreeNode(IG88Route route)
    {
        int[] list = RouteNodes(route);
        List<int> free = new List<int>();
        for (int i = 0; i < list.Length; i++)
        {
            Vector3 p = NodePos(list[i]);
            bool taken = false;
            for (int d = 0; d < droids.Count; d++)
                if (droids[d] != null && Vector3.Distance(droids[d].transform.position, p) < minDroidDistance) { taken = true; break; }
            if (!taken) free.Add(list[i]);
        }
        return free.Count == 0 ? -1 : free[Random.Range(0, free.Count)];
    }

    public void Unregister(IG88Controller d) { droids.Remove(d); }

    /// <summary>
    /// True if moving 'who' to newPos would bring it closer than minDroidDistance to another droid.
    /// Moving AWAY from a close droid is always allowed, so droids never get stuck inside each other.
    /// </summary>
    public bool WouldCollide(IG88Controller who, Vector3 newPos)
    {
        Vector3 cur = who.transform.position;
        for (int i = 0; i < droids.Count; i++)
        {
            IG88Controller o = droids[i];
            if (o == null || o == who) continue;
            Vector3 op = o.transform.position;
            float now = Vector3.Distance(cur, op);
            float after = Vector3.Distance(newPos, op);
            if (after < minDroidDistance && after < now - 0.0001f) return true;
        }
        return false;
    }

    // =====================================================================
    //  IG-88: Scene view gizmos (green / red / yellow connections + names)
    // =====================================================================

    private void OnDrawGizmos()
    {
        if (!drawWaypointGizmos) return;
        Transform[] n = { g1, g2, g3, g4, g5, g6, r1, r2, r3, r4, r5 };

        foreach (Edge e in Edges)
        {
            if (n[e.a] == null || n[e.b] == null) continue;
            Gizmos.color = e.kind == IG88EdgeKind.Green ? Color.green :
                           e.kind == IG88EdgeKind.Red ? Color.red : Color.yellow;
            Gizmos.DrawLine(n[e.a].position, n[e.b].position);
        }

        for (int i = 0; i < n.Length; i++)
        {
            if (n[i] == null) continue;
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(n[i].position, 0.15f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(n[i].position + Vector3.up * 0.35f, NodeNames[i]);
#endif
        }
    }
}