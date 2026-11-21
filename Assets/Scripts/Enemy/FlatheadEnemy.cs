using System.Collections.Generic;
using UnityEngine;
using FlatheadRobotKit;

/// <summary>
/// Flathead ENEMY brain, the counterpart of the IG88Controller. The GameManager adds it to the Flathead prefab by itself when it spawns
/// (the prefab only needs the FlatheadController, you don't have to add this script).
///  - It drives the FlatheadController (walking, walk direction, jump, turret aim). The animation itself stays in the FlatheadController.
///  - SHOOTING: red bolts from the gun muzzle, only while the robot sees the player, the gun points at him and the bolt has a free path.
///  - HEALTH + DEATH: a hit on the turret does 'headshot' damage, a hit anywhere else body damage (see the GameManager). At 0 every part
///    breaks loose and falls (from the ceiling too).
/// WHERE the robot goes and WHEN it detects / chases the player is decided by the FlatheadMoveSystem (waypoints only, no NavMesh).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Flathead Robot/Flathead Enemy")]
public class FlatheadEnemy : MonoBehaviour
{
    [Header("Health (set by GameManager when the robot spawns)")]
    public float health = 3f;

    [Header("Shooting")]
    [Tooltip("The robot only fires when its gun points at the player within this many degrees.")]
    [Range(2f, 45f)] public float maxFireAngle = 12f;

    [Header("Death: break apart")]
    [Tooltip("How hard the pieces are thrown away when the robot dies.")]
    public float scatterForce = 3f;
    [Tooltip("How fast the pieces tumble.")]
    public float spinForce = 4f;
    [Tooltip("Size of the collider of pieces that have no child bone, in metres.")]
    [Range(0.05f, 0.4f)] public float leafPieceSize = 0.12f;
    [Tooltip("Seconds until the pieces disappear. 0 = they stay on the ground forever.")]
    public float debrisLifetime = 0f;

    FlatheadController ctrl;
    GameManager gm;
    FlatheadMoveSystem move;
    bool ready;
    bool wasChasing;
    float nextFireTime;
    Vector3 aimPoint;
    Vector3 restForwardLocal = Vector3.forward;   // robot forward in root-local space (never changes)

    public FlatheadController Ctrl { get { return ctrl; } }
    public bool IsDead { get; private set; }

    /// <summary>While the robot is jumping, the spot it will land on is reserved: other Flatheads keep their distance from it too.</summary>
    public bool HasLanding { get; private set; }
    public Vector3 LandingPos { get; private set; }
    public void ReserveLanding(Vector3 pos) { LandingPos = pos; HasLanding = true; }
    public void ClearLanding() { HasLanding = false; }

    /// <summary>Walking speed (m/s) at which the feet don't slide.</summary>
    public float WalkSpeed { get { return ctrl.WalkSpeed; } }
    /// <summary>Where the robot looks from (the turret). The FlatheadMoveSystem uses it for the line-of-sight test.</summary>
    public Vector3 EyePosition { get { return ctrl.TurretPosition; } }
    public bool HasGun { get { return ctrl != null && ctrl.IsReady; } }
    /// <summary>Where the bolts start (the gun muzzle).</summary>
    public Vector3 MuzzlePosition { get { return ctrl.MuzzlePosition; } }

    void Awake()
    {
        ctrl = GetComponent<FlatheadController>();
        if (ctrl == null)
        {
            Debug.LogError("FlatheadEnemy: needs a FlatheadController on the same object.", this);
            enabled = false;
            return;
        }
        ctrl.DetectRig();
        EnsureHitCollider();
    }

    void OnDestroy()
    {
        if (gm != null) gm.UnregisterFlathead(this);
    }

    /// <summary>Called by the GameManager right after spawning.</summary>
    public void Init(GameManager manager, int startNode)
    {
        gm = manager;
        if (ctrl == null) return;

        // the FlatheadController must not do anything on its own: this script drives it
        ctrl.walking = false;
        ctrl.jump = false;
        ctrl.aimAtTarget = false;
        ctrl.orbitTarget = false;
        ctrl.moveWhileWalking = false;
        ctrl.jumpPause = 0f;
        ctrl.tiltMax = 89f;                  // on a wall or the ceiling the player is almost straight "up" from the robot's feet
        ctrl.stepsPerSecond = gm.FlatheadStepsPerSecond;
        ctrl.strideLength = gm.FlatheadStrideLength;
        ctrl.target = null;

        restForwardLocal = transform.InverseTransformDirection(ctrl.Forward);

        IsDead = false;
        health = gm.FlatheadHealth;
        wasChasing = false;
        move = new FlatheadMoveSystem(this, gm, startNode);   // puts the robot on its start waypoint
        ready = true;
    }

    /// <summary>
    /// The rotation that makes the robot stand with its 'up' (the side opposite its feet) along 'up' and its front along 'forward'.
    /// Independent of how the prefab was imported.
    /// </summary>
    public Quaternion PoseRotation(Vector3 forward, Vector3 up)
    {
        Quaternion rest = Quaternion.LookRotation(restForwardLocal, Vector3.up);
        return Quaternion.LookRotation(forward, up) * Quaternion.Inverse(rest);
    }

    /// <summary>The Walk Direction (degrees, 0 = forward, 90 = right, 180 = back, 270 = left) that makes the feet step toward a world direction.</summary>
    public float LocalWalkAngle(Vector3 worldDirection)
    {
        Vector3 up = transform.up;
        Vector3 f = ctrl.Forward;
        Vector3 r = Vector3.Cross(up, f);
        Vector3 d = Vector3.ProjectOnPlane(worldDirection, up);
        if (d.sqrMagnitude < 1e-8f) return ctrl.walkDirection;
        return Mathf.Atan2(Vector3.Dot(d, r), Vector3.Dot(d, f)) * Mathf.Rad2Deg;
    }

