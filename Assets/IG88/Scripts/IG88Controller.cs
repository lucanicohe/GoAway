using System.Collections.Generic;
using UnityEngine;

public enum IG88PatrolRoute { None, G6ToG5, G5ToG2, G2ToG1, R5ToR4, R4ToR2, R2ToR1 }
public enum IG88EdgeKind { Green, Red, Yellow }
public struct IG88Link { public int node; public IG88PatrolRoute route; public IG88EdgeKind kind; }

/// <summary>
/// IG-88 droid BODY. Put this on the droid's root object (the prefab already has it).
/// The GameManager spawns the droid and calls Init(). This script handles everything about the droid's body:
///  - ANIMATION: procedural walk cycle (it walks while the EnemyMoveSystem moves the droid) and aiming: waist, chest, head and gun arm
///    turn onto the player while he is being chased. No Animator needed.
///  - SHOOTING: red bolts, only while the droid sees the player and the bolt has a free path (never into walls), no firing delay.
///  - GLOW + LIGHT: green = calm, red = chase.
///  - HEALTH + DEATH: headshots do more damage than body hits (see the GameManager). At 0 every rigged part breaks loose and falls to the ground.
/// WHERE the droid walks and WHEN it detects / chases the player is decided by the EnemyMoveSystem: waypoints only, no NavMesh
/// (patrol only on its assigned segment, sight detection for patrol droids, close-range sensing for stationary guards, stop and fire when it
/// has a clear shot, never touch another droid).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("IG88/IG88 Controller")]
public class IG88Controller : MonoBehaviour, EnemyCullingManager.ICullableTarget
{
    // CullingGroup LOD & Frustum Culling state
    Transform EnemyCullingManager.ICullableTarget.Transform => transform;
    private bool isVisibleToCamera = true;
    private int distanceBand = 0; // 0 = 0-20m, 1 = 20-50m, 2 = 50-100m, 3 = 100m+
    private float aiAccumulatedDt = 0f;

    /// <summary>Invoked by EnemyCullingManager when camera visibility or distance band changes.</summary>
    public void OnCullingStateChanged(bool visible, int band)
    {
        bool wasVisible = isVisibleToCamera;
        isVisibleToCamera = visible;
        distanceBand = Mathf.Clamp(band, 0, 3);

        // When becoming visible again, immediately update look and pose
        if (!wasVisible && isVisibleToCamera)
        {
            UpdateLook(0f);
        }
    }

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

    [Header("Droid light (drag the Point Light inside the droid here)")]
    [Tooltip("Always on. Intensity comes from the GameManager, colour is green (calm) / red (chase).")]
    public Light chaseLight;

    [Header("Walk pose")]
    [Range(0f, 50f)] public float thighSwing = 26f;
    [Range(0f, 80f)] public float kneeBend = 38f;
    [Range(0f, 40f)] public float armSwing = 14f;
    [Range(0f, 0.1f)] public float bodyBob = 0.025f;
    [Range(0f, 15f)] public float torsoTwist = 4f;

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
    [Tooltip("The droid only fires when its gun points at the player within this many degrees.")]
    [Range(5f, 90f)] public float maxFireAngle = 35f;
    public bool drawGizmos = true;

    [Header("Sounds (drag the 3D Audio Sources of this prefab in here)")]
    [Tooltip("The patrol voice line. While the droid patrols it says it again 'Patrol Sound Gap' seconds after the previous one has FULLY played (never cut off, never restarted). Stops when the droid detects the player.")]
    public AudioSource patrolSound;
    [Tooltip("Seconds of silence between the END of one patrol line and the start of the next. Only counts while the droid patrols (not while chasing, not when Droid Patrol Off is on).")]
    [Min(0f)] public float patrolSoundGap = 3f;
    [Tooltip("Plays once at the moment the droid detects the player.")]
    public AudioSource detectSound;
    [Tooltip("The fire voice line, said when the droid shoots. It is never cut off or restarted: after it has FULLY played the droid waits 'Fire Sound Gap' seconds, then says it again on its next shot.")]
    public AudioSource fireSound;
    [Tooltip("Seconds of silence between the END of one fire line and the start of the next one.")]
    [Min(0f)] public float fireSoundGap = 3f;
    [Tooltip("Plays for every footstep while the droid walks (two steps per walk cycle, timed with the legs).")]
    public AudioSource footstepSound;
    [Tooltip("Plays once when the droid dies. Muffled when a wall is between the droid and the player.")]
    public AudioSource deathSound;

