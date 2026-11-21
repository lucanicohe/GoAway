using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// First person blaster. Left mouse (or right trigger) fires a green bolt from the barrel towards the crosshair.
///
///   muzzle flash  -> BlasterMuzzleFlash   (3D flame meshes)
///   recoil        -> BlasterRecoil        (gun group kicks and springs back)
///   bolt          -> BlasterBolt          (pooled, visible, flies at Bolt Speed)
///   hit           -> sparks + impact flash, debug log, damage through IBlasterDamageable, push on rigidbodies
///
/// Everything that flies or sparks is built once in Start() and recycled, nothing is instantiated while shooting.
/// That also means a thermal / material swapper that runs over all renderers sees every blaster object.
/// Nothing in the existing player is changed. Own colliders are ignored by hit detection (no layers needed).
/// </summary>
[AddComponentMenu("Blaster/Blaster Weapon")]
public class BlasterWeapon : MonoBehaviour
{
    [Header("References (found automatically if left empty)")]
    [Tooltip("Top of the player rig. Everything under it is ignored by bolts and by the aim ray.")]
    [SerializeField] private Transform ownerRoot;
    [Tooltip("Camera the crosshair ray comes from.")]
    [SerializeField] private UnityEngine.Camera aimCamera;
    [Tooltip("Barrel tip. Bolts start here.")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private BlasterMuzzleFlash muzzleFlash;
    [SerializeField] private BlasterRecoil recoil;



    [Header("Blaster Audio")]
    [SerializeField] private AudioSource blasterAudio;
    [SerializeField] private AudioClip blasterSound;
    [SerializeField, Range(0f, 1f)] private float shotVolume = 1f;

    [Header("Firing")]
    [SerializeField] private bool automatic = true;
    [Tooltip("Shots per second.")]
    [SerializeField, Min(0.1f)] private float fireRate = 7f;
    [Tooltip("Damage sent to IBlasterDamageable on a hit.")]
    [SerializeField] private float damage = 25f;
    [Tooltip("Push on rigidbodies that get hit.")]
    [SerializeField] private float impactForce = 6f;
    [Tooltip("Random spread in degrees.")]
    [SerializeField] private float spreadDegrees = 0.35f;
    [SerializeField] private float maxRange = 200f;
    [Tooltip("If the target is closer than this the bolt flies straight ahead instead of crossing over.")]
    [SerializeField] private float minAimDistance = 3f;

    [Header("Bolt")]
    [Tooltip("Metres per second.")]
    [SerializeField] private float boltSpeed = 90f;
    [SerializeField] private float boltLength = 0.7f;
    [SerializeField] private float boltRadius = 0.028f;
    [Tooltip("Thickness used for hit detection.")]
    [SerializeField] private float boltCastRadius = 0.03f;
    [SerializeField] private int boltPoolSize = 16;
    [SerializeField] private bool boltLight = true;
    [SerializeField] private Color boltLightColor = new Color(0.3f, 1f, 0.35f, 1f);
    [SerializeField] private float boltLightIntensity = 2f;
    [SerializeField] private float boltLightRange = 5f;

    [Header("Bolt materials (made in code if empty)")]
    [SerializeField] private Material boltCoreMaterial;
    [SerializeField] private Material boltGlowMaterial;
    [SerializeField] private Material boltGlowOuterMaterial;

    [Header("Hit sparks")]
    [SerializeField] private int sparkBurstPoolSize = 12;
    [SerializeField] private int sparksPerHit = 16;
    [SerializeField] private float sparkSpeedMin = 2.5f;
    [SerializeField] private float sparkSpeedMax = 9f;
    [SerializeField] private float sparkLifeMin = 0.22f;
    [SerializeField] private float sparkLifeMax = 0.55f;
    [SerializeField] private float sparkGravity = 9.81f;
    [SerializeField] private float sparkWidth = 0.012f;
    [SerializeField] private float sparkLength = 0.14f;
    [SerializeField] private float impactFlashSize = 0.22f;
    [SerializeField] private bool impactLight = true;
    [SerializeField] private float impactLightIntensity = 3f;
    [SerializeField] private float impactLightRange = 3.5f;
    [SerializeField] private Material sparkMaterialGreen;
    [SerializeField] private Material sparkMaterialWhite;
    [SerializeField] private Material impactFlashMaterial;

    [Header("Debug")]
    [Tooltip("Writes what was hit to the Console.")]
    [SerializeField] private bool logHits = true;
    [SerializeField] private bool logMisses = false;
    [Tooltip("Draws the shot path in the Scene view for a moment.")]
    [SerializeField] private bool drawShotLines = false;

    /// <summary>Raised on every shot.</summary>
    public event Action<BlasterWeapon> Fired;
    /// <summary>Raised on every hit of this weapon.</summary>
    public event Action<BlasterHitInfo> Hit;
    /// <summary>Raised on every hit of any weapon (handy for a HUD or score).</summary>
    public static event Action<BlasterHitInfo> AnyHit;

    public int ShotCount { get; private set; }
    public int HitCount { get; private set; }
    public BlasterHitInfo LastHit { get; private set; }
    public bool ThermalLook { get; private set; }

    /// <summary>Parent of every pooled bolt. A thermal script can use it to find the bolts.</summary>
    public Transform BoltPoolRoot { get { return boltRoot; } }
    /// <summary>Parent of every pooled spark burst.</summary>
    public Transform SparkPoolRoot { get { return sparkRoot; } }

    private Transform boltRoot;
    private Transform sparkRoot;
    private readonly List<BlasterBolt> bolts = new List<BlasterBolt>();
    private readonly List<BlasterHitSparks> bursts = new List<BlasterHitSparks>();
    private readonly List<Renderer> glowRenderers = new List<Renderer>();
    private readonly List<Light> boltLights = new List<Light>();
    private int nextBolt;
    private int nextBurst;
    private float nextFireTime;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[24];



    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        AnyHit = null;
    }

