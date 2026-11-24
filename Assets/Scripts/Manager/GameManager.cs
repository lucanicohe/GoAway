using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using TMPro;
using FlatheadRobotKit;

[DefaultExecutionOrder(-50)] // runs before most player scripts
public class GameManager : MonoBehaviour
{
    // Eight stationary guard posts (NEW1-NEW8) plus six separate patrol segments.
    public const int MaxDroids = 14;

    // One Flathead per waypoint is the physical maximum: 20 waypoints (W1-W13 on the walls, T1-T7 on the ceiling).
    public const int MaxFlatheads = 20;

    [Header("UniRx Reactive Properties (Event-driven UI & State)")]
    // Reactive Properties: only execute code when something changes.
    // The UniRx types are written with their full name on purpose: if another reactive library (R3) is in the project too,
    // a short name like ReactiveProperty can bind to the wrong one.
    public readonly UniRx.ReactiveProperty<int> AmmoProperty = new UniRx.ReactiveProperty<int>(30);
    public readonly UniRx.ReactiveProperty<float> HealthProperty = new UniRx.ReactiveProperty<float>(120f);
    public readonly UniRx.Subject<float> PlayerDamaged = new UniRx.Subject<float>();
    private readonly UniRx.CompositeDisposable reactiveSubscriptions = new UniRx.CompositeDisposable();

    [Header("CullingGroup (Frustum Culling & Distance LOD)")]
    [SerializeField] private EnemyCullingManager cullingManager;

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
    [Tooltip("Droids (IG-88 AND Flatheads) will NOT chase, target or shoot the player. Patrol droids keep moving on their routes; stationary IG-88 guards stay at their posts.")]
    [SerializeField] private bool droidOff = false;
    [Tooltip("Droids (IG-88 AND Flatheads) stand completely still and do nothing (no patrol, no chase, no aiming, no shooting). Overrides Droid Off.")]
    [SerializeField] private bool droidPatrolOff = false;

    [Header("IG-88 Spawning")]
    [Tooltip("Drag your IG88 prefab here (it must have the IG88Controller on its root).")]
    [SerializeField] private IG88Controller ig88Prefab;
    [Tooltip("How many droids to spawn. Max = 14: eight stationary guard posts (NEW1-NEW8) and six patrol segments. Each Play start randomly selects unique posts/routes, favoring locations farther from droids already placed.")]
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
    [Tooltip("For patrol droids: close-range detection through walls, but only after the droid has already seen the player, and only for 'Lose Sight Time'. " +
             "Stationary NEW1-NEW8 guards instead use their separate Guard Detection Radius, even before seeing the player.")]
    [FormerlySerializedAs("chaseRadius")]   // keeps the value you already set in the Inspector
    [Min(0f)]
    [SerializeField] private float detectionRadius = 5f;
    [Tooltip("Extra close-range detection used only by stationary NEW1-NEW8 guards. They can notice the player through walls inside this radius, even before seeing him. Default = 9 m.")]
    [Min(0f)]
    [SerializeField] private float stationaryGuardDetectionRadius = 9f;
    [Tooltip("SIGHT: a patrol droid detects the player when it has a clear line of sight within this range, and it shoots while it sees him. " +
             "Stationary guards may also detect the player through walls inside their separate short Guard Detection Radius. This is how far droids shoot.")]
    [Min(0f)]
    [SerializeField] private float sightRange = 40f;
    [Tooltip("Seconds a droid stays aware after it last saw the player (or, for a guard, last sensed him inside Guard Detection Radius). Then it gives up " +
             "and returns to its patrol route or guard post. Patrol droids still need sight to start a new chase; guards can detect again inside their radius.")]
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

    [Header("IG-88 Bolt Light (the red light on every droid bolt, works live while playing)")]
    [SerializeField] private float droidBoltLightIntensity = 2f;
    [SerializeField] private float droidBoltLightRange = 5f;

    [Header("IG-88 Patrol")]
    [Tooltip("Maximum route hops from a droid's spawn point. Each new patrol segment is one hop long, so 1 or more allows it to patrol the full segment.")]
    [Range(1, 6)]
    [SerializeField] private int patrolZoneSize = 2;

    [Header("IG-88 Collision")]
    [Tooltip("Droids never get closer than this to each other. A droid that would break this waits until the other one moves.")]
    [SerializeField] private float minDroidDistance = 1.5f;

    [Header("Waypoints - GREEN side patrols (drag empty objects here)")]
    [SerializeField] private Transform g1;
    [SerializeField] private Transform g2;
    [SerializeField] private Transform g3;   // retained as a scene waypoint; not used by the new patrols
    [SerializeField] private Transform g4;
    [SerializeField] private Transform g5;
    [SerializeField] private Transform g6;

    [Header("Waypoints - RED side patrols (drag empty objects here)")]
    [SerializeField] private Transform r1;
    [SerializeField] private Transform r2;
    [SerializeField] private Transform r3;
    [SerializeField] private Transform r4;
    [SerializeField] private Transform r5;

    [Header("Waypoints - STATIONARY GUARD POSTS (NEW1-NEW8)")]
    [SerializeField] private Transform new1;
    [SerializeField] private Transform new2;
    [SerializeField] private Transform new3;
    [SerializeField] private Transform new4;
    [SerializeField] private Transform new5;
    [SerializeField] private Transform new6;
    [SerializeField] private Transform new7;
    [SerializeField] private Transform new8;

    [Header("IG-88 Debug")]
    [SerializeField] private bool drawWaypointGizmos = true;

    // =====================================================================
    //  FLATHEAD ROBOTS (four-legged turrets on the walls and the ceiling)
    //  They use the same detection / aim settings as the droids above (Sight Range, Detection Radius, Lose Sight Time,
    //  Droid Accuracy, Droid Bolt Speed, Droid Off, Droid Patrol Off). Their damage and fire rate are their own (Flathead Weapon).
    //  They are SNEAKY (see 'Flathead Stealth'): one at a time hunts the player, but any Flathead under the player's crosshair immediately
    //  dodges and can fire in self-defense while moving.
    // =====================================================================

    [Header("Flathead Spawning")]
    [Tooltip("Drag your Flathead prefab here (it must have the FlatheadController on its root). The GameManager adds the FlatheadEnemy script by itself.")]
    [SerializeField] private FlatheadController flatheadPrefab;
    [Tooltip("How many Flatheads to spawn. Max = 20 (one per waypoint: W1-W13 on the walls, T1-T7 on the ceiling). They start spread out over free waypoints.")]
    [Range(0, MaxFlatheads)]
    [SerializeField] private int flatheadCount = 4;

    [Header("Flathead Health")]
    [Tooltip("Health of every Flathead. A body hit takes 'Damage Per Player Hit Body' away, a hit on the turret takes 'Damage Per Player Hit Head' away.")]
    [Min(0.01f)]
    [SerializeField] private float flatheadHealth = 3f;
    [Tooltip("A hit this close (metres) to the turret pivot counts as a headshot (unless the prefab has a separate collider on the turret).")]
    [Range(0.05f, 0.6f)]
    [SerializeField] private float flatheadTurretHitRadius = 0.25f;