    [Header("Sounds: no stacking")]
    [Tooltip("Two droids never start the same short sound (footstep) closer together than this many seconds. The second one is pushed back a little instead, so the two copies don't blur into one glitchy sound. Long sounds (patrol, detect, fire line) never start while another droid is still playing the same one.")]
    [Range(0f, 0.5f)] public float sameSoundGap = 0.08f;
    [Tooltip("Random pitch change for every footstep. 0.04 = plus or minus 4%. Stops identical copies from phasing.")]
    [Range(0f, 0.2f)] public float pitchVariation = 0.04f;

    [Header("Sounds through walls")]
    [Tooltip("When a wall is between the droid and the player (the camera), the three sounds get quieter and muffled.")]
    public bool muffleThroughWalls = true;
    [Tooltip("Which layers count as walls. Default = everything solid. The droid itself, the player and other droids are always ignored. Trigger colliders are ignored too.")]
    public LayerMask wallMask = ~0;
    [Tooltip("Volume while a wall blocks the sound (1 = unchanged, 0.4 = 40%).")]
    [Range(0f, 1f)] public float muffledVolume = 0.4f;
    [Tooltip("Low pass cutoff in Hz while a wall blocks the sound. Lower = more muffled.")]
    [Range(200f, 8000f)] public float muffledCutoff = 800f;
    [Tooltip("Seconds to fade between clear and muffled.")]
    [Range(0.01f, 1f)] public float muffleFadeTime = 0.15f;

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

    // ------------------------------------------------------------------ state

    GameManager gm;
    EnemyMoveSystem move;   // waypoint movement + detection of this droid (created in Init)
    bool ready;
    bool wasChasing;        // to notice the moment a chase starts / ends
    bool silentGuard;       // stationary sentries do not play the repeating patrol voice line

    float nextFireTime;
    float patrolNextTime;   // real time (seconds) when the droid may say the patrol line again = end of the last line + Patrol Sound Gap
    float fireNextTime;     // same for the fire line
    float lastPlayEnd;      // real time when the sound PlaySound just started will have fully played

    // sounds through walls: one entry per Audio Source (volume + low pass filter get changed while a wall blocks the sound)
    class SoundMuffle
    {
        public AudioSource src;
        public AudioLowPassFilter lowPass;
        public float baseVolume;
        public float basePitch;
    }
    readonly List<SoundMuffle> muffles = new List<SoundMuffle>();

    // one slot per kind of sound, shared by ALL droids: remembers until when that sound is "taken" (real time, so it also survives Play Mode restarts)
    enum DroidSound { Patrol, Detect, Fire, Step, Death }
    static readonly float[] soundBusyUntil = new float[5];
    const float MaxSoundDelay = 0.3f;   // a sound that would have to wait longer than this is skipped
    const float HalfPi = Mathf.PI * 0.5f;
    static readonly RaycastHit[] wallHits = new RaycastHit[16];
    bool wallBlocked;
    float muffleBlend;      // 0 = clear, 1 = muffled
    float wallCheckTimer;
    Vector3 aimPoint;       // where the droid aims (centre of the player)

