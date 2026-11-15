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

    [Header("Objects with Custom Temperature")]
    [SerializeField] private List<ThermalObject> thermalObjects = new List<ThermalObject>();

    private bool thermalMode = false;
    private Coroutine postExposureRoutine;

    private readonly Dictionary<Renderer, Material[]> originalMaterials = new();
    private readonly List<Material> thermalInstances = new();

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
        }

        SetPostExposure(normalPostExposure);

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

        foreach (Renderer rend in renderers)
        {
            if (rend == null)
                continue;

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
            }

            rend.sharedMaterials = newMaterials;
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
        }

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

    private float GetTemperature(Renderer rend)
    {
        if (laserPointer != null && IsInside(rend, laserPointer))
            return laserPointerTemperature;

        foreach (var entry in thermalObjects)
        {
            if (entry.target != null && IsInside(rend, entry.target))
                return entry.temperature;
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
        }

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
        if (thermalMode)
            ApplyNormalMode();
    }
}