    [Header("Flathead Weapon (the bolts a Flathead shoots; all work live while playing)")]
    [Tooltip("Health the player loses per Flathead bolt that hits him. (The IG-88 droids have their own 'Droid Damage'.)")]
    [Min(0f)]
    [SerializeField] private float flatheadDamage = 6f;
    [Tooltip("AUTOMATIC FIRE: bolts per second while a Flathead strikes. It keeps firing at this rate until its finite shot limit is reached. " +
             "(The IG-88 droids use 'Droid Fire Rate'.) Works at any frame rate.")]
    [Range(1f, 60f)]
    [SerializeField] private float flatheadFireRate = 25f;
    [Tooltip("Colour of the Flathead bolt: its core, its glow and the light on it.")]
    [SerializeField] private Color flatheadBoltColor = new Color(1f, 0.15f, 0.15f, 1f);
    [Tooltip("Intensity of the point light on every Flathead bolt.")]
    [Min(0f)]
    [SerializeField] private float flatheadBoltLightIntensity = 2f;
    [Tooltip("Range (metres) of the point light on every Flathead bolt.")]
    [Min(0f)]
    [SerializeField] private float flatheadBoltLightRange = 5f;

    [Header("IG-88 + Flathead Bolt Audio")]
    [Tooltip("Optional short seamless loop played in 3D while IG-88 and Flathead bolts are flying.")]
    [SerializeField] private AudioClip enemyBoltFlightLoop;
    [Tooltip("Played in 3D at the impact point when an IG-88 or Flathead bolt hits any collider.")]
    [SerializeField] private AudioClip enemyBoltImpactSound;
    [SerializeField, Range(0f, 1f)] private float enemyBoltFlightVolume = 0.45f;
    [SerializeField, Range(0f, 1f)] private float enemyBoltImpactVolume = 0.8f;
    [SerializeField, Min(0.1f)] private float enemyBoltAudioMinDistance = 1f;
    [SerializeField, Min(1f)] private float enemyBoltAudioMaxDistance = 35f;