    // look (light + glow)
    float alertBlend;       // 0 = calm (green), 1 = chase (red)
    // one entry per glow material instance of this droid
    class GlowMat
    {
        public Material m;
        public int emisColorId = -1;   // emission colour = the HDR tint next to the Emission Map
        public float emisIntensity;    // HDR strength taken from the material's original emission colour
        public int baseColorId = -1;   // base colour (_BaseColor in URP, _Color in built-in)
        public float baseBrightness;
        public float baseAlpha;
    }
    readonly List<GlowMat> glowMats = new List<GlowMat>();
    static readonly string[] EmissionColorNames = { "_EmissionColor", "_EmissiveColor", "_Emission", "_EmissionTint" };
    static readonly string[] EmissionMapNames = { "_EmissionMap", "_EmissiveColorMap" };
    static readonly string[] BaseColorNames = { "_BaseColor", "_Color" };
    static readonly string[] BaseMapNames = { "_BaseMap", "_MainTex" };
    static readonly Dictionary<Texture, Texture2D> maskMapCache = new Dictionary<Texture, Texture2D>();
    static bool glowLogged;

    const float LookFadeTime = 0.2f;    // green <-> red fade

    /// <summary>Direction the droid faces (world space, flattened).</summary>
    public Vector3 RestForward
    {
        get { return Vector3.ProjectOnPlane(transform.TransformDirection(restForwardLocal), transform.up).normalized; }
    }

    /// <summary>Where the droid looks from (the head bone). The EnemyMoveSystem uses it for the line-of-sight test.</summary>
    public Vector3 EyePosition
    {
        get { return (rigReady && head != null) ? head.t.position : transform.position + transform.up * 1.5f; }
    }

    /// <summary>True when the rig (and so the gun) was found.</summary>
    public bool HasGun { get { return rigReady && gunTip != null; } }

    /// <summary>Where the bolts start (the gun tip). Only valid when HasGun is true.</summary>
    public Vector3 GunTipPosition { get { return gunTip.t.position; } }

    void Awake() { DetectRig(); EnsureHitCollider(); }

    void OnDestroy()
    {
        if (gm != null) gm.Unregister(this);
        for (int i = 0; i < glowMats.Count; i++)
            if (glowMats[i].m != null) Destroy(glowMats[i].m);
    }

    /// <summary>Called by the GameManager right after spawning.</summary>
    public void Init(GameManager manager, IG88PatrolRoute patrolRoute, int startNode, bool standingGuard = false)
    {
        gm = manager;
        silentGuard = standingGuard;
        aimAtTarget = false;
        walking = false;
        IsDead = false;
        health = gm.DroidHealth;
        alertBlend = 0f;
        wasChasing = false;
        patrolNextTime = Time.realtimeSinceStartup + patrolSoundGap;   // first patrol line comes one gap after the spawn
        fireNextTime = 0f;                                             // the first shot may say the fire line right away
        PrepareSound(patrolSound);
        PrepareSound(detectSound);
        PrepareSound(fireSound);
        PrepareSound(footstepSound);
        PrepareSound(deathSound);
        SetupMuffle(patrolSound);
        SetupMuffle(detectSound);
        SetupMuffle(fireSound);
        SetupMuffle(footstepSound);
        SetupMuffle(deathSound);
        wallBlocked = false;
        muffleBlend = 0f;
        wallCheckTimer = 0f;
        move = new EnemyMoveSystem(this, gm, patrolRoute, startNode, standingGuard);   // puts the droid on its start waypoint
        if (chaseLight != null) chaseLight.shadows = LightShadows.None;   // a point light with shadows = 6 shadow maps each (the shadow atlas warning)
        SetupGlow();
        UpdateLook(0f);
        ready = true;
    }

    // ------------------------------------------------------------------ light + glow material

    static bool IsGlowMaterial(Material m, Material src)
    {
        if (m == src) return true;
        string n = m.name;
        if (n.EndsWith(" (Instance)")) n = n.Substring(0, n.Length - 11);
        return n == src.name;
    }

    static string FindName(Material m, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
            if (m.HasProperty(names[i])) return names[i];
        return null;
    }