    // ------------------------------------------------------------------ update

    void Update()
    {
        if (!ready || gm == null || move == null || IsDead) return;
        float dt = Time.deltaTime;

        // detection + waypoint movement (walking / jumping flags of the FlatheadController are set in here)
        move.Tick(dt);

        // a chase starts / ends: the turret aims at the player (or goes back to rest), and the first shot is allowed right away
        bool chasing = move.Chasing;
        if (chasing != wasChasing)
        {
            wasChasing = chasing;
            ctrl.aimAtTarget = chasing;
            if (chasing) nextFireTime = Time.time;
        }
        ctrl.target = gm.FlatheadAimTarget;   // an object that sits on the centre of the player (kept up to date by the GameManager)

        if (gm.Player != null) aimPoint = gm.PlayerAimPoint;

        if (!gm.DroidPatrolOff) UpdateFiring();
    }

    // ------------------------------------------------------------------ shooting (same rules as the IG-88)

    void UpdateFiring()
    {
        if (!move.Chasing || !gm.PlayerAlive) return;
        if (gm.Player == null || Time.time < nextFireTime) return;

        // sight is the only distance / visibility rule: it shoots while it sees the player
        if (!move.SeesPlayer) return;

        Vector3 origin = ctrl.MuzzlePosition;
        Vector3 want = aimPoint - origin;
        if (want.sqrMagnitude < 0.01f) return;

        // only when the gun really points at the player
        if (Vector3.Angle(ctrl.GunDirection, want) > maxFireAngle) return;

        // and the bolt must have a free path from the muzzle (never into a wall)
        if (!move.ClearShot) return;

        nextFireTime = Time.time + 1f / Mathf.Max(0.1f, gm.DroidFireRate);

        // aim at the player, then add the random spread (Droid Accuracy in the GameManager)
        Vector3 dir = want.normalized;
        float spread = gm.DroidSpreadDegrees;
        if (spread > 0f)
        {
            Vector2 s = Random.insideUnitCircle * spread;
            dir = Quaternion.LookRotation(dir) * Quaternion.Euler(-s.y, s.x, 0f) * Vector3.forward;
        }
        gm.FireDroidBolt(origin, dir);
    }

    // ------------------------------------------------------------------ health / death

    /// <summary>True if a hit counts as a headshot: it hit the turret (collider of the turret, or the hit point is close to the turret).</summary>
    public bool IsTurretHit(Collider hitCollider, Vector3 hitPoint)
    {
        if (IsDead || gm == null || ctrl == null || !ctrl.IsReady) return false;
        Transform pan = ctrl.TurretBone;
        if (hitCollider != null && pan != null && hitCollider.transform.IsChildOf(pan)) return true;   // a separate collider on the turret
        return Vector3.Distance(hitPoint, ctrl.TurretPosition) <= gm.FlatheadTurretHitRadius;
    }

    /// <summary>Damage from any source. When health reaches 0 the robot breaks apart.</summary>
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
        HasLanding = false;
        ctrl.walking = false;
        ctrl.jump = false;
        ctrl.aimAtTarget = false;
        if (gm != null) gm.UnregisterFlathead(this);   // a dead robot no longer blocks the living ones
        BreakApart(hitPoint, hitDirection);
        ctrl.enabled = false;                          // otherwise its pose reset would snap the bones back
        enabled = false;
    }

    // If the prefab has no solid collider, bolts would fly through the robot. Make a box from the renderer bounds.
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
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.center = transform.InverseTransformPoint(b.center);
        box.size = new Vector3(b.size.x / Mathf.Max(Mathf.Abs(s.x), 0.0001f),
                               b.size.y / Mathf.Max(Mathf.Abs(s.y), 0.0001f),
                               b.size.z / Mathf.Max(Mathf.Abs(s.z), 0.0001f));

        // a moving collider should have a kinematic rigidbody
        if (GetComponent<Rigidbody>() == null)
            gameObject.AddComponent<Rigidbody>().isKinematic = true;
    }

    void AddPiece(List<Transform> list, Transform t)
    {
        if (t == null || t == transform || !t.IsChildOf(transform)) return;
        if (!list.Contains(t)) list.Add(t);
    }

    // box that covers the bone and the points of its child bones, in the bone's own space
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

    /// <summary>Every rigged part (turret, body, legs ...) is cut loose and becomes a falling rigidbody.</summary>
    void BreakApart(Vector3 hitPoint, Vector3 hitDirection)
    {
        // old colliders (the hit box) would fight with the pieces
        Collider[] oldCols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < oldCols.Length; i++) oldCols[i].enabled = false;

        // which bones become pieces: everything the skinned mesh is bound to (+ the objects of any plain mesh renderers)
        List<Transform> pieces = new List<Transform>();
        SkinnedMeshRenderer[] skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int s = 0; s < skins.Length; s++)
        {
            skins[s].updateWhenOffscreen = true;   // bounds follow the falling bones, mesh is not culled
            Transform[] bones = skins[s].bones;
            for (int i = 0; i < bones.Length; i++) AddPiece(pieces, bones[i]);
        }
        MeshRenderer[] meshes = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < meshes.Length; i++) AddPiece(pieces, meshes[i].transform);
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
            body.maxDepenetrationVelocity = 3f;

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

        // pieces of the same robot never collide with each other
        for (int i = 0; i < boxes.Length; i++)
            for (int j = i + 1; j < boxes.Length; j++)
                Physics.IgnoreCollision(boxes[i], boxes[j]);

        if (debrisLifetime > 0f)
        {
            Destroy(debris.gameObject, debrisLifetime);
            Destroy(gameObject, debrisLifetime);
        }
    }
}