    [Header("Flathead Movement")]
    [Tooltip("Walk animation speed (full step cycles per second). The walking speed in m/s is Stride Length x Steps Per Second / 0.55, so the feet never slide.")]
    [Range(0.3f, 4f)]
    [SerializeField] private float flatheadStepsPerSecond = 1.8f;
    [Tooltip("Length of one step in metres.")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float flatheadStrideLength = 0.30f;
    [Tooltip("Metres per second while flying through the air on a jump (blue connection). Longer jumps take longer.")]
    [Min(0.5f)]
    [SerializeField] private float flatheadJumpSpeed = 3f;
    [Tooltip("How far the jump arcs out into the room (metres).")]
    [Range(0f, 2f)]
    [SerializeField] private float flatheadJumpArc = 0.5f;
    [Tooltip("Every Flathead patrols around the waypoint it spawned on. It only goes to waypoints at most this many connections away from its home.")]
    [Range(1, 10)]
    [SerializeField] private int flatheadPatrolZoneSize = 4;
    [Tooltip("Flatheads never get closer than this to each other. One that would break this waits until the other one moves.")]
    [SerializeField] private float minFlatheadDistance = 0.5f;

    [Header("Flathead Stealth and Evasion")]
    [Tooltip("A Flathead closer than this to the player (metres) senses him even WITHOUT a line of sight, through walls, like a mosquito smelling you. " +
             "Whenever ONE Flathead sees or senses the player, ALL Flatheads are alerted for 'Lose Sight Time' seconds and the next one comes, " +
             "even one that never saw him. 0 = sight only.")]
    [Min(0f)]
    [SerializeField] private float flatheadSenseRadius = 15f;
    [Tooltip("Maximum distance where the player's center-screen shot ray can trigger a Flathead to evade and defend itself.")]
    [Min(0f)]
    [SerializeField] private float flatheadAimThreatRange = 200f;
    [Tooltip("Walking and jump speed while it closes in on the player, as a multiple of normal speed. 2 = twice as fast.")]
    [Range(2f, 3f)]
    [SerializeField] private float flatheadSneakSpeed = 2f;
    [Tooltip("Walking speed (and jump speed) while it evades, flees during an attack, or runs after its burst.")]
    [Range(3.5f, 5f)]
    [SerializeField] private float flatheadFleeSpeed = 3.5f;
    [Tooltip("It stops and strikes when it is this close to the player (metres) and has a clear shot. If the track doesn't let it get that close, it strikes from the closest point it can reach.")]
    [Min(0.5f)]
    [SerializeField] private float flatheadStrikeDistance = 6f;
    [Tooltip("Bolts in one attack before it runs away. Set to 0 or 99+ for unlimited shots. With a finite limit, it runs after its burst and the next Flathead comes.")]
    [Min(0)]
    [SerializeField] private int flatheadShotsPerStrike = 99;
    [Tooltip("Seconds a Flathead stays away after a finite burst. Aim evasion is brief and does not remove the active hunter.")]
    [Min(0f)]
    [SerializeField] private float flatheadRetreatTime = 12f;
    [Tooltip("Pause (seconds) between one Flathead giving up / running / dying and the next one being sent.")]
    [Min(0f)]
    [SerializeField] private float flatheadReplaceDelay = 2f;

    [Header("Flathead Room Layout")]
    [Tooltip("World direction of the TOP of your waypoint drawing (where the arrows at the top of the picture point). Default (0,0,1) = the top of the drawing is world +Z, the right of the drawing is world +X. " +
             "Change it if your room is turned: the walls the robots stand on are worked out from it.")]
    [SerializeField] private Vector3 flatheadPictureUp = new Vector3(0f, 0f, 1f);
    [Tooltip("Metres the Flathead is lifted off the wall / ceiling, away from it. 0 = the Flathead's pivot (feet) sits exactly on the waypoint.")]
    [SerializeField] private float flatheadSurfaceOffset = 0f;

    [Header("Flathead Waypoints - WALLS (empty objects on the wall surface, as in the drawing)")]
    [SerializeField] private Transform w1;
    [SerializeField] private Transform w2;
    [SerializeField] private Transform w3;
    [SerializeField] private Transform w4;
    [SerializeField] private Transform w5;
    [SerializeField] private Transform w6;
    [SerializeField] private Transform w7;
    [SerializeField] private Transform w8;
    [SerializeField] private Transform w9;
    [SerializeField] private Transform w10;
    [SerializeField] private Transform w11;
    [SerializeField] private Transform w12;
    [SerializeField] private Transform w13;

    [Header("Flathead Waypoints - CEILING (empty objects on the ceiling surface, robot hangs upside down)")]
    [SerializeField] private Transform t1;
    [SerializeField] private Transform t2;
    [SerializeField] private Transform t3;
    [SerializeField] private Transform t4;
    [SerializeField] private Transform t5;
    [SerializeField] private Transform t6;
    [SerializeField] private Transform t7;

    [Header("Flathead Debug")]
    [Tooltip("Scene view: red line = WALK connection, blue line = JUMP connection, green stick at every waypoint = direction away from the surface (the robot's back, its feet are on the surface).")]
    [SerializeField] private bool drawFlatheadGizmos = true;

    // The waypoint graph, routes, shortest paths, and closest-track queries live in EnemyMoveSystem.
    // This GameManager keeps the IG-88 and Flathead waypoint objects and hands them over to their movement systems.
    float nextGraphRebuild;

    readonly List<IG88Controller> droids = new List<IG88Controller>();
    readonly List<FlatheadEnemy> flatheads = new List<FlatheadEnemy>();
    Transform flatheadAimTarget;   // sits on the centre of the player: the Flatheads' turrets aim at it
    FlatheadEnemy flatheadHunter;  // the ONE Flathead that is going after the player right now (the others lurk)
    float nextFlatheadHunterTime;  // the next one is not sent before this time
    float flatheadAlertUntil;      // while Time.time is below this, ALL Flatheads know where the player is (somebody saw / sensed him recently)
    UnityEngine.Camera gazeCamera; // camera whose center-screen shot ray detects a Flathead under aim
    FlatheadEnemy aimedFlatheadThisFrame;
    int flatheadAimCacheFrame = -1;
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

    // ----- droid bolt pool (UnityEngine.Pool.ObjectPool: filled once in Start, a bolt goes back to the pool the moment it hits or runs out of range) -----
    Transform droidBoltRoot, droidSparkRoot;
    UnityEngine.Pool.ObjectPool<IG88Bolt> droidBoltPool;
    int droidBoltCounter;
    readonly List<BlasterHitSparks> droidSparks = new List<BlasterHitSparks>();   // spark bursts: fixed ring of 12, the oldest burst is restarted
    int nextDroidSpark;
    Material boltCoreMat, boltGlowMat, boltGlowOuterMat, sparkRedMat, sparkWhiteMat, flashRedMat;

    // ----- Flathead bolt pool: same bolt as the droid's, but its own pool, materials (colour) and light, so it can look and hit differently -----
    Transform flatheadBoltRoot;
    UnityEngine.Pool.ObjectPool<IG88Bolt> flatheadBoltPool;
    int flatheadBoltCounter;
    Material flatheadCoreMat, flatheadGlowMat, flatheadGlowOuterMat;
    Color flatheadAppliedColor;
    const int FlatheadBoltPoolSize = 16;
    const int FlatheadBoltPoolMax = 128;
    const int DroidBoltPoolSize = 32;      // bolts made up front
    const int DroidBoltPoolMax = 256;      // the pool may grow to this many when many droids fire at once
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
    public float StationaryGuardDetectionRadius => stationaryGuardDetectionRadius;
    public float SightRange => sightRange;
    public float LoseSightTime => loseSightTime;
    public float DroidDamage => droidDamage;
    public float FlatheadDamage => flatheadDamage;
    public float FlatheadFireRate => flatheadFireRate;
    public float FlatheadSenseRadius => flatheadSenseRadius;
    public float FlatheadAimThreatRange => flatheadAimThreatRange;

    /// <summary>True while the Flatheads are alerted: one of them saw or sensed the player less than 'Lose Sight Time' seconds ago.</summary>
    public bool FlatheadsAlerted => Time.time < flatheadAlertUntil;
    /// <summary>A Flathead sees or senses the player right now: every Flathead is alerted for 'Lose Sight Time' seconds from now.</summary>
    public void FlatheadAlert() { flatheadAlertUntil = Time.time + loseSightTime; }
    public float DroidFireRate => droidFireRate;
    public float DroidSpreadDegrees => (1f - Mathf.Clamp01(droidAccuracy)) * MaxSpreadDegrees;
    public AudioClip EnemyBoltImpactSound => enemyBoltImpactSound;
    public float EnemyBoltImpactVolume => enemyBoltImpactVolume;
    public float EnemyBoltAudioMinDistance => enemyBoltAudioMinDistance;
    public float EnemyBoltAudioMaxDistance => enemyBoltAudioMaxDistance;

    public void ConfigureEnemyBoltFlightAudio(AudioSource source)
    {
        if (source == null) return;
        source.clip = enemyBoltFlightLoop;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = enemyBoltAudioMinDistance;
        source.maxDistance = Mathf.Max(enemyBoltAudioMinDistance, enemyBoltAudioMaxDistance);
        source.volume = enemyBoltFlightVolume;
    }

    public float WalkSpeed => walkSpeed;
    public float WalkSteps => walkStepsPerSecond;
    public float TurnSpeed => turnSpeed;
    public float DroidLightIntensity => droidLightIntensity;
    public Color DroidGreen => droidGreen;
    public Color DroidRed => droidRed;
    public Material DroidGlowMaterial => droidGlowMaterial;
    public float MinDroidDistance => minDroidDistance;
    public int PatrolZoneSize => patrolZoneSize;

    // ---------- public read-only values used by the Flatheads ----------
    public float FlatheadHealth => flatheadHealth;
    public float FlatheadTurretHitRadius => flatheadTurretHitRadius;
    public float FlatheadStepsPerSecond => flatheadStepsPerSecond;
    public float FlatheadStrideLength => flatheadStrideLength;
    public float FlatheadJumpSpeed => flatheadJumpSpeed;
    public float FlatheadJumpArc => flatheadJumpArc;
    public int FlatheadPatrolZone => flatheadPatrolZoneSize;
    public Transform FlatheadAimTarget => flatheadAimTarget;
    public float FlatheadSneakSpeed => Mathf.Max(2f, flatheadSneakSpeed);
    public float FlatheadFleeSpeed => Mathf.Max(3.5f, flatheadFleeSpeed);
    public float FlatheadStrikeDistance => flatheadStrikeDistance;
    public int FlatheadShotsPerStrike => flatheadShotsPerStrike;
    public float FlatheadRetreatTime => flatheadRetreatTime;

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
        stationaryGuardDetectionRadius = Mathf.Max(0f, stationaryGuardDetectionRadius);
        flatheadCount = Mathf.Clamp(flatheadCount, 0, MaxFlatheads);
        flatheadSneakSpeed = Mathf.Clamp(flatheadSneakSpeed, 2f, 3f);
        flatheadFleeSpeed = Mathf.Clamp(flatheadFleeSpeed, 3.5f, 5f);
    }

