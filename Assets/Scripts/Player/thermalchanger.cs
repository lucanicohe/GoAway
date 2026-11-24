using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ThermalChanger : MonoBehaviour
{
    [System.Serializable]
    public class ThermalObject
    {
        public GameObject target;
        public float temperature = 32f;
    }

    [Header("Thermal Material")]
    [SerializeField] private Material thermalMaterial;

    [Header("Random Temperature (for objects not in the list)")]
    [SerializeField] private float randomTemperatureAmount = 0.6f;

    [Header("Thermal Mode Effects")]
    [Tooltip("Disabled while thermal mode is on, re-enabled in normal mode.")]
    [SerializeField] private GameObject fog;

    [Tooltip("Volume containing the Bloom / Vignette / Lens Flare / Film Grain / Color Adjustments overrides.")]
    [SerializeField] private Volume volume;

    [Header("Bloom")]
    [SerializeField] private float normalBloomIntensity = 3f;
    [SerializeField] private float thermalBloomIntensity = 33f;

    [Header("Lens Flare")]
    [SerializeField] private float normalLensFlareIntensity = 0.6f;
    [SerializeField] private float thermalLensFlareIntensity = 0f;

    [Header("Vignette")]
    [SerializeField] private float normalVignetteIntensity = 0f;
    [SerializeField] private float thermalVignetteIntensity = 0.4f;

    [Header("Film Grain")]
    [SerializeField] private float normalFilmGrainIntensity = 0f;
    [SerializeField] private float thermalFilmGrainIntensity = 1f;

    [Header("Post Exposure")]
    [SerializeField] private bool flashPostExposureOnThermal = true;
    [SerializeField] private bool flashPostExposureOnNormal = false;
    [SerializeField] private float postExposurePeakThermal = 1f;
    [SerializeField] private float postExposurePeakNormal = 1f;
    [SerializeField] private float normalPostExposure = 0.3f;
    [SerializeField] private float thermalPostExposure = 0.6f;
    [SerializeField] private float postExposureFadeDuration = 0.5f;
    [Tooltip("X = time (0 → 1), Y = how far it has faded (0 = still at peak, 1 = fully at target)")]
    [SerializeField] private AnimationCurve postExposureCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Laser Pointer")]
    [SerializeField] private GameObject laserPointer;
    [SerializeField] private float laserPointerTemperature = 32f;

    [Header("Red Dot")]
    [SerializeField] private GameObject redDot;

    [Header("Visor HUD")]
    [SerializeField] private VisorHud visorHud;

    [Header("Blaster: Muzzle Flash")]
    [Tooltip("Drag in the MuzzleFlash object of the blaster (the one that has BlasterMuzzleFlash on it).")]
    [SerializeField] private GameObject muzzleFlash;
    [Tooltip("Temperature of the flash in thermal mode.")]
    [SerializeField] private float muzzleFlashTemperature = 100f;

    [Header("Blaster: Barrel + Gun Body Heat")]
    [Tooltip("Drag in the barrel of the gun.")]
    [SerializeField] private GameObject barrel;
    [Tooltip("Drag in the main gun body / receiver.")]
    [SerializeField] private GameObject gunBody;

    [Tooltip("Barrel temperature when it is cold.")]
    [SerializeField] private float barrelMinTemperature = 1f;
    [Tooltip("Barrel temperature when it is fully heated.")]
    [SerializeField] private float barrelMaxTemperature = 180f;

    [Tooltip("Gun body temperature when it is cold.")]
    [SerializeField] private float gunBodyMinTemperature = 1f;
    [Tooltip("Gun body temperature when it is fully heated.")]
    [SerializeField] private float gunBodyMaxTemperature = 55f;

    [Tooltip("How much heat the barrel gets per shot")]
    [SerializeField] private float barrelHeatPerShot = 6f;

    [Tooltip("How much heat the gun body gets per shot (much lower = heats slower)")]
    [SerializeField] private float gunBodyHeatPerShot = 1.2f;

    [Tooltip("Seconds needed to cool down from max to min.")]
    [SerializeField] private float cooldownSeconds = 45f;
    [Tooltip("Seconds after the last shot before cooling starts.")]
    [SerializeField] private float cooldownDelay = 0.5f;

    [Tooltip("Live barrel temperature (watch in play mode).")]
    [SerializeField] private float currentBarrelTemperature = 0f;
    [Tooltip("Live gun body temperature (watch in play mode).")]
    [SerializeField] private float currentGunBodyTemperature = 0f;

    [Header("Blaster: Bolts and Sparks")]
    [Tooltip("Found automatically if empty.")]
    [SerializeField] private BlasterWeapon blaster;
    [SerializeField] private float boltTemperature = 100f;
    [SerializeField] private float sparkTemperature = 80f;

    [Header("Flathead Robots (spawned by the GameManager)")]
    [Tooltip("Temperature of the Flathead's legs and body in thermal mode. Applies to every Flathead the GameManager spawns.")]
    [SerializeField] private float flatheadTemperature = 50f;
    [Tooltip("Temperature of the Flathead's turret + gun. Set it equal to the body temperature if you don't want a hotter turret.")]
    [SerializeField] private float flatheadTurretTemperature = 80f;
    [Tooltip("While thermal mode is on, how often (seconds) to look for Flatheads that spawned after it was switched on.")]
    [SerializeField] private float flatheadScanInterval = 0.5f;

    [Header("Objects with Custom Temperature")]
    [SerializeField] private List<ThermalObject> thermalObjects = new List<ThermalObject>();

    private bool thermalMode = false;
    private float nextFlatheadScanTime;
    private Coroutine postExposureRoutine;

    private readonly Dictionary<Renderer, Material[]> originalMaterials = new();
    private readonly List<Material> thermalInstances = new();

    // blaster heat
    private readonly List<Material> barrelInstances = new();
    private readonly List<Material> gunBodyInstances = new();
    private BlasterWeapon hookedBlaster;
    private float lastShotTime = -999f;
    private float nextFindTime;

    private static readonly int TempProperty = Shader.PropertyToID("_temp");

    private void Start()
    {
        thermalMode = false;

        if (laserPointer != null)
            laserPointer.SetActive(false);

        if (redDot != null)
            redDot.SetActive(true);

        if (visorHud != null)
        {
            visorHud.showSideArcs = false;
            visorHud.showCrosshair = true;
            visorHud.showCompass = true;
            visorHud.showBars = true;      // health + ammo bars visible in normal mode
        }

        SetPostExposure(normalPostExposure);

        currentBarrelTemperature = barrelMinTemperature;
        currentGunBodyTemperature = gunBodyMinTemperature;
        FindBlaster();

        Debug.Log("NORMAL MODE");
    }

    private void Update()
    {
        if (Keyboard.current != null &&
            (Keyboard.current.digit1Key.wasPressedThisFrame ||
             Keyboard.current.numpad1Key.wasPressedThisFrame))
        {
            if (thermalMode)
                ApplyNormalMode();
            else
                ApplyThermalMode();
        }

        if (blaster == null && Time.time >= nextFindTime)
        {
            nextFindTime = Time.time + 1f;
            FindBlaster();
        }

        UpdateBarrelHeat();

        // Flatheads that spawned while thermal mode was already on (e.g. "Respawn Flatheads") get the thermal look too
        if (thermalMode && Time.time >= nextFlatheadScanTime)
        {
            nextFlatheadScanTime = Time.time + Mathf.Max(0.1f, flatheadScanInterval);
            ApplyThermalToNewFlatheads();
        }
    }

    // ---------------------------------------------------------------- Flatheads spawned after thermal mode was switched on

    private FlatheadEnemy[] cachedFlatheads;   // reused buffer for the periodic Flathead scan

    private void ApplyThermalToNewFlatheads()
    {
        // Reuse the array reference when the count hasn't changed; still allocates when new
        // Flatheads spawn, but avoids the per-interval alloc during steady state.
        cachedFlatheads = FindObjectsByType<FlatheadEnemy>(FindObjectsSortMode.None);

        foreach (FlatheadEnemy robot in cachedFlatheads)
        {
            if (robot == null)
                continue;

            foreach (Renderer rend in robot.GetComponentsInChildren<Renderer>(true))
            {
                if (rend != null && !originalMaterials.ContainsKey(rend))
                    ApplyThermalToRenderer(rend);
            }
        }
    }

    // ---------------------------------------------------------------- Blaster: barrel + gun body heat

    private void FindBlaster()
    {
        if (blaster == null)
            blaster = FindFirstObjectByType<BlasterWeapon>();

        if (blaster != null && hookedBlaster != blaster)
        {
            if (hookedBlaster != null)
                hookedBlaster.Fired -= OnBlasterShot;

            hookedBlaster = blaster;
            hookedBlaster.Fired += OnBlasterShot;

            if (thermalMode)
                hookedBlaster.SetThermalLook(true);
        }
    }

    private void OnBlasterShot(BlasterWeapon weapon)
    {
        // Barrel heats fast
        currentBarrelTemperature = Mathf.Min(barrelMaxTemperature,
            currentBarrelTemperature + barrelHeatPerShot);

        // Gun body heats much slower
        currentGunBodyTemperature = Mathf.Min(gunBodyMaxTemperature,
            currentGunBodyTemperature + gunBodyHeatPerShot);

        lastShotTime = Time.time;
    }

    private void UpdateBarrelHeat()
    {
        if (Time.time - lastShotTime > cooldownDelay)
        {
            // Barrel cool down
            if (currentBarrelTemperature > barrelMinTemperature)
            {
                float range = Mathf.Max(0.0001f, barrelMaxTemperature - barrelMinTemperature);
                float perSecond = range / Mathf.Max(0.01f, cooldownSeconds);
                currentBarrelTemperature = Mathf.Max(barrelMinTemperature, currentBarrelTemperature - perSecond * Time.deltaTime);
            }

            // Gun Body cool down
            if (currentGunBodyTemperature > gunBodyMinTemperature)
            {
                float range = Mathf.Max(0.0001f, gunBodyMaxTemperature - gunBodyMinTemperature);
                float perSecond = range / Mathf.Max(0.01f, cooldownSeconds);
                currentGunBodyTemperature = Mathf.Max(gunBodyMinTemperature, currentGunBodyTemperature - perSecond * Time.deltaTime);
            }
        }

        // Live update while thermal mode is on — use index loop to avoid List enumerator allocation.
        if (thermalMode)
        {
            for (int i = 0; i < barrelInstances.Count; i++)
            {
                Material instance = barrelInstances[i];
                if (instance != null)
                    instance.SetFloat(TempProperty, currentBarrelTemperature);
            }

            for (int i = 0; i < gunBodyInstances.Count; i++)
            {
                Material instance = gunBodyInstances[i];
                if (instance != null)
                    instance.SetFloat(TempProperty, currentGunBodyTemperature);
            }
        }
    }

    private void ApplyThermalMode()
    {
        if (thermalMaterial == null)
        {
            Debug.LogError("Thermal Material is not assigned!");
            return;
        }

        thermalMode = true;

        Renderer[] renderers = FindObjectsByType<Renderer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        originalMaterials.Clear();
        thermalInstances.Clear();
        barrelInstances.Clear();
        gunBodyInstances.Clear();

        foreach (Renderer rend in renderers)
        {
            if (rend == null)
                continue;

            ApplyThermalToRenderer(rend);
        }

        // Thermal mode effects
        if (fog != null)
            fog.SetActive(false);

        if (laserPointer != null)
            laserPointer.SetActive(true);

        if (redDot != null)
            redDot.SetActive(false);

        if (visorHud != null)
        {
            visorHud.showSideArcs = true;
            visorHud.showCrosshair = false;
            visorHud.showCompass = false;
            visorHud.showBars = false;     // health + ammo bars ticked off in thermal mode
        }

        if (blaster != null)
            blaster.SetThermalLook(true);

        SetBloomIntensity(thermalBloomIntensity);
        SetLensFlareIntensity(thermalLensFlareIntensity);
        SetVignetteIntensity(thermalVignetteIntensity);
        SetFilmGrainIntensity(thermalFilmGrainIntensity);

        if (flashPostExposureOnThermal)
            StartPostExposureTransition(postExposurePeakThermal, thermalPostExposure);
        else
            SetPostExposure(thermalPostExposure);

        Debug.Log($"THERMAL MODE: {originalMaterials.Count} renderers updated.");
    }

    private void ApplyThermalToRenderer(Renderer rend)
    {
        Material[] original = rend.sharedMaterials;
        originalMaterials[rend] = original;

        float temperature = GetTemperature(rend);

        Material[] newMaterials = new Material[original.Length];

        for (int i = 0; i < original.Length; i++)
        {
            Material instance = new Material(thermalMaterial);

            if (instance.HasProperty(TempProperty))
            {
                instance.SetFloat(TempProperty, temperature);
            }
            else
            {
                Debug.LogError(
                    $"Thermal material missing '_temp' property: {rend.name}"
                );
            }

            newMaterials[i] = instance;
            thermalInstances.Add(instance);

            if (barrel != null && IsInside(rend, barrel))
                barrelInstances.Add(instance);

            if (gunBody != null && IsInside(rend, gunBody))
                gunBodyInstances.Add(instance);
        }

        rend.sharedMaterials = newMaterials;
    }

    private float GetTemperature(Renderer rend)
    {
        if (laserPointer != null && IsInside(rend, laserPointer))
            return laserPointerTemperature;

        // blaster parts
        if (barrel != null && IsInside(rend, barrel))
            return currentBarrelTemperature;

        if (gunBody != null && IsInside(rend, gunBody))
            return currentGunBodyTemperature;

        if (muzzleFlash != null && IsInside(rend, muzzleFlash))
            return muzzleFlashTemperature;

        if (blaster != null && blaster.IsBoltRenderer(rend))
            return boltTemperature;

        if (blaster != null && blaster.IsSparkRenderer(rend))
            return sparkTemperature;

        foreach (var entry in thermalObjects)
        {
            if (entry.target != null && IsInside(rend, entry.target))
                return entry.temperature;
        }

        // Flathead robots (spawned at runtime by the GameManager, so they can't be dragged into the list above)
        FlatheadEnemy flathead = rend.GetComponentInParent<FlatheadEnemy>();
        if (flathead != null)
        {
            Transform turret = flathead.Ctrl != null ? flathead.Ctrl.TurretBone : null;

            if (turret != null && (rend.transform == turret || rend.transform.IsChildOf(turret)))
                return flatheadTurretTemperature;

            return flatheadTemperature;
        }

        return Random.Range(0f, randomTemperatureAmount);
    }

    private bool IsInside(Renderer rend, GameObject target)
    {
        return target != null &&
               (rend.transform == target.transform ||
                rend.transform.IsChildOf(target.transform));
    }

    private void SetBloomIntensity(float value)
    {
        if (volume == null)
            return;

        if (volume.profile != null && volume.profile.TryGet(out Bloom bloom))
        {
            bloom.intensity.overrideState = true;
            bloom.intensity.value = value;
        }
        else
        {
            Debug.LogWarning("ThermalChanger: no Bloom override found on the assigned Volume's profile.");
        }
    }

    private void SetLensFlareIntensity(float value)
    {
        if (volume == null)
            return;

        if (volume.profile != null && volume.profile.TryGet(out ScreenSpaceLensFlare flare))
        {
            flare.intensity.overrideState = true;
            flare.intensity.value = value;
        }
        else
        {
            Debug.LogWarning("ThermalChanger: no Screen Space Lens Flare override found on the assigned Volume's profile.");
        }
    }

    private void SetVignetteIntensity(float value)
    {
        if (volume == null)
            return;

        if (volume.profile != null && volume.profile.TryGet(out Vignette vignette))
        {
            vignette.intensity.overrideState = true;
            vignette.intensity.value = value;
        }
        else
        {
            Debug.LogWarning("ThermalChanger: no Vignette override found on the assigned Volume's profile.");
        }
    }

    private void SetFilmGrainIntensity(float value)
    {
        if (volume == null)
            return;

        if (volume.profile != null && volume.profile.TryGet(out FilmGrain filmGrain))
        {
            filmGrain.intensity.overrideState = true;
            filmGrain.intensity.value = value;
        }
        else
        {
            Debug.LogWarning("ThermalChanger: no Film Grain override found on the assigned Volume's profile.");
        }
    }

    private void SetPostExposure(float value)
    {
        if (volume == null)
            return;

        if (volume.profile != null && volume.profile.TryGet(out ColorAdjustments colorAdj))
        {
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = value;
        }
        else
        {
            Debug.LogWarning("ThermalChanger: no Color Adjustments override found on the assigned Volume's profile.");
        }
    }

    private void StartPostExposureTransition(float peak, float targetValue)
    {
        if (postExposureRoutine != null)
            StopCoroutine(postExposureRoutine);

        postExposureRoutine = StartCoroutine(PostExposureFlashAndFade(peak, targetValue));
    }

    private IEnumerator PostExposureFlashAndFade(float peak, float targetValue)
    {
        // Instant peak
        SetPostExposure(peak);

        float duration = Mathf.Max(0.01f, postExposureFadeDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Use the curve (0 = still at peak, 1 = fully at target)
            float curveT = postExposureCurve.Evaluate(t);
            float current = Mathf.Lerp(peak, targetValue, curveT);

            SetPostExposure(current);
            yield return null;
        }

        SetPostExposure(targetValue);
        postExposureRoutine = null;
    }

    private void ApplyNormalMode()
    {
        thermalMode = false;

        foreach (var entry in originalMaterials)
        {
            if (entry.Key != null)
                entry.Key.sharedMaterials = entry.Value;
        }

        foreach (Material instance in thermalInstances)
        {
            if (instance != null)
                Destroy(instance);
        }

        originalMaterials.Clear();
        thermalInstances.Clear();
        barrelInstances.Clear();
        gunBodyInstances.Clear();

        // Normal mode effects
        if (fog != null)
            fog.SetActive(true);

        if (laserPointer != null)
            laserPointer.SetActive(false);

        if (redDot != null)
            redDot.SetActive(true);

        if (visorHud != null)
        {
            visorHud.showSideArcs = false;
            visorHud.showCrosshair = true;
            visorHud.showCompass = true;
            visorHud.showBars = true;      // health + ammo bars visible in normal mode
        }

        if (blaster != null)
            blaster.SetThermalLook(false);

        SetBloomIntensity(normalBloomIntensity);
        SetLensFlareIntensity(normalLensFlareIntensity);
        SetVignetteIntensity(normalVignetteIntensity);
        SetFilmGrainIntensity(normalFilmGrainIntensity);

        if (flashPostExposureOnNormal)
            StartPostExposureTransition(postExposurePeakNormal, normalPostExposure);
        else
        {
            if (postExposureRoutine != null)
            {
                StopCoroutine(postExposureRoutine);
                postExposureRoutine = null;
            }
            SetPostExposure(normalPostExposure);
        }

        Debug.Log("NORMAL MODE: Original materials restored.");
    }

    private void OnDestroy()
    {
        if (hookedBlaster != null)
            hookedBlaster.Fired -= OnBlasterShot;

        if (thermalMode)
            ApplyNormalMode();
    }
}