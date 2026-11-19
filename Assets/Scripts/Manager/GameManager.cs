using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using TMPro;

[DefaultExecutionOrder(-50)] // runs before most player scripts
public class GameManager : MonoBehaviour
{
    // One droid per waypoint is the physical maximum: there are 11 waypoints (G1-G6, R1-R5) and droids
    // can spawn and patrol on all of them.
    public const int MaxDroids = 11;

    [Header("Ammo")]
    [SerializeField] private int maxAmmo = 30;
    [SerializeField] private int currentAmmo = 30;          // live counter in Inspector
    [SerializeField] private bool infiniteAmmo = false;

    [Header("Player Health (shown on the Visor HUD health bar)")]
    [SerializeField] private float playerMaxHealth = 120f;
    [SerializeField] private float playerHealth = 120f;     // live counter in Inspector

    [Header("Damage Blood Overlay (Call of Duty style)")]
    [Tooltip("Drag your Canvas Images here (each one keeps its own sprite). Only their alpha (m_Color.a) is ever changed, never the colour. They all start at alpha 0. " +
             "On damage one random Image that is fully faded turns on; a new Image only turns on when one has fully faded again.")]
    [SerializeField] private List<UnityEngine.UI.Image> bloodImages = new List<UnityEngine.UI.Image>();
    [Tooltip("The alpha an Image jumps to the moment it turns on.")]
    [Range(0f, 1f)]
    [SerializeField] private float bloodHitAlpha = 0.3f;
    [Tooltip("Seconds the alpha needs to go linearly from the Hit Alpha back to 0.")]
    [Min(0.01f)]
    [SerializeField] private float bloodFadeTime = 1f;

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
    [Tooltip("Droids will NOT chase, target or shoot the player. They keep patrolling normally.")]
    [SerializeField] private bool droidOff = false;
    [Tooltip("Droids stand completely still and do nothing (no patrol, no chase, no aiming, no shooting). Overrides Droid Off.")]
    [SerializeField] private bool droidPatrolOff = false;

    [Header("IG-88 Spawning")]
    [Tooltip("Drag your IG88 prefab here (it must have the IG88Controller on its root).")]
    [SerializeField] private IG88Controller ig88Prefab;
    [Tooltip("How many droids to spawn. Max = 11 (one per waypoint, G1 and R1 included). They alternate: green patrol, red patrol, green, red ... Every start one droid stands on G2 and one on R2 or R3; the rest are spread out on free waypoints.")]
    [Range(0, MaxDroids)]
    [SerializeField] private int droidCount = 4;