    private void Awake()
    {
        // Auto-find missing refs
        if (blaster == null) blaster = FindFirstObjectByType<BlasterWeapon>();
        if (visorHud == null) visorHud = FindFirstObjectByType<VisorHud>();
        if (sway == null) sway = FindFirstObjectByType<WalkSprintRotationSway>();
        if (recoil == null) recoil = FindFirstObjectByType<BlasterRecoil>();

        // Initialize reactive properties from Inspector values
        AmmoProperty.Value = currentAmmo;
        HealthProperty.Value = playerHealth;

        // Auto-initialize CullingGroup manager
        if (cullingManager == null)
        {
            cullingManager = GetComponent<EnemyCullingManager>();
            if (cullingManager == null)
                cullingManager = gameObject.AddComponent<EnemyCullingManager>();
        }

        // blood overlay starts invisible (alpha 0, colour untouched)
        bloodAlphas = new float[bloodImages.Count];
        for (int i = 0; i < bloodAlphas.Length; i++)
            SetBloodAlpha(i, 0f);

        // Soft fix for the transform conflict
        if (autoFixSwayRecoilConflict)
            FixSwayRecoilOrder();

        BuildGraph();
        BuildFlatheadGraph();
    }

    private void Start()
    {
        currentAmmo = Mathf.Clamp(currentAmmo, 0, maxAmmo);
        AmmoProperty.Value = currentAmmo;

        playerMaxHealth = Mathf.Max(1f, playerMaxHealth);
        playerHealth = Mathf.Clamp(playerHealth, 0f, playerMaxHealth);
        HealthProperty.Value = playerHealth;

        // UniRx Reactive Subscriptions — only execute when values change!
        reactiveSubscriptions.Add(UniRx.ObservableExtensions.Subscribe(AmmoProperty, ammo =>
        {
            currentAmmo = ammo;
            UpdateAmmoUI();
            if (blaster != null)
            {
                bool canShoot = infiniteAmmo || ammo > 0;
                if (blaster.enabled != canShoot)
                    blaster.enabled = canShoot;
            }
        }));

        reactiveSubscriptions.Add(UniRx.ObservableExtensions.Subscribe(HealthProperty, hp =>
        {
            playerHealth = hp;
            UpdateHealthUI();
            if (hp <= 0f && playerWasAlive)
            {
                playerWasAlive = false;
                Debug.Log("Player is dead!");
            }
            else if (hp > 0f)
            {
                playerWasAlive = true;
            }
        }));

        reactiveSubscriptions.Add(UniRx.ObservableExtensions.Subscribe(PlayerDamaged, amt =>
        {
            if (amt > 0f) ShowBlood();
        }));

        if (blaster != null)
        {
            blaster.Fired += OnWeaponFired;
            blasterWasEnabled = blaster.enabled;
        }

        if (Player != null && cullingManager != null)
            cullingManager.SetReferencePoint(Player);

        BuildDroidPools();
        SpawnDroids();
        SpawnFlatheads();
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
            return;
        }