    private void Awake()
    {
        if (ownerRoot == null)
            ownerRoot = transform.root;
        if (aimCamera == null)
            aimCamera = ownerRoot.GetComponentInChildren<UnityEngine.Camera>();
        if (aimCamera == null)
            aimCamera = UnityEngine.Camera.main;
        if (muzzle == null)
        {
            Transform[] all = ownerRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "MuzzlePoint")
                {
                    muzzle = all[i];
                    break;
                }
            }
        }
        if (muzzle == null)
            muzzle = transform;
        if (muzzleFlash == null)
            muzzleFlash = ownerRoot.GetComponentInChildren<BlasterMuzzleFlash>(true);
        if (recoil == null)
            recoil = ownerRoot.GetComponentInChildren<BlasterRecoil>(true);
    }

    private void Start()
    {
        BuildPools();
    }

    private void OnDestroy()
    {
        if (boltRoot != null)
            Destroy(boltRoot.gameObject);
        if (sparkRoot != null)
            Destroy(sparkRoot.gameObject);
    }

    private void Update()
    {
        bool want = automatic ? FireHeld() : FirePressed();
        if (want && Time.time >= nextFireTime)
            Fire();
    }

    // ------------------------------------------------------------------ input

    private static bool FireHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            return true;
        if (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed)
            return true;
        return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(0);
#else
        return false;
#endif
    }

    private static bool FirePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;
        if (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame)
            return true;
        return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0);
#else
        return false;