    [Header("IG-88 Health")]
    [Tooltip("Health of every droid, applied when it spawns. A body hit takes 'Damage Per Player Hit Body' away, a headshot takes " +
             "'Damage Per Player Hit Head' away. Example: body 1, head 6 -> with health 6 a droid needs 6 body shots or 1 headshot.")]
    [Min(0.01f)]
    [SerializeField] private float droidHealth = 1f;
    [Tooltip("How much health one BODY hit of the player's blaster removes from a droid. 1 = Droid Health counts body shots.")]
    [FormerlySerializedAs("damagePerPlayerHit")]   // keeps the value you already set in the Inspector
    [Min(0.01f)]
    [SerializeField] private float damagePerPlayerHitBody = 1f;
    [Tooltip("How much health one HEADSHOT removes from a droid.")]
    [Min(0.01f)]
    [SerializeField] private float damagePerPlayerHitHead = 6f;
    [Tooltip("Where the head starts. A hit at or above the droid's head bone counts as a headshot; this is how many metres BELOW the head " +
             "bone a hit still counts. Raise it if shots at the lower face / neck count as body, lower it if upper-chest hits count as head. " +
             "Select a droid in the Scene view while playing to see the line (yellow plate). Not used if the droid has a separate collider on its head.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float headHitMargin = 0.05f;

    [Header("IG-88 Detection / Chase")]
    [Tooltip("Detection radius = CLOSE QUARTERS only, and ONLY after the droid has already SEEN the player once. Before its first sighting a droid " +
             "detects the Player (tag 'Player') by SIGHT only, this radius does nothing. After it has seen him and lost sight, it also senses him inside " +
             "this distance (through walls, from behind ...), but only for 'Lose Sight Time' seconds after it last SAW him. After that it forgets him and " +
             "has to SEE him again. Keep it small, e.g. 4-6 m.")]
    [FormerlySerializedAs("chaseRadius")]   // keeps the value you already set in the Inspector
    [Min(0f)]
    [SerializeField] private float detectionRadius = 5f;
    [Tooltip("SIGHT: a droid detects the Player the moment it can SEE him (clear line of sight, no wall in between) closer than this, " +
             "and it shoots him as long as it sees him. Sight is the ONLY way a droid detects the player for the first time. " +
             "This is also how far droids shoot.")]
    [Min(0f)]
    [SerializeField] private float sightRange = 40f;
    [Tooltip("Seconds a chasing droid keeps chasing after it last SAW the player (the timer counts from the last sight, the Detection Radius does " +
             "not extend it). Then it gives up, forgets him and goes back to its patrol; to chase again it has to SEE him. " +
             "While the timer runs it still walks toward him around corners.")]
    [Min(0f)]
    [SerializeField] private float loseSightTime = 10f;

    [Header("IG-88 Weapon (droids shoot as soon as they see the player, up to the Sight Range)")]
    [Tooltip("Health the player loses per droid bolt that hits him.")]
    [SerializeField] private float droidDamage = 6f;
    [Tooltip("Shots per second.")]
    [Min(0.1f)]
    [SerializeField] private float droidFireRate = 1.5f;
    [Tooltip("Droid aim. 1 = perfect, lower = more bolts miss. 0 = up to 15 degrees off.")]
    [Range(0f, 1f)]
    [SerializeField] private float droidAccuracy = 0.85f;
    [Tooltip("Metres per second (the player's blaster is 90).")]
    [SerializeField] private float droidBoltSpeed = 40f;

    [Header("IG-88 Speed")]
    [Tooltip("Metres per second. Used for patrolling AND chasing.")]
    [SerializeField] private float walkSpeed = 1.0f;
    [Tooltip("Walk animation speed (full step cycles per second).")]
    [SerializeField] private float walkStepsPerSecond = 1.1f;
    [Tooltip("Degrees per second the droid turns on the spot (robotic turning).")]
    [SerializeField] private float turnSpeed = 240f;

    [Header("IG-88 Light + Glow (green = calm, red = chase)")]
    [Tooltip("The droid's point light stays on at this intensity all the time, only its colour changes.")]
    [SerializeField] private float droidLightIntensity = 0.1f;
    [SerializeField] private Color droidGreen = new Color(0.25f, 1f, 0.35f, 1f);
    [SerializeField] private Color droidRed = new Color(1f, 0.15f, 0.15f, 1f);
    [Tooltip("Drag the droid's glow material here (the one with the green emission). Each droid gets its own copy, " +
             "its emission goes green -> red on chase, the original asset is not changed.")]
    [SerializeField] private Material droidGlowMaterial;

    [Header("IG-88 Patrol")]
    [Tooltip("Every droid patrols around the waypoint it spawned on. It only walks to waypoints at most this many connections away from its home. " +
             "1 = very small zone, 2 = natural, 4+ = roams almost the whole route.")]
    [Range(1, 6)]
    [SerializeField] private int patrolZoneSize = 2;

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
        // yellow (patrolled by BOTH routes, and used for chasing)
        new Edge(R5, G6, IG88EdgeKind.Yellow),
        new Edge(R1, G1, IG88EdgeKind.Yellow),
    };

    static readonly int[] GreenNodes = { G1, G2, G3, G4, G5, G6 };
    static readonly int[] RedNodes = { R1, R2, R3, R4, R5, G3 };

    // Waypoints where droids may NOT spawn or patrol (chase-only). Empty now: droids spawn and patrol on
    // G1 and R1 like on every other waypoint. To block a waypoint again, list it here, for example { G1, R1 }.
    static readonly int[] NoPatrolNodes = { };

    /// <summary>True for waypoints where droids may not spawn or patrol (they are chase-only).</summary>
    public bool IsNoPatrolNode(int node)
    {
        for (int i = 0; i < NoPatrolNodes.Length; i++)
            if (NoPatrolNodes[i] == node) return true;
        return false;
    }

    Transform[] nodes;
    List<IG88Link>[] links;
    float[,] dist;
    int[,] next;
    bool graphReady;
    float nextGraphRebuild;

    readonly List<IG88Controller> droids = new List<IG88Controller>();
    Transform player;
    float nextPlayerSearch;

    // ----- player movement: a droid that has a clear shot stands still and fires until the player moves -----
    Vector3 lastPlayerPos;
    bool havePlayerPos;
    float lastPlayerMoveTime = -999f;
    const float PlayerMovingSpeed = 0.1f;    // m/s: above this speed the player counts as moving
    const float PlayerMovingGrace = 0.25f;   // s: after his last movement the player still counts as moving this long (smooths physics-step stutter)
    Collider playerCollider;
    Transform playerColliderOwner;
    bool playerWasAlive = true;

    // ----- droid bolt pools (built once in Start, nothing is instantiated while shooting) -----
    Transform droidBoltRoot, droidSparkRoot;
    readonly List<IG88Bolt> droidBolts = new List<IG88Bolt>();
    readonly List<BlasterHitSparks> droidSparks = new List<BlasterHitSparks>();
    int nextDroidBolt, nextDroidSpark;
    Material boltCoreMat, boltGlowMat, boltGlowOuterMat, sparkRedMat, sparkWhiteMat, flashRedMat;
    const int DroidBoltPoolSize = 32;
    const int DroidSparkPoolSize = 12;
    const float DroidBoltRange = 200f;
    const float DroidBoltRadius = 0.03f;   // sphere-cast radius of a droid bolt (also used for the line-of-sight check)
    const float MaxSpreadDegrees = 15f;
    readonly RaycastHit[] losHits = new RaycastHit[32];

    // ---------- public read-only values used by the droids ----------
    public float DroidHealth => droidHealth;
    public bool DroidOff => droidOff;
    public bool DroidPatrolOff => droidPatrolOff;
    public float HeadHitMargin => headHitMargin;
    public float DetectionRadius => detectionRadius;
    public float SightRange => sightRange;
    public float LoseSightTime => loseSightTime;
    public float DroidDamage => droidDamage;
    public float DroidFireRate => droidFireRate;
    public float DroidSpreadDegrees => (1f - Mathf.Clamp01(droidAccuracy)) * MaxSpreadDegrees;
    public float WalkSpeed => walkSpeed;
    public float WalkSteps => walkStepsPerSecond;
    public float TurnSpeed => turnSpeed;
    public float DroidLightIntensity => droidLightIntensity;
    public Color DroidGreen => droidGreen;
    public Color DroidRed => droidRed;
    public Material DroidGlowMaterial => droidGlowMaterial;
    public float MinDroidDistance => minDroidDistance;
    public int PatrolZoneSize => patrolZoneSize;

    /// <summary>True while the player is moving (position changes), false once he stands still.</summary>
    public bool PlayerMoving => Time.time - lastPlayerMoveTime < PlayerMovingGrace;

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

    /// <summary>Where droids aim: the centre of the player's collider (falls back to 1.2 m above his pivot).</summary>
    public Vector3 PlayerAimPoint
    {
        get
        {
            Transform p = Player;
            if (p == null) return Vector3.zero;
            if (playerColliderOwner != p)
            {
                playerColliderOwner = p;
                playerCollider = p.GetComponentInChildren<Collider>();
                if (playerCollider == null) playerCollider = p.GetComponentInParent<Collider>();
            }
            return playerCollider != null ? playerCollider.bounds.center : p.position + Vector3.up * 1.2f;
        }
    }

    private bool blasterWasEnabled;
    private float[] bloodAlphas = new float[0];   // current alpha of every blood Image (same order as the list)
    private float bloodFadeRate;                  // alpha per second, set at the moment an Image turns on

    // keep the saved droid count inside the allowed range
    private void OnValidate()
    {
        droidCount = Mathf.Clamp(droidCount, 0, MaxDroids);
    }

    private void Awake()
    {
        // Auto-find missing refs
        if (blaster == null) blaster = FindFirstObjectByType<BlasterWeapon>();
        if (visorHud == null) visorHud = FindFirstObjectByType<VisorHud>();
        if (sway == null) sway = FindFirstObjectByType<WalkSprintRotationSway>();
        if (recoil == null) recoil = FindFirstObjectByType<BlasterRecoil>();

        // blood overlay starts invisible (alpha 0, colour untouched)
        bloodAlphas = new float[bloodImages.Count];
        for (int i = 0; i < bloodAlphas.Length; i++)
            SetBloodAlpha(i, 0f);

        // Soft fix for the transform conflict
        if (autoFixSwayRecoilConflict)
            FixSwayRecoilOrder();

        BuildGraph();
    }

    private void Start()
    {
        currentAmmo = Mathf.Clamp(currentAmmo, 0, maxAmmo);
        UpdateAmmoUI();

        playerMaxHealth = Mathf.Max(1f, playerMaxHealth);
        playerHealth = Mathf.Clamp(playerHealth, 0f, playerMaxHealth);
        UpdateHealthUI();

        if (blaster != null)
        {
            blaster.Fired += OnWeaponFired;
            blasterWasEnabled = blaster.enabled;
        }

        BuildDroidPools();
        SpawnDroids();
    }

    private void OnEnable()
    {
        BlasterWeapon.AnyHit += OnBlasterHit;
    }

    private void OnDisable()
    {
        BlasterWeapon.AnyHit -= OnBlasterHit;
    }

    // Every blaster hit in the scene: if the thing that was hit belongs to a droid, damage that droid.
    // A body hit counts as 'Damage Per Player Hit Body' (default 1), a headshot as 'Damage Per Player Hit Head' (default 6).
    private void OnBlasterHit(BlasterHitInfo info)
    {
        if (info.collider == null) return;
        IG88Controller droid = info.collider.GetComponentInParent<IG88Controller>();
        if (droid != null)
        {
            bool headshot = droid.IsHeadHit(info.collider, info.point);
            droid.TakeDamage(headshot ? damagePerPlayerHitHead : damagePerPlayerHitBody, info.point, info.direction);
        }
    }

    private void OnDestroy()
    {
        if (blaster != null)
            blaster.Fired -= OnWeaponFired;
        if (droidBoltRoot != null) Destroy(droidBoltRoot.gameObject);
        if (droidSparkRoot != null) Destroy(droidSparkRoot.gameObject);
    }

    private void Update()
    {
        UpdateBloodFade();
        TrackPlayerMovement();

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

    // Runs before the droids (execution order -50). The droids ask 'PlayerMoving'.
    private void TrackPlayerMovement()
    {
        Transform p = Player;
        if (p == null) { havePlayerPos = false; return; }
        if (!havePlayerPos) { lastPlayerPos = p.position; havePlayerPos = true; return; }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float speed = (p.position - lastPlayerPos).magnitude / dt;
        lastPlayerPos = p.position;
        if (speed > PlayerMovingSpeed) lastPlayerMoveTime = Time.time;
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

    private void UpdateHealthUI()
    {
        // Visor HUD (0-1), same way as the ammo bar
        if (visorHud != null)
        {
            float normalized = playerMaxHealth > 0f ? playerHealth / playerMaxHealth : 0f;
            visorHud.SetHealth(normalized);
        }
    }

    // ---------- Damage blood overlay ----------

    // Only the alpha of m_Color is written, red / green / blue stay exactly as set on the Image.
    private void SetBloodAlpha(int index, float alpha)
    {
        bloodAlphas[index] = alpha;
        UnityEngine.UI.Image img = bloodImages[index];
        if (img == null) return;
        Color c = img.color;
        c.a = alpha;
        img.color = c;
    }

    // Every hit: turn on ONE random Image that is fully faded (alpha 0). If none is fully faded, nothing new turns on.
    private void ShowBlood()
    {
        if (bloodHitAlpha <= 0f) return;

        int n = Mathf.Min(bloodImages.Count, bloodAlphas.Length);
        int free = 0;
        for (int i = 0; i < n; i++)
            if (bloodImages[i] != null && bloodAlphas[i] <= 0f) free++;
        if (free == 0) return;

        int pick = UnityEngine.Random.Range(0, free);
        for (int i = 0; i < n; i++)
        {
            if (bloodImages[i] == null || bloodAlphas[i] > 0f) continue;
            if (pick-- > 0) continue;

            bloodFadeRate = bloodHitAlpha / Mathf.Max(0.01f, bloodFadeTime);
            SetBloodAlpha(i, bloodHitAlpha);
            return;
        }
    }

    // Linear fade back to alpha 0, every Image on its own.
    private void UpdateBloodFade()
    {
        int n = Mathf.Min(bloodImages.Count, bloodAlphas.Length);
        float step = bloodFadeRate * Time.deltaTime;
        for (int i = 0; i < n; i++)
        {
            if (bloodAlphas[i] <= 0f) continue;
            SetBloodAlpha(i, Mathf.MoveTowards(bloodAlphas[i], 0f, step));
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

    public float PlayerHealth => playerHealth;
    public float PlayerMaxHealth => playerMaxHealth;
    public bool PlayerAlive => playerHealth > 0f;

    public void DamagePlayer(float amount)
    {
        if (playerHealth <= 0f) return;
        playerHealth = Mathf.Clamp(playerHealth - amount, 0f, playerMaxHealth);
        UpdateHealthUI();
        if (amount > 0f) ShowBlood();
        if (playerHealth <= 0f && playerWasAlive)
        {
            playerWasAlive = false;
            Debug.Log("Player is dead!");
        }
    }

    public void HealPlayer(float amount)
    {
        playerHealth = Mathf.Clamp(playerHealth + amount, 0f, playerMaxHealth);
        if (playerHealth > 0f) playerWasAlive = true;
        UpdateHealthUI();
    }

    public void SetPlayerHealth(float amount)
    {
        playerHealth = Mathf.Clamp(amount, 0f, playerMaxHealth);
        if (playerHealth > 0f) playerWasAlive = true;
        UpdateHealthUI();
    }

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

    /// <summary>True if a connection of this colour belongs to the patrol network of a route: its OWN colour plus the YELLOW ones.</summary>
    public static bool PatrolEdgeMatches(IG88Route r, IG88EdgeKind kind)
    {
        if (kind == IG88EdgeKind.Yellow) return true;
        return r == IG88Route.Green ? kind == IG88EdgeKind.Green : kind == IG88EdgeKind.Red;
    }

    /// <summary>True if the waypoint has at least one connection a droid of that route patrols (own colour or yellow).</summary>
    public bool NodeOnPatrolNetwork(int node, IG88Route r)
    {
        List<IG88Link> ls = links[node];
        for (int i = 0; i < ls.Count; i++)
            if (PatrolEdgeMatches(r, ls[i].kind)) return true;
        return false;
    }

    public int NodeCount { get { return nodes != null ? nodes.Length : 0; } }

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

    /// <summary>
    /// The closest point on the whole waypoint track (green + red + yellow connections) to a world position.
    /// The point lies on the connection a-b, t = 0 is at node a, t = 1 is at node b.
    /// </summary>
    public bool ClosestTrackPoint(Vector3 p, out int a, out int b, out float t, out Vector3 point)
    {
        a = -1; b = -1; t = 0f; point = p;
        if (!graphReady) return false;

        float best = float.MaxValue;
        for (int i = 0; i < Edges.Length; i++)
        {
            Vector3 pa = nodes[Edges[i].a].position;
            Vector3 pb = nodes[Edges[i].b].position;
            Vector3 ab = pb - pa;
            float len2 = ab.sqrMagnitude;
            float tt = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - pa, ab) / len2) : 0f;
            Vector3 q = pa + ab * tt;
            float d = (q - p).sqrMagnitude;
            if (d < best)
            {
                best = d;
                a = Edges[i].a; b = Edges[i].b; t = tt; point = q;
            }
        }
        return a >= 0;
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

        // Guaranteed start spots: every start one droid stands on G2 (green) and one on R2 or R3 (red, picked at random).
        // With only 1 droid, one of those two spots is used (at random).
        int[] forcedNodes = { G2, Random.value < 0.5f ? R2 : R3 };
        IG88Route[] forcedRoutes = { IG88Route.Green, IG88Route.Red };
        int firstForced = (count == 1 && Random.value < 0.5f) ? 1 : 0;

        for (int i = 0; i < count; i++)
        {
            IG88Route route = (i % 2 == 0) ? IG88Route.Green : IG88Route.Red;
            int node = -1;

            int fi = i + firstForced;
            if (fi < forcedNodes.Length)
            {
                int n = forcedNodes[fi];
                if (!NodeFree(n) && forcedRoutes[fi] == IG88Route.Red)
                    n = (n == R2) ? R3 : R2;                 // the other red spot
                if (NodeFree(n)) { node = n; route = forcedRoutes[fi]; }
            }
            if (node < 0)
                node = PickFreeNode(route);
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

    // true if no droid stands within 'Min Droid Distance' of that waypoint
    private bool NodeFree(int node)
    {
        Vector3 p = NodePos(node);
        for (int d = 0; d < droids.Count; d++)
        {
            if (droids[d] == null) continue;
            if (Vector3.Distance(droids[d].transform.position, p) < minDroidDistance) return false;
        }
        return true;
    }

    // picks the free waypoint that is farthest from the droids already placed, so droids start spread out
    private int PickFreeNode(IG88Route route)
    {
        int[] list = RouteNodes(route);
        int best = -1;
        float bestScore = -1f;
        for (int i = 0; i < list.Length; i++)
        {
            if (IsNoPatrolNode(list[i])) continue;
            Vector3 p = NodePos(list[i]);
            float nearest = float.MaxValue;
            bool taken = false;
            for (int d = 0; d < droids.Count; d++)
            {
                if (droids[d] == null) continue;
                float dd = Vector3.Distance(droids[d].transform.position, p);
                if (dd < minDroidDistance) { taken = true; break; }
                nearest = Mathf.Min(nearest, dd);
            }
            if (taken) continue;

            // farthest from everybody wins, a little randomness so it isn't identical every start
            float score = (nearest == float.MaxValue ? 0f : nearest) + Random.value * 0.5f;
            if (score > bestScore) { bestScore = score; best = list[i]; }
        }
        return best;
    }

    /// <summary>Number of connections from 'start' to every waypoint, walking only along the patrol network of one route (its own colour + the yellow connections).</summary>
    public int[] RouteHops(IG88Route r, int start)
    {
        int[] hops = new int[nodes.Length];
        for (int i = 0; i < hops.Length; i++) hops[i] = 999;
        Queue<int> q = new Queue<int>();
        hops[start] = 0;
        q.Enqueue(start);
        while (q.Count > 0)
        {
            int n = q.Dequeue();
            List<IG88Link> ls = links[n];
            for (int i = 0; i < ls.Count; i++)
            {
                bool ok = PatrolEdgeMatches(r, ls[i].kind);
                if (!ok || hops[ls[i].node] != 999) continue;
                hops[ls[i].node] = hops[n] + 1;
                q.Enqueue(ls[i].node);
            }
        }
        return hops;
    }

    /// <summary>True if another droid is standing close to that waypoint.</summary>
    public bool NodeCrowded(int node, IG88Controller who)
    {
        Vector3 p = NodePos(node);
        for (int i = 0; i < droids.Count; i++)
        {
            IG88Controller o = droids[i];
            if (o == null || o == who) continue;
            if (Vector3.Distance(o.transform.position, p) < minDroidDistance * 2.5f) return true;
        }
        return false;
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
    //  IG-88: droid blaster (same mechanic as the player's blaster, but red)
    // =====================================================================

    private void BuildDroidPools()
    {
        EnsureDroidMaterials();

        droidBoltRoot = new GameObject("IG88 Bolts").transform;
        droidSparkRoot = new GameObject("IG88 Sparks").transform;

        for (int i = 0; i < DroidBoltPoolSize; i++)
            droidBolts.Add(CreateDroidBolt(i));

        for (int i = 0; i < DroidSparkPoolSize; i++)
        {
            GameObject g = new GameObject("Spark Burst " + i);
            g.transform.SetParent(droidSparkRoot, false);
            BlasterHitSparks s = g.AddComponent<BlasterHitSparks>();
            // same settings as the player's sparks, red materials / red light
            s.Build(16, sparkRedMat, sparkWhiteMat, flashRedMat,
                    0.012f, 0.14f, 0.22f,
                    2.5f, 9f, 0.22f, 0.55f, 9.81f,
                    true, droidRed, 3f, 3.5f);
            droidSparks.Add(s);
        }
    }

    private IG88Bolt CreateDroidBolt(int index)
    {
        const float length = 0.7f, radius = 0.028f;

        GameObject root = new GameObject("Droid Bolt " + index);
        root.transform.SetParent(droidBoltRoot, false);
        IG88Bolt bolt = root.AddComponent<IG88Bolt>();
        bolt.Setup(this, DroidBoltRadius);

        MakeBoltCapsule("Core", root.transform, radius, length, boltCoreMat);
        MakeBoltCapsule("Glow", root.transform, radius * 2.2f, length * 1.15f, boltGlowMat);
        MakeBoltCapsule("Glow Outer", root.transform, radius * 3.6f, length * 1.4f, boltGlowOuterMat);

        Light l = root.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = droidRed;
        l.intensity = 2f;
        l.range = 5f;
        l.shadows = LightShadows.None;

        root.SetActive(false);
        return bolt;
    }

    private void MakeBoltCapsule(string objectName, Transform parent, float radius, float length, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = objectName;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);                       // capsule axis (Y) -> bolt axis (Z)
        go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);   // capsule is 1 wide, 2 tall
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// <summary>
    /// True if a droid bolt flying from 'origin' to 'target' (the player) would hit the player first,
    /// i.e. no wall or other obstacle is in the way. Uses the same sphere-cast as IG88Bolt and ignores
    /// droid bodies the same way (droids never block each other's shots), so what this says is what the bolt would do.
    /// </summary>
    public bool HasClearShot(Vector3 origin, Vector3 target)
    {
        Vector3 d = target - origin;
        float len = d.magnitude;
        if (len < 0.01f) return true;

        int n = Physics.SphereCastNonAlloc(origin, DroidBoltRadius, d / len, losHits, len, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        Collider first = null;
        for (int i = 0; i < n; i++)
        {
            Collider c = losHits[i].collider;
            if (c == null) continue;
            if (c.GetComponentInParent<IG88Controller>() != null) continue;   // droid bodies are ignored by the bolt too
            if (losHits[i].distance < bestDist) { bestDist = losHits[i].distance; first = c; }
        }

        if (first == null) return true;          // nothing in the way
        return IsPlayerCollider(first);          // the first thing the bolt would hit must be the player
    }

    /// <summary>A droid shoots: takes the next bolt from the pool.</summary>
    public void FireDroidBolt(Vector3 origin, Vector3 direction)
    {
        if (droidBolts.Count == 0) return;
        IG88Bolt b = droidBolts[nextDroidBolt];
        nextDroidBolt = (nextDroidBolt + 1) % droidBolts.Count;
        b.Launch(origin, direction, droidBoltSpeed, DroidBoltRange, droidDamage);
    }

    /// <summary>Called by a droid bolt when it hits something.</summary>
    public void DroidBoltHit(RaycastHit hit, Vector3 direction, float damage)
    {
        if (droidSparks.Count > 0)
        {
            BlasterHitSparks s = droidSparks[nextDroidSpark];
            nextDroidSpark = (nextDroidSpark + 1) % droidSparks.Count;
            s.Play(hit.point, hit.normal);
        }

        if (IsPlayerCollider(hit.collider))
            DamagePlayer(damage);

        Rigidbody rb = hit.rigidbody;
        if (rb != null && !rb.isKinematic)
            rb.AddForceAtPosition(direction.normalized * 3f, hit.point, ForceMode.Impulse);
    }

    private bool IsPlayerCollider(Collider c)
    {
        if (c == null) return false;
        if (c.CompareTag("Player")) return true;
        Transform p = Player;
        if (p == null) return false;
        Transform ct = c.transform;
        return ct.IsChildOf(p) || p.IsChildOf(ct);
    }

    private void EnsureDroidMaterials()
    {
        if (boltCoreMat == null) boltCoreMat = RedMaterial("IG88 Bolt Core", new Color(2.4f, 0.7f, 0.7f), false);
        if (boltGlowMat == null) boltGlowMat = RedMaterial("IG88 Bolt Glow", new Color(1.1f, 0.18f, 0.18f), true);
        if (boltGlowOuterMat == null) boltGlowOuterMat = RedMaterial("IG88 Bolt Glow Outer", new Color(0.55f, 0.1f, 0.1f), true);
        if (sparkRedMat == null) sparkRedMat = RedMaterial("IG88 Spark Red", new Color(4f, 0.6f, 0.35f), false);
        if (sparkWhiteMat == null) sparkWhiteMat = RedMaterial("IG88 Spark White", new Color(4f, 2.2f, 2.0f), false);
        if (flashRedMat == null) flashRedMat = RedMaterial("IG88 Impact Flash", new Color(2.2f, 0.4f, 0.3f), true);
    }

    // same recipe as the player's fallback materials, just red
    private static Material RedMaterial(string materialName, Color emission, bool transparent)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material m = new Material(shader);
        m.name = materialName;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", 1f);
            m.SetFloat("_DstBlend", 10f);
            m.SetFloat("_ZWrite", 0f);
            m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }
        else
        {
            m.SetColor("_BaseColor", Color.black);
        }
        return m;
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