using UnityEngine;

/// <summary>
/// 3D muzzle flash. Three flame shaped meshes (not sprites) share one material, so the thermal changer can swap the
/// material and the flash keeps its clean flame outline. Same idea as a sprite flash: every shot each plane picks a
/// random flame shape, a random roll and a random size, shows for a split second, then hides.
///
/// All planes sit on the barrel axis and cross each other around it (a cross section of flames), so the whole flash
/// follows the barrel. Turn on "Planes Face Camera" to get the old look back (planes 1 and 2 face the camera).
/// Fire() only changes meshes and transforms, it never touches the materials.
/// </summary>
[DefaultExecutionOrder(1000)]
[AddComponentMenu("Blaster/Blaster Muzzle Flash")]
public class BlasterMuzzleFlash : MonoBehaviour
{
    [Header("Flash Objects")]
    [Tooltip("Parent of the flash planes. Shown for 'Duration' on every shot, hidden otherwise.")]
    [SerializeField] private GameObject flashParent;
    [Tooltip("The mesh planes. Empty = found under Flash Parent.")]
    [SerializeField] private MeshFilter[] flashFilters;
    [Tooltip("Only used if the planes have to be created in code (empty setup).")]
    [SerializeField] private Material flashMaterial;

    [Header("Timing")]
    [SerializeField] private float duration = 0.06f;

    [Header("Cross Section (planes follow the barrel)")]
    [Tooltip("The planes are spread evenly around the barrel axis (3 planes = 60 degrees apart). The whole cross gets a random turn on every shot.")]
    [SerializeField] private bool randomTurnPerShot = true;

    [Header("Random Size")]
    [SerializeField] private float minScale = 0.85f;
    [SerializeField] private float maxScale = 1.2f;
    [Tooltip("Multiplies the size of all planes. Raise it if the flash looks small from behind the gun.")]
    [SerializeField] private float sizeMultiplier = 1.4f;

    [Header("Facing the camera (old look, off by default)")]
    [Tooltip("ON: planes 1 and 2 turn to face the camera, the last plane stays on the barrel. OFF: all planes cross around the barrel.")]
    [SerializeField] private bool planesFaceCamera = false;
    [SerializeField] private float minRoll = -25f;
    [SerializeField] private float maxRoll = 25f;
    [Tooltip("Camera the flame turns towards. Empty = Camera.main.")]
    [SerializeField] private UnityEngine.Camera faceCamera;

    [Header("Light")]
    [SerializeField] private bool useLight = true;
    [SerializeField] private Color lightColor = new Color(0.3f, 1f, 0.35f, 1f);
    [SerializeField] private float lightIntensity = 4f;
    [SerializeField] private float lightRange = 7f;
    [Tooltip("Moves the flash light up or down (local Y of this object). 0 = on the barrel axis.")]
    [SerializeField] private float lightYOffset = 0f;

    private Vector3[] baseScales;
    private float[] rolls;
    private float timer;
    private bool thermalLook;
    private Light flashLight;

    public bool IsFlashing { get { return timer > 0f; } }

    private void Awake()
    {
        if (flashParent == null)
            BuildDefaultPlanes();

        if (flashFilters == null || flashFilters.Length == 0)
            flashFilters = flashParent.GetComponentsInChildren<MeshFilter>(true);

        baseScales = new Vector3[flashFilters.Length];
        rolls = new float[flashFilters.Length];
        for (int i = 0; i < flashFilters.Length; i++)
        {
            if (flashFilters[i] == null)
                continue;
            baseScales[i] = flashFilters[i].transform.localScale * sizeMultiplier;
            flashFilters[i].sharedMesh = BlasterFlashShapes.Get(i);
        }

        if (useLight)
        {
            GameObject go = new GameObject("Flash Light");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, lightYOffset, 0.1f);
            flashLight = go.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = lightColor;
            flashLight.intensity = lightIntensity;
            flashLight.range = lightRange;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;
        }

        flashParent.SetActive(false);
    }

    // used when the component sits on an empty object
    private void BuildDefaultPlanes()
    {
        flashParent = new GameObject("Flash Parent");
        flashParent.transform.SetParent(transform, false);
        flashFilters = new MeshFilter[3];
        for (int i = 0; i < 3; i++)
        {
            GameObject p = new GameObject("Flash " + (i + 1));
            p.transform.SetParent(flashParent.transform, false);
            flashFilters[i] = p.AddComponent<MeshFilter>();
            MeshRenderer mr = p.AddComponent<MeshRenderer>();
            mr.sharedMaterial = flashMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }

    /// <summary>Show one flash. Called by BlasterWeapon for every shot.</summary>
    public void Fire()
    {
        if (flashParent == null || flashFilters == null)
            return;

        int last = flashFilters.Length - 1;
        float spacing = 180f / Mathf.Max(1, flashFilters.Length);      // planes are double sided, 180 degrees is a full turn
        float turn = randomTurnPerShot ? Random.Range(0f, 180f) : 0f;

        for (int i = 0; i < flashFilters.Length; i++)
        {
            MeshFilter f = flashFilters[i];
            if (f == null)
                continue;

            f.sharedMesh = BlasterFlashShapes.Get(Random.Range(0, BlasterFlashShapes.Count));
            f.transform.localScale = baseScales[i] * Random.Range(minScale, maxScale);

            if (planesFaceCamera)
                rolls[i] = (i == last) ? Random.Range(0f, 360f) : Random.Range(minRoll, maxRoll);
            else
                rolls[i] = turn + i * spacing;
        }

        timer = duration;
        flashParent.SetActive(true);
        ApplyOrientation();

        if (flashLight != null)
        {
            flashLight.transform.localPosition = new Vector3(0f, lightYOffset, 0.1f);
            flashLight.enabled = !thermalLook;
        }
    }

    /// <summary>BlasterWeapon.SetThermalLook calls this. The flash light is off in thermal mode.</summary>
    public void SetThermalLook(bool on)
    {
        thermalLook = on;
        if (on && flashLight != null)
            flashLight.enabled = false;
    }

    private void Update()
    {
        if (timer <= 0f)
            return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
            Hide();
    }

    private void LateUpdate()
    {
        // the cross follows the barrel by itself (children of the muzzle), only camera facing planes need updating
        if (timer > 0f && planesFaceCamera)
            ApplyOrientation();
    }

    private void Hide()
    {
        timer = 0f;
        if (flashParent != null)
            flashParent.SetActive(false);
        if (flashLight != null)
            flashLight.enabled = false;
    }

    private void ApplyOrientation()
    {
        UnityEngine.Camera cam = faceCamera != null ? faceCamera : UnityEngine.Camera.main;
        Transform ct = cam != null ? cam.transform : null;

        int last = flashFilters.Length - 1;
        for (int i = 0; i < flashFilters.Length; i++)
        {
            MeshFilter f = flashFilters[i];
            if (f == null)
                continue;

            bool onBarrel = !planesFaceCamera || i == last || ct == null;
            if (onBarrel)
            {
                // plane that contains the barrel axis, rolled around it
                f.transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, rolls[i]);
            }
            else
            {
                // plane turned to the camera, flame pointing the way the barrel points on screen
                Vector3 camForward = ct.forward;
                Vector3 dir = Vector3.ProjectOnPlane(transform.forward, camForward);
                if (dir.sqrMagnitude < 0.0001f)
                    dir = ct.up;
                dir.Normalize();
                dir = Quaternion.AngleAxis(rolls[i], camForward) * dir;
                f.transform.rotation = Quaternion.LookRotation(dir, -camForward);
            }
        }
    }
}