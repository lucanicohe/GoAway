using System.Collections.Generic;
using UnityEngine;

public enum IG88Route { Green, Red }
public enum IG88EdgeKind { Green, Red, Yellow }
public struct IG88Link { public int node; public IG88EdgeKind kind; }

/// <summary>
/// IG-88 droid controller. Put this on the droid's root object (the prefab already has it).
/// The GameManager spawns the droid and calls Init(). From then on the droid:
///  - PATROLS its route at walk speed, along the drawn connections of its own colour AND the yellow ones.
///  - FIRST DETECTION is by SIGHT only (clear line of sight, up to the Sight Range). The Detection Radius does nothing yet.
///    Once it has seen the player and then lost sight, it may also sense him inside the Detection Radius (no sight needed),
///    but only for 'Lose Sight Time' seconds after it LAST SAW him. After that it forgets him and must SEE him again.
///  - CHASE: walks along the waypoint track (all connections) to the closest point of the track to the player, and stops there.
///    It shoots red bolts ONLY while it can see him (never into walls). As soon as it has a clear shot it stops and fires;
///    it only walks again when the player moves.
///  - Never gets closer than 1 m to the player.
///  - Goes back to its patrol when it has not SEEN the player for 'Lose Sight Time' seconds.
///  - Never touches another droid: it waits until the other droid moves away.
///  - Has health. Headshots do more damage than body hits (see the GameManager). At 0 every rigged part breaks loose and falls to the ground.
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

    // chase stop point: the closest point on the waypoint track (any connection) to the player. A chasing droid walks there and stops.
    bool hasStopPoint;
    int stopA = -1, stopB = -1;   // the connection the stop point lies on
    float stopT;                  // 0 = at stopA, 1 = at stopB
    Vector3 stopPoint;

    float blockedTime;
    float blockLimit = 3f;
    float backoffTimer;

    float lastSightTime;    // last moment the droid SAW the player (a chase ends 'Lose Sight Time' seconds after this)
    bool hasSeenPlayer;     // true from the first sighting until the droid gives up: only then may the Detection Radius be used
    bool seesPlayer;        // this frame: player within Sight Range and a clear line of sight from the droid's head (updated in UpdateMode)
    bool clearShot;         // this frame: seesPlayer AND the bolt path from the gun tip to the player is free (updated in UpdateMode)
    float nextFireTime;
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

    // patrol zone
    int[] homeHops;                                  // connections from my home waypoint, along my own route
    static readonly List<int> tmp2 = new List<int>();

    const float ReverseMargin = 0.05f;
    const float GoalHysteresis = 0.75f;
    const float StopPointHysteresis = 0.25f;   // the stop point only jumps to another spot of the track if that is this much closer to the player
    const float ArriveEps = 0.02f;             // metres: this close to the stop point = arrived
    const float CloseDistance = 1f;     // chasing droids walk until they are this close to the player
    const float LookFadeTime = 0.2f;    // green <-> red fade
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
        for (int i = 0; i < glowMats.Count; i++)
            if (glowMats[i].m != null) Destroy(glowMats[i].m);
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
        alertBlend = 0f;
        hasSeenPlayer = false;
        seesPlayer = false;
        clearShot = false;
        hasStopPoint = false;
        lastSightTime = -999f;
        homeHops = gm.RouteHops(route, startNode);   // my home = the waypoint I spawned on
        transform.position = gm.NodePos(startNode);
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
        float goal = (mode == Mode.Chase) ? 1f : 0f;
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

    // ------------------------------------------------------------------ AI (movement happens in Update)

    void Update()
    {
        if (!ready || gm == null) return;
        float dt = Time.deltaTime;

        UpdateMode();

        Transform p = gm.Player;
        if (p != null) aimPoint = gm.PlayerAimPoint;

        if (gm.DroidPatrolOff)
        {
            walking = false;                    // Droid Patrol Off = stand still, do nothing
        }
        else
        {
            walking = Think(dt);
            UpdateFiring();
        }

        stepsPerSecond = gm.WalkSteps;
        UpdateLook(dt);
    }

    void UpdateMode()
    {
        Transform p = gm.Player;

        // Droid Off or Droid Patrol Off: never chase / target / shoot the player
        bool canChase = !gm.DroidOff && !gm.DroidPatrolOff && p != null;

        // DETECTION (is the droid aware of the player right now?)
        //  - FIRST detection is by SIGHT only: a clear line of sight closer than the Sight Range. The Detection Radius does nothing yet.
        //  - After the droid has seen the player (and maybe lost sight) it may ALSO sense him inside the Detection Radius,
        //    but only until 'Lose Sight Time' seconds after it LAST SAW him. After that it forgets him (see below) and needs sight again.
        // 'seesPlayer' / 'clearShot' are worked out once per frame and are ALSO what the stopping and shooting use.
        seesPlayer = false;
        clearShot = false;
        bool aware = false;
        if (canChase)
        {
            float d = Vector3.Distance(transform.position, p.position);
            seesPlayer = d < gm.SightRange && CanSeePlayer(p);
            if (seesPlayer)
            {
                hasSeenPlayer = true;
                lastSightTime = Time.time;
                // clear shot = it sees him AND the bolt would hit him (free path from the gun tip, not only from the head)
                clearShot = rigReady && gunTip != null && gm.HasClearShot(gunTip.t.position, gm.PlayerAimPoint);
            }
            bool radiusAllowed = hasSeenPlayer && Time.time <= lastSightTime + gm.LoseSightTime;
            aware = seesPlayer || (radiusAllowed && d < gm.DetectionRadius);
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
            aimAtTarget = true;
            nextFireTime = Time.time;   // no delay: the first shot is allowed right away (the gun only has to swing onto the player)
        }
        else if (mode == Mode.Chase && (!canChase || Time.time > lastSightTime + gm.LoseSightTime))
        {
            // player gone (or droids switched off) / not SEEN for 'Lose Sight Time' seconds: forget him, back to patrol.
            // From now on the Detection Radius is off again until the droid SEES him.
            mode = Mode.Patrol;
            aimAtTarget = false;
            hasSeenPlayer = false;
            hasStopPoint = false;
            BeginReturnToPatrol();
        }

        if (mode == Mode.Chase && p != null) target = p; // target = object tagged Player, found automatically
    }

    /// <summary>
    /// True if there is a clear line of sight from the droid's head to the centre of the player
    /// (walls and other solid objects block it, other droids don't). Same test the droid uses to decide it can shoot.
    /// </summary>
    bool CanSeePlayer(Transform p)
    {
        if (p == null) return false;
        Vector3 eye = (rigReady && head != null) ? head.t.position : transform.position + transform.up * 1.5f;
        return gm.HasClearShot(eye, gm.PlayerAimPoint);
    }

    // my patrol network = connections of my own colour + the yellow ones
    bool Matches(IG88EdgeKind kind)
    {
        return GameManager.PatrolEdgeMatches(route, kind);
    }

    void BeginReturnToPatrol()
    {
        if (toNode >= 0) returning = !Matches(gm.EdgeKindOf(fromNode, toNode));
        else returning = !gm.NodeOnPatrolNetwork(fromNode, route);

        if (returning)
        {
            goalNode = NearestRouteNode();
            if (goalNode < 0) returning = false;
        }
        else if (toNode >= 0 && gm.IsNoPatrolNode(toNode))
        {
            // chase ended while walking along my own route toward G1 / R1: turn around instead of walking on to it
            Reverse();
        }
    }

    // path length from where I am (standing on fromNode, or somewhere on the connection fromNode-toNode) to a node
    float CostToNode(int n)
    {
        if (toNode >= 0)
        {
            Vector3 pos = transform.position;
            float viaTo = Vector3.Distance(pos, gm.NodePos(toNode)) + gm.PathDist(toNode, n);
            float viaFrom = Vector3.Distance(pos, gm.NodePos(fromNode)) + gm.PathDist(fromNode, n);
            return Mathf.Min(viaTo, viaFrom);
        }
        return gm.PathDist(fromNode, n);
    }

    // nearest node of my patrol network (own colour + yellow), measured along the connections
    int NearestRouteNode()
    {
        int count = gm.NodeCount;
        int best = -1;
        float bestCost = float.MaxValue;
        for (int n = 0; n < count; n++)
        {
            if (gm.IsNoPatrolNode(n)) continue;                  // never "return to patrol" onto a blocked waypoint
            if (!gm.NodeOnPatrolNetwork(n, route)) continue;
            float cost = CostToNode(n);
            if (cost < bestCost) { bestCost = cost; best = n; }
        }
        return best;
    }

    // Chase target = the closest point on the waypoint track to the player (it can lie in the middle of a connection).
    // 'goalNode' = the end of that connection that is cheaper to reach first; the last bit is walked along the connection itself.
    void UpdateChaseGoal(Transform player)
    {
        if (player == null) return;

        int a, b; float t; Vector3 pt;
        if (!gm.ClosestTrackPoint(player.position, out a, out b, out t, out pt)) return;

        if (hasStopPoint) stopPoint = Vector3.Lerp(gm.NodePos(stopA), gm.NodePos(stopB), stopT);   // follows waypoints that move while playing
        if (!hasStopPoint ||
            Vector3.Distance(player.position, pt) + StopPointHysteresis < Vector3.Distance(player.position, stopPoint))
        {
            // the player moved: a new spot of the track is clearly closer to him (hysteresis stops flip-flopping between two spots)
            hasStopPoint = true;
            stopA = a; stopB = b; stopT = t; stopPoint = pt;
        }

        float costA = CostToNode(stopA) + Vector3.Distance(gm.NodePos(stopA), stopPoint);
        float costB = CostToNode(stopB) + Vector3.Distance(gm.NodePos(stopB), stopPoint);
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
        if (Vector3.Distance(gm.NodePos(fromNode), stopPoint) <= ArriveEps) return -1;
        return other;
    }

    // standing on fromNode: which node do we walk to next? (-1 = stay)
    int ChooseNext()
    {
        if (mode == Mode.Chase)
        {
            if (goalNode < 0) return -1;
            if (goalNode == fromNode) return FinalLegTarget();   // on the end of the stop connection: walk along it to the stop point
            return gm.NextHop(fromNode, goalNode);
        }
        if (returning)
        {
            if (goalNode < 0 || goalNode == fromNode) return -1;
            return gm.NextHop(fromNode, goalNode);
        }

        // patrol: random walk over the connections of MY route only
        tmp.Clear();
        List<IG88Link> links = gm.Links(fromNode);
        for (int i = 0; i < links.Count; i++)
            if (Matches(links[i].kind) && !gm.IsNoPatrolNode(links[i].node)) tmp.Add(links[i].node);   // G1 / R1: chase only
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
                if (!gm.NodeCrowded(tmp2[i], this)) tmp.Add(tmp2[i]);
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

    // not walking: while chasing, keep facing the player
    bool Idle(float dt)
    {
        Transform p = gm.Player;
        if (mode == Mode.Chase && p != null) FaceDirection(p.position - transform.position, dt);
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

            Vector3 toPlayer = player.position - transform.position;

            // 2) 1 m from the player: stop and keep facing him
            if (Vector3.ProjectOnPlane(toPlayer, transform.up).magnitude <= CloseDistance)
            {
                FaceDirection(toPlayer, dt);
                return false;
            }

            // 3) at the closest point of the waypoint track to the player: stop and keep facing him
            if (hasStopPoint && Vector3.Distance(transform.position, stopPoint) <= ArriveEps) return Idle(dt);
        }

        // standing on a node -> decide where to go
        if (toNode < 0)
        {
            if (backoffTimer > 0f) return Idle(dt);
            if (returning && fromNode == goalNode) returning = false;
            toNode = ChooseNext();
            if (toNode < 0) return Idle(dt);
        }

        Vector3 pos = transform.position;

        // chase, on the connection that holds the stop point: walk straight to the stop point, in whichever direction it lies
        bool onStopEdge = IsOnStopEdge();
        if (onStopEdge)
        {
            Vector3 ed = gm.NodePos(toNode) - gm.NodePos(fromNode);
            if (ed.sqrMagnitude > 1e-8f && Vector3.Dot(stopPoint - pos, ed) < 0f)
            {
                int sw = fromNode; fromNode = toNode; toNode = sw;   // the stop point is behind me: turn around (no back-off pause)
            }
        }
        else if ((mode == Mode.Chase || returning) && goalNode >= 0 && backoffTimer <= 0f)
        {
            // chase / returning: if the goal is now behind us, turn around in the middle of the connection
            float fwd = Vector3.Distance(pos, gm.NodePos(toNode)) + gm.PathDist(toNode, goalNode);
            float back = Vector3.Distance(pos, gm.NodePos(fromNode)) + gm.PathDist(fromNode, goalNode);
            if (back + ReverseMargin < fwd) Reverse();
        }

        Vector3 tp = onStopEdge ? stopPoint : gm.NodePos(toNode);
        Vector3 dir = tp - pos;
        float dist = dir.magnitude;

        // robotic: first turn on the spot, then walk in a straight line
        if (dist > 0.0001f && !FaceDirection(dir / dist, dt)) return false;

        float step = gm.WalkSpeed * dt;   // same walk speed for patrol and chase
        bool arrives = dist <= step;
        Vector3 newPos = arrives ? tp : pos + dir / dist * step;

        // never touch another droid: wait until it moves away
        if (gm.WouldCollide(this, newPos))
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

        transform.position = newPos;
        if (arrives && !onStopEdge)
        {
            prevNode = fromNode;
            fromNode = toNode;
            toNode = -1;
        }
        // arrived on the stop point in the middle of its connection: stay there (the stop check at the top keeps the droid standing)
        return true;
    }

    // ------------------------------------------------------------------ shooting (same idea as the player's blaster, red)

    void UpdateFiring()
    {
        if (mode != Mode.Chase || !gm.PlayerAlive) return;
        Transform p = gm.Player;
        if (p == null || Time.time < nextFireTime) return;

        // SIGHT is the only distance / visibility rule: the droid shoots while it sees the player (within the Sight Range),
        // from the very first frame it detects him. No sight (e.g. it only senses him through a wall) = no shooting.
        if (!seesPlayer) return;

        Vector3 origin = gunTip.t.position;
        Vector3 want = aimPoint - origin;
        if (want.sqrMagnitude < 0.01f) return;

        // only when the gun really points at the player (the arm has a limited turn range)
        Vector3 gunDir = gun.t.TransformDirection(gunFwdLocal);
        if (Vector3.Angle(gunDir, want) > maxFireAngle) return;

        // and the bolt itself must have a free path: checked from the gun tip (where the bolt starts) to the player (clearShot, worked out
        // in UpdateMode), so it never shoots into a wall even if the head sees past a corner. Done BEFORE the cooldown is set, so it fires
        // the moment the way is free.
        if (!clearShot) return;

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

    void OnDrawGizmosSelected()
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
        Gizmos.DrawLine(gunTip.t.position, aimPoint);
    }

    // Handy for your own scripts / UI buttons:
    public void SetWalking(bool value) { walking = value; }
    public void SetAiming(bool value) { aimAtTarget = value; }
}