    // custom shaders (Shader Graph): any colour property with "emi" or "glow" in its name
    static string FindColorByKeyword(Material m, string a, string b)
    {
        Shader sh = m.shader;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
            string n = sh.GetPropertyName(i);
            string low = n.ToLowerInvariant();
            if (low.Contains(a) || low.Contains(b)) return n;
        }
        return null;
    }

    static string DescribeShader(Material m)
    {
        Shader sh = m.shader;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("shader '").Append(sh.name).Append("', colour properties: ");
        for (int i = 0; i < sh.GetPropertyCount(); i++)
            if (sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color) sb.Append(sh.GetPropertyName(i)).Append(' ');
        sb.Append(", texture properties: ");
        for (int i = 0; i < sh.GetPropertyCount(); i++)
            if (sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture) sb.Append(sh.GetPropertyName(i)).Append(' ');
        return sb.ToString();
    }

    // Every droid gets its own copy of the glow material (the asset is never changed).
    // The glow colour is NOT taken from what the material looks like now: green and red are set explicitly
    // (colours from the GameManager). Texture maps are turned into brightness masks, so the tint decides the colour.
    void SetupGlow()
    {
        glowMats.Clear();
        Material src = gm.DroidGlowMaterial;
        if (src == null) return;

        string emisColorName = FindName(src, EmissionColorNames) ?? FindColorByKeyword(src, "emi", "glow");
        if (emisColorName == null)
        {
            Debug.LogWarning("IG88Controller: can't find an emission colour on the glow material. " + DescribeShader(src), this);
            return;
        }
        string emisMapName = FindName(src, EmissionMapNames);
        string baseColorName = FindName(src, BaseColorNames);
        string baseMapName = FindName(src, BaseMapNames);

        Renderer[] rs = GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < rs.Length; r++)
        {
            Material[] shared = rs[r].sharedMaterials;
            bool changed = false;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null || !IsGlowMaterial(shared[i], src)) continue;

                GlowMat g = new GlowMat();
                g.m = new Material(shared[i]);
                g.m.name = src.name + " (droid)";
                g.m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

                // emission: strength from the original, colour from the GameManager, map -> brightness mask
                g.emisColorId = Shader.PropertyToID(emisColorName);
                Texture eMap = emisMapName != null ? g.m.GetTexture(emisMapName) : null;
                g.emisIntensity = g.m.GetColor(g.emisColorId).maxColorComponent;
                if (g.emisIntensity < 0.01f) g.emisIntensity = (eMap != null) ? 1f : 2f;
                if (eMap != null) g.m.SetTexture(emisMapName, GetMaskMap(eMap));

                // base colour / base map: same idea, so no red-orange surface colour is left over
                if (baseColorName != null)
                {
                    g.baseColorId = Shader.PropertyToID(baseColorName);
                    Color b = g.m.GetColor(g.baseColorId);
                    g.baseBrightness = b.maxColorComponent;
                    g.baseAlpha = b.a;
                    Texture bMap = baseMapName != null ? g.m.GetTexture(baseMapName) : null;
                    if (bMap != null) g.m.SetTexture(baseMapName, GetMaskMap(bMap));
                }

                shared[i] = g.m;
                glowMats.Add(g);
                changed = true;
            }
            if (changed) rs[r].sharedMaterials = shared;
        }

        if (!glowLogged)
        {
            glowLogged = true;
            Debug.Log("[IG88] glow: " + glowMats.Count + " material slot(s) found. Using emission colour '" + emisColorName + "', emission map '" +
                      (emisMapName ?? "none") + "', base colour '" + (baseColorName ?? "none") + "'. " + DescribeShader(src));
        }
        if (glowMats.Count == 0)
            Debug.LogWarning("IG88Controller: the Droid Glow Material from the GameManager is not used by any renderer of this droid, so its glow can't change.", this);
    }

    // Brightness mask of a texture: every pixel becomes grey with the value (max of R,G,B) of the original.
    // A green glow map, a red one or a white one all give the same mask, the tint colour then decides the colour.
    // Made once and shared by all droids. Works with non-readable textures (goes through the GPU).
    static Texture GetMaskMap(Texture src)
    {
        Texture2D cached;
        if (maskMapCache.TryGetValue(src, out cached) && cached != null) return cached;

        int w = Mathf.Clamp(src.width, 4, 1024), h = Mathf.Clamp(src.height, 4, 1024);
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color32[] px = tex.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            byte v = (byte)Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
            px[i].r = v; px[i].g = v; px[i].b = v;
        }
        tex.SetPixels32(px);
        tex.Apply();
        tex.name = src.name + " (mask)";
        maskMapCache[src] = tex;
        return tex;
    }

    // light stays on at a constant intensity, only the colour changes; the glow material does the same green -> red
    void UpdateLook(float dt)
    {
        float goal = (move != null && move.Chasing) ? 1f : 0f;
        alertBlend = (dt <= 0f) ? goal : Mathf.MoveTowards(alertBlend, goal, dt / LookFadeTime);
        Color state = Color.Lerp(gm.DroidGreen, gm.DroidRed, alertBlend);

        if (chaseLight != null)
        {
            chaseLight.enabled = true;
            chaseLight.intensity = gm.DroidLightIntensity;
            chaseLight.color = state;
        }

        for (int i = 0; i < glowMats.Count; i++)
        {
            GlowMat g = glowMats[i];
            if (g.m == null) continue;
            float ei = g.emisIntensity;
            g.m.SetColor(g.emisColorId, new Color(state.r * ei, state.g * ei, state.b * ei, 1f));
            if (g.baseColorId >= 0)
            {
                float bb = g.baseBrightness;
                g.m.SetColor(g.baseColorId, new Color(state.r * bb, state.g * bb, state.b * bb, g.baseAlpha));
            }
            g.m.EnableKeyword("_EMISSION");   // Unity: the keyword must be enabled AFTER the colour is set
        }
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

    /// <summary>
    /// True if a hit counts as a headshot. Two ways, whichever the prefab allows:
    ///  1) the collider that was hit belongs to the head bone (a separate head collider), or
    ///  2) the droid has one big collider: the hit point is at or above the head bone (minus 'Head Hit Margin' from the GameManager).
    /// </summary>
    public bool IsHeadHit(Collider hitCollider, Vector3 hitPoint)
    {
        if (IsDead || gm == null || !rigReady || head == null || head.t == null) return false;
        if (hitCollider != null && hitCollider.transform.IsChildOf(head.t)) return true;
        float height = Vector3.Dot(hitPoint - head.t.position, transform.up);   // metres above (+) / below (-) the head bone
        return height >= -gm.HeadHitMargin;
    }

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
        // a dead droid is silent: no talking, no walking (the last shot sound may ring out), then the death sound
        if (patrolSound != null) patrolSound.Stop();
        if (detectSound != null) detectSound.Stop();
        if (footstepSound != null) footstepSound.Stop();        // also cancels a step that was still waiting to start
        UpdateSoundMuffle(0f, true);                            // wall between droid and player right now? the death sound follows it
        PlaySound(deathSound, DroidSound.Death, true);
        if (chaseLight != null) chaseLight.enabled = false;     // a dead droid doesn't glow
        for (int i = 0; i < glowMats.Count; i++)
        {
            GlowMat g = glowMats[i];
            if (g.m == null) continue;
            g.m.SetColor(g.emisColorId, Color.black);
            if (g.baseColorId >= 0) g.m.SetColor(g.baseColorId, new Color(0.08f, 0.08f, 0.08f, g.baseAlpha));
        }
        if (gm != null) gm.Unregister(this);                    // a dead droid no longer blocks the living ones
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

    // ------------------------------------------------------------------ update: the EnemyMoveSystem decides, the body animates + shoots

    void Update()
    {
        if (!ready || gm == null || move == null) return;
        float dt = Time.deltaTime;

        // Detection and line-of-sight are gameplay-critical, so keep them live at every distance.
        // CullingGroup distance bands may reduce patrol movement frequency, but must never delay noticing or shooting at the player.
        move.RefreshAwareness();

        // CullingGroup distance LOD applies to patrol movement only:
        // Band 0 (0-20m):   full frame rate
        // Band 1 (20-50m):  ~10 Hz (interval 0.1s)
        // Band 2 (50-100m): ~2 Hz (interval 0.5s)
        // Band 3 (100m+):   ~1 Hz (interval 1.0s)
        float interval = distanceBand == 0 ? 0f : (distanceBand == 1 ? 0.1f : (distanceBand == 2 ? 0.5f : 1.0f));
        aiAccumulatedDt += dt;
        // A chasing droid moves every frame too; patrol movement alone uses the distance interval.
        bool shouldUpdateMovement = interval <= 0f || aiAccumulatedDt >= interval || move.Chasing;

        if (shouldUpdateMovement)
        {
            // Waypoint movement uses accumulated dt so speed and distance stay correct at the reduced update rate.
            walking = move.Tick(aiAccumulatedDt);
            aiAccumulatedDt = 0f;
        }

        // hard stop any playing footstep the moment the droid stops moving
        if (!walking && footstepSound != null && footstepSound.isPlaying)
            footstepSound.Stop();

        // a chase starts / ends: aim at the player (or not), and the first shot is allowed right away
        bool chasing = move.Chasing;
        if (chasing != wasChasing)
        {
            wasChasing = chasing;
            aimAtTarget = chasing;
            patrolNextTime = Time.realtimeSinceStartup + patrolSoundGap;   // after a chase the next patrol line comes a full gap later
            if (chasing)
            {
                nextFireTime = Time.time;            // no delay: the gun only has to swing onto the player
                if (patrolSound != null) patrolSound.Stop();
                PlaySound(detectSound, DroidSound.Detect, false);
            }
        }

        // Nonessential audio muffle raycasts suspended when far away (>50m)
        if (chasing || distanceBand < 2)
        {
            UpdatePatrolSound(chasing);
            UpdateSoundMuffle(dt);
        }

        Transform p = gm.Player;
        if (chasing && p != null) target = p;        // target = object tagged Player, found automatically
        if (p != null) aimPoint = gm.PlayerAimPoint;

        // Shooting does NOT depend on the CullingGroup (UpdateFiring only does something while the droid chases and sees the player).
        // A wrong / late distance band used to switch the shooting, and with it the fire sound, off for good.
        if (!gm.DroidPatrolOff) UpdateFiring();

        stepsPerSecond = gm.WalkSteps;

        // Visual effects (glow material emission + point light):
        // Only update when visible to camera or close!
        if (isVisibleToCamera || distanceBand == 0)
        {
            UpdateLook(dt);
        }
    }

    // ------------------------------------------------------------------ shooting (same idea as the player's blaster, red)

    void UpdateFiring()
    {
        if (move == null || !move.Chasing || !gm.PlayerAlive) return;
        Transform p = gm.Player;
        if (p == null || Time.time < nextFireTime) return;

        // SIGHT is the only distance / visibility rule: the droid shoots while it sees the player (within the Sight Range),
        // from the very first frame it detects him. No sight (e.g. it only senses him through a wall) = no shooting.
        if (!move.SeesPlayer) return;

        Vector3 origin = gunTip.t.position;
        Vector3 want = aimPoint - origin;
        if (want.sqrMagnitude < 0.01f) return;

        // only when the gun really points at the player (the arm has a limited turn range)
        Vector3 gunDir = gun.t.TransformDirection(gunFwdLocal);
        if (Vector3.Angle(gunDir, want) > maxFireAngle) return;

        // and the bolt itself must have a free path: checked from the gun tip (where the bolt starts) to the player (ClearShot, worked out
        // by the EnemyMoveSystem), so it never shoots into a wall even if the head sees past a corner. Done BEFORE the cooldown is set, so it fires
        // the moment the way is free.
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
        SayFireLine();                                 // the fire voice line: paced, see SayFireLine
        gm.FireDroidBolt(origin, dir);
    }

    // ------------------------------------------------------------------ sounds

    // the three Audio Sources sit on the prefab: they must never loop or start on their own
    static void PrepareSound(AudioSource s)
    {
        if (s == null) return;
        s.loop = false;
        s.playOnAwake = false;
        s.Stop();
    }

    // Patrol line: while the droid patrols it says the line, and says it again 'Patrol Sound Gap' seconds AFTER it has fully played.
    void UpdatePatrolSound(bool chasing)
    {
        if (patrolSound == null) return;
        float now = Time.realtimeSinceStartup;
        if (silentGuard)
        {
            if (patrolSound.isPlaying) patrolSound.Stop();
            patrolNextTime = now + patrolSoundGap;
            return;
        }
        if (chasing || gm.DroidPatrolOff)
        {
            patrolNextTime = now + patrolSoundGap;       // not patrolling: after it the next line comes a full gap later
            return;
        }
        if (now < patrolNextTime || patrolSound.isPlaying) return;   // still inside the gap, or the last line has not finished yet
        if (PlaySound(patrolSound, DroidSound.Patrol, false))
            patrolNextTime = lastPlayEnd + patrolSoundGap;           // the gap starts when the line has FULLY played
        // blocked by another droid's patrol line = tries again next frame
    }

    // Fire line: said on a shot, never cut off or restarted. After it has fully played the droid waits 'Fire Sound Gap' seconds,
    // then says it again on its next shot. Shots in between are silent.
    void SayFireLine()
    {
        if (fireSound == null) return;
        float now = Time.realtimeSinceStartup;
        if (now < fireNextTime || fireSound.isPlaying) return;
        if (PlaySound(fireSound, DroidSound.Fire, false))
            fireNextTime = lastPlayEnd + fireSoundGap;
    }

    // Starts a droid sound without stacking it on the same sound of another droid.
    // shortSound = footstep / death (random pitch, only a tiny gap is kept free). Not short = patrol / detect / fire line (the sound is kept free until it has finished).
    // Returns false when the sound was skipped.
    bool PlaySound(AudioSource s, DroidSound kind, bool shortSound)
    {
        if (s == null || s.clip == null) return false;

        float pitch = BasePitch(s);
        if (shortSound && pitchVariation > 0f) pitch *= 1f + Random.Range(-pitchVariation, pitchVariation);

        float now = Time.realtimeSinceStartup;
        float start = Mathf.Max(now, soundBusyUntil[(int)kind]);   // another droid is playing this sound = start right after it
        float delay = start - now;
        if (delay > MaxSoundDelay) return false;                   // would have to wait too long: skip it
        float length = s.clip.length / Mathf.Max(0.1f, pitch);
        soundBusyUntil[(int)kind] = start + (shortSound ? sameSoundGap : length);
        lastPlayEnd = start + length;                              // when this sound will have fully played

        s.pitch = pitch;
        if (delay > 0.001f) s.PlayDelayed(delay);
        else s.Play();
        return true;
    }

    float BasePitch(AudioSource s)
    {
        for (int i = 0; i < muffles.Count; i++)
            if (muffles[i].src == s) return muffles[i].basePitch;
        return s.pitch;
    }

    // true when the walk phase passed 'x' between the last frame and this one (phase wraps at 2 pi)
    static bool PhaseCrossed(float a, float b, float x)
    {
        if (b >= a) return a < x && b >= x;
        return a < x || b >= x;
    }

    // each Audio Source gets a low pass filter (off while the sound is clear) and remembers its normal volume
    void SetupMuffle(AudioSource s)
    {
        if (s == null) return;
        for (int i = 0; i < muffles.Count; i++)
            if (muffles[i].src == s) return;                     // same source dragged into two fields

        SoundMuffle m = new SoundMuffle();
        m.src = s;
        m.baseVolume = s.volume;
        m.basePitch = s.pitch;
        m.lowPass = s.GetComponent<AudioLowPassFilter>();
        if (m.lowPass == null) m.lowPass = s.gameObject.AddComponent<AudioLowPassFilter>();
        m.lowPass.cutoffFrequency = 22000f;
        m.lowPass.enabled = false;
        muffles.Add(m);
    }

    // wall between droid and player = quieter + muffled, faded in and out smoothly
    void UpdateSoundMuffle(float dt, bool snap = false)
    {
        if (!muffleThroughWalls || muffles.Count == 0) return;

        wallCheckTimer -= dt;
        if (snap || wallCheckTimer <= 0f)
        {
            wallCheckTimer = 0.1f;                               // 10 checks per second is plenty
            wallBlocked = IsSoundBlocked();
        }

        // snap = no fade, jump straight to clear / muffled (used at the moment of death, the Update loop stops after that)
        if (snap) muffleBlend = wallBlocked ? 1f : 0f;
        else muffleBlend = Mathf.MoveTowards(muffleBlend, wallBlocked ? 1f : 0f, dt / muffleFadeTime);
        float cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(muffledCutoff), muffleBlend));   // fades evenly to the ear
        float vol = Mathf.Lerp(1f, muffledVolume, muffleBlend);
        for (int i = 0; i < muffles.Count; i++)
        {
            SoundMuffle m = muffles[i];
            if (m.src == null || m.lowPass == null) continue;
            m.src.volume = m.baseVolume * vol;
            m.lowPass.enabled = muffleBlend > 0.001f;
            m.lowPass.cutoffFrequency = cutoff;
        }
    }

    // line from the droid's head to the player's ears (the main camera). Anything solid in between that is not the droid, the player
    // or another droid counts as a wall.
    bool IsSoundBlocked()
    {
        UnityEngine.Camera cam = UnityEngine.Camera.main;   // full name on purpose: a script called "Camera" in the project would hide Unity's Camera
        Transform ear = cam != null ? cam.transform : (gm != null ? gm.Player : null);
        if (ear == null) return false;

        Vector3 from = EyePosition;
        Vector3 d = ear.position - from;
        float dist = d.magnitude;
        if (dist < 0.1f) return false;

        int n = Physics.RaycastNonAlloc(from, d / dist, wallHits, dist, wallMask, QueryTriggerInteraction.Ignore);
        Transform player = gm != null ? gm.Player : null;
        for (int i = 0; i < n; i++)
        {
            Transform h = wallHits[i].collider.transform;
            if (h.IsChildOf(transform)) continue;                            // this droid
            if (player != null && h.IsChildOf(player)) continue;             // the player
            if (h.GetComponentInParent<IG88Controller>() != null) continue;  // other droids
            return true;
        }
        return false;
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
        // When offscreen and not right next to the player, skip expensive procedural skeletal bone manipulation
        // (not while chasing: the gun has to keep aiming at the player or UpdateFiring never sees it pointing at him)
        if (!isVisibleToCamera && distanceBand > 0 && !aimAtTarget) return;

        if (!rigReady && !DetectRig()) return;
        float dt = Time.deltaTime;

        ResetPose();

        walkWeight = Mathf.MoveTowards(walkWeight, walking ? 1f : 0f, dt / 0.3f);
        if (walkWeight > 0f)
        {
            // only advance the walk cycle + play footsteps while actually walking
            // (stops residual phase crossings from firing footsteps while standing still during the fade-out)
            if (walking)
            {
                float phaseBefore = phase;
                phase = Mathf.Repeat(phase + stepsPerSecond * 2f * Mathf.PI * dt, 2f * Mathf.PI);

                // a foot lands when the leg is fully forward and starts to swing back: phase = 90 degrees (left) and 270 degrees (right)
                if (walkWeight >= 0.5f && footstepSound != null &&
                    (PhaseCrossed(phaseBefore, phase, HalfPi) || PhaseCrossed(phaseBefore, phase, HalfPi * 3f)))
                    PlaySound(footstepSound, DroidSound.Step, true);
            }
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
        Vector3 tp = aimPoint; // centre of the player

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

    void OnDrawGizmos()
    {
        if (!drawGizmos || !rigReady) return;

        // yellow plate = where the head starts: a hit at or above it is a headshot (Head Hit Margin in the GameManager)
        if (gm != null && head != null && head.t != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.matrix = Matrix4x4.TRS(head.t.position - transform.up * gm.HeadHitMargin, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.5f, 0.002f, 0.5f));
            Gizmos.matrix = Matrix4x4.identity;
        }

        if (target == null || gunTip == null) return;
        Gizmos.color = aimAtTarget ? Color.red : Color.gray;
        Gizmos.DrawLine(gunTip.t.position, target.position);
    }

    // Handy for your own scripts / UI buttons:
    public void SetWalking(bool value) { walking = value; }
    public void SetAiming(bool value) { aimAtTarget = value; }
}