        // same for the Flathead robots: a hit on the turret counts as a headshot
        FlatheadEnemy flathead = info.collider.GetComponentInParent<FlatheadEnemy>();
        if (flathead != null)
        {
            bool turretHit = flathead.IsTurretHit(info.collider, info.point);
            flathead.TakeDamage(turretHit ? damagePerPlayerHitHead : damagePerPlayerHitBody, info.point, info.direction);
        }
    }

    private void OnDestroy()
    {
        if (blaster != null)
            blaster.Fired -= OnWeaponFired;
        reactiveSubscriptions.Dispose();
        droidBoltPool = null;
        flatheadBoltPool = null;
        if (flatheadBoltRoot != null) Destroy(flatheadBoltRoot.gameObject);
        if (droidBoltRoot != null) Destroy(droidBoltRoot.gameObject);
        if (droidSparkRoot != null) Destroy(droidSparkRoot.gameObject);
        if (flatheadAimTarget != null) Destroy(flatheadAimTarget.gameObject);
    }

    private void Update()
    {
        UpdateBloodFade();
        TrackPlayerMovement();

        // Note: blaster.enabled is now reactively managed by AmmoProperty.Subscribe (no per-frame check needed!)

        // keep path lengths correct if you move waypoints while playing
        if (Time.time >= nextGraphRebuild)
        {
            nextGraphRebuild = Time.time + 1f;
            if (EnemyMoveSystem.Ready) EnemyMoveSystem.RebuildDistances();
            if (FlatheadMoveSystem.Ready) FlatheadMoveSystem.Configure(flatheadPictureUp, flatheadSurfaceOffset);
        }

        // the Flatheads' turrets aim at this object: keep it on the centre of the player
        if (flatheadAimTarget != null && Player != null) flatheadAimTarget.position = PlayerAimPoint;

        UpdateFlatheadHunter();
    }

    // Runs before the droids (execution order -50). The droids ask 'PlayerMoving'.
    private void TrackPlayerMovement()
    {
        Transform p = Player;
        if (p == null) { havePlayerPos = false; return; }
        if (cullingManager != null) cullingManager.SetReferencePoint(p);
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

        AmmoProperty.Value = Mathf.Max(0, AmmoProperty.Value - 1);

        if (AmmoProperty.Value <= 0)
            Debug.Log("Out of ammo!");
    }

    private void UpdateAmmoUI()
    {
        // Visor HUD (0-1) + live number text
        if (visorHud != null)
        {
            float normalized = maxAmmo > 0 ? (float)currentAmmo / maxAmmo : 0f;
            visorHud.SetAmmo(normalized);
            visorHud.SetAmmoNumber(currentAmmo);
        }

        // 3D TextMeshPro
        if (ammoText3D != null)
        {
            ammoText3D.text = currentAmmo.ToString();
        }
    }

    private void UpdateHealthUI()
    {
        // Visor HUD (0-1) + live number text (shows 120 → 0 etc.)
        if (visorHud != null)
        {
            float normalized = playerMaxHealth > 0f ? playerHealth / playerMaxHealth : 0f;
            visorHud.SetHealth(normalized);
            visorHud.SetHealthNumber(Mathf.RoundToInt(playerHealth));
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

    // ---------- Public helpers you can call from other scripts (UniRx Reactive) ----------

    public void AddAmmo(int amount)
    {
        AmmoProperty.Value = Mathf.Clamp(AmmoProperty.Value + amount, 0, maxAmmo);
    }

    public void SetAmmo(int amount)
    {
        AmmoProperty.Value = Mathf.Clamp(amount, 0, maxAmmo);
    }

    public int CurrentAmmo => AmmoProperty.Value;
    public int MaxAmmo => maxAmmo;
    public bool HasAmmo => infiniteAmmo || AmmoProperty.Value > 0;

    public float PlayerHealth => HealthProperty.Value;
    public float PlayerMaxHealth => playerMaxHealth;
    public bool PlayerAlive => HealthProperty.Value > 0f;

    public void DamagePlayer(float amount)
    {
        if (HealthProperty.Value <= 0f) return;
        HealthProperty.Value = Mathf.Clamp(HealthProperty.Value - amount, 0f, playerMaxHealth);
        PlayerDamaged.OnNext(amount);
    }

    public void HealPlayer(float amount)
    {
        HealthProperty.Value = Mathf.Clamp(HealthProperty.Value + amount, 0f, playerMaxHealth);
    }

    public void SetPlayerHealth(float amount)
    {
        HealthProperty.Value = Mathf.Clamp(amount, 0f, playerMaxHealth);
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
            AmmoProperty.Value = maxAmmo;
            Debug.Log("Reloaded!");
        }
    }

    // =====================================================================
    //  IG-88: waypoints (the graph itself lives in EnemyMoveSystem)
    // =====================================================================

    // hands all 19 waypoint objects to the EnemyMoveSystem, which owns all waypoints, connections and paths
    private void BuildGraph()
    {
        List<string> missing = new List<string>();
        Transform[] waypoints = { g1, g2, g3, g4, g5, g6, r1, r2, r3, r4, r5, new1, new2, new3, new4, new5, new6, new7, new8 };
        if (!EnemyMoveSystem.SetWaypoints(waypoints, missing))
            Debug.LogError("[GameManager] Waypoints not assigned: " + string.Join(", ", missing) +
                           ". No IG-88 droids will spawn until all 19 are dragged in.", this);
    }

    // =====================================================================
    //  IG-88: spawning + collision
    // =====================================================================

    [ContextMenu("Respawn Droids")]
    public void SpawnDroids()
    {
        ClearDroids();
        if (ig88Prefab == null) { Debug.LogWarning("[GameManager] No IG88 prefab assigned - no droids spawned.", this); return; }
        if (!EnemyMoveSystem.Ready) return;

        int count = Mathf.Clamp(droidCount, 0, MaxDroids);

        List<DroidSpawnOption> options = new List<DroidSpawnOption>(MaxDroids);
        for (int i = 0; i < 8; i++)
            options.Add(new DroidSpawnOption { standingGuard = true, node = EnemyMoveSystem.NEW1 + i });
        for (int i = 0; i < EnemyMoveSystem.PatrolRoutes.Length; i++)
            options.Add(new DroidSpawnOption { standingGuard = false, route = EnemyMoveSystem.PatrolRoutes[i], node = -1 });

        List<Vector3> placedPositions = new List<Vector3>(count);
        HashSet<int> usedPatrolStartNodes = new HashSet<int>();
        for (int i = 0; i < count && options.Count > 0; i++)
        {
            int bestOption = -1;
            int bestNode = -1;
            float bestScore = float.MinValue;

            for (int o = 0; o < options.Count; o++)
            {
                DroidSpawnOption option = options[o];
                int candidateNode;
                float score;
                if (option.standingGuard)
                {
                    candidateNode = option.node;
                    score = SpawnSpreadScore(EnemyMoveSystem.NodePos(candidateNode), placedPositions);
                }
                else
                {
                    candidateNode = BestAvailableRouteStart(option.route, usedPatrolStartNodes, placedPositions, out score);
                    if (candidateNode < 0) continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestOption = o;
                    bestNode = candidateNode;
                }
            }

            if (bestOption < 0)
            {
                Debug.LogWarning("[GameManager] Could not find a distinct start point for every requested droid. Spawned " + droids.Count + ".", this);
                break;
            }

            DroidSpawnOption chosen = options[bestOption];
            options.RemoveAt(bestOption);
            if (!chosen.standingGuard) usedPatrolStartNodes.Add(bestNode);

            Vector3 spawnPosition = EnemyMoveSystem.NodePos(bestNode);
            IG88Controller d = Instantiate(ig88Prefab, spawnPosition, ig88Prefab.transform.rotation);
            d.name = "IG88_" + (i + 1).ToString("00") + (chosen.standingGuard ? "_Guard" : "_Patrol");
            droids.Add(d);
            d.Init(this, chosen.route, bestNode, chosen.standingGuard);
            placedPositions.Add(spawnPosition);
            if (cullingManager != null) cullingManager.RegisterEnemy(d);
        }
    }

    private void ClearDroids()
    {
        for (int i = droids.Count - 1; i >= 0; i--)
        {
            if (droids[i] != null)
            {
                if (cullingManager != null) cullingManager.UnregisterEnemy(droids[i]);
                Destroy(droids[i].gameObject);
            }
        }
        droids.Clear();
    }

    /// <summary>Legacy helper for the three old middle-room points, no longer used by patrol spawning.</summary>
    public static bool IsChaseOnlyNode(int node)
    {
        return node == 2 || node == 3 || node == EnemyMoveSystem.R3;
    }

    private struct DroidSpawnOption
    {
        public bool standingGuard;
        public IG88PatrolRoute route;
        public int node;
    }

    // Select an unused endpoint for this route, preferring the one furthest from existing spawns.
    private int BestAvailableRouteStart(IG88PatrolRoute route, HashSet<int> usedNodes, List<Vector3> placed, out float score)
    {
        int[] endpoints = EnemyMoveSystem.RouteNodes(route);
        int bestNode = -1;
        score = float.MinValue;
        for (int i = 0; i < endpoints.Length; i++)
        {
            int node = endpoints[i];
            if (usedNodes.Contains(node)) continue;
            float candidateScore = SpawnSpreadScore(EnemyMoveSystem.NodePos(node), placed);
            if (candidateScore > score)
            {
                score = candidateScore;
                bestNode = node;
            }
        }
        return bestNode;
    }

    // Farthest-point placement spreads a partial set across the scene; random jitter changes each Play start.
    private float SpawnSpreadScore(Vector3 point, List<Vector3> placed)
    {
        if (placed.Count == 0) return Random.value;
        float nearest = float.MaxValue;
        for (int i = 0; i < placed.Count; i++)
            nearest = Mathf.Min(nearest, Vector3.Distance(point, placed[i]));
        return nearest + Random.Range(0f, 0.5f);
    }

    /// <summary>True if another droid is standing close to that waypoint.</summary>
    public bool NodeCrowded(int node, IG88Controller who)
    {
        Vector3 p = EnemyMoveSystem.NodePos(node);
        for (int i = 0; i < droids.Count; i++)
        {
            IG88Controller o = droids[i];
            if (o == null || o == who) continue;
            if (Vector3.Distance(o.transform.position, p) < minDroidDistance * 2.5f) return true;
        }
        return false;
    }

    public void Unregister(IG88Controller d)
    {
        if (cullingManager != null) cullingManager.UnregisterEnemy(d);
        droids.Remove(d);
    }

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

        droidBoltPool = new UnityEngine.Pool.ObjectPool<IG88Bolt>(
            createFunc: CreateDroidBolt,
            actionOnGet: null,
            actionOnRelease: b => { if (b != null) b.gameObject.SetActive(false); },
            actionOnDestroy: b => { if (b != null) Destroy(b.gameObject); },
            collectionCheck: false,
            defaultCapacity: DroidBoltPoolSize,
            maxSize: DroidBoltPoolMax);

        // fill the pool now so nothing is created while the first shots are flying
        IG88Bolt[] warm = new IG88Bolt[DroidBoltPoolSize];
        for (int i = 0; i < warm.Length; i++) warm[i] = droidBoltPool.Get();
        for (int i = 0; i < warm.Length; i++) droidBoltPool.Release(warm[i]);

        // the Flathead's own bolt pool
        EnsureFlatheadMaterials();
        flatheadBoltRoot = new GameObject("Flathead Bolts").transform;
        flatheadBoltPool = new UnityEngine.Pool.ObjectPool<IG88Bolt>(
            createFunc: CreateFlatheadBolt,
            actionOnGet: null,
            actionOnRelease: b => { if (b != null) b.gameObject.SetActive(false); },
            actionOnDestroy: b => { if (b != null) Destroy(b.gameObject); },
            collectionCheck: false,
            defaultCapacity: FlatheadBoltPoolSize,
            maxSize: FlatheadBoltPoolMax);

        IG88Bolt[] warmFlat = new IG88Bolt[FlatheadBoltPoolSize];
        for (int i = 0; i < warmFlat.Length; i++) warmFlat[i] = flatheadBoltPool.Get();
        for (int i = 0; i < warmFlat.Length; i++) flatheadBoltPool.Release(warmFlat[i]);

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
            s.ConfigureImpactAudio(enemyBoltImpactSound, enemyBoltImpactVolume,
                                   enemyBoltAudioMinDistance, enemyBoltAudioMaxDistance);
            droidSparks.Add(s);
        }
    }

    private IG88Bolt CreateDroidBolt()
    {
        return BuildBolt("Droid Bolt " + (droidBoltCounter++), droidBoltRoot, false,
                         boltCoreMat, boltGlowMat, boltGlowOuterMat,
                         droidRed, droidBoltLightIntensity, droidBoltLightRange);
    }

    private IG88Bolt CreateFlatheadBolt()
    {
        return BuildBolt("Flathead Bolt " + (flatheadBoltCounter++), flatheadBoltRoot, true,
                         flatheadCoreMat, flatheadGlowMat, flatheadGlowOuterMat,
                         flatheadBoltColor, flatheadBoltLightIntensity, flatheadBoltLightRange);
    }

    private IG88Bolt BuildBolt(string objectName, Transform parent, bool flathead,
                               Material core, Material glow, Material outer,
                               Color lightColor, float lightIntensity, float lightRange)
    {
        const float length = 0.7f, radius = 0.028f;

        GameObject root = new GameObject(objectName);
        root.transform.SetParent(parent, false);
        IG88Bolt bolt = root.AddComponent<IG88Bolt>();
        bolt.Setup(this, DroidBoltRadius, flathead);

        MakeBoltCapsule("Core", root.transform, radius, length, core);
        MakeBoltCapsule("Glow", root.transform, radius * 2.2f, length * 1.15f, glow);
        MakeBoltCapsule("Glow Outer", root.transform, radius * 3.6f, length * 1.4f, outer);

        Light l = root.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = lightColor;
        l.intensity = lightIntensity;
        l.range = lightRange;
        l.shadows = LightShadows.None;

        root.SetActive(false);
        return bolt;
    }

    // Flathead bolt materials: the three emission colours are worked out from the one 'Flathead Bolt Color'
    // (core = brighter and a little whiter, glow = the colour, outer glow = half of it; the default red gives the droid bolt look).
    private void EnsureFlatheadMaterials()
    {
        if (flatheadCoreMat == null) flatheadCoreMat = RedMaterial("Flathead Bolt Core", Color.black, false);
        if (flatheadGlowMat == null) flatheadGlowMat = RedMaterial("Flathead Bolt Glow", Color.black, true);
        if (flatheadGlowOuterMat == null) flatheadGlowOuterMat = RedMaterial("Flathead Bolt Glow Outer", Color.black, true);
        ApplyFlatheadBoltColor(true);
    }

    private void ApplyFlatheadBoltColor(bool force)
    {
        if (flatheadCoreMat == null || flatheadGlowMat == null || flatheadGlowOuterMat == null) return;
        if (!force && flatheadBoltColor == flatheadAppliedColor) return;   // only touch the materials when the colour really changed
        flatheadAppliedColor = flatheadBoltColor;

        Color c = flatheadBoltColor;
        Color whiter = Color.Lerp(c, Color.white, 0.2f);
        flatheadCoreMat.SetColor("_EmissionColor", new Color(whiter.r * 2.4f, whiter.g * 2.4f, whiter.b * 2.4f, 1f));
        flatheadGlowMat.SetColor("_EmissionColor", new Color(c.r * 1.1f, c.g * 1.1f, c.b * 1.1f, 1f));
        flatheadGlowOuterMat.SetColor("_EmissionColor", new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 1f));
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

    // ----- "is this collider part of a droid / Flathead?" (used by the line-of-sight check AND by the droid bolts) -----
    // Fast path: the IG-88 and Flathead prefab roots carry the "Droid" tag. The answer is remembered per collider, so the
    // tag walk / component walk happens ONCE per collider and every later hit is a dictionary lookup.
    // CompareTag throws when the tag does not exist in the project, so that is tested once (try / catch) and the tag is skipped if missing.
    const string EnemyTag = "Droid";
    static int enemyTagState;                                        // 0 = not checked yet, 1 = the tag exists, -1 = it does not
    static readonly Dictionary<Collider, bool> enemyColliderCache = new Dictionary<Collider, bool>(128);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetEnemyStatics()
    {
        enemyTagState = 0;
        enemyColliderCache.Clear();
    }

    static bool EnemyTagExists(Transform probe)
    {
        if (enemyTagState == 0)
        {
            try { probe.CompareTag(EnemyTag); enemyTagState = 1; }
            catch (UnityException) { enemyTagState = -1; }
        }
        return enemyTagState == 1;
    }

    public static bool IsEnemyCollider(Collider c)
    {
        if (c == null) return false;

        bool result;
        if (enemyColliderCache.TryGetValue(c, out result)) return result;

        result = false;
        Transform t = c.transform;
        if (EnemyTagExists(t))
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.CompareTag(EnemyTag)) { result = true; break; }
            }
        }
        if (!result)   // scenes / prefabs without the tag still work
            result = c.GetComponentInParent<IG88Controller>() != null || c.GetComponentInParent<FlatheadEnemy>() != null;

        if (enemyColliderCache.Count > 4096) enemyColliderCache.Clear();   // destroyed colliders (dead droid pieces) never pile up
        enemyColliderCache[c] = result;
        return result;
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
            if (IsEnemyCollider(c)) continue;   // droid and Flathead bodies are ignored by the bolt too
            if (losHits[i].distance < bestDist) { bestDist = losHits[i].distance; first = c; }
        }

        if (first == null) return true;          // nothing in the way
        return IsPlayerCollider(first);          // the first thing the bolt would hit must be the player
    }

    /// <summary>A droid shoots: takes a bolt from the pool (the pool makes a new one if all are in flight).</summary>
    public void FireDroidBolt(Vector3 origin, Vector3 direction)
    {
        if (droidBoltPool == null) return;
        IG88Bolt b = droidBoltPool.Get();
        b.Launch(origin, direction, droidBoltSpeed, DroidBoltRange, droidDamage, droidBoltLightIntensity, droidBoltLightRange, droidRed);
    }

    /// <summary>A Flathead shoots: its own bolt (damage, colour and light come from the Flathead Weapon section).</summary>
    public void FireFlatheadBolt(Vector3 origin, Vector3 direction)
    {
        if (flatheadBoltPool == null) return;
        ApplyFlatheadBoltColor(false);
        IG88Bolt b = flatheadBoltPool.Get();
        b.Launch(origin, direction, droidBoltSpeed, DroidBoltRange, flatheadDamage, flatheadBoltLightIntensity, flatheadBoltLightRange, flatheadBoltColor);
    }

    /// <summary>Called by a droid bolt when it has hit something or flown out of range: it goes back to the pool.</summary>
    public void ReleaseDroidBolt(IG88Bolt bolt)
    {
        if (bolt == null) return;
        UnityEngine.Pool.ObjectPool<IG88Bolt> pool = bolt.IsFlathead ? flatheadBoltPool : droidBoltPool;
        if (pool != null) pool.Release(bolt);
        else bolt.gameObject.SetActive(false);
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
    //  FLATHEADS: waypoints, spawning, collision
    // =====================================================================

    // hands the 20 waypoint objects to the FlatheadMoveSystem, which owns all Flathead waypoints, connections and paths
    private void BuildFlatheadGraph()
    {
        List<string> missing = new List<string>();
        Transform[] waypoints = { w1, w2, w3, w4, w5, w6, w7, w8, w9, w10, w11, w12, w13, t1, t2, t3, t4, t5, t6, t7 };
        if (!FlatheadMoveSystem.SetWaypoints(waypoints, missing, flatheadPictureUp, flatheadSurfaceOffset) && flatheadCount > 0 && flatheadPrefab != null)
            Debug.LogError("[GameManager] Flathead waypoints not assigned: " + string.Join(", ", missing) +
                           ". No Flatheads will spawn until all 20 are dragged in.", this);
    }

    [ContextMenu("Respawn Flatheads")]
    public void SpawnFlatheads()
    {
        ClearFlatheads();
        if (flatheadPrefab == null)
        {
            if (flatheadCount > 0) Debug.LogWarning("[GameManager] No Flathead prefab assigned - no Flatheads spawned.", this);
            return;
        }
        if (!FlatheadMoveSystem.Ready) return;

        if (flatheadAimTarget == null)
            flatheadAimTarget = new GameObject("Flathead Aim Target").transform;

        int count = Mathf.Clamp(flatheadCount, 0, MaxFlatheads);
        for (int i = 0; i < count; i++)
        {
            int node = PickFreeFlatheadNode();
            if (node < 0)
            {
                Debug.LogWarning("[GameManager] No free Flathead waypoint left (waypoints closer than 'Min Flathead Distance'?). Spawned " + flatheads.Count + " Flatheads.", this);
                break;
            }

            FlatheadController inst = Instantiate(flatheadPrefab, FlatheadMoveSystem.NodePos(node), flatheadPrefab.transform.rotation);
            inst.name = "Flathead_" + (i + 1).ToString("00");
            FlatheadEnemy enemy = inst.GetComponent<FlatheadEnemy>();
            if (enemy == null) enemy = inst.gameObject.AddComponent<FlatheadEnemy>();
            flatheads.Add(enemy);
            enemy.Init(this, node);   // puts it on the waypoint, in the pose of that waypoint (wall or ceiling)
            if (cullingManager != null) cullingManager.RegisterEnemy(enemy);
        }
    }

    private void ClearFlatheads()
    {
        for (int i = flatheads.Count - 1; i >= 0; i--)
        {
            if (flatheads[i] != null)
            {
                if (cullingManager != null) cullingManager.UnregisterEnemy(flatheads[i]);
                Destroy(flatheads[i].gameObject);
            }
        }
        flatheads.Clear();
        flatheadHunter = null;
        flatheadAlertUntil = 0f;
    }

    // picks the free waypoint that is farthest from the Flatheads already placed, so they start spread out
    private int PickFreeFlatheadNode()
    {
        int best = -1;
        float bestScore = -1f;
        for (int n = 0; n < FlatheadMoveSystem.NodeTotal; n++)
        {
            Vector3 p = FlatheadMoveSystem.NodePos(n);
            float nearest = float.MaxValue;
            bool taken = false;
            for (int d = 0; d < flatheads.Count; d++)
            {
                if (flatheads[d] == null) continue;
                float dd = Vector3.Distance(flatheads[d].transform.position, p);
                if (dd < minFlatheadDistance) { taken = true; break; }
                nearest = Mathf.Min(nearest, dd);
            }
            if (taken) continue;

            float score = (nearest == float.MaxValue ? 0f : nearest) + Random.value * 0.5f;
            if (score > bestScore) { bestScore = score; best = n; }
        }
        return best;
    }

    public void UnregisterFlathead(FlatheadEnemy f)
    {
        if (cullingManager != null) cullingManager.UnregisterEnemy(f);
        flatheads.Remove(f);

        // the hunter died: a beat later the next Flathead tries its luck
        if (flatheadHunter != null && ReferenceEquals(flatheadHunter, f))
        {
            flatheadHunter = null;
            nextFlatheadHunterTime = Time.time + flatheadReplaceDelay;
        }
    }

    // =====================================================================
    //  FLATHEAD SNEAK ATTACK: one hunter at a time, with direct-aim self-defense
    // =====================================================================

    /// <summary>
    /// Only ONE Flathead is assigned to hunt at a time. Any other Flathead the player directly aims at can evade and fire in self-defense
    /// without taking over the hunter slot. When the hunter flees after a finite burst, gives up or dies, the next one is sent after
    /// 'Flathead Replace Delay': the closest Flathead that isn't on cooldown. It does not need a line of sight to be selected.
    /// </summary>
    private void UpdateFlatheadHunter()
    {
        if (flatheadHunter != null)
        {
            if (!flatheadHunter.IsDead && flatheadHunter.IsHunting) return;   // still at it
            flatheadHunter = null;
            nextFlatheadHunterTime = Time.time + flatheadReplaceDelay;        // a beat before the next one comes
            return;
        }

        if (Time.time < nextFlatheadHunterTime) return;
        Transform p = Player;
        if (p == null || !PlayerAlive || droidOff || droidPatrolOff) return;
        if (!FlatheadsAlerted) return;   // nobody has seen or sensed the player lately: they all just lurk

        FlatheadEnemy best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < flatheads.Count; i++)
        {
            FlatheadEnemy f = flatheads[i];
            if (f == null || !f.CanHunt) continue;
            float d = (f.transform.position - p.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = f; }
        }
        if (best == null) return;

        flatheadHunter = best;
        best.StartHunting();
    }

    /// <summary>True when the player's center-screen shot ray hits this Flathead. Cached so all Flatheads share one raycast per frame.</summary>
    public bool IsPlayerAimingAt(FlatheadEnemy target)
    {
        if (target == null || droidOff || droidPatrolOff || !PlayerAlive)
            return false;

        if (flatheadAimCacheFrame != Time.frameCount)
            RefreshFlatheadAimCache();

        return aimedFlatheadThisFrame == target;
    }

    private void RefreshFlatheadAimCache()
    {
        flatheadAimCacheFrame = Time.frameCount;
        aimedFlatheadThisFrame = null;

        if (flatheadAimThreatRange <= 0f)
            return;
        if (gazeCamera == null)
            gazeCamera = UnityEngine.Camera.main;
        if (gazeCamera == null)
            return;

        Ray ray = gazeCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int count = Physics.RaycastNonAlloc(ray.origin, ray.direction, losHits, flatheadAimThreatRange,
                                            ~0, QueryTriggerInteraction.Ignore);
        int nearest = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = losHits[i].collider;
            if (c == null || IsPlayerCollider(c)) continue;
            if (losHits[i].distance < nearestDistance)
            {
                nearest = i;
                nearestDistance = losHits[i].distance;
            }
        }

        if (nearest >= 0)
        {
            FlatheadEnemy flathead = losHits[nearest].collider.GetComponentInParent<FlatheadEnemy>();
            if (flathead != null && !flathead.IsDead)
                aimedFlatheadThisFrame = flathead;
        }
    }

    /// <summary>
    /// True when the player is LOOKING at a point: it is within maxAngleDegrees of the centre of the screen, closer than maxDistance and
    /// nothing solid is in between (droid bodies and the player himself don't block the view). Doesn't care how well the thing is hidden.
    /// </summary>
    public bool IsPlayerLookingAt(Vector3 point, float maxAngleDegrees, float maxDistance)
    {
        if (maxAngleDegrees <= 0f) return false;
        if (gazeCamera == null) gazeCamera = UnityEngine.Camera.main;   // full name on purpose: a script called "Camera" would hide Unity's Camera
        if (gazeCamera == null) return false;

        Transform ct = gazeCamera.transform;
        Vector3 d = point - ct.position;
        float dist = d.magnitude;
        if (dist > maxDistance) return false;
        if (dist < 0.05f) return true;
        if (Vector3.Angle(ct.forward, d) > maxAngleDegrees) return false;

        int n = Physics.RaycastNonAlloc(ct.position, d / dist, losHits, Mathf.Max(0f, dist - 0.1f), ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = losHits[i].collider;
            if (c == null || IsEnemyCollider(c) || IsPlayerCollider(c)) continue;
            return false;   // a wall (or anything solid) is in the way
        }
        return true;
    }

    /// <summary>True if another Flathead is standing close to that waypoint.</summary>
    public bool FlatheadNodeCrowded(int node, FlatheadEnemy who)
    {
        Vector3 p = FlatheadMoveSystem.NodePos(node);
        for (int i = 0; i < flatheads.Count; i++)
        {
            FlatheadEnemy o = flatheads[i];
            if (o == null || o == who) continue;
            if (Vector3.Distance(o.transform.position, p) < minFlatheadDistance * 2.5f) return true;
            if (o.HasLanding && Vector3.Distance(o.LandingPos, p) < minFlatheadDistance * 2.5f) return true;
        }
        return false;
    }

    /// <summary>True if another Flathead stands on (or within 'Min Flathead Distance' of) that spot, so a jump must not land there.</summary>
    public bool FlatheadSpotTaken(Vector3 pos, FlatheadEnemy who)
    {
        for (int i = 0; i < flatheads.Count; i++)
        {
            FlatheadEnemy o = flatheads[i];
            if (o == null || o == who) continue;
            if (Vector3.Distance(o.transform.position, pos) < minFlatheadDistance) return true;
            if (o.HasLanding && Vector3.Distance(o.LandingPos, pos) < minFlatheadDistance) return true;   // somebody is about to land there
        }
        return false;
    }

    /// <summary>
    /// True if moving 'who' to newPos would bring it closer than 'Min Flathead Distance' to another Flathead.
    /// Moving AWAY from a close one is always allowed, so they never get stuck inside each other.
    /// </summary>
    public bool WouldCollideFlathead(FlatheadEnemy who, Vector3 newPos)
    {
        Vector3 cur = who.transform.position;
        for (int i = 0; i < flatheads.Count; i++)
        {
            FlatheadEnemy o = flatheads[i];
            if (o == null || o == who) continue;
            Vector3 op = o.transform.position;
            float now = Vector3.Distance(cur, op);
            float after = Vector3.Distance(newPos, op);
            if (after < minFlatheadDistance && after < now - 0.0001f) return true;

            // a robot that is in the air also blocks the spot it is going to land on
            if (o.HasLanding)
            {
                Vector3 lp = o.LandingPos;
                float lnow = Vector3.Distance(cur, lp);
                float lafter = Vector3.Distance(newPos, lp);
                if (lafter < minFlatheadDistance && lafter < lnow - 0.0001f) return true;
            }
        }
        return false;
    }

    // Scene view: red = walk connection, blue = jump connection, green stick = the robot's "up" (away from the surface its feet stand on)
    private void DrawFlatheadGizmos()
    {
        if (!drawFlatheadGizmos) return;
        Transform[] n = { w1, w2, w3, w4, w5, w6, w7, w8, w9, w10, w11, w12, w13, t1, t2, t3, t4, t5, t6, t7 };

        foreach (FlatheadMoveSystem.Edge e in FlatheadMoveSystem.Edges)
        {
            if (n[e.a] == null || n[e.b] == null) continue;
            Gizmos.color = e.kind == FlatheadEdgeKind.Walk ? Color.red : new Color(0.25f, 0.45f, 1f);
            Gizmos.DrawLine(n[e.a].position, n[e.b].position);
        }

        for (int i = 0; i < n.Length; i++)
        {
            if (n[i] == null) continue;
            Gizmos.color = i >= FlatheadMoveSystem.T1 ? Color.cyan : Color.white;
            Gizmos.DrawSphere(n[i].position, 0.15f);

            Vector3 up, fwd;
            FlatheadMoveSystem.PreviewFrame(i, flatheadPictureUp, out up, out fwd);
            Vector3 stand = n[i].position + up * flatheadSurfaceOffset;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(stand, stand + up * 0.6f);        // robot's back (feet are at the waypoint)
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(stand, stand + fwd * 0.3f);       // direction it faces
#if UNITY_EDITOR
            UnityEditor.Handles.Label(n[i].position + Vector3.up * 0.35f, FlatheadMoveSystem.NodeNames[i]);
#endif
        }
    }

    // =====================================================================
    //  IG-88: Scene view gizmos (the six patrol segments + waypoint names)
    // =====================================================================

    private void OnDrawGizmos()
    {
        DrawFlatheadGizmos();
        if (!drawWaypointGizmos) return;
        Transform[] n = { g1, g2, g3, g4, g5, g6, r1, r2, r3, r4, r5, new1, new2, new3, new4, new5, new6, new7, new8 };

        foreach (EnemyMoveSystem.Edge e in EnemyMoveSystem.Connections)
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
            UnityEditor.Handles.Label(n[i].position + Vector3.up * 0.35f, EnemyMoveSystem.NodeNames[i]);
#endif
        }
    }
}