#endif
    }

    // ------------------------------------------------------------------ firing

    /// <summary>Fire one shot now (also usable from your own code / AI).</summary>
    public bool Fire()
    {
        if (muzzle == null || aimCamera == null)
            return false;

        nextFireTime = Time.time + 1f / Mathf.Max(0.1f, fireRate);

        // 1. where is the crosshair pointing?
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit aimHit;
        bool aimFound = CastIgnoringOwner(ray.origin, ray.direction, maxRange, 0f, out aimHit);
        Vector3 aimPoint = aimFound ? aimHit.point : ray.origin + ray.direction * maxRange;

        // 2. bolt goes from the barrel to that point
        Vector3 origin = muzzle.position;
        Vector3 dir = aimPoint - origin;
        if (dir.magnitude < minAimDistance)
            dir = ray.direction;
        dir.Normalize();

        if (spreadDegrees > 0f)
        {
            Vector2 s = UnityEngine.Random.insideUnitCircle * spreadDegrees;
            dir = Quaternion.LookRotation(dir) * Quaternion.Euler(-s.y, s.x, 0f) * Vector3.forward;
        }

        if (drawShotLines)
            Debug.DrawLine(origin, aimPoint, Color.green, 0.5f);

        // 3. target closer than the barrel tip (standing against a wall): hit it straight away
        float muzzleDepth = Vector3.Dot(origin - ray.origin, ray.direction);
        if (aimFound && aimHit.distance <= muzzleDepth + 0.05f)
        {
            RegisterHit(aimHit, ray.direction);
        }
        else
        {
            BlasterBolt bolt = NextBolt();
            if (bolt != null)
                bolt.Launch(origin, dir, boltSpeed, maxRange);
            if (logMisses && !aimFound)
                Debug.Log("[Blaster] shot fired, nothing in range", this);
        }

        // 4. flash, recoil, audio, event
        if (muzzleFlash != null)
            muzzleFlash.Fire();

        if (recoil != null)
            recoil.Kick();

        if (blasterAudio != null && blasterSound != null)
            blasterAudio.PlayOneShot(blasterSound, shotVolume);

        ShotCount++;
        if (Fired != null)
            Fired(this);
        return true;
    }

    // ------------------------------------------------------------------ hit detection (used by BlasterBolt)

    private bool IsOwner(Transform t)
    {
        return ownerRoot != null && t.IsChildOf(ownerRoot);
    }

    /// <summary>Raycast / spherecast that skips the player's own colliders and triggers. Returns the nearest valid hit.</summary>
    public bool CastIgnoringOwner(Vector3 origin, Vector3 direction, float distance, float radius, out RaycastHit best)
    {
        int n;
        if (radius > 0.0001f)
            n = Physics.SphereCastNonAlloc(origin, radius, direction, hitBuffer, distance, ~0, QueryTriggerInteraction.Ignore);
        else
            n = Physics.RaycastNonAlloc(origin, direction, hitBuffer, distance, ~0, QueryTriggerInteraction.Ignore);

        best = default(RaycastHit);
        float bestDistance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider == null || IsOwner(h.collider.transform))
                continue;
            if (h.distance < bestDistance)
            {
                bestDistance = h.distance;
                best = h;
                found = true;
            }
        }
        return found;
    }

    /// <summary>Everything that happens when a bolt hits something.</summary>
    public void RegisterHit(RaycastHit hit, Vector3 direction)
    {
        IBlasterDamageable target = hit.collider.GetComponentInParent<IBlasterDamageable>();

        BlasterHitInfo info = new BlasterHitInfo();
        info.collider = hit.collider;
        info.gameObject = hit.collider.gameObject;
        info.point = hit.point;
        info.normal = hit.normal;
        info.direction = direction;
        info.distance = aimCamera != null ? Vector3.Distance(aimCamera.transform.position, hit.point) : hit.distance;
        info.damage = damage;
        info.damageableFound = target != null;
        info.weapon = this;

        // push physics objects
        Rigidbody rb = hit.rigidbody;
        if (rb != null && !rb.isKinematic)
            rb.AddForceAtPosition(direction.normalized * impactForce, hit.point, ForceMode.Impulse);

        PlaySparks(hit.point, hit.normal);

        if (logHits)
        {
            Debug.Log("[Blaster] HIT \"" + GetPath(hit.collider.transform) + "\"" +
                      "  tag: " + hit.collider.tag +
                      "  dist: " + info.distance.ToString("F1") + " m" +
                      "  point: " + hit.point.ToString("F2") +
                      "  rigidbody: " + (rb != null ? "yes" : "no") +
                      "  damageable: " + (target != null ? "yes (" + damage.ToString("F0") + " dmg)" : "no"),
                      hit.collider);
        }

        if (target != null)
            target.TakeBlasterDamage(info);

        HitCount++;
        LastHit = info;
        if (Hit != null)
            Hit(info);
        if (AnyHit != null)
            AnyHit(info);
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        int guard = 0;
        while (t.parent != null && guard++ < 6)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private void PlaySparks(Vector3 point, Vector3 normal)
    {
        if (bursts.Count == 0)
            return;
        BlasterHitSparks burst = bursts[nextBurst];
        nextBurst = (nextBurst + 1) % bursts.Count;
        burst.Play(point, normal);
    }

    private BlasterBolt NextBolt()
    {
        if (bolts.Count == 0)
            return null;
        BlasterBolt b = bolts[nextBolt];
        nextBolt = (nextBolt + 1) % bolts.Count;   // oldest bolt is recycled if all are in flight
        return b;
    }

    // ------------------------------------------------------------------ thermal support

    /// <summary>
    /// A thermal changer calls this. In thermal mode the soft glow shells and all blaster lights are switched off
    /// so only the clean hot bolt, sparks and flash shapes remain.
    /// </summary>
    public void SetThermalLook(bool on)
    {
        ThermalLook = on;
        for (int i = 0; i < glowRenderers.Count; i++)
            if (glowRenderers[i] != null)
                glowRenderers[i].enabled = !on;
        for (int i = 0; i < boltLights.Count; i++)
            if (boltLights[i] != null)
                boltLights[i].enabled = !on;
        if (muzzleFlash != null)
            muzzleFlash.SetThermalLook(on);
        for (int i = 0; i < bursts.Count; i++)
            if (bursts[i] != null)
                bursts[i].SetThermalLook(on);
    }

    public bool IsBoltRenderer(Renderer r)
    {
        return r != null && boltRoot != null && r.transform.IsChildOf(boltRoot);
    }

    public bool IsSparkRenderer(Renderer r)
    {
        return r != null && sparkRoot != null && r.transform.IsChildOf(sparkRoot);
    }

    // ------------------------------------------------------------------ pools

    private void BuildPools()
    {
        EnsureMaterials();

        boltRoot = new GameObject("Blaster Bolts").transform;
        sparkRoot = new GameObject("Blaster Sparks").transform;

        for (int i = 0; i < Mathf.Max(1, boltPoolSize); i++)
            bolts.Add(CreateBolt(i));

        for (int i = 0; i < Mathf.Max(1, sparkBurstPoolSize); i++)
        {
            GameObject g = new GameObject("Spark Burst " + i);
            g.transform.SetParent(sparkRoot, false);
            BlasterHitSparks s = g.AddComponent<BlasterHitSparks>();
            s.Build(Mathf.Max(1, sparksPerHit), sparkMaterialGreen, sparkMaterialWhite, impactFlashMaterial,
                    sparkWidth, sparkLength, impactFlashSize,
                    sparkSpeedMin, sparkSpeedMax, sparkLifeMin, sparkLifeMax, sparkGravity,
                    impactLight, boltLightColor, impactLightIntensity, impactLightRange);
            bursts.Add(s);
        }
    }

    private BlasterBolt CreateBolt(int index)
    {
        GameObject root = new GameObject("Bolt " + index);
        root.transform.SetParent(boltRoot, false);
        BlasterBolt bolt = root.AddComponent<BlasterBolt>();
        bolt.Setup(this, boltLength, boltCastRadius);

        MakeCapsule("Core", root.transform, boltRadius, boltLength, boltCoreMaterial, false);
        MakeCapsule("Glow", root.transform, boltRadius * 2.2f, boltLength * 1.15f, boltGlowMaterial, true);
        MakeCapsule("Glow Outer", root.transform, boltRadius * 3.6f, boltLength * 1.4f, boltGlowOuterMaterial, true);

        if (boltLight)
        {
            Light l = root.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = boltLightColor;
            l.intensity = boltLightIntensity;
            l.range = boltLightRange;
            l.shadows = LightShadows.None;
            boltLights.Add(l);
        }

        root.SetActive(false);
        return bolt;
    }

    private void MakeCapsule(string objectName, Transform parent, float radius, float length, Material material, bool isGlow)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = objectName;
        Collider c = go.GetComponent<Collider>();
        if (c != null)
            Destroy(c);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);                       // capsule axis (Y) -> bolt axis (Z)
        go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);   // capsule is 1 wide, 2 tall
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (isGlow)
            glowRenderers.Add(mr);
    }

    // ------------------------------------------------------------------ fallback materials (only if the .mat files are missing)

    private void EnsureMaterials()
    {
        if (boltCoreMaterial == null)
            boltCoreMaterial = FallbackMaterial("Blaster Bolt Core", new Color(0.7f, 2.4f, 0.8f), false);
        if (boltGlowMaterial == null)
            boltGlowMaterial = FallbackMaterial("Blaster Bolt Glow", new Color(0.18f, 1.1f, 0.25f), true);
        if (boltGlowOuterMaterial == null)
            boltGlowOuterMaterial = FallbackMaterial("Blaster Bolt Glow Outer", new Color(0.1f, 0.55f, 0.15f), true);
        if (sparkMaterialGreen == null)
            sparkMaterialGreen = FallbackMaterial("Blaster Spark Green", new Color(0.35f, 4f, 0.6f), false);
        if (sparkMaterialWhite == null)
            sparkMaterialWhite = FallbackMaterial("Blaster Spark White", new Color(2.2f, 4f, 2.4f), false);
        if (impactFlashMaterial == null)
            impactFlashMaterial = FallbackMaterial("Blaster Impact Flash", new Color(0.4f, 2.2f, 0.6f), true);
    }

    private static Material FallbackMaterial(string materialName, Color emission, bool transparent)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
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

    // ------------------------------------------------------------------ test helper

    [ContextMenu("Fire one shot (play mode)")]
    private void ContextFire()
    {
        if (Application.isPlaying)
            Fire();
    }
}
